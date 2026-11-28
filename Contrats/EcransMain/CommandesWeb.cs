namespace RigV3.Contrats.EcransMain;

// « Commandes web » (PROC_COMMANDES_WEB, web/src/ecrans/EcranCommandesWeb.tsx) : les GESTES écrits dans la base du greffe, « Mettre en
// attente » et « Valider » une commande (plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026). La lecture
// est RoutesCommandesWeb.Commandes, la recherche du débiteur RoutesRequisitions.Entreprises (celle de « Réquisitions », REQUI de RIG v2) ;
// l'écriture : Serveur/Socle/EcransMain/EcritureCommandesWeb.cs, ses routes : ModuleCommandesWeb.cs.

/// <summary>Les adresses des gestes « Mettre en attente » et « Valider ».</summary>
public static class RoutesTraitementCommandesWeb
{
    /// <summary>
    /// POST un geste (<c>RigV3.Contrats.EcransGeneres.GesteEcran</c>, Type = Decider, Ids = [l'identifiant de la commande]) : la
    /// commande passe « en attente » avec son motif facultatif (<see cref="ChampMotif"/>), comme
    /// TraiterCommandeWebService.MettreEnAttenteAsync de NéoRIG ; rend un <c>ResultatGeste</c> (422 : le refus).
    /// </summary>
    public const string MettreEnAttente = "/ecrans-main/{greffe}/commandes-web/mettre-en-attente";

    /// <summary>
    /// POST un geste (Type = Decider, Nom = « Valider », Ids = [l'identifiant de la commande]) : la commande est VALIDÉE sur le dossier
    /// RCS retenu au rapprochement (<see cref="ChampDossier"/>, son identifiant), comme TraiterCommandeWebService.ValiderAsync de
    /// NéoRIG ; rend un <c>ResultatGeste</c> (422 : le refus), Ids = [la commande, le numéro de la demande de la validation].
    /// </summary>
    public const string Valider = "/ecrans-main/{greffe}/commandes-web/valider";

    /// <summary>La clé du motif dans les Valeurs du geste.</summary>
    public const string ChampMotif = "Motif";

    /// <summary>La clé du dossier RCS retenu (son identifiant, <c>EntrepriseLue.Id</c>) dans les Valeurs de « Valider ».</summary>
    public const string ChampDossier = "Dossier";
}
