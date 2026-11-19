using System.Text.Json;

namespace RigV3Desktop.Pont;

// ============================================================
// LE PLAN DE TRAVAIL (barre d'onglets, ecran de procedure, visionneuse
// d'affaire, saisie guidee, modales) : son ETAT et ses REGLES, deplaces TELS
// QUELS du @code de Components/Pages/Home.razor (lot 2 du plan « finir le
// passage a React », 19/11/2026). Memes membres, memes noms : seules les
// attaches a Blazor ont change (module JS, references d'element, evenements
// du pointeur, StateHasChanged).
//
// Le rendu est fait par React : web/src/composants/Application.tsx monte
// l'application entiere (accueil du lot 1 compris) et web/src/plan/ porte le
// balisage du plan de travail. Home.razor n'est plus qu'une COQUILLE qui tient
// UNE session, monte l'application avec la vue calculee ici (Vue) et lui
// renvoie chaque geste (Agir, Pont/PlanTravailVue.cs).
//
// Pourquoi une session tenue par Home et pas ouverte par React : elle porte
// des objets VIVANTS -- l'etat de l'accueil (AccueilEtat), les fenetres WPF
// sorties (FenetreVue, FenetreDocument), le document partage -- qui ne
// traversent pas vers JavaScript, et ses minuteurs doivent redessiner la page
// sans geste de l'utilisateur (Change).
// ============================================================

/// <summary>
/// Le @code de Home.razor, deplace. Une session par page : la fenetre
/// principale, et une par onglet sorti dans sa propre fenetre (ModeFenetreVue).
/// </summary>
public sealed partial class SessionPlanTravail : IAsyncDisposable
{
    /// <param name="fenetre">Les commandes de la fenetre principale (absentes dans un banc).</param>
    /// <param name="partage">Le document montre par les fenetres detachees (absent dans un banc).</param>
    /// <param name="modeFenetreVue">Vrai dans la fenetre d'un onglet sorti (FenetreVue).</param>
    /// <param name="vueInitiale">Le code de l'ecran a ouvrir d'emblee dans cette fenetre-la.</param>
    public SessionPlanTravail(FenetreInterop? fenetre, DocumentPartage? partage,
                              bool modeFenetreVue = false, string? vueInitiale = null)
    {
        Fenetre = fenetre;
        Partage = partage;
        ModeFenetreVue = modeFenetreVue;
        VueInitiale = vueInitiale;
    }

    private readonly FenetreInterop? Fenetre;
    private readonly DocumentPartage? Partage;

    /// <summary>Le code de l ecran a ouvrir d emblee. Rempli par FenetreVue
    /// quand la page est celle d un onglet SORTI dans sa propre fenetre.</summary>
    public string? VueInitiale { get; }

    /// <summary>Vrai dans une fenetre d onglet arrache : plus de barre de titre
    /// maison ni de menu, la fenetre ne montre QUE l ecran arrache -- et elle
    /// ne reecrit pas l agencement global, sinon la derniere fenetre fermee
    /// ecraserait la disposition de la fenetre principale.</summary>
    public bool ModeFenetreVue { get; }

    /// <summary>Quelque chose a change SANS geste de la page (minuteur de
    /// l'accueil, fenetre sortie refermee, etoile allumee dans une autre
    /// fenetre) : la coquille redessine.</summary>
    public event Action? Change;

    /// <summary>
    /// Rejoue un changement venu d'ailleurs (fermeture d'une fenetre WPF, autre
    /// fenetre) sur le fil de la page, puis redessine. La coquille le branche sur
    /// son InvokeAsync, comme le .razor le faisait lui-meme ; sans coquille (un
    /// banc), le changement s'applique tout de suite.
    /// </summary>
    public Action<Action> Planifier { get; set; }
        = action => action();

    /// <summary>Compte les glisser d'onglet que le C# a termines lui-meme (onglet
    /// arrache en cours de geste) : la page remet alors l'onglet a sa place
    /// (finirGlisseOnglet de rig-accueil.js).</summary>
    private int FinsDeGlisse;

    // ---------------------------------------------------------------
    // Donnees de demonstration : la charte se juge sur du contenu reel.
    // ---------------------------------------------------------------
    // Le login connecte : son niveau d'acces decide des dossiers visibles.
    // Il vit dans la session de l'accueil (Pont/Accueil.cs), qui le change par
    // le rond de profil ; le plan de travail le relit ici.
    private CompteRig CompteCourant => Accueil.CompteCourant;
    private string Utilisateur => Accueil.Utilisateur;
    private string[] Domaines => Accueil.Domaines;

    /// <summary>Texte de la barre de recherche au-dessus de l'arbre du dossier.</summary>
    private string FiltreDeLArbre { get; set; } = string.Empty;

    // Le compte affiche en haut de l'ecran ne porte que sur les branches
    // que le login courant a le droit de voir.
    private IReadOnlyList<NoeudGed> DossiersVisibles
        => AccesGed.EnfantsVisibles(Arbre, Domaines, AjoutsArbre, MasquesArbre);

    /// <summary>Un ecran plein recouvre l'accueil et le titre : un ecran de
    /// procedure (.rig-proc) ou une affaire ouverte (.rig-vged). Le rond de
    /// profil passe alors dans le bandeau, seul reste visible au-dessus.</summary>
    private bool AccueilRecouvert => ProcOuverte is not null || DossierOuvert is not null;

    /// <summary>Le login vient de changer (rond de profil de l'accueil) : un noeud
    /// devenu invisible ne doit pas rester selectionne ni ouvert.</summary>
    private void ApresChangementLogin()
    {
        if (NoeudSelectionne is not null
            && !ArbreVisible().Any(n => ReferenceEquals(n, NoeudSelectionne)))
        {
            NoeudSelectionne = null;
            ApercuNoeud = null;
            GuideVisible = false;
        }
    }

    // ---------------------------------------------------------------
    // Validation hierarchique : un login sans ecriture directe SOUMET ses
    // modifications, un superieur les valide. Voir Metier/ValidationModifications.cs.
    // ---------------------------------------------------------------
    private sealed class DemandeModification
    {
        public required ModificationProposee Info { get; set; }
        public required Action Appliquer { get; init; }
    }

    private readonly List<DemandeModification> Validations = new();
    private int NumeroValidation;

    /// <summary>
    /// Passe le geste par la file de validation quand le login n'a pas le
    /// droit d'ecrire en direct. Rend true si le geste a ete SOUMIS (donc
    /// PAS applique) : l'appelant s'arrete alors la.
    /// </summary>
    private bool Soumettre(string geste, string cible, string detail, Action appliquer)
    {
        if (CompteCourant.PeutModifierDirect) return false;
        Validations.Add(new DemandeModification
        {
            Info = new ModificationProposee(++NumeroValidation, geste, cible, detail,
                                            CompteCourant.Nom, DateTime.Now),
            Appliquer = appliquer,
        });
        return true;
    }

    private void ValiderModification(DemandeModification d)
    {
        if (!CompteCourant.PeutValider || !d.Info.EstEnAttente) return;
        d.Appliquer();
        d.Info = d.Info with { Etat = EtatModification.Validee, Decideur = CompteCourant.Nom };
    }

    private void RejeterModification(DemandeModification d)
    {
        if (!CompteCourant.PeutValider || !d.Info.EstEnAttente) return;
        d.Info = d.Info with { Etat = EtatModification.Rejetee, Decideur = CompteCourant.Nom };
    }

    /// <summary>Les noeuds que le login courant peut voir, ajouts compris.</summary>
    private IEnumerable<NoeudGed> ArbreVisible()
    {
        var pile = new Stack<NoeudGed>();
        pile.Push(Arbre);
        while (pile.Count > 0)
        {
            var n = pile.Pop();
            yield return n;
            foreach (var e in AccesGed.EnfantsVisibles(n, Domaines, AjoutsArbre, MasquesArbre)) pile.Push(e);
        }
    }

    // ================= MODE TACHE (task driven) =================
    // Une alerte n'est pas une ligne a lire : c'est l'INSTANCE d'une recette
    // (Metier/RecetteTache.cs). Tant qu'une tache est ouverte, l'arbre et la
    // fiche se replient sur ce que la recette declare. Rien n'est supprime :
    // « Afficher tout le dossier » remet la vue entiere, et le nombre de
    // clics dit si la recette est incomplete.
    private RecetteTache? RecetteActive;

    /// <summary>L affaire sur laquelle la tache repliee est ouverte.</summary>
    private string? AffaireDeLaTache;
    private bool ToutLeDossier;

    /// <summary>Combien de fois l'utilisateur a du rouvrir la vue entiere.
    /// Mesure de qualite de la recette : beaucoup de clics = recette trop
    /// etroite. Journalise pour que le comptage sorte de l'ecran.</summary>
    private int ClicsToutAfficher;

    /// <summary>L'effet de la checklist sur lequel l'arbre est vise, s'il y en a un.</summary>
    private string EffetCible = "";

    /// <summary>
    /// La recette appliquee A L'ARBRE : nulle quand tout est affiche. Quand
    /// une ligne de la checklist est visee, la recette est restreinte aux
    /// pieces de CET effet -- la checklist pilote l'arbre.
    /// </summary>
    private RecetteTache? RecetteArbre
        => ToutLeDossier ? null
           : EffetCible.Length > 0 ? RecetteActive?.PourEffet(EffetCible)
           : RecetteActive;

    /// <summary>
    /// Clic sur une ligne d'effet : l'arbre se filtre sur la piece attendue.
    /// Un 2e clic sur la meme ligne revient a la tache entiere -- rien ne
    /// reste coince sur un filtre qu'on ne saurait pas defaire.
    /// </summary>
    private void ViserEffet(EffetRecette e)
    {
        if (e.Piece.Length == 0) return;
        EffetCible = EffetCible == e.Canal ? "" : e.Canal;
    }

    /// <summary>Recopie dans la trace les valeurs que le flux EDI porte pour
    /// les champs de cette recette. Sans effet si le flux n en propose aucune.</summary>
    private void ProposerLesValeursDuFlux(RecetteTache recette, string affaire)
    {
        foreach (var champ in recette.Champs)
        {
            if (SimulationEdi.ValeurProposee(champ.Table, champ.Colonne) is not { } propose) continue;
            var cle = RecetteTache.Cle(champ);
            Etat.NoterModification(affaire, new ModificationChamp(
                DateTime.Now, SimulationEdi.Message.Fichier,
                $"Flux EDI > {SimulationEdi.Message.Emetteur}", champ.Libelle, cle,
                Etat.DerniereValeur(affaire, cle) ?? string.Empty, propose));
        }
    }

    private void OuvrirTache(RecetteTache recette, string numeroDossier)
    {
        // CE QUE LE FLUX A DEJA DIT N EST PAS A RETAPER. Quand l affaire vient
        // d un flux EDI qui porte les valeurs, elles sont ecrites avant meme
        // que la fiche s ouvre -- par le MEME geste que la saisie a la main
        // (la trace des modifications), donc visibles dans l historique avec
        // le fichier pour auteur, et modifiables comme n importe quelle
        // valeur. Un champ dont le flux ne dit rien reste vide : on ne
        // remplit jamais a la place du greffier ce que personne n a declare.
        if (numeroDossier == SimulationEdi.Dossier)
            ProposerLesValeursDuFlux(recette, numeroDossier);

        RecetteActive = recette;
        AffaireDeLaTache = numeroDossier;
        ToutLeDossier = false;
        EffetCible = "";
        LigneCourante = 1;
        OuvrirParNumero(numeroDossier);
        // La tache mene DIRECTEMENT au seul endroit qui porte ses champs.
        var fiche = ArbreVisible().FirstOrDefault(n => n.EstFiche
                                                       && n.Libelle == InformationsDossier.LibelleNoeud);
        if (fiche is not null)
        {
            foreach (var n in ArbreVisible())
            {
                if (!n.EstPiece && !n.EstFiche && n.Descendants().Any(d => ReferenceEquals(d, fiche)))
                    NoeudsOuverts.Add(n);
            }
            OuvrirApercu(fiche);
            DonneesVisible = true;
        }
    }

    private void FermerTache()
    {
        RecetteActive = null;
        AffaireDeLaTache = null;
        ToutLeDossier = false;
        EffetCible = "";
    }

    /// <summary>
    /// Les champs de la tache encore vides. Ils sont lus dans la TRACE des
    /// modifications de l'affaire (qui a ecrit quoi, quand) : c'est cette
    /// trace qui prouve la fin de la tache, pas l'etat de l'ecran.
    /// </summary>
    private ChampRecette[] ChampsManquants()
        => RecetteActive is null || DossierOuvert is null
            ? []
            : RecetteActive.Manquants(
                cle => Etat.DerniereValeur(DossierOuvert.Numero, cle), NbLignesTache);

    /// <summary>Les cases exigees encore vides, avec leur rang de ligne.</summary>
    private (ChampRecette Champ, int Ligne)[] TrousDeLaTache()
        => RecetteActive is null || DossierOuvert is null
            ? []
            : RecetteActive.ManquantsParLigne(
                cle => Etat.DerniereValeur(DossierOuvert.Numero, cle), NbLignesTache);

    /// <summary>
    /// Le nombre de CASES a remplir : un champ repetable en vaut une par
    /// ligne ouverte. C'est ce total qui rend le compte honnete quand la
    /// tache porte trois dirigeants.
    /// </summary>
    private int CasesExigees()
        => RecetteActive is null
            ? 0
            : RecetteActive.ChampsObligatoires.Sum(c => c.Repetable ? NbLignesTache : 1);

    /// <summary>Nomme les cases manquantes, avec leur ligne quand il y en a plusieurs.</summary>
    private string NommerTrous((ChampRecette Champ, int Ligne)[] trous)
        => string.Join(", ", trous.Take(4).Select(t =>
               t.Champ.Repetable && NbLignesTache > 1
                   ? $"{t.Champ.Libelle} ({RecetteActive!.LibelleLigne} {t.Ligne})"
                   : t.Champ.Libelle))
           + (trous.Length > 4 ? $" et {trous.Length - 4} autre(s)" : "");

    /// <summary>La recette que l'alerte de lot demande d'ouvrir, s'il y en a une.</summary>
    private string LotDemande = "";

    /// <summary>La ligne en cours de saisie (dirigeant 1, 2, 3...).</summary>
    private int LigneCourante = 1;

    /// <summary>Combien de lignes cette tache a ouvertes sur cette affaire.</summary>
    private int NbLignesTache
        => RecetteActive is null || DossierOuvert is null
            ? 1
            : Etat.NbLignesTache(RecetteActive.Code, DossierOuvert.Numero);

    /// <summary>Combien de champs exiges manquent SUR CETTE LIGNE.</summary>
    private int TrousDeLaLigne(int rang)
        => RecetteActive is null || DossierOuvert is null
            ? 0
            : RecetteActive
                .ManquantsParLigne(cle => Etat.DerniereValeur(DossierOuvert.Numero, cle), NbLignesTache)
                .Count(x => x.Ligne == rang || !x.Champ.Repetable && rang == 1);

