-- SOC-018 : les chaines signees de la V3, ouvertes le jour du passage (carnet\socle.json, SOC-018, taches T01 et T04).
-- apres: SOC-005, SOC-016, NUM-001
--
-- Decision Q6 du cadrage : chaque logiciel garde ses chaines signees. La V2 garde les siennes (JET_NP, JET_P, FACTURE,
-- ENCAISSEMENT, GRAND_TOTAUX_*, FACTURE_DUPLICATA), verifiables dans la V2 ; la V3 ouvre les SIENNES, vides, au
-- passage du greffe. Rien ici ne lit ni n'ecrit une base V2 (CA02).
--
-- Les 7 chaines (C19) et leurs tables :
--   FAC  factures                       soc.FactureSignee (+ soc.FactureSigneeLigne, ventilation par taux)
--   ENC  encaissements                  soc.EncaissementSigne
--   GTF  grands totaux des factures     soc.GrandTotalFactures (+ soc.GrandTotalFacturesTva)
--   GTE  grands totaux des encaissements soc.GrandTotalEncaissements
--   DUP  duplicatas                     soc.DuplicataSigne
--   JNP  journal non purgeable          soc.EvenementTechnique (SOC-005), Chaine = 'JNP'
--   JP   journal purgeable              soc.EvenementTechnique (SOC-005), Chaine = 'JP'
-- Le journal des evenements est la table de SOC-005, posee en ajout seul « en attendant sa signature (SOC-018) » :
-- elle recoit ici sa signature, pas une copie. Le catalogue soc.CodeEvenement dit si un code est purgeable.
--
-- La signature (ECDSA P-256, prefixe E1:, comme la V2 : CertifExpressoApp48\SignatureEcdsa.cs) est calculee par la
-- partie serveur (Serveur\Socle\Chaines), qui tient la cle privee ; la base garde la cle PUBLIQUE (soc.CleSignature) :
-- n'importe qui peut verifier une chaine sans la cle privee. Chaque maillon reprend la signature du precedent.
--
-- INALTERABLE (T01) : une ligne signee ne se modifie ni ne s'efface. Seule la signature (SESSION_CONTEXT
-- 'rigv3.signature' = 1, posee et retiree par Serveur\Socle\Chaines\ServiceChaines.cs) ecrit dans les tables des
-- pieces ; seule la purge du journal purgeable d'un exercice archive ('rigv3.purge-jet') en efface. C'est une garde
-- contre l'erreur, pas contre le proprietaire de la base : une alteration directe est DETECTEE par le controle
-- d'integrite (evenements 90 et 95, CA04).
--
-- Mode d'emploi, regles et ecarts : docs\reconstruction\socle-chaines.md.
-- fix-ok: fichier NEUF ecrit en plusieurs passes, aucune correction a l'aveugle -- passes 2 et 3 : le journal purgeable repart a 1 par exercice comme SP_HashJETR19_P de la V2 (lu), car un evenement retro-date entrelace les rangs entre exercices et ferait un faux 95 (limite ecrite dans SP_VERIF_RUPTURE_SEQUENCE, lu) ; la purge ne prend que les maillons que l'archive couvre.

-- ---------------------------------------------------------------------------------------------------------------
-- 1. Le catalogue des 59 codes d'evenement (V2 : CODE_JET). Les lignes VALUES sont COPIEES TELLES QUELLES du script
--    de deploiement de la certification de la V2 : D:\RigApplication, branche origin/Development (commit 2f7d6c0 du
--    2026-11-13), Source\Outils\DLL\ExpressoCertification\CertifExpressoApp48\SQL_Sources\
--    SQLQuery_Deploy_Certification_FULL.sql, lignes 129 a 187 (le MERGE de CODE_JET). Memes 59 codes que
--    RIG_DEV.dbo.CODE_JET lu le 2026-11-20 (carnet, sources de SOC-018).
-- ---------------------------------------------------------------------------------------------------------------
CREATE TABLE soc.CodeEvenement (
    Code         smallint       NOT NULL CONSTRAINT PK_CodeEvenement PRIMARY KEY,   -- R_CODEJET_CODE
    Exigibilite  char(1)        NOT NULL,                                           -- C, O ou X
    Libelle      nvarchar(128)  NOT NULL,
    Purgeable    bit            NOT NULL,                                           -- R_CODEJET_PURGEABLE = 'O'
    Commentaire  nvarchar(max)  NULL
);
INSERT INTO soc.CodeEvenement (Code, Exigibilite, Libelle, Purgeable, Commentaire)
SELECT CONVERT(smallint, src.R_CODEJET_CODE), src.R_CODEJET_EXIGIBILITE, src.R_CODEJET_LIBELLE,
       CASE src.R_CODEJET_PURGEABLE WHEN N'O' THEN 1 ELSE 0 END, NULLIF(src.R_CODEJET_COMMENTAIRES, N'')
