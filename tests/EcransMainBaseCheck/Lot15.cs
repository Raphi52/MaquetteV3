using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
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

    public async Task JouerAsync() => await MaquettesAsync();

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
}
