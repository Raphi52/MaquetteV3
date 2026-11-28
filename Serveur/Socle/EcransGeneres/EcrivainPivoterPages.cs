using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Pivoter des pages » (PROC_PDF_WRITER, forme Document) : « Enregistrer le PDF » tourne les pages choisies du document ouvert et
/// remplace son fichier dans la base, comme RIG v2. Mode d'emploi : EcrivainEcran.cs ; les documents et leur fiche :
/// LecteurPivoterPages.cs ; la rotation dans le fichier : PagesPdf.cs. Plan « RIG v3 complet, mesure par le banc du parcours
/// complet », lot 5, 28/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_PDF_WRITER, ouvert par « Documents océrisés »
/// (D:\RigApplication\Source\RIG\DLL\Processus\PROC_NUM\PROC_NUM_PROC.cs, _Editer :160-216, puis Form_ShowModalClosed :219-288).
/// RIG v2 joue le geste en trois temps, que la V3 fait en un :
///   1. OUVRIR le document courant (_Editer) -- dans l'ordre :
///      - aucun document : « Veuillez sélectionner un document. » (:215) ; la V3 demande exactement un document (Ids) ;
///      - un TIF s'ouvre dans l'editeur TIF de RIG v2 (FORM_EDITPN, :183-197), PDF_WRITER seulement avec le parametre
///        PDF_WRITER_POUR_TIF (:163-164), de COMMUN_RIG, absent de la base du greffe : refuse, le motif de LecteurPivoterPages ;
///      - un PDF sans fichier dans la base : « Le fichier est corrompu. » (:202-205) ;
///      - un fichier qui n'est pas un PDF est d'abord converti en PDF (_GetFichierPDFAEditer, :291-308) : la V3 ne convertit pas, refus nomme ;
///      - PDF_WRITER compte les pages (Ghostscript) : un fichier illisible, « Problème durant la lecture du fichier PDF.\r\n(le nombre
///        de pages n'a pas pu être récupéré) » (PROC_PDF_WRITER\OPE_PDF_WRITER.cs:347 ; ses deux lignes jointes ici par une espace),
///        suivi du motif en mots (PagesPdf.PdfIllisible : chiffre, renvois illisibles...).
///      La reservation du document (DOCOC_ID_UTLST_EN_COURS) n'arrete pas _Editer : un document tenu en cours se pivote aussi.
///   2. CHOISIR et TOURNER (OPE_ROTATION.cs, OPE_PDF_WRITER.cs) :
///      - « Plage de pages » (OPE_PDF_WRITER.GetNumPagesFromEtendue, :582-610) : des numeros separes par « ; », « debut-fin » pour une
///        etendue (fin ramenee au nombre de pages), un numero hors du document ignore ; une plage qui ne donne aucune page laisse
///        place aux « Pages cochées » (Rotate, :640-645) ; les pages cochees : des numeros separes par « ; » ou « , » ;
///      - un document d'UNE page n'a ni cases ni plage (PROC_PDF_WRITER.cs:74-78) : sa page est tournee ;
///      - aucune page : RIG v2 ne tourne rien (Rotate, :647 « if (numPages.Count == 0) return; ») -- la V3 refuse au lieu
///        d'enregistrer un fichier inchange ;
///      - « Rotation » : 90° horaire (+90), 90° anti-horaire (-90), 180° (+180), les boutons « 90° », « -90° », « 180° »
///        (OPE_ROTATION._InitToolbar, :49-56 ; _UpdatePageRotation, OPE_PDF_WRITER.cs:630-638). Sans sens, rien n'est tourne : refuse.
///   3. ENREGISTRER (PROC_PDF_WRITER.DoValider, :84-101 : AmiAsposePdf.RotationPagesPdf, la rotation de chaque page plus celle
///      demandee, PagesPdf.cs) puis PROC_NUM recopie le fichier (Form_ShowModalClosed, :255-268 ; DOCOC_ID_UTLST :267) : DOCOC_FICHIER (le fichier),
///      DOCOC_TAILLE (sa longueur), DOCOC_NB_PAGES (ses pages), DOCOC_ID_UTLST (l'utilisateur courant) et doc.Sauve(), qui ecrit
///      aussi le nom du fichier sans espaces (« _ ») ni accents (DocumentOcerise.Sauve, RigMetier\Divers\DocumentOcerise.cs:404-415).
///      L'utilisateur courant de la V3 : le compte de l'agent, UTLST_ID_UTLST repris dans soc.Compte.IdV2 (SOC-001_comptes.sql:24) ;
///      un agent sans utilisateur RIG v2 ecrit NULL (le journal des gestes porte son nom).
/// NON REPRIS (nomme) : la conversion en PDF d'un autre format, l'editeur TIF, « Rétablir » et les vignettes de PDF_WRITER (une vue).
/// Trace : famille « dossier » (comme EcrivainDocumentsOcerises), « a modifie », les pages tournees avant et apres.
/// </summary>
internal sealed class EcrivainPivoterPages : IEcrivainEcran
{
    public string Procedure => "PROC_PDF_WRITER";

