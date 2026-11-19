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
//     Pieces jointes\FolderPiecesJointes.cs.
//   L'arbre REEL n'a que DEUX branches : OPE_VGED.cs,
//     _InitTreeview() n'ajoute que « Dossier RCS » (si l'affaire a un
//     dossier RCS) et « Judiciaire » (si elle a une instance). Les
//     Nodes\ « Non inscrits », « Bordereaux de requisition » et
//     « Bordereau mandataire » servent a des ouvertures SEPAREES de la
//     VGED (_model.Nodes.Clear()), pas a cet arbre, et « Ordonnances
//     de prorogation » n'y est jamais ajoute.
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
                              NoeudGed[] Enfants, string DomaineRequis = "",
                              string Fichier = "")
{
    /// <summary>
    /// Vrai PDF a afficher, relatif a wwwroot (ex. « documents/facture.pdf »).
    /// Vide = apercu texte de demonstration.
    /// </summary>
    public bool APdf => Fichier.Length > 0;

    public bool EstPiece => Enfants.Length == 0 && Extension.Length > 0;

    /// <summary>
    /// Une FICHE de donnees (« Informations du dossier ») : ni dossier ni piece.
    /// Elle ne se deplie pas et ouvre un formulaire dans la vue du milieu.
    /// </summary>
    public bool EstFiche => Icone == ArbreDemo.IconeFicheDossier;

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

    /// <summary>
    /// Une piece adossee a un VRAI document de greffe (PDF telecharge de RIG
    /// v2, dossier 1994B03178, le 15/09/2026), servi depuis wwwroot/documents.
    /// </summary>
    private static NoeudGed PiecePdf(string libelle, string date, int pages,
                                     string origine, string fichier)
        => new(libelle, "piece", date, pages, "PDF", origine, "", [], "", "documents/" + fichier);

    /// <summary>Icone qui marque le noeud « Informations du dossier ».</summary>
    public const string IconeFicheDossier = "fiche-dossier";

    /// <summary>Le KBIS en vigueur, premier noeud de « Dossier RCS ».</summary>
    public const string LibelleKbisAJour = "Kbis a jour";

    private static NoeudGed Fiche(string libelle)
        => new(libelle, IconeFicheDossier, "", 0, "", "", "", []);

    /// <summary>
    /// La fiche sur laquelle se place le bloc du milieu quand on DEPLIE ce
    /// dossier : « Dossier RCS » > « Informations du dossier », « Formalites »
    /// > « Informations des Formalites » (demandes du 15/09/2026). Null sinon.
    /// </summary>
    public static NoeudGed? FicheADeplier(NoeudGed dossier)
        => dossier.DomaineRequis == DomaineRcs || dossier.Libelle == "Formalites"
           || dossier.Libelle == "Depots d'actes"
            ? dossier.Enfants.FirstOrDefault(e => e.EstFiche)
            : null;

    /// <summary>
    /// Categorie des editions produites par une formalite. Pendant des
    /// documents « EDITIONS » de la VGED v2 : OPE_VGED.cs, _RechercherFormalites,
    /// lignes d'edition dont le modele a R_CODEMODELE_VDAS (DocFormaliteCodeVdas).
    /// </summary>
    public const string LibelleDocumentsGeneres = "Documents generes lors de la formalite";

    private static NoeudGed Doss(string libelle, string icone, params NoeudGed[] enfants)
        => new(libelle, icone, "", 0, "", "", "", enfants);

    /// <summary>
    /// Une racine de domaine. v2 ne construit l'arbre de la VGED qu'en deux
    /// branches (PROC_VGED\ETP_VGED\OPE_VGED.cs, _InitTreeview) :
    /// « Dossier RCS » si l'affaire a un dossier RCS, « Judiciaire » si elle
    /// a une instance. Le domaine porte ici le droit du login.
    /// </summary>
    private static NoeudGed Domaine(string libelle, string icone, string domaine,
                                    params NoeudGed[] enfants)
        => new(libelle, icone, "", 0, "", "", "", enfants, domaine);

    /// <summary>Les deux domaines de la VGED de v2 (voir DomaineGed).</summary>
    public const string DomaineRcs = DomaineGed.Rcs;
    public const string DomaineJudiciaire = DomaineGed.Judiciaire;

    /// <summary>Types de papier controles dans OPE_DESTINATAIRE.cs (v2).</summary>
    public static readonly string[] TypesPapier = ["NORMAL", "ENTETE", "PREIMPRIME", "RECYCLE"];

    public static readonly string[] Imprimantes =
        [@"\\srv-print\GREFFE-01", @"\\srv-print\GREFFE-02", "Apercu PDF"];

    public static readonly string[] FilesDifferees =
        ["Production (PS)", "Nuit (differe)", "Sans file"];

    /// <summary>
    /// L'arbre de l'affaire, dans la forme REELLE de v2. Lu dans
    /// PROC_VGED\ETP_VGED\OPE_VGED.cs (_InitTreeview) : deux branches et
    /// pas plus — « Dossier RCS » (Pieces jointes, Depots d'actes, Depots
    /// des comptes annuels, Formalites, Inscriptions privileges) et
    /// « Judiciaire » (Ordonnances > Demandeur / Defendeur). « Non
    /// inscrits » et « Bordereau de requisition » n'en font pas partie :
    /// v2 les ouvre dans un arbre SEPARE (_model.Nodes.Clear()), et
    /// « Ordonnances de prorogation » n'est jamais ajoute a l'arbre.
    /// Libelles pris un a un dans Nodes\*\Folder*.cs.
    /// </summary>
    // Affaire REELLE du greffe d Amiens, recopiee telle quelle depuis la base
    // RIG v2 (RIG_AMIENS@SQL-PROD\PROD, tables DOSSIER_RCS + ENTREPRISE,
    // DSSRC_ID_RPRTR = 119534, lecture du 11/09/2026) : dossier 2026B01067,
    // AMIENS BILLARDS, SARL, Siren 109 678 235, immatriculee le 11/09/2026,
    // code NAF 4649Z. Les pieces et leur contenu restent, eux, de la
    // demonstration : la maquette n a aucun acces base.
    public static NoeudGed Racine(string numero, string denomination, string siren,
                                  bool avecDossierRcs = true, bool avecJudiciaire = true)
    {
        var branches = new List<NoeudGed>();
        if (avecDossierRcs) branches.Add(BrancheDossierRcs());
        if (avecJudiciaire) branches.Add(BrancheJudiciaire());
        return Doss($"{numero} {denomination}" + (siren.Length > 0 ? $"   (Siren : {siren})" : ""),
                    "dossier", [.. branches]);
    }

    /// <summary>
    /// Une affaire PUREMENT judiciaire : instance, procedure collective,
    /// injonction de payer ou mesure, SANS dossier RCS au greffe. v2 fait
    /// exactement cela — PROC_VGED\ETP_VGED\OPE_VGED.cs, _InitTreeview()
    /// n ajoute la branche « Dossier RCS » que si l affaire A un dossier
    /// RCS, et la branche « Judiciaire » que si elle a une instance. Une
    /// societe non immatriculee au greffe (ou immatriculee ailleurs)
    /// n ouvre donc qu une seule branche.
    /// </summary>
    public static NoeudGed RacineJudiciaire(string numero, string denomination, string siren = "")
        => Racine(numero, denomination, siren, avecDossierRcs: false);

    /// <summary>La branche « Dossier RCS » (v2 : FolderDossierRCS.cs).</summary>
    private static NoeudGed BrancheDossierRcs()
        => Domaine("Dossier RCS", "dossier", DomaineRcs,
            // Formulaire des donnees du dossier (REPRIAC, REPRIPP, REPRIPP2,
            // REPRIPM2, XACORRI, XBCORRI) : voir Metier/InformationsDossier.cs.
            // Le KBIS EN VIGUEUR, en tete (demande du 18/09/2026) ; ceux des
            // formalites restent l'historique fige a leur validation.
            Piece(LibelleKbisAJour, "PDF", DateTime.Today.ToString("dd/MM/yyyy"), 1, "Edition greffe"),
            Fiche("Informations du dossier"),
            Piece("Statuts signes", "PDF", "12/03/2024", 14, "Depot guichet",
                "STATUTS DE LA SOCIETE\n\nArticle 1 - Forme\nLa societe est une societe a responsabilite limitee regie par les dispositions du Code de commerce.\n\nArticle 2 - Objet\nPrestation de services informatiques.\n\nArticle 3 - Denomination\nAMIENS BILLARDS."),
            Piece("Liste des souscripteurs", "PDF", "12/03/2024", 2, "Depot guichet",
                "LISTE DES SOUSCRIPTEURS\n\nNom / Actions / Montant\nDUPONT Jean   600   6 000 EUR\nMARTIN Claire 400   4 000 EUR"),
            Doss("Pieces jointes", "pj",
                Piece("Piece d'identite du president", "JPG", "12/03/2024", 1, "Numerisation",
                    "[Numerisation d'une carte nationale d'identite]"),
                Piece("Extrait Kbis", "PDF", "14/03/2024", 2, "Edition greffe",
                    "EXTRAIT KBIS\n\nGreffe du tribunal de commerce d Amiens\nIdentifiant : 109 678 235 RCS Amiens - 2026B01067\nDenomination : AMIENS BILLARDS\nForme juridique : Societe a responsabilite limitee\nCapital : 10 000 EUR"),
                PiecePdf("Certificat en matiere de difficultes des entreprises", "15/09/2026", 1,
                    "Edition greffe", "certificat-difficultes-entreprises.pdf"),
                PiecePdf("Document numerise", "23/03/2023", 5,
                    "Numerisation", "document-numerise.pdf")),
            Doss("Depots d'actes", "actes",
                // Champs du processus v2 XDAS : voir Metier/InformationsDepots.cs.
                // fix-ok: ajout demande (pas un correctif) — GedCheck rouge mesure (3 echecs : noeud « Informations des depots » absent sous Depots d'actes) avant ajout, vert apres
                Fiche(InformationsDepots.LibelleNoeud),
                Doss("Depot n 2024/0141", "sous-dossier",
                    Piece("Bail commercial", "PDF", "12/03/2024", 9, "Depot guichet",
                        "DEPOT D'ACTE N 2024/0141\n\nNature : bail commercial\nEntre SCI DU PARC, bailleur, et AMIENS BILLARDS, preneur.\nLocaux a usage de bureaux sis 12 rue des Lilas.")),
                Doss("Depot n A2024/038654", "sous-dossier",
                    PiecePdf("Extrait de proces-verbal du conseil de surveillance du 03/10/2024", "15/10/2024", 3,
                        "Depot guichet", "depot-acte-extrait-pv-conseil-surveillance.pdf"),
                    Doss(LibelleDocumentsGeneres, "editions",
                        PiecePdf("Certificat de depot d'actes", "15/10/2024", 1,
                            "Edition greffe", "certificat-depot-actes.pdf")))),
            Doss("Depots des comptes annuels", "bilans",
                Doss("Depot n B2024/061077", "sous-dossier",
                    PiecePdf("Certificat de depot des comptes annuels", "20/09/2024", 1,
                        "Edition greffe", "certificat-depot-comptes-annuels.pdf"),
                    PiecePdf("Lettre de regularisation du depot", "12/11/2024", 1,
                        "Edition greffe", "lettre-regularisation-comptes.pdf"),
                    PiecePdf("Facture n 24-191776", "12/11/2024", 1,
                        "Edition greffe", "facture-depot-comptes.pdf"))),
                // fix-ok: ajout demande (pas un correctif) — GedCheck rouge mesure « la categorie Formalites commence par la fiche » avant ajout, vert apres
            Doss("Formalites", "formalites",
                // Champs du processus v2 XACTION : voir Metier/InformationsFormalite.cs.
                // (libelle = InformationsFormalite.LibelleNoeud, verifie par GedCheck)
                Fiche("Informations des Formalites"),
                Doss("Formalite n 2024/00218", "sous-dossier",
                    Piece("Demande d'immatriculation M0", "PDF", "12/03/2024", 4, "Teletransmission",
                        "DEMANDE D'IMMATRICULATION - CERFA M0\n\nDenomination : AMIENS BILLARDS\nForme : Societe a responsabilite limitee\nCapital : 10 000 EUR\nSiege : 12 rue des Lilas"),
                    Piece("Declaration de non-condamnation", "PDF", "12/03/2024", 1, "Teletransmission",
                        "DECLARATION SUR L'HONNEUR DE NON-CONDAMNATION\n\nJe soussigne DUPONT Jean, ne le 4 avril 1978 a Lyon, declare n'avoir fait l'objet d'aucune condamnation penale ni sanction civile ou administrative de nature a m'interdire de gerer."),
                    // fix-ok: ajout demande (pas un correctif) — GedCheck rouge attendu avant, vert apres
                    Doss(LibelleDocumentsGeneres, "editions",
                        Piece("Certificat d'inscription", "PDF", "14/03/2024", 1, "Edition greffe",
                            "CERTIFICAT D'INSCRIPTION AU RCS\n\nAMIENS BILLARDS\n109 678 235 RCS Amiens"),
                        Piece("Avis BODACC - immatriculation", "PDF", "14/03/2024", 1, "Edition greffe",
                            "BODACC A - IMMATRICULATION\n\nAMIENS BILLARDS, SARL au capital de 10 000 EUR"))),
                Doss("Formalite n J00092709138", "sous-dossier",
                    PiecePdf("Synthese de depot au Guichet unique", "08/10/2024", 13,
                        "Teletransmission", "formalite-synthese-guichet-unique.pdf"),
                    PiecePdf("Liasse greffe - modification B1", "15/10/2024", 14,
                        "Edition greffe", "formalite-liasse-greffe.pdf"),
                    PiecePdf("Pieces jointes de la formalite", "12/09/2024", 22,
                        "Teletransmission", "formalite-pieces-jointes.pdf"))),
            Doss("Inscriptions privileges", "inscriptions",
                Doss("Inscription n 2024/0007", "sous-dossier",
                    Piece("Etat des inscriptions", "PDF", "14/03/2024", 1, "Edition greffe",
                        "ETAT DES INSCRIPTIONS\n\nAucun privilege, nantissement ni protet inscrit a ce jour."))));

    /// <summary>La branche « Judiciaire » (v2 : FolderJudiciaire.cs).</summary>
    private static NoeudGed BrancheJudiciaire()
        =>
        // ------------------------------------------------------------
        // Branche « Judiciaire ». v2 n y posait que « Ordonnances du
        // President ». Enrichie le 16/09/2026 (demande) d apres les
        // processus de CREATION et de MODIFICATION du judiciaire de
        // RIG v2, libelles LUS en base (RIG_AMIENS@SQL-PROD\PROD,
        // PROCESSUS_ET_FONCTIONALITE, PROCF_CODE_PROC/PROCF_LIBELLE,
        // lecture du 16/09/2026) :
        //   CREATION      CJUD « Proceder a l enrolement de toute affaire »,
        //     EF « proceder a l enrolement role des procedures collectives »,
        //     NPC « creer une nouvelle procedure collective »,
        //     NF « Creer une PC a partir d une instance »,
        //     REPRIPCH « Creation d une procedure collective ancienne »,
        //     EJC « Enroler une instance juge-commissaire ou president en
        //     matiere de PC », CLOT « Examen des clotures de procedures
        //     collectives », OPE « traiter une nouvelle ordonnance
        //     presidentielle », IPE « traiter une nouvelle procedure
        //     d injonction de payer », NI « Creation de non inscrit ».
        //   MODIFICATION  MJUD « Modifier les donnees d une affaire »,
        //     MPC « Modification de procedure collective », DEPPC
        //     « Proceder a un depot sur une procedure collective »,
        //     IPSUIVI « Modification et suivi des injonctions de payer »,
        //     SIGNJUD « Signature electronique des decisions judiciaires »,
        //     ORDOSIGN « Signer des ordonnances en masse », JOIN « Traiter
        //     jonctions et disjonctions », MESIN « traiter une mesure
        //     d instruction ».
        // Les objets ranges ici sont ceux que ces processus ecrivent en
        // base v2 (RigDatabase\dbo\Tables) : INSTANCE, INSTANCE_JUGE,
        // PROCEDURE_COLLECTIVE, PC_ETAT, DEPOT_PC, MESURE,
        // DECISION_JUDICIAIRE. NI (« non inscrit », table NON_INSCRIT)
        // n ouvre PAS de dossier ici : v2 l ouvre dans un arbre SEPARE.
        Domaine("Judiciaire", "judiciaire", DomaineJudiciaire,
            // Enrolement d une affaire contentieuse (CJUD), modifiee par
            // MJUD : table INSTANCE.
            Doss("Instances", "sous-dossier",
                Doss("Instance n 2022J00114 - AMIENS BILLARDS / SCI DU PARC", "sous-dossier",
                    Piece("Assignation en paiement", "PDF", "14/02/2022", 8, "Depot guichet",
                        "ASSIGNATION DEVANT LE TRIBUNAL DE COMMERCE\n\nDemandeur : SCI DU PARC\nDefendeur : AMIENS BILLARDS\nObjet : paiement de loyers commerciaux."),
                    Piece("2022J00114-conclusions-BEYLE-20/04/2023", "PDF", "20/04/2023", 58,
                        "Depot guichet", ""),
                    // JOIN : « Traiter jonctions et disjonctions ».
                    Doss("Jonctions et disjonctions", "sous-dossier",
                        Piece("Jonction avec l instance 2022J00187", "PDF", "05/09/2022", 1,
                            "Edition greffe",
                            "ORDONNANCE DE JONCTION\n\nLes instances 2022J00114 et 2022J00187 sont jointes.")),
                    // MESIN : « traiter une mesure d instruction » (table MESURE).
                    Doss("Mesures d instruction", "sous-dossier",
                        Piece("Designation d expert - M. LEROY", "PDF", "12/10/2022", 2,
                            "Edition greffe",
                            "MESURE D INSTRUCTION\n\nExpert designe : M. LEROY\nMission : evaluer les travaux."),
                        Piece("Rapport d expertise", "PDF", "03/03/2023", 42, "Depot guichet", "")),
                    // SIGNJUD : « Signature electronique des decisions
                    // judiciaires » (table DECISION_JUDICIAIRE).
                    Doss("Decisions judiciaires", "sous-dossier",
                        Piece("2022J00114-JUG-GONON-11/07/2023 17:50:58", "PDF", "11/07/2023", 2,
                            "Edition greffe", ""),
                        Piece("2022J00114-COUR-BEYLE-20/04/2023", "PDF", "20/04/2023", 2,
                            "Edition greffe", "")))),
            // NPC / NF / REPRIPCH (creation), MPC (modification),
            // EF (enrolement du role), CLOT (cloture) : tables
            // PROCEDURE_COLLECTIVE et PC_ETAT.
            Doss("Procedures collectives", "sous-dossier",
                Doss("Procedure n 2024P00042 - Redressement judiciaire", "sous-dossier",
                    Piece("Jugement d ouverture du redressement judiciaire", "PDF",
                        "18/06/2024", 4, "Edition greffe",
                        "JUGEMENT D OUVERTURE\n\nRedressement judiciaire de AMIENS BILLARDS.\nMandataire judiciaire : Me SERRANO.\nPeriode d observation : six mois."),
                    Piece("Etat des creances declarees", "PDF", "20/09/2024", 11,
                        "Edition greffe", ""),
                    // DEPPC : « Proceder a un depot sur une procedure
                    // collective » (table DEPOT_PC).
                    Doss("Depots sur la procedure", "sous-dossier",
                        Piece("Depot du rapport du mandataire", "PDF", "15/11/2024", 9,
                            "Depot guichet", ""),
                        Piece("Depot de l etat des creances", "PDF", "12/12/2024", 6,
                            "Depot guichet", "")),
                    // EJC : « Enroler une instance juge-commissaire ou
                    // president en matiere de PC ».
                    Doss("Instances juge-commissaire", "sous-dossier",
                        Piece("Requete en releve de forclusion", "PDF", "22/10/2024", 3,
                            "Depot guichet", ""),
                        Piece("Ordonnance du juge-commissaire", "PDF", "08/11/2024", 2,
                            "Edition greffe", "")),
                    // CLOT : « Examen des clotures de procedures collectives ».
                    Doss("Cloture", "sous-dossier",
                        Piece("Jugement de cloture pour insuffisance d actif", "PDF",
                            "16/05/2025", 3, "Edition greffe", "")))),
            // IPE (creation) et IPSUIVI (suivi) : les injonctions de payer.
            Doss("Injonctions de payer", "sous-dossier",
                Doss("Requete n 2021IP00530", "sous-dossier",
                    Piece("Requete en injonction de payer", "PDF", "10/09/2021", 3,
                        "Depot guichet", ""),
                    Piece("Ordonnance portant injonction de payer", "PDF", "23/09/2021", 2,
                        "Edition greffe", ""),
                    Piece("Signification par huissier", "PDF", "12/10/2021", 2,
                        "Depot guichet", ""))),
            // OPE (creation) et ORDOSIGN (signature en masse) : la seule
            // branche que v2 posait deja ici.
            Doss("Ordonnances", "sous-dossier",
                Doss("Demandeur", "partie",
                    Piece("2021IP00530-IP-PRESIDENT-23/09/2021 11:39:13", "PDF",
                          "23/09/2021", 3, "Edition greffe",
                          "ORDONNANCE DU PRESIDENT\n\nRequete du demandeur - injonction de payer.")),
                Doss("Defendeur", "partie",
                    PiecePdf("2024OP01500-OPHP-PDT-28/03/2024 14:33:28", "28/03/2024", 4,
                             "Edition greffe", "ordonnance-president.pdf"),
                    Piece("2024OP01735-OPHP-PDT-02/05/2024 16:22:57", "PDF", "02/05/2024", 1, "Edition greffe", ""),
                    Piece("2024OP00098-OPHP-PDT-19/01/2024 13:27:28", "PDF", "19/01/2024", 1, "Edition greffe", ""),
                    Piece("2023OP02936-OPHP-PDT-13/09/2023 11:25:00", "PDF", "13/09/2023", 1, "Edition greffe", ""),
                    Piece("2021OP01112-OPHP-PDT-28/07/2021 11:55:45", "PDF", "28/07/2021", 6, "Edition greffe", ""))));


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
                     "NORMAL", "04 76 00 00 00", "109 678 235", "", "", "", "Pas de tribunal",
                     "", "compta@amitel.fr", false, ""))
        ];
    }
}

