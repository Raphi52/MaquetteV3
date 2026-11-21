using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RecopieTransfertCheck;
using RigV3.Base;
using RigV3.Serveur.Recopie;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Transfert;

// Banc de REC-001 (carnet\recopie.json) : transferer chaque base de l'ancien RIG en une nuit, telle qu'elle est.
// Lot 7 du plan de reconstruction, phase 2 (2026-11-21). Un controle par critere, qui cite son id.
//
//  0. Une base V2 jetable construite a partir d'un EXTRAIT de RIG_DEV (ExtraitRigDev : RIG_DEV lu en lecture seule) :
//     les 9 tables « telles quelles » de REC-001, UTILISATEUR et les 17 tables des reprises de la phase 1. On y ajoute
//     une colonne (CA01, jumeau accepte) et deux liens casses herites, cle etrangere desactivee (CA04).
//  A. Transfert par l'OUTIL (RigV3.Transfert transferer) : toutes les tables, valeurs identiques (EXCEPT dans les deux
//     sens, contre-epreuve comprise), lues = ecrites + rejetees et correspondance (CA03), reprises appelees, liens
//     marques (CA04), V2 inchangee (CA06), duree mesuree et extrapolee a la plus grosse base connue (CA02).
//  D. Regle « rejeter » sur DEMANDE, et comptes deja repris : rejet avec motif, demande fille marquee, reprise non rejouee.
//  B. Des agents ecrivent dans la V2 pendant le transfert (CA05) : rien de bloque, etat fige, et la version du suivi des
//     changements donne exactement ce que la recopie continue (REC-003) devra rejouer.
//  E. Un ecart pendant l'ecriture (une colonne convertie en silence dans la V3) : ARRET PROPRE.
//  C. Schema different (CA01, T03) : table en plus sans regle, puis table attendue absente -> refus avant toute ecriture.
// Il lui faut « docker compose up -d » et l'acces en lecture a RIG_DEV (autre serveur : RIGV3_RIG_DEV).
// Doc : docs/reconstruction/recopie.md.

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

var reglesServeur = ReglesTransfert.Decouvrir();
// Les tables copiees telles quelles dans v2.X ; depuis le lot 10, les fiches partagees ont leur table dans la V3 (rec.Client...,
// RegleTable.Cible) et leur banc (RecopieFichesCheck) : ce banc garde la copie telle quelle et le rapport de toutes.
var tellesQuelles = reglesServeur.Regles.Where(r => r.Sorte == SorteRegle.TelleQuelle && r.Cible is null).Select(r => r.Table).ToList();
var tablesPrevues = new[] { "AFFAIRE", "DEMANDE", "ACTION_RCS", "ADRESSE_BASE", "ARRIVEE", "CATEGORIE", "CLIENT", "DOSSIER_RCS", "INSCRIPTION", "UTILISATEUR" };
Exiger(tablesPrevues.All(t => reglesServeur.Regles.Any(r => r.Table == t)) &&
       reglesServeur.Reprises.Select(r => r.Nom).SequenceEqual(["reglages du greffe", "comptes et droits", "types de demande par reference", "anciens numeros",
                                                                "adresse inconnue unique", "compteurs de numeros"]),
    $"REC-001 T03 : chaque table de tables_v2 de REC-001 a sa regle ({tellesQuelles.Count} telles quelles, UTILISATEUR par la reprise des comptes), " +
    $"et les 3 reprises de la phase 1, les 2 du lot 10 et celle du lot 11 (REC-009) sont branchees dans l'ordre : {string.Join(", ", reglesServeur.Reprises.Select(r => $"{r.Nom} ({r.Story})"))}");

// Le catalogue REEL de RIG_DEV, lu par le moteur (CA01) : ses tables telles quelles se recreent sur ce poste avec les memes
// types. RIG_DEV garde un classement sur des colonnes bit et datetime, que la base jetable ne peut pas reproduire : c'est
// ici, et seulement ici, que la regle « classement sur les seuls types texte » du moteur est essayee.
CatalogueV2 catalogueRigDev;
await using (var rd = new SqlConnection(chaineRigDev))
{
    await rd.OpenAsync();
    catalogueRigDev = await CatalogueV2.LireAsync(rd, default);
}
var direct = (await Lire(chaineRigDev, "SELECT (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0 AND name NOT IN ('sysdiagrams', 'dtproperties')), " +
    "(SELECT COUNT(*) FROM sys.foreign_keys), (SELECT COUNT(*) FROM sys.triggers WHERE parent_class = 1)")).Rows[0];
await using (var ddl = await BaseJetable.CreerAsync("RecopieTransfertDdl"))
{
    await Executer(ddl.Connexion, "EXEC (N'CREATE SCHEMA v2');");
    var ecarts = new List<string>();
    foreach (var t in tablesPrevues.Where(t => tellesQuelles.Contains(t)))
    {
        await Executer(ddl.Connexion, CopieTelleQuelle.Ddl(catalogueRigDev.Tables[t]));
        var attendu = await Lire(chaineRigDev, $"SELECT name, TYPE_NAME(system_type_id), max_length, precision, scale, is_nullable, is_identity FROM sys.columns WHERE object_id = OBJECT_ID('dbo.{t}') ORDER BY column_id");
        var obtenu = await Lire(ddl.Connexion, $"SELECT name, TYPE_NAME(system_type_id), max_length, precision, scale, is_nullable, is_identity FROM sys.columns WHERE object_id = OBJECT_ID('v2.{t}') ORDER BY column_id");
        string Forme(DataTable d) => string.Join(";", d.Rows.Cast<DataRow>().Select(r => string.Join(",", r.ItemArray)));
        if (Forme(attendu) != Forme(obtenu)) ecarts.Add(t);
    }
    var bitsClasses = await Nombre(chaineRigDev, "SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE c.collation_name IS NOT NULL " +
                                                 "AND TYPE_NAME(c.system_type_id) NOT IN ('varchar', 'char', 'nvarchar', 'nchar', 'text', 'ntext', 'sysname')");
    Exiger(catalogueRigDev.Tables.Count == Convert.ToInt32(direct[0]) && catalogueRigDev.NombreCles == Convert.ToInt32(direct[1])
           && catalogueRigDev.NombreDeclencheurs == Convert.ToInt32(direct[2]) && ecarts.Count == 0 && bitsClasses > 0,
        $"REC-001 CA01 : le moteur lit le catalogue reel de RIG_DEV ({catalogueRigDev.Tables.Count} tables, {catalogueRigDev.NombreCles} cles etrangeres, " +
        $"{catalogueRigDev.NombreDeclencheurs} declencheurs : les nombres de SQL Server) et recree ses tables telles quelles avec les memes colonnes, types, " +
        $"tailles et identites, malgre les {bitsClasses} colonnes non texte qui y portent un classement {string.Join(", ", ecarts)}");
}
var sansRegle = catalogueRigDev.Tables.Keys.Count(t => !reglesServeur.Regles.Any(r => string.Equals(r.Table, t, StringComparison.OrdinalIgnoreCase)));
Reste($"REC-001 T03 sur une base entiere : RIG_DEV a {sansRegle} tables sans regle ; le moteur refuse donc de la transferer tant que la story de chacune " +
      "n'a pas declare sa regle (telle quelle ou colonne par colonne) dans Serveur\\Recopie — c'est la regle de T03, la liste est imprimee au refus");

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
// L'outil transfere avec les regles du serveur : la base V2 a chacune de leurs tables (REC-012 a REC-019 depuis le lot 10).
extraits = ExtraitRigDev.Completer(extraits, entieres, reglesServeur.Regles.Select(r => r.Table));
await using var v2 = await BaseJetable.CreerAsync("RecopieTransfertV2");
var chronoExtrait = Stopwatch.StartNew();
var extrait = await ExtraitRigDev.ConstruireAsync(chaineRigDev, v2.Connexion, entieres, extraits, Info);
Info($"base V2 jetable {v2.Nom} : {extrait.Lignes.Values.Sum()} lignes dans {extrait.Lignes.Count} tables, {extrait.Cles} cles etrangeres verifiees, " +
     $"{extrait.Declencheurs} declencheurs recrees (desactives) en {chronoExtrait.Elapsed.TotalSeconds:0} s ; " +
     string.Join(", ", extrait.Lignes.Where(l => extraits.ContainsKey(l.Key)).Select(l => $"{l.Key} {l.Value}")));
