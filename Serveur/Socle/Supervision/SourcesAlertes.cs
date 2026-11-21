namespace RigV3.Serveur.Socle.Supervision;

/// <summary>Une cause d'alerte : tant qu'elle existe, son alerte reste ouverte ; disparue, l'alerte est fermee.</summary>
/// <param name="Cle">Unique par cause : « regle|greffe|... ». Elle commence par sa <paramref name="Regle"/>.</param>
/// <param name="Erreur">Ce que lit l'exploitation : la base, la table, l'erreur (1 000 caracteres au plus).</param>
public sealed record CauseAlerte(string Cle, string Regle, string? CodeGreffe, string Traitement, string Erreur);

/// <summary>
/// UNE SOURCE D'ALERTES D'UN AUTRE SUJET QUE LA SUPERVISION (REC-005 CA05, REC-006 CA04 -- lot 8 de la phase 2).
///
/// Le surveillant (SOC-011) evalue ses regles a chaque passage et y ajoute les causes de chaque source inscrite
/// (AddSingleton&lt;ISourceAlertes, ...&gt;) : meme alerte ouverte une fois, envoyee par le meme canal, fermee quand la
/// cause disparait, sans second canal ni seconde table. Une source en panne n'arrete pas le passage (journal technique)
/// et ses alertes ouvertes RESTENT ouvertes : une panne de lecture n'est pas une fin d'incident.
/// </summary>
public interface ISourceAlertes
{
    /// <summary>Les regles de cette source (le debut de la cle de ses causes), pour garder ses alertes si elle est en panne.</summary>
    IReadOnlyList<string> Regles { get; }

    Task<IReadOnlyList<CauseAlerte>> CausesAsync(DateTime maintenantUtc, CancellationToken ct);
}