/// <summary>
/// Qui voit quoi dans l'arbre de l'affaire. Un dossier marque d'un
/// DomaineRequis (« RCS » ou « JUD ») ne se montre qu'aux logins qui ont
/// ce domaine dans leurs droits : c'est ainsi que v2 decide des branches
/// de la VGED. Les elements ajoutes a la main (bouton « Nouveau ») se
/// rendent a la suite des enfants du dossier, sans toucher au jeu de
/// demonstration.
/// </summary>
public static class AccesGed
{
    public static IReadOnlyList<NoeudGed> EnfantsVisibles(
        NoeudGed parent, IReadOnlyCollection<string> domaines,
        IReadOnlyDictionary<NoeudGed, List<NoeudGed>>? ajouts = null,
        IReadOnlyCollection<NoeudGed>? masques = null,
        RecetteTache? recette = null)
    {
        var enfants = parent.Enfants.AsEnumerable();
        if (ajouts is not null && ajouts.TryGetValue(parent, out var ajoutes))
            enfants = enfants.Concat(ajoutes);
        return enfants
            .Where(e => e.DomaineRequis.Length == 0 || domaines.Contains(e.DomaineRequis))
            // MODE TACHE : une recette active replie les branches qu'elle ne
            // nomme pas. C'est un REPLI, pas une suppression -- le bouton
            // « Afficher tout le dossier » passe recette = null et tout
            // revient, sans qu'aucun noeud ait ete touche.
            .Where(e => recette is null
                        || recette.GardeBranche(parent.Libelle, e.Libelle,
                                               e.EstPiece || e.EstFiche || e.Enfants.Length == 0)
                        // LE CHEMIN VERS UNE PIECE NOMMEE RESTE OUVERT. Sans
                        // cela, une preuve rangee deux niveaux plus bas
                        // (Formalites > Formalite n ... > Documents generes >
                        // avis BODACC) restait invisible, et l'effet qu'elle
                        // atteste paraissait a faire.
                        || MeneAUnePieceGardee(e, recette)
                        // Une PIECE que la recette nomme passe, meme rangee
                        // sous un dossier qui n'est pas une branche declaree :
                        // c'est la piece qui est la preuve, pas son casier.
                        || e.EstPiece && recette.PiecesAttendues.Length > 0
                           && recette.GardePiece(e.Libelle))
            // LE REPLI DESCEND JUSQU'A LA PIECE. Sans cette ligne, le filtre
            // s'arretait a la BRANCHE : la branche gardee rendait TOUTES ses
            // pieces, donc le dossier entier restait a l'ecran. Une FICHE
            // n'est jamais repliee -- c'est elle qui porte les champs de la
            // tache. Repli, pas suppression : recette = null rend tout.
            .Where(e => recette is null || !e.EstPiece || recette.GardePiece(e.Libelle))
            // Un sous-dossier dont PLUS AUCUNE piece n'est gardee se replie a
            // son tour : sinon le filtre a la piece laissait a l'ecran une
            // rangee de dossiers vides, aussi encombrante que le dossier
            // entier. Un dossier qui porte une FICHE reste, toujours.
            .Where(e => recette is null || e.EstPiece || e.EstFiche
                        || PorteQuelqueChose(e, domaines, ajouts, masques, recette))
            // Un noeud supprime depuis l'arbre n'est pas retire du jeu de
            // demonstration (les enfants sont un tableau fige) : il est
            // MASQUE, ce qui vaut suppression pour l'utilisateur et garde
            // l'identite des autres noeuds intacte.
            .Where(e => masques is null || !masques.Contains(e))
            .ToArray();
    }

