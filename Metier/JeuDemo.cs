namespace RigV3Desktop;

// ============================================================
// Jeu de demonstration des ecrans de procedure.
//
// Il vivait dans Home.razor, qui depassait 2 100 lignes : chaque nouvel
// ecran y ajoutait ses donnees, ses criteres et sa grille. Les donnees
// sont ici, les ecrans dans Components/Ecrans/.
//
// Rien n'est branche sur une base : ces valeurs servent a montrer le
// comportement des ecrans, pas a refleter un greffe reel.
// ============================================================

/// <summary>Une demande, colonnes de PROC_DEMANDE/DemandeDataGridView.cs.</summary>
public sealed record DemandeRcs(string Numero, string Nom, string Dossier, string Siren,
                                string Client, string Traitement, string Etat, bool EnCours,
                                DateTime Creation2, string Validation, string Utilisateur,
                                bool Alerte, string Statut)
{
    public string Creation => Creation2.ToString("dd/MM/yyyy");
}

/// <summary>Une arrivee de courrier, colonnes de la table ARRIVEE.</summary>
public sealed record Arrivee(string Numero, DateTime Instant, string Mode, string Nom,
                             string References, string Case_, bool PaiementJoint,
                             string NumDemande, string TypeDemande, string Utilisateur,
                             bool Numerise, bool Attestation)
{
    public string Horodatage => Instant.ToString("dd/MM/yyyy HH:mm");
    public string ModeLisible =>
        JeuDemo.ModesArrivee.FirstOrDefault(m => m.Code == Mode).Libelle ?? Mode;
}

/// <summary>
/// Une entreprise trouvee par la recherche de requisition, colonnes de
/// PROC_REQUI/Operations/OPE_REQUI_RESULTATS.cs (_ConfigurerColonnes).
/// </summary>
public sealed record EntrepriseRequi(string Id, string Siren, string Gestion, string Designation,
                                     string TypeAdresse, string Adresse, string Type,
                                     bool NonInscrit, int NbInscriptions, bool Radie,
                                     bool ProcedureCollective, string CodePostal, string Ville,
                                     string Dirigeant, string Activite);

/// <summary>
/// Une ligne de panier : une commande de documents groupee par entreprise et
/// adresse. Portee de PROC_REQUI/Models/Commande.cs (classe Commande).
/// </summary>
public sealed class CommandeRequi
{
    public int NumCommande;
    public EntrepriseRequi Societe = null!;

    public int NbKBis;
    public int NbHistorique;
    public int NbDAS;
    public int NbCertifNonFaillite;
    public int NbCertifNonRcs;
    public int NbCertifNonRsac;
    /// <summary>Certificat de NON OPPOSITION (famille EDI « Non opposition » de la
    /// fiche 275) : l ecran commandait les trois autres certificats mais pas celui-ci.</summary>
    public int NbCertifNonOppo;
    public int NbDjCopie;
    public int NbDjExpedition;
    public int NbNavire;
    public int NbBeInterne;
    public int NbBeExterne;
    public int NbInscr;

    public DateTime DateCnf = JeuDemo.Aujourdhui;
    public DateTime DateCnRcs = JeuDemo.Aujourdhui;
    public DateTime DateCnRsac = JeuDemo.Aujourdhui;
    public DateTime DateCnOppo = JeuDemo.Aujourdhui;
    public string NomSci = string.Empty;

    /// <summary>Nom du requerant (le « requi ») pour le compte de qui la commande
    /// est passee. Fiche 275 : il doit figurer sur la commande, il est reporte sur
    /// les certificats edites.</summary>
    public string NomRequi = string.Empty;

    /// <summary>Date de TRAVAIL de la commande : la date a laquelle les pieces sont
    /// etablies. Elle sert de reference a la fraicheur ci-dessous.</summary>
    public DateTime DateTravail = JeuDemo.Aujourdhui;

    /// <summary>Au-dela de ce nombre de jours, le travail est considere comme perime
    /// et doit etre refait. Seuil NON defini par la fiche 275 : valeur d attente,
    /// a corriger ici seulement quand le metier l aura tranchee.</summary>
    public const int JoursFraicheur = 30;

    /// <summary>Age du travail en jours, a la date du jour de la maquette.</summary>
    public int AgeTravailJours => (int)(JeuDemo.Aujourdhui.Date - DateTravail.Date).TotalDays;

