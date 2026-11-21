// fix-ok: CS0246 HttpRequestException -- un projet WPF n'importe pas System.Net.Http d'office.
// fix-ok: lot 10 -- OuvrirAsync lisait RoutesIdentite.Identite (sans greffe, ancienne ligne 51), que le serveur juge dans la
// base commune depuis le lot 8 : tout agent de greffe y serait inconnu. Le greffe est maintenant choisi avant l'identite.
using System.Net.Http;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

// ============================================================
// L'AGENT DE CETTE SESSION ET SON GREFFE (SOC-001, tache T03 cote ecran, 2026-11-20 ;
// choix du greffe a l'ouverture, lot 10 du plan de reconstruction, 2026-11-21).
//
// A l'ouverture de la fenetre principale (MainWindow.Loaded), AVANT toute donnee :
//   - sans RIGV3_SERVEUR : rien ne change, l'accueil reste sur ses comptes de
//     demonstration (Metier/JeuDemo.cs) et son selecteur de login ;
//   - branchee au serveur : RIG choisit d'abord le GREFFE, comme la V2
//     (RigFormLogin.Login, D:\RigApplication\Source\Outils\DLL\RigFormsMetier\RigFormLogin.cs:282-319) :
//       1. un greffe passe au lancement (« /g3801 », « greffe=3801 », « CodeGreffe=3801 »)
//          s'ouvre directement ; il est refuse, avec les messages de la V2, si sa base
//          n'existe pas ou si ce poste ne le declare pas ;
//       2. sinon, les greffes DECLARES POUR CE POSTE (variable RIGV3_GREFFES, l'equivalent
//          V3 des cles de registre HKLM de la V2), que le serveur filtre comme la V2 (compte
//          present et actif dans la base du greffe) : un seul -> choisi d'office, plusieurs
//          -> l'ecran de choix (Pont/RacineChoixGreffe.cs), aucun -> l'ecran de refus ;
//     puis il demande au serveur qui est l'agent DANS LA BASE DE CE GREFFE
//     (GET /socle/identite/{greffe}). Sa session Windows part avec chaque demande
//     (ClientServeur, credentials de la session) : aucune saisie. Admis -> l'accueil, dont
//     l'entete nomme l'agent et le greffe ; refuse (ou serveur injoignable) -> l'ecran de
//     refus (Pont/EcranRefus.cs) remplace l'application entiere : aucune donnee (CA03).
//   Le greffe ouvert est celui de toute la session : un ecran qui appelle une route de
//   greffe le prend ici (CodeGreffe).
//
// Ce fichier ne depend que de ClientServeur et de Contrats/ : les bancs
// tests/SocleIdentiteCheck et tests/OuvertureGreffeCheck le compilent seul, contre le vrai serveur.
// ============================================================

/// <summary>Le greffe ouvert par la session : son code au catalogue et son nom.</summary>
public sealed record GreffeOuvert(string Code, string? Nom)
{
    /// <summary>Ce que l'entete de l'accueil affiche : « Greffe de Grenoble (3801) ».</summary>
    public string Libelle => string.IsNullOrWhiteSpace(Nom) ? $"Greffe {Code}" : $"{Nom.Trim()} ({Code})";
}

/// <summary>
/// Ce que l'ouverture decide, avant toute donnee. Trois cas exclusifs : <see cref="Demonstration"/> (aucun
/// serveur), <see cref="Identite"/> posee (greffe ouvert ou refus), ou <see cref="AChoisir"/> (plusieurs
/// greffes : l'agent choisit, puis <see cref="SessionAgent.OuvrirAsync"/>).
/// </summary>
/// <param name="Identite">L'agent juge (admis ou refuse) ; null en demonstration ou tant qu'un choix est a faire.</param>
/// <param name="Compte">Le compte Windows vu par le serveur, a montrer sur l'ecran de choix.</param>
/// <param name="AChoisir">Les greffes du poste que l'agent peut ouvrir, quand il y en a plusieurs.</param>
/// <param name="NonProposes">Les autres greffes du poste, avec leur raison (l'ecran de choix les nomme).</param>
public sealed record OuvertureSession(IdentiteAgent? Identite, string Compte, GreffeDuPoste[] AChoisir, GreffeDuPoste[] NonProposes)
{
    public bool Demonstration => Identite is null && AChoisir.Length == 0;
}

