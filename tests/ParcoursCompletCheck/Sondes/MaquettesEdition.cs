using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.EcransMain;
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Modèles d'édition » (PROC_EDIT_MAQUETTE, Pont/EcranMaquettesEdition.cs) : « Valider » un paramètre d'édition, le bouton Valider
/// d'OPE_PARAM_EDT_MAJ de RIG v2 (OPE_PARAM_EDT_MAJ.cs:494-527 ; écriture : Serveur/Socle/EcransMain/EcritureParametresEdition.cs), joué par
/// le pont (EcranMaquettesEdition.ParametreAsync, geste Enregistrer) puis relu par le pont : le paramètre de la maquette porte le nombre
/// d'exemplaires et la case Recto/Verso saisis, et sa copie XML PRMED_LISTE (que RIG v2 recopie par-dessus les colonnes à sa lecture,
/// ParametreEdition._UpdateChampsFromXml) les porte aussi. Les gestes d'une MAQUETTE (Modifier, Dupliquer, Supprimer, Valider) passent
/// par son document Word en GED (OPE_MAQUETTE.cs:160-246) et ne sont pas repris : le geste prouvé est celui du paramètre, le seul que RIG v3
/// écrit. LE JUMEAU, refusé sans écriture (journal des gestes inchangé) : « Nb exemplaires » à 0 (DoVerifier, :309-327 : « Le nombre
/// d'exemplaires est obligatoire. »). Lot 14 du plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU : les colonnes que LectureMaquettesEdition.cs et EcritureParametresEdition.cs lisent, aux types de RIG_DEV (INFORMATION_SCHEMA de
/// SQL-DEV\DEV, lu le 28/11/2026), ajoutées aux tables présentes -- v2.MAQUETTE du banc, créée par PROC_REDAA.sql et PROC_RECHERCHE_GED.sql,
/// n'avait pas MQTT_EXTENSION (500 au 28/11) --, les tables liées créées si absentes ; une maquette RIG et son paramètre. Codes S44_*.
/// fix-ok: cause mesurée par le banc du parcours (28/11/2026, avant cette sonde) : « SqlException : Invalid column name 'MQTT_EXTENSION' » à la
/// lecture des maquettes ; fichier neuf écrit en trois temps (colonnes de CEP_EDITION et COURRIER_JUDICIAIRE lues par la même requête).
/// </summary>
public sealed class SondeMaquettesEdition : ISondeEcran
{
    private const string Maquette = "S44_MAQ", Parametre = "S44P_EDT";

    public string Code => "PROC_EDIT_MAQUETTE";

