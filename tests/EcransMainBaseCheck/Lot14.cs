using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransMain;
using RigV3Desktop;
using RigV3Desktop.Pont;

// Banc du LOT 14 du plan « RIG v3 100 % fonctionnel » (27/11/2026) : les quatre ecrans faits main que le chef a confies au lot des
// acteurs et referentiels, joues a la fin de Program.cs sur la meme base jetable et le meme serveur en memoire, un fichier a lui :
//   A. « Acteurs » (LectureActeurs) : les six types de RIG v2 et leurs champs, une table absente ne rend rien ;
//   B. « Experts » (LectureExperts) : les experts et leurs mesures (PARTICIPANT joint a MESURE), en cours ou terminees ;
//   C. « Clients » (LectureClients.LireAsync) : CLIEN_TYPE = 'C', le libelle du groupe (INNER JOIN CLIENT_GROUPE_REF), le type V2 ;
//   D. « Fiche client » (LectureClients.FicheAsync) : identification, parametrage, informations, six derniers soldes, contacts.
// Chaque section : la lecture (lignes du banc reconnues par leur numero : d'autres sections ont rempli les memes tables), la route par
// le vrai serveur, le pont (base, demonstration, panne). Identifiants : a partir de 2 400 000 ((10 + 14) x 100 000, regle du plan).
// Les tables deja creees par Program.cs (JUGE, GREFFIER_ASSERMENTE, MESURE, FACTURE, VENTILATION, ENCAISSEMENT, CLIENT_SOLDE) et Lot20.cs
// (MANDATAIRE_PC) recoivent les colonnes que les lectures lisent, NULLABLES (types de RIG_DEV : RigBaseGreffe\GREFFE\*.cs).

