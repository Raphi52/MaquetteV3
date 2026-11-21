-- apres: REC-001, REC-002, REC-007
-- REC-011 : lier une nouvelle demande a une demande d'une affaire ancienne (carnet\recopie.json), lot 11 du plan de
-- reconstruction, phase 2 (2026-11-21). Le code : Serveur\Recopie\GardeAffaires.cs (blocage pose et leve par le
-- declencheur de garde de v2.DEMANDE), Serveur\Recopie\LiensDemandes.cs ; mode d'emploi : docs\reconstruction\recopie.md,
-- section « Les affaires des deux logiciels (REC-010, REC-011, REC-018) ».
--
-- Dans la base V3, v2.DEMANDE tient les demandes des DEUX logiciels : les anciennes (transferees, puis tenues a jour par la
-- remontee) et celles de la V3. Une demande de la V3 vise donc une ancienne par ses colonnes de la V2 elles-memes
-- (DMND_ID_DMND_MERE, DMND_ID_DMND_INHIBITRICE : l'identifiant de sa ligne dans la V3, dont l'identifiant V2 est dans
-- rec.Correspondance, T01). L'inverse ne le peut pas : la ligne d'une ancienne demande est celle de la V2, la remontee la
-- reecrit ; le lien qu'une ancienne demande porte vers une demande de la V3 est garde ici, et ecrit dans la V2 au retour
-- (REC-007), quand la demande de la V3 y a une ligne (CA04).
--
--   rec.LienDemande   chaque lien entre une demande de la V3 et une ancienne demande :
--                     Porteuse = 'V3' : la demande de la V3 est bloquee par (ou nee de) l'ancienne -- trace du blocage
--                                       pose par la regle de la V2 (CA01) et de sa levee (CA02) ;
--                     Porteuse = 'V2' : l'ancienne demande est bloquee par (ou nee de) la demande de la V3 -- ecrit dans
--                                       la V2 au retour (CA04).

CREATE TABLE rec.LienDemande (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_LienDemande PRIMARY KEY,
    Porteuse    char(2)        NOT NULL CONSTRAINT CK_LienDemande_Porteuse CHECK (Porteuse IN ('V3', 'V2')),
    -- La demande qui porte le lien et celle qu'il vise : leurs lignes de v2.DEMANDE dans la base V3.
    IdDemande   int            NOT NULL,
    Lien        varchar(12)    NOT NULL CONSTRAINT CK_LienDemande_Lien CHECK (Lien IN ('inhibitrice', 'mere')),
    IdVisee     int            NOT NULL,
    PoseLe      datetime2(0)   NOT NULL CONSTRAINT DF_LienDemande_PoseLe DEFAULT (SYSDATETIME()),
    PosePar     nvarchar(128)  NOT NULL,     -- « regle de blocage de la V2 », ou l'agent
    LeveLe      datetime2(0)   NULL,         -- blocage leve (la demande de l'ancien RIG validee ou abandonnee, CA02)
    Motif       nvarchar(400)  NULL,
    EcritV2Le   datetime2(0)   NULL          -- Porteuse 'V2' : le lien ecrit dans la V2 au retour (CA04)
);
CREATE INDEX IX_LienDemande_Demande ON rec.LienDemande (IdDemande, Lien) INCLUDE (Porteuse, IdVisee, LeveLe);
CREATE INDEX IX_LienDemande_Visee ON rec.LienDemande (IdVisee) INCLUDE (Porteuse, IdDemande, Lien, LeveLe);
