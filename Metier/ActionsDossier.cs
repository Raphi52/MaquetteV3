namespace RigV3Desktop;

// ============================================================
// LA BARRE D'ACTIONS DE LA VUE DU DOSSIER (plan « Vue du dossier = coeur des
// actions », lot 1, 20/11/2026). La page « Visualisation du dossier » devient le
// point de depart de ce qu'un agent de greffe fait sur une affaire : jusque-la,
// facturer, ecrire un courrier, annoncer au BODACC... obligeait a revenir a
// l'accueil et a rechercher l'affaire dans chaque ecran.
//
// Chaque action est rangee sous un VERBE et vise :
//   - soit un GESTE LOCAL, joue dans la page sans changer d'ecran (afficher le
//     Kbis, l'imprimer, imprimer l'arborescence, envoyer le dossier par mail,
//     copier le numero ou le SIREN) ;
//   - soit un ECRAN, ouvert dans un onglet « pour ce dossier »
//     (Pont/PlanTravailActions.cs) : l'onglet retient l'affaire et y ramene ;
//   - soit la FAMILLE des courriers (lot 9) : la barre la remplace par un bouton par
//     modele de courrier divers qui vaut pour l'affaire (Metier/CourriersDivers.cs), qui
//     produit le courrier rempli dans la colonne du document ; aucun modele, rien.
//     Elle ouvrait l'ecran PROC_COURRIER_DIVERS, le CATALOGUE des modeles : un ecran
//     de parametrage, qui reste au menu de l'accueil.
//
// Un ecran n'entre ici que s'il est REELLEMENT monte par l'aiguillage
// (web/src/plan/Aiguillage.tsx) : un code qui tomberait sur « ecran a venir »
// (EcranNonMaquette) ferait une action qui n'agit pas. tests/PlanTravailCheck
// le verifie sur le fichier.
//
// La TOUCHE est une lettre, unique parmi les actions qu'un meme type d'affaire
// montre (verifie par le meme banc). Les libelles des ecrans sont ceux de leur
// onglet : ce qu'on clique est ce que l'onglet affichera.
// ============================================================

/// <summary>Une action de la barre du dossier.</summary>
/// <param name="Code">Identifiant neutre remonte par la page (geste « actionDossier ») :
/// jamais un code d'ecran, la page n'en porte aucun (tests/NomMetierCheck).</param>
/// <param name="Verbe">Le groupe de la barre (<see cref="ActionsDossier.Verbes"/>).</param>
/// <param name="Ecran">Le code de l'ecran ouvert pour ce dossier ; nul pour un geste local.</param>
/// <param name="Types">Les types d'affaire qui la montrent (Dossier.Type : RCS, JUD).</param>
/// <param name="Detail">L'infobulle : ce que fait l'action.</param>
public sealed record ActionDossier(string Code, string Verbe, string Libelle, char Touche,
                                   string? Ecran, string[] Types, string Detail)
{
    public bool EstLocale => Ecran is null;
}

public static class ActionsDossier
{
    public const string Delivrer = "Delivrer";
    public const string Enregistrer = "Enregistrer";
    public const string Facturer = "Facturer";
    public const string Ecrire = "Ecrire";
    public const string Suivre = "Suivre";

    /// <summary>L'ordre des groupes dans la barre.</summary>
    public static readonly string[] Verbes = [Delivrer, Enregistrer, Facturer, Ecrire, Suivre];

    // Les gestes LOCAUX : leur code est le nom de l'action jouee par la session.
    public const string Kbis = "kbis";
    public const string ImprimerKbis = "imprimerKbis";
    public const string ImprimerArborescence = "imprimerArborescence";
    public const string EnvoyerDossier = "envoyerDossier";
    /// <summary>Copies : jouees par la PAGE (le presse-papiers est le sien), sans aller-retour au C#.</summary>
    public const string CopierNumero = "copierNumero";
    public const string CopierSiren = "copierSiren";

    /// <summary>La famille des courriers : jamais montree telle quelle, la barre la remplace par
    /// <see cref="DesCourriers"/>. Sa lettre lui est reservee : elle revient au premier modele.</summary>
    public const string Courriers = "courriers";
    /// <summary>Le code d'une action courrier : ce prefixe, puis le code du modele (« courrier:CD_NOTIF »).</summary>
    public const string PrefixeCourrier = "courrier:";

    private static readonly string[] Rcs = [DomaineGed.Rcs];
    private static readonly string[] Jud = [DomaineGed.Judiciaire];
    private static readonly string[] Tous = [DomaineGed.Rcs, DomaineGed.Judiciaire];

