namespace RigV3Desktop;

// ============================================================
// Arbre documentaire de l'affaire (esprit VGED de RIG v2).
//
// Structure et libelles LUS dans RIG v2 :
//   D:\RigApplication\Source\RIG\DLL\Processus\PROC_VGED\Nodes\
//     Dossier RCS\FolderDossierRCS.cs, Actes\FolderActes.cs ("Depots
//     d'actes"), Bilans\FolderBilans.cs ("Depots des comptes annuels"),
//     Formalites\FolderFormalites.cs + SubfolderFormalite.cs,
//     Inscriptions\FolderInscriptions.cs ("Inscriptions privileges"),
//     Judiciaire\FolderJudiciaire.cs > Ordonnances du President\
//     FolderOrdonnances.cs > FolderDemandeur.cs / FolderDefendeur.cs,
//     Ordonnances de prorogation\, Bordereaux de requisition\,
//     Bordereau mandataire\, Non inscrits\, Pieces jointes\.
//   Colonnes de l'arbre : PROC_VDOSSIER\Control\TreeviewGed.Designer.cs
//     colDocuments / colDateCreation ("Date de depot...") / colNbPages.
//
// Rien n'est branche sur une base : ces valeurs montrent le
// comportement de l'ecran, pas un dossier de greffe reel.
// ============================================================

/// <summary>
/// Un noeud de l'arbre GED. Un dossier porte des enfants, une piece n'en
/// porte pas : c'est le meme type, comme les Node de RIG v2.
/// </summary>
public sealed record NoeudGed(string Libelle, string Icone, string Date, int Pages,
                              string Extension, string Origine, string Apercu,
                              NoeudGed[] Enfants, int NiveauRequis = 0)
{
    public bool EstPiece => Enfants.Length == 0 && Extension.Length > 0;

    /// <summary>Nombre de pieces de ce noeud, sous-dossiers compris.</summary>
    public int NbPieces => EstPiece ? 1 : Enfants.Sum(e => e.NbPieces);

    public IEnumerable<NoeudGed> Descendants()
    {
        foreach (var e in Enfants)
        {
            yield return e;
            foreach (var pf in e.Descendants()) yield return pf;
        }
    }
}

/// <summary>
/// Une ligne du tableau des editions. Colonnes reprises une a une de
/// RIG v2 : PROC_TABLEAU_LOT3\Processus\TreeviewEditions.cs
/// (_DicoColonnesLot2) — Destinataire, agrafage, nom du document, envoi
/// au coffre, tribunal digital, envoi par courriel, Ex., imprimer,
/// imprimante, file ou imprimante, apercu avant impression, recto/verso,
/// file differee, impression supplementaire.
/// </summary>
public sealed record LigneEdition(string Destinataire, string NomDocument,
                                  bool Agrafage, bool CoffreFort, bool TribunalDigital,
                                  bool EnvoiParMail, int Exemplaires, bool Imprimer,
                                  string Imprimante, string FileImprimante,
                                  bool AffAvantImpression, bool RectoVerso,
                                  string FileDifferee, bool ImpressionSupplementaire,
                                  int Niveau, bool EstGroupe, DetailEdition? Detail);

/// <summary>
/// Le detail du destinataire selectionne. Champs et libelles LUS dans
/// RIG v2 : PROC_TABLEAU_LOT3\Processus\OPE_DESTINATAIRE.Designer.cs
/// (MetierTitre de chaque controle).
/// </summary>
public sealed record DetailEdition(string Designation, string AttentionDe,
                                   string AdressePostale, string TypePapier,
                                   string Telephone, string Siren,
                                   string NatureCoffreFort, string CoffreFort,
                                   string NatureTribunalDigital, string TribunalDigital,
                                   string ErreurTribunalDigital, string Courriel,
                                   bool ConserverPjOrigine, string Observation);

public static class ArbreDemo
{
    private const string DateEdition = "10/09/2026";

    private static NoeudGed Piece(string libelle, string ext, string date, int pages,
                                  string origine, string apercu = "")
        => new(libelle, "piece", date, pages, ext, origine, apercu, []);

    private static NoeudGed Doss(string libelle, string icone, params NoeudGed[] enfants)
        => new(libelle, icone, "", 0, "", "", "", enfants);

