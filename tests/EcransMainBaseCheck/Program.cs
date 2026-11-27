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
//   4. (lot 5) « Dépôts judiciaires » : sa lecture, sa route et son pont, sur la meme marche ;
//   5. (lot 7) « Créer une audience » et « Gestion des audiences » : leurs lectures, routes et ponts, sur la meme marche.
//   6. (lot 6) « Consultation judiciaire », « Actes », « Recherche d'affaires » et « Réquisitions », de meme.
// fix-ok: pas un correctif -- le lot 6 du plan-676a4b19 ajoute une section par ecran branche (6 a 9), une edition par ecran ;
//   chaque section est prouvee par un defaut reinjecte (plancher 2014 des actes retire : 3 echecs, 26/11/2026).
//   Cause mesuree de l'edition suivante (section 9) : le pont posait NbInscriptions = 0 et ProcedureCollective = false en dur
//   (EcranRequisitionsBase.cs:75, objection du controle final) ; la lecture les tire maintenant des regles de la V2. Et la
//   section 6 lit le XML : RPJCE_FLUX est une colonne de DEMAT_RPJC_ENR (DEMAT_RPJC_ENR.sql:15), l'ecran le disait non relu.
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

// ============================================================ 5. AUDIENCES (LectureAudiences.cs, LectureGestionAudiences.cs, lot 7)
// « Créer une audience » (PROC_AUDIENCE) : les listes de saisie (valeurs *_UTILISABLE_SAISIE, types d'AUDIENCE seuls,
// OPE_CREATION_AUDIENCE.cs:30-36) et les audiences a venir (AUDNC_AUDIENCE = 1, du jour et apres, affaires au role).
// « Gestion des audiences » (PROC_GESTAUDS) : AUDIENCE_CABINET_SUPPRIMEE, cabinets et audiences, date puis heure
// (OPE_RECHERCHE_GESTAUDS.cs:170, :285-290, :352-357). Identifiants 1 790 0xx (lot 7 : 1 700 000 a 1 799 999).
await Sql("""
    CREATE TABLE v2.TYPE_AUD_CAB (R_TYPEAUDCAB_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_TYPEAUDCAB_LIBELLE_ECRAN varchar(35) NOT NULL,
        R_TYPEAUDCAB_UTILISABLE_SAISIE bit NOT NULL, R_TYPEAUDCAB_AUDIENCE bit NOT NULL);
    CREATE TABLE v2.CHAMBRE (R_CHAMBRE_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_CHAMBRE_LIBELLE_ECRAN varchar(35) NOT NULL, R_CHAMBRE_UTILISABLE_SAISIE bit NOT NULL);
    CREATE TABLE v2.SECTION (R_SECTION_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_SECTION_LIBELLE_ECRAN varchar(35) NOT NULL, R_SECTION_UTILISABLE_SAISIE bit NOT NULL);
    CREATE TABLE v2.TYPE_CLASSEMENT (R_TYPECLASSE_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_TYPECLASSE_LIBELLE_ECRAN varchar(35) NOT NULL,
        R_TYPECLASSE_UTILISABLE_SAISIE bit NOT NULL);
    CREATE TABLE v2.CODE_CABINET (R_CODECABINE_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_CODECABINE_LIBELLE_ECRAN varchar(35) NOT NULL);
    CREATE TABLE v2.JUGE (JUG_ID_JUG int NOT NULL PRIMARY KEY, JUG_ABREGE varchar(16) NULL, JUG_CIVILITE varchar(7) NOT NULL, JUG_NOM varchar(35) NOT NULL,
        JUG_PRENOM varchar(20) NULL);
    CREATE TABLE v2.GREFFIER_ASSERMENTE (GRASS_ID_GRASS int NOT NULL PRIMARY KEY, GRASS_ABREGE varchar(16) NOT NULL, GRASS_CIVILITE varchar(7) NOT NULL,
        GRASS_NOM varchar(35) NOT NULL, GRASS_PRENOM varchar(20) NULL);
    CREATE TABLE v2.AUDIENCE_CABINET (AUDNC_ID_ADNC int NOT NULL PRIMARY KEY, AUDNC_AUDIENCE bit NOT NULL, AUDNC_TYPE_AUD_CAB varchar(5) NOT NULL,
        AUDNC_DATE datetime NULL, AUDNC_HEURE varchar(5) NULL, AUDNC_PUBLIQUE bit NOT NULL, AUDNC_CHAMBRE varchar(5) NULL, AUDNC_SECTION varchar(10) NULL,
        AUDNC_NON_DISPONIBLE_WEB bit NOT NULL);
    CREATE TABLE v2.APPEL_AFFAIRE (APPAF_ID_APPAF int NOT NULL PRIMARY KEY, APPAF_ID_ADNC int NULL);
    CREATE TABLE v2.AUDIENCE_CABINET_SUPPRIMEE (AUDNCS_ID_AUDNCS int NOT NULL PRIMARY KEY, AUDNCS_ID_AUDNC int NOT NULL, AUDNCS_AUDIENCE bit NOT NULL,
        AUDNCS_TYPE_AUD_CAB varchar(5) NOT NULL, AUDNCS_TYPE_CLASSEMENT_ADMIS varchar(128) NULL, AUDNCS_DATE datetime NULL, AUDNCS_HEURE varchar(5) NULL,
        AUDNCS_PUBLIQUE bit NOT NULL, AUDNCS_CHAMBRE varchar(5) NULL, AUDNCS_SECTION varchar(10) NULL, AUDNCS_ID_JUG int NULL, AUDNCS_ID_GRASS int NULL,
        AUDNCS_CODE_CABINET varchar(10) NULL, AUDNCS_NON_DISPONIBLE_WEB bit NOT NULL, AUDNCS_DATE_DIFFUSION datetime NULL,
        AUDNCS_AUTEUR_SUPPRESSION varchar(255) NOT NULL, AUDNCS_DATE_SUPPRESSION datetime NOT NULL);
    """);
// Listes : un type de CABINET (R_TYPEAUDCAB_AUDIENCE = 0) et une valeur non utilisable en saisie, ecartes (jumeaux des retenus).
// Audiences : 1790001 aujourd'hui (borne : retenue, 2 affaires), 1790002 dans 10 jours (non disponible web), 1790003 hier (ecartee),
// 1790004 un CABINET dans 5 jours (ecarte), 1790005 dans 10 jours a 08:30 (avant 1790002 : l'heure ordonne a date egale).
await Sql("""
    INSERT INTO v2.TYPE_AUD_CAB VALUES ('AC79', 'Audience contentieuse 79', 1, 1), ('AX79', 'Audience hors saisie 79', 0, 1), ('CB79', 'Cabinet 79', 1, 0);
    INSERT INTO v2.CHAMBRE VALUES ('C179', 'Chambre un 79', 1), ('C979', 'Chambre close 79', 0);
    INSERT INTO v2.SECTION VALUES ('S179', 'Section un 79', 1), ('S979', 'Section close 79', 0);
    INSERT INTO v2.TYPE_CLASSEMENT VALUES ('RE79', 'Renvoi 79', 1), ('XX79', 'Classement clos 79', 0);
    INSERT INTO v2.CODE_CABINET VALUES ('CAB79', 'Cabinet du banc 79');
    INSERT INTO v2.JUGE VALUES (1790201, 'JB79', 'M.', 'MARTIN', 'Paul'), (1790202, 'JV79', 'Mme', '', NULL);
    INSERT INTO v2.GREFFIER_ASSERMENTE VALUES (1790301, 'GB79', 'Mme', 'DURAND', 'Anne');
    """);
await Sql("""
    INSERT INTO v2.AUDIENCE_CABINET VALUES
        (1790001, 1, 'AC79', @j0, '14:00', 1, 'C179', 'S179', 0),
        (1790002, 1, 'AC79', @j10, '09:00', 0, 'C179', NULL, 1),
        (1790003, 1, 'AC79', @jm1, '09:00', 1, 'C179', 'S179', 0),
        (1790004, 0, 'CB79', @j5, '10:00', 0, NULL, NULL, 0),
        (1790005, 1, 'AC79', @j10, '08:30', 1, 'C179', 'S179', 0);
    INSERT INTO v2.APPEL_AFFAIRE VALUES (1790101, 1790001), (1790102, 1790001), (1790103, 1790003);
    INSERT INTO v2.AUDIENCE_CABINET_SUPPRIMEE (AUDNCS_ID_AUDNCS, AUDNCS_ID_AUDNC, AUDNCS_AUDIENCE, AUDNCS_TYPE_AUD_CAB, AUDNCS_TYPE_CLASSEMENT_ADMIS,
        AUDNCS_DATE, AUDNCS_HEURE, AUDNCS_PUBLIQUE, AUDNCS_CHAMBRE, AUDNCS_SECTION, AUDNCS_ID_JUG, AUDNCS_ID_GRASS, AUDNCS_CODE_CABINET,
        AUDNCS_NON_DISPONIBLE_WEB, AUDNCS_DATE_DIFFUSION, AUDNCS_AUTEUR_SUPPRESSION, AUDNCS_DATE_SUPPRESSION) VALUES
        (1790401, 1790091, 1, 'AC79', 'RE79', '20260305', '14:00', 1, 'C179', 'S179', NULL, NULL, NULL, 1, '20260307', 'agent.y', '20260306'),
        (1790402, 1790092, 0, 'CB79', NULL, '20260303', '09:00', 0, NULL, NULL, 1790201, 1790301, 'CAB79', 0, NULL, 'agent.x', '20260304'),
        (1790403, 1790093, 0, 'CB79', NULL, '20260303', '08:00', 0, NULL, NULL, 1790202, NULL, NULL, 0, NULL, 'agent.z', '20260304');
    """, ("@j0", jour), ("@j10", jour.AddDays(10)), ("@jm1", jour.AddDays(-1)), ("@j5", jour.AddDays(5)));
await using (var cn = new SqlConnection(greffe.Connexion))
{
    await cn.OpenAsync();
    var aud = await LectureAudiences.LireAsync(cn, jour, CancellationToken.None);
    Exiger(aud.Total == 3 && aud.Lignes.Select(a => (a.Date, a.Heure)).SequenceEqual([(jour, "14:00"), (jour.AddDays(10), "08:30"), (jour.AddDays(10), "09:00")]),
        $"audiences a venir : du jour (borne retenue) et apres, la veille et le cabinet ecartes, date puis heure ({string.Join(",", aud.Lignes.Select(a => a.Heure))})");
    Exiger(aud.Lignes[0] is { Type: "Audience contentieuse 79", Chambre: "Chambre un 79", Section: "Section un 79", Publique: true, NonDisponibleWeb: false, Affaires: 2 }
           && aud.Lignes[2] is { Publique: false, NonDisponibleWeb: true, Affaires: 0, Section: "" },
        "audiences a venir : libelles du type, de la chambre et de la section ; affaires au role (APPEL_AFFAIRE) ; publique et non disponible web");
    Exiger(aud.Types.SequenceEqual(["Audience contentieuse 79"]) && aud.Chambres.SequenceEqual(["Chambre un 79"]) && aud.Sections.SequenceEqual(["Section un 79"])
           && aud.Classements.SequenceEqual(["Renvoi 79"]),
        "listes de la creation : utilisables en saisie seulement, types d'AUDIENCE seuls (le cabinet ecarte)");
    var un = await LectureAudiences.LireAsync(cn, jour, CancellationToken.None, plafond: 1);
    Exiger(un.Lignes.Length == 1 && un.Total == 3, $"audiences a venir : plafond 1 -> une ligne, total 3 ({un.Total})");

    var sup = await LectureGestionAudiences.LireAsync(cn, CancellationToken.None);
    Exiger(sup.Total == 3 && sup.Lignes.Select(s => s.Heure).SequenceEqual(["08:00", "09:00", "14:00"]),
        $"audiences supprimees : cabinets ET audiences, date puis heure ({string.Join(",", sup.Lignes.Select(s => s.Heure))})");
    Exiger(sup.Lignes[1] is { Cabinet: true, CodeCabinet: "CAB79 - Cabinet du banc 79", Juge: "JB79 - M. MARTIN Paul", Greffier: "GB79 - Mme DURAND Anne", AuteurSuppression: "agent.x" }
           && sup.Lignes[0] is { Cabinet: true, Juge: "", Greffier: "", CodeCabinet: "" },
        "audiences supprimees, cabinet : « code - libelle », juge et greffier « abrege - civilite nom prenom » ; sans nom, vides (OPE_RECHERCHE_GESTAUDS.cs:279-323)");
    Exiger(sup.Lignes[2] is { Cabinet: false, Chambre: "C179 - Chambre un 79", Section: "S179", TypeClassement: "RE79", Publique: true, NonDisponibleWeb: true }
           && sup.Lignes[2].DateDiffusion == new DateTime(2026, 3, 7) && sup.Lignes[2].DateSuppression == new DateTime(2026, 3, 6),
        "audiences supprimees, audience : chambre « code - libelle », section, classement admis, date de diffusion et de suppression");
}
var audRoute = await http.GetFromJsonAsync<AudiencesProgrammeesLues>(RoutesEcransMain.Pour(RoutesAudiences.Programmees, Greffe), json);
Exiger(audRoute is { Total: 3 } && audRoute.Lignes[0].Affaires == 2, "route des audiences : les audiences a venir de la lecture");
var supRoute = await http.GetFromJsonAsync<AudiencesSupprimeesLues>(RoutesEcransMain.Pour(RoutesGestionAudiences.Supprimees, Greffe), json);
Exiger(supRoute is { Total: 3 }, "route des audiences supprimees : les lignes de la lecture");
Exiger((await http.GetAsync(RoutesEcransMain.Pour(RoutesAudiences.Programmees, GreffeAbsent))).StatusCode == HttpStatusCode.NotFound
       && (await http.GetAsync(RoutesEcransMain.Pour(RoutesGestionAudiences.Supprimees, GreffeAbsent))).StatusCode == HttpStatusCode.NotFound,
    "routes des audiences : greffe absent -> 404, sans repli");
var audBase = await EcranAudiences.VueAsync(client, Greffe);
Exiger(audBase is { Mode: "serveur", Total: 3 } && audBase.Lignes[0] is { Type: "Audience contentieuse 79", Publique: true, Web: true, NbAffaires: 2 }
       && audBase.Lignes[0].Date == jour.ToString("yyyy-MM-dd") && audBase.Lignes[2] is { Web: false }
       && audBase.Chambres.SequenceEqual(["Chambre un 79"]) && !audBase.Types.Intersect(EcranAudiences.Exemple().Types).Any(),
    "pont Creer une audience branche : les audiences et listes de la base (aucune de la demonstration), « publiee sur le web » = non « non disponible web »");
Exiger((await EcranAudiences.VueAsync(null, null)) is { Mode: "demo", Lignes.Length: 3 }, "pont Creer une audience sans serveur : la demonstration, mode « demo »");
Exiger((await EcranAudiences.VueAsync(client, GreffeAbsent)) is { Mode: "erreur", Lignes.Length: 0, Types.Length: 0, Message: not null },
    "pont Creer une audience, greffe absent : le motif, AUCUNE ligne ni liste");
var gesBase = await EcranGestionAudiences.VueAsync(client, Greffe);
Exiger(gesBase is { Mode: "serveur", Total: 3 } && gesBase.Lignes[1] is { Jour: "Mardi", Date: "03/03/2026", Cabinet: true, TypeCabinet: "CB79", Juge: "JB79 - M. MARTIN Paul" }
       && gesBase.Lignes[2] is { Jour: "Jeudi", Cabinet: false, TypeCabinet: "", DateSuppression: "06/03/2026", EnvoiSuppression: "07/03/2026" },
    "pont Gestion des audiences branche : jour en francais, mode de RIG v2 porte par chaque ligne, dates jj/mm/aaaa");
Exiger((await EcranGestionAudiences.VueAsync(null, null)) is { Mode: "demo", Lignes.Length: 5 }, "pont Gestion des audiences sans serveur : la demonstration, mode « demo »");
Exiger((await EcranGestionAudiences.VueAsync(client, GreffeAbsent)) is { Mode: "erreur", Lignes.Length: 0, Message: not null },
    "pont Gestion des audiences, greffe absent : le motif, AUCUNE ligne");

// ============================================================ 6. CONSULTATION JUDICIAIRE (LectureConsultationJud.cs, lot 6)
// « Consultation judiciaire » (PROC_CJUD, mode CJUDINFOG), branchee au lot 6 du plan : les enrolements RPJC (DEMAT_RPJC_ENR),
// etat du flux au libelle de eDematEtat (DematEtat.cs:17-47), traitement A / R / vide (ULT_COMBO_ACCEPTE_REFUSE.cs:20-21),
// role de l'instance creee a l'acceptation, du plus recent au plus ancien. Identifiants 1 640 0xx (lot 6 : 1 600 000 a
// 1 699 999). Joue apres les sections 4 et 5 : ses instances et demandes n'y entrent pas (autres tables lues).
await Sql("""
    CREATE TABLE v2.DEMAT_RPJC_ENR (RPJCE_ID_RPJCE int NOT NULL PRIMARY KEY, RPJCE_DATE_CREATION datetime NULL, RPJCE_ETAT varchar(2) NULL,
        RPJCE_ETAT_TRAITEMENT_RIG varchar(2) NULL, RPJCE_DATE_TRAITEMENT_RIG datetime NULL, RPJCE_TYPE_REJET_RIG varchar(100) NULL,
        RPJCE_MOTIF_REJET_RIG varchar(max) NULL, RPJCE_REFERENCE_DEMANDE varchar(10) NULL, RPJCE_ID_DMND int NULL, RPJCE_ID_INSTN int NULL);
    INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT) VALUES (1640101, '2026J64001', 'ENROLE ALPHA');
    INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_EN_COURS, DMND_CODE_PROSS) VALUES (1640201, 'D1640201', 'V', 0, 'CJUD');
    -- 1640001 : accepte, valide par Docapost (4), instance et demande ; 1640002 : integre (1), pas encore traite (jumeau de
    -- 1640001 : ni instance ni demande, sa reference RPVA) ; 1640003 : refuse, rejete (9), le plus ancien ; 1640004 : un etat
    -- hors de eDematEtat (77), montre tel quel.
    INSERT INTO v2.DEMAT_RPJC_ENR VALUES
        (1640001, '20260302 10:00', '4', 'A', '20260303', NULL, NULL, 'R164001', 1640201, 1640101),
        (1640002, '20260303 09:00', '1', NULL, NULL, NULL, NULL, 'R164002', NULL, NULL),
        (1640003, '20260301 08:00', '9', 'R', '20260302', 'Pièce manquante', 'Dossier incomplet', 'R164003', NULL, NULL),
        (1640004, '20260228 08:00', '77', NULL, NULL, NULL, NULL, 'R164004', NULL, NULL);
    """);
