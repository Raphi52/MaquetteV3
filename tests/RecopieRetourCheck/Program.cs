using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RecopieTransfertCheck;
using RigV3.Base;
using RigV3.Contrats.Recopie;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Recopie;
using RigV3.Serveur.Socle.Chaines;
using RigV3.Serveur.Socle.Greffes;

// Banc de REC-007, REC-009, REC-010, REC-011 et REC-018 (carnet\recopie.json), lot 11 du plan de reconstruction, phase 2
// (2026-11-21). Un controle par critere, qui cite son id.
//
//  0. Une base V2 jetable tiree de RIG_DEV (ExtraitRigDev, lecture seule) avec toutes les tables des regles du serveur ; les
//     fixtures des anciennes reprises (correspondances, tables de reprise base a base, fiches de remplissage) et d'un dossier
//     ou une demande en cours bloque les autres ; les declencheurs de date de validation de la V2 REACTIVES ;
//     Base\v2\recopie-v2.sql ; le transfert avec les regles du serveur ; la mise en service de la recopie (PontRecopie), qui
//     pose la garde des affaires (GardeAffaires) ; la fiche du pont de la V2 (Base\v2\pont-v2.sql, greffe beta).
//  A. REC-009 : anciens numeros, fiches de remplissage, tables de reprise en lecture seule.
//  B. REC-010 : l'historique d'une entreprise aux deux origines, une validation de la V2 relue, le refus d'ecrire, les echanges.
//  C. REC-011 : blocage par une ancienne demande pose puis leve, demande d'origine, lien vers la V3 ecrit au retour.
//  D. REC-018 : une arrivee de la V3 dans la V2, une ancienne demande qui s'y rattache, la liste des deux logiciels.
//  E. REC-007 : la decision reservee, le jour J, les types sans equivalent, le retour d'un mois d'affaires, la V3 fermee,
//     les chaines signees, le controle de nuit ensuite.
// Il lui faut « docker compose up -d » et l'acces en lecture a RIG_DEV (autre serveur : RIGV3_RIG_DEV).
// Doc : docs/reconstruction/recopie.md, sections du lot 11.
// fix-ok: banc NEUF du lot 11, relu avant son premier lancement : le modele des affaires du mois pouvait etre sans affaire
// (DMND_ID_AFFRE NULL, conversion de DBNull), CONCAT rend un bit en « 1 » et non « True », le type de la V3 seule a onze colonnes
// obligatoires (copie d'un type existant), et la comparaison d'ARRIVEE avant / apres le retour etait retiree sans raison.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var migrations = Path.Combine(racine, "Base", "migrations");
var scriptV2 = Path.Combine(racine, "Base", "v2", "recopie-v2.sql");
var pontV2 = Path.Combine(racine, "Base", "v2", "pont-v2.sql");
var chaineRigDev = Environment.GetEnvironmentVariable("RIGV3_RIG_DEV") is { Length: > 0 } c0 ? c0 :
    @"Server=SQL-DEV\DEV;Database=RIG_DEV;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;ApplicationIntent=ReadOnly;Connect Timeout=20";
const string Greffe = "8002";
const string Responsable = @"AMITEL\responsable.retour";

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
void Reste(string quoi) => Console.WriteLine("RESTE : " + quoi);
void Info(string quoi) => Console.WriteLine("        " + quoi);
// Ce qu'un controle de nuit a a traiter, compte par genre, table et colonne (un lien orphelin : sa colonne), avec trois exemples par groupe.
string ATraiterEnClair(RapportComparaison r) => r.Details.Count == 0 ? "" : " -- a traiter : " + string.Join(" ; ", r.Details
    .GroupBy(d => $"{d.Genre} {d.TableV2}{(d.Genre == "lien orphelin" ? "." + d.Colonnes : "")}")
    .Select(g => $"{g.Key} x{g.Count()} ({string.Join(", ", g.Take(3).Select(d => d.Genre == "lien orphelin" ? $"{d.IdV3} : {d.Detail}" : $"{d.IdV2}/{d.IdV3} {d.Colonnes}"))})"));