FROM (VALUES
    (N'10', N'C', N'Changement du mode de gestion de la séquence continue [1]', N'N', N'A utiliser en cas de changement de numérotation des pièces justificatives (changement des modalités de numérotation lié à des évolutions temporelles ou organisationnelles).'),
    (N'15', N'C', N'Changement du mode de gestion de la séquence continue [1]', N'N', N'A utiliser en cas de changement de numérotation des pièces justificatives (changement des modalités de numérotation lié à des évolutions temporelles ou organisationnelles).'),
    (N'20', N'X', N'Archivage fiscal de période', N'O', N'Evénement non purgeable si l''archivage de fin d''année ou d''exercice n''est pas exhaustif mais est constitué d''un assemblage d''archives périodiques.'),
    (N'30', N'C', N'Archivage fiscal d''année ou d''exercice', N'N', N''),
    (N'40', N'X', N'Arrêt du terminal, déconnexion, fermeture de session', N'O', N''),
    (N'50', N'X', N'Clôture de période (Jour/Mois)', N'O', N'Pour les logiciels de facturation procédant à des ventes BtoC, la clôture de période peut devenir facultative (selon les modalités retenues pour le projet d''adaptation du BOI-TVA-DECLA-30-10-30).'),
    (N'60', N'C', N'Clôture annuelle ou d''exercice', N'N', N''),
    (N'70', N'C', N'Début de mode dégradé (hors connexion, ...)', N'N', N'Le mode dégradé correspond à une déconnexion du terminal provoquant un fonctionnement autonome de ce dernier.'),
    (N'80', N'X', N'Démarrage du terminal, connexion, ouverture de session', N'O', N''),
    (N'90', N'C', N'Détection d''un défaut d''intégrité dans les données sécurisées ou dans une archive fiscale', N'N', N'L''intégrité est rompue quand une donnée soumise à sécurisation a été modifiée à postériori'),
    (N'95', N'C', N'Détection d''une rupture de la séquence continue', N'N', N'Concerne la détection de la rupture de la séquence continue hors changement du mode de gestion géré par le code événement 10.'),
    (N'100', N'C', N'Début de l''exécution d''une fonction spéciale', N'N', N'Les modes école et formation sont, par exemple, des fonctions spéciales.'),
    (N'105', N'C', N'Fin de l''exécution d''une fonction spéciale', N'N', N'Les modes école et formation sont, par exemple, des fonctions spéciales.'),
    (N'110', N'C', N'Exportation de données', N'O', N'L''exportation des données ne concerne pas l''exportation en comptabilité Un export de données peut être, par exemple, la production d''un fichier associé au Z de caisse, une exportation vers un module de gestion du stock...'),
    (N'120', N'C', N'Fin de mode dégradé', N'N', N'La fin du mode dégradé correspond à une reconnexion du terminal provoquant l''arrêt du mode autonome et le redémarrage du mode centralisateur/connecté.'),
    (N'123', N'C', N'Renvoi forcé de données vers le système central', N'N', N'Cet événement ne peut intervenir qu''après le code 120.'),
    (N'125', N'C', N'Centralisation des données après la fin du mode dégradé', N'N', N'Cet événement survient après la reconnexion du terminal provoquant l''arrêt du mode autonome (code 120) et identifie la constitution des données centralisées.'),
    (N'126', N'O', N'Rajout d''un point de vente ou d''un poste utilisateur', N'N', N''),
    (N'127', N'O', N'Suppression d''un point de vente ou d''un poste utilisateur', N'N', N''),
    (N'128', N'C', N'Changement d''assujetti utilisant le système d''encaissement', N'N', N'A utiliser cas de changement de l''entité assujettie détenant et utilisant le logiciel (Modification de l''identifiant du répertoire des entreprises SIREN) L''entité utilisatrice et la valorisation du Grand Total Perpétuel GTPCA/GTPVA avant changement d''assujetti doivent être mémorisées.'),
    (N'130', N'C', N'Habilitation d''utilisateur/Gestion des droits', N'O', N'Tout changement de droits utilisateurs, de profils, de groupe impactant les caractéristiques certifiées doit être tracé par cet événement'),
    (N'140', N'C', N'Importation de données externes', N'O', N'L''import d''un fichier issu d''un logiciel tiers doit provoquer l''insertion de cet événement (fichier issu d''une balance, d''un EDI etc...).'),
    (N'150', N'C', N'Imprimante indisponible', N'O', N'Le logiciel peut ne pas être en capacité de connaître l''information d''indisponibilité de l''imprimante, l''éditeur doit alors justifier pourquoi.'),
    (N'155', N'C', N'Duplicata d''une note', N'O', N'L''émission d''un duplicata de note implique forcément d''insérer cet événement.'),
    (N'156', N'C', N'Duplicata d''un justificatif de paiement', N'O', N'L''émission d''un duplicata de justificatif de paiement implique forcément d''insérer cet événement.'),
    (N'160', N'C', N'Enregistrement des écritures en comptabilité', N'N', N'Survient lorsque le logiciel écrit directement dans la base des écritures comptables.'),
    (N'170', N'C', N'Traitement de fond de caisse', N'O', N''),
    (N'180', N'C', N'Génération d''un fichier d''exportation des écritures comptables', N'O', N'Survient lorsque le logiciel génère un fichier d''échange pour importation dans un logiciel de comptabilité.'),
    (N'190', N'C', N'Annulation de pièce justificative ticket', N'O', N'Ne concerne pas la délivrance d''une facture d''avoir.'),
    (N'200', N'C', N'Purge des données', N'N', N'Ce code 200 doit être OBLIGATOIREMENT précédé d''une clôture et d''un archivage.'),
    (N'205', N'C', N'Purge Partielle du JET', N'N', N'Cet événement mémorise la purge des événements "purgeables" (Colonne E) du JET qui doit être obligatoirement précédée par la constitution d''une archive de l''année (ou de l''exercice).'),
    (N'210', N'C', N'Reprise de données d''autres logiciels suite à changement de systèmes', N'N', N'Avant un démarrage par exemple.'),
    (N'220', N'C', N'Restauration d''une base de données', N'N', N'Cet événement est imposé quand le logiciel intègre une fonction permettant la restauration des données.'),
    (N'230', N'C', N'Sauvegarde d''une base de données', N'N', N'Cet événement est imposé quand le logiciel intègre une fonction permettant la restauration des données.'),
    (N'240', N'C', N'Suivi des interventions de maintenance', N'N', N'Un éditeur, pour des raisons liées a des anomalies, peut avoir besoin d''intervenir sur la base de données.Ceci peut provoquer une rupture du chainage et de l''intégrité de la sécurisation.Dans ce cas, il doit entrer MANUELLEMENT ce code avec la raison de la maintenance.Ce code est donc un code "Manuel".'),
    (N'250', N'X', N'Installation d''une nouvelle version majeure du logiciel', N'N', N'Cet événement peut être créé par le programme d''installation du logiciel ou saisi manuellement.'),
    (N'255', N'C', N'Installation d''une nouvelle version mineure du logiciel', N'O', N'Cet événement peut être créé par le programme d''installation du logiciel ou saisi manuellement.'),
    (N'260', N'C', N'Initialisation des données', N'N', N'Peut survenir lors de la première utilisation du logiciel.'),
    (N'270', N'C', N'Evolution de paramètre de conformité aux règles', N'N', N'Tout changement de paramètres impactant les règles de certification doit être tracé par ce code événement. (Exemple : changement de localisation dans les paramètres du logiciel, activation de la saisie en multidevises, ...).'),
    (N'280', N'C', N'Début/fin d''opération de contrôle fiscal', N'O', N'Si le produit permet de créer un profil utilisateurs de type "Contrôle Fiscal", la première connexion de cet utilisateur doit provoquer l''insertion de cet événement.'),
    (N'290', N'O', N'Echange avec un expert-comptable (envoi du FEC ou du Z de caisse)', N'O', N'Concerne l''envoi dédié à un expert-comptable : - du Z de caisse (afin de s''assurer du respect de l''intégrité des données cumulatives) - du FEC (Afin de mémoriser l''envoi du FEC par une entreprise à son ECC avant la saisie des écritures de révision par ce dernier.)'),
    (N'295', N'C', N'Transfert de fichier à la demande de l''administration', N'N', N'Permet de mémoriser l''envoi de fichiers (et de leur éventuel scellement) à l''administration dans le cadre d''un Contrôle des Comptabilités Informatisées.'),
    (N'300', N'C', N'Activation de paramètre autorisé et documenté', N'N', N'Remplacé par le code 270, mais conservé à titre historique.'),
    (N'320', N'C', N'Abandon d''une opération de vente (transaction) commerciale non encore enregistrée', N'O', N'Concerne une saisie en cours non encore validée.'),
    (N'323', N'C', N'Annulation/modification d''une ligne d''article [3]', N'O', N'S''applique aux pièces justificatives en amont de la transaction.'),
    (N'324', N'C', N'Abandon d''une note validée', N'O', N'La note ici n''est pas une note d''honoraires mais la note utilisée en restauration. Une note validée abandonnée est une note signée "contre-passée".'),
    (N'325', N'C', N'Annulation(s) d''article(s) après commande', N'O', N''),
    (N'326', N'C', N'Retour(s) d''article(s) après une émission d''une note', N'O', N''),
    (N'327', N'C', N'Passage en note "offerte"', N'O', N''),
    (N'328', N'C', N'Passage d''article en "offert"', N'O', N''),
    (N'329', N'C', N'Non édition d''une note [2]', N'O', N'Permet de tracer les notes non imprimées.'),
    (N'330', N'O', N'Initialisation de la clôture de fin de période', N'O', N'En cas de clôture soumises à plusieurs traitements, cet événement permet de tracer le début de lancement du traitement de clôture'),
    (N'400', N'O', N'Changement de la durée de l''exercice', N'N', N''),
    (N'410', N'C', N'Changement de données affectant l''identification de l''assujetti ou du mandataire', N'N', N'Cet événement permet de tracer toute modification des données ayant un impact sur l''identification de l''assujetti (changement d''adresse, modification du Siret, changement de la dénomination sociale...).'),
    (N'420', N'C', N'Changement du mode de règlement, après émission du ticket', N'O', N''),
    (N'430', N'C', N'Changement de table (en restauration) affectée à une note', N'O', N''),
    (N'440', N'O', N'Transfert des archives fiscales sur support externe', N'N', N''),
    (N'450', N'C', N'Traçabilité du changement du trousseau de clés utilisé pour signer', N'N', N'Permet de tracer le changement du couple de clé utilisé pour signer les données.'),
    (N'900', N'O', N'Fonctions éditeur', N'O', N'L''éditeur peut insérer ses propres codes pour tracer des événements supplémentaires.')
) AS src (R_CODEJET_CODE, R_CODEJET_EXIGIBILITE, R_CODEJET_LIBELLE, R_CODEJET_PURGEABLE, R_CODEJET_COMMENTAIRES);

