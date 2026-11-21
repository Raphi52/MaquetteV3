using System.Data;
using System.Diagnostics;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using RecopieTransfertCheck;
using RigV3.Base;
using RigV3.Serveur;
using RigV3.Serveur.Recopie;
using RigV3.Serveur.Socle.Greffes;

// Banc de REC-012 a REC-017 et REC-019 (carnet\recopie.json) : les fiches partagees entre la V2 et la V3 d'un greffe, chacune
// dans SA table V3 avec sa correspondance colonne par colonne (Serveur\Recopie\FichesV3.cs, Base\migrations\
// REC-012_fiches-partagees.sql), transferees (REC-001) puis recopiees dans les deux sens (REC-002, REC-003). Lot 10 du plan
// de reconstruction, phase 2 (2026-11-21). Un controle par critere, qui cite son id.
//
//  0. Une base V2 jetable tiree de RIG_DEV (ExtraitRigDev, lecture seule) avec toutes les tables des regles du serveur ; ses
//     declencheurs de transmission REACTIVES ; Base\v2\recopie-v2.sql ; le transfert avec les regles du serveur ; la mise
//     en service de la recopie (PontRecopie), qui fige les fiches de la base.
//  A. La correspondance colonne par colonne de chaque fiche contre le catalogue reel de RIG_DEV.
//  B. Ce que le transfert doit garder (SIREN, numeros, adresses partagees, adresse inconnue, agents, types par reference).
//  C. Les passages de la recopie (ServiceRecopie.PasserAsync) : une fiche modifiee d'un cote, identique de l'autre.
//  D. Une seconde base, tiree de RIG_RECETTE : le meme numero de type designe un autre type (REC-019 CA02, CA05).
//  E. La vraie boucle du serveur (RecopieContinue) : le delai d'une desactivation de compte (REC-017 CA02).
// Il lui faut « docker compose up -d » et l'acces en lecture a RIG_DEV et RIG_RECETTE (autres : RIGV3_RIG_DEV, RIGV3_RIG_RECETTE).
// Doc : docs/reconstruction/recopie.md, section « Les fiches partagees (REC-012 a REC-019) ».

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var migrations = Path.Combine(racine, "Base", "migrations");
var scriptV2 = Path.Combine(racine, "Base", "v2", "recopie-v2.sql");
string Chaine(string variable, string nom) => Environment.GetEnvironmentVariable(variable) is { Length: > 0 } c ? c :
    $@"Server=SQL-DEV\DEV;Database={nom};Integrated Security=True;Encrypt=True;TrustServerCertificate=True;ApplicationIntent=ReadOnly;Connect Timeout=20";
var chaineRigDev = Chaine("RIGV3_RIG_DEV", "RIG_DEV");
var chaineRecette = Chaine("RIGV3_RIG_RECETTE", "RIG_RECETTE");
const string AgentV3 = "Agent V3 du banc des fiches";

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
List<string> Colonne0(DataTable t) => t.Rows.Cast<DataRow>().Select(r => Convert.ToString(r[0], System.Globalization.CultureInfo.InvariantCulture)!).ToList();

// ====================================================================================================
// 0. RIG_DEV, la base V2 jetable, le suivi des changements, le transfert, la mise en service
// ====================================================================================================
try
{
    LectureV2.ExigerLecture("SELECT @@SERVERNAME");
    Info($"RIG_DEV : {await Valeur(chaineRigDev, "SELECT @@SERVERNAME + ' / ' + DB_NAME()")} ; RIG_RECETTE : {await Valeur(chaineRecette, "SELECT DB_NAME()")}");
}
catch (SqlException e)
{
    Exiger(false, $"RIG_DEV et RIG_RECETTE lisibles : {e.Message} -- il faut l'acces en lecture a SQL-DEV\\DEV (ou RIGV3_RIG_DEV, RIGV3_RIG_RECETTE)");
    return 1;
}

