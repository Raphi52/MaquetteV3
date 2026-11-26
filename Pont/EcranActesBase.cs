using System.Globalization;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// L'entree de l'ecran « Actes » (web/src/ecrans/EcranActes.tsx). Plan « RIG v3 100 % fonctionnel », lot 6 (26/11/2026), sur
/// la marche du lot 17 (Pont/LectureEcranMain.cs).
///
/// Branche au serveur, l'ecran montre les actes de la base du greffe (Serveur/Socle/EcransMain/LectureActes.cs) : ceux que
/// PROC_ACTES_NUM de RIG v2 liste en mode Acte, deposes depuis 2014 et pas encore ranges dans la GED (la note de l'ecran le
/// dit : son titre reste celui du menu). Les criteres partent a la base : « N Dossier / N Depot », abreges (separes par des
/// virgules ou des espaces), categorie, periode, « Lister actes non numerisables » (ACTDP_SANS_DOCUMENT = 1, la case REMPLACE
/// la liste comme dans la V2), le nombre maxi. « Avec anomalie seulement » vise les bilans (mode Bilan de la V2, vue
/// ViewDcaEnAnomalie absente de la base V3) : aucune ligne, et la note le dit. La date de cloture, la confidentialite et les
/// anomalies (mode Bilan) restent vides. SANS serveur : la demonstration (Trouver, JeuDemo.Actes), marquee « exemple ». En
/// panne : le motif et aucune ligne.
/// </summary>
public static partial class EcranActes
{
    /// <summary>La note d'une liste lue en base : ce qu'elle montre au juste.</summary>
    public const string NoteBase = "Base du greffe : les actes déposés depuis 2014 qui ne sont pas encore rangés dans la GED "
                                 + "(« Actes non numérisés ou manquants » de RIG v2).";

    /// <summary>La note de « Avec anomalie seulement » en base.</summary>
    public const string NoteAnomalies = "Les anomalies sont celles des bilans (mode Bilan de RIG v2, vue ViewDcaEnAnomalie), "
                                      + "que la base V3 ne porte pas : aucun acte n'a d'anomalie lue.";

    [JSInvokable("EcranActes.Trouver")]
    public static Task<VueActes> TrouverBase(CriteresActes criteres)
        => TrouverAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, criteres);

    /// <summary>Les actes du greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<VueActes> TrouverAsync(ClientServeur? serveur, string? greffe, CriteresActes c)
    {
        var recherche = new RechercheActes(c.CritActeCritere ?? "",
            (c.CritAbrege ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            c.CritTypeDepot ?? "", c.CritDu?.ToDateTime(TimeOnly.MinValue), c.CritAu?.ToDateTime(TimeOnly.MinValue),
            c.CritNonNumerisables, c.CritTopMax);
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les actes", (s, g) =>
            s.EnvoyerAsync<RechercheActes, ActesLus>(RoutesEcransMain.Pour(RoutesActes.Actes, g), recherche));
        if (lu.Mode == LectureEcransMain.Demo) return Trouver(c);
        if (lu.Valeur is not { } v) return new VueActes([], [], lu.Mode, lu.Message);
        var categories = v.Categories.Select(k => new OptionActes(k.Code, k.Libelle)).ToArray();
        if (c.CritAnomalies) return new VueActes(categories, [], lu.Mode, NoteAnomalies, 0);
        return new VueActes(categories, [.. v.Lignes.Select((a, i) => Ligne(i + 1, a))], lu.Mode, NoteBase, v.Total);
    }

    private static string Jour(DateTime? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";

    private static LigneActe Ligne(int rang, ActeLu a)
        => new(rang, a.NumeroGestion, a.NumeroDepot, a.Designation, a.CategorieLibelle, a.Abrege, a.Description, Jour(a.DateDepot),
               Jour(a.DateActe), Cloture: "", Confidentiel: false, a.Demat, Anomalie: "", Numerisable: !a.SansDocument);
}
