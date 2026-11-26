-- Jeu de LecteurNavires (« Navires », PROC_RECHERCHE_BATEAU), lot 6 du plan-676a4b19. Identifiants 16005xx. Ce qui est retenu
-- ou ecarte, et pourquoi : PROC_RECHERCHE_BATEAU.attendu et .recherches. BATEAU et INSCRIPTION sont les fiches rec.Bateau et
-- rec.Inscription (REC-014) des migrations ; aucun autre jeu n'y pose de bateau, et ces inscriptions n'ont pas de demande (la
-- seule voie par laquelle LecteurMentionsEnMarge lit rec.Inscription).
SET IDENTITY_INSERT rec.Bateau ON;
INSERT INTO rec.Bateau (Id, Abrege, Nom, NumeroEnregistrement, Port, EstDerniere, EstNavire, DateImmatriculation, Proprietaire) VALUES
    -- un navire courant, deux inscriptions
    (1600501, 'L6PEL', 'L6 PELICAN', '600501', 'BR', 1, 1, '20190415', 'ARMEMENT BANC'),
    -- un navire renomme : l'ancien nom (masque sans « Afficher l'historique ») et le nouveau, meme numero d'enregistrement
    (1600502, 'L6ANC', 'L6 ANCIEN NOM', '600502', NULL, 0, 1, '20100101', NULL),
    (1600503, 'L6NOU', 'L6 NOUVEAU NOM', '600502', NULL, 1, 1, '20180101', NULL),
    -- ECARTE : pas un navire (BTEAU_IS_NAVIRE faux) -- jumeau retenu 1600501
    (1600504, 'L6BAR', 'L6 BARGE', '600504', NULL, 1, 0, NULL, NULL),
    -- un navire courant sans inscription : AFaire
    (1600505, 'L6SANS', 'L6 SANS INSCRIPTION', '600505', NULL, 1, 1, NULL, NULL);
SET IDENTITY_INSERT rec.Bateau OFF;
SET IDENTITY_INSERT rec.Inscription ON;
INSERT INTO rec.Inscription (Id, IdBateau, NumeroInscription, TypeInscription, ParTransfert, DateInscription, DatePeremption, Renouvelable,
    EtatInfo, DateDiffusion) VALUES
    (1600511, 1600501, '2024H00511', 'HYP', 0, '20240110', '20340110', 0, 'C', '20240110'),
    (1600512, 1600501, '2025H00512', 'HYP', 0, '20250210', NULL, 0, 'C', '20250210'),
    -- inscrite sur l'ancien nom : comptee pour le numero d'enregistrement (historique decoche), pour le bateau sinon
    (1600513, 1600502, '2015H00513', 'HYP', 0, '20150301', NULL, 0, 'C', '20150301');
SET IDENTITY_INSERT rec.Inscription OFF;
