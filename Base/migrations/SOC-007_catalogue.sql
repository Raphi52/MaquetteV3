-- SOC-007 : le catalogue commun des greffes (carnet\socle.json, SOC-007, lot 4).
--
-- Une fiche par greffe exploite : ou sont sa base V2 et sa base V3 (sur la MEME
-- instance, decision Q4 : une seule colonne Serveur pour les deux), s'il est beta,
-- s'il est outre-mer, son fuseau horaire, et sa date et heure de passage, en heure
-- LOCALE du greffe. La partie serveur ne trouve la base d'un greffe que la (CA04,
-- Serveur\Socle\Greffes\CatalogueGreffes.cs).
--
-- Source V2 : COMMUN_RIG.dbo.GREFFE ; tailles reprises de la classe V2
-- D:\RigApplication\Source\Outils\DLL\RigTools\SQL\COMMON\GREFFE.cs (GRF_CODE_GREFFE
-- varchar(20), GRF_NOM varchar(80)). Les noms de base et de serveur sont elargis a
-- 128 (taille d'un nom SQL Server) : la V2 les tient en varchar(40).
--
-- Le catalogue est rempli dans la base COMMUNE que le serveur ouvre
-- (ConnectionStrings:RigV3) ; la table existe aussi, vide, dans chaque base de greffe :
-- toutes les bases V3 ont le meme schema (SOC-008 CA02).
CREATE TABLE soc.Greffe (
    CodeGreffe      varchar(20)  NOT NULL CONSTRAINT PK_Greffe PRIMARY KEY,
    Nom             varchar(80)  NOT NULL,
    -- L'instance SQL Server des DEUX bases du greffe (SQL-PROD\PROD, RIGBD-ANTILLES...).
    Serveur         varchar(128) NOT NULL,
    BaseV2          varchar(128) NOT NULL,
    -- Nom deduit de la base V2 (RIG_AMIENS -> RIGV3_AMIENS) des la reprise ;
    -- BaseV3CreeeLe dit si elle existe deja (SOC-008).
    BaseV3          varchar(128) NOT NULL,
    BaseV3CreeeLe   datetime2(0) NULL,
    -- V2 : GRF_IS_GREFFE_BETA bit NULL ; vide = non beta (CA02, RigGreffe.cs:72-76).
    EstBeta         bit          NOT NULL,
    -- V2 : GRF_IS_DROM (Papeete compris : vague de livraison, fuseau, serveur local).
    EstOutreMer     bit          NOT NULL,
    -- Nom Windows du fuseau, celui de sys.time_zone_info (AT TIME ZONE) et de
    -- TimeZoneInfo : « SA Western Standard Time » = UTC-4 (Antilles).
    Fuseau          varchar(64)  NULL,
    -- Date et heure de passage a la V3, en heure LOCALE du greffe (CA05, SOC-014).
    PassageLocal    datetime2(0) NULL,
    -- Derniere modification (CA06) ; chaque modification est aussi gardee dans soc.GreffeHistorique.
    ModifiePar      varchar(128) NOT NULL,
    ModifieLe       datetime2(0) NOT NULL,
    CONSTRAINT UQ_Greffe_BaseV2 UNIQUE (Serveur, BaseV2),
    CONSTRAINT UQ_Greffe_BaseV3 UNIQUE (Serveur, BaseV3),
    -- Une heure locale sans fuseau ne fonde rien : pas de passage sans fuseau.
    CONSTRAINT CK_Greffe_PassageAvecFuseau CHECK (PassageLocal IS NULL OR Fuseau IS NOT NULL)
);
GO
-- L'historique des fiches (CA06) : une ligne par creation ou modification, avec
-- l'etat de la fiche APRES le changement, son auteur et sa date.
CREATE TABLE soc.GreffeHistorique (
    IdHistorique    int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_GreffeHistorique PRIMARY KEY,
    Operation       char(1)      NOT NULL CONSTRAINT CK_GreffeHistorique_Operation CHECK (Operation IN ('C', 'M')),
    CodeGreffe      varchar(20)  NOT NULL,
    Nom             varchar(80)  NOT NULL,
    Serveur         varchar(128) NOT NULL,
    BaseV2          varchar(128) NOT NULL,
    BaseV3          varchar(128) NOT NULL,
    BaseV3CreeeLe   datetime2(0) NULL,
    EstBeta         bit          NOT NULL,
    EstOutreMer     bit          NOT NULL,
    Fuseau          varchar(64)  NULL,
    PassageLocal    datetime2(0) NULL,
    ModifiePar      varchar(128) NOT NULL,
    ModifieLe       datetime2(0) NOT NULL
);
GO
CREATE INDEX IX_GreffeHistorique_Code ON soc.GreffeHistorique (CodeGreffe, IdHistorique);
GO
-- Les regles que rien ne doit contourner, pas meme une requete SQL ecrite a la main :
--  * une modification pose son auteur et sa date (CA06) ;
--  * un fuseau est un nom que SQL Server connait ;
--  * une fois le passage fait (heure locale du greffe atteinte), sa date et son fuseau
--    ne changent plus (CA05) ;
--  * un greffe ne sort pas du catalogue : son historique resterait sans fiche.
CREATE TRIGGER soc.TR_Greffe_Regles ON soc.Greffe AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted)
    BEGIN
        IF EXISTS (SELECT 1 FROM deleted)
            THROW 51704, N'SOC-007 : un greffe ne se retire pas du catalogue (son historique resterait sans fiche).', 1;
        RETURN;
    END;
    IF EXISTS (SELECT 1 FROM deleted)
    BEGIN
        IF UPDATE(CodeGreffe)
            THROW 51700, N'SOC-007 : le code d''un greffe ne change pas.', 1;
        IF NOT UPDATE(ModifiePar) OR NOT UPDATE(ModifieLe)
            THROW 51701, N'SOC-007 CA06 : une modification du catalogue pose son auteur (ModifiePar) et sa date (ModifieLe).', 1;
    END;
    IF EXISTS (SELECT 1 FROM inserted i
               WHERE i.Fuseau IS NOT NULL
                 AND NOT EXISTS (SELECT 1 FROM sys.time_zone_info z WHERE z.name = i.Fuseau))
        THROW 51702, N'SOC-007 : fuseau inconnu de SQL Server (sys.time_zone_info), ex. « SA Western Standard Time ».', 1;
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN deleted d ON d.CodeGreffe = i.CodeGreffe
               WHERE d.PassageLocal IS NOT NULL
                 AND d.PassageLocal <= CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE d.Fuseau AS datetime2(0))
                 AND (i.PassageLocal IS NULL OR i.PassageLocal <> d.PassageLocal
                      OR i.Fuseau IS NULL OR i.Fuseau <> d.Fuseau))
        THROW 51703, N'SOC-007 CA05 : le passage de ce greffe est fait ; sa date et son fuseau ne changent plus.', 1;

    INSERT INTO soc.GreffeHistorique (Operation, CodeGreffe, Nom, Serveur, BaseV2, BaseV3, BaseV3CreeeLe,
                                      EstBeta, EstOutreMer, Fuseau, PassageLocal, ModifiePar, ModifieLe)
    SELECT CASE WHEN EXISTS (SELECT 1 FROM deleted) THEN 'M' ELSE 'C' END,
           i.CodeGreffe, i.Nom, i.Serveur, i.BaseV2, i.BaseV3, i.BaseV3CreeeLe,
           i.EstBeta, i.EstOutreMer, i.Fuseau, i.PassageLocal, i.ModifiePar, i.ModifieLe
    FROM inserted i;
END;
