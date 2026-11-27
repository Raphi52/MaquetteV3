namespace RigV3.Contrats.EcransMain;

// La recherche des annonces BODACC de la palette Ctrl K et de la page Recherche (web/src/ecrans/annoncesBodacc.ts), lue dans
// la base du greffe (plan « RIG v3 100 % fonctionnel », lot 20, 27/11/2026 ; marche du lot 17). Le lecteur :
// Serveur/Socle/EcransMain/LectureBodacc.cs (ChercherAsync) -- la requete de l'ecran BODACC, sans sa periode.

/// <summary>L'adresse de la recherche : POST <see cref="RechercheBodacc"/>, rend <see cref="AnnoncesBodaccTrouvees"/>.</summary>
public static class RoutesBodaccRecherche
{
    public const string Recherche = "/ecrans-main/{greffe}/bodacc/recherche";

    /// <summary>Au plus autant d'annonces : la palette en montre quelques-unes, pas une liste de travail.</summary>
    public const int Plafond = 50;
}

/// <summary>Ce que l'agent a tape : chaque mot (3 caracteres au moins en tout) doit se retrouver dans l'annonce.</summary>
public sealed record RechercheBodacc(string Texte);

/// <summary>Les annonces trouvees (au plus <see cref="RoutesBodaccRecherche.Plafond"/>), et leur total en base.</summary>
public sealed record AnnoncesBodaccTrouvees(AnnonceBodaccLue[] Lignes, int Total);
