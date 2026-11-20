using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Base;
using RigV3.Contrats.Numerotation;
using RigV3.Serveur.Numerotation.Compteur;
using RigV3.Serveur.Numerotation.Series;

// Banc des SERIES DE NUMEROS REPRISES DE LA V2 (lot 6 du plan de reconstruction, 2026-11-21).
// Stories NUM-005 (reprendre chaque compteur a sa valeur du jour du passage) et NUM-009 a NUM-021 (les
// series du greffe) : un controle par critere, qui cite son id.
//
// L'oracle est LA V2 ELLE-MEME :
//  1. RIG_DEV (SQL-DEV\DEV) est lu en LECTURE SEULE (SELECT, rien d'autre : voir LireRigDev) ;
//  2. ses tables de numerotation sont recopiees dans une base jetable locale « V2 », avec le texte de
//     GENERER_NUMERO tel qu'il est deploye dans RIG_DEV (OBJECT_DEFINITION) ;
//  3. num.Reprendre recopie cette base V2 dans une base jetable V3 (toutes les migrations du depot) ;
//  4. pour chacun des 142 types, GENERER_NUMERO de la copie V2 dit ce que la V2 aurait donne, et
//     num.Attribuer de la V3, demande par la V3 puis par la V2, doit donner la meme chose, au meme format.
//
// Il lui faut le SQL Server de developpement (docker compose up -d) et l'acces en lecture a RIG_DEV
// (compte Windows ; autre serveur : variable RIGV3_RIG_DEV). Doc : docs/reconstruction/numerotation-series.md.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var chaineRigDev = Environment.GetEnvironmentVariable("RIGV3_RIG_DEV") is { Length: > 0 } c0 ? c0 :
    @"Server=SQL-DEV\DEV;Database=RIG_DEV;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;ApplicationIntent=ReadOnly;Connect Timeout=20";

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
// Ce qu'aucun banc de ce poste ne peut faire : dit, pas compte vert (docs/reconstruction/numerotation-series.md).
void Reste(string quoi) => Console.WriteLine("RESTE : " + quoi);
void Info(string quoi) => Console.WriteLine("        " + quoi);
var releve = new StringBuilder();   // le releve serie par serie, ecrit a cote du banc

// --- RIG_DEV : lecture seule -----------------------------------------------------------------------
// Garde-fou : une seule instruction, qui commence par SELECT, sans rien qui ecrive.
var ecriture = new Regex(@"\b(INSERT|UPDATE|DELETE|MERGE|EXEC|EXECUTE|DROP|ALTER|CREATE|TRUNCATE|GRANT|DENY|INTO)\b", RegexOptions.IgnoreCase);
void Garde(string sql)
{
    if (!Regex.IsMatch(sql, @"^\s*SELECT\b", RegexOptions.IgnoreCase) || sql.Contains(';') || ecriture.IsMatch(sql))
        throw new InvalidOperationException("Lecture de RIG_DEV refusee (SELECT seul) : " + sql);
}
async Task<SqlConnection> OuvrirRigDev()
{
    var cn = new SqlConnection(chaineRigDev);
    await cn.OpenAsync();
    await using var iso = new SqlCommand("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED", cn);
    await iso.ExecuteNonQueryAsync();
    return cn;
}
async Task<DataTable> LireRigDev(string sql)
{
    Garde(sql);
    await using var cn = await OuvrirRigDev();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
    var t = new DataTable();
    await using var l = await cmd.ExecuteReaderAsync();
    t.Load(l);
    return t;
}
async Task<object?> ScalaireRigDev(string sql)
{
    var t = await LireRigDev(sql);
    return t.Rows.Count == 0 || t.Rows[0][0] is DBNull ? null : t.Rows[0][0];
}

// --- Bases locales ----------------------------------------------------------------------------------
async Task Executer(string chaine, string sql, params (string, object?)[] p)
{
    await using var cn = new SqlConnection(chaine);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
    foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
    await cmd.ExecuteNonQueryAsync();
}
async Task<object?> Scalaire(string chaine, string sql, params (string, object?)[] p)
{
    await using var cn = new SqlConnection(chaine);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
    foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
    var r = await cmd.ExecuteScalarAsync();
    return r is DBNull ? null : r;
}
async Task<int> Entier(string chaine, string sql, params (string, object?)[] p)
    => Convert.ToInt32(await Scalaire(chaine, sql, p) ?? 0);
async Task<DataTable> Lire(string chaine, string sql, params (string, object?)[] p)
{
    await using var cn = new SqlConnection(chaine);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
    foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
    var t = new DataTable();
    await using var l = await cmd.ExecuteReaderAsync();
    t.Load(l);
    return t;
}

// ====================================================================================================
// 0. RIG_DEV joignable
// ====================================================================================================
string serveurV2;
try
{
    serveurV2 = (string)(await ScalaireRigDev("SELECT @@SERVERNAME + ' / ' + DB_NAME()"))!;
}
catch (Exception e)
{
    Exiger(false, $"RIG_DEV lisible ({new SqlConnectionStringBuilder(chaineRigDev).DataSource}) : {e.Message} -- " +
                  "il faut l'acces en lecture a SQL-DEV\\DEV (ou RIGV3_RIG_DEV)");
    return 1;
}
Exiger(true, $"RIG_DEV lu en lecture seule : {serveurV2}");

await using var v2 = await BaseJetable.CreerAsync("NumerotationSeriesV2");
await using var v3 = await BaseJetable.CreerAsync("NumerotationSeriesCheck");
var migrations = await Migrateur.AppliquerAsync(v3.Connexion, Path.Combine(racine, "Base", "migrations"));
var ordreMigrations = migrations.Jouees.ToList();
Exiger(ordreMigrations.Contains("NUM-005_reprise") && ordreMigrations.IndexOf("NUM-005_reprise") > ordreMigrations.IndexOf("NUM-001_compteurs"),
    $"migration NUM-005_reprise jouee apres NUM-001_compteurs, avec les autres du depot ({string.Join(", ", migrations.Jouees)})");

// ====================================================================================================
// 1. La copie V2 : les tables de RIG_DEV (definition lue dans RIG_DEV) et le vrai GENERER_NUMERO
// ====================================================================================================
async Task<string> Ddl(string table, string[]? colonnes)
{
    var t = await LireRigDev(
        "SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE, COLUMN_DEFAULT, " +
        $"COLUMNPROPERTY(OBJECT_ID('dbo.{table}'), COLUMN_NAME, 'IsIdentity') AS Ident " +
        $"FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = '{table}' ORDER BY ORDINAL_POSITION");
    var cols = new List<string>();
    foreach (DataRow r in t.Rows)
    {
        var nom = (string)r[0];
        if (colonnes is not null && !colonnes.Contains(nom, StringComparer.OrdinalIgnoreCase)) continue;
        var type = (string)r[1];
        var sqlType = type switch
        {
            "varchar" or "nvarchar" or "char" or "nchar" or "varbinary" or "binary" =>
                $"{type}({(Convert.ToInt32(r[2]) < 0 ? "max" : Convert.ToInt32(r[2]).ToString())})",
            "decimal" or "numeric" => $"{type}({r[3]},{r[4]})",
            _ => type,
        };
        var ident = colonnes is null && r[7] is int i && i == 1 ? " IDENTITY(1,1)" : "";
        var nul = colonnes is not null || (string)r[5] == "YES" ? " NULL" : " NOT NULL";
        var def = colonnes is null && r[6] is string d ? $" DEFAULT {d}" : "";
        cols.Add($"[{nom}] {sqlType}{ident}{nul}{def}");
    }
    if (cols.Count == 0) throw new InvalidOperationException($"RIG_DEV : table dbo.{table} sans colonne lisible.");
    return $"CREATE TABLE dbo.[{table}] ({string.Join(", ", cols)});";
}
async Task<int> Copier(string table, string[]? colonnes = null, string? filtre = null)
{
    await Executer(v2.Connexion, await Ddl(table, colonnes));
    var sql = $"SELECT {(colonnes is null ? "*" : string.Join(", ", colonnes))} FROM dbo.{table} WITH (NOLOCK)" +
              (filtre is null ? "" : " WHERE " + filtre);
    Garde(sql);
    await using var src = await OuvrirRigDev();
    await using var cmd = new SqlCommand(sql, src) { CommandTimeout = 600 };
    await using var lecteur = await cmd.ExecuteReaderAsync();
    using var bulk = new SqlBulkCopy(v2.Connexion, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock)
    { DestinationTableName = $"dbo.[{table}]", BulkCopyTimeout = 600, BatchSize = 50_000 };
    for (var i = 0; i < lecteur.FieldCount; i++) bulk.ColumnMappings.Add(lecteur.GetName(i), lecteur.GetName(i));
    await bulk.WriteToServerAsync(lecteur);
    return await Entier(v2.Connexion, $"SELECT COUNT(*) FROM dbo.[{table}]");
}

var chrono = Stopwatch.StartNew();
var lignesV2 = await Copier("NUMERO_METIER");
await Executer(v2.Connexion, "CREATE INDEX IX_NM ON dbo.NUMERO_METIER (NMMTR_TYPE_METIER, NMMTR_ANNEE, NMMTR_QUANTIEME);");
var sauvegarde = await Copier("NUMERO_METIER_SAVE_CDU");
var manquantsV2 = await Copier("NUMERO_CHRONO_MANQUANT");
var codesV2 = await Copier("GREFFE_NUMERO_LIAISON");
var mentionsV2 = await Copier("MENTION_OBSERVATION", ["MNTOB_ID_DSRCS", "MNTOB_NUM_ORDRE"]);
var etabV2 = await Copier("ETABLISSEMENT", ["ETBLS_ID_DSRCS", "ETBLS_NUM_ORDRE", "ETBLS_SUPPRIME", "ETBLS_TYPE_ADRESSE"]);
var bdfV2 = await Copier("EDI_BDF", ["EDIBDF_NUM_GESTION"], "EDIBDF_NUM_GESTION LIKE '[12][0-9][0-9][0-9]F%'");
var rolesV2 = await Copier("INSTANCE", ["INSTN_NUM_ROLE"], "INSTN_NUM_ROLE <> ''");
var gestionsV2 = await Copier("DOSSIER_RCS", ["DSSRC_NUM_GESTION"]);
// FACTURE a 1,7 million de lignes dans RIG_DEV : un extrait, les factures hors du motif « 99-999999 » et celles
// de l'annee ; la recherche du hors-format (num.ReleverHeritage) n'y fait pas de difference.
var facturesV2 = await Copier("FACTURE", ["FACTU_NUMERO_FACTURE"],
    $"FACTU_NUMERO_FACTURE NOT LIKE '[0-9][0-9]-%' OR FACTU_NUMERO_FACTURE LIKE '{DateTime.Today.Year % 100:00}-%'");
