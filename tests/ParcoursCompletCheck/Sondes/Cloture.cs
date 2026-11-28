using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Clôture » (PROC_CLOT, Pont/EcranCloture.cs + EcranClotureBase.cs) : le « Courrier aux débiteurs », mode de clôture 5 de RIG v2
/// (OPE_SELECT_PC.cs:1761-1775 ; écriture : Serveur/Socle/EcransMain/EcritureCloture.cs), joué par le pont sur une PC de la grille puis
/// relu par le pont : la colonne « Dernier courrier envoyé » de la PC porte la date du jour (OPE_SELECT_PC.cs:678-682), et la demande
/// d'édition de son courrier (INCIT_RDEB_CLOT_PC, source EVT_PROC = l'évènement) est dans la file des éditions. Lot 11 du plan « RIG v3
/// complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU (identifiants 41001xx, lot 11) : la PC P4100101, un examen de clôture à J+10 (évènement 4CLOT futur), un liquidateur en mission ;
/// le réglage CLOT_EVE_PRO_INCITR = CLOTINCITR, son code d'évènement (type C), son édition pour une PC et le paramètre de cette édition,
/// comme sur RIG_DEV (lus le 28/11/2026) ; les tables et colonnes que la lecture et l'écriture touchent, aux types de RIG_DEV.
/// </summary>
public sealed class SondeCloture : ISondeEcran
{
    private const int Pc = 4100101;
    private const string NumPc = "P4100101";

    public string Code => "PROC_CLOT";

