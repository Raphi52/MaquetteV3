using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using RigV3.Base;
using RigV3.Contrats.EcransMain;
using RigV3.Contrats.Socle;
using RigV3.Serveur;
using RigV3.Serveur.Socle.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using RigV3Desktop;
using RigV3Desktop.Pont;

// Banc des ECRANS FAITS MAIN BRANCHES SUR LA BASE (plan « RIG v3 100 % fonctionnel », lot 17, 26/11/2026).
// Jusqu'au lot 17, Demandes, Arrivee du courrier, Teletransmissions, BODACC et Ouvrir un dossier montraient JeuDemo meme
// branches au serveur de dev. Ce que la compilation ne dit pas, et que ce banc verifie sur des bases jetables :
//   1. les LECTURES (Serveur/Socle/EcransMain/) sur des lignes controlees : les filtres de RIG v2 cites dans chaque
//      fichier, chaque refus avec son jumeau accepte (meme borne, autre cote), l'ordre, le plafond et le total ;
//   2. les ROUTES par le vrai serveur en memoire : agent admis dans le greffe, 401 / 404 / 503 sans repli ;
//   3. les PONTS des cinq ecrans contre ce serveur (mode « serveur », lignes de la base, aucune de JeuDemo), sans serveur
//      (mode « demo » : JeuDemo, que l'ecran marque « exemple »), sans greffe et en panne (le motif, AUCUNE ligne) ; et le
//      geste « Ouvrir un dossier » branche : refuse, rien d'ecrit ;
//   4. (lot 5) « Dépôts judiciaires » : sa lecture, sa route et son pont, sur la meme marche.
// Identifiants des jeux : a partir de 2 700 000 ((10 + 17) x 100 000, regle du plan). Il lui faut le SQL Server de
// developpement : « docker compose up -d » a la racine du depot.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
var jour = DateTime.Today;

// ============================================================ 0. LES BASES, LE GREFFE, LES AGENTS

await using var commune = await BaseJetable.CreerAsync("EcrMainCommune");
await using var greffe = await BaseJetable.CreerAsync("EcrMainGreffe");
await Migrateur.AppliquerAsync(commune.Connexion, Path.Combine(racine, "Base", "migrations"));
await Migrateur.AppliquerAsync(greffe.Connexion, Path.Combine(racine, "Base", "migrations"));
const string Greffe = "9217", GreffeMuet = "9218", GreffeAbsent = "9299", Agent = "banc.main";

async Task Executer(string connexion, string sql, params (string Nom, object? Valeur)[] p)
{
    await using var cn = new SqlConnection(connexion);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn);
    foreach (var (nom, valeur) in p) cmd.Parameters.AddWithValue(nom, valeur ?? DBNull.Value);
    await cmd.ExecuteNonQueryAsync();
}
Task Sql(string sql, params (string Nom, object? Valeur)[] p) => Executer(greffe.Connexion, sql, p);
async Task<int> Compter(string sql)
{
    await using var cn = new SqlConnection(greffe.Connexion);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn);
    return Convert.ToInt32(await cmd.ExecuteScalarAsync());
}

await Sql("""
    INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES (@g, 'MAIN', N'Ecrans faits main');
    INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANC', 'Banc EcransMainBase');
    INSERT INTO soc.Compte (IdV2, CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
    SELECT 2700001, @a, 'BANC', 'Main', 'BANCMAIN', 'M.', @g, Id, 0 FROM soc.Service;
    INSERT INTO soc.Compte (IdV2, CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
    SELECT 2700002, 'banc.dupont', 'DUPONT', 'Marie', 'MDUPONT', 'Mme', @g, Id, 0 FROM soc.Service;
    INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte WHERE CompteWindows = @a;
    """, ("@g", Greffe), ("@a", Agent));
var serveurBancs = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
var authentification = new SqlConnectionStringBuilder(serveurBancs.ConnectionString);
authentification.Remove("Data Source");   // le serveur et la base viennent du catalogue
foreach (var (code, nomBase) in new[] { (Greffe, greffe.Nom), (GreffeMuet, $"{BaseJetable.Prefixe}inexistante_{Guid.NewGuid():N}") })
    await Executer(commune.Connexion,
        "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe) " +
        "VALUES (@c, @n, @s, @v2, @v3, 0, 0, 'Romance Standard Time', 'banc', SYSDATETIME());",
        ("@c", code), ("@n", $"Greffe {code} du banc"), ("@s", serveurBancs.DataSource), ("@v2", "RIG_BANC_" + code), ("@v3", nomBase));

