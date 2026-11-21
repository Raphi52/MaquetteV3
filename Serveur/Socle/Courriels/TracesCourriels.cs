using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;

namespace RigV3.Serveur.Socle.Courriels;

/// <summary>Une etape a ecrire dans soc.TraceEnvoi (EmailEnvoiTrace de la V2, memes champs).</summary>
public sealed record LigneTrace(
    Guid Suivi,
    string CodeGreffe,
    string Application,
    string? Mode,
    string Etape,
    string? TypeModele = null,
    int? IdCourriel = null,
    int? IdLigneEdition = null,
    string? IdsLignesEdition = null,
    string? Expediteur = null,
    string? Destinataires = null,
    string? Copie = null,
    string? Reponse = null,
    string? Sujet = null,
    bool? EstHtml = null,
    int? NbPieces = null,
    string? Pieces = null,
    string? Urls = null,
    string? SmtpHote = null,
    int? SmtpPort = null,
    bool? Succes = null,
    string? ErreurType = null,
    string? ErreurMessage = null,
    string? Pile = null,
    string? Origine = null,
    string? Agent = null,
    string? FichierDepot = null);

/// <summary>
/// LA TRACE DES ENVOIS (SOC-006 CA03, CA04), dans la base V3 du greffe : soc.TraceEnvoi.
/// Recopie d'EmailEnvoiTrace de la V2 (D:\RigApplication\Source\Outils\DLL\RigMetier\Mail\EmailEnvoiTrace.cs) :
/// ecriture de chaque etape, lecture pour l'ecran « Courriels envoyés », purge par anciennete.
/// </summary>
public static class TracesCourriels
{
    /// <summary>
    /// Ecrit une etape. Comme la V2 (EmailEnvoiTrace.Write, « best-effort ») : une trace qui ne s'ecrit pas n'arrete pas
    /// l'envoi -- un courriel deja remis ne se reprend pas --, et la cause part au journal du serveur au lieu d'etre perdue.
    /// Rend faux si l'etape n'a pas ete ecrite. Les textes trop longs sont coupes a la taille de la colonne, comme la V2.
    /// </summary>
    public static async Task<bool> EcrireAsync(SqlConnection cn, LigneTrace t, ILogger journal, CancellationToken ct)
    {
        try
        {
            await using var cmd = new SqlCommand("""
                INSERT INTO soc.TraceEnvoi (CodeGreffe, Suivi, Application, Mode, Etape, IdCourriel, IdLigneEdition, IdsLignesEdition,
                    Expediteur, Destinataires, Copie, Reponse, Sujet, EstHtml, NbPieces, Pieces, Urls, SmtpHote, SmtpPort, Succes,
                    ErreurType, ErreurMessage, Pile, Origine, Machine, UtilisateurWindows, TypeModele, FichierDepot)
                VALUES (@greffe, @suivi, @app, @mode, @etape, @idCourriel, @idLigne, @idsLignes,
                    @de, @a, @copie, @reponse, @sujet, @html, @nb, @pieces, @urls, @hote, @port, @succes,
                    @errType, @errMsg, @pile, @origine, @machine, @agent, @modele, @depot);
                """, cn);
            var p = cmd.Parameters;
            p.Add(Texte("@greffe", t.CodeGreffe, 20));
            p.Add(new SqlParameter("@suivi", SqlDbType.UniqueIdentifier) { Value = t.Suivi });
            p.Add(Texte("@app", t.Application, 80));
            p.Add(Texte("@mode", t.Mode, 20));
            p.Add(Texte("@etape", t.Etape, 50));
            p.Add(Entier("@idCourriel", t.IdCourriel));
            p.Add(Entier("@idLigne", t.IdLigneEdition));
            p.Add(Texte("@idsLignes", t.IdsLignesEdition, 1000));
            p.Add(Texte("@de", t.Expediteur, 300));
            p.Add(Texte("@a", t.Destinataires, 2000));
            p.Add(Texte("@copie", t.Copie, 2000));
            p.Add(Texte("@reponse", t.Reponse, 300));
            p.Add(Texte("@sujet", t.Sujet, 300));
            p.Add(new SqlParameter("@html", SqlDbType.Bit) { Value = (object?)t.EstHtml ?? DBNull.Value });
            p.Add(Entier("@nb", t.NbPieces));
            p.Add(Texte("@pieces", t.Pieces, -1));
            p.Add(Texte("@urls", t.Urls, -1));
            p.Add(Texte("@hote", t.SmtpHote, 200));
            p.Add(Entier("@port", t.SmtpPort));
            p.Add(new SqlParameter("@succes", SqlDbType.Bit) { Value = (object?)t.Succes ?? DBNull.Value });
            p.Add(Texte("@errType", t.ErreurType, 200));
            p.Add(Texte("@errMsg", t.ErreurMessage, -1));
            p.Add(Texte("@pile", t.Pile, -1));
            p.Add(Texte("@origine", t.Origine, -1));
            p.Add(Texte("@machine", Environment.MachineName, 100));
            p.Add(Texte("@agent", t.Agent, 100));
            p.Add(Texte("@modele", t.TypeModele, 40));
            p.Add(new SqlParameter("@depot", SqlDbType.NVarChar, 400) { Value = (object?)Couper(t.FichierDepot, 400) ?? DBNull.Value });
            await cmd.ExecuteNonQueryAsync(ct);
            return true;
        }
        catch (Exception e) when (e is SqlException or InvalidOperationException)
        {
            journal.LogError(EvenementTraceImpossible, e, "Trace de courriel impossible ({Etape}, suivi {Suivi}, greffe {Greffe}) : {Cause}",
                t.Etape, t.Suivi, t.CodeGreffe, e.Message);
            return false;
        }
    }