var reglesServeur = ReglesTransfert.Decouvrir();
var reglesRecopie = ReglesRecopie.Decouvrir();
var fichesV3 = reglesRecopie.Where(r => r.ColonneParColonne).ToList();
const string Inconnue = "* Adresse inconnu pour le client*";
// Les tables des anciennes reprises (REC-009, lot 11 : quelques lignes, ou aucune, sur RIG_DEV) sont tirees entieres.
var entieres = reglesServeur.Regles.Where(r => r.Sorte == SorteRegle.Reprise).Select(r => r.Table).Append("ENCAISSEMENT_REPRIS")
    .Concat(["MIGRATION_REFERENCE", "MIGRATION_TABLE", "MIGRATION_CHAMP", "MIGRATION_ID", "ARTICLE_CORRESP_REPLAY"]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
// Les lignes de l'extrait se choisissent par leurs identifiants, lus d'abord dans RIG_DEV : une seule requete faite de « OR »
// de sous-requetes sur les grosses tables y depassait dix minutes (mesure le 2026-11-21, 1er essai du banc).
async Task<string> Ids(string sql) => string.Join(", ", Colonne0(await Lire(chaineRigDev, sql)).Append("-1"));
var formes = new[]
{
    "ADRBS_BOITE_POSTALE IS NOT NULL", "ADRBS_CEDEX IS NOT NULL", "ADRBS_COMPLEMENT_NO IS NOT NULL", "ADRBS_PAYS <> 'FRA'", "ADRBS_CODE_INSEE IS NOT NULL",
    "ADRBS_TYPE_SPECIAL IS NOT NULL", "ADRBS_PRE_ADRESSE IS NOT NULL", "ADRBS_SUITE_ADRESSE IS NOT NULL", "ADRBS_NUM_VOIE LIKE '%[^0-9]%'", "ADRBS_TYPE_VOIE IS NULL",
};
var idsAdresses = await Ids($"SELECT TOP (500) ADRBS_ID_ADRBS FROM dbo.ADRESSE_BASE ORDER BY ADRBS_ID_ADRBS DESC") + ", " +
                  await Ids($"SELECT ADRBS_ID_ADRBS FROM dbo.ADRESSE_BASE WHERE ADRBS_NOM_VOIE = '{Inconnue}'");
foreach (var f in formes) idsAdresses += ", " + await Ids($"SELECT TOP (6) ADRBS_ID_ADRBS FROM dbo.ADRESSE_BASE WHERE {f} ORDER BY ADRBS_ID_ADRBS DESC");
var idsDossiers = await Ids("SELECT TOP (300) DSSRC_ID_DSRCS FROM dbo.DOSSIER_RCS ORDER BY DSSRC_ID_DSRCS DESC");
var idsHorsNorme = await Ids("SELECT TOP (40) DSSRC_ID_DSRCS FROM dbo.DOSSIER_RCS WHERE DSSRC_NUM_GESTION NOT LIKE '[12][0-9][0-9][0-9][A-Z][0-9][0-9][0-9][0-9][0-9]' ORDER BY DSSRC_ID_DSRCS");
var idsClients = await Ids("SELECT TOP (300) CLIEN_ID_CLIEN FROM dbo.CLIENT ORDER BY CLIEN_ID_CLIEN DESC");
var idsClientsPlus = await Ids("SELECT CLIEN_ID_CLIEN FROM dbo.CLIENT WHERE CLIEN_ABREGE IN (SELECT CLIEN_ABREGE FROM dbo.CLIENT WHERE CLIEN_ABREGE IS NOT NULL " +
                               "GROUP BY CLIEN_ABREGE HAVING COUNT(*) > 1)") + ", " +
                     await Ids("SELECT TOP (40) CLIEN_ID_CLIEN FROM dbo.CLIENT WHERE CLIEN_ID_UTLST IS NOT NULL ORDER BY CLIEN_ID_CLIEN DESC");
var idsInscriptions = await Ids("SELECT TOP (300) INEDT_ID_INSCR FROM dbo.INSCRIPTION ORDER BY INEDT_ID_INSCR DESC") + ", " +
                      await Ids("SELECT TOP (20) INEDT_ID_INSCR FROM dbo.INSCRIPTION WHERE INEDT_ID_CNEND IS NOT NULL ORDER BY INEDT_ID_INSCR DESC") + ", " +
                      await Ids("SELECT TOP (20) INEDT_ID_INSCR FROM dbo.INSCRIPTION WHERE INEDT_ID_INSCR_INSCRIPTION_PRE IS NOT NULL ORDER BY INEDT_ID_INSCR DESC");
var idsAffaires = await Ids("SELECT TOP (150) AFFRE_ID_AFFRE FROM dbo.AFFAIRE ORDER BY AFFRE_ID_AFFRE DESC");
var idsDemandesTypes = await Ids("SELECT DMND_ID_DMND FROM dbo.DEMANDE WHERE DMND_ID_CTGR IN (1417, 2136)");
string Derniers(string table, string cle, int n) => $"SELECT TOP ({n}) * FROM dbo.{table} ORDER BY {cle} DESC";
var extraits = new Dictionary<string, string>
{
    ["ADRESSE_BASE"] = $"SELECT * FROM dbo.ADRESSE_BASE WHERE ADRBS_ID_ADRBS IN ({idsAdresses})",
    ["ENTREPRISE"] = Derniers("ENTREPRISE", "ENTRP_ID_RPRTR", 300),
    ["DOSSIER_RCS"] = $"SELECT * FROM dbo.DOSSIER_RCS WHERE DSSRC_ID_DSRCS IN ({idsDossiers}, {idsHorsNorme})",
    ["CLIENT"] = $"SELECT * FROM dbo.CLIENT WHERE CLIEN_ID_CLIEN IN ({idsClients}, {idsClientsPlus})",
    ["CLIENT_ADRESSE"] = $"SELECT * FROM dbo.CLIENT_ADRESSE WHERE CLIAD_ID_CLIEN IN ({idsClients})",
    ["CLIENT_CONTACT"] = "SELECT TOP (0) * FROM dbo.CLIENT_CONTACT",
    ["CREANCE_END"] = "SELECT TOP (0) * FROM dbo.CREANCE_END",
    ["BATEAU"] = "SELECT TOP (0) * FROM dbo.BATEAU",
    ["INSCRIPTION"] = $"SELECT * FROM dbo.INSCRIPTION WHERE INEDT_ID_INSCR IN ({idsInscriptions})",
    ["RADIATION_TOTALE"] = Derniers("RADIATION_TOTALE", "RDTTL_ID_RDTTL", 30),
    ["ACTION_RCS"] = $"SELECT * FROM dbo.ACTION_RCS WHERE ACTRC_ID_DSRCS IN ({idsDossiers})",
    ["BODACC"] = Derniers("BODACC", "BDCCR_ID_BDCRC", 5),
    ["DEPOT_RCS"] = Derniers("DEPOT_RCS", "DPTRC_ID_DPTRC", 5),
    ["INSCRIPTION_RCS"] = Derniers("INSCRIPTION_RCS", "INSRC_ID_INSRC", 5),
    ["INSTANCE"] = Derniers("INSTANCE", "INSTN_ID_INSTN", 5),
    ["NON_INSCRIT"] = Derniers("NON_INSCRIT", "NINSC_ID_NINSC", 5),
    ["PASSAGE_CFE"] = Derniers("PASSAGE_CFE", "PSSGC_ID_PSSCF", 5),
    ["AFFAIRE"] = $"SELECT * FROM dbo.AFFAIRE WHERE AFFRE_ID_AFFRE IN ({idsAffaires})",
    ["DEMANDE"] = $"SELECT * FROM dbo.DEMANDE WHERE DMND_ID_AFFRE IN ({idsAffaires}) OR DMND_ID_DMND IN ({idsDemandesTypes})",
    ["ARRIVEE"] = Derniers("ARRIVEE", "ARRV_ID_ARRV", 150),
};
var sansExtrait = reglesServeur.Regles.Select(r => r.Table).Where(t => !entieres.Contains(t, StringComparer.OrdinalIgnoreCase) && !extraits.ContainsKey(t)).ToList();
Exiger(sansExtrait.Count == 0, $"le banc tire de RIG_DEV chaque table des regles du serveur ({reglesServeur.Regles.Count} regles) {string.Join(", ", sansExtrait)}");

await using var v2 = await BaseJetable.CreerAsync("RecopieFichesV2");
var chrono = Stopwatch.StartNew();
var extrait = await ExtraitRigDev.ConstruireAsync(chaineRigDev, v2.Connexion, entieres, extraits, Info);
Info($"base V2 jetable {v2.Nom} : {extrait.Lignes.Values.Sum()} lignes dans {extrait.Lignes.Count} tables, {extrait.Cles} cles verifiees, en {chrono.Elapsed.TotalSeconds:0} s ; " +
     string.Join(", ", extrait.Lignes.Where(l => extraits.ContainsKey(l.Key) || l.Key is "ENCAISSEMENT_REPRIS" or "CATEGORIE" or "UTILISATEUR").Select(l => $"{l.Key} {l.Value}")));

// Les fixtures, posees dans la V2 AVANT le suivi et le transfert : deux entreprises sur une meme adresse (REC-016 CA01-CA02) ;
// des fiches sur les deux adresses inconnues (CA03) ; un dossier et une inscription a l'etat de transmission « F » ; une
// imprimante reglee sur un poste (REC-017 CA03).
// RIG_DEV n'a aucune adresse inconnue des reprises de 2017 (lu le 2026-11-21 : ses deux « ADRESSE INCONNUE » sont des saisies) :
// le banc en pose deux, comme deux reprises successives (ClientExtended.GetAdresseInconnu : « 42000 SAINT ETIENNE FRA »).
var catalogueV2 = await CatalogueAsync(v2.Connexion);
var modeleAdresse = await Texte(v2.Connexion, "SELECT TOP (1) CONVERT(varchar(20), ADRBS_ID_ADRBS) FROM dbo.ADRESSE_BASE ORDER BY ADRBS_ID_ADRBS");
for (var i = 0; i < 2; i++)
    await Dupliquer(v2.Connexion, "dbo.ADRESSE_BASE", catalogueV2.Tables["ADRESSE_BASE"], modeleAdresse!,
        $"ADRBS_NOM_VOIE='{Inconnue}';ADRBS_CODE_POSTAL='42000';ADRBS_LOCALITE_POSTALE='SAINT ETIENNE';ADRBS_PAYS='FRA';ADRBS_NUM_VOIE=NULL;ADRBS_TYPE_VOIE=NULL;" +
        "ADRBS_COMPLEMENT_NO=NULL;ADRBS_PRE_ADRESSE=NULL;ADRBS_SUITE_ADRESSE=NULL;ADRBS_BOITE_POSTALE=NULL;ADRBS_CEDEX=NULL;ADRBS_LIEU_RATTACHE=NULL");
var inconnuesV2 = Colonne0(await Lire(v2.Connexion, $"SELECT ADRBS_ID_ADRBS FROM dbo.ADRESSE_BASE WHERE ADRBS_NOM_VOIE = '{Inconnue}' ORDER BY ADRBS_ID_ADRBS"));
var entreprisesAdr = Colonne0(await Lire(v2.Connexion, "SELECT TOP (3) ENTRP_ID_RPRTR FROM dbo.ENTREPRISE WHERE ENTRP_ID_ADRBS IS NOT NULL ORDER BY ENTRP_ID_RPRTR DESC"));
var adressePartagee = await Texte(v2.Connexion, $"SELECT ENTRP_ID_ADRBS FROM dbo.ENTREPRISE WHERE ENTRP_ID_RPRTR = {entreprisesAdr[0]}");
var adressesClients = Colonne0(await Lire(v2.Connexion, "SELECT TOP (2) CLIAD_ID_CLIAD FROM dbo.CLIENT_ADRESSE ORDER BY CLIAD_ID_CLIAD"));
await Executer(v2.Connexion, $"UPDATE dbo.ENTREPRISE SET ENTRP_ID_ADRBS = {adressePartagee} WHERE ENTRP_ID_RPRTR = {entreprisesAdr[1]};" +
                             (inconnuesV2.Count >= 2
                                 ? $"UPDATE dbo.CLIENT_ADRESSE SET CLIAD_ID_ADRBS = {inconnuesV2[0]} WHERE CLIAD_ID_CLIAD = {adressesClients[0]};" +
                                   $"UPDATE dbo.CLIENT_ADRESSE SET CLIAD_ID_ADRBS = {inconnuesV2[1]} WHERE CLIAD_ID_CLIAD = {adressesClients[1]};" +
                                   $"UPDATE dbo.ENTREPRISE SET ENTRP_ID_ADRBS = {inconnuesV2[1]} WHERE ENTRP_ID_RPRTR = {entreprisesAdr[2]};" : ""));
var dossierF = await Texte(v2.Connexion, "SELECT TOP (1) DSSRC_ID_DSRCS FROM dbo.DOSSIER_RCS ORDER BY DSSRC_ID_DSRCS");
var inscriptionF = await Texte(v2.Connexion, "SELECT TOP (1) INEDT_ID_INSCR FROM dbo.INSCRIPTION ORDER BY INEDT_ID_INSCR");
var posteRegle = await Texte(v2.Connexion, "SELECT TOP (1) UTLST_ID_UTLST FROM dbo.UTILISATEUR WHERE UTLST_DESACTIVE = 0 ORDER BY UTLST_ID_UTLST DESC");
await Executer(v2.Connexion, $"UPDATE dbo.DOSSIER_RCS SET DSSRC_ETAT_TRANSMISSION = 'F' WHERE DSSRC_ID_DSRCS = {dossierF};" +
                             $"UPDATE dbo.INSCRIPTION SET INEDT_ETAT_TRANSMISSION = 'F' WHERE INEDT_ID_INSCR = {inscriptionF};" +
                             $"UPDATE dbo.UTILISATEUR SET UTLST_IMPRIMANTE_DEFAUT = 'IMPRIMANTE-DU-POSTE', UTLST_SCANNER_DEFAUT = 'SCANNER-DU-POSTE' WHERE UTLST_ID_UTLST = {posteRegle};");
await Executer(v2.Connexion, "ENABLE TRIGGER [T_DOSSIER_RCS_MAJ_ETAT_TRANSMISSION] ON dbo.DOSSIER_RCS; ENABLE TRIGGER [T_INSCRIPTION_MAJ_ETAT_TRANSMISSION] ON dbo.INSCRIPTION;");

var partagees = reglesRecopie.Select(r => r.TableV2).ToList();
await NumerotationV2Check.Sqlcmd.JouerAsync(v2.Connexion, scriptV2, new Dictionary<string, string>
{
    ["BaseV2"] = v2.Nom, ["RetentionJours"] = "7", ["Tables"] = string.Join(",", partagees),
});

await using var v3 = await BaseJetable.CreerAsync("RecopieFichesV3");
await Migrateur.AppliquerAsync(v3.Connexion, migrations);
chrono.Restart();
var transfert = await MoteurTransfert.TransfererAsync(new OptionsTransfert(v2.Connexion, v3.Connexion)
    { Regles = reglesServeur.Regles, Reprises = reglesServeur.Reprises, TailleLot = 5000 });
var rapport = (await Lire(v3.Connexion, $"SELECT TableV2, Regle, TableV3, Lues, Ecrites, LiensCasses FROM rec.TransfertTable WHERE IdTransfert = {transfert.IdTransfert}"))
    .Rows.Cast<DataRow>().ToDictionary(r => (string)r[0], StringComparer.OrdinalIgnoreCase);
Exiger(transfert.Etat == "fini" && fichesV3.Where(f => f.TableV2 != "UTILISATEUR").All(f => rapport.TryGetValue(f.TableV2, out var l) && (string)l["TableV3"] == f.TableV3
                                                                                          && Convert.ToInt64(l["Lues"]) == Convert.ToInt64(l["Ecrites"])),
    $"REC-001 avec les fiches du lot 10 : la base transferee en {chrono.Elapsed.TotalSeconds:0} s ({transfert.Etat}), chaque fiche ecrite colonne par colonne dans SA table V3, " +
    $"lues = ecrites : {string.Join(", ", fichesV3.Where(f => rapport.ContainsKey(f.TableV2)).Select(f => $"{f.TableV2} -> {rapport[f.TableV2]["TableV3"]} ({rapport[f.TableV2]["Regle"]}, {rapport[f.TableV2]["Ecrites"]})"))} {transfert.Motif}");
if (transfert.Etat != "fini") { await SupprimerInstantanes(v2.Nom); return Bilan(); }

var avantService = await FichesRecopiees.ChargerAsync(v3.Connexion, v2.Nom, null, default);
var mes = await PontRecopie.MettreEnServiceAsync(v3.Connexion, v2.Nom, avantService);
var fiches = await FichesRecopiees.ChargerAsync(v3.Connexion, v2.Nom, null, default);
Exiger(mes.Faite && fiches.Problemes.Count == 0 && fiches.Tables.Count == reglesRecopie.Count
       && await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.FicheEnService") == reglesRecopie.Count,
    $"REC-002 T03 / REC-012 a REC-019 T03 : la mise en service pose {mes.Declencheurs.Count} declencheurs de file et fige les {reglesRecopie.Count} fiches de la base " +
    $"(rec.FicheEnService) ; {fichesV3.Count} ont leur table V3 colonne par colonne {string.Join(" ; ", mes.Refus.Concat(fiches.Problemes))}");
if (!mes.Faite) return Bilan();

TableRecopiee T(string tableV2) => fiches.ParTableV2[tableV2];
string V3Ecrit(string sql) => $"EXEC sys.sp_set_session_context @key = N'rigv3.agent', @value = N'{AgentV3}'; {sql}";
async Task<BilanPassage> Passer() => await ServiceRecopie.PasserAsync(v3.Connexion, v2.Nom, fiches);
async Task<string?> V2De(string tableV2, string idV3) => await Texte(v3.Connexion,
    $"SELECT TOP (1) IdV2 FROM rec.Correspondance WHERE BaseV2 = '{v2.Nom}' AND TableV2 = '{tableV2}' AND IdV3 = '{idV3}' ORDER BY CASE WHEN Origine = 'fusion' THEN 1 ELSE 0 END");
async Task<string?> V3De(string tableV2, string idV2) => await Texte(v3.Connexion,
    $"SELECT IdV3 FROM rec.Correspondance WHERE BaseV2 = '{v2.Nom}' AND TableV2 = '{tableV2}' AND IdV2 = '{idV2}'");
// Une fiche ecrite par un agent : la meme ligne qu'une autre, sous un nouvel identifiant, avec des valeurs changees
// (« COLONNE=expression;... »).
async Task<string> Dupliquer(string chaine, string tableSql, TableV2 table, string id, string? remplacer = null, string prefixe = "")
{
    var cle = table.ClePrimaire[0];
    var cols = table.Colonnes.Where(c => !c.Identite && !c.Calculee && c.TypeBase != "timestamp").Select(c => c.Nom).ToList();
    var valeurs = cols.Select(c => remplacer is not null && remplacer.Split(';').FirstOrDefault(r => r.StartsWith(c + "=", StringComparison.Ordinal)) is { } r ? r[(c.Length + 1)..] : $"[{c}]");
    return (await Texte(chaine, $"{prefixe} DECLARE @ids TABLE (Id nvarchar(200)); INSERT INTO {tableSql} ({string.Join(", ", cols.Select(c => $"[{c}]"))}) " +
                                $"OUTPUT CONVERT(nvarchar(200), inserted.[{cle}]) INTO @ids SELECT {string.Join(", ", valeurs)} FROM {tableSql} WHERE [{cle}] = '{id}'; SELECT Id FROM @ids;"))!;
}
// Les colonnes recopiees d'une fiche, identiques des deux cotes : oracle independant du moteur (EXCEPT de SQL Server sur la
// ligne V2 et la ligne V3 lue sous ses noms V2, liens traduits par la correspondance, traductions par leur expression).
async Task<long> Differentes(string tableV2, string idV2, string idV3)
{
    var t = T(tableV2);
    string Col3(ColonneV2 c)
    {
        var x = $"y.[{t.ColonneV3(c).Nom}]";
        if (t.Liens.TryGetValue(c.Nom, out var p))
            return $"ISNULL((SELECT TOP 1 CONVERT({c.TypeSql}, k.IdV2) FROM rec.Correspondance k WHERE k.TableV3 = '{p.NomV3}' AND k.IdV3 = CONVERT(nvarchar(200), {x}) " +
                   $"ORDER BY CASE WHEN k.Origine = 'fusion' THEN 1 ELSE 0 END), {x}) AS [{c.Nom}]";
        if (t.TraductionDe(c.Nom) is { } tr) return $"CONVERT({c.TypeSql}, {tr.SqlVersV2(v2.Nom).Replace("@v", x)}) AS [{c.Nom}]";
        return $"{x} AS [{c.Nom}]";
    }
    var a = string.Join(", ", t.Colonnes.Select(c => $"x.[{c.Nom}]"));
    var b = string.Join(", ", t.Colonnes.Select(Col3));
    var q2 = $"SELECT {a} FROM [{v2.Nom}].dbo.[{tableV2}] x WHERE x.[{t.Cle.Nom}] = '{idV2}'";
    var q3 = $"SELECT {b} FROM {t.NomV3Sql} y WHERE y.[{t.CleV3.Nom}] = '{idV3}'";
    return await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM ({q2} EXCEPT {q3}) d") + await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM ({q3} EXCEPT {q2}) d");
}
async Task<string> Identiques(string tableV2, string idV2, string idV3) => await Differentes(tableV2, idV2, idV3) == 0 ? "identiques" : "DIFFERENTES";

var depart = await Passer();
Exiger(depart.EnService && depart.Erreur is null && depart.Appliquees == 0 && depart.Remontees == 0,
    $"premier passage juste apres le transfert : rien a recopier ni a remonter ({depart.Appliquees} appliquees, {depart.Remontees} remontees) {depart.Erreur}");