    public string Ecran => "Pivoter des pages";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur (ModifierEnvois.cs), au mot pres.</summary>
    internal const string Enregistrer = "Enregistrer le PDF";

    /// <summary>Les sens de la liste du descripteur et leurs degres (+ horaire).</summary>
    internal static readonly (string Libelle, int Degres)[] SensDeRotation = [("90° horaire", 90), ("90° anti-horaire", -90), ("180°", 180)];

    internal const string Selectionner = "Veuillez sélectionner un document.";
    internal const string LectureImpossible = "Problème durant la lecture du fichier PDF. (le nombre de pages n'a pas pu être récupéré)";
    internal const string PasUnPdf =
        "Ce fichier n'est pas un PDF : RIG v2 le convertit en PDF avant de l'ouvrir, RIG v3 ne le convertit pas.";
    internal const string AucunePage =
        "Aucune page à pivoter : cochez des pages ou saisissez une plage (ex. 1-3;5). Sans page choisie, RIG v2 ne tourne rien.";
    internal const string SansSens =
        "Choisissez la rotation : 90° horaire, 90° anti-horaire ou 180° (les boutons « 90° », « -90° » et « 180° » de RIG v2).";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Enregistrer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        if (geste.Demande.Ids is not [var idTexte] || !int.TryParse(idTexte, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser(Selectionner);

        LigneLue ligne;
        await using (var r = await geste.LireAsync(LecteurPivoterPages.RequeteDocument, ct,
                         new SqlParameter("@plafond", 1), new SqlParameter("@document", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser(EcrivainDocumentsOcerises.Introuvable);
            ligne = LecteurPivoterPages.Ligne(r);
        }
        if (ligne.Groupe == LecteurPivoterPages.Tif) return IssueGeste.Refuser($"Ce TIF {LecteurPivoterPages.EditeurTif}.");
        if (ligne.Etat == "Rejetee") return IssueGeste.Refuser(LecteurPivoterPages.Corrompu);

        byte[] fichier;
        await using (var r = await geste.LireAsync("SELECT DOCOC_FICHIER FROM v2.DOCUMENT_OCERISE WHERE DOCOC_ID_DOCOC = @id", ct, new SqlParameter("@id", id)))
        {
            await r.ReadAsync(ct);
            fichier = (byte[])r[0];
        }
        if (!fichier.AsSpan().StartsWith("%PDF"u8))
            return IssueGeste.Refuser(ligne.Nom.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? LectureImpossible : PasUnPdf);
        int[] avant;
        try { avant = PagesPdf.Rotations(fichier); }
        catch (PagesPdf.PdfIllisible e) { return IssueGeste.Refuser($"{LectureImpossible} — {e.Message}."); }

        var pages = PagesChoisies(geste.Valeur("Sélection", "Plage de pages"), geste.Valeur("Sélection", "Pages cochées"), avant.Length);
        if (pages.Count == 0) return IssueGeste.Refuser(AucunePage);
        var saisi = Normaliser(geste.Valeur("Rotation", "Rotation"));
        if (SensDeRotation.Where(s => Normaliser(s.Libelle) == saisi).Select(s => (int?)s.Degres).FirstOrDefault() is not { } degres)
            return IssueGeste.Refuser(SansSens);

        var pivote = PagesPdf.Pivoter(fichier, pages.ToDictionary(p => p, _ => degres));
        var apres = PagesPdf.Rotations(pivote);
        var nom = NomSauve(ligne.Nom);
        int? utilisateur;
        await using (var r = await geste.LireAsync("SELECT IdV2 FROM soc.Compte WHERE CompteWindows = @c", ct, new SqlParameter("@c", geste.Agent.CompteWindows)))
            utilisateur = await r.ReadAsync(ct) && !r.IsDBNull(0) ? r.GetInt32(0) : null;
        await geste.EcrireAsync("""
            UPDATE v2.DOCUMENT_OCERISE SET DOCOC_FICHIER = @fichier, DOCOC_TAILLE = @taille, DOCOC_NB_PAGES = @pages,
                   DOCOC_ID_UTLST = @utilisateur, DOCOC_NOM_FICHIER = @nom
            WHERE DOCOC_ID_DOCOC = @id
            """, ct,
            new SqlParameter("@fichier", System.Data.SqlDbType.VarBinary, -1) { Value = pivote }, new SqlParameter("@taille", pivote.Length),
            new SqlParameter("@pages", apres.Length), new SqlParameter("@utilisateur", (object?)utilisateur ?? DBNull.Value),
            new SqlParameter("@nom", nom), new SqlParameter("@id", id));

        var libelle = SensDeRotation.First(s => s.Degres == degres).Libelle;
        var quoi = pages.Count == 1 ? $"page {pages[0]} tournée" : $"pages {string.Join(", ", pages)} tournées";
        return IssueGeste.Ecrit($"« {nom} » enregistré : {quoi} de {libelle} ; pages tournées : {LecteurPivoterPages.Tournees(apres)}.", [idTexte],
            new TraceGeste("dossier", $"le document océrisé {nom}", idTexte, "a modifie",
                new Dictionary<string, string> { ["Pages tournées"] = LecteurPivoterPages.Tournees(avant), ["Taille"] = fichier.Length.ToString(CultureInfo.InvariantCulture) },
                new Dictionary<string, string> { ["Pages tournées"] = LecteurPivoterPages.Tournees(apres), ["Taille"] = pivote.Length.ToString(CultureInfo.InvariantCulture),
                                                 ["Nom du fichier"] = nom }));
    }

    /// <summary>Les pages a tourner, dans l'ordre : la plage si elle en donne, sinon les cases cochees ; la page d'un document d'une page.</summary>
    internal static List<int> PagesChoisies(string? plage, string? cochees, int nombre)
    {
        if (nombre == 1) return [1];
        var pages = new List<int>();
        foreach (var morceau in (plage ?? "").Split(';'))
        {
            var tiret = morceau.IndexOf('-', StringComparison.Ordinal);
            if (tiret > 0)
            {
                var fin = Math.Min(Entier(morceau[(tiret + 1)..]), nombre);
                for (var p = Entier(morceau[..tiret]); p <= fin; p++) if (p >= 1 && !pages.Contains(p)) pages.Add(p);
            }
            else if (Entier(morceau) is var p && p >= 1 && p <= nombre && !pages.Contains(p)) pages.Add(p);
        }
        if (pages.Count > 0) { pages.Sort(); return pages; }
        foreach (var morceau in (cochees ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Entier(morceau) is var p && p >= 1 && p <= nombre && !pages.Contains(p)) pages.Add(p);
        pages.Sort();
        return pages;
    }

    /// <summary>ToInt32 de RIG v2 : un texte qui n'est pas un nombre vaut 0.</summary>
    private static int Entier(string texte) => int.TryParse(texte.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static string Normaliser(string? texte) => (texte ?? "").Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();

    /// <summary>Le nom que DocumentOcerise.Sauve ecrit (en tete) : espaces en « _ », lettres sans accents.</summary>
    internal static string NomSauve(string nom)
    {
        var decompose = nom.Replace(' ', '_').Normalize(System.Text.NormalizationForm.FormD);
        return new string([.. decompose.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)])
            .Normalize(System.Text.NormalizationForm.FormC);
    }
}
