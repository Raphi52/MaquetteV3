namespace RigV3Desktop.Pont;

// ============================================================
// LA VUE DU DOSSIER, proposition 3 du /draft du 19/11/2026 (conv-2009,
// « Onglets Suivi / Fil ») : ce que la mise en page ajoute au plan de travail.
//   - l'ARBRE se replie : sa colonne disparait, le chemin de l'element choisi
//     le remplace au-dessus des donnees, un clic le rouvre ;
//   - un LISERE de couleur par rubrique de l'arbre (Pieces jointes, Depots
//     d'actes...) : tout ce qui va ensemble porte le meme filet, et le point de
//     ses pieces dans l'historique prend la meme couleur ;
//   - la colonne de SUIVI a droite, a deux onglets : Suivi (en cours, reste a
//     faire et historique) et Fil (les notes ecrites sur le dossier et les valeurs
//     changees dans le formulaire).
// ============================================================
public sealed partial class SessionPlanTravail
{
    /// <summary>Le nombre de teintes de lisere (.rig-vged --rig-groupe-1 a 8, rig-accueil.css).</summary>
    public const int TeintesDeGroupe = 8;

    /// <summary>L'arbre est replie (geste « arbreReplier ») : il le reste d'une affaire a l'autre,
    /// comme la largeur de sa colonne.</summary>
    private bool ArbreReplie;

    /// <summary>La colonne de suivi, derniere colonne du corps, tant qu'on ne l'a pas glissee :
    /// un cinquieme de la largeur, jamais moins de 240 px (ses onglets et ses dates tiennent).</summary>
    private const string LargeurSuiviSouple = "minmax(240px, 20%)";

    /// <summary>La colonne de suivi est repliee a droite (geste « suiviReplier ») : il n'en reste
    /// qu'un rail, son bouton pour la rouvrir et le nombre d'etapes a corriger. Elle le reste
    /// d'une affaire a l'autre, comme l'arbre (demande du 19/11/2026).</summary>
    private bool SuiviReplie;

    /// <summary>La largeur du rail de la colonne de suivi repliee.</summary>
    private const string LargeurSuiviReplie = "38px";

    /// <summary>La largeur glissee a la troisieme poignee (entre le document et le suivi), en
    /// pixels ; null tant que personne ne l'a glissee : la colonne garde sa largeur souple.</summary>
    private double? LargeurSuiviReglee;
    private const double LargeurSuiviMin = 240;
    private const double LargeurSuiviMax = 900;

    /// <summary>
    /// Troisieme poignee : la largeur de la colonne de suivi. Le C# ne connait pas la largeur
    /// rendue de la colonne souple (un cinquieme de la fenetre) : l'ecran la mesure et l'envoie
    /// avec la saisie. Glisser a GAUCHE elargit la colonne (elle est collee au bord droit).
    /// </summary>
    private void DebutRedimSuivi(double clientX, double largeurRendue)
    {
        XDepart = clientX;
        LargeurDepart = largeurRendue > 0 ? largeurRendue : LargeurSuiviReglee ?? LargeurSuiviMin;
        ColonneRedim = 3;
        Redimensionne = true;
    }

    /// <summary>La longueur maximale d'un message du fil.</summary>
    private const int LongueurMessageMax = 2000;

    /// <summary>
    /// Les TETES de rubrique et leur teinte : chaque dossier pose sous un domaine
    /// (« Dossier RCS », « Judiciaire »), dans l'ordre de l'arbre. Les droits du
    /// login et les ajouts comptent, pas le repli du mode tache : une rubrique garde
    /// sa couleur quand une tache replie l'arbre.
    /// </summary>
    private Dictionary<NoeudGed, int> GroupesArbre()
    {
        var groupes = new Dictionary<NoeudGed, int>(ReferenceEqualityComparer.Instance);
        var rang = 0;
        foreach (var domaine in AccesGed.EnfantsVisibles(Arbre, Domaines, AjoutsArbre, MasquesArbre))
            foreach (var rubrique in AccesGed.EnfantsVisibles(domaine, Domaines, AjoutsArbre, MasquesArbre))
                if (!rubrique.EstPiece && !rubrique.EstFiche)
                    groupes[rubrique] = rang++ % TeintesDeGroupe + 1;
        return groupes;
    }

    // ------------------------------------------------------------
    // LE CLASSEUR DE L'ARBRE (/draft du 19/11/2026, proposition 5 « Chronologie »,
    // web/src/composants/ClasseurArbre.tsx) : une bascule par domaine (Dossier RCS,
    // Judiciaire), une carte par rubrique, UNE SEULE ouverte par domaine.
    // ------------------------------------------------------------

