using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Journal de caisse » (PROC_JCAISSE) : les encaissements de la periode choisie, par mode de paiement, et leurs totaux. Mode
/// d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 13, 27/11/2026. Lecture seule : RIG v2 n'ecrit rien ici.
///
/// SOURCE (ancien RIG) : PROC_JCAISSE (D:\RigApplication\Source\RIG\DLL\Processus\PROC_JCAISSE ; le Detail OPE de proc_jcaisse.md
/// fait foi). La requete du journal est celle de OPE_CRITERESGENERAUX.GetData (OPE_CRITERESGENERAUX.cs:168-175) :
///   ENCAISSEMENT INNER JOIN UTILISATEUR (ENCMT_ID_UTLST) INNER JOIN VENTILATION (ENCMT_ID_ENCMT = VENTI_ID_ENCMT)
///   INNER JOIN AFFAIRE (VENTI_ID_AFFRE) INNER JOIN CLIENT (VENTI_ID_CLIEN) INNER JOIN MODE_PAIEMENT (ENCMT_MODE_PAIEMENT =
///   R_MODEPAIEME_VALEUR), bornee par ENCMT_DATE_ENCAISSEMENT >= debut 0 h et <= fin 24 h (:175) ;
///   le payeur : CASE R_MODEPAIEME_TYPE WHEN 'RBT' THEN CLIENT.CLIEN_NOM ELSE ENCMT_DESIGNATION_PAYEUR (:168) ;
///   les champs du journal (JournalCaisse.cs:148-159, DllRapportCrystal\RigCrystalFactu) : date, payeur, montant, banque, numero de
///   titre, mode, affaire, login de l'agent.
/// UNE LIGNE PAR ENCAISSEMENT : la requete V2 rend une ligne par ventilation (jointure VENTILATION), chacune portant le montant de
/// l'ENCAISSEMENT (ENCMT_MONTANT, JournalCaisse.cs:151), et l'etat Crystal les regroupe (Expresso_JCaisse*.rpt, non lisible ici).
/// La V3 garde la meme condition -- un encaissement n'entre que s'il a au moins une ventilation sur une affaire et un client de la
/// base (EXISTS) -- et une ligne par encaissement : un encaissement ventile deux fois n'est pas compte deux fois dans les totaux.
/// Le client du payeur « RBT » est celui de sa premiere ventilation. Dans la base V3, CLIENT est la fiche rec.Client (REC-013) et
/// UTILISATEUR le compte soc.Compte (IdV2 = UTLST_ID_UTLST, SOC-001) : lu en LEFT JOIN, comme LecteurEncaissements, pour qu'un
/// encaissement dont l'agent n'est pas repris en V3 reste au journal -- ecart nomme (la V2 l'ecarte par sa jointure interne).
///
/// LA LISTE A L'OUVERTURE : la periode par defaut de la V2, aujourd'hui -> aujourd'hui (OPE_CRITERESGENERAUX._initValeurParDefaut,
/// :35-38). La copie de developpement ne garde que les plus anciens encaissements (2002) : le journal du jour y est vide.
///
/// PARAMETRES (IRechercheEcran ; les Parametres du descripteur, que l'Etat envoie a la base) :
///   « Période » « du|au » : les deux bornes de la V2 (0 h, 24 h) ; une borne vide reprend aujourd'hui, comme la V2 qui restaure la
///     valeur initiale d'une date videe (:298-299, :315-316) ;
///   « Caissier » : l'agent qui a encaisse (ENCMT_ID_UTLST, :184-191), cherche sur son nom, son prenom ou son compte (la V2 coche
///     des agents dans une liste ; la V3 cherche un texte) ;
///   « Modes de paiement » : un ou plusieurs modes separes par des virgules, par code (R_MODEPAIEME_VALEUR) ou libelle
///     (ENCMT_MODE_PAIEMENT IN (...), :203-214) ;
///   « Tri » : les 4 options d'OPE_METHODETRI (OPE_METHODETRI.Designer.cs:73-88), « Par mode de paiement » par defaut
///     (_InitValeur, :56). « Par date » trie par date : la V2 l'envoie au meme etat que « Par montant » (mode 3, :92-97 ; ecart
///     signale par la fiche, proc_jcaisse.md:291) -- la V3 fait ce que le libelle dit.
/// Le champ de recherche cherche dans le payeur, le numero de titre ou la banque.
/// Non repris (nommes) : les gardes « au moins un utilisateur » / « au moins un mode » (:178, :199-200) -- un filtre vide vaut
/// « tous », comme la case « Tous » de la V2 ; la pre-selection du caissier courant (:245-255).
///
/// GROUPES : le TYPE du mode de paiement (MODE_PAIEMENT.R_MODEPAIEME_TYPE, valeurs de RIG_DEV lues le 27/11/2026 : ES, CH, CB, VI,
/// RBT, REG, DIV) -- les sous-totaux « par mode de paiement » de l'etat Mode (Expresso_JCaisseMode.rpt) : especes, cheques, cartes,
/// virements (proc_jcaisse.md:104-107), plus les remboursements, les regularisations et les autres modes.
/// REGLE D'ETAT : ENCMT_DATE_COMPTABILISATION renseignee -> Validee « Comptabilisé » ; sinon remis en banque (ENCMT_REMIS renseigne
/// et different de -1, « pas de remise », OPE_RESULTAT.cs:139 de PROC_RECHENC) -> EnCours « Remis en banque » ; sinon AFaire « À
/// comptabiliser ». fix-ok: EcransGeneresCheck exige les trois etats AFaire / EnCours / Validee de tout descripteur
/// (tests/EcransGeneresCheck/Program.cs:150-152, lu le 27/11/2026) : la remise en banque, etat reel de la colonne, porte EnCours.
/// VALEURS : dans l'ordre des Colonnes du descripteur (Encaissement, Date, Payeur, Mode, Numero de titre, Caissier, Montant), le
/// montant en decimal exact (« 1234.50 », money de la base, jamais un double) : l'Etat en fait les totaux par groupe et en tout.
/// </summary>
internal sealed class LecteurJournalCaisse : IRechercheEcran
{
    public string Procedure => "PROC_JCAISSE";

