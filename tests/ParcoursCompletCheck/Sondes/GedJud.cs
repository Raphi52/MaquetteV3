using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « GED judiciaire » (PROC_GEDJUD, Pont/EcranGedJud.cs) : rejeter un document avec son motif et un commentaire (« Modifier le
/// document » de RIG v2, ETP_GED_MODIF), puis le relire par le pont, et relire la trace VIDIGED_HISTO. Lot 8 du plan « parcours
/// complet » (28/11/2026). Le jeu donne a v2.VIDIGED_INDEX les colonnes que la lecture et le geste nomment (le 500 « Invalid column
/// name 'VGIDX_ETAT' » du 28/11), cree les tables lues absentes des jeux des ecrans generes, aux types de RIG_DEV, et un document en
/// attente d'une audience ; identifiants 38002xx.
/// </summary>
public sealed class SondeGedJud : ISondeEcran
{
    private const string Document = "3800201", Motif = "Pièce illisible (sonde du parcours)", Commentaire = "Rescanner la page 2";

    public string Code => "PROC_GEDJUD";

    public string? Jeu => """
        IF OBJECT_ID(N'v2.VIDIGED_INDEX') IS NULL CREATE TABLE v2.VIDIGED_INDEX (VGIDX_ID_VGIDX int NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.VIDIGED_INDEX', 'VGIDX_LIBELLE') IS NULL ALTER TABLE v2.VIDIGED_INDEX ADD VGIDX_LIBELLE varchar(200) NULL;
        IF COL_LENGTH('v2.VIDIGED_INDEX', 'VGIDX_DATE_RECEPTION') IS NULL ALTER TABLE v2.VIDIGED_INDEX ADD VGIDX_DATE_RECEPTION datetime NULL;
        IF COL_LENGTH('v2.VIDIGED_INDEX', 'VGIDX_NATURE_DOC') IS NULL ALTER TABLE v2.VIDIGED_INDEX ADD VGIDX_NATURE_DOC varchar(50) NULL;
        IF COL_LENGTH('v2.VIDIGED_INDEX', 'VGIDX_ID_ENTITE') IS NULL ALTER TABLE v2.VIDIGED_INDEX ADD VGIDX_ID_ENTITE int NULL;
        IF COL_LENGTH('v2.VIDIGED_INDEX', 'VGIDX_TYPE_ENTITE') IS NULL ALTER TABLE v2.VIDIGED_INDEX ADD VGIDX_TYPE_ENTITE varchar(50) NULL;
        IF COL_LENGTH('v2.VIDIGED_INDEX', 'VGIDX_COMMENTAIRE') IS NULL ALTER TABLE v2.VIDIGED_INDEX ADD VGIDX_COMMENTAIRE varchar(1000) NULL;
        IF COL_LENGTH('v2.VIDIGED_INDEX', 'VGIDX_GUID_DOCUMENT') IS NULL ALTER TABLE v2.VIDIGED_INDEX ADD VGIDX_GUID_DOCUMENT uniqueidentifier NULL;
        IF COL_LENGTH('v2.VIDIGED_INDEX', 'VGIDX_ETAT') IS NULL ALTER TABLE v2.VIDIGED_INDEX ADD VGIDX_ETAT varchar(3) NULL;
        IF COL_LENGTH('v2.VIDIGED_INDEX', 'VGIDX_MOTIF_REJET') IS NULL ALTER TABLE v2.VIDIGED_INDEX ADD VGIDX_MOTIF_REJET varchar(1000) NULL;
        IF OBJECT_ID(N'v2.VIDIGED_PARAMETRAGE') IS NULL
            CREATE TABLE v2.VIDIGED_PARAMETRAGE (VGPRM_ID_VGPRM int NOT NULL PRIMARY KEY, VGPRM_TYPE varchar(50) NULL, VGPRM_CODE varchar(50) NULL,
                VGPRM_LIBELLE varchar(1000) NULL);
        IF OBJECT_ID(N'v2.VIDIGED_DJ') IS NULL CREATE TABLE v2.VIDIGED_DJ (VGDDJ_ID_VGDDJ int NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.VIDIGED_DJ', 'VGDDJ_GUID_DOCUMENT') IS NULL ALTER TABLE v2.VIDIGED_DJ ADD VGDDJ_GUID_DOCUMENT uniqueidentifier NULL;
        IF COL_LENGTH('v2.VIDIGED_DJ', 'VGDDJ_ID_DCJDC') IS NULL ALTER TABLE v2.VIDIGED_DJ ADD VGDDJ_ID_DCJDC int NULL;
        IF COL_LENGTH('v2.VIDIGED_DJ', 'VGDDJ_NUM_ORDRE') IS NULL ALTER TABLE v2.VIDIGED_DJ ADD VGDDJ_NUM_ORDRE smallint NULL;
        IF OBJECT_ID(N'v2.DECISION_JUDICIAIRE') IS NULL CREATE TABLE v2.DECISION_JUDICIAIRE (DCJDC_ID_DCJDC int NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.DECISION_JUDICIAIRE', 'DCJDC_DATE') IS NULL ALTER TABLE v2.DECISION_JUDICIAIRE ADD DCJDC_DATE datetime NULL;
        IF COL_LENGTH('v2.DECISION_JUDICIAIRE', 'DCJDC_DATE_PREVUE') IS NULL ALTER TABLE v2.DECISION_JUDICIAIRE ADD DCJDC_DATE_PREVUE datetime NULL;
        IF OBJECT_ID(N'v2.PROCEDURE_COLLECTIVE') IS NULL CREATE TABLE v2.PROCEDURE_COLLECTIVE (PRCLL_ID_PRCLL int NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_NUM_PC') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_NUM_PC varchar(10) NULL;
        IF OBJECT_ID(N'v2.PREVENTION') IS NULL CREATE TABLE v2.PREVENTION (PREVE_ID_PREVE int NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.PREVENTION', 'PREVE_NUM_PRV') IS NULL ALTER TABLE v2.PREVENTION ADD PREVE_NUM_PRV varchar(10) NULL;
        IF OBJECT_ID(N'v2.MESURE') IS NULL CREATE TABLE v2.MESURE (MSINS_ID_MSINS int NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.MESURE', 'MSINS_NUM_MESURE') IS NULL ALTER TABLE v2.MESURE ADD MSINS_NUM_MESURE varchar(11) NULL;
        IF OBJECT_ID(N'v2.VIDIGED_HISTO') IS NULL
            CREATE TABLE v2.VIDIGED_HISTO (VGHIS_ID_VGHIS int NOT NULL PRIMARY KEY, VGHIS_GUID_DOCUMENT uniqueidentifier NULL, VGHIS_LOGIN varchar(50) NULL,
                VGHIS_DATE datetime NULL, VGHIS_ACTION varchar(200) NULL);
        EXEC sp_executesql N'
        INSERT INTO v2.VIDIGED_PARAMETRAGE (VGPRM_ID_VGPRM, VGPRM_TYPE, VGPRM_CODE, VGPRM_LIBELLE) VALUES
            (3800211, ''ETAT'', ''A'', ''En attente''), (3800212, ''ETAT'', ''V'', ''Validé''), (3800213, ''ETAT'', ''R'', ''Rejeté'');
        INSERT INTO v2.VIDIGED_INDEX (VGIDX_ID_VGIDX, VGIDX_LIBELLE, VGIDX_ID_ENTITE, VGIDX_TYPE_ENTITE, VGIDX_DATE_RECEPTION, VGIDX_GUID_DOCUMENT, VGIDX_ETAT)
            VALUES (3800201, ''SONDE-GED-3800201'', 3800301, ''AUDIENCE'', @aujourdhui, ''38002010-0000-0000-0000-000000000001'', ''A'');
        ', N'@aujourdhui datetime', @aujourdhui;
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var r = await EcranGedJud.ModifierAsync(c.Client, c.Greffe, Document, "R", Motif, Commentaire);
        if (!r.Fait) return new(false, false, $"Rejeter le document {Document} : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");
        var vue = await EcranGedJud.DocumentsAsync(c.Client, c.Greffe);
        var ligne = vue.Lignes.FirstOrDefault(l => l.Id == Document);
        var histo = await c.Valeur("SELECT VGHIS_ACTION FROM v2.VIDIGED_HISTO WHERE VGHIS_GUID_DOCUMENT = '38002010-0000-0000-0000-000000000001'");
        return ligne is { CodeEtat: "R", Etat: "Rejeté", MotifRejet: Motif, Commentaire: Commentaire, Statut: "alerte" }
               && histo == "Modification(s) - Commentaire - Changement d'état - Motif rejet"
            ? new(true, true, $"Rejeter un document fait (« {r.Message} »), relu par le pont : {ligne.Etat}, motif et commentaire ; historique « {histo} »")
            : new(true, false, $"Rejeter un document rendu fait, mais relu {(ligne is null ? "absent" : $"« {ligne.CodeEtat} / {ligne.MotifRejet} / {ligne.Commentaire} »")}, historique « {histo} »");
    }
}
