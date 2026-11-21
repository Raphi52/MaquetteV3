// fix-ok: lot 20, section 4 du banc : la 1re compilation a dit BilanPassage ambigu (RigV3.Serveur.Recopie et Socle.Supervision, alias pose) et SignatureDeFond\n// absent (RigV3.Serveur.Socle.Chaines, using ajoute) ; ensuite la section a ete verte du premier coup, puis rouge avec l'etape des droits neutralisee.\n// fix-ok: banc NEUF du lot 19, retouche avant son premier lancement : la compilation a dit qu'il ne pouvait pas nommer
// DroitsRecopies (internal, rendu public), qu'un tuple sans nom ne se lisait pas par ses champs (rNouveau), et un CS8604 sur
// fSansCategorie ; la comparaison des noms de declencheurs passe en ensemble (l'ordre culturel de « _ » et des lettres varie).
using System.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RecopieTransfertCheck;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Serveur;
using RigV3.Serveur.Recopie;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Chaines;
using RigV3.Serveur.Socle.Supervision;
using BilanPassage = RigV3.Serveur.Recopie.BilanPassage;
using SocleDroitsCheck;

// Banc de SOC-002 T05 / CA07 (carnet\socle.json) et de la fin de REC-017 CA01 (carnet\recopie.json) : les droits d'un compte
// changes dans la V3 sont recopies dans la V2, chacun dans SON origine (Serveur\Recopie\DroitsRecopies.cs), par la recopie
// continue du compte (fiche « compte d'agent », REC-017). Lot 19 du plan de reconstruction, phase 2 (2026-11-21).
//
//  0. Une base V2 jetable tiree de RIG_DEV (lecture seule) : les comptes et leurs trois origines de droits, entieres. Le suivi des
//     changements (Base\v2\recopie-v2.sql), le transfert avec les deux reprises qui portent ces tables, la mise en service.
//  1. Chaque cas, change dans la V3 par le VRAI geste quand il existe (ServiceComptes : creer, role, supprimer) ou par une
//     ecriture de soc.CompteFonction (aucun ecran ne donne encore un droit direct), puis un passage de la recopie
//     (ServiceRecopie.PasserAsync). La preuve : l'ORACLE de la V2 (tests\SocleDroitsCheck\OracleDroitsV2.cs, qui rejoue
//     Utilisateur.ALaFonctionnalite sur les tables de la V2, independamment du serveur) repond comme la V3 pour CHAQUE fonction
//     du catalogue ; et, pour un compte cree, le controle d'ouverture de la V2 (RigConsoleAccueil.CheckUser) passe.
//  2. La contre-epreuve : sans le declencheur des roles, la V2 ne suit plus et l'oracle le voit.
// Il lui faut « docker compose up -d » et la lecture de RIG_DEV (autre : RIGV3_RIG_DEV).
// Doc : docs\reconstruction\socle-droits.md, section « Recopie V3 -> V2 des droits (lot 19) ».

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var migrations = Path.Combine(racine, "Base", "migrations");
var scriptV2 = Path.Combine(racine, "Base", "v2", "recopie-v2.sql");
var chaineRigDev = Environment.GetEnvironmentVariable("RIGV3_RIG_DEV") is { Length: > 0 } c0 ? c0 :
    @"Server=SQL-DEV\DEV;Database=RIG_DEV;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;ApplicationIntent=ReadOnly;Connect Timeout=20";
const string AgentV3 = "Agent V3 du banc des droits";
const string Ecran = "Comptes des agents";

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
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
async Task<string?> Texte(string chaine, string sql)
{
    var t = await Lire(chaine, sql);
    return t.Rows.Count == 0 || t.Rows[0][0] is DBNull ? null : Convert.ToString(t.Rows[0][0], System.Globalization.CultureInfo.InvariantCulture);
}
async Task<long> Nombre(string chaine, string sql) => long.Parse(await Texte(chaine, sql) ?? "0", System.Globalization.CultureInfo.InvariantCulture);
async Task Executer(string chaine, string sql)
{
    await using var cn = new SqlConnection(chaine);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
    await cmd.ExecuteNonQueryAsync();
}
List<string> Colonne0(DataTable t) => [.. t.Rows.Cast<DataRow>().Select(r => Convert.ToString(r[0], System.Globalization.CultureInfo.InvariantCulture)!)];

// ====================================================================================================
// 0. RIG_DEV, la base V2 jetable, le suivi, le transfert, la mise en service
// ====================================================================================================
try
{
    LectureV2.ExigerLecture("SELECT @@SERVERNAME");
    Info($"RIG_DEV : {await Texte(chaineRigDev, "SELECT @@SERVERNAME + ' / ' + DB_NAME()")}");
}
catch (SqlException e)
{
    Exiger(false, $"RIG_DEV lisible : {e.Message} -- il faut l'acces en lecture a SQL-DEV\\DEV (ou RIGV3_RIG_DEV)");
    return Bilan();
}

// Les tables des droits (SOC-002) et des types de demande (CATEGORIE, que l'ancien systeme lit) : les deux reprises du serveur
// qui les portent, et elles seules. UTILISATEUR_ROLE (les roles de l'ancien systeme) n'a pas encore de regle de transfert :
// le banc lui en donne une telle quelle -- la recopie ecrit dans la table de la V2, pas dans sa copie.
var serveur = ReglesTransfert.Decouvrir();
var tablesDroits = new[] { "UTILISATEUR", "SERVICE", "HABILIT_ROLE_USER", "PROCESSUS_ET_FONCTIONALITE", "HABILIT_ROLE_USER_PROCESSUS",
                           "HABILIT_UTILISATEUR_ROLE_USER", "UTILISATEUR_ROLE", "UTILISATEUR_A_POUR_ROLE", "COMPOSER_HABILITATION",
                           "EXPRESSO_MENU", "EXPRESSO_DROITS", "CATEGORIE" };
var reprises = serveur.Reprises.Where(r => r.Regles.Any() && r.Regles.All(t => tablesDroits.Contains(t.Table, StringComparer.OrdinalIgnoreCase))).ToList();
var regles = serveur.Regles.Where(r => tablesDroits.Contains(r.Table, StringComparer.OrdinalIgnoreCase))
    .Append(RegleTable.TelleQuelle("UTILISATEUR_ROLE", "SOC-002", "UTROL_LIBELLE", "UTROL_CODE_DIRECTION", "UTROL_ID_UTROL")).ToList();
Exiger(reprises.Select(r => r.Story).Order(StringComparer.Ordinal).SequenceEqual(["REC-019", "SOC-002"]) && regles.Count == tablesDroits.Length,
    $"le banc transfere les tables des droits par les reprises du serveur ({string.Join(", ", reprises.Select(r => $"{r.Story} « {r.Nom} »"))}) : {regles.Count} regles");

await using var v2 = await BaseJetable.CreerAsync("RecopieDroitsV2");
var extrait = await ExtraitRigDev.ConstruireAsync(chaineRigDev, v2.Connexion, tablesDroits, new Dictionary<string, string>(), Info);
Info($"base V2 jetable {v2.Nom} : {string.Join(", ", extrait.Lignes.Select(l => $"{l.Key} {l.Value}"))} ; {extrait.Cles} cles verifiees");

// Les comptes du banc, choisis dans la V2 : actifs, chacun pour un cas, tous differents.
var pris = new List<string>();
async Task<string> Compte(string condition, string quoi)
{
    var id = await Texte(v2.Connexion, $"SELECT TOP (1) CONVERT(varchar(20), u.UTLST_ID_UTLST) FROM dbo.UTILISATEUR AS u WHERE u.UTLST_DESACTIVE = 0 " +
                                       $"AND u.UTLST_ID_SRVC IS NOT NULL AND u.UTLST_ID_UTLST NOT IN ({string.Join(", ", pris.Append("-1"))}) AND {condition} ORDER BY u.UTLST_ID_UTLST DESC")
             ?? throw new InvalidOperationException($"RIG_DEV n'a pas de compte actif pour {quoi} ({condition})");
    pris.Add(id);
    return id;
}
// Deux roles ordinaires (ni Amitel, ni administrateurs) qui donnent le plus de fonctions.
var rolesOrdinaires = Colonne0(await Lire(v2.Connexion, "SELECT TOP (3) r.HROLE_NOM_ROLE FROM dbo.HABILIT_ROLE_USER AS r WHERE r.HROLE_ONLYAMITEL = 0 AND r.HROLE_ONLYADMIN = 0 " +
                                                         "ORDER BY (SELECT COUNT(*) FROM dbo.HABILIT_ROLE_USER_PROCESSUS AS p WHERE p.HRFON_NOM_ROLE = r.HROLE_NOM_ROLE) DESC, r.HROLE_NOM_ROLE"));
