namespace RigV3.Contrats.EcransMain;

// « Acteurs » (PROC_ACTEUR_JUD, web/src/ecrans/EcranActeurs.tsx) : le GESTE de l'écran, enregistrer les coordonnées d'un acteur (plan
// « RIG v3 complet, mesuré par le banc du parcours complet », lot 9, 28/11/2026). La lecture est RoutesActeurs.Acteurs ; l'écriture :
// Serveur/Socle/EcransMain/EcritureActeurs.cs, sa route : Serveur/Socle/EcransMain/ModuleActeurs.cs.

/// <summary>L'adresse du geste « Enregistrer les coordonnées » d'un acteur et les clés de ses valeurs (les titres des champs de RIG v2,
/// PROC_ACTEUR_JUD\Edition\OPE_EDITION_G*.Designer.cs).</summary>
public static class RoutesCoordonneesActeur
{
    /// <summary>
    /// POST un geste (<c>RigV3.Contrats.EcransGeneres.GesteEcran</c>, Type = Enregistrer, Ids = [« Type-numéro », la clé de la ligne :
    /// « Juge-12 », « Mandataire-3 »...]) : le téléphone, le fax et le courriel ; un champ absent garde la valeur de la base. Rend un
    /// <c>ResultatGeste</c> (422 : le refus de RIG v2, rien d'écrit).
    /// </summary>
    public const string Enregistrer = "/ecrans-main/{greffe}/acteurs/coordonnees";

    public const string ChampTelephone = "Téléphone", ChampFax = "Fax", ChampMail = "Mail";
}