    /// <summary>
    /// Les branches que le classeur RENDS : celles que l'utilisateur a ouvertes, plus
    /// la racine et ses domaines (la bascule montre l'un ou l'autre, jamais replie), et,
    /// dans chaque rubrique ouverte, TOUS ses sous-dossiers -- la carte les montre en
    /// intertitres et sa vue « Par date » a besoin de toutes ses pieces. NoeudsOuverts
    /// n'est pas touche : la regle du pont (« une branche fermee ne rend rien dessous »)
    /// reste vraie, c'est le choix des branches qui change.
    /// </summary>
    private HashSet<NoeudGed> OuvertsDuClasseur()
    {
        var vue = AccesGed.NouveauxMasques();
        vue.UnionWith(NoeudsOuverts);
        vue.Add(Arbre);
        void ToutOuvrir(NoeudGed dossier)
        {
            vue.Add(dossier);
            foreach (var e in AccesGed.EnfantsVisibles(dossier, Domaines, AjoutsArbre, MasquesArbre, RecetteArbre))
                if (!e.EstPiece && !e.EstFiche) ToutOuvrir(e);
        }
        foreach (var domaine in AccesGed.EnfantsVisibles(Arbre, Domaines, AjoutsArbre, MasquesArbre, RecetteArbre))
        {
            vue.Add(domaine);
            foreach (var rubrique in AccesGed.EnfantsVisibles(domaine, Domaines, AjoutsArbre, MasquesArbre, RecetteArbre))
                if (NoeudsOuverts.Contains(rubrique)) ToutOuvrir(rubrique);
        }
        return vue;
    }

    /// <summary>
    /// La carte d'une rubrique, cliquee (geste « arbreRubrique ») : fermee, elle s'ouvre
    /// et referme les autres rubriques de son domaine ; ouverte, elle se ferme. Rien
    /// n'est choisi ni ferme a cote (document, donnees) : ouvrir une carte, c'est
    /// parcourir. Une rubrique qui COMMENCE par sa fiche (Formalites) l'ouvre au
    /// milieu, comme son depliage dans l'arbre (ArbreDemo.FicheADeplier).
    /// </summary>
    private void BasculerRubrique(NoeudGed rubrique)
    {
        if (NoeudsOuverts.Remove(rubrique)) return;
        NoeudsOuverts.Add(rubrique);
        FermerLesAutresRubriques(rubrique);
        if (ArbreDemo.FicheADeplier(rubrique) is { } fiche) OuvrirApercu(fiche);
    }

    /// <summary>
    /// A l'ouverture d'une affaire, toutes les branches servies sont depliees (l'arbre
    /// s'ouvrait sur du contenu) : le classeur, lui, garde au plus UNE carte ouverte par
    /// domaine -- celle qui range l'element choisi ; les autres se ferment.
    /// </summary>
    private void UneRubriqueParDomaine()
    {
        foreach (var domaine in AccesGed.EnfantsVisibles(Arbre, Domaines, AjoutsArbre, MasquesArbre, RecetteArbre))
            foreach (var rubrique in AccesGed.EnfantsVisibles(domaine, Domaines, AjoutsArbre, MasquesArbre, RecetteArbre))
                if (!rubrique.EstPiece && !rubrique.EstFiche
                    && !(NoeudSelectionne is not null
                         && (ReferenceEquals(rubrique, NoeudSelectionne)
                             || rubrique.Descendants().Any(d => ReferenceEquals(d, NoeudSelectionne)))))
                    NoeudsOuverts.Remove(rubrique);
    }

    /// <summary>
    /// Le geste « arbreTri » : « Par dossier » ou « Par date » dans la carte ouverte. Le choix
    /// vaut pour toutes les affaires et survit au redemarrage (AccueilEtat.TriClasseur) ; il
    /// est pose aussi dans l'etat de cette session, sinon son prochain Enregistrer le defait.
    /// Une autre valeur est ignoree.
    /// </summary>
    private void ClasserLeClasseur(string tri)
    {
        if (tri is not (AccueilEtat.TriClasseurParDate or AccueilEtat.TriClasseurParDossier)) return;
        if (Etat.TriClasseur == tri) return;
        Etat.TriClasseur = tri;
        AccueilEtat.MemoriserTriClasseur(tri);
    }

