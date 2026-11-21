using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Recopie;

// ============================================================
// LES FICHES PARTAGEES QUI ONT LEUR TABLE DANS LA V3 (REC-012 a REC-017, REC-019 ; lot 10 du plan de reconstruction,
// phase 2, 2026-11-21). Carnet : C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\recopie.json.
//
// UNE declaration par fiche : sa table V3 (Base\migrations\REC-012_fiches-partagees.sql ; soc.Compte pour les agents), sa
// cle, et la correspondance de CHAQUE colonne de la V2 (nom V3, nom V2). Le transfert (REC-001) l'ecrit colonne par colonne
// dans sa table (RegleTable.Cible) ; la recopie continue (REC-002, REC-003) la lit et l'ecrit sous les memes noms. Une colonne
// de la V2 que la correspondance ne nomme pas est PROPRE a la V2 : la V3 ne l'a pas et ne la touche jamais (imprimantes d'un
// poste, deroulement d'un type) ; celles qu'un declencheur de la V2 ecrit (etat de transmission) sont en plus remises a leur
// valeur d'avant apres une recopie (ColonnesPropres, REC-002 CA04).
// Noms V3 : proposes (en francais, sans le prefixe de la V2) ; la story du domaine qui reprendra la fiche peut les changer
// par une migration neuve. Types : ceux de la V2, a l'identique.
// Les six objets d'une action du registre (depot, annonce, inscription au registre, instance, passage au centre de
// formalites, non-inscrit) et la radiation d'une inscription restent dans leur copie telle quelle (v2.*) : leurs colonnes
// metier sont celles de leurs stories RCS et Judiciaire (REC-015 T02) ; ils sont recopies tels quels (ReglesObjetsDuRegistre).
// Mode d'emploi : docs\reconstruction\recopie.md, section « Les fiches partagees (REC-012 a REC-019) ».
// ============================================================

/// <summary>Les traductions communes a plusieurs fiches.</summary>
internal static class TraductionsFiches
{
    /// <summary>REC-013 CA02 : un agent est retrouve d'une base a l'autre par son COMPTE DE SESSION (soc.Compte.CompteWindows =
    /// UTILISATEUR.UTLST_LOGIN, uniques des deux cotes, SOC-001), jamais par son identifiant numerique.</summary>
    public static readonly Traduction ParCompteDeSession = new("le compte de session",
        "(SELECT u.UTLST_ID_UTLST FROM {V2}.UTILISATEUR AS u JOIN soc.Compte AS a ON a.CompteWindows = u.UTLST_LOGIN WHERE a.Id = @v)",
        "(SELECT a.Id FROM soc.Compte AS a JOIN {V2}.UTILISATEUR AS u ON u.UTLST_LOGIN = a.CompteWindows WHERE u.UTLST_ID_UTLST = @v)");

    /// <summary>Un service, par son identifiant V2 que la reprise des comptes garde (soc.Service.IdV2, SOC-001).</summary>
    public static readonly Traduction ParService = new("le service (soc.Service.IdV2)",
        "(SELECT s.IdV2 FROM soc.Service AS s WHERE s.Id = @v)",
        "(SELECT TOP (1) s.Id FROM soc.Service AS s WHERE s.IdV2 = @v ORDER BY s.Id)");
}

/// <summary>La declaration d'une fiche : d'ou sortent sa regle de recopie et, si le transfert copie sa table, sa regle de
/// transfert.</summary>
internal sealed record FicheV3(RegleRecopie Recopie, bool CopieeParLeTransfert = true)
{
    public IReadOnlyList<(string V3, string Expression)> Calculees { get; init; } = [];

    public RegleTable Transfert
    {
        get
        {
            var r = Recopie;
            // Les colonnes attendues (relevees dans RIG_DEV le 2026-11-21) : la cle, la correspondance, les colonnes propres.
            var cle = r.TableV2 switch
            {
                "ADRESSE_BASE" => "ADRBS_ID_ADRBS", "ENTREPRISE" => "ENTRP_ID_RPRTR", "DOSSIER_RCS" => "DSSRC_ID_DSRCS",
                "CLIENT" => "CLIEN_ID_CLIEN", "CLIENT_ADRESSE" => "CLIAD_ID_CLIAD", "CLIENT_CONTACT" => "CLICO_ID_CLICO",
                "ENCAISSEMENT_REPRIS" => "ENRPS_NUMERO_TITRE", "CREANCE_END" => "CNEND_ID_CNEND", "BATEAU" => "BTEAU_ID_BTEAU",
                "INSCRIPTION" => "INEDT_ID_INSCR", "ACTION_RCS" => "ACTRC_ID_ACTRC",
                _ => throw new InvalidOperationException($"{r.TableV2} : la cle V2 de la fiche n'est pas declaree (FicheV3.Transfert)"),
            };
            var attendues = r.Correspondance!.Select(c => c.V2).Concat(r.ColonnesPropres).Append(cle).ToArray();
            return RegleTable.TelleQuelle(r.TableV2, r.Story, attendues) with
            {
                Cible = new CibleFiche(r.TableV3, r.CleV3 ?? "Id", r.Correspondance!) { Calculees = Calculees },
            };
        }
    }

    public static RegleRecopie Fiche(string tableV2, string tableV3, string story, string libelle, string cleV3, params (string V3, string V2)[] colonnes)
        => new(tableV2, tableV3, story, libelle) { Correspondance = [.. colonnes.Select(c => new ColonneFiche(c.V3, c.V2))], CleV3 = cleV3 };
}

internal static class RegleRecopieFiches
{
    /// <summary>La regle, avec la traduction de cette colonne (nom V2).</summary>
    public static RegleRecopie Traduite(this RegleRecopie r, string colonneV2, Traduction traduction)
        => r with { Correspondance = [.. r.Correspondance!.Select(c => string.Equals(c.V2, colonneV2, StringComparison.OrdinalIgnoreCase) ? c with { Traduction = traduction } : c)] };
}

