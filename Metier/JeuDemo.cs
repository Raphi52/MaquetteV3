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

/// <summary>Un login de la maquette et son niveau d'acces (0 = le plus bas).</summary>
public sealed record CompteRig(string Login, string Nom, int NiveauAcces, string Profil);

public static class JeuDemo
{
    public static readonly DateTime Aujourdhui = new(2026, 9, 9);

    /// <summary>
    /// Les comptes de demonstration. Le niveau d'acces se parametre par
    /// login : c'est lui qui decide des dossiers visibles dans l'arbre de
    /// l'affaire (voir AccesGed). Aucun annuaire n'est branche.
    /// </summary>
    public static readonly CompteRig[] Comptes =
    [
        new("rvilain", "Raphael", 2, "Greffier"),
        new("cmartin", "Claire", 1, "Redacteur"),
        new("pinvite", "Pauline", 0, "Accueil"),
    ];

    public static CompteRig Compte(string login)
        => Comptes.FirstOrDefault(c => c.Login == login) ?? Comptes[^1];

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

    public static readonly DemandeRcs[] Demandes =
    [
        new("0000",   "Immatriculation - AMITEL SAS",      "0000",   "903 118 776", "AMITEL",      "PROC_DEMANDE",   "En saisie",   true,  Aujourdhui.AddDays(-1),  "",           "R. VILAIN", false, "action"),
        new("004176", "Modification - SCI DU PARC",        "004176", "812 445 901", "SCI DU PARC", "PROC_DEMANDE",   "En controle", true,  Aujourdhui.AddDays(-9),  "",           "M. DURAND", true,  "alerte"),
        new("004179", "Radiation - MARTIN EURL",           "004179", "521 004 332", "MARTIN EURL", "PROC_ARCHIRAD",  "En controle", true,  Aujourdhui.AddDays(-4),  "",           "R. VILAIN", false, "attente"),
        new("004182", "Immatriculation - AMITEL SAS",      "004182", "903 118 776", "AMITEL",      "PROC_DEMANDE",   "En saisie",   true,  Aujourdhui.AddDays(-2),  "",           "R. VILAIN", false, "action"),
        new("004168", "Immatriculation - BATIPRO SARL",    "004168", "440 927 115", "BATIPRO",     "PROC_DEMANDE",   "Validee",     false, Aujourdhui.AddDays(-14), "29/08/2026", "M. DURAND", false, "ok"),
        new("004155", "Depot d actes - TRANSNORD SA",      "004155", "318 552 004", "TRANSNORD",   "PROC_ACTES_NUM", "Validee",     false, Aujourdhui.AddDays(-21), "22/08/2026", "C. BENIER", false, "ok"),
        new("004151", "Depot de bilan - TRANSNORD SA",     "004151", "318 552 004", "TRANSNORD",   "PROC_DEPOTJUD",  "En controle", true,  Aujourdhui.AddDays(-11), "",           "C. BENIER", true,  "alerte"),
        new("004144", "Cloture - LES TROIS CHENES SARL",   "004144", "789 221 460", "LES 3 CHENES","PROC_CLOT",      "Rejetee",     false, Aujourdhui.AddDays(-25), "",           "M. DURAND", false, "attente"),
        new("004138", "Arrivee du courrier - lot 2026-36", "004138", "",            "",            "PROC_ARRIVEE",   "Validee",     false, Aujourdhui.AddDays(-30), "12/08/2026", "C. BENIER", false, "ok"),
        new("004131", "BODACC - envoi hebdomadaire",       "004131", "",            "",            "PROC_BODACC",    "Validee",     false, Aujourdhui.AddDays(-33), "09/08/2026", "R. VILAIN", false, "ok")
    ];