// Les tables V2 lues : les colonnes lues, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le
// 26/11/2026). DOSSIER_RCS, ENTREPRISE, CLIENT, UTILISATEUR et PROCESSUS_ET_FONCTIONALITE sont les fiches des migrations
// (rec.DossierRcs, rec.Entreprise, rec.Client, soc.Compte, soc.Fonction : Serveur/Recopie/FichesV3.cs, ReprisesPhase1.cs).
await Sql("""
    CREATE TABLE v2.ETAT_DEMANDE (R_ETATDEMAND_VALEUR varchar(1) NOT NULL PRIMARY KEY, R_ETATDEMAND_LIBELLE_ECRAN varchar(35) NOT NULL);
    CREATE TABLE v2.DEMANDE (DMND_ID_DMND int NOT NULL PRIMARY KEY, DMND_ID_DSRCS int NULL, DMND_ID_UTLST int NULL, DMND_ID_ARRV int NULL,
        DMND_ID_DMND_MERE int NULL, DMND_NUM_DEMANDE varchar(11) NOT NULL, DMND_NOM varchar(255) NULL, DMND_NUM_INDICATIF varchar(14) NULL,
        DMND_ETAT_DEMANDE varchar(1) NOT NULL, DMND_EN_COURS bit NOT NULL, DMND_CODE_PROSS varchar(40) NULL, DMND_LIBELLE_ECRAN varchar(35) NULL,
        DMND_DATE_CREATION datetime NULL, DMND_DATE_VALIDATION datetime NULL);
    CREATE TABLE v2.EDI_DOSSIER (EDIDO_ID_EDIDO int NOT NULL PRIMARY KEY, EDIDO_ID_DMND int NULL);
    CREATE TABLE v2.FACTURE (FACTU_NUMERO_FACTURE varchar(25) NOT NULL PRIMARY KEY, FACTU_NUM_DEMANDE varchar(32) NULL, FACTU_ID_CLIEN int NULL,
        FACTU_DATE_CREATION datetime NULL);
    CREATE TABLE v2.MODE_ARRIVEE (R_MODEARRIVE_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_MODEARRIVE_LIBELLE_ECRAN varchar(35) NOT NULL);
    CREATE TABLE v2.ARRIVEE (ARRV_ID_ARRV int NOT NULL PRIMARY KEY, ARRV_NUM_ARRIVEE varchar(11) NULL, ARRV_MODE_ARRIVEE varchar(5) NULL,
        ARRV_DATEHEURE datetime NULL, ARRV_NOM varchar(255) NULL, ARRV_REFERENCES varchar(35) NULL, ARRV_PAIEMENT_JOINT bit NULL,
        ARRV_CASE_COURRIER varchar(10) NULL);
    CREATE TABLE v2.DEMAT_LIASSE_CFE (DMTLCF_ID_DMTLCF int NOT NULL PRIMARY KEY, DMTLCF_ID_DMND int NULL, DMTLCF_ARCHIVAGE_ETAT varchar(2) NULL);
    CREATE TABLE v2.DEMAT_DAS (DMTDAS_ID_DMTDAS int NOT NULL PRIMARY KEY, DMTDAS_ID_DMND int NULL, DMTDAS_ARCHIVAGE_ETAT varchar(2) NULL);
    CREATE TABLE v2.DEMANDE_EDITION (DMNED_ID_DMNED int NOT NULL PRIMARY KEY, DMNED_VALEUR_ID_SOURCE varchar(40) NULL, DMNED_CODE_MAQUETTE varchar(40) NULL);
    CREATE TABLE v2.PROCEDURE_COLLECTIVE (PRCLL_ID_PRCLL int NOT NULL PRIMARY KEY, PRCLL_ID_DSRCS int NULL, PRCLL_NUM_PC varchar(10) NULL);
    CREATE TABLE v2.PREVENTION (PREVE_ID_PREVE int NOT NULL PRIMARY KEY, PREVE_ID_DSRCS int NULL, PREVE_NUM_PRV varchar(10) NOT NULL);
    CREATE TABLE v2.ETABLISSEMENT_PC (ETBPC_ID_ETBPC int NOT NULL PRIMARY KEY, ETBPC_ID_PRCLL int NULL, ETBPC_ID_PREVE int NULL, ETBPC_NUM_ORDRE smallint NULL);
    CREATE TABLE v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR int NOT NULL PRIMARY KEY, EVNPR_ID_PRCLL int NULL, EVNPR_ID_PREVE int NULL,
        EVNPR_CODE_EVE_PRO varchar(10) NOT NULL, EVNPR_DATE datetime NULL);
    CREATE TABLE v2.BORDEREAU (BRDRC_ID_BRDRC int NOT NULL PRIMARY KEY, BRDRC_DATE_TRANSMISSION varchar(24) NULL);
    CREATE TABLE v2.BODACC (BDCCR_ID_BDCRC int NOT NULL PRIMARY KEY, BDCCR_ID_EVNPR int NULL, BDCCR_ID_BRDRC int NULL, BDCCR_ETAT_BODACC varchar(2) NULL,
        BDCCR_NUMERO_ANNONCE int NULL, BDCCR_TYPE_ANNONCE varchar(1) NULL, BDCCR_DATE_ENVOI datetime NULL, BDCCR_NUM_BORDEREAU varchar(10) NULL,
        BDCCR_NUM_PARUTION varchar(8) NULL, BDCCR_DATE_PUBLICITE datetime NULL, BDCCR_NUM_FACTURE varchar(9) NULL, BDCCR_TEXTE_BODACC varchar(max) NULL);
    CREATE TABLE v2.FORME_JURIDIQUE_UTI (R_FORMEJURID_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_FORMEJURID_LIBELLE_ECRAN varchar(35) NOT NULL,
        R_FORMEJURID_ABREGE_FRANCAIS varchar(16) NULL);
    """);

// Deux dossiers et leurs entreprises ; deux clients ; les traitements (fiche soc.Fonction : une procedure DLL, EXE ou
// PROCVB6 est un traitement ; une fonction FONC / FONCVB6 non, OPE_SEARCH_BLOC_2.cs:47-55) ; les etats de RIG v2.
async Task Dossier(int id, string nom, string siren, string gestion, DateTime immat, string forme)
    => await Sql("""
        SET IDENTITY_INSERT rec.Entreprise ON;
        INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, FormeJuridique, EtatInfo, FicheSommaire) VALUES (@id, @siren, '', @nom, @forme, 'C', 0);
        SET IDENTITY_INSERT rec.Entreprise OFF;
        SET IDENTITY_INSERT rec.DossierRcs ON;
        INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
            ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
            SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise)
        VALUES (@id, @id, 'B1', @gestion, @siren, @immat, 'C', 0, 0, @immat, 'A', 'C', 0, 0, 0, 0, 0, 0, 0);
        SET IDENTITY_INSERT rec.DossierRcs OFF;
        """, ("@id", id), ("@nom", nom), ("@siren", siren), ("@gestion", gestion), ("@immat", immat), ("@forme", forme));
await Dossier(2700001, "ALPHA BASE SARL", "412700001", "2019B27001", new DateTime(2019, 3, 12), "RL");
await Dossier(2700002, "BETA BASE SAS", "412700002", "2021B27002", new DateTime(2021, 6, 1), "ZZ");
await Sql("""
    SET IDENTITY_INSERT rec.Client ON;
    INSERT INTO rec.Client (Id, Abrege, NumeroCompte, Nom, Prenom, Type, Civilite, EstComptePc, EstCompteWeb)
    VALUES (2700001, 'CALPHA', '411270001', 'CABINET ALPHA', 'Jean', 'C', 'M.', 0, 0),
           (2700002, 'BCONS', '411270002', 'BETA CONSEIL', NULL, 'C', NULL, 0, 0);
    SET IDENTITY_INSERT rec.Client OFF;
    INSERT INTO soc.Fonction (Code, Libelle, TypeV2) VALUES ('DCA', 'Depot des comptes annuels', 'DLL'), ('IMMAT', 'Immatriculation', 'EXE'),
        ('ANCIEN', 'Procedure VB6', 'PROCVB6'), ('XFONC', 'Une fonction', 'FONC'), ('VBFONC', 'Une fonction VB6', 'FONCVB6');
    INSERT INTO v2.ETAT_DEMANDE VALUES ('Q', 'Qualifiée'), ('A', 'Arrivée'), ('I', 'Interrompue'), ('V', 'Validée'), ('N', 'Réclamation');
    INSERT INTO v2.FORME_JURIDIQUE_UTI VALUES ('RL', 'Société à responsabilité limitée', 'SARL');
    """);

// ============================================================ 1a. DEMANDES (LectureDemandes.cs)

async Task Demande(int id, string nom, string? indicatif, string etat, bool enCours, string? code, int? dossier, int? agent,
                   DateTime? creee, DateTime? validee = null, int? arrivee = null, int? mere = null, string? libelle = null)
    => await Sql("INSERT INTO v2.DEMANDE VALUES (@id, @d, @u, @ar, @m, @n, @nom, @ind, @e, @ec, @c, @lib, @cr, @va);",
        ("@id", id), ("@d", dossier), ("@u", agent), ("@ar", arrivee), ("@m", mere), ("@n", id.ToString()), ("@nom", nom), ("@ind", indicatif),
        ("@e", etat), ("@ec", enCours), ("@c", code), ("@lib", libelle), ("@cr", creee), ("@va", validee));
