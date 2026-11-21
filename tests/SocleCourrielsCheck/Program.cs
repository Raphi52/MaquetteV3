using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Serveur;
using RigV3.Serveur.Socle.Courriels;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;
using RigV3Desktop.Pont;
using SocleCourrielsCheck;

// Banc du LOT 1 du plan « Reconstruction de RIG -- phase 2 » (2026-11-21) : SOC-006, envoyer les courriels du greffe
// avec ses modeles et en garder la trace. Un controle par critere du carnet (C:\Users\raphael.vilain\Documents\Autowin\
// refonte-rig\carnet\socle.json), qui cite son id ; tout refus a son jumeau accepte.
//
// OU IL ECRIT : le SQL Server LOCAL seulement (docker compose up -d) -- ses bases jetables (BaseJetable) : la base
// commune (catalogue), la base V3 du greffe A, une seconde base V3 pour la reprise d'un schema V2 « versionne », et une
// fausse base V2 du greffe A (INFO_GREFFE, PARAM_EDITIONS, modeles) ; un dossier temporaire (logo, habillage en fichier,
// boite de depot) ; tout est supprime en sortie.
// OU IL LIT : RIG_DEV sur SQL-DEV\DEV, par LectureV2 (un SELECT seul), pour la reprise des 40 modeles (CA01).
// AUCUN VRAI COURRIEL : le serveur de messagerie est celui du banc (ServeurSmtpJetable, 127.0.0.1) ou la boite de depot.
// fix-ok: relecture avant le premier lancement -- connexion de LectureValeursGreffe laissee ouverte ; « localhost » peut
// viser ::1 alors que le serveur jetable n'ecoute que 127.0.0.1 ; « n° » sort « n&#176; » de WebUtility.HtmlEncode
// (codes 160-255 encodes, comme GreffeMailBuilder de la V2) ; le cas « habillage vide en base » passait par deux ecritures.
// fix-ok: reception refusee du lot 1 (chef, 2026-11-21) -- aucun controle ne jouait l'ecran Messagerie, dont le .tsx rendait des
// modeles inventes (vitest 8/8 rouge) : ajout des listes du banc, de CA05 par le nom du modele et de la partie « Messagerie ».
// LOT 15 (2026-11-21) : la partie « SOC-006 T04 (suite) » joue la gestion des modeles et des listes (fonction GESTEMAIL, regles de
// saisie de la V2, modele reserve a Amitel, journal des gestes) par les routes, puis par l'ecran.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var migrations = Path.Combine(racine, "Base", "migrations");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
const string GreffeA = "3801";
const string GreffeMuet = "9001";
const string GreffeAbsent = "9002";
const string Agent = @"AMITEL\banc.courriel";       // admis, fonction MAIL_LOT1, agent du greffe (pas Amitel)
const string AgentAmitel = @"AMITEL\banc.amitel";   // admis, fonction MAIL_LOT1, compte d'Amitel
const string SansDroit = @"AMITEL\banc.lecteur";    // admis, sans MAIL_LOT1

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

// --- La source V2 de la reprise : RIG_DEV ou RIG_RECETTE, en lecture seule, et rien d'autre ---------------
var sourceV2 = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("RIGV3_V2_COURRIELS")
    ?? @"Server=SQL-DEV\DEV;Database=RIG_DEV;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15")
{
    ApplicationIntent = ApplicationIntent.ReadOnly,
};
if (sourceV2.InitialCatalog is not ("RIG_DEV" or "RIG_RECETTE"))
{
    Exiger(false, $"source V2 refusee : {sourceV2.InitialCatalog} (le banc ne lit que RIG_DEV ou RIG_RECETTE)");
    return 1;
}