int Bilan()
{
    Console.WriteLine();
    Console.WriteLine(echecs.Count == 0 ? "RecopieRetourCheck : VERT (REC-007, REC-009, REC-010, REC-011, REC-018)"
                                        : $"RecopieRetourCheck : ROUGE, {echecs.Count} echec(s) : {string.Join(" | ", echecs)}");
    return echecs.Count == 0 ? 0 : 1;
}

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
async Task<string?> Texte(string chaine, string sql) => await Valeur(chaine, sql) is { } v ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
async Task<long> Nombre(string chaine, string sql) => Convert.ToInt64(await Valeur(chaine, sql) ?? 0L, CultureInfo.InvariantCulture);
async Task Executer(string chaine, string sql)
{
    await using var cn = new SqlConnection(chaine);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
    await cmd.ExecuteNonQueryAsync();
}
async Task<SqlException?> Refus(string chaine, string sql)
{
    try { await Executer(chaine, sql); return null; }
    catch (SqlException e) { return e; }
}
List<string> Colonne0(DataTable t) => t.Rows.Cast<DataRow>().Select(r => Convert.ToString(r[0], CultureInfo.InvariantCulture)!).ToList();
// Une copie d'une ligne existante, colonnes remplacees par des expressions SQL (sur la ligne modele) : l'identifiant donne.
async Task<long> Dupliquer(string chaine, string table, string cle, string modele, Dictionary<string, string> remplace)
{
    var cols = Colonne0(await Lire(chaine, $"SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID('{table}') AND c.is_identity = 0 AND c.is_computed = 0 " +
                                           "AND TYPE_NAME(c.system_type_id) <> 'timestamp' ORDER BY c.column_id"));
    var inconnues = remplace.Keys.Where(k => !cols.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
    if (inconnues.Count > 0) throw new InvalidOperationException($"{table} : colonnes inconnues {string.Join(", ", inconnues)}");
    return await Nombre(chaine, $"DECLARE @i TABLE (Id bigint); INSERT INTO {table} ({string.Join(", ", cols.Select(c => $"[{c}]"))}) " +
                                $"OUTPUT CONVERT(bigint, inserted.[{cle}]) INTO @i SELECT {string.Join(", ", cols.Select(c => remplace.TryGetValue(c, out var v) ? v : $"[{c}]"))} " +
                                $"FROM {table} WHERE [{cle}] = {modele}; SELECT Id FROM @i;");
}

// ====================================================================================================
// 0. RIG_DEV, la base V2 jetable et ses fixtures, le suivi des changements, le transfert, la mise en service
// ====================================================================================================
try
{
    LectureV2.ExigerLecture("SELECT @@SERVERNAME");
    Info($"RIG_DEV : {await Valeur(chaineRigDev, "SELECT @@SERVERNAME + ' / ' + DB_NAME()")}");
}
catch (SqlException e)
{
    Exiger(false, $"RIG_DEV lisible : {e.Message} -- il faut l'acces en lecture a SQL-DEV\\DEV (ou RIGV3_RIG_DEV)");
    return Bilan();
}

var reglesServeur = ReglesTransfert.Decouvrir();
var reglesRecopie = ReglesRecopie.Decouvrir();
Exiger(reglesRecopie.Where(r => r.RemonteeSeule).Select(r => r.TableV2).Order().SequenceEqual(["AFFAIRE", "DEMANDE"])
       && reglesServeur.Regles.Any(r => r.Table == "CORRESP_MIGRATION_RIG" && r.Sorte == SorteRegle.Reprise)
       && ReglesRec009Tables().All(t => reglesServeur.Regles.Any(r => r.Table == t)),
    "REC-010 T02 / REC-009 T02 : les affaires et les demandes remontent seules (de la V2 vers la V3) ; les trois tables de correspondance passent par la reprise " +
    "« anciens numeros », les tables de reprise base a base et les articles Replay ont leur regle");
static IEnumerable<string> ReglesRec009Tables() => ["MIGRATION_REFERENCE", "MIGRATION_TABLE", "MIGRATION_CHAMP", "MIGRATION_ID", "ARTICLE_CORRESP_REPLAY"];

async Task<string> Ids(string sql) => string.Join(", ", Colonne0(await Lire(chaineRigDev, sql)).Append("-1"));
var idsAffaires = await Ids("SELECT TOP (70) a.AFFRE_ID_AFFRE FROM dbo.AFFAIRE AS a WHERE a.AFFRE_ID_CLIEN IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.DEMANDE AS d " +
                            "WHERE d.DMND_ID_AFFRE = a.AFFRE_ID_AFFRE AND d.DMND_ID_DSRCS IS NOT NULL) ORDER BY a.AFFRE_ID_AFFRE DESC");
var idsDossiers = await Ids($"SELECT DISTINCT TOP (200) DMND_ID_DSRCS FROM dbo.DEMANDE WHERE DMND_ID_AFFRE IN ({idsAffaires}) AND DMND_ID_DSRCS IS NOT NULL");
var entieres = reglesServeur.Regles.Where(r => r.Sorte == SorteRegle.Reprise).Select(r => r.Table)
    .Concat(["ENCAISSEMENT_REPRIS", "ARTICLE_CORRESP_REPLAY", "MIGRATION_REFERENCE", "MIGRATION_TABLE", "MIGRATION_CHAMP", "MIGRATION_ID"])
    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
var extraits = ExtraitRigDev.Completer(new Dictionary<string, string>
{
    ["AFFAIRE"] = $"SELECT * FROM dbo.AFFAIRE WHERE AFFRE_ID_AFFRE IN ({idsAffaires})",
    ["DEMANDE"] = $"SELECT * FROM dbo.DEMANDE WHERE DMND_ID_AFFRE IN ({idsAffaires})",
    ["DOSSIER_RCS"] = $"SELECT * FROM dbo.DOSSIER_RCS WHERE DSSRC_ID_DSRCS IN ({idsDossiers})",
    ["INSCRIPTION"] = "SELECT TOP (40) * FROM dbo.INSCRIPTION ORDER BY INEDT_ID_INSCR DESC",
    ["ARRIVEE"] = "SELECT TOP (30) * FROM dbo.ARRIVEE ORDER BY ARRV_ID_ARRV DESC",
    ["CLIENT"] = "SELECT TOP (40) * FROM dbo.CLIENT ORDER BY CLIEN_ID_CLIEN DESC",
}, entieres, reglesServeur.Regles.Select(r => r.Table));

await using var v2 = await BaseJetable.CreerAsync("RecopieRetourV2");
var chrono = Stopwatch.StartNew();
var extrait = await ExtraitRigDev.ConstruireAsync(chaineRigDev, v2.Connexion, entieres, extraits, Info);
Info($"base V2 jetable {v2.Nom} : {extrait.Lignes.Values.Sum()} lignes dans {extrait.Lignes.Count} tables, {extrait.Cles} cles verifiees, en {chrono.Elapsed.TotalSeconds:0} s ; " +
     string.Join(", ", extrait.Lignes.Where(l => l.Key is "AFFAIRE" or "DEMANDE" or "DOSSIER_RCS" or "ARRIVEE" or "CLIENT" or "ARTICLE_CORRESP_REPLAY").Select(l => $"{l.Key} {l.Value}")));

// --- Les fixtures, posees dans la V2 AVANT le suivi et le transfert ---
// REC-009 CA01, CA02 : 20 correspondances d'anciens logiciels (RIG_DEV n'en a aucune, lu le 2026-11-20 : ce n'est pas un greffe
// repris), comme une reprise de 2017 les a laissees : dossiers et inscriptions de l'extrait, ancien numero, numero RIG, Infogreffe.
var dossiersRig = (await Lire(v2.Connexion, "SELECT TOP (14) DSSRC_ID_DSRCS, DSSRC_NUM_GESTION FROM dbo.DOSSIER_RCS WHERE DSSRC_NUM_GESTION IS NOT NULL " +
                                            "AND DSSRC_NUM_GESTION IN (SELECT DSSRC_NUM_GESTION FROM dbo.DOSSIER_RCS GROUP BY DSSRC_NUM_GESTION HAVING COUNT(*) = 1) ORDER BY DSSRC_ID_DSRCS"))
    .Rows.Cast<DataRow>().Select(r => (Id: Convert.ToInt32(r[0]), Numero: (string)r[1])).ToList();
var inscriptionsRig = (await Lire(v2.Connexion, "SELECT TOP (6) INEDT_ID_INSCR, INEDT_NUM_INSCRIPTION FROM dbo.INSCRIPTION WHERE INEDT_NUM_INSCRIPTION IS NOT NULL " +
                                                "AND INEDT_NUM_INSCRIPTION IN (SELECT INEDT_NUM_INSCRIPTION FROM dbo.INSCRIPTION GROUP BY INEDT_NUM_INSCRIPTION HAVING COUNT(*) = 1) ORDER BY INEDT_ID_INSCR"))
    .Rows.Cast<DataRow>().Select(r => (Id: Convert.ToInt32(r[0]), Numero: Convert.ToString(r[1], CultureInfo.InvariantCulture)!)).ToList();
var anciens = new List<(string Logiciel, string Table, int Id, string NumeroRig, string Ancien, string Infogreffe)>();
for (var i = 0; i < dossiersRig.Count && i < 10; i++)
    anciens.Add(("migration", "DOSSIER_RCS", dossiersRig[i].Id, dossiersRig[i].Numero, $"DED-2004-{i:000}", $"IG{dossiersRig[i].Numero}"));
foreach (var (ins, i) in inscriptionsRig.Select((x, i) => (x, i)))
    anciens.Add(("migration", "INSCRIPTION", ins.Id, ins.Numero, $"ALI-INS-{i:000}", $"IG-INS-{ins.Numero}"));
for (var i = 10; i < dossiersRig.Count && i < 12; i++)
    anciens.Add(("Oracle", "DOSSIER_RCS", dossiersRig[i].Id, dossiersRig[i].Numero, $"ORA-{i:000}", $"IG{dossiersRig[i].Numero}"));
for (var i = 12; i < dossiersRig.Count && i < 14; i++)
    anciens.Add(("GAGI", "DOSSIER_RCS", dossiersRig[i].Id, dossiersRig[i].Numero, $"{77000 + i}", $"IG{dossiersRig[i].Numero}"));
foreach (var a in anciens)
    await Executer(v2.Connexion, a.Logiciel switch
    {
        "migration" => $"INSERT INTO dbo.CORRESP_MIGRATION_RIG (MIGRI_CODE_GREFFE, MIGRI_TABLE, MIGRI_NO_MIG, MIGRI_NUMERO_MIG, MIGRI_ID_RIG, MIGRI_NUMERO_RIG, MIGRI_NUMERO_INFOGREFFE) " +
                       $"VALUES ('{Greffe}', '{a.Table}', 'NO-{a.Ancien}', '{a.Ancien}', {a.Id}, '{a.NumeroRig}', '{a.Infogreffe}')",
        "Oracle" => $"INSERT INTO dbo.CORRESP_ORACLE_RIG (ORARI_CODE_GREFFE, ORARI_TABLE, ORARI_NO_ORACLE, ORARI_NUMERO_ORACLE, ORARI_ID_RIG, ORARI_NUMERO_RIG, ORARI_NUMERO_INFOGREFFE) " +
                    $"VALUES ('{Greffe}', '{a.Table}', 'NO-{a.Ancien}', '{a.Ancien}', {a.Id}, '{a.NumeroRig}', '{a.Infogreffe}')",
        _ => $"INSERT INTO dbo.CORRESP_GAGI_RIG (GAGRI_CODE_GREFFE, GAGRI_TABLE, GAGRI_ID_GAGI, GAGRI_NUMERO_GAGI, GAGRI_ID_RIG, GAGRI_NUMERO_RIG, GAGRI_NUMERO_INFOGREFFE) " +
             $"VALUES ('{Greffe}', '{a.Table}', {a.Ancien}, 'GAGI-{a.Ancien}', {a.Id}, '{a.NumeroRig}', '{a.Infogreffe}')",
    });
// REC-009 CA04 : une reprise base a base (serveur, base et greffe source et destination, anciens et nouveaux identifiants, fusion,
// erreur) ; MIGRATION_CHAMP reste vide (« meme vides »).
await Executer(v2.Connexion, """
    INSERT INTO dbo.MIGRATION_REFERENCE VALUES (1, 'RIGBD-ANCIEN', 'RIG_ANCIEN', 'SQL-PROD\PROD', 'RIG_BANC', '8001', '8002');
    INSERT INTO dbo.MIGRATION_TABLE (MGRTB_ID_MGRRF, MGRTB_TABLE_ALIAS, MGRTB_TABLE_NOM, MGRTB_PRIMARY_KEY, MGRTB_FILTRE, MGRTB_REFERENCE)
        VALUES (1, 'CLIENT', 'CLIENT', 'CLIEN_ID_CLIEN', NULL, 0), (1, 'DOSSIER', 'DOSSIER_RCS', 'DSSRC_ID_DSRCS', NULL, 1);
    INSERT INTO dbo.MIGRATION_ID (MGRID_ID_MGRRF, MGRID_TABLE_ALIAS, MGRID_ID_ANCIEN, MGRID_ID_NOUVEAU, MGRID_FUSION, MGRID_ERR_NUMERO, MGRID_ERR_DESCRIPTION)
        VALUES (1, 'CLIENT', 101, 9101, 0, NULL, NULL), (1, 'CLIENT', 102, 9101, 1, NULL, NULL), (1, 'DOSSIER', 55, NULL, 0, 547, 'conflit de cle');
    """);
// REC-009 CA03 : les fiches que les reprises de 2017 creaient pour un lien casse (MigrationClient.cs:211-215, ClientExtended.cs:51).
var modeleClient = (await Texte(v2.Connexion, "SELECT TOP (1) CONVERT(varchar(20), CLIEN_ID_CLIEN) FROM dbo.CLIENT ORDER BY CLIEN_ID_CLIEN"))!;
foreach (var (abrege, nom, note) in new[] { ("PC-0001", "Fake data PC-0001", "Client crée automatiquement par la migration RIG"),
                                            ("PC-0002", "Fake data PC-0002", "Client crée automatiquement par la migration RIG"),
                                            ("MIG-0003", "SCP MARTIN", "Client crée automatiquement par la migration RIG") })
    await Dupliquer(v2.Connexion, "dbo.CLIENT", "CLIEN_ID_CLIEN", modeleClient,
        new() { ["CLIEN_ABREGE"] = $"'{abrege}'", ["CLIEN_NOM"] = $"'{nom}'", ["CLIEN_BLOC_NOTES"] = $"'{note}'" });
var modeleAdresse = (await Texte(v2.Connexion, "SELECT TOP (1) CONVERT(varchar(20), ADRBS_ID_ADRBS) FROM dbo.ADRESSE_BASE ORDER BY ADRBS_ID_ADRBS"))!;
for (var i = 0; i < 2; i++)
    await Dupliquer(v2.Connexion, "dbo.ADRESSE_BASE", "ADRBS_ID_ADRBS", modeleAdresse,
        new() { ["ADRBS_NOM_VOIE"] = $"'{AnciensNumeros.AdresseInconnue}'", ["ADRBS_CODE_POSTAL"] = "'42000'", ["ADRBS_LOCALITE_POSTALE"] = "'SAINT ETIENNE'" });
const string ReperesRemplissage = "SELECT N'CLIENT', CONVERT(nvarchar(200), CLIEN_ID_CLIEN) FROM dbo.CLIENT WHERE CLIEN_NOM LIKE N'Fake data%' OR CLIEN_BLOC_NOTES LIKE N'Client cr%automatiquement par la migration%' " +
                                  "UNION ALL SELECT N'ADRESSE_BASE', CONVERT(nvarchar(200), ADRBS_ID_ADRBS) FROM dbo.ADRESSE_BASE WHERE ADRBS_NOM_VOIE = N'* Adresse inconnu pour le client*'";
var remplissageV2 = (await Lire(v2.Connexion, ReperesRemplissage)).Rows.Cast<DataRow>().Select(r => $"{r[0]} {r[1]}").Order().ToList();

// REC-011 CA01 : un dossier ou une demande encore en cours bloque deja une autre demande du dossier (la regle de la V2,
// ULT_9ANLWIN.cls:148 : DMND_ID_DMND_INHIBITRICE, DMND_INHIBEE) ; ses autres demandes sont closes. Et un dossier sans blocage.
var dossiers2 = Colonne0(await Lire(v2.Connexion, "SELECT TOP (2) DMND_ID_DSRCS FROM dbo.DEMANDE WHERE DMND_ID_DSRCS IS NOT NULL GROUP BY DMND_ID_DSRCS " +
                                                  "HAVING COUNT(*) >= 2 ORDER BY COUNT(*) DESC, DMND_ID_DSRCS"));
var dossierBloque = dossiers2[0];
var dossierLibre = dossiers2[1];
var (bloquante, bloquee) = (await Lire(v2.Connexion, $"SELECT TOP (2) DMND_ID_DMND FROM dbo.DEMANDE WHERE DMND_ID_DSRCS = {dossierBloque} ORDER BY DMND_ID_DMND"))
    .Rows.Cast<DataRow>().Select(r => Convert.ToInt64(r[0])).ToList() switch { var l => (l[0], l[1]) };
await Executer(v2.Connexion, $"""
    UPDATE dbo.DEMANDE SET DMND_ETAT_DEMANDE = 'V', DMND_INHIBEE = 0, DMND_ID_DMND_INHIBITRICE = NULL WHERE DMND_ID_DSRCS IN ({dossierBloque}, {dossierLibre});
    UPDATE dbo.DEMANDE SET DMND_ID_DMND_INHIBITRICE = NULL, DMND_INHIBEE = 0 WHERE DMND_ID_DMND_INHIBITRICE IN (SELECT DMND_ID_DMND FROM dbo.DEMANDE WHERE DMND_ID_DSRCS IN ({dossierBloque}, {dossierLibre}));
    UPDATE dbo.DEMANDE SET DMND_ETAT_DEMANDE = 'Q', DMND_DATE_VALIDATION = NULL WHERE DMND_ID_DMND = {bloquante};
    UPDATE dbo.DEMANDE SET DMND_ETAT_DEMANDE = 'Q', DMND_DATE_VALIDATION = NULL, DMND_INHIBEE = 1, DMND_ID_DMND_INHIBITRICE = {bloquante},
                           DMND_TEXTE_INHIBITEUR = 'Annulation en cours' WHERE DMND_ID_DMND = {bloquee};
    """);
// Les declencheurs de date de validation de la V2 (DEMANDE.sql:189-233), recrees desactives par l'extrait : reactives (REC-007 CA02).
var declencheursDemande = Colonne0(await Lire(v2.Connexion, "SELECT name FROM sys.triggers WHERE parent_id = OBJECT_ID('dbo.DEMANDE') AND name IN ('T_DEMANDE_VALIDATION', 'T_DEMANDE_VALIDATION_INS')"));
foreach (var t in declencheursDemande) await Executer(v2.Connexion, $"ENABLE TRIGGER [{t}] ON dbo.DEMANDE;");
Exiger(declencheursDemande.Count == 2, $"prealable : les deux declencheurs de date de validation de la V2 sont recrees et actifs ({string.Join(", ", declencheursDemande)})");

await NumerotationV2Check.Sqlcmd.JouerAsync(v2.Connexion, scriptV2, new Dictionary<string, string>
{
    ["BaseV2"] = v2.Nom, ["RetentionJours"] = "7", ["Tables"] = string.Join(",", reglesRecopie.Select(r => r.TableV2)),
});
await using var v3 = await BaseJetable.CreerAsync("RecopieRetourV3");
await Migrateur.AppliquerAsync(v3.Connexion, migrations);
chrono.Restart();
var transfert = await MoteurTransfert.TransfererAsync(new OptionsTransfert(v2.Connexion, v3.Connexion)
    { Regles = reglesServeur.Regles, Reprises = reglesServeur.Reprises, TailleLot = 5000 });
Exiger(transfert.Etat == "fini", $"REC-001 prealable : la base transferee en {chrono.Elapsed.TotalSeconds:0} s ({transfert.Etat}) {transfert.Motif}");
if (transfert.Etat != "fini") return await Fin();
var mes = await PontRecopie.MettreEnServiceAsync(v3.Connexion, v2.Nom);
Exiger(mes.Faite && mes.Declencheurs.Contains("v2.TR_AFFAIRE_GardeAffaires") && mes.Declencheurs.Contains("v2.TR_DEMANDE_GardeAffaires")
       && !mes.Declencheurs.Any(d => d.Contains("AFFAIRE_FileRecopie") || d.Contains("DEMANDE_FileRecopie")),
    $"REC-010 T01 : la mise en service pose sur v2.AFFAIRE et v2.DEMANDE la garde des anciennes affaires, et aucune file vers la V2 ({string.Join(", ", mes.Declencheurs.Where(d => d.Contains("Garde")))}) {string.Join(" ; ", mes.Refus)}");
if (!mes.Faite) return await Fin();
// Les identifiants que la V2 donne desormais s'ecartent de ceux de la V3, comme apres des mois ou chacun cree ses lignes : un identifiant
// de la V3 ecrit tel quel dans la V2 y viserait une autre ligne, et les controles le verraient (au 4e lancement, les deux suites
// coincidaient : la demande 7054009 avait ce numero dans les deux bases).
foreach (var (t, k) in new[] { ("AFFAIRE", "AFFRE_ID_AFFRE"), ("DEMANDE", "DMND_ID_DMND"), ("ARRIVEE", "ARRV_ID_ARRV") })
    await Executer(v2.Connexion, $"DECLARE @m int = (SELECT MAX({k}) FROM dbo.{t}) + 100000; DBCC CHECKIDENT ('dbo.{t}', RESEED, @m) WITH NO_INFOMSGS;");
async Task<string?> V3De(string table, object idV2) => await Texte(v3.Connexion, $"SELECT IdV3 FROM rec.Correspondance WHERE BaseV2 = '{v2.Nom}' AND TableV2 = '{table}' AND IdV2 = '{idV2}'");
async Task<string?> V2De(string tableV3, object idV3) => await Texte(v3.Connexion, $"SELECT TOP (1) IdV2 FROM rec.Correspondance WHERE BaseV2 = '{v2.Nom}' AND TableV3 = '{tableV3}' AND IdV3 = '{idV3}'");
async Task<BilanPassage> Passer() => await ServiceRecopie.PasserAsync(v3.Connexion, v2.Nom);

// ====================================================================================================
// A. REC-009 -- les anciens logiciels
// ====================================================================================================
await using (var cn = new SqlConnection(v3.Connexion))
{
    await cn.OpenAsync();
    var trouves = 0;
    var detail = new List<string>();
    foreach (var a in anciens)
    {
        var r = await AnciensNumeros.ChercherAsync(cn, v2.Nom, a.Logiciel == "GAGI" ? a.Ancien : a.Ancien, null, default);
        var attendue = a.Table == "DOSSIER_RCS" ? "rec.DossierRcs" : "rec.Inscription";
        if (r.Length == 1 && r[0].TableRig == a.Table && r[0].IdRig == a.Id && r[0].TableV3 == attendue && r[0].IdV3 == await V3De(a.Table, a.Id) && r[0].Logiciel == a.Logiciel) trouves++;
        else detail.Add($"{a.Ancien} -> {string.Join(" ; ", r.Select(x => $"{x.TableRig} {x.IdRig} {x.TableV3} {x.IdV3}"))}");
    }
    Exiger(anciens.Count == 20 && trouves == 20,
        $"REC-009 CA01 (T06) : {trouves} fiches RIG trouvees sur {anciens.Count} anciens numeros cherches (Dedale/Alinea par la migration, Oracle, GAGI), chacune avec sa fiche de la V3 " +
        $"(dossier RCS, inscription) {string.Join(" | ", detail)}");
    var bons = 0;
    foreach (var a in anciens)
    {
        var r = await AnciensNumeros.ChercherAsync(cn, v2.Nom, null, a.NumeroRig, default);
        if (r.Any(x => x.AncienNumero == (a.Logiciel == "GAGI" ? $"GAGI-{a.Ancien}" : a.Ancien) && x.NumeroInfogreffe == a.Infogreffe && x.NumeroRig == a.NumeroRig)) bons++;
    }
    Exiger(bons == 20, $"REC-009 CA02 (T07) : {bons} numeros RIG sur 20 donnent l'ancien numero et le numero Infogreffe");

    var listees = await AnciensNumeros.AnciennesReprisesAsync(cn, v2.Nom, default);
    var listeesTexte = listees.Select(f => $"{f.TableV2} {f.IdV2}").Order().ToList();
    var clientsV3 = await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.Client WHERE Nom LIKE 'Fake data%' OR Nom = 'SCP MARTIN'");
    Exiger(remplissageV2.Count >= 5 && listeesTexte.SequenceEqual(remplissageV2) && listees.All(f => f.IdV3 is not null) && clientsV3 == 3
           && listees.Any(f => f.Motif.Contains("Fake data")) && listees.Any(f => f.Motif.Contains("adresse inconnue")),
        $"REC-009 CA03 (T08) : {remplissageV2.Count} fiches de remplissage dans la V2 avant le transfert, {listees.Length} listees apres, les memes, toutes dans la V3 " +
        $"(les 3 clients transferes tels quels, nom compris) : {string.Join(", ", listees.Select(f => $"{f.TableV2} {f.IdV2} -> {f.TableV3} {f.IdV3} ({f.Motif})"))}");
}
var rapportTables = (await Lire(v3.Connexion, $"SELECT TableV2, Lues, Ecrites FROM rec.TransfertTable WHERE IdTransfert = {transfert.IdTransfert}")).Rows.Cast<DataRow>()
    .ToDictionary(r => (string)r[0], r => (Lues: r[1] is DBNull ? -1 : Convert.ToInt64(r[1]), Ecrites: r[2] is DBNull ? -1 : Convert.ToInt64(r[2])), StringComparer.OrdinalIgnoreCase);
var repriseTables = new[] { "MIGRATION_REFERENCE", "MIGRATION_TABLE", "MIGRATION_CHAMP", "MIGRATION_ID" };
var comptesReprise = new List<string>();
var repriseOk = true;
foreach (var t in repriseTables)
{
    var avant = await Nombre(v2.Connexion, $"SELECT COUNT(*) FROM dbo.{t}");
    var apres = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM v2.{t}");
    var auRapport = rapportTables.TryGetValue(t, out var l) && l.Lues == avant && l.Ecrites == apres;
    repriseOk &= avant == apres && auRapport;
    comptesReprise.Add($"{t} {avant} -> {apres}{(auRapport ? " (au rapport)" : " (ABSENT DU RAPPORT)")}");
}
var ecritureMigration = await Refus(v3.Connexion, "UPDATE v2.MIGRATION_ID SET MGRID_ERR_DESCRIPTION = 'corrigee a la main' WHERE MGRID_ID_ANCIEN = 55");
Exiger(repriseOk && ecritureMigration?.Number == AnciensNumeros.ErreurLectureSeule
       && await Nombre(v3.Connexion, "SELECT COUNT(*) FROM v2.MIGRATION_ID WHERE MGRID_ERR_DESCRIPTION = 'conflit de cle'") == 1,
    $"REC-009 CA04 (T09) : les tables de reprise base a base reprises meme vides, leur nombre de lignes au rapport ({string.Join(", ", comptesReprise)}) ; " +
    $"en lecture seule dans la V3 (UPDATE refuse : {ecritureMigration?.Number} {ecritureMigration?.Message})");
var replayDiff = await Nombre(v3.Connexion, $"""
    SELECT COUNT(*) FROM (SELECT ARTCR_ID_ARTCR, ARTCR_CODE_ARTICLE_BASE_REPLAY, ARTCR_CODE_ARTICLE_CORRESP_RIG, ARTCR_DATE_DEBUT, ARTCR_DATE_FIN FROM [{v2.Nom}].dbo.ARTICLE_CORRESP_REPLAY
                          EXCEPT SELECT ARTCR_ID_ARTCR, ARTCR_CODE_ARTICLE_BASE_REPLAY, ARTCR_CODE_ARTICLE_CORRESP_RIG, ARTCR_DATE_DEBUT, ARTCR_DATE_FIN FROM v2.ARTICLE_CORRESP_REPLAY
                          UNION ALL
                          SELECT ARTCR_ID_ARTCR, ARTCR_CODE_ARTICLE_BASE_REPLAY, ARTCR_CODE_ARTICLE_CORRESP_RIG, ARTCR_DATE_DEBUT, ARTCR_DATE_FIN FROM v2.ARTICLE_CORRESP_REPLAY
                          EXCEPT SELECT ARTCR_ID_ARTCR, ARTCR_CODE_ARTICLE_BASE_REPLAY, ARTCR_CODE_ARTICLE_CORRESP_RIG, ARTCR_DATE_DEBUT, ARTCR_DATE_FIN FROM [{v2.Nom}].dbo.ARTICLE_CORRESP_REPLAY) AS x
    """);
var replayV2 = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.ARTICLE_CORRESP_REPLAY");
var replayFin = await Nombre(v3.Connexion, "SELECT COUNT(*) FROM v2.ARTICLE_CORRESP_REPLAY WHERE ARTCR_DATE_FIN IS NOT NULL");
var ecritureReplay = await Refus(v3.Connexion, "DELETE FROM v2.ARTICLE_CORRESP_REPLAY");
Exiger(replayV2 > 100 && replayDiff == 0 && ecritureReplay?.Number == AnciensNumeros.ErreurLectureSeule
       && await Nombre(v3.Connexion, "SELECT COUNT(*) FROM v2.ARTICLE_CORRESP_REPLAY") == replayV2,
    $"REC-009 CA05 (T10) : les {replayV2} correspondances d'articles Replay de RIG_DEV identiques dans la V3, dates de debut et de fin comprises ({replayFin} avec une fin), " +
    $"0 ligne differente ; en lecture seule (suppression refusee : {ecritureReplay?.Number})");

// ====================================================================================================
// B. REC-010 -- l'historique des affaires des deux logiciels
// ====================================================================================================
// Une entreprise de l'extrait qui a des affaires dans la V2, et une affaire nouvelle de la V3 sur son dossier.
var (siren, dossierSiren) = (await Lire(v3.Connexion, """
    SELECT TOP (1) e.Siren, s.Id FROM rec.Entreprise AS e JOIN rec.DossierRcs AS s ON s.IdEntreprise = e.Id
    JOIN v2.DEMANDE AS d ON d.DMND_ID_DSRCS = s.Id
    WHERE e.Siren IS NOT NULL AND e.Siren IN (SELECT Siren FROM rec.Entreprise GROUP BY Siren HAVING COUNT(*) = 1)
    GROUP BY e.Siren, s.Id ORDER BY COUNT(DISTINCT d.DMND_ID_AFFRE) DESC, s.Id
    """)).Rows.Cast<DataRow>().Select(r => (Convert.ToString(r[0], CultureInfo.InvariantCulture)!, Convert.ToInt64(r[1]))).First();
var modeleAffaire = (await Texte(v3.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), d.DMND_ID_AFFRE) FROM v2.DEMANDE AS d WHERE d.DMND_ID_DSRCS = {dossierSiren} ORDER BY d.DMND_ID_AFFRE"))!;
var modeleDemande = (await Texte(v3.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), DMND_ID_DMND) FROM v2.DEMANDE WHERE DMND_ID_DSRCS = {dossierSiren} ORDER BY DMND_ID_DMND"))!;
// Une demande de la V3 porte les identifiants de la V3 (type, agent : RegleRecopie.NumerosV2), lus par la correspondance de son modele.
string TypeV3(string colonne) => $"(SELECT TOP (1) TRY_CONVERT(int, IdV3) FROM rec.Correspondance WHERE TableV2 = 'CATEGORIE' AND IdV2 = CONVERT(nvarchar(200), [{colonne}]))";
string CompteV3(string colonne) => $"(SELECT TOP (1) TRY_CONVERT(int, IdV3) FROM rec.Correspondance WHERE TableV2 = 'UTILISATEUR' AND IdV2 = CONVERT(nvarchar(200), [{colonne}]))";
async Task<long> AffaireV3(string modele, string libelle) => await Dupliquer(v3.Connexion, "v2.AFFAIRE", "AFFRE_ID_AFFRE", modele, new()
{
    ["AFFRE_LIBELLE"] = $"'{libelle}'", ["AFFRE_DATE_CREATION"] = "DATEADD(minute, -5, GETDATE())",
    ["AFFRE_ID_UTLST_CREATION"] = CompteV3("AFFRE_ID_UTLST_CREATION"), ["AFFRE_ID_UTLST_EN_COURS"] = CompteV3("AFFRE_ID_UTLST_EN_COURS"),
});
var nDemande = 0;
async Task<long> DemandeV3(string modele, long affaire, string etat, string? validation, Dictionary<string, string>? plus = null)
{
    var r = new Dictionary<string, string>
    {
        ["DMND_ID_AFFRE"] = affaire.ToString(CultureInfo.InvariantCulture), ["DMND_NUM_DEMANDE"] = $"'V3{++nDemande:00000000}'", ["DMND_ETAT_DEMANDE"] = $"'{etat}'",
        ["DMND_DATE_VALIDATION"] = validation is null ? "NULL" : $"'{validation}'", ["DMND_ID_DMND_MERE"] = "NULL", ["DMND_ID_DMND_INHIBITRICE"] = "NULL",
        ["DMND_INHIBEE"] = "0", ["DMND_TEXTE_INHIBITEUR"] = "NULL", ["DMND_ID_ACTRC"] = "NULL", ["DMND_ID_INSCR"] = "NULL", ["DMND_ID_ADRBS"] = "NULL",
        ["DMND_ID_CLIENT_EXPEDITION"] = "NULL", ["DMND_ID_CTGR"] = TypeV3("DMND_ID_CTGR"), ["DMND_ID_UTLST"] = CompteV3("DMND_ID_UTLST"),
        ["DMND_DATE_CREATION"] = "DATEADD(minute, -4, GETDATE())", ["DMND_DATE_MODIFICATION"] = "DATEADD(minute, -4, GETDATE())",
    };
    foreach (var (k, v) in plus ?? []) r[k] = v;
    return await Dupliquer(v3.Connexion, "v2.DEMANDE", "DMND_ID_DMND", modele, r);
}
var affaireNeuve = await AffaireV3(modeleAffaire, "FORMALITE NOUVELLE V3");
var demandeNeuve = await DemandeV3(modeleDemande, affaireNeuve, "Q", null, new() { ["DMND_ID_DSRCS"] = dossierSiren.ToString(CultureInfo.InvariantCulture) });
await using (var cn = new SqlConnection(v3.Connexion))
{
    await cn.OpenAsync();
    var h = await HistoriqueAffaires.LireAsync(cn, v2.Nom, Greffe, "siren", siren, null, default);
    var anciennesV2 = await Nombre(v2.Connexion, $"SELECT COUNT(DISTINCT d.DMND_ID_AFFRE) FROM dbo.DEMANDE d JOIN dbo.DOSSIER_RCS s ON s.DSSRC_ID_DSRCS = d.DMND_ID_DSRCS " +
                                                 $"LEFT JOIN dbo.ENTREPRISE e ON e.ENTRP_ID_RPRTR = s.DSSRC_ID_RPRTR WHERE (s.DSSRC_SIREN = '{siren}' OR e.ENTRP_SIREN = '{siren}') AND d.DMND_ID_AFFRE IS NOT NULL");
    var neuve = h.Affaires.FirstOrDefault(a => a.Id == affaireNeuve);
    var anciennes = h.Affaires.Where(a => a.Logiciel == Origines.V2).ToList();
    Exiger(neuve is { Marque: Origines.MarqueNouvelle, Logiciel: Origines.V3 } && neuve.Demandes.Any(d => d.Id == demandeNeuve && d.EtatLibelle == "qualifiée")
           && anciennes.Count == anciennesV2 && anciennes.Count >= 1 && anciennes.All(a => a.Marque == Origines.MarqueAncienne && a.Demandes.Length > 0 && a.IdV2 is not null)
           && h.Anciennes == anciennesV2 && h.Nouvelles == 1,
        $"REC-010 CA01 (T05) : l'historique de l'entreprise {h.Titre} montre ses {h.Affaires.Length} affaires : {anciennes.Count} anciennes (la V2 en compte {anciennesV2}) " +
        $"marquees « {Origines.MarqueAncienne} », la nouvelle marquee « {neuve?.Marque} », chacune avec ses demandes et leur etat " +
        $"({string.Join(" ; ", h.Affaires.Take(4).Select(a => $"{a.Id} {a.Marque} {a.Etat} : {a.Demandes.Length} demande(s) {string.Join("/", a.Demandes.Select(d => d.EtatLibelle).Distinct())}"))})");
}

// CA02 : une demande d'une ancienne affaire validee dans la V2 (son declencheur ecrit la date), relue dans la V3 apres le passage suivant.
var aValider = (await Texte(v2.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), DMND_ID_DMND) FROM dbo.DEMANDE WHERE DMND_ETAT_DEMANDE <> 'V' AND DMND_ID_DSRCS NOT IN ({dossierBloque}) ORDER BY DMND_ID_DMND DESC"))!;
var chronoCa02 = Stopwatch.StartNew();
await Executer(v2.Connexion, $"UPDATE dbo.DEMANDE SET DMND_ETAT_DEMANDE = 'V', DMND_DATE_VALIDATION = NULL WHERE DMND_ID_DMND = {aValider}");
var dateV2 = (DateTime)(await Valeur(v2.Connexion, $"SELECT DMND_DATE_VALIDATION FROM dbo.DEMANDE WHERE DMND_ID_DMND = {aValider}"))!;
var passageCa02 = await Passer();
await using (var cn = new SqlConnection(v3.Connexion))
{
    await cn.OpenAsync();
    var d = await HistoriqueAffaires.DemandeAsync(cn, v2.Nom, int.Parse((await V3De("DEMANDE", aValider))!), default);
    Exiger(d is { Etat: "V", Logiciel: Origines.V2 } && d.ValideeLe == dateV2 && chronoCa02.Elapsed < TimeSpan.FromMinutes(1) && passageCa02.Erreur is null,
        $"REC-010 CA02 (T06) : la demande {aValider} validee dans la V2 le {dateV2:dd/MM/yyyy HH:mm:ss.fff} (date posee par son declencheur) se relit « {d?.EtatLibelle} » " +
        $"le {d?.ValideeLe:dd/MM/yyyy HH:mm:ss.fff} dans la V3, {chronoCa02.Elapsed.TotalSeconds:0.0} s apres (un passage de la recopie : {passageCa02.Remontees} remontee(s)) {passageCa02.Erreur}");
}

// CA03 : modifier une ancienne affaire, ou y ajouter une demande, depuis la V3 : refuse, rien n'est ecrit.
var ancienneAffaire = (await Texte(v3.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), DMND_ID_AFFRE) FROM v2.DEMANDE WHERE DMND_ID_DSRCS = {dossierSiren} AND DMND_ID_AFFRE <> {affaireNeuve} ORDER BY 1"))!;
var libelleAvant = await Texte(v3.Connexion, $"SELECT AFFRE_LIBELLE FROM v2.AFFAIRE WHERE AFFRE_ID_AFFRE = {ancienneAffaire}");
var demandesAvant = await Nombre(v3.Connexion, "SELECT COUNT(*) FROM v2.DEMANDE");
var modifier = await Refus(v3.Connexion, $"UPDATE v2.AFFAIRE SET AFFRE_LIBELLE = 'MODIFIEE DANS LA V3' WHERE AFFRE_ID_AFFRE = {ancienneAffaire}");
SqlException? ajouter = null;
try { await DemandeV3(modeleDemande, long.Parse(ancienneAffaire), "Q", null); } catch (SqlException e) { ajouter = e; }
var modifierDemande = await Refus(v3.Connexion, $"UPDATE v2.DEMANDE SET DMND_LIBELLE = 'X' WHERE DMND_ID_AFFRE = {ancienneAffaire}");
var jumeau = await Refus(v3.Connexion, $"UPDATE v2.AFFAIRE SET AFFRE_LIBELLE = 'FORMALITE NOUVELLE V3 (MODIFIEE)' WHERE AFFRE_ID_AFFRE = {affaireNeuve}");
Exiger(modifier?.Number == GardeAffaires.ErreurAncienneAffaire && modifier.Message.StartsWith(GardeAffaires.MessageAncienne, StringComparison.Ordinal)
       && ajouter?.Number == GardeAffaires.ErreurAncienneAffaire && modifierDemande?.Number == GardeAffaires.ErreurAncienneAffaire
       && await Texte(v3.Connexion, $"SELECT AFFRE_LIBELLE FROM v2.AFFAIRE WHERE AFFRE_ID_AFFRE = {ancienneAffaire}") == libelleAvant
       && await Nombre(v3.Connexion, "SELECT COUNT(*) FROM v2.DEMANDE") == demandesAvant && jumeau is null,
    $"REC-010 CA03 (T07) : modifier l'ancienne affaire {ancienneAffaire}, y ajouter une demande ou en modifier une : refuse « {modifier?.Message} » ; rien d'ecrit " +
    $"(libelle et nombre de demandes inchanges) ; jumeau : la nouvelle affaire de la V3 se modifie ({(jumeau is null ? "acceptee" : jumeau.Message)})");

// CA04 : les echanges d'une ancienne affaire, lus dans la base des echanges (EDI_GREFFE) par code greffe et identifiant V2.
await using var edi = await BaseJetable.CreerAsync("RecopieRetourEdi");
// Les tables de la base des echanges, colonnes lues sur EDI_GREFFE_2025 de SQL-DEV le 2026-11-21 (celles que l'historique lit) ; les
// tables d'etats et leurs libelles, lus sur la meme base le 2026-11-22 (FLUX_SORTANT_ETAT : 0, 1, 2, 6, 7 ; FLUX_ENTRANT_ETAT : 0, 1, 2, 7).
await Executer(edi.Connexion, """
    CREATE TABLE dbo.FLUX_SORTANT (FLUSOR_ID_FLUSOR int IDENTITY PRIMARY KEY, FLUSOR_CODE_EDI varchar(20) NOT NULL, FLUSOR_CODE_GREFFE char(4) NOT NULL,
        FLUSOR_ETAT int NOT NULL, FLUSOR_RIG_ID int NULL, FLUSOR_REFERENCE varchar(1024) NULL, FLUSOR_DATE_CREATION datetime NOT NULL, FLUSOR_ERREUR varchar(max) NULL);
    CREATE TABLE dbo.FLUX_ENTRANT (FLUENT_ID_FLUENT int IDENTITY PRIMARY KEY, FLUENT_CODE_EDI varchar(20) NOT NULL, FLUENT_CODE_GREFFE char(4) NOT NULL,
        FLUENT_ETAT int NOT NULL, FLUENT_RIG_ID int NULL, FLUENT_REFERENCE varchar(1024) NULL, FLUENT_DATE_CREATION datetime NOT NULL, FLUENT_ERREUR varchar(max) NULL);
    CREATE TABLE dbo.FLUX_SORTANT_ETAT (FLSOET_ID_FLSOET int NOT NULL PRIMARY KEY, FLSOET_LIBELLE varchar(50) NOT NULL, FLSOET_DESCRIPTION varchar(max) NOT NULL);
    CREATE TABLE dbo.FLUX_ENTRANT_ETAT (FLENET_ID_FLENET int NOT NULL PRIMARY KEY, FLENET_LIBELLE varchar(50) NOT NULL, FLENET_DESCRIPTION varchar(max) NOT NULL);
    INSERT INTO dbo.FLUX_SORTANT_ETAT VALUES (0, 'Annulé', ''), (1, 'Extrait avec succès', ''), (2, 'Intégré avec succès', ''), (6, 'Erreur d''extraction', ''),
        (7, 'Erreur d''intégration', '');
    INSERT INTO dbo.FLUX_ENTRANT_ETAT VALUES (0, 'Annulé', ''), (1, 'Extrait avec succès', ''), (2, 'Intégré avec succès', ''), (7, 'Erreur d''intégration', '');
    """);
var demandeEchange = (await Texte(v3.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), d.DMND_ID_DMND) FROM v2.DEMANDE AS d WHERE d.DMND_ID_AFFRE = {ancienneAffaire} ORDER BY 1"))!;
var demandeEchangeV2 = (await V2De("v2.DEMANDE", demandeEchange))!;
// Quatre flux de la demande : un envoi en echec d'integration (7) avec son message, un envoi en echec d'extraction (6, que seule la table
// des etats SORTANTS a : une jointure croisee sur FLUX_ENTRANT_ETAT le rendrait sans libelle), une reception integree (2), une reception
// dans un etat que la base ne decrit pas (5 : repli sur le numero).
await Executer(edi.Connexion, $"""
    INSERT INTO dbo.FLUX_SORTANT (FLUSOR_CODE_EDI, FLUSOR_CODE_GREFFE, FLUSOR_ETAT, FLUSOR_RIG_ID, FLUSOR_REFERENCE, FLUSOR_DATE_CREATION, FLUSOR_ERREUR)
        VALUES ('INPI_FORMALITE', '{Greffe}', 7, {demandeEchangeV2}, 'envoi banc', '2026-11-02 10:00', 'Pièce jointe absente'),
               ('INPI_FORMALITE', '{Greffe}', 6, {demandeEchangeV2}, 'extraction banc', '2026-11-02 11:00', 'Demande illisible'),
               ('INPI_FORMALITE', '5601', 7, {demandeEchangeV2}, 'autre greffe', '2026-11-02 10:00', NULL);
    INSERT INTO dbo.FLUX_ENTRANT (FLUENT_CODE_EDI, FLUENT_CODE_GREFFE, FLUENT_ETAT, FLUENT_RIG_ID, FLUENT_REFERENCE, FLUENT_DATE_CREATION, FLUENT_ERREUR)
        VALUES ('INPI_RETOUR', '{Greffe}', 2, {demandeEchangeV2}, 'accuse banc', '2026-11-03 09:00', NULL),
               ('INPI_RETOUR', '{Greffe}', 5, {demandeEchangeV2}, 'etat inconnu', '2026-11-03 10:00', NULL),
               ('INPI_RETOUR', '{Greffe}', 2, 999999999, 'autre demande', '2026-11-03 09:00', NULL);
    """);
await using (var cn = new SqlConnection(v3.Connexion))
{
    await cn.OpenAsync();
    var h = await HistoriqueAffaires.LireAsync(cn, v2.Nom, Greffe, "siren", siren, edi.Connexion, default);
    var a = h.Affaires.Single(x => x.Id == int.Parse(ancienneAffaire));
    var sansBase = await HistoriqueAffaires.LireAsync(cn, v2.Nom, Greffe, "siren", siren, null, default);
    var attendus = await Nombre(edi.Connexion, $"SELECT (SELECT COUNT(*) FROM dbo.FLUX_SORTANT WHERE FLUSOR_CODE_GREFFE = '{Greffe}' AND FLUSOR_RIG_ID = {demandeEchangeV2}) " +
                                                $"+ (SELECT COUNT(*) FROM dbo.FLUX_ENTRANT WHERE FLUENT_CODE_GREFFE = '{Greffe}' AND FLUENT_RIG_ID = {demandeEchangeV2})");
    // fix-ok: l'etat des echanges sortait en numero (« 7 ») faute de joindre FLUX_SORTANT_ETAT / FLUX_ENTRANT_ETAT ; ce controle
    // mesure rouge avant la jointure (« etat 7 « 7 » »), vert apres (22/11/2026).
    // L'etat se lit en clair (le libelle de la base des echanges), un echec est marque avec son message : un agent qui lit « 7 » ne
    // voit pas que l'envoi a echoue (refus du chef du 22/11/2026 sur la premiere version de l'ecran).
    EchangeHistorique? Flux(string reference) => a.Echanges.SingleOrDefault(e => e.Reference == reference);
    Exiger(a.Echanges.Length == attendus && attendus == 4
           && Flux("envoi banc") is { Sens: "envoi", Etat: 7, EtatLibelle: "Erreur d'intégration", EnErreur: true, Erreur: "Pièce jointe absente" }
           && Flux("extraction banc") is { Sens: "envoi", Etat: 6, EtatLibelle: "Erreur d'extraction", EnErreur: true }
           && Flux("accuse banc") is { Sens: "réception", Etat: 2, EtatLibelle: "Intégré avec succès", EnErreur: false }
           && Flux("etat inconnu") is { Sens: "réception", Etat: 5, EtatLibelle: "5", EnErreur: false }
           && a.Echanges.All(e => e.Reference is not ("autre greffe" or "autre demande")) && a.EchangesNote is null
           && sansBase.Affaires.Single(x => x.Id == a.Id).EchangesNote?.Contains("Recopie:BaseEchanges") == true,
        $"REC-010 CA04 (T08) : l'ancienne affaire {a.Id} liste ses {a.Echanges.Length} echanges de la base des echanges (la base en a {attendus} pour ce greffe et la demande " +
        $"{demandeEchangeV2} de la V2) avec leur etat en clair : {string.Join(" ; ", a.Echanges.Select(e => $"{e.Sens} {e.CodeEdi} etat {e.Etat} « {e.EtatLibelle} »" +
        $"{(e.EnErreur ? $" EN ERREUR ({e.Erreur})" : "")} ({e.Demande})"))} ; 7 = « Erreur d'intégration » et 6 = « Erreur d'extraction » marques en erreur, un etat sans " +
        "libelle rendu par son numero ; ni l'autre greffe ni l'autre demande ; sans base des echanges reglee, l'historique le dit");
}

