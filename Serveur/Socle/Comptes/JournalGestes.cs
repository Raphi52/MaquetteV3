using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;

namespace RigV3.Serveur.Socle.Comptes;

/// <summary>
/// LE JOURNAL DES GESTES (SOC-016) et LE JOURNAL DES DROITS (SOC-005), cote serveur.
///
/// Chaque ecriture se fait DANS la transaction du geste : un geste enregistre est journalise, un geste
/// annule ne l'est pas. Les tables sont en ajout seul (declencheurs de SOC-005_journal.sql et
/// SOC-016_gestes.sql) : ce fichier n'a ni modification ni effacement, et aucune route n'en expose.
/// Les domaines (dossier, facture, encaissement...) ecriront ici par <see cref="EcrireAsync"/>, au nom de
/// l'agent (jamais un compte technique, CA02) ; la recopie V2 -> V3 par <see cref="EcrireRecopieAsync"/>,
/// au nom du compte V2 d'origine (CA06).
/// </summary>
public static class JournalGestes
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Les verbes de soc.Journal (contrainte CK_Journal_Verbe), et leur forme lue a l'ecran.</summary>
    public static readonly IReadOnlyDictionary<string, string> Verbes = new Dictionary<string, string>
    {
        ["a cree"] = "a créé",
        ["a modifie"] = "a modifié",
        ["a desactive"] = "a désactivé",
        ["a supprime"] = "a supprimé",
        ["a valide"] = "a validé",
        ["a ouvert"] = "a ouvert",
    };

    /// <summary>Un geste d'un agent de la V3 (SOC-016 CA01, CA02).</summary>
    public static Task EcrireAsync(SqlConnection cn, SqlTransaction tx, Appelant agent, string? ecran,
                                   string famille, string objet, string? idObjet, string verbe, object? avant, object? apres, CancellationToken ct)
        => Inserer(cn, tx, "v3", agent.Id, agent.NomAffiche, null, ecran, famille, objet, idObjet, verbe, avant, apres, ct);

    /// <summary>Une ecriture de la recopie V2 -> V3 : attribuee a la recopie et au compte V2 d'origine (SOC-016 CA06).</summary>
    public static Task EcrireRecopieAsync(SqlConnection cn, SqlTransaction? tx, string compteV2,
                                          string famille, string objet, string? idObjet, string verbe, object? avant, object? apres, CancellationToken ct)
        => Inserer(cn, tx, "recopie-v2", null, null, compteV2, null, famille, objet, idObjet, verbe, avant, apres, ct);

    /// <summary>La ligne qui ouvre le journal de la V3 au passage (SOC-016 CA05).</summary>
    public static Task EcrireOuvertureAsync(SqlConnection cn, SqlTransaction tx, CancellationToken ct)
        => Inserer(cn, tx, "passage", null, null, null, null, "journal", "le journal de la V3", null, "a ouvert", null,
            new { message = "Le journal de la V3 commence au passage. Le journal de la V2 (table AUDIT) reste dans la V2, consultable la." }, ct);

    private static async Task Inserer(SqlConnection cn, SqlTransaction? tx, string source, int? idAgent, string? agent, string? compteV2,
                                      string? ecran, string famille, string objet, string? idObjet, string verbe, object? avant, object? apres, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            INSERT INTO soc.Journal (Source, IdAgent, Agent, CompteV2, Ecran, Famille, Objet, IdObjet, Verbe, Avant, Apres)
            VALUES (@source, @idAgent, @agent, @compteV2, @ecran, @famille, @objet, @idObjet, @verbe, @avant, @apres);
            """, cn, tx);
        cmd.Parameters.Add(new SqlParameter("@source", SqlDbType.VarChar, 10) { Value = source });
        cmd.Parameters.Add(new SqlParameter("@idAgent", SqlDbType.Int) { Value = (object?)idAgent ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@agent", SqlDbType.VarChar, 80) { Value = (object?)Borne(agent, 80) ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@compteV2", SqlDbType.VarChar, 35) { Value = (object?)compteV2 ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@ecran", SqlDbType.VarChar, 80) { Value = (object?)Borne(ecran, 80) ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@famille", SqlDbType.VarChar, 30) { Value = famille });
        cmd.Parameters.Add(new SqlParameter("@objet", SqlDbType.VarChar, 120) { Value = Borne(objet, 120) });
        cmd.Parameters.Add(new SqlParameter("@idObjet", SqlDbType.VarChar, 40) { Value = (object?)idObjet ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@verbe", SqlDbType.VarChar, 20) { Value = verbe });
        cmd.Parameters.Add(new SqlParameter("@avant", SqlDbType.NVarChar, -1) { Value = avant is null ? DBNull.Value : JsonSerializer.Serialize(avant, Json) });
        cmd.Parameters.Add(new SqlParameter("@apres", SqlDbType.NVarChar, -1) { Value = apres is null ? DBNull.Value : JsonSerializer.Serialize(apres, Json) });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Un ajout ou un retrait de role (SOC-005 CA01).</summary>
    public static async Task EcrireDroitAsync(SqlConnection cn, SqlTransaction tx, Appelant auteur, string? ecran,
                                              int idCompteVise, string compteVise, string role, bool ajout, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            INSERT INTO soc.JournalDroits (IdCompteVise, CompteVise, Role, Sens, IdAuteur, Auteur, Ecran)
            VALUES (@idVise, @vise, @role, @sens, @idAuteur, @auteur, @ecran);
            """, cn, tx);
        cmd.Parameters.Add(new SqlParameter("@idVise", SqlDbType.Int) { Value = idCompteVise });
        cmd.Parameters.Add(new SqlParameter("@vise", SqlDbType.VarChar, 35) { Value = compteVise });
        cmd.Parameters.Add(new SqlParameter("@role", SqlDbType.VarChar, 40) { Value = role });
        cmd.Parameters.Add(new SqlParameter("@sens", SqlDbType.VarChar, 7) { Value = ajout ? "ajout" : "retrait" });
        cmd.Parameters.Add(new SqlParameter("@idAuteur", SqlDbType.Int) { Value = auteur.Id });
        cmd.Parameters.Add(new SqlParameter("@auteur", SqlDbType.VarChar, 80) { Value = Borne(auteur.NomAffiche, 80) });
        cmd.Parameters.Add(new SqlParameter("@ecran", SqlDbType.VarChar, 80) { Value = (object?)Borne(ecran, 80) ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Un evenement technique (codes de CODE_JET de la V2) : 130 pour un droit qui touche la facturation
    /// ou l'encaissement (SOC-005 CA02), 210 pour la reprise (CA03).
    /// </summary>
    public static async Task EcrireEvenementAsync(SqlConnection cn, SqlTransaction tx, short code, string libelle, string detail,
                                                  Appelant? auteur, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            INSERT INTO soc.EvenementTechnique (Code, Libelle, Detail, IdAuteur, Auteur)
            VALUES (@code, @libelle, @detail, @idAuteur, @auteur);
            """, cn, tx);
        cmd.Parameters.Add(new SqlParameter("@code", SqlDbType.SmallInt) { Value = code });
        cmd.Parameters.Add(new SqlParameter("@libelle", SqlDbType.VarChar, 120) { Value = libelle });
        cmd.Parameters.Add(new SqlParameter("@detail", SqlDbType.NVarChar, 1000) { Value = Borne(detail, 1000) });
        cmd.Parameters.Add(new SqlParameter("@idAuteur", SqlDbType.Int) { Value = (object?)auteur?.Id ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@auteur", SqlDbType.VarChar, 80) { Value = auteur?.NomAffiche ?? "Reprise de la V2" });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Le journal des gestes, du plus recent au plus ancien, en phrases (SOC-016 CA03). Sans
    /// <paramref name="toutLeGreffe"/>, l'appelant ne voit que SES gestes : jamais l'activite d'un collegue.
    /// </summary>
    public static async Task<VueJournal> LireAsync(BaseRig b, Appelant appelant, bool toutLeGreffe,
                                                   string? agent, string? objet, DateOnly? du, DateOnly? au, CancellationToken ct)
    {
        await using var cn = await b.OuvrirAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT TOP (500) Id, Instant, Source, Agent, CompteV2, Ecran, Famille, Objet, Verbe, Avant, Apres
            FROM soc.Journal
            WHERE (@tout = 1 OR IdAgent = @moi)
              AND (@agent IS NULL OR Agent LIKE '%' + @agent + '%' OR CompteV2 LIKE '%' + @agent + '%')
              AND (@objet IS NULL OR Objet LIKE '%' + @objet + '%' OR Famille = @objet)
              AND (@du IS NULL OR Instant >= @du)
              AND (@au IS NULL OR Instant < DATEADD(day, 1, @au))
            ORDER BY Instant DESC, Id DESC;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@tout", SqlDbType.Bit) { Value = toutLeGreffe });
        cmd.Parameters.Add(new SqlParameter("@moi", SqlDbType.Int) { Value = appelant.Id });
        cmd.Parameters.Add(new SqlParameter("@agent", SqlDbType.VarChar, 80) { Value = Filtre(agent) });
        cmd.Parameters.Add(new SqlParameter("@objet", SqlDbType.VarChar, 120) { Value = Filtre(objet) });
        cmd.Parameters.Add(new SqlParameter("@du", SqlDbType.Date) { Value = du is { } d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@au", SqlDbType.Date) { Value = au is { } a ? a.ToDateTime(TimeOnly.MinValue) : DBNull.Value });
        var lignes = new List<LigneJournal>();
        await using var lecteur = await cmd.ExecuteReaderAsync(ct);
        while (await lecteur.ReadAsync(ct))
        {
            var source = lecteur.GetString(2);
            var qui = source switch
            {
                "recopie-v2" => $"La recopie de la V2 (compte V2 {lecteur.GetString(4)})",
                "passage" => "Le passage à la V3",
                _ => lecteur.GetString(3),
            };
            var verbe = lecteur.GetString(8);
            var lu = Verbes.TryGetValue(verbe, out var v) ? v : verbe;
            var objetLu = lecteur.GetString(7);
            lignes.Add(new LigneJournal(
                lecteur.GetInt64(0), DateTime.SpecifyKind(lecteur.GetDateTime(1), DateTimeKind.Utc),
                $"{qui} {lu} {objetLu}", qui, lu, lecteur.GetString(6), objetLu,
                lecteur.IsDBNull(5) ? null : lecteur.GetString(5),
                lecteur.IsDBNull(9) ? null : lecteur.GetString(9),
                lecteur.IsDBNull(10) ? null : lecteur.GetString(10),
                source));
        }
        return new VueJournal(toutLeGreffe, [.. lignes]);
    }

    /// <summary>Le journal des droits filtre par compte vise, role et periode, du plus recent au plus ancien (SOC-005 CA05).</summary>
    public static async Task<LigneJournalDroits[]> LireDroitsAsync(BaseRig b, string? compte, string? role, DateOnly? du, DateOnly? au, CancellationToken ct)
    {
        await using var cn = await b.OuvrirAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT TOP (500) Id, Instant, CompteVise, Role, Sens, Auteur, Ecran
            FROM soc.JournalDroits
            WHERE (@compte IS NULL OR CompteVise = @compte)
              AND (@role IS NULL OR Role = @role)
              AND (@du IS NULL OR Instant >= @du)
              AND (@au IS NULL OR Instant < DATEADD(day, 1, @au))
            ORDER BY Instant DESC, Id DESC;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@compte", SqlDbType.VarChar, 35) { Value = Filtre(compte) });
        cmd.Parameters.Add(new SqlParameter("@role", SqlDbType.VarChar, 40) { Value = Filtre(role) });
        cmd.Parameters.Add(new SqlParameter("@du", SqlDbType.Date) { Value = du is { } d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@au", SqlDbType.Date) { Value = au is { } a ? a.ToDateTime(TimeOnly.MinValue) : DBNull.Value });
        var lignes = new List<LigneJournalDroits>();
        await using var lecteur = await cmd.ExecuteReaderAsync(ct);
        while (await lecteur.ReadAsync(ct))
            lignes.Add(new LigneJournalDroits(lecteur.GetInt64(0), DateTime.SpecifyKind(lecteur.GetDateTime(1), DateTimeKind.Utc),
                lecteur.GetString(2), lecteur.GetString(3), lecteur.GetString(4), lecteur.GetString(5),
                lecteur.IsDBNull(6) ? null : lecteur.GetString(6)));
        return [.. lignes];
    }

    /// <summary>« 2026-11-20 » -> une date ; vide -> null ; illisible -> leve (la route rend 400).</summary>
    public static DateOnly? Date(string? texte)
        => string.IsNullOrWhiteSpace(texte) ? null
         : DateOnly.ParseExact(texte.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static object Filtre(string? texte) => string.IsNullOrWhiteSpace(texte) ? DBNull.Value : texte.Trim();

    private static string? Borne(string? texte, int longueur) => texte is null ? null : texte.Length <= longueur ? texte : texte[..longueur];
}