foreach (var d in extrait.DeclencheursNonRecrees) Info("declencheur non recree : " + d);
Exiger(extrait.Lignes.Count == reglesServeur.Regles.Select(r => r.Table).Concat(entieres).Distinct(StringComparer.OrdinalIgnoreCase).Count()
       // Les tables des anciennes reprises (REC-009, lot 11) sont vides sur RIG_DEV, qui n'est pas un greffe repris (lu le 2026-11-20).
       && extrait.Lignes.All(l => l.Value > 0 || l.Key is "NUMERO_METIER_SAVE_CDU" or "MAIL_TEMPLATE" or "CLIENT_CONTACT" or "BATEAU"
                                                   or "CORRESP_MIGRATION_RIG" or "CORRESP_ORACLE_RIG" or "CORRESP_GAGI_RIG" or "MIGRATION_REFERENCE"
                                                   or "MIGRATION_TABLE" or "MIGRATION_CHAMP" or "MIGRATION_ID"),
    $"base V2 jetable tiree de RIG_DEV : {extrait.Lignes.Count} tables, fermee sur ses {extrait.Cles} cles etrangeres (reposees avec verification)");

// La colonne en plus (CA01, jumeau accepte) et les deux liens casses herites (CA04), cle desactivee comme dans une base reprise.
await Executer(v2.Connexion, "ALTER TABLE dbo.ARRIVEE ADD COLONNE_EN_PLUS_REC001 varchar(30) NULL;");
await Executer(v2.Connexion, "UPDATE dbo.ARRIVEE SET COLONNE_EN_PLUS_REC001 = 'en plus ' + CONVERT(varchar(12), ARRV_ID_ARRV);");
var cleAdresse = (string)(await Valeur(v2.Connexion, "SELECT name FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('dbo.DEMANDE') AND referenced_object_id = OBJECT_ID('dbo.ADRESSE_BASE')"))!;
var cleClient = (string)(await Valeur(v2.Connexion, "SELECT name FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('dbo.AFFAIRE') AND referenced_object_id = OBJECT_ID('dbo.CLIENT')"))!;
var cleMere = (string)(await Valeur(v2.Connexion, "SELECT f.name FROM sys.foreign_keys f JOIN sys.foreign_key_columns c ON c.constraint_object_id = f.object_id " +
    "WHERE f.parent_object_id = OBJECT_ID('dbo.DEMANDE') AND COL_NAME(c.parent_object_id, c.parent_column_id) = 'DMND_ID_DMND_MERE'"))!;
var demandeCassee = await Nombre(v2.Connexion, "SELECT MIN(DMND_ID_DMND) FROM dbo.DEMANDE");
var demandeFille = await Nombre(v2.Connexion, "SELECT MAX(DMND_ID_DMND) FROM dbo.DEMANDE");
var affaireCassee = await Nombre(v2.Connexion, "SELECT MIN(AFFRE_ID_AFFRE) FROM dbo.AFFAIRE");
const long AdresseAbsente = 999_999_999, ClientAbsent = 999_999_998;
await Executer(v2.Connexion, $"ALTER TABLE dbo.DEMANDE NOCHECK CONSTRAINT [{cleAdresse}]; UPDATE dbo.DEMANDE SET DMND_ID_ADRBS = {AdresseAbsente} WHERE DMND_ID_DMND = {demandeCassee};");
await Executer(v2.Connexion, $"ALTER TABLE dbo.AFFAIRE NOCHECK CONSTRAINT [{cleClient}]; UPDATE dbo.AFFAIRE SET AFFRE_ID_CLIEN = {ClientAbsent} WHERE AFFRE_ID_AFFRE = {affaireCassee};");
await Executer(v2.Connexion, $"UPDATE dbo.DEMANDE SET DMND_ID_DMND_MERE = {demandeCassee} WHERE DMND_ID_DMND = {demandeFille};");

// L'empreinte de la V2 (CA06) : chaque table (lignes, CHECKSUM_AGG de BINARY_CHECKSUM et de CHECKSUM), et les ecritures
// physiques que SQL Server a comptees sur ses index (insertions, mises a jour, suppressions).
async Task<string> Empreinte()
{
    var parts = new List<string>();
    foreach (DataRow r in (await Lire(v2.Connexion, "SELECT name FROM sys.tables WHERE is_ms_shipped = 0 ORDER BY name")).Rows)
    {
        var t = (string)r[0];
        var e = await Lire(v2.Connexion, $"SELECT COUNT_BIG(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)), CHECKSUM_AGG(CHECKSUM(*)) FROM dbo.[{t}]");
        parts.Add($"{t}:{e.Rows[0][0]}:{e.Rows[0][1]}:{e.Rows[0][2]}");
    }
    return string.Join("|", parts);
}
async Task<long> EcrituresPhysiques() => await Nombre(v2.Connexion,
    "SELECT SUM(s.leaf_insert_count + s.leaf_update_count + s.leaf_delete_count + s.leaf_ghost_count) " +
    "FROM sys.dm_db_index_operational_stats(DB_ID(), NULL, NULL, NULL) s JOIN sys.tables t ON t.object_id = s.object_id");

var v3s = new List<BaseJetable>();
async Task<BaseJetable> NouvelleV3(string nom)
{
    var b = await BaseJetable.CreerAsync(nom);
    v3s.Add(b);
    await Migrateur.AppliquerAsync(b.Connexion, migrations);
    return b;
}
async Task<int> InstantanesRestes() => (int)await Nombre(v2.Connexion,
    $"SELECT COUNT(*) FROM sys.databases WHERE source_database_id = DB_ID() OR name LIKE '{v2.Nom}[_]rigv3[_]%'");

