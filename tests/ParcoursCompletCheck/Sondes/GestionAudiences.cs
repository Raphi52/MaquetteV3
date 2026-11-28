using RigV3.Contrats.EcransMain;
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Gestion des audiences » (PROC_GESTAUDS, Pont/EcranGestionAudiences.cs + EcranGestionAudiencesBase.cs) : un écran SANS geste
/// d'écriture dans RIG v2 -- boutons Valider et Vérifier cachés (FORM_GESTAUDS.cs:17-18), menu de la liste retiré
/// (OPE_RESULTAT_GESTAUDS.cs:167-168), « Supprimer » qui lirait une colonne absente (PROC_GESTAUDS.cs:272-280) : aucun geste n'est
/// inventé. Comme une forme générée sans geste (tests/ParcoursCompletCheck/Program.cs:31-32, 352-379), la sonde prouve sa RECHERCHE :
/// les critères de RIG v2 (OPE_RECHERCHE_GESTAUDS._Rechercher) appliqués PAR LE SERVEUR (LectureGestionAudiences.RechercherAsync), joués
/// par le pont, et les lignes relues EXACTEMENT. Décision du chef du plan (conv-2206, 28/11/2026, voie A). Lot 9 du plan « RIG v3 complet,
/// mesuré par le banc du parcours complet ».
///
/// LE JEU (AUDIENCE_CABINET_SUPPRIMEE, absente de la base du banc : 500 au 28/11, créée aux types de RIG_DEV) : une audience retenue et
/// trois JUMELLES, chacune écartée par un critère précis (la chambre, la date d'audience, le type de classement) ; un cabinet retenu et
/// son jumeau écarté par le juge. Identifiants 39005xx-39006xx (lot 9).
/// </summary>
public sealed class SondeGestionAudiences : ISondeEcran
{
    public string Code => "PROC_GESTAUDS";