string R1 = rolesOrdinaires[0], R2 = rolesOrdinaires[1], R3 = rolesOrdinaires[2];
string SansRole(string role) => $"NOT EXISTS (SELECT 1 FROM dbo.HABILIT_UTILISATEUR_ROLE_USER AS h WHERE h.HUROL_ID_UTLST = u.UTLST_ID_UTLST AND h.HUROL_NOM_ROLE = '{role}')";
const string SansAncien = "NOT EXISTS (SELECT 1 FROM dbo.UTILISATEUR_A_POUR_ROLE AS a WHERE a.X_UTARL_ID_UTLST = u.UTLST_ID_UTLST)";
// Comptabilite : une fonction (pas FONCVB6) qu'aucun role ne donne et qui a un menu a son nom ; une autre qui n'a que son ANCIEN nom.
const string PasVb6 = "p.PROCF_TYPE NOT IN ('FONCVB6', 'OLD', 'VB6') AND NOT EXISTS (SELECT 1 FROM dbo.HABILIT_ROLE_USER_PROCESSUS AS h WHERE h.HRFON_CODE_PROC = p.PROCF_CODE_PROC)";
var fCompta = (await Texte(v2.Connexion, $"SELECT TOP (1) p.PROCF_CODE_PROC FROM dbo.PROCESSUS_ET_FONCTIONALITE AS p WHERE {PasVb6} AND EXISTS (SELECT 1 FROM dbo.EXPRESSO_MENU AS m " +
                                         "WHERE m.EXPME_CLE = p.PROCF_CODE_PROC COLLATE Latin1_General_BIN2) ORDER BY p.PROCF_CODE_PROC"))!;
var anciensNoms = RepriseDroits.AnciensNomsDeMenu;
var fAncienNom = (await Texte(v2.Connexion, $"SELECT TOP (1) p.PROCF_CODE_PROC FROM dbo.PROCESSUS_ET_FONCTIONALITE AS p WHERE {PasVb6} AND p.PROCF_CODE_PROC IN " +
                                            $"({string.Join(", ", anciensNoms.Keys.Select(k => $"'{k}'"))}) AND NOT EXISTS (SELECT 1 FROM dbo.EXPRESSO_MENU AS m WHERE m.EXPME_CLE = p.PROCF_CODE_PROC) " +
                                            "ORDER BY p.PROCF_CODE_PROC"))!;
string SansMenu(string cle) => $"NOT EXISTS (SELECT 1 FROM dbo.EXPRESSO_DROITS AS d JOIN dbo.EXPRESSO_MENU AS m ON m.EXPME_ID_EXPME = d.EXPDR_ID_EXPME " +
                               $"WHERE d.EXPDR_ID_UTLST = u.UTLST_ID_UTLST AND m.EXPME_CLE = '{cle}')";
// Ancien systeme : une fonction FONCVB6 sans categorie (l'ancien systeme ne peut pas la donner) ; le domaine FONCTIONEL et deux de
// ses fonctions de niveau 6 (RIG_DEV, lu le 2026-11-21 : ARTDEL et ENCUPDDEL sous FONCTIONEL, n° 1294).
var fSansCategorie = (await Texte(v2.Connexion, "SELECT TOP (1) p.PROCF_CODE_PROC FROM dbo.PROCESSUS_ET_FONCTIONALITE AS p WHERE p.PROCF_TYPE IN ('FONCVB6', 'OLD', 'VB6') " +
                                                "AND NOT EXISTS (SELECT 1 FROM dbo.CATEGORIE AS c WHERE c.CTGR_REFERENCE = p.PROCF_CODE_PROC) AND NOT EXISTS " +
                                                "(SELECT 1 FROM dbo.HABILIT_ROLE_USER_PROCESSUS AS h WHERE h.HRFON_CODE_PROC = p.PROCF_CODE_PROC) ORDER BY p.PROCF_CODE_PROC"))!;
var hierarchie = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.CATEGORIE AS c JOIN dbo.CATEGORIE AS d ON d.CTGR_ID_CTGR = c.CTGR_ID_CTGR_DOMAINE " +
                                            "WHERE d.CTGR_REFERENCE = 'FONCTIONEL' AND c.CTGR_REFERENCE IN ('ARTDEL', 'ENCUPDDEL') AND c.CTGR_NIVEAU = 6");
Exiger(hierarchie == 2 && fSansCategorie.Length > 0, $"les fonctions de l'ancien systeme du banc sont dans RIG_DEV : ARTDEL et ENCUPDDEL (niveau 6) sous FONCTIONEL ({hierarchie}/2), {fSansCategorie} sans categorie");

var compteRoles = await Compte($"{SansRole(R2)} AND {SansRole(R3)} AND EXISTS (SELECT 1 FROM dbo.HABILIT_UTILISATEUR_ROLE_USER AS h WHERE h.HUROL_ID_UTLST = u.UTLST_ID_UTLST)", "un role ajoute puis retire");
var compteCompta = await Compte($"{SansMenu(fCompta)} AND {SansMenu(anciensNoms[fAncienNom])}", "un droit de comptabilite");
var compteAncien = await Compte("EXISTS (SELECT 1 FROM dbo.UTILISATEUR_A_POUR_ROLE AS a JOIN dbo.COMPOSER_HABILITATION AS h ON h.X_CPSHB_ID_UTROL = a.X_UTARL_ID_UTROL " +
                                "WHERE a.X_UTARL_ID_UTLST = u.UTLST_ID_UTLST AND h.X_CPSHB_REFERENCE_CTGR = 'FONCTIONEL' AND h.X_CPSHB_DEGRE = 3) AND NOT EXISTS (SELECT 1 FROM " +
                                "dbo.UTILISATEUR_A_POUR_ROLE AS a JOIN dbo.COMPOSER_HABILITATION AS h ON h.X_CPSHB_ID_UTROL = a.X_UTARL_ID_UTROL WHERE a.X_UTARL_ID_UTLST = u.UTLST_ID_UTLST " +
                                "AND h.X_CPSHB_REFERENCE_CTGR = 'ENCUPDDEL') AND NOT EXISTS (SELECT 1 FROM dbo.HABILIT_UTILISATEUR_ROLE_USER AS h JOIN " +
                                "dbo.HABILIT_ROLE_USER_PROCESSUS AS p ON p.HRFON_NOM_ROLE = h.HUROL_NOM_ROLE WHERE h.HUROL_ID_UTLST = u.UTLST_ID_UTLST " +
                                "AND p.HRFON_CODE_PROC = 'ENCUPDDEL')", "un droit de l'ancien systeme retire");
// Ni role ancien, ni role qui donne ARTDEL ou ENCUPDDEL (les roles « FACT : » de RIG_DEV les donnent : la V2 n'irait pas jusqu'a
// l'ancien systeme).
var compteDomaine = await Compte($"{SansAncien} AND NOT EXISTS (SELECT 1 FROM dbo.HABILIT_UTILISATEUR_ROLE_USER AS h JOIN dbo.HABILIT_ROLE_USER_PROCESSUS AS p " +
                                 "ON p.HRFON_NOM_ROLE = h.HUROL_NOM_ROLE WHERE h.HUROL_ID_UTLST = u.UTLST_ID_UTLST AND p.HRFON_CODE_PROC IN ('ARTDEL', 'ENCUPDDEL'))",
                                 "un domaine a 0 dans l'ancien systeme");
var compteImpossible = await Compte("1 = 1", "un droit que l'ancien systeme ne sait pas donner");
var compteRefus = await Compte($"{SansRole(R3)}", "un refus de la V2");
Info($"comptes du banc (V2) : roles {compteRoles}, comptabilite {compteCompta}, ancien {compteAncien}, domaine {compteDomaine}, impossible {compteImpossible}, " +
     $"refus {compteRefus} ; roles {R1}, {R2}, {R3} ; comptabilite {fCompta} et {fAncienNom} (menu « {anciensNoms[fAncienNom]} ») ; sans categorie {fSansCategorie}");

