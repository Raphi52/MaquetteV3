namespace RigV3.Contrats.EcransMain;

// « Dépôts judiciaires » (PROC_DEPOTJUD, web/src/ecrans/EcranDepotsJud.tsx), ecran fait main lu dans la base du greffe (plan
// « RIG v3 100 % fonctionnel », lot 5, 26/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureDepotsJud.cs.

/// <summary>L'adresse de la lecture : GET, les instances ou deposer (<see cref="InstancesDepotLues"/>).</summary>
public static class RoutesDepotsJud
{
    public const string Instances = "/ecrans-main/{greffe}/depots-jud";

    /// <summary>GET, les codes d'evenement avec depot proposes pour l'instance de role {role} (<see cref="CodesDepotLus"/>) ; lot 8 du plan
    /// « parcours complet » (28/11/2026), Serveur/Socle/EcransMain/ModuleDepotsJud.cs.</summary>
    public const string Codes = "/ecrans-main/{greffe}/depots-jud/{role}/codes";

    /// <summary>POST, le geste « nouvel evenement survenu » et son depot (GesteEcran : Ids = [le role de l'instance], valeurs
    /// <see cref="DepotEvenement"/>) ; ecrit par Serveur/Socle/EcransMain/EcritureDepotsJud.cs.</summary>
    public const string Deposer = "/ecrans-main/{greffe}/depots-jud/deposer";

    /// <summary>L'adresse des codes de l'instance <paramref name="role"/> du greffe <paramref name="greffe"/>.</summary>
    public static string CodesPour(string greffe, string role) =>
        RoutesEcransMain.Pour(Codes, greffe).Replace("{role}", Uri.EscapeDataString(role));
}

/// <summary>
/// Une instance de la recherche de PROC_DEPOTJUD : son numero de role, son nom courant, sa nature d'affaire, sa date
/// d'enrolement, son dernier evenement survenu (libelle et date), la demande qui la tient (vide si libre) et si la
/// facturation s'affiche (aucun client sur ses demandes).
/// </summary>
public sealed record InstanceDepotLue(string Numero, string Denomination, string Nature, DateTime? Enrolement, string DernierEvenement,
                                      DateTime? DateEvenement, string DemandeEnCours, bool AFacturer);

/// <summary>Les instances lues (au plus le plafond) et leur total.</summary>
public sealed record InstancesDepotLues(InstanceDepotLue[] Lignes, int Total);

/// <summary>Un code d'evenement de la liste de l'etape « Evénement survenu » : son code et son libelle (CODE_EVE_PRO).</summary>
public sealed record CodeDepotLu(string Code, string Libelle);

/// <summary>Les codes proposes pour l'instance <paramref name="Role"/> (vide : aucune instance, aucun code) ; <paramref name="Type"/> : son
/// type traduit (OP, CX, REF...), celui de la colonne R_CODEEVEPRO_COMPATIBLE_* que la liste filtre, vide sans instance JROP de ce role.</summary>
public sealed record CodesDepotLus(string Role, CodeDepotLu[] Codes, string Type = "");

/// <summary>
/// Les champs du geste « nouvel evenement survenu » de PROC_DEPOTJUD, sous leurs titres de RIG v2 : l'etape « Evénement survenu »
/// (PROC_DEPOTJUD.Designer.cs:71), ses champs « Code évènement de procédure » et « Date » (OPE_EVENEMENT.Designer.cs:362, :169).
/// La date est aaaa-mm-jj.
/// </summary>
public static class DepotEvenement
{
    public const string Section = "Evénement survenu";
    public const string Code = "Code évènement de procédure";
    public const string Date = "Date";
}