    public string? Jeu => JeuSonde.EnDeuxTemps($"""
        IF OBJECT_ID('v2.MAQUETTE') IS NULL
            CREATE TABLE v2.MAQUETTE (MQTT_CODE_MAQUETTE varchar(40) NOT NULL PRIMARY KEY, MQTT_NUM_DOCUMENT varchar(40) NULL, MQTT_LIBELLE varchar(32) NOT NULL);
        {ColonnesLot9.Ajouter("MAQUETTE", ("MQTT_LIBELLE", "varchar(32) NULL"), ("MQTT_EXTENSION", "varchar(10) NULL"), ("MQTT_TYPE_MAQUETTE", "varchar(30) NULL"),
            ("MQTT_ENTETE_FORMAT_PLIEUSE", "bit NOT NULL CONSTRAINT DF_P14_MQTT_PLIEUSE DEFAULT (0)"))}
        IF OBJECT_ID('v2.PARAMETRE_EDITION') IS NULL
            CREATE TABLE v2.PARAMETRE_EDITION (PRMED_CODE varchar(40) NOT NULL PRIMARY KEY, PRMED_LIBELLE varchar(100) NOT NULL);
        {ColonnesLot9.Ajouter("PARAMETRE_EDITION", ("PRMED_LISTE", "varchar(4000) NOT NULL CONSTRAINT DF_P14_PRMED_LISTE DEFAULT ('')"),
            ("PRMED_CODE_ENTITE_SOURCE", "varchar(16) NULL"), ("PRMED_CODE_PROCESSUS", "varchar(40) NULL"), ("PRMED_CODE_MAQUETTE", "varchar(40) NULL"),
            ("PRMED_IMPRIMANTE", "varchar(50) NULL"), ("PRMED_RECTO_VERSO", "bit NOT NULL CONSTRAINT DF_P14_PRMED_RV DEFAULT (0)"),
            ("PRMED_TYPE_GENERATION", "varchar(15) NULL"), ("PRMED_NOMBRE_EXEMPLAIRES", "int NOT NULL CONSTRAINT DF_P14_PRMED_NB DEFAULT (1)"),
            ("PRMED_IMPRESSION", "bit NOT NULL CONSTRAINT DF_P14_PRMED_IMP DEFAULT (0)"),
            ("PRMED_AFFICHER_AVANT_IMPRESSION", "bit NOT NULL CONSTRAINT DF_P14_PRMED_AFF DEFAULT (0)"),
            ("PRMED_READ_ONLY", "bit NOT NULL CONSTRAINT DF_P14_PRMED_RO DEFAULT (0)"), ("PRMED_AGRAFAGE", "bit NOT NULL CONSTRAINT DF_P14_PRMED_AGR DEFAULT (0)"),
            ("PRMED_TYPE_PAPIER", "varchar(30) NULL"), ("PRMED_LOT", "varchar(30) NULL"), ("PRMED_LOT_ILRAR", "varchar(30) NULL"), ("PRMED_LOT_LRAR", "varchar(30) NULL"),
            ("PRMED_GARDER_EDITION_LOT1", "bit NOT NULL CONSTRAINT DF_P14_PRMED_GL1 DEFAULT (1)"), ("PRMED_REQUETE_SQL", "varchar(max) NULL"),
            ("PRMED_NATURE_EDITION", "varchar(32) NULL"), ("PRMED_LOT_SUBSTITUTION", "varchar(30) NULL"))}
        IF OBJECT_ID('v2.CEP_EDITION') IS NULL
            CREATE TABLE v2.CEP_EDITION (CEPED_ID_CEPED int IDENTITY NOT NULL PRIMARY KEY, CEPED_CODE_EVE_PRO varchar(10) NOT NULL, CEPED_TYPE_EP varchar(1) NOT NULL,
                CEPED_CODE_EDITION varchar(20) NOT NULL);
        {ColonnesLot9.Ajouter("CEP_EDITION", ("CEPED_CODE_EVE_PRO", "varchar(10) NULL"), ("CEPED_CODE_EDITION", "varchar(20) NULL"))}
        IF OBJECT_ID('v2.CODE_EVE_PRO') IS NULL
            CREATE TABLE v2.CODE_EVE_PRO (R_CODEEVEPRO_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_CODEEVEPRO_LIBELLE_ECRAN varchar(35) NOT NULL);
        {ColonnesLot9.Ajouter("CODE_EVE_PRO", ("R_CODEEVEPRO_DEVELOPPE_EDIT", "varchar(1000) NULL"))}
        IF OBJECT_ID('v2.TYPE_DECISION') IS NULL
            CREATE TABLE v2.TYPE_DECISION (R_TYPEDECISI_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_TYPEDECISI_LIBELLE_ECRAN varchar(100) NOT NULL);
        {ColonnesLot9.Ajouter("TYPE_DECISION", ("R_TYPEDECISI_LIBELLE_ECRAN", "varchar(100) NULL"), ("R_TYPEDECISI_PARAMETRE_EDITION", "varchar(20) NULL"))}
        IF OBJECT_ID('v2.VIDIGED_PARAMETRAGE') IS NULL
            CREATE TABLE v2.VIDIGED_PARAMETRAGE (VGPRM_ID_VGPRM int NOT NULL PRIMARY KEY);
        {ColonnesLot9.Ajouter("VIDIGED_PARAMETRAGE", ("VGPRM_CODE", "varchar(50) NULL"), ("VGPRM_LIBELLE", "varchar(1000) NULL"),
            ("VGPRM_PARAMETRE_EDITION_CFEC", "varchar(20) NULL"))}
        IF OBJECT_ID('v2.ADAPTATEUR_ULT') IS NULL
            CREATE TABLE v2.ADAPTATEUR_ULT (ADPUL_ID_ADPUL int NOT NULL PRIMARY KEY);
        {ColonnesLot9.Ajouter("ADAPTATEUR_ULT", ("ADPUL_CODE", "varchar(10) NULL"), ("ADPUL_LIBELLE", "varchar(70) NULL"), ("ADPUL_VALEUR_DEFAUT", "varchar(max) NULL"))}
        IF OBJECT_ID('v2.COURRIER_JUDICIAIRE') IS NULL
            CREATE TABLE v2.COURRIER_JUDICIAIRE (COUJU_ID_COUJU int IDENTITY NOT NULL PRIMARY KEY, COUJU_CODE varchar(20) NOT NULL, COUJU_LIBELLE varchar(100) NOT NULL,
                COUJU_CODE_PRMED varchar(40) NOT NULL);
        {ColonnesLot9.Ajouter("COURRIER_JUDICIAIRE", ("COUJU_CODE", "varchar(20) NULL"), ("COUJU_LIBELLE", "varchar(100) NULL"),
            ("COUJU_CODE_PRMED", "varchar(40) NULL"))}
        """, $"""
        INSERT INTO v2.MAQUETTE (MQTT_CODE_MAQUETTE, MQTT_LIBELLE, MQTT_EXTENSION, MQTT_TYPE_MAQUETTE, MQTT_ENTETE_FORMAT_PLIEUSE)
            VALUES ('{Maquette}', 'Maquette de la sonde 44', 'docx', 'RIG', 0);
        INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_LISTE, PRMED_CODE_ENTITE_SOURCE, PRMED_CODE_PROCESSUS, PRMED_CODE_MAQUETTE,
            PRMED_TYPE_GENERATION, PRMED_NOMBRE_EXEMPLAIRES, PRMED_RECTO_VERSO, PRMED_AGRAFAGE, PRMED_IMPRESSION, PRMED_AFFICHER_AVANT_IMPRESSION,
            PRMED_READ_ONLY, PRMED_GARDER_EDITION_LOT1)
            VALUES ('{Parametre}', 'Parametre de la sonde 44', '<PARAMETRES CODE_PROCESSUS="EDIT_S44" NOMBRE_EXEMPLAIRES="1" RECTO_VERSO="0" />',
                'INSTANCE', 'EDIT_S44', '{Maquette}', 'FUSION_RIG', 1, 0, 0, 1, 0, 0, 0);
        """);

