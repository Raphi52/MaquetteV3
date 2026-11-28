using System.Text.Json;
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Réquisitions » (PROC_REQUI, Pont/EcranRequisitions.cs + EcranRequisitionsBase.cs). Le pont ne parle au serveur que par ses gestes
/// (la recherche est le geste « Rechercher » de la session, EcranRequisitions.AgirAsync) : le banc ne devine pas ses données, cette
/// sonde les prouve (DonneesAsync) -- une raison sociale de la base du banc, cherchée par le pont, rend ce dossier, lu en base.
///
/// LE GESTE N'EST PAS REPRIS (lot 7 du plan « RIG v3 complet », 28/11/2026) : le geste principal de RIG v2 est « Terminer » la
/// commande de documents (D:\RigApplication\Source\RCS\Exe\PROC_REQUI\frmREQUI.cs, Terminer :1399-2401) -- l'affaire et la demande
/// REQUI créées à l'ouverture (:143-155), le choix de l'imprimante et des clients facturé et destinataire (frmGestionImprimante,
/// :1438-1468), la facture (:1803-1910), les COMMANDE (InsererCommande :2528-2543), les LIGNE_EDITION et SELECTION des documents
/// (:2256-2330, :2847). La V3 ne les écrit pas encore (Pont/EcranRequisitions.cs, Terminer : « L impression et la facturation ne sont
/// pas encore portees (TODO #275) ») : la sonde le dit, la ligne reste rouge sur son geste, sans faux vert.
/// </summary>
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

    public Task<PreuveSonde> GesteAsync(ContexteSonde c) => Task.FromResult(new PreuveSonde(false, false,
        "« Terminer » la commande de documents (RIG v2 : affaire et demande REQUI, imprimante, clients, facture, COMMANDE, éditions — " +
        "frmREQUI.cs:1399-2401) n'est pas repris : la V3 n'écrit rien en base (Pont/EcranRequisitions.cs, Terminer), pas de faux vert"));
}
