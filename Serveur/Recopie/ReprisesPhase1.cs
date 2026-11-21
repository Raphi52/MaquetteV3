using Microsoft.Data.SqlClient;
using RigV3.Serveur.Numerotation.Series;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Recopie;

// ============================================================
// LES REPRISES DE LA PHASE 1, APPELEES PAR LE TRANSFERT ET NON REFAITES (REC-001).
//
//   reglages du greffe   SOC-003  RepriseReglages      PARAMETRAGE, GREFFE_PARAMETRE, MAIL_TEMPLATE (instantane)
//   comptes et droits    SOC-002  RepriseDroits        UTILISATEUR, SERVICE et les 8 tables de droits (instantane)
//   compteurs de numeros NUM-005  num.Reprendre        NUMERO_METIER et ses 3 tables (base V2 VIVANTE, en dernier)
//
// Chacune garde ses regles, ses refus et sa preuve (son banc : SocleGreffesCheck, SocleDroitsCheck,
// NumerotationSeriesCheck) ; le transfert y ajoute le comptage, la correspondance des tables portees une ligne pour une
// ligne et l'arret sur un ecart. Les compteurs se lisent dans la V2 vivante et non dans l'instantane : la V2 donne des
// numeros toute la nuit, et num.Reprendre les prend sous blocage a leur derniere valeur (NUM-005 CA01) ; le
// basculement des trois procedures de la V2 dans la meme transaction est le geste du passage (lot 13, SOC-024).
// Colonnes attendues relevees dans RIG_DEV (SQL-DEV\DEV) le 2026-11-21 par « RigV3.Transfert inventaire ».
// ============================================================

internal abstract class ReprisePhase1
{
    protected static RegleTable Table(string table, string story, params string[] colonnes)
        => new(table, SorteRegle.Reprise, story, colonnes);

    protected static RegleTable Table(string table, string story, bool facultative, params string[] colonnes)
        => new(table, SorteRegle.Reprise, story, colonnes, facultative);

    protected static async Task<int> CompterAsync(SqlConnection cn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }
}

internal sealed class RepriseDesReglages : ReprisePhase1, IRepriseTransfert
{
    public string Nom => "reglages du greffe";
    public string Story => "SOC-003";
    public int Rang => 10;
    public bool ApresLesTables => false;

