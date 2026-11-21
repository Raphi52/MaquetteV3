using System.Globalization;
using System.Text.Json;

namespace RigV3Desktop.Pont;

// ============================================================
// L'ACCUEIL (barre du haut, menu de gauche, entete, recherche, chiffres du
// jour, grille de tuiles) : son ETAT et ses REGLES, deplaces TELS QUELS du
// @code de Components/Pages/Home.razor (lot 1 du plan « finir le passage a
// React », 19/11/2026). Le rendu est fait par React :
//   web/src/composants/BarreRig.tsx  (header.rig-barre)
//   web/src/composants/Accueil.tsx   (div.rig-accueil)
//   web/src/composants/ThemeRig.tsx  (data-rig-theme sur <html>)
//
// Pourquoi une session tenue par Home.razor (et pas ouverte par React) :
// l'etat de l'accueil est PARTAGE avec le plan de travail, encore en Blazor
// jusqu'au lot 2 -- AccueilEtat (accueil.json), le login (il decide des
// branches visibles de l'arbre), le theme (pose sur la racine de la page).
// Home garde donc UNE SessionAccueil, lui passe a chaque rendu ce que seul le
// plan de travail sait (ContexteAccueil) et monte les iles avec la vue
// calculee ici. React remonte chaque geste par UN rappel ; SessionAccueil.Agir
// l'applique et rend, s'il y a lieu, une DemandeAccueil : ce que Home doit
// faire hors de l'accueil (ouvrir un ecran, une affaire, deplacer la fenetre).
//
// L'etat qui survit (theme, disposition des tuiles, favoris, reglages des
// tuiles) reste lu et ecrit par AccueilEtat, comme avant.
// ============================================================

/// <summary>Ce que seul le plan de travail (Home.razor, lot 2) sait, a chaque rendu.</summary>
/// <param name="Recouvert">Un ecran de procedure ou une affaire recouvre l'accueil :
/// son titre, et le rond de profil avec lui, ne se voient plus.</param>
/// <param name="CodeOuvert">Le code de l'ecran de procedure ouvert (lien actif du menu).</param>
public sealed record ContexteAccueil(bool Recouvert, string? CodeOuvert);

/// <summary>Ce que l'accueil demande au plan de travail apres un geste.</summary>
/// <param name="Quoi">Une des constantes de <see cref="DemandeAccueil"/>.</param>
/// <param name="Valeur">Le code d'ecran, le numero d'affaire ou le texte deja tape, selon <paramref name="Quoi"/>.</param>
/// <param name="Alerte">L'alerte cliquee (<see cref="OuvrirAlerte"/>).</param>
public sealed record DemandeAccueil(string Quoi, string Valeur = "", AlerteGreffe? Alerte = null)
{
    public const string OuvrirCode = "ouvrirCode";
    public const string OuvrirAlerte = "ouvrirAlerte";
    public const string OuvrirNumero = "ouvrirNumero";
    /// <summary>La barre de recherche ou sa pastille « Ctrl K » : ouvrir la palette
    /// (Pont/PlanTravailPalette.cs), avec ce qui y a deja ete tape.</summary>
    public const string OuvrirPalette = "ouvrirPalette";
    public const string LoginChange = "loginChange";
    public const string ThemeChange = "themeChange";
    public const string Deplacer = "deplacer";
    public const string Reduire = "reduire";
    public const string Agrandir = "agrandir";
    public const string Fermer = "fermer";
}

/// <summary>Un geste remonte par une ile de l'accueil (rappel « geste ») : son nom et sa valeur JSON.</summary>
public sealed record GesteIle(string Nom, JsonElement Valeur);

// ---------------- La vue (ce que React affiche) ----------------

public sealed record VueAccueil(string Theme, VuePlanAccueil Accueil);

public sealed record ThemeVue(string Code, string Libelle);

/// <summary>Le rond de profil et, ouvert, la liste des logins.</summary>
/// <param name="Titre">L'infobulle du rond (login, profil, domaines).</param>
public sealed record ProfilVue(bool Ouvert, string Initiales, string Titre, string Login,
                               CompteVue[] Comptes);
public sealed record CompteVue(string Login, string Initiales, string Profil, string Domaines, bool Actif);

public sealed record VuePlanAccueil(bool MenuReduit, MenuVue Menu, EnteteVue Entete,
                                    ChiffresVue Chiffres, ProueVue? Proue, TuileVue[] Tuiles, SignalVue? Signal);

public sealed record MenuVue(bool FavorisOuverts, FavoriVue[] Favoris, MomentVue[] Moments);
public sealed record FavoriVue(string Code, string Libelle, string Detail, bool Actif);
/// <param name="Sous">Les verbes visibles, seulement quand le metier est ouvert.</param>
public sealed record MomentVue(string Titre, string Teinte, string Signe, int Compte, bool Ouvert, SousVue[] Sous);
/// <param name="Entrees">Les ecrans visibles, seulement quand le verbe est ouvert.</param>
public sealed record SousVue(string Titre, bool Ouvert, int Compte, EntreeVue[] Entrees);
public sealed record EntreeVue(string Code, string Libelle, string Detail, bool Actif, bool Favori);

// fix-ok: lot 10 -- le greffe passe a cote de l'agent, pas en dernier : un 7e argument apres Profil() cassait le controle
// tests/CharteCheck/Program.cs:198-200 (« ctx.Recouvert ? null : Profil()); », mesure au banc du 21/11/2026).
/// <param name="Panneau">Le panneau « Tuiles affichees sur l'accueil », seulement ouvert.</param>
/// <param name="Greffe">Le greffe ouvert par la session, a cote de l'agent (« Greffe de Grenoble (3801) », lot 10) ;
/// null en demonstration.</param>
/// <param name="Profil">Le rond de profil, quand l'accueil n'est pas recouvert.</param>
public sealed record EnteteVue(string Date, string Utilisateur, string? Greffe, PhraseVue Phrase, bool PanneauOuvert,
                               PanneauTuilesVue? Panneau, ProfilVue? Profil);
/// <param name="HorsDelaiConnu">La mesure « Hors delai legal » est relevee.</param>
/// <param name="Traites">La valeur de « Dossiers traites », null tant qu'elle n'est pas relevee.</param>
public sealed record PhraseVue(bool HorsDelaiConnu, int HorsDelai, int Arrivees, string? Traites);
/// <param name="Theme">Le theme choisi ; <paramref name="Themes"/> ceux de ThemesRig. Le choix vit ICI
/// depuis que la barre du haut ne porte plus que les onglets (19/11/2026).</param>
public sealed record PanneauTuilesVue(string Theme, ThemeVue[] Themes, DispositionVue[] Dispositions, BasculeVue[] Tuiles);
public sealed record DispositionVue(string Id, string Titre, string Aide, bool Actif);


/// <summary>Les trois tuiles chiffrees. Les styles des anneaux sont ecrits ici, en
/// culture invariante (un « 33,3% » casserait le degrade).</summary>
public sealed record ChiffresVue(int? Deplie, int Arrivees, string Detail1, string? TitreDetail1, string Anneau1,
                                 int Cible2, string Valeur2, string? Objectif2,
                                 bool Alerte3, int Cible3, string Valeur3, string? Objectif3);

/// <summary>La liste du chiffre deplie, puis les tuiles en resume d'une ligne.</summary>
/// <param name="Compte">Le nombre en gras du titre de la liste.</param>
/// <param name="Lien">La tuile « Tout voir » est sur l'accueil (sinon pas de lien).</param>
public sealed record ProueVue(int Rang, string Compte, bool Lien, AlerteLigneVue[] Arrivees, MesureVue[] Mesures,
                              HorsDelaiVue[] HorsDelai, ResumeTuileVue[] Resumes);
public sealed record HorsDelaiVue(string Dossier, string Nom, int Jours, string Numero, string Creation);
public sealed record ResumeTuileVue(string Id, string Titre, ResumeSpanVue[] Ligne);
/// <summary>Un span de la ligne de resume. Chaque morceau est un noeud de texte a part
/// (ou un &lt;b&gt;), comme les @expressions du .razor.</summary>
/// <param name="BlancAvant">Un blanc le separe du span precedent (Performance).</param>
public sealed record ResumeSpanVue(MorceauVue[] Morceaux, bool BlancAvant);
public sealed record MorceauVue(string Texte, bool Gras);

/// <summary>Une tuile posee sur la grille. Un seul corps est rempli, selon <paramref name="Id"/>,
/// sauf quand ses reglages sont ouverts (<paramref name="Reglages"/>).</summary>
/// <param name="Rang">Le z-index de la tuile (96 pour un widget metier deploye).</param>
/// <param name="AvecTitre">Un widget metier porte deja son titre dans son propre en-tete.</param>
public sealed record TuileVue(string Id, string Titre, bool Maitresse, bool Devant,
                              int Colonne, int Ligne, int Largeur, int Hauteur, int Rang,
                              bool AvecTitre, bool ReglagesOuverts, ReglagesVue? Reglages,
                              MesureVue[]? Performance, AlertesTuileVue? Alertes, AlertesBisVue? AlertesBis,
                              AlerteLigneVue[]? Taches, NouveauteVue[]? Nouveautes,
                              ActualitesTuileVue? Actualites, ActiviteTuileVue? Activite, MetierVue? Metier);

/// <summary>« Proue » : la tuile a amener dans la vue et a entourer d'or.
/// <paramref name="Numero"/> change a chaque demande, meme pour la meme tuile.</summary>
public sealed record SignalVue(string Id, int Numero);

public sealed record AlerteLigneVue(string Id, int Nombre, string Palier, string Titre, string Detail,
                                    string LibelleCible);
public sealed record GroupeAlertesVue(string Categorie, bool Repliee, int Charge, AlerteLigneVue[] Lignes);
public sealed record AlertesTuileVue(bool Vide, bool AfficherDetail, GroupeAlertesVue[] Groupes);
public sealed record MesureVue(string Libelle, string Valeur, int Part, string Statut, int Objectif,
                               string ObjectifTexte, int Atteinte);
public sealed record NouveauteVue(string Module, string Pictogramme, string Teinte, string Action, string Suite,
                                  string Detail, bool Ouverte);
public sealed record ActualiteVue(string Filet, string Type, string Titre, string Message, string Auteur,
                                  string Horodatage);
public sealed record ActualitesTuileVue(ActualiteVue[] Actifs, string Etat);
public sealed record ActiviteTuileVue(string Filtre, AgentVue[] Agents, int Total, CompteActiviteVue[] Comptes,
                                      GesteVue[] Gestes);
