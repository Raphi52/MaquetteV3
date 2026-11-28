using System.Text.Json;
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Réquisitions » (PROC_REQUI, Pont/EcranRequisitions.cs + EcranRequisitionsBase.cs). Le pont ne parle au serveur que par ses gestes
/// (la recherche est le geste « Rechercher » de la session, EcranRequisitions.AgirAsync) : le banc ne devine pas ses données, cette
/// sonde les prouve (DonneesAsync) -- une raison sociale de la base du banc, cherchée par le pont, rend ce dossier, lu en base.
///
/// LE GESTE (lot 7 du plan « RIG v3 complet », 28/11/2026) : « Terminer » la commande de documents (D:\RigApplication\Source\RCS\Exe\
/// PROC_REQUI\frmREQUI.cs, Terminer :1399-2401), sur sa branche sans facture remise au guichet -- l'affaire et la demande REQUI, les
/// COMMANDE, la demande LPLURI du dossier et ses lignes d'édition, écrites par le serveur (Serveur/Socle/EcransMain/
/// EcritureRequisitions.cs, qui nomme ce qu'il ne reprend pas : facturation, clients, états d'inscriptions, impression), joué par le pont
/// puis relu par le pont (EcranRequisitions.LireCommandeAsync).
/// </summary>
// fix-ok: cause mesuree au banc ParcoursCompletCheck du 28/11/2026 (12:55) -- PROC_REQUI rouge « Terminer n'est pas repris : la V3
// n'ecrit rien en base » ; la sonde joue maintenant le geste ecrit par le serveur (EcritureRequisitions.cs) et le relit par le pont.
public sealed class SondeRequisitions : ISondeEcran
{
    public string Code => "PROC_REQUI";

