using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.Greffes;

/// <summary>
/// Les routes du catalogue des greffes et de leurs reglages (SOC-003, SOC-007, SOC-008 ;
/// lot 4). Le catalogue est lu dans la base commune (BaseRig) ; la base d'un greffe ne
/// s'ouvre que par lui (<see cref="CatalogueGreffes.OuvrirBaseV3Async"/>, CA04).
///
/// ACCES : toutes les routes du module portent <see cref="PolitiquesSocle.AgentAdmis"/>
/// (lot 2, SOC-001), posee une fois sur leur groupe pour qu'aucune ne l'oublie : 401 sans
/// session Windows, 403 pour un compte refuse ; la route ne s'execute pas. Les routes du
/// catalogue (<c>{code}</c>) jugent l'agent dans la base commune ; celles des reglages
/// (<c>{greffe}</c>, lot 9) dans la base V3 du greffe, comme la V2 lit et ecrit ses reglages
/// sous l'utilisateur du greffe -- 404 greffe inconnu, 503 base muette, sans repli.
///
/// MODIFIER (lot 9) : date et fuseau de passage, valeur d'un reglage. L'appelant doit avoir le
/// droit de modifier le greffe (<see cref="ServiceDroits.PeutModifierLeGreffe"/>, regle de la V2,
/// OPE_EDITER_GREFFE.cs:30-38) : sinon 403, rien n'est lu ni ecrit. Le geste s'ecrit au journal
/// des gestes (<see cref="JournalGestes.EcrireAsync"/>) DANS la transaction de la modification,
/// dans la base qu'elle modifie : la base commune pour le catalogue, celle du greffe pour un
/// reglage. L'AUTEUR d'une modification du catalogue est l'identite de l'agent (SOC-007 CA06) ;
/// celui d'un reglage est son NOM, comme la V2 (SOC-003 CA07, <see cref="AuteurReglage"/>).
///
/// La creation des bases (BaseGreffe) et les reprises (RepriseCatalogue,
/// RepriseReglages) ne sont PAS des routes : ce sont des gestes d'exploitation sur
/// les instances des greffes (docs/reconstruction/socle-greffes.md).
/// </summary>
internal sealed class ModuleGreffes : IModuleServeur
{
    /// <summary>Delai au-dela duquel un chemin reseau est declare injoignable (CA05).</summary>
    private static readonly TimeSpan DelaiChemin = TimeSpan.FromSeconds(5);