public static class SessionAgent
{
    /// <summary>Statut pose par l'ecran quand le serveur n'a pas pu repondre (il n'est pas
    /// un refus du serveur : <see cref="StatutsIdentite"/> n'en a pas).</summary>
    public const string StatutServeurInjoignable = "serveur-injoignable";

    /// <summary>Refus de la V2 : la base du greffe passe au lancement n'existe pas (RigFormLogin.cs:307-310,
    /// <c>RigGreffe.IsExisteBD</c> : absent du catalogue, ou base qui ne s'ouvre pas).</summary>
    public const string StatutGreffeInexistant = "greffe-inexistant";

    /// <summary>Refus de la V2 : ce poste ne declare pas le greffe passe au lancement (RigFormLogin.cs:311-316,
    /// cle de registre absente).</summary>
    public const string StatutGreffeHorsPoste = "greffe-hors-poste";

    /// <summary>Aucun greffe du poste n'est ouvrable pour l'agent (la V2 montre alors une liste vide).</summary>
    public const string StatutAucunGreffe = "aucun-greffe";

    /// <summary>
    /// Les greffes declares pour ce poste, l'equivalent V3 des cles de registre
    /// <c>HKLM\…\RIG\&lt;code&gt;</c> de la V2 : leurs codes, separes par « ; », « , » ou des espaces
    /// (« 3801;8002 »). Posee au niveau de la machine par le deploiement RDS, comme <see cref="ClientServeur.Variable"/>.
    /// La V2 y gardait aussi le serveur et la base : en V3 ils sont au catalogue commun du serveur.
    /// </summary>
    public const string VariableGreffesDuPoste = "RIGV3_GREFFES";

    private const string Hotline = "Merci de contacter la hotline d'Amitel pour signaler le problème.";

    /// <summary>L'agent de cette session, tel que le serveur l'a juge dans la base du greffe ; null =
    /// demonstration (aucun serveur), ou pas encore ouverte.</summary>
    public static IdentiteAgent? Identite { get; private set; }

    /// <summary>Le compte Windows tel que le serveur l'a recu (sans domaine ; en developpement, celui de
    /// RIGV3_COMPTE) ; la session Windows tant que le serveur n'a pas repondu.</summary>
    private static string CompteVu = Environment.UserName;

    /// <summary>Le greffe ouvert par la session ; null en demonstration, avant le choix, ou si l'agent est refuse.</summary>
    public static GreffeOuvert? Greffe { get; private set; }

    /// <summary>Le code du greffe ouvert : celui que portent les adresses des routes de greffe ; null hors serveur.</summary>
    public static string? CodeGreffe => Greffe?.Code;

    /// <summary>Le nom que l'entete de l'accueil affiche (« Prenom Nom ») ; null hors serveur.</summary>
    public static string? NomAgent => Identite is { Admis: true, Agent: { } agent } ? agent.NomAffiche : null;

