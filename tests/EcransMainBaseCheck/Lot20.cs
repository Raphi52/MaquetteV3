using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransMain;
using RigV3Desktop.Pont;

// Banc du LOT 20 du plan « RIG v3 100 % fonctionnel » (27/11/2026), joue a la fin de Program.cs sur la meme base jetable et le
// meme serveur en memoire : un fichier a lui, pour ne pas se disputer Program.cs avec les lots qui y ajoutent leur section.
//   A. la recherche BODACC de la palette Ctrl K (LectureBodacc.ChercherAsync) : sans la periode de l'ecran, chaque mot ;
//   B. « Cloture » (LectureCloture) : les filtres d'OPE_SELECT_PC, chacun avec son jumeau ;
//   C. « Archivage / radiation » (LectureArchivage) : dates, type RCS de RIG v2, liste, « Historique des FORMALITES » ;
//   D. « Coffre-fort » (LectureCoffreFort) : l'historique DEMAT_CFEC, son ordre et ses couleurs ;
//   E. « Commandes web » (LectureCommandesWeb) : la lecture, et les gestes qui ecrivent REFUSES, rien d'ecrit ;
//   F. « Mon accueil » (LectureParametresAccueil) : les reglages RIG v2 de l'agent appelant, et de lui seul.
// Chaque section : la lecture, la route par le vrai serveur, le pont (base, demonstration, panne).
// Identifiants : a partir de 3 000 000 ((10 + 20) x 100 000, regle du plan).

