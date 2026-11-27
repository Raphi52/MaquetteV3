using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Numerotation.Compteur;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.EcransMain;
using RigV3Desktop;
using RigV3Desktop.Pont;

// Banc du LOT 15 du plan « RIG v3 100 % fonctionnel » (27/11/2026) : l'ecran fait main que le chef a confie au lot des reglages,
// « Edition des maquettes » (PROC_EDIT_MAQUETTE, LectureMaquettesEdition), joue a la fin de Program.cs sur la meme base jetable et le
// meme serveur en memoire, un fichier a lui. La lecture (types de l'agent du greffe / d'Amitel, ordre du code, parametres par
// PRMED_CODE_MAQUETTE, tables liees des cinq sources de TableLiee.GetTableLiee dedoublonnees), la route par le vrai serveur (l'agent du
// banc n'est pas d'Amitel : Lot20.cs), le pont (base, demonstration, panne). Identifiants : a partir de 2 500 000 ((10 + 15) x 100 000,
// regle du plan) ; codes « L15… ». Types : ceux de RIG_DEV (INFORMATION_SCHEMA de la copie de dev, 27/11/2026). Les tables deja creees
// par Program.cs (CODE_EVE_PRO, TYPE_DECISION, VIDIGED_PARAMETRAGE) recoivent les colonnes lues, NULLABLES.