    /// <summary>Vrai tant que le travail est frais (age dans les JoursFraicheur).</summary>
    public bool TravailFrais => AgeTravailJours >= 0 && AgeTravailJours <= JoursFraicheur;

    /// <summary>Codes d inscription coches, au sens de la grille des 37 cases.</summary>
    public readonly HashSet<string> Inscriptions = new();

    /// <summary>Quantite demandee PAR type d inscription (clef = libelle de la case).
    /// Fiche 275 : une seule quantite globale ne dit pas combien de chaque type.
    /// NbInscr reste la somme, pour ne rien changer aux regles portees de v2.</summary>
    public readonly Dictionary<string, int> QteParInscription = new();

    /// <summary>KBIS interdit pour les non inscrits (AutoriserKBis de frmRechLET).</summary>
    public bool KBisPossible => !Societe.NonInscrit;

    /// <summary>Certificat de non faillite bloque si une procedure collective court
    /// ou si le dossier est radie (BlocageCNF de frmRechLET).</summary>
    public bool CnfPossible => !Societe.ProcedureCollective && !Societe.Radie;
}

/// <summary>Une case de la grille des inscriptions : son libelle affiche et les
/// codes TYPE_INSCRIPTION qu elle couvre (mapping de OPE_REQUI_ARTICLES.cs).</summary>
/// <summary>
/// Une commande passee sur le site web et arrivee par EDI (fiche 275).
/// Cote v2, l entree existe deja : Source/Edi/DLL/EdiMetier/EdiCmdWeb, avec les
/// types ET-CREQ / ET-PREQ et les trois familles endettement / faillite /
/// non opposition. Ici on ne porte QUE ce que l ecran doit afficher pour que le
/// greffier traite la commande ; rien n est branche sur la base.
/// </summary>
public sealed class CommandeWeb
{
    public string Numero = string.Empty;
    public DateTime Recue;
    public string Demandeur = string.Empty;
    /// <summary>Endettement, Faillite ou Non opposition.</summary>
    public string Famille = string.Empty;
    /// <summary>Code EDI de la commande (ET-CREQ / ET-PREQ).</summary>
    public string CodeEdi = string.Empty;
    /// <summary>La societe visee, telle que le demandeur l a saisie sur le web.</summary>
    public string Societe = string.Empty;
    public string Siren = string.Empty;
    /// <summary>« A traiter » ou « Traitee ».</summary>
    public string Etat = "A traiter";

    /// <summary>D ou vient la commande : « Site local » (le site du greffe) ou
    /// « Infogreffe ». Fiche 275, « Nom de la demande ».</summary>
    public string Origine = "Site local";

    /// <summary>Nom de la demande au format exact de la fiche 275 :
    /// « Site local ou Infogreffe - N de commande et date ». C est lui qui
    /// prerempli le nom du requi quand la commande est rapprochee d un dossier.</summary>
    public string NomDemande => Origine + " - " + Numero + " - " + Recue.ToString("dd/MM/yyyy");
}

public sealed record CaseInscription(string Libelle, string Codes, bool Stricte);

/// <summary>Un acte numerise, colonnes de RESULTAT/DataGridviewActes.cs.</summary>
public sealed record ActeNum(int Rang, string Dossier, string Depot, string Denomination,
                             string Categorie, string Abrege, string Description,
                             DateTime DateDepot, DateTime DateActe, DateTime? DateCloture,
                             bool Confidentiel, bool Demat, string Anomalie,
                             bool Numerisable)
{
    public string Depose => DateDepot.ToString("dd/MM/yyyy");
    public string Acte => DateActe.ToString("dd/MM/yyyy");
    public string Cloture => DateCloture?.ToString("dd/MM/yyyy") ?? string.Empty;
    public string CategorieLisible =>
        JeuDemo.CategoriesDepot.FirstOrDefault(c => c.Code == Categorie).Libelle ?? Categorie;
}

/// <summary>
/// Les deux domaines de la VGED de RIG v2 : ce sont les seules branches
/// que PROC_VGED\ETP_VGED\OPE_VGED.cs (_InitTreeview) ajoute a l'arbre
/// d'une affaire. Ils servent ici de droits portes par le login.
/// </summary>
public static class DomaineGed
{
    public const string Rcs = "RCS";
    public const string Judiciaire = "JUD";

