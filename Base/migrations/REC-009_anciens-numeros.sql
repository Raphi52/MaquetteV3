-- apres: REC-001, REC-012
-- REC-009 : garder les donnees reprises d'anciens logiciels (Dedale, Alinea, MyGreffe, Replay, GAGI, Oracle) et leurs anciens
-- numeros (carnet\recopie.json), lot 11 du plan de reconstruction, phase 2 (2026-11-21). Le code :
-- Serveur\Recopie\AnciensNumeros.cs ; mode d'emploi : docs\reconstruction\recopie.md, section « Les anciens logiciels (REC-009) ».
--
--   rec.AncienNumero          T01 : chaque ligne des trois tables de correspondance de la V2 (CORRESP_MIGRATION_RIG,
--                             CORRESP_ORACLE_RIG, CORRESP_GAGI_RIG), telle quelle, indexee sur l'ancien numero et sur la
--                             fiche RIG ; la fiche de la V3 se lit par rec.Correspondance (TableRig, IdRig)
--   rec.FicheAncienneReprise  CA03 : les fiches qu'une ancienne reprise a creees pour combler un lien casse (client
--                             « Fake data », client cree automatiquement par la migration, adresse inconnue), transferees
--                             telles quelles et listees pour le greffe
--
-- Les tables de reprise base a base (MIGRATION_REFERENCE, MIGRATION_TABLE, MIGRATION_CHAMP, MIGRATION_ID) et la
-- correspondance des articles Replay (ARTICLE_CORRESP_REPLAY) sont transferees telles quelles (v2.*) et mises en lecture
-- seule par le transfert (CA04, CA05) : elles ont une cle primaire, les trois tables ci-dessus n'en ont pas toutes
-- (CORRESP_ORACLE_RIG, CORRESP_GAGI_RIG : leur identite seule) -- d'ou leur reprise ligne pour ligne dans rec.AncienNumero.

CREATE TABLE rec.AncienNumero (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AncienNumero PRIMARY KEY,
    -- L'ancien logiciel, par la table qui le tenait : « migration » (CORRESP_MIGRATION_RIG : Dedale, Alinea, MyGreffe,
    -- Replay...), « Oracle » (CORRESP_ORACLE_RIG), « GAGI » (CORRESP_GAGI_RIG).
    Logiciel          varchar(10)   NOT NULL CONSTRAINT CK_AncienNumero_Logiciel CHECK (Logiciel IN ('migration', 'Oracle', 'GAGI')),
    TableV2           sysname       NOT NULL,     -- la table de correspondance de la V2
    IdV2              nvarchar(20)  NOT NULL,     -- sa ligne (MIGRI_ID_MIGRI, ORARI_ID_ORARI, GAGRI_ID_GAGRI)
    CodeGreffe        varchar(4)    NOT NULL,     -- *_CODE_GREFFE
    TableRig          varchar(100)  NOT NULL,     -- *_TABLE : DOSSIER_RCS, INSCRIPTION, PROCEDURE_COLLECTIVE, MESURE, INSTANCE...
    AncienId          varchar(100)  NULL,         -- MIGRI_NO_MIG, ORARI_NO_ORACLE, GAGRI_ID_GAGI
    AncienNumero      varchar(100)  NULL,         -- MIGRI_NUMERO_MIG, ORARI_NUMERO_ORACLE, GAGRI_NUMERO_GAGI
    IdRig             int           NULL,         -- *_ID_RIG : l'identifiant de la fiche dans la base V2
    NumeroRig         varchar(100)  NULL,         -- *_NUMERO_RIG
    NumeroInfogreffe  varchar(100)  NULL,         -- *_NUMERO_INFOGREFFE
    CONSTRAINT UQ_AncienNumero_Ligne UNIQUE (TableV2, IdV2)
);
CREATE INDEX IX_AncienNumero_Ancien ON rec.AncienNumero (AncienNumero) INCLUDE (Logiciel, TableRig, IdRig, NumeroRig, NumeroInfogreffe);
CREATE INDEX IX_AncienNumero_AncienId ON rec.AncienNumero (AncienId) INCLUDE (Logiciel, TableRig, IdRig, NumeroRig, NumeroInfogreffe);
CREATE INDEX IX_AncienNumero_Fiche ON rec.AncienNumero (TableRig, IdRig);
CREATE INDEX IX_AncienNumero_NumeroRig ON rec.AncienNumero (NumeroRig) INCLUDE (Logiciel, TableRig, IdRig, AncienNumero, NumeroInfogreffe);

CREATE TABLE rec.FicheAncienneReprise (
    -- La fiche de la V2 ; sa fiche dans la V3 se lit par rec.Correspondance au moment de la lire (les adresses inconnues sont
    -- fusionnees en une seule apres les tables, REC-016 CA03 : une fiche V3 notee ici serait perimee).
    TableV2     sysname        NOT NULL,     -- CLIENT, ADRESSE_BASE
    IdV2        nvarchar(200)  NOT NULL,
    Motif       nvarchar(200)  NOT NULL,     -- « client Fake data d'une ancienne reprise »...
    Libelle     nvarchar(400)  NULL,         -- ce que l'agent lit : le nom du client, la voie de l'adresse
    IdTransfert int            NOT NULL,
    CONSTRAINT PK_FicheAncienneReprise PRIMARY KEY (TableV2, IdV2)
);
