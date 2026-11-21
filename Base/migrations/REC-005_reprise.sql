-- apres: REC-001, SOC-011
-- REC-005 : reprendre le transfert apres un incident, sans perte ni doublon (carnet\recopie.json, REC-005 T01, T03, T04),
-- lot 8 du plan de reconstruction, phase 2 (2026-11-21).
--
--   rec.Transfert      un etat de plus, « interrompu » : un incident technique (base injoignable, serveur redemarre,
--                      annulation) a coupe le transfert ; l'instantane de la V2 est GARDE et le transfert se relance de
--                      lui-meme a la table et au lot ou il s'est arrete (CA04). Un transfert « en cours » dont le
--                      processus est mort (arret brutal) se reconnait au verrou de session qu'il ne tient plus.
--                      Reprises : combien de fois il a repris ; PointDeReprise : ce que dit le rapport du matin.
--   rec.TransfertLot   chaque lot ecrit d'une table, enregistre DANS la transaction de son ecriture (T01) : la ligne
--                      existe si et seulement si les lignes du lot et leur correspondance sont dans la base V3. La
--                      reprise repart apres la derniere cle du dernier lot, verifiee contre la table elle-meme.
--
-- Le moteur : Serveur\Recopie\MoteurTransfert.cs, CopieTelleQuelle.cs. Mode d'emploi : docs\reconstruction\recopie.md,
-- section « Reprendre apres un incident (REC-005) ».

ALTER TABLE rec.Transfert DROP CONSTRAINT CK_Transfert_Etat;
ALTER TABLE rec.Transfert ADD CONSTRAINT CK_Transfert_Etat CHECK (Etat IN ('en cours', 'fini', 'arrete', 'refuse', 'interrompu'));
ALTER TABLE rec.Transfert ADD
    Reprises       int            NOT NULL CONSTRAINT DF_Transfert_Reprises DEFAULT (0),
    PointDeReprise nvarchar(1000) NULL,
    -- Tables transferees par une regle PROVISOIRE (REC-008 : repetition d'une base entiere avant les stories de domaine).
    Provisoires    int            NULL;

CREATE TABLE rec.TransfertLot (
    IdTransfert int            NOT NULL CONSTRAINT FK_TransfertLot_Transfert REFERENCES rec.Transfert (Id),
    TableV2     sysname        NOT NULL,
    -- 1, 2, 3... dans l'ordre de la cle primaire ; une table vide a son lot 1 de 0 ligne (la table V3 est creee avec lui).
    Lot         int            NOT NULL,
    -- 0 : le premier passage ; 1 : la premiere reprise... (rec.Transfert.Reprises au moment de l'ecriture).
    Passage     int            NOT NULL,
    -- Lignes de la table ecrites a la fin de ce lot (cumul) et la derniere cle ecrite, en texte (ecriture de
    -- rec.Correspondance.IdV2) ; NULL pour un lot vide.
    Lignes      bigint         NOT NULL,
    DerniereCle nvarchar(200)  NULL,
    DureeMs     bigint         NOT NULL,
    Le          datetime2(3)   NOT NULL CONSTRAINT DF_TransfertLot_Le DEFAULT (SYSDATETIME()),
    CONSTRAINT PK_TransfertLot PRIMARY KEY (IdTransfert, TableV2, Lot)
);

-- Les alertes de la recopie (REC-005 CA05 ; REC-006 CA04 -- Serveur\Recopie\AlertesRecopie.cs, lue par le surveillant de
-- SOC-011) : leurs deux regles entrent dans la liste fermee des alertes (SOC-011_supervision.sql). Une regle de plus
-- s'ajoute de la meme facon, par une migration nouvelle qui reprend la liste COMPLETE de la derniere.
ALTER TABLE soc.AlerteExploitation DROP CONSTRAINT CK_AlerteExploitation_Regle;
ALTER TABLE soc.AlerteExploitation ADD CONSTRAINT CK_AlerteExploitation_Regle
    CHECK (Regle IN ('traitement-en-erreur', 'file-bloquee', 'base-injoignable', 'migrations-en-retard', 'echec-numerotation',
                     'recopie-incident', 'recopie-ecart'));
