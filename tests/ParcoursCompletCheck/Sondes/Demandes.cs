using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Demandes » (PROC_DEMANDE, Pont/EcranDemandes.cs + EcranDemandesBase.cs) : « Supprimer l'état en cours » d'une demande en cours,
/// le menu de la grille de RIG v2 (OPE_RESULTATS.tsmiDeleteEtatEnCoursDemande_Click, :814-833 ; écriture :
/// Serveur/Socle/EcransMain/EcritureDemandes.cs), joué par le pont puis relu par le pont : la demande n'est plus en cours, son état
/// ne change pas. Lot 7 du plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU : les colonnes que la lecture (LectureDemandes.cs) et le geste lisent, ajoutées à v2.DEMANDE si un jeu ne les a pas
/// (DMND_ID_UTLST manquait : 500 au 28/11), ETAT_DEMANDE, FACTURE, EDI_DOSSIER et LIGNE_RAPP_GENE créées si absentes, aux types de
/// RIG_DEV (RigDatabase\dbo\Tables\DEMANDE.sql, ETAT_DEMANDE.sql, LIGNE_RAPP_GENE.sql) ; la demande 3700001, en cours, état Q.
/// </summary>
public sealed class SondeDemandes : ISondeEcran
{
    private const string Numero = "D3700001";

    public string Code => "PROC_DEMANDE";

    public string? Jeu => """
        IF OBJECT_ID('v2.DEMANDE') IS NULL
            CREATE TABLE v2.DEMANDE (DMND_ID_DMND int NOT NULL PRIMARY KEY, DMND_NUM_DEMANDE varchar(11) NOT NULL, DMND_ETAT_DEMANDE varchar(1) NOT NULL);
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_DSRCS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_DSRCS int NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_UTLST') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_UTLST int NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_AFFRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_AFFRE int NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM varchar(255) NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_NUM_INDICATIF') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NUM_INDICATIF varchar(14) NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_EN_COURS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_EN_COURS bit NOT NULL DEFAULT (0);
        IF COL_LENGTH('v2.DEMANDE', 'DMND_CODE_PROSS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_CODE_PROSS varchar(40) NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_DATE_CREATION') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_DATE_CREATION datetime NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_DATE_VALIDATION') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_DATE_VALIDATION datetime NULL;
        IF OBJECT_ID('v2.ETAT_DEMANDE') IS NULL
            CREATE TABLE v2.ETAT_DEMANDE (R_ETATDEMAND_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_ETATDEMAND_LIBELLE_ECRAN varchar(35) NOT NULL);
        IF OBJECT_ID('v2.FACTURE') IS NULL CREATE TABLE v2.FACTURE (FACTU_NUMERO_FACTURE varchar(25) NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.FACTURE', 'FACTU_NUM_DEMANDE') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_NUM_DEMANDE varchar(32) NULL;
        IF COL_LENGTH('v2.FACTURE', 'FACTU_ID_CLIEN') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_ID_CLIEN int NULL;
        IF COL_LENGTH('v2.FACTURE', 'FACTU_DATE_CREATION') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_DATE_CREATION datetime NULL;
        IF OBJECT_ID('v2.EDI_DOSSIER') IS NULL CREATE TABLE v2.EDI_DOSSIER (EDIDO_ID_EDIDO int NOT NULL PRIMARY KEY, EDIDO_ID_DMND int NULL);
        IF OBJECT_ID('v2.LIGNE_RAPP_GENE') IS NULL
            CREATE TABLE v2.LIGNE_RAPP_GENE (LRPGN_ID_DMND int NULL, LRPGN_ID_UTLST int NULL, LRPGN_DATE_EFFECTIVE datetime NOT NULL,
                LRPGN_NUM_LIGNE int NOT NULL, LRPGN_HEURE_DEBUT_SAISIE datetime NULL, LRPGN_HEURE_FIN datetime NULL,
                LRPGN_ETAT_DEMANDE_SORTIE varchar(1) NOT NULL DEFAULT (''), LRPGN_SERVEUR_APPLI varchar(255) NULL,
                LRPGN_ID_LRPGN int IDENTITY(3700001, 1) NOT NULL PRIMARY KEY);
        EXEC sp_executesql N'
        IF NOT EXISTS (SELECT 1 FROM v2.ETAT_DEMANDE WHERE R_ETATDEMAND_VALEUR = ''Q'')
            INSERT INTO v2.ETAT_DEMANDE (R_ETATDEMAND_VALEUR, R_ETATDEMAND_LIBELLE_ECRAN) VALUES (''Q'', ''Qualifiée'');
        INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_NOM, DMND_ETAT_DEMANDE, DMND_EN_COURS, DMND_CODE_PROSS, DMND_DATE_CREATION)
        VALUES (3700001, ''D3700001'', ''SONDE LOT 7 - demande en cours'', ''Q'', 1, ''DCA'', @aujourdhui);',
            N'@aujourdhui datetime', @aujourdhui;
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var criteres = new CriteresDemandes(Numero, "", "", null, null, [], false, false, 50);
        var avant = await EcranDemandes.TrouverAsync(c.Client, c.Greffe, criteres, []);
        if (avant.Mode != "serveur" || avant.Lignes.SingleOrDefault(l => l.Numero == Numero) is not { EnCours: true } enCours)
            return new(false, false, $"Avant le geste, la demande {Numero} n'est pas lue en cours par le pont ({avant.Mode} « {avant.Message} »)");

        var r = await EcranDemandes.SupprimerEtatEnCoursAsync(c.Client, c.Greffe, Numero);
        if (!r.Fait) return new(false, false, $"Supprimer l'état en cours de {Numero} : refusé « {r.Message} »");

        var relue = (await EcranDemandes.TrouverAsync(c.Client, c.Greffe, criteres, [])).Lignes.SingleOrDefault(l => l.Numero == Numero);
        var rapport = await c.Valeur($"SELECT COUNT(*) FROM v2.LIGNE_RAPP_GENE WHERE LRPGN_ID_DMND = 3700001 AND LRPGN_ETAT_DEMANDE_SORTIE = 'Q'");
        return relue is { EnCours: false } && relue.Etat == enCours.Etat
            ? new(true, true, $"Supprimer l'état en cours de {Numero} fait (« {r.Message} »), relu par le pont : plus en cours, état « {relue.Etat} » inchangé"
                              + $" ; {rapport} ligne(s) du rapport général close(s) (agent du greffe seulement)")
            : new(true, false, $"Supprimer l'état en cours rendu fait, mais relu : {(relue is null ? "absente" : $"en cours = {relue.EnCours}, état « {relue.Etat} »")}");
    }
}