    /// <summary>
    /// Un dossier reserve : il ne s'affiche qu'a partir du niveau d'acces
    /// indique. v2 filtre les dossiers de la VGED sur les droits du login
    /// (UTILISATEUR / profil) ; ici le niveau est une donnee de demo.
    /// </summary>
    private static NoeudGed DossReserve(string libelle, string icone, int niveauRequis,
                                        params NoeudGed[] enfants)
        => new(libelle, icone, "", 0, "", "", "", enfants, niveauRequis);

    /// <summary>Types de papier controles dans OPE_DESTINATAIRE.cs (v2).</summary>
    public static readonly string[] TypesPapier = ["NORMAL", "ENTETE", "PREIMPRIME", "RECYCLE"];

    public static readonly string[] Imprimantes =
        [@"\\srv-print\GREFFE-01", @"\\srv-print\GREFFE-02", "Apercu PDF"];

    public static readonly string[] FilesDifferees =
        ["Production (PS)", "Nuit (differe)", "Sans file"];

    /// <summary>
    /// L'arbre de l'affaire. Racine = le dossier RCS, comme dans v2 ou la
    /// racine porte numero + denomination + Siren.
    /// </summary>
    public static NoeudGed Racine(string numero, string denomination, string siren)
        => Doss($"{numero} {denomination}" + (siren.Length > 0 ? $"   (Siren : {siren})" : ""),
                "dossier",
            Doss("Dossier RCS", "dossier",
                Piece("Statuts signes", "PDF", "12/03/2024", 14, "Depot guichet",
                    "STATUTS DE LA SOCIETE\n\nArticle 1 - Forme\nLa societe est une societe par actions simplifiee regie par les dispositions du Code de commerce.\n\nArticle 2 - Objet\nPrestation de services informatiques.\n\nArticle 3 - Denomination\nAMITEL SAS."),
                Piece("Liste des souscripteurs", "PDF", "12/03/2024", 2, "Depot guichet",
                    "LISTE DES SOUSCRIPTEURS\n\nNom / Actions / Montant\nDUPONT Jean   600   6 000 EUR\nMARTIN Claire 400   4 000 EUR")),
            Doss("Depots d'actes", "actes",
                Doss("Depot n 2024/0141", "sous-dossier",
                    Piece("Bail commercial", "PDF", "12/03/2024", 9, "Depot guichet",
                        "DEPOT D'ACTE N 2024/0141\n\nNature : bail commercial\nEntre SCI DU PARC, bailleur, et AMITEL SAS, preneur.\nLocaux a usage de bureaux sis 12 rue des Lilas."))),
            Doss("Depots des comptes annuels", "bilans"),
            Doss("Formalites", "formalites",
                Doss("Formalite n 2024/00218", "sous-dossier",
                    Piece("Demande d'immatriculation M0", "PDF", "12/03/2024", 4, "Teletransmission",
                        "DEMANDE D'IMMATRICULATION - CERFA M0\n\nDenomination : AMITEL SAS\nForme : Societe par actions simplifiee\nCapital : 10 000 EUR\nSiege : 12 rue des Lilas"),
                    Piece("Declaration de non-condamnation", "PDF", "12/03/2024", 1, "Teletransmission",
                        "DECLARATION SUR L'HONNEUR DE NON-CONDAMNATION\n\nJe soussigne DUPONT Jean, ne le 4 avril 1978 a Lyon, declare n'avoir fait l'objet d'aucune condamnation penale ni sanction civile ou administrative de nature a m'interdire de gerer."))),
            DossReserve("Signalements de divergences BE", "divergences", 1),
            DossReserve("Inscriptions privileges", "inscriptions", 1,
                Doss("Inscription n 2024/0007", "sous-dossier",
                    Piece("Etat des inscriptions", "PDF", "14/03/2024", 1, "Edition greffe",
                        "ETAT DES INSCRIPTIONS\n\nAucun privilege, nantissement ni protet inscrit a ce jour."))),
            DossReserve("Ordonnances de prorogation", "dossier", 2),
            Doss("Bordereaux de requisition", "dossier",
                Piece("Bordereau de numerisation", "PDF", "12/03/2024", 1, "Numerisation",
                    "BORDEREAU DE NUMERISATION D'UN DOSSIER\n\nSite de numerisation : greffe 9999\nNombre de pages : 31\nDate : 12/03/2024")),
            DossReserve("Bordereau mandataire", "dossier", 2),
            Doss("Non inscrits", "dossier"),
            Doss("Pieces jointes", "pj",
                Piece("Piece d'identite du president", "JPG", "12/03/2024", 1, "Numerisation",
                    "[Numerisation d'une carte nationale d'identite]"),
                Piece("Extrait Kbis", "PDF", "14/03/2024", 2, "Edition greffe",
                    "EXTRAIT KBIS\n\nGreffe du tribunal de commerce 9999\nIdentifiant : 903 118 776 RCS\nDenomination : AMITEL SAS\nForme juridique : Societe par actions simplifiee\nCapital : 10 000 EUR")),
            Doss("Divers", "dossier"),
            DossReserve("Judiciaire", "judiciaire", 2,
                Doss("Ordonnances", "sous-dossier",
                    Doss("Demandeur", "partie",
                        Piece("2021IP00530-IP-PRESIDENT-23/09/2021 11:39:13", "PDF",
                              "23/09/2021", 3, "Edition greffe",
                              "ORDONNANCE DU PRESIDENT\n\nRequete du demandeur - injonction de payer.")),
                    Doss("Defendeur", "partie",
                        Piece("2024OP01735-OPHP-PDT-02/05/2024 16:22:57", "PDF", "02/05/2024", 1, "Edition greffe", ""),
                        Piece("2024OP00098-OPHP-PDT-19/01/2024 13:27:28", "PDF", "19/01/2024", 1, "Edition greffe", ""),
                        Piece("2023OP02936-OPHP-PDT-13/09/2023 11:25:00", "PDF", "13/09/2023", 1, "Edition greffe", ""),
                        Piece("2022J00114-JUG-GONON-11/07/2023 17:50:58", "PDF", "11/07/2023", 2, "Edition greffe", ""),
                        Piece("2022J00114-conclusions-BEYLE-20/04/2023", "PDF", "20/04/2023", 58, "Depot guichet", ""),
                        Piece("2022J00114-COUR-BEYLE-20/04/2023", "PDF", "20/04/2023", 2, "Depot guichet", ""),
                        Piece("2021OP01112-OPHP-PDT-28/07/2021 11:55:45", "PDF", "28/07/2021", 6, "Edition greffe", "")))));

