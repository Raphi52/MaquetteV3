using System.Text.Json;
using Microsoft.JSInterop;

namespace RigV3Desktop.Pont;

/// <summary>Une action du processus v2 XACTION (menu « Actions »).</summary>
public sealed record ActionFicheVue(string Code, string Libelle);

/// <summary>Une entree du journal des modifications (bouton « Historique »), deja mise en forme.</summary>
public sealed record EntreeHistoVue(string Initiales, string Auteur, string Champ, string Element,
                                    string Avant, string Apres, string Quand, string Colonne);

/// <summary>Le bandeau du MODE TACHE.</summary>
/// <param name="Caches">Les champs que la tache masque (523 - les siens).</param>
public sealed record TacheFicheVue(string Code, string Titre, bool TousChamps, int Caches);

/// <summary>Le bandeau du VERROU KBIS.</summary>
/// <param name="Attente">Champs soumis a validation pour ce dossier (quand EnAttente).</param>
/// <param name="Brouillon">Champs modifies dans la demande en cours (quand EnEdition).</param>
public sealed record VerrouFicheVue(bool EnAttente, int Attente, bool EnEdition, int Brouillon);

/// <summary>Une etape de la frise a jauges.</summary>
/// <param name="Etat">complet, encours, partiel ou vide (suffixe de la classe rig-frise__pas--).</param>
/// <param name="Bloquants">Les obligatoires encore vides : « N requis » remplace « remplis / total ».</param>
/// <param name="Jauge">Largeur du filet de progression, en % entier (remplis * 100 / total).</param>
public sealed record PasFriseVue(int Index, string Titre, int Blocs, int Remplis, int Total,
                                 string Etat, bool Courant, int Bloquants, int Jauge);

/// <summary>La frise des etapes (affichee quand il y en a plus d'une, hors mode tache).</summary>
public sealed record FriseFicheVue(int Page, bool PrecedentInactif, bool SuivantInactif, PasFriseVue[] Pas);

/// <summary>Les trois vues de remplissage et le bouton de saisie guidee (hors mode tache).</summary>
/// <param name="Choix">0 renseignes, 1 a saisir, 2 tous (VueRemplie, VueVide, VueTout).</param>
public sealed record RemplissageVue(int Choix, int Remplis, int ASaisir, int Total, bool AucunChamp, bool Guide);

/// <summary>La SAISIE GUIDEE : un champ a la fois, au clavier.</summary>
/// <param name="Vide">L'etape n'a aucun champ a saisir.</param>
/// <param name="Rang">Le rang du champ courant, a partir de 1 (« champ 3 / 116 »).</param>
/// <param name="Avance">Largeur de la part qui avance, en % entier.</param>
/// <param name="Focus">Change chaque fois que le champ doit reprendre le focus (RendreFocus du .razor).</param>
public sealed record GuideVue(bool Vide, int Rang, int Total, int NbRequis, bool Requis, int PartRequis,
                              int Avance, string Etape, string Section, string Libelle, string Cle,
                              string Valeur, string Suivant, int Focus);

/// <summary>Un champ du formulaire. <paramref name="C"/> : son rang dans sa section (pour le geste « saisir »).</summary>
/// <param name="TitreColonne">L'infobulle de la cle : la COLONNE REELLE de la base quand elle est resolue.</param>
public sealed record ChampFicheVue(int C, string Libelle, string Processus, string Cle, string Valeur,
                                   string TitreColonne);

/// <summary>Un bloc repliable (une table v2). <paramref name="S"/> : son rang dans Sections.</summary>
public sealed record SectionFicheVue(int S, string Titre, string Table, ChampFicheVue[] Champs);

/// <summary>Ce que la fiche affiche.</summary>
/// <param name="Rappels">Les rappels que le DERNIER geste leve (toutAffiche, champModifie) ; vide hors geste.</param>
public sealed record VueFiche(string Session, string Domaine, int Champs, int Etapes, int Blocs, string Titre,
                              string Numero, string Sources, bool MenuActions, ActionFicheVue[] Actions,
                              bool Historique, ActionFicheVue? DerniereAction, EntreeHistoVue[] Entrees,
                              TacheFicheVue? Tache, VerrouFicheVue? Verrou, string Filtre,
                              FriseFicheVue? Frise, RemplissageVue? Remplissage, GuideVue? Guide,
                              bool LectureSeule, SectionFicheVue[] Sections, string[] Rappels);

