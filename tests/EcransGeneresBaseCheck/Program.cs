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
//      au mot pres ; pour une Saisie (seule forme a etapes), rangs de marches et regle des etapes (AFaire aucune
//      marche, EnCours au moins une, Validee toutes les obligatoires) ; et au moins une ligne sur le banc.
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
//     s'ecrit {J-5}, {M-16} ou {A+1} (jours, mois, annees), le premier ou le dernier jour d'un mois {D-1}, {F-1} ; les
//     lignes vides et celles qui commencent par # sont ignorees : y dire pourquoi chaque refus est refuse. Chaque refus a
//     son jumeau accepte (meme borne, autre cote). Les CELLULES d'une ligne (LigneLue.Valeurs, Referentiel et Etat) se
//     controlent en ajoutant « | valeurs: a ; b ; c » a la ligne attendue ; sans ce champ, elles ne sont pas comparees.
// Puis la section 4 controle tout lecteur contre son descripteur.
//
// LE SOCLE FONCTIONNEL (plan « RIG v3 100 % fonctionnel », lot 2, 26/11/2026) : la recherche, la fiche et les GESTES. Le
// banc trouve aussi les ECRIVAINS (EcrivainEcran.cs) et les joue par la route POST, au nom de l'agent du banc :
//   5. le socle : la garde d'ecriture (v2.* et rec.* seulement), la route des gestes (401, 404, sans ecrivain =
//      Demonstration), le pont (geste, fiche, recherche, ecran reuni) ;
//   6. pour BRANCHER LES GESTES D'UN ECRAN, a cote de ses jeux de lecture, deux fichiers neufs (aucune ligne d'ici) :
//      - <PROCEDURE>.gestes (obligatoire pour tout ecrivain) : des BLOCS separes par une ligne « --- », joues dans l'ordre,
//        APRES tous les .sql et .attendu. Dans un bloc (# et lignes vides ignores) :
//          geste: <TypeGeste> [<nom du geste>]      Enregistrer, Creer, Ajouter, Modifier, Supprimer, Agir Relancer...
//          ids: <id>,<id>                           les LigneLue.Id vises (vide pour Creer / Ajouter)
//          valeur: <cle> = <valeur>                 une par valeur saisie ; cle « Section › Libelle » ou colonne
//          resultat: fait | refuse | sans-ecrivain
//          message: <texte exact>                   ce que l'ecran affichera (le message de RIG v2 pour un refus)
//          lignes:                                  facultatif (fait) : puis TOUTES les lignes relues, format .attendu
//          fiche: <id>                              facultatif : puis des « Section › Libelle = valeur » que la fiche relue porte ;
//                                                   la ligne n d'une sous-liste (Tableau) : « Section › Libelle › n = cellule | cellule »
//          controle: <SELECT d'une valeur> => <attendu>   facultatif : ce que RIG v2 ecrit en plus (dates, etats...)
//          agent: <compte Windows>                  facultatif : le bloc est joue au nom de ce compte (cree par le .sql du jeu,
//                                                   admis dans le greffe) au lieu de l'agent du banc -- un geste qui exige un
//                                                   droit que l'agent du banc n'a pas (PROC_PARAM_IP : modifier le greffe, lot 14)
//        Un refus est verifie SANS ECRITURE : lignes relues identiques, journal des gestes inchange. Un geste fait ajoute au
//        moins une ligne au journal des gestes. Chaque refus a son jumeau accepte, dit en commentaire ;
//      - <PROCEDURE>.recherches (pour un lecteur qui cherche, IRechercheEcran) : des blocs « texte: », « critere: <Libelle> =
//        <valeur> » (repetable), puis « lignes: » et les lignes attendues, format .attendu (aucune : la recherche ne rend rien).
//        Jouees apres les .attendu, avant les .gestes ;
//   7. section 4 : tout ecrivain a un descripteur, un jeu .gestes, et des types de geste de sa forme ; tout lecteur qui
//      cherche declare des criteres du descripteur (ses Criteres, ou les Parametres d'une Audience : la date, la chambre
//      que l'ecran envoie) ; toute fiche lue (les trois premieres lignes qui portent un Id) a des sections et des champs du
//      descripteur (ses Sections, ou son Resultat pour une Operation), et n'a de champ modifiable que si la procedure a un
//      ecrivain qui l'enregistre (Enregistrer pour une Fiche, Modifier pour un Referentiel, dont la grille saisit les
//      colonnes de meme libelle).
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
// Une date relative au jour du banc ({J-5}, {M-16}, {A+1} : jours, mois, annees), dans une ligne attendue d'un .attendu ou
// d'un bloc « lignes: » d'un .gestes (lot 8 : une relance relue porte sa date et sa date limite).
// fix-ok: lu le 26/11/2026 (lot 8) -- les lignes « lignes: » d'un .gestes etaient comparees brutes (Comparer(apres,
// bloc.Lignes)), sans le remplacement {J-n} que seuls les .attendu recevaient : une ligne datee ne pouvait pas y etre ecrite.
// fix-ok: mesure le 26/11/2026 (lot 8, PROC_IPSUIVI.recherches) -- meme defaut pour les lignes d'un .recherches (bloc et route) :
// une IP trouvee par son numero porte les dates de ses actes ; elles recoivent maintenant le meme remplacement.
// {D-1} / {F-1} : le premier / le dernier jour du mois M-1 (lot 9 : la periode par defaut de RIG v2, « le mois precedent
// complet », PROC_STATCA OPE_RECHERCHE.cs:37-58, ne s'ecrivait pas en jours relatifs, qui changent avec le jour du mois).
string Dates(string ligne) => System.Text.RegularExpressions.Regex.Replace(ligne, @"\{([JMADF])([+-]\d+)\}", m =>
{
    var n = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
    var mois = new DateTime(jour.Year, jour.Month, 1).AddMonths(n);
    return D(m.Groups[1].Value switch
    {
        "J" => jour.AddDays(n), "M" => jour.AddMonths(n), "D" => mois, "F" => mois.AddMonths(1).AddDays(-1), _ => jour.AddYears(n),
    });
});