await Demande(2700010, "Depot des comptes 2025 - ALPHA BASE", "2019B27001", "V", false, "DCA", 2700001, 2700002, jour.AddDays(-10).AddHours(9), jour.AddDays(-2));
await Demande(2700011, "Immatriculation - BETA BASE", "2021B27002", "Q", true, "IMMAT", 2700002, null, jour.AddDays(-1).AddHours(23));
await Demande(2700012, "Modification - GAMMA BASE", null, "A", false, "IMMAT", null, null, jour.AddDays(-3).AddHours(14));
await Demande(2700013, "Pieces manquantes - DELTA BASE", null, "Y", false, "DCA", null, null, jour.AddDays(-4));   // « En attente pieces »
await Demande(2700014, "Reclamation - EPSILON BASE", null, "N", false, "DCA", null, null, jour.AddDays(-5));
await Sql("""
    INSERT INTO v2.FACTURE VALUES ('F270001', '2700010', 2700001, @j9), ('F270002', '2700010', 2700002, @j2), ('F270003', '2700011', 2700001, @j1);
    INSERT INTO v2.EDI_DOSSIER VALUES (2700001, 2700011);
    """, ("@j9", jour.AddDays(-9)), ("@j2", jour.AddDays(-2)), ("@j1", jour.AddDays(-1)));

async Task<DemandesLues> Demandes(string? texte = null, string? client = null, string? processus = null, DateTime? du = null,
                                  DateTime? au = null, string[]? etats = null, bool enCours = false, int plafond = 50)
{
    await using var cn = new SqlConnection(greffe.Connexion);
    await cn.OpenAsync();
    return await LectureDemandes.LireAsync(cn, new RechercheDemandes(texte, client, processus,
        du is { } d ? DateOnly.FromDateTime(d) : null, au is { } a ? DateOnly.FromDateTime(a) : null, etats ?? [], enCours, plafond), CancellationToken.None);
}
string Numeros(IEnumerable<DemandeLue> l) => string.Join(",", l.Select(d => d.Numero));

var toutes = await Demandes();
Exiger(Numeros(toutes.Lignes) == "2700014,2700012,2700011,2700010" && toutes.Total == 4,
    $"demandes : la plus recente en tete (DMND_ID_DMND decroissant), « En attente pieces » (Y) jamais montree ({Numeros(toutes.Lignes)}, total {toutes.Total})");
var d10 = toutes.Lignes.Single(d => d.Numero == "2700010");
Exiger(d10 is { Siren: "412700001", Dossier: "2019B27001", Etat: "Validée", TraitementLibelle: "Depot des comptes annuels", Utilisateur: "DUPONT Marie",
                Client: "BETA CONSEIL (BCONS)", EnCours: false } && d10.Creation == jour.AddDays(-10).AddHours(9) && d10.Validation == jour.AddDays(-2),
    $"demande : Siren du dossier, libelle de l'etat et du traitement, agent « nom prenom », client de la DERNIERE facture « nom (abrege) » ({d10})");
var d11 = toutes.Lignes.Single(d => d.Numero == "2700011");
Exiger(d11 is { Client: "M. CABINET ALPHA Jean (CALPHA)", Utilisateur: "", EnCours: true }, $"client « civilite nom prenom (abrege) » (MetierProcDemande.cs:93-100), sans agent : vide ({d11.Client})");
Exiger(toutes.Lignes.Single(d => d.Numero == "2700012") is { Client: "", Siren: "", Dossier: "" }, "une demande sans facture ni dossier : client, Siren et dossier vides");
// RIG v2 lit ETAT_DEMANDE SANS tri (OPE_ETATS_DMD.cs:63) : l'ordre de la table, que SQL ne garantit pas. Le banc controle
// l'ensemble, et « Traitement EDI » apres (:70).
Exiger(toutes.Etats[..^1].Order().SequenceEqual(new[] { "Qualifiée", "Arrivée", "Interrompue", "Validée", "Réclamation" }.Order())
       && toutes.Etats[^1] == LectureDemandes.EtatTraitementEdi,
    $"etats proposes : ETAT_DEMANDE, puis « Traitement EDI » (OPE_ETATS_DMD.cs:63-70) ({string.Join(", ", toutes.Etats)})");
Exiger(toutes.Traitements.SequenceEqual(["ANCIEN", "DCA", "IMMAT"]) && toutes.LibellesTraitements.SequenceEqual(["Procedure VB6", "Depot des comptes annuels", "Immatriculation"]),
    $"traitements proposes : procedures DLL / EXE / PROCVB6, pas les fonctions FONC / FONCVB6 ni les fonctions neuves V3 ({string.Join(", ", toutes.Traitements)})");

Exiger(Numeros((await Demandes(processus: "DCA")).Lignes) == "2700014,2700010" && Numeros((await Demandes(processus: "IMMAT")).Lignes) == "2700012,2700011",
    "processus : DMND_CODE_PROSS egal (refus : l'autre code ; jumeau : le sien)");
Exiger(Numeros((await Demandes(etats: ["Validée"])).Lignes) == "2700010" && Numeros((await Demandes(etats: ["Qualifiée", "Réclamation"])).Lignes) == "2700014,2700011",
    "etats : les libelles coches, traduits en DMND_ETAT_DEMANDE IN (...)");
Exiger(Numeros((await Demandes(etats: [LectureDemandes.EtatTraitementEdi])).Lignes) == "2700011", "« Traitement EDI » : la demande qui a une ligne EDI_DOSSIER");
Exiger((await Demandes(etats: ["Inconnu"])).Lignes.Length == 0, "un libelle d'etat inconnu ne rend rien (jamais ignore en silence)");
Exiger(Numeros((await Demandes(enCours: true)).Lignes) == "2700011", "en cours uniquement : DMND_EN_COURS = 1");
Exiger(Numeros((await Demandes(du: jour.AddDays(-3))).Lignes) == "2700012",
    "« Du » seul = CE jour-la (MetierProcDemande.cs:671-679) : la demande de J-3 a 14 h, pas celle de J-1");
Exiger(Numeros((await Demandes(du: jour.AddDays(-3), au: jour.AddDays(-1))).Lignes) == "2700012,2700011",
    "« Du » et « Au » : la periode, la borne « Au » comprise jusqu'a 23 h (J-1 a 23 h)");
Exiger(Numeros((await Demandes(au: jour.AddDays(-5))).Lignes) == "2700014,2700010", "« Au » seul : jusqu'a ce jour compris");
Exiger(Numeros((await Demandes(client: "411270001")).Lignes) == "2700011,2700010" && (await Demandes(client: "411270009")).Lignes.Length == 0,
    "client en chiffres : le numero de compte de TOUTE facture de la demande (refus : un autre compte)");