    /// <summary>
    /// Ce dossier porte-t-il, a n'importe quelle profondeur, une PIECE que la
    /// recette nomme ? Sans recette de pieces (liste vide), la reponse est
    /// non : le repli par branches garde alors son comportement d'origine.
    /// </summary>
    public static bool MeneAUnePieceGardee(NoeudGed dossier, RecetteTache recette)
        => recette.PiecesAttendues.Length > 0
           && !dossier.EstPiece && !dossier.EstFiche
           && dossier.Descendants().Any(d => d.EstPiece && recette.GardePiece(d.Libelle));

    /// <summary>
    /// Ce dossier garde-t-il encore quelque chose sous la tache : une piece
    /// nommee, une fiche, ou un sous-dossier qui en porte ? Un dossier vide
    /// de base (aucun enfant du tout) n'est PAS replie : il montre
    /// « aucune piece », comme dans RIG v2.
    /// </summary>
    public static bool PorteQuelqueChose(
        NoeudGed dossier, IReadOnlyCollection<string> domaines,
        IReadOnlyDictionary<NoeudGed, List<NoeudGed>>? ajouts,
        IReadOnlyCollection<NoeudGed>? masques, RecetteTache recette)
    {
        var tous = EnfantsVisibles(dossier, domaines, ajouts, masques);
        if (tous.Count == 0) return true;
        return EnfantsVisibles(dossier, domaines, ajouts, masques, recette).Count > 0;
    }

