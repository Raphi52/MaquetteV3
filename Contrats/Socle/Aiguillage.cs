using System.Globalization;

namespace RigV3.Contrats.Socle;

// L'aiguillage par affaire (lot 3 de la phase 2 : SOC-014, SOC-015 ; mode d'emploi :
// docs/reconstruction/socle-aiguillage.md). Le serveur le publie
// (Serveur/Socle/Aiguillage/ModuleAiguillage.cs) ; l'ecran de recherche d'affaires le lit
// (Pont/AncienRig.cs).

/// <summary>
/// L'adresse de l'aiguillage. {code} = code greffe d'une route du CATALOGUE commun : l'agent y est juge
/// dans la base commune, comme pour la fiche du greffe (RoutesGreffes.Fiche) -- la date de passage vit
/// la, et la question ne lit aucune base de greffe. 404 si le greffe n'est pas au catalogue.
/// </summary>
public static class RoutesAiguillage
{
    /// <summary>POST <see cref="QuestionAiguillage"/> -> <see cref="ReponseAiguillage"/> : pour chaque affaire,
    /// le logiciel qui la traite (SOC-014 CA06). Plusieurs affaires d'un coup : une page de resultats de
    /// recherche pose UNE question, pas une par ligne.</summary>
    public const string Aiguiller = "/socle/greffes/{code}/aiguillage";

    /// <summary>Au plus tant d'affaires par question (400 au-dela) : une page de recherche en montre bien moins.</summary>
    public const int AffairesParQuestion = 1000;

    public static string Pour(string route, string code) => route.Replace("{code}", Uri.EscapeDataString(code));
}

/// <summary>Les deux logiciels : la reponse de l'aiguillage ne prend que ces valeurs.</summary>
public static class Logiciels
{
    public const string V2 = "V2";
    public const string V3 = "V3";
}

/// <summary>Une affaire a aiguiller.</summary>
/// <param name="Numero">Le numero montre a l'agent ; rendu tel quel dans la reponse, jamais interprete.</param>
/// <param name="CreeeLe">Sa date de creation, en heure LOCALE du greffe (AFFAIRE.AFFRE_DATE_CREATION de la V2 :
/// l'heure du serveur qui l'a creee, Affaire.cs:126). null = aucune affaire existante n'est visee : une nouvelle
/// affaire, creee maintenant (SOC-014 CA01, CA03 ; arrivee sans affaire, CA05).</param>
/// <remarks>Aucun domaine, aucune nature, aucune entreprise : la regle ne compare que deux dates (CA06, Q18).</remarks>
public sealed record AffaireAAiguiller(string Numero, DateTime? CreeeLe);

/// <summary>La question posee a l'aiguillage.</summary>
public sealed record QuestionAiguillage(IReadOnlyList<AffaireAAiguiller> Affaires);

/// <summary>La reponse pour une affaire.</summary>
/// <param name="Logiciel"><see cref="Logiciels.V2"/> ou <see cref="Logiciels.V3"/>.</param>
/// <param name="Motif">La comparaison faite, en clair (« creee le 30/07/2026 00:00, avant le passage du ... »).</param>
public sealed record AffaireAiguillee(string Numero, string Logiciel, string Motif);

/// <summary>La reponse de l'aiguillage.</summary>
/// <param name="PassageLocal">La date de passage du greffe au catalogue (heure locale) ; null tant qu'elle n'est pas saisie.</param>
/// <param name="MaintenantLocal">L'heure locale du greffe au moment de la question : la date de creation d'une nouvelle
/// affaire ; null si le greffe n'a pas encore de fuseau (son passage ne peut alors pas etre saisi).</param>
public sealed record ReponseAiguillage(
    string Code,
    DateTime? PassageLocal,
    DateTime? MaintenantLocal,
    IReadOnlyList<AffaireAiguillee> Affaires);

/// <summary>
/// LA regle de SOC-014 (CA06) : une affaire se traite dans la V3 si et seulement si elle est creee a
/// l'heure de passage de son greffe ou apres ; sinon, et tant que le passage n'est pas saisi, dans la V2
/// (et pour un greffe beta revenu a l'ancien RIG, toujours dans la V2 : voir plus bas).
/// Aucune regle par domaine (Q18), aucune exception par nature d'affaire.
///
/// Elle vit ici, dans les contrats, parce que deux appelants la jouent : le serveur
/// (ModuleAiguillage, avec la date du catalogue) et la demonstration de l'ecran sans serveur
/// (Pont/AncienRig.cs). Une seule ecriture : ils ne peuvent pas juger differemment.
/// La borne est celle de <see cref="FicheGreffe.PassageFait"/> (heure atteinte = passage fait) :
/// une affaire creee a la minute du passage est deja une affaire de la V3.
///
/// Une seule exception, le RETOUR d'un greffe beta a l'ancien RIG (REC-007 T02) : ses affaires de la V3 y sont
/// recopiees, la V3 refuse toute creation (REC-007 CA03), et la date de passage, atteinte, ne change plus (SOC-007
/// CA05) -- la creation est rouverte dans la V2 par ce seul etat, que le serveur lit dans la base V3 du greffe
/// (rec.EtatRetour, RetourBeta.RevenuDepuisAsync) : toute affaire, ancienne ou nouvelle, se traite alors dans la V2.
/// </summary>
public static class RegleAiguillage
{
    /// <param name="passageLocal">La date de passage du greffe (heure locale) ; null = pas encore saisie.</param>
    /// <param name="creeeLe">La date de creation de l'affaire (heure locale du greffe) ; null = nouvelle affaire.</param>
    /// <param name="maintenantLocal">L'heure locale du greffe maintenant : la date de creation d'une nouvelle affaire.
    /// null seulement pour un greffe sans fuseau, qui n'a donc pas de date de passage (CatalogueGreffes.SaisirPassageAsync).</param>
    /// <param name="revenuV2Depuis">Le greffe beta est revenu (ou revient) a l'ancien RIG depuis cette date (REC-007) ;
    /// null : pas de retour.</param>
    public static AffaireAiguillee Aiguiller(string numero, DateTime? passageLocal, DateTime? creeeLe, DateTime? maintenantLocal,
                                             DateTime? revenuV2Depuis = null)
    {
        var creation = creeeLe ?? maintenantLocal;
        var quoi = creeeLe is { } c ? $"creee le {Date(c)}"
                 : maintenantLocal is { } m ? $"nouvelle affaire ({Date(m)})" : "nouvelle affaire";
        if (revenuV2Depuis is { } retour)
            return new(numero, Logiciels.V2, $"{quoi} ; greffe revenu a l'ancien RIG le {Date(retour)} (retour de beta) : traitee dans la V2");
        if (passageLocal is not { } passage)
            return new(numero, Logiciels.V2, $"{quoi} ; passage du greffe a la V3 pas encore fixe : traitee dans la V2");
        if (creation is null)
            throw new ArgumentException("Nouvelle affaire sans l'heure locale du greffe : la comparer a son passage est impossible.", nameof(maintenantLocal));
        return creation >= passage
            ? new(numero, Logiciels.V3, $"{quoi}, a partir du passage du {Date(passage)} : traitee dans la V3")
            : new(numero, Logiciels.V2, $"{quoi}, avant le passage du {Date(passage)} : traitee dans la V2");
    }

    private static string Date(DateTime d) => d.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}