Exiger(Numeros((await Demandes(client: "bco")).Lignes) == "2700010" && Numeros((await Demandes(client: "beta")).Lignes) == "2700010"
       && (await Demandes(client: "conseil")).Lignes.Length == 0,
    "client en lettres : le DEBUT du code abrege ou du nom (LIKE 'x%', :714-726) ; « conseil », au milieu du nom, ne trouve rien");
Exiger(Numeros((await Demandes(texte: "2700011")).Lignes) == "2700011" && Numeros((await Demandes(texte: "2019B27001")).Lignes) == "2700010"
       && Numeros((await Demandes(texte: "412 700 002")).Lignes) == "2700011" && Numeros((await Demandes(texte: "gamma")).Lignes) == "2700012"
       && (await Demandes(texte: "zzz")).Lignes.Length == 0,
    "demande : numero, numero de dossier, SIREN (espaces admis) ou mot du nom ; un texte absent ne trouve rien");
Exiger(Numeros((await Demandes(texte: "100%")).Lignes) == "" && Numeros((await Demandes(texte: "_")).Lignes) == "",
    "les caracteres speciaux du LIKE sont cherches tels quels (aucun nom ne contient « 100% » ni « _ »)");
var deux = await Demandes(plafond: 2);
Exiger(Numeros(deux.Lignes) == "2700014,2700012" && deux.Total == 4, $"plafond : les 2 premieres, le total reste 4 ({deux.Total})");

// ============================================================ 1b. ARRIVEES (LectureArrivees.cs)

await Sql("""
    INSERT INTO v2.MODE_ARRIVEE VALUES ('PTT', 'Poste'), ('EDI', 'Edi'), ('ACC', 'Accueil');
    INSERT INTO v2.ARRIVEE VALUES (2700101, 'A27000101', 'PTT', @j1, 'CABINET ALPHA', 'REF-ALPHA', 1, 'A'),
                                  (2700102, 'A27000102', 'EDI', @j2, 'INFOGREFFE', 'LIASSE-27', 0, ''),
                                  (2700103, 'A27000103', 'ACC', @j5, 'GUICHET BETA', NULL, 0, 'B');
    INSERT INTO v2.DEMAT_LIASSE_CFE VALUES (2700001, 2700111, '1');      -- en attente d'archivage : reste a numeriser
    INSERT INTO v2.DEMAT_DAS VALUES (2700001, 2700120, '2');             -- archive en GED : numerise
    INSERT INTO v2.DEMANDE_EDITION VALUES (2700001, '2700111', 'LOT2_ATTES_PROV'), (2700002, '2700120', 'LOT2_ARRIVEE_1_IMP');
    """, ("@j1", jour.AddDays(-1).AddHours(10)), ("@j2", jour.AddDays(-2).AddHours(9)), ("@j5", jour.AddDays(-5).AddHours(16)));
// L'arrivee postale : sa demande d'arrivee (la mere) et la demande saisie (la fille) ; l'arrivee EDI : sa seule demande.
await Demande(2700110, "Arrivee", null, "V", false, "ARRIVEE", null, null, jour.AddDays(-1), arrivee: 2700101);
await Demande(2700111, "Depot d'actes - ALPHA BASE", "2019B27001", "A", false, "DACT", 2700001, 2700002, jour.AddDays(-1), arrivee: 2700101, mere: 2700110, libelle: "Depot d'actes");
await Demande(2700120, "Modification - BETA BASE", "2021B27002", "Q", false, "MODIF", 2700002, null, jour.AddDays(-2), arrivee: 2700102, libelle: "Modification");

async Task<ArriveesLues> Arrivees(string? numero = null, string? nom = null, string? references = null, string? mode = null, string? caseCourrier = null,
                                  DateTime? du = null, DateTime? au = null, bool paiement = false, bool nonNumerise = false, int plafond = 50)
{
    await using var cn = new SqlConnection(greffe.Connexion);
    await cn.OpenAsync();
    return await LectureArrivees.LireAsync(cn, new RechercheArrivees(numero, nom, references, mode, caseCourrier,
        du is { } d ? DateOnly.FromDateTime(d) : null, au is { } a ? DateOnly.FromDateTime(a) : null, paiement, nonNumerise, plafond), CancellationToken.None);
}
string Arr(IEnumerable<ArriveeLue> l) => string.Join(",", l.Select(a => a.Numero));

var courrier = await Arrivees();
Exiger(Arr(courrier.Lignes) == "A27000101,A27000102,A27000103" && courrier.Total == 3 && courrier.ANumeriser == 1,
    $"arrivees : la plus recente en tete, total 3, une seule a numeriser ({Arr(courrier.Lignes)}, {courrier.ANumeriser})");
Exiger(courrier.Modes.Select(m => m.Code).SequenceEqual(["ACC", "EDI", "PTT"]) && courrier.Modes.First().Libelle == "Accueil",
    "les modes d'arrivee : MODE_ARRIVEE, par libelle");
var a101 = courrier.Lignes[0];
Exiger(a101 is { ModeLibelle: "Poste", NumDemande: "2700111", TypeDemande: "Depot d'actes", Utilisateur: "DUPONT Marie", Numerise: false, Attestation: true,
                 PaiementJoint: true, Case: "A", References: "REF-ALPHA" },
    $"arrivee postale : la demande SAISIE (la fille, pas la mere), son type et l'agent designe ; ligne demat en attente = a numeriser ; attestation LOT2_ATTES_PROV ({a101})");
Exiger(courrier.Lignes[1] is { NumDemande: "2700120", Numerise: true, Attestation: false, ModeLibelle: "Edi" },
    "arrivee EDI : sa demande ; demat archivee = numerisee ; un bordereau de numerisation n'est pas une attestation");
Exiger(courrier.Lignes[2] is { NumDemande: "", TypeDemande: "", Numerise: true, Attestation: false, References: "" }, "une arrivee sans demande : rien a numeriser, aucune demande");
Exiger(Arr((await Arrivees(mode: "EDI")).Lignes) == "A27000102" && Arr((await Arrivees(mode: "PTT")).Lignes) == "A27000101", "mode : ARRV_MODE_ARRIVEE egal");
Exiger(Arr((await Arrivees(nonNumerise: true)).Lignes) == "A27000101" && Arr((await Arrivees(paiement: true)).Lignes) == "A27000101",
    "reste a numeriser / paiement joint : la seule arrivee concernee");
Exiger(Arr((await Arrivees(numero: "A27000103")).Lignes) == "A27000103" && Arr((await Arrivees(numero: "2700120")).Lignes) == "A27000102",
    "numero : celui de l'arrivee, ou celui d'une de ses demandes");
