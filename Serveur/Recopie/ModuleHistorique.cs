using Microsoft.Data.SqlClient;
using RigV3.Contrats.Recopie;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;
using RigV3.Serveur.Socle.Supervision;

namespace RigV3.Serveur.Recopie;

/// <summary>
/// LES ROUTES DU LOT 11 (REC-007, REC-009, REC-010, REC-011, REC-018) : l'historique des affaires des deux logiciels, les
/// demandes et leurs liens, les arrivees, les anciens numeros -- en LECTURE, pour tout agent admis du greffe (rien ne s'y
/// modifie : une ancienne affaire se traite dans l'ancien RIG, la base le refuse, REC-010 CA03) ; et le retour d'un greffe
/// beta a l'ancien RIG, reserve a l'exploitation d'Amitel, dont la decision n'est prise que par la personne designee (CA08).
/// Chaque geste du retour (designation, regle de type, decision) va au journal des gestes (SOC-016).
/// </summary>
internal sealed class ModuleHistorique : IModuleServeur
{
    public const string NomEcran = "Historique de l'ancien RIG";
    public const string NomEcranRetour = "Retour à l'ancien RIG (exploitation)";

    /// <summary>La chaine de la base des echanges (EDI_GREFFE), en lecture : les envois et receptions d'une ancienne affaire
    /// (REC-010 CA04). Absente : l'historique le dit, sans echanges.</summary>
    public const string ReglageEchanges = "Recopie:BaseEchanges";

    public void Inscrire(IServiceCollection services, IConfiguration configuration) { }

    public void Brancher(IEndpointRouteBuilder routes)
    {
        var agents = routes.MapGroup(string.Empty).RequireAuthorization(PolitiquesSocle.AgentAdmis);

        agents.MapGet(RoutesHistorique.Historique, async (string greffe, string? critere, string? valeur, CatalogueGreffes c, IConfiguration config,
                                                         CancellationToken ct) =>
        {
            if (critere is not ("siren" or "gestion" or "client" or "arrivee") || string.IsNullOrWhiteSpace(valeur))
                return Results.BadRequest(new { motif = "Critère « siren », « gestion », « client » ou « arrivee », et une valeur." });
            return await SurBaseDuGreffe(c, greffe, ct, async (cn, baseV2) =>
                Results.Ok(await HistoriqueAffaires.LireAsync(cn, baseV2, greffe, critere, valeur, config[ReglageEchanges], ct)));
        });

        agents.MapGet(RoutesHistorique.Demande, async (string greffe, int id, CatalogueGreffes c, CancellationToken ct) =>
            await SurBaseDuGreffe(c, greffe, ct, async (cn, baseV2) =>
                await HistoriqueAffaires.DemandeAsync(cn, baseV2, id, ct) is { } d ? Results.Ok(d)
                    : Results.NotFound(new { motif = $"La demande {id} n'existe pas dans le greffe {greffe}." })));

        agents.MapGet(RoutesHistorique.Arrivee, async (string greffe, string numero, CatalogueGreffes c, CancellationToken ct) =>
            await SurBaseDuGreffe(c, greffe, ct, async (cn, baseV2) =>
                await HistoriqueAffaires.ArriveeAsync(cn, baseV2, numero, ct) is { } a ? Results.Ok(a)
                    : Results.NotFound(new { motif = $"Aucune arrivée n° {numero} dans le greffe {greffe}." })));

        agents.MapGet(RoutesHistorique.AnciensNumeros, async (string greffe, string? numero, string? rig, CatalogueGreffes c, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(numero) && string.IsNullOrWhiteSpace(rig))
                return Results.BadRequest(new { motif = "Saisir un ancien numéro ou un numéro RIG pour faire la recherche." });
            return await SurBaseDuGreffe(c, greffe, ct, async (cn, baseV2) => Results.Ok(await AnciensNumeros.ChercherAsync(cn, baseV2, numero, rig, ct)));
        });

