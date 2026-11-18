namespace RigV3Desktop;

/// <summary>
/// OU se pose une fenetre de document quand AUCUNE position n'est encore
/// memorisee pour son rang.
///
/// Le calcul vit ICI, en fonction PURE prenant les zones de travail en
/// parametre, et non dans <see cref="FenetreDocument"/> : la methode de Window
/// interrogeait l'API Windows en dur, donc la branche « il y a un second
/// ecran » etait INTESTABLE sur un poste a un seul moniteur -- et c'est
/// justement la branche qui compte pour qui travaille sur plusieurs ecrans
/// (demande du 14/09/2026). Sous cette forme, les controles internes la
/// rejouent avec autant d'ecrans qu'ils veulent.
/// </summary>
public static class PlacementDocument
{
    /// <summary>Une zone de travail d'ecran, en unites WPF.</summary>
    public readonly record struct Zone(double X, double Y, double Largeur, double Hauteur);

    /// <summary>
    /// Rend la place a donner a la fenetre, ou null s'il n'y a aucun ecran.
    /// </summary>
    /// <param name="zones">Zones de travail de TOUS les ecrans presents.</param>
    /// <param name="indexPrincipal">
    /// Index, dans <paramref name="zones"/>, de l'ecran qui porte la fenetre
    /// principale ; -1 quand il est inconnu.
    /// </param>
    /// <param name="rang">Rang de la fenetre (1 = la premiere sortie).</param>
    /// <param name="largeurActuelle">Largeur par defaut de la fenetre.</param>
    /// <param name="hauteurActuelle">Hauteur par defaut de la fenetre.</param>
    public static PoseFenetre? Calculer(
        IReadOnlyList<Zone> zones,
        int indexPrincipal,
        int rang,
        double largeurActuelle,
        double hauteurActuelle)
    {
        if (zones is null || zones.Count == 0) return null;

        var autre = -1;
        for (var i = 0; i < zones.Count; i++)
        {
            if (i == indexPrincipal) continue;
            autre = i;
            break;
        }

        var cible = autre >= 0
            ? autre
            : (indexPrincipal >= 0 && indexPrincipal < zones.Count ? indexPrincipal : 0);
        var z = zones[cible];

        // Decalage en cascade : deux fenetres sorties ne doivent pas se
        // recouvrir EXACTEMENT, sinon la seconde semble ne pas s'etre ouverte.
        var marche = 40 * Math.Max(0, rang - 1);

        if (autre >= 0)
        {
            // Un ecran libre : le document prend toute sa zone de travail.
            var pas = Math.Min(marche, Math.Max(0, z.Largeur - 400));
            return new PoseFenetre
            {
                Gauche = z.X + pas,
                Haut = z.Y + pas,
                Largeur = z.Largeur - pas,
                Hauteur = z.Hauteur - pas,
            };
        }

        // Un seul ecran : le document occupe la moitie droite, l'affaire reste
        // lisible a gauche.
        var largeur = Math.Min(largeurActuelle, z.Largeur / 2);
        var hauteur = Math.Min(hauteurActuelle, z.Hauteur);
        var decal = Math.Min(marche, Math.Max(0, z.Largeur / 2 - 200));
        return new PoseFenetre
        {
            Gauche = z.X + z.Largeur - largeur - decal,
            Haut = z.Y + decal,
            Largeur = largeur,
            Hauteur = hauteur,
        };
    }
}