    public IEnumerable<RegleTable> Regles =>
    [
        Table("PARAMETRAGE", "SOC-003",
            "PARAM_CODE", "PARAM_LIBELLE", "PARAM_VALEUR", "PARAM_AUTEUR_CREATION", "PARAM_DATE_CREATION",
            "PARAM_AUTEUR_MODIF", "PARAM_DATE_MODIF", "PARAM_UTILISATION", "PARAM_ID_PARAM"),
        Table("GREFFE_PARAMETRE", "SOC-003",
            "GRPAR_ROMPU", "GRPAR_RELEVE_CLT_IS_SOLDE", "GRPAR_COMMENTAIRE_COMPTE", "GRPAR_COMMENTAIRE_DIVERS",
            "GRPAR_ADRESSE_IS_PRINCIPALE", "GRPAR_TVA_SUR_ENCMT", "GRPAR_ID_BANQU", "GRPAR_COMPTE_CLT_DIVERS",
            "GRPAR_PUB_COMPTE_LIGNE1", "GRPAR_PUB_COMPTE_LIGNE2", "GRPAR_PUB_DIVERS_LIGNE1", "GRPAR_PUB_DIVERS_LIGNE2",
            "GRPAR_MODE_COMPTABILISATION", "GRPAR_COMPTE_COLLECTIF_CLT", "GRPAR_CENTRALISATION",
            "GRPAR_CENTRALISATION_COMPTE", "GRPAR_DUPLICATA_LETTRE_CHEQUE", "GRPAR_USE_LC_TROP_PERCU",
            "GRPAR_DERNIER_NUM_LC", "GRPAR_GREFFE_ADR_COMPLETE", "GRPAR_FACTURER_M_AU_ROLE", "GRPAR_ID_PAYEUR_DEFAUT",
            "GRPAR_TPG_DOMICILIATION_BANCAIRE", "GRPAR_TPG_CODE_BANQUE", "GRPAR_TPG_CODE_GUICHET", "GRPAR_TPG_NUM_COMPT",
            "GRPAR_TPG_CLE_RIB", "GRPAR_TPG_TITULAIRE_BANCAIRE", "GRPAR_TPG_ADRESSE_BANCAIRE",
            "GRPAR_TPG_FONCTION_SIGNATAIRE_ORDONNANCE", "GRPAR_TPG_NOM_SIGNATAIRE_ORDONNANCE", "GRPAR_TPG_NOM_GREFFIER",
            "GRPAR_COMMENTAIRE_COMPTE_PC", "GRPAR_EDIT_FACT_COMPTE_PC", "GRPAR_MONTANT_DMND_PROVISION",
            "GRPAR_NUM_TVA_INTRACOMM", "GRPAR_COMPTA_TRESO_USED", "GRPAR_COMPTA_TYPE_COMPTA", "GRPAR_GENERATION_OP_TPG",
            "GRPAR_COMPTA_EMOL_HT_196", "GRPAR_COMPTA_EMOL_HT_206", "GRPAR_COMPTA_EMOL_HT_186", "GRPAR_COMPTA_TVA_196",
            "GRPAR_COMPTA_TVA_206", "GRPAR_COMPTA_TVA_186", "GRPAR_COMPTA_DEB_BODACC", "GRPAR_COMPTA_DEB_INPI",
            "GRPAR_COMPTA_DEB_AUTRE_EXO", "GRPAR_COMPTA_COMPTE_BANQUE_PAIEMENT_TVA",
            "GRPAR_COMPTA_JOURNAL_ECRITURE_AUTO_TVA", "GRPAR_COMPTA_TVA_ATTENTE", "GRPAR_LIEU_CONTEST_CREANCE",
            "GRPAR_LC_SI_PAIE_TVA", "GRPAR_TPG_USE_SIGNATURE_JC", "GRPAR_USE_TX_BASE", "GRPAR_TX_BASE",
            "GRPAR_TX_BASE_FRAIS", "GRPAR_GESTION_TP_SIMPLIFIE", "GRPAR_COMPTA_BLOCAGE_AUTO", "GRPAR_COMPTA_EMOL_HT_20",
            "GRPAR_COMPTA_TVA_20"),
        Table("MAIL_TEMPLATE", "SOC-003", facultative: true,
            "MAILT_ID_MAILT", "MAILT_CODE", "MAILT_CONTENU", "MAILT_DATE_MODIF", "MAILT_AUTEUR_MODIF"),
    ];

    /// <summary>La reprise des reglages remplace a chaque passage ce que la V3 tient (chaque repetition repart de la
    /// V2) : elle est toujours appelee, puis sa comparaison valeur par valeur (SOC-003 CA01) ; un ecart arrete.</summary>
    public async Task<BilanRepriseTransfert> ExecuterAsync(ContexteReprise c, CancellationToken ct)
    {
        await using var v2 = new SqlConnection(c.ConnexionInstantane);
        await using var v3 = new SqlConnection(c.ConnexionV3);
        await v2.OpenAsync(ct);
        await v3.OpenAsync(ct);
        var bilan = await RepriseReglages.ReprendreAsync(v2, v3, ct);
        var ecarts = await RepriseReglages.ComparerAsync(v2, v3, ct);
        return new BilanRepriseTransfert(false,
            $"{bilan.Reglages} reglages, {bilan.Parametres} parametres du greffe, {bilan.Modeles} modeles de courriel ; comparaison : {ecarts.Count} ecart",
            [
                new TablePortee("PARAMETRAGE", "soc.Reglage",
                    "SELECT CONVERT(nvarchar(200), IdV2), CONVERT(nvarchar(200), Code) FROM soc.Reglage WHERE IdV2 IS NOT NULL"),
                new TablePortee("MAIL_TEMPLATE", "soc.ModeleCourriel",
                    "SELECT CONVERT(nvarchar(200), IdV2), CONVERT(nvarchar(200), Code) FROM soc.ModeleCourriel WHERE IdV2 IS NOT NULL"),
            ],
            [.. ecarts.Select(e => $"{e.Table} {e.Cle} {e.Colonne} : V2 « {e.ValeurV2} », V3 « {e.ValeurV3} »")]);
    }
}

