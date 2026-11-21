namespace RigV3Desktop.Pont;

/// <summary>
/// Une ligne de la fiche « Ancienne touche, nouvelle touche » (SOC-027).
/// </summary>
/// <param name="Numero">Le rang de la ligne dans la fiche (stable d'une ouverture a l'autre).</param>
/// <param name="EcranV2">L'ecran de l'ancien RIG ou la touche agissait (son nom, jamais son code).</param>
/// <param name="AncienneTouche">La touche de la V2 (« F7 », « Entrée »...), « — » pour un geste nouveau de la V3.</param>
/// <param name="Geste">Ce que la touche faisait dans la V2 (ou fait dans la V3 pour un geste nouveau).</param>
/// <param name="NouvelleTouche">La touche de la V3 qui la remplace ; null si elle est remplacee par un geste, supprimee ou a venir.</param>
/// <param name="Statut">Voir <see cref="StatutsCorrespondance"/>.</param>
/// <param name="Explication">Le geste qui la remplace, la raison de sa suppression, ou ce qui reste a faire.</param>
/// <param name="EcranV3">Le code de l'ecran de la V3 ou l'essayer (menu, ou <see cref="EcranCorrespondances.Accueil"/>,
/// <see cref="EcranCorrespondances.PageDossier"/>, <see cref="EcranCorrespondances.PageRecherche"/>) ; null si supprimee.</param>
/// <param name="NomEcranV3">Le nom de cet ecran tel que la V3 l'affiche.</param>
/// <param name="Ouvrable">L'ecran s'ouvre depuis la fiche (geste « ouvrirCode ») ; faux pour l'accueil et la page du dossier.</param>
/// <param name="Construit">L'ecran de la V3 existe deja (maquette) : la touche s'y essaie. Faux : il est au menu, « a venir ».</param>
/// <param name="Source">Ou la V2 le dit (fichier:ligne sous D:\RigApplication\Source) ; null pour un geste nouveau.</param>
public sealed record LigneCorrespondance(int Numero, string EcranV2, string AncienneTouche, string Geste, string? NouvelleTouche,
                                         string Statut, string Explication, string? EcranV3, string? NomEcranV3, bool Ouvrable,
                                         bool Construit, string? Source);

/// <summary>Un retour d'un greffe beta sur une ligne de la fiche (SOC-027 CA05, tache T03).</summary>
public sealed record RetourBeta(string Greffe, int Ligne, string Remarque, bool Integre);

/// <summary>Ce que l'ecran affiche : la fiche, et ou elle en est avant la formation des autres greffes (CA05).</summary>
/// <param name="EtatFiche">Voir <see cref="EcranCorrespondances.EtatFiche"/>.</param>
public sealed record VueEcranCorrespondances(LigneCorrespondance[] Lignes, string EtatFiche, RetourBeta[] RetoursBeta);

/// <summary>Les statuts d'une ligne de la fiche (SOC-027 CA02).</summary>
public static class StatutsCorrespondance
{
    /// <summary>La meme touche fait la meme chose dans la V3.</summary>
    public const string Identique = "identique";
    /// <summary>Une autre touche de la V3 fait la meme chose.</summary>
    public const string NouvelleTouche = "nouvelle-touche";
    /// <summary>Aucune touche : un geste (un bouton, un clic) la remplace, dit dans l'explication.</summary>
    public const string Geste = "geste";
    /// <summary>Elle n'a pas de pendant, et l'explication dit pourquoi.</summary>
    public const string Supprimee = "supprimee";
    /// <summary>L'ecran de la V3 n'est pas encore construit (ou pas encore au menu) : sa touche sera fixee avec lui.</summary>
    public const string AVenir = "a-venir";
    /// <summary>Un geste nouveau de la V3, sans touche dans l'ancien RIG.</summary>
    public const string Nouveau = "nouveau";

    public static readonly IReadOnlySet<string> Tous = new HashSet<string> { Identique, NouvelleTouche, Geste, Supprimee, AVenir, Nouveau };
}

