namespace RigV3Desktop;

/// <summary>
/// Une alerte de l'accueil, telle qu'affichee ET telle qu'on la CLIQUE.
/// <paramref name="Cible"/> dit ou le clic emmene : soit un code de
/// traitement (« PROC_DEMANDE »), soit une affaire precise sous la forme
/// « dossier:004151 ». <paramref name="LibelleCible"/> est ce qu'on lit dans
/// l'infobulle : l'utilisateur sait ou il va AVANT de cliquer.
/// </summary>
/// <paramref name="Nombre"/> est le COMBIEN : le nombre d'elements que cette
/// ligne demande de traiter. Il remplace la barre de couleur a gauche dans la
/// tuile « A traiter » (demande du 17/09/2026) : le greffier voit la CHARGE,
/// pas seulement un niveau d'importance.
public sealed record AlerteGreffe(string Id, string Titre, string Detail, string Statut,
                                  string Cible, string LibelleCible,
                                  string Categorie = CatalogueAlertes.Divers,
                                  int Nombre = 1)
{
    /// <summary>Poids de gravite, pour trier et pour filtrer par niveau.</summary>
    public int Gravite => NiveauDe(Statut);

    public static int NiveauDe(string statut) => statut switch
    {
        "alerte" => 3, "action" => 2, "attente" => 1, _ => 0
    };

    /// <summary>
    /// Une alerte TASK DRIVEN : sa cible est « tache:CODE:dossier ». Le clic
    /// n'ouvre pas seulement l'affaire, il ouvre la TACHE -- l'arbre et la
    /// fiche se replient alors sur les champs que la recette declare.
    /// </summary>
    public (string Code, string Dossier)? Tache
    {
        get
        {
            if (!Cible.StartsWith("tache:", StringComparison.Ordinal)) return null;
            var bouts = Cible.Split(':');
            return bouts.Length == 3 ? (bouts[1], bouts[2]) : null;
        }
    }

    /// <summary>
    /// Une alerte de LOT : sa cible est « lot:CODE ». Le clic n ouvre pas une
    /// affaire mais le TABLEAU des dossiers qui attendent cette recette.
    /// </summary>
    public string? Lot => Cible.StartsWith("lot:", StringComparison.Ordinal)
        ? Cible["lot:".Length..] : null;

    /// <summary>Numero d'affaire quand la cible est une affaire, sinon null.</summary>
    public string? Affaire => Cible.StartsWith("dossier:", StringComparison.Ordinal)
        ? Cible["dossier:".Length..] : null;
}


/// <summary>
/// Un TYPE d'alerte du greffe, tel que le metier le declare (feuille
/// « Alertes accueil v2 Rig »). <paramref name="Code"/> est le CODE_ALERTE
/// de RIG v2 ; il vaut null pour les alertes que le metier demande de CREER
/// et qui n'ont donc pas encore de code. <paramref name="Cible"/> est le
/// code de la procedure qui permet de TRAITER l'alerte : c'est la que le
/// clic emmene, sinon la ligne serait un constat sans suite.
/// </summary>
public sealed record TypeAlerte(int? Code, string Titre, string Categorie,
                                string Cible, string LibelleCible);

/// <summary>
/// Le catalogue des types d'alerte, groupe par CATEGORIE. Les categories
/// sont desactivables d'un seul geste dans les reglages de la tuile : le
/// greffier qui ne tient pas les comptes annuels eteint « DCA » et ne voit
/// plus ses cinq lignes, sans avoir a decocher chaque alerte une par une.
/// </summary>
public static class CatalogueAlertes
{
    public const string Arrivees = "Arrivees";
    public const string Formalites = "Formalites";
    public const string Notifications = "Notifications";
    public const string Das = "DAS";
    public const string Dca = "DCA";
    public const string Corrections = "Corrections diverses";
    public const string Surveillances = "Surveillances Greffe";
    public const string Divers = "Divers";

    /// <summary>L'ordre d'affichage des categories dans la tuile : celui de
    /// la feuille metier, qui suit la journee du greffe.</summary>
    public static readonly string[] Categories =
    [
        Arrivees, Formalites, Notifications, Das, Dca, Corrections, Surveillances, Divers
    ];