/// <summary>Les parametres de la coquille, a chaque rendu de l'accueil.</summary>
public sealed record ParametresFiche(string Numero, string Domaine, string Titre, SectionInfoDossier[] Sections,
                                     string[] Sources, RecetteTache? Recette, int LigneCourante,
                                     AccueilEtat? Etat, bool Verrou);

/// <summary>
/// Pont de « Informations du dossier » (web/src/composants/FicheInformationsDossier.tsx).
///
/// Ecran A SESSION, ouverte par la COQUILLE (Components/FicheInformationsDossier.razor) :
/// la fiche ecrit dans l'etat VIVANT de l'accueil (AccueilEtat : trace des
/// modifications, journal), un objet qui ne traverse pas vers JavaScript. La
/// coquille ouvre la session avec ses parametres (<see cref="Ouvrir"/>), les lui
/// repasse a chaque rendu (<see cref="Parametrer"/>), et la ferme quand la fiche
/// quitte l'ecran. React lit la vue et remonte chaque geste par les deux methodes
/// [JSInvokable] ; les rappels que le geste leve (ChampModifie, ToutAffiche)
/// reviennent dans la vue, et React les remonte a la coquille.
/// </summary>
public static class FicheInformationsDossier
{
    internal static readonly SessionsEcran<SessionFicheInformationsDossier> Sessions = new();

    /// <summary>Appele par la coquille a son premier rendu.</summary>
    public static string Ouvrir(ParametresFiche parametres)
        => Sessions.Ouvrir(new SessionFicheInformationsDossier(parametres));

    /// <summary>
    /// Appele par la coquille a chaque rendu de l'accueil. Rend vrai si ce que la
    /// fiche affiche a pu changer depuis la derniere vue servie (un parametre, ou
    /// la trace du dossier ecrite ailleurs) : la coquille le signale alors a React.
    /// </summary>
    public static bool Parametrer(string session, ParametresFiche parametres)
        => Sessions.Lire(session).Parametrer(parametres);

    [JSInvokable("FicheInformationsDossier.Vue")]
    public static VueFiche Vue(string session) => Sessions.Lire(session).Vue(session, []);

    [JSInvokable("FicheInformationsDossier.Agir")]
    public static VueFiche Agir(string session, string geste, JsonElement valeur)
    {
        var s = Sessions.Lire(session);
        var rappels = s.Agir(geste, valeur);
        return s.Vue(session, rappels);
    }

    /// <summary>Appele par la coquille quand la fiche quitte l'ecran.</summary>
    public static void Fermer(string session) => Sessions.Fermer(session);
}

