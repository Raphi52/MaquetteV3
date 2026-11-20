using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.Greffes;

/// <summary>
/// Les routes du catalogue des greffes et de leurs reglages (SOC-003, SOC-007, SOC-008 ;
/// lot 4). Le catalogue est lu dans la base commune (BaseRig) ; la base d'un greffe ne
/// s'ouvre que par lui (<see cref="CatalogueGreffes.OuvrirBaseV3Async"/>, CA04).
///
/// ACCES : toutes les routes du module portent <see cref="PolitiquesSocle.AgentAdmis"/>
/// (lot 2, SOC-001), posee une fois sur leur groupe pour qu'aucune ne l'oublie : 401 sans
/// session Windows, 403 pour un compte refuse ; la route ne s'execute pas.
/// L'AUTEUR d'une modification du catalogue est l'identite de cet agent admis (SOC-007
/// CA06) ; celui d'un reglage est son NOM, comme la V2 (SOC-003 CA07,
/// <see cref="AuteurReglage"/>). Le droit de modifier (exploitation d'Amitel, agent charge du
/// parametrage) viendra des droits du lot 3 (SOC-002).
///
/// La creation des bases (BaseGreffe) et les reprises (RepriseCatalogue,
/// RepriseReglages) ne sont PAS des routes : ce sont des gestes d'exploitation sur
/// les instances des greffes (docs/reconstruction/socle-greffes.md).
/// </summary>
internal sealed class ModuleGreffes : IModuleServeur
{
    /// <summary>Delai au-dela duquel un chemin reseau est declare injoignable (CA05).</summary>
    private static readonly TimeSpan DelaiChemin = TimeSpan.FromSeconds(5);

    public void Inscrire(IServiceCollection services, IConfiguration configuration)
        => services.AddSingleton<CatalogueGreffes>();

