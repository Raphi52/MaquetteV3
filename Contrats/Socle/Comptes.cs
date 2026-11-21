namespace RigV3.Contrats.Socle;

/// <summary>
/// Les adresses de la gestion des comptes des agents (SOC-004) et de leurs roles (SOC-002, SOC-005).
/// Toutes exigent un agent admis QUI A LE DROIT DE GERER LES DROITS (role
/// <see cref="RoutesDroits.RoleGestionDesDroits"/>) : sinon 403 (SOC-002 CA04).
/// Un refus METIER (compte deja pris, suppression d'un compte qui a agi...) n'est pas une erreur HTTP :
/// 200 avec <see cref="ResultatCompte.Fait"/> = false et le message, que l'ecran affiche.
/// </summary>
public static class RoutesComptes
{
    /// <summary>GET : <see cref="VueComptes"/> -- les comptes, les services et les roles que l'appelant peut voir.</summary>
    public const string Comptes = "/socle/comptes";

    /// <summary>POST <see cref="DemandeCompte"/> (Id null) -> <see cref="ResultatCompte"/>.</summary>
    public const string Creer = "/socle/comptes/creer";

    /// <summary>POST <see cref="DemandeCompte"/> (Id pose) -> <see cref="ResultatCompte"/>.</summary>
    public const string Modifier = "/socle/comptes/modifier";

    /// <summary>POST <see cref="DemandeSurCompte"/> -> <see cref="ResultatCompte"/>.</summary>
    public const string Desactiver = "/socle/comptes/desactiver";

    /// <summary>POST <see cref="DemandeSurCompte"/> -> <see cref="ResultatCompte"/> ; refuse pour un compte qui a agi (CA04).</summary>
    public const string Supprimer = "/socle/comptes/supprimer";

    /// <summary>POST <see cref="DemandeRole"/> -> <see cref="ResultatCompte"/> : donner ou retirer un role.</summary>
    public const string Role = "/socle/comptes/role";
}

/// <summary>Un compte d'agent tel que la gestion des comptes le montre (aucun mot de passe : SOC-001 CA08).</summary>
/// <param name="Service">Le nom du service de rattachement (son abrege s'il n'a pas de nom) ; vide sans service.</param>
/// <param name="Roles">Ses roles, y compris ceux que l'appelant ne peut pas attribuer.</param>
public sealed record CompteAgent(
    int Id,
    string CompteWindows,
    string Nom,
    string Prenom,
    string Civilite,
    string Abrege,
    string? CodeGreffe,
    int? IdService,
    string Service,
    bool Desactive,
    bool EstAmitel,
    bool EstAdministrateur,
    string[] Roles);

/// <summary>Un service du greffe (V2 : SERVICE), avec ses reglages de distribution (SOC-004 CA08).</summary>
/// <param name="HeureDistribution">« HH:mm », ou null sans heure.</param>
public sealed record ServiceGreffe(
    int Id,
    string Abrege,
    string? Nom,
    string? CodeDirection,
    bool DistributionAuto,
    bool DistributionSpecifique,
    string? HeureDistribution);

/// <summary>Un role que l'appelant voit -- donc qu'il peut attribuer (SOC-002 CA03).</summary>
/// <param name="Comptabilite">Le role ouvre une fonction de facturation ou d'encaissement (SOC-005 CA02).</param>
public sealed record RoleVisible(string Nom, string Libelle, bool ReserveAmitel, bool ReserveAdministrateur, bool Comptabilite);

/// <summary>Ce que l'ecran « Comptes des agents » affiche.</summary>
/// <param name="Agent">« Prenom Nom » de l'appelant.</param>
public sealed record VueComptes(CompteAgent[] Comptes, ServiceGreffe[] Services, RoleVisible[] Roles, string Agent);

/// <summary>Creer (Id null) ou modifier (Id pose) un compte.</summary>
/// <param name="Roles">A la creation : les roles a donner d'emblee (un compte sans role n'ouvre pas RIG,
/// SOC-001 CA05). Ignore a la modification (les roles passent par <see cref="RoutesComptes.Role"/>).</param>
/// <param name="Ecran">L'ecran d'ou part le geste, pour le journal (SOC-016 CA01).</param>
public sealed record DemandeCompte(
    int? Id,
    string CompteWindows,
    string Nom,
    string Prenom,
    string Civilite,
    int? IdService,
    string[]? Roles,
    string? Ecran);

/// <summary>Un geste sur un compte existant (desactiver, supprimer).</summary>
public sealed record DemandeSurCompte(int Id, string? Ecran);

/// <summary>Donner (<paramref name="Ajouter"/> = true) ou retirer un role a un compte.</summary>
public sealed record DemandeRole(int IdCompte, string Role, bool Ajouter, string? Ecran);

/// <summary>La reponse d'un geste sur les comptes.</summary>
/// <param name="Fait">Le geste est enregistre (et journalise).</param>
/// <param name="Message">La raison d'un refus (textes de la V2 quand elle en a un) ; null si fait.</param>
/// <param name="Compte">Le compte apres le geste ; null apres une suppression ou un refus.</param>
/// <param name="ProposerDesactivation">Suppression refusee : proposer la desactivation a la place (SOC-004 CA04).</param>
public sealed record ResultatCompte(bool Fait, string? Message, CompteAgent? Compte, bool ProposerDesactivation = false);
