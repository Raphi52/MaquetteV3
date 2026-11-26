using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

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
/// CE QUI MANQUE pour OUVRIR le document a l'ecran (nomme, non fait par ce lot) : une adresse du serveur qui rende ses octets
/// (GDRG_FICHIER est une archive ZIP a decompresser, GedRig.GetFile, RigMetier\Divers\GedRig.cs:600-660 ; GDRG_FICHIER_PDF
/// est lisible tel quel), son relais par le pont (Pont/EcranGenere.cs) et son affichage par la visionneuse
/// (web/src/ecrans/formes/FormeDocument.tsx, qui ne montre aujourd'hui que le nom et les precisions de la ligne).
///
/// GROUPES : Voir un PDF -- « PDF archivé » ; Voir une image -- « TIFF », « Image » (png, jpeg, bmp, gif).
/// REGLE D'ETAT (les colonnes de GED_RIG) :
///   aucun octet (GDRG_FICHIER et GDRG_FICHIER_PDF vides) -> AFaire « Fichier absent » ;
///   extrait par un agent (GDRG_LOGIN_NT_EXTRACTION)      -> EnCours « Extrait » ;
///   autre                                                 -> Validee « Disponible ».
/// </summary>
internal abstract class LecteurPiecesGed : ILecteurEcran
{
    public abstract string Procedure { get; }

    /// <summary>Les extensions lues, en minuscules.</summary>
    protected abstract string[] Extensions { get; }

    /// <summary>Le groupe du descripteur d'un document, par son extension en minuscules.</summary>
    protected abstract string Groupe(string extension);

    private const string Requete = """
        SELECT TOP (@plafond) g.GDRG_NUM_DOCUMENT, g.GDRG_EXTENSION, g.GDRG_NOM_FICHIER, g.GDRG_DATE_ARCHIVAGE, g.GDRG_NB_PAGE,
               g.GDRG_LOGIN_NT_EXTRACTION, DATALENGTH(g.GDRG_FICHIER) AS Taille,
               CASE WHEN g.GDRG_FICHIER IS NULL AND g.GDRG_FICHIER_PDF IS NULL THEN 0 ELSE 1 END AS AUnFichier,
               COUNT(*) OVER () AS Total
        FROM v2.GED_RIG AS g
        WHERE LOWER(LTRIM(RTRIM(g.GDRG_EXTENSION))) IN (/*extensions*/)
        ORDER BY g.GDRG_DATE_ARCHIVAGE DESC, g.GDRG_NUM_DOCUMENT
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        for (var k = 0; k < Extensions.Length; k++) parametres.Add(new SqlParameter($"@e{k}", Extensions[k]));
        var sql = Requete.Replace("/*extensions*/", string.Join(", ", Extensions.Select((_, k) => $"@e{k}")), StringComparison.Ordinal);
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

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
}

/// <summary>« Voir une image » (PROC_IMAGE) : les images de la GED (LecteurPiecesGed).</summary>
internal sealed class LecteurImagesGed : LecteurPiecesGed
{
    public override string Procedure => "PROC_IMAGE";

    internal const string Tiff = "TIFF", Image = "Image";

    protected override string[] Extensions => ["tif", "tiff", "png", "jpg", "jpeg", "bmp", "gif"];

    protected override string Groupe(string extension) => extension is "tif" or "tiff" ? Tiff : Image;
}
