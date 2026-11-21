-- SOC-004 : creer, modifier et desactiver le compte d'un agent (carnet\socle.json, SOC-004, tache T01).
-- apres: SOC-001
--
-- Complete soc.Service et soc.Compte (SOC-001_comptes.sql) de ce que la creation et la reprise des
-- comptes demandent :
--  - la distribution des services (V2 : SERVICE.sql:1-11), reprise a l'identique (CA08) ;
--  - la date de creation d'un compte (V2 : UTILISATEUR.UTLST_DATE_CREATION, UTILISATEUR.sql:24).
-- L'unicite du compte Windows sans tenir compte des majuscules (CA02) est deja tenue par
-- UQ_Compte_CompteWindows, colonne en collation CI.
-- Un compte qui a agi ne se supprime pas physiquement (CA04) : les journaux (SOC-005, SOC-016) le
-- citent comme auteur par cle etrangere, la base refuse donc sa suppression.

ALTER TABLE soc.Service ADD
    CodeDirection           varchar(4)  NULL,   -- SRVC_CODE_DIRECTION
    DistributionSpecifique  bit         NOT NULL CONSTRAINT DF_Service_DistributionSpecifique DEFAULT (0),  -- SRVC_DISTRI_SPECIFIQUE
    DistributionAuto        bit         NOT NULL CONSTRAINT DF_Service_DistributionAuto DEFAULT (1),        -- SRVC_DISTRIBUTION_AUTO
    HeureDistribution       datetime    NULL;   -- SRVC_HEURE_DISTRIBUTION (une heure posee sur le 30/12/1899, comme la V2)

ALTER TABLE soc.Compte ADD
    DateCreation  datetime  NULL;   -- UTLST_DATE_CREATION
