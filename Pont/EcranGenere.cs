using System.Net.Http;   // HttpRequestException : un projet WPF n'importe pas System.Net.Http d'office.
using System.Text.Json;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.Socle;
using RigV3Desktop.Pont.EcransGeneres;

namespace RigV3Desktop.Pont;

/// <summary>Les lignes que l'ecran genere affiche.</summary>
/// <param name="Mode">« base » : toutes les lignes viennent de la base du greffe ; « mixte » : un ecran reuni dont
/// certaines procedures n'ont pas encore de lecteur (leurs groupes restent en exemple) ; « exemple » : aucune ligne de
/// la base (sans serveur, sans greffe, ou aucun lecteur) ; « erreur » : le serveur n'a pas rendu les lignes, les
/// exemples restent, avec le motif.</param>
/// <param name="Message">Ce que l'ecran dit au-dessus de la liste (motif, plafond atteint) ; null sinon.</param>
/// <param name="Greffe">Le greffe lu ; null sans serveur ou sans greffe.</param>
/// <param name="Lignes">Les lignes, dans l'ordre d'affichage.</param>
/// <param name="Exemples">Les groupes dont les lignes sont des EXEMPLES du descripteur (noms fictifs) : l'ecran les
/// marque « exemple ».</param>
public sealed record VueLignesEcran(string Mode, string? Message, string? Greffe, LigneEcran[] Lignes, string[] Exemples);

/// <summary>
/// Pont des lignes d'un ecran genere (web/src/ecrans/EcranGenere.tsx). Le descripteur (Pont/EcransGeneres/) donne la
/// mise en page et des lignes d'exemple ; les vraies lignes viennent du serveur, procedure par procedure
/// (RoutesEcransGeneres : un lecteur par procedure, Serveur/Socle/EcransGeneres/LecteurEcran.cs). Rien a ajouter ici
/// pour brancher un ecran de plus : une procedure sans lecteur garde ses exemples, marques comme tels.
/// Le greffe : celui de la session (<see cref="SessionAgent.CodeGreffe"/>). Modele : Pont/EcranManquants.cs.
/// </summary>
public static class EcranGenere
{
    [JSInvokable("EcranGenere.Lignes")]
    public static Task<VueLignesEcran> Lignes(string code) => LignesAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, code);

    /// <summary>Les lignes de l'ecran <paramref name="code"/> lues sur <paramref name="serveur"/> pour le greffe
    /// <paramref name="greffe"/> (serveur null : la demonstration, les exemples).</summary>
    public static async Task<VueLignesEcran> LignesAsync(ClientServeur? serveur, string? greffe, string code)
    {
        var d = CatalogueGenere.Descripteur(code)
                ?? throw new ArgumentException($"{code} n'a pas de descripteur d'ecran genere (Pont/EcransGeneres/).", nameof(code));
        if (serveur is null) return Exemples(d, "exemple", null, null);
        if (string.IsNullOrWhiteSpace(greffe))
            return Exemples(d, "exemple", null, "Aucun greffe n'est ouvert dans cette session : les lignes se lisent dans la base du greffe choisi à l'ouverture de RIG.");

        // Un ecran reuni lit chacune de ses procedures ; un ecran simple, la sienne.
        var reuni = d.Membres.Length > 0;
        var procedures = reuni ? d.Membres : [d];
        LignesEcran[] lues;
        try
        {
            lues = await Task.WhenAll(procedures.Select(p => serveur.LireAsync<LignesEcran>(RoutesEcransGeneres.Pour(greffe, p.Code))));
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return Exemples(d, "erreur", greffe, Motif(e, serveur, greffe));
        }

        var lignes = new List<LigneEcran>();
        var exemples = new List<string>();
        var plafonds = new List<string>();
        foreach (var (p, l) in procedures.Zip(lues))
        {
            LigneEcran[] siennes = l.Branchee
                ? [.. l.Lignes.Select(x => new LigneEcran(x.Nom, x.Precisions, x.Groupe, x.Etat, x.Faites))]
                : p.Lignes;
            if (!l.Branchee) exemples.AddRange(reuni ? [p.Titre] : p.Groupes);
            else if (l.Total > l.Lignes.Length)
                plafonds.Add($"{(reuni ? p.Titre + " : l" : "L")}es {l.Lignes.Length} premières lignes sur {l.Total}");
            lignes.AddRange(reuni ? siennes.Select(x => CatalogueGenere.LigneReunie(p, x)) : siennes);
        }
        var mode = exemples.Count == 0 ? "base" : lues.Any(l => l.Branchee) ? "mixte" : "exemple";
        return new VueLignesEcran(mode, plafonds.Count == 0 ? null : string.Join(" ; ", plafonds) + ".", greffe,
                                  [.. lignes], [.. exemples]);
    }

    /// <summary>Les lignes d'exemple du descripteur, tous ses groupes marques « exemple ».</summary>
    private static VueLignesEcran Exemples(DescripteurEcran d, string mode, string? greffe, string? message)
        => new(mode, message, greffe, d.Lignes, d.Groupes);

    /// <summary>Pourquoi le serveur n'a pas rendu les lignes, en mots d'agent.</summary>
    private static string Motif(Exception e, ClientServeur serveur, string greffe)
    {
        if (e is not ErreurServeur s)
            return $"Le serveur de RIG ne répond pas ({serveur.Adresse}, {e.GetType().Name}).";
        return s.CodeHttp switch
        {
            401 or 403 => $"Vous n'avez pas accès aux données du greffe {greffe}.",
            // 404 : le greffe absent du catalogue (le corps le dit : RefusGreffe), ou un serveur d'avant cette route.
            404 => MotifDuRefus(s) ?? "Le serveur de RIG ne sait pas encore lire ces lignes : il date d'avant cette fonction, il faut le relancer.",
            503 => MotifDuRefus(s) ?? $"La base du greffe {greffe} ne répond pas. Réessayez dans un instant ou appelez l'assistance d'Amitel.",
            _ => $"Le serveur de RIG n'a pas rendu les lignes ({s.CodeHttp}).",
        };
    }

    /// <summary>Le motif d'un refus de greffe (404 greffe inconnu, 503 base muette) ; null si le corps n'en porte pas.</summary>
    private static string? MotifDuRefus(ErreurServeur e)
    {
        if (string.IsNullOrWhiteSpace(e.Corps)) return null;
        try { return JsonSerializer.Deserialize<RefusGreffe>(e.Corps, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Motif; }
        catch (JsonException) { return null; }
    }
}
