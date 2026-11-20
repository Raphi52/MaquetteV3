using System.Globalization;

namespace RigV3Desktop;

// ============================================================
// LES COURRIERS DIVERS : la liste UNIQUE des modeles (v2 : table COURRIER_JUDICIAIRE,
// colonnes COUJU_*), et le courrier qu'un modele produit pour une affaire.
//
// Deux lecteurs, une seule liste (decision du chef, plan « Vue du dossier = coeur des
// actions », lot 9, 20/11/2026) :
//   - l'ecran du CATALOGUE (PROC_COURRIER_DIVERS, ouvert par l'accueil) la lit par son
//     pont (Pont/EcranCourriersDivers.cs) : il la gardait jusque-la dans son .tsx ;
//   - la page du dossier propose les modeles qui valent pour la nature de l'affaire, et
//     chacun y PRODUIT le courrier rempli avec elle (Pont/PlanTravailActions.cs). Elle
//     ouvrait le catalogue lui-meme, un ecran de parametrage : constate par le chef le
//     20/11/2026 depuis 004182.
//
// Ce sont des courriers JUDICIAIRES : leur type d'entite (eTypeEntiteJudiciaire de v2)
// designe une instance, une procedure collective, une prevention ou une mesure. Le code
// ne connait AUCUNE lettre type pour un dossier RCS (cherche le 20/11/2026 : n'existent
// que des pieces deja envoyees -- « Lettre de regularisation du depot », Metier/ArbreGed.cs --,
// la lettre d'accompagnement d'une commande web, et des maquettes d'edition liees a un
// processus, web/src/ecrans/EcranMaquettesEdition.tsx) : un dossier RCS ne se voit donc
// proposer aucun courrier, plutot qu'une entree vide.
// ============================================================

/// <summary>A qui s'adresse un courrier divers.</summary>
public enum DestinataireCourrier
{
    /// <summary>A personne (COUJU_WITH_DEST faux) : une note d'information.</summary>
    Aucun,
    /// <summary>A la partie de l'affaire, a son adresse.</summary>
    Partie,
    /// <summary>Au mandataire judiciaire de la procedure.</summary>
    Mandataire
}

/// <summary>Un modele de courrier divers (v2 : COURRIER_JUDICIAIRE).</summary>
/// <param name="Code">COUJU_CODE.</param>
/// <param name="Libelle">COUJU_LIBELLE : ce que la barre du dossier propose, et l'objet du courrier.</param>
/// <param name="TypeEntite">COUJU_TYPE_ENTITE : INSTANCE, INSTANCE_PREVENTION, PROCEDURE_COLLECTIVE, PREVENTION, MESURE.</param>
/// <param name="ParamEdition">COUJU_CODE_PRMED.</param>
/// <param name="Destinataire"><see cref="DestinataireCourrier.Aucun"/> = COUJU_WITH_DEST faux.</param>
/// <param name="Actif">COUJU_ACTIF : un modele inactif n'est jamais propose sur une affaire.</param>
/// <param name="Corps">Le texte entre la formule d'appel et la signature ; « {denomination} » y
/// devient le nom de l'affaire.</param>
public sealed record ModeleCourrier(string Code, string Libelle, string TypeEntite, string ParamEdition,
                                    DestinataireCourrier Destinataire, bool Actif, string Corps)
{
    public bool AvecDestinataire => Destinataire != DestinataireCourrier.Aucun;
}

/// <summary>L'affaire pour qui on ecrit : ce que le courrier en reprend.</summary>
/// <param name="Adresse">Ses lignes separees par « \n » ; vide si le dossier ne la connait pas.</param>
public sealed record AffaireCourrier(string Numero, string Denomination, string Adresse);

/// <summary>Le destinataire d'un courrier : son nom, puis ses lignes d'adresse (« \n »).</summary>
public sealed record AdresseCourrier(string Nom, string Adresse);

public static class CourriersDivers
{
    /// <summary>Les types d'entite judiciaire (eTypeEntiteJudiciaire de v2), dans l'ordre du filtre du catalogue.</summary>
    public static readonly string[] TypesEntite =
        ["INSTANCE", "INSTANCE_PREVENTION", "PROCEDURE_COLLECTIVE", "PREVENTION", "MESURE"];

