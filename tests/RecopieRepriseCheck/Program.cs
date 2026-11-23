using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RecopieTransfertCheck;
using RigV3.Base;
using RigV3.Repetition;
using RigV3.Serveur;
using RigV3.Serveur.Recopie;
using RigV3.Serveur.Socle.Chaines;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Supervision;
using OutilComparaison = RigV3.Comparaison.LigneDeCommande;
using OutilTransfert = RigV3.Transfert.LigneDeCommande;

// Banc de REC-005, REC-006 et REC-008 (carnet\recopie.json), lot 8 du plan de reconstruction, phase 2 (2026-11-21).
// Un controle par critere, qui cite son id ; une contre-epreuve rouge sur chaque controle cle.
//
//  0. Une base V2 jetable tiree de RIG_DEV (ExtraitRigDev, le meme extrait que le banc de REC-001).
//  A. REC-005 CA04 et CA01 : le transfert, lance comme un PROCESSUS (RigV3.Transfert.exe), est TUE au milieu de la table
//     ARRIVEE ; le rapport du matin le dit interrompu avec son point de reprise ; relance, il reprend au lot suivant :
//     ni perte ni doublon (valeurs identiques, EXCEPT), aucun lot du premier passage reecrit ; relance d'un transfert
//     fini : refusee, rien ne change.
//  B. REC-005 : un incident en cours de table (base qui ne repond plus) -> « interrompu », code 4, instantane garde ;
//     contre-epreuve : un point de reprise saboté (dernier lot efface) est vu, le transfert s'arrete au lieu de doubler.
//  C. REC-006 CA01 a CA05 : le controle sur des bases egales (0 ecart, comptes exacts), un ecart injecte des deux cotes
//     (table, identifiants, colonnes), sa contre-epreuve, un ecart resorbe a la relecture, un lien orphelin, la purge a
//     90 jours, la lecture d'un etat fige pendant des ecritures d'agents.
//  D. Le serveur : REC-005 CA05 (alerte d'un incident de plus de 5 minutes, pas a 4), REC-006 CA04 (alerte d'un rapport
//     avec ecart, fermee au controle propre) et T04 (le controle de nuit planifie et joue par le planificateur).
//  E. REC-008 : une repetition complete sur une copie anonymisee (CA01, CA02, CA05), son rapport, le feu vert (CA03,
//     CA04) et le refus d'une copie en clair (CA05).
//  F. REC-007 CA07 (l'outil) : RigV3.Repetition supprimer, lance comme un processus, n'efface qu'une base gardee
//     (rigv3_rep_...) ; une base de banc et un nom hors lettres, chiffres et _ sont refuses (code 2), rien n'est efface.
// Il lui faut « docker compose up -d » et l'acces en lecture a RIG_DEV (autre serveur : RIGV3_RIG_DEV).
// Doc : docs/reconstruction/recopie.md, sections REC-005, REC-006, REC-008.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var migrations = Path.Combine(racine, "Base", "migrations");
var chaineRigDev = Environment.GetEnvironmentVariable("RIGV3_RIG_DEV") is { Length: > 0 } c0 ? c0 :
    @"Server=SQL-DEV\DEV;Database=RIG_DEV;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;ApplicationIntent=ReadOnly;Connect Timeout=20";

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
void Reste(string quoi) => Console.WriteLine("RESTE : " + quoi);
void Info(string quoi) => Console.WriteLine("        " + quoi);

async Task<DataTable> Lire(string chaine, string sql)
{
    await using var cn = new SqlConnection(chaine);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
    var t = new DataTable();
    await using var l = await cmd.ExecuteReaderAsync();
    t.Load(l);
    return t;
}
async Task<object?> Valeur(string chaine, string sql)
{
    var t = await Lire(chaine, sql);
    return t.Rows.Count == 0 || t.Rows[0][0] is DBNull ? null : t.Rows[0][0];
}
async Task<long> Nombre(string chaine, string sql) => Convert.ToInt64(await Valeur(chaine, sql) ?? 0L);
async Task Executer(string chaine, string sql)
{
    await using var cn = new SqlConnection(chaine);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
    await cmd.ExecuteNonQueryAsync();
}

// ====================================================================================================
// 0. RIG_DEV lisible, et la base V2 jetable tiree de son extrait
// ====================================================================================================
try { Info($"RIG_DEV : {await Valeur(chaineRigDev, "SELECT @@SERVERNAME + ' / ' + DB_NAME()")}"); }
catch (SqlException e)
{
    Exiger(false, $"RIG_DEV lisible ({new SqlConnectionStringBuilder(chaineRigDev).DataSource}) : {e.Message} -- il faut l'acces en lecture a SQL-DEV\\DEV (ou RIGV3_RIG_DEV)");
    return 1;
}
var reglesServeur = ReglesTransfert.Decouvrir();
// Les tables copiees telles quelles dans v2.X ; depuis le lot 10, CLIENT et les autres fiches partagees ont leur table dans la V3
// (rec.Client..., RegleTable.Cible), reprises par lots de la meme facon : la reprise de CLIENT, plus bas, le prouve sur rec.Client.
var tellesQuelles = reglesServeur.Regles.Where(r => r.Sorte == SorteRegle.TelleQuelle && r.Cible is null).Select(r => r.Table).ToList();
var entieres = reglesServeur.Regles.Where(r => r.Sorte == SorteRegle.Reprise).Select(r => r.Table).Append("CATEGORIE").ToList();
var extraits = new Dictionary<string, string>
{
    ["AFFAIRE"] = "SELECT TOP (3000) * FROM dbo.AFFAIRE ORDER BY AFFRE_ID_AFFRE DESC",
    ["DEMANDE"] = "SELECT * FROM dbo.DEMANDE WHERE DMND_ID_AFFRE IN (SELECT TOP (3000) AFFRE_ID_AFFRE FROM dbo.AFFAIRE ORDER BY AFFRE_ID_AFFRE DESC)",
    ["ACTION_RCS"] = "SELECT TOP (5000) * FROM dbo.ACTION_RCS ORDER BY ACTRC_ID_ACTRC DESC",
    ["ADRESSE_BASE"] = "SELECT TOP (10000) * FROM dbo.ADRESSE_BASE ORDER BY ADRBS_ID_ADRBS DESC",
    ["ARRIVEE"] = "SELECT TOP (20000) * FROM dbo.ARRIVEE ORDER BY ARRV_ID_ARRV DESC",
    ["CLIENT"] = "SELECT TOP (3000) * FROM dbo.CLIENT ORDER BY CLIEN_ID_CLIEN DESC",
    ["DOSSIER_RCS"] = "SELECT TOP (3000) * FROM dbo.DOSSIER_RCS ORDER BY DSSRC_ID_DSRCS DESC",
    ["INSCRIPTION"] = "SELECT TOP (3000) * FROM dbo.INSCRIPTION ORDER BY INEDT_ID_INSCR DESC",
};
// L'outil et le moteur transferent avec les regles du serveur : la base V2 a chacune de leurs tables (REC-012 a REC-019).
extraits = ExtraitRigDev.Completer(extraits, entieres, reglesServeur.Regles.Select(r => r.Table));
await using var v2 = await BaseJetable.CreerAsync("RecopieRepriseV2");
var chronoExtrait = Stopwatch.StartNew();
var extrait = await ExtraitRigDev.ConstruireAsync(chaineRigDev, v2.Connexion, entieres, extraits, Info);
Info($"base V2 jetable {v2.Nom} : {extrait.Lignes.Values.Sum()} lignes dans {extrait.Lignes.Count} tables en {chronoExtrait.Elapsed.TotalSeconds:0} s ; " +
     string.Join(", ", extrait.Lignes.Where(l => extraits.ContainsKey(l.Key)).Select(l => $"{l.Key} {l.Value}")));

var v3s = new List<BaseJetable>();
async Task<BaseJetable> NouvelleV3(string nom)
{
    var b = await BaseJetable.CreerAsync(nom);
    v3s.Add(b);
    await Migrateur.AppliquerAsync(b.Connexion, migrations);
    return b;
}
async Task<int> Instantanes(string prefixe = "rigv3") => (int)await Nombre(v2.Connexion,
    $"SELECT COUNT(*) FROM sys.databases WHERE source_database_id = DB_ID() AND name LIKE '{v2.Nom}[_]{prefixe}[_]%'");
