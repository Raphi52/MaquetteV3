namespace RigV3.Contrats.EcransMain;

// « Courriers divers » (PROC_COURRIER_DIVERS, web/src/ecrans/EcranCourriersDivers.tsx), ecran fait main lu et ecrit dans la base du
// greffe (plan « RIG v3 complet, mesure par le banc du parcours complet », lot 8, 28/11/2026). Le lecteur :
// Serveur/Socle/EcransMain/LectureCourriersDivers.cs ; l'ecriture : EcritureCourriersDivers.cs ; les routes : ModuleCourriersDivers.cs.

/// <summary>Les adresses : GET le catalogue (<see cref="CourriersDiversLus"/>) ; POST le geste « valider » ou « supprimer » un modele.</summary>
public static class RoutesCourriersDivers
{
    public const string Catalogue = "/ecrans-main/{greffe}/courriers-divers";

    /// <summary>POST GesteEcran : Creer (Ids vide) ou Enregistrer (Ids = [son id]) avec les champs de <see cref="SaisieCourrierDivers"/>,
    /// Supprimer (Ids = [son id]).</summary>
    public const string Geste = "/ecrans-main/{greffe}/courriers-divers/geste";
}

/// <summary>Un modele de courrier divers (COURRIER_JUDICIAIRE) : les colonnes de la liste de RIG v2 (OPE_RECHERCHE_COURRIER_DIVERS.cs:171-179).</summary>
public sealed record CourrierDiversLu(int Id, string Code, string Libelle, string TypeEntite, string ParamEdition, bool AvecDestinataire, bool Actif);

/// <summary>Un parametre d'edition proposable : son code, son libelle et le type d'entite dont il releve (PRMED_CODE_PROCESSUS EDIT_*).</summary>
public sealed record ParamEditionLu(string Code, string Libelle, string TypeEntite);

/// <summary>Le catalogue : les modeles, actifs ou non, et les parametres d'edition que la fiche propose par type d'entite.</summary>
public sealed record CourriersDiversLus(CourrierDiversLu[] Courriers, ParamEditionLu[] ParamsEdition);

/// <summary>
/// Les champs de la fiche d'un courrier divers, sous leurs titres de RIG v2 (OPE_EDITION_COURRIER_DIVERS.Designer.cs:78-135) : code
/// (20 car., obligatoire), libelle (100 car., obligatoire), parametre edition (obligatoire), avec destinataire, actif (cases « 1 » / « 0 »),
/// type entite (obligatoire). La section : le titre de l'etape d'ajout de RIG v2 (OPE_EDITION_COURRIER_DIVERS.cs:181).
/// </summary>
public static class SaisieCourrierDivers
{
    public const string Section = "Courrier divers";
    public const string Code = "Code";
    public const string Libelle = "Libellé";
    public const string ParamEdition = "Paramètre édition";
    public const string AvecDestinataire = "Avec destinataire";
    public const string Actif = "Actif";
    public const string TypeEntite = "Type entité";
}