    // LE JEU : les tables V2 que la recherche joint (Serveur/Socle/EcransMain/LectureEntreprisesRequi.cs : inscriptions du debiteur,
    // radiations, procedures collectives, codes de cloture, date de travail), creees si absentes aux types de RIG_DEV
    // (RigDatabase\dbo\Tables\DEBITEUR_END.sql, RADIATION_TOTALE.sql, PROCEDURE_COLLECTIVE.sql, CODE_CLOTURE.sql, DATE_TRAVAIL.sql) ;
    // une colonne absente d'une table deja creee par un jeu est ajoutee, vide (ses lignes n'en ont pas).
    public string? Jeu => """
        IF OBJECT_ID('v2.DEBITEUR_END') IS NULL
            CREATE TABLE v2.DEBITEUR_END (DBEND_ID_RPRTR int NULL, DBEND_ID_INSCR int NULL, DBEND_ID_ADRBS int NULL,
                DBEND_ID_DBEND int IDENTITY(3700001, 1) NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.DEBITEUR_END', 'DBEND_ID_RPRTR') IS NULL ALTER TABLE v2.DEBITEUR_END ADD DBEND_ID_RPRTR int NULL;
        IF COL_LENGTH('v2.DEBITEUR_END', 'DBEND_ID_INSCR') IS NULL ALTER TABLE v2.DEBITEUR_END ADD DBEND_ID_INSCR int NULL;
        IF COL_LENGTH('v2.DEBITEUR_END', 'DBEND_ID_ADRBS') IS NULL ALTER TABLE v2.DEBITEUR_END ADD DBEND_ID_ADRBS int NULL;
        IF OBJECT_ID('v2.RADIATION_TOTALE') IS NULL
            CREATE TABLE v2.RADIATION_TOTALE (RDTTL_ID_INSCR int NOT NULL, RDTTL_DATE_RADIATION datetime NOT NULL,
                RDTTL_ID_RDTTL int IDENTITY(3700001, 1) NOT NULL PRIMARY KEY);
        IF OBJECT_ID('v2.PROCEDURE_COLLECTIVE') IS NULL
            CREATE TABLE v2.PROCEDURE_COLLECTIVE (PRCLL_ID_PRCLL int IDENTITY(3700001, 1) NOT NULL PRIMARY KEY, PRCLL_ID_NINSC int NULL,
                PRCLL_ID_RPRTR int NOT NULL, PRCLL_ETAT_PC varchar(5) NOT NULL, PRCLL_CODE_CLOTURE varchar(5) NULL,
                PRCLL_NON_DIFFUSABLE bit NOT NULL DEFAULT (0));
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_ID_NINSC') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_ID_NINSC int NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_ID_RPRTR') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_ID_RPRTR int NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_ETAT_PC') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_ETAT_PC varchar(5) NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_CODE_CLOTURE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_CODE_CLOTURE varchar(5) NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_NON_DIFFUSABLE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_NON_DIFFUSABLE bit NULL;
        IF OBJECT_ID('v2.CODE_CLOTURE') IS NULL
            CREATE TABLE v2.CODE_CLOTURE (R_CODECLOTUR_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_CODECLOTUR_ARRET_DIFF_INFO bit NOT NULL DEFAULT (0));
        IF COL_LENGTH('v2.CODE_CLOTURE', 'R_CODECLOTUR_ARRET_DIFF_INFO') IS NULL ALTER TABLE v2.CODE_CLOTURE ADD R_CODECLOTUR_ARRET_DIFF_INFO bit NULL;
        IF OBJECT_ID('v2.DATE_TRAVAIL') IS NULL
            CREATE TABLE v2.DATE_TRAVAIL (DTTRV_CODE_DATE_TRAVAIL varchar(10) NOT NULL PRIMARY KEY, DTTRV_DATE_FRAICHEUR datetime NULL);
        IF COL_LENGTH('v2.DATE_TRAVAIL', 'DTTRV_DATE_FRAICHEUR') IS NULL ALTER TABLE v2.DATE_TRAVAIL ADD DTTRV_DATE_FRAICHEUR datetime NULL;
        -- Ce que « Terminer » ecrit (Serveur/Socle/EcransMain/EcritureRequisitions.cs), aux types de RIG_DEV (RigDatabase\dbo\Tables\
        -- AFFAIRE.sql, DEMANDE.sql, COMMANDE.sql, PSEUDO_VARIABLE.sql, LIGNE_EDITION.sql).
        IF OBJECT_ID('v2.AFFAIRE') IS NULL
            CREATE TABLE v2.AFFAIRE (AFFRE_ID_AFFRE int IDENTITY(3700001, 1) NOT NULL PRIMARY KEY, AFFRE_DATE_CREATION datetime NOT NULL);
        IF COL_LENGTH('v2.AFFAIRE', 'AFFRE_ID_UTLST_CREATION') IS NULL ALTER TABLE v2.AFFAIRE ADD AFFRE_ID_UTLST_CREATION int NULL;
        IF COL_LENGTH('v2.AFFAIRE', 'AFFRE_LIBELLE') IS NULL ALTER TABLE v2.AFFAIRE ADD AFFRE_LIBELLE varchar(70) NULL;
        IF OBJECT_ID('v2.DEMANDE') IS NULL
            CREATE TABLE v2.DEMANDE (DMND_ID_DMND int IDENTITY(3700001, 1) NOT NULL PRIMARY KEY, DMND_NUM_DEMANDE varchar(11) NOT NULL,
                DMND_ETAT_DEMANDE varchar(1) NOT NULL, DMND_EN_COURS bit NOT NULL DEFAULT (0));
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_AFFRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_AFFRE int NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM varchar(255) NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_NUM_INDICATIF') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NUM_INDICATIF varchar(14) NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_CODE_PROSS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_CODE_PROSS varchar(40) NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_DATE_CREATION') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_DATE_CREATION datetime NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM_UTILISATEUR') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM_UTILISATEUR varchar(255) NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_DSRCS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_DSRCS int NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_AUTRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_AUTRE int NULL;
        IF OBJECT_ID('v2.COMMANDE') IS NULL
            CREATE TABLE v2.COMMANDE (CMMDE_ID_CMMDE int IDENTITY(3700001, 1) NOT NULL PRIMARY KEY, CMMDE_DATE datetime NOT NULL,
                CMMDE_TYPE varchar(10) NULL, CMMDE_QTE smallint NULL, CMMDE_ID_DMND int NULL, CMMDE_ID_DSRCS int NULL, CMMDE_ID_NINSC int NULL,
                CMMDE_PROVENANCE varchar(10) NULL, CMMDE_GUICHET bit NULL);
        IF OBJECT_ID('v2.PSEUDO_VARIABLE') IS NULL
            CREATE TABLE v2.PSEUDO_VARIABLE (PSDVR_CPT int IDENTITY(3700001, 1) NOT NULL PRIMARY KEY, PSDVR_ID_DEMANDE int NOT NULL,
                PSDVR_NOM_VARIABLE varchar(50) NOT NULL, PSDVR_VALEUR_VARIABLE varchar(8000) NOT NULL);
        IF OBJECT_ID('v2.LIGNE_EDITION') IS NULL
            CREATE TABLE v2.LIGNE_EDITION (LGEDT_ID_LGEDT int IDENTITY(3700001, 1) NOT NULL PRIMARY KEY, LGEDT_ID_DMND int NULL,
                LGEDT_CODE_MODELE_DOC varchar(50) NOT NULL, LGEDT_TRANSMISSION varchar(20) NOT NULL, LGEDT_NUM_ORDRE smallint NOT NULL,
                LGEDT_NOMBRE smallint NOT NULL);
        -- La table d'un autre jeu n'en porte qu'une partie (LGEDT_CODE_MODELE_DOC absente, mesure du 28/11/2026) : NULL a l'ajout.
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_ID_DMND') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_ID_DMND int NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_CODE_MODELE_DOC') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_CODE_MODELE_DOC varchar(50) NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_ID_UTLST') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_ID_UTLST int NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_DESTINATION_PHYSIQUE') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_DESTINATION_PHYSIQUE varchar(50) NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_TRANSMISSION') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_TRANSMISSION varchar(20) NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_NUM_ORDRE') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_NUM_ORDRE smallint NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_NOMBRE') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_NOMBRE smallint NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_SCEAU') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_SCEAU varchar(255) NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_IMP_BAC') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_IMP_BAC varchar(10) NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_ETAT_EDT') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_ETAT_EDT varchar(1) NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_ETAT_TRS') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_ETAT_TRS varchar(1) NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_RECTO_VERSO') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_RECTO_VERSO bit NULL;
        """;