try
{
// ====================================================================================================
// A. Le transfert par l'outil
// ====================================================================================================
var v3A = await NouvelleV3("RecopieTransfertA");
var avant = await Empreinte();
var ecrituresAvant = await EcrituresPhysiques();
var sortieA = new StringWriter();
var codeA = await LigneDeCommande.ExecuterAsync(["transferer", "--v2", v2.Connexion, "--v3", v3A.Connexion, "--lot", "5000"], sortieA);
var texteA = sortieA.ToString();
var apres = await Empreinte();
var ecrituresApres = await EcrituresPhysiques();
foreach (var l in texteA.Split('\n').Where(l => Regex.IsMatch(l, @"\b(schema|reprise|fin|arret|refus)\b") || l.TrimStart().StartsWith("Transfert ")))
    Info("outil : " + l.TrimEnd());
var transfertA = (await Lire(v3A.Connexion, "SELECT TOP 1 Id, Etat, Motif, DureeMs, Instantane, InstantaneLe, Lues, Ecrites, Rejetees, LiensCasses FROM rec.Transfert ORDER BY Id DESC")).Rows[0];
var idA = (int)transfertA["Id"];
Exiger(codeA == 0 && (string)transfertA["Etat"] == "fini",
    $"REC-001 : l'outil « RigV3.Transfert transferer » rend 0 et le journal dit « fini » (transfert {idA}, {transfertA["Lues"]} lignes lues, {transfertA["Ecrites"]} ecrites) {transfertA["Motif"]}");

var tablesA = await Lire(v3A.Connexion, $"SELECT TableV2, Ordre, Regle, Story, TableV3, Lues, Ecrites, Rejetees, LiensCasses, Correspondances, Octets, DureeMs FROM rec.TransfertTable WHERE IdTransfert = {idA}");
var parTableA = tablesA.Rows.Cast<DataRow>().ToDictionary(r => (string)r["TableV2"], StringComparer.OrdinalIgnoreCase);
var presentes = reglesServeur.Regles.Where(r => extrait.Lignes.ContainsKey(r.Table)).Select(r => r.Table).ToList();
Exiger(presentes.All(parTableA.ContainsKey) && tablesPrevues.All(t => parTableA.TryGetValue(t, out var r) && Convert.ToInt64(r["Ecrites"]) == extrait.Lignes[t]),
    $"REC-001 : toutes les tables prevues transferees — les {tablesPrevues.Length} de tables_v2 ecrites en entier " +
    $"({string.Join(", ", tablesPrevues.Select(t => $"{t} {(parTableA.TryGetValue(t, out var r) ? r["Ecrites"] : "absente")}"))}) et une ligne de rapport pour chacune des {presentes.Count} tables a regle");

// CA03 : lues = ecrites + rejetees, correspondance de chaque ligne ecrite, identifiants V2 et V3 justes.
var ca03 = new List<string>();
foreach (var t in tellesQuelles)
{
    var r = parTableA[t];
    var lues = Convert.ToInt64(r["Lues"]); var ecrites = Convert.ToInt64(r["Ecrites"]); var rejetees = Convert.ToInt64(r["Rejetees"]);
    var pk = (string)(await Valeur(v2.Connexion, $"SELECT c.name FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id " +
                                                 $"JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.is_primary_key = 1 AND i.object_id = OBJECT_ID('dbo.{t}')"))!;
    var justes = await Nombre(v3A.Connexion, $"SELECT COUNT(*) FROM rec.Correspondance c JOIN v2.[{t}] x ON CONVERT(nvarchar(200), x.[{pk}]) = c.IdV2 AND c.IdV3 = c.IdV2 " +
                                            $"WHERE c.IdTransfert = {idA} AND c.TableV2 = '{t}' AND c.TableV3 = 'v2.{t}'");
    if (lues != extrait.Lignes[t] || lues != ecrites + rejetees || justes != ecrites || Convert.ToInt64(r["Correspondances"]) != ecrites)
        ca03.Add($"{t} (lues {lues}, ecrites {ecrites}, rejetees {rejetees}, correspondances justes {justes})");
}
foreach (var t in new[] { "UTILISATEUR", "SERVICE", "PARAMETRAGE" })
{
    var r = parTableA[t];
    if (Convert.ToInt64(r["Lues"]) != extrait.Lignes[t] || Convert.ToInt64(r["Ecrites"]) != extrait.Lignes[t] || Convert.ToInt64(r["Correspondances"]) != extrait.Lignes[t])
        ca03.Add($"{t} (reprise : lues {r["Lues"]}, ecrites {r["Ecrites"]})");
}
var comptesRelies = await Nombre(v3A.Connexion, $"SELECT COUNT(*) FROM rec.Correspondance c JOIN soc.Compte k ON CONVERT(nvarchar(200), k.Id) = c.IdV3 " +
                                               $"AND CONVERT(nvarchar(200), k.IdV2) = c.IdV2 WHERE c.IdTransfert = {idA} AND c.TableV2 = 'UTILISATEUR' AND c.TableV3 = 'soc.Compte'");
Exiger(ca03.Count == 0 && comptesRelies == extrait.Lignes["UTILISATEUR"],
    $"REC-001 CA03 : pour chaque table, lues = ecrites + rejetees, et chaque ligne ecrite a son identifiant V2 et son identifiant V3 dans rec.Correspondance " +
    $"({tellesQuelles.Sum(t => Convert.ToInt64(parTableA[t]["Ecrites"]))} lignes telles quelles ; UTILISATEUR -> soc.Compte : {comptesRelies} comptes relies) " +
    $"{string.Join(" ; ", ca03)}");

// Valeurs identiques (oracle independant du moteur : EXCEPT dans les deux sens, ligne a ligne, colonne a colonne).
async Task<List<string>> Differences(BaseJetable v3, IEnumerable<string> tables, IReadOnlyDictionary<string, long>? sauf = null)
{
    var diff = new List<string>();
    foreach (var t in tables)
    {
        var filtre = sauf is not null && sauf.TryGetValue(t, out var id) ? $" WHERE DMND_ID_DMND <> {id}" : "";
        var a = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM (SELECT * FROM [{v2.Nom}].dbo.[{t}]{filtre} EXCEPT SELECT * FROM v2.[{t}]) x");
        var b = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM (SELECT * FROM v2.[{t}] EXCEPT SELECT * FROM [{v2.Nom}].dbo.[{t}]) x");
        if (a + b > 0) diff.Add($"{t} : {a} lignes V2 sans leur pareille en V3, {b} lignes V3 sans leur pareille en V2");
    }
    return diff;
}
var diffA = await Differences(v3A, tellesQuelles);
var colonneEnPlus = await Nombre(v3A.Connexion, $"SELECT COUNT(*) FROM v2.ARRIVEE c JOIN [{v2.Nom}].dbo.ARRIVEE o ON o.ARRV_ID_ARRV = c.ARRV_ID_ARRV AND o.COLONNE_EN_PLUS_REC001 = c.COLONNE_EN_PLUS_REC001");
Exiger(diffA.Count == 0,
    $"REC-001 : valeurs identiques — pour les {tellesQuelles.Count} tables telles quelles, chaque ligne de la V2 a sa pareille exacte dans la V3 et reciproquement " +
    $"(EXCEPT, toutes colonnes) {string.Join(" ; ", diffA)}");
// Contre-epreuve : une seule valeur changee dans la V3 rougit l'oracle.
var clientTemoin = await Nombre(v3A.Connexion, "SELECT MAX(ARRV_ID_ARRV) FROM v2.ARRIVEE");
await Executer(v3A.Connexion, $"UPDATE v2.ARRIVEE SET COLONNE_EN_PLUS_REC001 = 'change' WHERE ARRV_ID_ARRV = {clientTemoin}");
var diffTemoin = await Differences(v3A, ["ARRIVEE"]);
await Executer(v3A.Connexion, $"UPDATE v2.ARRIVEE SET COLONNE_EN_PLUS_REC001 = 'en plus ' + CONVERT(varchar(12), ARRV_ID_ARRV) WHERE ARRV_ID_ARRV = {clientTemoin}");
Exiger(diffTemoin.Count == 1 && diffTemoin[0].Contains("1 lignes V2") && (await Differences(v3A, ["ARRIVEE"])).Count == 0,
    $"REC-001 contre-epreuve : une valeur changee dans la V3 (ARRIVEE {clientTemoin}) est vue par l'oracle ({string.Join("", diffTemoin)}), et plus rien une fois remise");

// CA01, jumeau accepte : une colonne en plus est imprimee et copiee telle quelle, le transfert continue.
var journalA = await Lire(v3A.Connexion, $"SELECT Genre, Message FROM rec.TransfertEvenement WHERE IdTransfert = {idA} ORDER BY Id");
var lignesJournalA = journalA.Rows.Cast<DataRow>().Select(r => ((string)r[0], (string)r[1])).ToList();
Exiger(lignesJournalA.Any(l => l.Item1 == "schema" && l.Item2.Contains("colonne en plus : ARRIVEE.COLONNE_EN_PLUS_REC001")) && texteA.Contains("ARRIVEE.COLONNE_EN_PLUS_REC001")
       && colonneEnPlus == extrait.Lignes["ARRIVEE"],
    $"REC-001 CA01 (jumeau accepte) : la colonne en plus de la base est imprimee et au journal, puis copiee telle quelle ({colonneEnPlus} valeurs identiques) ; une colonne ne bloque pas");

// Les reprises de la phase 1 : appelees (journal), leurs tables remplies par leur propre code, et jamais refaites ici.
var reprisesA = lignesJournalA.Where(l => l.Item1 == "reprise").Select(l => l.Item2).ToList();
var numA = (await Lire(v3A.Connexion, "SELECT TOP 1 Code, LignesV2, Compteurs FROM num.Reprise ORDER BY Id DESC")).Rows.Cast<DataRow>().FirstOrDefault();
var comptesA = await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM soc.Compte");
var reglagesA = await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM soc.Reglage");
var compteursA = await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM num.Compteur");
var ecrituresDirectes = Directory.GetFiles(Path.Combine(racine, "Serveur", "Recopie"), "*.cs")
    .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM|MERGE)\s+(soc|num)\.\w+", RegexOptions.IgnoreCase)
                          .Select(m => $"{Path.GetFileName(f)} : {m.Value}")).ToList();
