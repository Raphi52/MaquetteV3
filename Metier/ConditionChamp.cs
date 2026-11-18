namespace RigV3Desktop;

/// <summary>
/// La regle d'AFFICHAGE CONDITIONNEL d'un champ du formulaire guide.
///
/// Elle vivait dans Home.razor, en methode privee : aucun banc ne pouvait
/// l'exercer, et la seule preuve possible etait de lire le fichier source.
/// Elle est ici pour etre APPELEE par un test, pas seulement relue.
///
/// Forme de la condition : « COLONNE=Valeur », plusieurs valeurs separees
/// par « | », et « * » pour « renseigne, quelle que soit la valeur ».
/// La colonne testee est celle de la MEME fiche : un dirigeant personne
/// morale ne cache pas un champ de l'etablissement.
/// </summary>
public static class ConditionChamp
{
    /// <summary>Vrai si le champ doit etre montre. Pas de condition = toujours visible.</summary>
    public static bool Remplie(IReadOnlyDictionary<string, string> fiche, string visibleSi)
    {
        if (string.IsNullOrWhiteSpace(visibleSi)) return true;
        var coupure = visibleSi.IndexOf('=');
        if (coupure <= 0) return true;
        var colonne = visibleSi[..coupure];
        var attendues = visibleSi[(coupure + 1)..].Split('|');
        var valeur = fiche.TryGetValue(colonne, out var v) ? v : string.Empty;
        return attendues.Contains("*")
            ? !string.IsNullOrWhiteSpace(valeur)
            : attendues.Contains(valeur);
    }
}
