namespace RigV3Desktop.Pont;

/// <summary>
/// Un enrolement de l'ecran (les champs de « DossierJud » dans web/src/ecrans/EcranConsultationJud.tsx) ; dates aaaa-mm-jj.
/// <paramref name="NbPieces"/> et <paramref name="Anomalies"/> sont null quand la V3 ne les lit pas (flux entrant de la base EDI,
/// lecture du XML par la V2 : Serveur/Socle/EcransMain/LectureConsultationJud.cs). <paramref name="Xml"/> est null pour un
/// enrolement de la base : son XML se lit a la demande, par <paramref name="Id"/> (EcranConsultationJud.Xml) ; la demonstration
/// porte le sien et n'a pas d'Id.
/// </summary>
public sealed record LigneConsultationJud(string Affaire, string Denomination, string EtatDemat, string EtatTraitement, string TypeRejet,
                                          string Creation, string? Traitement, int? NbPieces, string? Anomalies, string MotifRejet,
                                          string? Xml, string Dossier, string? Id = null);

/// <summary>Le XML d'un enrolement lu a la demande : <paramref name="Xml"/>, ou <paramref name="Message"/> qui dit pourquoi il
/// n'y en a pas (pas de flux, enrolement disparu, panne).</summary>
public sealed record XmlConsultationJud(string Mode, string? Xml, string? Message);

/// <summary>Ce que l'ecran montre : ses enrolements, d'ou ils viennent (Pont/LectureEcranMain.cs) et leur total.</summary>
public sealed record VueConsultationJud(LigneConsultationJud[] Lignes, string Mode = "demo", string? Message = null, int Total = 0);

/// <summary>
/// Pont de l'ecran PROC_CJUD (web/src/ecrans/EcranConsultationJud.tsx). Jusqu'au lot 6 du plan « RIG v3 100 % fonctionnel »
/// (26/11/2026), l'ecran portait ses enrolements dans le .tsx, sans pont ; ils sont ici, la DEMONSTRATION rendue sans serveur et
/// marquee « exemple » a l'ecran. Les etats sont les libelles de RIG v2 (eDematEtat, DematEtat.cs:17-47 ; Accepté / Refusé,
/// ULT_COMBO_ACCEPTE_REFUSE.cs:20-21). Branche au serveur : EcranConsultationJudBase.cs.
/// </summary>
public static partial class EcranConsultationJud
{
    /// <summary>Les enrolements de demonstration, sans serveur.</summary>
    public static VueConsultationJud Exemple() => new(
    [
        new("2026J00121", "SARL BOULANGERIE DUVAL", "Validé", "Accepté", "", "2026-03-04", "2026-03-06", 12, "", "", "<enrolement num=\"2026J00121\" />", "2024B00318"),
        new("2026J00134", "SAS TRANSPORTS MARNE", "Intégré avec succès", "En attente", "", "2026-05-19", null, 5, "Piece 3 illisible", "", "<enrolement num=\"2026J00134\" />", "2024B00407"),
        new("2026J00140", "EURL COIFFURE MARIE", "Envoyé", "Refusé", "Piece manquante", "2026-06-02", "2026-06-03", 3, "Acte de saisine absent du depot", "Dossier incomplet", "<enrolement num=\"2026J00140\" />", "2023B01120"),
        new("2026J00155", "SCI DU MOULIN", "Intégré avec succès", "En attente", "", "2026-07-21", null, 0, "Aucun flux recu", "", "", "2025B00044"),
    ], LectureEcransMain.Demo, null, 4);
}
