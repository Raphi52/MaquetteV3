-- apres: REC-001, REC-002, REC-005, SOC-001
-- REC-012 a REC-017 et REC-019 : les FICHES PARTAGEES entre la V2 et la V3 d'un greffe ont leur table dans la V3 (carnet\
-- recopie.json), lot 10 du plan de reconstruction, phase 2 (2026-11-21). Jusqu'ici, leur copie « telle quelle » du schema v2
-- (v2.CLIENT, v2.DOSSIER_RCS...) en tenait lieu (REC-001, REC-002).
--
--   rec.Adresse          ADRESSE_BASE         REC-016  l'adresse partagee ; une seule « adresse inconnue » (EstInconnue)
--   rec.Entreprise       ENTREPRISE           REC-012  la fiche de personne du registre (denomination, SIREN, adresse)
--   rec.DossierRcs       DOSSIER_RCS          REC-012  le dossier au registre (numero de gestion, SIREN, procedure collective)
--   rec.Client           CLIENT               REC-013  le client ; son agent de rattachement est un compte de la V3 (soc.Compte)
--   rec.ClientAdresse    CLIENT_ADRESSE       REC-013  ses adresses
--   rec.ClientContact    CLIENT_CONTACT       REC-013  ses contacts
--   rec.ProvisionReprise ENCAISSEMENT_REPRIS  REC-013  les provisions reprises d'anciens logiciels, consommees une seule fois
--   rec.Inscription      INSCRIPTION          REC-014  l'inscription de surete, sa chaine de renouvellements
--   rec.Creance          CREANCE_END          REC-014  sa creance
--   rec.Bateau           BATEAU               REC-014  son bateau
--   rec.ActionRegistre   ACTION_RCS           REC-015  l'historique des actions d'un dossier du registre
--   rec.TypeDemande      CATEGORIE            REC-019  le type de demande, reconnu par sa REFERENCE (son numero V2 n'est que
--                                                      dans rec.Correspondance)
--   soc.Compte           UTILISATEUR          REC-017  (deja la : SOC-001) le compte d'un agent
--
-- La correspondance V2 <-> V3 de chaque colonne est dans Serveur\Recopie\FichesV3.cs (une ligne par colonne, le nom V2 en
-- commentaire ici aussi) : le transfert (REC-001) et la recopie continue (REC-002/003) la lisent. Les types sont CEUX DE LA
-- V2, a l'identique : une valeur ecrite d'un cote se relit egale de l'autre (comparaison de la ligne en JSON, empreinte du
-- transfert) ; la story du domaine qui voudra un autre type l'ecrira avec sa conversion.
-- Les identifiants techniques sont ceux de la V2 au transfert (IDENTITY garde, comme v2.*) : les tables encore « telles
-- quelles » (v2.DEMANDE, v2.AFFAIRE...) qui pointent vers une fiche continuent de la trouver. Sauf le type de demande : la V3
-- lui donne SON identifiant (REC-019 CA01).
-- AUCUNE cle etrangere entre ces tables, comme v2.* (recopie.md, « Les choix ») : un lien casse herite de la V2 (C5) doit
-- etre garde tel quel ; le transfert le releve (rec.LienCasse), la recopie le traduit par rec.Correspondance.
-- Les colonnes propres a la V2 (etat de transmission, imprimantes d'un poste, deroulement d'un type) n'ont pas de colonne ici.
-- Mode d'emploi : docs\reconstruction\recopie.md, section « Les fiches partagees ».