Exiger(Arr((await Arrivees(nom: "beta")).Lignes) == "A27000103" && Arr((await Arrivees(references: "liasse")).Lignes) == "A27000102"
       && Arr((await Arrivees(caseCourrier: "B")).Lignes) == "A27000103" && (await Arrivees(nom: "gamma")).Lignes.Length == 0,
    "expediteur, references, case : contient (refus : un nom absent)");
Exiger(Arr((await Arrivees(du: jour.AddDays(-2))).Lignes) == "A27000101,A27000102" && Arr((await Arrivees(au: jour.AddDays(-2))).Lignes) == "A27000102,A27000103",
    "date d'arrivee du..au : jours entiers, bornes comprises (J-2 a 9 h est dans les deux)");

// ============================================================ 1c. BODACC (LectureBodacc.cs)

var debutAnnee = new DateTime(jour.Year, 1, 1);
await Sql("""
    INSERT INTO v2.PROCEDURE_COLLECTIVE VALUES (2700201, 2700001, 'P2700201'), (2700202, 2700001, 'P2700202'), (2700203, 2700001, NULL);
    INSERT INTO v2.ETABLISSEMENT_PC VALUES (2700001, 2700201, NULL, 1), (2700002, 2700202, NULL, 1), (2700003, 2700202, NULL, 2);
    INSERT INTO v2.PREVENTION VALUES (2700301, 2700002, 'V2700301');
    INSERT INTO v2.EVENEMENT_PROCEDURE VALUES (2700401, 2700201, NULL, 'OJRJ', @j20), (2700402, 2700202, NULL, 'JUG', @j15),
        (2700403, 2700203, NULL, 'XX', @j10), (2700404, NULL, 2700301, 'PRV', @j12), (2700405, 2700201, NULL, 'CLO', @j8);
    INSERT INTO v2.BORDEREAU VALUES (2700501, @transmis);
    INSERT INTO v2.BODACC VALUES
        (2700601, 2700401, NULL, '1', 2700601, 'C', @j5, NULL, NULL, NULL, NULL, 'Ouverture RJ ALPHA'),
        (2700602, 2700402, 2700501, '1', 2700602, 'M', @j30, 'B2706', 'P26-1', @j25, 'F2706', 'Rectificatif ALPHA'),
        (2700603, 2700403, NULL, '1', 2700603, 'C', @j5, NULL, NULL, NULL, NULL, 'PC sans numero'),
        (2700604, 2700404, NULL, '1', 2700604, 'S', @j3, NULL, NULL, NULL, NULL, 'Annulation prevention BETA'),
        (2700605, 2700405, NULL, '9', 2700605, 'C', @j4, NULL, NULL, NULL, NULL, 'Annonce supprimee'),
        (2700606, 2700401, NULL, '1', 2700606, 'C', @j70, NULL, NULL, NULL, NULL, 'Annonce ancienne'),
        (2700607, 2700401, NULL, '1', 2700607, 'C', @debut, NULL, NULL, NULL, NULL, 'Annonce du premier janvier'),
        (2700608, 2700401, NULL, '1', 2700608, 'C', @veille, NULL, NULL, NULL, NULL, 'Annonce de l annee passee');
    """, ("@j20", jour.AddDays(-20)), ("@j15", jour.AddDays(-15)), ("@j10", jour.AddDays(-10)), ("@j12", jour.AddDays(-12)), ("@j8", jour.AddDays(-8)),
    ("@transmis", $"{jour.AddDays(-29):dd/MM/yyyy} 14:57:51"), ("@j5", jour.AddDays(-5)), ("@j30", jour.AddDays(-30)), ("@j25", jour.AddDays(-25)),
    ("@j3", jour.AddDays(-3)), ("@j4", jour.AddDays(-4)), ("@j70", jour.AddDays(-70)), ("@debut", debutAnnee), ("@veille", debutAnnee.AddHours(-1)));

async Task<AnnoncesBodaccLues> Bodacc()
{
    await using var cn = new SqlConnection(greffe.Connexion);
    await cn.OpenAsync();
    return await LectureBodacc.LireAsync(cn, jour.AddHours(15), CancellationToken.None);
}
string Ann(IEnumerable<AnnonceBodaccLue> l) => string.Join(",", l.Select(a => a.NumAnnonce));

await Sql("INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('BODACC_JUD_DATE', 'Debut BODACC judiciaire', @v);", ("@v", jour.AddDays(-60).ToString("dd/MM/yyyy")));
var bodacc = await Bodacc();
Exiger(Ann(bodacc.Lignes) == "2700602,2700601,2700604" && bodacc.Du == jour.AddDays(-60) && bodacc.Au == jour,
    $"BODACC, periode du reglage BODACC_JUD_DATE (J-60) a aujourd'hui : ordre de la date d'envoi ; sans l'annonce supprimee (etat 9), " +
    $"ni la PC sans numero, ni le second etablissement, ni l'annonce de J-70 ({Ann(bodacc.Lignes)})");
var envoyee = bodacc.Lignes[0];
Exiger(envoyee is { NumPc: "P2700202", TypeAnnonce: "Rectification", CodeEvePro: "JUG", NumBord: "B2706", NumFact: "F2706", NumParution: "P26-1", Dossier: "2019B27001" }
       && envoyee.Envoi == jour.AddDays(-30) && envoyee.PrevueEnvoi == jour.AddDays(-30) && envoyee.Parution == jour.AddDays(-25)
       && envoyee.Transmission == jour.AddDays(-29) && envoyee.DateEvePro == jour.AddDays(-15),
    $"annonce DEJA ENVOYEE (un bordereau) : PC, type M = Rectification, parution, facture, transmission du bordereau (texte jj/mm/aaaa), dossier de la PC ({envoyee})");
Exiger(bodacc.Lignes[1] is { Envoi: null, TypeAnnonce: "Création", NumPc: "P2700201", NumBord: "" } && bodacc.Lignes[1].PrevueEnvoi == jour.AddDays(-5),
    "annonce A ENVOYER (sans bordereau) : pas de date d'envoi, sa date prevue ; type C = Création");
Exiger(bodacc.Lignes[2] is { NumPc: "V2700301", TypeAnnonce: "Annulation", Dossier: "2021B27002", Envoi: null },
    "une prevention (sans etablissement) : son numero, son dossier ; type S = Annulation");
await Sql("DELETE FROM soc.Reglage WHERE Code = 'BODACC_JUD_DATE'; INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('BODACC_JUD_DATE', 'Debut BODACC judiciaire', @v);",
    ("@v", jour.AddDays(-70).ToString("dd/MM/yyyy")));
Exiger(Ann((await Bodacc()).Lignes) == "2700606,2700602,2700601,2700604", "jumeau : le reglage a J-70 fait entrer l'annonce de J-70");
await Sql("DELETE FROM soc.Reglage WHERE Code = 'BODACC_JUD_DATE';");
var parDefaut = await Bodacc();
var attendues = new (string Id, DateTime Envoi)[] { ("2700606", jour.AddDays(-70)), ("2700602", jour.AddDays(-30)), ("2700601", jour.AddDays(-5)),
                                                    ("2700604", jour.AddDays(-3)), ("2700607", debutAnnee), ("2700608", debutAnnee.AddHours(-1)) }
    .Where(a => a.Envoi >= debutAnnee).OrderBy(a => a.Envoi).Select(a => a.Id);