internal sealed class RepriseDesComptesEtDroits : ReprisePhase1, IRepriseTransfert
{
    public string Nom => "comptes et droits";
    public string Story => "SOC-002";
    public int Rang => 20;
    public bool ApresLesTables => false;

    public IEnumerable<RegleTable> Regles =>
    [
        Table("UTILISATEUR", "SOC-001",
            "UTLST_ID_SRVC", "UTLST_CODE_DIRECTION", "UTLST_ID_SMSTN", "UTLST_CODE_GREFFE", "UTLST_ABREGE",
            "UTLST_CIVILITE", "UTLST_LOGIN", "UTLST_PASSWORD", "UTLST_NOM", "UTLST_PRENOM", "UTLST_P100_DISPONIBILITE",
            "UTLST_TEXTE_DISPONIBILITE", "UTLST_DESACTIVE", "UTLST_TEL_DIRECT", "UTLST_ID_UTLST",
            "UTLST_IMPRIMANTE_DEFAUT", "UTLST_IMPRIMANTE_DEFAUT_LOT1", "UTLST_INITIALES", "UTLST_LAST_SERVER",
            "UTLST_LAST_SESSIONID", "UTLST_BAC_SORTIE", "UTLST_FILE_DIFFERE", "UTLST_DATE_CREATION", "UTLST_ISAMITEL",
            "UTLST_ISADMIN", "UTLST_SCANNER_DEFAUT", "UTLST_IS_OUTILS", "UTLST_IS_HOTLINE", "UTLST_PROFIL_SCANNER_DEFAUT",
            "UTLST_ID_SCANNER_DEFAUT", "UTLST_IS_MS_OFFICE"),
        Table("SERVICE", "SOC-004",
            "SRVC_CODE_DIRECTION", "SRVC_CODE_GREFFE", "SRVC_ABREGE", "SRVC_NOM", "SRVC_DISTRI_SPECIFIQUE",
            "SRVC_DISTRIBUTION_AUTO", "SRVC_HEURE_DISTRIBUTION", "SRVC_ID_SRVC"),
        Table("HABILIT_ROLE_USER", "SOC-002",
            "HROLE_NOM_ROLE", "HROLE_LIBELLE", "HROLE_ONLYAMITEL", "HROLE_ONLYADMIN"),
        Table("PROCESSUS_ET_FONCTIONALITE", "SOC-002",
            "PROCF_CODE_PROC", "PROCF_TYPE", "PROCF_LIBELLE", "PROCF_NOM_IMAGE", "PROCF_DIRECTEUR", "PROCF_DESCRIPTION",
            "PROCF_EXE", "PROCF_EXE_ARGUMENT", "PROCF_DLL", "PROCF_DLL_FORMULAIRE", "PROCF_VISIBLE_DANS_MENU",
            "PROCF_VISIBLE_SI_INTERDIT", "PROCF_EXECUTABLE", "PROCF_UNIQUE", "PROCF_ONLYAMITEL", "PROCF_CATEGORIE",
            "PROCF_MODE_EDITION", "PROCF_CODE_DATE_TRAVAIL", "PROCF_ONLYADMIN", "PROCF_DOMAINE", "PROCF_DETACHABLE",
            "PROCF_IGNORE_FERM_ACC", "PROCF_ALGO_AGRAFAGE", "PROCF_DETACHABLE_ECRAN", "PROCF_NOM_CAT_VGED",
            "PROCF_FORCE_AGRAFAGE", "PROCF_LIBELLE_DEMANDE_EDITION"),
        Table("HABILIT_ROLE_USER_PROCESSUS", "SOC-002",
            "HRFON_ID_HRFON", "HRFON_NOM_ROLE", "HRFON_CODE_PROC"),
        Table("HABILIT_UTILISATEUR_ROLE_USER", "SOC-002",
            "HUROL_ID_HUROL", "HUROL_ID_UTLST", "HUROL_NOM_ROLE", "HUROL_LOGIN_UTLST"),
        Table("UTILISATEUR_A_POUR_ROLE", "SOC-002",
            "X_UTARL_ID_UTROL", "X_UTARL_ID_UTLST"),
        Table("COMPOSER_HABILITATION", "SOC-002",
            "X_CPSHB_DEGRE", "X_CPSHB_REFERENCE_CTGR", "X_CPSHB_ID_UTROL"),
        Table("EXPRESSO_MENU", "SOC-002",
            "EXPME_ID_EXPME", "EXPME_LIBELLE", "EXPME_FORM", "EXPME_CLE", "EXPME_ID_EXPON", "EXPME_NEW_CODE",
            "EXPME_VB6_INTERDIT"),
        Table("EXPRESSO_DROITS", "SOC-002",
            "EXPDR_ID_EXPME", "EXPDR_ID_UTLST"),
    ];

