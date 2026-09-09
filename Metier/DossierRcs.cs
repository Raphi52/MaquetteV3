namespace RigV3Desktop;

/// <summary>
/// Une affaire telle qu'elle apparait dans la recherche et dans la
/// visionneuse. Le type vivait dans Home.razor ; la visionneuse etant
/// devenue un composant a part, il doit etre visible des deux.
/// </summary>
public sealed record Dossier(string Numero, string Objet, string Siren,
                            string Type, string Statut);

/// <summary>Une piece numerisee du dossier.</summary>
public sealed record Document(string Libelle, string Extension, string Date, int Pages,
                              string Origine, string Apercu);

/// <summary>Un dossier de l'arbre VGED et les pieces qu'il porte.</summary>
public sealed record Categorie(string Nom, string Libelle, Document[] Documents);
