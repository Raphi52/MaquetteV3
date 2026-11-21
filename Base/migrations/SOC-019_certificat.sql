-- SOC-019 : le certificat de la V3, retrouve par chaque greffe dans la V3 avec sa date de validite (carnet\socle.json,
-- SOC-019, CA04, taches T03 et T04 ; lot 17 du plan « Reconstruction de RIG — phase 2 », 2026-11-21).
-- apres: SOC-016
--
-- La V2 n'a pas d'ecran du certificat : il circule en piece jointe, et un greffe qui doit le presenter a l'administration
-- fiscale le demande a l'assistance. La V3 garde ICI, dans la base COMMUNE, le certificat que l'organisme accredite a
-- delivre a Amitel (memes champs que certificat-v3.json du dossier de la beta, Outils\Passage\Beta.cs) ; la partie serveur
-- le rend a tout agent admis d'un greffe, avec ce qu'il vaut aujourd'hui pour la version en service
-- (Serveur\Socle\Certification\ModuleCertification.cs). Ecrit par cette route seule (exploitation d'Amitel), en ajout :
-- un certificat renouvele est une ligne de plus, l'historique reste. Aucune ligne : « certification en cours ».
-- La table existe vide dans chaque base de greffe (meme schema, SOC-008 CA02) ; seule celle de la base commune est lue.
CREATE TABLE soc.CertificatLogiciel (
    Id              int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_CertificatLogiciel PRIMARY KEY,
    Logiciel        varchar(10)    NOT NULL CONSTRAINT CK_CertificatLogiciel_Logiciel CHECK (Logiciel IN ('V3')),
    Organisme       nvarchar(200)  NOT NULL CONSTRAINT CK_CertificatLogiciel_Organisme CHECK (LEN(Organisme) > 0),
    Accreditation   nvarchar(100)  NOT NULL CONSTRAINT CK_CertificatLogiciel_Accreditation CHECK (LEN(Accreditation) > 0),
    Numero          nvarchar(100)  NOT NULL CONSTRAINT CK_CertificatLogiciel_Numero CHECK (LEN(Numero) > 0),
    Referentiel     nvarchar(100)  NOT NULL,
    VersionCouverte varchar(20)    NOT NULL,
    DelivreLe       date           NOT NULL,
    ValideJusquau   date           NOT NULL,
    EnregistreLe    datetime2(3)   NOT NULL CONSTRAINT DF_CertificatLogiciel_EnregistreLe DEFAULT (SYSUTCDATETIME()),   -- UTC
    EnregistrePar   nvarchar(80)   NOT NULL,
    CONSTRAINT CK_CertificatLogiciel_Dates CHECK (ValideJusquau >= DelivreLe)
);
CREATE INDEX IX_CertificatLogiciel_Logiciel ON soc.CertificatLogiciel (Logiciel, EnregistreLe DESC, Id DESC);
GO

-- Un certificat enregistre ne se modifie pas : on en enregistre un autre (le journal des gestes dit qui, quand).
CREATE TRIGGER soc.CertificatLogiciel_Inalterable ON soc.CertificatLogiciel AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51019, N'Un certificat enregistré ne se modifie pas : enregistrez le nouveau certificat (SOC-019).', 1;
END;
GO

-- L'enregistrement d'un certificat est un geste de l'exploitation d'Amitel : il s'ecrit au journal des gestes de la base
-- commune, avec son auteur (SOC-016). Famille a part de « certification » (les chaines signees des factures, SOC-018).
INSERT INTO soc.FamilleJournal (Famille, Libelle, Facturation, ConservationAns) VALUES
    ('certificat', 'Certificat NF525 du logiciel', 0, 6);