// CA05 : le client d'une ancienne affaire est la fiche de la V3 (la V3 fait foi), relue apres une modification dans la V3.
var clientAncienne = (await Texte(v3.Connexion, $"SELECT CONVERT(varchar(20), AFFRE_ID_CLIEN) FROM v2.AFFAIRE WHERE AFFRE_ID_AFFRE = {ancienneAffaire}"))!;
await Executer(v3.Connexion, $"UPDATE rec.Client SET Nom = 'NOUVEAU NOM DU CLIENT V3' WHERE Id = {clientAncienne}");
await using (var cn = new SqlConnection(v3.Connexion))
{
    await cn.OpenAsync();
    var h = await HistoriqueAffaires.LireAsync(cn, v2.Nom, Greffe, "siren", siren, null, default);
    var a = h.Affaires.Single(x => x.Id == int.Parse(ancienneAffaire));
    var parClient = await HistoriqueAffaires.LireAsync(cn, v2.Nom, Greffe, "client", (await Texte(v3.Connexion, $"SELECT Abrege FROM rec.Client WHERE Id = {clientAncienne}"))!, null, default);
    Exiger(a.Client?.StartsWith("NOUVEAU NOM DU CLIENT V3", StringComparison.Ordinal) == true && parClient.Affaires.Any(x => x.Id == a.Id),
        $"REC-010 CA05 (T09) : le client de l'ancienne affaire {a.Id}, modifie dans la V3, s'affiche « {a.Client} » ({a.ClientCode}) -- la fiche de la V3, pas une copie ; " +
        $"l'historique du client {parClient.Titre} la montre aussi");
}