/// <summary>
/// Pont de l'ecran « Ancienne touche, nouvelle touche » (web/src/ecrans/EcranCorrespondances.tsx), SOC-027 : ecran NEUF,
/// « a maquetter » (Configurer › Mon poste). Il donne, pour chaque raccourci clavier de la V2, la touche ou le geste qui
/// le remplace dans la V3, ecran par ecran ; il se cherche et s'imprime. Sur un ecran de la V3, « ? » montre les lignes
/// de cet ecran avec l'ancienne touche a cote (CA03, web/src/ecrans/EcranCorrespondances.tsx, AideTouchesEcran).
///
/// D'ou vient chaque ligne :
///  - l'ANCIENNE touche : l'inventaire outille du code de la V2 (<see cref="InventaireTouchesV2"/>, genere par
///    tests/SoclePreferencesCheck/inventorier_touches_v2.py ; le banc le recompare au code, CA01) ;
///  - l'ecran de la V3 : le menu de la maquette (<see cref="MenuAccueil"/>, <see cref="CatalogueEcrans"/>), par le code de
///    la procedure, sauf les fenetres de la V2 que la V3 remplace (<see cref="EcransRemplaces"/>) ;
///  - la NOUVELLE touche : les regles du clavier de la V3 (<see cref="Regle"/>), tenues par ses bancs (ClavierCheck,
///    et SoclePreferencesCheck qui verifie chaque ecran annonce, CA04).
/// Aucun code de procedure n'est montre a l'agent : il voit le nom des ecrans.
/// </summary>
public static class EcranCorrespondances
{
    /// <summary>Le nom de l'ecran (menu, catalogue des ecrans).</summary>
    public const string NomEcran = "Ancienne touche, nouvelle touche";

    /// <summary>Le code de l'ecran au menu (sans fiche V2 : MenuGestesCheck le range dans ses ecrans neufs).</summary>
    public const string Code = "PROC_TOUCHES";

    /// <summary>L'accueil de la V3 (pas un ecran du menu : on y revient en fermant les onglets).</summary>
    public const string Accueil = "ACCUEIL";

    /// <summary>La page d'une affaire (la visionneuse), ouverte depuis une affaire : ses actions ont leur lettre.</summary>
    public const string PageDossier = "DOSSIER";

    /// <summary>La recherche partout (palette Ctrl K, et sa page « Recherche ») : elle remplace les fenetres de recherche de la V2.</summary>
    public const string PageRecherche = "RECHERCHE";

    /// <summary>
    /// Les ecrans de la V3 ou Entree, F5 et F7, dans un critere, lancent la recherche (web/src/plan/clavier.ts,
    /// toucheDeRecherche : les touches de l'ancien RIG ; la fenetre ne recharge plus sur F5, Pont/PageWeb.cs). Le banc lit
    /// leur .tsx et refuse un ecran annonce ici qui ne le fait pas, ou un ecran qui le fait sans y etre (CA04). Les autres
    /// ecrans de recherche de la V3 n'ont que leur bouton Rechercher : la fiche le dit, plutot que de promettre une touche.
    /// </summary>
    public static readonly IReadOnlySet<string> EntreeLanceLaRecherche = new HashSet<string>
    {
        "PROC_RECHAFF", "PROC_REQUI", "PROC_COMMANDES_WEB", "PROC_CERTIF", "PROC_HISTORIQUE_ANCIEN_RIG",
    };

    /// <summary>
    /// Les ecrans de la V3 dont la liste se filtre au fil de la saisie des criteres, sans bouton ni touche pour la lancer
    /// (le banc le verifie dans leur .tsx : des criteres, aucun appel a rechercher()). Un ecran construit qui n'est ni ici ni
    /// dans <see cref="EntreeLanceLaRecherche"/> n'a pas de critere : la fiche le dit, et le banc verifie qu'il n'en a pas.
    /// </summary>
    public static readonly IReadOnlySet<string> RechercheAuFilDeLaSaisie = new HashSet<string>
    {
        "PROC_ACTES_NUM", "PROC_DEMANDE", "PROC_GEDJUD", "PROC_COFFRE_FORT", "PROC_GCLIENT", "PROC_UTILISATEUR",
    };

    /// <summary>
    /// Les ecrans de la V3 ou F5, dans un critere, efface les criteres et la liste comme dans la V2 (Requisitions : RAZ,
    /// frmREQUI.cs:421) au lieu de lancer la recherche. Le banc le verifie dans leur .tsx.
    /// </summary>
    public static readonly IReadOnlySet<string> F5EffaceLesCriteres = new HashSet<string> { "PROC_REQUI" };

    /// <summary>Les touches qui lancent la recherche dans un critere de la V3 (TOUCHES_RECHERCHE de web/src/plan/clavier.ts).</summary>
    public static readonly IReadOnlySet<string> TouchesDeRecherche = new HashSet<string> { "Entree", "F5", "F7" };

