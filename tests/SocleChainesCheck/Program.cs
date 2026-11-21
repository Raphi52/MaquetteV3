using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Serveur;
using RigV3.Serveur.Socle.Chaines;
using RigV3.Serveur.Socle.Greffes;
using RigV3Desktop.Pont;

// Banc du LOT 5 de la phase 2 du plan de reconstruction (2026-11-21) : SOC-018, ouvrir les chaines signees de la V3 le
// jour du passage. Un controle par critere du carnet (C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\
// socle.json), qui cite son id ; tout refus a son jumeau accepte.
//
// OU IL ECRIT : le SQL Server LOCAL seulement (docker compose up -d) -- trois bases jetables (BaseJetable), supprimees en
// sortie : la base commune (le catalogue), la base V3 du greffe et une base V2 reduite a ses chaines signees. La cle de
// signature est une cle EN MEMOIRE (CoffreMemoire) ; le seul passage par le magasin de cles de Windows (T02) cree une
// cle de nom unique et l'efface aussitot.
// SOURCES V2 relues avant de coder (branche origin/Development de D:\RigApplication) : SP_INSERT_JET, SP_HashJETR19,
// SP_HashJETR19_P, JET_NP.sql, CertifExpressoApp48\SignatureEcdsa.cs, Codec.cs, CalculHash.cs,
// SQL_Sources\SQLQuery_Deploy_Certification_FULL.sql (CODE_JET, SP_HashFactureR12, SP_HashEncaissement13, SP_HashDuplicata,
// SP_INSERT_Totaux_*_Periodiques, SP_VERIF_RUPTURE_SEQUENCE, ARCHIVE_FISCALE_EXERCICE), Docs\Note_Methodologie_Verif_
// Integrite_90.md, PROC_CERTIF ; tests\NumerotationV2Check\v2\TR_NUMERO_METIER_JET.rigdev.sql, fn_CertifNF525Active.rigdev.sql.
// fix-ok: premier lancement rouge sur « Incorrect syntax near 'GO' » (Program.cs:87, mesure) -- la chaine brute se termine sans saut
// de ligne, « GO » collait au debut du fichier de la V2 ; un saut de ligne les separe. Le commentaire de fin de ligne est retire
// du code lu par les controles statiques (une colonne commentee « ARFE_JET_NP_SEQ_MAX » de la migration les aurait trompes).

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var migrations = Path.Combine(racine, "Base", "migrations");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
const string Greffe = "9118";
const string Comptable = "banc.comptable";   // une fonction de facturation (soc.Fonction.Comptabilite = 1)
const string Agent = "banc.agent";           // admis, sans fonction de facturation : 403
const string Amitel = "banc.amitel";         // compte Amitel : l'ouverture au passage

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
void Reste(string quoi) => Console.WriteLine("RESTE : " + quoi);

await using var commune = await BaseJetable.CreerAsync("SocleChainesCheck");
await using var v3 = await BaseJetable.CreerAsync("SocleChainesGreffe");
await using var v2 = await BaseJetable.CreerAsync("SocleChainesV2");
await Migrateur.AppliquerAsync(commune.Connexion, migrations);
await Migrateur.AppliquerAsync(v3.Connexion, migrations);

