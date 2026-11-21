using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using RecopieTransfertCheck;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Passage;
using RigV3.Serveur;
using RigV3.Serveur.Recopie;
using RigV3.Serveur.Socle.Chaines;
using RigV3.Serveur.Socle.Greffes;
using SoclePassageCheck;

// Banc du lot 13 du plan de reconstruction, phase 2 (2026-11-21) : SOC-019, SOC-022, SOC-024, SOC-025, SOC-026 du carnet
// socle.json. Un controle par critere, qui cite son id ; tout refus a son jumeau accepte.
//
//  0. Bases jetables : une base V2 modele tiree de RIG_DEV (l'extrait du banc du transfert, lot 7) avec les trois
//     programmes de numerotation d'origine (copies de tests\NumerotationV2Check\v2), une base V3 modele a jour des
//     migrations, un catalogue commun repris de la copie de COMMUN_RIG (40 greffes). Chaque scenario en restaure des
//     copies : une base par greffe, neuve.
//  A. Passage d'un greffe BETA (Chartres) par le moteur, des agents travaillant dans la V2 pendant ce temps : etapes dans
//     l'ordre, chaines ouvertes (260 en premier), transfert fini, bascule, numero donne par la V3 a un agent de la V2 sans
//     coupure de sa session, demandes en cours intactes, catalogue, aiguillage, V2 touchee par la seule bascule.
//     Puis le RETOUR (outil, « retour ») : programmes d'origine remis, date du catalogue retiree, la V2 numerote seule.
//  B. Greffe HORS BETA (Bourges) : refus d'entree (base d'un autre poste, base V3 deja servie), echec reel de la bascule
//     (un agent numerote entre le transfert et la bascule), echec apres la bascule (catalogue), tous deux revenus a la V2 ;
//     puis passage par l'OUTIL, garde levee, retour refuse (NUM-022 CA03).
//  C. Jour J et repetitions generales : plan des 37 greffes a leur heure locale, trois repetitions (Lyon, Papeete) par
//     l'outil, un echec simule sur un greffe sans effet sur l'autre, tableau de bord, regles de SOC-025 et contre-epreuves.
//  D. Criteres de la beta (SOC-022, SOC-019 CA01, CA05) sur le carnet reel, puis sur un dossier complet ; chaque critere
//     retire un a un refuse le demarrage ; criteres de fin ; regle de classement des defauts.
//  E. Certification (SOC-019) : dossier a remettre confronte au code des chaines ; suivi d'une version qui touche une
//     fonction certifiee.
//  F. Formation (SOC-026) : comptage des agents par metier sur RIG_DEV et sur la copie, sans donnee personnelle ;
//     supports tires des ecrans de la V3, vocabulaire des textes garde.
// Il lui faut « docker compose up -d » et la lecture de RIG_DEV (autre serveur : RIGV3_RIG_DEV).
// Doc : docs/reconstruction/socle-exploitation.md (« Passage d'un greffe » et suivantes).

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var migrations = Path.Combine(racine, "Base", "migrations");
var scriptsV2 = Path.Combine(racine, "Base", "v2");
var copiesV2 = Path.Combine(racine, "tests", "NumerotationV2Check", "v2");
var carnetReel = Environment.GetEnvironmentVariable("RIGV3_CARNET") is { Length: > 0 } cr ? cr
    : @"C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet";
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