    public Task<PreuveSonde>? DonneesAsync(ContexteSonde c) => DonneesBaseAsync(c);

    private static async Task<PreuveSonde> DonneesBaseAsync(ContexteSonde c)
    {
        static JsonElement V(object? v) => JsonSerializer.SerializeToElement(v);
        // Une denomination d'un dossier RCS de la base du banc (celle que la recherche lit : rec.DossierRcs et son entreprise).
        var nom = await c.Valeur("""
            SELECT TOP (1) e.Denomination FROM rec.DossierRcs AS s JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
            WHERE LEN(e.Denomination) >= 4 AND e.Denomination NOT LIKE '%[0-9]%' ORDER BY s.Id
            """);
        if (nom.Length == 0) return new(false, false, "aucun dossier RCS avec une dénomination dans la base du banc : rien à chercher");
        var vue = EcranRequisitions.Ouvrir();
        try
        {
            await EcranRequisitions.AgirAsync(c.Client, c.Greffe, vue.Session, "Critere", V(new { champ = "CritRaisonSociale", valeur = nom }));
            var trouvee = await EcranRequisitions.AgirAsync(c.Client, c.Greffe, vue.Session, "Rechercher", V(null));
            return trouvee is { ModeResultats: "serveur" } && trouvee.Trouvees.Any(t => t.Designation.Contains(nom, StringComparison.OrdinalIgnoreCase))
                ? new(true, true, $"base (pont EcranRequisitions.AgirAsync « Rechercher » « {nom} » : {trouvee.Trouvees.Length} dossier(s) lu(s) en base)")
                : new(false, false, $"recherche « {nom} » par le pont : {trouvee.ModeResultats} « {trouvee.MessageResultats} », {trouvee.Trouvees.Length} ligne(s)"
                    + " DIAG manquants : " + await c.Valeur("""
                        SELECT STRING_AGG(x.t + '.' + x.c, ', ') FROM (VALUES
                          ('rec.Inscription','Id'),('rec.Inscription','DatePeremption'),('rec.Inscription','DateDiffusion'),
                          ('rec.Adresse','Id'),('rec.Adresse','NumeroVoie'),('rec.Adresse','TypeVoie'),('rec.Adresse','NomVoie'),('rec.Adresse','CodePostal'),('rec.Adresse','Localite'),
                          ('rec.TypeDemande','Id'),('rec.TypeDemande','IdDomaine'),('rec.TypeDemande','IdActivite'),('rec.TypeDemande','CodeDateTravail'),('rec.TypeDemande','Reference'),
                          ('rec.DossierRcs','DateRadiation'),('rec.Entreprise','Patronyme'),('rec.Entreprise','Prenom'),('rec.Entreprise','IdAdresse'),('rec.Entreprise','FormeJuridique'),
                          ('v2.DEBITEUR_END','DBEND_ID_INSCR'),('v2.RADIATION_TOTALE','RDTTL_ID_RDTTL'),('v2.PROCEDURE_COLLECTIVE','PRCLL_ID_RPRTR'),
                          ('v2.CODE_CLOTURE','R_CODECLOTUR_ARRET_DIFF_INFO'),('v2.DATE_TRAVAIL','DTTRV_DATE_FRAICHEUR')) AS x(t, c)
                        WHERE COL_LENGTH(x.t, x.c) IS NULL
                        """));
        }
        finally { EcranRequisitions.Fermer(vue.Session); }
    }

