-- SOC-006 : les courriels du greffe, leurs modeles et la trace de chaque envoi
-- (carnet\socle.json, SOC-006 T01 ; lot 1 du plan « Reconstruction de RIG -- phase 2 »).
-- apres: SOC-003
--
-- Cinq tables, copiees de la V2 colonne pour colonne (memes types, memes tailles), lues et
-- ecrites par Serveur\Socle\Courriels :
--  * soc.ModeleMessage      <- dbo.EMAIL_MODEL : le « modele de mail » de la V2 (objet, texte,
--                              expediteur...). A NE PAS confondre avec soc.ModeleCourriel
--                              (SOC-003, dbo.MAIL_TEMPLATE) : l'HABILLAGE graphique par-dessus
--                              ce texte (envoi-mail-rig-send-email.md, « Faux ami »).
--  * soc.ListeDiffusion     <- dbo.LISTE_DISTRIBUTION_MAIL ;
--  * soc.ModeleMessageListe <- dbo.EMAIL_MODEL_LISTE_DISTRIB_DEFAUT (les listes par defaut d'un modele) ;
--  * soc.CourrielEnvoye     <- dbo.EMAIL_ENVOYE : un courriel parti (ecrit apres l'envoi, SendEmail.cs:845-846) ;
--  * soc.TraceEnvoi         <- dbo.EMAIL_ENVOI_TRACE : chaque etape d'un envoi, son succes ou son erreur (CA03).

-- EMAIL_MODEL (EMAIL_MODEL.sql:1-19 ; RIG_DEV lu le 2026-11-21 : 18 colonnes, dont les deux de la
-- signature, absentes du projet versionne -- on garde la forme la plus large).
CREATE TABLE soc.ModeleMessage (
    Type                varchar(40)   NOT NULL CONSTRAINT PK_ModeleMessage PRIMARY KEY,   -- EMAILMOD_TYPE
    Sujet               varchar(200)  NOT NULL,                                            -- EMAILMOD_SUJET
    Corps               varchar(4000) NOT NULL,                                            -- EMAILMOD_CORPS
    CorpsHtml           bit           NOT NULL,                                            -- EMAILMOD_CORPS_HTML
    Expediteur          varchar(200)  NULL,                                                -- EMAILMOD_FROM
    Reponse             varchar(200)  NULL,                                                -- EMAILMOD_REPLY
    Copie               varchar(200)  NULL,                                                -- EMAILMOD_COPY_TO (copie cachee)
    Lot1                bit           NOT NULL CONSTRAINT DF_ModeleMessage_Lot1 DEFAULT (1),
    Lot2                bit           NOT NULL CONSTRAINT DF_ModeleMessage_Lot2 DEFAULT (1),
    ReserveAmitel       bit           NOT NULL CONSTRAINT DF_ModeleMessage_ReserveAmitel DEFAULT (0), -- EMAILMOD_ONLY_AMITEL
    LibelleEcran        varchar(100)  NULL,
    Inscription         bit           NULL CONSTRAINT DF_ModeleMessage_Inscription DEFAULT (0),
    UtilisableEnSaisie  bit           NULL CONSTRAINT DF_ModeleMessage_UtilisableEnSaisie DEFAULT (1),
    Comptabilite        bit           NULL CONSTRAINT DF_ModeleMessage_Comptabilite DEFAULT (0),     -- EMAILMOD_COMPTA
    DestinataireDefaut  varchar(200)  NULL,                                                -- EMAILMOD_DEST_DEFAUT
    EnvoiAutomatique    bit           NOT NULL CONSTRAINT DF_ModeleMessage_EnvoiAutomatique DEFAULT (0), -- EMAILMOD_IS_ENVOI_AUTOMATIQUE
    SignatureActive     bit           NOT NULL CONSTRAINT DF_ModeleMessage_SignatureActive DEFAULT (0),  -- EMAILMOD_SIGNATURE_ACTIVE (RIG_DEV)
    Signature           varchar(2000) NULL                                                 -- EMAILMOD_SIGNATURE (RIG_DEV)
);
GO
-- LISTE_DISTRIBUTION_MAIL (LISTE_DISTRIBUTION_MAIL.sql). L'identifiant de la V2 est garde (reprise
-- avec KeepIdentity) : les liens par defaut des modeles le citent.
CREATE TABLE soc.ListeDiffusion (
    Id          int           IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ListeDiffusion PRIMARY KEY,  -- LSTDM_ID_LSTDM
    Code        varchar(35)   NOT NULL,
    Libelle     varchar(200)  NOT NULL,
    Adresses    varchar(7000) NOT NULL,                                                         -- LSTDM_ADR_MAILS
    Rcs         bit           NOT NULL,
    Judiciaire  bit           NOT NULL,
    Prive       varchar(200)  NULL,
    Actif       bit           NOT NULL
);
GO
-- EMAIL_MODEL_LISTE_DISTRIB_DEFAUT (EMAIL_MODEL_LISTE_DISTRIB_DEFAUT.sql) : memes cles etrangeres.
CREATE TABLE soc.ModeleMessageListe (
    Id          int           IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ModeleMessageListe PRIMARY KEY, -- EMLDD_ID_EMLDD
    TypeModele  varchar(40)   NOT NULL CONSTRAINT FK_ModeleMessageListe_Modele REFERENCES soc.ModeleMessage (Type),
    IdListe     int           NOT NULL CONSTRAINT FK_ModeleMessageListe_Liste REFERENCES soc.ListeDiffusion (Id)
);
GO
-- EMAIL_ENVOYE (EMAIL_ENVOYE.sql) : le courriel tel qu'il est parti (objet, texte du modele avant
-- habillage, adresses). Deux colonnes de la V3 : le numero de suivi de l'envoi (le lien avec sa
-- trace, que la V2 ne tenait que dans EMAIL_ENVOI_TRACE) et le type du modele.
CREATE TABLE soc.CourrielEnvoye (
    Id              int              IDENTITY(1, 1) NOT NULL CONSTRAINT PK_CourrielEnvoye PRIMARY KEY, -- EMAILENV_ID_EMAILENV
    Sujet           varchar(200)     NOT NULL,
    Corps           varchar(4000)    NOT NULL,
    CorpsHtml       bit              NOT NULL,
    Expediteur      varchar(200)     NULL,
    Reponse         varchar(200)     NULL,
    Copie           varchar(200)     NULL,
    Destinataires   varchar(2000)    NULL,                                                       -- EMAILENV_TO
    DateEnvoi       datetime         NULL,                                                       -- EMAILENV_DATE
    Suivi           uniqueidentifier NULL,
    TypeModele      varchar(40)      NULL
);
GO
CREATE INDEX IX_CourrielEnvoye_Suivi ON soc.CourrielEnvoye (Suivi);
GO
-- EMAIL_ENVOI_TRACE (RIG_Send_Email\SQL\EMAIL_ENVOI_TRACE.sql:3-30) : une ligne par etape d'un
-- envoi, reliees par le numero de suivi (EMTRC_CORRELATION_ID). Memes colonnes, en francais :
-- EMTRC_BCC -> Copie (la V2 y range EMAILENV_COPY_TO), EMTRC_STACKTRACE -> Pile,
-- EMTRC_COMMAND_LINE -> Origine (la V2 y mettait la ligne de commande de RIG_Send_Email ; le
-- serveur de la V3 y met ce qui a demande l'envoi), EMTRC_UTILISATEUR_WINDOWS -> le compte de
-- l'agent (la V2 y mettait celui du processus, qui EST l'agent sur son poste). CodeGreffe en 20,
-- comme soc.Greffe. Deux colonnes de la V3 : le type du modele et le fichier de la boite de
-- depot quand l'envoi n'a pas ete remis au serveur de messagerie (CA06, SMTP_IGNORE_DEV).
CREATE TABLE soc.TraceEnvoi (
    Id                  int              IDENTITY(1, 1) NOT NULL CONSTRAINT PK_TraceEnvoi PRIMARY KEY,
    DateCreation        datetime         NOT NULL CONSTRAINT DF_TraceEnvoi_Date DEFAULT (GETDATE()),
    CodeGreffe          varchar(20)      NULL,
    Suivi               uniqueidentifier NOT NULL,
    Application         varchar(80)      NOT NULL,
    Mode                varchar(20)      NULL,
    Etape               varchar(50)      NOT NULL,
    IdCourriel          int              NULL,                                                   -- EMTRC_ID_EMAILENV
    IdLigneEdition      int              NULL,
    IdsLignesEdition    varchar(1000)    NULL,
    Expediteur          varchar(300)     NULL,
    Destinataires       varchar(2000)    NULL,
    Copie               varchar(2000)    NULL,
    Reponse             varchar(300)     NULL,
    Sujet               varchar(300)     NULL,
    EstHtml             bit              NULL,
    NbPieces            int              NULL,
    Pieces              varchar(max)     NULL,
    Urls                varchar(max)     NULL,
    SmtpHote            varchar(200)     NULL,
    SmtpPort            int              NULL,
    Succes              bit              NULL,
    ErreurType          varchar(200)     NULL,
    ErreurMessage       varchar(max)     NULL,
    Pile                varchar(max)     NULL,
    Origine             varchar(max)     NULL,
    Machine             varchar(100)     NULL,
    UtilisateurWindows  varchar(100)     NULL,
    TypeModele          varchar(40)      NULL,
    FichierDepot        nvarchar(400)    NULL
);
GO
-- Les index de la V2 (memes cles), dont celui de la purge par anciennete (CA04).
CREATE INDEX IX_TraceEnvoi_Suivi ON soc.TraceEnvoi (Suivi, DateCreation);
GO
CREATE INDEX IX_TraceEnvoi_Courriel ON soc.TraceEnvoi (IdCourriel, DateCreation);
GO
CREATE INDEX IX_TraceEnvoi_LigneEdition ON soc.TraceEnvoi (IdLigneEdition, DateCreation);
GO
CREATE INDEX IX_TraceEnvoi_Date ON soc.TraceEnvoi (DateCreation);