Info($"copie V2 : NUMERO_METIER {lignesV2} lignes, SAVE_CDU {sauvegarde}, manquants {manquantsV2}, codes de liaison {codesV2}, " +
     $"mentions {mentionsV2}, etablissements {etabV2}, avis BDF {bdfV2}, roles {rolesV2}, gestions {gestionsV2}, factures (extrait) {facturesV2} " +
     $"({chrono.Elapsed.TotalSeconds:0} s)");

var definitionV2 = (string?)await ScalaireRigDev("SELECT OBJECT_DEFINITION(OBJECT_ID('dbo.GENERER_NUMERO'))") ?? "";
await using (var cn = new SqlConnection(v2.Connexion))
{
    await cn.OpenAsync();
    // Le texte de la V2 ecrit ses chaines entre guillemets ("9999", "") : QUOTED_IDENTIFIER OFF, comme a sa creation.
    await using (var q = new SqlCommand("SET QUOTED_IDENTIFIER OFF", cn)) await q.ExecuteNonQueryAsync();
    await using (var p = new SqlCommand(definitionV2, cn)) await p.ExecuteNonQueryAsync();
}
var sourceV2 = Path.Combine(@"D:\RigApplication\Source\BaseDeDonnées\RigDatabase\dbo\Stored Procedures", "GENERER_NUMERO.sql");
string Normaliser(string s) => string.Join("\n", s.Replace("\r", "").TrimStart('\uFEFF').Split('\n').Select(l => l.TrimEnd())).Trim();
if (File.Exists(sourceV2))
    Exiger(Normaliser(definitionV2) == Normaliser(File.ReadAllText(sourceV2)),
        "le GENERER_NUMERO deploye dans RIG_DEV est celui de la source code_v2 (GENERER_NUMERO.sql), au caractere pres");
else
    Reste($"comparer le GENERER_NUMERO de RIG_DEV a sa source : {sourceV2} absent de ce poste");
var empreinteV2 = await Scalaire(v2.Connexion, "SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM dbo.NUMERO_METIER");

// Les lignes de la V2, pour calculer en C# ce que la reprise doit faire (un second calcul, independant du SQL).
var lignes = new List<LigneV2>();
foreach (DataRow r in (await Lire(v2.Connexion,
    "SELECT NMMTR_ID_NMMTR, NMMTR_TYPE_METIER, NMMTR_STRUCTURE_NUMERO, NMMTR_ANNEE, NMMTR_QUANTIEME, NMMTR_CHRONO, NMMTR_CHRONO_DEBUT, " +
    "NMMTR_GESTION_NUM_ERR, NMMTR_ATTRIBUTION_ERRONEE, NMMTR_EXPLICATION, NMMTR_FILE_REDIRECTION, NMMTR_CODAGE_ANNEE, " +
    "NMMTR_LETTRE_CATEGORIE, NMMTR_SEPARATEUR, NMMTR_TAILLE_CHRONO, NMMTR_CODE_GREFFE FROM dbo.NUMERO_METIER")).Rows)
    lignes.Add(new LigneV2((int)r[0], (string)r[1], (string)r[2], (int)r[3], r[4] is DBNull ? null : (short)r[4], (int)r[5],
                           (int)r[6], (bool)r[7], (bool)r[8], r[9] as string, r[10] as string, (byte)r[11], r[12] as string,
                           r[13] as string, (short)r[14], r[15] as string));
var compteursV2 = lignes.Where(l => !l.Erronee).GroupBy(l => l.Cle).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.Chrono).ThenByDescending(l => l.Id).ToList());
Info($"RIG_DEV : {lignes.Count} lignes, {lignes.Select(l => l.Type).Distinct().Count()} types, {lignes.Count(l => l.Erronee)} lignes « attribution erronee » " +
     "(carnet, lu le 2026-11-20 : 26 553 lignes, 142 types, 1 365)");

// NUM-005 CA03, second calcul : les numeros que deux lignes ARRD d'un meme jour ont donnes tous les deux (de 1 au
// plus petit chrono), cherches dans ARRIVEE de RIG_DEV. La copie V2 recoit les lignes ARRIVEE de ces numeros, et
// seulement elles (ARRIVEE est grande) : num.ReleverHeritage ne cherche que ces numeros-la.
var attendusArrd = new List<string>();
foreach (var kv in compteursV2.Where(d => d.Key.Type == "ARRD" && d.Value.Count > 1))
{
    var k = kv.Value[0];
    var haut = kv.Value.Min(l => l.Chrono);
    var numeros = Enumerable.Range(k.Debut, Math.Max(0, haut - k.Debut + 1)).Select(c => Composer(k, kv.Key.Annee, kv.Key.Jour, c)).ToList();
    if (numeros.Count == 0) continue;
    var liste = string.Join(",", numeros.Select(n => $"'{n}'"));
    foreach (DataRow r in (await LireRigDev($"SELECT ARRV_NUM_ARRIVEE FROM dbo.ARRIVEE WITH (NOLOCK) WHERE ARRV_NUM_ARRIVEE IN ({liste}) GROUP BY ARRV_NUM_ARRIVEE HAVING COUNT(*) > 1")).Rows)
        attendusArrd.Add((string)r[0]);
}
if (attendusArrd.Count > 0)
    await Copier("ARRIVEE", ["ARRV_NUM_ARRIVEE"], $"ARRV_NUM_ARRIVEE IN ({string.Join(",", attendusArrd.Select(n => $"'{n}'"))})");

// ====================================================================================================
// 2. NUM-005 : la reprise
// ====================================================================================================
async Task<SqlConnection> OuvrirV3()
{
    var cn = new SqlConnection(v3.Connexion);
    await cn.OpenAsync();
    return cn;
}

// CA02, contre-epreuve : un ecart provoque (un declencheur du banc fausse un chrono pendant la copie) annule tout.
await Executer(v3.Connexion,
    "CREATE TRIGGER num.TR_Banc_Fausser ON num.Compteur AFTER INSERT AS " +
    "UPDATE num.Compteur SET Chrono = Chrono + 1 WHERE Id = (SELECT MIN(Id) FROM inserted WHERE Chrono IS NOT NULL);");
RapportReprise fausse;
await using (var cn = await OuvrirV3())
    fausse = await RepriseNumerotation.ReprendreAsync(cn, null, v2.Nom);
await Executer(v3.Connexion, "DROP TRIGGER num.TR_Banc_Fausser;");
Exiger(fausse.Code == -1 && fausse.Ecarts.Count >= 1 && fausse.Ecarts.All(e => e.Quoi == "compteur")
       && await Entier(v3.Connexion, "SELECT COUNT(*) FROM num.Compteur") == 0
       && await Entier(v3.Connexion, "SELECT COUNT(*) FROM num.NumeroRendu") == 0
       && Equals(await Scalaire(v2.Connexion, "SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM dbo.NUMERO_METIER"), empreinteV2),
    $"NUM-005 CA02 : un chrono fausse pendant la copie -> code {fausse.Code}, {fausse.Ecarts.Count} ecart(s) " +
    $"({string.Join(" ; ", fausse.Ecarts.Take(2).Select(e => $"{e.Type} {e.Annee} jour {e.Jour} V2 {e.V2} / V3 {e.V3}"))}), " +
    "transaction annulee : aucun compteur ni numero rendu dans la V3, table V2 intacte");

// CA01 : la reprise dans la transaction du passage ; pendant ce temps, la V2 ne peut donner aucun numero.
RapportReprise rapport;
int idReprise;
var bloqueV2 = "";
var dureeAppel = Stopwatch.StartNew();
await using (var cn = await OuvrirV3())
{
    await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
    rapport = await RepriseNumerotation.ReprendreAsync(cn, tx, v2.Nom);
    dureeAppel.Stop();
    // Une session V2, pendant que le passage tient la table : GENERER_NUMERO attend, puis renonce.
    await using (var cnV2 = new SqlConnection(v2.Connexion))
    {
        await cnV2.OpenAsync();
        await using (var lt = new SqlCommand("SET LOCK_TIMEOUT 2000", cnV2)) await lt.ExecuteNonQueryAsync();
        try
        {
            await using var g = new SqlCommand("dbo.GENERER_NUMERO", cnV2) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
            g.Parameters.Add("@TYPE_NUMERO_METIER", SqlDbType.VarChar, 5).Value = "RGB";
            g.Parameters.Add("@NUMERO_RETOURNE", SqlDbType.VarChar, 40).Direction = ParameterDirection.Output;
            await g.ExecuteNonQueryAsync();
            bloqueV2 = "non : la V2 a donne " + g.Parameters["@NUMERO_RETOURNE"].Value;
        }
        catch (SqlException e) when (e.Errors.Cast<SqlError>().Any(x => x.Number == 1222))
        {
            bloqueV2 = "oui (erreur 1222, delai de verrou depasse)";
        }
    }
    await tx.CommitAsync();
}
idReprise = rapport.IdReprise ?? 0;
Exiger(rapport.Code == 0 && bloqueV2.StartsWith("oui")
       && Equals(await Scalaire(v2.Connexion, "SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM dbo.NUMERO_METIER"), empreinteV2),
    $"NUM-005 CA01 : reprise dans la transaction du passage (code {rapport.Code}) ; GENERER_NUMERO de la V2 bloque pendant ce temps : " +
    $"{bloqueV2} ; table V2 inchangee");
Exiger(rapport.DureeMs is < 60_000,
    $"NUM-005 CA01 : blocage de la table V2 {rapport.DureeMs} ms (appel {dureeAppel.ElapsedMilliseconds} ms) pour {rapport.LignesV2} lignes : moins d'une minute");
Exiger(rapport.Code == 0 && rapport.Ecarts.Count == 0 && rapport.LignesV2 == lignesV2,
    $"NUM-005 CA02 : rapprochement a zero ecart sur toutes les lignes de RIG_DEV ({rapport.LignesV2}) -- {rapport.Message}");
var compteursApresReprise = await Entier(v3.Connexion, "SELECT COUNT(*) FROM num.Compteur");
var signalesHeritage = 0;
await using (var cn = await OuvrirV3())
    signalesHeritage = await RepriseNumerotation.ReleverHeritageAsync(cn, null, v2.Nom, idReprise);
Info($"num.ReleverHeritage : {signalesHeritage} signalement(s) au referent");

// CA02 detaille : pour chaque (type, annee, jour) de la V2, le compteur V3 a le plus grand chrono et le format de sa ligne.
var cv3 = new Dictionary<(string, int, int), DataRow>();
foreach (DataRow r in (await Lire(v3.Connexion,
    "SELECT Type, Annee, Jour, ISNULL(Chrono, 0), Structure, CodageAnnee, ISNULL(Lettre, ''), ISNULL(Separateur, ''), TailleChrono, " +
    "ISNULL(CodeGreffe, ''), Debut, GestionRendus, ISNULL(Redirection, '') FROM num.Compteur")).Rows)
    cv3[((string)r[0], (short)r[1], (short)r[2])] = r;
