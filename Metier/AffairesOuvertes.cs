namespace RigV3Desktop;

/// <summary>
/// Les affaires NEES pendant la demonstration : celles que l'ecran
/// « Ouvrir un dossier » vient de creer.
///
/// Pourquoi ici et pas dans l'ecran : un dossier ouvert qui ne serait connu
/// que de l'ecran qui l'a cree ne prouve rien. La recherche d'affaires et la
/// visionneuse doivent le retrouver, exactement comme une affaire du jeu de
/// demonstration. Ce registre est donc la SEULE source partagee, et le type
/// rendu est le <see cref="Dossier"/> deja utilise par la recherche.
///
/// Maquette : rien n'est ecrit en base, mais les dossiers ouverts sont
/// conserves dans accueil.json — comme la saisie guidee — pour survivre a la
/// fermeture de l'application.
/// </summary>
public static class AffairesOuvertes
{
    // Charge PARESSEUSEMENT depuis accueil.json : un dossier ouvert hier doit
    // etre la ce matin. Sans cela le travail de la veille disparaissait a la
    // fermeture, exactement comme la saisie guidee avant qu'elle soit ecrite.
    private static List<DossierOuvert>? _cache;

    private static List<DossierOuvert> Liste => _cache ??= AccueilEtat.Charger().DossiersOuverts;

    private static void Enregistrer()
    {
        var etat = AccueilEtat.Charger();
        etat.DossiersOuverts = Liste;
        etat.Enregistrer();
    }

    /// <summary>Le detail complet des dossiers ouverts (fiche de l'ecran).</summary>
    public static IReadOnlyList<DossierOuvert> Details => Liste;

    /// <summary>Les affaires ouvertes, de la plus recente a la plus ancienne,
    /// vues comme des affaires ordinaires de la recherche.</summary>
    public static IReadOnlyList<Dossier> Toutes
        => Liste.Select(d => new Dossier(d.Numero, d.Objet, d.Siren, "RCS", d.Statut)).ToList();

    /// <summary>Un SIREN est fait de neuf chiffres ; la saisie peut les espacer.</summary>
    public static string SirenNormalise(string? siren)
        => new((siren ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>Le dossier deja ouvert pour ce SIREN, s'il existe.</summary>
    public static Dossier? ParSiren(string? siren)
    {
        var s = SirenNormalise(siren);
        return s.Length == 0
            ? null
            : Toutes.FirstOrDefault(d => SirenNormalise(d.Siren) == s);
    }

    /// <summary>
    /// Le numero de gestion attribue par le greffe : annee + « B » + compteur.
    /// Il est calcule ici pour que deux ouvertures successives ne puissent pas
    /// porter le meme numero, quel que soit l'ecran appelant.
    /// </summary>
    public static string ProchainNumeroGestion(int annee)
    {
        var prefixe = annee.ToString() + "B";
        var suivant = Liste.Count(d => d.Numero.StartsWith(prefixe, StringComparison.Ordinal)) + 1;
        return prefixe + suivant.ToString("00000");
    }

    /// <summary>
    /// Ouvre le dossier et le rend visible partout. Rend null si le SIREN est
    /// invalide, la denomination vide, ou si ce SIREN a deja un dossier :
    /// l'appelant affiche alors le refus.
    /// </summary>
    public static Dossier? Ouvrir(string siren, string denomination, DateTime immatriculation,
                                  string? numeroGestion = null, string forme = "",
                                  decimal capital = 0m, string monnaie = "EUR", string origine = "")
    {
        var s = SirenNormalise(siren);
        if (s.Length != 9) return null;
        if (string.IsNullOrWhiteSpace(denomination)) return null;
        if (ParSiren(s) is not null) return null;

        var numero = string.IsNullOrWhiteSpace(numeroGestion)
            ? ProchainNumeroGestion(immatriculation.Year)
            : numeroGestion!.Trim();

        var ne = new DossierOuvert
        {
            Numero = numero,
            Siren = s,
            Denomination = denomination.Trim(),
            Forme = forme,
            Immatriculation = immatriculation,
            Capital = capital,
            Monnaie = monnaie,
            Origine = origine,
            Statut = "action"
        };
        Liste.Insert(0, ne);
        Enregistrer();
        return new Dossier(ne.Numero, ne.Objet, ne.Siren, "RCS", ne.Statut);
    }

    /// <summary>Remise a zero — n'existe que pour les bancs de controle.</summary>
    public static void Oublier()
    {
        Liste.Clear();
        Enregistrer();
    }

    /// <summary>Oublie ce qui est EN MEMOIRE et relit le disque : c'est ainsi
    /// qu'un banc prouve qu'un dossier a bien survecu a la fermeture.</summary>
    public static void Relire() => _cache = null;
}
