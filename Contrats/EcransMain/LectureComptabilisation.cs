namespace RigV3.Contrats.EcransMain;

// « Comptabilisation » (PROC_COMPTABILISATION, web/src/ecrans/EcranComptabilisation.tsx), écran fait main lu dans la base du
// greffe (plan « RIG v3 100 % fonctionnel », lot 13, 27/11/2026 ; marche du lot 17). Le lecteur :
// Serveur/Socle/EcransMain/LectureComptabilisation.cs.

/// <summary>L'adresse de la lecture : GET, les journées à comptabiliser (<see cref="ComptabilisationLue"/>).</summary>
public static class RoutesComptabilisation
{
    public const string Journees = "/ecrans-main/{greffe}/comptabilisation";
}

/// <summary>
/// Un nœud d'encaissements d'une journée, comme l'arbre de RIG v2 le montre (TreeViewJoursCompta.ChargerTreeView) : un remis
/// (<paramref name="Remis"/> : ENCMT_REMIS, Classes/EncaissementRemis), ou les encaissements NON remis d'un mode de paiement
/// (<paramref name="Remis"/> null). <paramref name="Mode"/> est le code (ENCMT_MODE_PAIEMENT), <paramref name="Libelle"/> son libellé
/// (MODE_PAIEMENT), <paramref name="TypeMode"/> son type (R_MODEPAIEME_TYPE) ; <paramref name="Observation"/> : la phrase de RIG v2 quand
/// un remis du même mode existe déjà ce jour-là (vide sinon). Montant en décimal exact.
/// </summary>
public sealed record RemisComptaLu(int? Remis, string Mode, string Libelle, string TypeMode, int Nombre, decimal Montant, DateTime Date,
                                   string Observation);

/// <summary>
/// Une journée à comptabiliser : l'agrégat de ses factures non comptabilisées (Classes/FactureJour : nombre, total TTC, affaires en
/// cours, factures sans demande) et ses nœuds d'encaissements.
/// </summary>
public sealed record JourneeComptaLue(DateTime Date, int Factures, decimal MontantTtc, int AffairesEnCours, int SansDemande,
                                      RemisComptaLu[] Encaissements);

/// <summary>
/// Les journées à comptabiliser, de la plus ancienne à la plus récente (au plus le plafond), et leur total.
/// <paramref name="Anomalie"/> : le message bloquant de RIG v2 quand des factures non comptabilisées ne sont rattachées à aucune
/// demande (aucune journée alors, comme RIG v2 qui vide l'arbre) ; null sinon. <paramref name="EncaissementsSeuls"/> : le réglage
/// EXPRESSO_FIGERFACTAUTO vaut « 0 » (cocher une journée ne coche que ses encaissements).
/// </summary>
public sealed record ComptabilisationLue(JourneeComptaLue[] Journees, int Total, string? Anomalie, bool EncaissementsSeuls);
