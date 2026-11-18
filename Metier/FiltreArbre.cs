using System.Globalization;
using System.Text;

namespace RigV3Desktop;

/// <summary>Comparaison de la recherche de l'arbre : sans casse ni accents
/// (« societe » trouve « Société »).</summary>
public static class FiltreArbre
{
    public static string Normaliser(string? texte)
    {
        if (string.IsNullOrEmpty(texte)) return string.Empty;
        var sb = new StringBuilder(texte.Length);
        foreach (var c in texte.Trim().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
