using System.Text.Json;

namespace RigV3Desktop.Pont;

// ============================================================
// LES RACINES DES FENETRES : ce que chaque fenetre WPF fait monter a sa page
// React. Elles remplacent les composants racines de Blazor (retire le
// 19/11/2026) :
//   Main.razor + Home.razor     -> RacinePlanTravail (fenetre principale,
//                                  bandes de bord comprises) ;
//   Home.razor (ModeFenetreVue) -> RacinePlanTravail (onglet sorti, FenetreVue) ;
//   DocumentDetacheVue.razor    -> RacineDocumentDetache (FenetreDocument).
// Meme cycle de vie que ces composants : ouverture (OnInitialized), demarrage
// apres le premier rendu (OnAfterRender), fermeture avec la fenetre (Dispose).
// Le transport est fait par Repartiteur ; le montage cote React par
// web/src/racine.tsx.
// ============================================================

/// <summary>La racine de la page d'une fenetre.</summary>
public abstract class RacineWeb : IAsyncDisposable
{
    /// <summary>Le composant React monte a la racine (nom de son fichier .tsx).</summary>
    public abstract string Composant { get; }

    /// <summary>Les proprietes du composant qui sont des GESTES : React les recoit
    /// en fonctions, qui reviennent ici par <see cref="Rappeler"/>.</summary>
    public virtual string[] Rappels => [];

    /// <summary>Vrai quand la page doit poser les bandes de redimensionnement.</summary>
    public virtual bool Bords => false;

    /// <summary>Les proprietes du composant, calculees a chaque envoi.</summary>
    public abstract IReadOnlyDictionary<string, object?> Proprietes();

    /// <summary>Un geste remonte par la page, avec sa valeur en JSON.</summary>
    public virtual Task Rappeler(string nom, JsonElement valeur)
        => throw new InvalidOperationException($"La racine « {Composant} » n'a pas de rappel « {nom} ».");

    /// <summary>Quelque chose a change SANS geste de la page : elle doit redessiner.</summary>
    public event Action? Change;

    protected void Changer() => Change?.Invoke();

    /// <summary>Rejoue une action sur le fil de la fenetre puis redessine la page
    /// (donne par le repartiteur ; sans lui, l'action s'applique tout de suite).</summary>
    protected Action<Action> Planifier { get; private set; } = action => action();

    internal void Brancher(Action<Action> planifier) => Planifier = planifier;

    public bool Demarree { get; private set; }
    public bool Fermee { get; private set; }

    public EtatRacine Etat() => new(Composant, Rappels, Bords, Proprietes());

    /// <summary>La page demande sa racine pour la premiere fois (OnInitialized).</summary>
    public virtual void Ouvrir() { }

    /// <summary>Apres le premier rendu de la page (OnAfterRender(premierRendu)).</summary>
    public void Demarrer()
    {
        if (Demarree || Fermee) return;
        Demarree = true;
        SurDemarrage();
    }

    protected virtual void SurDemarrage() { }

    /// <summary>La fenetre se ferme : minuteurs et sessions s'arretent avec elle.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Fermee) return;
        Fermee = true;
        await SurFermeture();
        GC.SuppressFinalize(this);
    }

    protected virtual ValueTask SurFermeture() => ValueTask.CompletedTask;
}