    /// <summary>Les types declares par le metier. Une entree sans Code est une
    /// alerte que la feuille marque « a creer » : elle n'existe pas encore en
    /// base, mais elle a deja sa place et sa destination.</summary>
    public static readonly TypeAlerte[] Types =
    [
        new(243, "DAS : Formalites IMR a numeriser",                 Arrivees,      "PROC_ACTES_NUM", "les actes a numeriser"),  // cible approximative : ecran PROC_NUM absent de la maquette
        new(236, "DAS qualifies a traiter",                          Arrivees,      "PROC_ACTES_NUM", "les actes deposes"),  // cible approximative : ecran PROC_DAS absent de la maquette
        new(245, "Formalites IMR numerisees et reclamees a traiter",  Arrivees,      "PROC_DEMANDE",         "les demandes"),
        new(235, "Formalites IMR qualifiees a traiter",              Arrivees,      "PROC_DEMANDE",         "les demandes"),

        new(270, "Formalites",                                       Formalites,    "PROC_DEMANDE",         "les demandes"),
        new(216, "Formalites (sans evenements)",                     Formalites,    "PROC_DEMANDE",         "les demandes"),
        new(282, "Formalites d'occultation d'adresse",               Formalites,    "PROC_DEMANDE", "les demandes"),  // cible approximative : ecran PROC_DJ_OCCULTATIONS absent de la maquette
        new(217, "Regularisations",                                  Formalites,    "PROC_TACHES_LOT", "le traitement en lot"),  // cible approximative : ecran PROC_RELANCELOT absent de la maquette
        new(null, "Regularisations d'occultation d'adresse",         Formalites,    "PROC_DEMANDE", "les demandes"),  // cible approximative : ecran PROC_DJ_OCCULTATIONS absent de la maquette
        new(251, "Reclamations payees sur site local",               Formalites,    "PROC_ENCAISS",         "les encaissements"),
        new(33,  "En reclamations > 15 jours",                       Formalites,    "PROC_TACHES_LOT", "le traitement en lot"),  // cible approximative : ecran PROC_RELANCELOT absent de la maquette
        new(231, "Radiations d'office 90P issus des AVISIR",         Formalites,    "PROC_ARCHIRAD",        "les radiations"),
        new(165, "Attentes d'agrement > 25 jours",                   Formalites,    "PROC_DEMANDE",         "les demandes"),

        new(37,  "Notifications inter Greffes",                      Notifications, "PROC_TELETRANS", "le suivi des teletransmissions"),  // cible approximative : ecran PROC_VNOTIF absent de la maquette
        new(63,  "Notifications rejetees ou en erreur",              Notifications, "PROC_TELETRANS", "le suivi des teletransmissions"),  // cible approximative : ecran PROC_VNOTIF absent de la maquette
        new(191, "BRIS en attente",                                  Notifications, "PROC_TELETRANS", "le suivi des teletransmissions"),  // cible approximative : ecran PROC_VNOTIF absent de la maquette
        new(null, "Notifications en reclamations > 15 jours",        Notifications, "PROC_TACHES_LOT", "le traitement en lot"),  // cible approximative : ecran PROC_RELANCELOT absent de la maquette

        new(261, "Depot d'actes de societes",                        Das,           "PROC_ACTES_NUM", "les actes deposes"),  // cible approximative : ecran PROC_DAS absent de la maquette
        new(181, "Regularisations DAS",                              Das,           "PROC_ACTES_NUM", "les actes deposes"),  // cible approximative : ecran PROC_DAS absent de la maquette
        new(null, "DAS en reclamations > 15 jours",                  Das,           "PROC_TACHES_LOT", "le traitement en lot"),  // cible approximative : ecran PROC_RELANCELOT absent de la maquette
        new(190, "Commandes WEB en attente de numerisation",         Das,           "PROC_ACTES_NUM", "les actes a numeriser"),  // cible approximative : ecran PROC_NUM absent de la maquette
        new(213, "Actes non numerises",                              Das,           "PROC_ACTES_NUM",       "les actes a numeriser"),

        new(36,  "Depot des comptes annuels",                        Dca,           "PROC_DOC_DEMAT", "les documents demateralises"),  // cible approximative : ecran PROC_DEP absent de la maquette
        new(59,  "Regularisations DCA",                              Dca,           "PROC_TACHES_LOT", "le traitement en lot"),  // cible approximative : ecran PROC_DCA_RELANCE absent de la maquette
        new(null, "DCA en reclamations > 15 jours",                  Dca,           "PROC_TACHES_LOT", "le traitement en lot"),  // cible approximative : ecran PROC_DCA_RELANCE absent de la maquette
        new(55,  "Rejets saisie des chiffres du bilan",              Dca,           "PROC_CONTROLE_VIDIGED", "le controle des saisies"),  // cible approximative : ecran PROC_VCHIFFRES absent de la maquette
        new(212, "Bilans non numerises",                             Dca,           "PROC_ACTES_NUM", "les actes a numeriser"),  // cible approximative : ecran PROC_REJETS_BILANS absent de la maquette

        new(185, "Rejets actes deposes Infogreffe",                  Corrections,   "PROC_ACTES_NUM", "les actes deposes"),  // cible approximative : ecran PROC_ACTREJ absent de la maquette
        new(31,  "Rejets BODACC RCS",                                Corrections,   "PROC_DEMANDE", "les demandes"),  // cible approximative : ecran PROC_GESTBOD absent de la maquette
        new(30,  "Rejets casier judiciaire",                         Corrections,   "PROC_DEMANDE", "les demandes"),  // cible approximative : ecran PROC_CJUD absent de la maquette
        new(32,  "Rejets Kbis Infogreffe",                           Corrections,   "PROC_DEMANDE", "les demandes"),  // cible approximative : ecran PROC_CJUDINFOG absent de la maquette
        new(127, "Controle qualite rejets Bodacc RCS",               Corrections,   "PROC_CONTROLE_VIDIGED", "le controle des saisies"),  // cible approximative : ecran PROC_CORDJRCS absent de la maquette
        new(223, "RBE reprise d'historique - REPRIPM a valider",     Corrections,   "PROC_DEMANDE", "les demandes"),  // cible approximative : ecran PROC_BEN absent de la maquette

        new(35,  "Condition suspensive d'immatriculation au RM > 25 jours", Surveillances, "PROC_DEMANDE", "les demandes"),  // cible approximative : ecran PROC_SURVMES absent de la maquette
        new(279, "Condition suspensive d'immatriculation au RM > 30 jours", Surveillances, "PROC_DEMANDE", "les demandes"),  // cible approximative : ecran PROC_SURVMES absent de la maquette
        new(210, "Divergences en attente > 1 mois",                  Surveillances, "PROC_DEMANDE", "les demandes"),  // cible approximative : ecran PROC_DBEN_ALERT absent de la maquette

        new(38,  "Demandes interrompues",                            Divers,        "PROC_DEMANDE",         "les demandes"),
        new(252, "Erreur envoi mail",                                Divers,        "PROC_GESTEMAIL",       "la gestion des mails"),
        new(null, "Signalement divergences",                         Divers,        "PROC_DEMANDE", "les demandes")  // cible approximative : ecran PROC_DBEN_ALERT absent de la maquette
    ];

