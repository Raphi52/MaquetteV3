-- apres: REC-001
-- REC-006 : comparer chaque nuit l'ancienne et la nouvelle base de chaque greffe (carnet\recopie.json, REC-006 T01),
-- lot 8 du plan de reconstruction, phase 2 (2026-11-21).
--
--   rec.Comparaison       un controle d'une base : etat fige des deux bases, duree, totaux (CA01) ; garde 90 jours
--                         (duree proposee, Recopie:ConservationRapportsJours), les details partent avec lui (CA04)
--   rec.ComparaisonTable  par table partagee : lignes de chaque cote, lignes comparees, ecarts, lignes sans correspondance
--   rec.ComparaisonEcart  le detail : table, les deux identifiants, les colonnes qui different (CA02), les liens
--                         orphelins (CA03). Ni valeur ni donnee personnelle : les noms de colonnes seulement.
--
-- Le controle : Serveur\Recopie\Comparaison.cs ; sa planification de nuit : ComparaisonNocturne.cs ; l'outil de
-- l'exploitant : Outils\Comparaison. Mode d'emploi : docs\reconstruction\recopie.md, section « Comparer chaque nuit ».

CREATE TABLE rec.Comparaison (
    Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Comparaison PRIMARY KEY,
    BaseV2             sysname       NOT NULL,
    ServeurV2          sysname       NOT NULL,
    -- Les deux etats figes (instantanes de base) lus, et leur date (CA05).
    InstantaneV2       sysname       NULL,
    InstantaneV3       sysname       NULL,
    EtatFigeLe         datetime2(3)  NULL,
    Debut              datetime2(3)  NOT NULL CONSTRAINT DF_Comparaison_Debut DEFAULT (SYSDATETIME()),
    Fin                datetime2(3)  NULL,
    DureeMs            bigint        NULL,
    Etat               varchar(10)   NOT NULL CONSTRAINT DF_Comparaison_Etat DEFAULT ('en cours')
                       CONSTRAINT CK_Comparaison_Etat CHECK (Etat IN ('en cours', 'fini', 'arrete')),
    Motif              nvarchar(max) NULL,
    Tables             int           NULL,
    LignesComparees    bigint        NULL,
    Ecarts             bigint        NULL,
    Orphelins          bigint        NULL,
    SansCorrespondance bigint        NULL,
    -- Ecarts vus dans l'etat fige et disparus des bases vivantes a la relecture (une recopie en cours) : comptes, non alertes.
    Resorbes           bigint        NULL,
    Compte             nvarchar(128) NOT NULL CONSTRAINT DF_Comparaison_Compte DEFAULT (ORIGINAL_LOGIN()),
    Machine            nvarchar(128) NULL CONSTRAINT DF_Comparaison_Machine DEFAULT (HOST_NAME())
);
CREATE INDEX IX_Comparaison_Base ON rec.Comparaison (BaseV2, Debut DESC);

CREATE TABLE rec.ComparaisonTable (
    IdComparaison        int           NOT NULL CONSTRAINT FK_ComparaisonTable_Comparaison REFERENCES rec.Comparaison (Id) ON DELETE CASCADE,
    TableV2              sysname       NOT NULL,
    TableV3              nvarchar(260) NULL,
    LignesV2             bigint        NULL,
    LignesV3             bigint        NULL,
    Comparees            bigint        NULL,
    Ecarts               bigint        NULL,
    -- Ligne V2 sans correspondance (ni rejetee par le transfert) ; ligne V3 sans correspondance.
    SansCorrespondanceV2 bigint        NULL,
    SansCorrespondanceV3 bigint        NULL,
    -- Correspondance dont la ligne V2, ou la ligne V3, n'existe plus.
    SansLigneV2          bigint        NULL,
    SansLigneV3          bigint        NULL,
    Rejetees             bigint        NULL,
    Resorbes             bigint        NULL,
    Colonnes             int           NULL,
    DureeMs              bigint        NULL,
    Detail               nvarchar(max) NULL,
    CONSTRAINT PK_ComparaisonTable PRIMARY KEY (IdComparaison, TableV2)
);

CREATE TABLE rec.ComparaisonEcart (
    Id            bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ComparaisonEcart PRIMARY KEY,
    IdComparaison int           NOT NULL CONSTRAINT FK_ComparaisonEcart_Comparaison REFERENCES rec.Comparaison (Id) ON DELETE CASCADE,
    Genre         varchar(30)   NOT NULL CONSTRAINT CK_ComparaisonEcart_Genre CHECK (Genre IN (
                      'ecart', 'sans correspondance V2', 'sans correspondance V3',
                      'correspondance sans ligne V2', 'correspondance sans ligne V3', 'lien orphelin')),
    TableV2       sysname       NOT NULL,
    IdV2          nvarchar(200) NULL,
    TableV3       nvarchar(260) NULL,
    IdV3          nvarchar(200) NULL,
    -- Ecart : les colonnes qui different ; lien orphelin : la colonne du lien.
    Colonnes      nvarchar(max) NULL,
    -- Lien orphelin : la cle de la V2, la fiche visee (table et identifiant).
    Detail        nvarchar(1000) NULL
);
CREATE INDEX IX_ComparaisonEcart_Comparaison ON rec.ComparaisonEcart (IdComparaison, TableV2, Genre);
