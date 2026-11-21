using System.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using RecopieTransfertCheck;
using RigV3.Base;
using RigV3.Contrats.Recopie;
using RigV3.Contrats.Socle;
using RigV3.Serveur;
using RigV3.Serveur.Recopie;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Chaines;
using RigV3.Serveur.Socle.Identite;
using RigV3.Serveur.Socle.Supervision;
using RigV3Desktop;
using RigV3Desktop.Pont;
using BilanPassage = RigV3.Serveur.Recopie.BilanPassage;

// Banc de REC-002, REC-003 et REC-004 (carnet\recopie.json) : la recopie continue entre la base V2 et la base V3 d'un
// greffe, dans les deux sens, et les fiches modifiees des deux cotes. Lot 9 du plan de reconstruction, phase 2
// (2026-11-21). Un controle par critere, qui cite son id.
//
//  0. Une base V2 jetable tiree de RIG_DEV (ExtraitRigDev, RIG_DEV lu en lecture seule), ses deux declencheurs de
//     transmission REACTIVES ; Base\v2\recopie-v2.sql joue dessus (le suivi des changements) ; le transfert (REC-001)
//     dans une base V3 jetable ; la mise en service de la recopie (PontRecopie).
//  A. Les passages un a un (ServiceRecopie.PasserAsync, ce que fait la boucle du serveur) : creation, ordre, transmission,
//     pas d'aller-retour, V2 hors ligne, ecritures de la V2, V3 arretee, conflits ; REC-005 sur la file (decision du chef du
//     21/11) : passage coupe puis repris, changement rejoue, refus mis de cote au 3e essai et relance, dans les deux sens.
//  B. La vraie boucle du serveur (RecopieContinue, « Recopie:Continue ») : le delai d'une fiche dans chaque sens.
//  C. Le vrai serveur en memoire et l'ecran (Pont/EcranConflits.cs) : l'alerte, la liste, « Traité », le rouge ; l'alerte
//     sur l'ACCUEIL de l'agent designe (Pont/Accueil.cs) et son clic dans le plan de travail ; les libelles d'agent ;
//     REC-005 CA05 : les alertes d'exploitation (surveillant de la supervision) et la relance par la route d'Amitel.
// Il lui faut « docker compose up -d » et l'acces en lecture a RIG_DEV (autre serveur : RIGV3_RIG_DEV).
// Doc : docs/reconstruction/recopie.md, section « La recopie continue ».
// fix-ok: banc NEUF du lot 9, retouche avant son premier essai : (1) le SQL Server des bancs est a l'heure UTC (mesure
// 2026-11-21 : SYSDATETIME() 08:52, poste 09:52) -- les dates comparees viennent de son horloge, plus de DateTime.Now ;
// (2) l'ordre de CA06 se lit par fiche (les lignes d'une meme fiche passent ensemble), pas ligne a ligne ; (3) le pool de
// connexions est vide apres la remise en ligne d'une base coupee.
// fix-ok: refus du chef (21/11, rejoue sur main 40624c2) : un changement refuse a chaque essai arretait toute la recopie du
// greffe (Applicateur.cs, « return » au premier echec ; Remontee.cs, idem) -- REC-005 CA01-CA03 et CA05 s'ajoutent ici sur
// la file continue, avec l'alerte de l'accueil (REC-004 CA02) et les libelles d'agent de l'ecran.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var migrations = Path.Combine(racine, "Base", "migrations");
var scriptV2 = Path.Combine(racine, "Base", "v2", "recopie-v2.sql");
var chaineRigDev = Environment.GetEnvironmentVariable("RIGV3_RIG_DEV") is { Length: > 0 } c0 ? c0 :
    @"Server=SQL-DEV\DEV;Database=RIG_DEV;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;ApplicationIntent=ReadOnly;Connect Timeout=20";
const string AgentV3 = "Agent V3 du banc";

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
// Aucune ligne (ou NULL) rend null, pas "" : Convert.ToString(null) rend une chaine vide, et un controle « absente » (is null)
// ne pouvait alors jamais etre vrai (mesure le 2026-11-21 sur REC-005 « fiche visee » : correspondance vide, V3De rendait "").
async Task<string?> Texte(string chaine, string sql) =>
    await Valeur(chaine, sql) is { } v ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null;
async Task<long> Nombre(string chaine, string sql) => Convert.ToInt64(await Valeur(chaine, sql) ?? 0L);
async Task Executer(string chaine, string sql)
{
    await using var cn = new SqlConnection(chaine);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
    await cmd.ExecuteNonQueryAsync();
}

// ====================================================================================================
// 0. RIG_DEV, la base V2 jetable, le suivi des changements, le transfert, la mise en service
// ====================================================================================================
try
{
    LectureV2.ExigerLecture("SELECT @@SERVERNAME");
    Info($"RIG_DEV : {await Valeur(chaineRigDev, "SELECT @@SERVERNAME + ' / ' + DB_NAME()")}");
}
catch (SqlException e)
{
    Exiger(false, $"RIG_DEV lisible ({new SqlConnectionStringBuilder(chaineRigDev).DataSource}) : {e.Message} -- il faut l'acces en lecture a SQL-DEV\\DEV (ou RIGV3_RIG_DEV)");
    return 1;
}

// Les regles du transfert prises explicitement (celles de REC-001 et les reprises de la phase 1) : le banc ne depend pas
// des regles que d'autres lots ajoutent pour d'autres tables. Et sous leur forme TELLE QUELLE (v2.X) : ce banc eprouve le
// moteur de la recopie sur les copies telles quelles, forme toujours valide (ARRIVEE, et toute fiche avant sa story) ; depuis
// le lot 10, les fiches qui ont leur table dans la V3 (rec.Client, soc.Compte...) ont leur banc, RecopieFichesCheck.
var reglesServeur = ReglesTransfert.Decouvrir();
var tablesRec001 = new[] { "AFFAIRE", "DEMANDE", "ACTION_RCS", "ADRESSE_BASE", "ARRIVEE", "CATEGORIE", "CLIENT", "DOSSIER_RCS", "INSCRIPTION" };
var reprisesPhase1 = reglesServeur.Reprises.Where(r => r.Story is not ("REC-016" or "REC-019")).ToList();
var reglesTransfert = reglesServeur.Regles
    .Where(r => r.Sorte == SorteRegle.Reprise && reprisesPhase1.Any(p => p.Nom == r.Reprise) || tablesRec001.Contains(r.Table, StringComparer.OrdinalIgnoreCase))
    .Select(r => r.Cible is not null ? r with { Cible = null }
                 : r.Sorte == SorteRegle.Reprise && !reprisesPhase1.Any(p => p.Nom == r.Reprise) ? RegleTable.TelleQuelle(r.Table, "REC-001", [.. r.Colonnes]) : r)
    .ToList();
var entieres = reglesTransfert.Where(r => r.Sorte == SorteRegle.Reprise).Select(r => r.Table).Append("CATEGORIE").Distinct().ToList();
var extraits = new Dictionary<string, string>
{
    ["AFFAIRE"] = "SELECT TOP (400) * FROM dbo.AFFAIRE ORDER BY AFFRE_ID_AFFRE DESC",
    ["DEMANDE"] = "SELECT * FROM dbo.DEMANDE WHERE DMND_ID_AFFRE IN (SELECT TOP (400) AFFRE_ID_AFFRE FROM dbo.AFFAIRE ORDER BY AFFRE_ID_AFFRE DESC)",
    ["ACTION_RCS"] = "SELECT TOP (1000) * FROM dbo.ACTION_RCS ORDER BY ACTRC_ID_ACTRC DESC",
    ["ADRESSE_BASE"] = "SELECT TOP (2000) * FROM dbo.ADRESSE_BASE ORDER BY ADRBS_ID_ADRBS DESC",
    ["ARRIVEE"] = "SELECT TOP (2000) * FROM dbo.ARRIVEE ORDER BY ARRV_ID_ARRV DESC",
    ["CLIENT"] = "SELECT TOP (1000) * FROM dbo.CLIENT ORDER BY CLIEN_ID_CLIEN DESC",
    ["DOSSIER_RCS"] = "SELECT TOP (1000) * FROM dbo.DOSSIER_RCS ORDER BY DSSRC_ID_DSRCS DESC",
    ["INSCRIPTION"] = "SELECT TOP (1000) * FROM dbo.INSCRIPTION ORDER BY INEDT_ID_INSCR DESC",
};
var reglesRecopie = ReglesRecopie.Decouvrir().Where(r => tablesRec001.Contains(r.TableV2, StringComparer.OrdinalIgnoreCase))
    .Select(r => r.ColonneParColonne ? r.EnTelleQuelle() : r).ToList();
var partagees = reglesRecopie.Select(r => r.TableV2).ToList();
Exiger(partagees.Count == 7 && partagees.All(t => tablesRec001.Contains(t)),
    $"REC-002 T03 : une regle de recopie par fiche partagee qui a sa table dans la V3 ({string.Join(", ", partagees)}) ; les autres tables_v2 de REC-002 n'ont pas encore de regle de transfert");

await using var v2 = await BaseJetable.CreerAsync("RecopieDeuxSensV2");
var chrono = Stopwatch.StartNew();
var extrait = await ExtraitRigDev.ConstruireAsync(chaineRigDev, v2.Connexion, entieres, extraits, Info);
Info($"base V2 jetable {v2.Nom} : {extrait.Lignes.Values.Sum()} lignes dans {extrait.Lignes.Count} tables, {extrait.Cles} cles etrangeres verifiees, " +
     $"en {chrono.Elapsed.TotalSeconds:0} s ; " + string.Join(", ", extrait.Lignes.Where(l => extraits.ContainsKey(l.Key)).Select(l => $"{l.Key} {l.Value}")));

// Deux fiches a l'etat de transmission « F » (une valeur de RIG_DEV : M, G, F, Z, N lus le 2026-11-21), posees AVANT que les
// declencheurs ne vivent : un dossier et une inscription pour REC-002 CA04, un dossier temoin.
var dossiersF = (await Lire(v2.Connexion, "SELECT TOP (3) DSSRC_ID_DSRCS FROM dbo.DOSSIER_RCS ORDER BY DSSRC_ID_DSRCS")).Rows.Cast<DataRow>().Select(r => Convert.ToString(r[0])!).ToList();
var inscriptionF = await Texte(v2.Connexion, "SELECT TOP (1) INEDT_ID_INSCR FROM dbo.INSCRIPTION ORDER BY INEDT_ID_INSCR");
await Executer(v2.Connexion, $"UPDATE dbo.DOSSIER_RCS SET DSSRC_ETAT_TRANSMISSION = 'F', DSSRC_DATE_MODIF_FLAG_TRANS = '2026-01-02T03:04:05' WHERE DSSRC_ID_DSRCS IN ({string.Join(", ", dossiersF)});" +
                             $"UPDATE dbo.INSCRIPTION SET INEDT_ETAT_TRANSMISSION = 'F', INEDT_DATE_MODIF_FLAG_TRANS = '2026-01-02T03:04:05' WHERE INEDT_ID_INSCR = {inscriptionF};");
await Executer(v2.Connexion, "ENABLE TRIGGER [T_DOSSIER_RCS_MAJ_ETAT_TRANSMISSION] ON dbo.DOSSIER_RCS; ENABLE TRIGGER [T_INSCRIPTION_MAJ_ETAT_TRANSMISSION] ON dbo.INSCRIPTION;");

// REC-003 CA02 : les programmes de la V2 (procedures, fonctions, vues, declencheurs) et ses colonnes, avant / apres.
async Task<string> ProgrammesV2() => string.Join("|",
    (await Lire(v2.Connexion, "SELECT o.name, o.type, CONVERT(varchar(64), HASHBYTES('SHA2_256', ISNULL(m.definition, N'')), 2), " +
                              "CONVERT(varchar(5), ISNULL(OBJECTPROPERTY(o.object_id, 'ExecIsTriggerDisabled'), 0)) " +
                              "FROM sys.objects o LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id " +
                              "WHERE o.is_ms_shipped = 0 AND o.type IN ('P', 'V', 'FN', 'IF', 'TF', 'TR') ORDER BY o.name")).Rows.Cast<DataRow>()
        .Select(r => string.Join(",", r.ItemArray))
    .Concat((await Lire(v2.Connexion, "SELECT t.name, c.name, TYPE_NAME(c.user_type_id), c.max_length, c.is_nullable FROM sys.columns c " +
                                      "JOIN sys.tables t ON t.object_id = c.object_id WHERE t.is_ms_shipped = 0 ORDER BY t.name, c.column_id")).Rows.Cast<DataRow>()
        .Select(r => string.Join(",", r.ItemArray))));
var programmesAvant = await ProgrammesV2();
await Executer(v2.Connexion, "EXEC (N'CREATE PROCEDURE dbo.TEMOIN_REC003 AS SELECT 1');");
var programmesTemoin = await ProgrammesV2();
await Executer(v2.Connexion, "DROP PROCEDURE dbo.TEMOIN_REC003;");

