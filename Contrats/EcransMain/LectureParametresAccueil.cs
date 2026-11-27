namespace RigV3.Contrats.EcransMain;

// « Mon accueil » (PROC_ACCUEIL_PARAM, web/src/ecrans/EcranParametresAccueil.tsx), écran fait main lu dans la base du greffe
// (plan « RIG v3 100 % fonctionnel », lot 20, 27/11/2026 ; marche du lot 17). Le lecteur :
// Serveur/Socle/EcransMain/LectureParametresAccueil.cs.

/// <summary>L'adresse de la lecture : GET, les réglages d'accueil RIG v2 de L'AGENT appelant (<see cref="ParametresAccueilLus"/>).</summary>
public static class RoutesParametresAccueil
{
    public const string Parametres = "/ecrans-main/{greffe}/parametres-accueil";
}

/// <summary>
/// Les réglages d'accueil de l'agent dans RIG v2 (PARAMETRE_UTILISATEUR, clés ConsoleAccueil_* de
/// AmiConstantes.Utilisateur.ParametreConsoleAccueil) : ceux qu'il a enregistrés, par nom de clé. Les deux profils de RIG v2 qui
/// règlent ce que l'écran montre : administrateur (les animations) et Amitel (la molette et la date libre).
/// <paramref name="AgentV2"/> : faux si l'agent n'a pas de compte RIG v2 (soc.Compte.IdV2 vide) -- rien à lire.
/// </summary>
public sealed record ParametresAccueilLus(IReadOnlyDictionary<string, string> Valeurs, bool EstAdministrateur, bool EstAmitel, bool AgentV2);