public sealed record AgentVue(string Login, string Nom);
public sealed record CompteActiviteVue(string Agent, int Nombre);
public sealed record GesteVue(string Agent, string Verbe, string Objet, string Depuis);

/// <summary>Un widget METIER (un par categorie de la feuille « Alertes accueil v2 Rig »).</summary>
/// <param name="TitreOuvrir">« Aller vers ... » de l'alerte ouverte ; null = « Ouvrir le dossier » inactif.</param>
/// <param name="Active">L'alerte dont les demandes sont montrees a droite.</param>
/// <param name="Panneau">Le panneau des demandes, seulement deploye.</param>
public sealed record MetierVue(bool Deploye, string Registre, string Titre, int Charge, string PalierCharge,
                               bool MenuOuvert, string[] Gestes, string? TitreOuvrir, string Filtre, bool FiltreActif,
                               int Urgents, int Demat, AlerteLigneVue[] Lignes, string? Active,
                               PanneauDemandesVue? Panneau);
/// <param name="Ouverte">L'alerte ouverte ; null = « Choisir une alerte a gauche ».</param>
public sealed record PanneauDemandesVue(AlerteLigneVue? Ouverte, DemandeVue[] Demandes, string TitreFermer);
public sealed record DemandeVue(string Chrono, string Societe, string Siren, string Type, string RecuLe,
                                int Anciennete, string Etat);
public sealed record AlertesBisVue(bool Vide, GroupeAlertesVue[] Groupes, string? Active, PanneauDemandesVue Panneau);

/// <param name="Genre">alertes, performance ou aucun (la tuile n'a pas de reglage propre).</param>
public sealed record ReglagesVue(string Genre, BasculeVue[] Categories, BasculeVue[] Alertes, int NiveauMini,
                                 string Tri, int Limite, bool AfficherDetail, BasculeVue[] Mesures);
/// <param name="Titre">L'infobulle de la bascule ; null = aucune.</param>
public sealed record BasculeVue(string Id, string Libelle, string? Titre, bool Actif);

/// <summary>
/// L'ETAT ET LES REGLES DE L'ACCUEIL. Une instance par fenetre, tenue par Home.razor.
/// Les membres gardent les noms qu'ils avaient dans le @code de Home.razor.
/// </summary>
public sealed class SessionAccueil : IAsyncDisposable
{
    public SessionAccueil(AccueilEtat etat)
    {
        Etat = etat;
        Theme = etat.Theme;
    }

    // ================= Fiches en conflit (REC-004 CA02) =================
    // fix-ok: refus du chef (21/11) : l'alerte des fiches modifiees des deux cotes etait servie par le serveur mais absente de
    // l'accueil (RecopieDeuxSensCheck : « Alerte inconnue de l'accueil : alerte-fiches-conflit » au clic) ; elle est relevee ici.
    /// <summary>L'alerte « fiche(s) modifiée(s) des deux côtés » de la tuile « A traiter ».</summary>
    public const string IdAlerteConflits = "alerte-fiches-conflit";

    /// <summary>Ou l'accueil lit cette alerte : par defaut le pont de l'ecran « Fiches en conflit » (le serveur du greffe de la
    /// session ; rien pour un agent que le greffe n'a pas designe ; la demonstration sans serveur). Un banc y branche le sien.</summary>
    public Func<Task<AlerteFichesEnConflit?>> SourceConflits { get; init; } = EcranConflits.Alerte;

    /// <summary>La derniere releve de l'alerte, lancee par <see cref="ReleverAlertes"/> et chaque minute : la vue suit a sa fin.</summary>
    public Task ReleveConflits { get; private set; } = Task.CompletedTask;

    private AlerteGreffe? AlerteConflits;

