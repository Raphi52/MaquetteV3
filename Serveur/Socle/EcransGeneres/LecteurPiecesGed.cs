using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Voir un PDF » (PROC_PDF) et « Voir une image » (PROC_IMAGE) : les documents de la GED du greffe que chaque visionneuse sait
/// ouvrir. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026. Les documents : DocumentsGed.cs.
///
/// SOURCE (ancien RIG) : PROC_PDF et PROC_IMAGE sont des VISIONNEUSES : la V2 leur passe le chemin d'un fichier (parametre
/// « filename », proc_pdf.md:31-40 ; « Filename » et « Titre », proc_image.md:24-28) et elles ne lisent rien dans la base. Le
/// document reel d'un greffe est dans sa GED : GED_RIG porte le fichier lui-meme (GDRG_FICHIER, et GDRG_FICHIER_PDF sa version
/// PDF), pas un chemin. Ces lecteurs listent donc les documents de la GED au format de la visionneuse, par leur extension
/// (GDRG_EXTENSION) : « pdf » pour Voir un PDF ; « tif », « tiff », « png », « jpg », « jpeg », « bmp », « gif » pour Voir une
/// image (les formats d'image que reconnait la copie, Outils\Anonymisation\Documents.cs). Les plus recemment archives en tete.
///
/// OUVRIR UNE PIECE (IFicheEcran, plan « RIG v3 complet », lot 3, 28/11/2026) : la V2 ouvre UN fichier, celui qu'on lui passe
/// (OPE_PDF : « filename », proc_pdf.md:35-40 ; OPE_IMAGE.DoChargerDonnees : « Filename », PROC_IMAGE\OPE_IMAGE.cs:23-27). La V3
/// ouvre UN document de la GED, par son numero (Id = GDRG_NUM_DOCUMENT), et seulement s'il est du format de la visionneuse : sa
/// fiche porte ce que la visionneuse V2 en montre -- le fichier ouvert et la piece lue (Voir un PDF) ; le fichier, le titre de la
/// fenetre et les pages (Voir une image : titre par defaut « Aperçu du fichier '<fichier>' », OPE_IMAGE.cs:26 ; les pages ne
/// s'affichent que pour un document de plusieurs pages, OPE_IMAGE.cs:28). Non lus (nommes) : « Dossier » et « Lue » de Voir un
/// PDF -- le mode dossier lit un repertoire du poste et les pieces non lues sont tenues en memoire le temps de la fenetre
/// (OPE_PDF, FilesToBeRead), rien de cela n'est dans la base.
///
/// CE QUI MANQUE pour MONTRER les pages du document (nomme, non fait par ce lot) : une adresse du serveur qui rende ses octets
/// (GDRG_FICHIER est une archive ZIP a decompresser, GedRig.GetFile, RigMetier\Divers\GedRig.cs:600-660 ; GDRG_FICHIER_PDF
/// est lisible tel quel), son relais par le pont (Pont/EcranGenere.cs) et son affichage par la visionneuse
/// (web/src/ecrans/formes/FormeDocument.tsx, qui montre la fiche de la piece ouverte, pas ses pages).
///
/// GROUPES : Voir un PDF -- « PDF archivé » ; Voir une image -- « TIFF », « Image » (png, jpeg, bmp, gif).
/// REGLE D'ETAT (les colonnes de GED_RIG) :
///   aucun octet (GDRG_FICHIER et GDRG_FICHIER_PDF vides) -> AFaire « Fichier absent » ;
///   extrait par un agent (GDRG_LOGIN_NT_EXTRACTION)      -> EnCours « Extrait » ;
///   autre                                                 -> Validee « Disponible ».
/// </summary>
internal abstract class LecteurPiecesGed : IFicheEcran
{
    public abstract string Procedure { get; }

    /// <summary>Les extensions lues, en minuscules.</summary>
    protected abstract string[] Extensions { get; }

    /// <summary>Le groupe du descripteur d'un document, par son extension en minuscules.</summary>
    protected abstract string Groupe(string extension);

    /// <summary>Les colonnes d'un document : celles de sa ligne, et de sa fiche.</summary>
    private const string Colonnes = """
        g.GDRG_NUM_DOCUMENT, g.GDRG_EXTENSION, g.GDRG_NOM_FICHIER, g.GDRG_DATE_ARCHIVAGE, g.GDRG_NB_PAGE,
               g.GDRG_LOGIN_NT_EXTRACTION, DATALENGTH(g.GDRG_FICHIER) AS Taille,
               CASE WHEN g.GDRG_FICHIER IS NULL AND g.GDRG_FICHIER_PDF IS NULL THEN 0 ELSE 1 END AS AUnFichier
        """;

    private const string Requete = $"""
        SELECT TOP (@plafond) {Colonnes}, COUNT(*) OVER () AS Total
        FROM v2.GED_RIG AS g
        WHERE LOWER(LTRIM(RTRIM(g.GDRG_EXTENSION))) IN (/*extensions*/)
        ORDER BY g.GDRG_DATE_ARCHIVAGE DESC, g.GDRG_NUM_DOCUMENT
        """;