    private void AjouterLigneTache()
    {
        if (RecetteActive is null || DossierOuvert is null) return;
        LigneCourante = Etat.AjouterLigneTache(RecetteActive.Code, DossierOuvert.Numero);
        Etat.Journaliser(DossierOuvert.Numero,
            $"Tache {RecetteActive.Code} : {RecetteActive.LibelleLigne} {LigneCourante} ajoute par {Utilisateur}");
    }

    private void RetirerLigneTache()
    {
        if (RecetteActive is null || DossierOuvert is null) return;
        var n = Etat.RetirerLigneTache(RecetteActive.Code, DossierOuvert.Numero);
        LigneCourante = Math.Min(LigneCourante, n);
    }

    /// <summary>
    /// Vrai si la PIECE qui atteste cet effet est deposee dans l'arbre du
    /// dossier. C'est la preuve visible a l'ecran, et c'est elle qui coche la
    /// case : une coche posee a la main ne prouvait rien.
    /// </summary>
    private bool EffetAtteste(EffetRecette e)
        => RecetteActive is not null
           && RecetteActive.EffetAtteste(e.Canal, PieceDansLArbre);

    /// <summary>Une piece de ce libelle est-elle deposee quelque part dans l'arbre ?</summary>
    private bool PieceDansLArbre(string motif)
        => ArbreVisible().Any(n => n.EstPiece
                                   && n.Libelle.Contains(motif, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Vrai si cet effet est traite : soit sa piece est deposee (preuve), soit
    /// il a ete force a la main -- ce qui reste possible pour les canaux
    /// qu'aucun document n'atteste (une notification, par exemple).
    /// </summary>
    private bool EffetCoche(string canal)
        => RecetteActive is not null && DossierOuvert is not null
           && (RecetteActive.EffetAtteste(canal, PieceDansLArbre)
               || Etat.EffetTraite(RecetteActive.Code, DossierOuvert.Numero, canal));

    /// <summary>Les publications et notifications encore a traiter.</summary>
    private EffetRecette[] EffetsRestants()
        => RecetteActive is null || DossierOuvert is null
            ? []
            : RecetteActive.EffetsRestants(EffetCoche);

    /// <summary>Une ligne de la checklist de SAISIE : un champ exige (ou
    /// propose) de la recette, pour UNE ligne, avec ce qui la coche.</summary>
    private sealed record CaseSaisie(ChampRecette Champ, int Ligne, string Libelle,
                                     bool Exige, bool Rempli, string Valeur);

    /// <summary>
    /// La checklist de saisie : un champ repetable donne UNE ligne par rang
    /// ouvert (« Patronyme du dirigeant - Dirigeant 2 »). La coche vient de
    /// la TRACE du dossier, jamais d'un clic : une valeur effacee decoche.
    /// </summary>
    private CaseSaisie[] CasesDeSaisie()
    {
        if (RecetteActive is null || DossierOuvert is null) return [];
        var cases = new List<CaseSaisie>();
        foreach (var champ in RecetteActive.Champs)
        {
            var rangs = champ.Repetable
                ? Enumerable.Range(1, Math.Max(1, NbLignesTache))
                : Enumerable.Range(1, 1);
            foreach (var rang in rangs)
            {
                var cle = RecetteTache.Cle(champ, rang);
                var valeur = Etat.DerniereValeur(DossierOuvert.Numero, cle) ?? string.Empty;
                cases.Add(new CaseSaisie(champ, rang,
                    champ.Repetable ? $"{champ.Libelle} - {RecetteActive.LibelleLigne} {rang}" : champ.Libelle,
                    RecetteActive.EstExige(champ, null),
                    !string.IsNullOrWhiteSpace(valeur), valeur));
            }
        }
        return cases.ToArray();
    }

    /// <summary>Coche ou decoche un effet, et le note au journal du dossier.</summary>
    private void BasculerEffet(EffetRecette e)
    {
        if (RecetteActive is null || DossierOuvert is null) return;
        Etat.BasculerEffet(RecetteActive.Code, DossierOuvert.Numero, e.Canal);
        Etat.Journaliser(DossierOuvert.Numero,
            $"Tache {RecetteActive.Code} - {e.Libelle} : "
            + (EffetCoche(e.Canal) ? "traite" : "remis a faire") + $" par {Utilisateur}");
    }

    /// <summary>
    /// La tache ne se clot que si SES champs sont ecrits ET si tout ce
    /// qu'elle entraine ailleurs a ete traite.
    /// </summary>
    private bool TachePeutSeClore()
        => RecetteActive is not null
           && ChampsManquants().Length == 0
           && EffetsRestants().Length == 0;

    /// <summary>Ce qui empeche de terminer, dit en clair sur le bouton.</summary>
    private string RaisonBlocage()
    {
        if (RecetteActive is null) return "";
        var champs = TrousDeLaTache();
        if (champs.Length > 0) return "Renseigner d'abord : " + NommerTrous(champs);
        var effets = EffetsRestants();
        if (effets.Length > 0)
            return "Reste a traiter : " + string.Join(", ", effets.Select(e => e.Libelle));
        return "Terminer la tache et retirer son alerte";
    }

    /// <summary>
    /// Termine la tache : elle est inscrite comme faite (qui, quand), le
    /// journal du dossier en garde la ligne, et son ALERTE ne se releve plus.
    /// </summary>
    private void TerminerTache()
    {
        if (RecetteActive is null || DossierOuvert is null) return;
        if (!TachePeutSeClore()) return;

        var code = RecetteActive.Code;
        var valeurs = string.Join(", ", RecetteActive.Champs
            .Select(c => $"{c.Libelle} = {Etat.DerniereValeur(DossierOuvert.Numero, RecetteTache.Cle(c))}"));
        Etat.CloreTache(code, DossierOuvert.Numero, Utilisateur);
        var propage = string.Join(", ", RecetteActive.EffetsObligatoires.Select(e => e.Libelle));
        Etat.Journaliser(DossierOuvert.Numero,
            $"Tache {code} terminee par {Utilisateur} : {valeurs} - propage : {propage}");

        RecetteActive = null;
        ToutLeDossier = false;
        // L'alerte correspondante disparait de la file : une tache faite ne
        // doit pas revenir. Le releve est refait sur l'etat a jour.
        Accueil.RelevePerf();
        Accueil.ReleverAlertes();
    }

    private void BasculerToutLeDossier()
    {
        ToutLeDossier = !ToutLeDossier;
        if (ToutLeDossier) CompterToutAffiche();
    }

    /// <summary>Un clic sur « Afficher tout » : compte et trace.</summary>
    private void CompterToutAffiche()
    {
        ClicsToutAfficher++;
        if (DossierOuvert is not null && RecetteActive is not null)
            Etat.Journaliser(DossierOuvert.Numero,
                $"Tache {RecetteActive.Code} : vue entiere rouverte ({ClicsToutAfficher} fois)");
    }

    /// <summary>Les dossiers que la tache replie, et les pieces qu'ils portent.</summary>
    private (int Branches, int Pieces) Replies()
    {
        if (RecetteActive is null) return (0, 0);
        var branches = 0;
        var pieces = 0;
        var pile = new Stack<NoeudGed>();
        pile.Push(Arbre);
        while (pile.Count > 0)
        {
            var n = pile.Pop();
            var tous = AccesGed.EnfantsVisibles(n, Domaines, AjoutsArbre, MasquesArbre);
            var gardes = AccesGed.EnfantsVisibles(n, Domaines, AjoutsArbre, MasquesArbre, RecetteActive);
            foreach (var e in tous)
            {
                if (gardes.Any(g => ReferenceEquals(g, e))) { pile.Push(e); continue; }
                branches++;
                pieces += e.NbPieces;
            }
        }
        return (branches, pieces);
    }

    private int BranchesMasquees => Replies().Branches;
    private int PiecesMasquees => Replies().Pieces;

    // ---- Bouton « Nouveau » de l'arbre : cree un dossier ou une piece ----
    private readonly Dictionary<NoeudGed, List<NoeudGed>> AjoutsArbre = AccesGed.NouveauxAjouts();
    private readonly HashSet<NoeudGed> MasquesArbre = AccesGed.NouveauxMasques();
    private readonly Dictionary<NoeudGed, string> LibellesArbre = AccesGed.NouveauxLibelles();
    private bool NouveauOuvert;

    // Renommage sur place et menu du clic droit.
    private NoeudGed? NoeudRenomme;
    private NoeudGed? MenuNoeud;
    private double MenuX, MenuY;

    /// <summary>Le libelle affiche d'un noeud : celui d'un renommage sinon le sien.</summary>
    private string Libelle(NoeudGed n)
        => LibellesArbre.TryGetValue(n, out var l) ? l : n.Libelle;

    /// <summary>Pose un libelle reecrit ET le retient dans accueil.json.</summary>
    private void PoserLibelle(NoeudGed n, string texte)
    {
        LibellesArbre[n] = texte;
        MemoriserArbre();
    }

    /// <summary>
    /// Retient l'arbre de l'affaire ouverte apres CHAQUE geste (creation,
    /// suppression, renommage). Les trois collections ci-dessus sont
    /// indexees par reference d'objet : sans cette ecriture, tout ce que
    /// l'utilisateur ajoute dans l'arbre part a la fermeture.
    /// </summary>
    private void MemoriserArbre()
    {
        if (DossierOuvert is null) return;
        Etat.EnregistrerArbreGed(CleAffaire,
            PersistanceGed.Capturer(Arbre, AjoutsArbre, MasquesArbre, LibellesArbre));
    }

    private void OuvrirNouveau()
    {
        NouveauOuvert = true;
    }

    private void NouveauSous(NoeudGed dossier)
    {
        NoeudSelectionne = dossier;
        MenuNoeud = null;
        NouveauOuvert = true;
    }

    private void OuvrirMenuNoeud((NoeudGed Noeud, double ClientX, double ClientY) e)
    {
        MenuNoeud = e.Noeud;
        MenuX = e.ClientX;
        MenuY = e.ClientY;
        NoeudRenomme = null;
    }

    private void FermerMenu() => MenuNoeud = null;

    private void DemarrerRenommage(NoeudGed n)
    {
        MenuNoeud = null;
        NoeudRenomme = n;
    }

    /// <summary>Renommage valide dans l'arbre. Texte vide = on abandonne.</summary>
    private void ValiderRenommage((NoeudGed Noeud, string Texte) e)
    {
        var texte = e.Texte.Trim();
        var noeud = e.Noeud;
        if (texte.Length > 0
            && !Soumettre("Renommer", Libelle(noeud), $"nouveau nom : {texte}",
                          () => PoserLibelle(noeud, texte)))
        {
            PoserLibelle(noeud, texte);
        }
        NoeudRenomme = null;
    }

    /// <summary>
    /// Suppression : le jeu de demonstration fige les enfants d'un dossier,
    /// on MASQUE donc le noeud. Un element ajoute a la main part vraiment.
    /// </summary>
    private void SupprimerNoeud(NoeudGed n)
    {
        MenuNoeud = null;
        if (ReferenceEquals(n, Arbre)) return;
        if (Soumettre("Supprimer", Libelle(n), "", () => AppliquerSuppression(n))) return;
        AppliquerSuppression(n);
    }

    private void AppliquerSuppression(NoeudGed n)
    {
        var retire = false;
        foreach (var liste in AjoutsArbre.Values)
            retire |= liste.RemoveAll(x => ReferenceEquals(x, n)) > 0;
        if (!retire) MasquesArbre.Add(n);
        foreach (var d in n.Descendants()) NoeudsOuverts.Remove(d);
        NoeudsOuverts.Remove(n);
        if (ReferenceEquals(NoeudSelectionne, n)) { NoeudSelectionne = null; ApercuNoeud = null; GuideVisible = false; }
        if (ReferenceEquals(NoeudRenomme, n)) NoeudRenomme = null;
        MemoriserArbre();
    }

    private void Dupliquer(NoeudGed n)
    {
        MenuNoeud = null;
        if (Soumettre("Dupliquer", Libelle(n), "", () => AppliquerDuplication(n))) return;
        AppliquerDuplication(n);
    }

    private void AppliquerDuplication(NoeudGed n)
    {
        var parent = ParentDe(n) ?? Arbre;
        var copie = n.EstPiece
            ? AccesGed.NouvellePiece($"{Libelle(n)} (copie)", n.Extension, DateDepot)
            : AccesGed.NouveauDossier($"{Libelle(n)} (copie)");
        Inserer(parent, copie);
        NoeudSelectionne = copie;
        NoeudRenomme = copie;
    }

    private void ToutDeplier(NoeudGed n)
    {
        MenuNoeud = null;
        NoeudsOuverts.Add(n);
        foreach (var d in n.Descendants()) if (!d.EstPiece) NoeudsOuverts.Add(d);
    }

    private void ToutReplier(NoeudGed n)
    {
        MenuNoeud = null;
        foreach (var d in n.Descendants()) NoeudsOuverts.Remove(d);
        NoeudsOuverts.Remove(n);
    }

    /// <summary>Le dossier qui porte ce noeud, ajouts manuels compris.</summary>
    private NoeudGed? ParentDe(NoeudGed n)
    {
        foreach (var candidat in ArbreVisible())
        {
            if (candidat.Enfants.Any(e => ReferenceEquals(e, n))) return candidat;
            if (AjoutsArbre.TryGetValue(candidat, out var l) && l.Any(e => ReferenceEquals(e, n)))
                return candidat;
        }
        return null;
    }

    private void Inserer(NoeudGed cible, NoeudGed element)
    {
        if (!AjoutsArbre.TryGetValue(cible, out var liste))
        {
            liste = new List<NoeudGed>();
            AjoutsArbre[cible] = liste;
        }
        liste.Add(element);
        NoeudsOuverts.Add(cible);
        MemoriserArbre();
    }

    /// <summary>Ou l'element va : le dossier clique, sinon la racine de l'affaire.</summary>
    private NoeudGed DossierCible
        => NoeudSelectionne is not null && !NoeudSelectionne.EstPiece ? NoeudSelectionne : Arbre;

    /// <summary>
    /// Une nature cliquee dans la grille : l'element est cree tout de suite
    /// avec un libelle par defaut, puis se nomme SUR PLACE dans l'arbre.
    /// </summary>
    private void AjouterElement(string nature)
    {
        var cible = DossierCible;
        var element = AccesGed.NouveauSelonNature(nature, DateDepot);
        NouveauOuvert = false;
        if (Soumettre("Creer", Libelle(cible), $"nouvel element : {element.Libelle}",
                      () => Inserer(cible, element))) return;
        Inserer(cible, element);
        NoeudSelectionne = element;
        NoeudRenomme = element;
    }

    private const string DateDepot = "10/09/2026";
    private const string Greffe = "9999";

    /// <summary>Theme d'affichage courant (code de ThemesRig), tenu par la session
    /// de l'accueil. Le fond de la fenetre WPF le suit (FenetreInterop.AppliquerFond),
    /// <html> aussi (ile ThemeRig).</summary>
    private string Theme => Accueil.Theme;

    // ================= Apercu d'une piece et impression =================
    private NoeudGed? ApercuNoeud;
    private string ApercuMessage = "";
    private bool ImpressionOuverte;
    private string ImprImprimante = ArbreDemo.Imprimantes[0];
    private string ImprFileDifferee = ArbreDemo.FilesDifferees[0];
    private string ImprTypePapier = ArbreDemo.TypesPapier[0];
    private int ImprExemplaires = 1;
    private bool ImprRectoVerso;

    /// <summary>
    /// Clic dans l'arbre : un dossier se replie, une PIECE s'ouvre en apercu.
    /// La selection reste posee pour le panneau de saisie guidee.
    /// Demande du 15/09/2026 : sans formulaire (dossier) la colonne des
    /// donnees se ferme ; sans apercu (fiche, dossier) celle du document aussi.
    /// </summary>
    private void OuvrirApercu(NoeudGed n)
    {
        ChoisirNoeud(n);
        if (PlacerSurInformations(n)) { GuideVisible = false; return; }
        if (!n.EstPiece) { DonneesVisible = false; GuideVisible = false; return; }
        ApercuNoeud = n;
        ApercuMessage = "";
        ImpressionOuverte = false;
        GuideVisible = true;
        // Les donnees affichees a cote suivent la piece ouverte.
        CodeEtapeGuide = EtapeDuNoeud(n).Code;
        SuivreEtape(EtapeDuNoeud(n));
        PublierDocument();
    }

    /// <summary>
    /// « Informations du dossier » dans le bloc du milieu : au clic sur la fiche,
    /// ou des que « Dossier RCS » est DEPLIE (demande du 15/09/2026).
    /// </summary>
    private bool PlacerSurInformations(NoeudGed n)
    {
        var fiche = n.EstFiche ? n
            : NoeudsOuverts.Contains(n) ? ArbreDemo.FicheADeplier(n) : null;
        if (fiche is null) return false;
        NoeudSelectionne = fiche;
        DonneesVisible = true;
        return true;
    }

    /// <summary>
    /// Recopie la piece courante vers les fenetres detachees QUI L'AFFICHENT
    /// deja : editer le formulaire met a jour la bonne fenetre, sans ecraser
    /// les autres documents sortis.
    /// </summary>
    private void PublierDocument()
    {
        if (FenetresDetachees.Count == 0 || ApercuNoeud is null) return;
        var valeurs = ValeursDocument(ApercuNoeud).Select(v => (v.Libelle, v.Valeur)).ToList();
        foreach (var f in FenetresDetachees)
        {
            if (ReferenceEquals(Partage!.Emplacement(f.Rang).Piece, ApercuNoeud))
                Partage!.Publier(f.Rang, ApercuNoeud, valeurs);
        }
    }

    private void FermerApercu()
    {
        ApercuNoeud = null;
        ImpressionOuverte = false;
        // Demande du 15/09/2026 : la croix ferme directement la colonne de
        // droite -- il n'existe plus de vue « Aucune piece ouverte ».
        GuideVisible = false;
    }

    private void OuvrirImpression() => ImpressionOuverte = true;

    /// <summary>
    /// « Editer » depuis la visionneuse : la piece reste affichee a droite,
    /// l'edition se fait sur place (la colonne de droite EST le document).
    /// </summary>
    private void EditerPiece()
    {
        if (ApercuNoeud is null) return;
        NoeudSelectionne = ApercuNoeud;
        // L'etape deduite de la piece : « Editer » ouvre le formulaire QUI
        // correspond au document, pas une page de saisie au hasard.
        CodeEtapeGuide = EtapeDuNoeud(ApercuNoeud).Code;
        SuivreEtape(EtapeDuNoeud(ApercuNoeud));
        DonneesVisible = true;
        ImpressionOuverte = false;
        ApercuMessage = "";
    }

    /// <summary>
    /// Sort la piece courante dans UNE NOUVELLE fenetre. On peut en sortir
    /// PLUSIEURS : chaque appel prend le plus petit rang libre. La fenetre
    /// revient a la position memorisee pour ce rang (demande du 14/09/2026 :
    /// place stable pour qui travaille sur plusieurs ecrans), sinon elle se
    /// pose sur l'autre ecran quand il y en a un.
    /// </summary>
    private void DetacherDocument()
    {
        if (ApercuNoeud is null) return;
        var rang = Partage!.RangLibre(FenetresDetachees.Select(f => f.Rang));
        Partage!.Publier(rang, ApercuNoeud,
            ValeursDocument(ApercuNoeud).Select(v => (v.Libelle, v.Valeur)).ToList());
        var piece = ApercuNoeud;
        var fenetre = new FenetreDocument(rang);
        fenetre.Closed += (_, _) =>
        {
            FenetresDetachees.Remove(fenetre);
            Partage!.Liberer(fenetre.Rang);
            if (VueFermee) return;
            // Demande du 15/09/2026 : fermer la fenetre RENVOIE le document
            // dans la colonne de droite, comme avant « Detacher ».
            Planifier(() =>
            {
                ApercuNoeud = piece;
                ApercuMessage = "";
                ImpressionOuverte = false;
                GuideVisible = true;
            });
        };
        FenetresDetachees.Add(fenetre);
        fenetre.Show();
        // Demande du 15/09/2026 : le document sorti ne reste pas AUSSI a
        // droite -- la colonne se ferme pour ne pas faire doublon.
        GuideVisible = false;
        ImpressionOuverte = false;
    }

    /// <summary>Vrai des que la vue est liberee (fermeture de l'application) :
    /// les fenetres qui se ferment alors ne rattachent plus rien.</summary>
    private bool VueFermee;

    /// <summary>Ramene tous les documents dans la 3e colonne (ferme leurs fenetres).</summary>
    private void RattacherDocument()
    {
        foreach (var f in FenetresDetachees.ToList()) f.Close();
        FenetresDetachees.Clear();
    }

    /// <summary>
    /// Ce que la saisie a porte SUR ce document : les champs renseignes de
    /// l'etape liee a la piece. C'est le lien « le formulaire affecte le
    /// document » -- l'apercu les affiche sous son entete.
    /// </summary>
    private List<(string Libelle, string Valeur)> ValeursDocument(NoeudGed n)
    {
        var etape = EtapeDuNoeud(n);
        var fiche = FicheVue(etape);
        return etape.Champs
                    .Where(c => fiche.TryGetValue(c.Colonne, out var v) && v.Length > 0)
                    .Select(c => (c.Libelle, fiche[c.Colonne]))
                    .ToList();
    }

    private void LancerImpression()
    {
        ImpressionOuverte = false;
        ApercuMessage = $"Envoye a {ImprImprimante} — {ImprExemplaires} ex., papier {ImprTypePapier}"
                      + (ImprRectoVerso ? ", recto-verso" : "")
                      + $", file {ImprFileDifferee}.";
    }

    private void Sauvegarder()
        => ApercuMessage = $"Enregistre sous {ApercuNoeud?.Libelle}.{ApercuNoeud?.Extension.ToLowerInvariant()}.";

    // ---- Envoi par mail (demande du 15/09/2026, remplace « Transferer »).
    // MAQUETTE : les messages types viennent d'une table en base dans RIG ;
    // ici trois messages fictifs en dur.
    private bool EnvoiMailOuvert;
    private string MailDestinataire = "";
    private int MailMessageIndex;
    private static readonly string[] MailDestinatairesDemo =
        { "avocat.dupont@barreau-exemple.fr", "greffe.civil@exemple.fr", "contact@huissier-exemple.fr" };
    private static readonly (string Titre, string Corps)[] MailMessagesDemo =
    {
        ("Transmission de piece", "Madame, Monsieur, Veuillez trouver ci-joint la piece relative au dossier cite en objet. Cordialement, Le greffe"),
        ("Convocation a l'audience", "Madame, Monsieur, Vous trouverez ci-joint votre convocation. Merci de vous presenter a la date indiquee. Le greffe"),
        ("Demande de pieces complementaires", "Madame, Monsieur, Afin de completer le dossier, merci de nous retourner les pieces mentionnees dans le document joint. Le greffe"),
    };

    private void OuvrirEnvoiMail()
    {
        MailDestinataire = "";
        MailMessageIndex = 0;
        EnvoiMailOuvert = true;
    }

    private void EnvoyerMail()
    {
        EnvoiMailOuvert = false;
        ApercuMessage = $"Mail « {MailMessagesDemo[MailMessageIndex].Titre} » envoye a {MailDestinataire.Trim()} (maquette : aucun envoi reel).";
    }

    /// <summary>Une page de l'apercu. La decoupe vit dans DocumentPartage :
    /// la fenetre detachee affiche exactement les memes pages.</summary>
    private static string TextePage(NoeudGed n, int feuille)
        => DocumentPartage.TextePage(n, feuille);

    // ================= Menu de gauche =================
    private record Proc(string Code, string Libelle);
    private record Groupe(string Code, string Libelle, string Statut, Proc[] Procs);

    // ================= Navigation metier =================
    /// <summary>Le catalogue du menu de gauche, deplace dans Pont/MenuAccueil.cs
    /// (lot 1 du passage a React). Le plan de travail le relit pour ses
    /// aiguillages : motif et libelle d'un ecran a venir.</summary>
    private readonly Pont.MomentMenu[] Moments = Pont.MenuAccueil.Moments;

    /// <summary>Ouvre l'ecran d'un code sans que l'appelant ait a connaitre son
    /// libelle : le catalogue <see cref="Groupes"/> reste la seule source.</summary>
    private void OuvrirCode(string code)
    {
        var p = Groupes.SelectMany(g => g.Procs).FirstOrDefault(x => x.Code == code);
        if (p is not null) { OuvrirProc(p); return; }

        // Le menu couvre desormais TOUT le catalogue de RIG v2, pas seulement
        // les ecrans deja portes. Pour une procedure sans ecran, le libelle du
        // menu sert de titre : on ouvre un ecran « a venir » qui se nomme,
        // plutot que de ne rien faire au clic.
        var e = EntreeDeCode(code);
        if (e is not null) OuvrirProc(new Proc(code, e.Libelle));
    }

    /// <summary>L'entree de menu qui porte un code, ou null.</summary>
    private Pont.EntreeMenu? EntreeDeCode(string code)
        => Moments.SelectMany(m => m.Sous).SelectMany(s => s.Entrees)
                  .FirstOrDefault(x => x.Code == code);

    /// <summary>Catalogue des ecrans portes depuis RIG v2. Il n'est PLUS affiche
    /// tel quel : il sert d'aiguillage, de source de libelles pour la recherche
    /// et d'inventaire de ce qui existe.</summary>
    private readonly Groupe[] Groupes =
    [
        new("RCS", "RCS", "action",
        [
            new("OUVERTURE_DOSSIER", "Ouverture d'un dossier RCS"),
            new("PROC_RECHAFF",   "Recherche d'affaires"),
            new("PROC_DEMANDE",   "Demandes"),
            new("PROC_REQUI",     "Requisitions - commande de documents"),
            new("PROC_COMMANDES_WEB", "Commandes web"),
            new("PROC_ARRIVEE",   "Arrivee du courrier"),
            new("PROC_ACTES_NUM", "Actes numerises"),
            new("PROC_CLOT",      "Cloture"),
            new("PROC_ARCHIRAD",  "Archivage / radiation"),
            new("BODACC",         "BODACC")
        ]),
        new("JUD", "Judiciaire", "attente",
        [
            new("PROC_DEPOTJUD",   "Depots judiciaires"),
            new("PROC_ACTEUR_JUD", "Acteurs"),
            new("PROC_CJUD",       "Consultation judiciaire"),
            new("PROC_AUDIENCE",   "Audiences"),
            new("PROC_GESTAUDS",   "Gestion des audiences"),
            new("PROC_EXPERT",     "Experts"),
            new("PROC_GEDJUD",     "GED judiciaire"),
            new("PROC_REDA",       "Redaction d'une decision")
        ]),
        new("GED", "Documents", "ok",
        [
            new("PROC_DOC_DEMAT",        "Documents demateralises"),
            new("PROC_CONTROLE_VIDIGED", "Controle VidiGED"),
            new("PROC_COFFRE_FORT",      "Coffre-fort"),
            new("PROC_TELETRANS",        "Teletransmissions (EDI)")
        ]),
        new("FAC", "Facturation", "ok",
        [
            new("PROC_FACTURE", "Factures"),
            new("PROC_ENCAISS", "Encaissements"),
            new("PROC_CREANCE", "Creances"),
            new("PROC_FCLIENT", "Fiches clients"),
            new("PROC_GCLIENT", "Gestion clients"),
            new("PROC_COMPTABILISATION", "Comptabilisation")
        ]),
        new("ADM", "Administration", "attente",
        [
            new("PROC_ACCUEIL_PARAM", "Parametres d'accueil"),
            new("PROC_ACTUALITES", "Actualites"),
            new("PROC_TACHES_LOT",    "Taches en lot"),
            new("PROC_VERROUS",       "Dossiers verrouilles"),
            new("PROC_STAT",       "Statistiques"),
            new("PROC_GESTEMAIL",     "Messagerie"),
            new("PROC_GESTBANQ",      "Banques"),
            new("PROC_EDIT_MAQUETTE", "Maquettes d'edition"),
            new("PROC_COURRIER_DIVERS", "Courriers divers")
        ])
    ];

    /// <summary>L'etat retenu d'une session a l'autre (accueil.json), tenu par la
    /// session de l'accueil et partage avec le plan de travail.</summary>
    private AccueilEtat Etat => Accueil.Etat;

    /// <summary>L'accueil : son etat et ses regles (Pont/Accueil.cs). Remplace a
    /// l'ouverture (OnInitialized) par la session chargee depuis accueil.json.</summary>
    private Pont.SessionAccueil Accueil = new(new AccueilEtat());

    /// <summary>Echap : ferme ce qui est ouvert, du plus interieur au plus
    /// exterieur -- l'affaire d'abord, l'ecran de procedure ensuite. Un seul
    /// appui ne referme donc jamais deux choses a la fois. La touche est ecoutee
    /// par la page (web/src/plan/clavier.ts) et remontee par le geste « echap ».</summary>
    private void ToucheEchap()
    {
        if (FermerPalette()) return;
        if (DossierOuvert is not null) FermerDossier();
        else if (ProcOuverte is not null) FermerProc();
    }

    /// <summary>L'ouverture de la page (OnInitialized du .razor).</summary>
    public void Initialiser()
    {
        Accueil = new Pont.SessionAccueil(AccueilEtat.Charger());
        Accueil.Change += SurChangeAccueil;
        AccueilEtat.FavorisChanges += SurFavorisChanges;
        // L AGENCEMENT d abord : les onglets ouverts la derniere fois, dans leur
        // ordre, et les vues qui etaient sorties en fenetre.
        RestaurerAgencement();
        Accueil.Initialiser();
    }

    /// <summary>
    /// Change quand l'observateur de defilement des etapes doit etre rebranche
    /// (une affaire vient de s'ouvrir) : la page le rebranche et remesure l'etape
    /// visible, comme le faisait PaginationSuivie = false dans le .razor.
    /// </summary>
    private int Pagination;

    /// <summary>Le dernier defilement demande a la page : la frise ou un lien vers
    /// un defaut amene la zone des etapes sur SON etape (allerEtape de
    /// rig-accueil.js). Le numero change a chaque demande ; la page joue chaque
    /// numero une fois, apres avoir rendu la zone.</summary>
    private DefilementVue? Defilement;
    private int NumeroDefilement;

    private void Defiler(int index, bool doux)
        => Defilement = new DefilementVue(++NumeroDefilement, index, doux);

    /// <summary>Le premier rendu de la page (OnAfterRenderAsync(premierRendu) du
    /// .razor) : le fond de la fenetre et les minuteurs de l'accueil.</summary>
    public void Demarrer()
    {
        // Le theme ne peut pas rester porte par la page : le fond de la fenetre
        // WPF, qui borde le WebView de 6 px, resterait sombre et cernerait le
        // mode clair d'un cadre noir. <html>, lui, est pose par l'ile ThemeRig.
        Fenetre?.AppliquerFond(Theme);
        // Les minuteurs de l'accueil (performance chaque seconde, actualites
        // chaque minute) : la session releve, Home re-rend (SurChangeAccueil).
        Accueil.Demarrer();
    }

    /// <summary>Une autre fenetre a allume/eteint une etoile : on reprend sa
    /// liste, sinon notre copie perimee l'ecraserait au prochain enregistrement.</summary>
    private void SurFavorisChanges(AccueilEtat source, List<string> liste)
    {
        if (ReferenceEquals(source, Etat)) return;
        Planifier(() => { Etat.VuesFavorites = liste; });
    }

    /// <summary>La page se ferme (DisposeAsync du .razor) : les minuteurs
    /// s'arretent, et les ecrans a session montes par le plan de travail
    /// (taches en lot, fiche d'informations) sont fermes avec elle.</summary>
    public async ValueTask DisposeAsync()
    {
        AccueilEtat.FavorisChanges -= SurFavorisChanges;
        Accueil.Change -= SurChangeAccueil;
        VueFermee = true;
        await Accueil.DisposeAsync();
        FermerSessionLot();
        FermerSessionFiche();
    }

    // ================= Recherche =================
    // La recherche est la PALETTE Ctrl K (PlanTravailPalette.cs), que la barre
    // de l'accueil ouvre (19/11/2026, conv-2018). Ce qui suit lui fournit le
    // fonds d'affaires et l'arbre dans lequel elle cherche, et ouvre ce qu'elle
    // a trouve.

    /// <summary>
    /// Une affaire ouverte dans la visionneuse. <c>SansDossierRcs</c> marque
    /// les affaires PUREMENT judiciaires : elles n ont aucun dossier au RCS,
    /// donc leur arbre n ouvre que la branche « Judiciaire » (v2 :
    /// PROC_VGED\ETP_VGED\OPE_VGED.cs, _InitTreeview).
    /// </summary>
    private record Dossier(string Numero, string Objet, string Siren, string Type, string Statut,
                           bool SansDossierRcs = false, string TypeDossier = "");

    /// <summary>
    /// Les champs d identite affiches dans le bandeau « Fiche sommaire », au
    /// dessus de l arbre — repris un a un de PROC_VDOSSIER (v2).
    /// </summary>
    private sealed record Sommaire(string FormeJuridique, string Sigle, string NomCommercial,
                                   string Civilite, string Designation, string Adresse,
                                   string GestionAnterieure, string Immatriculation,
                                   string Cessation, string DateMaj, string Mention);

    // Fiche sommaire du jeu de demonstration, par numero d affaire.
    private static readonly Dictionary<string, Sommaire> SommairesDemo = new()
    {
        ["2026B01067"] = new("Societe a responsabilite limitee", "AMB", "AMIENS BILLARDS",
            "PM", "AMIENS BILLARDS", "14 Rue des Jacobins\n80000 AMIENS",
            "", "01/09/2026", "", "01/09/2026", ""),
        ["004176"] = new("Societe civile immobiliere", "", "SCI DU PARC",
            "PM", "SCI DU PARC", "8 Avenue du Parc\n80000 AMIENS",
            "2014B00412", "12/03/2014", "", "24/08/2026", ""),
        ["004182"] = new("Societe a responsabilite limitee", "AMB", "AMIENS BILLARDS",
            "PM", "AMIENS BILLARDS", "14 Rue des Jacobins\n80000 AMIENS",
            "", "28/08/2026", "", "28/08/2026", ""),
        ["004179"] = new("Entreprise unipersonnelle a responsabilite limitee", "", "MARTIN EURL",
            "PM", "MARTIN EURL", "3 Rue Gambetta\n80100 ABBEVILLE",
            "2011B00187", "04/05/2011", "12/08/2026", "12/08/2026", "radiation du registre"),
        ["004168"] = new("Societe par actions simplifiee", "BTP", "BATIPRO SARL",
            "PM", "BATIPRO SARL", "27 Rue Maurice Flandrin\n69003 LYON",
            "", "30/07/2026", "", "30/07/2026", ""),
        ["J-00931"] = new("Societe anonyme", "TND", "TRANSNORD SA",
            "PM", "TRANSNORD SA", "41 Route de Paris\n80000 AMIENS",
            "2008B00951", "18/02/2008", "", "18/06/2026", "redressement judiciaire"),
        ["2026P00031"] = new("Entrepreneur individuel", "", "MOREAU EI",
            "PP", "MOREAU Julien", "6 Rue des Lilas\n80080 AMIENS",
            "", "", "", "05/09/2026", "liquidation judiciaire")
    };

    /// <summary>
    /// La fiche sommaire d une affaire : le jeu de demonstration, ECRASE par
    /// ce qui a reellement ete saisi dans les taches (la trace des
    /// modifications fait foi, comme pour la completude).
    /// </summary>
    private Sommaire SommaireDu(Dossier d)
    {
        var deduite = d.Objet.Contains(" - ")
            ? d.Objet[(d.Objet.IndexOf(" - ", StringComparison.Ordinal) + 3)..]
            : d.Objet;
        var s = SommairesDemo.TryGetValue(d.Numero, out var connue)
            ? connue
            : new Sommaire("", "", "", d.SansDossierRcs ? "PP" : "PM", deduite, "", "", "", "", "", "");

        string Ecrit(string cle, string defaut)
            => Etat.DerniereValeur(d.Numero, cle) is { Length: > 0 } v ? v : defaut;

        return s with
        {
            FormeJuridique = Ecrit("INSCRIT_PM.FORME_JURIDIQUE_UTI", s.FormeJuridique),
            Sigle = Ecrit("INSCRIT_PM.SIGLE", s.Sigle),
            Designation = Ecrit("INSCRIT_PM.DENOMINATION_USAGE", s.Designation),
            Cessation = Ecrit("DOSSIER_RCS.DATE_CESSATION", s.Cessation)
        };
    }

    private static readonly Dossier[] FondsDemo =
    [
        new("2026B01067",   "Affaire de demonstration - AMIENS BILLARDS", "109 678 235", "RCS", "action"),
        new("004176", "Modification - SCI DU PARC",     "812 445 901", "RCS", "alerte"),
        new("004182", "Immatriculation - AMIENS BILLARDS",   "109 678 235", "RCS", "action"),
        new("004179", "Radiation - MARTIN EURL",        "521 004 332", "RCS", "attente"),
        new("004168", "Immatriculation - BATIPRO SARL", "440 927 115", "RCS", "neutre"),
        new("J-00931", "Depot de bilan - TRANSNORD SA", "318 552 004", "JUD", "alerte"),
        // Affaires PUREMENT judiciaires (demande du 16/09/2026) : pas de
        // dossier RCS, donc pas de Siren et une seule branche dans l arbre.
        new("2026J00087",  "Assignation en paiement - DUPONT Jean",   "", "JUD", "action",  true),
        new("2026P00031",  "Liquidation judiciaire - MOREAU EI",      "", "JUD", "alerte",  true),
        new("2026IP00214", "Injonction de payer - LEROY Michel",      "", "JUD", "attente", true),
        new("2026M00045",  "Expertise avant proces - GARAGE CENTRAL", "", "JUD", "action",  true),
        new("2026C00059",  "Conciliation - ATELIER DU MEUBLE",        "", "JUD", "neutre",  true),
        // Dossiers de test, un par TYPE_DOSSIER (demande du 18/09/2026) :
        // 01 = A, 02 = B, 03 = C, 04 = D.
        new("01", "Dossier test type A - personne physique",   "", "RCS", "neutre", TypeDossier: "A"),
        new("02", "Dossier test type B - societe commerciale", "", "RCS", "neutre", TypeDossier: "B"),
        new("03", "Dossier test type C - GIE",                 "", "RCS", "neutre", TypeDossier: "C"),
        new("04", "Dossier test type D - societe civile",      "", "RCS", "neutre", TypeDossier: "D")
    ];

    /// <summary>
    /// Le fonds d'affaires REELLEMENT interrogeable : le jeu de demonstration
    /// PLUS les dossiers ouverts pendant la session par l'ecran « Ouvrir un
    /// dossier ». Un dossier qu'on vient d'ouvrir doit se retrouver ici,
    /// sinon l'ouverture n'aurait aucune suite.
    /// </summary>
    private Dossier[] Fonds =>
    [
        .. AffairesOuvertes.Toutes.Select(a =>
               new Dossier(a.Numero, a.Objet, a.Siren, a.Type, a.Statut)),
        .. FondsDemo
    ];

    /// <summary>Les TRAITEMENTS dont le libelle porte ce qui est tape. Le menu de
    /// gauche suppose qu'on sait deja quel outil repond au besoin ; la recherche,
    /// elle, part des mots de l'utilisateur. Taper « facture » doit donc proposer
    /// l'ecran des factures, exactement comme taper un SIREN propose l'affaire.</summary>
    private Proc[] TraitementsPour(string terme)
        => [.. Groupes.SelectMany(g => g.Procs)
                      .Where(p => PerimetreProfil.Visible(p.Code, Domaines))
                      .Where(p => RecherchePalette.Correspond(terme, p.Libelle))
                      .DistinctBy(p => p.Code)
                      .Take(4)];

    /// <summary>Un ELEMENT de l'arbre trouve par la recherche : l'affaire qui
    /// le porte et son chemin de libelles depuis la racine.</summary>
    private record ElementTrouve(Dossier Affaire, string[] Chemin);

    /// <summary>L'affaire dans l'arbre de laquelle on cherche un numero de
    /// depot / formalite / liasse : celle deja ouverte, sinon la premiere
    /// affaire ayant un dossier RCS.</summary>
    private Dossier AffaireCherchee
        => DossierOuvert ?? Fonds.FirstOrDefault(d => !d.SansDossierRcs) ?? Fonds[0];

    /// <summary>L'arbre que la recherche fouille : celui de l'affaire ouverte (avec
    /// ce que l'agent y a ajoute ou masque), sinon celui de l'affaire cherchee.</summary>
    private NoeudGed ArbreCherche()
    {
        var affaire = AffaireCherchee;
        return ReferenceEquals(affaire, DossierOuvert)
            ? Arbre
            : ArbreDemo.Racine(affaire.Numero, affaire.Objet, affaire.Siren,
                               avecDossierRcs: !affaire.SansDossierRcs);
    }

    /// <summary>Ouvre l'affaire et deplie l'arbre JUSQU'A l'element tape.</summary>
    private void OuvrirElementTrouve(ElementTrouve e)
    {
        OuvrirDossier(e.Affaire);
        var suite = RechercheArbre.Suivre(Arbre, e.Chemin);
        if (suite is null) return;
        NoeudsOuverts.Add(Arbre);
        foreach (var n in suite) NoeudsOuverts.Add(n);
        OuvrirApercu(suite[^1]);
    }

    // ================= Visionneuse =================
    private Dossier? DossierOuvert;

    // L'arbre de l'affaire ouverte, sa selection, et les dossiers deplies.
    private NoeudGed Arbre = ArbreDemo.Racine("2026B01067", "AMIENS BILLARDS", "109 678 235");
    private NoeudGed? NoeudSelectionne;
    private readonly HashSet<NoeudGed> NoeudsOuverts = AccesGed.NouveauxMasques();

    // Le tableau des editions de l'affaire et la ligne selectionnee.
    private LigneEdition[] Editions = [];

    /// <summary>
    /// Le tableau des editions n'est plus la colonne de droite de
    /// l'affaire : c'est un ecran a part, ouvert par un bouton.
    /// </summary>

    // ================= Colonnes de la vue affaire =================
    // Le panneau de droite se ferme (croix) et la colonne du milieu
    // s'ajuste en glissant le separateur. Largeur en pixels, bornee.
    // Ferme au depart : la colonne n'existe que pour montrer une piece.
    private bool GuideVisible;
    // Second locataire de la colonne de droite : le formulaire de l'etape liee
    // a la piece ouverte. Exclusif avec l'apercu -- « Editer » bascule dessus,
    // « Voir le document » revient.
    private bool DonneesVisible = true;
    private bool Redimensionne;
    // Quelle poignee est saisie : 1 = entre l'arbre et les donnees,
    // 2 = entre les donnees et le document.
    private int ColonneRedim = 1;
    private double LargeurArbre = 400;
    private double LargeurDonnees = 420;
    private const double LargeurArbreMin = 240;
    private const double LargeurArbreMax = 1100;
    private const double LargeurDonneesMin = 280;
    private const double LargeurDonneesMax = 1100;
    /// <summary>La colonne d'une poignee : le VIDE entre deux cartes de la vue du dossier,
    /// le meme que celui qui separe la colonne de suivi (.rig-vged__suivi, margin-left).
    /// Avec 6 px ici contre 10 px a cote du suivi, les ecarts etaient inegaux (passe de finition du 19/11/2026).</summary>
    private const string Gouttiere = "10px";
    /// <summary>Les fenetres de document actuellement sorties. PLUSIEURS
    /// documents peuvent l'etre en meme temps ; chacune porte son rang, qui
    /// est aussi la clef de sa position memorisee.</summary>
    private readonly List<FenetreDocument> FenetresDetachees = new();

    private string StyleCorps
    {
        get
        {
            // QUATRE zones cote a cote : l'arbre, les donnees, le document et,
            // toujours a droite, la colonne de SUIVI (onglets Suivi / Fil,
            // proposition 3 du /draft du 19/11/2026). L'arbre se replie, les
            // donnees et le document se ferment ; les colonnes se recalculent alors.
            static string Px(double v) =>
                v.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "px";

            var morceaux = new List<string>();
            if (!DonneesVisible && !GuideVisible)
            {
                morceaux.Add("minmax(0, 1fr)");
            }
            else
            {
                if (!ArbreReplie) morceaux.Add(Px(LargeurArbre));
                // Sans document a droite, les donnees prennent toute la largeur
                // restante : une largeur fixe laissait un grand vide (demande du 2026-09-15).
                // Arbre replie, la premiere poignee part avec lui.
                if (DonneesVisible)
                {
                    if (!ArbreReplie) morceaux.Add(Gouttiere);
                    morceaux.Add(GuideVisible ? Px(LargeurDonnees) : "minmax(0, 1fr)");
                }
                if (GuideVisible)
                {
                    if (!ArbreReplie || DonneesVisible) morceaux.Add(Gouttiere);
                    morceaux.Add("minmax(0, 1fr)");
                }
            }
            morceaux.Add(LargeurSuivi);
            // Arbre replie : une premiere rangee porte le chemin de l'element choisi,
            // au-dessus des donnees et du document ; la colonne de suivi prend les deux.
            var colonnes = "grid-template-columns: " + string.Join(" ", morceaux)
                         + "; grid-template-rows: " + (ArbreReplie ? "auto minmax(0, 1fr)" : "minmax(0, 1fr)");
            // Pendant le glissement, le navigateur selectionnerait le texte
            // survole : la vue se couvrait de surlignage bleu.
            return Redimensionne ? colonnes + "; user-select: none" : colonnes;
        }
    }

    // Le deplacement se calcule en RELATIF, a partir du point de saisie : la
    // position absolue du separateur dans la page n'est pas connue cote C# et
    // une constante (largeur du menu de gauche) donne un decalage -- la vue
    // affaire occupe tout l'ecran, sans menu.
    private double XDepart;
    private double LargeurDepart;

    private void DebutRedim(double clientX)
    {
        XDepart = clientX;
        LargeurDepart = LargeurArbre;
        ColonneRedim = 1;
        Redimensionne = true;
    }

    /// <summary>Seconde poignee : entre la colonne des donnees et le document.</summary>
    private void DebutRedim2(double clientX)
    {
        XDepart = clientX;
        LargeurDepart = LargeurDonnees;
        ColonneRedim = 2;
        Redimensionne = true;
    }

    private void PendantRedim(double clientX)
    {
        if (!Redimensionne) return;
        var largeur = LargeurDepart + (clientX - XDepart);
        if (ColonneRedim == 2)
            LargeurDonnees = Math.Clamp(largeur, LargeurDonneesMin, LargeurDonneesMax);
        else
            LargeurArbre = Math.Clamp(largeur, LargeurArbreMin, LargeurArbreMax);
    }

    /// <summary>
    /// Ce qui est clique dans l'arbre choisit l'etape saisie a droite.
    /// La correspondance suit les dossiers de PROC_VGED (v2) : chaque
    /// dossier documentaire alimente une table de la demande.
    /// </summary>
    private Etape EtapeDuNoeud(NoeudGed? noeud)
    {
        var code = "DEMANDE";
        var texte = (noeud?.Libelle ?? "").ToLowerInvariant();

        if (noeud is null) code = "DEMANDE";
        else if (texte.Contains("statut") || texte.Contains("souscripteur")
              || texte.Contains("capital")) code = "CAPITAL";
        else if (texte.Contains("bail") || texte.Contains("etablissement")
              || texte.Contains("siege") || texte.Contains("depots d'actes")
              || texte.Contains("depot n")) code = "SIEGE";
        else if (texte.Contains("non-condamnation") || texte.Contains("dirigeant")
              || texte.Contains("president") || texte.Contains("identite du"))
            code = "DIRIGEANTS";
        else if (texte.Contains("kbis") || texte.Contains("dossier rcs")
              || texte.Contains("m0") || texte.Contains("immatriculation"))
            code = "IDENTITE";
        else if (texte.Contains("comptes annuels") || texte.Contains("bilan")
              || texte.Contains("activite")) code = "ACTIVITE";
        else if (texte.Contains("inscription") || texte.Contains("privilege")
              || texte.Contains("judiciaire") || texte.Contains("ordonnance")
              || texte.Contains("divergence") || texte.Contains("requisition")
              || texte.Contains("mandataire")) code = "CONTROLE";
        else if (texte.Contains("formalite") || texte.Contains("demande"))
            code = "DEMANDE";

        return Etapes.First(e => e.Code == code);
    }

    /// <summary>
    /// L'etape du panneau de droite : celle choisie dans la frise en pied
    /// si l'utilisateur y a clique, sinon celle deduite de l'arbre.
    /// </summary>
    private string? CodeEtapeGuide;

    private Etape EtapeGuide()
        => CodeEtapeGuide is not null
            ? Etapes.FirstOrDefault(e => e.Code == CodeEtapeGuide) ?? EtapeDuNoeud(NoeudSelectionne)
            : EtapeDuNoeud(NoeudSelectionne);

    private void ChoisirEtapeGuide(Etape etape) => CodeEtapeGuide = etape.Code;

    private void SuivreEtape(Etape etape)
    {
        var i = Array.IndexOf(Etapes, etape);
        if (i < 0 || (i == EtapeCourante && EtapesOuvertes.Contains(i))) return;
        EtapeCourante = i;
        EtapesOuvertes.Add(i);
        EtapeADefiler = true;
    }

    private void ChoisirNoeud(NoeudGed n)
    {
        NoeudSelectionne = n;
        // Un clic dans l'arbre reprend la main sur la frise.
        CodeEtapeGuide = null;
    }

    private void OuvrirDossier(Dossier d)
    {
        DossierOuvert = d;

        // Historique de consultation : horodate a l'instant du clic, jamais
        // reconstitue a l'affichage. C'est ce que la tuile « Mes affaires
        // recentes » lit.
        Etat.Consulter(d.Numero, d.Objet, d.Siren, d.Type, d.Statut);

        // Ouvrir une affaire tombe sur l'arbre, jamais sur la saisie : c'est
        // l'ecran d'entree de VGED dans RIG v2.
        EtapeCourante = 0;
        EtapesOuvertes.Clear();
        EtapesOuvertes.Add(0);
        Pagination++;

        // Une affaire purement judiciaire n ouvre QUE la branche Judiciaire :
        // v2 n ajoute « Dossier RCS » que si l affaire a un dossier RCS.
        Arbre = ArbreDemo.Racine(d.Numero, d.Objet, d.Siren,
                                 avecDossierRcs: !d.SansDossierRcs);
        Editions = ArbreDemo.Editions(d.Numero, d.Objet);

        // L'arbre est reconstruit a neuf : on y repose ce que l'utilisateur
        // avait ajoute, masque ou renomme lors d'une session precedente.
        PersistanceGed.Appliquer(Arbre, Etat.ArbreGedDe(d.Numero),
                                 AjoutsArbre, MasquesArbre, LibellesArbre);

        // La racine et tous les dossiers servis sont deplies d'entree :
        // l'arbre s'ouvre sur du contenu, pas sur une ligne fermee.
        NoeudsOuverts.Clear();
        NoeudsOuverts.Add(Arbre);
        foreach (var n in Arbre.Descendants().Where(n => !n.EstPiece && n.NbPieces > 0))
            NoeudsOuverts.Add(n);

        NoeudSelectionne = Arbre.Descendants().FirstOrDefault(n => n.EstPiece);

        // KBIS A JOUR (demande du 18/09/2026) : le dossier s'ouvre sur
        // « Informations du dossier » au milieu et le KBIS en vigueur a droite.
        var kbis = Arbre.Descendants().FirstOrDefault(n => n.Libelle == ArbreDemo.LibelleKbisAJour);
        var fiche = Arbre.Descendants().FirstOrDefault(n => n.EstFiche
                                                          && n.Libelle == InformationsDossier.LibelleNoeud);
        if (kbis is not null && fiche is not null)
        {
            NoeudSelectionne = fiche;
            DonneesVisible = true;
            ApercuNoeud = kbis;
            ApercuMessage = "";
            GuideVisible = true;
        }
    }

    private void FermerDossier()
    {
        DossierOuvert = null;
        NoeudSelectionne = null;
    }

    // ================= Ecran de procedure =================

    /// <summary>
    /// Dit POURQUOI une procedure du menu n'a pas d'ecran, quand la raison
    /// est etablie dans les sources de RIG v2. Vide sinon.
    /// </summary>
    private string? MotifNonMaquette(string code)
    {
        // Les motifs ETABLIS dans les sources de RIG v2 restent ecrits ici, un
        // par un : ils disent un constat, pas une place dans le menu.
        var etabli = code switch
        {
            "PROC_CREANCE" =>
                "L'ancienne application ne contenait aucun ecran pour les creances : "
                + "ni champ de recherche, ni colonne, ni action. Il n'y a donc rien a "
                + "reprendre, et en inventer donnerait un ecran trompeur. Cet ecran "
                + "reste a definir avec le greffe.",
            _ => null
        };
        if (etabli is not null) return etabli;

        // Le motif se LIT dans le menu, il ne s'invente pas : le besoin servi
        // par l'entree (« Detail ») et sa place dans le metier y sont deja
        // ecrits. Dire cela vaut mieux que le texte generique « Cet ecran n'est
        // pas encore disponible », qui ne distingue pas 125 entrees.
        foreach (var moment in Moments)
        {
            foreach (var sous in moment.Sous)
            {
                foreach (var entree in sous.Entrees)
                {
                    if (entree.Code != code) continue;
                    return $"Ce que cette entree doit servir : {entree.Detail}. "
                         + $"Elle se range dans « {moment.Titre} › {sous.Titre} » "
                         + $"(code {entree.Code}). L'ecran n'est pas encore porte depuis "
                         + "RIG v2 ; aucun ecran voisin n'est affiche a sa place, cela "
                         + "donnerait une fausse idee de ce que fait la maquette.";
                }
            }
        }

        return null;
    }

    // Le cadre et l aiguillage vivent ici ; les criteres, les grilles et
    // les jeux de donnees sont dans Components/Ecrans/ et Metier/JeuDemo.cs.

    /// <summary>
    /// LA BARRE D ONGLETS (demande du 17/09/2026 : « un systeme d onglet comme
    /// un navigateur web »). Avant, une seule vue pouvait etre ouverte a la
    /// fois : en ouvrir une autre effacait la premiere. Desormais chaque vue
    /// ouverte prend un onglet ; la logique (ouvrir, activer, fermer,
    /// reordonner, arracher) vit dans EtatOnglets, verifiable hors interface.
    /// </summary>
    private readonly EtatOnglets Onglets = new();

    /// <summary>Le Proc derriere chaque code d onglet : l onglet ne retient que
    /// le code, c est lui la clef stable (le libelle, lui, peut changer).</summary>
    private readonly Dictionary<string, Proc> ProcsConnues = new();

    /// <summary>La vue MONTREE dans cette fenetre. Null quand on est sur
    /// l accueil ou quand l onglet actif a ete sorti dans sa propre fenetre --
    /// les onglets, eux, restent ouverts.</summary>
    private Proc? ProcOuverte
        => Onglets.Montre is { } o && ProcsConnues.TryGetValue(o.Code, out var p) ? p : null;

    private void OuvrirProc(Proc proc)
    {
        ProcsConnues[proc.Code] = proc;
        // Deja ouverte ? On revient dessus au lieu d en creer un doublon.
        Onglets.Ouvrir(proc.Code, proc.Libelle);
        DossierOuvert = null;
        RetenirAgencement();
    }


    // ============ ONGLETS : GLISSER, REORDONNER, ARRACHER ============
    // VueInitiale et ModeFenetreVue (les [Parameter] du .razor) sont donnes au
    // constructeur : voir l'en-tete de la classe.

    /// <summary>Les fenetres d onglets actuellement sorties.</summary>
    private readonly List<FenetreVue> FenetresVues = new();

    private string? GlisseCode;
    private double GlisseDepartX, GlisseDepartY;
    private bool GlisseEnCours;
    private double LargeurBarre, HauteurPage;
    /// <summary>Bas de la barre d onglets dans la page (mesure au debut du geste).</summary>
    private double BasBarreOnglets = double.MaxValue;
    private List<(double Gauche, double Droite)> CasesOnglets = new();

    /// <summary>Ce que la page mesure pour nous : la taille de la zone visible
    /// et la place de chaque onglet.</summary>
    public sealed record MesureBarre(double[] Page, double[][] Cases);

    private void DebutGlisse(OngletVue onglet, double clientX, double clientY, MesureBarre? mesure)
    {
        GlisseCode = onglet.Code;
        GlisseDepartX = clientX;
        GlisseDepartY = clientY;
        GlisseEnCours = false;
        CasesOnglets = new();
        if (mesure is null) return;
        LargeurBarre = mesure.Page.Length > 0 ? mesure.Page[0] : 0;
        HauteurPage = mesure.Page.Length > 1 ? mesure.Page[1] : 0;
        BasBarreOnglets = mesure.Page.Length > 2 ? mesure.Page[2] : double.MaxValue;
        CasesOnglets = mesure.Cases.Where(c => c.Length >= 2)
                                   .Select(c => (c[0], c[1])).ToList();
    }

    private void GlisseOnglet(double clientX, double clientY)
    {
        if (GlisseCode is null) return;
        // Un simple clic ne doit pas reordonner : on n entre en glissement
        // qu au-dela de quelques pixels.
        if (!GlisseEnCours
            && Math.Abs(clientX - GlisseDepartX) + Math.Abs(clientY - GlisseDepartY) < 6) return;
        GlisseEnCours = true;

        // Tire HORS de la barre d onglets (vers le bas, ou hors de la fenetre) :
        // l onglet part TOUT DE SUITE dans sa fenetre, qui nait sous le pointeur
        // et le suit jusqu au lacher -- comme un navigateur. Attendre le lacher
        // laissait l utilisateur glisser dans le vide, sans rien voir bouger.
        if (clientY > BasBarreOnglets + ArrachageOnglet.MargeDefaut
            || ArrachageOnglet.EstSorti(clientX, clientY, 0, 0, LargeurBarre, HauteurPage))
        {
            var code = GlisseCode;
            GlisseCode = null;
            GlisseEnCours = false;
            FinsDeGlisse++;
            DetacherOnglet(code, suivreCurseur: true);
            RetenirAgencement();
            return;
        }

        var de = Onglets.IndexDe(GlisseCode);
        // Les places mesurees au debut du geste servent de reperes : elles ne
        // sont pas remesurees a chaque pixel, la barre bougerait sous le doigt.
        var vers = EtatOnglets.IndexCible(CasesOnglets, clientX);
        if (de >= 0 && vers != de) Onglets.Deplacer(de, vers);
    }

    private void FinGlisse(double clientX, double clientY)
    {
        var code = GlisseCode;
        GlisseCode = null;
        if (code is null) return;

        if (!GlisseEnCours)
        {
            Onglets.Activer(code);
        }
        else if (ArrachageOnglet.EstSorti(clientX, clientY, 0, 0, LargeurBarre, HauteurPage))
        {
            // LACHE HORS DE LA FENETRE : l onglet part dans sa propre fenetre,
            // qu on peut poser sur le second ecran.
            DetacherOnglet(code);
        }
        GlisseEnCours = false;
        RetenirAgencement();
    }

    private void FermerOnglet(string code)
    {
        var fenetre = FenetresVues.FirstOrDefault(f => f.Code == code);
        Onglets.Fermer(code);
        // Fermer l onglet ferme AUSSI sa fenetre : sinon la fenetre resterait
        // orpheline, sans onglet pour la retrouver.
        fenetre?.Close();
        RetenirAgencement();
    }

    /// <summary>Sort l onglet dans sa propre fenetre. L onglet reste dans la
    /// barre, grise : c est ce qui dit ou la vue est partie, et le clic dessus
    /// ramene la fenetre au premier plan.</summary>
    private void DetacherOnglet(string code, bool suivreCurseur = false)
    {
        if (!ProcsConnues.TryGetValue(code, out var proc)) return;
        if (!Onglets.Detacher(code)) return;
        OuvrirFenetreVue(proc, suivreCurseur);
    }

    private void OuvrirFenetreVue(Proc proc, bool suivreCurseur = false)
    {
        var fenetre = new FenetreVue(proc.Code, proc.Libelle, FenetresVues.Count + 1, suivreCurseur);
        fenetre.Closed += (_, _) =>
        {
            FenetresVues.Remove(fenetre);
            if (VueFermee) return;
            // Fermer la fenetre RAMENE la vue dans son onglet, comme un
            // navigateur : rien n est perdu.
            Planifier(() =>
            {
                Onglets.Rattacher(proc.Code);
                RetenirAgencement();
            });
        };
        FenetresVues.Add(fenetre);
        fenetre.Show();
    }

    /// <summary>Clic sur un onglet : on l active, ou on ramene sa fenetre
    /// devant s il a ete sorti.</summary>
    private void ActiverOnglet(OngletVue onglet)
    {
        if (onglet.Detache)
        {
            var fenetre = FenetresVues.FirstOrDefault(f => f.Code == onglet.Code);
            if (fenetre is not null) { fenetre.Activate(); return; }
            Onglets.Rattacher(onglet.Code);
        }
        else Onglets.Activer(onglet.Code);
        RetenirAgencement();
    }

    /// <summary>Retient l AGENCEMENT : les onglets, leur ordre, celui qui est
    /// actif, et ceux qui sont sortis en fenetre. C est ce qui se rejoue au
    /// demarrage suivant.</summary>
    private bool EnRestauration;

    private void RetenirAgencement()
    {
        // Pendant la restauration on REJOUE l agencement : le reecrire a chaque
        // onglet repose ecraserait le fichier avant meme de l avoir fini de lire.
        if (ModeFenetreVue || EnRestauration) return;
        try
        {
            AccueilEtat.MemoriserOnglets(
                Onglets.Codes(),
                Onglets.Actuel?.Code ?? string.Empty,
                Onglets.Onglets.Where(o => o.Detache).Select(o => o.Code).ToList());
        }
        catch (System.IO.IOException) { }
    }

    /// <summary>Rejoue l agencement retenu : memes onglets, meme ordre, meme
    /// onglet actif, et les vues sorties retrouvent leur fenetre.</summary>
    private void RestaurerAgencement()
    {
        if (ModeFenetreVue)
        {
            // Une fenetre d onglet arrache ne montre QUE son ecran.
            if (!string.IsNullOrWhiteSpace(VueInitiale)) OuvrirCode(VueInitiale!);
            return;
        }

        EnRestauration = true;
        try { RejouerOnglets(); } finally { EnRestauration = false; }
    }

    private void RejouerOnglets()
    {
        foreach (var code in Etat.OngletsVue.ToList()) OuvrirCode(code);
        foreach (var code in Etat.VuesDetachees.ToList()) DetacherOnglet(code);

        if (!string.IsNullOrWhiteSpace(Etat.OngletActif)
            && Onglets.IndexDe(Etat.OngletActif) >= 0
            && !Etat.VuesDetachees.Contains(Etat.OngletActif))
            Onglets.Activer(Etat.OngletActif);
        else Onglets.Masquer();
    }

    /// <summary>L'affaire depuis laquelle un traitement a ete lance. Sans elle,
    /// cliquer une action du contexte FERMAIT l'affaire : on repartait de zero,
    /// exactement ce que la navigation par objet cherche a supprimer.</summary>
    private Dossier? RetourAffaire;

    /// <summary>Ouvre un traitement SANS perdre l'affaire : elle se rouvre d'elle-meme
    /// quand on referme l'ecran.</summary>
    private void OuvrirProcDepuisAffaire(Proc proc)
    {
        var depuis = DossierOuvert;
        OuvrirProc(proc);
        RetourAffaire = depuis;
    }

    /// <summary>
    /// La MAISON, premier onglet de la barre du haut (19/11/2026) : ramene a
    /// l'accueil SANS fermer les onglets -- comme un navigateur, on y revient
    /// d'un clic. L'affaire ouverte, elle, n'a pas d'onglet : elle se ferme,
    /// comme par sa croix.
    /// </summary>
    private void AllerALAccueil()
    {
        Onglets.Masquer();
        FermerDossier();
        RetenirAgencement();
    }

    private void FermerProc()
    {
        if (Onglets.Actuel is { } courant) Onglets.Fermer(courant.Code);
        RetenirAgencement();
        if (Onglets.Montre is null && RetourAffaire is not null)
        {
            DossierOuvert = RetourAffaire;
            RetourAffaire = null;
        }
    }

    /// <summary>Les traitements proposes DANS l'affaire ouverte. L'utilisateur n'a
    /// plus a savoir quel outil repond a son besoin : l'objet ouvert porte ses
    /// propres actions. Le tri suit le domaine de l'affaire (RCS ou judiciaire),
    /// car proposer « Depots judiciaires » sur une immatriculation RCS serait du
    /// bruit. Les libelles viennent du menu : une seule source, jamais deux noms
    /// pour la meme chose.</summary>
    private Proc[] ActionsDe(Dossier d)
    {
        var codes = d.Type == "JUD"
            ? new[] { "PROC_DEPOTJUD", "PROC_ACTEUR_JUD", "PROC_AUDIENCE", "PROC_GEDJUD", "PROC_FACTURE" }
            : new[] { "PROC_DEMANDE", "PROC_ACTES_NUM", "PROC_DOC_DEMAT", "PROC_CLOT", "PROC_FACTURE" };

        var connus = Groupes.SelectMany(g => g.Procs).ToDictionary(p => p.Code);
        return codes.Where(connus.ContainsKey)
                    .Where(c => PerimetreProfil.Visible(c, Domaines))
                    .Select(c => connus[c]).ToArray();
    }

    /// <summary>
    /// Point d entree commun aux trois ecrans : ils remontent un NUMERO de
    /// dossier, la page se charge de retrouver l affaire et d ouvrir la
    /// visionneuse. Les ecrans n ont ainsi rien a savoir de la visionneuse.
    /// </summary>
    private void OuvrirParNumero(string numeroDossier)
    {
        if (string.IsNullOrWhiteSpace(numeroDossier)) return;

        var connu = Fonds.FirstOrDefault(f => f.Numero == numeroDossier);
        if (connu is null)
        {
            var demande = JeuDemo.Demandes.FirstOrDefault(d => d.Dossier == numeroDossier);
            connu = new Dossier(numeroDossier,
                                demande?.Nom ?? numeroDossier,
                                demande?.Siren ?? string.Empty,
                                "RCS",
                                demande?.Statut ?? "action");
        }

        // On QUITTE la vue courante sans fermer les onglets : comme un
        // navigateur, ils restent la et on y revient d un clic.
        Onglets.Masquer();

        // UNE TACHE NE SUIT PAS SUR UNE AUTRE AFFAIRE. Quitter le dossier de
        // la tache pour un autre referme le mode replie : sans cela l arbre
        // du nouveau dossier restait filtre par la recette d un autre.
        if (RecetteActive is not null && AffaireDeLaTache != numeroDossier)
        {
            RecetteActive = null;
            AffaireDeLaTache = null;
            ToutLeDossier = false;
        }

        // OUVRIR UN DOSSIER QUI A UNE TACHE EN ATTENTE, C EST OUVRIR SA TACHE.
        // Le greffier qui entre par le courrier ou par la file tombait sur le
        // dossier ENTIER : 521 champs pour une demande qui n en porte que
        // deux. Le mode replie n existait que par l alerte de l accueil. Il
        // est desormais pose ICI, au point d entree commun a tous les ecrans,
        // donc le chemin est le meme d ou qu on vienne. Le dossier reste
        // entier : le bandeau de tache porte « Afficher tout le dossier ».
        if (RecetteActive is null && TacheEnAttente(numeroDossier) is { } attendue)
        {
            OuvrirTache(attendue.Recette, numeroDossier);
            return;
        }

        OuvrirDossier(connu);
    }

    /// <summary>La tache encore a faire sur cette affaire, s il y en a une.
    /// Lue dans le MEME releve que les alertes de l accueil : une tache close
    /// n en ressort pas, et l ecran d ou l on vient n a rien a declarer.</summary>
    private (RecetteTache Recette, string Code)? TacheEnAttente(string numeroDossier)
    {
        foreach (var a in Accueil.AlertesOuvertes())
        {
            if (a.Tache is not { } t || t.Dossier != numeroDossier) continue;
            if (RecettesTaches.ParCode(t.Code) is { } r) return (r, t.Code);
        }
        return null;
    }

    // ================= Saisie guidee (la frise) =================
    // Les etapes et leurs champs sont calques sur les tables reelles du
    // dossier RCS de RIG v2 (D:/RigApplication/Source/BaseDeDonnees/
    // RigDatabase/dbo/Tables) : DEMANDE, DOSSIER_RCS, CAPITAL, SIEGER +
    // ETABLISSEMENT, ACTIVITE, DIRIGEANT, OBSERVATION_RCS.
    // La colonne d'origine est rappelee sous chaque champ : c'est ce qui
    // permet de brancher la saisie sur la base sans deviner.

    // Ecran plein de la saisie guidee : il n'est plus un onglet, il s'ouvre
    // par le bouton « Controle du dossier » (la colonne de droite affiche
    // desormais le document, pas le formulaire).
    // Les etapes vivent dans la colonne du milieu : elles existent des qu'une
    // affaire est ouverte et que la colonne n'est pas fermee.
    private bool EtapesAffichees => DossierOuvert is not null && DonneesVisible;
    private bool EtapeADefiler;
    private int EtapeCourante;
    // Les etapes DEPLIEES de l'accordeon (plusieurs a la fois). Seules elles
    // portent leurs champs dans la vue : une etape repliee n'est qu'une ligne.
    private readonly HashSet<int> EtapesOuvertes = [0];

    // Chaque etape garde SA fiche affichee : les 7 etapes sont a l'ecran en
    // meme temps, un index unique les ferait basculer ensemble.
    private readonly Dictionary<string, int> FicheParEtape = new();
    // Les controles de validite parcourent toutes les fiches d'une etape :
    // pendant ce parcours l'index affiche est mis de cote.
    private int? FicheForcee;

    // Ce qui est saisi est CONSERVE : il vit dans accueil.json, range par
    // affaire puis par etape. Une etape marquee Multiple porte plusieurs
    // fiches (plusieurs dirigeants, plusieurs etablissements), les autres
    // une seule.
    private string CleAffaire => DossierOuvert?.Numero ?? "sans-affaire";

    private List<Dictionary<string, string>> Fiches(string etape)
    {
        if (!Etat.Saisies.TryGetValue(CleAffaire, out var parEtape))
        {
            parEtape = new Dictionary<string, List<Dictionary<string, string>>>();
            Etat.Saisies[CleAffaire] = parEtape;
        }
        if (!parEtape.TryGetValue(etape, out var fiches) || fiches.Count == 0)
        {
            fiches = [NouvelleFiche(etape)];
            parEtape[etape] = fiches;
        }
        return fiches;
    }

    /// <summary>
    /// Une fiche neuve part PREREMPLIE : chaque champ qui a une valeur de
    /// depart l'a deja ecrite. Le redacteur corrige ce qui differe au lieu de
    /// tout taper — et la valeur existe vraiment dans la fiche, donc elle part
    /// au document et a l'enregistrement.
    /// </summary>
    private Dictionary<string, string> NouvelleFiche(string etape)
    {
        var fiche = new Dictionary<string, string>();
        var e = Etapes.FirstOrDefault(x => x.Code == etape);
        if (e is null) return fiche;
        foreach (var c in e.Champs)
        {
            if (c.Defaut.Length > 0) fiche[c.Colonne] = c.Defaut;
        }
        return fiche;
    }

    private int IndexFiche(Etape etape)
    {
        var fiches = Fiches(etape.Code);
        if (FicheForcee is { } forcee) return Math.Clamp(forcee, 0, fiches.Count - 1);
        var i = FicheParEtape.TryGetValue(etape.Code, out var v) ? v : 0;
        return Math.Clamp(i, 0, fiches.Count - 1);
    }

    private void ChoisirFiche(Etape etape, int index)
        => FicheParEtape[etape.Code] = index;

    // Les champs d'une etape (ChampsSaisie du .razor) sont rendus par
    // web/src/plan/Etapes.tsx (fonction Champs), une seule fois pour toutes les
    // pages ; leur vue est calculee par ChampsVue (Pont/PlanTravailVue.cs) avec
    // les memes regles : Visible, RefusDe, RequisManquant, Valeur.

    private Dictionary<string, string> FicheVue(Etape etape)
        => Fiches(etape.Code)[IndexFiche(etape)];

    private string Valeur(Etape e, Champ c)
        => FicheVue(e).TryGetValue(c.Colonne, out var v) ? v : string.Empty;

    /// <summary>
    /// Chaque frappe porte la valeur sur la previsualisation (et les fenetres
    /// detachees) sans attendre la sortie du champ. L'enregistrement et le
    /// journal restent sur « change » : un evenement par frappe serait illisible.
    /// </summary>
    private void EcrireEnDirect(Etape e, Champ c, string? v)
    {
        FicheVue(e)[c.Colonne] = v ?? string.Empty;
        PublierDocument();
    }

    private static bool EstKbis(NoeudGed n)
        => n.Libelle.Contains("kbis", StringComparison.OrdinalIgnoreCase);

    private string LireSaisie(string codeEtape, string colonne)
    {
        var etape = Etapes.FirstOrDefault(x => x.Code == codeEtape);
        return etape is not null && FicheVue(etape).TryGetValue(colonne, out var v) ? v : string.Empty;
    }

    private string DenominationAffaire()
    {
        var objet = DossierOuvert?.Objet ?? "";
        var tiret = objet.LastIndexOf(" - ", StringComparison.Ordinal);
        return tiret >= 0 ? objet[(tiret + 3)..] : objet;
    }

    private void Ecrire(Etape e, Champ c, string? v)
    {
        FicheVue(e)[c.Colonne] = v ?? string.Empty;
        PublierDocument();
        Etat.Enregistrer();
        // Journal au grain de l ETAPE, pas du champ : un evenement par frappe
        // rendrait l historique illisible.
        Etat.Journaliser(CleAffaire, $"Saisie de l'etape {e.Titre}");
    }

    // Avancement d'une etape : toutes fiches confondues, sinon une deuxieme
    // fiche vide ferait reculer un pas deja complet sans qu'on comprenne.
    private (int Remplis, int Total) Avancement(Etape e)
    {
        var fiches = Fiches(e.Code);
        var remplis = fiches.Sum(f => e.Champs.Count(c =>
            f.TryGetValue(c.Colonne, out var v) && !string.IsNullOrWhiteSpace(v)));
        return (remplis, e.Champs.Length * fiches.Count);
    }

    private void AjouterFiche(Etape e)
    {
        var fiches = Fiches(e.Code);
        fiches.Add(NouvelleFiche(e.Code));
        FicheParEtape[e.Code] = fiches.Count - 1;
        Etat.Enregistrer();
    }

    private void SupprimerFiche(Etape e)
    {
        var fiches = Fiches(e.Code);
        if (fiches.Count <= 1) return;
        fiches.RemoveAt(IndexFiche(e));
        FicheParEtape[e.Code] = Math.Max(0, IndexFiche(e) - 1);
        Etat.Enregistrer();
    }

    /// <summary>
    /// Aller a une etape (lien vers un defaut, piece ouverte) : elle se DEPLIE
    /// dans l'accordeon, sans replier les autres, et la page la fait defiler
    /// jusqu'a elle.
    /// </summary>
    private void AllerEtape(int index)
    {
        index = Math.Clamp(index, 0, Etapes.Length - 1);
        EtapeCourante = index;
        EtapesOuvertes.Add(index);
        Defiler(index, doux: true);
    }

    /// <summary>Un clic sur la ligne d'une etape : la deplie (elle devient la
    /// courante et la page l'amene en vue) ou la replie.</summary>
    private void BasculerEtape(int index)
    {
        index = Math.Clamp(index, 0, Etapes.Length - 1);
        if (EtapesOuvertes.Remove(index)) return;
        AllerEtape(index);
    }

    /// <summary>Remontee par la page (web/src/plan/pagination.ts) : l'etape la plus visible a change.</summary>
    private void EtapeVisible(int index)
    {
        if (index == EtapeCourante) return;
        EtapeCourante = Math.Clamp(index, 0, Etapes.Length - 1);
    }

    // ---- Controle de validite --------------------------------------
    // Deux natures de defaut, volontairement distinctes :
    //  - REQUIS MANQUANT : le champ est vide alors qu'il est obligatoire ;
    //  - VALEUR REFUSEE  : la valeur saisie ne tient pas (cle SIREN, date
    //    posterieure a aujourd'hui, code NAF mal forme...).
    // Melanger les deux ferait clignoter en rouge un formulaire vierge.

    private static readonly DateTime Ajd = DateTime.Today;

    private string? RefusDe(Etape e, Champ c)
    {
        if (!Visible(e, c)) return null;
        var motif = ValiditeSaisie.Refus(c.Controle, Valeur(e, c), Ajd);
        if (motif is not null) return motif;

        // Coherences entre deux dates d'une meme fiche.
        var fiche = FicheVue(e);
        if (c.Colonne == "DSSRC_DATE_EFFET")
        {
            fiche.TryGetValue("DSSRC_DATE_IMMAT", out var immat);
            fiche.TryGetValue("DSSRC_DATE_EFFET", out var effet);
            return ValiditeSaisie.RefusOrdre(immat, effet, "la date d'immatriculation");
        }
        if (c.Colonne == "ETBLS_DATE_DOMICILIATION")
        {
            fiche.TryGetValue("ETBLS_DATE_DEBUT", out var debut);
            fiche.TryGetValue("ETBLS_DATE_DOMICILIATION", out var domi);
            return ValiditeSaisie.RefusOrdre(debut, domi, "la date de debut");
        }
        return null;
    }

    /// <summary>
    /// La condition « COLONNE=Valeur|Valeur2 » (ou « COLONNE=* » : renseigne)
    /// lue sur la fiche COURANTE. Pas de condition = toujours visible.
    /// </summary>
    private static bool ConditionRemplie(Dictionary<string, string> fiche, string visibleSi)
        => ConditionChamp.Remplie(fiche, visibleSi);

    private bool Visible(Etape e, Champ c) => ConditionRemplie(FicheVue(e), c.VisibleSi);

    // Un champ CACHE ne compte ni comme requis manquant ni comme refus :
    // sinon le controle final bloquerait sur un champ que personne ne voit.
    private bool RequisManquant(Etape e, Champ c)
        => c.Obligatoire && Visible(e, c) && string.IsNullOrWhiteSpace(Valeur(e, c));

    /// <summary>Ce qui bloque une etape, toutes ses fiches confondues.</summary>
    private (int Manquants, int Refuses) Defauts(Etape e)
    {
        var fiches = Fiches(e.Code);
        var manquants = 0;
        var refuses = 0;
        var memoire = FicheForcee;
        for (var i = 0; i < fiches.Count; i++)
        {
            FicheForcee = i;
            foreach (var c in e.Champs)
            {
                if (RequisManquant(e, c)) manquants++;
                else if (RefusDe(e, c) is not null) refuses++;
            }
        }
        FicheForcee = memoire;
        return (manquants, refuses);
    }

    /// <summary>Un defaut precis, tel qu'il sera montre au controle final.</summary>
    private record Defaut(int IndexEtape, string Etape, int Fiche, string Unite,
                         string Champ, string Colonne, string Motif, bool EstManquant);

    /// <summary>
    /// Tous les defauts des 7 etapes, a plat. C'est ce que le controle final
    /// presente : sans cette liste, il faudrait rouvrir chaque etape une par
    /// une pour savoir ce qui bloque.
    /// </summary>
    private List<Defaut> TousLesDefauts()
    {
        var liste = new List<Defaut>();
        var memoireFiche = FicheForcee;

        for (var e = 0; e < Etapes.Length; e++)
        {
            var etape = Etapes[e];
            var fiches = Fiches(etape.Code);
            for (var f = 0; f < fiches.Count; f++)
            {
                FicheForcee = f;
                foreach (var c in etape.Champs)
                {
                    if (RequisManquant(etape, c))
                    {
                        liste.Add(new Defaut(e, etape.Titre, f, etape.Unite,
                            c.Libelle, c.Colonne, "a renseigner", true));
                    }
                    else if (RefusDe(etape, c) is { } motif)
                    {
                        liste.Add(new Defaut(e, etape.Titre, f, etape.Unite,
                            c.Libelle, c.Colonne, motif, false));
                    }
                }
            }
        }

        FicheForcee = memoireFiche;
        return liste;
    }

    private void AllerAuDefaut(Defaut d)
    {
        FicheParEtape[Etapes[d.IndexEtape].Code] = d.Fiche;
        AllerEtape(d.IndexEtape);
    }

    /// <summary>
    /// Lien du bandeau : la colonne des etapes peut etre fermee, ou occupee
    /// par une fiche d'informations. On l'affiche d'abord ; le defilement
    /// jusqu'au defaut part avec la vue, et la page le joue APRES avoir rendu la
    /// zone des pages (le Task.Delay(1) du .razor attendait ce rendu).
    /// </summary>
    private void AllerAuDefautDepuisBandeau(Defaut d)
    {
        if (NoeudSelectionne is { EstFiche: true }) NoeudSelectionne = null;
        DonneesVisible = true;
        FicheParEtape[Etapes[d.IndexEtape].Code] = d.Fiche;
        EtapeCourante = d.IndexEtape;
        AllerEtape(d.IndexEtape);
    }

    /// <summary>
    /// Un dossier valide au controle final l est PARTOUT : grille de
    /// resultats, tuile Ma file, visionneuse. Une seule source de verite,
    /// sinon les trois ecrans se contrediraient.
    /// </summary>
    private bool EstValide(string numeroDossier) => Etat.Valides.Contains(numeroDossier);

    private bool DossierValide => EstValide(CleAffaire);

    private void ValiderDossier()
    {
        // Ceinture ET bretelles : le bouton est deja desactive, mais un
        // dossier ne doit pas pouvoir etre valide par un autre chemin.
        if (TousLesDefauts().Count > 0) return;
        Etat.Valides.Add(CleAffaire);
        Etat.Enregistrer();
        Etat.Journaliser(CleAffaire, "Dossier VALIDE au controle final");
    }

    private void RouvrirDossier()
    {
        Etat.Valides.Remove(CleAffaire);
        Etat.Enregistrer();
        Etat.Journaliser(CleAffaire, "Saisie ROUVERTE apres validation");
    }

    /// <summary>
    /// Un champ du formulaire guide.
    ///
    /// Defaut   : la valeur de DEPART, ECRITE dans la fiche a sa creation (pas
    ///            seulement affichee) — c'est elle qui part au document et dans
    ///            accueil.json. Elle vient des listes de Metier/ (JeuDemo,
    ///            Greffes), jamais d'une valeur inventee ici.
    /// VisibleSi: condition d'affichage, « COLONNE=Valeur » (plusieurs valeurs
    ///            separees par « | », « * » = champ renseigne, quel qu'il soit).
    ///            La colonne testee est celle de la MEME fiche : un dirigeant
    ///            personne morale ne cache pas un champ de l'etablissement.
    /// </summary>
    private record Champ(string Libelle, string Colonne, string Type = "text",
                         string Exemple = "", bool Obligatoire = false,
                         string Largeur = "moitie", string[]? ChoixPossibles = null,
                         string Controle = "", string Defaut = "", string VisibleSi = "")
    {
        public string[] Choix => ChoixPossibles ?? [];
    }

    private record Etape(string Code, string Titre, string Source, Champ[] Champs,
                         bool Multiple = false, string Unite = "Fiche");

    /// <summary>
    /// La date du jeu de demonstration (Metier/JeuDemo.cs), au format que
    /// rend un champ « date ». Les valeurs de depart s'y accrochent : aucune
    /// date n'est ecrite en dur dans le formulaire.
    /// </summary>
    private static readonly string DateDemo = JeuDemo.Aujourdhui.ToString("yyyy-MM-dd");
    private static readonly string GreffeDefaut = $"{Greffes.Defaut.Code} - {Greffes.Defaut.Ville}";

    /// <summary>
    /// La societe de reference du jeu de demonstration (Metier/JeuDemo.cs) :
    /// AMIENS BILLARDS, siege. Les valeurs de depart des champs libres s'y
    /// accrochent, exactement comme DateDemo et GreffeDefaut, pour qu'aucune
    /// donnee ne soit ecrite en dur dans le formulaire.
    /// </summary>
    private static readonly EntrepriseRequi SocieteDemo = JeuDemo.EntreprisesRequi[0];
    private static readonly DemandeRcs DemandeDemo = JeuDemo.Demandes[0];

    /// <summary>
    /// Les adresses connues du jeu de demonstration. La colonne v2 s'appelle
    /// ETABLISSEMENT.ID_ADRBS : c'est un RENVOI vers une adresse deja
    /// enregistree, pas du texte saisi. Le champ est donc une liste.
    /// </summary>
    private static readonly string[] AdressesDemo =
        JeuDemo.EntreprisesRequi.Select(e => e.Adresse).Distinct().ToArray();

    private readonly Etape[] Etapes =
    [
        new("DEMANDE", "Demande", "table DEMANDE",
        [
            new("Numero de demande", "DMND_NUM_DEMANDE", Exemple: "2026B01067", Obligatoire: true,
                Defaut: DemandeDemo.Numero),
            new("Numero indicatif", "DMND_NUM_INDICATIF", Exemple: "2024/0141",
                Defaut: SocieteDemo.Gestion),
            new("Libelle a l'ecran", "DMND_LIBELLE_ECRAN", Exemple: "Immatriculation - AMIENS BILLARDS", Largeur: "pleine",
                Defaut: DemandeDemo.Nom),
            new("Etat de la demande", "DMND_ETAT_DEMANDE", Obligatoire: true,
                ChoixPossibles: JeuDemo.EtatsDemande, Defaut: "En saisie"),
            new("Nom du deposant", "DMND_NOM", Exemple: "DUPONT Jean",
                Defaut: SocieteDemo.Dirigeant)
        ]),

        new("IDENTITE", "Identite", "tables DOSSIER_RCS et INSCRIT_PM",
        [
            new("SIREN", "DSSRC_SIREN", Exemple: "109 678 235", Obligatoire: true, Controle: "siren",
                Defaut: SocieteDemo.Siren),
            // Kbis « Denomination ou raison sociale » : colonne v2
            // INSCRIT_PM.DENOMINATION_STATUT. L'apercu prenait jusqu'ici le
            // libelle de l'affaire, qui n'est pas la denomination statutaire.
            // INSCRIT_PM : une personne PHYSIQUE n'a pas de denomination statutaire,
            // elle a un patronyme. Le champ disparait comme le font deja, en
            // miroir, les champs INSCRIT_PP pour une personne morale.
            new("Denomination statutaire", "INSPM_DENOMINATION_STATUT", Largeur: "pleine",
                Obligatoire: true, Exemple: "AMIENS BILLARDS",
                Defaut: SocieteDemo.Designation,
                VisibleSi: "DSSRC_TYPE_RCS=Personne morale|Etablissement principal"),
            new("Numero de gestion", "DSSRC_NUM_GESTION", Exemple: "2024 B 00141", Obligatoire: true,
                Defaut: SocieteDemo.Gestion),
            new("Type de dossier", "DSSRC_TYPE_RCS", Obligatoire: true,
                ChoixPossibles: ["Personne morale", "Personne physique", "Etablissement principal"],
                Defaut: "Personne morale"),
            new("Date d'immatriculation", "DSSRC_DATE_IMMAT", Type: "date", Obligatoire: true,
                Controle: "date-passee", Defaut: DateDemo),
            // DOSSIER_RCS.DATE_PREMIERE_IMMATRICULATION (Metier/InformationsDossier.cs) :
            // le Kbis la distingue de la date d'immatriculation au greffe courant.
            new("Date de 1re immatriculation", "DSSRC_DATE_PREMIERE_IMMATRICULATION", Type: "date",
                Controle: "date-passee", Defaut: DateDemo),
            new("Motif d'immatriculation", "DSSRC_MOTIF_IMMAT",
                ChoixPossibles: ["Creation", "Transfert de siege", "Reprise", "Reactivation"],
                Defaut: "Creation"),
            // Un transfert vient d'un autre greffe : son numero de gestion
            // anterieur (DOSSIER_RCS.NUM_ANTERIEUR) n'a de sens que la.
            new("N° de gestion anterieur", "DSSRC_NUM_ANTERIEUR", Exemple: "2019 B 00712",
                VisibleSi: "DSSRC_MOTIF_IMMAT=Transfert de siege"),
            new("Date d'effet", "DSSRC_DATE_EFFET", Type: "date", Defaut: DateDemo),
            new("Provisoirement sans activite", "DSSRC_PROVISOIREMENT_SANS",
                ChoixPossibles: ["Non", "Oui"], Defaut: "Non"),
            // DOSSIER_RCS.AUTO_ENTREPRENEUR : n'existe que pour une personne physique.
            new("Micro-Entrepreneur", "DSSRC_AUTO_ENTREPRENEUR", ChoixPossibles: ["Non", "Oui"],
                Defaut: "Non", VisibleSi: "DSSRC_TYPE_RCS=Personne physique"),
            // Identite de la personne physique (table INSCRIT_PP) : masquee
            // pour une personne morale, ou ces champs n'ont aucun sens.
            new("Civilite", "INSPP_CIVILITE", ChoixPossibles: ["M.", "Mme"], Defaut: "M.",
                VisibleSi: "DSSRC_TYPE_RCS=Personne physique"),
            new("Patronyme", "INSPP_PATRONYME", Exemple: "DUPONT",
                VisibleSi: "DSSRC_TYPE_RCS=Personne physique"),
            new("Date de naissance", "INSPP_DATE_NAISSANCE", Type: "date", Controle: "date-passee",
                VisibleSi: "DSSRC_TYPE_RCS=Personne physique")
        ]),

        new("CAPITAL", "Capital", "table CAPITAL",
        [
            new("Montant du capital", "CPTL_MONTANT", Type: "number", Exemple: "10000", Obligatoire: true, Controle: "montant",
                Defaut: "10000"),
            new("Monnaie", "CPTL_MONNAIE", ChoixPossibles: ["EUR", "USD", "CHF"], Obligatoire: true, Defaut: "EUR"),
            new("Capital variable", "CPTL_VARIABLE", ChoixPossibles: ["Non", "Oui"], Defaut: "Non"),
            // Le minimum statutaire ne se declare QUE pour un capital variable.
            new("Minimum statutaire", "CPTL_MINIMUM_STATUTAIRE", Type: "number", Controle: "montant",
                VisibleSi: "CPTL_VARIABLE=Oui"),
            new("Taux de liberation", "CPTL_TAUX_LIBERATION", Type: "number", Exemple: "100",
                Controle: "taux", Defaut: "100"),
            new("Apports en nature", "CPTL_APPORTS_NATURE", ChoixPossibles: ["Non", "Oui"], Defaut: "Non"),
            new("Texte du capital", "CPTL_TEXTE_CAPITAL", Largeur: "pleine",
                Exemple: "Capital de 10 000 EUR divise en 1 000 actions de 10 EUR",
                Defaut: "Capital de 10 000 EUR divise en 1 000 actions de 10 EUR")
        ]),

        new("SIEGE", "Siege et etablissements", "tables SIEGER et ETABLISSEMENT",
        [
            new("Type d'adresse", "ETBLS_TYPE_ADRESSE", Obligatoire: true,
                ChoixPossibles: ["Siege social", "Etablissement principal", "Etablissement secondaire"],
                Defaut: "Siege social"),
            // Kbis « Adresse du siege » : colonne v2 ETABLISSEMENT.ID_ADRBS.
            // L'apercu imprimait le TYPE d'adresse faute de ce champ.
            new("Adresse", "ETBLS_ID_ADRBS", Largeur: "pleine", Obligatoire: true,
                Exemple: "12 rue des Lilas 80000 AMIENS",
                ChoixPossibles: AdressesDemo, Defaut: SocieteDemo.Adresse),
            // Entete du Kbis : colonne v2 ETABLISSEMENT.CODE_GREFFE, jusqu'ici
            // ecrite en dur (« Amiens ») dans l'apercu.
            new("Code greffe", "ETBLS_CODE_GREFFE", Obligatoire: true,
                ChoixPossibles: Greffes.Choix, Defaut: GreffeDefaut),
            new("Date de debut", "ETBLS_DATE_DEBUT", Type: "date", Obligatoire: true,
                Controle: "date-passee", Defaut: DateDemo),
            new("Statut du local", "ETBLS_STATUT_LOCAL",
                ChoixPossibles: ["Proprietaire", "Locataire", "Domiciliataire", "Mise a disposition"],
                Defaut: "Locataire"),
            new("Mode d'exploitation", "ETBLS_MODE_EXPLOITATION",
                ChoixPossibles: ["Exploitation directe", "Location-gerance", "Gerance mandat"],
                Defaut: "Exploitation directe"),
            new("Origine du fonds", "ETBLS_ORIGINE_FONDS",
                ChoixPossibles: ["Creation", "Achat", "Apport", "Succession"], Defaut: "Creation"),
            // Une date de domiciliation ne concerne qu'un local domicilie.
            new("Date de domiciliation", "ETBLS_DATE_DOMICILIATION", Type: "date", Defaut: DateDemo,
                VisibleSi: "ETBLS_STATUT_LOCAL=Domiciliataire"),
            // AJOUT - section LOI_1956 « Loi de 1956 (location-gerance) » de
            // Metier/InformationsDossier.cs : un fonds donne en location-gerance
            // porte un acte date, une expiration et une regle de reconduction.
            // Rien de tout cela n'existait dans la maquette.
            new("Date de l'acte de location-gerance", "LOI56_DATE_ACTE", Type: "date",
                Controle: "date-passee", Defaut: DateDemo,
                VisibleSi: "ETBLS_MODE_EXPLOITATION=Location-gerance"),
            new("Expiration de la location-gerance", "LOI56_DATE_EXPIRATION", Type: "date",
                VisibleSi: "ETBLS_MODE_EXPLOITATION=Location-gerance"),
            new("Avec tacite reconduction", "LOI56_TACITE_RECONDUC", ChoixPossibles: ["Oui", "Non"],
                Defaut: "Oui", VisibleSi: "ETBLS_MODE_EXPLOITATION=Location-gerance")
        ], Multiple: true, Unite: "Etablissement"),

        new("ACTIVITE", "Activite", "table ACTIVITE",
        [
            new("Activite declaree", "ACTVT_ACTIVITE_DECLAREE", Largeur: "pleine", Obligatoire: true,
                Exemple: "Prestation de services informatiques, conseil en systemes",
                Defaut: SocieteDemo.Activite),
            new("Code NAF", "ACTVT_CODE_NAF", Exemple: "6202A", Controle: "naf"),
            new("Code APE", "ACTVT_CODE_APE", Exemple: "6202A", Controle: "naf"),
            new("Nature des activites", "ACTVT_NATURE_ACTIVITES",
                ChoixPossibles: ["Commerciale", "Artisanale", "Liberale", "Agricole"],
                Defaut: "Commerciale"),
            new("Date de debut", "ACTVT_DATE_DEBUT", Type: "date", Obligatoire: true,
                Controle: "date-passee", Defaut: DateDemo),
            new("Activite reglementee", "ACTVT_ACTIVITE_REGLEMENTEE", ChoixPossibles: ["Non", "Oui"], Defaut: "Non"),
            new("Activite ambulante", "ACTVT_ACTIVITE_AMBULANTE", ChoixPossibles: ["Non", "Oui"], Defaut: "Non"),
            new("Activite permanente", "ACTVT_PERMANENTE", ChoixPossibles: ["Oui", "Non"], Defaut: "Oui"),
            // AJOUT - ACTIVITE.SAISONNIERE (Metier/InformationsDossier.cs, section
            // ACTIVITE) : v2 le demande, la maquette ne l'avait pas. Une activite
            // non permanente est saisonniere ou occasionnelle : la question ne se
            // pose que dans ce cas.
            new("Activite saisonniere", "ACTVT_SAISONNIERE", ChoixPossibles: ["Oui", "Non"],
                Defaut: "Oui", VisibleSi: "ACTVT_PERMANENTE=Non")
        ]),

        new("DIRIGEANTS", "Dirigeants", "table DIRIGEANT",
        [
            new("Fonction", "DRGNT_FONCTION_DIRIGEANT", Obligatoire: true,
                ChoixPossibles: ["President", "Directeur general", "Gerant", "Commissaire aux comptes", "Administrateur"],
                Defaut: "Gerant"),
            new("Personne physique", "DRGNT_PERSONNE_PHYSIQUE", ChoixPossibles: ["Oui", "Non"],
                Obligatoire: true, Defaut: "Oui"),
            new("Numero d'ordre", "DRGNT_NUM_ORDRE", Type: "number", Exemple: "1", Defaut: "1"),
            new("Date d'entree", "DRGNT_DATE_ENTREE", Type: "date", Obligatoire: true,
                Controle: "date-passee", Defaut: DateDemo),
            // Un dirigeant personne PHYSIQUE n'a pas de SIREN : le champ dispraait
            // de l'ecran au lieu de rester vide et incomprehensible.
            new("SIREN du dirigeant", "DRGNT_NUM_SIREN", Exemple: "si personne morale", Controle: "siren",
                VisibleSi: "DRGNT_PERSONNE_PHYSIQUE=Non"),
            // AJOUT - section DIRIGEANT_PM de Metier/InformationsDossier.cs
            // (DENOMINATION_STATUT) : un dirigeant personne morale se nomme, la
            // maquette ne demandait que son SIREN.
            new("Denomination du dirigeant", "DRGPM_DENOMINATION_STATUT", Largeur: "pleine",
                Exemple: "HOLDING DUPONT SAS",
                VisibleSi: "DRGNT_PERSONNE_PHYSIQUE=Non"),
            new("Organe collegial", "DRGNT_ORGANE_COLLEGIAL", ChoixPossibles: ["Non", "Oui"], Defaut: "Non"),
            new("Associe indefiniment responsable", "DRGNT_ASSOCIE_ISR", ChoixPossibles: ["Non", "Oui"], Defaut: "Non")
        ], Multiple: true, Unite: "Dirigeant"),

        new("CONTROLE", "Controle", "table OBSERVATION_RCS",
        [
            new("Texte de la mention", "OBSRC_TEXTE_MENTION", Largeur: "pleine",
                Exemple: "Observation portee au registre"),
            // MENTION_OBSERVATION.DATE_MENTION et .DIFFUSABLE (Metier/InformationsDossier.cs) :
            // une mention sans date ni regle de diffusion n'est pas publiable.
            // Ils n'apparaissent que si une mention est effectivement redigee.
            new("Date de la mention", "MNTOB_DATE_MENTION", Type: "date", Controle: "date-passee",
                Defaut: DateDemo, VisibleSi: "OBSRC_TEXTE_MENTION=*"),
            new("Diffusable Kbis", "MNTOB_DIFFUSABLE", ChoixPossibles: ["Oui", "Non"], Defaut: "Oui",
                VisibleSi: "OBSRC_TEXTE_MENTION=*"),
            new("Etat du dossier", "DSSRC_ETAT_DOSSIER", Obligatoire: true,
                ChoixPossibles: ["A controler", "Conforme", "En anomalie"], Defaut: "A controler")
        ])
    ];

    // ================= Les gestes de l'accueil =================

    /// <summary>Ce que seul le plan de travail sait, passe a la vue de l'accueil.</summary>
    private Pont.ContexteAccueil ContexteDeLAccueil()
    {
        return new(AccueilRecouvert, ProcOuverte?.Code);
    }

    /// <summary>Un geste de la barre ou de l'accueil : la session l'applique ; ce
    /// qu'il demande hors de l'accueil (ouvrir, fenetre) est fait ici.</summary>
    public void SurGesteAccueil(Pont.GesteIle g)
    {
        if (Accueil.Agir(g.Nom, g.Valeur) is not { } demande) return;
        switch (demande.Quoi)
        {
            case Pont.DemandeAccueil.OuvrirCode: OuvrirCode(demande.Valeur); break;
            case Pont.DemandeAccueil.OuvrirAlerte: OuvrirAlerte(demande.Alerte!); break;
            case Pont.DemandeAccueil.OuvrirNumero: OuvrirParNumero(demande.Valeur); break;
            case Pont.DemandeAccueil.OuvrirPalette: OuvrirPalette(demande.Valeur, ancree: true); break;
            case Pont.DemandeAccueil.LoginChange: ApresChangementLogin(); break;
            case Pont.DemandeAccueil.ThemeChange: Fenetre?.AppliquerFond(Theme); break;
            case Pont.DemandeAccueil.Deplacer: Fenetre?.Deplacer(); break;
            case Pont.DemandeAccueil.Reduire: Fenetre?.Reduire(); break;
            case Pont.DemandeAccueil.Agrandir: Fenetre?.BasculerAgrandissement(); break;
            case Pont.DemandeAccueil.Fermer: Fenetre?.Fermer(); break;
            default: throw new InvalidOperationException($"Demande inconnue de l'accueil : {demande.Quoi}.");
        }
    }

    /// <summary>Un minuteur de l'accueil a releve (performance, actualites) : la vue suit.</summary>
    private void SurChangeAccueil() => Change?.Invoke();

    /// <summary>
    /// Le clic sur une alerte EMMENE : soit sur l'affaire nommee par l'alerte,
    /// soit sur l'ecran du traitement concerne. Sans cela la tuile disait un
    /// probleme sans donner le moyen de le traiter.
    /// </summary>
    private void OuvrirAlerte(AlerteGreffe a)
    {
        // Une alerte de LOT ouvre le TABLEAU des dossiers de cette recette,
        // deja selectionnee : l'alerte dit « ces dossiers-la », l'ecran doit
        // s'ouvrir dessus, pas sur un choix a refaire.
        if (a.Lot is { Length: > 0 } codeLot)
        {
            LotDemande = codeLot;
            OuvrirCode("PROC_TACHES_LOT");
            return;
        }

        // Une alerte task driven ouvre la TACHE, pas seulement l'affaire.
        if (a.Tache is { } t && RecettesTaches.ParCode(t.Code) is { } recette)
        {
            OuvrirTache(recette, t.Dossier);
            return;
        }
        if (a.Affaire is { Length: > 0 } numero) { OuvrirParNumero(numero); return; }
        OuvrirCode(a.Cible);
    }

    /// <summary>SIREN pose dans l'ecran d'ouverture quand on y arrive depuis une
    /// recherche restee vide.</summary>
    private string? SirenAImmatriculer;

    /// <summary>Change avec le SIREN : l'ecran est RECONSTRUIT, sinon Blazor
    /// reutiliserait l'instance et le numero ne serait pas repose.</summary>
    private object CleOuverture => SirenAImmatriculer ?? "ouverture-vierge";

    /// <summary>La recherche n'a rien trouve : on bascule sur l'ouverture d'un
    /// dossier, en emportant le SIREN deja saisi.</summary>
    private void OuvrirUnDossierPour(string siren)
    {
        SirenAImmatriculer = string.IsNullOrWhiteSpace(siren) ? null : siren.Trim();
        OuvrirProc(new Proc("OUVERTURE_DOSSIER", "Ouverture d'un dossier RCS"));
    }
}
