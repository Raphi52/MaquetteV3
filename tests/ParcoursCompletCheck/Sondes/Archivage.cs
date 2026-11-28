using RigV3.Contrats.EcransMain;
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Archivage / radiation » (PROC_ARCHIRAD, Pont/EcranArchivageBase.cs) : « Préparer le bordereau », un bordereau par dossier comme RIG v2
/// par défaut (PROC_ARCHIRAD.cs:155-217 ; écriture : Serveur/Socle/EcransMain/EcritureArchivage.cs), joué par le pont sur la recherche
/// « type B, radiés en 1989 », puis relu par le pont : le dossier dont une formalité n'est pas numérisée a reçu son annexe « Historique
/// des FORMALITES » et sort de la liste ; son jumeau, aux formalités numérisées (liasse VIDIGED), n'a pas de bordereau et y reste.
/// Contrôle SQL : la demande ARCHIRAD du dossier porte sa ligne d'édition BGEDANNEX_1_IMP. Lot 11 du plan « RIG v3 complet, mesuré par le
/// banc du parcours complet » (28/11/2026).
///
/// LE JEU (identifiants 41002xx, lot 11) : deux dossiers B1 radiés en 1989, chacun une formalité (inscription) de 1985 avec son passage CFE
/// -- 4100201 immatriculé en 1980, liasse non numérisée ; 4100202 immatriculé en 1985, liasse numérisée ; les tables V2 lues et écrites,
/// aux types de RIG_DEV (ACTION_RCS_GED_ANNEXES, INSCRIPTION_RCS, PASSAGE_CFE, LIGNE_EDITION : INFORMATION_SCHEMA de SQL-DEV\DEV, 28/11/2026).
/// </summary>
public sealed class SondeArchivage : ISondeEcran
{
    private const int AEditer = 4100201, Numerise = 4100202;

    public string Code => "PROC_ARCHIRAD";

    private static readonly RechercheArchivage Recherche = new(new DateOnly(1989, 1, 1), new DateOnly(1989, 12, 31), "B", null);

