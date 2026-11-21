namespace RigV3.Contrats.Socle;

// Les echanges de la supervision d'Amitel (lot 2 de la phase 2 du plan de reconstruction, 2026-11-21) :
// SOC-017 (retrouver une erreur technique sans aller sur les serveurs) et SOC-011 (surveiller la partie
// serveur et ses traitements depuis Amitel). Le serveur les publie (Serveur/Socle/Supervision/ModuleSupervision.cs),
// l'ecran « Supervision » les lit (Pont/EcranSupervision.cs). Mode d'emploi : docs/reconstruction/socle-supervision.md.

/// <summary>
/// Les adresses de la supervision. Toutes sont COMMUNES (sans {greffe}) : le journal technique, les etats et les
/// alertes vivent dans la base commune du serveur, pour les 40 greffes ensemble, outre-mer compris (SOC-017 CA06,
/// SOC-011 CA03). La consultation est reservee a l'exploitation d'Amitel (compte marque Amitel) : 403 sinon. Un agent
/// ne recoit d'une erreur que son identifiant de suivi ; le detail (message, pile) ne se lit qu'ici.
/// </summary>
public static class RoutesSupervision
{
    /// <summary>L'en-tete qui porte l'identifiant de suivi d'un parcours : envoye par l'ecran pour regrouper ses
    /// appels (facultatif), rendu par le serveur sur CHAQUE reponse.</summary>
    public const string EnTeteSuivi = "X-Rig-Suivi";

    /// <summary>POST <see cref="ErreurEcran"/> : une erreur non prevue de l'ecran, journalisee ; rend <see cref="SuiviErreur"/>.
    /// Ouverte a toute session Windows (l'agent n'est peut-etre pas encore admis quand l'erreur survient).</summary>
    public const string ErreursEcran = "/socle/erreurs";

    /// <summary>GET (Amitel) : recherche dans le journal technique (?suivi=&amp;greffe=&amp;compte=&amp;programme=&amp;du=&amp;au=),
    /// la plus recente en premier, <see cref="ErreursAuPlus"/> au plus.</summary>
    public const string Erreurs = "/socle/supervision/erreurs";

    /// <summary>GET (Amitel) : tous les messages d'un parcours, par son identifiant de suivi (SOC-017 CA02).</summary>
    public const string Parcours = "/socle/supervision/erreurs/{suivi}";

    /// <summary>GET (Amitel) : l'etat des 40 greffes, des traitements et les alertes (SOC-011 CA03).</summary>
    public const string Etat = "/socle/supervision/etat";

    /// <summary>POST <see cref="DemandeRelance"/> (Amitel) : relancer un traitement arrete, sans perte de file (SOC-011 CA04).</summary>
    public const string Relancer = "/socle/supervision/traitements/relancer";

    /// <summary>Le plus de lignes qu'une recherche rend (les plus recentes).</summary>
    public const int ErreursAuPlus = 200;

    /// <summary>L'adresse du parcours d'un identifiant de suivi.</summary>
    public static string PourParcours(string suivi) => Parcours.Replace("{suivi}", Uri.EscapeDataString(suivi));
}

/// <summary>Les niveaux du journal technique ; chacun a sa duree de conservation ecrite (soc.ConservationTechnique, SOC-017 CA04).</summary>
public static class NiveauxErreur
{
    /// <summary>Une erreur non prevue (exception non interceptee, plantage d'ecran) : gardee le plus longtemps.</summary>
    public const string Grave = "grave";

    /// <summary>Une erreur interceptee par le code et journalisee (jamais avalee, SOC-017 CA03).</summary>
    public const string Erreur = "erreur";

    /// <summary>Une trace technique detaillee (avertissement) : purgee la premiere.</summary>
    public const string Avertissement = "avertissement";
}

/// <summary>D'ou vient une ligne du journal technique.</summary>
public static class OriginesErreur
{
    public const string Ecran = "ecran";
    public const string Serveur = "serveur";
    public const string Traitement = "traitement";
}

/// <summary>Les etats publies d'un traitement (SOC-011 CA01, ceux des services de la V2 dans ETAT_SERVICE).</summary>
public static class EtatsTraitement
{
    /// <summary>Demarre, rien a faire.</summary>
    public const string EnMarche = "en marche";

    /// <summary>Traite un element de sa file.</summary>
    public const string TravauxEnCours = "travaux en cours";

    /// <summary>Demarre, des elements attendent dans sa file.</summary>
    public const string EnAttente = "en attente";

    /// <summary>Arrete sur une erreur : il attend la relance de l'exploitant ; sa file est gardee.</summary>
    public const string EnErreur = "en erreur";

    /// <summary>Arrete : il attend la relance de l'exploitant ; sa file est gardee.</summary>
    public const string Arrete = "arrete";
}

/// <summary>L'etat de la base V3 d'un greffe, vu par la supervision.</summary>
public static class SantesGreffe
{
    public const string Ok = "ok";

    /// <summary>La base V3 n'est pas encore creee (catalogue, SOC-008) : rien a surveiller, aucune alerte.</summary>
    public const string PasCreee = "pas creee";

    /// <summary>La base ne repond pas : alerte.</summary>
    public const string Injoignable = "injoignable";

    /// <summary>La base a moins de migrations jouees que la base commune : alerte.</summary>
    public const string MigrationsEnRetard = "migrations en retard";

