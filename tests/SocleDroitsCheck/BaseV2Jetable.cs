using Microsoft.Data.SqlClient;

namespace SocleDroitsCheck;

/// <summary>
/// UNE PETITE BASE V2, jetable, sur le SQL Server du poste : les tables de droits de la V2, reduites aux
/// colonnes que la reprise et l'oracle lisent (memes noms, memes types que
/// D:\RigApplication\Source\BaseDeDonnées\RigDatabase\dbo\Tables), et un jeu de comptes qui fait passer
/// chaque origine de droits de la V2 par son cas limite. RIG_DEV n'est jamais ecrit : il est lu a part.
/// </summary>
public static class BaseV2Jetable
{
    public const int Responsable = 101, Amitel = 102, Administrateur = 103, Agent = 104, Ancien3 = 105, Ancien2 = 106, Compta = 107, Parti = 108;

    public static async Task CreerAsync(string connexion)
    {
        await using var cn = new SqlConnection(connexion);
        await cn.OpenAsync();
        foreach (var lot in System.Text.RegularExpressions.Regex.Split(Script, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline)
                                                           .Where(l => l.Trim().Length > 0))
        {
            await using var cmd = new SqlCommand(lot, cn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private const string Script = """
        CREATE TABLE dbo.SERVICE (SRVC_CODE_DIRECTION varchar(4) NULL, SRVC_CODE_GREFFE varchar(4) NOT NULL, SRVC_ABREGE varchar(16) NOT NULL,
            SRVC_NOM varchar(70) NULL, SRVC_DISTRI_SPECIFIQUE bit NOT NULL, SRVC_DISTRIBUTION_AUTO bit NOT NULL,
            SRVC_HEURE_DISTRIBUTION datetime NULL, SRVC_ID_SRVC int NOT NULL PRIMARY KEY);
        CREATE TABLE dbo.UTILISATEUR (UTLST_ID_SRVC int NULL, UTLST_CODE_GREFFE varchar(4) NULL, UTLST_ABREGE varchar(16) NOT NULL,
            UTLST_CIVILITE varchar(7) NOT NULL, UTLST_LOGIN varchar(35) NOT NULL, UTLST_PASSWORD varchar(20) NULL, UTLST_NOM varchar(35) NOT NULL,
            UTLST_PRENOM varchar(35) NOT NULL, UTLST_DESACTIVE bit NOT NULL, UTLST_ID_UTLST int NOT NULL PRIMARY KEY, UTLST_DATE_CREATION datetime NULL,
            UTLST_ISAMITEL bit NOT NULL, UTLST_ISADMIN bit NOT NULL, UTLST_IS_HOTLINE bit NOT NULL);
        CREATE TABLE dbo.HABILIT_ROLE_USER (HROLE_NOM_ROLE varchar(40) NOT NULL PRIMARY KEY, HROLE_LIBELLE varchar(80) NOT NULL,
            HROLE_ONLYAMITEL bit NOT NULL, HROLE_ONLYADMIN bit NOT NULL);
        CREATE TABLE dbo.PROCESSUS_ET_FONCTIONALITE (PROCF_CODE_PROC varchar(40) NOT NULL PRIMARY KEY, PROCF_TYPE varchar(40) NOT NULL,
            PROCF_LIBELLE varchar(110) NOT NULL, PROCF_ONLYAMITEL bit NOT NULL, PROCF_ONLYADMIN bit NOT NULL, PROCF_DOMAINE int NULL);
        CREATE TABLE dbo.HABILIT_ROLE_USER_PROCESSUS (HRFON_ID_HRFON int IDENTITY(1,1) PRIMARY KEY, HRFON_NOM_ROLE varchar(40) NOT NULL, HRFON_CODE_PROC varchar(40) NOT NULL);
        CREATE TABLE dbo.HABILIT_UTILISATEUR_ROLE_USER (HUROL_ID_HUROL int IDENTITY(1,1) PRIMARY KEY, HUROL_ID_UTLST int NOT NULL, HUROL_NOM_ROLE varchar(40) NOT NULL);
        CREATE TABLE dbo.CATEGORIE (CTGR_ID_CTGR int NOT NULL PRIMARY KEY, CTGR_ID_CTGR_DOMAINE int NULL, CTGR_ID_CTGR_ACTIVITE int NULL,
            CTGR_REFERENCE varchar(10) NOT NULL, CTGR_NIVEAU tinyint NOT NULL);
        CREATE TABLE dbo.UTILISATEUR_A_POUR_ROLE (X_UTARL_ID_UTROL int NOT NULL, X_UTARL_ID_UTLST int NOT NULL, PRIMARY KEY (X_UTARL_ID_UTROL, X_UTARL_ID_UTLST));
        CREATE TABLE dbo.COMPOSER_HABILITATION (X_CPSHB_DEGRE tinyint NOT NULL, X_CPSHB_REFERENCE_CTGR varchar(10) NOT NULL, X_CPSHB_ID_UTROL int NOT NULL,
            PRIMARY KEY (X_CPSHB_ID_UTROL, X_CPSHB_REFERENCE_CTGR));
        CREATE TABLE dbo.EXPRESSO_MENU (EXPME_ID_EXPME int NOT NULL PRIMARY KEY, EXPME_CLE varchar(50) NULL);
        CREATE TABLE dbo.EXPRESSO_DROITS (EXPDR_ID_EXPME int NOT NULL, EXPDR_ID_UTLST int NOT NULL, PRIMARY KEY (EXPDR_ID_EXPME, EXPDR_ID_UTLST));
        GO
        INSERT INTO dbo.SERVICE VALUES
            ('9999', '9999', 'RCS', 'Registre du Commerce et des sociétés', 1, 0, '1899-12-30T10:00:00', 5),
            ('9999', '9999', 'JUD', 'Service Judiciaire', 0, 0, NULL, 7);
        INSERT INTO dbo.UTILISATEUR VALUES
            (5, '3801', 'rresp',   'Mme', 'rresp',   'secretV2', 'Responsable', 'Rose',  0, 101, '2020-01-02', 0, 0, 0),
            (5, '9999', 'aamitel', 'M.',  'aamitel', NULL,       'Amitel',      'Alain', 0, 102, '2020-01-02', 1, 0, 0),
            (5, '9999', 'aadmin',  'Mme', 'aadmin',  NULL,       'Admin',       'Anne',  0, 103, '2020-01-02', 1, 1, 0),
            (7, '3801', 'jagent',  'M.',  'jagent',  NULL,       'Agent',       'Jules', 0, 104, '2020-01-02', 0, 0, 0),
            (5, '3801', 'pancien', 'M.',  'pancien', NULL,       'Ancien',      'Paul',  0, 105, NULL,         0, 0, 0),
            (5, '3801', 'qancien', 'M.',  'qancien', NULL,       'Ancien',      'Quentin', 0, 106, NULL,       0, 0, 0),
            (5, '3801', 'ccompta', 'Mme', 'ccompta', NULL,       'Compta',      'Claire', 0, 107, NULL,        0, 0, 0),
            (7, '3801', 'ddepart', 'M.',  'ddepart', NULL,       'Depart',      'Denis', 1, 108, NULL,         0, 0, 0);
        INSERT INTO dbo.HABILIT_ROLE_USER VALUES
            ('GestionDesHabilitations', 'Gestion des habilitations', 0, 0),
            ('GREFFIER', 'GREFFIER', 0, 0),
            ('RCSNIV01', 'Role RCS niveau  1', 0, 0),
            ('FACT : STANDARD', 'FACT : STANDARD', 0, 0),
            ('ADMINAMITEL', 'Administration par Amitel', 1, 0),
            ('ADMINRIG', 'Reserve aux administrateurs', 0, 1);
        INSERT INTO dbo.PROCESSUS_ET_FONCTIONALITE (PROCF_CODE_PROC, PROCF_TYPE, PROCF_LIBELLE, PROCF_ONLYAMITEL, PROCF_ONLYADMIN, PROCF_DOMAINE) VALUES
            ('RCS_CONSULT',   'DLL',     'Consultation RCS', 0, 0, 2),
            ('FACT',          'DLL',     'Facturation', 0, 0, 16),
            ('ENCUPDDEL',     'FONCVB6', 'ENCUPDDEL', 0, 0, 16),
            ('ACTJUD',        'FONCVB6', 'ACTJUD', 0, 0, 4),
            ('PRCJUD',        'OLD',     'PRCJUD', 0, 0, 4),
            ('GESTDROITSEXP', 'EXE',     'Gestion des droits Expresso', 0, 0, 16),
            ('FACT_EXP',      'EXE',     'Factures Expresso', 0, 0, 16),
            ('MENUVB6',       'FONCVB6', 'Menu de comptabilite sur une fonction VB6', 0, 0, 16);
        INSERT INTO dbo.HABILIT_ROLE_USER_PROCESSUS (HRFON_NOM_ROLE, HRFON_CODE_PROC) VALUES
            ('RCSNIV01', 'RCS_CONSULT'), ('GREFFIER', 'RCS_CONSULT'), ('FACT : STANDARD', 'FACT');
        INSERT INTO dbo.HABILIT_UTILISATEUR_ROLE_USER (HUROL_ID_UTLST, HUROL_NOM_ROLE) VALUES
            (101, 'GestionDesHabilitations'), (101, 'GREFFIER'), (102, 'GestionDesHabilitations'), (102, 'ADMINAMITEL'),
            (103, 'GestionDesHabilitations'), (104, 'RCSNIV01'), (105, 'RCSNIV01'), (106, 'RCSNIV01'), (107, 'RCSNIV01'), (108, 'RCSNIV01');
        -- Ancien systeme : un domaine (niveau 1), son activite (niveau 2), un processus (niveau 4), une fonction de niveau 1.
        INSERT INTO dbo.CATEGORIE (CTGR_ID_CTGR, CTGR_REFERENCE, CTGR_NIVEAU, CTGR_ID_CTGR_DOMAINE, CTGR_ID_CTGR_ACTIVITE) VALUES
            (1, 'ENCUPDDEL', 1, NULL, NULL), (10, 'DOMJUD', 1, NULL, NULL), (11, 'ACTJUD', 2, 10, NULL), (12, 'PRCJUD', 4, 10, 11);
        INSERT INTO dbo.UTILISATEUR_A_POUR_ROLE VALUES (1, 105), (2, 106);
        INSERT INTO dbo.COMPOSER_HABILITATION (X_CPSHB_ID_UTROL, X_CPSHB_REFERENCE_CTGR, X_CPSHB_DEGRE) VALUES
            (1, 'ENCUPDDEL', 3), (1, 'DOMJUD', 3), (1, 'PRCJUD', 3),
            (2, 'ENCUPDDEL', 2), (2, 'DOMJUD', 2), (2, 'PRCJUD', 3);
        -- Comptabilite : un menu au nom de la fonction, un ANCIEN nom (DROITS -> GESTDROITSEXP), un menu sur une fonction VB6.
        INSERT INTO dbo.EXPRESSO_MENU VALUES (1, 'FACT_EXP'), (2, 'DROITS'), (3, 'MENUVB6');
        INSERT INTO dbo.EXPRESSO_DROITS VALUES (1, 107), (2, 107), (3, 107);
        """;
}