    /// <summary>Un ensemble de noeuds qui distingue deux noeuds par leur identite.</summary>
    public static HashSet<NoeudGed> NouveauxMasques()
        => new(ReferenceEqualityComparer.Instance);

    /// <summary>Des libelles reecrits (renommage) indexes par identite de noeud.</summary>
    public static Dictionary<NoeudGed, string> NouveauxLibelles()
        => new(ReferenceEqualityComparer.Instance);

    /// <summary>Un dictionnaire d'ajouts qui distingue deux noeuds par leur identite.</summary>
    public static Dictionary<NoeudGed, List<NoeudGed>> NouveauxAjouts()
        => new(ReferenceEqualityComparer.Instance);

    /// <summary>Un dossier cree depuis l'arbre (bouton « Nouveau »).</summary>
    public static NoeudGed NouveauDossier(string libelle)
        => new(libelle, "dossier", "", 0, "", "", "", []);

    /// <summary>Une piece creee depuis l'arbre (bouton « Nouveau »).</summary>
    public static NoeudGed NouvellePiece(string libelle, string extension, string date)
        => new(libelle, "piece", date, 1, extension, "Ajout manuel", "", []);

    /// <summary>
    /// Les natures proposees par le bouton « Nouveau », dans l'ordre
    /// d'affichage : code interne, libelle par defaut du nouvel element,
    /// icone (classe CSS) et extension (vide = dossier).
    /// </summary>
    public static readonly (string Code, string Titre, string Extension)[] Natures =
    [
        ("dossier",       "Nouveau dossier",       ""),
        ("sous-dossier",  "Nouveau sous-dossier",  ""),
        ("pdf",           "Document PDF",          "PDF"),
        ("jpg",           "Image JPG",             "JPG"),
        ("tif",           "Numerisation TIF",      "TIF"),
        ("courrier",      "Courrier",              "PDF"),
        ("note",          "Note interne",          "PDF"),
        ("ordonnance",    "Ordonnance",            "PDF"),
        ("assignation",   "Assignation",           "PDF"),
        ("statuts",       "Statuts",               "PDF"),
        ("kbis",          "Extrait Kbis",          "PDF"),
        ("identite",      "Piece d'identite",      "PDF"),
        ("facture",       "Facture",               "PDF"),
        ("pv",            "Proces-verbal",         "PDF")
    ];

