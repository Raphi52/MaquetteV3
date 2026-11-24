namespace RigV3Desktop.Pont;

/// <summary>Un geste principal de la V3, tel que l'aide le montre.</summary>
/// <param name="Titre">Ce que l'agent veut faire.</param>
/// <param name="Touche">La touche, s'il y en a une (« Ctrl K », « Entrée, F5 ou F7 ») ; null pour un geste a la souris.</param>
/// <param name="Comment">Comment, en une phrase.</param>
public sealed record GesteAide(string Titre, string? Touche, string Comment);

/// <summary>Ce que l'ecran « Premiers pas » affiche.</summary>
/// <param name="PremiereFois">C'est la premiere ouverture de la V3 par cet agent : l'aide s'est ouverte d'elle-meme.</param>
/// <param name="Gestes">Les gestes principaux (SOC-026 CA06).</param>
/// <param name="EcranTouches">Le code de la fiche « Ancienne touche, nouvelle touche » (SOC-027), que l'aide ouvre.</param>
/// <param name="CheminMenu">Ou se rouvre l'aide dans le menu (« Configurer › Prendre en main › Premiers pas »).</param>
public sealed record VueEcranAide(bool PremiereFois, GesteAide[] Gestes, string EcranTouches, string NomEcranTouches, string CheminMenu);

/// <summary>
/// Pont de l'ecran « Premiers pas » (web/src/ecrans/EcranAide.tsx), SOC-026 CA06 et T04 : ecran NEUF, « a maquetter » dans le
/// carnet (la V2 n'a pas d'aide a l'ouverture). A la premiere ouverture de la V3 par un agent, il s'ouvre de lui-meme
/// (web/src/composants/Application.tsx, <see cref="AOuvrirAuDemarrage"/>) et montre les gestes principaux -- le menu geste ›
/// verbe, la recherche (Ctrl K ; Entree, F5, F7 dans un critere), les fenetres de document, Echap et « ? », signaler un
/// defaut -- et ouvre la fiche des touches (SOC-027). « Deja vue » est une preference de l'agent (AccueilEtat.AideVue) :
/// branchee au serveur, elle le suit d'un serveur RDS a l'autre (decision du lot 4). L'aide se rouvre depuis le menu.
/// Chaque geste est tire des regles du clavier et du menu de la V3 (EcranCorrespondances, MenuAccueil, EcranSignalement),
/// pour qu'elle ne promette pas une touche qui n'existe pas (le banc tests/SocleBetaCheck le verifie).
/// </summary>
public static class EcranAide
{
    /// <summary>Le nom de l'ecran (menu, catalogue des ecrans).</summary>
    public const string NomEcran = "Premiers pas";

    /// <summary>Le code de l'ecran au menu (sans fiche V2 : MenuGestesCheck le range dans ses ecrans neufs).</summary>
    public const string Code = "PROC_PREMIERS_PAS";

    /// <summary>L'ecran que la V3 ouvre d'elle-meme a son ouverture : l'aide (son code) a la premiere ouverture de cet agent,
    /// sur ce poste ou un autre ; null s'il l'a deja vue. Le code vient d'ici : seul l'aiguillage nomme les codes cote page
    /// (tests/NomMetierCheck).</summary>
    [JSInvokable("EcranAide.AOuvrirAuDemarrage")]
    public static string? AOuvrirAuDemarrage() => AccueilEtat.Charger().AideVue ? null : Code;

    /// <summary>L'aide, montree : elle est desormais « vue » pour l'agent (rien ne change si elle l'etait deja).</summary>
    [JSInvokable("EcranAide.Ouvrir")]
    public static VueEcranAide Ouvrir() => Vue(AccueilEtat.MemoriserAideVue());

    /// <summary>Ce que l'aide montre ; <paramref name="premiereFois"/> : elle vient de s'ouvrir d'elle-meme.</summary>
    public static VueEcranAide Vue(bool premiereFois)
    {
        var touches = EcranCorrespondances.TouchesDeRecherche.Select(EcranCorrespondances.Lisible)
            .OrderBy(t => t == "Entrée" ? 0 : 1).ThenBy(t => t, StringComparer.Ordinal).ToList();
        var recherche = touches.Count > 1 ? $"{string.Join(", ", touches[..^1])} ou {touches[^1]}" : string.Join("", touches);
        var gestes = new[]
        {
            new GesteAide("Trouver un écran : le menu, geste › verbe", null,
                $"À gauche, les gestes du greffe ({string.Join(", ", MenuAccueil.Moments.Select(m => m.Titre))}), chacun coupé en verbes ; " +
                "un écran s'ouvre dans un onglet de la barre du haut, et la maison ramène à l'accueil."),
            new GesteAide("Rechercher partout", "Ctrl K",
                "De n'importe quel écran : affaires, écrans et pièces ; les flèches choisissent la ligne, Entrée l'ouvre."),
            new GesteAide("Lancer une recherche", recherche,
                "Dans un critère d'un écran de recherche, comme dans l'ancien RIG ; F5 ne recharge plus la fenêtre."),
            new GesteAide("Ouvrir une pièce dans sa propre fenêtre", null,
                "Sur l'aperçu d'une pièce, « Détacher » l'ouvre dans une fenêtre de document ; un onglet glissé hors de la barre part dans sa fenêtre, sur un autre écran."),
            new GesteAide("Fermer", "Échap", "Ce qui est ouvert : une fenêtre, l'affaire, puis l'écran."),
            new GesteAide("Voir les touches de l'écran", "?",
                "Hors d'un champ : les touches de l'écran ouvert, avec l'ancienne touche de l'ancien RIG à côté."),
            new GesteAide("Signaler un défaut", EcranSignalement.Raccourci,
                "Ou le bouton « Signaler un défaut » en haut de l'accueil : l'écran, le greffe, la version et l'heure partent avec votre phrase ; gardez l'identifiant de suivi."),
        };
        return new VueEcranAide(premiereFois, gestes, EcranCorrespondances.Code, EcranCorrespondances.NomEcran, CheminMenu(Code));
    }

    /// <summary>« Configurer › Prendre en main › Premiers pas » : ou le menu range un ecran ; son seul nom s'il n'y est pas.
    /// Une procedure reunie se trouve sous l'ecran qui la reunit : « Modifier › L'affaire › Corriger un dossier RCS › Mentions en marge ».</summary>
    public static string CheminMenu(string code)
    {
        foreach (var m in MenuAccueil.Moments)
            foreach (var s in m.Sous)
            {
                if (s.Entrees.FirstOrDefault(e => e.Code == code) is { } e)
                    return string.Join(" › ", new[] { m.Titre, s.Titre, e.Libelle }.Where(t => t.Length > 0));
                if (s.Entrees.FirstOrDefault(e => e.Membres.Any(x => x.Code == code)) is { } reunion)
                    return string.Join(" › ", new[] { m.Titre, s.Titre, reunion.Libelle, CatalogueEcrans.Nom(code) }.Where(t => t.Length > 0));
            }
        return CatalogueEcrans.Nom(code);
    }
}
