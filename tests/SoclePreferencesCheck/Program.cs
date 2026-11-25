using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Serveur;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Preferences;
using RigV3Desktop;
using RigV3Desktop.Pont;

// Banc de SOC-013 (retrouver ses preferences et ses documents sur n'importe quel serveur RDS) et de SOC-027 (retrouver
// ses gestes grace a la fiche « ancienne touche, nouvelle touche ») : lot 4 du plan « Reconstruction de RIG — phase 2 »,
// 2026-11-21. Un controle par critere, qui cite son id.
//
// SOC-013 : le vrai serveur en memoire, sur la base commune (catalogue) et la base V3 d'un greffe, toutes deux jetables.
// Deux « postes » : deux fichiers accueil.json (RIGV3_ETAT) et deux serveurs RDS (« RDS-A », « RDS-B »), avec le client de
// l'app (ClientServeur) et son depot (Pont/PreferencesServeur.cs). La reprise lit une copie d'essai de la V2 relue sur
// RIG_DEV (preferences_v2_rig_dev.sql).
// SOC-027 : la fiche (Pont/EcranCorrespondances.cs) rejouee contre le code de la V2 (D:\RigApplication\Source, lecture
// seule) et contre les ecrans de la V3 (menu, aiguillage, .tsx).
//
// Il lui faut le SQL Server de developpement : « docker compose up -d » a la racine du depot.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
// Ce qu'aucun banc de ce poste ne peut faire : dit, pas compte vert (docs/reconstruction/socle-preferences.md).
void Reste(string quoi) => Console.WriteLine("RESTE : " + quoi);

var dossierEtat = Directory.CreateTempSubdirectory("rigv3-soc013-");
string Poste(string nom) => Path.Combine(dossierEtat.FullName, nom, "accueil.json");
void SurLePoste(string nom) => Environment.SetEnvironmentVariable("RIGV3_ETAT", Poste(nom));