-- ---------------------------------------------------------------------------------------------------------------
-- 2. La certification de la base : une ligne, posee a l'ouverture des chaines (T04). Il n'y a AUCUN reglage pour la
--    desactiver : la V3 certifie tous les greffes des le premier jour (CA06), quel que soit ACTIVATION_CERTIF_NF525
--    de la base V2. Le fuseau dit a quelle heure du greffe se lisent les jours, les mois et l'exercice.
-- ---------------------------------------------------------------------------------------------------------------
CREATE TABLE soc.Certification (
    Id          tinyint        NOT NULL CONSTRAINT PK_Certification PRIMARY KEY CONSTRAINT CK_Certification_Une CHECK (Id = 1),
    CodeGreffe  varchar(20)    NOT NULL,
    Fuseau      varchar(64)    NOT NULL,
    OuverteLe   datetime2(3)   NOT NULL CONSTRAINT DF_Certification_OuverteLe DEFAULT (SYSUTCDATETIME()),   -- UTC
    OuvertePar  nvarchar(128)  NOT NULL
);

-- Les cles publiques qui ont signe (une par trousseau ; un changement de trousseau est un evenement 450).
CREATE TABLE soc.CleSignature (
    Id           int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_CleSignature PRIMARY KEY,
    Empreinte    char(64)        NOT NULL CONSTRAINT UQ_CleSignature_Empreinte UNIQUE,   -- SHA-256 (hexa) de la cle publique
    ClePublique  varbinary(400)  NOT NULL,                                               -- SubjectPublicKeyInfo (DER)
    Algorithme   varchar(30)     NOT NULL,
    Conteneur    nvarchar(128)   NULL,                                                   -- ou vit la cle privee
    DepuisLe     datetime2(3)    NOT NULL CONSTRAINT DF_CleSignature_DepuisLe DEFAULT (SYSUTCDATETIME())
);