/// <summary>
/// L'application entiere (accueil et plan de travail) : la fenetre principale,
/// ou un onglet sorti dans sa propre fenetre (<paramref name="modeFenetreVue"/>).
/// Ex-Main.razor et Home.razor : la session du plan de travail, ses deux rappels
/// (geste, plan) et, dans la fenetre principale, les bandes de bord.
/// </summary>
public sealed class RacinePlanTravail(FenetreInterop? fenetre, DocumentPartage? partage,
                                      bool modeFenetreVue = false, string? vueInitiale = null) : RacineWeb
{
    /// <summary>La session du plan de travail (le @code de Home.razor, deplace au lot 2).</summary>
    public SessionPlanTravail Plan { get; } = new(fenetre, partage, modeFenetreVue, vueInitiale);

    /// <summary>Vrai dans la fenetre d'un onglet sorti (FenetreVue).</summary>
    public bool ModeFenetreVue => modeFenetreVue;

    /// <summary>L'ecran ouvert d'emblee dans la fenetre d'un onglet sorti.</summary>
    public string? VueInitiale => vueInitiale;

    public override string Composant => "Application";

    public override string[] Rappels => ["geste", "plan"];

    /// <summary>
    /// BANDES DE BORD de la fenetre principale (ex-Main.razor, 18/11/2026) : le
    /// WebView couvre toute la fenetre, sans marge (MainWindow.xaml), et avale
    /// donc le pointeur sur les bords. Des bandes invisibles de 5 px rendent le
    /// redimensionnement a Windows. Pas dans la fenetre d'un onglet sorti (elle
    /// a sa barre de titre native et ne doit pas redimensionner la principale) ;
    /// pas dans une fenetre agrandie, dont les bords ne se tirent pas.
    /// </summary>
    public override bool Bords => !modeFenetreVue && fenetre is not null && fenetre.Redimensionnable;

    public override IReadOnlyDictionary<string, object?> Proprietes()
        => new Dictionary<string, object?> { ["vue"] = Plan.Vue() };

    public override void Ouvrir()
    {
        if (!modeFenetreVue && fenetre is not null)
        {
            fenetre.SuivreEtat();
            fenetre.EtatChange += Changer;
        }
        // Un changement venu d'ailleurs (fenetre WPF refermee, autre fenetre) se
        // rejoue sur le fil de la page, puis la page redessine.
        Plan.Planifier = action => Planifier(action);
        Plan.Change += Changer;
        Plan.Initialiser();
    }

    protected override void SurDemarrage() => Plan.Demarrer();

    public override Task Rappeler(string nom, JsonElement valeur)
    {
        switch (nom)
        {
            case "geste": Plan.SurGesteAccueil(valeur.Deserialize<GesteIle>(Repartiteur.Json)!); break;
            case "plan": Plan.Jouer(valeur.Deserialize<GestePlan>(Repartiteur.Json)!); break;
            case "bord" when Bords:
                fenetre!.Redimensionner(Enum.Parse<BordFenetre>(valeur.GetString() ?? string.Empty));
                break;
            case "bord": break; // fenetre agrandie entre-temps : ses bords ne se tirent plus
            default: return base.Rappeler(nom, valeur);
        }
        return Task.CompletedTask;
    }

    protected override async ValueTask SurFermeture()
    {
        if (fenetre is not null) fenetre.EtatChange -= Changer;
        Plan.Change -= Changer;
        await Plan.DisposeAsync();
    }
}

/// <summary>
/// La fenetre detachee du document (ex-DocumentDetacheVue.razor) : l'apercu de
/// la piece de SON emplacement, redessine a chaque changement de celui-ci.
/// </summary>
public sealed class RacineDocumentDetache(DocumentPartage partage, int rang) : RacineWeb
{
    /// <summary>Rang de CETTE fenetre (1re detachee, 2e...) : la clef de son
    /// emplacement, qui permet d'externaliser PLUSIEURS documents a la fois.</summary>
    public int Rang => rang;

    private DocumentDetache? Emplacement;

    public override string Composant => "DocumentDetacheVue";

    public override IReadOnlyDictionary<string, object?> Proprietes()
        => new Dictionary<string, object?>
        {
            ["piece"] = Emplacement is null ? null : DocumentDetacheVue.Vue(Emplacement)
        };

    public override void Ouvrir()
    {
        Emplacement = partage.Emplacement(rang);
        Emplacement.Change += Changer;
    }

    protected override ValueTask SurFermeture()
    {
        if (Emplacement is not null) Emplacement.Change -= Changer;
        return ValueTask.CompletedTask;
    }
}
