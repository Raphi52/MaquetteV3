// fix-ok: fichier NEUF du lot 9 ; premier essai mesure par RecopieDeuxSensCheck (REC-004 T03 rouge) : RIG_DEV porte 5 colonnes
// que RigDatabase\dbo\Tables ne declare pas (CLIEN_TVA_INTRA, CLIEN_SIRET, DSSRC_INPI_IO_ETAT, DSSRC_INPI_IO_DATE_ENVOI,
// INEDT_ID_SSADR_X) -- leurs libelles, proposes, sont ajoutes.
namespace RigV3.Contrats.Recopie;

/// <summary>
/// CE QUE L'AGENT LIT A LA PLACE DES NOMS DE COLONNES (REC-004, ecran « Fiches en conflit ») : la sorte de fiche
/// (« Client ») et le titre de chaque champ des fiches partagees (« E-mail »), jamais « CLIENT » ni « CLIEN_EMAIL ».
///
/// D'ou viennent les titres. Quand l'ancien RIG montre le champ sur une fiche, c'est SON titre : la propriete MetierTitre
/// du controle lie a la colonne (« _ultEmailClient.Link(Client.CLIEN_EMAIL) » dans OPE_IDENTIFICATION.cs, et
/// « this._ultEmailClient.MetierTitre = "E-mail" » dans OPE_IDENTIFICATION.Designer.cs) -- releve le 2026-11-21 sur
/// D:\RigApplication\Source (tous les .Designer.cs, controle et colonne apparies par le .cs voisin), marque « V2 » ci-dessous.
/// Fiches lues : RIG\DLL\Processus\PROC_FCLIENT\ETP 01 - Identification (client), les ecrans RCS du dossier et de
/// l'inscription (PROC_*). Les autres colonnes n'ont de titre sur aucune fiche de la V2 (techniques, ou montrees par un
/// autre ecran) : leur libelle est PROPOSE, a valider par le referent (docs\reconstruction\recopie.md).
/// Une colonne absente d'ici se lit d'apres son nom (<see cref="Champ"/>) : le banc RecopieDeuxSensCheck exige un libelle
/// pour chaque colonne de chaque fiche partagee, une table ajoutee a la recopie ajoute donc ses libelles ici.
/// </summary>
public static class LibellesRecopie
{
    /// <summary>La sorte de fiche, comme l'agent la nomme.</summary>
    public static string Table(string tableV2) => Tables.TryGetValue(tableV2, out var l) ? l : D(tableV2);

    /// <summary>Le titre du champ ; d'apres le nom de la colonne s'il n'est pas connu (« CLIEN_FOO_BAR » -> « Foo bar »).</summary>
    public static string Champ(string tableV2, string colonne)
        => Champs.TryGetValue(tableV2, out var t) && t.TryGetValue(colonne, out var l) ? l : D(SansPrefixe(colonne));

    /// <summary>Vrai si la colonne a un libelle ecrit ici (le banc l'exige de chaque colonne recopiee).</summary>
    public static bool Connu(string tableV2, string colonne) => Champs.TryGetValue(tableV2, out var t) && t.ContainsKey(colonne);

    /// <summary>Les champs dont la valeur ne s'affiche jamais : le mot de passe du compte web d'un client (CLIEN_MDP), que
    /// la fiche client de la V2 masque (« Mot de passe », controle a saisie cachee).</summary>
    public static bool Masque(string tableV2, string colonne) => tableV2 == "CLIENT" && colonne == "CLIEN_MDP";

    private static string SansPrefixe(string colonne) => colonne.IndexOf('_') is > 0 and var i && i < colonne.Length - 1 ? colonne[(i + 1)..] : colonne;

    private static string D(string nom)
    {
        var mots = nom.Replace('_', ' ').Trim().ToLowerInvariant();
        return mots.Length == 0 ? nom : char.ToUpperInvariant(mots[0]) + mots[1..];
    }