var differents = compteursV2.Where(kv =>
{
    var k = kv.Value[0];
    return !cv3.TryGetValue(kv.Key, out var r) || (int)r[3] != k.Chrono || (string)r[4] != k.Structure || (byte)r[5] != k.Codage
           || (string)r[6] != (k.Lettre ?? "") || (string)r[7] != (k.Separateur ?? "") || (short)r[8] != k.Taille
           || (string)r[9] != (k.CodeGreffe ?? "") || (int)r[10] != k.Debut || (bool)r[11] != k.Gestion
           || (string)r[12] != (k.Redirection ?? "");
}).Select(kv => kv.Key).ToList();
Exiger(differents.Count == 0 && compteursApresReprise == compteursV2.Count + 2,
    $"NUM-005 CA02 : relu en C# depuis la copie V2, {compteursV2.Count} cles (type, annee, jour) -> {compteursApresReprise - 2} compteurs V3 au meme chrono " +
    $"(le plus grand), meme format, meme reglage ({differents.Count} different(s){(differents.Count > 0 ? " : " + string.Join(", ", differents.Take(5)) : "")}) ; " +
    "+ FACV3 et BDFNI ouverts par la V3");

// CA03 : compteurs en double.
var doubles = compteursV2.Where(kv => kv.Value.Count > 1).ToList();
var journalDoubles = await Entier(v3.Connexion, "SELECT COUNT(*) FROM num.Journal WHERE Evenement = 'reprise' AND Explication LIKE N'compteur en double%'");
var signalDoubles = await Entier(v3.Connexion, "SELECT COUNT(*) FROM num.Signalement WHERE Sujet = 'compteur en double'");
Exiger(doubles.Count > 0 && doubles.All(kv => cv3.TryGetValue(kv.Key, out var r) && (int)r[3] == kv.Value.Max(l => l.Chrono))
       && journalDoubles == doubles.Sum(kv => kv.Value.Count - 1) && signalDoubles == journalDoubles,
    $"NUM-005 CA03 : {doubles.Count} cles en double dans la V2 ({string.Join(", ", doubles.Select(kv => $"{kv.Key.Item1} {kv.Key.Item2} jour {kv.Key.Item3} : " + string.Join("/", kv.Value.Select(l => l.Chrono))))}) " +
    $"-> le plus grand chrono garde, {journalDoubles} ligne(s) au journal et au referent");
var portesDeuxFois = await Lire(v3.Connexion, "SELECT Serie, Annee, Jour, Valeur, Detail FROM num.Signalement WHERE Sujet = 'numero porte deux fois' ORDER BY Valeur");
var lusDeuxFois = portesDeuxFois.Rows.Cast<DataRow>().Select(r => (string)r[3]).ToList();
Exiger(attendusArrd.Count > 0 && lusDeuxFois.SequenceEqual(attendusArrd.OrderBy(x => x, StringComparer.Ordinal)),
    $"NUM-005 CA03 : numeros de ces jours portes deux fois dans ARRIVEE de RIG_DEV ({string.Join(", ", attendusArrd)}) listes au referent : " +
    $"{string.Join(", ", lusDeuxFois)}");

// CA04 : annees futures copiees telles quelles et listees.
var futures = lignes.Where(l => l.Annee > DateTime.Today.Year + 1).ToList();
var futursCopies = futures.Where(l => !l.Erronee).All(l => cv3.TryGetValue(l.Cle, out var r) && (int)r[3] == compteursV2[l.Cle].Max(x => x.Chrono));
Exiger(futures.Count > 0 && futursCopies
       && await Entier(v3.Connexion, "SELECT COUNT(*) FROM num.Signalement WHERE Sujet = 'annee future'") == futures.Count,
    $"NUM-005 CA04 : {futures.Count} lignes d'annees au-dela de {DateTime.Today.Year + 1} ({string.Join(", ", futures.Select(l => $"{l.Type} {l.Annee}{(l.Erronee ? " (rendue)" : "")}"))}) " +
    "copiees telles quelles, chrono compris, et listees au referent");

// CA05 : lignes « attribution erronee ».
var attendusRendus = new List<(string Serie, int Annee, int Jour, int Chrono, int IdV2, string Explication)>();
var illisibles = 0;
foreach (var g in lignes.Where(l => l.Erronee).GroupBy(l => (l.Cle, l.Chrono)))
{
    var e = g.OrderBy(l => l.Id).First();
    var q = e.Structure.Contains('Q');
    if (e.Chrono <= 0 || (q && (e.Quantieme is null || e.Quantieme < 1 || e.Quantieme > 366))) { illisibles += g.Count(); continue; }
    if (!compteursV2.TryGetValue(e.Cle, out var cs)) continue;
    var c = cs[0];
    if (c.Chrono == 0 || e.Chrono > c.Chrono || e.Chrono < c.Debut) continue;
    if (!c.Structure.Contains('Q') && !c.Gestion) continue;
    attendusRendus.Add((e.Type, e.Annee, e.Cle.Jour, e.Chrono, e.Id,
                        string.IsNullOrWhiteSpace(e.Explication) ? "sans explication (reprise de la V2)" : e.Explication.Trim(' ')));
}
var rendusV3 = (await Lire(v3.Connexion, "SELECT Serie, Annee, Jour, Chrono, IdV2, Explication FROM num.NumeroRendu")).Rows.Cast<DataRow>()
    .Select(r => ((string)r[0], (int)(short)r[1], (int)(short)r[2], (int)r[3], (int)r[4], (string)r[5])).ToHashSet();
var journalRendus = await Entier(v3.Connexion, "SELECT COUNT(*) FROM num.Journal WHERE Evenement = 'reprise' AND Explication LIKE N'rendu non redonnable%'");
var journalIllisibles = await Entier(v3.Connexion, "SELECT COUNT(*) FROM num.Journal WHERE Evenement = 'reprise' AND Explication LIKE N'rendu non redonnable%illisible%'");
var sansExplication = attendusRendus.Count(r => r.Explication == "sans explication (reprise de la V2)");
Exiger(rendusV3.SetEquals(attendusRendus) && journalRendus == lignes.Count(l => l.Erronee) - attendusRendus.Count && journalIllisibles == illisibles,
    $"NUM-005 CA05 : {lignes.Count(l => l.Erronee)} lignes rendues de la V2 -> {rendusV3.Count} numeros redonnables (dont {sansExplication} « sans explication (reprise de la V2) »), " +
    $"identiques au calcul C# ; {journalRendus} au journal seulement, dont {journalIllisibles} illisibles (chrono ou jour a 0 ; carnet : 178) et ceux au-dela du compteur");

// CA06 : la copie de sauvegarde.
string? refusArchive = null;
try { await Executer(v3.Connexion, "UPDATE num.Archive SET Chrono = Chrono + 1;"); }
catch (SqlException e) when (e.Number == 51002) { refusArchive = "refuse (51002)"; }
var archivees = await Entier(v3.Connexion, "SELECT COUNT(*) FROM num.Archive");
Exiger(archivees == sauvegarde && sauvegarde > 0 && refusArchive is not null && compteursApresReprise == compteursV2.Count + 2,
    $"NUM-005 CA06 : NUMERO_METIER_SAVE_CDU ({sauvegarde} lignes) dans num.Archive ({archivees}), modification {refusArchive ?? "ACCEPTEE"} ; " +
    "aucun compteur n'en vient");

// CA02 : une base V2 qui n'a que NUMERO_METIER (aucune table annexe : sauvegarde, manquants, codes de liaison,
// mentions, avis BDF) se reprend aussi, sans rien inventer.
await using (var v2min = await BaseJetable.CreerAsync("NumerotationSeriesV2min"))
await using (var v3min = await BaseJetable.CreerAsync("NumerotationSeriesMin"))
{
    await Migrateur.AppliquerAsync(v3min.Connexion, Path.Combine(racine, "Base", "migrations"));
    await Executer(v2min.Connexion, await Ddl("NUMERO_METIER", null));
    var colonnesNm = (string)(await Scalaire(v2.Connexion,
        "SELECT STRING_AGG(QUOTENAME(name), ',') WITHIN GROUP (ORDER BY column_id) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.NUMERO_METIER')"))!;
    await Executer(v2min.Connexion,
        $"SET IDENTITY_INSERT dbo.NUMERO_METIER ON; INSERT dbo.NUMERO_METIER ({colonnesNm}) SELECT {colonnesNm} FROM [{v2.Nom}].dbo.NUMERO_METIER " +
        "WHERE NMMTR_TYPE_METIER IN ('RGB', 'JPC', 'ARRD'); SET IDENTITY_INSERT dbo.NUMERO_METIER OFF;");
    RapportReprise minimal;
    await using (var cn = new SqlConnection(v3min.Connexion))
    {
        await cn.OpenAsync();
        minimal = await RepriseNumerotation.ReprendreAsync(cn, null, v2min.Nom);
    }
    var clesMin = compteursV2.Keys.Count(k => k.Type is "RGB" or "JPC" or "ARRD");
    Exiger(minimal.Code == 0 && minimal.Ecarts.Count == 0 && minimal.Compteurs == clesMin && minimal.Archivees == 0 && minimal.Manquants == 0
           && minimal.CodesLiaison == 0 && minimal.OrdresFiche == 0
           && await Entier(v3min.Connexion, "SELECT COUNT(*) FROM num.Compteur WHERE Type = 'BDFNI' AND Chrono IS NULL") == 1,
        $"NUM-005 CA02 : une base V2 sans tables annexes (RGB, JPC, ARRD seuls) se reprend a zero ecart (code {minimal.Code}) : {minimal.Compteurs} compteurs, " +
        "rien d'archive ni de manquant, BDFNI ouvert sans valeur (départ 1)");
}

