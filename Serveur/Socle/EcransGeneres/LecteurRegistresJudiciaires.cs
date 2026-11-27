using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Registres judiciaires » (PROC_REGJUD) : les lignes des trois registres reglementaires -- le repertoire general, le registre
/// des decisions d'injonction de payer et celui des ordonnances du president. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3
/// 100 % fonctionnel », lot 6, 26/11/2026. RIG v2 n'ecrit que des demandes d'edition (proc_regjud.md:756-757) : « Impression »
/// les cree dans la file des editions (EcrivainRegistresJudiciaires.cs) ; l'apercu Crystal n'est pas repris (nomme).
///
/// SOURCE (ancien RIG) : PROC_REGJUD, les trois OPE_REGISTRE_* (le Detail OPE de proc_regjud.md fait foi, tableau :642-655) :
///   - REPERTOIRE GENERAL (OPE_REGISTRE_GENERAL.cs:62-78, proc_regjud.md:188-229) : INSTANCE ayant un numero au repertoire
///     general (ISNULL(INSTN_NUM_REP_GENERAL, '') <> ''), INNER JOIN NATURE_AFFAIRE ; la decision (DECISION_JUDICIAIRE) hors
///     avant-dire-droit et dont ISNULL(DCJDC_DATE, DCJDC_DATE_PREVUE) <= aujourd'hui, son type (TYPE_DECISION, hors sursis a
///     statuer et complement d'expertise) et son ressort (RESSORT) ; la chambre, celle de la premiere audience de l'affaire
///     (AUDIENCE_CABINET par APPEL_AFFAIRE, APPAF_NUM_ORDRE). La date « d'acte » de la V2 est la date de saisine du greffe
///     (« INSTN_DATE_SAISINE_GREFFE as DATE_ACTE », proc_regjud.md:211) ;
///   - DECISIONS D'INJONCTION DE PAYER (OPE_REGISTRE_DECISIONS_IP, IP.cs:45-48) : DECISION_JUDICIAIRE JOIN INSTANCE de type IP
///     JOIN TYPE_DECISION ; le numero est celui de la minute (DCJDC_NUM_REPERTOIRE), la date ISNULL(DCJDC_DATE, DCJDC_DATE_PREVUE) ;
///   - ORDONNANCES DU PRESIDENT (OPE_REGISTRE_ORDO_PRESIDENT, ORDO.cs:52-54) : INSTANCE de type RP JOIN NATURE_AFFAIRE.
/// Le repertoire general et les ordonnances portent ici UNE ligne par instance, avec sa derniere decision (la requete de la V2 en
/// rend une par decision, que l'edition regroupe).
///
/// UN PLAFOND PAR REGISTRE : la V2 a une etape, donc une liste, par registre (ETP_REGISTRE_*, proc_regjud.md:34-48). Chaque
/// registre rend ses `plafond` premieres lignes et son total (LignesLues.TotalParGroupe) : un long repertoire general ne masque
/// plus les injonctions ni les ordonnances (controle final du lot 6, 26/11/2026 : un plafond commun, trie registre par registre,
/// les rendait illisibles des que le repertoire general passait 200 lignes), et le pont ne dit coupe que le registre qui l'est.
///
/// RECHERCHE (IRechercheEcran) : les criteres de la V2, ses parametres au libelle du descripteur, cherches DANS la base (la V2
/// relance sa requete a chaque saisie, Ult_ChangementValeur -> RefreshResultats, proc_regjud.md:156-160) :
///   - « Dates d'acte » (INSTN_DATE_ACTE), « Dates de saisine » (INSTN_DATE_SAISINE_GREFFE), « Dates d'enrôlement »
///     (INSTN_DATE_ENROLEMENT) : les trois registres (GENERAL.cs:84-130, IP.cs:53-88, ORDO.cs:58-93). Les deux bornes ->
///     BETWEEN ; une seule -> la colonne EGALE a ce jour, comme la V2 (« = CONVERT(DATETIME, ..., 103) ») ;
///   - « Type d'instance » : le repertoire general seul (GENERAL.cs:41-56, :107-116), INSTN_NUM_ROLE LIKE '%<type>[0-9]%'.
///     Les registres des IP et des ordonnances n'ont pas ce critere dans la V2 : il ne les filtre pas ;
///   - le texte (champ de recherche de la V3, sans equivalent V2) : le role ou le nom de l'affaire, colonnes de la grille V2.
/// NON REPRIS, nommes : plusieurs types d'instance a la fois (la V2 coche ; la V3 en choisit un) ; les criteres propres a un
/// registre (IP : date et type de decision, numeros d'IP et de minute, IP.cs:89-128 ; ordonnances : code affaire, numeros,
/// de PC ou hors PC, ORDO.cs:94-118) ; la ventilation (compte par type, proc_regjud.md:19 ; VentilationLibelle, OPE_REGISTRE_ABSTRACT.cs:27).
///
/// GROUPES : « Répertoire général », « Injonctions de payer », « Ordonnances du président ».
/// REGLE D'ETAT (la decision de la ligne) : aucune -> AFaire « Sans décision » ; prevue seulement (DCJDC_DATE vide) -> EnCours
/// « Décision prévue » ; rendue (DCJDC_DATE) -> Validee « Décision rendue ».
/// VALEURS (les colonnes du descripteur) : rôle, date d'acte, numéro au répertoire, nature, chambre, date de décision, type de
/// décision, ressort.
/// </summary>
internal sealed class LecteurRegistresJudiciaires : IRechercheEcran
{
    public string Procedure => "PROC_REGJUD";

