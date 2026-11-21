-- apres: REC-002, REC-005_reprise
-- REC-005 CA01 a CA03 et CA05 sur la recopie CONTINUE (carnet\recopie.json ; decision du chef du 21/11 a 11:50 : le lot 9
-- les porte sur SA file), lot 9 du plan de reconstruction, phase 2 (2026-11-21). REC-002_recopie.sql est deja jouee :
-- elle ne se modifie plus, ce qui change s'ecrit ici.
--
--   Un changement REFUSE par la base d'en face (contrainte, declencheur) n'arrete plus la recopie du greffe : il compte un
--   essai, les autres fiches passent ; au 3e essai (seuil propose), il est MIS DE COTE avec son message ; les changements
--   suivants de la MEME fiche attendent derriere lui. L'exploitant corrige la cause et le RELANCE : il repasse, puis ceux
--   qui attendaient, dans l'ordre. Une PANNE de base (hors ligne, injoignable) ne compte aucun essai : le passage
--   s'arrete, tout attend le suivant (REC-002 CA06). Le moteur : Serveur\Recopie\Applicateur.cs et Remontee.cs.
--
--   rec.FileRecopie        V3 -> V2 : l'etat « mise de cote » et sa trace (quand, relance par qui et quand).
--   rec.RemonteeEchec      V2 -> V3 : une fiche de la V2 que la V3 refuse. Le point de reprise de la remontee avance
--                          quand meme (les autres fiches ne l'attendent pas) : la fiche est rejouee d'ici, de son etat
--                          COURANT dans la V2, tant qu'elle est « en echec » ; « mise de cote » au 3e essai ; « resolue »
--                          quand elle est enfin passee (la ligne reste, trace de l'incident).
--   rec.PassageRecopie     les essais refuses et les mises de cote du passage, et la table ou il s'est arrete : l'alerte
--                          d'un incident de plus de 5 minutes nomme la base, la table et l'erreur (CA05,
--                          Serveur\Recopie\AlertesRecopie.cs, regle « recopie-incident » deja dans la liste fermee).
--
-- Mode d'emploi et fiche d'incident : docs\reconstruction\recopie.md, section « Un changement refuse : mis de cote, relance ».

ALTER TABLE rec.FileRecopie DROP CONSTRAINT CK_FileRecopie_Etat;
ALTER TABLE rec.FileRecopie ADD CONSTRAINT CK_FileRecopie_Etat CHECK (Etat IN ('en attente', 'appliquee', 'mise de cote'));
ALTER TABLE rec.FileRecopie ADD
    -- Le 3e essai refuse : la ligne sort de la file, son message reste dans Erreur.
    MiseDeCoteLe datetime2(3)  NULL,
    -- La relance de l'exploitant (Etat -> « en attente », Essais -> 0) : la trace reste sur la ligne une fois appliquee.
    RelanceLe    datetime2(3)  NULL,
    RelancePar   nvarchar(128) NULL;
GO
CREATE INDEX IX_FileRecopie_MisesDeCote ON rec.FileRecopie (TableV3, IdV3) INCLUDE (MiseDeCoteLe, Erreur) WHERE Etat = 'mise de cote';

CREATE TABLE rec.RemonteeEchec (
    BaseV2         sysname        NOT NULL,
    TableV2        sysname        NOT NULL,
    -- La cle primaire de la fiche dans la V2, en texte (meme ecriture que rec.Correspondance.IdV2).
    IdV2           nvarchar(200)  NOT NULL,
    -- La version du suivi des changements de la V2 du premier changement refuse : l'ordre des reprises.
    Version        bigint         NOT NULL,
    Etat           varchar(12)    NOT NULL CONSTRAINT CK_RemonteeEchec_Etat CHECK (Etat IN ('en echec', 'mise de cote', 'resolue')),
    Essais         int            NOT NULL,
    Erreur         nvarchar(2000) NULL,
    PremierEchecLe datetime2(3)   NOT NULL CONSTRAINT DF_RemonteeEchec_Premier DEFAULT (SYSDATETIME()),
    DernierEssaiLe datetime2(3)   NOT NULL CONSTRAINT DF_RemonteeEchec_Dernier DEFAULT (SYSDATETIME()),
    MiseDeCoteLe   datetime2(3)   NULL,
    RelanceLe      datetime2(3)   NULL,
    RelancePar     nvarchar(128)  NULL,
    ResolueLe      datetime2(3)   NULL,
    CONSTRAINT PK_RemonteeEchec PRIMARY KEY (BaseV2, TableV2, IdV2)
);

ALTER TABLE rec.PassageRecopie ADD
    -- Changements refuses par la base d'en face pendant ce passage (un essai de plus chacun), et ceux mis de cote.
    Refusees     int     NOT NULL CONSTRAINT DF_PassageRecopie_Refusees DEFAULT (0),
    MisesDeCote  int     NOT NULL CONSTRAINT DF_PassageRecopie_MisesDeCote DEFAULT (0),
    -- La table V2 de la fiche sur laquelle le passage s'est arrete (panne), ou NULL.
    TableArret   sysname NULL;
