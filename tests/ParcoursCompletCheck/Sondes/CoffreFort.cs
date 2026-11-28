using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Coffre-fort » (PROC_COFFRE_FORT, Pont/EcranCoffreFortBase.cs) : « Re-transmettre » un dépôt de l'historique, le bouton de RIG v2
/// (FORM_COFFRE_FORT.cs:30, :76-80 ; OPE_TREEVIEW_RETRANSMETTRE.cs:507-640 ; écriture : Serveur/Socle/EcransMain/EcritureCoffreFort.cs),
/// joué par le pont puis relu par le pont : le dépôt en erreur d'envoi (8) passe à « 2 - Traité par le greffe », traité aujourd'hui, avec le
/// dernier document GED de son évènement. Les JUMEAUX refusés : un dépôt déjà « Envoyé à Extelia » (3 : « En attente de transmission »,
/// :125-126) et un évènement sans document indexé (« Document non numérisé », :103-106), rien d'écrit ni de journal. Lot 10 du plan
/// « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU : DEMAT_ETAT (absente : 500 au 28/11) créée si absente et ses états 2, 3, 4, 8 (libellés de RIG_DEV) ; les colonnes de DEMAT_CFEC
/// que la lecture et le geste lisent (le banc n'en a que deux), aux types de RIG_DEV (RigDatabase\dbo\Tables\DEMAT_CFEC.sql, nullables :
/// la table a déjà des lignes) ; VGIDX_ID_EVNPR de VIDIGED_INDEX ; deux évènements 4000001 et 4000002 (sans instance : aucun écran
/// judiciaire ne les liste), un index GED du premier (sans entité : la GED judiciaire ne le liste pas), et trois dépôts 4000001 (8, le
/// premier évènement), 4000002 (4, le second), 4000003 (3, le premier).
/// </summary>
public sealed class SondeCoffreFort : ISondeEcran
{
    private const string Ancien = "40000001-0000-4000-8000-000000000000", Nouveau = "40000001-0000-4000-8000-000000000001";

    public string Code => "PROC_COFFRE_FORT";