    public string? Jeu => JeuSonde.EnDeuxTemps(JeuSonde.Demandes + """
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_NUM_PC') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_NUM_PC varchar(10) NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_NOM_AFFAIRE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_NOM_AFFAIRE varchar(250) NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_FORFAIT_FACTURE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_FORFAIT_FACTURE bit NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_EXTERIEUR') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_EXTERIEUR bit NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_CODE_CLOTURE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_CODE_CLOTURE varchar(5) NULL;
        IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_ID_DSRCS') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_ID_DSRCS int NULL;
        IF OBJECT_ID(N'v2.EVENEMENT_PROCEDURE') IS NULL
            CREATE TABLE v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR int IDENTITY(4100100, 1) NOT NULL PRIMARY KEY, EVNPR_CODE_EVE_PRO varchar(10) NOT NULL);
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_ID_PRCLL') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_ID_PRCLL int NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_ID_PREVE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_ID_PREVE int NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE datetime NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_SURVENU') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_SURVENU bit NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DELAI_EXPIRE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DELAI_EXPIRE bit NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE_MAXI') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE_MAXI datetime NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE_MINI') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE_MINI datetime NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE_FIXE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE_FIXE datetime NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_NUMERO_SOURCE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_NUMERO_SOURCE varchar(36) NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_TYPE_EVE_PRO') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_TYPE_EVE_PRO varchar(1) NULL;
        IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_NUM_ORDRE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_NUM_ORDRE smallint NULL;
        IF OBJECT_ID(N'v2.INSTANCE') IS NULL
            CREATE TABLE v2.INSTANCE (INSTN_ID_INSTN int NOT NULL PRIMARY KEY, INSTN_NUM_ROLE varchar(11) NULL);
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_ID_PRCLL') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_ID_PRCLL int NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_CODE_AFFAIRE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_CODE_AFFAIRE varchar(10) NULL;
        IF OBJECT_ID(N'v2.MANDATAIRE_PC') IS NULL
            CREATE TABLE v2.MANDATAIRE_PC (MNDPC_ID_MNDPC int NOT NULL PRIMARY KEY, MNDPC_CIVILITE varchar(7) NOT NULL, MNDPC_NOM varchar(100) NOT NULL,
                MNDPC_COMPLEMENT_NOM varchar(100) NULL);
        IF OBJECT_ID(N'v2.GERER_PC') IS NULL
            CREATE TABLE v2.GERER_PC (GRRPC_ID_GRRPC int NOT NULL PRIMARY KEY, GRRPC_ID_MNDPC int NOT NULL, GRRPC_ID_PRCLL int NULL,
                GRRPC_NUM_ORDRE smallint NOT NULL, GRRPC_FONCTION_PC varchar(5) NOT NULL, GRRPC_FIN_MISSION bit NOT NULL, GRRPC_ID_PREVE int NULL);
        IF OBJECT_ID(N'v2.CODE_EVE_PRO') IS NULL
            CREATE TABLE v2.CODE_EVE_PRO (R_CODEEVEPRO_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_CODEEVEPRO_LIBELLE_ECRAN varchar(35) NOT NULL);
        IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_TYPE_EVE_PRO') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_TYPE_EVE_PRO varchar(1) NULL;
        IF OBJECT_ID(N'v2.CEP_EDITION') IS NULL
            CREATE TABLE v2.CEP_EDITION (CEPED_ID_CEPED int NOT NULL PRIMARY KEY, CEPED_CODE_EVE_PRO varchar(10) NOT NULL, CEPED_TYPE_EP varchar(1) NOT NULL,
                CEPED_CODE_EDITION varchar(20) NOT NULL, CEPED_DESTI_MODE_ENVOI varchar(250) NULL, CEPED_CONDITION varchar(250) NULL,
                CEPED_AUTRE_PARAMETRE varchar(100) NULL);
        IF OBJECT_ID(N'v2.CEP_ARTICLE') IS NULL
            CREATE TABLE v2.CEP_ARTICLE (CEPAR_ID_CEPAR int NOT NULL PRIMARY KEY, CEPAR_CODE_EVE_PRO varchar(10) NULL, CEPAR_TYPE_EP varchar(1) NULL,
                CEPAR_CODE_ARTICLE varchar(20) NULL, CEPAR_CONDITION varchar(250) NULL, CEPAR_NUM_ORDRE smallint NOT NULL);
        IF OBJECT_ID(N'v2.PARAMETRE_EDITION') IS NULL
            CREATE TABLE v2.PARAMETRE_EDITION (PRMED_CODE varchar(40) NOT NULL PRIMARY KEY, PRMED_LIBELLE varchar(100) NOT NULL,
                PRMED_TYPE_GENERATION varchar(15) NULL, PRMED_LISTE varchar(4000) NOT NULL);
        IF COL_LENGTH('v2.PARAMETRE_EDITION', 'PRMED_CODE_MAQUETTE') IS NULL ALTER TABLE v2.PARAMETRE_EDITION ADD PRMED_CODE_MAQUETTE varchar(40) NULL;
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_CODE_ENTITE_SOURCE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_CODE_ENTITE_SOURCE varchar(16) NULL;
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_VALEUR_ID_SOURCE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_VALEUR_ID_SOURCE varchar(40) NULL;
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_CODE_MAQUETTE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_CODE_MAQUETTE varchar(40) NULL;
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_CODE_PROCESSUS') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_CODE_PROCESSUS varchar(40) NULL;
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_ID_DMND') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_ID_DMND int NULL;
        """, $"""
        IF NOT EXISTS (SELECT 1 FROM soc.Reglage WHERE Code = 'CLOT_EVE_PRO_INCITR')
            INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('CLOT_EVE_PRO_INCITR', 'CLOT : évènement du courrier aux débiteurs', 'CLOTINCITR');
        IF NOT EXISTS (SELECT 1 FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = 'CLOTINCITR')
            INSERT INTO v2.CODE_EVE_PRO (R_CODEEVEPRO_VALEUR, R_CODEEVEPRO_LIBELLE_ECRAN, R_CODEEVEPRO_TYPE_EVE_PRO)
            VALUES ('CLOTINCITR', 'incitation requête en clôture', 'C');
        IF NOT EXISTS (SELECT 1 FROM v2.CEP_EDITION WHERE CEPED_CODE_EVE_PRO = 'CLOTINCITR')
            INSERT INTO v2.CEP_EDITION (CEPED_ID_CEPED, CEPED_CODE_EVE_PRO, CEPED_TYPE_EP, CEPED_CODE_EDITION, CEPED_DESTI_MODE_ENVOI, CEPED_CONDITION)
            VALUES (4100121, 'CLOTINCITR', 'P', 'INCIT_RDEB_CLOT_PC', 'DIR', '');
        IF NOT EXISTS (SELECT 1 FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = 'INCIT_RDEB_CLOT_PC')
            INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_TYPE_GENERATION, PRMED_LISTE, PRMED_CODE_MAQUETTE)
            VALUES ('INCIT_RDEB_CLOT_PC', 'Incitation requête débiteur clôture PC', 'FUSION_RIG', '', 'LOT2_INCIT_REQ_CLOT');
        DECLARE @identite bit = CONVERT(bit, OBJECTPROPERTY(OBJECT_ID(N'v2.PROCEDURE_COLLECTIVE'), 'TableHasIdentity'));
        IF @identite = 1 SET IDENTITY_INSERT v2.PROCEDURE_COLLECTIVE ON;
        INSERT INTO v2.PROCEDURE_COLLECTIVE (PRCLL_ID_PRCLL, PRCLL_NUM_PC, PRCLL_NOM_AFFAIRE, PRCLL_EXTERIEUR, PRCLL_CODE_CLOTURE, PRCLL_FORFAIT_FACTURE)
        VALUES ({Pc}, '{NumPc}', 'SONDE COURRIER AUX DEBITEURS', 0, NULL, 0);
        IF @identite = 1 SET IDENTITY_INSERT v2.PROCEDURE_COLLECTIVE OFF;
        DECLARE @eve bit = CONVERT(bit, OBJECTPROPERTY(OBJECT_ID(N'v2.EVENEMENT_PROCEDURE'), 'TableHasIdentity'));
        IF @eve = 1 SET IDENTITY_INSERT v2.EVENEMENT_PROCEDURE ON;
        INSERT INTO v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR, EVNPR_ID_PRCLL, EVNPR_CODE_EVE_PRO, EVNPR_DATE, EVNPR_SURVENU, EVNPR_DELAI_EXPIRE,
            EVNPR_DATE_MAXI, EVNPR_TYPE_EVE_PRO, EVNPR_NUM_ORDRE)
        VALUES (4100111, {Pc}, '4CLOT', DATEADD(DAY, -20, @aujourdhui), 0, 0, DATEADD(DAY, 10, @aujourdhui), 'F', 1);
        IF @eve = 1 SET IDENTITY_INSERT v2.EVENEMENT_PROCEDURE OFF;
        INSERT INTO v2.MANDATAIRE_PC (MNDPC_ID_MNDPC, MNDPC_CIVILITE, MNDPC_NOM, MNDPC_COMPLEMENT_NOM) VALUES (4100191, 'MAITRE', 'SONDE', 'Liquidateur');
        INSERT INTO v2.GERER_PC (GRRPC_ID_GRRPC, GRRPC_ID_MNDPC, GRRPC_ID_PRCLL, GRRPC_NUM_ORDRE, GRRPC_FONCTION_PC, GRRPC_FIN_MISSION)
        VALUES (4100195, 4100191, {Pc}, 1, 'LIQ', 0);
        """);

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var avant = await EcranCloture.AffairesAsync(c.Client, c.Greffe, null, null);
        if (avant.Lignes.FirstOrDefault(l => l.NumPc == NumPc) is not { Id: { } id } ligne)
            return new(false, false, $"la PC {NumPc} du jeu n'est pas dans la grille ({avant.Mode}{(avant.Message is null ? "" : " « " + avant.Message + " »")}, " +
                                     $"{avant.Lignes.Length} ligne(s))");
        if (ligne.DernierCourrier.Length > 0) return new(false, false, $"la PC {NumPc} a déjà un courrier ({ligne.DernierCourrier}) avant le geste");

