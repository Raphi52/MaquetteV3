using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RigV3.Base;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.Socle;
using RigV3.Serveur;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;
using RigV3Desktop.Pont;
using RigV3Desktop.Pont.EcransGeneres;

// Banc des ECRANS GENERES BRANCHES SUR LA BASE (plan « ecrans retrouves branches sur la base », lot 1, 24/11/2026).
// Ce que la compilation ne dit pas, et que ce banc verifie sur des bases jetables :
//   1. les LECTEURS (Serveur/Socle/EcransGeneres/) sur des lignes controlees : les filtres de l'ancien RIG (chaque
//      refus a son jumeau accepte), la regle d'etat, l'ordre, le plafond et le total ;
//   2. la ROUTE par le vrai serveur en memoire : agent admis dans le greffe, 401 / 404 / 503 sans repli, procedure
//      sans lecteur = Branchee faux ;
//   3. le PONT (Pont/EcranGenere.cs) contre ce serveur : vraies lignes, exemples sans serveur ou sans greffe, ecran
//      reuni « mixte », motif d'une panne ; et, apres les jeux (3b), l'ecran reuni entierement branche en « base » ;
//   4. TOUT lecteur trouve (ceux des lots suivants compris) contre le DESCRIPTEUR de sa procedure : groupes et etats
//      au mot pres, rangs de marches, regle des ecrans generes (AFaire aucune marche, EnCours au moins une, Validee
//      toutes les obligatoires), et au moins une ligne sur le banc.
//
// BRANCHER UN ECRAN DE PLUS (lots suivants) : son lecteur (un fichier, mode d'emploi en tete de LecteurEcran.cs), et
// dans tests/EcransGeneresBaseCheck/Jeux/ deux fichiers neufs, aucune ligne de ce Program.cs a modifier :
//   - <PROCEDURE>.sql : ses lignes controlees. Le banc joue chaque jeu sur la base du greffe, avec le parametre
//     @aujourdhui (la date du jour du banc). Un jeu cree ses tables v2.* s'il les trouve absentes
//     (IF OBJECT_ID(N'v2.X') IS NULL CREATE TABLE ...) et leur ajoute les colonnes qui lui manquent
//     (IF COL_LENGTH('v2.X', 'COL') IS NULL ALTER TABLE ...) : les types sont ceux de RIG_DEV, cites. Identifiants
//     de dossier, d'entreprise et d'action : a partir de 100000 x le rang du lot, pour ne pas croiser un autre jeu ;
//   - <PROCEDURE>.attendu : les lignes que le lecteur doit rendre, DANS L'ORDRE, une par ligne, au format
//     « Nom | Precisions | Groupe | Etat | [faites] » (celui des ECHEC de ce banc). Une date relative au jour du banc
//     s'ecrit {J-5}, {M-16} ou {A+1} (jours, mois, annees) ; les lignes vides et celles qui commencent par # sont
//     ignorees : y dire pourquoi chaque refus est refuse. Chaque refus a son jumeau accepte (meme borne, autre cote).
// Puis la section 4 controle tout lecteur contre son descripteur.
//
// Il lui faut le SQL Server de developpement : « docker compose up -d » a la racine du depot.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
var jour = DateTime.Today;
string D(DateTime d) => FormeLigne.Date(d);

// ============================================================ 0. LES BASES, LE GREFFE, L'AGENT

await using var commune = await BaseJetable.CreerAsync("EcrGenCommune");
await using var greffe = await BaseJetable.CreerAsync("EcrGenGreffe");
await Migrateur.AppliquerAsync(commune.Connexion, Path.Combine(racine, "Base", "migrations"));
await Migrateur.AppliquerAsync(greffe.Connexion, Path.Combine(racine, "Base", "migrations"));
const string Greffe = "9201", GreffeMuet = "9203", GreffeAbsent = "9299", Agent = "banc.ecrans";

async Task Executer(string connexion, string sql, params (string Nom, object? Valeur)[] p)
{
    await using var cn = new SqlConnection(connexion);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn);
    foreach (var (nom, valeur) in p) cmd.Parameters.AddWithValue(nom, valeur ?? DBNull.Value);
    await cmd.ExecuteNonQueryAsync();
}
Task Sql(string sql, params (string Nom, object? Valeur)[] p) => Executer(greffe.Connexion, sql, p);

await Sql("""
    INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES (@g, 'ECR', N'Ecrans generes');
    INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANC', 'Banc EcransGeneresBase');
    INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
    SELECT @a, 'BANC', 'Ecrans', 'BANCECR', 'M.', @g, Id, 0 FROM soc.Service;
    INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte;
    """, ("@g", Greffe), ("@a", Agent));
var serveurBancs = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
var authentification = new SqlConnectionStringBuilder(serveurBancs.ConnectionString);
authentification.Remove("Data Source");   // le serveur et la base viennent du catalogue
foreach (var (code, nomBase) in new[] { (Greffe, greffe.Nom), (GreffeMuet, $"{BaseJetable.Prefixe}inexistante_{Guid.NewGuid():N}") })
    await Executer(commune.Connexion,
        "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe) " +
        "VALUES (@c, @n, @s, @v2, @v3, 0, 0, 'Romance Standard Time', 'banc', SYSDATETIME());",
        ("@c", code), ("@n", $"Greffe {code} du banc"), ("@s", serveurBancs.DataSource), ("@v2", "RIG_BANC_" + code), ("@v3", nomBase));