    /// <summary>L'ENDETTEMENT : injonctions de payer, creances et
    /// recouvrement. Troisieme metier du greffe a cote du RCS et du
    /// judiciaire (demande utilisateur du 2026-09-17).</summary>
    public const string Endettement = "END";
}

/// <summary>
/// Un login de la maquette et les DOMAINES auxquels il donne acces. Les
/// domaines sont ceux des deux branches de la VGED de v2 : « RCS » et
/// « JUD » (judiciaire). Ce sont eux qui decident des dossiers visibles
/// dans l'arbre de l'affaire (voir AccesGed).
/// </summary>
public sealed record CompteRig(string Login, string Nom, string[] Domaines, string Profil,
                              bool PeutModifierDirect = true, bool PeutValider = false)
{
    /// <summary>Ce login doit faire valider ses modifications par un superieur.</summary>
    public bool SoumetAValidation => !PeutModifierDirect;

    /// <summary>Les domaines, tels qu'ils s'ecrivent dans le selecteur de login.</summary>
    public string DomainesLisibles =>
        Domaines.Length == 0 ? "aucun domaine" : string.Join(" + ", Domaines);

    /// <summary>Les initiales du rond de profil (« RV » pour rvilain). Un login
    /// s'ecrit initiale du prenom + nom : ses deux premieres lettres les donnent,
    /// le compte n'a pas d'autre champ pour le nom de famille.</summary>
    public string Initiales =>
        (Login.Length >= 2 ? Login[..2] : Login).ToUpperInvariant();

    /// <summary>Le compte tel que les demandes l'inscrivent dans leur colonne
    /// « Utilisateur » : « R. VILAIN » pour rvilain. Meme regle que
    /// <see cref="Initiales"/> (initiale du prenom, puis le nom).</summary>
    public string Signature =>
        Login.Length < 2 ? Login.ToUpperInvariant()
                         : $"{char.ToUpperInvariant(Login[0])}. {Login[1..].ToUpperInvariant()}";
}

/// <summary>
/// Un EVENEMENT RECURRENT en attente sur une affaire : le code de
/// l evenement RCS (CODE_EVENEMENT_RCS) et le dossier concerne. C est LUI
/// qui declenche une alerte de tache, et non un dossier choisi au hasard
/// dans la file : une alerte task driven doit correspondre a un evenement
/// reel, sinon elle envoie le greffier faire un travail que personne n a
/// demande.
/// </summary>
/// <param name="Signale">Quand l evenement est arrive : sert a montrer le
/// plus ancien en premier, comme le reste de la file.</param>
public sealed record EvenementAttendu(string Code, string Dossier, DateTime Signale);

public static class JeuDemo
{
    public static readonly DateTime Aujourdhui = new(2026, 9, 9);

    /// <summary>Delai legal de traitement d'une demande, en jours.</summary>
    public const int DelaiLegalJours = 7;

    /// <summary>
    /// Les demandes encore en cours au-dela du delai legal, la plus ancienne en
    /// tete. Source UNIQUE (18/11/2026) : la tache « hors delai » (AlertesGreffe),
    /// la mesure de la tuile Performance (MesuresPerf) et la liste que l'accueil
    /// deplie sous la tuile chiffree « Hors delai legal » la lisent toutes trois.
    /// Avant, chacun refaisait le filtre avec sa propre copie du delai : un
    /// chiffre pouvait differer de la liste posee juste dessous. Elle vit ICI
    /// parce que chaque banc qui compile l'un de ces fichiers compile aussi
    /// celui-ci (PerfCheck n'embarque pas AlertesGreffe.cs).
    /// </summary>
    public static DemandeRcs[] DemandesHorsDelai()
        => Demandes.Where(d => d.EnCours && (Aujourdhui - d.Creation2).TotalDays > DelaiLegalJours)
                   .OrderBy(d => d.Creation2)
                   .ToArray();

    /// <summary>Jours entiers ecoules depuis la creation de la demande (le « J+n »).</summary>
    public static int JoursDepuis(DemandeRcs d) => (int)(Aujourdhui - d.Creation2).TotalDays;

