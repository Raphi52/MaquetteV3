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
/// NON REPRIS (nomme) : AJOUTER -- la V2 n'ajoute un document qu'en scannant ou en important un FICHIER (OPE_SCANNER), « Demonstration »
/// a l'ecran ; « Dupliquer » (PROC_NUM_PROC.Dupliquer, :385-423), un troisieme geste de ligne que la grille n'offre pas ; les
/// traitements de fichier (export, editeurs, pages blanches, fusion).
/// </summary>
internal sealed class EcrivainDocumentsOcerises : IEcrivainEcran
{
    public string Procedure => "PROC_NUM";

    public string Ecran => "Documents océrisés";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Modifier, TypeGeste.Supprimer];

    internal const string Introuvable = "Document introuvable parmi les documents océrisés : relisez la liste.";
    internal const string SeulNom = "Seul le nom du document s'enregistre : RIG v2 n'en laisse saisir aucune autre colonne.";
    internal const string NonRenommable = "Ce document est en cours de numérisation : RIG v2 n'enregistre pas son nom.";
    internal const string NomVide = "Nom vide : RIG v2 garde le nom du document.";
    internal const string CaracteresSpeciaux = "Des caractères spéciaux, accents ou espaces dans le nom du fichier empêchent la sauvegarde des modifications.";
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
        return geste.Demande.Type == TypeGeste.Modifier ? await RenommerAsync(geste, id, idTexte, nomFichier, ct)
                                                        : await SupprimerAsync(geste, id, idTexte, nomFichier, ct);
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
