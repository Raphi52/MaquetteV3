using System.Globalization;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// L'entrée de l'écran « Rédaction d'une décision » (web/src/ecrans/EcranRedaction.tsx). Plan « RIG v3 100 % fonctionnel », lot 10
/// (27/11/2026), sur la marche du lot 17 (Pont/LectureEcranMain.cs).
///
/// Branché au serveur, l'écran montre les décisions judiciaires des instances que RIG v2 rédige (séries JRCT, JRPC, JRREF), dans
/// l'ordre de PROC_REDA, avec leurs listes, leurs appels d'affaire délibéré et prononcé, leurs documents en GED et leurs mesures
/// (Serveur/Socle/EcransMain/LectureRedaction.cs, qui cite RIG v2). L'écran n'a AUCUN geste : il ne rédige ni ne valide rien
/// (RIG v2 le fait par décision, OPE_DECISION_JUDICIAIRE et les états de OPE_REDA_ETAT, non repris ici). SANS serveur : la
/// démonstration (Exemple), marquée « exemple ». En panne : le motif et aucune décision.
/// </summary>
public static partial class EcranRedaction
{
    [JSInvokable("EcranRedaction.Decisions")]
    public static Task<VueRedaction> Decisions() => DecisionsAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Les décisions du greffe <paramref name="greffe"/> (null serveur : la démonstration).</summary>
    public static async Task<VueRedaction> DecisionsAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les décisions à rédiger", (s, g) =>
            s.LireAsync<DecisionsRedactionLues>(RoutesEcransMain.Pour(RoutesRedaction.Decisions, g)));
        if (lu.Mode == LectureEcransMain.Demo) return Exemple();
        if (lu.Valeur is not { } v) return new VueRedaction([], lu.Mode, lu.Message);
        return new VueRedaction([.. v.Lignes.Select(Vue)], lu.Mode, null);
    }

    /// <summary>Une décision lue, telle que l'écran la montre (dates jj/mm/aaaa, vide sans date).</summary>
    internal static DecisionRedaction Vue(DecisionRedactionLue d)
    {
        static string Jour(DateTime? x) => x is { } j ? j.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "";
        return new DecisionRedaction(d.Id, d.Role, d.Affaire, d.Prononce.Length == 0, d.TypeDecision, d.SensGeneral, d.Ressort,
            Jour(d.Date), Jour(d.DatePrevue), d.MiseEnDelibere, d.Prononce, d.AvantDireDroit, d.Contradictoire, d.ExecutionProvisoire,
            d.DebatsPublics, d.DecisionPublique, d.MesureInstruction, d.Documents, d.Mesures);
    }
}
