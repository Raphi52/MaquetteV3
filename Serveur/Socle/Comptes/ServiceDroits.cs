using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.Comptes;

/// <summary>
/// L'agent qui appelle, tel que la gestion des droits a besoin de le connaitre.
/// Ses marques Amitel et administrateur suivent la V2 : un ADMINISTRATEUR est un compte Amitel
/// marque administrateur (Utilisateur.IsUserAdministrateur, Utilisateur.cs:75-77).
/// </summary>
public sealed record Appelant(int Id, string CompteWindows, string NomAffiche, bool EstAmitel, bool MarqueAdministrateur, bool GereLesDroits)
{
    /// <summary>Utilisateur.IsUserAdministrateur de la V2 : Amitel ET marque administrateur.</summary>
    public bool EstAdministrateur => EstAmitel && MarqueAdministrateur;

    /// <summary>
    /// SOC-002 CA03 : ce que l'appelant voit -- donc peut attribuer -- d'un role ou d'une fonction reserves.
    /// Regle de la V2 (OPE_RECHERCHE_PROCESSUS.cs:319-320, meme regle pour les roles) : hors Amitel, ni les
    /// reserves Amitel ni les reserves administrateur ; Amitel non administrateur, pas les reserves
    /// administrateur ; administrateur, tout.
    /// </summary>
    public bool Voit(bool reserveAmitel, bool reserveAdministrateur)
        => EstAdministrateur || (EstAmitel ? !reserveAdministrateur : !reserveAmitel && !reserveAdministrateur);
}

/// <summary>
/// LA VERIFICATION D'UN DROIT (SOC-002, tache T02), en lecture seule.
///
/// Un compte a une fonction s'il l'a par l'une des trois origines de la V2, reunies a la reprise dans
/// soc.Droit (Base\migrations\SOC-002_droits.sql) : un de ses roles, l'ancien systeme (degre > 2) ou un
/// menu de comptabilite. La V2 (Utilisateur.cs:298-347) faisait le meme calcul a CHAQUE verification,
/// en ajoutant au catalogue toute fonction inconnue (Utilisateur.cs:300-316) et en avalant toute erreur
/// de l'ancien systeme comme un refus sans trace (Habilitation.cs:8-16). La V3 :
///  - refuse une fonction inconnue SANS RIEN ECRIRE (CA05) ;
///  - refuse sur une panne ET la trace avec le compte, la fonction et l'heure (CA06).
/// </summary>
public sealed class ServiceDroits(ServiceIdentite identites, ILogger<ServiceDroits> journal)
{
    /// <summary>L'evenement de journal d'une verification en panne (CA06).</summary>
    public static readonly EventId EvenementPanne = new(51006, "PanneVerificationDroit");

    private static readonly object CleAppelant = new();

    /// <summary>
    /// L'appelant admis de cette requete (SOC-001), avec son identifiant et son droit de gerer les droits ;
    /// null s'il n'est pas admis. Lu une fois par requete.
    /// </summary>
    public async Task<Appelant?> AppelantAsync(HttpContext contexte, BaseRig b, CancellationToken ct)
    {
        if (contexte.Items.TryGetValue(CleAppelant, out var deja)) return (Appelant?)deja;
        var identite = await identites.DeLAppelantAsync(contexte, b, ct);
        Appelant? appelant = null;
        if (identite is { Admis: true, Agent: { } agent })
        {
            await using var cn = await b.OuvrirAsync(ct);
            await using var cmd = new SqlCommand("""
                SELECT c.Id, c.EstAmitel, c.EstAdministrateur,
                       CASE WHEN EXISTS (SELECT 1 FROM soc.CompteRole r WHERE r.IdCompte = c.Id AND r.Role = @gestion) THEN 1 ELSE 0 END
                FROM soc.Compte c WHERE c.CompteWindows = @compte;
                """, cn);
            cmd.Parameters.Add(new SqlParameter("@compte", SqlDbType.VarChar, 35) { Value = agent.CompteWindows });
            cmd.Parameters.Add(new SqlParameter("@gestion", SqlDbType.VarChar, 40) { Value = RoutesDroits.RoleGestionDesDroits });
            await using var lecteur = await cmd.ExecuteReaderAsync(ct);
            if (await lecteur.ReadAsync(ct))
                appelant = new Appelant(lecteur.GetInt32(0), agent.CompteWindows, agent.NomAffiche,
                    lecteur.GetBoolean(1), lecteur.GetBoolean(2), lecteur.GetInt32(3) == 1);
        }
        contexte.Items[CleAppelant] = appelant;
        return appelant;
    }