// Fixture de l'ancien systeme, AVANT le transfert : un role ancien qui met le domaine FONCTIONEL a 0 (il prime : ses fonctions
// sont refusees) et ARTDEL a 3, attache au compte « domaine ».
await Executer(v2.Connexion, $"""
    DECLARE @r TABLE (Id int);
    INSERT INTO dbo.UTILISATEUR_ROLE (UTROL_LIBELLE, UTROL_CODE_DIRECTION) OUTPUT inserted.UTROL_ID_UTROL INTO @r VALUES ('BANC DOMAINE 0', NULL);
    INSERT INTO dbo.COMPOSER_HABILITATION (X_CPSHB_DEGRE, X_CPSHB_REFERENCE_CTGR, X_CPSHB_ID_UTROL) SELECT 0, 'FONCTIONEL', Id FROM @r UNION ALL SELECT 3, 'ARTDEL', Id FROM @r;
    INSERT INTO dbo.UTILISATEUR_A_POUR_ROLE (X_UTARL_ID_UTROL, X_UTARL_ID_UTLST) SELECT Id, {compteDomaine} FROM @r;
    """);

await NumerotationV2Check.Sqlcmd.JouerAsync(v2.Connexion, scriptV2, new Dictionary<string, string> { ["BaseV2"] = v2.Nom, ["RetentionJours"] = "7", ["Tables"] = "UTILISATEUR" });
await using var v3 = await BaseJetable.CreerAsync("RecopieDroitsV3");
await Migrateur.AppliquerAsync(v3.Connexion, migrations);
var transfert = await MoteurTransfert.TransfererAsync(new OptionsTransfert(v2.Connexion, v3.Connexion) { Regles = regles, Reprises = reprises, TailleLot = 5000 });
Exiger(transfert.Etat == "fini", $"REC-001 : les comptes et leurs droits transferes ({transfert.Etat}) {transfert.Motif}");
if (transfert.Etat != "fini") { await SupprimerInstantanes(v2.Nom); return Bilan(); }

var regle = ReglesRecopie.Decouvrir().Single(r => r.TableV2 == "UTILISATEUR");
var mes = await PontRecopie.MettreEnServiceAsync(v3.Connexion, v2.Nom, await FichesRecopiees.ChargerAsync(v3.Connexion, v2.Nom, [regle], default));
var fiches = await FichesRecopiees.ChargerAsync(v3.Connexion, v2.Nom, null, default);
Exiger(mes.Faite && regle.Jointe is DroitsRecopies
       && mes.Declencheurs.ToHashSet(StringComparer.Ordinal).SetEquals(["soc.TR_Compte_FileRecopie", "soc.TR_CompteFonction_FileRecopie", "soc.TR_CompteRole_FileRecopie"]),
    $"SOC-002 T05 : la fiche « compte d'agent » (REC-017) porte ses droits ; la mise en service pose le declencheur du compte et ceux de ses roles et de ses " +
    $"droits directs ({string.Join(", ", mes.Declencheurs)}) {string.Join(" ; ", mes.Refus)}");
if (!mes.Faite) return Bilan();

string V3Ecrit(string sql) => $"EXEC sys.sp_set_session_context @key = N'rigv3.agent', @value = N'{AgentV3}'; {sql}";
async Task<BilanPassage> Passer() => await ServiceRecopie.PasserAsync(v3.Connexion, v2.Nom, fiches);
async Task<int> V3De(string idV2) => int.Parse((await Texte(v3.Connexion,
    $"SELECT IdV3 FROM rec.Correspondance WHERE BaseV2 = '{v2.Nom}' AND TableV2 = 'UTILISATEUR' AND IdV2 = '{idV2}' AND TableV3 = 'soc.Compte'"))!);
async Task<string?> V2De(int idV3) => await Texte(v3.Connexion,
    $"SELECT IdV2 FROM rec.Correspondance WHERE BaseV2 = '{v2.Nom}' AND TableV2 = 'UTILISATEUR' AND IdV3 = '{idV3}' AND TableV3 = 'soc.Compte'");

// L'oracle de la V2 contre la V3, pour CHAQUE fonction du catalogue de la V2 : les fonctions ou ils different.
async Task<(int Fonctions, List<string> Ecarts, int AccordeesV2)> Comparer(string idV2)
{
    var oracle = await OracleDroitsV2.LireAsync(v2.Connexion);
    var id = int.Parse(idV2);
    await using var cn = new SqlConnection(v3.Connexion);
    await cn.OpenAsync();
    var v3Droits = await ServiceDroits.FonctionsAutoriseesAsync(cn, await V3De(idV2), default);
    var ecarts = oracle.Fonctions.Where(f => oracle.ALaFonctionnalite(id, f) != v3Droits.Contains(f))
                                 .Select(f => $"{f} (V2 {(oracle.ALaFonctionnalite(id, f) ? "oui" : "non")}, V3 {(v3Droits.Contains(f) ? "oui" : "non")})").ToList();
    return (oracle.Fonctions.Count, ecarts, oracle.Fonctions.Count(f => oracle.ALaFonctionnalite(id, f)));
}
string Dit((int Fonctions, List<string> Ecarts, int AccordeesV2) r) =>
    $"{r.Fonctions} fonctions comparees, {r.AccordeesV2} accordees par la V2, {r.Ecarts.Count} ecart(s){(r.Ecarts.Count > 0 ? " : " + string.Join(", ", r.Ecarts.Take(6)) : "")}";

// Le controle d'ouverture de la V2 (RIG\EXE\Accueil\RigClientAccueil\RigConsoleAccueil.cs:117-131, CheckUser), dans son ordre :
// compte connu (GetUtilisateurByName : majuscules, Utilisateur.cs:421-423), au moins un role, actif, un service. Le 2e controle
// teste RoleUsers == null, jamais vrai (Utilisateur.cs:250-253) : la V3 l'applique comme la V2 le prevoit (socle-identite.md, ecart 1),
// et le banc aussi -- un compte sans role n'a de toute facon aucune fonction dans la V2.
async Task<string> OuvertureV2(string login)
{
    var u = await Lire(v2.Connexion, $"SELECT UTLST_ID_UTLST, UTLST_DESACTIVE, UTLST_ID_SRVC FROM dbo.UTILISATEUR WHERE UPPER(UTLST_LOGIN) = UPPER('{login}')");
    if (u.Rows.Count == 0) return $"refusee : « L'utilisateur '{login}' n'a pas été défini dans RIG. »";
    var id = Convert.ToInt32(u.Rows[0][0]);
    if (await Nombre(v2.Connexion, $"SELECT COUNT(*) FROM dbo.HABILIT_UTILISATEUR_ROLE_USER AS h JOIN dbo.HABILIT_ROLE_USER AS r ON r.HROLE_NOM_ROLE = h.HUROL_NOM_ROLE " +
                                   $"WHERE h.HUROL_ID_UTLST = {id}") == 0)
        return $"refusee : « Aucune habilitation n'a été définie pour l'utilisateur '{login}'. »";
    if ((bool)u.Rows[0][1]) return $"refusee : « L'utilisateur '{login}' a été désactivé. »";
    if (u.Rows[0][2] is DBNull || await Nombre(v2.Connexion, $"SELECT COUNT(*) FROM dbo.SERVICE WHERE SRVC_ID_SRVC = {u.Rows[0][2]}") == 0)
        return $"refusee : « Aucun service n'est rattaché à l'utilisateur '{login}'. »";
    return "ouverte";
}

// Le responsable qui fait les gestes : un compte de la V3 qui gere les droits (sinon le premier compte actif).
var idResp = int.Parse((await Texte(v3.Connexion, "SELECT TOP (1) c.Id FROM soc.Compte AS c WHERE c.Desactive = 0 AND c.EstAmitel = 0 ORDER BY " +
                                                  $"CASE WHEN EXISTS (SELECT 1 FROM soc.CompteRole AS r WHERE r.IdCompte = c.Id AND r.Role = '{RoutesDroits.RoleGestionDesDroits}') THEN 0 ELSE 1 END, c.Id"))!);