Exiger(reglesServeur.Reprises.All(r => reprisesA.Any(m => m.StartsWith($"Reprise « {r.Nom} » : "))) && numA is not null && (int)numA["Code"] == 0
       && comptesA == extrait.Lignes["UTILISATEUR"] && reglagesA == extrait.Lignes["PARAMETRAGE"] && compteursA > 0 && ecrituresDirectes.Count == 0,
    $"REC-001 : les reprises de la phase 1 sont appelees et non refaites — reglages ({reglagesA} soc.Reglage), comptes et droits ({comptesA} soc.Compte), " +
    $"num.Reprendre (code {numA?["Code"]}, {numA?["LignesV2"]} lignes V2, {compteursA} compteurs) ; Serveur\\Recopie n'ecrit aucune table soc. ni num. lui-meme " +
    $"({(ecrituresDirectes.Count == 0 ? "aucune ecriture" : string.Join(", ", ecrituresDirectes))})");
var ordreReprises = lignesJournalA.Select((l, i) => (l, i)).Where(x => x.l.Item1 is "reprise" or "table").Select(x => x.l.Item2).ToList();
// Le journal ecrit « Reprise « <nom> » (<story>) : appel » (MoteurTransfert.ReprendreAsync).
int Appel(string nom) => ordreReprises.FindIndex(m => m.StartsWith($"Reprise « {nom} » (") && m.EndsWith(": appel"));
Exiger(Appel("reglages du greffe") >= 0 && Appel("reglages du greffe") < Appel("comptes et droits")
       && Appel("comptes et droits") < ordreReprises.FindIndex(m => m.Contains(": copie telle quelle"))
       && ordreReprises.FindLastIndex(m => m.Contains(": copie telle quelle")) < Appel("compteurs de numeros"),
    "REC-001 : reglages et comptes repris avant les tables ; les compteurs en dernier, dans la V2 vivante (NUM-005 : leur derniere valeur)");

// L'ordre d'ecriture deduit des cles etrangeres.
// Toutes les tables copiees par lots, telles quelles ou colonne par colonne (lot 10) : un seul ordre d'ecriture.
var ordreTables = tablesA.Rows.Cast<DataRow>().Where(r => (string)r["Regle"] is "telle quelle" or "colonne par colonne").ToDictionary(r => (string)r["TableV2"], r => (int)r["Ordre"]);
var liensTQ = await Lire(v2.Connexion, "SELECT OBJECT_NAME(parent_object_id), OBJECT_NAME(referenced_object_id) FROM sys.foreign_keys WHERE parent_object_id <> referenced_object_id");
var inverses = liensTQ.Rows.Cast<DataRow>().Where(r => ordreTables.ContainsKey((string)r[0]) && ordreTables.ContainsKey((string)r[1]) && ordreTables[(string)r[0]] <= ordreTables[(string)r[1]])
    .Select(r => $"{r[0]} -> {r[1]}").ToList();
Exiger(inverses.Count == 0 && ordreTables.Count == reglesServeur.Regles.Count(r => r.Sorte == SorteRegle.TelleQuelle),
    $"REC-001 T02 : ordre d'ecriture deduit des cles etrangeres, chaque table apres celles qu'elle vise : " +
    $"{string.Join(", ", ordreTables.OrderBy(o => o.Value).Select(o => o.Key))} {string.Join(", ", inverses)}");

// Catalogue relu : cles et declencheurs de la base elle-meme.
var clesV2 = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM sys.foreign_keys");
var declV2 = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM sys.triggers WHERE parent_class = 1");
var catalogueA = lignesJournalA.FirstOrDefault(l => l.Item1 == "catalogue").Item2 ?? "";
Exiger(catalogueA.Contains($"{clesV2} cles etrangeres (2 desactivees") && catalogueA.Contains($"{declV2} declencheurs") && declV2 > 0,
    $"REC-001 T02 : le transfert lit le catalogue REEL de la base (« {catalogueA} ») : ses {clesV2} cles, dont les 2 desactivees de l'extrait, et ses {declV2} declencheurs");