// Les tables V2 lues par les deux premiers lecteurs : les colonnes qu'ils lisent, aux types de RIG_DEV
// (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 24/11/2026). DOSSIER_RCS, ENTREPRISE et ACTION_RCS sont les
// fiches rec.* des migrations (Serveur/Recopie/FichesV3.cs).
await Sql("""
    CREATE TABLE v2.DEMANDE (DMND_ID_DMND int NOT NULL PRIMARY KEY, DMND_ID_DSRCS int NULL, DMND_ID_ACTRC int NULL,
        DMND_NUM_DEMANDE varchar(11) NOT NULL, DMND_ETAT_DEMANDE varchar(1) NOT NULL, DMND_CODE_PROSS varchar(40) NULL,
        DMND_DATE_CREATION datetime NULL, DMND_DATE_VALIDATION datetime NULL);
    CREATE TABLE v2.DEMAT_DCA (DMTDCA_ID_DMTDCA int NOT NULL PRIMARY KEY, DMTDCA_ID_DMND int NULL, DMTDCA_ORIGINE varchar(10) NOT NULL,
        DMTDCA_EXERCICE_MILLESIME varchar(4) NULL, DMTDCA_DATE_CLOTURE datetime NULL);
    CREATE TABLE v2.DEPOT_RCS (DPTRC_ID_DPTRC int NOT NULL PRIMARY KEY, DPTRC_ID_EXRCC int NULL, DPTRC_NUM_DEPOT varchar(12) NOT NULL);
    CREATE TABLE v2.EXERCICE (EXRCC_ID_EXRCC int NOT NULL PRIMARY KEY, EXRCC_DATE_CLOTURE datetime NOT NULL);
    CREATE TABLE v2.DILIGENCE_RCS (DLGRC_ID_DLGRC int NOT NULL PRIMARY KEY, DLGRC_ID_ACTRC int NULL, DLGRC_NATURE_DILIGENCE varchar(10) NOT NULL,
        DLGRC_ETAT_DILIGENCE varchar(1) NULL, DLGRC_ETAT_RELANCE varchar(1) NULL);
    CREATE TABLE v2.RELANCE_RCS (RLCRC_ID_RLCRC int NOT NULL PRIMARY KEY, RLCRC_ID_DLGRC int NULL, RLCRC_NUM_ORDRE smallint NOT NULL,
        RLCRC_DATE_RELANCE datetime NOT NULL, RLCRC_DATE_LIMITE datetime NULL);
    CREATE TABLE v2.INSCRIT_PM (INSPM_ID_INSPM int IDENTITY PRIMARY KEY, INSPM_ID_DSRCS int NULL, INSPM_EXPIRATION_PM datetime NULL,
        INSPM_DATE_DISSOLUTION datetime NULL, INSPM_DATE_FORMALITE_DISSOLUTION datetime NULL, INSPM_DATE_EXPIRATION_PRORO_DISS datetime NULL);
    CREATE TABLE v2.MENTION_OBSERVATION (MNTOB_ID_MNTOB int IDENTITY PRIMARY KEY, MNTOB_ID_DSRCS int NULL, MNTOB_CODE_MENTION varchar(5) NULL,
        MNTOB_DIFFUSABLE bit NOT NULL);
    CREATE TABLE v2.PROCEDURE_COLLECTIVE (PRCLL_ID_PRCLL int IDENTITY PRIMARY KEY, PRCLL_ID_DSRCS int NULL, PRCLL_CODE_SOLUTION varchar(5) NULL,
        PRCLL_CODE_CLOTURE varchar(5) NULL);
    CREATE TABLE v2.CODE_SOLUTION (R_CODESOLUTI_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_CODESOLUTI_SURVEILLANCE_RCS bit NOT NULL);
    CREATE TABLE v2.CODE_CLOTURE (R_CODECLOTUR_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_CODECLOTUR_SURVEILLANCE_RCS bit NOT NULL);
    """);

// Un dossier du registre et son entreprise (meme identifiant), un numero de gestion « 2001B000nn ».
async Task Dossier(int id, string nom, string? siren = null, string typeRcs = "B1", bool provisoirementSans = false,
                   DateTime? radiation = null, string? pc = null, DateTime? cessation = null, DateTime? maj = null)
    => await Sql("""
        SET IDENTITY_INSERT rec.Entreprise ON;
        INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES (@id, @siren, '', @nom, 'C', 0);
        SET IDENTITY_INSERT rec.Entreprise OFF;
        SET IDENTITY_INSERT rec.DossierRcs ON;
        INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
            ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
            SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise, DateCessation, DateRadiation, ProcedureCollective)
        VALUES (@id, @id, @type, @gestion, @siren, '20010101', 'C', @sans, 0, @maj, 'A', 'C', 0, 0, 0, 0, 0, 0, 0, @cess, @rad, @pc);
        SET IDENTITY_INSERT rec.DossierRcs OFF;
        """, ("@id", id), ("@nom", nom), ("@siren", siren), ("@type", typeRcs), ("@gestion", $"2001B{id:00000}"), ("@sans", provisoirementSans),
        ("@maj", maj ?? jour.AddYears(-5)), ("@cess", cessation), ("@rad", radiation), ("@pc", pc));