    /// <summary>
    /// Le tableau des editions de l'affaire, dans la forme de v2 : une
    /// ligne de processus, un groupe par dossier d'edition, puis un
    /// destinataire par ligne.
    /// </summary>
    public static LigneEdition[] Editions(string numero, string denomination)
    {
        static LigneEdition Groupe(string libelle, int niveau)
            => new(libelle, "", false, false, false, false, 0, false, "", "", false, false,
                   "", false, niveau, true, null);

        static LigneEdition Dest(string destinataire, string document, DetailEdition detail,
                                 bool coffre = false, bool mail = false, bool td = false,
                                 int ex = 1, bool imprimer = true)
            => new(destinataire, document, false, coffre, td, mail, ex, imprimer,
                   @"\\srv-print\GREFFE-01", "Production (PS)", false, false,
                   "Production (PS)", false, 2, false, detail);

        return
        [
            Groupe("Processus : MPC", 0),
            Groupe($"{numero} {denomination}", 1),
            Dest("Monsieur le Directeur Departemental des Finances Publiques (Tresor public general)",
                 $"Publicite Tresorerie Generale (ouverture) : 1 LIR - {DateEdition} (EP n 16)",
                 new("Monsieur le Directeur Departemental des Finances Publiques", "",
                     "Pole de la gestion fiscale\nFiscalite des professionnels et du recouvrement\n9 Boulevard Joseph Vallier BP 1629",
                     "NORMAL", "", "", "", "3801_GRENOBLE_DDFIP", "", "Pas de tribunal",
                     "Le telephone ou le courriel n'est pas defini", "", false, ""),
                 coffre: true),
            Dest("Registre du Commerce et des Societes (Registre du commerce)",
                 $"Publicite RCS (ouverture) : 1 LIR - {DateEdition} (EP n 16)",
                 new("Greffe du tribunal de commerce", "",
                     "Registre du commerce et des societes\n9999 GREFFE",
                     "NORMAL", "", "", "", "", "", "Pas de tribunal", "", "", false, "")),
            Dest("Monsieur le Procureur Guillaume GEORGES (Procureur)",
                 $"Publicite Procureur (LJI - Coffre) : 1 LIR - {DateEdition} (EP n 16)",
                 new("Monsieur le Procureur de la Republique", "Guillaume GEORGES",
                     "Parquet du tribunal judiciaire\nPlace Firmin Gautier",
                     "ENTETE", "04 76 44 00 00", "", "Requisitoire", "3801_PARQUET", "",
                     "Pas de tribunal", "", "parquet@justice.fr", false, ""),
                 coffre: true, mail: true),
            Dest("Me SERRANO Philippe (Liquidateur judiciaire LSD)",
                 $"Derniere Prise (LJI - LIQ - Coffre) : 1 LIR - {DateEdition} (EP n 16)",
                 new("Maitre SERRANO Philippe", "",
                     "Mandataire judiciaire\n12 rue de la Republique",
                     "NORMAL", "04 76 12 34 56", "", "Mandat", "3801_MANDATAIRE", "",
                     "Pas de tribunal", "", "serrano@mandataire.fr", true, "Copie a conserver"),
                 coffre: true, mail: true),
            Dest("Mme SIVERA Brigitte (Juge commissaire titulaire)",
                 $"Information par mail du JC - Amiel : 1 LIR - {DateEdition} (EP n 16)",
                 new("Madame SIVERA Brigitte", "Juge commissaire", "Tribunal de commerce",
                     "NORMAL", "", "", "", "", "", "Pas de tribunal", "",
                     "b.sivera@greffe.fr", false, ""),
                 mail: true, imprimer: false),
            Dest("OFFICE SPECIAL DE PUBLICITE (Bodacc)",
                 $"Duplicata BODACC : 1 LIR - {DateEdition} (EP n 16)",
                 new("Office special de publicite", "",
                     "Direction de l'information legale et administrative\n26 rue Desaix",
                     "NORMAL", "", "", "", "", "", "Pas de tribunal", "", "", false, "")),
            Dest("Les Affiches de Grenoble et du Dauphine (Journal annonces legales)",
                 $"Duplicata JAL : 1 LIR - {DateEdition} (EP n 16)",
                 new("Les Affiches de Grenoble et du Dauphine", "",
                     "Journal d'annonces legales\n1 place Victor Hugo",
                     "NORMAL", "", "", "", "", "", "Pas de tribunal", "",
                     "annonces@affiches.fr", false, "")),
            Groupe("Facture du dossier", 1),
            Dest(denomination, $"Facture : {denomination}",
                 new(denomination, "Service comptabilite", "12 rue des Lilas\n38000 GRENOBLE",
                     "NORMAL", "04 76 00 00 00", "903 118 776", "", "", "", "Pas de tribunal",
                     "", "compta@amitel.fr", false, ""))
        ];
    }
}