async Task Executer(string connexion, string sql, params (string Nom, object? Valeur)[] p)
{
    await using var cn = new SqlConnection(connexion);
    await cn.OpenAsync();
    foreach (var lot in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline).Where(l => l.Trim().Length > 0))
    {
        await using var cmd = new SqlCommand(lot, cn);
        foreach (var (nom, valeur) in p) cmd.Parameters.AddWithValue(nom, valeur ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
async Task<T> Scalaire<T>(string connexion, string sql, params (string Nom, object? Valeur)[] p)
{
    await using var cn = new SqlConnection(connexion);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn);
    foreach (var (nom, valeur) in p) cmd.Parameters.AddWithValue(nom, valeur ?? DBNull.Value);
    var v = await cmd.ExecuteScalarAsync();
    return v is null or DBNull ? default! : (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
}
async Task<Exception?> Lever(Func<Task> action)
{
    try { await action(); return null; }
    catch (Exception e) { return e; }
}
Task<int> Compter(string sql) => Scalaire<int>(v3.Connexion, sql);

// --- La base V2 du greffe, reduite a ses chaines signees (formes de la V2 : JET_NP.sql, SQLQuery_Deploy_Certification_FULL.sql),
// --- avec la certification DESACTIVEE (ACTIVATION_CERTIF_NF525 = 0) et la fonction d'activation de RIG_DEV (CA02, CA06).
await Executer(v2.Connexion, """
    CREATE TABLE dbo.PARAMETRAGE (PARAM_CODE varchar(100) NOT NULL PRIMARY KEY, PARAM_VALEUR varchar(1000) NULL);
    INSERT dbo.PARAMETRAGE VALUES ('ACTIVATION_CERTIF_NF525', '0');
    CREATE TABLE dbo.JET_NP (JET_ID_JET_NP bigint IDENTITY(1,1) PRIMARY KEY, JET_DATE_NP datetime NOT NULL, JET_CODE_NP nvarchar(3) NOT NULL,
        JET_LOGIN_NP nvarchar(256) NULL, JET_INFORMATIONS_NP nvarchar(200) NULL, JET_CODE_CAISSE_NP nvarchar(15) NULL,
        JET_SIGNATURE_NP nvarchar(max) NULL, JET_SIGN_SEQ_NP bigint NULL);
    CREATE TABLE dbo.JET_P (JET_ID_JET_P bigint IDENTITY(1,1) PRIMARY KEY, JET_DATE_P datetime NOT NULL, JET_CODE_P nvarchar(3) NOT NULL,
        JET_LOGIN_P nvarchar(256) NULL, JET_INFORMATIONS_P nvarchar(200) NULL, JET_CODE_CAISSE_P nvarchar(15) NULL,
        JET_SIGNATURE_P nvarchar(max) NULL, JET_SIGN_SEQ_P bigint NULL);
    CREATE TABLE dbo.FACTURE (FACTU_NUMERO_FACTURE varchar(25) NOT NULL PRIMARY KEY, FACTU_MONTANT_TTC money NOT NULL,
        FACTU_DATE_CREATION datetime NOT NULL, FACTU_SIGN_ELEC nvarchar(250) NULL, FACTU_SIGN_SEQ bigint NULL);
    CREATE TABLE dbo.ENCAISSEMENT (ENCMT_ID_ENCMT int NOT NULL PRIMARY KEY, ENCMT_MONTANT money NOT NULL, ENCMT_DATE_SAISIE datetime NOT NULL,
        ENCMT_SIGN_ELEC nvarchar(250) NULL, ENCMT_SIGN_SEQ bigint NULL);
    INSERT dbo.JET_NP (JET_DATE_NP, JET_CODE_NP, JET_LOGIN_NP, JET_INFORMATIONS_NP, JET_CODE_CAISSE_NP, JET_SIGNATURE_NP, JET_SIGN_SEQ_NP)
    VALUES ('2025-03-01T08:00:00', N'260', N'rig', N'Initialisation des donnees (V2)', N'POSTE1', N'E1:v2-signature-np-1', 1),
           ('2026-08-31T18:00:00', N'60', N'rig', N'Cloture annuelle', N'POSTE1', N'E1:v2-signature-np-2', 2);
    INSERT dbo.JET_P (JET_DATE_P, JET_CODE_P, JET_LOGIN_P, JET_INFORMATIONS_P, JET_CODE_CAISSE_P, JET_SIGNATURE_P, JET_SIGN_SEQ_P)
    VALUES ('2026-08-31T18:00:00', N'50', N'rig', N'Cloture de periode', N'POSTE1', N'E1:v2-signature-p-1', 1);
    INSERT dbo.FACTURE VALUES ('26-022767', 312.40, '2026-08-31T10:00:00', N'E1:v2-signature-facture-1', 1), ('26-022768', 48.00, '2026-08-31T11:00:00', N'E1:v2-signature-facture-2', 2);
    INSERT dbo.ENCAISSEMENT VALUES (1, 312.40, '2026-08-31T10:05:00', N'E1:v2-signature-encaissement-1', 1);
    GO
    """ + Environment.NewLine + File.ReadAllText(Path.Combine(racine, "tests", "NumerotationV2Check", "v2", "fn_CertifNF525Active.rigdev.sql")));
const string EmpreinteV2 = """
    SELECT CONCAT(
      (SELECT COUNT(*) FROM dbo.JET_NP), '|', CONVERT(varchar(64), HASHBYTES('SHA2_256', (SELECT * FROM dbo.JET_NP ORDER BY 1 FOR JSON PATH)), 2), '|',
      (SELECT COUNT(*) FROM dbo.JET_P), '|', CONVERT(varchar(64), HASHBYTES('SHA2_256', (SELECT * FROM dbo.JET_P ORDER BY 1 FOR JSON PATH)), 2), '|',
      (SELECT COUNT(*) FROM dbo.FACTURE), '|', CONVERT(varchar(64), HASHBYTES('SHA2_256', (SELECT * FROM dbo.FACTURE ORDER BY 1 FOR JSON PATH)), 2), '|',
      (SELECT COUNT(*) FROM dbo.ENCAISSEMENT), '|', CONVERT(varchar(64), HASHBYTES('SHA2_256', (SELECT * FROM dbo.ENCAISSEMENT ORDER BY 1 FOR JSON PATH)), 2));
    """;
var empreinteV2Avant = await Scalaire<string>(v2.Connexion, EmpreinteV2);

// --- Les comptes du greffe (dans SA base V3) et le catalogue commun ----------------------------------------------------
await Executer(v3.Connexion, """
    INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES (@greffe, 'COMPTA', N'Comptabilite');
    INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANC_COMPTA', 'Banc : comptable'), ('BANC_LECTURE', 'Banc : sans facturation');
    INSERT INTO soc.Fonction (Code, Libelle, TypeV2, Comptabilite) VALUES ('BANCFACT', 'Facturation (banc)', 'PROC', 1), ('BANCRECH', 'Recherche (banc)', 'PROC', 0);
    INSERT INTO soc.RoleFonction (Role, Fonction) VALUES ('BANC_COMPTA', 'BANCFACT'), ('BANC_LECTURE', 'BANCRECH');
    INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive, EstAmitel)
    SELECT v.c, 'BANC', v.p, v.a, 'M.', @greffe, s.Id, 0, v.amitel
    FROM (VALUES (@comptable, 'Comptable', 'BCPT', 0), (@agent, 'Agent', 'BAGT', 0), (@amitel, 'Amitel', 'BAMI', 1)) v (c, p, a, amitel)
    CROSS JOIN soc.Service s;
    INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, CASE CompteWindows WHEN @comptable THEN 'BANC_COMPTA' ELSE 'BANC_LECTURE' END FROM soc.Compte;
    """, ("@greffe", Greffe), ("@comptable", Comptable), ("@agent", Agent), ("@amitel", Amitel));
var serveurBancs = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
var authentification = new SqlConnectionStringBuilder(serveurBancs.ConnectionString);
authentification.Remove("Data Source");
await Executer(commune.Connexion,
    "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe) " +
    "VALUES (@c, N'Greffe du banc des chaines', @s, @v2, @v3, 0, 0, 'Romance Standard Time', 'banc', SYSDATETIME());",
    ("@c", Greffe), ("@s", serveurBancs.DataSource), ("@v2", v2.Nom), ("@v3", v3.Nom));

// --- Le vrai serveur, en memoire : cle de signature en memoire, signature de fond arretee (le banc la joue lui-meme) ----
var coffre = new CoffreMemoire();
await using var app = ServeurRig.Construire(["--environment", "Development"], s =>
{
    s.WebHost.UseTestServer();
    s.Logging.SetMinimumLevel(LogLevel.Warning);
    s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
    s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
    s.Configuration[SignatureDeFond.Reglage] = "0";
    s.Services.AddSingleton<ICoffreCles>(coffre);
});
await app.StartAsync();
var http = app.GetTestClient();
var chaines = app.Services.GetRequiredService<ServiceChaines>();
string R(string route) => RoutesChaines.Pour(route, Greffe);

async Task<(HttpStatusCode Code, string Corps)> Appeler(HttpMethod verbe, string route, object? corps, string compte = Comptable)
{
    using var demande = new HttpRequestMessage(verbe, route);
    if (corps is not null) demande.Content = JsonContent.Create(corps);
    demande.Headers.Add(RoutesIdentite.EnTeteCompte, compte);
    using var r = await http.SendAsync(demande);
    return (r.StatusCode, await r.Content.ReadAsStringAsync());
}
async Task<T> Lire<T>(HttpMethod verbe, string route, object? corps = null, string compte = Comptable)
{
    var (code, texte) = await Appeler(verbe, route, corps, compte);
    if (code != HttpStatusCode.OK) throw new InvalidOperationException($"{verbe} {route} : {(int)code} {texte}");
    return JsonSerializer.Deserialize<T>(texte, json)!;
}
async Task<BilanIntegrite> Controler() => await Lire<BilanIntegrite>(HttpMethod.Post, R(RoutesChaines.Controle), new { });
async Task<T> SurV3<T>(Func<SqlConnection, SqlTransaction, Task<T>> travail, bool valider = true)
{
    await using var cn = new SqlConnection(v3.Connexion);
    await cn.OpenAsync();
    await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
    var r = await travail(cn, tx);
    if (valider) await tx.CommitAsync(); else await tx.RollbackAsync();
    return r;
}
static string Bilan(BilanIntegrite b) => string.Join(" ; ", b.Chaines.Select(c => $"{c.Code} {c.Valides}/{c.Maillons}"
    + (c.Defauts.Length + c.Ruptures.Length > 0 ? $" [{string.Join(" | ", c.Defauts.Concat(c.Ruptures))}]" : ""))) + (b.Archives.Length > 0 ? $" ; archives [{string.Join(" | ", b.Archives)}]" : "");

var f1 = new PieceFacture("V3-25-000001", "FACTURE", new DateTime(2025, 12, 30, 10, 0, 0), 120.00m, "SARL Martin", "80000", "FR12345678901",
    [new LigneTva(20.00m, 100.00m), new LigneTva(0.00m, 20.00m)]);
var f2 = new PieceFacture("V3-25-000002", "AVOIR", new DateTime(2025, 12, 31, 11, 0, 0), -30.00m, "SARL Martin", "80000", "FR12345678901",
    [new LigneTva(20.00m, -30.00m)]);
var f3 = new PieceFacture("V3-26-000001", "FACTURE", DateTime.Now.AddMinutes(-5), 60.00m, "M. Durand", "80100", null, [new LigneTva(20.00m, 60.00m)]);

// =====================================================================================================================
// Avant le passage
// =====================================================================================================================
Console.WriteLine("--- SOC-018 : avant le passage");
var avant = await Lire<EtatCertification>(HttpMethod.Get, R(RoutesChaines.Etat));
Exiger(avant is { Ouverte: false, Active: false } && avant.Chaines.Length == 7 && avant.Chaines.All(c => c.Maillons == 0 && c.DerniereSeq == 0),
    $"SOC-018 CA01 (avant) : avant le passage, aucune chaine n'est ouverte et les 7 sont vides ({string.Join(", ", avant.Chaines.Select(c => $"{c.Code}={c.Maillons}"))})");
// La livraison de la base (lot 12, Outils\Livraison\Livreur.cs) ecrit son evenement 250 AVANT le passage.
await Executer(v3.Connexion, "INSERT INTO soc.EvenementTechnique (Code, Libelle, Detail, IdAuteur, Auteur) VALUES " +
    "(250, 'Installation d''une nouvelle version majeure du logiciel', 'Version 2026.1.0 livree (banc)', NULL, 'Livraison banc');");
var avantPassage = await Lever(() => SurV3((cn, tx) => chaines.SignerFactureAsync(cn, tx, f1)));
Exiger(avantPassage is ChainesNonOuvertes && await Compter("SELECT COUNT(*) FROM soc.FactureSignee") == 0,
    $"SOC-018 CA01 (avant) : rien ne se signe avant l'ouverture des chaines ({avantPassage?.GetType().Name} : {avantPassage?.Message})");

// =====================================================================================================================
// CA01 : les 7 chaines commencent vides au passage, l'evenement 260 ouvre le journal
// =====================================================================================================================
Console.WriteLine("--- SOC-018 CA01 : ouverture au passage");
var parComptable = await Appeler(HttpMethod.Post, R(RoutesChaines.Ouverture), new { }, Comptable);
Exiger(parComptable.Code == HttpStatusCode.Forbidden && parComptable.Corps.Contains("Amitel") && await Compter("SELECT COUNT(*) FROM soc.Certification") == 0,
    $"SOC-018 CA01 : l'ouverture est un geste du passage, refusee a un comptable du greffe ({(int)parComptable.Code}), rien n'est ouvert");
var ouverture = await Lire<EtatCertification>(HttpMethod.Post, R(RoutesChaines.Ouverture), new { }, Amitel);
var premier = await Scalaire<int>(v3.Connexion, "SELECT Code FROM soc.EvenementTechnique WHERE Chaine = 'JNP' AND Seq = 1");
var deuxieme = await Scalaire<int>(v3.Connexion, "SELECT Code FROM soc.EvenementTechnique WHERE Chaine = 'JNP' AND Seq = 2");
var detail260 = await Scalaire<string>(v3.Connexion, "SELECT Detail FROM soc.EvenementTechnique WHERE Code = 260");
Exiger(ouverture is { Ouverte: true, Active: true, Greffe: Greffe, EnAttente: 0 }
       && ouverture.Chaines.Where(c => c.Code != ChainesSignees.JournalNonPurgeable).All(c => c.Maillons == 0)
       && ouverture.Chaines.Single(c => c.Code == ChainesSignees.JournalNonPurgeable).Maillons == 2
       && premier == 260 && deuxieme == 250,
    $"SOC-018 CA01 : au passage, les 7 chaines commencent vides et l'evenement 260 en est le premier maillon (JNP n° 1 = {premier}, " +
    $"n° 2 = {deuxieme}, l'evenement de livraison ecrit avant ; {string.Join(", ", ouverture.Chaines.Select(c => $"{c.Code}={c.Maillons}"))})");
Exiger(detail260.Contains("Les chaînes de la V2 restent dans la V2") && detail260.Contains(ouverture.EmpreinteCle!),
    $"SOC-018 CA01 : l'evenement 260 dit ce qui s'ouvre, que la V2 garde ses chaines, et la cle qui signe (« {detail260[..90]}… »)");
var secondeOuverture = await Appeler(HttpMethod.Post, R(RoutesChaines.Ouverture), new { }, Amitel);
Exiger(secondeOuverture.Code == HttpStatusCode.Conflict && await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = 260") == 1
       && await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Chaine = 'JNP'") == 2,
    $"SOC-018 CA01 : une seconde ouverture est refusee ({(int)secondeOuverture.Code}) et ne change rien (un seul 260)");
Exiger(await Compter("SELECT COUNT(*) FROM soc.Journal WHERE Famille = 'certification' AND Verbe = 'a ouvert'") == 1,
    "SOC-016 : l'ouverture des chaines est au journal des gestes, avec son auteur");

// =====================================================================================================================
// CA06 : la certification est active dans la V3 des le premier jour, quel que soit le reglage de la base V2
// =====================================================================================================================
Console.WriteLine("--- SOC-018 CA06 : certification active partout");
var activeV2 = await Scalaire<bool>(v2.Connexion, "SELECT dbo.fn_CertifNF525Active()");
var m1 = await SurV3((cn, tx) => chaines.SignerFactureAsync(cn, tx, f1));
Exiger(!activeV2 && ouverture.Active && m1.Seq == 1 && m1.Signature.StartsWith(Signatures.Prefixe, StringComparison.Ordinal),
    $"SOC-018 CA06 : la base V2 du greffe a la certification DESACTIVEE (fn_CertifNF525Active() = {(activeV2 ? 1 : 0)}) et la V3 signe quand meme " +
    $"sa premiere facture (maillon {m1.Seq}, {m1.Signature[..12]}…)");
// Le code sans ses commentaires (lignes de commentaire, et la fin de ligne apres « -- » en SQL ou « // » en C#).
string[] SansCommentaire(string fichier) => File.ReadAllLines(fichier)
    .Where(l => !Regex.IsMatch(l.TrimStart(), @"^(//|--|\*|/\*)"))
    .Select(l => Regex.Replace(l, fichier.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ? @"\s--.*$" : @"\s//.*$", "")).ToArray();
var codeChaines = Directory.GetFiles(Path.Combine(racine, "Serveur", "Socle", "Chaines"), "*.cs")
    .Append(Path.Combine(migrations, "SOC-018_chaines.sql")).ToArray();
var lectureActivation = codeChaines.SelectMany(f => SansCommentaire(f).Select(l => (f, l)))
    .Where(x => Regex.IsMatch(x.l, "ACTIVATION_CERTIF_NF525|fn_CertifNF525Active|PARAMETRAGE", RegexOptions.IgnoreCase)).ToList();
Exiger(lectureActivation.Count == 0,
    $"SOC-018 CA06 : aucun code des chaines ne lit le reglage de certification de la V2 ({codeChaines.Length} fichiers lus, hors commentaires)");
var interrupteur = await Lever(() => Executer(v3.Connexion, "DELETE FROM soc.Certification;"));
Exiger(interrupteur is SqlException { Number: 51018 } && await Compter("SELECT COUNT(*) FROM soc.Certification") == 1,
    "SOC-018 CA06 : la certification de la V3 n'a pas d'interrupteur : effacer son ouverture est refuse par la base");

// =====================================================================================================================
// CA03 : chaque facture ou encaissement reprend la signature precedente de sa chaine ; le controle passe
// =====================================================================================================================
Console.WriteLine("--- SOC-018 CA03 : signature chainee");
var m2 = await SurV3((cn, tx) => chaines.SignerFactureAsync(cn, tx, f2));
var annulee = await SurV3((cn, tx) => chaines.SignerFactureAsync(cn, tx, f3 with { Numero = "V3-26-ANNULEE" }), valider: false);
var m3 = await SurV3((cn, tx) => chaines.SignerFactureAsync(cn, tx, f3));
var spki = await Scalaire<byte[]>(v3.Connexion, "SELECT TOP (1) ClePublique FROM soc.CleSignature ORDER BY Id");
using var clePublique = Signatures.ClePublique(spki);
string Pre(PieceFacture f, long seq, string? precedente) => Preimages.Facture(seq, precedente, f.Numero, f.Nature, new DateTime(f.EmiseLe.Ticks - f.EmiseLe.Ticks % TimeSpan.TicksPerSecond),
    f.MontantTtc, f.Client, f.CodePostal, f.TvaIntra, f.Lignes.Select(l => (l.Taux, l.MontantTtc)));
Exiger(m2.Seq == 2 && Signatures.Verifier(clePublique, Pre(f2, 2, m1.Signature), m2.Signature)
       && !Signatures.Verifier(clePublique, Pre(f2, 2, null), m2.Signature) && Signatures.Verifier(clePublique, Pre(f1, 1, null), m1.Signature),
    "SOC-018 CA03 : la signature de la facture 2 reprend celle de la facture 1 (verifiee avec la seule cle publique de la base ; " +
    "sans la precedente, elle ne verifie plus) ; la premiere n'a pas de precedente");
Exiger(annulee.Seq == 3 && m3.Seq == 3 && await Compter("SELECT COUNT(*) FROM soc.FactureSignee") == 3,
    $"SOC-018 CA03 : une facture signee dans une transaction annulee ne laisse aucun trou (son rang {annulee.Seq} est repris par la suivante, {m3.Seq}) " +
    "-- la V2 en laissait un « legitime » (SEQUENCE SQL, SP_VERIF_RUPTURE_SEQUENCE)");
var e1 = await SurV3((cn, tx) => chaines.SignerEncaissementAsync(cn, tx, new PieceEncaissement("ENC-25-0001", new DateTime(2025, 12, 30, 10, 5, 0), 120.00m)));
var e2 = await SurV3((cn, tx) => chaines.SignerEncaissementAsync(cn, tx, new PieceEncaissement("ENC-25-0002", new DateTime(2025, 12, 31, 11, 5, 0), -30.00m)));
var enParallele = await Task.WhenAll(Enumerable.Range(1, 8).Select(i =>
    SurV3((cn, tx) => chaines.SignerEncaissementAsync(cn, tx, new PieceEncaissement($"ENC-26-{i:0000}", DateTime.Now.AddMinutes(-i), 10m * i)))));
var rangsEnc = enParallele.Select(m => m.Seq).Order().ToArray();
Exiger(e1.Seq == 1 && e2.Seq == 2 && rangsEnc.SequenceEqual(Enumerable.Range(3, 8).Select(i => (long)i)),
    $"SOC-018 CA03 : les encaissements se chainent aussi, 8 signes en meme temps prennent les rangs 3 a 10 sans trou ni doublon ({string.Join(",", rangsEnc)})");
var d1 = await SurV3((cn, tx) => chaines.SignerDuplicataAsync(cn, tx, new PieceDuplicata(f1.Numero, DateTime.Now, "demande du client", "AMITEL\\" + Comptable)));
Exiger(d1.Seq == 1 && await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = 155 AND Chaine = 'JP'") == 1,
    "SOC-018 T02 : le duplicata d'une facture se signe dans sa chaine et ecrit l'evenement 155 (journal purgeable)");
var controle1 = await Controler();
Exiger(controle1 is { Integre: true, ValeurProbante: true, EvenementsEcrits.Length: 0 }
       && controle1.Chaines.Single(c => c.Code == ChainesSignees.Factures).Valides == 3
       && controle1.Chaines.Single(c => c.Code == ChainesSignees.Encaissements).Valides == 10
       && controle1.Chaines.All(c => c.Valides == c.Maillons),
    $"SOC-018 CA03 : le controle d'integrite de toutes les chaines passe ({Bilan(controle1)})");

// T01 : aucune modification des lignes signees hors des procedures de signature (jumeau : la signature, elle, a ecrit).
var majFacture = await Lever(() => Executer(v3.Connexion, "UPDATE soc.FactureSignee SET MontantTtc = 1 WHERE Seq = 1;"));
var effEnc = await Lever(() => Executer(v3.Connexion, "DELETE FROM soc.EncaissementSigne WHERE Seq = 1;"));
var insDirect = await Lever(() => Executer(v3.Connexion,
    "INSERT INTO soc.FactureSignee (Numero, Nature, EmiseLe, MontantTtc, Client, Seq, Signature, IdCle) VALUES ('FAUX', 'FACTURE', SYSDATETIME(), 1, 'x', 99, 'E1:faux', 1);"));
var majEvt = await Lever(() => Executer(v3.Connexion, "UPDATE soc.EvenementTechnique SET Detail = N'falsifie' WHERE Chaine = 'JNP' AND Seq = 1;"));
var reculer = await Lever(() => Executer(v3.Connexion, "UPDATE soc.EtatChaine SET DerniereSeq = 0 WHERE Chaine = 'FAC';"));
Exiger(majFacture is SqlException { Number: 51018 } && effEnc is SqlException { Number: 51018 } && insDirect is SqlException { Number: 51018 }
       && majEvt is SqlException { Number: 51010 } && reculer is SqlException { Number: 51018 }
       && await Compter("SELECT COUNT(*) FROM soc.FactureSignee") == 3 && await Compter("SELECT COUNT(*) FROM soc.EncaissementSigne") == 10,
    "SOC-018 T01 : une ligne signee ne se modifie, ne s'efface ni n'entre hors de la signature (facture, encaissement, evenement, etat de chaine), " +
    "meme au proprietaire de la base ; jumeau : les 3 factures et 10 encaissements signes sont la");

// =====================================================================================================================
// Les series que la V3 tient seule (FACV3) ecrivent leurs evenements dans sa chaine (NUM-006 CA03, laisse a SOC-018)
// =====================================================================================================================
Console.WriteLine("--- SOC-018 : evenements des series de la V3");
await Executer(v3.Connexion, "INSERT num.Compteur (Type, Annee, Jour, Libelle, Structure, CodageAnnee, Lettre, Separateur, TailleChrono, CodeGreffe, Debut, Chrono, GestionRendus) " +
    "VALUES ('FACV3', 2026, 0, 'Factures de la V3 (NUM-020)', 'LA-C', 2, 'V3-', NULL, 6, NULL, 1, NULL, 0);");
var dixAvant = await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = 10");
await Executer(v3.Connexion, "UPDATE num.Compteur SET Chrono = 12, DerniereAttribution = SYSDATETIME() WHERE Type = 'FACV3';");
var dixApresChrono = await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = 10");
await Executer(v3.Connexion, "UPDATE num.Compteur SET CodageAnnee = 4 WHERE Type = 'FACV3';");
var dixEnAttente = await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = 10 AND Seq IS NULL");
var passage = await app.Services.GetRequiredService<SignatureDeFond>().PasserAsync();
var dix = await Scalaire<string>(v3.Connexion, "SELECT CONCAT(Chaine, '|', Seq, '|', Detail) FROM soc.EvenementTechnique WHERE Code = 10");
Exiger(dixAvant == 0 && dixApresChrono == 0,
    $"SOC-018 / NUM-006 CA03 : le simple avancement du chrono de FACV3 n'ecrit aucun evenement ({dixAvant} -> {dixApresChrono})");
Exiger(dixEnAttente == 1 && passage.Single(p => p.Greffe == Greffe) is { Signes: >= 1, Erreur: null }
       && dix.StartsWith("JNP|", StringComparison.Ordinal) && dix.Contains("FACV3") && dix.Contains("codage de l'année 2 -> 4"),
    $"SOC-018 / NUM-006 CA03 : un changement de mode de la serie FACV3, que la V3 tient seule, ecrit l'evenement 10 dans le journal de la V3, " +
    $"signe par la signature de fond (« {dix} »)");

// =====================================================================================================================
// CA05 : clotures du jour, du mois et de l'exercice ; grands totaux signes ; archive scellee liee au journal non purgeable
// =====================================================================================================================
Console.WriteLine("--- SOC-018 CA05 : clotures et archive fiscale");
var jour1 = await Lire<ResultatCloture>(HttpMethod.Post, R(RoutesChaines.Clotures), new DemandeCloture("jour", "2025-12-30"));
var jour2 = await Lire<ResultatCloture>(HttpMethod.Post, R(RoutesChaines.Clotures), new DemandeCloture("jour", "2025-12-31"));
GrandTotal F(ResultatCloture r) => r.Totaux.Single(t => t.Chaine == ChainesSignees.TotauxFactures);
GrandTotal E(ResultatCloture r) => r.Totaux.Single(t => t.Chaine == ChainesSignees.TotauxEncaissements);
Exiger(F(jour1) is { Type: "GTP", Periode: "20251230", Total: 120.00m, CumulReel: 120.00m, CumulAbsolu: 120.00m, NbPieces: 1 }
       && F(jour1).ParTaux.Select(t => (t.Taux, t.MontantTtc)).SequenceEqual([(20.00m, 100.00m), (0.00m, 20.00m)])
       && E(jour1) is { Total: 120.00m, NbPieces: 1 },
    $"SOC-018 CA05 : la cloture du 30/12/2025 calcule et signe le total du jour, par taux de TVA, et son cumul perpetuel ({F(jour1).Total} ; {F(jour1).CumulReel})");
Exiger(F(jour2) is { Periode: "20251231", Total: -30.00m, CumulReel: 90.00m, CumulAbsolu: 150.00m } && E(jour2) is { CumulReel: 90.00m, CumulAbsolu: 150.00m },
    $"SOC-018 CA05 : le 31/12, l'avoir de -30 donne un cumul perpetuel reel de 90 et absolu de 150 (le precedent du meme type plus la periode)");
var reCloture = await Appeler(HttpMethod.Post, R(RoutesChaines.Clotures), new DemandeCloture("jour", "2025-12-31"));
var gtfAvant = await Compter("SELECT COUNT(*) FROM soc.GrandTotalFactures");
var mois = await Lire<ResultatCloture>(HttpMethod.Post, R(RoutesChaines.Clotures), new DemandeCloture("mois", "2025-12-15"));
var enCours = await Appeler(HttpMethod.Post, R(RoutesChaines.Clotures), new DemandeCloture("exercice", $"{DateTime.Now.Year}-06-01"));
Exiger(reCloture.Code == HttpStatusCode.Conflict && reCloture.Corps.Contains("déjà clos") && enCours.Code == HttpStatusCode.Conflict
       && enCours.Corps.Contains("n'est pas terminé") && F(mois) is { Type: "GTM", Periode: "202512", Total: 90.00m, CumulReel: 90.00m, NbPieces: 2 }
       && await Compter("SELECT COUNT(*) FROM soc.GrandTotalFactures") == gtfAvant + 1,
    $"SOC-018 CA05 : un jour deja clos ne se recloture pas, un exercice en cours ne se clot pas ({(int)reCloture.Code}, {(int)enCours.Code}) ; " +
    $"le mois de decembre 2025 se clot (GTM {F(mois).Total})");
// Deux evenements purgeables et un non purgeable dates de l'exercice 2025 (ecrits apres coup) : l'archive les porte.
await Executer(v3.Connexion, """
    INSERT INTO soc.EvenementTechnique (Instant, Code, Libelle, Detail, IdAuteur, Auteur) VALUES
      ('2025-06-15T08:00:00', 155, 'Duplicata d''une note', 'Duplicata (exercice 2025, banc)', NULL, 'banc'),
      ('2025-06-15T09:00:00', 40, 'Arret du terminal', 'Arret du poste (exercice 2025, banc)', NULL, 'banc'),
      ('2025-06-15T10:00:00', 410, 'Changement de donnees affectant l''identification de l''assujetti', 'Adresse du greffe (exercice 2025, banc)', NULL, 'banc');
    """);
var exercice = await Lire<ResultatCloture>(HttpMethod.Post, R(RoutesChaines.Clotures), new DemandeCloture("exercice", "2025-12-31"));
var a = exercice.Archive!;
byte[] contenu = await Scalaire<byte[]>(v3.Connexion, "SELECT Contenu FROM soc.ArchiveFiscale WHERE Exercice = 2025");
var sceau = await Scalaire<string>(v3.Connexion, "SELECT Sceau FROM soc.ArchiveFiscale WHERE Exercice = 2025");
var evt30 = await Scalaire<string>(v3.Connexion, "SELECT CONCAT(Code, '|', Chaine, '|', Seq, '|', Detail) FROM soc.EvenementTechnique WHERE Id = @id", ("@id", a.IdEvenement));
Exiger(F(exercice) is { Type: "GTA/E", Periode: "2025", Total: 90.00m } && a is { Exercice: 2025, NbPieces: 4, DerniereSeqJp: 2 }
       && Signatures.Empreinte(contenu) == a.Empreinte && evt30.StartsWith($"30|JNP|{a.SeqEvenement}|", StringComparison.Ordinal) && evt30.Contains(a.Empreinte)
       && Signatures.Verifier(clePublique, Preimages.Sceau(Greffe, 2025, a.Empreinte, a.NbPieces, a.DerniereSeqJnp, a.DerniereSeqJp, a.IdEvenement), sceau),
    $"SOC-018 CA05 : la cloture de l'exercice 2025 signe son grand total et produit l'archive fiscale {a.Fichier} ({a.NbPieces} pieces, " +
    $"empreinte {a.Empreinte[..12]}…), scellee, enregistree par l'evenement 30 du journal non purgeable (maillon {a.SeqEvenement})");
using (var doc = JsonDocument.Parse(contenu))
{
    var r = doc.RootElement;
    var cleArchive = Signatures.ClePublique(Convert.FromBase64String(r.GetProperty("cles")[0].GetProperty("clePubliqueSpki").GetString()!));
    var fa = r.GetProperty("factures")[0];
    var lignes = fa.GetProperty("lignes").EnumerateArray().Select(l => (l.GetProperty("taux").GetDecimal(), l.GetProperty("montantTtc").GetDecimal())).ToList();
    var preArchive = Preimages.Facture(fa.GetProperty("seq").GetInt64(), null, fa.GetProperty("numero").GetString()!, fa.GetProperty("nature").GetString()!,
        fa.GetProperty("emiseLe").GetDateTime(), fa.GetProperty("montantTtc").GetDecimal(), fa.GetProperty("client").GetString()!,
        fa.GetProperty("codePostal").GetString(), fa.GetProperty("tvaIntra").GetString(), lignes);
    Exiger(r.GetProperty("factures").GetArrayLength() == 2 && r.GetProperty("journalPurgeable").GetArrayLength() == 2
           && r.GetProperty("journalNonPurgeable").GetArrayLength() == 1
           && Signatures.Verifier(cleArchive, preArchive, fa.GetProperty("signature").GetString()),
        "SOC-018 CA05 : l'archive porte les pieces, totaux et evenements de l'exercice avec la cle publique : la signature de la facture " +
        "V3-25-000001 s'y verifie sans la base (2 factures, 2 evenements purgeables, 1 non purgeable)");
}
var controle2 = await Controler();
Exiger(controle2.Integre && controle2.EvenementsEcrits.Length == 0 && controle2.Archives.Length == 0,
    $"SOC-018 CA05 : apres les clotures, les chaines des totaux et l'archive passent le controle ({Bilan(controle2)})");

// =====================================================================================================================
// CA07 : la consultation (journal filtrable par type et par date, grands totaux en lecture seule), la purge limitee
// =====================================================================================================================
Console.WriteLine("--- SOC-018 CA07 : consultation et purge");
var parCode = await Lire<EvenementSigne[]>(HttpMethod.Get, R(RoutesChaines.Journal) + "?code=10");
var purgeables = await Lire<EvenementSigne[]>(HttpMethod.Get, R(RoutesChaines.Journal) + "?partie=p");
var leJour = await Lire<EvenementSigne[]>(HttpMethod.Get, R(RoutesChaines.Journal) + "?du=2025-06-15&au=2025-06-15");
var lendemain = await Lire<EvenementSigne[]>(HttpMethod.Get, R(RoutesChaines.Journal) + "?du=2025-06-16&au=2025-06-16");
var illisible = await Appeler(HttpMethod.Get, R(RoutesChaines.Journal) + "?du=15/06/2025", null);
Exiger(parCode.Length == 1 && parCode[0].Code == 10 && purgeables.Length > 0 && purgeables.All(e => e.Purgeable)
       && leJour.Select(e => e.Code).Order().SequenceEqual(new short[] { 40, 155, 410 }) && lendemain.Length == 0 && illisible.Code == HttpStatusCode.BadRequest,
    $"SOC-018 CA07 : le journal se filtre par type ({parCode.Length} evenement 10), par partie ({purgeables.Length} purgeables) et par date " +
    $"({leJour.Length} le 15/06/2025, {lendemain.Length} le lendemain) ; une date illisible est refusee ({(int)illisible.Code})");
var totaux = await Lire<GrandTotal[]>(HttpMethod.Get, R(RoutesChaines.Totaux));
var ecritureTotaux = new List<HttpStatusCode>();
foreach (var verbe in new[] { HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
    ecritureTotaux.Add((await Appeler(verbe, R(RoutesChaines.Totaux), new { total = 0 })).Code);
Exiger(totaux.Count(t => t.Chaine == "GTF") == 4 && totaux.Count(t => t.Chaine == "GTE") == 4
       && ecritureTotaux.All(c => c is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound),
    $"SOC-018 CA07 : les grands totaux se lisent (4 des factures, 4 des encaissements) et aucune route ne les modifie ({string.Join(", ", ecritureTotaux)})");
var refusAgent = await Appeler(HttpMethod.Get, R(RoutesChaines.Etat), null, Agent);
var refusJournal = await Appeler(HttpMethod.Get, R(RoutesChaines.Journal), null, Agent);
var accesComptable = await Appeler(HttpMethod.Get, R(RoutesChaines.Etat), null, Comptable);
Exiger(refusAgent.Code == HttpStatusCode.Forbidden && refusAgent.Corps.Contains("facturation") && refusJournal.Code == HttpStatusCode.Forbidden
       && accesComptable.Code == HttpStatusCode.OK,
    $"SOC-018 CA07 : la consultation est aux comptables du greffe : un agent sans fonction de facturation est refuse ({(int)refusAgent.Code}), " +
    $"le comptable est servi ({(int)accesComptable.Code})");
var jpAvant = await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Chaine = 'JP'");
var purgeSansArchive = await Appeler(HttpMethod.Post, R(RoutesChaines.Purge), new DemandePurge(DateTime.Now.Year));
Exiger(purgeSansArchive.Code == HttpStatusCode.Conflict && purgeSansArchive.Corps.Contains("archive fiscale")
       && await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Chaine = 'JP'") == jpAvant,
    $"SOC-018 CA07 : l'exercice {DateTime.Now.Year}, sans archive, ne se purge pas ({(int)purgeSansArchive.Code}) : rien n'est efface");
var purge = await Lire<ResultatPurge>(HttpMethod.Post, R(RoutesChaines.Purge), new DemandePurge(2025));
var restants2025 = await Scalaire<string>(v3.Connexion,
    "SELECT CONCAT(SUM(CASE WHEN Chaine = 'JP' THEN 1 ELSE 0 END), '|', SUM(CASE WHEN Chaine = 'JNP' THEN 1 ELSE 0 END)) FROM soc.EvenementTechnique WHERE Exercice = 2025");
var evt205 = await Scalaire<string>(v3.Connexion, "SELECT CONCAT(Chaine, '|', Detail) FROM soc.EvenementTechnique WHERE Id = @id", ("@id", purge.IdEvenement));
Exiger(purge.Purges == 2 && restants2025 == "0|1" && evt205.StartsWith("JNP|Purge partielle du journal : 2 ", StringComparison.Ordinal)
       && await Compter("SELECT COUNT(*) FROM soc.EtatChaine WHERE Chaine = 'JP' AND Exercice = 2025 AND PurgeJusqua = 2") == 1,
    $"SOC-018 CA07 : apres l'archive, la purge de l'exercice 2025 efface ses 2 evenements purgeables et garde le non purgeable " +
    $"(reste purgeable|non purgeable = {restants2025}) ; l'evenement 205 l'ecrit au journal non purgeable");
var jnpAvant = await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Chaine = 'JNP'");
var forcerNp = await Lever(() => Executer(v3.Connexion,
    "EXEC sys.sp_set_session_context @key = N'rigv3.purge-jet', @value = 1; DELETE FROM soc.EvenementTechnique WHERE Chaine = 'JNP' AND Exercice = 2025;"));
var forcerJp = await Lever(() => Executer(v3.Connexion,
    "EXEC sys.sp_set_session_context @key = N'rigv3.purge-jet', @value = 1; DELETE FROM soc.EvenementTechnique WHERE Chaine = 'JP' AND Exercice = @x;",
    ("@x", DateTime.Now.Year)));
Exiger(forcerNp is SqlException { Number: 51010 } && forcerJp is SqlException { Number: 51010 }
       && await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Chaine = 'JNP'") == jnpAvant,
    "SOC-018 CA07 : meme par la porte de la purge, la base refuse d'effacer le journal NON purgeable, ou le purgeable d'un exercice sans archive");
var controle3 = await Controler();
Exiger(controle3.Integre && controle3.EvenementsEcrits.Length == 0,
    $"SOC-018 CA07 : apres la purge, le controle ne voit ni trou ni defaut : l'exercice purge reste prouve par son archive ({Bilan(controle3)})");

// --- L'ecran « Certification NF525 » (Pont/EcranChaines.cs) par le vrai ClientServeur ----------------------------------
ClientServeur ClientEcran(string compte)
{
    Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, compte);
    try { return new ClientServeur(new Uri("http://localhost/"), app.GetTestServer().CreateHandler()); }
    finally { Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null); }
}
var ecran = ClientEcran(Comptable);
var tout = new FiltreJournal(null, null, null, null);
var vue = await EcranChaines.OuvrirAsync(ecran, Greffe, tout);
Exiger(vue is { Mode: "serveur", Etat.Ouverte: true } && vue.Journal.Length > 5 && vue.Totaux.Length == 8 && vue.Codes.Length == 59,
    $"Ecran SOC-018 CA07 : l'ecran lit l'etat, le journal ({vue.Journal.Length}), les 8 grands totaux et les 59 codes du greffe {Greffe}");
var vueCode = await EcranChaines.OuvrirAsync(ecran, Greffe, new FiltreJournal("np", 260, null, null));
var vuePurge = await EcranChaines.PurgerAsync(ecran, Greffe, new DemandePurge(DateTime.Now.Year));
var vueControle = await EcranChaines.ControlerAsync(ecran, Greffe, tout);
var vueAgent = await EcranChaines.OuvrirAsync(ClientEcran(Agent), Greffe, tout);
var vueDemo = await EcranChaines.OuvrirAsync(null, null, tout);
Exiger(vueCode.Journal is [{ Code: 260 }] && vuePurge is { Echec: true } && vuePurge.Message!.Contains("archive fiscale")
       && vueControle is { Echec: false } && vueControle.Message!.StartsWith("Intégrité vérifiée", StringComparison.Ordinal)
       && vueAgent is { Mode: "refuse", Journal.Length: 0 } && vueAgent.Message!.Contains("facturation") && vueDemo.Mode == "demo",
    $"Ecran SOC-018 CA07 : filtre par code (260 seul), purge refusee sans archive en phrase lisible, controle (« {vueControle.Message} »), " +
    $"agent sans facturation refuse, demonstration sans serveur");

// =====================================================================================================================
// CA04 : une ligne signee modifiee directement en base est detectee (90) ; un trou dans la sequence aussi (95)
// =====================================================================================================================
Console.WriteLine("--- SOC-018 CA04 : alterations directes en base");
var quatreVingtDix = await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = 90");
var quatreVingtQuinze = await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = 95");
await Executer(v3.Connexion, """
    DISABLE TRIGGER soc.FactureSignee_Inalterable ON soc.FactureSignee;
    UPDATE soc.FactureSignee SET MontantTtc = MontantTtc - 1 WHERE Seq = 2;
    ENABLE TRIGGER soc.FactureSignee_Inalterable ON soc.FactureSignee;
    """);
var controle4 = await Controler();
var fac = controle4.Chaines.Single(c => c.Code == ChainesSignees.Factures);
var detail90 = await Scalaire<string>(v3.Connexion, "SELECT TOP (1) CONCAT(Chaine, '|', Detail) FROM soc.EvenementTechnique WHERE Code = 90 ORDER BY Id DESC");
Exiger(fac.Defauts is [var defaut] && defaut.Contains("maillon 2") && fac.Ruptures.Length == 0 && fac.Valides == 2
       && controle4.Chaines.Where(c => c.Code != ChainesSignees.Factures).All(c => c.Defauts.Length == 0 && c.Ruptures.Length == 0)
       && await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = 90") == quatreVingtDix + 1 && detail90.StartsWith("JNP|", StringComparison.Ordinal),
    $"SOC-018 CA04 : la facture 2 modifiee directement en base est detectee, et un evenement 90 est ecrit et signe au journal non purgeable " +
    $"(« {detail90} ») ; les autres chaines restent integres");
await Executer(v3.Connexion, """
    DISABLE TRIGGER soc.EncaissementSigne_Inalterable ON soc.EncaissementSigne;
    DELETE FROM soc.EncaissementSigne WHERE Seq IN (5, 10);
    ENABLE TRIGGER soc.EncaissementSigne_Inalterable ON soc.EncaissementSigne;
    """);
var controle5 = await Controler();
var enc = controle5.Chaines.Single(c => c.Code == ChainesSignees.Encaissements);
Exiger(enc.Defauts.Length == 0 && enc.Ruptures.Any(r => r.Contains("le maillon 5 manque")) && enc.Ruptures.Any(r => r.Contains("dernier maillon émis (10)"))
       && await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = 95") >= quatreVingtQuinze + 2,
    $"SOC-018 CA04 : un trou dans la serie des encaissements (le 5) et la disparition du dernier (le 10) ecrivent chacun un evenement 95, " +
    $"sans les compter comme alteration ({string.Join(" | ", enc.Ruptures)})");
await Executer(v3.Connexion, """
    DISABLE TRIGGER soc.ArchiveFiscale_Inalterable ON soc.ArchiveFiscale;
    UPDATE soc.ArchiveFiscale SET Contenu = Contenu + 0x20 WHERE Exercice = 2025;
    ENABLE TRIGGER soc.ArchiveFiscale_Inalterable ON soc.ArchiveFiscale;
    """);
var controle6 = await Controler();
Exiger(controle6.Archives.Any(x => x.Contains("exercice 2025") && x.Contains("empreinte")),
    $"SOC-018 CA04 : une archive fiscale modifiee en base est detectee (evenement 90, « defaut d'integrite ... dans une archive fiscale ») : {string.Join(" | ", controle6.Archives)}");

// =====================================================================================================================
// Copie anonymisee (SOC-021, lot 6) : un controle qui la voit ne vaut pas preuve ; la repetition du passage (SOC-025)
// s'y joue, la signature y reste donc possible
// =====================================================================================================================
var alertesAvantCopie = await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code IN (90, 95)");
await Executer(v3.Connexion, "EXEC sys.sp_addextendedproperty @name = N'RIGV3_ANONYMISATION', @value = N'copie du banc SocleChainesCheck';");
var controleCopie = await Controler();
var signeeCopie = await SurV3((cn, tx) => chaines.SignerFactureAsync(cn, tx, f3 with { Numero = "V3-26-COPIE" }));
Exiger(controleCopie is { ValeurProbante: false, EvenementsEcrits.Length: 0 } && controleCopie.Reserve!.Contains("copie anonymisée")
       && controleCopie.Chaines.Any(c => c.Defauts.Length + c.Ruptures.Length > 0)
       && await Compter("SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code IN (90, 95)") == alertesAvantCopie,
    "SOC-021 (decision du lot 6) : sur une copie anonymisee, le controle trouve des defauts mais se dit sans valeur probante et n'ecrit ni 90 ni 95");
Exiger(signeeCopie.Seq == 4,
    $"SOC-025 (jumeau) : sur la copie, la signature reste possible pour repeter le passage (facture signee au maillon {signeeCopie.Seq})");

// =====================================================================================================================
// CA02 : aucune ligne signee de la V2 n'est copiee, modifiee ni signee de nouveau
// =====================================================================================================================
Console.WriteLine("--- SOC-018 CA02 : les chaines de la V2 intactes");
var empreinteV2Apres = await Scalaire<string>(v2.Connexion, EmpreinteV2);
Exiger(empreinteV2Apres == empreinteV2Avant,
    $"SOC-018 CA02 : apres le passage, les signatures, les clotures, la purge et les controles de la V3, les chaines de la base V2 du greffe " +
    $"sont identiques a l'octet (empreinte {empreinteV2Avant[..40]}…)");
var versLaV2 = codeChaines.SelectMany(f => SansCommentaire(f).Select(l => (f, l)))
    .Where(x => Regex.IsMatch(x.l, @"OuvrirBaseV2Async|LectureV2|\bdbo\.|JET_NP|JET_P\b|FACTURE_DUPLICATA|GRAND_TOTAUX", RegexOptions.IgnoreCase)).ToList();
Exiger(versLaV2.Count == 0,
    $"SOC-018 CA02 : aucun code des chaines n'ouvre la base V2 ni ne nomme une table signee de la V2 (hors commentaires){(versLaV2.Count > 0 ? " : " + versLaV2[0].l.Trim() : "")}");
Reste("SOC-018 CA02 : « elles restent verifiables dans la V2 » se prouve par CertifExpressoApp (mode 10) sur la vraie base V2 de chaque greffe -- " +
      "le poste n'a ni ces bases ni la cle ECDSA de la V2 ; la V3 n'y touche pas (empreinte ci-dessus)");

// =====================================================================================================================
// T02 : la cle d'un greffe, en production, est une cle CNG non exportable (comme la V2)
// =====================================================================================================================
if (OperatingSystem.IsWindows())
{
    var cng = new CoffreClesCng();
    var essai = "BANC" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    var cle = (ECDsaCng)cng.CleDuGreffe(essai);
    try
    {
        var exporter = await Lever(() => { cle.ExportParameters(includePrivateParameters: true); return Task.CompletedTask; });
        var preimage = Preimages.Encaissement(1, null, "E", new DateTime(2026, 1, 1), 1m);
        using var publique = Signatures.ClePublique(cle.ExportSubjectPublicKeyInfo());
        Exiger(cle.Key.ExportPolicy == CngExportPolicies.None && exporter is CryptographicException
               && Signatures.Verifier(publique, preimage, Signatures.Signer(cle, preimage)) && cng.Conteneur(essai) == CoffreClesCng.PrefixeConteneur + essai,
            $"SOC-018 T02 : en production, la cle du greffe vit dans un conteneur CNG non exportable ({cng.Conteneur(essai)}) ; elle signe, sa cle publique verifie, " +
            "sa cle privee ne sort pas");
    }
    finally { cle.Key.Delete(); }
}

Reste("SOC-018 CA05 : la cloture quotidienne automatique (job de la V2) n'est pas planifiee : la route des clotures attend le traitement de fond qui l'appellera (SOC-011)");
Reste("SOC-018 : la certification NF525 de la V3 elle-meme (auditeur, attestation) n'est pas un banc : SOC-019, lot 13");
Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "VERT : SOC-018 tient ses criteres." : $"ROUGE : {echecs.Count} controle(s) en echec.");
foreach (var e in echecs) Console.WriteLine("  - " + e);
return echecs.Count == 0 ? 0 : 1;

/// <summary>La cle de signature du banc : en memoire, une par greffe, jamais dans le magasin de cles de la personne qui lance le banc.</summary>
internal sealed class CoffreMemoire : ICoffreCles
{
    private readonly ConcurrentDictionary<string, ECDsa> cles = new();
    public ECDsa CleDuGreffe(string codeGreffe) => cles.GetOrAdd(codeGreffe, _ => ECDsa.Create(ECCurve.NamedCurves.nistP256));
    public string Conteneur(string codeGreffe) => $"memoire du banc SocleChainesCheck ({codeGreffe})";
}