        var journal = await c.Journal();
        var r = await EcranCloture.EnvoyerCourrierDebiteursAsync(c.Client, c.Greffe, [id], facturer: false);
        if (!r.Fait) return new(false, false, $"Courrier aux débiteurs : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");
        if (await c.Journal() == journal) return new(true, false, "Courrier aux débiteurs rendu fait sans trace au journal des gestes");

        var apres = await EcranCloture.AffairesAsync(c.Client, c.Greffe, null, null);
        var relue = apres.Lignes.FirstOrDefault(l => l.NumPc == NumPc);
        var jour = c.Jour.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var courriers = await c.Valeur($"""
            SELECT COUNT(*) FROM v2.DEMANDE_EDITION AS de JOIN v2.EVENEMENT_PROCEDURE AS e ON e.EVNPR_NUMERO_SOURCE = de.DMNED_VALEUR_ID_SOURCE
            WHERE e.EVNPR_ID_PRCLL = {Pc} AND e.EVNPR_CODE_EVE_PRO = 'CLOTINCITR' AND e.EVNPR_SURVENU = 1 AND de.DMNED_CODE_ENTITE_SOURCE = 'EVT_PROC'
              AND de.DMNED_CODE_MAQUETTE = 'LOT2_INCIT_REQ_CLOT'
            """);
        return relue?.DernierCourrier == jour && courriers == "1"
            ? new(true, true, $"Courrier aux débiteurs fait (« {r.Message} »), relu par le pont : la PC {NumPc} porte son dernier courrier du {jour} ; " +
                              "contrôle SQL : son courrier INCIT_RDEB_CLOT_PC est dans la file des éditions")
            : new(true, false, $"Courrier aux débiteurs rendu fait, mais relu : dernier courrier « {relue?.DernierCourrier ?? "PC absente"} », " +
                               $"{courriers} courrier(s) dans la file");
    }
}