-- Le dernier maillon emis par chaine : un ordre transactionnel (la V2 prenait une SEQUENCE SQL, qui laisse un trou
-- « legitime » a chaque transaction annulee, SP_VERIF_RUPTURE_SEQUENCE). Ici un trou est toujours une rupture, et
-- la suppression des DERNIERS maillons se voit aussi (maillon present le plus haut < DerniereSeq).
-- Le journal PURGEABLE repart a 1 a chaque exercice, comme la V2 (SP_HashJETR19_P : une purge retire un exercice
-- entier, la chaine ne doit pas dependre d'un exercice anterieur) : une ligne par exercice, Exercice = 0 pour les
-- autres chaines. PurgeJusqua : les maillons de l'exercice effaces par sa purge (CA07), qui ne sont pas des trous.
CREATE TABLE soc.EtatChaine (
    Chaine       varchar(3)    NOT NULL CONSTRAINT CK_EtatChaine_Chaine CHECK (Chaine IN ('FAC', 'ENC', 'GTF', 'GTE', 'DUP', 'JNP', 'JP')),
    Exercice     smallint      NOT NULL CONSTRAINT DF_EtatChaine_Exercice DEFAULT (0),
    DerniereSeq  bigint        NOT NULL CONSTRAINT DF_EtatChaine_DerniereSeq DEFAULT (0),
    PurgeJusqua  bigint        NULL,
    MisAJourLe   datetime2(3)  NOT NULL CONSTRAINT DF_EtatChaine_MisAJourLe DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_EtatChaine PRIMARY KEY (Chaine, Exercice),
    CONSTRAINT CK_EtatChaine_Exercice CHECK ((Chaine = 'JP' AND Exercice > 0) OR (Chaine <> 'JP' AND Exercice = 0)),
    CONSTRAINT CK_EtatChaine_Purge CHECK (PurgeJusqua IS NULL OR (Chaine = 'JP' AND PurgeJusqua <= DerniereSeq))
);

-- ---------------------------------------------------------------------------------------------------------------
-- 3. Le journal des evenements (SOC-005) recoit sa signature. Un evenement est ecrit par n'importe quel programme
--    (droits, reprise, livraison, declencheurs) SANS signature ; la partie serveur le signe ensuite, dans l'ordre,
--    en lui donnant sa chaine (purgeable ou non, selon son code), son exercice et son rang (comme le job de la V2 :
--    SP_INSERT_JET range, CertifExpressoApp signe).
-- ---------------------------------------------------------------------------------------------------------------
ALTER TABLE soc.EvenementTechnique ADD
    Chaine     varchar(3)    NULL,   -- 'JNP' ou 'JP', posee a la signature
    Exercice   smallint      NULL,   -- l'annee de l'evenement a l'heure du greffe, posee a la signature
    Seq        bigint        NULL,   -- le rang dans sa chaine
    Signature  varchar(120)  NULL,
    IdCle      int           NULL CONSTRAINT FK_EvenementTechnique_Cle REFERENCES soc.CleSignature (Id),
    SigneLe    datetime2(3)  NULL;
GO
ALTER TABLE soc.EvenementTechnique ADD
    CONSTRAINT FK_EvenementTechnique_Code FOREIGN KEY (Code) REFERENCES soc.CodeEvenement (Code),
    CONSTRAINT CK_EvenementTechnique_Chaine CHECK (Chaine IS NULL OR Chaine IN ('JNP', 'JP')),
    CONSTRAINT CK_EvenementTechnique_Signe CHECK (
        (Chaine IS NULL AND Exercice IS NULL AND Seq IS NULL AND Signature IS NULL AND IdCle IS NULL AND SigneLe IS NULL)
     OR (Chaine IS NOT NULL AND Exercice IS NOT NULL AND Seq IS NOT NULL AND Signature IS NOT NULL AND IdCle IS NOT NULL AND SigneLe IS NOT NULL));
CREATE UNIQUE INDEX UX_EvenementTechnique_MaillonJnp ON soc.EvenementTechnique (Seq) WHERE Chaine = 'JNP';
CREATE UNIQUE INDEX UX_EvenementTechnique_MaillonJp ON soc.EvenementTechnique (Exercice, Seq) WHERE Chaine = 'JP';
CREATE INDEX IX_EvenementTechnique_EnAttente ON soc.EvenementTechnique (Id) WHERE Seq IS NULL;
CREATE INDEX IX_EvenementTechnique_Instant ON soc.EvenementTechnique (Instant DESC) INCLUDE (Code, Chaine);
GO

-- ---------------------------------------------------------------------------------------------------------------
-- 4. Les pieces signees. Chaque ligne entre signee (Seq, Signature et IdCle obligatoires) : la partie serveur
--    calcule la signature avant l'insertion, dans la transaction de l'enregistrement de la piece (CA03).
--    Les factures et les encaissements eux-memes sont au domaine Facturation (notes de la story) : ces tables
--    portent ce qui est SIGNE d'eux, les champs de la pre-image de la V2 (SP_HashFactureR12, SP_HashEncaissement13).
-- ---------------------------------------------------------------------------------------------------------------
CREATE TABLE soc.FactureSignee (
    Id          bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_FactureSignee PRIMARY KEY,
    Numero      varchar(25)     NOT NULL CONSTRAINT UQ_FactureSignee_Numero UNIQUE,   -- FACTU_NUMERO_FACTURE (serie FACV3)
    Nature      varchar(7)      NOT NULL CONSTRAINT CK_FactureSignee_Nature CHECK (Nature IN ('FACTURE', 'AVOIR')),
    EmiseLe     datetime2(0)    NOT NULL,   -- heure du greffe (FACTU_DATE_CREATION)
    MontantTtc  decimal(19, 2)  NOT NULL,
    Client      nvarchar(100)   NOT NULL,   -- CLIEN_NOM
    CodePostal  varchar(10)     NULL,       -- ADRBS_CODE_POSTAL
    TvaIntra    varchar(15)     NULL,       -- CLIEN_TVA_INTRA
    Seq         bigint          NOT NULL CONSTRAINT UQ_FactureSignee_Seq UNIQUE,
    Signature   varchar(120)    NOT NULL,
    IdCle       int             NOT NULL CONSTRAINT FK_FactureSignee_Cle REFERENCES soc.CleSignature (Id),
    SigneeLe    datetime2(3)    NOT NULL CONSTRAINT DF_FactureSignee_SigneeLe DEFAULT (SYSUTCDATETIME())
);
CREATE INDEX IX_FactureSignee_EmiseLe ON soc.FactureSignee (EmiseLe);

-- La ventilation d'une facture par taux (V2 : ARTICLE_UTILISE, montant TTC et taux de chaque article), dans la pre-image.
CREATE TABLE soc.FactureSigneeLigne (
    IdFacture   bigint          NOT NULL CONSTRAINT FK_FactureSigneeLigne_Facture REFERENCES soc.FactureSignee (Id),
    Rang        smallint        NOT NULL,
    Taux        decimal(5, 2)   NOT NULL,
    MontantTtc  decimal(19, 2)  NOT NULL,
    CONSTRAINT PK_FactureSigneeLigne PRIMARY KEY (IdFacture, Rang)
);

CREATE TABLE soc.EncaissementSigne (
    Id          bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_EncaissementSigne PRIMARY KEY,
    Numero      varchar(32)     NOT NULL CONSTRAINT UQ_EncaissementSigne_Numero UNIQUE,   -- ENCMT_ID_ENCMT
    EncaisseLe  datetime2(0)    NOT NULL,   -- heure du greffe (ENCMT_DATE_SAISIE)
    Montant     decimal(19, 2)  NOT NULL,   -- ENCMT_MONTANT
    Seq         bigint          NOT NULL CONSTRAINT UQ_EncaissementSigne_Seq UNIQUE,
    Signature   varchar(120)    NOT NULL,
    IdCle       int             NOT NULL CONSTRAINT FK_EncaissementSigne_Cle REFERENCES soc.CleSignature (Id),
    SigneLe     datetime2(3)    NOT NULL CONSTRAINT DF_EncaissementSigne_SigneLe DEFAULT (SYSUTCDATETIME())
);
CREATE INDEX IX_EncaissementSigne_EncaisseLe ON soc.EncaissementSigne (EncaisseLe);

-- Un duplicata imprime (V2 : FACTURE_DUPLICATA, SP_HashDuplicata) : il reprend la signature de sa facture ET celle
-- du duplicata precedent de la chaine.
CREATE TABLE soc.DuplicataSigne (
    Id             bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_DuplicataSigne PRIMARY KEY,
    NumeroFacture  varchar(25)     NOT NULL CONSTRAINT FK_DuplicataSigne_Facture REFERENCES soc.FactureSignee (Numero),
    Rang           int             NOT NULL,   -- le combientieme duplicata de cette facture (X_FACTDUP_NBR)
    ImprimeLe      datetime2(0)    NOT NULL,   -- heure du greffe
    Motif          nvarchar(200)   NOT NULL,
    Compte         nvarchar(128)   NOT NULL,   -- qui l'a imprime
    Seq            bigint          NOT NULL CONSTRAINT UQ_DuplicataSigne_Seq UNIQUE,
    Signature      varchar(120)    NOT NULL,
    IdCle          int             NOT NULL CONSTRAINT FK_DuplicataSigne_Cle REFERENCES soc.CleSignature (Id),
    SigneLe        datetime2(3)    NOT NULL CONSTRAINT DF_DuplicataSigne_SigneLe DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_DuplicataSigne_Rang UNIQUE (NumeroFacture, Rang)
);

-- Les grands totaux (V2 : GRAND_TOTAUX_PERIODIQUES_FACTURE et _ENCAISSEMENT, SP_INSERT_Totaux_*_Periodiques) :
-- GTP jour (yyyyMMdd), GTM mois (yyyyMM), GTA/E exercice (yyyy). Le total de la periode, et deux cumuls PERPETUELS :
-- le precedent du meme type plus la periode, en valeur reelle et en valeur absolue.
CREATE TABLE soc.GrandTotalFactures (
    Id           bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_GrandTotalFactures PRIMARY KEY,
    Type         varchar(5)      NOT NULL CONSTRAINT CK_GrandTotalFactures_Type CHECK (Type IN ('GTP', 'GTM', 'GTA/E')),
    Periode      varchar(8)      NOT NULL,
    Total        decimal(19, 2)  NOT NULL,
    CumulReel    decimal(19, 2)  NOT NULL,
    CumulAbsolu  decimal(19, 2)  NOT NULL,
    NbPieces     int             NOT NULL,
    CalculeLe    datetime2(3)    NOT NULL,   -- UTC
    Information  nvarchar(255)   NULL,
    Seq          bigint          NOT NULL CONSTRAINT UQ_GrandTotalFactures_Seq UNIQUE,
    Signature    varchar(120)    NOT NULL,
    IdCle        int             NOT NULL CONSTRAINT FK_GrandTotalFactures_Cle REFERENCES soc.CleSignature (Id),
    CONSTRAINT UQ_GrandTotalFactures_Periode UNIQUE (Type, Periode)
);
CREATE TABLE soc.GrandTotalFacturesTva (
    IdTotal     bigint          NOT NULL CONSTRAINT FK_GrandTotalFacturesTva_Total REFERENCES soc.GrandTotalFactures (Id),
    Taux        decimal(5, 2)   NOT NULL,
    MontantTtc  decimal(19, 2)  NOT NULL,
    CONSTRAINT PK_GrandTotalFacturesTva PRIMARY KEY (IdTotal, Taux)
);
CREATE TABLE soc.GrandTotalEncaissements (
    Id           bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_GrandTotalEncaissements PRIMARY KEY,
    Type         varchar(5)      NOT NULL CONSTRAINT CK_GrandTotalEncaissements_Type CHECK (Type IN ('GTP', 'GTM', 'GTA/E')),
    Periode      varchar(8)      NOT NULL,
    Total        decimal(19, 2)  NOT NULL,
    CumulReel    decimal(19, 2)  NOT NULL,
    CumulAbsolu  decimal(19, 2)  NOT NULL,
    NbPieces     int             NOT NULL,
    CalculeLe    datetime2(3)    NOT NULL,
    Information  nvarchar(255)   NULL,
    Seq          bigint          NOT NULL CONSTRAINT UQ_GrandTotalEncaissements_Seq UNIQUE,
    Signature    varchar(120)    NOT NULL,
    IdCle        int             NOT NULL CONSTRAINT FK_GrandTotalEncaissements_Cle REFERENCES soc.CleSignature (Id),
    CONSTRAINT UQ_GrandTotalEncaissements_Periode UNIQUE (Type, Periode)
);

-- L'archive fiscale d'un exercice (V2 : ARCHIVE_FISCALE_EXERCICE) : le contenu (JSON) de toutes les pieces, totaux et
-- evenements signes de l'exercice, son empreinte SHA-256, son sceau (signature de l'empreinte) et l'evenement 30
-- du journal NON purgeable qui l'enregistre (CA05 : « liee au journal des evenements non purgeables »).
CREATE TABLE soc.ArchiveFiscale (
    Id              int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ArchiveFiscale PRIMARY KEY,
    Exercice        smallint        NOT NULL CONSTRAINT UQ_ArchiveFiscale_Exercice UNIQUE,
    ProduiteLe      datetime2(3)    NOT NULL CONSTRAINT DF_ArchiveFiscale_ProduiteLe DEFAULT (SYSUTCDATETIME()),
    ProduitePar     nvarchar(128)   NOT NULL,
    Fichier         nvarchar(260)   NOT NULL,   -- le nom sous lequel l'archive s'exporte
    Contenu         varbinary(max)  NOT NULL,   -- UTF-8
    Empreinte       char(64)        NOT NULL,   -- SHA-256 (hexa) du contenu
    Sceau           varchar(120)    NOT NULL,
    IdCle           int             NOT NULL CONSTRAINT FK_ArchiveFiscale_Cle REFERENCES soc.CleSignature (Id),
    NbPieces        int             NOT NULL,
    DerniereSeqJnp  bigint          NOT NULL,   -- ARFE_JET_NP_SEQ_MAX : le journal non purgeable couvert
    DerniereSeqJp   bigint          NOT NULL,   -- ARFE_JET_P_SEQ_MAX
    IdEvenement     bigint          NOT NULL CONSTRAINT FK_ArchiveFiscale_Evenement REFERENCES soc.EvenementTechnique (Id)
);

