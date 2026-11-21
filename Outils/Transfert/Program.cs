using RigV3.Transfert;

// ============================================================
// L'OUTIL DU TRANSFERT INITIAL D'UNE BASE V2 VERS SA BASE V3 (REC-001 ; lot 7 du plan, 2026-11-21).
// Mode d'emploi complet : docs/reconstruction/recopie.md.
//
//   RigV3.Transfert transferer --v2 <chaine de la base V2> --v3 <chaine de la base V3> [--lot 50000] [--exploitation]
//       transfere la base V2 (Database=RIG_<GREFFE>) dans sa base V3 (a jour des migrations, meme instance) :
//       instantane date, schema compare aux regles, reprises de la phase 1, tables telles quelles, controle de fin ;
//       imprime le journal puis le rapport de fin de nuit, table par table.
//       Sans --exploitation, les deux bases doivent etre sur CE poste (SQL Server de docker-compose.yml) : un agent
//       qui lance l'outil d'ici n'ecrit jamais sur SQL-DEV, SQL-PROD ni un serveur de greffe.
//       Apres un incident (code 4, ou un arret brutal), la MEME commande reprend au dernier lot enregistre (REC-005).
//       --provisoires (repetition seulement, REC-008) : une table sans regle recoit une regle provisoire, marquee.
//   RigV3.Transfert rapport --v3 <chaine de la base V3> | --liste <fichier : une chaine par ligne> [--exploitation]
//       le rapport du matin (REC-005 CA04) : pour chaque base, « termine » ou « interrompu » et son point de reprise.
//   RigV3.Transfert inventaire --v2 <chaine> [--tables AFFAIRE,DEMANDE]
//       lit le catalogue d'une base de reference (lecture seule) et imprime les colonnes attendues de chaque table,
//       a recopier dans la regle de la table (Serveur/Recopie) ; sans --tables : les tables des regles du serveur.
// Code de sortie : 0 fini ; 1 arrete (un ecart) ; 2 refuse avant toute ecriture ; 3 commande mal formee ;
// 4 interrompu (un incident : relancer reprend). Rapport : 0 toutes terminees, 1 sinon.
// ============================================================

return await LigneDeCommande.ExecuterAsync(args, Console.Out);