// ====================================================================================================
// 3. NUM-009 a NUM-020 CA01 : chaque serie continue, la V3 puis la V2, au format de la V2
// ====================================================================================================
var familles = new (string Story, string Nom, string[] Types)[]
{
    ("NUM-009", "repertoire general, roles, repertoire des decisions", ["6", "JGAFF", "JGDJ", "JGJC", "JGOP", "JRCT", "JRDD", "JRDSJ", "JRIP", "JRJC", "JRMC", "JRMDE", "JROP", "JRORC", "JRPC", "JRPR", "JRREF"]),
    ("NUM-010", "depots au dossier des procedures collectives", ["JD13", "JD31", "JD40", "JD69", "JDCAS", "JDCCC", "JDCCO", "JDCIA", "JDCOL", "JDCS", "JDDCP", "JDDIL", "JDDIV", "JDEC", "JDETA", "JDETM", "JDEX", "JDEXT", "JDHUI", "JDIMP", "JDINV", "JDOCP", "JDOFF", "JDPLA", "JDPLI", "JDPVC", "JDPVR", "JDRAN", "JDRAP", "JDRCP", "JDRED", "JDREP", "JDRFM", "JDRIP", "JDSAI", "JDSAN"]),
    ("NUM-011", "procedures collectives, preventions, expertises, taxes", ["JMEXP", "JPC", "JPCEX", "JPRV", "OTIJ", "PREV", "TPG"]),
    ("NUM-012", "numero de gestion RCS", ["RGA", "RGAC", "RGB", "RGC", "RGD", "RGI", "RGNI"]),
    ("NUM-013", "registre chronologique", ["RC1", "RC5", "RC6", "RGCH"]),
    ("NUM-014", "depots des actes et des comptes", ["RA4", "RD1", "RD2", "RD3", "RD4", "RD5", "RD6", "RD7", "RDB", "RDC"]),
    ("NUM-015", "suretes et privileges", ["EAG", "EAN", "ECB", "ECN", "EDC", "EFA", "EFC", "EGD", "EGS", "EHF", "EHM", "EIN", "EIS", "EJF", "ELM", "EMO", "ENJ", "ENJSC", "ENP", "ENPS", "EPD", "EPE", "EPT", "EPX", "ERP", "ESC", "ESF", "ESM", "ESP", "ESS", "EVF", "EWA", "EWW"]),
    ("NUM-016", "arrivees et demandes", ["ARRC", "ARRD", "ARRHF", "ARRNQ", "ARRR", "DMD", "EAR", "HF", "RA1"]),
    ("NUM-017", "lettres recommandees", ["JLR", "JLRAR", "LRARI", "LRARN", "LRARR", "LRI", "LRN", "LRR"]),
    ("NUM-018", "BODACC et lots INPI", ["LTIA", "LTIC", "LTIF", "RAP", "RBOE"]),
    ("NUM-019", "autres series du greffe", ["IBREQ", "LIAI", "PARA", "RCJN", "SIREN"]),
    ("NUM-020", "factures de la V2", ["FACTU"]),
};
var typesCarnet = familles.SelectMany(f => f.Types).ToList();
var typesV2 = lignes.Select(l => l.Type).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();
Exiger(typesCarnet.Count == typesCarnet.Distinct().Count() && typesCarnet.OrderBy(t => t, StringComparer.Ordinal).SequenceEqual(typesV2),
    $"NUM-009 a NUM-020 : les {typesCarnet.Count} types des stories sont exactement les {typesV2.Count} types de RIG_DEV " +
    $"(absents du carnet : {string.Join(", ", typesV2.Except(typesCarnet).DefaultIfEmpty("aucun"))} ; absents de RIG_DEV : {string.Join(", ", typesCarnet.Except(typesV2).DefaultIfEmpty("aucun"))})");

// Le jour du serveur, une fois : la V2 et la V3 recoivent la meme annee et le meme quantieme (pas de course a minuit).
var dateServeur = await Lire(v3.Connexion, "SELECT YEAR(GETDATE()), DATEPART(dayofyear, GETDATE())");
var anneeDuJour = (int)dateServeur.Rows[0][0];
var jourDuJour = (int)dateServeur.Rows[0][1];