    /// <summary>
    /// Les evenements recurrents qui attendent d etre traites. Le code doit
    /// correspondre a une recette connue (RecettesTaches) : sans recette,
    /// l alerte n aurait aucun champ a ouvrir.
    /// </summary>
    public static readonly EvenementAttendu[] EvenementsAttendusBase =
    [
        new("DENO", "004176", Aujourdhui.AddDays(-2)),
        new("DENO", "2026B01067", Aujourdhui.AddDays(-1)),
        // DPER et DSTE menent a la MEME tache « changer un dirigeant » : deux
        // codes RCS selon la forme de la societe (societes de personnes /
        // societes de capitaux), une seule recette cote greffier.
        new("DSTE", "004182", Aujourdhui.AddDays(-3)),
        new("DPER", "004168", Aujourdhui.AddDays(-5)),
        // Les trois evenements trouves dans les requetes CFE de RIG v2
        // (_PM (M).sql l.35-36, _PP (P).sql l.43) : sigle, forme juridique,
        // nom d usage. Un evenement par dossier connu, pour que la file
        // montre plusieurs taches distinctes et pas une seule repetee.
        new("SIGL", "004179", Aujourdhui.AddDays(-4)),
        new("FORM", "004151", Aujourdhui.AddDays(-6)),
        new("NUSA", "004144", Aujourdhui.AddDays(-7)),
        // L etat civil de la personne physique : quatre evenements de plus,
        // dont les effets sont lus dans CODE_EVENEMENT_RCS.
        new("NOPA", "004168", Aujourdhui.AddDays(-9)),
        new("PRENO", "004179", Aujourdhui.AddDays(-10)),
        new("PSEU", "004182", Aujourdhui.AddDays(-11)),
        new("COLL", "004155", Aujourdhui.AddDays(-8)),
        // Evenement REEL du referentiel dont AUCUNE recette n existe encore
        // (AACT, « Adjonction d activite de l etablissement principal »,
        // lu sur RIG_AMIENS le 16/09/2026). Il doit rester SANS alerte,
        // sinon le clic ouvrirait une tache vide. C est le cas limite que le
        // banc de controle verifie -- il a remplace COLL, qui a desormais sa
        // recette : un cas limite couvert par une recette ne teste plus rien.
        // Les TROIS evenements les plus frequents du greffe dont la base
        // declare les champs (comptage 12 mois sur EVENEMENT_RCS, 16/09/2026 :
        // RADI 797, DISS 569, CESM 530).
        new("DISS", "004151", Aujourdhui.AddDays(-13)),
        new("CESM", "004155", Aujourdhui.AddDays(-14)),
        new("RADI", "004179", Aujourdhui.AddDays(-15)),
        new("AACT", "004144", Aujourdhui.AddDays(-12))
    ];

    /// <summary>
    /// Les comptes de demonstration. Les domaines se parametrent par
    /// login : ce sont eux qui decident des branches visibles dans l'arbre
    /// de l'affaire (voir AccesGed). Aucun annuaire n'est branche.
    /// </summary>
    public static readonly CompteRig[] Comptes =
    [
        // Le greffier est le SUPERIEUR : lui seul valide les modifications soumises.
        new("rvilain", "Raphael",
            [DomaineGed.Rcs, DomaineGed.Judiciaire, DomaineGed.Endettement], "Greffier",
            PeutModifierDirect: true, PeutValider: true),
        new("cmartin", "Claire", [DomaineGed.Rcs], "Redacteur RCS"),
        new("pjudic", "Pauline", [DomaineGed.Judiciaire], "Redacteur judiciaire"),
        // Profil SANS ecriture directe : ses gestes partent en demande de
        // validation, ils ne touchent la GED qu'une fois valides.
        new("astagiaire", "Alex", [DomaineGed.Rcs, DomaineGed.Judiciaire],
            "Redacteur soumis a validation", PeutModifierDirect: false, PeutValider: false),
        // Le metier de l'ENDETTEMENT : il ne voit ni le registre ni les
        // audiences, seulement les injonctions de payer et les creances.
        new("mdupuis", "Marc", [DomaineGed.Endettement], "Redacteur endettement"),
    ];