// ====================================================================================================
// C. REC-011 -- les liens entre demandes des deux logiciels
// ====================================================================================================
var bloquanteV3 = (await V3De("DEMANDE", bloquante))!;
var affaireBloquee = (await Texte(v3.Connexion, $"SELECT CONVERT(varchar(20), DMND_ID_AFFRE) FROM v2.DEMANDE WHERE DMND_ID_DMND = {bloquanteV3}"))!;
var affaireC = await AffaireV3(affaireBloquee, "DEMANDE SUR UN DOSSIER BLOQUE");
var demandeBloquee = await DemandeV3(bloquanteV3, affaireC, "Q", null, new() { ["DMND_ID_DSRCS"] = dossierBloque });
var demandeLibre = await DemandeV3(bloquanteV3, affaireC, "Q", null, new() { ["DMND_ID_DSRCS"] = dossierLibre });
await using (var cn = new SqlConnection(v3.Connexion))
{
    await cn.OpenAsync();
    var d = await HistoriqueAffaires.DemandeAsync(cn, v2.Nom, (int)demandeBloquee, default);
    var libre = await HistoriqueAffaires.DemandeAsync(cn, v2.Nom, (int)demandeLibre, default);
    var lien = d?.Liens.FirstOrDefault(l => l.Lien == "bloquée par");
    Exiger(d is { Bloquee: true } && lien is not null && lien.IdDemande == int.Parse(bloquanteV3) && lien.Logiciel == Origines.V2 && lien.EtatLibelle == "qualifiée"
           && d.TexteBlocage?.Contains("de l'ancien RIG") == true && libre is { Bloquee: false } && libre.Liens.Length == 0
           && await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.LienDemande WHERE Porteuse = 'V3' AND IdDemande = {demandeBloquee} AND IdVisee = {bloquanteV3} AND LeveLe IS NULL") == 1,
        $"REC-011 CA01 (T05) : la demande {demandeBloquee} creee dans la V3 sur le dossier {dossierBloque}, ou la demande {bloquante} de la V2 encore en cours bloque deja la {bloquee}, " +
        $"est bloquee par elle : « {d?.TexteBlocage} » ; l'agent voit {lien?.Lien} {lien?.Numero} ({lien?.Logiciel}, {lien?.EtatLibelle}) ; jumeau : sur le dossier {dossierLibre} sans blocage, " +
        $"la demande {demandeLibre} ne l'est pas");
}
var chronoCa02b = Stopwatch.StartNew();
await Executer(v2.Connexion, $"UPDATE dbo.DEMANDE SET DMND_ETAT_DEMANDE = 'V' WHERE DMND_ID_DMND = {bloquante}");
var passageLeve = await Passer();
await using (var cn = new SqlConnection(v3.Connexion))
{
    await cn.OpenAsync();
    var d = await HistoriqueAffaires.DemandeAsync(cn, v2.Nom, (int)demandeBloquee, default);
    var trace = d?.Liens.FirstOrDefault(l => l.Leve);
    Exiger(d is { Bloquee: false } && trace is { Logiciel: Origines.V2 } && chronoCa02b.Elapsed < TimeSpan.FromMinutes(1)
           && await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.LienDemande WHERE IdDemande = {demandeBloquee} AND LeveLe IS NOT NULL AND Motif LIKE '%validée%'") == 1,
        $"REC-011 CA02 (T06) : la demande {bloquante} validee dans la V2, la demande {demandeBloquee} de la V3 est debloquee {chronoCa02b.Elapsed.TotalSeconds:0.0} s apres, " +
        $"au passage qui la remonte ({passageLeve.Remontees} remontee(s)) ; la trace reste lisible ({trace?.Lien} {trace?.Numero}, levee le {trace?.LeveLe:HH:mm:ss}) {passageLeve.Erreur}");
}
// CA03 : une demande de la V3 nee d'une ancienne demande ; le lien s'affiche et l'ancienne s'ouvre en lecture.
var mereV3 = (await Texte(v3.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), d.DMND_ID_DMND) FROM v2.DEMANDE AS d WHERE d.DMND_ID_AFFRE = {ancienneAffaire} ORDER BY 1 DESC"))!;
var fille = await DemandeV3(bloquanteV3, affaireC, "Q", null, new() { ["DMND_ID_DMND_MERE"] = mereV3, ["DMND_ID_DSRCS"] = dossierLibre });
await using (var cn = new SqlConnection(v3.Connexion))
{
    await cn.OpenAsync();
    var d = await HistoriqueAffaires.DemandeAsync(cn, v2.Nom, (int)fille, default);
    var lien = d?.Liens.FirstOrDefault(l => l.Lien == "née de");
    var mere = lien is null ? null : await HistoriqueAffaires.DemandeAsync(cn, v2.Nom, lien.IdDemande, default);
    var ecrireMere = await Refus(v3.Connexion, $"UPDATE v2.DEMANDE SET DMND_LIBELLE = 'X' WHERE DMND_ID_DMND = {mereV3}");
    Exiger(lien is { Logiciel: Origines.V2 } && lien.IdDemande == int.Parse(mereV3) && mere is { Logiciel: Origines.V2 } && mere.Liens.Any(l => l.Lien == "a engendré" && l.IdDemande == fille)
           && ecrireMere?.Number == GardeAffaires.ErreurAncienneAffaire,
        $"REC-011 CA03 (T07) : la demande {fille} de la V3 montre « {lien?.Lien} {lien?.Numero} » ({lien?.Logiciel}) ; la demande d'origine s'ouvre en lecture (n° {mere?.Numero}, {mere?.EtatLibelle}, " +
        $"elle montre « a engendré » {fille}) et refuse l'ecriture ({ecrireMere?.Number})");
}
// CA04 (le lien pose ici, ecrit dans la V2 au retour, partie E) : une ancienne demande bloquee par une demande de la V3, une autre nee d'elle.
// Deux anciennes demandes (avec une correspondance V2), autres que celles des controles precedents : une ancienne affaire peut n'en
// avoir qu'une (mesure au 3e lancement du banc : la 5495749 n'en a qu'une, la requete recevait un identifiant vide).
var deuxAnciennes = Colonne0(await Lire(v3.Connexion, $"SELECT TOP (2) CONVERT(varchar(20), d.DMND_ID_DMND) FROM v2.DEMANDE AS d JOIN rec.Correspondance AS c " +
    $"ON c.TableV3 = 'v2.DEMANDE' AND c.IdV3 = CONVERT(nvarchar(200), d.DMND_ID_DMND) WHERE d.DMND_ID_DMND NOT IN ({mereV3}, {bloquanteV3}, {await V3De("DEMANDE", bloquee)}) " +
    $"AND d.DMND_ID_DSRCS NOT IN ({dossierBloque}) ORDER BY d.DMND_ID_DMND DESC"));