    // La fiche : le document de ce numero, s'il est du format de la visionneuse (un PDF ne s'ouvre pas dans Voir une image).
    private const string Fiche = $"""
        SELECT {Colonnes}
        FROM v2.GED_RIG AS g
        WHERE g.GDRG_NUM_DOCUMENT = @id AND LOWER(LTRIM(RTRIM(g.GDRG_EXTENSION))) IN (/*extensions*/)
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, AuxExtensions(Requete), Ligne, ct, [new("@plafond", plafond), .. ParametresExtensions()]);

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        await using var r = await LectureV2.LireAsync(baseGreffe, AuxExtensions(Fiche), ct, [new("@id", id), .. ParametresExtensions()]);
        if (!await r.ReadAsync(ct)) return null;
        var ligne = Ligne(r);
        return new FicheEcran(Procedure, id, ligne.Nom, Sections(ligne, CriteresConsultation.EntierDe(r, "GDRG_NB_PAGE")));
    }

    /// <summary>Les sections de la fiche, aux titres et libelles du descripteur (en tete du fichier).</summary>
    /// <param name="ligne">La ligne du document : son nom (le fichier) et ses precisions (la piece).</param>
    /// <param name="pages">GDRG_NB_PAGE ; null s'il n'est pas connu.</param>
    protected abstract SectionFiche[] Sections(LigneLue ligne, int? pages);

    private string AuxExtensions(string sql)
        => sql.Replace("/*extensions*/", string.Join(", ", Extensions.Select((_, k) => $"@e{k}")), StringComparison.Ordinal);

    private IEnumerable<SqlParameter> ParametresExtensions() => Extensions.Select((e, k) => new SqlParameter($"@e{k}", e));

    private LigneLue Ligne(SqlDataReader r)
    {
        var numero = FormeLigne.Texte(r, "GDRG_NUM_DOCUMENT");
        var extension = FormeLigne.Texte(r, "GDRG_EXTENSION").ToLowerInvariant();
        var login = FormeLigne.Texte(r, "GDRG_LOGIN_NT_EXTRACTION");
        var pages = CriteresConsultation.EntierDe(r, "GDRG_NB_PAGE");
        var fichier = Convert.ToInt32(r["AUnFichier"], System.Globalization.CultureInfo.InvariantCulture) == 1;
        var taille = r["Taille"] is DBNull ? (long?)null : Convert.ToInt64(r["Taille"], System.Globalization.CultureInfo.InvariantCulture);
        var precisions = FormeLigne.Joindre(
            $"Document {numero}",
            extension.ToUpperInvariant(),
            pages is { } p ? $"{p} page{(p > 1 ? "s" : "")}" : null,
            DocumentsGed.Taille(taille),
            $"archivé le {FormeLigne.Date(FormeLigne.DateDe(r, "GDRG_DATE_ARCHIVAGE"))}",
            !fichier ? "aucun fichier dans la GED" : login.Length > 0 ? $"extrait par {login}" : null);
        // La regle d'etat (en tete du fichier).
        var etat = !fichier ? "AFaire" : login.Length > 0 ? "EnCours" : "Validee";
        return new LigneLue(DocumentsGed.Nom("", FormeLigne.Texte(r, "GDRG_NOM_FICHIER"), numero), precisions, Groupe(extension), etat, [])
        {
            Id = numero,
        };
    }
}

/// <summary>« Voir un PDF » (PROC_PDF) : les PDF de la GED (LecteurPiecesGed).</summary>
internal sealed class LecteurPiecesPdf : LecteurPiecesGed
{
    public override string Procedure => "PROC_PDF";

    internal const string PdfArchive = "PDF archivé";

    protected override string[] Extensions => ["pdf"];

    protected override string Groupe(string extension) => PdfArchive;

    internal const string Ouverture = "Ouverture", Fichier = "Fichier", Lecture = "Lecture", Piece = "Pièce";

    // Le fichier ouvert (OPE_PDF.FileName) et la piece lue ; « Dossier » et « Lue » ne sont pas dans la base (en tete du fichier).
    protected override SectionFiche[] Sections(LigneLue ligne, int? pages) =>
    [
        new(Ouverture, [new ChampFiche(Fichier, ligne.Nom)]),
        new(Lecture, [new ChampFiche(Piece, ligne.Precisions)]),
    ];
}

/// <summary>« Voir une image » (PROC_IMAGE) : les images de la GED (LecteurPiecesGed).</summary>
internal sealed class LecteurImagesGed : LecteurPiecesGed
{
    public override string Procedure => "PROC_IMAGE";

    internal const string Tiff = "TIFF", Image = "Image";

    protected override string[] Extensions => ["tif", "tiff", "png", "jpg", "jpeg", "bmp", "gif"];

    protected override string Groupe(string extension) => extension is "tif" or "tiff" ? Tiff : Image;

    internal const string Ouverture = "Ouverture", Fichier = "Fichier", Titre = "Titre", Pages = "Pages";

    // Le fichier, le titre par defaut de la V2 (aucun titre n'est passe : OPE_IMAGE.cs:26) et, pour un document de plusieurs
    // pages seulement, ses pages (OPE_IMAGE.cs:28 : la navigation n'apparait qu'au-dela d'une page).
    protected override SectionFiche[] Sections(LigneLue ligne, int? pages) => pages is > 1
        ? [Fenetre(ligne), new(Pages, [new ChampFiche(Pages, pages.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))])]
        : [Fenetre(ligne)];

    private static SectionFiche Fenetre(LigneLue ligne)
        => new(Ouverture, [new ChampFiche(Fichier, ligne.Nom), new ChampFiche(Titre, $"Aperçu du fichier '{ligne.Nom}'")]);
}
