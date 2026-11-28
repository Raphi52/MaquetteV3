namespace RigV3.Contrats.EcransMain;

// « Arrivée du courrier » (PROC_ARRIVEE, web/src/ecrans/EcranArrivee.tsx) : le GESTE de l'écran, enregistrer une arrivée
// (plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026). La lecture est RoutesEcransMain.Arrivees ;
// l'écriture : Serveur/Socle/EcransMain/EcritureArrivee.cs, ses routes : Serveur/Socle/EcransMain/ModuleArrivee.cs.

/// <summary>L'adresse du geste « Enregistrer une arrivée ».</summary>
public static class RoutesArrivee
{
    /// <summary>
    /// POST un geste (<c>RigV3.Contrats.EcransGeneres.GesteEcran</c>, Type = Creer, Ids vide) : la saisie d'une arrivée de RIG v2
    /// (OPE_ARRIVEE), son mode (<see cref="ChampMode"/>, PTT par défaut) et sa date (<see cref="ChampDate"/>, aaaa-mm-jj, aujourd'hui
    /// par défaut) ; rend un <c>ResultatGeste</c> dont Ids = [le numéro d'arrivée donné] (422 : le refus de RIG v2).
    /// </summary>
    public const string Enregistrer = "/ecrans-main/{greffe}/arrivees/enregistrer";

    /// <summary>La clé du mode d'arrivée dans les Valeurs du geste (le titre du combo de RIG v2, OPE_ARRIVEE.Designer.cs:73).</summary>
    public const string ChampMode = "Mode d'arrivée";

    /// <summary>La clé de la date d'arrivée dans les Valeurs du geste.</summary>
    public const string ChampDate = "Date d'arrivée";
}