/// <summary>Ce que Program.cs prete au banc du lot 14.</summary>
internal sealed record BancLot14(Func<string, (string Nom, object? Valeur)[], Task> ExecuterSql, Action<bool, string> Verifier, string Connexion,
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

    /// <summary>« IF ... CREATE TABLE » (cle seule) puis une colonne NULLABLE par (nom, type) absente.</summary>
    private static string Table(string table, string cle, params (string Colonne, string Type)[] colonnes)
        => $"IF OBJECT_ID(N'v2.{table}') IS NULL CREATE TABLE v2.{table} ({cle} int NOT NULL PRIMARY KEY);\n"
           + string.Concat(colonnes.Select(c => $"IF COL_LENGTH('v2.{table}', '{c.Colonne}') IS NULL ALTER TABLE v2.{table} ADD {c.Colonne} {c.Type} NULL;\n"));

    public async Task JouerAsync()
    {
        await ActeursAsync();
        await ExpertsAsync();
        await ClientsAsync();
    }

    // ============================================================ A. ACTEURS (LectureActeurs.cs)
    // 2400501 juge (adresse rec.Adresse, chambre, dates), 2400502 mandataire PC inactif (coffre, SIREN), 2400503 parquetier, 2400504 greffier
    // (compte V2 77), 2400505 intervenant PC (sans colonne « actif » : toujours actif), 2400506 expert.
    private async Task ActeursAsync()
    {
        await Sql(Table("JUGE", "JUG_ID_JUG", ("JUG_ABREGE", "varchar(16)"), ("JUG_CIVILITE", "varchar(7)"), ("JUG_NOM", "varchar(35)"),
                      ("JUG_PRENOM", "varchar(20)"), ("JUG_STATUT", "varchar(5)"), ("JUG_CHAMBRE", "varchar(5)"), ("JUG_SECTION", "varchar(5)"),
                      ("JUG_ADRESSE_MAIL", "varchar(70)"), ("JUG_ACTIF", "bit"), ("JUG_ID_ADRBS", "int"), ("JUG_TELEPHONE", "varchar(15)"),
                      ("JUG_FAX", "varchar(15)"), ("JUG_DATE_DEBUT", "datetime"), ("JUG_DATE_FIN", "datetime"), ("JUG_BUREAU", "varchar(20)"))
                + Table("MANDATAIRE_PC", "MNDPC_ID_MNDPC", ("MNDPC_ID_ADRBS", "int"), ("MNDPC_CIVILITE", "varchar(7)"), ("MNDPC_NOM", "varchar(100)"),
                      ("MNDPC_COMPLEMENT_NOM", "varchar(100)"), ("MNDPC_ABREGE", "varchar(10)"), ("MNDPC_QUALIF_PROFESSION", "varchar(5)"),
                      ("MNDPC_ACTIF", "bit"), ("MNDPC_ADRESSE_MAIL", "varchar(70)"), ("MNDPC_TELEPHONE", "varchar(15)"), ("MNDPC_FAX", "varchar(15)"),
                      ("MNDPC_NOM_COFFRE", "varchar(50)"), ("MNDPC_SIREN", "varchar(9)"))
                + Table("PARQUETIER", "PRQTR_ID_PRQTR", ("PRQTR_ABREGE", "varchar(16)"), ("PRQTR_CIVILITE", "varchar(7)"), ("PRQTR_NOM", "varchar(35)"),
                      ("PRQTR_PRENOM", "varchar(20)"), ("PRQTR_STATUT", "varchar(5)"), ("PRQTR_ADRESSE_MAIL", "varchar(70)"), ("PRQTR_ACTIF", "bit"),
                      ("PRQTR_ID_ADRBS", "int"), ("PRQTR_TELEPHONE", "varchar(15)"), ("PRQTR_FAX", "varchar(15)"))
                + Table("GREFFIER_ASSERMENTE", "GRASS_ID_GRASS", ("GRASS_ABREGE", "varchar(16)"), ("GRASS_CIVILITE", "varchar(7)"),
                      ("GRASS_NOM", "varchar(35)"), ("GRASS_PRENOM", "varchar(20)"), ("GRASS_STATUT", "varchar(5)"), ("GRASS_ADRESSE_MAIL", "varchar(70)"),
                      ("GRASS_TELEPHONE", "varchar(15)"), ("GRASS_FAX", "varchar(15)"), ("GRASS_ACTIF", "bit"), ("GRASS_ID_UTLST", "int"))
                + Table("INTERVENANT_PC_REPER", "INTRPR_ID_INTRPR", ("INTRPR_ABREGE", "varchar(16)"), ("INTRPR_CIVILITE", "varchar(7)"),
                      ("INTRPR_NOM", "varchar(70)"), ("INTRPR_COMPLEMENT_NOM", "varchar(70)"), ("INTRPR_QUALIF_PROFESSION", "varchar(5)"),
                      ("INTRPR_ID_ADRBS", "int"), ("INTRPR_ADRESSE_MAIL", "varchar(70)"), ("INTRPR_TELEPHONE", "varchar(15)"), ("INTRPR_FAX", "varchar(15)"),
                      ("INTRPR_NOM_COFFRE", "varchar(50)"), ("INTRPR_DATE_DEMARRAGE_COFFRE", "datetime"))
                + Table("EXPERT", "EXPRT_ID_EXPRT", ("EXPRT_ID_ADRBS", "int"), ("EXPRT_ABREGE", "varchar(16)"), ("EXPRT_CIVILITE", "varchar(7)"),
                      ("EXPRT_NOM", "varchar(70)"), ("EXPRT_COMPLEMENT_NOM", "varchar(70)"), ("EXPRT_QUALIF_PROFESSION", "varchar(5)"),
                      ("EXPRT_DATE_NAISSANCE", "datetime"), ("EXPRT_ADRESSE_MAIL", "varchar(70)"), ("EXPRT_TELEPHONE", "varchar(15)"),
                      ("EXPRT_FAX", "varchar(15)"), ("EXPRT_EST_ACTIF", "bit")));
        await Sql("""
            DECLARE @adresse int;
            INSERT INTO rec.Adresse (NumeroVoie, TypeVoie, NomVoie, CodePostal, Localite, EtatInfo) VALUES ('3', 'RUE', 'DU BANC ACTEUR', '38000', 'GRENOBLE', '1');
            SET @adresse = SCOPE_IDENTITY();
            INSERT INTO v2.JUGE (JUG_ID_JUG, JUG_ABREGE, JUG_CIVILITE, JUG_NOM, JUG_PRENOM, JUG_STATUT, JUG_CHAMBRE, JUG_SECTION, JUG_ACTIF, JUG_ID_ADRBS,
                                 JUG_TELEPHONE, JUG_DATE_DEBUT, JUG_DATE_FIN, JUG_BUREAU)
            VALUES (2400501, 'L14JUG', 'MME', 'ACTEURJUGE', 'Rose', 'T', '2', 'B', 1, @adresse, '0476000001', '20200105', NULL, 'B-14');
            INSERT INTO v2.MANDATAIRE_PC (MNDPC_ID_MNDPC, MNDPC_CIVILITE, MNDPC_NOM, MNDPC_COMPLEMENT_NOM, MNDPC_ABREGE, MNDPC_ACTIF, MNDPC_NOM_COFFRE, MNDPC_SIREN)
            VALUES (2400502, 'ME', 'ACTEURMANDA', 'Selarl', 'L14MPC', 0, 'COFFRE_L14', '240050200');
            INSERT INTO v2.PARQUETIER (PRQTR_ID_PRQTR, PRQTR_ABREGE, PRQTR_CIVILITE, PRQTR_NOM, PRQTR_PRENOM, PRQTR_STATUT, PRQTR_ACTIF)
            VALUES (2400503, 'L14PRQ', 'MR', 'ACTEURPARQUET', 'Paul', 'S', 1);
            INSERT INTO v2.GREFFIER_ASSERMENTE (GRASS_ID_GRASS, GRASS_ABREGE, GRASS_CIVILITE, GRASS_NOM, GRASS_PRENOM, GRASS_ACTIF, GRASS_ID_UTLST)
            VALUES (2400504, 'L14GRF', 'MME', 'ACTEURGREFFE', 'Ines', 1, 77);
            INSERT INTO v2.INTERVENANT_PC_REPER (INTRPR_ID_INTRPR, INTRPR_ABREGE, INTRPR_CIVILITE, INTRPR_NOM, INTRPR_QUALIF_PROFESSION, INTRPR_NOM_COFFRE)
            VALUES (2400505, 'L14INT', 'ME', 'ACTEURINTERV', 'HUI', 'COFFRE_INT');
            INSERT INTO v2.EXPERT (EXPRT_ID_EXPRT, EXPRT_ABREGE, EXPRT_CIVILITE, EXPRT_NOM, EXPRT_QUALIF_PROFESSION, EXPRT_DATE_NAISSANCE, EXPRT_EST_ACTIF)
            VALUES (2400506, 'L14EXP', 'MR', 'ACTEUREXPERT', 'EC', '19700514', 1), (2400507, 'L14EXO', 'MR', 'ACTEURRETIRE', 'EC', NULL, 0);
            """);
        var lus = await SurLaBase(cn => LectureActeurs.LireAsync(cn, CancellationToken.None));
        ActeurLu? Un(string type, int id) => lus.Lignes.FirstOrDefault(a => a.Type == type && a.Id == id);
        Exiger(Un("Juge", 2400501) is { Abrege: "L14JUG", Civilite: "MME", Nom: "ACTEURJUGE", Prenom: "Rose", Statut: "T", Chambre: "2", Section: "B",
                                        Debut: "2020-01-05", Fin: null, Bureau: "B-14", Actif: true, Telephone: "0476000001" } j
               && j.Adresse == "3 RUE DU BANC ACTEUR 38000 GRENOBLE",
            $"Acteurs : le juge et ses champs de JUGE, son adresse rec.Adresse ({Un("Juge", 2400501)})");
        Exiger(Un("Mandataire", 2400502) is { Nom: "ACTEURMANDA", ComplementNom: "Selarl", Actif: false, NomCoffre: "COFFRE_L14", Siren: "240050200" },
            "Acteurs : le mandataire PC inactif, son coffre et son SIREN (MANDATAIRE_PC)");
        Exiger(Un("Parquetier", 2400503) is { Statut: "S", Actif: true } && Un("Greffier", 2400504) is { Utilisateur: "77", Prenom: "Ines" },
            "Acteurs : le parquetier et son statut ; le greffier et son compte V2 (GRASS_ID_UTLST)");
        Exiger(Un("Intervenant", 2400505) is { Actif: true, Qualification: "HUI", NomCoffre: "COFFRE_INT" } && Un("Expert", 2400507) is { Actif: false },
            "Acteurs : l'intervenant PC toujours actif (aucune colonne « actif » en V2) ; l'expert inactif reste lu (l'ecran filtre)");
        var types = lus.Lignes.Select(a => a.Type).Distinct().ToArray();
        Exiger(types.SequenceEqual(["Juge", "Mandataire", "Parquetier", "Greffier", "Intervenant", "Expert"]) && lus.Total == lus.Lignes.Length,
            $"Acteurs : les six types dans l'ordre de RIG v2 ({string.Join(", ", types)})");
        var juges = lus.Lignes.Where(a => a.Type == "Juge").Select(a => a.Abrege).ToArray();
        Exiger(juges.SequenceEqual(juges.Order(StringComparer.Ordinal)), "Acteurs : chaque type par abrege (OPE_RECHERCHE_ACTEUR.cs:481)");
        Exiger((await Http.GetFromJsonAsync<ActeursLus>(RoutesEcransMain.Pour(RoutesActeurs.Acteurs, Greffe), Json)) is { } route
               && route.Lignes.Any(a => a.Id == 2400506 && a.Type == "Expert"),
            "route Acteurs par le serveur : les acteurs de la base");
        var pont = await EcranActeurs.ActeursAsync(Client, Greffe);
        Exiger(pont is { Mode: "serveur" } && pont.Acteurs.FirstOrDefault(a => a.Cle == "Juge-2400501") is { Debut: "2020-01-05", Chambre: "2" }
               && pont.Acteurs.Count(a => a.Abrege.StartsWith("L14", StringComparison.Ordinal)) == 7,
            "pont Acteurs branche : chaque acteur et sa cle « type-numero »");
        Exiger((await EcranActeurs.ActeursAsync(null, null)) is { Mode: "demo", Acteurs.Length: 0 },
            "pont Acteurs sans serveur : mode « demo », aucun acteur (l'ecran garde ses exemples, marques)");
        Exiger((await EcranActeurs.ActeursAsync(Client, GreffeAbsent)) is { Mode: "erreur", Acteurs.Length: 0, Message: not null },
            "pont Acteurs, greffe absent : le motif, AUCUN acteur");
    }

    // ============================================================ B. EXPERTS (LectureExperts.cs)
    // L'expert 2400506 : participant ACTIF a la mesure ouverte 2400601 (en cours), participant a la mesure CLOTUREE 2400602 (terminee) ;
    // le participant 2400613 cite un autre expert (2400507) : il n'est pas dans les mesures de 2400506.
    private async Task ExpertsAsync()
    {
        await Sql(Table("MESURE", "MSINS_ID_MSINS", ("MSINS_NUM_MESURE", "varchar(11)"), ("MSINS_DATE_CLOTURE", "datetime"))
                + Table("PARTICIPANT", "PRTCP_ID_PRTCP", ("PRTCP_ID_MSINS", "int"), ("PRTCP_ID_EXPRT", "int"), ("PRTCP_ID_ADRBS", "int"),
                      ("PRTCP_CIVILITE", "varchar(7)"), ("PRTCP_NOM", "varchar(70)"), ("PRTCP_COMPLEMENT_NOM", "varchar(70)"), ("PRTCP_ACTIF", "bit"),
                      ("PRTCP_QUALIF_PROFESSION", "varchar(5)")));
        await Sql("""
            INSERT INTO v2.MESURE (MSINS_ID_MSINS, MSINS_NUM_MESURE, MSINS_DATE_CLOTURE) VALUES (2400601, 'M14OUV', NULL), (2400602, 'M14CLO', '20250101');
            INSERT INTO v2.PARTICIPANT (PRTCP_ID_PRTCP, PRTCP_ID_MSINS, PRTCP_ID_EXPRT, PRTCP_CIVILITE, PRTCP_NOM, PRTCP_COMPLEMENT_NOM, PRTCP_ACTIF, PRTCP_QUALIF_PROFESSION)
            VALUES (2400611, 2400601, 2400506, 'MR', 'ACTEUREXPERT', 'Cabinet', 1, 'EC'), (2400612, 2400602, 2400506, 'MR', 'ACTEUREXPERT', NULL, 1, 'EC'),
                   (2400613, 2400601, 2400507, 'MR', 'ACTEURRETIRE', NULL, 1, 'EC');
            """);
        var lus = await SurLaBase(cn => LectureExperts.LireAsync(cn, CancellationToken.None));
        Exiger(lus.Experts.FirstOrDefault(e => e.Id == 2400506) is { Abrege: "L14EXP", Naissance: "14/05/1970", Actif: true, Qualification: "EC" }
               && lus.Experts.FirstOrDefault(e => e.Id == 2400507) is { Actif: false },
            "Experts : la fiche de l'expert (naissance jj/mm/aaaa) ; l'inactif reste lu (l'ecran filtre)");
        var mesures = lus.Mesures.Where(m => m.IdExpert == 2400506).Select(m => $"{m.NumMesure}:{(m.EnCours ? "en cours" : "terminee")}").ToArray();
        Exiger(mesures.SequenceEqual(["M14CLO:terminee", "M14OUV:en cours"]) && lus.Mesures.Any(m => m.IdExpert == 2400507 && m.NumMesure == "M14OUV"),
            $"Experts : ses mesures (PARTICIPANT joint a MESURE), en cours si active et non cloturee ; le participant d'un autre expert lui reste ({string.Join(", ", mesures)})");
        Exiger((await Http.GetFromJsonAsync<ExpertsLus>(RoutesEcransMain.Pour(RoutesExperts.Experts, Greffe), Json)) is { } route
               && route.Experts.Any(e => e.Id == 2400506),
            "route Experts par le serveur : les experts de la base");
        var pont = await EcranExperts.ExpertsAsync(Client, Greffe);
        Exiger(pont is { Mode: "serveur" } && pont.Experts.FirstOrDefault(e => e.Cle == "2400506") is { Nom: "ACTEUREXPERT" }
               && pont.Mesures.Count(m => m.Expert == "2400506") == 2,
            "pont Experts branche : l'expert par son numero, ses deux mesures");
        Exiger((await EcranExperts.ExpertsAsync(null, null)) is { Mode: "demo", Experts.Length: 0 }
               && (await EcranExperts.ExpertsAsync(Client, GreffeAbsent)) is { Mode: "erreur", Experts.Length: 0, Message: not null },
            "pont Experts sans serveur : « demo », aucun expert ; greffe absent : le motif, aucun expert");
    }

    // ============================================================ C-D. CLIENTS ET FICHE CLIENT (LectureClients.cs)
    // 2400701 « en compte » actif (groupe AV), sept soldes, deux factures, une ventilation et son encaissement, un contact ;
    // 2400702 web bloque ; 2400703 de type « A » (pas un client en compte) et 2400704 d'un groupe absent de CLIENT_GROUPE_REF : ecartes.
    private async Task ClientsAsync()
    {
        await Sql("""
            IF OBJECT_ID(N'v2.CLIENT_GROUPE_REF') IS NULL
                CREATE TABLE v2.CLIENT_GROUPE_REF (R_CLIENTGROU_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_CLIENTGROU_LIBELLE_ECRAN varchar(35) NOT NULL);
            IF OBJECT_ID(N'v2.CLIENT_CONTACT') IS NULL
                CREATE TABLE v2.CLIENT_CONTACT (CLICO_ID_CLICO int NOT NULL PRIMARY KEY, CLICO_ID_CLIEN int NULL, CLICO_NOM varchar(70) NULL,
                    CLICO_PRENOM varchar(70) NULL, CLICO_TELEPHONE varchar(20) NULL, CLICO_FONCTION varchar(70) NULL, CLICO_SERVICE varchar(70) NULL,
                    CLICO_FAX varchar(20) NULL, CLICO_EMAIL varchar(100) NULL, CLICO_PORTABLE varchar(20) NULL);
            """);
        await Sql("""
            INSERT INTO v2.CLIENT_GROUPE_REF VALUES ('AV', 'Avocats'), ('WB', 'Web');
            SET IDENTITY_INSERT rec.Client ON;
            INSERT INTO rec.Client (Id, Abrege, NumeroCompte, Nom, Prenom, Type, Civilite, EstComptePc, EstCompteWeb, Groupe, Statut, Siren, Email,
                                    Telephone, ModePaiement, SeuilCompte, ReleveFacture, EtatEdi, DateEdi, BlocNotes, DateDerniereModification)
            VALUES (2400701, 'L14CLA', '411240701', 'CLIENT BANC', 'Alpha', 'C', 'MR', 0, 0, 'AV', 'A', '240070100', 'alpha@banc.fr', '0476000002', 'CH',
                    150.25, 1, '2', '20261015', 'Notes du banc', '20261020'),
                   (2400702, 'L14CLB', '411240702', 'CLIENT WEB', NULL, 'C', 'PM', 0, 1, 'WB', 'B', NULL, NULL, NULL, NULL, NULL, 0, NULL, NULL, NULL, NULL),
                   (2400703, 'L14CLC', '411240703', 'PAS EN COMPTE', NULL, 'A', 'PM', 0, 0, 'AV', 'A', NULL, NULL, NULL, NULL, NULL, 0, NULL, NULL, NULL, NULL),
                   (2400704, 'L14CLD', '411240704', 'SANS GROUPE', NULL, 'C', 'PM', 0, 0, 'ZZ', 'A', NULL, NULL, NULL, NULL, NULL, 0, NULL, NULL, NULL, NULL);
            SET IDENTITY_INSERT rec.Client OFF;
            INSERT INTO rec.ClientAdresse (IdClient, LigneAdresse, CodePostal, Localite, ParDefaut) VALUES (2400701, '5 RUE DU CLIENT', '38000', 'GRENOBLE', 1);
            INSERT INTO v2.CLIENT_SOLDE (CLISO_DATE_SOLDE, CLISO_MONTANT, CLISO_ID_CLIEN, CLISO_ID_UTLST) VALUES
                ('20260101', 10, 2400701, 1), ('20260201', 20, 2400701, 1), ('20260301', 30, 2400701, 1), ('20260401', 40, 2400701, 1),
                ('20260501', 50, 2400701, 1), ('20260601', 60, 2400701, 1), ('20260701', 70.5, 2400701, 1);
            INSERT INTO v2.FACTURE (FACTU_NUMERO_FACTURE, FACTU_ID_CLIEN, FACTU_DATE_CREATION, FACTU_DATE_COMPTABILISATION) VALUES
                ('F14-1', 2400701, '20260105', '19000101'), ('F14-2', 2400701, '20260905', '19000101');
            INSERT INTO v2.ENCAISSEMENT (ENCMT_ID_ENCMT, ENCMT_DATE_ENCAISSEMENT, ENCMT_DATE_COMPTABILISATION) VALUES (2400721, '20260310', '19000101');
            INSERT INTO v2.VENTILATION (VENTI_ID_VENTI, VENTI_ID_CLIEN, VENTI_ID_ENCMT, VENTI_MONTANT) VALUES (2400731, 2400701, 2400721, 12);
            INSERT INTO v2.CLIENT_CONTACT (CLICO_ID_CLICO, CLICO_ID_CLIEN, CLICO_NOM, CLICO_PRENOM, CLICO_FONCTION, CLICO_PORTABLE, CLICO_EMAIL)
            VALUES (2400741, 2400701, 'CONTACT', 'Jean', 'Associe', '0600000001', 'jean@banc.fr');
            """);
        var lus = await SurLaBase(cn => LectureClients.LireAsync(cn, CancellationToken.None));
        var miens = lus.Lignes.Where(c => c.Abrege.StartsWith("L14", StringComparison.Ordinal)).ToArray();
        Exiger(miens.Select(c => c.Id).SequenceEqual([2400701, 2400702]),
            $"Clients : CLIEN_TYPE = 'C' (2400703 ecarte) et INNER JOIN CLIENT_GROUPE_REF (2400704, groupe absent, ecarte) ({string.Join(",", miens.Select(c => c.Id))})");
        Exiger(miens[0] is { Nom: "CLIENT BANC Alpha", Groupe: "Avocats", Statut: "A", Type: "En compte", Compte: "411240701" }
               && miens[1] is { Type: "Web", Statut: "B", Groupe: "Web" },
            "Clients : « nom prénom », le libelle du groupe, le statut V2 et le type de DgvRow.GetTypeClient");
        Exiger(LectureClients.TypeClient(false, false, "3801", "") == "Greffe" && LectureClients.TypeClient(false, true, "", "PR") == "Prévention"
               && LectureClients.TypeClient(false, true, "", "PC") == "PC" && LectureClients.TypeClient(true, true, "", "") == "",
            "Clients : les cinq types de RIG v2 (greffe, prevention, PC), et rien hors de ses cas");
        var pontListe = await EcranClients.ClientsAsync(Client, Greffe);
        Exiger(pontListe is { Mode: "serveur" } && pontListe.Clients.FirstOrDefault(c => c.Id == 2400702) is { Statut: "Bloque", Type: "Web" }
               && pontListe.Clients.FirstOrDefault(c => c.Id == 2400701) is { Statut: "Actif" },
            "pont Clients branche : statut et type aux libelles des criteres de l'ecran");
        Exiger((await EcranClients.ClientsAsync(null, null)) is { Mode: "demo", Clients.Length: 0 }
               && (await EcranClients.ClientsAsync(Client, GreffeAbsent)) is { Mode: "erreur", Clients.Length: 0, Message: not null },
            "pont Clients sans serveur : « demo », aucun client ; greffe absent : le motif, aucun client");

        var fiche = await SurLaBase(cn => LectureClients.FicheAsync(cn, 2400701, CancellationToken.None));
        Exiger(fiche is { Abrege: "L14CLA", Siren: "240070100", Adresse: "5 RUE DU CLIENT 38000 GRENOBLE", Groupe: "Avocats", ModePaiement: "CH",
                          Provision: 15025, ReleveFacture: true, SynchroInfogreffe: "15/10/2026", Notes: "Notes du banc", DerniereModif: "20/10/2026" },
            $"Fiche client : identification, adresse par defaut, parametrage (provision = CLIEN_SEUIL_COMPTE, en centimes), synchro Infogreffe « 2 » = sa date ({fiche})");
        Exiger(fiche is { NbFactures: 2, FactuPremiere: "05/01/2026", FactuDerniere: "05/09/2026", NbEncaissements: 1, EncaissPremier: "10/03/2026" },
            "Fiche client : factures du client et encaissements de ses ventilations (OPE_INFORMATIONS)");
        Exiger(fiche is not null && fiche.Soldes.Select(s => s.Date).SequenceEqual(["2026-07-01", "2026-06-01", "2026-05-01", "2026-04-01", "2026-03-01", "2026-02-01"])
               && fiche.Soldes[0].Solde == 7050,
            "Fiche client : les SIX derniers soldes, du plus recent (« top 6 … order by CLISO_DATE_SOLDE desc »)");
        Exiger(fiche is { Contacts: [{ Nom: "CONTACT Jean", Fonction: "Associe", Telephone: "0600000001", Mail: "jean@banc.fr" }] },
            "Fiche client : ses contacts (CLIENT_CONTACT), le portable a defaut du telephone");
        Exiger(await SurLaBase(cn => LectureClients.FicheAsync(cn, 2400703, CancellationToken.None)) is null,
            "Fiche client : un client hors compte (type A) n'a pas de fiche ici");
        Exiger((await Http.GetFromJsonAsync<FicheClientLue>(RoutesEcransMain.Pour(RoutesClients.Fiche, Greffe).Replace("{id}", "2400701"), Json)) is { NbFactures: 2 }
               && (await Http.GetAsync(RoutesEcransMain.Pour(RoutesClients.Fiche, Greffe).Replace("{id}", "2400703"))).StatusCode == System.Net.HttpStatusCode.NotFound,
            "route Fiche client par le serveur : la fiche ; un client absent : 404 et son motif");
        var pont = await EcranFicheClient.FicheAsync(Client, Greffe, 2400701);
        Exiger(pont is { Mode: "serveur", Fiche: { EtatCompte: "Actif", Type: "En compte", Provision: 15025 } }
               && pont.Mouvements.Select(m => m.Date).SequenceEqual(["2026-02-01", "2026-03-01", "2026-04-01", "2026-05-01", "2026-06-01", "2026-07-01"])
               && pont.Mouvements[^1].Solde == 7050 && pont.Mouvements.All(m => m.Debit == 0 && m.Credit == 0)
               && pont.Contacts is [{ Principal: false, Abrege: "L14CLA" }],
            "pont Fiche client branche : les soldes du plus ancien au plus recent (le dernier = solde actuel), sans debit ni credit inventes ; aucun contact « principal »");
        Exiger((await EcranFicheClient.FicheAsync(null, null, 1)) is { Mode: "demo", Fiche: null }
               && (await EcranFicheClient.FicheAsync(Client, GreffeAbsent, 2400701)) is { Mode: "erreur", Fiche: null, Message: not null },
            "pont Fiche client sans serveur : « demo », aucune fiche ; greffe absent : le motif, aucune fiche");
    }
}
