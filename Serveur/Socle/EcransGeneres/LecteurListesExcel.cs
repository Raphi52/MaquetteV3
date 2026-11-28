using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Export Excel » (PROC_LISTEEXCEL, forme Etat) : les listes d'instances que l'ancien RIG exporte vers Excel, telles que le greffe
/// les a parametrees. Mode d'emploi des lecteurs : LecteurEcran.cs (plan « RIG v3 100 % fonctionnel », lot 11, 27/11/2026).
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_LISTEEXCEL (cite « OPE_RECHERCHE.cs », « ExcelInstance.cs ») ;
/// le parametrage : Outils\DLL\RigMetier\ParametrageListe\ (ParametrageListe.cs, ParametrageListeXls.cs, ParametrageListeXlsListe.cs).
/// Fiche : D:\RigApplication\Documentation\reference\proc\proc_listeexcel.md, « Parametres de lancement ».
///
/// LES TYPES : le menu lance PROC_LISTEEXCEL avec un type de liste (MetierParametres) ; ceux des instances sont INS, IDJ, IEF, IEP,
///   SDJ, GIE et AFF (OPE_RECHERCHE.cs:77-84, proc_listeexcel.md:24-33). Une ligne par type que la base declare
///   (PARAMETRAGE_LISTE_XLS_TYPE_LISTE), dans cet ordre, a son libelle ; un type d'un autre ecran (procedures, mesures, preventions,
///   JAL, BODACC) n'est pas lu ici. Le type « CitationAffaireSignifiee » de la fiche n'est declare dans aucune base lue (RIG_DEV).
/// CE QUI EST LU, par type :
///   - ses colonnes : les champs SELECT de PARAMETRAGE_LISTE_XLS, et parmi eux ceux de la liste par defaut, qui ont un ordre
///     (GetAllParametrageListeXlsByTypeAndNumOrdreNotNull, ParametrageListeXls.cs:64) ;
///   - ses LISTES PARAMETREES (PARAMETRAGE_LISTE_XLS_LISTE du type, GetAllParametrageListeXlsListeByType, ParametrageListeXlsListe.cs:57),
///     que la liste deroulante de la V2 propose (OPE_RECHERCHE.cs:476-482) ; et celle qu'elle choisit d'office : la ligne « LISTE » du
///     type nomme son code (GetListeParDefaut, ParametrageListeXlsListe.cs:62-73, « RIEN » dans RIG_DEV = aucune) ;
///   - puis une ligne par liste parametree, sous son type : son libelle (son code s'il est vide), ses colonnes
///     (PARAMETRAGE_LISTE_XLS_COMPOSITION, GetSqlOfListe, :78).
/// GROUPES (ceux du descripteur) : INS, IDJ, IEF, IEP, SDJ « Instances » ; GIE « Groupements » ; AFF « Affaires et citations ».
/// ETAT : « A compter » (AFaire) pour toute ligne, sans periode.
///
/// LE COMPTAGE (ChercherAsync, « Periode » remplie, lot 11 reprise) -- ce que la V2 recompte a chaque critere change
///   (OPE_RECHERCHE.cs:55-101 -> CountNbOccurencesInstance -> ExcelInstance.CountNbOccurence, ExcelInstance.cs:9-104) : une ligne par
///   type (« Type de liste » rempli : ce type seul), « SELECT COUNT(*) » sur la partie FROM de sa requete (PARAMETRAGE_LISTE.PRMLS_REQUETE
///   de LISTEXLS_<type>, la derniere clause FROM, :22-33) avec la clause WHERE du type (:38-66). Les sept FROM sont lus dans RIG_DEV
///   (SQL-DEV\DEV, lecture seule, 27/11/2026) et recrits ici sous le schema v2 : la V3 n'execute aucun texte SQL lu dans la base.
///   Les jointures de referentiel par valeur (NAT_AFF_PART, NATURE_AFFAIRE, CODE_ACTE_PROCE, CODE_AFFAIRE, CODE_EVE_PRO, RESSORT,
///   CHAMBRE, CODE_CABINET : leur cle) et par identifiant (APPEL_AFFAIRE, AUDIENCE_CABINET, PROCEDURE_COLLECTIVE) ne changent pas le
///   compte : seules restent celles qui le changent ou que la clause lit --
///     INS : INSTANCE, INSTN_DATE_ENROLEMENT entre les deux dates ;
///     IDJ : INSTANCE INNER JOIN DECISION_JUDICIAIRE DJPrincip LEFT JOIN TYPE_DECISION, ISNULL(DCJDC_DATE, DCJDC_DATE_PREVUE) entre ;
///     IEF : INSTANCE INNER JOIN EVENEMENT_PROCEDURE, ISNULL(maxi, ISNULL(mini, fixe)) entre, non survenu, delai non expire ;
///     IEP : INSTANCE INNER JOIN EVENEMENT_PROCEDURE LEFT JOIN DEPOT_PC (DPTPC_ID_EVNPR), EVNPR_DATE entre ;
///     SDJ : INSTANCE LEFT JOIN DECISION_JUDICIAIRE, enrolement entre, DCJDC_DATE IS NULL ;
///     GIE : INSTANCE, enrolement entre -- plus les fragments SQL des reglages LSTXLS_GIE_CODE_ACTE_PROCE_REQUETE et
///           LSTXLS_GIE_NATURE_AFFAIRE_REQUETE (:51-56) : un greffe qui en a pose n'est pas compte (texte SQL du parametrage), la
///           ligne le dit ;
///     AFF : les instances enrolees AVANT la date de debut sans decision definitive avant elle (:57-65 ; la fin est ignoree, :91).
///   Les bornes sont des JOURS, comme la V2 (ToShortDateString puis CONVERT(datetime, ..., 103) : BETWEEN de 00:00 a 00:00) ; une fin
///   vide prend le debut (:36). Les autres criteres de la V2 :
///     « Types d'instance » (OPE_TYPE_INSTANCE.GetSqlForTypeInstance, :15-40) : RA RC MD DD PP LS IP OP JC R F J, separes par des
///       espaces, virgules ou points-virgules -> AND ((INSTN_NUM_ROLE LIKE '%[0-9]<type>[0-9]%') OR ...) ; un code inconnu est ignore et
///       la ligne le nomme ;
///     « Codes d'evenement » (IEF et IEP seuls, OPE_RECHERCHE.cs:99 ; OPE_CODE_EVE_PRO.GetSqlForCodeEvePro, :54-60, la saisie en texte)
///       : debuts de code separes par « ; » -> AND (EVNPR_CODE_EVE_PRO LIKE '<code>%' OR ...) ;
///     « Sans sursis a statuer », « Sans ADD expertise », « Sans ADD » (IDJ seul, OPE_RECHERCHE.cs:93-97 ; ExcelInstance.cs:68-85).
///   ETAT : plus de 65 536 lignes -> EnAttente « Bloquee : trop de lignes » (TextNbOccurence_ChangementValeur, OPE_RECHERCHE.cs:466-472 :
///   le bouton Excel est grise) ; sinon EnCours « Comptee ». Cellules : le type, son nombre de lignes. Id : le type, sa periode et ses
///   criteres (Comptage, plus bas), que « Lancer l'export Excel » relit -- une ligne de GIE non comptee n'en a pas.
/// L'EXPORT : EcrivainListesExcel.cs (lot 5 du plan-f4fb097f) le pose dans la file des editions. « Exportee » n'est pas pose ici : la
///   demande d'edition se relit dans la file des editions (PROC_POOLEDITION).
/// </summary>
internal sealed class LecteurListesExcel : IRechercheEcran
{
    public string Procedure => "PROC_LISTEEXCEL";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Instances = "Instances", Groupements = "Groupements", Affaires = "Affaires et citations";