    /// <summary>
    /// Le compte <paramref name="idCompte"/> a-t-il la fonction <paramref name="fonction"/> ? Ne leve jamais :
    /// une panne est un REFUS, trace (CA06). Aucune ecriture, quelle que soit la reponse (CA05).
    /// </summary>
    public async Task<VerificationDroit> VerifierAsync(BaseRig b, int idCompte, string compte, string fonction, CancellationToken ct)
    {
        fonction = (fonction ?? string.Empty).Trim();
        // Plus long que la colonne : elle ne peut pas y etre ; la passer en varchar(40) la tronquerait
        // et pourrait accorder une AUTRE fonction.
        if (fonction.Length is 0 or > 40)
            return new VerificationDroit(fonction, false, OriginesDroit.FonctionInconnue, "fonction absente du catalogue");
        try
        {
            await using var cn = await b.OuvrirAsync(ct);
            await using var cmd = new SqlCommand("""
                SELECT f.Code,
                       (SELECT TOP (1) d.Origine + '|' + ISNULL(d.Detail, '')
                        FROM soc.Droit d
                        WHERE d.IdCompte = @compte AND d.Fonction = f.Code
                        ORDER BY CASE d.Origine WHEN 'role' THEN 0 WHEN 'ancien' THEN 1 ELSE 2 END, d.Detail)
                FROM soc.Fonction f
                WHERE f.Code = @fonction;
                """, cn);
            cmd.Parameters.Add(new SqlParameter("@compte", SqlDbType.Int) { Value = idCompte });
            cmd.Parameters.Add(new SqlParameter("@fonction", SqlDbType.VarChar, 40) { Value = fonction });
            await using var lecteur = await cmd.ExecuteReaderAsync(ct);
            if (!await lecteur.ReadAsync(ct))
                return new VerificationDroit(fonction, false, OriginesDroit.FonctionInconnue, "fonction absente du catalogue");
            var code = lecteur.GetString(0);
            if (lecteur.IsDBNull(1)) return new VerificationDroit(code, false, OriginesDroit.Aucune, null);
            var origine = lecteur.GetString(1).Split('|', 2);
            return new VerificationDroit(code, true, origine[0], origine[1].Length == 0 ? null : origine[1]);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var instant = DateTime.UtcNow;
            journal.LogError(EvenementPanne, e,
                "Verification de droit en panne, fonction refusee : compte {Compte}, fonction {Fonction}, a {Instant:O} (UTC) : {Cause}",
                compte, fonction, instant, e.Message);
            return new VerificationDroit(fonction, false, OriginesDroit.Panne, $"verification en panne a {instant:O}");
        }
    }

    /// <summary>
    /// Toutes les fonctions du catalogue que le compte a, en une lecture (la meme vue que <see cref="VerifierAsync"/>) :
    /// c'est la reponse de la V3 que le comparateur de la reprise oppose a celle de la V2, fonction par fonction.
    /// </summary>
    public static async Task<HashSet<string>> FonctionsAutoriseesAsync(SqlConnection cn, int idCompte, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT DISTINCT f.Code FROM soc.Fonction f
            WHERE EXISTS (SELECT 1 FROM soc.Droit d WHERE d.IdCompte = @compte AND d.Fonction = f.Code);
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@compte", SqlDbType.Int) { Value = idCompte });
        var fonctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var lecteur = await cmd.ExecuteReaderAsync(ct);
        while (await lecteur.ReadAsync(ct)) fonctions.Add(lecteur.GetString(0));
        return fonctions;
    }
}