var resp = new Appelant(idResp, (await Texte(v3.Connexion, $"SELECT CompteWindows FROM soc.Compte WHERE Id = {idResp}"))!, "Responsable du banc", false, false, true);
var comptes = new ServiceComptes();
async Task<ResultatCompte> Geste(Func<SqlConnection, Task<ResultatCompte>> geste)
{
    await using var cn = new SqlConnection(v3.Connexion);
    await cn.OpenAsync();
    await using (var cmd = new SqlCommand($"EXEC sys.sp_set_session_context @key = N'rigv3.agent', @value = N'{resp.CompteWindows}';", cn)) await cmd.ExecuteNonQueryAsync();
    return await geste(cn);
}
var idService = int.Parse((await Texte(v3.Connexion, "SELECT TOP (1) Id FROM soc.Service WHERE IdV2 IS NOT NULL ORDER BY Id"))!);

// Avant tout changement : la reprise est exacte pour les comptes du banc (le point de depart de chaque cas).
foreach (var id in pris)
{
    var r = await Comparer(id);
    if (r.Ecarts.Count > 0) Exiger(false, $"point de depart : le compte V2 {id} repris repond comme la V2 ({Dit(r)})");
}

// ====================================================================================================
// 1. LES CAS
// ====================================================================================================

// --- REC-017 CA01 (fin) / SOC-004 CA06 : un compte cree dans la V3 avec un role ouvre la V2 sans autre geste ---------------------
var cree = await Geste(cn => comptes.CreerAsync(cn, resp, "3801", new DemandeCompte(null, "banc.droits.nouveau", "NOUVEAU", "Agent", "M.", idService, [R1], Ecran), default));
var avantPassage = await OuvertureV2("banc.droits.nouveau");
var p1 = await Passer();
var idNouveauV2 = cree.Compte is { } cc ? await V2De(cc.Id) : null;
var ouverture = await OuvertureV2("banc.droits.nouveau");
var rNouveau = idNouveauV2 is null ? (Fonctions: 0, Ecarts: new List<string> { "pas dans la V2" }, AccordeesV2: 0) : await Comparer(idNouveauV2);
var roleV2 = idNouveauV2 is null ? null : await Texte(v2.Connexion, $"SELECT CONCAT(HUROL_NOM_ROLE, ' / ', HUROL_LOGIN_UTLST) FROM dbo.HABILIT_UTILISATEUR_ROLE_USER WHERE HUROL_ID_UTLST = {idNouveauV2}");
Exiger(cree.Fait && p1.Erreur is null && ouverture == "ouverte" && roleV2 == $"{R1} / banc.droits.nouveau" && rNouveau.Ecarts.Count == 0 && rNouveau.AccordeesV2 > 0,
    $"REC-017 CA01 / SOC-002 CA07 : un compte cree dans la V3 avec le role {R1} (geste « creer » de la V3) arrive dans la V2 avec ce role ({roleV2}) : le controle d'ouverture " +
    $"de la V2 passe ({ouverture} ; avant le passage : {avantPassage}) et l'oracle de la V2 repond comme la V3 -- {Dit(rNouveau)}");
// Jumeau : la meme creation SANS role arrive dans la V2, qui la refuse a l'ouverture comme elle le prevoit (au moins un role).
var sansRole = await Geste(cn => comptes.CreerAsync(cn, resp, "3801", new DemandeCompte(null, "banc.droits.sansrole", "SANSROLE", "Agent", "Mme", idService, null, Ecran), default));
await Passer();
var ouvertureSansRole = await OuvertureV2("banc.droits.sansrole");
Exiger(sansRole.Fait && ouvertureSansRole.StartsWith("refusee : « Aucune habilitation"),
    $"REC-017 CA01 (jumeau) : le meme compte cree SANS role arrive dans la V2, qui ne l'ouvre pas ({ouvertureSansRole})");

// --- SOC-002 CA07 : un role ajoute puis retire dans la V3 -------------------------------------------------------------------
var idRolesV3 = await V3De(compteRoles);
var ajout = await Geste(cn => comptes.RoleAsync(cn, resp, new DemandeRole(idRolesV3, R2, true, Ecran), default));
var p2 = await Passer();
var rAjout = await Comparer(compteRoles);
var aR2 = await Nombre(v2.Connexion, $"SELECT COUNT(*) FROM dbo.HABILIT_UTILISATEUR_ROLE_USER WHERE HUROL_ID_UTLST = {compteRoles} AND HUROL_NOM_ROLE = '{R2}'");
Exiger(ajout.Fait && p2.Erreur is null && aR2 == 1 && rAjout.Ecarts.Count == 0,
    $"SOC-002 CA07 : le role {R2} ajoute au compte {compteRoles} dans la V3 est dans la V2 (HABILIT_UTILISATEUR_ROLE_USER : {aR2}) ; l'oracle de la V2 repond comme la V3 -- {Dit(rAjout)}");
var retrait = await Geste(cn => comptes.RoleAsync(cn, resp, new DemandeRole(idRolesV3, R2, false, Ecran), default));
var p3 = await Passer();
var rRetrait = await Comparer(compteRoles);
var resteR2 = await Nombre(v2.Connexion, $"SELECT COUNT(*) FROM dbo.HABILIT_UTILISATEUR_ROLE_USER WHERE HUROL_ID_UTLST = {compteRoles} AND HUROL_NOM_ROLE = '{R2}'");
Exiger(retrait.Fait && p3.Erreur is null && resteR2 == 0 && rRetrait.Ecarts.Count == 0 && rRetrait.AccordeesV2 < rAjout.AccordeesV2,
    $"SOC-002 CA07 : le role {R2} retire dans la V3 est retire dans la V2 ; la V2 refuse de nouveau ce qu'il donnait ({rAjout.AccordeesV2} -> {rRetrait.AccordeesV2} fonctions) -- {Dit(rRetrait)}");

// --- SOC-002 CA07 : un droit direct « comptabilite », au nom de la fonction puis sous un ancien nom de menu --------------------
var idComptaV3 = await V3De(compteCompta);
await Executer(v3.Connexion, V3Ecrit($"INSERT INTO soc.CompteFonction (IdCompte, Fonction, Origine, Detail) VALUES ({idComptaV3}, '{fCompta}', 'comptabilite', 'banc'), " +
                                     $"({idComptaV3}, '{fAncienNom}', 'comptabilite', 'banc');"));
var p4 = await Passer();
var menusDonnes = Colonne0(await Lire(v2.Connexion, $"SELECT m.EXPME_CLE FROM dbo.EXPRESSO_DROITS AS d JOIN dbo.EXPRESSO_MENU AS m ON m.EXPME_ID_EXPME = d.EXPDR_ID_EXPME " +
                                                    $"WHERE d.EXPDR_ID_UTLST = {compteCompta} AND m.EXPME_CLE IN ('{fCompta}', '{anciensNoms[fAncienNom]}') ORDER BY m.EXPME_CLE"));
var rCompta = await Comparer(compteCompta);
Exiger(p4.Erreur is null && menusDonnes.Order().SequenceEqual(new[] { fCompta, anciensNoms[fAncienNom] }.Order()) && rCompta.Ecarts.Count == 0,
    $"SOC-002 CA07 : deux droits directs « comptabilite » donnes dans la V3 ({fCompta} ; {fAncienNom}, dont la V2 n'a que l'ancien menu « {anciensNoms[fAncienNom]} ») sont ses " +
    $"menus de comptabilite dans la V2 (EXPRESSO_DROITS : {string.Join(", ", menusDonnes)}) ; l'oracle de la V2 repond comme la V3 -- {Dit(rCompta)}");
await Executer(v3.Connexion, V3Ecrit($"DELETE FROM soc.CompteFonction WHERE IdCompte = {idComptaV3} AND Fonction IN ('{fCompta}', '{fAncienNom}');"));
var p5 = await Passer();
var menusRestes = await Nombre(v2.Connexion, $"SELECT COUNT(*) FROM dbo.EXPRESSO_DROITS AS d JOIN dbo.EXPRESSO_MENU AS m ON m.EXPME_ID_EXPME = d.EXPDR_ID_EXPME " +
                                             $"WHERE d.EXPDR_ID_UTLST = {compteCompta} AND m.EXPME_CLE IN ('{fCompta}', '{anciensNoms[fAncienNom]}')");
