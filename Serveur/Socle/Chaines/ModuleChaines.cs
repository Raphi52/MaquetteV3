using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.Chaines;

/// <summary>
/// SOC-018 : les routes des chaines signees d'un greffe (le greffe dans l'adresse, base ouverte par le catalogue).
///
/// QUI. Toutes portent <see cref="PolitiquesSocle.AgentAdmis"/>. La consultation, le controle, les clotures et la purge
/// sont aux COMPTABLES du greffe (CA07 : un agent qui a au moins une fonction de facturation ou d'encaissement,
/// soc.Fonction.Comptabilite, le domaine Comptabilite de la V2) et a Amitel ; l'ouverture, geste du passage, a Amitel
/// seul. Ecart avec la V2, a valider : ses ecrans PROC_CERTIF ne sont ouverts qu'au role ADMINAMITEL
/// (docs/reconstruction/socle-chaines.md). Un refus ne lit ni n'ecrit rien.
/// </summary>
internal sealed class ModuleChaines : IModuleServeur
{
    public void Inscrire(IServiceCollection services, IConfiguration configuration)
    {
        // La cle de chaque greffe dans le magasin CNG de Windows (non exportable). Un banc inscrit la sienne avant
        // (TryAdd) : jamais de cle de banc dans le profil de la personne qui le lance.
        services.TryAddSingleton<ICoffreCles>(_ => OperatingSystem.IsWindows()
            ? new CoffreClesCng()
            : throw new PlatformNotSupportedException("La partie serveur de la V3 signe avec une cle CNG de Windows (SOC-018)."));
        services.AddSingleton<ServiceChaines>();
        services.AddSingleton<Acces>();
        services.AddSingleton<SignatureDeFond>();
        services.AddHostedService(sp => sp.GetRequiredService<SignatureDeFond>());
    }

    public void Brancher(IEndpointRouteBuilder routes)
    {
        var chaines = routes.MapGroup(string.Empty).RequireAuthorization(PolitiquesSocle.AgentAdmis);

        chaines.MapGet(RoutesChaines.Etat, (string greffe, HttpContext h, Acces a, CancellationToken ct)
            => a.SurLaBase(h, greffe, Qui.Comptable, ct, (cn, _) => Ok(LectureChaines.EtatAsync(cn, greffe, ct))));

        chaines.MapGet(RoutesChaines.Codes, (string greffe, HttpContext h, Acces a, CancellationToken ct)
            => a.SurLaBase(h, greffe, Qui.Comptable, ct, (cn, _) => Ok(LectureChaines.CodesAsync(cn, ct))));

        chaines.MapGet(RoutesChaines.Journal, (string greffe, string? partie, short? code, string? du, string? au, HttpContext h, Acces a,
                                               CancellationToken ct) =>
        {
            if (!Jour(du, out var debut) || !Jour(au, out var fin))
                return Task.FromResult(Results.BadRequest(new RefusGreffe("Dates attendues au format aaaa-mm-jj.")));
            return a.SurLaBase(h, greffe, Qui.Comptable, ct, (cn, _) => Ok(LectureChaines.JournalAsync(cn, partie, code, debut, fin, ct)));
        });

        chaines.MapGet(RoutesChaines.Totaux, (string greffe, string? chaine, HttpContext h, Acces a, CancellationToken ct)
            => a.SurLaBase(h, greffe, Qui.Comptable, ct, (cn, _) => Ok(LectureChaines.TotauxAsync(cn, chaine, ct))));

        chaines.MapPost(RoutesChaines.Ouverture, (string greffe, HttpContext h, Acces a, CatalogueGreffes catalogue, ServiceChaines service,
                                                  CancellationToken ct)
            => a.SurLaBase(h, greffe, Qui.Amitel, ct, async (cn, moi) =>
            {
                var fiche = await catalogue.LireAsync(greffe, ct);
                var fuseau = fiche?.Fuseau ?? Fuseaux.ParDefaut(greffe, fiche?.EstOutreMer ?? false)
                             ?? throw new RefusChaines($"Le greffe {greffe} n'a pas de fuseau : le saisir avant d'ouvrir ses chaînes.");
                return Results.Ok(await service.OuvrirAsync(cn, greffe, fuseau, moi.CompteWindows,
                    (c, t, x) => JournalGestes.EcrireAsync(c, t, moi, null, Famille, "les chaînes signées de la V3", greffe, "a ouvert", null,
                        new { greffe, fuseau, message = "Chaînes ouvertes vides, événement 260 en premier maillon ; les chaînes de la V2 restent dans la V2." }, x),
                    ct));
            }));

        chaines.MapPost(RoutesChaines.Controle, (string greffe, HttpContext h, Acces a, ServiceChaines service, CancellationToken ct)
            => a.SurLaBase(h, greffe, Qui.Comptable, ct, (cn, moi) => Ok(ControleIntegrite.ControlerAsync(service, cn, moi.CompteWindows, moi.Id, ct))));

        chaines.MapPost(RoutesChaines.Clotures, (string greffe, DemandeCloture demande, HttpContext h, Acces a, ServiceChaines service,
                                                 CancellationToken ct)
            => a.SurLaBase(h, greffe, Qui.Comptable, ct, (cn, moi) => Ok(service.CloturerAsync(cn, demande, moi.CompteWindows, moi.Id,
                (c, t, x) => JournalGestes.EcrireAsync(c, t, moi, null, Famille, $"la clôture {demande.Periode} du {demande.Date}", demande.Date,
                    "a valide", null, demande, x), ct))));

        chaines.MapPost(RoutesChaines.Purge, (string greffe, DemandePurge demande, HttpContext h, Acces a, ServiceChaines service,
                                              CancellationToken ct)
            => a.SurLaBase(h, greffe, Qui.Comptable, ct, (cn, moi) => Ok(service.PurgerAsync(cn, demande.Exercice, moi.CompteWindows, moi.Id,
                (c, t, x) => JournalGestes.EcrireAsync(c, t, moi, null, Famille, $"le journal purgeable de l'exercice {demande.Exercice}",
                    demande.Exercice.ToString(CultureInfo.InvariantCulture), "a supprime", null, demande, x), ct))));
    }