    public string? Jeu => $"""
        IF OBJECT_ID('v2.DEMAT_ETAT') IS NULL
            CREATE TABLE v2.DEMAT_ETAT (R_DEMATETAT_VALEUR varchar(2) NOT NULL PRIMARY KEY, R_DEMATETAT_LIBELLE_ECRAN varchar(35) NOT NULL,
                R_DEMATETAT_SYNONYME varchar(2) NOT NULL, R_DEMATETAT_UTILISABLE_SAISIE bit NOT NULL, R_DEMATETAT_DEVELOPPE_EDIT varchar(200) NOT NULL);
        IF OBJECT_ID('v2.DEMAT_CFEC') IS NULL CREATE TABLE v2.DEMAT_CFEC (CFEC_ID_CFEC int NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_DATE_CREATION') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_DATE_CREATION datetime NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_DATE_TRAITEMENT_RIG') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_DATE_TRAITEMENT_RIG datetime NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_USER_TRAITEMENT_RIG') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_USER_TRAITEMENT_RIG varchar(255) NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_ETAT') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_ETAT varchar(2) NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_GUID_DOCUMENT') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_GUID_DOCUMENT uniqueidentifier NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_NATURE_DOC') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_NATURE_DOC varchar(50) NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_ID_ENTITE') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_ID_ENTITE int NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_TYPE_ENTITE') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_TYPE_ENTITE varchar(25) NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_NUM_ENTITE') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_NUM_ENTITE varchar(50) NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_DATE_RECEPTION_AR') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_DATE_RECEPTION_AR datetime NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_DATE_DEPOT') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_DATE_DEPOT datetime NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_DATE_ACCUSE_RECEPTION') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_DATE_ACCUSE_RECEPTION datetime NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_DATE_LECTURE') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_DATE_LECTURE datetime NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_TYPE_DE_DESTINATAIRE') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_TYPE_DE_DESTINATAIRE varchar(50) NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_ID_DESTINATAIRE') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_ID_DESTINATAIRE int NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_NOM_DOCUMENT_DS_COFFRE') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_NOM_DOCUMENT_DS_COFFRE varchar(255) NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_DATE_DOCUMENT') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_DATE_DOCUMENT datetime NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_ERREUR') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_ERREUR varchar(max) NULL;
        IF COL_LENGTH('v2.DEMAT_CFEC', 'CFEC_IDENTIFIANT_DEPOT') IS NULL ALTER TABLE v2.DEMAT_CFEC ADD CFEC_IDENTIFIANT_DEPOT varchar(38) NULL;
        IF OBJECT_ID('v2.MANDATAIRE_PC') IS NULL CREATE TABLE v2.MANDATAIRE_PC (MNDPC_ID_MNDPC int NOT NULL PRIMARY KEY, MNDPC_NOM varchar(100) NOT NULL);
        IF COL_LENGTH('v2.MANDATAIRE_PC', 'MNDPC_ABREGE') IS NULL ALTER TABLE v2.MANDATAIRE_PC ADD MNDPC_ABREGE varchar(16) NULL;
        IF OBJECT_ID('v2.EVENEMENT_PROCEDURE') IS NULL
            CREATE TABLE v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR int NOT NULL PRIMARY KEY, EVNPR_CODE_EVE_PRO varchar(10) NOT NULL, EVNPR_DATE datetime NULL);
        IF OBJECT_ID('v2.VIDIGED_INDEX') IS NULL
            CREATE TABLE v2.VIDIGED_INDEX (VGIDX_ID_VGIDX int NOT NULL PRIMARY KEY, VGIDX_TYPE_ENTITE varchar(50) NULL, VGIDX_GUID_DOCUMENT uniqueidentifier NULL);
        IF COL_LENGTH('v2.VIDIGED_INDEX', 'VGIDX_ID_EVNPR') IS NULL ALTER TABLE v2.VIDIGED_INDEX ADD VGIDX_ID_EVNPR int NULL;
        -- Lot 16 : le compte rendu de la transmission, dans la file des editions (EcritureCoffreFort.cs) -- type de RIG_DEV (varchar(max)).
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_PARAMETRE_GENERATION') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_PARAMETRE_GENERATION varchar(max) NULL;
        EXEC sp_executesql N'
        INSERT INTO v2.DEMAT_ETAT (R_DEMATETAT_VALEUR, R_DEMATETAT_LIBELLE_ECRAN, R_DEMATETAT_SYNONYME, R_DEMATETAT_UTILISABLE_SAISIE, R_DEMATETAT_DEVELOPPE_EDIT)
        SELECT e.v, e.l, e.v, 1, e.d FROM (VALUES
            (''2'', ''Traité par le greffe'', ''Le fichier a été traité par le greffe''),
            (''3'', ''Envoyé à Extelia'', ''La réponse a été générée et envoyée à Extelia''),
            (''4'', ''Validé par Extelia'', ''La réponse a été validée par Extelia (état final)''),
            (''8'', ''Erreur d''''envoi à Extelia'', ''La génération de la réponse a échouée.'')) AS e (v, l, d)
        WHERE NOT EXISTS (SELECT 1 FROM v2.DEMAT_ETAT AS x WHERE x.R_DEMATETAT_VALEUR = e.v);
        INSERT INTO v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR, EVNPR_CODE_EVE_PRO, EVNPR_DATE) VALUES (4000001, ''L10EP'', DATEADD(day, -10, @aujourdhui)),
            (4000002, ''L10EP'', DATEADD(day, -10, @aujourdhui));
        INSERT INTO v2.VIDIGED_INDEX (VGIDX_ID_VGIDX, VGIDX_TYPE_ENTITE, VGIDX_ID_EVNPR, VGIDX_GUID_DOCUMENT) VALUES (4000001, ''INSTANCE'', 4000001, ''{Nouveau}'');
        INSERT INTO v2.DEMAT_CFEC (CFEC_ID_CFEC, CFEC_DATE_CREATION, CFEC_DATE_TRAITEMENT_RIG, CFEC_USER_TRAITEMENT_RIG, CFEC_ETAT, CFEC_GUID_DOCUMENT,
            CFEC_NATURE_DOC, CFEC_ID_ENTITE, CFEC_TYPE_ENTITE, CFEC_NUM_ENTITE, CFEC_TYPE_DE_DESTINATAIRE, CFEC_NOM_DOCUMENT_DS_COFFRE, CFEC_DATE_DOCUMENT)
        VALUES (4000001, DATEADD(day, -9, @aujourdhui), DATEADD(day, -9, @aujourdhui), ''ancien_agent'', ''8'', ''{Ancien}'', ''ORDO'', 4000001,
                ''EVENEMENT_PROCEDURE'', ''2026J00401'', ''MJ'', ''2026J00401 Ordonnance (sonde L10)'', DATEADD(day, -10, @aujourdhui)),
               (4000002, DATEADD(day, -9, @aujourdhui), DATEADD(day, -9, @aujourdhui), ''ancien_agent'', ''4'', NULL, ''ORDO'', 4000002,
                ''EVENEMENT_PROCEDURE'', ''2026J00402'', ''MJ'', ''2026J00402 Ordonnance (sonde L10)'', DATEADD(day, -10, @aujourdhui)),
               (4000003, DATEADD(day, -9, @aujourdhui), DATEADD(day, -9, @aujourdhui), ''ancien_agent'', ''3'', ''{Ancien}'', ''JUGT'', 4000001,
                ''EVENEMENT_PROCEDURE'', ''2026J00401'', ''MJ'', ''2026J00401 Jugement (sonde L10)'', DATEADD(day, -10, @aujourdhui));',
            N'@aujourdhui datetime', @aujourdhui;
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        async Task<DepotCoffre?> Depot(string id) => (await EcranCoffreFort.DepotsAsync(c.Client, c.Greffe)).Depots.FirstOrDefault(d => d.Code == id);
        var depart = await Depot("4000001");
        if (depart is null || !depart.Etat.StartsWith("8 - ", StringComparison.Ordinal))
            return new(false, false, $"le dépôt 4000001 n'est pas lu en erreur d'envoi avant le geste (« {depart?.Etat ?? "absent"} »)");

        // Les jumeaux refusés : un dépôt en attente de transmission (3), un évènement sans document indexé.
        var journal = await c.Journal();
        var attente = await EcranCoffreFort.RelancerTransmissionAsync(c.Client, c.Greffe, ["4000003"]);
        var sansDocument = await EcranCoffreFort.RelancerTransmissionAsync(c.Client, c.Greffe, ["4000002"]);
        var ensemble = await EcranCoffreFort.RelancerTransmissionAsync(c.Client, c.Greffe, ["4000001", "4000003"]);
        if (attente.Fait || sansDocument.Fait || ensemble.Fait || await c.Journal() != journal || (await Depot("4000001"))?.Etat != depart.Etat)
            return new(false, false, $"Re-transmettre un dépôt refusé par RIG v2 : en attente {(attente.Fait ? "fait" : "refusé")} « {attente.Message} », "
                                     + $"sans document {(sansDocument.Fait ? "fait" : "refusé")} « {sansDocument.Message} », les deux ensemble {(ensemble.Fait ? "fait" : "refusé")}");

        var r = await EcranCoffreFort.RelancerTransmissionAsync(c.Client, c.Greffe, ["4000001"]);
        if (!r.Fait) return new(false, false, $"Re-transmettre le dépôt 4000001 : refusé « {r.Message} »");
        var relu = await Depot("4000001");
        var controle = await c.Valeur("SELECT CONCAT(CAST(CFEC_GUID_DOCUMENT AS varchar(40)), '|', CFEC_USER_TRAITEMENT_RIG) FROM v2.DEMAT_CFEC WHERE CFEC_ID_CFEC = 4000001");
        var aujourdhui = c.Jour.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        // Lot 16 : le compte rendu Word de RIG v2 (OPE_TREEVIEW_RETRANSMETTRE.cs:544-549, :622), relu dans la file des editions.
        var compteRendu = await c.Valeur("""
            SELECT COUNT(*) FROM v2.DEMANDE_EDITION
            WHERE DMNED_LIBELLE_DOCUMENT LIKE N'Transmission EP[_]DJ le % à %h%m%s par banc[_]ecrans' AND DMNED_PHASE_TRAITEMENT = 'C'
              AND CAST(DMNED_PARAMETRE_GENERATION AS xml).value('(/PARAMETRES/@TITRE)[1]', 'nvarchar(200)') LIKE N'Transmission effectuée le % à % par banc[_]ecrans (1 dépôts)'
              AND CAST(DMNED_PARAMETRE_GENERATION AS xml).value('(/PARAMETRES/@TABLEAU)[1]', 'nvarchar(max)') LIKE N'%NUMERO_ENTITE="2026J00401" TYPE_DOCUMENT="ORDO" DESTINATAIRE="MJ"%'
            """);
        return relu is { Etat: "2 - Traité par le greffe" } && relu.TraitementRig == aujourdhui
               && controle.Equals($"{Nouveau}|banc_ecrans", StringComparison.OrdinalIgnoreCase)
               && compteRendu == "1" && r.Message.Contains("Compte rendu « Transmission EP_DJ le ", StringComparison.Ordinal)
            ? new(true, true, $"Re-transmettre fait (« {r.Message} »), relu par le pont : « {relu.Etat} », traité le {relu.TraitementRig}, dernier document GED "
                              + $"et agent relus en base, compte rendu relu dans la file des éditions ; refus « {attente.Message} » et « {sansDocument.Message} »")
            : new(true, false, $"Re-transmettre rendu fait, mais relu « {relu?.Etat ?? "absent"} » traité le {relu?.TraitementRig ?? "?"}, en base « {controle} », "
                               + $"compte rendu dans la file : {compteRendu} (« {r.Message} »)");
    }
}
