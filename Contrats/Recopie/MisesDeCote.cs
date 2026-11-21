namespace RigV3.Contrats.Recopie;

// Les changements MIS DE COTE par la recopie continue d'un greffe (REC-005 CA02, CA03 sur la file de REC-002/REC-003,
// lot 9 de la phase 2, 2026-11-21) : un changement que la base d'en face refuse a chaque essai (contrainte, declencheur)
// est mis de cote au 3e ; les autres fiches continuent, ceux de la meme fiche attendent derriere lui. L'exploitation
// d'Amitel les lit, corrige la cause et RELANCE chacun : il repasse au passage suivant, puis ceux qui attendaient, dans
// l'ordre. Le serveur : Serveur/Recopie/ModuleConflits.cs et ServiceRecopie.cs. Fiche d'incident :
// docs/reconstruction/recopie.md, section « Un changement refuse : mis de cote, relance ».

/// <summary>Les adresses des changements mis de cote d'un greffe, reservees a l'exploitation d'Amitel (la marque
/// soc.Compte.EstAmitel de la base commune, PolitiquesSupervision.ExploitationAmitel : 403 sinon).</summary>
public static class RoutesMisesDeCote
{
    /// <summary>GET -> <see cref="ChangementMisDeCote"/>[] : ceux qui attendent une relance, le plus ancien d'abord.</summary>
    public const string Liste = "/recopie/{greffe}/mises-de-cote";

    /// <summary>POST <see cref="DemandeRelanceRecopie"/> -> <see cref="ChangementMisDeCote"/> relance (404 s'il n'est pas mis de cote).</summary>
    public const string Relancer = "/recopie/{greffe}/mises-de-cote/relancer";

    public const string SensV3VersV2 = "V3 vers V2";
    public const string SensV2VersV3 = "V2 vers V3";

    public static string Pour(string route, string greffe) => route.Replace("{greffe}", Uri.EscapeDataString(greffe));
}

/// <summary>Un changement mis de cote.</summary>
/// <param name="Sens"><see cref="RoutesMisesDeCote.SensV3VersV2"/> (la V2 l'a refuse) ou <see cref="RoutesMisesDeCote.SensV2VersV3"/> (la V3 l'a refuse).</param>
/// <param name="Ligne">V3 vers V2 : la ligne de rec.FileRecopie ; V2 vers V3 : null (la fiche est rejouee de son etat courant).</param>
/// <param name="TableV2">La table de la fiche (« CLIENT »).</param>
/// <param name="Fiche">L'identifiant de la fiche dans la base qui l'a ecrite (V3 vers V2 : la V3 ; V2 vers V3 : la V2).</param>
/// <param name="Erreur">Le message de la base qui a refuse, au dernier essai.</param>
/// <param name="Derriere">Les changements suivants de la meme fiche qui attendent derriere lui.</param>
public sealed record ChangementMisDeCote(
    string Sens,
    long? Ligne,
    string TableV2,
    string Fiche,
    int Essais,
    string? Erreur,
    DateTime? MiseDeCoteLe,
    int Derriere,
    DateTime? RelanceLe,
    string? RelancePar);

/// <summary>Relancer un changement mis de cote, une fois la cause corrigee.</summary>
/// <param name="Ligne">V3 vers V2 : la ligne de rec.FileRecopie.</param>
/// <param name="TableV2">V2 vers V3 : la table de la fiche.</param>
/// <param name="IdV2">V2 vers V3 : la cle de la fiche dans la V2.</param>
public sealed record DemandeRelanceRecopie(string Sens, long? Ligne, string? TableV2, string? IdV2);
