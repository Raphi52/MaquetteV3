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
/// <param name="Vides">Les groupes BRANCHES sur la base mais sans aucune ligne (ecran reuni : le titre de la procedure) :
/// l'ecran les garde, avec « Aucune ligne dans la base du greffe », au lieu de les laisser disparaitre.</param>
public sealed record VueLignesEcran(string Mode, string? Message, string? Greffe, LigneEcran[] Lignes, string[] Exemples, string[] Vides)
{
    /// <summary>Les groupes dont les lignes ont ete CHERCHEES dans la base (la recherche demandee y est appliquee) :
    /// l'ecran ne les refiltre pas. Vide sans recherche, ou quand aucun lecteur ne cherche.</summary>
    public string[] Cherchees { get; init; } = [];

    /// <summary>Vrai : au moins une procedure de l'ecran cherche dans la base ; l'ecran y envoie sa recherche
    /// (EcranGenere.Chercher). Faux : l'ecran filtre les lignes qu'il a.</summary>
    public bool Recherche { get; init; }

    /// <summary>Les groupes dont les lignes s'arretent au plafond du serveur (la base en a plus : Message dit combien). Un
    /// geste sur « les lignes affichees » n'y porte pas sur tout le groupe : FormeEtat refuse d'imprimer une liste coupee
    /// (RIG v2 imprime le registre entier, OPE_RESULTATS.cs:65-73 ; lot 6 du plan « RIG v3 100 % fonctionnel »). Quand le
    /// lecteur plafonne chaque groupe (LignesEcran.TotalParGroupe), seuls les groupes coupes y sont.</summary>
    public string[] Plafonnees { get; init; } = [];
}

/// <summary>La fiche d'une ligne, telle que l'ecran l'ouvre.</summary>
/// <param name="Mode">« base » : lue dans la base du greffe (<paramref name="Fiche"/>) ; « exemple » : rien a lire (ligne
/// d'exemple, sans serveur, ou procedure sans lecteur de fiche), l'ecran garde ses champs vides ; « erreur » : le serveur
/// n'a pas rendu la fiche, avec le motif.</param>
/// <param name="Message">Pourquoi la fiche n'est pas lue ; null si elle l'est.</param>
/// <param name="Fiche">La fiche lue ; null hors du mode « base ».</param>
public sealed record VueFicheEcran(string Mode, string? Message, FicheEcran? Fiche);

/// <summary>
/// Pont des ecrans generes (web/src/ecrans/EcranGenere.tsx et ses formes). Le descripteur (Pont/EcransGeneres/) donne la
/// mise en page et des lignes d'exemple ; les vraies lignes, la fiche d'une ligne et les gestes viennent du serveur,
/// procedure par procedure (RoutesEcransGeneres : lecteurs, Serveur/Socle/EcransGeneres/LecteurEcran.cs ; ecrivains,
/// EcrivainEcran.cs). Rien a ajouter ici pour brancher un ecran de plus : une procedure sans lecteur garde ses exemples,
/// marques comme tels ; sans ecrivain, un geste dit « Demonstration : rien n'est enregistre ».
/// Le greffe : celui de la session (<see cref="SessionAgent.CodeGreffe"/>). Modele : Pont/EcranManquants.cs.
/// </summary>
public static class EcranGenere
{
    /// <summary>La phrase de tout geste qui n'ecrit rien faute d'ecrivain ou de serveur (web/src/ecrans/formes/commun.tsx,
    /// DEMONSTRATION).</summary>
    public const string Demonstration = "Démonstration : rien n’est enregistré";