    /// <summary>Les parametres du descripteur, au mot pres.</summary>
    internal const string CritereType = "Type de liste", CriterePeriode = "Période", CritereTypes = "Types d'instance",
                          CritereCodes = "Codes d'événement", CritereSansSursis = "Sans sursis à statuer",
                          CritereSansAddExpertise = "Sans ADD expertise", CritereSansAdd = "Sans ADD";

    public IReadOnlyCollection<string> Criteres { get; } =
        [CritereType, CriterePeriode, CritereTypes, CritereCodes, CritereSansSursis, CritereSansAddExpertise, CritereSansAdd];

    /// <summary>Au-dela, Excel n'affiche pas les lignes : la V2 grise l'export (OPE_RECHERCHE.cs:467-472).</summary>
    internal const int PlafondExcel = 65536;

    /// <summary>Les cases de OPE_TYPE_INSTANCE (:18-29), dans leur ordre.</summary>
    internal static readonly string[] TypesInstance = ["RA", "RC", "MD", "DD", "PP", "LS", "IP", "OP", "JC", "R", "F", "J"];

    /// <summary>La partie FROM de chaque type (voir l'en-tete) et sa clause WHERE ; « @du » / « @au » : les jours saisis.</summary>
    private static readonly Dictionary<string, string> Comptages = new()
    {
        ["INS"] = "FROM v2.INSTANCE WHERE (INSTN_DATE_ENROLEMENT BETWEEN @du AND @au)",
        ["IDJ"] = """
            FROM v2.INSTANCE INNER JOIN v2.DECISION_JUDICIAIRE AS DJPrincip ON INSTN_ID_INSTN = DJPrincip.DCJDC_ID_INSTN
            LEFT OUTER JOIN v2.TYPE_DECISION ON R_TYPEDECISI_VALEUR = DJPrincip.DCJDC_TYPE_DECISION
            WHERE (ISNULL(DJPrincip.DCJDC_DATE, DJPrincip.DCJDC_DATE_PREVUE) BETWEEN @du AND @au)
            """,
        ["IEF"] = """
            FROM v2.INSTANCE INNER JOIN v2.EVENEMENT_PROCEDURE ON EVNPR_ID_INSTN = INSTN_ID_INSTN
            WHERE (ISNULL(EVNPR_DATE_MAXI, ISNULL(EVNPR_DATE_MINI, EVNPR_DATE_FIXE)) BETWEEN @du AND @au) AND EVNPR_SURVENU = 0 AND EVNPR_DELAI_EXPIRE = 0
            """,
        ["IEP"] = """
            FROM v2.INSTANCE INNER JOIN v2.EVENEMENT_PROCEDURE ON EVNPR_ID_INSTN = INSTN_ID_INSTN
            LEFT OUTER JOIN v2.DEPOT_PC ON DPTPC_ID_EVNPR = EVNPR_ID_EVNPR
            WHERE (EVNPR_DATE BETWEEN @du AND @au)
            """,
        ["SDJ"] = """
            FROM v2.INSTANCE LEFT OUTER JOIN v2.DECISION_JUDICIAIRE ON INSTN_ID_INSTN = DCJDC_ID_INSTN
            WHERE (INSTN_DATE_ENROLEMENT BETWEEN @du AND @au) AND DCJDC_DATE IS NULL
            """,
        ["GIE"] = "FROM v2.INSTANCE WHERE (INSTN_DATE_ENROLEMENT BETWEEN @du AND @au)",
        ["AFF"] = """
            FROM v2.INSTANCE WHERE INSTN_ID_INSTN IN (SELECT DISTINCT INSTN_ID_INSTN FROM v2.INSTANCE
                LEFT OUTER JOIN v2.DECISION_JUDICIAIRE ON DCJDC_ID_INSTN = INSTN_ID_INSTN
                WHERE INSTN_DATE_ENROLEMENT < @du
                  AND (SELECT COUNT(DCJDC_ID_DCJDC) FROM v2.DECISION_JUDICIAIRE INNER JOIN v2.TYPE_DECISION ON R_TYPEDECISI_VALEUR = DCJDC_TYPE_DECISION
                       WHERE R_TYPEDECISI_ADD_EXPERTISE <> 1 AND R_TYPEDECISI_SURSIS_STATUER <> 1 AND (ISNULL(DCJDC_AVANT_DIRE_DROIT, 0) <> '1')
                         AND DCJDC_ID_INSTN = INSTN_ID_INSTN AND ISNULL(DCJDC_DATE, DCJDC_DATE_PREVUE) < @du) = 0)
            """,
    };