// CA04, regle « marquer » : la ligne au lien casse est ecrite avec SA valeur, le lien est note, le transfert ne s'arrete pas.
var marquees = await Lire(v3A.Connexion, $"SELECT TableV2, IdV2, Cle, Valeur, Cause FROM rec.LienCasse WHERE IdTransfert = {idA} ORDER BY TableV2");
var adresseGardee = await Nombre(v3A.Connexion, $"SELECT DMND_ID_ADRBS FROM v2.DEMANDE WHERE DMND_ID_DMND = {demandeCassee}");
var clientGarde = await Nombre(v3A.Connexion, $"SELECT AFFRE_ID_CLIEN FROM v2.AFFAIRE WHERE AFFRE_ID_AFFRE = {affaireCassee}");
bool Marque(string table, long id, string cle, long valeur) => marquees.Rows.Cast<DataRow>().Any(r => (string)r[0] == table && (string)r[1] == id.ToString()
    && (string)r[2] == cle && (string)r[3] == valeur.ToString() && (string)r[4] == "absente de la V2");
Exiger(marquees.Rows.Count == 2 && Marque("DEMANDE", demandeCassee, cleAdresse, AdresseAbsente) && Marque("AFFAIRE", affaireCassee, cleClient, ClientAbsent)
       && adresseGardee == AdresseAbsente && clientGarde == ClientAbsent && Convert.ToInt64(parTableA["DEMANDE"]["LiensCasses"]) == 1
       && Convert.ToInt64(transfertA["LiensCasses"]) == 2 && (string)transfertA["Etat"] == "fini",
    $"REC-001 CA04 (regle « marquer ») : la demande {demandeCassee} (adresse {AdresseAbsente} absente) et l'affaire {affaireCassee} (client {ClientAbsent} absent) " +
    $"sont ecrites avec leur valeur telle quelle, chaque lien note dans rec.LienCasse et compte au rapport ({transfertA["LiensCasses"]}), sans arret");

// CA06 : la V2 n'a pas ete ecrite.
Exiger(avant == apres && ecrituresApres == ecrituresAvant && await InstantanesRestes() == 0,
    $"REC-001 CA06 : la base V2 est identique avant et apres le transfert (empreinte de ses {avant.Split('|').Length} tables, " +
    $"{ecrituresApres - ecrituresAvant} ecriture physique comptee par SQL Server sur ses index) ; l'instantane est supprime");
await Executer(v2.Connexion, $"UPDATE dbo.ARRIVEE SET COLONNE_EN_PLUS_REC001 = 'temoin' WHERE ARRV_ID_ARRV = {clientTemoin}");
var empreinteTemoin = await Empreinte();
await Executer(v2.Connexion, $"UPDATE dbo.ARRIVEE SET COLONNE_EN_PLUS_REC001 = 'en plus ' + CONVERT(varchar(12), ARRV_ID_ARRV) WHERE ARRV_ID_ARRV = {clientTemoin}");
Exiger(empreinteTemoin != avant && await EcrituresPhysiques() > ecrituresApres,
    "REC-001 CA06 contre-epreuve : une seule valeur ecrite dans la V2 change son empreinte et le compte des ecritures physiques");

// CA02 : duree mesuree, et extrapolee a la plus grosse base connue de ce poste (RIG_DEV : les 40 bases ne se lisent pas d'ici).
var dureeA = Convert.ToInt64(transfertA["DureeMs"]);
var tq = tablesA.Rows.Cast<DataRow>().Where(r => (string)r["Regle"] == "telle quelle").ToList();
var msTables = tq.Sum(r => Convert.ToInt64(r["DureeMs"]));
var octetsTables = tq.Sum(r => Convert.ToInt64(r["Octets"]));
var lignesTables = tq.Sum(r => Convert.ToInt64(r["Ecrites"]));
LectureV2.ExigerLecture("SELECT SUM(a.total_pages)");
var tailleRigDev = (await Lire(chaineRigDev,
    "SELECT SUM(CONVERT(bigint, a.total_pages)) * 8192, (SELECT SUM(p2.rows) FROM sys.partitions p2 JOIN sys.tables t2 ON t2.object_id = p2.object_id " +
    "WHERE p2.index_id IN (0, 1) AND t2.is_ms_shipped = 0), (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0) " +
    "FROM sys.allocation_units a JOIN sys.partitions p ON p.partition_id = a.container_id JOIN sys.tables t ON t.object_id = p.object_id WHERE t.is_ms_shipped = 0")).Rows[0];
var octetsRigDev = Convert.ToInt64(tailleRigDev[0]); var lignesRigDev = Convert.ToInt64(tailleRigDev[1]); var tablesRigDev = Convert.ToInt32(tailleRigDev[2]);
var fixe = dureeA - msTables;
var parOctets = fixe + (double)octetsRigDev * msTables / Math.Max(1, octetsTables);
var parLignes = fixe + (double)lignesRigDev * msTables / Math.Max(1, lignesTables);
var estimation = TimeSpan.FromMilliseconds(Math.Max(parOctets, parLignes));
var fenetre = TimeSpan.FromHours(10);   // 20 h -> 6 h, proposee par le dossier d'exploitation, a valider
Exiger(dureeA > 0 && msTables > 0 && msTables <= dureeA && tq.All(r => Convert.ToInt64(r["DureeMs"]) >= 0) && estimation < fenetre,
    $"REC-001 CA02 (mesure et extrapolation) : transfert mesure {dureeA / 1000.0:0.0} s pour {lignesTables} lignes telles quelles ({octetsTables / 1048576.0:0.0} Mo ecrits, " +
    $"{octetsTables / 1048576.0 / Math.Max(0.001, msTables / 1000.0):0.0} Mo/s, {lignesTables / Math.Max(0.001, msTables / 1000.0):0} lignes/s, controle de fin compris) ; " +
    $"extrapole a RIG_DEV ({octetsRigDev / 1073741824.0:0.0} Go, {lignesRigDev / 1e6:0.0} millions de lignes, {tablesRigDev} tables) : " +
    $"{TimeSpan.FromMilliseconds(parOctets):h\\:mm} au volume, {TimeSpan.FromMilliseconds(parLignes):h\\:mm} aux lignes, dans la fenetre de {fenetre.TotalHours:0} h (20 h - 6 h)");
Reste("REC-001 CA02 sur la copie anonymisee de la PLUS GROSSE des 40 bases, sur un serveur de la taille de la production : ni l'une ni l'autre ne sont sur ce poste " +
      "(les bases des greffes ne se lisent pas d'ici sans accord ; la copie anonymisee attend la nouvelle reception du lot 6) -> repetition REC-008 (lot 8)");

// Le rapport de fin de nuit (T04) et l'evenement 210.
var evenement210 = await Nombre(v3A.Connexion, $"SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = 210 AND Detail LIKE N'Transfert initial de la base V2 {v2.Nom} (transfert {idA})%'");
Exiger(texteA.Contains("table ") && tablesPrevues.All(t => Regex.IsMatch(texteA, $@"^\s+{t}\s+(telle quelle|colonne par colonne|reprise)\s+\d+", RegexOptions.Multiline)) && evenement210 == 1
       && transfertA["InstantaneLe"] is DateTime,
    $"REC-001 T04 : le rapport de fin de nuit imprime chaque table (lues, ecrites, rejetees, liens, duree), le journal garde l'instantane date " +
    $"({transfertA["InstantaneLe"]:yyyy-MM-dd HH:mm:ss}) et un seul evenement 210 « reprise de donnees » le dit");