var dossier = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "SocleCourrielsCheck_" + Guid.NewGuid().ToString("N")[..8])).FullName;
var depot = Path.Combine(dossier, "depot");
var editions = Path.Combine(dossier, "editions");
var logo = Path.Combine(editions, GreffeA, "Images", "logo.png");
Directory.CreateDirectory(Path.GetDirectoryName(logo)!);
await File.WriteAllBytesAsync(logo, Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
var habillageFichier = Path.Combine(dossier, "template-shell.txt");
// Le journal du serveur : lu par les controles, et apres l'arret du serveur (fin du fichier).
var capteur = new CapteurJournal();

try
{
    await using var commune = await BaseJetable.CreerAsync("SocleCourrielsCheck");
    await using var v3a = await BaseJetable.CreerAsync("SCV3A");
    await using var v3b = await BaseJetable.CreerAsync("SCV3B");
    await using var v2a = await BaseJetable.CreerAsync("SCV2A");
    foreach (var b in new[] { commune, v3a, v3b })
        await Migrateur.AppliquerAsync(b.Connexion, migrations);

    async Task Executer(string chaine, string sql, params (string Nom, object? Valeur)[] parametres)
    {
        await using var cn = new SqlConnection(chaine);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var (n, v) in parametres) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
    async Task<object?> Scalaire(string chaine, string sql, params (string Nom, object? Valeur)[] parametres)
    {
        await using var cn = new SqlConnection(chaine);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var (n, v) in parametres) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        var v2 = await cmd.ExecuteScalarAsync();
        return v2 is DBNull ? null : v2;
    }
    async Task<int> Compter(string chaine, string sql, params (string Nom, object? Valeur)[] parametres)
        => Convert.ToInt32(await Scalaire(chaine, sql, parametres));

    // --- Le catalogue commun : le greffe A (sa base V3, sa fausse base V2), un greffe muet (base absente) ---------
    var serveurBancs = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
    var instance = serveurBancs.DataSource;
    var authentification = new SqlConnectionStringBuilder(serveurBancs.ConnectionString);
    authentification.Remove("Data Source");
    foreach (var (code, v2, v3) in new[] { (GreffeA, v2a.Nom, v3a.Nom), (GreffeMuet, "RIG_BANC_MUET", $"{BaseJetable.Prefixe}inexistante_{Guid.NewGuid():N}") })
        await Executer(commune.Connexion,
            "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe) " +
            "VALUES (@c, @n, @s, @v2, @v3, 0, 0, 'Romance Standard Time', 'banc', SYSDATETIME());",
            ("@c", code), ("@n", $"Greffe {code} du banc"), ("@s", instance), ("@v2", v2), ("@v3", v3));
    // Le chemin des editions du greffe (COMMUN_RIG.GREFFE.GRF_PATH_EDITION_LOT1, repris par SOC-003) : le logo y est.
    await Executer(commune.Connexion,
        "INSERT INTO soc.GreffeInfoV2 (CodeGreffe, Colonne, Valeur, Texte, TypeV2, ReprisLe) VALUES (@c, 'GRF_PATH_EDITION_LOT1', @v, NULL, 'varchar(80)', SYSDATETIME());",
        ("@c", GreffeA), ("@v", editions + Path.DirectorySeparatorChar));

    // --- Les agents du greffe A, dans SA base : la politique AgentAdmis les y juge ---------------------------------
    await Executer(v3a.Connexion, """
        INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES (@greffe, 'GREF', N'Greffe');
        INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANC', 'Banc SocleCourriels'), ('BANC_LECTURE', 'Banc : admis sans MAIL_LOT1');
        INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive, EstAmitel)
        SELECT v.Compte, 'BANC', 'Courriels', v.Abrege, 'M.', @greffe, s.Id, 0, v.Amitel
        FROM (VALUES ('banc.courriel', 'BCOU', 0), ('banc.amitel', 'BAMI', 1), ('banc.lecteur', 'BLEC', 0)) v (Compte, Abrege, Amitel)
        CROSS JOIN soc.Service s;
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, CASE WHEN CompteWindows = 'banc.lecteur' THEN 'BANC_LECTURE' ELSE 'BANC' END FROM soc.Compte;
        INSERT INTO soc.Fonction (Code, Libelle, TypeV2) VALUES ('MAIL_LOT1', 'Mails envoyés en Lot 1', 'DLL');
        INSERT INTO soc.RoleFonction (Role, Fonction) VALUES ('BANC', 'MAIL_LOT1');
        INSERT INTO soc.ParametreGreffe (Colonne, Valeur, TypeV2) VALUES ('GRPAR_TPG_NOM_GREFFIER', CAST('Maître Banc, greffier' AS varchar(80)), 'varchar(80)');
        INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('SEND_EMAIL_TEMPLATE_PATH', 'Habillage des courriels', @fichier);
        """, ("@greffe", GreffeA), ("@fichier", habillageFichier));

    // --- La fausse base V2 du greffe A : ce que la V3 y lit encore (INFO_GREFFE, PARAM_EDITIONS), et des modeles
    // --- au schema du projet versionne (16 colonnes, sans les deux de la signature) avec une liste de diffusion.
    await Executer(v2a.Connexion, """
        CREATE TABLE dbo.INFO_GREFFE (CODE_GREFFE varchar(4) NOT NULL, PWD_SQL varchar(16) NULL, PWD_GREFFE varchar(8) NULL,
            ADRESSE_MAIL_RCS varchar(70) NULL, ADRESSE_MAIL_JUDICIAIRE varchar(70) NULL, ADRESSE_MAIL_NO_REPLY varchar(70) NULL,
            ADRESSE_MAIL_ACCUSE_ENVOI_JUD varchar(150) NULL, ADRESSE_MAIL_COMPTABILITE varchar(70) NULL);
        INSERT INTO dbo.INFO_GREFFE VALUES ('3801', 'secret-sql-banc', 'secret', 'rcs@greffe-banc.example', 'judiciaire@greffe-banc.example',
            'ne-pas-repondre@greffe-banc.example', 'accuse-jud@greffe-banc.example', NULL);
        CREATE TABLE dbo.PARAM_EDITIONS (PRMED_VILLE_MAJ varchar(80) NULL, PRMED_VILLE_MIN varchar(80) NULL);
        INSERT INTO dbo.PARAM_EDITIONS VALUES ('GRENOBLE', 'Grenoble');
        CREATE TABLE dbo.EMAIL_MODEL (
            EMAILMOD_TYPE VARCHAR (40) NOT NULL PRIMARY KEY, EMAILMOD_SUJET VARCHAR (200) NOT NULL, EMAILMOD_CORPS VARCHAR (4000) NOT NULL,
            EMAILMOD_CORPS_HTML BIT NOT NULL, EMAILMOD_FROM VARCHAR (200) NULL, EMAILMOD_REPLY VARCHAR (200) NULL, EMAILMOD_COPY_TO VARCHAR (200) NULL,
            EMAILMOD_LOT_1 BIT DEFAULT ((1)) NOT NULL, EMAILMOD_LOT_2 BIT DEFAULT ((1)) NOT NULL, EMAILMOD_ONLY_AMITEL BIT DEFAULT ((0)) NOT NULL,
            EMAILMOD_LIBELLE_ECRAN VARCHAR (100) NULL, EMAILMOD_INSCRIPTION BIT DEFAULT ((0)) NULL, EMAILMOD_UTILISABLE_EN_SAISIE BIT DEFAULT ((1)) NULL,
            EMAILMOD_COMPTA BIT DEFAULT ((0)) NULL, EMAILMOD_DEST_DEFAUT VARCHAR (200) NULL, EMAILMOD_IS_ENVOI_AUTOMATIQUE BIT DEFAULT ((0)) NOT NULL);
        CREATE TABLE dbo.LISTE_DISTRIBUTION_MAIL (LSTDM_ID_LSTDM INT IDENTITY (1, 1) NOT NULL PRIMARY KEY, LSTDM_CODE VARCHAR (35) NOT NULL,
            LSTDM_LIBELLE VARCHAR (200) NOT NULL, LSTDM_ADR_MAILS VARCHAR (7000) NOT NULL, LSTDM_RCS BIT NOT NULL, LSTDM_JUDICIAIRE BIT NOT NULL,
            LSTDM_PRIVE VARCHAR (200) NULL, LSTDM_ACTIF BIT NOT NULL);
        CREATE TABLE dbo.EMAIL_MODEL_LISTE_DISTRIB_DEFAUT (EMLDD_ID_EMLDD INT IDENTITY (1, 1) NOT NULL PRIMARY KEY,
            EMLDD_TYPE_EMAILMOD VARCHAR (40) NOT NULL REFERENCES dbo.EMAIL_MODEL (EMAILMOD_TYPE),
            EMLDD_ID_LSTDM INT NOT NULL REFERENCES dbo.LISTE_DISTRIBUTION_MAIL (LSTDM_ID_LSTDM));
        INSERT INTO dbo.EMAIL_MODEL (EMAILMOD_TYPE, EMAILMOD_SUJET, EMAILMOD_CORPS, EMAILMOD_CORPS_HTML, EMAILMOD_FROM, EMAILMOD_ONLY_AMITEL, EMAILMOD_COMPTA)
        VALUES ('CA', 'Certificat', 'Maître,|Le certificat.', 0, '<[ADRESSE_MAIL_JUDICIAIRE]>', 1, 1);
        SET IDENTITY_INSERT dbo.LISTE_DISTRIBUTION_MAIL ON;
        INSERT INTO dbo.LISTE_DISTRIBUTION_MAIL (LSTDM_ID_LSTDM, LSTDM_CODE, LSTDM_LIBELLE, LSTDM_ADR_MAILS, LSTDM_RCS, LSTDM_JUDICIAIRE, LSTDM_PRIVE, LSTDM_ACTIF)
        VALUES (7, 'MANDATAIRES', 'Mandataires du ressort', 'a@etude.example;b@etude.example', 0, 1, NULL, 1);
        SET IDENTITY_INSERT dbo.LISTE_DISTRIBUTION_MAIL OFF;
        SET IDENTITY_INSERT dbo.EMAIL_MODEL_LISTE_DISTRIB_DEFAUT ON;
        INSERT INTO dbo.EMAIL_MODEL_LISTE_DISTRIB_DEFAUT (EMLDD_ID_EMLDD, EMLDD_TYPE_EMAILMOD, EMLDD_ID_LSTDM) VALUES (3, 'CA', 7);
        SET IDENTITY_INSERT dbo.EMAIL_MODEL_LISTE_DISTRIB_DEFAUT OFF;
        """);

    // --- Le vrai serveur, en memoire ; l'agent se designe par l'en-tete de developpement (SOC-001 CA07) ----------
    await using var smtp = new ServeurSmtpJetable();
    await using var app = ServeurRig.Construire(["--environment", "Development"], s =>
    {
        s.WebHost.UseTestServer();
        s.Logging.SetMinimumLevel(LogLevel.Warning);
        s.Logging.AddProvider(capteur);
        s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
        s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
        s.Configuration["Courriels:Depot"] = depot;
        s.Configuration["Courriels:Remise"] = "smtp";
        s.Configuration["Courriels:Smtp:Hote"] = "127.0.0.1";
        s.Configuration["Courriels:Smtp:Port"] = smtp.Port.ToString();
    });
    await app.StartAsync();
    var http = app.GetTestClient();
    var catalogue = app.Services.GetRequiredService<CatalogueGreffes>();
    var baseRig = app.Services.GetRequiredService<BaseRig>();
    var journaux = app.Services.GetRequiredService<ILoggerFactory>();
    var service = app.Services.GetRequiredService<ServiceCourriels>();
    ServiceCourriels Service(OptionsCourriels o) => new(o, catalogue, baseRig, journaux.CreateLogger<ServiceCourriels>());
    async Task<HttpResponseMessage> Appeler(string adresse, string? compte)
    {
        using var demande = new HttpRequestMessage(HttpMethod.Get, adresse);
        if (compte is not null) demande.Headers.Add(RoutesIdentite.EnTeteCompte, compte);
        return await http.SendAsync(demande);
    }
    async Task<T> Lire<T>(string adresse, string compte = Agent)
    {
        using var r = await Appeler(adresse, compte);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<T>(json))!;
    }
    async Task<List<(string Etape, bool? Succes, string? Erreur)>> Etapes(Guid suivi)
    {
        var liste = new List<(string, bool?, string?)>();
        await using var cn = new SqlConnection(v3a.Connexion);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT Etape, Succes, ErreurMessage FROM soc.TraceEnvoi WHERE Suivi = @s ORDER BY Id;", cn);
        cmd.Parameters.AddWithValue("@s", suivi);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) liste.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetBoolean(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return liste;
    }
    string Suite(IEnumerable<(string Etape, bool? Succes, string? Erreur)> etapes) => string.Join(" > ", etapes.Select(e => e.Etape));

    // =====================================================================================================
    // SOC-006 CA01 — les modeles de la base V2 repris a l'identique (RIG_DEV, puis un schema V2 « versionne »)
    // =====================================================================================================
    Console.WriteLine($"--- CA01 : modeles repris de {sourceV2.InitialCatalog} ({sourceV2.DataSource}), en lecture seule");
    await using (var cnV2 = new SqlConnection(sourceV2.ConnectionString))
    await using (var cnA = new SqlConnection(v3a.Connexion))
    {
        await cnV2.OpenAsync();
        await cnA.OpenAsync();
        var nbModelesV2 = Convert.ToInt32(await LectureV2.ValeurAsync(cnV2, "SELECT COUNT(*) FROM dbo.EMAIL_MODEL", default));
        var nbAmitelV2 = Convert.ToInt32(await LectureV2.ValeurAsync(cnV2, "SELECT COUNT(*) FROM dbo.EMAIL_MODEL WHERE EMAILMOD_ONLY_AMITEL = 1", default));
        var bilan = await RepriseCourriels.ReprendreAsync(cnV2, cnA);
        Exiger(bilan.Modeles == nbModelesV2 && nbModelesV2 > 0,
            $"SOC-006 CA01 : les {nbModelesV2} modeles de {sourceV2.InitialCatalog} sont repris ({bilan.Modeles} dans soc.ModeleMessage, {bilan.Listes} liste(s))");
        var ecarts = await RepriseCourriels.ComparerAsync(cnV2, cnA);
        Exiger(ecarts.Count == 0,
            $"SOC-006 CA01 : la comparaison V2 / V3, colonne par colonne (type, objet, texte, format, expediteur, reponse, copie, destinataire par defaut, cases), trouve 0 ecart ({ecarts.Count}{(ecarts.Count > 0 ? ", ex. " + ecarts[0] : "")})");
        Exiger(await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage WHERE ReserveAmitel = 1") == nbAmitelV2,
            $"SOC-006 CA01 : les {nbAmitelV2} modeles « reserve a Amitel » de la V2 le restent");
        // Contre-epreuve de la comparaison : un texte change d'un caractere en V3 est vu, sur ce modele et cette colonne.
        var unType = (string)(await Scalaire(v3a.Connexion, "SELECT TOP 1 Type FROM soc.ModeleMessage ORDER BY Type"))!;
        await Executer(v3a.Connexion, "UPDATE soc.ModeleMessage SET Corps = Corps + '.' WHERE Type = @t;", ("@t", unType));
        var change = await RepriseCourriels.ComparerAsync(cnV2, cnA);
        Exiger(change.Count == 1 && change[0].Cle == unType && change[0].Colonne == "EMAILMOD_CORPS",
            $"SOC-006 CA01 (contre-epreuve) : un point ajoute au texte de « {unType} » en V3 est trouve ({change.Count} ecart : {string.Join(" ; ", change)})");
        var bilan2 = await RepriseCourriels.ReprendreAsync(cnV2, cnA);
        Exiger(bilan2 == bilan && (await RepriseCourriels.ComparerAsync(cnV2, cnA)).Count == 0,
            "SOC-006 CA01 : la reprise se rejoue (repetition du transfert) et repart de la V2 : 0 ecart");
    }
    await using (var cnV2 = new SqlConnection(v2a.Connexion))
    await using (var cnB = new SqlConnection(v3b.Connexion))
    {
        await cnV2.OpenAsync();
        await cnB.OpenAsync();
        var bilan = await RepriseCourriels.ReprendreAsync(cnV2, cnB);
        var ecarts = await RepriseCourriels.ComparerAsync(cnV2, cnB);
        Exiger(bilan == new BilanRepriseCourriels(1, 1, 1) && ecarts.Count == 0
               && await Compter(v3b.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessageListe WHERE Id = 3 AND TypeModele = 'CA' AND IdListe = 7") == 1
               && await Compter(v3b.Connexion, "SELECT COUNT(*) FROM soc.ListeDiffusion WHERE Id = 7 AND Adresses = 'a@etude.example;b@etude.example'") == 1,
            $"SOC-006 CA01, T05 : une base V2 au schema versionne (sans signature) se reprend, listes de diffusion et liens par defaut gardent leurs identifiants ({bilan}, {ecarts.Count} ecart)");
        Exiger(await Compter(v3b.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage WHERE Type = 'CA' AND SignatureActive = 0 AND Signature IS NULL AND ReserveAmitel = 1 AND Comptabilite = 1") == 1,
            "SOC-006 CA01 (jumeau) : une colonne absente de cette V2 (signature) prend la valeur par defaut de la V3, les cases presentes sont gardees");
    }

    // Les modeles du banc, ajoutes apres la reprise (qui remplace tout) :
    //  BANC_DOC : texte avec | $PieceJointe$ $LOGO$, adresses en etiquettes INFO_GREFFE, copie cachee ;
    //  BANC_SIMPLE : sans copie (le refus du serveur ne touche que son destinataire) ;
    //  BANC_HTML : texte HTML, pour le rendu historique avec logo ; BANC_AMITEL : reserve a Amitel ;
    //  BANC_ETIQUETTE : expediteur sur une etiquette qu'INFO_GREFFE n'a pas.
    await Executer(v3a.Connexion, """
        INSERT INTO soc.ModeleMessage (Type, Sujet, Corps, CorpsHtml, Expediteur, Reponse, Copie, ReserveAmitel, DestinataireDefaut) VALUES
        ('BANC_DOC', 'Votre document', 'Madame, Monsieur,|Voici vos documents :|$PieceJointe$|$LOGO$ Le greffe', 0,
            'Greffe du banc <[ADRESSE_MAIL_JUDICIAIRE]>', '[ADRESSE_MAIL_NO_REPLY]', '[ADRESSE_MAIL_ACCUSE_ENVOI_JUD]', 0, NULL),
        ('BANC_SIMPLE', 'Avis du greffe', 'Bonjour,|Un avis.', 0, '[ADRESSE_MAIL_RCS]', NULL, NULL, 0, NULL),
        ('BANC_HTML', 'Avis HTML', '<p>Bonjour</p>$LOGO$<p>Le greffe</p>', 1, '[ADRESSE_MAIL_RCS]', NULL, NULL, 0, 'defaut@greffe-banc.example'),
        ('BANC_AMITEL', 'Reserve Amitel', 'Interne', 0, '[ADRESSE_MAIL_RCS]', NULL, NULL, 1, NULL),
        ('BANC_ETIQUETTE', 'Sans expediteur', 'Texte', 0, '[ADRESSE_MAIL_COMPTABILITE]', NULL, NULL, 0, NULL);
        """);
    // Les listes de distribution du banc (la reprise de RIG_DEV n'en a aucune) : une publique (« * ») liee par defaut a
    // BANC_DOC, une a l'agent du banc, une a un collegue (liee elle aussi a BANC_DOC : l'agent ne doit pas la voir), une
    // publique inactive. Regle de la V2 : LSTDM_PRIVE = login OR LSTDM_PRIVE = '*' (ListeDistributionMail.cs:63).
    await Executer(v3a.Connexion, """
        INSERT INTO soc.ListeDiffusion (Code, Libelle, Adresses, Rcs, Judiciaire, Prive, Actif) VALUES
        ('BANC_PUBLIQUE', 'Mandataires du banc', 'm1@etude-banc.example;m2@etude-banc.example', 0, 1, '*', 1),
        ('BANC_PERSO', 'Liste de l''agent du banc', 'perso@client-banc.example', 1, 0, 'banc.courriel', 1),
        ('BANC_COLLEGUE', 'Liste d''un collegue', 'collegue@client-banc.example', 1, 1, 'banc.autre', 1),
        ('BANC_INACTIVE', 'Ancienne liste', 'ancien@etude-banc.example', 0, 1, '*', 0);
        INSERT INTO soc.ModeleMessageListe (TypeModele, IdListe)
        SELECT 'BANC_DOC', Id FROM soc.ListeDiffusion WHERE Code IN ('BANC_PUBLIQUE', 'BANC_COLLEGUE');
        """);
    var documents = new DocumentCourriel[]
    {
        new(@"\\ged\editions\3801\kbis-001.pdf", "https://documents.greffe-banc.example/d/0001"),
        new(@"\\ged\editions\3801\statuts-001.pdf", "https://documents.greffe-banc.example/d/0002"),
    };
    var habillageBase = """
        <MailTemplate><Subject>Ignore</Subject><LogoPath></LogoPath><HtmlBody><![CDATA[<html><body><h1>Tribunal de commerce de {{VILLE_TRIBUNAL}}</h1><div class="message">{{MESSAGE_PERSONNALISE}}</div><table>{{LISTE_DOCUMENTS_HTML}}</table><p>Le greffier : {{NOM_GREFFIER}}</p><p>[{{JETON_INCONNU}}]</p><img src="cid:LogoGreffe"/></body></html>]]></HtmlBody></MailTemplate>
        """;
    await File.WriteAllTextAsync(habillageFichier, """
        <MailTemplate><HtmlBody><![CDATA[<html><body><h2>Habillage du fichier, {{VILLE_TRIBUNAL}}</h2>{{MESSAGE_PERSONNALISE}}<table>{{LISTE_DOCUMENTS_HTML}}</table></body></html>]]></HtmlBody></MailTemplate>
        """);
    async Task PoserHabillage(string? contenu)
    {
        await Executer(v3a.Connexion, "DELETE FROM soc.ModeleCourriel;");
        if (contenu is not null)
            await Executer(v3a.Connexion, "INSERT INTO soc.ModeleCourriel (Code, Contenu) VALUES ('SHELL', @c);", ("@c", contenu));
    }
    async Task<CourrielPrepare> Preparer(string modele, string? destinataires = "client@exemple-banc.example", IReadOnlyList<DocumentCourriel>? docs = null)
    {
        await using var cn = new SqlConnection(v3a.Connexion);
        await cn.OpenAsync();
        return await service.PreparerAsync(GreffeA, cn, new DemandeCourriel(modele, destinataires), docs ?? documents, default);
    }

    // =====================================================================================================
    // SOC-006 CA02 — jetons remplaces comme la V2, habillage en base puis fichier
    // =====================================================================================================
    Console.WriteLine("--- CA02 : jetons et habillage");
    await PoserHabillage(habillageBase);
    var habille = await Preparer("BANC_DOC");
    var html = habille.Rendu.VueHtml ?? "";
    Exiger(habille.Rendu.Habille && html.Contains("Tribunal de commerce de GRENOBLE") && html.Contains("Le greffier : Maître Banc, greffier"),
        "SOC-006 CA02 : {{VILLE_TRIBUNAL}} (PARAM_EDITIONS de la base V2) et {{NOM_GREFFIER}} (GREFFE_PARAMETRE repris) sont remplaces");
    Exiger(html.Contains("<p>[]</p>") && !html.Contains("{{"),
        "SOC-006 CA02 : un jeton inconnu de l'habillage devient vide, comme GreffeMailBuilder.ReplaceTokens ; aucun {{…}} ne reste");
    Exiger(html.Contains("Madame, Monsieur,<br/>Voici vos documents :<br/>") && html.Contains(">Lien n&#176;1</a>") && html.Contains(">Lien n&#176;2</a>")
           && html.Contains("https://documents.greffe-banc.example/d/0002") && html.Contains("<table></table>"),
        "SOC-006 CA02 : le texte du modele tombe dans {{MESSAGE_PERSONNALISE}} (« | » en sauts de ligne), ses liens a la place de $PieceJointe$ et pas une seconde fois en bas");
    Exiger(html.Contains("<img src=\"cid:LogoGreffe\" style=\"border:0;\" />") && habille.Rendu.Logo == logo && habille.Rendu.IdLogo == "LogoGreffe",
        $"SOC-006 CA02 : $LOGO$ devient le logo du greffe, embarque sous cid:LogoGreffe ({habille.Rendu.Logo})");
    Exiger(habille.Expediteur == "Greffe du banc <judiciaire@greffe-banc.example>" && habille.Reponse == "ne-pas-repondre@greffe-banc.example"
           && habille.Copie == "accuse-jud@greffe-banc.example" && habille.Destinataires == "client@exemple-banc.example",
        $"SOC-006 CA02 : les etiquettes des adresses prennent la valeur d'INFO_GREFFE, comme SendEmail.filtrerMail (de « {habille.Expediteur} », reponse « {habille.Reponse} », copie « {habille.Copie} »)");
    await using var cnValeurs = await OuvrirV3A();
    var valeurs = await LectureValeursGreffe.LireAsync(GreffeA, cnValeurs, catalogue, baseRig, default);
    Exiger(!valeurs.InfoGreffe.ContainsKey("PWD_SQL") && !valeurs.InfoGreffe.ContainsKey("PWD_GREFFE")
           && LectureValeursGreffe.ResoudreAdresses("[PWD_SQL]", valeurs.InfoGreffe) == ""
           && valeurs.InfoGreffe["ADRESSE_MAIL_RCS"] == "rcs@greffe-banc.example",
        "SOC-006 CA02 : les mots de passe d'INFO_GREFFE ne sont jamais des valeurs de courriel ([PWD_SQL] reste vide), les adresses si");

    // Habillage en BASE prioritaire sur le fichier : le reglage pointe deja le fichier, c'est la base qui a servi.
    Exiger(!html.Contains("Habillage du fichier"), "SOC-006 CA02 : l'habillage en base passe avant le fichier du reglage (MailTemplateStore.cs:22-28)");
    await PoserHabillage(null);
    var duFichier = await Preparer("BANC_DOC");
    Exiger(duFichier.Rendu.Habille && (duFichier.Rendu.VueHtml ?? "").Contains("Habillage du fichier, GRENOBLE"),
        "SOC-006 CA02 : sans habillage en base, celui du fichier designe par le reglage SEND_EMAIL_TEMPLATE_PATH sert (repli)");
    await PoserHabillage("");
    var videEnBase = await Preparer("BANC_DOC");
    Exiger(videEnBase.Rendu.Habille && (videEnBase.Rendu.VueHtml ?? "").Contains("Habillage du fichier"),
        "SOC-006 CA02 (jumeau) : un habillage VIDE en base compte comme absent, le fichier sert (MailTemplateStore : « si non vide »)");
    await PoserHabillage(null);
    await Executer(v3a.Connexion, "UPDATE soc.Reglage SET Valeur = @v, AuteurModif = 'banc', DateModif = GETDATE() WHERE Code = 'SEND_EMAIL_TEMPLATE_PATH';",
        ("@v", Path.Combine(dossier, "absent.txt")));
    var historique = await Preparer("BANC_DOC");
    Exiger(!historique.Rendu.Habille && historique.Rendu.VueHtml is null
           && historique.Rendu.Corps.Contains("Madame, Monsieur,<br/>Voici vos documents :<br/><br/>   <a href='https://documents.greffe-banc.example/d/0001'> Lien n°1 </a>")
           && historique.Rendu.Corps.Contains("$LOGO$ Le greffe"),
        "SOC-006 CA02 : ni habillage en base ni fichier lisible -> le rendu historique de la V2 (liens a la place de $PieceJointe$, « | » en <br/> ; $LOGO$ d'un texte brut reste tel quel, comme la V2)");
    var avecLogoHtml = await Preparer("BANC_HTML", null, []);
    Exiger(avecLogoHtml.Rendu.VueHtml is { } vh && vh.StartsWith("<html><body><p>Bonjour</p><br/><img src=\"cid:logo\" width=\"400\" height=\"100\"/><br/>")
           && avecLogoHtml.Rendu.Corps == "<p>Bonjour</p><p>Le greffe</p>\r\n" && avecLogoHtml.Rendu.IdLogo == "logo"
           && avecLogoHtml.Destinataires == "defaut@greffe-banc.example",
        "SOC-006 CA02 : un texte HTML sans habillage recoit le logo du greffe sous cid:logo (SetLogoInMail, 400 x 100) ; sans destinataire, celui du modele");
    await PoserHabillage("<MailTemplate><HtmlBody>casse");
    capteur.Vider();
    var casse = await Preparer("BANC_DOC");
    Exiger(!casse.Rendu.Habille && casse.Rendu.Corps.Contains("Lien n°1") && capteur.Evenements.Any(e => e.Id == ServiceCourriels.EvenementValeurManquante.Id && e.Message.Contains("habillage illisible")),
        "SOC-006 CA02 (jumeau) : un habillage illisible ne casse pas l'envoi : rendu historique, et la cause part au journal du serveur");
    await PoserHabillage(habillageBase);

    // =====================================================================================================
    // SOC-006 CA03 — une trace a chaque envoi : accepte, refuse, serveur injoignable, preparation impossible
    // =====================================================================================================
    Console.WriteLine($"--- CA03 : trace des envois (serveur de messagerie jetable 127.0.0.1:{smtp.Port})");
    var accepte = await service.EnvoyerAsync(GreffeA, new DemandeCourriel("BANC_DOC", "client@exemple-banc.example", " [ref : D-2026-0042]",
        documents, "MANUEL", "banc SocleCourriels", Agent, IdsLignesEdition: "101,102"));
    var recu = smtp.Recus.LastOrDefault();
    Exiger(accepte.Etat == EtatsCourriel.Envoye && accepte.IdCourriel is not null && recu is not null
           && recu.Pour.Contains("<client@exemple-banc.example>") && recu.Pour.Contains("<accuse-jud@greffe-banc.example>")
           && recu.Donnees.Contains("Content-ID: <LogoGreffe>"),
        $"SOC-006 CA03 : le serveur de messagerie accepte le courriel (destinataire et copie cachee, logo embarque) -- {accepte.Etat}, courriel n° {accepte.IdCourriel}");
    var etapesAccepte = await Etapes(accepte.Suivi);
    Exiger(Suite(etapesAccepte) == "PREPARATION_EMAIL > EMAIL_SEND_DEBUT > SMTP_APPEL > EMAIL_SEND_SUCCES > EMAIL_ENVOYE",
        $"SOC-006 CA03 : chaque etape de l'envoi est tracee, comme la V2 ({Suite(etapesAccepte)})");
    var ligneSucces = await Compter(v3a.Connexion, """
        SELECT COUNT(*) FROM soc.TraceEnvoi WHERE Suivi = @s AND Etape = 'EMAIL_SEND_SUCCES' AND CodeGreffe = @g AND Succes = 1
          AND Expediteur = 'Greffe du banc <judiciaire@greffe-banc.example>' AND Destinataires = 'client@exemple-banc.example'
          AND Copie = 'accuse-jud@greffe-banc.example' AND Sujet = 'Votre document [ref : D-2026-0042]' AND NbPieces = 2
          AND Urls LIKE 'https://documents.greffe-banc.example/d/0001;%' AND SmtpHote = '127.0.0.1' AND SmtpPort = @p
          AND Mode = 'MANUEL' AND UtilisateurWindows = @agent AND IdsLignesEdition = '101,102' AND TypeModele = 'BANC_DOC'
          AND ErreurMessage IS NULL;
        """, ("@s", accepte.Suivi), ("@g", GreffeA), ("@p", smtp.Port), ("@agent", Agent));
    Exiger(ligneSucces == 1,
        "SOC-006 CA03 : la trace porte l'identifiant de suivi, le greffe, l'etape, l'expediteur, les destinataires, l'objet, le nombre de pieces et le succes");
    Exiger(await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.CourrielEnvoye WHERE Id = @id AND Suivi = @s AND Sujet = 'Votre document [ref : D-2026-0042]' AND Corps LIKE 'Madame, Monsieur,|%' AND DateEnvoi IS NOT NULL",
               ("@id", accepte.IdCourriel ?? -1), ("@s", accepte.Suivi)) == 1
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.TraceEnvoi WHERE Suivi = @s AND Etape = 'EMAIL_ENVOYE' AND IdCourriel = @id", ("@s", accepte.Suivi), ("@id", accepte.IdCourriel ?? -1)) == 1,
        "SOC-006 CA03 : le courriel parti est enregistre (soc.CourrielEnvoye, texte du modele) et la trace le cite (EMAIL_ENVOYE)");

    var avantRefus = smtp.Recus.Count;
    var refuse = await service.EnvoyerAsync(GreffeA, new DemandeCourriel("BANC_SIMPLE", "refuse@exemple-banc.example", Mode: "AUTO", Agent: Agent));
    var etapesRefus = await Etapes(refuse.Suivi);
    Exiger(refuse.Etat == EtatsCourriel.Echec && refuse.IdCourriel is null && (refuse.Erreur ?? "").Contains("5.1.1") && smtp.Recus.Count == avantRefus
           && Suite(etapesRefus) == "PREPARATION_EMAIL > EMAIL_SEND_DEBUT > SMTP_APPEL > EMAIL_SEND_ERREUR",
        $"SOC-006 CA03 : le serveur de messagerie REFUSE le destinataire -> echec trace ({Suite(etapesRefus)} : « {refuse.Erreur} »)");
    Exiger(await Compter(v3a.Connexion, """
               SELECT COUNT(*) FROM soc.TraceEnvoi WHERE Suivi = @s AND Etape = 'EMAIL_SEND_ERREUR' AND Succes = 0
                 AND ErreurType LIKE 'System.Net.Mail.SmtpFailedRecipient%' AND ErreurMessage LIKE '%5.1.1%' AND Pile IS NOT NULL
                 AND Destinataires = 'refuse@exemple-banc.example' AND Sujet = 'Avis du greffe' AND CodeGreffe = @g;
               """, ("@s", refuse.Suivi), ("@g", GreffeA)) == 1
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.CourrielEnvoye WHERE Suivi = @s", ("@s", refuse.Suivi)) == 0,
        "SOC-006 CA03 : la trace du refus porte le type et le message de l'erreur ; aucun courriel n'est enregistre comme parti");

    var portFerme = PortLibre();
    var injoignable = await Service(new OptionsCourriels { Remise = "smtp", SmtpHote = "127.0.0.1", SmtpPort = portFerme, Depot = depot })
        .EnvoyerAsync(GreffeA, new DemandeCourriel("BANC_SIMPLE", "client@exemple-banc.example"));
    Exiger(injoignable.Etat == EtatsCourriel.Echec && Suite(await Etapes(injoignable.Suivi)).EndsWith("SMTP_APPEL > EMAIL_SEND_ERREUR"),
        $"SOC-006 CA03 : un serveur de messagerie qui ne repond pas est un echec trace (« {injoignable.Erreur} »)");

    var connexionsAvant = smtp.Connexions;
    var inconnu = await service.EnvoyerAsync(GreffeA, new DemandeCourriel("BANC_INEXISTANT", "client@exemple-banc.example"));
    var sansExpediteur = await service.EnvoyerAsync(GreffeA, new DemandeCourriel("BANC_ETIQUETTE", "client@exemple-banc.example"));
    Exiger(inconnu.Etat == EtatsCourriel.Echec && Suite(await Etapes(inconnu.Suivi)) == "PREPARATION_ERREUR"
           && (inconnu.Erreur ?? "").Contains("BANC_INEXISTANT")
           && sansExpediteur.Etat == EtatsCourriel.Echec && (sansExpediteur.Erreur ?? "").Contains("ADRESSE_MAIL_COMPTABILITE")
           && smtp.Connexions == connexionsAvant,
        $"SOC-006 CA03 : modele inconnu ou etiquette d'adresse sans valeur -> rien n'est remis, la trace dit pourquoi (« {sansExpediteur.Erreur} »)");

    // Une trace qui ne s'ecrit pas n'arrete pas l'envoi (EmailEnvoiTrace.Write « best-effort ») ; la cause va au journal.
    await Executer(v3a.Connexion, "EXEC sp_rename 'soc.TraceEnvoi', 'TraceEnvoiCachee';");
    capteur.Vider();
    var avantPanne = smtp.Recus.Count;
    var sansTrace = await service.EnvoyerAsync(GreffeA, new DemandeCourriel("BANC_SIMPLE", "client@exemple-banc.example"));
    await Executer(v3a.Connexion, "EXEC sp_rename 'soc.TraceEnvoiCachee', 'TraceEnvoi';");
    Exiger(sansTrace.Etat == EtatsCourriel.Envoye && smtp.Recus.Count == avantPanne + 1
           && capteur.Evenements.Count(e => e.Id == TracesCourriels.EvenementTraceImpossible.Id) == 5,
        $"SOC-006 CA03 (jumeau) : la table de trace en panne, le courriel part quand meme et chaque etape perdue est au journal du serveur ({capteur.Evenements.Count(e => e.Id == TracesCourriels.EvenementTraceImpossible.Id)} evenements)");

    // =====================================================================================================
    // SOC-006 CA06 — hors production, aucun courriel n'est remis au vrai serveur de messagerie
    // =====================================================================================================
    Console.WriteLine("--- CA06 : hors production, boite de depot");
    var parDefaut = OptionsCourriels.Lire(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Courriels:Depot"] = depot }).Build());
    var connexionsDepot = smtp.Connexions;
    var depose = await Service(parDefaut).EnvoyerAsync(GreffeA, new DemandeCourriel("BANC_DOC", "client@exemple-banc.example", Agent: Agent));
    var eml = depose.FichierDepot is { } f && File.Exists(f) ? await File.ReadAllTextAsync(f) : "";
    Exiger(!parDefaut.Production && parDefaut.Remise == "depot" && depose.Etat == EtatsCourriel.Ignore && depose.IdCourriel is not null
           && eml.Contains("To: client@exemple-banc.example") && eml.Contains("Content-ID: <LogoGreffe>") && smtp.Connexions == connexionsDepot,
        $"SOC-006 CA06 : par defaut, le courriel n'est remis a aucun serveur : il est range dans la boite de depot ({Path.GetFileName(depose.FichierDepot ?? "")})");
    var etapesDepot = await Etapes(depose.Suivi);
    Exiger(Suite(etapesDepot) == "PREPARATION_EMAIL > EMAIL_SEND_DEBUT > SMTP_IGNORE_DEV > EMAIL_SEND_SUCCES > EMAIL_ENVOYE"
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.TraceEnvoi WHERE Suivi = @s AND Etape = 'SMTP_IGNORE_DEV' AND Succes = 1 AND FichierDepot = @f",
               ("@s", depose.Suivi), ("@f", depose.FichierDepot ?? "")) == 1,
        $"SOC-006 CA06 : la trace le note « envoi ignore » (SMTP_IGNORE_DEV), avec le fichier, comme la V2 hors production ({Suite(etapesDepot)})");
    // Un serveur de messagerie nomme, hors production : jamais contacte. « smtp.invalid » ne se resout jamais (RFC 2606) :
    // si la garde lachait, l'envoi echouerait au lieu d'etre ignore.
    var horsProd = await Service(new OptionsCourriels { Remise = "smtp", SmtpHote = "smtp.invalid", Depot = depot })
        .EnvoyerAsync(GreffeA, new DemandeCourriel("BANC_SIMPLE", "client@exemple-banc.example"));
    Exiger(horsProd.Etat == EtatsCourriel.Ignore && !Suite(await Etapes(horsProd.Suivi)).Contains("SMTP_APPEL"),
        "SOC-006 CA06 : hors production, un serveur de messagerie qui n'est pas celui du poste n'est jamais appele : envoi ignore, range dans la boite de depot");
    var avantProd = smtp.Recus.Count;
    var enProd = await Service(new OptionsCourriels { Production = true, Remise = "smtp", SmtpHote = "127.0.0.1", SmtpPort = smtp.Port, Depot = depot })
        .EnvoyerAsync(GreffeA, new DemandeCourriel("BANC_SIMPLE", "client@exemple-banc.example"));
    Exiger(enProd.Etat == EtatsCourriel.Envoye && smtp.Recus.Count == avantProd + 1,
        "SOC-006 CA06 (jumeau) : un serveur declare de production remet le courriel au serveur de messagerie");
    Exiger(OptionsCourriels.EstBoucleLocale("127.0.0.1") && OptionsCourriels.EstBoucleLocale("::1") && OptionsCourriels.EstBoucleLocale("[::1]")
           && OptionsCourriels.EstBoucleLocale("localhost") && !OptionsCourriels.EstBoucleLocale("srvmail.amitel.fr")
           && !OptionsCourriels.EstBoucleLocale("127.0.0.1.nip.io") && !OptionsCourriels.EstBoucleLocale("10.0.0.1"),
        "SOC-006 CA06 : « serveur du poste » = une adresse de bouclage ecrite en clair ; un nom n'est jamais resolu");
    Exception? Refus(Dictionary<string, string?> c)
    {
        try { OptionsCourriels.Lire(new ConfigurationBuilder().AddInMemoryCollection(c).Build()); return null; }
        catch (InvalidOperationException e) { return e; }
    }
    Exiger(Refus(new() { ["Courriels:Production"] = "true" }) is not null
           && Refus(new() { ["Courriels:Remise"] = "smtp" }) is not null
           && Refus(new() { ["Courriels:Remise"] = "pigeon" }) is not null
           && Refus(new() { ["Courriels:ConservationJours"] = "0" }) is not null
           && Refus(new() { ["Courriels:Production"] = "true", ["Courriels:Remise"] = "smtp", ["Courriels:Smtp:Hote"] = "srvmail.amitel.fr" }) is null,
        "SOC-006 CA06 : le serveur refuse de demarrer sur une configuration incoherente (production sans serveur de messagerie, smtp sans hote, duree nulle) ; une production complete demarre");

    // =====================================================================================================
    // SOC-006 CA04 — le menage quotidien efface les traces de plus de 90 jours (duree reglable)
    // =====================================================================================================
    Console.WriteLine("--- CA04 : menage des traces");
    var vieille = Guid.NewGuid();
    var recente = Guid.NewGuid();
    foreach (var (suivi, jours) in new[] { (vieille, 91), (recente, 89) })
        await Executer(v3a.Connexion,
            "INSERT INTO soc.TraceEnvoi (DateCreation, CodeGreffe, Suivi, Application, Etape, Succes) VALUES (DATEADD(DAY, -@j, GETDATE()), @g, @s, 'banc', 'EMAIL_SEND_SUCCES', 1);",
            ("@j", jours), ("@g", GreffeA), ("@s", suivi));
    var traceAvant = await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.TraceEnvoi");
    var menage = app.Services.GetRequiredService<MenageCourriels>();
    var passage = await menage.PasserAsync();
    var bilanA = passage.Single(p => p.Greffe == GreffeA);
    Exiger(bilanA.Effacees == 1 && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.TraceEnvoi WHERE Suivi = @s", ("@s", vieille)) == 0
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.TraceEnvoi") == traceAvant - 1,
        $"SOC-006 CA04 : le menage efface la trace de 91 jours ({bilanA.Effacees} effacee), comme la V2 (90 jours par defaut)");
    Exiger(await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.TraceEnvoi WHERE Suivi = @s", ("@s", recente)) == 1
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.TraceEnvoi WHERE Suivi = @s", ("@s", accepte.Suivi)) == 5,
        "SOC-006 CA04 (jumeau) : une trace de 89 jours et celles du jour restent");
    Exiger(passage.Single(p => p.Greffe == GreffeMuet).Erreur is { Length: > 0 } && passage.Count == 2,
        "SOC-006 CA04 : un greffe dont la base ne repond pas est note et n'arrete pas le menage des autres");
    var menage30 = new MenageCourriels(new OptionsCourriels { ConservationJours = 30, Depot = depot }, catalogue, journaux.CreateLogger<MenageCourriels>());
    var passage30 = await menage30.PasserAsync();
    Exiger(passage30.Single(p => p.Greffe == GreffeA).Effacees == 1
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.TraceEnvoi WHERE Suivi = @s", ("@s", recente)) == 0,
        "SOC-006 CA04 : la duree reste un reglage (Courriels:ConservationJours) : a 30 jours, la trace de 89 jours part");
    Exiger(MenageCourriels.AttenteJusquA(new TimeSpan(3, 0, 0), new DateTime(2026, 11, 21, 2, 0, 0)) == TimeSpan.FromHours(1)
           && MenageCourriels.AttenteJusquA(new TimeSpan(3, 0, 0), new DateTime(2026, 11, 21, 4, 0, 0)) == TimeSpan.FromHours(23)
           && app.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().Contains(menage),
        "SOC-006 T03 : le menage passe chaque jour a 03:00 (heure du serveur), il tourne avec le serveur");

    // =====================================================================================================
    // SOC-006 CA05 — un modele reserve a Amitel n'est pas dans la liste d'un agent du greffe
    // =====================================================================================================
    Console.WriteLine("--- CA05 : modeles reserves a Amitel");
    var modelesAgent = await Lire<ModeleMessage[]>(RoutesCourriels.Pour(RoutesCourriels.Modeles, GreffeA), Agent);
    var modelesAmitel = await Lire<ModeleMessage[]>(RoutesCourriels.Pour(RoutesCourriels.Modeles, GreffeA), AgentAmitel);
    var nonReserves = await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage WHERE ReserveAmitel = 0");
    var tous = await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage");
    Exiger(modelesAgent.All(m => !m.ReserveAmitel) && modelesAgent.Length == nonReserves && !modelesAgent.Any(m => m.Type == "BANC_AMITEL")
           && modelesAgent.Any(m => m.Type == "BANC_DOC"),
        $"SOC-006 CA05 : un agent du greffe ne voit pas les modeles reserves a Amitel ({modelesAgent.Length} modeles sur {tous}, BANC_AMITEL absent)");
    Exiger(modelesAmitel.Length == tous && modelesAmitel.Any(m => m.Type == "BANC_AMITEL" && m.ReserveAmitel),
        $"SOC-006 CA05 (jumeau) : un agent d'Amitel les voit tous ({modelesAmitel.Length})");
    // Par son nom non plus : le modele reserve est « inconnu » pour l'agent du greffe (404), lisible pour Amitel.
    using (var parNomAgent = await Appeler(RoutesCourriels.PourModele(GreffeA, "BANC_AMITEL"), Agent))
    using (var parNomAmitel = await Appeler(RoutesCourriels.PourModele(GreffeA, "BANC_AMITEL"), AgentAmitel))
    using (var modeleInconnu = await Appeler(RoutesCourriels.PourModele(GreffeA, "PIGEON"), AgentAmitel))
        Exiger(parNomAgent.StatusCode == HttpStatusCode.NotFound && parNomAmitel.StatusCode == HttpStatusCode.OK
               && modeleInconnu.StatusCode == HttpStatusCode.NotFound,
            $"SOC-006 CA05 : un modele reserve a Amitel ne se lit pas non plus par son nom pour un agent du greffe ({(int)parNomAgent.StatusCode}, " +
            $"comme un modele inconnu : {(int)modeleInconnu.StatusCode}) ; Amitel le lit ({(int)parNomAmitel.StatusCode})");

    // =====================================================================================================
    // La route de consultation : liste, filtres, detail, droits
    // =====================================================================================================
    Console.WriteLine("--- Route de consultation des envois");
    var aujourdhui = DateOnly.FromDateTime(DateTime.Today);
    var envois = await Lire<CourrielSuivi[]>(RoutesCourriels.Filtrer(GreffeA, du: aujourdhui));
    var suivisDuJour = await Compter(v3a.Connexion, "SELECT COUNT(DISTINCT Suivi) FROM soc.TraceEnvoi WHERE DateCreation >= CAST(GETDATE() AS date)");
    Exiger(envois.Length == suivisDuJour && envois.Length >= 8
           && envois.Single(e => e.Suivi == accepte.Suivi).Etat == EtatsCourriel.Envoye
           && envois.Single(e => e.Suivi == refuse.Suivi) is { Etat: EtatsCourriel.Echec, Erreur: { } err } && err.Contains("5.1.1")
           && envois.Single(e => e.Suivi == depose.Suivi) is { Etat: EtatsCourriel.Ignore, FichierDepot: { } fd } && fd == depose.FichierDepot
           && envois.Zip(envois.Skip(1)).All(p => p.First.Debut >= p.Second.Debut),
        $"SOC-006 CA03 (route) : la liste du jour rend chaque envoi une fois, son issue (envoye, echec et sa cause, ignore et son fichier), les plus recents d'abord ({envois.Length})");
    var echecsSeuls = await Lire<CourrielSuivi[]>(RoutesCourriels.Filtrer(GreffeA, etat: EtatsCourriel.Echec));
    var parDestinataire = await Lire<CourrielSuivi[]>(RoutesCourriels.Filtrer(GreffeA, destinataire: "refuse@"));
    var parModele = await Lire<CourrielSuivi[]>(RoutesCourriels.Filtrer(GreffeA, modele: "BANC_HTML"));
    var demain = await Lire<CourrielSuivi[]>(RoutesCourriels.Filtrer(GreffeA, du: aujourdhui.AddDays(1)));
    var un = await Lire<CourrielSuivi[]>(RoutesCourriels.Filtrer(GreffeA, max: 1));
    Exiger(echecsSeuls.Length == 4 && echecsSeuls.All(e => e.Etat == EtatsCourriel.Echec)
           && parDestinataire.Select(e => e.Suivi).SequenceEqual([refuse.Suivi])
           && parModele.Length == 0 && demain.Length == 0 && un.Length == 1,
        $"Route : les filtres de l'ecran V2 (periode, destinataire, modele, nombre) et l'issue ({echecsSeuls.Length} echecs, {parDestinataire.Length} pour refuse@, 0 demain, 1 avec max=1)");
    var detail = await Lire<DetailCourriel>(RoutesCourriels.Pour(RoutesCourriels.Envoi, GreffeA, accepte.Suivi));
    Exiger(detail.Envoi.Suivi == accepte.Suivi && detail.Etapes.Select(e => e.Etape).SequenceEqual(etapesAccepte.Select(e => e.Etape))
           && detail.Corps == "Madame, Monsieur,|Voici vos documents :|$PieceJointe$|$LOGO$ Le greffe" && detail.CorpsHtml == false,
        "Route : le detail d'un envoi rend ses etapes dans l'ordre et le texte du courriel parti (chargé a la demande, comme le double-clic de la V2)");
    using (var r = await Appeler(RoutesCourriels.Pour(RoutesCourriels.Envoi, GreffeA, Guid.NewGuid()), Agent))
        Exiger(r.StatusCode == HttpStatusCode.NotFound, $"Route : un numero de suivi inconnu -> 404 ({(int)r.StatusCode})");
    using (var r = await Appeler(RoutesCourriels.Pour(RoutesCourriels.Envois, GreffeA) + "?etat=pigeon", Agent))
        Exiger(r.StatusCode == HttpStatusCode.BadRequest, $"Route : une issue inconnue -> 400 ({(int)r.StatusCode})");

    // Les deux premieres exigent MAIL_LOT1 ; les trois de la Messagerie (modeles, un modele, listes) seulement d'etre admis.
    var lectures = new[]
    {
        RoutesCourriels.Pour(RoutesCourriels.Envois, GreffeA),
        RoutesCourriels.Pour(RoutesCourriels.Envoi, GreffeA, accepte.Suivi),
        RoutesCourriels.Pour(RoutesCourriels.Modeles, GreffeA),
        RoutesCourriels.PourModele(GreffeA, "BANC_DOC"),
        RoutesCourriels.Pour(RoutesCourriels.ListesDiffusion, GreffeA),
    };
    var sansSession = new List<int>();
    var absent = new List<int>();
    foreach (var adresse in lectures)
    {
        using (var r = await Appeler(adresse, null)) sansSession.Add((int)r.StatusCode);
        using (var r = await Appeler(adresse.Replace(GreffeA, GreffeAbsent), Agent)) absent.Add((int)r.StatusCode);
    }
    Exiger(sansSession.All(c => c == 401) && absent.All(c => c == 404),
        $"Acces : sans session Windows -> 401 ({string.Join(", ", sansSession)}) ; greffe absent du catalogue -> 404 ({string.Join(", ", absent)})");
    var refusDroit = new List<(int Code, string Corps)>();
    foreach (var adresse in lectures[..2])
        using (var r = await Appeler(adresse, SansDroit)) refusDroit.Add(((int)r.StatusCode, await r.Content.ReadAsStringAsync()));
    var sansDroitMessagerie = new List<int>();
    foreach (var adresse in lectures[2..])
        using (var r = await Appeler(adresse, SansDroit)) sansDroitMessagerie.Add((int)r.StatusCode);
    Exiger(refusDroit.All(x => x.Code == 403 && x.Corps.Contains("MAIL_LOT1")) && sansDroitMessagerie.All(c => c == 200),
        $"Acces : sans la fonction MAIL_LOT1 (procedure V2 « Mails envoyés en Lot 1 »), la liste et le detail sont refuses (403, motif nomme) ; " +
        $"les modeles, un modele et les listes restent lisibles ({string.Join(", ", sansDroitMessagerie)})");
    var routesModule = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
        .Where(r => r.Metadata.GetMetadata<ModuleOrigine>()?.Nom == "RigV3.Serveur.Socle.Courriels.ModuleCourriels").ToList();
    var sansPolitique = routesModule.Where(r => !r.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == PolitiquesSocle.AgentAdmis))
        .Select(r => r.RoutePattern.RawText).ToList();
    const int RoutesDEcriture = 6;   // creer, modifier, activer : un modele et une liste (partie « SOC-006 T04 » plus bas)
    Exiger(routesModule.Count == lectures.Length + RoutesDEcriture && sansPolitique.Count == 0,
        $"Acces : les {routesModule.Count} routes du module portent PolitiquesSocle.AgentAdmis -- les {lectures.Length} de lecture appelees ci-dessus, " +
        $"les {RoutesDEcriture} d'ecriture dans la partie « SOC-006 T04 »");
    var source = await File.ReadAllTextAsync(Path.Combine(racine, "Serveur", "Socle", "Courriels", "ModuleCourriels.cs"));
    Exiger(!source.Contains("BaseRig b,") && !source.Contains(".OuvrirAsync(") && source.Contains("OuvrirBaseV3Async"),
        "Acces : le module n'ouvre la base du greffe que par le catalogue (OuvrirBaseV3Async), jamais la base unique");

    // =====================================================================================================
    // L'ecran « Courriels envoyés » (Pont/EcranCourriels.cs) sur le vrai serveur, par le meme client que l'app
    // =====================================================================================================
    Console.WriteLine("--- Ecran « Courriels envoyés »");
    ClientServeur ClientEcran(string compte)
    {
        Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, compte);
        try { return new ClientServeur(new Uri("http://localhost/"), app.GetTestServer().CreateHandler()); }
        finally { Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null); }
    }
    var vueEcran = await EcranCourriels.OuvrirAsync(ClientEcran(Agent), GreffeA, null);
    Exiger(vueEcran.Mode == "serveur" && vueEcran.Envois.Length == envois.Length && vueEcran.Filtre.Du == aujourdhui.ToString("yyyy-MM-dd"),
        $"Ecran SOC-006 : a l'ouverture, les courriels du jour du greffe, comme l'ecran V2 ({vueEcran.Mode}, {vueEcran.Envois.Length})");
    var vueEchecs = await EcranCourriels.OuvrirAsync(ClientEcran(Agent), GreffeA, new FiltreCourriels(null, null, null, null, EtatsCourriel.Echec));
    var detailEcran = await EcranCourriels.DetailAsync(ClientEcran(Agent), GreffeA, refuse.Suivi.ToString());
    Exiger(vueEchecs.Envois.Length == echecsSeuls.Length && detailEcran.Detail is { } de
           && de.Etapes.Any(e => e.Etape == EtapesCourriel.EnvoiErreur && (e.ErreurMessage ?? "").Contains("5.1.1")),
        "Ecran SOC-006 CA03 : filtre sur les echecs ; le detail d'un refus montre l'etape et la cause");
    var refusEcran = await EcranCourriels.OuvrirAsync(ClientEcran(SansDroit), GreffeA, null);
    Exiger(refusEcran.Mode == "refuse" && (refusEcran.Message ?? "").Contains("MAIL_LOT1") && refusEcran.Envois.Length == 0,
        $"Ecran : sans MAIL_LOT1, l'ecran dit pourquoi (« {refusEcran.Message} »)");
    var demo = await EcranCourriels.OuvrirAsync(null, null, null);
    var demoDetail = await EcranCourriels.DetailAsync(null, null, demo.Envois[0].Suivi.ToString());
    var sansGreffe = await EcranCourriels.OuvrirAsync(ClientEcran(Agent), null, null);
    Exiger(demo.Mode == "demo" && demo.Envois.Select(e => e.Etat).Order().SequenceEqual([EtatsCourriel.Echec, EtatsCourriel.Envoye, EtatsCourriel.Ignore])
           && demoDetail.Detail is not null && sansGreffe.Mode == "sans-greffe",
        "Ecran : sans RIGV3_SERVEUR, trois envois de demonstration (un par issue) ; branche sans greffe ouvert, il le dit");

    // =====================================================================================================
    // L'ecran « Messagerie » (Pont/EcranMessagerie.cs, SOC-006 T04) sur le vrai serveur : CA05 cote ecran
    // ENTREE QUI LE FAIT TOMBER si le branchement est faux : l'agent du greffe banc.courriel (EstAmitel = 0) ouvre la
    // Messagerie alors que la base porte BANC_AMITEL (ReserveAmitel = 1) -- l'ancienne maquette, ses modeles inventes et sa
    // bascule « Amitel » pour tous ne passent aucun de ces controles.
    // =====================================================================================================
    Console.WriteLine("--- Ecran « Messagerie » (modeles du greffe, CA05 cote ecran)");
    var messagerieAgent = await EcranMessagerie.OuvrirAsync(ClientEcran(Agent), GreffeA);
    var messagerieAmitel = await EcranMessagerie.OuvrirAsync(ClientEcran(AgentAmitel), GreffeA);
    Exiger(messagerieAgent.Mode == "serveur" && messagerieAgent.Greffe == GreffeA
           && messagerieAgent.Modeles.Select(m => m.Type).SequenceEqual(modelesAgent.Select(m => m.Type))
           && messagerieAgent.Modeles.All(m => !m.ReserveAmitel) && !messagerieAgent.Modeles.Any(m => m.Type == "BANC_AMITEL")
           && !messagerieAgent.VoitReservesAmitel,
        $"Ecran SOC-006 CA05 : la Messagerie d'un agent du greffe montre les modeles de SA base sans aucun reserve a Amitel, et sans la bascule « Amitel » ({messagerieAgent.Modeles.Length} sur {tous})");
    Exiger(messagerieAmitel.Mode == "serveur" && messagerieAmitel.Modeles.Length == tous
           && messagerieAmitel.Modeles.Any(m => m.Type == "BANC_AMITEL") && messagerieAmitel.VoitReservesAmitel,
        $"Ecran SOC-006 CA05 (jumeau) : la Messagerie d'un agent d'Amitel montre aussi les modeles qui lui sont reserves, et la bascule « Amitel » ({messagerieAmitel.Modeles.Length})");
    var ouvertParAgent = await EcranMessagerie.ModeleAsync(ClientEcran(Agent), GreffeA, "BANC_AMITEL");
    var ouvertParAmitel = await EcranMessagerie.ModeleAsync(ClientEcran(AgentAmitel), GreffeA, "BANC_AMITEL");
    Exiger(ouvertParAgent is { Mode: "erreur", Apercu: null } && (ouvertParAgent.Message ?? "").Contains("BANC_AMITEL")
           && ouvertParAmitel.Apercu is { Modele.Type: "BANC_AMITEL", Modele.ReserveAmitel: true },
        $"Ecran SOC-006 CA05 : un agent du greffe n'ouvre pas un modele reserve a Amitel en le demandant par son nom (« {ouvertParAgent.Message} ») ; Amitel l'ouvre");
    Exiger(messagerieAgent.Listes.Select(l => l.Code).SequenceEqual(["BANC_INACTIVE", "BANC_PERSO", "BANC_PUBLIQUE"])
           && messagerieAmitel.Listes.Select(l => l.Code).SequenceEqual(["BANC_INACTIVE", "BANC_PUBLIQUE"])
           && messagerieAgent.Listes.Single(l => l.Code == "BANC_PUBLIQUE") is { Proprietaire: RoutesCourriels.ListePublique, Judiciaire: true, Actif: true }
           && messagerieAgent.Listes.Single(l => l.Code == "BANC_INACTIVE").Actif == false,
        $"Ecran SOC-006 : les listes de distribution du greffe -- publiques et les siennes, pas celle d'un collegue, inactives marquees ({string.Join(", ", messagerieAgent.Listes.Select(l => l.Code))} ; Amitel : {string.Join(", ", messagerieAmitel.Listes.Select(l => l.Code))})");

    await PoserHabillage(habillageBase);
    var apercuDoc = (await EcranMessagerie.ModeleAsync(ClientEcran(Agent), GreffeA, "BANC_DOC")).Apercu;
    Exiger(apercuDoc is { Habille: true } ap
           && ap.Expediteur == "Greffe du banc <judiciaire@greffe-banc.example>" && ap.Reponse == "ne-pas-repondre@greffe-banc.example"
           && ap.Copie == "accuse-jud@greffe-banc.example" && ap.Corps == "Madame, Monsieur,|Voici vos documents :|$PieceJointe$|$LOGO$ Le greffe"
           && ap.Rendu.Contains("Tribunal de commerce de GRENOBLE") && ap.Rendu.Contains("Le greffier : Maître Banc, greffier")
           && ap.Rendu.Contains("Voici vos documents :") && ap.Rendu.Contains("href=\"#\"")
           && ap.Rendu.Contains("data:image/png;base64,") && !ap.Rendu.Contains("cid:")
           && ap.ListesParDefaut.Select(l => l.Code).SequenceEqual(["BANC_PUBLIQUE"]),
        "Ecran SOC-006 : l'apercu d'un modele est le courriel du greffe tel qu'il partirait -- adresses d'INFO_GREFFE, habillage, ville, greffier, " +
        "liens d'exemple, logo embarque -- avec ses listes par defaut, sans la liste privee d'un collegue");
    var apercuEtiquette = (await EcranMessagerie.ModeleAsync(ClientEcran(Agent), GreffeA, "BANC_ETIQUETTE")).Apercu;
    Exiger(apercuEtiquette is { Expediteur: "" } ae && ae.ValeursManquantes.Any(v => v.Contains("[ADRESSE_MAIL_COMPTABILITE]")),
        $"Ecran SOC-006 (jumeau) : une etiquette sans valeur dans le greffe laisse l'adresse vide et l'apercu le dit ({string.Join(" ; ", apercuEtiquette?.ValeursManquantes ?? [])})");
    var demoMessagerie = await EcranMessagerie.OuvrirAsync(null, null);
    var demoFiche = await EcranMessagerie.ModeleAsync(null, null, demoMessagerie.Modeles[0].Type);
    var messagerieSansGreffe = await EcranMessagerie.OuvrirAsync(ClientEcran(Agent), null);
    var messagerieInconnu = await EcranMessagerie.OuvrirAsync(ClientEcran(@"AMITEL\banc.inconnu"), GreffeA);
    Exiger(demoMessagerie is { Mode: "demo", VoitReservesAmitel: false } && demoMessagerie.Modeles.Length > 0
           && demoMessagerie.Modeles.All(m => !m.ReserveAmitel) && demoFiche is { Mode: "demo", Apercu: not null }
           && messagerieSansGreffe.Mode == "sans-greffe" && messagerieInconnu is { Mode: "refuse", Modeles.Length: 0 },
        $"Ecran : la demonstration seulement sans RIGV3_SERVEUR (aucun modele reserve a Amitel) ; sans greffe ouvert ou compte refuse, aucun modele ({messagerieInconnu.Mode})");

    // =====================================================================================================
    // SOC-006 T04 (suite, lot 15) — la gestion des modeles et des listes depuis la Messagerie (procedure V2 PROC_GESTEMAIL)
    // ENTREES QUI LE FONT TOMBER si une regle manque : banc.lecteur (admis, SANS GESTEMAIL) qui ecrit ; banc.courriel (agent
    // du greffe) qui modifie BANC_AMITEL (ReserveAmitel = 1) ; un nom ou un code deja pris ; une adresse invalide.
    // =====================================================================================================
    Console.WriteLine("--- SOC-006 T04 : creer, modifier, desactiver un modele et une liste (GESTEMAIL)");
    async Task<HttpResponseMessage> Ecrire<T>(HttpMethod verbe, string adresse, string? compte, T corps)
    {
        using var demande = new HttpRequestMessage(verbe, adresse) { Content = JsonContent.Create(corps, options: json) };
        if (compte is not null) demande.Headers.Add(RoutesIdentite.EnTeteCompte, compte);
        return await http.SendAsync(demande);
    }
    async Task<(int Code, string Corps)> Geste<T>(HttpMethod verbe, string adresse, string? compte, T corps)
    {
        using var r = await Ecrire(verbe, adresse, compte, corps);
        return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
    }
    SaisieModele Saisie(string type, string sujet = "Avis du banc", string corps = "Bonjour,|Un avis du greffe.|$PieceJointe$",
                        string? expediteur = "Greffe du banc <[ADRESSE_MAIL_RCS]>", bool lot1 = true, bool auto = false, bool amitel = false,
                        int[]? listes = null)
        => new(type, "Avis du banc", sujet, corps, expediteur, "[ADRESSE_MAIL_NO_REPLY]", null, null, lot1, true, false, false, auto, amitel,
               false, null, listes);
    var modelesGreffe = RoutesCourriels.Pour(RoutesCourriels.Modeles, GreffeA);
    var listesGreffe = RoutesCourriels.Pour(RoutesCourriels.ListesDiffusion, GreffeA);
    Task<int> Gestes() => Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.Journal WHERE Famille = 'courriel'");
    var idAgent = await Compter(v3a.Connexion, "SELECT Id FROM soc.Compte WHERE CompteWindows = 'banc.courriel'");
    var idPublique = await Compter(v3a.Connexion, "SELECT Id FROM soc.ListeDiffusion WHERE Code = 'BANC_PUBLIQUE'");
    var idCollegue = await Compter(v3a.Connexion, "SELECT Id FROM soc.ListeDiffusion WHERE Code = 'BANC_COLLEGUE'");
    // GESTEMAIL est bien une fonction du catalogue de la V2 : la reprise des droits (SOC-002, RepriseDroits) la porte dans la
    // base V3 de chaque greffe avec les roles qui l'ont. Le banc la pose avec SON libelle V2 (lu, jamais ecrit).
    string? libelleGestemail;
    int rolesGestemail;
    await using (var cnV2 = new SqlConnection(sourceV2.ConnectionString))
    {
        await cnV2.OpenAsync();
        libelleGestemail = (await LectureV2.ValeurAsync(cnV2,
            "SELECT PROCF_LIBELLE FROM dbo.PROCESSUS_ET_FONCTIONALITE WHERE PROCF_CODE_PROC = 'GESTEMAIL'", default))?.ToString();
        rolesGestemail = Convert.ToInt32(await LectureV2.ValeurAsync(cnV2,
            "SELECT COUNT(*) FROM dbo.HABILIT_ROLE_USER_PROCESSUS WHERE HRFON_CODE_PROC = 'GESTEMAIL'", default));
    }
    Exiger(libelleGestemail is { Length: > 0 },
        $"SOC-006 T04 : GESTEMAIL est une fonction du catalogue de {sourceV2.InitialCatalog} (« {libelleGestemail} », donnee a {rolesGestemail} role(s)) : " +
        "la reprise des droits la porte dans chaque base de greffe");
    await Executer(v3a.Connexion, """
        INSERT INTO soc.Fonction (Code, Libelle, TypeV2) VALUES ('GESTEMAIL', @libelle, 'DLL');
        INSERT INTO soc.RoleFonction (Role, Fonction) VALUES ('BANC', 'GESTEMAIL');
        """, ("@libelle", libelleGestemail ?? "Modèle de mail"));

    // Aucun modele de la V2 ne devient impossible a enregistrer : les regles de saisie de la V3 acceptent les 40 de RIG_DEV, sauf
    // un champ que la V2 exige aussi (sujet, libelle ecran : MetierObligatoire, OPE_EDIT_EMAILMODEL.Designer.cs:103 et :191) --
    // l'agent le complete a l'enregistrement, en V2 comme en V3.
    string[] exigesParLaV2 = ["Le sujet est obligatoire.", "Le libellé écran est obligatoire."];
    var refusesV2 = new List<string>();
    var aCompleterV2 = new List<string>();
    await using (var cn = await OuvrirV3A())
    await using (var cmd = new SqlCommand("""
        SELECT Type, LibelleEcran, Sujet, Corps, Expediteur, Reponse, Copie, DestinataireDefaut, Lot1, Lot2, CorpsHtml
        FROM soc.ModeleMessage WHERE Type NOT LIKE 'BANC[_]%';
        """, cn))
    await using (var r = await cmd.ExecuteReaderAsync())
        while (await r.ReadAsync())
        {
            string? T(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            var (ok, erreurs) = ReglesCourriels.ControlerModele(new SaisieModele(r.GetString(0), T(1), r.GetString(2), r.GetString(3), T(4), T(5),
                T(6), T(7), r.GetBoolean(8), r.GetBoolean(9), false, false, false, false, false, null), r.GetBoolean(10), new HabillageDuGreffe(false, false));
            if (ok is null && erreurs.All(exigesParLaV2.Contains)) aCompleterV2.Add($"{r.GetString(0)} ({string.Join(" ", erreurs)})");
            else if (ok is null) refusesV2.Add($"{r.GetString(0)} : {string.Join(" ", erreurs)}");
        }
    var nbV2 = await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage WHERE Type NOT LIKE 'BANC[_]%'");
    Exiger(refusesV2.Count == 0 && nbV2 > 0,
        $"SOC-006 T04 : les {nbV2} modeles repris de {sourceV2.InitialCatalog} passent les regles de saisie de la V3 (noms, tailles, adresses et etiquettes) : " +
        $"aucun ne devient impossible a enregistrer ({refusesV2.Count} refuse(s){(refusesV2.Count > 0 ? ", ex. " + refusesV2[0] : "")} ; " +
        $"a completer comme en V2 : {(aCompleterV2.Count == 0 ? "aucun" : string.Join(", ", aCompleterV2))})");

    // --- Creation d'un modele par un agent GESTEMAIL ---------------------------------------------------------------
    var gestesAvant = await Gestes();
    var cree = await Geste(HttpMethod.Post, modelesGreffe, Agent, Saisie("BANC_NEUF", listes: [idPublique]) with { Ecran = "banc" });
    Exiger(cree.Code == 201 && await Compter(v3a.Connexion, """
               SELECT COUNT(*) FROM soc.ModeleMessage WHERE Type = 'BANC_NEUF' AND Sujet = 'Avis du banc' AND Corps = 'Bonjour,|Un avis du greffe.|$PieceJointe$'
                 AND CorpsHtml = 0 AND Expediteur = 'Greffe du banc <[ADRESSE_MAIL_RCS]>' AND Reponse = '[ADRESSE_MAIL_NO_REPLY]' AND Lot1 = 1
                 AND ReserveAmitel = 0 AND UtilisableEnSaisie = 1;
               """) == 1
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessageListe WHERE TypeModele = 'BANC_NEUF' AND IdListe = @l", ("@l", idPublique)) == 1,
        $"SOC-006 T04 : un agent GESTEMAIL CREE un modele (POST {RoutesCourriels.Modeles}) -- {cree.Code}, ses champs et sa liste par defaut en base");
    Exiger(await Gestes() == gestesAvant + 1 && await Compter(v3a.Connexion, """
               SELECT COUNT(*) FROM soc.Journal WHERE Famille = 'courriel' AND Source = 'v3' AND IdAgent = @a AND Agent = 'Courriels BANC'
                 AND Verbe = 'a cree' AND Objet = 'le modèle de courriel BANC_NEUF' AND IdObjet = 'BANC_NEUF' AND Ecran = 'banc'
                 AND Avant IS NULL AND Apres LIKE '%"sujet":"Avis du banc"%';
               """, ("@a", idAgent)) == 1,
        "SOC-016 / SOC-006 T04 : la creation est ecrite au journal des gestes, au nom de l'agent, avec son ecran et le modele cree");
    var double_ = await Geste(HttpMethod.Post, modelesGreffe, Agent, Saisie("banc_neuf"));
    var invalide = await Geste(HttpMethod.Post, modelesGreffe, Agent, Saisie("BANC_INVALIDE", sujet: new string('s', 201), expediteur: "pas une adresse") with
    {
        Copie = "[ADRESSE_MAIL_ACCUSE_ENVOI_JUD];copie@@banc",
    });
    var sansExpediteurSaisi = await Geste(HttpMethod.Post, modelesGreffe, Agent, Saisie("BANC_SANS_DE", expediteur: null));
    var etiquetteSecrete = await Geste(HttpMethod.Post, modelesGreffe, Agent, Saisie("BANC_SECRET", expediteur: "[PWD_SQL]"));
    Exiger(double_.Code == 400 && double_.Corps.Contains("existe déjà") && invalide.Code == 400 && invalide.Corps.Contains("Le sujet tient en 200 caractères (201 saisis)")
           && invalide.Corps.Contains("« pas une adresse »") && invalide.Corps.Contains("« copie@@banc »")
           && sansExpediteurSaisi.Code == 400 && sansExpediteurSaisi.Corps.Contains("L'expéditeur est obligatoire")
           && etiquetteSecrete.Code == 400 && etiquetteSecrete.Corps.Contains("[PWD_SQL]")
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage WHERE Type IN ('BANC_INVALIDE', 'BANC_SANS_DE', 'BANC_SECRET')") == 0
           && await Gestes() == gestesAvant + 1,
        $"SOC-006 T04 : controle des saisies -- nom deja pris (meme en minuscules), sujet trop long, adresse ou copie invalide, expediteur absent, etiquette " +
        $"de mot de passe : 400 avec le motif, rien n'est ecrit ni journalise (« {Court(invalide.Corps, 140)} »)");
    // La V2 refuse d'enregistrer sans sujet ni libelle ecran : MetierObligatoire (OPE_EDIT_EMAILMODEL.Designer.cs:103 et :191),
    // controle a la validation par Ult.moteur.cs:516. La V3 aussi.
    var sansSujet = await Geste(HttpMethod.Post, modelesGreffe, Agent, Saisie("BANC_SANS_SUJET", sujet: ""));
    var sansLibelle = await Geste(HttpMethod.Post, modelesGreffe, Agent, Saisie("BANC_SANS_LIBELLE") with { LibelleEcran = null });
    Exiger(sansSujet.Code == 400 && sansSujet.Corps.Contains("Le sujet est obligatoire")
           && sansLibelle.Code == 400 && sansLibelle.Corps.Contains("Le libellé écran est obligatoire")
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage WHERE Type IN ('BANC_SANS_SUJET', 'BANC_SANS_LIBELLE')") == 0
           && await Gestes() == gestesAvant + 1,
        $"SOC-006 T04 : sujet et libelle ecran obligatoires comme la V2 (OPE_EDIT_EMAILMODEL.Designer.cs:103, :191) -- 400 avec le motif, " +
        $"rien n'est ecrit ni journalise ({sansSujet.Code} « {Court(sansSujet.Corps, 60)} » / {sansLibelle.Code} « {Court(sansLibelle.Corps, 60)} »)");

    // --- Modification ------------------------------------------------------------------------------------------------
    var modifie = await Geste(HttpMethod.Put, RoutesCourriels.PourModele(GreffeA, "BANC_NEUF"), Agent,
        Saisie("BANC_NEUF", sujet: "Avis modifie", lot1: false, auto: true, listes: []) with { Signature = "Le greffier du banc", SignatureActive = true });
    Exiger(modifie.Code == 200 && await Compter(v3a.Connexion, """
               SELECT COUNT(*) FROM soc.ModeleMessage WHERE Type = 'BANC_NEUF' AND Sujet = 'Avis modifie' AND Lot1 = 0 AND EnvoiAutomatique = 0
                 AND SignatureActive = 1 AND Signature = 'Le greffier du banc';
               """) == 1
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessageListe WHERE TypeModele = 'BANC_NEUF'") == 0,
        $"SOC-006 T04 : un agent GESTEMAIL MODIFIE un modele (PUT) -- objet, signature (EMAILMOD_SIGNATURE_ACTIVE, EMAILMOD_SIGNATURE), liste par defaut retiree ; " +
        $"envoi automatique demande sans RCS -> remis a faux, comme la V2 ({modifie.Code})");
    Exiger(await Compter(v3a.Connexion, """
               SELECT COUNT(*) FROM soc.Journal WHERE Famille = 'courriel' AND IdAgent = @a AND Verbe = 'a modifie' AND IdObjet = 'BANC_NEUF'
                 AND Avant LIKE '%"sujet":"Avis du banc"%' AND Apres LIKE '%"sujet":"Avis modifie"%' AND Apres LIKE '%"envoiAutomatique":false%';
               """, ("@a", idAgent)) == 1,
        "SOC-016 / SOC-006 T04 : la modification est ecrite au journal avec les valeurs avant et apres");
    var renomme = await Geste(HttpMethod.Put, RoutesCourriels.PourModele(GreffeA, "BANC_NEUF"), Agent, Saisie("BANC_AUTRE_NOM"));
    var avecRcs = await Geste(HttpMethod.Put, RoutesCourriels.PourModele(GreffeA, "BANC_NEUF"), Agent, Saisie("BANC_NEUF", lot1: true, auto: true));
    Exiger(renomme.Code == 400 && renomme.Corps.Contains("ne change pas")
           && avecRcs.Code == 200 && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage WHERE Type = 'BANC_NEUF' AND Lot1 = 1 AND EnvoiAutomatique = 1") == 1,
        "SOC-006 T04 : le nom d'un modele ne change pas une fois cree (400) ; jumeau : l'envoi automatique d'un modele RCS est garde");

    // V2 OPE_EDIT_EMAILMODEL.cs:91-110 : sous un habillage, $LOGO$ est interdit ; un modele HTML sous un habillage regle porte $PieceJointe$.
    var logoSousHabillage = await Geste(HttpMethod.Put, RoutesCourriels.PourModele(GreffeA, "BANC_NEUF"), Agent,
        Saisie("BANC_NEUF", corps: "Bonjour|$LOGO$|$PieceJointe$"));
    var htmlSansPiece = await Geste(HttpMethod.Put, RoutesCourriels.PourModele(GreffeA, "BANC_HTML"), Agent,
        Saisie("BANC_HTML", corps: "<p>Bonjour</p><p>Le greffe</p>"));
    await PoserHabillage(null);   // plus d'habillage en base ; le reglage pointe un fichier absent : le greffe n'a plus d'habillage
    var logoSansHabillage = await Geste(HttpMethod.Put, RoutesCourriels.PourModele(GreffeA, "BANC_NEUF"), Agent,
        Saisie("BANC_NEUF", corps: "Bonjour|$LOGO$|$PieceJointe$"));
    Exiger(logoSousHabillage.Code == 400 && logoSousHabillage.Corps.Contains("$LOGO$ est interdit")
           && htmlSansPiece.Code == 400 && htmlSansPiece.Corps.Contains("$PieceJointe$ est obligatoire")
           && logoSansHabillage.Code == 200
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage WHERE Type = 'BANC_NEUF' AND CorpsHtml = 1 AND Corps = 'Bonjour|$LOGO$|$PieceJointe$'") == 1,
        "SOC-006 T04 : $LOGO$ refuse sous l'habillage du greffe, $PieceJointe$ exige d'un modele HTML ; jumeau : sans habillage, $LOGO$ passe et le " +
        "modele devient HTML (EmailModel.Sauve)");

    // --- Desactivation, reactivation ----------------------------------------------------------------------------------
    var activationNeuf = RoutesCourriels.PourModele(GreffeA, "BANC_NEUF", RoutesCourriels.ActivationModele);
    var desactive = await Geste(HttpMethod.Put, activationNeuf, Agent, new SaisieActivation(false, "banc"));
    var etatDesactive = await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage WHERE Type = 'BANC_NEUF' AND UtilisableEnSaisie = 0");
    var vuParLaListe = (await Lire<ModeleMessage[]>(modelesGreffe)).Single(m => m.Type == "BANC_NEUF");
    var gestesDesactive = await Gestes();
    var encore = await Geste(HttpMethod.Put, activationNeuf, Agent, new SaisieActivation(false, "banc"));
    var gestesEncore = await Gestes();
    var reactive = await Geste(HttpMethod.Put, activationNeuf, Agent, new SaisieActivation(true, "banc"));
    Exiger(desactive.Code == 200 && etatDesactive == 1 && vuParLaListe.UtilisableEnSaisie == false && encore.Code == 200 && gestesEncore == gestesDesactive
           && reactive.Code == 200 && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage WHERE Type = 'BANC_NEUF' AND UtilisableEnSaisie = 1") == 1
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.Journal WHERE Famille = 'courriel' AND IdObjet = 'BANC_NEUF' AND Verbe = 'a desactive' AND Apres = '{\"actif\":false}'") == 1
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.Journal WHERE Famille = 'courriel' AND IdObjet = 'BANC_NEUF' AND Verbe = 'a modifie' AND Apres = '{\"actif\":true}'") == 1,
        "SOC-006 T04 : un agent GESTEMAIL DESACTIVE un modele (il reste en base, la liste le rend inactif) puis le REACTIVE ; chaque geste au journal, " +
        "un second « désactiver » ne change rien et n'ecrit rien");

    // --- Sans GESTEMAIL : 403, rien n'est ecrit -------------------------------------------------------------------------
    var gestesAvant403 = await Gestes();
    var refus403 = new[]
    {
        await Geste(HttpMethod.Post, modelesGreffe, SansDroit, Saisie("BANC_LECTEUR")),
        await Geste(HttpMethod.Put, RoutesCourriels.PourModele(GreffeA, "BANC_NEUF"), SansDroit, Saisie("BANC_NEUF", sujet: "par le lecteur")),
        await Geste(HttpMethod.Put, activationNeuf, SansDroit, new SaisieActivation(false)),
        await Geste(HttpMethod.Post, listesGreffe, SansDroit, new SaisieListe("BANC_LECTEUR", "Liste du lecteur", "l@banc.example", true, false, true)),
        await Geste(HttpMethod.Put, RoutesCourriels.PourListe(GreffeA, idPublique), SansDroit,
            new SaisieListe("BANC_PUBLIQUE", "par le lecteur", "l@banc.example", false, true, true)),
        await Geste(HttpMethod.Put, RoutesCourriels.PourListe(GreffeA, idPublique, RoutesCourriels.ActivationListe), SansDroit, new SaisieActivation(false)),
    };
    Exiger(refus403.All(r => r.Code == 403 && r.Corps.Contains(RoutesCourriels.FonctionGestion))
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage WHERE Type = 'BANC_LECTEUR' OR Sujet = 'par le lecteur'") == 0
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ListeDiffusion WHERE Code = 'BANC_LECTEUR' OR Libelle = 'par le lecteur' OR (Code = 'BANC_PUBLIQUE' AND Actif = 0)") == 0
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage WHERE Type = 'BANC_NEUF' AND UtilisableEnSaisie = 1") == 1
           && await Gestes() == gestesAvant403,
        $"SOC-006 T04 : SANS la fonction GESTEMAIL, chacune des 6 ecritures (modele et liste : creer, modifier, desactiver) est refusee -- 403, motif nomme, " +
        $"rien n'est ecrit ni journalise ({string.Join(", ", refus403.Select(r => r.Code))})");
    var sansSessionEcriture = await Geste(HttpMethod.Post, modelesGreffe, null, Saisie("BANC_ANONYME"));
    var greffeAbsentEcriture = await Geste(HttpMethod.Post, modelesGreffe.Replace(GreffeA, GreffeAbsent), Agent, Saisie("BANC_ABSENT"));
    Exiger(sansSessionEcriture.Code == 401 && greffeAbsentEcriture.Code == 404,
        $"Acces : une ecriture sans session -> 401 ({sansSessionEcriture.Code}) ; dans un greffe absent du catalogue -> 404 ({greffeAbsentEcriture.Code})");

    // --- Un modele reserve a Amitel n'est modifiable que par Amitel (CA05 : l'agent du greffe ne le voit pas) -----------
    var amitelAvant = await Scalaire(v3a.Connexion, "SELECT Sujet + '|' + CAST(UtilisableEnSaisie AS varchar(1)) FROM soc.ModeleMessage WHERE Type = 'BANC_AMITEL'");
    var amitelParAgent = await Geste(HttpMethod.Put, RoutesCourriels.PourModele(GreffeA, "BANC_AMITEL"), Agent,
        Saisie("BANC_AMITEL", sujet: "modifie par le greffe") with { Expediteur = "[ADRESSE_MAIL_RCS]" });
    var amitelDesactiveParAgent = await Geste(HttpMethod.Put, RoutesCourriels.PourModele(GreffeA, "BANC_AMITEL", RoutesCourriels.ActivationModele), Agent,
        new SaisieActivation(false));
    var cocheAmitelParAgent = await Geste(HttpMethod.Post, modelesGreffe, Agent, Saisie("BANC_FAUX_AMITEL", amitel: true));
    var amitelApresAgent = await Scalaire(v3a.Connexion, "SELECT Sujet + '|' + CAST(UtilisableEnSaisie AS varchar(1)) FROM soc.ModeleMessage WHERE Type = 'BANC_AMITEL'");
    Exiger(amitelParAgent.Code == 404 && amitelDesactiveParAgent.Code == 404 && amitelApresAgent?.ToString() == amitelAvant?.ToString()
           && cocheAmitelParAgent.Code == 403 && cocheAmitelParAgent.Corps.Contains("Amitel")
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage WHERE Type = 'BANC_FAUX_AMITEL'") == 0,
        $"SOC-006 T04 / CA05 : un agent du GREFFE ne modifie ni ne desactive un modele reserve a Amitel -- {amitelParAgent.Code} / {amitelDesactiveParAgent.Code}, " +
        $"comme un modele inconnu, rien ne bouge (« {amitelApresAgent} ») ; il ne coche pas « réservé à Amitel » ({cocheAmitelParAgent.Code})");
    var amitelParAmitel = await Geste(HttpMethod.Put, RoutesCourriels.PourModele(GreffeA, "BANC_AMITEL"), AgentAmitel,
        Saisie("BANC_AMITEL", sujet: "modifie par Amitel", amitel: true) with { Expediteur = "[ADRESSE_MAIL_RCS]" });
    Exiger(amitelParAmitel.Code == 200
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage WHERE Type = 'BANC_AMITEL' AND Sujet = 'modifie par Amitel' AND ReserveAmitel = 1") == 1,
        $"SOC-006 T04 / CA05 (jumeau) : un agent d'AMITEL modifie le modele qui lui est reserve, et il le reste ({amitelParAmitel.Code})");

    // --- Les listes par defaut : seulement parmi les listes que l'agent voit ; celle d'un collegue reste liee ----------
    var docSansListes = await Geste(HttpMethod.Put, RoutesCourriels.PourModele(GreffeA, "BANC_DOC"), Agent,
        Saisie("BANC_DOC", corps: "Madame, Monsieur,|Voici vos documents :|$PieceJointe$|$LOGO$ Le greffe", listes: []) with
        {
            Expediteur = "Greffe du banc <[ADRESSE_MAIL_JUDICIAIRE]>", Copie = "[ADRESSE_MAIL_ACCUSE_ENVOI_JUD]",
        });
    // Le meme texte : BANC_DOC est devenu HTML ($LOGO$, EmailModel.Sauve), seule la liste demandee doit faire refuser.
    var docListeCollegue = await Geste(HttpMethod.Put, RoutesCourriels.PourModele(GreffeA, "BANC_DOC"), Agent,
        Saisie("BANC_DOC", corps: "Madame, Monsieur,|Voici vos documents :|$PieceJointe$|$LOGO$ Le greffe", listes: [idCollegue]));
    Exiger(docSansListes.Code == 200
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessageListe WHERE TypeModele = 'BANC_DOC' AND IdListe = @l", ("@l", idPublique)) == 0
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessageListe WHERE TypeModele = 'BANC_DOC' AND IdListe = @l", ("@l", idCollegue)) == 1
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ModeleMessage WHERE Type = 'BANC_DOC' AND CorpsHtml = 1") == 1
           && docListeCollegue.Code == 400 && docListeCollegue.Corps.Contains($"n° {idCollegue}"),
        $"SOC-006 T04 : les listes par defaut se choisissent parmi celles que l'agent voit -- la liste publique decochee part, la liste privee d'un collegue " +
        $"reste liee ; la lui demander est refuse (400) ({docSansListes.Code} {Court(docSansListes.Corps, 200)} / {docListeCollegue.Code} {Court(docListeCollegue.Corps, 120)})");

    // --- Les listes de distribution : creer, modifier, publique / privee, desactiver ---------------------------------
    var gestesListes = await Gestes();
    var listeCree = await Geste(HttpMethod.Post, listesGreffe, Agent,
        new SaisieListe("BANC_EXPERTS", "Experts du banc", "expert.un@cabinet.example\r\n expert.deux@cabinet.example;expert.un@cabinet.example", true, false, false, "banc"));
    var idExperts = await Compter(v3a.Connexion, "SELECT ISNULL(MAX(Id), 0) FROM soc.ListeDiffusion WHERE Code = 'BANC_EXPERTS'");
    Exiger(listeCree.Code == 201 && await Compter(v3a.Connexion, """
               SELECT COUNT(*) FROM soc.ListeDiffusion WHERE Code = 'BANC_EXPERTS' AND Libelle = 'Experts du banc'
                 AND Adresses = 'expert.un@cabinet.example;expert.deux@cabinet.example' AND Rcs = 1 AND Judiciaire = 0 AND Prive = 'banc.courriel' AND Actif = 1;
               """) == 1
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.Journal WHERE Famille = 'courriel' AND Verbe = 'a cree' AND Objet = 'la liste de distribution BANC_EXPERTS' AND IdAgent = @a",
               ("@a", idAgent)) == 1,
        $"SOC-006 T04 : un agent GESTEMAIL CREE une liste privee (proprietaire : lui), adresses une par ligne dedoublonnees en « a;b », geste au journal ({listeCree.Code})");
    var codePris = await Geste(HttpMethod.Post, listesGreffe, Agent, new SaisieListe("banc_collegue", "Meme code qu'un collegue", "x@banc.example", false, true, true));
    var adresseInvalide = await Geste(HttpMethod.Post, listesGreffe, Agent, new SaisieListe("BANC_FAUSSE", "Adresse fausse", "bon@banc.example;pas-une-adresse", false, true, true));
    var listeVide = await Geste(HttpMethod.Post, listesGreffe, Agent, new SaisieListe("BANC_VIDE", "Sans adresse", " ; ", false, true, true));
    Exiger(codePris.Code == 400 && codePris.Corps.Contains("existe déjà") && adresseInvalide.Code == 400 && adresseInvalide.Corps.Contains("Courriel (pas-une-adresse) invalide")
           && listeVide.Code == 400 && listeVide.Corps.Contains("au moins une adresse")
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ListeDiffusion WHERE Code IN ('BANC_FAUSSE', 'BANC_VIDE') OR Libelle = 'Meme code qu''un collegue'") == 0,
        "SOC-006 T04 : controle des saisies d'une liste -- code deja pris (meme par la liste privee d'un collegue), adresse invalide (« Courriel (x) invalide » " +
        "de la V2), aucune adresse : 400, rien n'est ecrit");
    var listePublique = await Geste(HttpMethod.Put, RoutesCourriels.PourListe(GreffeA, idExperts), Agent,
        new SaisieListe("BANC_EXPERTS", "Experts du ressort", "expert.un@cabinet.example;expert.trois@cabinet.example", true, true, true, "banc"));
    var redevientPrivee = await Geste(HttpMethod.Put, RoutesCourriels.PourListe(GreffeA, idExperts), Agent,
        new SaisieListe("BANC_EXPERTS", "Experts du ressort", "expert.un@cabinet.example", true, true, false));
    var listeDuCollegue = await Geste(HttpMethod.Put, RoutesCourriels.PourListe(GreffeA, idCollegue), Agent,
        new SaisieListe("BANC_COLLEGUE", "prise au collegue", "x@banc.example", true, true, true));
    Exiger(listePublique.Code == 200 && await Compter(v3a.Connexion, """
               SELECT COUNT(*) FROM soc.ListeDiffusion WHERE Id = @id AND Libelle = 'Experts du ressort' AND Prive = '*' AND Judiciaire = 1
                 AND Adresses = 'expert.un@cabinet.example;expert.trois@cabinet.example';
               """, ("@id", idExperts)) == 1
           && redevientPrivee.Code == 400 && redevientPrivee.Corps.Contains("ne redevient pas privée")
           && listeDuCollegue.Code == 404 && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.ListeDiffusion WHERE Code = 'BANC_COLLEGUE' AND Libelle = 'Liste d''un collegue'") == 1,
        "SOC-006 T04 : un agent GESTEMAIL MODIFIE sa liste et la rend publique ; une liste publique ne redevient pas privee (400, V2 :198) ; la liste privee " +
        "d'un collegue n'existe pas pour lui (404), elle ne bouge pas");
    var listeDesactivee = await Geste(HttpMethod.Put, RoutesCourriels.PourListe(GreffeA, idExperts, RoutesCourriels.ActivationListe), Agent, new SaisieActivation(false, "banc"));
    var listesVues = await Lire<ListeDiffusion[]>(listesGreffe);
    Exiger(listeDesactivee.Code == 200 && listesVues.Single(l => l.Id == idExperts).Actif == false
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.Journal WHERE Famille = 'courriel' AND Verbe = 'a desactive' AND IdObjet = @id", ("@id", idExperts.ToString())) == 1
           && await Gestes() == gestesListes + 3,
        $"SOC-006 T04 : un agent GESTEMAIL DESACTIVE une liste (elle reste, inactive) ; creer, modifier, desactiver : 3 gestes au journal, les refus aucun");

    // --- L'ecran « Messagerie » sur le vrai serveur : boutons selon GESTEMAIL, motif du serveur, apercu recalcule ------
    Console.WriteLine("--- Ecran « Messagerie » : ecriture");
    var ecranGestion = await EcranMessagerie.OuvrirAsync(ClientEcran(Agent), GreffeA);
    var ecranLecteur = await EcranMessagerie.OuvrirAsync(ClientEcran(SansDroit), GreffeA);
    var ecranAmitel = await EcranMessagerie.OuvrirAsync(ClientEcran(AgentAmitel), GreffeA);
    Exiger(ecranGestion is { PeutModifier: true, EstAmitel: false, Dictionnaire: null, Compte: "banc.courriel" }
           && ecranLecteur is { Mode: "serveur", PeutModifier: false } && ecranLecteur.Modeles.Length > 0
           && ecranAmitel is { PeutModifier: true, EstAmitel: true } && (ecranAmitel.Dictionnaire ?? "").Contains("{{MESSAGE_PERSONNALISE}}"),
        "Ecran SOC-006 T04 : la Messagerie montre ses boutons d'ecriture a un agent GESTEMAIL, la LECTURE seule sans (le serveur le dit) ; le " +
        "dictionnaire des tags et la case Amitel a un agent d'Amitel seul");
    var parEcran = await EcranMessagerie.EnregistrerModeleAsync(ClientEcran(Agent), GreffeA,
        Saisie("BANC_NEUF", sujet: "Avis par l'écran", corps: "Bonjour,|Recalcul de l'apercu.|$PieceJointe$"), "BANC_NEUF");
    Exiger(parEcran is { Ok: true, Fiche.Apercu: { } ap2 } && ap2.Modele.Sujet == "Avis par l'écran" && ap2.Rendu.Contains("Recalcul")
           && parEcran.Vue!.Modeles.Single(m => m.Type == "BANC_NEUF").Sujet == "Avis par l'écran"
           && await Compter(v3a.Connexion, "SELECT COUNT(*) FROM soc.Journal WHERE Famille = 'courriel' AND IdObjet = 'BANC_NEUF' AND Ecran = @e", ("@e", EcranMessagerie.NomEcran)) == 1,
        $"Ecran SOC-006 T04 : Enregistrer depuis la Messagerie modifie le modele, relit la liste et RECALCULE l'apercu ; le journal nomme l'ecran (« {parEcran.Message} »)");
    var refusParEcran = await EcranMessagerie.EnregistrerModeleAsync(ClientEcran(Agent), GreffeA, Saisie("BANC_NEUF", expediteur: "pas une adresse"), "BANC_NEUF");
    var lecteurParEcran = await EcranMessagerie.EnregistrerModeleAsync(ClientEcran(SansDroit), GreffeA, Saisie("BANC_NEUF"), "BANC_NEUF");
    var demoEcriture = await EcranMessagerie.EnregistrerModeleAsync(null, null, Saisie("ASS"), "ASS");
    var demoActivation = await EcranMessagerie.ActiverListeAsync(null, null, 1, false);
    Exiger(refusParEcran is { Ok: false, Vue: null } && refusParEcran.Message.Contains("« pas une adresse »") && !refusParEcran.Message.Contains("{")
           && lecteurParEcran is { Ok: false } && lecteurParEcran.Message.Contains(RoutesCourriels.FonctionGestion)
           && demoEcriture is { Ok: false } && demoEcriture.Message.StartsWith("Démonstration") && demoActivation is { Ok: false }
           && (await EcranMessagerie.OuvrirAsync(null, null)).PeutModifier,
        $"Ecran SOC-006 T04 : un refus du serveur s'affiche avec SON motif (« {Court(refusParEcran.Message, 90)} »), sans GESTEMAIL le motif nomme la fonction ; " +
        "sans serveur, la demonstration montre le formulaire et refuse proprement chaque ecriture");
    var listeParEcran = await EcranMessagerie.EnregistrerListeAsync(ClientEcran(Agent), GreffeA,
        new SaisieListe("BANC_ECRAN", "Liste de l'écran", "ecran@banc.example", false, true, false), null);
    var activeParEcran = await EcranMessagerie.ActiverModeleAsync(ClientEcran(Agent), GreffeA, "BANC_NEUF", false);
    Exiger(listeParEcran is { Ok: true } && listeParEcran.Vue!.Listes.Any(l => l.Code == "BANC_ECRAN" && l.Proprietaire == "banc.courriel")
           && activeParEcran is { Ok: true, Fiche.Apercu.Modele.UtilisableEnSaisie: false },
        "Ecran SOC-006 T04 : une liste creee depuis l'ecran revient dans ses listes ; Désactiver depuis l'ecran rend le modele inactif");

    // Ce que l'arret du serveur ecrit au journal se lit apres sa destruction, a la sortie de ce bloc.
    capteur.Vider();
    await app.StopAsync();

    async Task<SqlConnection> OuvrirV3A()
    {
        var cn = new SqlConnection(v3a.Connexion);
        await cn.OpenAsync();
        return cn;
    }
}
finally
{
    try { Directory.Delete(dossier, recursive: true); } catch (IOException) { }
}