    /// <summary>
    /// Les SOUS-DOSSIERS replies dans le classeur (« Par dossier », clic sur l'intertitre),
    /// pour l'affaire ouverte. Retenus avec le reste de son arbre (MemoriserArbre, par
    /// chemin dans accueil.json), reposes a sa reouverture (PersistanceGed.Appliquer) :
    /// d'une affaire a l'autre et apres redemarrage (demande du 19/11/2026).
    /// </summary>
    private readonly HashSet<NoeudGed> SousDossiersReplies = AccesGed.NouveauxMasques();

    /// <summary>Un sous-dossier : un dossier range DANS une rubrique (ni la racine, ni un
    /// domaine, ni la tete d'une rubrique, qui est sa carte).</summary>
    private bool EstSousDossier(NoeudGed n)
        => !n.EstPiece && !n.EstFiche && !ReferenceEquals(n, Arbre)
           && !AccesGed.EnfantsVisibles(Arbre, Domaines, AjoutsArbre, MasquesArbre).Any(d => ReferenceEquals(d, n))
           && !GroupesArbre().ContainsKey(n);

    /// <summary>Le geste « arbreReplierDossier » : replie le sous-dossier, ou le rouvre, et le retient.</summary>
    private void BasculerSousDossier(NoeudGed n)
    {
        if (!EstSousDossier(n)) return;
        if (!SousDossiersReplies.Remove(n)) SousDossiersReplies.Add(n);
        MemoriserArbre();
    }

    /// <summary>
    /// Un element vient d'etre choisi (setter de NoeudSelectionne) : les sous-dossiers
    /// replies qui le rangent se rouvrent, et le restent. Replier celui qui range l'element
    /// DEJA ouvert reste possible : la selection ne change pas, rien ne se rouvre.
    /// </summary>
    private void RouvrirLesSousDossiersDe(NoeudGed? element)
    {
        if (element is null || SousDossiersReplies.Count == 0) return;
        if (SousDossiersReplies.RemoveWhere(d => Range(d, element)) > 0) MemoriserArbre();
    }

    /// <summary>
    /// Les CARTES ouvertes de l'affaire telles que son arbre enregistre les connait
    /// (ArbreGedEnregistre.Cartes) : comparees au classeur apres chaque geste (RetenirLesCartes).
    /// </summary>
    private readonly HashSet<NoeudGed> CartesRetenues = AccesGed.NouveauxMasques();

    /// <summary>Les cartes ouvertes du classeur : les tetes de rubrique depliees.</summary>
    private HashSet<NoeudGed> CartesOuvertes()
    {
        var cartes = AccesGed.NouveauxMasques();
        foreach (var rubrique in GroupesArbre().Keys)
            if (NoeudsOuverts.Contains(rubrique)) cartes.Add(rubrique);
        return cartes;
    }

    /// <summary>
    /// Apres CHAQUE geste (Agir, SurGesteAccueil) : si les cartes ouvertes ont change, quoi
    /// qui les ait ouvertes (clic sur la carte, historique, palette, mode tache), l'arbre de
    /// l'affaire est reecrit -- elles se rouvriront avec elle, meme apres un redemarrage
    /// (demande du 19/11/2026). Un seul point plutot qu'un appel par geste : aucun chemin
    /// qui ouvre une carte ne peut l'oublier.
    /// </summary>
    private void RetenirLesCartes()
    {
        if (DossierOuvert is null || CartesRetenues.SetEquals(CartesOuvertes())) return;
        MemoriserArbre();
    }

    /// <summary>
    /// A l'ouverture de l'affaire : ses cartes retenues se rouvrent, sauf dans le classeur
    /// ou l'element choisi a deja ouvert la sienne (une carte par domaine, et l'element
    /// ouvert reste visible).
    /// </summary>
    private void RouvrirLesCartes(IReadOnlyCollection<NoeudGed> cartes)
    {
        foreach (var domaine in AccesGed.EnfantsVisibles(Arbre, Domaines, AjoutsArbre, MasquesArbre, RecetteArbre))
        {
            var rubriques = AccesGed.EnfantsVisibles(domaine, Domaines, AjoutsArbre, MasquesArbre, RecetteArbre)
                                    .Where(r => !r.EstPiece && !r.EstFiche).ToList();
            if (rubriques.Any(NoeudsOuverts.Contains)) continue;
            if (rubriques.FirstOrDefault(cartes.Contains) is { } carte) NoeudsOuverts.Add(carte);
        }
        CartesRetenues.Clear();
        CartesRetenues.UnionWith(cartes);
    }

    /// <summary>Le dossier range cet element, a toute profondeur (elements crees compris).</summary>
    private bool Range(NoeudGed dossier, NoeudGed element)
        => dossier.Enfants.Concat(AjoutsArbre.TryGetValue(dossier, out var a) ? a : [])
                  .Any(e => ReferenceEquals(e, element) || Range(e, element));

