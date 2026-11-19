namespace RigV3Desktop.Pont;

/// <summary>Une valeur du formulaire portee au document (libelle, valeur).</summary>
public sealed record ValeurDocumentVue(string Libelle, string Valeur);

/// <summary>Une feuille de l'apercu : son numero et son texte (DocumentPartage.TextePage).</summary>
public sealed record FeuilleDocumentVue(int Numero, string Texte);

/// <summary>La piece affichee dans la fenetre detachee.</summary>
/// <param name="Fichier">Le vrai PDF, relatif a wwwroot ; vide = apercu texte.</param>
/// <param name="Feuilles">Au moins une feuille, meme pour une piece sans page.</param>
public sealed record PieceDetacheeVue(string Extension, int Pages, string Date, string Libelle,
                                      bool APdf, string Fichier, ValeurDocumentVue[] Valeurs,
                                      FeuilleDocumentVue[] Feuilles);

/// <summary>
/// Pont de la FENETRE DETACHEE du document (web/src/composants/DocumentDetacheVue.tsx) :
/// le meme apercu que la 3e zone de l'ecran VDossier, seul dans sa fenetre.
///
/// La coquille Components/DocumentDetacheVue.razor garde l'abonnement a
/// l'emplacement de SA fenetre (DocumentPartage, service de l'application qui ne
/// traverse pas vers React) et, a chaque changement, passe a React la piece
/// decrite ici. La decoupe en pages reste celle de DocumentPartage.TextePage :
/// elle n'existe qu'une fois. L'objectif (zoom) n'est PAS ici : il est propre a
/// chaque fenetre, donc tenu par React. Metier/ n'est pas modifie.
/// </summary>
public static class DocumentDetacheVue
{
    /// <summary>La piece ouverte dans cet emplacement, ou null quand aucune ne l'est.</summary>
    public static PieceDetacheeVue? Vue(DocumentDetache emplacement)
    {
        var piece = emplacement.Piece;
        if (piece is null) return null;
        var total = Math.Max(1, piece.Pages);
        return new PieceDetacheeVue(
            piece.Extension, piece.Pages, piece.Date, piece.Libelle, piece.APdf, piece.Fichier,
            (emplacement.Valeurs ?? []).Select(v => new ValeurDocumentVue(v.Libelle, v.Valeur)).ToArray(),
            Enumerable.Range(1, total).Select(f => new FeuilleDocumentVue(f, DocumentPartage.TextePage(piece, f))).ToArray());
    }
}