    /// <summary>L'evenement de journal d'une trace qui n'a pas pu s'ecrire.</summary>
    public static readonly EventId EvenementTraceImpossible = new(51061, "TraceCourrielImpossible");

    /// <summary>
    /// Les envois du greffe, un par numero de suivi, les plus recents d'abord, <paramref name="max"/> au plus (500 par
    /// defaut, jamais illimite, comme OPE_LISTE_MAILS_LOT1 de la V2). Bornes de date incluses, sur le jour du debut.
    /// </summary>
    public static async Task<CourrielSuivi[]> ListerAsync(SqlConnection cn, DateOnly? du, DateOnly? au, string? destinataire,
                                                          string? modele, string? etat, int? max, CancellationToken ct)
    {
        var plafond = max is > 0 ? Math.Min(max.Value, RoutesCourriels.MaximumParDefaut) : RoutesCourriels.MaximumParDefaut;
        await using var cmd = new SqlCommand($"""
            WITH Envois AS (
                SELECT t.Suivi, MIN(t.DateCreation) AS Debut, {EtatSql} AS Etat,
                       MAX(t.TypeModele) AS TypeModele, MAX(t.Expediteur) AS Expediteur, MAX(t.Destinataires) AS Destinataires,
                       MAX(t.Copie) AS Copie, MAX(t.Sujet) AS Sujet, MAX(t.NbPieces) AS NbPieces, MAX(t.IdCourriel) AS IdCourriel,
                       MAX(CASE WHEN t.Succes = 0 THEN t.ErreurMessage END) AS Erreur, MAX(t.UtilisateurWindows) AS Agent,
                       MAX(t.FichierDepot) AS FichierDepot
                FROM soc.TraceEnvoi t
                GROUP BY t.Suivi)
            SELECT TOP (@max) Suivi, Debut, Etat, TypeModele, Expediteur, Destinataires, Copie, Sujet, NbPieces, IdCourriel, Erreur, Agent, FichierDepot
            FROM Envois
            WHERE (@du IS NULL OR Debut >= @du)
              AND (@au IS NULL OR Debut < DATEADD(DAY, 1, @au))
              AND (@dest IS NULL OR Destinataires LIKE '%' + @dest + '%' OR Copie LIKE '%' + @dest + '%')
              AND (@modele IS NULL OR TypeModele LIKE '%' + @modele + '%')
              AND (@etat IS NULL OR Etat = @etat)
            ORDER BY Debut DESC;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@max", SqlDbType.Int) { Value = plafond });
        cmd.Parameters.Add(new SqlParameter("@du", SqlDbType.DateTime) { Value = du is { } d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@au", SqlDbType.DateTime) { Value = au is { } a ? a.ToDateTime(TimeOnly.MinValue) : DBNull.Value });
        cmd.Parameters.Add(Texte("@dest", Vide(destinataire), 2000));
        cmd.Parameters.Add(Texte("@modele", Vide(modele), 40));
        cmd.Parameters.Add(Texte("@etat", Vide(etat), 20));
        var envois = new List<CourrielSuivi>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) envois.Add(Envoi(r));
        return [.. envois];
    }

    /// <summary>Un envoi, le texte du courriel parti et ses etapes dans l'ordre ; null s'il est inconnu.</summary>
    public static async Task<DetailCourriel?> DetailAsync(SqlConnection cn, Guid suivi, CancellationToken ct)
    {
        CourrielSuivi? envoi = null;
        await using (var cmd = new SqlCommand($"""
            SELECT t.Suivi, MIN(t.DateCreation), {EtatSql},
                   MAX(t.TypeModele), MAX(t.Expediteur), MAX(t.Destinataires), MAX(t.Copie), MAX(t.Sujet), MAX(t.NbPieces),
                   MAX(t.IdCourriel), MAX(CASE WHEN t.Succes = 0 THEN t.ErreurMessage END), MAX(t.UtilisateurWindows), MAX(t.FichierDepot)
            FROM soc.TraceEnvoi t WHERE t.Suivi = @suivi GROUP BY t.Suivi;
            """, cn))
        {
            cmd.Parameters.Add(new SqlParameter("@suivi", SqlDbType.UniqueIdentifier) { Value = suivi });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct)) envoi = Envoi(r);
        }
        if (envoi is null) return null;

        var etapes = new List<EtapeCourriel>();
        await using (var cmd = new SqlCommand("""
            SELECT DateCreation, Etape, Mode, Succes, SmtpHote, SmtpPort, ErreurType, ErreurMessage, FichierDepot
            FROM soc.TraceEnvoi WHERE Suivi = @suivi ORDER BY DateCreation, Id;
            """, cn))
        {
            cmd.Parameters.Add(new SqlParameter("@suivi", SqlDbType.UniqueIdentifier) { Value = suivi });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                etapes.Add(new EtapeCourriel(r.GetDateTime(0), r.GetString(1), Chaine(r, 2), r.IsDBNull(3) ? null : r.GetBoolean(3),
                    Chaine(r, 4), r.IsDBNull(5) ? null : r.GetInt32(5), Chaine(r, 6), Chaine(r, 7), Chaine(r, 8)));
        }

        string? corps = null;
        bool? html = null;
        if (envoi.IdCourriel is { } id)
        {
            await using var cmd = new SqlCommand("SELECT Corps, CorpsHtml FROM soc.CourrielEnvoye WHERE Id = @id;", cn);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct)) { corps = r.GetString(0); html = r.GetBoolean(1); }
        }
        return new DetailCourriel(envoi, corps, html, [.. etapes]);
    }

    /// <summary>
    /// Efface les traces plus anciennes que <paramref name="jours"/> jours (EmailEnvoiTrace.Purge, EmailEnvoiTrace.cs:171-188),
    /// par paquets de 10 000 pour ne pas tenir la table longtemps ; la V2 n'en effacait qu'un paquet par lancement de
    /// RIG_Send_Email, le menage de la V3 va jusqu'au bout. Rend le nombre de traces effacees.
    /// </summary>
    public static async Task<int> PurgerAsync(SqlConnection cn, int jours, CancellationToken ct)
    {
        if (jours <= 0) throw new ArgumentOutOfRangeException(nameof(jours), jours, "La duree de conservation est un nombre de jours positif.");
        var total = 0;
        while (true)
        {
            await using var cmd = new SqlCommand(
                "DELETE TOP (10000) FROM soc.TraceEnvoi WHERE DateCreation < DATEADD(DAY, -@jours, GETDATE());", cn) { CommandTimeout = 60 };
            cmd.Parameters.Add(new SqlParameter("@jours", SqlDbType.Int) { Value = jours });
            var effacees = await cmd.ExecuteNonQueryAsync(ct);
            total += effacees;
            if (effacees < 10000) return total;
        }
    }

    /// <summary>L'etat d'un envoi d'apres ses etapes (<see cref="EtatsCourriel"/>), calcule par la base.</summary>
    private const string EtatSql = $"""
        CASE WHEN MAX(CASE WHEN t.Etape IN ('{EtapesCourriel.EnvoiErreur}', '{EtapesCourriel.PreparationErreur}') THEN 1 ELSE 0 END) = 1 THEN '{EtatsCourriel.Echec}'
             WHEN MAX(CASE WHEN t.Etape = '{EtapesCourriel.EnvoiSucces}' THEN 1 ELSE 0 END) = 1
                  THEN CASE WHEN MAX(CASE WHEN t.Etape = '{EtapesCourriel.IgnoreHorsProduction}' THEN 1 ELSE 0 END) = 1
                            THEN '{EtatsCourriel.Ignore}' ELSE '{EtatsCourriel.Envoye}' END
             ELSE '{EtatsCourriel.EnCours}' END
        """;

    private static CourrielSuivi Envoi(SqlDataReader r)
        => new(r.GetGuid(0), r.GetDateTime(1), r.GetString(2), Chaine(r, 3), Chaine(r, 4), Chaine(r, 5), Chaine(r, 6), Chaine(r, 7),
               r.IsDBNull(8) ? null : r.GetInt32(8), r.IsDBNull(9) ? null : r.GetInt32(9), Chaine(r, 10), Chaine(r, 11), Chaine(r, 12));

    private static string? Chaine(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static string? Vide(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Couper(string? s, int taille) => s is not null && taille > 0 && s.Length > taille ? s[..taille] : s;

    private static SqlParameter Texte(string nom, string? valeur, int taille)
        => new(nom, SqlDbType.VarChar, taille) { Value = (object?)Couper(valeur, taille) ?? DBNull.Value };

    private static SqlParameter Entier(string nom, int? valeur) => new(nom, SqlDbType.Int) { Value = (object?)valeur ?? DBNull.Value };
}