    public static readonly ActionDossier[] Catalogue =
    [
        // ---- Delivrer : ce qui sort du greffe vers le demandeur ----
        new(Kbis, Delivrer, "Kbis a jour", 'K', null, Tous,
            "Affiche le Kbis en vigueur dans la colonne du document"),
        new(ImprimerKbis, Delivrer, "Imprimer le Kbis", 'I', null, Tous,
            "Affiche le Kbis a jour et ouvre le choix de l'imprimante"),
        new(ImprimerArborescence, Delivrer, "Imprimer l'arborescence", 'P', null, Tous,
            "Imprime la liste des dossiers et des pieces de l'affaire"),
        new("requisitions", Delivrer, "Requisitions - commande de documents", 'R', "PROC_REQUI", Rcs,
            "Commander des documents pour ce dossier (nouvel onglet)"),
        new("commandesWeb", Delivrer, "Commandes web", 'W', "PROC_COMMANDES_WEB", Rcs,
            "Traiter les commandes web recues pour ce dossier (nouvel onglet)"),

        // ---- Enregistrer : ce qui entre au registre ou au role ----
        new("demandes", Enregistrer, "Demandes", 'D', "PROC_DEMANDE", Rcs,
            "Les demandes et formalites de ce dossier (nouvel onglet)"),
        new("depotsJudiciaires", Enregistrer, "Depots judiciaires", 'D', "PROC_DEPOTJUD", Jud,
            "Enregistrer un depot au tribunal pour cette affaire (nouvel onglet)"),
        new("bodacc", Enregistrer, "BODACC", 'B', "BODACC", Tous,
            "Les annonces BODACC de ce dossier (nouvel onglet)"),
        new("cloture", Enregistrer, "Cloture", 'O', "PROC_CLOT", Rcs,
            "Fermer une affaire terminee (nouvel onglet)"),
        new("archivage", Enregistrer, "Archivage / radiation", 'A', "PROC_ARCHIRAD", Rcs,
            "Archiver ou radier ce dossier (nouvel onglet)"),

        // ---- Facturer ----
        new("factures", Facturer, "Factures", 'F', "PROC_FACTURE", Tous,
            "Les factures de ce dossier (nouvel onglet)"),
        new("encaissements", Facturer, "Encaissements", 'E', "PROC_ENCAISS", Tous,
            "Les reglements de ce dossier (nouvel onglet)"),

        // ---- Ecrire ----
        new(Courriers, Ecrire, "Courriers divers", 'C', null, Jud,
            "Les courriers types qui valent pour la nature de l'affaire"),
        new("redaction", Ecrire, "Redaction d'une decision", 'R', "PROC_REDA", Jud,
            "Rediger une decision pour cette affaire (nouvel onglet)"),
        new(EnvoyerDossier, Ecrire, "Envoyer le dossier par mail", 'M', null, Tous,
            "Envoie le dossier entier, toutes ses pieces, par courriel"),

        // ---- Suivre ----
        new(CopierNumero, Suivre, "Copier le n°", 'N', null, Tous,
            "Copie le numero de l'affaire dans le presse-papiers"),
        new(CopierSiren, Suivre, "Copier le SIREN", 'S', null, Tous,
            "Copie le SIREN dans le presse-papiers"),
        new("consultationJudiciaire", Suivre, "Consultation judiciaire", 'J', "PROC_CJUD", Jud,
            "Consulter l'affaire au role (nouvel onglet)"),
        new("audiences", Suivre, "Audiences", 'U', "PROC_AUDIENCE", Jud,
            "Les audiences de cette affaire (nouvel onglet)"),
        new("gedJudiciaire", Suivre, "GED judiciaire", 'G', "PROC_GEDJUD", Jud,
            "Les documents judiciaires de cette affaire (nouvel onglet)"),
        new("verrous", Suivre, "Dossiers verrouilles", 'V', "PROC_VERROUS", Tous,
            "Qui tient ce dossier verrouille (nouvel onglet)"),
        new("teletransmissions", Suivre, "Teletransmissions (EDI)", 'T', "PROC_TELETRANS", Rcs,
            "Les envois EDI de ce dossier (nouvel onglet)"),
    ];

    /// <summary>Les actions qu'un type d'affaire montre, dans l'ordre du catalogue.</summary>
    public static IEnumerable<ActionDossier> Pour(string type)
        => Catalogue.Where(a => a.Types.Contains(type));

    /// <summary>L'action de ce code, ou null.</summary>
    public static ActionDossier? DeCode(string code)
        => Catalogue.FirstOrDefault(a => a.Code == code);

    /// <summary>
    /// Les actions courrier d'une affaire, une par modele, a la place de la famille
    /// <see cref="Courriers"/>. Le premier modele prend la lettre de la famille (C) ; les
    /// suivants, la premiere lettre de leur libelle qu'aucune autre action de l'affaire
    /// (<paramref name="prises"/>) ni un modele precedent ne porte, a defaut la premiere libre
    /// de l'alphabet (une affaire montre bien moins de 26 actions).
    /// </summary>
    public static ActionDossier[] DesCourriers(IEnumerable<ModeleCourrier> modeles, IEnumerable<char> prises)
    {
        var famille = DeCode(Courriers)!;
        var lettres = new HashSet<char>(prises);
        return modeles.Select(m =>
        {
            var touche = (famille.Touche + m.Libelle.ToUpperInvariant() + "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
                .First(c => char.IsAsciiLetterUpper(c) && !lettres.Contains(c));
            lettres.Add(touche);
            return new ActionDossier(PrefixeCourrier + m.Code, Ecrire, m.Libelle, touche, null, famille.Types,
                "Ecrit ce courrier pour l'affaire et l'affiche dans la colonne du document");
        }).ToArray();
    }
}