    /// <summary>Familles du journal des gestes (soc.FamilleJournal) : la fiche du catalogue
    /// (migration SOC-007_journal.sql) et un reglage du greffe (SOC-016_gestes.sql).</summary>
    internal const string FamilleGreffe = "greffe", FamilleReglage = "reglage";

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
                                                   ServiceDroits droits, BaseRig b, CancellationToken ct) =>
        {
            if (Auteur(http) is not { } auteur || await droits.AppelantAsync(http, b, ct) is not { } moi) return SansAuteur();
            if (!ServiceDroits.PeutModifierLeGreffe(moi)) return SansLeDroit(moi);
            // Heure LOCALE du greffe, sans decalage ni « Z » : « 2027-03-01T08:00 » (CA05).
            if (!DateTime.TryParseExact(saisie.PassageLocal, ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                return Refus(StatusCodes.Status400BadRequest,
                    $"Date de passage illisible : « {saisie.PassageLocal} ». Forme attendue : 2027-03-01T08:00, heure locale du greffe, sans decalage.");
            return await Modifier(() => c.SaisirPassageAsync(code, local, auteur,
                JournalDeLaFiche(moi, saisie.Ecran, code, f => new { passageLocal = f.PassageLocal }), ct));
        });

        greffes.MapPut(RoutesGreffes.Fuseau, async (string code, SaisieFuseau saisie, HttpContext http, CatalogueGreffes c,
                                                  ServiceDroits droits, BaseRig b, CancellationToken ct) =>
        {
            if (Auteur(http) is not { } auteur || await droits.AppelantAsync(http, b, ct) is not { } moi) return SansAuteur();
            if (!ServiceDroits.PeutModifierLeGreffe(moi)) return SansLeDroit(moi);
            return await Modifier(() => c.SaisirFuseauAsync(code, saisie.Fuseau, auteur,
                JournalDeLaFiche(moi, saisie.Ecran, code, f => new { fuseau = f.Fuseau }), ct));
        });

        greffes.MapGet(RoutesGreffes.Base, async (string code, CatalogueGreffes c, CancellationToken ct) =>
            await SurBaseV3(c, code, ct, async cn =>
            {
                var fiche = (await c.LireAsync(code, ct))!;
                var (jouees, derniere) = await Migrateur.EtatJournalAsync(cn, ct);
                return Results.Ok(new EtatBaseGreffe(code, fiche.Serveur, fiche.BaseV3, true, jouees, derniere, null));
            }));

        greffes.MapGet(RoutesGreffes.Reglages, async (string greffe, CatalogueGreffes c, CancellationToken ct) =>
            await SurBaseV3(c, greffe, ct, async cn => Results.Ok(await LireReglagesAsync(cn, null, null, ct))));

        greffes.MapGet(RoutesGreffes.Reglage, async (string greffe, string reglage, CatalogueGreffes c, CancellationToken ct) =>
            await SurBaseV3(c, greffe, ct, async cn =>
                (await LireReglagesAsync(cn, null, reglage, ct)).FirstOrDefault() is { } r ? Results.Ok(r) : ReglageAbsent(greffe, reglage)));

        // CA07 : comme Parametrage.UpdateValeurParametrage (Parametrage.cs:103-121) -- l'auteur
        // (le NOM de l'agent, AuteurReglage) et la date sont poses avec la valeur ; un reglage
        // absent ne se cree pas (404). L'agent est celui que la politique AgentAdmis vient de
        // juger dans la base de CE greffe : DeLAppelantAsync le reprend de la requete.
        // fix-ok: la route ecrivait le compte de la session (« AMITEL\banc.socle », banc rouge du 2026-11-21) au lieu du nom UTLST_NOM de la V2.
        // fix-ok: pas un correctif -- lot 14 (27/11/2026, ordre du chef msg-cab1a60f) : droit, UPDATE et relecture extraits dans
        // EcritureReglage.ModifierAsync, appelee aussi par le geste « Codes chefs IP » ; le journal reste ecrit ici, avec l'ecran de la saisie.
        greffes.MapPut(RoutesGreffes.Reglage, async (string greffe, string reglage, SaisieReglage saisie, HttpContext http,
                                                   CatalogueGreffes c, ServiceIdentite identites, ServiceDroits droits, BaseRig b,
                                                   CancellationToken ct) =>
        {
            if (AuteurReglage.De(await identites.DeLAppelantAsync(http, b, ct), http.User) is not { } auteur)
                return SansAuteur();
            return await SurBaseV3(c, greffe, ct, async cn =>
            {
                if (await droits.AppelantAsync(http, b, cn, ct) is not { } moi) return SansAuteur();
                await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
                // Le droit, l'UPDATE et la relecture : EcritureReglage, la seule ecriture d'un reglage (un geste d'ecran l'appelle aussi).
                switch (await EcritureReglage.ModifierAsync(cn, tx, moi, auteur, reglage, saisie.Valeur, ct))
                {
                    case ReglageEcrit.SansLeDroit refus: return Refus(StatusCodes.Status403Forbidden, refus.Motif);
                    case ReglageEcrit.Modifie ecrit:
                        var t = ecrit.Trace;
                        await JournalGestes.EcrireAsync(cn, tx, moi, saisie.Ecran, t.Famille, t.Objet, t.IdObjet, t.Verbe, t.Avant, t.Apres, ct);
                        await tx.CommitAsync(ct);
                        return Results.Ok(ecrit.Apres);
                    default: return ReglageAbsent(greffe, reglage);
                }
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

    /// <summary>Le geste d'une modification de la fiche, ecrit au journal de la base commune dans la
    /// transaction du catalogue (<see cref="CatalogueGreffes.SaisirPassageAsync"/>) : avant et apres de
    /// la seule valeur changee.</summary>
    private static EcritureDuGeste JournalDeLaFiche(Appelant moi, string? ecran, string code, Func<FicheGreffe, object> valeur)
        => (cn, tx, avant, apres, ct) => JournalGestes.EcrireAsync(cn, tx, moi, ecran, FamilleGreffe, $"la fiche du greffe {code}", code,
            "a modifie", valeur(avant), valeur(apres), ct);

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

    internal static async Task<List<ReglageGreffe>> LireReglagesAsync(SqlConnection cn, SqlTransaction? tx, string? code, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            "SELECT Code, Libelle, Valeur, Utilisation, AuteurCreation, DateCreation, AuteurModif, DateModif FROM soc.Reglage " +
            (code is null ? "ORDER BY Code;" : "WHERE Code = @c;"), cn, tx);
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

    /// <summary>403 : l'agent est admis mais n'a pas le droit de modifier le greffe ; rien n'a ete lu ni ecrit.</summary>
    private static IResult SansLeDroit(Appelant moi) => Refus(StatusCodes.Status403Forbidden, MotifSansLeDroit(moi));

    /// <summary>Pourquoi <paramref name="moi"/> ne modifie pas le greffe (sa fiche, un reglage) : la route et un geste d'ecran le disent pareil.</summary>
    internal static string MotifSansLeDroit(Appelant moi)
        => $"Modification refusee : le compte {moi.CompteWindows} n'a pas le droit de modifier le greffe (il faut etre {ServiceDroits.QuiPeutModifierLeGreffe}).";

    private static IResult Refus(int statut, string motif) => Results.Json(new RefusGreffe(motif), statusCode: statut);
}