    [JSInvokable("EcranGenere.Lignes")]
    public static Task<VueLignesEcran> Lignes(string code) => LignesAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, code);

    /// <summary>Les lignes cherchees : le texte du champ de recherche et les criteres remplis (au libelle du descripteur).</summary>
    [JSInvokable("EcranGenere.Chercher")]
    public static Task<VueLignesEcran> Chercher(string code, string? texte, Dictionary<string, string>? criteres)
        => LignesAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, code, new RechercheEcran(texte, criteres ?? []));

    /// <summary>La fiche de la ligne <paramref name="id"/> du groupe <paramref name="groupe"/> (sa procedure dans un ecran reuni).</summary>
    [JSInvokable("EcranGenere.Fiche")]
    public static Task<VueFicheEcran> Fiche(string code, string groupe, string? id)
        => FicheAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, code, groupe, id);

    /// <summary>Un geste de l'ecran sur les lignes <paramref name="ids"/> du groupe <paramref name="groupe"/>.</summary>
    [JSInvokable("EcranGenere.Geste")]
    public static Task<ResultatGeste> Geste(string code, string groupe, TypeGeste type, string? nom, string[] ids, Dictionary<string, string> valeurs)
        => GesteAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, code, groupe, new GesteEcran(type, ids, valeurs) { Nom = nom });

    /// <summary>Les lignes de l'ecran <paramref name="code"/> lues sur <paramref name="serveur"/> pour le greffe
    /// <paramref name="greffe"/> (serveur null : la demonstration, les exemples), cherchees par <paramref name="recherche"/>.</summary>
    public static async Task<VueLignesEcran> LignesAsync(ClientServeur? serveur, string? greffe, string code, RechercheEcran? recherche = null)
    {
        var d = Descripteur(code);
        if (serveur is null) return Exemples(d, "exemple", null, null);
        if (string.IsNullOrWhiteSpace(greffe))
            return Exemples(d, "exemple", null, "Aucun greffe n'est ouvert dans cette session : les lignes se lisent dans la base du greffe choisi à l'ouverture de RIG.");

        // Un ecran reuni lit chacune de ses procedures ; un ecran simple, la sienne.
        var reuni = d.Membres.Length > 0;
        var procedures = reuni ? d.Membres : [d];
        LignesEcran[] lues;
        try
        {
            lues = await Task.WhenAll(procedures.Select(p => serveur.LireAsync<LignesEcran>(RoutesEcransGeneres.Pour(greffe, p.Code, recherche))));
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return Exemples(d, "erreur", greffe, Motif(e, serveur, greffe));
        }

        var lignes = new List<LigneEcran>();
        var exemples = new List<string>();
        var vides = new List<string>();
        var cherchees = new List<string>();
        var plafonds = new List<string>();
        var plafonnees = new List<string>();
        foreach (var (p, l) in procedures.Zip(lues))
        {
            LigneEcran[] siennes = l.Branchee
                ? [.. l.Lignes.Select(x => new LigneEcran(x.Nom, x.Precisions, x.Groupe, x.Etat, x.Faites) { Id = x.Id, Valeurs = x.Valeurs })]
                : p.Lignes;
            if (!l.Branchee) exemples.AddRange(reuni ? [p.Titre] : p.Groupes);
            else if (l.Total == 0 && siennes.Length == 0) vides.Add(p.Titre);
            else if (!reuni && l.TotalParGroupe is { } parGroupe)
            {
                // Un plafond par groupe (des registres distincts) : seul le groupe dont la base a plus de lignes est coupe.
                foreach (var g in p.Groupes)
                {
                    var lus = l.Lignes.Count(x => x.Groupe == g);
                    if (parGroupe.GetValueOrDefault(g) <= lus) continue;
                    plafonds.Add($"{g} : les {lus} premières lignes sur {parGroupe[g]}");
                    plafonnees.Add(g);
                }
            }
            else if (l.Total > l.Lignes.Length)
            {
                plafonds.Add($"{(reuni ? p.Titre + " : l" : "L")}es {l.Lignes.Length} premières lignes sur {l.Total}");
                plafonnees.AddRange(reuni ? [p.Titre] : p.Groupes);
            }
            if (l.Cherchee) cherchees.AddRange(reuni ? [p.Titre] : p.Groupes);
            lignes.AddRange(reuni ? siennes.Select(x => CatalogueGenere.LigneReunie(p, x)) : siennes);
        }
        var mode = exemples.Count == 0 ? "base" : lues.Any(l => l.Branchee) ? "mixte" : "exemple";
        return new VueLignesEcran(mode, plafonds.Count == 0 ? null : string.Join(" ; ", plafonds) + ".", greffe,
                                  [.. lignes], [.. exemples], [.. vides])
               { Cherchees = [.. cherchees], Recherche = lues.Any(l => l.Chercheur), Plafonnees = [.. plafonnees] };
    }

    /// <summary>La fiche <paramref name="id"/> de la procedure du groupe <paramref name="groupe"/> de l'ecran <paramref name="code"/>.</summary>
    public static async Task<VueFicheEcran> FicheAsync(ClientServeur? serveur, string? greffe, string code, string groupe, string? id)
    {
        var p = Procedure(Descripteur(code), groupe);
        if (string.IsNullOrWhiteSpace(id)) return new("exemple", "Ligne d'exemple : aucune fiche dans la base du greffe.", null);
        if (serveur is null || string.IsNullOrWhiteSpace(greffe)) return new("exemple", "Sans serveur : la fiche ne se lit pas.", null);
        try
        {
            return new("base", null, await serveur.LireAsync<FicheEcran>(RoutesEcransGeneres.FichePour(greffe, p.Code, id)));
        }
        catch (ErreurServeur e) when (e.CodeHttp == 404)
        {
            return new("exemple", MotifDuRefus(e) ?? $"{p.Titre} ne lit pas encore de fiche dans la base du greffe.", null);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return new("erreur", Motif(e, serveur, greffe), null);
        }
    }

    /// <summary>
    /// Joue <paramref name="geste"/> pour la procedure du groupe <paramref name="groupe"/>. Sans serveur, sans greffe, sur
    /// une ligne d'exemple (sans identifiant) ou sans ecrivain : rien n'est ecrit, SansEcrivain (l'ecran dit
    /// « Demonstration »). Un controle de RIG v2 qui refuse (422) : son message. Jamais un faux succes.
    /// </summary>
    public static async Task<ResultatGeste> GesteAsync(ClientServeur? serveur, string? greffe, string code, string groupe, GesteEcran geste)
    {
        var p = Procedure(Descripteur(code), groupe);
        if (serveur is null || string.IsNullOrWhiteSpace(greffe))
            return new ResultatGeste(false, $"{Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        var creation = geste.Type is TypeGeste.Creer or TypeGeste.Ajouter;
        if (!creation && (geste.Ids.Length == 0 || geste.Ids.Any(string.IsNullOrWhiteSpace)))
            return new ResultatGeste(false, $"{Demonstration} : une ligne d'exemple n'existe pas dans la base du greffe.") { SansEcrivain = true };
        try
        {
            return await serveur.EnvoyerAsync<GesteEcran, ResultatGeste>(RoutesEcransGeneres.GestesPour(greffe, p.Code), geste);
        }
        catch (ErreurServeur e) when (e.CodeHttp == 422 && Lire<ResultatGeste>(e.Corps) is { } refus)
        {
            return refus;
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return new ResultatGeste(false, $"Rien n'est enregistré : {Motif(e, serveur, greffe)}");
        }
    }

    private static DescripteurEcran Descripteur(string code)
        => CatalogueGenere.Descripteur(code)
           ?? throw new ArgumentException($"{code} n'a pas de descripteur d'ecran genere (Pont/EcransGeneres/).", nameof(code));

    /// <summary>La procedure d'une ligne : celle de son groupe dans un ecran reuni, sinon l'ecran lui-meme.</summary>
    private static DescripteurEcran Procedure(DescripteurEcran d, string groupe)
        => d.Membres.FirstOrDefault(m => m.Titre == groupe) ?? d;

    /// <summary>Les lignes d'exemple du descripteur, tous ses groupes marques « exemple ».</summary>
    private static VueLignesEcran Exemples(DescripteurEcran d, string mode, string? greffe, string? message)
        => new(mode, message, greffe, d.Lignes, d.Groupes, []);

    /// <summary>Pourquoi le serveur n'a pas repondu, en mots d'agent.</summary>
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

    /// <summary>Le motif d'un refus (corps { motif : ... } : greffe inconnu, base muette, fiche absente) ; null sinon.</summary>
    private static string? MotifDuRefus(ErreurServeur e) => Lire<RefusGreffe>(e.Corps)?.Motif;

    private static T? Lire<T>(string? corps) where T : class
    {
        if (string.IsNullOrWhiteSpace(corps)) return null;
        try { return JsonSerializer.Deserialize<T>(corps, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { return null; }
    }
}
