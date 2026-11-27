using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// Les routes des ECRANS FAITS MAIN qui lisaient la demonstration (plan « RIG v3 100 % fonctionnel », lot 17) : Demandes,
/// Arrivee du courrier et Teletransmissions, BODACC, dossiers deja ouverts. Chaque lecture est dans son fichier (Lecture*.cs),
/// sources V2 citees. La base est la base V3 du greffe de l'adresse, ouverte par le catalogue commun (aucune chaine ici, aucun
/// repli sur une autre base) ; l'agent y est admis (PolitiquesSocle.AgentAdmis), comme pour les ecrans generes
/// (ModuleEcransGeneres) : le menu filtre deja les ecrans par profil.
/// </summary>
internal sealed class ModuleEcransMain : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
    {
        routes.MapPost(RoutesEcransMain.Demandes, (string greffe, RechercheDemandes recherche, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureDemandes.LireAsync(cn, recherche, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        routes.MapPost(RoutesEcransMain.Arrivees, (string greffe, RechercheArrivees recherche, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureArrivees.LireAsync(cn, recherche, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        routes.MapGet(RoutesEcransMain.Bodacc, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureBodacc.LireAsync(cn, DateTime.Now, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        routes.MapGet(RoutesEcransMain.DossiersOuverts, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureDossiersOuverts.LireAsync(cn, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        // Lot 5 : « Dépôts judiciaires » (LectureDepotsJud.cs).
        routes.MapGet(RoutesDepotsJud.Instances, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureDepotsJud.LireAsync(cn, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        // Lot 7 : « Créer une audience » (LectureAudiences.cs) et « Gestion des audiences » (LectureGestionAudiences.cs).
        routes.MapGet(RoutesAudiences.Programmees, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureAudiences.LireAsync(cn, DateTime.Now, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
        routes.MapGet(RoutesGestionAudiences.Supprimees, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureGestionAudiences.LireAsync(cn, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        // fix-ok: pas un correctif -- le lot 6 du plan-676a4b19 ajoute une route par ecran fait main branche (consultation
        // judiciaire, actes, affaires, requisitions), une edition par ecran ; chacune prouvee par EcransMainBaseCheck. Puis
        // la route du XML : le controle final a mesure que RPJCE_FLUX est dans DEMAT_RPJC_ENR (DEMAT_RPJC_ENR.sql:15), que
        // l'ecran disait « non relu » a tort.
        // Lot 6 : « Consultation judiciaire » (LectureConsultationJud.cs).
        routes.MapGet(RoutesConsultationJud.Enrolements, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureConsultationJud.LireAsync(cn, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
        routes.MapGet(RoutesConsultationJud.Xml, (string greffe, int id, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureConsultationJud.LireXmlAsync(cn, id, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        // Lot 6 : « Actes » (LectureActes.cs) ; « aujourd'hui - 2 jours » se compte au jour du serveur.
        routes.MapPost(RoutesActes.Actes, (string greffe, RechercheActes recherche, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureActes.LireAsync(cn, recherche, DateTime.Today, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        // Lot 6 : « Recherche d'affaires » (LectureAffaires.cs).
        routes.MapPost(RoutesAffaires.Affaires, (string greffe, RechercheAffaires recherche, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureAffaires.LireAsync(cn, recherche, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        // Lot 10 : « Rédaction d'une décision » (LectureRedaction.cs), les décisions des instances que RIG v2 rédige.
        routes.MapGet(RoutesRedaction.Decisions, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureRedaction.LireAsync(cn, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
        // Lot 9 : « Dossiers verrouillés » (LectureVerrous.cs), les demandes qui verrouillent leurs objets.
        routes.MapGet(RoutesVerrous.Verrous, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureVerrous.LireAsync(cn, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
        // Lot 11 : « GED judiciaire » (LectureGedJud.cs) et « Documents dématérialisés » (LectureDocDemat.cs).
        routes.MapGet(RoutesGedJud.Documents, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureGedJud.LireAsync(cn, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
        routes.MapGet(RoutesDocDemat.Pieces, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureDocDemat.LireAsync(cn, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
        // Lot 12 : « Gestion des banques » (LectureBanques.cs).
        routes.MapGet(RoutesBanques.Banques, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureBanques.LireAsync(cn, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
        // Lot 12 : « Encaissements » et « Factures » ouverts pour un dossier (LectureEncaissements.cs, LectureFactures.cs).
        routes.MapGet(RoutesEncaissements.Depart, (string greffe, string dossier, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureEncaissements.LireAsync(cn, dossier, DateTime.Now, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
        routes.MapGet(RoutesFactures.Demande, (string greffe, string dossier, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(new DemandeDuDossierLue(await LectureFactures.LireAsync(cn, dossier, DateTime.Now, ct)))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
        // Lot 9 : « Statistiques » (LectureStatistiques.cs), les volets de RIG v2 calcules dans la base.
        routes.MapPost(RoutesStatistiques.Statistiques, (string greffe, RechercheStatistiques recherche, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureStatistiques.LireAsync(cn, recherche, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
        // Lot 6 : « Réquisitions », la recherche des entreprises (LectureEntreprisesRequi.cs).
        routes.MapPost(RoutesRequisitions.Entreprises, (string greffe, RechercheEntreprises recherche, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureEntreprisesRequi.LireAsync(cn, recherche, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
    }

    /// <summary><paramref name="faire"/> sur la base V3 du greffe ; 404 / 503 de la politique si elle ne s'ouvre plus.</summary>
    private static async Task<IResult> SurLaBase(CatalogueGreffes c, string greffe, CancellationToken ct, Func<SqlConnection, Task<IResult>> faire)
    {
        SqlConnection cn;
        try { cn = await c.OuvrirBaseV3Async(greffe, ct); }
        catch (GreffeInconnu e) { return ReponseGreffeNonOuvert.Pour(e); }
        catch (BaseGreffeInjoignable e) { return ReponseGreffeNonOuvert.Pour(e); }
        await using (cn) return await faire(cn);
    }
}
