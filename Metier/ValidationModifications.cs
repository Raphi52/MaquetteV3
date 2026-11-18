namespace RigV3Desktop;

// ============================================================
// Circuit de VALIDATION HIERARCHIQUE des modifications de documents.
//
// Certains logins n'ont pas le droit de toucher la GED en direct : leurs
// gestes (renommer, supprimer, dupliquer, creer) ne s'appliquent pas, ils
// partent en DEMANDE. Un login habilite (le superieur) la valide ou la
// rejette ; la modification n'est appliquee qu'a la validation.
//
// Rien n'est branche sur une base : la file vit en memoire de la maquette.
// ============================================================

/// <summary>Les etats d'une demande de modification.</summary>
public static class EtatModification
{
    public const string EnAttente = "En attente de validation";
    public const string Validee = "Validee";
    public const string Rejetee = "Rejetee";
}

/// <summary>
/// Une modification SOUMISE par un login sans droit d'ecriture directe.
/// <paramref name="Geste"/> est le verbe metier (Renommer, Supprimer,
/// Dupliquer, Creer), <paramref name="Cible"/> le document concerne.
/// </summary>
public sealed record ModificationProposee(
    int Numero,
    string Geste,
    string Cible,
    string Detail,
    string Auteur,
    DateTime Depot,
    string Etat = EtatModification.EnAttente,
    string Decideur = "",
    string Motif = "")
{
    public string DepotLisible => Depot.ToString("dd/MM/yyyy HH:mm");

    /// <summary>La ligne telle qu'elle se lit dans le panneau de validation.</summary>
    public string Resume => Detail.Length > 0
        ? $"{Geste} « {Cible} » : {Detail}"
        : $"{Geste} « {Cible} »";

    public bool EstEnAttente => Etat == EtatModification.EnAttente;
}