CREATE TABLE rec.Adresse (
    Id                          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Adresse PRIMARY KEY,  -- ADRBS_ID_ADRBS
    NumeroVoie                  varchar(15)  NULL,      -- ADRBS_NUM_VOIE
    ComplementNumero            varchar(7)   NULL,      -- ADRBS_COMPLEMENT_NO
    TypeVoie                    varchar(4)   NULL,      -- ADRBS_TYPE_VOIE
    NomVoie                     varchar(70)  NULL,      -- ADRBS_NOM_VOIE
    CodePostal                  varchar(10)  NULL,      -- ADRBS_CODE_POSTAL
    Localite                    varchar(70)  NULL,      -- ADRBS_LOCALITE_POSTALE
    Pays                        varchar(3)   NULL,      -- ADRBS_PAYS
    CodeRoutage                 varchar(5)   NULL,      -- ADRBS_CODE_ROUTAGE
    EtatInfo                    varchar(1)   NOT NULL,  -- ADRBS_ETAT_INFO
    FormateePourMediapost       bit          NULL,      -- ADRBS_FORMATE_POUR_MEDIAPOST
    PreAdresse                  varchar(255) NULL,      -- ADRBS_PRE_ADRESSE
    SuiteAdresse                varchar(255) NULL,      -- ADRBS_SUITE_ADRESSE
    BoitePostale                varchar(7)   NULL,      -- ADRBS_BOITE_POSTALE
    Cedex                       varchar(5)   NULL,      -- ADRBS_CEDEX
    LieuRattache                varchar(255) NULL,      -- ADRBS_LIEU_RATTACHE
    DivisionTerritoriale        varchar(9)   NULL,      -- ADRBS_DIVISION_TERRIT
    TypeSpecial                 varchar(1)   NULL,      -- ADRBS_TYPE_SPECIAL
    ValeurSpecial               varchar(32)  NULL,      -- ADRBS_VALEUR_SPECIAL
    CodeInsee                   varchar(6)   NULL,      -- ADRBS_CODE_INSEE
    -- REC-016 CA03 : l'adresse « * Adresse inconnu pour le client* » des reprises de 2017 (ClientExtended.GetAdresseInconnu),
    -- UNE SEULE dans la V3 : le transfert garde la premiere et y rattache les autres (FichesV3.cs, AdresseInconnueUnique).
    EstInconnue                 bit          NOT NULL CONSTRAINT DF_Adresse_EstInconnue DEFAULT (0)
);
CREATE UNIQUE INDEX UX_Adresse_Inconnue ON rec.Adresse (EstInconnue) WHERE EstInconnue = 1;

CREATE TABLE rec.Entreprise (
    Id                          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Entreprise PRIMARY KEY,  -- ENTRP_ID_RPRTR
    IdAdresse                   int          NULL,      -- ENTRP_ID_ADRBS -> rec.Adresse
    IdSousAdresse               int          NULL,      -- ENTRP_ID_SSADR_X (aucune table visee dans la V2)
    Siren                       varchar(10)  NULL,      -- ENTRP_SIREN
    Civilite                    varchar(7)   NOT NULL,  -- ENTRP_CIVILITE
    Denomination                varchar(512) NOT NULL,  -- ENTRP_DESIGNATION
    Patronyme                   varchar(70)  NULL,      -- ENTRP_PATRONYME
    Prenom                      varchar(70)  NULL,      -- ENTRP_PRENOM
    DateNaissance               datetime     NULL,      -- ENTRP_DATE_NAISSANCE
    DateNaissanceNonCommuniquee varchar(4)   NULL,      -- ENTRP_DATE_NAISSANCE_NC
    FormeJuridique              varchar(10)  NULL,      -- ENTRP_FORME_JURIDIQUE_UTI
    Sigle                       varchar(128) NULL,      -- ENTRP_SIGLE
    ComplementDenomination      varchar(max) NULL,      -- ENTRP_COMPLEMENT_DESIGN
    CodeNaf                     varchar(6)   NULL,      -- ENTRP_CODE_NAF
    EtatInfo                    varchar(1)   NOT NULL,  -- ENTRP_ETAT_INFO
    FicheSommaire               bit          NOT NULL,  -- ENTRP_FICHE_SOMMAIRE
    CodeApe                     varchar(4)   NULL,      -- ENTRP_CODE_APE
    NomUsage                    varchar(70)  NULL       -- ENTRP_NOM_USAGE
);
CREATE INDEX IX_Entreprise_Siren ON rec.Entreprise (Siren);