// Les tables telles quelles : chaque ligne de la V2 a sa pareille exacte dans la V3, et reciproquement (toutes colonnes).
async Task<List<string>> Differences(BaseJetable v3, IEnumerable<string> tables)
{
    var diff = new List<string>();
    foreach (var t in tables)
    {
        var a = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM (SELECT * FROM [{v2.Nom}].dbo.[{t}] EXCEPT SELECT * FROM v2.[{t}]) x");
        var b = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM (SELECT * FROM v2.[{t}] EXCEPT SELECT * FROM [{v2.Nom}].dbo.[{t}]) x");
        if (a + b > 0) diff.Add($"{t} : {a} lignes V2 sans leur pareille en V3, {b} lignes V3 sans leur pareille en V2");
    }
    return diff;
}
// Le verrou de session du transfert est rendu quand SQL Server voit la connexion du processus tue se fermer.
async Task<bool> VerrouLibre(BaseJetable v3)
{
    for (var i = 0; i < 100; i++)
    {
        if (await Nombre(v3.Connexion, $"SELECT APPLOCK_TEST(N'public', N'rigv3-transfert:{v2.Nom}', N'Exclusive', N'Session')") == 1) return true;
        await Task.Delay(100);
    }
    return false;
}

try
{
// ====================================================================================================
// A. REC-005 CA04 et CA01 : le processus du transfert tue au milieu d'une table, puis relance
// ====================================================================================================
var v3A = await NouvelleV3("RecopieRepriseA");
const int Lot = 500;
var exe = Path.Combine(AppContext.BaseDirectory, "RigV3.Transfert.exe");
var demarrage = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
foreach (var a in new[] { "transferer", "--v2", v2.Connexion, "--v3", v3A.Connexion, "--lot", Lot.ToString() }) demarrage.ArgumentList.Add(a);
var sortieTuee = new StringBuilder();
long lotsVusAuKill = 0;
using (var p = Process.Start(demarrage)!)
{
    p.OutputDataReceived += (_, e) => { lock (sortieTuee) sortieTuee.AppendLine(e.Data); };
    p.ErrorDataReceived += (_, e) => { lock (sortieTuee) sortieTuee.AppendLine(e.Data); };
    p.BeginOutputReadLine();
    p.BeginErrorReadLine();
    var attente = Stopwatch.StartNew();
    while (!p.HasExited && attente.Elapsed < TimeSpan.FromMinutes(3))
    {
        lotsVusAuKill = await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM rec.TransfertLot WITH (NOLOCK) WHERE TableV2 = 'ARRIVEE'");
        if (lotsVusAuKill >= 5 && await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM rec.TransfertTable WITH (NOLOCK) WHERE TableV2 = 'ARRIVEE'") == 0)
        {
            p.Kill(entireProcessTree: true);   // ARRET BRUTAL : ni catch, ni finally, ni journal ferme
            break;
        }
        await Task.Delay(10);
    }
    await p.WaitForExitAsync();
}
var tue = (await Lire(v3A.Connexion, "SELECT Id, Etat, Instantane FROM rec.Transfert")).Rows;
var idA = Convert.ToInt32(tue[0][0]);
var lotsAvant = (await Lire(v3A.Connexion, "SELECT Lot, Passage, Lignes, DerniereCle, CONVERT(varchar(30), Le, 126) FROM rec.TransfertLot WHERE TableV2 = 'ARRIVEE' ORDER BY Lot"))
    .Rows.Cast<DataRow>().Select(r => (Lot: Convert.ToInt32(r[0]), Passage: Convert.ToInt32(r[1]), Lignes: Convert.ToInt64(r[2]), Cle: r[3] as string, Le: (string)r[4])).ToList();
var arriveesEcrites = await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM v2.ARRIVEE");
var correspArrivees = await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM rec.Correspondance WHERE TableV2 = 'ARRIVEE'");
var finiesAvant = (await Lire(v3A.Connexion, "SELECT TableV2 FROM rec.TransfertTable")).Rows.Cast<DataRow>().Select(r => (string)r[0]).ToHashSet();
var instantaneGarde = await Instantanes();
Exiger(tue.Count == 1 && (string)tue[0][1] == "en cours" && lotsAvant.Count >= 4 && lotsAvant.All(l => l.Passage == 0)
       && arriveesEcrites == lotsAvant[^1].Lignes && correspArrivees == arriveesEcrites && arriveesEcrites < extrait.Lignes["ARRIVEE"]
       && finiesAvant.Contains("ADRESSE_BASE") && !finiesAvant.Contains("ARRIVEE") && instantaneGarde == 1,
    $"REC-005 T05 (incident) : le processus du transfert est tue au milieu d'ARRIVEE ({lotsVusAuKill} lots vus) : le journal reste « {tue[0][1]} », {lotsAvant.Count} lots " +
    $"enregistres ({arriveesEcrites} lignes sur {extrait.Lignes["ARRIVEE"]}), exactement leurs lignes et leurs correspondances dans la V3 (le lot en vol est annule par SQL Server), " +
    $"{finiesAvant.Count} tables finies avant (dont ADRESSE_BASE), l'instantane de la V2 garde ({instantaneGarde})");

Exiger(await VerrouLibre(v3A), "REC-005 : le verrou de session du processus tue est rendu par SQL Server (un second transfert de la base peut le prendre)");
var matin = new StringWriter();
var codeMatin = await OutilTransfert.ExecuterAsync(["rapport", "--v3", v3A.Connexion], matin);
var texteMatin = matin.ToString();
var lotK = lotsAvant[^1];
Exiger(codeMatin == 1 && texteMatin.Contains("INTERROMPU") && texteMatin.Contains($"table en cours ARRIVEE : lot {lotK.Lot} enregistre ({lotK.Lignes} lignes ecrites, derniere cle {lotK.Cle}")
       && texteMatin.Contains("arret brutal"),
    $"REC-005 CA04 : le rapport du matin dit la base « interrompu » avec son point de reprise (code {codeMatin}) : « {texteMatin.Split('\n').Skip(1).FirstOrDefault()?.Trim()} » / " +
    $"« {texteMatin.Split('\n').Skip(2).FirstOrDefault()?.Trim()} »");

// La relance : la MEME commande.
var sortieR = new StringWriter();
var chronoR = Stopwatch.StartNew();
var codeR = await OutilTransfert.ExecuterAsync(["transferer", "--v2", v2.Connexion, "--v3", v3A.Connexion, "--lot", Lot.ToString()], sortieR);
var texteR = sortieR.ToString();
var transfertR = (await Lire(v3A.Connexion, "SELECT Id, Etat, Reprises, Lues, Ecrites, PointDeReprise FROM rec.Transfert")).Rows;
var lotsApres = (await Lire(v3A.Connexion, "SELECT Lot, Passage, Lignes, DerniereCle, CONVERT(varchar(30), Le, 126) FROM rec.TransfertLot WHERE TableV2 = 'ARRIVEE' ORDER BY Lot"))
    .Rows.Cast<DataRow>().Select(r => (Lot: Convert.ToInt32(r[0]), Passage: Convert.ToInt32(r[1]), Lignes: Convert.ToInt64(r[2]), Cle: r[3] as string, Le: (string)r[4])).ToList();
var premiersIntacts = lotsAvant.All(a => lotsApres.Any(b => b == a));
var lotsAttendus = (int)Math.Ceiling(extrait.Lignes["ARRIVEE"] / (double)Lot);
Exiger(codeR == 0 && transfertR.Count == 1 && Convert.ToInt32(transfertR[0][0]) == idA && (string)transfertR[0][1] == "fini" && Convert.ToInt32(transfertR[0][2]) == 1
       && transfertR[0][5] is DBNull && premiersIntacts && lotsApres.Count == lotsAttendus
       && lotsApres.Where(l => l.Lot > lotK.Lot).All(l => l.Passage == 1) && lotsApres.Count > lotK.Lot && lotsApres[lotK.Lot].Lignes == lotK.Lignes + Lot
       && texteR.Contains($"ARRIVEE : copie telle quelle") && texteR.Contains("reprise apres son dernier lot enregistre") && texteR.Contains("non refaite(s)"),
    $"REC-005 CA04 : relance, le transfert {idA} REPREND (meme identifiant, 1 reprise, code {codeR}, {chronoR.ElapsedMilliseconds} ms) a la table et au lot ou il s'etait arrete : " +
    $"les {finiesAvant.Count} tables finies ne sont pas refaites, ARRIVEE repart du lot {lotK.Lot + 1} apres la cle {lotK.Cle} ; ses {lotK.Lot} premiers lots (passage 0) sont " +
    $"intacts, les {lotsApres.Count - lotK.Lot} suivants sont du passage 1 ({lotsApres.Count} lots de {Lot} au total)");

var comptes = new List<string>();
foreach (var t in tellesQuelles)
{
    var (lignesV3, corresp, distinctes) = (await Nombre(v3A.Connexion, $"SELECT COUNT(*) FROM v2.[{t}]"),
        await Nombre(v3A.Connexion, $"SELECT COUNT(*) FROM rec.Correspondance WHERE TableV2 = '{t}'"),
        await Nombre(v3A.Connexion, $"SELECT COUNT(DISTINCT IdV2) FROM rec.Correspondance WHERE TableV2 = '{t}'"));
    if (lignesV3 != extrait.Lignes[t] || corresp != lignesV3 || distinctes != corresp) comptes.Add($"{t} : V2 {extrait.Lignes[t]}, V3 {lignesV3}, correspondances {corresp} ({distinctes} distinctes)");
}
var diffA = await Differences(v3A, tellesQuelles);
var comptesRelies = await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM rec.Correspondance WHERE TableV2 = 'UTILISATEUR'");
Exiger(comptes.Count == 0 && diffA.Count == 0 && comptesRelies == extrait.Lignes["UTILISATEUR"] && await Instantanes() == 0,
    $"REC-005 CA01 et T05 : apres la reprise, AUCUNE PERTE NI DOUBLON — pour les {tellesQuelles.Count} tables, autant de lignes que la V2, une correspondance et une seule par ligne, " +
    $"chaque ligne identique a la sienne dans la V2 et reciproquement (EXCEPT, toutes colonnes) ; {comptesRelies} comptes relies ; instantane supprime a la fin " +
    $"{string.Join(" ; ", comptes.Concat(diffA))}");

// CA01 (rejouer ce qui est deja applique) : relancer un transfert fini ne reecrit rien.
var empreinteV3 = await Valeur(v3A.Connexion, "SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM v2.ARRIVEE");
var correspAvant = await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM rec.Correspondance");
var sortieDeja = new StringWriter();
var codeDeja = await OutilTransfert.ExecuterAsync(["transferer", "--v2", v2.Connexion, "--v3", v3A.Connexion], sortieDeja);
Exiger(codeDeja == 2 && sortieDeja.ToString().Contains($"a deja un transfert dans cette base V3 (transfert {idA}, fini")
       && Equals(await Valeur(v3A.Connexion, "SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM v2.ARRIVEE"), empreinteV3)
       && await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM rec.Correspondance") == correspAvant,
    $"REC-005 CA01 (transfert) : relancer un transfert deja fini est refuse (code {codeDeja}) et ne touche aucune ligne (empreinte d'ARRIVEE inchangee, correspondances inchangees)");
var matinFin = new StringWriter();
var codeMatinFin = await OutilTransfert.ExecuterAsync(["rapport", "--v3", v3A.Connexion], matinFin);
Exiger(codeMatinFin == 0 && matinFin.ToString().Contains("TERMINE") && matinFin.ToString().Contains("1 reprise(s)"),
    $"REC-005 CA04 : le rapport du matin suivant dit « termine » (1 reprise) : « {matinFin.ToString().Split('\n').Skip(1).FirstOrDefault()?.Trim()} »");
// REC-005 CA01 a CA03 sur la RECOPIE CONTINUE (changement rejouable, 3 essais puis mise de cote, attente par fiche, relance
// unitaire) : prouves sur la file rec.FileRecopie par tests\RecopieDeuxSensCheck (lot 9) ; ce banc prouve le transfert.

// ====================================================================================================
// B. REC-005 : un incident (base qui ne repond plus) en cours de table ; contre-epreuve : point de reprise sabote
// ====================================================================================================
var v3B = await NouvelleV3("RecopieRepriseB");
var rapportB = await MoteurTransfert.TransfererAsync(new OptionsTransfert(v2.Connexion, v3B.Connexion)
{
    TailleLot = Lot,
    ApresLot = (table, lot, _, _) => table == "CLIENT" && lot == 3
        ? throw new InvalidOperationException("la base ne repond plus (incident simule par le banc)") : Task.CompletedTask,
});
var transfertB = (await Lire(v3B.Connexion, "SELECT Etat, Motif, PointDeReprise FROM rec.Transfert")).Rows[0];
Exiger(rapportB.Etat == "interrompu" && rapportB.CodeSortie == 4 && (string)transfertB[0] == "interrompu" && ((string)transfertB[1]).Contains("table CLIENT")
       && ((string)transfertB[1]).Contains("la base ne repond plus") && ((string)transfertB[2]).Contains("table en cours CLIENT : lot 3 enregistre (1500 lignes")
       && await Instantanes() == 1 && await Nombre(v3B.Connexion, "SELECT COUNT(*) FROM rec.Client") == 3 * Lot,
    $"REC-005 : un incident (erreur sur un lot) laisse le transfert « interrompu », code {rapportB.CodeSortie}, motif et point de reprise au journal " +
    $"(« {transfertB[2]} »), les 3 lots enregistres gardes et l'instantane garde pour la reprise");
// Contre-epreuve : on efface le dernier lot enregistre de CLIENT -- le point de reprise ne dit plus ce que la table contient.
await Executer(v3B.Connexion, "DELETE FROM rec.TransfertLot WHERE TableV2 = 'CLIENT' AND Lot = 3");
var rapportB2 = await MoteurTransfert.TransfererAsync(new OptionsTransfert(v2.Connexion, v3B.Connexion) { TailleLot = Lot });
// CLIENT a sa table V3 (rec.Client, lot 10) : annulee, elle est videe (sa migration l'a creee, elle reste).
var clientsB = await Nombre(v3B.Connexion, "SELECT COUNT(*) FROM rec.Client");
Exiger(rapportB2.Etat == "arrete" && rapportB2.CodeSortie == 1 && rapportB2.Motif!.Contains("CLIENT : point de reprise incoherent : lot 2 enregistre avec 1000 lignes")
       && rapportB2.Motif.Contains("a 1500 lignes") && clientsB == 0 && await Nombre(v3B.Connexion, "SELECT COUNT(*) FROM rec.Correspondance WHERE TableV2 = 'CLIENT'") == 0
       && await Instantanes() == 0,
    $"REC-005 contre-epreuve : un point de reprise qui ne dit plus la table (dernier lot efface) est VU — le transfert s'arrete sur l'ecart au lieu de relire 500 lignes deja ecrites " +
    $"(« {rapportB2.Motif} ») ; la table entamee est annulee, l'instantane supprime");
var rapportB3 = await MoteurTransfert.TransfererAsync(new OptionsTransfert(v2.Connexion, v3B.Connexion) { TailleLot = Lot });
Exiger(rapportB3.Etat == "refuse" && rapportB3.Motif!.Contains("un ecart ne se reprend pas"),
    $"REC-005 : un transfert arrete sur un ecart ne se reprend pas : relance refusee (« {rapportB3.Motif} »)");

// ====================================================================================================
// C. REC-006 : le controle des deux bases
// ====================================================================================================
var sortieC = new StringWriter();
var codeC = await OutilComparaison.ExecuterAsync(["comparer", "--v2", v2.Connexion, "--v3", v3A.Connexion], sortieC);
var tablesC = (await Lire(v3A.Connexion, "SELECT TableV2, TableV3, LignesV2, LignesV3, Comparees, Ecarts, SansCorrespondanceV2, SansCorrespondanceV3, Colonnes FROM rec.ComparaisonTable " +
                                         "WHERE IdComparaison = (SELECT MAX(Id) FROM rec.Comparaison)")).Rows.Cast<DataRow>().ToList();
var comparaisonC = (await Lire(v3A.Connexion, "SELECT TOP (1) Etat, Tables, LignesComparees, Ecarts, Orphelins, SansCorrespondance, InstantaneV2, InstantaneV3 FROM rec.Comparaison ORDER BY Id DESC")).Rows[0];
var tqComparees = tablesC.Where(r => (string)r[1] == "v2." + (string)r[0]).ToList();
// Depuis le lot 10, 7 des fiches partagees ont leur table V3 (rec.*, REC-012 a REC-019) : comparees colonne par colonne, par
// leur correspondance ; le nombre de lignes V3 d'un type de demande peut etre plus petit (types indiscernables fusionnes).
var fichesC = tablesC.Where(r => ((string)r[1]).StartsWith("rec.", StringComparison.Ordinal)).ToList();
var utilisateurC = tablesC.SingleOrDefault(r => (string)r[0] == "UTILISATEUR");
bool Egale(DataRow r) => Convert.ToInt64(r[2]) == extrait.Lignes[(string)r[0]] && Convert.ToInt64(r[4]) == Convert.ToInt64(r[2])
                         && Convert.ToInt64(r[5]) == 0 && Convert.ToInt64(r[6]) == 0 && Convert.ToInt64(r[7]) == 0;
Exiger(codeC == 0 && (string)comparaisonC[0] == "fini" && tqComparees.Count == 3 && fichesC.Count == 7 && tablesC.Count == 11
       && tqComparees.All(r => Egale(r) && Convert.ToInt64(r[3]) == Convert.ToInt64(r[2]) && Convert.ToInt32(r[8]) > 5)
       && fichesC.All(r => Egale(r) && ((string)r[0] == "CATEGORIE" || Convert.ToInt64(r[3]) == Convert.ToInt64(r[2])) && Convert.ToInt32(r[8]) > 0)
       && utilisateurC is not null && (string)utilisateurC[1] == "soc.Compte" && Egale(utilisateurC) && Convert.ToInt32(utilisateurC[8]) > 5
       && Convert.ToInt64(comparaisonC[3]) == 0 && Convert.ToInt64(comparaisonC[4]) == 0 && Convert.ToInt64(comparaisonC[5]) == 0 && comparaisonC[6] is string && comparaisonC[7] is string
       && await Instantanes(ComparaisonBases.Usage) == 0,
    $"REC-006 CA01 et T05 : le controle (outil, code {codeC}) compare pour chaque table partagee le nombre de lignes et l'empreinte de chaque ligne par la correspondance, " +
    $"et ecrit son rapport : {comparaisonC[1]} tables, {comparaisonC[2]} lignes comparees, 0 ecart, 0 sans correspondance, 0 orphelin ; les 3 tables telles quelles toutes colonnes " +
    $"({string.Join(", ", tqComparees.Select(r => $"{r[0]} {r[4]}/{r[2]}"))}), les 7 fiches dans leur table V3 colonne par colonne " +
    $"({string.Join(", ", fichesC.Select(r => $"{r[0]} -> {r[1]} {r[4]}/{r[2]}, {r[8]} colonnes"))}), UTILISATEUR -> soc.Compte par ses {utilisateurC?[4]} correspondances, " +
    $"{utilisateurC?[8]} colonnes (REC-017) ; etats figes {comparaisonC[6]} et {comparaisonC[7]}, supprimes a la fin");

// CA02 : un ecart introduit EXPRES de chaque cote, sans passer par la capture.
var clientEcart = await Nombre(v2.Connexion, "SELECT MAX(CLIEN_ID_CLIEN) FROM dbo.CLIENT");
var demandeEcart = await Nombre(v2.Connexion, "SELECT MAX(DMND_ID_DMND) FROM dbo.DEMANDE");
var arriveeEcart = await Nombre(v3A.Connexion, "SELECT MIN(ARRV_ID_ARRV) FROM v2.ARRIVEE");
var nomClient = (string?)await Valeur(v2.Connexion, $"SELECT CLIEN_NOM FROM dbo.CLIENT WHERE CLIEN_ID_CLIEN = {clientEcart}");
var libelleDemande = (string?)await Valeur(v2.Connexion, $"SELECT DMND_LIBELLE FROM dbo.DEMANDE WHERE DMND_ID_DMND = {demandeEcart}");
var refArrivee = await Valeur(v3A.Connexion, $"SELECT ARRV_REFERENCES FROM v2.ARRIVEE WHERE ARRV_ID_ARRV = {arriveeEcart}");
await Executer(v2.Connexion, $"UPDATE dbo.CLIENT SET CLIEN_NOM = 'ECART INJECTE PAR LE BANC', CLIEN_EMAIL = 'banc@exemple.invalid' WHERE CLIEN_ID_CLIEN = {clientEcart};");
await Executer(v2.Connexion, $"UPDATE dbo.DEMANDE SET DMND_LIBELLE = 'ECART INJECTE' WHERE DMND_ID_DMND = {demandeEcart};");
await Executer(v3A.Connexion, $"UPDATE v2.ARRIVEE SET ARRV_REFERENCES = 'ECART V3' WHERE ARRV_ID_ARRV = {arriveeEcart};");
var rapportEcart = await ComparaisonBases.ComparerAsync(new OptionsComparaison(v2.Connexion, v3A.Connexion));
var ecartsVus = rapportEcart.Details.Where(d => d.Genre == "ecart").ToList();
bool Vu(string table, long id, string colonnes) => ecartsVus.Any(e => e.TableV2 == table && e.IdV2 == id.ToString() && e.IdV3 == id.ToString() && e.Colonnes == colonnes);
var ecartsEnBase = await Nombre(v3A.Connexion, $"SELECT COUNT(*) FROM rec.ComparaisonEcart WHERE IdComparaison = {rapportEcart.Id} AND Genre = 'ecart'");
Exiger(rapportEcart.CodeSortie == 5 && rapportEcart.Ecarts == 3 && ecartsVus.Count == 3 && ecartsEnBase == 3
       && Vu("CLIENT", clientEcart, "CLIEN_NOM, CLIEN_EMAIL") && Vu("DEMANDE", demandeEcart, "DMND_LIBELLE") && Vu("ARRIVEE", arriveeEcart, "ARRV_REFERENCES"),
    $"REC-006 CA02 et T06 : trois ecarts injectes sans la capture (deux dans la V2, un dans la V3) sont listes avec leur table, les deux identifiants et les colonnes qui different : " +
    string.Join(" ; ", ecartsVus.Select(e => $"{e.TableV2} {e.IdV2}->{e.IdV3} [{e.Colonnes}]")) + $" (code {rapportEcart.CodeSortie}, rec.ComparaisonEcart : {ecartsEnBase})");
// Contre-epreuve : les valeurs remises, plus rien.
await Executer(v2.Connexion, $"UPDATE dbo.CLIENT SET CLIEN_NOM = (SELECT Nom FROM [{v3A.Nom}].rec.Client WHERE Id = {clientEcart}), " +
                             $"CLIEN_EMAIL = (SELECT Email FROM [{v3A.Nom}].rec.Client WHERE Id = {clientEcart}) WHERE CLIEN_ID_CLIEN = {clientEcart};");
await Executer(v2.Connexion, $"UPDATE dbo.DEMANDE SET DMND_LIBELLE = (SELECT DMND_LIBELLE FROM [{v3A.Nom}].v2.DEMANDE WHERE DMND_ID_DMND = {demandeEcart}) WHERE DMND_ID_DMND = {demandeEcart};");
await Executer(v3A.Connexion, $"UPDATE v2.ARRIVEE SET ARRV_REFERENCES = (SELECT ARRV_REFERENCES FROM [{v2.Nom}].dbo.ARRIVEE WHERE ARRV_ID_ARRV = {arriveeEcart}) WHERE ARRV_ID_ARRV = {arriveeEcart};");
var rapportPropre = await ComparaisonBases.ComparerAsync(new OptionsComparaison(v2.Connexion, v3A.Connexion));
Exiger(rapportPropre.CodeSortie == 0 && rapportPropre.ATraiter == 0
       && nomClient == (string?)await Valeur(v2.Connexion, $"SELECT CLIEN_NOM FROM dbo.CLIENT WHERE CLIEN_ID_CLIEN = {clientEcart}")
       && libelleDemande == (string?)await Valeur(v2.Connexion, $"SELECT DMND_LIBELLE FROM dbo.DEMANDE WHERE DMND_ID_DMND = {demandeEcart}")
       && Equals(refArrivee, await Valeur(v3A.Connexion, $"SELECT ARRV_REFERENCES FROM v2.ARRIVEE WHERE ARRV_ID_ARRV = {arriveeEcart}")),
    "REC-006 CA02 contre-epreuve : les trois valeurs remises, le controle suivant ne trouve plus rien (code 0) : ce sont bien elles qu'il voyait");

// Un ecart vu dans l'etat fige mais disparu des bases vivantes (une recopie en cours entre les deux instantanes) : resorbe.
var arriveeTransitoire = await Nombre(v3A.Connexion, "SELECT MAX(ARRV_ID_ARRV) FROM v2.ARRIVEE");
await Executer(v2.Connexion, $"UPDATE dbo.ARRIVEE SET ARRV_REFERENCES = 'EN VOL' WHERE ARRV_ID_ARRV = {arriveeTransitoire};");
var refTransitoire = await Valeur(v3A.Connexion, $"SELECT ARRV_REFERENCES FROM v2.ARRIVEE WHERE ARRV_ID_ARRV = {arriveeTransitoire}");
var rapportVol = await ComparaisonBases.ComparerAsync(new OptionsComparaison(v2.Connexion, v3A.Connexion)
{
    // Juste apres la prise des etats figes, la « recopie » remet la V2 d'accord avec la V3.
    Suivre = m =>
    {
        if (m.StartsWith("Etats figes", StringComparison.Ordinal))
            Executer(v2.Connexion, $"UPDATE dbo.ARRIVEE SET ARRV_REFERENCES = {(refTransitoire is string s ? $"'{s.Replace("'", "''")}'" : "NULL")} WHERE ARRV_ID_ARRV = {arriveeTransitoire};")
                .GetAwaiter().GetResult();
    },
});
Exiger(rapportVol.Ecarts == 0 && rapportVol.Resorbes == 1 && rapportVol.CodeSortie == 0,
    $"REC-006 : un ecart vu dans l'etat fige et disparu des deux bases vivantes a la relecture (recopie en cours) est compte « resorbe » ({rapportVol.Resorbes}), pas alerte " +
    $"({rapportVol.Ecarts} ecart)");

// CA03 : une demande ou une affaire V3 qui vise une fiche partagee sans correspondance.
var (affaireOrph, clientOrph) = await Lire(v3A.Connexion, "SELECT TOP (1) AFFRE_ID_AFFRE, AFFRE_ID_CLIEN FROM v2.AFFAIRE WHERE AFFRE_ID_CLIEN IS NOT NULL ORDER BY AFFRE_ID_AFFRE")
    is { Rows.Count: 1 } ao ? (Convert.ToInt64(ao.Rows[0][0]), Convert.ToInt64(ao.Rows[0][1])) : (0L, 0L);
var affairesDuClient = await Nombre(v3A.Connexion, $"SELECT COUNT(*) FROM v2.AFFAIRE WHERE AFFRE_ID_CLIEN = {clientOrph}");
await Executer(v3A.Connexion, $"DELETE FROM rec.Correspondance WHERE TableV2 = 'CLIENT' AND IdV2 = N'{clientOrph}';");
var rapportOrph = await ComparaisonBases.ComparerAsync(new OptionsComparaison(v2.Connexion, v3A.Connexion));
var orphelins = rapportOrph.Details.Where(d => d.Genre == "lien orphelin").ToList();
var orphelinAffaire = orphelins.FirstOrDefault(o => o.TableV2 == "AFFAIRE" && o.IdV3 == affaireOrph.ToString());
var clientSans = rapportOrph.Details.Any(d => d.Genre == "sans correspondance V2" && d.TableV2 == "CLIENT" && d.IdV2 == clientOrph.ToString())
                 && rapportOrph.Details.Any(d => d.Genre == "sans correspondance V3" && d.TableV2 == "CLIENT" && d.IdV3 == clientOrph.ToString());
await Executer(v3A.Connexion, "INSERT INTO rec.Correspondance (BaseV2, TableV2, IdV2, TableV3, IdV3, IdTransfert) " +
                              $"VALUES (N'{v2.Nom}', N'CLIENT', N'{clientOrph}', N'rec.Client', N'{clientOrph}', {idA});");
Exiger(orphelinAffaire is not null && orphelinAffaire.Colonnes == "AFFRE_ID_CLIEN" && orphelinAffaire.Detail!.Contains($"vise CLIENT {clientOrph}")
       && orphelins.Count(o => o.TableV2 == "AFFAIRE") == affairesDuClient && rapportOrph.Orphelins >= affairesDuClient && clientSans,
    $"REC-006 CA03 et T07 : une correspondance supprimee (CLIENT {clientOrph}) : l'affaire V3 {affaireOrph} qui le vise est listee « lien orphelin » ({orphelinAffaire?.Detail}), " +
    $"{orphelins.Count} lien(s) orphelin(s) en tout ; le client lui-meme est « sans correspondance » des deux cotes");

// CA04 (purge) : les rapports de plus de 90 jours partent avec leur detail, ceux de 89 jours restent.
await Executer(v3A.Connexion, """
    INSERT INTO rec.Comparaison (BaseV2, ServeurV2, Debut, Etat) VALUES (N'PURGE91', N'banc', DATEADD(day, -91, SYSDATETIME()), 'fini'), (N'PURGE89', N'banc', DATEADD(day, -89, SYSDATETIME()), 'fini');
    INSERT INTO rec.ComparaisonTable (IdComparaison, TableV2) SELECT Id, N'CLIENT' FROM rec.Comparaison WHERE BaseV2 IN (N'PURGE91', N'PURGE89');
    INSERT INTO rec.ComparaisonEcart (IdComparaison, Genre, TableV2) SELECT Id, 'ecart', N'CLIENT' FROM rec.Comparaison WHERE BaseV2 IN (N'PURGE91', N'PURGE89');
    """);
var rapportPurge = await ComparaisonBases.ComparerAsync(new OptionsComparaison(v2.Connexion, v3A.Connexion));
var restes = (await Lire(v3A.Connexion, "SELECT c.BaseV2, (SELECT COUNT(*) FROM rec.ComparaisonTable t WHERE t.IdComparaison = c.Id), (SELECT COUNT(*) FROM rec.ComparaisonEcart e WHERE e.IdComparaison = c.Id) " +
                                        "FROM rec.Comparaison c WHERE c.BaseV2 LIKE N'PURGE%'")).Rows.Cast<DataRow>().ToList();
var orphelinsDetails = await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM rec.ComparaisonTable t WHERE NOT EXISTS (SELECT 1 FROM rec.Comparaison c WHERE c.Id = t.IdComparaison)");
Exiger(rapportPurge.Purges == 1 && restes.Count == 1 && (string)restes[0][0] == "PURGE89" && Convert.ToInt32(restes[0][1]) == 1 && Convert.ToInt32(restes[0][2]) == 1 && orphelinsDetails == 0
       && rapportPurge.CodeSortie == 0,
    $"REC-006 CA04 et T08 (purge) : au controle suivant, le rapport de 91 jours est purge avec ses tables et ses ecarts ({rapportPurge.Purges}), celui de 89 jours reste ; aucun detail orphelin");

// CA05 : un etat fige, lu pendant que des agents ecrivent dans la V2.
var arretEcritures = new CancellationTokenSource();
var ecritures = new List<(long Ms, bool Bloquee)>();
var ids = (await Lire(v2.Connexion, "SELECT TOP (200) ARRV_ID_ARRV FROM dbo.ARRIVEE ORDER BY ARRV_ID_ARRV")).Rows.Cast<DataRow>().Select(r => Convert.ToInt64(r[0])).ToList();
var agents = Task.Run(async () =>
{
    var i = 0;
    while (!arretEcritures.IsCancellationRequested)
    {
        var chrono = Stopwatch.StartNew();
        try
        {
            await Executer(v2.Connexion, $"SET LOCK_TIMEOUT 2000; UPDATE dbo.ARRIVEE SET ARRV_CASE_COURRIER = ARRV_CASE_COURRIER WHERE ARRV_ID_ARRV = {ids[i++ % ids.Count]};");
            lock (ecritures) ecritures.Add((chrono.ElapsedMilliseconds, false));
        }
        catch (SqlException e) when (e.Number == 1222) { lock (ecritures) ecritures.Add((chrono.ElapsedMilliseconds, true)); }
        await Task.Delay(5);
    }
});
var chronoCA05 = Stopwatch.StartNew();
var rapportCA05 = await ComparaisonBases.ComparerAsync(new OptionsComparaison(v2.Connexion, v3A.Connexion) { Tables = ComparaisonBases.TablesPartagees });
var dureeCA05 = chronoCA05.ElapsedMilliseconds;
arretEcritures.Cancel();
await agents;
var lignesCA05 = rapportCA05.Tables.Sum(t => t.LignesV2);
// Le volume des fiches partagees de RIG_DEV (catalogue, sans lire les tables) : l'extrapolation de la duree d'un controle de nuit.
var volumeRigDev = await Nombre(chaineRigDev, "SELECT SUM(p.rows) FROM sys.partitions p JOIN sys.tables t ON t.object_id = p.object_id WHERE p.index_id IN (0, 1) AND t.name IN (" +
                                              string.Join(", ", ComparaisonBases.TablesPartagees.Select(t => $"'{t}'")) + ")");
var estimation = TimeSpan.FromMilliseconds(volumeRigDev / Math.Max(1.0, lignesCA05 / (double)dureeCA05));
Exiger(rapportCA05.Etat == "fini" && ecritures.Count > 0 && ecritures.Count(e => e.Bloquee) == 0 && ecritures.Max(e => e.Ms) < 2000 && estimation < TimeSpan.FromHours(4),
    $"REC-006 CA05 et T09 : le controle lit un etat fige : {ecritures.Count} ecritures d'agents dans la V2 pendant qu'il tourne, aucune bloquee (attente limitee a 2 s : 0 refus), " +
    $"la plus longue {ecritures.Max(e => e.Ms)} ms ; {lignesCA05} lignes en {dureeCA05} ms ({lignesCA05 * 1000.0 / dureeCA05:0} lignes/s) ; extrapole aux {volumeRigDev} lignes des " +
    $"fiches partagees de RIG_DEV : {estimation:hh\\:mm\\:ss}, avant l'ouverture (02 h -> 06 h)");
Reste("REC-006 CA05 sur les 40 bases, la nuit, sur le serveur de production : ni les bases ni le serveur ne sont sur ce poste ; l'estimation ci-dessus vient du poste de developpement");

// ====================================================================================================
// D. Le serveur : alertes (REC-005 CA05, REC-006 CA04) et controle de nuit planifie (REC-006 T04)
// ====================================================================================================
var v3C = await NouvelleV3("RecopieRepriseC");
var rapportCinc = await MoteurTransfert.TransfererAsync(new OptionsTransfert(v2.Connexion, v3C.Connexion)
{
    TailleLot = Lot,
    ApresLot = (table, lot, _, _) => table == "CLIENT" && lot == 2 ? throw new InvalidOperationException("serveur de la V2 redemarre (incident simule)") : Task.CompletedTask,
});
Exiger(rapportCinc.Etat == "interrompu", $"banc : transfert de la base du greffe 9302 interrompu ({rapportCinc.Motif})");
await using var commune = await BaseJetable.CreerAsync("RecopieRepriseCommune");
await Migrateur.AppliquerAsync(commune.Connexion, migrations);
var serveurBancs = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
var authentification = new SqlConnectionStringBuilder(serveurBancs.ConnectionString);
authentification.Remove("Data Source");
await Executer(commune.Connexion,
    "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, BaseV3CreeeLe, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe) VALUES " +
    $"('9301', N'Greffe du banc (controle)', '{serveurBancs.DataSource}', '{v2.Nom}', '{v3A.Nom}', SYSDATETIME(), 0, 0, 'Romance Standard Time', 'banc', SYSDATETIME()), " +
    // Une base V2 par greffe (UQ_Greffe_BaseV2) : 9302 n'a besoin que de sa base V3 (l'alerte la lit seule).
    $"('9302', N'Greffe du banc (incident)', '{serveurBancs.DataSource}', 'RIG_BANC_9302', '{v3C.Nom}', SYSDATETIME(), 0, 0, 'Romance Standard Time', 'banc', SYSDATETIME());");
await using var app = ServeurRig.Construire(["--environment", "Development"], s =>
{
    s.WebHost.UseTestServer();
    s.Logging.SetMinimumLevel(LogLevel.Warning);
    s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
    s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
    s.Configuration[SignatureDeFond.Reglage] = "0";
    s.Configuration["Supervision:Automatique"] = "false";
    s.Configuration[ReglagesPlanificateur.CleIntervalle] = "0";
});
await app.StartAsync();
var surveillant = app.Services.GetRequiredService<Surveillant>();
var canal = (CanalAlerteSimule)app.Services.GetRequiredService<ICanalAlerte>();
async Task<List<(string Regle, string? Greffe, string Erreur, bool Fermee)>> Alertes() =>
    [.. (await Lire(commune.Connexion, "SELECT Regle, CodeGreffe, Erreur, CASE WHEN FermeeLe IS NULL THEN 0 ELSE 1 END FROM soc.AlerteExploitation WHERE Regle LIKE 'recopie-%' ORDER BY Id"))
        .Rows.Cast<DataRow>().Select(r => ((string)r[0], r[1] as string, (string)r[2], Convert.ToInt32(r[3]) == 1))];

// REC-005 CA05 : un incident de 4 minutes n'alerte pas, un de 6 minutes alerte, en nommant la base, la table et l'erreur.
await Executer(v3C.Connexion, "UPDATE rec.Transfert SET Fin = DATEADD(minute, -4, SYSDATETIME())");
await surveillant.PasserAsync();
var a4 = await Alertes();
await Executer(v3C.Connexion, "UPDATE rec.Transfert SET Fin = DATEADD(minute, -6, SYSDATETIME())");
await surveillant.PasserAsync();
var a6 = await Alertes();
var incident = a6.FirstOrDefault(a => a.Regle == AlertesRecopie.RegleIncident && a.Greffe == "9302" && !a.Fermee);
var envoyee = canal.Recues.FirstOrDefault(a => a.Regle == AlertesRecopie.RegleIncident && a.CodeGreffe == "9302");
Exiger(!a4.Any(a => a.Regle == AlertesRecopie.RegleIncident) && incident != default && incident.Erreur.Contains($"base {v2.Nom}") && incident.Erreur.Contains("table CLIENT")
       && incident.Erreur.Contains("serveur de la V2 redemarre") && incident.Erreur.Contains("interrompu depuis 6 min") && envoyee is not null,
    $"REC-005 CA05 et T09 : un incident de recopie de 4 minutes n'alerte pas ; a 6 minutes (plus de 5), l'exploitation recoit une alerte (canal {canal.Nom}) qui nomme la base, " +
    $"la table et l'erreur : « {incident.Erreur} »");
var rapportCrep = await MoteurTransfert.TransfererAsync(new OptionsTransfert(v2.Connexion, v3C.Connexion) { TailleLot = Lot });
await surveillant.PasserAsync();
Exiger(rapportCrep.Etat == "fini" && rapportCrep.Passage == 1 && (await Alertes()).Single(a => a.Regle == AlertesRecopie.RegleIncident).Fermee,
    $"REC-005 : le transfert repris et fini (reprise {rapportCrep.Passage}), l'alerte d'incident se ferme au passage suivant de la supervision");
// REC-005 CA05 (second cas, changement MIS DE COTE sur la file continue, meme source AlertesRecopie) : prouve par
// tests\RecopieDeuxSensCheck (lot 9).

// REC-006 CA04 : un rapport avec ecart alerte ; le controle propre suivant ferme l'alerte.
await Executer(v2.Connexion, $"UPDATE dbo.CLIENT SET CLIEN_NOM = 'ECART NUIT' WHERE CLIEN_ID_CLIEN = {clientEcart};");
var rapportNuit = await ComparaisonBases.ComparerAsync(new OptionsComparaison(v2.Connexion, v3A.Connexion));
await surveillant.PasserAsync();
var ecartAlerte = (await Alertes()).FirstOrDefault(a => a.Regle == AlertesRecopie.RegleEcart && a.Greffe == "9301" && !a.Fermee);
await Executer(v2.Connexion, $"UPDATE dbo.CLIENT SET CLIEN_NOM = (SELECT Nom FROM [{v3A.Nom}].rec.Client WHERE Id = {clientEcart}) WHERE CLIEN_ID_CLIEN = {clientEcart};");
// CA05 a laisse des ecritures d'agents dans la V2 (ARRV_CASE_COURRIER remis a lui-meme : aucune valeur changee).
var rapportNuitPropre = await ComparaisonBases.ComparerAsync(new OptionsComparaison(v2.Connexion, v3A.Connexion));
await surveillant.PasserAsync();
Exiger(rapportNuit.Ecarts == 1 && ecartAlerte != default && ecartAlerte.Erreur.Contains("1 ecart(s)") && ecartAlerte.Erreur.Contains("CLIENT (1 ecarts")
       && !ecartAlerte.Erreur.Contains("ECART NUIT") && canal.Recues.Any(a => a.Regle == AlertesRecopie.RegleEcart)
       && rapportNuitPropre.ATraiter == 0 && (await Alertes()).Where(a => a.Regle == AlertesRecopie.RegleEcart).All(a => a.Fermee),
    $"REC-006 CA04 et T08 (alerte) : un rapport avec un ecart alerte l'exploitation (« {ecartAlerte.Erreur} » — tables et comptes, aucune valeur) ; le controle propre " +
    "suivant la ferme");

// REC-006 T04 : le controle de nuit inscrit au planificateur, du entre 02:00 et 06:00 heure du greffe, joue par le moteur des traitements.
// 9302 n'a pas de vraie base V2 : il sort du controle de nuit (base V3 « pas encore creee » au catalogue).
await Executer(commune.Connexion, "UPDATE soc.Greffe SET BaseV3CreeeLe = NULL, ModifiePar = 'banc', ModifieLe = SYSDATETIME() WHERE CodeGreffe = '9302';");
var planificateur = app.Services.GetRequiredService<PlanificateurNocturne>();
var moteur = app.Services.GetRequiredService<MoteurTraitements>();
var tache = app.Services.GetServices<ITachePlanifiee>().OfType<ComparaisonNocturne>().Single();
var catalogue = app.Services.GetRequiredService<CatalogueGreffes>();
var fiche9301 = (await catalogue.LireAsync("9301"))!;
var nuit = new DateTime(2026, 11, 21, 1, 30, 0, DateTimeKind.Utc);   // 02:30 a Paris (heure d'hiver)
var dejaFait = await tache.ChargesDuesAsync(fiche9301, nuit, default);
await Executer(v3A.Connexion, "UPDATE rec.Comparaison SET Debut = DATEADD(hour, -21, Debut)");
var avantHeure = await tache.ChargesDuesAsync(fiche9301, nuit.AddHours(-1), default);
var apresOuverture = await tache.ChargesDuesAsync(fiche9301, nuit.AddHours(4.5), default);
var due = await tache.ChargesDuesAsync(fiche9301, nuit, default);
var comparaisonsAvant = await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM rec.Comparaison");
var depots = await planificateur.PasserAsync(nuit);
var faits = await moteur.PasserAsync(ComparaisonNocturne.NomTraitement);
var nouvelle = (await Lire(v3A.Connexion, "SELECT TOP (1) Etat, Ecarts, Compte FROM rec.Comparaison ORDER BY Id DESC")).Rows[0];
Exiger(dejaFait.Count == 0 && avantHeure.Count == 0 && apresOuverture.Count == 0 && due.SequenceEqual(["2026-11-21"])
       && depots.Any(d => d.Traitement == ComparaisonNocturne.NomTraitement && d.Greffe == "9301" && d.Deposes == 1) && faits >= 1
       && await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM rec.Comparaison") == comparaisonsAvant + 1 && (string)nouvelle[0] == "fini" && moteur.Connait(ComparaisonNocturne.NomTraitement),
    $"REC-006 T04 : le controle de nuit est une tache du planificateur ({ComparaisonNocturne.NomTraitement}) : rien avant 02:00 ni apres 06:00 heure du greffe, rien si un controle " +
    $"a deja tourne depuis 20 h ; du a 02:30, il est depose ({depots.Count(d => d.Traitement == ComparaisonNocturne.NomTraitement && d.Deposes > 0)} greffe(s)) et joue par le moteur ({faits}) : " +
    $"un rapport de plus, « {nouvelle[0]} »");
await app.StopAsync();

// ====================================================================================================
// E. REC-008 : la repetition sur une copie anonymisee, son rapport et le feu vert
// ====================================================================================================
async Task<string> EmpreinteV2()
{
    var parts = new List<string>();
    foreach (DataRow r in (await Lire(v2.Connexion, "SELECT name FROM sys.tables WHERE is_ms_shipped = 0 ORDER BY name")).Rows)
        parts.Add($"{r[0]}:{await Valeur(v2.Connexion, $"SELECT CONCAT(COUNT_BIG(*), ':', CHECKSUM_AGG(BINARY_CHECKSUM(*))) FROM dbo.[{r[0]}]")}");
    return string.Join("|", parts);
}
// Une table SANS CLE PRIMAIRE et sans regle (RIG_DEV en a 104) : la repetition la copie par une regle provisoire, en une transaction.
await Executer(v2.Connexion, "CREATE TABLE dbo.JOURNAL_SANS_CLE_REC008 (Quand datetime NOT NULL, Quoi varchar(40) NULL); " +
                             "INSERT INTO dbo.JOURNAL_SANS_CLE_REC008 SELECT TOP (1234) ARRV_DATEHEURE, LEFT(ARRV_NOM, 40) FROM dbo.ARRIVEE;");
var dossierRep = Path.Combine(Path.GetTempPath(), "rigv3-repetition-banc-" + Guid.NewGuid().ToString("N")[..8]);
var empreinteSource = await EmpreinteV2();
var chronoRep = Stopwatch.StartNew();
var rep = await Repetition.RepeterAsync(new OptionsRepetition
{
    Sources = [v2.Connexion],
    DossierRapport = dossierRep,
    DossierMigrations = migrations,
    Echantillon = 1000,
    TailleLot = 5000,
    Graine = "banc-rec008",
});
var dureeRep = chronoRep.ElapsedMilliseconds;
var b0 = rep.Bases.Single();
var json = Directory.GetFiles(dossierRep, "repetition-*.json").SingleOrDefault();
var md = Directory.GetFiles(dossierRep, "repetition-*.md").SingleOrDefault();
var relu = json is null ? null : JsonSerializer.Deserialize<RapportRepetition>(await File.ReadAllTextAsync(json), RapportRepetition.Json);
var basesRestees = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM sys.databases WHERE name LIKE 'rigv3[_]rep[_]%' AND (name = '" + (b0.Copie ?? "") + "' OR name = '" + (b0.BaseV3 ?? "") + "')");
Info($"repetition : {dureeRep} ms ; " + string.Join(" | ", rep.Journal));
Exiger(b0.Etat == "fini" && b0.CopieAnonymisee && b0.ControleCopie!.StartsWith("vert") && b0.ControleCopie.Contains("retrouves dans la copie : aucun")
       && b0.ControleCopie.Contains("cles et numeros gardes changes : aucun") && b0.ControleCopie.Contains("volumes differents : aucun"),
    $"REC-008 CA01 et T05 : la copie anonymisee de la base est controlee avant de servir : {b0.ControleCopie}");
Exiger(b0.EtatTransfert == "fini" && b0.DureeTransfertMs > 0 && b0.Lignes > 0 && b0.Rejets is not null && b0.RetardRecopie.StartsWith("non mesure")
       && b0.Ecarts == 0 && b0.Orphelins == 0 && b0.SansCorrespondance == 0 && b0.LignesComparees > 0 && b0.Provisoires == 3 && b0.Tables == extrait.Lignes.Count + 1 + 2
       && relu is not null && relu.Bases.Single().DureeTransfertMs == b0.DureeTransfertMs && md is not null && (await File.ReadAllTextAsync(md)).Contains("Journal de préparation"),
    $"REC-008 CA02 et T06 : une repetition complete (anonymisation, transfert, controle, mesures) sur la copie anonymisee, en {dureeRep / 1000.0:0.0} s : son rapport " +
    $"({Path.GetFileName(json)} et .md) donne pour la base la duree du transfert ({b0.DureeTransfertMs} ms, {b0.Lignes} lignes, {b0.Tables} tables dont {b0.Provisoires} par une regle " +
    $"PROVISOIRE — la marque de l'anonymisation et une table sans cle primaire), les rejets par table et motif ({b0.Rejets.Count} groupe(s)), le retard de recopie (« {b0.RetardRecopie[..11]} ») et les ecarts du " +
    $"controle ({b0.Ecarts} ecart, {b0.Orphelins} orphelin, {b0.SansCorrespondance} sans correspondance sur {b0.LignesComparees} lignes)");
Reste("REC-008 CA02 (retard maximal de recopie) : la recopie continue (lot 9) et le simulateur d'une journee d'ecritures (REC-008 T04) manquent ; le rapport l'ecrit « non mesure » " +
      "et le feu vert le refuse");
Exiger(rep.Journal.Any(l => l.Contains("lue en lecture seule") && l.Contains("DEJA anonymisee") && l.Contains("aucune copie en clair n'est ecrite ni deplacee"))
       && rep.Journal.Any(l => l.Contains("marquee anonymisee")) && rep.Journal.Any(l => l.Contains("3 par une regle PROVISOIRE"))
       && await EmpreinteV2() == empreinteSource && basesRestees == 0,
    $"REC-008 CA05 et T09 : le journal de preparation dit que la source a ete LUE et la copie ecrite deja anonymisee, puis marquee avant de servir ; la base source est identique " +
    $"avant et apres (empreinte de ses {extrait.Lignes.Count} tables) ; la copie et la base V3 de la repetition sont supprimees a la fin");
// CA05 : une copie en clair donnee comme jeu de repetition est refusee avant toute lecture.
var refus = await Repetition.RepeterAsync(new OptionsRepetition { Copies = [v2.Connexion], DossierRapport = dossierRep, DossierMigrations = migrations });
var r0 = refus.Bases.Single();
Exiger(r0.Etat == "copie non anonymisee" && !r0.CopieAnonymisee && r0.BaseV3 is null && r0.IdTransfert is null && refus.Journal.Any(l => l.Contains("REFUS") && l.Contains("marque d'anonymisation")),
    $"REC-008 CA05 (contre-epreuve) : une copie NON anonymisee donnee comme jeu de repetition est refusee avant toute lecture, et le journal le dit : « {refus.Journal.FirstOrDefault(l => l.Contains("REFUS"))} »");

// CA03 : le feu vert. Sur le rapport reel : rouge (1 base sur 40, regles provisoires, recopie non mesuree).
var feuReel = Feu.Juger(rep, [], new OptionsFeu());
Exiger(!feuReel.Vert && feuReel.Rouges.Any(r => r.StartsWith("1 base(s) repetee(s) jusqu'au bout sur 40")) && feuReel.Rouges.Any(r => r.Contains("PROVISOIRE"))
       && feuReel.Rouges.Any(r => r.StartsWith("retard de recopie non mesure")),
    $"REC-008 CA03 : le feu du rapport reel est ROUGE : {string.Join(" ; ", feuReel.Rouges)}");
// Un rapport de 40 bases qui tient tout : vert ; le meme avec UN rejet sans regle de reprise : rouge (T07).
BaseRepetee Base(int i, IReadOnlyList<RejetsTable>? rejets = null) => b0 with
{
    Base = $"RIG_BANC_{i:00}", Provisoires = 0, Rejets = rejets ?? [], RetardMaxRecopieMs = 1200, DureeTransfertMs = 600_000,
};
RapportRepetition Rapport(DateTime le, IEnumerable<BaseRepetee> bases) => rep with { Debut = le, Fin = le.AddHours(8), Bases = [.. bases] };
var quarante = Rapport(DateTime.Now, Enumerable.Range(1, 40).Select(i => Base(i)));
var avecRejet = Rapport(DateTime.Now, Enumerable.Range(1, 40).Select(i => i == 7 ? Base(i, [new RejetsTable("DEMANDE", "lien casse : rejeter", 3, "lien casse : FK_X", null)]) : Base(i)));
var avecRegle = Rapport(DateTime.Now, Enumerable.Range(1, 40).Select(i => i == 7 ? Base(i, [new RejetsTable("DEMANDE", "lien casse : rejeter", 3, "lien casse : FK_X", "ressaisies par le greffe")]) : Base(i)));
var (fVert, fRejet, fRegle) = (Feu.Juger(quarante, [], new OptionsFeu()), Feu.Juger(avecRejet, [], new OptionsFeu()), Feu.Juger(avecRegle, [], new OptionsFeu()));
var heure40 = Rapport(DateTime.Now, Enumerable.Range(1, 40).Select(i => Base(i) with { DureeTransfertMs = 3_600_000 }));
var (a4ok, trop) = (Feu.Juger(heure40, [], new OptionsFeu(Parallele: 4)), Feu.Juger(heure40, [], new OptionsFeu(Parallele: 3)));
Exiger(fVert.Vert && !fRejet.Vert && fRejet.Rouges.Single().Contains("rejet(s) sans regle de reprise ecrite : RIG_BANC_07 DEMANDE (lien casse : rejeter, 3 lignes)") && fRegle.Vert
       && a4ok.Vert && !trop.Vert && trop.Rouges.Single().StartsWith("fenetre de nuit depassee"),
    $"REC-008 CA03 et T07 : 40 bases qui tiennent tout : feu VERT ; une seule avec un rejet sans regle de reprise ecrite : ROUGE (« {fRejet.Rouges.FirstOrDefault()} ») ; la regle ecrite, " +
    $"VERT ; 40 bases d'une heure, 4 a la fois (10 h) dans une fenetre de 10 h : VERT, 3 a la fois : ROUGE (« {trop.Rouges.FirstOrDefault()} »)");
// CA04 : la derniere repetition complete a moins de 30 jours au jour J.
var jourJ = DateOnly.FromDateTime(DateTime.Today).AddDays(60);
var il29 = Rapport(jourJ.AddDays(-29).ToDateTime(TimeOnly.MinValue), quarante.Bases);
var il31 = Rapport(jourJ.AddDays(-31).ToDateTime(TimeOnly.MinValue), quarante.Bases);
var partielle = Rapport(jourJ.AddDays(-2).ToDateTime(TimeOnly.MinValue), quarante.Bases.Take(39));
var (j29, j31) = (Feu.Juger(il29, [], new OptionsFeu(JourJ: jourJ)), Feu.Juger(partielle, [il31], new OptionsFeu(JourJ: jourJ)));
Exiger(j29.Vert && !j31.Vert && j31.Rouges.Any(r => r.Contains("derniere repetition complete le") && r.Contains("31 jours avant (30 au plus)")),
    $"REC-008 CA04 et T08 : jour J le {jourJ:yyyy-MM-dd} : derniere repetition complete 29 jours avant, VERT ; 31 jours avant (et une plus recente incomplete, 39 bases), " +
    $"ROUGE : « {j31.Rouges.FirstOrDefault(r => r.StartsWith("jour J"))} »");
// Une base ENTIERE en tables mais reduite en lignes (--max-lignes) : le controle de la copie se juge sur la reduction, et le feu
// la refuse (sa duree n'est pas celle de la base).
var repReduite = await Repetition.RepeterAsync(new OptionsRepetition
{
    Sources = [v2.Connexion], DossierRapport = dossierRep, DossierMigrations = migrations, MaxLignes = 100, Echantillon = 200, TailleLot = 5000, Graine = "banc-rec008",
});
var br = repReduite.Bases.Single();
var feuReduit = Feu.Juger(quarante with { Bases = [.. quarante.Bases.Select(b => b with { MaxLignes = 100 })] }, [], new OptionsFeu());
Exiger(br.MaxLignes == 100 && br.CopieAnonymisee && br.ControleCopie!.StartsWith("vert") && br.ControleCopie.Contains("copie REDUITE a 100 lignes par table")
       && !br.ControleCopie.Contains("HORS REDUCTION") && !feuReduit.Vert && feuReduit.Rouges.Single().StartsWith("copie(s) REDUITE(S)"),
    $"REC-008 (copie reduite, --max-lignes 100) : le controle de la copie se juge sur la reduction (« {br.ControleCopie?[..Math.Min(170, br.ControleCopie.Length)]}… ») ; " +
    $"40 bases parfaites mais reduites : feu ROUGE (« {feuReduit.Rouges.FirstOrDefault()} »)");
// Depuis le lot 18, une copie reduite est FERMEE sur ses cles etrangeres (Outils\Anonymisation\Fermeture.cs) : la repetition d'une
// base reduite va au bout (au lot 8, la reduction non fermee s'arretait dans la reprise des comptes et droits, SOC-002). Mesure sur
// RIG_DEV entier : docs\reconstruction\recopie.md.
Exiger(br.Etat == "fini" && br.ControleCopie?.Contains("par la fermeture sur les cles") == true,
    $"REC-008 (lot 18) : base entiere en copie reduite FERMEE sur ses cles : la repetition va au bout (etat {br.Etat}" +
    $"{(br.Motif is null ? "" : ", « " + br.Motif[..Math.Min(140, br.Motif.Length)] + " »")})");
Reste("REC-008 sur les 40 bases des greffes : leur lecture demande l'accord pour chaque base (regle d'acces SQL des agents) et se fait chez Amitel, l'anonymisation sur le " +
      "serveur du greffe ; RigV3.Repetition repeter --sources <liste> --accord <BASE> le fait ; ce banc le prouve sur une base du poste");
try { Directory.Delete(dossierRep, recursive: true); } catch (IOException) { }

// ====================================================================================================
// F. REC-007 CA07 (l'outil) : RigV3.Repetition supprimer n'efface qu'une base gardee par --garder (rigv3_rep_...)
// ====================================================================================================
// Lancee comme un PROCESSUS (le chemin de l'exploitant) sur deux bases vides du serveur des bancs : un temoin jetable
// (rigv3_banc_..., une base de banc, pas de repetition) qui doit rester, et une base au nom d'une base gardee qui doit partir.
// Contre-epreuve faite le 2026-11-23 : le prefixe exige elargi a « rigv3_ » (defaut reinjecte), le temoin est efface : rouge.
await using var temoinSupprimer = await BaseJetable.CreerAsync("RecopieRepriseSupprimer");
var gardee = $"rigv3_rep_banc_supprimer_{Guid.NewGuid().ToString("N")[..8]}";
var masterBancs = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs()) { InitialCatalog = "master" }.ConnectionString;
await Executer(masterBancs, $"CREATE DATABASE [{gardee}] COLLATE {BaseJetable.Collation};");
async Task<(int Code, string Sortie)> SupprimerParLOutil(string nom)
{
    var d = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "RigV3.Repetition.exe"))
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    foreach (var a in new[] { "supprimer", "--base", nom }) d.ArgumentList.Add(a);
    using var p = Process.Start(d)!;
    var (sortie, erreur) = (p.StandardOutput.ReadToEndAsync(), p.StandardError.ReadToEndAsync());
    await p.WaitForExitAsync();
    return (p.ExitCode, (await sortie + await erreur).Trim());
}
try
{
    var refusBanc = await SupprimerParLOutil(temoinSupprimer.Nom);
    var refusCaractere = await SupprimerParLOutil(gardee + "-x");
    var rienEfface = await BaseJetable.ExisteAsync(temoinSupprimer.Nom) && await BaseJetable.ExisteAsync(gardee);
    var accepte = await SupprimerParLOutil(gardee);
    var gardeePartie = !await BaseJetable.ExisteAsync(gardee);
    Exiger(refusBanc.Code == 2 && refusBanc.Sortie.Contains("n'est pas une base de repetition") && refusCaractere.Code == 2 && rienEfface
           && accepte.Code == 0 && gardeePartie && await BaseJetable.ExisteAsync(temoinSupprimer.Nom),
        $"REC-007 CA07 (outil) : RigV3.Repetition supprimer REFUSE une base qui n'est pas gardee par une repetition — le temoin {temoinSupprimer.Nom} " +
        $"(code {refusBanc.Code}, « {refusBanc.Sortie} ») et un nom rigv3_rep_ avec un caractere hors lettres, chiffres et _ (code {refusCaractere.Code}) — " +
        $"sans rien effacer ; la base gardee {gardee} est supprimee (code {accepte.Code}, « {accepte.Sortie} »)");
}
finally
{
    await Executer(masterBancs, $"IF DB_ID(N'{gardee}') IS NOT NULL BEGIN ALTER DATABASE [{gardee}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{gardee}]; END");
}
}
finally
{
    // Un instantane restant (banc rouge en route) empecherait de supprimer la base V2 jetable.
    await Executer(v2.Connexion, "DECLARE @s nvarchar(max) = N''; SELECT @s += N'DROP DATABASE ' + QUOTENAME(name) + N'; ' FROM sys.databases WHERE source_database_id = DB_ID(); " +
                                 "USE master; EXEC (@s);");
    foreach (var b in v3s)
    {
        await Executer(b.Connexion, "DECLARE @s nvarchar(max) = N''; SELECT @s += N'DROP DATABASE ' + QUOTENAME(name) + N'; ' FROM sys.databases WHERE source_database_id = DB_ID(); " +
                                    "USE master; EXEC (@s);");
        await b.DisposeAsync();
    }
}

Console.WriteLine(echecs.Count == 0 ? "RecopieRepriseCheck : VERT" : $"RecopieRepriseCheck : ROUGE ({echecs.Count} echec(s))");
return echecs.Count == 0 ? 0 : 1;