var rComptaRetrait = await Comparer(compteCompta);
Exiger(p5.Erreur is null && menusRestes == 0 && rComptaRetrait.Ecarts.Count == 0,
    $"SOC-002 CA07 : les deux droits « comptabilite » retires dans la V3 sont retires des menus du compte dans la V2 ({menusRestes} restant) -- {Dit(rComptaRetrait)}");

// --- SOC-002 CA07 : un droit direct « ancien », retire puis rendu, sur un compte que des roles anciens PARTAGES servent -------
var idAncienV3 = await V3De(compteAncien);
var partages = Colonne0(await Lire(v2.Connexion, $"SELECT CONVERT(varchar(20), X_UTARL_ID_UTROL) FROM dbo.UTILISATEUR_A_POUR_ROLE WHERE X_UTARL_ID_UTLST = {compteAncien}"));
var voisins = Colonne0(await Lire(v2.Connexion, $"SELECT DISTINCT CONVERT(varchar(20), a.X_UTARL_ID_UTLST) FROM dbo.UTILISATEUR_A_POUR_ROLE AS a JOIN dbo.UTILISATEUR AS u ON " +
                                                $"u.UTLST_ID_UTLST = a.X_UTARL_ID_UTLST WHERE u.UTLST_DESACTIVE = 0 AND a.X_UTARL_ID_UTROL IN ({string.Join(", ", partages.Append("-1"))}) " +
                                                $"AND a.X_UTARL_ID_UTLST <> {compteAncien}"));
var avaitEncupddel = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM soc.CompteFonction WHERE IdCompte = {idAncienV3} AND Fonction = 'ENCUPDDEL' AND Origine = 'ancien'");
await Executer(v3.Connexion, V3Ecrit($"DELETE FROM soc.CompteFonction WHERE IdCompte = {idAncienV3} AND Fonction = 'ENCUPDDEL';"));
var p6 = await Passer();
var rAncienRetrait = await Comparer(compteAncien);
var rolesAnciensApres = Colonne0(await Lire(v2.Connexion, $"SELECT r.UTROL_LIBELLE FROM dbo.UTILISATEUR_A_POUR_ROLE AS a JOIN dbo.UTILISATEUR_ROLE AS r ON r.UTROL_ID_UTROL = a.X_UTARL_ID_UTROL " +
                                                          $"WHERE a.X_UTARL_ID_UTLST = {compteAncien}"));
var degreEncupddel = await Texte(v2.Connexion, $"SELECT CONVERT(varchar(5), h.X_CPSHB_DEGRE) FROM dbo.COMPOSER_HABILITATION AS h JOIN dbo.UTILISATEUR_ROLE AS r ON r.UTROL_ID_UTROL = h.X_CPSHB_ID_UTROL " +
                                               $"WHERE r.UTROL_LIBELLE = '{DroitsRecopies.RoleAncienDuCompte(int.Parse(compteAncien))}' AND h.X_CPSHB_REFERENCE_CTGR = 'ENCUPDDEL'");
Exiger(avaitEncupddel == 1 && p6.Erreur is null && rAncienRetrait.Ecarts.Count == 0 && rolesAnciensApres.SequenceEqual([DroitsRecopies.RoleAncienDuCompte(int.Parse(compteAncien))])
       && degreEncupddel == "0",
    $"SOC-002 CA07 : le droit « ancien » ENCUPDDEL, que le compte {compteAncien} tenait du domaine FONCTIONEL a 3 de ses roles anciens partages ({partages.Count}), retire dans la V3 : " +
    $"le compte ne garde que son role a lui ({string.Join(", ", rolesAnciensApres)}), ENCUPDDEL a {degreEncupddel} ; l'oracle de la V2 repond comme la V3 -- {Dit(rAncienRetrait)}");
var voisinsFaux = new List<string>();
foreach (var v in voisins.Take(15))
    if ((await Comparer(v)).Ecarts.Count > 0) voisinsFaux.Add(v);
Exiger(voisins.Count > 0 && voisinsFaux.Count == 0,
    $"SOC-002 CA07 (jumeau) : les {Math.Min(voisins.Count, 15)} premiers autres comptes actifs de ces roles anciens gardent exactement leurs droits (oracle = V3) {string.Join(", ", voisinsFaux)}");
await Executer(v3.Connexion, V3Ecrit($"INSERT INTO soc.CompteFonction (IdCompte, Fonction, Origine, Detail) VALUES ({idAncienV3}, 'ENCUPDDEL', 'ancien', 'banc');"));
var p7 = await Passer();
var rAncienRendu = await Comparer(compteAncien);
var oracleRendu = await OracleDroitsV2.LireAsync(v2.Connexion);
Exiger(p7.Erreur is null && rAncienRendu.Ecarts.Count == 0 && oracleRendu.ALaFonctionnalite(int.Parse(compteAncien), "ENCUPDDEL"),
    $"SOC-002 CA07 : le meme droit « ancien » rendu dans la V3 est rendu par l'ancien systeme de la V2 -- {Dit(rAncienRendu)}");

// Le domaine a 0 prime dans la V2 : accorder ENCUPDDEL demande de retirer ce 0 au compte, et ARTDEL (a 3 dans le meme role,
// refuse par le domaine) ne doit pas passer accorde pour autant.
var idDomaineV3 = await V3De(compteDomaine);
var oracleAvant = await OracleDroitsV2.LireAsync(v2.Connexion);
var refusesAvant = !oracleAvant.ALaFonctionnalite(int.Parse(compteDomaine), "ENCUPDDEL") && !oracleAvant.ALaFonctionnalite(int.Parse(compteDomaine), "ARTDEL");
await Executer(v3.Connexion, V3Ecrit($"INSERT INTO soc.CompteFonction (IdCompte, Fonction, Origine, Detail) VALUES ({idDomaineV3}, 'ENCUPDDEL', 'ancien', 'banc');"));
var p8 = await Passer();
var oracleDomaine = await OracleDroitsV2.LireAsync(v2.Connexion);
var rDomaine = await Comparer(compteDomaine);
var degresDomaine = string.Join(", ", Colonne0(await Lire(v2.Connexion, "SELECT CONCAT(r.UTROL_LIBELLE, ' ', h.X_CPSHB_REFERENCE_CTGR, '=', h.X_CPSHB_DEGRE) FROM dbo.UTILISATEUR_A_POUR_ROLE AS a " +
    "JOIN dbo.UTILISATEUR_ROLE AS r ON r.UTROL_ID_UTROL = a.X_UTARL_ID_UTROL JOIN dbo.COMPOSER_HABILITATION AS h ON h.X_CPSHB_ID_UTROL = r.UTROL_ID_UTROL " +
    $"WHERE a.X_UTARL_ID_UTLST = {compteDomaine} ORDER BY 1")));
Exiger(refusesAvant && p8.Erreur is null && rDomaine.Ecarts.Count == 0 && oracleDomaine.ALaFonctionnalite(int.Parse(compteDomaine), "ENCUPDDEL")
       && !oracleDomaine.ALaFonctionnalite(int.Parse(compteDomaine), "ARTDEL"),
    $"SOC-002 CA07 : un droit « ancien » donne dans la V3 sous un domaine que la V2 met a 0 pour ce compte (FONCTIONEL, qui prime) : ENCUPDDEL accorde, ARTDEL toujours " +
    $"refuse (avant : refuses {refusesAvant} ; apres : ENCUPDDEL {oracleDomaine.ALaFonctionnalite(int.Parse(compteDomaine), "ENCUPDDEL")}, ARTDEL " +
    $"{oracleDomaine.ALaFonctionnalite(int.Parse(compteDomaine), "ARTDEL")} ; ancien systeme du compte : {degresDomaine} ; passage {p8.Erreur ?? "sans erreur"}) -- {Dit(rDomaine)}");