    /// <summary>Jamais verifiee (supervision pas encore passee).</summary>
    public const string NonVerifiee = "non verifiee";
}

/// <summary>Les noms que la supervision donne a ce qui n'est pas un traitement de fond : la partie serveur elle-meme, et
/// les « traitements » nommes par les alertes d'un greffe (sa base, la numerotation de l'ancien RIG).</summary>
public static class TraitementsSupervision
{
    public const string PartieServeur = "Partie serveur";
    public const string BaseDuGreffe = "Base V3 du greffe";
    public const string Numerotation = "Numérotation de l'ancien RIG";
}

/// <summary>Les regles d'alerte (SOC-011 CA02, NUM-007 CA05).</summary>
public static class ReglesAlerte
{
    public const string TraitementEnErreur = "traitement-en-erreur";
    public const string FileBloquee = "file-bloquee";
    public const string BaseInjoignable = "base-injoignable";
    public const string MigrationsEnRetard = "migrations-en-retard";
    public const string EchecNumerotation = "echec-numerotation";
}

/// <summary>Une erreur non prevue de l'ecran, envoyee au serveur (SOC-017 T03). Le compte n'y est pas : le serveur
/// le prend de la session Windows, pas de ce que l'ecran declare.</summary>
/// <param name="Programme">Ce qui a echoue : la methode du pont (« EcranCompteurs.Rendre »).</param>
/// <param name="Suivi">L'identifiant d'un parcours deja commence (une erreur du serveur recue par l'ecran) ; null : nouveau.</param>
/// <param name="Poste">Le serveur RDS ou tourne l'ecran.</param>
public sealed record ErreurEcran(string Programme, string Message, string? Type, string? Pile, string? CodeGreffe, string? Suivi, string? Poste);

/// <summary>Ce qu'un agent recoit d'une erreur : son identifiant de suivi et une phrase pour l'assistance ; jamais le detail.</summary>
public sealed record SuiviErreur(string Suivi, string Message);

/// <summary>Une ligne du journal technique (SOC-017 CA01), lue par l'exploitation d'Amitel. Instant en UTC.</summary>
public sealed record ErreurTechnique(
    long Id,
    string Suivi,
    DateTime Instant,
    string Niveau,
    string Origine,
    string? CodeGreffe,
    string? Compte,
    string Serveur,
    string Programme,
    string? Type,
    string Message,
    string? Pile);

/// <summary>L'etat d'un greffe : sa base V3 et les echecs de numerotation venus de l'ancien RIG (Base\v2\echecs.sql).</summary>
/// <param name="Echecs">Echecs de numerotation comptes sur l'instance pour sa base V2 ; null s'ils n'ont pas pu etre lus.</param>
/// <param name="EchecsDetail">Pourquoi les echecs n'ont pas pu etre lus, ou la cause du dernier.</param>
public sealed record EtatGreffe(
    string Code,
    string Nom,
    bool EstOutreMer,
    string Serveur,
    string Sante,
    string? Detail,
    int? MigrationsJouees,
    string? DerniereMigration,
    int? Echecs,
    DateTime? PremierEchec,
    DateTime? DernierEchec,
    string? EchecsDetail,
    DateTime? VerifieLe);

/// <summary>L'etat publie d'un traitement de fond (SOC-011 CA01), par le serveur qui le fait tourner.</summary>
/// <param name="FileBaisseLe">La derniere fois que sa file a baisse (ou etait vide) : la regle de file bloquee part de la.</param>
public sealed record EtatTraitement(
    string Serveur,
    string Traitement,
    string Etat,
    int EnCours,
    int EnAttente,
    int EnErreur,
    string? CodeGreffe,
    string? Message,
    DateTime PublieLe,
    DateTime? FileBaisseLe);

/// <summary>Une alerte envoyee a l'exploitation d'Amitel (SOC-011 CA02) : elle nomme le greffe, le traitement et l'erreur.</summary>
/// <param name="EnvoyeeLe">Quand le canal l'a recue ; null tant qu'il ne l'a pas prise (elle sera renvoyee).</param>
/// <param name="FermeeLe">Quand la cause a disparu ; null tant qu'elle dure.</param>
public sealed record AlerteExploitation(
    int Id,
    string Regle,
    string? CodeGreffe,
    string Traitement,
    string Erreur,
    DateTime OuverteLe,
    DateTime? EnvoyeeLe,
    string? Canal,
    DateTime? FermeeLe);

/// <summary>Ce que voit l'exploitant (SOC-011 CA03) : lu dans la base commune, sans se connecter a aucun serveur de greffe.</summary>
/// <param name="DernierPassage">La derniere verification des greffes ; null si la supervision n'est pas encore passee.</param>
public sealed record EtatSupervision(
    DateTime? DernierPassage,
    EtatGreffe[] Greffes,
    EtatTraitement[] Traitements,
    AlerteExploitation[] Alertes);

/// <summary>Relancer un traitement (SOC-011 CA04).</summary>
public sealed record DemandeRelance(string Traitement);

/// <summary>Ce qu'a fait une relance : l'etat avant, et ce qui l'attendait en file (rien n'est perdu).</summary>
public sealed record ResultatRelance(string Traitement, string EtatAvant, string EtatApres, int EnFile);
