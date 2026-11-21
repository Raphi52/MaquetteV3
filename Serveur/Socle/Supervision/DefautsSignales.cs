using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.Supervision;

// ============================================================
// LES DEFAUTS SIGNALES DEPUIS LA V3 (SOC-022 CA06, T03 ; lot 17 du plan « Reconstruction de RIG — phase 2 », 2026-11-21).
//
// Un agent d'un greffe beta signale un defaut en un geste (bouton de l'en-tete, ou Ctrl Maj D) : l'ecran prend sans
// saisie l'ecran ouvert, le greffe, la version, l'heure du poste et la derniere erreur suivie ; l'agent ajoute une
// phrase. La partie serveur y ajoute le compte de la session et l'identifiant de suivi : celui du PARCOURS de la
// requete (IntercepteurErreurs, en-tete X-Rig-Suivi), le meme mecanisme que les erreurs -- pas un second. L'agent lit
// cet identifiant a l'ecran ; l'exploitation d'Amitel le retrouve dans l'ecran Supervision (onglet Defauts) et
// « RigV3.Passage beta defauts » le classe par la regle ecrite de SOC-022 CA03 (Outils\Passage\Beta.cs).
// Table soc.DefautSignale de la base COMMUNE (Base\migrations\SOC-022_defauts.sql) ; aucune autre ecriture.
// ============================================================

/// <summary>
/// Les routes des defauts signales, a part du module de la supervision (un module par sujet ; celui du lot 2 garde ses
/// cinq routes, que son banc compte) : POST /socle/defauts, ouvert a toute session Windows comme la remontee des erreurs
/// de l'ecran ; GET /socle/supervision/defauts, reserve a l'exploitation d'Amitel (<see cref="PolitiquesSupervision.ExploitationAmitel"/>).
/// </summary>
internal sealed class ModuleDefauts : IModuleServeur
{
    public void Inscrire(IServiceCollection services, IConfiguration configuration) => services.AddSingleton<DefautsSignales>();

    public void Brancher(IEndpointRouteBuilder routes)
    {
        // SOC-022 CA06 : un defaut signale depuis la V3. Son identifiant de suivi est celui du parcours de CETTE requete
        // (IntercepteurErreurs) ; si l'ecran en a envoye un (un parcours deja commence), le defaut en recoit un neuf : un
        // identifiant ne nomme qu'un defaut, c'est par lui qu'il se classe (CA03). Le compte vient de la session.
        routes.MapPost(RoutesSupervision.Defauts, async (SignalementDefaut s, HttpContext h, DefautsSignales defauts, CancellationToken ct) =>
            {
                if (DefautsSignales.Refus(s) is { } refus) return Results.BadRequest(new RefusGreffe(refus));
                var envoye = Suivis.Normaliser(h.Request.Headers[RoutesSupervision.EnTeteSuivi].ToString());
                var suivi = envoye is null && Parcours.Courant is { } parcours ? parcours.Suivi : Suivis.Nouveau();
                h.Response.Headers[RoutesSupervision.EnTeteSuivi] = suivi;
                await defauts.EcrireAsync(s, suivi, ServiceIdentite.CompteDe(h.User), ct);
                return Results.Ok(new SuiviErreur(suivi, DefautsSignales.PhraseAgent(suivi)));
            })
            .RequireAuthorization();

        routes.MapGet(RoutesSupervision.DefautsSignales, async (string? suivi, string? greffe, DefautsSignales defauts, CancellationToken ct)
                => Results.Ok(await defauts.ListerAsync(suivi, greffe, ct)))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis, PolitiquesSupervision.ExploitationAmitel);
    }
}

/// <summary>Ecrit et lit les defauts signales, dans la base commune.</summary>
public sealed class DefautsSignales(BaseRig commune)
{
    /// <summary>La phrase montree a l'agent : l'identifiant, et a quoi il sert.</summary>
    public static string PhraseAgent(string suivi)
        => $"Défaut signalé à Amitel. Identifiant de suivi {suivi} : donnez-le si l'assistance d'Amitel vous rappelle.";

    /// <summary>Le refus d'un signalement mal forme (motif en clair) ; null s'il peut s'ecrire.</summary>
    public static string? Refus(SignalementDefaut? s)
    {
        if (s is null) return "Le corps doit être le signalement du défaut.";
        if (string.IsNullOrWhiteSpace(s.Phrase)) return "Décrivez le défaut en une phrase : c'est la seule chose à écrire.";
        if (s.Phrase.Trim().Length > RoutesSupervision.PhraseAuPlus)
            return $"La phrase dépasse {RoutesSupervision.PhraseAuPlus} signes : gardez l'essentiel, l'écran et le contexte partent avec.";
        if (string.IsNullOrWhiteSpace(s.Ecran)) return "L'écran ouvert manque au signalement.";
        if (s.DerniereErreur is { Length: > 0 } e && Suivis.Normaliser(e) is null)
            return $"« {e} » n'est pas un identifiant de suivi (forme attendue : 7KQ2-M9XP).";
        return null;
    }

