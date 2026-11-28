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

// Banc du LOT 7 du plan « RIG v3 complet, mesure par le banc du parcours complet » (28/11/2026) : les GESTES ecrits par le serveur de
// trois ecrans faits main, joues a la fin de Program.cs sur la meme base jetable et le meme serveur en memoire, un fichier a lui.
//   A. « Arrivee du courrier » : Enregistrer une arrivee (EcritureArrivee.cs, OPE_ARRIVEE de RIG v2) ;
//   B. « Demandes » : Supprimer l'etat en cours (EcritureDemandes.cs, OPE_RESULTATS.tsmiDeleteEtatEnCoursDemande_Click) ;
//   C. « Commandes web » : Mettre en attente (EcritureCommandesWeb.cs, regles de NeoRIG).
// Chaque refus a son JUMEAU accepte (meme borne, autre cote), rien d'ecrit ni de journal sur un refus, une ligne au journal sur un
// geste fait, et la relecture par le pont. Identifiants : a partir de 3 700 000 ((30 + 7) x 100 000, regle du plan), codes « L7… ».
// Les tables deja creees par Program.cs et Lot20.cs (MODE_ARRIVEE, ARRIVEE, DEMANDE, AFFAIRE, COMMANDE_WEB) recoivent les colonnes que
// les gestes ecrivent, aux types de RIG_DEV.
//
// L'ENTREE QUI REND CHAQUE SECTION ROUGE si l'ecriture etait fausse (vu rouge, defaut reinjecte, compte rendu du lot 7) :
//   A. le mode « L7N » (R_MODEARRIVE_UTILISABLE_SAISIE = 0) : sans le filtre de ULT_COMBO_MODE_ARRIVEE, il serait enregistre ;
//   B. la demande L7 3700012, PAS en cours : sans la garde de OPE_RESULTATS.cs:818, elle serait « supprimee » et journalisee ;
//   C. la commande 3700402, DEJA en attente (T) : sans la regle de TraitementCommandeWeb.Verifier, elle serait remise en attente.

