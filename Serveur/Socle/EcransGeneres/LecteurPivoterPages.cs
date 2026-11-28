using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Pivoter des pages » (PROC_PDF_WRITER, forme Document) : les documents de la base du greffe que RIG v2 ouvre dans son editeur
/// PDF, et la fiche d'un document ouvert. Mode d'emploi des lecteurs : LecteurEcran.cs (plan « RIG v3 complet, mesure par le banc du
/// parcours complet », lot 5, 28/11/2026).
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_PDF_WRITER (fiche proc_pdf_writer.md). PROC_PDF_WRITER ne
/// lit aucune table : il recoit un FICHIER (parametres FilenameIn / FilenameOut, PROC_PDF_WRITER.cs:60-61). Le seul appelant qui lui
/// donne un document DE LA BASE est « Documents ocerises » (PROC_NUM, _Editer, PROC_NUM_PROC.cs:160-215) : le fichier d'un
/// DocumentOcerise (DOCOC_FICHIER) extrait sur le poste (GetFichierRaw, Divers\DocumentOcerise.cs:467-475). L'autre appelant, la GED
/// des documents dematerialises (UltTreeViewDocDemat.cs:276-289), lui donne un fichier du DISQUE, hors base : non lu ici.
/// LES LIGNES : les documents de la liste de PROC_NUM (LecteurDocumentsOcerises.Requete : hors OCR differe, OPE_PIECENUM.cs:93-96 ;
///   du plus recent au plus ancien), Id = DOCOC_ID_DOCOC. Le fichier lui-meme n'est pas lu : sa presence seulement (DATALENGTH).
/// GROUPES : l'aiguillage de _Editer (PROC_NUM_PROC.cs:162) -- « TIF » pour un .tif / .tiff, « PDF » sinon (un autre format est
///   converti en PDF avant l'editeur, _GetFichierPDFAEditer, :291-308).
/// REGLE D'ETAT, dans l'ordre de _Editer :
///   - un TIF -> EnAttente « Éditeur TIF » : RIG v2 ne l'ouvre dans PDF_WRITER que si le parametre PDF_WRITER_POUR_TIF vaut vrai
///     (PROC_NUM_PROC.cs:163-164), sinon dans son editeur TIF (FORM_EDITPN, :183-196). Ce parametre est dans RIG_PARAMETRE de la base
///     commune COMMUN_RIG (RigParametre.cs:215-233), absente de la base du greffe (et du COMMUN_RIG de SQL-DEV, lu le 28/11/2026) :
///     la V3 applique le cas par defaut de la V2, la ligne le dit ;
///   - un PDF sans fichier dans la base -> Rejetee « Fichier absent » : la V2 refuse de l'ouvrir, « Le fichier est corrompu. »
///     (_GetFichierPDFAEditer rend "" , PROC_NUM_PROC.cs:202-205) ;
///   - un PDF qu'un processus tient en cours (DOCOC_ID_UTLST_EN_COURS, la reservation de la V2) -> EnCours « En cours ailleurs » ;
///   - sinon AFaire « À pivoter ». Validee « Enregistré » n'a pas de ligne : RIG v2 ne marque pas un document pivote.
/// LA FICHE (IFicheEcran) : le document que PDF_WRITER ouvre -- section « Fichier source » (nom, format, pages, taille, et « Pages
///   tournées » : la rotation de chaque page lue dans le fichier, PagesPdf.cs, ce que les vignettes de PDF_WRITER montrent). Un document
///   que la V2 n'ouvre pas dans PDF_WRITER (TIF, fichier absent) s'ouvre en lecture seule, avec le motif de la ligne.
/// LA ROTATION et son enregistrement (« Enregistrer le PDF ») : EcrivainPivoterPages.cs (lot 5, 28/11/2026).
/// fix-ok: les citations relues par grep le 28/11/2026 (PROC_PDF_WRITER.cs:60-61 et :89, PROC_NUM_PROC.cs:162-163, :191, :203) ; la
/// 1re redaction donnait :58-59 pour FilenameIn / FilenameOut. Les autres editions de ce fichier neuf : une coquille de commentaire et la
/// garde qui leve si la requete de LecteurDocumentsOcerises change.
/// </summary>
internal sealed class LecteurPivoterPages : IFicheEcran
{
    public string Procedure => "PROC_PDF_WRITER";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Pdf = "PDF", Tif = "TIF";

    /// <summary>Le message de RIG v2 quand le fichier du document ne se lit pas (PROC_NUM_PROC.cs:203).</summary>
    internal const string Corrompu = "Le fichier est corrompu.";

    /// <summary>Ce que la ligne d'un TIF dit (en tete) : RIG v2 l'ouvre dans son editeur TIF.</summary>
    internal const string EditeurTif =
        "s'ouvre dans l'éditeur TIF de RIG v2 (paramètre PDF_WRITER_POUR_TIF de COMMUN_RIG, absent de la base du greffe)";

    /// <summary>La liste de PROC_NUM, plus la presence du fichier ; « /*document*/ » recoit le filtre d'un seul document (fiche).</summary>
    private static readonly string Requete = AvecFichier(LecteurDocumentsOcerises.Requete);

    /// <summary>Un document de la liste (@document ; @plafond = 1) : la fiche, et l'ecrivain qui verifie que la V2 l'ouvrirait.</summary>
    internal static readonly string RequeteDocument = Requete.Replace("/*document*/", "AND d.DOCOC_ID_DOCOC = @document", StringComparison.Ordinal);

