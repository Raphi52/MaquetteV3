using System.Security.Claims;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.Greffes;

/// <summary>
/// QUI A MODIFIE UN REGLAGE (SOC-003 CA07 ; lot 4), comme la V2
/// (Parametrage.UpdateValeurParametrage, Parametrage.cs:104-108) : le NOM de l'agent
/// reconnu (UTLST_NOM, ici soc.Compte.Nom) ; s'il n'est pas reconnu, son compte Windows
/// SANS domaine (AmiEnvironment.UserName = System.Environment.UserName, AmiEnvironment.cs:14 ;
/// ici <see cref="ServiceIdentite.CompteDe"/>). Les auteurs repris de la V2 sont des noms :
/// la V3 en ecrit aussi, la colonne ne melange pas noms et comptes.
/// </summary>
public static class AuteurReglage
{
    /// <returns>L'auteur a ecrire ; null si l'appelant n'a aucune identite (modification refusee).</returns>
    public static string? De(IdentiteAgent? identite, ClaimsPrincipal utilisateur)
        => identite?.Agent is { } agent ? agent.Nom : ServiceIdentite.CompteDe(utilisateur);

    /// <summary>
    /// Le meme auteur pour un GESTE d'ecran genere (EcrivainEcran.cs), qui n'a pas la requete mais l'agent admis
    /// (<see cref="Appelant"/>, lu dans soc.Compte de la base du greffe) : son NOM, soc.Compte.Nom -- pas
    /// <see cref="Appelant.NomAffiche"/> (« Prenom Nom »). Un appelant est toujours reconnu ; son compte (sans domaine)
    /// ne sert que si sa ligne a disparu entre-temps.
    /// </summary>
    public static async Task<string> DeLAgentAsync(SqlConnection cn, SqlTransaction tx, Appelant agent, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT Nom FROM soc.Compte WHERE Id = @id;", cn, tx);
        cmd.Parameters.Add(new SqlParameter("@id", System.Data.SqlDbType.Int) { Value = agent.Id });
        return await cmd.ExecuteScalarAsync(ct) is string { Length: > 0 } nom ? nom : agent.CompteWindows;
    }
}
