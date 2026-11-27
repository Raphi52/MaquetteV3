using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Modèles de rédaction » (PROC_REDAA) : l'administration de la redaction automatique des decisions (REDA) -- modeles de document,
/// maquettes REDA, sections et glossaire public. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026.
///
/// SOURCE (ancien RIG) : PROC_REDAA (D:\RigApplication\Source\RIG\DLL\Processus\PROC_REDAA ; Detail OPE de proc_redaa.md, qui fait foi).
///   - « Modèles de document » et « Maquettes REDA » : OPE_RECH_MODEL_MAQ.GetListe charge TOUS les modeles de document (MODELE_DOCUMENT,
///     :85) et toutes les maquettes de type « REDA » (MAQUETTE.MQTT_TYPE_MAQUETTE, :84 ; Maquette.GetMaquettesFromType, Maquette.cs:60) ;
///     le choix « modèle » / « maquette » de sa recherche (GrbChoixRech, :36) en fait deux listes, donc deux groupes, chacun avec sa fiche
///     (ETP_EDT_MODEL, ETP_EDT_MAQ). La composition d'un modele (COMPOSITION_MODELE, ordonnee par X_CMPMD_INDEX, :137-143) dit ses
///     maquettes ; une maquette utilisee par un modele ne se supprime pas (OPE_EDT_MAQ.cs:153-154) ;
///   - « Sections » : toutes les sections (GetAllSectionMaquette, OPE_RECH_SECTION.cs:92) ; « Glossaire » : le glossaire PUBLIC
///     (GLOSS_PROPRIETAIRE = '*', OPE_EDT_GLOSSAIRE.cs:121-123). La V2 y ajoute les entrees PRIVEES de l'utilisateur courant
///     (GetAllGlossaireOfUserAndPublic, OPE_RECH_GLOSSAIRE.cs:96) : un lecteur ne connait pas l'agent, elles ne sont pas lues ici
///     (l'ecrivain, qui le connait, les ajoute, modifie et supprime). Sections et glossaire sont deux onglets de la V2 (ETP_SECTION,
///     ETP_GLOSSAIRE) : deux groupes ;
///   - « Jugements » : l'administration d'un jugement (ETP_XREDA) s'ouvre sur UNE decision recue en parametre, pas sur une liste : le
///     groupe reste vide.
/// Chaque groupe est une liste a lui (un onglet de la V2) : le plafond vaut par groupe (LignesLues.TotalParGroupe), un long catalogue de
/// modeles ne masque pas les sections. Tri : par code (la V2 trie par la recherche plein texte, sans ordre fixe).
/// RECHERCHE : le champ cherche le code ou le libelle, les « _ » du code valant des blancs (OPE_RECH_MODEL_MAQ.cs:93, :171).
/// ETAT : un modele sans maquette, une maquette sans document Word -> AFaire « À compléter » ; sinon Validee « Enregistré ».
/// VALEURS : dans l'ordre des Colonnes (Code, Libellé, Nature, Utilisation). ID : « <nature>:<code> » (modele, maquette, section,
/// glossaire). FICHE : la section de sa nature, aux champs du formulaire de la V2, modifiables quand EcrivainModelesRedaction.cs les
/// enregistre -- un modele (OPE_EDT_MODEL.cs:98-111 : libelle, type, actif, maquettes dans l'ordre), une section (OPE_EDT_SECTION.cs:107-115 :
/// description, requete SQL, quatre niveaux ; l'ecrivain la reserve a l'equipe Amitel, l. 65-71), une entree du glossaire (texte associe,
/// OPE_EDT_GLOSSAIRE.cs:125), une maquette REDA (OPE_EDT_MAQ.cs:81-82 : le libelle, le code fige).
/// </summary>
internal sealed class LecteurModelesRedaction : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_REDAA";

    internal const string Modeles = "Modèles de document", Maquettes = "Maquettes REDA", Sections = "Sections", Glossaire = "Glossaire",
        Jugements = "Jugements";
    private static readonly string[] Groupes = [Modeles, Maquettes, Sections, Glossaire, Jugements];

    /// <summary>Les sections de la fiche (celles du descripteur, Pont/EcransGeneres/Configurer.cs) et leurs champs hors colonnes.</summary>
    internal const string SectionModele = "Modèle de document", SectionMaquette = "Maquette REDA", SectionSection = "Section",
        SectionGlossaire = "Entrée du glossaire";
    internal const string ChampCode = "Code", ChampLibelle = "Libellé", ChampUtilisation = "Utilisation";
    internal const string ChampType = "Type de modèle", ChampActif = "Actif", ChampMaquettes = "Maquettes, dans l'ordre";
    internal const string ChampRequete = "Requête SQL", ChampOccurrence = "Occurrence", ChampFusion = "Fusion", ChampGeneration = "Génération",
        ChampPersistance = "Persistance";
    internal const string ChampTexte = "Texte associé", ChampPrive = "Privé";

    /// <summary>Les quatre niveaux d'une section : la valeur en base et le texte des listes de la V2 (PROC_REDAA\Controls\ComboBox\
    /// ULT_COMBO_OCCURENCE.cs:12, ULT_COMBO_FUSION.cs:12, ULT_COMBO_GENERATION.cs:12, ULT_COMBO_PERSISTANCE.cs:12), au mot pres.</summary>
    internal static readonly (string Champ, string Colonne, (int Valeur, string Texte)[] Options)[] Niveaux =
    [
        (ChampOccurrence, "SCTMQ_NIVEAU_OCCURRENCE", [(1, "occurence unique"), (2, "occurence multiples")]),
        (ChampFusion, "SCTMQ_NIVEAU_FUSION", [(1, "fusion à la génération"), (2, "fusion à l'édition")]),
        (ChampGeneration, "SCTMQ_NIVEAU_GENERATION", [(0, "non régénérable"), (1, "régénérable")]),
        (ChampPersistance, "SCTMQ_NIVEAU_PERSISTANCE", [(0, "non persistante"), (1, "persistante")]),
    ];

    /// <summary>Le prefixe de l'Id d'une ligne, par nature (1 modele, 2 maquette, 3 section, 4 glossaire).</summary>
    internal static readonly string[] Prefixes = ["modele:", "maquette:", "section:", "glossaire:"];

    public IReadOnlyCollection<string> Criteres { get; } = [];

    // Nature (= Bloc, un groupe chacun) : 1 modele, 2 maquette, 3 section, 4 glossaire.
    private const string Requete = """
        SELECT o.* FROM (
            SELECT u.*, ROW_NUMBER() OVER (PARTITION BY u.Nature ORDER BY u.Code) AS Rang, COUNT(*) OVER (PARTITION BY u.Nature) AS TotalBloc
            FROM (
                SELECT 1 AS Nature, m.MDDCM_CODE_MODELE_DOCUMENT AS Code, m.MDDCM_LIBELLE AS Libelle,
                       (SELECT COUNT(*) FROM v2.COMPOSITION_MODELE AS c WHERE c.X_CMPMD_CODE_MODELE_DOCUMENT = m.MDDCM_CODE_MODELE_DOCUMENT) AS Liens,
                       CAST(1 AS bit) AS AvecDocument
                FROM v2.MODELE_DOCUMENT AS m
                UNION ALL
                SELECT 2, q.MQTT_CODE_MAQUETTE, q.MQTT_LIBELLE,
                       (SELECT COUNT(*) FROM v2.COMPOSITION_MODELE AS c WHERE c.X_CMPMD_CODE_MAQUETTE = q.MQTT_CODE_MAQUETTE),
                       CAST(CASE WHEN q.MQTT_NUM_DOCUMENT IS NULL OR q.MQTT_NUM_DOCUMENT = '' THEN 0 ELSE 1 END AS bit)
                FROM v2.MAQUETTE AS q WHERE q.MQTT_TYPE_MAQUETTE = 'REDA'
                UNION ALL
                SELECT 3, s.SCTMQ_CODE_SECTION_MAQUETTE, s.SCTMQ_LIBELLE, 0, CAST(1 AS bit) FROM v2.SECTION_MAQUETTE AS s
                UNION ALL
                SELECT 4, g.GLOSS_CODE_GLOSSAIRE, g.GLOSS_LIBELLE, 0, CAST(1 AS bit) FROM v2.GLOSSAIRE AS g WHERE g.GLOSS_PROPRIETAIRE = '*'
            ) AS u
            WHERE 1 = 1 /*filtres*/
        ) AS o
        WHERE o.Rang <= @plafond
        ORDER BY o.Nature, o.Rang
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("(REPLACE(u.Code, '_', ' ') LIKE @texte OR u.Libelle LIKE @texte)", ("@texte", FormeLigne.Contient(recherche.Texte.Replace('_', ' '))));
        return Lire(baseGreffe, f, plafond, ct);
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        var nature = Array.FindIndex(Prefixes, p => id.StartsWith(p, StringComparison.Ordinal)) + 1;
        if (nature == 0) return null;
        var code = id[Prefixes[nature - 1].Length..];
        var f = new FiltresRecherche();
        f.Ajouter("u.Nature = @nature AND u.Code = @code", ("@nature", nature), ("@code", code));
        if ((await Lire(baseGreffe, f, 1, ct)).Lignes.FirstOrDefault() is not { Valeurs: [var c, var libelle, _, var utilisation] } ligne) return null;
        return nature switch
        {
            1 => await FicheModeleAsync(baseGreffe, id, ligne.Nom, c, libelle, utilisation, ct),
            // Le formulaire d'une maquette (OPE_EDT_MAQ.cs:81-82) : le code fige, le libelle modifiable.
            // fix-ok: le libelle se lisait seulement, sur la cause fausse « document Word en GED » ; Valider l'ecrit en base
            // (OPE_EDT_MAQ.cs:91-96, relu le 27/11/2026, objection du juge).
            2 => new FicheEcran(Procedure, id, ligne.Nom, [new SectionFiche(SectionMaquette,
                [new ChampFiche(ChampCode, c), new ChampFiche(ChampLibelle, libelle) { Modifiable = true }, new ChampFiche(ChampUtilisation, utilisation)])]),
            3 => await FicheSectionAsync(baseGreffe, id, ligne.Nom, c, ct),
            _ => await FicheGlossaireAsync(baseGreffe, id, ligne.Nom, c, libelle, ct),
        };
    }

    /// <summary>Le formulaire d'un modele (OPE_EDT_MODEL.cs:98-111) : le code fige (l. 98), le reste modifiable ; ses maquettes dans
    /// l'ordre de la composition (l. 108, X_CMPMD_INDEX), une par ligne.</summary>
    private async Task<FicheEcran?> FicheModeleAsync(SqlConnection cn, string id, string nom, string code, string libelle, string utilisation, CancellationToken ct)
    {
        string type = "";
        bool actif = false;
        await using (var r = await LectureV2.LireAsync(cn,
                         "SELECT MDDCM_TYPE_MODELE_DOCUMENT, MDDCM_ACTIF FROM v2.MODELE_DOCUMENT WHERE MDDCM_CODE_MODELE_DOCUMENT = @code", ct,
                         new SqlParameter("@code", code)))
        {
            if (!await r.ReadAsync(ct)) return null;
            type = FormeLigne.Texte(r, "MDDCM_TYPE_MODELE_DOCUMENT");
            actif = !r.IsDBNull(1) && Convert.ToBoolean(r.GetValue(1), CultureInfo.InvariantCulture);
        }
        var maquettes = new List<string>();
        await using (var r = await LectureV2.LireAsync(cn, """
                         SELECT X_CMPMD_CODE_MAQUETTE FROM v2.COMPOSITION_MODELE WHERE X_CMPMD_CODE_MODELE_DOCUMENT = @code
                         ORDER BY X_CMPMD_INDEX, X_CMPMD_ID_CMPMD
                         """, ct, new SqlParameter("@code", code)))
            while (await r.ReadAsync(ct)) maquettes.Add(FormeLigne.Texte(r, "X_CMPMD_CODE_MAQUETTE"));
        return new FicheEcran(Procedure, id, nom, [new SectionFiche(SectionModele,
        [
            new ChampFiche(ChampCode, code), new ChampFiche(ChampLibelle, libelle) { Modifiable = true },
            new ChampFiche(ChampType, type) { Modifiable = true }, new ChampFiche(ChampActif, FormeLigne.Case(actif)) { Modifiable = true },
            new ChampFiche(ChampMaquettes, string.Join("\n", maquettes)) { Modifiable = true }, new ChampFiche(ChampUtilisation, utilisation),
        ])]);
    }

    /// <summary>Le formulaire d'une section (OPE_EDT_SECTION.cs:107-115) : le code fige (l. 108), la description, la requete et les
    /// quatre niveaux, au texte de leur liste.</summary>
    private async Task<FicheEcran?> FicheSectionAsync(SqlConnection cn, string id, string nom, string code, CancellationToken ct)
    {
        await using var r = await LectureV2.LireAsync(cn, """
            SELECT SCTMQ_LIBELLE, SCTMQ_REQUETE_SQL, SCTMQ_NIVEAU_OCCURRENCE, SCTMQ_NIVEAU_FUSION, SCTMQ_NIVEAU_GENERATION, SCTMQ_NIVEAU_PERSISTANCE
            FROM v2.SECTION_MAQUETTE WHERE SCTMQ_CODE_SECTION_MAQUETTE = @code
            """, ct, new SqlParameter("@code", code));
        if (!await r.ReadAsync(ct)) return null;
        var champs = new List<ChampFiche>
        {
            new(ChampCode, code), new(ChampLibelle, FormeLigne.Texte(r, "SCTMQ_LIBELLE")) { Modifiable = true },
            new(ChampRequete, FormeLigne.Texte(r, "SCTMQ_REQUETE_SQL")) { Modifiable = true },
        };
        foreach (var (champ, colonne, options) in Niveaux)
        {
            var valeur = r.IsDBNull(r.GetOrdinal(colonne)) ? (int?)null : Convert.ToInt32(r[colonne], CultureInfo.InvariantCulture);
            champs.Add(new ChampFiche(champ, options.FirstOrDefault(o => o.Valeur == valeur).Texte ?? "") { Modifiable = true });
        }
        return new FicheEcran(Procedure, id, nom, [new SectionFiche(SectionSection, [.. champs])]);
    }

    /// <summary>Une entree du glossaire (OPE_EDT_GLOSSAIRE.cs:117-125) : le code fige, la description et le texte associe modifiables ;
    /// la liste ne lit que le public ('*'), qui ne redevient pas prive (l. 122).</summary>
    private async Task<FicheEcran> FicheGlossaireAsync(SqlConnection cn, string id, string nom, string code, string libelle, CancellationToken ct)
    {
        var texte = "";
        await using (var r = await LectureV2.LireAsync(cn,
                         "SELECT GLOSS_TEXTE FROM v2.GLOSSAIRE WHERE GLOSS_CODE_GLOSSAIRE = @code AND GLOSS_PROPRIETAIRE = '*'", ct, new SqlParameter("@code", code)))
            if (await r.ReadAsync(ct)) texte = FormeLigne.Texte(r, "GLOSS_TEXTE");
        return new FicheEcran(Procedure, id, nom,
            [new SectionFiche(SectionGlossaire, [new ChampFiche(ChampCode, code), new ChampFiche(ChampLibelle, libelle) { Modifiable = true },
                new ChampFiche(ChampTexte, texte) { Modifiable = true }, new ChampFiche(ChampPrive, FormeLigne.Case(false))])]);
    }

    private static async Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, int plafond, CancellationToken ct)
    {
        var lignes = new List<LigneLue>();
        var totaux = Groupes.ToDictionary(g => g, _ => 0, StringComparer.Ordinal);
        await using var r = await LectureV2.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));
        while (await r.ReadAsync(ct))
        {
            var ligne = Ligne(r);
            totaux[ligne.Groupe] = Convert.ToInt32(r["TotalBloc"], CultureInfo.InvariantCulture);
            lignes.Add(ligne);
        }
        return new LignesLues(lignes, totaux.Values.Sum()) { TotalParGroupe = totaux };
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var nature = Convert.ToInt32(r["Nature"], CultureInfo.InvariantCulture);
        var code = FormeLigne.Texte(r, "Code");
        var libelle = FormeLigne.Texte(r, "Libelle");
        var liens = Convert.ToInt32(r["Liens"], CultureInfo.InvariantCulture);
        var avecDocument = Convert.ToBoolean(r["AvecDocument"], CultureInfo.InvariantCulture);
        var (natureMot, utilisation, aFaire) = nature switch
        {
            1 => ("modèle", liens == 0 ? "aucune maquette" : $"{liens} maquette(s)", liens == 0),
            2 => ("maquette REDA", !avecDocument ? "sans document Word" : liens == 0 ? "utilisée par aucun modèle" : $"utilisée par {liens} modèle(s)", !avecDocument),
            3 => ("section", "", false),
            _ => ("glossaire public", "", false),
        };
        return new LigneLue(libelle.Length > 0 ? $"{code} - {libelle}" : code, FormeLigne.Joindre(natureMot, utilisation),
            Groupes[nature - 1], aFaire ? "AFaire" : "Validee", [])
        {
            Id = Prefixes[nature - 1] + code,
            Valeurs = [code, libelle, natureMot, utilisation],
        };
    }
}