-- La famille du journal des gestes pour l'ouverture, les clotures et la purge (SOC-016).
INSERT INTO soc.FamilleJournal (Famille, Libelle, Facturation, ConservationAns)
VALUES ('certification', 'Certification des factures', 1, 6);
GO

-- ---------------------------------------------------------------------------------------------------------------
-- 5. L'inalterabilite (T01). Messages en clair : la story dit ce qui est permis.
-- ---------------------------------------------------------------------------------------------------------------
-- Le journal des evenements : ajout libre (tout programme ecrit ses evenements) ; une seule modification permise,
-- la SIGNATURE d'un evenement qui n'en a pas encore, sans toucher a son contenu ; un seul effacement permis, la purge
-- du journal PURGEABLE d'un exercice dont l'archive fiscale est faite (CA07). SOC-005 CA04 tient toujours : tout
-- autre changement est refuse, meme au proprietaire de la base (51010).
ALTER TRIGGER soc.EvenementTechnique_AjoutSeul ON soc.EvenementTechnique AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    IF NOT EXISTS (SELECT 1 FROM inserted)
    BEGIN
        IF ISNULL(CAST(SESSION_CONTEXT(N'rigv3.purge-jet') AS int), 0) = 1
           AND NOT EXISTS (SELECT 1 FROM deleted d
                           WHERE d.Chaine IS NULL OR d.Chaine <> 'JP'
                              -- l'archive de son exercice existe ET le contient (un evenement signe apres elle n'y est pas)
                              OR NOT EXISTS (SELECT 1 FROM soc.ArchiveFiscale a WHERE a.Exercice = d.Exercice AND d.Seq <= a.DerniereSeqJp))
            RETURN;
        THROW 51010, N'Le journal des evenements est en ajout seul : une ligne ne s''efface pas (SOC-005 CA04) ; seule la purge du journal purgeable d''un exercice archive le peut (SOC-018 CA07).', 1;
    END;
    IF ISNULL(CAST(SESSION_CONTEXT(N'rigv3.signature') AS int), 0) = 1
       AND NOT EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.Id = d.Id
                       WHERE d.Seq IS NOT NULL OR i.Seq IS NULL
                          OR i.Code <> d.Code OR i.Instant <> d.Instant OR i.Libelle <> d.Libelle OR i.Detail <> d.Detail
                          OR i.Auteur <> d.Auteur OR ISNULL(i.IdAuteur, -1) <> ISNULL(d.IdAuteur, -1))
       AND (SELECT COUNT(*) FROM inserted) = (SELECT COUNT(*) FROM deleted)
        RETURN;
    THROW 51010, N'Le journal des evenements est en ajout seul : une ligne ne se modifie pas (SOC-005 CA04) ; seule la signature d''un evenement non encore signe le peut (SOC-018).', 1;
