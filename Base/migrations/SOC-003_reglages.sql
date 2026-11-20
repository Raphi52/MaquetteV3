-- SOC-003 : les reglages du greffe, repris a l'identique (carnet\socle.json, SOC-003, lot 4).
-- apres: SOC-007
--
-- Quatre familles, chacune copiee de la V2 telle quelle (decision Q2) :
--  * soc.Reglage         <- dbo.PARAMETRAGE du greffe (une ligne par reglage) ;
--  * soc.ParametreGreffe <- dbo.GREFFE_PARAMETRE du greffe (une ligne de 61 colonnes) ;
--  * soc.ModeleCourriel  <- dbo.MAIL_TEMPLATE du greffe (l'habillage des courriels) ;
--  * soc.GreffeInfoV2    <- la ligne du greffe dans COMMUN_RIG.dbo.GREFFE (« informations
--                           du greffe tenues en commun »), a cote de sa fiche du catalogue.
-- Lecture et ecriture : Serveur\Socle\Greffes (LectureReglage.cs, RepriseReglages.cs).

-- PARAMETRAGE (D:\RigApplication\Source\BaseDeDonnées\RigDatabase\dbo\Tables\PARAMETRAGE.sql:1-12,
-- colonnes relues dans RIG_DEV le 2026-11-20) : memes types et memes tailles, la valeur reste
-- du TEXTE (« 1 », « OUI », « Vrai »... : chaque fonction le lit a sa facon, CA02).
CREATE TABLE soc.Reglage (
    Code            varchar(100)  NOT NULL CONSTRAINT PK_Reglage PRIMARY KEY,
    Libelle         varchar(1000) NOT NULL,
    Valeur          varchar(1000) NULL,
    Utilisation     varchar(500)  NULL,
    AuteurCreation  varchar(50)   NULL,
    DateCreation    datetime      NULL,
    AuteurModif     varchar(50)   NULL,
    DateModif       datetime      NULL,
    -- PARAM_ID_PARAM de la V2 (trace de la reprise) ; NULL pour un reglage ne en V3.
    IdV2            int           NULL
);
GO
-- GREFFE_PARAMETRE (GREFFE_PARAMETRE.sql:1-60 ; 61 colonnes relues dans RIG_DEV) : une
-- ligne par COLONNE de la V2, sa valeur gardee avec son type (sql_variant : money reste
-- un nombre, bit un booleen) et le type V2 en clair. Une base de greffe dont la table a
-- derive (colonne en plus ou en moins) se reprend telle qu'elle est.
CREATE TABLE soc.ParametreGreffe (
    Colonne         varchar(128)  NOT NULL CONSTRAINT PK_ParametreGreffe PRIMARY KEY,
    Valeur          sql_variant   NULL,
    TypeV2          varchar(40)   NOT NULL,
    AuteurModif     varchar(50)   NULL,
    DateModif       datetime      NULL
);
GO
-- MAIL_TEMPLATE (MAIL_TEMPLATE.sql:1-9 ; MAILT_CONTENU est nvarchar(max) dans RIG_DEV,
-- varchar(max) dans le projet versionne : on garde le plus large).
CREATE TABLE soc.ModeleCourriel (
    Code            varchar(40)   NOT NULL CONSTRAINT PK_ModeleCourriel PRIMARY KEY,
    Contenu         nvarchar(max) NULL,
    DateModif       datetime      NULL,
    AuteurModif     varchar(50)   NULL,
    IdV2            int           NULL
);
GO
-- COMMUN_RIG.dbo.GREFFE, la ligne du greffe (54 colonnes dans la classe V2 GREFFE.cs,
-- 55 relevees en base le 2026-11-20) : une ligne par colonne. Valeur garde le type ;
-- Texte porte les colonnes varchar(max) (GRF_PARAM_TEST), qu'un sql_variant ne tient pas.
-- Mots de passe compris, tels que la V2 les garde (chiffres) : aucune route ne les sert.
CREATE TABLE soc.GreffeInfoV2 (
    CodeGreffe      varchar(20)   NOT NULL CONSTRAINT FK_GreffeInfoV2_Greffe REFERENCES soc.Greffe (CodeGreffe),
    Colonne         varchar(128)  NOT NULL,
    Valeur          sql_variant   NULL,
    Texte           nvarchar(max) NULL,
    TypeV2          varchar(40)   NOT NULL,
    ReprisLe        datetime2(0)  NOT NULL,
    CONSTRAINT PK_GreffeInfoV2 PRIMARY KEY (CodeGreffe, Colonne)
);
GO
-- CA07 : un reglage modifie dit qui l'a modifie et quand, comme la V2
-- (Parametrage.cs:103-121 pose PARAM_AUTEUR_MODIF et PARAM_DATE_MODIF). Tenu ici pour
-- qu'aucune requete ecrite a la main ne change une valeur sans auteur.
CREATE TRIGGER soc.TR_Reglage_Auteur ON soc.Reglage AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(Valeur) AND (NOT UPDATE(AuteurModif) OR NOT UPDATE(DateModif))
        THROW 51731, N'SOC-003 CA07 : modifier un reglage pose son auteur (AuteurModif) et sa date (DateModif).', 1;
END;