await using (var cn = new SqlConnection(greffe.Connexion))
{
    await cn.OpenAsync();
    var enr = await LectureConsultationJud.LireAsync(cn, CancellationToken.None);
    Exiger(enr.Total == 4 && enr.Lignes.Select(e => e.Id).SequenceEqual([1640002, 1640001, 1640003, 1640004]),
        $"consultation judiciaire : tous les enrolements, du plus recent au plus ancien ({string.Join(",", enr.Lignes.Select(e => e.Id))})");
    Exiger(enr.Lignes[1] is { Role: "2026J64001", Denomination: "ENROLE ALPHA", EtatDemat: "Validé", EtatTraitement: "Accepté", Demande: "D1640201", Reference: "R164001" }
           && enr.Lignes[1].Traitement == new DateTime(2026, 3, 3),
        "consultation judiciaire : accepte (A), valide (4), le role et le nom de l'instance, la demande RIG");
    Exiger(enr.Lignes[0] is { Role: "", Denomination: "", EtatDemat: "Intégré avec succès", EtatTraitement: "En attente", Demande: "" } && enr.Lignes[0].Traitement is null,
        "consultation judiciaire : pas encore traite -> « En attente », sans instance ni demande");
    Exiger(enr.Lignes[2] is { EtatDemat: "Rejeté", EtatTraitement: "Refusé", TypeRejet: "Pièce manquante", MotifRejet: "Dossier incomplet" }
           && enr.Lignes[3].EtatDemat == "77",
        "consultation judiciaire : refuse (R) avec type et motif ; un etat hors eDematEtat reste son code");
    var un = await LectureConsultationJud.LireAsync(cn, CancellationToken.None, plafond: 1);
    Exiger(un.Lignes.Length == 1 && un.Total == 4, $"consultation judiciaire : plafond 1 -> une ligne, total 4 ({un.Total})");
}
var enrRoute = await http.GetFromJsonAsync<EnrolementsLus>(RoutesEcransMain.Pour(RoutesConsultationJud.Enrolements, Greffe), json);
Exiger(enrRoute is { Total: 4 } && enrRoute.Lignes[0].Id == 1640002, "route de la consultation judiciaire : les lignes de la lecture");
Exiger((await http.GetAsync(RoutesEcransMain.Pour(RoutesConsultationJud.Enrolements, GreffeAbsent))).StatusCode == HttpStatusCode.NotFound,
    "route de la consultation judiciaire : greffe absent -> 404, sans repli");
var cjBase = await EcranConsultationJud.VueAsync(client, Greffe);
Exiger(cjBase is { Mode: "serveur", Total: 4 }
       && cjBase.Lignes[1] is { Affaire: "2026J64001", Denomination: "ENROLE ALPHA", Dossier: "2026J64001", Creation: "2026-03-02", Traitement: "2026-03-03", NbPieces: null, Anomalies: null, Xml: null }
       && cjBase.Lignes[0] is { Affaire: "R164002", Denomination: "Référence RPVA R164002", Dossier: "", Traitement: null }
       && !cjBase.Lignes.Any(l => EcranConsultationJud.Exemple().Lignes.Any(e => e.Affaire == l.Affaire)),
    "pont Consultation judiciaire branche : les enrolements de la base (aucun de la demonstration), la reference RPVA tant qu'il n'y a pas d'instance, pieces / anomalies / XML non lus (null)");
Exiger(cjBase.Lignes[1].Id == "1640001" && EcranConsultationJud.Exemple().Lignes.All(l => l.Id is null && l.Xml is not null),
    "pont Consultation judiciaire : chaque enrolement de la base porte son id (le XML se lit par lui) ; la demonstration porte son XML, sans id");
// « Visualiser le XML » (ult_ButtonvisuXML) : RPJCE_FLUX de l'enrolement, lu a la demande (PROC_CJUD.cs:437-455), en UTF-8
// (accents gardes). Jumeaux : un enrolement sans flux (null, dit) ; un enrolement absent (Existe faux, dit).
const string FluxBanc = "<demandeEnrolement><referenceDemande>R164001</referenceDemande><demandeur>Société Écluse</demandeur></demandeEnrolement>";
await Sql("ALTER TABLE v2.DEMAT_RPJC_ENR ADD RPJCE_FLUX varbinary(max) NULL;");
await Sql("UPDATE v2.DEMAT_RPJC_ENR SET RPJCE_FLUX = @flux WHERE RPJCE_ID_RPJCE = 1640001;", ("@flux", System.Text.Encoding.UTF8.GetBytes(FluxBanc)));
await using (var cn = new SqlConnection(greffe.Connexion))
{
    await cn.OpenAsync();
    Exiger(await LectureConsultationJud.LireXmlAsync(cn, 1640001, CancellationToken.None) == new FluxEnrolementLu(1640001, true, FluxBanc)
           && await LectureConsultationJud.LireXmlAsync(cn, 1640002, CancellationToken.None) == new FluxEnrolementLu(1640002, true, null)
           && await LectureConsultationJud.LireXmlAsync(cn, 1649999, CancellationToken.None) == new FluxEnrolementLu(1649999, false, null),
        "consultation judiciaire, XML : le flux de l'enrolement en UTF-8 ; sans flux, null ; absent, Existe faux");
}
Exiger(await http.GetFromJsonAsync<FluxEnrolementLu>(RoutesConsultationJud.XmlPour(Greffe, 1640001), json) == new FluxEnrolementLu(1640001, true, FluxBanc),
    "route du XML de la consultation judiciaire : le flux de la lecture");
Exiger((await EcranConsultationJud.XmlAsync(client, Greffe, "1640001")) == new XmlConsultationJud("serveur", FluxBanc, null)
       && (await EcranConsultationJud.XmlAsync(client, Greffe, "1640002")) == new XmlConsultationJud("serveur", null, EcranConsultationJud.SansFlux)
       && (await EcranConsultationJud.XmlAsync(client, Greffe, "1649999")) == new XmlConsultationJud("serveur", null, EcranConsultationJud.Disparu)
       && (await EcranConsultationJud.XmlAsync(client, Greffe, "abc")) is { Xml: null, Message: EcranConsultationJud.Disparu }
       && (await EcranConsultationJud.XmlAsync(client, GreffeAbsent, "1640001")) is { Mode: "erreur", Xml: null, Message: not null },
    "pont Consultation judiciaire, « Visualiser le XML » : le flux ; sans flux, absent ou id illisible, il le dit ; greffe absent, le motif");
Exiger((await EcranConsultationJud.VueAsync(null, null)) is { Mode: "demo", Lignes.Length: 4 }, "pont Consultation judiciaire sans serveur : la demonstration, mode « demo »");
Exiger((await EcranConsultationJud.VueAsync(client, GreffeAbsent)) is { Mode: "erreur", Lignes.Length: 0, Message: not null },
    "pont Consultation judiciaire, greffe absent : le motif, AUCUNE ligne");

// ============================================================ 7. ACTES (LectureActes.cs, lot 6)
// « Actes » (PROC_ACTES_NUM, mode Acte) : les actes deposes depuis 2014 et jusqu'a aujourd'hui - 2 jours, hors comptes annuels,
// pas encore dans la GED (ACTDP_GUID_VIDIGED vide), OPE_RESULTAT.cs:393-431 ; criteres de ChargerDonnees (:450-541). Chaque
// refus a son jumeau retenu (1641031). Identifiants 1 641 0xx.
await Sql("""
    ALTER TABLE v2.DEMANDE ADD DMND_ID_ACTRC int NULL;
    CREATE TABLE v2.DEMAT_DCA (DMTDCA_ID_DMTDCA int NOT NULL PRIMARY KEY, DMTDCA_ID_DMND int NULL);
    CREATE TABLE v2.CATEGORIE_DEPOT (R_CATEGORIED_VALEUR varchar(16) NOT NULL PRIMARY KEY, R_CATEGORIED_LIBELLE_ECRAN varchar(35) NULL,
        R_CATEGORIED_COMPTES_ANNUEL bit NULL);
    CREATE TABLE v2.ABREGE_PIECE (R_ABREGEPIEC_VALEUR varchar(16) NOT NULL PRIMARY KEY, R_ABREGEPIEC_SYNONYME varchar(16) NULL,
        R_ABREGEPIEC_DEVELOPPE_EDIT varchar(256) NULL);
    CREATE TABLE v2.DEPOT_RCS (DPTRC_ID_DPTRC int NOT NULL PRIMARY KEY, DPTRC_NUM_DEPOT varchar(12) NOT NULL, DPTRC_DATE datetime NULL,
        DPTRC_CATEGORIE_DEPOT varchar(16) NULL);
    CREATE TABLE v2.ACTE_DEPOSE (ACTDP_ID_ACDPS int NOT NULL PRIMARY KEY, ACTDP_ID_DPTRC int NULL, ACTDP_ABREGE_PIECE varchar(16) NOT NULL,
        ACTDP_NUM_ORDRE int NULL, ACTDP_DESCRIPTION varchar(256) NULL, ACTDP_DATE datetime NULL, ACTDP_SANS_DOCUMENT bit NULL,
        ACTDP_GUID_VIDIGED uniqueidentifier NULL);
    INSERT INTO v2.CATEGORIE_DEPOT VALUES ('A64', 'Depots actes banc', 0), ('B64', 'Comptes banc', 1), ('C64', 'Ordonnances banc', NULL);
    INSERT INTO v2.ABREGE_PIECE VALUES ('PV64', 'PVAG', 'Proces-verbal du banc'), ('ST64', 'STAT', 'Statuts du banc');
    SET IDENTITY_INSERT rec.Entreprise ON;
    INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES (1641001, '516410001', '', N'ACTES ALPHA SAS', 'C', 0);
    SET IDENTITY_INSERT rec.Entreprise OFF;
    SET IDENTITY_INSERT rec.DossierRcs ON;
    INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
        ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
        SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise) VALUES
        (1641001, 1641001, 'B1', '2010B64101', '516410001', '20100101', 'C', 0, 0, '20100101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0);
    SET IDENTITY_INSERT rec.DossierRcs OFF;
    SET IDENTITY_INSERT rec.ActionRegistre ON;
    INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification) VALUES
        (1641011, 1641001, 1641021, 'BANCACT', 'C', 'V', 0), (1641012, 1641001, 1641022, 'BANCACT', 'C', 'V', 0),
        (1641013, 1641001, 1641023, 'BANCACT', 'C', 'V', 0), (1641014, 1641001, 1641024, 'BANCACT', 'C', 'V', 0),
        (1641015, 1641001, 1641025, 'BANCACT', 'C', 'V', 0);
    SET IDENTITY_INSERT rec.ActionRegistre OFF;
    """);
// 1641021 depose le 04/05/2020 (retenu) ; 1641022 en 2013 (sous le plancher de 2014, retrouve par son numero) ; 1641023 en
// categorie de comptes annuels ; 1641024 hier (apres « aujourd'hui - 2 jours ») ; 1641025 en 2021, son acte sans document.
await Sql("""
    INSERT INTO v2.DEPOT_RCS VALUES (1641021, 'D641021', '20200504 10:00', 'A64'), (1641022, 'D641022', '20130601', 'A64'),
        (1641023, 'D641023', '20200601', 'B64'), (1641024, 'D641024', @hier, 'A64'), (1641025, 'D641025', '20210101', 'C64');
    INSERT INTO v2.ACTE_DEPOSE VALUES
        (1641031, 1641021, 'PV64', 1, NULL, '20200430', 0, NULL),
        (1641032, 1641021, 'ST64', 2, 'Statuts mis a jour', '20200430', 0, NEWID()),
        (1641033, 1641022, 'PV64', 1, 'PV de 2013', '20130530', 0, NULL),
        (1641034, 1641023, 'PV64', 1, NULL, '20200530', 0, NULL),
        (1641035, 1641024, 'PV64', 1, NULL, @hier, 0, NULL),
        (1641036, 1641025, 'ST64', 1, NULL, '20201230', 1, NULL);
    INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_EN_COURS, DMND_CODE_PROSS, DMND_ID_ACTRC) VALUES
        (1641041, 'D1641041', 'V', 0, 'DAS', 1641011);
    INSERT INTO v2.DEMAT_DAS (DMTDAS_ID_DMTDAS, DMTDAS_ID_DMND) VALUES (1641051, 1641041);
    """, ("@hier", jour.AddDays(-1)));
RechercheActes Actes(string numero = "", string[]? abreges = null, string categorie = "", DateTime? du = null, DateTime? au = null,
                     bool sansDocument = false, int plafond = 50) => new(numero, abreges ?? [], categorie, du, au, sansDocument, plafond);
await using (var cn = new SqlConnection(greffe.Connexion))
{
    await cn.OpenAsync();
    async Task<int[]> Ids(RechercheActes r) => [.. (await LectureActes.LireAsync(cn, r, jour, CancellationToken.None)).Lignes.Select(a => a.Id)];
    var actes = await LectureActes.LireAsync(cn, Actes(), jour, CancellationToken.None);
    Exiger(actes.Total == 1 && actes.Lignes.Select(a => a.Id).SequenceEqual([1641031]),
        $"actes : seul l'acte de 2020 hors GED et hors comptes annuels (2013, GED, comptes annuels, hier, sans document ecartes) ({string.Join(",", actes.Lignes.Select(a => a.Id))})");
    Exiger(actes.Lignes[0] is { NumeroGestion: "2010B64101", NumeroDepot: "D641021", Designation: "ACTES ALPHA SAS", Categorie: "A64",
                                CategorieLibelle: "Depots actes banc", Abrege: "PVAG", Description: "Proces-verbal du banc", Demat: true, SansDocument: false }
           && actes.Lignes[0].DateDepot == new DateTime(2020, 5, 4, 10, 0, 0) && actes.Lignes[0].DateActe == new DateTime(2020, 4, 30),
        "actes : designation, abrege (synonyme), description de l'abrege a defaut, demat (un flux DAS sur l'action)");
    Exiger(actes.Categories.Select(k => k.Code).SequenceEqual(["A64", "C64"]),
        $"actes : les categories hors comptes annuels, par libelle ({string.Join(",", actes.Categories.Select(k => k.Code))})");
    Exiger((await Ids(Actes(numero: "D641022"))).SequenceEqual([1641033]),
        "actes : un numero leve le plancher de 2014 a 1954 (OPE_RESULTAT.cs:500-501)");
    var parGestion = await LectureActes.LireAsync(cn, Actes(numero: "2010B641"), jour, CancellationToken.None);
    Exiger(parGestion.Total == 2 && parGestion.Lignes.Select(a => a.Id).SequenceEqual([1641033, 1641031]),
        "actes : le numero de gestion LIKE %n%, depots de 2013 et 2020, par date de depot");
    var unSeul = await LectureActes.LireAsync(cn, Actes(numero: "2010B641", plafond: 1), jour, CancellationToken.None);
    Exiger(unSeul.Lignes.Length == 1 && unSeul.Total == 2, $"actes : plafond 1 -> une ligne, total 2 ({unSeul.Total})");
    Exiger((await Ids(Actes(sansDocument: true))).SequenceEqual([1641036]),
        "actes : « Lister actes non numerisables » -> ACTDP_SANS_DOCUMENT = 1 seuls (la case remplace la liste)");
    Exiger((await Ids(Actes(abreges: ["STAT"]))).Length == 0 && (await Ids(Actes(abreges: ["PVAG", "STAT"]))).SequenceEqual([1641031]),
        "actes : abreges coches -> R_ABREGEPIEC_SYNONYME IN (...)");
    Exiger((await Ids(Actes(du: new DateTime(2020, 5, 5)))).Length == 0 && (await Ids(Actes(au: new DateTime(2020, 5, 4)))).SequenceEqual([1641031]),
        "actes : periode de depot, bornes incluses (CONVERT(date, DPTRC_DATE))");
    Exiger((await Ids(Actes(categorie: "C64"))).Length == 0 && (await Ids(Actes(categorie: "A64"))).SequenceEqual([1641031]),
        "actes : la categorie choisie filtre DPTRC_CATEGORIE_DEPOT");
}
var actesRoute = await http.PostAsJsonAsync(RoutesEcransMain.Pour(RoutesActes.Actes, Greffe), Actes(), json);
var actesLus = await actesRoute.Content.ReadFromJsonAsync<ActesLus>(json);
Exiger(actesLus is { Total: 1 } && actesLus.Lignes[0].Id == 1641031, "route des actes : les lignes de la lecture");
Exiger((await http.PostAsJsonAsync(RoutesEcransMain.Pour(RoutesActes.Actes, GreffeAbsent), Actes(), json)).StatusCode == HttpStatusCode.NotFound,
    "route des actes : greffe absent -> 404, sans repli");
