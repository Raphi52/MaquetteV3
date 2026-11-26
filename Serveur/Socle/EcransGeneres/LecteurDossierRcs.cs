using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Dossier RCS » (PROC_VDOSSIER) : retrouver un dossier du registre par l'un de ses numeros, et lire sa fiche (identite et
/// documents). Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026. Lecture seule.
///
/// SOURCE (ancien RIG) : PROC_VDOSSIER (classe PROC_VGED ; le Detail OPE de proc_vdossier.md fait foi).
///   - L'entree par numero (OPE_ENTREE_VGED_NUMERO, proc_vdossier.md:314-347) : « n° de demande, Siren, dossier, liaison,
///     depot, inscription, chrono formalite, non-inscrit... ». Le routage de PROC_VGED (PROC_VGED.cs:115-260,
///     proc_vdossier.md:57-127) resout : la demande -> son dossier (DMND_ID_DSRCS, ou le dossier de son action RCS) ; le SIREN
///     -> le dossier ; le depot -> le dossier de son action (ACTION_RCS.ACTRC_ID_DPTRC) ; le non-inscrit -> lui-meme. Le
///     bouton « Rechercher » ouvre la recherche d'entreprise par nom (OPE_ENTREE_VGED_NUMERO.cs:76-107) : le critere
///     « Dénomination » la reprend (LIKE sur la denomination de la fiche rec.Entreprise, ou du non-inscrit).
///   - L'entree par inscription de privilege (OPE_ENTREE_VGED_INSCRIPTION, proc_vdossier.md:349-369) : l'inscription -> les
///     dossiers des entreprises de ses debiteurs (Inscription.DossiersRCS, RigMetier\Inscription\Inscription.cs:1172-1184 :
///     DEBITEUR_END.DBEND_ID_INSCR, DBEND_ID_RPRTR -> DOSSIER_RCS), plusieurs dossiers se choisissant dans une liste
///     (OPE_SELECT_DOSSIERRCS) -- ici, la liste des resultats.
///   Non repris (nommes) : le numero de liaison, le chrono de formalite, le bordereau de requisition et le numero d'instance.
///   Dans la base V3 : DOSSIER_RCS = rec.DossierRcs, ENTREPRISE = rec.Entreprise, ADRESSE_BASE = rec.Adresse, ACTION_RCS =
///   rec.ActionRegistre, INSCRIPTION = rec.Inscription (Serveur/Recopie/FichesV3.cs) ; les autres tables, v2.* telles quelles.
///
/// LA LISTE A L'OUVERTURE : RIG v2 ouvre sur la saisie du numero, sans liste. L'ecran V3 montre les dossiers MIS A JOUR
/// AUJOURD'HUI (DSSRC_DATE_MAJ, de 0 h a 24 h) : les dossiers que le greffe traite dans la journee. C'est une liste de depart de
/// la V3, sans equivalent dans la V2 ; la recherche, elle, est celle de la V2. La copie de developpement (les plus anciens
/// dossiers) n'en a aucun du jour : l'ecran s'y ouvre vide, la recherche par numero retrouve les dossiers.
///
/// GROUPES : « Dossier RCS » (trouve par son numero, son SIREN, un depot, une inscription ou son nom), « Non-inscrit »,
/// « Demande » (trouve par le numero d'une demande : le dossier de la demande).
/// REGLE D'ETAT (les colonnes de DOSSIER_RCS ; du non-inscrit, sa radiation seule) :
///   radie (DSSRC_DATE_RADIATION, NINSC_DATE_RADIATION)                        -> Validee « Radié » ;
///   provisoirement sans activite, ou en cessation (DSSRC_PROVISOIREMENT_SANS,
///   DSSRC_DATE_CESSATION)                                                     -> EnCours « Sans activité » ;
///   autre                                                                     -> AFaire « Actif ».
///
/// FICHE (IFicheEcran), Id « D-12 » (dossier 12) ou « N-7 » (non-inscrit 7) : « Identité » (les colonnes de la grille de
/// OPE_SELECT_DOSSIERRCS, proc_vdossier.md:383-387) et « Documents » -- l'arbre de OPE_VGED, pour ce que la V3 en lit : les
/// actes (depots sans exercice), les bilans (depots d'un exercice : DPTRC_ID_EXRCC), les formalites (les actions RCS), les
/// inscriptions de privilege, et les beneficiaires effectifs declares (BENEFICIAIRE_EFFECTIF). Non lus (nommes) : les
/// surveillances DCA/RBE, la requisition et les pieces jointes (OPE_VGED.cs:504-803), et les gestes (ouvrir, imprimer, Kbis,
/// envoi par courriel : ils produisent des editions, OPE_VGED.cs:824-1549).
/// </summary>
internal sealed class LecteurDossierRcs : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_VDOSSIER";

    internal const string GroupeDossier = "Dossier RCS", GroupeNonInscrit = "Non-inscrit", GroupeDemande = "Demande";

    internal const string CritereNumero = "Numéro", CritereDenomination = "Dénomination", CritereInscription = "Inscription privilège";

    internal const string SectionIdentite = "Identité", SectionDocuments = "Documents";

    internal const string ChampDenomination = "Dénomination", ChampGestion = "Numéro de gestion", ChampSiren = "SIREN",
                          ChampImmatriculation = "Date d'immatriculation", ChampRadiation = "Date de radiation", ChampAdresse = "Adresse",
                          ChampNaf = "Code NAF", ChampPm = "Personne morale", ChampActes = "Actes", ChampBilans = "Bilans",
                          ChampFormalites = "Formalités", ChampInscriptions = "Inscriptions privilèges", ChampBe = "Signalements BE";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereNumero, CritereDenomination, CritereInscription];

    // Le nom d'une entreprise : sa denomination, sinon le nom et le prenom de la personne.
    private const string NomEntreprise = "COALESCE(NULLIF(LTRIM(RTRIM(e.Denomination)), ''), LTRIM(RTRIM(CONCAT(e.Patronyme, ' ', e.Prenom))))";
    private const string NomNonInscrit = """
        COALESCE(NULLIF(LTRIM(RTRIM(n.NINSC_DENOMINATION)), ''), LTRIM(RTRIM(CONCAT(ISNULL(n.NINSC_NOM_USAGE, n.NINSC_PATRONYME), ' ', n.NINSC_PRENOMS))))
        """;

    // Bloc : 0 dossier, 1 non-inscrit, 2 demande (l'ordre des groupes). /*dossier*/, /*noninscrit*/, /*demande*/ : les filtres.
    private const string Requete = $"""
        SELECT TOP (@plafond) l.*, COUNT(*) OVER () AS Total
        FROM (SELECT 0 AS Bloc, s.Id, CAST(NULL AS varchar(11)) AS Demande, s.NumeroGestion AS Numero, s.Siren,
                     {NomEntreprise} AS Nom, s.DateImmatriculation, s.DateRadiation, s.ProvisoirementSans, s.DateCessation
              FROM rec.DossierRcs AS s
              LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
              WHERE 1 = 1 /*dossier*/
              UNION ALL
              SELECT 1, n.NINSC_ID_NINSC, NULL, n.NINSC_NUM_NON_INSCRIT, n.NINSC_NUM_SIREN, {NomNonInscrit}, NULL, n.NINSC_DATE_RADIATION,
                     CONVERT(bit, 0), NULL
              FROM v2.NON_INSCRIT AS n
              WHERE 1 = 1 /*noninscrit*/
              UNION ALL
              SELECT 2, s.Id, d.DMND_NUM_DEMANDE, s.NumeroGestion, s.Siren, {NomEntreprise}, s.DateImmatriculation, s.DateRadiation,
                     s.ProvisoirementSans, s.DateCessation
              FROM v2.DEMANDE AS d
              JOIN rec.DossierRcs AS s ON s.Id = COALESCE(d.DMND_ID_DSRCS, (SELECT a.IdDossier FROM rec.ActionRegistre AS a WHERE a.Id = d.DMND_ID_ACTRC))
              LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
              WHERE 1 = 1 /*demande*/) AS l
        ORDER BY l.Bloc, l.Numero, l.Id, l.Demande
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        // Les dossiers mis a jour aujourd'hui (en tete du fichier) ; ni non-inscrit ni demande.
        var sql = Requete.Replace("/*dossier*/", " AND s.DateMaj >= @debut AND s.DateMaj < @finExclue", StringComparison.Ordinal)
                         .Replace("/*noninscrit*/", " AND 1 = 0", StringComparison.Ordinal)
                         .Replace("/*demande*/", " AND 1 = 0", StringComparison.Ordinal);
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, new SqlParameter("@plafond", plafond),
            new SqlParameter("@debut", aujourdhui.Date), new SqlParameter("@finExclue", aujourdhui.Date.AddDays(1)));
    }

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var dossier = new List<string>();
        var nonInscrit = new List<string>();
        var demande = new List<string>();
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };

        // Un numero (le champ de recherche, ou le critere « Numéro ») : dossier, SIREN, depot, non-inscrit, demande.
        void Numero(string valeur, string nom)
        {
            dossier.Add($"(s.NumeroGestion = {nom} OR s.Siren = {nom} OR EXISTS (SELECT 1 FROM rec.ActionRegistre AS a " +
                        $"JOIN v2.DEPOT_RCS AS p ON p.DPTRC_ID_DPTRC = a.IdDepot WHERE a.IdDossier = s.Id AND p.DPTRC_NUM_DEPOT = {nom}))");
            nonInscrit.Add($"(n.NINSC_NUM_NON_INSCRIT = {nom} OR n.NINSC_NUM_SIREN = {nom})");
            demande.Add($"d.DMND_NUM_DEMANDE = {nom}");
            parametres.Add(new SqlParameter(nom, CriteresConsultation.Numero(valeur)));
        }
        // Un nom : la recherche d'entreprise de la V2 (en tete du fichier) ; une demande ne se trouve pas par nom.
        void Nom(string valeur, string nom)
        {
            dossier.Add($"{NomEntreprise} LIKE {nom}");
            nonInscrit.Add($"{NomNonInscrit} LIKE {nom}");
            demande.Add("1 = 0");
            parametres.Add(new SqlParameter(nom, FormeLigne.Contient(valeur)));
        }

        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            // Le champ de recherche : le numero OU le nom.
            var t = recherche.Texte.Trim();
            dossier.Add($"(s.NumeroGestion = @tn OR s.Siren = @tn OR {NomEntreprise} LIKE @tl OR EXISTS (SELECT 1 FROM rec.ActionRegistre AS a " +
                        "JOIN v2.DEPOT_RCS AS p ON p.DPTRC_ID_DPTRC = a.IdDepot WHERE a.IdDossier = s.Id AND p.DPTRC_NUM_DEPOT = @tn))");
            nonInscrit.Add($"(n.NINSC_NUM_NON_INSCRIT = @tn OR n.NINSC_NUM_SIREN = @tn OR {NomNonInscrit} LIKE @tl)");
            demande.Add("d.DMND_NUM_DEMANDE = @tn");
            parametres.Add(new SqlParameter("@tn", CriteresConsultation.Numero(t)));
            parametres.Add(new SqlParameter("@tl", FormeLigne.Contient(t)));
        }
        if (recherche.Critere(CritereNumero) is { } numero) Numero(numero, "@numero");
        if (recherche.Critere(CritereDenomination) is { } denomination) Nom(denomination, "@nom");
        if (recherche.Critere(CritereInscription) is { } inscription)
        {
            // Les dossiers des entreprises des debiteurs de l'inscription (Inscription.DossiersRCS) ; ni non-inscrit ni demande.
            dossier.Add("EXISTS (SELECT 1 FROM rec.Inscription AS i JOIN v2.DEBITEUR_END AS b ON b.DBEND_ID_INSCR = i.Id " +
                        "WHERE REPLACE(i.NumeroInscription, ' ', '') = @inscription AND b.DBEND_ID_RPRTR = s.IdEntreprise)");
            nonInscrit.Add("1 = 0");
            demande.Add("1 = 0");
            parametres.Add(new SqlParameter("@inscription", CriteresConsultation.Numero(inscription)));
        }
        static string Et(List<string> c) => c.Count == 0 ? "" : " AND " + string.Join(" AND ", c);
        var sql = Requete.Replace("/*dossier*/", Et(dossier), StringComparison.Ordinal)
                         .Replace("/*noninscrit*/", Et(nonInscrit), StringComparison.Ordinal)
                         .Replace("/*demande*/", Et(demande), StringComparison.Ordinal);
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var bloc = Convert.ToInt32(r["Bloc"], System.Globalization.CultureInfo.InvariantCulture);
        var id = FormeLigne.Texte(r, "Id");
        var numero = FormeLigne.Texte(r, "Numero");
        var radiation = FormeLigne.DateDe(r, "DateRadiation");
        var sansActivite = CriteresConsultation.Vrai(r, "ProvisoirementSans") || FormeLigne.DateDe(r, "DateCessation") is not null;
        // La regle d'etat (en tete du fichier).
        var etat = radiation is not null ? "Validee" : sansActivite ? "EnCours" : "AFaire";
        var precisions = FormeLigne.Joindre(
            bloc == 2 ? $"Demande {FormeLigne.Texte(r, "Demande")}" : null,
            bloc == 1 ? $"Non-inscrit {numero}" : $"Dossier RCS {FormeLigne.NumeroGestion(numero)}",
            FormeLigne.Texte(r, "Siren") is { Length: > 0 } siren ? $"SIREN {FormeLigne.Siren(siren)}" : null,
            FormeLigne.DateDe(r, "DateImmatriculation") is { } immat ? $"immatriculé le {FormeLigne.Date(immat)}" : null,
            radiation is { } rad ? $"radié le {FormeLigne.Date(rad)}" : sansActivite ? "sans activité" : null);
        var nom = FormeLigne.Texte(r, "Nom") is { Length: > 0 } n ? n : bloc == 1 ? $"Non-inscrit {numero}" : $"Dossier {FormeLigne.NumeroGestion(numero)}";
        return new LigneLue(nom, precisions, bloc switch { 1 => GroupeNonInscrit, 2 => GroupeDemande, _ => GroupeDossier }, etat, [])
        {
            Id = $"{(bloc == 1 ? 'N' : 'D')}-{id}",
        };
    }

    // ------------------------------------------------------------------ LA FICHE

    private const string Adresse = "LTRIM(RTRIM(CONCAT(a.NumeroVoie, ' ', a.TypeVoie, ' ', a.NomVoie, ' ', a.CodePostal, ' ', a.Localite)))";

    private const string FicheDossier = $"""
        SELECT s.Id, s.NumeroGestion AS Numero, s.Siren, {NomEntreprise} AS Nom, s.DateImmatriculation, s.DateRadiation,
               {Adresse} AS Adresse, e.CodeNaf, e.Id AS IdEntreprise,
               CASE WHEN EXISTS (SELECT 1 FROM v2.INSCRIT_PM AS pm WHERE pm.INSPM_ID_DSRCS = s.Id) THEN 1 ELSE 0 END AS Pm
        FROM rec.DossierRcs AS s
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        LEFT JOIN rec.Adresse AS a ON a.Id = e.IdAdresse
        WHERE s.Id = @id
        """;

    private const string FicheNonInscrit = $"""
        SELECT n.NINSC_ID_NINSC AS Id, n.NINSC_NUM_NON_INSCRIT AS Numero, n.NINSC_NUM_SIREN AS Siren, {NomNonInscrit} AS Nom,
               CAST(NULL AS datetime) AS DateImmatriculation, n.NINSC_DATE_RADIATION AS DateRadiation, {Adresse} AS Adresse,
               CAST(NULL AS varchar(6)) AS CodeNaf, n.NINSC_ID_RPRTR AS IdEntreprise, CONVERT(int, n.NINSC_PM) AS Pm
        FROM v2.NON_INSCRIT AS n
        LEFT JOIN rec.Adresse AS a ON a.Id = n.NINSC_ID_ADRBS
        WHERE n.NINSC_ID_NINSC = @id
        """;

    // Les depots du dossier (ou du non-inscrit), par ses actions RCS : sans exercice, les actes ; avec, les bilans.
    private const string Depots = """
        SELECT p.DPTRC_NUM_DEPOT, p.DPTRC_DATE, x.EXRCC_DATE_CLOTURE,
               (SELECT COUNT(*) FROM v2.ACTE_DEPOSE AS ad WHERE ad.ACTDP_ID_DPTRC = p.DPTRC_ID_DPTRC) AS Pieces
        FROM v2.DEPOT_RCS AS p
        LEFT JOIN v2.EXERCICE AS x ON x.EXRCC_ID_EXRCC = p.DPTRC_ID_EXRCC
        WHERE p.DPTRC_ID_DPTRC IN (SELECT a.IdDepot FROM rec.ActionRegistre AS a WHERE /*proprietaire*/)
          AND p.DPTRC_ID_EXRCC /*bilan*/
        ORDER BY p.DPTRC_DATE DESC, p.DPTRC_NUM_DEPOT DESC
        """;

    private const string Formalites = """
        SELECT a.NumeroArrivee, a.DateArrivee, a.CodeProcessus, a.EtatAction
        FROM rec.ActionRegistre AS a
        WHERE /*proprietaire*/
        ORDER BY a.DateArrivee DESC, a.Id DESC
        """;

    private const string Inscriptions = """
        SELECT DISTINCT i.NumeroInscription, i.TypeInscription, i.DateInscription, i.DatePeremption
        FROM rec.Inscription AS i
        JOIN v2.DEBITEUR_END AS b ON b.DBEND_ID_INSCR = i.Id
        WHERE b.DBEND_ID_RPRTR = @entreprise
        ORDER BY i.DateInscription DESC, i.NumeroInscription DESC
        """;

    private const string Beneficiaires = """
        SELECT LTRIM(RTRIM(CONCAT(ISNULL(b.BENEF_NOM_USAGE, b.BENEF_PATRONYME), ' ', b.BENEF_PRENOM_ETAT_CIVIL))) AS Nom,
               b.BENEF_DATE_EFFET, b.BENEF_DEPART_DATE
        FROM v2.BENEFICIAIRE_EFFECTIF AS b
        WHERE b.BENEF_ID_DSRCS = @id AND b.BENEF_SUPPRIME = 0
        ORDER BY b.BENEF_DATE_EFFET DESC, b.BENEF_ID_BENEF
        """;

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (id.Length < 3 || id[1] != '-' || id[0] is not ('D' or 'N') || CriteresConsultation.Entier(id[2..]) is not { } n) return null;
        var estDossier = id[0] == 'D';
        string nom;
        int? entreprise;
        ChampFiche[] identite;
        await using (var r = await LectureV2.LireAsync(baseGreffe, estDossier ? FicheDossier : FicheNonInscrit, ct, new SqlParameter("@id", n)))
        {
            if (!await r.ReadAsync(ct)) return null;
            var numero = FormeLigne.Texte(r, "Numero");
            nom = FormeLigne.Texte(r, "Nom") is { Length: > 0 } x ? x : estDossier ? $"Dossier {FormeLigne.NumeroGestion(numero)}" : $"Non-inscrit {numero}";
            entreprise = CriteresConsultation.EntierDe(r, "IdEntreprise");
            identite =
            [
                new(ChampDenomination, FormeLigne.Texte(r, "Nom")),
                new(ChampGestion, estDossier ? FormeLigne.NumeroGestion(numero) : numero),
                new(ChampSiren, FormeLigne.Siren(FormeLigne.Texte(r, "Siren"))),
                new(ChampImmatriculation, FormeLigne.DateFiche(FormeLigne.DateDe(r, "DateImmatriculation"))),
                new(ChampRadiation, FormeLigne.DateFiche(FormeLigne.DateDe(r, "DateRadiation"))),
                new(ChampAdresse, FormeLigne.Texte(r, "Adresse")),
                new(ChampNaf, FormeLigne.Texte(r, "CodeNaf")),
                new(ChampPm, FormeLigne.Case(Convert.ToInt32(r["Pm"], System.Globalization.CultureInfo.InvariantCulture) == 1)),
            ];
        }
        var proprietaire = estDossier ? "a.IdDossier = @id" : "a.IdNonInscrit = @id";
        string[] Depot(SqlDataReader r) =>
        [
            FormeLigne.Texte(r, "DPTRC_NUM_DEPOT"), FormeLigne.Date(FormeLigne.DateDe(r, "DPTRC_DATE")),
            FormeLigne.Date(FormeLigne.DateDe(r, "EXRCC_DATE_CLOTURE")), FormeLigne.Texte(r, "Pieces"),
        ];
        var actes = await CriteresConsultation.SousListeAsync(baseGreffe,
            Depots.Replace("/*proprietaire*/", proprietaire, StringComparison.Ordinal).Replace("/*bilan*/", "IS NULL", StringComparison.Ordinal),
            Depot, ct, new SqlParameter("@id", n));
        var bilans = await CriteresConsultation.SousListeAsync(baseGreffe,
            Depots.Replace("/*proprietaire*/", proprietaire, StringComparison.Ordinal).Replace("/*bilan*/", "IS NOT NULL", StringComparison.Ordinal),
            Depot, ct, new SqlParameter("@id", n));
        var formalites = await CriteresConsultation.SousListeAsync(baseGreffe, Formalites.Replace("/*proprietaire*/", proprietaire, StringComparison.Ordinal),
            r => [FormeLigne.Texte(r, "NumeroArrivee"), FormeLigne.Date(FormeLigne.DateDe(r, "DateArrivee")), FormeLigne.Texte(r, "CodeProcessus"),
                  FormeLigne.Texte(r, "EtatAction")], ct, new SqlParameter("@id", n));
        var inscriptions = entreprise is { } ent
            ? await CriteresConsultation.SousListeAsync(baseGreffe, Inscriptions,
                r => [FormeLigne.Texte(r, "NumeroInscription"), FormeLigne.Texte(r, "TypeInscription"),
                      FormeLigne.Date(FormeLigne.DateDe(r, "DateInscription")), FormeLigne.Date(FormeLigne.DateDe(r, "DatePeremption"))],
                ct, new SqlParameter("@entreprise", ent))
            : [];
        var beneficiaires = estDossier
            ? await CriteresConsultation.SousListeAsync(baseGreffe, Beneficiaires,
                r => [FormeLigne.Texte(r, "Nom"), FormeLigne.Date(FormeLigne.DateDe(r, "BENEF_DATE_EFFET")),
                      FormeLigne.Date(FormeLigne.DateDe(r, "BENEF_DEPART_DATE"))], ct, new SqlParameter("@id", n))
            : [];
        string[] colonnesDepot = ["N° de dépôt", "Date du dépôt", "Clôture de l'exercice", "Pièces"];
        return new FicheEcran(Procedure, id, nom,
        [
            new SectionFiche(SectionIdentite, identite),
            new SectionFiche(SectionDocuments,
            [
                CriteresConsultation.Tableau(ChampActes, colonnesDepot, actes),
                CriteresConsultation.Tableau(ChampBilans, colonnesDepot, bilans),
                CriteresConsultation.Tableau(ChampFormalites, ["N° d'arrivée", "Date d'arrivée", "Processus", "État"], formalites),
                CriteresConsultation.Tableau(ChampInscriptions, ["N° d'inscription", "Type", "Inscrite le", "Périme le"], inscriptions),
                CriteresConsultation.Tableau(ChampBe, ["Bénéficiaire", "Effet le", "Départ le"], beneficiaires),
            ]),
        ]);
    }
}
