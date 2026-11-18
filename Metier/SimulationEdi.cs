namespace RigV3Desktop;

/// <summary>
/// Un message EDI recu par le greffe, tel qu il arrive : un fichier, un
/// emetteur, et des lignes au format du flux. Il porte le code evenement
/// (DENO) et les valeurs demandees -- c est la matiere PREMIERE de la chaine.
/// </summary>
public sealed record MessageEdi(string Fichier, string Emetteur, DateTime Recu,
                                string Siren, string Societe, string Dossier,
                                string CodeEvenement, string[] Lignes)
{
    public string Horodatage => Recu.ToString("dd/MM/yyyy HH:mm");
}

/// <summary>
/// LE CAS DU JOUR : un flux EDI arrive ce matin, deja rapproche d un dossier,
/// dont l evenement attend d etre traite. Ce n est PAS un mode demonstration
/// qu on allume : c est une ligne de plus dans le jeu du greffe, au meme titre
/// que les autres arrivees et les autres demandes.
///
/// Pourquoi il vit ici et pas dans JeuDemo : les trois artefacts qu il produit
/// (une arrivee, une demande, un evenement) doivent rester RELIES entre eux --
/// meme dossier, meme horodatage, meme reference de fichier. Eparpilles dans
/// trois tableaux, le lien se perdait au premier changement de l un d eux.
///
/// La mise en situation se joue donc dans les ECRANS REELS, sans vue dediee :
///   Courrier du jour   -> la ligne EDI, mode Edi, rapprochee de la demande
///   Demandes recues    -> la demande de modification, en saisie
///   Accueil / Alertes  -> la tache a faire, signalee du jour
///   Fiche du dossier   -> les seuls champs que la recette declare
/// </summary>
public static class SimulationEdi
{
    /// <summary>Le dossier vise : une affaire qui n est PAS deja dans la file,
    /// sinon le cas se confondrait avec un dossier existant.</summary>
    public const string Dossier = "004188";

    /// <summary>Le flux, tel qu il est arrive ce matin.</summary>
    public static readonly MessageEdi Message = new(
        "EDI-2026-44108.xml",
        "Guichet unique INPI",
        JeuDemo.Aujourdhui.AddHours(9).AddMinutes(12),
        "901 553 226",
        "NORD MARINE SAS",
        Dossier,
        "DENO",
        [
            "ENT+FORMALITE+MODIF",
            "SIR+901553226",
            "EVT+DENO+Modification de la denomination",
            "ANC+NORD MARINE SAS",
            "NOU+NORMARINE ATLANTIQUE",
            "DOM+normarine-atlantique.fr",
            "PJ +PV assemblee generale du 02/09/2026",
        ]);

    /// <summary>La valeur demandee par le flux, telle qu elle sera saisie.</summary>
    public const string NouvelleDenomination = "NORMARINE ATLANTIQUE";
    public const string NouveauNomDomaine = "normarine-atlantique.fr";

    /// <summary>La ligne du COURRIER DU JOUR : le flux entre par la, mode EDI,
    /// deja rapproche de la demande qu il a ouverte.</summary>
    public static Arrivee Arrivee => new(
        "A-2026-0913", Message.Recu, "EDI", Message.Societe, Message.Fichier,
        string.Empty, false, Dossier, "Modification", "R. VILAIN", true, false);

    /// <summary>La DEMANDE ouverte par le flux, dans la file comme les autres.</summary>
    public static DemandeRcs Demande => new(
        Dossier, "Modification - " + Message.Societe, Dossier, Message.Siren,
        "NORD MARINE", "PROC_DEMANDE", "En saisie", true,
        Message.Recu, string.Empty, "R. VILAIN", true, "action");

    /// <summary>L EVENEMENT RCS signale : c est LUI qui fait l alerte.</summary>
    public static EvenementAttendu Evenement => new(Message.CodeEvenement, Dossier, Message.Recu);

    /// <summary>
    /// La valeur que le FLUX propose pour un champ de la fiche, ou rien s il
    /// ne dit rien dessus. Le fichier porte deja la denomination et le nom de
    /// domaine : les faire retaper serait demander de recopier ce que la
    /// machine a recu -- et de le recopier faux. La correspondance est ecrite
    /// ici, en TABLE.COLONNE, pour qu aucun ecran ne porte de valeur en dur.
    /// </summary>
    public static string? ValeurProposee(string table, string colonne)
        => (table, colonne) switch
        {
            ("INSCRIT_PM", "DENOMINATION_USAGE") => NouvelleDenomination,
            ("ETABLISSEMENT", "NOM_DOMAINE") => NouveauNomDomaine,
            _ => null
        };
}
