namespace RigV3.Contrats.Recopie;

// Les echanges de l'ecran « Fiches en conflit » (REC-004, lot 9 de la phase 2 du plan de reconstruction, 2026-11-21) :
// une fiche partagee modifiee dans la V2 ET dans la V3 entre deux passages de la recopie. La V3 a gagne dans les deux
// bases ; l'ecart garde ce que la V2 avait ecrit et que la V3 a ecrase, pour que l'agent designe le ressaisisse s'il le
// faut. Le serveur les publie (Serveur/Recopie/ModuleConflits.cs), l'ecran les lit (Pont/EcranConflits.cs).
// Mode d'emploi : docs/reconstruction/recopie.md, section « La recopie continue ».
// fix-ok: refus du chef (21/11) constate sur la capture de l'ecran : la liste montrait CLIENT et CLIEN_EMAIL, des noms de
// colonnes, la ou l'agent lit « Client » et « E-mail » sur la fiche de la V2 -- d'ou LibelleTable, Libelle et TableEcarts.

/// <summary>Les adresses des ecarts de recopie. Toutes portent le greffe (les ecarts vivent dans sa base V3) et sont
/// reservees a l'agent designe : la fonction <see cref="FonctionSuivi"/> (403 sinon, et pas d'alerte).</summary>
public static class RoutesConflits
{
    /// <summary>GET ?traites=false|true&amp;table=CLIENT -> <see cref="VueEcarts"/> (non traites par defaut, le plus recent d'abord).</summary>
    public const string Ecarts = "/recopie/{greffe}/ecarts";

    /// <summary>GET -> <see cref="AlerteEcarts"/> : ce que l'accueil de l'agent designe affiche (REC-004 CA02).</summary>
    public const string Alerte = "/recopie/{greffe}/ecarts/alerte";

    /// <summary>POST (corps vide) -> <see cref="EcartRecopie"/> : l'ecart marque traite par l'appelant (REC-004 CA03).</summary>
    public const string Traiter = "/recopie/{greffe}/ecarts/{id}/traite";

    /// <summary>La fonction de la V3 qui designe l'agent qui suit les ecarts (REC-004 T03) ; le greffe la donne par la
    /// gestion des droits, le role qui gere les droits l'a d'office (migration REC-002_recopie.sql).</summary>
    public const string FonctionSuivi = "RECOPIE_ECARTS";

    /// <summary>Un ecart non traite depuis autant de jours ouvres passe en alerte rouge (REC-004 CA03, seuil propose).</summary>
    public const int JoursOuvresAvantRouge = 5;

    public static string Pour(string route, string greffe) => route.Replace("{greffe}", Uri.EscapeDataString(greffe));

    public static string PourEcart(string greffe, long id) => Pour(Traiter, greffe).Replace("{id}", id.ToString());
}

/// <summary>Un champ de la fiche que la V2 avait change et que la V3 a ecrase.</summary>
/// <param name="Champ">La colonne de la V2 (« CLIEN_EMAIL ») : une adresse, que l'ecran ne montre qu'en infobulle.</param>
/// <param name="Libelle">Ce que l'agent lit : le titre du champ sur la fiche de la V2 (« E-mail » sur la fiche client),
/// <see cref="LibellesRecopie.Champ"/>.</param>
/// <param name="ValeurV2">Ce que la V2 avait ecrit (perdu), en texte lisible ; null = vide.</param>
/// <param name="ValeurV3">Ce que la V3 a garde dans les deux bases ; null = vide.</param>
public sealed record ChampEcart(string Champ, string Libelle, string? ValeurV2, string? ValeurV3);

/// <summary>Une fiche modifiee des deux cotes entre deux passages de la recopie (REC-004 CA01).</summary>
/// <param name="Fiche">Ce que l'agent lit : « client 3001 — DUPONT ».</param>
/// <param name="Cible">Ou mene le clic : « dossier:2026B00012 » (l'affaire), « ecran:PROC_FCLIENT » (un ecran), ou null.</param>
/// <param name="DateV2">La date de mise a jour de la fiche dans la V2 quand la table en a une, sinon le moment ou la
/// recopie a vu le changement.</param>
/// <param name="AuteurV2">L'auteur du changement de la V2 quand elle le note ; null sinon (la V2 ne garde pas qui
/// modifie la plupart de ses fiches).</param>
/// <param name="EnRouge">Non traite depuis <see cref="RoutesConflits.JoursOuvresAvantRouge"/> jours ouvres ou plus (CA03).</param>
/// <param name="LibelleTable">La sorte de fiche pour l'agent (« Client », « Dossier RCS ») : <see cref="LibellesRecopie.Table"/>.</param>
public sealed record EcartRecopie(
    long Id,
    string TableV2,
    string LibelleTable,
    string IdV2,
    string IdV3,
    string Fiche,
    string? Cible,
    DateTime DetecteLe,
    DateTime DateV2,
    string? AuteurV2,
    DateTime DateV3,
    string AuteurV3,
    ChampEcart[] Champs,
    DateTime? TraiteLe,
    string? TraitePar,
    bool EnRouge);

/// <param name="Tables">Les tables qui ont des ecarts, pour le filtre de l'ecran.</param>
public sealed record VueEcarts(EcartRecopie[] Ecarts, TableEcarts[] Tables);

/// <summary>Une table du filtre de l'ecran : son code (la valeur envoyee au serveur) et ce que l'agent lit.</summary>
public sealed record TableEcarts(string Code, string Libelle);

/// <summary>L'alerte « fiche modifiee des deux cotes » de l'accueil (REC-004 CA02, CA03).</summary>
/// <param name="NonTraites">Le nombre d'ecarts non traites.</param>
/// <param name="EnRouge">Parmi eux, ceux qui attendent depuis 5 jours ouvres ou plus.</param>
public sealed record AlerteEcarts(int NonTraites, int EnRouge, DateTime? PlusAncien);