    internal static readonly string[] Groupes = ["Répertoire général", "Injonctions de payer", "Ordonnances du président"];

    /// <summary>La lettre de l'id d'une ligne, par registre (repertoire General, decisions des Ip, Ordonnances).</summary>
    internal static readonly char[] Lettres = ['G', 'I', 'O'];

    internal const string CritereActe = "Dates d'acte";
    internal const string CritereSaisine = "Dates de saisine";
    internal const string CritereEnrolement = "Dates d'enrôlement";
    internal const string CritereType = "Type d'instance";

    /// <summary>Les types d'instance que la V2 propose au repertoire general (TypesInstancesChecked, GENERAL.cs:41-56).</summary>
    internal static readonly string[] TypesInstance = ["J", "F", "R", "JC", "OP", "IP", "LS", "PP", "DD", "MD", "RC", "RA"];

    public IReadOnlyCollection<string> Criteres { get; } = [CritereActe, CritereSaisine, CritereEnrolement, CritereType];

    // La decision retenue d'une instance : hors avant-dire-droit, rendue ou prevue au plus tard aujourd'hui, la plus recente.
    private const string Decision = """
        OUTER APPLY (SELECT TOP (1) d.DCJDC_DATE, d.DCJDC_DATE_PREVUE, t.R_TYPEDECISI_LIBELLE_ECRAN, rs.R_RESSORT_LIBELLE_ECRAN
                     FROM v2.DECISION_JUDICIAIRE AS d
                     LEFT JOIN v2.TYPE_DECISION AS t ON t.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
                          AND t.R_TYPEDECISI_SURSIS_STATUER = 0 AND t.R_TYPEDECISI_ADD_EXPERTISE = 0
                     LEFT JOIN v2.RESSORT AS rs ON rs.R_RESSORT_VALEUR = d.DCJDC_RESSORT
                     WHERE d.DCJDC_ID_INSTN = i.INSTN_ID_INSTN AND ISNULL(d.DCJDC_AVANT_DIRE_DROIT, '') <> '1'
                       AND ISNULL(d.DCJDC_DATE, d.DCJDC_DATE_PREVUE) < @demain
                     ORDER BY ISNULL(d.DCJDC_DATE, d.DCJDC_DATE_PREVUE) DESC, d.DCJDC_ID_DCJDC DESC) AS dj
        """;

    private const string Chambre = """
        ISNULL((SELECT TOP (1) ac.AUDNC_CHAMBRE FROM v2.AUDIENCE_CABINET AS ac
                JOIN v2.APPEL_AFFAIRE AS ap ON ap.APPAF_ID_ADNC = ac.AUDNC_ID_ADNC
                WHERE ap.APPAF_ID_INSTN = i.INSTN_ID_INSTN ORDER BY ap.APPAF_NUM_ORDRE), '')
        """;