// ====================================================================================================
// D. Regle « rejeter » sur DEMANDE, et une reprise deja faite
// ====================================================================================================
var v3D = await NouvelleV3("RecopieTransfertD");
await RepriseDroits.ReprendreAsync(v2.Connexion, v3D.Connexion);
var comptesAvantD = await Lire(v3D.Connexion, "SELECT COUNT(*), MAX(Id), CHECKSUM_AGG(CHECKSUM(*)) FROM soc.Compte");
var reglesD = reglesServeur.Regles.Select(r => r.Table == "DEMANDE" ? r with { SiLienCasse = SiLienCasse.Rejeter } : r).ToList();
var rapportD = await MoteurTransfert.TransfererAsync(new OptionsTransfert(v2.Connexion, v3D.Connexion) { Regles = reglesD, Reprises = reglesServeur.Reprises, TailleLot = 5000 });
var idD = rapportD.IdTransfert;
var rejetD = await Lire(v3D.Connexion, $"SELECT TableV2, IdV2, Motif, Regle FROM rec.Rejet WHERE IdTransfert = {idD}");
var demandeD = rapportD.Tables.Single(t => t.TableV2 == "DEMANDE");
var filleMarquee = await Nombre(v3D.Connexion, $"SELECT COUNT(*) FROM rec.LienCasse WHERE IdTransfert = {idD} AND TableV2 = 'DEMANDE' AND IdV2 = '{demandeFille}' " +
                                              $"AND Cle = '{cleMere}' AND Valeur = '{demandeCassee}' AND Cause = 'rejetee par le transfert'");
var rejetee = rejetD.Rows.Cast<DataRow>().SingleOrDefault();
Exiger(rapportD.Etat == "fini" && rejetee is not null && (string)rejetee[0] == "DEMANDE" && (string)rejetee[1] == demandeCassee.ToString()
       && ((string)rejetee[2]).Contains(cleAdresse) && ((string)rejetee[2]).Contains(AdresseAbsente.ToString()) && (string)rejetee[3] == "lien casse : rejeter"
       && await Nombre(v3D.Connexion, $"SELECT COUNT(*) FROM v2.DEMANDE WHERE DMND_ID_DMND = {demandeCassee}") == 0
       && demandeD.Lues == demandeD.Ecrites + demandeD.Rejetees && demandeD.Rejetees == 1 && filleMarquee == 1
       && (await Differences(v3D, ["DEMANDE"], new Dictionary<string, long> { ["DEMANDE"] = demandeCassee })).Count == 0,
    $"REC-001 CA04 et CA03 (regle « rejeter ») : la demande {demandeCassee} n'est pas ecrite, elle est dans rec.Rejet avec sa table, son identifiant et le motif " +
    $"« {rejetee?[2]} » ; DEMANDE : {demandeD.Lues} lues = {demandeD.Ecrites} ecrites + {demandeD.Rejetees} rejetee ; sa demande fille {demandeFille} est ecrite, " +
    $"le lien vers la mere rejetee marque ; aucune valeur inventee, pas d'arret");
var comptesApresD = await Lire(v3D.Connexion, "SELECT COUNT(*), MAX(Id), CHECKSUM_AGG(CHECKSUM(*)) FROM soc.Compte");
var evenementsD = await Lire(v3D.Connexion, $"SELECT Message FROM rec.TransfertEvenement WHERE IdTransfert = {idD} AND Genre = 'reprise'");
Exiger(evenementsD.Rows.Cast<DataRow>().Any(r => ((string)r[0]).StartsWith("Reprise « comptes et droits » : deja faite"))
       && string.Join("/", comptesAvantD.Rows[0].ItemArray) == string.Join("/", comptesApresD.Rows[0].ItemArray)
       && rapportD.Tables.Single(t => t.TableV2 == "UTILISATEUR").Ecrites == extrait.Lignes["UTILISATEUR"],
    $"REC-001 : une reprise deja faite dans la base V3 n'est pas rejouee (comptes et droits : {comptesApresD.Rows[0][0]} comptes inchanges), ses lignes sont seulement comptees et reliees");

// ====================================================================================================
// B. Des agents ecrivent dans la V2 pendant le transfert (CA05)
// ====================================================================================================
await Executer(v2.Connexion, $"ALTER DATABASE [{v2.Nom}] SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON)");
await Executer(v2.Connexion, "ALTER TABLE dbo.ARRIVEE ENABLE CHANGE_TRACKING; ALTER TABLE dbo.CLIENT ENABLE CHANGE_TRACKING;");
var v3B = await NouvelleV3("RecopieTransfertB");
var colonnesArrivee = (await Lire(v2.Connexion, "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ARRIVEE') AND is_identity = 0 ORDER BY column_id"))
    .Rows.Cast<DataRow>().Select(r => $"[{r[0]}]").ToList();
var modeleArrivee = await Nombre(v2.Connexion, "SELECT MIN(ARRV_ID_ARRV) FROM dbo.ARRIVEE");
var ecritures = new List<(int N, long Id, DateTime Debut, DateTime Fin, bool Bloquee)>();
var arret = new CancellationTokenSource();
var debutTransfert = DateTime.MaxValue; var instantanePris = DateTime.MaxValue;
var agents = Task.Run(async () =>
{
    await using var cn = new SqlConnection(v2.Connexion);
    await cn.OpenAsync();
    await using (var t = new SqlCommand("SET LOCK_TIMEOUT 2000;", cn)) await t.ExecuteNonQueryAsync();
    for (var n = 1; !arret.IsCancellationRequested; n++)
    {
        var debut = DateTime.Now;
        try
        {
            await using var cmd = new SqlCommand(
                $"INSERT INTO dbo.ARRIVEE ({string.Join(", ", colonnesArrivee)}) SELECT {string.Join(", ", colonnesArrivee)} FROM dbo.ARRIVEE WHERE ARRV_ID_ARRV = {modeleArrivee}; " +
                "DECLARE @id bigint = SCOPE_IDENTITY(); " +
                $"UPDATE dbo.ARRIVEE SET COLONNE_EN_PLUS_REC001 = 'ecrit {n}' WHERE ARRV_ID_ARRV = {clientTemoin}; SELECT @id;", cn);
            var id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            ecritures.Add((n, id, debut, DateTime.Now, false));
        }
        catch (SqlException e) when (e.Number == 1222) { ecritures.Add((n, 0, debut, DateTime.Now, true)); }
        await Task.Delay(15);
    }
});
while (ecritures.Count < 20) await Task.Delay(20);
var rapportB = await MoteurTransfert.TransfererAsync(new OptionsTransfert(v2.Connexion, v3B.Connexion)
{
    TailleLot = 5000,
    Suivre = e =>
    {
        if (e.Genre == "debut") debutTransfert = DateTime.Now;
        if (e.Genre == "instantane") instantanePris = DateTime.Now;
    },
});
await Task.Delay(300);
arret.Cancel();
await agents;
var faites = ecritures.Where(e => !e.Bloquee).ToList();
var pendant = faites.Where(e => e.Debut > instantanePris).ToList();
var avantInstantane = faites.Where(e => e.Fin < debutTransfert).ToList();
var idsV3 = (await Lire(v3B.Connexion, "SELECT ARRV_ID_ARRV FROM v2.ARRIVEE")).Rows.Cast<DataRow>().Select(r => Convert.ToInt64(r[0])).ToHashSet();
var valeurV3 = (string?)await Valeur(v3B.Connexion, $"SELECT COLONNE_EN_PLUS_REC001 FROM v2.ARRIVEE WHERE ARRV_ID_ARRV = {clientTemoin}");
var kV3 = valeurV3 is { } vv && vv.StartsWith("ecrit ") ? int.Parse(vv[6..]) : 0;
var plusLongue = faites.Count == 0 ? 0 : faites.Max(e => (e.Fin - e.Debut).TotalMilliseconds);
Exiger(rapportB.Etat == "fini" && ecritures.Count(e => e.Bloquee) == 0 && plusLongue < 2000 && pendant.Count > 0,
    $"REC-001 CA05 : {faites.Count} ecritures d'agents dans la V2 pendant le transfert ({pendant.Count} apres l'instantane), aucune bloquee " +
    $"(attente de verrou limitee a 2 s : {ecritures.Count(e => e.Bloquee)} refus), la plus longue {plusLongue:0} ms ; transfert {rapportB.Etat}");
