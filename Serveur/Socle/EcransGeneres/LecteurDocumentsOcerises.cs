using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Documents océrisés » (PROC_NUM, forme Referentiel) : les documents numerises ou importes du greffe, en PDF ou en TIF. Mode d'emploi
/// des lecteurs : LecteurEcran.cs (plan « RIG v3 100 % fonctionnel », lot 11, 27/11/2026).
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_NUM (proc_num.md, Detail OPE, qui fait foi). La liste est celle d'OPE_PIECENUM
/// (ETP_PIECENUM_\OPE_PIECENUM.cs:85-107) : les DocumentOcerise du greffe -- de l'utilisateur, ou tous -- SAUF ceux lies a l'OCR differe
/// (DOCOC_IS_LIE_OCR_DIFFERE = 0, :93-96), du plus recent au plus ancien (DOCOC_DATE_CREATION). Le fichier lui-meme (DOCOC_FICHIER) n'est
/// pas lu : la liste n'en montre que le nom, les pages et la taille. Dans la base V3, DOCUMENT_OCERISE est la table V2 telle quelle.
/// FORME REFERENTIEL (lot 11, reprise) : la liste de la V2 est une grille dont le NOM se saisit dans la ligne puis s'enregistre
/// (TreeViewPiecesNum.NodeNom_ChangesApplied, Composants_\TreeViewPiecesNum.cs:346-365 ; le champ nom d'OPE_PIECENUM_1.Modifier,
/// :341-377) : la colonne « Nom » est le nom SANS extension, comme la V2 l'affiche (Path.GetFileNameWithoutExtension,
/// OPE_PIECENUM_10.cs:105, OPE_PIECENUM_1.cs:121). Les cellules suivent les colonnes du descripteur : Nom, Format, Pages, Taille,
/// Cree le, Affaire. La fiche d'une ligne (son Id = DOCOC_ID_DOCOC) porte les memes champs, le nom seul modifiable ; un document
/// « en cours de numerisation » ou en numerisation rapide garde son nom en lecture (Modifier, :344-354, n'enregistre rien pour eux).
/// GROUPES : le format de l'edition (_Editer, PROC_NUM_PROC.cs:160-215) -- « TIF » pour un fichier .tif / .tiff, « PDF » sinon (tout
/// import est converti en PDF, OPE_SCANNER.cs:691-693).
/// REGLE D'ETAT : un document qu'un processus tient en cours (DOCOC_ID_UTLST_EN_COURS et DOCOC_CODE_PROC_EN_COURS, la reservation de
/// la V2) -> EnCours « En cours » ; sinon AFaire « Disponible ». La V2 n'a pas d'etat « enregistre » : Validee n'a pas de ligne.
/// LES GESTES : EcrivainDocumentsOcerises.cs (renommer, supprimer). Les autres sont des traitements de FICHIER, non repris : exporter
/// sur le disque (SaveFileDialog, PROC_NUM_PROC.cs:310-333), modifier dans PDF_WRITER ou l'editeur TIF, supprimer les pages blanches
/// (RemoveBlankPages.exe, :338-364), fusionner (Aspose, OPE_PIECENUM.cs:184-303) ; et « Dupliquer » (CreateCopie, :369-423), un
/// troisieme geste de ligne que la grille n'offre pas (Modifier et Supprimer seuls, FormeReferentiel.tsx).
/// </summary>
internal sealed class LecteurDocumentsOcerises : IFicheEcran
{
    public string Procedure => "PROC_NUM";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Pdf = "PDF", Tif = "TIF";

    /// <summary>Les colonnes du descripteur, au mot pres ; « Nom » est la seule que la V2 laisse saisir.</summary>
    internal static readonly string[] Colonnes = ["Nom", "Format", "Pages", "Taille", "Créé le", "Affaire"];

    /// <summary>Le nom d'un document qu'on numerise encore (OPE_PIECENUM_1.cs:129, :352 ; OPE_PIECENUM_10._DOC_OC_DIFFERE, :27).</summary>
    internal const string EnNumerisation = "en cours de numérisation", Differe = "DIFFERE";