    public static readonly ModeleCourrier[] Modeles =
    [
        new("CD_CONV", "Convocation simple", "INSTANCE", "MAQ_CONV_SIMPLE", DestinataireCourrier.Partie, true,
            "Vous etes prie(e) de vous presenter au greffe du tribunal de commerce, muni(e) des pieces de "
            + "l'affaire citee en reference, afin qu'il soit procede a son examen."),
        new("CD_RELAN", "Relance de piece manquante", "INSTANCE", "MAQ_RELANCE", DestinataireCourrier.Partie, true,
            "Sauf erreur de notre part, le dossier de l'affaire citee en reference reste incomplet. Nous vous "
            + "remercions de nous faire parvenir sans delai les pieces manquantes."),
        new("CD_NOTIF", "Notification au mandataire", "PROCEDURE_COLLECTIVE", "MAQ_NOTIF_MAND", DestinataireCourrier.Mandataire, true,
            "Nous avons l'honneur de vous notifier, en votre qualite de mandataire judiciaire, la procedure "
            + "ouverte a l'egard de {denomination}. Les pieces de l'affaire citee en reference sont tenues a "
            + "votre disposition au greffe."),
        new("CD_INFO", "Information sans destinataire", "PREVENTION", "MAQ_INFO", DestinataireCourrier.Aucun, true,
            "Pour information, l'affaire citee en reference, concernant {denomination}, fait l'objet d'un suivi "
            + "par le greffe. Ce courrier n'appelle pas de reponse."),
        new("CD_MESURE", "Courrier de suivi de mesure", "MESURE", "MAQ_SUIVI_MES", DestinataireCourrier.Partie, false,
            "Nous vous informons du suivi de la mesure ordonnee dans l'affaire citee en reference, concernant "
            + "{denomination}."),
    ];

    /// <summary>
    /// Les types d'entite que porte une affaire judiciaire de cette nature (les natures de la
    /// recherche d'affaires, Pont/EcranRechAffaires.cs). Une affaire de prevention porte aussi
    /// les modeles INSTANCE_PREVENTION : l'instance ouverte dans une prevention. Nature inconnue
    /// ou nulle : aucun.
    /// </summary>
    public static string[] TypesDeLaNature(string? nature) => nature switch
    {
        "instance" => ["INSTANCE"],
        "collective" => ["PROCEDURE_COLLECTIVE"],
        "prevention" => ["PREVENTION", "INSTANCE_PREVENTION"],
        "mesure" => ["MESURE"],
        _ => []
    };

    /// <summary>Les modeles ACTIFS qui valent pour une affaire de cette nature, dans l'ordre du catalogue.</summary>
    public static IEnumerable<ModeleCourrier> Pour(string? nature)
    {
        var types = TypesDeLaNature(nature);
        return Modeles.Where(m => m.Actif && types.Contains(m.TypeEntite));
    }

    /// <summary>Le modele de ce code, ou null.</summary>
    public static ModeleCourrier? DeCode(string code) => Modeles.FirstOrDefault(m => m.Code == code);

    /// <summary>Ce qui tient lieu d'une adresse ou d'un destinataire inconnus : ils se disent, ils ne s'inventent pas.</summary>
    public const string AdresseInconnue = "(adresse non renseignee au dossier)";
    public const string DestinataireInconnu = "(destinataire non renseigne au dossier)";

    /// <summary>L'origine d'un courrier produit, comme celle des pieces du classeur.</summary>
    public const string Origine = "Courrier divers";

    /// <summary>Le titre du document produit : le modele, puis l'affaire.</summary>
    public static string Titre(ModeleCourrier modele, string numero) => $"{modele.Libelle} - {numero}";

    /// <summary>
    /// Le texte du courrier : l'en-tete du greffe, le destinataire (aucun pour un modele qui
    /// n'en demande pas), le lieu et la date, l'affaire (n°, denomination, adresse), l'objet,
    /// le corps du modele, la signature. Une ligne par ligne de la page du document.
    /// </summary>
    /// <param name="destinataire">Celui que le modele demande ; null s'il est inconnu au dossier.</param>
    public static string Rediger(ModeleCourrier modele, AffaireCourrier affaire, AdresseCourrier? destinataire,
                                 Greffe greffe, DateTime date)
    {
        var lignes = new List<string> { greffe.Libelle };
        if (greffe.Adresse.Length > 0) lignes.Add(greffe.Adresse);
        lignes.Add("");
        if (modele.AvecDestinataire)
        {
            if (destinataire is null) lignes.Add(DestinataireInconnu);
            else
            {
                lignes.Add(destinataire.Nom);
                lignes.Add(destinataire.Adresse.Length > 0 ? destinataire.Adresse : AdresseInconnue);
            }
            lignes.Add("");
        }
        var ville = CultureInfo.GetCultureInfo("fr-FR").TextInfo.ToTitleCase(greffe.Ville.ToLowerInvariant());
        lignes.Add($"{ville}, le {date:dd/MM/yyyy}");
        lignes.Add("");
        lignes.Add($"Affaire n° {affaire.Numero} : {affaire.Denomination}");
        lignes.Add(affaire.Adresse.Length > 0 ? affaire.Adresse.Replace("\n", ", ") : AdresseInconnue);
        lignes.Add($"Objet : {modele.Libelle}");
        lignes.Add("");
        lignes.Add("Madame, Monsieur,");
        lignes.Add(modele.Corps.Replace("{denomination}", affaire.Denomination));
        lignes.Add("");
        lignes.Add("Le greffier");
        return string.Join("\n", lignes);
    }
}