var sortieScript = await NumerotationV2Check.Sqlcmd.JouerAsync(v2.Connexion, scriptV2, new Dictionary<string, string>
{
    ["BaseV2"] = v2.Nom, ["RetentionJours"] = "7", ["Tables"] = string.Join(",", partagees),
});
var suivies = (await Lire(v2.Connexion, "SELECT t.name FROM sys.change_tracking_tables c JOIN sys.tables t ON t.object_id = c.object_id")).Rows.Cast<DataRow>()
    .Select(r => (string)r[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
var retention = await Texte(v2.Connexion, "SELECT CONCAT(retention_period, ' ', retention_period_units_desc) FROM sys.change_tracking_databases WHERE database_id = DB_ID()");
Exiger(partagees.All(suivies.Contains) && suivies.Count == partagees.Count && retention == "7 DAYS" && sortieScript.Resultats.Count == 2,
    $"REC-003 T01 : Base\\v2\\recopie-v2.sql active le suivi des changements de SQL Server sur la base ({retention}) et sur ses {suivies.Count} tables partagees, et seulement elles");

await using var v3 = await BaseJetable.CreerAsync("RecopieDeuxSensV3");
await Migrateur.AppliquerAsync(v3.Connexion, migrations);
var transfert = await MoteurTransfert.TransfererAsync(new OptionsTransfert(v2.Connexion, v3.Connexion)
    { Regles = reglesTransfert, Reprises = reprisesPhase1, TailleLot = 5000 });
Exiger(transfert.Etat == "fini" && transfert.VersionSuivi is not null,
    $"REC-001 prealable : la base V2 transferee dans la V3 (transfert {transfert.IdTransfert} « {transfert.Etat} », version de suivi {transfert.VersionSuivi}) {transfert.Motif}");
if (transfert.Etat != "fini") return Bilan();

// La premiere mise en service avec ces regles telles quelles ; le serveur (parties B et C) recopie ensuite la base avec les
// regles de sa mise en service (rec.FicheEnService).
var fiches = await FichesRecopiees.ChargerAsync(v3.Connexion, v2.Nom, reglesRecopie, default);
var mes = await PontRecopie.MettreEnServiceAsync(v3.Connexion, v2.Nom, fiches);
Exiger(fiches.Problemes.Count == 0 && mes.Faite && mes.Declencheurs.Count == partagees.Count && mes.Version == transfert.VersionSuivi
       && fiches.ParTableV2["ACTION_RCS"].Liens.ContainsKey("ACTRC_ID_DSRCS") && fiches.ParTableV2["INSCRIPTION"].Liens.ContainsKey("INEDT_ID_ADRBS"),
    $"mise en service : {mes.Declencheurs.Count} declencheurs de file ({string.Join(", ", mes.Declencheurs)}), remontee au depart de la version {mes.Version} du transfert ; " +
    $"liens entre fiches partagees lus dans le catalogue : {string.Join(", ", fiches.Tables.SelectMany(t => t.Liens.Select(l => $"{t.NomV2}.{l.Key} -> {l.Value.NomV2}")))} " +
    $"{string.Join(" ; ", fiches.Problemes.Concat(mes.Refus))}");
var dejaEnService = await PontRecopie.MettreEnServiceAsync(v3.Connexion, v2.Nom, fiches);
Exiger(dejaEnService.Faite && await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.Remontee") == 1,
    "mise en service rejouee : les declencheurs sont reecrits, le point de reprise n'est pas touche");

// --- outils du banc -----------------------------------------------------------------------------------------------
var serveurBancs = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs()) { InitialCatalog = "master" }.ConnectionString;
async Task<BilanPassage> Passer() => await ServiceRecopie.PasserAsync(v3.Connexion, v2.Nom, fiches);
TableRecopiee T(string table) => fiches.ParTableV2[table];
string Cle(string table) => T(table).Cle.Nom;
string V3Ecrit(string sql) => $"EXEC sys.sp_set_session_context @key = N'rigv3.agent', @value = N'{AgentV3}'; {sql}";
// Les colonnes texte d'une fiche qu'un agent saisit (ni lien, ni nom, ni date, ni propre), pour y ecrire des valeurs reconnaissables.
List<ColonneV2> Textes(string table) => T(table).Colonnes
    .Where(c => c.TypeBase is "varchar" or "nvarchar" or "char" or "nchar" && (c.Longueur < 0 || (c.TypeBase.StartsWith('n') ? c.Longueur / 2 : c.Longueur) >= 4)
                && !T(table).Liens.ContainsKey(c.Nom) && !string.Equals(c.Nom, T(table).Regle.ColonneNom, StringComparison.OrdinalIgnoreCase)
                && !c.Nom.Contains("DATE", StringComparison.OrdinalIgnoreCase) && !c.Nom.Contains("SIREN", StringComparison.OrdinalIgnoreCase)
                && !c.Nom.Contains("ETAT", StringComparison.OrdinalIgnoreCase)).ToList();
int Taille(ColonneV2 c) => c.Longueur < 0 ? 40 : Math.Min(40, c.TypeBase.StartsWith('n') ? c.Longueur / 2 : c.Longueur);
string Val(ColonneV2 c, string marque) => marque.Length <= Taille(c) ? marque : marque[..Taille(c)];
// Les fiches de chaque table, une par controle, jamais reprises d'un controle a l'autre.
var reserve = new Dictionary<string, Queue<string>>();
foreach (var t in partagees)
    reserve[t] = new Queue<string>((await Lire(v3.Connexion, $"SELECT TOP (200) CONVERT(nvarchar(200), {Cle(t)}) FROM v2.[{t}] ORDER BY {Cle(t)} DESC"))
        .Rows.Cast<DataRow>().Select(r => (string)r[0]).Where(id => !dossiersF.Contains(id) || t != "DOSSIER_RCS"));
string Prendre(string t) => reserve[t].Dequeue();
// Une fiche recopiee par un agent : la meme ligne, sous un nouvel identifiant, avec des valeurs changees.
async Task<string> Dupliquer(string chaine, string tableSql, TableV2 table, string id, string? remplacer = null, string prefixe = "")
{
    var cle = table.ClePrimaire[0];
    var cols = table.Colonnes.Where(c => !c.Identite && !c.Calculee && c.TypeBase != "timestamp").Select(c => c.Nom).ToList();
    var valeurs = cols.Select(c => remplacer is not null && remplacer.Split(';').FirstOrDefault(r => r.StartsWith(c + "=", StringComparison.Ordinal)) is { } r ? r[(c.Length + 1)..] : $"[{c}]");
    return (await Texte(chaine, $"{prefixe} DECLARE @ids TABLE (Id nvarchar(200)); INSERT INTO {tableSql} ({string.Join(", ", cols.Select(c => $"[{c}]"))}) " +
                                $"OUTPUT CONVERT(nvarchar(200), inserted.[{cle}]) INTO @ids SELECT {string.Join(", ", valeurs)} FROM {tableSql} WHERE [{cle}] = {id}; SELECT Id FROM @ids;"))!;
}
async Task<string?> V2De(string table, string idV3) => await Texte(v3.Connexion,
    $"SELECT IdV2 FROM rec.Correspondance WHERE TableV3 = 'v2.{table}' AND IdV3 = '{idV3}' AND BaseV2 = '{v2.Nom}' AND TableV2 = '{table}'");
async Task<string?> V3De(string table, string idV2) => await Texte(v3.Connexion,
    $"SELECT IdV3 FROM rec.Correspondance WHERE BaseV2 = '{v2.Nom}' AND TableV2 = '{table}' AND IdV2 = '{idV2}'");
async Task<string?> EnV2(string table, string colonne, string idV2) => await Texte(v2.Connexion, $"SELECT CONVERT(nvarchar(400), [{colonne}]) FROM dbo.[{table}] WHERE [{Cle(table)}] = {idV2}");
async Task<string?> EnV3(string table, string colonne, string idV3) => await Texte(v3.Connexion, $"SELECT CONVERT(nvarchar(400), [{colonne}]) FROM v2.[{table}] WHERE [{Cle(table)}] = {idV3}");
async Task<long> EnAttente() => await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.FileRecopie WHERE Etat = 'en attente'");
// Les colonnes recopiees de la fiche, identiques des deux cotes (oracle independant du moteur : EXCEPT de SQL Server, liens
// traduits par la correspondance).
async Task<long> Differentes(string table, string idV2, string idV3)
{
    var t = T(table);
    string Col(string alias, ColonneV2 c, bool v3) => t.Liens.TryGetValue(c.Nom, out var p) && v3
        ? $"ISNULL((SELECT TOP 1 CONVERT({c.TypeSql}, k.IdV2) FROM rec.Correspondance k WHERE k.TableV3 = 'v2.{p.NomV2}' AND k.IdV3 = CONVERT(nvarchar(200), {alias}.[{c.Nom}])), {alias}.[{c.Nom}]) AS [{c.Nom}]"
        : $"{alias}.[{c.Nom}]";
    var a = string.Join(", ", t.Colonnes.Select(c => Col("x", c, false)));
    var b = string.Join(", ", t.Colonnes.Select(c => Col("y", c, true)));
    return await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM (SELECT {a} FROM [{v2.Nom}].dbo.[{table}] x WHERE x.[{t.Cle.Nom}] = {idV2} " +
                                      $"EXCEPT SELECT {b} FROM v2.[{table}] y WHERE y.[{t.Cle.Nom}] = {idV3}) d") +
           await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM (SELECT {b} FROM v2.[{table}] y WHERE y.[{t.Cle.Nom}] = {idV3} " +
                                      $"EXCEPT SELECT {a} FROM [{v2.Nom}].dbo.[{table}] x WHERE x.[{t.Cle.Nom}] = {idV2}) d");
}

var depart = await Passer();
Exiger(depart.EnService && depart.Erreur is null && depart.Appliquees == 0 && depart.Remontees == 0,
    $"premier passage juste apres le transfert : rien a recopier ni a remonter ({depart.Appliquees} appliquees, {depart.Remontees} remontees) {depart.Erreur}");

var ecartsProvoques = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
BaseRigApp? appB = null;
Microsoft.AspNetCore.Builder.WebApplication? appC = null;
try
{
// ====================================================================================================
// A. REC-002 — recopier dans l'ancien RIG chaque changement fait dans le nouveau
// ====================================================================================================

// CA02 : un client cree dans la V3. Un agent de la V2 en cree un juste avant : les deux identites donnent le meme numero,
// la V2 doit donc donner le sien a la fiche de la V3, et la correspondance le garder.
var clientBase = Prendre("CLIENT");
var clientDeLaV2 = await Dupliquer(v2.Connexion, "dbo.CLIENT", T("CLIENT").V2, clientBase, "CLIEN_NOM='CREE PAR UN AGENT V2'");
var clientV3 = await Dupliquer(v3.Connexion, "v2.CLIENT", T("CLIENT").V3, clientBase, "CLIEN_NOM='CREE DANS LA V3'", V3Ecrit(""));
var clientsV2Avant = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.CLIENT");
var p1 = await Passer();
var clientV2 = await V2De("CLIENT", clientV3);
var origine = await Texte(v3.Connexion, $"SELECT Origine FROM rec.Correspondance WHERE TableV3 = 'v2.CLIENT' AND IdV3 = '{clientV3}'");
await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.CLIENT SET CLIEN_NOM = 'MODIFIE UNE FOIS' WHERE CLIEN_ID_CLIEN = {clientV3};"));
var p2 = await Passer();
await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.CLIENT SET CLIEN_NOM = 'MODIFIE DEUX FOIS' WHERE CLIEN_ID_CLIEN = {clientV3};"));
var p3 = await Passer();
var clientsV2Apres = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.CLIENT");
var lignesV2 = clientV2 is null ? 0 : await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.CLIENT WHERE CLIEN_NOM LIKE 'MODIFIE%' OR CLIEN_NOM = 'CREE DANS LA V3'");
Exiger(p1.Erreur is null && p2.Erreur is null && p3.Erreur is null && clientV2 is not null && clientV2 != clientV3 && origine == "recopie"
       && clientV2 != clientDeLaV2 && clientsV2Apres == clientsV2Avant + 1 && lignesV2 == 1
       && await EnV2("CLIENT", "CLIEN_NOM", clientV2) == "MODIFIE DEUX FOIS"
       && await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.Correspondance WHERE TableV3 = 'v2.CLIENT' AND IdV3 = '{clientV3}'") == 1,
    $"REC-002 CA02 : le client {clientV3} cree dans la V3 recoit l'identifiant V2 {clientV2} (la V2 venait de donner {clientDeLaV2} a un client de ses agents), " +
    $"ecrit dans rec.Correspondance (origine « {origine} ») ; deux modifications suivantes mettent a jour cette ligne V2 ({clientsV2Avant} -> {clientsV2Apres} clients dans la V2, une seule ligne)");
// Le client de l'agent V2 est remonte dans la V3, sous un identifiant donne par la V3 (REC-003 CA03, jumeau de CA02).
var clientDeLaV2EnV3 = await V3De("CLIENT", clientDeLaV2);
Exiger(clientDeLaV2EnV3 is not null && clientDeLaV2EnV3 != clientDeLaV2 && await EnV3("CLIENT", "CLIEN_NOM", clientDeLaV2EnV3) == "CREE PAR UN AGENT V2",
    $"REC-003 CA03 (jumeau) : le client {clientDeLaV2} cree par un agent de la V2 est cree dans la V3 sous l'identifiant {clientDeLaV2EnV3} que la V3 lui donne, avec sa correspondance");

// CA03 : une action du registre et son dossier, crees dans la V3 dans la MEME transaction, l'action d'abord (la file a donc
// l'action avant le dossier) : la V2, qui verifie la cle ACTION_RCS -> DOSSIER_RCS, doit recevoir le dossier en premier.
var cleAction = await Texte(v2.Connexion, "SELECT f.name FROM sys.foreign_keys f WHERE f.parent_object_id = OBJECT_ID('dbo.ACTION_RCS') AND f.referenced_object_id = OBJECT_ID('dbo.DOSSIER_RCS') AND f.is_not_trusted = 0 AND f.is_disabled = 0");
var actionBase = Prendre("ACTION_RCS");
var dossierBase = Prendre("DOSSIER_RCS");
var colsAction = T("ACTION_RCS").V3.Colonnes.Where(c => !c.Identite).Select(c => c.Nom).ToList();
var colsDossier = T("DOSSIER_RCS").V3.Colonnes.Where(c => !c.Identite).Select(c => c.Nom).ToList();
var ids = (await Lire(v3.Connexion, V3Ecrit($"""
    BEGIN TRANSACTION;
    DECLARE @a TABLE (Id int); DECLARE @d TABLE (Id int);
    INSERT INTO v2.ACTION_RCS ({string.Join(", ", colsAction.Select(c => $"[{c}]"))}) OUTPUT inserted.ACTRC_ID_ACTRC INTO @a
    SELECT {string.Join(", ", colsAction.Select(c => c == "ACTRC_ID_DSRCS" ? "NULL" : $"[{c}]"))} FROM v2.ACTION_RCS WHERE ACTRC_ID_ACTRC = {actionBase};
    INSERT INTO v2.DOSSIER_RCS ({string.Join(", ", colsDossier.Select(c => $"[{c}]"))}) OUTPUT inserted.DSSRC_ID_DSRCS INTO @d
    SELECT {string.Join(", ", colsDossier.Select(c => c == "DSSRC_NUM_GESTION" ? "'2026B99901'" : $"[{c}]"))} FROM v2.DOSSIER_RCS WHERE DSSRC_ID_DSRCS = {dossierBase};
    UPDATE v2.ACTION_RCS SET ACTRC_ID_DSRCS = (SELECT Id FROM @d) WHERE ACTRC_ID_ACTRC = (SELECT Id FROM @a);
    COMMIT;
    SELECT (SELECT CONVERT(nvarchar(20), Id) FROM @a), (SELECT CONVERT(nvarchar(20), Id) FROM @d);
    """))).Rows[0];
var actionV3 = (string)ids[0];
var dossierV3 = (string)ids[1];
var fileCa03 = (await Lire(v3.Connexion, $"SELECT TableV3, Genre FROM rec.FileRecopie WHERE (TableV3 = 'v2.ACTION_RCS' AND IdV3 = '{actionV3}') OR (TableV3 = 'v2.DOSSIER_RCS' AND IdV3 = '{dossierV3}') ORDER BY Id"))
    .Rows.Cast<DataRow>().Select(r => $"{r[0]} {r[1]}").ToList();
var p4 = await Passer();
var actionV2 = await V2De("ACTION_RCS", actionV3);
var dossierV2 = await V2De("DOSSIER_RCS", dossierV3);
var lienV2 = actionV2 is null ? null : await EnV2("ACTION_RCS", "ACTRC_ID_DSRCS", actionV2);
var ordreApplique = (await Lire(v3.Connexion, $"SELECT TableV3, MIN(AppliqueeLe) FROM rec.FileRecopie WHERE (TableV3 = 'v2.ACTION_RCS' AND IdV3 = '{actionV3}') OR (TableV3 = 'v2.DOSSIER_RCS' AND IdV3 = '{dossierV3}') GROUP BY TableV3 ORDER BY 2"))
    .Rows.Cast<DataRow>().Select(r => (string)r[0]).ToList();
Exiger(cleAction is not null && fileCa03.Count >= 3 && fileCa03[0] == "v2.ACTION_RCS C" && p4.Erreur is null && actionV2 is not null && dossierV2 is not null
       && lienV2 == dossierV2 && ordreApplique.SequenceEqual(["v2.DOSSIER_RCS", "v2.ACTION_RCS"]),
    $"REC-002 CA03 : action {actionV3} et dossier {dossierV3} crees dans la V3, l'action en tete de file ({string.Join(", ", fileCa03)}) : le dossier est ecrit dans la V2 " +
    $"avant l'action ({string.Join(" puis ", ordreApplique)}), l'action V2 {actionV2} vise le dossier V2 {dossierV2} (lu : {lienV2}), aucune erreur de la cle {cleAction} {p4.Erreur}");
string? refusCle = null;
try { await Executer(v2.Connexion, $"UPDATE dbo.ACTION_RCS SET ACTRC_ID_DSRCS = 999999999 WHERE ACTRC_ID_ACTRC = {actionV2};"); }
catch (SqlException e) when (e.Number == 547) { refusCle = e.Message; }
Exiger(refusCle is not null, "REC-002 CA03 temoin : la V2 refuse bien une action qui vise un dossier absent (erreur 547) -- l'ordre d'ecriture est donc ce qui a ete prouve");

// CA04 : le SIREN d'un dossier et la date de mise a jour d'une inscription changes dans la V3 ; leur etat « F » dans la V2.
var dossierCa04 = dossiersF[0];
var temoinCa04 = dossiersF[1];
await Executer(v2.Connexion, $"UPDATE dbo.DOSSIER_RCS SET DSSRC_SIREN = '999000111' WHERE DSSRC_ID_DSRCS = {temoinCa04};");
var etatTemoin = await EnV2("DOSSIER_RCS", "DSSRC_ETAT_TRANSMISSION", temoinCa04);
var avantCa04 = await Texte(v2.Connexion, $"SELECT CONCAT(DSSRC_ETAT_TRANSMISSION, '|', CONVERT(varchar(30), DSSRC_DATE_MODIF_FLAG_TRANS, 126)) FROM dbo.DOSSIER_RCS WHERE DSSRC_ID_DSRCS = {dossierCa04}");
var avantInscription = await Texte(v2.Connexion, $"SELECT CONCAT(INEDT_ETAT_TRANSMISSION, '|', CONVERT(varchar(30), INEDT_DATE_MODIF_FLAG_TRANS, 126)) FROM dbo.INSCRIPTION WHERE INEDT_ID_INSCR = {inscriptionF}");
var fileAvantCa04 = await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.FileRecopie");
await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.DOSSIER_RCS SET DSSRC_SIREN = '888777666' WHERE DSSRC_ID_DSRCS = {dossierCa04}; " +
                                     $"UPDATE v2.INSCRIPTION SET INEDT_DATE_MAJ = '2026-11-21T09:00:00' WHERE INEDT_ID_INSCR = {inscriptionF};"));