ServeurBanc? serveur = null;
try
{
    // ====================================================================================================
    // A. La correspondance colonne par colonne, contre le catalogue reel de RIG_DEV
    // ====================================================================================================
    CatalogueV2 rigDev;
    await using (var rd = new SqlConnection(chaineRigDev))
    {
        await rd.OpenAsync();
        rigDev = await CatalogueV2.LireAsync(rd, default);
    }
    // Les colonnes de la V2 qu'une fiche laisse a la V2, une par une (aucune n'est oubliee en silence) : la cle, les colonnes
    // propres et, pour un compte d'agent, les reglages du poste et ce que la V3 n'a pas (mot de passe, disponibilite...).
    // Aucun mot de passe n'entre dans la V3 (SOC-001 CA08) : celui du compte web d'un client reste a la V2.
    var laisseesAttendues = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["CLIENT"] = ["CLIEN_ID_CLIEN", "CLIEN_MDP"],
        ["UTILISATEUR"] = ["UTLST_ID_UTLST", "UTLST_CODE_DIRECTION", "UTLST_ID_SMSTN", "UTLST_PASSWORD", "UTLST_P100_DISPONIBILITE", "UTLST_TEXTE_DISPONIBILITE",
                           "UTLST_TEL_DIRECT", "UTLST_IMPRIMANTE_DEFAUT", "UTLST_IMPRIMANTE_DEFAUT_LOT1", "UTLST_INITIALES", "UTLST_LAST_SERVER",
                           "UTLST_LAST_SESSIONID", "UTLST_BAC_SORTIE", "UTLST_FILE_DIFFERE", "UTLST_DATE_CREATION", "UTLST_SCANNER_DEFAUT", "UTLST_IS_OUTILS",
                           "UTLST_PROFIL_SCANNER_DEFAUT", "UTLST_ID_SCANNER_DEFAUT", "UTLST_IS_MS_OFFICE"],
    };
    var completude = new List<string>();
    foreach (var r in fichesV3)
    {
        var t2 = rigDev.Tables[r.TableV2];
        var t3 = T(r.TableV2).V3;
        var nommees = r.Correspondance!.Select(c => c.V2).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var laissees = t2.Colonnes.Select(c => c.Nom).Where(c => !nommees.Contains(c)).ToList();
        var attendues = laisseesAttendues.GetValueOrDefault(r.TableV2) ?? [t2.ClePrimaire[0], .. r.ColonnesPropres];
        var enTrop = r.Correspondance!.Where(c => !t2.Colonnes.Any(x => string.Equals(x.Nom, c.V2, StringComparison.OrdinalIgnoreCase))).Select(c => c.V2).ToList();
        var v3Seules = t3.Colonnes.Select(c => c.Nom).Where(n => !r.Correspondance!.Any(c => string.Equals(c.V3, n, StringComparison.OrdinalIgnoreCase))
                                                               && !string.Equals(n, r.CleV3, StringComparison.OrdinalIgnoreCase)).ToList();
        var memesTypes = r.Correspondance!.All(c => c.Traduction is not null || t2.Colonne(c.V2).TypeSql == t3.Colonne(c.V3).TypeSql);
        if (!laissees.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(attendues) || enTrop.Count > 0 || !memesTypes)
            completude.Add($"{r.TableV2} : laissees a la V2 [{string.Join(", ", laissees)}], attendues [{string.Join(", ", attendues)}], inconnues de RIG_DEV [{string.Join(", ", enTrop)}], types {(memesTypes ? "egaux" : "DIFFERENTS")}");
        Info($"{r.TableV2} -> {r.TableV3} : {r.Correspondance!.Count} colonnes en correspondance, {laissees.Count} laissees a la V2 ({string.Join(", ", laissees)})" +
             (v3Seules.Count > 0 ? $", de la V3 seule : {string.Join(", ", v3Seules)}" : ""));
    }
    Exiger(completude.Count == 0 && fichesV3.Count == 13,
        $"REC-012 a REC-019 T01/T02 : les {fichesV3.Count} fiches ont leur table V3 et la correspondance de CHAQUE colonne du catalogue de RIG_DEV : nommee, ou laissee a la V2 " +
        $"(cle, etat de transmission, deroulement d'un type, reglages d'un poste, mot de passe), au meme type que la V2 {string.Join(" ; ", completude)}");

    // ====================================================================================================
    // B. Ce que le transfert garde
    // ====================================================================================================
    // REC-012 CA01 : SIREN et numero de gestion, caractere pour caractere (classement binaire et longueur), tous les dossiers.
    var horsNorme = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.DOSSIER_RCS WHERE DSSRC_NUM_GESTION NOT LIKE '[12][0-9][0-9][0-9][A-Z][0-9][0-9][0-9][0-9][0-9]'");
    string ComparerNumeros(string tableV3) => $"""
        SELECT COUNT(*) FROM [{v2.Nom}].dbo.DOSSIER_RCS AS d JOIN rec.Correspondance AS c ON c.BaseV2 = '{v2.Nom}' AND c.TableV2 = 'DOSSIER_RCS' AND c.IdV2 = CONVERT(nvarchar(200), d.DSSRC_ID_DSRCS)
        LEFT JOIN {tableV3} AS x ON CONVERT(nvarchar(200), x.Id) = c.IdV3
        WHERE x.Id IS NULL OR ISNULL(x.NumeroGestion, '~') COLLATE Latin1_General_BIN2 <> ISNULL(d.DSSRC_NUM_GESTION, '~') COLLATE Latin1_General_BIN2
           OR ISNULL(x.Siren, '~') COLLATE Latin1_General_BIN2 <> ISNULL(d.DSSRC_SIREN, '~') COLLATE Latin1_General_BIN2
           OR DATALENGTH(x.NumeroGestion) <> DATALENGTH(d.DSSRC_NUM_GESTION) OR ISNULL(DATALENGTH(x.Siren), -1) <> ISNULL(DATALENGTH(d.DSSRC_SIREN), -1)
        """;
    var dossiersV2 = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.DOSSIER_RCS");
    var numerosDifferents = await Nombre(v3.Connexion, ComparerNumeros("rec.DossierRcs"));
    // Contre-epreuve : un numero de gestion qui perd une espace finale dans la V3 doit etre vu.
    var temoinNum = await Texte(v3.Connexion, "SELECT TOP (1) CONVERT(varchar(20), Id) FROM rec.DossierRcs ORDER BY Id");
    var numTemoin = await Texte(v3.Connexion, $"SELECT NumeroGestion FROM rec.DossierRcs WHERE Id = {temoinNum}");
    await Executer(v3.Connexion, $"DISABLE TRIGGER ALL ON rec.DossierRcs; UPDATE rec.DossierRcs SET NumeroGestion = LOWER(NumeroGestion) WHERE Id = {temoinNum}; ENABLE TRIGGER ALL ON rec.DossierRcs;");
    var contreNum = await Nombre(v3.Connexion, ComparerNumeros("rec.DossierRcs"));
    await Executer(v3.Connexion, $"DISABLE TRIGGER ALL ON rec.DossierRcs; UPDATE rec.DossierRcs SET NumeroGestion = '{numTemoin}' WHERE Id = {temoinNum}; ENABLE TRIGGER ALL ON rec.DossierRcs;");
    Exiger(numerosDifferents == 0 && horsNorme > 0 && contreNum == 1,
        $"REC-012 CA01 : les {dossiersV2} dossiers transferes ont le meme SIREN et le meme numero de gestion dans la V2 et la V3, caractere pour caractere (classement binaire, " +
        $"meme longueur), dont {horsNorme} numeros hors norme (« 1980AC0059 ») ; contre-epreuve : un numero passe en minuscules dans la V3 est vu ({contreNum} ecart)");

    // REC-016 CA02 et CA03 : l'adresse partagee et l'adresse inconnue.
    string References(string a) => $"""
        SELECT a, COUNT(*) FROM (SELECT {(a == "V2" ? "ENTRP_ID_ADRBS" : "IdAdresse")} AS a FROM {(a == "V2" ? $"[{v2.Nom}].dbo.ENTREPRISE" : "rec.Entreprise")}
                                 UNION ALL SELECT {(a == "V2" ? "CLIAD_ID_ADRBS" : "IdAdresse")} FROM {(a == "V2" ? $"[{v2.Nom}].dbo.CLIENT_ADRESSE" : "rec.ClientAdresse")}
                                 UNION ALL SELECT {(a == "V2" ? "INEDT_ID_ADRBS" : "IdAdresse")} FROM {(a == "V2" ? $"[{v2.Nom}].dbo.INSCRIPTION" : "rec.Inscription")}) AS r
        WHERE a IS NOT NULL GROUP BY a HAVING COUNT(*) > 1
        """;
    var partagesV2 = (await Lire(v3.Connexion, References("V2"))).Rows.Cast<DataRow>().Select(r => (Id: Convert.ToString(r[0])!, N: Convert.ToInt64(r[1]))).ToList();
    var partagesV3 = (await Lire(v3.Connexion, References("V3"))).Rows.Cast<DataRow>().ToDictionary(r => Convert.ToString(r[0])!, r => Convert.ToInt64(r[1]));
    var horsInconnue = partagesV2.Where(p => !inconnuesV2.Contains(p.Id)).ToList();
    Exiger(horsInconnue.Count > 0 && horsInconnue.All(p => partagesV3.GetValueOrDefault(p.Id) == p.N)
           && await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.Adresse") + inconnuesV2.Count - 1 == await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.ADRESSE_BASE"),
        $"REC-016 CA02 : {horsInconnue.Count} adresse(s) de la V2 referencee(s) par plusieurs fiches (entreprise, adresse de client, inscription) le restent dans la V3, " +
        $"une seule adresse referencee par les memes fiches : {string.Join(", ", horsInconnue.Take(5).Select(p => $"{p.Id} x{p.N} -> x{partagesV3.GetValueOrDefault(p.Id)}"))} ; " +
        $"rec.Adresse a les adresses de la V2 moins les {Math.Max(0, inconnuesV2.Count - 1)} inconnue(s) rattachee(s)");
    var inconnuesV3 = Colonne0(await Lire(v3.Connexion, $"SELECT Id FROM rec.Adresse WHERE NomVoie = '{Inconnue}'"));
    var gardee = await Texte(v3.Connexion, "SELECT Id FROM rec.Adresse WHERE EstInconnue = 1");
    var visees = inconnuesV2.Count < 2 ? -1 : await Nombre(v3.Connexion,
        $"SELECT COUNT(*) FROM rec.ClientAdresse WHERE Id IN ({adressesClients[0]}, {adressesClients[1]}) AND IdAdresse = {gardee}") +
        await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.Entreprise WHERE Id = {entreprisesAdr[2]} AND IdAdresse = {gardee}");
    var correspInconnues = inconnuesV2.Count < 2 ? -1 : await Nombre(v3.Connexion,
        $"SELECT COUNT(DISTINCT IdV3) FROM rec.Correspondance WHERE BaseV2 = '{v2.Nom}' AND TableV2 = 'ADRESSE_BASE' AND IdV2 IN ({string.Join(", ", inconnuesV2.Select(i => $"'{i}'"))})");
    Exiger(inconnuesV2.Count == 2 && inconnuesV3.Count == 1 && gardee == inconnuesV3[0] && visees == 3 && correspInconnues == 1,
        $"REC-016 CA03 : les {inconnuesV2.Count} adresses inconnues de la V2 ({string.Join(", ", inconnuesV2)}) deviennent UNE adresse inconnue dans la V3 ({gardee}, EstInconnue), " +
        $"reconnue comme telle ; les 3 fiches qui visaient l'une ou l'autre la visent ({visees}) ; les deux correspondances menent a elle ({correspInconnues} adresse V3)");

    // REC-013 T01 et CA02 au transfert : codes abreges, agents par leur compte de session.
    var doublonsV2 = await Nombre(v2.Connexion, "SELECT COUNT(*) - COUNT(DISTINCT CLIEN_ABREGE) FROM dbo.CLIENT WHERE CLIEN_ABREGE IS NOT NULL");
    var herites = await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.Client WHERE DoublonHerite = 1");
    var agentsFaux = await Nombre(v3.Connexion, $"""
        SELECT COUNT(*) FROM [{v2.Nom}].dbo.CLIENT AS c JOIN rec.Client AS x ON x.Id = c.CLIEN_ID_CLIEN
        LEFT JOIN [{v2.Nom}].dbo.UTILISATEUR AS u ON u.UTLST_ID_UTLST = c.CLIEN_ID_UTLST LEFT JOIN soc.Compte AS a ON a.Id = x.IdCompteRattachement
        WHERE ISNULL(u.UTLST_LOGIN, '~') <> ISNULL(a.CompteWindows, '~')
        """);
    var agentsRattaches = await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.Client WHERE IdCompteRattachement IS NOT NULL");
    var idsDifferents = await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.Client AS x JOIN soc.Compte AS a ON a.Id = x.IdCompteRattachement WHERE a.IdV2 <> a.Id");
    Exiger(doublonsV2 > 0 && herites == doublonsV2 && agentsFaux == 0 && agentsRattaches > 0 && idsDifferents > 0,
        $"REC-013 T01 et CA02 au transfert : les {doublonsV2} codes abreges portes deux fois par la V2 sont gardes et marques « doublon herite » ({herites}), le code est unique " +
        $"pour tout le reste ; les {agentsRattaches} clients rattaches a un agent visent dans la V3 le compte de meme nom de session ({agentsFaux} faux), alors que " +
        $"{idsDifferents} d'entre eux ont un identifiant V3 different de celui de la V2");

    // REC-019 CA01 et CA05 : tous les types de RIG_DEV, par leur reference.
    var typesV2 = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.CATEGORIE");
    var typesSansCorresp = await Nombre(v3.Connexion, $"""
        SELECT COUNT(*) FROM [{v2.Nom}].dbo.CATEGORIE AS c
        LEFT JOIN rec.Correspondance AS k ON k.BaseV2 = '{v2.Nom}' AND k.TableV2 = 'CATEGORIE' AND k.IdV2 = CONVERT(nvarchar(200), c.CTGR_ID_CTGR) AND k.TableV3 = 'rec.TypeDemande'
        LEFT JOIN rec.TypeDemande AS t ON CONVERT(nvarchar(200), t.Id) = k.IdV3
        WHERE t.Id IS NULL OR t.Reference COLLATE Latin1_General_BIN2 <> c.CTGR_REFERENCE COLLATE Latin1_General_BIN2 OR t.Niveau <> c.CTGR_NIVEAU
        """);
    var parentsFaux = await Nombre(v3.Connexion, $"""
        SELECT COUNT(*) FROM [{v2.Nom}].dbo.CATEGORIE AS c
        JOIN rec.Correspondance AS k ON k.BaseV2 = '{v2.Nom}' AND k.TableV2 = 'CATEGORIE' AND k.IdV2 = CONVERT(nvarchar(200), c.CTGR_ID_CTGR)
        JOIN rec.TypeDemande AS t ON CONVERT(nvarchar(200), t.Id) = k.IdV3
        CROSS APPLY (VALUES (c.CTGR_ID_CTGR_DOMAINE, t.IdDomaine), (c.CTGR_ID_CTGR_FAMILLE, t.IdFamille), (c.CTGR_ID_CTGR_ACTIVITE, t.IdActivite)) AS p (V2, V3)
        WHERE ISNULL(CONVERT(nvarchar(200), p.V3), '~') <> ISNULL((SELECT IdV3 FROM rec.Correspondance AS q WHERE q.BaseV2 = '{v2.Nom}' AND q.TableV2 = 'CATEGORIE'
                                                                    AND q.IdV2 = CONVERT(nvarchar(200), p.V2)), '~')
        """);
    var memeNumero = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.Correspondance WHERE BaseV2 = '{v2.Nom}' AND TableV2 = 'CATEGORIE' AND IdV2 = IdV3");
    var colonnesNumero = await Nombre(v3.Connexion, "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('rec.TypeDemande') AND (name LIKE '%V2%' OR name LIKE 'CTGR%')");
    Exiger(typesV2 == await Nombre(chaineRigDev, "SELECT COUNT(*) FROM dbo.CATEGORIE") && typesSansCorresp == 0 && parentsFaux == 0 && colonnesNumero == 0 && memeNumero < typesV2 / 10,
        $"REC-019 CA01 : les {typesV2} types de RIG_DEV (la table entiere) sont dans la V3, chacun retrouve par sa reference et son niveau ({typesSansCorresp} sans), " +
        $"ses parents traduits par la correspondance ({parentsFaux} faux) ; la V3 leur donne SON identifiant ({memeNumero} seulement egaux au numero V2 par hasard), " +
        "et le numero V2 n'est que dans rec.Correspondance (aucune colonne de rec.TypeDemande ne le porte)");
    var doubles = (await Lire(v3.Connexion, $"""
        SELECT t.Reference, t.Niveau, COUNT(*) FROM rec.TypeDemande AS t WHERE t.Reference IN ('AMITEL', 'EDIT_PRV', 'PREVENTION') GROUP BY t.Reference, t.Niveau
        """)).Rows.Cast<DataRow>().Select(r => $"{r[0]} niveau {r[1]} x{r[2]}").ToList();
    var indiscernables = await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.TypesDemandeIndiscernables");
    Exiger(doubles.Count == 7 && doubles.All(d => d.EndsWith("x1")) && indiscernables == 0,
        $"REC-019 CA05 sur RIG_DEV : les references portees plusieurs fois sont distinguees par leur niveau et leurs parents ({string.Join(", ", doubles)}) ; " +
        $"aucune entree indiscernable ({indiscernables}) -- mesure : les trois AMITEL sont aux niveaux 1, 2 et 3 (T08 en annoncait deux indiscernables) ; " +
        "la liste d'un cas indiscernable est prouvee sur la base de RIG_RECETTE (D)");

    // ====================================================================================================
    // C. La recopie dans les deux sens, fiche par fiche
    // ====================================================================================================
    // --- REC-012 ---------------------------------------------------------------------------------------
    // CA02 : une modification de la denomination faite dans la V3 (une affaire de modification : la fiche de personne et la date
    // du dossier), dont un changement de casse seul.
    var dossierCa02 = dossierF!;
    var entrepriseCa02 = await Texte(v3.Connexion, $"SELECT IdEntreprise FROM rec.DossierRcs WHERE Id = {dossierCa02}")
                         ?? await Texte(v3.Connexion, "SELECT TOP (1) IdEntreprise FROM rec.DossierRcs WHERE IdEntreprise IS NOT NULL ORDER BY Id");
    var etatAvantCa02 = await Texte(v2.Connexion, $"SELECT CONCAT(DSSRC_ETAT_TRANSMISSION, '|', CONVERT(varchar(30), DSSRC_DATE_MODIF_FLAG_TRANS, 126)) FROM dbo.DOSSIER_RCS WHERE DSSRC_ID_DSRCS = {dossierCa02}");
    await Executer(v3.Connexion, V3Ecrit($"UPDATE rec.Entreprise SET Denomination = 'NOUVELLE DENOMINATION DU BANC' WHERE Id = {entrepriseCa02}; " +
                                         $"UPDATE rec.DossierRcs SET DateMaj = '2026-11-21T10:00:00', CompteurMaj = CompteurMaj + 1 WHERE Id = {dossierCa02};"));
    var p12a = await Passer();
    await Executer(v3.Connexion, V3Ecrit($"UPDATE rec.Entreprise SET Denomination = 'Nouvelle Denomination du banc' WHERE Id = {entrepriseCa02};"));
    var fileCasse = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.FileRecopie WHERE TableV3 = 'rec.Entreprise' AND IdV3 = '{entrepriseCa02}' AND Etat = 'en attente'");
    var p12b = await Passer();
    var entrepriseV2 = await V2De("ENTREPRISE", entrepriseCa02!);
    var denominationV2 = await Texte(v2.Connexion, $"SELECT ENTRP_DESIGNATION FROM dbo.ENTREPRISE WHERE ENTRP_ID_RPRTR = {entrepriseV2} AND ENTRP_DESIGNATION = 'Nouvelle Denomination du banc' COLLATE Latin1_General_BIN2");
    var etatApresCa02 = await Texte(v2.Connexion, $"SELECT CONCAT(DSSRC_ETAT_TRANSMISSION, '|', CONVERT(varchar(30), DSSRC_DATE_MODIF_FLAG_TRANS, 126)) FROM dbo.DOSSIER_RCS WHERE DSSRC_ID_DSRCS = {dossierCa02}");
    var dateMajV2 = await Texte(v2.Connexion, $"SELECT CONVERT(varchar(19), DSSRC_DATE_MAJ, 126) FROM dbo.DOSSIER_RCS WHERE DSSRC_ID_DSRCS = {dossierCa02}");
    Exiger(p12a.Erreur is null && p12b.Erreur is null && denominationV2 is not null && fileCasse == 1 && dateMajV2 == "2026-11-21T10:00:00" && etatApresCa02 == etatAvantCa02
           && etatAvantCa02!.StartsWith('F'),
        $"REC-012 CA02 : la denomination changee dans la V3 se lit dans la V2 (« {denominationV2} », changement de casse seul compris : {fileCasse} ligne de file) et le dossier " +
        $"modifie n'est PAS marque « a transmettre » par la V2 (etat {etatAvantCa02} -> {etatApresCa02}) : Infogreffe ne la recoit que de la V3");
    // Contre-epreuve de la casse : le declencheur de file d'avant (comparaison au classement de la base) ne voit pas un changement de casse.
    var nomDeclencheur = $"[{T("ENTREPRISE").V3.Schema}].[{PontRecopie.NomDeclencheur(T("ENTREPRISE"))}]";
    var declencheur = (await Texte(v3.Connexion, $"SELECT OBJECT_DEFINITION(OBJECT_ID('{nomDeclencheur}'))"))!;
    await Executer(v3.Connexion, $"DROP TRIGGER {nomDeclencheur};");
    await Executer(v3.Connexion, declencheur.Replace(" COLLATE Latin1_General_BIN2", ""));
    await Executer(v3.Connexion, V3Ecrit($"UPDATE rec.Entreprise SET Denomination = 'NOUVELLE DENOMINATION DU BANC' WHERE Id = {entrepriseCa02};"));
    var fileSansBin = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.FileRecopie WHERE TableV3 = 'rec.Entreprise' AND IdV3 = '{entrepriseCa02}' AND Etat = 'en attente'");
    await Executer(v3.Connexion, $"DROP TRIGGER {nomDeclencheur};");
    await Executer(v3.Connexion, declencheur);
    await Executer(v3.Connexion, V3Ecrit($"UPDATE rec.Entreprise SET Denomination = 'Nouvelle Denomination du banc' WHERE Id = {entrepriseCa02};"));
    await Passer();
    Exiger(fileSansBin == 0,
        $"contre-epreuve REC-012 CA02 : sans la comparaison binaire du declencheur de file, le changement de casse de la denomination n'entre pas dans la file ({fileSansBin} ligne) -- " +
        "il n'arrivait jamais dans la V2");

    // CA03 : une mention de procedure collective portee par la V2 sur un dossier ancien.
    var dossierCa03 = await Texte(v2.Connexion, $"SELECT TOP (1) DSSRC_ID_DSRCS FROM dbo.DOSSIER_RCS WHERE DSSRC_PROCEDURE_PC IS NULL AND DSSRC_ID_DSRCS <> {dossierF} ORDER BY DSSRC_ID_DSRCS");
    await Executer(v2.Connexion, $"UPDATE dbo.DOSSIER_RCS SET DSSRC_PROCEDURE_PC = 'RJ', DSSRC_DATE_MAJ = '2026-11-21T11:00:00' WHERE DSSRC_ID_DSRCS = {dossierCa03};");
    var p12c = await Passer();
    var pcV3 = await Texte(v3.Connexion, $"SELECT ProcedureCollective FROM rec.DossierRcs WHERE Id = {await V3De("DOSSIER_RCS", dossierCa03!)}");
    var transmisParV2 = await Texte(v2.Connexion, $"SELECT DSSRC_ETAT_TRANSMISSION FROM dbo.DOSSIER_RCS WHERE DSSRC_ID_DSRCS = {dossierCa03}");
    var renvoi = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.FileRecopie WHERE TableV3 = 'rec.DossierRcs' AND IdV3 = '{await V3De("DOSSIER_RCS", dossierCa03!)}'");
    Exiger(p12c.Erreur is null && pcV3 == "RJ" && transmisParV2 == "M" && renvoi == 0,
        $"REC-012 CA03 : la procedure collective portee par la V2 sur un dossier ancien remonte (V3 : « {pcV3} ») ; c'est la V2 qui la transmet (son declencheur l'a marque " +
        $"« {transmisParV2} »), la V3 ne la renvoie pas ({renvoi} ligne de file)");

    // CA04 : une entreprise nouvelle immatriculee dans la V3 : sa fiche de personne et son dossier.
    var modeleEntreprise = await Texte(v3.Connexion, "SELECT TOP (1) CONVERT(varchar(20), Id) FROM rec.Entreprise ORDER BY Id DESC");
    var modeleDossier = await Texte(v3.Connexion, "SELECT TOP (1) CONVERT(varchar(20), Id) FROM rec.DossierRcs ORDER BY Id DESC");
    var nouvelle = await Dupliquer(v3.Connexion, "rec.Entreprise", T("ENTREPRISE").V3, modeleEntreprise!, "Denomination='IMMATRICULEE DANS LA V3';Siren='999888777'", V3Ecrit(""));
    var nouveauDossier = await Dupliquer(v3.Connexion, "rec.DossierRcs", T("DOSSIER_RCS").V3, modeleDossier!,
        $"IdEntreprise={nouvelle};NumeroGestion='2026B90001';Siren='999888777'", V3Ecrit(""));
    var p12d = await Passer();
    var trouve = await Lire(v2.Connexion, "SELECT d.DSSRC_ID_DSRCS, e.ENTRP_DESIGNATION, d.DSSRC_ETAT_TRANSMISSION FROM dbo.DOSSIER_RCS d JOIN dbo.ENTREPRISE e ON e.ENTRP_ID_RPRTR = d.DSSRC_ID_RPRTR " +
                                          "WHERE d.DSSRC_SIREN = '999888777' AND d.DSSRC_NUM_GESTION = '2026B90001'");
    Exiger(p12d.Erreur is null && trouve.Rows.Count == 1 && (string)trouve.Rows[0][1] == "IMMATRICULEE DANS LA V3" && (string)trouve.Rows[0][2] == "N"
           && Convert.ToString(trouve.Rows[0][0]) == await V2De("DOSSIER_RCS", nouveauDossier),
        $"REC-012 CA04 : l'entreprise immatriculee dans la V3 a dans la V2 un dossier et une fiche de personne lies, retrouvables par SIREN et numero de gestion " +
        $"({trouve.Rows.Count} trouve, etat de transmission « {(trouve.Rows.Count > 0 ? trouve.Rows[0][2] : "")} » : la V2 ne l'envoie pas)");

    // --- REC-013 ---------------------------------------------------------------------------------------
    var modeleClient = await Texte(v3.Connexion, "SELECT TOP (1) CONVERT(varchar(20), Id) FROM rec.Client WHERE DoublonHerite = 0 ORDER BY Id DESC");
    var modeleAdresseClient = await Texte(v3.Connexion, "SELECT TOP (1) CONVERT(varchar(20), Id) FROM rec.ClientAdresse ORDER BY Id DESC");
    // CA01 : un client cree dans la V3, avec son adresse.
    var clientCa01 = await Dupliquer(v3.Connexion, "rec.Client", T("CLIENT").V3, modeleClient!, "Abrege='BANC13A';Nom='CLIENT CREE DANS LA V3';IdCompteRattachement=NULL", V3Ecrit(""));
    var adresseCa01 = await Dupliquer(v3.Connexion, "rec.ClientAdresse", T("CLIENT_ADRESSE").V3, modeleAdresseClient!,
        $"IdClient={clientCa01};LigneAdresse='12 RUE DU BANC';CodePostal='80000';Localite='AMIENS';IdAdresse=NULL", V3Ecrit(""));
    var p13a = await Passer();
    var parAbrege = await Lire(v2.Connexion, "SELECT c.CLIEN_ID_CLIEN, c.CLIEN_NOM, a.CLIAD_LIGNE_ADRESSE, a.CLIAD_CODE_POSTAL, a.CLIAD_LOCALITE_POSTALE FROM dbo.CLIENT c " +
                                             "LEFT JOIN dbo.CLIENT_ADRESSE a ON a.CLIAD_ID_CLIEN = c.CLIEN_ID_CLIEN WHERE c.CLIEN_ABREGE = 'BANC13A'");
    Exiger(p13a.Erreur is null && parAbrege.Rows.Count == 1 && (string)parAbrege.Rows[0][1] == "CLIENT CREE DANS LA V3" && parAbrege.Rows[0][2] as string == "12 RUE DU BANC"
           && parAbrege.Rows[0][4] as string == "AMIENS" && await Differentes("CLIENT", Convert.ToString(parAbrege.Rows[0][0])!, clientCa01) == 0,
        $"REC-013 CA01 : le client cree dans la V3 se trouve dans la V2 par son code abrege (BANC13A : {parAbrege.Rows.Count}), meme nom, meme adresse " +
        $"({(parAbrege.Rows.Count > 0 ? $"{parAbrege.Rows[0][2]}, {parAbrege.Rows[0][3]} {parAbrege.Rows[0][4]}" : "")}), fiche identique colonne par colonne");

    // CA02 : l'agent de rattachement, dans un sens puis dans l'autre, par le compte de session.
    var agents = (await Lire(v3.Connexion, "SELECT TOP (2) Id, IdV2, CompteWindows FROM soc.Compte WHERE IdV2 IS NOT NULL AND IdV2 <> Id ORDER BY Id DESC")).Rows.Cast<DataRow>().ToList();
    var clientCa02 = await Texte(v3.Connexion, "SELECT TOP (1) CONVERT(varchar(20), Id) FROM rec.Client WHERE DoublonHerite = 0 AND Id <> " + clientCa01 + " ORDER BY Id");
    await Executer(v3.Connexion, V3Ecrit($"UPDATE rec.Client SET IdCompteRattachement = {agents[0][0]} WHERE Id = {clientCa02};"));
    var p13b = await Passer();
    var loginV2 = await Texte(v2.Connexion, $"SELECT u.UTLST_LOGIN FROM dbo.CLIENT c JOIN dbo.UTILISATEUR u ON u.UTLST_ID_UTLST = c.CLIEN_ID_UTLST WHERE c.CLIEN_ID_CLIEN = {await V2De("CLIENT", clientCa02!)}");
    var idV2Agent = await Texte(v2.Connexion, $"SELECT CLIEN_ID_UTLST FROM dbo.CLIENT WHERE CLIEN_ID_CLIEN = {await V2De("CLIENT", clientCa02!)}");
    await Executer(v2.Connexion, $"UPDATE dbo.CLIENT SET CLIEN_ID_UTLST = {agents[1][1]}, CLIEN_DATE_DERNIERE_MODIF = '2026-11-21T12:00:00' WHERE CLIEN_ID_CLIEN = {await V2De("CLIENT", clientCa02!)};");
    var p13c = await Passer();
    var agentV3 = await Texte(v3.Connexion, $"SELECT a.CompteWindows FROM rec.Client c JOIN soc.Compte a ON a.Id = c.IdCompteRattachement WHERE c.Id = {clientCa02}");
    var idV3Agent = await Texte(v3.Connexion, $"SELECT IdCompteRattachement FROM rec.Client WHERE Id = {clientCa02}");
    Exiger(p13b.Erreur is null && p13c.Erreur is null && loginV2 == (string)agents[0][2] && idV2Agent == Convert.ToString(agents[0][1]) && idV2Agent != Convert.ToString(agents[0][0])
           && agentV3 == (string)agents[1][2] && idV3Agent == Convert.ToString(agents[1][0]),
        $"REC-013 CA02 : l'agent de rattachement d'un client vise le meme agent dans les deux sens, retrouve par son compte de session : V3 -> V2 « {agents[0][2]} » " +
        $"(V3 n° {agents[0][0]} -> V2 n° {idV2Agent}), V2 -> V3 « {agentV3} » (V2 n° {agents[1][1]} -> V3 n° {idV3Agent}), jamais le meme numero d'une base a l'autre");

    // CA03 : le meme code abrege cree au meme moment dans la V2 et dans la V3.
    var modeleClientV2 = await Texte(v2.Connexion, "SELECT TOP (1) CONVERT(varchar(20), CLIEN_ID_CLIEN) FROM dbo.CLIENT ORDER BY CLIEN_ID_CLIEN DESC");
    var clientV2Ca03 = await Dupliquer(v2.Connexion, "dbo.CLIENT", T("CLIENT").V2, modeleClientV2!, "CLIEN_ABREGE='BANC13D';CLIEN_NOM='CREE DANS LA V2'");
    var clientV3Ca03 = await Dupliquer(v3.Connexion, "rec.Client", T("CLIENT").V3, modeleClient!, "Abrege='BANC13D';Nom='CREE DANS LA V3';IdCompteRattachement=NULL", V3Ecrit(""));
    var p13d = await Passer();
    var codesV3 = Colonne0(await Lire(v3.Connexion, "SELECT Nom FROM rec.Client WHERE Abrege = 'BANC13D'"));
    var echecCa03 = (await Lire(v3.Connexion, $"SELECT Etat, Erreur FROM rec.RemonteeEchec WHERE TableV2 = 'CLIENT' AND IdV2 = '{clientV2Ca03}'")).Rows.Cast<DataRow>().FirstOrDefault();
    var ecartCa03 = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.Ecart e JOIN rec.EcartChamp c ON c.IdEcart = e.Id WHERE e.TableV2 = 'CLIENT' AND e.IdV2 = '{clientV2Ca03}' AND c.Champ = 'CLIEN_ABREGE' AND e.TraiteLe IS NULL");
    Exiger(p13d.Erreur is null && codesV3.SequenceEqual(["CREE DANS LA V3"]) && echecCa03 is not null && (string)echecCa03[0] == "mise de cote"
           && ((string)echecCa03[1]).Contains("code abrege deja pris") && ecartCa03 == 1 && await V2De("CLIENT", clientV3Ca03) is not null,
        $"REC-013 CA03 : le meme code abrege cree des deux cotes n'est pas un doublon en silence : le client V3 garde le code (V3 : {string.Join(", ", codesV3)}), celui de la V2 est mis " +
        $"de cote d'emblee (« {echecCa03?[1]} ») et l'ecart est ouvert pour l'agent designe ({ecartCa03})");

    // CA04 : une affaire V3 pour un client de la V2 : elle vise le client correspondant, sans copie.
    var clientV2Ca04 = await Texte(v2.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), CLIEN_ID_CLIEN) FROM dbo.CLIENT WHERE CLIEN_ABREGE IS NOT NULL AND CLIEN_ID_CLIEN <> {clientV2Ca03} ORDER BY CLIEN_ID_CLIEN");
    var clientsAvant = await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.Client");
    var modeleAffaire = await Texte(v3.Connexion, "SELECT TOP (1) CONVERT(varchar(20), AFFRE_ID_AFFRE) FROM v2.AFFAIRE ORDER BY AFFRE_ID_AFFRE DESC");
    var catV3 = await CatalogueAsync(v3.Connexion);
    var affaireV3 = await Dupliquer(v3.Connexion, "v2.AFFAIRE", catV3.Tables["v2.AFFAIRE"], modeleAffaire!, $"AFFRE_ID_CLIEN={await V3De("CLIENT", clientV2Ca04!)}");
    var vise = await Lire(v3.Connexion, $"SELECT c.Abrege, c.Nom FROM v2.AFFAIRE a JOIN rec.Client c ON c.Id = a.AFFRE_ID_CLIEN WHERE a.AFFRE_ID_AFFRE = {affaireV3}");
    var dansV2 = await Lire(v2.Connexion, $"SELECT CLIEN_ABREGE, CLIEN_NOM FROM dbo.CLIENT WHERE CLIEN_ID_CLIEN = {clientV2Ca04}");
    Exiger(vise.Rows.Count == 1 && Equals(vise.Rows[0][0], dansV2.Rows[0][0]) && Equals(vise.Rows[0][1], dansV2.Rows[0][1]) && await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.Client") == clientsAvant,
        $"REC-013 CA04 : l'affaire creee dans la V3 pour le client {dansV2.Rows[0][0]} de la V2 pointe vers sa fiche correspondante (« {(vise.Rows.Count > 0 ? vise.Rows[0][1] : "")} »), " +
        "sans copie du client");

    // CA05 : une adresse et des contacts d'un client partage modifies dans la V3 ; un contact retire dans la V3.
    var adresseCa05 = await Texte(v3.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), Id) FROM rec.ClientAdresse WHERE Id <> {adresseCa01} AND IdClient IS NOT NULL ORDER BY Id");
    await Executer(v3.Connexion, V3Ecrit($"UPDATE rec.ClientAdresse SET LigneAdresse = '5 PLACE MODIFIEE', Localite = 'AMIENS CEDEX' WHERE Id = {adresseCa05};"));
    var contactGarde = (await Texte(v3.Connexion, V3Ecrit($"DECLARE @i TABLE (Id int); INSERT INTO rec.ClientContact (IdClient, Nom, Prenom, Email) OUTPUT inserted.Id INTO @i " +
                                                          $"VALUES ({clientCa02}, 'GARDE', 'Contact', 'garde@banc.fr'); SELECT Id FROM @i;")))!;
    var contactRetire = (await Texte(v3.Connexion, V3Ecrit($"DECLARE @i TABLE (Id int); INSERT INTO rec.ClientContact (IdClient, Nom, Prenom, Email) OUTPUT inserted.Id INTO @i " +
                                                           $"VALUES ({clientCa02}, 'RETIRE', 'Contact', 'retire@banc.fr'); SELECT Id FROM @i;")))!;
    var p13e = await Passer();
    var contactRetireV2 = await V2De("CLIENT_CONTACT", contactRetire);
    await Executer(v3.Connexion, V3Ecrit($"DELETE FROM rec.ClientContact WHERE Id = {contactRetire};"));
    var p13f = await Passer();
    var contactsV2 = Colonne0(await Lire(v2.Connexion, $"SELECT CLICO_NOM FROM dbo.CLIENT_CONTACT WHERE CLICO_ID_CLIEN = {await V2De("CLIENT", clientCa02!)} ORDER BY CLICO_NOM"));
    Exiger(p13e.Erreur is null && p13f.Erreur is null && await Differentes("CLIENT_ADRESSE", (await V2De("CLIENT_ADRESSE", adresseCa05!))!, adresseCa05!) == 0
           && contactsV2.SequenceEqual(["GARDE"]) && contactRetireV2 is not null && await V2De("CLIENT_CONTACT", contactRetire) is null
           && await Differentes("CLIENT_CONTACT", (await V2De("CLIENT_CONTACT", contactGarde))!, contactGarde) == 0,
        $"REC-013 CA05 : l'adresse modifiee dans la V3 est la meme dans la V2 ({await Identiques("CLIENT_ADRESSE", (await V2De("CLIENT_ADRESSE", adresseCa05!))!, adresseCa05!)}), " +
        $"les contacts aussi, et le contact retire dans la V3 n'est plus propose par la V2 (contacts V2 : {string.Join(", ", contactsV2)})");

    // CA06 : une provision reprise, consommee une seule fois.
    var provisions = Colonne0(await Lire(v2.Connexion, "SELECT TOP (2) ENRPS_NUMERO_TITRE FROM dbo.ENCAISSEMENT_REPRIS WHERE ENRPS_CONSOMMER = 0 ORDER BY ENRPS_NUMERO_TITRE"));
    string Solde(string baseSql, string table, string montant, string consomme, string nom) =>
        $"SELECT CONVERT(varchar(40), ISNULL(SUM(TRY_CONVERT(money, REPLACE({montant}, ',', '.'))), 0)) + '|' + CONVERT(varchar(10), COUNT(*)) FROM {baseSql}{table} " +
        $"WHERE {consomme} = 0 AND {nom} = (SELECT {nom} FROM {baseSql}{table} WHERE {(table.Contains("ENCAISSEMENT") ? "ENRPS_NUMERO_TITRE" : "NumeroTitre")} = '{provisions[0]}')";
    // La V2 consomme la premiere (Encaissement.Sauve : UPDATE conditionnel), puis la V3 essaie, AVANT tout passage.
    await Executer(v2.Connexion, $"Update ENCAISSEMENT_REPRIS Set ENRPS_CONSOMMER=1 Where ENRPS_NUMERO_TITRE= '{provisions[0]}' AND ENRPS_CONSOMMER = 0");
    var refusV3 = await ProvisionsReprises.ConsommerAsync(v3.Connexion, v2.Nom, provisions[0]);
    // La V3 consomme la premiere, puis la V2 essaie.
    var okV3 = await ProvisionsReprises.ConsommerAsync(v3.Connexion, v2.Nom, provisions[1]);
    int lignesV2;
    await using (var cn = new SqlConnection(v2.Connexion))
    {
        await cn.OpenAsync();
        await using var cmd = new SqlCommand($"Update ENCAISSEMENT_REPRIS Set ENRPS_CONSOMMER=1 Where ENRPS_NUMERO_TITRE= '{provisions[1]}' AND ENRPS_CONSOMMER = 0", cn);
        lignesV2 = await cmd.ExecuteNonQueryAsync();
    }
    var p13g = await Passer();
    var etatsV3 = Colonne0(await Lire(v3.Connexion, $"SELECT CONVERT(varchar(5), Consommee) FROM rec.ProvisionReprise WHERE NumeroTitre IN ('{provisions[0]}', '{provisions[1]}') ORDER BY NumeroTitre"));
    var soldeV2 = await Texte(v3.Connexion, Solde($"[{v2.Nom}].dbo.", "ENCAISSEMENT_REPRIS", "ENRPS_MONTANT", "ENRPS_CONSOMMER", "ENRPS_NOM_DOSSIER"));
    var soldeV3 = await Texte(v3.Connexion, Solde("rec.", "ProvisionReprise", "Montant", "Consommee", "NomDossier"));
    var ecartsProvision = await Nombre(v3.Connexion, "SELECT COUNT(*) FROM rec.Ecart WHERE TableV2 = 'ENCAISSEMENT_REPRIS'");
    Exiger(!refusV3.Consommee && refusV3.Motif!.Contains("deja utilisee") && okV3.Consommee && lignesV2 == 0 && p13g.Erreur is null
           && etatsV3.SequenceEqual(["1", "1"]) && soldeV2 == soldeV3 && ecartsProvision == 0,
        $"REC-013 CA06 : une provision reprise ne se consomme qu'une fois : consommee d'abord par la V2, la V3 refuse avec le motif (« {refusV3.Motif} ») ; consommee d'abord par " +
        $"la V3 (reservee dans la V2), l'UPDATE de la V2 ne touche plus aucune ligne ({lignesV2}) ; solde du client identique des deux cotes ({soldeV2} / {soldeV3}), aucun ecart");

    // --- REC-014 ---------------------------------------------------------------------------------------
    // CA01 : une inscription d'origine V2 renouvelee dans la V3.
    var origine = await Texte(v3.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), Id) FROM rec.Inscription WHERE Id <> {inscriptionF} ORDER BY Id DESC");
    var renouvelee = await Dupliquer(v3.Connexion, "rec.Inscription", T("INSCRIPTION").V3, origine!,
        $"IdInscriptionPrecedente={origine};NumeroInscription='B14R00001';DateInscription='2026-11-21'", V3Ecrit(""));
    var p14a = await Passer();
    var renouveleeV2 = await V2De("INSCRIPTION", renouvelee);
    var precedenteV2 = await Texte(v2.Connexion, $"SELECT INEDT_ID_INSCR_INSCRIPTION_PRE FROM dbo.INSCRIPTION WHERE INEDT_ID_INSCR = {renouveleeV2}");
    Exiger(p14a.Erreur is null && precedenteV2 == await V2De("INSCRIPTION", origine!) && await Differentes("INSCRIPTION", renouveleeV2!, renouvelee) == 0,
        $"REC-014 CA01 : l'inscription V2 {await V2De("INSCRIPTION", origine!)} renouvelee dans la V3 : la nouvelle pointe vers elle par la correspondance, et la V2 voit la meme chaine " +
        $"(V2 {renouveleeV2} -> {precedenteV2})");

    // CA02 : une inscription V3 avec sa creance et son bateau.
    var creance = (await Texte(v3.Connexion, V3Ecrit("DECLARE @i TABLE (Id int); INSERT INTO rec.Creance (LiquideEvaluee, Monnaie, MontantPrincipal, SaufMemoire, AlienableChosesFongibles, " +
        "AlienablePartsSociales, ActionResolutoire, PacteCommissoire, MarqueBrevetDessinModele) OUTPUT inserted.Id INTO @i VALUES (1, 'EUR', 15000, 0, 0, 0, 0, 0, 0); SELECT Id FROM @i;")))!;
    var bateau = (await Texte(v3.Connexion, V3Ecrit("DECLARE @i TABLE (Id int); INSERT INTO rec.Bateau (Abrege, Nom, Port, EstNavire) OUTPUT inserted.Id INTO @i " +
        "VALUES ('BANC14', 'LE NAVIRE DU BANC', 'LH', 1); SELECT Id FROM @i;")))!;
    var avecCreance = await Dupliquer(v3.Connexion, "rec.Inscription", T("INSCRIPTION").V3, origine!,
        $"IdInscriptionPrecedente=NULL;IdCreance={creance};IdBateau={bateau};NumeroInscription='B14N00002'", V3Ecrit(""));
    var p14b = await Passer();
    var ordre14 = Colonne0(await Lire(v3.Connexion, $"SELECT TableV3 FROM rec.FileRecopie WHERE (TableV3 = 'rec.Creance' AND IdV3 = '{creance}') OR (TableV3 = 'rec.Bateau' AND IdV3 = '{bateau}') " +
                                                    $"OR (TableV3 = 'rec.Inscription' AND IdV3 = '{avecCreance}') GROUP BY TableV3 ORDER BY MIN(AppliqueeLe), MIN(Id)"));
    var inscriptionV2 = await Lire(v2.Connexion, $"SELECT INEDT_NUM_INSCRIPTION, INEDT_ID_CNEND, INEDT_ID_BTEAU FROM dbo.INSCRIPTION WHERE INEDT_ID_INSCR = {await V2De("INSCRIPTION", avecCreance)}");
    Exiger(p14b.Erreur is null && inscriptionV2.Rows.Count == 1 && (string)inscriptionV2.Rows[0][0] == "B14N00002"
           && Convert.ToString(inscriptionV2.Rows[0][1]) == await V2De("CREANCE_END", creance) && Convert.ToString(inscriptionV2.Rows[0][2]) == await V2De("BATEAU", bateau)
           && ordre14.Last() == "rec.Inscription",
        $"REC-014 CA02 : l'inscription V3 avec sa creance et son bateau : creance et bateau ecrits dans la V2 avant elle (ordre : {string.Join(", ", ordre14)}), l'inscription garde " +
        $"son numero ({(inscriptionV2.Rows.Count > 0 ? inscriptionV2.Rows[0][0] : "absente")}) et vise leurs lignes V2");

    // CA03 : une mise a jour d'inscription recopiee n'est pas marquee « a transmettre » par la V2.
    var etatInsAvant = await Texte(v2.Connexion, $"SELECT CONCAT(INEDT_ETAT_TRANSMISSION, '|', CONVERT(varchar(30), INEDT_DATE_MODIF_FLAG_TRANS, 126)) FROM dbo.INSCRIPTION WHERE INEDT_ID_INSCR = {inscriptionF}");
    await Executer(v3.Connexion, V3Ecrit($"UPDATE rec.Inscription SET DateMaj = '2026-11-21T13:00:00', DatePeremption = '2036-11-21' WHERE Id = {inscriptionF};"));
    var p14c = await Passer();
    var etatInsApres = await Texte(v2.Connexion, $"SELECT CONCAT(INEDT_ETAT_TRANSMISSION, '|', CONVERT(varchar(30), INEDT_DATE_MODIF_FLAG_TRANS, 126)) FROM dbo.INSCRIPTION WHERE INEDT_ID_INSCR = {inscriptionF}");
    var peremptionV2 = await Texte(v2.Connexion, $"SELECT CONVERT(varchar(10), INEDT_DATE_PEREMPTION, 126) FROM dbo.INSCRIPTION WHERE INEDT_ID_INSCR = {inscriptionF}");
    Exiger(p14c.Erreur is null && peremptionV2 == "2036-11-21" && etatInsApres == etatInsAvant && etatInsAvant!.StartsWith('F'),
        $"REC-014 CA03 : la mise a jour d'inscription recopiee (peremption {peremptionV2}) ne la marque pas « a transmettre » : le declencheur de la V2 a joue, son etat est remis " +
        $"({etatInsAvant} -> {etatInsApres})");

    // CA04 : une radiation faite dans la V2 sur une inscription ancienne.
    var radiable = await Texte(v2.Connexion, "SELECT TOP (1) CONVERT(varchar(20), i.INEDT_ID_INSCR) FROM dbo.INSCRIPTION i WHERE NOT EXISTS (SELECT 1 FROM dbo.RADIATION_TOTALE r " +
                                             $"WHERE r.RDTTL_ID_INSCR = i.INEDT_ID_INSCR) AND i.INEDT_ID_INSCR <> {inscriptionF} ORDER BY i.INEDT_ID_INSCR");
    await Executer(v2.Connexion, $"INSERT INTO dbo.RADIATION_TOTALE (RDTTL_ID_INSCR, RDTTL_DATE_RADIATION, RDTTL_TEXTE_RADIATION, RDTTL_NUM_REGISTRE) " +
                                 $"VALUES ({radiable}, '2026-11-21', 'Radiation totale saisie dans l''ancien RIG (banc)', 'R2026-0001');");
    var p14d = await Passer();
    var radieeV3 = await Lire(v3.Connexion, $"SELECT i.NumeroInscription, r.RDTTL_DATE_RADIATION FROM rec.Inscription i JOIN v2.RADIATION_TOTALE r ON r.RDTTL_ID_INSCR = i.Id " +
                                            $"WHERE i.Id = {await V3De("INSCRIPTION", radiable!)} AND r.RDTTL_NUM_REGISTRE = 'R2026-0001'");
    Exiger(p14d.Erreur is null && radieeV3.Rows.Count == 1,
        $"REC-014 CA04 : la radiation faite dans la V2 sur l'inscription {radiable} remonte : la V3 la voit radiee (radiation totale du {(radieeV3.Rows.Count > 0 ? radieeV3.Rows[0][1] : "?")} " +
        "sur sa fiche) -- la V2 radie par une ligne RADIATION_TOTALE (Inscription.IsRadieeOuPerimee), recopiee telle quelle");

    // --- REC-015 ---------------------------------------------------------------------------------------
    // CA01 : une action de chaque sorte enregistree dans la V3 sur un dossier qui a un historique V2, chacune avec son objet.
    var dossierHisto = await Texte(v3.Connexion, "SELECT TOP (1) CONVERT(varchar(20), IdDossier) FROM rec.ActionRegistre WHERE IdDossier IS NOT NULL GROUP BY IdDossier ORDER BY COUNT(*) DESC");
    var modeleAction = await Texte(v3.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), Id) FROM rec.ActionRegistre WHERE IdDossier = {dossierHisto} ORDER BY Id");
    var sortes = new (string TableV2, string Colonne)[]
    {
        ("DEPOT_RCS", "IdDepot"), ("BODACC", "IdAnnonceBodacc"), ("INSCRIPTION_RCS", "IdInscriptionRegistre"),
        ("INSTANCE", "IdInstance"), ("PASSAGE_CFE", "IdPassageCfe"), ("NON_INSCRIT", "IdNonInscrit"),
    };
    var actionsCa01 = new List<(string Sorte, string Action, string Objet)>();
    foreach (var (table, colonne) in sortes)
    {
        var modeleObjet = await Texte(v3.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), [{T(table).Cle.Nom}]) FROM v2.[{table}] ORDER BY [{T(table).Cle.Nom}] DESC");
        if (modeleObjet is null) { actionsCa01.Add((table, "", "")); continue; }
        var objet = await Dupliquer(v3.Connexion, $"v2.[{table}]", T(table).V3, modeleObjet, null, V3Ecrit(""));
        var nulles = string.Join(";", sortes.Where(s => s.Colonne != colonne).Select(s => $"{s.Colonne}=NULL"));
        var action = await Dupliquer(v3.Connexion, "rec.ActionRegistre", T("ACTION_RCS").V3, modeleAction!,
            $"{nulles};{colonne}={objet};IdDossier={dossierHisto};NumeroArrivee='B15-{table[..3]}';DateArrivee='2026-11-21T14:00:00'", V3Ecrit(""));
        actionsCa01.Add((table, action, objet));
    }
    var p15a = await Passer();
    var vuesV2 = new List<string>();
    foreach (var (sorte, action, objet) in actionsCa01.Where(a => a.Action != ""))
    {
        var colonneV2 = T("ACTION_RCS").Colonnes.First(c => T("ACTION_RCS").Liens.TryGetValue(c.Nom, out var p) && p.NomV2 == sorte).Nom;
        var lu = await Texte(v2.Connexion, $"SELECT CONCAT(CONVERT(varchar(30), ACTRC_DATE_ARRIVEE, 126), '|', [{colonneV2}]) FROM dbo.ACTION_RCS WHERE ACTRC_ID_ACTRC = {await V2De("ACTION_RCS", action)} " +
                                           $"AND ACTRC_ID_DSRCS = {await V2De("DOSSIER_RCS", dossierHisto!)}");
        var objetAvant = await Texte(v3.Connexion, $"SELECT CASE WHEN (SELECT MIN(AppliqueeLe) FROM rec.FileRecopie WHERE TableV3 = 'v2.{sorte}' AND IdV3 = '{objet}') <= " +
                                                   $"(SELECT MIN(AppliqueeLe) FROM rec.FileRecopie WHERE TableV3 = 'rec.ActionRegistre' AND IdV3 = '{action}') THEN 'avant' ELSE 'APRES' END");
        if (lu == $"2026-11-21T14:00:00|{await V2De(sorte, objet)}" && objetAvant == "avant") vuesV2.Add(sorte);
    }
    Exiger(p15a.Erreur is null && vuesV2.Count == sortes.Length,
        $"REC-015 CA01 : les actions enregistrees dans la V3 sur le dossier {dossierHisto} (historique V2) sont dans l'historique V2 a leur date, chacune avec son objet ecrit avant elle : " +
        $"{string.Join(", ", vuesV2)} ({actionsCa01.Count(a => a.Action == "")} sorte(s) sans objet dans l'extrait de RIG_DEV)");

    // CA02 : une action enregistree dans la V2 pour une affaire ancienne.
    var dossierV2Histo = await V2De("DOSSIER_RCS", dossierHisto!);
    var modeleActionV2 = await Texte(v2.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), ACTRC_ID_ACTRC) FROM dbo.ACTION_RCS WHERE ACTRC_ID_DSRCS = {dossierV2Histo} ORDER BY ACTRC_ID_ACTRC");
    var actionV2 = await Dupliquer(v2.Connexion, "dbo.ACTION_RCS", T("ACTION_RCS").V2, modeleActionV2!, "ACTRC_NUM_ARRIVEE='B15-V2';ACTRC_DATE_ARRIVEE='2026-11-21T15:00:00'");
    var p15b = await Passer();
    var actionV3 = await V3De("ACTION_RCS", actionV2);
    Exiger(p15b.Erreur is null && actionV3 is not null && await Texte(v3.Connexion, $"SELECT CONVERT(varchar(20), IdDossier) FROM rec.ActionRegistre WHERE Id = {actionV3}") == dossierHisto
           && await Differentes("ACTION_RCS", actionV2, actionV3) == 0,
        $"REC-015 CA02 : l'action enregistree dans la V2 remonte dans l'historique du meme dossier de la V3 (action V3 {actionV3}, fiche {await Identiques("ACTION_RCS", actionV2, actionV3 ?? "0")})");

    // CA03 : une journee simulee, puis les historiques de 100 dossiers tires au hasard compares.
    var auHasard = Colonne0(await Lire(v3.Connexion, "SELECT TOP (100) CONVERT(varchar(20), d.Id) FROM rec.DossierRcs d WHERE EXISTS (SELECT 1 FROM rec.ActionRegistre a WHERE a.IdDossier = d.Id) ORDER BY NEWID()"));
    foreach (var (d, i) in auHasard.Take(10).Select((d, i) => (d, i)))
    {
        var m = await Texte(v3.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), Id) FROM rec.ActionRegistre WHERE IdDossier = {d} ORDER BY Id");
        await Dupliquer(v3.Connexion, "rec.ActionRegistre", T("ACTION_RCS").V3, m!, $"NumeroArrivee='J{i:00}-V3';DateArrivee='2026-11-21T16:{i:00}:00'", V3Ecrit(""));
        var dV2 = await V2De("DOSSIER_RCS", auHasard[^(i + 1)]);
        var mV2 = await Texte(v2.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), ACTRC_ID_ACTRC) FROM dbo.ACTION_RCS WHERE ACTRC_ID_DSRCS = {dV2} ORDER BY ACTRC_ID_ACTRC");
        await Dupliquer(v2.Connexion, "dbo.ACTION_RCS", T("ACTION_RCS").V2, mV2!, $"ACTRC_NUM_ARRIVEE='J{i:00}-V2';ACTRC_DATE_ARRIVEE='2026-11-21T17:{i:00}:00'");
    }
    var p15c = await Passer();
    var historiquesDifferents = new List<string>();
    foreach (var d in auHasard)
    {
        var dV2 = await V2De("DOSSIER_RCS", d);
        var h2 = string.Join(";", Colonne0(await Lire(v2.Connexion, $"SELECT CONCAT(CONVERT(varchar(30), ACTRC_DATE_ARRIVEE, 126), '|', ACTRC_CODE_PROSS, '|', ACTRC_NUM_ARRIVEE, '|', ACTRC_ETAT_ACTION) " +
                                                                    $"FROM dbo.ACTION_RCS WHERE ACTRC_ID_DSRCS = {dV2} ORDER BY ACTRC_DATE_ARRIVEE, ACTRC_NUM_ARRIVEE, ACTRC_CODE_PROSS, ACTRC_ETAT_ACTION")));
        var h3 = string.Join(";", Colonne0(await Lire(v3.Connexion, $"SELECT CONCAT(CONVERT(varchar(30), DateArrivee, 126), '|', CodeProcessus, '|', NumeroArrivee, '|', EtatAction) " +
                                                                    $"FROM rec.ActionRegistre WHERE IdDossier = {d} ORDER BY DateArrivee, NumeroArrivee, CodeProcessus, EtatAction")));
        if (h2 != h3) historiquesDifferents.Add(d);
    }
    Exiger(p15c.Erreur is null && auHasard.Count >= 50 && historiquesDifferents.Count == 0,
        $"REC-015 CA03 : apres une journee simulee (10 actions dans la V3, 10 dans la V2), les historiques de {auHasard.Count} dossiers tires au hasard ont les memes actions, dans le " +
        $"meme ordre, dans les deux logiciels {string.Join(", ", historiquesDifferents.Take(5))}");

    // CA04 : une demande V3 qui porte une action du registre. Hors de toute ancienne affaire : depuis le lot 11, la V3 refuse d'ajouter une
    // demande a une affaire de l'ancien RIG (REC-010 CA03, Serveur\Recopie\GardeAffaires.cs), et son modele est une demande transferee.
    var modeleDemande = await Texte(v3.Connexion, "SELECT TOP (1) CONVERT(varchar(20), DMND_ID_DMND) FROM v2.DEMANDE ORDER BY DMND_ID_DMND DESC");
    var demandeV3 = await Dupliquer(v3.Connexion, "v2.DEMANDE", catV3.Tables["v2.DEMANDE"], modeleDemande!, $"DMND_ID_ACTRC={actionsCa01.First(a => a.Action != "").Action};DMND_ID_DMND_MERE=NULL;DMND_ID_AFFRE=NULL;DMND_ID_DMND_INHIBITRICE=NULL");
    var suivie = await Texte(v3.Connexion, $"SELECT a.NumeroArrivee FROM v2.DEMANDE d JOIN rec.ActionRegistre a ON a.Id = d.DMND_ID_ACTRC WHERE d.DMND_ID_DMND = {demandeV3}");
    var demandesTransferees = await Nombre(v3.Connexion, "SELECT COUNT(*) FROM v2.DEMANDE WHERE DMND_ID_ACTRC IS NOT NULL");
    var demandesOrphelines = await Nombre(v3.Connexion, "SELECT COUNT(*) FROM v2.DEMANDE d WHERE d.DMND_ID_ACTRC IS NOT NULL AND NOT EXISTS (SELECT 1 FROM rec.ActionRegistre a WHERE a.Id = d.DMND_ID_ACTRC)");
    Exiger(suivie is not null && suivie.StartsWith("B15-") && demandesTransferees > 0 && demandesOrphelines == 0,
        $"REC-015 CA04 : la demande V3 qui porte une action du registre mene a l'action correspondante (« {suivie} ») ; les {demandesTransferees} demandes transferees avec une action " +
        $"la retrouvent toutes ({demandesOrphelines} sans)");

    // --- REC-016 ---------------------------------------------------------------------------------------
    // CA01 : l'adresse partagee par deux entreprises, modifiee dans la V3.
    await Executer(v3.Connexion, V3Ecrit($"UPDATE rec.Adresse SET NomVoie = 'RUE CORRIGEE DANS LA V3', NumeroVoie = '7' WHERE Id = {adressePartagee};"));
    var p16a = await Passer();
    var vues16 = Colonne0(await Lire(v2.Connexion, $"SELECT CONCAT(e.ENTRP_ID_RPRTR, ':', a.ADRBS_NUM_VOIE, ' ', a.ADRBS_NOM_VOIE) FROM dbo.ENTREPRISE e JOIN dbo.ADRESSE_BASE a ON a.ADRBS_ID_ADRBS = e.ENTRP_ID_ADRBS " +
                                                   $"WHERE e.ENTRP_ID_RPRTR IN ({entreprisesAdr[0]}, {entreprisesAdr[1]}) ORDER BY 1"));
    Exiger(p16a.Erreur is null && vues16.Count == 2 && vues16.All(v => v.EndsWith(":7 RUE CORRIGEE DANS LA V3")) && await V2De("ADRESSE_BASE", adressePartagee!) == adressePartagee,
        $"REC-016 CA01 : l'adresse corrigee dans la V3 change sa ligne de la V2, et les deux fiches V2 qui la referencent la voient : {string.Join(" ; ", vues16)}");

    // CA04 : dix adresses de formes variees saisies dans la V3 : meme decoupage dans la V2 que celui que la V2 produit.
    var sources = new List<string>();
    foreach (var f in formes)
        if (await Texte(v3.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), x.Id) FROM rec.Adresse x JOIN rec.Correspondance c ON c.IdV3 = CONVERT(nvarchar(200), x.Id) AND c.TableV2 = 'ADRESSE_BASE' " +
                                      $"JOIN [{v2.Nom}].dbo.ADRESSE_BASE a ON CONVERT(nvarchar(200), a.ADRBS_ID_ADRBS) = c.IdV2 WHERE {f.Replace("ADRBS_", "a.ADRBS_")} " +
                                      $"AND x.EstInconnue = 0 {(sources.Count == 0 ? "" : $"AND x.Id NOT IN ({string.Join(", ", sources)})")} ORDER BY x.Id DESC") is { } s) sources.Add(s);
    var saisies = new List<(string Source, string Saisie)>();
    foreach (var s in sources) saisies.Add((s, await Dupliquer(v3.Connexion, "rec.Adresse", T("ADRESSE_BASE").V3, s, null, V3Ecrit(""))));
    var p16b = await Passer();
    var decoupage = "ADRBS_NUM_VOIE, ADRBS_COMPLEMENT_NO, ADRBS_TYPE_VOIE, ADRBS_NOM_VOIE, ADRBS_CODE_POSTAL, ADRBS_LOCALITE_POSTALE, ADRBS_PAYS, ADRBS_PRE_ADRESSE, " +
                    "ADRBS_SUITE_ADRESSE, ADRBS_BOITE_POSTALE, ADRBS_CEDEX, ADRBS_LIEU_RATTACHE, ADRBS_TYPE_SPECIAL, ADRBS_VALEUR_SPECIAL, ADRBS_CODE_INSEE";
    var memeDecoupage = 0;
    foreach (var (source, saisie) in saisies)
        if (await V2De("ADRESSE_BASE", saisie) is { } nouvelleV2 && await Nombre(v2.Connexion,
                $"SELECT COUNT(*) FROM (SELECT {decoupage} FROM dbo.ADRESSE_BASE WHERE ADRBS_ID_ADRBS = {nouvelleV2} EXCEPT SELECT {decoupage} FROM dbo.ADRESSE_BASE " +
                $"WHERE ADRBS_ID_ADRBS = {await V2De("ADRESSE_BASE", source)}) d") == 0 && nouvelleV2 != await V2De("ADRESSE_BASE", source))
            memeDecoupage++;
    Exiger(p16b.Erreur is null && saisies.Count == 10 && memeDecoupage == saisies.Count,
        $"REC-016 CA04 : {saisies.Count} adresses de formes variees (boite postale, cedex, complement de numero, pays etranger, code INSEE, adresse speciale, pre- et suite " +
        $"d'adresse, numero non numerique, sans type de voie) saisies dans la V3 sont ecrites dans la V2 avec le decoupage de la V2 (numero, type et nom de voie, code postal, " +
        $"ville, pays...) : {memeDecoupage} identiques a l'adresse que la V2 avait produite");

    // --- REC-017 ---------------------------------------------------------------------------------------
    var service = (await Lire(v3.Connexion, "SELECT TOP (1) Id, IdV2 FROM soc.Service WHERE IdV2 IS NOT NULL AND IdV2 <> Id ORDER BY Id")).Rows.Cast<DataRow>().FirstOrDefault()
                  ?? (await Lire(v3.Connexion, "SELECT TOP (1) Id, IdV2 FROM soc.Service WHERE IdV2 IS NOT NULL ORDER BY Id")).Rows[0];
    // CA01 : un compte cree dans la V3 pour un nouvel agent.
    var compteCa01 = (await Texte(v3.Connexion, V3Ecrit("DECLARE @i TABLE (Id int); INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive) " +
        $"OUTPUT inserted.Id INTO @i VALUES ('banc_rec017_nouveau', 'NOUVEL', 'Agent', 'NAG', 'M.', '0000', {service[0]}, 0); SELECT Id FROM @i;")))!;
    var p17a = await Passer();
    var compteV2 = await Lire(v2.Connexion, "SELECT UTLST_ID_UTLST, UTLST_ID_SRVC, UTLST_DESACTIVE, UTLST_NOM, UTLST_IMPRIMANTE_DEFAUT, UTLST_SCANNER_DEFAUT, UTLST_P100_DISPONIBILITE " +
                                            "FROM dbo.UTILISATEUR WHERE UTLST_LOGIN = 'banc_rec017_nouveau'");
    Exiger(p17a.Erreur is null && compteV2.Rows.Count == 1 && Convert.ToString(compteV2.Rows[0][1]) == Convert.ToString(service[1]) && !(bool)compteV2.Rows[0][2]
           && Convert.ToString(compteV2.Rows[0][0]) == await V2De("UTILISATEUR", compteCa01),
        $"REC-017 CA01 : le compte cree dans la V3 est dans la V2 au meme nom de session, actif, rattache au meme service (V3 n° {service[0]} -> V2 n° {(compteV2.Rows.Count > 0 ? compteV2.Rows[0][1] : "?")})");
    Reste("REC-017 CA01 « l'agent ouvre la V2 sans autre geste » : la V2 exige aussi au moins un role (HABILIT_UTILISATEUR_ROLE_USER, RigConsoleAccueil.cs:113-133) ; " +
          "les roles d'un compte se recopient avec les droits, que T02 laisse aux stories du Socle commun (SOC-002) -- compte, actif et service sont prouves ici");

    // CA03 : les reglages du poste dans la V2 ne sont jamais ecrases ; vides pour un nouveau compte.
    var posteV3 = await V3De("UTILISATEUR", posteRegle!);
    await Executer(v3.Connexion, V3Ecrit($"UPDATE soc.Compte SET Nom = 'RENOMME DANS LA V3' WHERE Id = {posteV3};"));
    var p17b = await Passer();
    var poste = await Lire(v2.Connexion, $"SELECT UTLST_NOM, UTLST_IMPRIMANTE_DEFAUT, UTLST_SCANNER_DEFAUT FROM dbo.UTILISATEUR WHERE UTLST_ID_UTLST = {posteRegle}");
    Exiger(p17b.Erreur is null && (string)poste.Rows[0][0] == "RENOMME DANS LA V3" && poste.Rows[0][1] as string == "IMPRIMANTE-DU-POSTE" && poste.Rows[0][2] as string == "SCANNER-DU-POSTE"
           && compteV2.Rows[0][4] is DBNull && compteV2.Rows[0][5] is DBNull && Convert.ToInt32(compteV2.Rows[0][6]) == 100,
        $"REC-017 CA03 : le compte recopie de la V3 (nom « {poste.Rows[0][0]} ») garde son imprimante et son scanner du poste dans la V2 ({poste.Rows[0][1]}, {poste.Rows[0][2]}) ; " +
        "pour le nouveau compte ils restent vides (la disponibilite prend la valeur par defaut de la V2, 100)");

    // CA04 : un nom de session deja pris dans la V2 par un autre compte.
    var modeleCompteV2 = await Texte(v2.Connexion, "SELECT TOP (1) CONVERT(varchar(20), UTLST_ID_UTLST) FROM dbo.UTILISATEUR ORDER BY UTLST_ID_UTLST DESC");
    var compteV2Pris = await Dupliquer(v2.Connexion, "dbo.UTILISATEUR", T("UTILISATEUR").V2, modeleCompteV2!, "UTLST_LOGIN='banc_rec017_pris';UTLST_NOM='PRIS DANS LA V2'");
    var compteV3Pris = (await Texte(v3.Connexion, V3Ecrit("DECLARE @i TABLE (Id int); INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive) " +
        $"OUTPUT inserted.Id INTO @i VALUES ('banc_rec017_pris', 'NOUVEAU', 'Dans la V3', 'NDV', 'M.', '0000', {service[0]}, 0); SELECT Id FROM @i;")))!;
    var p17c = await Passer();
    var miseDeCote = (await Lire(v3.Connexion, $"SELECT Etat, Erreur, Essais FROM rec.FileRecopie WHERE TableV3 = 'soc.Compte' AND IdV3 = '{compteV3Pris}' ORDER BY Id")).Rows.Cast<DataRow>().FirstOrDefault();
    var ecart17 = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.Ecart WHERE TableV2 = 'UTILISATEUR' AND IdV3 = '{compteV3Pris}' AND IdV2 = '{compteV2Pris}'");
    var loginsV2 = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.UTILISATEUR WHERE UTLST_LOGIN = 'banc_rec017_pris'");
    Exiger(p17c.Erreur is null && miseDeCote is not null && (string)miseDeCote[0] == "mise de cote" && ((string)miseDeCote[1]).Contains("nom de session deja pris")
           && Convert.ToInt32(miseDeCote[2]) == 1 && ecart17 >= 1 && loginsV2 == 1,
        $"REC-017 CA04 : un nom de session deja pris dans la V2 par un autre compte : la recopie du compte V3 est mise de cote d'emblee ({miseDeCote?[2]} essai) avec le motif " +
        $"« {miseDeCote?[1]} », l'ecart est ouvert ({ecart17}) et la V2 garde un seul compte a ce nom ({loginsV2})");

    // --- REC-019 ---------------------------------------------------------------------------------------
    // CA03 : un type cree seulement dans la V3.
    var typesV2Avant = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.CATEGORIE");
    var modeleType = await Texte(v3.Connexion, "SELECT TOP (1) CONVERT(varchar(20), Id) FROM rec.TypeDemande WHERE Niveau = 4 ORDER BY Id");
    var typeV3Seul = await Dupliquer(v3.Connexion, "rec.TypeDemande", T("CATEGORIE").V3, modeleType!, "Reference='V3SEUL';LibelleEcran='Type de la V3 seule'", V3Ecrit(""));
    var p19a = await Passer();
    await using var cn19 = new SqlConnection(v3.Connexion);
    await cn19.OpenAsync();
    var sansEquivalent = await TypesDemande.SansEquivalentAsync(cn19, v2.Nom);
    var numeroV3Seul = await TypesDemande.NumeroV2Async(cn19, v2.Nom, int.Parse(typeV3Seul));
    var fileType = await Texte(v3.Connexion, $"SELECT MAX(CONCAT(Etat, ' : ', Erreur)) FROM rec.FileRecopie WHERE TableV3 = 'rec.TypeDemande' AND IdV3 = '{typeV3Seul}'");
    Exiger(p19a.Erreur is null && await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.CATEGORIE") == typesV2Avant && numeroV3Seul is null
           && sansEquivalent.Any(s => s.IdV3 == int.Parse(typeV3Seul) && s.Reference == "V3SEUL") && await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.CATEGORIE WHERE CTGR_REFERENCE = 'V3SEUL'") == 0,
        $"REC-019 CA03 : un type cree seulement dans la V3 n'est rangé sous aucun type de la V2 : il n'y est pas cree ({fileType}), il n'a pas de numero V2, et il est liste sans equivalent " +
        $"({sansEquivalent.Count} type(s) : {string.Join(", ", sansEquivalent.Select(s => s.Reference))}) pour le retour d'un greffe dans la V2 (REC-007)");

    // CA04 : un type commun modifie dans la V3 (libelle, service) : seuls les champs partages changent dans la V2.
    var typeCommun = await Texte(v2.Connexion, "SELECT TOP (1) CONVERT(varchar(20), CTGR_ID_CTGR) FROM dbo.CATEGORIE WHERE CTGR_ID_MDPRC IS NOT NULL ORDER BY CTGR_ID_CTGR");
    var deroulementAvant = await Texte(v2.Connexion, $"SELECT CTGR_ID_MDPRC FROM dbo.CATEGORIE WHERE CTGR_ID_CTGR = {typeCommun}");
    var typeCommunV3 = await V3De("CATEGORIE", typeCommun!);
    await Executer(v3.Connexion, V3Ecrit($"UPDATE rec.TypeDemande SET LibelleEcran = 'Libelle change V3', IdService = {service[0]} WHERE Id = {typeCommunV3};"));
    var p19b = await Passer();
    var typeV2 = await Lire(v2.Connexion, $"SELECT CTGR_LIBELLE_ECRAN, CTGR_ID_SRVC, CTGR_ID_MDPRC FROM dbo.CATEGORIE WHERE CTGR_ID_CTGR = {typeCommun}");
    Exiger(p19b.Erreur is null && (string)typeV2.Rows[0][0] == "Libelle change V3" && Convert.ToString(typeV2.Rows[0][1]) == Convert.ToString(service[1])
           && Convert.ToString(typeV2.Rows[0][2]) == deroulementAvant && await Differentes("CATEGORIE", typeCommun!, typeCommunV3!) == 0,
        $"REC-019 CA04 : le type {typeCommun} modifie dans la V3 change dans la V2 sur les champs partages (libelle « {typeV2.Rows[0][0]} », service V2 n° {typeV2.Rows[0][1]}) ; " +
        $"son deroulement reste celui de la V2 ({deroulementAvant} -> {typeV2.Rows[0][2]})");

    // ====================================================================================================
    // D. Une seconde base, tiree de RIG_RECETTE : le meme numero de type designe un autre type (REC-019 CA02, CA05)
    // ====================================================================================================
    var regleDemande = reglesServeur.Regles.Single(r => r.Table == "DEMANDE");
    var repriseTypes = reglesServeur.Reprises.Single(r => r.Story == "REC-019");
    await using var v2b = await BaseJetable.CreerAsync("RecopieFichesRecette");
    var idsRecette = string.Join(", ", Colonne0(await Lire(chaineRecette, "SELECT TOP (15) DMND_ID_DMND FROM dbo.DEMANDE WHERE DMND_ID_CTGR = 1417 ORDER BY DMND_ID_DMND DESC"))
                                        .Concat(Colonne0(await Lire(chaineRecette, "SELECT TOP (15) DMND_ID_DMND FROM dbo.DEMANDE WHERE DMND_ID_CTGR = 2136 ORDER BY DMND_ID_DMND DESC"))).Append("-1"));
    await ExtraitRigDev.ConstruireAsync(chaineRecette, v2b.Connexion, ["CATEGORIE"], new Dictionary<string, string>
    {
        ["DEMANDE"] = $"SELECT * FROM dbo.DEMANDE WHERE DMND_ID_DMND IN ({idsRecette})",
    }, Info);
    // CA05, contre-epreuve : une entree indiscernable d'une autre (meme reference, meme niveau, memes parents), comme une base peut en avoir.
    var jumeau = await Texte(v2b.Connexion, "SELECT TOP (1) CONVERT(varchar(20), CTGR_ID_CTGR) FROM dbo.CATEGORIE WHERE CTGR_REFERENCE = 'AMITEL' AND CTGR_NIVEAU = 2");
    var catB = await CatalogueAsync(v2b.Connexion);
    var jumelle = await Dupliquer(v2b.Connexion, "dbo.CATEGORIE", catB.Tables["CATEGORIE"], jumeau!, "CTGR_LIBELLE_ECRAN='Jumelle indiscernable'");
    await using var v3b = await BaseJetable.CreerAsync("RecopieFichesRecetteV3");
    await Migrateur.AppliquerAsync(v3b.Connexion, migrations);
    var transfertB = await MoteurTransfert.TransfererAsync(new OptionsTransfert(v2b.Connexion, v3b.Connexion)
        { Regles = [regleDemande, reglesServeur.Regles.Single(r => r.Table == "CATEGORIE")], Reprises = [repriseTypes], TailleLot = 5000 });
    string TypeDesDemandes(BaseJetable b2, BaseJetable b3) => $"""
        SELECT CONCAT(d.DMND_ID_CTGR, '=', t.Reference) FROM v2.DEMANDE d
        JOIN rec.Correspondance k ON k.BaseV2 = '{b2.Nom}' AND k.TableV2 = 'CATEGORIE' AND k.IdV2 = CONVERT(nvarchar(200), d.DMND_ID_CTGR)
        JOIN rec.TypeDemande t ON CONVERT(nvarchar(200), t.Id) = k.IdV3 WHERE d.DMND_ID_CTGR IN (1417, 2136) GROUP BY d.DMND_ID_CTGR, t.Reference ORDER BY 1
        """;
    var typesDev = Colonne0(await Lire(v3.Connexion, TypeDesDemandes(v2, v3)));
    var typesRecette = Colonne0(await Lire(v3b.Connexion, TypeDesDemandes(v2b, v3b)));
    var refsV2Dev = Colonne0(await Lire(v2.Connexion, "SELECT CONCAT(CTGR_ID_CTGR, '=', CTGR_REFERENCE) FROM dbo.CATEGORIE WHERE CTGR_ID_CTGR IN (1417, 2136) ORDER BY 1"));
    var refsV2Recette = Colonne0(await Lire(v2b.Connexion, "SELECT CONCAT(CTGR_ID_CTGR, '=', CTGR_REFERENCE) FROM dbo.CATEGORIE WHERE CTGR_ID_CTGR IN (1417, 2136) ORDER BY 1"));
    // RIG_DEV n'a de demandes que du 1417 (43), RIG_RECETTE que du 2136 (589) (lu le 2026-11-21) : chacune garde le type de la
    // reference de SA base, qui n'est pas celui que le meme numero designe dans l'autre.
    Exiger(transfertB.Etat == "fini" && typesDev.Count >= 1 && typesRecette.Count >= 1 && typesDev.All(refsV2Dev.Contains) && typesRecette.All(refsV2Recette.Contains)
           && typesDev.All(t => !refsV2Recette.Contains(t)) && typesRecette.All(t => !refsV2Dev.Contains(t)) && !refsV2Dev.Intersect(refsV2Recette).Any(),
        $"REC-019 CA02 : deux bases ou le meme numero designe deux types differents (RIG_DEV {string.Join(", ", refsV2Dev)} ; RIG_RECETTE {string.Join(", ", refsV2Recette)}) : " +
        $"chaque demande transferee garde le type de SA reference -- demandes de RIG_DEV {string.Join(", ", typesDev)}, de RIG_RECETTE {string.Join(", ", typesRecette)}, jamais " +
        $"celui du meme numero dans l'autre base (transfert de la seconde base : {transfertB.Etat} {transfertB.Motif})");
    var listees = Colonne0(await Lire(v3b.Connexion, "SELECT CONCAT(Reference, ' niveau ', Niveau, ' : ', LibelleEcran) FROM rec.TypesDemandeIndiscernables ORDER BY Id"));
    var typesB = await Nombre(v3b.Connexion, "SELECT COUNT(*) FROM rec.TypeDemande");
    Exiger(listees.Count == 2 && listees.All(l => l.StartsWith("AMITEL niveau 2")) && typesB == await Nombre(v2b.Connexion, "SELECT COUNT(*) FROM dbo.CATEGORIE"),
        $"REC-019 CA05 : deux entrees encore indiscernables (meme reference, meme niveau, memes parents) sont transferees toutes les deux et listees pour le referent, jamais " +
        $"fusionnees ni choisies au hasard : {string.Join(" ; ", listees)} ({typesB} types transferes pour {typesB} dans la V2)");

    // ====================================================================================================
    // E. La vraie boucle du serveur : REC-017 CA02, la desactivation d'un compte en moins d'une minute
    // ====================================================================================================
    await using var commune = await BaseJetable.CreerAsync("RecopieFichesCommune");
    await Migrateur.AppliquerAsync(commune.Connexion, migrations);
    var instance = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs()).DataSource;
    var authentification = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
    authentification.Remove("Data Source");
    await Executer(commune.Connexion, "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, BaseV3CreeeLe, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe) " +
                                      $"VALUES ('9910', N'Greffe du banc des fiches', '{instance}', '{v2.Nom}', '{v3.Nom}', SYSDATETIME(), 0, 0, 'Romance Standard Time', 'banc', SYSDATETIME());");
    serveur = new ServeurBanc(ServeurRig.Construire(["--environment", "Development"], s =>
    {
        s.WebHost.UseTestServer();
        s.Logging.SetMinimumLevel(LogLevel.Warning);
        s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
        s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
        s.Configuration[RecopieContinue.Reglage] = "true";
        s.Configuration["Recopie:IntervalleSecondes"] = "5";
    }));
    await serveur.App.StartAsync();
    var t0 = Stopwatch.StartNew();
    await Executer(v3.Connexion, V3Ecrit($"UPDATE soc.Compte SET Desactive = 1 WHERE Id = {compteCa01};"));
    double delai = double.PositiveInfinity;
    while (t0.Elapsed < TimeSpan.FromSeconds(90))
    {
        if (await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.UTILISATEUR WHERE UTLST_LOGIN = 'banc_rec017_nouveau' AND UTLST_DESACTIVE = 1") == 1) { delai = t0.Elapsed.TotalSeconds; break; }
        await Task.Delay(200);
    }
    await serveur.App.StopAsync();
    Exiger(delai < 60,
        $"REC-017 CA02 : le compte desactive dans la V3 l'est dans la V2 en {delai:0.0} s (moins d'une minute), par la boucle du serveur (« Recopie:Continue », un passage toutes les 5 s)");
}
finally
{
    if (serveur is not null) await serveur.DisposeAsync();
}

