using System.Globalization;
using System.IO;
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

// fix-ok: mesuré au banc du 28/11/2026 14:58 -- « Invalid column name 'INTRP_NUM_ORDRE' / 'INTRP_CIVILITE' / 'INTRP_FONCTION_PC' » (erreur 207
// du Jeu) : la v2.INTERVENANT_PC du banc vient de PROC_SUPP_ACTEUR_JUD.sql:34, sans ces colonnes ; le Jeu les ajoute.

/// <summary>
/// « Clôture » (PROC_CLOT, Pont/EcranCloture.cs + EcranClotureBase.cs). Lot 11 du plan « RIG v3 complet, mesuré par le banc du parcours
/// complet » (28/11/2026), refaite après le refus du chef du 28/11 (« le courrier aux débiteurs est secondaire »).
///
/// LE GESTE PRINCIPAL, l'« Enrôlement sur saisine d'office » (mode 1, celui que RIG v2 présélectionne depuis l'alerte : OPE_EXAMEN_CLOT.cs:
/// 57-59 ; OPE_SELECT_PC.cs:1776-1899 ; écriture : Serveur/Socle/EcransMain/EcritureCloture.EnrolerAsync), joué par le pont sur la PC
/// P4100102 de la grille :
///   1. la JUMELLE refusée : le jour de l'audience à 23:59, sans audience -- RIG v2 ouvre alors la création d'audience, la V3 refuse ;
///      rien au journal ;
///   2. l'enrôlement à l'audience du jour + 14 à 09:30, appel à 09:45, classement par défaut (CLOT_SO_TYPECLA = PC) ;
///   3. RELU PAR LE PONT (EcranCloture.EnrolementsAsync, l'onglet « Enrolement / requete ») : l'instance FCLIA7 de la PC, son n° de rôle
///      JRPC et son répertoire JGAFF, le demandeur du réglage (« Saisine d'office ») et la PC défenderesse, l'acte QLI, son APPEL à
///      l'audience (date, heure, classement PC, heure d'appel), la CONVOCATION ED_CLOTLJ survenue du jour, le 4CLO de la PC SURVENU du jour ;
///      et la PC a quitté la grille « Sélection PC » (une instance de clôture l'en exclut, OPE_SELECT_PC.cs:522-523). Contrôle SQL : la
///      demande EF validée et ses liens 2J, 2P, 2V (RIG_DEV, demande 7028286), ses deux éditions de convocation en file.
/// Ce qui ferait échouer la sonde si le geste était faux : le 4CLO de la PC laissé futur (ExamenSurvenu vide), l'appel absent ou à une
/// autre audience, la jumelle écrite.
///
/// LE SECOND, le « Courrier aux débiteurs » (mode 5, OPE_SELECT_PC.cs:1761-1775), sur la PC P4100101 : relu par la colonne « Dernier
/// courrier envoyé » et son courrier dans la file des éditions.
///
/// LE JEU (identifiants 41001xx, lot 11), comme RIG_DEV (lu le 28/11/2026) : les réglages CLOT_* et DEMANDEUR_SO_* (mode « Huissier »,
/// convocation ED_CLOTLJ), le code affaire FCLIA7 (JRPC, type 08, répertoire, nature 4HE, acte QLI), les éditions de ED_CLOTLJ pour le
/// document interne (01CLOTLJ, CONV_HUIS_CLOT : CEP_EDITION 6 et 870) ; un liquidateur et un huissier sur chaque PC ; les tables et
/// colonnes que la lecture et les gestes touchent, aux types de RIG_DEV.
/// </summary>
public sealed class SondeCloture : ISondeEcran
{
    private const int Pc = 4100101;
    private const string NumPc = "P4100101";
    private const int PcSo = 4100102;
    private const string NumPcSo = "P4100102";
    private const int PcCapture = 4100103;
    private const string NumPcCapture = "P4100103";

    /// <summary>
    /// PREUVE EN IMAGE (lot 11, 28/11/2026) : avec RIGV3_SONDE_CLOTURE_PAUSE = un chemin de fichier, la sonde, son Jeu joué et avant son
    /// geste, y écrit le greffe et la base du banc puis ATTEND que le fichier disparaisse (45 min au plus) -- comme le mode RIGV3_BANC_GARDER
    /// d'EcransGeneresBaseCheck. Un serveur lancé à part sur la base commune du banc (le fichier rigv3-parcours-*.txt du banc la nomme) sert
    /// alors à l'application la PC P4100103, à enrôler à l'écran ; la sonde reprend ensuite sur P4100102. Sans la variable : rien ne change.
    /// </summary>
    public const string VariablePause = "RIGV3_SONDE_CLOTURE_PAUSE";

    private static async Task PauseCaptureAsync(ContexteSonde c)
    {
        if (Environment.GetEnvironmentVariable(VariablePause) is not { Length: > 0 } fichier) return;
        await File.WriteAllTextAsync(fichier, $"greffe={c.Greffe}\nbase={await c.Valeur("SELECT DB_NAME()")}\npc={NumPcCapture}\n");
        Console.WriteLine($"Sonde Clôture en pause pour une preuve à l'écran (PC {NumPcCapture}) : supprimer {fichier} pour reprendre.");
        var fin = DateTime.Now.AddMinutes(45);
        while (File.Exists(fichier) && DateTime.Now < fin) await Task.Delay(1000);
    }

    public string Code => "PROC_CLOT";