var p5 = await Passer();
var apresCa04 = await Texte(v2.Connexion, $"SELECT CONCAT(DSSRC_ETAT_TRANSMISSION, '|', CONVERT(varchar(30), DSSRC_DATE_MODIF_FLAG_TRANS, 126)) FROM dbo.DOSSIER_RCS WHERE DSSRC_ID_DSRCS = {dossierCa04}");
var apresInscription = await Texte(v2.Connexion, $"SELECT CONCAT(INEDT_ETAT_TRANSMISSION, '|', CONVERT(varchar(30), INEDT_DATE_MODIF_FLAG_TRANS, 126)) FROM dbo.INSCRIPTION WHERE INEDT_ID_INSCR = {inscriptionF}");
// InfoGreffe_Segment_BI_INITIAL.sql:21 de la V2 n'envoie que les dossiers « DSSRC_ETAT_TRANSMISSION = 'M' ».
var envoyesInfogreffe = await Nombre(v2.Connexion, $"SELECT COUNT(*) FROM dbo.DOSSIER_RCS WHERE DSSRC_ETAT_TRANSMISSION = 'M' AND DSSRC_ID_DSRCS IN ({dossierCa04}, {dossierV2 ?? "0"})");
Exiger(etatTemoin == "M", $"REC-002 CA04 temoin : le declencheur de la V2 vit -- un SIREN change par un agent de la V2 marque le dossier {temoinCa04} « {etatTemoin} » (a transmettre)");
Exiger(p5.Erreur is null && await EnV2("DOSSIER_RCS", "DSSRC_SIREN", dossierCa04) == "888777666" && apresCa04 == avantCa04 && apresInscription == avantInscription
       && envoyesInfogreffe == 0 && await EnV2("DOSSIER_RCS", "DSSRC_ETAT_TRANSMISSION", dossierV2!) == "N",
    $"REC-002 CA04 : le SIREN change dans la V3 est dans la V2, et l'etat de transmission du dossier reste « {apresCa04} » (avant : « {avantCa04} »), celui de l'inscription " +
    $"« {apresInscription} » ; aucun des deux dossiers n'est « M » pour la vue d'envoi a Infogreffe ; le dossier cree dans la V3 a l'etat « N » dans la V2 {p5.Erreur}");

