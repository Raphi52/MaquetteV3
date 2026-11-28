namespace RigV3.Contrats.EcransMain;

// « Numéros manquants » (PROC_NUMEROS_MANQUANTS, web/src/ecrans/EcranManquants.tsx) : le geste d'édition de la liste (lot 11 du
// plan « RIG v3 complet, mesuré par le banc du parcours complet », 28/11/2026). La lecture reste RoutesCompteur.Manquants
// (Serveur/Numerotation/Compteur/ModuleCompteur.cs).

/// <summary>L'adresse du geste « Éditer la liste » (procédure LNUMCM de RIG v2).</summary>
public static class RoutesManquants
{
    /// <summary>
    /// POST un geste (<c>RigV3.Contrats.EcransGeneres.GesteEcran</c>, Type = Agir, Ids vide) : l'étape ALNUMCM de LNUMCM, « Editer
    /// la liste des numéros chono manquants » (MODELE_PROCESSUS de RIG_DEV) -- sa demande LNUMCM validée et sa ligne d'édition
    /// ALNUMCM_1_IMP ; rend un <c>ResultatGeste</c> dont Ids = [la demande] (422 : refusé ; 403 : sans la fonction LNUMCM).
    /// Écriture : Serveur/Socle/EcransMain/EcritureManquants.cs.
    /// </summary>
    public const string Editer = "/ecrans-main/{greffe}/numeros-manquants/editer";
}