    /// <summary>L'identifiant stable d'un type : le code quand il existe,
    /// sinon un identifiant derive du libelle — pour que l'interrupteur de
    /// l'alerte survive au redemarrage meme sans code RIG.</summary>
    public static string IdentifiantDe(TypeAlerte t)
        => t.Code is int c
            ? $"alerte-{c}"
            : "alerte-" + new string(t.Titre.ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');

    /// <summary>
    /// Le NOMBRE d'elements en attente sur ce type d'alerte. En attendant le
    /// branchement sur RIG (une requete par CODE_ALERTE), c'est une valeur de
    /// DEMONSTRATION : elle est DERIVEE du code alerte, donc stable d'un
    /// affichage a l'autre -- un tirage au hasard ferait danser les chiffres
    /// a chaque rafraichissement, et la maquette mentirait sur la charge.
    /// Une alerte « a creer » (sans code RIG) ne compte rien : elle n'existe
    /// pas encore en base, afficher un chiffre serait une invention.
    /// </summary>
    /// <summary>
    /// La GRAVITE d'un type d'alerte, lue dans son libelle metier. Toutes les
    /// alertes etaient creees « attente » en dur : les pastilles rondes
    /// sortaient donc toutes de la meme couleur et le compteur « urgents »
    /// restait a zero dans les huit widgets (mesure du 17/09/2026). La feuille
    /// Feuil2 ne porte pas de colonne de gravite ; le libelle, lui, dit
    /// clairement le depassement de delai et le rejet.
    /// - un DELAI franchi (« > 15 jours », « > 1 mois ») = alerte (rouge) ;
    /// - un REJET ou une ERREUR a reprendre = action (orange) ;
    /// - tout le reste = attente (jaune) ;
    /// - une alerte sans aucun element a traiter = ok (vert).
    /// </summary>
    public static string StatutDe(TypeAlerte t)
    {
        if (NombreDemo(t) == 0) return "ok";
        var titre = t.Titre;
        if (titre.Contains('>')) return "alerte";
        if (titre.Contains("Rejet", StringComparison.OrdinalIgnoreCase)
         || titre.Contains("erreur", StringComparison.OrdinalIgnoreCase)
         || titre.Contains("Regularisation", StringComparison.OrdinalIgnoreCase)
         || titre.Contains("interrompue", StringComparison.OrdinalIgnoreCase)) return "action";
        return "attente";
    }

    public static int NombreDemo(TypeAlerte t)
        => t.Code is int c ? c * 7 % 23 : 0;

    /// <summary>L'identifiant de l'interrupteur d'une CATEGORIE, distinct de
    /// ceux des alertes pour que les deux reglages ne se marchent pas dessus.</summary>
    public static string IdentifiantCategorie(string categorie)
        => "cat-" + categorie.ToLowerInvariant().Replace(' ', '-');
}

/// <summary>
/// Les alertes de l'accueil, CALCULEES sur les dossiers connus (JeuDemo) :
/// aucune ligne n'est ecrite en dur, sinon la tuile mentirait des que la
/// file bouge — meme regle que le widget performance (MesuresPerf).
/// </summary>
public sealed class AlertesGreffe
{
    /// <summary>Delai legal de traitement d'une demande, en jours.</summary>
    public const int DelaiLegalJours = 7;