CriteresActes CritActes(bool sansDocument = false, bool anomalies = false) => new("", "", "", null, null, sansDocument, anomalies, 50);
var acBase = await EcranActes.TrouverAsync(client, Greffe, CritActes());
Exiger(acBase is { Mode: "serveur", Total: 1, Message: EcranActes.NoteBase }
       && acBase.Lignes[0] is { Rang: 1, Dossier: "2010B64101", Depot: "D641021", Denomination: "ACTES ALPHA SAS", CategorieLisible: "Depots actes banc",
                                Abrege: "PVAG", Depose: "04/05/2020", Acte: "30/04/2020", Cloture: "", Demat: true, Anomalie: "", Numerisable: true }
       && acBase.Categories.Length == 2,
    "pont Actes branche : les actes de la base, la note dit ce que la liste montre");
var acSansDoc = await EcranActes.TrouverAsync(client, Greffe, CritActes(sansDocument: true));
Exiger(acSansDoc.Lignes is [{ Depot: "D641025", Numerisable: false }], "pont Actes : la case « non numerisables » part a la base");
Exiger((await EcranActes.TrouverAsync(client, Greffe, CritActes(anomalies: true))) is { Mode: "serveur", Lignes.Length: 0, Message: EcranActes.NoteAnomalies },
    "pont Actes : « Avec anomalie seulement » -> aucune ligne, la note nomme le mode Bilan non lu");
Exiger((await EcranActes.TrouverAsync(null, null, CritActes())) is { Mode: "demo", Lignes.Length: > 0 }, "pont Actes sans serveur : la demonstration, mode « demo »");
Exiger((await EcranActes.TrouverAsync(client, GreffeAbsent, CritActes())) is { Mode: "erreur", Lignes.Length: 0, Message: not null },
    "pont Actes, greffe absent : le motif, AUCUNE ligne");

// ============================================================ 8. RECHERCHE D'AFFAIRES (LectureAffaires.cs, lot 6)
// « Recherche d'affaires » (PROC_RECHAFF) : AFFAIRE par son numero, celui d'une demande ou d'une facture (OPE_CRITERES.cs:30-126),
// solde de Affaire.GetSolde (ventilations - articles utilises, Affaire.cs:318-335), demandes et factures jointes ; ajouts de la V3 :
// libelle ou SIREN, instance rattachee (role sans blancs, comme PROC_VJUD). Sans critere : rien. Identifiants 1 642 0xx.
// La colonne ajoutee doit exister avant que le lot qui l'ecrit soit compile : un appel a part.
await Sql("ALTER TABLE v2.INSTANCE ADD INSTN_DATE_SAISINE_GREFFE datetime NULL");
await Sql("""
    CREATE TABLE v2.AFFAIRE (AFFRE_ID_AFFRE int NOT NULL PRIMARY KEY, AFFRE_LIBELLE varchar(255) NULL, AFFRE_DATE_CREATION datetime NULL);
    CREATE TABLE v2.VENTILATION (VENTI_ID_VENTI int NOT NULL PRIMARY KEY, VENTI_ID_AFFRE int NULL, VENTI_MONTANT money NULL);
    CREATE TABLE v2.ARTICLE_UTILISE (ARTUT_ID_ARTUT int NOT NULL PRIMARY KEY, ARTUT_ID_AFFRE int NULL, ARTUT_MONTANT_TTC money NULL);
    INSERT INTO v2.AFFAIRE VALUES (1642001, 'Immatriculation - AFF ALPHA', '20260201'), (1642002, 'Assignation - AFF BETA', '20260301'),
        (1642003, 'Autre - AFF GAMMA', '20260115');
    -- 1642001 : une demande sur le dossier RCS 1641001 (section 7), facturee ; ventile 100,00, articles 142,50 -> solde -42,50.
    -- 1642002 : une instance (role 2026J64201), une demande sans facture ; solde 0. 1642003 : rien.
    INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_EN_COURS, DMND_CODE_PROSS, DMND_ID_AFFRE, DMND_ID_DSRCS) VALUES
        (1642101, 'D1642101', 'V', 0, 'IMMAT', 1642001, 1641001), (1642102, 'D1642102', 'V', 0, 'CJUD', 1642002, NULL);
    INSERT INTO v2.FACTURE (FACTU_NUMERO_FACTURE, FACTU_NUM_DEMANDE) VALUES ('F1642101', 'D1642101');
    INSERT INTO v2.VENTILATION VALUES (1642301, 1642001, 60.00), (1642302, 1642001, 40.00);
    INSERT INTO v2.ARTICLE_UTILISE VALUES (1642401, 1642001, 142.50);
    INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT, INSTN_CODE_AFFAIRE, INSTN_ID_AFFRE, INSTN_DATE_SAISINE_GREFFE)
        VALUES (1642201, '2026J64201', 'AFF BETA SARL', 'CT64', 1642002, '20260227');
    """);
RechercheAffaires Affaires(string numero = "", string libelle = "", string idInstance = "", string role = "", string code = "", string nom = "",
                           DateTime? du = null, DateTime? au = null) => new(numero, libelle, idInstance, role, code, nom, du, au, 200);
await using (var cn = new SqlConnection(greffe.Connexion))
{
    await cn.OpenAsync();
    async Task<int[]> Ids(RechercheAffaires r) => [.. (await LectureAffaires.LireAsync(cn, r, CancellationToken.None)).Lignes.Select(a => a.Id)];
    Exiger((await Ids(Affaires())).Length == 0, "affaires : sans aucun critere, rien (SearchQuery vide, OPE_CRITERES.cs:48)");
    var alpha = await LectureAffaires.LireAsync(cn, Affaires(numero: "1642001"), CancellationToken.None);
    Exiger(alpha.Total == 1 && alpha.Lignes[0] is { Id: 1642001, Libelle: "Immatriculation - AFF ALPHA", Solde: -42.50m, Demandes: "D1642101",
                                                      Factures: "F1642101", Siren: "516410001", AvecDossierRcs: true, IdInstance: "", Role: "" }
           && alpha.Lignes[0].Creation == new DateTime(2026, 2, 1),
        "affaires : par numero d'affaire ; solde = ventilations - articles (Affaire.cs:318-335), demandes, factures, SIREN du dossier");
    Exiger((await Ids(Affaires(numero: "D1642102"))).SequenceEqual([1642002]) && (await Ids(Affaires(numero: "F1642101"))).SequenceEqual([1642001])
           && (await Ids(Affaires(numero: "1642999"))).Length == 0,
        "affaires : le numero d'une demande ou d'une facture trouve son affaire ; un numero inconnu, rien");
    Exiger((await Ids(Affaires(libelle: "AFF"))).SequenceEqual([1642002, 1642001, 1642003]) && (await Ids(Affaires(libelle: "516 410 001"))).SequenceEqual([1642001]),
        "affaires : libelle contient, ou SIREN du dossier ; de la plus recente a la plus ancienne");
    var beta = await LectureAffaires.LireAsync(cn, Affaires(role: "2026 J 64201"), CancellationToken.None);
    Exiger(beta.Lignes is [{ Id: 1642002, IdInstance: "1642201", Role: "2026J64201", CodeAffaire: "CT64", Nom: "AFF BETA SARL", Solde: 0m, Factures: "" }]
           && beta.Lignes[0].Saisine == new DateTime(2026, 2, 27) && (await Ids(Affaires(role: "2026J64202"))).Length == 0,
        "affaires : le role de l'instance, blancs retires (comme PROC_VJUD) ; un autre role, rien");
    Exiger((await Ids(Affaires(nom: "BETA"))).SequenceEqual([1642002]) && (await Ids(Affaires(code: "CT64"))).SequenceEqual([1642002])
           && (await Ids(Affaires(idInstance: "1642201"))).SequenceEqual([1642002]) && (await Ids(Affaires(idInstance: "abc"))).Length == 0,
        "affaires : nom courant, code affaire et id de l'instance ; un id illisible ne trouve rien");
    Exiger((await Ids(Affaires(du: new DateTime(2026, 2, 28)))).Length == 0 && (await Ids(Affaires(au: new DateTime(2026, 2, 27)))).SequenceEqual([1642002]),
        "affaires : periode de saisine de l'instance, bornes incluses");
}
var affRoute = await http.PostAsJsonAsync(RoutesEcransMain.Pour(RoutesAffaires.Affaires, Greffe), Affaires(libelle: "AFF"), json);
Exiger((await affRoute.Content.ReadFromJsonAsync<AffairesLues>(json)) is { Total: 3 }, "route des affaires : les lignes de la lecture");
Exiger((await http.PostAsJsonAsync(RoutesEcransMain.Pour(RoutesAffaires.Affaires, GreffeAbsent), Affaires(), json)).StatusCode == HttpStatusCode.NotFound,
    "route des affaires : greffe absent -> 404, sans repli");
CriteresAffaires CritAff(string numero = "", string libelle = "", string type = "", string etat = "", string nom = "")
    => new(numero, libelle, type, "", "", "", "", nom, etat, null, null);
var affBase = await EcranRechAffaires.TrouverAsync(client, Greffe, CritAff(numero: "1642001"), null, true, "1642001");
Exiger(affBase is { Mode: "serveur", Message: EcranRechAffaires.NoteBase, AucunCritere: false, Choisie.Numero: "1642001" }
       && affBase.Lignes is [{ Numero: "1642001", Type: "RCS", Siren: "516 410 001", SoldeValeur: -42.50m, Creation: "01/02/2026", Saisine: "",
                               Demandes: "D1642101", Factures: "F1642101", Nature: "", Etat: "" }],
    "pont Recherche d'affaires branche : l'affaire de la base, type RCS (demande sur un dossier), la note");
Exiger((await EcranRechAffaires.TrouverAsync(client, Greffe, CritAff(nom: "BETA", type: "JUD"), null, true, null)).Lignes is [{ Numero: "1642002", Nature: "instance", Role: "2026J64201" }]
       && (await EcranRechAffaires.TrouverAsync(client, Greffe, CritAff(nom: "BETA", type: "RCS"), null, true, null)).Lignes.Length == 0,
    "pont Recherche d'affaires : le type se filtre en memoire sur les affaires lues (JUD avec instance)");
Exiger((await EcranRechAffaires.TrouverAsync(client, Greffe, CritAff(libelle: "AFF", etat: "En cours"), null, true, null)).Lignes.Length == 0,
    "pont Recherche d'affaires : AFFAIRE n'a pas d'etat -> le critere « Etat » ne trouve rien en base");
Exiger((await EcranRechAffaires.TrouverAsync(client, Greffe, CritAff(), null, true, null)) is { Mode: "serveur", AucunCritere: true, Lignes.Length: 0 },
    "pont Recherche d'affaires : sans critere, aucune ligne (comme RIG v2)");
Exiger((await EcranRechAffaires.CompterAsync(client, Greffe, [CritAff(libelle: "AFF"), CritAff(libelle: "AFF", type: "JUD"), CritAff(type: "JUD")]))
       .SequenceEqual([3, 1, null]),
    "pont Recherche d'affaires : le compte des vues sur la base ; une vue sans critere cherche n'a pas de compte (null, jamais un faux 0)");
Exiger((await EcranRechAffaires.TrouverAsync(null, null, CritAff(), null, true, null)) is { Mode: "demo", Lignes.Length: > 0 },
    "pont Recherche d'affaires sans serveur : la demonstration, mode « demo »");
Exiger((await EcranRechAffaires.TrouverAsync(client, GreffeAbsent, CritAff(numero: "1"), null, true, null)) is { Mode: "erreur", Lignes.Length: 0, Message: not null },
    "pont Recherche d'affaires, greffe absent : le motif, AUCUNE ligne");

// ============================================================ 9. REQUISITIONS (LectureEntreprisesRequi.cs, lot 6)
// « Réquisitions » (PROC_REQUI), la recherche des entreprises (OPE_REQUI_RECHERCHE.cs:140-256 de la branche 275) sur la requete de
// « Dossier RCS » : designation contient, SIREN egal, adresse du siege ; « Inclure les supprimes » ; limite 50..500. Identifiants
// 1 643 0xx (et le dossier 1641001 de la section 7, sans adresse).
await Sql("""
    SET IDENTITY_INSERT rec.Adresse ON;
    INSERT INTO rec.Adresse (Id, NumeroVoie, TypeVoie, NomVoie, CodePostal, Localite, EtatInfo) VALUES
        (1643001, '12', 'RUE', 'DES BANCS', '80000', 'AMIENS', 'C'), (1643002, '4', 'AV', 'DU PARC', '80480', 'DURY', 'C');
    SET IDENTITY_INSERT rec.Adresse OFF;
    SET IDENTITY_INSERT rec.Entreprise ON;
    INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, Patronyme, Prenom, FormeJuridique, IdAdresse, EtatInfo, FicheSommaire) VALUES
        (1643001, '516430001', '', N'REQUI ALPHA SARL', NULL, NULL, 'SARL', 1643001, 'C', 0),
        (1643002, '516430002', '', N'', N'BANCPP', N'Paul', NULL, 1643002, 'C', 0),
        (1643003, '516430003', '', N'REQUI RADIEE SA', NULL, NULL, 'SA', 1643001, 'C', 0);
    SET IDENTITY_INSERT rec.Entreprise OFF;
    SET IDENTITY_INSERT rec.DossierRcs ON;
    INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
        ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
        SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise, DateRadiation) VALUES
        (1643001, 1643001, 'B1', '2011B64301', '516430001', '20110101', 'C', 0, 0, '20110101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
        (1643002, 1643002, 'A1', '2011A64302', '516430002', '20110101', 'C', 0, 0, '20110101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
        (1643003, 1643003, 'B1', '2011B64303', '516430003', '20110101', 'C', 0, 0, '20110101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, '20200101');
    SET IDENTITY_INSERT rec.DossierRcs OFF;
    """);
// Le nombre d'inscriptions (RechercheDossier.NombreInscription, RechercheDossier.cs:127-165) a la date de fraicheur du domaine
// ENDETT (30/06/2026 ici : une inscription diffusee apres ne compte pas, meme si elle l'est avant aujourd'hui), et la procedure
// collective qui bloque le certificat de non-faillite (VerifSiPC « ENTR », OPE_REQUI_RESULTATS.cs:579-616) : chaque regle et
// son jumeau. Identifiants 1 643 1xx. L'ALTER passe seul : la colonne ajoutee n'existe pas a la compilation du meme lot SQL.
await Sql("""
    ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_ID_RPRTR int NULL, PRCLL_NON_DIFFUSABLE bit NULL, PRCLL_ID_NINSC int NULL,
        PRCLL_ETAT_PC varchar(1) NULL, PRCLL_CODE_CLOTURE varchar(5) NULL;
    CREATE TABLE v2.CODE_CLOTURE (R_CODECLOTUR_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_CODECLOTUR_ARRET_DIFF_INFO bit NULL);
    CREATE TABLE v2.DEBITEUR_END (DBEND_ID_DBEND int NOT NULL PRIMARY KEY, DBEND_ID_RPRTR int NULL, DBEND_ID_INSCR int NULL, DBEND_ID_ADRBS int NULL);
    CREATE TABLE v2.RADIATION_TOTALE (RDTTL_ID_RDTTL int NOT NULL PRIMARY KEY, RDTTL_ID_INSCR int NULL, RDTTL_DATE_RADIATION datetime NULL);
    CREATE TABLE v2.DATE_TRAVAIL (DTTRV_CODE_DATE_TRAVAIL varchar(10) NOT NULL PRIMARY KEY, DTTRV_DATE_FRAICHEUR datetime NULL);
    """);
