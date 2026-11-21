using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Serveur;
using RigV3.Serveur.Socle.Greffes;
using RigV3Desktop.Pont;

// Banc du CHOIX DU GREFFE A L'OUVERTURE DE RIG (lot 10 du plan de reconstruction, 2026-11-21).
//
// Source V2 relue avant de coder : D:\RigApplication\Source\Outils\DLL\RigFormsMetier\RigFormLogin.cs,
// Login :282-319 (greffe du lancement, ses deux refus :307-316, choix d'office du seul greffe :289-294) et
// _ChargerListeCodeGreffe :158-241 (greffes du poste, compte absent ou desactive ecarte :183-185) ;
// AmiSystem\ListeParametre.cs:153-262 (lecture de « /g3801 », « greffe= », « CodeGreffe= »).
//
// Sur le vrai serveur (Kestrel sur 127.0.0.1, en developpement pour jouer plusieurs comptes), une base
// commune jetable dont le catalogue porte deux greffes jetables A et B, un greffe muet et un code absent,
// et le vrai client de l'ecran : Pont/SessionAgent.cs, puis un ecran de greffe (Pont/EcranCompteurs.cs)
// appele comme l'application l'appelle (ClientServeur.Actif). Il lui faut « docker compose up -d ».

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var migrations = Path.Combine(racine, "Base", "migrations");

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

// Le vrai serveur ; ses journaux d'information (une ligne par requete) noieraient le banc.
static void Silence(WebApplicationBuilder s) => s.Logging.SetMinimumLevel(LogLevel.Warning);

// --- (1) L'argument de lancement, lu comme la V2 (ListeParametre) -----------------------------------------
string?[] lus =
[
    SessionAgent.GreffeDeLancement(["/g3801"]), SessionAgent.GreffeDeLancement(["/g", "3801"]),
    SessionAgent.GreffeDeLancement(["greffe=3801"]), SessionAgent.GreffeDeLancement(["CodeGreffe=3801"]),
    SessionAgent.GreffeDeLancement(["codegreffe=3801"]), SessionAgent.GreffeDeLancement(["utilisateur=jdupont;/g3801"]),
];
Exiger(lus.All(g => g == "3801"),
    $"Lot 10 (1) : le greffe du lancement se lit comme la V2 -- « /g3801 », « /g 3801 », « greffe= », « CodeGreffe= » (sans majuscules), " +
    $"parmi d'autres parametres separes par « ; » ({string.Join(", ", lus)})");
Exiger(SessionAgent.GreffeDeLancement(["CodeGreffe=1111;greffe=2222"]) == "2222",
    "Lot 10 (1) : « greffe » passe avant « CodeGreffe », comme RigFormLogin.cs:284-286");
string?[] sansGreffe =
[
    SessionAgent.GreffeDeLancement([]), SessionAgent.GreffeDeLancement(["utilisateur=jdupont"]),
    SessionAgent.GreffeDeLancement(["greffe="]), SessionAgent.GreffeDeLancement(["/G3801"]),
];
Exiger(sansGreffe.All(g => g is null),
    "Lot 10 (1) (jumeau) : sans greffe au lancement -- rien, un autre parametre, « greffe= » vide, « /G » (la V2 ne lit que « /g ») -- aucun greffe n'est impose");
Exiger(SessionAgent.CodesDuPoste("3801; 8002,3801  9201").SequenceEqual(["3801", "8002", "9201"]) && SessionAgent.CodesDuPoste(null).Count == 0,
    $"Lot 10 (2) : la declaration du poste ({SessionAgent.VariableGreffesDuPoste}) se lit dans son ordre, sans doublon ; absente, elle est vide");