    /// <summary>Lit l'alerte au serveur. Le clic mene a la liste (l'ecran « Fiches en conflit »). Sans ecart a revoir, pas de
    /// ligne. Serveur injoignable : l'alerte garde son dernier etat, la releve suivante (une minute) reessaie.</summary>
    private async Task RelireConflitsAsync()
    {
        AlerteFichesEnConflit? a;
        try { a = await SourceConflits(); }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or ErreurServeur) { return; }
        var nouvelle = a is { Nombre: > 0 }
            ? new AlerteGreffe(IdAlerteConflits, a.Titre, a.Detail, a.Statut, a.Cible, "la liste des fiches en conflit", CatalogueAlertes.Corrections, a.Nombre)
            : null;
        if (nouvelle == AlerteConflits) return;
        AlerteConflits = nouvelle;
        Alertes = AlertesOuvertes();
        Change?.Invoke();
    }

    /// <summary>L'etat retenu d'une session a l'autre (accueil.json).</summary>
    public AccueilEtat Etat { get; }

    /// <summary>Change hors d'un geste (minuteurs) : Home re-rend, la vue suit.</summary>
    public event Action? Change;

    // ---------------------------------------------------------------
    // Le login connecte : son niveau d'acces decide des dossiers visibles.
    // ---------------------------------------------------------------
    public string LoginCourant { get; private set; } = JeuDemo.Comptes[0].Login;
    public CompteRig CompteCourant => JeuDemo.Compte(LoginCourant);
    /// <summary>Le nom que l'entete salue : l'agent reconnu par sa session Windows quand l'app
    /// est branchee au serveur (SOC-001, Pont/SessionAgent.cs), sinon le login de demonstration.</summary>
    public string Utilisateur => SessionAgent.NomAgent ?? CompteCourant.Nom;
    public string[] Domaines => CompteCourant.Domaines;

    /// <summary>Theme d'affichage courant (code de ThemesRig). Clair par
    /// defaut ; la valeur retenue du profil est reposee a la construction.</summary>
    public string Theme { get; private set; }

    /// <summary>
    /// Premier passage, a l'ouverture de la fenetre (ex-OnInitialized de Home) :
    /// releves, puis la disposition d'origine reposee si elle a change.
    /// </summary>
    public void Initialiser()
    {
        RelevePerf();
        ReleveActualites();
        ReleverAlertes();

        // La disposition d'origine a change (tuiles « Reprendre » et « Ma file »
        // retirees, grille retassee sur 5 lignes) : un accueil.json ecrit avant
        // ce changement garderait l'ancienne grille, donc on la repose une fois.
        if (Etat.VersionDisposition < DispositionCourante)
        {
            foreach (var disparue in Etat.Tuiles.Keys
                         .Where(id => !Definitions.Any(d => d.Id == id)).ToList())
            {
                Etat.Tuiles.Remove(disparue);
            }
            // v9 : les dispositions preparees. La disposition active est
            // appliquee EN ENTIER, allumage compris : sinon « A traiter BIS »,
            // allumee par defaut en v8, recouvrirait le haut de l'accueil.
            AppliquerDisposition(DispositionActive.Id);
            Etat.VersionDisposition = DispositionCourante;
            Etat.Enregistrer();
        }

        foreach (var def in Definitions) { Pose(def.Id); }
        foreach (var a in Alertes)
        {
            if (!Etat.Alertes.ContainsKey(a.Id)) Etat.Alertes[a.Id] = true;
        }
    }

    /// <summary>
    /// Les minuteurs, branches au premier rendu de la fenetre (comme avant) :
    /// la performance chaque seconde, les actualites chaque minute.
    /// </summary>
    public void Demarrer()
    {
        MinuteurPerf ??= new Timer(_ =>
        {
            RelevePerf();
            Change?.Invoke();
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        // Les actualites vivent sur le reseau : une minute de retard est sans
        // consequence, une lecture par seconde martyriserait le partage.
        MinuteurActualites ??= new Timer(_ =>
        {
            ReleveActualites();
            // Les fiches en conflit vivent sur le serveur : une minute suffit aussi (la releve previent la vue a sa fin).
            ReleveConflits = RelireConflitsAsync();
            Change?.Invoke();
        }, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    public async ValueTask DisposeAsync()
    {
        if (MinuteurPerf is not null) await MinuteurPerf.DisposeAsync();
        if (MinuteurActualites is not null) await MinuteurActualites.DisposeAsync();
        MinuteurPerf = null;
        MinuteurActualites = null;
    }

    // ================= Reglages, theme, profil =================
    // La barre du haut ne porte plus que les onglets et les boutons de la
    // fenetre (19/11/2026, « comme Brave ») : le theme se choisit dans les
    // reglages de l'accueil, le profil a droite de son titre.

    /// <summary>Panneau du rond de profil : la liste des logins.</summary>
    public bool PanneauProfilOuvert { get; private set; }

    public bool PanneauTuilesOuvert { get; private set; }

    private void BasculerPanneauTuiles() => PanneauTuilesOuvert = !PanneauTuilesOuvert;

    /// <summary>
    /// Le theme ne peut pas rester porte par l'accueil : le fond de la page hote
    /// (html, pose par ThemeRig.tsx) et le fond de la fenetre WPF, qui borde le
    /// WebView de 6 px (Home : FenetreInterop.AppliquerFond), resteraient sombres
    /// et cerneraient le mode clair d'un cadre noir.
    /// </summary>
    private void ChoisirTheme(string? code)
    {
        // Le setter de l'etat ramene un code inconnu au clair : on relit SA
        // valeur plutot que de recopier telle quelle celle de la liste.
        Etat.Theme = code ?? ThemesRig.Defaut;
        Theme = Etat.Theme;
        Etat.Enregistrer();
    }

    /// <summary>Un compte choisi dans le panneau du rond de profil : le login change.
    /// Home replie ensuite ce qui n'est plus visible pour ce login (arbre, apercu).</summary>
    private void ChangerLogin(string login)
    {
        LoginCourant = login;
        PanneauProfilOuvert = false;
    }

    // ================= Menu de gauche =================

    /// <summary>Le groupe Favoris est ouvert d'entree, comme « Ouvrir » : on
    /// l'a mis la pour y aller directement.</summary>
    public bool FavorisOuverts { get; private set; } = true;

    /// <summary>Les ecrans d'un verbe que le login courant a le droit de voir.</summary>
    private EntreeMenu[] EntreesVisibles(SousMenu s)
        => s.Entrees.Where(e => PerimetreProfil.Visible(e.Code, Domaines)).ToArray();

    /// <summary>Les ecrans etoiles, dans l'ordre ou ils l'ont ete. Un favori
    /// pointant vers un ecran que le profil courant ne voit plus est FILTRE
    /// ici, pas efface du fichier : le droit peut revenir.</summary>
    private EntreeMenu[] EntreesFavorites()
        => Etat.VuesFavorites
               .Select(c => MenuAccueil.Moments.SelectMany(m => m.Sous)
                                   .SelectMany(s => s.Entrees)
                                   .FirstOrDefault(e => e.Code == c))
               .Where(e => e is not null && PerimetreProfil.Visible(e.Code, Domaines))
               .Select(e => e!)
               .ToArray();

    /// <summary>Allume ou eteint l'etoile d'un ecran. Le choix est ecrit dans
    /// accueil.json a l'instant du clic.</summary>
    private void BasculerFavori(string code) => Etat.BasculerFavori(code);

    /// <summary>Les verbes d'un metier qui gardent au moins un ecran.</summary>
    private SousMenu[] SousVisibles(MomentMenu m)
        => m.Sous.Where(s => EntreesVisibles(s).Length > 0).ToArray();

    /// <summary>Les metiers qui gardent au moins un ecran : un metier vide
    /// n'est pas affiche du tout.</summary>
    private MomentMenu[] MomentsVisibles
        => MenuAccueil.Moments.Where(m => SousVisibles(m).Length > 0).ToArray();

    private int NombreEcransVisibles(MomentMenu m)
        => SousVisibles(m).Sum(s => EntreesVisibles(s).Length);

    /// <summary>Sous-menus deplies, reperes par « Metier/Verbe ». A l'ouverture
    /// d'un metier, son PREMIER verbe s'ouvre seul : on voit tout de suite des
    /// ecrans, pas une liste de titres vides.</summary>
    private readonly HashSet<string> SousOuverts = new() { "Ouvrir/Creer une affaire" };
    private static string CleSous(MomentMenu m, SousMenu s) => $"{m.Titre}/{s.Titre}";
    private bool SousEstOuvert(MomentMenu m, SousMenu s)
        => s.Titre.Length == 0 || SousOuverts.Contains(CleSous(m, s));
    private void BasculerSous(MomentMenu m, SousMenu s)
    {
        var cle = CleSous(m, s);
        if (!SousOuverts.Add(cle)) SousOuverts.Remove(cle);
    }

    private readonly HashSet<string> MomentsOuverts = new() { "Ouvrir" };
    private void BasculerMoment(string titre)
    {
        if (!MomentsOuverts.Add(titre)) { MomentsOuverts.Remove(titre); return; }

        // A l'ouverture d'un metier, son premier verbe s'ouvre avec lui : sans
        // cela on ne verrait qu'une liste de titres, et il faudrait deux clics
        // de plus pour atteindre le moindre ecran.
        var m = MenuAccueil.Moments.FirstOrDefault(x => x.Titre == titre);
        if (m is not null && m.Sous.Length > 0 && m.Sous[0].Titre.Length > 0)
            SousOuverts.Add(CleSous(m, m.Sous[0]));
    }

    /// <summary>Menu de gauche replie sur un rail etroit. Le menu reste
    /// visible dans les deux etats : seule la largeur de sa colonne change.</summary>
    public bool MenuReduit { get; private set; }
    private void BasculerMenu() => MenuReduit = !MenuReduit;

    /// <summary>Clic sur un moment. Si le menu est replie, le clic le deploie
    /// d'abord : sur le rail, les libelles ne sont pas lisibles.</summary>
    private void OuvrirMoment(string titre)
    {
        if (MenuReduit)
        {
            MenuReduit = false;
            MomentsOuverts.Add(titre);
            return;
        }
        BasculerMoment(titre);
    }


    // ================= Tuiles =================
    /// <summary>Une tuile de l'accueil. <paramref name="Maitresse"/> designe la tuile
    /// prioritaire, mise en avant.</summary>
    public sealed record DefinitionTuile(string Id, string Titre, PoseTuile Defaut,
        string Statut = "neutre", bool Maitresse = false);

    /// <summary>Numero de la disposition d'origine ecrite dans ce fichier. A
    /// monter d'un cran a chaque fois qu'on change la grille par defaut.</summary>
    public const int DispositionCourante = 9;

    public readonly DefinitionTuile[] Definitions =
    [
        // Disposition calee pour que TOUTES les tuiles tiennent dans l'ecran
        // visible, SANS defilement (demande du 17/09/2026) : 5 lignes de grille
        // au total, la ou l'ancienne disposition en occupait 10.
        // « A traiter » tient toute la MOITIE DROITE sur les 5 lignes : c'est la
        // tuile la plus utilisee de la journee, elle ne doit jamais demander de scroll.
        new("alertes",     "A traiter",    new() { Colonne = 7, Ligne = 1, Largeur = 6,  Hauteur = 3 }, "alerte", true),
        // « Mes taches du jour » : les constats CALCULES sur les dossiers
        // (AlertesGreffe.ReleverCalculees) que « A traiter » ne montre plus
        // depuis le 17/09/2026. Ils portent le mecanisme TASK DRIVEN : le clic
        // ouvre la tache, l'arbre et la fiche se replient alors sur les seuls
        // champs que la recette declare. Sans cette tuile, ce code vivait
        // encore mais n'avait plus aucune porte d'entree dans l'accueil.
        // « A traiter BIS » (demande du 21/09/2026) : la version ENRICHIE de
        // « A traiter », en bande sur TOUT le haut de l'ecran. A gauche l'arbre
        // des categories, a droite les demandes de l'alerte choisie -- le meme
        // tableau que les widgets metier (PanneauDemandes).
        // Hors disposition « Bandeau » elle est eteinte : sa pose par defaut est
        // rangee SOUS les widgets metier, ou elle ne recouvre rien.
        new("alertes-bis", "A traiter BIS", new() { Colonne = 1, Ligne = 12, Largeur = 12, Hauteur = 3 }, "alerte"),
        new("taches",      "Mes taches du jour", new() { Colonne = 7, Ligne = 4, Largeur = 6, Hauteur = 2 }, "alerte"),
        // Moitie gauche, apres le retrait de « Mes affaires recentes »
        // (17/09/2026) : plus aucun trou dans la grille, les 5 lignes x 6
        // colonnes sont entierement occupees.
        // Le journal des gestes de l'equipe : reserve au chef de greffe (le
        // compte qui VALIDE). C'est une LISTE : il prend la hauteur (3 lignes)
        // laissee libre en haut a gauche.
        new("activite",    "Activite des utilisateurs", new() { Colonne = 1, Ligne = 1, Largeur = 3, Hauteur = 3 }, "action"),
        // Les actualites sont REDIGEES (pannes INPI, alertes fraude CNG), la ou
        // « A traiter » est CALCULEE sur les dossiers : deux tuiles distinctes,
        // sinon un message ecrit se perdrait au milieu de lignes calculees.
        new("actualites",  "Actualites", new() { Colonne = 4, Ligne = 1, Largeur = 3, Hauteur = 1 }, "attente"),
        new("nouveautes",  "Nouveautes",   new() { Colonne = 4, Ligne = 2, Largeur = 3, Hauteur = 2 }, "attente"),
        // La performance est une bande de chiffres : elle gagne a etre LARGE,
        // elle occupe donc toute la largeur du bas de la moitie gauche.
        new("performance", "Performance",  new() { Colonne = 1, Ligne = 4, Largeur = 6, Hauteur = 2 }, "ok"),
        // ---- Les widgets METIER : UN PAR CATEGORIE de la feuille « Alertes
        // accueil v2 Rig » (Feuil2), demande du 17/09/2026. Chacun est un
        // panneau double : a gauche la recherche, les gestes et les alertes de
        // SA categorie, a droite les demandes de l'alerte selectionnee.
        // POSE (demande du 17/09/2026) : QUATRE par ligne, sur 3 colonnes de
        // grille chacun, et 3 lignes de haut -- la vue par defaut n'affiche que
        // la colonne de gauche, il lui faut donc de la HAUTEUR pour montrer
        // toutes les alertes de la categorie sans defiler dans la tuile. Les
        // huit tiennent ainsi en deux bandes (lignes 6-8 et 9-11), sous les six
        // tuiles d'origine dont la disposition n'est pas touchee.
        new("metier-arrivees",      "Arrivées",              new() { Colonne = 1,  Ligne = 6, Largeur = 3, Hauteur = 3 }, "attente"),
        new("metier-formalites",    "Formalités",            new() { Colonne = 4,  Ligne = 6, Largeur = 3, Hauteur = 3 }, "attente"),
        new("metier-notifications", "Notifications",         new() { Colonne = 7,  Ligne = 6, Largeur = 3, Hauteur = 3 }, "attente"),
        new("metier-das",           "DAS",                   new() { Colonne = 10, Ligne = 6, Largeur = 3, Hauteur = 3 }, "attente"),
        new("metier-dca",           "DCA",                   new() { Colonne = 1,  Ligne = 9, Largeur = 3, Hauteur = 3 }, "attente"),
        new("metier-corrections",   "Corrections diverses",  new() { Colonne = 4,  Ligne = 9, Largeur = 3, Hauteur = 3 }, "attente"),
        new("metier-surveillances", "Surveillances Greffe",  new() { Colonne = 7,  Ligne = 9, Largeur = 3, Hauteur = 3 }, "attente"),
        new("metier-divers",        "Divers",                new() { Colonne = 10, Ligne = 9, Largeur = 3, Hauteur = 3 }, "attente")
    ];

    // ================= Dispositions preparees =================
    /// <summary>Une disposition prete a l'emploi (demande du 21/09/2026) : les
    /// tuiles ETEINTES et, pour celles qui s'ecartent de la pose par defaut de
    /// <see cref="Definitions"/>, leur pose propre. Une tuile absente de
    /// <paramref name="Poses"/> garde sa pose par defaut.</summary>
    public sealed record Disposition(string Id, string Titre, string Aide,
        string[] Eteintes, (string Id, int C, int L, int W, int H)[] Poses);

    private static readonly string[] TuilesMetier =
    [
        "metier-arrivees", "metier-formalites", "metier-notifications", "metier-das",
        "metier-dca", "metier-corrections", "metier-surveillances", "metier-divers"
    ];

    public static readonly Disposition[] Dispositions =
    [
        // 1. Le gros widget « A traiter BIS » sur tout le haut de l'ecran.
        new("bandeau", "Bandeau", "« A traiter BIS » en bande sur tout le haut",
            ["alertes", .. TuilesMetier],
            [
                P("alertes-bis", 1, 1, 12, 3),
                P("activite",    1, 4, 3, 2),
                P("actualites",  4, 4, 3, 1),
                P("nouveautes",  4, 5, 3, 1),
                P("taches",      7, 4, 6, 2),
                P("performance", 1, 6, 12, 2)
            ]),
        // 2. Le « A traiter » habituel sur la moitie droite (poses par defaut).
        new("lateral", "Latéral", "« A traiter » sur la moitie droite",
            ["alertes-bis", .. TuilesMetier], []),
        // 3. Les huit petits widgets metier, un par categorie, en tete.
        new("metiers", "Par métier", "Un petit widget par categorie metier",
            ["alertes", "alertes-bis"],
            [
                P("metier-arrivees",      1,  1, 3, 3),
                P("metier-formalites",    4,  1, 3, 3),
                P("metier-notifications", 7,  1, 3, 3),
                P("metier-das",           10, 1, 3, 3),
                P("metier-dca",           1,  4, 3, 3),
                P("metier-corrections",   4,  4, 3, 3),
                P("metier-surveillances", 7,  4, 3, 3),
                P("metier-divers",        10, 4, 3, 3),
                P("activite",    1, 7, 3, 3),
                P("actualites",  4, 7, 3, 1),
                P("nouveautes",  4, 8, 3, 2),
                P("taches",      7, 7, 6, 2),
                P("performance", 7, 9, 6, 1)
            ])
    ];

    private static (string Id, int C, int L, int W, int H) P(string id, int c, int l, int w, int h)
        => (id, c, l, w, h);

    private Disposition DispositionActive
        => Dispositions.FirstOrDefault(d => d.Id == Etat.Disposition) ?? Dispositions[1];

    /// <summary>Choisit une disposition : pose ET allumage de chaque tuile.</summary>
    private void AppliquerDisposition(string id)
    {
        Etat.Disposition = id;
        var d = DispositionActive;
        foreach (var def in Definitions) { Pose(def.Id).Visible = !d.Eteintes.Contains(def.Id); }
        ReinitialiserDisposition();
    }

    public PoseTuile Pose(string id)
    {
        if (Etat.Tuiles.TryGetValue(id, out var pose)) return pose;
        var defaut = Definitions.First(d => d.Id == id).Defaut;
        var copie = new PoseTuile
        {
            Colonne = defaut.Colonne, Ligne = defaut.Ligne,
            Largeur = defaut.Largeur, Hauteur = defaut.Hauteur,
            Visible = !DispositionActive.Eteintes.Contains(id)
        };
        Etat.Tuiles[id] = copie;
        return copie;
    }

    /// <summary>Cette tuile est-elle dans le perimetre du compte connecte ?
    /// « Activite des utilisateurs » montre le travail des collegues : elle
    /// n'appartient qu'au chef de greffe. Une tuile non autorisee ne s'affiche
    /// pas ET ne figure pas dans le panneau de choix : sinon on saurait
    /// qu'elle existe sans pouvoir l'ouvrir.</summary>
    private bool TuileAutorisee(DefinitionTuile def)
        => def.Id != "activite" || JournalActions.VisiblePour(CompteCourant);

    /// <summary>
    /// Remet CHAQUE tuile a sa pose dans la disposition choisie (colonne,
    /// ligne, largeur, hauteur) : chaque disposition est calee sur la grille
    /// 12 colonnes sans chevauchement. Ce que l'utilisateur a allume ou
    /// eteint est conserve : seule la geometrie est reprise.
    /// </summary>
    private void ReinitialiserDisposition()
    {
        var rang = 1;
        var poses = DispositionActive.Poses;
        foreach (var def in Definitions)
        {
            var pose = Pose(def.Id);
            var propre = poses.FirstOrDefault(x => x.Id == def.Id);
            var (c, l, w, h) = propre.Id is null
                ? (def.Defaut.Colonne, def.Defaut.Ligne, def.Defaut.Largeur, def.Defaut.Hauteur)
                : (propre.C, propre.L, propre.W, propre.H);
            pose.Colonne = c;
            pose.Ligne   = l;
            pose.Largeur = w;
            pose.Hauteur = h;
            pose.Ordre   = rang++;
        }
        Etat.Enregistrer();
    }

    private void BasculerTuile(string id)
    {
        var p = Pose(id);
        p.Visible = !p.Visible;
        // Une tuile qu'on rallume est la derniere manipulee : elle passe
        // devant, sinon elle pourrait reapparaitre sous une voisine.
        if (p.Visible) Etat.Avancer(id);
        Etat.Enregistrer();
    }

    /// <summary>La tuile est lachee sur une case (glisser-deposer de Accueil.tsx).</summary>
    public void PoserTuile(string id, int colonne, int ligne)
    {
        var p = Pose(id);
        p.Colonne = Math.Clamp(colonne, 1, Math.Max(13 - p.Largeur, 1));
        p.Ligne = Math.Max(ligne, 1);
        Etat.Avancer(id);
        Etat.Enregistrer();
    }

    /// <summary>La poignee du coin bas-droit est relachee (Accueil.tsx).</summary>
    public void DimensionnerTuile(string id, int largeur, int hauteur)
    {
        var p = Pose(id);
        p.Largeur = Math.Clamp(largeur, 3, Math.Max(13 - p.Colonne, 3));
        p.Hauteur = Math.Max(hauteur, 1);
        Etat.Avancer(id);
        Etat.Enregistrer();
    }

    // ================= Corps des tuiles =================

    // --- Performance : activite des DOSSIERS, calculee dans MesuresPerf.cs
    //     (traites, en cours, hors delai, rejets, pieces, delai moyen).
    private readonly MesuresPerf Sonde = new();
    public MesurePerf[] Mesures { get; private set; } = [];
    private Timer? MinuteurPerf;

    public void RelevePerf()
    {
        Mesures = Sonde.Relever();
    }

    private bool MesureActive(string libelle)
        => !Etat.Mesures.TryGetValue(libelle, out var v) || v;

    private void BasculerMesure(string libelle)
    {
        Etat.Mesures[libelle] = !MesureActive(libelle);
        Etat.Enregistrer();
    }

    // --- Alertes : relevees sur les dossiers connus (AlertesGreffe), jamais
    // ecrites en dur, et chacune s'allume ou s'eteint independamment.
    private readonly AlertesGreffe SondeAlertes = new();
    public AlerteGreffe[] Alertes { get; private set; } = [];

    /// <summary>Les constats calcules, montres par la tuile « Mes taches du jour ».</summary>
    public AlerteGreffe[] Taches { get; private set; } = [];

    /// <summary>Relit les alertes et les taches a la source (demarrage, tache
    /// close, « Rafraichir les compteurs »).</summary>
    public void ReleverAlertes()
    {
        Alertes = AlertesOuvertes();
        Taches = TachesOuvertes();
        ReleveConflits = RelireConflitsAsync();
    }

    /// <summary>
    /// Les constats CALCULES sur les dossiers (hors delai, verrous, taches en
    /// attente, lots), MOINS les taches deja terminees. C'est ce que montre la
    /// tuile « Mes taches du jour » : le meme filtre que la file d'alertes,
    /// pour qu'une tache close ne revienne pas au redemarrage.
    /// </summary>
    public AlerteGreffe[] TachesOuvertes()
        => SondeAlertes.ReleverCalculees()
            .Where(a => a.Tache is not { } t || !TacheDejaFaite(t.Code, t.Dossier))
            .ToArray();

    /// <summary>
    /// Les alertes relevees, MOINS celles dont la tache est deja terminee.
    /// Sans ce filtre, la file redonnerait une tache faite au redemarrage.
    /// </summary>
    public AlerteGreffe[] AlertesOuvertes()
        => SondeAlertes.Relever()
            .Where(a => a.Tache is not { } t || !TacheDejaFaite(t.Code, t.Dossier))
            // Les fiches modifiees des deux cotes (REC-004 CA02) : relevees au serveur, pour l'agent designe seulement.
            .Concat(AlerteConflits is { } conflits ? [conflits] : Array.Empty<AlerteGreffe>())
            .ToArray();

    /// <summary>
    /// Vrai si la tache portee par ce code a deja ete terminee sur cette
    /// affaire. Le test passe par la RECETTE, jamais par le code brut :
    /// depuis la 2e recette, DPER et DSTE menent a la meme tache, et la
    /// cloture est retenue sous le code de la recette. Comparer les codes
    /// bruts laisserait revenir l'alerte DPER apres une tache close en DSTE.
    /// </summary>
    private bool TacheDejaFaite(string code, string affaire)
        => RecettesTaches.ParCode(code) is { } r
           && Etat.TacheFaite(r.Code, affaire);

    /// <summary>Ce que la tuile des taches montre : le plus grave en tete.</summary>
    private AlerteGreffe[] TachesVues
        => Taches.OrderByDescending(a => a.Gravite)
                 .ThenBy(a => a.Titre, StringComparer.OrdinalIgnoreCase)
                 .ToArray();

    /// <summary>Reglages d'affichage de la tuile, conserves dans accueil.json.</summary>
    private ReglagesAlertes ReglagesAlertes => Etat.ReglagesAlertes;

    /// <summary>Ce que la tuile montre reellement : ecoute, gravite, ordre, plafond.</summary>
    private AlerteGreffe[] AlertesVues
        => ReglagesAlertes.Appliquer(Alertes, AlerteActive, CategorieActive);

    /// <summary>Les alertes vues, GROUPEES par categorie, dans l'ordre du
    /// catalogue metier. Une categorie sans alerte visible ne fait pas de
    /// titre vide : elle disparait.</summary>
    private (string Categorie, AlerteGreffe[] Lignes)[] AlertesParCategorie
        => CatalogueAlertes.Categories
            .Select(c => (Categorie: c, Lignes: AlertesVues.Where(a => a.Categorie == c).ToArray()))
            .Where(g => g.Lignes.Length > 0)
            .ToArray();

    /// <summary>Les lignes « Arrivees » telles que la tuile « A traiter » les
    /// montre (memes reglages d'ecoute) : la tuile chiffree « Arrivees » affiche
    /// LEUR total, donc le meme nombre que le titre du groupe.</summary>
    private AlerteGreffe[] ArriveesVues
        => AlertesVues.Where(a => a.Categorie == CatalogueAlertes.Arrivees).ToArray();

    /// <summary>Une mesure de la tuile Performance, par son libelle
    /// (MesuresPerf.Relever) ; null tant que la premiere releve n'est pas faite.</summary>
    private MesurePerf? MesureNommee(string libelle)
        => Mesures.FirstOrDefault(m => m.Libelle == libelle);

    // ================= « Proue » (/draft conv-1983, 18/11/2026) =================
    // Un clic sur une tuile chiffree deplie SA liste juste dessous ; pendant ce
    // temps la grille se replie en resumes d'une ligne, pour que la liste se lise
    // sans defiler. Un second clic sur le chiffre, un resume ou le lien « Tout
    // voir » rendent la grille. Rien n'est enregistre : l'accueil s'ouvre
    // toujours sur la grille, comme avant.

    /// <summary>La tuile chiffree depliee (1 arrivees, 2 traites, 3 hors delai), ou null.</summary>
    public int? ChiffreDeplie { get; private set; }

    /// <summary>La tuile a amener dans la vue au rendu qui fait reapparaitre la grille.</summary>
    private string? TuileASignaler;
    private int NumeroSignal;

    private void BasculerChiffre(int rang) => ChiffreDeplie = ChiffreDeplie == rang ? null : rang;

    private void VoirTuile(string id)
    {
        ChiffreDeplie = null;
        TuileASignaler = id;
        NumeroSignal++;
    }

    /// <summary>« mercredi 18 novembre » ; les capitales viennent du CSS.</summary>
    private static string DateDuJour
        => DateTime.Today.ToString("dddd d MMMM", CultureInfo.GetCultureInfo("fr-FR"));

    /// <summary>Vrai si la tuile est posee sur l'accueil et permise a ce compte :
    /// un lien « Tout voir » vers une tuile masquee ne menerait nulle part.</summary>
    private bool TuileVisible(string id)
        => Definitions.FirstOrDefault(d => d.Id == id) is { } def && Pose(id).Visible && TuileAutorisee(def);

    /// <summary>Les tuiles de la grille, dans l'ordre de lecture (ligne puis colonne).</summary>
    private DefinitionTuile[] TuilesEnResume
        => Definitions.Where(d => Pose(d.Id).Visible && TuileAutorisee(d))
                      .OrderBy(d => Pose(d.Id).Ligne).ThenBy(d => Pose(d.Id).Colonne)
                      .ToArray();

    /// <summary>
    /// L'anneau des arrivees : une part par categorie, de la plus chargee a la
    /// moins chargee, chacune un cran plus pale, un fin vide entre deux parts.
    /// Les pourcentages sont ecrits en culture INVARIANTE : « 33,3% » casserait
    /// le degrade.
    /// </summary>
    public static string AnneauArrivees(AlerteGreffe[] lignes)
    {
        var parts = lignes.Where(a => a.Nombre > 0).OrderByDescending(a => a.Nombre).ToArray();
        var total = parts.Sum(a => a.Nombre);
        if (total == 0) return "var(--rig-anneau-piste)"; // piste posee sur .rig-chiffre

        static string Pc(double v) => v.ToString("0.##", CultureInfo.InvariantCulture) + "%";
        var vide = parts.Length > 1 ? 1.6 : 0;
        var segments = new List<string>();
        double debut = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var fin = debut + parts[i].Nombre * 100.0 / total;
            var plein = Math.Max(debut, fin - vide);
            var opacite = Math.Max(32, 100 - 22 * i);
            segments.Add($"color-mix(in srgb, var(--rig-chiffre-texte) {opacite}%, transparent) {Pc(debut)} {Pc(plein)}");
            if (plein < fin) segments.Add($"transparent {Pc(plein)} {Pc(fin)}");
            debut = fin;
        }
        return "conic-gradient(" + string.Join(", ", segments) + ")";
    }

    /// <summary>
    /// Le resume d'UNE ligne d'une tuile, quand la grille est repliee : ce que
    /// la tuile montre en premier (le plus grave, le plus recent, le plus
    /// charge), lu aux memes sources que son corps.
    /// </summary>
    private ResumeSpanVue[] ResumeTuile(DefinitionTuile def)
    {
        static MorceauVue T(string texte) => new(texte, false);
        static MorceauVue B(string texte) => new(texte, true);
        static ResumeSpanVue S(params MorceauVue[] m) => new(m, false);
        var spans = new List<ResumeSpanVue>();
        switch (def.Id)
        {
            case "alertes" or "alertes-bis":
                var groupes = AlertesParCategorie;
                if (groupes.Length == 0) spans.Add(S(T("Aucune alerte a afficher")));
                foreach (var g in groupes.Take(3))
                    spans.Add(S(B(g.Lignes.Sum(x => x.Nombre).ToString()), T(" "), T(g.Categorie)));
                break;
            case "taches":
                var tache = TachesVues.FirstOrDefault();
                if (tache is null)
                {
                    spans.Add(S(T("Aucune tache en attente")));
                }
                else
                {
                    spans.Add(S(T(tache.Titre)));
                    if (TachesVues.Length > 1)
                        spans.Add(S(T("+"), T((TachesVues.Length - 1).ToString()), T(" autre(s)")));
                }
                break;
            case "activite":
                var geste = ActivitesVues.FirstOrDefault();
                spans.Add(geste is null
                    ? S(T("Aucun geste aujourd'hui"))
                    : S(B(geste.Agent), T(" "), T(geste.Verbe), T(" "), T(geste.Objet)));
                break;
            case "actualites":
                var actu = ActualitesVues.Actifs.FirstOrDefault();
                spans.Add(actu is null
                    ? S(T("Aucune actualite en cours"))
                    : S(B(actu.Fiche.Libelle), T(" "), T(actu.Titre)));
                break;
            case "nouveautes":
                var nouv = Nouveautes.FirstOrDefault();
                if (nouv is not null) spans.Add(S(B(nouv.Action), T(" "), T(nouv.Suite)));
                break;
            case "performance":
                // La mesure la plus loin de son objectif, hors les deux deja en tuiles chiffrees.
                var mesure = Mesures.Where(x => MesureActive(x.Libelle)
                                             && x.Libelle != MesuresPerf.LibelleTraites
                                             && x.Libelle != MesuresPerf.LibelleHorsDelai)
                                    .OrderBy(x => x.Atteinte)
                                    .FirstOrDefault();
                if (mesure is not null)
                {
                    spans.Add(S(T(mesure.Libelle), T(" "), B(mesure.Valeur)));
                    spans.Add(new ResumeSpanVue([T(mesure.ObjectifTexte)], BlancAvant: true));
                }
                break;
            case var id when id.StartsWith("metier-"):
                var lignesMetier = MetierLignes(MetierCategorie(id));
                var plusChargee = lignesMetier.OrderByDescending(x => x.Nombre).FirstOrDefault();
                spans.Add(S(B(lignesMetier.Sum(x => x.Nombre).ToString()), T(" a traiter")));
                if (plusChargee is not null) spans.Add(S(T(plusChargee.Titre)));
                break;
        }
        return [.. spans];
    }

    /// <summary>Une categorie eteinte masque TOUTES ses alertes. Absente des
    /// reglages = surveillee : une nouvelle categorie livree plus tard se voit
    /// sans que personne ait a l'allumer.</summary>
    private bool CategorieActive(string categorie)
        => !Etat.CategoriesAlertes.TryGetValue(CatalogueAlertes.IdentifiantCategorie(categorie), out var v) || v;

    /// <summary>Une categorie REPLIEE garde son titre et son compte, mais cache
    /// ses lignes. C'est un rangement de lecture, pas un filtre : les alertes
    /// restent comptees dans le statut de la tuile.</summary>
    private bool CategorieRepliee(string categorie)
        => Etat.CategoriesRepliees.TryGetValue(CatalogueAlertes.IdentifiantCategorie(categorie), out var v) && v;

    private void BasculerRepliCategorie(string categorie)
    {
        var id = CatalogueAlertes.IdentifiantCategorie(categorie);
        Etat.CategoriesRepliees[id] = !CategorieRepliee(categorie);
        Etat.Enregistrer();
    }

    private void BasculerCategorie(string categorie)
    {
        var id = CatalogueAlertes.IdentifiantCategorie(categorie);
        Etat.CategoriesAlertes[id] = !(Etat.CategoriesAlertes.TryGetValue(id, out var v) && v);
        Etat.Enregistrer();
    }

    private void BasculerAlerte(string id)
    {
        Etat.Alertes[id] = !(Etat.Alertes.TryGetValue(id, out var v) && v);
        Etat.Enregistrer();
    }

    private bool AlerteActive(string id) => !Etat.Alertes.TryGetValue(id, out var v) || v;

    private void ReglerAlertes(Action<ReglagesAlertes> geste)
    {
        geste(Etat.ReglagesAlertes);
        Etat.Enregistrer();
    }

    // Les trois reglages des listes et du champ de la tuile « A traiter ».
    private void ChoisirNiveauAlertes(string? valeur)
        => ReglerAlertes(r => r.NiveauMini = int.TryParse(valeur, out var n)
                                             ? Math.Clamp(n, 0, 3) : 0);

    private void ChoisirTriAlertes(string? valeur)
        => ReglerAlertes(r => r.Tri = valeur is { Length: > 0 } t ? t : "gravite");

    private void ChoisirLimiteAlertes(string? valeur)
        => ReglerAlertes(r => r.Limite = int.TryParse(valeur, out var n)
                                         ? Math.Clamp(n, 0, 20) : 0);

    // ================= Actualites ==================
    // Des messages REDIGES, lus dans un fichier PARTAGE. Le reseau peut etre
    // coupe : on affiche alors le dernier bon contenu ET on l'ECRIT sous la
    // tuile -- sans cette ligne, l'utilisateur croirait lire l'actualite du
    // jour alors qu'il lit celle d'avant-hier.
    private readonly DepotActualites Actualites = new();
    private LectureActualites ActualitesVues = new([], SourceActualites.Aucune, null, string.Empty);
    private Timer? MinuteurActualites;

    private string EtatActualites => ActualitesVues.Etat;

    /// <summary>
    /// Releve les actualites. Au TOUT PREMIER demarrage, aucun fichier partage
    /// n'existe encore : on y SEME les deux actualites dictees par le metier
    /// (fraude CNG, INPI HS) plutot que d'afficher une tuile vide. Sans cela,
    /// le widget ne montre rien tant que personne n'a redige, et personne ne
    /// redige tant que le widget ne montre rien.
    /// </summary>
    public void ReleveActualites()
    {
        var maintenant = DateTime.Now;
        var lu = Actualites.Lire(maintenant);
        if (lu.Source == SourceActualites.Aucune
            && Actualites.Ecrire(DepotActualites.Exemples(maintenant)))
        {
            lu = Actualites.Lire(maintenant);
        }
        ActualitesVues = lu;
    }

    // ================= Reglages d'une tuile =================
    // Un seul panneau ouvert a la fois, dans le corps de la tuile concernee.
    // Les reglages d'une tuile portent sur SON contenu (quelles alertes,
    // quelles mesures). Retirer la tuile de l'accueil n'est pas un reglage
    // de tuile : cela se fait dans les reglages generaux, dans la barre du
    // haut, seul endroit d'ou l'on peut aussi la faire revenir.
    private string? ReglagesTuile;

    private void BasculerReglages(string id)
        => ReglagesTuile = ReglagesTuile == id ? null : id;

    // ============ LES WIDGETS METIER (un par categorie de la Feuil2) ============

    /// <summary>La categorie du catalogue portee par une tuile « metier-... ».
    /// L'identifiant de tuile est stable et sans accent ; la categorie, elle,
    /// vient du catalogue metier.</summary>
    private static string MetierCategorie(string idTuile) => idTuile switch
    {
        "metier-arrivees"      => CatalogueAlertes.Arrivees,
        "metier-formalites"    => CatalogueAlertes.Formalites,
        "metier-notifications" => CatalogueAlertes.Notifications,
        "metier-das"           => CatalogueAlertes.Das,
        "metier-dca"           => CatalogueAlertes.Dca,
        "metier-corrections"   => CatalogueAlertes.Corrections,
        "metier-surveillances" => CatalogueAlertes.Surveillances,
        _                      => CatalogueAlertes.Divers
    };

    /// <summary>Les alertes de la categorie, dans l'ordre du catalogue metier.
    /// Volontairement NON filtrees par les reglages de la tuile « A traiter » :
    /// un widget de categorie montre SA categorie en entier.</summary>
    private AlerteGreffe[] MetierLignes(string categorie)
        => Alertes.Where(a => a.Categorie == categorie).ToArray();

    /// <summary>Ce que chaque widget metier a d'ouvert a droite, et ce qu'on a
    /// tape dans sa recherche. Une entree vide = panneau de droite ferme.</summary>
    private readonly Dictionary<string, string> MetierChoix = new();
    private readonly Dictionary<string, string> MetierFiltre = new();

    /// <summary>Les widgets metier DEPLOYES. La maquette du 17/09/2026 porte un
    /// bouton de deploiement dans l'en-tete du panneau de gauche : la tuile fait
    /// 6 colonnes sur 2 lignes, donc le tableau de droite n'y montre que quelques
    /// demandes. Deployer sort le widget de la grille pour lire la liste en grand.</summary>
    private readonly HashSet<string> MetierDeploye = new();

    private bool MetierEstDeploye(string idTuile) => MetierDeploye.Contains(idTuile);

    private void BasculerDeploiementMetier(string idTuile)
    {
        if (!MetierDeploye.Remove(idTuile)) MetierDeploye.Add(idTuile);
    }

    private AlerteGreffe? MetierAlerte(string idTuile, AlerteGreffe[] lignes)
    {
        if (lignes.Length == 0) return null;
        return MetierChoix.TryGetValue(idTuile, out var id)
            ? lignes.FirstOrDefault(a => a.Id == id) ?? lignes[0]
            : lignes[0];
    }

    /// <summary>
    /// Le clic sur une alerte du panneau de gauche. REPLIE, le widget n'a pas
    /// de place pour la liste : il se DEPLOIE (demande du 17/09/2026), et la
    /// liste montree est celle de l'alerte qu'on vient de choisir. Sans cela le
    /// clic ne produisait rien de visible -- il ne faisait que preparer un
    /// panneau qui n'existait pas encore.
    /// </summary>
    private void ChoisirMetier(string idTuile, string idAlerte)
    {
        MetierChoix[idTuile] = idAlerte;
        MetierMenu = null;
        if (!MetierEstDeploye(idTuile)) MetierDeploye.Add(idTuile);
    }

    private void FermerMetier(string idTuile)
    {
        MetierChoix[idTuile] = "";
    }

    /// <summary>
    /// La croix du panneau de droite. DEPLOYE, elle REDUIT le widget -- elle le
    /// remet dans la grille (demande du 17/09/2026) : retirer le tableau sans
    /// replier aurait laisse un widget plein ecran a moitie vide. Replie, il n'y
    /// a rien a reduire : elle ferme le panneau.
    /// </summary>
    private void ReduireMetier(string idTuile)
    {
        MetierMenu = null;
        if (MetierEstDeploye(idTuile)) { BasculerDeploiementMetier(idTuile); return; }
        FermerMetier(idTuile);
    }

    /// <summary>Le widget dont le menu « trois points » est ouvert. UN SEUL a la
    /// fois : deux menus ouverts sur deux widgets voisins se recouvriraient.</summary>
    private string? MetierMenu;

    private bool MetierMenuOuvert(string idTuile) => MetierMenu == idTuile;

    private void BasculerMenuMetier(string idTuile)
        => MetierMenu = MetierMenu == idTuile ? null : idTuile;

    /// <summary>L'alerte ouverte a droite d'un widget metier (ou de « A traiter
    /// BIS ») : celle qu'« Ouvrir le dossier » et « Ouvrir › » emmenent.</summary>
    private AlerteGreffe? MetierOuverte(string idTuile)
    {
        if (idTuile == "alertes-bis")
        {
            var choisie = MetierAlerte(idTuile, AlertesVues);
            return MetierChoix.TryGetValue(idTuile, out var c) && c.Length == 0 ? null : choisie;
        }
        var lignes = MetierLignes(MetierCategorie(idTuile));
        var ferme = MetierChoix.TryGetValue(idTuile, out var choix) && choix.Length == 0;
        return (ferme || !MetierEstDeploye(idTuile)) ? null : MetierAlerte(idTuile, lignes);
    }

    /// <summary>« Ouvrir le dossier » du menu : le MEME chemin que le clic sur
    /// une alerte, et le menu se referme -- sinon il resterait ouvert par-dessus
    /// l'ecran qui vient de s'ouvrir.</summary>
    private DemandeAccueil? OuvrirDepuisMenuMetier(string idTuile, AlerteGreffe? alerte)
    {
        MetierMenu = null;
        return alerte is null ? null : new DemandeAccueil(DemandeAccueil.OuvrirAlerte, Alerte: alerte);
    }

    /// <summary>« Rafraichir les compteurs » : relit les alertes et les mesures
    /// a la source, comme au demarrage. Le menu se referme pour que le nouveau
    /// compte soit visible.</summary>
    private void RafraichirMetier(string idTuile)
    {
        MetierMenu = null;
        ReleverAlertes();
        RelevePerf();
    }

    /// <summary>« Parametrer le widget » : ouvre le panneau de reglages de la
    /// tuile, celui de l'engrenage de son en-tete.</summary>
    private void ParametrerMetier(string idTuile)
    {
        MetierMenu = null;
        BasculerReglages(idTuile);
    }

    private void FiltrerMetier(string idTuile, string texte)
    {
        MetierFiltre[idTuile] = texte;
    }

    private string MetierFiltreDe(string idTuile)
        => MetierFiltre.TryGetValue(idTuile, out var f) ? f : "";

    /// <summary>Les lignes du panneau de droite, filtrees par la recherche du
    /// widget (numero de chrono ou societe).</summary>
    private DemandeAlerte[] MetierDemandes(string idTuile, AlerteGreffe alerte)
    {
        var filtre = MetierFiltreDe(idTuile).Trim();
        var lignes = PanneauMetier.Lignes(alerte);
        return filtre.Length == 0
            ? lignes
            : lignes.Where(l => l.Chrono.Contains(filtre, StringComparison.OrdinalIgnoreCase)
                             || l.Societe.Contains(filtre, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    // ================= Nouveautes =================
    /// <summary>
    /// Une nouveaute LIVREE. <paramref name="Module"/> est l'ecran ou elle vit,
    /// <paramref name="Pictogramme"/> le code de l'icone (voir IconeNouveaute),
    /// reconnue avant meme d'etre lue, et
    /// <paramref name="Action"/> le debut de phrase A L'IMPERATIF : on annonce ce
    /// que l'utilisateur PEUT FAIRE, pas ce qui a ete code.
    /// </summary>
    public sealed record Nouveaute(string Module, string Pictogramme, string Teinte,
        string Action, string Suite, string Detail);

    /// <summary>
    /// Les nouveautes de la version en cours, la plus utile en tete. Liste
    /// ECRITE A LA MAIN a chaque livraison : il n'y a pas de source a interroger,
    /// et une nouveaute se redige, elle ne se deduit pas d'un journal de commits.
    /// </summary>
    public static readonly Nouveaute[] Nouveautes =
    [
        new("MENU",     "boussole",     "vert",   "Cherchez par geste", "154 procedures",
            "Le menu ne liste plus des modules mais des GESTES : tapez « immatriculer », "
            + "« radier », « nantir » et la procedure remonte directement, sans passer par "
            + "l'arborescence. Les 154 procedures du greffe sont couvertes."),
        new("PROFIL",   "profil",       "bleu",   "Votre metier seul",  "le reste s'efface",
            "Votre profil metier (greffier, formaliste, comptable) masque les ecrans qui ne "
            + "vous concernent pas. Rien n'est supprime : le profil se change a tout moment "
            + "depuis votre nom, en haut a droite."),
        new("ACCUEIL",  "megaphone",    "or",     "Publiez une actualite", "vu par le greffe",
            "Le widget Actualites permet d'ecrire une annonce visible par tout le greffe sur "
            + "son accueil. Utile pour une consigne du jour ou une indisponibilite : elle "
            + "s'affiche jusqu'a sa date de fin."),
        new("AFFICHAGE","clair-sombre", "violet", "Cinq themes",        "choix retenu",
            "Clair, sombre, violet pilules, noir et orange ou argile et corail : le theme se "
            + "choisit dans la liste des reglages de l'entete. Le choix est retenu poste par "
            + "poste et reapplique au prochain demarrage.")
    ];

    // Le module de la nouveaute DEPLIEE, ou null quand tout est replie. Un seul
    // panneau ouvert a la fois : la tuile est basse, deux details ouverts la
    // feraient defiler au lieu de se lire d'un coup d'oeil.
    private string? NouveauteOuverte;

    private void BasculerNouveaute(string module)
        => NouveauteOuverte = NouveauteOuverte == module ? null : module;

    // --- Activite des utilisateurs : le journal des gestes de l'equipe, lu
    // dans JournalActions. Le filtre vit ICI et non dans accueil.json : c'est
    // une question du moment (« qu'a fait Claire ce matin ? »), pas un reglage
    // qu'on veut retrouver demain matin.
    private string FiltreActivite = string.Empty;

    private ActionAgent[] ActivitesVues => JournalActions.De(FiltreActivite);

    // =====================================================================
    // LES GESTES remontes par React (un seul rappel, « geste », pour les iles)
    // =====================================================================

    /// <summary>
    /// Applique le geste <paramref name="geste"/> et rend ce que Home doit faire
    /// ensuite hors de l'accueil (null : rien, la vue suffit). Un geste inconnu
    /// leve une exception : il n'est jamais ignore en silence.
    /// </summary>
    public DemandeAccueil? Agir(string geste, JsonElement valeur)
    {
        switch (geste)
        {
            // --- Barre du haut
            case "deplacer": return new(DemandeAccueil.Deplacer);
            case "reduire": return new(DemandeAccueil.Reduire);
            case "agrandir": return new(DemandeAccueil.Agrandir);
            case "fermer": return new(DemandeAccueil.Fermer);
            case "theme": ChoisirTheme(Texte(valeur)); return new(DemandeAccueil.ThemeChange, Theme);
            // --- Rond de profil
            case "profil": PanneauProfilOuvert = !PanneauProfilOuvert; return null;
            case "fermerProfil": PanneauProfilOuvert = false; return null;
            case "login": ChangerLogin(Exiger(valeur)); return new(DemandeAccueil.LoginChange, LoginCourant);
            // --- Menu de gauche
            case "menu": BasculerMenu(); return null;
            case "favorisOuverts": FavorisOuverts = !FavorisOuverts; return null;
            case "moment": OuvrirMoment(Exiger(valeur)); return null;
            case "sous":
            {
                var m = MenuAccueil.Moments.First(x => x.Titre == Champ(valeur, "moment"));
                BasculerSous(m, m.Sous.First(s => s.Titre == Champ(valeur, "sous")));
                return null;
            }
            case "ouvrir": return new(DemandeAccueil.OuvrirCode, Exiger(valeur));
            case "favori": BasculerFavori(Exiger(valeur)); return null;
            // --- Entete : panneau des tuiles
            case "panneauTuiles": BasculerPanneauTuiles(); return null;
            case "fermerPanneauTuiles": PanneauTuilesOuvert = false; return null;
            case "reinitialiser": ReinitialiserDisposition(); return null;
            case "disposition": AppliquerDisposition(Exiger(valeur)); return null;
            case "tuile": BasculerTuile(Exiger(valeur)); return null;
            // --- Recherche : la barre ouvre la palette, avec ce qui y est tape
            case "palette": return new(DemandeAccueil.OuvrirPalette, Texte(valeur) ?? string.Empty);
            // --- Chiffres du jour et « Proue »
            case "chiffre": BasculerChiffre(Entier(valeur)); return null;
            case "voirTuile": VoirTuile(Exiger(valeur)); return null;
            case "numero": return new(DemandeAccueil.OuvrirNumero, Exiger(valeur));
            case "alerte": return new(DemandeAccueil.OuvrirAlerte, Alerte: Trouver(Alertes, Exiger(valeur)));
            case "tache": return new(DemandeAccueil.OuvrirAlerte, Alerte: Trouver(Taches, Exiger(valeur)));
            // --- Grille
            case "poser":
                PoserTuile(Champ(valeur, "id"), ChampEntier(valeur, "colonne"), ChampEntier(valeur, "ligne"));
                return null;
            case "dimensionner":
                DimensionnerTuile(Champ(valeur, "id"), ChampEntier(valeur, "largeur"), ChampEntier(valeur, "hauteur"));
                return null;
            case "reglagesTuile": BasculerReglages(Exiger(valeur)); return null;
            // --- Corps des tuiles
            case "repli": BasculerRepliCategorie(Exiger(valeur)); return null;
            case "rediger": return new(DemandeAccueil.OuvrirCode, "PROC_ACTUALITES");
            case "activite": FiltreActivite = Texte(valeur) ?? string.Empty; return null;
            case "nouveaute": BasculerNouveaute(Exiger(valeur)); return null;
            case "metierDeployer": BasculerDeploiementMetier(Exiger(valeur)); return null;
            case "metierMenu": BasculerMenuMetier(Exiger(valeur)); return null;
            case "metierOuvrir":
            {
                var id = Exiger(valeur);
                return OuvrirDepuisMenuMetier(id, MetierOuverte(id));
            }
            case "metierRafraichir": RafraichirMetier(Exiger(valeur)); return null;
            case "metierParametrer": ParametrerMetier(Exiger(valeur)); return null;
            case "metierFiltre": FiltrerMetier(Champ(valeur, "id"), Champ(valeur, "texte")); return null;
            case "metierChoisir": ChoisirMetier(Champ(valeur, "id"), Champ(valeur, "alerte")); return null;
            case "metierReduire": ReduireMetier(Exiger(valeur)); return null;
            case "metierAlerte":
            {
                var ouverte = MetierOuverte(Exiger(valeur));
                return ouverte is null ? null : new(DemandeAccueil.OuvrirAlerte, Alerte: ouverte);
            }
            case "bisChoisir": MetierChoix["alertes-bis"] = Exiger(valeur); return null;
            // --- Reglages d'une tuile
            case "categorie": BasculerCategorie(Exiger(valeur)); return null;
            case "surveiller": BasculerAlerte(Exiger(valeur)); return null;
            case "niveau": ChoisirNiveauAlertes(Texte(valeur)); return null;
            case "tri": ChoisirTriAlertes(Texte(valeur)); return null;
            case "limite": ChoisirLimiteAlertes(Texte(valeur)); return null;
            case "detail": ReglerAlertes(r => r.AfficherDetail = !r.AfficherDetail); return null;
            case "mesure": BasculerMesure(Exiger(valeur)); return null;
            default:
                throw new InvalidOperationException($"Geste inconnu de l'accueil : « {geste} ».");
        }
    }

    private static AlerteGreffe Trouver(AlerteGreffe[] liste, string id)
        => liste.FirstOrDefault(a => a.Id == id)
           ?? throw new InvalidOperationException($"Alerte inconnue de l'accueil : {id}.");

    private static string? Texte(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => v.GetRawText()
    };

    private static string Exiger(JsonElement v)
        => Texte(v) ?? throw new InvalidOperationException("Geste de l'accueil sans valeur.");

    private static int Entier(JsonElement v)
        => v.ValueKind == JsonValueKind.Number ? v.GetInt32() : int.Parse(Exiger(v), CultureInfo.InvariantCulture);

    private static string Champ(JsonElement v, string nom)
        => v.TryGetProperty(nom, out var c) && Texte(c) is { } t
            ? t
            : throw new InvalidOperationException($"Geste de l'accueil sans « {nom} ».");

    private static int ChampEntier(JsonElement v, string nom)
        => v.TryGetProperty(nom, out var c) ? Entier(c)
           : throw new InvalidOperationException($"Geste de l'accueil sans « {nom} ».");

    // =====================================================================
    // LA VUE : ce que les iles affichent, calcule a chaque rendu de Home.
    // =====================================================================

    public VueAccueil Vue(ContexteAccueil ctx)
    {
        return new VueAccueil(Theme, Plan(ctx));
    }

    /// <summary>Le rond de profil (« Commandes rondes », 18/11/2026) : les initiales
    /// du login ; un clic deroule les comptes. Il est a droite du titre de
    /// l'accueil, et seulement la : la barre du haut ne porte plus que les onglets.
    /// C'est le selecteur des logins de DEMONSTRATION : branchee au serveur, l'agent est
    /// celui de la session Windows et n'en change pas -- pas de rond (SOC-001 T03).</summary>
    private ProfilVue? Profil()
    {
        if (SessionAgent.NomAgent is not null) return null;
        var c = CompteCourant;
        return new ProfilVue(PanneauProfilOuvert, c.Initiales,
            $"{c.Login} · {c.Profil} ({c.DomainesLisibles}) : changer de login", c.Login,
            PanneauProfilOuvert
                ? JeuDemo.Comptes.Select(x => new CompteVue(x.Login, x.Initiales, x.Profil, x.DomainesLisibles,
                                                            x.Login == LoginCourant)).ToArray()
                : []);
    }

    private VuePlanAccueil Plan(ContexteAccueil ctx)
    {
        // Les trois chiffres du jour, lus UNE fois : la phrase de l'entete et les
        // tuiles chiffrees disent donc toujours les memes nombres.
        var chiffreArrivees = ArriveesVues;
        var chiffreArriveesTotal = chiffreArrivees.Sum(a => a.Nombre);
        var chiffreEnTete = chiffreArrivees.OrderByDescending(a => a.Nombre).FirstOrDefault();
        var chiffreTraites = MesureNommee(MesuresPerf.LibelleTraites);
        var chiffreHorsDelai = MesureNommee(MesuresPerf.LibelleHorsDelai);
        var horsDelaiNombre = chiffreHorsDelai is null ? 0 : JeuDemo.DemandesHorsDelai().Length;

        var entete = new EnteteVue(DateDuJour, Utilisateur, SessionAgent.Greffe?.Libelle,
            new PhraseVue(chiffreHorsDelai is not null, horsDelaiNombre, chiffreArriveesTotal, chiffreTraites?.Valeur),
            PanneauTuilesOuvert,
            PanneauTuilesOuvert
                ? new PanneauTuilesVue(
                    Theme, ThemesRig.Tous.Select(t => new ThemeVue(t.Code, t.Libelle)).ToArray(),
                    Dispositions.Select(d => new DispositionVue(d.Id, d.Titre, d.Aide, Etat.Disposition == d.Id)).ToArray(),
                    Definitions.Where(TuileAutorisee)
                               .Select(def => new BasculeVue(def.Id, def.Titre, null, Pose(def.Id).Visible)).ToArray())
                : null,
            ctx.Recouvert ? null : Profil());

        var chiffres = new ChiffresVue(ChiffreDeplie, chiffreArriveesTotal,
            chiffreEnTete is null ? "rien a traiter" : $"{chiffreEnTete.Nombre} : {chiffreEnTete.Titre}",
            chiffreEnTete?.Titre, AnneauArrivees(chiffreArrivees),
            Math.Clamp(chiffreTraites?.Atteinte ?? 0, 0, 100), chiffreTraites?.Valeur ?? "-", chiffreTraites?.ObjectifTexte,
            horsDelaiNombre > 0, chiffreHorsDelai?.Part ?? 0, chiffreHorsDelai?.Valeur ?? "-", chiffreHorsDelai?.ObjectifTexte);

        ProueVue? proue = null;
        if (ChiffreDeplie is int deplie)
        {
            var resumes = TuilesEnResume.Select(def => new ResumeTuileVue(def.Id, def.Titre, ResumeTuile(def))).ToArray();
            proue = deplie switch
            {
                1 => new ProueVue(1, chiffreArriveesTotal.ToString(), TuileVisible("alertes"),
                                  chiffreArrivees.Select(Ligne).ToArray(), [], [], resumes),
                2 => new ProueVue(2, chiffreTraites?.Valeur ?? "-", TuileVisible("performance"),
                                  [], MesuresVues(), [], resumes),
                _ => HorsDelai(resumes)
            };
        }

        var tuiles = new List<TuileVue>();
        foreach (var def in Definitions)
        {
            var p = Pose(def.Id);
            if (!p.Visible || !TuileAutorisee(def)) continue;
            // Le z-index inline de la tuile (son rang d'empilement) cree un
            // CONTEXTE : un widget metier deploye, meme en position fixe et
            // z-index 89, restait donc enferme dans le rang de SA tuile et
            // passait derriere une tuile de rang superieur. Une tuile
            // deployee prend un rang qui bat toutes les autres.
            var devant = MetierEstDeploye(def.Id);
            var reglages = ReglagesTuile == def.Id;
            var corps = reglages ? null : def.Id;
            tuiles.Add(new TuileVue(def.Id, def.Titre, def.Maitresse, devant,
                p.Colonne, p.Ligne, p.Largeur, p.Hauteur, devant ? 96 : p.Ordre,
                !def.Id.StartsWith("metier-"), reglages, reglages ? ReglagesDe(def) : null,
                corps == "performance" ? MesuresVues() : null,
                corps == "alertes" ? AlertesTuile() : null,
                corps == "alertes-bis" ? AlertesBis() : null,
                corps == "taches" ? TachesVues.Select(Ligne).ToArray() : null,
                corps == "nouveautes" ? NouveautesVues() : null,
                corps == "actualites" ? ActualitesTuile() : null,
                corps == "activite" ? ActiviteTuile() : null,
                corps is not null && corps.StartsWith("metier-") ? Metier(def) : null));
        }

        return new VuePlanAccueil(MenuReduit, Menu(ctx), entete,
            chiffres, proue, [.. tuiles],
            TuileASignaler is { } signal ? new SignalVue(signal, NumeroSignal) : null);
    }

    private ProueVue HorsDelai(ResumeTuileVue[] resumes)
    {
        var horsDelai = JeuDemo.DemandesHorsDelai();
        return new ProueVue(3, horsDelai.Length.ToString(), TuileVisible("taches"), [], [],
            horsDelai.Select(d => new HorsDelaiVue(d.Dossier, d.Nom, JeuDemo.JoursDepuis(d), d.Numero, d.Creation))
                     .ToArray(),
            resumes);
    }

    private MenuVue Menu(ContexteAccueil ctx)
    {
        var favoris = EntreesFavorites()
            .Select(e => new FavoriVue(e.Code, e.Libelle, e.Detail, ctx.CodeOuvert == e.Code)).ToArray();
        var moments = MomentsVisibles.Select(moment =>
        {
            var ouvert = MomentsOuverts.Contains(moment.Titre);
            var sous = !ouvert ? [] : SousVisibles(moment).Select(s =>
            {
                var sousOuvert = SousEstOuvert(moment, s);
                var entrees = EntreesVisibles(s);
                return new SousVue(s.Titre, sousOuvert, entrees.Length, !sousOuvert ? [] : entrees
                    .Select(e => new EntreeVue(e.Code, e.Libelle, e.Detail, ctx.CodeOuvert == e.Code,
                                               Etat.EstFavori(e.Code)))
                    .ToArray());
            }).ToArray();
            return new MomentVue(moment.Titre, moment.Teinte, moment.Signe, NombreEcransVisibles(moment), ouvert, sous);
        }).ToArray();
        return new MenuVue(FavorisOuverts, favoris, moments);
    }

    private static AlerteLigneVue Ligne(AlerteGreffe a)
        => new(a.Id, a.Nombre, PanneauMetier.Palier(a.Nombre), a.Titre, a.Detail, a.LibelleCible);

    private MesureVue[] MesuresVues()
        => Mesures.Where(m => MesureActive(m.Libelle))
                  .Select(m => new MesureVue(m.Libelle, m.Valeur, m.Part, m.Statut, m.Objectif, m.ObjectifTexte, m.Atteinte))
                  .ToArray();

    private GroupeAlertesVue[] Groupes()
        => AlertesParCategorie.Select(g =>
        {
            var repliee = CategorieRepliee(g.Categorie);
            return new GroupeAlertesVue(g.Categorie, repliee, g.Lignes.Sum(x => x.Nombre),
                                        repliee ? [] : g.Lignes.Select(Ligne).ToArray());
        }).ToArray();

    // Le corps ne montre QUE les alertes surveillees : les interrupteurs
    // vivent dans les reglages de la tuile (bouton engrenage de son entete),
    // pas dans la lecture courante.
    private AlertesTuileVue AlertesTuile()
        => new(AlertesVues.Length == 0, ReglagesAlertes.AfficherDetail, Groupes());

    /// <summary>« A traiter BIS » : l'arbre de toutes les categories (comme
    /// « A traiter ») a gauche ; le clic sur une alerte la SELECTIONNE et montre
    /// ses demandes a droite, au lieu d'ouvrir l'ecran cible.</summary>
    private AlertesBisVue AlertesBis()
    {
        const string id = "alertes-bis";
        var ouverte = MetierOuverte(id);
        return new AlertesBisVue(AlertesVues.Length == 0, Groupes(), ouverte?.Id, PanneauDemandes(id, ouverte));
    }

    /// <summary>Le panneau de droite : les demandes de l'alerte ouverte. PARTAGE
    /// par les widgets metier et « A traiter BIS » -- un seul tableau.</summary>
    private PanneauDemandesVue PanneauDemandes(string idTuile, AlerteGreffe? ouverte)
        => new(ouverte is null ? null : Ligne(ouverte),
               ouverte is null ? [] : MetierDemandes(idTuile, ouverte)
                   .Select(d => new DemandeVue(d.Chrono, d.Societe, d.Siren, d.Type, d.RecuLe, d.Anciennete, d.Etat))
                   .ToArray(),
               MetierEstDeploye(idTuile) ? "Reduire le widget" : "Fermer le panneau");

    /// <summary>
    /// Le corps d'un widget metier : deux panneaux, comme la maquette metier du
    /// 17/09/2026. A GAUCHE la recherche, les gestes de la categorie, trois
    /// compteurs et la liste des alertes ; A DROITE les demandes de l'alerte
    /// selectionnee. Les BOUTONS DE GESTE sont de la maquette : ils nomment le
    /// geste, ils ne l'executent pas -- ils sont desactives, pas trompeurs.
    /// </summary>
    private MetierVue Metier(DefinitionTuile def)
    {
        var cat = MetierCategorie(def.Id);
        var lignes = MetierLignes(cat);
        // Demande du 17/09/2026 : replie, le widget ne montre QUE sa colonne
        // de gauche -- c'est la lecture courante. La liste des demandes est
        // une consultation, elle n'apparait qu'une fois le widget DEPLOYE.
        var ouverte = MetierOuverte(def.Id);
        var filtre = MetierFiltreDe(def.Id);
        var charge = lignes.Sum(a => a.Nombre);
        var deploye = MetierEstDeploye(def.Id);
        return new MetierVue(deploye, PanneauMetier.Registre(cat), PanneauMetier.Titre(cat), charge,
            PanneauMetier.Palier(charge), MetierMenuOuvert(def.Id), PanneauMetier.ActionsDe(cat),
            ouverte is null ? null : "Aller vers " + ouverte.LibelleCible, filtre, filtre.Trim().Length > 0,
            PanneauMetier.Urgents(lignes), PanneauMetier.Demat(lignes), lignes.Select(Ligne).ToArray(),
            ouverte?.Id, deploye ? PanneauDemandes(def.Id, ouverte) : null);
    }

    private NouveauteVue[] NouveautesVues()
        => Nouveautes.Select(n => new NouveauteVue(n.Module, n.Pictogramme, n.Teinte, n.Action, n.Suite, n.Detail,
                                                   NouveauteOuverte == n.Module)).ToArray();

    private ActualitesTuileVue ActualitesTuile()
        => new(ActualitesVues.Actifs.Select(b => new ActualiteVue(b.Fiche.Filet, b.Fiche.Libelle, b.Titre, b.Message,
                                                                    b.Auteur, b.Horodatage)).ToArray(),
               EtatActualites);

    private ActiviteTuileVue ActiviteTuile()
    {
        var vues = ActivitesVues;
        return new ActiviteTuileVue(FiltreActivite,
            JournalActions.Agents.Select(l => new AgentVue(l, JeuDemo.Compte(l).Nom)).ToArray(),
            vues.Length,
            JournalActions.ParAgentAujourdhui().Select(c => new CompteActiviteVue(c.Agent, c.Nombre)).ToArray(),
            vues.Select(a => new GesteVue(a.Agent, a.Verbe, a.Objet, a.Depuis)).ToArray());
    }

    private ReglagesVue ReglagesDe(DefinitionTuile def) => def.Id switch
    {
        "alertes" => new ReglagesVue("alertes",
            CatalogueAlertes.Categories
                .Select(c => new BasculeVue(c, c, "Afficher ou masquer toutes les alertes de " + c, CategorieActive(c)))
                .ToArray(),
            Alertes.Where(x => CategorieActive(x.Categorie))
                   .Select(a => new BasculeVue(a.Id, a.Titre, "Mene vers " + a.LibelleCible, AlerteActive(a.Id)))
                   .ToArray(),
            ReglagesAlertes.NiveauMini, ReglagesAlertes.Tri, ReglagesAlertes.Limite, ReglagesAlertes.AfficherDetail, []),
        "performance" => new ReglagesVue("performance", [], [], 0, "", 0, false,
            Mesures.Select(m => new BasculeVue(m.Libelle, m.Libelle, null, MesureActive(m.Libelle))).ToArray()),
        _ => new ReglagesVue("aucun", [], [], 0, "", 0, false, [])
    };
}