    public static readonly Arrivee[] Arrivees =
    [
        new("A-2026-0912", new(2026, 9, 9, 8, 41, 0),  "PTT", "SCI DU PARC",          "LR 4A 118 992 7",  "C-12", false, "004176", "Modification",    "M. DURAND", true,  true),
        new("A-2026-0911", new(2026, 9, 9, 8, 22, 0),  "ACC", "AMITEL SAS",           "depot guichet",    "C-01", true,  "0000",   "Immatriculation", "R. VILAIN", true,  true),
        new("A-2026-0910", new(2026, 9, 8, 16, 55, 0), "EDI", "BATIPRO SARL",         "EDI-2026-44012",   "",     false, "004168", "Immatriculation", "M. DURAND", true,  false),
        new("A-2026-0909", new(2026, 9, 8, 15, 10, 0), "REC", "TRANSNORD SA",         "1A 220 447 118 3", "C-07", false, "004151", "Depot de bilan",  "C. BENIER", false, true),
        new("A-2026-0908", new(2026, 9, 8, 11, 30, 0), "MAL", "MARTIN EURL",          "greffe@… 08/09",   "",     false, "004179", "Radiation",       "R. VILAIN", true,  false),
        new("A-2026-0907", new(2026, 9, 7, 14, 5, 0),  "FAX", "LES TROIS CHENES",     "fax 03 22 xx",     "C-04", false, "004144", "Cloture",         "M. DURAND", false, false),
        new("A-2026-0906", new(2026, 9, 7, 9, 48, 0),  "TAX", "CABINET LEROY",        "colis 8821",       "C-09", true,  "",       "Depot d actes",   "",          false, false),
        new("A-2026-0905", new(2026, 9, 4, 17, 2, 0),  "AUT", "TRIBUNAL DE COMMERCE", "navette interne",  "",     false, "",       "Judiciaire",      "C. BENIER", true,  false)
    ];

    public static readonly ActeNum[] Actes =
    [
        new(1,  "0000",   "D-2026-1188", "AMITEL SAS",       "A",   "STAT",  "Statuts constitutifs",             new(2026, 9, 8),  new(2026, 3, 12),  null,              false, true,  "",                       true),
        new(2,  "0000",   "D-2026-1188", "AMITEL SAS",       "A",   "LSOUS", "Liste des souscripteurs",          new(2026, 9, 8),  new(2026, 3, 12),  null,              false, true,  "",                       true),
        new(3,  "004176", "D-2026-1181", "SCI DU PARC",      "A",   "PVAG",  "PV assemblee generale",            new(2026, 9, 2),  new(2026, 8, 28),  null,              false, true,  "page 3 illisible",       true),
        new(4,  "004151", "D-2026-1170", "TRANSNORD SA",     "B",   "BILAN", "Comptes annuels 2025",             new(2026, 8, 29), new(2025, 12, 31), new(2025, 12, 31), true,  true,  "",                       true),
        new(5,  "004151", "D-2026-1170", "TRANSNORD SA",     "B",   "RESUL", "Compte de resultat 2025",          new(2026, 8, 29), new(2025, 12, 31), new(2025, 12, 31), true,  false, "attente numerisation",   true),
        new(6,  "004168", "D-2026-1152", "BATIPRO SARL",     "N",   "BEN",   "Declaration beneficiaire effectif", new(2026, 8, 26), new(2026, 8, 20), null,              true,  true,  "",                       true),
        new(7,  "004144", "D-2026-1140", "LES TROIS CHENES", "PV",  "PVCLO", "PV de cloture",                    new(2026, 8, 15), new(2026, 8, 10),  null,              false, false, "",                       true),
        new(8,  "004179", "D-2026-1131", "MARTIN EURL",      "DIV", "DIV",   "Courrier de radiation",            new(2026, 8, 12), new(2026, 8, 11),  null,              false, true,  "",                       true),
        new(9,  "004155", "D-2026-1119", "TRANSNORD SA",     "A",   "BAIL",  "Bail commercial (original papier)", new(2026, 8, 4),  new(2026, 7, 30), null,              false, false, "format non numerisable", false),
        new(10, "004155", "D-2026-1119", "TRANSNORD SA",     "P",   "ORDPR", "Ordonnance de prorogation",        new(2026, 8, 4),  new(2026, 7, 28),  null,              false, false, "reliure cousue",         false)
    ];
}