    /// <summary>Le greffier a son poste : tout dossier porte par QUELQU'UN
    /// D'AUTRE et encore en cours est un dossier verrouille pour lui.</summary>
    public string Utilisateur { get; init; } = "R. VILAIN";

    /// <summary>
    /// Ce que la tuile « A traiter » MONTRE : uniquement les types d'alerte
    /// declares par le metier dans « Alertes accueil v2 Rig.xlsx » (feuille
    /// Feuil2), et RIEN d'autre. Demande du 17/09/2026 : la tuile est le
    /// miroir de la feuille metier, pas un melange avec des constats
    /// calcules sur le jeu de demonstration. Ces derniers restent disponibles
    /// par <see cref="ReleverCalculees"/>, qui n'alimente plus l'accueil.
    /// </summary>
    public AlerteGreffe[] Relever()
        => CatalogueAlertes.Types
            .Select(t => new AlerteGreffe(CatalogueAlertes.IdentifiantDe(t), t.Titre,
                t.Code is int c ? $"code alerte {c}" : "alerte a creer - pas encore de code RIG",
                CatalogueAlertes.StatutDe(t), t.Cible, t.LibelleCible, t.Categorie,
                CatalogueAlertes.NombreDemo(t)))
            .ToArray();

    /// <summary>
    /// Les constats CALCULES sur les dossiers connus (hors delai, verrous,
    /// lots, taches en attente). Conserves et verifies, mais PLUS affiches
    /// dans la tuile « A traiter » depuis le 17/09/2026.
    /// </summary>
    public AlerteGreffe[] ReleverCalculees()
    {
        var demandes = JeuDemo.Demandes;
        var liste = new List<AlerteGreffe>();

        var horsDelai = demandes
            .Where(d => d.EnCours && (JeuDemo.Aujourdhui - d.Creation2).TotalDays > DelaiLegalJours)
            .OrderBy(d => d.Creation2)
            .ToArray();
        if (horsDelai.Length > 0)
        {
            var plusVieille = horsDelai[0];
            var jours = (int)(JeuDemo.Aujourdhui - plusVieille.Creation2).TotalDays;
            liste.Add(new("delai",
                $"{horsDelai.Length} demande(s) hors delai legal",
                $"la plus ancienne : J+{jours} - {plusVieille.Nom}",
                "alerte",
                $"dossier:{plusVieille.Dossier}",
                $"affaire {plusVieille.Dossier}",
                CatalogueAlertes.Formalites));
        }

        // Les alertes TASK DRIVEN : une par EVENEMENT RECURRENT reellement
        // en attente. Chacune porte la recette a rejouer, pas un simple
        // constat. Un evenement dont le code n a pas de recette est IGNORE :
        // l alerte n aurait aucun champ a ouvrir, et envoyer le greffier sur
        // une tache vide est pire que ne rien afficher.
        var attendus = JeuDemo.EvenementsAttendus
            .OrderBy(e => e.Signale)
            .Select(e => (Evt: e, Recette: RecettesTaches.ParCode(e.Code),
                          Affaire: demandes.FirstOrDefault(d => d.Dossier == e.Dossier)))
            .Where(x => x.Recette is not null && x.Affaire is not null)
            .ToArray();

        // REGROUPEMENT : quand PLUSIEURS affaires attendent la MEME recette,
        // elles font UNE entree, pas une par dossier. Le greffier n a pas
        // quarante decisions a prendre, il en a une -- « je traite les
        // denominations » -- et le tableau du lot les lui montre ensemble.
        // Une recette qui se saisit ligne par ligne (plusieurs dirigeants par
        // dossier) ne se groupe PAS : son tableau n existe pas, et l envoyer
        // vers un ecran qui la refuse serait une impasse.
        foreach (var groupe in attendus.GroupBy(x => x.Recette!))
        {
            var recette = groupe.Key;
            var tout = groupe.ToArray();

            // CE QUI ARRIVE AUJOURD HUI NE PART PAS EN LOT. Le traitement en
            // lot sert a ecouler l ARRIERE : plusieurs dossiers qui attendent
            // la meme chose depuis des jours. Un evenement signale le jour
            // meme -- un flux EDI qui vient d entrer -- doit rester une
            // alerte NOMMEE, qui mene droit a sa zone d edition : le noyer
            // dans un compteur de lot ferait perdre le seul cas ou le greffe
            // peut encore repondre le jour meme.
            var duJour = tout.Where(x => x.Evt.Signale.Date == JeuDemo.Aujourdhui.Date).ToArray();
            var lignes = tout.Where(x => x.Evt.Signale.Date != JeuDemo.Aujourdhui.Date).ToArray();

            foreach (var x in duJour)
                liste.Add(new($"tache-{x.Evt.Code}-{x.Evt.Dossier}".ToLowerInvariant(),
                    recette.Titre,
                    $"{x.Evt.Dossier} - {x.Affaire!.Nom} - signale aujourd hui",
                    "alerte",
                    $"tache:{x.Evt.Code}:{x.Evt.Dossier}",
                    $"la tache {x.Evt.Code} sur {x.Evt.Dossier}",
                    CatalogueAlertes.Formalites));

            if (lignes.Length > 1 && !recette.ARepetition)
            {
                var plusVieux = (int)(JeuDemo.Aujourdhui - lignes[0].Evt.Signale).TotalDays;
                liste.Add(new($"lot-{recette.Code}".ToLowerInvariant(),
                    $"{lignes.Length} dossiers : {recette.Titre}",
                    $"le plus ancien signale il y a {plusVieux} j - traitement en lot",
                    "action",
                    $"lot:{recette.Code}",
                    $"le tableau des {lignes.Length} dossiers",
                    CatalogueAlertes.Formalites));
                continue;
            }

            foreach (var x in lignes)
            {
                var jours = (int)(JeuDemo.Aujourdhui - x.Evt.Signale).TotalDays;
                liste.Add(new($"tache-{x.Evt.Code}-{x.Evt.Dossier}".ToLowerInvariant(),
                    recette.Titre,
                    $"{x.Evt.Dossier} - {x.Affaire!.Nom} - signale il y a {jours} j",
                    "action",
                    $"tache:{x.Evt.Code}:{x.Evt.Dossier}",
                    $"la tache {x.Evt.Code} sur {x.Evt.Dossier}",
                    CatalogueAlertes.Formalites));
            }
        }

        var verrous = demandes
            .Where(d => d.EnCours && d.Utilisateur != Utilisateur
                        && !string.IsNullOrWhiteSpace(d.Utilisateur))
            .ToArray();
        if (verrous.Length > 0)
            liste.Add(new("verrou",
                $"{verrous.Length} dossier(s) ouvert(s) par un tiers",
                $"{verrous[0].Dossier} - {verrous[0].Utilisateur}",
                "attente",
                "PROC_VERROUS",
                "les dossiers verrouilles",
                CatalogueAlertes.Divers));

        var rejets = demandes.Where(d => d.Etat == "Rejetee").ToArray();
        if (rejets.Length > 0)
            liste.Add(new("rejet",
                $"{rejets.Length} demande(s) rejetee(s)",
                $"a reprendre : {rejets[0].Dossier}",
                "action",
                "PROC_DEMANDE",
                "les demandes",
                CatalogueAlertes.Formalites));

        var anomalies = JeuDemo.Actes.Where(a => !string.IsNullOrWhiteSpace(a.Anomalie)).ToArray();
        if (anomalies.Length > 0)
            liste.Add(new("scan",
                $"{anomalies.Length} piece(s) numerisee(s) a controler",
                anomalies[0].Anomalie,
                "action",
                "PROC_CONTROLE_VIDIGED",
                "le controle des scans",
                CatalogueAlertes.Corrections));

        var aNumeriser = JeuDemo.Arrivees.Where(a => !a.Numerise).ToArray();
        if (aNumeriser.Length > 0)
            liste.Add(new("courrier",
                $"{aNumeriser.Length} courrier(s) arrive(s) non numerise(s)",
                $"le plus ancien : {aNumeriser[^1].Horodatage}",
                "attente",
                "PROC_ARRIVEE",
                "le courrier du jour",
                CatalogueAlertes.Arrivees));

        var edi = JeuDemo.Arrivees.Where(a => a.Mode == "EDI").OrderByDescending(a => a.Instant).ToArray();
        liste.Add(edi.Length > 0
            ? new("teletr", "Teletransmission au vert",
                  $"dernier envoi recu le {edi[0].Horodatage}", "ok",
                  "PROC_TELETRANS", "le suivi des teletransmissions", CatalogueAlertes.Notifications)
            : new("teletr", "Aucune teletransmission recue",
                  "aucun flux EDI dans le courrier du jour", "action",
                  "PROC_TELETRANS", "le suivi des teletransmissions", CatalogueAlertes.Notifications));

        return liste.ToArray();
    }
}

/// <summary>
/// Reglages de la tuile Alertes, conserves dans accueil.json. Ils portent
/// sur ce que la tuile MONTRE : a partir de quelle gravite, dans quel ordre,
/// et combien de lignes au plus.
/// </summary>
public sealed class ReglagesAlertes
{
    /// <summary>Gravite minimale affichee : 0 tout, 1 attente, 2 action, 3 alerte.</summary>
    public int NiveauMini { get; set; }

