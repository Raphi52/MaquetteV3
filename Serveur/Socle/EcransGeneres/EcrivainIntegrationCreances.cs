using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Intégrer des créances » (PROC_INTEGCREANCE) : les creances transitoires en attente d'integration (LecteurCreancesTransitoires.cs,
/// CreancesTransitoires.Requete(true)), une File. Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026.
/// GROUPES : le mode de la creance (Admissions, Contestations, Défauts de réponse : les trois onglets de la V2,
/// eModeIntegrationCreances). ETAT : sans erreur -> AFaire « À intégrer » ; avec CRTRA_NATURE_ERREUR -> EnCours « En erreur » (elle
/// se re-integre apres correction, IntegrationCreances.cs:83 remet l'erreur a vide) ; une creance integree sort de la liste.
/// </summary>
internal sealed class LecteurIntegrationCreances : ILecteurEcran
{
    public string Procedure => "PROC_INTEGCREANCE";

    internal static string GroupeDe(string mode) => mode switch
    {
        CreancesTransitoires.Contestation => "Contestations",
        CreancesTransitoires.DefautReponse => "Défauts de réponse",
        _ => "Admissions",
    };

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, CreancesTransitoires.Requete(enAttenteSeulement: true), r => new LigneLue(
                CreancesTransitoires.Nom(r), CreancesTransitoires.Precisions(r), GroupeDe(CreancesTransitoires.Groupe(r)),
                FormeLigne.Texte(r, "CRTRA_NATURE_ERREUR").Length > 0 ? "EnCours" : "AFaire", [])
            { Id = FormeLigne.Texte(r, "CRTRA_ID_CRTRA") }, ct, new SqlParameter("@plafond", plafond));
}

/// <summary>
/// « Intégrer des créances » (PROC_INTEGCREANCE) : INTEGRER_SELECTIONNEES, sur les creances cochees. Mode d'emploi : EcrivainEcran.cs ;
/// l'integration d'une creance : CreancesPcV2.cs.
///
/// SOURCE (ancien RIG) : IntegrationCreances.GenereCreances (INTCREA\1 Integration des Creances Transitoires\IntegrationCreances.cs:75-
/// 539), appelee sur la selection de l'onglet (une procedure collective par integration, Detail OPE de proc_integcreance.md) :
///   - une creance a la fois ; une erreur sur l'une n'arrete pas les autres (« continue », :111-516) : son erreur est ECRITE dans
///     CRTRA_NATURE_ERREUR (SauveImportation, CreanceTransitoire.cs:1065-1067) et le rapport final compte reussites et erreurs ;
///   - la V3 integre les ADMISSIONS (CreancesPcV2) ; ce qui cree une instance (contestation, defaut de reponse, etat 6, PC d'avant le
///     15/02/2009) est SAUTE et nomme dans le message, sans rien ecrire pour lui (ce n'est pas une erreur de la V2) ;
///   - rien d'integre ni d'erreur ecrite : refus (le socle n'ecrit rien).
/// NON REPRIS (nomme) : l'import du fichier des mandataires (OPE_INTEGRER_INTEGCREANCE : un fichier du poste), les statistiques et
/// le courriel des erreurs (Outils.SendMailJudHL), et ce que CreancesPcV2 nomme.
/// </summary>
internal sealed class EcrivainIntegrationCreances : IEcrivainEcran
{
    public string Procedure => "PROC_INTEGCREANCE";
    public string Ecran => "Intégrer des créances";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var ids = geste.Demande.Ids.Select(i => int.TryParse(i, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
                                   .Where(n => n > 0).Distinct().Order().ToList();
        if (ids.Count == 0) return IssueGeste.Refuser("Aucune créance cochée : rien n'est intégré.");
        var traces = new List<TraceGeste>();
        var integrees = new List<string>();
        var erreurs = new List<string>();
        var sautees = new List<string>();
        // Dans l'ordre des identifiants, comme la V2 (OrderBy CRTRA_ID_CRTRA, IntegrationCreances.cs:107).
        foreach (var id in ids)
        {
            if (await CreancesPcV2.TransitoireAsync(geste, id, ct) is not { } t) { sautees.Add($"n° {id} (déjà intégrée ou absente)"); continue; }
            var nom = t.Nom.Length > 0 ? t.Nom : $"la créance transitoire n° {id}";
            var (creance, refus, natureErreur) = await CreancesPcV2.IntegrerAdmissionAsync(geste, t, ct);
            if (creance is not null)
            {
                integrees.Add($"{nom} (créance n° {creance.NumeroOrdre} de la PC {creance.NumPc})");
                traces.Add(CreancesPcV2.Trace(t, creance));
            }
            else if (natureErreur is not null)
            {
                await geste.EcrireAsync("UPDATE v2.CREANCE_TRANSITOIRE SET CRTRA_NATURE_ERREUR = @e WHERE CRTRA_ID_CRTRA = @id", ct,
                    new SqlParameter("@e", natureErreur), new SqlParameter("@id", id));
                erreurs.Add($"{nom} : {natureErreur}");
                traces.Add(new TraceGeste("affaire", $"la créance transitoire n° {id} ({nom})", id.ToString(CultureInfo.InvariantCulture), "a modifie",
                    new { Erreur = (string?)null }, new { Erreur = natureErreur }));
            }
            else sautees.Add($"{nom} : {refus}");
        }
        if (traces.Count == 0)
            return IssueGeste.Refuser("Aucune créance intégrée : " + string.Join(" ; ", sautees));
        var message = $"{integrees.Count} créance{(integrees.Count > 1 ? "s" : "")} intégrée{(integrees.Count > 1 ? "s" : "")} dans la base du greffe" +
                      (integrees.Count > 0 ? " : " + string.Join(", ", integrees) : "") +
                      $" ; {erreurs.Count} créance{(erreurs.Count > 1 ? "s" : "")} en erreur" + (erreurs.Count > 0 ? " : " + string.Join(", ", erreurs) : "") + ".";
        if (sautees.Count > 0) message += " Non intégrées par la V3 : " + string.Join(" ; ", sautees);
        return IssueGeste.Ecrit(message, [.. ids.Select(i => i.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }
}