        agents.MapGet(RoutesHistorique.AnciennesReprises, async (string greffe, CatalogueGreffes c, CancellationToken ct) =>
            await SurBaseDuGreffe(c, greffe, ct, async (cn, baseV2) => Results.Ok(await AnciensNumeros.AnciennesReprisesAsync(cn, baseV2, ct))));

        BrancherRetour(routes);
    }

    /// <summary>Le retour (REC-007) : exploitation d'Amitel (agent admis marque Amitel, comme la supervision et les changements
    /// mis de cote). La decision elle-meme n'est prise que par la personne designee : RetourBeta la refuse a tout autre compte,
    /// et l'enregistre (CA08).</summary>
    private static void BrancherRetour(IEndpointRouteBuilder routes)
    {
        var exploitation = new[] { PolitiquesSocle.AgentAdmis, PolitiquesSupervision.ExploitationAmitel };

        routes.MapGet(RoutesHistorique.Retour, async (string greffe, CatalogueGreffes c, CancellationToken ct) =>
                await SurBaseDuGreffe(c, greffe, ct, async (cn, baseV2) => Results.Ok(await RetourBeta.PreparerAsync(cn, baseV2, greffe, ct))))
            .RequireAuthorization(exploitation);

        routes.MapPut(RoutesHistorique.ResponsableRetour, async (string greffe, DesignationRetour d, HttpContext http, CatalogueGreffes c, ServiceDroits droits,
                                                                BaseRig commune, CancellationToken ct) =>
                await SurBaseDuGreffe(c, greffe, ct, async (cn, baseV2) =>
                {
                    var moi = await droits.AppelantAsync(http, commune, cn, ct);
                    if (moi is null) return Results.Json(new { motif = "Agent inconnu dans la base de ce greffe." }, statusCode: StatusCodes.Status403Forbidden);
                    if (string.IsNullOrWhiteSpace(d.Compte)) return Results.BadRequest(new { motif = "Le compte de la personne désignée est obligatoire." });
                    await using var tx = cn.BeginTransaction();
                    var avant = await EcrituresRecopie.ScalaireAsync(cn, tx, "SELECT Compte FROM rec.ResponsableRetour WHERE BaseV2 = @b;", ct, ("@b", baseV2));
                    await RetourBeta.DesignerAsync(cn, tx, baseV2, d.Compte, moi.NomAffiche, ct);
                    await JournalGestes.EcrireAsync(cn, tx, moi, NomEcranRetour, "reglage", $"personne désignée pour le retour à l'ancien RIG du greffe {greffe}",
                                                    baseV2, "a modifie", new { compte = avant }, new { compte = d.Compte.Trim() }, ct);
                    await tx.CommitAsync(ct);
                    return Results.Ok(await RetourBeta.PreparerAsync(cn, baseV2, greffe, ct));
                }))
            .RequireAuthorization(exploitation);

        routes.MapPut(RoutesHistorique.TypeRetour, async (string greffe, RegleTypeRetourDemandee r, HttpContext http, CatalogueGreffes c, ServiceDroits droits,
                                                         BaseRig commune, CancellationToken ct) =>
                await SurBaseDuGreffe(c, greffe, ct, async (cn, baseV2) =>
                {
                    var moi = await droits.AppelantAsync(http, commune, cn, ct);
                    if (moi is null) return Results.Json(new { motif = "Agent inconnu dans la base de ce greffe." }, statusCode: StatusCodes.Status403Forbidden);
                    await using var tx = cn.BeginTransaction();
                    try
                    {
                        await RetourBeta.ChoisirTypeAsync(cn, tx, baseV2, r.IdTypeV3, r.IdTypeV2, moi.NomAffiche, ct);
                    }
                    catch (RetourImpossible e)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.BadRequest(new { motif = e.Message });
                    }
                    await JournalGestes.EcrireAsync(cn, tx, moi, NomEcranRetour, "reglage", $"type de demande {r.IdTypeV3} de la V3 au retour à l'ancien RIG",
                                                    r.IdTypeV3.ToString(System.Globalization.CultureInfo.InvariantCulture), "a modifie", null, new { typeV2 = r.IdTypeV2 }, ct);
                    await tx.CommitAsync(ct);
                    return Results.Ok(await RetourBeta.PreparerAsync(cn, baseV2, greffe, ct));
                }))
            .RequireAuthorization(exploitation);

        routes.MapPost(RoutesHistorique.Retour, async (string greffe, DemandeRetour d, HttpContext http, CatalogueGreffes c, ServiceDroits droits,
                                                      BaseRig commune, ILoggerFactory journaux, CancellationToken ct) =>
        {
            var fiche = await c.LireAsync(greffe, ct);
            if (fiche is null) return Results.NotFound(new { motif = $"Le greffe {greffe} n'est pas au catalogue des greffes." });
            return await SurBaseDuGreffe(c, greffe, ct, async (cn, baseV2) =>
            {
                var moi = await droits.AppelantAsync(http, commune, cn, ct);
                if (moi is null) return Results.Json(new { motif = "Agent inconnu dans la base de ce greffe." }, statusCode: StatusCodes.Status403Forbidden);
                var bilan = await RetourBeta.RevenirAsync(c.ConnexionBase(fiche.Serveur, fiche.BaseV3), baseV2, greffe, moi.CompteWindows, d.Motif,
                                                          journaux.CreateLogger("RigV3.Recopie.Retour"), ct);
                await using (var tx = cn.BeginTransaction())
                {
                    await JournalGestes.EcrireAsync(cn, tx, moi, NomEcranRetour, "affaire", $"retour à l'ancien RIG du greffe {greffe} ({bilan.Etat})",
                                                    bilan.IdDecision.ToString(System.Globalization.CultureInfo.InvariantCulture), "a valide", null,
                                                    new { decision = bilan.IdDecision, etat = bilan.Etat, motif = d.Motif, bilan.Affaires, bilan.Demandes }, ct);
                    await tx.CommitAsync(ct);
                }
                // Une decision refusee est enregistree (CA08) et rendue en 403 : l'appelant n'est pas la personne designee, ou
                // les conditions du retour ne sont pas reunies (409).
                return bilan.Etat switch
                {
                    "faite" => Results.Ok(bilan),
                    "refusee" when bilan.Detail?.Contains("personne désignée", StringComparison.Ordinal) == true
                                   || bilan.Detail?.Contains("personne n'est désignée", StringComparison.Ordinal) == true
                        => Results.Json(bilan, statusCode: StatusCodes.Status403Forbidden),
                    _ => Results.Json(bilan, statusCode: StatusCodes.Status409Conflict),
                };
            });
        }).RequireAuthorization(exploitation);
    }

    /// <summary>La base V3 du greffe et sa base V2 (celle de la recopie, rec.Remontee) ; 409 si la recopie n'y est pas en service.</summary>
    private static async Task<IResult> SurBaseDuGreffe(CatalogueGreffes c, string greffe, CancellationToken ct, Func<SqlConnection, string, Task<IResult>> travail)
    {
        SqlConnection cn;
        try { cn = await c.OuvrirBaseV3Async(greffe, ct); }
        catch (GreffeInconnu e) { return ReponseGreffeNonOuvert.Pour(e); }
        catch (BaseGreffeInjoignable e) { return ReponseGreffeNonOuvert.Pour(e); }
        await using (cn)
        {
            var baseV2 = await EcrituresRecopie.ScalaireAsync(cn, null, "IF OBJECT_ID(N'rec.Remontee') IS NOT NULL SELECT TOP (1) BaseV2 FROM rec.Remontee;", ct);
            if (baseV2 is null)
                return Results.Json(new { motif = $"La recopie entre l'ancien RIG et la V3 n'est pas en service pour le greffe {greffe} : rien à montrer de l'ancien RIG." },
                                    statusCode: StatusCodes.Status409Conflict);
            return await travail(cn, baseV2);
        }
    }
}