/// <summary>Ce que Program.cs prete au banc du lot 15.</summary>
internal sealed record BancLot15(Func<string, (string Nom, object? Valeur)[], Task> ExecuterSql, Action<bool, string> Verifier, string Connexion,
                                 ClientServeur Client, HttpClient Http, JsonSerializerOptions Json, string Greffe, string GreffeAbsent)
{
    private async Task<T> SurLaBase<T>(Func<SqlConnection, Task<T>> lire)
    {
        await using var cn = new SqlConnection(Connexion);
        await cn.OpenAsync();
        return await lire(cn);
    }

    private Task Sql(string sql) => ExecuterSql(sql, []);

    private void Exiger(bool ok, string quoi) => Verifier(ok, quoi);

    public async Task JouerAsync()
    {
        await MaquettesAsync();
        await GestesParametresAsync();
    }

    // ============================================================ EDITION DES MAQUETTES (LectureMaquettesEdition.cs)
    // Maquettes : L15_ACTE (RIG, deux parametres), L15_CRYS (CRYSTAL, aucun), L15_REDA (REDA) et L15_XLS (EXCEL) pour Amitel seul,
    // L15_ZZZ (type INCONNU : jamais). Parametres : L15P_A et L15P_B (L15_ACTE, lus dans l'ordre du code), L15P_R (L15_REDA),
    // L15P_O (sans maquette : jamais lu). Tables liees de L15P_A : un code evenement (deux lignes CEP_EDITION au meme code : une seule),
    // un type de decision, un parametrage Vidiged (son code en minuscules : la V2 compare sans casse) ; de L15P_B : un ULT de processus
    // (« EDITPARAM » seul : le jumeau « AUTREPARAM » est ecarte), un courrier judiciaire.
    private async Task MaquettesAsync()
    {
        await Sql("""
            IF OBJECT_ID(N'v2.MAQUETTE') IS NULL
                CREATE TABLE v2.MAQUETTE (MQTT_CODE_MAQUETTE varchar(40) NOT NULL PRIMARY KEY, MQTT_NUM_DOCUMENT varchar(40) NULL,
                    MQTT_LIBELLE varchar(32) NULL, MQTT_EXTENSION varchar(10) NULL, MQTT_TYPE_MAQUETTE varchar(30) NULL,
                    MQTT_ENTETE_FORMAT_PLIEUSE bit NULL);
            IF OBJECT_ID(N'v2.PARAMETRE_EDITION') IS NULL
                CREATE TABLE v2.PARAMETRE_EDITION (PRMED_CODE varchar(40) NOT NULL PRIMARY KEY, PRMED_LIBELLE varchar(100) NOT NULL,
                    PRMED_CODE_PROCESSUS varchar(40) NULL, PRMED_CODE_MAQUETTE varchar(40) NULL, PRMED_IMPRIMANTE varchar(50) NULL,
                    PRMED_RECTO_VERSO bit NULL, PRMED_TYPE_GENERATION varchar(15) NULL, PRMED_NOMBRE_EXEMPLAIRES int NULL, PRMED_IMPRESSION bit NULL,
                    PRMED_AFFICHER_AVANT_IMPRESSION bit NULL, PRMED_READ_ONLY bit NULL, PRMED_AGRAFAGE bit NULL, PRMED_TYPE_PAPIER varchar(30) NULL,
                    PRMED_LOT varchar(30) NULL, PRMED_LOT_ILRAR varchar(30) NULL, PRMED_LOT_LRAR varchar(30) NULL, PRMED_GARDER_EDITION_LOT1 bit NULL,
                    PRMED_REQUETE_SQL varchar(max) NULL, PRMED_NATURE_EDITION varchar(32) NULL, PRMED_LOT_SUBSTITUTION varchar(30) NULL);
            IF OBJECT_ID(N'v2.CEP_EDITION') IS NULL
                CREATE TABLE v2.CEP_EDITION (CEPED_ID_CEPED int IDENTITY NOT NULL PRIMARY KEY, CEPED_CODE_EVE_PRO varchar(10) NOT NULL,
                    CEPED_TYPE_EP varchar(1) NOT NULL, CEPED_CODE_EDITION varchar(20) NOT NULL);
            IF OBJECT_ID(N'v2.ADAPTATEUR_ULT') IS NULL
                CREATE TABLE v2.ADAPTATEUR_ULT (ADPUL_ID_ADPUL int NOT NULL PRIMARY KEY, ADPUL_CODE varchar(30) NULL, ADPUL_LIBELLE varchar(70) NULL,
                    ADPUL_VALEUR_DEFAUT varchar(max) NULL);
            IF OBJECT_ID(N'v2.COURRIER_JUDICIAIRE') IS NULL
                CREATE TABLE v2.COURRIER_JUDICIAIRE (COUJU_ID_COUJU int IDENTITY NOT NULL PRIMARY KEY, COUJU_CODE varchar(20) NOT NULL,
                    COUJU_LIBELLE varchar(100) NOT NULL, COUJU_CODE_PRMED varchar(40) NOT NULL);
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_DEVELOPPE_EDIT') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_DEVELOPPE_EDIT varchar(1000) NULL;
            IF COL_LENGTH('v2.TYPE_DECISION', 'R_TYPEDECISI_PARAMETRE_EDITION') IS NULL ALTER TABLE v2.TYPE_DECISION ADD R_TYPEDECISI_PARAMETRE_EDITION varchar(20) NULL;
            IF COL_LENGTH('v2.VIDIGED_PARAMETRAGE', 'VGPRM_PARAMETRE_EDITION_CFEC') IS NULL ALTER TABLE v2.VIDIGED_PARAMETRAGE ADD VGPRM_PARAMETRE_EDITION_CFEC varchar(20) NULL;
            """);
        await Sql("""
            INSERT INTO v2.MAQUETTE (MQTT_CODE_MAQUETTE, MQTT_LIBELLE, MQTT_EXTENSION, MQTT_TYPE_MAQUETTE, MQTT_ENTETE_FORMAT_PLIEUSE) VALUES
                ('L15_ZZZ', 'Type inconnu', 'html', 'INCONNU', 0), ('L15_XLS', 'Tableau du banc', 'xls', 'EXCEL', 0),
                ('L15_REDA', 'Decision du banc', 'doc', 'REDA', 0), ('L15_CRYS', 'Etat du banc', 'rpt', 'CRYSTAL', NULL),
                ('L15_ACTE', 'Acte du banc', 'html', 'RIG', 1);
            INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_CODE_PROCESSUS, PRMED_CODE_MAQUETTE, PRMED_IMPRIMANTE, PRMED_RECTO_VERSO,
                PRMED_TYPE_GENERATION, PRMED_NOMBRE_EXEMPLAIRES, PRMED_IMPRESSION, PRMED_AFFICHER_AVANT_IMPRESSION, PRMED_READ_ONLY, PRMED_AGRAFAGE,
                PRMED_TYPE_PAPIER, PRMED_LOT, PRMED_LOT_ILRAR, PRMED_LOT_LRAR, PRMED_GARDER_EDITION_LOT1, PRMED_REQUETE_SQL, PRMED_NATURE_EDITION,
                PRMED_LOT_SUBSTITUTION) VALUES
                ('L15P_B', 'Second parametre', 'EDIT_PC', 'L15_ACTE', NULL, 0, 'REPORT_SQL', NULL, 0, 0, 1, 1, NULL, NULL, NULL, NULL, NULL,
                 'SELECT 1', NULL, NULL),
                ('L15P_A', 'Premier parametre', 'EDIT_INS', 'L15_ACTE', 'IMP15', 1, 'FUSION_RIG', 2, 1, 1, 0, 0, 'NORMAL', 'LOT15', 'LOT15I',
                 'LOT15R', 1, NULL, 'COURRIER', 'LOT15S'),
                ('L15P_R', 'Parametre REDA', 'EDIT_DJ', 'L15_REDA', NULL, NULL, NULL, 1, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
                 NULL, NULL),
                ('L15P_O', 'Parametre orphelin', 'EDIT_INS', NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
                 NULL, NULL);
            INSERT INTO v2.CODE_EVE_PRO (R_CODEEVEPRO_VALEUR, R_CODEEVEPRO_LIBELLE_ECRAN, R_CODEEVEPRO_DEVELOPPE_EDIT) VALUES
                ('EV15', 'Evenement 15', 'Evenement du banc 15');
            INSERT INTO v2.CEP_EDITION (CEPED_CODE_EVE_PRO, CEPED_TYPE_EP, CEPED_CODE_EDITION) VALUES ('EV15', 'E', 'L15P_A'), ('EV15', 'P', 'L15P_A');
            INSERT INTO v2.TYPE_DECISION (R_TYPEDECISI_VALEUR, R_TYPEDECISI_LIBELLE_ECRAN, R_TYPEDECISI_PARAMETRE_EDITION) VALUES
                ('TD15', 'Decision du banc 15', 'L15P_A');
            INSERT INTO v2.VIDIGED_PARAMETRAGE (VGPRM_ID_VGPRM, VGPRM_CODE, VGPRM_LIBELLE, VGPRM_PARAMETRE_EDITION_CFEC) VALUES
                (2500501, 'VG15', 'Vidiged du banc 15', 'l15p_a');
            INSERT INTO v2.ADAPTATEUR_ULT (ADPUL_ID_ADPUL, ADPUL_CODE, ADPUL_LIBELLE, ADPUL_VALEUR_DEFAUT) VALUES
                (2500601, 'EDITPARAM', 'Ult du banc 15', 'L15P_B'), (2500602, 'AUTREPARAM', 'Ult ecarte', 'L15P_B');
            INSERT INTO v2.COURRIER_JUDICIAIRE (COUJU_CODE, COUJU_LIBELLE, COUJU_CODE_PRMED) VALUES ('CJ15', 'Courrier du banc 15', 'L15P_B');
            """);

        var greffe = await SurLaBase(cn => LectureMaquettesEdition.LireAsync(cn, agentAmitel: false, CancellationToken.None));
        var miennes = greffe.Lignes.Where(m => m.Code.StartsWith("L15_", StringComparison.Ordinal)).ToArray();
        Exiger(miennes.Select(m => m.Code).SequenceEqual(["L15_ACTE", "L15_CRYS"]),
            $"maquettes, agent du greffe : RIG et CRYSTAL seuls, dans l'ordre du code (OPE_MAQUETTE.cs:83) ({string.Join(",", miennes.Select(m => m.Code))})");
        Exiger(miennes[0] is { Libelle: "Acte du banc", Type: "RIG", Extension: "html", EnteteFormatPlieuse: true }
               && miennes[1] is { Type: "CRYSTAL", Extension: "rpt", EnteteFormatPlieuse: false, Parametres.Length: 0 },
            "maquettes : code, libelle, type, format du document, entete plieuse ; une maquette sans parametre se lit sans parametre");
        var p = miennes[0].Parametres;
        Exiger(p.Select(x => x.Code).SequenceEqual(["L15P_A", "L15P_B"]),
            $"maquettes : les parametres de L15_ACTE (PRMED_CODE_MAQUETTE), dans l'ordre du code ; l'orphelin n'est pas lu ({string.Join(",", p.Select(x => x.Code))})");
        Exiger(p[0] is { Libelle: "Premier parametre", CodeProcessus: "EDIT_INS", NatureEdition: "COURRIER", Imprimante: "IMP15", TypePapier: "NORMAL",
                         Lot: "LOT15", LotLrar: "LOT15R", LotIlrar: "LOT15I", LotSubstitution: "LOT15S", NombreExemplaires: 2, RectoVerso: true,
                         Agrafage: false, AfficherAvantImpression: true, LectureSeule: false, Impression: true, GarderEditionLot1: true,
                         TypeGeneration: "FUSION_RIG", RequeteSql: "" }
               && p[1] is { NombreExemplaires: null, RectoVerso: false, Agrafage: true, LectureSeule: true, TypeGeneration: "REPORT_SQL", RequeteSql: "SELECT 1",
                            Imprimante: "" },
            "maquettes : les champs d'OPE_PARAM_EDT_MAJ, vides ou faux quand la base n'a rien");
        Exiger(p[0].TablesLiees.Select(t => (t.Type, t.Code, t.Libelle)).SequenceEqual(
                   [("CODE EVE PRO", "EV15", "Evenement du banc 15"), ("TYPE DECISION", "TD15", "Decision du banc 15"),
                    ("VIDIGED PARAMETRAGE", "VG15", "Vidiged du banc 15")]),
            $"maquettes : tables liees de L15P_A -- un code evenement (deux lignes CEP_EDITION, une seule), une decision, Vidiged sans casse ({string.Join(",", p[0].TablesLiees.Select(t => t.Code))})");
        Exiger(p[1].TablesLiees.Select(t => (t.Type, t.Code)).SequenceEqual([("ULT DE PROCESSUS", "EDITPARAM"), ("COURRIER JUDICIAIRE", "CJ15")]),
            $"maquettes : tables liees de L15P_B -- l'ULT « EDITPARAM » seul, le courrier judiciaire ({string.Join(",", p[1].TablesLiees.Select(t => t.Type + " " + t.Code))})");

        var amitel = await SurLaBase(cn => LectureMaquettesEdition.LireAsync(cn, agentAmitel: true, CancellationToken.None));
        var vuesAmitel = amitel.Lignes.Where(m => m.Code.StartsWith("L15_", StringComparison.Ordinal)).ToArray();
        Exiger(vuesAmitel.Select(m => m.Code).SequenceEqual(["L15_ACTE", "L15_CRYS", "L15_REDA", "L15_XLS"])
               && vuesAmitel[2].Parametres.Select(x => x.Code).SequenceEqual(["L15P_R"]),
            $"maquettes, agent Amitel : EXCEL et REDA en plus (OPE_MAQUETTE.cs:82), jamais un type inconnu ({string.Join(",", vuesAmitel.Select(m => m.Code))})");
        var une = await SurLaBase(cn => LectureMaquettesEdition.LireAsync(cn, agentAmitel: false, CancellationToken.None, plafond: 1));
        Exiger(une.Lignes.Length == 1 && une.Total == greffe.Total, $"maquettes : plafond 1 -> une ligne, total {greffe.Total} ({une.Total})");

        var route = await Http.GetFromJsonAsync<MaquettesLues>(RoutesEcransMain.Pour(RoutesMaquettesEdition.Maquettes, Greffe), Json);
        Exiger(route is { } r && r.Lignes.Where(m => m.Code.StartsWith("L15_", StringComparison.Ordinal)).Select(m => m.Code).SequenceEqual(["L15_ACTE", "L15_CRYS"]),
            "route des maquettes par le serveur : l'agent du banc n'est pas d'Amitel -> RIG et CRYSTAL seuls");
        Exiger((await Http.GetAsync(RoutesEcransMain.Pour(RoutesMaquettesEdition.Maquettes, GreffeAbsent))).StatusCode == HttpStatusCode.NotFound,
            "route des maquettes : greffe absent -> 404, sans repli");

        var pont = await EcranMaquettesEdition.MaquettesAsync(Client, Greffe);
        var acte = pont.Maquettes.FirstOrDefault(m => m.Code == "L15_ACTE");
        Exiger(pont is { Mode: "serveur", Message: null } && acte is { Libelle: "Acte du banc", Process: "EDIT_INS, EDIT_PC", TypeDoc: "html", TypeMaquette: "RIG",
                                                                       Maj: "", EntetePlieuse: true }
               && acte.Tables.SequenceEqual(["CODE EVE PRO · EV15", "TYPE DECISION · TD15", "VIDIGED PARAMETRAGE · VG15", "ULT DE PROCESSUS · EDITPARAM",
                                             "COURRIER JUDICIAIRE · CJ15"])
               && acte.Parametres[0] is { Code: "L15P_A", NbExemplaires: "2", DocNonModifiable: false, GarderEditionLot1: true }
               && acte.Parametres[1] is { NbExemplaires: "", DocNonModifiable: true },
            "pont Maquettes branche : la forme de l'ecran (processus des parametres, format, type, tables liees « TYPE · code », exemplaires en texte)");
        Exiger((await EcranMaquettesEdition.MaquettesAsync(null, null)) is { Mode: "demo", Maquettes.Length: 0 },
            "pont Maquettes sans serveur : mode « demo », aucune maquette (la demonstration reste celle de l'ecran)");
        Exiger((await EcranMaquettesEdition.MaquettesAsync(Client, GreffeAbsent)) is { Mode: "erreur", Maquettes.Length: 0, Message: not null },
            "pont Maquettes, greffe absent : le motif, AUCUNE maquette");
    }

    // ============================================================ GESTES D'UN PARAMETRE (EcritureParametresEdition.cs)
    // Apres la lecture (ses controles ont lu L15P_A et L15P_B tels qu'inseres). Maquette L15_GES (RIG) et ses parametres L15G_* : file
    // d'Amitel en lot (AMI15) ou en lot LRAR, lot du greffe (LOT15X), EDIT_LDEC sans / avec DECISION_JUD, un parametre libre a supprimer et
    // trois parametres lies chacun a UNE table (Vidiged en minuscules, type de decision, courrier judiciaire). L'agent du banc n'est pas
    // d'Amitel (route) ; le geste d'un agent d'Amitel est joue directement par le socle (EcrivainsEcrans.JouerAsync), comme la route.
    // Chaque refus : 422, la ligne et le journal des gestes inchanges ; chaque refus a son jumeau accepte.
    // fix-ok: pas un correctif a l'aveugle -- ces retouches sont un AJOUT en trois endroits (usings, appel, section) ; la seule correction
    // porte sur deux cases attendues, ecrites d'abord hors de l'ordre du CONCAT de Ligne() (recto, agrafage, impression, affichage,
    // lecture seule, garder lot 1) ; banc vert ensuite, et rouge (2 echecs) quand l'ecriture du XML PRMED_LISTE est retiree.
    private async Task GestesParametresAsync()
    {
        await Sql("""
            IF COL_LENGTH('v2.PARAMETRE_EDITION', 'PRMED_LISTE') IS NULL ALTER TABLE v2.PARAMETRE_EDITION ADD PRMED_LISTE varchar(4000) NULL;
            IF COL_LENGTH('v2.PARAMETRE_EDITION', 'PRMED_CODE_ENTITE_SOURCE') IS NULL ALTER TABLE v2.PARAMETRE_EDITION ADD PRMED_CODE_ENTITE_SOURCE varchar(16) NULL;
            IF OBJECT_ID(N'v2.LOT') IS NULL CREATE TABLE v2.LOT (LOT_NOM varchar(255) NOT NULL PRIMARY KEY);
            """);
        await Sql("""
            INSERT INTO v2.MAQUETTE (MQTT_CODE_MAQUETTE, MQTT_LIBELLE, MQTT_EXTENSION, MQTT_TYPE_MAQUETTE, MQTT_ENTETE_FORMAT_PLIEUSE) VALUES
                ('L15_GES', 'Gestes du banc', 'html', 'RIG', 0);
            INSERT INTO v2.LOT (LOT_NOM) VALUES ('AMI15'), ('LOT15X');
            INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_CODE_PROCESSUS, PRMED_CODE_ENTITE_SOURCE, PRMED_CODE_MAQUETTE,
                PRMED_TYPE_GENERATION, PRMED_NOMBRE_EXEMPLAIRES, PRMED_RECTO_VERSO, PRMED_AGRAFAGE, PRMED_IMPRESSION, PRMED_AFFICHER_AVANT_IMPRESSION,
                PRMED_READ_ONLY, PRMED_GARDER_EDITION_LOT1, PRMED_LOT, PRMED_LOT_LRAR, PRMED_LISTE) VALUES
                ('L15G_AMI', 'Parametre file Amitel', 'EDIT_INS', 'INSTANCE', 'L15_GES', 'FUSION_RIG', 1, 1, 0, 1, 0, 0, 0, 'AMI15', NULL,
                 '<PARAMETRES CODE_PROCESSUS="EDIT_INS" LIBELLE="Ancien libelle" NOMBRE_EXEMPLAIRES="1" RECTO_VERSO="1" />'),
                ('L15G_LRAR', 'Parametre LRAR Amitel', 'EDIT_INS', 'INSTANCE', 'L15_GES', 'FUSION_RIG', 1, 0, 0, 1, 0, 0, 0, 'LOT15X', 'AMI15', ''),
                ('L15G_LOT', 'Parametre lot du greffe', 'EDIT_INS', 'INSTANCE', 'L15_GES', 'REPORT_SQL', 2, 0, 0, 1, 0, 0, 0, 'LOT15X', NULL, ''),
                ('L15G_LDEC', 'Liste sans decision', 'EDIT_LDEC', 'INSTANCE', 'L15_GES', 'FUSION_RIG', 1, 0, 0, 1, 0, 0, 0, NULL, NULL, ''),
                ('L15G_LDEC2', 'Liste des decisions', 'EDIT_LDEC', 'DECISION_JUD', 'L15_GES', 'FUSION_RIG', 1, 0, 0, 1, 0, 0, 0, NULL, NULL, ''),
                ('L15G_DEL', 'Parametre libre', 'EDIT_INS', 'INSTANCE', 'L15_GES', 'FUSION_RIG', 1, 0, 0, 1, 0, 0, 0, NULL, NULL, ''),
                ('L15G_V', 'Lie a Vidiged', 'EDIT_INS', 'INSTANCE', 'L15_GES', 'FUSION_RIG', 1, 0, 0, 1, 0, 0, 0, NULL, NULL, ''),
                ('L15G_T', 'Lie a une decision', 'EDIT_INS', 'INSTANCE', 'L15_GES', 'FUSION_RIG', 1, 0, 0, 1, 0, 0, 0, NULL, NULL, ''),
                ('L15G_C', 'Lie a un courrier', 'EDIT_INS', 'INSTANCE', 'L15_GES', 'FUSION_RIG', 1, 0, 0, 1, 0, 0, 0, NULL, NULL, '');
            INSERT INTO v2.VIDIGED_PARAMETRAGE (VGPRM_ID_VGPRM, VGPRM_CODE, VGPRM_LIBELLE, VGPRM_PARAMETRE_EDITION_CFEC) VALUES
                (2500503, 'VG15G', 'Vidiged des gestes', 'l15g_v');
            INSERT INTO v2.TYPE_DECISION (R_TYPEDECISI_VALEUR, R_TYPEDECISI_LIBELLE_ECRAN, R_TYPEDECISI_PARAMETRE_EDITION) VALUES
                ('TD15G', 'Decision des gestes', 'L15G_T');
            INSERT INTO v2.COURRIER_JUDICIAIRE (COUJU_CODE, COUJU_LIBELLE, COUJU_CODE_PRMED) VALUES ('CJ15G', 'Courrier des gestes', 'L15G_C');
            """);

        var nl = Environment.NewLine;
        var route = RoutesEcransMain.Pour(RoutesMaquettesEdition.Parametre, Greffe);
        async Task<(HttpStatusCode Statut, ResultatGeste? R)> Route(TypeGeste type, string code, Dictionary<string, string> valeurs)
        {
            var reponse = await Http.PostAsJsonAsync(route, new GesteEcran(type, [code], valeurs), Json);
            return (reponse.StatusCode, await reponse.Content.ReadFromJsonAsync<ResultatGeste>(Json));
        }
        async Task<ResultatGeste> Amitel(TypeGeste type, string code, Dictionary<string, string> valeurs)
        {
            await using var cn = new SqlConnection(Connexion);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT Id FROM soc.Compte WHERE CompteWindows = 'banc.main'", cn);
            var moi = new Appelant((int)(await cmd.ExecuteScalarAsync())!, "banc.main", "Banc Amitel", EstAmitel: true, MarqueAdministrateur: false,
                                   GereLesDroits: false);
            return await EcrivainsEcrans.JouerAsync(EcritureParametresEdition.Ecran, "BancLot15", (g, t) => EcritureParametresEdition.JouerAsync(g, true, t),
                cn, new GesteEcran(type, [code], valeurs), moi, DateTime.Now, new DistributeurNumeros(), CancellationToken.None);
        }
        Task<string?> Ligne(string code) => SurLaBase(async cn =>
        {
            await using var cmd = new SqlCommand("""
                SELECT CONCAT(PRMED_LIBELLE, '|', PRMED_NOMBRE_EXEMPLAIRES, '|', CAST(PRMED_RECTO_VERSO AS int), CAST(PRMED_AGRAFAGE AS int),
                    CAST(PRMED_IMPRESSION AS int), CAST(PRMED_AFFICHER_AVANT_IMPRESSION AS int), CAST(PRMED_READ_ONLY AS int),
                    CAST(PRMED_GARDER_EDITION_LOT1 AS int), '|', PRMED_LISTE)
                FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = @c
                """, cn);
            cmd.Parameters.AddWithValue("@c", code);
            return (string?)await cmd.ExecuteScalarAsync();
        });
        Task<int> Journal() => SurLaBase(async cn =>
        {
            await using var cmd = new SqlCommand("SELECT COUNT(*) FROM soc.Journal", cn);
            return (int)(await cmd.ExecuteScalarAsync())!;
        });
        var E = EcritureParametresEdition.ChampExemplaires;
        var L = EcritureParametresEdition.ChampLibelle;
        // Un refus PAR LA ROUTE : 422, son message au mot pres, la ligne et le journal inchanges.
        async Task RefusRoute(string quoi, TypeGeste type, string code, Dictionary<string, string> valeurs, string message)
        {
            var (avant, journal) = (await Ligne(code), await Journal());
            var (statut, r) = await Route(type, code, valeurs);
            Exiger(statut == HttpStatusCode.UnprocessableEntity && r is { Fait: false } && r.Message == message
                   && await Ligne(code) == avant && await Journal() == journal,
                $"parametre d'edition, {quoi} : refuse (422) au message de RIG v2, rien d'ecrit ({(int)statut} « {r?.Message} »)");
        }
        async Task RefusAmitel(string quoi, TypeGeste type, string code, Dictionary<string, string> valeurs, string message)
        {
            var (avant, journal) = (await Ligne(code), await Journal());
            var r = await Amitel(type, code, valeurs);
            Exiger(r is { Fait: false } && r.Message == message && await Ligne(code) == avant && await Journal() == journal,
                $"parametre d'edition, {quoi} (agent d'Amitel) : refuse au message de RIG v2, rien d'ecrit (« {r.Message} »)");
        }

        // ENREGISTRER -- DoVerifier (OPE_PARAM_EDT_MAJ.cs:322-326) : file d'Amitel (lot ou lot LRAR « AMI… ») -> 1 exemplaire.
        const string MessageAmitel = "Le nombre d'exemplaires doit être à 1 si on veut imprimer à AMITEL. Pensez à prévenir le greffe.";
        await RefusRoute("2 exemplaires, lot AMI15", TypeGeste.Enregistrer, "L15G_AMI", new() { [E] = "2" }, MessageAmitel);
        await RefusRoute("2 exemplaires, lot LRAR AMI15", TypeGeste.Enregistrer, "L15G_LRAR", new() { [E] = "2" }, MessageAmitel);
        // Jumeau : un lot du greffe (LOT15X) -> 3 exemplaires ecrits ; la colonne et la cle NOMBRE_EXEMPLAIRES du XML (vide : creee).
        var j0 = await Journal();
        var (s3, r3) = await Route(TypeGeste.Enregistrer, "L15G_LOT", new() { [E] = "3" });
        Exiger(s3 == HttpStatusCode.OK && r3 is { Fait: true, Message: "Paramètre d'édition L15G_LOT enregistré." }
               && await Ligne("L15G_LOT") == "Parametre lot du greffe|3|001000|<PARAMETRES NOMBRE_EXEMPLAIRES=\"3\" />" && await Journal() == j0 + 1,
            $"parametre d'edition, lot du greffe : 3 exemplaires ecrits, colonne ET XML PRMED_LISTE, une ligne au journal ({await Ligne("L15G_LOT")})");
        // Jumeau du lot AMI15 : 1 exemplaire garde ; libelle, agrafage et recto-verso changes. Le XML : LIBELLE retire (la V2 le lit
        // par-dessus la colonne), RECTO_VERSO change sur place, AGRAFAGE ajoute a la fin ; CODE_PROCESSUS et NOMBRE_EXEMPLAIRES intacts.
        var (sA, rA) = await Route(TypeGeste.Enregistrer, "L15G_AMI",
            new() { [L] = "  Nouveau libelle ", [EcritureParametresEdition.ChampAgrafage] = "Oui", [EcritureParametresEdition.ChampRectoVerso] = "Non",
                    [E] = "1" });
        Exiger(sA == HttpStatusCode.OK && rA is { Fait: true }
               && await Ligne("L15G_AMI") == "Nouveau libelle|1|011000|<PARAMETRES CODE_PROCESSUS=\"EDIT_INS\" NOMBRE_EXEMPLAIRES=\"1\" RECTO_VERSO=\"0\" AGRAFAGE=\"1\" />",
            $"parametre d'edition, lot AMI15 a 1 exemplaire : libelle (blancs retires) et cases ecrits, XML mis a jour cle par cle ({await Ligne("L15G_AMI")})");

        // Champs obligatoires (Designer :226-236) et nombre en chiffres (MetierRegExSaisie « [0-9] », :237).
        await RefusRoute("nombre d'exemplaires vide", TypeGeste.Enregistrer, "L15G_LOT", new() { [E] = " " }, "« Nb exemplaires » est obligatoire.");
        await RefusRoute("0 exemplaire", TypeGeste.Enregistrer, "L15G_LOT", new() { [E] = "0" }, "Le nombre d'exemplaires est obligatoire.");
        await RefusRoute("exemplaires « 2a »", TypeGeste.Enregistrer, "L15G_LOT", new() { [E] = "2a" }, "« Nb exemplaires » : des chiffres seuls (2a).");
        await RefusRoute("libelle vide", TypeGeste.Enregistrer, "L15G_LOT", new() { [L] = "" }, "« Libellé paramètre édition » est obligatoire.");
        await RefusRoute("libelle de 101 caracteres", TypeGeste.Enregistrer, "L15G_LOT", new() { [L] = new string('x', 101) },
            "« Libellé paramètre édition » : 100 caractères au plus.");
        var (s100, r100) = await Route(TypeGeste.Enregistrer, "L15G_LOT", new() { [L] = new string('y', 100) });
        Exiger(s100 == HttpStatusCode.OK && r100 is { Fait: true } && (await Ligne("L15G_LOT"))!.StartsWith(new string('y', 100) + "|3|", StringComparison.Ordinal),
            "parametre d'edition, libelle de 100 caracteres (jumeau de 101) : ecrit");

        // EDIT_LDEC sans DECISION_JUD (:314-316), sur la valeur lue : refuse quel que soit le champ change ; jumeau avec DECISION_JUD.
        await RefusRoute("EDIT_LDEC sans DECISION_JUD", TypeGeste.Enregistrer, "L15G_LDEC", new() { [EcritureParametresEdition.ChampImpression] = "Non" },
            "Si vous saisissez EDIT_LDEC en code processus vous devez saisir DECISION_JUD en code entité source.");
        var (sL, rL) = await Route(TypeGeste.Enregistrer, "L15G_LDEC2", new() { [EcritureParametresEdition.ChampImpression] = "Non" });
        Exiger(sL == HttpStatusCode.OK && rL is { Fait: true } && (await Ligne("L15G_LDEC2"))!.Contains("|000000|", StringComparison.Ordinal),
            $"parametre d'edition, EDIT_LDEC avec DECISION_JUD : la case Impression ecrite ({await Ligne("L15G_LDEC2")})");

        // « Garder edition lot 1 » (:140-141) : agent du greffe refuse ; Amitel sur une fusion RIG ecrit (colonne seule : hors des cles du
        // XML, ParametreEdition.cs:270-300) ; Amitel sur un REPORT_SQL refuse.
        const string MessageGarder = "« Garder edition lot 1 » ne se montre qu'à un agent d'Amitel, pour une génération FUSION_RIG (RIG v2) : rien n'est enregistré.";
        await RefusRoute("Garder edition lot 1, agent du greffe", TypeGeste.Enregistrer, "L15P_A", new() { [EcritureParametresEdition.ChampGarderLot1] = "Non" }, MessageGarder);
        var rG = await Amitel(TypeGeste.Enregistrer, "L15P_A", new() { [EcritureParametresEdition.ChampGarderLot1] = "Non" });
        Exiger(rG is { Fait: true } && (await Ligne("L15P_A"))!.EndsWith("101100|", StringComparison.Ordinal),
            $"parametre d'edition, Garder edition lot 1 par Amitel, fusion RIG : colonne ecrite, XML vide laisse vide ({await Ligne("L15P_A")})");
        await RefusAmitel("Garder edition lot 1, REPORT_SQL", TypeGeste.Enregistrer, "L15P_B", new() { [EcritureParametresEdition.ChampGarderLot1] = "Oui" }, MessageGarder);

        // Une maquette REDA (:210) : l'agent du greffe ne la voit pas (introuvable), Amitel la lit sans la modifier.
        await RefusRoute("maquette REDA, agent du greffe", TypeGeste.Enregistrer, "L15P_R", new() { [E] = "2" },
            "Paramètre d'édition introuvable dans la base du greffe (L15P_R).");
        await RefusAmitel("maquette REDA", TypeGeste.Enregistrer, "L15P_R", new() { [E] = "2" },
            "Les paramètres d'une maquette REDA se lisent sans se modifier (RIG v2 : formulaire en lecture seule).");
        // Un champ que la V3 n'ecrit pas, rien de change, un code absent, un geste que l'ecran n'a pas.
        await RefusRoute("champ Imprimante", TypeGeste.Enregistrer, "L15G_LOT", new() { ["Imprimante"] = "IMP15" },
            "« Imprimante » ne s'enregistre pas encore dans RIG v3 : rien n'est enregistré.");
        await RefusRoute("aucun champ change", TypeGeste.Enregistrer, "L15G_LOT", new() { [E] = "3" },
            "Paramètre d'édition L15G_LOT : aucun champ n'a changé, rien n'est enregistré.");
        await RefusRoute("code absent", TypeGeste.Enregistrer, "L15G_NUL", new() { [E] = "1" }, "Paramètre d'édition introuvable dans la base du greffe (L15G_NUL).");
        var (sM, rM) = await Route(TypeGeste.Modifier, "L15G_LOT", new() { [E] = "5" });
        Exiger(sM == HttpStatusCode.OK && rM is { Fait: false, SansEcrivain: true } && (await Ligne("L15G_LOT"))!.Contains("|3|", StringComparison.Ordinal),
            "parametre d'edition, geste Modifier : l'ecran n'en a pas, rien d'ecrit (SansEcrivain)");

        // SUPPRIMER -- reserve a Amitel (:111) ; CanSupp (:259-307), une table a la fois, dans l'ordre de la V2.
        await RefusRoute("supprimer, agent du greffe", TypeGeste.Supprimer, "L15G_DEL", [],
            "Seul un agent d'Amitel supprime un paramètre d'édition (RIG v2 : bouton Supprimer grisé pour le greffe).");
        var jD = await Journal();
        var rD = await Amitel(TypeGeste.Supprimer, "L15G_DEL", []);
        Exiger(rD is { Fait: true, Message: "Paramètre d'édition L15G_DEL supprimé." } && await Ligne("L15G_DEL") is null && await Journal() == jD + 1,
            "parametre d'edition, supprimer par Amitel un parametre libre : ligne supprimee, une ligne au journal");
        // Deux lignes CEP_EDITION EV15 : « (EV15 - EV15 ) », au caractere pres de la V2 (:268-274).
        await RefusAmitel("supprimer, lie a un code evenement", TypeGeste.Supprimer, "L15P_A", [],
            $"Le code param L15P_A est lié aux évènements suivants : (EV15 - EV15 ).{nl}Vous devez supprimer le ou les liens dans la table CODE_EVE_PRO de l'outil des tables de référence.");
        await RefusAmitel("supprimer, lie a un ULT de processus", TypeGeste.Supprimer, "L15P_B", [],
            $"Le code param L15P_B est utilisé dans les processus.{nl}Vous ne pouvez pas le supprimer.");
        await RefusAmitel("supprimer, lie a Vidiged (sans casse)", TypeGeste.Supprimer, "L15G_V", [],
            $"Le code param L15G_V est utilisé dans le paramétrage de vidiged.{nl}Vous ne pouvez pas le supprimer.");
        await RefusAmitel("supprimer, lie a un type de decision", TypeGeste.Supprimer, "L15G_T", [],
            $"Le code param L15G_T est utilisé dans les types decisions.{nl}Vous ne pouvez pas le supprimer.");
        await RefusAmitel("supprimer, lie a un courrier judiciaire", TypeGeste.Supprimer, "L15G_C", [],
            $"Le code param L15G_C est utilisé dans les courriers judiciaires.{nl}Vous ne pouvez pas le supprimer.");

        // LE PONT (EcranMaquettesEdition.ParametreAsync) : ecrit, refus de RIG v2 (422) au mot pres, demonstration sans serveur ; relu.
        var pEcrit = await EcranMaquettesEdition.ParametreAsync(Client, Greffe, new GesteEcran(TypeGeste.Enregistrer, ["L15G_LOT"], new Dictionary<string, string> { [E] = "4" }));
        var pRefus = await EcranMaquettesEdition.ParametreAsync(Client, Greffe, new GesteEcran(TypeGeste.Enregistrer, ["L15G_LOT"], new Dictionary<string, string> { [E] = "0" }));
        var pDemo = await EcranMaquettesEdition.ParametreAsync(null, null, new GesteEcran(TypeGeste.Enregistrer, ["L15G_LOT"], new Dictionary<string, string> { [E] = "5" }));
        var relue = (await EcranMaquettesEdition.MaquettesAsync(Client, Greffe)).Maquettes.FirstOrDefault(m => m.Code == "L15_GES")?.Parametres
            .FirstOrDefault(x => x.Code == "L15G_LOT");
        Exiger(pEcrit is { Fait: true } && pRefus is { Fait: false, SansEcrivain: false, Message: "Le nombre d'exemplaires est obligatoire." }
               && pDemo is { Fait: false, SansEcrivain: true } && pDemo.Message.StartsWith(EcranGenere.Demonstration, StringComparison.Ordinal)
               && relue is { NbExemplaires: "4" },
            $"pont Parametre : ecrit puis relu (4 exemplaires), refus de RIG v2 au mot pres, demonstration sans serveur ({pRefus.Message} / {relue?.NbExemplaires})");
        Exiger((await EcranMaquettesEdition.MaquettesAsync(Client, Greffe)) is { AgentAmitel: false },
            "pont Maquettes : l'agent du banc n'est pas d'Amitel (Supprimer cache a l'ecran)");
    }
}
