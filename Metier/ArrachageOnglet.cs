namespace RigV3Desktop;

/// <summary>
/// DECIDE si un onglet glisse a ete sorti de la fenetre, donc s'il doit
/// partir dans sa propre fenetre (« comme un navigateur »).
///
/// Le calcul vit ici, en fonction PURE, et non dans la page : le glisser HTML
/// ne franchit pas la frontiere du WebView2, la sortie se juge donc sur les
/// coordonnees ECRAN du pointeur comparees a la fenetre principale -- une
/// branche qu'aucune compilation ne verifie et qu'on ne peut pas rejouer a la
/// main sans deux ecrans.
/// </summary>
public static class ArrachageOnglet
{
    /// <summary>Marge, en pixels : sortir d'un cheveu n'arrache pas l'onglet,
    /// sinon un glisser un peu large le long du bord detacherait par accident.</summary>
    public const double MargeDefaut = 24;

    /// <summary>
    /// Vrai quand le point (coordonnees ECRAN) est hors de la fenetre, marge
    /// comprise. <paramref name="largeur"/> ou <paramref name="hauteur"/> nulle
    /// = fenetre inconnue : on ne detache pas (mieux vaut ne rien faire que
    /// d'arracher au hasard).
    /// </summary>
    public static bool EstSorti(double x, double y,
        double gauche, double haut, double largeur, double hauteur,
        double marge = MargeDefaut)
    {
        if (largeur <= 0 || hauteur <= 0) return false;
        return x < gauche - marge
            || x > gauche + largeur + marge
            || y < haut - marge
            || y > haut + hauteur + marge;
    }
}