Exiger(Ann(parDefaut.Lignes) == string.Join(",", attendues) && parDefaut.Du == debutAnnee,
    $"sans reglage : du 1er janvier (OPE_RECH_BOD.cs:65) -- l'annonce du 1er janvier a 0 h est dedans, celle de la veille a 23 h dehors ({Ann(parDefaut.Lignes)})");

// ============================================================ 1d. DOSSIERS OUVERTS (LectureDossiersOuverts.cs)

await using (var cn = new SqlConnection(greffe.Connexion))
{
    await cn.OpenAsync();
    var dossiers = await LectureDossiersOuverts.LireAsync(cn, CancellationToken.None);
    Exiger(dossiers.Total == 2 && dossiers.Lignes.Select(d => d.Numero).SequenceEqual(["2021B27002", "2019B27001"])
           && dossiers.Lignes[1] is { Denomination: "ALPHA BASE SARL", Forme: "SARL", Siren: "412700001" } && dossiers.Lignes[1].Immatriculation == new DateTime(2019, 3, 12)
           && dossiers.Lignes[0].Forme == "ZZ",
        "dossiers : les derniers immatricules en tete ; la forme par son abrege (FORME_JURIDIQUE_UTI), a defaut son code");
}

// ============================================================ 2. LES ROUTES, PAR LE VRAI SERVEUR EN MEMOIRE

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
var tout = new RechercheDemandes(null, null, null, null, null, [], false, 50);
// Les demandes de la base a ce moment : les 4 de la section 1a et les 3 des arrivees (1b).
var maintenant = await Demandes();
Exiger(maintenant.Total == 7 && Numeros(maintenant.Lignes) == "2700120,2700111,2700110,2700014,2700012,2700011,2700010",
    $"les demandes des arrivees sont des demandes comme les autres ({Numeros(maintenant.Lignes)})");

var parRoute = await (await http.PostAsJsonAsync(RoutesEcransMain.Pour(RoutesEcransMain.Demandes, Greffe), tout, json)).Content.ReadFromJsonAsync<DemandesLues>(json);
Exiger(parRoute is { Total: 7 } && Numeros(parRoute.Lignes) == Numeros(maintenant.Lignes), $"route des demandes : les lignes de la lecture ({parRoute?.Total})");
var anonyme = app.GetTestClient();
Exiger((await anonyme.PostAsJsonAsync(RoutesEcransMain.Pour(RoutesEcransMain.Demandes, Greffe), tout, json)).StatusCode == HttpStatusCode.Unauthorized,
    "route : sans session -> 401");
var absent = await http.PostAsJsonAsync(RoutesEcransMain.Pour(RoutesEcransMain.Demandes, GreffeAbsent), tout, json);
var muet = await http.GetAsync(RoutesEcransMain.Pour(RoutesEcransMain.Bodacc, GreffeMuet));
Exiger(absent.StatusCode == HttpStatusCode.NotFound && muet.StatusCode == HttpStatusCode.ServiceUnavailable,
    $"route : greffe absent du catalogue -> 404 ({(int)absent.StatusCode}), base muette -> 503 ({(int)muet.StatusCode}), sans repli");
Exiger(ServeurRig.Decouvrir(typeof(ServeurRig).Assembly).Any(m => m.GetType().Name == "ModuleEcransMain"),
    "le module des ecrans faits main est trouve par reflexion, sans toucher Serveur/ServeurRig.cs");

// ============================================================ 3. LES PONTS DES CINQ ECRANS

Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, Agent);
ClientServeur client;
try { client = new ClientServeur(new Uri("http://localhost/"), app.GetTestServer().CreateHandler()); }
finally { Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null); }
var nomsDemo = JeuDemo.Demandes.Select(d => d.Numero).ToHashSet();
var critDemandes = new CriteresDemandes("", "", "", null, null, [], false, false, 50);

var demBase = await EcranDemandes.TrouverAsync(client, Greffe, critDemandes, []);
Exiger(demBase is { Mode: "serveur", Message: null, Total: 7 } && demBase.Lignes.Select(l => l.Numero).SequenceEqual(maintenant.Lignes.Select(d => d.Numero))
       && !demBase.Lignes.Any(l => nomsDemo.Contains(l.Numero)) && demBase.Lignes.All(l => !l.Alerte)
       && demBase.Lignes.Single(l => l.Numero == "2700010") is { Creation: var c, Validation: var v, TraitementLibelle: "Depot des comptes annuels" }
       && c == jour.AddDays(-10).ToString("dd/MM/yyyy") && v == jour.AddDays(-2).ToString("dd/MM/yyyy"),
    "pont Demandes branche : les demandes de la base (aucune de JeuDemo), dates jj/mm/aaaa, aucune en alerte");
Exiger((await EcranDemandes.TrouverAsync(client, Greffe, critDemandes with { CritAlerte = true }, [])).Lignes.Length == 0,
    "pont Demandes : « Alerte uniquement » ne rend rien en base (RIG v2 n'a pas d'alerte par demande), pas toute la base");
var demDemo = await EcranDemandes.TrouverAsync(null, null, critDemandes, []);
Exiger(demDemo.Mode == "demo" && demDemo.Lignes.Length > 0 && demDemo.Lignes.All(l => nomsDemo.Contains(l.Numero)),
    "pont Demandes sans serveur : la demonstration JeuDemo, en mode « demo » (l'ecran la marque « exemple »)");
var demSansGreffe = await EcranDemandes.TrouverAsync(client, null, critDemandes, []);
var demPanne = await EcranDemandes.TrouverAsync(client, GreffeAbsent, critDemandes, []);
Exiger(demSansGreffe is { Mode: "sans-greffe", Lignes.Length: 0 } && demPanne is { Mode: "erreur", Lignes.Length: 0 } && demPanne.Message?.Contains(GreffeAbsent) == true,
    $"pont Demandes sans greffe / greffe absent : le motif, AUCUNE ligne (jamais JeuDemo a la place) ({demPanne.Message})");

var critArrivee = new CriteresArrivee("", "", "", "", "", null, null, false, false, 50);
var arrBase = await EcranArrivee.TrouverAsync(client, Greffe, critArrivee);
Exiger(arrBase is { Mode: "serveur", Total: 3, ANumeriser: 1 } && arrBase.Lignes.Select(l => l.Numero).SequenceEqual(["A27000101", "A27000102", "A27000103"])
       && arrBase.Lignes[0].Horodatage == jour.AddDays(-1).AddHours(10).ToString("dd/MM/yyyy HH:mm") && arrBase.Lignes[0].ModeLisible == "Poste"
       && arrBase.Modes.Any(m => m is { Code: "EDI", Libelle: "Edi" }),
    "pont Arrivee branche : le courrier de la base, horodatage jj/mm/aaaa hh:mm, modes de MODE_ARRIVEE");