// CA05 (REC-002) et CA05 (REC-003) : la recopie du passage CA04 a ete vue par le suivi des changements de la V2 -- et
// reconnue. Le declencheur de la V2 a reecrit le dossier dans la meme instruction : SA version ne porte pas la marque de la
// recopie (mesure du lot), et elle est quand meme reconnue, par l'etat commun.
var contexte = await Texte(v2.Connexion, $"SELECT CONVERT(varchar(40), c.SYS_CHANGE_CONTEXT, 1) FROM CHANGETABLE(CHANGES dbo.DOSSIER_RCS, {transfert.VersionSuivi}) c WHERE c.DSSRC_ID_DSRCS = {dossierCa04}");
var suiteFile = new List<long>();
var suitePassages = new List<BilanPassage>();
for (var i = 0; i < 3; i++) { suitePassages.Add(await Passer()); suiteFile.Add(await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.FileRecopie")); }
var journalRecopie = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM soc.Journal WHERE Source = 'recopie-v2' AND IdObjet IN ('{dossierCa04}', '{inscriptionF}')");
// Le meme passage remonte aussi le dossier temoin, que l'agent de la V2 vient de changer : c'est sa seule remontee.
var journalTemoin = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM soc.Journal WHERE Source = 'recopie-v2' AND IdObjet = '{temoinCa04}'");
Exiger(p5.Echos >= 2 && p5.Remontees == 1 && journalTemoin == 1 && suiteFile.All(n => n == fileAvantCa04 + 2) && journalRecopie == 0,
    $"REC-002 CA05 : les deux fiches recopiees dans la V2 sont vues par la remontee du meme passage ({p5.Echos} reconnues comme des recopies) et ne reviennent pas vers la V3 : " +
    $"la file reste a {fileAvantCa04 + 2} lignes ({string.Join(", ", suiteFile)}) sur trois passages, aucune ecriture « recopie V2 » au journal pour elles ; la seule remontee " +
    $"({p5.Remontees}) est le dossier temoin {temoinCa04} change par l'agent de la V2 (contexte du suivi sur la derniere version du dossier recopie : {contexte ?? "aucun"}, " +
    "celui de la remise de l'etat de transmission par la recopie apres le declencheur)");
Exiger(suitePassages.All(b => b.Erreur is null && b.Appliquees == 0 && b.Remontees == 0),
    $"REC-003 CA05 : trois passages de plus ne font rien ({string.Join(" ; ", suitePassages.Select(b => $"{b.Appliquees} appliquees, {b.Remontees} remontees, {b.Echos} reconnues"))}) : aucun aller-retour");

// CA06 : la base V2 hors ligne pendant que des agents de la V3 ecrivent (le banc la coupe le temps de trois passages).
var clientCa06 = Prendre("CLIENT");
var adresseCa06 = Prendre("ADRESSE_BASE");
var colAdresse = Textes("ADRESSE_BASE")[0];
await Executer(serveurBancs, $"ALTER DATABASE [{v2.Nom}] SET OFFLINE WITH ROLLBACK IMMEDIATE;");
var ecrituresV3 = new List<long>();
var horsLigne = new List<BilanPassage>();
try
{
    foreach (var sql in new[] { $"UPDATE v2.CLIENT SET CLIEN_NOM = 'HORS LIGNE 1' WHERE CLIEN_ID_CLIEN = {clientCa06};",
                                $"UPDATE v2.ADRESSE_BASE SET [{colAdresse.Nom}] = '{Val(colAdresse, "HL-ADR")}' WHERE ADRBS_ID_ADRBS = {adresseCa06};",
                                $"UPDATE v2.CLIENT SET CLIEN_NOM = 'HORS LIGNE 2' WHERE CLIEN_ID_CLIEN = {clientCa06};" })
    {
        var t0 = Stopwatch.StartNew();
        await Executer(v3.Connexion, V3Ecrit(sql));
        ecrituresV3.Add(t0.ElapsedMilliseconds);
        horsLigne.Add(await Passer());
    }
}
finally
{
    await Executer(serveurBancs, $"ALTER DATABASE [{v2.Nom}] SET ONLINE;");
    SqlConnection.ClearAllPools();   // les connexions de la base coupee ne servent plus
}
// Une panne de la base n'est pas un refus du changement (REC-005 CA02) : aucun essai n'est compte, sinon trois passages
// d'une V2 arretee mettraient de cote tout ce que les agents ecrivent ; le motif, lui, est note sur la ligne.
var essais = await Nombre(v3.Connexion, $"SELECT MAX(Essais) FROM rec.FileRecopie WHERE TableV3 = 'v2.CLIENT' AND IdV3 = '{clientCa06}'");
var motifHorsLigne = await Texte(v3.Connexion, $"SELECT MAX(Erreur) FROM rec.FileRecopie WHERE TableV3 = 'v2.CLIENT' AND IdV3 = '{clientCa06}'");
var attenteHorsLigne = await EnAttente();
var retour = await Passer();
// L'ordre : les fiches passent dans l'ordre de leur premiere ligne en attente (le client, ecrit d'abord, puis l'adresse).
var ordreRetour = (await Lire(v3.Connexion, $"SELECT TableV3 FROM rec.FileRecopie WHERE (TableV3 = 'v2.CLIENT' AND IdV3 = '{clientCa06}') OR (TableV3 = 'v2.ADRESSE_BASE' AND IdV3 = '{adresseCa06}') " +
                                            "GROUP BY TableV3 ORDER BY MIN(AppliqueeLe), MIN(Id)")).Rows.Cast<DataRow>().Select(r => (string)r[0]).ToList();
Exiger(horsLigne.All(b => b.Erreur is not null && b.Appliquees == 0 && b.Refusees == 0) && ecrituresV3.All(ms => ms < 2000) && attenteHorsLigne == 3 && essais == 0
       && motifHorsLigne?.Contains(v2.Nom) == true
       && retour.Erreur is null && retour.Appliquees == 3 && await EnAttente() == 0
       && await EnV2("CLIENT", "CLIEN_NOM", clientCa06) == "HORS LIGNE 2" && await EnV2("ADRESSE_BASE", colAdresse.Nom, adresseCa06) == Val(colAdresse, "HL-ADR")
       && ordreRetour.SequenceEqual(["v2.CLIENT", "v2.ADRESSE_BASE"]),
    $"REC-002 CA06 : base V2 hors ligne, la V3 ecrit sans attendre ({string.Join(", ", ecrituresV3)} ms) ; trois passages echouent (« {horsLigne[0].Erreur?[..Math.Min(90, horsLigne[0].Erreur!.Length)]} »), " +
    $"les {attenteHorsLigne} changements attendent dans la file, motif note, aucun essai compte ({essais} : une panne n'est pas un refus, rien n'est mis de cote) ; " +
    "au retour, un passage les applique dans l'ordre, la V2 a la derniere valeur (« HORS LIGNE 2 »)");

// ====================================================================================================
// A. REC-003 — remonter dans le nouveau RIG ce que l'ancien ecrit sur les fiches partagees
// ====================================================================================================
Exiger(await ProgrammesV2() == programmesAvant && programmesTemoin != programmesAvant,
    $"REC-003 CA02 : apres la mise en service et tous ces passages, aucun programme de la V2 n'a change (procedures, fonctions, vues, declencheurs et leur etat, colonnes : " +
    $"{programmesAvant.Split('|').Length} elements identiques) ; contre-epreuve : une procedure ajoutee change l'empreinte");

// CA03 : un dossier et son action crees par un agent de la V2 ; puis le dossier change encore -- sa version de suivi passe
// apres celle de l'action, que la remontee lit donc en premier.
var dossierBase2 = Prendre("DOSSIER_RCS");
var colsDossierV2 = T("DOSSIER_RCS").V2.Colonnes.Where(c => !c.Identite).Select(c => c.Nom).ToList();
var colsActionV2 = T("ACTION_RCS").V2.Colonnes.Where(c => !c.Identite).Select(c => c.Nom).ToList();
var colDossier = Textes("DOSSIER_RCS")[0];
var ids2 = (await Lire(v2.Connexion, $"""
    BEGIN TRANSACTION;
    DECLARE @a TABLE (Id int); DECLARE @d TABLE (Id int);
    INSERT INTO dbo.DOSSIER_RCS ({string.Join(", ", colsDossierV2.Select(c => $"[{c}]"))}) OUTPUT inserted.DSSRC_ID_DSRCS INTO @d
    SELECT {string.Join(", ", colsDossierV2.Select(c => c == "DSSRC_NUM_GESTION" ? "'2026B99902'" : $"[{c}]"))} FROM dbo.DOSSIER_RCS WHERE DSSRC_ID_DSRCS = {dossierBase2};
    INSERT INTO dbo.ACTION_RCS ({string.Join(", ", colsActionV2.Select(c => $"[{c}]"))}) OUTPUT inserted.ACTRC_ID_ACTRC INTO @a
    SELECT {string.Join(", ", colsActionV2.Select(c => c == "ACTRC_ID_DSRCS" ? "(SELECT Id FROM @d)" : $"[{c}]"))} FROM dbo.ACTION_RCS WHERE ACTRC_ID_ACTRC = {actionBase};
    COMMIT;
    UPDATE dbo.DOSSIER_RCS SET [{colDossier.Nom}] = '{Val(colDossier, "V2-APRES")}' WHERE DSSRC_ID_DSRCS = (SELECT Id FROM @d);
    SELECT (SELECT CONVERT(nvarchar(20), Id) FROM @a), (SELECT CONVERT(nvarchar(20), Id) FROM @d);
    """)).Rows[0];
var actionV2b = (string)ids2[0];
var dossierV2b = (string)ids2[1];
var versions = await Texte(v2.Connexion, $"SELECT CONCAT((SELECT SYS_CHANGE_VERSION FROM CHANGETABLE(CHANGES dbo.ACTION_RCS, {transfert.VersionSuivi}) c WHERE c.ACTRC_ID_ACTRC = {actionV2b}), ' < ', " +
                                        $"(SELECT SYS_CHANGE_VERSION FROM CHANGETABLE(CHANGES dbo.DOSSIER_RCS, {transfert.VersionSuivi}) c WHERE c.DSSRC_ID_DSRCS = {dossierV2b}))");
var p6 = await Passer();
var actionV3b = await V3De("ACTION_RCS", actionV2b);
var dossierV3b = await V3De("DOSSIER_RCS", dossierV2b);
Exiger(p6.Erreur is null && actionV3b is not null && dossierV3b is not null && await EnV3("ACTION_RCS", "ACTRC_ID_DSRCS", actionV3b) == dossierV3b
       && await EnV3("DOSSIER_RCS", colDossier.Nom, dossierV3b) == Val(colDossier, "V2-APRES")
       && await Texte(v3.Connexion, $"SELECT Origine FROM rec.Correspondance WHERE TableV2 = 'ACTION_RCS' AND IdV2 = '{actionV2b}'") == "remontee"
       && await Differentes("ACTION_RCS", actionV2b, actionV3b) == 0 && await Differentes("DOSSIER_RCS", dossierV2b, dossierV3b) == 0,
    $"REC-003 CA03 : l'action {actionV2b} et son dossier {dossierV2b} crees dans la V2 (versions de suivi {versions} : l'action d'abord) sont crees dans la V3 avec leur correspondance, " +
    $"le dossier avant l'action : l'action V3 {actionV3b} vise le dossier V3 {dossierV3b}, valeurs identiques des deux cotes {p6.Erreur}");

// CA04 : la V3 arretee (sa base hors ligne) pendant que la V2 ecrit : 300 modifications de 60 clients et 20 arrivees.
// Le cout du suivi pour la V2 : les memes modifications sur une copie de CLIENT sans suivi.
var clientsCa04 = Enumerable.Range(0, 60).Select(_ => Prendre("CLIENT")).ToList();
await Executer(v2.Connexion, $"SELECT * INTO dbo.TEMOIN_SANS_SUIVI FROM dbo.CLIENT WHERE CLIEN_ID_CLIEN IN ({string.Join(", ", clientsCa04)});");
async Task<double> Moyenne(string table)
{
    await using var cn = new SqlConnection(v2.Connexion);
    await cn.OpenAsync();
    var t0 = Stopwatch.StartNew();
    for (var tour = 1; tour <= 5; tour++)
        foreach (var id in clientsCa04)
        {
            await using var cmd = new SqlCommand($"UPDATE dbo.{table} SET CLIEN_NOM = @n WHERE CLIEN_ID_CLIEN = {id};", cn);
            cmd.Parameters.AddWithValue("@n", $"V2 ARRET V3 {tour} {id}");
            await cmd.ExecuteNonQueryAsync();
        }
    return t0.Elapsed.TotalMilliseconds / (5 * clientsCa04.Count);
}
var pointAvant = await Nombre(v3.Connexion, "SELECT Version FROM rec.Remontee");
await Executer(serveurBancs, $"ALTER DATABASE [{v3.Nom}] SET OFFLINE WITH ROLLBACK IMMEDIATE;");
double avecSuivi, sansSuivi;
var arrivees = new List<string>();
try
{
    sansSuivi = await Moyenne("TEMOIN_SANS_SUIVI");
    avecSuivi = await Moyenne("CLIENT");
    var arriveeBase = Prendre("ARRIVEE");
    for (var i = 0; i < 20; i++) arrivees.Add(await Dupliquer(v2.Connexion, "dbo.ARRIVEE", T("ARRIVEE").V2, arriveeBase, $"ARRV_NOM='ARRIVEE V2 {i}'"));
}
finally
{
    await Executer(serveurBancs, $"ALTER DATABASE [{v3.Nom}] SET ONLINE;");
    SqlConnection.ClearAllPools();
}
await Executer(v2.Connexion, "DROP TABLE dbo.TEMOIN_SANS_SUIVI;");
var reprise = await Passer();
var manquants = new List<string>();
foreach (var id in clientsCa04)
    if (await V3De("CLIENT", id) is not { } i3 || await EnV3("CLIENT", "CLIEN_NOM", i3) != $"V2 ARRET V3 5 {id}" || await Differentes("CLIENT", id, i3) != 0) manquants.Add("client " + id);
foreach (var id in arrivees)
    if (await V3De("ARRIVEE", id) is not { } i3 || await Differentes("ARRIVEE", id, i3) != 0) manquants.Add("arrivee " + id);
var pointApres = await Nombre(v3.Connexion, "SELECT Version FROM rec.Remontee");
var courante = await Nombre(v2.Connexion, "SELECT CHANGE_TRACKING_CURRENT_VERSION()");
Exiger(avecSuivi < 50 && reprise.Erreur is null && manquants.Count == 0 && reprise.Remontees == clientsCa04.Count + arrivees.Count && pointApres == courante && pointApres > pointAvant,
    $"REC-003 CA04 : la base V3 hors ligne, la V2 ecrit sans attendre ({avecSuivi:0.00} ms par modification avec le suivi, {sansSuivi:0.00} ms sans) ; au retour, un passage " +
    $"remonte les {clientsCa04.Count} clients (5 modifications chacun, la derniere gagne) et les {arrivees.Count} arrivees, identiques des deux cotes, aucune ne manque " +
    $"({reprise.Remontees} remontees, point de reprise {pointAvant} -> {pointApres}) {string.Join(", ", manquants.Take(5))} {reprise.Erreur}");

// ====================================================================================================
// A. REC-004 — signaler a un agent du greffe une fiche modifiee des deux cotes
// ====================================================================================================
// Les dates de rec.* sont celles du serveur SQL (le SQL Server de Docker est a l'heure UTC) : le banc lit son horloge.
async Task<DateTime> MaintenantSql() => (DateTime)(await Valeur(v3.Connexion, "SELECT SYSDATETIME()"))!;
var debutConflits = await MaintenantSql();
var conflits = new List<(string Table, string Id, ColonneV2 Col, string ValV3, string ValV2, string? AuteurV2, string? IdAgentV2)>();
foreach (var t in partagees)
{
    var id = Prendre(t);
    var col = Textes(t)[0];
    var valV3 = Val(col, $"V3-{t}");
    var valV2 = Val(col, $"V2-{t}");
    // Le client : l'agent de la V2 l'enregistre comme Client.Sauve de la V2 (RigMetier\Facture\Client.cs:522-523) -- la date
    // de mise a jour ET l'agent courant dans CLIEN_ID_UTLST, un autre agent que celui de la fiche. Son nom attendu est celui
    // que la fiche client de la V2 affiche (Utilisateur.NomPrenomAbrege : « NOM Prenom (ABREGE) »), lu ici colonne a colonne.
    string? auteurV2 = null, idAgentV2 = null;
    if (t == "CLIENT" && (await Lire(v2.Connexion, "SELECT TOP (1) UTLST_ID_UTLST, UTLST_NOM, UTLST_PRENOM, UTLST_ABREGE FROM dbo.UTILISATEUR " +
                                                    $"WHERE UTLST_ID_UTLST <> ISNULL((SELECT CLIEN_ID_UTLST FROM dbo.CLIENT WHERE CLIEN_ID_CLIEN = {id}), -1) " +
                                                    "ORDER BY UTLST_ID_UTLST")).Rows.Cast<DataRow>().FirstOrDefault() is { } agent)
        (idAgentV2, auteurV2) = (Convert.ToString(agent[0]), $"{agent[1]} {agent[2]} ({agent[3]})");
    await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.[{t}] SET [{col.Nom}] = '{valV3}' WHERE [{Cle(t)}] = {id};"));
    await Executer(v2.Connexion, $"UPDATE dbo.[{t}] SET [{col.Nom}] = '{valV2}'" +
                                 (t == "CLIENT" ? $", CLIEN_DATE_DERNIERE_MODIF = '2026-11-21T08:30:00', CLIEN_ID_UTLST = {idAgentV2 ?? "NULL"}" : "") + $" WHERE [{Cle(t)}] = {id};");
    conflits.Add((t, id, col, valV3, valV2, auteurV2, idAgentV2));
    ecartsProvoques[t] = 1;
}
var p7 = await Passer();
var ca01 = new List<string>();
foreach (var (t, id, col, valV3, valV2, auteurV2, idAgentV2) in conflits)
{
    // Une ligne par champ perdu. Le client : l'agent de la V2 a change le champ, sa date de mise a jour et son agent -- trois
    // champs perdus, et l'auteur V2 de l'ecart est cet agent. Les 6 autres tables n'ont aucune colonne d'agent (leurs .sql
    // dans RigDatabase\dbo\Tables) : leur auteur V2 reste vide.
    var e = (await Lire(v3.Connexion, $"SELECT e.Id, e.DateV2, e.DateV3, e.AuteurV3, e.AuteurV2, e.Fiche, c.Champ, c.ValeurV2, c.ValeurV3 " +
                                       $"FROM rec.Ecart e JOIN rec.EcartChamp c ON c.IdEcart = e.Id WHERE e.TableV2 = '{t}' AND e.IdV2 = '{id}' ORDER BY c.Champ")).Rows.Cast<DataRow>().ToList();
    var attendus = t == "CLIENT" ? new[] { col.Nom, "CLIEN_DATE_DERNIERE_MODIF", "CLIEN_ID_UTLST" }.Order(StringComparer.Ordinal).ToArray() : [col.Nom];
    var champ = e.FirstOrDefault(r => (string)r[6] == col.Nom);
    var ok = e.Select(r => (string)r[6]).Order(StringComparer.Ordinal).SequenceEqual(attendus) && e.Select(r => Convert.ToInt64(r[0])).Distinct().Count() == 1
             && champ is not null && LigneJson.Lisible((string)champ[7]) == valV2 && LigneJson.Lisible((string)champ[8]) == valV3
             && (string)e[0][3] == AgentV3 && (DateTime)e[0][2] >= debutConflits.AddSeconds(-1)
             && (t == "CLIENT" ? auteurV2 is not null && e[0][4] as string == auteurV2 : e[0][4] is DBNull)
             && (t != "CLIENT" || (DateTime)e[0][1] == new DateTime(2026, 11, 21, 8, 30, 0))
             && await EnV2(t, col.Nom, id) == valV3 && await EnV3(t, col.Nom, id) == valV3 && await Differentes(t, id, id) == 0;
    if (!ok) ca01.Add($"{t} {id} ({e.Count} champ(s) : {string.Join(", ", e.Select(r => $"{r[6]} {r[7]} -> {r[8]}"))} ; auteur V2 " +
                      $"{(e.FirstOrDefault()?[4] is string a ? (a == auteurV2 ? "juste" : "autre que le nom de l'agent n° " + idAgentV2) : "vide")} ; V2 « {await EnV2(t, col.Nom, id)} », V3 « {await EnV3(t, col.Nom, id)} »)");
}
var exemple = conflits[0];
var agentClient = conflits.FirstOrDefault(c => c.Table == "CLIENT").IdAgentV2;
Exiger(p7.Erreur is null && p7.Ecarts == conflits.Count && ca01.Count == 0,
    $"REC-004 CA01 : une fiche de chaque table partagee modifiee dans la V3 puis dans la V2 avant le passage : la V3 gagne dans les deux bases, et {p7.Ecarts} ecarts sont ecrits, " +
    $"chacun avec le champ, la valeur V2 perdue, la valeur V3 gardee, les deux dates (celle de la V2 lue dans CLIEN_DATE_DERNIERE_MODIF pour un client) et les deux auteurs : " +
    $"V3 « {AgentV3} », V2 pour le client : le nom de l'agent n° {agentClient} que la V2 note dans CLIEN_ID_UTLST, ecrit comme sa fiche client l'affiche (« NOM Prenom (ABREGE) ») -- les 6 autres tables " +
    $"ne notent pas le leur -- ex. {exemple.Table} {exemple.Id}.{exemple.Col.Nom} : « {exemple.ValV2} » perdu, « {exemple.ValV3} » garde {string.Join(" ; ", ca01)} {p7.Erreur}");
var apresConflits = await Passer();
Exiger(apresConflits.Erreur is null && apresConflits.Ecarts == 0 && apresConflits.Remontees == 0 && await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.Ecart") == conflits.Count,
    "REC-004 CA01 (suite) : le passage suivant ne remonte pas la valeur perdue de la V2 et n'ecrit aucun nouvel ecart : la valeur de la V3 reste dans les deux bases");

// Jumeaux acceptes : la V3 seule change une fiche, ou la V2 puis la V3 a deux passages d'ecart -- aucun ecart.
var seule = Prendre("CLIENT");
var colClient = Textes("CLIENT");
await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.CLIENT SET [{colClient[0].Nom}] = '{Val(colClient[0], "V3 SEULE")}' WHERE CLIEN_ID_CLIEN = {seule};"));
var j1 = await Passer();
var suite = Prendre("CLIENT");
await Executer(v2.Connexion, $"UPDATE dbo.CLIENT SET [{colClient[0].Nom}] = '{Val(colClient[0], "V2 AVANT")}' WHERE CLIEN_ID_CLIEN = {suite};");
var j2 = await Passer();
await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.CLIENT SET [{colClient[0].Nom}] = '{Val(colClient[0], "V3 APRES")}' WHERE CLIEN_ID_CLIEN = {suite};"));
var j3 = await Passer();
Exiger(j1.Ecarts == 0 && j2.Ecarts == 0 && j3.Ecarts == 0 && j1.Appliquees == 1 && j2.Remontees == 1 && j3.Appliquees == 1
       && await EnV2("CLIENT", colClient[0].Nom, suite) == Val(colClient[0], "V3 APRES"),
    "REC-004 CA01 (jumeaux acceptes) : une fiche changee par la V3 seule, ou par la V2 puis par la V3 a deux passages d'ecart, n'est pas un conflit -- aucun ecart, chaque changement suit");

// Contre-epreuve : la meme situation de conflit, mais l'etat commun de la fiche efface (le defaut d'une recopie qui ne
// saurait pas ce que les deux bases avaient en commun) : aucun ecart -- l'oracle de CA01 le voit rouge.
var aveugle = Prendre("CLIENT");
await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.CLIENT SET [{colClient[0].Nom}] = '{Val(colClient[0], "V3 AVEUGLE")}' WHERE CLIEN_ID_CLIEN = {aveugle};"));
await Executer(v2.Connexion, $"UPDATE dbo.CLIENT SET [{colClient[0].Nom}] = '{Val(colClient[0], "V2 PERDUE")}' WHERE CLIEN_ID_CLIEN = {aveugle};");
await Executer(v3.Connexion, $"UPDATE rec.FileRecopie SET Avant = NULL WHERE TableV3 = 'v2.CLIENT' AND IdV3 = '{aveugle}'; DELETE FROM rec.EtatLigne WHERE TableV2 = 'CLIENT' AND IdV2 = '{aveugle}';");
var ce = await Passer();
var ecartAveugle = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.Ecart WHERE TableV2 = 'CLIENT' AND IdV2 = '{aveugle}'");
Exiger(ce.Erreur is null && ecartAveugle == 0 && await EnV2("CLIENT", colClient[0].Nom, aveugle) == Val(colClient[0], "V3 AVEUGLE"),
    $"REC-004 CA01 contre-epreuve : sans l'etat commun, la valeur « {Val(colClient[0], "V2 PERDUE")} » de la V2 disparait en silence ({ecartAveugle} ecart) -- " +
    "c'est exactement ce que l'oracle de CA01 refuse : il est rouge sur ce defaut");

// CA04 : deux champs differents d'une meme fiche, l'un change par la V2, l'autre par la V3 : la fiche entiere de la V3 gagne.
var ca04 = Prendre("CLIENT");
var champA = colClient[0];
var champB = colClient[1];
var avantA = await EnV2("CLIENT", champA.Nom, ca04);
// L'agent deja note sur la fiche (celui d'un enregistrement plus ancien) : la V2 change ici le champ sans noter ni date ni
// agent, cet agent n'est donc pas l'auteur du changement -- jumeau accepte de l'auteur V2 de CA01.
var agentAncien = await Texte(v2.Connexion, $"SELECT CLIEN_ID_UTLST FROM dbo.CLIENT WHERE CLIEN_ID_CLIEN = {ca04}");
if (agentAncien is null)
{
    agentAncien = await Texte(v2.Connexion, "SELECT MIN(UTLST_ID_UTLST) FROM dbo.UTILISATEUR");
    await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.CLIENT SET CLIEN_ID_UTLST = {agentAncien} WHERE CLIEN_ID_CLIEN = {ca04};"));
    await Passer();
}
await Executer(v2.Connexion, $"UPDATE dbo.CLIENT SET [{champA.Nom}] = '{Val(champA, "V2 CHAMP A")}' WHERE CLIEN_ID_CLIEN = {ca04};");
await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.CLIENT SET [{champB.Nom}] = '{Val(champB, "V3 CHAMP B")}' WHERE CLIEN_ID_CLIEN = {ca04};"));
var p8 = await Passer();
ecartsProvoques["CLIENT"]++;
var champsCa04 = (await Lire(v3.Connexion, $"SELECT c.Champ, c.ValeurV2, c.ValeurV3, e.AuteurV2 FROM rec.Ecart e JOIN rec.EcartChamp c ON c.IdEcart = e.Id WHERE e.TableV2 = 'CLIENT' AND e.IdV2 = '{ca04}'"))
    .Rows.Cast<DataRow>().ToList();
Exiger(p8.Ecarts == 1 && champsCa04.Count == 1 && (string)champsCa04[0][0] == champA.Nom && LigneJson.Lisible((string)champsCa04[0][1]) == Val(champA, "V2 CHAMP A")
       && LigneJson.Lisible(champsCa04[0][2] as string) == avantA && champsCa04[0][3] is DBNull && agentAncien is not null
       && await EnV2("CLIENT", champA.Nom, ca04) == avantA && await EnV2("CLIENT", champB.Nom, ca04) == Val(champB, "V3 CHAMP B") && await Differentes("CLIENT", ca04, ca04) == 0,
    $"REC-004 CA04 : la V2 change {champA.Nom}, la V3 change {champB.Nom} : la fiche entiere de la V3 gagne ({champA.Nom} revient a « {avantA} », {champB.Nom} = « {Val(champB, "V3 CHAMP B")} » " +
    $"dans les deux bases) et l'ecart liste le seul champ V2 perdu, {champA.Nom} « {Val(champA, "V2 CHAMP A")} » (fusion champ par champ : question ouverte au referent) ; " +
    $"la V2 n'a note ni date ni agent : l'agent {agentAncien} deja sur la fiche n'est pas donne pour auteur (auteur V2 {(champsCa04.FirstOrDefault()?[3] is string ? "donne : un nom d'agent" : "non note")})");

// CA05 : le compte du controle quotidien (REC-006) par base et par table.
var parJour = (await Lire(v3.Connexion, "SELECT BaseV2, TableV2, Ecarts FROM rec.EcartsParJour WHERE Jour = CAST(SYSDATETIME() AS date)")).Rows.Cast<DataRow>()
    .ToDictionary(r => (string)r[1], r => (Base: (string)r[0], Nombre: Convert.ToInt32(r[2])), StringComparer.OrdinalIgnoreCase);
Exiger(parJour.Count == ecartsProvoques.Count && ecartsProvoques.All(p => parJour.TryGetValue(p.Key, out var v) && v.Nombre == p.Value && v.Base == v2.Nom),
    $"REC-004 CA05 : rec.EcartsParJour (lue par le controle quotidien, REC-006) donne pour la journee, base {v2.Nom}, le nombre exact d'ecarts provoques par table : " +
    string.Join(", ", parJour.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value.Nombre}")));

// SOC-016 CA06 sur la remontee (REC-003) : une ecriture venue de la V2 est au journal de la V3 au nom de la recopie ET du
// compte V2 d'origine. La V2 note l'agent d'un client (CLIEN_ID_UTLST, ecrit avec la date par Client.Sauve,
// RigMetier\Facture\Client.cs:522-523) : le journal porte son compte de connexion (UTLST_LOGIN, varchar(35) comme
// soc.Journal.CompteV2, « le compte jdupont » de SOC-016_gestes.sql). Jumeaux : le client « suite » ci-dessus, change par la
// V2 sans date ni agent, et le dossier temoin de CA04 (DOSSIER_RCS n'a pas de colonne d'agent) restent « non note par la V2 ».
var signe = Prendre("CLIENT");
var agentSigne = (await Lire(v2.Connexion, "SELECT TOP (1) CONVERT(nvarchar(20), UTLST_ID_UTLST), UTLST_LOGIN FROM dbo.UTILISATEUR " +
                                           $"WHERE UTLST_ID_UTLST <> ISNULL((SELECT CLIEN_ID_UTLST FROM dbo.CLIENT WHERE CLIEN_ID_CLIEN = {signe}), -1) ORDER BY UTLST_ID_UTLST")).Rows[0];
await Executer(v2.Connexion, $"UPDATE dbo.CLIENT SET [{colClient[0].Nom}] = '{Val(colClient[0], "V2 SIGNE")}', CLIEN_DATE_DERNIERE_MODIF = '2026-11-21T08:45:00', " +
                             $"CLIEN_ID_UTLST = {agentSigne[0]} WHERE CLIEN_ID_CLIEN = {signe};");
var p9 = await Passer();
Task<string?> CompteJournal(string objet) =>
    Texte(v3.Connexion, $"SELECT TOP (1) CompteV2 FROM soc.Journal WHERE Source = 'recopie-v2' AND Famille = 'fiche-partagee' AND Objet = '{objet}' ORDER BY Id DESC");
var compteSigne = await CompteJournal($"client {signe}");
var compteSuite = await CompteJournal($"client {suite}");
var compteTemoin = await CompteJournal($"dossier RCS {temoinCa04}");
// Aucun compte d'agent de RIG_DEV n'est affiche, meme en echec : seulement ce qu'il est.
string Dit(string? compte) => compte switch
{
    null => "aucune ligne au journal",
    "non note par la V2" => "« non note par la V2 »",
    _ when compte == (string)agentSigne[1] => "le UTLST_LOGIN de l'agent n° " + agentSigne[0],
    _ => "un compte d'agent",
};
Exiger(p9.Erreur is null && p9.Remontees == 1 && p9.Ecarts == 0 && compteSigne == (string)agentSigne[1]
       && compteSuite == "non note par la V2" && compteTemoin == "non note par la V2",
    $"SOC-016 CA06 (REC-003) : le client {signe} enregistre dans la V2 par l'agent n° {agentSigne[0]} (date et CLIEN_ID_UTLST) est au journal de la V3 au nom de la recopie et du " +
    $"compte V2 de cet agent ({Dit(compteSigne)}) ; jumeaux : le client {suite}, change sans date ni agent, {Dit(compteSuite)} ; le dossier {temoinCa04}, sans colonne d'agent, " +
    $"{Dit(compteTemoin)} {p9.Erreur}");

// REC-006 sur les fiches recopiees : le controle de nuit (ComparaisonBases, lot 8) ne compare pas les colonnes propres a
// chaque logiciel (RegleRecopie.ColonnesPropres : l'etat de transmission), que la recopie ne copie dans aucun sens. Le
// dossier temoin de CA04, change par un agent de la V2, y est « M » (a transmettre) et garde son etat dans la V3 : compare,
// il sortirait en ecart chaque nuit.
// Il ne compare pas non plus les identifiants tels quels : une fiche creee d'un cote recoit le numero que l'autre base lui
// donne (CA02 : le client cree dans la V3 a un autre numero dans la V2) et ses liens sont traduits par la correspondance. Ici, un
// dossier cree par un agent de la V2 et un dossier cree dans la V3 prennent le meme numero : la recopie donne a chacun le
// numero suivant de l'autre base ; une action creee dans la V3 sur ce dossier V3 vise donc, dans la V2, un autre numero.
// Comparees par leur identifiant et leur lien bruts, ces fiches sortiraient en ecart chaque nuit.
var etatsTemoin = (V2: await EnV2("DOSSIER_RCS", "DSSRC_ETAT_TRANSMISSION", temoinCa04), V3: await EnV3("DOSSIER_RCS", "DSSRC_ETAT_TRANSMISSION", temoinCa04));
var dossierIdsBase = Prendre("DOSSIER_RCS");
var dossierIdsDeLaV2 = await Dupliquer(v2.Connexion, "dbo.DOSSIER_RCS", T("DOSSIER_RCS").V2, dossierIdsBase, "DSSRC_NUM_GESTION='2026B99902'");
var dossierIdsV3 = await Dupliquer(v3.Connexion, "v2.DOSSIER_RCS", T("DOSSIER_RCS").V3, dossierIdsBase, "DSSRC_NUM_GESTION='2026B99903'", V3Ecrit(""));
var actionIdsV3 = await Dupliquer(v3.Connexion, "v2.ACTION_RCS", T("ACTION_RCS").V3, Prendre("ACTION_RCS"), $"ACTRC_ID_DSRCS={dossierIdsV3}", V3Ecrit(""));
var pIds = await Passer();
var dossierIdsV2 = await V2De("DOSSIER_RCS", dossierIdsV3);
var actionIdsV2 = await V2De("ACTION_RCS", actionIdsV3);
var lienIdsV2 = actionIdsV2 is null ? null : await EnV2("ACTION_RCS", "ACTRC_ID_DSRCS", actionIdsV2);
var idsDivergent = pIds.Erreur is null && dossierIdsV2 is not null && dossierIdsV2 != dossierIdsV3 && lienIdsV2 == dossierIdsV2
                   && clientV2 is not null && clientV2 != clientV3 && await Differentes("ACTION_RCS", actionIdsV2!, actionIdsV3) == 0;
string[] tablesNuit = ["CLIENT", "DOSSIER_RCS", "INSCRIPTION", "ACTION_RCS"];
var nuit = await ComparaisonBases.ComparerAsync(new OptionsComparaison(v2.Connexion, v3.Connexion) { Tables = tablesNuit });
var ecartsNuit = nuit.Details.Where(d => d.Genre == "ecart").ToList();
var propresComparees = nuit.Tables.Where(t => T(t.TableV2).Propres.Any(p => t.Detail is null || !t.Detail.Contains(p.Nom))).Select(t => t.TableV2).ToList();
// Jumeaux : une vraie difference reste vue (ecrite dans la V2, controlee avant le passage de la recopie) -- sur une colonne
// recopiee, et sur un lien : l'action vise dans la V2 un autre dossier (meme numero des deux cotes) que dans la V3.
var dossierNuit = Prendre("DOSSIER_RCS");
await Executer(v2.Connexion, $"UPDATE dbo.DOSSIER_RCS SET [{colDossier.Nom}] = '{Val(colDossier, "NUIT V2")}' WHERE DSSRC_ID_DSRCS = {dossierNuit};");
if (actionIdsV2 is not null) await Executer(v2.Connexion, $"UPDATE dbo.ACTION_RCS SET ACTRC_ID_DSRCS = {dossierNuit} WHERE ACTRC_ID_ACTRC = {actionIdsV2};");
var nuitEcart = await ComparaisonBases.ComparerAsync(new OptionsComparaison(v2.Connexion, v3.Connexion) { Tables = tablesNuit });
var ecartsNuit2 = nuitEcart.Details.Where(d => d.Genre == "ecart").ToList();
var pNuit = await Passer();
string Ecarts(IEnumerable<EcartComparaison> l) => string.Join(" ; ", l.Select(e => $"{e.TableV2} {e.IdV2}->{e.IdV3} [{e.Colonnes}]"));
var vusAttendus = new[] { $"DOSSIER_RCS {dossierNuit}->{dossierNuit} [{colDossier.Nom}]", $"ACTION_RCS {actionIdsV2}->{actionIdsV3} [ACTRC_ID_DSRCS]" };
Exiger(etatsTemoin.V2 == "M" && etatsTemoin.V3 != "M" && idsDivergent && nuit.Etat == "fini" && nuit.Tables.Count == tablesNuit.Length && nuit.Ecarts == 0
       && propresComparees.Count == 0
       && nuitEcart.Etat == "fini" && nuitEcart.Ecarts == 2 && ecartsNuit2.Select(e => $"{e.TableV2} {e.IdV2}->{e.IdV3} [{e.Colonnes}]").Order().SequenceEqual(vusAttendus.Order())
       && pNuit.Erreur is null && pNuit.Remontees == 2,
    $"REC-006 sur les fiches recopiees : le dossier {temoinCa04} est « {etatsTemoin.V2} » dans la V2 et « {etatsTemoin.V3} » dans la V3 (etat de transmission, jamais recopie) ; " +
    $"identifiants divergents : client V3 {clientV3} = V2 {clientV2}, dossier V3 {dossierIdsV3} = V2 {dossierIdsV2} (le dossier V2 {dossierIdsDeLaV2} a pris son numero), " +
    $"action V3 {actionIdsV3} = V2 {actionIdsV2} qui vise le dossier V2 {lienIdsV2} {pIds.Erreur} ; " +
    $"le controle de nuit de {string.Join(", ", tablesNuit)} ({nuit.Etat}, {nuit.Tables.Count} tables, {nuit.LignesComparees} lignes) n'y voit pas d'ecart : {nuit.Ecarts} ecart(s) {Ecarts(ecartsNuit)}" +
    (propresComparees.Count > 0 ? $" — colonnes propres comparees pour {string.Join(", ", propresComparees)}" : "") +
    $" ; jumeaux : {colDossier.Nom} change dans la V2 sur le dossier {dossierNuit}, et l'action V2 {actionIdsV2} tournee vers ce dossier, sont vus " +
    $"({nuitEcart.Ecarts} ecarts : {Ecarts(ecartsNuit2)}), puis remontes ({pNuit.Remontees}) {pNuit.Erreur}");

// ====================================================================================================
// A. REC-005 sur la file continue : reprendre sans perte ni doublon ; un changement refuse est mis de cote, pas bloquant
// ====================================================================================================
// Contre-epreuve (rouge avant la correction) : sur le code d'avant (main 40624c2), le premier refus arretait le passage
// (« return » de Applicateur.AppliquerAsync et de Remontee.RemonterAsync) -- l'entree qui le montre est le client
// « REFUSE PAR LA V2 » que la contrainte CK_BANC_REC005 de la V2 refuse : SUIT LE REFUS 1 et 2 n'arrivaient jamais.
async Task<bool> JusquA(Func<Task<bool>> vu, double secondes = 30)
{
    var t0 = Stopwatch.StartNew();
    while (t0.Elapsed < TimeSpan.FromSeconds(secondes)) { if (await vu()) return true; await Task.Delay(100); }
    return false;
}
// La version du suivi des changements de la V2 pour une ligne : elle ne bouge que si la ligne est reecrite.
async Task<long?> VersionV2(string table, string id) => await Valeur(v2.Connexion,
    $"SELECT c.SYS_CHANGE_VERSION FROM CHANGETABLE(VERSION dbo.[{table}], ([{Cle(table)}]), ({id})) AS c") is { } vv ? Convert.ToInt64(vv) : null;
async Task<string?> LigneV2(string table, string id) => await Texte(v2.Connexion, $"SELECT * FROM dbo.[{table}] WHERE [{Cle(table)}] = {id} FOR JSON PATH");
async Task<List<(long Id, string Etat, int Essais, string? Erreur, DateTime? RelanceLe, string? RelancePar)>> LignesDe(string table, string idV3) =>
    [.. (await Lire(v3.Connexion, $"SELECT Id, Etat, Essais, Erreur, RelanceLe, RelancePar FROM rec.FileRecopie WHERE TableV3 = 'v2.{table}' AND IdV3 = '{idV3}' ORDER BY Id"))
        .Rows.Cast<DataRow>().Select(r => (Convert.ToInt64(r[0]), (string)r[1], Convert.ToInt32(r[2]), r[3] as string, r[4] as DateTime?, r[5] as string))];
async Task<(string Etat, int Essais, string? Erreur, string? RelancePar)?> EchecDe(string table, string idV2) =>
    (await Lire(v3.Connexion, $"SELECT Etat, Essais, Erreur, RelancePar FROM rec.RemonteeEchec WHERE BaseV2 = '{v2.Nom}' AND TableV2 = '{table}' AND IdV2 = '{idV2}'"))
        .Rows.Cast<DataRow>().Select(r => ((string, int, string?, string?)?)((string)r[0], Convert.ToInt32(r[1]), r[2] as string, r[3] as string)).FirstOrDefault();
Info($"file avant REC-005 : {await EnAttente()} changement(s) en attente");

// CA01 (1/2) : un passage coupe au milieu d'un lot. Trois clients changes dans la V3 ; un agent de la V2 tient le 2e (sa
// transaction reste ouverte), le passage s'arrete donc dessus ; on le coupe la (annulation : la connexion du passage
// abandonne sa transaction, comme un service arrete brutalement). Puis le passage suivant (le service redemarre).
var coupes = new[] { Prendre("CLIENT"), Prendre("CLIENT"), Prendre("CLIENT") };
for (var i = 0; i < coupes.Length; i++)
    await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.CLIENT SET CLIEN_NOM = 'COUPE {i + 1}' WHERE CLIEN_ID_CLIEN = {coupes[i]};"));
var clientsAvantCoupe = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.CLIENT");
bool premiereAppliquee, passageBloque;
string arretCoupe;
var etatsCoupe = new List<string>();
{
    await using var agentV2 = new SqlConnection(v2.Connexion);
    await agentV2.OpenAsync();
    await using var tenue = (SqlTransaction)await agentV2.BeginTransactionAsync();
    int spid;
    await using (var cmd = new SqlCommand($"UPDATE dbo.CLIENT SET CLIEN_FAX = CLIEN_FAX WHERE CLIEN_ID_CLIEN = {coupes[1]}; SELECT @@SPID;", agentV2, tenue))
        spid = Convert.ToInt32(await cmd.ExecuteScalarAsync());
    using var coupe = new CancellationTokenSource();
    var enCours = ServiceRecopie.PasserAsync(v3.Connexion, v2.Nom, fiches, ct: coupe.Token);
    premiereAppliquee = await JusquA(async () => (await LignesDe("CLIENT", coupes[0])).All(l => l.Etat == "appliquee"));
    passageBloque = await JusquA(async () => await Nombre(serveurBancs, $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = {spid}") > 0);
    coupe.Cancel();
    try { arretCoupe = (await enCours).Erreur ?? "passage fini sans erreur"; }
    catch (Exception e) { arretCoupe = $"{e.GetType().Name} : {e.Message}"; }
    await tenue.RollbackAsync();
    foreach (var id in coupes) etatsCoupe.Add(string.Join("+", (await LignesDe("CLIENT", id)).Select(l => l.Etat)));
}
var versionPremiere = await VersionV2("CLIENT", coupes[0]);
var repriseCoupe = await Passer();
var nomsApresCoupe = new List<string?>();
var differentesCoupe = 0L;
foreach (var id in coupes) { nomsApresCoupe.Add(await EnV2("CLIENT", "CLIEN_NOM", id)); differentesCoupe += await Differentes("CLIENT", id, id); }
Exiger(premiereAppliquee && passageBloque && etatsCoupe.SequenceEqual(["appliquee", "en attente", "en attente"])
       && repriseCoupe.Erreur is null && repriseCoupe.Appliquees == 2 && await VersionV2("CLIENT", coupes[0]) == versionPremiere
       && nomsApresCoupe.SequenceEqual(["COUPE 1", "COUPE 2", "COUPE 3"]) && differentesCoupe == 0
       && await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.CLIENT") == clientsAvantCoupe,
    $"REC-005 CA01 : un passage coupe pendant l'ecriture du 2e de trois clients (« {arretCoupe[..Math.Min(80, arretCoupe.Length)]} ») laisse le 1er applique et les deux " +
    $"suivants en file ({string.Join(", ", etatsCoupe)}) ; au redemarrage, le passage repart du dernier changement applique et enregistre : il applique les 2 autres " +
    $"({repriseCoupe.Appliquees}), ne reecrit pas le 1er (sa version de suivi dans la V2 reste {versionPremiere}), les trois fiches sont identiques des deux cotes, aucun client en plus");

// CA01 (2/2) : rejouer un changement deja applique (une modification, et la creation du client de CA02) laisse la ligne
// identique -- la V2 n'est meme pas reecrite (version de suivi inchangee), aucun doublon, aucun ecart.
var rejoues = new[] { (V3: coupes[0], V2: coupes[0]), (V3: clientV3, V2: clientV2!) };
var avantRejeu = new List<(long? Version, string? Ligne)>();
foreach (var r in rejoues) avantRejeu.Add((await VersionV2("CLIENT", r.V2), await LigneV2("CLIENT", r.V2)));
var clientsAvantRejeu = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.CLIENT");
var lignesRejouees = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.FileRecopie WHERE TableV3 = 'v2.CLIENT' AND IdV3 IN ('{coupes[0]}', '{clientV3}')");
await Executer(v3.Connexion, $"UPDATE rec.FileRecopie SET Etat = 'en attente', AppliqueeLe = NULL WHERE TableV3 = 'v2.CLIENT' AND IdV3 IN ('{coupes[0]}', '{clientV3}');");
var rejeu = await Passer();
var identiques = 0;
for (var i = 0; i < rejoues.Length; i++)
    if (await VersionV2("CLIENT", rejoues[i].V2) == avantRejeu[i].Version && await LigneV2("CLIENT", rejoues[i].V2) == avantRejeu[i].Ligne) identiques++;
Exiger(rejeu.Erreur is null && rejeu.Appliquees == lignesRejouees && rejeu.Ecarts == 0 && identiques == rejoues.Length
       && await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.CLIENT") == clientsAvantRejeu
       && await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.Correspondance WHERE TableV3 = 'v2.CLIENT' AND IdV3 = '{clientV3}'") == 1,
    $"REC-005 CA01 : {lignesRejouees} changements deja appliques rejoues (le client {coupes[0]} modifie, le client {clientV3} cree dans la V3) : {identiques} lignes V2 " +
    $"identiques au caractere pres et jamais reecrites (version de suivi inchangee), aucun client en plus ({clientsAvantRejeu}), une seule correspondance, aucun ecart");

// CA02 (V3 vers V2) : un changement que la V2 refuse a chaque essai (une contrainte de la V2 : CK_BANC_REC005).
var colDerriere = colClient[1];
await Executer(v2.Connexion, "ALTER TABLE dbo.CLIENT WITH NOCHECK ADD CONSTRAINT CK_BANC_REC005 CHECK (CLIEN_NOM NOT LIKE 'REFUSE PAR LA V2%');");
var refuse = Prendre("CLIENT");
var suit1 = Prendre("CLIENT");
var suit2 = Prendre("CLIENT");
var nomAvantRefus = await EnV2("CLIENT", "CLIEN_NOM", refuse);
await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.CLIENT SET CLIEN_NOM = 'REFUSE PAR LA V2' WHERE CLIEN_ID_CLIEN = {refuse};"));
await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.CLIENT SET CLIEN_NOM = 'SUIT LE REFUS 1' WHERE CLIEN_ID_CLIEN = {suit1};"));
var refus1 = await Passer();
var refus2 = await Passer();
var apresDeux = await LignesDe("CLIENT", refuse);
await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.CLIENT SET CLIEN_NOM = 'SUIT LE REFUS 2' WHERE CLIEN_ID_CLIEN = {suit2};"));
var refus3 = await Passer();
// La meme fiche change encore dans la V3 : ce changement attend derriere celui qui est mis de cote.
await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.CLIENT SET [{colDerriere.Nom}] = '{Val(colDerriere, "DERRIERE")}' WHERE CLIEN_ID_CLIEN = {refuse};"));
var refus4 = await Passer();
var lignesRefus = await LignesDe("CLIENT", refuse);
string Bilans(params BilanPassage[] b) => string.Join(" ; ", b.Select(x => $"{x.Appliquees} appl., {x.Remontees} rem., {x.Refusees} refus, {x.MisesDeCote} de cote{(x.Erreur is null ? "" : " — " + x.Erreur)}"));
Exiger(refus1.Erreur is null && refus1.Refusees == 1 && refus1.Appliquees == 1 && refus2.Refusees == 1 && refus2.MisesDeCote == 0
       && apresDeux.Count == 1 && apresDeux[0] is { Etat: "en attente", Essais: 2 }
       && refus3.Refusees == 1 && refus3.MisesDeCote == 1 && refus3.Appliquees == 1
       && lignesRefus.Count == 2 && lignesRefus[0] is { Etat: "mise de cote", Essais: 3 } && lignesRefus[0].Erreur?.Contains("CK_BANC_REC005") == true
       && lignesRefus[1] is { Etat: "en attente", Essais: 0 } && refus4.Erreur is null && refus4.Refusees == 0 && refus4.Appliquees == 0
       && await EnV2("CLIENT", "CLIEN_NOM", suit1) == "SUIT LE REFUS 1" && await EnV2("CLIENT", "CLIEN_NOM", suit2) == "SUIT LE REFUS 2"
       && await EnV2("CLIENT", "CLIEN_NOM", refuse) == nomAvantRefus && await EnV2("CLIENT", colDerriere.Nom, refuse) != Val(colDerriere, "DERRIERE"),
    $"REC-005 CA02 (V3 vers V2) : le client {refuse} « REFUSE PAR LA V2 », que la contrainte CK_BANC_REC005 de la V2 refuse, compte un essai par passage " +
    $"(apres 2 : « {apresDeux.FirstOrDefault().Etat} », {apresDeux.FirstOrDefault().Essais} essais) et il est mis de cote au 3e avec son message " +
    $"(« {lignesRefus.FirstOrDefault().Erreur?[..Math.Min(90, lignesRefus.FirstOrDefault().Erreur?.Length ?? 0)]} ») ; pendant ce temps les autres fiches passent (SUIT LE REFUS 1 et 2 " +
    $"dans la V2) et le changement suivant de la meme fiche attend derriere lui ({string.Join(", ", lignesRefus.Select(l => $"{l.Etat} {l.Essais}"))}) — passages : " +
    Bilans(refus1, refus2, refus3, refus4));

// CA03 (V3 vers V2) : l'exploitant corrige la cause (la contrainte retiree) et relance le changement mis de cote.
await Executer(v2.Connexion, "ALTER TABLE dbo.CLIENT DROP CONSTRAINT CK_BANC_REC005;");
var relanceAttente = lignesRefus.Count > 1 && await ServiceRecopie.RelancerAsync(v3.Connexion, lignesRefus[1].Id, "banc exploitant");
var relancee = lignesRefus.Count > 0 && await ServiceRecopie.RelancerAsync(v3.Connexion, lignesRefus[0].Id, "banc exploitant");
var refus5 = await Passer();
var lignesRelancees = await LignesDe("CLIENT", refuse);
Exiger(relancee && !relanceAttente && refus5.Erreur is null && refus5.Appliquees == 2 && lignesRelancees.All(l => l.Etat == "appliquee")
       && lignesRelancees.FirstOrDefault() is { RelanceLe: not null, RelancePar: "banc exploitant" }
       && await EnV2("CLIENT", "CLIEN_NOM", refuse) == "REFUSE PAR LA V2" && await EnV2("CLIENT", colDerriere.Nom, refuse) == Val(colDerriere, "DERRIERE")
       && await Differentes("CLIENT", refuse, refuse) == 0,
    $"REC-005 CA03 (V3 vers V2) : la cause corrigee, la relance du changement mis de cote (ligne {lignesRefus.FirstOrDefault().Id}, trace « {lignesRelancees.FirstOrDefault().RelancePar} ») " +
    $"l'applique au passage suivant, puis celui qui attendait derriere lui : {refus5.Appliquees} lignes appliquees dans l'ordre, la V2 a les deux valeurs (« REFUSE PAR LA V2 », " +
    $"« {Val(colDerriere, "DERRIERE")} »), identique a la V3 ; jumeau : une ligne qui n'est pas mise de cote ne se relance pas ({(relanceAttente ? "relancee" : "refuse")})");

// CA02 et CA03 (V2 vers V3) : un changement de la V2 que la V3 refuse (CK_BANC_REC005_V3, posee sur la table de la V3).
await Executer(v3.Connexion, "ALTER TABLE v2.CLIENT WITH NOCHECK ADD CONSTRAINT CK_BANC_REC005_V3 CHECK (CLIEN_NOM NOT LIKE 'REFUSE PAR LA V3%');");
var refuseV2 = Prendre("CLIENT");
var monte1 = Prendre("CLIENT");
var monte2 = Prendre("CLIENT");
var nomV3AvantRefus = await EnV3("CLIENT", "CLIEN_NOM", refuseV2);
await Executer(v2.Connexion, $"UPDATE dbo.CLIENT SET CLIEN_NOM = 'REFUSE PAR LA V3' WHERE CLIEN_ID_CLIEN = {refuseV2};");
await Executer(v2.Connexion, $"UPDATE dbo.CLIENT SET CLIEN_NOM = 'REMONTE APRES LE REFUS 1' WHERE CLIEN_ID_CLIEN = {monte1};");
var monte1Bilan = await Passer();
var monte2Bilan = await Passer();
var echecDeux = await EchecDe("CLIENT", refuseV2);
await Executer(v2.Connexion, $"UPDATE dbo.CLIENT SET CLIEN_NOM = 'REMONTE APRES LE REFUS 2' WHERE CLIEN_ID_CLIEN = {monte2};");
var monte3Bilan = await Passer();
await Executer(v2.Connexion, $"UPDATE dbo.CLIENT SET [{colDerriere.Nom}] = '{Val(colDerriere, "DERRIERE V2")}' WHERE CLIEN_ID_CLIEN = {refuseV2};");
var monte4Bilan = await Passer();
var echecCote = await EchecDe("CLIENT", refuseV2);
var pointRefus = await Nombre(v3.Connexion, "SELECT Version FROM rec.Remontee");
var couranteRefus = await Nombre(v2.Connexion, "SELECT CHANGE_TRACKING_CURRENT_VERSION()");
Exiger(monte1Bilan.Erreur is null && monte1Bilan.Refusees == 1 && monte1Bilan.Remontees == 1 && monte2Bilan.Refusees == 1 && echecDeux is { Etat: "en echec", Essais: 2 }
       && monte3Bilan.Refusees == 1 && monte3Bilan.MisesDeCote == 1 && monte3Bilan.Remontees == 1 && monte4Bilan.Erreur is null && monte4Bilan.Refusees == 0
       && echecCote is { Etat: "mise de cote", Essais: 3 } && echecCote.Value.Erreur?.Contains("CK_BANC_REC005_V3") == true && pointRefus == couranteRefus
       && await EnV3("CLIENT", "CLIEN_NOM", monte1) == "REMONTE APRES LE REFUS 1" && await EnV3("CLIENT", "CLIEN_NOM", monte2) == "REMONTE APRES LE REFUS 2"
       && await EnV3("CLIENT", "CLIEN_NOM", refuseV2) == nomV3AvantRefus && await EnV3("CLIENT", colDerriere.Nom, refuseV2) != Val(colDerriere, "DERRIERE V2"),
    $"REC-005 CA02 (V2 vers V3) : le client {refuseV2} « REFUSE PAR LA V3 » ecrit par un agent de la V2 compte un essai par passage (apres 2 : « {echecDeux?.Etat} » " +
    $"{echecDeux?.Essais}), mis de cote au 3e avec son message (« {echecCote?.Erreur?[..Math.Min(90, echecCote?.Erreur?.Length ?? 0)]} ») ; les autres remontent " +
    $"(REMONTE APRES LE REFUS 1 et 2), le point de reprise avance quand meme ({pointRefus} = version courante {couranteRefus}) et le changement suivant de la meme fiche " +
    $"attend derriere lui — passages : {Bilans(monte1Bilan, monte2Bilan, monte3Bilan, monte4Bilan)}");
await Executer(v3.Connexion, "ALTER TABLE v2.CLIENT DROP CONSTRAINT CK_BANC_REC005_V3;");
var relanceeV2 = await ServiceRecopie.RelancerRemonteeAsync(v3.Connexion, v2.Nom, "CLIENT", refuseV2, "banc exploitant");
var monte5Bilan = await Passer();
var echecResolu = await EchecDe("CLIENT", refuseV2);
Exiger(relanceeV2 && monte5Bilan.Erreur is null && monte5Bilan.Remontees == 1 && echecResolu is { Etat: "resolue", RelancePar: "banc exploitant" }
       && await EnV3("CLIENT", "CLIEN_NOM", refuseV2) == "REFUSE PAR LA V3" && await EnV3("CLIENT", colDerriere.Nom, refuseV2) == Val(colDerriere, "DERRIERE V2")
       && await Differentes("CLIENT", refuseV2, refuseV2) == 0,
    $"REC-005 CA03 (V2 vers V3) : la cause corrigee et la fiche relancee, elle remonte au passage suivant avec les deux changements de la V2, dans l'ordre (« REFUSE PAR LA V3 », " +
    $"« {Val(colDerriere, "DERRIERE V2")} »), identique des deux cotes ; sa trace reste (« {echecResolu?.Etat} », relancee par « {echecResolu?.RelancePar} ») {monte5Bilan.Erreur}");

// CA02 et CA03 (V2 vers V3), une fiche qui en VISE une refusee : un dossier et son action crees ensemble par un agent de la
// V2 (meme version de suivi) ; la V3 refuse le dossier. L'action attend derriere lui sans compter d'essai et SANS SE PERDRE,
// alors que le point de reprise avance ; la relance du dossier fait passer les deux, l'action visant le dossier de la V3.
// Defaut mesure a la relecture : l'action, ecartee sans etre notee, disparaissait de la remontee une fois le point avance.
await Executer(v3.Connexion, "ALTER TABLE v2.DOSSIER_RCS WITH NOCHECK ADD CONSTRAINT CK_BANC_REC005_LIEN CHECK (DSSRC_NUM_GESTION <> '2026B99904');");
var dossierBase3 = Prendre("DOSSIER_RCS");
var ids3 = (await Lire(v2.Connexion, $"""
    BEGIN TRANSACTION;
    DECLARE @a TABLE (Id int); DECLARE @d TABLE (Id int);
    INSERT INTO dbo.DOSSIER_RCS ({string.Join(", ", colsDossierV2.Select(c => $"[{c}]"))}) OUTPUT inserted.DSSRC_ID_DSRCS INTO @d
    SELECT {string.Join(", ", colsDossierV2.Select(c => c == "DSSRC_NUM_GESTION" ? "'2026B99904'" : $"[{c}]"))} FROM dbo.DOSSIER_RCS WHERE DSSRC_ID_DSRCS = {dossierBase3};
    INSERT INTO dbo.ACTION_RCS ({string.Join(", ", colsActionV2.Select(c => $"[{c}]"))}) OUTPUT inserted.ACTRC_ID_ACTRC INTO @a
    SELECT {string.Join(", ", colsActionV2.Select(c => c == "ACTRC_ID_DSRCS" ? "(SELECT Id FROM @d)" : $"[{c}]"))} FROM dbo.ACTION_RCS WHERE ACTRC_ID_ACTRC = {actionBase};
    COMMIT;
    SELECT (SELECT CONVERT(nvarchar(20), Id) FROM @a), (SELECT CONVERT(nvarchar(20), Id) FROM @d);
    """)).Rows[0];
var (actionVisee, dossierRefuse) = ((string)ids3[0], (string)ids3[1]);
var visees = new List<BilanPassage>();
for (var i = 0; i < 3; i++) visees.Add(await Passer());
var dossierCote = await EchecDe("DOSSIER_RCS", dossierRefuse);
var actionAttend = await EchecDe("ACTION_RCS", actionVisee);
var actionAbsente = await V3De("ACTION_RCS", actionVisee) is null;
var pointVisee = await Nombre(v3.Connexion, "SELECT Version FROM rec.Remontee");
var couranteVisee = await Nombre(v2.Connexion, "SELECT CHANGE_TRACKING_CURRENT_VERSION()");
await Executer(v3.Connexion, "ALTER TABLE v2.DOSSIER_RCS DROP CONSTRAINT CK_BANC_REC005_LIEN;");
var relanceVisee = await ServiceRecopie.RelancerRemonteeAsync(v3.Connexion, v2.Nom, "DOSSIER_RCS", dossierRefuse, "banc exploitant");
var apresVisee = await Passer();
var dossierV3Visee = await V3De("DOSSIER_RCS", dossierRefuse);
var actionV3Visee = await V3De("ACTION_RCS", actionVisee);
var lienVisee = actionV3Visee is null ? null : await EnV3("ACTION_RCS", "ACTRC_ID_DSRCS", actionV3Visee);
var (finAction, finDossier) = (await EchecDe("ACTION_RCS", actionVisee), await EchecDe("DOSSIER_RCS", dossierRefuse));
Exiger(visees.All(b => b.Erreur is null && b.Refusees == 1) && dossierCote is { Etat: "mise de cote", Essais: 3 }
       && actionAttend is { Etat: "en echec", Essais: 0 } && actionAttend.Value.Erreur?.Contains("attend") == true && actionAbsente && pointVisee == couranteVisee
       && relanceVisee && apresVisee.Erreur is null && dossierV3Visee is not null && actionV3Visee is not null
       && lienVisee == dossierV3Visee && finAction is { Etat: "resolue" } && finDossier is { Etat: "resolue" },
    $"REC-005 CA02 et CA03 (fiche visee, V2 vers V3) : le dossier {dossierRefuse} refuse par la V3 est mis de cote au 3e essai (« {dossierCote?.Etat} », {dossierCote?.Essais}) ; " +
    $"l'action {actionVisee} qui le vise attend sans essai (« {actionAttend?.Etat} », {actionAttend?.Essais} essai, « {actionAttend?.Erreur?[..Math.Min(60, actionAttend?.Erreur?.Length ?? 0)]} »), " +
    $"absente de la V3 mais pas perdue alors que le point de reprise avance ({pointVisee} = {couranteVisee}) ; la relance du dossier fait passer les deux : " +
    $"l'action V3 {actionV3Visee} vise le dossier V3 {lienVisee} (attendu {dossierV3Visee}), les deux « {finAction?.Etat} » / « {finDossier?.Etat} » " +
    $"(absente avant : {actionAbsente}, relance : {relanceVisee}) — passages : {Bilans([.. visees, apresVisee])}");

// ====================================================================================================
// B. La vraie boucle du serveur (RecopieContinue) : REC-002 CA01 et REC-003 CA01
// ====================================================================================================
await using var commune = await BaseJetable.CreerAsync("RecopieDeuxSensCommune");
await Migrateur.AppliquerAsync(commune.Connexion, migrations);
var instance = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs()).DataSource;
var authentification = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
authentification.Remove("Data Source");
const string Greffe = "9901";
// Le greffe du banc (sa base V3 creee), et un greffe pas encore passe (base V3 nommee, jamais creee) que la boucle doit ignorer.
const string GreffePasPasse = "9902";
await Executer(commune.Connexion, "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, BaseV3CreeeLe, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe) " +
                                  $"VALUES ('{Greffe}', N'Greffe du banc de la recopie', '{instance}', '{v2.Nom}', '{v3.Nom}', SYSDATETIME(), 0, 0, 'Romance Standard Time', 'banc', SYSDATETIME()), " +
                                  $"('{GreffePasPasse}', N'Greffe pas encore passe', '{instance}', 'RIG_BANC_PAS_PASSE', '{BaseJetable.Prefixe}jamais_creee_{Guid.NewGuid():N}', NULL, 0, 0, 'Romance Standard Time', 'banc', SYSDATETIME());");
appB = new BaseRigApp(ServeurRig.Construire(["--environment", "Development"], s =>
{
    s.WebHost.UseTestServer();
    s.Logging.SetMinimumLevel(LogLevel.Warning);
    s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
    s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
    s.Configuration[RecopieContinue.Reglage] = "true";
    s.Configuration["Recopie:IntervalleSecondes"] = "1";
}));
await appB.App.StartAsync();
var delaisV3 = new List<(string Table, double S)>();
var delaisV2 = new List<(string Table, double S)>();
async Task<double> Attendre(Func<Task<bool>> vu)
{
    var t0 = Stopwatch.StartNew();
    while (t0.Elapsed < TimeSpan.FromSeconds(90)) { if (await vu()) return t0.Elapsed.TotalSeconds; await Task.Delay(100); }
    return double.PositiveInfinity;
}
foreach (var t in partagees)
{
    var id = Prendre(t);
    var col = Textes(t)[0];
    var v = Val(col, $"DELAI3-{t}");
    await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.[{t}] SET [{col.Nom}] = '{v}' WHERE [{Cle(t)}] = {id};"));
    delaisV3.Add((t, await Attendre(async () => await EnV2(t, col.Nom, id) == v)));
}
foreach (var t in partagees)
{
    var id = Prendre(t);
    var col = Textes(t)[0];
    var v = Val(col, $"DELAI2-{t}");
    await Executer(v2.Connexion, $"UPDATE dbo.[{t}] SET [{col.Nom}] = '{v}' WHERE [{Cle(t)}] = {id};");
    delaisV2.Add((t, await Attendre(async () => await EnV3(t, col.Nom, id) == v)));
}
await appB.App.StopAsync();
var passagesBoucle = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.PassageRecopie WHERE Erreur IS NULL AND Debut >= '{debutConflits:yyyy-MM-ddTHH:mm:ss}'");
Exiger(delaisV3.All(d => d.S < 60),
    $"REC-002 CA01 : une fiche de chaque table partagee modifiee dans la V3 se lit dans la V2 en {delaisV3.Max(d => d.S):0.0} s au plus (delai propose : une minute) — " +
    $"{string.Join(", ", delaisV3.Select(d => $"{d.Table} {d.S:0.0} s"))} ; la boucle du serveur (« Recopie:Continue », 1 s ici, 5 s par defaut) a fait {passagesBoucle} passages");
Exiger(delaisV2.All(d => d.S < 60),
    $"REC-003 CA01 : une fiche de chaque table partagee modifiee dans la V2 se lit dans la V3 en {delaisV2.Max(d => d.S):0.0} s au plus (delai propose : une minute) — " +
    string.Join(", ", delaisV2.Select(d => $"{d.Table} {d.S:0.0} s")));
// Les erreurs de la recopie elle-meme (les autres traitements du serveur -- planificateur de nuit, surveillance -- se
// plaignent a leur facon du greffe sans base, et c'est leur affaire).
var erreursBoucle = await Lire(commune.Connexion, "SELECT ISNULL(CodeGreffe, '-'), Programme, LEFT(Message, 160) FROM soc.ErreurTechnique " +
                                                  "WHERE Programme LIKE '%Recopie%' OR Message LIKE 'Recopie%'");
var autresErreurs = await Nombre(commune.Connexion, "SELECT COUNT(*) FROM soc.ErreurTechnique");
Exiger(erreursBoucle.Rows.Count == 0 && autresErreurs > 0,
    $"la boucle du serveur laisse de cote le greffe {GreffePasPasse}, dont la base V3 n'est pas creee : aucune erreur de la recopie au journal technique pendant ses " +
    $"{passagesBoucle} passages (le journal est bien ecrit : {autresErreurs} lignes des autres traitements du serveur) " +
    string.Join(" ; ", erreursBoucle.Rows.Cast<DataRow>().Select(r => $"{r[0]} {r[1]} : {r[2]}")));

// ====================================================================================================
// C. Le vrai serveur et l'ecran « Fiches en conflit » : REC-004 CA02 et CA03
// ====================================================================================================
// Le compte sans son domaine : le serveur le retire de la session, comme la V2 (ServiceIdentite.CompteDe).
const string Designe = "banc_designe", Autre = "banc_autre";
await Executer(v3.Connexion, $"""
    INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES ('{Greffe}', 'REC', N'Recopie');
    INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANC_SUIVI', 'Banc : suit les ecarts de recopie'), ('BANC_AUTRE', 'Banc : agent non designe');
    INSERT INTO soc.RoleFonction (Role, Fonction) VALUES ('BANC_SUIVI', '{RoutesConflits.FonctionSuivi}');
    INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
    SELECT v.Compte, v.Nom, 'Banc', LEFT(v.Nom, 16), 'M.', '{Greffe}', (SELECT MAX(Id) FROM soc.Service WHERE Abrege = 'REC'), 0
    FROM (VALUES ('{Designe}', 'DESIGNE'), ('{Autre}', 'AUTRE')) v (Compte, Nom);
    INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, CASE CompteWindows WHEN '{Designe}' THEN 'BANC_SUIVI' ELSE 'BANC_AUTRE' END
    FROM soc.Compte WHERE CompteWindows IN ('{Designe}', '{Autre}');
    """);
appC = ServeurRig.Construire(["--environment", "Development"], s =>
{
    s.WebHost.UseTestServer();
    s.Logging.SetMinimumLevel(LogLevel.Warning);
    s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
    s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
    // La supervision est jouee par le banc (REC-005 CA05), passage par passage, comme le banc du lot 8.
    s.Configuration["Supervision:Automatique"] = "false";
    s.Configuration[SignatureDeFond.Reglage] = "0";
    s.Configuration[ReglagesPlanificateur.CleIntervalle] = "0";
});
await appC.StartAsync();
ClientServeur ClientEcran(string compte)
{
    Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, compte);
    try { return new ClientServeur(new Uri("http://localhost/"), appC.GetTestServer().CreateHandler()); }
    finally { Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null); }
}
var designe = ClientEcran(Designe);
var autre = ClientEcran(Autre);
var nonTraitesBase = await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.Ecart WHERE TraiteLe IS NULL");
var alerte = await EcranConflits.AlerteAsync(designe, Greffe);
var alerteAutre = await EcranConflits.AlerteAsync(autre, Greffe);
var vueDesigne = await EcranConflits.OuvrirAsync(designe, Greffe);
var vueAutre = await EcranConflits.OuvrirAsync(autre, Greffe);
var http = appC.GetTestClient();
http.DefaultRequestHeaders.Add(RoutesIdentite.EnTeteCompte, Autre);
var brut403 = await http.GetAsync(RoutesConflits.Pour(RoutesConflits.Ecarts, Greffe));
var ecartDossier = vueDesigne.Ecarts.FirstOrDefault(e => e.TableV2 == "DOSSIER_RCS");
var numGestion = ecartDossier is null ? null : await EnV3("DOSSIER_RCS", "DSSRC_NUM_GESTION", ecartDossier.IdV3);
var ecartClient = vueDesigne.Ecarts.FirstOrDefault(e => e.TableV2 == "CLIENT");
Exiger(alerte is not null && alerte.Nombre == nonTraitesBase && alerte.Cible == EcranConflits.Code && alerte.Titre.StartsWith($"{nonTraitesBase} fiche(s) modifiée(s) des deux côtés")
       && vueDesigne.Mode == "serveur" && vueDesigne.PeutTraiter && vueDesigne.Ecarts.Length == nonTraitesBase
       && ecartDossier?.Cible == $"dossier:{numGestion?.Trim()}" && ecartClient?.Cible == "ecran:PROC_FCLIENT"
       && alerteAutre is null && vueAutre.Mode == "refuse" && vueAutre.Ecarts.Length == 0 && brut403.StatusCode == HttpStatusCode.Forbidden,
    $"REC-004 CA02 : pour l'agent designe (fonction {RoutesConflits.FonctionSuivi}), l'alerte « {alerte?.Titre} » donne le nombre d'ecarts non traites ({alerte?.Nombre} = {nonTraitesBase} en base) " +
    $"et mene a l'ecran {alerte?.Cible} ; la liste montre les {vueDesigne.Ecarts.Length} ecarts, une fiche de dossier ouvre son affaire ({ecartDossier?.Cible}) et un client l'ecran " +
    $"des fiches clients ({ecartClient?.Cible}) ; un agent non designe n'a ni alerte ni liste (mode « {vueAutre.Mode} », route {(int)brut403.StatusCode})" +
    (vueDesigne.Mode == "serveur" ? "" : $" -- agent designe : mode « {vueDesigne.Mode} » : {vueDesigne.Message}"));
if (vueDesigne.Ecarts.Length < 2)
{
    var diagnostic = appC.GetTestClient();
    diagnostic.DefaultRequestHeaders.Add(RoutesIdentite.EnTeteCompte, Designe);
    var r = await diagnostic.GetAsync(RoutesConflits.Pour(RoutesConflits.Ecarts, Greffe));
    Info($"route des ecarts pour l'agent designe : {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    return Bilan();
}
var sansEcran = vueDesigne.Ecarts.Where(e => e.Cible is null).Select(e => e.TableV2).Distinct().ToList();
Reste($"REC-004 CA02 : {string.Join(", ", sansEcran)} n'ont pas encore d'ecran de fiche dans la V3 : leur ecart s'affiche sans « Ouvrir la fiche »");

// REC-004 CA02 sur l'ACCUEIL : l'agent designe ouvre l'accueil de la V3 (la session que la fenetre monte, Pont/Accueil.cs,
// branchee ici sur le vrai serveur en memoire) : l'alerte « fiche(s) modifiee(s) des deux cotes » est dans la tuile
// « A traiter », avec le nombre d'ecarts non traites ; son clic ouvre la liste (l'ecran « Fiches en conflit »). Jumeau :
// l'agent non designe ne l'a pas. Le clic est joue dans le vrai plan de travail (Pont/PlanTravail.cs) : sans serveur dans ce
// processus, son accueil montre la demonstration de la meme alerte, et le chemin du clic est le meme.
var etatAvant = Environment.GetEnvironmentVariable("RIGV3_ETAT");
var etatBanc = Path.Combine(Path.GetTempPath(), "rigv3-recopie-" + Guid.NewGuid().ToString("N") + ".json");
Environment.SetEnvironmentVariable("RIGV3_ETAT", etatBanc);
try
{
    await using var accueilDesigne = new SessionAccueil(AccueilEtat.Charger()) { SourceConflits = () => EcranConflits.AlerteAsync(designe, Greffe) };
    accueilDesigne.Initialiser();
    await accueilDesigne.ReleveConflits;
    var ligneAccueil = accueilDesigne.Alertes.FirstOrDefault(a => a.Id == SessionAccueil.IdAlerteConflits);
    var dansLaTuile = accueilDesigne.Vue(new ContexteAccueil(false, null)).Accueil.Tuiles.FirstOrDefault(t => t.Id == "alertes")?.Alertes?.Groupes
        .SelectMany(g => g.Lignes.Select(l => (g.Categorie, Ligne: l))).FirstOrDefault(x => x.Ligne.Id == SessionAccueil.IdAlerteConflits);
    var clic = accueilDesigne.Agir("alerte", JsonSerializer.SerializeToElement(SessionAccueil.IdAlerteConflits));
    await using var accueilAutre = new SessionAccueil(AccueilEtat.Charger()) { SourceConflits = () => EcranConflits.AlerteAsync(autre, Greffe) };
    accueilAutre.Initialiser();
    await accueilAutre.ReleveConflits;
    await using var plan = new SessionPlanTravail(null, null);
    plan.Initialiser();
    plan.SurGesteAccueil(new GesteIle("alerte", JsonSerializer.SerializeToElement(SessionAccueil.IdAlerteConflits)));
    var ouvert = plan.Vue().Proc?.Code;
    Exiger(ligneAccueil is { Cible: EcranConflits.Code } && ligneAccueil.Nombre == alerte!.Nombre && ligneAccueil.Titre == alerte.Titre
           && dansLaTuile?.Ligne is { } l && l.Nombre == alerte.Nombre && clic is { Quoi: DemandeAccueil.OuvrirAlerte, Alerte.Cible: EcranConflits.Code }
           && accueilAutre.Alertes.All(a => a.Id != SessionAccueil.IdAlerteConflits) && ouvert == EcranConflits.Code,
        $"REC-004 CA02 (accueil) : l'agent designe ouvre l'accueil de la V3 : la tuile « A traiter » ({dansLaTuile?.Categorie}) porte l'alerte « {ligneAccueil?.Titre} », " +
        $"{ligneAccueil?.Nombre} = les ecarts non traites du serveur ({alerte?.Nombre}) ; son clic demande l'ecran {clic?.Alerte?.Cible}, que le plan de travail ouvre " +
        $"(ecran ouvert : {ouvert ?? "aucun"}) ; l'agent non designe n'a pas l'alerte sur son accueil");
}
finally
{
    Environment.SetEnvironmentVariable("RIGV3_ETAT", etatAvant);
    try { File.Delete(etatBanc); } catch (IOException) { }
}

// L'ecran parle comme l'agent (refus du chef du 21/11) : « Client », « E-mail » -- les titres de la fiche de la V2 --, jamais
// CLIENT ni CLIEN_EMAIL. Chaque colonne de chaque fiche partagee a son libelle (une table ajoutee a la recopie ajoute les
// siens a LibellesRecopie). Le mot de passe d'un compte web client ne sort jamais du serveur, meme dans un ecart.
var sansLibelle = fiches.Tables.SelectMany(t => t.Colonnes.Concat(t.Propres).Append(t.Cle).Select(c => $"{t.NomV2}.{c.Nom}"))
    .Where(c => !LibellesRecopie.Connu(c.Split('.')[0], c.Split('.')[1])).ToList();
var champsVus = vueDesigne.Ecarts.SelectMany(e => e.Champs.Select(c => (e.TableV2, Champ: c))).ToList();
var ecartClientMdp = vueDesigne.Ecarts.First(e => e.TableV2 == "CLIENT");
await Executer(v3.Connexion, $"INSERT INTO rec.EcartChamp (IdEcart, Champ, ValeurV2, ValeurV3) VALUES ({ecartClientMdp.Id}, 'CLIEN_MDP', '\"secret-v2\"', '\"secret-v3\"');");
var champMdp = (await EcranConflits.OuvrirAsync(designe, Greffe)).Ecarts.First(e => e.Id == ecartClientMdp.Id).Champs.FirstOrDefault(c => c.Champ == "CLIEN_MDP");
await Executer(v3.Connexion, $"DELETE FROM rec.EcartChamp WHERE IdEcart = {ecartClientMdp.Id} AND Champ = 'CLIEN_MDP';");
Exiger(sansLibelle.Count == 0 && champsVus.Count > 0 && champsVus.All(x => x.Champ.Libelle == LibellesRecopie.Champ(x.TableV2, x.Champ.Champ) && x.Champ.Libelle != x.Champ.Champ)
       && vueDesigne.Ecarts.All(e => e.LibelleTable == LibellesRecopie.Table(e.TableV2) && e.LibelleTable != e.TableV2)
       && vueDesigne.Tables.All(t => t.Libelle == LibellesRecopie.Table(t.Code) && t.Libelle != t.Code)
       && LibellesRecopie.Champ("CLIENT", "CLIEN_EMAIL") == "E-mail" && LibellesRecopie.Table("CLIENT") == "Client"
       && champMdp is { Libelle: "Mot de passe", ValeurV2: "(masqué)", ValeurV3: "(masqué)" },
    $"REC-004 T03 (ecran) : les {champsVus.Count} champs des ecarts portent le titre de la fiche de la V2 (ex. {string.Join(", ", champsVus.Take(4).Select(x => $"{x.Champ.Champ} -> « {x.Champ.Libelle} »"))}), " +
    $"les tables leur nom d'agent ({string.Join(", ", vueDesigne.Tables.Select(t => $"{t.Code} -> « {t.Libelle} »"))}) ; " +
    $"{fiches.Tables.Sum(t => t.Colonnes.Count + t.Propres.Count + 1)} colonnes recopiees, {sansLibelle.Count} sans libelle {string.Join(", ", sansLibelle.Take(8))} ; " +
    $"le mot de passe d'un client se lit « {champMdp?.ValeurV2} »");

// CA03 : marquer traite ; vieillir deux ecarts de 5 et de 4 jours ouvres.
var aTraiter = vueDesigne.Ecarts[^1];
var refusTraite = await EcranConflits.TraiterAsync(autre, Greffe, aTraiter.Id, false, null);
var intact = await Valeur(v3.Connexion, $"SELECT TraiteLe FROM rec.Ecart WHERE Id = {aTraiter.Id}") is null;
var apresTraite = await EcranConflits.TraiterAsync(designe, Greffe, aTraiter.Id, false, null);
var traites = await EcranConflits.OuvrirAsync(designe, Greffe, traites: true);
var alerteApres = await EcranConflits.AlerteAsync(designe, Greffe);
var geste = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM soc.Journal WHERE Famille = 'ecart-recopie' AND Verbe = 'a valide' AND IdObjet = '{aTraiter.Id}' AND Ecran = '{ModuleConflitsNom.Nom}'");
var deTraite = traites.Ecarts.FirstOrDefault(e => e.Id == aTraiter.Id);
var aujourdhuiSql = (await MaintenantSql()).Date;
Exiger(refusTraite.Mode == "refuse" && intact && apresTraite.Mode == "serveur" && !apresTraite.Echec && apresTraite.Ecarts.All(e => e.Id != aTraiter.Id)
       && alerteApres?.Nombre == nonTraitesBase - 1 && deTraite is { TraitePar: "Banc DESIGNE", TraiteLe: not null } && deTraite.TraiteLe!.Value.Date == aujourdhuiSql && geste == 1,
    $"REC-004 CA03 : l'ecart {aTraiter.Id} marque traite par l'agent designe sort de l'alerte ({nonTraitesBase} -> {alerteApres?.Nombre}) et reste consultable avec son nom et la date " +
    $"(« {deTraite?.TraitePar} », {deTraite?.TraiteLe:dd/MM/yyyy HH:mm}), le geste au journal (« {apresTraite.Message} ») ; " +
    $"l'agent non designe est refuse sans que rien ne bouge (mode « {refusTraite.Mode} »)");

// Le rouge a 5 jours ouvres, mesure par un calendrier ecrit ici (jours feries legaux 2026-2027, Code du travail L3133-1).
var feries = new HashSet<DateTime>(new[] { "2026-01-01", "2026-04-06", "2026-05-01", "2026-05-08", "2026-05-14", "2026-05-25", "2026-07-14", "2026-08-15",
    "2026-11-01", "2026-11-11", "2026-12-25", "2027-01-01", "2027-03-29", "2027-05-01", "2027-05-06", "2027-05-08", "2027-05-17", "2027-07-14",
    "2027-08-15", "2027-11-01", "2027-11-11", "2027-12-25" }.Select(DateTime.Parse));
bool Ouvre(DateTime d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !feries.Contains(d.Date);
var maintenantSql = await MaintenantSql();
DateTime IlYa(int joursOuvres)
{
    var d = maintenantSql.Date;
    for (var n = 0; ; d = d.AddDays(-1)) if (Ouvre(d) && ++n == joursOuvres) break;
    return d.AddDays(-1).AddHours(15);
}
var restants = apresTraite.Ecarts;
await Executer(v3.Connexion, $"UPDATE rec.Ecart SET DetecteLe = '{IlYa(5):yyyy-MM-ddTHH:mm:ss}' WHERE Id = {restants[0].Id}; " +
                             $"UPDATE rec.Ecart SET DetecteLe = '{IlYa(4):yyyy-MM-ddTHH:mm:ss}' WHERE Id = {restants[1].Id};");
var vieux = await EcranConflits.OuvrirAsync(designe, Greffe);
var alerteRouge = await EcranConflits.AlerteAsync(designe, Greffe);
Exiger(vieux.Ecarts.Single(e => e.Id == restants[0].Id).EnRouge && !vieux.Ecarts.Single(e => e.Id == restants[1].Id).EnRouge
       && alerteRouge is { EnRouge: 1, Statut: "alerte" } && JoursOuvres.Entre(IlYa(5), maintenantSql) == 5 && JoursOuvres.Entre(IlYa(4), maintenantSql) == 4,
    $"REC-004 CA03 : un ecart non traite depuis 5 jours ouvres (vu le {IlYa(5):dd/MM}) passe en rouge, celui de 4 jours ouvres (le {IlYa(4):dd/MM}) non ; " +
    $"l'alerte de l'accueil devient « {alerteRouge?.Statut} » : « {alerteRouge?.Detail} » (week-ends et jours feries ne comptent pas)");

// ====================================================================================================
// C. REC-005 CA05 : l'exploitation d'Amitel alertee (le surveillant de SOC-011 lit AlertesRecopie), et sa relance
// ====================================================================================================
// « Quand il dure plus de 5 minutes ou met un changement de cote » : trois incidents de la recopie continue, chacun avec son
// jumeau sous le seuil (2 essais ; 4 minutes), et leur fin (l'alerte se ferme au passage suivant de la supervision).
var surveillant = appC.Services.GetRequiredService<Surveillant>();
var canal = (CanalAlerteSimule)appC.Services.GetRequiredService<ICanalAlerte>();
async Task<List<(string Erreur, bool Fermee)>> Incidents() =>
    [.. (await Lire(commune.Connexion, $"SELECT Erreur, CASE WHEN FermeeLe IS NULL THEN 0 ELSE 1 END FROM soc.AlerteExploitation " +
                                       $"WHERE Regle = '{AlertesRecopie.RegleIncident}' AND CodeGreffe = '{Greffe}' ORDER BY Id"))
        .Rows.Cast<DataRow>().Select(r => ((string)r[0], Convert.ToInt32(r[1]) == 1))];
async Task<List<string>> Ouverts() => [.. (await Incidents()).Where(a => !a.Fermee).Select(a => a.Erreur)];
string Court(IEnumerable<string> l) => string.Join(" | ", l.Select(e => e[..Math.Min(160, e.Length)]));
HttpClient Http(string compte)
{
    var c = appC.GetTestClient();
    c.DefaultRequestHeaders.Add(RoutesIdentite.EnTeteCompte, compte);
    return c;
}
const string Exploitant = "banc_amitel";
await Executer(v3.Connexion, $"""
    INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANC_EXPL', 'Banc : exploitation Amitel');
    INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive, EstAmitel, EstAdministrateur)
    SELECT '{Exploitant}', 'EXPLOITANT', 'Banc', 'BEXP', 'M.', '{Greffe}', (SELECT MAX(Id) FROM soc.Service WHERE Abrege = 'REC'), 0, 1, 0;
    INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC_EXPL' FROM soc.Compte WHERE CompteWindows = '{Exploitant}';
    """);

// 1. Un changement mis de cote : pas d'alerte a 2 essais, une alerte au 3e (base, table, erreur) ; l'exploitation lit la
// liste et relance par sa route (un agent du greffe est refuse), l'alerte se ferme.
await Executer(v2.Connexion, "ALTER TABLE dbo.CLIENT WITH NOCHECK ADD CONSTRAINT CK_BANC_REC005 CHECK (CLIEN_NOM NOT LIKE 'REFUSE PAR LA V2%');");
var cote = Prendre("CLIENT");
await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.CLIENT SET CLIEN_NOM = 'REFUSE PAR LA V2 (ALERTE)' WHERE CLIEN_ID_CLIEN = {cote};"));
await Passer();
await Passer();
await surveillant.PasserAsync();
var aDeuxEssais = await Ouverts();
await Passer();
await surveillant.PasserAsync();
var aTroisEssais = await Ouverts();
var envoyee = canal.Recues.LastOrDefault(a => a.Regle == AlertesRecopie.RegleIncident && a.CodeGreffe == Greffe);
var listeAgent = await Http(Designe).GetAsync(RoutesMisesDeCote.Pour(RoutesMisesDeCote.Liste, Greffe));
var listeExploitant = await Http(Exploitant).GetFromJsonAsync<ChangementMisDeCote[]>(RoutesMisesDeCote.Pour(RoutesMisesDeCote.Liste, Greffe)) ?? [];
var misDeCote = listeExploitant.FirstOrDefault(m => m.Fiche == cote);
await Executer(v2.Connexion, "ALTER TABLE dbo.CLIENT DROP CONSTRAINT CK_BANC_REC005;");
var demande = new DemandeRelanceRecopie(RoutesMisesDeCote.SensV3VersV2, misDeCote?.Ligne, null, null);
var relanceAgent = await Http(Designe).PostAsJsonAsync(RoutesMisesDeCote.Pour(RoutesMisesDeCote.Relancer, Greffe), demande);
var relanceExploitant = await Http(Exploitant).PostAsJsonAsync(RoutesMisesDeCote.Pour(RoutesMisesDeCote.Relancer, Greffe), demande);
var relanceVue = relanceExploitant.IsSuccessStatusCode ? await relanceExploitant.Content.ReadFromJsonAsync<ChangementMisDeCote>() : null;
var apresRelance = await Passer();
await surveillant.PasserAsync();
var finCote = await Ouverts();
Exiger(aDeuxEssais.Count == 0 && aTroisEssais.Count == 1 && aTroisEssais[0].Contains($"base {v2.Nom}") && aTroisEssais[0].Contains("table CLIENT")
       && aTroisEssais[0].Contains("CK_BANC_REC005") && aTroisEssais[0].Contains("mis de cote") && envoyee is not null
       && listeAgent.StatusCode == HttpStatusCode.Forbidden && misDeCote is { Sens: RoutesMisesDeCote.SensV3VersV2, Essais: 3, TableV2: "CLIENT" }
       && relanceAgent.StatusCode == HttpStatusCode.Forbidden && relanceVue is { RelancePar: not null }
       && apresRelance.Erreur is null && apresRelance.Appliquees == 1 && await EnV2("CLIENT", "CLIEN_NOM", cote) == "REFUSE PAR LA V2 (ALERTE)" && finCote.Count == 0,
    $"REC-005 CA05 (mis de cote) : a 2 essais, aucune alerte ; au 3e, l'exploitation recoit (canal {canal.Nom}) « {Court(aTroisEssais)} » ; elle lit la liste " +
    $"({listeExploitant.Length} changement(s), {misDeCote?.Essais} essais) et relance par sa route (relance par « {relanceVue?.RelancePar} » ; un agent du greffe : " +
    $"{(int)listeAgent.StatusCode} / {(int)relanceAgent.StatusCode}) ; le passage suivant l'applique et l'alerte se ferme ({finCote.Count} ouverte)");

// 2. Une panne de la base V2 qui dure : a 4 minutes pas d'alerte, a 6 minutes une alerte (base, table, erreur) ; fermee au retour.
var enPanne = Prendre("CLIENT");
List<string> panne4, panne6;
await Executer(serveurBancs, $"ALTER DATABASE [{v2.Nom}] SET OFFLINE WITH ROLLBACK IMMEDIATE;");
try
{
    await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.CLIENT SET CLIEN_NOM = 'PANNE' WHERE CLIEN_ID_CLIEN = {enPanne};"));
    await Passer();
    await Passer();
    // Le debut de la panne : le premier passage en erreur depuis le dernier passage reussi, recule de 4 puis de 6 minutes.
    const string Recul = "UPDATE rec.PassageRecopie SET Debut = DATEADD(minute, -{0}, Debut), Fin = DATEADD(minute, -{0}, Fin) WHERE Id = (SELECT MIN(Id) FROM rec.PassageRecopie " +
                         "WHERE Erreur IS NOT NULL AND Id > ISNULL((SELECT MAX(Id) FROM rec.PassageRecopie WHERE Erreur IS NULL), 0));";
    await Executer(v3.Connexion, string.Format(Recul, 4));
    await surveillant.PasserAsync();
    panne4 = await Ouverts();
    await Executer(v3.Connexion, string.Format(Recul, 2));
    await surveillant.PasserAsync();
    panne6 = await Ouverts();
}
finally
{
    await Executer(serveurBancs, $"ALTER DATABASE [{v2.Nom}] SET ONLINE;");
    SqlConnection.ClearAllPools();
}
var finPanne = await Passer();
await surveillant.PasserAsync();
var apresPanne = await Ouverts();
Exiger(panne4.Count == 0 && panne6.Count == 1 && panne6[0].Contains($"base {v2.Nom}") && panne6[0].Contains("table CLIENT") && panne6[0].Contains("depuis 6 min")
       && panne6[0].Contains("offline", StringComparison.OrdinalIgnoreCase) && finPanne.Erreur is null && finPanne.Appliquees == 1 && apresPanne.Count == 0,
    $"REC-005 CA05 (panne) : la base V2 coupee, les passages echouent ; a 4 minutes aucune alerte, a 6 minutes « {Court(panne6)} » ; au retour de la base le passage " +
    $"applique le changement en attente et l'alerte se ferme ({apresPanne.Count} ouverte)");

// 3. La recopie qui ne passe plus du tout (la boucle arretee, aucun passage) : le plus vieux changement de la V3 attend.
var enRetard = Prendre("CLIENT");
await Executer(v3.Connexion, V3Ecrit($"UPDATE v2.CLIENT SET CLIEN_NOM = 'RETARD' WHERE CLIEN_ID_CLIEN = {enRetard};"));
const string Vieillir = "UPDATE rec.FileRecopie SET Le = DATEADD(minute, -{0}, Le) WHERE Etat = 'en attente';";
await Executer(v3.Connexion, string.Format(Vieillir, 4));
await surveillant.PasserAsync();
var retard4 = await Ouverts();
await Executer(v3.Connexion, string.Format(Vieillir, 2));
await surveillant.PasserAsync();
var retard6 = await Ouverts();
var finRetard = await Passer();
await surveillant.PasserAsync();
var apresRetard = await Ouverts();
Exiger(retard4.Count == 0 && retard6.Count == 1 && retard6[0].Contains($"base {v2.Nom}") && retard6[0].Contains("table CLIENT") && retard6[0].Contains("depuis 6 min")
       && finRetard.Erreur is null && finRetard.Appliquees == 1 && apresRetard.Count == 0,
    $"REC-005 CA05 (retard) : sans aucun passage, un changement de la V3 qui attend depuis 4 minutes n'alerte pas ; depuis 6 minutes : « {Court(retard6)} » ; " +
    $"le passage suivant l'applique et l'alerte se ferme ({apresRetard.Count} ouverte)");
}
finally
{
    if (appB is not null) await appB.DisposeAsync();
    if (appC is not null) { await appC.StopAsync(); await appC.DisposeAsync(); }
}

Reste("REC-003 T01 : le cout du suivi des changements se mesure ici sur une base jetable ; la mesure sur la copie anonymisee de la plus grosse base attend REC-008 (lot 8)");
return Bilan();

int Bilan()
{
    Console.WriteLine();
    if (echecs.Count == 0)
    {
        Console.WriteLine("VERT : la recopie dans les deux sens, les fiches en conflit et la reprise de la file tiennent leurs criteres (REC-002, REC-003, REC-004, REC-005 CA01-CA03 et CA05).");
        return 0;
    }
    Console.WriteLine($"ROUGE : {echecs.Count} controle(s) en echec.");
    foreach (var e in echecs) Console.WriteLine("  - " + e);
    return 1;
}

/// <summary>Le serveur de la partie B, arrete AVANT d'etre detruit (decision du lot 1 : sinon « BackgroundService failed »).</summary>
sealed class BaseRigApp(Microsoft.AspNetCore.Builder.WebApplication app) : IAsyncDisposable
{
    public Microsoft.AspNetCore.Builder.WebApplication App { get; } = app;
    public async ValueTask DisposeAsync()
    {
        try { await App.StopAsync(); } catch (ObjectDisposedException) { }
        await App.DisposeAsync();
    }
}

/// <summary>Le nom d'ecran que le module ecrit au journal (ModuleConflits.NomEcran, interne au serveur).</summary>
static class ModuleConflitsNom
{
    public const string Nom = "Fiches en conflit";
}
