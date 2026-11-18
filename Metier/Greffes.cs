namespace RigV3Desktop;

// ============================================================
// Les greffes de tribunal de commerce : ce que l'entete d'un extrait
// Kbis imprime (libelle du greffe, adresse, ville du R.C.S.).
//
// Le code greffe est une VRAIE colonne de RIG v2 : ETABLISSEMENT.
// CODE_GREFFE (« Code greffe », processus XACORRI / XBCORRI, cf.
// Metier/InformationsDossier.cs). Jusqu'ici l'apercu Kbis ecrivait
// « Amiens » en dur : le greffe n'etait porte par aucun champ.
//
// MAQUETTE : seule l'adresse postale d'Amiens est connue (relevee sur
// l'entete Kbis d'Amiens). Les autres greffes n'ont PAS d'adresse ici
// plutot qu'une adresse inventee : la ligne se retire de l'entete.
// ============================================================

/// <summary>Un greffe : code v2, ville du R.C.S., libelle imprime, adresse.</summary>
public sealed record Greffe(string Code, string Ville, string Libelle, string Adresse);

public static class Greffes
{
    public static readonly Greffe[] Tous =
    [
        new("8001", "AMIENS", "Greffe du Tribunal de Commerce d'Amiens",
            "14 rue Robert de Luzarches 80000 AMIENS"),
        new("7501", "PARIS", "Greffe du Tribunal de Commerce de Paris", ""),
        new("5910", "LILLE METROPOLE", "Greffe du Tribunal de Commerce de Lille Metropole", ""),
        new("6901", "LYON", "Greffe du Tribunal de Commerce de Lyon", ""),
        new("3801", "GRENOBLE", "Greffe du Tribunal de Commerce de Grenoble", ""),
    ];

    /// <summary>Le greffe de l'affaire de demonstration.</summary>
    public static Greffe Defaut => Tous[0];

    /// <summary>Ce que le formulaire propose : « 8001 - AMIENS ».</summary>
    public static string[] Choix => Tous.Select(g => $"{g.Code} - {g.Ville}").ToArray();

    /// <summary>
    /// Retrouve un greffe depuis ce que le formulaire a retenu (« 8001 - AMIENS »,
    /// « 8001 » ou « AMIENS »). Une saisie vide ou inconnue rend le greffe par
    /// defaut : l'entete du Kbis ne doit jamais se vider.
    /// </summary>
    public static Greffe Trouver(string saisie)
    {
        var texte = (saisie ?? "").Trim();
        if (texte.Length == 0) return Defaut;
        var code = texte.Split(' ')[0];
        return Tous.FirstOrDefault(g => g.Code == code)
            ?? Tous.FirstOrDefault(g => string.Equals(g.Ville, texte, StringComparison.OrdinalIgnoreCase))
            ?? Defaut;
    }
}