var arrDemo = await EcranArrivee.TrouverAsync(null, null, critArrivee);
Exiger(arrDemo.Mode == "demo" && arrDemo.Lignes.Length > 0 && arrDemo.Lignes.All(l => JeuDemo.Arrivees.Any(a => a.Numero == l.Numero)),
    "pont Arrivee sans serveur : JeuDemo.Arrivees, mode « demo »");

var fluxBase = await EcranTeletransmission.FluxAsync(client, Greffe, "EDI");
var tousFlux = await EcranTeletransmission.FluxAsync(client, Greffe, "TOUS");
Exiger(fluxBase is { Mode: "serveur" } && fluxBase.Flux.Select(f => f.Numero).SequenceEqual(["A27000102"])
       && fluxBase.Dernier == $"dernier flux : {jour.AddDays(-2).AddHours(9):dd/MM/yyyy HH:mm}" && tousFlux.Flux.Length == 3,
    "pont Teletransmissions branche : les arrivees EDI de la base (TOUS : les trois), et le dernier flux");
Exiger((await EcranTeletransmission.FluxAsync(null, null, "EDI")) is { Mode: "demo", Flux.Length: > 0 }, "pont Teletransmissions sans serveur : JeuDemo, mode « demo »");
Exiger((await EcranTeletransmission.FluxAsync(client, GreffeAbsent, "EDI")) is { Mode: "erreur", Flux.Length: 0, Dernier: EcranTeletransmission.SansFlux },
    "pont Teletransmissions, greffe absent : aucun flux, le motif");

await Sql("INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('BODACC_JUD_DATE', 'Debut BODACC judiciaire', @v);", ("@v", jour.AddDays(-60).ToString("dd/MM/yyyy")));
var bodBase = await EcranBodacc.AnnoncesAsync(client, Greffe);
Exiger(bodBase is { Mode: "serveur", Total: 3 } && bodBase.Annonces.Select(a => a.NumAnnonce).SequenceEqual(["2700602", "2700601", "2700604"])
       && bodBase.Aujourdhui == jour.ToString("yyyy-MM-dd") && bodBase.Periode == $"du {jour.AddDays(-60):dd/MM/yyyy} au {jour:dd/MM/yyyy}"
       && bodBase.Annonces[0] is { Envoi: var envoi, NumBord: "B2706", Dossier: "2019B27001" } && envoi == jour.AddDays(-30).ToString("yyyy-MM-dd")
       && bodBase.Annonces[1] is { Envoi: null, Parution: null },
    "pont BODACC branche : les annonces de la base, dates aaaa-mm-jj (null = a envoyer), la vraie date du jour, la periode de RIG v2");
var bodDemo = await EcranBodacc.AnnoncesAsync(null, null);
Exiger(bodDemo is { Mode: "demo", Annonces.Length: 0, Periode: "" } && bodDemo.Aujourdhui == JeuDemo.Aujourdhui.ToString("yyyy-MM-dd"),
    "pont BODACC sans serveur : mode « demo », la date du jeu ; l'ecran garde ses annonces d'exemple");

var ouvBase = await EcranOuvertureDossier.VueAsync(client, Greffe);
Exiger(ouvBase is { Mode: "serveur", Total: 2 } && ouvBase.Ouverts.Select(o => o.Numero).SequenceEqual(["2021B27002", "2019B27001"])
       && ouvBase.Ouverts.All(o => o is { Capital: "", Monnaie: "", Origine: "" }) && ouvBase.Ouverts[1].Immatriculation == "12/03/2019",
    "pont Ouvrir un dossier branche : les dossiers de la base, sans capital, monnaie ni origine inventes");
var avant = await Compter("SELECT COUNT(*) FROM rec.DossierRcs") + await Compter("SELECT COUNT(*) FROM rec.Entreprise");
var refus = await EcranOuvertureDossier.OuvrirAsync(client, Greffe,
    new FicheOuverture("Guichet unique (INPI)", "512700099", new DateOnly(2026, 11, 26), "SOCIETE BANC", "SAS", 1000m, "EUR"));
var apres = await Compter("SELECT COUNT(*) FROM rec.DossierRcs") + await Compter("SELECT COUNT(*) FROM rec.Entreprise");
Exiger(refus is { Bloquant: true, Message: EcranOuvertureDossier.SansEcritureV2 } && refus.Ouverts.Length == 2 && avant == apres,
    $"pont Ouvrir un dossier branche : « Ouvrir » refuse et le dit, RIEN n'est ecrit ({avant} -> {apres} fiches)");
Exiger((await EcranOuvertureDossier.VueAsync(null, null)) is { Mode: "demo" }, "pont Ouvrir un dossier sans serveur : la demonstration, mode « demo »");
var ouvPanne = await EcranOuvertureDossier.OuvrirAsync(client, GreffeMuet, new FicheOuverture("", "512700099", null, "X", "SAS", 1m, "EUR"));
Exiger(ouvPanne is { Bloquant: true, Ouverts.Length: 0 } && ouvPanne.Message != EcranOuvertureDossier.SansEcritureV2,
    $"pont Ouvrir un dossier, base muette : le motif de la panne, pas le refus d'ecriture ({ouvPanne.Message})");

// ============================================================ 4. DEPOTS JUDICIAIRES (LectureDepotsJud.cs, lot 5)
// « Dépôts judiciaires » (PROC_DEPOTJUD), branche au lot 5 du plan : la serie JROP de la recherche (PROC_DEPOTJUD.Designer.cs:121),
// le dernier evenement SURVENU, « a facturer » sans client sur les factures des demandes de l'affaire (PROC_DEPOTJUD.cs:58-64),
// la demande qui tient l'instance (2J, en cours ou W/I/N). Joue en dernier : ses demandes n'entrent dans aucun controle
// precedent. Identifiants 1 530 0xx (lot 5 : 1 500 000 a 1 599 999).
await Sql("""
    ALTER TABLE v2.DEMANDE ADD DMND_ID_AFFRE int NULL;
    ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_ID_INSTN int NULL, EVNPR_SURVENU bit NULL;
    CREATE TABLE v2.DOSSIER_DEMANDE (DSSDM_ID_DSSDM int NOT NULL PRIMARY KEY, DSSDM_ID_DMND int NOT NULL,
        DSSDM_CODE_SEQUENCEUR varchar(20) NOT NULL, DSSDM_VALEUR_SEQUENCEUR varchar(100) NULL);
    CREATE TABLE v2.CODE_AFFAIRE (R_CODEAFFAIR_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_CODEAFFAIR_TYPE_NUM_ROLE varchar(5) NULL);
    CREATE TABLE v2.NATURE_AFFAIRE (R_NATUREAFFA_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_NATUREAFFA_LIBELLE_ECRAN varchar(75) NULL);
    CREATE TABLE v2.CODE_EVE_PRO (R_CODEEVEPRO_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_CODEEVEPRO_LIBELLE_ECRAN varchar(70) NULL);
    CREATE TABLE v2.INSTANCE (INSTN_ID_INSTN int NOT NULL PRIMARY KEY, INSTN_NUM_ROLE varchar(11) NULL, INSTN_NOM_COURANT varchar(250) NULL,
        INSTN_CODE_AFFAIRE varchar(10) NULL, INSTN_NATURE_AFFAIRE varchar(10) NULL, INSTN_DATE_ENROLEMENT datetime NULL, INSTN_ID_AFFRE int NULL);
    """);
