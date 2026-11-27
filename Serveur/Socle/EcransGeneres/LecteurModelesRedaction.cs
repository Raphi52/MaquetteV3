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
///   - « Modèles et maquettes » : OPE_RECH_MODEL_MAQ.GetListe charge TOUS les modeles de document (MODELE_DOCUMENT, :85) et toutes les
///     maquettes de type « REDA » (MAQUETTE.MQTT_TYPE_MAQUETTE, :84) ; la composition d'un modele (COMPOSITION_MODELE, ordonnee par
///     X_CMPMD_INDEX, :137-143) dit ses maquettes, et une maquette utilisee par un modele ne se supprime pas (OPE_EDT_MAQ.cs:153-154) ;
///   - « Sections et glossaire » : toutes les sections (GetAllSectionMaquette, OPE_RECH_SECTION.cs:92) ; le glossaire PUBLIC
///     (GLOSS_PROPRIETAIRE = '*', OPE_EDT_GLOSSAIRE.cs:121-123). La V2 y ajoute les entrees PRIVEES de l'utilisateur courant
///     (GetAllGlossaireOfUserAndPublic, OPE_RECH_GLOSSAIRE.cs:96) : un lecteur ne connait pas l'agent, elles ne sont pas lues ici ;
///   - « Jugements » : l'administration d'un jugement (ETP_XREDA) s'ouvre sur UNE decision recue en parametre, pas sur une liste : le
///     groupe reste vide.
/// Chaque groupe est une liste a lui (un onglet de la V2) : le plafond vaut par groupe (LignesLues.TotalParGroupe), un long catalogue de
/// modeles ne masque pas les sections. Tri : par nature puis par code (la V2 trie par la recherche plein texte, sans ordre fixe).
/// RECHERCHE : le champ cherche le code ou le libelle, les « _ » du code valant des blancs (OPE_RECH_MODEL_MAQ.cs:93, :171).
/// ETAT : un modele sans maquette, une maquette sans document Word -> AFaire « À compléter » ; sinon Validee « Enregistré ».
/// VALEURS : dans l'ordre des Colonnes (Code, Libellé, Nature, Utilisation).
/// AUCUN GESTE BRANCHE : creer, dupliquer ou supprimer un modele ou une maquette passe par son document Word en GED (OPE_EDT_MAQ.cs:103-147,
/// :164-192) ; les sections ne s'editent que par Amitel (OPE_EDT_SECTION.cs:64-71) ; le glossaire public/prive depend du login de
/// l'agent (OPE_EDT_GLOSSAIRE.cs:66-75). Reste a brancher, nomme au compte rendu du lot.
/// </summary>
internal sealed class LecteurModelesRedaction : IRechercheEcran
{
    public string Procedure => "PROC_REDAA";

    internal const string ModelesMaquettes = "Modèles et maquettes", SectionsGlossaire = "Sections et glossaire", Jugements = "Jugements";
    private static readonly string[] Groupes = [ModelesMaquettes, SectionsGlossaire, Jugements];

    public IReadOnlyCollection<string> Criteres { get; } = [];

    // Nature : 1 modele, 2 maquette, 3 section, 4 glossaire ; Bloc : 1 modeles et maquettes, 2 sections et glossaire.
    private const string Requete = """
        SELECT o.* FROM (
            SELECT u.*, ROW_NUMBER() OVER (PARTITION BY u.Bloc ORDER BY u.Nature, u.Code) AS Rang, COUNT(*) OVER (PARTITION BY u.Bloc) AS TotalBloc
            FROM (
                SELECT 1 AS Bloc, 1 AS Nature, m.MDDCM_CODE_MODELE_DOCUMENT AS Code, m.MDDCM_LIBELLE AS Libelle,
                       (SELECT COUNT(*) FROM v2.COMPOSITION_MODELE AS c WHERE c.X_CMPMD_CODE_MODELE_DOCUMENT = m.MDDCM_CODE_MODELE_DOCUMENT) AS Liens,
                       CAST(1 AS bit) AS AvecDocument
                FROM v2.MODELE_DOCUMENT AS m
                UNION ALL
                SELECT 1, 2, q.MQTT_CODE_MAQUETTE, q.MQTT_LIBELLE,
                       (SELECT COUNT(*) FROM v2.COMPOSITION_MODELE AS c WHERE c.X_CMPMD_CODE_MAQUETTE = q.MQTT_CODE_MAQUETTE),
                       CAST(CASE WHEN q.MQTT_NUM_DOCUMENT IS NULL OR q.MQTT_NUM_DOCUMENT = '' THEN 0 ELSE 1 END AS bit)
                FROM v2.MAQUETTE AS q WHERE q.MQTT_TYPE_MAQUETTE = 'REDA'
                UNION ALL
                SELECT 2, 3, s.SCTMQ_CODE_SECTION_MAQUETTE, s.SCTMQ_LIBELLE, 0, CAST(1 AS bit) FROM v2.SECTION_MAQUETTE AS s
                UNION ALL
                SELECT 2, 4, g.GLOSS_CODE_GLOSSAIRE, g.GLOSS_LIBELLE, 0, CAST(1 AS bit) FROM v2.GLOSSAIRE AS g WHERE g.GLOSS_PROPRIETAIRE = '*'
            ) AS u
            WHERE 1 = 1 /*filtres*/
        ) AS o
        WHERE o.Rang <= @plafond
        ORDER BY o.Bloc, o.Rang
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
            nature <= 2 ? ModelesMaquettes : SectionsGlossaire, aFaire ? "AFaire" : "Validee", [])
        {
            Valeurs = [code, libelle, natureMot, utilisation],
        };
    }
}