    /// <summary>Le paramètre de la sonde, lu par le pont dans sa maquette ; null s'il n'y est pas.</summary>
    private static ParametreEditionVue? Lu(VueMaquettes vue)
        => vue.Maquettes.FirstOrDefault(m => m.Code == Maquette)?.Parametres.FirstOrDefault(p => p.Code == Parametre);

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var avant = await EcranMaquettesEdition.MaquettesAsync(c.Client, c.Greffe);
        if (avant.Mode != "serveur") return new(false, false, $"les maquettes ne se lisent pas ({avant.Mode} « {avant.Message} »)");
        if (Lu(avant) is not { NbExemplaires: "1", RectoVerso: false } p0)
            return new(false, false, $"le paramètre {Parametre} du jeu n'est pas lu dans la maquette {Maquette} (1 exemplaire, recto seul) : « {Lu(avant)} »");

        var journal = await c.Journal();
        var refus = await EcranMaquettesEdition.ParametreAsync(c.Client, c.Greffe, new GesteEcran(TypeGeste.Enregistrer, [Parametre],
            new Dictionary<string, string> { [EcritureParametresEdition.ChampExemplaires] = "0" }));
        if (refus.Fait || refus.Message != "Le nombre d'exemplaires est obligatoire." || await c.Journal() != journal)
            return new(false, false, $"jumeau refusé attendu (« Nb exemplaires » à 0, DoVerifier de RIG v2), rendu : {(refus.Fait ? "fait" : "refusé")} « {refus.Message} »");

        var r = await EcranMaquettesEdition.ParametreAsync(c.Client, c.Greffe, new GesteEcran(TypeGeste.Enregistrer, [Parametre],
            new Dictionary<string, string> { [EcritureParametresEdition.ChampExemplaires] = "2", [EcritureParametresEdition.ChampRectoVerso] = "Oui" }));
        if (!r.Fait) return new(false, false, $"Valider le paramètre {Parametre} : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");
        if (await c.Journal() == journal) return new(true, false, $"Valider le paramètre {Parametre} rendu fait sans trace au journal des gestes");

        var apres = await EcranMaquettesEdition.MaquettesAsync(c.Client, c.Greffe);
        var p = Lu(apres);
        var liste = await c.Valeur($"SELECT PRMED_LISTE FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = '{Parametre}'");
        var xml = liste.Contains("NOMBRE_EXEMPLAIRES=\"2\"", StringComparison.Ordinal) && liste.Contains("RECTO_VERSO=\"1\"", StringComparison.Ordinal);
        return apres.Mode == "serveur" && p is { NbExemplaires: "2", RectoVerso: true } && p.Libelle == p0.Libelle && xml
            ? new(true, true, $"Valider le paramètre d'édition fait (« {r.Message} »), relu par le pont : {Parametre} de la maquette {Maquette} passe de " +
                              $"1 exemplaire recto seul à 2 exemplaires recto/verso, sa copie XML PRMED_LISTE aussi ; jumeau refusé (« Nb exemplaires » à 0)")
            : new(true, false, $"Valider le paramètre rendu fait, mais relu {apres.Mode} « {p} », PRMED_LISTE « {liste} »");
    }
}