    /// <summary>RepriseDroits ne se joue qu'une fois, sur une base V3 sans compte : une base qui en a deja n'est pas
    /// reprise une seconde fois (« deja faite »), ses correspondances et ses comptes sont seulement verifies.</summary>
    public async Task<BilanRepriseTransfert> ExecuterAsync(ContexteReprise c, CancellationToken ct)
    {
        int comptes;
        await using (var v3 = new SqlConnection(c.ConnexionV3))
        {
            await v3.OpenAsync(ct);
            comptes = await CompterAsync(v3, "SELECT COUNT(*) FROM soc.Compte;", ct);
        }
        string detail;
        if (comptes > 0) detail = $"{comptes} comptes deja dans la base V3";
        else
        {
            var b = await RepriseDroits.ReprendreAsync(c.ConnexionInstantane, c.ConnexionV3, ct);
            detail = $"{b.Comptes} comptes, {b.Services} services, {b.Roles} roles, {b.Fonctions} fonctions, {b.Droits} droits " +
                     $"({b.RolesAttribues} roles attribues, {b.DroitsAncien} de l'ancien systeme, {b.DroitsComptabilite} de la comptabilite)";
        }
        return new BilanRepriseTransfert(comptes > 0, detail,
        [
            new TablePortee("UTILISATEUR", "soc.Compte",
                "SELECT CONVERT(nvarchar(200), IdV2), CONVERT(nvarchar(200), Id) FROM soc.Compte WHERE IdV2 IS NOT NULL"),
            new TablePortee("SERVICE", "soc.Service",
                "SELECT CONVERT(nvarchar(200), IdV2), CONVERT(nvarchar(200), Id) FROM soc.Service WHERE IdV2 IS NOT NULL"),
        ], []);
    }
}

internal sealed class RepriseDesCompteurs : ReprisePhase1, IRepriseTransfert
{
    public string Nom => "compteurs de numeros";
    public string Story => "NUM-005";
    public int Rang => 90;
    public bool ApresLesTables => true;

