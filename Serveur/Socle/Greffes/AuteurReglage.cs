using System.Security.Claims;
using RigV3.Contrats.Socle;
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
}