    /// <summary>La famille du journal des gestes (migration SOC-018_chaines.sql).</summary>
    private const string Famille = "certification";

    private static async Task<IResult> Ok<T>(Task<T> lecture) => Results.Ok(await lecture);

    private static bool Jour(string? texte, out DateOnly? jour)
    {
        jour = null;
        if (string.IsNullOrWhiteSpace(texte)) return true;
        if (!DateOnly.TryParseExact(texte.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var j)) return false;
        jour = j;
        return true;
    }
}

/// <summary>Qui peut faire le geste d'une route des chaines.</summary>
internal enum Qui { Comptable, Amitel }

/// <summary>Le controle d'acces des routes des chaines, sur la base du greffe de l'adresse.</summary>
internal sealed class Acces(CatalogueGreffes catalogue, ServiceDroits droits, BaseRig commune)
{
    public async Task<IResult> SurLaBase(HttpContext h, string greffe, Qui qui, CancellationToken ct,
                                         Func<SqlConnection, Appelant, Task<IResult>> travail)
    {
        SqlConnection cn;
        try { cn = await catalogue.OuvrirBaseV3Async(greffe, ct); }
        catch (GreffeInconnu e) { return ReponseGreffeNonOuvert.Pour(e); }
        catch (BaseGreffeInjoignable e) { return ReponseGreffeNonOuvert.Pour(e); }
        await using (cn)
        {
            var moi = await droits.AppelantAsync(h, commune, cn, ct);
            if (moi is null)
                return Results.Json(new RefusGreffe($"Refusé : vous n'êtes pas admis dans le greffe {greffe}."), statusCode: StatusCodes.Status403Forbidden);
            if (!moi.EstAmitel && (qui == Qui.Amitel || !await EstComptableAsync(cn, moi.Id, ct)))
                return Results.Json(new RefusGreffe(qui == Qui.Amitel
                    ? $"Refusé : l'ouverture des chaînes signées est un geste du passage, réservé à Amitel (compte {moi.CompteWindows})."
                    : $"Refusé : le compte {moi.CompteWindows} n'a aucune fonction de facturation ou d'encaissement dans le greffe {greffe} ; " +
                      "les chaînes signées sont consultées par les comptables du greffe."), statusCode: StatusCodes.Status403Forbidden);
            try { return await travail(cn, moi); }
            catch (ChainesNonOuvertes e) { return Results.Json(new RefusGreffe(e.Message), statusCode: StatusCodes.Status409Conflict); }
            catch (ChainesDejaOuvertes e) { return Results.Json(new RefusGreffe(e.Message), statusCode: StatusCodes.Status409Conflict); }
            catch (RefusChaines e) { return Results.Json(new RefusGreffe(e.Message), statusCode: StatusCodes.Status409Conflict); }
        }
    }

    /// <summary>Le compte a-t-il une fonction du domaine Comptabilite (facturation, encaissement) dans ce greffe ?</summary>
    private static async Task<bool> EstComptableAsync(SqlConnection cn, int idCompte, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT CASE WHEN EXISTS (SELECT 1 FROM soc.Droit d JOIN soc.Fonction f ON f.Code = d.Fonction
                                     WHERE d.IdCompte = @id AND f.Comptabilite = 1) THEN 1 ELSE 0 END;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = idCompte });
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }
}
