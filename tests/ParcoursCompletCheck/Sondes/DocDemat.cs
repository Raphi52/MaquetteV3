using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Documents dématérialisés » (PROC_DOC_DEMAT, Pont/EcranDocDemat.cs) : « Supprimer » une pièce puis la « Remettre en vigueur », le
/// menu de l'arbre de RIG v2 (UltTreeViewDocDemat.cs:290-316 ; écriture : Serveur/Socle/EcransMain/EcritureDocDemat.cs), joués par le pont
/// puis relus par le pont. Écran ouvert hors d'une demande = lecture seule : la V2 n'écrit la base que pour une personne d'Amitel
/// (:291, :306). Le JUMEAU refusé : le même geste par l'agent du banc (banc.ecrans, pas d'Amitel), rien d'écrit ni de journal. Lot 10 du
/// plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU : TYPE_PJ_CREFO et DEMAT_CREFO_PJ (absente : 500 au 28/11) créées si absentes, aux types de RIG_DEV (RigDatabase\dbo\Tables\
/// TYPE_PJ_CREFO.sql, DEMAT_CREFO_PJ.sql : DMCOPJ_ID_DMCOPJ IDENTITY) ; les colonnes de DEMAT_LIASSE_CFE, DEMAT_DAS et DEMANDE que la
/// lecture joint (LectureDocDemat.cs) ; une liasse 4000001 sans demande ni numéro INPI (aucun autre écran ne la liste), deux pièces en
/// vigueur ; le compte « sonde.l10.amitel » (Amitel), admis dans le greffe.
/// </summary>
public sealed class SondeDocDemat : ISondeEcran
{
    internal const string Amitel = "sonde.l10.amitel";
    private const string Fichier = "SONDE_L10_STATUTS.pdf";

    public string Code => "PROC_DOC_DEMAT";

