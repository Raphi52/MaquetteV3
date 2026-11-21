namespace RigV3.Contrats.Socle;

/// <summary>Les adresses des droits (SOC-002) et de leur journal (SOC-005). Chacune porte le code du greffe,
/// <c>{greffe}</c> : les droits sont lus dans la base V3 de CE greffe (lot 9) ; 404 greffe inconnu, 503 base muette.</summary>
public static class RoutesDroits
{
    /// <summary>GET ?fonction=CODE : l'appelant a-t-il cette fonction ? -> <see cref="VerificationDroit"/>.
    /// Ouverte a tout agent admis (chacun verifie SES droits).</summary>
    public const string Verifier = "/socle/{greffe}/droits/verifier";

    /// <summary>GET ?compte=&amp;role=&amp;du=&amp;au= (dates yyyy-MM-dd, bornes comprises) -> <see cref="LigneJournalDroits"/>[],
    /// de la plus recente a la plus ancienne. Reserve a qui gere les droits (SOC-005 CA05).</summary>
    public const string Journal = "/socle/{greffe}/droits/journal";

    /// <summary>L'adresse d'une de ces routes pour le greffe <paramref name="greffe"/> (son code au catalogue).</summary>
    public static string Pour(string route, string greffe) => route.Replace("{greffe}", Uri.EscapeDataString(greffe));

    /// <summary>Le role qui donne le droit de gerer les comptes et les droits : celui de la V2
    /// (Utilisateur.IsHabilit, Utilisateur.cs:364-366).</summary>
    public const string RoleGestionDesDroits = "GestionDesHabilitations";
}

/// <summary>Les valeurs de <see cref="VerificationDroit.Origine"/>.</summary>
public static class OriginesDroit
{
    /// <summary>Par un role du compte (premiere origine de la V2).</summary>
    public const string Role = "role";

    /// <summary>Par l'ancien systeme de droits de la V2, degre superieur a 2.</summary>
    public const string Ancien = "ancien";

    /// <summary>Par un menu de comptabilite (Expresso) de la V2, eventuellement sous un ancien nom.</summary>
    public const string Comptabilite = "comptabilite";

    /// <summary>Refuse : le compte n'a la fonction par aucune origine.</summary>
    public const string Aucune = "aucune";

    /// <summary>Refuse : la fonction n'existe pas dans le catalogue ; rien n'y est ajoute (SOC-002 CA05).</summary>
    public const string FonctionInconnue = "fonction-inconnue";

    /// <summary>Refuse : la verification a echoue ; la panne est tracee (SOC-002 CA06).</summary>
    public const string Panne = "panne";
}

/// <summary>La reponse de <see cref="RoutesDroits.Verifier"/>.</summary>
/// <param name="Origine">Une valeur de <see cref="OriginesDroit"/>.</param>
/// <param name="Detail">Le role, le degre ou le menu qui accorde ; la cause d'un refus.</param>
public sealed record VerificationDroit(string Fonction, bool Autorise, string Origine, string? Detail);

/// <summary>Une ligne du journal des droits (SOC-005 CA01) ; <paramref name="Instant"/> en UTC.</summary>
/// <param name="Sens">« ajout » ou « retrait ».</param>
public sealed record LigneJournalDroits(long Id, DateTime Instant, string CompteVise, string Role, string Sens, string Auteur, string? Ecran);
