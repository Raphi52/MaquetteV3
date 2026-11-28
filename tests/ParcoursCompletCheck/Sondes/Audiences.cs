using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Créer une audience » (PROC_AUDIENCE, Pont/EcranAudiences.cs + EcranAudiencesBase.cs) : « Créer l'audience », la saisie
/// OPE_CREATION_AUDIENCE de RIG v2 (écriture : Serveur/Socle/EcransMain/EcritureAudiences.cs), jouée par le pont puis relue par le pont :
/// l'audience est dans les audiences à venir, avec son type, sa chambre, sa section et ses cases. Lot 9 du plan « RIG v3 complet, mesuré
/// par le banc du parcours complet » (28/11/2026).
///
/// LES JUMEAUX, joués par le même pont et vérifiés sans écriture (journal des gestes inchangé) : la même saisie SANS chambre, que le type
/// exige (R_TYPEAUDCAB_PARAMETRAGE_CHAMBRE, OPE_CREATION_AUDIENCE.cs:65-74) -> « Champ obligatoire : Chambre. » ; puis la même audience
/// une seconde fois -> le doublon de RIG v2 (OPE_CREATION_AUDIENCE.cs:127-137).
///
/// LE JEU : les colonnes que la lecture et l'écriture lisent, aux types de RIG_DEV (copie de développement, INFORMATION_SCHEMA du
/// 28/11/2026 : TYPE_AUD_CAB, CHAMBRE, SECTION, TYPE_CLASSEMENT, AUDIENCE_CABINET), ajoutées si absentes -- R_TYPEAUDCAB_AUDIENCE manquait
/// à la base du banc (500 au 28/11) ; un type d'audience « AU39 » qui exige la chambre, la chambre « K39 » qui exige la section, sa section
/// « K39.A », le type de classement « R39 ». Codes « …39 » (lot 9).
/// </summary>
public sealed class SondeAudiences : ISondeEcran
{
    private const string Type = "AU39", Chambre = "K39", Section = "K39.A", Classement = "R39";

    public string Code => "PROC_AUDIENCE";