var (ancienneBloqueeParV3, ancienneNeeDeV3) = (deuxAnciennes[0], deuxAnciennes[1]);
await using (var cn = new SqlConnection(v3.Connexion))
{
    await cn.OpenAsync();
    await HistoriqueAffaires.LierAncienneAsync(cn, null, v2.Nom, int.Parse(ancienneBloqueeParV3), "inhibitrice", (int)demandeLibre, "banc", default);
    await HistoriqueAffaires.LierAncienneAsync(cn, null, v2.Nom, int.Parse(ancienneNeeDeV3), "mere", (int)fille, "banc", default);
    InvalidOperationException? mauvaisSens = null;
    try { await HistoriqueAffaires.LierAncienneAsync(cn, null, v2.Nom, (int)fille, "mere", (int)demandeLibre, "banc", default); } catch (InvalidOperationException e) { mauvaisSens = e; }
    Exiger(mauvaisSens is not null, $"REC-011 T01 : le lien porte par une ancienne demande vers une demande de la V3 se pose, le sens inverse est refuse ({mauvaisSens?.Message})");
}

// ====================================================================================================
// D. REC-018 -- l'arrivee enregistree une fois
// ====================================================================================================
var modeleArrivee = (await Texte(v3.Connexion, "SELECT TOP (1) CONVERT(varchar(20), ARRV_ID_ARRV) FROM v2.ARRIVEE ORDER BY ARRV_ID_ARRV"))!;
var arriveeV3 = await Dupliquer(v3.Connexion, "v2.ARRIVEE", "ARRV_ID_ARRV", modeleArrivee, new()
    { ["ARRV_NUM_ARRIVEE"] = "'2026V300042'", ["ARRV_MODE_ARRIVEE"] = "'C'", ["ARRV_DATEHEURE"] = "'2026-11-21 08:47:13'", ["ARRV_NOM"] = "'PLI MIXTE DU BANC'" });
var passageArrivee = await Passer();
var arriveeV2 = await Lire(v2.Connexion, "SELECT ARRV_ID_ARRV, ARRV_MODE_ARRIVEE, ARRV_DATEHEURE, ARRV_NOM FROM dbo.ARRIVEE WHERE ARRV_NUM_ARRIVEE = '2026V300042'");
Exiger(arriveeV2.Rows.Count == 1 && (string)arriveeV2.Rows[0][1] == "C" && (DateTime)arriveeV2.Rows[0][2] == new DateTime(2026, 11, 21, 8, 47, 13)
       && (string)arriveeV2.Rows[0][3] == "PLI MIXTE DU BANC" && await V2De("v2.ARRIVEE", arriveeV3) == Convert.ToString(arriveeV2.Rows[0][0], CultureInfo.InvariantCulture),
    $"REC-018 CA01 (T05) : l'arrivee 2026V300042 enregistree dans la V3 est dans la V2 apres un passage ({passageArrivee.Appliquees} appliquee(s)), meme numero, mode, date et heure " +
    $"({(arriveeV2.Rows.Count == 1 ? $"V2 {arriveeV2.Rows[0][0]} : {arriveeV2.Rows[0][1]} {arriveeV2.Rows[0][2]:dd/MM/yyyy HH:mm:ss}" : "absente")}) {passageArrivee.Erreur}");
// CA02 : dans la V2, l'agent retrouve l'arrivee par son numero et y rattache une demande d'une ancienne affaire.
var ancienneAffaireV2 = (await V2De("v2.AFFAIRE", ancienneAffaire))!;
var modeleDemandeV2 = (await Texte(v2.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), DMND_ID_DMND) FROM dbo.DEMANDE WHERE DMND_ID_AFFRE = {ancienneAffaireV2} ORDER BY 1"))!;
var demandeV2Arrivee = await Dupliquer(v2.Connexion, "dbo.DEMANDE", "DMND_ID_DMND", modeleDemandeV2, new()
{
    ["DMND_ID_ARRV"] = "(SELECT ARRV_ID_ARRV FROM dbo.ARRIVEE WHERE ARRV_NUM_ARRIVEE = '2026V300042')", ["DMND_NUM_DEMANDE"] = "'V2ARRIVEE1'", ["DMND_ETAT_DEMANDE"] = "'A'",
    ["DMND_DATE_VALIDATION"] = "NULL", ["DMND_ID_DMND_MERE"] = "NULL", ["DMND_ID_DMND_INHIBITRICE"] = "NULL", ["DMND_INHIBEE"] = "0",
});
var demandeV3Arrivee = await DemandeV3(bloquanteV3, affaireC, "A", null, new() { ["DMND_ID_ARRV"] = arriveeV3.ToString(CultureInfo.InvariantCulture), ["DMND_ID_DSRCS"] = dossierLibre });
var passageCa02c = await Passer();
Exiger(await Nombre(v2.Connexion, $"SELECT COUNT(*) FROM dbo.DEMANDE d JOIN dbo.ARRIVEE a ON a.ARRV_ID_ARRV = d.DMND_ID_ARRV WHERE a.ARRV_NUM_ARRIVEE = '2026V300042' AND d.DMND_ID_DMND = {demandeV2Arrivee}") == 1
       && await V3De("DEMANDE", demandeV2Arrivee) is not null,
    $"REC-018 CA02 (T06) : dans la V2, la demande {demandeV2Arrivee} de l'ancienne affaire {ancienneAffaireV2} se rattache a l'arrivee retrouvee par son numero ; elle remonte dans la V3 " +
    $"({passageCa02c.Remontees} remontee(s)) {passageCa02c.Erreur}");