    // Les filtres de la recherche se posent a la place de /*general*/ (repertoire general) et de /*communs*/ (les deux autres).
    // Rang et TotalBloc : le plafond et le total de CHAQUE registre (en tete du fichier).
    private const string Requete = $"""
        SELECT n.* FROM (
            SELECT l.*, ROW_NUMBER() OVER (PARTITION BY l.Bloc ORDER BY l.DateActe DESC, l.Role, l.Id) AS Rang,
                   COUNT(*) OVER (PARTITION BY l.Bloc) AS TotalBloc
            FROM (SELECT 0 AS Bloc, i.INSTN_ID_INSTN AS Id, i.INSTN_NUM_ROLE AS Role, i.INSTN_NOM_COURANT AS NomCourant,
                         i.INSTN_DATE_SAISINE_GREFFE AS DateActe, i.INSTN_NUM_REP_GENERAL AS Numero, na.R_NATUREAFFA_ISO AS Nature,
                         {Chambre} AS Chambre, dj.DCJDC_DATE AS DateDecision, dj.DCJDC_DATE_PREVUE AS DatePrevue,
                         dj.R_TYPEDECISI_LIBELLE_ECRAN AS TypeDecision, dj.R_RESSORT_LIBELLE_ECRAN AS Ressort
                  FROM v2.INSTANCE AS i
                  JOIN v2.NATURE_AFFAIRE AS na ON na.R_NATUREAFFA_VALEUR = i.INSTN_NATURE_AFFAIRE
                  {Decision}
                  WHERE ISNULL(i.INSTN_NUM_REP_GENERAL, '') <> '' /*general*/
                  UNION ALL
                  SELECT 1, d.DCJDC_ID_DCJDC, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, i.INSTN_DATE_ACTE, d.DCJDC_NUM_REPERTOIRE, NULL, '',
                         d.DCJDC_DATE, d.DCJDC_DATE_PREVUE, t.R_TYPEDECISI_LIBELLE_ECRAN, NULL
                  FROM v2.DECISION_JUDICIAIRE AS d
                  JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
                  JOIN v2.TYPE_DECISION AS t ON t.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
                  WHERE i.INSTN_TYPE_INSTANCE = 'IP' /*communs*/
                  UNION ALL
                  SELECT 2, i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, i.INSTN_DATE_ACTE, i.INSTN_NUM_REP_GENERAL,
                         na.R_NATUREAFFA_LIBELLE_ECRAN, {Chambre}, dj.DCJDC_DATE, dj.DCJDC_DATE_PREVUE, dj.R_TYPEDECISI_LIBELLE_ECRAN,
                         dj.R_RESSORT_LIBELLE_ECRAN
                  FROM v2.INSTANCE AS i
                  JOIN v2.NATURE_AFFAIRE AS na ON na.R_NATUREAFFA_VALEUR = i.INSTN_NATURE_AFFAIRE
                  {Decision}
                  WHERE i.INSTN_TYPE_INSTANCE = 'RP' /*communs*/) AS l) AS n
        WHERE n.Rang <= @plafond
        ORDER BY n.Bloc, n.Rang
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), new FiltresRecherche(), aujourdhui, plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        // Les criteres des trois registres (en tete du fichier), puis celui du seul repertoire general.
        var communs = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            communs.Ajouter("i.INSTN_NUM_ROLE LIKE @texte OR i.INSTN_NOM_COURANT LIKE @texte", ("@texte", FormeLigne.Contient(recherche.Texte)));
        Periode(communs, recherche.Critere(CritereActe), "i.INSTN_DATE_ACTE", "@acte");
        Periode(communs, recherche.Critere(CritereSaisine), "i.INSTN_DATE_SAISINE_GREFFE", "@saisine");
        Periode(communs, recherche.Critere(CritereEnrolement), "i.INSTN_DATE_ENROLEMENT", "@enrolement");
        var general = new FiltresRecherche();
        if (recherche.Critere(CritereType) is { } type)
            general.Ajouter("i.INSTN_NUM_ROLE LIKE @typeRole", ("@typeRole", $"%{type}[0-9]%"));
        return Lire(baseGreffe, communs, general, aujourdhui, plafond, ct);
    }

    /// <summary>Une periode saisie « du|au » (dates ISO de l'ecran) : les deux bornes -> BETWEEN, une seule -> egale (GENERAL.cs:84-93).</summary>
    private static void Periode(FiltresRecherche f, string? valeur, string colonne, string nom)
    {
        if (valeur is null) return;
        var bornes = valeur.Split('|');
        var du = CriteresConsultation.Date(bornes[0]);
        var au = bornes.Length > 1 ? CriteresConsultation.Date(bornes[1]) : null;
        if (du is { } d && au is { } a) f.Ajouter($"{colonne} BETWEEN {nom}Du AND {nom}Au", ($"{nom}Du", d.Date), ($"{nom}Au", a.Date));
        else if ((du ?? au) is { } jour) f.Ajouter($"{colonne} = {nom}Du", ($"{nom}Du", jour.Date));
    }

    private static async Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche communs, FiltresRecherche general,
                                               DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var sql = Requete.Replace("/*general*/", communs.EnSuite + general.EnSuite, StringComparison.Ordinal)
                         .Replace("/*communs*/", communs.EnSuite, StringComparison.Ordinal);
        var lignes = new List<LigneLue>();
        var totaux = Groupes.ToDictionary(g => g, _ => 0, StringComparer.Ordinal);
        // « <= getdate() » de la V2 : avant demain 0 h, le jour du banc ou du serveur (jamais GETDATE()).
        await using var r = await LectureV2.LireAsync(baseGreffe, sql, ct, communs.Parametres(general.Parametres(
            new SqlParameter("@plafond", plafond), new SqlParameter("@demain", aujourdhui.Date.AddDays(1)))));
        while (await r.ReadAsync(ct))
        {
            var ligne = Ligne(r);
            totaux[ligne.Groupe] = Convert.ToInt32(r["TotalBloc"], CultureInfo.InvariantCulture);
            lignes.Add(ligne);
        }
        return new LignesLues(lignes, totaux.Values.Sum()) { TotalParGroupe = totaux };
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var bloc = Convert.ToInt32(r["Bloc"], CultureInfo.InvariantCulture);
        var role = FormeLigne.Texte(r, "Role");
        var rendue = FormeLigne.DateDe(r, "DateDecision");
        var prevue = FormeLigne.DateDe(r, "DatePrevue");
        var dateActe = FormeLigne.DateDe(r, "DateActe");
        var numero = FormeLigne.Texte(r, "Numero");
        var type = FormeLigne.Texte(r, "TypeDecision");
        // La regle d'etat (en tete du fichier).
        var etat = rendue is not null ? "Validee" : prevue is not null ? "EnCours" : "AFaire";
        var decision = rendue ?? prevue;
        var nom = FormeLigne.Joindre(role.Length > 0 ? $"Rôle {role}" : null, FormeLigne.Texte(r, "NomCourant"));
        var precisions = FormeLigne.Joindre(
            dateActe is { } a ? $"acte du {FormeLigne.Date(a)}" : null,
            numero.Length > 0 ? (bloc == 1 ? $"minute {numero}" : $"n° {numero}") : null,
            FormeLigne.Texte(r, "Nature"),
            FormeLigne.Texte(r, "Chambre") is { Length: > 0 } ch ? $"chambre {ch}" : null,
            decision is { } dd ? $"{(rendue is null ? "décision prévue le" : "décision du")} {FormeLigne.Date(dd)}" : "sans décision",
            type, FormeLigne.Texte(r, "Ressort"));
        return new LigneLue(nom.Length > 0 ? nom : $"Instance n° {FormeLigne.Texte(r, "Id")}", precisions, Groupes[bloc], etat, [])
        {
            // L'id du geste « Impression » (EcrivainRegistresJudiciaires.cs) : la lettre du registre, puis l'instance (G, O) ou la
            // decision (I).
            Id = $"{Lettres[bloc]}-{FormeLigne.Texte(r, "Id")}",
            Valeurs =
            [
                role, FormeLigne.DateFiche(dateActe), numero, FormeLigne.Texte(r, "Nature"), FormeLigne.Texte(r, "Chambre"),
                FormeLigne.DateFiche(decision), type, FormeLigne.Texte(r, "Ressort"),
            ],
        };
    }
}