    public string? Jeu => $"""
        IF NOT EXISTS (SELECT 1 FROM soc.Compte WHERE CompteWindows = '{Amitel}')
        BEGIN
            INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive, EstAmitel)
            SELECT TOP 1 '{Amitel}', 'AMITEL', 'Sonde L10', 'SL10AMI', 'M.', CodeGreffe, Id, 0, 1 FROM soc.Service ORDER BY Id;
            INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte WHERE CompteWindows = '{Amitel}';
        END
        IF OBJECT_ID('v2.TYPE_PJ_CREFO') IS NULL
            CREATE TABLE v2.TYPE_PJ_CREFO (R_TYPEPJCREF_VALEUR varchar(50) NOT NULL PRIMARY KEY, R_TYPEPJCREF_LIBELLE_ECRAN varchar(100) NOT NULL,
                R_TYPEPJCREF_DEVELOPPE_EDIT varchar(300) NOT NULL, R_TYPEPJCREF_CATEG_PJ varchar(10) NOT NULL, R_TYPEPJCREF_VISUALISABLE bit NOT NULL,
                R_TYPEPJCREF_ABREGE_PIECE varchar(16) NULL, R_TYPEPJCREF_NUM_ORDRE smallint NULL);
        IF OBJECT_ID('v2.DEMAT_LIASSE_CFE') IS NULL CREATE TABLE v2.DEMAT_LIASSE_CFE (DMTLCF_ID_DMTLCF int NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.DEMAT_LIASSE_CFE', 'DMTLCF_ID_DMND') IS NULL ALTER TABLE v2.DEMAT_LIASSE_CFE ADD DMTLCF_ID_DMND int NULL;
        IF COL_LENGTH('v2.DEMAT_LIASSE_CFE', 'DMTLCF_DATE_CREATION') IS NULL ALTER TABLE v2.DEMAT_LIASSE_CFE ADD DMTLCF_DATE_CREATION datetime NULL;
        IF OBJECT_ID('v2.DEMAT_DAS') IS NULL CREATE TABLE v2.DEMAT_DAS (DMTDAS_ID_DMTDAS int NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.DEMAT_DAS', 'DMTDAS_ID_DMND') IS NULL ALTER TABLE v2.DEMAT_DAS ADD DMTDAS_ID_DMND int NULL;
        IF COL_LENGTH('v2.DEMAT_DAS', 'DMTDAS_DATE_CREATION') IS NULL ALTER TABLE v2.DEMAT_DAS ADD DMTDAS_DATE_CREATION datetime NULL;
        IF OBJECT_ID('v2.DEMANDE') IS NULL
            CREATE TABLE v2.DEMANDE (DMND_ID_DMND int NOT NULL PRIMARY KEY, DMND_NUM_DEMANDE varchar(11) NOT NULL, DMND_ETAT_DEMANDE varchar(1) NOT NULL);
        IF COL_LENGTH('v2.DEMANDE', 'DMND_NUM_INDICATIF') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NUM_INDICATIF varchar(14) NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM varchar(255) NULL;
        IF OBJECT_ID('v2.DEMAT_CREFO_PJ') IS NULL
            CREATE TABLE v2.DEMAT_CREFO_PJ (DMCOPJ_ID_DMCOPJ int IDENTITY(4000001, 1) NOT NULL PRIMARY KEY, DMCOPJ_ID_DMTLCF int NULL, DMCOPJ_ID_DMTDAS int NULL,
                DMCOPJ_DATE_CREATION datetime NOT NULL, DMCOPJ_TYPE_PJ_CREFO varchar(50) NOT NULL, DMCOPJ_INDICE smallint NOT NULL,
                DMCOPJ_NUM_ORDRE smallint NOT NULL, DMCOPJ_NOM_FICHIER varchar(128) NOT NULL, DMCOPJ_DATE_PIECE date NULL,
                DMCOPJ_SUPPRIME bit NOT NULL DEFAULT (0), DMCOPJ_DATE_MODIFICATION datetime NULL, DMCOPJ_DATE_SUPPRIME datetime NULL,
                DMCOPJ_ETAT_INFOGREFFE char(15) NULL, DMCOPJ_ID_INPI varchar(30) NULL, DMCOPJ_GUID_GED_RIG uniqueidentifier NULL);
        EXEC sp_executesql N'
        IF NOT EXISTS (SELECT 1 FROM v2.TYPE_PJ_CREFO WHERE R_TYPEPJCREF_VALEUR = ''RCS-SONDE_STATUTS'')
            INSERT INTO v2.TYPE_PJ_CREFO (R_TYPEPJCREF_VALEUR, R_TYPEPJCREF_LIBELLE_ECRAN, R_TYPEPJCREF_DEVELOPPE_EDIT, R_TYPEPJCREF_CATEG_PJ,
                R_TYPEPJCREF_VISUALISABLE, R_TYPEPJCREF_ABREGE_PIECE, R_TYPEPJCREF_NUM_ORDRE)
            VALUES (''RCS-SONDE_STATUTS'', ''Statuts (sonde L10)'', ''Statuts mis à jour'', ''ACTE'', 1, ''STAT'', 1);
        INSERT INTO v2.DEMAT_LIASSE_CFE (DMTLCF_ID_DMTLCF, DMTLCF_DATE_CREATION) VALUES (4000001, DATEADD(day, 1, @aujourdhui));
        INSERT INTO v2.DEMAT_CREFO_PJ (DMCOPJ_ID_DMTLCF, DMCOPJ_DATE_CREATION, DMCOPJ_TYPE_PJ_CREFO, DMCOPJ_INDICE, DMCOPJ_NUM_ORDRE,
            DMCOPJ_NOM_FICHIER, DMCOPJ_DATE_PIECE, DMCOPJ_SUPPRIME)
        VALUES (4000001, @aujourdhui, ''RCS-SONDE_STATUTS'', -1, 1, ''{Fichier}'', @aujourdhui, 0);', N'@aujourdhui datetime', @aujourdhui;
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var id = await c.Valeur($"SELECT DMCOPJ_ID_DMCOPJ FROM v2.DEMAT_CREFO_PJ WHERE DMCOPJ_NOM_FICHIER = '{Fichier}'");
        if (id.Length == 0) return new(false, false, $"le jeu n'a pas posé la pièce {Fichier}");
        async Task<string?> Etat() => (await EcranDocDemat.PiecesAsync(c.Client, c.Greffe)).Pieces.FirstOrDefault(p => p.Id == id)?.Etat;

        // Le jumeau refusé : banc.ecrans n'est pas d'Amitel, l'arbre est en lecture seule.
        var journal = await c.Journal();
        var refus = await EcranDocDemat.ModifierEtatAsync(c.Client, c.Greffe, id, supprimer: true);
        if (refus.Fait || await Etat() != "En vigueur" || await c.Journal() != journal)
            return new(false, false, $"Supprimer par un agent qui n'est pas d'Amitel : {(refus.Fait ? "fait" : "refusé")} « {refus.Message} », relu « {await Etat()} »");

        var amitel = c.ClientDe(Amitel);
        var r = await EcranDocDemat.ModifierEtatAsync(amitel, c.Greffe, id, supprimer: true);
        if (!r.Fait) return new(false, false, $"Supprimer la pièce {id} : refusé « {r.Message} »");
        var apres = await Etat();
        var date = await c.Valeur($"SELECT CASE WHEN DMCOPJ_DATE_SUPPRIME IS NULL THEN 'vide' ELSE 'posée' END FROM v2.DEMAT_CREFO_PJ WHERE DMCOPJ_ID_DMCOPJ = {id}");
        if (apres != "Supprimée" || date != "posée")
            return new(true, false, $"Supprimer rendu fait (« {r.Message} »), mais relu « {apres ?? "absente"} », date de suppression {date}");

        // Le même geste une seconde fois : RIG v2 ne l'offre pas sur une pièce supprimée (CmsItem_Opening :236) ; puis la remise en vigueur.
        var deja = await EcranDocDemat.ModifierEtatAsync(amitel, c.Greffe, id, supprimer: true);
        var remise = await EcranDocDemat.ModifierEtatAsync(amitel, c.Greffe, id, supprimer: false);
        var finale = await Etat();
        return !deja.Fait && remise.Fait && finale == "En vigueur"
            ? new(true, true, $"Supprimer fait (« {r.Message} »), relu « {apres} » par le pont ; resupprimer refusé ; « Remettre en vigueur » fait, relu « {finale} » ; "
                              + $"refus hors Amitel « {refus.Message} »")
            : new(true, false, $"Supprimer fait et relu, mais resupprimer {(deja.Fait ? "accepté" : "refusé")}, remettre en vigueur "
                               + $"{(remise.Fait ? "fait" : "refusé « " + remise.Message + " »")}, relu « {finale} »");
    }
}
