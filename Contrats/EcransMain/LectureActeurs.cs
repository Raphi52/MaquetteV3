namespace RigV3.Contrats.EcransMain;

// « Acteurs » (PROC_ACTEUR_JUD, web/src/ecrans/EcranActeurs.tsx), écran fait main lu dans la base du greffe (plan « RIG v3 100 %
// fonctionnel », lot 14, 27/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureActeurs.cs.

/// <summary>L'adresse de la lecture : GET, les acteurs des six types de RIG v2 (<see cref="ActeursLus"/>).</summary>
public static class RoutesActeurs
{
    public const string Acteurs = "/ecrans-main/{greffe}/acteurs";
}

/// <summary>
/// Un acteur (juge, mandataire PC, parquetier, greffier assermenté, intervenant PC ou expert), aux champs que l'écran montre et que
/// sa table RIG v2 porte ; un champ que la table n'a pas reste vide. Dates aaaa-mm-jj.
/// </summary>
public sealed record ActeurLu(string Type, int Id, string Abrege, string Civilite, string Nom, string Prenom, string ComplementNom, string Adresse,
                              string Telephone, string Fax, string Mail, string Qualification, string Statut, string Chambre, string Section,
                              string? Debut, string? Fin, string Bureau, string NomCoffre, bool Actif, string Siren, string Utilisateur);

/// <summary>Les acteurs lus (au plus le plafond par type), dans l'ordre des types puis des abrégés, et leur total.</summary>
public sealed record ActeursLus(ActeurLu[] Lignes, int Total);