// Le serveur du banc s'arrete proprement : rien au journal entre la fin des controles et sa destruction. Detruit sans
// StopAsync, l'hote annule ses services de fond sans s'etre arrete, et le Surveillant (lot 2) sort en « BackgroundService
// failed » (OperationCanceledException, Surveillant.cs WaitForNextTickAsync) : une fausse panne au milieu d'un banc vert.
var arret = capteur.Evenements.ToArray();
Exiger(arret.Length == 0,
    $"Arret : le serveur du banc s'arrete sans erreur ni alerte au journal ({arret.Length} evenement(s){(arret.Length > 0 ? " : " + arret[0].Message : "")})");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "SocleCourrielsCheck : tous les controles sont verts." : $"SocleCourrielsCheck : {echecs.Count} controle(s) ROUGE(S).");
return echecs.Count == 0 ? 0 : 1;

static string Court(string? texte, int taille) => texte is null ? "" : texte.Length <= taille ? texte : texte[..taille] + "…";

static int PortLibre()
{
    var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
    l.Start();
    var port = ((IPEndPoint)l.LocalEndpoint).Port;
    l.Stop();
    return port;
}

/// <summary>Garde les evenements du journal du serveur (identifiant et message) pour les controles.</summary>
sealed class CapteurJournal : ILoggerProvider
{
    public ConcurrentQueue<(int Id, string Message)> Evenements { get; } = new();
    public void Vider() { while (Evenements.TryDequeue(out _)) { } }
    public ILogger CreateLogger(string categorie) => new Journal(this);
    public void Dispose() { }

    private sealed class Journal(CapteurJournal capteur) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel niveau) => niveau >= LogLevel.Warning;
        public void Log<TState>(LogLevel niveau, EventId id, TState etat, Exception? exception, Func<TState, Exception?, string> format)
        {
            if (IsEnabled(niveau)) capteur.Evenements.Enqueue((id.Id, format(etat, exception)));
        }
    }
}