    /// <summary>Cree l'element correspondant a une nature du choix « Nouveau ».</summary>
    public static NoeudGed NouveauSelonNature(string code, string date)
    {
        var n = Natures.FirstOrDefault(x => x.Code == code, Natures[0]);
        // Le CODE de nature est stocke comme icone du noeud : c'est ainsi que
        // l'arbre retrouve le dessin exact choisi a la creation (une
        // ordonnance reste une ordonnance, pas une feuille anonyme).
        return n.Extension.Length == 0
            ? new(n.Titre, n.Code, "", 0, "", "", "", [])
            : new(n.Titre, n.Code, date, 1, n.Extension, "Ajout manuel", "", []);
    }

    /// <summary>
    /// Le dessin a poser devant un noeud de l'arbre. Un noeud cree porte
    /// deja son code de nature ; ceux du jeu de demonstration portent un
    /// type large (« piece », « judiciaire »), on retombe alors sur leur
    /// extension de fichier.
    /// </summary>
    public static string CodeIcone(NoeudGed noeud)
    {
        if (noeud.EstFiche) return "note";
        if (!noeud.EstPiece) return "dossier";
        if (Natures.Any(n => n.Code == noeud.Icone)) return noeud.Icone;
        return noeud.Extension.ToUpperInvariant() switch
        {
            "JPG" or "JPEG" or "PNG" => "jpg",
            "TIF" or "TIFF" => "tif",
            _ => "pdf"
        };
    }
}