    public string? Jeu => JeuSonde.EnDeuxTemps(JeuSonde.Demandes + """
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_NUM_PC') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_NUM_PC varchar(10) NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_NOM_AFFAIRE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_NOM_AFFAIRE varchar(250) NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_FORFAIT_FACTURE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_FORFAIT_FACTURE bit NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_EXTERIEUR') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_EXTERIEUR bit NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_CODE_CLOTURE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_CODE_CLOTURE varchar(5) NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_ID_DSRCS') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_ID_DSRCS int NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_ID_ADRBS') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_ID_ADRBS int NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_ID_NINSC') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_ID_NINSC int NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_ID_RPRTR') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_ID_RPRTR int NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_NOM_DEBITEUR_PRINCIPAL') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_NOM_DEBITEUR_PRINCIPAL varchar(250) NULL;
        IF OBJECT_ID(N'v2.EVENEMENT_PROCEDURE') IS NULL
            CREATE TABLE v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR int IDENTITY(4100100, 1) NOT NULL PRIMARY KEY, EVNPR_CODE_EVE_PRO varchar(10) NOT NULL);
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_ID_PRCLL') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_ID_PRCLL int NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_ID_INSTN') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_ID_INSTN int NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_ID_PREVE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_ID_PREVE int NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE datetime NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_SURVENU') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_SURVENU bit NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DELAI_EXPIRE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DELAI_EXPIRE bit NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE_MAXI') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE_MAXI datetime NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE_MINI') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE_MINI datetime NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE_FIXE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE_FIXE datetime NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_NUMERO_SOURCE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_NUMERO_SOURCE varchar(36) NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_NUMERO_SOURCE_EP_LIE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_NUMERO_SOURCE_EP_LIE varchar(36) NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_TYPE_EVE_PRO') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_TYPE_EVE_PRO varchar(1) NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_NUM_ORDRE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_NUM_ORDRE smallint NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_AVEC_PIECE_NUMERISE') IS NULL
            ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_AVEC_PIECE_NUMERISE bit NOT NULL CONSTRAINT DF_P11_EVNPR_PIECE DEFAULT (0);
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_NATURE_DOC_GED') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_NATURE_DOC_GED varchar(50) NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_TYPE_DEPOSANT_GED') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_TYPE_DEPOSANT_GED varchar(50) NULL;
        IF OBJECT_ID(N'v2.INSTANCE') IS NULL
            CREATE TABLE v2.INSTANCE (INSTN_ID_INSTN int NOT NULL PRIMARY KEY, INSTN_NUM_ROLE varchar(11) NULL);
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_ID_PRCLL') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_ID_PRCLL int NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_CODE_AFFAIRE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_CODE_AFFAIRE varchar(10) NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_ID_AFFRE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_ID_AFFRE int NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_NOM_COURANT') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_NOM_COURANT varchar(250) NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_DATE_ENROLEMENT') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_DATE_ENROLEMENT datetime NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_NUM_REP_GENERAL') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_NUM_REP_GENERAL varchar(10) NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_NATURE_AFFAIRE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_NATURE_AFFAIRE varchar(10) NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_CODE_ACTE_PROCE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_CODE_ACTE_PROCE varchar(3) NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_DATE_ACTE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_DATE_ACTE datetime NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_ETAT_INSTANCE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_ETAT_INSTANCE varchar(1) NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_ETAT_INFO') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_ETAT_INFO varchar(1) NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_NUMERO_SOURCE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_NUMERO_SOURCE varchar(36) NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_TYPE_INSTANCE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_TYPE_INSTANCE varchar(2) NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_NAT_AFF_PART') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_NAT_AFF_PART varchar(5) NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_DATE_SAISINE_GREFFE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_DATE_SAISINE_GREFFE datetime NULL;
        IF OBJECT_ID(N'v2.PARTIE') IS NULL
            CREATE TABLE v2.PARTIE (PRT_ID_PRT int NOT NULL PRIMARY KEY, PRT_ID_INSTN int NOT NULL, PRT_NUM_ORDRE smallint NOT NULL,
                PRT_CIVILITE varchar(7) NOT NULL, PRT_QUALIF_PARTIE varchar(5) NOT NULL);
        IF COL_LENGTH('v2.PARTIE', 'PRT_DESIGNATION') IS NULL ALTER TABLE v2.PARTIE ADD PRT_DESIGNATION varchar(512) NULL;
        IF COL_LENGTH('v2.PARTIE', 'PRT_NOM') IS NULL ALTER TABLE v2.PARTIE ADD PRT_NOM varchar(512) NULL;
        IF COL_LENGTH('v2.PARTIE', 'PRT_SIREN') IS NULL ALTER TABLE v2.PARTIE ADD PRT_SIREN varchar(10) NULL;
        IF COL_LENGTH('v2.PARTIE', 'PRT_EDITION_JUGEMENT') IS NULL ALTER TABLE v2.PARTIE ADD PRT_EDITION_JUGEMENT varchar(5) NULL;
        IF COL_LENGTH('v2.PARTIE', 'PRT_MANDATAIRE_EDITION_JUGEMENT') IS NULL ALTER TABLE v2.PARTIE ADD PRT_MANDATAIRE_EDITION_JUGEMENT varchar(5) NULL;
        IF COL_LENGTH('v2.PARTIE', 'PRT_MANDATAIRE_PLAI_EDITION_JUGEMENT') IS NULL ALTER TABLE v2.PARTIE ADD PRT_MANDATAIRE_PLAI_EDITION_JUGEMENT varchar(5) NULL;
        IF COL_LENGTH('v2.PARTIE', 'PRT_ID_ADRBS') IS NULL ALTER TABLE v2.PARTIE ADD PRT_ID_ADRBS int NULL;
        IF COL_LENGTH('v2.PARTIE', 'PRT_ID_DSRCS') IS NULL ALTER TABLE v2.PARTIE ADD PRT_ID_DSRCS int NULL;
        IF COL_LENGTH('v2.PARTIE', 'PRT_ID_NINSC') IS NULL ALTER TABLE v2.PARTIE ADD PRT_ID_NINSC int NULL;
        IF COL_LENGTH('v2.PARTIE', 'PRT_ID_RPRTR') IS NULL ALTER TABLE v2.PARTIE ADD PRT_ID_RPRTR int NULL;
        IF OBJECT_ID(N'v2.CHAMBRE') IS NULL
            CREATE TABLE v2.CHAMBRE (R_CHAMBRE_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_CHAMBRE_LIBELLE_ECRAN varchar(35) NOT NULL);
        IF OBJECT_ID(N'v2.SECTION') IS NULL
            CREATE TABLE v2.SECTION (R_SECTION_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_SECTION_LIBELLE_ECRAN varchar(35) NOT NULL);
        IF OBJECT_ID(N'v2.JUGE') IS NULL
            CREATE TABLE v2.JUGE (JUG_ID_JUG int NOT NULL PRIMARY KEY, JUG_CIVILITE varchar(7) NOT NULL, JUG_NOM varchar(35) NOT NULL);
        IF COL_LENGTH('v2.JUGE', 'JUG_PRENOM') IS NULL ALTER TABLE v2.JUGE ADD JUG_PRENOM varchar(20) NULL;
        IF OBJECT_ID(N'v2.AUDIENCE_CABINET') IS NULL
            CREATE TABLE v2.AUDIENCE_CABINET (AUDNC_ID_ADNC int NOT NULL PRIMARY KEY, AUDNC_DATE datetime NULL, AUDNC_HEURE varchar(5) NULL);
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_AUDIENCE') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_AUDIENCE bit NOT NULL CONSTRAINT DF_P11_AC_AUD DEFAULT (1);
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_CHAMBRE') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_CHAMBRE varchar(5) NULL;
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_SECTION') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_SECTION varchar(10) NULL;
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_ID_JUG') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_ID_JUG int NULL;
        IF OBJECT_ID(N'v2.COMPOSITION') IS NULL
            CREATE TABLE v2.COMPOSITION (CMPST_ID_CMAUD int NOT NULL PRIMARY KEY, CMPST_ID_ADNC int NOT NULL);
        IF OBJECT_ID(N'v2.APPEL_AFFAIRE') IS NULL
            CREATE TABLE v2.APPEL_AFFAIRE (APPAF_ID_APPAF int NOT NULL PRIMARY KEY, APPAF_ID_INSTN int NULL, APPAF_NUM_ORDRE smallint NOT NULL,
                APPAF_ID_ADNC int NOT NULL);
        IF COL_LENGTH('v2.APPEL_AFFAIRE', 'APPAF_ID_CMAUD') IS NULL ALTER TABLE v2.APPEL_AFFAIRE ADD APPAF_ID_CMAUD int NULL;
        IF COL_LENGTH('v2.APPEL_AFFAIRE', 'APPAF_TYPE_CLASSEMENT') IS NULL ALTER TABLE v2.APPEL_AFFAIRE ADD APPAF_TYPE_CLASSEMENT varchar(5) NULL;
        IF COL_LENGTH('v2.APPEL_AFFAIRE', 'APPAF_HEURE_APPEL') IS NULL ALTER TABLE v2.APPEL_AFFAIRE ADD APPAF_HEURE_APPEL varchar(5) NULL;
        IF COL_LENGTH('v2.APPEL_AFFAIRE', 'APPAF_ETAT_PREPARATION') IS NULL ALTER TABLE v2.APPEL_AFFAIRE ADD APPAF_ETAT_PREPARATION varchar(1) NULL;
        IF COL_LENGTH('v2.APPEL_AFFAIRE', 'APPAF_ETAT_RETOUR') IS NULL ALTER TABLE v2.APPEL_AFFAIRE ADD APPAF_ETAT_RETOUR varchar(1) NULL;
        IF OBJECT_ID(N'v2.TYPE_CLASSEMENT') IS NULL
            CREATE TABLE v2.TYPE_CLASSEMENT (R_TYPECLASSE_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_TYPECLASSE_LIBELLE_ECRAN varchar(35) NOT NULL);
        IF OBJECT_ID(N'v2.CODE_AFFAIRE') IS NULL
            CREATE TABLE v2.CODE_AFFAIRE (R_CODEAFFAIR_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_CODEAFFAIR_LIBELLE_ECRAN varchar(100) NOT NULL);
        IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_TYPE_NUM_ROLE') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_TYPE_NUM_ROLE varchar(5) NULL;
        IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_TYPE_INSTANCE') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_TYPE_INSTANCE varchar(2) NULL;
        IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_NUM_REPERTOIRE') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_NUM_REPERTOIRE bit NULL;
        IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_NATURE_AFF') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_NATURE_AFF varchar(10) NULL;
        IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_CODE_ACTE_PRO') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_CODE_ACTE_PRO varchar(3) NULL;
        IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_NAT_AFF_PART') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_NAT_AFF_PART varchar(5) NULL;
        IF OBJECT_ID(N'v2.CODE_ACTE_PROCE') IS NULL
            CREATE TABLE v2.CODE_ACTE_PROCE (R_CODEACTEPR_VALEUR varchar(3) NOT NULL PRIMARY KEY, R_CODEACTEPR_LIBELLE_ECRAN varchar(35) NOT NULL);
        IF COL_LENGTH('v2.CODE_ACTE_PROCE', 'R_CODEACTEPR_ISO') IS NULL ALTER TABLE v2.CODE_ACTE_PROCE ADD R_CODEACTEPR_ISO varchar(20) NULL;
        IF OBJECT_ID(N'v2.MANDATAIRE_PC') IS NULL
            CREATE TABLE v2.MANDATAIRE_PC (MNDPC_ID_MNDPC int NOT NULL PRIMARY KEY, MNDPC_CIVILITE varchar(7) NOT NULL, MNDPC_NOM varchar(100) NOT NULL,
                MNDPC_COMPLEMENT_NOM varchar(100) NULL);
        IF OBJECT_ID(N'v2.GERER_PC') IS NULL
            CREATE TABLE v2.GERER_PC (GRRPC_ID_GRRPC int NOT NULL PRIMARY KEY, GRRPC_ID_MNDPC int NOT NULL, GRRPC_ID_PRCLL int NULL,
                GRRPC_NUM_ORDRE smallint NOT NULL, GRRPC_FONCTION_PC varchar(5) NOT NULL, GRRPC_FIN_MISSION bit NOT NULL, GRRPC_ID_PREVE int NULL);
        IF OBJECT_ID(N'v2.INTERVENANT_PC') IS NULL
            CREATE TABLE v2.INTERVENANT_PC (INTRP_ID_INTRP int NOT NULL PRIMARY KEY, INTRP_ID_PRCLL int NOT NULL, INTRP_NUM_ORDRE smallint NOT NULL,
                INTRP_CIVILITE varchar(7) NOT NULL, INTRP_NOM varchar(35) NOT NULL, INTRP_FONCTION_PC varchar(5) NOT NULL, INTRP_ID_INTRPR int NULL);
        -- Celle du banc (PROC_SUPP_ACTEUR_JUD.sql:34) n'a que l'id, la PC, le nom et l'intervenant répertorié (mesuré le 28/11/2026).
        IF COL_LENGTH('v2.INTERVENANT_PC', 'INTRP_NUM_ORDRE') IS NULL ALTER TABLE v2.INTERVENANT_PC ADD INTRP_NUM_ORDRE smallint NULL;
        IF COL_LENGTH('v2.INTERVENANT_PC', 'INTRP_CIVILITE') IS NULL ALTER TABLE v2.INTERVENANT_PC ADD INTRP_CIVILITE varchar(7) NULL;
        IF COL_LENGTH('v2.INTERVENANT_PC', 'INTRP_FONCTION_PC') IS NULL ALTER TABLE v2.INTERVENANT_PC ADD INTRP_FONCTION_PC varchar(5) NULL;
        IF OBJECT_ID(N'v2.CODE_EVE_PRO') IS NULL
            CREATE TABLE v2.CODE_EVE_PRO (R_CODEEVEPRO_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_CODEEVEPRO_LIBELLE_ECRAN varchar(35) NOT NULL);
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_TYPE_EVE_PRO') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_TYPE_EVE_PRO varchar(1) NULL;
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_AVEC_DEPOT') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_AVEC_DEPOT bit NULL;
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_CATEGORIE_DEPOT_INS') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_CATEGORIE_DEPOT_INS varchar(3) NULL;
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_AVEC_PIECE_NUMERISE_INSMES') IS NULL
            ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_AVEC_PIECE_NUMERISE_INSMES bit NOT NULL CONSTRAINT DF_P11_CEP_PIECE DEFAULT (0);
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_NATURE_DOC_GED') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_NATURE_DOC_GED varchar(50) NULL;
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_TYPE_DEPOSANT_GED') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_TYPE_DEPOSANT_GED varchar(50) NULL;
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_CODE_EVE_PRO_FUTUR') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_CODE_EVE_PRO_FUTUR varchar(10) NULL;
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_MOIS_DATE_FIXE') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_MOIS_DATE_FIXE tinyint NULL;
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_JOURS_DATE_FIXE') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_JOURS_DATE_FIXE tinyint NULL;
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_SEMAINES_DATE_FIXE') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_SEMAINES_DATE_FIXE tinyint NULL;
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_MOIS_DATE_MAXI') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_MOIS_DATE_MAXI tinyint NULL;
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_JOURS_DATE_MAXI') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_JOURS_DATE_MAXI tinyint NULL;
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_SEMAINES_DATE_MAXI') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_SEMAINES_DATE_MAXI tinyint NULL;
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_MOIS_DATE_MINI') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_MOIS_DATE_MINI tinyint NULL;
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_JOURS_DATE_MINI') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_JOURS_DATE_MINI tinyint NULL;
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_SEMAINES_DATE_MINI') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_SEMAINES_DATE_MINI tinyint NULL;
        IF OBJECT_ID(N'v2.CEP_EDITION') IS NULL
            CREATE TABLE v2.CEP_EDITION (CEPED_ID_CEPED int NOT NULL PRIMARY KEY, CEPED_CODE_EVE_PRO varchar(10) NOT NULL, CEPED_TYPE_EP varchar(1) NOT NULL,
                CEPED_CODE_EDITION varchar(20) NOT NULL, CEPED_DESTI_MODE_ENVOI varchar(250) NULL, CEPED_CONDITION varchar(250) NULL,
                CEPED_AUTRE_PARAMETRE varchar(100) NULL);
        IF OBJECT_ID(N'v2.CEP_ARTICLE') IS NULL
            CREATE TABLE v2.CEP_ARTICLE (CEPAR_ID_CEPAR int NOT NULL PRIMARY KEY, CEPAR_CODE_EVE_PRO varchar(10) NULL, CEPAR_TYPE_EP varchar(1) NULL,
                CEPAR_CODE_ARTICLE varchar(20) NULL, CEPAR_CONDITION varchar(250) NULL, CEPAR_NUM_ORDRE smallint NOT NULL);
        IF OBJECT_ID(N'v2.PARAMETRE_EDITION') IS NULL
            CREATE TABLE v2.PARAMETRE_EDITION (PRMED_CODE varchar(40) NOT NULL PRIMARY KEY, PRMED_LIBELLE varchar(100) NOT NULL,
                PRMED_TYPE_GENERATION varchar(15) NULL, PRMED_LISTE varchar(4000) NOT NULL);
        IF COL_LENGTH('v2.PARAMETRE_EDITION', 'PRMED_CODE_MAQUETTE') IS NULL ALTER TABLE v2.PARAMETRE_EDITION ADD PRMED_CODE_MAQUETTE varchar(40) NULL;
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_CODE_ENTITE_SOURCE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_CODE_ENTITE_SOURCE varchar(16) NULL;
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_VALEUR_ID_SOURCE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_VALEUR_ID_SOURCE varchar(40) NULL;
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_CODE_MAQUETTE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_CODE_MAQUETTE varchar(40) NULL;
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_CODE_PROCESSUS') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_CODE_PROCESSUS varchar(40) NULL;
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_ID_DMND') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_ID_DMND int NULL;
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_LIBELLE_DOCUMENT') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_LIBELLE_DOCUMENT varchar(255) NULL;
        IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = 'JRPC')
            INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, Lettre, TailleChrono, Debut) VALUES ('JRPC', YEAR(@aujourdhui), 'ALC', 4, 'F', 5, 1);
        IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = 'JGAFF')
            INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, TailleChrono, Debut) VALUES ('JGAFF', YEAR(@aujourdhui), 'AC', 4, 6, 1);
        """, $"""
        -- Les réglages de la clôture, comme RIG_DEV (PARAMETRAGE, lu le 28/11/2026).
        INSERT INTO soc.Reglage (Code, Libelle, Valeur)
        SELECT r.Code, r.Libelle, r.Valeur FROM (VALUES
            ('CLOT_EVE_PRO_INCITR', 'CLOT : évènement du courrier aux débiteurs', 'CLOTINCITR'),
            ('CLOT_CODE_AFFAIRE_SO', 'CLOT : code affaire de l''enrôlement sur saisine d''office', 'FCLIA7'),
            ('CLOT_MODE_SO', 'CLOT : mode de convocation sur saisine d''office', 'Huissier'),
            ('CLOT_EVE_PRO_CONVSO_HUI', 'CLOT : convocation sur saisine d''office par huissier', 'ED_CLOTLJ'),
            ('CLOT_SO_TYPECLA', 'CLOT : type de classement par défaut', 'PC'),
            ('DEMANDEUR_SO_CIVILITE', 'Demandeur des saisines d''office : civilité', 'PM'),
            ('DEMANDEUR_SO_DESIGNATION', 'Demandeur des saisines d''office : désignation', 'Saisine d''office')) AS r (Code, Libelle, Valeur)
        WHERE NOT EXISTS (SELECT 1 FROM soc.Reglage AS x WHERE x.Code = r.Code);
        IF NOT EXISTS (SELECT 1 FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = 'CLOTINCITR')
            INSERT INTO v2.CODE_EVE_PRO (R_CODEEVEPRO_VALEUR, R_CODEEVEPRO_LIBELLE_ECRAN, R_CODEEVEPRO_TYPE_EVE_PRO)
            VALUES ('CLOTINCITR', 'incitation requête en clôture', 'C');
        IF NOT EXISTS (SELECT 1 FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = 'ED_CLOTLJ')
            INSERT INTO v2.CODE_EVE_PRO (R_CODEEVEPRO_VALEUR, R_CODEEVEPRO_LIBELLE_ECRAN, R_CODEEVEPRO_TYPE_EVE_PRO, R_CODEEVEPRO_AVEC_DEPOT)
            VALUES ('ED_CLOTLJ', 'edition pour audience de cloture', 'E', 0);
        IF NOT EXISTS (SELECT 1 FROM v2.CEP_EDITION WHERE CEPED_CODE_EVE_PRO = 'CLOTINCITR')
            INSERT INTO v2.CEP_EDITION (CEPED_ID_CEPED, CEPED_CODE_EVE_PRO, CEPED_TYPE_EP, CEPED_CODE_EDITION, CEPED_DESTI_MODE_ENVOI, CEPED_CONDITION)
            VALUES (4100121, 'CLOTINCITR', 'P', 'INCIT_RDEB_CLOT_PC', 'DIR', '');
        IF NOT EXISTS (SELECT 1 FROM v2.CEP_EDITION WHERE CEPED_CODE_EVE_PRO = 'ED_CLOTLJ')
            INSERT INTO v2.CEP_EDITION (CEPED_ID_CEPED, CEPED_CODE_EVE_PRO, CEPED_TYPE_EP, CEPED_CODE_EDITION, CEPED_DESTI_MODE_ENVOI, CEPED_CONDITION)
            VALUES (4100122, 'ED_CLOTLJ', 'I', '01CLOTLJ', 'DI', '-NAIMP'), (4100123, 'ED_CLOTLJ', 'I', 'CONV_HUIS_CLOT', '', '-NAIMP');
        INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_TYPE_GENERATION, PRMED_LISTE, PRMED_CODE_MAQUETTE)
        SELECT p.Code, p.Libelle, 'FUSION_RIG', p.Liste, p.Maquette FROM (VALUES
            ('INCIT_RDEB_CLOT_PC', 'Incitation requête débiteur clôture PC', '', 'LOT2_INCIT_REQ_CLOT'),
            ('01CLOTLJ', 'Chemise clôture LJ', '<PARAMETRES CODE_ENTITE_SOURCE="EVT_PROC" CODE_MAQUETTE="3801_01CLOTLJ" CODE_PROCESSUS="EDIT_EPI" HAS_OBSERVATION="0" IMPRESSION="1" LIBELLE_DOCUMENT="Chemise clôture LJ" NOMBRE_EXEMPLAIRES="1" TYPE_GENERATION="FUSION_RIG" />', NULL),
            ('CONV_HUIS_CLOT', 'Convoc. clôture par huissier', '<PARAMETRES CODE_ENTITE_SOURCE="EVT_PROC" CODE_MAQUETTE="3801_CONV_HUIS_CLOT" CODE_PROCESSUS="EDIT_EPI" HAS_OBSERVATION="0" IMPRESSION="1" LIBELLE_DOCUMENT="Convoc. clôture par huissier" NOMBRE_EXEMPLAIRES="1" TYPE_GENERATION="FUSION_RIG" />', NULL)
            ) AS p (Code, Libelle, Liste, Maquette)
        WHERE NOT EXISTS (SELECT 1 FROM v2.PARAMETRE_EDITION AS x WHERE x.PRMED_CODE = p.Code);
        IF NOT EXISTS (SELECT 1 FROM v2.CODE_AFFAIRE WHERE R_CODEAFFAIR_VALEUR = 'FCLIA7')
            INSERT INTO v2.CODE_AFFAIRE (R_CODEAFFAIR_VALEUR, R_CODEAFFAIR_LIBELLE_ECRAN, R_CODEAFFAIR_TYPE_NUM_ROLE, R_CODEAFFAIR_TYPE_INSTANCE,
                R_CODEAFFAIR_NUM_REPERTOIRE, R_CODEAFFAIR_NATURE_AFF, R_CODEAFFAIR_CODE_ACTE_PRO, R_CODEAFFAIR_NAT_AFF_PART)
            VALUES ('FCLIA7', 'Clôture pour insuffisance d''actif', 'JRPC', '08', 1, '4HE', 'QLI', '0A');
        IF NOT EXISTS (SELECT 1 FROM v2.CODE_ACTE_PROCE WHERE R_CODEACTEPR_VALEUR = 'QLI')
            INSERT INTO v2.CODE_ACTE_PROCE (R_CODEACTEPR_VALEUR, R_CODEACTEPR_LIBELLE_ECRAN, R_CODEACTEPR_ISO) VALUES ('QLI', 'requête du liquidateur', '02');
        IF NOT EXISTS (SELECT 1 FROM v2.TYPE_CLASSEMENT WHERE R_TYPECLASSE_VALEUR = 'PC')
            INSERT INTO v2.TYPE_CLASSEMENT (R_TYPECLASSE_VALEUR, R_TYPECLASSE_LIBELLE_ECRAN) VALUES ('PC', 'procédures collectives');

        -- Les deux PC : P4100101 (le courrier) et P4100102 (l'enrôlement), un examen de clôture à J+10, un liquidateur, un huissier.
        DECLARE @identite bit = CONVERT(bit, OBJECTPROPERTY(OBJECT_ID(N'v2.PROCEDURE_COLLECTIVE'), 'TableHasIdentity'));
        IF @identite = 1 SET IDENTITY_INSERT v2.PROCEDURE_COLLECTIVE ON;
        INSERT INTO v2.PROCEDURE_COLLECTIVE (PRCLL_ID_PRCLL, PRCLL_NUM_PC, PRCLL_NOM_AFFAIRE, PRCLL_EXTERIEUR, PRCLL_CODE_CLOTURE, PRCLL_FORFAIT_FACTURE)
        VALUES ({Pc}, '{NumPc}', 'SONDE COURRIER AUX DEBITEURS', 0, NULL, 0), ({PcSo}, '{NumPcSo}', 'SONDE ENROLEMENT SO', 0, NULL, 0);
        IF @identite = 1 SET IDENTITY_INSERT v2.PROCEDURE_COLLECTIVE OFF;
        DECLARE @eve bit = CONVERT(bit, OBJECTPROPERTY(OBJECT_ID(N'v2.EVENEMENT_PROCEDURE'), 'TableHasIdentity'));
        IF @eve = 1 SET IDENTITY_INSERT v2.EVENEMENT_PROCEDURE ON;
        INSERT INTO v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR, EVNPR_ID_PRCLL, EVNPR_CODE_EVE_PRO, EVNPR_DATE, EVNPR_SURVENU, EVNPR_DELAI_EXPIRE,
            EVNPR_DATE_MAXI, EVNPR_TYPE_EVE_PRO, EVNPR_NUM_ORDRE)
        VALUES (4100111, {Pc}, '4CLOT', DATEADD(DAY, -20, @aujourdhui), 0, 0, DATEADD(DAY, 10, @aujourdhui), 'F', 1),
               (4100112, {PcSo}, '4CLO', NULL, 0, 0, DATEADD(DAY, 10, @aujourdhui), '4', 1);
        IF @eve = 1 SET IDENTITY_INSERT v2.EVENEMENT_PROCEDURE OFF;
        INSERT INTO v2.MANDATAIRE_PC (MNDPC_ID_MNDPC, MNDPC_CIVILITE, MNDPC_NOM, MNDPC_COMPLEMENT_NOM) VALUES (4100191, 'MAITRE', 'SONDE', 'Liquidateur');
        INSERT INTO v2.GERER_PC (GRRPC_ID_GRRPC, GRRPC_ID_MNDPC, GRRPC_ID_PRCLL, GRRPC_NUM_ORDRE, GRRPC_FONCTION_PC, GRRPC_FIN_MISSION)
        VALUES (4100195, 4100191, {Pc}, 1, 'LIQ', 0), (4100196, 4100191, {PcSo}, 1, 'LIQ', 0);
        INSERT INTO v2.INTERVENANT_PC (INTRP_ID_INTRP, INTRP_ID_PRCLL, INTRP_NUM_ORDRE, INTRP_CIVILITE, INTRP_NOM, INTRP_FONCTION_PC)
        VALUES (4100181, {PcSo}, 1, 'SCP', 'SONDE HUISSIER', 'HUI');

        -- P4100103 : la même, laissée à l'écran pour la preuve en image (PauseCaptureAsync) ; la sonde n'y touche pas.
        IF @identite = 1 SET IDENTITY_INSERT v2.PROCEDURE_COLLECTIVE ON;
        INSERT INTO v2.PROCEDURE_COLLECTIVE (PRCLL_ID_PRCLL, PRCLL_NUM_PC, PRCLL_NOM_AFFAIRE, PRCLL_EXTERIEUR, PRCLL_CODE_CLOTURE, PRCLL_FORFAIT_FACTURE)
        VALUES ({PcCapture}, '{NumPcCapture}', 'CAPTURE ENROLEMENT SO', 0, NULL, 0);
        IF @identite = 1 SET IDENTITY_INSERT v2.PROCEDURE_COLLECTIVE OFF;
        IF @eve = 1 SET IDENTITY_INSERT v2.EVENEMENT_PROCEDURE ON;
        INSERT INTO v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR, EVNPR_ID_PRCLL, EVNPR_CODE_EVE_PRO, EVNPR_DATE, EVNPR_SURVENU, EVNPR_DELAI_EXPIRE,
            EVNPR_DATE_MAXI, EVNPR_TYPE_EVE_PRO, EVNPR_NUM_ORDRE)
        VALUES (4100113, {PcCapture}, '4CLO', NULL, 0, 0, DATEADD(DAY, 12, @aujourdhui), '4', 1);
        IF @eve = 1 SET IDENTITY_INSERT v2.EVENEMENT_PROCEDURE OFF;
        INSERT INTO v2.GERER_PC (GRRPC_ID_GRRPC, GRRPC_ID_MNDPC, GRRPC_ID_PRCLL, GRRPC_NUM_ORDRE, GRRPC_FONCTION_PC, GRRPC_FIN_MISSION)
        VALUES (4100197, 4100191, {PcCapture}, 1, 'LIQ', 0);
        INSERT INTO v2.INTERVENANT_PC (INTRP_ID_INTRP, INTRP_ID_PRCLL, INTRP_NUM_ORDRE, INTRP_CIVILITE, INTRP_NOM, INTRP_FONCTION_PC)
        VALUES (4100182, {PcCapture}, 1, 'SCP', 'SONDE HUISSIER', 'HUI');

        -- L'audience de clôture du jour + 14 à 09:30, seule ce jour-là.
        INSERT INTO v2.AUDIENCE_CABINET (AUDNC_ID_ADNC, AUDNC_DATE, AUDNC_HEURE, AUDNC_AUDIENCE) VALUES (4100131, DATEADD(DAY, 14, @aujourdhui), '09:30', 1);
        """);

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        await PauseCaptureAsync(c);
        var enrolement = await EnrolementAsync(c);
        if (!enrolement.Fait || !enrolement.Relu) return enrolement;
        var courrier = await CourrierAsync(c);
        return courrier.Fait && courrier.Relu ? new(true, true, $"{enrolement.Detail} — en second, {courrier.Detail}") : courrier;
    }

    /// <summary>Le geste principal : l'enrôlement sur saisine d'office de P4100102, sa jumelle refusée, puis sa relecture par le pont.</summary>
    private static async Task<PreuveSonde> EnrolementAsync(ContexteSonde c)
    {
        static string J(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var avant = await EcranCloture.AffairesAsync(c.Client, c.Greffe, null, null);
        if (avant.Lignes.FirstOrDefault(l => l.NumPc == NumPcSo) is not { Id: { } id })
            return new(false, false, $"la PC {NumPcSo} du jeu n'est pas dans la grille ({avant.Mode}{(avant.Message is null ? "" : " « " + avant.Message + " »")}, " +
                                     $"{avant.Lignes.Length} ligne(s))");

        // 1. La jumelle : le jour de l'audience du jeu, mais à 23:59, où aucune audience n'existe -> refusée, rien au journal. (Une date
        //    seule ne suffit pas : la base du banc porte les audiences d'autres jeux, dont une le jour + 15 -- mesuré le 28/11/2026.)
        var journal = await c.Journal();
        var jumelle = await EcranCloture.CreerEnrolementsAsync(c.Client, c.Greffe, [id], J(c.Jour.AddDays(14)), "23:59", "", "", facturer: false);
        if (jumelle.Fait || jumelle.SansEcrivain || !jumelle.Message.Contains("Aucune audience", StringComparison.Ordinal))
            return new(false, false, $"jumelle sans audience : attendu un refus « Aucune audience… », rendu {(jumelle.Fait ? "fait" : "refus")} « {jumelle.Message} »");
        if (await c.Journal() != journal) return new(false, false, "jumelle refusée mais écrite au journal des gestes");

        // 2. L'enrôlement à l'audience du jour + 14 (09:30), appel à 09:45, classement par défaut.
        var r = await EcranCloture.CreerEnrolementsAsync(c.Client, c.Greffe, [id], J(c.Jour.AddDays(14)), "09:30", "", "09:45", facturer: false);
        if (!r.Fait) return new(false, false, $"Enrôlement sur saisine d'office : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");
        if (await c.Journal() == journal) return new(true, false, "Enrôlement rendu fait sans trace au journal des gestes");

        // 3. Relu par le pont : l'instance, son appel, sa convocation, le 4CLO survenu ; la PC a quitté la grille.
        var jour = DateOnly.FromDateTime(c.Jour);
        var lus = await EcranCloture.EnrolementsAsync(c.Client, c.Greffe, jour, jour);
        var e = lus.Lignes.FirstOrDefault(l => l.NumPc == NumPcSo);
        if (e is null) return new(true, false, $"Enrôlement fait (« {r.Message} ») mais aucune instance de clôture relue pour {NumPcSo} ({lus.Mode}, {lus.Lignes.Length} ligne(s))");
        var attendu = new[]
        {
            // La série JRPC du banc peut venir d'un autre jeu (lettre J, mesuré le 28/11/2026) : seule l'année du rôle est sûre.
            ("rôle JRPC", e.Role.StartsWith($"{c.Jour.Year}", StringComparison.Ordinal)), ("répertoire JGAFF", e.Repertoire.StartsWith($"{c.Jour.Year}", StringComparison.Ordinal)),
            ("demandeur", e.Demandeur == "Saisine d'office"), ("défendeur", e.Defendeur == "SONDE ENROLEMENT SO"),
            ("acte", e.ActeSaisine == "requête du liquidateur"), ("enrôlement", e.Enrolement == J(c.Jour)),
            ("audience", e.Audience == J(c.Jour.AddDays(14)) && e.HeureAudience == "09:30"), ("classement", e.Classement == "PC"),
            ("heure d'appel", e.HeureAppel == "09:45"), ("convocation", e.Convocation == "ED_CLOTLJ" && e.DateConvocation == J(c.Jour)),
            ("4CLO survenu", e.ExamenSurvenu == J(c.Jour)),
        };
        if (attendu.Where(a => !a.Item2).Select(a => a.Item1).ToArray() is { Length: > 0 } faux)
            return new(true, false, $"Enrôlement fait mais relu faux ({string.Join(", ", faux)}) : rôle « {e.Role} », répertoire « {e.Repertoire} », " +
                                    $"{e.Demandeur} # {e.Defendeur}, acte « {e.ActeSaisine} », audience {e.Audience} {e.HeureAudience}, classement « {e.Classement} », " +
                                    $"appel « {e.HeureAppel} », convocation {e.Convocation} {e.DateConvocation}, 4CLO survenu « {e.ExamenSurvenu} »");
        var apres = await EcranCloture.AffairesAsync(c.Client, c.Greffe, null, null);
        if (apres.Lignes.Any(l => l.NumPc == NumPcSo)) return new(true, false, $"Enrôlement fait mais la PC {NumPcSo} reste dans la grille « Sélection PC »");

        // Contrôle SQL : la demande EF validée, ses liens 2J / 2P / 2V, ses deux éditions de convocation en file.
        var demande = await c.Valeur($"""
            SELECT COUNT(*) FROM v2.DEMANDE AS d
            WHERE d.DMND_CODE_PROSS = 'EF' AND d.DMND_ETAT_DEMANDE = 'V' AND d.DMND_NUM_INDICATIF = '{e.Role}' AND d.DMND_LES_NUMEROS = '{e.Role} {e.Repertoire}'
              AND d.DMND_NOM = 'Saisine d''office # SONDE ENROLEMENT SO'
              AND EXISTS (SELECT 1 FROM v2.DOSSIER_DEMANDE AS x WHERE x.DSSDM_ID_DMND = d.DMND_ID_DMND AND x.DSSDM_CODE_SEQUENCEUR = '2J' AND x.DSSDM_VALEUR_SEQUENCEUR = '{e.IdInstance}')
              AND EXISTS (SELECT 1 FROM v2.DOSSIER_DEMANDE AS x WHERE x.DSSDM_ID_DMND = d.DMND_ID_DMND AND x.DSSDM_CODE_SEQUENCEUR = '2P' AND x.DSSDM_VALEUR_SEQUENCEUR = '{PcSo}')
              AND EXISTS (SELECT 1 FROM v2.DOSSIER_DEMANDE AS x WHERE x.DSSDM_ID_DMND = d.DMND_ID_DMND AND x.DSSDM_CODE_SEQUENCEUR = '2V')
            """);
        var editions = await c.Valeur($"""
            SELECT COUNT(*) FROM v2.DEMANDE_EDITION AS de JOIN v2.EVENEMENT_PROCEDURE AS ev ON ev.EVNPR_NUMERO_SOURCE = de.DMNED_VALEUR_ID_SOURCE
            WHERE ev.EVNPR_ID_INSTN = {e.IdInstance} AND ev.EVNPR_CODE_EVE_PRO = 'ED_CLOTLJ' AND de.DMNED_CODE_ENTITE_SOURCE = 'EVT_PROC'
            """);
        return demande == "1" && editions == "2"
            ? new(true, true, $"Enrôlement sur saisine d'office fait (« {r.Message} ») ; jumelle sans audience refusée, rien écrit ; relu par le pont : instance " +
                              $"{e.Role} (répertoire {e.Repertoire}) de la PC {NumPcSo}, {e.Demandeur} # {e.Defendeur}, appel à l'audience du {e.Audience} " +
                              $"{e.HeureAudience} (classement {e.Classement}, appel {e.HeureAppel}), convocation {e.Convocation} du {e.DateConvocation}, " +
                              $"4CLO survenu le {e.ExamenSurvenu}, PC sortie de la grille ; contrôle SQL : demande EF validée liée 2J/2P/2V, 2 éditions en file")
            : new(true, false, $"Enrôlement relu par le pont, mais contrôle SQL : {demande} demande EF conforme, {editions} édition(s) de convocation en file (2 attendues)");
    }

    /// <summary>Le geste second : le courrier aux débiteurs de P4100101, relu par sa colonne « Dernier courrier envoyé ».</summary>
    private static async Task<PreuveSonde> CourrierAsync(ContexteSonde c)
    {
        var avant = await EcranCloture.AffairesAsync(c.Client, c.Greffe, null, null);
        if (avant.Lignes.FirstOrDefault(l => l.NumPc == NumPc) is not { Id: { } id } ligne)
            return new(false, false, $"la PC {NumPc} du jeu n'est pas dans la grille ({avant.Mode}, {avant.Lignes.Length} ligne(s))");
        if (ligne.DernierCourrier.Length > 0) return new(false, false, $"la PC {NumPc} a déjà un courrier ({ligne.DernierCourrier}) avant le geste");

        var journal = await c.Journal();
        var r = await EcranCloture.EnvoyerCourrierDebiteursAsync(c.Client, c.Greffe, [id], facturer: false);
        if (!r.Fait) return new(false, false, $"Courrier aux débiteurs : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");
        if (await c.Journal() == journal) return new(true, false, "Courrier aux débiteurs rendu fait sans trace au journal des gestes");

        var apres = await EcranCloture.AffairesAsync(c.Client, c.Greffe, null, null);
        var relue = apres.Lignes.FirstOrDefault(l => l.NumPc == NumPc);
        var jour = c.Jour.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var courriers = await c.Valeur($"""
            SELECT COUNT(*) FROM v2.DEMANDE_EDITION AS de JOIN v2.EVENEMENT_PROCEDURE AS e ON e.EVNPR_NUMERO_SOURCE = de.DMNED_VALEUR_ID_SOURCE
            WHERE e.EVNPR_ID_PRCLL = {Pc} AND e.EVNPR_CODE_EVE_PRO = 'CLOTINCITR' AND e.EVNPR_SURVENU = 1 AND de.DMNED_CODE_ENTITE_SOURCE = 'EVT_PROC'
              AND de.DMNED_CODE_MAQUETTE = 'LOT2_INCIT_REQ_CLOT'
            """);
        return relue?.DernierCourrier == jour && courriers == "1"
            ? new(true, true, $"courrier aux débiteurs fait, relu : la PC {NumPc} porte son dernier courrier du {jour}, son courrier dans la file des éditions")
            : new(true, false, $"Courrier aux débiteurs rendu fait, mais relu : dernier courrier « {relue?.DernierCourrier ?? "PC absente"} », " +
                               $"{courriers} courrier(s) dans la file");
    }
}