await Sql("""
    INSERT INTO v2.DATE_TRAVAIL VALUES ('WBANC', '20260630');
    SET IDENTITY_INSERT rec.TypeDemande ON;
    INSERT INTO rec.TypeDemande (Id, Reference, Niveau, CodeDateTravail, LibelleEcran, CreationDossier, AffichageTous, DistributionAuto,
        AffichageDistribution, PrioriteGroupe, RegistreArrivee) VALUES (1643101, 'ENDETT', 1, 'WBANC', 'Endettement banc', 0, 0, 0, 0, 0, 0);
    SET IDENTITY_INSERT rec.TypeDemande OFF;
    SET IDENTITY_INSERT rec.Entreprise ON;
    INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, Patronyme, Prenom, FormeJuridique, IdAdresse, EtatInfo, FicheSommaire) VALUES
        (1643104, '516430004', '', N'PCBANC DEUX PC', NULL, NULL, 'SA', NULL, 'C', 0),
        (1643105, '516430005', '', N'PCBANC NON DIFFUSABLE', NULL, NULL, 'SA', NULL, 'C', 0),
        (1643106, '516430006', '', N'PCBANC CLOTURE ARRET', NULL, NULL, 'SA', NULL, 'C', 0),
        (1643107, '516430007', '', N'PCBANC ETAT N', NULL, NULL, 'SA', NULL, 'C', 0),
        (1643108, '516430008', '', N'PCBANC NON DIFFUSABLE NI', NULL, NULL, 'SA', NULL, 'C', 0);
    SET IDENTITY_INSERT rec.Entreprise OFF;
    SET IDENTITY_INSERT rec.DossierRcs ON;
    INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
        ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
        SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise, DateRadiation) VALUES
        (1643104, 1643104, 'B1', '2011B64304', '516430004', '20110101', 'C', 0, 0, '20110101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
        (1643105, 1643105, 'B1', '2011B64305', '516430005', '20110101', 'C', 0, 0, '20110101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
        (1643106, 1643106, 'B1', '2011B64306', '516430006', '20110101', 'C', 0, 0, '20110101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
        (1643107, 1643107, 'B1', '2011B64307', '516430007', '20110101', 'C', 0, 0, '20110101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
        (1643108, 1643108, 'B1', '2011B64308', '516430008', '20110101', 'C', 0, 0, '20110101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL);
    SET IDENTITY_INSERT rec.DossierRcs OFF;
    -- REQUI ALPHA SARL (1643001, siege 1643001) : 101 sans peremption, 102 perimee plus tard, 106 radiee APRES la fraicheur
    -- comptent (3) ; 103 diffusee apres la fraicheur, 104 perimee avant, 105 radiee avant, 107 a une autre adresse, non.
    -- PCBANC DEUX PC (1643104, sans adresse) : 108, a n'importe quelle adresse, compte (1).
    SET IDENTITY_INSERT rec.Inscription ON;
    INSERT INTO rec.Inscription (Id, NumeroInscription, TypeInscription, ParTransfert, DateInscription, DatePeremption, Renouvelable,
        EtatInfo, DateDiffusion) VALUES
        (1643101, 'B64310101', 'PRIV', 0, '20260101', NULL, 0, 'C', '20260101'),
        (1643102, 'B64310102', 'PRIV', 0, '20260101', '20300101', 0, 'C', '20260101'),
        (1643103, 'B64310103', 'PRIV', 0, '20260101', NULL, 0, 'C', '20260801'),
        (1643104, 'B64310104', 'PRIV', 0, '20250101', '20260301', 0, 'C', '20250101'),
        (1643105, 'B64310105', 'PRIV', 0, '20260101', NULL, 0, 'C', '20260101'),
        (1643106, 'B64310106', 'PRIV', 0, '20260101', NULL, 0, 'C', '20260101'),
        (1643107, 'B64310107', 'PRIV', 0, '20260101', NULL, 0, 'C', '20260101'),
        (1643108, 'B64310108', 'PRIV', 0, '20260101', NULL, 0, 'C', '20260101');
    SET IDENTITY_INSERT rec.Inscription OFF;
    INSERT INTO v2.DEBITEUR_END VALUES (1643101, 1643001, 1643101, 1643001), (1643102, 1643001, 1643102, 1643001),
        (1643103, 1643001, 1643103, 1643001), (1643104, 1643001, 1643104, 1643001), (1643105, 1643001, 1643105, 1643001),
        (1643106, 1643001, 1643106, 1643001), (1643107, 1643001, 1643107, 1643002), (1643108, 1643104, 1643108, 1643001);
    INSERT INTO v2.RADIATION_TOTALE VALUES (1643105, 1643105, '20260501'), (1643106, 1643106, '20260901');
    -- PC : REQUI ALPHA SARL une PC diffusable (bloque) ; DEUX PC deux PC non diffusables (bloque : « 2 PC ») ; NON DIFFUSABLE
    -- sans non-inscrit, CLOTURE ARRET (arret de diffusion), ETAT N : ne bloquent pas ; NON DIFFUSABLE NI (avec non-inscrit) bloque.
    INSERT INTO v2.CODE_CLOTURE VALUES ('CLBAN', 1);
    INSERT INTO v2.PROCEDURE_COLLECTIVE (PRCLL_ID_PRCLL, PRCLL_ID_DSRCS, PRCLL_NUM_PC, PRCLL_ID_RPRTR, PRCLL_NON_DIFFUSABLE, PRCLL_ID_NINSC,
        PRCLL_ETAT_PC, PRCLL_CODE_CLOTURE) VALUES
        (1643101, 1643001, '2026P64301', 1643001, 0, NULL, 'O', NULL),
        (1643102, 1643104, '2026P64302', 1643104, 1, NULL, 'O', NULL), (1643103, 1643104, '2026P64303', 1643104, 1, NULL, 'O', NULL),
        (1643104, 1643105, '2026P64304', 1643105, 1, NULL, 'O', NULL),
        (1643105, 1643106, '2026P64305', 1643106, 0, NULL, 'O', 'CLBAN'),
        (1643106, 1643107, '2026P64306', 1643107, 0, NULL, 'N', NULL),
        (1643107, 1643108, '2026P64307', 1643108, 1, 1643001, 'O', NULL);
    """);
RechercheEntreprises Requi(string raison = "", string siren = "", string numVoie = "", string adresse = "", string cp = "", string ville = "",
                           bool radies = false, int limite = 50) => new(raison, siren, numVoie, adresse, cp, ville, radies, limite);
await using (var cn = new SqlConnection(greffe.Connexion))
{
    await cn.OpenAsync();
    async Task<int[]> Ids(RechercheEntreprises r) => [.. (await LectureEntreprisesRequi.LireAsync(cn, r, CancellationToken.None)).Lignes.Select(e => e.Id)];
    Exiger((await Ids(Requi())).Length == 0, "requisitions : aucun critere cherchable, rien");
    var requi = await LectureEntreprisesRequi.LireAsync(cn, Requi(raison: "REQUI"), CancellationToken.None);
    Exiger(requi.Total == 1 && requi.Lignes is [{ Id: 1643001, Siren: "516430001", Gestion: "2011B64301", Designation: "REQUI ALPHA SARL", Personne: "PM",
                                                   FormeJuridique: "SARL", Adresse: "12 RUE DES BANCS", CodePostal: "80000", Ville: "AMIENS", Radie: false }],
        $"requisitions : la designation contient la raison sociale ; le radie ecarte, la personne physique non concernee ({string.Join(",", requi.Lignes.Select(e => e.Id))})");
    Exiger((await Ids(Requi(raison: "REQUI", radies: true))).SequenceEqual([1643001, 1643003]),
        "requisitions : « Inclure les supprimes » ajoute le radie (ordre de la designation)");
    var pp = await LectureEntreprisesRequi.LireAsync(cn, Requi(siren: "516 430 002"), CancellationToken.None);
    Exiger(pp.Lignes is [{ Id: 1643002, Designation: "BANCPP Paul", Personne: "PP" }] && (await Ids(Requi(siren: "51643000"))).Length == 0,
        "requisitions : SIREN egal, blancs retires (un SIREN partiel ne trouve rien) ; une personne physique par son nom et prenom");
    Exiger(requi.Lignes is [{ NbInscriptions: 3, ProcedureCollective: true }] && pp.Lignes is [{ NbInscriptions: 0, ProcedureCollective: false }],
        "requisitions : nombre d'inscriptions a la date de fraicheur ENDETT (3 : ni diffusee apres, ni perimee ni radiee avant, ni a "
        + $"une autre adresse) et PC diffusable qui bloque ; sans inscription ni PC : 0, aucune ({requi.Lignes.FirstOrDefault()}, {pp.Lignes.FirstOrDefault()})");
    var pcs = (await LectureEntreprisesRequi.LireAsync(cn, Requi(raison: "PCBANC"), CancellationToken.None)).Lignes
        .ToDictionary(e => e.Designation, e => (e.NbInscriptions, e.ProcedureCollective));
    Exiger(pcs.Count == 5 && pcs["PCBANC DEUX PC"] == (1, true) && pcs["PCBANC NON DIFFUSABLE"] == (0, false)
           && pcs["PCBANC CLOTURE ARRET"] == (0, false) && pcs["PCBANC ETAT N"] == (0, false) && pcs["PCBANC NON DIFFUSABLE NI"] == (0, true),
        "requisitions, VerifSiPC « ENTR » : deux PC bloquent ; une PC non diffusable sans non-inscrit, a code de cloture qui arrete la "
        + "diffusion ou a l'etat N ne bloque pas, non diffusable AVEC non-inscrit bloque ; sans adresse, les inscriptions a toute adresse ("
        + string.Join("; ", pcs.Select(kv => $"{kv.Key}={kv.Value}")) + ")");
    Exiger((await Ids(Requi(cp: "804"))).SequenceEqual([1643002]) && (await Ids(Requi(ville: "amien"))).SequenceEqual([1643001])
           && (await Ids(Requi(adresse: "BANCS", numVoie: "12"))).SequenceEqual([1643001]) && (await Ids(Requi(adresse: "BANCS", numVoie: "13"))).Length == 0,
        "requisitions : adresse du siege -- code postal commence par, ville et voie contiennent, numero de voie egal");
}
Exiger((await (await http.PostAsJsonAsync(RoutesEcransMain.Pour(RoutesRequisitions.Entreprises, Greffe), Requi(raison: "REQUI"), json))
        .Content.ReadFromJsonAsync<EntreprisesLues>(json)) is { Total: 1 }, "route des requisitions : les lignes de la lecture");
Exiger((await http.PostAsJsonAsync(RoutesEcransMain.Pour(RoutesRequisitions.Entreprises, GreffeAbsent), Requi(raison: "X"), json)).StatusCode == HttpStatusCode.NotFound,
    "route des requisitions : greffe absent -> 404, sans repli");
// La session de l'ecran, comme React la joue : un critere saisi, puis « Rechercher » (lit la base avant de ranger).
var rq = EcranRequisitions.Ouvrir();
async Task<VueRequisitions> GesteRequi(ClientServeur? serveur, string? code, string geste, object? valeur = null)
    => rq = await EcranRequisitions.AgirAsync(serveur, code, rq.Session, geste, JsonSerializer.SerializeToElement(valeur));
await GesteRequi(client, Greffe, "Critere", new { champ = "CritRaisonSociale", valeur = "REQUI" });
await GesteRequi(client, Greffe, "Rechercher");
Exiger(rq is { ModeResultats: "serveur", MessageResultats: SessionRequisitions.NoteBase, ARecherche: true, Interrompue: false }
       && rq.Trouvees is [{ Id: "1643001", Siren: "516 430 001", Gestion: "2011B64301", Designation: "REQUI ALPHA SARL", TypeAdresse: "Siege",
                            Adresse: "12 RUE DES BANCS, 80000 AMIENS", Type: "PM", Radie: false, NbInscriptions: 3 }]
       && rq.Trouvees[0].Infobulle.Contains("Procedure collective en cours"),
    "pont Requisitions branche : « Rechercher » lit les dossiers de la base (inscriptions et PC lues), la note dit ce qui n'est pas repris");
await GesteRequi(client, Greffe, "Critere", new { champ = "CritRaisonSociale", valeur = "" });
await GesteRequi(client, Greffe, "Critere", new { champ = "CritDirigeant", valeur = "DUPONT" });
await GesteRequi(client, Greffe, "Rechercher");
Exiger(rq is { ModeResultats: "serveur", MessageResultats: SessionRequisitions.NoteNonCherches, Trouvees.Length: 0 },
    "pont Requisitions : le dirigeant seul n'est pas cherche en base, la note le dit (aucune ligne)");
await GesteRequi(client, GreffeAbsent, "Critere", new { champ = "CritRaisonSociale", valeur = "REQUI" });
await GesteRequi(client, GreffeAbsent, "Critere", new { champ = "CritDirigeant", valeur = "" });
await GesteRequi(client, GreffeAbsent, "Rechercher");
Exiger(rq is { ModeResultats: "erreur", MessageResultats: not null, Trouvees.Length: 0 }, "pont Requisitions, greffe absent : le motif, AUCUNE ligne");
await GesteRequi(null, null, "Critere", new { champ = "CritRaisonSociale", valeur = "AMIENS" });
await GesteRequi(null, null, "Rechercher");
Exiger(rq is { ModeResultats: "demo", Trouvees.Length: > 0 }, "pont Requisitions sans serveur : la demonstration");
EcranRequisitions.Fermer(rq.Session);

// ============================================================ 10. DOSSIERS VERROUILLES (LectureVerrous.cs, lot 9)
// « Dossiers verrouillés » (PROC_VERROUS), branche au lot 9 du plan : les demandes qui verrouillent leurs objets, la regle de
// RIG v2 (Demande.GetOldLock, Demande.cs:1290-1316 : en cours, ou etat W, I, N, ET un objet lie par DOSSIER_DEMANDE). La demande
// 1530203 du jeu des depots (en cours, liee) en fait partie : les controles portent sur les demandes 19095xx. Identifiants du
// lot 9 : 1 900 000 a 1 999 999.
await Sql("ALTER TABLE v2.DEMANDE ADD DMND_NOM_UTILISATEUR varchar(255) NULL;");
// 1909501 en cours, liee ; 1909502 interrompue (I), liee ; 1909503 en reclamation (N), liee, tenue par « banc.moi » ; 1909506 en
// attente (W, sans libelle dans ETAT_DEMANDE), liee, « Agent.Tiers ». Jumeaux refuses : 1909504 en cours SANS objet lie (de
// 1909501), 1909505 validee, liee (de 1909502).
await Sql("""
    INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_NOM, DMND_NUM_INDICATIF, DMND_ETAT_DEMANDE, DMND_EN_COURS, DMND_CODE_PROSS,
        DMND_DATE_CREATION, DMND_NOM_UTILISATEUR) VALUES
        (1909501, 'D1909501', 'Instance - VERROU ALPHA', 'B9V01', 'Q', 1, 'MJUD', '20261101', 'agent.tiers'),
        (1909502, 'D1909502', 'Instance - VERROU BETA', 'B9V02', 'I', 0, 'MPC', '20261102', 'agent.tiers'),
        (1909503, 'D1909503', 'Instance - VERROU GAMMA', 'B9V03', 'N', 0, 'MJUD', '20261103', 'banc.moi'),
        (1909504, 'D1909504', 'Instance - VERROU SANS OBJET', 'B9V04', 'Q', 1, 'MJUD', '20261101', 'agent.tiers'),
        (1909505, 'D1909505', 'Instance - VERROU VALIDE', 'B9V05', 'V', 0, 'MJUD', '20261101', 'agent.tiers'),
        (1909506, 'D1909506', 'Instance - VERROU DELTA', NULL, 'W', 0, 'MJUD', '20261104', 'Agent.Tiers');
    INSERT INTO v2.DOSSIER_DEMANDE VALUES (1909551, 1909501, '2J', '1909601'), (1909552, 1909502, '2J', '1909602'),
        (1909553, 1909503, '2J', '1909603'), (1909555, 1909505, '2J', '1909605'), (1909556, 1909506, '2J', '1909606');
    """);
await using (var cn = new SqlConnection(greffe.Connexion))
{
    await cn.OpenAsync();
    var verrous = await LectureVerrous.LireAsync(cn, CancellationToken.None);
    var miens = verrous.Lignes.Where(v => v.Demande.StartsWith("D19095", StringComparison.Ordinal)).ToArray();
    Exiger(miens.Select(v => v.Demande).SequenceEqual(["D1909501", "D1909502", "D1909503", "D1909506"]) && verrous.Total == verrous.Lignes.Length,
        $"verrous : en cours ou W/I/N ET un objet lie, du plus ancien au plus recent ; 1909504 (sans objet) et 1909505 (validee) ecartees ({string.Join(",", miens.Select(v => v.Demande))})");
    Exiger(miens[0] is { Dossier: "B9V01", Etat: "En cours", Utilisateur: "agent.tiers", Traitement: "MJUD" } && miens[0].Creation == new DateTime(2026, 11, 1)
           && miens[1].Etat == "Interrompue" && miens[2].Etat == "Réclamation" && miens[3] is { Etat: "W", Dossier: "" },
        "verrous : « En cours » si DMND_EN_COURS, sinon le libelle de ETAT_DEMANDE (le code s'il n'en a pas) ; qui la tient : DMND_NOM_UTILISATEUR");
    var un = await LectureVerrous.LireAsync(cn, CancellationToken.None, plafond: 1);
    Exiger(un.Lignes.Length == 1 && un.Total == verrous.Total, $"verrous : plafond 1 -> une ligne, total {verrous.Total} ({un.Total})");
}
var verrousRoute = await http.GetFromJsonAsync<VerrousLus>(RoutesEcransMain.Pour(RoutesVerrous.Verrous, Greffe), json);
Exiger(verrousRoute is { Total: > 0 } && verrousRoute.Lignes.Any(v => v.Demande == "D1909503"), "route des verrous : les lignes de la lecture");
Exiger((await http.GetAsync(RoutesEcransMain.Pour(RoutesVerrous.Verrous, GreffeAbsent))).StatusCode == HttpStatusCode.NotFound,
    "route des verrous : greffe absent -> 404, sans repli");
var jourVerrous = new DateTime(2026, 11, 27);
var autres = await EcranVerrous.VerrouillesAsync(client, Greffe, "agent.tiers", false, jourVerrous);
var miensPont = autres.Lignes.Where(l => l.Nom.Contains("VERROU", StringComparison.Ordinal)).ToArray();
Exiger(autres.Mode == "serveur" && miensPont.Length == 1 && miensPont[0] is { Dossier: "B9V03", Utilisateur: "banc.moi", Statut: "alerte", Age: 24, Etat: "Réclamation · MJUD (D1909503)" }
       && !autres.Lignes.Any(l => EcranVerrous.Exemple("R. VILAIN", true).Lignes.Any(e => e.Dossier == l.Dossier)),
    $"pont Verrous branche : les demandes d'un AUTRE agent (« agent.tiers » ecarte, « Agent.Tiers » aussi : sans casse), aucune de la demonstration ({string.Join(",", miensPont.Select(l => l.Dossier))})");
var tous = await EcranVerrous.VerrouillesAsync(client, Greffe, "agent.tiers", true, jourVerrous);
var tousMiens = tous.Lignes.Where(l => l.Nom.Contains("VERROU", StringComparison.Ordinal)).ToArray();
Exiger(tousMiens.Select(l => l.Dossier).SequenceEqual(["B9V01", "B9V02", "B9V03", "D1909506"]) && tousMiens[0] is { Statut: "attente", Age: 26 }
       && tous.Detenteurs.Any(d => d is { Utilisateur: "agent.tiers", Nombre: 2 }),
    "pont Verrous, mes dossiers compris : les quatre ; sans numero de dossier, le numero de demande ; detenteurs par nom");
Exiger((await EcranVerrous.VerrouillesAsync(null, null, "R. VILAIN", false, jourVerrous)) is { Mode: "demo", Lignes.Length: > 0 },
    "pont Verrous sans serveur : la demonstration, mode « demo »");
Exiger((await EcranVerrous.VerrouillesAsync(client, GreffeAbsent, "R. VILAIN", false, jourVerrous)) is { Mode: "erreur", Lignes.Length: 0, Message: not null },
    "pont Verrous, greffe absent : le motif, AUCUNE ligne");

