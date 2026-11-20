using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Numerotation;

namespace RigV3.Serveur.Numerotation.Compteur;

/// <summary>
/// Le compteur occupe : une autre transaction (V2 ou V3) le tient au-dela du delai d'attente.
/// Rien n'a ete consomme ; la demande peut etre refaite. Erreur SQL 51001 de num.Attribuer / num.Restituer.
/// </summary>
public sealed class CompteurOccupeException(string message, SqlException cause) : Exception(message, cause);

/// <summary>
/// LE SEUL CHEMIN de la partie serveur pour donner ou rendre un numero (NUM-001 T04) : il appelle
/// les programmes de la base V3, num.Attribuer et num.Restituer -- les memes que la V2 appellera
/// (lot 7). Toute la regle (series, redirection, numeros rendus, format, verrous) vit dans ces
/// programmes : ce service ne fait que les appeler et traduire leurs reponses.
///
/// Dans un acte qui numerote (une inscription, une decision...), le module de l'acte le recoit en
/// parametre de sa route et l'appelle DANS SA TRANSACTION : si l'acte est annule, le numero n'est pas
/// consomme (NUM-001 CA03), et un numero rendu par une annulation l'est avec elle (NUM-004 T03).
/// <code>
/// routes.MapPost("/rcs/inscriptions", async (BaseRig b, DistributeurNumeros num, ...) =>
/// {
///     await using var cn = await b.OuvrirAsync(ct);
///     await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
///     var n = await num.AttribuerAsync(cn, tx, new DemandeNumero("RC1"), compte, poste, ct);
///     if (n.Code != CodesNumerotation.Ok) return Results.UnprocessableEntity(n);
///     // ... INSERT de l'acte avec n.Numero ...
///     await tx.CommitAsync(ct);
/// });
/// </code>
/// Mode d'emploi : docs/reconstruction/numerotation-compteur.md.
/// </summary>
public sealed class DistributeurNumeros(int delaiVerrouMs = DistributeurNumeros.DelaiParDefautMs)
{
    /// <summary>Le reglage du delai d'attente d'un compteur tenu par une autre transaction (millisecondes).</summary>
    public const string Reglage = "Numerotation:DelaiVerrouMs";

    /// <summary>10 s : sous le delai d'une commande SqlClient (30 s), pour que l'agent lise
    /// « compteur occupe » plutot qu'un delai depasse anonyme.</summary>
    public const int DelaiParDefautMs = 10_000;

    /// <summary>L'erreur SQL levee par les programmes quand le compteur reste tenu au-dela du delai.</summary>
    public const int ErreurCompteurOccupe = 51001;

    /// <summary>Le delai d'attente d'un compteur tenu (millisecondes ; -1 = sans limite).</summary>
    public int DelaiVerrouMs { get; } = delaiVerrouMs;

    /// <summary>Le numero suivant de la serie <see cref="DemandeNumero.Type"/>, pour la V3.</summary>
    /// <param name="cn">Une connexion ouverte a la base V3 du greffe.</param>
    /// <param name="tx">La transaction de l'acte ; null = le programme ouvre la sienne.</param>
    /// <param name="compte">Le compte de l'agent (journal) ; null = le compte SQL de la connexion.</param>
    /// <param name="poste">Le poste de l'agent (journal) ; null = celui de la connexion.</param>
    /// <exception cref="CompteurOccupeException">Le compteur est tenu au-dela du delai.</exception>
    public async Task<NumeroAttribue> AttribuerAsync(SqlConnection cn, SqlTransaction? tx, DemandeNumero demande,
                                                     string? compte, string? poste, CancellationToken ct = default)
    {
        await using var cmd = Programme(cn, tx, "num.Attribuer");
        cmd.Parameters.Add("@Type", SqlDbType.VarChar, 5).Value = demande.Type;
        cmd.Parameters.Add("@Annee", SqlDbType.SmallInt).Value = demande.Annee;
        cmd.Parameters.Add("@Jour", SqlDbType.SmallInt).Value = demande.Jour;
        cmd.Parameters.Add("@CodeGreffe", SqlDbType.VarChar, 5).Value = (object?)demande.CodeGreffe ?? DBNull.Value;
        Demandeur(cmd, compte, poste);
        var numero = Sortie(cmd, "@Numero", SqlDbType.VarChar, 40);
        var serie = Sortie(cmd, "@Serie", SqlDbType.VarChar, 5);
        var chrono = Sortie(cmd, "@Chrono", SqlDbType.Int);
        var reattribue = Sortie(cmd, "@Reattribue", SqlDbType.Bit);
        var message = Sortie(cmd, "@Message", SqlDbType.NVarChar, 400);
        var code = await Executer(cmd, ct);
        return new NumeroAttribue(code, Texte(serie), Texte(numero), demande.Annee, demande.Jour,
                                  chrono.Value is int c ? c : null, reattribue.Value is true, Texte(message));
    }