async Task Action(int id, int dossier, int? depot = null)
    => await Sql("""
        SET IDENTITY_INSERT rec.ActionRegistre ON;
        INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification)
        VALUES (@id, @d, @p, 'BANC', 'C', 'V', 0);
        SET IDENTITY_INSERT rec.ActionRegistre OFF;
        """, ("@id", id), ("@d", dossier), ("@p", depot));
async Task Demande(int id, string code, string etat, int? dossier, DateTime? creee, DateTime? validee = null, int? action = null)
    => await Sql("INSERT INTO v2.DEMANDE VALUES (@id, @d, @a, @n, @e, @c, @cr, @va);",
        ("@id", id), ("@d", dossier), ("@a", action), ("@n", $"26{id:000000000}"), ("@e", etat), ("@c", code), ("@cr", creee), ("@va", validee));
async Task Pm(int dossier, DateTime? expiration = null, DateTime? formaliteDissolution = null, DateTime? proroDissolution = null)
    => await Sql("INSERT INTO v2.INSCRIT_PM (INSPM_ID_DSRCS, INSPM_EXPIRATION_PM, INSPM_DATE_FORMALITE_DISSOLUTION, INSPM_DATE_EXPIRATION_PRORO_DISS) VALUES (@d, @e, @f, @p);",
        ("@d", dossier), ("@e", expiration), ("@f", formaliteDissolution), ("@p", proroDissolution));
var numeroRelance = 0;
async Task Relance(int dossier, string nature, DateTime relance, DateTime? limite, string etatDiligence = "1", string etatRelance = "2", short ordre = 1)
{
    var n = ++numeroRelance;
    await Action(9000 + n, dossier);
    await Sql("INSERT INTO v2.DILIGENCE_RCS VALUES (@n, @a, @nat, @ed, @er); INSERT INTO v2.RELANCE_RCS VALUES (@n, @n, @o, @r, @l);",
        ("@n", n), ("@a", 9000 + n), ("@nat", nature), ("@ed", etatDiligence), ("@er", etatRelance), ("@o", ordre), ("@r", relance), ("@l", limite));
}

var lecteurs = LecteursEcrans.Decouvrir(typeof(ServeurRig).Assembly);
async Task<LignesLues> Lire(string procedure, int plafond = 200, DateTime? aujourdhui = null)
{
    await using var cn = new SqlConnection(greffe.Connexion);
    await cn.OpenAsync();
    return await lecteurs.ParProcedure[procedure].LireAsync(cn, aujourdhui ?? jour, plafond, CancellationToken.None);
}
string Texte(LigneLue l) => $"{l.Nom} | {l.Precisions} | {l.Groupe} | {l.Etat} | [{string.Join(",", l.Faites)}]";
void Comparer(IReadOnlyList<LigneLue> lues, string[] attendues, string quoi)
{
    var obtenues = lues.Select(Texte).ToArray();
    var ok = obtenues.SequenceEqual(attendues);
    Exiger(ok, quoi);
    if (!ok)
    {
        Console.WriteLine("      attendu :"); foreach (var a in attendues) Console.WriteLine("        " + a);
        Console.WriteLine("      obtenu  :"); foreach (var o in obtenues) Console.WriteLine("        " + o);
    }
}

// ============================================================ 1a. COMPTES ANNUELS (PROC_DCA)

Exiger(lecteurs.ParProcedure.ContainsKey("PROC_DCA") && lecteurs.ParProcedure.ContainsKey("PROC_ROFSURVEILLANCES"),
    $"les lecteurs sont trouves par reflexion, sans liste : {string.Join(", ", lecteurs.ParProcedure.Keys.Order())}");

await Dossier(1, "ALPHA BANC SARL", "412000001");
await Dossier(2, "BETA BANC SAS", "412000002");
await Dossier(3, "GAMMA BANC SARL", "412000003");
await Dossier(4, "DELTA BANC SAS", "412000004");
await Dossier(5, "EPSILON BANC SARL", null);          // sans SIREN : le numero de gestion le nomme
await Dossier(6, "ZETA BANC SAS", "412000006");
await Dossier(9, "IOTA BANC SARL", "412000009");
await Sql("INSERT INTO v2.EXERCICE VALUES (703, '20241231'); INSERT INTO v2.DEPOT_RCS VALUES (603, 703, 'D603');");
await Action(503, 3, depot: 603);
await Demande(101, "DCA", "Q", 1, jour.AddDays(-2));                                   // a faire
await Demande(110, "DCA", "A", null, jour.AddDays(-1));                                // arrivee, sans dossier
await Demande(102, "DCA", "I", 2, jour.AddDays(-5));                                   // interrompue
await Demande(109, "DCA", "S", 9, jour.AddDays(-6));                                   // code hors ETAT_DEMANDE (vu sur RIG_DEV)
await Demande(105, "DCADEMAT", "N", 5, jour.AddDays(-15), jour.AddDays(-12));          // reclamation
await Demande(103, "DCA", "V", 3, jour.AddDays(-30), jour.AddDays(-20), action: 503);  // valide, exercice par le depot
await Demande(104, "DCADEMAT", "V", 4, jour.AddDays(-10), jour.AddDays(-8));           // valide, flux INPI
await Demande(106, "DCA", "O", 6, jour.AddDays(-40));                                  // refus
await Demande(107, "DCA", "X", 1, jour.AddDays(-1));                                   // supprimee : pas de ligne
await Demande(108, "ROF", "Q", 1, jour.AddDays(-1));                                   // autre procedure : pas de ligne
await Sql("INSERT INTO v2.DEMAT_DCA VALUES (1, 104, 'INPI_DCA', '2025', '20251231'), (2, 105, 'CREFO_DCA', '2025', NULL);");