    /// <summary>Les fenetres de la V2 que la V3 remplace par un autre ecran (ni au menu, ni sous leur code).</summary>
    public static readonly IReadOnlyDictionary<string, string> EcransRemplaces = new Dictionary<string, string>
    {
        ["PROC_ACCUEIL"] = Accueil,
        ["PROC_RIGCLIENTNEWSRIG"] = "PROC_ACTUALITES",
        ["PROC_RIGPARAMACCUEIL"] = "PROC_ACCUEIL_PARAM",
        ["PROC_VGED"] = PageDossier,
        ["PROC_VDOSSIER"] = PageDossier,
        ["PROC_BATEAU"] = "PROC_RECHERCHE_BATEAU",
        // Meme fonction, autre code au menu de la maquette (correspondance a valider par le referent) : la gestion des
        // experts et des juges, les divergences BEN, les habilitations (les droits des comptes, SOC-002).
        ["PROC_GEXPERT"] = "PROC_EXPERT",
        ["PROC_GJUGE"] = "PROC_ACTEUR_JUD",
        ["PROC_DBEN"] = "PROC_DBEN_ALERT",
        ["PROC_GESTIONHABILITATION"] = "PROC_UTILISATEUR",
        // Les fenetres de recherche ouvertes depuis un autre ecran (OCX de recherche de la V2) : la palette.
        ["PROC_RECHERCHE_AUDCAB"] = PageRecherche,
        ["PROC_RECHERCHE_DOSSIERJUDICIAIRE"] = PageRecherche,
        ["PROC_RECHERCHE_ENTREPRISE_INFOGREFFE"] = PageRecherche,
        ["PROC_RECHERCHE_INSTANCE"] = PageRecherche,
        ["PROC_RECHERCHE_MESURE"] = PageRecherche,
        ["PROC_RECHERCHE_PREVENTION"] = PageRecherche,
        ["PROC_RECHERCHE_PROCEDURECOLLECTIVE"] = PageRecherche,
        ["PROC_RECHRECHE_JUGE"] = PageRecherche,
        ["PROC_RECHRECHE_MANDATAIRE_PC"] = PageRecherche,
    };

    /// <summary>
    /// Ou en est la fiche avant la formation des 37 autres greffes (CA05) : « avant-beta » (elle n'est pas encore passee
    /// chez les greffes beta), « retours-en-cours », « livrable » (tous les retours integres). Le banc refuse « livrable »
    /// tant qu'un retour n'est pas integre.
    /// </summary>
    public const string EtatFiche = "avant-beta";

    /// <summary>Les retours des greffes beta (Le Puy, Chartres, Vienne) sur la fiche : aucun encore (SOC-027 T03).</summary>
    public static readonly RetourBeta[] RetoursBeta = [];

    /// <summary>
    /// La regle de CA05 : la fiche ne part aux 37 autres greffes (« livrable ») que si les greffes beta ont rendu leurs
    /// retours et que chacun est integre. Rend le motif d'un refus, ou null. Chaque retour vise une ligne de la fiche.
    /// </summary>
    public static string? RefusLivraison(string etat, IReadOnlyList<RetourBeta> retours, IReadOnlyList<LigneCorrespondance> fiche)
    {
        if (etat is not ("avant-beta" or "retours-en-cours" or "livrable")) return $"état de la fiche inconnu : « {etat} »";
        if (retours.FirstOrDefault(r => fiche.All(l => l.Numero != r.Ligne)) is { } horsFiche)
            return $"le retour de {horsFiche.Greffe} vise la ligne {horsFiche.Ligne}, absente de la fiche";
        if (etat != "livrable") return null;
        if (retours.Count == 0) return "aucun retour des greffes bêta : la fiche ne peut pas partir aux autres greffes";
        return retours.FirstOrDefault(r => !r.Integre) is { } attente
            ? $"le retour de {attente.Greffe} sur la ligne {attente.Ligne} n'est pas intégré"
            : null;
    }

    /// <summary>La fiche entiere, construite une fois.</summary>
    public static readonly LigneCorrespondance[] Fiche = Construire();

    [JSInvokable("EcranCorrespondances.Ouvrir")]
    public static VueEcranCorrespondances Ouvrir() => new(Fiche, EtatFiche, RetoursBeta);