    /// <summary>Referme les rubriques soeurs de celle-ci, dans son domaine.</summary>
    private void FermerLesAutresRubriques(NoeudGed rubrique)
    {
        foreach (var domaine in AccesGed.EnfantsVisibles(Arbre, Domaines, AjoutsArbre, MasquesArbre, RecetteArbre))
        {
            var soeurs = AccesGed.EnfantsVisibles(domaine, Domaines, AjoutsArbre, MasquesArbre, RecetteArbre);
            if (!soeurs.Any(s => ReferenceEquals(s, rubrique))) continue;
            foreach (var s in soeurs)
                if (!ReferenceEquals(s, rubrique) && !s.EstPiece && !s.EstFiche) NoeudsOuverts.Remove(s);
        }
    }

    /// <summary>
    /// Le chemin de l'element choisi dans l'arbre, montre quand l'arbre est replie :
    /// le numero de l'affaire, puis chaque dossier, puis l'element. Sans choix : le numero seul.
    /// </summary>
    private string[] CheminSelection(string numero)
    {
        var chemin = new List<string>();
        bool Chercher(NoeudGed n)
        {
            if (ReferenceEquals(n, NoeudSelectionne)) { chemin.Add(LibelleAffiche(n)); return true; }
            chemin.Add(LibelleAffiche(n));
            foreach (var e in AccesGed.EnfantsVisibles(n, Domaines, AjoutsArbre, MasquesArbre, RecetteArbre))
                if (Chercher(e)) return true;
            chemin.RemoveAt(chemin.Count - 1);
            return false;
        }
        if (NoeudSelectionne is null || !Chercher(Arbre)) return [numero];
        // La racine porte « numero objet (Siren : ...) » : le numero suffit en tete du chemin.
        chemin[0] = numero;
        return [.. chemin];
    }

    /// <summary>Les pieces que l'arbre montre a ce login (droits, ajouts, masques, mode tache).</summary>
    private int NbPiecesVisibles()
    {
        int Compter(NoeudGed n) => n.EstPiece
            ? 1
            : AccesGed.EnfantsVisibles(n, Domaines, AjoutsArbre, MasquesArbre, RecetteArbre).Sum(Compter);
        return Compter(Arbre);
    }

    /// <summary>
    /// Le FIL de l'affaire, du plus ancien au plus recent : les notes ecrites dans
    /// l'onglet « Fil » et les valeurs changees dans le formulaire (la trace des
    /// modifications que montre aussi le bouton « Historique » de la fiche).
    /// </summary>
    private MessageFilVue[] VueFil(string numero)
        => FilDeLAffaire(Etat.Fil.TryGetValue(numero, out var n) ? n : [],
                         Etat.Modifications.TryGetValue(numero, out var m) ? m : [],
                         DateTime.Now);

    /// <summary>Les notes et les modifications d'une affaire, fondues en un seul fil date
    /// (public : tests/PlanTravailCheck le joue sur des dates choisies).</summary>
    public static MessageFilVue[] FilDeLAffaire(IEnumerable<MessageFil> notes,
                                                IEnumerable<ModificationChamp> modifs, DateTime maintenant)
    {
        return notes.Select(x => (x.Quand, Vue: new MessageFilVue(Initiales(x.Auteur), NomAffiche(x.Auteur),
                                                                   Moment(x.Quand, maintenant), x.Texte,
                                                                   false, "", "", "")))
            .Concat(modifs.Select(x => (x.Quand, Vue: new MessageFilVue(Initiales(x.Auteur), NomAffiche(x.Auteur),
                                                                         Moment(x.Quand, maintenant), "", true, x.Champ,
                                                                         x.Avant.Length == 0 ? "(vide)" : x.Avant,
                                                                         x.Apres.Length == 0 ? "(vide)" : x.Apres))))
            // OrderBy est STABLE : a la meme minute, les notes d'abord, dans leur ordre.
            .OrderBy(x => x.Quand)
            .Select(x => x.Vue)
            .ToArray();
    }

    // ------------------------------------------------------------
    // EN COURS SUR L'AFFAIRE (plan « Vue du dossier = coeur des actions », lot 3,
    // 20/11/2026), en tete de l'onglet « Suivi » : les demandes, commandes web,
    // courriers et actes attendus qui la portent, et les derniers gestes poses
    // dessus. L'agent les voit et les traite sans quitter la page : chaque ligne
    // ouvre son ecran POUR ce dossier, comme la barre d'actions
    // (OuvrirProcDepuisAffaire : l'onglet retient l'affaire, l'ecran en recoit le
    // contexte), et « M'attribuer » est le geste de la palette (« paletteAttribuer »),
    // sur la meme regle (RecherchePalette.Attribuable).
    // ------------------------------------------------------------