/// <summary>
/// LES FICHES DE REC-012 A REC-017 ET REC-019, une par table V2. La colonne qui date un changement cote V2, celle de son agent,
/// celle qui la nomme et l'ecran qui l'ouvre (REC-004) sont celles du lot 9 (ReglesRec002), gardees.
/// Transmission : DOSSIER_RCS.sql:85-103 et INSCRIPTION.sql:71-89 de la V2 (le declencheur marque « M » ; l'etat « N »,
/// reserve a la migration, le bloque). Les autres tables n'ont pas de declencheur dans RIG_DEV (lu le 2026-11-21).
/// </summary>
internal sealed class ReglesFichesV3 : IReglesRecopie, IReglesTransfert
{
    public static IReadOnlyList<FicheV3> Fiches { get; } =
    [
        // REC-016 : l'adresse partagee. Le transfert ne garde qu'UNE adresse inconnue (AdresseInconnueUnique) ; la premiere
        // est marquee ici, les autres y sont rattachees apres les tables.
        new FicheV3(FicheV3.Fiche("ADRESSE_BASE", "rec.Adresse", "REC-016", "adresse", "Id",
            ("NumeroVoie", "ADRBS_NUM_VOIE"), ("ComplementNumero", "ADRBS_COMPLEMENT_NO"), ("TypeVoie", "ADRBS_TYPE_VOIE"),
            ("NomVoie", "ADRBS_NOM_VOIE"), ("CodePostal", "ADRBS_CODE_POSTAL"), ("Localite", "ADRBS_LOCALITE_POSTALE"),
            ("Pays", "ADRBS_PAYS"), ("CodeRoutage", "ADRBS_CODE_ROUTAGE"), ("EtatInfo", "ADRBS_ETAT_INFO"),
            ("FormateePourMediapost", "ADRBS_FORMATE_POUR_MEDIAPOST"), ("PreAdresse", "ADRBS_PRE_ADRESSE"), ("SuiteAdresse", "ADRBS_SUITE_ADRESSE"),
            ("BoitePostale", "ADRBS_BOITE_POSTALE"), ("Cedex", "ADRBS_CEDEX"), ("LieuRattache", "ADRBS_LIEU_RATTACHE"),
            ("DivisionTerritoriale", "ADRBS_DIVISION_TERRIT"), ("TypeSpecial", "ADRBS_TYPE_SPECIAL"), ("ValeurSpecial", "ADRBS_VALEUR_SPECIAL"),
            ("CodeInsee", "ADRBS_CODE_INSEE")) with { ColonneNom = "ADRBS_NOM_VOIE" })
        {
            Calculees = [("EstInconnue", $"CASE WHEN c.ADRBS_NOM_VOIE = '{AdresseInconnueUnique.NomVoie}' AND NOT EXISTS (SELECT 1 FROM dbo.ADRESSE_BASE AS d " +
                                         "WHERE d.ADRBS_NOM_VOIE = c.ADRBS_NOM_VOIE AND d.ADRBS_ID_ADRBS < c.ADRBS_ID_ADRBS) THEN CONVERT(bit, 1) ELSE CONVERT(bit, 0) END")],
        },
        // REC-012 : la fiche de personne du registre.
        new FicheV3(FicheV3.Fiche("ENTREPRISE", "rec.Entreprise", "REC-012", "entreprise", "Id",
            ("IdAdresse", "ENTRP_ID_ADRBS"), ("IdSousAdresse", "ENTRP_ID_SSADR_X"), ("Siren", "ENTRP_SIREN"),
            ("Civilite", "ENTRP_CIVILITE"), ("Denomination", "ENTRP_DESIGNATION"), ("Patronyme", "ENTRP_PATRONYME"),
            ("Prenom", "ENTRP_PRENOM"), ("DateNaissance", "ENTRP_DATE_NAISSANCE"), ("DateNaissanceNonCommuniquee", "ENTRP_DATE_NAISSANCE_NC"),
            ("FormeJuridique", "ENTRP_FORME_JURIDIQUE_UTI"), ("Sigle", "ENTRP_SIGLE"), ("ComplementDenomination", "ENTRP_COMPLEMENT_DESIGN"),
            ("CodeNaf", "ENTRP_CODE_NAF"), ("EtatInfo", "ENTRP_ETAT_INFO"), ("FicheSommaire", "ENTRP_FICHE_SOMMAIRE"),
            ("CodeApe", "ENTRP_CODE_APE"), ("NomUsage", "ENTRP_NOM_USAGE")) with { ColonneNom = "ENTRP_DESIGNATION" }),
        // REC-012 : le dossier au registre ; son etat de transmission est celui de la V2 (CA02).
        new FicheV3(FicheV3.Fiche("DOSSIER_RCS", "rec.DossierRcs", "REC-012", "dossier RCS", "Id",
            ("IdEntreprise", "DSSRC_ID_RPRTR"), ("TypeRcs", "DSSRC_TYPE_RCS"), ("NumeroGestion", "DSSRC_NUM_GESTION"),
            ("FinalGestion", "DSSRC_FINAL_GESTION"), ("Siren", "DSSRC_SIREN"), ("DateImmatriculation", "DSSRC_DATE_IMMAT"),
            ("MotifImmatriculation", "DSSRC_MOTIF_IMMAT"), ("DateEffet", "DSSRC_DATE_EFFET"), ("NumeroAnterieur", "DSSRC_NUM_ANTERIEUR"),
            ("ProvisoirementSans", "DSSRC_PROVISOIREMENT_SANS"), ("DateCessation", "DSSRC_DATE_CESSATION"), ("DateRadiation", "DSSRC_DATE_RADIATION"),
            ("CompteurMaj", "DSSRC_COMPTEUR_MAJ"), ("DateMaj", "DSSRC_DATE_MAJ"), ("EtatDossier", "DSSRC_ETAT_DOSSIER"),
            ("EtatInfo", "DSSRC_ETAT_INFO"), ("EtatVax", "DSSRC_ETAT_VAX"), ("DateMajInfogreffe", "DSSRC_DATE_MAJ_INFOG"),
            ("DateMajIntergreffe", "DSSRC_DATE_MAJ_INTERG"), ("DateMajCaducite", "DSSRC_DATE_MAJ_CADUCITE"), ("DateFormaliteCessation", "DSSRC_DATE_FORMALITE_CESS"),
            ("NumeroArchivage", "DSSRC_NUM_ARCHIVAGE"), ("ProcedureCollective", "DSSRC_PROCEDURE_PC"), ("InscritRegistreMetiers", "DSSRC_INSCRIT_REGISTRE_METIER"),
            ("EtatInfogreffeAnterieur", "DSSRC_ETAT_INFOG_ANT"), ("NumeroInterne", "DSSRC_NOINT"), ("ModificationInterne", "DSSRC_TOP_MODIFINT"),
            ("DepartementRm", "DSSRC_DPT_RM"), ("SirenProvisoire", "DSSRC_SIREN_PROVISOIRE"), ("Accre", "DSSRC_ACCRE"),
            ("RenvoiExperian", "DSSRC_BUG_RENVOI_EXPERIAN"), ("AutoEntrepreneur", "DSSRC_AUTO_ENTREPRENEUR"), ("DateDebut", "DSSRC_DATE_DEBUT"),
            ("Eirl", "DSSRC_EIRL"), ("DatePremiereImmatriculation", "DSSRC_DATE_PREMIERE_IMMATRICULATION"), ("AutoEntrepreneurRegularise", "DSSRC_AUTO_ENTREPRENEUR_REGUL"),
            ("InpiEtat", "DSSRC_INPI_ETAT"), ("InpiIoEtat", "DSSRC_INPI_IO_ETAT"), ("InpiIoDateEnvoi", "DSSRC_INPI_IO_DATE_ENVOI"),
            ("Euid", "DSSRC_EUID")) with
        {
            ColonnesPropres = ["DSSRC_ETAT_TRANSMISSION", "DSSRC_DATE_MODIF_FLAG_TRANS"],
            CreationV2 = new Dictionary<string, string> { ["DSSRC_ETAT_TRANSMISSION"] = "'N'" },
            ColonneDateV2 = "DSSRC_DATE_MAJ", ColonneNom = "DSSRC_NUM_GESTION", Cible = "dossier:{DSSRC_NUM_GESTION}",
        }),
        // REC-013 : le client. Son agent de rattachement par son compte de session (CA02) ; son code abrege unique dans la V3
        // (T01, CA03) -- les doublons herites de la V2 sont gardes et marques au transfert.
        new FicheV3(FicheV3.Fiche("CLIENT", "rec.Client", "REC-013", "client", "Id",
            ("Abrege", "CLIEN_ABREGE"), ("NumeroCompte", "CLIEN_NUMERO_COMPTE"), ("Nom", "CLIEN_NOM"),
            ("Prenom", "CLIEN_PRENOM"), ("Type", "CLIEN_TYPE"), ("Civilite", "CLIEN_CIVILITE"),
            ("Statut", "CLIEN_STATUT"), ("SeuilCompte", "CLIEN_SEUIL_COMPTE"), ("CompteCollectif", "CLIEN_COMPTE_COLLECTIF"),
            ("Groupe", "CLIEN_GROUPE"), ("ModePaiement", "CLIEN_MODE_PAIEMENT"), ("DateDerniereModification", "CLIEN_DATE_DERNIERE_MODIF"),
            ("DateReport", "CLIEN_DATE_REPORT"), ("AssujettiTva", "CLIEN_ASSUJETTI_TVA"), ("ReleveFacture", "CLIEN_RELEVE_FACTURE"),
            ("BlocNotes", "CLIEN_BLOC_NOTES"), ("MontantReport", "CLIEN_MONTANT_REPORT"), ("IdCompteRattachement", "CLIEN_ID_UTLST"),
            ("Email", "CLIEN_EMAIL"), ("Telephone", "CLIEN_TELEPHONE"), ("TelephonePortable", "CLIEN_TELEPHONE_PORTABLE"),
            ("Fax", "CLIEN_FAX"), ("EstComptePc", "CLIEN_IS_COMPTE_PC"), ("EstCompteWeb", "CLIEN_IS_COMPTE_WEB"),
            ("CodeGreffe", "CLIEN_CODE_GREFFE"), ("EtatEdi", "CLIEN_ETAT_EDI"),
            ("NumeroEdi", "CLIEN_NUM_EDI"), ("DateEdi", "CLIEN_DATE_EDI"), ("Siren", "CLIEN_SIREN"),
            ("DateNaissance", "CLIEN_DATE_NAISSANCE"), ("TvaIntracommunautaire", "CLIEN_TVA_INTRA"), ("Siret", "CLIEN_SIRET"))
            .Traduite("CLIEN_ID_UTLST", TraductionsFiches.ParCompteDeSession) with
        {
            // fix-ok: auteur V2 d'un ecart client toujours vide -- mesure 2026-11-21 par RecopieDeuxSensCheck (REC-004 CA01 rouge :
            // CLIEN_ID_UTLST 1 -> 189 change par la V2, AuteurV2 vide) : la regle ne nommait pas la colonne d'agent de la V2.
            // Client.Sauve de la V2 (RigMetier\Facture\Client.cs:522-523) ecrit la date ET l'agent courant ; sa fiche client les
            // affiche (« Derniere modification le ... par ... », PROC_FCLIENT.cs:47). (Regle du lot 9, gardee.)
            ColonneDateV2 = "CLIEN_DATE_DERNIERE_MODIF", ColonneAuteurV2 = "CLIEN_ID_UTLST", ColonneNom = "CLIEN_NOM", Cible = "ecran:PROC_FCLIENT",
            Unique = new CleMetier("CLIEN_ABREGE", "code abrege", UniqueDansV2: false, UniqueDansV3: true),
        })
        {
            Calculees = [("DoublonHerite", "CASE WHEN c.CLIEN_ABREGE IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.CLIENT AS d WHERE d.CLIEN_ABREGE = c.CLIEN_ABREGE " +
                                           "AND d.CLIEN_ID_CLIEN < c.CLIEN_ID_CLIEN) THEN CONVERT(bit, 1) ELSE CONVERT(bit, 0) END")],
        },
        // REC-013 CA05 : ses adresses (le lien vers l'adresse partagee n'a pas de cle dans la V2) et ses contacts.
        new FicheV3(FicheV3.Fiche("CLIENT_ADRESSE", "rec.ClientAdresse", "REC-013", "adresse de client", "Id",
            ("IdClient", "CLIAD_ID_CLIEN"), ("LigneAdresse", "CLIAD_LIGNE_ADRESSE"), ("SuiteAdresse", "CLIAD_SUITE_ADRESSE"),
            ("CodePostal", "CLIAD_CODE_POSTAL"), ("Localite", "CLIAD_LOCALITE_POSTALE"), ("ParDefaut", "CLIAD_ADRESSE_PAR_DEFAUT"),
            ("TypeAdresse", "CLIAD_TYPE_ADRESSE"), ("Pays", "CLIAD_PAYS"), ("IdAdresse", "CLIAD_ID_ADRBS")) with
        {
            ColonneNom = "CLIAD_LIGNE_ADRESSE", LiensSansCle = new Dictionary<string, string> { ["CLIAD_ID_ADRBS"] = "ADRESSE_BASE" },
        }),
        new FicheV3(FicheV3.Fiche("CLIENT_CONTACT", "rec.ClientContact", "REC-013", "contact de client", "Id",
            ("Nom", "CLICO_NOM"), ("Prenom", "CLICO_PRENOM"), ("Telephone", "CLICO_TELEPHONE"),
            ("Fonction", "CLICO_FONCTION"), ("Service", "CLICO_SERVICE"), ("Fax", "CLICO_FAX"),
            ("Email", "CLICO_EMAIL"), ("Portable", "CLICO_PORTABLE"), ("IdClient", "CLICO_ID_CLIEN")) with { ColonneNom = "CLICO_NOM" }),
        // REC-013 CA06 : les provisions reprises ; leur consommation passe par ProvisionsReprises (la V2 reserve d'abord).
        new FicheV3(FicheV3.Fiche("ENCAISSEMENT_REPRIS", "rec.ProvisionReprise", "REC-013", "provision reprise", "NumeroTitre",
            ("Libelle", "ENRPS_LIBELLE"), ("Montant", "ENRPS_MONTANT"), ("NomDossier", "ENRPS_NOM_DOSSIER"),
            ("Banque", "ENRPS_BANQUE"), ("Devise", "ENRPS_DEVISE"), ("Date", "ENRPS_DATE"),
            ("Consommee", "ENRPS_CONSOMMER")) with { ColonneNom = "ENRPS_NOM_DOSSIER" }),
        // REC-014 : la creance et le bateau d'une inscription, ecrits avant elle (liens de la V2).
        new FicheV3(FicheV3.Fiche("CREANCE_END", "rec.Creance", "REC-014", "creance", "Id",
            ("LiquideEvaluee", "CNEND_LIQUIDE_EVALUEE"), ("Monnaie", "CNEND_MONNAIE"), ("MontantPrincipal", "CNEND_MONTANT_PRINCIPAL"),
            ("TauxInteret", "CNEND_TAUX_INTERET"), ("MontantAccessoires", "CNEND_MONTANT_ACCESSOIRES"), ("MontantTotal", "CNEND_MONTANT_TOTAL"),
            ("DateExpiration", "CNEND_DATE_EXPIRATION"), ("Duree", "CNEND_DUREE"), ("DateEcheance", "CNEND_DATE_ECHEANCE"),
            ("SaufMemoire", "CNEND_SAUF_MEMOIRE"), ("CreancesFutures", "CNEND_CREANCES_FUTURES"), ("Article244", "CNEND_ARTICLE_244"),
            ("DispositionsParticulieres", "CNEND_DISPOSITIONS_PARTICULIERES"), ("AlienableChosesFongibles", "CNEND_ALIENABLE_CHOSES_FONGIBLES"),
            ("AlienablePartsSociales", "CNEND_ALIENABLE_PARTS_SOCIALES"), ("ActionResolutoire", "CNEND_ACTION_RESOLUTOIRE"),
            ("PacteCommissoire", "CNEND_PACTE_COMMISSOIRE"), ("MarqueBrevetDessinModele", "CNEND_MARQUE_BREVET_DESSIN_MODELE"))),
        new FicheV3(FicheV3.Fiche("BATEAU", "rec.Bateau", "REC-014", "bateau", "Id",
            ("LieuImmatriculation", "BTEAU_LIEU_IMMAT"), ("Abrege", "BTEAU_ABREGE"), ("DateImmatriculation", "BTEAU_DATE_IMMAT"),
            ("Nom", "BTEAU_NOM"), ("Proprietaire", "BTEAU_PROPRIETAIRE"), ("Historique", "BTEAU_HISTORIQUE"),
            ("NumeroEnregistrement", "BTEAU_NUM_ENREGISTREMENT"), ("NumeroMatriculeDouane", "BTEAU_NUM_MATRICULE_DOUANE"), ("NumeroOmi", "BTEAU_NUM_OMI"),
            ("NumeroCinHinWin", "BTEAU_NUM_CIN_HIN_WIN"), ("Port", "BTEAU_PORT"), ("Registre", "BTEAU_REGISTRE"),
            ("DomaineCommerce", "BTEAU_DOMAINE_COMMERCE"), ("Longueur", "BTEAU_LONGUEUR"), ("Jauge", "BTEAU_JAUGE"),
            ("Nationalite", "BTEAU_NATIONALITE"), ("NomGardien", "BTEAU_NOM_GARDIEN"), ("EstDerniere", "BTEAU_IS_LAST"),
            ("EstNavire", "BTEAU_IS_NAVIRE"), ("ApiIdCompte", "BTEAU_API_ID_UTLST"), ("ApiDateAppel", "BTEAU_API_DATE_APPEL"),
            ("ApiUrl", "BTEAU_API_URL"), ("ApiJson", "BTEAU_API_JSON"), ("ApiErreurs", "BTEAU_API_DATA_ERREURS"),
            ("ApiDonneesModifiees", "BTEAU_API_IS_DATA_MODIF"))
            .Traduite("BTEAU_API_ID_UTLST", TraductionsFiches.ParCompteDeSession) with { ColonneNom = "BTEAU_NOM" }),
        // REC-014 : l'inscription, sa chaine de renouvellements (INEDT_ID_INSCR_INSCRIPTION_PRE, lien sur elle-meme) ; son etat de
        // transmission est celui de la V2 (CA03).
        new FicheV3(FicheV3.Fiche("INSCRIPTION", "rec.Inscription", "REC-014", "inscription", "Id",
            ("IdProcedureCollective", "INEDT_ID_PRCLL"), ("IdInscriptionPrecedente", "INEDT_ID_INSCR_INSCRIPTION_PRE"), ("IdSousAdresse", "INEDT_ID_SSADR_X"),
            ("IdAdresse", "INEDT_ID_ADRBS"), ("IdCreance", "INEDT_ID_CNEND"), ("IdBateau", "INEDT_ID_BTEAU"),
            ("NumeroInscription", "INEDT_NUM_INSCRIPTION"), ("TypeInscription", "INEDT_TYPE_INSCRIPTION"), ("ParTransfert", "INEDT_PAR_TRANSFERT"),
            ("DateInscription", "INEDT_DATE_INSCRIPTION"), ("DatePeremption", "INEDT_DATE_PEREMPTION"), ("DateEffet", "INEDT_DATE_EFFET"),
            ("Renouvelable", "INEDT_RENOUVELABLE"), ("EtatInfo", "INEDT_ETAT_INFO"), ("DateDiffusion", "INEDT_DATE_DIFFUSION"),
            ("EtatVax", "INEDT_ETAT_VAX"), ("DateMaj", "INEDT_DATE_MAJ"), ("DateMajInfogreffe", "INEDT_DATE_MAJ_INFOG"),
            ("DateMajIntergreffe", "INEDT_DATE_MAJ_INTERG"), ("NumeroRegistre", "INEDT_NUM_REGISTRE"), ("EtatTraitement", "INEDT_ETAT_TRAITEMENT"),
            ("DateTraitement", "INEDT_DATE_TRAITEMENT"), ("Uuid", "INEDT_UUID")) with
        {
            ColonnesPropres = ["INEDT_ETAT_TRANSMISSION", "INEDT_DATE_MODIF_FLAG_TRANS"],
            CreationV2 = new Dictionary<string, string> { ["INEDT_ETAT_TRANSMISSION"] = "'N'" },
            ColonneDateV2 = "INEDT_DATE_MAJ", ColonneNom = "INEDT_NUM_INSCRIPTION",
        }),
        // REC-015 : l'historique des actions d'un dossier ; ses objets passent avant elle (liens de la V2).
        new FicheV3(FicheV3.Fiche("ACTION_RCS", "rec.ActionRegistre", "REC-015", "action du registre", "Id",
            ("IdDepot", "ACTRC_ID_DPTRC"), ("IdAnnonceBodacc", "ACTRC_ID_BDCRC"), ("IdInscriptionRegistre", "ACTRC_ID_INSRC"),
            ("IdDossier", "ACTRC_ID_DSRCS"), ("IdPassageCfe", "ACTRC_ID_PSSCF"), ("IdNonInscrit", "ACTRC_ID_NINSC"),
            ("NumeroArrivee", "ACTRC_NUM_ARRIVEE"), ("DateArrivee", "ACTRC_DATE_ARRIVEE"), ("CodeProcessus", "ACTRC_CODE_PROSS"),
            ("EtatInfo", "ACTRC_ETAT_INFO"), ("EtatAction", "ACTRC_ETAT_ACTION"), ("SurNotification", "ACTRC_SUR_NOTIFICATION"),
            ("IdInstance", "ACTRC_ID_INSTN"), ("IdDepotHistorique", "ACTRC_ID_DPTRCH")) with { ColonneNom = "ACTRC_NUM_ARRIVEE" }),
        // REC-019 : le type de demande, par sa reference. Son identifiant est celui de la V3 : le transfert le porte par sa
        // reprise (TypesDemande.cs), pas par une copie. La V3 ne cree jamais un type dans la V2 (CA03) et n'y change que les
        // champs partages ; le deroulement du type (CTGR_ID_MDPRC) reste celui de la V2 (CA04).
        new FicheV3(FicheV3.Fiche("CATEGORIE", "rec.TypeDemande", "REC-019", "type de demande", "Id",
            ("IdService", "CTGR_ID_SRVC"), ("IdFamille", "CTGR_ID_CTGR_FAMILLE"), ("IdDomaine", "CTGR_ID_CTGR_DOMAINE"),
            ("IdActivite", "CTGR_ID_CTGR_ACTIVITE"), ("CodeDateTravail", "CTGR_CODE_DATE_TRAVAIL"), ("Reference", "CTGR_REFERENCE"),
            ("Abrege", "CTGR_ABREGE"), ("Nom", "CTGR_NOM"), ("Niveau", "CTGR_NIVEAU"),
            ("Libelle", "CTGR_LIBELLE"), ("LibelleEcran", "CTGR_LIBELLE_ECRAN"), ("LibelleEdition", "CTGR_LIBELLE_EDITION"),
            ("CreationDossier", "CTGR_CREATION_DOSSIER"), ("CaseCourrier", "CTGR_CASE_COURRIER"), ("AffichageTous", "CTGR_AFFICHAGE_TOUS"),
            ("DureePrevue", "CTGR_DUREE_PREVUE"), ("DistributionAuto", "CTGR_DISTRIBUTION_AUTO"), ("AffichageDistribution", "CTGR_AFFICHAGE_DISTRI"),
            ("PrioriteGroupe", "CTGR_PRIORITE_GROUPE"), ("IndexAide", "CTGR_INDEX_AIDE"), ("FamilleDossier", "CTGR_FAMILLE_DOSSIER"),
            ("CodeDateTravailClt", "CTGR_CODE_DATE_TRAVAIL_CLT"), ("LibelleEcranClt", "CTGR_LIBELLE_ECRAN_CLT"), ("LibelleEditionClt", "CTGR_LIBELLE_EDITION_CLT"),
            ("FamilleDossierClt", "CTGR_FAMILLE_DOSSIER_CLT"), ("IndexAideClt", "CTGR_INDEX_AIDE_CLT"), ("CaseCourrierClt", "CTGR_CASE_COURRIER_CLT"),
            ("SaisieArrivee", "CTGR_SAISIE_ARRIVEE"), ("CreationEntreprise", "CTGR_CREATION_ENTREPRISE"), ("RegistreArrivee", "CTGR_REGISTRE_ARRIVEE"))
            .Traduite("CTGR_ID_SRVC", TraductionsFiches.ParService) with
        {
            ColonnesPropres = ["CTGR_ID_MDPRC"], ColonneNom = "CTGR_LIBELLE_ECRAN", SansCreationV2 = true,
            // Le domaine, la famille et l'activite d'un type sont d'autres types, lus par la correspondance (la V3 donne ses
            // propres numeros, RepriseTypesDemande). La V2 les tient par trois cles etrangeres vers CTGR_ID_CTGR (actives et
            // verifiees sur RIG_DEV et RIG_RECETTE, lu le 2026-11-21) ; declares ici aussi, ils restent traduits sur une base
            // ou ces cles manqueraient -- sinon un numero V2 y serait ecrit dans la V3.
            LiensSansCle = new Dictionary<string, string>
            {
                ["CTGR_ID_CTGR_DOMAINE"] = "CATEGORIE", ["CTGR_ID_CTGR_FAMILLE"] = "CATEGORIE", ["CTGR_ID_CTGR_ACTIVITE"] = "CATEGORIE",
            },
        }, CopieeParLeTransfert: false),
        // REC-017 : le compte d'un agent, celui de la V3 (soc.Compte, SOC-001), porte par la reprise des comptes ; recopie par
        // son nom de session, unique des deux cotes (CA04). Les reglages du poste dans la V2 (imprimantes, scanner, bac de
        // sortie, derniere session) et son mot de passe n'ont pas de colonne dans la V3 : jamais ecrases, vides pour un compte
        // cree par la V3 (CA03). Les droits (roles) restent ceux des stories du Socle commun (T02).
        new FicheV3(FicheV3.Fiche("UTILISATEUR", "soc.Compte", "REC-017", "compte d'agent", "Id",
            ("CompteWindows", "UTLST_LOGIN"), ("Nom", "UTLST_NOM"), ("Prenom", "UTLST_PRENOM"),
            ("Abrege", "UTLST_ABREGE"), ("Civilite", "UTLST_CIVILITE"), ("CodeGreffe", "UTLST_CODE_GREFFE"),
            ("IdService", "UTLST_ID_SRVC"), ("Desactive", "UTLST_DESACTIVE"), ("EstAmitel", "UTLST_ISAMITEL"),
            ("EstAdministrateur", "UTLST_ISADMIN"), ("EstAssistance", "UTLST_IS_HOTLINE"))
            .Traduite("UTLST_ID_SRVC", TraductionsFiches.ParService) with
        {
            ColonneNom = "UTLST_LOGIN", Unique = new CleMetier("UTLST_LOGIN", "nom de session", UniqueDansV2: true, UniqueDansV3: true),
        }, CopieeParLeTransfert: false),
    ];