// ============================================================
// Conserver d'une session a l'autre ce que l'utilisateur a change dans
// l'arbre : elements crees, elements masques, elements renommes.
//
// Pourquoi ce n'est pas un simple « enregistre le dictionnaire » : les
// trois collections de l'ecran sont indexees par IDENTITE d'objet
// (ReferenceEqualityComparer, AccesGed.Nouveaux*). Une reference ne
// survit pas a la fermeture de l'application, et l'arbre est reconstruit
// a neuf a chaque ouverture d'affaire (ArbreDemo.Racine). La clef
// ECRITE est donc un CHEMIN de libelles depuis la racine, re-resolu sur
// l'arbre neuf a la relecture.
// ============================================================

/// <summary>Un element cree a la main dans l'arbre, tel qu'il est conserve.</summary>
public sealed class AjoutGedEnregistre
{
    public string Libelle { get; set; } = "";
    public string Icone { get; set; } = "dossier";
    public string Date { get; set; } = "";
    public int Pages { get; set; }
    public string Extension { get; set; } = "";
    public string Origine { get; set; } = "";
}

/// <summary>Ce qu'un utilisateur a change dans l'arbre d'UNE affaire.</summary>
public sealed class ArbreGedEnregistre
{
    /// <summary>Elements crees, par chemin du dossier qui les porte.</summary>
    public Dictionary<string, List<AjoutGedEnregistre>> Ajouts { get; set; } = new();

