using RigV3.Passage;

// ============================================================
// L'OUTIL DU PASSAGE A LA V3 (lot 13 du plan, phase 2, 2026-11-21 : SOC-019, SOC-022, SOC-024, SOC-025, SOC-026).
// Mode d'emploi complet : docs/reconstruction/socle-exploitation.md, sections « Passage d'un greffe » et suivantes.
//
//   RigV3.Passage passer --commune <base commune> --greffe <code> [--passage aaaa-mm-jjThh:mm] [--fin-beta ...]
//       le passage d'UN greffe, dans l'ordre : controles, chaines signees ouvertes (SOC-018), transfert (REC-001),
//       bascule de la numerotation (Base\v2\bascule.sql), controles, date de passage au catalogue, aiguillage,
//       garde levee hors beta. Une etape qui echoue : retour a l'etat d'avant, le greffe reste sur la V2.
//   RigV3.Passage retour --commune ... --greffe <code>       un greffe beta rendu a la V2 (retour.sql, NUM-022)
//   RigV3.Passage jour-j --commune ... --jour aaaa-mm-jj    le plan du jour J, greffe par greffe a son heure locale
//   RigV3.Passage repeter ... --numero <n> --rapport <f>    une repetition generale sur les copies (SOC-025)
//   RigV3.Passage tableau --dossier <passages>              le tableau de bord du jour J (SOC-024 CA06)
//   RigV3.Passage comparer --repetitions <dossier>          les regles de SOC-025 sur les rapports de repetition
//   RigV3.Passage beta demarrer|terminer|defauts ...        les criteres ecrits de la beta (SOC-022, SOC-019 CA01)
//   RigV3.Passage certification suivi ...                   une version qui touche une fonction certifiee (SOC-019 CA03)
//   RigV3.Passage formation --v2 <base V2> --greffe <code>  le nombre d'agents a former par metier (SOC-026 CA01)
// Sans --exploitation, le catalogue et l'instance du greffe doivent etre sur CE poste (SQL Server de
// docker-compose.yml) : un agent qui lance l'outil d'ici n'ecrit jamais sur SQL-DEV, SQL-PROD ni un serveur de greffe.
// Le passage se lance sous le compte de service de la partie serveur : la cle de signature des chaines du greffe
// est dans son magasin de cles (SOC-018).
// Code de sortie : 0 passe / retour fait / conforme ; 1 echec rattrape, revenu a la V2 / critere manquant ; 2 refuse, rien touche ;
// 3 commande mal formee ; 4 echec ET retour impossible (intervention).
// ============================================================

return await LigneDeCommande.ExecuterAsync(args, Console.Out);