const string Guichet = "Guichet", Demat = "Dématérialisé (INPI, CREFO)";
var dca = await Lire("PROC_DCA");
Comparer(dca.Lignes,
[
    $"Demande n° 26000000110 | reçu le {D(jour.AddDays(-1))} | {Guichet} | AFaire | []",
    $"ALPHA BANC SARL · SIREN 412 000 001 | reçu le {D(jour.AddDays(-2))} | {Guichet} | AFaire | []",
    $"BETA BANC SAS · SIREN 412 000 002 | reçu le {D(jour.AddDays(-5))} | {Guichet} | EnCours | [0]",
    $"IOTA BANC SARL · SIREN 412 000 009 | reçu le {D(jour.AddDays(-6))} · état « S » de l'ancien RIG | {Guichet} | EnCours | [0]",
    $"EPSILON BANC SARL · 2001 B 00005 | Flux CREFO · Exercice 2025 · réclamation du {D(jour.AddDays(-12))} | {Demat} | EnAttente | [0,1,4]",
    $"DELTA BANC SAS · SIREN 412 000 004 | Flux INPI · Exercice 2025 clos le 31/12/2025 · déposé le {D(jour.AddDays(-8))} | {Demat} | Validee | [0,1,2,3]",
    $"GAMMA BANC SARL · SIREN 412 000 003 | Exercice clos le 31/12/2024 · déposé le {D(jour.AddDays(-20))} | {Guichet} | Validee | [0,1,3]",
    $"ZETA BANC SAS · SIREN 412 000 006 | refusé le {D(jour.AddDays(-40))} | {Guichet} | Rejetee | [0,1,4]",
], "PROC_DCA : demandes DCA et DCADEMAT, etat par DMND_ETAT_DEMANDE, a faire d'abord puis commence, reclamation, clos ; " +
   "supprimee (X) et autre procedure ecartees ; exercice par le depot (guichet) ou le flux (dematerialise)");
Exiger(dca.Total == 8, $"PROC_DCA : total = 8 lignes de la base ({dca.Total})");
var dcaPlafond = await Lire("PROC_DCA", plafond: 3);
Exiger(dcaPlafond.Lignes.Count == 3 && dcaPlafond.Total == 8 && dcaPlafond.Lignes[0].Nom.StartsWith("Demande n°"),
    $"PROC_DCA : plafond 3 -> les 3 premieres lignes, total 8 garde ({dcaPlafond.Lignes.Count} lignes, total {dcaPlafond.Total})");

// ============================================================ 1b. RADIATIONS D'OFFICE (PROC_ROFSURVEILLANCES)

const string Terme = "Dissolution par survenance du terme", TroisAns = "Dissolution depuis plus de trois ans", Sans = "Sans activité";
// S123-124 : terme passe de 15 mois, sans MOTER diffusable, non radie, sans procedure collective.
await Dossier(11, "BANC ROF 11"); await Pm(11, expiration: jour.AddMonths(-16));                       // retenu
await Dossier(12, "BANC ROF 12"); await Pm(12, expiration: jour.AddMonths(-14));                       // 14 mois : pas encore
await Dossier(13, "BANC ROF 13"); await Pm(13, expiration: jour.AddMonths(-20));
await Sql("INSERT INTO v2.MENTION_OBSERVATION (MNTOB_ID_DSRCS, MNTOB_CODE_MENTION, MNTOB_DIFFUSABLE) VALUES (13, 'MOTER', 1), (14, 'MOTER', 0);");
await Dossier(14, "BANC ROF 14"); await Pm(14, expiration: jour.AddMonths(-20));                       // MOTER non diffusable : retenu
await Dossier(15, "BANC ROF 15", radiation: jour.AddMonths(-1)); await Pm(15, expiration: jour.AddMonths(-20));
await Dossier(16, "BANC ROF 16"); await Pm(16, expiration: jour.AddMonths(-20)); await Demande(116, "ROF", "Q", 16, jour.AddDays(-3));
await Dossier(17, "BANC ROF 17"); await Pm(17, expiration: jour.AddMonths(-20)); await Demande(117, "ROF", "V", 17, jour.AddDays(-2), jour.AddDays(-2));
await Dossier(18, "BANC ROF 18"); await Pm(18, expiration: jour.AddMonths(-20)); await Demande(118, "ROF", "X", 18, jour.AddDays(-2));
await Sql("""
    INSERT INTO v2.CODE_CLOTURE VALUES ('CLNO', 0), ('CLOK', 1);
    INSERT INTO v2.PROCEDURE_COLLECTIVE (PRCLL_ID_DSRCS, PRCLL_CODE_SOLUTION, PRCLL_CODE_CLOTURE) VALUES (19, NULL, 'CLNO'), (20, NULL, 'CLOK'), (22, NULL, NULL);
    """);