return Bilan();

// Un transfert interrompu garde son etat fige pour etre repris (REC-005) : la base jetable ne se supprime pas tant qu'il existe.
static async Task SupprimerInstantanes(string baseV2)
{
    await using var cn = new SqlConnection(new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs()) { InitialCatalog = "master" }.ConnectionString);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand("DECLARE @n sysname, @sql nvarchar(400); DECLARE c CURSOR LOCAL FOR SELECT name FROM sys.databases WHERE source_database_id = DB_ID(@b); " +
                                         "OPEN c; FETCH NEXT FROM c INTO @n; WHILE @@FETCH_STATUS = 0 BEGIN SET @sql = N'DROP DATABASE ' + QUOTENAME(@n); EXEC (@sql); " +
                                         "FETCH NEXT FROM c INTO @n; END", cn);
    cmd.Parameters.AddWithValue("@b", baseV2);
    await cmd.ExecuteNonQueryAsync();
}

static async Task<CatalogueV2> CatalogueAsync(string chaine)
{
    await using var cn = new SqlConnection(chaine);
    await cn.OpenAsync();
    return await CatalogueV2.LireAsync(cn, default);
}

int Bilan()
{
    Console.WriteLine();
    if (echecs.Count == 0)
    {
        Console.WriteLine("VERT : les fiches partagees ont leur table V3, leur correspondance colonne par colonne, et restent les memes dans les deux logiciels (REC-012 a REC-017, REC-019).");
        return 0;
    }
    Console.WriteLine($"ROUGE : {echecs.Count} controle(s) en echec.");
    foreach (var e in echecs) Console.WriteLine("  - " + e);
    return 1;
}

/// <summary>Le serveur de la partie E, arrete AVANT d'etre detruit (decision du lot 1 : sinon « BackgroundService failed »).</summary>
sealed class ServeurBanc(Microsoft.AspNetCore.Builder.WebApplication app) : IAsyncDisposable
{
    public Microsoft.AspNetCore.Builder.WebApplication App { get; } = app;
    public async ValueTask DisposeAsync()
    {
        try { await App.StopAsync(); } catch (ObjectDisposedException) { }
        await App.DisposeAsync();
    }
}
