using System.Globalization;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// L'entree de l'ecran « Recherche d'affaires » (web/src/ecrans/EcranRechAffaires.tsx). Plan « RIG v3 100 % fonctionnel », lot 6
/// (26/11/2026), sur la marche du lot 17 (Pont/LectureEcranMain.cs).
///
/// Branche au serveur, la base cherche les affaires (Serveur/Socle/EcransMain/LectureAffaires.cs) par les criteres qu'elle porte :
/// numero (d'affaire, de demande ou de facture), libelle ou SIREN, et ceux de l'instance rattachee (id, role, code affaire, nom,
/// saisine). Le reste -- type, nature, etat, solde minimal -- et le tri restent ceux de ce pont, en memoire, sur les affaires
/// lues. Sans aucun critere, RIG v2 ne cherche rien : aucune ligne. Ce que la base ne porte pas, nomme : l'etat d'une affaire
/// (AFFAIRE n'en a pas : le critere « Etat » ne trouve rien en base), la nature hors instance (« instance » si l'affaire a une
/// instance, sinon vide). Le type est « JUD » avec une instance, « RCS » si une demande a un dossier RCS, sinon vide. SANS
/// serveur : la demonstration (Trouver), marquee « exemple ». En panne : le motif et aucune ligne.
/// </summary>
public static partial class EcranRechAffaires
{
    /// <summary>La note d'une liste lue en base : ce qu'elle montre au juste.</summary>
    public const string NoteBase = "Base du greffe : les affaires trouvées par leur numéro (ou celui d'une demande, d'une facture), "
                                 + "leur libellé ou SIREN, ou leur instance. Sans critère, RIG v2 ne cherche rien.";

    [JSInvokable("EcranRechAffaires.Trouver")]
    public static Task<VueAffaires> TrouverBase(CriteresAffaires criteres, string? cleTri, bool ascendant, string? selection)
        => TrouverAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, criteres, cleTri, ascendant, selection);

    /// <summary>Le nombre d'affaires de chaque vue (une vue est un jeu de criteres), sur le meme fonds que la grille.</summary>
    [JSInvokable("EcranRechAffaires.Compter")]
    public static Task<int?[]> CompterBase(CriteresAffaires[] vues) => CompterAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, vues);

    /// <summary>La vue du greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<VueAffaires> TrouverAsync(ClientServeur? serveur, string? greffe, CriteresAffaires c, string? cleTri,
                                                        bool ascendant, string? selection)
    {
        var lu = await LireAsync(serveur, greffe, c);
        if (lu.Mode == LectureEcransMain.Demo) return Trouver(c, cleTri, ascendant, selection);
        if (lu.Valeur is not { } v)
            return new VueAffaires(Natures, Etats, [], "", AucunCritere(c), SirenCherche(c), null, lu.Mode, lu.Message);
        return Construire([.. v.Lignes.Select(DeLaBase)], c, EnMemoire(c), cleTri, ascendant, selection) with { Mode = lu.Mode, Message = NoteBase };
    }

    /// <summary>Le compte de chaque vue : la demonstration sans serveur ; en base, une lecture par vue. Null -- pas de compte, la
    /// puce n'en montre aucun -- pour une vue sans critere que la base cherche (RIG v2 ne cherche rien sans critere : un « 0 »
    /// a cote des resultats affiches tromperait) ou dont la lecture echoue.</summary>
    public static async Task<int?[]> CompterAsync(ClientServeur? serveur, string? greffe, CriteresAffaires[] vues)
    {
        if (serveur is null) return [.. Compter(vues).Select(n => (int?)n)];
        var comptes = new int?[vues.Length];
        for (var i = 0; i < vues.Length; i++)
        {
            if (!CritereCherche(vues[i])) continue;
            var lu = await LireAsync(serveur, greffe, vues[i]);
            comptes[i] = lu.Valeur is { } v ? Trouvees([.. v.Lignes.Select(DeLaBase)], EnMemoire(vues[i]), null, true).Length : null;
        }
        return comptes;
    }

    private static Task<LectureEcranMain<AffairesLues>> LireAsync(ClientServeur? serveur, string? greffe, CriteresAffaires c)
    {
        var recherche = new RechercheAffaires(c.CritNumero ?? "", c.CritLibelle ?? "", c.CritIdInstance ?? "", c.CritRole ?? "",
            c.CritCodeAffaire ?? "", c.CritNom ?? "", c.CritSaisineDu?.ToDateTime(TimeOnly.MinValue), c.CritSaisineAu?.ToDateTime(TimeOnly.MinValue),
            200);
        return LectureEcransMain.LireAsync(serveur, greffe, "les affaires", (s, g) =>
            s.EnvoyerAsync<RechercheAffaires, AffairesLues>(RoutesEcransMain.Pour(RoutesAffaires.Affaires, g), recherche));
    }

    /// <summary>Vrai si <paramref name="c"/> porte un critere que la base cherche (LectureAffaires) ; sinon RIG v2 ne cherche rien.</summary>
    private static bool CritereCherche(CriteresAffaires c)
        => new[] { c.CritNumero, c.CritLibelle, c.CritIdInstance, c.CritRole, c.CritCodeAffaire, c.CritNom }.Any(t => !string.IsNullOrWhiteSpace(t))
           || c.CritSaisineDu is not null || c.CritSaisineAu is not null;

    /// <summary>Les criteres que la base a deja appliques sont retires : en memoire restent type, nature, etat et solde.</summary>
    private static CriteresAffaires EnMemoire(CriteresAffaires c) => c with
    {
        CritNumero = "", CritLibelle = "", CritIdInstance = "", CritRole = "", CritCodeAffaire = "", CritNom = "",
        CritSaisineDu = null, CritSaisineAu = null,
    };

    private static Affaire DeLaBase(AffaireLue a)
    {
        var siren = a.Siren.Length == 9 ? $"{a.Siren[..3]} {a.Siren[3..6]} {a.Siren[6..]}" : a.Siren;
        var type = a.Role.Length > 0 ? "JUD" : a.AvecDossierRcs ? "RCS" : "";
        return new Affaire(a.Id.ToString(CultureInfo.InvariantCulture), a.Libelle, siren, type, a.IdInstance.Length > 0 ? "instance" : "",
            a.Creation ?? DateTime.MinValue, a.Solde, a.IdInstance, a.Role, a.CodeAffaire, a.Nom, Etat: "", a.Saisine ?? DateTime.MinValue,
            a.Demandes, a.Factures);
    }
}