await using (var cn = new SqlConnection(v3.Connexion))
{
    await cn.OpenAsync();
    var a = await HistoriqueAffaires.ArriveeAsync(cn, v2.Nom, "2026V300042", default);
    Exiger(a is { DansAncienRig: 1 } && a.Demandes.Length == 2 && a.Demandes.Any(d => d.Id == demandeV3Arrivee && d.Logiciel == Origines.V3)
           && a.Demandes.Any(d => d.IdV2 == demandeV2Arrivee.ToString(CultureInfo.InvariantCulture) && d.Logiciel == Origines.V2) && a.IdV2 is not null,
        $"REC-018 CA03 (T07) : l'arrivee {a?.Numero} ouverte dans la V3 liste ses {a?.Demandes.Length} demandes des deux logiciels, celle de l'ancien RIG marquee : " +
        string.Join(" ; ", a?.Demandes.Select(d => $"{d.Numero} ({d.Logiciel})") ?? []));
}

// ====================================================================================================
// E. REC-007 -- le retour a l'ancien RIG pendant la beta
// ====================================================================================================
// La fiche du pont de la V2, telle que la bascule la pose (Base\v2\pont-v2.sql, lot 7 : ses tables lues ici dans le script meme),
// pour un greffe beta dont la beta finit dans 30 jours ; les corps d'origine des programmes gardes (NUM-022).
var textePont = await File.ReadAllTextAsync(pontV2);
var tablesPont = Regex.Matches(textePont, @"IF OBJECT_ID\(N'rigv3\.(Pont|CorpsOrigine|RenvoiFactureV3)', N'U'\) IS NULL\s*CREATE TABLE[\s\S]*?\n\);").Select(m => m.Value).ToList();
Exiger(tablesPont.Count == 3, $"prealable : Base\\v2\\pont-v2.sql pose rigv3.Pont, rigv3.CorpsOrigine et rigv3.RenvoiFactureV3 ({tablesPont.Count} trouvees)");
await Executer(v2.Connexion, "IF SCHEMA_ID(N'rigv3') IS NULL EXEC (N'CREATE SCHEMA rigv3 AUTHORIZATION dbo');");
foreach (var t in tablesPont) await Executer(v2.Connexion, t);
await Executer(v2.Connexion, $"""
    INSERT INTO rigv3.Pont (Id, CodeGreffe, BaseV3, BasculeLe, BasculePar, FinBeta) VALUES (1, '{Greffe}', '{v3.Nom}', DATEADD(day, -20, SYSDATETIME()), 'banc', DATEADD(day, 30, SYSDATETIME()));
    INSERT INTO rigv3.CorpsOrigine (Objet, Definition, QuotedIdentifier, AnsiNulls, GardeLe)
        VALUES (N'GENERER_NUMERO', N'CREATE PROCEDURE ...', 0, 1, SYSDATETIME()), (N'CHERCHER_NUMERO', N'CREATE PROCEDURE ...', 0, 1, SYSDATETIME()),
               (N'RESTITUER_NUMERO', N'CREATE PROCEDURE ...', 0, 1, SYSDATETIME());
    """);
// La facturation de la V3 (CA04) : une facture signee dans les chaines de la V3 (SOC-018), et une ligne d'article utilise rattachee a une
// affaire de la V3 -- la table ARTICLE_UTILISE n'est pas encore reprise par la V3 (story Facturation) : le banc la pose, reduite a sa cle et
// a son lien vers l'affaire (ARTUT_ID_AFFRE, sys.foreign_keys de RIG_DEV lu le 2026-11-21), des deux cotes.
await Executer(v2.Connexion, "CREATE TABLE dbo.ARTICLE_UTILISE (ARTUT_ID_ARTUT int IDENTITY PRIMARY KEY, ARTUT_ID_AFFRE int NULL CONSTRAINT FK_ARTICLE_UTILISE_AFFAIRE REFERENCES dbo.AFFAIRE (AFFRE_ID_AFFRE));");
await Executer(v3.Connexion, "CREATE TABLE v2.ARTICLE_UTILISE (ARTUT_ID_ARTUT int IDENTITY PRIMARY KEY, ARTUT_ID_AFFRE int NULL);");
var chaines = new ServiceChaines(new CoffreMemoireBanc());
await using (var cn = new SqlConnection(v3.Connexion))
{
    await cn.OpenAsync();
    await chaines.OuvrirAsync(cn, Greffe, "Romance Standard Time", "banc", null);
    await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
    await chaines.SignerFactureAsync(cn, tx, new PieceFacture("FACV3-2026-000001", "FACTURE", new DateTime(2026, 11, 20, 14, 0, 0), 120m, "CLIENT DU BANC", "43000", null,
                                                              [new LigneTva(20m, 120m)]));
    await tx.CommitAsync();
}

// Un mois d'affaires de la V3 (CA07, T05) : 120 affaires de deux demandes, l'une validee a une date du mois passe (CA02, T06), l'autre en cours.
var modeles = (await Lire(v3.Connexion, $"""
    SELECT TOP (120) CONVERT(varchar(20), d.DMND_ID_AFFRE), CONVERT(varchar(20), MIN(d.DMND_ID_DMND)) FROM v2.DEMANDE AS d
    JOIN rec.Correspondance AS c ON c.TableV3 = 'v2.DEMANDE' AND c.IdV3 = CONVERT(nvarchar(200), d.DMND_ID_DMND)
    WHERE d.DMND_ID_DSRCS IS NOT NULL AND d.DMND_ID_DSRCS NOT IN ({dossierBloque}) AND d.DMND_ID_CTGR IS NOT NULL AND d.DMND_ID_AFFRE IS NOT NULL
      AND EXISTS (SELECT 1 FROM rec.Correspondance AS k WHERE k.TableV2 = 'CATEGORIE' AND k.IdV2 = CONVERT(nvarchar(200), d.DMND_ID_CTGR))
    GROUP BY d.DMND_ID_AFFRE ORDER BY d.DMND_ID_AFFRE DESC
    """)).Rows.Cast<DataRow>().Select(r => ((string)r[0], (string)r[1])).ToList();
var mois = new List<(long Affaire, long Validee, string Date, long EnCours)>();
for (var i = 0; i < 120; i++)
{
    var (ma, md) = modeles[i % modeles.Count];
    var a = await AffaireV3(ma, $"AFFAIRE DU MOIS {i:000}");
    var date = new DateTime(2026, 10, 1, 9, 0, 0).AddDays(i % 30).AddMinutes(i * 7).AddMilliseconds(i * 10 % 997).ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
    var dv = await DemandeV3(md, a, "V", date);
    var de = await DemandeV3(md, a, "Q", null);
    mois.Add((a, dv, date, de));
}
// Une demande nee d'une demande de la V3 creee APRES elle (le lien attend la fin du retour).
var filleAvant = await DemandeV3(modeles[0].Item2, mois[0].Affaire, "Q", null);
var mereApres = await DemandeV3(modeles[0].Item2, mois[0].Affaire, "Q", null);
await Executer(v3.Connexion, $"UPDATE v2.DEMANDE SET DMND_ID_DMND_MERE = {mereApres} WHERE DMND_ID_DMND = {filleAvant}");
await Executer(v3.Connexion, $"INSERT INTO v2.ARTICLE_UTILISE (ARTUT_ID_AFFRE) VALUES ({mois[1].Affaire});");

// REC-007 CA07, pendant la beta (jumeau du controle d'apres le retour) : le controle de nuit d'un greffe qui cree ses affaires dans la V3 n'alerte
// pas pour elles. Une affaire ou une demande de la V3 n'a pas de correspondance (REC-010 T02) et ses liens portent des identifiants de la V3
// (RegleRecopie.NumerosV2) : ni un ecart, ni un lien orphelin. Pris apres un passage, pour que les fiches de la V3 (arrivee) soient recopiees.
var passageBeta = await Passer();
var pendantBeta = await ComparaisonBases.ComparerAsync(new OptionsComparaison(v2.Connexion, v3.Connexion));
var lignesV3Beta = await Nombre(v3.Connexion, "SELECT COUNT(*) FROM v2.DEMANDE AS d WHERE NOT EXISTS (SELECT 1 FROM rec.Correspondance AS c WHERE c.TableV3 = 'v2.DEMANDE' AND c.IdV3 = CONVERT(nvarchar(200), d.DMND_ID_DMND))");
Exiger(pendantBeta.Etat == "fini" && pendantBeta.Orphelins == 0 && pendantBeta.ATraiter == 0 && pendantBeta.CodeSortie == 0 && lignesV3Beta >= 240 && passageBeta.Erreur is null,
    $"REC-007 CA07 pendant la beta : {lignesV3Beta} demandes de la V3 sans correspondance (le mois et les controles precedents) ; le controle de nuit " +
    $"({pendantBeta.Tables.Count} tables, code {pendantBeta.CodeSortie}) : {pendantBeta.LignesComparees} lignes comparees, {pendantBeta.Ecarts} ecart(s), " +
    $"{pendantBeta.SansCorrespondance} sans correspondance, {pendantBeta.Orphelins} lien(s) orphelin(s){ATraiterEnClair(pendantBeta)} {passageBeta.Erreur}");

await using (var cn = new SqlConnection(v3.Connexion))
{
    await cn.OpenAsync();
    // CA08 (T12) : la personne designee, puis un autre compte qui demande le retour : refuse et enregistre.
    await RetourBeta.DesignerAsync(cn, null, v2.Nom, Responsable, "banc", default);
}
var affairesV2Avant = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.AFFAIRE");
var autre = await RetourBeta.RevenirAsync(v3.Connexion, v2.Nom, Greffe, @"GREFFE\agent.ordinaire", "le module des actes ne marche plus");
var decisionAutre = await Lire(v3.Connexion, $"SELECT Auteur, Le, Motif, Etat FROM rec.DecisionRetour WHERE Id = {autre.IdDecision}");
Exiger(autre.Etat == "refusee" && decisionAutre.Rows.Count == 1 && (string)decisionAutre.Rows[0][0] == @"GREFFE\agent.ordinaire" && (string)decisionAutre.Rows[0][2] == "le module des actes ne marche plus"
       && await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.AFFAIRE") == affairesV2Avant && await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.EtatRetour") == 0,
    $"REC-007 CA08 (T12) : un autre compte que la personne designee ({Responsable}) demande le retour : refuse « {autre.Detail} » ; la demande est enregistree " +
    $"(auteur {decisionAutre.Rows[0][0]}, le {decisionAutre.Rows[0][1]:dd/MM/yyyy HH:mm}, motif « {decisionAutre.Rows[0][2]} ») ; rien d'ecrit dans la V2, la V3 reste ouverte");

// CA05 (T09) : le jour J des autres greffes passe (la fin de la beta), le retour est refuse.
await Executer(v2.Connexion, "UPDATE rigv3.Pont SET FinBeta = DATEADD(day, -1, SYSDATETIME()) WHERE Id = 1");
var apresJourJ = await RetourBeta.RevenirAsync(v3.Connexion, v2.Nom, Greffe, Responsable, "defaut bloquant apres le jour J");
await Executer(v2.Connexion, "UPDATE rigv3.Pont SET FinBeta = DATEADD(day, 30, SYSDATETIME()) WHERE Id = 1");
Exiger(apresJourJ.Etat == "refusee" && apresJourJ.Detail?.Contains("jour J") == true && await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.AFFAIRE") == affairesV2Avant,
    $"REC-007 CA05 (T09) : fin de beta (jour J des autres greffes) dans le passe : le retour est refuse, « {apresJourJ.Detail} »");

// CA06 (T10) : un type de demande cree dans la V3 seule, une demande de ce type : listee avant le retour, qui ne demarre pas sans regle.
// rec.TypeDemande a onze colonnes obligatoires (REC-012_fiches-partagees.sql) : le type de la V3 seule est la copie d'un type existant.
var typeV3 = await Dupliquer(v3.Connexion, "rec.TypeDemande", "Id", (await Texte(v3.Connexion, "SELECT TOP (1) CONVERT(varchar(20), Id) FROM rec.TypeDemande WHERE Niveau = 3 ORDER BY Id"))!,
    new() { ["Reference"] = "'V3SEULE'", ["Libelle"] = "'Type de la V3 seule (banc)'", ["LibelleEcran"] = "'V3 SEULE'" });