    public IEnumerable<RegleTable> Regles =>
    [
        Table("NUMERO_METIER", "NUM-005",
            "NMMTR_ID_NMMTR", "NMMTR_TYPE_METIER", "NMMTR_LIBELLE", "NMMTR_STRUCTURE_NUMERO", "NMMTR_CODE_GREFFE",
            "NMMTR_ANNEE", "NMMTR_CODAGE_ANNEE", "NMMTR_LETTRE_CATEGORIE", "NMMTR_TAILLE_CHRONO", "NMMTR_CHRONO_DEBUT",
            "NMMTR_CHRONO", "NMMTR_DATE_ATTRIBUTION", "NMMTR_QUANTIEME", "NMMTR_GESTION_NUM_ERR",
            "NMMTR_ATTRIBUTION_ERRONEE", "NMMTR_EXPLICATION", "NMMTR_FILE_REDIRECTION", "NMMTR_PARQUEE",
            "NMMTR_DATE_PARQUAGE", "NMMTR_NOM_RESP_PARQUAGE", "NMMTR_DATE_CREATION", "NMMTR_AUTEUR_CREATION",
            "NMMTR_DATE_MODIF", "NMMTR_AUTEUR_MODIF", "NMMTR_SEPARATEUR", "NMMTR_TABLE_AFFECTEE", "NMMTR_ATTRIBUT_AFFECTE"),
        Table("NUMERO_METIER_SAVE_CDU", "NUM-005", facultative: true,
            "NMMTR_ID_NMMTR", "NMMTR_TYPE_METIER", "NMMTR_LIBELLE", "NMMTR_STRUCTURE_NUMERO", "NMMTR_CODE_GREFFE",
            "NMMTR_ANNEE", "NMMTR_CODAGE_ANNEE", "NMMTR_LETTRE_CATEGORIE", "NMMTR_TAILLE_CHRONO", "NMMTR_CHRONO_DEBUT",
            "NMMTR_CHRONO", "NMMTR_DATE_ATTRIBUTION", "NMMTR_QUANTIEME", "NMMTR_GESTION_NUM_ERR",
            "NMMTR_ATTRIBUTION_ERRONEE", "NMMTR_EXPLICATION", "NMMTR_FILE_REDIRECTION", "NMMTR_PARQUEE",
            "NMMTR_DATE_PARQUAGE", "NMMTR_NOM_RESP_PARQUAGE", "NMMTR_DATE_CREATION", "NMMTR_AUTEUR_CREATION",
            "NMMTR_DATE_MODIF", "NMMTR_AUTEUR_MODIF", "NMMTR_SEPARATEUR", "NMMTR_TABLE_AFFECTEE", "NMMTR_ATTRIBUT_AFFECTE"),
        Table("NUMERO_CHRONO_MANQUANT", "NUM-013", facultative: true,
            "NMCHM_NUMERO_CHRONO", "NMCHM_TYPE", "NMCHM_ANNEE", "NMCHM_DOMAINE", "NMCHM_TYPE_METIER", "NMCHM_ID_NMCHM"),
        Table("GREFFE_NUMERO_LIAISON", "NUM-019", facultative: true,
            "GRNLN_CODE"),
    ];

    /// <summary>num.Reprendre (NUM-005) sur la base V2 vivante, puis num.ReleverHeritage hors blocage. -10 (la base V3
    /// a deja des compteurs) : deja faite ; -1 (ecart au rapprochement, tout annule) et -11 : ecart, le transfert s'arrete.</summary>
    public async Task<BilanRepriseTransfert> ExecuterAsync(ContexteReprise c, CancellationToken ct)
    {
        await using var v3 = new SqlConnection(c.ConnexionV3);
        await v3.OpenAsync(ct);
        var r = await RepriseNumerotation.ReprendreAsync(v3, null, c.BaseV2, ct: ct);
        if (r.Code == -10)
            return new BilanRepriseTransfert(true, r.Message ?? "compteurs deja repris", [], []);
        if (r.Code != 0)
            return new BilanRepriseTransfert(false, $"num.Reprendre : code {r.Code} — {r.Message}", [],
                [$"num.Reprendre code {r.Code} : {r.Message}", .. r.Ecarts.Select(e => $"{e.Type} {e.Annee} jour {e.Jour} {e.Quoi} : V2 {e.V2}, V3 {e.V3}")]);
        var signales = await RepriseNumerotation.ReleverHeritageAsync(v3, null, c.BaseV2, r.IdReprise, ct);
        return new BilanRepriseTransfert(false,
            $"reprise {r.IdReprise} : {r.LignesV2} lignes V2, {r.Compteurs} compteurs, blocage {r.DureeMs} ms, {r.Ecarts.Count} ecart au rapprochement, " +
            $"{signales} valeurs heritees signalees au referent", [], []);
    }
}