    /// <summary>Les lignes d'un ecran de la V3 (son code), puis celles qui valent partout : l'aide « ? » d'un ecran (CA03).</summary>
    [JSInvokable("EcranCorrespondances.DeLEcran")]
    public static LigneCorrespondance[] DeLEcran(string code)
        => [.. Fiche.Where(l => l.EcranV3 == code), .. Fiche.Where(l => l.EcranV2 == ToutesLesProcedures)];

    /// <summary>Le nom des lignes qui valent sur toute la V2 (barre d'outils, menus).</summary>
    public const string ToutesLesProcedures = "Tous les écrans";

    /// <summary>Le code de l'ecran de la V3 ou essayer une touche d'une procedure de la V2 ; null si la V3 n'en a pas.</summary>
    public static string? EcranV3De(string procedureV2)
    {
        if (EcransRemplaces.TryGetValue(procedureV2, out var remplace)) return remplace;
        return CatalogueEcrans.Nom(procedureV2).Length > 0 ? procedureV2 : null;
    }

    /// <summary>L'ecran existe-t-il deja dans la V3 (maquette) ? Les pages (accueil, dossier, recherche) toujours.</summary>
    public static bool EstConstruit(string ecranV3)
        => ecranV3 is Accueil or PageDossier or PageRecherche
           || CatalogueEcrans.Familles.SelectMany(f => f.Ecrans).Any(e => e.Code == ecranV3);

    /// <summary>Le nom d'un ecran de la V3, tel que l'agent le voit.</summary>
    public static string NomDe(string ecranV3) => ecranV3 switch
    {
        Accueil => "Accueil",
        PageDossier => "Page du dossier",
        PageRecherche => "Recherche partout (Ctrl K)",
        _ => CatalogueEcrans.Nom(ecranV3),
    };

    /// <summary>Ce que faisait chaque action de l'inventaire, en clair.</summary>
    private static string GesteDe(string action) => action switch
    {
        "rechercher" => "Lancer la recherche",
        "valider-saisie" => "Valider le champ saisi",
        "valider-ligne" => "Choisir la ligne de la liste",
        "ouvrir-ligne" => "Ouvrir la ligne choisie",
        "liste-entree" => "Rester sur la ligne (Entrée ne descend pas)",
        "actualiser" => "Actualiser la liste",
        "effacer-criteres" => "Effacer les critères",
        "fermer" => "Fermer ou annuler",
        "arreter-chargement" => "Arrêter le chargement de l'arbre",
        "adresse" => "Saisir l'adresse",
        "masquer-encaissements" => "Masquer ou montrer les encaissements repris",
        "remplir-automatiquement" => "Remplir la saisie automatiquement",
        "derniers-resultats" => "Reprendre le dernier DCA ou statut",
        "aide" => "Afficher l'aide des touches",
        "terminer" => "Terminer la réquisition",
        "annuler-modifications" => "Annuler les modifications de la liste",
        "essai-developpeur" => "Outil d'essai des développeurs",
        "sans-effet" => "Aucun (touche sans effet)",
        _ => action,
    };

