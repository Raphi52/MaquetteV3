using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Acteurs » (PROC_ACTEUR_JUD, Pont/EcranActeursBase.cs) : « Enregistrer les coordonnées » d'un juge, le « Valider » de
/// OPE_EDITION_GJUGE de RIG v2 (écriture : Serveur/Socle/EcransMain/EcritureActeurs.cs), joué par le pont puis relu par le pont : la ligne
/// du juge porte le téléphone et les deux courriels saisis (le séparateur « ; » de ULT_TEXT_MULTIMAIL), et le juge repasse « à diffuser »
/// à Experian (JUG_TOP_EXPERIAN = 0, OPE_EDITION_GJUGE.cs:135). Lot 9 du plan « RIG v3 complet, mesuré par le banc du parcours complet »
/// (28/11/2026).
///
/// LES JUMEAUX, refusés sans écriture (journal des gestes inchangé) : un juge sans abrégé ni prénom (OPE_EDITION_GJUGE.DoVerifier, :90-92 :
/// RIG v2 refuse de valider sa fiche) ; un courriel hors du format de RIG v2 (RegEx.Email).
///
/// LE JEU : les colonnes que LectureActeurs.cs lit pour les six types, aux types de RIG_DEV (copie de développement, INFORMATION_SCHEMA du
/// 28/11/2026), ajoutées aux tables présentes -- JUG_ID_ADRBS manquait à la base du banc (500 au 28/11) ; deux juges. Identifiants 39004xx.
/// </summary>
public sealed class SondeActeurs : ISondeEcran
{
    private const string Juge = "Juge-3900401", JugeSansNom = "Juge-3900402";

    public string Code => "PROC_ACTEUR_JUD";

