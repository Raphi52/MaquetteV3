using System.Data;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Identite;

/// <summary>
/// QUI APPELLE, ET PEUT-IL OUVRIR RIG ? (SOC-001, tache T02.)
///
/// Le compte vient de la session Windows transmise par l'ecran (authentification
/// integree, <see cref="ModuleIdentite"/>), sans domaine, comme
/// <c>System.Environment.UserName</c> dans la V2 (AmiEnvironment.cs:14). Il est
/// cherche dans soc.Compte sans tenir compte des majuscules (collation CI de la
/// colonne, comme le ToUpper de Utilisateur.cs:428), puis juge par les QUATRE
/// controles de la V2, dans SON ordre (RigConsoleAccueil.cs:113-133) :
/// compte inconnu, aucune habilitation, desactive, aucun service. Le premier qui
/// echoue donne le refus ; un compte desactive ET sans role est donc refuse pour
/// « aucune habilitation », comme dans la V2.
/// Rien n'est ecrit : un refus ne cree ni compte ni trace (CA03).
///
/// DANS QUELLE BASE (lot 8). Comme la V2 (Utilisateur.GetUtilisateurCourant(CodeGreffeRig),
/// Utilisateur.cs:424-432), le compte se cherche dans la base DU GREFFE choisi : une route dont
/// l'adresse porte <c>{greffe}</c> (<see cref="PolitiquesSocle.ParametreGreffe"/>) ouvre la base V3
/// de ce greffe par le catalogue commun (<see cref="CatalogueGreffes.OuvrirBaseV3Async"/>) : greffe
/// absent -> <see cref="GreffeInconnu"/> (404), base muette -> <see cref="BaseGreffeInjoignable"/>
/// (503), jamais de repli sur une autre base. Un agent declare dans un seul greffe est donc inconnu
/// des autres. Une route COMMUNE (sans greffe : le catalogue des greffes, /socle/identite) juge
/// le compte dans la base commune du serveur (<see cref="BaseRig"/>).
/// </summary>
public sealed class ServiceIdentite(CatalogueGreffes catalogue)
{
    /// <summary>Cle de <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/> : l'identite
    /// jugee une fois par requete (la route et la politique d'acces la partagent).</summary>
    private static readonly object CleRequete = new();

    /// <summary>Longueur de soc.Compte.CompteWindows (UTLST_LOGIN de la V2).</summary>
    private const int LongueurCompte = 35;

    /// <summary>
    /// Le compte Windows d'un appelant authentifie, sans son domaine (« AMITEL\jdupont »
    /// ou « jdupont@AMITEL.LOCAL » -> « jdupont ») ; null s'il n'est pas authentifie.
    /// </summary>
    public static string? CompteDe(ClaimsPrincipal utilisateur)
    {
        if (utilisateur.Identity is not { IsAuthenticated: true, Name: { } nom }) return null;
        var compte = nom.Trim();
        var barre = compte.LastIndexOf('\\');
        if (barre >= 0) compte = compte[(barre + 1)..];
        var arobase = compte.IndexOf('@');
        if (arobase > 0) compte = compte[..arobase];
        return compte.Length == 0 ? null : compte;
    }

    /// <summary>Le greffe choisi par la requete : la valeur <c>{greffe}</c> de son adresse ; null pour une route commune.</summary>
    public static string? GreffeDe(HttpContext contexte)
        => contexte.GetRouteValue(PolitiquesSocle.ParametreGreffe) is string { Length: > 0 } greffe ? greffe : null;

    /// <summary>
    /// L'identite de l'appelant de cette requete, jugee une seule fois ; null s'il n'est pas authentifie
    /// (aucune base n'est alors ouverte). Le compte est cherche dans la base du greffe de la requete
    /// (<see cref="GreffeDe"/>), sinon dans la base commune <paramref name="commune"/>.
    /// </summary>
    /// <exception cref="GreffeInconnu">Le greffe de l'adresse n'est pas au catalogue.</exception>
    /// <exception cref="BaseGreffeInjoignable">Sa base V3 ne repond pas.</exception>
    public async Task<IdentiteAgent?> DeLAppelantAsync(HttpContext contexte, BaseRig commune, CancellationToken ct)
    {
        if (contexte.Items.TryGetValue(CleRequete, out var deja)) return (IdentiteAgent?)deja;
        var compte = CompteDe(contexte.User);
        IdentiteAgent? identite = null;
        if (compte is not null)
        {
            await using var cn = GreffeDe(contexte) is { } greffe
                ? await catalogue.OuvrirBaseV3Async(greffe, ct)
                : await commune.OuvrirAsync(ct);
            identite = await IdentifierAsync(cn, compte, ct);
        }
        contexte.Items[CleRequete] = identite;
        return identite;
    }

