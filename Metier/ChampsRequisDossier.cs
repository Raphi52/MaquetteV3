namespace RigV3Desktop;

// ============================================================
// Les champs OBLIGATOIRES de « Informations du dossier ».
//
// Liste LUE, pas devinee : lecture du 17/09/2026 de la base de RECETTE
// RIG_DEV@SQL-DEV\DEV, colonnes NOT NULL sans valeur par defaut des 31
// tables de Metier/InformationsDossier.cs. Requete de regeneration :
//
//   SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
//   WHERE IS_NULLABLE = 'NO' AND COLUMN_DEFAULT IS NULL
//     AND TABLE_NAME IN (<les tables de InformationsDossier.Sections>)
//     AND COLUMN_NAME NOT LIKE '%[_]ID[_]%' AND COLUMN_NAME NOT LIKE '%ETAT_INFO'
//     AND COLUMN_NAME NOT LIKE '%NUM_ORDRE' AND COLUMN_NAME NOT LIKE '%COMPTEUR_MAJ'
//   ORDER BY TABLE_NAME, COLUMN_NAME
//
// 28 colonnes rendues sur 523 champs. Les colonnes sont ecrites ICI SANS
// leur prefixe de table (DSSRC_, INSPM_...), comme dans InformationsDossier :
// ColonnesRig.Resoudre retablit la colonne reelle.
//
// ECARTE : BRANCHE_ACTIVITE.ORIGINE_INFO -- NOT NULL en base mais AUCUN
// champ de la fiche ne le porte ; l'inscrire creerait un requis que
// personne ne pourrait remplir.
// ============================================================

/// <summary>Les champs qu'un dossier ne peut pas laisser vides.</summary>
public static class ChampsRequisDossier
{
    /// <summary>Colonnes NOT NULL, mais ECRITES PAR LE SYSTEME : le greffier
    /// ne les saisit jamais (numerotation, horodatage, etat calcule). Elles
    /// restent dans la liste lue, et sont retirees du parcours guide.</summary>
    public static readonly (string Table, string Colonne)[] Systeme =
    [
        ("DOSSIER_RCS", "DATE_MAJ"),
        ("DOSSIER_RCS", "NUM_GESTION"),
        ("DOSSIER_RCS", "ETAT_DOSSIER"),
    ];

    /// <summary>Les tables dont les requis valent TOUJOURS : le dossier existe,
    /// donc ses champs de tete se remplissent. Toutes les autres tables sont
    /// CONDITIONNELLES (voir <see cref="BlocConditionnel"/>).</summary>
    public static readonly string[] TablesToujoursActives = ["DOSSIER_RCS"];

    /// <summary>La liste lue en recette, colonnes techniques deja ecartees.</summary>
    public static readonly (string Table, string Colonne)[] Lus =
    [
        ("ACTIVITE_CONDITIONS_EXERCICE", "AUTORITE"),
        ("ACTIVITE_CONDITIONS_EXERCICE", "DATE_DELIVRANCE"),
        ("AFFECTATION_PATRIMOINE", "REGISTRE_IMMAT"),
        ("BENEFICIAIRE_EFFECTIF", "DATE_EFFET"),
        ("BRANCHE_ACTIVITE", "PARTIE_ACTIVITE"),
        ("BRANCHE_ACTIVITE", "PRINCIPALE"),
        ("DIRIGEANT", "FONCTION_DIRIGEANT"),
        ("DIRIGEANT_PP", "CIVILITE"),
        ("DOSSIER_RCS", "DATE_IMMAT"),
        ("DOSSIER_RCS", "DATE_MAJ"),
        ("DOSSIER_RCS", "ETAT_DOSSIER"),
        ("DOSSIER_RCS", "MOTIF_IMMAT"),
        ("DOSSIER_RCS", "NUM_GESTION"),
        ("DOSSIER_RCS", "TYPE_RCS"),
        ("ENTOURAGE", "ROLE_ENTOURAGE"),
        ("EXERCICE", "DATE_CLOTURE"),
        ("EXERCICE", "ETAT_DCA"),
        ("EXERCICE", "TYPE_EXERCICE"),
        ("INSCRIT_PM", "DENOMINATION_USAGE"),
        ("INSCRIT_PM", "FORME_JURIDIQUE_UTI"),
        ("INSCRIT_PP", "CIVILITE"),
        ("INSCRIT_PP", "PATRONYME"),
        ("INSCRIT_PP", "PRENOMS_ETAT_CIVIL"),
        ("NOTE_DOSSIER_RCS", "TEXTE_NOTE"),
        ("OFFICE_AUTRE", "TEXTE_MENTION"),
        ("OFFICE_PC", "TEXTE_MENTION"),
        ("REPRESENTANT_PM", "CIVILITE"),
    ];

    /// <summary>Ce que le greffier doit REELLEMENT saisir : la liste lue, moins
    /// les colonnes ecrites par le systeme.</summary>
    public static readonly (string Table, string Colonne)[] ASaisir =
        Lus.Where(x => !Systeme.Contains(x)).ToArray();

    /// <summary>Vrai quand ce champ est obligatoire ET saisi par un humain.</summary>
    public static bool EstRequis(string table, string colonne)
        => ASaisir.Contains((table, colonne));

    /// <summary>Vrai quand les requis de cette table ne valent que si le bloc
    /// est ENTAME. Un dossier personne morale n'a pas de patronyme d'inscrit
    /// PP, et un dossier sans dirigeant n'a pas de fonction de dirigeant :
    /// reclamer ces champs rendrait le parcours impossible a terminer.</summary>
    public static bool BlocConditionnel(string table)
        => !TablesToujoursActives.Contains(table);

    /// <summary>Nombre de champs a saisir obligatoirement.</summary>
    public static int Nombre => ASaisir.Length;
}