Exiger(avantInstantane.All(e => idsV3.Contains(e.Id)) && pendant.All(e => !idsV3.Contains(e.Id)) && avantInstantane.Count > 0
       && kV3 >= avantInstantane.Max(e => e.N) && kV3 < pendant.Min(e => e.N),
    $"REC-001 CA05 : la V3 porte l'etat fige date du {rapportB.InstantaneLe:HH:mm:ss.fff} — les {avantInstantane.Count} arrivees ecrites avant y sont, " +
    $"aucune des {pendant.Count} ecrites apres ; l'arrivee temoin modifiee y a la valeur « {valeurV3} » de ce moment, la V2 a continue jusqu'a « ecrit {faites.Max(e => e.N)} »");
// La ligne temoin est une arrivee depuis le lot 10 (CLIENT a sa table rec.Client) : elle est dans la V3, modifiee apres, et
// comptee a part ; les autres changements d'ARRIVEE sont exactement les arrivees que le transfert n'a pas prises.
var aRejouer = (await Lire(v2.Connexion, $"SELECT ARRV_ID_ARRV FROM CHANGETABLE(CHANGES dbo.ARRIVEE, {rapportB.VersionSuivi ?? 0}) c WHERE c.ARRV_ID_ARRV <> {clientTemoin}"))
    .Rows.Cast<DataRow>().Select(r => Convert.ToInt64(r[0])).ToHashSet();
var manquantes = faites.Select(e => e.Id).Where(id => !idsV3.Contains(id)).ToHashSet();
var clientARejouer = await Nombre(v2.Connexion, $"SELECT COUNT(*) FROM CHANGETABLE(CHANGES dbo.ARRIVEE, {rapportB.VersionSuivi ?? 0}) c WHERE c.ARRV_ID_ARRV = {clientTemoin}");
Exiger(rapportB.VersionSuivi is not null && aRejouer.SetEquals(manquantes) && clientARejouer == 1,
    $"REC-001 CA05 : le journal garde la version du suivi des changements lue dans l'instantane ({rapportB.VersionSuivi}) ; les changements de la V2 depuis cette version " +
    $"sont exactement ce que le transfert n'a pas pris ({aRejouer.Count} arrivees ecrites, l'arrivee temoin modifiee) : la recopie continue (REC-003) les rejouera de la");
Reste("REC-001 CA05 « la recopie continue rejoue ce qui a ete ecrit apres » : le lecteur de changements est REC-003 (lot 9) ; ce banc prouve qu'il a son point de depart exact");

// ====================================================================================================
// E. Un ecart pendant l'ecriture : arret propre
// ====================================================================================================
var v3E = await NouvelleV3("RecopieTransfertE");
// Une conversion qui CHANGE les valeurs sans erreur : une date a la milliseconde devient une date a la minute. Sur ARRIVEE,
// copiee telle quelle (DOSSIER_RCS, la table des premiers essais, a sa table V3 depuis le lot 10).
// fix-ok: le 1er scenario (varchar -> nvarchar) gardait chaque valeur a l'identique — BINARY_CHECKSUM('ABC123') vaut 72745491
// en varchar comme en nvarchar (mesure sur le SQL Server du poste) — il n'y avait donc pas d'ecart de valeurs a voir.
string? colonneConvertie = null;
foreach (DataRow r in (await Lire(v2.Connexion, "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ARRIVEE') AND TYPE_NAME(system_type_id) = 'datetime' " +
                                                "AND is_nullable = 1 ORDER BY column_id")).Rows)
{
    var c = (string)r[0];
    if (await Nombre(v2.Connexion, $"SELECT CASE WHEN MIN([{c}]) >= '19000101' AND MAX([{c}]) < '20790601' AND COUNT(CASE WHEN DATEPART(second, [{c}]) <> 0 " +
                                    $"OR DATEPART(millisecond, [{c}]) <> 0 THEN 1 END) > 0 THEN 1 ELSE 0 END FROM dbo.ARRIVEE") == 1) { colonneConvertie = c; break; }
}
Exiger(colonneConvertie is not null, $"banc : une colonne date de ARRIVEE a des secondes ({colonneConvertie})");
await Executer(v3E.Connexion, $"""
    CREATE TRIGGER rec001_conversion_silencieuse ON DATABASE AFTER CREATE_TABLE AS
    BEGIN
        DECLARE @e xml = EVENTDATA();
        IF @e.value('(/EVENT_INSTANCE/SchemaName)[1]', 'sysname') = N'v2' AND @e.value('(/EVENT_INSTANCE/ObjectName)[1]', 'sysname') = N'ARRIVEE'
            ALTER TABLE v2.ARRIVEE ALTER COLUMN [{colonneConvertie}] smalldatetime NULL;
    END
    """);