    internal const string Especes = "Espèces", Cheque = "Chèque", Carte = "Carte bancaire", Virement = "Virement",
                          Remboursement = "Remboursement", Regularisation = "Régularisation", AutreMode = "Autre mode";

    /// <summary>Le groupe de chaque type de mode de paiement (R_MODEPAIEME_TYPE) ; un type inconnu va en « Autre mode ».</summary>
    internal static readonly IReadOnlyDictionary<string, string> GroupeParType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ES"] = Especes, ["CH"] = Cheque, ["CB"] = Carte, ["VI"] = Virement, ["RBT"] = Remboursement, ["REG"] = Regularisation, ["DIV"] = AutreMode,
    };

    internal const string CriterePeriode = "Période", CritereCaissier = "Caissier", CritereModes = "Modes de paiement", CritereTri = "Tri";
    internal const string TriMode = "Par mode de paiement", TriUtilisateur = "Par utilisateur", TriMontant = "Par montant", TriDate = "Par date";

    public IReadOnlyCollection<string> Criteres { get; } = [CriterePeriode, CritereCaissier, CritereModes, CritereTri];

    // La premiere ventilation de l'encaissement sur une affaire et un client de la base (la condition des INNER JOIN de la V2).
    private const string Requete = """
        SELECT TOP (@plafond) e.ENCMT_ID_ENCMT, e.ENCMT_DATE_ENCAISSEMENT, e.ENCMT_MONTANT, e.ENCMT_NUMERO_TITRE, e.ENCMT_DESIGNATION_BANQUE,
               e.ENCMT_DATE_COMPTABILISATION, e.ENCMT_REMIS, e.ENCMT_MODE_PAIEMENT, mp.R_MODEPAIEME_LIBELLE_ECRAN AS Mode, mp.R_MODEPAIEME_TYPE AS TypeMode,
               CASE mp.R_MODEPAIEME_TYPE WHEN 'RBT' THEN v.ClientNom ELSE e.ENCMT_DESIGNATION_PAYEUR END AS Payeur,
               LTRIM(RTRIM(CONCAT(u.Prenom, ' ', u.Nom))) AS Caissier, u.CompteWindows AS Login,
               COUNT(*) OVER () AS Total
        FROM v2.ENCAISSEMENT AS e
        INNER JOIN v2.MODE_PAIEMENT AS mp ON mp.R_MODEPAIEME_VALEUR = e.ENCMT_MODE_PAIEMENT
        CROSS APPLY (SELECT TOP (1) c.Nom AS ClientNom
                     FROM v2.VENTILATION AS vt
                     INNER JOIN v2.AFFAIRE AS a ON a.AFFRE_ID_AFFRE = vt.VENTI_ID_AFFRE
                     INNER JOIN rec.Client AS c ON c.Id = vt.VENTI_ID_CLIEN
                     WHERE vt.VENTI_ID_ENCMT = e.ENCMT_ID_ENCMT
                     ORDER BY vt.VENTI_ID_VENTI) AS v
        LEFT JOIN soc.Compte AS u ON u.IdV2 = e.ENCMT_ID_UTLST
        WHERE e.ENCMT_DATE_ENCAISSEMENT >= @debut AND e.ENCMT_DATE_ENCAISSEMENT < @finExclue /*filtres*/
        ORDER BY /*tri*/
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, aujourdhui.Date, aujourdhui.Date, new FiltresRecherche(), TriMode, plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var (du, au) = Periode(recherche.Critere(CriterePeriode), aujourdhui);
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("e.ENCMT_DESIGNATION_PAYEUR LIKE @texte OR e.ENCMT_NUMERO_TITRE LIKE @texte OR e.ENCMT_DESIGNATION_BANQUE LIKE @texte OR v.ClientNom LIKE @texte",
                ("@texte", FormeLigne.Contient(recherche.Texte)));
        if (recherche.Critere(CritereCaissier) is { } caissier)
            f.Ajouter("u.Nom LIKE @caissier OR u.Prenom LIKE @caissier OR u.CompteWindows LIKE @caissier OR CONCAT(u.Prenom, ' ', u.Nom) LIKE @caissier",
                ("@caissier", FormeLigne.Contient(caissier)));
        if (recherche.Critere(CritereModes) is { } modes)
        {
            var liste = modes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var clauses = new List<string>();
            var parametres = new List<(string, object?)>();
            for (var k = 0; k < liste.Length; k++)
            {
                clauses.Add($"e.ENCMT_MODE_PAIEMENT = @mode{k} OR mp.R_MODEPAIEME_LIBELLE_ECRAN = @mode{k}");
                parametres.Add(($"@mode{k}", liste[k]));
            }
            if (clauses.Count > 0) f.Ajouter(string.Join(" OR ", clauses), [.. parametres]);
        }
        return Lire(baseGreffe, du, au, f, recherche.Critere(CritereTri) ?? TriMode, plafond, ct);
    }

    /// <summary>La periode « du|au » de l'ecran ; une borne vide ou illisible reprend aujourd'hui (la valeur initiale de la V2).</summary>
    internal static (DateTime Du, DateTime Au) Periode(string? valeur, DateTime aujourdhui)
    {
        var bornes = (valeur ?? "").Split('|');
        var du = CriteresConsultation.Date(bornes[0]) ?? aujourdhui.Date;
        var au = (bornes.Length > 1 ? CriteresConsultation.Date(bornes[1]) : null) ?? aujourdhui.Date;
        return (du.Date, au.Date);
    }

    private static string Ordre(string tri) => tri switch
    {
        TriUtilisateur => "Caissier, e.ENCMT_DATE_ENCAISSEMENT, e.ENCMT_ID_ENCMT",
        TriMontant => "e.ENCMT_MONTANT, e.ENCMT_DATE_ENCAISSEMENT, e.ENCMT_ID_ENCMT",
        TriDate => "e.ENCMT_DATE_ENCAISSEMENT, e.ENCMT_ID_ENCMT",
        _ => "mp.R_MODEPAIEME_TYPE, mp.R_MODEPAIEME_LIBELLE_ECRAN, e.ENCMT_DATE_ENCAISSEMENT, e.ENCMT_ID_ENCMT",
    };

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, DateTime du, DateTime au, FiltresRecherche f, string tri, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe,
            Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal).Replace("/*tri*/", Ordre(tri), StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond), new SqlParameter("@debut", du), new SqlParameter("@finExclue", au.AddDays(1))));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var id = FormeLigne.Texte(r, "ENCMT_ID_ENCMT");
        var nom = $"Encaissement {id}";
        var date = FormeLigne.DateDe(r, "ENCMT_DATE_ENCAISSEMENT");
        var montant = CriteresConsultation.Decimal(r, "ENCMT_MONTANT");
        var mode = FormeLigne.Texte(r, "Mode");
        var titre = FormeLigne.Texte(r, "ENCMT_NUMERO_TITRE");
        var payeur = FormeLigne.Texte(r, "Payeur");
        var caissier = FormeLigne.Texte(r, "Caissier") is { Length: > 0 } c ? c : FormeLigne.Texte(r, "Login");
        var comptabilise = FormeLigne.DateDe(r, "ENCMT_DATE_COMPTABILISATION") is not null;
        var remise = CriteresConsultation.EntierDe(r, "ENCMT_REMIS") is { } nr && nr != -1 ? nr : (int?)null;
        var (etat, libelleEtat) = comptabilise ? ("Validee", "comptabilisé") : remise is { } numeroRemise ? ("EnCours", $"remis en banque (remise {numeroRemise})")
            : ("AFaire", "à comptabiliser");
        var groupe = GroupeParType.GetValueOrDefault(FormeLigne.Texte(r, "TypeMode"), AutreMode);
        var precisions = FormeLigne.Joindre(mode, CriteresConsultation.Euros(montant), $"le {FormeLigne.Date(date)}", payeur,
            titre.Length > 0 ? $"titre {titre}" : null, FormeLigne.Texte(r, "ENCMT_DESIGNATION_BANQUE"),
            caissier.Length > 0 ? $"caissier {caissier}" : null, libelleEtat);
        return new LigneLue(nom, precisions, groupe, etat, [])
        {
            Id = id,
            Valeurs = [nom, FormeLigne.DateFiche(date), payeur, mode, titre, caissier, CriteresConsultation.MontantFiche(montant)],
        };
    }
}