    public string? Jeu => $"""
        IF OBJECT_ID('v2.AUDIENCE_CABINET_SUPPRIMEE') IS NULL
            CREATE TABLE v2.AUDIENCE_CABINET_SUPPRIMEE (AUDNCS_ID_AUDNCS int NOT NULL PRIMARY KEY, AUDNCS_ID_AUDNC int NOT NULL, AUDNCS_AUDIENCE bit NOT NULL,
                AUDNCS_TYPE_AUD_CAB varchar(5) NOT NULL, AUDNCS_TYPE_CLASSEMENT_ADMIS varchar(128) NULL, AUDNCS_DATE datetime NULL, AUDNCS_HEURE varchar(5) NULL,
                AUDNCS_NOMBRE_APPELS smallint NOT NULL, AUDNCS_NUM_APPEL_ATTRIB bit NOT NULL, AUDNCS_PUBLIQUE bit NOT NULL, AUDNCS_CHAMBRE varchar(5) NULL,
                AUDNCS_SECTION varchar(10) NULL, AUDNCS_ETAT_AUDIENCE varchar(5) NULL, AUDNCS_ID_JUG int NULL, AUDNCS_ID_GRASS int NULL,
                AUDNCS_CODE_CABINET varchar(10) NULL, AUDNCS_ETAT_CABINET varchar(1) NULL, AUDNCS_NON_DISPONIBLE_WEB bit NOT NULL, AUDNCS_TOP_DIFFUSION bit NOT NULL,
                AUDNCS_DATE_DIFFUSION datetime NULL, AUDNCS_AUTEUR_SUPPRESSION varchar(255) NOT NULL, AUDNCS_DATE_SUPPRESSION datetime NOT NULL);
        IF OBJECT_ID('v2.CODE_CABINET') IS NULL
            CREATE TABLE v2.CODE_CABINET (R_CODECABINE_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_CODECABINE_LIBELLE_ECRAN varchar(35) NOT NULL);
        IF OBJECT_ID('v2.CHAMBRE') IS NULL
            CREATE TABLE v2.CHAMBRE (R_CHAMBRE_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_CHAMBRE_LIBELLE_ECRAN varchar(35) NOT NULL);
        IF OBJECT_ID('v2.JUGE') IS NULL
            CREATE TABLE v2.JUGE (JUG_ID_JUG int NOT NULL PRIMARY KEY, JUG_CIVILITE varchar(7) NOT NULL, JUG_NOM varchar(35) NOT NULL);
        {ColonnesLot9.Ajouter("JUGE", ("JUG_ABREGE", "varchar(16) NULL"), ("JUG_PRENOM", "varchar(20) NULL"))}
        IF OBJECT_ID('v2.GREFFIER_ASSERMENTE') IS NULL
            CREATE TABLE v2.GREFFIER_ASSERMENTE (GRASS_ID_GRASS int NOT NULL PRIMARY KEY, GRASS_CIVILITE varchar(7) NOT NULL, GRASS_NOM varchar(35) NOT NULL);
        {ColonnesLot9.Ajouter("GREFFIER_ASSERMENTE", ("GRASS_ABREGE", "varchar(16) NOT NULL CONSTRAINT DF_P9G_GRA_ABR DEFAULT ('')"), ("GRASS_PRENOM", "varchar(20) NULL"))}
        EXEC sp_executesql N'
        INSERT INTO v2.CHAMBRE (R_CHAMBRE_VALEUR, R_CHAMBRE_LIBELLE_ECRAN) VALUES (''K39G'', ''Chambre G de la sonde 39''), (''K39H'', ''Chambre H de la sonde 39'');
        INSERT INTO v2.CODE_CABINET (R_CODECABINE_VALEUR, R_CODECABINE_LIBELLE_ECRAN) VALUES (''C39G'', ''Cabinet G de la sonde 39'');
        INSERT INTO v2.JUGE (JUG_ID_JUG, JUG_ABREGE, JUG_CIVILITE, JUG_NOM, JUG_PRENOM) VALUES (3900601, ''S39J1'', ''M.'', ''JUGE GESTAUDS'', ''Un''),
            (3900602, ''S39J2'', ''Mme'', ''JUGE AUTRE'', ''Deux'');
        INSERT INTO v2.GREFFIER_ASSERMENTE (GRASS_ID_GRASS, GRASS_ABREGE, GRASS_CIVILITE, GRASS_NOM, GRASS_PRENOM) VALUES (3900611, ''S39G1'', ''Mme'', ''GREFFIER GESTAUDS'', ''Anne'');
        INSERT INTO v2.AUDIENCE_CABINET_SUPPRIMEE (AUDNCS_ID_AUDNCS, AUDNCS_ID_AUDNC, AUDNCS_AUDIENCE, AUDNCS_TYPE_AUD_CAB, AUDNCS_TYPE_CLASSEMENT_ADMIS,
            AUDNCS_DATE, AUDNCS_HEURE, AUDNCS_NOMBRE_APPELS, AUDNCS_NUM_APPEL_ATTRIB, AUDNCS_PUBLIQUE, AUDNCS_CHAMBRE, AUDNCS_SECTION, AUDNCS_ID_JUG,
            AUDNCS_ID_GRASS, AUDNCS_CODE_CABINET, AUDNCS_NON_DISPONIBLE_WEB, AUDNCS_TOP_DIFFUSION, AUDNCS_DATE_DIFFUSION, AUDNCS_AUTEUR_SUPPRESSION,
            AUDNCS_DATE_SUPPRESSION) VALUES
            -- retenue ; jumelles : une autre chambre, une autre date (avril), un autre classement (X39G seul)
            (3900501, 3909501, 1, ''AU39G'', ''R39G;X39G'', ''20260310'', ''09:00'', 0, 0, 1, ''K39G'', ''K39G.A'', NULL, NULL, NULL, 0, 0, ''20260312'', ''sonde.parcours'', ''20260311''),
            (3900502, 3909502, 1, ''AU39G'', ''R39G'', ''20260310'', ''09:30'', 0, 0, 1, ''K39H'', ''K39G.A'', NULL, NULL, NULL, 0, 0, NULL, ''sonde.parcours'', ''20260311''),
            (3900503, 3909503, 1, ''AU39G'', ''R39G'', ''20260410'', ''09:00'', 0, 0, 1, ''K39G'', ''K39G.A'', NULL, NULL, NULL, 0, 0, NULL, ''sonde.parcours'', ''20260411''),
            (3900504, 3909504, 1, ''AU39G'', ''X39G'', ''20260310'', ''10:00'', 0, 0, 1, ''K39G'', ''K39G.A'', NULL, NULL, NULL, 0, 0, NULL, ''sonde.parcours'', ''20260311''),
            -- cabinet retenu ; jumeau : un autre juge
            (3900511, 3909511, 0, ''CB39G'', NULL, ''20260310'', ''14:00'', 0, 0, 0, NULL, NULL, 3900601, 3900611, ''C39G'', 0, 0, NULL, ''sonde.parcours'', ''20260311''),
            (3900512, 3909512, 0, ''CB39G'', NULL, ''20260310'', ''15:00'', 0, 0, 0, NULL, NULL, 3900602, NULL, ''C39G'', 0, 0, NULL, ''sonde.parcours'', ''20260311'');';
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        // Les jumelles sont bien dans la base (la liste d'ouverture les montre) : c'est le critère qui les écarte, pas leur absence.
        var tout = await EcranGestionAudiences.VueAsync(c.Client, c.Greffe);
        var jumelles = tout.Lignes.Count(l => l.Type is "AU39G" or "CB39G");
        if (tout.Mode != "serveur" || jumelles != 6)
            return new(false, false, $"Rechercher : la liste d'ouverture lit {tout.Mode}, {jumelles} ligne(s) du jeu sur 6 -- {tout.Message}");

        // Sans date de début, RIG v2 ne cherche rien (OPE_RECHERCHE_GESTAUDS.cs:172).
        var sansDate = await EcranGestionAudiences.RechercherAsync(c.Client, c.Greffe, new RechercheAudiencesSupprimees(false, Type: "AU39G"));
        if (sansDate is not { Mode: "serveur", Lignes.Length: 0, Message: not null })
            return new(false, false, $"Rechercher sans date de début : {sansDate.Mode}, {sansDate.Lignes.Length} ligne(s), « {sansDate.Message} » (RIG v2 ne cherche rien)");

        var audiences = await EcranGestionAudiences.RechercherAsync(c.Client, c.Greffe, new RechercheAudiencesSupprimees(false, AudienceDu: "2026-03-01",
            AudienceAu: "2026-03-31", Type: "AU39G", Chambre: "K39G", Section: "K39G.A", TypeClassement: "R39G", Max: 10));
        var cabinets = await EcranGestionAudiences.RechercherAsync(c.Client, c.Greffe, new RechercheAudiencesSupprimees(true, AudienceDu: "2026-03-10",
            Type: "CB39G", Juge: 3900601, CodeCabinet: "C39G", Max: 10));

        var a = audiences.Lignes.FirstOrDefault();
        var k = cabinets.Lignes.FirstOrDefault();
        var audienceOk = audiences is { Mode: "serveur", Lignes.Length: 1 } && a is
        {
            Jour: "Mardi", Date: "10/03/2026", Heure: "09:00", Type: "AU39G", Chambre: "K39G - Chambre G de la sonde 39", Section: "K39G.A",
            TypeClassement: "R39G;X39G", Cabinet: false, AuteurSuppression: "sonde.parcours", DateSuppression: "11/03/2026", EnvoiSuppression: "12/03/2026",
        };
        var cabinetOk = cabinets is { Mode: "serveur", Lignes.Length: 1 } && k is
        {
            Heure: "14:00", Cabinet: true, CodeCabinet: "C39G - Cabinet G de la sonde 39", Juge: "S39J1 - M. JUGE GESTAUDS Un",
            Greffier: "S39G1 - Mme GREFFIER GESTAUDS Anne",
        };
        return audienceOk && cabinetOk
            ? new(true, true, "lecture seule (RIG v2 n'écrit rien dans PROC_GESTAUDS) : recherche V2 jouée par le pont, appliquée par le serveur, relue "
                              + "exactement -- audiences (dates, type, chambre, section, classement) : 1 ligne, ses 3 jumelles écartées ; cabinets (date seule, "
                              + "type, juge, code) : 1 ligne, son jumeau écarté ; sans date de début, rien de cherché")
            : new(true, false, $"Rechercher : audiences {audiences.Mode} {string.Join(" ; ", audiences.Lignes.Select(l => $"{l.Date} {l.Heure} {l.Chambre} {l.TypeClassement}"))} ; "
                               + $"cabinets {cabinets.Mode} {string.Join(" ; ", cabinets.Lignes.Select(l => $"{l.Date} {l.Heure} {l.Juge}"))}");
    }
}
