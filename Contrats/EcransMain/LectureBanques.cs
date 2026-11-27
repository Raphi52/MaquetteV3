namespace RigV3.Contrats.EcransMain;

// « Gestion des banques » (PROC_GESTBANQ, web/src/ecrans/EcranBanques.tsx), écran fait main lu dans la base du greffe (plan « RIG v3
// 100 % fonctionnel », lot 12, 27/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureBanques.cs.

/// <summary>L'adresse de la lecture : GET, les banques du greffe (<see cref="BanquesLues"/>).</summary>
public static class RoutesBanques
{
    public const string Banques = "/ecrans-main/{greffe}/banques";
}

/// <summary>
/// Une banque de RIG v2 (une ligne de BANQUE) : son code et son nom (OPE_BANQIDENTITE), son adresse (OPE_BANQADRESSE : ligne, suite,
/// code postal, localité), son compte et son journal (OPE_PARAMCOMPTABLE), les paramètres de ses lettres chèques (OPE_BANQCHEQUE :
/// modèle, compteur, et les trois émissions autorisées) et les codes des modes de paiement qui lui sont associés
/// (MODE_PAIEMENT_BANQUE, Banque.GetListeModePaiementBanque).
/// </summary>
public sealed record BanqueLue(int Id, string Code, string Nom, string LigneAdresse, string SuiteAdresse, string CodePostal, string Localite,
    string Compte, string Journal, string ModeleLettreCheque, long? CompteurLettreCheque, bool PourTropPercu, bool PourNotification,
    bool PourFournisseur, string[] Modes);

/// <summary>Les banques lues, dans l'ordre de RIG v2 (sa clé), et leur total.</summary>
public sealed record BanquesLues(BanqueLue[] Lignes, int Total);