    public IEnumerable<RegleRecopie> Regles => Fiches.Select(f => f.Recopie);

    IEnumerable<RegleTable> IReglesTransfert.Regles => Fiches.Where(f => f.CopieeParLeTransfert).Select(f => f.Transfert);
}

/// <summary>
/// LES OBJETS QU'UNE ACTION DU REGISTRE VISE (REC-015) ET LA RADIATION D'UNE INSCRIPTION (REC-014 CA04), TELS QUELS. Leurs
/// colonnes metier sont celles de leurs stories RCS et Judiciaire (REC-015 T02) : en attendant, la V3 les tient dans leur
/// copie v2.* et la recopie les passe telles quelles dans les deux sens, avant l'action (ou l'inscription) qui les vise.
/// Transmission : DEPOT_RCS a son declencheur (T_DEPOT_RCS_MAJ_ETAT_TRANSMISSION, « M » sur une date ou un numero change) ;
/// INSTANCE remet a 0 le « top Experian » quand sa date de mise a jour Infogreffe change (T_INSTANCE_MAJ_TOP_EXPERIAN) :
/// colonnes propres, remises a leur valeur d'avant (lus dans RIG_DEV le 2026-11-21). La radiation : la V2 radie une
/// inscription en lui ecrivant une ligne RADIATION_TOTALE (Inscription.IsRadieeOuPerimee, RigMetier\Inscription\Inscription.cs:33).
/// Colonnes attendues relevees dans RIG_DEV le 2026-11-21.
/// </summary>
internal sealed class ReglesObjetsDuRegistre : IReglesRecopie, IReglesTransfert
{
    public IEnumerable<RegleRecopie> Regles =>
    [
        RegleRecopie.TelleQuelle("BODACC", "REC-015", "annonce BODACC") with { ColonneNom = "BDCCR_NUMERO_ANNONCE" },
        RegleRecopie.TelleQuelle("DEPOT_RCS", "REC-015", "dépôt") with
        {
            ColonnesPropres = ["DPTRC_ETAT_TRANSMISSION", "DPTRC_DATE_MODIF_FLAG_TRANS"],
            CreationV2 = new Dictionary<string, string> { ["DPTRC_ETAT_TRANSMISSION"] = "'N'" }, ColonneNom = "DPTRC_NUM_DEPOT",
        },
        RegleRecopie.TelleQuelle("INSCRIPTION_RCS", "REC-015", "inscription au registre") with { ColonneNom = "INSRC_NUM_CHRONO" },
        RegleRecopie.TelleQuelle("INSTANCE", "REC-015", "instance") with { ColonnesPropres = ["INSTN_TOP_EXPERIAN"], ColonneNom = "INSTN_NOM_COURANT" },
        RegleRecopie.TelleQuelle("NON_INSCRIT", "REC-015", "non-inscrit") with { ColonneNom = "NINSC_DENOMINATION" },
        RegleRecopie.TelleQuelle("PASSAGE_CFE", "REC-015", "passage au centre de formalités") with { ColonneNom = "PSSGC_NUM_LIAISON" },
        RegleRecopie.TelleQuelle("RADIATION_TOTALE", "REC-014", "radiation d'inscription") with { ColonneNom = "RDTTL_NUM_REGISTRE" },
    ];