    /// <summary>Chemins des elements masques (« supprimes » a l'ecran).</summary>
    public List<string> Masques { get; set; } = new();

    /// <summary>Nouveaux libelles (renommage), par chemin.</summary>
    public Dictionary<string, string> Libelles { get; set; } = new();

    /// <summary>Chemins des sous-dossiers REPLIES dans le classeur de l'arbre (« Par dossier »,
    /// web/src/composants/ClasseurArbre.tsx ; demande du 19/11/2026).</summary>
    public List<string> Replies { get; set; } = new();

    public bool Vide() => Ajouts.Count == 0 && Masques.Count == 0 && Libelles.Count == 0 && Replies.Count == 0;
}

public static class PersistanceGed
{
    // Deux freres peuvent porter le MEME libelle : le rang les distingue.
    // La barre oblique separe les segments, elle est donc echappee.
    private static string Segment(string libelle, int rang)
        => libelle.Replace("/", "%2F") + (rang == 0 ? "" : "~" + rang);

    private static IEnumerable<NoeudGed> Tous(
        NoeudGed parent, IReadOnlyDictionary<NoeudGed, List<NoeudGed>>? ajouts)
        => ajouts is not null && ajouts.TryGetValue(parent, out var a)
            ? parent.Enfants.Concat(a) : parent.Enfants;

