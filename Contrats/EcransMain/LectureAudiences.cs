namespace RigV3.Contrats.EcransMain;

// « Créer une audience » (PROC_AUDIENCE, web/src/ecrans/EcranAudiences.tsx), ecran fait main lu dans la base du greffe (plan
// « RIG v3 100 % fonctionnel », lot 7, 26/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureAudiences.cs.

/// <summary>L'adresse de la lecture : GET, les listes de la creation et les audiences a venir (<see cref="AudiencesProgrammeesLues"/>).</summary>
public static class RoutesAudiences
{
    public const string Programmees = "/ecrans-main/{greffe}/audiences";
}

/// <summary>Une audience a venir : date, heure, type (libelle), chambre et section (libelles), publique, non disponible sur le web,
/// nombre d'affaires au role.</summary>
public sealed record AudienceProgrammeeLue(DateTime? Date, string Heure, string Type, string Chambre, string Section, bool Publique,
                                           bool NonDisponibleWeb, int Affaires);

/// <summary>Les audiences a venir (au plus le plafond), leur total, et les listes de saisie de la creation : types d'audience,
/// chambres, sections, types de classement.</summary>
public sealed record AudiencesProgrammeesLues(AudienceProgrammeeLue[] Lignes, int Total, string[] Types, string[] Chambres, string[] Sections,
                                              string[] Classements);