    /// <summary>
    /// Ouvre la session. <paramref name="serveur"/> rend le client (<see cref="ClientServeur.Actif"/>) ;
    /// null = demonstration, rien n'est demande. <paramref name="arguments"/> : la ligne de commande
    /// (<see cref="GreffeDeLancement"/>) ; <paramref name="greffesDuPoste"/> : la valeur de
    /// <see cref="VariableGreffesDuPoste"/>. Une adresse illisible ou un serveur qui ne repond pas n'est PAS
    /// un retour discret a la demonstration : c'est un refus d'ouvrir, qui dit la cause.
    /// </summary>
    public static async Task<OuvertureSession> PreparerAsync(Func<ClientServeur?> serveur, IReadOnlyList<string> arguments,
                                                             string? greffesDuPoste, CancellationToken ct = default)
    {
        Identite = null;
        Greffe = null;
        CompteVu = Environment.UserName;
        ClientServeur? client = null;
        try
        {
            client = serveur();
            if (client is null) return new OuvertureSession(null, Environment.UserName, [], []);
            var poste = CodesDuPoste(greffesDuPoste);

            if (GreffeDeLancement(arguments) is { } code)
            {
                // L'ordre de la V2 : la base existe-t-elle (:307-310), puis ce poste la declare-t-il (:311-316) ?
                var choix = await client.LireAsync<ChoixDuGreffe>(RoutesOuverture.Pour([code]), ct);
                CompteVu = choix.Compte;
                // Un « code » qui en porte plusieurs (« 3801,8002 ») n'est le code d'aucun greffe.
                var greffe = choix.Greffes is [var seul] ? seul : new GreffeDuPoste(code, null, EtatsGreffeDuPoste.HorsCatalogue);
                if (greffe.Etat is EtatsGreffeDuPoste.HorsCatalogue or EtatsGreffeDuPoste.BaseInjoignable)
                    return Decide(Refus(StatutGreffeInexistant, $"la base de données du greffe '{code}' n'existe pas."));
                if (!poste.Contains(greffe.Code, StringComparer.OrdinalIgnoreCase))
                    return Decide(Refus(StatutGreffeHorsPoste,
                        $"Impossible de se connecter à la base de données du greffe '{code}' depuis ce serveur."));
                return Decide(await IdentifierAsync(client, greffe, ct));
            }

            if (poste.Count == 0)
                return Decide(Refus(StatutAucunGreffe,
                    $"Aucun greffe n'est déclaré pour ce poste : la variable {VariableGreffesDuPoste} donne leurs codes (ex. 3801;8002)."));
            var duPoste = await client.LireAsync<ChoixDuGreffe>(RoutesOuverture.Pour(poste), ct);
            CompteVu = duPoste.Compte;
            var ouvrables = duPoste.Greffes.Where(g => g.Ouvrable).ToArray();
            var autres = duPoste.Greffes.Where(g => !g.Ouvrable).ToArray();
            return ouvrables.Length switch
            {
                0 => Decide(Refus(StatutAucunGreffe,
                    $"Aucun des greffes de ce poste ne vous est ouvert : {string.Join(", ", autres.Select(g => $"{g.Code} ({Raison(g)})"))}.")),
                // V2 (:286-291) : le seul greffe present est choisi sans montrer la fenetre.
                1 => Decide(await IdentifierAsync(client, ouvrables[0], ct)),
                _ => new OuvertureSession(null, duPoste.Compte, ouvrables, autres),
            };
        }
        catch (Exception e) when (EstPanne(e))
        {
            return Decide(Panne(client, e));
        }
    }

    /// <summary>
    /// Ouvre le greffe <paramref name="greffe"/> choisi sur l'ecran de choix : l'agent y est juge par le serveur,
    /// dans la base de ce greffe. Rend l'identite (admise ou refusee) ; la session garde le greffe si l'agent est admis.
    /// </summary>
    public static async Task<IdentiteAgent> OuvrirAsync(ClientServeur client, GreffeDuPoste greffe, CancellationToken ct = default)
    {
        Identite = null;
        Greffe = null;
        IdentiteAgent identite;
        try { identite = await IdentifierAsync(client, greffe, ct); }
        catch (Exception e) when (EstPanne(e)) { identite = Panne(client, e); }
        return Decide(identite).Identite!;
    }

