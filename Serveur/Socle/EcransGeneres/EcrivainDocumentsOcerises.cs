using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Documents océrisés » (PROC_NUM, forme Referentiel) : les deux gestes de RIG v2 qui ECRIVENT la table DOCUMENT_OCERISE depuis la
/// liste -- renommer un document et le supprimer. Mode d'emploi : EcrivainEcran.cs ; la grille : LecteurDocumentsOcerises.cs (plan
/// « RIG v3 100 % fonctionnel », lot 11, reprise du 27/11/2026).
///
/// MODIFIER = « Enregistrer les noms de fichiers » (ETP_PIECENUM_\OPE_PIECENUM_1.Modifier, :341-377 ; la grille :
/// Composants_\TreeViewPiecesNum.NodeNom_ChangesApplied, :346-365). Seule la colonne « Nom » s'enregistre (la V2 n'en saisit aucune
/// autre). Controles, dans l'ordre de la V2 :
///   - un document « DIFFERE », « en cours de numerisation » ou en numerisation rapide n'est pas renomme (Modifier, :344-354 : return
///     sans rien ecrire) ;
///   - un nom hors des lettres sans accent, chiffres, espace, tiret, souligne et point (RegEx.TexteSimpleAvecEspace,
///     RigTools\TOOLS\RegEx.cs:39, le controle de format de l'ULT) : « Des caractères spéciaux, accents ou espaces dans le nom du
///     fichier empêchent la sauvegarde des modifications. » (:356-358) ;
///   - un nom vide n'enregistre rien (:375 : le champ reprend l'ancien nom).
///   Puis DOCOC_NOM_FICHIER = le nom saisi + l'extension du document (GetExtension, Divers\DocumentOcerise.cs:163-168).
/// SUPPRIMER = le menu « Suppression » de la ligne (OPE_PIECENUM_1.ContextMenuStrip_Click, :269-283) puis DocumentOcerise.Delete
/// (Divers\DocumentOcerise.cs:369-401). Controles :
///   - un document que l'OCR utilise (une ligne de VIDIGED_INDEX_TMP_OCR pour lui, IsLieVidigedTmpOCR, :91-103) : « Ce document est en
///     cours d'utilisation pour océrisation. Il sera supprimé de cette liste automatiquement une fois le traitement terminé. » ;
///   - un document lie a l'OCR differe n'est pas dans la liste (LecteurDocumentsOcerises) : introuvable.
///   Puis ses tags (DOCUMENT_OCERISE_TAGS du document, :384-390) et le document lui-meme, dans la transaction du geste.
/// DUPLIQUER = le bouton « Dupliquer le document sélectionné » (FORM_NUM.cs:99-100 ; PROC_NUM_PROC.Dupliquer, :388-423 ;
/// CreateCopie, :369-386), au menu de la ligne (type Agir, descripteur.Gestes). Controle : un document sans fichier
/// (DOCOC_FICHIER vide : GetFichierRaw ne rend aucun fichier, PathOrFileExists faux, :406) : « Impossible de dupliquer le document »
/// (:417). Puis une NOUVELLE ligne de DOCUMENT_OCERISE : les champs du document (DocumentOcerise.Copie : tous ceux de _listeChamps,
/// ni l'identifiant ni le fichier, RigBaseGreffe\GREFFE\DOCUMENT_OCERISE.cs:266-270), le fichier recopie (DOCOC_FICHIERSetFile,
/// :382), la creation a maintenant, ni processus ni utilisateur en cours (:375-378), et le nom de la copie que la V2 propose : la
/// saisie de la V2 s'ouvre sur le nom du document (:392-400) et, ce nom garde, la copie s'appelle « <nom>-copie.<extension> »
/// (ChangeExtension, :410), les espaces en « _ » et sans accents a l'enregistrement (DocumentOcerise.Sauve, :404-408). Son tag
/// « OCR_DIFFERE » (CreateDocumentOcerise(..., isOcr), DocumentOcerise.cs:249-255 et SetTagOCR :571-580) : « 1 » si le document est
/// en traitement OCR (IsTraitementOCR, :65-69), sinon « 0 ». La question « Voulez-vous copier le document ... ? » (:390) : le choix
/// du geste au menu en tient lieu ; un autre nom que celui propose se donne ensuite par Modifier.
/// NON REPRIS (nomme) : AJOUTER -- la V2 n'ajoute un document qu'en scannant ou en important un FICHIER (OPE_SCANNER), « Demonstration »
/// a l'ecran ; les traitements de fichier (export, editeurs, pages blanches, fusion).
/// </summary>
internal sealed class EcrivainDocumentsOcerises : IEcrivainEcran
{
    public string Procedure => "PROC_NUM";