END;
GO

-- Les pieces signees, leurs lignes, les totaux et l'archive : rien ne s'y ecrit hors de la signature.
CREATE TRIGGER soc.FactureSignee_Inalterable ON soc.FactureSignee AFTER INSERT, UPDATE, DELETE AS
BEGIN
    IF ISNULL(CAST(SESSION_CONTEXT(N'rigv3.signature') AS int), 0) = 1 AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    THROW 51018, N'Une facture signee ne se modifie ni ne s''efface ; elle n''entre que par sa signature (SOC-018).', 1;
END;
GO
CREATE TRIGGER soc.FactureSigneeLigne_Inalterable ON soc.FactureSigneeLigne AFTER INSERT, UPDATE, DELETE AS
BEGIN
    IF ISNULL(CAST(SESSION_CONTEXT(N'rigv3.signature') AS int), 0) = 1 AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    THROW 51018, N'La ventilation d''une facture signee ne se modifie ni ne s''efface (SOC-018).', 1;
END;
GO
CREATE TRIGGER soc.EncaissementSigne_Inalterable ON soc.EncaissementSigne AFTER INSERT, UPDATE, DELETE AS
BEGIN
    IF ISNULL(CAST(SESSION_CONTEXT(N'rigv3.signature') AS int), 0) = 1 AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    THROW 51018, N'Un encaissement signe ne se modifie ni ne s''efface ; il n''entre que par sa signature (SOC-018).', 1;