try
{
    // =================================================================================================================
    // SOC-013 — le serveur, la base du greffe, trois agents
    // =================================================================================================================
    await using var b = await BaseJetable.CreerAsync("SoclePreferencesCheck");
    var migrations = await Migrateur.AppliquerAsync(b.Connexion, Path.Combine(racine, "Base", "migrations"));
    Exiger(migrations.Jouees.Contains("SOC-013_preferences"),
        "SOC-013 T01 : la migration SOC-013_preferences est jouee sur une base neuve, avec les autres du depot");
    await using var commune = await BaseJetable.CreerAsync("SoclePrefCommune");
    await Migrateur.AppliquerAsync(commune.Connexion, Path.Combine(racine, "Base", "migrations"));

    const string Greffe = "9301";
    const string Agent1 = "banc.pref1", Agent2 = "banc.pref2", Agent3 = "banc.pref3";
    async Task Executer(string connexion, string sql, params (string Nom, object Valeur)[] p)
    {
        await using var cn = new SqlConnection(connexion);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var (nom, valeur) in p) cmd.Parameters.AddWithValue(nom, valeur);
        await cmd.ExecuteNonQueryAsync();
    }
    async Task<T> Valeur<T>(string sql, params (string Nom, object Valeur)[] p)
    {
        await using var cn = new SqlConnection(b.Connexion);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var (nom, valeur) in p) cmd.Parameters.AddWithValue(nom, valeur);
        var v = await cmd.ExecuteScalarAsync();
        return v is null or DBNull ? default! : (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }
    // Les agents du greffe : admis (role, service, actifs). Les deux premiers viennent de la V2 (IdV2 = UTLST_ID_UTLST).
    await Executer(b.Connexion, """
        INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES (@greffe, 'PREF', N'Preferences');
        INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANC', 'Banc SoclePreferences');
        INSERT INTO soc.Compte (IdV2, CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService)
        SELECT v.IdV2, v.Compte, 'BANC', 'Preferences', LEFT(v.Compte, 16), 'M.', @greffe, s.Id
        FROM (VALUES (5, @a1), (209, @a2), (NULL, @a3)) v (IdV2, Compte) CROSS JOIN soc.Service s;
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte;
        """, ("@greffe", Greffe), ("@a1", Agent1), ("@a2", Agent2), ("@a3", Agent3));
    var serveurBancs = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
    var authentification = new SqlConnectionStringBuilder(serveurBancs.ConnectionString);
    authentification.Remove("Data Source");
    await Executer(commune.Connexion,
        "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe) " +
        "VALUES (@c, N'Greffe du banc des preferences', @s, 'RIG_BANC_PREF', @v3, 0, 0, 'Romance Standard Time', 'banc', SYSDATETIME());",
        ("@c", Greffe), ("@s", serveurBancs.DataSource), ("@v3", b.Nom));

    await using var app = ServeurRig.Construire(["--environment", "Development"], s =>
    {
        s.WebHost.UseTestServer();
        s.Logging.SetMinimumLevel(LogLevel.Warning);
        s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
        s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
    });
    await app.StartAsync();
    // Le client de l'app, au nom d'un agent (l'en-tete de developpement que la production ignore, SOC-001 CA07).
    ClientServeur Client(string compte)
    {
        Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, compte);
        try { return new ClientServeur(new Uri("http://localhost/"), app.GetTestServer().CreateHandler()); }
        finally { Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null); }
    }
    // fix-ok: relu avant le premier lancement -- OuvrirDepot etait au niveau du fichier, hors de portee de Client et de
    // l'app (declares dans ce try) ; l'agent 2 s'ouvrait sur le fichier du poste RDS-C et y aurait envoye ses reglages.
    // Ouvre la V3 d'un agent sur un serveur RDS (le meme geste que l'app) ; rend l'ouverture et son depot.
    async Task<(OuverturePreferences Ouverture, PreferencesServeur Depot)> OuvrirDepot(string compte, string serveur, string session)
    {
        var o = await PreferencesServeur.OuvrirAsync(Client(compte), Greffe, serveur, session);
        return (o, PreferencesServeur.Actif!);
    }
    var routePreferences = RoutesPreferences.Pour(RoutesPreferences.Preferences, Greffe);
    async Task<int> IdCompte(string compte) => await Valeur<int>("SELECT Id FROM soc.Compte WHERE CompteWindows = @c;", ("@c", compte));
    async Task<string?> PreferenceEnBase(string compte, string nom)
        => await Valeur<string?>("SELECT Valeur FROM soc.Preference WHERE IdCompte = @id AND Nom = @n;", ("@id", await IdCompte(compte)), ("@n", nom));

    // -----------------------------------------------------------------------------------------------------------------
    // CA01 — un agent regle son accueil sur un serveur RDS et le retrouve sur un autre
    // -----------------------------------------------------------------------------------------------------------------
    Console.WriteLine();
    Console.WriteLine("--- SOC-013 CA01 et CA05 : reglages faits sur RDS-A, retrouves sur RDS-B");
    SurLePoste("rds-a");
    var ouvertureA = await PreferencesServeur.OuvrirAsync(Client(Agent1), Greffe, "RDS-A", "1:poste-a");
    var depotA = PreferencesServeur.Actif!;
    Exiger(ouvertureA.Panne is null && ouvertureA.Reprises == 0 && AccueilEtat.Distant == depotA,
        $"SOC-013 CA01 : premiere ouverture branchee sur RDS-A : rien a reprendre, le depot se branche ({ouvertureA.Panne ?? "sans panne"})");
    // Les reglages de CA01 : theme, onglet de demarrage, tri par defaut, imprimante (CA05) -- par les memes gestes que l'app.
    var etatA = AccueilEtat.Charger();
    etatA.Theme = "sombre";
    etatA.ReglagesAlertes.Tri = "titre";
    etatA.Enregistrer();
    AccueilEtat.MemoriserOnglets(["PROC_DEMANDE", "PROC_REQUI"], "PROC_REQUI", []);
    AccueilEtat.MemoriserTriClasseur(AccueilEtat.TriClasseurParDossier);
    AccueilEtat.MemoriserImprimante("Imprimante du 2e etage");
    await depotA.ViderAsync();
    Exiger(depotA.DernierEchec is null, $"SOC-013 CA01 : chaque reglage change sur RDS-A part au serveur ({depotA.DernierEchec ?? "sans echec"})");
    Exiger(await PreferenceEnBase(Agent1, NomsPreferences.Theme) == "\"sombre\""
           && await Valeur<string>("SELECT TOP 1 ModifieSur FROM soc.Preference WHERE IdCompte = @id AND Nom = 'Theme';", ("@id", await IdCompte(Agent1))) == "RDS-A",
        "SOC-013 CA01 : le theme est dans la base du greffe (soc.Preference), ecrit depuis RDS-A");

    SurLePoste("rds-b");
    Exiger(AccueilEtat.Charger().Theme == ThemesRig.Defaut, "SOC-013 CA01 : le poste RDS-B part d'un accueil.json vide (theme clair par defaut)");
    var ouvertureB = await PreferencesServeur.OuvrirAsync(Client(Agent1), Greffe, "RDS-B", "2:poste-b");
    var depotB = PreferencesServeur.Actif!;
    var etatB = AccueilEtat.Charger();
    Exiger(ouvertureB.Panne is null && ouvertureB.Reprises > 0, $"SOC-013 CA01 : a l'ouverture sur RDS-B, les preferences sont relues ({ouvertureB.Reprises} reprises)");
    Exiger(etatB.Theme == "sombre", $"SOC-013 CA01 : RDS-B retrouve le theme (lu : {etatB.Theme})");
    Exiger(etatB.OngletActif == "PROC_REQUI" && etatB.OngletsVue.SequenceEqual(["PROC_DEMANDE", "PROC_REQUI"]),
        $"SOC-013 CA01 : RDS-B retrouve ses onglets et son onglet de demarrage (lu : {string.Join(",", etatB.OngletsVue)} / {etatB.OngletActif})");
    Exiger(etatB.TriClasseur == AccueilEtat.TriClasseurParDossier && etatB.ReglagesAlertes.Tri == "titre",
        $"SOC-013 CA01 : RDS-B retrouve ses tris par defaut (classeur {etatB.TriClasseur}, alertes {etatB.ReglagesAlertes.Tri})");
    Exiger(etatB.Imprimante == "Imprimante du 2e etage", $"SOC-013 CA05 : RDS-B retrouve l'imprimante par defaut (lu : « {etatB.Imprimante} »)");

    // Dans l'autre sens : RDS-B change le theme, RDS-A le retrouve a sa prochaine ouverture.
    var b2 = AccueilEtat.Charger();
    b2.Theme = "argile";
    b2.Enregistrer();
    await depotB.ViderAsync();
    SurLePoste("rds-a");
    await PreferencesServeur.OuvrirAsync(Client(Agent1), Greffe, "RDS-A", "1:poste-a-2");
    Exiger(AccueilEtat.Charger().Theme == "argile", "SOC-013 CA01 : un reglage fait sur RDS-B est retrouve a la reouverture sur RDS-A");

    // Contre-epreuve du controle cle : sans le depot branche, le reglage reste sur son poste.
    PreferencesServeur.Fermer();
    var seul = AccueilEtat.Charger();
    seul.Theme = "violet";
    seul.Enregistrer();
    SurLePoste("rds-c");
    await PreferencesServeur.OuvrirAsync(Client(Agent1), Greffe, "RDS-C", "3:poste-c");
    Exiger(AccueilEtat.Charger().Theme == "argile",
        "SOC-013 CA01 contre-epreuve : un reglage fait sans le depot branche n'arrive pas sur un autre serveur (le controle voit l'absence)");
    PreferencesServeur.Fermer();

    // Sans serveur, rien ne change : le fichier seul, aucun depot.
    SurLePoste("sans-serveur");
    var demo = AccueilEtat.Charger();
    demo.Theme = "orange";
    demo.Enregistrer();
    Exiger(AccueilEtat.Distant is null && AccueilEtat.Charger().Theme == "orange" && File.Exists(Poste("sans-serveur")),
        "SOC-013 : sans serveur, les reglages restent dans accueil.json, comme avant (aucun depot branche)");

    // Chaque agent ne lit que les siennes.
    var deLAutre = await Client(Agent2).LireAsync<PreferencesAgent>(routePreferences);
    Exiger(!deLAutre.Valeurs.ContainsKey(NomsPreferences.Theme),
        "SOC-013 CA01 : un autre agent ne lit pas les preferences de l'agent (aucune adresse ne nomme un autre agent)");
    using (var anonyme = app.GetTestClient())
    using (var r = await anonyme.GetAsync(routePreferences))
        Exiger(r.StatusCode == HttpStatusCode.Unauthorized, $"SOC-013 : sans session Windows, les preferences ne se lisent pas ({(int)r.StatusCode})");
    // Refus et jumeau accepte, meme borne : un nom de 61 caracteres est refuse, 60 passe.
    var nom60 = new string('n', ServicePreferences.LongueurNom);
    var nom61 = nom60 + "x";
    var c2 = Client(Agent2);
    var refus = await Statut(() => c2.ModifierAsync<PreferencesAgent, PreferencesAgent>(routePreferences, new(new Dictionary<string, string> { [nom61] = "1" }, "RDS-A")));
    var accepte = await Statut(() => c2.ModifierAsync<PreferencesAgent, PreferencesAgent>(routePreferences, new(new Dictionary<string, string> { [nom60] = "1" }, "RDS-A")));
    Exiger(refus == 400 && accepte == 200, $"SOC-013 : un nom de preference de 61 caracteres est refuse (400), son jumeau de 60 est ecrit ({refus}/{accepte})");
    // Une valeur illisible venue du serveur ne casse pas l'accueil : la valeur du poste reste.
    SurLePoste("illisible");
    var avant = AccueilEtat.Charger();
    avant.Theme = "sombre";
    avant.Enregistrer();
    var reprises = AccueilEtat.ReprendrePreferences(new Dictionary<string, string>
    {
        ["Tuiles"] = "123", ["Theme"] = "{", ["TriClasseur"] = "\"dossier\"", ["NomInconnu"] = "\"x\"",
    });
    var apres = AccueilEtat.Charger();
    Exiger(reprises == 1 && apres.Theme == "sombre" && apres.TriClasseur == "dossier" && apres.Tuiles.Count == avant.Tuiles.Count,
        $"SOC-013 CA01 : une valeur illisible ou d'une autre forme laisse celle du poste ; seule la valeur lisible est reprise ({reprises})");

    // Le branchement de l'app : relu AVANT l'accueil, et l'imprimante suivie par la boite d'impression (CA05).
    var fenetre = File.ReadAllText(Path.Combine(racine, "MainWindow.xaml.cs"));
    var avantBrancher = fenetre.IndexOf("PreferencesServeur.BrancherSessionAsync", StringComparison.Ordinal);
    Exiger(avantBrancher > 0 && avantBrancher < fenetre.IndexOf("PageWeb.Brancher(Vue", StringComparison.Ordinal)
           && Regex.Matches(fenetre, @"PreferencesServeur\.BrancherSessionAsync").Count == 2 && fenetre.Contains("PreferencesServeur.FermerAsync()"),
        "SOC-013 T03 : l'app relit les preferences avant de monter l'accueil (greffe du lancement et ecran de choix), et les envoie a la fermeture");
    // Les documents recus : l'app s'abonne avant la session, et chaque document ouvre une fenetre de document au rang
    // libre de TOUTES les fenetres de document (sinon une affaire detachee et un document recu liraient le meme emplacement).
    var abonnement = fenetre.IndexOf("DocumentsRecus.Brancher(new AfficheurFenetres(), AccueilEtat.DossierPiecesJointes)", StringComparison.Ordinal);
    var fenetreDocument = File.ReadAllText(Path.Combine(racine, "FenetreDocument.xaml.cs"));
    var plan = File.ReadAllText(Path.Combine(racine, "Pont", "PlanTravail.cs"));
    Exiger(abonnement > 0 && abonnement < avantBrancher
           && Regex.IsMatch(fenetreDocument, @"class AfficheurFenetres : Pont\.IAfficheurDocuments[\s\S]{0,400}?Dispatcher\.BeginInvoke\(\(\) => FenetreDocument\.Ouvrir\(piece, valeurs\)\)")
           && Regex.IsMatch(fenetreDocument, @"App\.Partage\.RangLibre\(RangsOuverts\(\)\);\s*App\.Partage\.Publier\(rang, piece, valeurs\);[\s\S]{0,200}?new FenetreDocument\(rang\)[\s\S]{0,200}?\.Show\(\)")
           && plan.Contains("Partage!.RangLibre(FenetreDocument.RangsOuverts())"),
        "SOC-013 T03 : l'app ouvre chaque document recu dans une fenetre de document (abonnee avant la session, au rang libre de toutes les fenetres de document)");
    var planVue = File.ReadAllText(Path.Combine(racine, "Pont", "PlanTravailVue.cs"));
    Exiger(plan.Contains("if (Etat.Imprimante is { Length: > 0 } imprimante) ImprImprimante = imprimante;")
           && Regex.IsMatch(planVue, @"case ""imprimante"":[\s\S]{0,200}?AccueilEtat\.MemoriserImprimante\(ImprImprimante\)"),
        "SOC-013 CA05 : la boite d'impression s'ouvre sur l'imprimante de l'agent, et la derniere choisie devient la sienne");

    // -----------------------------------------------------------------------------------------------------------------
    // CA03 — un document prepare pour un agent s'ouvre dans SA session, nulle part ailleurs
    // -----------------------------------------------------------------------------------------------------------------
    Console.WriteLine();
    Console.WriteLine("--- SOC-013 CA03 et CA04 : les documents de la partie serveur, ouverts a l'ecran");
    // Les editions de la partie serveur : de vrais fichiers sur « le serveur » (un dossier du banc). Un PDF d'une page.
    var dossierEditions = Directory.CreateDirectory(Path.Combine(dossierEtat.FullName, "serveur-editions")).FullName;
    byte[] Pdf(string titre) => System.Text.Encoding.Latin1.GetBytes($"%PDF-1.4\n1 0 obj << /Type /Page >> endobj\n% {titre}\n%%EOF\n");
    async Task<int> Deposer(string compte, string titre, string extension = ".pdf", byte[]? contenu = null, bool fichierPresent = true)
    {
        var fichier = Path.Combine(dossierEditions, titre + extension);
        if (fichierPresent) await File.WriteAllBytesAsync(fichier, contenu ?? Pdf(titre));
        await using var cn = new SqlConnection(b.Connexion);
        await cn.OpenAsync();
        return await DocumentsAgents.DeposerAsync(cn, compte, NaturesDocument.Afficher, titre, fichier, "banc-editions", default);
    }
    // L'ecran : chaque document recu s'ouvre dans une fenetre de document (l'app : FenetreDocument ; ici, le releveur).
    var dossierPieces = Path.Combine(dossierEtat.FullName, "pieces-du-poste");
    var fenetres = new AfficheurDuBanc();
    DocumentsRecus.Brancher(fenetres, dossierPieces);
    // Le fichier du poste derriere une piece (son adresse https://pieces.rigv3.example/<sous-dossier>/<fichier>).
    var hotePieces = $"https://{PiecesJointes.Hote}/";
    string? FichierDuPoste(NoeudGed piece)
        => piece.Fichier.StartsWith(hotePieces, StringComparison.Ordinal)
            ? Path.Combine(dossierPieces, Uri.UnescapeDataString(piece.Fichier[hotePieces.Length..]).Replace('/', Path.DirectorySeparatorChar))
            : null;
    NoeudGed? Montree(string titre) => fenetres.Montres.Select(m => m.Piece).LastOrDefault(p => p.Libelle == titre);
    // L'agent 1 a ouvert sur RDS-A puis RDS-B puis RDS-A puis RDS-C : sa session est la derniere (RDS-C).
    SurLePoste("rds-c");
    var depotC = (await OuvrirDepot(Agent1, "RDS-C", "3:poste-c-2")).Depot;
    var idDoc = await Deposer(Agent1, "Kbis 2026B00042");
    var aAncienne = await depotA.ReleverAsync();
    SurLePoste("agent2");
    var aLAutre = await (await OuvrirDepot(Agent2, "RDS-A", "4:autre-agent")).Depot.ReleverAsync();
    var aLaSienne = await depotC.ReleverAsync();
    var deNouveau = await depotC.ReleverAsync();
    Exiger(aLaSienne.Length == 1 && aLaSienne[0].Id == idDoc && aLaSienne[0].Titre == "Kbis 2026B00042",
        $"SOC-013 CA03 : le document s'ouvre dans la session ou l'agent travaille (RDS-C, {aLaSienne.Length} recu)");
    Exiger(aAncienne.Length == 0, $"SOC-013 CA03 : rien n'arrive dans une ancienne session du meme agent (RDS-A, {aAncienne.Length} recu)");
    Exiger(aLAutre.Length == 0, $"SOC-013 CA03 : rien n'arrive chez un autre agent ({aLAutre.Length} recu)");
    Exiger(deNouveau.Length == 0 && depotC.Recus.Count == 1, "SOC-013 CA03 : un document n'est remis qu'une fois");
    await DocumentsRecus.AttendreAsync();
    var montre = fenetres.Montres.Count == 1 ? fenetres.Montres[0] : default;
    Exiger(montre.Piece is { APdf: true, Libelle: "Kbis 2026B00042", Extension: "PDF" } piece
           && FichierDuPoste(piece) is { } surLePoste && File.Exists(surLePoste)
           && File.ReadAllBytes(surLePoste).SequenceEqual(File.ReadAllBytes(Path.Combine(dossierEditions, "Kbis 2026B00042.pdf")))
           && montre.Valeurs.Any(v => v == ("Préparé par", "banc-editions")),
        "SOC-013 CA03 T03 : le document recu s'ouvre dans une fenetre de document de la session ou l'agent travaille, et nulle part ailleurs : "
        + $"le PDF du serveur, range sur le poste ({fenetres.Montres.Count} fenetre(s) : {string.Join(", ", fenetres.Montres.Select(m => m.Piece.Libelle))})");
    // Le contenu : le document de l'agent seulement (refus, et son jumeau accepte : le meme document, pour lui).
    var routeKbis = RoutesPreferences.PourDocument(Greffe, idDoc);
    var parUnAutre = await Statut(() => Client(Agent2).LireAsync<ContenuDocument>(routeKbis));
    var parLui = await Client(Agent1).LireAsync<ContenuDocument>(routeKbis);
    Exiger(parUnAutre == 404 && parLui.Nom == "Kbis 2026B00042.pdf" && parLui.Contenu.Length > 0,
        $"SOC-013 CA03 : le contenu d'un document ne se lit que par son agent (un autre : {parUnAutre} ; lui : {parLui.Contenu.Length} octets)");
    // Un fichier disparu du serveur, ou d'un type que la V3 n'affiche pas : la fenetre s'ouvre quand meme et dit pourquoi
    // (jumeau : le PDF present, ouvert ci-dessus avec son contenu).
    var idDisparu = await Deposer(Agent1, "Etat disparu", fichierPresent: false);
    await Deposer(Agent1, "Courrier a relire", ".docx", [0x50, 0x4B, 0x03, 0x04, 0x77]);
    var disparuSurServeur = await Statut(() => Client(Agent1).LireAsync<ContenuDocument>(RoutesPreferences.PourDocument(Greffe, idDisparu)));
    await depotC.ReleverAsync();
    await DocumentsRecus.AttendreAsync();
    Exiger(disparuSurServeur == 404 && Montree("Etat disparu") is { APdf: false } disparu && disparu.Apercu.Contains("n'a pas pu être lu sur le serveur")
           && Montree("Courrier a relire") is { APdf: false } word && word.Apercu.Contains("la V3 n'affiche que ceux-là"),
        $"SOC-013 CA03 : un document sans fichier sur le serveur (404) ou d'un type que la V3 n'affiche pas s'ouvre quand meme, avec la raison ({fenetres.Montres.Count} fenetres)");
    // Contre-epreuve du controle cle (l'essai refuse : DocumentsArrives sans abonne) : le document est recu, rien ne s'ouvre.
    DocumentsRecus.Debrancher();
    var avantSansAbonne = fenetres.Montres.Count;
    await Deposer(Agent1, "Recu sans abonne");
    var recuSansAbonne = await depotC.ReleverAsync();
    await DocumentsRecus.AttendreAsync();
    Exiger(recuSansAbonne.Length == 1 && fenetres.Montres.Count == avantSansAbonne,
        "SOC-013 CA03 contre-epreuve : sans abonne a DocumentsArrives, un document recu ne s'ouvre pas (le controle voit l'absence)");
    DocumentsRecus.Brancher(fenetres, dossierPieces);
    Exiger(await Valeur<string>("SELECT CONCAT(RemisServeur, '|', RemisSession) FROM soc.DocumentAgent WHERE Id = @id;", ("@id", idDoc)) == "RDS-C|3:poste-c-2",
        "SOC-013 CA03 : la base dit ou et dans quelle session le document a ete remis");
    var sansSession = await Statut(() => Client(Agent1).LireAsync<DocumentRecu[]>(RoutesPreferences.Pour(RoutesPreferences.Documents, Greffe)));
    Exiger(sansSession == 400, $"SOC-013 CA03 : une releve qui ne dit pas sa session est refusee ({sansSession})");

    // -----------------------------------------------------------------------------------------------------------------
    // CA04 — un agent sans V3 ouverte : le document l'attend, et s'affiche a sa prochaine ouverture
    // -----------------------------------------------------------------------------------------------------------------
    var idAttente = await Deposer(Agent3, "Courrier en attente");
    Exiger(await Valeur<int>("SELECT COUNT(*) FROM soc.DocumentAgent WHERE Id = @id AND RemisLe IS NULL;", ("@id", idAttente)) == 1
           && await Valeur<int>("SELECT COUNT(*) FROM soc.SessionAgent WHERE IdCompte = @id;", ("@id", await IdCompte(Agent3))) == 0,
        "SOC-013 CA04 : pour un agent sans V3 ouverte, le document attend dans la base (non remis)");
    SurLePoste("agent3");
    var (ouverture3, _) = await OuvrirDepot(Agent3, "RDS-B", "5:agent3");
    Exiger(ouverture3.Documents.Length == 1 && ouverture3.Documents[0].Id == idAttente,
        $"SOC-013 CA04 : a sa prochaine ouverture, le document qui l'attendait lui est remis ({ouverture3.Documents.Length} recu)");
    await DocumentsRecus.AttendreAsync();
    Exiger(Montree("Courrier en attente") is { APdf: true } attendu && FichierDuPoste(attendu) is { } attenduSurPoste && File.Exists(attenduSurPoste),
        "SOC-013 CA04 T03 : a l'ouverture de l'application, le document qui l'attendait s'ouvre dans une fenetre de document");
    DocumentsRecus.Debrancher();
    var inconnu = false;
    try { await Deposer("banc.inconnu", "Perdu"); }
    catch (InvalidOperationException) { inconnu = true; }
    Exiger(inconnu, "SOC-013 CA04 : un document pour un compte inconnu du greffe est refuse, pas garde pour personne");
    PreferencesServeur.Fermer();

    // -----------------------------------------------------------------------------------------------------------------
    // CA02 — la reprise des preferences de la V2
    // -----------------------------------------------------------------------------------------------------------------
    Console.WriteLine();
    Console.WriteLine("--- SOC-013 CA02 : reprise des preferences de la V2 (copie d'essai relue sur RIG_DEV)");
    await using var v2 = await BaseJetable.CreerAsync("SoclePrefV2");
    await Executer(v2.Connexion, File.ReadAllText(Path.Combine(racine, "tests", "SoclePreferencesCheck", "preferences_v2_rig_dev.sql")));
    // Les ecrans de la V3 : ceux du menu, procedures reunies comprises (un favori « Mentions en marge » ouvre
    // l'ecran qui la reunit, Pont/MenuAccueil.cs), et ceux du catalogue.
    var ecransV3 = MenuAccueil.Toutes.Select(e => e.Code)
        .Concat(CatalogueEcrans.Familles.SelectMany(f => f.Ecrans).Select(e => e.Code)).ToHashSet();
    BilanReprisePreferences bilan, bilan2;
    const string EtatPreferences = "SELECT CONCAT(COUNT(*), '/', CHECKSUM_AGG(CHECKSUM(IdCompte, Nom, Valeur))) FROM soc.Preference;";
    string apresUne, apresDeux;
    {
        await using var cnV2 = new SqlConnection(v2.Connexion);
        await using var cnV3 = new SqlConnection(b.Connexion);
        await cnV2.OpenAsync();
        await cnV3.OpenAsync();
        bilan = await ReprisePreferences.ReprendreAsync(cnV2, cnV3, ecransV3);
        apresUne = await Valeur<string>(EtatPreferences);
        bilan2 = await ReprisePreferences.ReprendreAsync(cnV2, cnV3, ecransV3);
        apresDeux = await Valeur<string>(EtatPreferences);
    }
    var fav209 = JsonSerializer.Deserialize<string[]>(await PreferenceEnBase(Agent2, NomsPreferences.VuesFavorites) ?? "[]")!;
    Exiger(bilan.AgentsRepris == 2 && bilan.PreferencesReprises > 0,
        $"SOC-013 CA02 : les preferences des agents de la V2 qui ont un compte V3 sont reprises ({bilan.AgentsRepris} agents, {bilan.PreferencesReprises} preferences)");
    Exiger(await PreferenceEnBase(Agent2, NomsPreferences.Theme) == "\"clair\"", "SOC-013 CA02 : le theme MetroBlue de la V2 devient le theme clair");
    Exiger(fav209.Length >= 4 && fav209[0] == "PROC_ARRIVEE" && fav209.All(ecransV3.Contains)
           && Array.IndexOf(fav209, "PROC_VDOSSIER") > 0 && Array.IndexOf(fav209, "PROC_FACTURE") > Array.IndexOf(fav209, "PROC_VDOSSIER"),
        $"SOC-013 CA02 : les favoris de la V2 deviennent ceux de la V3, dans leur ordre, par la DLL du processus (VGED → dossier, FACT → factures) : {string.Join(", ", fav209)}");
    Exiger(await PreferenceEnBase(Agent1, NomsPreferences.Imprimante) == "\"Production\""
           && await PreferenceEnBase(Agent1, NomsPreferences.OngletActif) == "\"\"",
        "SOC-013 CA02 et CA05 : l'imprimante par defaut de la V2 est reprise ; un ecran de demarrage vide reste l'accueil");
    PreferenceSansEquivalent? Liste(string nom) => bilan.SansEquivalent.FirstOrDefault(s => s.NomV2 == nom);
    Exiger(Liste("AmiConstantes.Rig.FontSize") is { Agents: 2 } && Liste("ConsoleAccueil_Theme") is { Agents: 1 } theme && theme.Raison.Contains("MetroGreen")
           && Liste("ConsoleAccueil_Fav_01") is { } sep && sep.Raison.Contains("separateur"),
        $"SOC-013 CA02 : ce qui n'a pas d'equivalent est liste au referent (nom, nombre d'agents, raison) : {bilan.SansEquivalent.Count} noms");
    Exiger(bilan.AgentsSansCompteV3.SequenceEqual([9999]),
        $"SOC-013 CA02 : un agent de la V2 sans compte V3 est liste, sans bloquer la reprise ({string.Join(",", bilan.AgentsSansCompteV3)})");
    Exiger(bilan2.PreferencesReprises == bilan.PreferencesReprises && apresDeux == apresUne,
        $"SOC-013 CA02 : reprendre deux fois donne les memes preferences, sans doublon (soc.Preference : {apresUne} puis {apresDeux})");
    Exiger(ReprisePreferences.ThemesV2.Values.All(t => ThemesRig.Tous.Any(x => x.Code == t)),
        "SOC-013 CA02 : chaque theme de la V2 repris vise un theme qui existe dans la V3 (ThemesRig)");
    Reste("SOC-013 CA02 : la reprise des 40 bases de greffe se joue au passage (lot de la recopie), pas depuis ce poste : aucune ecriture sur SQL-PROD.");
    Reste("SOC-013 T05 : deux serveurs RDS de recette reels (ferme RDS) : ici deux postes simules, meme serveur de la V3.");

    // =================================================================================================================
    // SOC-027 — la fiche « ancienne touche, nouvelle touche »
    // =================================================================================================================
    Console.WriteLine();
    Console.WriteLine("--- SOC-027 : la fiche « ancienne touche, nouvelle touche »");
    var fiche = EcranCorrespondances.Fiche;
    var inventaire = InventaireTouchesV2.Lignes;

    // CA01 — chaque raccourci de la V2 est dans la fiche avec son ecran ; l'inventaire suit le code de la V2.
    var sourceV2 = @"D:\RigApplication\Source";
    if (Directory.Exists(sourceV2))
    {
        var duCode = LireToucheDeLaV2(sourceV2);
        var dansInventaire = inventaire.Select(t => (t.Procedure, t.Touche)).ToHashSet();
        var manquants = Manquants(duCode, dansInventaire);
        var perimes = dansInventaire.Where(p => !duCode.Contains(p)).ToList();
        Exiger(duCode.Count > 100 && manquants.Count == 0,
            $"SOC-027 CA01 : chaque raccourci du code de la V2 ({duCode.Count} couples ecran-touche, {duCode.Select(p => p.Procedure).Distinct().Count()} ecrans) est dans l'inventaire"
            + (manquants.Count == 0 ? "" : $" ; manquent : {string.Join(", ", manquants.Take(8))} -> relancer inventorier_touches_v2.py"));
        Exiger(perimes.Count == 0, $"SOC-027 CA01 : l'inventaire n'a pas de touche que la V2 n'a plus ({string.Join(", ", perimes.Take(8))})");
        // Contre-epreuve rouge du controle cle : un couple retire de l'inventaire est vu manquant.
        var unRetire = dansInventaire.First();
        Exiger(Manquants(duCode, dansInventaire.Where(p => p != unRetire).ToHashSet()).SequenceEqual([unRetire]),
            $"SOC-027 CA01 contre-epreuve : une touche de la V2 retiree de l'inventaire est vue manquante ({unRetire})");
    }
    else Reste("SOC-027 CA01 : le code de la V2 n'est pas sur ce poste : l'inventaire n'a pas ete recompare au code.");
    var manquantesFiche = inventaire.Where(t => !fiche.Any(l => l.EcranV2 == t.EcranV2 && l.AncienneTouche == EcranCorrespondances.Lisible(t.Touche))).ToList();
    Exiger(manquantesFiche.Count == 0 && fiche.Any(l => l.AncienneTouche == "Alt + lettre soulignée"),
        $"SOC-027 CA01 : chaque raccourci de l'inventaire ({inventaire.Length}) figure dans la fiche avec son ecran de la V2, et les lettres soulignees aussi");

    // CA02 — chaque ligne : la nouvelle touche, ou le geste, ou la suppression et sa raison.
    var malDites = fiche.Where(l => !StatutsCorrespondance.Tous.Contains(l.Statut)
        || string.IsNullOrWhiteSpace(l.Explication)
        || (l.Statut is StatutsCorrespondance.Identique or StatutsCorrespondance.NouvelleTouche or StatutsCorrespondance.Nouveau) != (l.NouvelleTouche is not null)
        || (l.Statut == StatutsCorrespondance.AVenir && l.Construit)).ToList();
    Exiger(malDites.Count == 0,
        $"SOC-027 CA02 : chaque ligne donne la nouvelle touche, ou le geste qui la remplace, ou pourquoi elle est supprimee ; « a venir » seulement pour un ecran pas encore construit"
        + (malDites.Count == 0 ? "" : $" ; en defaut : {string.Join(" | ", malDites.Take(5).Select(l => $"{l.Numero} {l.EcranV2} {l.AncienneTouche} {l.Statut}"))}"));
    Exiger(fiche.Select(l => l.Numero).SequenceEqual(Enumerable.Range(1, fiche.Length)), "SOC-027 CA02 : les lignes sont numerotees de 1 a N, sans trou");
    var parStatut = fiche.GroupBy(l => l.Statut).ToDictionary(g => g.Key, g => g.Count());
    Console.WriteLine("INFO : " + string.Join(", ", parStatut.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}")));
    if (parStatut.GetValueOrDefault(StatutsCorrespondance.AVenir) is > 0 and var aVenir)
        Reste($"SOC-027 CA02 : {aVenir} ligne(s) visent un ecran de la V3 pas encore construit (ou pas encore au menu) : leur touche se fixera avec l'ecran.");

    // CA03 — sur un ecran de la V3, « ? » montre ses touches avec l'ancienne a cote.
    var deRequi = EcranCorrespondances.DeLEcran("PROC_REQUI");
    Exiger(deRequi.Count(l => l.EcranV3 == "PROC_REQUI") >= 5 && deRequi.All(l => !string.IsNullOrWhiteSpace(l.AncienneTouche))
           && deRequi.Any(l => l.AncienneTouche == "F1" && l.NouvelleTouche == "?"),
        $"SOC-027 CA03 : l'aide d'un ecran (Requisitions) donne ses touches avec l'ancienne touche a cote, F1 → « ? » ({deRequi.Length} lignes)");
    var procedure = File.ReadAllText(Path.Combine(racine, "web", "src", "plan", "Procedure.tsx"));
    Exiger(procedure.Contains("<AideTouchesEcran code={proc.code} actif={!cachee} />"),
        "SOC-027 CA03 : chaque ecran de procedure ecoute « ? » quand il est visible (Procedure.tsx ; vitest joue la touche)");

    // CA04 — chaque ligne mene a un ecran qui existe, et la touche annoncee y fait ce que dit la fiche.
    var aiguillage = File.ReadAllText(Path.Combine(racine, "web", "src", "plan", "Aiguillage.tsx"));
    var composants = Regex.Matches(aiguillage, @"case '(?<code>[A-Z_]+)':(?:\s*//[^\n]*)*\s*return\s+(?:ecran\.\w+\s*\?\s*)?<(?<c>\w+)")
        .ToDictionary(m => m.Groups["code"].Value, m => m.Groups["c"].Value);
    var pages = new[] { EcranCorrespondances.Accueil, EcranCorrespondances.PageDossier, EcranCorrespondances.PageRecherche };
    var sansEcran = fiche.Where(l => l.EcranV3 is { } e && !pages.Contains(e) && !ecransV3.Contains(e)).ToList();
    Exiger(sansEcran.Count == 0 && fiche.Count(l => l.EcranV3 is not null) > 100,
        $"SOC-027 CA04 : chaque geste de la fiche mene a un ecran qui existe dans la V3 ({fiche.Count(l => l.EcranV3 is not null)} lignes)"
        + (sansEcran.Count == 0 ? "" : $" ; sans ecran : {string.Join(", ", sansEcran.Take(5).Select(l => l.EcranV3))}"));
    var construitsSansCase = fiche.Where(l => l.Construit && l.EcranV3 is { } e && e is not (EcranCorrespondances.Accueil or EcranCorrespondances.PageDossier)
                                              && !composants.ContainsKey(e) && !(e == EcranCorrespondances.PageRecherche && aiguillage.Contains("case 'RECHERCHE':"))).ToList();
    Exiger(construitsSansCase.Count == 0,
        $"SOC-027 CA04 : un ecran annonce « construit » a bien son ecran dans l'aiguillage ({string.Join(", ", construitsSansCase.Select(l => l.EcranV3).Distinct())})");
    Exiger(fiche.Where(l => l.Ouvrable).All(l => ecransV3.Contains(l.EcranV3!) || l.EcranV3 == EcranCorrespondances.PageRecherche),
        "SOC-027 CA04 : le bouton d'une ligne ouvre un ecran du menu (ou la page Recherche), par le geste « ouvrirCode »");
    // Entree lance la recherche : verifie dans le .tsx de chaque ecran annonce, et l'inverse (fiche en retard). Un ecran
    // passe par toucheDeRecherche (Entree, F5, F7 : web/src/plan/clavier.ts), ou teste Entree lui-meme (un ecran en retard).
    var entreeRecherche = new Regex(@"(\.key\s*===?\s*['""]Enter['""]|toucheDeRecherche\(e\)\))[\s\S]{0,120}?rechercher\(");
    string? Tsx(string code) => composants.TryGetValue(code, out var c) && File.Exists(Path.Combine(racine, "web", "src", "ecrans", c + ".tsx"))
        ? File.ReadAllText(Path.Combine(racine, "web", "src", "ecrans", c + ".tsx")) : null;
    foreach (var code in EcranCorrespondances.EntreeLanceLaRecherche)
        Exiger(Tsx(code) is { } t && entreeRecherche.IsMatch(t), $"SOC-027 CA04 : Entree lance bien la recherche sur l'ecran annonce ({CatalogueEcrans.Nom(code)})");
    // Les ecrans du menu ou une touche de recherche de la V2 arrive (les pages -- accueil, dossier, palette -- ont leur regle).
    var ecransRecherche = inventaire.Where(t => t.Action == "rechercher").Select(t => EcranCorrespondances.EcranV3De(t.Procedure)).OfType<string>()
        .Distinct().Where(code => !pages.Contains(code) && Tsx(code) is not null).ToList();
    var enRetard = ecransRecherche.Where(code => !EcranCorrespondances.EntreeLanceLaRecherche.Contains(code) && entreeRecherche.IsMatch(Tsx(code)!)).ToList();
    Exiger(enRetard.Count == 0, $"SOC-027 CA04 : aucun ecran ne lance la recherche sur Entree sans que la fiche le dise ({string.Join(", ", enRetard)})");
    // « La liste se filtre au fil de la saisie » : des criteres, aucun bouton qui lance la recherche. Un critere est un champ
    // ecrit dans l'ecran (<input>, <select>) ou le champ « Aller a… » (Ctrl K) de la tete de liste, que l'ecran demande au
    // socle par la prop filtre de EnteteListe (web/src/composants/ListeModerne.tsx) : depuis les listes modernes
    // (25/11/2026), un critere texte qui filtrait sur place DEVIENT ce champ (Gestion clients n'a plus d'<input> a elle).
    var criteres = new Regex(@"<(input|select)\b|\bfiltre=\{\{");
    foreach (var code in EcranCorrespondances.RechercheAuFilDeLaSaisie)
        Exiger(Tsx(code) is { } t && criteres.IsMatch(t) && !t.Contains("rechercher(", StringComparison.Ordinal),
            $"SOC-027 CA04 : la liste se filtre bien au fil de la saisie sur l'ecran annonce ({CatalogueEcrans.Nom(code)})");
    // « Pas de critere de recherche » : l'ecran construit, ni dans l'une ni dans l'autre liste, n'a vraiment aucun champ.
    var sansCritere = ecransRecherche.Where(code => !EcranCorrespondances.EntreeLanceLaRecherche.Contains(code)
                                                    && !EcranCorrespondances.RechercheAuFilDeLaSaisie.Contains(code)).ToList();
    Exiger(sansCritere.All(code => !criteres.IsMatch(Tsx(code)!)),
        $"SOC-027 CA04 : un ecran annonce « sans critere de recherche » n'a vraiment aucun champ ({string.Join(", ", sansCritere.Select(CatalogueEcrans.Nom))})");
    // F5 et F7 (decision du chef du plan, 21/11/2026) : la fenetre ne recharge plus, et la recherche se lance sur ces touches
    // la ou Entree la lance -- par la fonction commune du clavier, sur chaque ecran annonce. Sur Requisitions, F5 efface
    // les criteres, comme dans la V2 (frmREQUI.cs:421).
    var pageWeb = File.ReadAllText(Path.Combine(racine, "Pont", "PageWeb.cs"));
    var clavierTs = File.ReadAllText(Path.Combine(racine, "web", "src", "plan", "clavier.ts"));
    bool NeRechargePas(string page) => Regex.IsMatch(page, @"^\s*coeur\.Settings\.AreBrowserAcceleratorKeysEnabled = false;", RegexOptions.Multiline)
                                       && !page.Contains("AreBrowserAcceleratorKeysEnabled = true", StringComparison.Ordinal);
    Exiger(NeRechargePas(pageWeb),
        "SOC-027 CA04 : F5 et F7 ne rechargent jamais la fenetre de la V3 (Pont/PageWeb.cs coupe les touches du navigateur)");
    Exiger(!NeRechargePas(pageWeb.Replace("AreBrowserAcceleratorKeysEnabled = false;", "AreBrowserAcceleratorKeysEnabled = true;")),
        "SOC-027 CA04 contre-epreuve : la fenetre qui garde les touches du navigateur (F5 recharge) est vue");
    var touchesTs = Regex.Match(clavierTs, @"TOUCHES_RECHERCHE: readonly string\[\] = \[(?<t>[^\]]*)\]").Groups["t"].Value
        .Split(',').Select(t => t.Trim().Trim('\'')).Select(t => t == "Enter" ? "Entree" : t).ToHashSet();
    Exiger(touchesTs.SetEquals(EcranCorrespondances.TouchesDeRecherche) && touchesTs.SetEquals(["Entree", "F5", "F7"]),
        $"SOC-027 CA04 : Entree, F5 et F7 lancent la recherche dans un critere (clavier.ts : {string.Join(", ", touchesTs)} ; la fiche : {string.Join(", ", EcranCorrespondances.TouchesDeRecherche)})");
    var parLaFonction = new Regex(@"toucheDeRecherche\(e\)\)\s*rechercher\(");
    foreach (var code in EcranCorrespondances.EntreeLanceLaRecherche)
        Exiger(Tsx(code) is { } t && parLaFonction.IsMatch(t),
            $"SOC-027 CA04 : F5 et F7 lancent la recherche comme Entree sur l'ecran annonce ({CatalogueEcrans.Nom(code)} passe par toucheDeRecherche)");
    var effaceF5 = new Regex(@"e\.key === 'F5'\)\s*\{\s*e\.preventDefault\(\);\s*geste\('Effacer'\);\s*return\s*\}[\s\S]{0,200}?toucheDeRecherche\(e\)");
    foreach (var code in EcranCorrespondances.F5EffaceLesCriteres)
        Exiger(Tsx(code) is { } t && effaceF5.IsMatch(t), $"SOC-027 CA04 : F5 efface les criteres sur l'ecran annonce, avant la recherche ({CatalogueEcrans.Nom(code)})");
    // Ou F5 efface les criteres, la fiche ne dit nulle part que F5 y lance la recherche.
    var f5DitLancer = fiche.Where(l => l.EcranV3 is { } e && EcranCorrespondances.F5EffaceLesCriteres.Contains(e)
                                       && Regex.IsMatch(l.Explication, @"F5[^;.]*la lancent|F5 ou F7")).ToList();
    Exiger(f5DitLancer.Count == 0 && fiche.Any(l => l.EcranV3 == "PROC_REQUI" && l.AncienneTouche == "Entrée" && l.Explication.Contains("F5 y efface les critères")),
        $"SOC-027 CA02 : sur Requisitions, la fiche dit que F5 efface les criteres, jamais qu'il lance la recherche ({string.Join(" | ", f5DitLancer.Select(l => $"{l.Numero} {l.AncienneTouche}"))})");
    var rechargeDite = fiche.Where(l => l.Explication.Contains("recharge", StringComparison.OrdinalIgnoreCase)).ToList();
    var f5f7NonIdentiques = fiche.Where(l => l.AncienneTouche is "F5" or "F7" && l.EcranV3 is { } e && EcranCorrespondances.EntreeLanceLaRecherche.Contains(e)
                                             && l.Geste == "Lancer la recherche" && (l.Statut != StatutsCorrespondance.Identique || l.NouvelleTouche != l.AncienneTouche)).ToList();
    Exiger(rechargeDite.Count == 0 && f5f7NonIdentiques.Count == 0 && fiche.Any(l => l.AncienneTouche == "F7" && l.EcranV3 == "PROC_REQUI" && l.Statut == StatutsCorrespondance.Identique)
           && fiche.Any(l => l.AncienneTouche == "F5" && l.EcranV3 == "PROC_REQUI" && l.Statut == StatutsCorrespondance.Identique),
        $"SOC-027 CA02 : la fiche ne previent plus que F5 recharge ({rechargeDite.Count} ligne(s)) ; F7 et F5 des Requisitions sont « meme touche »"
        + (f5f7NonIdentiques.Count == 0 ? "" : $" ; en defaut : {string.Join(" | ", f5f7NonIdentiques.Take(4).Select(l => $"{l.Numero} {l.EcranV2} {l.AncienneTouche}"))}"));
    var application = File.ReadAllText(Path.Combine(racine, "web", "src", "composants", "Application.tsx"));
    var clavier = File.ReadAllText(Path.Combine(racine, "web", "src", "plan", "clavier.ts"));
    Exiger(application.Contains("e.key.toLowerCase() !== 'k'") && clavier.Contains("e.key !== 'Escape'") && aiguillage.Contains("case 'PROC_TOUCHES':"),
        "SOC-027 CA04 : les touches communes annoncees existent (Ctrl K : la palette ; Echap : la fermeture ; « ? » : l'ecran de la fiche)");
    var ecartsCritere = fiche.Count(l => l.Explication.Contains("n'a pas de critère de recherche"));
    if (ecartsCritere > 0)
        Reste($"SOC-027 CA04 : {ecartsCritere} touche(s) de recherche de la V2 visent un ecran construit sans critere de recherche (ecart a valider).");

    // CA05 — les retours des greffes beta, integres avant la formation des autres.
    Exiger(EcranCorrespondances.RefusLivraison(EcranCorrespondances.EtatFiche, EcranCorrespondances.RetoursBeta, fiche) is null,
        $"SOC-027 CA05 : l'etat de la fiche ({EcranCorrespondances.EtatFiche}) tient la regle des retours des greffes beta");
    var retourOuvert = new RetourBeta("4302", 1, "Le Puy : F7 manque sur l'ecran", Integre: false);
    Exiger(EcranCorrespondances.RefusLivraison("livrable", [retourOuvert], fiche) is not null
           && EcranCorrespondances.RefusLivraison("livrable", [retourOuvert with { Integre = true }], fiche) is null
           && EcranCorrespondances.RefusLivraison("livrable", [], fiche) is not null,
        "SOC-027 CA05 : la fiche ne part aux autres greffes que lorsque chaque retour beta est integre (jumeau : integre, elle part)");
    Reste("SOC-027 CA05 : les retours des greffes beta (Le Puy, Chartres, Vienne) se recueillent chez eux, pas depuis ce poste.");

    // Le serveur du banc s'arrete avant d'etre detruit (decision du lot 1 de la phase 2, controle « Arret » de
    // tests/SocleCourrielsCheck) : detruit sans StopAsync, l'hote annule ses services de fond sans s'etre arrete et le
    // Surveillant (lot 2) sort en « BackgroundService failed » au journal, une fausse panne apres un banc vert.
    await app.StopAsync();
}
finally
{
    PreferencesServeur.Fermer();
    Environment.SetEnvironmentVariable("RIGV3_ETAT", null);
    try { dossierEtat.Delete(recursive: true); } catch (IOException) { }
}

Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "SoclePreferencesCheck : vert." : $"SoclePreferencesCheck : {echecs.Count} echec(s).");
return echecs.Count == 0 ? 0 : 1;