    /// <summary>
    /// Le chemin de CHAQUE noeud de l'arbre, ajouts compris. Les droits et
    /// les masquages ne filtrent rien ici : un noeud masque doit garder son
    /// chemin, sinon son masquage ne se retrouverait pas.
    /// </summary>
    public static Dictionary<NoeudGed, string> Chemins(
        NoeudGed racine, IReadOnlyDictionary<NoeudGed, List<NoeudGed>>? ajouts = null)
    {
        var chemins = AccesGed.NouveauxLibelles();
        chemins[racine] = "";
        var pile = new Stack<NoeudGed>();
        pile.Push(racine);
        while (pile.Count > 0)
        {
            var parent = pile.Pop();
            var rangs = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in Tous(parent, ajouts))
            {
                rangs.TryGetValue(e.Libelle, out var rang);
                rangs[e.Libelle] = rang + 1;
                if (chemins.ContainsKey(e)) continue;
                chemins[e] = chemins[parent] + "/" + Segment(e.Libelle, rang);
                pile.Push(e);
            }
        }
        return chemins;
    }

    /// <summary>L'etat de l'arbre a ecrire dans accueil.json (<paramref name="replies"/> : les
    /// sous-dossiers replies du classeur, aucun s'il est absent).</summary>
    public static ArbreGedEnregistre Capturer(
        NoeudGed racine,
        IReadOnlyDictionary<NoeudGed, List<NoeudGed>> ajouts,
        IReadOnlyCollection<NoeudGed> masques,
        IReadOnlyDictionary<NoeudGed, string> libelles,
        IReadOnlyCollection<NoeudGed>? replies = null)
    {
        var chemins = Chemins(racine, ajouts);
        var etat = new ArbreGedEnregistre();
        foreach (var couple in ajouts)
        {
            if (couple.Value.Count == 0) continue;
            if (!chemins.TryGetValue(couple.Key, out var chemin)) continue;
            etat.Ajouts[chemin] = couple.Value.Select(n => new AjoutGedEnregistre
            {
                Libelle = n.Libelle, Icone = n.Icone, Date = n.Date,
                Pages = n.Pages, Extension = n.Extension, Origine = n.Origine
            }).ToList();
        }
        foreach (var n in masques)
            if (chemins.TryGetValue(n, out var chemin)) etat.Masques.Add(chemin);
        foreach (var couple in libelles)
            if (chemins.TryGetValue(couple.Key, out var chemin)) etat.Libelles[chemin] = couple.Value;
        foreach (var n in replies ?? [])
            if (chemins.TryGetValue(n, out var chemin)) etat.Replies.Add(chemin);
        // Ordre stable : le fichier ne change pas quand l'etat ne change pas.
        etat.Replies.Sort(StringComparer.Ordinal);
        return etat;
    }

    /// <summary>
    /// Repose l'etat relu sur un arbre NEUF. Les trois collections de
    /// l'ecran sont videes puis re-remplies : un chemin qui ne se retrouve
    /// pas (jeu de demonstration change) est ignore, jamais bloquant.
    /// </summary>
    public static void Appliquer(
        NoeudGed racine, ArbreGedEnregistre? etat,
        Dictionary<NoeudGed, List<NoeudGed>> ajouts,
        HashSet<NoeudGed> masques,
        Dictionary<NoeudGed, string> libelles,
        HashSet<NoeudGed>? replies = null)
    {
        ajouts.Clear();
        masques.Clear();
        libelles.Clear();
        replies?.Clear();
        if (etat is null) return;

        // Du moins profond au plus profond : un element pose SOUS un dossier
        // cree a la main n'a de parent qu'une fois ce dossier recree.
        foreach (var chemin in etat.Ajouts.Keys
                     .OrderBy(c => c.Count(x => x == '/'))
                     .ThenBy(c => c, StringComparer.Ordinal))
        {
            var parent = Chemins(racine, ajouts).FirstOrDefault(p => p.Value == chemin).Key;
            if (parent is null) continue;
            ajouts[parent] = etat.Ajouts[chemin]
                .Select(a => new NoeudGed(a.Libelle, a.Icone, a.Date, a.Pages,
                                          a.Extension, a.Origine, "", []))
                .ToList();
        }

        var parChemin = new Dictionary<string, NoeudGed>(StringComparer.Ordinal);
        foreach (var couple in Chemins(racine, ajouts)) parChemin[couple.Value] = couple.Key;
        foreach (var chemin in etat.Masques)
            if (parChemin.TryGetValue(chemin, out var n)) masques.Add(n);
        foreach (var couple in etat.Libelles)
            if (parChemin.TryGetValue(couple.Key, out var n)) libelles[n] = couple.Value;
        if (replies is not null)
            foreach (var chemin in etat.Replies)
                if (parChemin.TryGetValue(chemin, out var n)) replies.Add(n);
    }
}
