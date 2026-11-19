namespace RigV3Desktop.Pont;

// ============================================================
// LA VUE DU DOSSIER, proposition 3 du /draft du 19/11/2026 (conv-2009,
// « Onglets Suivi / Fil ») : ce que la mise en page ajoute au plan de travail.
//   - l'ARBRE se replie : sa colonne disparait, le chemin de l'element choisi
//     le remplace au-dessus des donnees, un clic le rouvre ;
//   - un LISERE de couleur par rubrique de l'arbre (Pieces jointes, Depots
//     d'actes...) : tout ce qui va ensemble porte le meme filet, et le point de
//     ses pieces dans l'historique prend la meme couleur ;
//   - la colonne de SUIVI a droite, a deux onglets : Suivi (reste a faire et
//     historique) et Fil (les notes ecrites sur le dossier et les valeurs
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
