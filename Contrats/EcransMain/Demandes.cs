namespace RigV3.Contrats.EcransMain;

// « Demandes » (PROC_DEMANDE, web/src/ecrans/EcranDemandes.tsx) : le GESTE de l'écran, « Supprimer l'état en cours » du menu de
// la grille de RIG v2 (plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026). La lecture est
// RoutesEcransMain.Demandes ; l'écriture : Serveur/Socle/EcransMain/EcritureDemandes.cs, sa route : ModuleDemandes.cs.

/// <summary>L'adresse du geste « Supprimer l'état en cours ».</summary>
public static class RoutesDemandes
{
    /// <summary>
    /// POST un geste (<c>RigV3.Contrats.EcransGeneres.GesteEcran</c>, Type = Agir, Ids = [le numéro de la demande]) : la demande
    /// n'est plus « en cours », son état ne change pas (OPE_RESULTATS.tsmiDeleteEtatEnCoursDemande_Click de RIG v2) ; rend un
    /// <c>ResultatGeste</c> (422 : le refus).
    /// </summary>
    public const string SupprimerEtatEnCours = "/ecrans-main/{greffe}/demandes/supprimer-etat-en-cours";
}
