using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Numerotation;
using RigV3.Serveur.Numerotation.Compteur;

namespace RigV3.Serveur.Numerotation.Series;

/// <summary>Un numero de liaison ou de lettre recommandee donne par la V3, fini comme la V2 le finit.</summary>
/// <param name="Brut">La reponse du compteur (num.Attribuer) : code, serie, numero brut (« G3802601784* »).</param>
/// <param name="Numero">Le numero tel que la V2 l'ecrit dans ses fiches (cle posee, format national) ;
/// null si le compteur a refuse ou si la cle ne se calcule pas.</param>
public sealed record NumeroFini(NumeroAttribue Brut, string? Numero, string? Message);

/// <summary>
/// Les deux series que la DLL de la V2 retouche apres GENERER_NUMERO (NUM-017, NUM-019) : la V3 les
/// demande au meme compteur (<see cref="DistributeurNumeros"/>, dans la transaction de l'acte) avec les
/// memes parametres que la V2, puis les finit avec <see cref="FinitionV2"/>.
/// </summary>
public static class NumerosSpeciaux
{
    /// <summary>
    /// Le numero de liaison d'un passage au CFE (serie LIAI, PassageCfe.cs:161). La V2 passe a
    /// GENERER_NUMERO le code INSEE du greffe ; la V3 prend le code du compteur LIAI et exige qu'il soit
    /// un code de liaison du greffe (num.CodeLiaison, repris de GREFFE_NUMERO_LIAISON) : jamais le
    /// « 9999 » par defaut de GENERER_NUMERO (NUM-019 CA02).
    /// </summary>
    public static async Task<NumeroFini> LiaisonAsync(DistributeurNumeros numeros, SqlConnection cn, SqlTransaction? tx,
                                                       string? compte, string? poste, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand(
            "SELECT TOP (1) c.CodeGreffe FROM num.Compteur AS c " +
            "WHERE c.Type = 'LIAI' AND EXISTS (SELECT 1 FROM num.CodeLiaison AS l " +
            "                                  WHERE l.Code = ISNULL(c.Lettre, '') + ISNULL(c.CodeGreffe, '')) " +
            "ORDER BY c.Annee DESC, c.Jour DESC;", cn, tx);
        var code = await cmd.ExecuteScalarAsync(ct) as string;
        if (string.IsNullOrEmpty(code))
            return new NumeroFini(new NumeroAttribue(CodesNumerotation.TypeInconnu, "LIAI", null, 0, 0, null, false, null), null,
                "Numero de liaison : le compteur LIAI ne porte aucun code de liaison du greffe (num.CodeLiaison) ; rien n'a ete donne.");
        var brut = await numeros.AttribuerAsync(cn, tx, new DemandeNumero("LIAI", CodeGreffe: code), compte, poste, ct);
        if (brut.Code != CodesNumerotation.Ok || brut.Numero is null) return new NumeroFini(brut, null, brut.Message);
        var fini = FinitionV2.AvecCleLiaison(brut.Numero);
        return fini.Contains('*') || fini == brut.Numero
            ? new NumeroFini(brut, null, $"Numero de liaison {brut.Numero} : la cle ne se calcule pas.")
            : new NumeroFini(brut, fini, null);
    }

    /// <summary>
    /// Le numero d'une lettre recommandee (avec ou sans avis de reception), comme GenererLR
    /// (Numerotation.cs:124-145) : une seule file par serie, l'annee fixee a 2000, puis la cle et le
    /// format national « CP NNN NNN NNNN C ».
    /// </summary>
    public static async Task<NumeroFini> LettreRecommandeeAsync(DistributeurNumeros numeros, SqlConnection cn, SqlTransaction? tx,
                                                                bool avecAvisReception, string? compte, string? poste,
                                                                CancellationToken ct = default)
    {
        var brut = await numeros.AttribuerAsync(cn, tx, new DemandeNumero(avecAvisReception ? "LRARN" : "LRN", AnneeLettresRecommandees),
                                                compte, poste, ct);
        if (brut.Code != CodesNumerotation.Ok || brut.Numero is null) return new NumeroFini(brut, null, brut.Message);
        var fini = FinitionV2.LettreRecommandee(brut.Numero);
        return fini == ""
            ? new NumeroFini(brut, null, $"Lettre recommandee {brut.Numero} : la cle ne se calcule pas (Numerotation.cs:139-141).")
            : new NumeroFini(brut, fini, null);
    }

    /// <summary>L'annee de la file unique des lettres recommandees (Numerotation.cs:126).</summary>
    public const int AnneeLettresRecommandees = 2000;

    /// <summary>
    /// Le numero suivant d'une fiche hors du compteur commun (NUM-021 CA01 : l'ordre des mentions d'un
    /// dossier, fiche « MENTION_OBSERVATION », cle = identifiant du dossier) : num.AttribuerOrdre.
    /// <paramref name="premier"/> : l'ordre d'une fiche qui n'en a aucun (2 pour « ETABLISSEMENT », comme la V2).
    /// </summary>
    public static async Task<int> OrdreAsync(SqlConnection cn, SqlTransaction? tx, string fiche, int cle, int premier = 1,
                                             CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand("num.AttribuerOrdre", cn, tx) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@Fiche", SqlDbType.VarChar, 40).Value = fiche;
        cmd.Parameters.Add("@Cle", SqlDbType.Int).Value = cle;
        cmd.Parameters.Add("@Premier", SqlDbType.Int).Value = premier;
        var ordre = cmd.Parameters.Add("@Ordre", SqlDbType.Int);
        ordre.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);
        return (int)ordre.Value;
    }
}