// Une ligne attendue qui finit par « | valeurs: a ; b » compare AUSSI les cellules de la ligne lue (LigneLue.Valeurs) :
// lot 9 -- les chiffres d'un Etat (PROC_STATCA) sont dans ses cellules, que Texte ne montre pas ; sans ce champ, rien ne change.
// fix-ok: mesure le 27/11/2026 (lot 9) -- Texte (plus haut) ne porte pas les cellules : aucun jeu ne pouvait voir un chiffre faux
// d'un Etat. Le stock de PROC_STATCA fausse expres (B + C - D) sort ROUGE au bloc 1 de PROC_STATCA.recherches, qui compare ses
// « valeurs: », puis vert une fois le calcul remis. Trois
// retouches de ce fichier pour une seule cause : l'en-tete (format), Dates ({D-1}/{F-1}), et la route des recherches (sans « valeurs: »).
const string ChampValeurs = " | valeurs: ";
void Comparer(IReadOnlyList<LigneLue> lues, string[] attendues, string quoi)
{
    var obtenues = lues.Select((l, k) => k < attendues.Length && attendues[k].Contains(ChampValeurs)
        ? Texte(l) + ChampValeurs + string.Join(" ; ", l.Valeurs) : Texte(l)).ToArray();
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
var autres = reuniDca.Membres.Where(m => m.Code != "PROC_DCA").ToArray();
// L'ecran reuni « Dépôts au registre » (DCA, DAS, DASBEN) a TOUS ses membres branches depuis le lot 5 du plan plan-676a4b19 :
// il est lu en base APRES les jeux (section 3b, plus bas), comme celui des radiations d'office. Le mode « mixte » (un membre
// branche, les autres en exemple) reste controle ci-dessous par le serveur factice : DCA branche, les autres sans lecteur.
// fix-ok: mesure le 26/11/2026 (lot 5) -- ici, avant les jeux, le banc rendait ROUGE ce controle : le lecteur de DAS
// echoue (SQL 207, colonne inconnue) sur des colonnes de v2.DEMANDE que seul son jeu ajoute, et l'ecran n'est plus mixte.
// Un groupe BRANCHE mais sans aucune ligne (la copie de dev n'en garde que 200 par table) ne disparait pas : la vue le
// porte dans Vides, l'ecran le montre avec « Aucune ligne ». Serveur factice : DCA vide, les autres procedures sans lecteur.
using var videHandler = new RepondeurVide(descripteurDca.Code);
var clientVide = new ClientServeur(new Uri("http://localhost/"), videHandler);
var reuniVide = await EcranGenere.LignesAsync(clientVide, Greffe, reuniDca.Code);
Exiger(reuniVide.Vides.SequenceEqual([descripteurDca.Titre]) && !reuniVide.Exemples.Contains(descripteurDca.Titre)
       && reuniVide.Lignes.All(l => l.Groupe != descripteurDca.Titre) && reuniVide.Exemples.SequenceEqual(autres.Select(m => m.Titre)),
    $"pont, ecran reuni, procedure branchee SANS ligne : « {descripteurDca.Titre} » est dans Vides, pas dans Exemples ({string.Join(", ", reuniVide.Vides)})");
Exiger(base_.Vides.Length == 0, "pont : une procedure qui a des lignes n'est jamais dans Vides");
// Une liste COUPEE au plafond du serveur (la base a plus de lignes) : le pont en nomme les groupes dans Plafonnees, et
// FormeEtat refuse alors d'imprimer -- RIG v2 imprime le registre entier (OPE_RESULTATS.cs:65-73 ; lot 6 du plan-676a4b19).
using var plafondHandler = new RepondeurPlafond();
var coupee = await EcranGenere.LignesAsync(new ClientServeur(new Uri("http://localhost/"), plafondHandler), Greffe, "PROC_REGJUD");
Exiger(coupee.Mode == "base" && coupee.Plafonnees.SequenceEqual(CatalogueGenere.Descripteur("PROC_REGJUD")!.Groupes)
       && coupee.Message == "Les 1 premières lignes sur 1194.",
    $"pont, liste coupee au plafond : tous les groupes de la procedure dans Plafonnees, le message dit combien ({string.Join(", ", coupee.Plafonnees)} ; {coupee.Message})");
Exiger(base_.Plafonnees.Length == 0, "pont : une liste complete n'a aucun groupe coupe");
// Un lecteur qui plafonne CHAQUE groupe (LignesEcran.TotalParGroupe : les trois registres de PROC_REGJUD) : seul le groupe dont la
// base a plus de lignes est coupe -- un registre complet reste imprimable a cote (controle final du lot 6, 26/11/2026).
using var parGroupeHandler = new RepondeurPlafond(new Dictionary<string, int>
    { ["Répertoire général"] = 1194, ["Injonctions de payer"] = 1, ["Ordonnances du président"] = 0 });
var coupeeParGroupe = await EcranGenere.LignesAsync(new ClientServeur(new Uri("http://localhost/"), parGroupeHandler), Greffe, "PROC_REGJUD");
Exiger(coupeeParGroupe.Mode == "base" && coupeeParGroupe.Plafonnees.SequenceEqual(["Répertoire général"])
       && coupeeParGroupe.Message == "Répertoire général : les 1 premières lignes sur 1194.",
    $"pont, plafond par groupe : seul le registre coupe est dans Plafonnees, le message le nomme ({string.Join(", ", coupeeParGroupe.Plafonnees)} ; {coupeeParGroupe.Message})");
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
        .Select(l => Dates(l.Trim()))
        .ToArray();
    var lues = await Lire(code);
    Comparer(lues.Lignes, attendues, $"{code} : les lignes de {Path.GetFileName(fichier)}, dans l'ordre");
    Exiger(lues.Total == attendues.Length, $"{code} : total = {attendues.Length} lignes attendues ({lues.Total})");
}
// « Registres judiciaires » : UN PLAFOND PAR REGISTRE (LecteurRegistresJudiciaires.cs, en tete). fix-ok: mesure au controle final
// du lot 6 (26/11/2026) -- un plafond commun, trie registre par registre, ne rendait que le repertoire general des qu'il atteignait le
// plafond (capture 14 : « Injonctions de payer 0 », « Ordonnances du président 0 »). Plafond 1 : la premiere ligne de CHAQUE
// registre, et le total de chacun ; par la route et le pont, une liste complete n'a aucun registre coupe.
var groupesRegjud = CatalogueGenere.Descripteur("PROC_REGJUD")!.Groupes;
var registresComplets = await Lire("PROC_REGJUD");
var registresPlafond1 = await Lire("PROC_REGJUD", plafond: 1);
var lignesParRegistre = registresComplets.Lignes.GroupBy(l => l.Groupe).ToDictionary(g => g.Key, g => g.Count());
Exiger(groupesRegjud.All(lignesParRegistre.ContainsKey) && lignesParRegistre.Values.Any(n => n > 1)
       && registresPlafond1.Lignes.Select(Texte).SequenceEqual(registresComplets.Lignes.GroupBy(l => l.Groupe).Select(g => Texte(g.First())))
       && registresPlafond1.Total == registresComplets.Total && registresPlafond1.TotalParGroupe is { } totauxRegjud
       && groupesRegjud.All(g => totauxRegjud.GetValueOrDefault(g) == lignesParRegistre[g]),
    $"PROC_REGJUD, plafond 1 : la premiere ligne de chaque registre et le total de chacun ({string.Join(", ", registresPlafond1.Lignes.Select(l => l.Groupe))} ; " +
    $"totaux {string.Join(", ", registresPlafond1.TotalParGroupe?.Select(t => $"{t.Key} {t.Value}") ?? [])})");
var vueRegistres = await EcranGenere.LignesAsync(client, Greffe, "PROC_REGJUD");
Exiger(vueRegistres.Mode == "base" && vueRegistres.Plafonnees.Length == 0 && vueRegistres.Message is null
       && vueRegistres.Lignes.Length == registresComplets.Lignes.Count,
    $"pont, PROC_REGJUD par la route : registres complets, aucun registre coupe ({string.Join(", ", vueRegistres.Plafonnees)} ; {vueRegistres.Message})");

// « Tables de référence » : UN PLAFOND PAR TABLE (LecteurTablesReference.Requete). fix-ok: controle final du lot 14 (3e reprise,
// 27/11/2026) -- un plafond commun aux trois tables (TOP sur leur union) cachait 21 des 128 types de depot AJMJ de la copie de dev
// sans le dire (89 + 4 + 107 = 200 « sur 221 », capture 05) ; RIG v2 ouvre chaque table a part, en entier (OPE_SELECTION_TABLE,
// FactoryHelper.TEXTE_REQUETE_VALEUR sans TOP). Plafond 1 : le premier code de chaque table et le total de chacune.
var groupesTabref = CatalogueGenere.Descripteur("PROC_GTABREF")!.Groupes;
var tablesCompletes = await Lire("PROC_GTABREF");
var tablesPlafond1 = await Lire("PROC_GTABREF", plafond: 1);
var codesParTable = tablesCompletes.Lignes.GroupBy(l => l.Groupe).ToDictionary(g => g.Key, g => g.Count());
Exiger(groupesTabref.All(codesParTable.ContainsKey) && codesParTable.Values.All(n => n > 1)
       && tablesPlafond1.Lignes.Select(Texte).SequenceEqual(tablesCompletes.Lignes.GroupBy(l => l.Groupe).Select(g => Texte(g.First())))
       && tablesPlafond1.Total == tablesCompletes.Total && tablesPlafond1.TotalParGroupe is { } totauxTabref
       && groupesTabref.All(g => totauxTabref.GetValueOrDefault(g) == codesParTable[g]),
    $"PROC_GTABREF, plafond 1 : le premier code de chaque table et le total de chacune ({string.Join(", ", tablesPlafond1.Lignes.Select(l => l.Groupe))} ; " +
    $"totaux {string.Join(", ", tablesPlafond1.TotalParGroupe?.Select(t => $"{t.Key} {t.Value}") ?? [])})");

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
// « Dépôts au registre » : DCA, DAS et DASBEN branches (lot 5 du plan plan-676a4b19) -- mode base, aucun exemple, chaque
// groupe porte exactement autant de lignes que le lecteur de son membre en rend.
var lignesDepots = new Dictionary<string, int>(StringComparer.Ordinal);
foreach (var m in reuniDca.Membres) lignesDepots[m.Titre] = (await Lire(m.Code)).Lignes.Count;
var baseDepots = await EcranGenere.LignesAsync(client, Greffe, reuniDca.Code);
Exiger(baseDepots.Mode == "base" && baseDepots.Exemples.Length == 0 && baseDepots.Message is null
       && reuniDca.Membres.All(m => baseDepots.Lignes.Count(l => l.Groupe == m.Titre) == lignesDepots[m.Titre]),
    $"pont, ecran reuni « {reuniDca.Titre} » (tous ses membres branches) : mode base, aucun exemple, chaque groupe a les lignes " +
    $"de son lecteur ({string.Join(", ", lignesDepots.Select(p => $"{p.Key} {p.Value}"))} ; mode {baseDepots.Mode}, exemples : " +
    $"{string.Join(", ", baseDepots.Exemples)})");

