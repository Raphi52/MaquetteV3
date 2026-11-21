using RigV3.Comparaison;

// ============================================================
// L'OUTIL DU CONTROLE DES DEUX BASES D'UN GREFFE (REC-006 ; lot 8 du plan, 2026-11-21).
// Mode d'emploi complet : docs/reconstruction/recopie.md, « Comparer chaque nuit ».
//
//   RigV3.Comparaison comparer --v2 <chaine de la base V2> --v3 <chaine de sa base V3> [--tables A,B | --toutes]
//                              [--detail 1000] [--conservation 90] [--exploitation]
//       compare, sur un etat fige de chaque base, les fiches partagees (ou les tables nommees, ou --toutes : chaque
//       table transferee) : nombre de lignes, empreinte de chaque ligne par la table de correspondance, colonnes qui
//       different, lignes sans correspondance, liens orphelins ; ecrit le rapport dans la base V3 (rec.Comparaison)
//       et l'imprime. C'est le controle que la partie serveur fait chaque nuit (ComparaisonNocturne).
//       Sans --exploitation, les deux bases doivent etre sur CE poste : un agent qui le lance d'ici n'ouvre aucune
//       base de greffe.
// Code de sortie : 0 aucun ecart ; 5 au moins un ecart, lien orphelin ou ligne sans correspondance ; 1 controle arrete ;
// 3 commande mal formee ou serveur refuse.
// ============================================================

return await LigneDeCommande.ExecuterAsync(args, Console.Out);