// --- REC-005 sur les droits : ce que la V2 ne peut pas recevoir, et ce qu'elle refuse ----------------------------------------
var idImpossibleV3 = await V3De(compteImpossible);
await Executer(v3.Connexion, V3Ecrit($"INSERT INTO soc.CompteFonction (IdCompte, Fonction, Origine, Detail) VALUES ({idImpossibleV3}, '{fSansCategorie}', 'ancien', 'banc');"));
var p9 = await Passer();
var miseDeCote = await Lire(v3.Connexion, $"SELECT Id, Essais, Erreur FROM rec.FileRecopie WHERE TableV3 = 'soc.Compte' AND IdV3 = '{idImpossibleV3}' AND Etat = 'mise de cote'");
var ecartImpossible = await Texte(v3.Connexion, $"SELECT TOP (1) Fiche FROM rec.Ecart WHERE TableV3 = 'soc.Compte' AND IdV3 = '{idImpossibleV3}' ORDER BY Id DESC");
var rImpossible = await Comparer(compteImpossible);
Exiger(p9.MisesDeCote == 1 && miseDeCote.Rows.Count == 1 && Convert.ToInt32(miseDeCote.Rows[0][1]) == 1 && (miseDeCote.Rows[0][2] as string ?? "").Contains(fSansCategorie)
       && ecartImpossible is not null && rImpossible.Ecarts.Count == 1,
    $"REC-005 / SOC-002 T05 : un droit que l'ancien systeme de la V2 ne sait pas donner ({fSansCategorie}, sans categorie) est mis de cote d'emblee ({(miseDeCote.Rows.Count > 0 ? miseDeCote.Rows[0][1] : "?")} essai) " +
    $"avec son motif (« {(miseDeCote.Rows.Count > 0 ? miseDeCote.Rows[0][2] : "")} ») et un ecart pour l'agent designe ; rien n'est ecrit a peu pres dans la V2 (la seule difference est ce droit : {Dit(rImpossible)})");
await Executer(v3.Connexion, V3Ecrit($"DELETE FROM soc.CompteFonction WHERE IdCompte = {idImpossibleV3} AND Fonction = '{fSansCategorie}';"));
await Passer();
var attendDerriere = await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.FileRecopie WHERE TableV3 = 'soc.Compte' AND IdV3 = '{idImpossibleV3}' AND Etat = 'en attente'");
var relance = miseDeCote.Rows.Count == 1 && await ServiceRecopie.RelancerAsync(v3.Connexion, Convert.ToInt64(miseDeCote.Rows[0][0]), "banc des droits");
var p10 = await Passer();
var rImpossibleFin = await Comparer(compteImpossible);
Exiger(attendDerriere == 1 && relance && p10.Erreur is null && await Nombre(v3.Connexion, $"SELECT COUNT(*) FROM rec.FileRecopie WHERE TableV3 = 'soc.Compte' AND IdV3 = '{idImpossibleV3}' AND Etat <> 'appliquee'") == 0
       && rImpossibleFin.Ecarts.Count == 0,
    $"REC-005 CA03 : le droit retire dans la V3, son changement attend derriere la mise de cote ({attendDerriere}) ; relance, les deux passent dans l'ordre -- {Dit(rImpossibleFin)}");

// Un refus de la V2 (une contrainte posee par le banc) : un essai par passage, mis de cote au 3e, puis relance une fois la cause levee.
var idRefusV3 = await V3De(compteRefus);
await Executer(v2.Connexion, $"ALTER TABLE dbo.HABILIT_UTILISATEUR_ROLE_USER WITH NOCHECK ADD CONSTRAINT CK_BANC_SOC002 CHECK (HUROL_ID_UTLST <> {compteRefus});");
var donne = await Geste(cn => comptes.RoleAsync(cn, resp, new DemandeRole(idRefusV3, R3, true, Ecran), default));
var essais = new List<int>();
for (var i = 0; i < 3; i++)
{
    await Passer();
    essais.Add(int.Parse(await Texte(v3.Connexion, $"SELECT CONVERT(varchar(5), MAX(Essais)) FROM rec.FileRecopie WHERE TableV3 = 'soc.Compte' AND IdV3 = '{idRefusV3}' AND Etat <> 'appliquee'") ?? "0"));
}
var refusMisDeCote = await Lire(v3.Connexion, $"SELECT Id, Erreur FROM rec.FileRecopie WHERE TableV3 = 'soc.Compte' AND IdV3 = '{idRefusV3}' AND Etat = 'mise de cote'");
await Executer(v2.Connexion, "ALTER TABLE dbo.HABILIT_UTILISATEUR_ROLE_USER DROP CONSTRAINT CK_BANC_SOC002;");
var relanceRefus = refusMisDeCote.Rows.Count == 1 && await ServiceRecopie.RelancerAsync(v3.Connexion, Convert.ToInt64(refusMisDeCote.Rows[0][0]), "banc des droits");
var p11 = await Passer();
var rRefus = await Comparer(compteRefus);
Exiger(donne.Fait && essais.SequenceEqual([1, 2, 3]) && refusMisDeCote.Rows.Count == 1 && (refusMisDeCote.Rows[0][1] as string ?? "").Contains("CK_BANC_SOC002")
       && relanceRefus && p11.Erreur is null && rRefus.Ecarts.Count == 0,
    $"REC-005 CA02-CA03 / SOC-002 T05 : un role que la V2 refuse (contrainte CK_BANC_SOC002) compte un essai par passage ({string.Join(", ", essais)}), mis de cote au 3e ; " +
    $"la cause levee et la ligne relancee, il passe -- {Dit(rRefus)}");

// --- REC-004 sur les droits : changes dans la V2 depuis le dernier passage, puis dans la V3 : la V3 gagne, l'ecart est ecrit -----
await Executer(v2.Connexion, $"INSERT INTO dbo.HABILIT_UTILISATEUR_ROLE_USER (HUROL_ID_UTLST, HUROL_NOM_ROLE) VALUES ({compteRoles}, '{R3}');");
await Executer(v3.Connexion, V3Ecrit($"IF EXISTS (SELECT 1 FROM soc.CompteFonction WHERE IdCompte = {idRolesV3} AND Fonction = '{fCompta}') " +
                                     $"DELETE FROM soc.CompteFonction WHERE IdCompte = {idRolesV3} AND Fonction = '{fCompta}' " +
                                     $"ELSE INSERT INTO soc.CompteFonction (IdCompte, Fonction, Origine, Detail) VALUES ({idRolesV3}, '{fCompta}', 'comptabilite', 'banc');"));
var p12 = await Passer();
var ecartDroits = await Lire(v3.Connexion, $"SELECT e.Fiche, c.Champ, c.ValeurV2, c.ValeurV3 FROM rec.Ecart AS e JOIN rec.EcartChamp AS c ON c.IdEcart = e.Id " +
                                           $"WHERE e.TableV3 = 'soc.Compte' AND e.IdV3 = '{idRolesV3}'");
var r3Reste = await Nombre(v2.Connexion, $"SELECT COUNT(*) FROM dbo.HABILIT_UTILISATEUR_ROLE_USER WHERE HUROL_ID_UTLST = {compteRoles} AND HUROL_NOM_ROLE = '{R3}'");
var rConflit = await Comparer(compteRoles);
Exiger(p12.Erreur is null && p12.Ecarts == 1 && r3Reste == 0 && ecartDroits.Rows.Count == 1 && (string)ecartDroits.Rows[0][1] == DroitsRecopies.ChampRoles
       && ((string)ecartDroits.Rows[0][2]).Contains(R3) && !((string)ecartDroits.Rows[0][3]).Contains(R3) && rConflit.Ecarts.Count == 0,
    $"REC-004 / SOC-002 T05 : un role donne dans la V2 depuis le dernier passage ({R3}), puis un droit change dans la V3 : la V3 gagne (role retire de la V2), et l'ecart " +
    $"« {(ecartDroits.Rows.Count > 0 ? ecartDroits.Rows[0][0] : "")} » garde la valeur perdue ({(ecartDroits.Rows.Count > 0 ? $"{ecartDroits.Rows[0][1]} : {ecartDroits.Rows[0][2]} -> {ecartDroits.Rows[0][3]}" : "aucun")}) -- {Dit(rConflit)}");