var rapportE = await MoteurTransfert.TransfererAsync(new OptionsTransfert(v2.Connexion, v3E.Connexion) { TailleLot = 5000 });
var ordreE = rapportE.Tables.Where(t => t.Regle is "telle quelle" or "colonne par colonne").Select(t => t.TableV2).ToList();
var tablesV3E = (await Lire(v3E.Connexion, "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('v2')")).Rows.Cast<DataRow>().Select(r => (string)r[0]).ToHashSet();
var transfertE = (await Lire(v3E.Connexion, $"SELECT Etat, Motif FROM rec.Transfert WHERE Id = {rapportE.IdTransfert}")).Rows[0];
var apresArrivee = ordreTables.Where(o => o.Value > ordreTables["ARRIVEE"]).Select(o => o.Key).ToList();
Exiger(rapportE.Etat == "arrete" && rapportE.CodeSortie == 1 && (string)transfertE[0] == "arrete" && ((string)transfertE[1]).Contains("ARRIVEE : les valeurs ecrites ne sont pas celles de la V2")
       && !tablesV3E.Contains("ARRIVEE") && ordreE.Where(tellesQuelles.Contains).All(tablesV3E.Contains) && ordreE.Count == ordreTables["ARRIVEE"] - 1
       && apresArrivee.All(t => !tablesV3E.Contains(t)) && await Nombre(v3E.Connexion, "SELECT COUNT(*) FROM num.Compteur") == 0
       && await Nombre(v3E.Connexion, "SELECT COUNT(*) FROM rec.Correspondance WHERE TableV2 = 'ARRIVEE'") == 0
       && await Nombre(v3E.Connexion, "SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = 210 AND Detail LIKE N'Transfert initial%'") == 0
       && await InstantanesRestes() == 0,
    $"REC-001 : un ecart arrete le transfert proprement — colonne {colonneConvertie} de ARRIVEE convertie en silence dans la V3 : l'empreinte de fin de table le voit, " +
    $"la table est annulee (ni table ni correspondance), les {ordreE.Count} tables d'avant restent avec leur rapport, les {apresArrivee.Count} d'apres et les compteurs ne sont pas faits, " +
    $"journal « arrete » avec le motif (« {transfertE[1]} »), instantane supprime, code 1");

// ====================================================================================================
// C. Schema different (CA01, T03) : refus avant toute ecriture
// ====================================================================================================
async Task<string> Ecrit(BaseJetable v3) => string.Join(", ",
    $"{await Nombre(v3.Connexion, "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('v2')")} tables v2",
    $"{await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.Correspondance")} correspondances",
    $"{await Nombre(v3.Connexion, "SELECT COUNT(*) FROM soc.Compte")} comptes",
    $"{await Nombre(v3.Connexion, "SELECT COUNT(*) FROM soc.Reglage")} reglages",
    $"{await Nombre(v3.Connexion, "SELECT COUNT(*) FROM num.Compteur")} compteurs");
const string RienEcrit = "0 tables v2, 0 correspondances, 0 comptes, 0 reglages, 0 compteurs";
var v3C = await NouvelleV3("RecopieTransfertC");
await Executer(v2.Connexion, "CREATE TABLE dbo.TABLE_EN_PLUS_REC001 (Id int NOT NULL PRIMARY KEY, Texte varchar(20) NULL); INSERT INTO dbo.TABLE_EN_PLUS_REC001 VALUES (1, 'une ligne');");
var rapportC3 = await MoteurTransfert.TransfererAsync(new OptionsTransfert(v2.Connexion, v3C.Connexion));
var ecritC3 = await Ecrit(v3C);
await Executer(v2.Connexion, "DROP TABLE dbo.TABLE_EN_PLUS_REC001;");
Exiger(rapportC3.Etat == "refuse" && rapportC3.CodeSortie == 2 && rapportC3.Motif!.Contains("table sans regle : TABLE_EN_PLUS_REC001")
       && rapportC3.Schema.Contains("table en plus, sans regle : TABLE_EN_PLUS_REC001 (1 lignes)") && ecritC3 == RienEcrit && await InstantanesRestes() == 0,
    $"REC-001 T03 : une table lue sans regle (TABLE_EN_PLUS_REC001) est imprimee et le transfert refuse de demarrer, rien n'est ecrit ({ecritC3}) : « {rapportC3.Motif} »");

var clesVersInscription = await Lire(v2.Connexion, "SELECT OBJECT_NAME(parent_object_id), name FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID('dbo.INSCRIPTION') AND parent_object_id <> referenced_object_id");
foreach (DataRow r in clesVersInscription.Rows) await Executer(v2.Connexion, $"ALTER TABLE dbo.[{r[0]}] DROP CONSTRAINT [{r[1]}];");
await Executer(v2.Connexion, """
    DROP TABLE dbo.INSCRIPTION;
    ALTER TABLE dbo.UTILISATEUR ADD COLONNE_EN_PLUS_UTILISATEUR int NULL;
    DECLARE @defaut sysname = (SELECT d.name FROM sys.default_constraints d JOIN sys.columns c ON c.object_id = d.parent_object_id AND c.column_id = d.parent_column_id
                               WHERE d.parent_object_id = OBJECT_ID('dbo.ADRESSE_BASE') AND c.name = 'ADRBS_COMPLEMENT_NO');
    IF @defaut IS NOT NULL EXEC (N'ALTER TABLE dbo.ADRESSE_BASE DROP CONSTRAINT [' + @defaut + N']');
    ALTER TABLE dbo.ADRESSE_BASE DROP COLUMN ADRBS_COMPLEMENT_NO;
    """);
var sortieC2 = new StringWriter();
var codeC2 = await LigneDeCommande.ExecuterAsync(["transferer", "--v2", v2.Connexion, "--v3", v3C.Connexion], sortieC2);
var rapportC2 = (await Lire(v3C.Connexion, "SELECT TOP 1 Etat, Motif FROM rec.Transfert ORDER BY Id DESC")).Rows[0];
var ecritC2 = await Ecrit(v3C);
var texteC2 = sortieC2.ToString();
Exiger(codeC2 == 2 && (string)rapportC2[0] == "refuse" && ((string)rapportC2[1]).Contains("table attendue absente : INSCRIPTION") && ecritC2 == RienEcrit
       && texteC2.Contains("table attendue absente : INSCRIPTION") && texteC2.Contains("colonne en plus : UTILISATEUR.COLONNE_EN_PLUS_UTILISATEUR")
       && texteC2.Contains("colonne attendue absente : ADRESSE_BASE.ADRBS_COMPLEMENT_NO") && texteC2.Contains("table facultative absente") == false
       && await InstantanesRestes() == 0,
    $"REC-001 CA01 : une base au schema different est lue dans son catalogue ; l'outil imprime la table attendue absente (INSCRIPTION), la colonne en plus " +
    $"(UTILISATEUR) et la colonne absente (ADRESSE_BASE), puis s'arrete AVANT toute ecriture (code 2 ; {ecritC2})");

// L'outil refuse un serveur qui n'est pas ce poste, sans s'y connecter ; le meme sur ce poste est accepte (transfert A).
var sortieGarde = new StringWriter();
var chronoGarde = Stopwatch.StartNew();
var codeGarde = await LigneDeCommande.ExecuterAsync(["transferer", "--v2", chaineRigDev, "--v3", v3C.Connexion], sortieGarde);
Exiger(codeGarde == 3 && sortieGarde.ToString().Contains("n'est pas le serveur de ce poste") && chronoGarde.ElapsedMilliseconds < 2000 && codeA == 0,
    $"REC-001 : sans --exploitation, l'outil refuse une base qui n'est pas sur ce poste (RIG_DEV : code {codeGarde}, aucune connexion) ; sur ce poste, il transfere (A : code {codeA})");
}
finally
{
    foreach (var b in v3s) await b.DisposeAsync();
}

Console.WriteLine(echecs.Count == 0 ? "RecopieTransfertCheck : VERT" : $"RecopieTransfertCheck : ROUGE ({echecs.Count} echec(s))");
return echecs.Count == 0 ? 0 : 1;