    IEnumerable<RegleTable> IReglesTransfert.Regles =>
    [
        RegleTable.TelleQuelle("BODACC", "REC-015",
            "BDCCR_ID_EVNPC", "BDCCR_ID_DMND_TE", "BDCCR_ID_BRDRC", "BDCCR_BODACC_PREALABLE", "BDCCR_TYPE_BODACC", "BDCCR_TEXTE_BODACC",
            "BDCCR_DATE_ENVOI", "BDCCR_NUM_BORDEREAU", "BDCCR_DATE_PUBLICITE", "BDCCR_COUT_BODACC", "BDCCR_MONNAIE", "BDCCR_TARIF_BO_APPLIQUE",
            "BDCCR_BODACC_ELECTRONIQUE", "BDCCR_ETAT_BODACC", "BDCCR_ETAT_ELECT", "BDCCR_DATE_ENVOI_TE", "BDCCR_NUM_PARUTION", "BDCCR_NUM_FACTURE",
            "BDCCR_BODACC_PAPIER", "BDCCR_ID_BDCRC", "BDCCR_NUM_OSP", "BDCCR_ID_EVNPR", "BDCCR_NUMERO_SOURCE", "BDCCR_MOTIF_REJET_BODACC_1",
            "BDCCR_MOTIF_REJET_BODACC_2", "BDCCR_COMMENTAIRE", "BDCCR_TEXTE_OBSERVATION", "BDCCR_NUM_AP", "BDCCR_AP_SUR_NI", "BDCCR_FLUX_XML",
            "BDCCR_DATE_CREA_XML", "BDCCR_ETAT_XML", "BDCCR_NUMERO_ANNONCE", "BDCCR_IDENTIFIANT_CLIENT", "BDCCR_TYPE_ANNONCE", "BDCCR_ID_PREC_BOD",
            "BDCCR_CODE_EP_SUPPR", "BDCCR_DATE_EP_SUPPR", "BDCCR_DATE_FACTURE", "BDCCR_MONTANT_FACTURE", "BDCCR_DEVISE_FACTURE",
            "BDCCR_IDENTIFIANT_REEMISSION", "BDCCR_TYPE_BODACC_2", "BDCCR_CONTROLE_QUALITE", "BDCCR_DATE_LIBRE", "BDCCR_TEXTE_LIBRE",
            "BDCCR_CONTROLE_QUALITE_VERIFIE", "BDCCR_ID_BDCRC_AP", "BDCCR_INFOG_ETAT", "BDCCR_INFOG_DATE_ENVOI", "BDCCR_INFOG_DATE_RETOUR",
            "BDCCR_INFOG_MOTIF_REJET", "BDCCR_URL_ANNONCE"),
        RegleTable.TelleQuelle("DEPOT_RCS", "REC-015",
            "DPTRC_ID_EXRCC", "DPTRC_NUM_ARRIVEE", "DPTRC_NUM_DEPOT", "DPTRC_DATE", "DPTRC_CATEGORIE_DEPOT", "DPTRC_NUMERISE", "DPTRC_SAISIE_SIMPLIFIEE",
            "DPTRC_ENVOI_INPI", "DPTRC_ETAT_INFO", "DPTRC_ID_DPTRC", "DPTRC_ETAT_VAX", "DPTRC_DATE_MAJ_INTERG", "DPTRC_DATE_MAJ_INFOG", "DPTRC_GRATUIT",
            "DPTRC_NUM_DEPOT_ANNULE", "DPTRC_NUM_DEPOT_REMPLACE", "DPTRC_ETAT_TRANSMISSION", "DPTRC_DATE_MODIF_FLAG_TRANS", "DPTRC_DATE_MAJ_VAX",
            "DPTRC_SANS_BODACC", "DPTRC_ID_LTINPI", "DPTRC_ID_DMTDCA", "DPTRC_GUID_VIDIGED_BordereauMandataire", "DPTRC_VIDI_TAILLE_DOC",
            "DPTRC_VIDI_NB_PAGE", "DPTRC_DIFFUSABILITE_CONFIDENTIELLE", "DPTRC_HISTO_DIFFU_CONFI", "DPTRC_DISPENSE_ANNEXE",
            "DPTRC_RESULTAT_CONFIDENTIEL", "DPTRC_HISTO_RESULTAT_CONFI", "DPTRC_PRESENTATION_SIMPLIFIEE", "DPTRC_HISTO_PRESENTATION_SIMPLIFIEE",
            "DPTRC_DATE_REPLICATION_VIDIGED_BordereauMandataire"),
        RegleTable.TelleQuelle("INSCRIPTION_RCS", "REC-015",
            "INSRC_NUM_CHRONO", "INSRC_TYPE_INS_RCS", "INSRC_DATE_FORMALITE", "INSRC_DATE_EFFET_PPAL", "INSRC_ENVOI_INPI", "INSRC_ETAT_INFO",
            "INSRC_ID_INSRC", "INSRC_ID_LTINPI", "INSRC_ID_RGAUT", "INSRC_INPI_ETAT", "INSRC_INPI_ETAT_DATA", "INSRC_INPI_IO_ETAT",
            "INSRC_INPI_IO_DATE_ENVOI"),
        RegleTable.TelleQuelle("INSTANCE", "REC-015",
            "INSTN_ID_INSTN", "INSTN_ID_AFFRE", "INSTN_ID_PRCLL", "INSTN_ID_JUG", "INSTN_ID_CRNPC", "INSTN_CODE_AFFAIRE", "INSTN_NOM_COURANT",
            "INSTN_DATE_ENROLEMENT", "INSTN_NUM_ROLE", "INSTN_NUM_REP_GENERAL", "INSTN_QUALIF_JURIDICTION", "INSTN_QUALIF_PROCEDURE",
            "INSTN_NATURE_AFFAIRE", "INSTN_CODE_ACTE_PROCE", "INSTN_DATE_ACTE", "INSTN_TEXTE_ACTE", "INSTN_DELIVRANCE", "INSTN_PREMIERE_AUDIENCE",
            "INSTN_CALENDRIER", "INSTN_NUM_ETAPE_CAL", "INSTN_DATE_DEBUT_CAL", "INSTN_ETAT_INSTANCE", "INSTN_ETAT_INFO", "INSTN_ETAT_VAX",
            "INSTN_DATE_MAJ_INFOG", "INSTN_DATE_MAJ_INTERG", "INSTN_ID_INSTN_INSTANCE_PRINCIPA", "INSTN_NUM_ORDRE", "INSTN_DATE_JONCTION",
            "INSTN_CAUSE_JONCTION", "INSTN_TYPE_JONCTION", "INSTN_COMPTABLE_JONCTION", "INSTN_DEPENS_JONCTION", "INSTN_NOM_JONCTION",
            "INSTN_NUMERO_SOURCE", "INSTN_ETAT_TRANSMISSION", "INSTN_ID_CRTRA", "INSTN_ID_PREVE", "INSTN_TYPE_INSTANCE", "INSTN_NAT_AFF_PART",
            "INSTN_TOP_EXPERIAN", "INSTN_DATE_EXPERIAN", "INSTN_REPRISE_TGI", "INSTN_NOM_TGI", "INSTN_NOTE_AUDIENCE", "INSTN_TIMBRE_AJ",
            "INSTN_EXONERATION_TIMBRE_AJ", "INSTN_DATE_TIMBRE_AJ", "INSTN_DEMAT_TIMBRE_AJ", "INSTN_DATE_SAISIE_AJ", "INSTN_DATE_SAISINE_GREFFE",
            "INSTN_ID_DSJUD", "INSTN_IP_EUROPEENNE", "INSTN_AVIS_MP", "INSTN_PRESENCE_MP", "INSTN_DEMAT_ORIGINE"),
        RegleTable.TelleQuelle("NON_INSCRIT", "REC-015",
            "NINSC_ID_RPRTR", "NINSC_NUM_SIREN", "NINSC_CIVILITE", "NINSC_PATRONYME", "NINSC_PRENOMS", "NINSC_NOM_USAGE", "NINSC_DATE_NAISSANCE",
            "NINSC_DATE_NAISSANCE_NC", "NINSC_LIEU_NAISSANCE", "NINSC_DEPARTEMENT_NAISS", "NINSC_PAYS", "NINSC_NATIONALITE", "NINSC_CODE_SITUATION",
            "NINSC_DENOMINATION", "NINSC_SIGLE", "NINSC_MONNAIE", "NINSC_MONTANT", "NINSC_NOM_COMMERCIAL", "NINSC_ENSEIGNE_GENERALE", "NINSC_TEXTE",
            "NINSC_ETAT_INFO", "NINSC_DATE_MAJ", "NINSC_COMPTEUR_MAJ", "NINSC_NUM_NON_INSCRIT", "NINSC_FINAL_NON_INSCRIT", "NINSC_PRINCIPAL",
            "NINSC_DATE_RADIATION", "NINSC_NUM_GESTION_VAX", "NINSC_NUM_GESTION_VAX2", "NINSC_NUM_GESTION_VAX3", "NINSC_NUM_GESTION_VAX4",
            "NINSC_NUM_GESTION_VAX5", "NINSC_TYPE_NON_INSCRIT", "NINSC_PM", "NINSC_ETAT_VAX", "NINSC_DATE_MAJ_INFOG", "NINSC_DATE_MAJ_INTERG",
            "NINSC_ARROND_NAISSANCE", "NINSC_ID_NINSC", "NINSC_FORME_JURIDIQUE_UTI", "NINSC_INSCRIT_REGISTRE_METIER", "NINSC_TYPE_RCS",
            "NINSC_NI_SECONDAIRE", "NINSC_CODE_GREFFE", "NINSC_ID_ADRBS"),
        RegleTable.TelleQuelle("PASSAGE_CFE", "REC-015",
            "PSSGC_ID_INFCF", "PSSGC_CFE", "PSSGC_NUM_LIAISON", "PSSGC_DATE_CFE", "PSSGC_DIRECT", "PSSGC_ETAT_CFE", "PSSGC_DATE_MAJ", "PSSGC_ID_PSSCF",
            "PSSGC_RM", "PSSGC_TYPE_LIASSE_CFE", "PSSGC_EMISSION_EDI", "PSSGC_NUM_UNIQUE_ID", "PSSGC_VIDI_GUID_LIASSE", "PSSGC_VIDI_GUID_PJ",
            "PSSGC_VIDI_DATE_ARCHIVAGE", "PSSGC_VIDI_EXT_AMINUM_ID_DOC", "PSSGC_CCI", "PSSGC_CA", "PSSGC_IS_DEMAT", "PSSGC_NUM_LIAISON_OLD",
            "PSSGC_VIDI_TAILLE_LIASSE", "PSSGC_VIDI_NB_PAGE_LIASSE", "PSSGC_VIDI_TAILLE_PJ", "PSSGC_VIDI_NB_PAGE_PJ", "PSSGC_VIDI_GUID_LIASSE1",
            "PSSGC_VIDI_TAILLE_LIASSE1", "PSSGC_VIDI_NB_PAGE_LIASSE1", "PSSGC_ENVOI_LIASSE1_ETAT", "PSSGC_ENVOI_LIASSE1_DATEREP"),
        RegleTable.TelleQuelle("RADIATION_TOTALE", "REC-014",
            "RDTTL_ID_INSCR", "RDTTL_DATE_RADIATION", "RDTTL_TEXTE_RADIATION", "RDTTL_NUM_REGISTRE", "RDTTL_ID_RDTTL"),
    ];
}