    /// <summary>
    /// « Terminer » la commande de documents, joué par le pont sur un dossier de la base du banc : un Kbis (et un certificat de
    /// non-faillite si le dossier le permet), remis au guichet, sans facture (Serveur/Socle/EcransMain/EcritureRequisitions.cs, ses
    /// sources RIG v2 et ce qu'il ne reprend pas). Relu par le pont (EcranRequisitions.LireCommandeAsync) : la demande REQUI validée,
    /// ses lignes de COMMANDE, la demande LPLURI du dossier et sa ligne d'édition KBISXML_1_PDF.
    /// </summary>
    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        static JsonElement V(object? v) => JsonSerializer.SerializeToElement(v);
        var nom = await c.Valeur("""
            SELECT TOP (1) e.Denomination FROM rec.DossierRcs AS s JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
            WHERE LEN(e.Denomination) >= 4 AND e.Denomination NOT LIKE '%[0-9]%' ORDER BY s.Id
            """);
        if (nom.Length == 0) return new(false, false, "aucun dossier RCS avec une dénomination dans la base du banc : rien à commander");
        var vue = EcranRequisitions.Ouvrir();
        try
        {
            await EcranRequisitions.AgirAsync(c.Client, c.Greffe, vue.Session, "Critere", V(new { champ = "CritRaisonSociale", valeur = nom }));
            var trouvee = await EcranRequisitions.AgirAsync(c.Client, c.Greffe, vue.Session, "Rechercher", V(null));
            if (trouvee.Trouvees.FirstOrDefault(t => t.Designation.Contains(nom, StringComparison.OrdinalIgnoreCase)) is not { } ligne)
                return new(false, false, $"recherche « {nom} » par le pont : {trouvee.ModeResultats}, aucune ligne à commander");
            var panier = await EcranRequisitions.AgirAsync(c.Client, c.Greffe, vue.Session, "AjouterAuPanier", V(ligne.Rang));
            await EcranRequisitions.AgirAsync(c.Client, c.Greffe, vue.Session, "Article", V(new { champ = "NbKBis", valeur = "1" }));
            var cnf = panier.Articles is { CnfPossible: true };
            if (cnf) await EcranRequisitions.AgirAsync(c.Client, c.Greffe, vue.Session, "Article", V(new { champ = "NbCertifNonFaillite", valeur = "1" }));
            await EcranRequisitions.AgirAsync(c.Client, c.Greffe, vue.Session, "BasculerCritere", V("CritGuichet"));
            var fin = await EcranRequisitions.AgirAsync(c.Client, c.Greffe, vue.Session, "Terminer", V(null));
            if (!fin.Message.StartsWith("Votre commande est terminée", StringComparison.Ordinal) || fin.NbPanier != 0)
                return new(false, false, $"Terminer « {ligne.Designation} » par le pont : « {fin.Message} » ({fin.NbPanier} commande(s) au panier)");

            // Relue par le pont, a part : la derniere demande REQUI de la base (controle SQL), lue par EcranRequisitions.LireCommandeAsync.
            var numero = await c.Valeur("SELECT TOP (1) DMND_NUM_DEMANDE FROM v2.DEMANDE WHERE DMND_CODE_PROSS = 'REQUI' ORDER BY DMND_ID_DMND DESC");
            var relue = await EcranRequisitions.LireCommandeAsync(c.Client, c.Greffe, numero);
            var lpluri = relue.Valeur?.Demandes.SingleOrDefault();
            var types = relue.Valeur?.Lignes.Select(l => $"{l.Type} x{l.Quantite}").ToArray() ?? [];
            var attendus = cnf ? new[] { "KBIS x1", "CNF x1" } : ["KBIS x1"];
            return relue.Valeur is { Requi: { Etat: "V", EnCours: false } requi } && types.SequenceEqual(attendus)
                   && relue.Valeur.Lignes.All(l => l.Guichet && l.IdDossier == requi.IdDossier)
                   && lpluri is { Etat: "V" } && lpluri.Nom.StartsWith($"REQUI de la {numero} - ", StringComparison.Ordinal)
                   && lpluri.Editions.Any(e => e.Modele == "KBISXML_1_PDF" && e.Nombre == 1)
                   && requi.Nom == lpluri.Numero
                ? new(true, true, $"Terminer fait (« {fin.Message[..fin.Message.IndexOf(')', StringComparison.Ordinal)]}) »), relu par le pont : "
                                + $"demande {requi.Numero} validée, commande {string.Join(", ", types)} au guichet, {lpluri.Numero} « {lpluri.Nom} » : "
                                + string.Join(", ", lpluri.Editions.Select(e => $"{e.Modele} x{e.Nombre}")))
                : new(true, false, $"Terminer rendu fait, mais relu ({relue.Mode} {relue.Message}) : demande {numero} "
                                 + $"{(relue.Valeur?.Requi is { } r ? $"état {r.Etat}, en cours {r.EnCours}, nom « {r.Nom} »" : "absente")}, "
                                 + $"lignes [{string.Join(", ", types)}], LPLURI {(lpluri is null ? "absente" : $"« {lpluri.Nom} » {lpluri.Etat} [{string.Join(", ", lpluri.Editions.Select(e => e.Modele))}]")}");
        }
        finally { EcranRequisitions.Fermer(vue.Session); }
    }
}