    private static readonly Dictionary<string, string> Tables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CLIENT"] = "Client",
        ["DOSSIER_RCS"] = "Dossier RCS",
        ["INSCRIPTION"] = "Inscription",
        ["ADRESSE_BASE"] = "Adresse",
        ["ACTION_RCS"] = "Action du registre",
        ["ARRIVEE"] = "Arrivée",
        ["CATEGORIE"] = "Catégorie",
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Champs = new(StringComparer.OrdinalIgnoreCase)
    {
        // RigDatabase\dbo\Tables\CLIENT.sql ; fiche client : PROC_FCLIENT\ETP 01 - Identification\OPE_IDENTIFICATION.Designer.cs.
        ["CLIENT"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CLIEN_ABREGE"] = "Abrégé client",                                   // V2
            ["CLIEN_NUMERO_COMPTE"] = "N° de compte",                              // V2
            ["CLIEN_NOM"] = "Nom du client",                                       // V2
            ["CLIEN_PRENOM"] = "Prénom",
            ["CLIEN_TYPE"] = "Type de client",
            ["CLIEN_CIVILITE"] = "Civilité",
            ["CLIEN_STATUT"] = "État du compte",                                   // V2
            ["CLIEN_SEUIL_COMPTE"] = "Provision demandée",                         // V2
            ["CLIEN_COMPTE_COLLECTIF"] = "Compte collectif",
            ["CLIEN_GROUPE"] = "Groupe client",                                    // V2
            ["CLIEN_MODE_PAIEMENT"] = "Mode de paiement privilégié",               // V2
            ["CLIEN_DATE_DERNIERE_MODIF"] = "Date de dernière modification du compte", // V2
            ["CLIEN_DATE_REPORT"] = "Date du report",
            ["CLIEN_ASSUJETTI_TVA"] = "Assujetti à la TVA",
            ["CLIEN_RELEVE_FACTURE"] = "Relevé de facture",                        // V2
            ["CLIEN_BLOC_NOTES"] = "Notes",                                        // V2
            ["CLIEN_MONTANT_REPORT"] = "Montant du report",
            ["CLIEN_ID_UTLST"] = "Agent de la dernière modification",
            ["CLIEN_EMAIL"] = "E-mail",                                            // V2
            ["CLIEN_TELEPHONE"] = "Téléphone",                                     // V2
            ["CLIEN_TELEPHONE_PORTABLE"] = "Portable",                             // V2
            ["CLIEN_FAX"] = "Fax",
            ["CLIEN_ID_CLIEN"] = "N° interne du client",
            ["CLIEN_IS_COMPTE_PC"] = "Compte de procédure collective",
            ["CLIEN_IS_COMPTE_WEB"] = "Compte web",
            ["CLIEN_MDP"] = "Mot de passe",                                        // V2
            ["CLIEN_CODE_GREFFE"] = "Code greffe",
            ["CLIEN_ETAT_EDI"] = "État EDI",
            ["CLIEN_NUM_EDI"] = "N° EDI",
            ["CLIEN_DATE_EDI"] = "Date EDI",
            ["CLIEN_SIREN"] = "N° Siren",                                          // V2
            ["CLIEN_DATE_NAISSANCE"] = "Date de naissance",                        // V2
            // Dans RIG_DEV, pas dans RigDatabase\dbo\Tables\CLIENT.sql (releve par RecopieDeuxSensCheck le 2026-11-21).
            ["CLIEN_TVA_INTRA"] = "N° de TVA intracommunautaire",
            ["CLIEN_SIRET"] = "N° Siret",
        },
        // RigDatabase\dbo\Tables\DOSSIER_RCS.sql ; titres V2 des ecrans du dossier RCS (PROC_*).
        ["DOSSIER_RCS"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DSSRC_ID_RPRTR"] = "Répertoire",
            ["DSSRC_TYPE_RCS"] = "Type RCS",                                       // V2
            ["DSSRC_NUM_GESTION"] = "N° de gestion",                               // V2
            ["DSSRC_FINAL_GESTION"] = "Fin du n° de gestion",
            ["DSSRC_SIREN"] = "N° Siren",                                          // V2
            ["DSSRC_DATE_IMMAT"] = "Date d'immatriculation",                       // V2 (« Date d'immat. », « Immatriculation »)
            ["DSSRC_MOTIF_IMMAT"] = "Motif d'immatriculation",
            ["DSSRC_DATE_EFFET"] = "Date d'effet",
            ["DSSRC_NUM_ANTERIEUR"] = "N° antérieur",
            ["DSSRC_PROVISOIREMENT_SANS"] = "Provisoirement sans activité",
            ["DSSRC_DATE_CESSATION"] = "Date de cessation",                        // V2 (« Cessation »)
            ["DSSRC_DATE_RADIATION"] = "Date de radiation",                        // V2
            ["DSSRC_COMPTEUR_MAJ"] = "Nombre de mises à jour",
            ["DSSRC_DATE_MAJ"] = "Date de mise à jour",
            ["DSSRC_ETAT_DOSSIER"] = "État du dossier",
            ["DSSRC_ETAT_INFO"] = "État informatique",
            ["DSSRC_ETAT_VAX"] = "État VAX",
            ["DSSRC_DATE_MAJ_INFOG"] = "Mise à jour Infogreffe",
            ["DSSRC_DATE_MAJ_INTERG"] = "Mise à jour intergreffe",
            ["DSSRC_DATE_MAJ_CADUCITE"] = "Mise à jour de la caducité",
            ["DSSRC_DATE_FORMALITE_CESS"] = "Date de la formalité de cessation",
            ["DSSRC_NUM_ARCHIVAGE"] = "N° d'archivage",
            ["DSSRC_PROCEDURE_PC"] = "Procédure collective",
            ["DSSRC_ID_DSRCS"] = "N° interne du dossier",
            ["DSSRC_INSCRIT_REGISTRE_METIER"] = "Inscrit au répertoire des métiers",
            ["DSSRC_ETAT_INFOG_ANT"] = "État Infogreffe antérieur",
            ["DSSRC_ETAT_TRANSMISSION"] = "État de transmission",
            ["DSSRC_DATE_MODIF_FLAG_TRANS"] = "Date de l'état de transmission",
            ["DSSRC_NOINT"] = "N° INT",
            ["DSSRC_TOP_MODIFINT"] = "Marque de reprise",
            ["DSSRC_DPT_RM"] = "Département du répertoire des métiers",
            ["DSSRC_SIREN_PROVISOIRE"] = "Siren provisoire",
            ["DSSRC_ACCRE"] = "ACCRE",
            ["DSSRC_BUG_RENVOI_EXPERIAN"] = "Renvoi Experian",
            ["DSSRC_AUTO_ENTREPRENEUR"] = "Auto-entrepreneur",
            ["DSSRC_DATE_DEBUT"] = "Date de début d'activité",
            ["DSSRC_EIRL"] = "EIRL",
            ["DSSRC_DATE_PREMIERE_IMMATRICULATION"] = "Date de première immatriculation",
            ["DSSRC_AUTO_ENTREPRENEUR_REGUL"] = "Régularisation auto-entrepreneur",
            ["DSSRC_INPI_ETAT"] = "État INPI",
            ["DSSRC_EUID"] = "EUID",
            // Dans RIG_DEV, pas dans RigDatabase\dbo\Tables\DOSSIER_RCS.sql (releve par RecopieDeuxSensCheck le 2026-11-21).
            ["DSSRC_INPI_IO_ETAT"] = "État de l'envoi à l'INPI",
            ["DSSRC_INPI_IO_DATE_ENVOI"] = "Date d'envoi à l'INPI",
        },
        // RigDatabase\dbo\Tables\INSCRIPTION.sql ; titres V2 des ecrans de l'inscription (PROC_*).
        ["INSCRIPTION"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["INEDT_ID_PRCLL"] = "Procédure collective",
            ["INEDT_ID_INSCR_INSCRIPTION_PRE"] = "Inscription précédente",
            ["INEDT_ID_ADRBS"] = "Adresse",
            ["INEDT_ID_CNEND"] = "Créance",
            ["INEDT_ID_BTEAU"] = "Bateau",
            ["INEDT_NUM_INSCRIPTION"] = "N° de l'inscription",                     // V2
            ["INEDT_TYPE_INSCRIPTION"] = "Type de l'inscription",                  // V2
            ["INEDT_PAR_TRANSFERT"] = "Inscription par transfert ?",               // V2
            ["INEDT_DATE_INSCRIPTION"] = "Date inscription",                       // V2
            ["INEDT_DATE_PEREMPTION"] = "Date péremption",                         // V2
            ["INEDT_DATE_EFFET"] = "Date d'effet",                                 // V2
            ["INEDT_RENOUVELABLE"] = "Renouvelable ?",                             // V2
            ["INEDT_ETAT_INFO"] = "État informatique",
            ["INEDT_DATE_DIFFUSION"] = "Date 1ère diffusion",                      // V2
            ["INEDT_ETAT_VAX"] = "État VAX",
            ["INEDT_DATE_MAJ"] = "Date de mise à jour",
            ["INEDT_DATE_MAJ_INFOG"] = "Mise à jour Infogreffe",
            ["INEDT_DATE_MAJ_INTERG"] = "Mise à jour intergreffe",
            ["INEDT_NUM_REGISTRE"] = "N° de registre",
            ["INEDT_ID_INSCR"] = "N° interne de l'inscription",
            ["INEDT_ETAT_TRANSMISSION"] = "État de transmission",
            ["INEDT_DATE_MODIF_FLAG_TRANS"] = "Date de l'état de transmission",
            ["INEDT_ETAT_TRAITEMENT"] = "État de traitement",
            ["INEDT_DATE_TRAITEMENT"] = "Date de traitement",
            ["INEDT_UUID"] = "Identifiant UUID",
            // Dans RIG_DEV, pas dans RigDatabase\dbo\Tables\INSCRIPTION.sql (releve par RecopieDeuxSensCheck le 2026-11-21).
            ["INEDT_ID_SSADR_X"] = "Sous-adresse",
        },
        // RigDatabase\dbo\Tables\ADRESSE_BASE.sql : aucun titre V2 releve (l'adresse se saisit par un controle composite).
        ["ADRESSE_BASE"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ADRBS_NUM_VOIE"] = "N° de voie",
            ["ADRBS_COMPLEMENT_NO"] = "Complément du n° (bis, ter)",
            ["ADRBS_TYPE_VOIE"] = "Type de voie",
            ["ADRBS_NOM_VOIE"] = "Nom de voie",
            ["ADRBS_CODE_POSTAL"] = "Code postal",
            ["ADRBS_LOCALITE_POSTALE"] = "Localité",
            ["ADRBS_PAYS"] = "Pays",
            ["ADRBS_CODE_ROUTAGE"] = "Code de routage",
            ["ADRBS_ETAT_INFO"] = "État informatique",
            ["ADRBS_ID_ADRBS"] = "N° interne de l'adresse",
            ["ADRBS_FORMATE_POUR_MEDIAPOST"] = "Formatée pour Médiapost",
            ["ADRBS_PRE_ADRESSE"] = "Complément avant l'adresse",
            ["ADRBS_SUITE_ADRESSE"] = "Suite de l'adresse",
            ["ADRBS_BOITE_POSTALE"] = "Boîte postale",
            ["ADRBS_CEDEX"] = "Cedex",
            ["ADRBS_LIEU_RATTACHE"] = "Lieu-dit",
            ["ADRBS_DIVISION_TERRIT"] = "Division territoriale",
            ["ADRBS_TYPE_SPECIAL"] = "Type spécial",
            ["ADRBS_VALEUR_SPECIAL"] = "Valeur spéciale",
            ["ADRBS_CODE_INSEE"] = "Code INSEE de la commune",
        },
        // RigDatabase\dbo\Tables\ACTION_RCS.sql ; les liens visent DEPOT_RCS, BODACC, INSCRIPTION_RCS, DOSSIER_RCS,
        // PASSAGE_CFE, NON_INSCRIT, INSTANCE et DEPOT_RCS_HISTO (cle primaire de meme prefixe).
        ["ACTION_RCS"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ACTRC_ID_DPTRC"] = "Dépôt RCS",
            ["ACTRC_ID_BDCRC"] = "Annonce BODACC",
            ["ACTRC_ID_INSRC"] = "Inscription RCS",
            ["ACTRC_ID_DSRCS"] = "Dossier RCS",
            ["ACTRC_ID_PSSCF"] = "Passage CFE",
            ["ACTRC_ID_NINSC"] = "Non-inscrit",
            ["ACTRC_NUM_ARRIVEE"] = "N° d'arrivée",
            ["ACTRC_DATE_ARRIVEE"] = "Date d'arrivée",
            ["ACTRC_CODE_PROSS"] = "Code processus",                               // V2
            ["ACTRC_ETAT_INFO"] = "État informatique",
            ["ACTRC_ETAT_ACTION"] = "État action",                                 // V2
            ["ACTRC_ID_ACTRC"] = "N° interne de l'action",
            ["ACTRC_SUR_NOTIFICATION"] = "Sur notification",
            ["ACTRC_ID_INSTN"] = "Instance",
            ["ACTRC_ID_DPTRCH"] = "Dépôt RCS (historique)",
        },
        // RigDatabase\dbo\Tables\ARRIVEE.sql : aucun titre V2 releve.
        ["ARRIVEE"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ARRV_NUM_ARRIVEE"] = "N° d'arrivée",
            ["ARRV_MODE_ARRIVEE"] = "Mode d'arrivée",
            ["ARRV_DATEHEURE"] = "Date et heure d'arrivée",
            ["ARRV_NOM"] = "Nom",
            ["ARRV_REFERENCES"] = "Références",
            ["ARRV_PAIEMENT_JOINT"] = "Paiement joint",
            ["ARRV_CASE_COURRIER"] = "Case courrier",
            ["ARRV_ID_ARRV"] = "N° interne de l'arrivée",
        },
        // RigDatabase\dbo\Tables\CATEGORIE.sql : aucun titre V2 releve (parametrage).
        ["CATEGORIE"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CTGR_ID_MDPRC"] = "Modèle de processus",
            ["CTGR_ID_SRVC"] = "Service",
            ["CTGR_ID_CTGR_FAMILLE"] = "Famille",
            ["CTGR_ID_CTGR_DOMAINE"] = "Domaine",
            ["CTGR_ID_CTGR_ACTIVITE"] = "Activité",
            ["CTGR_CODE_DATE_TRAVAIL"] = "Code de la date de travail",
            ["CTGR_REFERENCE"] = "Référence",
            ["CTGR_ABREGE"] = "Abrégé",
            ["CTGR_NOM"] = "Nom",
            ["CTGR_NIVEAU"] = "Niveau",
            ["CTGR_LIBELLE"] = "Libellé",
            ["CTGR_LIBELLE_ECRAN"] = "Libellé à l'écran",
            ["CTGR_LIBELLE_EDITION"] = "Libellé des éditions",
            ["CTGR_CREATION_DOSSIER"] = "Crée un dossier",
            ["CTGR_CASE_COURRIER"] = "Case courrier",
            ["CTGR_AFFICHAGE_TOUS"] = "Affichée pour tous",
            ["CTGR_DUREE_PREVUE"] = "Durée prévue",
            ["CTGR_DISTRIBUTION_AUTO"] = "Distribution automatique",
            ["CTGR_AFFICHAGE_DISTRI"] = "Affichée à la distribution",
            ["CTGR_PRIORITE_GROUPE"] = "Priorité de groupe",
            ["CTGR_INDEX_AIDE"] = "Index de l'aide",
            ["CTGR_FAMILLE_DOSSIER"] = "Famille de dossier",
            ["CTGR_CODE_DATE_TRAVAIL_CLT"] = "Code de la date de travail (client)",
            ["CTGR_LIBELLE_ECRAN_CLT"] = "Libellé à l'écran (client)",
            ["CTGR_LIBELLE_EDITION_CLT"] = "Libellé des éditions (client)",
            ["CTGR_FAMILLE_DOSSIER_CLT"] = "Famille de dossier (client)",
            ["CTGR_INDEX_AIDE_CLT"] = "Index de l'aide (client)",
            ["CTGR_CASE_COURRIER_CLT"] = "Case courrier (client)",
            ["CTGR_ID_CTGR"] = "N° interne de la catégorie",
            ["CTGR_SAISIE_ARRIVEE"] = "Saisie à l'arrivée",
            ["CTGR_CREATION_ENTREPRISE"] = "Crée une entreprise",
            ["CTGR_REGISTRE_ARRIVEE"] = "Registre des arrivées",
        },
    };
}