    public string? Jeu => """
        IF OBJECT_ID('v2.TYPE_AUD_CAB') IS NULL
            CREATE TABLE v2.TYPE_AUD_CAB (R_TYPEAUDCAB_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_TYPEAUDCAB_LIBELLE_ECRAN varchar(35) NOT NULL);
        IF COL_LENGTH('v2.TYPE_AUD_CAB', 'R_TYPEAUDCAB_PARAMETRAGE_CABINET') IS NULL
            ALTER TABLE v2.TYPE_AUD_CAB ADD R_TYPEAUDCAB_PARAMETRAGE_CABINET bit NOT NULL CONSTRAINT DF_P9_TAC_CAB DEFAULT (0);
        IF COL_LENGTH('v2.TYPE_AUD_CAB', 'R_TYPEAUDCAB_UTILISABLE_SAISIE') IS NULL
            ALTER TABLE v2.TYPE_AUD_CAB ADD R_TYPEAUDCAB_UTILISABLE_SAISIE bit NOT NULL CONSTRAINT DF_P9_TAC_SAISIE DEFAULT (1);
        IF COL_LENGTH('v2.TYPE_AUD_CAB', 'R_TYPEAUDCAB_AUDIENCE') IS NULL
            ALTER TABLE v2.TYPE_AUD_CAB ADD R_TYPEAUDCAB_AUDIENCE bit NOT NULL CONSTRAINT DF_P9_TAC_AUD DEFAULT (0);
        IF COL_LENGTH('v2.TYPE_AUD_CAB', 'R_TYPEAUDCAB_PARAMETRAGE_CHAMBRE') IS NULL
            ALTER TABLE v2.TYPE_AUD_CAB ADD R_TYPEAUDCAB_PARAMETRAGE_CHAMBRE bit NOT NULL CONSTRAINT DF_P9_TAC_CH DEFAULT (0);
        IF OBJECT_ID('v2.CHAMBRE') IS NULL
            CREATE TABLE v2.CHAMBRE (R_CHAMBRE_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_CHAMBRE_LIBELLE_ECRAN varchar(35) NOT NULL);
        IF COL_LENGTH('v2.CHAMBRE', 'R_CHAMBRE_UTILISABLE_SAISIE') IS NULL
            ALTER TABLE v2.CHAMBRE ADD R_CHAMBRE_UTILISABLE_SAISIE bit NOT NULL CONSTRAINT DF_P9_CH_SAISIE DEFAULT (1);
        IF COL_LENGTH('v2.CHAMBRE', 'R_CHAMBRE_PARAMETRAGE_SECTION') IS NULL
            ALTER TABLE v2.CHAMBRE ADD R_CHAMBRE_PARAMETRAGE_SECTION bit NOT NULL CONSTRAINT DF_P9_CH_SEC DEFAULT (0);
        IF OBJECT_ID('v2.SECTION') IS NULL
            CREATE TABLE v2.SECTION (R_SECTION_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_SECTION_LIBELLE_ECRAN varchar(35) NOT NULL);
        IF COL_LENGTH('v2.SECTION', 'R_SECTION_UTILISABLE_SAISIE') IS NULL
            ALTER TABLE v2.SECTION ADD R_SECTION_UTILISABLE_SAISIE bit NOT NULL CONSTRAINT DF_P9_SEC_SAISIE DEFAULT (1);
        IF OBJECT_ID('v2.TYPE_CLASSEMENT') IS NULL
            CREATE TABLE v2.TYPE_CLASSEMENT (R_TYPECLASSE_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_TYPECLASSE_LIBELLE_ECRAN varchar(35) NOT NULL);
        IF COL_LENGTH('v2.TYPE_CLASSEMENT', 'R_TYPECLASSE_UTILISABLE_SAISIE') IS NULL
            ALTER TABLE v2.TYPE_CLASSEMENT ADD R_TYPECLASSE_UTILISABLE_SAISIE bit NOT NULL CONSTRAINT DF_P9_TC_SAISIE DEFAULT (1);
        IF OBJECT_ID('v2.AUDIENCE_CABINET') IS NULL
            CREATE TABLE v2.AUDIENCE_CABINET (AUDNC_ID_ADNC int NOT NULL PRIMARY KEY, AUDNC_DATE datetime NULL, AUDNC_HEURE varchar(5) NULL);
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_AUDIENCE') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_AUDIENCE bit NOT NULL CONSTRAINT DF_P9_AC_AUD DEFAULT (1);
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_TYPE_AUD_CAB') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_TYPE_AUD_CAB varchar(5) NOT NULL CONSTRAINT DF_P9_AC_TYPE DEFAULT ('');
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_TYPE_CLASSEMENT_ADMIS') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_TYPE_CLASSEMENT_ADMIS varchar(128) NULL;
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_NOMBRE_APPELS') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_NOMBRE_APPELS smallint NOT NULL CONSTRAINT DF_P9_AC_NBAPP DEFAULT (0);
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_NUM_APPEL_ATTRIB') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_NUM_APPEL_ATTRIB bit NOT NULL CONSTRAINT DF_P9_AC_NUMAPP DEFAULT (0);
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_PUBLIQUE') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_PUBLIQUE bit NOT NULL CONSTRAINT DF_P9_AC_PUB DEFAULT (1);
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_CHAMBRE') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_CHAMBRE varchar(5) NULL;
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_SECTION') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_SECTION varchar(10) NULL;
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_ETAT_AUDIENCE') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_ETAT_AUDIENCE varchar(1) NULL;
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_ETAT_CABINET') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_ETAT_CABINET varchar(1) NULL;
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_TOP_DIFFUSION') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_TOP_DIFFUSION bit NOT NULL CONSTRAINT DF_P9_AC_TOP DEFAULT (0);
        IF COL_LENGTH('v2.AUDIENCE_CABINET', 'AUDNC_NON_DISPONIBLE_WEB') IS NULL ALTER TABLE v2.AUDIENCE_CABINET ADD AUDNC_NON_DISPONIBLE_WEB bit NOT NULL CONSTRAINT DF_P9_AC_WEB DEFAULT (0);
        IF OBJECT_ID('v2.APPEL_AFFAIRE') IS NULL CREATE TABLE v2.APPEL_AFFAIRE (APPAF_ID_APPAF int NOT NULL PRIMARY KEY, APPAF_ID_ADNC int NULL);
        EXEC sp_executesql N'
        INSERT INTO v2.TYPE_AUD_CAB (R_TYPEAUDCAB_VALEUR, R_TYPEAUDCAB_LIBELLE_ECRAN, R_TYPEAUDCAB_PARAMETRAGE_CABINET, R_TYPEAUDCAB_UTILISABLE_SAISIE,
            R_TYPEAUDCAB_AUDIENCE, R_TYPEAUDCAB_PARAMETRAGE_CHAMBRE) VALUES (''AU39'', ''Audience de la sonde 39'', 0, 1, 1, 1);
        INSERT INTO v2.CHAMBRE (R_CHAMBRE_VALEUR, R_CHAMBRE_LIBELLE_ECRAN, R_CHAMBRE_UTILISABLE_SAISIE, R_CHAMBRE_PARAMETRAGE_SECTION)
            VALUES (''K39'', ''Chambre de la sonde 39'', 1, 1);
        INSERT INTO v2.SECTION (R_SECTION_VALEUR, R_SECTION_LIBELLE_ECRAN, R_SECTION_UTILISABLE_SAISIE) VALUES (''K39.A'', ''Section A de la sonde 39'', 1);
        INSERT INTO v2.TYPE_CLASSEMENT (R_TYPECLASSE_VALEUR, R_TYPECLASSE_LIBELLE_ECRAN, R_TYPECLASSE_UTILISABLE_SAISIE) VALUES (''R39'', ''Renvoi de la sonde 39'', 1);';
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var date = c.Jour.AddDays(30).ToString("yyyy-MM-dd");
        var saisie = new SaisieAudience(date, "10:30", Type, Chambre, Section, [Classement], Publique: true, NonDisponibleWeb: false);

        // Jumeau refusé : le type exige la chambre.
        var journal = await c.Journal();
        var sansChambre = await EcranAudiences.CreerAsync(c.Client, c.Greffe, saisie with { Chambre = "", Section = "" });
        if (sansChambre.Fait || sansChambre.Message != "Champ obligatoire : Chambre." || await c.Journal() != journal)
            return new(false, false, $"Créer l'audience sans la chambre que le type exige : attendu le refus « Champ obligatoire : Chambre. », rendu "
                                     + $"{(sansChambre.Fait ? "fait" : "refusé")} « {sansChambre.Message} »");

        var r = await EcranAudiences.CreerAsync(c.Client, c.Greffe, saisie);
        if (!r.Fait || r.Ids is not [var id]) return new(false, false, $"Créer l'audience : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");

        var vue = await EcranAudiences.VueAsync(c.Client, c.Greffe);
        var lue = vue.Lignes.FirstOrDefault(l => l.Date == date && l.Heure == "10:30" && l.Type == "Audience de la sonde 39");
        var enBase = await c.Valeur($"SELECT CONCAT(AUDNC_CHAMBRE, '|', AUDNC_SECTION, '|', AUDNC_TYPE_CLASSEMENT_ADMIS) FROM v2.AUDIENCE_CABINET WHERE AUDNC_ID_ADNC = {int.Parse(id)}");

        // Jumeau refusé : la même audience une seconde fois (le doublon de RIG v2), rien d'écrit.
        journal = await c.Journal();
        var doublon = await EcranAudiences.CreerAsync(c.Client, c.Greffe, saisie);
        if (doublon.Fait || doublon.Message != "L'audience saisie existe déjà. Merci de vérifier votre saisie." || await c.Journal() != journal)
            return new(true, false, $"Créer l'audience fait (n°{id}), mais la même audience rejouée : {(doublon.Fait ? "faite" : "refusée")} « {doublon.Message} »");

        return vue.Mode == "serveur" && lue is { Chambre: "Chambre de la sonde 39", Section: "Section A de la sonde 39", Publique: true, Web: true, NbAffaires: 0 }
               && enBase == $"{Chambre}|{Section}|{Classement}"
            ? new(true, true, $"Créer l'audience fait (« {r.Message} »), relue par le pont : {lue.Date} {lue.Heure}, {lue.Type}, {lue.Chambre}, " +
                              $"{lue.Section} ; jumeaux refusés sans écriture (chambre exigée, doublon)")
            : new(true, false, $"Créer l'audience rendu fait (n°{id}), mais relue : {vue.Mode}, "
                               + (lue is null ? "absente des audiences à venir" : $"« {lue.Chambre} / {lue.Section} / web {lue.Web} »") + $", base « {enBase} »");
    }
}