    /// <summary>« gravite » (le plus grave en tete) ou « titre » (alphabetique).</summary>
    public string Tri { get; set; } = "gravite";

    /// <summary>Nombre maximum de lignes ; 0 = pas de plafond.</summary>
    public int Limite { get; set; }

    /// <summary>Le detail sous le titre, qu'on peut replier pour une tuile plus dense.</summary>
    public bool AfficherDetail { get; set; }

    /// <summary>Applique les reglages a une liste relevee, dans l'ordre : filtre
    /// d'ecoute, filtre de gravite, tri, plafond. Fonction pure : verifiable
    /// sans interface.</summary>
    public AlerteGreffe[] Appliquer(IEnumerable<AlerteGreffe> relevees, Func<string, bool> ecoutee,
                                    Func<string, bool>? categorieEcoutee = null)
    {
        var vues = relevees.Where(a => categorieEcoutee is null || categorieEcoutee(a.Categorie))
                           .Where(a => ecoutee(a.Id))
                           .Where(a => a.Gravite >= NiveauMini);
        vues = Tri == "titre"
            ? vues.OrderBy(a => a.Titre, StringComparer.OrdinalIgnoreCase)
            : vues.OrderByDescending(a => a.Gravite).ThenBy(a => a.Titre, StringComparer.OrdinalIgnoreCase);
        var tableau = vues.ToArray();
        return Limite > 0 && tableau.Length > Limite ? tableau[..Limite] : tableau;
    }
}

/// <summary>
/// Une ligne du panneau de DROITE du widget metier : une demande qui attend
/// sous l'alerte selectionnee. Colonnes de la maquette metier du 17/09/2026
/// (N CHRONO, societe, SIREN, type, recu le, anciennete, etat).
/// </summary>
public sealed record DemandeAlerte(string Chrono, string Societe, string Siren,
                                   string Type, string RecuLe, int Anciennete, string Etat);

/// <summary>
/// Ce que montre un widget METIER -- un widget par categorie d'alertes de la
/// feuille « Alertes accueil v2 Rig » (Feuil2) : a gauche les alertes de la
/// categorie, a droite les demandes de l'alerte selectionnee.
/// </summary>
public static class PanneauMetier
{
    /// <summary>Les gestes proposes en haut du panneau gauche. Ce sont des
    /// boutons de MAQUETTE : ils nomment le geste metier de la categorie,
    /// aucun n'ecrit quoi que ce soit. Ne pas les presenter comme actifs.</summary>
    public static readonly Dictionary<string, string[]> Actions = new()
    {
        [CatalogueAlertes.Arrivees]      = ["Numeriser", "Qualifier", "Affecter"],
        [CatalogueAlertes.Formalites]    = ["Ajouter un depot", "Reclamer", "Refuser", "Radier"],
        [CatalogueAlertes.Notifications] = ["Renvoyer", "Acquitter", "Reclamer"],
        [CatalogueAlertes.Das]           = ["Ajouter un depot", "Reclamer", "Refuser", "Radier"],
        [CatalogueAlertes.Dca]           = ["Ajouter un bilan", "Numeriser", "Reclamer", "Refuser"],
        [CatalogueAlertes.Corrections]   = ["Corriger", "Renvoyer", "Valider"],
        [CatalogueAlertes.Surveillances] = ["Relancer", "Radier", "Clore"],
        [CatalogueAlertes.Divers]        = ["Reprendre", "Renvoyer"]
    };