    /// <summary>
    /// Le compte d'un login. Un login inconnu retombe sur le compte le
    /// MOINS dote : un droit ne s'obtient jamais par defaut.
    /// </summary>
    public static CompteRig Compte(string login)
        => Comptes.FirstOrDefault(c => c.Login == login)
           ?? Comptes.OrderBy(c => c.Domaines.Length).First();

    // ---- LE CAS DU JOUR (SimulationEdi) fait partie des listes ----
    // Un flux EDI arrive ce matin, sa demande et son evenement. Ce ne sont
    // pas des lignes de demonstration a part : les ecrans, les alertes et les
    // bancs de controle lisent CES listes-ci, donc le cas se rencontre la ou
    // le greffier travaille deja -- le courrier, la file, l accueil.

    /// <summary>Les evenements recurrents en attente, cas du jour compris.</summary>
    public static EvenementAttendu[] EvenementsAttendus
        => [.. EvenementsAttendusBase, SimulationEdi.Evenement];

    /// <summary>Les demandes de la file, cas du jour en tete (la plus recente).
    /// Une demande attribuee pendant la session (<see cref="Attribuer"/>) porte
    /// son nouvel agent : tous les ecrans lisent la meme liste.</summary>
    public static DemandeRcs[] Demandes
        => [.. new[] { SimulationEdi.Demande }.Concat(DemandesBase).Select(Attribuee)];

    // Qui s'occupe de quelle demande, change pendant la session par « Me
    // l'attribuer » (palette Ctrl K, 19/11/2026). Aucune base n'est branchee :
    // l'attribution vit le temps de l'application, comme le reste du jeu.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Attributions = new();

    /// <summary>Confie une demande a un agent (sa <see cref="CompteRig.Signature"/>).</summary>
    public static void Attribuer(string numeroDemande, string utilisateur)
        => Attributions[numeroDemande] = utilisateur;

    private static DemandeRcs Attribuee(DemandeRcs d)
        => Attributions.TryGetValue(d.Numero, out var u) ? d with { Utilisateur = u } : d;

    /// <summary>Le courrier arrive, cas du jour en tete.</summary>
    public static Arrivee[] Arrivees
        => [SimulationEdi.Arrivee, .. ArriveesBase];

    public static readonly string[] EtatsDemande =
        ["En saisie", "En controle", "Validee", "Rejetee"];

    /// <summary>Modes LUS dans dbo.MODE_ARRIVEE (RIG_AMIENS, 09/09/2026).</summary>
    public static readonly (string Code, string Libelle)[] ModesArrivee =
    [
        ("ACC", "Accueil"),
        ("AUT", "Autre"),
        ("MAL", "e-Mail"),
        ("EDI", "Edi"),
        ("FAX", "Fax"),
        ("PTT", "Poste"),
        ("REC", "Recommande"),
        ("TAX", "Taxi-colis")
    ];

    /// <summary>
    /// Categories LUES dans dbo.CATEGORIE_DEPOT (RIG_AMIENS, 09/09/2026) :
    /// extrait des plus courantes sur les 50 que compte la table.
    /// </summary>
    public static readonly (string Code, string Libelle)[] CategoriesDepot =
    [
        ("A",   "Depots actes"),
        ("D",   "Depots actes PP"),
        ("E",   "Depots actes EIRL"),
        ("F",   "Depots actes AC"),
        ("B",   "Depots des comptes annuels"),
        ("G",   "Depots comptes AC"),
        ("K",   "Depots comptes PP"),
        ("N",   "Beneficiaire effectif"),
        ("P",   "Ordonnance de prorogation"),
        ("PV",  "Proces-Verbal"),
        ("DIV", "Depot divers")
    ];