// Le code HTTP d'un appel du client (200 s'il reussit).
static async Task<int> Statut(Func<Task> appel)
{
    try { await appel(); return 200; }
    catch (ErreurServeur e) { return e.CodeHttp; }
}

// Les couples (procedure, touche) du code de la V2 : la MEME lecture que tests/SoclePreferencesCheck/inventorier_touches_v2.py.
static HashSet<(string Procedure, string Touche)> LireToucheDeLaV2(string source)
{
    var comparaison = new Regex(@"(?:==\s*Keys\.|case\s+Keys\.)(F\d{1,2}|Enter|Return|Escape)\b");
    var couples = new HashSet<(string, string)>();
    foreach (var f in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
    {
        if (Path.GetFileName(f).Contains("Designer", StringComparison.Ordinal)) continue;
        var proc = Path.GetRelativePath(source, f).Split(Path.DirectorySeparatorChar)
            .FirstOrDefault(x => x.StartsWith("PROC_", StringComparison.OrdinalIgnoreCase));
        if (proc is null) continue;
        foreach (var ligne in File.ReadLines(f))
        {
            if (ligne.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
            foreach (Match m in comparaison.Matches(ligne))
                couples.Add((proc.ToUpperInvariant(), m.Groups[1].Value switch { "Enter" or "Return" => "Entree", "Escape" => "Echap", var k => k }));
        }
    }
    return couples;
}

static List<(string Procedure, string Touche)> Manquants(HashSet<(string Procedure, string Touche)> duCode, HashSet<(string Procedure, string Touche)> inventaire)
    => [.. duCode.Where(p => !inventaire.Contains(p)).OrderBy(p => p.Procedure).ThenBy(p => p.Touche)];

// Le releveur des fenetres de document : ce que l'app montrerait (SOC-013 T03).
sealed class AfficheurDuBanc : IAfficheurDocuments
{
    private readonly List<(NoeudGed Piece, IReadOnlyList<(string Libelle, string Valeur)> Valeurs)> montres = [];

    public List<(NoeudGed Piece, IReadOnlyList<(string Libelle, string Valeur)> Valeurs)> Montres { get { lock (montres) return [.. montres]; } }

    public void Montrer(NoeudGed piece, IReadOnlyList<(string Libelle, string Valeur)> valeurs) { lock (montres) montres.Add((piece, valeurs)); }
}