    // Les types de l'ecran, dans leur ordre (une sous-requete : LectureV2 n'admet qu'un SELECT seul, sans WITH).
    // fix-ok: mesure au banc EcransGeneresBaseCheck (27/11/2026) -- InvalidOperationException « Lecture V2 refusee : seule une
    // requete SELECT seule est permise » sur la premiere version, ecrite avec WITH Types AS (...).
    private const string Types = """
        (SELECT t.PRMLLXTYP_CODE AS Type, t.PRMLLXCMP_LIBELLE AS LibelleType,
                CASE t.PRMLLXTYP_CODE WHEN 'INS' THEN 1 WHEN 'IDJ' THEN 2 WHEN 'IEF' THEN 3 WHEN 'IEP' THEN 4 WHEN 'SDJ' THEN 5
                                      WHEN 'GIE' THEN 6 ELSE 7 END AS Rang
         FROM v2.PARAMETRAGE_LISTE_XLS_TYPE_LISTE AS t
         WHERE t.PRMLLXTYP_CODE IN ('INS', 'IDJ', 'IEF', 'IEP', 'SDJ', 'GIE', 'AFF'))
        """;

    private const string Requete = $$"""
        SELECT TOP (@plafond) x.*, COUNT(*) OVER () AS Total
        FROM (SELECT ty.Rang, 0 AS Sorte, 0 AS IdListe, ty.Type, ty.LibelleType, ty.LibelleType AS Nom, CAST(NULL AS varchar(50)) AS Code,
                     (SELECT COUNT(*) FROM v2.PARAMETRAGE_LISTE_XLS AS c
                      WHERE c.PRMLX_TYPE_LISTE = ty.Type AND c.PRMLX_TYPE_TEXTE = 'SELECT' AND c.PRMLX_NUM_ORDRE IS NOT NULL) AS ColonnesDefaut,
                     (SELECT COUNT(*) FROM v2.PARAMETRAGE_LISTE_XLS AS c
                      WHERE c.PRMLX_TYPE_LISTE = ty.Type AND c.PRMLX_TYPE_TEXTE = 'SELECT') AS Colonnes,
                     (SELECT COUNT(*) FROM v2.PARAMETRAGE_LISTE_XLS_LISTE AS l WHERE l.PRMLLXLIST_TYPE = ty.Type) AS Listes,
                     (SELECT TOP (1) ISNULL(NULLIF(l.PRMLLXLIST_LIBELLE, ''), l.PRMLLXLIST_CODE)
                      FROM v2.PARAMETRAGE_LISTE_XLS_LISTE AS l
                      JOIN v2.PARAMETRAGE_LISTE_XLS AS d ON d.PRMLX_LIBELLE = l.PRMLLXLIST_CODE AND d.PRMLX_TYPE_LISTE = l.PRMLLXLIST_TYPE
                      WHERE d.PRMLX_TYPE_LISTE = ty.Type AND d.PRMLX_TYPE_TEXTE = 'LISTE'
                      ORDER BY l.PRMLLXLIST_ID_PRMLLXLIST) AS ParDefaut,
                     0 AS EstDefaut
              FROM {{Types}} AS ty
              UNION ALL
              SELECT ty.Rang, 1, l.PRMLLXLIST_ID_PRMLLXLIST, ty.Type, ty.LibelleType, ISNULL(NULLIF(l.PRMLLXLIST_LIBELLE, ''), l.PRMLLXLIST_CODE),
                     l.PRMLLXLIST_CODE,
                     (SELECT COUNT(*) FROM v2.PARAMETRAGE_LISTE_XLS_COMPOSITION AS c WHERE c.PRMLLXCMP_ID_LISTE = l.PRMLLXLIST_ID_PRMLLXLIST),
                     NULL, NULL, NULL,
                     CASE WHEN EXISTS (SELECT 1 FROM v2.PARAMETRAGE_LISTE_XLS AS d WHERE d.PRMLX_TYPE_LISTE = l.PRMLLXLIST_TYPE
                                       AND d.PRMLX_TYPE_TEXTE = 'LISTE' AND d.PRMLX_LIBELLE = l.PRMLLXLIST_CODE) THEN 1 ELSE 0 END
              FROM v2.PARAMETRAGE_LISTE_XLS_LISTE AS l
              JOIN {{Types}} AS ty ON ty.Type = l.PRMLLXLIST_TYPE) AS x
        ORDER BY x.Rang, x.Sorte, x.IdListe
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    /// <summary>Le comptage de la V2 (en tete du fichier) ; sans date de debut, la liste des types et de leurs listes.</summary>
    public async Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var bornes = (recherche.Critere(CriterePeriode) ?? "").Split('|');
        var du = CriteresConsultation.Date(bornes[0]);
        if (du is null) return await LireAsync(baseGreffe, aujourdhui, plafond, ct);
        var au = (bornes.Length > 1 ? CriteresConsultation.Date(bornes[1]) : null) ?? du.Value;

        var mots = (recherche.Critere(CritereTypes) ?? "").Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                                                          .Select(m => m.ToUpperInvariant()).Distinct().ToList();
        var types = TypesInstance.Where(mots.Contains).ToList();
        var inconnus = mots.Where(m => !TypesInstance.Contains(m)).ToList();
        var codes = (recherche.Critere(CritereCodes) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var choisi = recherche.Critere(CritereType);

        var reglages = new List<string>();
        await using (var r = await LectureV2.LireAsync(baseGreffe,
                         "SELECT Code, Valeur FROM soc.Reglage WHERE Code IN ('LSTXLS_GIE_CODE_ACTE_PROCE_REQUETE', 'LSTXLS_GIE_NATURE_AFFAIRE_REQUETE')", ct))
            while (await r.ReadAsync(ct))
                if (FormeLigne.Texte(r, "Valeur").Length > 0) reglages.Add(FormeLigne.Texte(r, "Code"));

        var typesLus = new List<(string Type, string Libelle)>();
        await using (var r = await LectureV2.LireAsync(baseGreffe, $"SELECT ty.Type, ty.LibelleType FROM {Types} AS ty ORDER BY ty.Rang", ct))
            while (await r.ReadAsync(ct))
            {
                var (type, libelle) = (FormeLigne.Texte(r, "Type"), FormeLigne.Texte(r, "LibelleType"));
                if (choisi is null || string.Equals(choisi, type, StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(choisi, libelle, StringComparison.OrdinalIgnoreCase)) typesLus.Add((type, libelle));
            }

        var lignes = new List<LigneLue>();
        foreach (var (type, libelle) in typesLus)
        {
            var periode = type == "AFF" ? $"enrôlées avant le {FormeLigne.Date(du)}" : $"du {FormeLigne.Date(du)} au {FormeLigne.Date(au)}";
            var filtres = new List<string?>
            {
                types.Count > 0 ? $"types {string.Join(", ", types)}" : null,
                inconnus.Count > 0 ? $"type{(inconnus.Count > 1 ? "s" : "")} d'instance inconnu{(inconnus.Count > 1 ? "s" : "")}, ignoré{(inconnus.Count > 1 ? "s" : "")} : {string.Join(", ", inconnus)}" : null,
            };
            if (type == "GIE" && reglages.Count > 0)
            {
                lignes.Add(new LigneLue(libelle, FormeLigne.Joindre(periode, $"non compté : le greffe filtre ce type par le texte SQL de son paramétrage ({string.Join(", ", reglages)}), que RIG v3 n'exécute pas"),
                                        Groupe(type), "AFaire", []) { Valeurs = [libelle, "non compté : filtre SQL du paramétrage"] });
                continue;
            }
            var idj = type == "IDJ";
            var comptage = new Comptage(type, du.Value.Date, au.Date, bornes.Length > 1 && CriteresConsultation.Date(bornes[1]) is not null,
                [.. types], type is "IEF" or "IEP" ? codes : [], idj && Coche(recherche.Critere(CritereSansSursis)),
                idj && Coche(recherche.Critere(CritereSansAddExpertise)), idj && Coche(recherche.Critere(CritereSansAdd)));
            var (sql, parametres, enPlus) = comptage.Requete();
            filtres.AddRange(enPlus);
            int nombre;
            await using (var r = await LectureV2.LireAsync(baseGreffe, sql, ct, parametres))
                nombre = await r.ReadAsync(ct) ? Convert.ToInt32(r["Nombre"], CultureInfo.InvariantCulture) : 0;
            var trop = nombre > PlafondExcel;
            lignes.Add(new LigneLue(libelle,
                FormeLigne.Joindre(Nombre(nombre, "ligne", "lignes", "aucune ligne"), periode, FormeLigne.Joindre([.. filtres]),
                                   trop ? "au-delà de 65 536 lignes, Excel ne les affiche pas : changez vos critères" : null),
                Groupe(type), trop ? "EnAttente" : "EnCours", []) { Id = comptage.Id, Valeurs = [libelle, nombre.ToString(CultureInfo.InvariantCulture)] });
        }
        return new LignesLues(lignes, lignes.Count);
    }

    /// <summary>
    /// Le COMPTAGE d'un type sur une periode (en tete du fichier) et l'Id de sa ligne : « type~types~codes~cases|du|au »
    /// (EditionsV2.IdSurPeriode -- la forme Etat n'envoie au geste que les Id des lignes affichees, pas ses parametres). Types : les cases
    /// de OPE_TYPE_INSTANCE ; codes : IEF et IEP seuls ; cases : S sans sursis, E sans ADD expertise, A sans ADD (IDJ seul), F la fin de
    /// periode saisie (le titre de l'export de RIG v2 ne la porte que si elle l'est, OPE_RECHERCHE.cs:246). « Lancer l'export Excel »
    /// (EcrivainListesExcel.cs) recompte par la meme requete, dans la transaction du geste.
    /// </summary>
    internal sealed record Comptage(string Type, DateTime Du, DateTime Au, bool FinSaisie, string[] Types, string[] Codes,
                                    bool SansSursis, bool SansAddExpertise, bool SansAdd)
    {
        internal string Id => EditionsV2.IdSurPeriode(
            string.Join('~', Type, string.Join(',', Types), string.Join(';', Codes),
                        (SansSursis ? "S" : "") + (SansAddExpertise ? "E" : "") + (SansAdd ? "A" : "") + (FinSaisie ? "F" : "")), Du, Au);

        /// <summary>Le comptage d'un Id de ligne ; null s'il n'en est pas un (une ligne sans periode, un type d'un autre ecran).</summary>
        internal static Comptage? Lire(string? id)
        {
            if (EditionsV2.LirePeriode(id) is not { } p) return null;
            var parts = p.Cle.Split('~');
            if (parts.Length != 4 || !Comptages.ContainsKey(parts[0])) return null;
            var types = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (types.Any(t => !TypesInstance.Contains(t))) return null;
            var cases = parts[3];
            return new Comptage(parts[0], p.Debut, p.Fin, cases.Contains('F'), types, parts[2].Split(';', StringSplitOptions.RemoveEmptyEntries),
                                cases.Contains('S'), cases.Contains('E'), cases.Contains('A'));
        }

        /// <summary>La requete du compte (une colonne « Nombre ») et ses filtres en plus de la periode et des types, en mots.</summary>
        internal (string Sql, SqlParameter[] Parametres, List<string> Filtres) Requete()
        {
            var filtres = new List<string>();
            var sql = new System.Text.StringBuilder("SELECT COUNT(*) AS Nombre ").Append(Comptages[Type]);
            var parametres = new List<SqlParameter> { new("@du", Du), new("@au", Au) };
            if (Type == "IDJ")
            {
                if (SansSursis) { sql.Append(" AND (R_TYPEDECISI_SURSIS_STATUER = 0)"); filtres.Add("sans sursis à statuer"); }
                if (SansAddExpertise) { sql.Append(" AND (R_TYPEDECISI_ADD_EXPERTISE = 0)"); filtres.Add("sans ADD expertise"); }
                if (SansAdd) { sql.Append(" AND (ISNULL(DJPrincip.DCJDC_AVANT_DIRE_DROIT, '') <> 1)"); filtres.Add("sans ADD"); }
            }
            if (Types.Length > 0)
            {
                sql.Append(" AND (").AppendJoin(" OR ", Types.Select((_, k) => $"(INSTN_NUM_ROLE LIKE @t{k})")).Append(')');
                parametres.AddRange(Types.Select((t, k) => new SqlParameter($"@t{k}", $"%[0-9]{t}[0-9]%")));
            }
            if (Type is "IEF" or "IEP" && Codes.Length > 0)
            {
                sql.Append(" AND (").AppendJoin(" OR ", Codes.Select((_, k) => $"EVNPR_CODE_EVE_PRO LIKE @c{k}")).Append(')');
                parametres.AddRange(Codes.Select((c, k) => new SqlParameter($"@c{k}", c.Replace("[", "[[]", StringComparison.Ordinal) + "%")));
                filtres.Add($"codes {string.Join(", ", Codes)}");
            }
            return (sql.ToString(), [.. parametres], filtres);
        }
    }

    private static string Groupe(string type) => type switch { "GIE" => Groupements, "AFF" => Affaires, _ => Instances };

    /// <summary>Une case cochee de l'ecran (« oui », comme les autres lecteurs et ecrivains).</summary>
    private static bool Coche(string? valeur) => valeur?.Trim().ToLowerInvariant() is "oui" or "true" or "1";

    private static string Nombre(int n, string un, string plusieurs, string aucun) =>
        n == 0 ? aucun : n == 1 ? $"1 {un}" : $"{n.ToString(CultureInfo.InvariantCulture)} {plusieurs}";

    private static LigneLue Ligne(SqlDataReader r)
    {
        var type = FormeLigne.Texte(r, "Type");
        var groupe = Groupe(type);
        var entier = (string colonne) => Convert.ToInt32(r[colonne], CultureInfo.InvariantCulture);
        string precisions;
        if (entier("Sorte") == 0)
        {
            var (colonnes, parDefaut) = (entier("Colonnes"), entier("ColonnesDefaut"));
            precisions = FormeLigne.Joindre(
                colonnes == 0 ? "aucune colonne paramétrée"
                              : $"{parDefaut.ToString(CultureInfo.InvariantCulture)} colonne{(parDefaut > 1 ? "s" : "")} par défaut sur {colonnes.ToString(CultureInfo.InvariantCulture)}",
                Nombre(entier("Listes"), "liste paramétrée", "listes paramétrées", "aucune liste paramétrée"),
                FormeLigne.Texte(r, "ParDefaut") is { Length: > 0 } defaut ? $"liste par défaut : {defaut}" : null);
        }
        else
            precisions = FormeLigne.Joindre(
                $"liste paramétrée {FormeLigne.Texte(r, "Code")}", FormeLigne.Texte(r, "LibelleType"),
                Nombre(entier("ColonnesDefaut"), "colonne", "colonnes", "aucune colonne"),
                entier("EstDefaut") == 1 ? "liste par défaut" : null);
        return new LigneLue(FormeLigne.Texte(r, "Nom"), precisions, groupe, "AFaire", []);
    }
}