await Dossier(19, "BANC ROF 19"); await Pm(19, expiration: jour.AddMonths(-20));                       // cloture qui exclut
await Dossier(20, "BANC ROF 20"); await Pm(20, expiration: jour.AddMonths(-20));                       // cloture qui laisse surveiller : retenu
await Dossier(21, "BANC ROF 21", pc: "RJ"); await Pm(21, expiration: jour.AddMonths(-20));            // DSSRC_PROCEDURE_PC
await Dossier(22, "BANC ROF 22"); await Pm(22, expiration: jour.AddMonths(-20));                       // procedure sans code : exclut
// S123-131 : dissous depuis plus de 3 ans (ou fin de prorogation de plus de 3 ans).
await Dossier(31, "BANC ROF 31"); await Pm(31, formaliteDissolution: jour.AddYears(-4)); await Action(1031, 31);
await Dossier(32, "BANC ROF 32"); await Pm(32, formaliteDissolution: jour.AddYears(-4)); await Relance(32, "S123-131", jour.AddDays(-20), jour.AddDays(10));
await Dossier(33, "BANC ROF 33"); await Pm(33, formaliteDissolution: jour.AddYears(-2)); await Action(1033, 33);
await Dossier(34, "BANC ROF 34", typeRcs: "A2"); await Pm(34, formaliteDissolution: jour.AddYears(-4)); await Action(1034, 34);
await Dossier(35, "BANC ROF 35"); await Pm(35, formaliteDissolution: jour.AddYears(-4)); await Relance(35, "S123-131", jour.AddDays(-60), jour.AddDays(-5));
await Dossier(36, "BANC ROF 36"); await Pm(36, formaliteDissolution: jour.AddYears(-10), proroDissolution: jour.AddYears(-2)); await Action(1036, 36);
await Dossier(37, "BANC ROF 37"); await Pm(37, formaliteDissolution: jour.AddYears(-1), proroDissolution: jour.AddYears(-4)); await Action(1037, 37);
// SSAC2A : provisoirement sans activite, ni cessation ni dissolution, premiere relance expiree.
await Dossier(41, "BANC ROF 41", provisoirementSans: true, maj: jour.AddDays(-1)); await Pm(41); await Relance(41, "SSAC2A", jour.AddDays(-90), jour.AddDays(-10));
await Dossier(42, "BANC ROF 42", provisoirementSans: true); await Pm(42); await Relance(42, "SSAC2A", jour.AddDays(-30), jour.AddDays(5));
await Dossier(43, "BANC ROF 43"); await Pm(43); await Relance(43, "SSAC2A", jour.AddDays(-90), jour.AddDays(-10));
await Dossier(44, "BANC ROF 44", provisoirementSans: true); await Pm(44); await Relance(44, "SSAC2A", jour.AddDays(-90), jour.AddDays(-10), ordre: 2);
await Dossier(45, "BANC ROF 45", provisoirementSans: true, cessation: jour.AddYears(-1)); await Pm(45); await Relance(45, "SSAC2A", jour.AddDays(-90), jour.AddDays(-10));

string TermeLigne(int id, string etat = "AFaire", string faites = "[]", string? suite = null)
    => $"BANC ROF {id} · 2001 B {id:00000} | Terme survenu le {D(jour.AddMonths(id is 11 ? -16 : -20))}{suite} | {Terme} | {etat} | {faites}";
string SansRelanceLigne(int id, DateTime dissolution)
    => $"BANC ROF {id} · 2001 B {id:00000} | Dissolution déclarée le {D(dissolution)} · sans relance en cours | {TroisAns} | AFaire | []";
var termes = new[]
{
    TermeLigne(11), TermeLigne(14),
    TermeLigne(16, "EnCours", "[0,1]", $" · radiation commencée le {D(jour.AddDays(-3))}"),
    TermeLigne(17, "Validee", "[0,1,2,3]", $" · radiation du {D(jour.AddDays(-2))}"),
    TermeLigne(18), TermeLigne(20),
};
var ssac2a = $"BANC ROF 41 · 2001 B 00041 | Relance du {D(jour.AddDays(-90))} expirée le {D(jour.AddDays(-10))} · mis à jour après la dernière relance | {Sans} | AFaire | []";

var rof = await Lire("PROC_ROFSURVEILLANCES");
Comparer(rof.Lignes,
[
    .. termes,
    SansRelanceLigne(31, jour.AddYears(-4)), SansRelanceLigne(35, jour.AddYears(-4)), SansRelanceLigne(37, jour.AddYears(-1)),
    ssac2a,
], "PROC_ROFSURVEILLANCES (reglage ROFSURV_S123_136 absent : S123-131 sans relance en cours) : S123-124 a 15 mois du terme " +
   "(14 mois, MOTER diffusable, radie, procedure collective ecartes ; MOTER non diffusable, cloture qui laisse surveiller retenus) ; " +
   "S123-131 dissous ou proroge depuis 3 ans (2 ans, type A2, relance en cours ecartes) ; SSAC2A relance expiree (relance a venir, " +
   "2e relance, activite, cessation ecartes) ; etat par la demande ROF (Q en cours, V validee, X ignoree) ; tri surveillance puis gestion");
Exiger(rof.Total == 10, $"PROC_ROFSURVEILLANCES : total = 10 ({rof.Total})");