/// <summary>Ce que Program.cs prete au banc du lot 7.</summary>
internal sealed record BancParcoursLot7(Func<string, (string Nom, object? Valeur)[], Task> ExecuterSql, Action<bool, string> Verifier,
                                        string Connexion, ClientServeur Client, HttpClient Http, JsonSerializerOptions Json, string Greffe,
                                        DateTime Jour)
{
    private Task Sql(string sql) => ExecuterSql(sql, [("@aujourdhui", Jour)]);

    private void Exiger(bool ok, string quoi) => Verifier(ok, quoi);

    private async Task<string> Valeur(string sql)
    {
        await using var cn = new SqlConnection(Connexion);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToString(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    private Task<string> Journal() => Valeur("SELECT COUNT(*) FROM soc.Journal");

    private async Task<(HttpStatusCode Statut, ResultatGeste? R)> Route(string route, GesteEcran geste)
    {
        var reponse = await Http.PostAsJsonAsync(RoutesEcransMain.Pour(route, Greffe), geste, Json);
        return (reponse.StatusCode, await reponse.Content.ReadFromJsonAsync<ResultatGeste>(Json));
    }

    public async Task JouerAsync()
    {
        await ArriveeAsync();
        await DemandesAsync();
        await CommandesWebAsync();
        await RequisitionsAsync();
    }

    // ============================================================ D. TERMINER UNE COMMANDE DE REQUISITIONS (EcritureRequisitions.cs)
    // Sur le dossier 2700001 « ALPHA BASE SARL » de Program.cs. Les tables que le geste ecrit, aux types de RIG_DEV ; le compteur DMD
    // des jeux (PROC_CNI.sql). L'entree qui rend la section rouge si l'ecriture etait fausse : la commande SANS la case « Guichet »
    // (frmGestionImprimante.cs:221-224) -- sans cette garde, elle serait ecrite sans client ni lettre d'envoi.
    private async Task RequisitionsAsync()
    {
        await Sql("""
            IF COL_LENGTH('v2.AFFAIRE', 'AFFRE_ID_UTLST_CREATION') IS NULL ALTER TABLE v2.AFFAIRE ADD AFFRE_ID_UTLST_CREATION int NULL;
            IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_AFFRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_AFFRE int NULL;
            IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_AUTRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_AUTRE int NULL;
            IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM_UTILISATEUR') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM_UTILISATEUR varchar(255) NULL;
            IF OBJECT_ID('v2.COMMANDE') IS NULL
                CREATE TABLE v2.COMMANDE (CMMDE_ID_CMMDE int IDENTITY(3700001, 1) NOT NULL PRIMARY KEY, CMMDE_DATE datetime NOT NULL,
                    CMMDE_TYPE varchar(10) NULL, CMMDE_QTE smallint NULL, CMMDE_ID_DMND int NULL, CMMDE_ID_DSRCS int NULL, CMMDE_ID_NINSC int NULL,
                    CMMDE_PROVENANCE varchar(10) NULL, CMMDE_GUICHET bit NULL);
            IF OBJECT_ID('v2.PSEUDO_VARIABLE') IS NULL
                CREATE TABLE v2.PSEUDO_VARIABLE (PSDVR_CPT int IDENTITY(3700001, 1) NOT NULL PRIMARY KEY, PSDVR_ID_DEMANDE int NOT NULL,
                    PSDVR_NOM_VARIABLE varchar(50) NOT NULL, PSDVR_VALEUR_VARIABLE varchar(8000) NOT NULL);
            IF OBJECT_ID('v2.LIGNE_EDITION') IS NULL
                CREATE TABLE v2.LIGNE_EDITION (LGEDT_ID_LGEDT int IDENTITY(3700001, 1) NOT NULL PRIMARY KEY, LGEDT_ID_DMND int NULL,
                    LGEDT_ID_UTLST int NULL, LGEDT_CODE_MODELE_DOC varchar(50) NOT NULL, LGEDT_TRANSMISSION varchar(20) NOT NULL,
                    LGEDT_DESTINATION_PHYSIQUE varchar(50) NULL, LGEDT_NUM_ORDRE smallint NOT NULL, LGEDT_NOMBRE smallint NOT NULL,
                    LGEDT_SCEAU varchar(255) NULL, LGEDT_IMP_BAC varchar(10) NULL, LGEDT_RECTO_VERSO bit NOT NULL);
            IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = 'DMD')
                INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, Lettre, TailleChrono, Debut) VALUES ('DMD', YEAR(@aujourdhui), 'LAQC', 2, 'D', 5, 1);
            """);
        GesteEcran Commande(string guichet) => new(TypeGeste.Creer, [], new Dictionary<string, string>
        {
            [RoutesTerminerRequisitions.ChampGuichet] = guichet,
            [RoutesTerminerRequisitions.Cle(1, RoutesTerminerRequisitions.Dossier)] = "2700001",
            [RoutesTerminerRequisitions.Cle(1, RoutesTerminerRequisitions.Kbis)] = "2",
            [RoutesTerminerRequisitions.Cle(1, RoutesTerminerRequisitions.CertifNonFaillite)] = "1",
            [RoutesTerminerRequisitions.Cle(1, RoutesTerminerRequisitions.DateCertifNonFaillite)] = "2026-11-02",
        }) { Nom = "Terminer" };
        Task<string> Ecrit() => Valeur("SELECT CONCAT((SELECT COUNT(*) FROM v2.COMMANDE), '|', (SELECT COUNT(*) FROM v2.LIGNE_EDITION), '|', (SELECT COUNT(*) FROM v2.AFFAIRE))");

        // Refus : sans « Guichet », rien d'ecrit, rien au journal.
        var (avant, journal) = (await Ecrit(), await Journal());
        var (s0, r0) = await Route(RoutesTerminerRequisitions.Terminer, Commande("0"));
        Exiger(s0 == HttpStatusCode.UnprocessableEntity && r0 is { Fait: false, Message: EcritureRequisitions.SansGuichet }
               && await Ecrit() == avant && await Journal() == journal,
            $"Terminer sans « Guichet » : refus de RIG v2 (422 « {r0?.Message} »), rien d'ecrit ni journalise");

        // Jumeau : au guichet -> la demande REQUI et la LPLURI validees, deux lignes de COMMANDE, deux editions, la date du certificat.
        var (s1, r1) = await Route(RoutesTerminerRequisitions.Terminer, Commande("1"));
        var numero = r1?.Ids.FirstOrDefault() ?? "";
        Exiger(s1 == HttpStatusCode.OK && r1 is { Fait: true } && numero.StartsWith('D') && int.Parse(await Journal()) == int.Parse(journal) + 1,
            $"Terminer au guichet : fait ({(int)s1} « {r1?.Message} »), demande {numero}, une ligne au journal");
        Exiger(await Valeur($"""
                SELECT STRING_AGG(CONCAT(c.CMMDE_TYPE, ' x', c.CMMDE_QTE, ' ', c.CMMDE_ID_DSRCS, ' ', c.CMMDE_PROVENANCE, ' ', CONVERT(int, c.CMMDE_GUICHET)), ', ')
                    WITHIN GROUP (ORDER BY c.CMMDE_ID_CMMDE)
                FROM v2.COMMANDE AS c JOIN v2.DEMANDE AS d ON d.DMND_ID_DMND = c.CMMDE_ID_DMND WHERE d.DMND_NUM_DEMANDE = '{numero}'
                """) == "KBIS x2 2700001 REQUI 1, CNF x1 2700001 REQUI 1",
            "Terminer : les lignes de COMMANDE de RIG v2 (InsererCommande, frmREQUI.cs:2528-2543) sur la demande REQUI");
        Exiger(await Valeur($"""
                SELECT STRING_AGG(CONCAT(l.LGEDT_CODE_MODELE_DOC, ' ', l.LGEDT_TRANSMISSION, ' ', l.LGEDT_NUM_ORDRE, ' x', l.LGEDT_NOMBRE, ' ', l.LGEDT_IMP_BAC), ', ')
                    WITHIN GROUP (ORDER BY l.LGEDT_NUM_ORDRE)
                FROM v2.LIGNE_EDITION AS l JOIN v2.DEMANDE AS d ON d.DMND_ID_DMND = l.LGEDT_ID_DMND
                WHERE d.DMND_CODE_PROSS = 'LPLURI' AND d.DMND_NOM LIKE 'REQUI de la {numero} - %'
                """) == "KBISXML_1_PDF PDF 1 x2 SECURISE, ACNF_1_IMP IMP 2 x1 SECURISE"
               && await Valeur($"""
                SELECT p.PSDVR_VALEUR_VARIABLE FROM v2.PSEUDO_VARIABLE AS p JOIN v2.DEMANDE AS d ON d.DMND_ID_DMND = p.PSDVR_ID_DEMANDE
                WHERE p.PSDVR_NOM_VARIABLE = 'DATE_CERTIFICAT' AND d.DMND_NOM LIKE 'REQUI de la {numero} - %'
                """) == "02/11/2026",
            "Terminer : les editions de la demande LPLURI (KBISXML_1_PDF puis ACNF_1_IMP, transmission tiree du code) et la date du certificat");

        // Relue par la route, puis par le pont.
        var reponse = await Http.PostAsJsonAsync(RoutesEcransMain.Pour(RoutesTerminerRequisitions.Commande, Greffe), new CommandeRequiDemandee(numero), Json);
        var corps = await reponse.Content.ReadAsStringAsync();
        var lue = reponse.IsSuccessStatusCode ? JsonSerializer.Deserialize<CommandeRequiLue>(corps, Json) : null;
        Exiger(lue is { Requi: { Etat: "V", EnCours: false, IdDossier: 2700001 } requi, Demandes: [{ Etat: "V", EnCours: false } lpluri] }
               && requi.Nom == lpluri.Numero && lpluri.Nom == $"REQUI de la {numero} - ALPHA BASE SARL"
               && string.Join(", ", lue.Lignes.Select(l => $"{l.Type} x{l.Quantite}")) == "KBIS x2, CNF x1",
            $"Relecture de la commande {numero} : REQUI validee, nommee de sa LPLURI, sur le dossier ({(int)reponse.StatusCode} {(lue is null ? corps[..Math.Min(400, corps.Length)] : "")})");

        static JsonElement V(object? v) => JsonSerializer.SerializeToElement(v);
        var vue = EcranRequisitions.Ouvrir();
        try
        {
            await EcranRequisitions.AgirAsync(Client, Greffe, vue.Session, "Critere", V(new { champ = "CritRaisonSociale", valeur = "ALPHA BASE" }));
            var trouvee = await EcranRequisitions.AgirAsync(Client, Greffe, vue.Session, "Rechercher", V(null));
            var rang = trouvee.Trouvees.First(t => t.Designation == "ALPHA BASE SARL").Rang;
            await EcranRequisitions.AgirAsync(Client, Greffe, vue.Session, "AjouterAuPanier", V(rang));
            await EcranRequisitions.AgirAsync(Client, Greffe, vue.Session, "Article", V(new { champ = "NbHistorique", valeur = "1" }));
            var sansGuichet = await EcranRequisitions.AgirAsync(Client, Greffe, vue.Session, "Terminer", V(null));
            Exiger(sansGuichet.Message == EcritureRequisitions.SansGuichet && sansGuichet.NbPanier == 1,
                $"pont Requisitions, Terminer sans « Guichet » : le refus du serveur, le panier reste (« {sansGuichet.Message} »)");
            await EcranRequisitions.AgirAsync(Client, Greffe, vue.Session, "BasculerCritere", V("CritGuichet"));
            var fin = await EcranRequisitions.AgirAsync(Client, Greffe, vue.Session, "Terminer", V(null));
            Exiger(fin.Message.StartsWith("Votre commande est terminée", StringComparison.Ordinal) && fin.Message.Contains("commande HISTO x1", StringComparison.Ordinal)
                   && fin.Message.Contains("CHISTO_1_IMP x1", StringComparison.Ordinal) && fin.NbPanier == 0,
                $"pont Requisitions, Terminer au guichet : ecrit par le serveur, relu par le pont, panier vide (« {fin.Message} »)");
        }
        finally { EcranRequisitions.Fermer(vue.Session); }
    }

    // ============================================================ A. ENREGISTRER UNE ARRIVEE (EcritureArrivee.cs)
    // Les modes de Program.cs (PTT Poste, EDI Edi, ACC Accueil) deviennent utilisables en saisie ; « L7N » ne l'est pas. Le compteur ARRD
    // de RIG_DEV (LAQC, annee sur 2 chiffres, lettre A, chrono sur 5) pour le jour du banc.
    private async Task ArriveeAsync()
    {
        await Sql("""
            IF COL_LENGTH('v2.MODE_ARRIVEE', 'R_MODEARRIVE_UTILISABLE_SAISIE') IS NULL
                ALTER TABLE v2.MODE_ARRIVEE ADD R_MODEARRIVE_UTILISABLE_SAISIE bit NOT NULL DEFAULT (1);
            """);
        await Sql("""
            INSERT INTO v2.MODE_ARRIVEE (R_MODEARRIVE_VALEUR, R_MODEARRIVE_LIBELLE_ECRAN, R_MODEARRIVE_UTILISABLE_SAISIE) VALUES ('L7N', 'Hors saisie', 0);
            IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = 'ARRD')
                INSERT INTO num.Compteur (Type, Annee, Jour, Libelle, Structure, CodageAnnee, Lettre, Separateur, TailleChrono, Debut)
                VALUES ('ARRD', YEAR(@aujourdhui), DATEPART(dayofyear, @aujourdhui), 'Numéro des arrivées, type Demande', 'LAQC', 2, 'A', '', 5, 1);
            """);
        Task<string> Arrivees() => Valeur("SELECT COUNT(*) FROM v2.ARRIVEE");

        // Refus : un mode hors saisie, un mode inconnu, une date illisible -- rien d'ecrit, aucun numero consomme, rien au journal.
        async Task Refus(string quoi, string? mode, string? date, string message)
        {
            var (avant, journal, chrono) = (await Arrivees(), await Journal(), await Valeur("SELECT ISNULL(SUM(Chrono), 0) FROM num.Compteur WHERE Type = 'ARRD'"));
            var r = await EcranArrivee.EnregistrerAsync(Client, Greffe, mode, date);
            Exiger(r is { Fait: false } && r.Message == message && await Arrivees() == avant && await Journal() == journal
                   && await Valeur("SELECT ISNULL(SUM(Chrono), 0) FROM num.Compteur WHERE Type = 'ARRD'") == chrono,
                $"Enregistrer une arrivee, {quoi} : refuse au message dit, rien d'ecrit, aucun numero consomme (« {r.Message} »)");
        }
        await Refus("mode L7N non utilisable en saisie", "L7N", null,
            "Le mode d'arrivée « L7N » n'est pas dans la liste des modes utilisables en saisie (table MODE_ARRIVEE).");
        await Refus("mode inconnu ZZZ", "zzz", null,
            "Le mode d'arrivée « ZZZ » n'est pas dans la liste des modes utilisables en saisie (table MODE_ARRIVEE).");
        await Refus("date 28/11/2026 (pas aaaa-mm-jj)", "PTT", "28/11/2026", "« Date d'arrivée » n'est pas une date : 28/11/2026.");

        // Jumeau du mode hors saisie : PTT (ici sans mode : le PTT de RIG v2, OPE_ARRIVEE.DoInitZoneSaisie) -> ecrite, numerotee ARRD,
        // case « A », l'instant de la creation ; une ligne au journal ; relue par le pont.
        var j0 = await Journal();
        var r1 = await EcranArrivee.EnregistrerAsync(Client, Greffe, "", "");
        var numero = r1.Ids is [var n] ? n : "";
        var attendu = $"A{Jour:yy}{Jour.DayOfYear:000}";
        Exiger(r1 is { Fait: true } && numero.StartsWith(attendu, StringComparison.Ordinal) && numero.Length == 11
               && await Valeur($"SELECT CONCAT(ARRV_MODE_ARRIVEE, '|', ARRV_CASE_COURRIER, '|', CAST(ARRV_PAIEMENT_JOINT AS int), '|', CONVERT(char(10), ARRV_DATEHEURE, 23)) FROM v2.ARRIVEE WHERE ARRV_NUM_ARRIVEE = '{numero}'")
                  == $"PTT|A|0|{Jour:yyyy-MM-dd}"
               && await Journal() == (int.Parse(j0) + 1).ToString(),
            $"Enregistrer une arrivee sans mode : PTT, numero ARRD du jour ({numero}), case A, une ligne au journal (« {r1.Message} »)");
        var relue = await EcranArrivee.TrouverAsync(Client, Greffe, new CriteresArrivee(numero, "", "", "", "", null, null, false, false, 50));
        Exiger(relue is { Mode: "serveur", Lignes: [{ ModeLisible: "Poste", Case: "A" } l] } && l.Numero == numero,
            $"Enregistrer une arrivee : relue par le pont (mode « Poste », case A) ({relue.Mode}, {relue.Lignes.Length} ligne(s))");

        // Jumeau de la date illisible : 2026-11-20 -> la date saisie a minuit (Ult_Date de RIG v2), le numero reste celui du jour.
        var r2 = await EcranArrivee.EnregistrerAsync(Client, Greffe, "acc", Jour.AddDays(-8).ToString("yyyy-MM-dd"));
        Exiger(r2 is { Fait: true, Ids: [var n2] } && n2.StartsWith(attendu, StringComparison.Ordinal) && n2 != numero
               && await Valeur($"SELECT CONCAT(ARRV_MODE_ARRIVEE, '|', CONVERT(char(16), ARRV_DATEHEURE, 120)) FROM v2.ARRIVEE WHERE ARRV_NUM_ARRIVEE = '{n2}'")
                  == $"ACC|{Jour.AddDays(-8):yyyy-MM-dd} 00:00",
            $"Enregistrer une arrivee le {Jour.AddDays(-8):dd/MM} en Accueil (saisi « acc ») : la date saisie a minuit, le numero du jour suivant (« {r2.Message} »)");

        // La route : un autre type de geste n'ecrit rien (Demonstration) ; sans serveur, rien non plus.
        var (sAutre, rAutre) = await Route(RoutesArrivee.Enregistrer, new GesteEcran(TypeGeste.Supprimer, [numero], new Dictionary<string, string>()));
        Exiger(sAutre == HttpStatusCode.OK && rAutre is { Fait: false, SansEcrivain: true },
            $"route Enregistrer une arrivee : un geste Supprimer n'est pas ecrit (« {rAutre?.Message} »)");
        Exiger(await EcranArrivee.EnregistrerAsync(null, null, "PTT", "") is { Fait: false, SansEcrivain: true },
            "pont Arrivee sans serveur : Enregistrer repond « Démonstration », rien d'ecrit");
    }

    // ============================================================ B. SUPPRIMER L'ETAT EN COURS (EcritureDemandes.cs)
    // 3700011 en cours (etat Q), rattachee a l'affaire 3700001 verrouillee par l'agent 2700002 ; 3700012 pas en cours ; 3700013 en cours,
    // pour l'agent d'Amitel. Les colonnes ecrites ajoutees a DEMANDE et AFFAIRE, LIGNE_RAPP_GENE creee aux types de RIG_DEV.
    private async Task DemandesAsync()
    {
        await Sql("""
            IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_AFFRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_AFFRE int NULL;
            IF COL_LENGTH('v2.AFFAIRE', 'AFFRE_ID_UTLST_EN_COURS') IS NULL ALTER TABLE v2.AFFAIRE ADD AFFRE_ID_UTLST_EN_COURS int NULL;
            IF OBJECT_ID('v2.LIGNE_RAPP_GENE') IS NULL
                CREATE TABLE v2.LIGNE_RAPP_GENE (LRPGN_ID_DMND int NULL, LRPGN_ID_UTLST int NULL, LRPGN_DATE_EFFECTIVE datetime NOT NULL,
                    LRPGN_NUM_LIGNE int NOT NULL, LRPGN_HEURE_DEBUT_SAISIE datetime NULL, LRPGN_HEURE_FIN datetime NULL,
                    LRPGN_ETAT_DEMANDE_SORTIE varchar(1) NOT NULL DEFAULT (''), LRPGN_SERVEUR_APPLI varchar(255) NULL,
                    LRPGN_ID_LRPGN int IDENTITY(3700001, 1) NOT NULL PRIMARY KEY);
            """);
        await Sql("""
            INSERT INTO v2.AFFAIRE (AFFRE_ID_AFFRE, AFFRE_LIBELLE, AFFRE_DATE_CREATION, AFFRE_ID_UTLST_EN_COURS) VALUES (3700001, 'L7 affaire verrouillee', @aujourdhui, 2700002);
            INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_NOM, DMND_ETAT_DEMANDE, DMND_EN_COURS, DMND_CODE_PROSS, DMND_DATE_CREATION, DMND_ID_AFFRE)
            VALUES (3700011, '3700011', 'L7 demande en cours', 'Q', 1, 'IMMAT', @aujourdhui, 3700001),
                   (3700012, '3700012', 'L7 demande pas en cours', 'Q', 0, 'IMMAT', @aujourdhui, NULL),
                   (3700013, '3700013', 'L7 demande en cours, Amitel', 'Q', 1, 'IMMAT', @aujourdhui, NULL);
            """);
        Task<string> Etat(int id) => Valeur($"SELECT CONCAT(DMND_ETAT_DEMANDE, '|', CAST(DMND_EN_COURS AS int)) FROM v2.DEMANDE WHERE DMND_ID_DMND = {id}");
        Task<string> Rapport(int id) => Valeur($"SELECT COUNT(*) FROM v2.LIGNE_RAPP_GENE WHERE LRPGN_ID_DMND = {id}");

        async Task Refus(string quoi, string numero, string message)
        {
            var (journal, rapports) = (await Journal(), await Valeur("SELECT COUNT(*) FROM v2.LIGNE_RAPP_GENE"));
            var (s, r) = await Route(RoutesDemandes.SupprimerEtatEnCours,
                new GesteEcran(TypeGeste.Agir, [numero], new Dictionary<string, string>()) { Nom = EcritureDemandes.Geste });
            Exiger(s == HttpStatusCode.UnprocessableEntity && r is { Fait: false } && r.Message == message && await Journal() == journal
                   && await Valeur("SELECT COUNT(*) FROM v2.LIGNE_RAPP_GENE") == rapports,
                $"Supprimer l'etat en cours, {quoi} : refuse (422) au message dit, rien d'ecrit ni au journal ({(int)s} « {r?.Message} »)");
        }
        await Refus("demande 3700012 pas en cours", "3700012", "La demande 3700012 n'est pas en cours : il n'y a pas d'état en cours à supprimer.");
        Exiger(await Etat(3700012) == "Q|0", "Supprimer l'etat en cours refuse : la demande 3700012 est intacte");
        await Refus("demande introuvable", "3799999", "Demande introuvable dans la base du greffe (3799999).");

        // Jumeau : 3700011 en cours -> plus en cours, etat Q inchange, l'affaire deverrouillee, la ligne du rapport general de l'agent
        // du greffe (IdV2 2700001) close a l'etat de sortie Q ; une ligne au journal ; relue par le pont.
        var j0 = await Journal();
        var r = await EcranDemandes.SupprimerEtatEnCoursAsync(Client, Greffe, " 3700011 ");
        Exiger(r is { Fait: true, Ids: ["3700011"] } && await Etat(3700011) == "Q|0"
               && await Valeur("SELECT ISNULL(CAST(AFFRE_ID_UTLST_EN_COURS AS varchar(10)), 'libre') FROM v2.AFFAIRE WHERE AFFRE_ID_AFFRE = 3700001") == "libre"
               && await Valeur("SELECT CONCAT(LRPGN_ID_UTLST, '|', LRPGN_NUM_LIGNE, '|', LRPGN_ETAT_DEMANDE_SORTIE, '|', CASE WHEN LRPGN_HEURE_FIN IS NULL THEN 'ouverte' ELSE 'close' END, '|', LRPGN_SERVEUR_APPLI) FROM v2.LIGNE_RAPP_GENE WHERE LRPGN_ID_DMND = 3700011")
                  == "2700001|1|Q|close|RIG v3/banc.main/Menu 'Supprimer état en cours'"
               && await Journal() == (int.Parse(j0) + 1).ToString(),
            $"Supprimer l'etat en cours de 3700011 : plus en cours, etat inchange, affaire deverrouillee, rapport general clos, journal (« {r.Message} »)");
        var relue = await EcranDemandes.TrouverAsync(Client, Greffe, new CriteresDemandes("3700011", "", "", null, null, [], false, false, 50), []);
        Exiger(relue is { Mode: "serveur" } && relue.Lignes.SingleOrDefault(l => l.Numero == "3700011") is { EnCours: false },
            "Supprimer l'etat en cours : relu par le pont, la demande n'est plus en cours");
        await Refus("la meme demande, deja retiree", "3700011", "La demande 3700011 n'est pas en cours : il n'y a pas d'état en cours à supprimer.");

        // Un agent d'Amitel (OPE_RESULTATS.cs:822) : la demande sort de l'etat en cours, sans ligne du rapport general.
        await using (var cn = new SqlConnection(Connexion))
        {
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT Id FROM soc.Compte WHERE CompteWindows = 'banc.main'", cn);
            var moi = new Appelant((int)(await cmd.ExecuteScalarAsync())!, "banc.main", "Banc Amitel", EstAmitel: true, MarqueAdministrateur: false,
                                   GereLesDroits: false);
            var amitel = await EcrivainsEcrans.JouerAsync(EcritureDemandes.Ecran, "BancParcoursLot7", EcritureDemandes.JouerAsync, cn,
                new GesteEcran(TypeGeste.Agir, ["3700013"], new Dictionary<string, string>()), moi, DateTime.Now, new DistributeurNumeros(), CancellationToken.None);
            Exiger(amitel is { Fait: true } && await Etat(3700013) == "Q|0" && await Rapport(3700013) == "0",
                $"Supprimer l'etat en cours par un agent d'Amitel : plus en cours, AUCUNE ligne du rapport general (« {amitel.Message} »)");
        }
        Exiger(await EcranDemandes.SupprimerEtatEnCoursAsync(Client, Greffe, " ") is { Fait: false, Message: "Aucune demande choisie : rien n'est modifié." }
               && await EcranDemandes.SupprimerEtatEnCoursAsync(null, null, "3700012") is { Fait: false, SansEcrivain: true },
            "pont Demandes : sans demande choisie, rien n'est envoye ; sans serveur, « Démonstration »");
    }

    // ============================================================ C. METTRE EN ATTENTE UNE COMMANDE WEB (EcritureCommandesWeb.cs)
    // Apres Lot20.cs (qui cree COMMANDE_WEB sans les colonnes du traitement) : 3700401 et 3700404 a traiter (A), 3700402 deja en attente
    // (T), 3700403 validee (V).
    private async Task CommandesWebAsync()
    {
        await Sql("""
            IF COL_LENGTH('v2.COMMANDE_WEB', 'CMDWB_ID_UTLST') IS NULL ALTER TABLE v2.COMMANDE_WEB ADD CMDWB_ID_UTLST int NULL;
            IF COL_LENGTH('v2.COMMANDE_WEB', 'CMDWB_DATE_MODIFICATION') IS NULL
                ALTER TABLE v2.COMMANDE_WEB ADD CMDWB_DATE_MODIFICATION datetime NOT NULL DEFAULT (getdate());
            """);
        await Sql("""
            INSERT INTO v2.COMMANDE_WEB (CMDWB_ID_CMDWB, CMDWB_REFERENCE_COMMANDE, CMDWB_NUMERO_SUIVI, CMDWB_DATE_COMMANDE, CMDWB_FAMILLE, CMDWB_TYPE_PRODUIT,
                CMDWB_ORIGINE, CMDWB_SIREN, CMDWB_DENOMINATION, CMDWB_NOM_REQUERANT, CMDWB_ETAT, CMDWB_MOTIF, CMDWB_DATE_TRAITEMENT)
            VALUES (3700401, 'L7-CW-1', 'L7-CW-1-1', DATEADD(hour, 8, @aujourdhui), 2, 'ET-CP', 'IG', '437000001', 'L7 ALPHA WEB', NULL, 'A', NULL, NULL),
                   (3700402, 'L7-CW-2', 'L7-CW-2-1', DATEADD(hour, 7, @aujourdhui), 2, 'ET-CP', 'IG', '437000002', 'L7 BETA WEB', NULL, 'T', 'Deja', NULL),
                   (3700403, 'L7-CW-3', 'L7-CW-3-1', DATEADD(hour, 6, @aujourdhui), 2, 'ET-CP', 'IG', '437000003', 'L7 GAMMA WEB', NULL, 'V', NULL, @aujourdhui),
                   (3700404, 'L7-CW-4', 'L7-CW-4-1', DATEADD(hour, 5, @aujourdhui), 2, 'ET-CP', 'IG', '437000004', 'L7 DELTA WEB', NULL, 'A', NULL, NULL);
            """);
        Task<string> Commande(int id) => Valeur($"SELECT CONCAT(CMDWB_ETAT, '|', ISNULL(CMDWB_MOTIF, '(null)'), '|', ISNULL(CAST(CMDWB_ID_UTLST AS varchar(10)), '(null)')) FROM v2.COMMANDE_WEB WHERE CMDWB_ID_CMDWB = {id}");
        GesteEcran Attente(int id, string? motif) => new(TypeGeste.Decider, [id.ToString()],
            motif is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [RoutesTraitementCommandesWeb.ChampMotif] = motif }) { Nom = "Mettre en attente" };

        async Task Refus(string quoi, int id, string? motif, string message)
        {
            var (avant, journal) = (await Commande(id), await Journal());
            var (s, r) = await Route(RoutesTraitementCommandesWeb.MettreEnAttente, Attente(id, motif));
            Exiger(s == HttpStatusCode.UnprocessableEntity && r is { Fait: false } && r.Message == message && await Commande(id) == avant
                   && await Journal() == journal,
                $"Mettre en attente, {quoi} : refuse (422) au message de NeoRIG, rien d'ecrit ({(int)s} « {r?.Message} »)");
        }
        await Refus("commande 3700402 deja en attente", 3700402, null, EcritureCommandesWeb.DejaEnAttente);
        await Refus("commande 3700403 validee", 3700403, null, EcritureCommandesWeb.DejaTraitee);
        await Refus("commande introuvable", 3799999, null, EcritureCommandesWeb.Introuvable);
        await Refus("motif de 501 caracteres", 3700404, new string('m', 501), EcritureCommandesWeb.MotifTropLong);

        // Jumeau du motif trop long : 500 caracteres, ecrits ; l'operateur est l'identifiant RIG v2 de l'agent (2700001).
        var j0 = await Journal();
        var (s500, r500) = await Route(RoutesTraitementCommandesWeb.MettreEnAttente, Attente(3700404, new string('m', 500)));
        Exiger(s500 == HttpStatusCode.OK && r500 is { Fait: true } && await Commande(3700404) == $"T|{new string('m', 500)}|2700001"
               && await Journal() == (int.Parse(j0) + 1).ToString(),
            $"Mettre en attente, motif de 500 caracteres : ecrit (etat T, motif, operateur 2700001), une ligne au journal (« {r500?.Message} »)");

        // Jumeau de « deja en attente » : 3700401 a traiter, par l'ecran (choisir, ouvrir la bande, saisir le motif, Decider), motif sans
        // ses blancs ; la session relit la base et ouvre la suivante.
        static JsonElement V(object? v) => JsonSerializer.SerializeToElement(v);
        var vue = await EcranCommandesWeb.OuvrirAsync(Client, Greffe);
        try
        {
            await EcranCommandesWeb.AgirAsync(Client, Greffe, vue.Session, "Choisir", V(3700401));
            await EcranCommandesWeb.AgirAsync(Client, Greffe, vue.Session, "OuvrirBande", V("MettreEnAttente"));
            await EcranCommandesWeb.AgirAsync(Client, Greffe, vue.Session, "Texte", V(new { champ = "MotifSaisi", valeur = "  Piece manquante L7 " }));
            var apres = await EcranCommandesWeb.AgirAsync(Client, Greffe, vue.Session, "Decider", V("MettreEnAttente"));
            Exiger(apres.Message is { Severite: "succes", Texte: var texte } && texte.StartsWith("Commande L7-CW-1 mise en attente.", StringComparison.Ordinal)
                   && await Commande(3700401) == "T|Piece manquante L7|2700001" && apres.Lignes.All(l => l.Id != 3700401),
                $"pont Commandes web, Mettre en attente : ecrit par le serveur, motif sans ses blancs, la liste relue n'a plus la commande a traiter (« {apres.Message?.Texte} »)");
            // Valider reste refuse (editions et facture absentes), rien d'ecrit.
            await EcranCommandesWeb.AgirAsync(Client, Greffe, vue.Session, "Choisir", V(3000401));
            var valider = await EcranCommandesWeb.AgirAsync(Client, Greffe, vue.Session, "Decider", V("Valider"));
            Exiger(valider.Message is { Texte: SessionCommandesWeb.SansEcriture } && await Valeur("SELECT CMDWB_ETAT FROM v2.COMMANDE_WEB WHERE CMDWB_ID_CMDWB = 3000401") == "A",
                "pont Commandes web : « Valider » reste refuse, la commande reste a traiter");
        }
        finally { EcranCommandesWeb.Fermer(vue.Session); }

        var relue = await EcranCommandesWeb.OuvrirAsync(Client, Greffe);
        try
        {
            await EcranCommandesWeb.AgirAsync(Client, Greffe, relue.Session, "ChoisirOnglet", V("EnAttente"));
            var panneau = (await EcranCommandesWeb.AgirAsync(Client, Greffe, relue.Session, "Choisir", V(3700401))).Panneau;
            Exiger(panneau is { MotifAttente: "Piece manquante L7" },
                $"pont Commandes web : relue dans une session neuve, la commande 3700401 est sous « En attente » avec son motif ({panneau?.MotifAttente ?? "absente"})");
        }
        finally { EcranCommandesWeb.Fermer(relue.Session); }
    }
}
