namespace RigV3.Contrats.EcransMain;

// « Commandes web » (PROC_COMMANDES_WEB, web/src/ecrans/EcranCommandesWeb.tsx) : le GESTE écrit dans la base du greffe, « Mettre en
// attente » une commande (plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026). La lecture est
// RoutesCommandesWeb.Commandes ; l'écriture : Serveur/Socle/EcransMain/EcritureCommandesWeb.cs, sa route : ModuleCommandesWeb.cs.

/// <summary>L'adresse du geste « Mettre en attente ».</summary>
public static class RoutesTraitementCommandesWeb
{
    /// <summary>
    /// POST un geste (<c>RigV3.Contrats.EcransGeneres.GesteEcran</c>, Type = Decider, Ids = [l'identifiant de la commande]) : la
    /// commande passe « en attente » avec son motif facultatif (<see cref="ChampMotif"/>), comme
    /// TraiterCommandeWebService.MettreEnAttenteAsync de NéoRIG ; rend un <c>ResultatGeste</c> (422 : le refus).
    /// </summary>
    public const string MettreEnAttente = "/ecrans-main/{greffe}/commandes-web/mettre-en-attente";

    /// <summary>La clé du motif dans les Valeurs du geste.</summary>
    public const string ChampMotif = "Motif";
}
