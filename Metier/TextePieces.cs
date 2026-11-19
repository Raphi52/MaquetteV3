using System.Collections.Concurrent;
using System.IO;

namespace RigV3Desktop;

// ============================================================
// LE TEXTE D'UNE PIECE, page par page, pour la recherche (Metier/RecherchePalette.cs).
//
// Une piece de demonstration porte son texte dans l'arbre (NoeudGed.Apercu) : une
// seule page. Un VRAI document de greffe est un PDF (NoeudGed.Fichier, sous wwwroot) :
// son texte a ete tire une fois pour toutes par extraire-texte-pdf.ps1 dans
// « <document>.pdf.txt », a cote du PDF, les pages separees par un saut de page (\f).
// Les pages vides restent a leur place : la page N du texte est la page N du PDF, et
// la liseuse de la page « Recherche » peut ouvrir le PDF a la page du passage.
// Un PDF sans fichier texte (image numerisee, jamais extrait) n'a pas de texte.
// ============================================================
public static class TextePieces
{
    private static readonly ConcurrentDictionary<string, string[]> Lus = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Les pages de texte de la piece ; vide si elle n'en a pas.</summary>
    /// <param name="wwwroot">Le dossier servi a la page (Pont/PageWeb.DossierWwwroot).</param>
    public static string[] Pages(NoeudGed piece, string wwwroot)
    {
        if (!piece.EstPiece) return [];
        if (piece.Apercu.Length > 0) return [piece.Apercu];
        return piece.APdf ? Lus.GetOrAdd(Path.Combine(wwwroot, piece.Fichier + ".txt"), Lire) : [];
    }

    private static string[] Lire(string chemin)
    {
        if (!File.Exists(chemin)) return [];
        var pages = File.ReadAllText(chemin).Replace("\r\n", "\n").Split('\f');
        // pdftotext termine CHAQUE page par un saut : apres le dernier, rien.
        if (pages.Length > 1 && pages[^1].Trim().Length == 0) pages = pages[..^1];
        return pages.Any(p => p.Any(char.IsLetterOrDigit)) ? [.. pages.Select(p => p.TrimEnd())] : [];
    }
}