await Sql("INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('ROFSURV_S123_136', 'Relance seule', '1');");
var rofRelance = await Lire("PROC_ROFSURVEILLANCES");
Comparer(rofRelance.Lignes,
[
    .. termes,
    $"BANC ROF 35 · 2001 B 00035 | Relance du {D(jour.AddDays(-60))} expirée le {D(jour.AddDays(-5))} | {TroisAns} | AFaire | []",
    ssac2a,
], "PROC_ROFSURVEILLANCES (reglage ROFSURV_S123_136 = 1) : S123-131 seulement relance expiree (31 et 37 sans relance ecartes, 32 relance a venir ecarte)");
// fix-ok: banc plante mesure (SqlException « SOC-003 CA07 : modifier un reglage pose son auteur et sa date ») : le
// declencheur soc.TR_Reglage_Auteur exige AuteurModif et DateModif sur toute modification d'un reglage.
await Sql("UPDATE soc.Reglage SET Valeur = 'OUI', AuteurModif = 'banc', DateModif = SYSDATETIME() WHERE Code = 'ROFSURV_S123_136';");
var rofOui = await Lire("PROC_ROFSURVEILLANCES");
Exiger(rofOui.Total == 10, $"PROC_ROFSURVEILLANCES : le reglage se lit « == \"1\" » comme la V2 (« OUI » = case decochee : {rofOui.Total} lignes, 10 attendues)");
await Sql("DELETE FROM soc.Reglage WHERE Code = 'ROFSURV_S123_136';");

// ============================================================ 2. LA ROUTE, PAR LE VRAI SERVEUR EN MEMOIRE

await using var app = ServeurRig.Construire(["--environment", "Development"], s =>
{
    s.WebHost.UseTestServer();
    s.Logging.SetMinimumLevel(LogLevel.Warning);
    s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
    s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
});
await app.StartAsync();
var http = app.GetTestClient();
http.DefaultRequestHeaders.Add(RoutesIdentite.EnTeteCompte, Agent);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

var parRoute = await http.GetFromJsonAsync<LignesEcran>(RoutesEcransGeneres.Pour(Greffe, "PROC_DCA"), json);
Exiger(parRoute is { Branchee: true, Total: 8, Plafond: 200 } && parRoute.Lignes.Select(Texte).SequenceEqual(dca.Lignes.Select(Texte)),
    $"route {RoutesEcransGeneres.Pour(Greffe, "PROC_DCA")} : les lignes du lecteur, Branchee, total et plafond ({parRoute?.Lignes.Length} lignes)");
var rofRoute = await http.GetFromJsonAsync<LignesEcran>(RoutesEcransGeneres.Pour(Greffe, "PROC_ROFSURVEILLANCES"), json);
Exiger(rofRoute is { Branchee: true, Total: 10 }, $"route PROC_ROFSURVEILLANCES : 10 lignes, la date du jour du serveur ({rofRoute?.Total})");
var sansLecteur = await http.GetFromJsonAsync<LignesEcran>(RoutesEcransGeneres.Pour(Greffe, "PROC_SANS_LECTEUR"), json);
Exiger(sansLecteur is { Branchee: false, Total: 0 } && sansLecteur.Lignes.Length == 0,
    "route : une procedure sans lecteur rend 200 Branchee = faux, sans ligne (l'ecran garde ses exemples)");
var anonyme = app.GetTestClient();
var sansSession = await anonyme.GetAsync(RoutesEcransGeneres.Pour(Greffe, "PROC_DCA"));
Exiger(sansSession.StatusCode == HttpStatusCode.Unauthorized, $"route : sans session -> 401 ({(int)sansSession.StatusCode})");
var absent = await http.GetAsync(RoutesEcransGeneres.Pour(GreffeAbsent, "PROC_DCA"));
var muet = await http.GetAsync(RoutesEcransGeneres.Pour(GreffeMuet, "PROC_DCA"));
Exiger(absent.StatusCode == HttpStatusCode.NotFound && muet.StatusCode == HttpStatusCode.ServiceUnavailable,
    $"route : greffe absent du catalogue -> 404 ({(int)absent.StatusCode}), base muette -> 503 ({(int)muet.StatusCode}), sans repli");
Exiger(ServeurRig.Decouvrir(typeof(ServeurRig).Assembly).Any(m => m.GetType().Name == "ModuleEcransGeneres"),
    "le module des ecrans generes est trouve par reflexion, sans toucher Serveur/ServeurRig.cs");

// ============================================================ 3. LE PONT (Pont/EcranGenere.cs) CONTRE CE SERVEUR

Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, Agent);
ClientServeur client;
try { client = new ClientServeur(new Uri("http://localhost/"), app.GetTestServer().CreateHandler()); }
finally { Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null); }
var descripteurDca = CatalogueGenere.Descripteur("PROC_DCA")!;
var nomsExemple = CatalogueGenere.Tous.SelectMany(d => d.Lignes).Select(l => l.Nom).ToHashSet();

var demo = await EcranGenere.LignesAsync(null, null, "PROC_DCA");
Exiger(demo.Mode == "exemple" && demo.Message is null && demo.Lignes.SequenceEqual(descripteurDca.Lignes) && demo.Exemples.SequenceEqual(descripteurDca.Groupes),
    "pont sans serveur : les lignes d'exemple du descripteur, tous ses groupes marques « exemple »");