    /// <summary>Rendre un numero donne par erreur (NUM-004), pour la V3.</summary>
    /// <exception cref="CompteurOccupeException">Le compteur est tenu au-dela du delai.</exception>
    public async Task<ResultatRestitution> RestituerAsync(SqlConnection cn, SqlTransaction? tx, DemandeRestitution demande,
                                                          string? compte, string? poste, CancellationToken ct = default)
    {
        await using var cmd = Programme(cn, tx, "num.Restituer");
        cmd.Parameters.Add("@Type", SqlDbType.VarChar, 5).Value = demande.Type;
        cmd.Parameters.Add("@Numero", SqlDbType.VarChar, 40).Value = demande.Numero;
        cmd.Parameters.Add("@Annee", SqlDbType.SmallInt).Value = demande.Annee;
        cmd.Parameters.Add("@Explication", SqlDbType.NVarChar, -1).Value = (object?)demande.Explication ?? DBNull.Value;
        Demandeur(cmd, compte, poste);
        var serie = Sortie(cmd, "@Serie", SqlDbType.VarChar, 5);
        var issue = Sortie(cmd, "@Issue", SqlDbType.VarChar, 20);
        var message = Sortie(cmd, "@Message", SqlDbType.NVarChar, 400);
        var code = await Executer(cmd, ct);
        return new ResultatRestitution(code, Texte(issue), Texte(serie), Texte(message));
    }

    /// <summary>Les compteurs de la base (tous, ou ceux d'un type), dans l'ordre type, annee, jour.</summary>
    public static async Task<IReadOnlyList<CompteurNumeros>> ListerAsync(SqlConnection cn, string? type, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand(
            "SELECT c.Type, c.Annee, c.Jour, c.Libelle, c.Structure, c.CodageAnnee, c.Lettre, c.Separateur, c.TailleChrono, " +
            "       c.CodeGreffe, c.Debut, c.Chrono, c.GestionRendus, c.Redirection, c.TableCible, c.ColonneCible, " +
            "       c.DerniereAttribution, " +
            "       (SELECT COUNT(*) FROM num.NumeroRendu r WHERE r.Serie = c.Type AND r.Annee = c.Annee AND r.Jour = c.Jour) " +
            "FROM num.Compteur c WHERE (@type IS NULL OR c.Type = @type) ORDER BY c.Type, c.Annee, c.Jour;", cn);
        cmd.Parameters.Add("@type", SqlDbType.VarChar, 5).Value = (object?)type ?? DBNull.Value;
        var compteurs = new List<CompteurNumeros>();
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct))
            compteurs.Add(new CompteurNumeros(
                l.GetString(0), l.GetInt16(1), l.GetInt16(2), Nul(l, 3), l.GetString(4), l.GetByte(5), Nul(l, 6), Nul(l, 7),
                l.GetInt16(8), Nul(l, 9), l.GetInt32(10), l.IsDBNull(11) ? null : l.GetInt32(11), l.GetBoolean(12),
                Nul(l, 13), Nul(l, 14), Nul(l, 15), l.IsDBNull(16) ? null : l.GetDateTime(16), l.GetInt32(17)));
        return compteurs;
    }

    private SqlCommand Programme(SqlConnection cn, SqlTransaction? tx, string nom)
    {
        var cmd = new SqlCommand(nom, cn, tx) { CommandType = CommandType.StoredProcedure };
        // L'attente d'un verrou est bornee par le programme (@DelaiVerrouMs) ; la commande, elle,
        // ne doit pas couper avant lui.
        cmd.CommandTimeout = DelaiVerrouMs < 0 ? 0 : DelaiVerrouMs / 1000 + 30;
        cmd.Parameters.Add("@Logiciel", SqlDbType.Char, 2).Value = "V3";
        cmd.Parameters.Add("@DelaiVerrouMs", SqlDbType.Int).Value = DelaiVerrouMs;
        return cmd;
    }

    private static void Demandeur(SqlCommand cmd, string? compte, string? poste)
    {
        cmd.Parameters.Add("@Compte", SqlDbType.NVarChar, 128).Value = (object?)compte ?? DBNull.Value;
        cmd.Parameters.Add("@Poste", SqlDbType.NVarChar, 128).Value = (object?)poste ?? DBNull.Value;
    }

    private static SqlParameter Sortie(SqlCommand cmd, string nom, SqlDbType type, int taille = 0)
    {
        var p = taille > 0 ? cmd.Parameters.Add(nom, type, taille) : cmd.Parameters.Add(nom, type);
        p.Direction = ParameterDirection.Output;
        return p;
    }

    private static async Task<int> Executer(SqlCommand cmd, CancellationToken ct)
    {
        var retour = cmd.Parameters.Add("@RETURN_VALUE", SqlDbType.Int);
        retour.Direction = ParameterDirection.ReturnValue;
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException e) when (e.Number == ErreurCompteurOccupe)
        {
            throw new CompteurOccupeException(e.Message, e);
        }
        return (int)retour.Value;
    }

    private static string? Texte(SqlParameter p) => p.Value is string s ? s : null;

    private static string? Nul(SqlDataReader l, int i) => l.IsDBNull(i) ? null : l.GetString(i);
}