// 1530001 (JROP) : une facture a un client sur une demande de son affaire, deux evenements survenus et un a venir plus recent ;
// 1530002 (JROP) : une facture SANS client (jumeau : a facturer), tenue par une demande en cours, sans nature ni evenement ;
// 1530003 (JRCT) : hors de la serie de la recherche (jumeau de 1530001, meme affaire).
await Sql("""
    INSERT INTO v2.CODE_AFFAIRE VALUES ('OP53', 'JROP'), ('CT53', 'JRCT');
    INSERT INTO v2.NATURE_AFFAIRE VALUES ('N53', 'Opposition du banc');
    INSERT INTO v2.CODE_EVE_PRO VALUES ('JUG53', 'Jugement du banc'), ('ASS53', 'Assignation du banc');
    INSERT INTO v2.INSTANCE VALUES
        (1530001, '2026J53001', 'DEPOT ALPHA # DEPOT BETA', 'OP53', 'N53', '20260203', 1530101),
        (1530002, '2026J53002', 'DEPOT GAMMA', 'OP53', NULL, '20260204', 1530102),
        (1530003, '2026J53003', 'DEPOT HORS SERIE', 'CT53', 'N53', '20260205', 1530101);
    INSERT INTO v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR, EVNPR_ID_INSTN, EVNPR_CODE_EVE_PRO, EVNPR_DATE, EVNPR_SURVENU) VALUES
        (1530011, 1530001, 'ASS53', '20260201', 1), (1530012, 1530001, 'JUG53', '20260205', 1), (1530013, 1530001, 'ASS53', '20260301', 0);
    INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_EN_COURS, DMND_CODE_PROSS, DMND_ID_AFFRE) VALUES
        (1530201, 'D1530201', 'V', 0, 'MJUD', 1530101), (1530202, 'D1530202', 'V', 0, 'MJUD', 1530102),
        (1530203, 'D1530203', 'Q', 1, 'MJUD', NULL);
    INSERT INTO v2.FACTURE (FACTU_NUMERO_FACTURE, FACTU_NUM_DEMANDE, FACTU_ID_CLIEN) VALUES ('F1530201', 'D1530201', 2700001), ('F1530202', 'D1530202', NULL);
    INSERT INTO v2.DOSSIER_DEMANDE VALUES (1530251, 1530203, '2J', '1530002');
    """);
await using (var cn = new SqlConnection(greffe.Connexion))
{
    await cn.OpenAsync();
    var depots = await LectureDepotsJud.LireAsync(cn, CancellationToken.None);
    Exiger(depots.Total == 2 && depots.Lignes.Select(d => d.Numero).SequenceEqual(["2026J53002", "2026J53001"]),
        $"depots judiciaires : la serie JROP seule (2026J53003, JRCT, ecartee), role decroissant ({string.Join(",", depots.Lignes.Select(d => d.Numero))})");
    Exiger(depots.Lignes[1] is { Denomination: "DEPOT ALPHA # DEPOT BETA", Nature: "Opposition du banc", DernierEvenement: "Jugement du banc", DemandeEnCours: "", AFacturer: false }
           && depots.Lignes[1].DateEvenement == new DateTime(2026, 2, 5) && depots.Lignes[1].Enrolement == new DateTime(2026, 2, 3),
        "depots judiciaires : le dernier evenement SURVENU (pas celui a venir), la nature par son libelle ; une facture a un client : rien a facturer");
    Exiger(depots.Lignes[0] is { Nature: "", DernierEvenement: "", DemandeEnCours: "D1530203", AFacturer: true } && depots.Lignes[0].DateEvenement is null,
        "depots judiciaires : facture sans client -> a facturer (PROC_DEPOTJUD.cs:58-64) ; la demande en cours qui la tient (2J)");
    var un = await LectureDepotsJud.LireAsync(cn, CancellationToken.None, plafond: 1);
    Exiger(un.Lignes.Length == 1 && un.Total == 2, $"depots judiciaires : plafond 1 -> une ligne, total 2 ({un.Total})");
}
var depotsRoute = await http.GetFromJsonAsync<InstancesDepotLues>(RoutesEcransMain.Pour(RoutesDepotsJud.Instances, Greffe), json);
Exiger(depotsRoute is { Total: 2 } && depotsRoute.Lignes[0].Numero == "2026J53002", "route des depots judiciaires : les lignes de la lecture");
Exiger((await http.GetAsync(RoutesEcransMain.Pour(RoutesDepotsJud.Instances, GreffeAbsent))).StatusCode == HttpStatusCode.NotFound,
    "route des depots judiciaires : greffe absent -> 404, sans repli");
var depBase = await EcranDepotsJud.VueAsync(client, Greffe);
Exiger(depBase is { Mode: "serveur", Total: 2 } && depBase.Lignes[1] is { Etat: "Libre", Statut: "ok", Ouverture: "2026-02-03", DernierEvenement: "Jugement du banc (05/02/2026)", Dossier: "2026J53001", Chambre: "", AFacturer: false }
       && depBase.Lignes[0] is { Etat: "En cours", Statut: "attente", AFacturer: true }
       && !depBase.Lignes.Any(l => EcranDepotsJud.Exemple().Lignes.Any(e => e.Numero == l.Numero)),
    "pont Depots judiciaires branche : les instances de la base (aucune de la demonstration), « En cours » si une demande la tient, le dossier = l'instance");
Exiger((await EcranDepotsJud.VueAsync(null, null)) is { Mode: "demo", Lignes.Length: 4 }, "pont Depots judiciaires sans serveur : la demonstration, mode « demo »");
Exiger((await EcranDepotsJud.VueAsync(client, GreffeAbsent)) is { Mode: "erreur", Lignes.Length: 0, Message: not null },
    "pont Depots judiciaires, greffe absent : le motif, AUCUNE ligne");

Console.WriteLine();
if (echecs.Count == 0) { Console.WriteLine("Ecrans faits main branches sur la base : VERT."); return 0; }
Console.WriteLine(echecs.Count + " echec(s) : " + string.Join(" | ", echecs));
return 1;