    public static readonly DemandeRcs[] DemandesBase =
    [
        new("2026B01067",   "Immatriculation - AMIENS BILLARDS",      "2026B01067",   "109 678 235", "AMIENS BILL.",      "PROC_DEMANDE",   "En saisie",   true,  Aujourdhui.AddDays(-1),  "",           "R. VILAIN", false, "action"),
        new("004176", "Modification - SCI DU PARC",        "004176", "812 445 906", "SCI DU PARC", "PROC_DEMANDE",   "En controle", true,  Aujourdhui.AddDays(-9),  "",           "M. DURAND", true,  "alerte"),
        new("004179", "Radiation - MARTIN EURL",           "004179", "521 004 332", "MARTIN EURL", "PROC_ARCHIRAD",  "En controle", true,  Aujourdhui.AddDays(-4),  "",           "R. VILAIN", false, "attente"),
        new("004182", "Immatriculation - AMIENS BILLARDS",      "004182", "109 678 235", "AMIENS BILL.",      "PROC_DEMANDE",   "En saisie",   true,  Aujourdhui.AddDays(-2),  "",           "R. VILAIN", false, "action"),
        new("004168", "Immatriculation - BATIPRO SARL",    "004168", "440 927 115", "BATIPRO",     "PROC_DEMANDE",   "Validee",     false, Aujourdhui.AddDays(-14), "29/08/2026", "M. DURAND", false, "ok"),
        new("004155", "Depot d actes - TRANSNORD SA",      "004155", "318 552 007", "TRANSNORD",   "PROC_ACTES_NUM", "Validee",     false, Aujourdhui.AddDays(-21), "22/08/2026", "C. BENIER", false, "ok"),
        new("004151", "Depot de bilan - TRANSNORD SA",     "004151", "318 552 007", "TRANSNORD",   "PROC_DEPOTJUD",  "En controle", true,  Aujourdhui.AddDays(-11), "",           "C. BENIER", true,  "alerte"),
        new("004144", "Cloture - LES TROIS CHENES SARL",   "004144", "789 221 460", "LES 3 CHENES","PROC_CLOT",      "Rejetee",     false, Aujourdhui.AddDays(-25), "",           "M. DURAND", false, "attente"),
        new("004138", "Arrivee du courrier - lot 2026-36", "004138", "",            "",            "PROC_ARRIVEE",   "Validee",     false, Aujourdhui.AddDays(-30), "12/08/2026", "C. BENIER", false, "ok"),
        new("004131", "BODACC - envoi hebdomadaire",       "004131", "",            "",            "PROC_BODACC",    "Validee",     false, Aujourdhui.AddDays(-33), "09/08/2026", "R. VILAIN", false, "ok")
    ];

    public static readonly Arrivee[] ArriveesBase =
    [
        new("A-2026-0912", new(2026, 9, 9, 8, 41, 0),  "PTT", "SCI DU PARC",          "LR 4A 118 992 7",  "C-12", false, "004176", "Modification",    "M. DURAND", true,  true),
        new("A-2026-0911", new(2026, 9, 9, 8, 22, 0),  "ACC", "AMIENS BILLARDS",           "depot guichet",    "C-01", true,  "2026B01067",   "Immatriculation", "R. VILAIN", true,  true),
        new("A-2026-0910", new(2026, 9, 8, 16, 55, 0), "EDI", "BATIPRO SARL",         "EDI-2026-44012",   "",     false, "004168", "Immatriculation", "M. DURAND", true,  false),
        new("A-2026-0909", new(2026, 9, 8, 15, 10, 0), "REC", "TRANSNORD SA",         "1A 220 447 118 3", "C-07", false, "004151", "Depot de bilan",  "C. BENIER", false, true),
        new("A-2026-0908", new(2026, 9, 8, 11, 30, 0), "MAL", "MARTIN EURL",          "greffe@… 08/09",   "",     false, "004179", "Radiation",       "R. VILAIN", true,  false),
        new("A-2026-0907", new(2026, 9, 7, 14, 5, 0),  "FAX", "LES TROIS CHENES",     "fax 03 22 xx",     "C-04", false, "004144", "Cloture",         "M. DURAND", false, false),
        new("A-2026-0906", new(2026, 9, 7, 9, 48, 0),  "TAX", "CABINET LEROY",        "colis 8821",       "C-09", true,  "",       "Depot d actes",   "",          false, false),
        new("A-2026-0905", new(2026, 9, 4, 17, 2, 0),  "AUT", "TRIBUNAL DE COMMERCE", "navette interne",  "",     false, "",       "Judiciaire",      "C. BENIER", true,  false)
    ];