    /// <summary>
    /// Le greffe passe au lancement, lu comme la V2 : arguments recolles par des espaces puis coupes sur « ; »
    /// (ListeParametre.ConcatArgs et SetParametre, AmiSystem\ListeParametre.cs:153-262), chaque morceau
    /// « cle=valeur » (cle sans majuscules) ou « /g3801 » / « /g 3801 » ; « greffe » d'abord, sinon
    /// « CodeGreffe » (RigFormLogin.cs:284-286). null sans greffe.
    /// </summary>
    public static string? GreffeDeLancement(IReadOnlyList<string> arguments)
    {
        string greffe = "", codeGreffe = "";
        foreach (var morceau in string.Join(' ', arguments).Split(';'))
        {
            var p = morceau.Trim();
            if (p.Length == 0) continue;
            var egal = p.IndexOf('=');
            if (egal >= 0)
            {
                var cle = p[..egal];
                if (cle.Equals("greffe", StringComparison.OrdinalIgnoreCase)) greffe = p[(egal + 1)..];
                else if (cle.Equals("CodeGreffe", StringComparison.OrdinalIgnoreCase)) codeGreffe = p[(egal + 1)..];
                continue;
            }
            var mots = p.Split(' ');
            if (mots.Length == 1 && p.Length > 2) mots = [p[..2], p[2..]];
            if (mots[0] == "/g" && mots.Length > 1) greffe = mots[1].Trim();
        }
        var code = greffe.Trim().Length > 0 ? greffe.Trim() : codeGreffe.Trim();
        return code.Length > 0 ? code : null;
    }

    /// <summary>Les codes de <see cref="VariableGreffesDuPoste"/>, dans leur ordre, sans doublon ni vide.</summary>
    public static IReadOnlyList<string> CodesDuPoste(string? declaration)
        => [.. (declaration ?? "").Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                  .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Pourquoi un greffe du poste n'est pas propose, en clair.</summary>
    public static string Raison(GreffeDuPoste greffe) => greffe.Etat switch
    {
        EtatsGreffeDuPoste.HorsCatalogue => "absent du catalogue des greffes",
        EtatsGreffeDuPoste.BaseInjoignable => "sa base ne répond pas",
        EtatsGreffeDuPoste.CompteAbsent => "votre compte n'y est pas défini",
        EtatsGreffeDuPoste.CompteDesactive => "votre compte y est désactivé",
        _ => greffe.Etat,
    };

    /// <summary>L'identite de l'agent dans la base du greffe ; un 404 / 503 dit que cette base n'existe pas (V2).</summary>
    private static async Task<IdentiteAgent> IdentifierAsync(ClientServeur client, GreffeDuPoste greffe, CancellationToken ct)
    {
        IdentiteAgent identite;
        try { identite = await client.LireAsync<IdentiteAgent>(RoutesIdentite.Pour(greffe.Code), ct); }
        catch (ErreurServeur e) when (e.CodeHttp is 404 or 503)
        {
            return Refus(StatutGreffeInexistant, $"la base de données du greffe '{greffe.Code}' n'existe pas.");
        }
        if (identite.Admis) Greffe = new GreffeOuvert(greffe.Code, greffe.Nom);
        return identite;
    }

    private static OuvertureSession Decide(IdentiteAgent identite)
    {
        Identite = identite;
        if (!identite.Admis) Greffe = null;
        return new OuvertureSession(identite, identite.Compte, [], []);
    }

    /// <summary>Un refus decide par l'ecran (pas par les quatre controles du serveur), au nom du compte vu par le serveur.</summary>
    private static IdentiteAgent Refus(string statut, string message)
        => new(statut, CompteVu, null, message, Hotline);

    private static bool EstPanne(Exception e)
        => e is ErreurServeur or HttpRequestException or InvalidOperationException or TaskCanceledException;

    private static IdentiteAgent Panne(ClientServeur? client, Exception e)
    {
        var ou = client is null ? $"variable {ClientServeur.Variable}" : client.Adresse.AbsoluteUri;
        return new IdentiteAgent(StatutServeurInjoignable, Environment.UserName, null,
            e is ErreurServeur { CodeHttp: 401 }
                ? $"Le serveur de RIG ({ou}) n'a pas reconnu la session Windows '{Environment.UserName}'."
                : $"RIG n'a pas pu joindre son serveur ({ou}) : {e.Message}",
            Hotline);
    }
}