// ============================================================ 11. REDACTION D'UNE DECISION (LectureRedaction.cs, lot 10)
// « Rédaction d'une décision » (PROC_REDA), branchee au lot 10 du plan : les decisions des instances des series JRCT, JRPC, JRREF
// (PROC_REDA.InitParam), sans date d'abord puis « date ou date prevue » decroissante puis id decroissant (PROC_REDA.cs:106-110) ;
// les listes par leur libelle (le code sans libelle) ; le titre des appels (AppelAffaire.TitreSimplifieREDA, :162-172) ; le
// nombre de documents VIDIGED_DJ ; les mesures (MESURE.MSINS_ID_DCJDC, titrees comme DecisionJudiciaire.cs:167-174). Aucune
// decision avant cette section : toutes sont du jeu. Identifiants du lot 10 : 2 000 000 a 2 099 999.
await Sql("""
    CREATE TABLE v2.DECISION_JUDICIAIRE (DCJDC_ID_DCJDC int NOT NULL PRIMARY KEY, DCJDC_ID_INSTN int NOT NULL, DCJDC_TYPE_DECISION varchar(10) NULL,
        DCJDC_SENS_GENERAL varchar(5) NULL, DCJDC_RESSORT varchar(1) NULL, DCJDC_AVANT_DIRE_DROIT varchar(1) NULL, DCJDC_CONTRADICTOIRE varchar(1) NULL,
        DCJDC_EXECUTION_PROVISOIRE varchar(1) NOT NULL DEFAULT ('0'), DCJDC_DATE datetime NULL, DCJDC_DATE_PREVUE datetime NULL,
        DCJDC_DECISION_PUBLIQUE bit NOT NULL DEFAULT (0), DCJDC_DEBAT_PUBLIC bit NOT NULL DEFAULT (0),
        DCJDC_ID_APPAF_DELIBERER int NULL, DCJDC_ID_APPAF_PRONONCER int NULL);
    CREATE TABLE v2.TYPE_DECISION (R_TYPEDECISI_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_TYPEDECISI_LIBELLE_ECRAN varchar(100) NOT NULL,
        R_TYPEDECISI_TYPE_MESURE_LIE varchar(5) NULL);
    CREATE TABLE v2.SENS_GENERAL (R_SENSGENERA_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_SENSGENERA_LIBELLE_ECRAN varchar(35) NOT NULL);
    CREATE TABLE v2.RESSORT (R_RESSORT_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_RESSORT_LIBELLE_ECRAN varchar(35) NOT NULL);
    CREATE TABLE v2.AVANT_DIRE_DROIT (R_AVANTDIRED_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_AVANTDIRED_LIBELLE_ECRAN varchar(35) NOT NULL);
    CREATE TABLE v2.CONTRADICTOIRE (R_CONTRADICT_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_CONTRADICT_LIBELLE_ECRAN varchar(35) NOT NULL);
    CREATE TABLE v2.EXECUTION_PROVISOIRE (R_EXECUTIONP_VALEUR varchar(1) NOT NULL PRIMARY KEY, R_EXECUTIONP_LIBELLE_ECRAN varchar(35) NOT NULL);
    CREATE TABLE v2.VIDIGED_DJ (VGDDJ_ID_VGDDJ int NOT NULL PRIMARY KEY, VGDDJ_ID_DCJDC int NULL);
    CREATE TABLE v2.MESURE (MSINS_ID_MSINS int NOT NULL PRIMARY KEY, MSINS_ID_DCJDC int NULL, MSINS_NUM_MESURE varchar(11) NULL, MSINS_NUM_ORDRE int NULL);
    """);
// Instances : 2000101 JRCT, 2000102 JRREF, 2000103 JRPC (acceptees) ; jumeaux refuses : 2000104 JROP, 2000105 JRPR (prevention,
// mode « PR » non repris). Decisions de 2000101 : 2000202 sans date (en tete), 2000203 date prevue seule (01/04), 2000204 et
// 2000201 meme date (06/03 : la plus grande d'abord) ; puis 2000205 (2025), 2000208 (2024). 2000206 et 2000207 : refusees.
await Sql("""
    INSERT INTO v2.CODE_AFFAIRE (R_CODEAFFAIR_VALEUR, R_CODEAFFAIR_TYPE_NUM_ROLE) VALUES ('CT10', 'JRCT'), ('RF10', 'JRREF'), ('PC10', 'JRPC'),
        ('OP10', 'JROP'), ('PR10', 'JRPR');
    INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT, INSTN_CODE_AFFAIRE) VALUES
        (2000101, '2026J10101', 'REDA ALPHA - ouverture', 'CT10'), (2000102, '2025R10102', 'REDA BETA - refere', 'RF10'),
        (2000103, '2024J10103', 'REDA GAMMA - PC', 'PC10'), (2000104, '2026J10104', 'REDA OPPOSITION', 'OP10'),
        (2000105, '2026P10105', 'REDA PREVENTION', 'PR10');
    INSERT INTO v2.TYPE_DECISION VALUES ('TD10E', 'Jugement avec expertise', 'EXP'), ('TD10S', 'Jugement simple', NULL);
    INSERT INTO v2.SENS_GENERAL VALUES ('F10', 'Favorable du banc');
    INSERT INTO v2.RESSORT VALUES ('1', 'Premier ressort du banc');
    INSERT INTO v2.AVANT_DIRE_DROIT VALUES ('0', 'Non (banc)');
    INSERT INTO v2.CONTRADICTOIRE VALUES ('1', 'Contradictoire (banc)');
    INSERT INTO v2.EXECUTION_PROVISOIRE VALUES ('1', 'De droit (banc)');
    INSERT INTO v2.AUDIENCE_CABINET (AUDNC_ID_ADNC, AUDNC_AUDIENCE, AUDNC_TYPE_AUD_CAB, AUDNC_DATE, AUDNC_HEURE, AUDNC_PUBLIQUE, AUDNC_CHAMBRE,
        AUDNC_NON_DISPONIBLE_WEB) VALUES (2000401, 1, 'AUD', '19900220', '14:00', 1, '2', 0), (2000402, 1, 'AUD', '19900101', NULL, 1, NULL, 0);
    INSERT INTO v2.APPEL_AFFAIRE (APPAF_ID_APPAF, APPAF_ID_ADNC) VALUES (2000301, 2000401), (2000302, NULL), (2000303, 2000402);
    INSERT INTO v2.DECISION_JUDICIAIRE (DCJDC_ID_DCJDC, DCJDC_ID_INSTN, DCJDC_TYPE_DECISION, DCJDC_SENS_GENERAL, DCJDC_RESSORT, DCJDC_AVANT_DIRE_DROIT,
        DCJDC_CONTRADICTOIRE, DCJDC_EXECUTION_PROVISOIRE, DCJDC_DATE, DCJDC_DATE_PREVUE, DCJDC_DECISION_PUBLIQUE, DCJDC_DEBAT_PUBLIC,
        DCJDC_ID_APPAF_DELIBERER, DCJDC_ID_APPAF_PRONONCER) VALUES
        (2000201, 2000101, 'TD10E', 'F10', '1', '0', '1', '1', '20260306', '20260305', 0, 1, 2000301, NULL),
        (2000202, 2000101, NULL, NULL, NULL, NULL, NULL, '0', NULL, NULL, 0, 0, NULL, NULL),
        (2000203, 2000101, 'TD10S', NULL, NULL, NULL, NULL, '0', NULL, '20260401', 0, 0, NULL, NULL),
        (2000204, 2000101, 'TD10S', NULL, '9', NULL, NULL, '0', '20260306', NULL, 1, 0, NULL, 2000302),
        (2000205, 2000102, 'TD10S', NULL, NULL, NULL, NULL, '0', '20250101', NULL, 0, 0, NULL, 2000303),
        (2000206, 2000104, 'TD10S', NULL, NULL, NULL, NULL, '0', '20260310', NULL, 0, 0, NULL, NULL),
        (2000207, 2000105, 'TD10S', NULL, NULL, NULL, NULL, '0', '20260310', NULL, 0, 0, NULL, NULL),
        (2000208, 2000103, 'TD10S', NULL, NULL, NULL, NULL, '0', '20240601', NULL, 0, 0, NULL, NULL);
    INSERT INTO v2.VIDIGED_DJ VALUES (2000501, 2000201), (2000502, 2000201), (2000503, 2000205), (2000504, NULL);
    INSERT INTO v2.MESURE VALUES (2000601, 2000201, '26EXP00114', 2), (2000602, 2000201, NULL, 1), (2000603, 2000206, '26EXP00999', 1);
    """);
await using (var cn = new SqlConnection(greffe.Connexion))
{
    await cn.OpenAsync();
    var reda = await LectureRedaction.LireAsync(cn, CancellationToken.None);
    Exiger(reda.Lignes.Select(d => d.Id).SequenceEqual([2000202, 2000203, 2000204, 2000201, 2000205, 2000208]) && reda.Total == 6,
        "redaction : series JRCT, JRREF, JRPC (JROP et JRPR ecartees) ; sans date d'abord, puis date ou date prevue decroissante, puis id "
        + $"decroissant ({string.Join(",", reda.Lignes.Select(d => d.Id))}, total {reda.Total})");
    var alpha = reda.Lignes.Single(d => d.Id == 2000201);
    Exiger(alpha is { Role: "2026J10101", Affaire: "REDA ALPHA - ouverture", TypeDecision: "Jugement avec expertise", SensGeneral: "Favorable du banc",
                      Ressort: "Premier ressort du banc", AvantDireDroit: "Non (banc)", Contradictoire: "Contradictoire (banc)",
                      ExecutionProvisoire: "De droit (banc)", DebatsPublics: true, DecisionPublique: false, MesureInstruction: true, Documents: 2,
                      MiseEnDelibere: "20/02/1990 14:00 - Chambre 2", Prononce: "" }
           && alpha.Date == new DateTime(2026, 3, 6) && alpha.DatePrevue == new DateTime(2026, 3, 5)
           && alpha.Mesures.SequenceEqual(["Expertise en attente de numérotation...", "Expertise n°26EXP00114"]),
        $"redaction : listes par leur libelle, cases, mesure liee au type, 2 documents en GED, appel delibere titre (date, heure, chambre), "
        + $"mesures par numero d'ordre ({alpha})");
    var sansAudience = reda.Lignes.Single(d => d.Id == 2000204);
    var sansRien = reda.Lignes.Single(d => d.Id == 2000202);
    Exiger(sansAudience is { Prononce: LectureRedaction.AppelSansAudience, MiseEnDelibere: "", Ressort: "9", MesureInstruction: false, Documents: 0, Mesures.Length: 0 }
           && reda.Lignes.Single(d => d.Id == 2000205) is { Prononce: "01/01/1990", Documents: 1 }
           && sansRien is { TypeDecision: "", Ressort: "", ExecutionProvisoire: "0", MiseEnDelibere: "", Prononce: "" },
        "redaction : appel sans audience -> le titre de RIG v2 ; audience sans heure ni chambre -> la date seule ; code sans libelle -> le code ; "
        + "sans appel -> vide");
    var redaDeux = await LectureRedaction.LireAsync(cn, CancellationToken.None, plafond: 2);
    Exiger(redaDeux.Lignes.Length == 2 && redaDeux.Total == 6, $"redaction : plafond 2 -> deux decisions, total 6 ({redaDeux.Lignes.Length}, {redaDeux.Total})");
}
var redaRoute = await http.GetFromJsonAsync<DecisionsRedactionLues>(RoutesEcransMain.Pour(RoutesRedaction.Decisions, Greffe), json);
Exiger(redaRoute is { Total: 6 } && redaRoute.Lignes.Any(d => d.Id == 2000201), "route de la redaction : les decisions de la lecture");
Exiger((await http.GetAsync(RoutesEcransMain.Pour(RoutesRedaction.Decisions, GreffeAbsent))).StatusCode == HttpStatusCode.NotFound,
    "route de la redaction : greffe absent -> 404, sans repli");
var redaPont = await EcranRedaction.DecisionsAsync(client, Greffe);
var exemple = EcranRedaction.Exemple();
// fix-ok: le pont perdait le total lu (Pont/EcranRedactionBase.cs construisait VueRedaction sans v.Total), l'ecran ne pouvait pas
// dire « 500 affichees sur 689 » (route de dev : total 689, 500 lignes) ; Total == 6 ci-dessous le garde.
Exiger(redaPont.Mode == "serveur" && redaPont.Decisions.Length == 6 && redaPont.Total == 6
       && redaPont.Decisions.Single(d => d.Id == 2000201) is { Numero: "2026J10101", Delibere: true, Date: "06/03/2026", DatePrevue: "05/03/2026" }
       && redaPont.Decisions.Single(d => d.Id == 2000204) is { Delibere: false, Date: "06/03/2026", DatePrevue: "" }
       && !redaPont.Decisions.Any(d => exemple.Decisions.Any(e => e.Numero == d.Numero)),
    $"pont Redaction branche : les decisions de la base (deliberee tant qu'aucun appel prononce, dates jj/mm/aaaa), leur total "
    + $"(l'ecran dit « n affichees sur total » au plafond : {redaPont.Total}), aucune de la demonstration");
Exiger((await EcranRedaction.DecisionsAsync(null, null)) is { Mode: "demo", Decisions.Length: 4 },
    "pont Redaction sans serveur : la demonstration, mode « demo »");
Exiger((await EcranRedaction.DecisionsAsync(client, GreffeAbsent)) is { Mode: "erreur", Decisions.Length: 0, Message: not null },
    "pont Redaction, greffe absent : le motif, AUCUNE decision");

// ============================================================ 12. STATISTIQUES (LectureStatistiques.cs, lot 9)
// « Statistiques » (PROC_STAT), calcule au lot 9 : les requetes de RIG v2 (Outils\DLLEdition\RigRapport\Jud\Stats) sur une
// periode ISOLEE, mars 1971, ou aucun autre jeu n'a de ligne. Chaque compte a son jumeau ecarte (hors periode, exterieure,
// autre motif de role, autre type de decision). Les tables du jeu sont posees ici aux types de RIG_DEV. Identifiants 19110xx.
await Sql("""
    IF COL_LENGTH('v2.INSTANCE', 'INSTN_CODE_ACTE_PROCE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_CODE_ACTE_PROCE varchar(10) NULL;
    IF OBJECT_ID(N'v2.DECISION_JUDICIAIRE') IS NULL
        CREATE TABLE v2.DECISION_JUDICIAIRE (DCJDC_ID_DCJDC int NOT NULL PRIMARY KEY, DCJDC_ID_INSTN int NULL);
    IF COL_LENGTH('v2.DECISION_JUDICIAIRE', 'DCJDC_TYPE_DECISION') IS NULL ALTER TABLE v2.DECISION_JUDICIAIRE ADD DCJDC_TYPE_DECISION varchar(10) NULL;
    IF COL_LENGTH('v2.DECISION_JUDICIAIRE', 'DCJDC_ID_APPAF_PRONONCER') IS NULL ALTER TABLE v2.DECISION_JUDICIAIRE ADD DCJDC_ID_APPAF_PRONONCER int NULL;
    IF COL_LENGTH('v2.DECISION_JUDICIAIRE', 'DCJDC_ID_APPAF_DELIBERER') IS NULL ALTER TABLE v2.DECISION_JUDICIAIRE ADD DCJDC_ID_APPAF_DELIBERER int NULL;
    IF COL_LENGTH('v2.DECISION_JUDICIAIRE', 'DCJDC_DATE') IS NULL ALTER TABLE v2.DECISION_JUDICIAIRE ADD DCJDC_DATE datetime NULL;
    IF COL_LENGTH('v2.DECISION_JUDICIAIRE', 'DCJDC_DATE_PREVUE') IS NULL ALTER TABLE v2.DECISION_JUDICIAIRE ADD DCJDC_DATE_PREVUE datetime NULL;
    IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_ID_INSTN') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_ID_INSTN int NULL;
    IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_SURVENU') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_SURVENU bit NULL;
    IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_EXTERIEUR') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_EXTERIEUR bit NULL;
    IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_DATE_OUVERTURE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_DATE_OUVERTURE datetime NULL;
    IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_DATE_SAUVEGARDE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_DATE_SAUVEGARDE datetime NULL;
    IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_CODE_SOLUTION') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_CODE_SOLUTION varchar(10) NULL;
    IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_DATE_SOLUTION') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_DATE_SOLUTION datetime NULL;
    IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_CODE_SOLUTION_2') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_CODE_SOLUTION_2 varchar(10) NULL;
    IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_DATE_SOLUTION_2') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_DATE_SOLUTION_2 datetime NULL;
    IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_REGIME_PC') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_REGIME_PC varchar(2) NULL;
    IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_CODE_CLOTURE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_CODE_CLOTURE varchar(5) NULL;
    IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_DATE_CLOTURE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_DATE_CLOTURE datetime NULL;
    IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_CODE_ACTE_PROCE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_CODE_ACTE_PROCE varchar(10) NULL;
    IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_DATE_RETABL_PROF') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_DATE_RETABL_PROF datetime NULL;
    IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_DATE_TRAITEMENT_SORTIE_CRISE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_DATE_TRAITEMENT_SORTIE_CRISE datetime NULL;
    IF COL_LENGTH('v2.ETABLISSEMENT_PC', 'ETBPC_CODE_NAF') IS NULL ALTER TABLE v2.ETABLISSEMENT_PC ADD ETBPC_CODE_NAF varchar(10) NULL;
    CREATE TABLE v2.CHIFFRES_PC (CHFPC_ID_CHFPC int NOT NULL PRIMARY KEY, CHFPC_ID_PRCLL int NULL, CHFPC_SALARIES_OUVERTURE int NULL,
        CHFPC_SALARIES_PLAN int NULL, CHFPC_CHIFFRE_AFFAIRE money NULL, CHFPC_DECLARE_TOTAL money NULL, CHFPC_ACTIF_DECLARE money NULL);
    CREATE TABLE v2.CODE_ACTE_PROCE (R_CODEACTEPR_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_CODEACTEPR_SYNO_AMITEL varchar(10) NULL);
    -- CODE_CLOTURE est posee par la section 9 (requisitions) sans son libelle.
    IF OBJECT_ID(N'v2.CODE_CLOTURE') IS NULL CREATE TABLE v2.CODE_CLOTURE (R_CODECLOTUR_VALEUR varchar(5) NOT NULL PRIMARY KEY);
    IF COL_LENGTH('v2.CODE_CLOTURE', 'R_CODECLOTUR_LIBELLE_ECRAN') IS NULL ALTER TABLE v2.CODE_CLOTURE ADD R_CODECLOTUR_LIBELLE_ECRAN varchar(70) NULL;
    CREATE TABLE v2.SOUS_RUBRIQUE_NAF (SRNAF_CODE varchar(10) NOT NULL PRIMARY KEY, SRNAF_LIBELLE varchar(255) NULL);
    CREATE TABLE v2.EQUIV_NAF (EQNAF_NIV_5 varchar(10) NOT NULL PRIMARY KEY, EQNAF_NIV_1 varchar(10) NULL, EQNAF_NIV_2 varchar(10) NULL,
        EQNAF_NIV_3 varchar(10) NULL, EQNAF_NIV_4 varchar(10) NULL);
    """);