/// <summary>
/// REC-016 CA03 : UNE SEULE ADRESSE INCONNUE DANS LA V3. Les reprises de 2017 creaient chacune « * Adresse inconnu pour le
/// client* » si elles ne la trouvaient pas (ClientExtended.GetAdresseInconnu, Migration Infogreffe Informatique\2017\St Etienne) :
/// une base reprise plusieurs fois peut en avoir plusieurs. RIG_DEV n'en a aucune (lu le 2026-11-21 : ses deux « ADRESSE
/// INCONNUE » sont des saisies d'agent, que la regle ne touche pas -- a valider avec le referent). Apres les tables, la V3
/// garde la premiere (marquee EstInconnue par
/// le transfert), y rattache les fiches V3 qui visaient les autres (rec.Entreprise, rec.ClientAdresse, rec.Inscription), fait
/// pointer leur correspondance vers elle (origine « fusion ») et les retire. Les tables encore telles quelles (v2.DEMANDE...)
/// gardent l'identifiant V2 et la trouvent par rec.Correspondance. Rejouable : sans autre adresse inconnue, rien n'est fait.
/// </summary>
internal sealed class AdresseInconnueUnique : IRepriseTransfert
{
    public const string NomVoie = "* Adresse inconnu pour le client*";

    public string Nom => "adresse inconnue unique";
    public string Story => "REC-016";
    public int Rang => 80;
    public bool ApresLesTables => true;
    public IEnumerable<RegleTable> Regles => [];

