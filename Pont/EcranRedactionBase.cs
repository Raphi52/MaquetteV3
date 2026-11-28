using System.Globalization;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// L'entrée de l'écran « Rédaction d'une décision » (web/src/ecrans/EcranRedaction.tsx). Plan « RIG v3 100 % fonctionnel », lot 10
/// (27/11/2026), sur la marche du lot 17 (Pont/LectureEcranMain.cs).
///
/// Branché au serveur, l'écran montre les décisions judiciaires des instances que RIG v2 rédige (séries JRCT, JRPC, JRREF), dans
/// l'ordre de PROC_REDA, avec leurs listes, leurs appels d'affaire délibéré et prononcé, leurs documents en GED et leurs mesures
/// (Serveur/Socle/EcransMain/LectureRedaction.cs, qui cite RIG v2). Son geste (lot 8 du plan « parcours complet », 28/11/2026) :
/// VALIDER une décision -- ses listes, ses dates et ses deux cases, comme le bouton Valider de OPE_DECISION_JUDICIAIRE --, écrit par
/// le serveur (Serveur/Socle/EcransMain/EcritureRedaction.cs) ; la rédaction du document et ses états (OPE_REDA_ETAT) ne sont pas
/// repris. SANS serveur : la démonstration (Exemple), marquée « exemple ». En panne : le motif et aucune décision.
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
        return new VueRedaction([.. v.Lignes.Select(Vue)], lu.Mode, null, v.Total);
    }

    /// <summary>
    /// Valider la décision <paramref name="id"/> : <paramref name="champs"/> porte, sous leurs titres de RIG v2 (ValidationDecision :
    /// « Ressort », « Date »...), les champs changés ; un champ absent garde la valeur de la base. Dates aaaa-mm-jj, cases « 1 » / « 0 ».
    /// Sans serveur, ou pour une décision d'exemple : rien n'est écrit, « Démonstration ».
    /// </summary>
    [JSInvokable("EcranRedaction.Valider")]
    public static Task<ResultatGeste> Valider(int id, Dictionary<string, string> champs)
        => ValiderAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, id, champs);

    public static async Task<ResultatGeste> ValiderAsync(ClientServeur? serveur, string? greffe, int id, IReadOnlyDictionary<string, string> champs)
    {
        if (serveur is null || greffe is null)
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        var valeurs = champs.ToDictionary(c => GesteEcran.Cle(ValidationDecision.Section, c.Key), c => c.Value);
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(ValidationDecision.Valider, greffe),
            new GesteEcran(TypeGeste.Enregistrer, [id.ToString(CultureInfo.InvariantCulture)], valeurs));
    }

    /// <summary>Les valeurs des listes de la fiche (ressort, contradictoire...), lues dans la base pour « Valider ». Sans serveur : aucune
    /// (la démonstration n'a pas de geste).</summary>
    [JSInvokable("EcranRedaction.Listes")]
    public static Task<VueListesDecision> Listes() => ListesAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    public static async Task<VueListesDecision> ListesAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les listes de la décision", (s, g) =>
            s.LireAsync<ListeDecisionLue[]>(RoutesEcransMain.Pour(ValidationDecision.Listes, g)));
        return new VueListesDecision([.. (lu.Valeur ?? []).Select(l => new ListeDecision(l.Champ, l.Obligatoire,
            [.. l.Valeurs.Select(v => new ValeurListe(v.Valeur, v.Libelle))]))], lu.Mode, lu.Message);
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
