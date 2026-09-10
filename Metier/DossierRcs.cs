namespace RigV3Desktop;

/// <summary>
/// Une affaire telle qu'elle apparait dans la recherche et dans la
/// visionneuse. Le type vivait dans Home.razor ; la visionneuse etant
/// devenue un composant a part, il doit etre visible des deux.
/// </summary>
public sealed record Dossier(string Numero, string Objet, string Siren,
                            string Type, string Statut);