// ============================================================ 3c. LE SOCLE FONCTIONNEL : RECHERCHES, GESTES (en-tete, 5 et 6)

// La garde d'ecriture : seules v2.* et rec.* de la base du greffe (chaque refus a son jumeau accepte).
bool Ecrit(string sql) { try { GesteEnCours.ExigerEcritureV2(sql); return true; } catch (InvalidOperationException) { return false; } }
Exiger(Ecrit("UPDATE v2.INSTANCE SET INSTN_NOTE_AUDIENCE = @n WHERE INSTN_ID_INSTN = @id") && !Ecrit("UPDATE soc.Compte SET Nom = @n WHERE Id = @id")
       && Ecrit("INSERT INTO rec.DossierRcs (Id) VALUES (@id)") && !Ecrit("DELETE FROM dbo.INSTANCE WHERE INSTN_ID_INSTN = @id")
       && Ecrit("MERGE INTO v2.PARTIE AS p USING (SELECT 1 AS x) AS s ON 1 = 0 WHEN MATCHED THEN UPDATE SET PRT_NOM = @n;")
       && !Ecrit("DROP TABLE v2.INSTANCE") && !Ecrit("SELECT 1"),
    "socle : un ecrivain n'ecrit que v2.* et rec.* (UPDATE v2 / rec accepte, soc et dbo refuses, MERGE v2 accepte, DROP et SELECT refuses)");

var ecrivains = EcrivainsEcrans.Decouvrir(typeof(ServeurRig).Assembly);
Exiger(ecrivains.ParProcedure.ContainsKey("PROC_MJUD"),
    $"les ecrivains sont trouves par reflexion, sans liste : {string.Join(", ", ecrivains.ParProcedure.Keys.Order())}");

// La route des gestes : sans session 401, greffe absent 404, procedure sans ecrivain = Demonstration (rien d'ecrit).
var gesteVide = new GesteEcran(TypeGeste.Agir, ["1"], new Dictionary<string, string>()) { Nom = "Relancer" };
var gesteAnonyme = await anonyme.PostAsJsonAsync(RoutesEcransGeneres.GestesPour(Greffe, "PROC_MJUD"), gesteVide, json);
var gesteAbsent = await http.PostAsJsonAsync(RoutesEcransGeneres.GestesPour(GreffeAbsent, "PROC_MJUD"), gesteVide, json);
var sansEcrivain = await (await http.PostAsJsonAsync(RoutesEcransGeneres.GestesPour(Greffe, "PROC_SANS_ECRIVAIN"), gesteVide, json))
    .Content.ReadFromJsonAsync<ResultatGeste>(json);
var typeAbsent = await (await http.PostAsJsonAsync(RoutesEcransGeneres.GestesPour(Greffe, "PROC_MJUD"), gesteVide, json))
    .Content.ReadFromJsonAsync<ResultatGeste>(json);
Exiger(gesteAnonyme.StatusCode == HttpStatusCode.Unauthorized && gesteAbsent.StatusCode == HttpStatusCode.NotFound
       && sansEcrivain is { Fait: false, SansEcrivain: true } && typeAbsent is { Fait: false, SansEcrivain: true },
    $"route des gestes : sans session 401 ({(int)gesteAnonyme.StatusCode}), greffe absent 404 ({(int)gesteAbsent.StatusCode}), " +
    $"sans ecrivain ou geste non ecrit = Demonstration ({sansEcrivain?.Message} / {typeAbsent?.Message})");

var dossierJeux = Path.Combine(racine, "tests", "EcransGeneresBaseCheck", "Jeux");
async Task<string> Valeur(string sql)
{
    await using var cn = new SqlConnection(greffe.Connexion);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn);
    return Convert.ToString(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
}

// Les RECHERCHES (<PROCEDURE>.recherches), avant les gestes.
foreach (var fichier in Directory.GetFiles(dossierJeux, "*.recherches").Order(StringComparer.Ordinal))
{
    var code = Path.GetFileNameWithoutExtension(fichier);
    if (lecteurs.ParProcedure.GetValueOrDefault(code) is not IRechercheEcran chercheur)
    {
        Exiger(false, $"jeu {code}.recherches : aucun lecteur qui cherche (IRechercheEcran) pour cette procedure");
        continue;
    }
    var rang = 0;
    foreach (var bloc in Blocs.Lire(File.ReadAllLines(fichier)))
    {
        rang++;
        var recherche = new RechercheEcran(bloc.Texte, bloc.Criteres);
        await using var cn = new SqlConnection(greffe.Connexion);
        await cn.OpenAsync();
        var lues = await chercheur.ChercherAsync(cn, recherche, jour, 200, CancellationToken.None);
        Comparer(lues.Lignes, [.. (bloc.Lignes ?? []).Select(Dates)], $"{code}.recherches bloc {rang} (texte « {bloc.Texte} », criteres {string.Join(", ", bloc.Criteres.Select(c => $"{c.Key} = {c.Value}"))})");
        Exiger(lues.Total == (bloc.Lignes?.Count ?? 0), $"{code}.recherches bloc {rang} : total = {bloc.Lignes?.Count ?? 0} ({lues.Total})");
    }
    // La meme recherche par la route : Cherchee, Chercheur ; sans recherche : Chercheur seul.
    var premier = Blocs.Lire(File.ReadAllLines(fichier)).First();
    var parRouteCherchee = await http.GetFromJsonAsync<LignesEcran>(RoutesEcransGeneres.Pour(Greffe, code, new RechercheEcran(premier.Texte, premier.Criteres)), json);
    var parRouteSans = await http.GetFromJsonAsync<LignesEcran>(RoutesEcransGeneres.Pour(Greffe, code), json);
    // Par la route, les lignes seules (sans le champ « valeurs: », que Comparer controle au bloc 1 ci-dessus).
    Exiger(parRouteCherchee is { Cherchee: true, Chercheur: true } && parRouteCherchee.Lignes.Select(Texte).SequenceEqual((premier.Lignes ?? [])
               .Select(a => Dates(a.Contains(ChampValeurs) ? a[..a.IndexOf(ChampValeurs, StringComparison.Ordinal)] : a)))
           && parRouteSans is { Cherchee: false, Chercheur: true },
        $"{code} : la recherche passe par la route (texte et criteres en parametres d'adresse), Cherchee ; sans recherche, la liste de travail");
}

