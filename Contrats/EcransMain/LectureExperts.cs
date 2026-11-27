namespace RigV3.Contrats.EcransMain;

// « Experts » (PROC_EXPERT, web/src/ecrans/EcranExperts.tsx), écran fait main lu dans la base du greffe (plan « RIG v3 100 %
// fonctionnel », lot 14, 27/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureExperts.cs.

/// <summary>L'adresse de la lecture : GET, les experts et leurs mesures (<see cref="ExpertsLus"/>).</summary>
public static class RoutesExperts
{
    public const string Experts = "/ecrans-main/{greffe}/experts";
}

/// <summary>Un expert (EXPERT de RIG v2), aux champs de sa fiche d'édition (OPE_EDITION_EXPERT) ; naissance jj/mm/aaaa.</summary>
public sealed record ExpertLu(int Id, string Abrege, string Civilite, string Nom, string ComplementNom, string Naissance, string Adresse,
                              string Telephone, string Fax, string Mail, string Qualification, bool Actif);

/// <summary>
/// Une mesure où l'expert participe (PARTICIPANT joint à MESURE, Participant.GetListeParticipantJoinMesureByExpert) : les colonnes de
/// ULT_LISTVIEW_MESURES_EXPERT ; <paramref name="EnCours"/> = participant actif sur une mesure non clôturée.
/// </summary>
public sealed record MesureExpertLue(int IdExpert, string NumMesure, string Civilite, string NomParticipant, string ComplementNom,
                                     string Qualification, string Adresse, bool EnCours);

/// <summary>Les experts (au plus le plafond), par abrégé, leurs mesures, et le total des experts.</summary>
public sealed record ExpertsLus(ExpertLu[] Experts, MesureExpertLue[] Mesures, int Total);