    public async Task<BilanRepriseTransfert> ExecuterAsync(ContexteReprise contexte, CancellationToken ct)
    {
        await using var cn = new SqlConnection(contexte.ConnexionV3);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand($"""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DECLARE @garde int = (SELECT Id FROM rec.Adresse WHERE EstInconnue = 1);
            DECLARE @autres TABLE (Id int PRIMARY KEY);
            IF @garde IS NOT NULL
                INSERT INTO @autres SELECT Id FROM rec.Adresse WHERE NomVoie = @nom AND Id <> @garde;
            UPDATE rec.Entreprise SET IdAdresse = @garde WHERE IdAdresse IN (SELECT Id FROM @autres);
            DECLARE @e int = @@ROWCOUNT;
            UPDATE rec.ClientAdresse SET IdAdresse = @garde WHERE IdAdresse IN (SELECT Id FROM @autres);
            SET @e += @@ROWCOUNT;
            UPDATE rec.Inscription SET IdAdresse = @garde WHERE IdAdresse IN (SELECT Id FROM @autres);
            SET @e += @@ROWCOUNT;
            UPDATE rec.Correspondance SET IdV3 = CONVERT(nvarchar(200), @garde), Origine = 'fusion'
            WHERE BaseV2 = @b AND TableV2 = N'ADRESSE_BASE' AND TableV3 = N'rec.Adresse' AND IdV3 IN (SELECT CONVERT(nvarchar(200), Id) FROM @autres);
            DELETE FROM rec.Adresse WHERE Id IN (SELECT Id FROM @autres);
            SELECT CONCAT(ISNULL(CONVERT(varchar(20), @garde), '-'), '|', (SELECT COUNT(*) FROM @autres), '|', @e);
            COMMIT;
            """, cn) { CommandTimeout = 600 };
        cmd.Parameters.AddWithValue("@nom", NomVoie);
        cmd.Parameters.AddWithValue("@b", contexte.BaseV2);
        var r = ((string)(await cmd.ExecuteScalarAsync(ct))!).Split('|');
        var detail = r[0] == "-" ? "aucune adresse inconnue dans la base"
            : $"adresse inconnue gardee : {r[0]} ; {r[1]} autre(s) rattachee(s) a elle (correspondance « fusion »), {r[2]} fiche(s) V3 repointee(s)";
        return new BilanRepriseTransfert(false, detail, [], []);
    }
}