// PREUVE A L'ECRAN SUR LES JEUX (plan-676a4b19, lot 11, accord du chef du 27/11/2026 : « une copie jetable, servie par ton propre
// serveur ») : RIGV3_BANC_GARDER = un chemin de fichier. Les jeux sont joues, leurs gestes PAS encore : le banc y ecrit sa base
// commune, son greffe et son agent, puis attend que ce fichier disparaisse. Un serveur lance a part sur cette base commune
// (ConnectionStrings__RigV3) sert alors a l'application les lignes des jeux, pour y jouer un geste a l'ecran. Le fichier supprime,
// le banc s'arrete (code 3, gestes et section 4 non joues) et SUPPRIME ses bases (await using). Sans la variable : rien ne change.
if (Environment.GetEnvironmentVariable("RIGV3_BANC_GARDER") is { Length: > 0 } garde)
{
    await File.WriteAllTextAsync(garde, $"commune={commune.Nom}\ngreffe={Greffe}\nbase={greffe.Nom}\nagent={Agent}\n");
    Console.WriteLine($"Bases gardees pour une preuve a l'ecran (commune {commune.Nom}) : supprimer {garde} pour les rendre.");
    while (File.Exists(garde)) await Task.Delay(1000);
    return 3;
}

// « Releve de compte » (PROC_RELCLI) SANS le droit comptable : le premier controle de RIG v2 (PROC_RELCLI.cs:72-73, la fonction
// EXPCOMPTA par les roles de l'agent ; EcrivainRelevesClients.cs). Le droit que PROC_RELCLI.sql donne au role de l'agent du banc est
// retire, le geste du bloc 3 de PROC_RELCLI.gestes est joue tel quel : refus au message de RIG v2, aucun solde ecrit, journal inchange.
// Le droit est remis AVANT la boucle des gestes : le bloc 3 lui-meme, joue ensuite dans le meme etat de base, est le jumeau accepte.
await Sql("DELETE FROM soc.RoleFonction WHERE Role = 'BANC' AND Fonction = 'EXPCOMPTA'");
var soldesAvantDroit = await Valeur("SELECT COUNT(*) FROM v2.CLIENT_SOLDE");
var journalAvantDroit = await Valeur("SELECT COUNT(*) FROM soc.Journal");
var reponseSansDroit = await http.PostAsJsonAsync(RoutesEcransGeneres.GestesPour(Greffe, "PROC_RELCLI"),
    new GesteEcran(TypeGeste.Agir, ["2207212"], new Dictionary<string, string> { ["Date de début"] = "01/01/2000", ["Date de fin"] = "31/12/2099" })
    { Nom = "Valider les soldes" }, json);
var sansDroit = await reponseSansDroit.Content.ReadFromJsonAsync<ResultatGeste>(json);
Exiger(reponseSansDroit.StatusCode == HttpStatusCode.UnprocessableEntity && sansDroit is { Fait: false }
       && sansDroit.Message == "Droits insuffisants : seul un utilisateur ayant les droits comptables peut effectuer cette opération. " +
                               "Si la validation de cette période est nécessaire, demandez à votre comptable d'effectuer l'opération."
       && await Valeur("SELECT COUNT(*) FROM v2.CLIENT_SOLDE") == soldesAvantDroit
       && await Valeur("SELECT COUNT(*) FROM soc.Journal") == journalAvantDroit,
    $"PROC_RELCLI, Valider les soldes SANS le droit EXPCOMPTA : refus de RIG v2, aucun solde ni trace ({(int)reponseSansDroit.StatusCode} « {sansDroit?.Message} »)");
await Sql("INSERT INTO soc.RoleFonction (Role, Fonction) VALUES ('BANC', 'EXPCOMPTA')");

// Les GESTES (<PROCEDURE>.gestes), joues par la route POST, dans l'ordre ; un bloc « agent: » au nom de son compte (en-tete, 6).
// fix-ok: pas un correctif -- lot 14 (27/11/2026) : l'agent du banc est cree sans EstAmitel ni EstAdministrateur (section 0, defauts 0 de
// SOC-001_comptes.sql:33-34), donc un geste qui exige le droit de modifier le greffe (EcritureReglage) n'aurait ici que des refus.
var clientsParAgent = new Dictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase);
HttpClient ClientDe(string? agent)
{
    if (agent is null) return http;
    if (!clientsParAgent.TryGetValue(agent, out var client))
    {
        client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(RoutesIdentite.EnTeteCompte, agent);
        clientsParAgent[agent] = client;
    }
    return client;
}
foreach (var fichier in Directory.GetFiles(dossierJeux, "*.gestes").Order(StringComparer.Ordinal))
{
    var code = Path.GetFileNameWithoutExtension(fichier);
    if (!ecrivains.ParProcedure.ContainsKey(code)) { Exiger(false, $"jeu {code}.gestes : aucun ecrivain pour cette procedure"); continue; }
    var rang = 0;
    foreach (var bloc in Blocs.Lire(File.ReadAllLines(fichier)))
    {
        rang++;
        var quoi = $"{code}.gestes bloc {rang} ({bloc.Geste?.Type} {string.Join(",", bloc.Geste?.Ids ?? [])})";
        if (bloc.Geste is null || bloc.Resultat is null) { Exiger(false, $"{quoi} : « geste: » et « resultat: » sont obligatoires"); continue; }
        var avant = (await Lire(code)).Lignes.Select(Texte).ToArray();
        var journalAvant = await Valeur("SELECT COUNT(*) FROM soc.Journal");
        var reponse = await ClientDe(bloc.Agent).PostAsJsonAsync(RoutesEcransGeneres.GestesPour(Greffe, code), bloc.Geste, json);
        var r = await reponse.Content.ReadFromJsonAsync<ResultatGeste>(json);
        var obtenu = r is null ? "illisible" : r.SansEcrivain ? "sans-ecrivain" : r.Fait ? "fait" : "refuse";
        var statut = obtenu == "refuse" ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.OK;
        Exiger(obtenu == bloc.Resultat && reponse.StatusCode == statut && (bloc.Message is null || r?.Message == bloc.Message),
            $"{quoi} : {bloc.Resultat} « {bloc.Message} » (obtenu {obtenu} {(int)reponse.StatusCode} « {r?.Message} »)");
        var apres = (await Lire(code)).Lignes;
        var journalApres = await Valeur("SELECT COUNT(*) FROM soc.Journal");
        if (bloc.Resultat == "fait")
        {
            Exiger(int.Parse(journalApres) > int.Parse(journalAvant), $"{quoi} : trace au journal des gestes ({journalAvant} -> {journalApres})");
            if (bloc.Lignes is not null) Comparer(apres, [.. bloc.Lignes.Select(Dates)], $"{quoi} : les lignes relues dans la base apres le geste");
        }
        else
            Exiger(journalApres == journalAvant && apres.Select(Texte).SequenceEqual(avant),
                $"{quoi} : refuse, RIEN d'ecrit (lignes relues identiques, journal des gestes inchange : {journalAvant} -> {journalApres})");
        if (bloc.Fiche is { } ficheId)
        {
            await using var cn = new SqlConnection(greffe.Connexion);
            await cn.OpenAsync();
            var fiche = lecteurs.ParProcedure.GetValueOrDefault(code) is IFicheEcran f ? await f.LireFicheAsync(cn, ficheId, DateTime.Now, CancellationToken.None) : null;
            foreach (var (cle, attendue) in bloc.ValeursFiche)
            {
                var lue = fiche?.Sections.SelectMany(s => s.Champs.SelectMany(c => c.Lignes
                        .Select((l, i) => (Cle: $"{GesteEcran.Cle(s.Titre, c.Libelle)} › {i + 1}", Valeur: string.Join(" | ", l)))
                        .Prepend((Cle: GesteEcran.Cle(s.Titre, c.Libelle), c.Valeur))))
                    .FirstOrDefault(c => c.Cle == cle).Valeur;
                Exiger(lue == attendue, $"{quoi} : la fiche {ficheId} relue porte « {cle} » = « {attendue} » (lue : « {lue ?? "absent"} »)");
            }
        }
        foreach (var (sql, attendue) in bloc.Controles)
        {
            var lue = await Valeur(sql);
            Exiger(lue == attendue, $"{quoi} : controle « {sql} » = {attendue} (lu : {lue})");
        }
    }
}