// --- SOC-004 CA04 : un compte qui n'a jamais agi, supprime dans la V3, disparait de la V2 avec ses roles ---------------------------
var supprime = cree.Compte is { } cs ? await Geste(cn => comptes.SupprimerAsync(cn, resp, new DemandeSurCompte(cs.Id, Ecran), default)) : null;
var p13 = await Passer();
var resteNouveau = await Nombre(v2.Connexion, "SELECT COUNT(*) FROM dbo.UTILISATEUR WHERE UTLST_LOGIN = 'banc.droits.nouveau'") +
                   await Nombre(v2.Connexion, $"SELECT COUNT(*) FROM dbo.HABILIT_UTILISATEUR_ROLE_USER WHERE HUROL_ID_UTLST = {idNouveauV2 ?? "-1"}");
Exiger(supprime is { Fait: true } && p13.Erreur is null && resteNouveau == 0,
    $"SOC-004 CA04 / SOC-002 T05 : le compte cree par le banc (jamais agi) supprime dans la V3 est retire de la V2, ses roles d'abord ({resteNouveau} ligne restante) {supprime?.Message}");

// ====================================================================================================
// 2. LA CONTRE-EPREUVE : sans le declencheur des roles, la V2 ne suit plus, et l'oracle le voit
// ====================================================================================================
await Executer(v3.Connexion, "DISABLE TRIGGER soc.TR_CompteRole_FileRecopie ON soc.CompteRole;");
var sansDeclencheur = await Geste(cn => comptes.RoleAsync(cn, resp, new DemandeRole(idRolesV3, R2, true, Ecran), default));
await Passer();
var rRouge = await Comparer(compteRoles);
await Executer(v3.Connexion, "ENABLE TRIGGER soc.TR_CompteRole_FileRecopie ON soc.CompteRole;");
Exiger(sansDeclencheur.Fait && rRouge.Ecarts.Count > 0,
    $"contre-epreuve : le declencheur des roles coupe, le role {R2} donne dans la V3 n'arrive pas dans la V2 et l'oracle le voit ({rRouge.Ecarts.Count} fonction(s) differente(s))");
var rattrape = await Geste(cn => comptes.RoleAsync(cn, resp, new DemandeRole(idRolesV3, R2, false, Ecran), default));
await Geste(cn => comptes.RoleAsync(cn, resp, new DemandeRole(idRolesV3, R2, true, Ecran), default));
var p14 = await Passer();
var rRattrape = await Comparer(compteRoles);
Exiger(rattrape.Fait && p14.Erreur is null && rRattrape.Ecarts.Count == 0,
    $"contre-epreuve (fin) : le declencheur remis, le changement suivant du compte remet la V2 d'accord avec la V3 -- {Dit(rRattrape)}");

// ====================================================================================================
// 3. AUCUN AUTRE COMPTE TOUCHE : chaque compte actif de la V2, chaque fonction
// ====================================================================================================
var actifs = Colonne0(await Lire(v2.Connexion, "SELECT CONVERT(varchar(20), UTLST_ID_UTLST) FROM dbo.UTILISATEUR WHERE UTLST_DESACTIVE = 0 ORDER BY UTLST_ID_UTLST"));
var oracleFinal = await OracleDroitsV2.LireAsync(v2.Connexion);
var correspondances = (await Lire(v3.Connexion, $"SELECT IdV2, IdV3 FROM rec.Correspondance WHERE BaseV2 = '{v2.Nom}' AND TableV2 = 'UTILISATEUR' AND TableV3 = 'soc.Compte'"))
    .Rows.Cast<DataRow>().ToDictionary(r => (string)r[0], r => int.Parse((string)r[1]));
var comparaisons = 0;
var comptesFaux = new List<string>();
await using (var cn = new SqlConnection(v3.Connexion))
{
    await cn.OpenAsync();
    foreach (var a in actifs.Where(correspondances.ContainsKey))
    {
        var d = await ServiceDroits.FonctionsAutoriseesAsync(cn, correspondances[a], default);
        var faux = oracleFinal.Fonctions.Count(f => oracleFinal.ALaFonctionnalite(int.Parse(a), f) != d.Contains(f));
        comparaisons += oracleFinal.Fonctions.Count;
        if (faux > 0) comptesFaux.Add($"{a} ({faux})");
    }
}
Exiger(comptesFaux.Count == 0 && comparaisons > 0,
    $"SOC-002 CA07 : apres tous les cas, chaque compte actif de la V2 ({actifs.Count(correspondances.ContainsKey)}) repond comme la V3 pour chaque fonction ({comparaisons} comparaisons) " +
    string.Join(", ", comptesFaux));

// ====================================================================================================
// 4. LE CONTROLE DE NUIT DES DROITS (lot 20 : REC-006 CA01 « pour chaque table partagee », CA02, CA04 ; SOC-002 T06)
//    Le meme controle que la nuit (ComparaisonBases : etats figes des deux bases), sur la base de ce banc apres sa « journee » de
//    recopie de droits ci-dessus. Il compare la REPONSE de la V2 a celle de la V3, compte par compte, fonction par fonction.
// ====================================================================================================
var optionsNuit = new OptionsComparaison(v2.Connexion, v3.Connexion) { Tables = ["UTILISATEUR"] };
var nuitPropre = await ComparaisonBases.ComparerAsync(optionsNuit);
var droitsPropre = nuitPropre.Tables.SingleOrDefault(t => t.TableV2 == DroitsRecopies.TableControle);
var comptesActifsRecopies = actifs.Count(correspondances.ContainsKey);
var enBasePropre = await Texte(v3.Connexion, $"SELECT CONCAT(LignesV2, '/', Comparees, '/', Ecarts) FROM rec.ComparaisonTable WHERE IdComparaison = {nuitPropre.Id} AND TableV2 = '{DroitsRecopies.TableControle}'");
Exiger(nuitPropre.Etat == "fini" && droitsPropre is not null && droitsPropre.LignesV2 == comptesActifsRecopies && comptesActifsRecopies > 0
       && droitsPropre.Comparees == (long)comptesActifsRecopies * oracleFinal.Fonctions.Count && droitsPropre.Ecarts == 0
       && !nuitPropre.Details.Any(d => d.TableV2 == DroitsRecopies.TableControle)
       && enBasePropre == $"{comptesActifsRecopies}/{droitsPropre.Comparees}/0",
    $"REC-006 CA01 (droits) / SOC-002 T06 : apres la journee de recopie, le controle de nuit compare les droits de chaque compte actif recopie ({droitsPropre?.LignesV2} comptes, " +
    $"{oracleFinal.Fonctions.Count} fonctions : {droitsPropre?.Comparees} reponses V2 / V3), 0 ecart, et l'ecrit au rapport de la base (rec.ComparaisonTable : {enBasePropre}) — {droitsPropre?.Detail}");

// Un role donne A LA MAIN dans l'ancien RIG, hors recopie (aucun declencheur ne le suit : le rapport est la seule trace).
var roleMain = (await Texte(v2.Connexion, $"SELECT TOP (1) r.HROLE_NOM_ROLE FROM dbo.HABILIT_ROLE_USER AS r WHERE r.HROLE_NOM_ROLE IN ('{R1}', '{R2}', '{R3}') " +
                                          $"AND NOT EXISTS (SELECT 1 FROM dbo.HABILIT_UTILISATEUR_ROLE_USER AS h WHERE h.HUROL_ID_UTLST = {compteCompta} AND h.HUROL_NOM_ROLE = r.HROLE_NOM_ROLE) " +
                                          "ORDER BY r.HROLE_NOM_ROLE"))!;