// Instances : 1911001 F et 1911002 J enrolees dans la periode (la fin a 0 h comprise, comme l'ecran V2) ; 1911003 R ; 1911004 J
// enrolee le 1er avril (jumeau ecarte) ; 1911005 IP ; 1911006 OP ; 1911007 JC (repartitions) ; 1911008 IP enrolee en 1970.
// Decisions (audience de prononce, MetierStats.cs:190-229) : 1911101 JF sur 1911001, prononce <> delibere -> apres instruction ;
// 1911102 JF sur 1911002, prononce = delibere -> sur siege ; 1911103 « XX » sur 1911002 -> autres ; 1911104 JF sur 1911001 en
// avril (jumeau) ; 1911105 sur 1911003 (R) -> refere ; 1911106 sur 1911005, prevue le 12 (sans date rendue) -> IP ; 1911107 sur
// 1911006 -> ordonnance sur requete ; 1911108 sur 1911007 -> JC. Reglages : ceux de RIG_DEV ('JF','ZIND' et 'JF').
await Sql("""
    INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('STAT_TYPE_DECISION_JUGEMENTS_INSTRUCTION', 'banc', '''JF'',''ZIND'''),
        ('STAT_TYPE_DECISION_JUGEMENTS_SIEGE', 'banc', '''JF''');
    INSERT INTO v2.NATURE_AFFAIRE (R_NATUREAFFA_VALEUR, R_NATUREAFFA_LIBELLE_ECRAN)
        SELECT v, l FROM (VALUES ('9S1', 'Nature banc 9 un'), ('9S2', 'Nature banc 9 deux')) AS n (v, l)
        WHERE NOT EXISTS (SELECT 1 FROM v2.NATURE_AFFAIRE AS x WHERE x.R_NATUREAFFA_VALEUR = n.v);
    INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_DATE_ENROLEMENT, INSTN_NATURE_AFFAIRE, INSTN_CODE_ACTE_PROCE) VALUES
        (1911001, '1971F00001', '19710310', '40a', 'B9ASS'), (1911002, '1971J00002', '19710331', NULL, NULL),
        (1911003, '1971R00003', '19710315', NULL, NULL), (1911004, '1971J00004', '19710401', NULL, NULL),
        (1911005, '1971IP0005', '19710305', NULL, NULL), (1911006, '1971OP0006', '19700101', '9S2', NULL),
        (1911007, '1971JC0007', '19700101', '9S1', NULL), (1911008, '1970IP0008', '19700101', NULL, NULL);
    INSERT INTO v2.AUDIENCE_CABINET (AUDNC_ID_ADNC, AUDNC_AUDIENCE, AUDNC_TYPE_AUD_CAB, AUDNC_DATE, AUDNC_PUBLIQUE, AUDNC_NON_DISPONIBLE_WEB) VALUES
        (1911201, 1, 'B9', '19710320', 1, 0), (1911202, 1, 'B9', '19710301', 1, 0), (1911203, 1, 'B9', '19710321', 1, 0),
        (1911204, 1, 'B9', '19710322', 1, 0), (1911205, 1, 'B9', '19710402', 1, 0), (1911206, 1, 'B9', '19710325', 1, 0);
    INSERT INTO v2.APPEL_AFFAIRE (APPAF_ID_APPAF, APPAF_ID_ADNC) VALUES
        (1911301, 1911201), (1911302, 1911202), (1911303, 1911203), (1911304, 1911204), (1911305, 1911205), (1911306, 1911206);
    INSERT INTO v2.DECISION_JUDICIAIRE (DCJDC_ID_DCJDC, DCJDC_ID_INSTN, DCJDC_TYPE_DECISION, DCJDC_ID_APPAF_PRONONCER, DCJDC_ID_APPAF_DELIBERER,
        DCJDC_DATE, DCJDC_DATE_PREVUE) VALUES
        (1911101, 1911001, 'JF', 1911301, 1911302, NULL, NULL), (1911102, 1911002, 'JF', 1911303, 1911303, NULL, NULL),
        (1911103, 1911002, 'XX', 1911304, 1911302, NULL, NULL), (1911104, 1911001, 'JF', 1911305, 1911302, NULL, NULL),
        (1911105, 1911003, 'ORD', 1911306, 1911306, NULL, NULL), (1911106, 1911005, NULL, NULL, NULL, NULL, '19710312'),
        (1911107, 1911006, NULL, NULL, NULL, '19710303', NULL), (1911108, 1911007, NULL, NULL, NULL, '19710305', NULL);
    """);
// Procedures collectives (MetierStats.cs:262-587) : 1911401 ouverte le 5 mars, assignation (B9ASS -> ASS) -> RJ ; 1911402
// liquidation (LIJI) le 10 mars, regime 5 -> LJ et LJ simplifiee ; 1911403 sauvegarde le 8 puis ouverture le 20, saisine du
// parquet (B9SORP -> SORP) -> sauvegarde et conversion en RJ ; 1911404 EXTERIEURE ouverte le 6 (jumeau de 1911401, ecartee
// partout) ; 1911405 cession (CS1) le 25 puis LJ en solution 2 le 26 -> plan de cession et conversion en LJ ; 1911406 close le
// 31 a 15 h -- comptee par les clotures internes (la fin portee a 23:59:59, :606-612), PAS par celles du volet web (la fin a 0 h,
// _RequeteWebClotListe :1661-1671) ; 1911409 close le 15 (comptee par les deux) ; 1911407 close le 1er avril (jumeau) ; 1911408
// retablissement professionnel le 12.
// Chiffres : 1911401 10 salaries, CA 100,50, passif 2000, actif 500 ; 1911402 3 salaries, CA 800 ; 1911403 5 salaries, CA 20,
// passif 300 ; 1911405 7 salaries repris dans le plan. Etablissements (activites) : 1911401 code NAF 4711A (G « Commerce ») ;
// 1911402 un code absent d'EQUIV_NAF (« Sans activité ou activité inconnue ») ; 1911403 sans etablissement (ecarte, :215).
// Evenements : DESRJ sur 1911001 (dessaisissement), ICAUSEFIN et IPEXECUT sur 1911005 ; IPEXECUT sur 1911008 en avril (jumeau).
await Sql("""
    INSERT INTO v2.CODE_ACTE_PROCE (R_CODEACTEPR_VALEUR, R_CODEACTEPR_SYNO_AMITEL) VALUES ('B9ASS', 'ASS'), ('B9SORP', 'SORP');
    INSERT INTO v2.CODE_CLOTURE (R_CODECLOTUR_VALEUR, R_CODECLOTUR_LIBELLE_ECRAN) VALUES ('B9CIA', 'Insuffisance d''actif (banc 9)');
    INSERT INTO v2.PROCEDURE_COLLECTIVE (PRCLL_ID_PRCLL, PRCLL_NUM_PC, PRCLL_EXTERIEUR, PRCLL_DATE_OUVERTURE, PRCLL_DATE_SAUVEGARDE, PRCLL_CODE_SOLUTION,
        PRCLL_DATE_SOLUTION, PRCLL_CODE_SOLUTION_2, PRCLL_DATE_SOLUTION_2, PRCLL_REGIME_PC, PRCLL_CODE_CLOTURE, PRCLL_DATE_CLOTURE, PRCLL_CODE_ACTE_PROCE,
        PRCLL_DATE_RETABL_PROF) VALUES
        (1911401, '1971P11401', 0, '19710305', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'B9ASS', NULL),
        (1911402, '1971P11402', 0, '19701201', NULL, 'LIJI', '19710310', NULL, NULL, '5', NULL, NULL, NULL, NULL),
        (1911403, '1971P11403', 0, '19710320', '19710308', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'B9SORP', NULL),
        (1911404, '1971P11404', 1, '19710306', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'B9ASS', NULL),
        (1911405, '1971P11405', 0, '19600101', NULL, 'CS1', '19710325', 'LJ', '19710326', NULL, NULL, NULL, NULL, NULL),
        (1911406, '1971P11406', 0, '19600101', NULL, NULL, NULL, NULL, NULL, NULL, 'B9CIA', '19710331 15:00', NULL, NULL),
        (1911407, '1971P11407', 0, '19600101', NULL, NULL, NULL, NULL, NULL, NULL, 'B9CIA', '19710401', NULL, NULL),
        (1911408, '1971P11408', 0, '19500101', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, '19710312'),
        (1911409, '1971P11409', 0, '19600101', NULL, NULL, NULL, NULL, NULL, NULL, 'B9CIA', '19710315', NULL, NULL);
    INSERT INTO v2.CHIFFRES_PC (CHFPC_ID_CHFPC, CHFPC_ID_PRCLL, CHFPC_SALARIES_OUVERTURE, CHFPC_SALARIES_PLAN, CHFPC_CHIFFRE_AFFAIRE, CHFPC_DECLARE_TOTAL,
        CHFPC_ACTIF_DECLARE) VALUES
        (1911501, 1911401, 10, NULL, 100.50, 2000, 500), (1911502, 1911402, 3, NULL, 800, NULL, NULL),
        (1911503, 1911403, 5, NULL, 20, 300, NULL), (1911505, 1911405, NULL, 7, NULL, NULL, NULL);
    INSERT INTO v2.ETABLISSEMENT_PC (ETBPC_ID_ETBPC, ETBPC_ID_PRCLL, ETBPC_NUM_ORDRE, ETBPC_CODE_NAF) VALUES
        (1911601, 1911401, 1, '4711A'), (1911602, 1911402, 1, 'B9ZZZ');
    INSERT INTO v2.EQUIV_NAF (EQNAF_NIV_5, EQNAF_NIV_1, EQNAF_NIV_2, EQNAF_NIV_3, EQNAF_NIV_4) VALUES ('4711A', 'G', '47', '47.1', '47.11');
    INSERT INTO v2.SOUS_RUBRIQUE_NAF (SRNAF_CODE, SRNAF_LIBELLE) VALUES ('G', 'Commerce');
    INSERT INTO v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR, EVNPR_ID_INSTN, EVNPR_CODE_EVE_PRO, EVNPR_DATE) VALUES
        (1911701, 1911001, 'DESRJ', '19710311'), (1911702, 1911005, 'ICAUSEFIN', '19710315'), (1911703, 1911005, 'IPEXECUT', '19710316'),
        (1911704, 1911008, 'IPEXECUT', '19710410');
    """);
var mars1971 = (Debut: new DateTime(1971, 3, 1), Fin: new DateTime(1971, 3, 31));
string Rendu(StatistiquesLues s) => string.Join(" || ", s.Tableaux.Select(t =>
    $"{t.Titre}{(t.Cause is null ? "" : $" [{t.Cause}]")} : " + string.Join(" ; ", t.Lignes.Select(l => $"{l.Libelle}={string.Join("/", l.Valeurs)}"))));
await using (var cn = new SqlConnection(greffe.Connexion))
{
    await cn.OpenAsync();
    Task<StatistiquesLues> Volet(string v, string? niveau = null)
        => LectureStatistiques.LireAsync(cn, new RechercheStatistiques(v, mars1971.Debut, mars1971.Fin, niveau), CancellationToken.None);

    var internes = await Volet(VoletsStatistiques.Internes);
    var vu = Rendu(internes);
    Exiger(vu.StartsWith("Statistiques judiciaires : Affaires enrôlées=2 ; Affaires en file R enrôlées=1 ; Affaires en file J enrôlées=1 ; "
           + "Affaires en file F enrôlées=1 ; Jugements après instruction=1 ; Jugements sur siège=1 ; Autres=1 ; Ordonnances de référé=1 ; "
           + "Injonctions de payer=1 ; Ordonnances sur requête=1 || ", StringComparison.Ordinal),
        $"statistiques internes, judiciaires (MetierStats.cs:118-260, reglages 'JF','ZIND' / 'JF') : {vu}");
    Exiger(vu.Contains("Procédures collectives : Ouvertures de procédures RJ=1 ; Ouvertures de procédures LJ=1 ; Ouvertures de procédures LJ simplifiées=1 ; "
           + "Ouvertures de sauvegardes=1 ; Conversion de sauvegarde en RJ=1 ; Conversion en LJ=1 ; Plan de cession=1 ; Plans de continuation=0 ; "
           + "Plans de sauvegarde=0 ; Salariés concernés par les ouvertures de RJ=10 ; Salariés concernés par les ouvertures de LJ=3 ; "
           + "Salariés concernés par les ouvertures de sauvegarde=5 ; Salariés concernés par les ouvertures (total)=15 ; Salariés repris dans les plans=7 ; "
           + "Chiffre d'affaires des entreprises=120,5 ; Passif déclaré=2300 ; Actif déclaré=500 ; Assignations en RJ ou LJ=1 ; Dessaisissements=1 ; "
           + "Déboutés=0 ; Ouvertures de PC par assignation=1 ; Ouvertures de PC par saisine du parquet=1 ; Ouvertures de PC par saisine d'office=0 ; "
           + "Ouvertures de PC de rétablissement professionnel=1 ; Ouvertures de traitement de sortie de crise=0 ; Plans de traitement de sortie de crise=0 || ",
               StringComparison.Ordinal),
        "statistiques internes, procedures collectives (:262-587) : la PC exterieure 1911404 n'est comptee nulle part");
    Exiger(vu.Contains("Procédures collectives - Clôtures : Insuffisance d'actif (banc 9)=2 || ", StringComparison.Ordinal)
           && vu.EndsWith("Statistiques IP : Mises au rôle=1 ; Ordonnances IP=1 ; Ordonnances de fin d'instance=1 ; Exécutoires=1 ; Oppositions=0", StringComparison.Ordinal),
        "statistiques internes : les clotures du 15 et du 31 a 15 h comptees, celle du 1er avril non ; IP (:619-700), l'executoire d'avril ecarte");

    var web = Rendu(await Volet(VoletsStatistiques.Web));
    Exiger(web.StartsWith("Ouvertures, transformations et plans : Ouvertures RJ=1/0/0/0/0/1/0/0/0/1 ; Ouvertures LJ=0/0/1/0/0/1/0/0/0/1 ; "
           + "Ouvertures sauvegardes=1/0/0/0/0/1/0/0/0/1 ; Transformations RJ en LJ=1/0/0/0/1/0/0/0/0/1 ; Conversions LJ en LJS=0/0/0/0/0/0/0/0/0/0 ; "
           + "Plans de continuation=0/0/0/0/0/0/0/0/0/0 ; Plans de cession=1/0/0/0/1/0/0/0/0/1 ; Plans de sauvegarde=0/0/0/0/0/0/0/0/0/0 || "
           + "Totaux : Total des ouvertures=3 ; Total des transformations et conversions=1 ; Total des plans=1 || "
           + "Clôtures : Insuffisance d'actif (banc 9)=1/0/0/0/1/0/0/0/0/1 ; Total=/////////1", StringComparison.Ordinal),
        $"statistiques web (:817-1729) : tranches de CA (sans chiffre : la premiere) et de salaries, totaux, clotures (celle du 31 a 15 h ecartee) : {web}");

    var rep = Rendu(await Volet(VoletsStatistiques.RepartitionsJcOp));
    Exiger(rep == "Jugements de contentieux (JC) : 9S1 - Nature banc 9 un=1 ; Total=1 || Ordonnances (OP) : 9S2 - Nature banc 9 deux=1 ; Total=1",
        $"repartitions JC et OP (:704-814) : {rep}");

    var ca = Rendu(await Volet(VoletsStatistiques.ActivitesCa, "1"));
    Exiger(ca == "Ouvertures par activités et chiffre d'affaires, niveau 1 : =Sans activité ou activité inconnue/0/0/1/0/1 ; G=Commerce/1/0/0/0/1 ; TOTAL=/1/0/1/0/2",
        $"activites par CA, niveau 1 (MetierStats.Activites.cs) : la PC sans etablissement ecartee : {ca}");
    var sal = Rendu(await Volet(VoletsStatistiques.ActivitesSalaries, "9"));
    Exiger(sal == "Ouvertures par activités et nombre de salariés, niveau 1 : =Sans activité ou activité inconnue/0/1/0/0/0/1 ; G=Commerce/0/1/0/0/0/1 ; TOTAL=/0/2/0/0/0/2",
        $"activites par salaries, un niveau hors 1-4 ramene a 1 (OPE_NIVEAU) : {sal}");

    var semestre = await Volet(VoletsStatistiques.Semestrielles);
    Exiger(semestre is { NombrePc: "non calculé", Tableaux: [{ Cause: { } cause }] } && cause.Contains("GERER_PC (absente de la base V3)", StringComparison.Ordinal)
           && cause.Contains("MANDATAIRE_PC (absente de la base V3)", StringComparison.Ordinal),
        $"semestrielles : les tables des mandataires absentes de cette base sont nommees, rien n'est invente ({Rendu(semestre)})");

    var inversees = await LectureStatistiques.LireAsync(cn, new RechercheStatistiques(VoletsStatistiques.Internes, mars1971.Fin, mars1971.Debut, null), CancellationToken.None);
    Exiger(inversees is { Refus: LectureStatistiques.DatesInversees, Tableaux.Length: 0 },
        "statistiques : debut apres la fin -> le message de RIG v2 (OPE_DATES.cs:37-38), rien de calcule");
    var avril = Rendu(await LectureStatistiques.LireAsync(cn, new RechercheStatistiques(VoletsStatistiques.Internes, new DateTime(1971, 4, 1), new DateTime(1971, 4, 30), null), CancellationToken.None));
    Exiger(avril.StartsWith("Statistiques judiciaires : Affaires enrôlées=1 ; ", StringComparison.Ordinal) && avril.Contains("Exécutoires=1", StringComparison.Ordinal),
        $"statistiques internes d'avril 1971 : les jumeaux d'avril y sont (1911004, l'executoire de 1911008) : {avril}");
}
var statRoute = await http.PostAsJsonAsync(RoutesEcransMain.Pour(RoutesStatistiques.Statistiques, Greffe),
    new RechercheStatistiques(VoletsStatistiques.RepartitionsJcOp, mars1971.Debut, mars1971.Fin, null), json);