// « Registres judiciaires » SANS le reglage d'edition REP_GENERAL (EcrivainRegistresJudiciaires.cs, en tete) : la V2 ne cree alors
// AUCUNE demande pour le repertoire general (DemandeEdition.cs:2582-2584, OPE_REGISTRE_ABSTRACT.cs:120). Le reglage du jeu
// (PROC_REGJUD.sql, joue avec lui dans PROC_REGJUD.gestes bloc 3) est efface. Refus : le repertoire general seul, rien d'ecrit ;
// jumeau accepte : envoye avec une decision des IP, les IP partent seuls et le message dit pourquoi le repertoire n'est pas imprime.
await Sql("DELETE FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = 'REP_GENERAL'");
const string SansReglageRepGeneral = "Le répertoire général n'est pas imprimé : le réglage d'édition REP_GENERAL n'existe pas dans ce greffe, et RIG v2 ne crée alors aucune demande.";
var demandesAvantRegjud = await Valeur("SELECT COUNT(*) FROM v2.DEMANDE_EDITION");
var journalAvantRegjud = await Valeur("SELECT COUNT(*) FROM soc.Journal");
var reponseSansReglage = await http.PostAsJsonAsync(RoutesEcransGeneres.GestesPour(Greffe, "PROC_REGJUD"),
    new GesteEcran(TypeGeste.Agir, ["G-1601201"], new Dictionary<string, string>()) { Nom = "Impression" }, json);
var sansReglage = await reponseSansReglage.Content.ReadFromJsonAsync<ResultatGeste>(json);
Exiger(reponseSansReglage.StatusCode == HttpStatusCode.UnprocessableEntity && sansReglage is { Fait: false } && sansReglage.Message == SansReglageRepGeneral
       && await Valeur("SELECT COUNT(*) FROM v2.DEMANDE_EDITION") == demandesAvantRegjud
       && await Valeur("SELECT COUNT(*) FROM soc.Journal") == journalAvantRegjud,
    $"PROC_REGJUD, Impression du repertoire general SANS reglage REP_GENERAL : refus, aucune demande ni trace ({(int)reponseSansReglage.StatusCode} « {sansReglage?.Message} »)");
var reponseMixte = await http.PostAsJsonAsync(RoutesEcransGeneres.GestesPour(Greffe, "PROC_REGJUD"),
    new GesteEcran(TypeGeste.Agir, ["G-1601201", "I-1601221"], new Dictionary<string, string>()) { Nom = "Impression" }, json);
var mixte = await reponseMixte.Content.ReadFromJsonAsync<ResultatGeste>(json);
Exiger(reponseMixte.StatusCode == HttpStatusCode.OK && mixte is { Fait: true }
       && mixte.Message == "Registre des 1 décisions des IP : placé dans la file des éditions. " + SansReglageRepGeneral
       && int.Parse(await Valeur("SELECT COUNT(*) FROM v2.DEMANDE_EDITION")) == int.Parse(demandesAvantRegjud) + 1
       && await Valeur("SELECT COUNT(*) FROM v2.DEMANDE_EDITION WHERE DMNED_LIBELLE_DOCUMENT = N'Répertoire général des 1 instances enrôlées'") == "0",
    $"PROC_REGJUD, jumeau : repertoire general et IP sans reglage -- seule la demande des IP est creee, le message le dit ({(int)reponseMixte.StatusCode} « {mixte?.Message} »)");

// « Publications BODACC », « Éditer » le certificat 2110210 (EcrivainPublicationsBodacc.cs, en tete ; lot 11) : trois refus qui changent
// un reglage que PROC_GESTBOD.gestes lit (la base des jeux est commune), poses ICI apres tous les gestes puis remis. Refus : le reglage
// CERTIF_BOD_DEST vise « PC » (la PC elle-meme, un destinataire que RIG v2 ajoute et que la V3 ne calcule pas) ; CERTIF_BOD_DEST absent ;
// le reglage d'edition CERTIF_BOD absent (CreateDemandeEditionByParametreGeneration rend null). Rien d'ecrit a chaque fois. Jumeau
// accepte : CERTIF_BOD_DEST = « MDJ » seul -- le mandataire au coffre, sans document interne (PROC_GESTBOD.gestes bloc 9 : les deux).
{
    async Task<(HttpStatusCode Statut, ResultatGeste? R)> EditerCertificat()
    {
        var reponse = await http.PostAsJsonAsync(RoutesEcransGeneres.GestesPour(Greffe, "PROC_GESTBOD"),
            new GesteEcran(TypeGeste.Confirmer, ["C-2110210"], new Dictionary<string, string>()) { Nom = "Éditer" }, json);
        return (reponse.StatusCode, await reponse.Content.ReadFromJsonAsync<ResultatGeste>(json));
    }
    const string EditionsBodacc = "SELECT COUNT(*) FROM v2.DEMANDE_EDITION";
    var avantBodacc = await Valeur(EditionsBodacc);
    await Sql("UPDATE soc.Reglage SET Valeur = 'MDJ;PC', AuteurModif = 'banc', DateModif = SYSDATETIME() WHERE Code = 'CERTIF_BOD_DEST'");
    var (statutPc, pc) = await EditerCertificat();
    await Sql("DELETE FROM soc.Reglage WHERE Code = 'CERTIF_BOD_DEST'");
    var (statutAbsent, sansDestinataires) = await EditerCertificat();
    await Sql("INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('CERTIF_BOD_DEST', 'Banc EcransGeneresBase lot 11', 'MDJ')");
    await Sql("UPDATE v2.PARAMETRE_EDITION SET PRMED_CODE = 'CERTIF_BOD_BANC11' WHERE PRMED_CODE = 'CERTIF_BOD'");
    var (statutSansEdition, sansEdition) = await EditerCertificat();
    await Sql("UPDATE v2.PARAMETRE_EDITION SET PRMED_CODE = 'CERTIF_BOD' WHERE PRMED_CODE = 'CERTIF_BOD_BANC11'");
    var refusBodacc = await Valeur(EditionsBodacc);
    var (statutMdj, mdj) = await EditerCertificat();
    var apresBodacc = await Valeur(EditionsBodacc);
    await Sql("UPDATE soc.Reglage SET Valeur = 'MDJ;DI', AuteurModif = 'banc', DateModif = SYSDATETIME() WHERE Code = 'CERTIF_BOD_DEST'");
    Exiger(statutPc == HttpStatusCode.UnprocessableEntity && pc?.Message == "Le réglage CERTIF_BOD_DEST vise « PC », un destinataire que RIG v3 ne calcule pas "
               + "(seuls les mandataires de la procédure et le document interne le sont) : rien n'est édité, faire le certificat dans RIG v2."
           && statutAbsent == HttpStatusCode.UnprocessableEntity
           && sansDestinataires?.Message == "Rien n'est édité : le réglage CERTIF_BOD_DEST (destinataires du certificat) n'existe pas dans ce greffe."
           && statutSansEdition == HttpStatusCode.UnprocessableEntity
           && sansEdition?.Message == "Rien n'est édité : le réglage d'édition CERTIF_BOD n'existe pas dans ce greffe, et RIG v2 ne peut alors pas créer l'édition."
           && refusBodacc == avantBodacc,
        $"PROC_GESTBOD, Éditer sans destinataire calculable, sans CERTIF_BOD_DEST, sans reglage CERTIF_BOD : trois refus, aucune edition "
        + $"({avantBodacc} -> {refusBodacc} ; « {pc?.Message} » / « {sansDestinataires?.Message} » / « {sansEdition?.Message} »)");
    Exiger(statutMdj == HttpStatusCode.OK && mdj is { Fait: true } && int.Parse(apresBodacc) == int.Parse(refusBodacc) + 1
           && mdj.Message == "1 édition placée dans la file des éditions : Certificat de parution au Bodacc, 1990P14101 EP BANC11C du 15/05/2001 00:00:00 "
               + "→ Me MANDAUN Selarl (coffre-fort). Impression et envoi au coffre-fort simulés : le service d'édition de RIG v2 les fait, RIG v3 ne l'appelle pas.",
        $"PROC_GESTBOD, jumeau : CERTIF_BOD_DEST = « MDJ » -- une seule edition, au mandataire ({refusBodacc} -> {apresBodacc} ; « {mdj?.Message} »)");
}

