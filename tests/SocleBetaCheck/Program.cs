using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Passage;
using RigV3.Serveur;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;
using RigV3.Serveur.Socle.Supervision;
using RigV3Desktop;
using RigV3Desktop.Pont;

// Banc du lot 17 du plan « Reconstruction de RIG — phase 2 » (2026-11-21) : les trois ecrans que le lot 13 a laisses
// « a maquetter » (docs\reconstruction\socle-exploitation.md), sans lesquels la beta ne peut ni demarrer ni se suivre.
// Un controle par critere, qui cite son id ; chaque refus a son jumeau accepte.
//
//   SOC-019 CA04 : un greffe passe a la V3 retrouve le certificat de la V3, avec sa date de validite, sans passer par
//                  l'assistance (route GET /socle/greffes/{greffe}/certificat, ecran « Certificat NF525 de la V3 ») ;
//                  sans certificat : « certification en cours » ; expire ou d'une autre version : dit tel quel.
//   SOC-022 CA06 : un agent signale un defaut depuis la V3 en un geste, avec l'identifiant de suivi (le mecanisme des
//                  erreurs du lot 2) ; Amitel le voit dans l'ecran Supervision ; « RigV3.Passage beta defauts » le lit
//                  et le classe par la regle de SOC-022 CA03.
//   SOC-026 CA06 : a la premiere ouverture, l'aide « Premiers pas » montre les gestes principaux et l'acces a la fiche
//                  des touches ; « vue » suit l'agent d'un serveur RDS a l'autre ; elle se rouvre depuis le menu.
//
// Le vrai serveur en memoire (ServeurRig.Construire), sur une base commune et la base V3 d'un greffe, jetables ; les
// ponts des ecrans (Pont/EcranCertificat.cs, EcranSignalement.cs, EcranSupervision.cs, EcranAide.cs) avec le client de
// l'app ; deux « postes » (deux fichiers accueil.json, RIGV3_ETAT) et deux serveurs RDS. Il lui faut le SQL Server de
// developpement : « docker compose up -d » a la racine du depot. Rien n'est lu ni ecrit sur SQL-DEV ni SQL-PROD.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
// Ce qu'aucun banc de ce poste ne peut faire : dit, pas compte vert (docs/reconstruction/socle-exploitation.md, lot 17).
void Reste(string quoi) => Console.WriteLine("RESTE : " + quoi);
string Lire(string relatif) => File.ReadAllText(Path.Combine(racine, relatif));

var temp = Directory.CreateTempSubdirectory("rigv3-lot17-");
string Poste(string nom) => Path.Combine(temp.FullName, nom, "accueil.json");
void SurLePoste(string nom) => Environment.SetEnvironmentVariable("RIGV3_ETAT", Poste(nom));
SurLePoste("banc");

const string Greffe = "4302";
const string Agent1 = "banc.beta1", Agent2 = "banc.beta2", Amitel = "banc.amitel";