    /// <summary>Le marqueur d'une numerisation rapide (DocumentOcerise.NUMERISATION_RAPIDE, Divers\DocumentOcerise.cs:29, :170-175).</summary>
    internal const string NumerisationRapide = "Numerisation_Rapide";

    /// <summary>La requete de la liste ; « /*document*/ » recoit le filtre d'un seul document (fiche, ecrivain).</summary>
    internal const string Requete = """
        SELECT TOP (@plafond) d.DOCOC_ID_DOCOC, d.DOCOC_NOM_FICHIER, d.DOCOC_DATE_CREATION, d.DOCOC_NB_PAGES, d.DOCOC_TAILLE,
               d.DOCOC_NUM_JUD, d.DOCOC_CODE_PROCESSUS, d.DOCOC_ID_UTLST_EN_COURS, d.DOCOC_CODE_PROC_EN_COURS, d.DOCOC_DATE_EN_COURS,
               COUNT(*) OVER () AS Total
        FROM v2.DOCUMENT_OCERISE AS d
        WHERE ISNULL(d.DOCOC_IS_LIE_OCR_DIFFERE, 0) = 0 /*document*/
        ORDER BY d.DOCOC_DATE_CREATION DESC, d.DOCOC_ID_DOCOC DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*document*/", "", StringComparison.Ordinal), Ligne, ct,
                                  new SqlParameter("@plafond", plafond));

    /// <summary>La fiche d'un document : ses cellules, le nom modifiable sauf pour un document en numerisation (en tete).</summary>
    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var document)) return null;
        await using var r = await LectureV2.LireAsync(baseGreffe, Requete.Replace("/*document*/", "AND d.DOCOC_ID_DOCOC = @document", StringComparison.Ordinal),
                                                     ct, new SqlParameter("@plafond", 1), new SqlParameter("@document", document));
        if (!await r.ReadAsync(ct)) return null;
        var l = Ligne(r);
        var nomable = Renommable(FormeLigne.Texte(r, "DOCOC_NOM_FICHIER"));
        return new FicheEcran(Procedure, id, l.Nom,
            [new SectionFiche("Document", [.. Colonnes.Select((c, k) => new ChampFiche(c, l.Valeurs[k]) { Modifiable = nomable && k == 0 })])]);
    }

    /// <summary>Le nom que la V2 laisse changer : ni en numerisation, ni differe, ni numerisation rapide (OPE_PIECENUM_1.cs:344-354).</summary>
    internal static bool Renommable(string nomFichier) =>
        nomFichier != Differe && nomFichier != EnNumerisation && !nomFichier.Contains(NumerisationRapide, StringComparison.OrdinalIgnoreCase);

    /// <summary>L'extension que la V2 remet au nom saisi (DocumentOcerise.GetExtension / Extension, Divers\DocumentOcerise.cs:143-168) :
    /// « .tif » en numerisation rapide, sinon celle du nom actuel, « .pdf » s'il n'en a pas.</summary>
    internal static string Extension(string nomFichier)
    {
        if (nomFichier.Contains(NumerisationRapide, StringComparison.OrdinalIgnoreCase)) return ".tif";
        var ext = System.IO.Path.GetExtension(nomFichier);
        return string.IsNullOrEmpty(ext) ? ".pdf" : ext;
    }

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
        var affiche = nom.Length > 0 && nom != Differe ? System.IO.Path.GetFileNameWithoutExtension(nom) : EnNumerisation;
        return new LigneLue(nom.Length > 0 ? nom : $"Document n° {FormeLigne.Texte(r, "DOCOC_ID_DOCOC")}", precisions, tif ? Tif : Pdf,
                            enCours ? "EnCours" : "AFaire", [])
        {
            Id = FormeLigne.Texte(r, "DOCOC_ID_DOCOC"),
            Valeurs = [affiche, tif ? Tif : Pdf, pages?.ToString(CultureInfo.InvariantCulture) ?? "",
                       taille is { } ko ? $"{Math.Max(1, (ko + 1023) / 1024)} Ko" : "",
                       FormeLigne.DateFiche(FormeLigne.DateDe(r, "DOCOC_DATE_CREATION")), FormeLigne.Texte(r, "DOCOC_NUM_JUD")],
        };
    }
}