    /// <summary>
    /// La regle du clavier de la V3 pour une touche de la V2 : (nouvelle touche, statut, explication). Les gestes communs
    /// (rechercher, fermer, choisir une ligne...) suivent les regles de toute la V3 ; un geste propre a un ecran se decide
    /// avec cet ecran : « a venir » tant qu'il n'est pas construit.
    /// </summary>
    // fix-ok: mesure du 2026-11-21 (liste des ecarts de la fiche) -- « valider le champ » (Entree de l'intervalle des alertes
    // de l'accueil, F7 de la gestion des audiences, recherche en commentaire dans la V2) tombait dans la regle « rechercher » et
    // la fiche annoncait a tort « Entree n'y lance pas la recherche » : regle a part ; et quatre ecrans de la V2 au menu sous un
    // autre code (experts, juges, divergences BEN, habilitations) sortaient « pas au menu » : EcransRemplaces.
    // Decision du chef du plan (21/11/2026) : F5 et F7 ne rechargent plus la fenetre (Pont/PageWeb.cs) et lancent la
    // recherche la ou Entree la lance : la fiche ne previent plus que « F5 recharge toute la fenetre ».
    public static (string? Nouvelle, string Statut, string Explication) Regle(string touche, string action, string ecranV3, bool construit)
    {
        switch (action)
        {
            case "sans-effet":
                return (null, StatutsCorrespondance.Supprimee, "Sans effet dans l'ancien RIG (son code est vide ou en commentaire) : rien à reprendre.");
            case "essai-developpeur":
                return (null, StatutsCorrespondance.Supprimee, "Écran d'essai des développeurs : il n'existe pas dans la V3.");
            case "fermer":
                return ("Échap", touche == "Echap" ? StatutsCorrespondance.Identique : StatutsCorrespondance.NouvelleTouche,
                        "Échap ferme ce qui est ouvert : une fenêtre, l'affaire, puis l'écran.");
            case "liste-entree":
                return ("Entrée", StatutsCorrespondance.Identique, "Dans une liste de la V3 non plus, Entrée ne fait pas descendre d'une ligne.");
            case "aide":
                return ("?", StatutsCorrespondance.NouvelleTouche, "« ? » (hors d'un champ) montre les touches de l'écran ouvert, avec l'ancienne touche à côté.");
            case "rechercher" when ecranV3 == Accueil:
                return ("Ctrl K", StatutsCorrespondance.NouvelleTouche, "La recherche de l'accueil est la palette : Ctrl K, la saisie, puis Entrée.");
            case "rechercher" or "valider-saisie" when ecranV3 == PageRecherche:
                return ("Ctrl K", StatutsCorrespondance.NouvelleTouche, "Les fenêtres de recherche de l'ancien RIG sont remplacées par la palette : Ctrl K, la saisie, puis Entrée.");
            case "rechercher" when EntreeLanceLaRecherche.Contains(ecranV3):
            {
                // Sur un ecran ou F5 efface les criteres (Requisitions), seules Entree et F7 lancent la recherche.
                var f5Efface = F5EffaceLesCriteres.Contains(ecranV3);
                var lancent = f5Efface ? "Entrée et F7 la lancent ; F5 y efface les critères" : "Entrée, F5 et F7 la lancent toutes les trois";
                return TouchesDeRecherche.Contains(touche) && !(f5Efface && touche == "F5")
                    ? (Lisible(touche), StatutsCorrespondance.Identique, $"{Lisible(touche)} dans un critère lance la recherche, comme avant ; {lancent}.")
                    : ("Entrée", StatutsCorrespondance.NouvelleTouche, $"{Lisible(touche)} n'est pas repris pour la recherche : {lancent}.");
            }
            case "rechercher" when RechercheAuFilDeLaSaisie.Contains(ecranV3):
                return (null, StatutsCorrespondance.Geste, "La liste se filtre au fil de la saisie des critères : plus besoin de la lancer.");
            case "rechercher" when construit:
                return (null, StatutsCorrespondance.Supprimee,
                        "L'écran de la V3 n'a pas de critère de recherche : il montre sa liste entière (écart à valider).");
            case "valider-saisie" when construit:
                return (null, StatutsCorrespondance.Geste, "Plus besoin de valider le champ : la V3 prend la saisie au fil de la frappe.");
            case "valider-ligne" or "ouvrir-ligne" when ecranV3 == PageRecherche:
                return ("Entrée", touche == "Entree" ? StatutsCorrespondance.Identique : StatutsCorrespondance.NouvelleTouche,
                        "Dans la palette, les flèches choisissent la ligne et Entrée l'ouvre.");
            case "valider-ligne" or "ouvrir-ligne" when construit:
                return (null, StatutsCorrespondance.Geste, "Un clic sur la ligne l'ouvre : la V3 ne choisit pas encore une ligne au clavier (écart signalé).");
            case "actualiser" when construit:
                return (null, StatutsCorrespondance.Geste,
                        ecranV3 == Accueil ? "Les tuiles de l'accueil se relisent seules." : "La liste se relit à l'ouverture de l'écran et après chaque geste ; relancer la recherche la relit.");
            case "effacer-criteres" when touche == "F5" && F5EffaceLesCriteres.Contains(ecranV3):
                return ("F5", StatutsCorrespondance.Identique, "F5 dans un critère efface les critères et la liste, comme avant (bouton Réinitialiser).");
            case "effacer-criteres" when construit:
                return (null, StatutsCorrespondance.Geste, "Vider les champs des critères : la V3 n'a pas de touche pour tout effacer.");
            case "arreter-chargement" when construit:
                return ("Échap", StatutsCorrespondance.NouvelleTouche, "Échap ferme l'affaire, et avec elle ce qu'elle chargeait.");
            case "adresse" when ecranV3 == "PROC_FCLIENT":
                return (null, StatutsCorrespondance.Geste, "L'adresse se saisit dans le champ Adresse de la fiche, sans fenêtre à part.");
            case "terminer" when construit:
                return (null, StatutsCorrespondance.Geste, "Bouton Terminer de l'écran.");
            case "masquer-encaissements" or "derniers-resultats" or "adresse" or "annuler-modifications" or "remplir-automatiquement" when construit:
                return (null, StatutsCorrespondance.Supprimee,
                        "L'écran de la V3 n'a pas cette fonction : la touche n'a rien à faire (écart à valider par le référent).");
        }
        return (null, StatutsCorrespondance.AVenir, "L'écran n'est pas encore construit dans la V3 : sa touche sera fixée avec lui.");
    }