    /// <summary>Juge le compte Windows <paramref name="compte"/> sur la base ouverte <paramref name="cn"/>.</summary>
    public async Task<IdentiteAgent> IdentifierAsync(SqlConnection cn, string compte, CancellationToken ct)
    {
        // Un nom plus long que la colonne ne peut pas y etre ; le passer en varchar(35)
        // le tronquerait et pourrait reconnaitre un AUTRE compte.
        if (compte.Length > LongueurCompte) return Juger(compte, null);

        await using var cmd = new SqlCommand("""
            SELECT c.CompteWindows, c.Nom, c.Prenom, c.Abrege, c.Civilite, c.Desactive,
                   c.EstAmitel, c.EstAdministrateur, c.EstAssistance,
                   COALESCE(s.Nom, s.Abrege) AS Service,
                   (SELECT COUNT(*) FROM soc.CompteRole r WHERE r.IdCompte = c.Id) AS Roles
            FROM soc.Compte c
            LEFT JOIN soc.Service s ON s.Id = c.IdService
            WHERE c.CompteWindows = @compte;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@compte", SqlDbType.VarChar, LongueurCompte) { Value = compte });
        await using var lecteur = await cmd.ExecuteReaderAsync(ct);
        LigneCompte? ligne = null;
        if (await lecteur.ReadAsync(ct))
        {
            ligne = new LigneCompte(
                new AgentReconnu(
                    lecteur.GetString(0), lecteur.GetString(1), lecteur.GetString(2), lecteur.GetString(3),
                    lecteur.GetString(4), lecteur.IsDBNull(9) ? string.Empty : lecteur.GetString(9),
                    lecteur.GetBoolean(6), lecteur.GetBoolean(7), lecteur.GetBoolean(8)),
                Desactive: lecteur.GetBoolean(5),
                Roles: lecteur.GetInt32(10),
                AUnService: !lecteur.IsDBNull(9));
        }
        return Juger(compte, ligne);
    }

    /// <summary>Ce que la base dit d'un compte, avant jugement.</summary>
    internal sealed record LigneCompte(AgentReconnu Agent, bool Desactive, int Roles, bool AUnService);

    /// <summary>
    /// Les quatre controles de CheckUser (RigConsoleAccueil.cs:113-133), dans leur ordre et
    /// avec leurs messages. <paramref name="compte"/> est celui de la session, comme
    /// AmiEnvironment.UserName dans les messages de la V2.
    /// </summary>
    internal static IdentiteAgent Juger(string compte, LigneCompte? ligne)
    {
        const string Hotline = "Merci de contacter la hotline d'Amitel pour signaler le problème.";
        if (ligne is null)
            return Refus(StatutsIdentite.Inconnu, compte, $"L'utilisateur '{compte}' n'a pas été défini dans RIG.", Hotline);
        // La V2 teste « RoleUsers == null », jamais vrai (liste vide au pire) : elle laisse
        // donc entrer un compte sans role. La V3 fait ce que la V2 prevoit (ecart a valider,
        // docs/reconstruction/socle-identite.md).
        if (ligne.Roles == 0)
            return Refus(StatutsIdentite.SansDroit, compte, $"Aucune habilitation n'a été définie pour l'utilisateur '{compte}'.", Hotline);
        if (ligne.Desactive)
            return Refus(StatutsIdentite.Desactive, compte, $"L'utilisateur '{compte}' a été désactivé.", Hotline);
        if (!ligne.AUnService)
            return Refus(StatutsIdentite.SansService, compte, $"Aucun service n'est rattaché à l'utilisateur '{compte}'.",
                "Merci de signaler le problème à la hotline d'AMITEL.");
        return new IdentiteAgent(StatutsIdentite.Admis, compte, ligne.Agent, null, null);
    }

    private static IdentiteAgent Refus(string statut, string compte, string message, string assistance)
        => new(statut, compte, null, message, assistance);
}
