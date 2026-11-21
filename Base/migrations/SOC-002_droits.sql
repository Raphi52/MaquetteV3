-- SOC-002 : retrouver exactement ses droits le jour du passage (carnet\socle.json, SOC-002, tache T01).
-- apres: SOC-001
--
-- La V2 a TROIS origines de droits, consultees dans cet ordre par Utilisateur.ALaFonctionnalite
-- (D:\RigApplication\Source\Outils\DLL\RigMetier\Habilitation\Utilisateur.cs:298-347) :
--   1. les roles du compte (HABILIT_UTILISATEUR_ROLE_USER) et les fonctions de chaque role
--      (HABILIT_ROLE_USER_PROCESSUS) ;
--   2. pour une fonction de type FONCVB6 seulement : l'ancien systeme (UTILISATEUR_A_POUR_ROLE,
--      COMPOSER_HABILITATION, CATEGORIE), autorise au-dessus du degre 2 (RigOldHabilitation\Habilitation.cs:8-16) ;
--   3. pour toute autre fonction : les menus de comptabilite du compte (EXPRESSO_DROITS), y compris
--      sous six anciens noms de menu (Utilisateur.cs:327-341).
-- La V3 les reunit dans UN modele (CA02) : les roles (soc.Role, soc.CompteRole, deja la par SOC-001),
-- les fonctions de chaque role (soc.RoleFonction) et les droits DIRECTS d'un compte (soc.CompteFonction),
-- qui gardent leur origine V2 (« ancien » ou « comptabilite ») pour la comparaison avec la V2.
-- La traduction (degre, type de la fonction, anciens noms) est faite UNE fois, a la reprise
-- (Serveur\Socle\Comptes\RepriseDroits.cs) : la verification de la V3 n'est plus qu'une lecture.

-- Le catalogue des fonctions (V2 : PROCESSUS_ET_FONCTIONALITE). La V2 cherche une fonction sans tenir
-- compte des majuscules (ProcessusEtFonctionalite.cs:326-327, CurrentCultureIgnoreCase) : collation CI.
-- Une fonction inconnue n'y est JAMAIS ajoutee par une verification (CA05) : la V2 le faisait
-- (Utilisateur.cs:300-316), la V3 refuse sans rien ecrire.
CREATE TABLE soc.Fonction (
    Code                   varchar(40)   COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL CONSTRAINT PK_Fonction PRIMARY KEY,  -- PROCF_CODE_PROC
    Libelle                varchar(110)  NOT NULL,   -- PROCF_LIBELLE
    TypeV2                 varchar(40)   NOT NULL,   -- PROCF_TYPE (FONCVB6 : ancien systeme ; « V3 » : fonction neuve)
    -- Domaine Comptabilite de la V2 (PROCF_DOMAINE & 16, ProcessusEtFonctionalite.cs:42-43) : la fonction
    -- touche la facturation ou l'encaissement ; un changement de droits sur elle est un evenement 130 (SOC-005 CA02).
    Comptabilite           bit           NOT NULL CONSTRAINT DF_Fonction_Comptabilite DEFAULT (0),
    ReserveAmitel          bit           NOT NULL CONSTRAINT DF_Fonction_ReserveAmitel DEFAULT (0),          -- PROCF_ONLYAMITEL
    ReserveAdministrateur  bit           NOT NULL CONSTRAINT DF_Fonction_ReserveAdministrateur DEFAULT (0)   -- PROCF_ONLYADMIN
);

-- Les fonctions de chaque role (V2 : HABILIT_ROLE_USER_PROCESSUS).
CREATE TABLE soc.RoleFonction (
    Role      varchar(40)  NOT NULL CONSTRAINT FK_RoleFonction_Role REFERENCES soc.Role (Nom),
    Fonction  varchar(40)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL CONSTRAINT FK_RoleFonction_Fonction REFERENCES soc.Fonction (Code),
    CONSTRAINT PK_RoleFonction PRIMARY KEY (Role, Fonction)
);

-- Les droits DIRECTS d'un compte, traduits des deux autres origines de la V2.
--   ancien       : ancien systeme, degre final > 2, sur une fonction FONCVB6 ;
--   comptabilite : menu de comptabilite du compte (EXPRESSO_DROITS), sous le nom de la fonction ou un ancien nom.
CREATE TABLE soc.CompteFonction (
    IdCompte  int          NOT NULL CONSTRAINT FK_CompteFonction_Compte REFERENCES soc.Compte (Id),
    Fonction  varchar(40)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL CONSTRAINT FK_CompteFonction_Fonction REFERENCES soc.Fonction (Code),
    Origine   varchar(12)  NOT NULL CONSTRAINT CK_CompteFonction_Origine CHECK (Origine IN ('ancien', 'comptabilite')),
    Detail    varchar(80)  NULL,  -- d'ou vient le droit dans la V2 (degre et categorie, nom du menu)
    CONSTRAINT PK_CompteFonction PRIMARY KEY (IdCompte, Fonction, Origine)
);
GO

-- LE modele unique : un compte a une fonction s'il l'a par un de ses roles OU par un droit direct.
CREATE VIEW soc.Droit AS
    SELECT cr.IdCompte, rf.Fonction, CAST('role' AS varchar(12)) AS Origine, CAST(cr.Role AS varchar(80)) AS Detail
    FROM soc.CompteRole cr
    JOIN soc.RoleFonction rf ON rf.Role = cr.Role
    UNION ALL
    SELECT cf.IdCompte, cf.Fonction, cf.Origine, cf.Detail
    FROM soc.CompteFonction cf;
GO

-- Le droit de gerer les droits est un ROLE dans la V2 (Utilisateur.IsHabilit, Utilisateur.cs:364-366) :
-- la V3 en a besoin des sa creation (CA04), avant toute reprise ; la reprise met son libelle a jour.
INSERT INTO soc.Role (Nom, Libelle) VALUES ('GestionDesHabilitations', 'Gestion des habilitations');