    /// <summary>La touche telle que l'agent l'ecrit.</summary>
    public static string Lisible(string touche) => touche switch { "Entree" => "Entrée", "Echap" => "Échap", _ => touche };

    private static LigneCorrespondance[] Construire()
    {
        var lignes = new List<LigneCorrespondance>();
        void Ajouter(string ecranV2, string ancienne, string geste, string? nouvelle, string statut, string explication,
                     string? ecranV3, string? source)
            => lignes.Add(new LigneCorrespondance(lignes.Count + 1, ecranV2, ancienne, geste, nouvelle, statut, explication, ecranV3,
                ecranV3 is null ? null : NomDe(ecranV3), ecranV3 is not null && ecranV3 is not (Accueil or PageDossier),
                ecranV3 is not null && EstConstruit(ecranV3), source));

        // Ce qui vaut sur tous les ecrans de la V2 : les lettres soulignees des boutons et des menus, que l'accueil de la V2
        // force a l'affichage (RigConsoleAccueil.cs:97, SPI_SETKEYBOARDCUES) ; et les gestes nouveaux de la V3.
        Ajouter(ToutesLesProcedures, "Alt + lettre soulignée", "Déclencher un bouton de la barre (Valider, Suivant, Quitter, Rechercher…)",
            "Lettre de l'action, sans Alt", StatutsCorrespondance.NouvelleTouche,
            "Sur la page du dossier, chaque action a sa lettre, sans Alt, et « ? » les montre. Sur un écran, Tab mène au bouton et Entrée le déclenche.",
            PageDossier, @"RIG\EXE\Accueil\RigClientAccueil\RigConsoleAccueil.cs:97");
        Ajouter(ToutesLesProcedures, "—", "Rechercher partout : affaires, écrans, pièces", "Ctrl K", StatutsCorrespondance.Nouveau,
            "Nouveau dans la V3 : la palette s'ouvre de partout ; Entrée ouvre la ligne choisie.", PageRecherche, null);
        Ajouter(ToutesLesProcedures, "—", "Voir les touches de l'écran ouvert", "?", StatutsCorrespondance.Nouveau,
            "Nouveau dans la V3 (hors d'un champ) ; l'ancien RIG ne l'avait que sur les réquisitions (F1).", Code, null);

        // Chaque raccourci de l'inventaire de la V2, ecran par ecran, dans l'ordre des noms.
        foreach (var t in InventaireTouchesV2.Lignes.OrderBy(t => t.EcranV2, StringComparer.CurrentCulture).ThenBy(t => t.Touche).ThenBy(t => t.Action))
        {
            var ecranV3 = EcranV3De(t.Procedure);
            string? nouvelle;
            string statut, explication;
            if (ecranV3 is null)
            {
                (nouvelle, statut, explication) = t.Action is "sans-effet" or "essai-developpeur"
                    ? Regle(t.Touche, t.Action, "", false)
                    : (null, StatutsCorrespondance.AVenir, "Cet écran de l'ancien RIG n'a pas encore sa place dans le menu de la V3 (à décider avec le référent).");
            }
            else (nouvelle, statut, explication) = Regle(t.Touche, t.Action, ecranV3, EstConstruit(ecranV3));
            // Un ecran supprime n'a pas d'ecran ou l'essayer.
            Ajouter(t.EcranV2, Lisible(t.Touche), GesteDe(t.Action), nouvelle, statut, explication,
                    statut == StatutsCorrespondance.Supprimee && t.Action is "sans-effet" or "essai-developpeur" ? null : ecranV3, t.Source);
        }
        return [.. lignes];
    }
}