    public static readonly ActeNum[] Actes =
    [
        new(1,  "2026B01067",   "D-2026-1188", "AMIENS BILLARDS",       "A",   "STAT",  "Statuts constitutifs",             new(2026, 9, 8),  new(2026, 3, 12),  null,              false, true,  "",                       true),
        new(2,  "2026B01067",   "D-2026-1188", "AMIENS BILLARDS",       "A",   "LSOUS", "Liste des souscripteurs",          new(2026, 9, 8),  new(2026, 3, 12),  null,              false, true,  "",                       true),
        new(3,  "004176", "D-2026-1181", "SCI DU PARC",      "A",   "PVAG",  "PV assemblee generale",            new(2026, 9, 2),  new(2026, 8, 28),  null,              false, true,  "page 3 illisible",       true),
        new(4,  "004151", "D-2026-1170", "TRANSNORD SA",     "B",   "BILAN", "Comptes annuels 2025",             new(2026, 8, 29), new(2025, 12, 31), new(2025, 12, 31), true,  true,  "",                       true),
        new(5,  "004151", "D-2026-1170", "TRANSNORD SA",     "B",   "RESUL", "Compte de resultat 2025",          new(2026, 8, 29), new(2025, 12, 31), new(2025, 12, 31), true,  false, "attente numerisation",   true),
        new(6,  "004168", "D-2026-1152", "BATIPRO SARL",     "N",   "BEN",   "Declaration beneficiaire effectif", new(2026, 8, 26), new(2026, 8, 20), null,              true,  true,  "",                       true),
        new(7,  "004144", "D-2026-1140", "LES TROIS CHENES", "PV",  "PVCLO", "PV de cloture",                    new(2026, 8, 15), new(2026, 8, 10),  null,              false, false, "",                       true),
        new(8,  "004179", "D-2026-1131", "MARTIN EURL",      "DIV", "DIV",   "Courrier de radiation",            new(2026, 8, 12), new(2026, 8, 11),  null,              false, true,  "",                       true),
        new(9,  "004155", "D-2026-1119", "TRANSNORD SA",     "A",   "BAIL",  "Bail commercial (original papier)", new(2026, 8, 4),  new(2026, 7, 30), null,              false, false, "format non numerisable", false),
        new(10, "004155", "D-2026-1119", "TRANSNORD SA",     "P",   "ORDPR", "Ordonnance de prorogation",        new(2026, 8, 4),  new(2026, 7, 28),  null,              false, false, "reliure cousue",         false)
    ];

    // ---- PROC_REQUI : requisitions / commande de documents (fiche 275) ----
    // Les 37 cases d inscription, dans l ORDRE et avec les libelles de
    // PROC_REQUI/Operations/OPE_REQUI_ARTICLES.Designer.cs (_ConfigChk), et les
    // codes TYPE_INSCRIPTION du mapping des infobulles (OPE_REQUI_ARTICLES.cs).
    // « Stricte » = case prise en compte par GestionQteInscr (25 sur 37).
    public static readonly CaseInscription[] CasesInscription =
    [
        new("SS",   "S",         true),  new("PT",  "T",       true),
        new("PN",   "N, J, A, B", true), new("PV",  "V",       true),
        new("PNOM", "O",         true),  new("CB",  "C",       true),
        new("L",    "L, R",      true),  new("F",   "F",       true),
        new("W",    "W",         true),  new("IN",  "I",       true),
        new("HF",   "H",         true),  new("X",   "X",       true),
        new("D",    "D",         true),  new("PECN", "P, E",   true),
        new("K",    "K",         true),  new("U",   "U",       true),
        new("Q",    "Q",         true),  new("G",   "G",       true),
        new("SC",   "Z",         true),  new("JF",  "JF",      true),
        new("NP",   "NP, Y",     true),  new("NJ",  "",        false),
        new("FA",   "",          false), new("WA",  "WA",      true),
        new("SF",   "SF",        true),  new("SM",  "SM",      true),
        new("SP",   "SP",        true),  new("ST",  "",        false),
        new("VNO",  "",          false), new("STP", "",        false),
        new("GSD",  "",          false), new("Parts", "",      false),
        new("CLR",  "",          false), new("Fluvial", "",    false),
        new("Maritime", "",      false), new("NAIJ", "",       false),
        new("Z",    "",          false)
    ];