    public string Ecran => "Documents océrisés";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Modifier, TypeGeste.Supprimer, TypeGeste.Agir];

    internal const string Introuvable = "Document introuvable parmi les documents océrisés : relisez la liste.";
    internal const string SeulNom = "Seul le nom du document s'enregistre : RIG v2 n'en laisse saisir aucune autre colonne.";
    internal const string NonRenommable = "Ce document est en cours de numérisation : RIG v2 n'enregistre pas son nom.";
    internal const string NomVide = "Nom vide : RIG v2 garde le nom du document.";
    internal const string CaracteresSpeciaux = "Des caractères spéciaux, accents ou espaces dans le nom du fichier empêchent la sauvegarde des modifications.";
    internal const string Dupliquer = "Dupliquer";
    internal const string ImpossibleDupliquer = "Impossible de dupliquer le document";
    internal const string EnOcr = "Ce document est en cours d'utilisation pour océrisation. Il sera supprimé de cette liste automatiquement une fois le traitement terminé.";

    /// <summary>RegEx.TexteSimpleAvecEspace (RigTools\TOOLS\RegEx.cs:39).</summary>
    private static readonly Regex TexteSimple = new("^[- _.0-9a-zA-Z]*$", RegexOptions.CultureInvariant);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids.Length != 1 || !int.TryParse(geste.Demande.Ids[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser(Introuvable);
        string? nomFichier = null;
        await using (var r = await geste.LireAsync(LecteurDocumentsOcerises.Requete.Replace("/*document*/", "AND d.DOCOC_ID_DOCOC = @document", StringComparison.Ordinal),
                         ct, new SqlParameter("@plafond", 1), new SqlParameter("@document", id)))
            if (await r.ReadAsync(ct)) nomFichier = FormeLigne.Texte(r, "DOCOC_NOM_FICHIER");
        if (nomFichier is null) return IssueGeste.Refuser(Introuvable);
        var idTexte = id.ToString(CultureInfo.InvariantCulture);
        return geste.Demande.Type switch
        {
            TypeGeste.Modifier => await RenommerAsync(geste, id, idTexte, nomFichier, ct),
            TypeGeste.Supprimer => await SupprimerAsync(geste, id, idTexte, nomFichier, ct),
            _ when geste.Demande.Nom == Dupliquer => await DupliquerAsync(geste, id, nomFichier, ct),
            _ => IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}."),
        };
    }

    /// <summary>« Dupliquer » (en tete du fichier). fix-ok: mesure par le controle final du lot 11 (27/11/2026) -- la grille
    /// n'offrait que Modifier et Supprimer et cet ecrivain ne connaissait pas Agir : la copie de la V2 (CreateCopie, une ecriture en
    /// base, PROC_NUM_PROC.cs:369-386) n'etait pas reprise.</summary>
    private static async Task<IssueGeste> DupliquerAsync(GesteEnCours geste, int id, string nomFichier, CancellationToken ct)
    {
        var valeurs = new Dictionary<string, object?>(StringComparer.Ordinal);
        byte[]? fichier = null;
        await using (var r = await geste.LireAsync("SELECT * FROM v2.DOCUMENT_OCERISE WHERE DOCOC_ID_DOCOC = @id", ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser(Introuvable);
            for (var k = 0; k < r.FieldCount; k++)
            {
                var colonne = r.GetName(k);
                if (colonne.Equals("DOCOC_ID_DOCOC", StringComparison.OrdinalIgnoreCase)) continue;
                if (colonne.Equals("DOCOC_FICHIER", StringComparison.OrdinalIgnoreCase)) fichier = r.IsDBNull(k) ? null : (byte[])r.GetValue(k);
                else valeurs[colonne] = r.IsDBNull(k) ? null : r.GetValue(k);
            }
        }
        if (fichier is null) return IssueGeste.Refuser(ImpossibleDupliquer);

        var copie = NomDeCopie(nomFichier);
        valeurs["DOCOC_NOM_FICHIER"] = copie;
        valeurs["DOCOC_DATE_CREATION"] = geste.Maintenant;
        foreach (var enCours in new[] { "DOCOC_ID_UTLST_EN_COURS", "DOCOC_CODE_PROC_EN_COURS", "DOCOC_DATE_EN_COURS" })
            if (valeurs.ContainsKey(enCours)) valeurs[enCours] = null;
        valeurs["DOCOC_FICHIER"] = fichier;
        var idCopie = await geste.InsererAsync("v2.DOCUMENT_OCERISE", "DOCOC_ID_DOCOC", valeurs, ct);

        bool ocr;
        await using (var r = await geste.LireAsync("""
            SELECT TOP (1) 1 FROM v2.DOCUMENT_OCERISE_TAGS
            WHERE DOCOT_ID_DOCOC = @id AND DOCOT_TYPE = 'OCR_DIFFERE' AND CONVERT(varchar(10), DOCOT_DONNEES_OCERISEES) = '1'
            """, ct, new SqlParameter("@id", id)))
            ocr = await r.ReadAsync(ct);
        await geste.InsererAsync("v2.DOCUMENT_OCERISE_TAGS", "DOCOT_ID_DOCOT", new Dictionary<string, object?>
        {
            ["DOCOT_DONNEES_OCERISEES"] = ocr ? "1" : "0", ["DOCOT_ID_DOCOC"] = idCopie, ["DOCOT_NUM_PAGE"] = 0, ["DOCOT_TYPE"] = "OCR_DIFFERE",
        }, ct);
        var idCopieTexte = idCopie.ToString(CultureInfo.InvariantCulture);
        return IssueGeste.Ecrit($"Document « {nomFichier} » dupliqué en « {copie} », le nom que RIG v2 propose (il se change par Modifier).",
            [idCopieTexte], new TraceGeste("dossier", $"le document océrisé {copie}", idCopieTexte, "a cree", null,
                new Dictionary<string, string> { ["Nom du fichier"] = copie, ["Copie de"] = nomFichier }));
    }

    /// <summary>Le nom de la copie que la V2 propose (PROC_NUM_PROC.cs:410, puis DocumentOcerise.Sauve :404-408) : « -copie » avant
    /// l'extension, les espaces en « _ », sans accents.</summary>
    internal static string NomDeCopie(string nom)
    {
        var copie = Path.ChangeExtension(nom, "-copie" + Path.GetExtension(nom)).Replace(".-", "-", StringComparison.Ordinal).Replace(' ', '_');
        var decompose = copie.Normalize(System.Text.NormalizationForm.FormD);
        return new string([.. decompose.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)])
            .Normalize(System.Text.NormalizationForm.FormC);
    }

    private static async Task<IssueGeste> RenommerAsync(GesteEnCours geste, int id, string idTexte, string nomFichier, CancellationToken ct)
    {
        var colonne = LecteurDocumentsOcerises.Colonnes[0];
        if (!geste.Demande.Valeurs.TryGetValue(colonne, out var saisi)) return IssueGeste.Refuser(SeulNom);
        if (!LecteurDocumentsOcerises.Renommable(nomFichier)) return IssueGeste.Refuser(NonRenommable);
        var nom = saisi.Trim();
        if (!TexteSimple.IsMatch(nom)) return IssueGeste.Refuser(CaracteresSpeciaux);
        if (nom.Length == 0) return IssueGeste.Refuser(NomVide);
        var nouveau = nom + LecteurDocumentsOcerises.Extension(nomFichier);
        await geste.EcrireAsync("UPDATE v2.DOCUMENT_OCERISE SET DOCOC_NOM_FICHIER = @nom WHERE DOCOC_ID_DOCOC = @id", ct,
                                new SqlParameter("@nom", nouveau), new SqlParameter("@id", id));
        return IssueGeste.Ecrit($"Document renommé : « {nomFichier} » devient « {nouveau} ».", [idTexte],
            new TraceGeste("dossier", $"le document océrisé {nouveau}", idTexte, "a modifie",
                new Dictionary<string, string> { ["Nom du fichier"] = nomFichier }, new Dictionary<string, string> { ["Nom du fichier"] = nouveau }));
    }

    private static async Task<IssueGeste> SupprimerAsync(GesteEnCours geste, int id, string idTexte, string nomFichier, CancellationToken ct)
    {
        bool enOcr;
        await using (var r = await geste.LireAsync("SELECT TOP (1) 1 FROM v2.VIDIGED_INDEX_TMP_OCR WHERE VITOC_ID_DOC_OCR = @id", ct,
                         new SqlParameter("@id", id)))
            enOcr = await r.ReadAsync(ct);
        if (enOcr) return IssueGeste.Refuser(EnOcr);
        var tags = await geste.EcrireAsync("DELETE FROM v2.DOCUMENT_OCERISE_TAGS WHERE DOCOT_ID_DOCOC = @id", ct, new SqlParameter("@id", id));
        await geste.EcrireAsync("DELETE FROM v2.DOCUMENT_OCERISE WHERE DOCOC_ID_DOCOC = @id", ct, new SqlParameter("@id", id));
        var avecTags = tags switch { 0 => "", 1 => ", avec sa donnée océrisée", _ => $", avec ses {tags} données océrisées" };
        return IssueGeste.Ecrit($"Document « {nomFichier} » supprimé{avecTags}.",
            [idTexte], new TraceGeste("dossier", $"le document océrisé {nomFichier}", idTexte, "a supprime",
                new Dictionary<string, string> { ["Nom du fichier"] = nomFichier }, null));
    }
}