const string GreffeA = "9301", GreffeB = "9302", GreffeMuet = "9303", GreffeAbsent = "9399";
const string NomA = "Greffe de Grenoble (banc)", NomB = "Greffe de Vienne (banc)";
string[] nomsBases;
await using (var commune = await BaseJetable.CreerAsync("OuvertureGreffeCheck"))
await using (var baseA = await BaseJetable.CreerAsync("OuvertureGreffeA"))
await using (var baseB = await BaseJetable.CreerAsync("OuvertureGreffeB"))
{
    nomsBases = [commune.Nom, baseA.Nom, baseB.Nom];
    foreach (var cs in new[] { commune.Connexion, baseA.Connexion, baseB.Connexion })
        await Migrateur.AppliquerAsync(cs, migrations);

    async Task Executer(string connexion, string sql, params (string Nom, object Valeur)[] parametres)
    {
        await using var cn = new SqlConnection(connexion);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var (nom, valeur) in parametres) cmd.Parameters.AddWithValue(nom, valeur);
        await cmd.ExecuteNonQueryAsync();
    }
    async Task<long> Lignes(string connexion)
    {
        await using var cn = new SqlConnection(connexion);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("""
            DECLARE @sql nvarchar(max) = N'SELECT CAST(0 AS bigint)';
            SELECT @sql += N' + (SELECT COUNT_BIG(*) FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N')'
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id;
            EXEC sp_executesql @sql;
            """, cn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    // Les comptes de chaque greffe, dans SA base. AGENT.AB est dans les deux (noms differents : on sait
    // quelle base l'a reconnu) ; DESACT.AB est desactive dans A seulement ; les autres n'ont qu'un greffe.
    // « GestionDesHabilitations » (SOC-002) ouvre l'ecran « Comptes des agents ».
    async Task Compte(string cs, string greffe, string compte, string nom, bool actif, params string[] roles)
    {
        await Executer(cs, """
            INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
            SELECT @compte, @nom, 'Agent', LEFT(@compte, 16), 'M.', @greffe, s.Id, @desactive FROM soc.Service s WHERE s.CodeGreffe = @greffe;
            """, ("@compte", compte), ("@nom", nom), ("@greffe", greffe), ("@desactive", !actif));
        foreach (var role in roles)
            await Executer(cs, "INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, @role FROM soc.Compte WHERE CompteWindows = @compte;",
                ("@role", role), ("@compte", compte));
    }
    foreach (var (cs, greffe) in new[] { (baseA.Connexion, GreffeA), (baseB.Connexion, GreffeB) })
        await Executer(cs, """
            INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES (@greffe, 'GRF', N'Service du greffe');
            INSERT INTO soc.Role (Nom, Libelle) VALUES ('GREFFIER', 'Greffier');
            """, ("@greffe", greffe));
    await Compte(baseA.Connexion, GreffeA, "AGENT.AB", "Grenoble", true, "GREFFIER", "GestionDesHabilitations");
    await Compte(baseA.Connexion, GreffeA, "AGENT.A", "Seul", true, "GREFFIER");
    await Compte(baseA.Connexion, GreffeA, "DESACT.AB", "Desactive", false, "GREFFIER");
    await Compte(baseA.Connexion, GreffeA, "DESACT_SANSROLE.A", "Desactive", false);
    await Compte(baseA.Connexion, GreffeA, "SANSROLE.A", "Sansrole", true);
    await Compte(baseA.Connexion, GreffeA, "COLLEGUE.A", "Collegue de Grenoble", true, "GREFFIER");
    await Compte(baseB.Connexion, GreffeB, "AGENT.AB", "Vienne", true, "GREFFIER", "GestionDesHabilitations");
    await Compte(baseB.Connexion, GreffeB, "DESACT.AB", "Actif a Vienne", true, "GREFFIER");
    await Compte(baseB.Connexion, GreffeB, "COLLEGUE.B", "Collegue de Vienne", true, "GREFFIER");

    // Le catalogue de la base commune : A, B, un greffe dont la base n'existe pas ; 9399 n'y est pas.
    var serveurBancs = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
    var authentificationGreffes = new SqlConnectionStringBuilder(serveurBancs.ConnectionString);
    authentificationGreffes.Remove("Data Source");             // le serveur et la base viennent du catalogue
    var baseMuette = $"{BaseJetable.Prefixe}inexistante_{Guid.NewGuid():N}";   // jamais creee
    foreach (var (code, nom, nomBase) in new[] { (GreffeA, NomA, baseA.Nom), (GreffeB, NomB, baseB.Nom), (GreffeMuet, "Greffe muet (banc)", baseMuette) })
        await Executer(commune.Connexion, """
            INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe)
            VALUES (@c, @n, @s, @v2, @v3, 0, 0, 'Romance Standard Time', 'banc', SYSDATETIME());
            """, ("@c", code), ("@n", nom), ("@s", serveurBancs.DataSource), ("@v2", "RIG_BANC_" + code), ("@v3", nomBase));

    // --- Le serveur, en developpement (changement de compte permis), sur une vraie connexion ----------------
    await using var app = ServeurRig.Construire(["--environment", "Development", "--urls", "http://127.0.0.1:0"], s =>
    {
        Silence(s);
        s.Configuration[$"ConnectionStrings:{BaseRig.NomConnexion}"] = commune.Connexion;
        s.Configuration[CatalogueGreffes.CleAuthentification] = authentificationGreffes.ConnectionString;
    });
    await app.StartAsync();
    var adresse = new Uri(app.Urls.First());

    // Le client de l'ecran pour un compte donne (RIGV3_COMPTE, lu a sa construction).
    ClientServeur Client(string compte)
    {
        Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, compte);
        try { return new ClientServeur(adresse); }
        finally { Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null); }
    }
    static string Etats(ChoixDuGreffe c) => string.Join(", ", c.Greffes.Select(g => $"{g.Code}={g.Etat}{(g.Nom is null ? "" : $" « {g.Nom} »")}"));

    // --- (2) Le serveur filtre les greffes du poste comme la V2 ---------------------------------------------
    var avant = (await Lignes(commune.Connexion), await Lignes(baseA.Connexion), await Lignes(baseB.Connexion));
    var agentAB = Client("agent.ab");
    var tous = await agentAB.LireAsync<ChoixDuGreffe>(RoutesOuverture.Pour([GreffeA, GreffeB, GreffeMuet, GreffeAbsent]));
    Exiger(tous.Compte == "agent.ab"
           && tous.Greffes.Select(g => (g.Code, g.Etat)).SequenceEqual([(GreffeA, EtatsGreffeDuPoste.Ouvrable), (GreffeB, EtatsGreffeDuPoste.Ouvrable),
               (GreffeMuet, EtatsGreffeDuPoste.BaseInjoignable), (GreffeAbsent, EtatsGreffeDuPoste.HorsCatalogue)])
           && tous.Greffes[0].Nom == NomA && tous.Greffes[1].Nom == NomB,
        $"Lot 10 (2) : pour chaque greffe du poste, dans son ordre, le serveur dit s'il s'ouvre (nom du catalogue), si sa base ne repond pas " +
        $"ou s'il n'est pas au catalogue ({Etats(tous)})");
    var seulA = await Client("agent.a").LireAsync<ChoixDuGreffe>(RoutesOuverture.Pour([GreffeA, GreffeB]));
    var desactiveA = await Client("desact.ab").LireAsync<ChoixDuGreffe>(RoutesOuverture.Pour([GreffeA, GreffeB]));
    Exiger(seulA.Greffes.Select(g => g.Etat).SequenceEqual([EtatsGreffeDuPoste.Ouvrable, EtatsGreffeDuPoste.CompteAbsent])
           && seulA.Greffes[1].Nom is null
           && desactiveA.Greffes.Select(g => g.Etat).SequenceEqual([EtatsGreffeDuPoste.CompteDesactive, EtatsGreffeDuPoste.Ouvrable])
           && desactiveA.Greffes[0].Nom is null,
        $"Lot 10 (2) : comme la V2 (RigFormLogin.cs:183-185), un greffe ou le compte n'est pas defini, ou desactive, n'est pas propose -- et ne " +
        $"donne pas son nom ({Etats(seulA)} ; {Etats(desactiveA)})");
    var desactiveSansRole = await Client("desact_sansrole.a").LireAsync<ChoixDuGreffe>(RoutesOuverture.Pour([GreffeA]));
    var sansRole = await Client("sansrole.a").LireAsync<ChoixDuGreffe>(RoutesOuverture.Pour([GreffeA]));
    Exiger(desactiveSansRole.Greffes[0].Etat == EtatsGreffeDuPoste.CompteDesactive && sansRole.Greffes[0].Etat == EtatsGreffeDuPoste.Ouvrable,
        $"Lot 10 (2) : le filtre est celui de la liste de la V2, pas le jugement d'ouverture -- desactive ET sans role : ecarte " +
        $"({desactiveSansRole.Greffes[0].Etat}) ; actif sans role : propose, le refus vient apres le choix ({sansRole.Greffes[0].Etat})");
    var apres = (await Lignes(commune.Connexion), await Lignes(baseA.Connexion), await Lignes(baseB.Connexion));
    Exiger(avant == apres, $"Lot 10 (2) : la liste des greffes du poste n'ecrit rien, ni dans la base commune ni dans celles des greffes ({avant} -> {apres})");

    using (var brut = new HttpClient { BaseAddress = adresse })
    {
        using var anonyme = await brut.GetAsync(RoutesOuverture.Pour([GreffeA]).TrimStart('/'));
        brut.DefaultRequestHeaders.Add(RoutesIdentite.EnTeteCompte, "agent.ab");
        using var vide = await brut.GetAsync(RoutesOuverture.GreffesDuPoste.TrimStart('/'));
        using var trop = await brut.GetAsync(RoutesOuverture.Pour(Enumerable.Range(1, RoutesOuverture.CodesAuPlus + 1).Select(i => $"{i:0000}")).TrimStart('/'));
        using var auPlus = await brut.GetAsync(RoutesOuverture.Pour(Enumerable.Range(1, RoutesOuverture.CodesAuPlus).Select(i => $"{i:0000}")).TrimStart('/'));
        Exiger(anonyme.StatusCode == HttpStatusCode.Unauthorized && vide.StatusCode == HttpStatusCode.BadRequest
               && trop.StatusCode == HttpStatusCode.BadRequest && auPlus.StatusCode == HttpStatusCode.OK,
            $"Lot 10 (2) : sans session Windows, 401 ; sans code ou au-dela de {RoutesOuverture.CodesAuPlus} codes, 400 ; " +
            $"{RoutesOuverture.CodesAuPlus} codes passent ({(int)anonyme.StatusCode}, {(int)vide.StatusCode}, {(int)trop.StatusCode}, {(int)auPlus.StatusCode})");
    }

    // --- (1) Un greffe passe au lancement s'ouvre directement, ou est refuse avec les messages de la V2 ------
    var lancementA = await SessionAgent.PreparerAsync(() => agentAB, ["/g" + GreffeA], $"{GreffeA};{GreffeB}");
    Exiger(lancementA.Identite is { Admis: true, Agent.Nom: "Grenoble" } && lancementA.AChoisir.Length == 0
           && SessionAgent.CodeGreffe == GreffeA && SessionAgent.Greffe?.Libelle == $"{NomA} ({GreffeA})" && SessionAgent.NomAgent == "Agent Grenoble",
        $"Lot 10 (1) : lance avec /g{GreffeA}, RIG ouvre directement ce greffe ; l'agent est juge dans SA base (nom « {SessionAgent.NomAgent} »), " +
        $"la session garde le greffe (« {SessionAgent.Greffe?.Libelle} »)");
    var lancementB = await SessionAgent.PreparerAsync(() => agentAB, ["greffe=" + GreffeB], $"{GreffeA};{GreffeB}");
    Exiger(lancementB.Identite is { Admis: true, Agent.Nom: "Vienne" } && SessionAgent.CodeGreffe == GreffeB,
        $"Lot 10 (1) (jumeau) : lance avec greffe={GreffeB}, le meme agent ouvre B, reconnu dans la base de B (« {lancementB.Identite?.Agent?.Nom} »)");
    var absent = await SessionAgent.PreparerAsync(() => agentAB, ["/g" + GreffeAbsent], $"{GreffeA};{GreffeAbsent}");
    var absentGreffe = SessionAgent.Greffe;
    var muet = await SessionAgent.PreparerAsync(() => agentAB, ["/g" + GreffeMuet], $"{GreffeA};{GreffeMuet}");
    Exiger(absent.Identite is { Admis: false, Statut: SessionAgent.StatutGreffeInexistant, Agent: null }
           && absent.Identite.Message == $"la base de données du greffe '{GreffeAbsent}' n'existe pas." && absentGreffe is null
           && muet.Identite is { Statut: SessionAgent.StatutGreffeInexistant } && muet.Identite.Message == $"la base de données du greffe '{GreffeMuet}' n'existe pas."
           && SessionAgent.Greffe is null && SessionAgent.NomAgent is null,
        $"Lot 10 (1) : un greffe du lancement absent du catalogue, ou dont la base ne s'ouvre pas, est refuse avec le message de la V2 " +
        $"(RigFormLogin.cs:307-310) ; rien n'est ouvert ({absent.Identite?.Message} / {muet.Identite?.Message})");
    var horsPoste = await SessionAgent.PreparerAsync(() => agentAB, ["/g" + GreffeB], GreffeA);
    Exiger(horsPoste.Identite is { Admis: false, Statut: SessionAgent.StatutGreffeHorsPoste, Compte: "agent.ab" }
           && horsPoste.Identite.Message == $"Impossible de se connecter à la base de données du greffe '{GreffeB}' depuis ce serveur."
           && SessionAgent.Greffe is null && SessionAgent.NomAgent is null,
        $"Lot 10 (1) : un greffe du lancement que ce poste ne declare pas est refuse avec le message de la V2 (RigFormLogin.cs:311-316), " +
        $"meme si l'agent y a un compte ({horsPoste.Identite?.Message})");
    var ordreV2 = await SessionAgent.PreparerAsync(() => agentAB, ["/g" + GreffeAbsent], "");
    Exiger(ordreV2.Identite is { Statut: SessionAgent.StatutGreffeInexistant },
        $"Lot 10 (1) : l'ordre de la V2 -- un greffe ni au catalogue ni au poste est refuse pour sa base d'abord ({ordreV2.Identite?.Statut})");
    var inconnuB = await SessionAgent.PreparerAsync(() => Client("agent.a"), ["CodeGreffe=" + GreffeB], $"{GreffeA};{GreffeB}");
    Exiger(inconnuB.Identite is { Admis: false, Statut: StatutsIdentite.Inconnu } && inconnuB.Identite.Message == "L'utilisateur 'agent.a' n'a pas été défini dans RIG."
           && SessionAgent.Greffe is null,
        $"Lot 10 (3) : le greffe ouvert, l'agent y passe les controles de la V2 -- connu de A seulement, il est inconnu de B ({inconnuB.Identite?.Message})");

    // --- (2) Sans greffe au lancement : les greffes du poste ------------------------------------------------
    var choix = await SessionAgent.PreparerAsync(() => agentAB, [], $"{GreffeA};{GreffeB};{GreffeMuet}");
    Exiger(choix is { Identite: null, Demonstration: false, Compte: "agent.ab" }
           && choix.AChoisir.Select(g => (g.Code, g.Nom)).SequenceEqual([(GreffeA, NomA), (GreffeB, NomB)])
           && choix.NonProposes.Select(g => (g.Code, g.Etat)).SequenceEqual([(GreffeMuet, EtatsGreffeDuPoste.BaseInjoignable)])
           && SessionAgent.Identite is null && SessionAgent.Greffe is null,
        $"Lot 10 (2) : plusieurs greffes du poste connaissent l'agent -> RIG les propose a l'ecran de choix ({string.Join(", ", choix.AChoisir.Select(g => g.Code))} ; " +
        $"non propose : {string.Join(", ", choix.NonProposes.Select(g => $"{g.Code} {SessionAgent.Raison(g)}"))}) ; rien n'est ouvert avant le choix");
    var choisiB = await SessionAgent.OuvrirAsync(agentAB, choix.AChoisir[1]);
    Exiger(choisiB is { Admis: true, Agent.Nom: "Vienne" } && SessionAgent.CodeGreffe == GreffeB && SessionAgent.Greffe?.Libelle == $"{NomB} ({GreffeB})",
        $"Lot 10 (3) : le greffe choisi s'ouvre -- l'agent est juge dans la base de B (« {choisiB.Agent?.Nom} »), la session garde « {SessionAgent.Greffe?.Libelle} »");
    var dOffice = await SessionAgent.PreparerAsync(() => Client("agent.a"), [], $"{GreffeA};{GreffeB}");
    var dOfficeB = await SessionAgent.PreparerAsync(() => Client("desact.ab"), [], $"{GreffeA};{GreffeB}");
    Exiger(dOffice is { Identite: { Admis: true, Agent.Nom: "Seul" }, AChoisir.Length: 0 }
           && dOfficeB is { Identite: { Admis: true, Agent.Nom: "Actif a Vienne" }, AChoisir.Length: 0 } && SessionAgent.CodeGreffe == GreffeB,
        $"Lot 10 (2) : un seul greffe du poste s'ouvre pour l'agent -> choisi d'office, sans ecran de choix (V2 :289-294) : A pour agent.a, " +
        $"B pour un compte desactive dans A ({dOffice.Identite?.Agent?.Nom}, {dOfficeB.Identite?.Agent?.Nom})");
    var sansRoleOuvert = await SessionAgent.PreparerAsync(() => Client("sansrole.a"), [], GreffeA);
    Exiger(sansRoleOuvert.Identite is { Admis: false, Statut: StatutsIdentite.SansDroit } && SessionAgent.Greffe is null,
        $"Lot 10 (3) : propose puis choisi d'office, un compte sans role est refuse a l'ouverture, comme CheckUser apres la connexion de la V2 ({sansRoleOuvert.Identite?.Message})");
    var aucunDeclare = await SessionAgent.PreparerAsync(() => agentAB, [], "  ");
    var aucunOuvert = await SessionAgent.PreparerAsync(() => Client("agent.a"), [], $"{GreffeB};{GreffeAbsent}");
    Exiger(aucunDeclare.Identite is { Admis: false, Statut: SessionAgent.StatutAucunGreffe } && aucunDeclare.Identite.Message!.Contains(SessionAgent.VariableGreffesDuPoste)
           && aucunOuvert.Identite is { Admis: false, Statut: SessionAgent.StatutAucunGreffe }
           && aucunOuvert.Identite.Message == $"Aucun des greffes de ce poste ne vous est ouvert : {GreffeB} (votre compte n'y est pas défini), {GreffeAbsent} (absent du catalogue des greffes).",
        $"Lot 10 (2) : aucun greffe declare pour le poste, ou aucun qui s'ouvre pour l'agent -> l'ecran de refus le dit, greffe par greffe " +
        $"({aucunDeclare.Identite?.Message} / {aucunOuvert.Identite?.Message})");

    // --- (3) Le greffe de la session sert aux ecrans : le MEME chemin que l'application -----------------------
    // MainWindow : PreparerAsync(() => ClientServeur.Actif, ...) ; RacineChoixGreffe : OuvrirAsync(ClientServeur.Actif!, choisi).
    Environment.SetEnvironmentVariable(ClientServeur.Variable, adresse.AbsoluteUri);
    Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, "agent.ab");
    var actif = ClientServeur.Actif;
    Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null);
    var appli = await SessionAgent.PreparerAsync(() => ClientServeur.Actif, [], $"{GreffeA};{GreffeB}");
    // Sans greffe ouvert, aucune adresse de greffe : la liste est vide et le controle qui suit le dit.
    async Task<string[]> ComptesDeLaSession()
        => SessionAgent.CodeGreffe is { } greffe
            ? [.. (await ClientServeur.Actif!.LireAsync<VueComptes>(RoutesComptes.Pour(RoutesComptes.Comptes, greffe))).Comptes.Select(c => c.CompteWindows)]
            : [];
    await SessionAgent.OuvrirAsync(ClientServeur.Actif!, appli.AChoisir.Single(g => g.Code == GreffeB));
    var comptesB = await ComptesDeLaSession();
    var compteursB = await EcranCompteurs.Ouvrir();
    await SessionAgent.OuvrirAsync(ClientServeur.Actif!, appli.AChoisir.Single(g => g.Code == GreffeA));
    var comptesA = await ComptesDeLaSession();
    var compteursA = await EcranCompteurs.Ouvrir();
    Exiger(actif is not null && comptesB.Contains("COLLEGUE.B") && !comptesB.Contains("COLLEGUE.A")
           && comptesA.Contains("COLLEGUE.A") && !comptesA.Contains("COLLEGUE.B"),
        $"Lot 10 (3) : « Comptes des agents » (Pont/EcranComptes.cs lit SessionAgent.CodeGreffe) liste les comptes du greffe ouvert -- " +
        $"B : {string.Join(", ", comptesB)} ; A : {string.Join(", ", comptesA)}");
    Exiger(compteursB is { Mode: "serveur", Greffe: GreffeB } && compteursA is { Mode: "serveur", Greffe: GreffeA },
        $"Lot 10 (3) : l'ecran « Compteurs de numéros », appele comme par la page (EcranCompteurs.Ouvrir), lit les compteurs du greffe de la " +
        $"session ({compteursB.Mode} {compteursB.Greffe}, puis {compteursA.Mode} {compteursA.Greffe} ; {compteursB.Message ?? "sans message"})");
    Environment.SetEnvironmentVariable(ClientServeur.Variable, null);
    await app.StopAsync();
}