    /// <summary>
    /// L'INTITULE METIER de la categorie, tel que la maquette du 17/09/2026
    /// ecrit le titre du widget (« Depots d'actes · DAS »). Le nom de categorie
    /// de la Feuil2 est un CODE de classement (DAS, DCA) : seul, il ne dit pas
    /// au greffier ce que le widget contient.
    /// </summary>
    public static readonly Dictionary<string, string> Intitules = new()
    {
        [CatalogueAlertes.Arrivees]      = "Arrivees",
        [CatalogueAlertes.Formalites]    = "Formalites",
        [CatalogueAlertes.Notifications] = "Notifications",
        [CatalogueAlertes.Das]           = "Depots d'actes",
        [CatalogueAlertes.Dca]           = "Comptes annuels",
        [CatalogueAlertes.Corrections]   = "Corrections diverses",
        [CatalogueAlertes.Surveillances] = "Surveillances",
        [CatalogueAlertes.Divers]        = "Divers"
    };

    /// <summary>Le titre du widget : l'intitule metier, suivi du CODE de
    /// categorie quand il apporte quelque chose. « Formalites · Formalites »
    /// ne dirait rien deux fois.</summary>
    public static string Titre(string categorie)
    {
        var intitule = Intitules.TryGetValue(categorie, out var i) ? i : categorie;
        // Le CODE n'est ajoute que s'il apprend quelque chose. « Surveillances
        // Greffe » contient deja « Surveillances » : le suffixe n'aurait fait
        // que repeter le titre et, a quatre widgets par ligne, le tronquer.
        var redondant = categorie.StartsWith(intitule, StringComparison.OrdinalIgnoreCase)
                     || intitule.StartsWith(categorie, StringComparison.OrdinalIgnoreCase);
        return redondant ? intitule : intitule + " · " + categorie;
    }