var statLue = await statRoute.Content.ReadFromJsonAsync<StatistiquesLues>(json);
Exiger(statRoute.IsSuccessStatusCode && statLue is { Tableaux: [{ Lignes: [{ Libelle: "9S1 - Nature banc 9 un" }, ..] }, ..] },
    $"route Statistiques par le serveur : le volet calcule ({(int)statRoute.StatusCode})");
var statPont = await EcranStatistiques.CalculerAsync(client, Greffe, VoletsStatistiques.RepartitionsJcOp, "1971-03-01", "1971-03-31", null);
Exiger(statPont is { Mode: "serveur", Tableaux: [{ Lignes: [{ Libelle: "9S1 - Nature banc 9 un", Valeurs: ["1"] }, ..] }, ..] },
    "pont Statistiques branche : les tableaux de la base, mode « serveur » (aucune mention exemple)");
Exiger((await EcranStatistiques.CalculerAsync(null, null, VoletsStatistiques.Internes, "1971-03-01", "1971-03-31", null)) is { Mode: "demo", Tableaux.Length: > 0 },
    "pont Statistiques sans serveur : la demonstration, mode « demo »");
Exiger((await EcranStatistiques.CalculerAsync(client, GreffeAbsent, VoletsStatistiques.Internes, "1971-03-01", "1971-03-31", null)) is { Mode: "erreur", Tableaux.Length: 0, Message: not null },
    "pont Statistiques, greffe absent : le motif, AUCUN tableau");

// ============================================================ 12. GED JUDICIAIRE ET DOCUMENTS DEMATERIALISES (lot 11)
// « GED judiciaire » (PROC_GEDJUD, LectureGedJud.cs) et « Documents dématérialisés » (PROC_DOC_DEMAT, LectureDocDemat.cs),
// branches au lot 11 du plan. Placee APRES les statistiques : ses instances n'entrent dans aucun de leurs comptes. Aucun
// document VIDIGED_INDEX ni aucune piece DEMAT_CREFO_PJ avant cette section : toutes sont du jeu. Identifiants du lot 11 :
// 2 150 000 a 2 159 999. Colonnes aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 27/11/2026).
await Sql("""
    IF OBJECT_ID(N'v2.VIDIGED_INDEX') IS NULL CREATE TABLE v2.VIDIGED_INDEX (VGIDX_ID_VGIDX int NOT NULL PRIMARY KEY, VGIDX_LIBELLE varchar(200) NULL,
        VGIDX_DATE_RECEPTION datetime NOT NULL, VGIDX_NATURE_DOC varchar(50) NULL, VGIDX_ID_ENTITE int NULL, VGIDX_TYPE_ENTITE varchar(50) NULL,
        VGIDX_COMMENTAIRE varchar(1000) NULL, VGIDX_GUID_DOCUMENT uniqueidentifier NOT NULL, VGIDX_ETAT varchar(3) NULL, VGIDX_MOTIF_REJET varchar(1000) NULL);
    IF OBJECT_ID(N'v2.VIDIGED_PARAMETRAGE') IS NULL CREATE TABLE v2.VIDIGED_PARAMETRAGE (VGPRM_ID_VGPRM int NOT NULL PRIMARY KEY, VGPRM_CODE varchar(50) NULL,
        VGPRM_LIBELLE varchar(1000) NULL, VGPRM_TYPE varchar(50) NULL);
    IF COL_LENGTH('v2.VIDIGED_DJ', 'VGDDJ_GUID_DOCUMENT') IS NULL ALTER TABLE v2.VIDIGED_DJ ADD VGDDJ_GUID_DOCUMENT uniqueidentifier NULL;
    IF COL_LENGTH('v2.VIDIGED_DJ', 'VGDDJ_NUM_ORDRE') IS NULL ALTER TABLE v2.VIDIGED_DJ ADD VGDDJ_NUM_ORDRE smallint NULL;
    IF OBJECT_ID(N'v2.DEMAT_CREFO_PJ') IS NULL CREATE TABLE v2.DEMAT_CREFO_PJ (DMCOPJ_ID_DMCOPJ int NOT NULL PRIMARY KEY, DMCOPJ_ID_DMTLCF int NULL,
        DMCOPJ_ID_DMTDAS int NULL, DMCOPJ_TYPE_PJ_CREFO varchar(50) NOT NULL, DMCOPJ_NUM_ORDRE smallint NOT NULL, DMCOPJ_NOM_FICHIER varchar(128) NOT NULL,
        DMCOPJ_DATE_PIECE date NULL, DMCOPJ_SUPPRIME bit NOT NULL, DMCOPJ_ETAT_INFOGREFFE char(15) NULL);
    IF OBJECT_ID(N'v2.TYPE_PJ_CREFO') IS NULL CREATE TABLE v2.TYPE_PJ_CREFO (R_TYPEPJCREF_VALEUR varchar(50) NOT NULL PRIMARY KEY,
        R_TYPEPJCREF_LIBELLE_ECRAN varchar(100) NOT NULL, R_TYPEPJCREF_ABREGE_PIECE varchar(16) NULL);
    IF COL_LENGTH('v2.DEMAT_LIASSE_CFE', 'DMTLCF_DATE_CREATION') IS NULL ALTER TABLE v2.DEMAT_LIASSE_CFE ADD DMTLCF_DATE_CREATION datetime NULL;
    IF COL_LENGTH('v2.DEMAT_DAS', 'DMTDAS_DATE_CREATION') IS NULL ALTER TABLE v2.DEMAT_DAS ADD DMTDAS_DATE_CREATION datetime NULL;
    """);
// GED : l'instance 2150001, la PC 2150101, la prevention 2150201, la mesure 2150301 ; deux decisions de l'instance, 2150401
// rendue le 02/06/2004 et 2150402 sans date (prevue le 10/06), liees au document 2150601 dans l'ordre 2 puis 1 (la premiere
// liee est donc 2150402 : l'ordre VGDDJ_NUM_ORDRE, pas l'identifiant). Documents, du plus recent au plus ancien : 2150605
// (audience : son identifiant), 2150603 rejete (lu : « Afficher tout » est un critere de l'ecran), 2150602 de nature sans
// libelle (le code), 2150604 « VR » sans libelle (le code), 2150601, 2150607 (instance absente de la copie : son identifiant).
// Jumeau ecarte : 2150606 sans entite (OPE_RECHERCHE_DOC.cs:375).
await Sql("""
    INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE) VALUES (2150001, '2004J00007');
    INSERT INTO v2.PROCEDURE_COLLECTIVE (PRCLL_ID_PRCLL, PRCLL_NUM_PC) VALUES (2150101, '2004RJ0011');
    INSERT INTO v2.PREVENTION (PREVE_ID_PREVE, PREVE_NUM_PRV) VALUES (2150201, '2004PRV011');
    INSERT INTO v2.MESURE (MSINS_ID_MSINS, MSINS_NUM_MESURE) VALUES (2150301, '2004M00011');
    INSERT INTO v2.DECISION_JUDICIAIRE (DCJDC_ID_DCJDC, DCJDC_ID_INSTN, DCJDC_DATE, DCJDC_DATE_PREVUE) VALUES
        (2150401, 2150001, '20040602', '20040601'), (2150402, 2150001, NULL, '20040610');
    INSERT INTO v2.VIDIGED_PARAMETRAGE (VGPRM_ID_VGPRM, VGPRM_CODE, VGPRM_LIBELLE, VGPRM_TYPE) VALUES
        (2150501, 'ASS', 'Assignation', 'NATURE_DOC'), (2150502, 'V', 'Validé', 'ETAT'), (2150503, 'A', 'En attente', 'ETAT'),
        (2150504, 'R', 'Rejeté', 'ETAT'), (2150505, 'V', 'Nature homonyme', 'NATURE_DOC');
    INSERT INTO v2.VIDIGED_INDEX (VGIDX_ID_VGIDX, VGIDX_LIBELLE, VGIDX_DATE_RECEPTION, VGIDX_NATURE_DOC, VGIDX_ID_ENTITE, VGIDX_TYPE_ENTITE,
        VGIDX_COMMENTAIRE, VGIDX_GUID_DOCUMENT, VGIDX_ETAT, VGIDX_MOTIF_REJET) VALUES
        (2150601, 'Assignation banc', '20040601 10:00', 'ASS', 2150001, 'INSTANCE', 'piece originale', '00000000-0000-0000-0000-000002150601', 'V', NULL),
        (2150602, 'Conclusions banc', '20040603 09:00', 'XX', 2150101, 'PROCEDURE_COLLECTIVE', NULL, '00000000-0000-0000-0000-000002150602', 'A', NULL),
        (2150603, 'Rejet banc', '20040604 09:00', 'ASS', 2150201, 'PREVENTION', NULL, '00000000-0000-0000-0000-000002150603', 'R', 'Illisible'),
        (2150604, 'Mesure banc', '20040602 09:00', 'ASS', 2150301, 'MESURE', NULL, '00000000-0000-0000-0000-000002150604', 'VR', NULL),
        (2150605, 'Audience banc', '20040605 09:00', NULL, 2150999, 'AUDIENCE', NULL, '00000000-0000-0000-0000-000002150605', 'V', NULL),
        (2150606, 'Sans entite banc', '20040606 09:00', 'ASS', NULL, 'INSTANCE', NULL, '00000000-0000-0000-0000-000002150606', 'V', NULL),
        (2150607, 'Instance absente banc', '20040531 09:00', 'ASS', 2159999, 'INSTANCE', NULL, '00000000-0000-0000-0000-000002150607', 'V', NULL);
    INSERT INTO v2.VIDIGED_DJ (VGDDJ_ID_VGDDJ, VGDDJ_ID_DCJDC, VGDDJ_GUID_DOCUMENT, VGDDJ_NUM_ORDRE) VALUES
        (2150701, 2150401, '00000000-0000-0000-0000-000002150601', 2), (2150702, 2150402, '00000000-0000-0000-0000-000002150601', 1);
    """);
await using (var cn = new SqlConnection(greffe.Connexion))
{
    await cn.OpenAsync();
    var ged = await LectureGedJud.LireAsync(cn, CancellationToken.None);
    Exiger(ged.Lignes.Select(d => d.Id).SequenceEqual([2150605, 2150603, 2150602, 2150604, 2150601, 2150607]) && ged.Total == 6,
        $"GED judiciaire : avec une entite seulement (2150606 ecarte), rejete compris, du plus recent au plus ancien ({string.Join(",", ged.Lignes.Select(d => d.Id))})");
    var parId = ged.Lignes.ToDictionary(d => d.Id);
    Exiger(parId[2150601] is { Entite: "2004J00007", TypeEntite: "Instance", Nature: "Assignation", Etat: "Validé", DecisionsLiees: 2, DateDecision: null }
           && parId[2150601].DatePrevue == new DateTime(2004, 6, 10) && parId[2150601].Commentaire == "piece originale",
        "GED : une instance, sa nature et son etat par leur libelle, deux decisions liees, la PREMIERE par VGDDJ_NUM_ORDRE (2150402, sans date)");
    Exiger(parId[2150602] is { Entite: "2004RJ0011", TypeEntite: "Procédure collective", Nature: "XX", Etat: "En attente", DecisionsLiees: 0 }
           && parId[2150603] is { Entite: "2004PRV011", Etat: "Rejeté", CodeEtat: "R", MotifRejet: "Illisible" }
           && parId[2150604] is { Entite: "2004M00011", TypeEntite: "Mesure", Etat: "VR" },
        "GED : PC, prevention, mesure par leur numero ; une nature ou un etat sans libelle garde son code ; le motif du rejet");
    Exiger(parId[2150605] is { Entite: "Audience 2150999", Nature: "" } && parId[2150607] is { Entite: "Instance 2159999" },
        "GED : une audience, ou une entite absente de la copie, garde son identifiant");
    var gedDeux = await LectureGedJud.LireAsync(cn, CancellationToken.None, plafond: 2);
    Exiger(gedDeux is { Lignes.Length: 2, Total: 6 }, "GED : au plus le plafond, et le total de la base");
}
var gedRoute = await http.GetFromJsonAsync<DocumentsGedLus>(RoutesEcransMain.Pour(RoutesGedJud.Documents, Greffe), json);
Exiger(gedRoute is { Total: 6, Lignes: [{ Id: 2150605 }, ..] }, "route GED judiciaire par le serveur : les documents de la base");
Exiger((await http.GetAsync(RoutesEcransMain.Pour(RoutesGedJud.Documents, GreffeAbsent))).StatusCode == HttpStatusCode.NotFound,
    "route GED judiciaire, greffe absent du catalogue : 404");
var gedPont = await EcranGedJud.DocumentsAsync(client, Greffe);
Exiger(gedPont is { Mode: "serveur", Message: null, Total: 6 }
       && gedPont.Lignes.Single(d => d.Id == "2150601") is { Decision: "Assignation banc", Instance: "2004J00007", NbLies: 2, DateDecision: null, DatePrevue: "2004-06-10", Reception: "2004-06-01", Statut: "ok" }
       && gedPont.Lignes.Single(d => d.Id == "2150603") is { Statut: "alerte", CodeEtat: "R" }
       && gedPont.Lignes.Single(d => d.Id == "2150602") is { Statut: "attente" },
    "pont GED branche : les documents a la forme de l'ecran, dates aaaa-mm-jj, la teinte de l'etat V2 (rejete rouge, attente orange)");
Exiger((await EcranGedJud.DocumentsAsync(null, null)) is { Mode: "demo", Lignes.Length: 0 },
    "pont GED sans serveur : mode « demo », aucune ligne (l'ecran garde sa demonstration, marquee « exemple »)");
Exiger((await EcranGedJud.DocumentsAsync(client, GreffeAbsent)) is { Mode: "erreur", Lignes.Length: 0, Message: not null },
    "pont GED, greffe absent : le motif, AUCUNE ligne");

// Demat : deux liasses CFE (2150901 de la demande 2150801, dossier « B11D01», creee le 01/06/2004 ; 2150902 de la demande
// 2150802 sans numero indicatif, creee le 01/05/2004) et un DAS sans demande (2150951, cree le 01/07/2004). Pieces : 2151002
// (ordre 1, supprimee : lue) et 2151001 (ordre 2) de 2150901 ; 2151003 d'un type absent de TYPE_PJ_CREFO (le code) ; 2151004
// du DAS. Aucune piece avant cette section : les liasses des sections precedentes n'en ont pas.
await Sql("""
    INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_NOM, DMND_NUM_INDICATIF, DMND_ETAT_DEMANDE, DMND_EN_COURS, DMND_CODE_PROSS) VALUES
        (2150801, 'D2150801', 'Demat - BANC ALPHA', 'B11D01', 'V', 0, 'DOC_DEMAT'), (2150802, 'D2150802', 'Demat - BANC BETA', NULL, 'V', 0, 'DOC_DEMAT');
    INSERT INTO v2.DEMAT_LIASSE_CFE (DMTLCF_ID_DMTLCF, DMTLCF_ID_DMND, DMTLCF_DATE_CREATION) VALUES (2150901, 2150801, '20040601'), (2150902, 2150802, '20040501');
    INSERT INTO v2.DEMAT_DAS (DMTDAS_ID_DMTDAS, DMTDAS_ID_DMND, DMTDAS_DATE_CREATION) VALUES (2150951, NULL, '20040701');
    INSERT INTO v2.TYPE_PJ_CREFO (R_TYPEPJCREF_VALEUR, R_TYPEPJCREF_LIBELLE_ECRAN, R_TYPEPJCREF_ABREGE_PIECE) VALUES
        ('PJ_KBIS_B11', 'Extrait Kbis', 'KBIS'), ('PJ_STAT_B11', 'Statuts', 'STAT');
    INSERT INTO v2.DEMAT_CREFO_PJ (DMCOPJ_ID_DMCOPJ, DMCOPJ_ID_DMTLCF, DMCOPJ_ID_DMTDAS, DMCOPJ_TYPE_PJ_CREFO, DMCOPJ_NUM_ORDRE, DMCOPJ_NOM_FICHIER,
        DMCOPJ_DATE_PIECE, DMCOPJ_SUPPRIME, DMCOPJ_ETAT_INFOGREFFE) VALUES
        (2151001, 2150901, NULL, 'PJ_STAT_B11', 2, 'statuts.pdf', '20040528', 0, 'ACQ'),
        (2151002, 2150901, NULL, 'PJ_KBIS_B11', 1, 'kbis.pdf', NULL, 1, NULL),
        (2151003, 2150902, NULL, 'PJ_INCONNU_B11', 1, 'autre.pdf', '20040430', 0, NULL),
        (2151004, NULL, 2150951, 'PJ_KBIS_B11', 1, 'das.pdf', '20040630', 0, NULL);
    """);