    /// <summary>Le fonds interroge par la recherche de requisition. Jeu d exemple :
    /// aucune base n est branchee dans la maquette.</summary>
    public static readonly EntrepriseRequi[] EntreprisesRequi =
    [
        new("1",  "402 118 447", "2026B01067", "AMIENS BILLARDS",            "Siege",           "12 rue des Trois Cailloux, 80000 AMIENS",   "PM", false, 14, false, false, "80000", "AMIENS",   "DUPONT Jean",      "Commerce de detail"),
        new("2",  "402 118 447", "2026B01067", "AMIENS BILLARDS",            "Etablissement",   "4 avenue du General Leclerc, 80090 AMIENS",  "PM", false,  3, false, false, "80090", "AMIENS",   "DUPONT Jean",      "Commerce de detail"),
        new("3",  "812 445 906", "004176",     "SCI DU PARC",                "Siege",           "18 rue du Parc, 80480 DURY",                 "PM", false,  6, false, false, "80480", "DURY",     "LEROY Anne",       "Location immobiliere"),
        new("4",  "318 552 007", "004151",     "TRANSNORD SA",               "Siege",           "ZI Nord, 80440 BOVES",                       "PM", false, 21, false, true,  "80440", "BOVES",    "MARCHAND Paul",    "Transport routier"),
        new("5",  "318 552 007", "004155",     "TRANSNORD SA",               "Etablissement",   "Quai de Somme, 80100 ABBEVILLE",             "PM", false,  2, false, true,  "80100", "ABBEVILLE","MARCHAND Paul",    "Transport routier"),
        new("6",  "790 220 114", "004168",     "BATIPRO SARL",               "Siege",           "7 rue de la Briqueterie, 80000 AMIENS",      "PM", false,  9, false, false, "80000", "AMIENS",   "BERTIN Sophie",    "Travaux de maconnerie"),
        new("7",  "",            "004179",     "MARTIN Eric (EURL)",         "Siege",           "2 place Gambetta, 80200 PERONNE",            "PP", false,  4, true,  false, "80200", "PERONNE",  "MARTIN Eric",      "Conseil de gestion"),
        new("8",  "823 445 902", "004144",     "LES TROIS CHENES",           "Siege",           "31 route de Doullens, 80260 VILLERS",        "PM", false, 11, true,  false, "80260", "VILLERS",  "CARON Luc",        "Restauration"),
        new("9",  "",            "",           "CABINET LEROY (non inscrit)", "Correspondance", "9 rue Saint-Leu, 80000 AMIENS",              "PP", true,   0, false, false, "80000", "AMIENS",   "LEROY Anne",       "Activite juridique"),
        new("10", "901 553 226", "004188",     "NORD MARINE SAS",            "Siege",           "Bassin a flot, 80230 SAINT-VALERY",          "PM", false,  5, false, false, "80230", "SAINT-VALERY", "VASSEUR Marc", "Reparation navale")
    ];

    /// <summary>Les commandes web arrivees par EDI et pas encore traitees.
    /// Jeu d exemple : la lecture reelle de CMD_WEB n est pas branchee ici.</summary>
    public static CommandeWeb[] CommandesWeb() =>
    [
        new() { Origine = "Site local", Numero = "CW-2026-000412", Recue = new DateTime(2026, 9, 8, 8, 14, 0),  Demandeur = "BANQUE DU NORD",        Famille = "Endettement",     CodeEdi = "ET-CREQ", Societe = "AMIENS BILLARDS", Siren = "402 118 447" },
        new() { Origine = "Infogreffe", Numero = "CW-2026-000413", Recue = new DateTime(2026, 9, 8, 9, 2, 0),   Demandeur = "MAITRE CARPENTIER",     Famille = "Faillite",        CodeEdi = "ET-PREQ", Societe = "TRANSNORD SA",    Siren = "318 552 007" },
        new() { Origine = "Infogreffe", Numero = "CW-2026-000414", Recue = new DateTime(2026, 9, 9, 14, 38, 0), Demandeur = "SCP DUBOIS ET ASSOCIES",Famille = "Non opposition",  CodeEdi = "ET-CREQ", Societe = "BATIPRO SARL",    Siren = "790 220 114" },
        new() { Origine = "Site local", Numero = "CW-2026-000415", Recue = new DateTime(2026, 9, 10, 7, 55, 0), Demandeur = "BANQUE DU NORD",        Famille = "Endettement",     CodeEdi = "ET-CREQ", Societe = "NORD MARINE SAS", Siren = "901 553 226" }
    ];
}