    public static string[] ActionsDe(string categorie)
        => Actions.TryGetValue(categorie, out var a) ? a : [];

    /// <summary>Plafond de lignes montrees a droite : au-dela, la tuile
    /// deroulerait sans rien apprendre de plus.</summary>
    public const int LignesMax = 12;

    /// <summary>
    /// Les demandes qui attendent sous une alerte. Comme
    /// <see cref="CatalogueAlertes.NombreDemo"/>, c'est de la DEMONSTRATION en
    /// attendant la requete RIG par CODE_ALERTE : les lignes sont DERIVEES des
    /// dossiers connus (JeuDemo.Demandes), prises en boucle a partir d'un rang
    /// fixe par l'identifiant de l'alerte. Donc stables d'un affichage a
    /// l'autre -- une tuile qui redistribue ses dossiers a chaque rendu
    /// mentirait sur la charge.
    /// </summary>
    public static DemandeAlerte[] Lignes(AlerteGreffe alerte)
    {
        var dossiers = JeuDemo.Demandes;
        if (dossiers.Length == 0 || alerte.Nombre <= 0) return [];

        var combien = Math.Min(Math.Min(alerte.Nombre, LignesMax), dossiers.Length);
        // Plafonne au nombre de dossiers : au-dela, le modulo reboucle et le premier dossier ressort en double.
        // Rang de depart DETERMINISTE : la somme des caracteres de l'id, pas
        // string.GetHashCode() -- ce dernier est reamorce a chaque demarrage du
        // processus en .NET, les dossiers changeraient donc a chaque lancement.
        var depart = alerte.Id.Sum(c => c) % dossiers.Length;
        var lignes = new DemandeAlerte[combien];
        for (var i = 0; i < combien; i++)
        {
            var d = dossiers[(depart + i) % dossiers.Length];
            var anciennete = (int)(JeuDemo.Aujourdhui - d.Creation2).TotalDays;
            lignes[i] = new DemandeAlerte(d.Numero, d.Client.Length > 0 ? d.Client : d.Nom,
                Masquer(d.Siren), TypeDe(d), d.Creation, anciennete, d.Etat);
        }
        return lignes;
    }