async Task<(int Code, string? Numero)> V2Generer(string type, int annee, int jour, string? codeGreffe)
{
    await using var cn = new SqlConnection(v2.Connexion);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand("dbo.GENERER_NUMERO", cn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
    cmd.Parameters.Add("@TYPE_NUMERO_METIER", SqlDbType.VarChar, 5).Value = type;
    var n = cmd.Parameters.Add("@NUMERO_RETOURNE", SqlDbType.VarChar, 40);
    n.Direction = ParameterDirection.Output;
    cmd.Parameters.Add("@ANNEE", SqlDbType.Int).Value = annee;          // la DLL de la V2 passe des entiers (Numerotation.cs:54-55)
    cmd.Parameters.Add("@QUANTIEME", SqlDbType.Int).Value = jour;
    if (codeGreffe is not null) cmd.Parameters.Add("@CODE_GREFFE", SqlDbType.VarChar, 5).Value = codeGreffe;
    var rc = cmd.Parameters.Add("@rc", SqlDbType.Int);
    rc.Direction = ParameterDirection.ReturnValue;
    await cmd.ExecuteNonQueryAsync();
    return ((int)rc.Value, n.Value as string);
}
async Task<NumeroAttribue> V3Attribuer(string type, int annee, int jour, string? codeGreffe, string logiciel)
{
    await using var cn = await OuvrirV3();
    await using var cmd = new SqlCommand("num.Attribuer", cn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
    cmd.Parameters.Add("@Type", SqlDbType.VarChar, 5).Value = type;
    cmd.Parameters.Add("@Annee", SqlDbType.SmallInt).Value = annee;
    cmd.Parameters.Add("@Jour", SqlDbType.SmallInt).Value = jour;
    cmd.Parameters.Add("@CodeGreffe", SqlDbType.VarChar, 5).Value = (object?)codeGreffe ?? DBNull.Value;
    cmd.Parameters.Add("@Logiciel", SqlDbType.Char, 2).Value = logiciel;
    cmd.Parameters.Add("@Compte", SqlDbType.NVarChar, 128).Value = logiciel == "V2" ? "agent.v2" : "agent.v3";
    cmd.Parameters.Add("@Poste", SqlDbType.NVarChar, 128).Value = "BANC";
    var numero = cmd.Parameters.Add("@Numero", SqlDbType.VarChar, 40);
    var serie = cmd.Parameters.Add("@Serie", SqlDbType.VarChar, 5);
    var ch = cmd.Parameters.Add("@Chrono", SqlDbType.Int);
    var re = cmd.Parameters.Add("@Reattribue", SqlDbType.Bit);
    var msg = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 400);
    foreach (var p in new[] { numero, serie, ch, re, msg }) p.Direction = ParameterDirection.Output;
    var rc = cmd.Parameters.Add("@rc", SqlDbType.Int);
    rc.Direction = ParameterDirection.ReturnValue;
    await cmd.ExecuteNonQueryAsync();
    return new NumeroAttribue((int)rc.Value, serie.Value as string, numero.Value as string, annee, jour, ch.Value as int?,
                              re.Value is true, msg.Value as string);
}

// Les parametres que la V2 passe vraiment : les lettres recommandees ont une file unique, annee 2000
// (Numerotation.cs:126-131) ; le numero de liaison recoit le code du greffe (PassageCfe.cs:161, code du compteur ici).
int AnneeAppel(string type) => type is "LRN" or "LRARN" ? NumerosSpeciaux.AnneeLettresRecommandees : anneeDuJour;
string? GreffeAppel(string type) => type == "LIAI" ? lignes.Where(l => l.Type == "LIAI" && !l.Erronee).OrderByDescending(l => l.Annee).First().CodeGreffe : null;

var resultats = new Dictionary<string, (NumeroAttribue M1, NumeroAttribue M2, List<string> V2)>();
var identiques = new HashSet<string>();   // les types dont la V3 a donne exactement ce que la V2 aurait donne
releve.AppendLine("| Story | Type | Format (structure, annee, lettre, separateur, chiffres) | Repris de RIG_DEV (annee, jour : chrono) | GENERER_NUMERO de la V2 aurait donne | La V3 donne (V3 puis V2) |");
releve.AppendLine("|---|---|---|---|---|---|");
foreach (var (story, nom, types) in familles)
{
    var ok = 0;
    foreach (var type in types)
    {
        var redirection = lignes.Where(l => l.Type == type && !string.IsNullOrWhiteSpace(l.Redirection)).Select(l => l.Redirection!.Trim()).FirstOrDefault();
        var serie = redirection ?? type;
        var annee = AnneeAppel(type);
        var reference = lignes.Where(l => l.Type == serie && !l.Erronee).OrderBy(l => l.Annee == annee ? 0 : 1).ThenByDescending(l => l.Annee).ThenByDescending(l => l.Quantieme).First();
        var parJour = reference.Structure.Contains('Q');
        var cle = (serie, annee, parJour ? jourDuJour : 0);
        var avant = compteursV2.TryGetValue(cle, out var cs) ? cs[0].Chrono : (int?)null;
        var v3Avant = cv3.TryGetValue(cle, out var r3) ? (int)r3[3] : (int?)null;

        // Les numeros rendus que la V2 redonnerait mais que la V3 refuse : jamais donnes (au-dela du compteur,
        // NUM-004 CA04) -- calcules en C# depuis les lignes de la V2.
        var refuses = lignes.Where(l => l.Erronee && l.Type == serie && l.Annee == annee && (!parJour || l.Quantieme == jourDuJour)
                                         && (avant is null or 0 || l.Chrono > avant || l.Chrono < cs![0].Debut))
                            .Select(l => Composer(reference, annee, parJour ? jourDuJour : 0, l.Chrono)).ToHashSet();
        var v2Donnes = new List<string>();
        for (var i = 0; i < 2 + refuses.Count; i++)
        {
            var (code, numero) = await V2Generer(type, annee, jourDuJour, GreffeAppel(type));
            v2Donnes.Add(code == 0 ? numero ?? "(null)" : $"code {code}");
        }
        var attendus = v2Donnes.Where(n => !refuses.Contains(n)).Take(2).ToList();
        var m1 = await V3Attribuer(type, annee, jourDuJour, GreffeAppel(type), "V3");
        var m2 = await V3Attribuer(type, annee, jourDuJour, GreffeAppel(type), "V2");
        resultats[type] = (m1, m2, v2Donnes);

        // La V2 redonne « 0 » sans fin quand sa valeur de depart est 0 (chrono 0 = rien donne) : ecart du lot 5,
        // la V3 donne 0 puis 1.
        var doublonV2 = attendus.Count == 2 && attendus[0] == attendus[1];
        var egal = m1.Code == 0 && m2.Code == 0 && m1.Numero == attendus[0]
                   && (doublonV2 ? m2.Chrono == m1.Chrono + 1 : m2.Numero == attendus[1])
                   && v3Avant == avant;
        var ecart = refuses.Count > 0 ? $" ; ecart voulu NUM-004 CA04 : la V2 aurait d'abord redonne {string.Join(", ", refuses)}, jamais donne"
                  : doublonV2 ? $" ; ecart voulu (lot 5) : la V2 redonne {attendus[0]} deux fois, la V3 donne {m2.Numero}" : "";
        if (egal) { ok++; identiques.Add(type); }
        var format = $"{reference.Structure}, {reference.Codage}, «{reference.Lettre}», «{reference.Separateur}», {reference.Taille}";
        var repris = avant is null
            ? $"{annee}{(parJour ? $" j{jourDuJour}" : "")} : pas de compteur ({(parJour ? "nouveau jour" : "nouvelle annee")}, depart {reference.Debut})"
            : $"{annee}{(parJour ? $" j{jourDuJour}" : "")} : {avant}";
        Exiger(egal, $"{story} CA01 {type}{(serie != type ? $" -> {serie}" : "")} [{format}] repris {repris} (V3 : {v3Avant?.ToString() ?? "aucun"}) ; " +
                     $"la V2 aurait donne {string.Join(" puis ", attendus)} ; la V3 donne {m1.Numero ?? $"code {m1.Code}"} (V3) puis {m2.Numero ?? $"code {m2.Code} {m2.Message}"} (V2){ecart}");
        releve.AppendLine($"| {story} | {type}{(serie != type ? $" → {serie}" : "")} | {format} | {repris} | {string.Join(", ", v2Donnes)} | {m1.Numero}, {m2.Numero}{(ecart.Length > 0 ? " (" + ecart.TrimStart(' ', ';') + ")" : "")} |");
    }
    Info($"{story} ({nom}) : {ok}/{types.Length} series continuent a l'identique");
}

// ====================================================================================================
// 4. Les criteres propres a chaque famille
// ====================================================================================================
async Task<DataTable> Formes(string a, string b) => await Lire(v3.Connexion,
    "SELECT SerieA, SerieB, Annee, Jour, NumerosCommuns FROM num.FormePartagee WHERE ((SerieA = @a AND SerieB = @b) OR (SerieA = @b AND SerieB = @a)) " +
    "AND Annee = @y AND NumerosCommuns > 0", ("@a", a), ("@b", b), ("@y", anneeDuJour));
async Task<bool> FormeCommune(string a, string b) => (await Formes(a, b)).Rows.Count > 0;

// NUM-009 CA02 : les trois repertoires des decisions, le meme jour, chacun son compteur.
var decisions = await Lire(v3.Connexion,
    "SELECT Serie, Numero, Chrono FROM num.Journal WHERE Serie IN ('JGDJ', 'JGJC', 'JGOP') AND Annee = @a AND Jour = @j " +
    "AND Evenement IN ('attribution', 'reattribution') ORDER BY Id", ("@a", anneeDuJour), ("@j", jourDuJour));
var parSerie = decisions.Rows.Cast<DataRow>().GroupBy(r => (string)r[0]).ToDictionary(g => g.Key, g => g.Select(r => (int)r[2]).ToList());
var uniques = decisions.Rows.Cast<DataRow>().Select(r => ((string)r[0], (string)r[1])).Distinct().Count() == decisions.Rows.Count;
Exiger(parSerie.Count == 3 && parSerie.Values.All(c => c.Count == 2 && c[1] == c[0] + 1) && uniques
       && await FormeCommune("JGDJ", "JGJC") && await FormeCommune("JGDJ", "JGOP") && await FormeCommune("JGJC", "JGOP"),
    $"NUM-009 CA02 : JGDJ, JGJC, JGOP le meme jour ({jourDuJour}) : chacun suit son compteur du jour " +
    $"({string.Join(" ; ", parSerie.Select(kv => kv.Key + " " + string.Join(",", kv.Value)))}), un numero = (serie, valeur), formes partagees listees (num.FormePartagee)");

// NUM-009 CA03 : les numeros de role en double, heritees, acceptes seuls.
var rolesDoubles = (await LireRigDev("SELECT INSTN_NUM_ROLE FROM dbo.INSTANCE WITH (NOLOCK) WHERE INSTN_NUM_ROLE <> '' GROUP BY INSTN_NUM_ROLE HAVING COUNT(*) > 1"))
    .Rows.Cast<DataRow>().Select(r => (string)r[0]).OrderBy(x => x, StringComparer.Ordinal).ToList();
var rolesHerites = (await Lire(v3.Connexion, "SELECT Valeur FROM num.ValeurHeritee WHERE Fiche = 'INSTANCE' AND Colonne = 'NUM_ROLE' ORDER BY Valeur"))
    .Rows.Cast<DataRow>().Select(r => (string)r[0]).ToList();
var jamaisRedonnes = true;
foreach (var role in rolesDoubles)
{
    // Le compteur qui l'a donne : le format d'un type de role qui le lit, a son annee.
    var lu = await Lire(v3.Connexion,
        "SELECT TOP (1) c.Type, c.Chrono, l.Chrono FROM num.Compteur c CROSS APPLY num.LireNumero(@n, c.Structure, c.CodageAnnee, c.Lettre, c.Separateur, " +
        "c.TailleChrono, DATALENGTH(ISNULL(c.CodeGreffe, ''))) l WHERE c.Type LIKE 'JR%' AND c.Jour = 0 AND RIGHT(CAST(c.Annee AS varchar(5)), LEN(l.CodeAnnee)) = l.CodeAnnee",
        ("@n", role));
    jamaisRedonnes &= lu.Rows.Count == 1 && (int)lu.Rows[0][1] >= (int)lu.Rows[0][2]
        && await Entier(v3.Connexion, "SELECT COUNT(*) FROM num.NumeroRendu WHERE Numero = @n", ("@n", role)) == 0;
}
var heriteOui = await Entier(v3.Connexion, "SELECT CAST(num.EstHeritee('INSTANCE', 'NUM_ROLE', @v) AS int)", ("@v", rolesDoubles.FirstOrDefault() ?? ""));
var heriteNon = await Entier(v3.Connexion, "SELECT CAST(num.EstHeritee('INSTANCE', 'NUM_ROLE', @v) AS int)", ("@v", resultats["JRIP"].M1.Numero));
Exiger(rolesDoubles.Count > 0 && rolesHerites.SequenceEqual(rolesDoubles) && jamaisRedonnes && heriteOui == 1 && heriteNon == 0,
    $"NUM-009 CA03 : roles portes par deux instances dans RIG_DEV ({string.Join(", ", rolesDoubles)}) acceptes comme doublons herites, et seulement eux " +
    $"(num.EstHeritee : {heriteOui} pour {rolesDoubles.FirstOrDefault()}, {heriteNon} pour {resultats["JRIP"].M1.Numero}) ; leur compteur est deja au-dela, jamais redonnes");
Exiger(identiques.Contains("JRIP") && resultats["JRIP"].M1.Serie == "JRIP" && resultats["JRIP"].M2.Serie == "JRIP",
    $"NUM-009 CA04 : une instance V3 puis une instance V2 le meme jour : {resultats["JRIP"].M1.Numero} puis {resultats["JRIP"].M2.Numero}, " +
    "les deux suivants du role JRIP, ceux que la V2 aurait donnes");

// NUM-010 CA02 : JDETM et JDSAN composent tous deux « SANC… ».
var sanc = await Formes("JDETM", "JDSAN");
Exiger(sanc.Rows.Count == 1 && resultats["JDETM"].M1.Numero!.StartsWith("SANC") && resultats["JDSAN"].M1.Numero!.StartsWith("SANC"),
    $"NUM-010 CA02 : JDETM ({resultats["JDETM"].M1.Numero}) et JDSAN ({resultats["JDSAN"].M1.Numero}) : num.FormePartagee signale " +
    $"{(sanc.Rows.Count == 1 ? sanc.Rows[0][4] : 0)} numero(s) {anneeDuJour} portes par les deux series ; la lettre de JDETM est a confirmer par le referent");

// NUM-011 CA02 : JPRV numerote les preventions ; PREV reste reprise ; RGCH a la forme de PREV.
var prevRepris = lignes.Where(l => l.Type == "PREV" && !l.Erronee).All(l => cv3.TryGetValue(l.Cle, out var r) && (int)r[3] == compteursV2[l.Cle][0].Chrono);
Exiger(prevRepris && resultats["JPRV"].M1.Code == 0 && await FormeCommune("PREV", "RGCH"),
    $"NUM-011 CA02 : une prevention prend JPRV ({resultats["JPRV"].M1.Numero}) ; PREV ({string.Join(", ", lignes.Where(l => l.Type == "PREV").Select(l => l.Annee).Distinct())}) " +
    "reprise telle quelle ; RGCH et PREV ecrivent la meme forme, signalee (usage de PREV a mesurer, Q11)");

// NUM-011 CA03 : une demande au Tresor annulee rend ses numeros (NUM-004).
var distributeur = new DistributeurNumeros();
ResultatRestitution rendTpg;
NumeroAttribue redonneTpg;
await using (var cn = await OuvrirV3())
{
    rendTpg = await distributeur.RestituerAsync(cn, null, new DemandeRestitution("TPG", resultats["TPG"].M2.Numero!, "Annulation de la demande au Tresor (fiche #1873)"), "agent.v3", "BANC");
    redonneTpg = await distributeur.AttribuerAsync(cn, null, new DemandeNumero("TPG"), "agent.v3", "BANC");
}
Exiger(rendTpg.Code == 0 && rendTpg.Issue == "rendu" && redonneTpg.Numero == resultats["TPG"].M2.Numero && redonneTpg.Reattribue,
    $"NUM-011 CA03 : demande au Tresor annulee : {resultats["TPG"].M2.Numero} rendu ({rendTpg.Issue}), redonne au suivant ({redonneTpg.Numero})");

// NUM-012 CA02 : numeros de gestion sans annee.
var apV2 = (await LireRigDev("SELECT DISTINCT DSSRC_NUM_GESTION FROM dbo.DOSSIER_RCS WITH (NOLOCK) WHERE DSSRC_NUM_GESTION <> ''")).Rows.Cast<DataRow>()
    .Select(r => (string)r[0]).Where(v => !Regex.IsMatch(v, "^[12][0-9]{3}")).OrderBy(x => x, StringComparer.Ordinal).ToList();
var apV3 = (await Lire(v3.Connexion, "SELECT Valeur FROM num.ValeurHeritee WHERE Fiche = 'DOSSIER_RCS' ORDER BY Valeur")).Rows.Cast<DataRow>().Select(r => (string)r[0]).ToList();
var formatsRg = "SELECT COUNT(*) FROM num.Compteur c CROSS APPLY num.LireNumero(@n, c.Structure, c.CodageAnnee, c.Lettre, c.Separateur, c.TailleChrono, " +
                "DATALENGTH(ISNULL(c.CodeGreffe, ''))) l WHERE c.Type LIKE 'RG%'";
var apLus = 0;
foreach (var ap in apV2) apLus += await Entier(v3.Connexion, formatsRg, ("@n", ap));
var rgbLu = await Entier(v3.Connexion, formatsRg, ("@n", resultats["RGB"].M1.Numero));
Exiger(apV2.Count > 0 && apV3.SequenceEqual(apV2) && apLus == 0 && rgbLu > 0
       && await Entier(v3.Connexion, "SELECT CAST(num.EstHeritee('DOSSIER_RCS', 'NUM_GESTION', @v) AS int)", ("@v", apV2[0])) == 1,
    $"NUM-012 CA02 : {apV2.Count} numeros de gestion sans annee dans RIG_DEV ({string.Join(", ", apV2)}) gardes comme valeurs heritees ; " +
    $"aucun format RG* ne les lit (le jumeau {resultats["RGB"].M1.Numero} se lit) : la V3 ne compose jamais ce format");
Exiger(await Entier(v3.Connexion, "SELECT COUNT(*) FROM num.Compteur WHERE TableCible LIKE '%DOSSIER_RCS%' OR ColonneCible LIKE '%RCS%' OR ColonneCible LIKE '%SIREN%'") == 0,
    "NUM-012 CA03 : aucun compteur repris ne vise le numero RCS du dossier (TableCible / ColonneCible) : il reste le SIREN, recopie tel quel");

// NUM-013 CA02, CA03 : numeros manquants du registre.
var manquants = await Lire(v3.Connexion, "SELECT Numero, Motif, Chrono FROM num.NumeroManquant WHERE Serie = 'RC1' ORDER BY Numero");
var manquantsRedonnables = await Entier(v3.Connexion,
    "SELECT COUNT(*) FROM num.NumeroRendu r JOIN num.Journal j ON j.Evenement = 'manquant' AND j.Serie = r.Serie AND j.Numero = r.Numero");
Exiger(manquants.Rows.Count == manquantsV2 && manquantsV2 > 0 && manquantsRedonnables == 0
       && manquants.Rows.Cast<DataRow>().All(r => (string)r[1] == "manquant constaté par la V2, motif inconnu" && r[2] is int),
    $"NUM-013 CA02 : les {manquantsV2} numeros manquants de la V2 ({string.Join(", ", manquants.Rows.Cast<DataRow>().Select(r => (string)r[0]))}) repris au journal " +
    "avec le motif « manquant constaté par la V2, motif inconnu », jamais redonnes");
int declare, declareRendu, jamaisDonne;
await using (var cn = await OuvrirV3())
{
    async Task<int> Declarer(string numero, string motif)
    {
        await using var cmd = new SqlCommand("num.DeclarerManquant", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Type", "RC1");
        cmd.Parameters.AddWithValue("@Numero", numero);
        cmd.Parameters.AddWithValue("@Motif", motif);
        var rc = cmd.Parameters.Add("@rc", SqlDbType.Int);
        rc.Direction = ParameterDirection.ReturnValue;
        await cmd.ExecuteNonQueryAsync();
        return (int)rc.Value;
    }
    declare = await Declarer(resultats["RC1"].M1.Numero!, "inscription abandonnee par le deposant");
    declareRendu = await Declarer(resultats["RC1"].M2.Numero!, "erreur de saisie");
    await distributeur.RestituerAsync(cn, null, new DemandeRestitution("RC1", resultats["RC1"].M2.Numero!, "erreur de saisie"), "agent.v3", "BANC");
    var auDela = resultats["RC1"].M2.Numero![..^6] + ((resultats["RC1"].M2.Chrono ?? 0) + 500).ToString("000000");
    jamaisDonne = await Declarer(auDela, "essai");
}
var listeRc1 = (await Lire(v3.Connexion, "SELECT Numero, Motif FROM num.NumeroManquant WHERE Serie = 'RC1'")).Rows.Cast<DataRow>()
    .ToDictionary(r => (string)r[0], r => (string)r[1]);
Exiger(declare == 0 && declareRendu == 0 && jamaisDonne == CodesNumerotation.JamaisDonne
       && listeRc1.TryGetValue(resultats["RC1"].M1.Numero!, out var motif1) && motif1 == "inscription abandonnee par le deposant"
       && !listeRc1.ContainsKey(resultats["RC1"].M2.Numero!),
    $"NUM-013 CA03 : {resultats["RC1"].M1.Numero} donne puis non utilise : liste des manquants avec son motif ; {resultats["RC1"].M2.Numero} rendu (NUM-004) : " +
    $"absent de la liste ; un numero jamais donne ne peut pas manquer (code {jamaisDonne})");
Exiger(await FormeCommune("RC1", "RC5") && await FormeCommune("RC1", "RC6") && await FormeCommune("RC5", "RC6")
       && resultats["RC1"].M1.Numero![..4] == resultats["RC5"].M1.Numero![..4],
    $"NUM-013 CA04 : RC1 ({resultats["RC1"].M1.Numero}), RC5 ({resultats["RC5"].M1.Numero}), RC6 ({resultats["RC6"].M1.Numero}) ecrivent « F » + annee + numero : " +
    "chacune son compteur, un numero = (serie, valeur), formes partagees signalees");

// NUM-014 CA02 : comptes annuels d'une ancienne affaire (V2) et d'une nouvelle (V3) le meme jour.
Exiger(identiques.Contains("RDC") && resultats["RDC"].M1.Serie == "RDC" && resultats["RDC"].M2.Serie == "RDC",
    $"NUM-014 CA02 : comptes deposes en V3 puis en V2 le meme jour : {resultats["RDC"].M1.Numero} puis {resultats["RDC"].M2.Numero}, " +
    "les deux suivants de la serie RDC, ceux que la V2 aurait donnes");

// NUM-015 CA02 : ECN et ENP composent annee + « E » + numero.
Exiger(await FormeCommune("ECN", "ENP") && resultats["ECN"].M1.Serie == "ECN" && resultats["ENP"].M1.Serie == "ENP",
    $"NUM-015 CA02 : ECN ({resultats["ECN"].M1.Numero}) et ENP ({resultats["ENP"].M1.Numero}) ecrivent annee + « E » + numero, chacun son compteur ; signale (num.FormePartagee), " +
    "le referent dit si la lettre partagee est voulue");

// NUM-016 CA02, CA03 : redirection ARRC -> ARRD ; series par jour.
Exiger(resultats["ARRC"].M1.Serie == "ARRD" && resultats["ARRC"].M1.Code == 0,
    $"NUM-016 CA02 : une arrivee de complement (ARRC) recoit un numero de la serie ARRD ({resultats["ARRC"].M1.Numero}), comme dans la V2");
if (jourDuJour < 365)
{
    var lendemain = jourDuJour + 1;
    var nouveaux = new List<NumeroAttribue>();
    foreach (var t in new[] { "ARRD", "DMD" })
    {
        nouveaux.Add(await V3Attribuer(t, anneeDuJour, lendemain, null, "V3"));
        nouveaux.Add(await V3Attribuer(t, anneeDuJour, lendemain, null, "V2"));
    }
    var lignesJour = await Entier(v3.Connexion, "SELECT COUNT(*) FROM num.Compteur WHERE Type IN ('ARRD', 'DMD') AND Annee = @a AND Jour = @j",
                                  ("@a", anneeDuJour), ("@j", lendemain));
    // Le jumeau : la veille, qui a deja un compteur dans la V2, continue au lieu de repartir a 1.
    var veille = jourDuJour - 1;
    var suiteVeille = await V3Attribuer("ARRD", anneeDuJour, veille, null, "V3");
    var attenduVeille = compteursV2.TryGetValue(("ARRD", anneeDuJour, veille), out var cVeille) ? cVeille[0].Chrono + 1 : 1;
    Exiger(nouveaux.Select(n => n.Chrono).SequenceEqual(new int?[] { 1, 2, 1, 2 }) && lignesJour == 2 && suiteVeille.Chrono == attenduVeille,
        $"NUM-016 CA03 : le jour change ({lendemain}) : ARRD et DMD repartent a 1 ({string.Join(", ", nouveaux.Select(n => n.Numero))}), " +
        $"un compteur du jour chacun ({lignesJour}) ; la veille ({veille}) continue : {suiteVeille.Numero}");
}
else Reste($"NUM-016 CA03 : jour {jourDuJour}, pas de lendemain dans l'annee pour le jouer");

// NUM-017 CA03 : cle des lettres recommandees, contre les numeros reels de RIG_DEV.
var lrReels = (await LireRigDev("SELECT TOP (3000) LTREC_NUM_RECOMMANDE FROM dbo.LETTRE_RECOMMANDEE WITH (NOLOCK) " +
    "WHERE LTREC_NUM_RECOMMANDE LIKE '[0-9A-Z][0-9A-Z] [0-9][0-9][0-9] [0-9][0-9][0-9] [0-9][0-9][0-9][0-9] [0-9]' ORDER BY LTREC_ID_LTREC DESC"))
    .Rows.Cast<DataRow>().Select(r => (string)r[0]).ToList();
var lrFausses = lrReels.Where(n => FinitionV2.CleLettreRecommandee(n.Substring(3, 3) + n.Substring(7, 3) + n.Substring(11, 4)) != n[^1..]).ToList();
NumeroFini lrV3;
await using (var cn = await OuvrirV3())
    lrV3 = await NumerosSpeciaux.LettreRecommandeeAsync(distributeur, cn, null, true, "agent.v3", "BANC");
var (_, lrBrutV2) = await V2Generer("LRARN", NumerosSpeciaux.AnneeLettresRecommandees, 0, null);
Exiger(lrReels.Count > 100 && lrFausses.Count == 0 && lrV3.Numero is not null && lrV3.Brut.Numero == lrBrutV2
       && lrV3.Numero == FinitionV2.LettreRecommandee(lrBrutV2!) && Regex.IsMatch(lrV3.Numero, @"^\w\w \d{3} \d{3} \d{4} \d$"),
    $"NUM-017 CA03 : la cle recalculee de {lrReels.Count} lettres recommandees reelles de RIG_DEV est la leur ({lrFausses.Count} fausse(s)) ; " +
    $"la V3 donne {lrV3.Numero} (brut {lrV3.Brut.Numero}), la V2 aurait donne le brut {lrBrutV2}, au format national");

// NUM-017 CA02 : fin de plage.
await Executer(v3.Connexion, "UPDATE num.Compteur SET FinPlage = Chrono + 3, AlertePlage = NULL WHERE Type = 'LRARN' AND Annee = 2000; " +
                              "UPDATE num.Compteur SET FinPlage = Chrono + 1000000, AlertePlage = 1000 WHERE Type = 'LRN' AND Annee = 2000;");
var alertes = (await Lire(v3.Connexion, "SELECT Type, Restants FROM num.PlageAEpuiser")).Rows.Cast<DataRow>().ToDictionary(r => (string)r[0], r => (int)r[1]);
var plage = new List<NumeroAttribue>();
for (var i = 0; i < 4; i++) plage.Add(await V3Attribuer("LRARN", 2000, 0, null, i % 2 == 0 ? "V3" : "V2"));
Exiger(alertes.TryGetValue("LRARN", out var restants) && restants == 3 && !alertes.ContainsKey("LRN")
       && plage.Take(3).All(n => n.Code == 0) && plage[3].Code == CodesNumerotation.SeriePleine && plage[3].Message!.StartsWith("Plage épuisée"),
    $"NUM-017 CA02 : plage LRARN a 3 numeros de sa fin : signalee (num.PlageAEpuiser, {restants} restants ; LRN, loin de sa fin, ne l'est pas) ; " +
    $"3 numeros donnes puis refus « {plage[3].Message} »");

// NUM-018 CA02 : lots INPI, un chrono seul.
Exiger(await FormeCommune("LTIA", "LTIC") && await FormeCommune("LTIA", "LTIF") && await FormeCommune("LTIC", "LTIF"),
    $"NUM-018 CA02 : LTIA, LTIC, LTIF ecrivent un chrono seul ({resultats["LTIA"].M1.Numero}, {resultats["LTIC"].M1.Numero}, {resultats["LTIF"].M1.Numero}) : " +
    "un lot = (serie, numero), formes partagees signalees ; derniere annee dans RIG_DEV : " +
    $"{string.Join(", ", new[] { "LTIA", "LTIC", "LTIF" }.Select(t => lignes.Where(l => l.Type == t).Max(l => l.Annee)))} (usage a mesurer, Q11)");

// NUM-019 CA02 : le numero de liaison, code de liaison du greffe et cle de la V2.
// L'oracle : les numeros que la serie LIAI de la V2 a ecrits, c'est-a-dire ceux de sa lettre (« G »). Les numeros de
// liaison d'autres lettres (J, B, M, U...) viennent d'autres emetteurs : leur cle ne suit pas CleNumLiaison (mesure du
// 2026-11-21 : J 34 091 sur 117 166, B 139 sur 139 differents) et la V3 ne les compose jamais.
var lettreLiaison = lignes.Where(l => l.Type == "LIAI" && !l.Erronee).OrderByDescending(l => l.Annee).First().Lettre ?? "";
var liaisonsReelles = (await LireRigDev("SELECT TOP (3000) PSSGC_NUM_LIAISON FROM dbo.PASSAGE_CFE WITH (NOLOCK) " +
    $"WHERE PSSGC_NUM_LIAISON COLLATE Latin1_General_BIN LIKE '{lettreLiaison}[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]' " +
    "ORDER BY PSSGC_NUM_LIAISON DESC")).Rows.Cast<DataRow>().Select(r => (string)r[0]).ToList();
var liaisonsFausses = liaisonsReelles.Where(n => FinitionV2.CleLiaison(n[..11]) != n[11..]).ToList();
NumeroFini liaisonV3, liaisonSansCode;
await using (var cn = await OuvrirV3())
{
    liaisonV3 = await NumerosSpeciaux.LiaisonAsync(distributeur, cn, null, "agent.v3", "BANC");
    await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
    await using (var d = new SqlCommand("DELETE FROM num.CodeLiaison;", cn, tx)) await d.ExecuteNonQueryAsync();
    liaisonSansCode = await NumerosSpeciaux.LiaisonAsync(distributeur, cn, tx, "agent.v3", "BANC");
    await tx.RollbackAsync();
}
var (_, liaisonBrutV2) = await V2Generer("LIAI", anneeDuJour, jourDuJour, GreffeAppel("LIAI"));
var codesLiaison = (await Lire(v3.Connexion, "SELECT Code FROM num.CodeLiaison ORDER BY Code")).Rows.Cast<DataRow>().Select(r => (string)r[0]).ToList();
Exiger(liaisonsReelles.Count > 100 && liaisonsFausses.Count == 0 && liaisonV3.Numero is not null && !liaisonV3.Numero.Contains("9999")
       && codesLiaison.Contains(liaisonV3.Numero[..5]) && liaisonV3.Brut.Numero == liaisonBrutV2
       && liaisonV3.Numero == FinitionV2.AvecCleLiaison(liaisonBrutV2!) && codesLiaison.Count == codesV2
       && liaisonSansCode.Numero is null && liaisonSansCode.Brut.Code != 0,
    $"NUM-019 CA02 : la cle recalculee de {liaisonsReelles.Count} numeros de liaison « {lettreLiaison} » reels de RIG_DEV est la leur ({liaisonsFausses.Count} fausse(s)) ; " +
    $"la V3 donne {liaisonV3.Numero} (code de liaison {liaisonV3.Numero?[..5]} parmi les {codesLiaison.Count} repris, jamais 9999), la V2 aurait donne " +
    $"{liaisonBrutV2} -> {FinitionV2.AvecCleLiaison(liaisonBrutV2 ?? "")} ; sans code de liaison, la V3 refuse ({liaisonSansCode.Message})");

// NUM-020 : les factures.
var factu = resultats["FACTU"];
Exiger(factu.M1.Code == 0 && factu.M2.Chrono == factu.M1.Chrono + 1,
    $"NUM-020 CA01 : la V2 continue sa serie FACTU, tenue par le compteur de la V3 : {factu.M1.Numero} puis {factu.M2.Numero}");
var facV3 = await V3Attribuer("FACV3", 0, 0, null, "V3");
var facV3b = await V3Attribuer("FACV3", 0, 0, null, "V3");
var lireAvec = "SELECT COUNT(*) FROM num.Compteur c CROSS APPLY num.LireNumero(@n, c.Structure, c.CodageAnnee, c.Lettre, c.Separateur, c.TailleChrono, " +
               "DATALENGTH(ISNULL(c.CodeGreffe, ''))) l WHERE c.Type = @t";
var v3LuParV2 = await Entier(v3.Connexion, lireAvec, ("@n", facV3.Numero), ("@t", "FACTU"));
var v2LuParV3 = await Entier(v3.Connexion, lireAvec, ("@n", factu.M1.Numero), ("@t", "FACV3"));
Exiger(facV3.Code == 0 && facV3.Chrono == 1 && facV3.Numero == $"{RepriseNumerotation.PrefixeFacturesV3}{anneeDuJour % 100:00}-000001"
       && v3LuParV2 == 0 && v2LuParV3 == 0,
    $"NUM-020 CA02 : la premiere facture de la V3 ouvre sa serie : {facV3.Numero} ; le format de la V2 ne peut pas l'ecrire (lu par FACTU : {v3LuParV2}) " +
    $"ni la V3 celui de la V2 (lu par FACV3 : {v2LuParV3}) -- prefixe provisoire, choix du referent Facturation");
ResultatRestitution annuleV2, annuleV3;
NumeroAttribue apresV2, apresV3;
await using (var cn = await OuvrirV3())
{
    annuleV2 = await distributeur.RestituerAsync(cn, null, new DemandeRestitution("FACTU", factu.M2.Numero!, "facture annulee par avoir"), "agent.v3", "BANC");
    annuleV3 = await distributeur.RestituerAsync(cn, null, new DemandeRestitution("FACV3", facV3b.Numero!, "facture annulee par avoir"), "agent.v3", "BANC");
    apresV2 = await distributeur.AttribuerAsync(cn, null, new DemandeNumero("FACTU"), "agent.v3", "BANC");
    apresV3 = await distributeur.AttribuerAsync(cn, null, new DemandeNumero("FACV3"), "agent.v3", "BANC");
}
Exiger(annuleV2.Issue == "non redonnable" && annuleV3.Issue == "non redonnable" && apresV2.Chrono == factu.M2.Chrono + 1 && apresV3.Chrono == facV3b.Chrono + 1,
    $"NUM-020 CA03 : une facture annulee ({factu.M2.Numero}, {facV3b.Numero}) n'est jamais redonnee ({annuleV2.Issue}) : l'avoir prend le suivant " +
    $"({apresV2.Numero}, {apresV3.Numero})");
var astraV2 = (await LireRigDev("SELECT DISTINCT FACTU_NUMERO_FACTURE FROM dbo.FACTURE WITH (NOLOCK) WHERE FACTU_NUMERO_FACTURE NOT LIKE '[0-9][0-9]-%'"))
    .Rows.Cast<DataRow>().Select(r => (string)r[0]).Where(n => !Regex.IsMatch(n, @"^\d{2}-\d{6}$")).OrderBy(x => x, StringComparer.Ordinal).ToList();
var astraV3 = (await Lire(v3.Connexion, "SELECT Valeur FROM num.ValeurHeritee WHERE Fiche = 'FACTURE' ORDER BY Valeur")).Rows.Cast<DataRow>().Select(r => (string)r[0]).ToList();
Exiger(astraV2.Count > 0 && astraV3.SequenceEqual(astraV2)
       && await Entier(v3.Connexion, "SELECT COUNT(*) FROM num.Signalement WHERE Sujet = 'numero hors format' AND Referent = 'Facturation'") == astraV2.Count,
    $"NUM-020 CA04 : factures hors format de RIG_DEV ({string.Join(", ", astraV2)}) gardees comme valeurs heritees, hors de toute serie, listees au referent Facturation");
var facturesJournal = (await Lire(v3.Connexion, "SELECT Serie, Numero, Chrono FROM num.Journal WHERE Serie IN ('FACTU', 'FACV3') AND Evenement = 'attribution' ORDER BY Id"))
    .Rows.Cast<DataRow>().Select(r => ((string)r[0], (string)r[1], (int)r[2])).ToList();
var continues = facturesJournal.GroupBy(f => f.Item1).All(g => g.Select(f => f.Item3).Zip(g.Select(f => f.Item3).Skip(1)).All(p => p.Second == p.First + 1));
var communes = facturesJournal.Where(f => f.Item1 == "FACTU").Select(f => f.Item2).Intersect(facturesJournal.Where(f => f.Item1 == "FACV3").Select(f => f.Item2)).Count();
Exiger(continues && communes == 0 && facturesJournal.Select(f => f.Item1).Distinct().Count() == 2,
    $"NUM-020 CA05 : {facturesJournal.Count} factures donnees ce passage : chaque serie continue de son cote, {communes} numero dans les deux");
Reste("NUM-020 CA06 : numero de la V3 repris dans la facture electronique -- la facture electronique est SOC-020 (phase suivante) ; le numero est deja unique dans FACV3");

// ====================================================================================================
// 5. NUM-021 : les numeros calcules hors du compteur commun
// ====================================================================================================
// CA02, CA03 : le releve (source, fiche, partage V2/V3, regle). Chaque source est relue a sa ligne.
var inventaire = new (string Source, int Ligne, string Jeton, string Fiche, string Partage, string Regle)[]
{
    (@"Outils\DLL\RigMetier\_Dossier\DossierRCS.cs", 2138, "numOrdre = 1", "MENTION_OBSERVATION.MNTOB_NUM_ORDRE (ordre des mentions d'un dossier)", "oui (dossier RCS, Q7)", "num.OrdreFiche « MENTION_OBSERVATION », repris au plus grand de chaque dossier"),
    (@"RCS\Exe\PROC_REQUI\frmREQUI.cs", 3140, "MAX(ETBLS_NUM_ORDRE)", "ETABLISSEMENT.ETBLS_NUM_ORDRE (etablissements NQ d'un dossier)", "oui (dossier RCS, Q7)", "num.OrdreFiche « ETABLISSEMENT », premier = 2 comme la V2"),
    (@"Edi\DLL\EdiMetier\EdiBanqueFrance\EdiBanqueFranceAvisJUDEdiSortant.cs", 108, "MAX(EDIBDF_NUM_GESTION)", "EDI_BDF.EDIBDF_NUM_GESTION (« <annee>F » des non-inscrits)", "oui (avis BDF des deux logiciels)", "compteur V3 BDFNI, repris au plus grand de l'annee"),
    (@"Edi\DLL\TransInfogreffeMetier\TransmissionJud.cs", 28, "MAX(TRJUD_NUM_TRANS)", "TRANSMISSION_JUD.TRJUD_NUM_TRANS (base des transmissions Infogreffe)", "oui si la V3 transmet", "compteur V3 dans la base des echanges : domaine Echanges"),
    (@"VB6\Bin Ult Cliente\CLI_VALLIN\frmVisuDepotActe.frm", 5994, "MAX(EVNRC_NUM_ORDRE)", "EVENEMENT_RCS / ACTE_DEPOSE NUM_ORDRE (traitement propre a un client)", "non (traitement d'un seul greffe)", "reste local, usage a mesurer (Q11)"),
    (@"Judiciaire\Exe\PROC_INJONCTION_DCA\OLD\frmInjonctionsDCA.cs", 588, "MAX(DMNED_NUM_ORDRE_GROUPE)", "DEMANDE_EDITION.DMNED_NUM_ORDRE_GROUPE (dossier OLD)", "non (code retire)", "aucun : code hors service"),
};
var sourcesV2 = @"D:\RigApplication\Source";
if (Directory.Exists(sourcesV2))
{
    var lues = inventaire.Where(i =>
    {
        var f = Path.Combine(sourcesV2, i.Source);
        if (!File.Exists(f)) return false;
        var texte = File.ReadLines(f, Encoding.Latin1).Skip(i.Ligne - 3).Take(6);
        return texte.Any(l => l.Contains(i.Jeton, StringComparison.OrdinalIgnoreCase));
    }).Count();
    Exiger(lues == inventaire.Length,
        $"NUM-021 CA02 : {inventaire.Length} numeros calcules hors des trois programmes, chacun relu a sa source ({lues}/{inventaire.Length}) : " +
        string.Join(" ; ", inventaire.Select(i => $"{i.Fiche} [{i.Source}:{i.Ligne}]")));
}
else Reste($"NUM-021 CA02 : relire les sources du releve -- {sourcesV2} absent de ce poste");
Exiger(inventaire.All(i => i.Regle.Length > 0) && inventaire.Where(i => i.Partage.StartsWith("oui")).All(i => i.Regle.Contains("V3") || i.Regle.Contains("num.")),
    "NUM-021 CA03 : chaque numero partage a sa regle (compteur ou fiche tenus par la V3), un numero propre a un seul greffe ou retire reste local");
Reste("NUM-021 CA02 : EDI_GREFFE.dbo.NUM_TRANSMISSION et la base des transmissions -- lecture refusee au compte de ce poste (« Cannot open database EDI_GREFFE », 2026-11-21) : a relever avec un droit de lecture");

// CA01 : l'ordre des mentions d'un dossier.
var mentionsMax = await LireRigDev("SELECT COUNT(*), SUM(CAST(m AS bigint)) FROM (SELECT MAX(ISNULL(MNTOB_NUM_ORDRE, 0)) AS m FROM dbo.MENTION_OBSERVATION WITH (NOLOCK) WHERE MNTOB_ID_DSRCS IS NOT NULL GROUP BY MNTOB_ID_DSRCS) x");
var ordresV3 = await Lire(v3.Connexion, "SELECT COUNT(*), SUM(CAST(Dernier AS bigint)) FROM num.OrdreFiche WHERE Fiche = 'MENTION_OBSERVATION'");
var dossierPlein = await Lire(v3.Connexion, "SELECT TOP (1) Cle, Dernier FROM num.OrdreFiche WHERE Fiche = 'MENTION_OBSERVATION' ORDER BY Dernier DESC, Cle");
var (cleDossier, dernierMention) = ((int)dossierPlein.Rows[0][0], (int)dossierPlein.Rows[0][1]);
int ordreV3, ordreV2, ordreNeuf, ordreEtabNeuf;
await using (var cn = await OuvrirV3())
{
    ordreV3 = await NumerosSpeciaux.OrdreAsync(cn, null, "MENTION_OBSERVATION", cleDossier);
    ordreV2 = await NumerosSpeciaux.OrdreAsync(cn, null, "MENTION_OBSERVATION", cleDossier);
    ordreNeuf = await NumerosSpeciaux.OrdreAsync(cn, null, "MENTION_OBSERVATION", -1);
    ordreEtabNeuf = await NumerosSpeciaux.OrdreAsync(cn, null, "ETABLISSEMENT", -1, premier: 2);
}
Exiger(Convert.ToInt64(ordresV3.Rows[0][0]) == Convert.ToInt64(mentionsMax.Rows[0][0]) && Convert.ToInt64(ordresV3.Rows[0][1]) == Convert.ToInt64(mentionsMax.Rows[0][1])
       && ordreV3 == dernierMention + 1 && ordreV2 == dernierMention + 2 && ordreNeuf == 1 && ordreEtabNeuf == 2,
    $"NUM-021 CA01 : ordre des mentions repris pour {ordresV3.Rows[0][0]} dossiers (= RIG_DEV, somme des plus grands {ordresV3.Rows[0][1]}) ; " +
    $"au dossier {cleDossier} (dernier {dernierMention}) une mention V3 puis une V2 recoivent {ordreV3} et {ordreV2}, jamais deux fois le meme ; " +
    $"dossier sans mention : {ordreNeuf} ; etablissement NQ sans ordre : {ordreEtabNeuf} comme la V2");

// CA04 : « <annee>F » des non-inscrits, donne par la V3 aux deux logiciels.
var maxBdf = (string?)await ScalaireRigDev($"SELECT MAX(EDIBDF_NUM_GESTION) FROM dbo.EDI_BDF WITH (NOLOCK) WHERE EDIBDF_NUM_GESTION LIKE '{anneeDuJour}F%'");
// Le calcul de la V2 (EdiBanqueFranceAvisJUDEdiSortant.cs:110-115), rejoue en lecture.
var suivantV2 = string.IsNullOrEmpty(maxBdf) ? $"{anneeDuJour}F00001" : maxBdf[..5] + (int.Parse(maxBdf.Substring(5, 5)) + 1).ToString("00000");
var bdfDonneV2 = await V3Attribuer("BDFNI", 0, 0, null, "V2");
var bdfDonneV3 = await V3Attribuer("BDFNI", 0, 0, null, "V3");
Exiger(bdfDonneV2.Numero == suivantV2 && bdfDonneV3.Chrono == bdfDonneV2.Chrono + 1 && bdfDonneV3.Numero != bdfDonneV2.Numero,
    $"NUM-021 CA04 : plus grand non-inscrit {anneeDuJour} de RIG_DEV {maxBdf ?? "(aucun)"} : le premier avis de la V2 recoit {bdfDonneV2.Numero} (ce que la V2 aurait calcule : {suivantV2}), " +
    $"celui de la V3 {bdfDonneV3.Numero}");

// ====================================================================================================
// 6. NUM-005 CA04 : apres la reprise, pas d'annee au-dela de la suivante
// ====================================================================================================
var future = futures.First(l => !l.Erronee);
var refusFutur = await V3Attribuer(future.Type, future.Annee, future.Cle.Jour > 0 ? future.Cle.Jour : 1, null, "V3");
var chronoFutur = await Entier(v3.Connexion, "SELECT ISNULL(Chrono, 0) FROM num.Compteur WHERE Type = @t AND Annee = @a AND Jour = @j",
                               ("@t", future.Type), ("@a", future.Annee), ("@j", future.Cle.Jour));
var anneeSuivante = await V3Attribuer("RGB", anneeDuJour + 1, 0, null, "V3");
Exiger(refusFutur.Code == CodesNumerotation.AnneeHorsLimite && refusFutur.Message!.StartsWith("Année refusée") && chronoFutur == future.Chrono
       && anneeSuivante.Code == 0 && anneeSuivante.Chrono == 1,
    $"NUM-005 CA04 : une demande {future.Type} {future.Annee} est refusee (code {refusFutur.Code}, « {refusFutur.Message?[..Math.Min(70, refusFutur.Message.Length)]}… »), " +
    $"le compteur repris reste a {chronoFutur} ; l'annee suivante reste permise (RGB {anneeDuJour + 1} : {anneeSuivante.Numero})");

Reste("NUM-005 CA07 / T04 / T08 : rejouer la reprise sur les 40 copies anonymisees (zero ecart, outre-mer compris) -- aucune copie anonymisee sur ce poste ; " +
      "ce banc la joue sur RIG_DEV");
Reste("NUM-005 CA08 : la reprise inverse (NUM-022, retour a la V2 pendant la beta) -- story NUM-022, hors de ce lot");
Reste("NUM-005 CA01 : remplacement des trois programmes de la V2 dans la meme transaction -- lot 7 (Base\\v2\\bascule.sql) ; num.Reprendre travaille deja dans la transaction de l'appelant");

// --- Le releve, a cote du banc -----------------------------------------------------------------------
var fichierReleve = Path.Combine(AppContext.BaseDirectory, "releve-series.md");
await File.WriteAllTextAsync(fichierReleve, $"Releve du {DateTime.Now:yyyy-MM-dd HH:mm}, RIG_DEV ({serveurV2}), jour {jourDuJour}\n\n" + releve);
Info($"releve serie par serie : {fichierReleve}");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0
    ? $"NumerotationSeriesCheck : VERT -- reprise de RIG_DEV et {typesCarnet.Count} series controlees ({chrono.Elapsed.TotalSeconds:0} s)"
    : $"NumerotationSeriesCheck : ROUGE -- {echecs.Count} echec(s)");
return echecs.Count == 0 ? 0 : 1;

// Le numero ecrit selon le format d'une ligne de la V2, comme GENERER_NUMERO.sql:245-288 (pour les calculs du banc).
static string Composer(LigneV2 l, int annee, int jour, int chrono)
{
    var codeAnnee = l.Codage switch { 0 => "", 1 => (annee % 10).ToString(), 2 => (annee % 100).ToString("00"), 4 => annee.ToString(), _ => "XXXX" };
    var c = chrono.ToString().PadLeft(l.Taille, '0');
    var sb = new StringBuilder();
    foreach (var ch in l.Structure)
        sb.Append(ch switch
        {
            'A' => codeAnnee, 'S' => l.Separateur ?? "", 'C' => c, 'L' => l.Lettre ?? "", 'Q' => jour.ToString("000"),
            'G' => l.CodeGreffe ?? "", 'K' => "*", _ => ch.ToString(),
        });
    return sb.ToString();
}

/// <summary>Une ligne de NUMERO_METIER (V2).</summary>
internal sealed record LigneV2(int Id, string Type, string Structure, int Annee, short? Quantieme, int Chrono, int Debut, bool Gestion,
                               bool Erronee, string? Explication, string? Redirection, byte Codage, string? Lettre, string? Separateur,
                               short Taille, string? CodeGreffe)
{
    /// <summary>La cle du compteur dans la V3 : (type, annee, jour si le format porte « Q », sinon 0).</summary>
    public (string Type, int Annee, int Jour) Cle => (Type, Annee, Structure.Contains('Q') ? Quantieme ?? 0 : 0);
}