    public string? Jeu => JeuSonde.EnDeuxTemps(JeuSonde.Demandes + JeuSonde.LignesEdition + """
        IF OBJECT_ID(N'v2.ACTION_RCS_GED_ANNEXES') IS NULL
            CREATE TABLE v2.ACTION_RCS_GED_ANNEXES (ACRGA_ID_ACRGA int IDENTITY(4100200, 1) NOT NULL PRIMARY KEY, ACRGA_ID_ACTRC int NOT NULL,
                ACRGA_LIBELLE_PIECE varchar(256) NOT NULL);
        IF COL_LENGTH('v2.ACTION_RCS_GED_ANNEXES', 'ACRGA_IS_DEMAT') IS NULL ALTER TABLE v2.ACTION_RCS_GED_ANNEXES ADD ACRGA_IS_DEMAT bit NOT NULL DEFAULT (0);
        IF COL_LENGTH('v2.ACTION_RCS_GED_ANNEXES', 'ACRGA_DATE_CREATION') IS NULL ALTER TABLE v2.ACTION_RCS_GED_ANNEXES ADD ACRGA_DATE_CREATION datetime NULL;
        IF COL_LENGTH('v2.ACTION_RCS_GED_ANNEXES', 'ACRGA_NOM_UTILISATEUR_CREATION') IS NULL
            ALTER TABLE v2.ACTION_RCS_GED_ANNEXES ADD ACRGA_NOM_UTILISATEUR_CREATION varchar(50) NULL;
        IF OBJECT_ID(N'v2.INSCRIPTION_RCS') IS NULL
            CREATE TABLE v2.INSCRIPTION_RCS (INSRC_ID_INSRC int NOT NULL PRIMARY KEY, INSRC_DATE_FORMALITE datetime NOT NULL);
        IF COL_LENGTH('v2.INSCRIPTION_RCS', 'INSRC_DATE_FORMALITE') IS NULL ALTER TABLE v2.INSCRIPTION_RCS ADD INSRC_DATE_FORMALITE datetime NULL;
        IF OBJECT_ID(N'v2.PASSAGE_CFE') IS NULL
            CREATE TABLE v2.PASSAGE_CFE (PSSGC_ID_PSSCF int NOT NULL PRIMARY KEY, PSSGC_VIDI_GUID_LIASSE uniqueidentifier NULL);
        IF COL_LENGTH('v2.PASSAGE_CFE', 'PSSGC_VIDI_GUID_LIASSE') IS NULL ALTER TABLE v2.PASSAGE_CFE ADD PSSGC_VIDI_GUID_LIASSE uniqueidentifier NULL;
        """, $"""
        SET IDENTITY_INSERT rec.Entreprise ON;
        INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
            ({AEditer}, NULL, '', N'SONDE ARCHIVAGE A NUMERISER', 'C', 0), ({Numerise}, NULL, '', N'SONDE ARCHIVAGE NUMERISE', 'C', 0);
        SET IDENTITY_INSERT rec.Entreprise OFF;
        SET IDENTITY_INSERT rec.DossierRcs ON;
        INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
            ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
            SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise, DateRadiation) VALUES
            ({AEditer}, {AEditer}, 'B1', '1989B41201', NULL, '19800101', 'C', 0, 0, '19890101', 'R', 'C', 0, 0, 0, 0, 0, 0, 0, '19890615'),
            ({Numerise}, {Numerise}, 'B1', '1989B41202', NULL, '19850101', 'C', 0, 0, '19890101', 'R', 'C', 0, 0, 0, 0, 0, 0, 0, '19890701');
        SET IDENTITY_INSERT rec.DossierRcs OFF;
        INSERT INTO v2.INSCRIPTION_RCS (INSRC_ID_INSRC, INSRC_DATE_FORMALITE) VALUES (4100221, '19850301'), (4100222, '19850301');
        INSERT INTO v2.PASSAGE_CFE (PSSGC_ID_PSSCF, PSSGC_VIDI_GUID_LIASSE) VALUES (4100231, NULL), (4100232, NEWID());
        SET IDENTITY_INSERT rec.ActionRegistre ON;
        INSERT INTO rec.ActionRegistre (Id, IdDossier, IdInscriptionRegistre, IdPassageCfe, CodeProcessus, EtatInfo, EtatAction, SurNotification) VALUES
            (4100211, {AEditer}, 4100221, 4100231, 'MODIF', 'C', 'V', 0), (4100212, {Numerise}, 4100222, 4100232, 'MODIF', 'C', 'V', 0);
        SET IDENTITY_INSERT rec.ActionRegistre OFF;
        """);

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        string Numeros(VueArchivage v) => string.Join(",", v.Lignes.Select(l => l.Dossier));
        var avant = await EcranArchivage.DossiersAsync(c.Client, c.Greffe, Recherche);
        if (!avant.Lignes.Any(l => l.Dossier == "1989B41201") || !avant.Lignes.Any(l => l.Dossier == "1989B41202"))
            return new(false, false, $"les dossiers du jeu ne sont pas dans la recherche ({avant.Mode}{(avant.Message is null ? "" : " « " + avant.Message + " »")} : {Numeros(avant)})");

        var r = await EcranArchivage.CreerBordereauAsync(c.Client, c.Greffe, Recherche);
        if (!r.Fait) return new(false, false, $"Préparer le bordereau : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");
        if (r.Ids is not [var fait] || fait != AEditer.ToString())
            return new(true, false, $"Préparer le bordereau rendu fait sur {string.Join(",", r.Ids)} au lieu du seul dossier {AEditer} (« {r.Message} »)");

        var apres = await EcranArchivage.DossiersAsync(c.Client, c.Greffe, Recherche);
        var bordereau = await c.Valeur($"""
            SELECT COUNT(*) FROM v2.LIGNE_EDITION AS l JOIN v2.DEMANDE AS d ON d.DMND_ID_DMND = l.LGEDT_ID_DMND
            WHERE d.DMND_ID_DSRCS = {AEditer} AND d.DMND_CODE_PROSS = 'ARCHIRAD' AND d.DMND_ETAT_DEMANDE = 'V' AND l.LGEDT_CODE_MODELE_DOC = 'BGEDANNEX_1_IMP'
            """);
        return !apres.Lignes.Any(l => l.Dossier == "1989B41201") && apres.Lignes.Any(l => l.Dossier == "1989B41202") && bordereau == "1"
            ? new(true, true, $"Préparer le bordereau fait (« {r.Message} »), relu par le pont : 1989B41201 sort de la liste (annexe « Historique des " +
                              "FORMALITES »), son jumeau numérisé 1989B41202 y reste ; contrôle SQL : la demande ARCHIRAD du dossier porte BGEDANNEX_1_IMP")
            : new(true, false, $"Préparer le bordereau rendu fait, mais relu : liste « {Numeros(apres)} », {bordereau} bordereau(x)");
    }
}
