namespace RigV3.Contrats.EcransGeneres;

// Les lignes d'un ECRAN GENERE lues dans la base du greffe (plan « ecrans retrouves branches sur la base »,
// lot 1, 24/11/2026). L'ecran est decrit par un descripteur (Pont/EcransGeneres/) ; ses lignes viennent du
// lecteur de SA procedure, cote serveur (Serveur/Socle/EcransGeneres/, un fichier par procedure : mode
// d'emploi en tete de LecteurEcran.cs). L'ecran les recoit par Pont/EcranGenere.cs.

/// <summary>
/// L'adresse des lignes d'un ecran genere. {greffe} = le code du greffe ouvert : l'agent y est juge dans la base V3
/// de CE greffe, qui est aussi la base lue (401 sans session, 403 compte refuse, 404 greffe inconnu, 503 base muette,
/// sans repli). {procedure} = le code de la procedure du menu (« PROC_DCA »). Lecture seule.
/// </summary>
public static class RoutesEcransGeneres
{
    /// <summary>GET : les lignes de la procedure (<see cref="LignesEcran"/>).</summary>
    public const string Lignes = "/ecrans-generes/{greffe}/{procedure}/lignes";

    /// <summary>L'adresse a appeler pour le greffe <paramref name="greffe"/> et la procedure <paramref name="procedure"/>.</summary>
    public static string Pour(string greffe, string procedure)
        => Lignes.Replace("{greffe}", Uri.EscapeDataString(greffe)).Replace("{procedure}", Uri.EscapeDataString(procedure));
}

/// <summary>Une ligne de l'ecran, telle que la mise en page l'affiche (les noms de LigneEcran du descripteur).</summary>
/// <param name="Nom">Ce qui la nomme : la denomination et le SIREN ou le numero de gestion.</param>
/// <param name="Precisions">Ce qui la distingue (exercice, dates, origine...).</param>
/// <param name="Groupe">Un des groupes du descripteur de la procedure, au mot pres.</param>
/// <param name="Etat">Un des etats du descripteur (AFaire, EnCours, Validee, EnAttente, Rejetee...).</param>
/// <param name="Faites">Les rangs des marches deja franchies (a partir de 0), selon la regle de l'etat.</param>
public sealed record LigneLue(string Nom, string Precisions, string Groupe, string Etat, int[] Faites);

/// <summary>La reponse de <see cref="RoutesEcransGeneres.Lignes"/>.</summary>
/// <param name="Procedure">Le code demande.</param>
/// <param name="Branchee">Faux : aucun lecteur pour cette procedure (l'ecran garde ses lignes d'exemple).</param>
/// <param name="Lignes">Au plus <see cref="Plafond"/> lignes, dans l'ordre d'affichage.</param>
/// <param name="Total">Le nombre de lignes de la base qui repondent aux filtres (au-dela du plafond compris).</param>
/// <param name="Plafond">Le nombre maximal de lignes rendues.</param>
public sealed record LignesEcran(string Procedure, bool Branchee, LigneLue[] Lignes, int Total, int Plafond);