var sansGreffe = await EcranGenere.LignesAsync(client, null, "PROC_DCA");
Exiger(sansGreffe.Mode == "exemple" && sansGreffe.Message?.Contains("Aucun greffe") == true,
    $"pont sans greffe ouvert : les exemples, et pourquoi ({sansGreffe.Message})");
var base_ = await EcranGenere.LignesAsync(client, Greffe, "PROC_DCA");
Exiger(base_.Mode == "base" && base_.Greffe == Greffe && base_.Exemples.Length == 0 && base_.Message is null
       && base_.Lignes.Select(l => l.Nom).SequenceEqual(dca.Lignes.Select(l => l.Nom)) && !base_.Lignes.Any(l => nomsExemple.Contains(l.Nom)),
    $"pont branche : les {base_.Lignes.Length} lignes de la base, aucun nom fictif d'un descripteur, aucun groupe en exemple");
var reuniDca = CatalogueGenere.Tous.First(d => d.Membres.Any(m => m.Code == "PROC_DCA"));
var mixte = await EcranGenere.LignesAsync(client, Greffe, reuniDca.Code);
var autres = reuniDca.Membres.Where(m => m.Code != "PROC_DCA").ToArray();
Exiger(mixte.Mode == "mixte" && mixte.Exemples.SequenceEqual(autres.Select(m => m.Titre))
       && mixte.Lignes.Count(l => l.Groupe == descripteurDca.Titre) == 8
       && mixte.Lignes.First(l => l.Groupe == descripteurDca.Titre).Precisions == $"{Guichet} · reçu le {D(jour.AddDays(-1))}"
       && autres.All(m => mixte.Lignes.Count(l => l.Groupe == m.Titre) == m.Lignes.Length),
    $"pont, ecran reuni « {reuniDca.Titre} » : les 8 lignes de la base dans le groupe « {descripteurDca.Titre} » (ancien groupe en tete), " +
    $"les autres procedures ({string.Join(", ", autres.Select(m => m.Titre))}) en exemple");
// Un groupe BRANCHE mais sans aucune ligne (la copie de dev n'en garde que 200 par table) ne disparait pas : la vue le
// porte dans Vides, l'ecran le montre avec « Aucune ligne ». Serveur factice : DCA vide, les autres procedures sans lecteur.
using var videHandler = new RepondeurVide(descripteurDca.Code);
var clientVide = new ClientServeur(new Uri("http://localhost/"), videHandler);
var reuniVide = await EcranGenere.LignesAsync(clientVide, Greffe, reuniDca.Code);
Exiger(reuniVide.Vides.SequenceEqual([descripteurDca.Titre]) && !reuniVide.Exemples.Contains(descripteurDca.Titre)
       && reuniVide.Lignes.All(l => l.Groupe != descripteurDca.Titre) && reuniVide.Exemples.SequenceEqual(autres.Select(m => m.Titre)),
    $"pont, ecran reuni, procedure branchee SANS ligne : « {descripteurDca.Titre} » est dans Vides, pas dans Exemples ({string.Join(", ", reuniVide.Vides)})");
Exiger(base_.Vides.Length == 0 && mixte.Vides.Length == 0, "pont : une procedure qui a des lignes n'est jamais dans Vides");
var panne = await EcranGenere.LignesAsync(client, GreffeAbsent, "PROC_DCA");
Exiger(panne.Mode == "erreur" && panne.Lignes.SequenceEqual(descripteurDca.Lignes) && panne.Message?.Contains(GreffeAbsent) == true,
    $"pont, greffe absent (404) : les exemples restent, marques, avec le motif du serveur ({panne.Message})");

// ============================================================ 3b. LES JEUX DES LOTS SUIVANTS
// Un fichier par procedure (voir l'en-tete), joues APRES les sections 2 et 3 : leurs lignes ne doivent pas changer
// ce que la route et le pont lisent des lignes de PROC_DCA et PROC_ROFSURVEILLANCES posees plus haut.
var jeux = Directory.Exists(Path.Combine(racine, "tests", "EcransGeneresBaseCheck", "Jeux"))
    ? Directory.GetFiles(Path.Combine(racine, "tests", "EcransGeneresBaseCheck", "Jeux"), "*.sql").Order(StringComparer.Ordinal).ToArray()
    : [];