    /// <summary>La requete de PROC_NUM, la presence du fichier en plus ; leve si sa liste de colonnes a change (rien de silencieux).</summary>
    private static string AvecFichier(string requete)
    {
        const string Ancre = "d.DOCOC_DATE_EN_COURS,";
        if (!requete.Contains(Ancre, StringComparison.Ordinal))
            throw new InvalidOperationException($"LecteurPivoterPages : la requete de LecteurDocumentsOcerises ne porte plus « {Ancre} ».");
        return requete.Replace(Ancre, Ancre + " CASE WHEN DATALENGTH(d.DOCOC_FICHIER) > 0 THEN 1 ELSE 0 END AS AvecFichier,", StringComparison.Ordinal);
    }

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*document*/", "", StringComparison.Ordinal), Ligne, ct,
                                  new SqlParameter("@plafond", plafond));

    /// <summary>Le document que PDF_WRITER ouvre (en tete) ; en lecture seule s'il ne l'ouvre pas.</summary>
    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var document)) return null;
        LigneLue l;
        string pages, taille, fichier;
        await using (var r = await LectureV2.LireAsync(baseGreffe, RequeteDocument, ct, new SqlParameter("@plafond", 1), new SqlParameter("@document", document)))
        {
            if (!await r.ReadAsync(ct)) return null;
            l = Ligne(r);
            pages = r["DOCOC_NB_PAGES"] is int p ? p.ToString(CultureInfo.InvariantCulture) : "";
            taille = r["DOCOC_TAILLE"] is int t ? Ko(t) : "";
            fichier = Convert.ToInt32(r["AvecFichier"], CultureInfo.InvariantCulture) == 1
                ? FormeLigne.Texte(r, "DOCOC_NOM_FICHIER") : $"absent de la base : « {Corrompu} »";
        }
        var tournees = "";
        if (l.Etat is "AFaire" or "EnCours")
        {
            await using var f = await LectureV2.LireAsync(baseGreffe, "SELECT DOCOC_FICHIER FROM v2.DOCUMENT_OCERISE WHERE DOCOC_ID_DOCOC = @document", ct,
                                                         new SqlParameter("@document", document));
            await f.ReadAsync(ct);
            try { tournees = Tournees(PagesPdf.Rotations((byte[])f[0])); }
            catch (PagesPdf.PdfIllisible e) { tournees = $"fichier illisible : {e.Message}"; }
        }
        return new FicheEcran(Procedure, id, l.Nom,
            [new SectionFiche("Fichier source", [new("Fichier source", fichier), new("Format", l.Groupe), new("Pages", pages), new("Taille", taille),
                                                 new("Pages tournées", tournees)])])
        {
            LectureSeule = l.Etat is "EnAttente" or "Rejetee",
            Motif = l.Etat switch { "EnAttente" => $"Ce TIF {EditeurTif}.", "Rejetee" => Corrompu, _ => null },
        };
    }

    private static string Ko(int octets) => $"{Math.Max(1, (octets + 1023) / 1024)} Ko";

    /// <summary>Les pages tournees d'un fichier, en mots : « page 1 : 90° ; page 3 : 180° », ou « aucune ».</summary>
    internal static string Tournees(int[] rotations)
    {
        var tournees = rotations.Select((r, k) => (Page: k + 1, Rotation: r)).Where(p => p.Rotation != 0).ToArray();
        return tournees.Length == 0 ? "aucune" : string.Join(" ; ", tournees.Select(p => $"page {p.Page} : {p.Rotation}°"));
    }

    internal static LigneLue Ligne(SqlDataReader r)
    {
        var nom = FormeLigne.Texte(r, "DOCOC_NOM_FICHIER");
        var tif = nom.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || nom.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase);
        var avecFichier = Convert.ToInt32(r["AvecFichier"], CultureInfo.InvariantCulture) == 1;
        var enCours = r["DOCOC_ID_UTLST_EN_COURS"] is not DBNull;
        var processus = FormeLigne.Texte(r, "DOCOC_CODE_PROC_EN_COURS");
        // L'ordre de _Editer (PROC_NUM_PROC.cs:162, :199-205) : le format d'abord, puis le fichier ; la reservation ensuite.
        var etat = tif ? "EnAttente" : !avecFichier ? "Rejetee" : enCours ? "EnCours" : "AFaire";
        var precisions = FormeLigne.Joindre(
            tif ? Tif : Pdf,
            r["DOCOC_NB_PAGES"] is int n ? $"{n} page{(n > 1 ? "s" : "")}" : null,
            r["DOCOC_TAILLE"] is int o ? Ko(o) : null,
            FormeLigne.DateDe(r, "DOCOC_DATE_CREATION") is { } d ? $"créé le {FormeLigne.Date(d)}" : null,
            FormeLigne.Texte(r, "DOCOC_NUM_JUD") is { Length: > 0 } j ? $"affaire {j}" : null,
            etat switch
            {
                "EnAttente" => EditeurTif,
                "Rejetee" => $"fichier absent de la base : « {Corrompu} »",
                "EnCours" => FormeLigne.Joindre("en cours", processus.Length > 0 ? $"processus {processus}" : null,
                                                FormeLigne.DateDe(r, "DOCOC_DATE_EN_COURS") is { } e ? $"depuis le {FormeLigne.Date(e)}" : null),
                _ => null,
            });
        return new LigneLue(nom.Length > 0 ? nom : $"Document n° {FormeLigne.Texte(r, "DOCOC_ID_DOCOC")}", precisions, tif ? Tif : Pdf, etat, [])
        {
            Id = FormeLigne.Texte(r, "DOCOC_ID_DOCOC"),
        };
    }
}
