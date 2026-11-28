using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

// LES SONDES DES ECRANS FAITS MAIN (tests/ParcoursCompletCheck, en tete de Program.cs).
//
// Le banc mesure seul les DONNEES d'un ecran fait main (les lectures de son pont et leur « Mode »). Son GESTE, non : chaque
// ecran fait main a son propre pont (EcranReglages.ModifierAsync, EcranComptes.Creer...). Une sonde le joue PAR SON PONT,
// contre le serveur en memoire du banc, puis le RELIT par le meme pont.
//
// AJOUTER UNE SONDE (un lot qui rend un ecran fait main vert) : un fichier neuf dans ce dossier, <Ecran>.cs, une classe
// qui implemente ISondeEcran -- trouvee par reflexion, aucune ligne de Program.cs a toucher, aucun conflit entre lots.
//   - Code   : le code de la procedure au menu (PROC_REGLAGES_GREFFE) ;
//   - Jeu    : facultatif, le SQL de ses donnees, joue sur la base du greffe du banc AVANT le parcours (parametre
//              @aujourdhui). Identifiants a partir de 100000 x le rang du lot (regle des jeux), tables v2.* creees si
//              absentes, comme un jeu de tests/EcransGeneresBaseCheck/Jeux ;
//   - GesteAsync : joue le geste par le pont, le relit, et rend la preuve. Fait = le pont a dit fait ; Relu = la relecture par
//              le pont montre ce qui a ete ecrit. Un refus, une demonstration ou une relecture fausse : Detail dit pourquoi.
// Exemple : Sondes/ReglagesGreffe.cs.

/// <summary>Le geste d'un ecran fait main, joue par son pont puis relu.</summary>
public interface ISondeEcran
{
    /// <summary>Le code de la procedure au menu (Pont/MenuAccueil.cs).</summary>
    string Code { get; }

    /// <summary>Le SQL des donnees de la sonde, joue sur la base du greffe avant le parcours ; null sans donnees propres.</summary>
    string? Jeu => null;

    /// <summary>Joue le geste par le pont de l'ecran, le relit, et dit ce qui a ete prouve.</summary>
    Task<PreuveSonde> GesteAsync(ContexteSonde c);

    /// <summary>Facultatif : les DONNEES de l'ecran, quand le banc ne sait pas les mesurer seul (un pont qui lit par une session,
    /// des criteres que la valeur vide ne remplit pas). Fait = les lignes viennent de la base, Detail dit lesquelles ; null : le
    /// banc appelle lui-meme les lectures du pont.</summary>
    Task<PreuveSonde>? DonneesAsync(ContexteSonde c) => null;
}

/// <summary>Ce qu'une sonde a prouve.</summary>
/// <param name="Fait">Le pont a rendu le geste fait (pas un refus, pas une demonstration).</param>
/// <param name="Relu">La relecture par le pont montre ce que le geste a ecrit.</param>
/// <param name="Detail">Le geste et sa relecture, ou la cause d'un echec, en une phrase.</param>
public sealed record PreuveSonde(bool Fait, bool Relu, string Detail);

/// <summary>Ce qu'une sonde recoit du banc.</summary>
/// <param name="Client">Le client du pont, au nom de l'agent du banc, branche sur le serveur en memoire.</param>
/// <param name="Greffe">Le code du greffe des jeux.</param>
/// <param name="Jour">Le jour du banc (@aujourdhui des jeux).</param>
/// <param name="Valeur">Un SELECT d'une valeur sur la base du greffe, rendu en texte.</param>
/// <param name="Sql">Du SQL joue sur la base du greffe (parametre @aujourdhui).</param>
/// <param name="Journal">Le nombre de lignes du journal des gestes (soc.Journal).</param>
/// <param name="ClientDe">Le client du pont au nom d'un autre compte Windows (cree par le Jeu de la sonde, admis dans le greffe).</param>
public sealed record ContexteSonde(ClientServeur Client, string Greffe, DateTime Jour, Func<string, Task<string>> Valeur,
                                   Func<string, Task> Sql, Func<Task<string>> Journal, Func<string, ClientServeur> ClientDe);