foreach (var jeu in jeux)
{
    try { await Sql(File.ReadAllText(jeu), ("@aujourdhui", jour)); Exiger(true, $"jeu {Path.GetFileName(jeu)} joue"); }
    catch (SqlException e) { Exiger(false, $"jeu {Path.GetFileName(jeu)} : {e.Message}"); }
}
// Les lignes attendues de chaque jeu (<PROCEDURE>.attendu), lues apres TOUS les jeux : un jeu ne doit pas faire entrer
// ses lignes dans l'ecran d'un autre. Un jeu sans .attendu, ou un .attendu sans lecteur, est une faute du lot.
foreach (var jeu in jeux)
{
    var code = Path.GetFileNameWithoutExtension(jeu);
    var fichier = Path.ChangeExtension(jeu, ".attendu");
    if (!File.Exists(fichier)) { Exiger(false, $"jeu {code} : {Path.GetFileName(fichier)} absent (les lignes que son lecteur doit rendre)"); continue; }
    if (!lecteurs.ParProcedure.ContainsKey(code)) { Exiger(false, $"jeu {code} : aucun lecteur pour cette procedure"); continue; }
    var attendues = File.ReadAllLines(fichier)
        .Where(l => l.Trim() is { Length: > 0 } t && !t.StartsWith('#'))
        .Select(l => System.Text.RegularExpressions.Regex.Replace(l.Trim(), @"\{([JMA])([+-]\d+)\}", m =>
        {
            var n = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            return D(m.Groups[1].Value switch { "J" => jour.AddDays(n), "M" => jour.AddMonths(n), _ => jour.AddYears(n) });
        }))
        .ToArray();
    var lues = await Lire(code);
    Comparer(lues.Lignes, attendues, $"{code} : les lignes de {Path.GetFileName(fichier)}, dans l'ordre");
    Exiger(lues.Total == attendues.Length, $"{code} : total = {attendues.Length} lignes attendues ({lues.Total})");
}
// L'ecran reuni des radiations d'office (« Relances et injonctions ») a TOUS ses membres branches depuis le lot 1 du plan
// plan-f7ba4de2 : lu APRES les jeux, parce que les lecteurs des relances et injonctions lisent des colonnes que seuls
// leurs jeux ajoutent aux tables du banc. Le mode « mixte » reste controle plus haut par l'ecran reuni de PROC_DCA.
// fix-ok: mesure le 25/11/2026 (lot 1, plan-f7ba4de2) -- a l'ancien emplacement (section 3, avant les jeux) le banc rendait
// ROUGE, 1 echec, ce controle : l'ecran n'est plus « mixte » (ses 6 membres ont un lecteur) et ces lecteurs echouent
// sur les colonnes que leurs jeux n'ont pas encore ajoutees. Deplacement accorde par le chef (msg-04572c8e).
var reuniRof = CatalogueGenere.Tous.First(d => d.Membres.Any(m => m.Code == "PROC_ROFSURVEILLANCES"));
var baseRof = await EcranGenere.LignesAsync(client, Greffe, reuniRof.Code);
Exiger(baseRof.Mode == "base" && baseRof.Exemples.Length == 0
       && baseRof.Lignes.Count(l => l.Groupe == CatalogueGenere.Descripteur("PROC_ROFSURVEILLANCES")!.Titre) == 10,
    $"pont, ecran reuni « {reuniRof.Titre} » (tous ses membres branches) : mode base, aucun exemple, les 10 radiations " +
    $"d'office de la base dans leur groupe (mode {baseRof.Mode}, exemples : {string.Join(", ", baseRof.Exemples)})");

// ============================================================ 4. TOUT LECTEUR CONTRE LE DESCRIPTEUR DE SA PROCEDURE

foreach (var (code, lecteur) in lecteurs.ParProcedure.OrderBy(p => p.Key, StringComparer.Ordinal))
{
    var d = CatalogueGenere.Descripteur(code);
    Exiger(d is { Membres.Length: 0 }, $"{code} : son lecteur ({lecteur.GetType().Name}) lit une procedure qui a son descripteur (pas un ecran reuni)");
    if (d is null) continue;
    LignesLues lues;
    try { lues = await Lire(code); }
    catch (SqlException e)
    {
        Exiger(false, $"{code} : le lecteur echoue sur le banc ({e.Message}) -- ajouter son jeu tests/EcransGeneresBaseCheck/Jeux/{code}.sql");
        continue;
    }
    Exiger(lues.Lignes.Count > 0, $"{code} : au moins une ligne sur le banc ({lues.Lignes.Count}) -- sinon le lecteur n'est pas essaye");
    var etats = d.Etats.Select(e => e.Code).ToHashSet();
    var obligatoires = d.Marches.Select((m, k) => (m, k)).Where(x => x.m.Obligatoire).Select(x => x.k).ToArray();
    var fautes = lues.Lignes.Where(l =>
            !d.Groupes.Contains(l.Groupe) || !etats.Contains(l.Etat)
            || l.Faites.Distinct().Count() != l.Faites.Length || l.Faites.Any(k => k < 0 || k >= d.Marches.Length)
            || (l.Etat == "AFaire" && l.Faites.Length > 0) || (l.Etat == "EnCours" && l.Faites.Length == 0)
            || (l.Etat == "Validee" && obligatoires.Except(l.Faites).Any()))
        .Select(Texte).ToList();
    Exiger(fautes.Count == 0, $"{code} : chaque ligne a un groupe et un etat du descripteur, des marches de l'ecran, et suit la regle " +
        $"AFaire / EnCours / Validee ({lues.Lignes.Count} lignes{(fautes.Count > 0 ? " ; fautives : " + string.Join(" // ", fautes) : "")})");
    var couverts = lues.Lignes.Select(l => l.Etat).Distinct().Order().ToArray();
    Console.WriteLine($"NOTE : {code} : etats couverts par le banc : {string.Join(", ", couverts)}");
}

await app.StopAsync();
Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "EcransGeneresBaseCheck : VERT" : $"EcransGeneresBaseCheck : ROUGE ({echecs.Count} echec(s))");
return echecs.Count == 0 ? 0 : 1;

/// <summary>Un serveur factice : la procedure <c>vide</c> est branchee et sans ligne, toute autre n'a pas de lecteur.</summary>
sealed class RepondeurVide(string vide) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken jeton)
    {
        var code = requete.RequestUri!.AbsolutePath.Split('/')[^2];
        var corps = new LignesEcran(code, code == vide, [], 0, 200);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(corps, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
    }
}