/// <summary>
/// L'etat de la fiche : le @code de FicheInformationsDossier.razor, DEPLACE TEL QUEL
/// (memes membres, memes noms, memes regles). Metier/ et AccueilEtat ne sont pas
/// modifies. Seuls changent les points de contact avec Blazor : un EventCallback
/// devient un rappel note dans la vue, et RendreFocus devient un compteur que
/// React suit pour rendre le focus au champ guide.
/// </summary>
public sealed class SessionFicheInformationsDossier
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---- Parametres de la coquille (les [Parameter] du .razor) ----
    private string Numero = "";
    private string Domaine = "Dossier RCS";
    private string Titre = InformationsDossier.LibelleNoeud;
    private SectionInfoDossier[] Sections = InformationsDossier.Sections;
    private string[] Sources = InformationsDossier.ProcessusSources;

    /// <summary>La tache en cours : elle restreint la fiche a ses seuls champs.</summary>
    private RecetteTache? Recette;

    /// <summary>La ligne saisie quand la tache en compte plusieurs (dirigeant 1, 2, 3...).</summary>
    private int LigneCourante = 1;

    /// <summary>Etat persistant : porte l'historique des modifications et le journal.</summary>
    private AccueilEtat? Etat;

    /// <summary>Verrou KBIS : actif pour « Informations du dossier » seulement.</summary>
    private bool Verrou;

    /// <summary>Les rappels leves par le geste en cours (les EventCallback du .razor).</summary>
    private readonly List<string> Rappels = new();

    /// <summary>Ce dont dependait la derniere vue servie (voir <see cref="Parametrer"/>).</summary>
    private Empreinte? Servie;

    internal SessionFicheInformationsDossier(ParametresFiche p) => Poser(p);

    private void Poser(ParametresFiche p)
    {
        Numero = p.Numero;
        Domaine = p.Domaine;
        Titre = p.Titre;
        Sections = p.Sections;
        Sources = p.Sources;
        Recette = p.Recette;
        LigneCourante = p.LigneCourante;
        Etat = p.Etat;
        Verrou = p.Verrou;
    }

    internal bool Parametrer(ParametresFiche p)
    {
        Poser(p);
        return Servie != Empreindre();
    }

    /// <summary>
    /// Tout ce que la vue lit HORS de la session : les parametres (par identite
    /// pour les objets, comme Blazor les recevait) et la trace du dossier, que
    /// d'autres ecrans ecrivent aussi (proposition d'un flux EDI).
    /// </summary>
    private sealed record Empreinte(string Numero, string Domaine, string Titre, object Sections, object Sources,
                                    RecetteTache? Recette, int LigneCourante, AccueilEtat? Etat, bool Verrou,
                                    object? Trace, int Ecritures);

    private Empreinte Empreindre()
    {
        List<ModificationChamp>? trace = null;
        Etat?.Modifications.TryGetValue(Numero, out trace);
        return new Empreinte(Numero, Domaine, Titre, Sections, Sources, Recette, LigneCourante, Etat, Verrou,
                             trace, trace?.Count ?? -1);
    }

    /// <summary>Le nombre d'etapes REELLEMENT couvertes par ces sections : la
    /// fiche sert aussi les formalites et les depots, dont les tables ne sont
    /// pas dans le regroupement du dossier (elles tiennent alors en une).</summary>
    private int NombreEtapes
        => Sections.Select(s => EtapesInfoDossier.Rang(s.Table)).Distinct().Count();

    /// <summary>
    /// La cle sous laquelle ce champ est ECRIT dans la trace. Un champ
    /// repetable de la tache courante porte son rang de ligne ; tous les
    /// autres gardent « TABLE.COLONNE » -- rien ne change hors tache.
    /// </summary>
    private string CleDuChamp(SectionInfoDossier bloc, ChampInfoDossier c)
    {
        var champ = Recette?.Champs.FirstOrDefault(
            x => x.Table == bloc.Table && x.Colonne == c.Colonne);
        return champ is { Repetable: true }
            ? RecetteTache.Cle(champ, LigneCourante)
            : $"{bloc.Table}.{c.Colonne}";
    }

    private bool MenuActions;
    private bool Historique;
    private ActionXaction? DerniereAction;

    private void BasculerHistorique()
    {
        Historique = !Historique;
        MenuActions = false;
    }

    private ModificationChamp[] Entrees()
        => Etat is not null && Etat.Modifications.TryGetValue(Numero, out var l)
            ? l.AsEnumerable().Reverse().ToArray()
            : Array.Empty<ModificationChamp>();

    private void Choisir(ActionXaction a)
    {
        MenuActions = false;
        DerniereAction = a;
        Etat?.Journaliser(Numero, $"Action {ActionsXaction.Processus} demandee : {a.Libelle} ({a.Code})");
    }

    /// <summary>
    /// Ce que le champ AFFICHE. Le dictionnaire local ne connait que ce qui a
    /// ete tape dans CETTE vue : a la reouverture d'une affaire, ou quand une
    /// valeur a ete ecrite ailleurs (proposition d'un flux EDI), il est vide
    /// et le champ paraissait vierge alors que la trace portait deja la
    /// valeur -- la tache se disait complete devant un formulaire blanc. La
    /// trace fait donc foi des que la vue n'a rien de plus recent.
    /// </summary>
    private string ValeurAffichee(string cle)
        => Valeurs.TryGetValue(cle, out var v) ? v
           : Etat?.DerniereValeur(Numero, cle) ?? string.Empty;

    // La trace nomme la LIGNE : « ... > Dirigeants (Dirigeant 2) ». Sans
    // cela, trois nominations le meme jour produisaient trois lignes
    // d'historique impossibles a distinguer.
    private void Modifier(SectionInfoDossier bloc, ChampInfoDossier c, string cle, string apres)
    {
        var avant = ValeurAffichee(cle);
        Valeurs[cle] = apres;
        var ou = $"{Titre} > {bloc.Titre}";
        if (Recette is not null && cle.Contains('[') && Recette.ARepetition)
            ou += $" ({Recette.LibelleLigne} {LigneCourante})";
        Etat?.NoterModification(Numero, new ModificationChamp(DateTime.Now, Environment.UserName,
            ou, c.Libelle, cle, avant, apres));
        // La vue qui porte la tache doit recompter ses champs renseignes :
        // sans ce signal, le bouton « Terminer la tache » resterait grise.
        Rappels.Add("champModifie");
    }

    private bool EnEdition;
    // Brouillon de la demande : la valeur en vigueur n'est PAS ecrasee tant
    // qu'un responsable n'a pas valide (hors maquette).
    private readonly Dictionary<string, string> Brouillon = new();
    // Demandes en attente par dossier, pour la session de l'application : elles
    // survivent au changement de noeud (la fiche est recreee).
    private static readonly Dictionary<string, int> Attentes = new();
    private bool EnAttente => Attentes.ContainsKey(Numero);

    private void Saisir(SectionInfoDossier bloc, ChampInfoDossier c, string cle, string apres)
    {
        if (!Verrou) { Modifier(bloc, c, cle, apres); return; }
        if (!EnEdition) return;
        if (apres == ValeurAffichee(cle)) Brouillon.Remove(cle); else Brouillon[cle] = apres;
    }

    private void AnnulerEdition() { Brouillon.Clear(); EnEdition = false; }

    private void EnvoyerDemande()
    {
        Attentes[Numero] = Brouillon.Count;
        Brouillon.Clear();
        EnEdition = false;
    }

    private static string Initiales(string auteur)
        => new string(auteur.Split('.', ' ', '_').Where(x => x.Length > 0)
                            .Take(2).Select(x => char.ToUpperInvariant(x[0])).ToArray());

    private string Filtre = "";
    private readonly Dictionary<string, string> Valeurs = new();

    /// <summary>Les sections qui ont au moins un champ retenu par le filtre.</summary>
    private (SectionInfoDossier Section, ChampInfoDossier[] Champs)[] SectionsVisibles()
        => Sections.Select(s => (Section: s, Champs: s.Champs.Where(c => Garde(s, c)).ToArray()))
                   .Where(x => x.Champs.Length > 0)
                   .ToArray();

    private int Page;

    /// <summary>La SAISIE GUIDEE : un champ a la fois, au clavier.</summary>
    private bool Guide;
    private int PasGuide;
    private string SaisieGuide = string.Empty;
    /// <summary>RendreFocus du .razor : chaque increment rend le focus au champ guide.</summary>
    private int Focus;

    private void BasculerGuide()
    {
        Guide = !Guide;
        PasGuide = 0;
        SaisieGuide = string.Empty;
        if (Guide) Focus++;
    }

    /// <summary>Tous les champs de l'etape, a plat, dans l'ordre des blocs.</summary>
    private (SectionInfoDossier Section, ChampInfoDossier Champ)[] ChampsGuide(EtapeInfoDossier etape)
        => Sections.Where(s => etape.Tables.Contains(s.Table))
                   .SelectMany(s => s.Champs.Where(c => GardeTache(s, c))
                                            .Select(c => (Section: s, Champ: c)))
                   .OrderByDescending(x => EstRequisActif(x.Section, x.Champ))
                   .ToArray();

    /// <summary>Vrai quand ce bloc porte deja au moins une valeur. Un bloc
    /// VIERGE ne reclame pas ses obligatoires : sinon un dossier personne
    /// morale exigerait un patronyme d'inscrit personne physique, et un
    /// dossier sans dirigeant une fonction de dirigeant.</summary>
    private bool BlocEntame(SectionInfoDossier s)
        => s.Champs.Any(c => EstRempli(s, c));

    /// <summary>Un champ obligatoire QUI S'APPLIQUE a ce dossier : la colonne
    /// est NOT NULL en base (ChampsRequisDossier), elle n'est pas ecrite par
    /// le systeme, et son bloc est actif.</summary>
    private bool EstRequisActif(SectionInfoDossier s, ChampInfoDossier c)
        => ChampsRequisDossier.EstRequis(s.Table, c.Colonne)
           && (!ChampsRequisDossier.BlocConditionnel(s.Table) || BlocEntame(s));

    /// <summary>Les obligatoires encore VIDES d'une etape : ce qui bloque.</summary>
    private int RequisManquants((SectionInfoDossier Section, ChampInfoDossier[] Champs)[] blocs)
        => blocs.Sum(b => b.Section.Champs.Count(
               c => EstRequisActif(b.Section, c) && !EstRempli(b.Section, c)));

    /// <summary>La touche frappee dans le champ guide (ce que KeyboardEventArgs portait).</summary>
    private sealed record Touche(string Key, bool ShiftKey, string Saisie);

    /// <summary>Entree passe au suivant, Maj+Entree revient, Echap sort.</summary>
    private void ClavierGuide(Touche ev, SectionInfoDossier s, ChampInfoDossier c, string cle, int total)
    {
        if (ev.Key == "Escape") { BasculerGuide(); return; }
        if (ev.Key != "Enter") return;
        if (ev.ShiftKey) { PasGuide = Math.Max(0, PasGuide - 1); SaisieGuide = string.Empty; Focus++; return; }
        ValiderGuide(s, c, cle, total);
    }

    /// <summary>Ecrit la valeur tapee (si elle a change) puis avance d'un champ.</summary>
    private void ValiderGuide(SectionInfoDossier s, ChampInfoDossier c, string cle, int total)
    {
        if (SaisieGuide.Length > 0 && SaisieGuide != ValeurAffichee(cle))
            Modifier(s, c, cle, SaisieGuide);
        SaisieGuide = string.Empty;
        PasGuide = Math.Min(total - 1, PasGuide + 1);
        Focus++;
    }

    /// <summary>Les trois vues de remplissage (voir .rig-infos__vue).</summary>
    public const int VueRemplie = 0;
    public const int VueVide = 1;
    public const int VueTout = 2;

    /// <summary>La vue courante. On ouvre sur les champs RENSEIGNES : afficher
    /// d'emblee les 523 champs (dont ~480 vides) etait le vrai defaut de cette
    /// fiche -- un mur de cases avant toute lecture.</summary>
    private int Remplissage = VueRemplie;

    /// <summary>Vrai quand ce champ porte deja une valeur (vue ou trace).</summary>
    private bool EstRempli(SectionInfoDossier s, ChampInfoDossier c)
        => ValeurAffichee(CleDuChamp(s, c)).Length > 0;

    /// <summary>Avancement d'une etape : champs renseignes sur total.</summary>
    private (int Remplis, int Total) Avancement((SectionInfoDossier Section, ChampInfoDossier[] Champs)[] blocs)
    {
        var total = 0; var remplis = 0;
        foreach (var b in blocs)
            foreach (var c in b.Section.Champs)
            {
                total++;
                if (EstRempli(b.Section, c)) remplis++;
            }
        return (remplis, total);
    }

    private int NombreTotal => Sections.Sum(s => s.Champs.Length);
    private int NombreRemplis => Sections.Sum(s => s.Champs.Count(c => EstRempli(s, c)));

    /// <summary>Vrai quand l'utilisateur a rouvert la fiche entiere depuis la tache.</summary>
    private bool TousChamps;

    /// <summary>Un clic sur « Afficher tous les champs » : il se COMPTE (rappel
    /// ToutAffiche, mesure de qualite de la recette -- chaque clic dit qu'elle est
    /// incomplete).</summary>
    private void BasculerTousChamps()
    {
        TousChamps = !TousChamps;
        Page = 0;
        if (TousChamps) Rappels.Add("toutAffiche");
    }

    private bool Garde(SectionInfoDossier s, ChampInfoDossier c)
        => GardeTache(s, c)
           && GardeRemplissage(s, c)
           && (Filtre.Length == 0
               || c.Libelle.Contains(Filtre, StringComparison.OrdinalIgnoreCase)
               || c.Colonne.Contains(Filtre, StringComparison.OrdinalIgnoreCase));

    /// <summary>La vue de remplissage retenue. Le mode tache et un filtre texte
    /// la neutralisent : on cherche alors un champ PRECIS, rempli ou non.</summary>
    private bool GardeRemplissage(SectionInfoDossier s, ChampInfoDossier c)
        => ModeTache || Filtre.Length > 0 || Remplissage == VueTout
           || (Remplissage == VueRemplie) == EstRempli(s, c);

    /// <summary>Vrai quand la fiche est restreinte aux champs de la tache.</summary>
    private bool ModeTache => Recette is not null && !TousChamps;

    private bool GardeTache(SectionInfoDossier s, ChampInfoDossier c)
        => Recette is null || TousChamps || Recette.GardeChamp(s.Table, c.Colonne);

    // ------------------------------------------------------------------
    // Le balisage du .razor, en donnees : ce qui etait calcule pendant le
    // rendu (groupes d'etapes, page bornee, champ guide courant) l'est ici,
    // dans le meme ordre.
    // ------------------------------------------------------------------

    private sealed record Rendu(
        (SectionInfoDossier Section, ChampInfoDossier[] Champs)[] Visibles,
        (EtapeInfoDossier Etape, (SectionInfoDossier Section, ChampInfoDossier[] Champs)[] Blocs)[] Groupes,
        int Page,
        (SectionInfoDossier Section, ChampInfoDossier[] Champs)[] Affichees);

    /// <summary>
    /// ETAPES NOMMEES (EtapesInfoDossier) : les 31 tables v2 sont regroupees en
    /// six etapes metier. On affiche UNE etape a la fois -- mais TOUS ses blocs
    /// empiles. Un filtre actif montre tout ce qui correspond, toutes etapes
    /// confondues ; en mode tache, les champs demandes tiennent ENSEMBLE.
    /// </summary>
    private Rendu Calculer()
    {
        var visibles = SectionsVisibles();
        var groupes = visibles
            .GroupBy(v => EtapesInfoDossier.Rang(v.Section.Table))
            .OrderBy(g => g.Key)
            .Select(g => (Etape: EtapesInfoDossier.Etapes[g.Key], Blocs: g.ToArray()))
            .ToArray();
        var page = Math.Clamp(Page, 0, Math.Max(0, groupes.Length - 1));
        var etalees = Filtre.Length > 0 || ModeTache;
        var affichees = etalees || groupes.Length == 0
            ? visibles
            : groupes[page].Blocs;
        return new Rendu(visibles, groupes, page, affichees);
    }

    /// <summary>Le champ guide courant, tel que le .razor le calculait au rendu.</summary>
    private (SectionInfoDossier Section, ChampInfoDossier Champ)[] PasDuGuide(Rendu r)
        => ChampsGuide(r.Groupes[r.Page].Etape);

    private bool GuideAffiche(Rendu r) => Guide && !ModeTache && r.Groupes.Length > 0;

    internal VueFiche Vue(string session, string[] rappels)
    {
        Servie = Empreindre();
        var r = Calculer();
        var historique = Historique
            ? Entrees().Select(m => new EntreeHistoVue(Initiales(m.Auteur), m.Auteur, m.Champ, m.Element,
                                                       m.Avant.Length == 0 ? "(vide)" : m.Avant,
                                                       m.Apres.Length == 0 ? "(vide)" : m.Apres,
                                                       m.Quand.ToString("dd/MM/yyyy HH:mm"), m.Colonne)).ToArray()
            : [];

        // MODE TACHE : la recette declare les champs a modifier ; la fiche ne
        // montre qu'eux. Le bouton rouvre les 523 champs SANS fermer la tache.
        var tache = Recette is null ? null
            : new TacheFicheVue(Recette.Code, Recette.Titre, TousChamps,
                                Sections.Sum(x => x.Champs.Length) - Recette.Champs.Length);

        // VERROU KBIS (maquette du 18/09/2026, agent SANS droits).
        var verrou = !Verrou ? null
            : new VerrouFicheVue(EnAttente, EnAttente ? Attentes[Numero] : 0, EnEdition, Brouillon.Count);

        // FRISE A JAUGES, la meme que l'ecran de saisie d'une formalite.
        FriseFicheVue? frise = null;
        if (r.Groupes.Length > 1 && !ModeTache)
        {
            var pas = new PasFriseVue[r.Groupes.Length];
            for (var i = 0; i < r.Groupes.Length; i++)
            {
                var g = r.Groupes[i];
                var (remplis, total) = Avancement(g.Blocs);
                var courant = i == r.Page && Filtre.Length == 0;
                var etat = total > 0 && remplis == total ? "complet"
                         : courant ? "encours"
                         : remplis > 0 ? "partiel" : "vide";
                pas[i] = new PasFriseVue(i, g.Etape.Titre, g.Blocs.Length, remplis, total, etat, courant,
                                         RequisManquants(g.Blocs), total == 0 ? 0 : remplis * 100 / total);
            }
            frise = new FriseFicheVue(r.Page, r.Page == 0 || Filtre.Length > 0,
                                      r.Page == r.Groupes.Length - 1 || Filtre.Length > 0, pas);
        }

        // Le VIDE est un choix, plus un mur : par defaut on ne montre que les
        // champs RENSEIGNES (41 sur 523 dans un dossier courant).
        var remplissage = ModeTache ? null
            : new RemplissageVue(Remplissage, NombreRemplis, NombreTotal - NombreRemplis, NombreTotal,
                                 r.Visibles.Length == 0, Guide);

        GuideVue? guide = null;
        SectionFicheVue[] sections = [];
        if (GuideAffiche(r))
        {
            var pas = PasDuGuide(r);
            var i = Math.Clamp(PasGuide, 0, Math.Max(0, pas.Length - 1));
            if (pas.Length == 0)
            {
                guide = new GuideVue(true, 0, 0, 0, false, 0, 0, "", "", "", "", "", "", Focus);
            }
            else
            {
                var courant = pas[i];
                var cleG = CleDuChamp(courant.Section, courant.Champ);
                var suivant = i + 1 < pas.Length ? pas[i + 1].Champ.Libelle : "fin de l'etape";
                // PARCOURS REQUIS D'ABORD : ChampsGuide met en tete les champs
                // obligatoires. La jauge se coupe donc en deux : segment ROUGE
                // tant qu'on est dans les obligatoires, gris pour les facultatifs.
                var nbRequis = pas.Count(x => EstRequisActif(x.Section, x.Champ));
                var courantRequis = EstRequisActif(courant.Section, courant.Champ);
                guide = new GuideVue(false, i + 1, pas.Length, nbRequis, courantRequis, nbRequis * 100 / pas.Length,
                                     (i + 1) * 100 / pas.Length, r.Groupes[r.Page].Etape.Titre,
                                     courant.Section.Titre, courant.Champ.Libelle, cleG, ValeurAffichee(cleG),
                                     suivant, Focus);
            }
        }
        else
        {
            sections = r.Affichees.Select(vu => new SectionFicheVue(
                Array.IndexOf(Sections, vu.Section), vu.Section.Titre, vu.Section.Table,
                vu.Champs.Select(c =>
                {
                    // Un champ REPETABLE de la tache porte le rang de la ligne
                    // saisie : sans lui, le 2e dirigeant ecraserait le 1er.
                    var cle = CleDuChamp(vu.Section, c);
                    // En infobulle, la COLONNE REELLE de la base -- « INSPP_PATRONYME »
                    // et non « PATRONYME » (ColonnesRig).
                    var reelle = ColonnesRig.Resoudre(vu.Section.Table, c.Colonne);
                    return new ChampFicheVue(
                        Array.IndexOf(vu.Section.Champs, c), c.Libelle, c.Processus, cle,
                        Verrou && Brouillon.TryGetValue(cle, out var prop) ? prop : ValeurAffichee(cle),
                        reelle.Resolue ? $"En base : {reelle}" : cle);
                }).ToArray())).ToArray();
        }

        return new VueFiche(
            session, Domaine, Sections.Sum(s => s.Champs.Length), NombreEtapes, Sections.Length, Titre,
            Numero, string.Join(", ", Sources), MenuActions,
            MenuActions ? ActionsXaction.Liste.Select(a => new ActionFicheVue(a.Code, a.Libelle)).ToArray() : [],
            Historique, DerniereAction is null ? null : new ActionFicheVue(DerniereAction.Code, DerniereAction.Libelle),
            historique, tache, verrou, Filtre, frise, remplissage, guide,
            Verrou && !EnEdition, sections, rappels);
    }

    /// <summary>Un champ du formulaire, nomme par son rang de section et son rang dans la section.</summary>
    private sealed record Champ(int S, int C, string Valeur);

    /// <summary>
    /// Un geste de l'ecran (un @onclick, @onchange ou @onkeydown du .razor).
    /// Rend les rappels qu'il leve. Un geste inconnu leve une exception : jamais ignore.
    /// </summary>
    internal string[] Agir(string geste, JsonElement valeur)
    {
        Rappels.Clear();
        switch (geste)
        {
            case "menuActions": MenuActions = !MenuActions; break;
            case "choisir":
                var code = valeur.GetString();
                Choisir(ActionsXaction.Liste.FirstOrDefault(a => a.Code == code)
                        ?? throw new InvalidOperationException($"Action XACTION inconnue : {code}."));
                break;
            case "historique": BasculerHistorique(); break;
            case "tousChamps": BasculerTousChamps(); break;
            case "demanderModification": EnEdition = true; break;
            case "annulerEdition": AnnulerEdition(); break;
            case "envoyerDemande": EnvoyerDemande(); break;
            case "filtre": Filtre = valeur.GetString() ?? ""; break;
            case "etape": Page = valeur.GetInt32(); Filtre = string.Empty; break;
            case "page": Page = valeur.GetInt32(); break;
            case "remplissage": Remplissage = valeur.GetInt32(); break;
            case "guide": BasculerGuide(); break;
            case "saisir":
            {
                var ch = valeur.Deserialize<Champ>(Json)!;
                var bloc = Sections[ch.S];
                var c = bloc.Champs[ch.C];
                Saisir(bloc, c, CleDuChamp(bloc, c), ch.Valeur ?? string.Empty);
                break;
            }
            case "clavierGuide":
            {
                var ev = valeur.Deserialize<Touche>(Json)!;
                SaisieGuide = ev.Saisie ?? string.Empty;
                if (ChampGuideCourant() is { } g) ClavierGuide(ev, g.Section, g.Champ, g.Cle, g.Total);
                break;
            }
            case "validerGuide":
            {
                SaisieGuide = valeur.GetString() ?? string.Empty;
                if (ChampGuideCourant() is { } g) ValiderGuide(g.Section, g.Champ, g.Cle, g.Total);
                break;
            }
            case "precedentGuide":
                if (ChampGuideCourant() is { } p) PasGuide = p.Index - 1;
                break;
            default:
                throw new InvalidOperationException($"Geste inconnu de la fiche : {geste}.");
        }
        return Rappels.ToArray();
    }

    /// <summary>Le champ guide AFFICHE (celui que le geste vise), ou null hors saisie guidee.</summary>
    private (SectionInfoDossier Section, ChampInfoDossier Champ, string Cle, int Total, int Index)? ChampGuideCourant()
    {
        var r = Calculer();
        if (!GuideAffiche(r)) return null;
        var pas = PasDuGuide(r);
        if (pas.Length == 0) return null;
        var i = Math.Clamp(PasGuide, 0, pas.Length - 1);
        return (pas[i].Section, pas[i].Champ, CleDuChamp(pas[i].Section, pas[i].Champ), pas.Length, i);
    }
}