// Le PONT (Pont/EcranGenere.cs) : un geste sans serveur est une demonstration ; sur une ligne de l'ecran reuni
// « Corriger une affaire judiciaire », la fiche, la recherche et un refus de RIG v2 passent par lui.
var corriger = CatalogueGenere.Tous.First(d => d.Membres.Any(m => m.Code == "PROC_MJUD"));
var titreMjud = CatalogueGenere.Descripteur("PROC_MJUD")!.Titre;
var gesteDemo = await EcranGenere.GesteAsync(null, null, corriger.Code, titreMjud,
    new GesteEcran(TypeGeste.Enregistrer, ["1200001"], new Dictionary<string, string> { ["Instance › Saisine tribunal"] = "2026-01-14" }));
var gesteExemple = await EcranGenere.GesteAsync(client, Greffe, corriger.Code, titreMjud,
    new GesteEcran(TypeGeste.Enregistrer, [""], new Dictionary<string, string>()));
var gesteVerrou = await EcranGenere.GesteAsync(client, Greffe, corriger.Code, titreMjud,
    new GesteEcran(TypeGeste.Enregistrer, ["1200002"], new Dictionary<string, string> { ["Instance › Saisine tribunal"] = "2026-01-12" }));
Exiger(gesteDemo is { Fait: false, SansEcrivain: true } && gesteDemo.Message.StartsWith(EcranGenere.Demonstration, StringComparison.Ordinal)
       && gesteExemple is { Fait: false, SansEcrivain: true }
       && gesteVerrou is { Fait: false, SansEcrivain: false } && gesteVerrou.Message.StartsWith("Instance bloquée (26012000002)", StringComparison.Ordinal),
    $"pont, gestes : sans serveur « {gesteDemo.Message} » ; ligne d'exemple « {gesteExemple.Message} » ; instance verrouillee : « {gesteVerrou.Message} »");
var ficheParPont = await EcranGenere.FicheAsync(client, Greffe, corriger.Code, titreMjud, "1200001");
var ficheVerrouParPont = await EcranGenere.FicheAsync(client, Greffe, corriger.Code, titreMjud, "1200002");
// fix-ok: mesure le 26/11/2026 (lot 3) -- ce controle visait PROC_MPC de « Corriger une affaire judiciaire », dont les cinq
// procedures ont depuis leur lecteur de fiche : il prend le premier membre d'un ecran reuni qui n'en a pas, quel que soit le
// lot qui branche les autres.
var (reuniSansFiche, sansFiche) = CatalogueGenere.Tous.SelectMany(d => d.Membres.Select(m => (Reuni: d, Membre: m)))
    .First(x => lecteurs.ParProcedure.GetValueOrDefault(x.Membre.Code) is not IFicheEcran);
var ficheExemple = await EcranGenere.FicheAsync(client, Greffe, reuniSansFiche.Code, sansFiche.Titre, "1");
Exiger(ficheParPont is { Mode: "base", Fiche: { LectureSeule: false } } && ficheParPont.Fiche.Nom == "ALPHA BANC c/ BETA BANC"
       && ficheVerrouParPont.Fiche is { LectureSeule: true, Motif: { } motif } && motif.StartsWith("Instance bloquée", StringComparison.Ordinal)
       && ficheExemple.Mode == "exemple",
    $"pont, fiches : une instance lue dans la base ({ficheParPont.Mode}), une verrouillee en lecture seule avec le message de RIG v2 " +
    $"({ficheVerrouParPont.Fiche?.Motif}), une procedure sans lecteur de fiche en exemple ({ficheExemple.Message})");
// fix-ok: mesure le 26/11/2026 (lot 3) -- ce controle attendait « Instance judiciaire » SEULE dans Cherchees ; les quatre autres
// procedures de l'ecran reuni cherchent depuis dans la base. Il attend maintenant chaque membre qui a un lecteur qui cherche.
var chercheeParPont = await EcranGenere.LignesAsync(client, Greffe, corriger.Code, new RechercheEcran("12003", new Dictionary<string, string>()));
var chercheurs = corriger.Membres.Where(m => lecteurs.ParProcedure.GetValueOrDefault(m.Code) is IRechercheEcran).Select(m => m.Titre).ToArray();
Exiger(chercheeParPont.Recherche && chercheeParPont.Cherchees.Order().SequenceEqual(chercheurs.Order()) && chercheurs.Contains(titreMjud)
       && chercheeParPont.Lignes.Where(l => l.Groupe == titreMjud).Select(l => l.Nom).SequenceEqual(["EPSILON BANC"])
       && chercheeParPont.Lignes.Where(l => l.Groupe == titreMjud).All(l => l.Id is not null),
    $"pont, ecran reuni « {corriger.Titre} » : la recherche part dans la base pour chaque procedure qui cherche ({string.Join(", ", chercheurs)} ; " +
    $"Cherchees : {string.Join(", ", chercheeParPont.Cherchees)}), les lignes de « {titreMjud} » portent leur Id");

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
    // Les Faites ne comptent que les ETAPES d'une Saisie, seule forme qui en garde (lot 1 du plan « RIG v3 100 %
    // fonctionnel », 25/11/2026) : les sections d'une autre forme ne se franchissent pas, aucune forme ne lit Faites --
    // la meme regle qu'EcransGeneresCheck. Mesure : la recherche retiree des sections de PROC_VTRID, son lecteur
    // (Faites [0, 1, 3], comptes sur l'ancien escalier) sortait ici en rouge sans que l'ecran change.
    var etapes = d.Marches.Length > 0;
    var obligatoires = d.Marches.Select((m, k) => (m, k)).Where(x => x.m.Obligatoire).Select(x => x.k).ToArray();
    var fautes = lues.Lignes.Where(l =>
            !d.Groupes.Contains(l.Groupe) || !etats.Contains(l.Etat)
            || (etapes && (l.Faites.Distinct().Count() != l.Faites.Length || l.Faites.Any(k => k < 0 || k >= d.Marches.Length)
                           || (l.Etat == "AFaire" && l.Faites.Length > 0) || (l.Etat == "EnCours" && l.Faites.Length == 0)
                           || (l.Etat == "Validee" && obligatoires.Except(l.Faites).Any()))))
        .Select(Texte).ToList();
    Exiger(fautes.Count == 0, $"{code} : chaque ligne a un groupe et un etat du descripteur, des marches de l'ecran, et suit la regle " +
        $"AFaire / EnCours / Validee ({lues.Lignes.Count} lignes{(fautes.Count > 0 ? " ; fautives : " + string.Join(" // ", fautes) : "")})");
    var couverts = lues.Lignes.Select(l => l.Etat).Distinct().Order().ToArray();
    Console.WriteLine($"NOTE : {code} : etats couverts par le banc : {string.Join(", ", couverts)}");

    // Le socle fonctionnel (en-tete, 7) : les criteres cherches sont ceux que l'ecran envoie (les criteres, ou les
    // parametres d'une audience : FormeAudience, useRecherche) ; une fiche lue a ses sections et ses champs (le resultat
    // d'une operation compris), et n'a de champ modifiable que si un ecrivain l'enregistre (Enregistrer ou Modifier).
    if (lecteur is IRechercheEcran chercheur)
    {
        var envoyes = d.Criteres.Concat(d.Forme == FormeEcran.Audience ? d.Parametres.Select(p => p.Libelle) : []).ToArray();
        Exiger(chercheur.Criteres.All(envoyes.Contains),
            $"{code} : les criteres cherches dans la base ({string.Join(", ", chercheur.Criteres)}) sont ceux que l'ecran envoie ({string.Join(", ", envoyes)})");
    }
    if (lecteur is IFicheEcran fiches)
    {
        var avecId = lues.Lignes.Where(l => l.Id is not null).Take(3).ToList();
        Exiger(avecId.Count > 0, $"{code} : lit des fiches, et ses lignes du banc portent leur Id ({avecId.Count})");
        var gestes = ecrivains.ParProcedure.GetValueOrDefault(code)?.Gestes ?? [];
        var enregistre = gestes.Contains(TypeGeste.Enregistrer) || gestes.Contains(TypeGeste.Modifier);
        var sectionsLues = d.Sections.Concat(d.Resultat).ToArray();
        foreach (var l in avecId)
        {
            await using var cn = new SqlConnection(greffe.Connexion);
            await cn.OpenAsync();
            var f = await fiches.LireFicheAsync(cn, l.Id!, DateTime.Now, CancellationToken.None);
            var horsDescripteur = f?.Sections.SelectMany(s => sectionsLues.FirstOrDefault(x => x.Titre == s.Titre) is { } section
                    ? s.Champs.Where(c => section.Champs.FirstOrDefault(x => x.Libelle == c.Libelle) is not { } champ
                                          || (champ.Type == TypeChamp.Tableau) != (c.Colonnes.Length > 0)
                                          || (c.Modifiable && (!enregistre || champ.Type == TypeChamp.Tableau)))
                               .Select(c => $"{s.Titre} › {c.Libelle}")
                    : [$"section « {s.Titre} »"]).ToList();
            Exiger(f is not null && horsDescripteur!.Count == 0,
                $"{code} : la fiche {l.Id} ({l.Nom}) a les sections et champs du descripteur, ses sous-listes en colonnes, et un " +
                $"champ modifiable seulement s'il s'enregistre{(horsDescripteur is { Count: > 0 } ? " ; fautifs : " + string.Join(", ", horsDescripteur) : "")}");
        }
    }
}