    /// <summary>
    /// Le SIREN tel que la maquette metier le montre : MASQUE. Les widgets de
    /// l'accueil sont lus en salle et projetes en demonstration ; le numero
    /// entier n'y apprend rien et sort une donnee d'entreprise de son ecran.
    /// Un dossier sans SIREN garde son tiret : masquer le vide dirait un numero
    /// qui n'existe pas.
    /// </summary>
    public static string Masquer(string siren)
        => siren.Length > 0 ? "*** *** ***" : "-";

    /// <summary>La CHARGE URGENTE d'une categorie : la somme des elements a
    /// traiter des seules alertes de gravite « alerte ». C'est le compteur
    /// « URGENTS » de la maquette -- un nombre de dossiers, pas de lignes.</summary>
    public static int Urgents(IEnumerable<AlerteGreffe> lignes)
        => lignes.Where(a => a.Statut == "alerte").Sum(a => a.Nombre);

    /// <summary>
    /// La charge DEMATERIALISEE : les alertes qui attendent une numerisation ou
    /// arrivent par un flux demat. Reconnues sur le LIBELLE seul, faute d'un
    /// indicateur porte par le catalogue metier. La CIBLE d'ecran, elle, est
    /// ecartee : toutes les alertes DAS pointent vers PROC_ACTES_NUM, le
    /// compteur affichait donc la charge ENTIERE de la categorie (mesure du
    /// 17/09/2026 : 50 sur 50) et ne distinguait plus rien.
    /// </summary>
    public static int Demat(IEnumerable<AlerteGreffe> lignes)
        => lignes.Where(a => a.Titre.Contains("numeris", StringComparison.OrdinalIgnoreCase)
                          || a.Titre.Contains("demat", StringComparison.OrdinalIgnoreCase)
                          || a.Titre.Contains("WEB", StringComparison.Ordinal))
                  .Sum(a => a.Nombre);

    /// <summary>Le REGISTRE dont releve la categorie, montre comme puce devant
    /// le titre du widget (« RCS » dans la maquette). Le catalogue de la
    /// Feuil2 ne porte que des alertes du registre du commerce.</summary>
    public static string Registre(string categorie) => "RCS";

    /// <summary>
    /// Le PALIER de charge d'un widget, qui colore la pastille de son en-tete
    /// (demande du 17/09/2026) : bleu tant que la charge reste tenable, orange
    /// quand elle monte, rouge quand elle deborde. La demande disait « bleu
    /// jusqu'a 10, orange jusqu'a 20, rouge a partir de 30 » : le palier rouge
    /// commence des 21, sinon les charges de 21 a 29 n'auraient aucune couleur.
    /// Une charge NULLE n'est pas une charge basse : elle est verte, comme les
    /// lignes d'alerte a zero.
    /// </summary>
    public static string Palier(int charge) => charge switch
    {
        <= 0  => "vide",
        <= 10 => "bas",
        <= 20 => "moyen",
        _     => "haut"
    };

    /// <summary>Le TYPE lisible d'une demande : ce que la maquette metier
    /// montre en colonne TYPE (« Modification », « Radiation »...). Il se lit
    /// dans le libelle de la demande, avant le tiret.</summary>
    private static string TypeDe(DemandeRcs d)
    {
        var tiret = d.Nom.IndexOf(" - ", StringComparison.Ordinal);
        return tiret > 0 ? d.Nom[..tiret] : d.Traitement;
    }
}