// --- (4) Sans RIGV3_SERVEUR, rien ne change ; un serveur muet est un refus qui dit la cause -------------------
var demo = await SessionAgent.PreparerAsync(() => null, ["/g" + GreffeA], GreffeA);
var compteursDemo = await EcranCompteurs.OuvrirAsync(null, SessionAgent.CodeGreffe);
Exiger(demo is { Demonstration: true, Identite: null } && SessionAgent.Greffe is null && SessionAgent.NomAgent is null && compteursDemo.Mode == "demo",
    "Lot 10 (4) : sans RIGV3_SERVEUR, rien n'est demande, aucun greffe n'est ouvert, meme avec un greffe au lancement : la demonstration est inchangee");
var panne = await SessionAgent.PreparerAsync(() => new ClientServeur(new Uri("http://127.0.0.1:1/")), [], GreffeA);
Exiger(panne.Identite is { Admis: false, Statut: SessionAgent.StatutServeurInjoignable } && panne.Identite.Message!.Contains("127.0.0.1:1")
       && SessionAgent.Greffe is null,
    $"Lot 10 (4) : un serveur qui ne repond pas est un refus d'ouvrir qui dit la cause, pas un retour discret a la demonstration ({panne.Identite?.Message})");

var restantes = new List<string>();
foreach (var nom in nomsBases) if (await BaseJetable.ExisteAsync(nom)) restantes.Add(nom);
Exiger(restantes.Count == 0, $"bases jetables supprimees : {string.Join(", ", nomsBases)}");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0
    ? "TOUT VERT : RIG choisit son greffe a l'ouverture comme la V2 (lancement, poste, choix d'office, ecran de choix) et la session le garde pour les ecrans."
    : $"{echecs.Count} ECHEC(S).");
return echecs.Count == 0 ? 0 : 1;

// fix-ok: le banc PLANTAIT au lieu de rougir sous la contre-epreuve « identite sans greffe » (ArgumentNullException dans
// RoutesComptes.Pour, CodeGreffe nul, mesure le 21/11/2026) : ComptesDeLaSession rend une liste vide, le controle le dit.
// Autres retouches : using inutile retire, lignes V2 citees corrigees (:289-294).