await Executer(v2.Connexion, $"INSERT INTO dbo.HABILIT_UTILISATEUR_ROLE_USER (HUROL_ID_UTLST, HUROL_NOM_ROLE) VALUES ({compteCompta}, '{roleMain}');");
var attenduMain = await Comparer(compteCompta);   // l'oracle de la V2 contre la V3, independant du serveur
var nuitMain = await ComparaisonBases.ComparerAsync(optionsNuit);
var droitsMain = nuitMain.Tables.Single(t => t.TableV2 == DroitsRecopies.TableControle);
var ecartsDroits = nuitMain.Details.Where(d => d.TableV2 == DroitsRecopies.TableControle).ToList();
var enBaseMain = await Lire(v3.Connexion, $"SELECT IdV2, IdV3, Colonnes, Detail FROM rec.ComparaisonEcart WHERE IdComparaison = {nuitMain.Id} AND TableV2 = '{DroitsRecopies.TableControle}'");
Exiger(attenduMain.Ecarts.Count > 0 && droitsMain.Ecarts == attenduMain.Ecarts.Count && ecartsDroits.Count == attenduMain.Ecarts.Count
       && ecartsDroits.All(d => d.IdV2 == compteCompta && d.IdV3 == idComptaV3.ToString() && d.Detail == "V2 : accordée ; V3 : refusée")
       && ecartsDroits.Select(d => d.Colonnes).Order(StringComparer.Ordinal).SequenceEqual(attenduMain.Ecarts.Select(e => e.Split(' ')[0]).Order(StringComparer.Ordinal))
       && enBaseMain.Rows.Count == ecartsDroits.Count && nuitMain.Ecarts >= droitsMain.Ecarts && nuitMain.CodeSortie == 5,
    $"REC-006 CA02 (droits) : le role {roleMain} donne a la main dans la V2 au compte {compteCompta} (hors recopie) est liste : {droitsMain.Ecarts} fonction(s) que la V2 accorde et que la V3 refuse " +
    $"(compte V2 {compteCompta}, V3 {idComptaV3}, fonction, « V2 : accordée ; V3 : refusée »), les memes que l'oracle de la V2 — par ex. {string.Join(", ", ecartsDroits.Take(4).Select(d => d.Colonnes))} ; " +
    $"ecrit dans rec.ComparaisonEcart ({enBaseMain.Rows.Count} lignes), code de sortie du controle {nuitMain.CodeSortie}");

// REC-006 CA04 : l'alerte d'exploitation (le vrai surveillant, la source AlertesRecopie) nomme la table des droits ; le controle propre suivant la ferme.
const string GreffeNuit = "9303";
var serveurBancs = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
var authentification = new SqlConnectionStringBuilder(serveurBancs.ConnectionString);
authentification.Remove("Data Source");
await Executer(v3.Connexion,
    "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, BaseV3CreeeLe, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe) VALUES " +
    $"('{GreffeNuit}', N'Greffe du banc (droits)', '{serveurBancs.DataSource}', '{v2.Nom}', '{v3.Nom}', SYSDATETIME(), 0, 0, 'Romance Standard Time', 'banc', SYSDATETIME());");
await using var app = ServeurRig.Construire(["--environment", "Development"], s =>
{
    s.WebHost.UseTestServer();
    s.Logging.SetMinimumLevel(LogLevel.Warning);
    s.Configuration["ConnectionStrings:RigV3"] = v3.Connexion;
    s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
    s.Configuration[SignatureDeFond.Reglage] = "0";
    s.Configuration["Supervision:Automatique"] = "false";
    s.Configuration[ReglagesPlanificateur.CleIntervalle] = "0";
});
await app.StartAsync();
var surveillant = app.Services.GetRequiredService<Surveillant>();
var canal = (CanalAlerteSimule)app.Services.GetRequiredService<ICanalAlerte>();
async Task<List<(string Erreur, bool Fermee)>> AlertesEcart() =>
    [.. (await Lire(v3.Connexion, $"SELECT Erreur, CASE WHEN FermeeLe IS NULL THEN 0 ELSE 1 END FROM soc.AlerteExploitation WHERE Regle = '{AlertesRecopie.RegleEcart}' AND CodeGreffe = '{GreffeNuit}' ORDER BY Id"))
        .Rows.Cast<DataRow>().Select(r => ((string)r[0], Convert.ToInt32(r[1]) == 1))];
await surveillant.PasserAsync();
var alertes = await AlertesEcart();
var ouverte = alertes.FirstOrDefault(a => !a.Fermee);
Exiger(ouverte != default && ouverte.Erreur.Contains($"{DroitsRecopies.TableControle} ({droitsMain.Ecarts} ecarts") && ouverte.Erreur.Contains($"controle {nuitMain.Id}")
       && !ouverte.Erreur.Contains(roleMain) && canal.Recues.Any(a => a.Regle == AlertesRecopie.RegleEcart && a.CodeGreffe == GreffeNuit),
    $"REC-006 CA04 (droits) : le role donne a la main leve l'alerte d'exploitation « recopie-ecart » du greffe {GreffeNuit}, qui nomme la table des droits et le rapport (« {ouverte.Erreur} ») " +
    "— aucun role ni valeur dans le message ; envoyee par le canal d'alerte");

// Le jumeau (meme controle, ce que la V2 dit est ce que la V3 dit) : le role retire a la main, plus aucun ecart de droits, l'alerte se ferme.
await Executer(v2.Connexion, $"DELETE FROM dbo.HABILIT_UTILISATEUR_ROLE_USER WHERE HUROL_ID_UTLST = {compteCompta} AND HUROL_NOM_ROLE = '{roleMain}';");
var nuitRetire = await ComparaisonBases.ComparerAsync(optionsNuit);
var droitsRetire = nuitRetire.Tables.Single(t => t.TableV2 == DroitsRecopies.TableControle);
await surveillant.PasserAsync();
Exiger(droitsRetire.Ecarts == 0 && droitsRetire.Comparees == droitsPropre!.Comparees && (await AlertesEcart()).All(a => a.Fermee),
    $"REC-006 CA04 (droits, jumeau) : le role retire a la main, le controle suivant ne trouve plus d'ecart de droits ({droitsRetire.Ecarts}) et l'alerte est fermee");

// Un droit retire dans la V3 sans que la recopie l'ait passe (le declencheur coupe, l'agent garde le droit dans la V2) : meme controle, meme alerte.
await Executer(v3.Connexion, "DISABLE TRIGGER soc.TR_CompteRole_FileRecopie ON soc.CompteRole;");
var roleRetireV3 = await Geste(cn => comptes.RoleAsync(cn, resp, new DemandeRole(idRolesV3, R2, false, Ecran), default));
var nuitRatee = await ComparaisonBases.ComparerAsync(optionsNuit);
await Executer(v3.Connexion, "ENABLE TRIGGER soc.TR_CompteRole_FileRecopie ON soc.CompteRole;");
var droitsRatee = nuitRatee.Tables.Single(t => t.TableV2 == DroitsRecopies.TableControle);
var attenduRatee = await Comparer(compteRoles);
Exiger(roleRetireV3.Fait && droitsRatee.Ecarts > 0 && droitsRatee.Ecarts == attenduRatee.Ecarts.Count
       && nuitRatee.Details.Where(d => d.TableV2 == DroitsRecopies.TableControle).All(d => d.IdV2 == compteRoles && d.Detail == "V2 : accordée ; V3 : refusée"),
    $"REC-006 CA01 (droits) : une recopie de droits ratee (le role {R2} retire dans la V3, jamais passe dans la V2) est vue par le controle : {droitsRatee.Ecarts} fonction(s) du compte {compteRoles}, " +
    $"comme l'oracle de la V2 ({attenduRatee.Ecarts.Count})");
// Le changement suivant du compte (declencheur remis) met la V2 d'accord, comme dans la contre-epreuve ci-dessus.
await Geste(cn => comptes.RoleAsync(cn, resp, new DemandeRole(idRolesV3, R2, true, Ecran), default));
await Geste(cn => comptes.RoleAsync(cn, resp, new DemandeRole(idRolesV3, R2, false, Ecran), default));
await Passer();
var nuitRattrapee = await ComparaisonBases.ComparerAsync(optionsNuit);
Exiger(nuitRattrapee.Tables.Single(t => t.TableV2 == DroitsRecopies.TableControle).Ecarts == 0,
    "REC-006 CA01 (droits, jumeau) : la recopie passee, le controle suivant ne voit plus d'ecart de droits");
await app.StopAsync();

await SupprimerInstantanes(v2.Nom);
return Bilan();

// Un transfert garde son instantane de la base V2 : la base jetable ne se supprime pas tant qu'il existe.
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

int Bilan()
{
    Console.WriteLine();
    if (echecs.Count == 0)
    {
        Console.WriteLine("VERT : les droits changes dans la V3 sont ceux de la V2, chacun dans son origine (SOC-002 T05, CA07 ; REC-017 CA01).");
        return 0;
    }
    Console.WriteLine($"ROUGE : {echecs.Count} controle(s) en echec.");
    foreach (var e in echecs) Console.WriteLine("  - " + e);
    return 1;
}