/// <summary>
/// Qui voit quoi dans l'arbre de l'affaire. Un dossier marque d'un
/// NiveauRequis ne se montre qu'aux logins dont le niveau d'acces est au
/// moins egal. Les elements ajoutes a la main (bouton « Nouveau ») se
/// rendent a la suite des enfants du dossier, sans toucher au jeu de
/// demonstration.
/// </summary>
public static class AccesGed
{
    public static IReadOnlyList<NoeudGed> EnfantsVisibles(
        NoeudGed parent, int niveauAcces,
        IReadOnlyDictionary<NoeudGed, List<NoeudGed>>? ajouts = null)
    {
        var enfants = parent.Enfants.AsEnumerable();
        if (ajouts is not null && ajouts.TryGetValue(parent, out var ajoutes))
            enfants = enfants.Concat(ajoutes);
        return enfants.Where(e => e.NiveauRequis <= niveauAcces).ToArray();
    }

    /// <summary>Un dictionnaire d'ajouts qui distingue deux noeuds par leur identite.</summary>
    public static Dictionary<NoeudGed, List<NoeudGed>> NouveauxAjouts()
        => new(ReferenceEqualityComparer.Instance);

    /// <summary>Un dossier cree depuis l'arbre (bouton « Nouveau »).</summary>
    public static NoeudGed NouveauDossier(string libelle)
        => new(libelle, "dossier", "", 0, "", "", "", [], 0);

    /// <summary>Une piece creee depuis l'arbre (bouton « Nouveau »).</summary>
    public static NoeudGed NouvellePiece(string libelle, string extension, string date)
        => new(libelle, "piece", date, 1, extension, "Ajout manuel", "", [], 0);
}
