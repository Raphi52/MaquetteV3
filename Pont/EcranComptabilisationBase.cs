using System.Globalization;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// L'entrée de l'écran « Comptabilisation » (web/src/ecrans/EcranComptabilisation.tsx). Plan « RIG v3 100 % fonctionnel », lot 13
/// (27/11/2026), sur la marche du lot 17 (Pont/LectureEcranMain.cs).
///
/// Branché au serveur, l'écran montre l'arbre de RIG v2 : les journées à comptabiliser, leurs factures et leurs encaissements remis
/// ou non remis, ou le message bloquant de RIG v2 quand des factures n'ont pas de demande (Serveur/Socle/EcransMain/
/// LectureComptabilisation.cs, qui cite RIG v2). Les quatre traitements (comptabiliser les factures, les encaissements, remettre en
/// banque, lettrer les PV et REG) ne sont PAS branchés : ils écrivent la date de comptabilisation, les remis et le lettrage
/// (OPE_COMPTABILISATION.sauve, :253-360), non repris ici. SANS serveur : la démonstration (Exemple), marquée « exemple ». En panne :
/// le motif et aucune journée.
/// </summary>
public static partial class EcranComptabilisation
{
    [JSInvokable("EcranComptabilisation.Journees")]
    public static Task<VueComptabilisation> Journees() => JourneesAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Les journées du greffe <paramref name="greffe"/> (null serveur : la démonstration).</summary>
    public static async Task<VueComptabilisation> JourneesAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les journées à comptabiliser", (s, g) =>
            s.LireAsync<ComptabilisationLue>(RoutesEcransMain.Pour(RoutesComptabilisation.Journees, g)));
        if (lu.Mode == LectureEcransMain.Demo) return Exemple() with { Mode = LectureEcransMain.Demo };
        if (lu.Valeur is not { } v) return new VueComptabilisation([], 0, null, false, lu.Mode, lu.Message);
        return new VueComptabilisation([.. v.Journees.Select(Vue)], v.Total, v.Anomalie, v.EncaissementsSeuls, lu.Mode, null);
    }

    /// <summary>Une journée lue, telle que l'écran la montre (dates aaaa-mm-jj, montants en centimes).</summary>
    internal static JourneeCompta Vue(JourneeComptaLue j) => new(Jour(j.Date), j.Factures, Centimes(j.MontantTtc), j.AffairesEnCours,
        j.SansDemande, [.. j.Encaissements.Select(e => new RemisCompta(e.Remis ?? 0, e.Libelle, e.TypeMode, e.Nombre, Centimes(e.Montant),
                                                                         Jour(e.Date), e.Observation))]);

    private static string Jour(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Les centimes d'un montant (money : quatre décimales) ; une fraction de centime s'arrondit au plus proche, comme l'euro.</summary>
    internal static long Centimes(decimal montant) => (long)decimal.Round(montant * 100m, MidpointRounding.AwayFromZero);
}