/// <summary>Ce que Program.cs prete au banc du lot 20.</summary>
internal sealed record BancLot20(Func<string, (string Nom, object? Valeur)[], Task> ExecuterSql, Action<bool, string> Verifier, string Connexion,
                                 ClientServeur Client, HttpClient Http, JsonSerializerOptions Json, string Greffe, string GreffeAbsent,
                                 DateTime Jour)
{
    private async Task<T> SurLaBase<T>(Func<SqlConnection, Task<T>> lire)
    {
        await using var cn = new SqlConnection(Connexion);
        await cn.OpenAsync();
        return await lire(cn);
    }

    private async Task<int> Compter(string sql)
    {
        await using var cn = new SqlConnection(Connexion);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task JouerAsync()
    {
        await BodaccAsync();
        await ClotureAsync();
        await ArchivageAsync();
        await CoffreFortAsync();
        await CommandesWebAsync();
        await ParametresAccueilAsync();
    }

    // ============================================================ A. RECHERCHE BODACC (palette Ctrl K)
    // Les annonces de la section 1c : 2700601 « Ouverture RJ ALPHA » (C, J-5), 2700602 « Rectificatif ALPHA » (M, J-30, bordereau),
    // 2700603 PC sans numero, 2700604 prevention BETA (S), 2700605 supprimee (« Annonce supprimee »), 2700606 J-70, 2700607 et
    // 2700608 autour du 1er janvier -- dossier 2019B27001 pour les PC P2700201 / P2700202.
    private async Task BodaccAsync()
    {
        string Ann(AnnoncesBodaccTrouvees t) => string.Join(",", t.Lignes.Select(a => a.NumAnnonce));
        Task<AnnoncesBodaccTrouvees> Chercher(string texte, int plafond = RoutesBodaccRecherche.Plafond)
            => SurLaBase(cn => LectureBodacc.ChercherAsync(cn, texte, CancellationToken.None, plafond));

        var alpha = await Chercher("alpha");
        Exiger(Ann(alpha) == "2700602,2700601" && alpha.Total == 2,
            $"recherche BODACC « alpha » : les deux annonces, SANS la periode de l'ecran (J-30 comprise), dans l'ordre de RIG v2 ({Ann(alpha)})");
        Exiger(Ann(await Chercher("ALPHÀ")) == "2700602,2700601", "recherche BODACC : casse et accents ignores (le pliage de la palette)");
        Exiger(Ann(await Chercher("rectification alpha")) == "2700602",
            "recherche BODACC : chaque mot doit s'y trouver ; le type M se cherche a son mot (« Rectification »)");
        var gestion = await Chercher("2019B27001");
        // L'ordre de RIG v2 (date d'envoi) depend du jour : J-70 tombe avant ou apres le 1er janvier selon le mois (section 1c).
        var debutAnnee = new DateTime(Jour.Year, 1, 1);
        var parDate = new (string Id, DateTime Envoi)[] { ("2700606", Jour.AddDays(-70)), ("2700602", Jour.AddDays(-30)), ("2700601", Jour.AddDays(-5)),
                                                          ("2700607", debutAnnee), ("2700608", debutAnnee.AddHours(-1)) };
        Exiger(Ann(gestion) == string.Join(",", parDate.OrderBy(a => a.Envoi).Select(a => a.Id)) && !gestion.Lignes.Any(a => a.NumAnnonce == "2700605"),
            $"recherche BODACC par le dossier : toutes ses annonces, hors la supprimee (etat 9) ({Ann(gestion)})");
        Exiger(Ann(await Chercher("sans numero")) == "", "recherche BODACC : une PC sans numero n'est pas montree (meme filtre que l'ecran)");
        Exiger((await Chercher("al")).Lignes.Length == 0 && (await Chercher("a l")).Lignes.Length == 0 && (await Chercher("lpha")).Total == 2,
            "recherche BODACC : moins de trois caracteres, rien ; jumeau : « lpha » trouve");
        Exiger((await Chercher("lpha_")).Total == 0 && (await Chercher("lpha%")).Total == 0,
            "recherche BODACC : _ et % sont pris a la lettre, pas comme jokers");
        var deux = await Chercher("annonce", plafond: 2);
        Exiger(deux is { Lignes.Length: 2, Total: 3 }, $"recherche BODACC : au plus le plafond, et le total en base ({deux.Total})");

        var parRoute = await (await Http.PostAsJsonAsync(RoutesEcransMain.Pour(RoutesBodaccRecherche.Recherche, Greffe), new RechercheBodacc("alpha"), Json))
            .Content.ReadFromJsonAsync<AnnoncesBodaccTrouvees>(Json);
        Exiger(parRoute is { Total: 2 }, "route de la recherche BODACC par le serveur : les annonces de la base");
        Exiger((await Http.PostAsJsonAsync(RoutesEcransMain.Pour(RoutesBodaccRecherche.Recherche, GreffeAbsent), new RechercheBodacc("alpha"), Json)).StatusCode
               == HttpStatusCode.NotFound, "route de la recherche BODACC, greffe absent : 404");
        var pont = await EcranBodacc.ChercherAsync(Client, Greffe, "alpha");
        Exiger(pont is { Mode: "serveur", Message: null, Total: 2 } && pont.Annonces[0] is { NumAnnonce: "2700602", Dossier: "2019B27001", Envoi: not null },
            "pont de la recherche BODACC branche : les annonces de la base, a la forme de l'ecran");
        Exiger((await EcranBodacc.ChercherAsync(null, null, "alpha")) is { Mode: "demo", Annonces.Length: 0 },
            "pont de la recherche BODACC sans serveur : mode « demo », aucune annonce (la page cherche ses exemples, marques)");
        Exiger((await EcranBodacc.ChercherAsync(Client, GreffeAbsent, "alpha")) is { Mode: "erreur", Annonces.Length: 0, Message: not null },
            "pont de la recherche BODACC, greffe absent : le motif, AUCUNE annonce");
    }

    // ============================================================ B. CLOTURE (LectureCloture.cs)
    // PC 3000101 : deux evenements 4CLO futurs -- le plus RECENT (EVNPR_DATE) a sa limite a J+10 (date maxi), l'autre a J+40 --,
    //   au forfait, un liquidateur en mission (plus un fin de mission et un MDJ, ecartes) ;
    // 3000102 : limite par la seule date fixe, a J-5, sans liquidateur ; 3000108 : une instance d'un autre code (jumeau de 3000107) ;
    // ecartees : 3000103 exterieure, 3000104 cloturee, 3000105 evenement survenu, 3000106 delai expire, 3000107 instance de
    // cloture (CLOT_CODE_AFFAIRE), 3000109 evenement « 3CLO ».
    private async Task ClotureAsync()
    {
        await Sql("""
            IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_NOM_AFFAIRE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_NOM_AFFAIRE varchar(250) NULL;
            IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_FORFAIT_FACTURE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_FORFAIT_FACTURE bit NULL;
            IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_EXTERIEUR') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_EXTERIEUR bit NULL;
            IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_CODE_CLOTURE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_CODE_CLOTURE varchar(5) NULL;
            IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_SURVENU') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_SURVENU bit NULL;
            IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DELAI_EXPIRE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DELAI_EXPIRE bit NULL;
            IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE_MAXI') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE_MAXI datetime NULL;
            IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE_MINI') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE_MINI datetime NULL;
            IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE_FIXE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE_FIXE datetime NULL;
            IF COL_LENGTH('v2.INSTANCE', 'INSTN_ID_PRCLL') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_ID_PRCLL int NULL;
            IF OBJECT_ID(N'v2.MANDATAIRE_PC') IS NULL
                CREATE TABLE v2.MANDATAIRE_PC (MNDPC_ID_MNDPC int NOT NULL PRIMARY KEY, MNDPC_CIVILITE varchar(7) NOT NULL, MNDPC_NOM varchar(100) NOT NULL,
                    MNDPC_COMPLEMENT_NOM varchar(100) NULL);
            IF COL_LENGTH('v2.MANDATAIRE_PC', 'MNDPC_ABREGE') IS NULL ALTER TABLE v2.MANDATAIRE_PC ADD MNDPC_ABREGE varchar(10) NULL;
            IF OBJECT_ID(N'v2.GERER_PC') IS NULL
                CREATE TABLE v2.GERER_PC (GRRPC_ID_GRRPC int NOT NULL PRIMARY KEY, GRRPC_ID_MNDPC int NOT NULL, GRRPC_ID_PRCLL int NULL,
                    GRRPC_NUM_ORDRE smallint NOT NULL, GRRPC_FONCTION_PC varchar(5) NOT NULL, GRRPC_FIN_MISSION bit NOT NULL, GRRPC_ID_PREVE int NULL);
            """, []);
        await Sql("""
            INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('CLOT_CODE_AFFAIRE', 'Cloture sur requete', 'FCLOT20'),
                ('CLOT_CODE_AFFAIRE_SO', 'Cloture sur saisine d''office', 'FCLIA20');
            INSERT INTO v2.PROCEDURE_COLLECTIVE (PRCLL_ID_PRCLL, PRCLL_ID_DSRCS, PRCLL_NUM_PC, PRCLL_NOM_AFFAIRE, PRCLL_EXTERIEUR, PRCLL_CODE_CLOTURE,
                PRCLL_FORFAIT_FACTURE) VALUES
                (3000101, 2700001, 'P3000101', 'CLOTURE UN', 0, NULL, 1), (3000102, 2700002, 'P3000102', 'CLOTURE DEUX', 0, NULL, 0),
                (3000103, 2700002, 'P3000103', 'EXTERIEURE', 1, NULL, 0), (3000104, 2700002, 'P3000104', 'DEJA CLOTUREE', 0, 'X', 0),
                (3000105, 2700002, 'P3000105', 'SURVENU', 0, NULL, 0), (3000106, 2700002, 'P3000106', 'DELAI EXPIRE', 0, NULL, 0),
                (3000107, 2700002, 'P3000107', 'INSTANCE DE CLOTURE', 0, NULL, 0), (3000108, 2700002, 'P3000108', 'AUTRE INSTANCE', 0, NULL, 0),
                (3000109, 2700002, 'P3000109', 'PAS 4CLO', 0, NULL, 0);
            INSERT INTO v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR, EVNPR_ID_PRCLL, EVNPR_ID_PREVE, EVNPR_CODE_EVE_PRO, EVNPR_DATE, EVNPR_SURVENU,
                EVNPR_DELAI_EXPIRE, EVNPR_DATE_MAXI, EVNPR_DATE_MINI, EVNPR_DATE_FIXE) VALUES
                (3000111, 3000101, NULL, '4CLOT', @j2, 0, 0, @j10, @j5, NULL), (3000112, 3000101, NULL, '4CLOX', @j1, 0, 0, NULL, @j40, NULL),
                (3000121, 3000102, NULL, '4CLOT', @j1, 0, 0, NULL, NULL, @jm5),
                (3000131, 3000103, NULL, '4CLOT', @j1, 0, 0, NULL, NULL, @jm5), (3000141, 3000104, NULL, '4CLOT', @j1, 0, 0, NULL, NULL, @jm5),
                (3000151, 3000105, NULL, '4CLOT', @j1, 1, 0, NULL, NULL, @jm5), (3000161, 3000106, NULL, '4CLOT', @j1, 0, 1, NULL, NULL, @jm5),
                (3000171, 3000107, NULL, '4CLOT', @j1, 0, 0, NULL, NULL, @jm5), (3000181, 3000108, NULL, '4CLOT', @j1, 0, 0, @j20, NULL, NULL),
                (3000191, 3000109, NULL, '3CLOT', @j1, 0, 0, NULL, NULL, @jm5);
            INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_CODE_AFFAIRE, INSTN_ID_PRCLL) VALUES
                (3000172, '2026J30001', 'FCLOT20', 3000107), (3000182, '2026J30002', 'AUTRE20', 3000108);
            INSERT INTO v2.MANDATAIRE_PC (MNDPC_ID_MNDPC, MNDPC_CIVILITE, MNDPC_NOM, MNDPC_COMPLEMENT_NOM, MNDPC_ABREGE) VALUES
                (3000191, 'MAITRE', 'LIQUIDE', 'Jean', 'LIQ20'), (3000192, 'MAITRE', 'PARTI', NULL, 'PAR20'), (3000193, 'MAITRE', 'JUDICIAIRE', NULL, 'MDJ20');
            INSERT INTO v2.GERER_PC (GRRPC_ID_GRRPC, GRRPC_ID_MNDPC, GRRPC_ID_PRCLL, GRRPC_NUM_ORDRE, GRRPC_FONCTION_PC, GRRPC_FIN_MISSION) VALUES
                (3000195, 3000191, 3000101, 1, 'LIQ', 0), (3000196, 3000192, 3000101, 2, 'LIQ', 1), (3000197, 3000193, 3000101, 3, 'MDJ', 0);
            """, [("@j1", Jour.AddDays(-30)), ("@j2", Jour.AddDays(-20)), ("@j5", Jour.AddDays(5)), ("@j10", Jour.AddDays(10)),
                  ("@j20", Jour.AddDays(20)), ("@j40", Jour.AddDays(40)), ("@jm5", Jour.AddDays(-5))]);

        Task<AffairesClotureLues> Lire(DateOnly? du, DateOnly? au)
            => SurLaBase(cn => LectureCloture.LireAsync(cn, new RechercheCloture(du, au), CancellationToken.None));
        string Pc(AffairesClotureLues l) => string.Join(",", l.Lignes.Select(a => a.NumPc));
        var jour = DateOnly.FromDateTime(Jour);

        var toutes = await Lire(null, null);
        Exiger(Pc(toutes) == "P3000101,P3000102,P3000108" && toutes.Total == 3,
            $"Cloture : les PC du greffe, non cloturees, avec un 4CLO futur et sans instance de cloture, par numero ; ni exterieure, ni " +
            $"cloturee, ni 4CLO survenu ou expire, ni instance FCLOT20, ni « 3CLO » ({Pc(toutes)})");
        var un = toutes.Lignes[0];
        Exiger(un is { AuForfait: true, Dossier: "2019B27001", NomAffaire: "CLOTURE UN" } && un.LimiteExamen == Jour.AddDays(10)
               && un.Liquidateurs == "Maitre LIQUIDE Jean",
            $"Cloture : la limite de l'evenement 4CLO le plus RECENT (date maxi), au forfait, le liquidateur EN MISSION seul ({un})");
        Exiger(toutes.Lignes[1] is { AuForfait: false, Liquidateurs: "" } && toutes.Lignes[1].LimiteExamen == Jour.AddDays(-5),
            "Cloture : sans date maxi ni mini, la date fixe ; sans liquidateur, aucun nom");
        Exiger(Pc(await Lire(null, jour)) == "P3000102", "Cloture, sans date de debut : la limite AVANT la date de fin (J-5 < J ; J+10 non)");
        Exiger(Pc(await Lire(jour.AddDays(30), jour.AddDays(50))) == "P3000101",
            "Cloture, du J+30 au J+50 : P3000101 par son evenement a J+40, comme RIG v2 (n'importe quel 4CLO futur dans la plage)");
        Exiger(Pc(await Lire(jour.AddDays(11), jour.AddDays(19))) == "", "Cloture, du J+11 au J+19 : aucune limite dans la plage");
        await Sql("DELETE FROM soc.Reglage WHERE Code = 'CLOT_CODE_AFFAIRE';", []);
        Exiger(Pc(await Lire(null, null)) == "P3000101,P3000102,P3000107,P3000108",
            "Cloture, jumeau : sans le reglage CLOT_CODE_AFFAIRE, l'instance FCLOT20 n'ecarte plus sa PC");
        await Sql("INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('CLOT_CODE_AFFAIRE', 'Cloture sur requete', 'FCLOT20');", []);

        Exiger((await (await Http.PostAsJsonAsync(RoutesEcransMain.Pour(RoutesCloture.Affaires, Greffe), new RechercheCloture(null, null), Json))
                .Content.ReadFromJsonAsync<AffairesClotureLues>(Json)) is { Total: 3 }, "route Cloture par le serveur : les PC de la base");
        var pont = await EcranCloture.AffairesAsync(Client, Greffe, null, null);
        Exiger(pont is { Mode: "serveur", Message: null, Total: 3 }
               && pont.Lignes[0] is { NumPc: "P3000101", Affaire: "CLOTURE UN", AuForfait: true, AFacturer: false, Chambre: "", Classement: "", Forfait: "" }
               && pont.Lignes[0].LimiteExamen == Jour.AddDays(10).ToString("yyyy-MM-dd")
               && pont.Lignes[1] is { AFacturer: true, Liquidateurs: EcranCloture.SansLiquidateur }
               && pont.Aujourdhui == Jour.ToString("yyyy-MM-dd"),
            "pont Cloture branche : les PC a la forme de l'ecran, rien d'invente (chambre, classement, forfait vides), « au forfait » = jamais facture, la phrase V2 sans liquidateur");
        Exiger((await EcranCloture.AffairesAsync(null, null, null, null)) is { Mode: "demo", Lignes.Length: 0, Aujourdhui: "2026-09-09" },
            "pont Cloture sans serveur : mode « demo », aucune ligne, la date du jeu (l'ecran garde ses exemples)");
        Exiger((await EcranCloture.AffairesAsync(Client, GreffeAbsent, null, null)) is { Mode: "erreur", Lignes.Length: 0, Message: not null },
            "pont Cloture, greffe absent : le motif, AUCUNE ligne");
    }

    // ============================================================ C. ARCHIVAGE (LectureArchivage.cs)
    // Dossiers radies en 1991 (hors de toute autre section) : 3000201 B1 et 3000202 B2 retenus ; 3000203 B1 deja dote de son
    // « Historique des FORMALITES » (ecarte) ; 3000204 A1 (autre type) ; 3000205 B1 radie en 1993 (hors periode).
    private async Task ArchivageAsync()
    {
        async Task Dossier(int id, string nom, string type, DateTime radiation)
            => await Sql("""
                SET IDENTITY_INSERT rec.Entreprise ON;
                INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, Prenom, FormeJuridique, EtatInfo, FicheSommaire)
                VALUES (@id, NULL, '', @nom, NULL, 'RL', 'C', 0);
                SET IDENTITY_INSERT rec.Entreprise OFF;
                SET IDENTITY_INSERT rec.DossierRcs ON;
                INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
                    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
                    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise, DateRadiation)
                VALUES (@id, @id, @type, @gestion, NULL, '19800101', 'C', 0, 0, '19800101', 'R', 'C', 0, 0, 0, 0, 0, 0, 0, @rad);
                SET IDENTITY_INSERT rec.DossierRcs OFF;
                """, [("@id", id), ("@nom", nom), ("@type", type), ("@gestion", $"1991B{id % 100000:00000}"), ("@rad", radiation)]);
        await Dossier(3000201, "ARCHIVE UN", "B1", new DateTime(1991, 3, 10));
        await Dossier(3000202, "ARCHIVE DEUX", "B2", new DateTime(1991, 2, 1));
        await Dossier(3000203, "DEJA ANNEXE", "B1", new DateTime(1991, 4, 1));
        await Dossier(3000204, "AUTRE TYPE", "A1", new DateTime(1991, 5, 1));
        await Dossier(3000205, "HORS PERIODE", "B1", new DateTime(1993, 1, 1));
        await Sql("""
            IF OBJECT_ID(N'v2.ACTION_RCS_GED_ANNEXES') IS NULL
                CREATE TABLE v2.ACTION_RCS_GED_ANNEXES (ACRGA_ID_ACRGA int NOT NULL PRIMARY KEY, ACRGA_ID_ACTRC int NULL, ACRGA_LIBELLE_PIECE varchar(250) NULL);
            SET IDENTITY_INSERT rec.ActionRegistre ON;
            INSERT INTO rec.ActionRegistre (Id, IdDossier, CodeProcessus, EtatInfo, EtatAction, SurNotification) VALUES
                (3000211, 3000203, 'BANC20', 'C', 'V', 0), (3000212, 3000201, 'BANC20', 'C', 'V', 0);
            SET IDENTITY_INSERT rec.ActionRegistre OFF;
            INSERT INTO v2.ACTION_RCS_GED_ANNEXES (ACRGA_ID_ACRGA, ACRGA_ID_ACTRC, ACRGA_LIBELLE_PIECE) VALUES
                (3000221, 3000211, 'Historique des FORMALITES'), (3000222, 3000212, 'Autre annexe');
            """, []);
        Task<DossiersArchivageLus> Lire(RechercheArchivage r) => SurLaBase(cn => LectureArchivage.LireAsync(cn, r, CancellationToken.None));
        string Num(DossiersArchivageLus l) => string.Join(",", l.Lignes.Select(d => d.Numero));
        var periode = new RechercheArchivage(new DateOnly(1991, 1, 1), new DateOnly(1991, 12, 31), "B", null);

        var b = await Lire(periode);
        Exiger(Num(b) == "1991B00202,1991B00201" && b.Total == 2 && b.Motif is null,
            $"Archivage, type B (B1 et B2) en 1991 : par date de radiation ; sans le dossier deja dote de son « Historique des FORMALITES », " +
            $"ni l'autre type, ni 1993 ({Num(b)})");
        Exiger(b.Lignes[1] is { Designation: "ARCHIVE UN", TypeRcs: "B1" } && b.Lignes[1].Radiation == new DateTime(1991, 3, 10),
            "Archivage : la designation et le type du dossier ; une autre annexe ne l'ecarte pas (jumeau)");
        Exiger(Num(await Lire(periode with { TypeRcs = "A" })) == "1991B00204", "Archivage, type A : A1 et A2 (InTypeRcs)");
        Exiger(Num(await Lire(periode with { Au = new DateOnly(1993, 1, 1) })) == "1991B00202,1991B00201,1991B00205",
            "Archivage, jumeau : jusqu'au 01/01/1993 a 24 h, le dossier radie ce jour-la entre");
        Exiger(Num(await Lire(periode with { Liste = "1991B00203  1991B00201" })) == "1991B00201",
            "Archivage, mode liste : les numeros saisis (espaces multiples admis), sans le dossier deja annexe");
        var sansType = await Lire(periode with { TypeRcs = "" });
        var sansDate = await Lire(periode with { Du = null });
        var typeInconnu = await Lire(periode with { TypeRcs = "PM" });
        Exiger(sansType is { Total: 0, Motif: LectureArchivage.CriteresManquants } && sansDate.Motif == LectureArchivage.CriteresManquants
               && typeInconnu.Motif == LectureArchivage.CriteresManquants,
            "Archivage : sans type, sans date ou avec un type hors de la liste de RIG v2, rien n'est cherche et le motif le dit");
        Exiger((await (await Http.PostAsJsonAsync(RoutesEcransMain.Pour(RoutesArchivage.Dossiers, Greffe), periode, Json))
                .Content.ReadFromJsonAsync<DossiersArchivageLus>(Json)) is { Total: 2 }, "route Archivage par le serveur : les dossiers de la base");
        var pont = await EcranArchivage.DossiersAsync(Client, Greffe, periode);
        Exiger(pont is { Mode: "serveur", Total: 2, MaxBordereau: 150 } && pont.Types.SequenceEqual(["A", "B", "C", "D", "AC", "I"])
               && pont.Lignes[0] is { Dossier: "1991B00202", Denomination: "ARCHIVE DEUX", Type: "B2", Radiation: "1991-02-01" },
            "pont Archivage branche : les dossiers a la forme de l'ecran, les types de RIG v2, le plafond du bordereau");
        Exiger((await EcranArchivage.DossiersAsync(null, null, periode)) is { Mode: "demo", Lignes.Length: 0 },
            "pont Archivage sans serveur : mode « demo », aucune ligne (l'ecran garde ses exemples)");
        Exiger((await EcranArchivage.DossiersAsync(Client, GreffeAbsent, periode)) is { Mode: "erreur", Lignes.Length: 0, Message: not null },
            "pont Archivage, greffe absent : le motif, AUCUNE ligne");
    }

    // ============================================================ D. COFFRE-FORT (LectureCoffreFort.cs)
    // 3000301 traite le 01/06/2004, etat 3 depose (vert) ; 3000302 traite le 01/05/2004, etat 8 en erreur (rouge) ; 3000303 etat 3
    // SANS date de depot (orange, meme un 3) ; 3000304 d'un etat absent de DEMAT_ETAT (pas montre).
    private async Task CoffreFortAsync()
    {
        await Sql("""
            CREATE TABLE v2.DEMAT_ETAT (R_DEMATETAT_VALEUR varchar(4) NOT NULL PRIMARY KEY, R_DEMATETAT_LIBELLE_ECRAN varchar(70) NULL);
            CREATE TABLE v2.DEMAT_CFEC (CFEC_ID_CFEC int NOT NULL PRIMARY KEY, CFEC_NUM_ENTITE varchar(30) NULL, CFEC_TYPE_ENTITE varchar(30) NULL,
                CFEC_NATURE_DOC varchar(20) NULL, CFEC_TYPE_DE_DESTINATAIRE varchar(20) NULL, CFEC_ID_DESTINATAIRE int NULL, CFEC_ETAT varchar(4) NULL,
                CFEC_ERREUR varchar(500) NULL, CFEC_NOM_DOCUMENT_DS_COFFRE varchar(250) NULL, CFEC_IDENTIFIANT_DEPOT varchar(50) NULL,
                CFEC_DATE_TRAITEMENT_RIG datetime NULL, CFEC_DATE_RECEPTION_AR datetime NULL, CFEC_DATE_DEPOT datetime NULL,
                CFEC_DATE_ACCUSE_RECEPTION datetime NULL, CFEC_DATE_LECTURE datetime NULL);
            INSERT INTO v2.DEMAT_ETAT VALUES ('3', 'Déposé'), ('8', 'Erreur de dépôt');
            INSERT INTO v2.DEMAT_CFEC (CFEC_ID_CFEC, CFEC_NUM_ENTITE, CFEC_TYPE_ENTITE, CFEC_NATURE_DOC, CFEC_TYPE_DE_DESTINATAIRE, CFEC_ID_DESTINATAIRE,
                CFEC_ETAT, CFEC_ERREUR, CFEC_NOM_DOCUMENT_DS_COFFRE, CFEC_IDENTIFIANT_DEPOT, CFEC_DATE_TRAITEMENT_RIG, CFEC_DATE_RECEPTION_AR,
                CFEC_DATE_DEPOT, CFEC_DATE_ACCUSE_RECEPTION, CFEC_DATE_LECTURE) VALUES
                (3000301, '2004J00007', 'INSTANCE', 'ACAD', 'MANDATAIRE', 3000191, '3', NULL, 'decision.pdf', 'DEP-1', '20040601', '20040602', '20040602', '20040603', NULL),
                (3000302, '2004J00008', 'INSTANCE', 'CD', 'MANDATAIRE', NULL, '8', 'Destinataire inconnu', NULL, NULL, '20040501', NULL, '20040502', NULL, NULL),
                (3000303, '2004J00009', 'EDITION', 'CD', 'TRIBUNAL', NULL, '3', NULL, NULL, NULL, '20040701', NULL, NULL, NULL, NULL),
                (3000304, '2004J00010', 'INSTANCE', 'CD', 'MANDATAIRE', NULL, '5', NULL, NULL, NULL, '20040401', NULL, NULL, NULL, NULL);
            """, []);
        var lus = await SurLaBase(cn => LectureCoffreFort.LireAsync(cn, CancellationToken.None));
        Exiger(lus.Lignes.Select(d => d.Id).SequenceEqual([3000302, 3000301, 3000303]) && lus.Total == 3,
            $"Coffre-fort : l'historique DEMAT_CFEC par date de traitement RIG ; un etat absent de DEMAT_ETAT n'est pas montre " +
            $"({string.Join(",", lus.Lignes.Select(d => d.Id))})");
        Exiger(lus.Lignes[1] is { Entite: "2004J00007", Nature: "ACAD", AbregeDestinataire: "LIQ20", NomDestinataire: "LIQUIDE", LibelleEtat: "Déposé",
                                  NomDocument: "decision.pdf", IdentifiantDepot: "DEP-1" } && lus.Lignes[1].AccuseReception == new DateTime(2004, 6, 3),
            "Coffre-fort : l'entite, la nature, le mandataire destinataire (abrege, nom), le libelle de l'etat, le document et les dates");
        Exiger((await SurLaBase(cn => LectureCoffreFort.LireAsync(cn, CancellationToken.None, plafond: 1))) is { Lignes.Length: 1, Total: 3 },
            "Coffre-fort : au plus le plafond, et le total");
        Exiger((await Http.GetFromJsonAsync<DepotsCoffreLus>(RoutesEcransMain.Pour(RoutesCoffreFort.Depots, Greffe), Json)) is { Total: 3 },
            "route Coffre-fort par le serveur : les depots de la base");
        var pont = await EcranCoffreFort.DepotsAsync(Client, Greffe);
        Exiger(pont is { Mode: "serveur", Total: 3 }
               && pont.Depots[0] is { Code: "3000302", Statut: "alerte", Etat: "8 - Destinataire inconnu", Erreur: "Destinataire inconnu", ATransmettre: false }
               && pont.Depots[1] is { Statut: "ok", Etat: "3 - Déposé", Destinataire: "LIQUIDE", AvisDepotCoffre: "2004-06-02", EnvoiInfogreffe: null, Coffre: "" }
               && pont.Depots[2] is { Statut: "attente", Destinataire: "TRIBUNAL" },
            "pont Coffre-fort branche : l'etat et la couleur de RIG v2 (8 rouge, 3 vert, sans depot orange), rien d'invente (envoi Infogreffe, coffre)");
        Exiger((await EcranCoffreFort.DepotsAsync(null, null)) is { Mode: "demo", Depots.Length: 0 },
            "pont Coffre-fort sans serveur : mode « demo », aucun depot (l'ecran garde ses exemples)");
        Exiger((await EcranCoffreFort.DepotsAsync(Client, GreffeAbsent)) is { Mode: "erreur", Depots.Length: 0, Message: not null },
            "pont Coffre-fort, greffe absent : le motif, AUCUN depot");
    }

    // ============================================================ E. COMMANDES WEB (LectureCommandesWeb.cs)
    // 3000401 a traiter (IG, famille 2), 3000402 rejetee (SL, famille 5), 3000403 d'une famille hors de NeoRIG (7, ecartee).
    private async Task CommandesWebAsync()
    {
        await Sql("""
            CREATE TABLE v2.COMMANDE_WEB (CMDWB_ID_CMDWB int NOT NULL PRIMARY KEY, CMDWB_REFERENCE_COMMANDE varchar(50) NULL, CMDWB_NUMERO_SUIVI varchar(50) NULL,
                CMDWB_DATE_COMMANDE datetime NULL, CMDWB_FAMILLE tinyint NULL, CMDWB_TYPE_PRODUIT varchar(10) NULL, CMDWB_ORIGINE varchar(5) NULL,
                CMDWB_SIREN varchar(9) NULL, CMDWB_DENOMINATION varchar(250) NULL, CMDWB_NOM_REQUERANT varchar(250) NULL, CMDWB_ETAT char(1) NULL,
                CMDWB_MOTIF varchar(500) NULL, CMDWB_DATE_TRAITEMENT datetime NULL);
            INSERT INTO v2.COMMANDE_WEB VALUES
                (3000401, '60324-BANC1', '60324-BANC1-1', '20261001 08:30', 2, 'ET-CP', 'IG', '412700001', 'ALPHA BASE SARL', 'REQUERANT UN', 'A', NULL, NULL),
                (3000402, 'SL-BANC-2', '2-1', '20260930 08:00', 5, 'FA-CF', 'SL', '412700002', 'BETA BASE SAS', NULL, 'R', 'Siren inconnu', '20261002'),
                (3000403, 'X-BANC-3', '3-1', '20260929 08:00', 7, 'XX', 'IG', NULL, 'HORS FAMILLE', NULL, 'A', NULL, NULL);
            """, []);
        var lues = await SurLaBase(cn => LectureCommandesWeb.LireAsync(cn, CancellationToken.None));
        Exiger(lues.Lignes.Select(c => c.Id).SequenceEqual([3000402, 3000401]) && lues.Total == 2,
            "Commandes web : COMMANDE_WEB par date de commande ; une famille hors de NeoRIG (7) est ecartee");
        Exiger(lues.Lignes[0] is { Etat: "R", Origine: "SL", Famille: 5, Motif: "Siren inconnu", Denomination: "BETA BASE SAS" },
            "Commandes web : l'etat, l'origine, la famille et le motif tels que la base les porte");
        Exiger((await Http.GetFromJsonAsync<CommandesWebLues>(RoutesEcransMain.Pour(RoutesCommandesWeb.Commandes, Greffe), Json)) is { Total: 2 },
            "route Commandes web par le serveur : les commandes de la base");

        JsonElement Valeur(object? v) => JsonSerializer.SerializeToElement(v);
        var vue = await EcranCommandesWeb.OuvrirAsync(Client, Greffe);
        Exiger(vue is { Mode: "serveur", Provenance: null } && vue.Lignes.Select(l => l.Id).SequenceEqual([3000401])
               && vue.Tuiles.Single(t => t.Etat == "Rejetee").Nombre == 1,
            "pont Commandes web branche : la tuile « A traiter » montre la commande A de la base, la rejetee compte sous sa tuile ; aucune de JeuDemo");
        EcranCommandesWeb.Agir(vue.Session, "Choisir", Valeur(3000401));
        var apres = EcranCommandesWeb.Agir(vue.Session, "Decider", Valeur("Valider"));
        Exiger(apres.Message is { Severite: "avertissement", Texte: SessionCommandesWeb.SansEcriture }
               && apres.Lignes.Select(l => l.Id).SequenceEqual([3000401])
               && await Compter("SELECT COUNT(*) FROM v2.COMMANDE_WEB WHERE CMDWB_ETAT = 'A' AND CMDWB_ID_CMDWB = 3000401") == 1,
            "pont Commandes web branche : « Valider » REFUSE et dit pourquoi ; la commande reste « A traiter », rien n'est ecrit");
        var saisie = EcranCommandesWeb.Agir(vue.Session, "NouvelleCommande", Valeur(null));
        Exiger(saisie is { Message.Texte: SessionCommandesWeb.SansEcriture } && saisie.Tuiles.Sum(t => t.Nombre) == 2,
            "pont Commandes web branche : la saisie au guichet refuse aussi, aucune commande neuve en memoire");
        EcranCommandesWeb.Fermer(vue.Session);
        var demo = await EcranCommandesWeb.OuvrirAsync(null, null);
        Exiger(demo is { Mode: "demo" } && demo.Tuiles.Sum(t => t.Nombre) > 0,
            "pont Commandes web sans serveur : la demonstration, mode « demo » (tests/CommandesWebCheck la joue)");
        EcranCommandesWeb.Fermer(demo.Session);
        var panne = await EcranCommandesWeb.OuvrirAsync(Client, GreffeAbsent);
        Exiger(panne is { Mode: "erreur", Lignes.Length: 0, Provenance: not null } && panne.Tuiles.Sum(t => t.Nombre) == 0,
            "pont Commandes web, greffe absent : le motif, AUCUNE commande");
        EcranCommandesWeb.Fermer(panne.Session);
    }

    // ============================================================ F. MON ACCUEIL (LectureParametresAccueil.cs)
    // L'agent du banc (banc.main, IdV2 2700001) a trois reglages, dont un en double ; un autre agent (2700002) en a un ; une cle
    // hors de l'ecran n'est pas lue.
    private async Task ParametresAccueilAsync()
    {
        await Sql("""
            CREATE TABLE v2.PARAMETRE_UTILISATEUR (PRMTR_ID_PRMTR int NOT NULL PRIMARY KEY, PRMTR_ID_UTLST int NULL, PRMTR_NOM_PARAMETRE varchar(100) NULL,
                PRMTR_VALEUR_PARAMETRE varchar(1000) NULL);
            INSERT INTO v2.PARAMETRE_UTILISATEUR VALUES
                (3000501, 2700001, 'ConsoleAccueil_IntervalSlideshowAlertes', '5000'), (3000502, 2700001, 'ConsoleAccueil_Colorier1LigneSur2', 'False'),
                (3000503, 2700001, 'ConsoleAccueil_Colorier1LigneSur2', 'True'), (3000504, 2700001, 'ConsoleAccueil_Theme', 'MetroBlue'),
                (3000505, 2700002, 'ConsoleAccueil_Form_MaxWidth', '1920');
            INSERT INTO soc.Compte (IdV2, CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
            SELECT NULL, 'banc.sansv2', 'SANS', 'V2', 'SANSV2', 'M.', CodeGreffe, IdService, 0 FROM soc.Compte WHERE IdV2 = 2700001;
            """, []);
        async Task<int> IdCompte(string login)
        {
            await using var cn = new SqlConnection(Connexion);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT Id FROM soc.Compte WHERE CompteWindows = @l", cn);
            cmd.Parameters.AddWithValue("@l", login);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        var moi = await IdCompte("banc.main");
        var lus = await SurLaBase(cn => LectureParametresAccueil.LireAsync(cn, moi, estAdministrateur: true, estAmitel: false, CancellationToken.None));
        Exiger(lus is { AgentV2: true, EstAdministrateur: true, EstAmitel: false } && lus.Valeurs.Count == 2
               && lus.Valeurs["ConsoleAccueil_IntervalSlideshowAlertes"] == "5000" && lus.Valeurs["ConsoleAccueil_Colorier1LigneSur2"] == "True",
            "Mon accueil : les reglages RIG v2 de l'agent (la cle en double : la plus recente), ses profils ; ni le theme (hors ecran), ni ceux d'un autre agent");
        var sansV2 = await SurLaBase(async cn => await LectureParametresAccueil.LireAsync(cn, await IdCompte("banc.sansv2"), false, false, CancellationToken.None));
        Exiger(sansV2 is { AgentV2: false, Valeurs.Count: 0 }, "Mon accueil : un agent sans compte RIG v2 n'a rien a relire, et le dit");
        var parRoute = await Http.GetFromJsonAsync<ParametresAccueilLus>(RoutesEcransMain.Pour(RoutesParametresAccueil.Parametres, Greffe), Json);
        Exiger(parRoute is { AgentV2: true, Valeurs.Count: 2 }, "route Mon accueil par le serveur : les reglages de L'AGENT APPELANT (banc.main)");
        Exiger((await Http.GetAsync(RoutesEcransMain.Pour(RoutesParametresAccueil.Parametres, GreffeAbsent))).StatusCode == HttpStatusCode.NotFound,
            "route Mon accueil, greffe absent : 404");
        var pont = await EcranParametresAccueil.ReglagesAsync(Client, Greffe);
        Exiger(pont is { Mode: "serveur", Message: null, AgentV2: true } && pont.Valeurs["ConsoleAccueil_IntervalSlideshowAlertes"] == "5000",
            "pont Mon accueil branche : les reglages lus, la cle RIG v2 telle quelle (l'ecran convertit les millisecondes)");
        Exiger((await EcranParametresAccueil.ReglagesAsync(null, null)) is { Mode: "demo", Valeurs.Count: 0 },
            "pont Mon accueil sans serveur : mode « demo » (l'ecran garde ses reglages d'exemple)");
        Exiger((await EcranParametresAccueil.ReglagesAsync(Client, GreffeAbsent)) is { Mode: "erreur", Message: not null },
            "pont Mon accueil, greffe absent : le motif");
    }

    /// <summary>Le SQL du banc, sur la base du greffe (Program.cs, Sql).</summary>
    private Task Sql(string sql, (string Nom, object? Valeur)[] p) => ExecuterSql(sql, p);

    /// <summary>Un controle du banc (Program.cs, Exiger).</summary>
    private void Exiger(bool ok, string quoi) => Verifier(ok, quoi);
}