await using (var cn = new SqlConnection(greffe.Connexion))
{
    await cn.OpenAsync();
    var demat = await LectureDocDemat.LireAsync(cn, CancellationToken.None);
    Exiger(demat.Lignes.Select(p => p.Id).SequenceEqual([2151004, 2151002, 2151001, 2151003]) && demat.Total == 4,
        $"pieces demat : de la demat la plus recemment creee a la plus ancienne, dans l'ordre DMCOPJ_NUM_ORDRE ({string.Join(",", demat.Lignes.Select(p => p.Id))})");
    var parId = demat.Lignes.ToDictionary(p => p.Id);
    Exiger(parId[2151002] is { Dossier: "B11D01", Demande: "Demat - BANC ALPHA", Demat: "Liasse CFE 2150901", Abrege: "KBIS", Libelle: "Extrait Kbis", Supprimee: true, DatePiece: null }
           && parId[2151001] is { Abrege: "STAT", Fichier: "statuts.pdf", EtatInfogreffe: "ACQ", Supprimee: false }
           && parId[2151001].DatePiece == new DateTime(2004, 5, 28),
        "pieces demat : le dossier de la demande (numero indicatif), le type par TYPE_PJ_CREFO, une piece supprimee lue ; l'etat Infogreffe sans blancs");
    Exiger(parId[2151003] is { Dossier: "D2150802", Abrege: "PJ_INCONNU_B11", Libelle: "PJ_INCONNU_B11" } && parId[2151004] is { Dossier: "DAS 2150951", Demat: "DAS 2150951" },
        "pieces demat : sans numero indicatif, le numero de demande ; sans demande, la demat ; un type inconnu garde son code");
}
var dematPont = await EcranDocDemat.PiecesAsync(client, Greffe);
Exiger(dematPont is { Mode: "serveur", Message: null, Total: 4 }
       && dematPont.Pieces.Single(p => p.Id == "2151002") is { Etat: "Supprimée", Date: "" }
       && dematPont.Pieces.Single(p => p.Id == "2151001") is { Etat: "En vigueur", Date: "2004-05-28", Dossier: "B11D01" },
    "pont Documents dematerialises branche : les pieces a la forme de l'ecran (Supprimée / En vigueur, date aaaa-mm-jj)");
Exiger((await http.GetFromJsonAsync<PiecesDematLues>(RoutesEcransMain.Pour(RoutesDocDemat.Pieces, Greffe), json)) is { Total: 4 },
    "route Documents dematerialises par le serveur : les pieces de la base");
Exiger((await EcranDocDemat.PiecesAsync(null, null)) is { Mode: "demo", Pieces.Length: 0 },
    "pont Documents dematerialises sans serveur : mode « demo », aucune piece");
Exiger((await EcranDocDemat.PiecesAsync(client, GreffeAbsent)) is { Mode: "erreur", Pieces.Length: 0, Message: not null },
    "pont Documents dematerialises, greffe absent : le motif, AUCUNE piece");

// ============================================================ 13. COMPTABILISATION (LectureComptabilisation.cs, lot 13)
// « Comptabilisation » (PROC_COMPTABILISATION), branchee au lot 13 du plan : l'arbre des journees de RIG v2 (OPE_COMPTABILISATION,
// TreeViewJoursCompta). La lecture prend TOUTES les factures et tous les encaissements non comptabilises de la base : les factures
// des sections precedentes sont marquees comptabilisees le 01/01/1900 le temps de la section, puis rendues. Journees isolees en mars
// 1971. Identifiants 2313xxx (lot 13 : 2 300 000 a 2 399 999). Chaque regle a son jumeau ecarte.
// fix-ok: mesure au premier passage du banc (27/11/2026) -- la comparaison formatait les montants dans la culture du poste
// (« 50,00 » contre « 50.00 » attendu) et l'UPDATE de soc.Reglage sans AuteurModif/DateModif levait « SOC-003 CA07 ».
await Sql("""
    IF COL_LENGTH('v2.FACTURE', 'FACTU_MONTANT_TTC') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_MONTANT_TTC money NULL;
    IF COL_LENGTH('v2.FACTURE', 'FACTU_DATE_COMPTABILISATION') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_DATE_COMPTABILISATION datetime NULL;
    IF COL_LENGTH('v2.AFFAIRE', 'AFFRE_ID_UTLST_EN_COURS') IS NULL ALTER TABLE v2.AFFAIRE ADD AFFRE_ID_UTLST_EN_COURS int NULL;
    IF COL_LENGTH('v2.VENTILATION', 'VENTI_ID_ENCMT') IS NULL ALTER TABLE v2.VENTILATION ADD VENTI_ID_ENCMT int NULL;
    IF COL_LENGTH('v2.ARTICLE_UTILISE', 'ARTUT_NUMERO_FACTURE') IS NULL ALTER TABLE v2.ARTICLE_UTILISE ADD ARTUT_NUMERO_FACTURE varchar(25) NULL;
    IF OBJECT_ID(N'v2.ENCAISSEMENT') IS NULL
        CREATE TABLE v2.ENCAISSEMENT (ENCMT_ID_ENCMT int NOT NULL PRIMARY KEY, ENCMT_DATE_ENCAISSEMENT datetime NOT NULL,
            ENCMT_MODE_PAIEMENT varchar(8) NULL, ENCMT_MONTANT money NULL, ENCMT_REMIS int NULL, ENCMT_DATE_COMPTABILISATION datetime NULL);
    IF OBJECT_ID(N'v2.MODE_PAIEMENT') IS NULL
        CREATE TABLE v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR varchar(8) NOT NULL PRIMARY KEY, R_MODEPAIEME_LIBELLE_ECRAN varchar(35) NOT NULL,
            R_MODEPAIEME_TYPE varchar(2) NULL);
    """);
await Sql("""
    UPDATE v2.FACTURE SET FACTU_DATE_COMPTABILISATION = '19000101' WHERE FACTU_DATE_COMPTABILISATION IS NULL;
    INSERT INTO v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR, R_MODEPAIEME_LIBELLE_ECRAN, R_MODEPAIEME_TYPE)
    SELECT v, l, t FROM (VALUES ('CH13', 'Chèque du banc', 'CH'), ('ES13', 'Espèces du banc', 'ES'), ('VI13', 'Virement du banc', 'VI'),
                                ('REG', 'Régularisation', 'RG')) AS m(v, l, t)
    WHERE NOT EXISTS (SELECT 1 FROM v2.MODE_PAIEMENT AS x WHERE x.R_MODEPAIEME_VALEUR = m.v);
    -- 2313001 en cours (tenue par un agent) ; 2313002 non.
    INSERT INTO v2.AFFAIRE (AFFRE_ID_AFFRE, AFFRE_LIBELLE, AFFRE_ID_UTLST_EN_COURS) VALUES
        (2313001, 'Compta - AFF ALPHA', 5), (2313002, 'Compta - AFF BETA', NULL);
    INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_EN_COURS, DMND_CODE_PROSS, DMND_ID_AFFRE) VALUES
        (2313101, 'D2313101', 'V', 0, 'IMMAT', 2313001), (2313102, 'D2313102', 'V', 0, 'IMMAT', 2313002),
        (2313103, 'D2313103', 'V', 0, 'IMMAT', NULL);
    -- 01/03/1971 : deux factures (100,10 + 50,05 TTC, une affaire en cours) ; jumeau ecarte F2313203 deja comptabilisee.
    -- 03/03/1971 : F2313204, sa demande SANS affaire -- jumeau ecarte : son jour n'est pas une journee (INNER JOIN AFFAIRE).
    INSERT INTO v2.FACTURE (FACTU_NUMERO_FACTURE, FACTU_NUM_DEMANDE, FACTU_DATE_CREATION, FACTU_MONTANT_TTC, FACTU_DATE_COMPTABILISATION) VALUES
        ('F2313201', 'D2313101', '19710301 09:00', 100.10, NULL), ('F2313202', 'D2313102', '19710301 17:30', 50.05, NULL),
        ('F2313203', 'D2313101', '19710301 10:00', 999.99, '19710401'), ('F2313204', 'D2313103', '19710303', 12.00, NULL);
    -- 01/03/1971 : remis 77 en cheque (30,00 + 20,00) ; non remis : cheque 10,00 (un remis du meme mode le meme jour : observation),
    -- REG 5,00 (pas d'observation pour REG/PV), especes 1,23 ; jumeaux ecartes : 2313306 deja comptabilise, 2313307 sans mode.
    -- 02/03/1971 : un virement non remis 7,77, ventile sur 2313002 : la journee existe par son seul encaissement.
    -- 04/03/1971 : remis 78 (virement 8,00) SANS ventilation sur une affaire -- jumeau ecarte : son jour n'est pas une journee.
    INSERT INTO v2.ENCAISSEMENT (ENCMT_ID_ENCMT, ENCMT_DATE_ENCAISSEMENT, ENCMT_MODE_PAIEMENT, ENCMT_MONTANT, ENCMT_REMIS, ENCMT_DATE_COMPTABILISATION) VALUES
        (2313301, '19710301 09:10', 'CH13', 30.00, 77, NULL), (2313302, '19710301 11:00', 'CH13', 20.00, 77, NULL),
        (2313303, '19710301 12:00', 'CH13', 10.00, NULL, NULL), (2313304, '19710301 12:30', 'REG', 5.00, NULL, NULL),
        (2313305, '19710301 13:00', 'ES13', 1.23, NULL, NULL), (2313306, '19710301 14:00', 'ES13', 40.00, NULL, '19710305'),
        (2313307, '19710301 15:00', NULL, 3.00, NULL, NULL), (2313308, '19710302 10:00', 'VI13', 7.77, NULL, NULL),
        (2313309, '19710304 10:00', 'VI13', 8.00, 78, NULL);
    INSERT INTO v2.VENTILATION (VENTI_ID_VENTI, VENTI_ID_AFFRE, VENTI_MONTANT, VENTI_ID_ENCMT) VALUES
        (2313401, 2313001, 30.00, 2313301), (2313402, 2313002, 7.77, 2313308);
    """);
await using (var cn = new SqlConnection(greffe.Connexion))
{
    await cn.OpenAsync();
    var compta = await LectureComptabilisation.LireAsync(cn, CancellationToken.None);
    Exiger(compta is { Anomalie: null, EncaissementsSeuls: false, Total: 2 }
           && compta.Journees.Select(j => j.Date).SequenceEqual([new DateTime(1971, 3, 1), new DateTime(1971, 3, 2)]),
        "comptabilisation : les journees = jours des factures (demande ET affaire) UNION jours des encaissements ventiles, de la plus "
        + $"ancienne a la plus recente ; le 03/03 (demande sans affaire) et le 04/03 (sans ventilation) ecartes ({string.Join(",", compta.Journees.Select(j => j.Date.ToString("dd/MM")))})");
    var mars1 = compta.Journees[0];
    Exiger(mars1 is { Factures: 2, MontantTtc: 150.15m, AffairesEnCours: 1, SansDemande: 0 },
        $"comptabilisation : l'agregat FactureJour du 01/03 -- 2 factures, 150,15 TTC exact, 1 affaire en cours ; la facture deja comptabilisee ecartee ({mars1})");
    // Montant en ecriture invariante : « 50.00 », quelle que soit la culture du poste.
    Exiger(mars1.Encaissements.Select(e => $"{e.Remis?.ToString() ?? "-"}|{e.Mode}|{e.Libelle}|{e.TypeMode}|{e.Nombre}|{e.Montant.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}|{e.Observation}")
               .SequenceEqual(["77|CH13|Chèque du banc|CH|2|50.00|", "-|REG|Régularisation|RG|1|5.00|",
                               $"-|CH13|Chèque du banc|CH|1|10.00|{LectureComptabilisation.RemisDejaPresents}", "-|ES13|Espèces du banc|ES|1|1.23|"]),
        "comptabilisation : sous le 01/03, le remis 77 (2 cheques, 50,00), puis les non remis REG, puis les autres modes par code, le cheque "
        + "non remis avec l'observation de RIG v2 ; l'encaissement comptabilise et celui sans mode ecartes ("
        + string.Join(" ; ", mars1.Encaissements.Select(e => $"{e.Remis}|{e.Mode}|{e.Nombre}|{e.Montant}")) + ")");
    Exiger(compta.Journees[1] is { Factures: 0, MontantTtc: 0m } && compta.Journees[1].Encaissements is [{ Remis: null, Mode: "VI13", Montant: 7.77m }],
        "comptabilisation : le 02/03, une journee par son seul encaissement (virement non remis 7,77)");
    var une = await LectureComptabilisation.LireAsync(cn, CancellationToken.None, plafond: 1);
    Exiger(une.Journees.Length == 1 && une.Total == 2, $"comptabilisation : plafond 1 -> une journee, total 2 ({une.Total})");
}
// Le reglage EXPRESSO_FIGERFACTAUTO (« 1 » par defaut) : « 0 » affiche le libelle du parametre ; jumeau « 1 » : non.
await Sql("INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('EXPRESSO_FIGERFACTAUTO', 'Banc EcransMainBase lot 13', '0')");
await using (var cn = new SqlConnection(greffe.Connexion))
{
    await cn.OpenAsync();
    Exiger((await LectureComptabilisation.LireAsync(cn, CancellationToken.None)).EncaissementsSeuls, "comptabilisation : EXPRESSO_FIGERFACTAUTO a « 0 » -> encaissements seuls");
    // SOC-003 CA07 : modifier un reglage pose son auteur et sa date.
    await Sql("UPDATE soc.Reglage SET Valeur = '1', AuteurModif = 'banc', DateModif = SYSDATETIME() WHERE Code = 'EXPRESSO_FIGERFACTAUTO'");
    Exiger(!(await LectureComptabilisation.LireAsync(cn, CancellationToken.None)).EncaissementsSeuls, "comptabilisation : jumeau, « 1 » -> non");
}
var comptaRoute = await http.GetFromJsonAsync<ComptabilisationLue>(RoutesEcransMain.Pour(RoutesComptabilisation.Journees, Greffe), json);
Exiger(comptaRoute is { Total: 2, Anomalie: null } && comptaRoute.Journees[0].MontantTtc == 150.15m, "route de la comptabilisation : les journees de la lecture");
Exiger((await http.GetAsync(RoutesEcransMain.Pour(RoutesComptabilisation.Journees, GreffeAbsent))).StatusCode == HttpStatusCode.NotFound,
    "route de la comptabilisation : greffe absent -> 404, sans repli");
var comptaPont = await EcranComptabilisation.JourneesAsync(client, Greffe);
Exiger(comptaPont is { Mode: "serveur", Total: 2, Anomalie: null } && comptaPont.Journees[0] is { Date: "1971-03-01", MontantTotalTTC: 15015, NbFactures: 2 }
       && comptaPont.Journees[0].Remis[0] is { NumeroRemis: 77, ModePaiement: "Chèque du banc", TypeMode: "CH", SommeEncaissement: 5000, DateEncaissement: "1971-03-01" }
       && comptaPont.Journees[0].Remis[2] is { NumeroRemis: 0, Observation: LectureComptabilisation.RemisDejaPresents }
       && !comptaPont.Journees.Any(j => EcranComptabilisation.Exemple().Journees.Any(e => e.Date == j.Date)),
    "pont Comptabilisation branche : les journees de la base (aaaa-mm-jj, montants en centimes, non remis = 0), aucune de la demonstration");
// Une facture non comptabilisee sans demande bloque l'arbre (RIG v2), son affaire par ARTICLE_UTILISE ; retiree ensuite.
await Sql("""
    INSERT INTO v2.FACTURE (FACTU_NUMERO_FACTURE, FACTU_NUM_DEMANDE, FACTU_DATE_CREATION, FACTU_MONTANT_TTC) VALUES ('F2313205', 'D2313999', '19710301', 1.00);
    INSERT INTO v2.ARTICLE_UTILISE (ARTUT_ID_ARTUT, ARTUT_ID_AFFRE, ARTUT_NUMERO_FACTURE) VALUES (2313501, 2313001, 'F2313205');
    """);
var anomalie = await EcranComptabilisation.JourneesAsync(client, Greffe);
Exiger(anomalie is { Mode: "serveur", Journees.Length: 0, Total: 0 }
       && anomalie.Anomalie == "ANOMALIE : 1 facture(s) ne sont rattachées à aucune demande. Merci de contacter Amitel.\n\nFacture N° F2313205 (affaire N° 2313001)",
    $"comptabilisation : une facture sans demande -> le message de RIG v2 et AUCUNE journee ({anomalie.Anomalie})");
await Sql("DELETE FROM v2.ARTICLE_UTILISE WHERE ARTUT_ID_ARTUT = 2313501; DELETE FROM v2.FACTURE WHERE FACTU_NUMERO_FACTURE = 'F2313205';");
Exiger((await EcranComptabilisation.JourneesAsync(client, Greffe)) is { Anomalie: null, Total: 2 }, "comptabilisation : jumeau, la facture retiree -> les journees reviennent");
Exiger((await EcranComptabilisation.JourneesAsync(null, null)) is { Mode: "demo", Journees.Length: 3, EncaissementsSeuls: true },
    "pont Comptabilisation sans serveur : la demonstration, mode « demo »");
Exiger((await EcranComptabilisation.JourneesAsync(client, GreffeAbsent)) is { Mode: "erreur", Journees.Length: 0, Message: not null },
    "pont Comptabilisation, greffe absent : le motif, AUCUNE journee");
await Sql("UPDATE v2.FACTURE SET FACTU_DATE_COMPTABILISATION = NULL WHERE FACTU_DATE_COMPTABILISATION = '19000101';");

Console.WriteLine();
if (echecs.Count == 0) { Console.WriteLine("Ecrans faits main branches sur la base : VERT."); return 0; }
Console.WriteLine(echecs.Count + " echec(s) : " + string.Join(" | ", echecs));
return 1;