// Tout ECRIVAIN : un descripteur d'une procedure, un jeu .gestes, et des gestes de sa forme (en-tete, 7).
// Un Referentiel joue aussi Agir : les gestes de ligne que sa fiche nomme, au menu de la ligne (« Dupliquer » un document
// ocerise, FormeReferentiel.tsx, lot 11).
var GestesDeLaForme = new Dictionary<FormeEcran, TypeGeste[]>
{
    [FormeEcran.Fiche] = [TypeGeste.Enregistrer], [FormeEcran.Saisie] = [TypeGeste.Creer],
    [FormeEcran.Referentiel] = [TypeGeste.Ajouter, TypeGeste.Modifier, TypeGeste.Supprimer, TypeGeste.Agir], [FormeEcran.File] = [TypeGeste.Agir],
    [FormeEcran.Consultation] = [TypeGeste.Agir], [FormeEcran.Etat] = [TypeGeste.Agir], [FormeEcran.Document] = [TypeGeste.Agir],
    [FormeEcran.Operation] = [TypeGeste.Confirmer], [FormeEcran.Audience] = [TypeGeste.Decider],
};
foreach (var (code, ecrivain) in ecrivains.ParProcedure.OrderBy(p => p.Key, StringComparer.Ordinal))
{
    var d = CatalogueGenere.Descripteur(code);
    Exiger(d is { Membres.Length: 0 } && ecrivain.Gestes.Count > 0 && ecrivain.Gestes.All(GestesDeLaForme[d.Forme].Contains),
        $"{code} : son ecrivain ({ecrivain.GetType().Name}) ecrit une procedure qui a son descripteur, des gestes de sa forme " +
        $"{d?.Forme} ({string.Join(", ", ecrivain.Gestes)})");
    Exiger(File.Exists(Path.Combine(dossierJeux, code + ".gestes")),
        $"{code} : son ecrivain a son jeu tests/EcransGeneresBaseCheck/Jeux/{code}.gestes (en-tete, 6)");
}

// ============================================================ 5. EXPORT EXCEL : GROUPEMENT NON COMPTE ET PLAFOND DE 65 536 (lot 11)
// Deux cas de LecteurListesExcel qu'un jeu .recherches ne peut pas poser sans fausser ses autres blocs (la base des jeux est
// commune) : un reglage du greffe qui filtre le groupement par du texte SQL (LSTXLS_GIE_*_REQUETE, OPE_INSTANCE.cs:51-56 : le
// groupement n'est plus compte) et plus de 65 536 lignes (TextNbOccurence_ChangementValeur, OPE_RECHERCHE.cs:466-472 : la liste
// est bloquee). Poses ICI, apres tous les jeux et tous les lecteurs, puis retires. Chaque refus a son jumeau de l'autre cote.
// Instances de 1966 (aucun jeu ne touche cette annee), identifiants du lot 11 : 2 120 001 a 2 185 537.
{
    var excel = (IRechercheEcran)lecteurs.ParProcedure["PROC_LISTEEXCEL"];
    async Task<LigneLue[]> CompterExcel(string periode)
    {
        await using var cn = new SqlConnection(greffe.Connexion);
        await cn.OpenAsync();
        var criteres = new Dictionary<string, string> { ["Période"] = periode };
        return [.. (await excel.ChercherAsync(cn, new RechercheEcran("", criteres), jour, 200, CancellationToken.None)).Lignes];
    }
    await Sql("INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('LSTXLS_GIE_NATURE_AFFAIRE_REQUETE', 'Banc EcransGeneresBase lot 11', @v)",
        ("@v", "AND INSTN_NATURE_AFFAIRE = 'X'"));
    var gie = (await CompterExcel("1968-03-01|1968-03-31")).Single(l => l.Groupe == "Groupements");
    await Sql("DELETE FROM soc.Reglage WHERE Code = 'LSTXLS_GIE_NATURE_AFFAIRE_REQUETE'");
    var gieCompte = (await CompterExcel("1968-03-01|1968-03-31")).Single(l => l.Groupe == "Groupements");
    Exiger(gie is { Etat: "AFaire", Valeurs: [_, "non compté : filtre SQL du paramétrage"] } && gie.Precisions.Contains("LSTXLS_GIE_NATURE_AFFAIRE_REQUETE", StringComparison.Ordinal)
           && gieCompte is { Etat: "EnCours", Valeurs: [_, "2"] },
        $"PROC_LISTEEXCEL : un reglage SQL du groupement -> « non compté », le reglage nomme ; jumeau sans reglage : compte, 2 ({Texte(gie)} / {Texte(gieCompte)})");

    // 65 536 instances enrolees en mars 1966 : au seuil, comptees (« plus de 65 536 » seulement bloque) ; une de plus : bloquee.
    await Sql("""
        INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT, INSTN_DATE_ENROLEMENT)
        SELECT TOP (65536) 2120000 + n, '1966RA' + RIGHT('00000' + CAST(n AS varchar(6)), 5), 'EXCEL PLAFOND BANC 11', '19660315'
        FROM (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b) AS x;
        """);
    var auSeuil = (await CompterExcel("1966-03-01|1966-03-31")).Single(l => l.Nom == "Liste des instances");
    await Sql("INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT, INSTN_DATE_ENROLEMENT) VALUES (2185537, '1966RB99999', 'EXCEL PLAFOND BANC 11', '19660316')");
    var auDela = (await CompterExcel("1966-03-01|1966-03-31")).Single(l => l.Nom == "Liste des instances");
    await Sql("DELETE FROM v2.INSTANCE WHERE INSTN_ID_INSTN BETWEEN 2120001 AND 2185537");
    Exiger(auSeuil is { Etat: "EnCours", Valeurs: [_, "65536"] } && !auSeuil.Precisions.Contains("au-delà", StringComparison.Ordinal)
           && auDela is { Etat: "EnAttente", Valeurs: [_, "65537"] } && auDela.Precisions.Contains("au-delà de 65 536 lignes", StringComparison.Ordinal),
        $"PROC_LISTEEXCEL : 65 536 lignes comptees ; 65 537 -> « Bloquée : trop de lignes » (OPE_RECHERCHE.cs:467) ({Texte(auSeuil)} / {Texte(auDela)})");
}

