using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Documents océrisés » (PROC_NUM, forme Document) : les documents numerises ou importes du greffe, en PDF ou en TIF. Mode d'emploi
/// des lecteurs : LecteurEcran.cs (plan « RIG v3 100 % fonctionnel », lot 11, 27/11/2026).
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_NUM (proc_num.md, Detail OPE, qui fait foi). La liste est celle d'OPE_PIECENUM
/// (ETP_PIECENUM_\OPE_PIECENUM.cs:85-107) : les DocumentOcerise du greffe -- de l'utilisateur, ou tous -- SAUF ceux lies a l'OCR differe
/// (DOCOC_IS_LIE_OCR_DIFFERE = 0, :93-96), du plus recent au plus ancien (DOCOC_DATE_CREATION). Le fichier lui-meme (DOCOC_FICHIER) n'est
/// pas lu : la liste n'en montre que le nom, les pages et la taille. Dans la base V3, DOCUMENT_OCERISE est la table V2 telle quelle.
/// GROUPES : le format de l'edition (_Editer, PROC_NUM_PROC.cs:160-215) -- « TIF » pour un fichier .tif / .tiff, « PDF » sinon (tout
/// import est converti en PDF, OPE_SCANNER.cs:691-693).
/// REGLE D'ETAT : un document qu'un processus tient en cours (DOCOC_ID_UTLST_EN_COURS et DOCOC_CODE_PROC_EN_COURS, la reservation de
/// la V2) -> EnCours « En cours » ; sinon AFaire « Disponible ». La V2 n'a pas d'etat « enregistre » : Validee n'a pas de ligne.
/// LES GESTES ne sont pas repris (aucun ecrivain) : la forme Document ne les envoie pas a la base, et dans la V2 ils sont des
/// traitements de FICHIER -- exporter sur le disque (SaveFileDialog, :310-333), modifier dans PDF_WRITER ou l'editeur TIF, supprimer
/// les pages blanches (RemoveBlankPages.exe, :338-364), fusionner (Aspose, OPE_PIECENUM.cs:184-303) ; seuls « Dupliquer » (CreateCopie,
/// :369-423) et « Enregistrer les noms de fichiers » (MODIFIER) ecrivent la table : nommes dans le rendu du lot.
/// </summary>
internal sealed class LecteurDocumentsOcerises : ILecteurEcran
{
    public string Procedure => "PROC_NUM";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Pdf = "PDF", Tif = "TIF";

    private const string Requete = """
        SELECT TOP (@plafond) d.DOCOC_ID_DOCOC, d.DOCOC_NOM_FICHIER, d.DOCOC_DATE_CREATION, d.DOCOC_NB_PAGES, d.DOCOC_TAILLE,
               d.DOCOC_NUM_JUD, d.DOCOC_CODE_PROCESSUS, d.DOCOC_ID_UTLST_EN_COURS, d.DOCOC_CODE_PROC_EN_COURS, d.DOCOC_DATE_EN_COURS,
               COUNT(*) OVER () AS Total
        FROM v2.DOCUMENT_OCERISE AS d
        WHERE ISNULL(d.DOCOC_IS_LIE_OCR_DIFFERE, 0) = 0
        ORDER BY d.DOCOC_DATE_CREATION DESC, d.DOCOC_ID_DOCOC DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var nom = FormeLigne.Texte(r, "DOCOC_NOM_FICHIER");
        var tif = nom.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || nom.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase);
        var pages = r["DOCOC_NB_PAGES"] is int p ? p : (int?)null;
        var taille = r["DOCOC_TAILLE"] is int t ? t : (int?)null;
        var enCours = r["DOCOC_ID_UTLST_EN_COURS"] is not DBNull;
        var processus = FormeLigne.Texte(r, "DOCOC_CODE_PROC_EN_COURS");
        var precisions = FormeLigne.Joindre(
            tif ? Tif : Pdf,
            pages is { } n ? $"{n} page{(n > 1 ? "s" : "")}" : null,
            taille is { } o ? $"{Math.Max(1, (o + 1023) / 1024)} Ko" : null,
            FormeLigne.DateDe(r, "DOCOC_DATE_CREATION") is { } d ? $"créé le {FormeLigne.Date(d)}" : null,
            FormeLigne.Texte(r, "DOCOC_NUM_JUD") is { Length: > 0 } j ? $"affaire {j}" : null,
            enCours ? FormeLigne.Joindre("en cours", processus.Length > 0 ? $"processus {processus}" : null,
                                         FormeLigne.DateDe(r, "DOCOC_DATE_EN_COURS") is { } e ? $"depuis le {FormeLigne.Date(e)}" : null) : null);
        return new LigneLue(nom.Length > 0 ? nom : $"Document n° {FormeLigne.Texte(r, "DOCOC_ID_DOCOC")}", precisions, tif ? Tif : Pdf,
                            enCours ? "EnCours" : "AFaire", []) { Id = FormeLigne.Texte(r, "DOCOC_ID_DOCOC") };
    }
}
