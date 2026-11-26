namespace RigV3.Contrats.EcransMain;

// Les ECRANS FAITS MAIN (web/src/ecrans/Ecran*.tsx, hors ecrans generes) lus dans la base du greffe (plan « RIG v3 100 %
// fonctionnel », lot 17, 26/11/2026). Jusqu'ici leurs ponts (Pont/EcranDemandes.cs et voisins) ne lisaient que la
// demonstration (Metier/JeuDemo.cs), meme branches au serveur de dev. Ils lisent desormais la base V3 du greffe ouvert,
// par ces routes ; sans serveur, la demonstration reste, marquee « exemple » a l'ecran.
//
// Chaque lecture cite sa source V2 dans son lecteur (Serveur/Socle/EcransMain/). Les criteres arrivent dans le CORPS d'un
// POST : ce sont ceux de l'ecran, nommes comme lui.

/// <summary>
/// Les adresses des ecrans faits main. {greffe} = le code du greffe ouvert : l'agent y est juge dans la base V3 de CE
/// greffe, qui est aussi la base lue (401 sans session, 403 compte refuse, 404 greffe inconnu, 503 base muette, sans repli).
/// </summary>
public static class RoutesEcransMain
{
    /// <summary>POST <see cref="RechercheDemandes"/> : les demandes (<see cref="DemandesLues"/>), ecran « Demandes ».</summary>
    public const string Demandes = "/ecrans-main/{greffe}/demandes";

    /// <summary>POST <see cref="RechercheArrivees"/> : le courrier arrive (<see cref="ArriveesLues"/>), ecrans « Arrivée du
    /// courrier » et « Télétransmissions » (un flux teletransmis est une arrivee de mode EDI).</summary>
    public const string Arrivees = "/ecrans-main/{greffe}/arrivees";

    /// <summary>GET : les annonces BODACC de la periode de recherche de RIG v2 (<see cref="AnnoncesBodaccLues"/>).</summary>
    public const string Bodacc = "/ecrans-main/{greffe}/bodacc";

    /// <summary>GET : les derniers dossiers RCS immatricules au greffe (<see cref="DossiersOuvertsLus"/>), ecran « Ouvrir un
    /// dossier ».</summary>
    public const string DossiersOuverts = "/ecrans-main/{greffe}/dossiers-ouverts";

    /// <summary>Le plafond de lignes d'une liste : une liste de travail, pas une extraction (meme borne que l'ecran, 500).</summary>
    public const int Plafond = 500;

    /// <summary>L'adresse <paramref name="route"/> du greffe <paramref name="greffe"/>.</summary>
    public static string Pour(string route, string greffe) => route.Replace("{greffe}", Uri.EscapeDataString(greffe));
}

/// <summary>Les criteres de l'ecran « Demandes » (Pont/EcranDemandes.cs, CriteresDemandes), dates sans heure.</summary>
/// <param name="Demande">Le champ « Demande » : numero de demande, numero de dossier, SIREN ou mot du nom.</param>
/// <param name="Client">Le champ « Client » : numero de compte (chiffres) ou debut du code abrege ou du nom.</param>
/// <param name="Processus">Le code du traitement (DMND_CODE_PROSS), vide pour tous.</param>
/// <param name="Etats">Les etats coches, par LIBELLE (ceux de <see cref="DemandesLues.Etats"/>).</param>
/// <param name="Plafond">« Nombre maxi. de demandes affichees » (1 a <see cref="RoutesEcransMain.Plafond"/>).</param>
public sealed record RechercheDemandes(string? Demande, string? Client, string? Processus, DateOnly? Du, DateOnly? Au,
                                       string[] Etats, bool EnCours, int Plafond);

/// <summary>Une demande lue (colonnes de PROC_DEMANDE/DemandeDataGridView.cs de RIG v2).</summary>
public sealed record DemandeLue(string Numero, string Nom, string Dossier, string Siren, string Client, string Traitement,
                                string TraitementLibelle, string Etat, bool EnCours, DateTime? Creation, DateTime? Validation,
                                string Utilisateur);

/// <summary>Les demandes trouvees, leur total, et les listes de choix de l'ecran lues dans la meme base.</summary>
/// <param name="Traitements">Les codes des traitements proposes (PROCESSUS_ET_FONCTIONALITE), dans l'ordre de la liste.</param>
/// <param name="LibellesTraitements">Leur libelle, dans le meme ordre.</param>
/// <param name="Etats">Les libelles des etats proposes, dans l'ordre de la liste de RIG v2.</param>
public sealed record DemandesLues(DemandeLue[] Lignes, int Total, string[] Traitements, string[] LibellesTraitements, string[] Etats);

/// <summary>Les criteres de l'ecran « Arrivée du courrier » (Pont/EcranArrivee.cs, CriteresArrivee), dates sans heure.
/// « Télétransmissions » n'envoie que <see cref="Mode"/> (EDI, ou vide pour tous).</summary>
public sealed record RechercheArrivees(string? Numero, string? Nom, string? References, string? Mode, string? Case,
                                       DateOnly? Du, DateOnly? Au, bool PaiementJoint, bool NonNumerise, int Plafond);

/// <summary>Une arrivee lue (table ARRIVEE de RIG v2, et la demande qu'elle a creee).</summary>
public sealed record ArriveeLue(string Numero, DateTime? Instant, string Mode, string ModeLibelle, string Nom, string References,
                                string Case, bool PaiementJoint, string NumDemande, string TypeDemande, string Utilisateur,
                                bool Numerise, bool Attestation);

/// <summary>Un mode d'arrivee (table MODE_ARRIVEE de RIG v2).</summary>
public sealed record ModeArriveeLu(string Code, string Libelle);

/// <summary>Les arrivees trouvees, leur total, celles qui restent a numeriser, et les modes d'arrivee.</summary>
public sealed record ArriveesLues(ArriveeLue[] Lignes, int Total, int ANumeriser, ModeArriveeLu[] Modes);

/// <summary>Une annonce BODACC (grilles BodaccToSendRigDataGridView / BodaccAlreadySentRigDataGridView de RIG v2).</summary>
/// <param name="Envoi">La date d'envoi d'une annonce DEJA ENVOYEE (sur un bordereau) ; null pour une annonce a envoyer.</param>
/// <param name="PrevueEnvoi">La date d'envoi prevue (BDCCR_DATE_ENVOI).</param>
public sealed record AnnonceBodaccLue(string NumAnnonce, string NumPc, string TypeAnnonce, string CodeEvePro, DateTime? DateEvePro,
                                      DateTime? PrevueEnvoi, DateTime? Envoi, string NumParution, DateTime? Parution, string NumBord,
                                      string NumFact, string Texte, string Dossier, DateTime? Transmission);

/// <summary>Les annonces de la periode <paramref name="Du"/> - <paramref name="Au"/> (dates d'envoi), et leur total.</summary>
public sealed record AnnoncesBodaccLues(AnnonceBodaccLue[] Lignes, int Total, DateTime Du, DateTime Au);

/// <summary>Un dossier RCS du greffe (fiche rec.DossierRcs reprise de DOSSIER_RCS, et son entreprise).</summary>
public sealed record DossierOuvertLu(string Numero, string Siren, string Denomination, string Forme, DateTime? Immatriculation);

/// <summary>Les derniers dossiers immatricules, et le nombre de dossiers du greffe.</summary>
public sealed record DossiersOuvertsLus(DossierOuvertLu[] Lignes, int Total);