END;
GO
CREATE TRIGGER soc.DuplicataSigne_Inalterable ON soc.DuplicataSigne AFTER INSERT, UPDATE, DELETE AS
BEGIN
    IF ISNULL(CAST(SESSION_CONTEXT(N'rigv3.signature') AS int), 0) = 1 AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    THROW 51018, N'Un duplicata signe ne se modifie ni ne s''efface (SOC-018).', 1;
END;
GO
CREATE TRIGGER soc.GrandTotalFactures_Inalterable ON soc.GrandTotalFactures AFTER INSERT, UPDATE, DELETE AS
BEGIN
    IF ISNULL(CAST(SESSION_CONTEXT(N'rigv3.signature') AS int), 0) = 1 AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    THROW 51018, N'Un grand total signe ne se modifie ni ne s''efface (SOC-018).', 1;
END;
GO
CREATE TRIGGER soc.GrandTotalFacturesTva_Inalterable ON soc.GrandTotalFacturesTva AFTER INSERT, UPDATE, DELETE AS
BEGIN
    IF ISNULL(CAST(SESSION_CONTEXT(N'rigv3.signature') AS int), 0) = 1 AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    THROW 51018, N'Un grand total signe ne se modifie ni ne s''efface (SOC-018).', 1;
END;
GO
CREATE TRIGGER soc.GrandTotalEncaissements_Inalterable ON soc.GrandTotalEncaissements AFTER INSERT, UPDATE, DELETE AS
BEGIN
    IF ISNULL(CAST(SESSION_CONTEXT(N'rigv3.signature') AS int), 0) = 1 AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    THROW 51018, N'Un grand total signe ne se modifie ni ne s''efface (SOC-018).', 1;