var demandeTypeV3 = await DemandeV3(modeles[2].Item2, mois[2].Affaire, "Q", null, new() { ["DMND_ID_CTGR"] = typeV3.ToString(CultureInfo.InvariantCulture) });
PreparationRetour prepType;
await using (var cn = new SqlConnection(v3.Connexion)) { await cn.OpenAsync(); prepType = await RetourBeta.PreparerAsync(cn, v2.Nom, Greffe, default); }
var sansRegle = await RetourBeta.RevenirAsync(v3.Connexion, v2.Nom, Greffe, Responsable, "test du type sans equivalent");
var typeV2Choisi = (await Texte(v2.Connexion, "SELECT TOP (1) CONVERT(varchar(20), CTGR_ID_CTGR) FROM dbo.CATEGORIE WHERE CTGR_NIVEAU = 3 ORDER BY CTGR_ID_CTGR"))!;
await using (var cn = new SqlConnection(v3.Connexion)) { await cn.OpenAsync(); await RetourBeta.ChoisirTypeAsync(cn, null, v2.Nom, (int)typeV3, int.Parse(typeV2Choisi), "banc", default); }
PreparationRetour prepApres;
await using (var cn = new SqlConnection(v3.Connexion)) { await cn.OpenAsync(); prepApres = await RetourBeta.PreparerAsync(cn, v2.Nom, Greffe, default); }
Exiger(prepType.TypesSansEquivalent.Any(t => t.IdTypeV3 == typeV3 && t.Demandes == 1 && t.IdTypeV2 is null) && sansRegle.Etat == "refusee" && sansRegle.Detail?.Contains("CA06") == true
       && prepApres.TypesSansEquivalent.Any(t => t.IdTypeV3 == typeV3 && t.IdTypeV2 == int.Parse(typeV2Choisi)) && !prepApres.Refus.Any(r => r.Contains("CA06")),
    $"REC-007 CA06 (T10) : la demande {demandeTypeV3} d'un type de la V3 seule est listee avant le retour ({string.Join(", ", prepType.TypesSansEquivalent.Select(t => $"{t.Libelle} : {t.Demandes}"))}), " +
    $"qui ne demarre pas (« {sansRegle.Detail?[..Math.Min(160, sansRegle.Detail.Length)]}... ») ; la regle choisie (type {typeV2Choisi} de l'ancien RIG), il n'est plus refuse pour elle");

// T03 : une ligne d'une table du registre (v2.INSTANCE, recopiee telle quelle) rattachee a une affaire de la V3 n'a pas de regle de retour : refus.
// Posee sous la marque de la remontee pour qu'elle n'aille pas dans la file vers la V2 (elle n'existe que pour la preparation).
var instanceModele = await Texte(v3.Connexion, "SELECT TOP (1) CONVERT(varchar(20), INSTN_ID_INSTN) FROM v2.INSTANCE ORDER BY 1");
PreparationRetour prepInstance = prepApres;
var instanceV3 = 0L;
if (instanceModele is not null)
{
    var colonnesInstance = Colonne0(await Lire(v3.Connexion, "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('v2.INSTANCE') AND is_identity = 0 AND is_computed = 0 ORDER BY column_id"));
    var identite = await Texte(v3.Connexion, "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('v2.INSTANCE') AND is_identity = 1");
    instanceV3 = await Nombre(v3.Connexion, $"EXEC sys.sp_set_session_context N'{PontRecopie.CleRemontee}', 1; DECLARE @i TABLE (Id bigint); " +
        $"INSERT INTO v2.INSTANCE ({string.Join(", ", colonnesInstance.Select(c => $"[{c}]"))}) OUTPUT CONVERT(bigint, inserted.[{identite ?? "INSTN_ID_INSTN"}]) INTO @i " +
        $"SELECT {string.Join(", ", colonnesInstance.Select(c => c.Equals("INSTN_ID_AFFRE", StringComparison.OrdinalIgnoreCase) ? mois[3].Affaire.ToString(CultureInfo.InvariantCulture) : $"[{c}]"))} " +
        $"FROM v2.INSTANCE WHERE INSTN_ID_INSTN = {instanceModele}; SELECT Id FROM @i;");
    await using (var cn = new SqlConnection(v3.Connexion)) { await cn.OpenAsync(); prepInstance = await RetourBeta.PreparerAsync(cn, v2.Nom, Greffe, default); }
    await Executer(v3.Connexion, $"EXEC sys.sp_set_session_context N'{PontRecopie.CleRemontee}', 1; DELETE FROM v2.INSTANCE WHERE INSTN_ID_INSTN = {instanceV3};");
}
Exiger(instanceModele is null || prepInstance.Dependantes.Any(d => d.TableV2 == "INSTANCE" && d.Sort == RetourBeta.SortSansRegle) && prepInstance.Refus.Any(r => r.StartsWith("INSTANCE", StringComparison.Ordinal)),
    $"REC-007 T03 : une instance rattachee a une affaire de la V3 (INSTANCE.INSTN_ID_AFFRE) n'a pas de regle de retour : le retour la liste et refuse " +
    $"({string.Join(" ; ", prepInstance.Dependantes.Select(d => $"{d.TableV2} {d.Lignes} : {d.Sort}"))})");

// Le retour, par la personne designee (CA08), chronometre (CA07), puis ses preuves.
var empreintesV2 = async () => (await Lire(v2.Connexion, """
    SELECT s.name + '.' + t.name, (SELECT SUM(p.rows) FROM sys.partitions AS p WHERE p.object_id = t.object_id AND p.index_id IN (0, 1)) FROM sys.tables AS t
    JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    """)).Rows.Cast<DataRow>().ToDictionary(r => (string)r[0], r => Convert.ToInt64(r[1]));
async Task<Dictionary<string, string>> Checksums(string chaine, IEnumerable<string> tables)
{
    var r = new Dictionary<string, string>();
    foreach (var t in tables) r[t] = await Texte(chaine, $"SELECT CONCAT(COUNT_BIG(*), '|', CHECKSUM_AGG(BINARY_CHECKSUM(*))) FROM {t}") ?? "";
    return r;
}
var tablesV2 = (await empreintesV2()).Keys.Where(t => t is not ("dbo.AFFAIRE" or "dbo.DEMANDE" or "rigv3.RenvoiFactureV3")).ToList();
var avantRetourV2 = await Checksums(v2.Connexion, tablesV2);
var chainesV3Avant = await Checksums(v3.Connexion, ["soc.FactureSignee", "soc.EncaissementSigne", "soc.GrandTotalFactures", "soc.EvenementTechnique"]);
var demandesV2Avant = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.DEMANDE");
PreparationRetour preparation;
await using (var cn = new SqlConnection(v3.Connexion)) { await cn.OpenAsync(); preparation = await RetourBeta.PreparerAsync(cn, v2.Nom, Greffe, default); }
Info($"avant le retour : {preparation.Affaires} affaires et {preparation.Demandes} demandes de la V3, refus : {string.Join(" ; ", preparation.Refus)}");
// T02 (jumeau, avant le retour) : l'etat que l'aiguillage lit dans la base V3 (ModuleAiguillage -> RetourBeta.RevenuDepuisAsync) est vide.
DateTime? revenuAvant;
await using (var cn = new SqlConnection(v3.Connexion)) { await cn.OpenAsync(); revenuAvant = await RetourBeta.RevenuDepuisAsync(cn, v2.Nom, default); }
chrono.Restart();
var retour = await RetourBeta.RevenirAsync(v3.Connexion, v2.Nom, Greffe, Responsable, "defaut bloquant du module des actes pendant la beta (banc)");
var dureeRetour = chrono.Elapsed;
var decision = await Lire(v3.Connexion, $"SELECT Auteur, Le, Motif, Etat, Affaires, Demandes FROM rec.DecisionRetour WHERE Id = {retour.IdDecision}");
Exiger(retour.Etat == "faite" && (string)decision.Rows[0][0] == Responsable && (string)decision.Rows[0][3] == "faite" && (string)decision.Rows[0][2] == retour.Motif
       && await Texte(v3.Connexion, "SELECT Etat FROM rec.EtatRetour") == "revenu en V2",
    $"REC-007 CA08 (T12) : la decision de la personne designee est enregistree (auteur {decision.Rows[0][0]}, le {decision.Rows[0][1]:dd/MM/yyyy HH:mm}, motif « {decision.Rows[0][2]} ») " +
    $"et le retour est fait : {retour.Detail}");
if (retour.Etat != "faite") return await Fin();

// CA01 (T05) : chaque affaire et demande de la V3 est dans la V2, ses numeros tels quels, ses liens traduits.
var comparees = 0;
var differences = new List<string>();
foreach (var (a, dv, _, de) in mois.Take(120))
{
    var aV2 = await V2De("v2.AFFAIRE", a);
    var ligneV3 = await Texte(v3.Connexion, $"SELECT CONCAT(a.AFFRE_LIBELLE, '|', CONVERT(varchar(30), a.AFFRE_DATE_CREATION, 121), '|', k.IdV2) FROM v2.AFFAIRE a " +
                                            $"LEFT JOIN rec.Correspondance k ON k.TableV2 = 'CLIENT' AND k.TableV3 = 'rec.Client' AND k.IdV3 = CONVERT(nvarchar(200), a.AFFRE_ID_CLIEN) WHERE a.AFFRE_ID_AFFRE = {a}");
    var ligneV2 = aV2 is null ? null : await Texte(v2.Connexion, $"SELECT CONCAT(AFFRE_LIBELLE, '|', CONVERT(varchar(30), AFFRE_DATE_CREATION, 121), '|', AFFRE_ID_CLIEN) FROM dbo.AFFAIRE WHERE AFFRE_ID_AFFRE = {aV2}");
    foreach (var d in new[] { dv, de })
    {
        var dV2 = await V2De("v2.DEMANDE", d);
        var numV3 = await Texte(v3.Connexion, $"SELECT CONCAT(DMND_NUM_DEMANDE, '|', DMND_NUM_INDICATIF, '|', DMND_LES_NUMEROS, '|', DMND_ETAT_DEMANDE) FROM v2.DEMANDE WHERE DMND_ID_DMND = {d}");
        var numV2 = dV2 is null ? null : await Texte(v2.Connexion, $"SELECT CONCAT(DMND_NUM_DEMANDE, '|', DMND_NUM_INDICATIF, '|', DMND_LES_NUMEROS, '|', DMND_ETAT_DEMANDE) FROM dbo.DEMANDE WHERE DMND_ID_DMND = {dV2}");
        var affaireDeLaDemande = dV2 is null ? null : await Texte(v2.Connexion, $"SELECT DMND_ID_AFFRE FROM dbo.DEMANDE WHERE DMND_ID_DMND = {dV2}");
        if (numV3 == numV2 && affaireDeLaDemande == aV2) comparees++; else differences.Add($"demande {d} : V3 {numV3} / V2 {numV2} (affaire {affaireDeLaDemande} pour {aV2})");
    }
    if (ligneV2 is null || ligneV2 != ligneV3) differences.Add($"affaire {a} : V3 {ligneV3} / V2 {ligneV2}");
}
var demandesV2Apres = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.DEMANDE");
var dossierTraduit = await Nombre(v3.Connexion, $"""
    SELECT COUNT(*) FROM v2.DEMANDE AS d3 JOIN rec.Correspondance AS k ON k.TableV3 = 'v2.DEMANDE' AND k.IdV3 = CONVERT(nvarchar(200), d3.DMND_ID_DMND) AND k.Origine = 'retour'
    JOIN [{v2.Nom}].dbo.DEMANDE AS d2 ON d2.DMND_ID_DMND = CONVERT(int, k.IdV2)
    JOIN rec.Correspondance AS s ON s.TableV2 = 'DOSSIER_RCS' AND s.TableV3 = 'rec.DossierRcs' AND s.IdV3 = CONVERT(nvarchar(200), d3.DMND_ID_DSRCS)
    WHERE CONVERT(nvarchar(200), d2.DMND_ID_DSRCS) = s.IdV2
    """);
Exiger(comparees == 240 && differences.Count == 0 && demandesV2Apres - demandesV2Avant == preparation.Demandes && retour.Affaires == preparation.Affaires && dossierTraduit >= 240,
    $"REC-007 CA01 (T05) : les {retour.Affaires} affaires et {retour.Demandes} demandes creees dans la V3 sont dans la V2 (+{demandesV2Apres - demandesV2Avant} demandes) ; " +
    $"240 demandes du mois comparees : numero, indicatif, numeros donnes et etat tels quels, chacune dans SON affaire ; client et dossier traduits par la correspondance " +
    $"({dossierTraduit} dossiers) {string.Join(" | ", differences.Take(5))}");
var lienDifferee = await Texte(v2.Connexion, $"SELECT CONVERT(varchar(20), DMND_ID_DMND_MERE) FROM dbo.DEMANDE WHERE DMND_ID_DMND = {await V2De("v2.DEMANDE", filleAvant)}");
var typeEcrit = await Texte(v2.Connexion, $"SELECT CONVERT(varchar(20), DMND_ID_CTGR) FROM dbo.DEMANDE WHERE DMND_ID_DMND = {await V2De("v2.DEMANDE", demandeTypeV3)}");
Exiger(lienDifferee == await V2De("v2.DEMANDE", mereApres) && typeEcrit == typeV2Choisi,
    $"REC-007 T02 : une demande nee d'une demande de la V3 creee apres elle garde son lien dans la V2 ({lienDifferee}) ; la demande du type de la V3 seule y va sous le type choisi ({typeEcrit})");

// CA02 (T06) : sur les 100 premieres demandes validees, la date de validation de la V2 est celle de la V3 ; contre-epreuve : le declencheur de la V2 est
// actif -- une demande validee inseree sans date y recoit la date du jour.
var datesEgales = 0;
foreach (var (_, dv, date, _) in mois.Take(100))
{
    var dV2 = await V2De("v2.DEMANDE", dv);
    var d2 = (DateTime?)await Valeur(v2.Connexion, $"SELECT DMND_DATE_VALIDATION FROM dbo.DEMANDE WHERE DMND_ID_DMND = {dV2}");
    if (d2 == DateTime.Parse(date, CultureInfo.InvariantCulture) && d2 == (DateTime?)await Valeur(v3.Connexion, $"SELECT DMND_DATE_VALIDATION FROM v2.DEMANDE WHERE DMND_ID_DMND = {dv}")) datesEgales++;
}
var temoin = await Dupliquer(v2.Connexion, "dbo.DEMANDE", "DMND_ID_DMND", modeleDemandeV2, new()
    { ["DMND_ETAT_DEMANDE"] = "'V'", ["DMND_DATE_VALIDATION"] = "NULL", ["DMND_NUM_DEMANDE"] = "'TEMOINCA02'", ["DMND_ID_DMND_MERE"] = "NULL", ["DMND_ID_DMND_INHIBITRICE"] = "NULL", ["DMND_INHIBEE"] = "0" });
