# Base/migrations — les scripts de la base V3

Un fichier par migration, nommé par la story du carnet qui l'exige :
`SOC-001_comptes.sql`, `NUM-001_compteurs.sql`. Aucune liste à tenir : l'outil
`Base/RigV3.Migrations.csproj` lit le dossier.

- Les tables d'un thème vont dans le schéma de son préfixe en minuscules
  (`soc.Compte`, `num.Compteur`) ; l'outil crée le schéma s'il manque.
- Besoin d'une autre migration avant la sienne : en-tête `-- apres: SOC-001`.
- Une migration jouée ne se modifie plus : on en écrit une nouvelle.
- `GO` seul sur sa ligne coupe les lots (pour `CREATE PROCEDURE`, `CREATE VIEW`…).

Recette complète : `docs/reconstruction/architecture.md`, « Ajouter une table ».