try
{
    // =================================================================================================================
    // Les bases, les agents, le serveur
    // =================================================================================================================
    await using var commune = await BaseJetable.CreerAsync("SocleBetaCommune");
    var jouees = await Migrateur.AppliquerAsync(commune.Connexion, Path.Combine(racine, "Base", "migrations"));
    Exiger(jouees.Jouees.Contains("SOC-019_certificat") && jouees.Jouees.Contains("SOC-022_defauts"),
        "SOC-019 T03 / SOC-022 T03 : les migrations SOC-019_certificat et SOC-022_defauts sont jouees sur une base neuve, avec les autres du depot");
    await using var v3 = await BaseJetable.CreerAsync("SocleBetaLePuy");
    await Migrateur.AppliquerAsync(v3.Connexion, Path.Combine(racine, "Base", "migrations"));

    async Task Executer(string connexion, string sql, params (string Nom, object Valeur)[] p)
    {
        await using var cn = new SqlConnection(connexion);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var (nom, valeur) in p) cmd.Parameters.AddWithValue(nom, valeur);
        await cmd.ExecuteNonQueryAsync();
    }
    async Task<T> Valeur<T>(string connexion, string sql, params (string Nom, object Valeur)[] p)
    {
        await using var cn = new SqlConnection(connexion);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var (nom, valeur) in p) cmd.Parameters.AddWithValue(nom, valeur);
        var v = await cmd.ExecuteScalarAsync();
        return v is null or DBNull ? default! : (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }
    // Deux agents du greffe beta de Le Puy, admis dans SA base ; l'exploitation d'Amitel, admise dans la base commune.
    const string Comptes = """
        INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES (@greffe, 'BETA', N'Beta');
        INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANC', 'Banc SocleBeta');
        INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive, EstAmitel, EstAdministrateur)
        SELECT v.Compte, v.Nom, 'Banc', v.Abrege, 'Mme', @greffe, s.Id, 0, v.Amitel, 0
        FROM (VALUES (@a, 'DUPONT', 'BDUP', 0), (@b, 'MARTIN', 'BMAR', 0), (@c, 'EXPLOITANT', 'BEXP', 1)) v (Compte, Nom, Abrege, Amitel)
        CROSS JOIN soc.Service s
        WHERE v.Amitel = @amitel;
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte;
        """;
    await Executer(v3.Connexion, Comptes, ("@greffe", Greffe), ("@a", Agent1), ("@b", Agent2), ("@c", Amitel), ("@amitel", 0));
    await Executer(commune.Connexion, Comptes, ("@greffe", "9999"), ("@a", Agent1), ("@b", Agent2), ("@c", Amitel), ("@amitel", 1));
    var serveurBancs = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
    var authentification = new SqlConnectionStringBuilder(serveurBancs.ConnectionString);
    authentification.Remove("Data Source");
    await Executer(commune.Connexion,
        "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe) " +
        "VALUES (@c, N'Le Puy', @s, 'RIG_BANC_BETA', @v3, 1, 0, 'Romance Standard Time', 'banc', SYSDATETIME());",
        ("@c", Greffe), ("@s", serveurBancs.DataSource), ("@v3", v3.Nom));

    await using var app = ServeurRig.Construire(["--environment", "Development"], s =>
    {
        s.WebHost.UseTestServer();
        s.Logging.SetMinimumLevel(LogLevel.Warning);
        s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
        s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
        s.Configuration["Supervision:Automatique"] = "false";
    });
    await app.StartAsync();
    var http = app.GetTestClient();
    // Le client de l'app, au nom d'un agent (l'en-tete de developpement que la production ignore, SOC-001 CA07).
    ClientServeur Client(string compte)
    {
        Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, compte);
        try { return new ClientServeur(new Uri("http://localhost/"), app.GetTestServer().CreateHandler()); }
        finally { Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null); }
    }
    async Task<HttpResponseMessage> Envoyer(HttpMethod verbe, string adresse, object? corps, string compte, string? suivi = null)
    {
        var req = new HttpRequestMessage(verbe, adresse);
        if (corps is not null) req.Content = JsonContent.Create(corps);
        req.Headers.Add(RoutesIdentite.EnTeteCompte, compte);
        if (suivi is not null) req.Headers.Add(RoutesSupervision.EnTeteSuivi, suivi);
        return await http.SendAsync(req);
    }
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var agent = Client(Agent1);
    var amitel = Client(Amitel);
    var versionServeur = (await agent.LireAsync<VersionServeur>(RoutesVersions.Serveur)).Version;
    var majeure = string.Join('.', versionServeur.Split('.').Take(2));
    var aujourdhui = DateOnly.FromDateTime(DateTime.Today);

    // Acces : chaque route des deux modules du lot porte sa politique, lue dans le serveur lui-meme.
    var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().ToList();
    IEnumerable<string> Politiques(RouteEndpoint r) => r.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy ?? "session");
    var duLot = routes.Where(r => r.Metadata.GetMetadata<ModuleOrigine>()?.Nom is "RigV3.Serveur.Socle.Certification.ModuleCertification"
                                                                                 or "RigV3.Serveur.Socle.Supervision.ModuleDefauts").ToList();
    var lirePourGreffe = duLot.Where(r => r.RoutePattern.RawText == RoutesCertification.Certificat && Politiques(r).Contains(PolitiquesSocle.AgentAdmis)).ToList();
    var reserveesAmitel = duLot.Where(r => Politiques(r).Contains(PolitiquesSupervision.ExploitationAmitel)).Select(r => r.RoutePattern.RawText).ToList();
    var sansSession = duLot.Where(r => !Politiques(r).Any()).Select(r => r.RoutePattern.RawText).ToList();
    Exiger(duLot.Count == 4 && lirePourGreffe.Count == 1 && sansSession.Count == 0
           && reserveesAmitel.OrderBy(x => x).SequenceEqual(new[] { RoutesCertification.Enregistrer, RoutesSupervision.DefautsSignales }.OrderBy(x => x)),
        $"Acces : les {duLot.Count} routes du lot exigent une session ; le certificat se lit par tout agent admis du greffe, " +
        $"l'enregistrer et lire les defauts sont reserves a Amitel ({string.Join(", ", reserveesAmitel)}) ; signaler est ouvert a toute session");

    // =================================================================================================================
    // SOC-019 CA04 — le certificat de la V3, retrouve par le greffe avec sa validite
    // =================================================================================================================
    Console.WriteLine();
    Console.WriteLine("--- SOC-019 CA04 : le certificat de la V3, dans la V3");
    var routeCertificat = RoutesCertification.Pour(Greffe);

    // Sans certificat enregistre : « certification en cours », jamais un faux certificat (le serveur, puis l'ecran).
    var avant = await agent.LireAsync<EtatCertificat>(routeCertificat);
    var ecranAvant = await EcranCertificat.OuvrirAsync(agent, Greffe);
    Exiger(avant is { Etat: EtatsCertificat.EnCours, Certificat: null, EnregistreLe: null } && avant.VersionEnService == versionServeur
           && ecranAvant is { Mode: "serveur", Etat: { Etat: EtatsCertificat.EnCours, Certificat: null } },
        $"SOC-019 CA04 : sans certificat enregistre, le greffe lit « {avant.Etat} » (version en service {avant.VersionEnService}), sans certificat d'exemple");
    var demo = await EcranCertificat.OuvrirAsync(null, null);
    Exiger(demo is { Mode: "demo", Etat: { Etat: EtatsCertificat.EnCours, Certificat: null } } && demo.Message!.Contains("aucun d'exemple"),
        "SOC-019 CA04 : sans serveur (demonstration), l'ecran dit « certification en cours » et ne montre aucun certificat");

    // L'enregistrement : Amitel seul ; un certificat qui n'en est pas un est refuse ; son jumeau complet passe.
    Certificat Cert(string numero, string version, DateOnly delivre, DateOnly jusqua, string accreditation = "COFRAC 5-0012")
        => new(RoutesCertification.LogicielV3, "AFNOR Certification", accreditation, numero, "NF525 version 2", version, delivre, jusqua);
    var valide = Cert("NF525-V3-2027-001", majeure, aujourdhui.AddDays(-15), aujourdhui.AddYears(3));
    var parAgent = await Envoyer(HttpMethod.Put, RoutesCertification.Enregistrer, valide, Agent1);
    var sansAccreditation = await Envoyer(HttpMethod.Put, RoutesCertification.Enregistrer, valide with { Accreditation = " " }, Amitel);
    var deLaV2 = await Envoyer(HttpMethod.Put, RoutesCertification.Enregistrer, valide with { Logiciel = "V2" }, Amitel);
    var dates = await Envoyer(HttpMethod.Put, RoutesCertification.Enregistrer, valide with { ValideJusquau = valide.DelivreLe.AddDays(-1) }, Amitel);
    var nombreAvant = await Valeur<int>(commune.Connexion, "SELECT COUNT(*) FROM soc.CertificatLogiciel;");
    Exiger(parAgent.StatusCode == HttpStatusCode.Forbidden && sansAccreditation.StatusCode == HttpStatusCode.BadRequest
           && deLaV2.StatusCode == HttpStatusCode.BadRequest && dates.StatusCode == HttpStatusCode.BadRequest && nombreAvant == 0
           && (await sansAccreditation.Content.ReadAsStringAsync()).Contains("organisme accrédité"),
        $"SOC-019 CA04 : un agent de greffe n'enregistre pas le certificat ({(int)parAgent.StatusCode}) ; Amitel non plus un certificat sans " +
        $"accreditation ({(int)sansAccreditation.StatusCode}), celui de la V2 ({(int)deLaV2.StatusCode}) ou aux dates inversees ({(int)dates.StatusCode}) : rien n'est ecrit");
    var enregistre = await amitel.ModifierAsync<Certificat, EtatCertificat>(RoutesCertification.Enregistrer, valide);
    var journal = await Valeur<int>(commune.Connexion,
        "SELECT COUNT(*) FROM soc.Journal WHERE Famille = 'certificat' AND IdObjet = @n AND Verbe = 'a cree' AND Agent IS NOT NULL;", ("@n", valide.Numero));
    Exiger(enregistre is { Etat: EtatsCertificat.Valide, Greffe: null } && journal == 1,
        $"SOC-019 CA04 (jumeau) : le certificat complet d'un organisme accredite s'enregistre (Amitel, {enregistre.Etat}), au journal des gestes de la base commune");

    // Le greffe le retrouve, avec sa date de validite, dans la V3.
    var lu = await agent.LireAsync<EtatCertificat>(routeCertificat);
    var ecran = await EcranCertificat.OuvrirAsync(agent, Greffe);
    Exiger(lu is { Etat: EtatsCertificat.Valide, Motifs.Length: 0 } && lu.Certificat == valide && lu.Greffe == Greffe && lu.EnregistreLe is not null
           && ecran.Etat?.Certificat == valide && ecran.Mode == "serveur",
        $"SOC-019 CA04 : le greffe {Greffe} retrouve le certificat {lu.Certificat?.Numero} de {lu.Certificat?.Organisme} ({lu.Certificat?.Accreditation}), " +
        $"version {lu.Certificat?.VersionCouverte}, valide jusqu'au {lu.Certificat?.ValideJusquau:dd/MM/yyyy} : « {lu.Etat} » pour la version {lu.VersionEnService}");

    // Expire : dit tel quel. Le renouvellement d'Amitel est une ligne de plus ; un certificat enregistre ne se modifie pas.
    var expire = Cert("NF525-V3-2023-007", majeure, aujourdhui.AddYears(-3), aujourdhui.AddDays(-1));
    await amitel.ModifierAsync<Certificat, EtatCertificat>(RoutesCertification.Enregistrer, expire);
    var luExpire = await agent.LireAsync<EtatCertificat>(routeCertificat);
    Exiger(luExpire is { Etat: EtatsCertificat.Expire } && luExpire.Certificat == expire
           && luExpire.Motifs.Any(m => m.Contains($"expiré le {expire.ValideJusquau:dd/MM/yyyy}")),
        $"SOC-019 CA04 : un certificat expire est dit tel quel (« {luExpire.Etat} » : {string.Join(" ; ", luExpire.Motifs)}), le certificat reste lisible");
    // D'une autre version que celle en service (Directory.Build.props) : signale.
    var autreVersion = Cert("NF525-V3-2025-003", "2025.9", aujourdhui.AddDays(-10), aujourdhui.AddYears(2));
    await amitel.ModifierAsync<Certificat, EtatCertificat>(RoutesCertification.Enregistrer, autreVersion);
    var luAutre = await agent.LireAsync<EtatCertificat>(routeCertificat);
    Exiger(luAutre is { Etat: EtatsCertificat.AutreVersion } && luAutre.Motifs.Any(m => m.Contains("2025.9") && m.Contains(versionServeur)),
        $"SOC-019 CA04 : un certificat qui ne couvre pas la version en service est signale (« {luAutre.Etat} » : {string.Join(" ; ", luAutre.Motifs)})");
    // La borne de validite, des deux cotes : valide le dernier jour, expire le lendemain (la meme regle que la beta, CA01).
    var (dernierJour, _) = RegleCertificat.Juger(valide, valide.ValideJusquau, versionServeur);
    var (lendemain, _) = RegleCertificat.Juger(valide, valide.ValideJusquau.AddDays(1), versionServeur);
    var (veille, _) = RegleCertificat.Juger(valide, valide.DelivreLe.AddDays(-1), versionServeur);
    Exiger(dernierJour == EtatsCertificat.Valide && lendemain == EtatsCertificat.Expire && veille == EtatsCertificat.PasEncoreValide
           && RegleCertificat.Juger(valide with { Accreditation = "" }, aujourdhui, versionServeur).Etat == EtatsCertificat.Incomplet,
        $"SOC-019 CA04 / CA01 : borne de validite jouee des deux cotes (dernier jour : {dernierJour}, lendemain : {lendemain}, veille de la delivrance : {veille}) ; sans accreditation : incomplet");
    string? modif = null;
    try { await Executer(commune.Connexion, "UPDATE soc.CertificatLogiciel SET ValideJusquau = DATEADD(year, 5, ValideJusquau);"); }
    catch (SqlException e) { modif = e.Message; }
    Exiger(modif?.Contains("ne se modifie pas") == true,
        "SOC-019 CA04 : un certificat enregistre ne se modifie pas (prolonger sa validite est refuse par la base : enregistrer le nouveau)");
    // Le renouvellement remet l'etat valide : le dernier enregistre fait foi.
    var renouvele = await amitel.ModifierAsync<Certificat, EtatCertificat>(RoutesCertification.Enregistrer, valide with { Numero = "NF525-V3-2027-002" });
    Exiger(renouvele.Etat == EtatsCertificat.Valide && (await agent.LireAsync<EtatCertificat>(routeCertificat)).Certificat?.Numero == "NF525-V3-2027-002"
           && await Valeur<int>(commune.Connexion, "SELECT COUNT(*) FROM soc.CertificatLogiciel;") == 4,
        "SOC-019 CA04 : le certificat renouvele par Amitel est celui que le greffe lit ; les precedents restent (historique)");

    // L'ecran au menu (Configurer), avec ses trois listes, sans origine V2.
    var menu = Lire(Path.Combine("Pont", "MenuAccueil.cs"));
    var aiguillage = Lire(Path.Combine("web", "src", "plan", "Aiguillage.tsx"));
    Exiger(MenuAccueil.VerbeDe(EcranCertificat.Code)?.Titre == "Configurer" && CatalogueEcrans.Nom(EcranCertificat.Code) == EcranCertificat.NomEcran
           && aiguillage.Contains("case 'PROC_CERTIFICAT':") && aiguillage.Contains("<EcranCertificat />")
           && Lire(Path.Combine("web", "src", "ecrans", "EcranCertificat.tsx")).Contains("Sans origine RIG v2"),
        $"SOC-019 T04 : l'ecran « {EcranCertificat.NomEcran} » s'ouvre depuis le menu ({EcranAide.CheminMenu(EcranCertificat.Code)}), catalogue et aiguillage compris");

    // =================================================================================================================
    // SOC-022 CA06 — signaler un defaut depuis la V3, en un geste, avec l'identifiant de suivi
    // =================================================================================================================
    Console.WriteLine();
    Console.WriteLine("--- SOC-022 CA06 : un defaut signale depuis la V3");
    // L'agent ouvre son greffe (la session de l'app), puis une erreur lui est montree (le mecanisme du lot 2).
    var identite = await SessionAgent.OuvrirAsync(agent, new GreffeDuPoste(Greffe, "Le Puy", EtatsGreffeDuPoste.Ouvrable));
    var erreurMontree = await EcranSupervision.SignalerAsync(agent, "EcranCompteurs.Rendre", new InvalidOperationException("panne d'essai du banc"), Greffe);
    Exiger(identite.Admis && SessionAgent.CodeGreffe == Greffe && erreurMontree is not null && EcranSupervision.DerniereErreur == erreurMontree,
        $"SOC-022 CA06 : l'agent a ouvert son greffe {SessionAgent.Greffe?.Libelle} ; la derniere erreur montree ({erreurMontree?.Suivi}) est gardee par la session");

    // Ce qui part sans saisie, montre a l'agent avant qu'il ecrive.
    var heure = new DateTimeOffset(2026, 11, 21, 14, 32, 0, TimeSpan.FromHours(1));
    var contexte = EcranSignalement.Contexte(agent, "Compteurs de numéros", "PROC_COMPTEURS_NUM", heure);
    Exiger(contexte is { Mode: "serveur", CodeGreffe: Greffe, Ecran: "Compteurs de numéros", CodeEcran: "PROC_COMPTEURS_NUM", Heure: "21/11/2026 14:32" }
           && contexte.Version == versionServeur && contexte.Greffe == "Le Puy (4302)" && contexte.DerniereErreur == erreurMontree!.Suivi
           && contexte.Agent.Contains("DUPONT", StringComparison.OrdinalIgnoreCase),
        $"SOC-022 CA06 : le signalement emporte sans saisie l'ecran ({contexte.Ecran}), le greffe ({contexte.Greffe}), l'agent ({contexte.Agent}), " +
        $"la version ({contexte.Version}), l'heure ({contexte.Heure}) et la derniere erreur suivie ({contexte.DerniereErreur})");

    // Le geste : une phrase, envoyer. Le serveur rend l'identifiant de suivi.
    const string Phrase = "Le numéro rendu ne réapparaît pas dans la liste après « Rendre le numéro ».";
    var signale = await EcranSignalement.EnvoyerAsync(agent, new DemandeSignalement(Phrase, "Compteurs de numéros", "PROC_COMPTEURS_NUM"), heure);
    var suivi = signale.Suivi ?? "";
    Exiger(signale.Envoye && Suivis.Normaliser(suivi) == suivi && signale.Message.Contains(suivi) && suivi != erreurMontree.Suivi,
        $"SOC-022 CA06 : un geste, une phrase : l'agent recoit l'identifiant de suivi {suivi} (« {signale.Message} »)");
    // Ce que le serveur a garde : le compte vient de la session, pas de l'ecran.
    var ligne = await Valeur<string>(commune.Connexion,
        "SELECT CONCAT(CodeGreffe, '|', Compte, '|', Ecran, '|', CodeEcran, '|', Version, '|', CAST(HeurePoste AS varchar(34)), '|', DerniereErreur, '|', Phrase) " +
        "FROM soc.DefautSignale WHERE Suivi = @s;", ("@s", suivi));
    Exiger(ligne == $"{Greffe}|{Agent1}|Compteurs de numéros|PROC_COMPTEURS_NUM|{versionServeur}|2026-11-21 14:32:00 +01:00|{erreurMontree.Suivi}|{Phrase}",
        $"SOC-022 CA06 : le defaut est garde avec son identifiant, le greffe, le compte de la session, l'ecran, la version, l'heure du poste " +
        $"(avec son decalage, pour l'outre-mer) et la derniere erreur ({ligne})");

    // L'identifiant est celui du parcours de la requete (en-tete X-Rig-Suivi), comme pour les erreurs ; un identifiant envoye
    // par l'ecran (parcours deja commence) n'est jamais repris par un defaut : un identifiant = un defaut.
    var brut = await Envoyer(HttpMethod.Post, RoutesSupervision.Defauts,
        new SignalementDefaut("Deuxième défaut du banc.", "Accueil", null, Greffe, versionServeur, heure, null, "RDS-B"), Agent2);
    var brutSuivi = (await brut.Content.ReadFromJsonAsync<SuiviErreur>(json))!;
    var avecEnTete = await Envoyer(HttpMethod.Post, RoutesSupervision.Defauts,
        new SignalementDefaut("Troisième défaut du banc.", "Accueil", null, Greffe, versionServeur, heure, null, "RDS-B"), Agent2, suivi);
    var avecEnTeteSuivi = (await avecEnTete.Content.ReadFromJsonAsync<SuiviErreur>(json))!;
    Exiger(brut.IsSuccessStatusCode && brut.Headers.GetValues(RoutesSupervision.EnTeteSuivi).Single() == brutSuivi.Suivi
           && avecEnTete.IsSuccessStatusCode && avecEnTeteSuivi.Suivi != suivi
           && avecEnTete.Headers.GetValues(RoutesSupervision.EnTeteSuivi).Single() == avecEnTeteSuivi.Suivi,
        $"SOC-022 CA06 : l'identifiant d'un defaut est celui du parcours rendu en en-tete {RoutesSupervision.EnTeteSuivi} ({brutSuivi.Suivi}) ; " +
        $"un identifiant deja pris envoye par l'ecran n'est pas repris ({avecEnTeteSuivi.Suivi})");

    // Refus et jumeaux : une phrase vide ou trop longue ne s'ecrit pas, une phrase de 1000 signes passe.
    var vide = await Envoyer(HttpMethod.Post, RoutesSupervision.Defauts, new SignalementDefaut("  ", "Accueil", null, Greffe, null, null, null, null), Agent1);
    var trop = await Envoyer(HttpMethod.Post, RoutesSupervision.Defauts,
        new SignalementDefaut(new string('x', RoutesSupervision.PhraseAuPlus + 1), "Accueil", null, Greffe, null, null, null, null), Agent1);
    var mauvaiseErreur = await Envoyer(HttpMethod.Post, RoutesSupervision.Defauts,
        new SignalementDefaut("défaut", "Accueil", null, Greffe, null, null, "pas-un-suivi", null), Agent1);
    var pile = await Envoyer(HttpMethod.Post, RoutesSupervision.Defauts,
        new SignalementDefaut(new string('y', RoutesSupervision.PhraseAuPlus), "Accueil", null, Greffe, null, null, null, null), Agent1);
    var pontVide = await EcranSignalement.EnvoyerAsync(agent, new DemandeSignalement("", "Accueil", null), heure);
    var pontDemo = await EcranSignalement.EnvoyerAsync(null, new DemandeSignalement("essai", "Accueil", null), heure);
    var nombre = await Valeur<int>(commune.Connexion, "SELECT COUNT(*) FROM soc.DefautSignale;");
    Exiger(vide.StatusCode == HttpStatusCode.BadRequest && trop.StatusCode == HttpStatusCode.BadRequest && mauvaiseErreur.StatusCode == HttpStatusCode.BadRequest
           && pile.IsSuccessStatusCode && nombre == 4 && pontVide is { Envoye: false, Suivi: null } && pontDemo is { Envoye: false, Suivi: null, Contexte.Mode: "demo" },
        $"SOC-022 CA06 : sans phrase, avec une phrase de plus de {RoutesSupervision.PhraseAuPlus} signes ou une « derniere erreur » qui n'est pas un identifiant, rien ne s'ecrit " +
        $"({(int)vide.StatusCode}, {(int)trop.StatusCode}, {(int)mauvaiseErreur.StatusCode}) ; {RoutesSupervision.PhraseAuPlus} signes passent ; sans serveur, rien ne part ni aucun identifiant ({nombre} defauts)");

    // Amitel le voit dans l'ecran Supervision ; un agent de greffe ne lit pas la liste.
    var supervision = await EcranSupervision.OuvrirAsync(amitel);
    var recherche = await EcranSupervision.RechercherAsync(amitel, new RechercheErreurs(suivi.ToLowerInvariant(), null, null, null, null));
    var listeParAgent = await Envoyer(HttpMethod.Get, RoutesSupervision.DefautsSignales, null, Agent1);
    Exiger(supervision.Mode == "serveur" && supervision.Defauts?.Any(d => d.Suivi == suivi && d.Phrase == Phrase && d.Compte == Agent1) == true
           && recherche.Defauts is [{ } trouve] && trouve.Suivi == suivi && recherche.Message!.Contains("est un défaut signalé")
           && listeParAgent.StatusCode == HttpStatusCode.Forbidden,
        $"SOC-022 CA06 : Amitel voit le defaut {suivi} dans l'ecran Supervision ({supervision.Defauts?.Length} defaut(s)) et le retrouve par l'identifiant que l'agent dicte " +
        $"(« {recherche.Message} ») ; un agent de greffe ne lit pas la liste ({(int)listeParAgent.StatusCode})");
    var parcoursErreur = await EcranSupervision.RechercherAsync(amitel, new RechercheErreurs(erreurMontree.Suivi, null, null, null, null));
    Exiger(parcoursErreur.Erreurs.Any(e => e.Programme == "EcranCompteurs.Rendre"),
        $"SOC-022 CA06 : la derniere erreur que le defaut emporte ({erreurMontree.Suivi}) ramene son parcours dans la Supervision");

    // « RigV3.Passage beta defauts » le lit, avec son identifiant de suivi, et la regle de CA03 le classe une fois decrit.
    var carnet = Directory.CreateDirectory(Path.Combine(temp.FullName, "carnet")).FullName;
    var dossier = Directory.CreateDirectory(Path.Combine(temp.FullName, "beta")).FullName;
    var bancs = Directory.CreateDirectory(Path.Combine(temp.FullName, "bancs")).FullName;
    await File.WriteAllTextAsync(Path.Combine(carnet, "essai.json"), """[ { "id": "TST-001", "titre": "essai", "valide_par": "Referent", "validation_greffe_beta": false } ]""");
    await File.WriteAllTextAsync(Path.Combine(bancs, "Banc.cs"), "// TST-001 CA01\n");
    async Task<(int Code, string Texte)> Beta(string quoi)
    {
        var s = new StringWriter();
        var c = await LigneDeCommande.ExecuterAsync(["beta", quoi, "--carnet", carnet, "--dossier", dossier, "--commune", commune.Connexion, "--bancs", bancs], s);
        return (c, s.ToString());
    }
    var listeBrute = await Beta("defauts");
    var finBrute = await Beta("terminer");
    var tousLesSuivis = (await EcranSupervision.OuvrirAsync(amitel)).Defauts!.Select(d => d.Suivi).ToList();
    Exiger(listeBrute.Code == 0 && Regex.IsMatch(listeBrute.Texte, $@"A CLASSER\s+\(V3\)\s+suivi {suivi}\s+greffe {Greffe}\s+{Regex.Escape(Phrase)}")
           && listeBrute.Texte.Contains("ecran « Compteurs de numéros »") && listeBrute.Texte.Contains($"derniere erreur {erreurMontree.Suivi}")
           && tousLesSuivis.All(listeBrute.Texte.Contains),
        $"SOC-022 CA06 / CA03 : « beta defauts » lit les {tousLesSuivis.Count} defauts signales depuis la V3 avec leur identifiant de suivi, « a classer » tant qu'aucune personne ne les a decrits");
    Exiger(finBrute.Code == 1 && finBrute.Texte.Contains("MANQUE   SOC-022 CA04 : aucun defaut bloquant ouvert") && finBrute.Texte.Contains("pas encore classe"),
        "SOC-022 CA04 (contre-epreuve) : un defaut signale et pas encore classe garde la beta ouverte : il peut etre bloquant");
    // Classe dans defauts-beta.json sous son identifiant : la regle ecrite dit bloquant ou non.
    async Task Classer(bool bloquant)
    {
        var defauts = tousLesSuivis.Select((s, i) => s == suivi
            ? new DefautBeta("BETA-101", s, Greffe, "numero rendu absent de la liste", RegleDefauts.Ouvert, GesteImpossible: true, Contournement: !bloquant)
            : new DefautBeta($"BETA-{102 + i}", s, Greffe, "defaut d'essai du banc", RegleDefauts.Clos)).ToList();
        await File.WriteAllTextAsync(Path.Combine(dossier, "defauts-beta.json"), JsonSerializer.Serialize(defauts, RapportPassage.Json));
    }
    await Classer(bloquant: false);
    var listeNon = await Beta("defauts");
    var finNon = await Beta("terminer");
    await Classer(bloquant: true);
    var listeBloquant = await Beta("defauts");
    var finBloquant = await Beta("terminer");
    Exiger(Regex.IsMatch(listeNon.Texte, $@"non bloquant\s+BETA-101\s+suivi {suivi}") && !listeNon.Texte.Contains("A CLASSER")
           && finNon.Texte.Contains("OK       SOC-022 CA04 : aucun defaut bloquant ouvert")
           && Regex.IsMatch(listeBloquant.Texte, $@"BLOQUANT\s+BETA-101\s+suivi {suivi}.*sans contournement")
           && finBloquant.Texte.Contains($"MANQUE   SOC-022 CA04 : aucun defaut bloquant ouvert — 1 defaut(s) bloquant(s) ouvert(s) : BETA-101 [{suivi}]"),
        $"SOC-022 CA03 : decrit sous son identifiant {suivi}, le defaut est classe par la regle ecrite : avec contournement non bloquant (la fin de beta ne l'attend pas), " +
        "sans contournement BLOQUANT (la beta reste ouverte, l'identifiant cite)");

    // Le geste : un bouton dans l'en-tete, une touche dans la fiche des touches, la meme boite partout.
    var entete = Lire(Path.Combine("web", "src", "accueil", "Entete.tsx"));
    var boite = Lire(Path.Combine("web", "src", "composants", "SignalementDefaut.tsx"));
    var page = Lire(Path.Combine("web", "src", "composants", "Application.tsx"));
    var ligneFiche = EcranCorrespondances.Fiche.SingleOrDefault(l => l.NouvelleTouche == EcranCorrespondances.ToucheSignalement);
    Exiger(Regex.IsMatch(entete, @"aria-label=""Signaler un défaut""\s+onClick=\{\(\) => demanderSignalement\(\)\}")
           && Regex.IsMatch(boite, @"e\.shiftKey && !e\.altKey && !e\.repeat && e\.key\.toLowerCase\(\) === 'd'") && EcranCorrespondances.ToucheSignalement == "Ctrl Maj D"
           && page.Contains("<SignalementDefaut ecran={ecranOuvert} />")
           && ligneFiche is { Statut: StatutsCorrespondance.Nouveau, EcranV2: EcranCorrespondances.ToutesLesProcedures }
           && boite.Contains("'EcranSignalement.Envoyer'") && boite.Contains("'EcranSignalement.Preparer'"),
        $"SOC-022 T03 : en un geste -- le bouton « Signaler un défaut » de l'en-tete, ou {EcranCorrespondances.ToucheSignalement} de partout, " +
        $"inscrite a la fiche « {EcranCorrespondances.NomEcran} » (ligne {ligneFiche?.Numero}, « {ligneFiche?.Statut} ») ; vitest joue la boite");

    // =================================================================================================================
    // SOC-026 CA06 — l'aide de la premiere ouverture, « vue » d'un serveur RDS a l'autre
    // =================================================================================================================
    Console.WriteLine();
    Console.WriteLine("--- SOC-026 CA06 : l'aide « Premiers pas » a la premiere ouverture");
    SurLePoste("rds-a");
    var ouvertureA = await PreferencesServeur.OuvrirAsync(agent, Greffe, "RDS-A", "1:poste-a");
    var depotA = PreferencesServeur.Actif!;
    var aOuvrirA = EcranAide.AOuvrirAuDemarrage();
    var premiere = EcranAide.Ouvrir();
    var apres = EcranAide.AOuvrirAuDemarrage();
    var rouverte = EcranAide.Ouvrir();
    await depotA.ViderAsync();
    var idAgent = await Valeur<int>(v3.Connexion, "SELECT Id FROM soc.Compte WHERE CompteWindows = @c;", ("@c", Agent1));
    var enBase = await Valeur<string?>(v3.Connexion, "SELECT Valeur FROM soc.Preference WHERE IdCompte = @id AND Nom = 'AideVue';", ("@id", idAgent));
    Exiger(ouvertureA.Panne is null && aOuvrirA == EcranAide.Code && premiere.PremiereFois && apres is null && !rouverte.PremiereFois && enBase == "true"
           && depotA.DernierEchec is null,
        $"SOC-026 CA06 : premiere ouverture sur RDS-A : la V3 ouvre l'aide d'elle-meme ({aOuvrirA}), montree une fois (puis {apres ?? "rien"}) ; " +
        $"« vue » part au serveur (soc.Preference AideVue = {enBase})");
    SurLePoste("rds-b");
    var ouvertureB = await PreferencesServeur.OuvrirAsync(agent, Greffe, "RDS-B", "2:poste-b");
    var aOuvrirB = EcranAide.AOuvrirAuDemarrage();
    Exiger(ouvertureB.Panne is null && ouvertureB.Reprises >= 1 && aOuvrirB is null,
        $"SOC-026 CA06 : le meme agent sur RDS-B (autre fichier de reglages) : « vue » est reprise du serveur ({ouvertureB.Reprises} preference(s)), l'aide ne se rouvre pas seule");
    SurLePoste("rds-c");
    var ouvertureC = await PreferencesServeur.OuvrirAsync(Client(Agent2), Greffe, "RDS-B", "3:poste-c");
    Exiger(ouvertureC.Panne is null && EcranAide.AOuvrirAuDemarrage() == EcranAide.Code,
        "SOC-026 CA06 (jumeau) : un autre agent, qui ne l'a pas vue, a l'aide a sa premiere ouverture : « vue » est a l'agent, pas au poste");
    PreferencesServeur.Fermer();

    // Ce qu'elle montre : les gestes principaux, chacun tire des regles de la V3, et l'acces a la fiche des touches.
    var vueAide = EcranAide.Vue(premiereFois: true);
    GesteAide? Geste(string debut) => vueAide.Gestes.FirstOrDefault(g => g.Titre.StartsWith(debut, StringComparison.Ordinal));
    var apercu = Lire(Path.Combine("web", "src", "plan", "Apercu.tsx"));
    var clavier = Lire(Path.Combine("web", "src", "plan", "clavier.ts"));
    Exiger(Geste("Trouver un écran : le menu, geste › verbe") is { } g1 && MenuAccueil.Moments.All(m => g1.Comment.Contains(m.Titre))
           && Geste("Lancer une recherche")?.Touche == "Entrée, F5 ou F7" && EcranCorrespondances.TouchesDeRecherche.SetEquals(["Entree", "F5", "F7"])
           && Geste("Rechercher partout")?.Touche == "Ctrl K" && page.Contains("e.key.toLowerCase() !== 'k'")
           && Geste("Ouvrir une pièce dans sa propre fenêtre") is { } g4 && g4.Comment.Contains("« Détacher »") && apercu.Contains("aria-label=\"Detacher\"")
           && Geste("Fermer")?.Touche == "Échap" && clavier.Contains("e.key !== 'Escape'") && Geste("Voir les touches")?.Touche == "?"
           && Geste("Signaler un défaut")?.Touche == EcranCorrespondances.ToucheSignalement,
        $"SOC-026 CA06 : l'aide montre les gestes principaux, chacun tenu par le code de la V3 : {string.Join(" ; ", vueAide.Gestes.Select(g => $"{g.Titre} [{g.Touche ?? "souris"}]"))}");
    Exiger(vueAide.EcranTouches == EcranCorrespondances.Code && MenuAccueil.VerbeDe(EcranCorrespondances.Code) is not null
           && aiguillage.Contains("<EcranAide ouvrir={(code) => plan('ouvrirCode', code)} />"),
        $"SOC-026 CA06 : l'aide ouvre la fiche « {vueAide.NomEcranTouches} » (SOC-027), par le geste « ouvrirCode »");
    Exiger(vueAide.CheminMenu == "Configurer › Prendre en main la V3 › Premiers pas" && CatalogueEcrans.Nom(EcranAide.Code) == EcranAide.NomEcran
           && aiguillage.Contains("case 'PROC_PREMIERS_PAS':") && page.Contains("appeler<string | null>('EcranAide.AOuvrirAuDemarrage')")
           && page.Contains("gestePlan('ouvrirCode', code)") && Lire(Path.Combine("web", "src", "ecrans", "EcranAide.tsx")).Contains("Sans origine RIG v2"),
        $"SOC-026 T04 : l'aide s'ouvre d'elle-meme a la premiere ouverture (Application.tsx) et se rouvre depuis le menu ({vueAide.CheminMenu})");

    Reste("SOC-019 CA04 : le vrai certificat (organisme accredite, audit) n'existe pas encore : la V3 dit « certification en cours » ; Amitel l'enregistrera a sa delivrance.");
    Reste("SOC-022 T05 : le signalement essaye par un agent de chaque greffe beta (Le Puy, Chartres, Vienne) se fait chez eux, pendant la formation, pas depuis ce poste.");
    Reste("SOC-026 T05 : l'aide affichee a la premiere ouverture sur les vrais serveurs RDS des greffes : chez Amitel, a l'installation de la V3.");

    // Le serveur du banc s'arrete avant d'etre detruit (decision du lot 1 de la phase 2, controle « Arret » de SocleCourrielsCheck).
    await app.StopAsync();
}
finally
{
    PreferencesServeur.Fermer();
    Environment.SetEnvironmentVariable("RIGV3_ETAT", null);
    try { temp.Delete(recursive: true); } catch (IOException) { }
}

Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "SocleBetaCheck : vert." : $"SocleBetaCheck : {echecs.Count} echec(s).");
return echecs.Count == 0 ? 0 : 1;