CREATE TABLE rec.DossierRcs (
    Id                          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_DossierRcs PRIMARY KEY,  -- DSSRC_ID_DSRCS
    IdEntreprise                int          NULL,      -- DSSRC_ID_RPRTR -> rec.Entreprise
    TypeRcs                     varchar(3)   NOT NULL,  -- DSSRC_TYPE_RCS
    NumeroGestion               varchar(10)  NOT NULL,  -- DSSRC_NUM_GESTION
    FinalGestion                varchar(1)   NULL,      -- DSSRC_FINAL_GESTION
    Siren                       varchar(9)   NULL,      -- DSSRC_SIREN
    DateImmatriculation         datetime     NOT NULL,  -- DSSRC_DATE_IMMAT
    MotifImmatriculation        varchar(1)   NOT NULL,  -- DSSRC_MOTIF_IMMAT
    DateEffet                   datetime     NULL,      -- DSSRC_DATE_EFFET
    NumeroAnterieur             varchar(11)  NULL,      -- DSSRC_NUM_ANTERIEUR
    ProvisoirementSans          bit          NOT NULL,  -- DSSRC_PROVISOIREMENT_SANS
    DateCessation               datetime     NULL,      -- DSSRC_DATE_CESSATION
    DateRadiation               datetime     NULL,      -- DSSRC_DATE_RADIATION
    CompteurMaj                 smallint     NOT NULL,  -- DSSRC_COMPTEUR_MAJ
    DateMaj                     datetime     NOT NULL,  -- DSSRC_DATE_MAJ
    EtatDossier                 varchar(1)   NOT NULL,  -- DSSRC_ETAT_DOSSIER
    EtatInfo                    varchar(1)   NOT NULL,  -- DSSRC_ETAT_INFO
    EtatVax                     varchar(4)   NULL,      -- DSSRC_ETAT_VAX
    DateMajInfogreffe           datetime     NULL,      -- DSSRC_DATE_MAJ_INFOG
    DateMajIntergreffe          datetime     NULL,      -- DSSRC_DATE_MAJ_INTERG
    DateMajCaducite             datetime     NULL,      -- DSSRC_DATE_MAJ_CADUCITE
    DateFormaliteCessation      datetime     NULL,      -- DSSRC_DATE_FORMALITE_CESS
    NumeroArchivage             varchar(10)  NULL,      -- DSSRC_NUM_ARCHIVAGE
    ProcedureCollective         varchar(3)   NULL,      -- DSSRC_PROCEDURE_PC
    InscritRegistreMetiers      bit          NOT NULL,  -- DSSRC_INSCRIT_REGISTRE_METIER
    EtatInfogreffeAnterieur     tinyint      NULL,      -- DSSRC_ETAT_INFOG_ANT
    NumeroInterne               int          NULL,      -- DSSRC_NOINT
    ModificationInterne         bit          NOT NULL,  -- DSSRC_TOP_MODIFINT
    DepartementRm               varchar(3)   NULL,      -- DSSRC_DPT_RM
    SirenProvisoire             bit          NOT NULL,  -- DSSRC_SIREN_PROVISOIRE
    Accre                       bit          NOT NULL,  -- DSSRC_ACCRE
    RenvoiExperian              varchar(10)  NULL,      -- DSSRC_BUG_RENVOI_EXPERIAN
    AutoEntrepreneur            bit          NOT NULL,  -- DSSRC_AUTO_ENTREPRENEUR
    DateDebut                   datetime     NULL,      -- DSSRC_DATE_DEBUT
    Eirl                        bit          NOT NULL,  -- DSSRC_EIRL
    DatePremiereImmatriculation datetime     NULL,      -- DSSRC_DATE_PREMIERE_IMMATRICULATION
    AutoEntrepreneurRegularise  bit          NOT NULL,  -- DSSRC_AUTO_ENTREPRENEUR_REGUL
    InpiEtat                    char(15)     NULL,      -- DSSRC_INPI_ETAT
    InpiIoEtat                  varchar(30)  NULL,      -- DSSRC_INPI_IO_ETAT
    InpiIoDateEnvoi             datetime     NULL,      -- DSSRC_INPI_IO_DATE_ENVOI
    Euid                        varchar(16)  NULL       -- DSSRC_EUID
    -- DSSRC_ETAT_TRANSMISSION et DSSRC_DATE_MODIF_FLAG_TRANS : propres a la V2 (ce qu'elle envoie a Infogreffe, REC-012 CA02).
);
CREATE INDEX IX_DossierRcs_NumeroGestion ON rec.DossierRcs (NumeroGestion);
CREATE INDEX IX_DossierRcs_Siren ON rec.DossierRcs (Siren);

CREATE TABLE rec.Client (
    Id                          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Client PRIMARY KEY,  -- CLIEN_ID_CLIEN
    Abrege                      varchar(10)  NULL,      -- CLIEN_ABREGE
    NumeroCompte                varchar(15)  NOT NULL,  -- CLIEN_NUMERO_COMPTE
    Nom                         varchar(255) NOT NULL,  -- CLIEN_NOM
    Prenom                      varchar(60)  NULL,      -- CLIEN_PRENOM
    Type                        varchar(6)   NOT NULL,  -- CLIEN_TYPE
    Civilite                    varchar(5)   NULL,      -- CLIEN_CIVILITE
    Statut                      varchar(1)   NULL,      -- CLIEN_STATUT
    SeuilCompte                 money        NULL,      -- CLIEN_SEUIL_COMPTE
    CompteCollectif             varchar(15)  NULL,      -- CLIEN_COMPTE_COLLECTIF
    Groupe                      varchar(5)   NULL,      -- CLIEN_GROUPE
    ModePaiement                varchar(8)   NULL,      -- CLIEN_MODE_PAIEMENT
    DateDerniereModification    datetime     NULL,      -- CLIEN_DATE_DERNIERE_MODIF
    DateReport                  datetime     NULL,      -- CLIEN_DATE_REPORT
    AssujettiTva                bit          NULL,      -- CLIEN_ASSUJETTI_TVA
    ReleveFacture               bit          NULL,      -- CLIEN_RELEVE_FACTURE
    BlocNotes                   varchar(2500) NULL,     -- CLIEN_BLOC_NOTES
    MontantReport               money        NULL,      -- CLIEN_MONTANT_REPORT
    -- REC-013 CA02 : l'agent de rattachement est un compte de la V3, retrouve d'une base a l'autre par son COMPTE DE
    -- SESSION (soc.Compte.CompteWindows = UTILISATEUR.UTLST_LOGIN), jamais par son identifiant numerique.
    IdCompteRattachement        int          NULL,      -- CLIEN_ID_UTLST (traduit par le compte de session)
    Email                       varchar(255) NULL,      -- CLIEN_EMAIL
    Telephone                   varchar(20)  NULL,      -- CLIEN_TELEPHONE
    TelephonePortable           varchar(20)  NULL,      -- CLIEN_TELEPHONE_PORTABLE
    Fax                         varchar(20)  NULL,      -- CLIEN_FAX
    EstComptePc                 bit          NOT NULL,  -- CLIEN_IS_COMPTE_PC
    EstCompteWeb                bit          NOT NULL,  -- CLIEN_IS_COMPTE_WEB
    -- CLIEN_MDP (mot de passe du compte web) : AUCUNE colonne -- la V3 ne garde aucun mot de passe (SOC-001 CA08,
    -- SocleIdentiteCheck). Il reste dans la V2, jamais recopie ; un client cree par la V3 n'en a pas.
    -- fix-ok: colonne MotDePasseWeb retiree -- mesure 2026-11-21 par SocleIdentiteCheck (SOC-001 CA08 rouge, 3 colonnes de mot de passe sur 2).
    CodeGreffe                 varchar(4)   NULL,      -- CLIEN_CODE_GREFFE
    EtatEdi                     varchar(2)   NULL,      -- CLIEN_ETAT_EDI
    NumeroEdi                   int          NULL,      -- CLIEN_NUM_EDI
    DateEdi                     datetime     NULL,      -- CLIEN_DATE_EDI
    Siren                       varchar(9)   NULL,      -- CLIEN_SIREN
    DateNaissance               datetime     NULL,      -- CLIEN_DATE_NAISSANCE
    TvaIntracommunautaire       nvarchar(20) NULL,      -- CLIEN_TVA_INTRA
    Siret                       nvarchar(14) NULL,      -- CLIEN_SIRET
    -- REC-013 T01 : le code abrege est UNIQUE dans la V3. La V2 ne l'impose pas (IDX_CLIEN_ABREGE, non unique) : RIG_DEV a 7
    -- codes portes deux fois (lu le 2026-11-21). Le transfert les garde tous et marque les suivants du premier « doublon
    -- herite » ; l'unicite vaut pour tout ce que la V3 cree ou recoit ensuite.
    DoublonHerite               bit          NOT NULL CONSTRAINT DF_Client_DoublonHerite DEFAULT (0)
);
CREATE UNIQUE INDEX UX_Client_Abrege ON rec.Client (Abrege) WHERE Abrege IS NOT NULL AND DoublonHerite = 0;

CREATE TABLE rec.ClientAdresse (
    Id                          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ClientAdresse PRIMARY KEY,  -- CLIAD_ID_CLIAD
    IdClient                    int          NULL,      -- CLIAD_ID_CLIEN -> rec.Client
    LigneAdresse                varchar(255) NOT NULL,  -- CLIAD_LIGNE_ADRESSE
    SuiteAdresse                varchar(255) NULL,      -- CLIAD_SUITE_ADRESSE
    CodePostal                  varchar(32)  NULL,      -- CLIAD_CODE_POSTAL
    Localite                    varchar(255) NULL,      -- CLIAD_LOCALITE_POSTALE
    ParDefaut                   bit          NOT NULL,  -- CLIAD_ADRESSE_PAR_DEFAUT
    TypeAdresse                 varchar(1)   NULL,      -- CLIAD_TYPE_ADRESSE
    Pays                        varchar(32)  NULL,      -- CLIAD_PAYS
    IdAdresse                   int          NULL       -- CLIAD_ID_ADRBS -> rec.Adresse (sans cle sur RIG_DEV, avec cle sur RIG_RECETTE)
);
CREATE INDEX IX_ClientAdresse_Client ON rec.ClientAdresse (IdClient);

CREATE TABLE rec.ClientContact (
    Id                          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ClientContact PRIMARY KEY,  -- CLICO_ID_CLICO
    IdClient                    int          NOT NULL,  -- CLICO_ID_CLIEN -> rec.Client
    Nom                         varchar(100) NULL,      -- CLICO_NOM
    Prenom                      varchar(100) NULL,      -- CLICO_PRENOM
    Telephone                   varchar(20)  NULL,      -- CLICO_TELEPHONE
    Fonction                    varchar(50)  NULL,      -- CLICO_FONCTION
    Service                     varchar(50)  NULL,      -- CLICO_SERVICE
    Fax                         varchar(20)  NULL,      -- CLICO_FAX
    Email                       varchar(100) NULL,      -- CLICO_EMAIL
    Portable                    varchar(20)  NULL       -- CLICO_PORTABLE
);
CREATE INDEX IX_ClientContact_Client ON rec.ClientContact (IdClient);

CREATE TABLE rec.ProvisionReprise (
    NumeroTitre                 varchar(14)  NOT NULL CONSTRAINT PK_ProvisionReprise PRIMARY KEY,  -- ENRPS_NUMERO_TITRE
    Libelle                     varchar(300) NULL,      -- ENRPS_LIBELLE
    Montant                     varchar(50)  NULL,      -- ENRPS_MONTANT
    NomDossier                  varchar(50)  NOT NULL,  -- ENRPS_NOM_DOSSIER
    Banque                      varchar(50)  NULL,      -- ENRPS_BANQUE
    Devise                      varchar(4)   NULL,      -- ENRPS_DEVISE
    Date                        datetime     NULL,      -- ENRPS_DATE
    -- 0 disponible, 1 consommee (Encaissement.Sauve de la V2, mode PV). REC-013 CA06 : la V3 ne la consomme qu'apres
    -- l'avoir reservee dans la V2 (Serveur\Recopie\ProvisionsReprises.cs) -- le premier qui reserve gagne.
    Consommee                   smallint     NOT NULL   -- ENRPS_CONSOMMER
);

CREATE TABLE rec.Creance (
    Id                          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Creance PRIMARY KEY,  -- CNEND_ID_CNEND
    LiquideEvaluee              bit          NOT NULL,  -- CNEND_LIQUIDE_EVALUEE
    Monnaie                     varchar(3)   NULL,      -- CNEND_MONNAIE
    MontantPrincipal            money        NULL,      -- CNEND_MONTANT_PRINCIPAL
    TauxInteret                 varchar(64)  NULL,      -- CNEND_TAUX_INTERET
    MontantAccessoires          money        NULL,      -- CNEND_MONTANT_ACCESSOIRES
    MontantTotal                money        NULL,      -- CNEND_MONTANT_TOTAL
    DateExpiration              datetime     NULL,      -- CNEND_DATE_EXPIRATION
    Duree                       varchar(35)  NULL,      -- CNEND_DUREE
    DateEcheance                datetime     NULL,      -- CNEND_DATE_ECHEANCE
    SaufMemoire                 bit          NOT NULL,  -- CNEND_SAUF_MEMOIRE
    CreancesFutures             varchar(256) NULL,      -- CNEND_CREANCES_FUTURES
    Article244                  bit          NULL,      -- CNEND_ARTICLE_244
    DispositionsParticulieres   bit          NULL,      -- CNEND_DISPOSITIONS_PARTICULIERES
    AlienableChosesFongibles    bit          NOT NULL,  -- CNEND_ALIENABLE_CHOSES_FONGIBLES
    AlienablePartsSociales      bit          NOT NULL,  -- CNEND_ALIENABLE_PARTS_SOCIALES
    ActionResolutoire           bit          NOT NULL,  -- CNEND_ACTION_RESOLUTOIRE
    PacteCommissoire            bit          NOT NULL,  -- CNEND_PACTE_COMMISSOIRE
    MarqueBrevetDessinModele    bit          NOT NULL   -- CNEND_MARQUE_BREVET_DESSIN_MODELE
);

CREATE TABLE rec.Bateau (
    Id                          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Bateau PRIMARY KEY,  -- BTEAU_ID_BTEAU
    LieuImmatriculation         varchar(35)  NULL,      -- BTEAU_LIEU_IMMAT
    Abrege                      varchar(12)  NOT NULL,  -- BTEAU_ABREGE
    DateImmatriculation         datetime     NULL,      -- BTEAU_DATE_IMMAT
    Nom                         varchar(70)  NOT NULL,  -- BTEAU_NOM
    Proprietaire                varchar(max) NULL,      -- BTEAU_PROPRIETAIRE
    Historique                  varchar(max) NULL,      -- BTEAU_HISTORIQUE
    NumeroEnregistrement        varchar(6)   NULL,      -- BTEAU_NUM_ENREGISTREMENT
    NumeroMatriculeDouane       varchar(5)   NULL,      -- BTEAU_NUM_MATRICULE_DOUANE
    NumeroOmi                   varchar(7)   NULL,      -- BTEAU_NUM_OMI
    NumeroCinHinWin             varchar(15)  NULL,      -- BTEAU_NUM_CIN_HIN_WIN
    Port                        varchar(2)   NULL,      -- BTEAU_PORT
    Registre                    varchar(50)  NULL,      -- BTEAU_REGISTRE
    DomaineCommerce             varchar(50)  NULL,      -- BTEAU_DOMAINE_COMMERCE
    Longueur                    float        NULL,      -- BTEAU_LONGUEUR
    Jauge                       float        NULL,      -- BTEAU_JAUGE
    Nationalite                 varchar(15)  NULL,      -- BTEAU_NATIONALITE
    NomGardien                  varchar(70)  NULL,      -- BTEAU_NOM_GARDIEN
    EstDerniere                 bit          NULL,      -- BTEAU_IS_LAST
    EstNavire                   bit          NULL,      -- BTEAU_IS_NAVIRE
    -- L'agent qui a interroge le registre des navires : un compte de la V3, par son compte de session (comme le client).
    ApiIdCompte                 int          NULL,      -- BTEAU_API_ID_UTLST (traduit par le compte de session)
    ApiDateAppel                datetime     NULL,      -- BTEAU_API_DATE_APPEL
    ApiUrl                      varchar(256) NULL,      -- BTEAU_API_URL
    ApiJson                     varchar(max) NULL,      -- BTEAU_API_JSON
    ApiErreurs                  varchar(1000) NULL,     -- BTEAU_API_DATA_ERREURS
    ApiDonneesModifiees         bit          NULL       -- BTEAU_API_IS_DATA_MODIF
);

CREATE TABLE rec.Inscription (
    Id                          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Inscription PRIMARY KEY,  -- INEDT_ID_INSCR
    IdProcedureCollective       int          NULL,      -- INEDT_ID_PRCLL (aucune cle dans la V2)
    -- REC-014 CA01 : la chaine des renouvellements, par la correspondance quand l'inscription d'origine est restee dans la V2.
    IdInscriptionPrecedente     int          NULL,      -- INEDT_ID_INSCR_INSCRIPTION_PRE -> rec.Inscription
    IdSousAdresse               int          NULL,      -- INEDT_ID_SSADR_X (aucune table visee dans la V2)
    IdAdresse                   int          NULL,      -- INEDT_ID_ADRBS -> rec.Adresse
    IdCreance                   int          NULL,      -- INEDT_ID_CNEND -> rec.Creance
    IdBateau                    int          NULL,      -- INEDT_ID_BTEAU -> rec.Bateau
    NumeroInscription           varchar(11)  NOT NULL,  -- INEDT_NUM_INSCRIPTION
    TypeInscription             varchar(5)   NOT NULL,  -- INEDT_TYPE_INSCRIPTION
    ParTransfert                bit          NOT NULL,  -- INEDT_PAR_TRANSFERT
    DateInscription             datetime     NOT NULL,  -- INEDT_DATE_INSCRIPTION
    DatePeremption              datetime     NULL,      -- INEDT_DATE_PEREMPTION
    DateEffet                   datetime     NULL,      -- INEDT_DATE_EFFET
    Renouvelable                bit          NOT NULL,  -- INEDT_RENOUVELABLE
    EtatInfo                    varchar(1)   NOT NULL,  -- INEDT_ETAT_INFO
    DateDiffusion               datetime     NOT NULL,  -- INEDT_DATE_DIFFUSION
    EtatVax                     varchar(4)   NULL,      -- INEDT_ETAT_VAX
    DateMaj                     datetime     NULL,      -- INEDT_DATE_MAJ
    DateMajInfogreffe           datetime     NULL,      -- INEDT_DATE_MAJ_INFOG
    DateMajIntergreffe          datetime     NULL,      -- INEDT_DATE_MAJ_INTERG
    NumeroRegistre              varchar(10)  NULL,      -- INEDT_NUM_REGISTRE
    EtatTraitement              varchar(20)  NULL,      -- INEDT_ETAT_TRAITEMENT
    DateTraitement              datetime     NULL,      -- INEDT_DATE_TRAITEMENT
    Uuid                        varchar(512) NULL       -- INEDT_UUID
    -- INEDT_ETAT_TRANSMISSION et INEDT_DATE_MODIF_FLAG_TRANS : propres a la V2 (REC-014 CA03).
);
CREATE INDEX IX_Inscription_Numero ON rec.Inscription (NumeroInscription);

CREATE TABLE rec.ActionRegistre (
    Id                          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ActionRegistre PRIMARY KEY,  -- ACTRC_ID_ACTRC
    -- Le dossier et les six objets qu'une action peut viser (REC-015 T01) : depot, annonce, inscription au registre,
    -- instance, passage au centre de formalites, non-inscrit. Leurs colonnes metier restent celles de leur story (RCS,
    -- Judiciaire) : leur table V3 est aujourd'hui v2.DEPOT_RCS, v2.BODACC... (FichesV3.cs, ReglesObjetsDuRegistre).
    IdDossier                   int          NULL,      -- ACTRC_ID_DSRCS -> rec.DossierRcs
    IdDepot                     int          NULL,      -- ACTRC_ID_DPTRC -> v2.DEPOT_RCS
    IdAnnonceBodacc             int          NULL,      -- ACTRC_ID_BDCRC -> v2.BODACC
    IdInscriptionRegistre       int          NULL,      -- ACTRC_ID_INSRC -> v2.INSCRIPTION_RCS
    IdPassageCfe                int          NULL,      -- ACTRC_ID_PSSCF -> v2.PASSAGE_CFE
    IdNonInscrit                int          NULL,      -- ACTRC_ID_NINSC -> v2.NON_INSCRIT
    IdInstance                  int          NULL,      -- ACTRC_ID_INSTN -> v2.INSTANCE
    IdDepotHistorique           int          NULL,      -- ACTRC_ID_DPTRCH (aucune cle dans la V2)
    NumeroArrivee               varchar(10)  NULL,      -- ACTRC_NUM_ARRIVEE
    DateArrivee                 datetime     NULL,      -- ACTRC_DATE_ARRIVEE
    CodeProcessus               varchar(40)  NOT NULL,  -- ACTRC_CODE_PROSS
    EtatInfo                    varchar(1)   NOT NULL,  -- ACTRC_ETAT_INFO
    EtatAction                  varchar(1)   NOT NULL,  -- ACTRC_ETAT_ACTION
    SurNotification             bit          NOT NULL   -- ACTRC_SUR_NOTIFICATION
);
CREATE INDEX IX_ActionRegistre_Dossier ON rec.ActionRegistre (IdDossier);

CREATE TABLE rec.TypeDemande (
    -- REC-019 CA01 : l'identifiant est celui de la V3 ; le numero du type dans une base V2 n'est que dans rec.Correspondance.
    Id                          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_TypeDemande PRIMARY KEY,
    Reference                   varchar(10)  NOT NULL,  -- CTGR_REFERENCE
    Niveau                      tinyint      NOT NULL,  -- CTGR_NIVEAU
    -- Les parents dans l'arbre des types (REC-019 CA05), en identifiants de la V3.
    IdDomaine                   int          NULL,      -- CTGR_ID_CTGR_DOMAINE -> rec.TypeDemande
    IdFamille                   int          NULL,      -- CTGR_ID_CTGR_FAMILLE -> rec.TypeDemande
    IdActivite                  int          NULL,      -- CTGR_ID_CTGR_ACTIVITE -> rec.TypeDemande
    IdService                   int          NULL,      -- CTGR_ID_SRVC -> soc.Service (par soc.Service.IdV2)
    CodeDateTravail             varchar(10)  NULL,      -- CTGR_CODE_DATE_TRAVAIL
    Abrege                      varchar(10)  NULL,      -- CTGR_ABREGE
    Nom                         varchar(35)  NULL,      -- CTGR_NOM
    Libelle                     varchar(70)  NULL,      -- CTGR_LIBELLE
    LibelleEcran                varchar(35)  NOT NULL,  -- CTGR_LIBELLE_ECRAN
    LibelleEdition              varchar(70)  NULL,      -- CTGR_LIBELLE_EDITION
    CreationDossier             bit          NOT NULL,  -- CTGR_CREATION_DOSSIER
    CaseCourrier                varchar(10)  NULL,      -- CTGR_CASE_COURRIER
    AffichageTous               bit          NOT NULL,  -- CTGR_AFFICHAGE_TOUS
    DureePrevue                 datetime     NULL,      -- CTGR_DUREE_PREVUE
    DistributionAuto            bit          NOT NULL,  -- CTGR_DISTRIBUTION_AUTO
    AffichageDistribution       bit          NOT NULL,  -- CTGR_AFFICHAGE_DISTRI
    PrioriteGroupe              bit          NOT NULL,  -- CTGR_PRIORITE_GROUPE
    IndexAide                   varchar(16)  NULL,      -- CTGR_INDEX_AIDE
    FamilleDossier              varchar(60)  NULL,      -- CTGR_FAMILLE_DOSSIER
    CodeDateTravailClt          varchar(10)  NULL,      -- CTGR_CODE_DATE_TRAVAIL_CLT
    LibelleEcranClt             varchar(35)  NULL,      -- CTGR_LIBELLE_ECRAN_CLT
    LibelleEditionClt           varchar(70)  NULL,      -- CTGR_LIBELLE_EDITION_CLT
    FamilleDossierClt           varchar(60)  NULL,      -- CTGR_FAMILLE_DOSSIER_CLT
    IndexAideClt                varchar(16)  NULL,      -- CTGR_INDEX_AIDE_CLT
    CaseCourrierClt             varchar(10)  NULL,      -- CTGR_CASE_COURRIER_CLT
    SaisieArrivee               bit          NULL,      -- CTGR_SAISIE_ARRIVEE
    CreationEntreprise          bit          NULL,      -- CTGR_CREATION_ENTREPRISE
    RegistreArrivee             bit          NOT NULL   -- CTGR_REGISTRE_ARRIVEE
    -- CTGR_ID_MDPRC (le deroulement du type dans la V2) : propre a la V2, jamais change par la V3 (REC-019 CA04).
);
CREATE INDEX IX_TypeDemande_Reference ON rec.TypeDemande (Reference, Niveau);
GO

-- REC-019 CA05 : les types encore indiscernables (meme reference, meme niveau, memes parents), listes pour le referent du
-- parametrage -- jamais fusionnes ni choisis au hasard. Mesure sur RIG_DEV (2026-11-21) : aucun ; les trois AMITEL sont aux
-- niveaux 1, 2 et 3, les deux PREVENTION et les deux EDIT_PRV different par leur niveau.
CREATE VIEW rec.TypesDemandeIndiscernables
AS
SELECT t.Id, t.Reference, t.Niveau, t.IdDomaine, t.IdFamille, t.IdActivite, t.LibelleEcran,
       COUNT_BIG(*) OVER (PARTITION BY t.Reference, t.Niveau, t.IdDomaine, t.IdFamille, t.IdActivite) AS Entrees
FROM rec.TypeDemande AS t
WHERE EXISTS (SELECT 1 FROM rec.TypeDemande AS a WHERE a.Id <> t.Id AND a.Reference = t.Reference AND a.Niveau = t.Niveau
              AND EXISTS (SELECT a.IdDomaine, a.IdFamille, a.IdActivite INTERSECT SELECT t.IdDomaine, t.IdFamille, t.IdActivite));
GO

-- Les tables recopiees de chaque base V2, figees a la MISE EN SERVICE de la recopie (PontRecopie.MettreEnServiceAsync) :
-- le serveur recopie une base avec les regles de sa mise en service, pas avec celles que son code declare au jour le jour.
-- Sans elle, une regle ajoutee au serveur (une nouvelle fiche partagee) entrait dans la recopie d'un greffe deja en service
-- alors que sa table V2 n'y est pas suivie (Base\v2\recopie-v2.sql) ni sa table V3 munie de son declencheur de file : la
-- remontee s'arretait sur « le suivi des changements n'est plus active ». Une nouvelle fiche entre par une nouvelle mise en
-- service (le script V2 d'abord), qui la refuse en nommant ce qui manque.
CREATE TABLE rec.FicheEnService (
    BaseV2      sysname       NOT NULL,
    TableV2     sysname       NOT NULL,
    TableV3     nvarchar(260) NOT NULL,
    PoseeLe     datetime2(3)  NOT NULL CONSTRAINT DF_FicheEnService_PoseeLe DEFAULT (SYSDATETIME()),
    CONSTRAINT PK_FicheEnService PRIMARY KEY (BaseV2, TableV2)
);

-- Une correspondance « fusion » : une ligne de la V2 rattachee a une fiche V3 qui en represente plusieurs (les adresses
-- inconnues de la V2, REC-016 CA03). Ce n'est jamais elle que la recopie V3 -> V2 suit (EcrituresRecopie.IdV2Async).
ALTER TABLE rec.Correspondance DROP CONSTRAINT CK_Correspondance_Origine;
ALTER TABLE rec.Correspondance ADD CONSTRAINT CK_Correspondance_Origine CHECK (Origine IN ('recopie', 'remontee', 'fusion'));

-- Un lien « sans equivalent dans la V3 » : une valeur traduite au transfert (un agent par son compte de session, REC-013
-- CA02) que la V3 n'a pas ; elle est videe dans la fiche V3 et gardee ici, jamais inventee (RegleTable.Cible).
ALTER TABLE rec.LienCasse DROP CONSTRAINT CK_LienCasse_Cause;
ALTER TABLE rec.LienCasse ADD CONSTRAINT CK_LienCasse_Cause CHECK (Cause IN ('absente de la V2', 'rejetee par le transfert', 'sans equivalent dans la V3'));