string SansPool(string c, string poste = "POSTE-BANC") => new SqlConnectionStringBuilder(c) { Pooling = false, WorkstationID = poste }.ConnectionString;
async Task Executer(string chaine, string sql)
{
    await using var cn = new SqlConnection(SansPool(chaine));
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
    await cmd.ExecuteNonQueryAsync();
}
async Task<object?> Valeur(string chaine, string sql)
{
    await using var cn = new SqlConnection(SansPool(chaine));
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
    var v = await cmd.ExecuteScalarAsync();
    return v is DBNull ? null : v;
}
async Task<long> Nombre(string chaine, string sql) => Convert.ToInt64(await Valeur(chaine, sql) ?? 0L);
async Task<List<string>> Lignes(string chaine, string sql)
{
    var l = new List<string>();
    await using var cn = new SqlConnection(SansPool(chaine));
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
        l.Add(string.Join("|", Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "NULL" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
    return l;
}

// Le serveur des bancs, et la facon de s'y authentifier telle que la recoit le catalogue (Greffes:Authentification).
var serveurBancs = BaseJetable.ServeurDesBancs();
var sb = new SqlConnectionStringBuilder(serveurBancs);
var instance = sb.DataSource;
sb.Remove("Data Source");
var authentification = sb.ConnectionString;
var master = SansPool(new SqlConnectionStringBuilder(serveurBancs) { InitialCatalog = "master" }.ConnectionString);
var sessionEchecs = "rigv3_banc_echecs_" + Guid.NewGuid().ToString("N")[..8];
var temp = Path.Combine(Path.GetTempPath(), "SoclePassageCheck_" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(temp);
var coffre = new CoffreMemoire();
var bases = new List<BaseJetable>();
var sauvegardes = new List<string>();

// ====================================================================================================
// 0. RIG_DEV lisible ; les bases modeles
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

async Task<BaseJetable> NouvelleBase(string nom)
{
    var b = await BaseJetable.CreerAsync(nom);
    bases.Add(b);
    return b;
}
var dossierSauvegarde = Convert.ToString(await Valeur(master, "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(260));"))!;
string Fichier(string dossier, string nom) => dossier.TrimEnd('/', '\\') + (dossier.Contains('/') ? "/" : "\\") + nom;
async Task<string> Sauvegarder(BaseJetable b)
{
    var f = Fichier(dossierSauvegarde, b.Nom + ".bak");
    await Executer(master, $"BACKUP DATABASE [{b.Nom}] TO DISK = N'{f}' WITH INIT, COPY_ONLY;");
    sauvegardes.Add(f);
    return f;
}
// Une copie d'une base modele : une base jetable restauree de sa sauvegarde (meme contenu, autre nom, autres fichiers).
async Task<BaseJetable> Copie(string nom, string sauvegarde, BaseJetable modele)
{
    var b = await NouvelleBase(nom);
    var fichiers = await Lignes(master, $"SELECT name, type_desc, physical_name FROM sys.master_files WHERE database_id = DB_ID(N'{modele.Nom}')");
    var deplacements = fichiers.Select(l => l.Split('|')).Select(f =>
    {
        var dossier = f[2][..Math.Max(f[2].LastIndexOf('/'), f[2].LastIndexOf('\\'))];
        return $"MOVE N'{f[0]}' TO N'{Fichier(dossier, b.Nom + (f[1] == "ROWS" ? ".mdf" : "_log.ldf"))}'";
    });
    await Executer(master, $"ALTER DATABASE [{b.Nom}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                           $"RESTORE DATABASE [{b.Nom}] FROM DISK = N'{sauvegarde}' WITH REPLACE, RECOVERY, {string.Join(", ", deplacements)}; " +
                           $"ALTER DATABASE [{b.Nom}] SET MULTI_USER;");
    return b;
}

var chrono0 = Stopwatch.StartNew();
var reglesServeur = ReglesTransfert.Decouvrir();
var entieres = reglesServeur.Regles.Where(r => r.Sorte == SorteRegle.Reprise).Select(r => r.Table).Append("CATEGORIE").ToList();
var extraits = new Dictionary<string, string>
{
    ["AFFAIRE"] = "SELECT TOP (300) * FROM dbo.AFFAIRE ORDER BY AFFRE_ID_AFFRE DESC",
    ["DEMANDE"] = "SELECT * FROM dbo.DEMANDE WHERE DMND_ID_AFFRE IN (SELECT TOP (300) AFFRE_ID_AFFRE FROM dbo.AFFAIRE ORDER BY AFFRE_ID_AFFRE DESC)",
    ["ACTION_RCS"] = "SELECT TOP (300) * FROM dbo.ACTION_RCS ORDER BY ACTRC_ID_ACTRC DESC",
    ["ADRESSE_BASE"] = "SELECT TOP (500) * FROM dbo.ADRESSE_BASE ORDER BY ADRBS_ID_ADRBS DESC",
    ["ARRIVEE"] = "SELECT TOP (500) * FROM dbo.ARRIVEE ORDER BY ARRV_ID_ARRV DESC",
    ["CLIENT"] = "SELECT TOP (300) * FROM dbo.CLIENT ORDER BY CLIEN_ID_CLIEN DESC",
    ["DOSSIER_RCS"] = "SELECT TOP (300) * FROM dbo.DOSSIER_RCS ORDER BY DSSRC_ID_DSRCS DESC",
    ["INSCRIPTION"] = "SELECT TOP (300) * FROM dbo.INSCRIPTION ORDER BY INEDT_ID_INSCR DESC",
};
var modeleV2 = await NouvelleBase("PassageModeleV2");
var extrait = await ExtraitRigDev.ConstruireAsync(chaineRigDev, modeleV2.Connexion, entieres, extraits, Info);
// Les trois programmes de numerotation d'origine, tels que la V2 les a (QUOTED_IDENTIFIER OFF : ils ecrivent « "9999" »).
await using (var cn = new SqlConnection(SansPool(modeleV2.Connexion)))
{
    await cn.OpenAsync();
    foreach (var f in new[] { "GENERER_NUMERO.sql", "CHERCHER_NUMERO.sql", "RESTITUER_NUMERO.sql" })
    {
        await using (var reglages = new SqlCommand("SET QUOTED_IDENTIFIER OFF; SET ANSI_NULLS ON;", cn)) await reglages.ExecuteNonQueryAsync();
        foreach (var lot in ScriptSqlcmd.Lots(Path.Combine(copiesV2, f), new Dictionary<string, string>()))
        {
            await using var cmd = new SqlCommand(lot, cn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
    await using var fin = new SqlCommand("SET QUOTED_IDENTIFIER ON; GRANT EXECUTE ON dbo.GENERER_NUMERO TO public;", cn);
    await fin.ExecuteNonQueryAsync();
}
var programmesOrigine = await Lignes(modeleV2.Connexion,
    "SELECT o.name, m.definition, m.uses_quoted_identifier FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id " +
    "WHERE o.name IN ('GENERER_NUMERO', 'CHERCHER_NUMERO', 'RESTITUER_NUMERO') ORDER BY o.name");
var sauvegardeV2 = await Sauvegarder(modeleV2);
var modeleV3 = await NouvelleBase("PassageModeleV3");
await Migrateur.AppliquerAsync(modeleV3.Connexion, migrations);
var sauvegardeV3 = await Sauvegarder(modeleV3);
Info($"bases modeles en {chrono0.Elapsed.TotalSeconds:0} s : V2 {extrait.Lignes.Values.Sum()} lignes dans {extrait.Lignes.Count} tables tirees de RIG_DEV " +
     $"+ 3 programmes d'origine ; V3 a jour des {Migrateur.Lire(migrations).Count} migrations");
Exiger(programmesOrigine.Count == 3 && extrait.Lignes.ContainsKey("NUMERO_METIER") && extrait.Lignes["NUMERO_METIER"] > 0,
    $"base V2 modele : NUMERO_METIER de RIG_DEV ({extrait.Lignes.GetValueOrDefault("NUMERO_METIER")} lignes) et les 3 programmes de numerotation d'origine");

// Le catalogue commun : la copie de COMMUN_RIG.dbo.GREFFE (40 greffes exploites) reprise par la V3 (SOC-007).
var communRig = await NouvelleBase("PassageCommunRig");
await Executer(communRig.Connexion, await File.ReadAllTextAsync(Path.Combine(racine, "tests", "SocleGreffesCheck", "commun_rig_greffe.sql")));
async Task<BaseJetable> NouvelleCommune(string nom)
{
    var c = await Copie(nom, sauvegardeV3, modeleV3);
    await using var v2 = new SqlConnection(SansPool(communRig.Connexion));
    await using var v3 = new SqlConnection(SansPool(c.Connexion));
    await v2.OpenAsync();
    await v3.OpenAsync();
    await RepriseCatalogue.ReprendreAsync(v2, v3, "banc.passage");
    return c;
}
async Task Pointer(BaseJetable commune, string code, BaseJetable v2, BaseJetable v3) => await Executer(commune.Connexion,
    $"UPDATE soc.Greffe SET Serveur = '{instance}', BaseV2 = '{v2.Nom}', BaseV3 = '{v3.Nom}', ModifiePar = 'banc.passage', ModifieLe = SYSDATETIME() " +
    $"WHERE CodeGreffe = '{code}';");
async Task<int> ProgrammesBascules(BaseJetable v2) => (int)await Nombre(v2.Connexion,
    "SELECT COUNT(*) FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id " +
    "WHERE o.name IN ('GENERER_NUMERO', 'CHERCHER_NUMERO', 'RESTITUER_NUMERO') AND m.definition LIKE '%rigv3.%'");
async Task<bool> ProgrammesDOrigine(BaseJetable v2) => (await Lignes(v2.Connexion,
    "SELECT o.name, m.definition, m.uses_quoted_identifier FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id " +
    "WHERE o.name IN ('GENERER_NUMERO', 'CHERCHER_NUMERO', 'RESTITUER_NUMERO') ORDER BY o.name")).SequenceEqual(programmesOrigine);
// Les objets dbo de la V2 (nom, type, date de modification) : ce que la bascule a le droit de changer est exclu.
async Task<List<string>> ObjetsV2(BaseJetable v2) => await Lignes(v2.Connexion,
    "SELECT o.name, o.type, CONVERT(varchar(30), o.modify_date, 126) FROM sys.objects o WHERE o.schema_id = SCHEMA_ID('dbo') AND o.is_ms_shipped = 0 " +
    "AND o.name NOT IN ('GENERER_NUMERO', 'CHERCHER_NUMERO', 'RESTITUER_NUMERO', 'NUMERO_METIER', 'TR_NUMERO_METIER_RIGV3') " +
    "AND o.parent_object_id <> ISNULL(OBJECT_ID('dbo.NUMERO_METIER'), 0) ORDER BY o.name");
// Un agent de la V2 demande un numero, comme l'injecteur d'avis (EdiOld\VB6\InjecteurEdi_AVISIR\TraitementAvis.cls:180-199).
async Task<string?> Numeroter(SqlConnection agent, string type)
{
    await using var cmd = new SqlCommand("GENERER_NUMERO", agent) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
    cmd.Parameters.Add("@TYPE_NUMERO_METIER", SqlDbType.VarChar, 5).Value = type;
    var n = cmd.Parameters.Add("@NUMERO_RETOURNE", SqlDbType.VarChar, 40);
    n.Direction = ParameterDirection.InputOutput;
    n.Value = "";
    await cmd.ExecuteNonQueryAsync();
    return n.Value as string;
}
// Une serie simple de l'annee du serveur (ni jour, ni redirection, ni numeros rendus) : celle que l'agent fait avancer.
var serie = (string?)await Valeur(modeleV2.Connexion,
    "SELECT TOP (1) NMMTR_TYPE_METIER FROM dbo.NUMERO_METIER WHERE NMMTR_ANNEE = YEAR(SYSDATETIME()) AND ISNULL(NMMTR_ATTRIBUTION_ERRONEE, 0) = 0 " +
    "AND ISNULL(NMMTR_GESTION_NUM_ERR, 0) = 0 AND ISNULL(NMMTR_FILE_REDIRECTION, '') = '' AND NMMTR_STRUCTURE_NUMERO NOT LIKE '%Q%' " +
    "AND NMMTR_CHRONO IS NOT NULL GROUP BY NMMTR_TYPE_METIER HAVING COUNT(*) = 1 ORDER BY NMMTR_TYPE_METIER");
Exiger(serie is not null, $"une serie de numeros de l'annee du serveur dans NUMERO_METIER de RIG_DEV pour faire numeroter un agent : {serie}");
async Task<long> ChronoV2(BaseJetable v2) => await Nombre(v2.Connexion,
    $"SELECT NMMTR_CHRONO FROM dbo.NUMERO_METIER WHERE NMMTR_TYPE_METIER = '{serie}' AND NMMTR_ANNEE = YEAR(SYSDATETIME()) AND ISNULL(NMMTR_ATTRIBUTION_ERRONEE, 0) = 0");

// L'heure du SERVEUR fait foi pour les scripts (fin de beta) ; demain 07:00 pour un passage dont l'heure n'est pas atteinte.
var demain = DateOnly.FromDateTime(DateTime.Now.AddDays(1));
var passageDemain = demain.ToDateTime(new TimeOnly(7, 0));
var heureServeur = (DateTime)(await Valeur(master, "SELECT CAST(SYSDATETIME() AS datetime2(0));"))!;
var finBeta = heureServeur.AddDays(30);
string Iso(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

try
{
var commune = await NouvelleCommune("PassageCommune");
var fiches = (await Lignes(commune.Connexion, "SELECT CodeGreffe, Nom, Serveur, EstBeta, EstOutreMer, ISNULL(Fuseau, '') FROM soc.Greffe ORDER BY CodeGreffe"))
             .Select(l => l.Split('|')).ToDictionary(x => x[0]);
Exiger(fiches.Count == 40 && fiches.Values.Count(f => f[3] == "True") == 3 && fiches.Values.Count(f => f[4] == "True") == 6,
    $"catalogue commun repris de COMMUN_RIG : {fiches.Count} greffes, {fiches.Values.Count(f => f[3] == "True")} beta, {fiches.Values.Count(f => f[4] == "True")} outre-mer");

OptionsPassage Options(BaseJetable c, string code) => new(SansPool(c.Connexion), code)
{
    Coffre = coffre, Authentification = authentification, DossierScripts = scriptsV2, SessionEchecs = sessionEchecs,
    Auteur = "banc.passage", Suivre = Info,
};
string[] Cli(string commande, BaseJetable c, string code, params string[] plus)
    => [commande, "--commune", SansPool(c.Connexion), "--greffe", code, "--authentification", authentification, "--scripts", scriptsV2,
        "--session-echecs", sessionEchecs, .. plus];

// ====================================================================================================
// A. Passage d'un greffe beta (Chartres, 2801), des agents au travail dans la V2
// ====================================================================================================
var v2A = await Copie("PassageChartresV2", sauvegardeV2, modeleV2);
var v3A = await Copie("PassageChartresV3", sauvegardeV3, modeleV3);
await Pointer(commune, "2801", v2A, v3A);
var objetsAvant = await ObjetsV2(v2A);
await using var agent = new SqlConnection(SansPool(v2A.Connexion, "POSTE-AGENT-CHARTRES"));
await agent.OpenAsync();
var spid = Convert.ToInt32(await new SqlCommand("SELECT @@SPID", agent).ExecuteScalarAsync());
var demandeAgent = await Nombre(v2A.Connexion, "SELECT MAX(DMND_ID_DMND) FROM dbo.DEMANDE");
var journalV3Avant = 0L;
string? numeroPendant = null;
long chronoV3AvantNumero = 0, chronoV3ApresNumero = 0;
var rapportA = Path.Combine(temp, "passages", "passage-2801.json");
var a = await MoteurPassage.PasserAsync(Options(commune, "2801") with
{
    PassageLocal = passageDemain, FinBeta = finBeta, FichierRapport = rapportA,
    ApresEtape = async (etape, ct) =>
    {
        if (etape == EtapesPassage.Transfert)
        {
            // L'agent continue sa saisie pendant le passage : la V2 reste ouverte (le transfert lit un instantane).
            await using var u = new SqlCommand($"UPDATE dbo.DEMANDE SET DMND_LIBELLE = 'saisie pendant le passage' WHERE DMND_ID_DMND = {demandeAgent}", agent);
            await u.ExecuteNonQueryAsync(ct);
        }
        if (etape == EtapesPassage.Bascule)
        {
            // Bascule faite : le meme agent, sur la meme session, demande un numero ; il vient de la V3.
            journalV3Avant = await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM num.Journal");
            chronoV3AvantNumero = await Nombre(v3A.Connexion, $"SELECT Chrono FROM num.Compteur WHERE Type = '{serie}' AND Annee = YEAR(SYSDATETIME()) AND Jour = 0");
            numeroPendant = await Numeroter(agent, serie!);
            chronoV3ApresNumero = await Nombre(v3A.Connexion, $"SELECT Chrono FROM num.Compteur WHERE Type = '{serie}' AND Annee = YEAR(SYSDATETIME()) AND Jour = 0");
        }
    },
});
Console.WriteLine(RigV3.Passage.LigneDeCommande.Texte(a));
Exiger(a.Issue == IssuesPassage.Passe && IssuesPassage.Code(a.Issue) == 0,
    $"SOC-024 T02 : le passage de Chartres (beta) par le moteur rend « {a.Issue} » (code {IssuesPassage.Code(a.Issue)}) en {a.DureeMs / 1000.0:0.0} s");
Exiger(a.Etapes.Select(e => e.Nom).SequenceEqual(EtapesPassage.Ordre) && a.Etapes.All(e => e.Etat == EtatsEtape.Faite)
       && a.Etapes.Zip(a.Etapes.Skip(1)).All(p => p.First.DebutUtc <= p.Second.DebutUtc),
    $"SOC-024 CA02 : les etapes se suivent dans l'ordre, chacune faite et chronometree : {string.Join(" > ", a.Etapes.Select(e => $"{e.Nom} {e.DureeMs} ms"))}");
var premierMaillon = await Valeur(v3A.Connexion, "SELECT TOP (1) Code FROM soc.EvenementTechnique WHERE Chaine = 'JNP' ORDER BY Seq");
Exiger(Convert.ToInt32(premierMaillon) == 260 && await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM soc.Certification WHERE CodeGreffe = '2801'") == 1,
    "SOC-024 CA02 / SOC-018 CA01 : les chaines signees de la V3 sont ouvertes au passage, l'evenement 260 en premier maillon du journal non purgeable");
Exiger(Convert.ToString(await Valeur(v3A.Connexion, "SELECT Etat FROM rec.Transfert")) == "fini" && a.LignesTransferees > 0
       && await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM v2.AFFAIRE") == await Nombre(v2A.Connexion, "SELECT COUNT(*) FROM dbo.AFFAIRE"),
    $"SOC-024 CA02 / REC-001 : le transfert du passage est fini ({a.LignesTransferees} lignes ecrites, transfert {a.IdTransfert}), AFFAIRE recopiee entiere");
Exiger(await ProgrammesBascules(v2A) == 3 && Convert.ToString(await Valeur(v2A.Connexion, "SELECT BaseV3 FROM rigv3.Pont WHERE RetourLe IS NULL")) == v3A.Nom,
    "SOC-024 CA02 / NUM-002 : les 3 programmes de numerotation de la base V2 demandent leurs numeros a la base V3 du greffe");
var nouveauxJournal = await Lignes(v3A.Connexion, $"SELECT Evenement, Logiciel, Serie FROM num.Journal ORDER BY Id OFFSET {journalV3Avant} ROWS");
Exiger(numeroPendant is { Length: > 0 } && chronoV3ApresNumero == chronoV3AvantNumero + 1 && nouveauxJournal.Count == 1
       && nouveauxJournal[0].StartsWith("attribution|V2|", StringComparison.Ordinal) && await ChronoV2(v2A) == chronoV3ApresNumero,
    $"SOC-024 CA04 : apres la bascule, un agent de la V2 obtient « {numeroPendant} » de la V3 (compteur {serie} {chronoV3AvantNumero} -> {chronoV3ApresNumero}, " +
    $"journal {string.Join(", ", nouveauxJournal)}), recopie dans NUMERO_METIER");
var spidApres = agent.State == ConnectionState.Open ? Convert.ToInt32(await new SqlCommand("SELECT @@SPID", agent).ExecuteScalarAsync()) : -1;
Exiger(spidApres == spid && Convert.ToString(await Valeur(v2A.Connexion, $"SELECT DMND_LIBELLE FROM dbo.DEMANDE WHERE DMND_ID_DMND = {demandeAgent}")) == "saisie pendant le passage"
       && a.Etapes.Single(e => e.Nom == EtapesPassage.ControlesApres).Detail.Contains("demande(s) en cours d'avant toujours la"),
    $"SOC-024 CA04 : la session de l'agent (spid {spid}) n'est jamais coupee, sa saisie faite pendant le transfert est gardee, les demandes en cours d'avant sont toutes la");
Exiger((await ObjetsV2(v2A)).SequenceEqual(objetsAvant),
    $"SOC-024 CA04 : dans la base V2, seuls les programmes de numerotation et le pont ont change ({objetsAvant.Count} autres objets dbo intacts)");
var ficheA = (await Lignes(commune.Connexion, "SELECT CONVERT(varchar(16), PassageLocal, 126) FROM soc.Greffe WHERE CodeGreffe = '2801'")).Single();
var aigNouvelle = RegleAiguillage.Aiguiller("n", passageDemain, passageDemain, passageDemain);
var aigAncienne = RegleAiguillage.Aiguiller("a", passageDemain, passageDemain.AddMinutes(-1), passageDemain);
Exiger(ficheA == passageDemain.ToString("yyyy-MM-ddTHH:mm") && aigNouvelle.Logiciel == Logiciels.V3 && aigAncienne.Logiciel == Logiciels.V2
       && a.Etapes.Single(e => e.Nom == EtapesPassage.Aiguillage).Detail.Contains("traitee dans la V3"),
    $"SOC-024 CA02 : date de passage au catalogue ({ficheA}, heure locale) ; la creation d'affaires passe a la V3 a cette heure, une affaire d'avant reste a la V2");
var relu = RapportPassage.Lire(rapportA);
Exiger(relu.Issue == IssuesPassage.Passe && relu.DerniereEtapeFaite == EtapesPassage.FinDeGarde && relu.Etapes.Count == a.Etapes.Count,
    $"SOC-024 CA06 : le rapport du passage est ecrit au fil des etapes ({Path.GetFileName(rapportA)}) et se relit : derniere etape faite {relu.DerniereEtapeFaite}");

// Le retour d'un greffe beta (NUM-022, SOC-024 CA03 « repasse plus tard ») : par l'OUTIL.
var sortieRetour = new StringWriter();
var codeRetour = await RigV3.Passage.LigneDeCommande.ExecuterAsync(Cli("retour", commune, "2801"), sortieRetour, coffre);
Info(sortieRetour.ToString().Trim().Replace("\n", "\n        "));
var chronoV2AvantRetour = await ChronoV2(v2A);
var numeroApresRetour = await Numeroter(agent, serie!);
Exiger(codeRetour == 0 && await ProgrammesDOrigine(v2A) && await ChronoV2(v2A) == chronoV2AvantRetour + 1
       && await Nombre(v3A.Connexion, "SELECT COUNT(*) FROM sys.triggers WHERE name = 'TR_Compteur_RenduALaV2'") == 1
       && await Valeur(commune.Connexion, "SELECT PassageLocal FROM soc.Greffe WHERE CodeGreffe = '2801'") is null,
    $"SOC-024 CA03 / NUM-022 : « retour » (code {codeRetour}) remet les 3 programmes d'origine a l'identique, la V2 numerote seule " +
    $"(« {numeroApresRetour} »), la V3 ne donne plus de numero, la date de passage non atteinte est retiree du catalogue");

// ====================================================================================================
// B. Greffe hors beta (Bourges, 1801) : refus d'entree, echecs reels, revenus a la V2 ; puis passage par l'outil
// ====================================================================================================
var sortieB0 = new StringWriter();
var codeB0 = await RigV3.Passage.LigneDeCommande.ExecuterAsync(Cli("passer", commune, "1801", "--passage", Iso(passageDemain)), sortieB0, coffre);
Exiger(codeB0 == 2 && sortieB0.ToString().Contains("n'est pas sur ce poste"),
    $"SOC-024 T02 : un greffe dont l'instance n'est pas sur ce poste ({fiches["1801"][2]}) est refuse sans connexion ni ecriture (code {codeB0})");
// Un catalogue d'un autre poste : refuse AVANT de s'y connecter (sans quoi l'appel attendrait le delai de connexion), passage comme retour.
const string CommuneDistante = @"Server=SQL-PROD\PROD;Database=RIGV3_COMMUN;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=30";
var chronoDistant = Stopwatch.StartNew();
var sortieB0b = new StringWriter();
var codeB0b = await RigV3.Passage.LigneDeCommande.ExecuterAsync(["passer", "--commune", CommuneDistante, "--greffe", "8002"], sortieB0b, coffre);
var sortieB0c = new StringWriter();
var codeB0c = await RigV3.Passage.LigneDeCommande.ExecuterAsync(["retour", "--commune", CommuneDistante, "--greffe", "8002"], sortieB0c, coffre);
Exiger(codeB0b == 2 && codeB0c == 2 && sortieB0b.ToString().Contains("le catalogue (SQL-PROD") && sortieB0c.ToString().Contains("le catalogue (SQL-PROD")
       && chronoDistant.Elapsed < TimeSpan.FromSeconds(10),
    $"SOC-024 T02 : un catalogue qui n'est pas sur ce poste est refuse sans s'y connecter, pour « passer » ({codeB0b}) comme pour « retour » ({codeB0c}), " +
    $"en {chronoDistant.ElapsedMilliseconds} ms");
var v2B = await Copie("PassageBourgesV2", sauvegardeV2, modeleV2);
// La base V3 de Chartres, deja servie, donnee a Bourges : le catalogue refuse qu'elle serve deux greffes a la fois
// (UQ_Greffe_BaseV3), Chartres la rend d'abord.
await Executer(commune.Connexion, "UPDATE soc.Greffe SET BaseV3 = 'RIGV3_CHARTRES', ModifiePar = 'banc.passage', ModifieLe = SYSDATETIME() WHERE CodeGreffe = '2801'");
await Pointer(commune, "1801", v2B, v3A);
var sortieB1 = new StringWriter();
var codeB1 = await RigV3.Passage.LigneDeCommande.ExecuterAsync(Cli("passer", commune, "1801", "--passage", Iso(passageDemain)), sortieB1, coffre);
Exiger(codeB1 == 2 && sortieB1.ToString().Contains("a deja servi") && await ProgrammesDOrigine(v2B),
    $"SOC-024 T02 : une base V3 qui a deja servi (chaines ouvertes, transfert fait) est refusee aux controles d'entree, rien n'est touche (code {codeB1})");

// B1. Un agent numerote dans la V2 entre le transfert (compteurs repris) et la bascule : la bascule refuse.
var v3B1 = await Copie("PassageBourgesV3a", sauvegardeV3, modeleV3);
await Pointer(commune, "1801", v2B, v3B1);
await using var agentB = new SqlConnection(SansPool(v2B.Connexion, "POSTE-AGENT-BOURGES"));
await agentB.OpenAsync();
var b1 = await MoteurPassage.PasserAsync(Options(commune, "1801") with
{
    PassageLocal = passageDemain,
    ApresEtape = async (etape, _) => { if (etape == EtapesPassage.Transfert) await Numeroter(agentB, serie!); },
});
Console.WriteLine(RigV3.Passage.LigneDeCommande.Texte(b1));
var etapeBascule = b1.Etapes.Single(e => e.Nom == EtapesPassage.Bascule);
Exiger(b1.Issue == IssuesPassage.RevenuV2 && etapeBascule.Etat == EtatsEtape.Echec && etapeBascule.Detail.Contains("ne sont pas ceux")
       && b1.Etapes.Where(e => EtapesPassage.Ordre.SkipWhile(n => n != EtapesPassage.ControlesApres).Contains(e.Nom)).All(e => e.Etat == EtatsEtape.NonJouee)
       && await ProgrammesDOrigine(v2B) && await Valeur(commune.Connexion, "SELECT PassageLocal FROM soc.Greffe WHERE CodeGreffe = '1801'") is null && b1.BaseV3ARecreer,
    "SOC-024 CA03 : un agent numerote dans la V2 apres la reprise des compteurs -> la bascule refuse (« ne sont pas ceux de ... »), rien de ce qui suit n'est joue, " +
    "le greffe reste entierement sur la V2 (programmes d'origine, catalogue sans passage), la base V3 est dite a recreer");

// B2. Echec apres la bascule (le catalogue refuse : fuseau retire) : retour.sql remet la V2.
var v3B2 = await Copie("PassageBourgesV3b", sauvegardeV3, modeleV3);
await Pointer(commune, "1801", v2B, v3B2);
var b2 = await MoteurPassage.PasserAsync(Options(commune, "1801") with
{
    PassageLocal = passageDemain,
    ApresEtape = async (etape, _) =>
    {
        if (etape == EtapesPassage.ControlesApres)
            await Executer(commune.Connexion, "UPDATE soc.Greffe SET Fuseau = NULL, ModifiePar = 'banc.passage', ModifieLe = SYSDATETIME() WHERE CodeGreffe = '1801'");
    },
});
await Executer(commune.Connexion, $"UPDATE soc.Greffe SET Fuseau = '{Fuseaux.Metropole}', ModifiePar = 'banc.passage', ModifieLe = SYSDATETIME() WHERE CodeGreffe = '1801'");
Console.WriteLine(RigV3.Passage.LigneDeCommande.Texte(b2));
var retourB2 = b2.Etapes.SingleOrDefault(e => e.Nom == EtapesPassage.Retour);
Exiger(b2.Issue == IssuesPassage.RevenuV2 && b2.Etapes.Single(e => e.Nom == EtapesPassage.Bascule).Etat == EtatsEtape.Faite
       && b2.Etapes.Single(e => e.Nom == EtapesPassage.Catalogue).Etat == EtatsEtape.Echec
       && retourB2 is { Etat: EtatsEtape.Faite } && retourB2.Detail.Contains("Retour fait") && await ProgrammesDOrigine(v2B)
       && Convert.ToString(await Valeur(v2B.Connexion, "SELECT CASE WHEN RetourLe IS NULL THEN 'non' ELSE 'oui' END FROM rigv3.Pont")) == "oui",
    $"SOC-024 CA03 : une etape qui echoue APRES la bascule (catalogue : « {b2.Etapes.Single(e => e.Nom == EtapesPassage.Catalogue).Detail} ») -> retour.sql joue, " +
    "les programmes d'origine reviennent a l'identique : aucune etape a moitie faite");

// B3. Le passage par l'OUTIL, sur une base V3 neuve : passe ; hors beta, la garde est levee, le retour refuse.
var v2B3 = await Copie("PassageBourgesV2b", sauvegardeV2, modeleV2);
var v3B3 = await Copie("PassageBourgesV3c", sauvegardeV3, modeleV3);
await Pointer(commune, "1801", v2B3, v3B3);
var sortieB3 = new StringWriter();
var rapportB3 = Path.Combine(temp, "passages", "passage-1801.json");
var codeB3 = await RigV3.Passage.LigneDeCommande.ExecuterAsync(Cli("passer", commune, "1801", "--passage", Iso(passageDemain), "--rapport", rapportB3), sortieB3, coffre);
Info(sortieB3.ToString().Split('\n').Where(l => !l.StartsWith("           ", StringComparison.Ordinal)).Aggregate("", (x, y) => x + y + "\n        ").Trim());
var sortieB4 = new StringWriter();
var codeB4 = await RigV3.Passage.LigneDeCommande.ExecuterAsync(Cli("retour", commune, "1801"), sortieB4, coffre);
Exiger(codeB3 == 0 && await ProgrammesBascules(v2B3) == 3
       && await Nombre(v2B3.Connexion, "SELECT COUNT(*) FROM rigv3.CorpsOrigine") == 0 && await Nombre(v2B3.Connexion, "SELECT COUNT(*) FROM rigv3.CorpsOrigineArchive") == 3,
    $"SOC-024 T02 : « RigV3.Passage passer » fait passer Bourges (code {codeB3}) ; hors beta, la garde est levee a la fin (corps d'origine archive, NUM-022 CA03)");
// fix-ok: retour.sql refuse ici par « la beta est finie depuis le ... n'est plus deployable (NUM-022 CA03) » (fin-de-beta.sql pose FinBeta = maintenant),
// pas par « archive » : mesure au 2e lancement du banc le 2026-11-21 ; la marque commune des deux refus est « NUM-022 CA03 ».
Exiger(codeB4 == 2 && sortieB4.ToString().Contains("NUM-022 CA03") && await ProgrammesBascules(v2B3) == 3,
    $"NUM-022 CA03 : le retour d'un greffe hors beta passe est refuse (code {codeB4} : « {sortieB4.ToString().Split('\n').FirstOrDefault(l => l.Contains("refus"))?.Trim()} ») et ne change rien");
var sortieTableau = new StringWriter();
await RigV3.Passage.LigneDeCommande.ExecuterAsync(["tableau", "--dossier", Path.Combine(temp, "passages")], sortieTableau);
Info(sortieTableau.ToString().Trim().Replace("\n", "\n        "));
Exiger(sortieTableau.ToString().Contains("1801") && sortieTableau.ToString().Contains("2801") && sortieTableau.ToString().Contains(EtapesPassage.FinDeGarde),
    "SOC-024 CA06 : « tableau » montre pour chaque greffe son issue et l'etape atteinte, lus dans les rapports de passage");

// ====================================================================================================
// C. Jour J et repetitions generales
// ====================================================================================================
var sortiePlan = new StringWriter();
var codePlan = await RigV3.Passage.LigneDeCommande.ExecuterAsync(["jour-j", "--commune", SansPool(commune.Connexion), "--jour", demain.ToString("yyyy-MM-dd"),
                                                                 "--plan", Path.Combine(temp, "plan.json")], sortiePlan);
var plan = PlanJourJ.Calculer(await new CatalogueGreffes(new BaseRig(commune.Connexion), new ConfigurationBuilder().Build()).ListerAsync(), demain, PlanJourJ.OuvertureParDefaut);
var papeete = plan.Passages.Single(p => p.Code == "9871");
var ecartPapeete = (papeete.HeureParis - papeete.PassageLocal).TotalHours;
Exiger(codePlan == 0 && plan.Passages.Count == 37 && plan.Passages.Count(p => p.Vague == PlanJourJ.Metropole) == 31
       && plan.Passages.Count(p => p.Vague == PlanJourJ.OutreMer) == 6 && plan.DejaPasses.Count == 3
       && plan.Passages.All(p => p.PassageLocal == demain.ToDateTime(new TimeOnly(7, 0))) && plan.Passages[^1].Code == "9871" && ecartPapeete is 11 or 12
       && plan.Passages.Zip(plan.Passages.Skip(1)).All(x => x.First.PassageUtc <= x.Second.PassageUtc),
    $"SOC-024 CA01 : jour J du {demain:yyyy-MM-dd} : 37 greffes (31 de metropole, 6 d'outre-mer), chacun a 07:00 a SON heure locale, ranges par heure reelle " +
    $"({plan.Passages[0].Code} {plan.Passages[0].Nom} en premier a {plan.Passages[0].HeureParis:HH:mm} heure de Paris, Papeete en dernier a {papeete.HeureParis:HH:mm}, " +
    $"{ecartPapeete} h de decalage) ; les 3 beta deja passes");
using (var sites = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(racine, "Outils", "Livraison", "sites.json"))))
    Exiger(TimeOnly.Parse(sites.RootElement.GetProperty("ouverture").GetProperty("debut").GetString()!) == PlanJourJ.OuvertureParDefaut,
        $"SOC-024 CA01 : l'heure d'ouverture du plan ({PlanJourJ.OuvertureParDefaut:HH\\:mm}) est celle des fenetres de livraison (Outils\\Livraison\\sites.json)");

// Une repetition ne s'ecrit jamais sur la production (SOC-025 CA06) : un greffe du catalogue sur SQL-PROD -> refus avant tout passage.
var sortieProd = new StringWriter();
var codeProd = await RigV3.Passage.LigneDeCommande.ExecuterAsync(["repeter", "--commune", SansPool(commune.Connexion), "--jour", demain.ToString("yyyy-MM-dd"),
    "--numero", "1", "--rapport", Path.Combine(temp, "prod.json"), "--greffes", "0101"], sortieProd, coffre);
Exiger(codeProd == 2 && sortieProd.ToString().Contains("SOC-025 CA06") && !File.Exists(Path.Combine(temp, "prod.json")),
    $"SOC-025 CA06 : une repetition dont un greffe vise une instance de production ({fiches["0101"][2]}) est refusee avant tout passage (code {codeProd})");

// Trois repetitions (Lyon, metropole ; Papeete, outre-mer), chacune sur ses copies neuves et son catalogue d'essai.
var repetitions = Path.Combine(temp, "repetitions");
var codesRep = new List<int>();
for (var n = 1; n <= 3; n++)
{
    var communeR = await NouvelleCommune($"PassageRep{n}Commune");
    foreach (var code in new[] { "6901", "9871" })
        await Pointer(communeR, code, await Copie($"PassageRep{n}V2{code}", sauvegardeV2, modeleV2), await Copie($"PassageRep{n}V3{code}", sauvegardeV3, modeleV3));
    var sortieR = new StringWriter();
    codesRep.Add(await RigV3.Passage.LigneDeCommande.ExecuterAsync([
        "repeter", "--commune", SansPool(communeR.Connexion), "--jour", demain.ToString("yyyy-MM-dd"), "--numero", n.ToString(),
        "--rapport", Path.Combine(repetitions, $"repetition-{n}.json"), "--greffes", "6901,9871", "--authentification", authentification,
        "--scripts", scriptsV2, "--session-echecs", sessionEchecs, "--dossier-passages", Path.Combine(temp, $"passages-rep{n}"),
        .. (n == 1 ? new[] { "--simuler-echec", "6901:controles-apres" } : [])], sortieR, coffre));
    Info(string.Join("\n        ", sortieR.ToString().Split('\n').Where(l => l.StartsWith("Repetition") || l.StartsWith("  ")).Select(l => l.TrimEnd())));
}
var r1 = RapportRepetition.Lire(Path.Combine(repetitions, "repetition-1.json"));
var r2 = RapportRepetition.Lire(Path.Combine(repetitions, "repetition-2.json"));
var r3 = RapportRepetition.Lire(Path.Combine(repetitions, "repetition-3.json"));
var lyon1 = r1.Bases.Single(b => b.Greffe == "6901");
Exiger(codesRep.All(c => c == 0) && lyon1.Issue == IssuesPassage.RevenuV2 && lyon1.Etapes.Any(e => e.Nom == EtapesPassage.Retour && e.Etat == EtatsEtape.Faite)
       && r1.Bases.Single(b => b.Greffe == "9871").Issue == IssuesPassage.Passe && r1.Bases[0].Greffe == "6901",
    "SOC-024 CA03 / T06 : repetition 1, echec simule sur Lyon apres sa bascule -> Lyon revient a la V2 (retour.sql), Papeete, qui passe apres lui, passe quand meme");
Exiger(new[] { r2, r3 }.All(r => r.Bases.Count == 2 && r.Bases.All(b => b.Issue == IssuesPassage.Passe)),
    $"SOC-025 T02 : repetitions 2 et 3 : Lyon et Papeete passes ({string.Join(", ", new[] { r1, r2, r3 }.Select(r => $"r{r.Numero} {MoteurRepetition.TexteDuree(r.DureeMs)}"))})");

var sortieComp1 = new StringWriter();
var codeComp1 = await RigV3.Passage.LigneDeCommande.ExecuterAsync(["comparer", "--repetitions", repetitions], sortieComp1);
Exiger(codeComp1 == 1 && sortieComp1.ToString().Contains("MANQUE   SOC-025 CA02") && sortieComp1.ToString().Contains("6901"),
    $"SOC-025 CA02 : l'ecart de la repetition 1 (Lyon revenu a la V2) sans explication arrete la comparaison (code {codeComp1})");
var explications = Path.Combine(temp, "explications.json");
await File.WriteAllTextAsync(explications, JsonSerializer.Serialize(new[] { new Explication(1, "6901", "echec simule apres la bascule (SOC-024 T06) : retour joue") }));
var defautsRep = Path.Combine(temp, "defauts-repetitions.json");
await File.WriteAllTextAsync(defautsRep, JsonSerializer.Serialize(new[] { new DefautRepetition("REP-001", 1, "6901", EtapesPassage.ControlesApres, true, "#banc") }));
var sortieComp2 = new StringWriter();
var codeComp2 = await RigV3.Passage.LigneDeCommande.ExecuterAsync(["comparer", "--repetitions", repetitions, "--explications", explications,
                                                                  "--defauts", defautsRep, "--bases", "6901,9871"], sortieComp2);
Info(sortieComp2.ToString().Trim().Replace("\n", "\n        "));
Exiger(codeComp2 == 0 && Regex.Matches(sortieComp2.ToString(), @"^OK ", RegexOptions.Multiline).Count >= 10,
    "SOC-025 CA01-CA06 : avec l'ecart explique et le defaut rejoue, les 3 repetitions sont conformes : toutes les bases, rapport par etape, nuit tenue, " +
    "derniere sans ecart, durees stables, defaut rejoue, aucune ecriture en production");

// Contre-epreuves sur les memes rapports : chaque regle refuse son cas.
var regles = new ComparaisonRepetitions.Regles
{
    BasesAttendues = ["6901", "9871"],
    Explications = ComparaisonRepetitions.LireExplications(explications),
    Defauts = ComparaisonRepetitions.LireDefauts(defautsRep),
};
RapportRepetition Variante(Action<RapportRepetition> changer)
{
    var v = RapportRepetition.Lire(Path.Combine(repetitions, "repetition-3.json"));
    changer(v);
    return v;
}
bool Refuse(IReadOnlyList<RapportRepetition> reps, string critere) => ComparaisonRepetitions.Comparer(reps, regles).Any(c => !c.Ok && c.Critere == critere);
var refusDeux = Refuse([r1, r2], "SOC-025 CA04");
var refusBase = Refuse([r1, r2, Variante(v => v.Bases.RemoveAll(b => b.Greffe == "9871"))], "SOC-025 CA01");
var refusRejoue = Refuse([r1, r2, Variante(v => v.Bases.RemoveAll(b => b.Greffe == "6901"))], "SOC-025 CA05");
var refusNuit = Refuse([r1, r2, Variante(v => v.DureeMs = (long)TimeSpan.FromHours(11).TotalMilliseconds)], "SOC-025 CA03");
var refusRetard = Refuse([r1, r2, Variante(v => v.Bases[0].FinUtc = v.Bases[0].PassageUtc!.Value.AddMinutes(5))], "SOC-025 CA03");
var refusStable = Refuse([r1, r2, Variante(v => v.Bases[1].DureeMs = v.Bases[1].DureeMs * 10 + 120_000)], "SOC-025 CA04");
var refusEcart = Refuse([r1, r2, Variante(v => v.Bases[1].Ecarts.Add(new EcartPassage("comptes", "1 compte de plus dans la V3")))], "SOC-025 CA04");
var refusProd = Refuse([r1, r2, Variante(v => v.Bases[1].Serveur = @"SQL-PROD\PROD")], "SOC-025 CA06");
var refusExplication = ComparaisonRepetitions.Comparer([r1, r2, r3], regles with { Explications = [] }).Any(c => !c.Ok && c.Critere == "SOC-025 CA02");
Exiger(refusDeux && refusBase && refusRejoue && refusNuit && refusRetard && refusStable && refusEcart && refusProd && refusExplication,
    $"SOC-025 contre-epreuves : 2 repetitions seulement ({refusDeux}), une base manquante ({refusBase}), defaut non rejoue ({refusRejoue}), nuit depassee ({refusNuit}), " +
    $"base finie apres son heure ({refusRetard}), duree instable ({refusStable}), ecart dans la derniere ({refusEcart}), base en production ({refusProd}), " +
    $"ecart non explique ({refusExplication}) : chacune refusee");

// ====================================================================================================
// D. Criteres de la beta (SOC-022 ; SOC-019 CA01, CA05)
// ====================================================================================================
var dossierVide = Path.Combine(temp, "beta-vide");
Directory.CreateDirectory(dossierVide);
var sortieReel = new StringWriter();
var codeReel = await RigV3.Passage.LigneDeCommande.ExecuterAsync(["beta", "demarrer", "--carnet", carnetReel, "--dossier", dossierVide,
    "--commune", SansPool(commune.Connexion), "--bancs", Path.Combine(racine, "tests")], sortieReel);
Info(sortieReel.ToString().Trim().Replace("\n", "\n        "));
var storiesReelles = DossierBeta.LireCarnet(carnetReel);
Exiger(codeReel == 1 && sortieReel.ToString().Contains("MANQUE   SOC-022 CA01 : V3 complete") && sortieReel.ToString().Contains("encore « à valider »")
       && sortieReel.ToString().Contains("MANQUE   SOC-019 CA01") && sortieReel.ToString().Contains("OK       SOC-022 CA02"),
    $"SOC-022 CA01 : sur le carnet reel ({storiesReelles.Count} stories) et sans dossier, la beta ne demarre pas (code {codeReel}) : stories non validees, " +
    "pas de certificat, ni repetition, ni formation ; les greffes beta du catalogue sont bien Le Puy, Chartres et Vienne");

// Un dossier complet : trois stories (l'une change le geste de l'agent), citees par un banc, validees ; certificats ; repetitions ;
// formation des 3 greffes beta ; validations ; defauts.
var carnetEssai = Path.Combine(temp, "carnet");
var bancsEssai = Path.Combine(temp, "bancs");
var dossier = Path.Combine(temp, "beta");
Directory.CreateDirectory(carnetEssai);
Directory.CreateDirectory(bancsEssai);
Directory.CreateDirectory(Path.Combine(dossier, "repetitions"));
var betas = new[] { "2801", "3802", "4302" };
var version = RigV3.Passage.LigneDeCommande.VersionV3();
var aujourdhui = DateOnly.FromDateTime(DateTime.Today);
var majeure = string.Join('.', version.Split('.').Take(2));
async Task Ecrire(string chemin, object valeur) => await File.WriteAllTextAsync(chemin, JsonSerializer.Serialize(valeur, RapportPassage.Json));
async Task PoserDossier()
{
    await File.WriteAllTextAsync(Path.Combine(carnetEssai, "essai.json"), """
        [ { "id": "TST-001", "titre": "geste change", "valide_par": "Referent RCS", "validation_greffe_beta": true },
          { "id": "TST-002", "titre": "socle", "valide_par": "Referent Socle", "validation_greffe_beta": false },
          { "id": "TST-003", "titre": "recopie", "valide_par": "Referent Recopie", "validation_greffe_beta": false } ]
        """);
    await File.WriteAllTextAsync(Path.Combine(carnetEssai, "non-repris-essai.json"), """[ { "id": "TST-999", "valide_par": "à valider" } ]""");
    await File.WriteAllTextAsync(Path.Combine(bancsEssai, "Banc.cs"), "// TST-001 CA01, TST-002 CA01, TST-003 CA02\n");
    await Ecrire(Path.Combine(dossier, "certificat-v3.json"), new Certificat("V3", "Organisme d'essai", "COFRAC 5-0000", "NF525-V3-0001", "NF525",
        majeure, aujourdhui.AddDays(-10), aujourdhui.AddYears(3)));
    await Ecrire(Path.Combine(dossier, "certificat-v2.json"), new Certificat("V2", "Organisme d'essai", "COFRAC 5-0000", "NF525-V2-0007", "NF525",
        "2026.5", aujourdhui.AddYears(-1), aujourdhui.AddYears(1)));
    // fix-ok: DirectoryNotFoundException mesuree au 2e lancement : le cas « sans repetition » supprime le dossier, la remise doit le recreer.
    Directory.CreateDirectory(Path.Combine(dossier, "repetitions"));
    foreach (var f in Directory.GetFiles(repetitions)) File.Copy(f, Path.Combine(dossier, "repetitions", Path.GetFileName(f)), true);
    File.Copy(explications, Path.Combine(dossier, "explications.json"), true);
    File.Copy(defautsRep, Path.Combine(dossier, "defauts-repetitions.json"), true);
    await Ecrire(Path.Combine(dossier, "formation.json"), new PlanFormation([.. betas.Select(g => new FormationGreffe(g,
        [new FormationMetier("RCS", 12, 12), new FormationMetier("judiciaire", 4, 4)]))]));
    await Ecrire(Path.Combine(dossier, "validations-beta.json"), betas.Select(g => new ValidationBeta("TST-001", g, "chef de greffe", aujourdhui)).ToList());
    await Ecrire(Path.Combine(dossier, "defauts-beta.json"), new List<DefautBeta>
    {
        new("BETA-001", "#4101", "2801", "libelle tronque dans les demandes", RegleDefauts.Ouvert),
        new("BETA-002", "#4102", "3802", "numero de gestion en double", RegleDefauts.Clos, ActeOuRegistreFaux: true),
    });
}
async Task<(int Code, string Texte)> Beta(string quoi)
{
    var s = new StringWriter();
    var c = await RigV3.Passage.LigneDeCommande.ExecuterAsync(["beta", quoi, "--carnet", carnetEssai, "--dossier", dossier,
        "--commune", SansPool(commune.Connexion), "--bancs", bancsEssai], s);
    return (c, s.ToString());
}
await PoserDossier();
var demarre = await Beta("demarrer");
var termine = await Beta("terminer");
Info(demarre.Texte.Trim().Replace("\n", "\n        "));
Exiger(demarre.Code == 0 && Regex.Matches(demarre.Texte, "^OK ", RegexOptions.Multiline).Count == 7,
    $"SOC-022 CA01 / SOC-019 CA01 : dossier complet -> la beta peut demarrer (code {demarre.Code}) : V3 complete, stories validees, certificat de la V3 " +
    $"(version {majeure}), certificat de la V2 a jour, repetitions sans ecart, formation des 3 greffes beta, greffes beta du catalogue");
Exiger(termine.Code == 0,
    $"SOC-022 CA04 : criteres de fin remplis (un defaut ouvert non bloquant, le bloquant clos ; validations des 3 greffes beta) -> la beta se termine (code {termine.Code})");

// Chaque critere de demarrage retire un a un : la beta ne demarre pas, et la sortie nomme le critere.
async Task<bool> RefusDemarrage(string critere, Func<Task> casser)
{
    await PoserDossier();
    await casser();
    var r = await Beta("demarrer");
    await PoserDossier();
    return r.Code == 1 && r.Texte.Contains("MANQUE   " + critere);
}
var refusBeta = new Dictionary<string, bool>
{
    ["story sans banc"] = await RefusDemarrage("SOC-022 CA01 : V3 complete", () => File.WriteAllTextAsync(Path.Combine(bancsEssai, "Banc.cs"), "// TST-001, TST-002\n")),
    ["story a valider"] = await RefusDemarrage("SOC-022 CA01 : toutes les stories", async () =>
        await File.WriteAllTextAsync(Path.Combine(carnetEssai, "essai.json"), (await File.ReadAllTextAsync(Path.Combine(carnetEssai, "essai.json"))).Replace("Referent Socle", "à valider"))),
    ["sans certificat V3"] = await RefusDemarrage("SOC-019 CA01", () => { File.Delete(Path.Combine(dossier, "certificat-v3.json")); return Task.CompletedTask; }),
    ["certificat V3 echu"] = await RefusDemarrage("SOC-019 CA01", () => Ecrire(Path.Combine(dossier, "certificat-v3.json"),
        new Certificat("V3", "Organisme d'essai", "COFRAC 5-0000", "NF525-V3-0001", "NF525", majeure, aujourdhui.AddYears(-4), aujourdhui.AddDays(-1)))),
    ["certificat d'une autre version"] = await RefusDemarrage("SOC-019 CA01", () => Ecrire(Path.Combine(dossier, "certificat-v3.json"),
        new Certificat("V3", "Organisme d'essai", "COFRAC 5-0000", "NF525-V3-0001", "NF525", "2025.9", aujourdhui.AddDays(-10), aujourdhui.AddYears(3)))),
    ["attestation sans organisme accredite"] = await RefusDemarrage("SOC-019 CA01", () => Ecrire(Path.Combine(dossier, "certificat-v3.json"),
        new Certificat("V3", "Amitel", "", "ATT-EDITEUR", "attestation individuelle", majeure, aujourdhui.AddDays(-10), aujourdhui.AddYears(3)))),
    ["certificat V2 echu"] = await RefusDemarrage("SOC-019 CA05", () => Ecrire(Path.Combine(dossier, "certificat-v2.json"),
        new Certificat("V2", "Organisme d'essai", "COFRAC 5-0000", "NF525-V2-0007", "NF525", "2026.5", aujourdhui.AddYears(-4), aujourdhui.AddDays(-1)))),
    ["sans repetition"] = await RefusDemarrage("SOC-022 CA01 : repetitions", () => { Directory.Delete(Path.Combine(dossier, "repetitions"), true); return Task.CompletedTask; }),
    ["repetition avec ecart non explique"] = await RefusDemarrage("SOC-022 CA01 : repetitions", () => { File.Delete(Path.Combine(dossier, "explications.json")); return Task.CompletedTask; }),
    ["formation incomplete"] = await RefusDemarrage("SOC-022 CA01 / SOC-026 CA03", () => Ecrire(Path.Combine(dossier, "formation.json"), new PlanFormation(
        [.. betas.Select(g => new FormationGreffe(g, [new FormationMetier("RCS", 12, g == "2801" ? 11 : 12)]))]))),
};
await Executer(commune.Connexion, "UPDATE soc.Greffe SET EstBeta = 1, ModifiePar = 'banc.passage', ModifieLe = SYSDATETIME() WHERE CodeGreffe = '6901'");
refusBeta["un 4e greffe beta"] = (await Beta("demarrer")) is var q && q.Code == 1 && q.Texte.Contains("MANQUE   SOC-022 CA02");
await Executer(commune.Connexion, "UPDATE soc.Greffe SET EstBeta = 0, ModifiePar = 'banc.passage', ModifieLe = SYSDATETIME() WHERE CodeGreffe = '6901'");
Exiger(refusBeta.Values.All(v => v) && (await Beta("demarrer")).Code == 0,
    $"SOC-022 CA01 / CA02 : chaque critere qui manque bloque le demarrage, nomme : {string.Join(", ", refusBeta.Select(x => $"{x.Key} {(x.Value ? "refuse" : "ACCEPTE")}"))} ; " +
    "remis, le demarrage repasse");

// Criteres de fin, un a un ; une date seule ne termine jamais la beta.
async Task<bool> RefusFin(string critere, Func<Task> casser)
{
    await PoserDossier();
    await casser();
    var r = await Beta("terminer");
    await PoserDossier();
    return r.Code == 1 && r.Texte.Contains("MANQUE   " + critere);
}
var refusFin = new Dictionary<string, bool>
{
    ["defaut bloquant ouvert"] = await RefusFin("SOC-022 CA04 : aucun defaut bloquant", () => Ecrire(Path.Combine(dossier, "defauts-beta.json"),
        new List<DefautBeta> { new("BETA-003", "#4103", "4302", "Kbis sans la derniere mention", RegleDefauts.Ouvert, ActeOuRegistreFaux: true) })),
    ["defaut sans identifiant de suivi"] = await RefusFin("SOC-022 CA04 : aucun defaut bloquant", () => Ecrire(Path.Combine(dossier, "defauts-beta.json"),
        new List<DefautBeta> { new("BETA-004", "", "4302", "couleur", RegleDefauts.Ouvert) })),
    ["validation d'un greffe beta absente"] = await RefusFin("SOC-022 CA05", () => Ecrire(Path.Combine(dossier, "validations-beta.json"),
        betas.Take(2).Select(g => new ValidationBeta("TST-001", g, "chef de greffe", aujourdhui)).ToList())),
    ["transferts repetes avec ecart"] = await RefusFin("SOC-022 CA04 : transferts repetes", () => { File.Delete(Path.Combine(dossier, "explications.json")); return Task.CompletedTask; }),
};
var dossierDate = Path.Combine(temp, "beta-date");
Directory.CreateDirectory(dossierDate);
await File.WriteAllTextAsync(Path.Combine(dossierDate, "fin-prevue.json"), $"{{ \"FinPrevue\": \"{aujourdhui.AddDays(-30):yyyy-MM-dd}\" }}");
var sortieDate = new StringWriter();
var codeDate = await RigV3.Passage.LigneDeCommande.ExecuterAsync(["beta", "terminer", "--carnet", carnetEssai, "--dossier", dossierDate,
    "--commune", SansPool(commune.Connexion), "--bancs", bancsEssai], sortieDate);
Exiger(refusFin.Values.All(v => v) && codeDate == 1,
    $"SOC-022 CA04 : chaque critere de fin qui manque garde la beta ouverte ({string.Join(", ", refusFin.Select(x => $"{x.Key} {(x.Value ? "refuse" : "ACCEPTE")}"))}) ; " +
    $"une date de fin passee, seule, ne la termine pas (code {codeDate})");

// La regle de classement (CA03), cas par cas, et la liste des defauts ouverts lisible a tout moment.
var casDefauts = new (DefautBeta D, bool Bloquant)[]
{
    (new("D1", "#1", "2801", "perte", "ouvert", DonneesPerduesOuFaussees: true), true),
    (new("D2", "#2", "2801", "Kbis", "ouvert", ActeOuRegistreFaux: true), true),
    (new("D3", "#3", "2801", "facture", "ouvert", ChainesOuFacturation: true), true),
    (new("D4", "#4", "2801", "droit", "ouvert", DroitsOuConfidentialite: true), true),
    (new("D5", "#5", "2801", "geste", "ouvert", GesteImpossible: true), true),
    (new("D6", "#6", "2801", "geste contourne", "ouvert", GesteImpossible: true, Contournement: true), false),
    (new("D7", "#7", "2801", "libelle", "ouvert"), false),
};
await Ecrire(Path.Combine(dossier, "defauts-beta.json"), casDefauts.Select(x => x.D).ToList());
var liste = await Beta("defauts");
await PoserDossier();
Exiger(casDefauts.All(x => RegleDefauts.Classer(x.D).Bloquant == x.Bloquant) && liste.Code == 0
       && Regex.Matches(liste.Texte, "BLOQUANT").Count == 5 && liste.Texte.IndexOf("BLOQUANT", StringComparison.Ordinal) < liste.Texte.IndexOf("non bloquant", StringComparison.Ordinal),
    "SOC-022 CA03 : la regle ecrite classe chaque defaut (perte, registre, chaine signee, droit, geste sans contournement : bloquant ; geste contourne, gene : non) " +
    "et « beta defauts » liste les defauts ouverts, bloquants d'abord, avec leur identifiant de suivi");

// ====================================================================================================
// E. Certification (SOC-019)
// ====================================================================================================
var dossierCertif = Path.Combine(racine, "docs", "certification", "dossier-certification-v3.md");
var texteCertif = File.Exists(dossierCertif) ? await File.ReadAllTextAsync(dossierCertif) : "";
var sourceChaines = await File.ReadAllTextAsync(Path.Combine(racine, "Serveur", "Socle", "Chaines", "ServiceChaines.cs"));
var chainesCode = Regex.Match(sourceChaines, @"INSERT INTO soc\.EtatChaine \(Chaine\) VALUES (?<v>[^;]+);").Groups["v"].Value;
var sept = Regex.Matches(chainesCode, @"'(?<c>[A-Z]{2,3})'").Select(m => m.Groups["c"].Value).Append("JP").ToList();
var codesEcrits = typeof(ServiceChaines).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(short) && f.Name.StartsWith("Code", StringComparison.Ordinal))
                                        .Select(f => (short)f.GetRawConstantValue()!).Concat([(short)210, (short)250, (short)255]).Distinct().Order().ToList();
var codesAbsents = codesEcrits.Where(c => !Regex.IsMatch(texteCertif, $@"\|\s*{c}\s*\|")).ToList();
var sections = new[] { "chaînes", "signature", "journal", "clôture", "archiv", "purge", "certificat", "version" };
var sectionsAbsentes = sections.Where(s => !Regex.IsMatch(texteCertif, $@"^#+ .*{s}", RegexOptions.Multiline | RegexOptions.IgnoreCase)).ToList();
Exiger(sept.Count == 7 && sept.All(c => texteCertif.Contains($"`{c}`")) && codesAbsents.Count == 0 && sectionsAbsentes.Count == 0,
    $"SOC-019 CA02 : le dossier a remettre (docs\\certification\\dossier-certification-v3.md) decrit les 7 chaines du code ({string.Join(", ", sept)}), " +
    $"la signature, le journal, les clotures, l'archivage et la purge, et chaque evenement que la V3 ecrit ({string.Join(", ", codesEcrits)})" +
    (codesAbsents.Count + sectionsAbsentes.Count > 0 ? $" -- absents : {string.Join(", ", codesAbsents)} {string.Join(", ", sectionsAbsentes)}" : ""));
Exiger(Regex.IsMatch(texteCertif, @"31/12[^\n]*archive|archive[^\n]*31/12", RegexOptions.IgnoreCase) && texteCertif.Contains("CloturerAsync"),
    "SOC-019 (decision du lot 14) : le dossier soumet a l'organisme l'archive fiscale scellee le 31/12 avec le total annuel, et dit ce qui change s'il la refuse (CloturerAsync)");
var perimetreReel = SuiviCertification.Perimetre.Where(p => !p.StartsWith("Serveur/Facturation", StringComparison.Ordinal) && !p.StartsWith("Base/migrations/FAC-", StringComparison.Ordinal))
    .Where(p => !Directory.EnumerateFiles(racine, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(racine, f).Replace('\\', '/'))
                         .Any(f => f.StartsWith(p, StringComparison.OrdinalIgnoreCase) && !f.Contains("/node_modules/") && !f.Contains("/bin")))
    .ToList();
var registre = Path.Combine(temp, "suivi-certification.json");
await Ecrire(registre, new List<SuiviVersion> { new("2026.2.0", true, "Organisme d'essai", aujourdhui, "avis 2026-17") });
async Task<int> Suivi(string v, string fichiers, string? reg) => await RigV3.Passage.LigneDeCommande.ExecuterAsync(
    ["certification", "suivi", "--version", v, "--fichiers", fichiers, .. (reg is null ? Array.Empty<string>() : ["--registre", reg])], TextWriter.Null);
var suiviSans = await Suivi("2026.2.0", @"Serveur\Socle\Chaines\ServiceChaines.cs,web/src/ecrans/EcranFactures.tsx", null);
var suiviAvec = await Suivi("2026.2.0", @"Serveur\Socle\Chaines\ServiceChaines.cs,web/src/ecrans/EcranFactures.tsx", registre);
var suiviAutre = await Suivi("2026.3.0", "Base/migrations/SOC-018_chaines.sql", registre);
var suiviHors = await Suivi("2026.3.0", "web/src/ecrans/EcranFactures.tsx,Pont/EcranComptes.cs", null);
Exiger(perimetreReel.Count == 0 && suiviSans == 1 && suiviAvec == 0 && suiviAutre == 1 && suiviHors == 0,
    $"SOC-019 CA03 : une version qui touche une fonction certifiee ne se livre qu'avec l'accord du suivi ({suiviSans} sans accord, {suiviAvec} avec, {suiviAutre} accord " +
    $"d'une autre version) ; hors du perimetre, pas de suivi ({suiviHors}) ; le perimetre vise des fichiers du depot" +
    (perimetreReel.Count > 0 ? $" -- introuvables : {string.Join(", ", perimetreReel)}" : ""));

// ====================================================================================================
// F. Formation (SOC-026)
// ====================================================================================================
var sortieCompteDev = new StringWriter();
var planDev = Path.Combine(temp, "formation-rigdev.json");
var codeCompteDev = await RigV3.Passage.LigneDeCommande.ExecuterAsync(["formation", "--v2", chaineRigDev, "--greffe", "9999", "--plan", planDev], sortieCompteDev);
var sortieCompteCopie = new StringWriter();
var codeCompteCopie = await RigV3.Passage.LigneDeCommande.ExecuterAsync(["formation", "--v2", SansPool(modeleV2.Connexion), "--greffe", "9999"], sortieCompteCopie);
Info(sortieCompteDev.ToString().Trim().Replace("\n", "\n        "));
var rcsDirect = await Nombre(modeleV2.Connexion, "SELECT COUNT(*) FROM dbo.UTILISATEUR u JOIN dbo.SERVICE s ON s.SRVC_ID_SRVC = u.UTLST_ID_SRVC " +
                                                  "WHERE s.SRVC_ABREGE = 'RCS' AND u.UTLST_DESACTIVE = 0 AND ISNULL(u.UTLST_ISAMITEL, 0) = 0");
var planCompte = JsonSerializer.Deserialize<PlanFormation>(await File.ReadAllTextAsync(planDev), RapportPassage.Json)!;
var rcsOutil = planCompte.Greffes.Single().Metiers.SingleOrDefault(m => m.Metier == "RCS")?.AFormer ?? -1;
string Sans(string s) => string.Join("\n", s.Split('\n').Skip(0).Select(l => Regex.Replace(l, @"\(rigv3_banc_\w+\)|\(RIG_DEV\)", "")));
Exiger(codeCompteDev == 0 && codeCompteCopie == 0 && rcsOutil == rcsDirect && rcsOutil > 0 && Sans(sortieCompteDev.ToString()) == Sans(sortieCompteCopie.ToString()),
    $"SOC-026 CA01 : le nombre d'agents a former par greffe et par metier vient d'un comptage : RIG_DEV et sa copie donnent les memes nombres " +
    $"(RCS {rcsOutil}, recompte a part : {rcsDirect}), comptes Amitel et desactives exclus");
var personnels = (await Lignes(modeleV2.Connexion, "SELECT UTLST_LOGIN FROM dbo.UTILISATEUR WHERE LEN(UTLST_LOGIN) >= 4 " +
                                                     "UNION SELECT UTLST_NOM FROM dbo.UTILISATEUR WHERE LEN(UTLST_NOM) >= 4"))
                  .Where(v => v != "NULL").ToList();
// fix-ok: la recherche de chaque nom dans la sortie trouvait des noms de famille egaux a des mots de l'outil (« Amitel », « service »...),
// mesure au 3e lancement du banc : le controle porte desormais sur les MOTS sortis, tous du vocabulaire de l'outil et des services.
var vocabulaire = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "Greffe", "agent", "s", "a", "former", "exclus", "compte", "Amitel", "actif", "desactive", "service", "sans", "classer", "tous", "metiers",
    "Greffes", "Code", "Metiers", "Metier", "AFormer", "Formes", "RIG_DEV",
};
foreach (var (abrege, metier) in ComptageAgents.MetierDuService)
    foreach (var mot in Regex.Matches(abrege + " " + metier, @"\p{L}+").Select(m => m.Value)) vocabulaire.Add(mot);
var motsSortis = Regex.Matches(sortieCompteDev.ToString() + await File.ReadAllTextAsync(planDev), @"\p{L}[\p{L}_]*").Select(m => m.Value).Distinct().ToList();
var inconnus = motsSortis.Where(m => !vocabulaire.Contains(m)).ToList();
var fuites = personnels.Where(p => motsSortis.Contains(p, StringComparer.OrdinalIgnoreCase) && !vocabulaire.Contains(p)).Count();
Exiger(inconnus.Count == 0 && fuites == 0 && personnels.Count > 50,
    $"SOC-026 T01 : le comptage ne sort aucune donnee personnelle : ses {motsSortis.Count} mots sont ceux de l'outil, des services et des metiers " +
    $"(aucun des {personnels.Count} logins et noms de la base{(inconnus.Count > 0 ? $" ; {inconnus.Count} mot(s) hors vocabulaire, non imprimes" : "")})");

var dossierFormation = Path.Combine(racine, "docs", "formation");
var attendus = new Dictionary<string, string> { [SupportsFormation.FichierEcrans] = SupportsFormation.EcransParMetier(), [SupportsFormation.FichierLibelles] = SupportsFormation.LibellesQuiChangent() };
if (Environment.GetEnvironmentVariable("RIGV3_ECRIRE_FORMATION") == "1")
    foreach (var (f, texte) in attendus)
    {
        Directory.CreateDirectory(dossierFormation);
        await File.WriteAllTextAsync(Path.Combine(dossierFormation, f), texte.Replace("\r\n", "\n"));
        Info($"ecrit : docs\\formation\\{f}");
    }
foreach (var (f, texte) in attendus)
{
    var chemin = Path.Combine(dossierFormation, f);
    var ecrit = File.Exists(chemin) ? SupportsFormation.LignesDeTableau(await File.ReadAllTextAsync(chemin)) : [];
    var calcule = SupportsFormation.LignesDeTableau(texte);
    var mensonges = ecrit.Except(calcule).ToList();
    var nouveaux = calcule.Except(ecrit).ToList();
    Exiger(ecrit.Count > 0 && mensonges.Count == 0,
        $"SOC-026 T03 : docs\\formation\\{f} ({ecrit.Count} lignes) est tire des ecrans de la V3 : aucune ligne ne decrit un ecran ou un libelle qui n'existe plus" +
        (mensonges.Count > 0 ? $" -- a regenerer : {string.Join(" / ", mensonges.Take(3))}" : ""));
    if (nouveaux.Count > 0) Reste($"docs\\formation\\{f} : {nouveaux.Count} ligne(s) d'ecran ajoutee(s) depuis sa generation ; regenerer (RIGV3_ECRIRE_FORMATION=1 .\\tests.ps1 SoclePassage)");
}
var perdus = SupportsFormation.VocabulairePerdu();
Exiger(perdus.Count == 0 && SupportsFormation.Changements().Count > 0,
    $"SOC-026 CA05 : sur les {SupportsFormation.Changements().Count} ecrans renommes de la V2 a la V3, aucun ne perd un mot impose par les textes " +
    $"({string.Join(", ", SupportsFormation.VocabulaireDesTextes)})" + (perdus.Count > 0 ? $" -- {string.Join(" ; ", perdus)}" : ""));
var readme = Path.Combine(dossierFormation, "README.md");
var texteReadme = File.Exists(readme) ? await File.ReadAllTextAsync(readme) : "";
Exiger(new[] { "vague", "bêta", "copie anonymisée", "assistance", "outre-mer", "RigV3.Passage formation", "ecrans-par-metier.md", "libelles-qui-changent.md" }
           .All(m => texteReadme.Contains(m, StringComparison.OrdinalIgnoreCase)),
    "SOC-026 CA02-CA04 : docs\\formation\\README.md donne le plan par vague (beta avant la beta, 37 avant le jour J), l'environnement d'essai sur la copie anonymisee, " +
    "le comptage, l'assistance du jour J et ses horaires d'outre-mer, et renvoie aux supports tires des ecrans");

// Le mode d'emploi decrit chaque commande de l'outil.
var exploitation = await File.ReadAllTextAsync(Path.Combine(racine, "docs", "reconstruction", "socle-exploitation.md"));
var commandes = new[] { "passer", "retour", "jour-j", "repeter", "tableau", "comparer", "beta demarrer", "beta terminer", "beta defauts", "certification suivi", "formation" };
var nonDecrites = commandes.Where(c => !exploitation.Contains($"RigV3.Passage {c}")).ToList();
Exiger(nonDecrites.Count == 0, $"docs\\reconstruction\\socle-exploitation.md decrit chaque commande de RigV3.Passage" + (nonDecrites.Count > 0 ? $" -- manquent : {string.Join(", ", nonDecrites)}" : ""));

Reste("SOC-024 CA01, CA05, CA06 et SOC-025 CA01, CA03 sur les 40 vraies bases : pas d'acces aux instances de production ni aux copies anonymisees des greffes " +
      "depuis ce poste (et aucune copie ne sert de jeu de repetition avant la nouvelle reception du lot 6, decision du chef) ; l'outil est celui qu'on lancera");
Reste("SOC-019 CA01, CA04, CA05 : le certificat lui-meme (organisme accredite, audit), son ecran dans la V3, celui de la V2 -- hors de ce poste");
Reste("SOC-022 CA06 et SOC-026 CA06 : le signalement d'un defaut depuis la V3 et l'aide a la premiere ouverture sont des ecrans « a maquetter » hors des fichiers de ce lot");
}
finally
{
    foreach (var b in bases.AsEnumerable().Reverse())
        try { await b.DisposeAsync(); } catch (SqlException e) { Console.WriteLine($"        base {b.Nom} non supprimee : {e.Message}"); }
    try
    {
        await Executer(master, $"IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'{sessionEchecs}') DROP EVENT SESSION [{sessionEchecs}] ON SERVER;");
        foreach (var f in sauvegardes) await Executer(master, $"EXEC master.sys.xp_delete_files N'{f}';");
    }
    catch (SqlException e) { Console.WriteLine($"        nettoyage du serveur : {e.Message}"); }
    try { Directory.Delete(temp, true); } catch (IOException) { }
}

Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "VERT : SOC-019, SOC-022, SOC-024, SOC-025, SOC-026 tiennent leurs criteres joues ici." : $"ROUGE : {echecs.Count} controle(s) en echec.");
foreach (var e in echecs) Console.WriteLine("  - " + e);
return echecs.Count == 0 ? 0 : 1;

/// <summary>La cle de signature du banc : en memoire, une par greffe, jamais dans le magasin de cles de la personne qui lance le banc.</summary>
internal sealed class CoffreMemoire : ICoffreCles
{
    private readonly ConcurrentDictionary<string, ECDsa> cles = new();
    public ECDsa CleDuGreffe(string codeGreffe) => cles.GetOrAdd(codeGreffe, _ => ECDsa.Create(ECCurve.NamedCurves.nistP256));
    public string Conteneur(string codeGreffe) => $"memoire du banc SoclePassageCheck ({codeGreffe})";
}