    public void Brancher(IEndpointRouteBuilder routes)
    {
        var greffes = routes.MapGroup(string.Empty).RequireAuthorization(PolitiquesSocle.AgentAdmis);

        greffes.MapGet(RoutesGreffes.Liste, async (CatalogueGreffes c, CancellationToken ct) =>
            Results.Ok(await c.ListerAsync(ct)));

        greffes.MapGet(RoutesGreffes.Fiche, async (string code, CatalogueGreffes c, CancellationToken ct) =>
            await c.LireAsync(code, ct) is { } fiche ? Results.Ok(fiche) : Inconnu(code));

        greffes.MapGet(RoutesGreffes.Historique, async (string code, CatalogueGreffes c, CancellationToken ct) =>
            await c.LireAsync(code, ct) is null ? Inconnu(code) : Results.Ok(await c.HistoriqueAsync(code, ct)));

        greffes.MapPut(RoutesGreffes.Passage, async (string code, SaisiePassage saisie, HttpContext http, CatalogueGreffes c,
                                                   CancellationToken ct) =>
        {
            if (Auteur(http) is not { } auteur) return SansAuteur();
            // Heure LOCALE du greffe, sans decalage ni « Z » : « 2027-03-01T08:00 » (CA05).
            if (!DateTime.TryParseExact(saisie.PassageLocal, ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                return Refus(StatusCodes.Status400BadRequest,
                    $"Date de passage illisible : « {saisie.PassageLocal} ». Forme attendue : 2027-03-01T08:00, heure locale du greffe, sans decalage.");
            return await Modifier(() => c.SaisirPassageAsync(code, local, auteur, ct));
        });

        greffes.MapPut(RoutesGreffes.Fuseau, async (string code, SaisieFuseau saisie, HttpContext http, CatalogueGreffes c,
                                                  CancellationToken ct) =>
            Auteur(http) is { } auteur ? await Modifier(() => c.SaisirFuseauAsync(code, saisie.Fuseau, auteur, ct)) : SansAuteur());

        greffes.MapGet(RoutesGreffes.Base, async (string code, CatalogueGreffes c, CancellationToken ct) =>
            await SurBaseV3(c, code, ct, async cn =>
            {
                var fiche = (await c.LireAsync(code, ct))!;
                var (jouees, derniere) = await Migrateur.EtatJournalAsync(cn, ct);
                return Results.Ok(new EtatBaseGreffe(code, fiche.Serveur, fiche.BaseV3, true, jouees, derniere, null));
            }));

        greffes.MapGet(RoutesGreffes.Reglages, async (string code, CatalogueGreffes c, CancellationToken ct) =>
            await SurBaseV3(c, code, ct, async cn => Results.Ok(await LireReglagesAsync(cn, null, ct))));

        greffes.MapGet(RoutesGreffes.Reglage, async (string code, string reglage, CatalogueGreffes c, CancellationToken ct) =>
            await SurBaseV3(c, code, ct, async cn =>
                (await LireReglagesAsync(cn, reglage, ct)).FirstOrDefault() is { } r ? Results.Ok(r) : ReglageAbsent(code, reglage)));

        // CA07 : comme Parametrage.UpdateValeurParametrage (Parametrage.cs:103-121) -- l'auteur
        // (le NOM de l'agent, AuteurReglage) et la date sont poses avec la valeur ; un reglage
        // absent ne se cree pas (404). L'agent est celui que la politique AgentAdmis vient de
        // juger : DeLAppelantAsync le reprend de la requete, sans relire la base.
        // fix-ok: la route ecrivait le compte de la session (« AMITEL\banc.socle », banc rouge du 2026-11-21) au lieu du nom UTLST_NOM de la V2.
        greffes.MapPut(RoutesGreffes.Reglage, async (string code, string reglage, SaisieReglage saisie, HttpContext http,
                                                   CatalogueGreffes c, ServiceIdentite identites, BaseRig b,
                                                   CancellationToken ct) =>
        {
            if (AuteurReglage.De(await identites.DeLAppelantAsync(http, b, ct), http.User) is not { } auteur)
                return SansAuteur();
            return await SurBaseV3(c, code, ct, async cn =>
            {
                await using var cmd = new SqlCommand(
                    "UPDATE soc.Reglage SET Valeur = @v, AuteurModif = @a, DateModif = GETDATE() WHERE Code = @c;", cn);
                cmd.Parameters.Add(new SqlParameter("@v", System.Data.SqlDbType.VarChar, 1000) { Value = (object?)saisie.Valeur ?? DBNull.Value });
                // varchar(50) comme PARAM_AUTEUR_MODIF : la V2 coupe aussi ses textes a la taille de la colonne.
                cmd.Parameters.Add(new SqlParameter("@a", System.Data.SqlDbType.VarChar, 50) { Value = auteur.Length > 50 ? auteur[..50] : auteur });
                cmd.Parameters.Add(new SqlParameter("@c", System.Data.SqlDbType.VarChar, 100) { Value = reglage });
                if (await cmd.ExecuteNonQueryAsync(ct) == 0) return ReglageAbsent(code, reglage);
                return Results.Ok((await LireReglagesAsync(cn, reglage, ct)).Single());
            });
        });

        greffes.MapGet(RoutesGreffes.Reprise, async (string code, CatalogueGreffes c, CancellationToken ct) =>
            await SurBaseV3(c, code, ct, async cn => Results.Ok(await RepriseReglages.RapportAsync(code, cn, DelaiChemin, ct))));
    }

    /// <summary>L'auteur d'une modification : l'identite authentifiee de la requete ; null sinon.
    /// La politique du groupe refuse deja l'appel sans session : ce test est le second verrou,
    /// si une route sortait un jour du groupe.</summary>
    private static string? Auteur(HttpContext http)
        => http.User.Identity is { IsAuthenticated: true, Name: { Length: > 0 } nom } ? nom : null;

    private static async Task<IResult> SurBaseV3(CatalogueGreffes c, string code, CancellationToken ct, Func<SqlConnection, Task<IResult>> travail)
    {
        SqlConnection cn;
        try { cn = await c.OuvrirBaseV3Async(code, ct); }
        catch (GreffeInconnu) { return Inconnu(code); }
        catch (BaseGreffeInjoignable e)
        {
            // CA04 : signalee, sans repli silencieux sur une autre base.
            return Results.Json(new EtatBaseGreffe(code, e.Serveur, e.Base, false, 0, null, e.Message),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        await using (cn) return await travail(cn);
    }

    private static async Task<IResult> Modifier(Func<Task<FicheGreffe>> modification)
    {
        try { return Results.Ok(await modification()); }
        catch (GreffeInconnu e) { return Refus(StatusCodes.Status404NotFound, e.Message); }
        catch (ArgumentException e) { return Refus(StatusCodes.Status400BadRequest, e.Message); }
        catch (InvalidOperationException e) { return Refus(StatusCodes.Status409Conflict, e.Message); }
        // La base refuse aussi (declencheur soc.TR_Greffe_Regles) : meme reponse, sa cause en clair.
        catch (SqlException e) when (e.Number is >= 51700 and <= 51704) { return Refus(StatusCodes.Status409Conflict, e.Message); }
    }

    private static async Task<List<ReglageGreffe>> LireReglagesAsync(SqlConnection cn, string? code, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            "SELECT Code, Libelle, Valeur, Utilisation, AuteurCreation, DateCreation, AuteurModif, DateModif FROM soc.Reglage " +
            (code is null ? "ORDER BY Code;" : "WHERE Code = @c;"), cn);
        if (code is not null) cmd.Parameters.Add(new SqlParameter("@c", System.Data.SqlDbType.VarChar, 100) { Value = code });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var liste = new List<ReglageGreffe>();
        string? Texte(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        DateTime? Date(int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
        while (await r.ReadAsync(ct))
            liste.Add(new ReglageGreffe(r.GetString(0), r.GetString(1), Texte(2), Texte(3), Texte(4), Date(5), Texte(6), Date(7)));
        return liste;
    }

    private static IResult Inconnu(string code)
        => Refus(StatusCodes.Status404NotFound, $"Le greffe « {code} » n'est pas au catalogue commun.");

    private static IResult ReglageAbsent(string code, string reglage)
        => Refus(StatusCodes.Status404NotFound, $"Le reglage « {reglage} » n'existe pas dans la base du greffe {code}.");

    private static IResult SansAuteur()
        => Refus(StatusCodes.Status401Unauthorized,
            "Modification refusee : la requete n'a pas d'identite (session Windows), son auteur ne serait pas garde.");

    private static IResult Refus(int statut, string motif) => Results.Json(new RefusGreffe(motif), statusCode: statut);
}