    public string? Jeu => $"""
        IF OBJECT_ID('v2.JUGE') IS NULL
            CREATE TABLE v2.JUGE (JUG_ID_JUG int NOT NULL PRIMARY KEY, JUG_CIVILITE varchar(7) NOT NULL, JUG_NOM varchar(35) NOT NULL);
        {ColonnesLot9.Ajouter("JUGE", ("JUG_ABREGE", "varchar(16) NULL"), ("JUG_PRENOM", "varchar(20) NULL"), ("JUG_STATUT", "varchar(3) NULL"),
            ("JUG_CHAMBRE", "varchar(5) NULL"), ("JUG_SECTION", "varchar(10) NULL"), ("JUG_ADRESSE_MAIL", "varchar(70) NULL"),
            ("JUG_ACTIF", "bit NOT NULL CONSTRAINT DF_P9_JUG_ACTIF DEFAULT (1)"), ("JUG_ID_ADRBS", "int NULL"), ("JUG_TELEPHONE", "varchar(15) NULL"),
            ("JUG_FAX", "varchar(15) NULL"), ("JUG_DATE_DEBUT", "datetime NULL"), ("JUG_DATE_FIN", "datetime NULL"), ("JUG_BUREAU", "varchar(100) NULL"),
            ("JUG_TOP_EXPERIAN", "bit NOT NULL CONSTRAINT DF_P9_JUG_TOP DEFAULT (0)"))}
        {ColonnesLot9.Ajouter("MANDATAIRE_PC", ("MNDPC_ID_ADRBS", "int NULL"), ("MNDPC_CIVILITE", "varchar(7) NOT NULL CONSTRAINT DF_P9_MND_CIV DEFAULT ('')"),
            ("MNDPC_NOM", "varchar(100) NOT NULL CONSTRAINT DF_P9_MND_NOM DEFAULT ('')"), ("MNDPC_COMPLEMENT_NOM", "varchar(100) NULL"),
            ("MNDPC_ABREGE", "varchar(16) NOT NULL CONSTRAINT DF_P9_MND_ABR DEFAULT ('')"), ("MNDPC_QUALIF_PROFESSION", "varchar(5) NULL"),
            ("MNDPC_ACTIF", "bit NOT NULL CONSTRAINT DF_P9_MND_ACTIF DEFAULT (1)"), ("MNDPC_ADRESSE_MAIL", "varchar(1000) NULL"),
            ("MNDPC_TELEPHONE", "varchar(15) NULL"), ("MNDPC_FAX", "varchar(15) NULL"), ("MNDPC_NOM_COFFRE", "varchar(100) NULL"), ("MNDPC_SIREN", "varchar(9) NULL"))}
        {ColonnesLot9.Ajouter("PARQUETIER", ("PRQTR_ABREGE", "varchar(16) NOT NULL CONSTRAINT DF_P9_PRQ_ABR DEFAULT ('')"),
            ("PRQTR_CIVILITE", "varchar(7) NOT NULL CONSTRAINT DF_P9_PRQ_CIV DEFAULT ('')"), ("PRQTR_NOM", "varchar(35) NOT NULL CONSTRAINT DF_P9_PRQ_NOM DEFAULT ('')"),
            ("PRQTR_PRENOM", "varchar(20) NULL"), ("PRQTR_STATUT", "varchar(3) NULL"), ("PRQTR_ADRESSE_MAIL", "varchar(70) NULL"),
            ("PRQTR_ACTIF", "bit NOT NULL CONSTRAINT DF_P9_PRQ_ACTIF DEFAULT (1)"), ("PRQTR_ID_ADRBS", "int NULL"), ("PRQTR_TELEPHONE", "varchar(15) NULL"),
            ("PRQTR_FAX", "varchar(15) NULL"), ("PRQTR_TOP_EXPERIAN", "bit NOT NULL CONSTRAINT DF_P9_PRQ_TOP DEFAULT (0)"))}
        {ColonnesLot9.Ajouter("GREFFIER_ASSERMENTE", ("GRASS_ABREGE", "varchar(16) NOT NULL CONSTRAINT DF_P9_GRA_ABR DEFAULT ('')"),
            ("GRASS_CIVILITE", "varchar(7) NOT NULL CONSTRAINT DF_P9_GRA_CIV DEFAULT ('')"), ("GRASS_NOM", "varchar(35) NOT NULL CONSTRAINT DF_P9_GRA_NOM DEFAULT ('')"),
            ("GRASS_PRENOM", "varchar(20) NULL"), ("GRASS_STATUT", "varchar(3) NULL"), ("GRASS_ADRESSE_MAIL", "varchar(70) NULL"),
            ("GRASS_TELEPHONE", "varchar(15) NULL"), ("GRASS_FAX", "varchar(15) NULL"), ("GRASS_ACTIF", "bit NOT NULL CONSTRAINT DF_P9_GRA_ACTIF DEFAULT (1)"),
            ("GRASS_ID_UTLST", "int NULL"), ("GRASS_TOP_EXPERIAN", "bit NOT NULL CONSTRAINT DF_P9_GRA_TOP DEFAULT (0)"))}
        {ColonnesLot9.Ajouter("INTERVENANT_PC_REPER", ("INTRPR_ABREGE", "varchar(16) NULL"),
            ("INTRPR_CIVILITE", "varchar(7) NOT NULL CONSTRAINT DF_P9_INT_CIV DEFAULT ('')"), ("INTRPR_NOM", "varchar(35) NOT NULL CONSTRAINT DF_P9_INT_NOM DEFAULT ('')"),
            ("INTRPR_COMPLEMENT_NOM", "varchar(35) NULL"), ("INTRPR_QUALIF_PROFESSION", "varchar(5) NOT NULL CONSTRAINT DF_P9_INT_QUAL DEFAULT ('')"),
            ("INTRPR_ID_ADRBS", "int NULL"), ("INTRPR_ADRESSE_MAIL", "varchar(70) NULL"), ("INTRPR_TELEPHONE", "varchar(15) NULL"), ("INTRPR_FAX", "varchar(15) NULL"),
            ("INTRPR_NOM_COFFRE", "varchar(100) NULL"), ("INTRPR_DATE_DEMARRAGE_COFFRE", "datetime NULL"))}
        {ColonnesLot9.Ajouter("EXPERT", ("EXPRT_ID_ADRBS", "int NULL"), ("EXPRT_CIVILITE", "varchar(7) NULL"), ("EXPRT_NOM", "varchar(70) NULL"),
            ("EXPRT_COMPLEMENT_NOM", "varchar(70) NULL"), ("EXPRT_QUALIF_PROFESSION", "varchar(5) NULL"), ("EXPRT_ADRESSE_MAIL", "varchar(70) NULL"),
            ("EXPRT_TELEPHONE", "varchar(15) NULL"), ("EXPRT_FAX", "varchar(15) NULL"), ("EXPRT_EST_ACTIF", "bit NOT NULL CONSTRAINT DF_P9_EXA_ACTIF DEFAULT (1)"))}
        EXEC sp_executesql N'
        INSERT INTO v2.JUGE (JUG_ID_JUG, JUG_ABREGE, JUG_CIVILITE, JUG_NOM, JUG_PRENOM, JUG_ACTIF, JUG_TOP_EXPERIAN)
            VALUES (3900401, ''S39JUG'', ''M.'', ''JUGE SONDE'', ''Paul'', 1, 1), (3900402, NULL, ''Mme'', ''JUGE SANS PRENOM'', NULL, 1, 1);';
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        // Jumeaux refusés, rien d'écrit.
        var journal = await c.Journal();
        var sansNom = await EcranActeurs.EnregistrerCoordonneesAsync(c.Client, c.Greffe, JugeSansNom, "0123456789", "", "");
        var mailFaux = await EcranActeurs.EnregistrerCoordonneesAsync(c.Client, c.Greffe, Juge, "0123456789", "", "pas-un-courriel");
        if (sansNom.Fait || !sansNom.Message.StartsWith("Saisir le prénom du juge", StringComparison.Ordinal)
            || mailFaux.Fait || mailFaux.Message != "« Mail » : Courriel (pas-un-courriel) invalide." || await c.Journal() != journal)
            return new(false, false, $"Enregistrer les coordonnées, jumeaux : juge sans abrégé ni prénom {(sansNom.Fait ? "fait" : "refusé")} « {sansNom.Message} », "
                                     + $"courriel faux {(mailFaux.Fait ? "fait" : "refusé")} « {mailFaux.Message} »");

        var r = await EcranActeurs.EnregistrerCoordonneesAsync(c.Client, c.Greffe, Juge, "01.23.45.67.89", "", "juge.sonde@exemple.fr, greffe@exemple.fr");
        if (!r.Fait) return new(false, false, $"Enregistrer les coordonnées du juge : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");

        var vue = await EcranActeurs.ActeursAsync(c.Client, c.Greffe);
        var lu = vue.Acteurs.FirstOrDefault(a => a.Cle == Juge);
        var top = await c.Valeur("SELECT CAST(JUG_TOP_EXPERIAN AS int) FROM v2.JUGE WHERE JUG_ID_JUG = 3900401");
        return vue.Mode == "serveur" && lu is { Tel: "01.23.45.67.89", Fax: "", Mail: "juge.sonde@exemple.fr;greffe@exemple.fr", Nom: "JUGE SONDE" } && top == "0"
            ? new(true, true, $"Enregistrer les coordonnées fait (« {r.Message} »), relu par le pont : {lu.Nom} {lu.Tel}, {lu.Mail} ; à rediffuser à "
                              + "Experian ; jumeaux refusés sans écriture (juge sans abrégé ni prénom, courriel faux)")
            : new(true, false, $"Enregistrer les coordonnées rendu fait, mais relu : {vue.Mode}, « {lu?.Tel} / {lu?.Mail} », Experian {top}");
    }
}

/// <summary>Les colonnes qu'une sonde du lot 9 ajoute à une table v2.* de la base du banc, si la table existe et que la colonne manque.</summary>
internal static class ColonnesLot9
{
    public static string Ajouter(string table, params (string Colonne, string Type)[] colonnes)
        => string.Join("\n", colonnes.Select(c =>
            $"IF OBJECT_ID('v2.{table}') IS NOT NULL AND COL_LENGTH('v2.{table}', '{c.Colonne}') IS NULL ALTER TABLE v2.{table} ADD {c.Colonne} {c.Type};"));
}