// ============================================================ 6. L'APERCU KBIS D'UN VRAI DOSSIER (PROC_XEX, lot 13)
// Pont/ApercuKbisDossier.cs contre ce serveur : la fiche de 2300601 (PROC_XEX.sql) devient l'extrait de Pont/ApercuKbis.cs, mise en
// forme comprise (capital N2 fr, dates jj/mm/aaaa, greffe de l'en-tete par le code greffe du siege) ; sans serveur, aucun extrait.
{
    var kbis = await ApercuKbisDossier.DossierAsync(client, Greffe, "PROC_XEX", "Dossiers RCS", "2300601");
    string Ligne(string titre) => kbis.Kbis?.Sections.SelectMany(s => s.Lignes).FirstOrDefault(l => l.Titre == titre)?.Libelle ?? "absente";
    Exiger(kbis is { Mode: "base", Kbis: { GreffeLibelle: "Greffe du Tribunal de Commerce de Grenoble", VilleRcs: "GRENOBLE", Gestion: "2013B60001" } }
           && Ligne("Immatriculation au RCS, numero") == "523 006 010 R.C.S. GRENOBLE" && Ligne("Date d'immatriculation") == "01/01/2013"
           && Ligne("Forme juridique") == "Société à responsabilité limitée" && Ligne("Capital social") == "15 000,50 EUR (variable)"
           && Ligne("Adresse du siege") == "12 RUE DU BANC KBIS 38000 GRENOBLE" && Ligne("Qualite") == "Président"
           && Ligne("Activite(s) exercee(s)") == "Édition de logiciels" && Ligne("Code NAF") == "5829C" && Ligne("Origine du fonds ou de l'activite") == "Création",
        $"apercu Kbis PROC_XEX : la fiche du dossier 2300601 mise en extrait par Pont/ApercuKbis.cs (greffe {kbis.Kbis?.GreffeLibelle}, capital "
        + $"« {Ligne("Capital social")} », forme « {Ligne("Forme juridique")} », adresse « {Ligne("Adresse du siege")} »)");
    var sansSiege = await ApercuKbisDossier.DossierAsync(client, Greffe, "PROC_XEX", "Dossiers RCS", "2300602");
    Exiger(sansSiege.Kbis is { VilleRcs: var ville } && ville == RigV3Desktop.Greffes.Trouver(Greffe).Ville
           && !sansSiege.Kbis.Sections.Any(s => s.Titre.StartsWith("GESTION", StringComparison.Ordinal)),
        $"apercu Kbis PROC_XEX, jumeau sans siege ni dirigeant : le greffe de la session, aucune section de direction ({sansSiege.Kbis?.VilleRcs})");
    Exiger((await ApercuKbisDossier.DossierAsync(null, null, "PROC_XEX", "Dossiers RCS", "2300601")) is { Mode: "exemple", Kbis: null, Message: not null },
        "apercu Kbis PROC_XEX sans serveur : aucun extrait, le motif");
}

await app.StopAsync();
Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "EcransGeneresBaseCheck : VERT" : $"EcransGeneresBaseCheck : ROUGE ({echecs.Count} echec(s))");
return echecs.Count == 0 ? 0 : 1;

/// <summary>Un bloc d'un jeu .gestes ou .recherches (format : en-tete, 6).</summary>
sealed class Bloc
{
    public GesteEcran? Geste { get; set; }
    public string? Resultat { get; set; }
    public string? Message { get; set; }
    public List<string>? Lignes { get; set; }
    public string? Fiche { get; set; }
    public List<(string Cle, string Valeur)> ValeursFiche { get; } = [];
    public List<(string Sql, string Attendu)> Controles { get; } = [];
    public string? Agent { get; set; }
    public string? Texte { get; set; }
    public Dictionary<string, string> Criteres { get; } = new(StringComparer.Ordinal);
}

/// <summary>La lecture des jeux .gestes et .recherches : des blocs separes par « --- », des lignes « cle: valeur ».</summary>
static class Blocs
{
    private static readonly string[] Cles = ["geste:", "ids:", "valeur:", "resultat:", "message:", "lignes:", "fiche:", "controle:", "agent:", "texte:", "critere:"];

    /// <summary>« cle = valeur » (le premier « = » precede d'un blanc ; une valeur vide est permise).</summary>
    private static (string, string) Paire(string texte)
    {
        var i = texte.IndexOf(" =", StringComparison.Ordinal);
        return i < 0 ? (texte.Trim(), "") : (texte[..i].Trim(), texte[(i + 2)..].Trim());
    }

    public static List<Bloc> Lire(string[] lignes)
    {
        var blocs = new List<Bloc>();
        var b = new Bloc();
        string? suite = null;   // « lignes » ou « fiche » : les lignes qui suivent leur appartiennent
        TypeGeste type = default;
        string? nom = null;
        var ids = new List<string>();
        var valeurs = new Dictionary<string, string>(StringComparer.Ordinal);
        void Clore()
        {
            if (b.Resultat is not null) b.Geste = new GesteEcran(type, [.. ids], new Dictionary<string, string>(valeurs)) { Nom = nom };
            if (b.Resultat is not null || b.Lignes is not null || b.Texte is not null || b.Criteres.Count > 0) blocs.Add(b);
            b = new Bloc(); suite = null; nom = null; ids.Clear(); valeurs.Clear();
        }
        foreach (var brute in lignes)
        {
            var l = brute.Trim();
            if (l.Length == 0 || l.StartsWith('#')) continue;
            if (l == "---") { Clore(); continue; }
            var cle = Cles.FirstOrDefault(c => l.StartsWith(c, StringComparison.Ordinal));
            if (cle is null)
            {
                if (suite == "lignes") b.Lignes!.Add(l);
                else if (suite == "fiche") b.ValeursFiche.Add(Paire(l));
                else throw new InvalidOperationException($"Ligne hors bloc : « {l} »");
                continue;
            }
            var v = l[cle.Length..].Trim();
            suite = null;
            switch (cle)
            {
                case "geste:":
                    var mots = v.Split(' ', 2);
                    type = Enum.Parse<TypeGeste>(mots[0]);
                    nom = mots.Length > 1 ? mots[1] : null;
                    break;
                case "ids:": ids.AddRange(v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
                case "valeur:": var (k, x) = Paire(v); valeurs[k] = x; break;
                case "resultat:": b.Resultat = v; break;
                case "message:": b.Message = v; break;
                case "lignes:": b.Lignes = []; suite = "lignes"; break;
                case "fiche:": b.Fiche = v; suite = "fiche"; break;
                case "controle:":
                    var i = v.LastIndexOf(" => ", StringComparison.Ordinal);
                    b.Controles.Add((v[..i].Trim(), v[(i + 4)..].Trim()));
                    break;
                case "agent:": b.Agent = v; break;
                case "texte:": b.Texte = v; break;
                case "critere:": var (c, y) = Paire(v); b.Criteres[c] = y; break;
            }
        }
        Clore();
        return blocs;
    }
}

/// <summary>Un serveur factice : la procedure <c>vide</c> est branchee et sans ligne, toute autre n'a pas de lecteur.</summary>
/// <summary>Un serveur factice qui rend UNE ligne sur 1194 (liste coupee au plafond) pour toute procedure ; avec
/// <paramref name="parGroupe"/>, une ligne au repertoire general et une aux IP, et le total de chaque groupe.</summary>
sealed class RepondeurPlafond(Dictionary<string, int>? parGroupe = null) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken jeton)
    {
        var code = requete.RequestUri!.AbsolutePath.Split('/')[^2];
        var corps = parGroupe is null
            ? new LignesEcran(code, true, [new LigneLue("2026J00001", "", "Registre Général", "Validee", []) { Id = "G-1" }], 1194, 1)
            : new LignesEcran(code, true, [new LigneLue("2026J00001", "", "Répertoire général", "Validee", []) { Id = "G-1" },
                                           new LigneLue("2026IP0007", "", "Injonctions de payer", "Validee", []) { Id = "I-7" }],
                              parGroupe.Values.Sum(), 1) { TotalParGroupe = parGroupe };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(corps, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
    }
}

sealed class RepondeurVide(string vide) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken jeton)
    {
        var code = requete.RequestUri!.AbsolutePath.Split('/')[^2];
        var corps = new LignesEcran(code, code == vide, [], 0, 200);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(corps, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
    }
}
