using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.Aiguillage;

/// <summary>
/// L'aiguillage par affaire (SOC-014 T01 ; lot 3 de la phase 2) : pour chaque affaire, le logiciel qui la
/// traite, d'apres la SEULE comparaison de sa date de creation avec la date de passage de son greffe au
/// catalogue commun (SOC-007, <see cref="CatalogueGreffes"/>). La regle est
/// <see cref="RegleAiguillage.Aiguiller"/>, partagee avec la demonstration de l'ecran.
///
/// - une affaire existante : sa date de creation decide (CA02 : avant le passage, V2) ;
/// - aucune affaire visee (nouvelle affaire, nouvelle formalite, arrivee sans affaire) : l'affaire serait
///   creee MAINTENANT, a l'heure locale du greffe (CA01, CA03, CA05) ;
/// - aucun domaine, aucune nature dans la question : il n'y a rien sur quoi brancher une regle par
///   domaine (CA06, Q18).
///
/// Lecture seule : la route ne lit que la fiche du catalogue (base commune), aucune base de greffe ; elle
/// n'ecrit rien, d'ou l'absence de journal des gestes. ACCES : <see cref="PolitiquesSocle.AgentAdmis"/>,
/// l'agent juge dans la base commune comme pour la fiche du greffe (401 sans session, 403 compte refuse).
/// </summary>
internal sealed class ModuleAiguillage : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
    {
        var aiguillage = routes.MapGroup(string.Empty).RequireAuthorization(PolitiquesSocle.AgentAdmis);

        aiguillage.MapPost(RoutesAiguillage.Aiguiller, async (string code, QuestionAiguillage? question,
                                                            CatalogueGreffes catalogue, CancellationToken ct) =>
        {
            if (question?.Affaires is not { } affaires || affaires.Any(a => a is null || a.Numero is null))
                return Refus(StatusCodes.Status400BadRequest, "Question illisible : une liste d'affaires (numero, date de creation ou null) est attendue.");
            if (affaires.Count > RoutesAiguillage.AffairesParQuestion)
                return Refus(StatusCodes.Status400BadRequest,
                    $"{affaires.Count} affaires dans une seule question : {RoutesAiguillage.AffairesParQuestion} au plus.");
            if (await catalogue.LireAsync(code, ct) is not { } fiche)
                return Refus(StatusCodes.Status404NotFound, $"Le greffe « {code} » n'est pas au catalogue commun.");

            // L'heure du greffe, pas celle du serveur : un greffe d'outre-mer passe a SON heure (SOC-007 CA05).
            DateTime? maintenant = fiche.Fuseau is null ? null : Fuseaux.MaintenantLocal(fiche.Fuseau);
            var reponses = affaires
                .Select(a => RegleAiguillage.Aiguiller(a.Numero, fiche.PassageLocal, a.CreeeLe, maintenant))
                .ToList();
            return Results.Ok(new ReponseAiguillage(fiche.Code, fiche.PassageLocal, maintenant, reponses));
        });
    }

    private static IResult Refus(int statut, string motif) => Results.Json(new RefusGreffe(motif), statusCode: statut);
}
