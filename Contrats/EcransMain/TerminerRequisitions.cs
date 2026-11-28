namespace RigV3.Contrats.EcransMain;

// « Réquisitions » (PROC_REQUI, web/src/ecrans/EcranRequisitions.tsx) : le GESTE écrit dans la base du greffe, « Terminer » la commande
// de documents, et sa relecture (plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026). La recherche des
// entreprises est RoutesRequisitions.Entreprises ; l'écriture : Serveur/Socle/EcransMain/EcritureRequisitions.cs, la relecture :
// LectureCommandesRequi.cs, leurs routes : ModuleRequisitions.cs.

/// <summary>Les adresses du geste « Terminer » et de la relecture de la commande terminée.</summary>
public static class RoutesTerminerRequisitions
{
    /// <summary>
    /// POST un geste (<c>RigV3.Contrats.EcransGeneres.GesteEcran</c>, Type = Creer, Ids vide) : les commandes du panier, chacune sous
    /// <see cref="Cle"/>(n, champ) (n = 1, 2... : le numéro de la commande), et la case <see cref="ChampGuichet"/>. Rend un
    /// <c>ResultatGeste</c> dont Ids[0] est le numéro de la demande REQUI (422 : le refus de RIG v2).
    /// </summary>
    public const string Terminer = "/ecrans-main/{greffe}/requisitions/terminer";

    /// <summary>POST <see cref="CommandeRequiDemandee"/> : la commande terminée, relue en base (<see cref="CommandeRequiLue"/>).</summary>
    public const string Commande = "/ecrans-main/{greffe}/requisitions/commande";

    /// <summary>« 1 » : la commande est remise au guichet (case « Guichet » de RIG v2).</summary>
    public const string ChampGuichet = "Guichet";

    /// <summary>Les champs d'une commande : l'identifiant du dossier RCS et le nombre de chaque document (OPE_REQUI_ARTICLES).</summary>
    public const string Dossier = "Dossier", Kbis = "Kbis", Historique = "Historique", Das = "DAS", CertifNonFaillite = "CNF",
                        DateCertifNonFaillite = "DateCNF", CertifNonRcs = "CNRCS", CertifNonRsac = "CNRSAC", DateCertifNonRsac = "DateCNRSAC",
                        Inscriptions = "Inscriptions", AutresDocuments = "Autres";

    /// <summary>La clé du champ <paramref name="champ"/> de la commande <paramref name="numero"/> dans les Valeurs du geste.</summary>
    public static string Cle(int numero, string champ) => $"Commande {numero} › {champ}";
}

/// <summary>La commande à relire : le numéro de sa demande REQUI.</summary>
public sealed record CommandeRequiDemandee(string NumeroDemande);

/// <summary>Une ligne de la table COMMANDE : le type de document, la quantité, le dossier, remise au guichet ou non.</summary>
public sealed record LigneCommandeRequiLue(string Type, int Quantite, int? IdDossier, bool Guichet);

/// <summary>Une ligne d'édition d'une demande : le modèle de document, le nombre d'exemplaires, l'imprimante.</summary>
public sealed record EditionRequiLue(string Modele, int Nombre, string Imprimante);

/// <summary>Une demande de la commande (la demande REQUI ou une demande LPLURI par dossier) : numéro, nom, état, dossier, éditions.</summary>
public sealed record DemandeRequiLue(string Numero, string Nom, string Etat, bool EnCours, int? IdDossier, EditionRequiLue[] Editions);

/// <summary>La commande terminée telle que la base la garde ; Requi null : aucune demande REQUI de ce numéro.</summary>
public sealed record CommandeRequiLue(DemandeRequiLue? Requi, LigneCommandeRequiLue[] Lignes, DemandeRequiLue[] Demandes);