    /// <summary>Ecrit le defaut sous l'identifiant <paramref name="suivi"/> ; le compte est celui de la session.</summary>
    public async Task<DefautSignale> EcrireAsync(SignalementDefaut s, string suivi, string? compte, CancellationToken ct)
    {
        var instant = DateTime.UtcNow;
        var poste = Borne(string.IsNullOrWhiteSpace(s.Poste) ? "(poste inconnu)" : s.Poste.Trim(), 120)!;
        var ecran = Borne(s.Ecran.Trim(), 200)!;
        var phrase = s.Phrase.Trim();
        var derniere = Suivis.Normaliser(s.DerniereErreur);
        await using var cn = await commune.OuvrirAsync(ct);
        await using var cmd = new SqlCommand("""
            INSERT INTO soc.DefautSignale (Suivi, Instant, CodeGreffe, Compte, Poste, Ecran, CodeEcran, Version, HeurePoste, DerniereErreur, Phrase)
            OUTPUT inserted.Id
            VALUES (@suivi, @instant, @greffe, @compte, @poste, @ecran, @code, @version, @heure, @derniere, @phrase);
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@suivi", SqlDbType.Char, 9) { Value = suivi });
        cmd.Parameters.Add(new SqlParameter("@instant", SqlDbType.DateTime2) { Value = instant });
        cmd.Parameters.Add(new SqlParameter("@greffe", SqlDbType.VarChar, 20) { Value = (object?)Borne(Vide(s.CodeGreffe), 20) ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@compte", SqlDbType.VarChar, 35) { Value = (object?)Borne(compte, 35) ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@poste", SqlDbType.VarChar, 120) { Value = poste });
        cmd.Parameters.Add(new SqlParameter("@ecran", SqlDbType.NVarChar, 200) { Value = ecran });
        cmd.Parameters.Add(new SqlParameter("@code", SqlDbType.VarChar, 60) { Value = (object?)Borne(Vide(s.CodeEcran), 60) ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@version", SqlDbType.VarChar, 30) { Value = (object?)Borne(Vide(s.Version), 30) ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@heure", SqlDbType.DateTimeOffset) { Value = (object?)s.HeurePoste ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@derniere", SqlDbType.Char, 9) { Value = (object?)derniere ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@phrase", SqlDbType.NVarChar, RoutesSupervision.PhraseAuPlus) { Value = phrase });
        var id = (long)(await cmd.ExecuteScalarAsync(ct))!;
        return new DefautSignale(id, suivi, instant, Vide(s.CodeGreffe), compte, poste, ecran, Vide(s.CodeEcran), Vide(s.Version),
                                 s.HeurePoste, derniere, phrase);
    }

    /// <summary>Les defauts signales, le plus recent en premier, <see cref="RoutesSupervision.DefautsAuPlus"/> au plus ; par
    /// identifiant de suivi et par greffe si l'un ou l'autre est donne.</summary>
    public async Task<IReadOnlyList<DefautSignale>> ListerAsync(string? suivi, string? greffe, CancellationToken ct)
    {
        await using var cn = await commune.OuvrirAsync(ct);
        return await LireAsync(cn, suivi, greffe, ct);
    }

    /// <summary>La meme lecture sur une connexion deja ouverte a la base commune : « RigV3.Passage beta defauts » lit ainsi
    /// les defauts du dossier de la beta (Outils\Passage\Beta.cs), sans passer par la partie serveur.</summary>
    public static async Task<IReadOnlyList<DefautSignale>> LireAsync(SqlConnection cn, string? suivi, string? greffe, CancellationToken ct)
    {
        await using var cmd = new SqlCommand($"""
            SELECT TOP ({RoutesSupervision.DefautsAuPlus}) Id, Suivi, Instant, CodeGreffe, Compte, Poste, Ecran, CodeEcran, Version, HeurePoste, DerniereErreur, Phrase
            FROM soc.DefautSignale
            WHERE (@suivi IS NULL OR Suivi = @suivi) AND (@greffe IS NULL OR CodeGreffe = @greffe)
            ORDER BY Instant DESC, Id DESC;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@suivi", SqlDbType.Char, 9)
            { Value = (object?)Suivis.Normaliser(suivi) ?? (string.IsNullOrWhiteSpace(suivi) ? DBNull.Value : "?????????") });
        cmd.Parameters.Add(new SqlParameter("@greffe", SqlDbType.VarChar, 20) { Value = (object?)Borne(Vide(greffe), 20) ?? DBNull.Value });
        var lignes = new List<DefautSignale>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            lignes.Add(new DefautSignale(r.GetInt64(0), r.GetString(1), DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
                r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetDateTimeOffset(9), r.IsDBNull(10) ? null : r.GetString(10), r.GetString(11)));
        return lignes;
    }

    private static string? Vide(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Borne(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