var dateTemoin = (DateTime?)await Valeur(v2.Connexion, $"SELECT DMND_DATE_VALIDATION FROM dbo.DEMANDE WHERE DMND_ID_DMND = {temoin}");
await Executer(v2.Connexion, $"DELETE FROM dbo.DEMANDE WHERE DMND_ID_DMND = {temoin}");
Exiger(datesEgales == 100 && dateTemoin is not null && dateTemoin.Value.Date >= DateTime.Today.AddDays(-1),
    $"REC-007 CA02 (T06) : {datesEgales} demandes validees sur 100 ont dans la V2 la date de validation de la V3 (du mois passe), pas celle du retour ; " +
    $"contre-epreuve : une demande validee ecrite sans date recoit {dateTemoin:dd/MM/yyyy HH:mm} du declencheur de la V2");

// CA03 (T07) : apres le retour, la V3 refuse toute creation pour ce greffe et le dit ; l'affaire se cree dans la V2 et y reste (elle remonte en ancienne).
SqlException? creationV3 = null;
try { await AffaireV3(modeleAffaire, "APRES LE RETOUR"); } catch (SqlException e) { creationV3 = e; }
var affaireApresV2 = await Dupliquer(v2.Connexion, "dbo.AFFAIRE", "AFFRE_ID_AFFRE", ancienneAffaireV2, new() { ["AFFRE_LIBELLE"] = "'CREEE DANS L''ANCIEN RIG APRES LE RETOUR'" });
var passageApres = await Passer();
var remontee = await V3De("AFFAIRE", affaireApresV2);
Exiger(creationV3?.Number == GardeAffaires.ErreurGreffeRevenu && creationV3.Message.Contains("revenu à l'ancien RIG") && remontee is not null,
    $"REC-007 CA03 (T07) : apres le retour, une creation dans la V3 est refusee « {creationV3?.Message} » ; dans la V2, l'affaire {affaireApresV2} se cree et remonte dans la V3 " +
    $"en ancienne affaire ({remontee}) {passageApres.Erreur}");
// T02 : la creation d'affaires est rouverte dans la V2 -- l'aiguillage d'un greffe passe (passage du 01/08, atteint, qui ne change plus) lit l'etat
// pose par le retour et envoie la nouvelle affaire a la V2 ; avant le retour, la meme regle l'envoyait a la V3. La route elle-meme : SocleAiguillageCheck.
DateTime? revenuApres;
await using (var cn = new SqlConnection(v3.Connexion)) { await cn.OpenAsync(); revenuApres = await RetourBeta.RevenuDepuisAsync(cn, v2.Nom, default); }
var passageGreffe = new DateTime(2026, 8, 1, 8, 0, 0);
var aiguilleAvant = RegleAiguillage.Aiguiller("nouvelle", passageGreffe, null, DateTime.Now, revenuAvant);
var aiguilleApres = RegleAiguillage.Aiguiller("nouvelle", passageGreffe, null, DateTime.Now, revenuApres);
Exiger(revenuAvant is null && aiguilleAvant.Logiciel == Logiciels.V3 && revenuApres is not null && aiguilleApres.Logiciel == Logiciels.V2,
    $"REC-007 T02 : creation d'affaires refermee dans la V3 et rouverte dans la V2 -- l'aiguillage envoie une nouvelle affaire a la V2 apres le retour " +
    $"(« {aiguilleApres.Motif} ») ; avant le retour, a la V3 (« {aiguilleAvant.Motif} »)");

// CA04 (T08) : les chaines signees de la V2 intactes (aucune table de la V2 n'a change, hors affaires, demandes et renvois) ; la facture de la V3 lisible et
// integre ; la ligne de facturation de la V3 restee dans la V3, avec son renvoi dans la V2.
var apresRetourV2 = await Checksums(v2.Connexion, tablesV2);
var changees = apresRetourV2.Where(kv => avantRetourV2[kv.Key] != kv.Value).Select(kv => kv.Key).ToList();
var chainesV3Apres = await Checksums(v3.Connexion, chainesV3Avant.Keys);
bool integre;
await using (var cn = new SqlConnection(v3.Connexion)) { await cn.OpenAsync(); integre = (await ControleIntegrite.ControlerAsync(chaines, cn, "banc", null)).Integre; }
var renvoi = await Lire(v2.Connexion, "SELECT IdAffaireV2, TableV3, IdV3, BaseV3 FROM rigv3.RenvoiFactureV3");
Exiger(changees.Count == 0 && chainesV3Apres.All(kv => kv.Key == "soc.EvenementTechnique" || chainesV3Avant[kv.Key] == kv.Value) && integre
       && await Nombre(v3.Connexion, "SELECT COUNT(*) FROM soc.FactureSignee WHERE Numero = 'FACV3-2026-000001'") == 1
       && renvoi.Rows.Count == 1 && Convert.ToString(renvoi.Rows[0][0], CultureInfo.InvariantCulture) == await V2De("v2.AFFAIRE", mois[1].Affaire) && (string)renvoi.Rows[0][1] == "v2.ARTICLE_UTILISE"
       && await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.ARTICLE_UTILISE") == 0,
    $"REC-007 CA04 (T08) : aucune table de la V2 n'a change hors affaires, demandes et renvois ({tablesV2.Count} comparees{(changees.Count > 0 ? ", CHANGEES : " + string.Join(", ", changees) : "")}) -- " +
    $"rien n'entre dans ses chaines signees ; la facture FACV3-2026-000001 de la V3 reste lisible, chaines de la V3 integres ({integre}) ; l'article utilise de la V3 reste dans la V3, " +
    $"la V2 a son renvoi (affaire {(renvoi.Rows.Count > 0 ? renvoi.Rows[0][0] : "?")} -> {(renvoi.Rows.Count > 0 ? renvoi.Rows[0][1] : "?")} {(renvoi.Rows.Count > 0 ? renvoi.Rows[0][2] : "")})");

// REC-011 CA04 (T08) : les liens d'anciennes demandes vers des demandes de la V3, ecrits dans la V2 vers leurs lignes V2.
var inhibitriceV2 = await Texte(v2.Connexion, $"SELECT CONCAT(DMND_ID_DMND_INHIBITRICE, '|', DMND_INHIBEE) FROM dbo.DEMANDE WHERE DMND_ID_DMND = {await V2De("v2.DEMANDE", ancienneBloqueeParV3)}");
var mereV2 = await Texte(v2.Connexion, $"SELECT CONVERT(varchar(20), DMND_ID_DMND_MERE) FROM dbo.DEMANDE WHERE DMND_ID_DMND = {await V2De("v2.DEMANDE", ancienneNeeDeV3)}");
var filleMereV2 = await Texte(v2.Connexion, $"SELECT CONVERT(varchar(20), DMND_ID_DMND_MERE) FROM dbo.DEMANDE WHERE DMND_ID_DMND = {await V2De("v2.DEMANDE", fille)}");
Exiger(inhibitriceV2 == $"{await V2De("v2.DEMANDE", demandeLibre)}|1" && mereV2 == await V2De("v2.DEMANDE", fille) && filleMereV2 == await V2De("v2.DEMANDE", mereV3),
    $"REC-011 CA04 (T08) : recopiees dans la V2 au retour, la demande {demandeLibre} de la V3 y bloque l'ancienne {ancienneBloqueeParV3} (inhibitrice {inhibitriceV2}), la {fille} y engendre " +
    $"l'ancienne {ancienneNeeDeV3} (mere {mereV2}) et y est nee de l'ancienne {mereV3} (mere {filleMereV2}) : chaque lien vise la ligne V2 correspondante");

// CA07 (T11) : le retour chronometre et extrapole au mois le plus charge de RIG_DEV ; le controle quotidien (REC-006) ne trouve ensuite aucun ecart.
var passageFinal = await Passer();
var moisMax = (await Lire(chaineRigDev, "SELECT TOP (1) COUNT(*) FROM dbo.DEMANDE WHERE DMND_DATE_CREATION >= '2024-01-01' GROUP BY YEAR(DMND_DATE_CREATION) * 100 + MONTH(DMND_DATE_CREATION) ORDER BY 1 DESC")).Rows[0][0];
var parDemande = dureeRetour.TotalSeconds / Math.Max(1, retour.Demandes + retour.Affaires);
var extrapole = TimeSpan.FromSeconds(parDemande * (Convert.ToInt64(moisMax) * 1.5));
// Le controle de nuit tel qu'il tourne (ComparaisonNocturne : toutes les fiches partagees) ; ce qui alerte l'exploitation est ATraiter
// (ecarts + liens orphelins + sans correspondance, AlertesRecopie) : c'est lui qui doit etre nul, liens orphelins compris.
var controle = await ComparaisonBases.ComparerAsync(new OptionsComparaison(v2.Connexion, v3.Connexion));
Exiger(controle.Etat == "fini" && controle.Ecarts == 0 && controle.SansCorrespondance == 0 && controle.Orphelins == 0 && controle.ATraiter == 0 && controle.CodeSortie == 0
       && extrapole < TimeSpan.FromHours(10) && passageFinal.Erreur is null,
    $"REC-007 CA07 (T11) : retour de {retour.Affaires} affaires et {retour.Demandes} demandes en {dureeRetour.TotalSeconds:0.0} s ({parDemande * 1000:0.0} ms par ligne) ; extrapole au " +
    $"mois le plus charge de RIG_DEV ({moisMax} demandes, plus la moitie en affaires) : {extrapole:hh\\:mm\\:ss}, dans la nuit (20 h -> 6 h) ; le controle de nuit ensuite " +
    $"({controle.Tables.Count} tables, {controle.Etat}, code {controle.CodeSortie}) : {controle.LignesComparees} lignes comparees, {controle.Ecarts} ecart(s), " +
    $"{controle.SansCorrespondance} sans correspondance, {controle.Orphelins} lien(s) orphelin(s){ATraiterEnClair(controle)}");
// Contre-epreuve de CA07 (l'autre cote de la borne) : une ligne qui est dans la V2 doit toujours viser une fiche de la V2. La correspondance d'une
// affaire rendue mise de cote, ses deux demandes rendues (dans la V2) visent une affaire sans equivalent : deux liens orphelins, et seulement elles.
var affaireTemoin = mois[5].Affaire.ToString(CultureInfo.InvariantCulture);
await Executer(v3.Connexion, $"UPDATE rec.Correspondance SET TableV2 = N'AFFAIRE_TEMOIN' WHERE TableV2 = N'AFFAIRE' AND TableV3 = N'v2.AFFAIRE' AND IdV3 = N'{affaireTemoin}'");
var temoin07 = await ComparaisonBases.ComparerAsync(new OptionsComparaison(v2.Connexion, v3.Connexion) { Tables = ["AFFAIRE", "DEMANDE"] });
await Executer(v3.Connexion, $"UPDATE rec.Correspondance SET TableV2 = N'AFFAIRE' WHERE TableV2 = N'AFFAIRE_TEMOIN' AND IdV3 = N'{affaireTemoin}'");
var orphelinsTemoin = temoin07.Details.Where(d => d.Genre == "lien orphelin").ToList();
var demandesTemoin = new[] { mois[5].Validee, mois[5].EnCours }.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToHashSet();
Exiger(temoin07.Orphelins == 2 && orphelinsTemoin.All(d => d.Colonnes == "DMND_ID_AFFRE" && demandesTemoin.Contains(d.IdV3!)) && temoin07.CodeSortie == 5,
    $"REC-007 CA07 contre-epreuve : la correspondance de l'affaire rendue {affaireTemoin} mise de cote, le controle de nuit liste {temoin07.Orphelins} lien(s) orphelin(s) " +
    $"(attendu : ses demandes {string.Join(" et ", demandesTemoin)}){ATraiterEnClair(temoin07)}");

Reste("REC-007 CA07 sur la copie anonymisee d'un greffe beta avec un mois d'affaires simulees : attend un vrai greffe beta (Le Puy, Chartres, Vienne), la copie anonymisee " +
      "(lot 6) est recue ; " +
      "ici une base tiree de RIG_DEV et un mois simule de 120 affaires");

return await Fin();

// Les etats figes (instantanes) que le transfert ou le controle de nuit laissent sur une base jetable l'empechent d'etre supprimee :
// retires avant la fin, a chaque sortie apres le transfert.
async Task<int> Fin()
{
    await SupprimerInstantanes(v2.Nom);
    await SupprimerInstantanes(v3.Nom);
    return Bilan();
}

async Task SupprimerInstantanes(string nomBase)
{
    var instantanes = Colonne0(await Lire(v3.Connexion, $"SELECT name FROM sys.databases WHERE source_database_id = DB_ID('{nomBase}')"));
    foreach (var i in instantanes) await Executer(v3.Connexion, $"DROP DATABASE [{i}]");
}

internal sealed class CoffreMemoireBanc : ICoffreCles
{
    private readonly ConcurrentDictionary<string, ECDsa> cles = new();
    public ECDsa CleDuGreffe(string codeGreffe) => cles.GetOrAdd(codeGreffe, _ => ECDsa.Create(ECCurve.NamedCurves.nistP256));
    public string Conteneur(string codeGreffe) => $"memoire du banc RecopieRetourCheck ({codeGreffe})";
}