    /// <summary>Combien de gestes recents la colonne montre sous « En cours ».</summary>
    private const int ActiviteMontree = 4;

    private sealed record GesteTravailDossier(string Nature, string Numero);

    /// <summary>Ce qui est en cours sur l'affaire, tel que ce login le voit.</summary>
    private EnCoursDossierVue VueEnCours(Dossier d)
    {
        var compte = Accueil.CompteCourant;
        var moi = compte.Signature;
        var travail = RecherchePalette.TravailDuDossier(d.Numero, EstValide);
        return new EnCoursDossierVue(
            [.. travail.Select(t => new TravailDossierVue(t.Nature, t.Numero, t.Titre, t.Detail, t.Etat, t.Ton, t.Filet,
                                                          t.Qui.Length > 0 ? Agent(t.Qui) : null, t.Qui == moi,
                                                          RecherchePalette.Attribuable(t, moi), t.Delai,
                                                          EcranDuTravail(t.Ecran)?.Libelle ?? ""))],
            travail.Count(t => t.Urgence == 0),
            [.. RecherchePalette.Activite(d.Numero, compte, ActiviteMontree)
                .Select(a => new ActivitePaletteVue(a.Agent, a.Verbe, a.Objet, a.Depuis))]);
    }

    /// <summary>L'ecran d'une ligne, s'il s'ouvre pour le dossier depuis cette fenetre et pour ce login.</summary>
    private Proc? EcranDuTravail(string code)
        => EcranPourLeDossier(code) ? Groupes.SelectMany(g => g.Procs).FirstOrDefault(p => p.Code == code) : null;

    /// <summary>Le geste « enCoursOuvrir » : l'ecran de cette ligne, ouvert pour l'affaire ouverte.</summary>
    private void OuvrirTravailDuDossier(GesteTravailDossier g)
    {
        // L'affaire fermee, ou la ligne terminee, entre le clic et son traitement : plus rien a ouvrir.
        if (DossierOuvert is not { } d
            || RecherchePalette.TravailDuDossier(d.Numero, EstValide)
                   .FirstOrDefault(t => t.Nature == g.Nature && t.Numero == g.Numero) is not { } ligne) return;
        var proc = EcranDuTravail(ligne.Ecran)
            ?? throw new InvalidOperationException($"La ligne {g.Numero} n'ouvre aucun ecran pour l'affaire {d.Numero}.");
        OuvrirProcDepuisAffaire(proc);
    }

    /// <summary>Le geste « filEcrire » : la note entre au fil de l'affaire ouverte, signee du login Windows
    /// (le meme auteur que la trace des modifications).</summary>
    private void EcrireAuFil(string texte)
    {
        if (DossierOuvert is not { } d) return;
        texte = texte.Trim();
        if (texte.Length == 0) return;
        if (texte.Length > LongueurMessageMax) texte = texte[..LongueurMessageMax];
        Etat.EcrireAuFil(d.Numero, new MessageFil(DateTime.Now, Environment.UserName, texte));
    }

    /// <summary>« raphael.vilain » : « RV ».</summary>
    internal static string Initiales(string auteur)
        => new string(auteur.Split('.', ' ', '_').Where(x => x.Length > 0)
                            .Take(2).Select(x => char.ToUpperInvariant(x[0])).ToArray());

    /// <summary>« raphael.vilain » : « R. VILAIN » ; un login d'un seul mot, en capitales.</summary>
    internal static string NomAffiche(string auteur)
    {
        var mots = auteur.Split('.', ' ', '_').Where(x => x.Length > 0).ToArray();
        return mots.Length < 2
            ? auteur.ToUpperInvariant()
            : char.ToUpperInvariant(mots[0][0]) + ". " + string.Join(" ", mots.Skip(1)).ToUpperInvariant();
    }

    /// <summary>« 10:42 » le jour meme, « hier 16:20 » la veille, sinon « 12/03/2024 10:42 ».</summary>
    internal static string Moment(DateTime quand, DateTime maintenant)
        => quand.Date == maintenant.Date ? quand.ToString("HH:mm")
         : quand.Date == maintenant.Date.AddDays(-1) ? "hier " + quand.ToString("HH:mm")
         : quand.ToString("dd/MM/yyyy HH:mm");
}
