# La base V2 jetable du banc NumerotationV2Check

Ce dossier monte, dans le SQL Server du poste (`docker compose up -d`), une base V2 réduite aux seuls
objets que touchent les programmes de numérotation. Rien n'est écrit sur une vraie base V2.

| Fichier | Source | Créé avec |
|---|---|---|
| `NUMERO_METIER.sql` | copie exacte de `D:\RigApplication\Source\BaseDeDonnées\RigDatabase\dbo\Tables\NUMERO_METIER.sql` (2026-11-21) | réglages par défaut |
| `GENERER_NUMERO.sql`, `CHERCHER_NUMERO.sql`, `RESTITUER_NUMERO.sql` | copies exactes de `...\RigDatabase\dbo\Stored Procedures\` : les programmes **d'origine**, que la bascule remplace et que le retour arrière remet | `QUOTED_IDENTIFIER OFF` (comme sur RIG_DEV : ils écrivent `"9999"`) |
| `VerifTypeMetier.sql` | copie exacte de `...\RigDatabase\dbo\Functions\VerifTypeMetier.sql` (une des lectures de NUM-006 CA02) | `QUOTED_IDENTIFIER OFF`, `ANSI_NULLS OFF` (comme sur RIG_DEV) |
| `TR_NUMERO_METIER_JET.rigdev.sql`, `fn_CertifNF525Active.rigdev.sql` | `OBJECT_DEFINITION` lu sur `RIG_DEV` (SQL-DEV\DEV, lecture seule, 2026-11-21) : le déclencheur NF525 de la table des compteurs, absent du projet de base | réglages par défaut |
| `nf525.sql` | les tables qu'il lit, réduites ; `SP_INSERT_JET` en **doublure** (range l'événement sans le signer) | réglages par défaut |
| `reprise.sql` | les tables que lisent `num.Reprendre` et `num.ReleverHeritage` (lot 6), jouées par `bascule.sql` avec `Reprise=oui` : réduites aux colonnes lues, types du projet de base ; `NUMERO_METIER_SAVE_CDU` et `NUMERO_CHRONO_MANQUANT` lues sur `RIG_DEV` (lecture seule, 2026-11-21) | réglages par défaut |

Les copies ne se modifient pas : elles sont la V2 telle qu'elle est. Pour les rafraîchir, recopier les
fichiers du projet de base et relire les deux objets sur RIG_DEV.
