namespace RigV3Desktop;

// ============================================================
// Les ETAPES NOMMEES de « Informations du dossier ».
//
// Le formulaire porte 31 sous-sections (une par table v2) : paginees une
// par une et numerotees, l'utilisateur devait deviner « etape 17 » pour
// trouver les dirigeants. Les tables sont donc regroupees en SIX etapes
// metier, nommees comme le greffier les nomme. Le regroupement est
// exhaustif : EtapesInfoDossier.Verifier() echoue si une table de
// InformationsDossier.Sections n'est rattachee a aucune etape.
// ============================================================

/// <summary>Une etape nommee : son titre et les tables v2 qu'elle regroupe.</summary>
public sealed record EtapeInfoDossier(string Titre, string[] Tables);

public static class EtapesInfoDossier
{
    public static readonly EtapeInfoDossier[] Etapes =
    [
        new("Identite", [
            "DOSSIER_RCS", "INSCRIT_PP", "INSCRIT_PM",
            "SITUATION_MATRIMO", "INSAISISSABILITE"]),
        new("Activite et etablissements", [
            "ACTIVITE", "ACTIVITE_CONDITIONS_EXERCICE", "BRANCHE_ACTIVITE",
            "ETABLISSEMENT", "MUTATION_FONDS", "LOI_1956"]),
        new("Personnes liees", [
            "DIRIGEANT", "DIRIGEANT_PP", "DIRIGEANT_PM", "REPRESENTANT_PM",
            "BENEFICIAIRE_EFFECTIF", "BEN_EFFEC_MODAL_CTRL", "ENTOURAGE",
            "TNS", "TNS_BENEFICIAIRE"]),
        new("Capital et exercice", [
            "CAPITAL", "EXERCICE", "AFFECTATION_PATRIMOINE", "CONTRAT_APPUI"]),
        new("Mentions et observations", [
            "NOTE_DOSSIER_RCS", "MENTION_OBSERVATION", "OBSERVATION_RCS",
            "MENTION_SPECIAL", "HORS_RESSORT"]),
        new("Procedures et offices", ["OFFICE_PC", "OFFICE_AUTRE"]),
    ];

    /// <summary>L'etape qui porte cette table, ou la derniere par defaut.</summary>
    public static EtapeInfoDossier Pour(string table)
        => Etapes.FirstOrDefault(e => e.Tables.Contains(table)) ?? Etapes[^1];

    /// <summary>Le rang de l'etape qui porte cette table (0 si inconnue).</summary>
    public static int Rang(string table)
    {
        for (var i = 0; i < Etapes.Length; i++)
            if (Etapes[i].Tables.Contains(table)) return i;
        return 0;
    }

    /// <summary>Les tables du formulaire qu'aucune etape ne rattache.</summary>
    public static string[] TablesOrphelines()
        => InformationsDossier.Sections.Select(s => s.Table)
            .Where(t => !Etapes.Any(e => e.Tables.Contains(t)))
            .ToArray();

    /// <summary>Les tables citees par une etape mais absentes du formulaire.</summary>
    public static string[] TablesFantomes()
        => Etapes.SelectMany(e => e.Tables)
            .Where(t => !InformationsDossier.Sections.Any(s => s.Table == t))
            .ToArray();
}