END;
GO
CREATE TRIGGER soc.ArchiveFiscale_Inalterable ON soc.ArchiveFiscale AFTER INSERT, UPDATE, DELETE AS
BEGIN
    IF ISNULL(CAST(SESSION_CONTEXT(N'rigv3.signature') AS int), 0) = 1 AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    THROW 51018, N'Une archive fiscale scellee ne se modifie ni ne s''efface (SOC-018).', 1;
END;
GO
CREATE TRIGGER soc.CleSignature_Inalterable ON soc.CleSignature AFTER UPDATE, DELETE AS
BEGIN
    IF NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    THROW 51018, N'Une cle publique de signature ne se modifie ni ne s''efface : les maillons qu''elle a signes en dependent (SOC-018).', 1;
END;
GO
CREATE TRIGGER soc.Certification_Inalterable ON soc.Certification AFTER UPDATE, DELETE AS
BEGIN
    IF NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    THROW 51018, N'L''ouverture des chaines ne se defait pas : la certification de la V3 n''a pas d''interrupteur (SOC-018 CA06).', 1;
END;
GO
CREATE TRIGGER soc.EtatChaine_EnAvant ON soc.EtatChaine AFTER UPDATE, DELETE AS
BEGIN
    IF NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51018, N'L''etat d''une chaine signee ne s''efface pas (SOC-018).', 1;
    IF (SELECT COUNT(*) FROM inserted) <> (SELECT COUNT(*) FROM deleted)
       OR EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Chaine = d.Chaine AND i.Exercice = d.Exercice
                  WHERE i.Chaine IS NULL OR i.DerniereSeq < d.DerniereSeq
                     OR ISNULL(i.PurgeJusqua, -1) < ISNULL(d.PurgeJusqua, -1))
        THROW 51018, N'Le dernier maillon d''une chaine signee ne recule jamais, et une purge ne s''annule pas (SOC-018).', 1;
END;
GO

-- ---------------------------------------------------------------------------------------------------------------
-- 6. Les series de numeros de la V3 ecrivent leurs evenements (NUM-006 CA03, laisse a SOC-018) : un changement de
--    MODE d'une serie (format, codage de l'annee, restitution des numeros rendus) est un evenement 10 « Changement
--    du mode de gestion de la sequence continue » dans le journal de la V3. C'est le seul journal ou il s'ecrit pour
--    les series que la V3 tient seule (FACV3, BDFNI : jamais recopiees dans NUMERO_METIER, NUM-006 CA01) ; pour les
--    autres, la recopie fait aussi ecrire la V2 dans le sien (TR_NUMERO_METIER_JET, si la V2 est certifiee).
--    Comme le declencheur de la V2 (tests\NumerotationV2Check\v2\TR_NUMERO_METIER_JET.rigdev.sql) : le simple
--    avancement du chrono n'ecrit rien, et une anomalie d'ecriture annule le changement (trace ou rien). Ecart :
--    la V2 ignore une serie sans greffe ; une base V3 n'a qu'un greffe, toutes ses series sont tracees.
-- ---------------------------------------------------------------------------------------------------------------
CREATE TRIGGER num.TR_Compteur_ModeSequence ON num.Compteur AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT (UPDATE(Structure) OR UPDATE(CodageAnnee) OR UPDATE(GestionRendus)) RETURN;
    INSERT INTO soc.EvenementTechnique (Code, Libelle, Detail, IdAuteur, Auteur)
    SELECT 10, LEFT(c.Libelle, 120),
           LEFT(CONCAT(N'Changement du mode de la série ', i.Type, N' ', i.Annee,
                       CASE WHEN i.Jour > 0 THEN CONCAT(N' (jour ', i.Jour, N')') ELSE N'' END,
                       N' : format ', d.Structure, N' -> ', i.Structure,
                       N', codage de l''année ', d.CodageAnnee, N' -> ', i.CodageAnnee,
                       N', numéros rendus redonnés ', CASE d.GestionRendus WHEN 1 THEN N'oui' ELSE N'non' END,
                       N' -> ', CASE i.GestionRendus WHEN 1 THEN N'oui' ELSE N'non' END), 1000),
           NULL,
           LEFT(ISNULL(CONVERT(nvarchar(128), SESSION_CONTEXT(N'rigv3.compte')), SUSER_SNAME()), 80)
    FROM inserted i
    JOIN deleted d ON d.Id = i.Id
    JOIN soc.CodeEvenement c ON c.Code = 10
    WHERE i.Structure <> d.Structure OR i.CodageAnnee <> d.CodageAnnee OR i.GestionRendus <> d.GestionRendus;
END;
