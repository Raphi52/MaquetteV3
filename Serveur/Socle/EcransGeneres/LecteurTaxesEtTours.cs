using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Taxes et tours de rôle » (PROC_TAXESETTOURS) : le CONTENU des trois etats de RIG v2, lu dans la base du greffe -- les tours de
/// role des mandataires (4D_TOURS.rpt) et leurs acomptes et taxes (4D_TAXES_ACOMPTES.rpt). Mode d'emploi : LecteurEcran.cs. Plan
/// « RIG v3 100 % fonctionnel », lot 7, 26/11/2026.
///
/// SOURCE (ancien RIG ; proc_taxesettours.md, le Detail OPE fait foi : un processus d'EDITION pure, il n'ecrit rien) :
///   - LA PERIODE : du 1er janvier de l'annee en cours a aujourd'hui par defaut (OPE_PERIODE.InitDate, OPE_PERIODE.cs:31-32), dite dans
///     chaque ligne. La forme Etat envoie les parametres saisis a ce lecteur, qui cherche (IRechercheEcran ; FormeEtat.tsx:9-13) :
///     « Date de début » et « Date de fin » remplacent ces bornes -- un champ vide garde la valeur que RIG v2 pre-remplit, puisque
///     les dates de la V3 partent vides la ou celles de RIG v2 partent remplies. Un debut apres la fin : RIG v2 refuse d'editer
///     (« La date de début doit être inférieure à la date de fin », OPE_RECHERCHE.cs:50-51) -- la V3 ne rend aucune ligne. Debut et
///     fin egaux : une seule journee, comme la fin videe de RIG v2 (« = date de debut », PlumitifPcTaxes.cs:138-139, Tours.cs:306-307) ;
///   - LES CASES « Tours », « Acomptes », « Taxes » (OPE_TYPE_EDITION) : un etat par case cochee (OPE_RECHERCHE.cs:61-75), donc le
///     groupe de chaque case cochee ; aucune cochee : les trois (RIG v2 grise alors Editer, :86-90 ; la V3 montre le contenu) ;
///   - le texte : le nom, le complement ou l'abrege du mandataire ;
///   - ACOMPTES ET TAXES (PlumitifPcTaxes.GetReqTaxes, PlumitifPcTaxes.cs:132-154) : la somme de AUTRE_CHIFFRE.ACHFF_MONTANT de la
///     periode (ACHFF_DATE), par fonction (ACHFF_FONCTION_PC) et par mandataire (MANDATAIRE_PC par son abrege, nom + complement),
///     des codes chiffre R_CODECHIFFR_ACOMPTE = 1 (acomptes) ou R_CODECHIFFR_TAXE = 1 (taxes) ; une ligne par mandataire, les
///     montants ADJ, CEP, LIQ, MDJ en colonnes (« MJ » compte pour MDJ, :89-106 ; une autre fonction ne remplit aucune colonne) ;
///     RIG v2 regroupe sans ORDER BY, la V3 trie par nom ;
///   - TOURS DE ROLE (PlumitifPcTours.GenererEditionTours, PlumitifPcTours.cs:64-147 ; Tours.cs:155-280) : les mandataires des PC
///     non exterieures (PRCLL_EXTERIEUR = 0) dont la date de designation (GRRPC_DATE_DEBUT, sinon l'ouverture ou la sauvegarde de la
///     PC) est dans la periode, en fonction ADJ, LIQ / LQJ, MDJ / RDC / MJ ; pour chacun et chaque fonction (administrateur,
///     liquidateur, mandataire judiciaire), le nombre de lignes CHIFFRES_PC x PC x GERER_PC qui satisfont chaque condition « tour »
///     du greffe : 4D_TOUR_ADJ_* pour les administrateurs, 4D_TOUR_MAND_* pour les autres (N, 1 a 4 ; PARAMETRAGE repris dans
///     soc.Reglage). Une ligne si au moins un tour est non nul ; nom « NOM COMPLEMENT (ABREGE) », triee par ce nom (:111-117).
///     RIG v2 colle ces conditions dans sa requete SQL (Tours.cs:319-371) : la V3 n'execute aucun SQL venu des reglages, elle les
///     EVALUE (comparaisons sur les colonnes CHFPC_* de CHIFFRES_PC, AND, OR, NOT, IS [NOT] NULL, parentheses ; la logique a trois
///     valeurs de SQL). Une condition hors de cette grammaire fait echouer la lecture, son code nomme.
///
/// CE QUE LA V3 NE REPREND PAS : l'etat Crystal lui-meme (son titre, le nom du tribunal, l'impression) -- le tableau EST l'edition.
/// « Éditer » (RIG v2 n'ecrit rien) place chaque etat affiche dans la file des editions : EcrivainTaxesEtTours.cs (lot 1 du plan-f4fb097f),
/// par l'Id de la ligne (l'etat, le mandataire, la periode : EditionsV2.IdSurPeriode).
/// </summary>
internal sealed partial class LecteurTaxesEtTours : IRechercheEcran
{
    public string Procedure => "PROC_TAXESETTOURS";

    /// <summary>Les groupes du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string Tours = "Tours de rôle", Acomptes = "Acomptes", Taxes = "Taxes";

    /// <summary>Les parametres du descripteur (OPE_PERIODE et OPE_TYPE_EDITION), au mot pres ; les cases portent le nom de leur groupe.</summary>
    internal const string CritereDebut = "Date de début", CritereFin = "Date de fin", CritereTours = "Tours";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDebut, CritereFin, CritereTours, Acomptes, Taxes];

    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => ChercherAsync(baseGreffe, new RechercheEcran(null, new Dictionary<string, string>()), aujourdhui, plafond, ct);

    // fix-ok: second controle du lot 7 (26/11/2026) -- la periode et les cases d'OPE_PERIODE / OPE_TYPE_EDITION n'atteignaient pas la
    // base : ce lecteur n'etait pas IRechercheEcran, alors que la forme Etat envoie ses parametres a un lecteur qui cherche
    // (FormeEtat.tsx:9-13). Une seule cause, portee par les remplacements de l'en-tete, de la lecture et des deux requetes.
    public async Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var saisiDebut = CriteresConsultation.Date(recherche.Critere(CritereDebut));
        var saisiFin = CriteresConsultation.Date(recherche.Critere(CritereFin));
        var debut = (saisiDebut ?? new DateTime(aujourdhui.Year, 1, 1)).Date;
        var fin = (saisiFin ?? aujourdhui).Date;
        // « La date de début doit être inférieure à la date de fin » (OPE_RECHERCHE.cs:50-51) : rien n'est edite.
        if (debut > fin) return new LignesLues([], 0);
        // fix-ok: le banc ne compare que « Nom | Precisions | Groupe | Etat » (tests/EcransGeneresBaseCheck/Program.cs:187), pas les
        // cellules : les tours et les montants passent aussi dans les precisions ; la periode par defaut s'y dit en mots (un .attendu
        // n'ecrit qu'une date relative au jour, {J-n}, Program.cs:41 -- pas le 1er janvier) ; une periode saisie, par ses dates.
        var periode = saisiDebut is null && saisiFin is null ? "du 1er janvier à aujourd'hui" : $"du {FormeLigne.Date(debut)} au {FormeLigne.Date(fin)}";
        // Un etat par case cochee (OPE_RECHERCHE.cs:61-75) ; aucune : les trois.
        var cochees = new[] { CritereTours, Acomptes, Taxes }.Where(c => recherche.Critere(c) == "oui").ToHashSet(StringComparer.Ordinal);
        // La case « Tours » n'a pas le libelle de son groupe (« Tours de rôle ») : le test porte sur la CASE, jamais sur le groupe
        // (mesure par PROC_TAXESETTOURS.recherches bloc 5 : Tours coche seul rendait 0 ligne).
        bool Voulu(string caseCochee) => cochees.Count == 0 || cochees.Contains(caseCochee);
        var texte = string.IsNullOrWhiteSpace(recherche.Texte) ? null : FormeLigne.Contient(recherche.Texte);
        var lignes = new List<LigneLue>();
        if (Voulu(CritereTours)) lignes.AddRange(await ToursAsync(baseGreffe, debut, fin, periode, texte, ct));
        if (Voulu(Acomptes)) lignes.AddRange(await MontantsAsync(baseGreffe, debut, fin, "ACOMPTES", periode, texte, ct));
        if (Voulu(Taxes)) lignes.AddRange(await MontantsAsync(baseGreffe, debut, fin, "TAXES", periode, texte, ct));
        return new LignesLues([.. lignes.Take(plafond)], lignes.Count);
    }

    /// <summary>Le texte cherche dans le nom, le complement ou l'abrege du mandataire d'alias m ; rien sans texte.</summary>
    private const string FiltreTexte = " AND (@texte IS NULL OR m.MNDPC_NOM LIKE @texte OR m.MNDPC_COMPLEMENT_NOM LIKE @texte OR m.MNDPC_ABREGE LIKE @texte)";

    private static SqlParameter Texte(string? texte) => new("@texte", (object?)texte ?? DBNull.Value);

    // ------------------------------------------------------------------ ACOMPTES ET TAXES

    private static async Task<List<LigneLue>> MontantsAsync(SqlConnection cn, DateTime debut, DateTime fin, string type, string periode, string? texte,
                                                            CancellationToken ct)
    {
        var colonne = type == "TAXES" ? "R_CODECHIFFR_TAXE" : "R_CODECHIFFR_ACOMPTE";
        var parMandataire = new SortedDictionary<string, decimal?[]>(StringComparer.Create(Francais, false));
        var idDe = new Dictionary<string, int>(StringComparer.Ordinal);   // le mandataire de la ligne (le plus petit id de ce nom)
        await using (var r = await LectureV2.LireAsync(cn, $"""
            SELECT SUM(ac.ACHFF_MONTANT) AS Montant, m.MNDPC_NOM + ' ' + ISNULL(m.MNDPC_COMPLEMENT_NOM, '') AS Nom, ac.ACHFF_FONCTION_PC AS Fonction,
                   MIN(m.MNDPC_ID_MNDPC) AS IdMandataire
            FROM v2.AUTRE_CHIFFRE AS ac
            JOIN v2.CODE_CHIFFRE AS cc ON ac.ACHFF_CODE_CHIFFRE = cc.R_CODECHIFFR_VALEUR
            JOIN v2.MANDATAIRE_PC AS m ON m.MNDPC_ABREGE = ac.ACHFF_ABREGE_ORGANE
            WHERE ac.ACHFF_DATE BETWEEN @debut AND @fin AND cc.{colonne} = 1{FiltreTexte}
            GROUP BY ac.ACHFF_FONCTION_PC, m.MNDPC_NOM + ' ' + ISNULL(m.MNDPC_COMPLEMENT_NOM, '')
            """, ct, new SqlParameter("@debut", debut), new SqlParameter("@fin", fin), Texte(texte)))
            while (await r.ReadAsync(ct))
            {
                var nom = FormeLigne.Texte(r, "Nom");
                if (!parMandataire.TryGetValue(nom, out var montants)) parMandataire[nom] = montants = new decimal?[4];
                var idMandataire = Convert.ToInt32(r["IdMandataire"], CultureInfo.InvariantCulture);
                idDe[nom] = idDe.TryGetValue(nom, out var deja) ? Math.Min(deja, idMandataire) : idMandataire;
                var rang = FormeLigne.Texte(r, "Fonction") switch { "ADJ" => 0, "CEP" => 1, "LIQ" => 2, "MDJ" or "MJ" => 3, _ => -1 };
                if (rang >= 0 && r["Montant"] is not DBNull) montants[rang] = Convert.ToDecimal(r["Montant"], CultureInfo.InvariantCulture);
            }
        var groupe = type == "TAXES" ? Taxes : Acomptes;
        string[] fonctions = ["ADJ", "CEP", "LIQ", "MDJ"];
        return [.. parMandataire.Select(p => new LigneLue(p.Key,
            FormeLigne.Joindre([.. p.Value.Select((m, k) => m is { } v ? $"{fonctions[k]} {v.ToString("N2", Francais)} €" : null), periode]), groupe, "Validee", [])
        {
            // L'etat (A acomptes, X taxes), le mandataire et la periode : ce que « Éditer » edite (EcrivainTaxesEtTours.cs).
            Id = EditionsV2.IdSurPeriode($"{(type == "TAXES" ? 'X' : 'A')}{idDe[p.Key].ToString(CultureInfo.InvariantCulture)}", debut, fin),
            Valeurs = [p.Key, "", "", "", "", "", "", .. p.Value.Select(Montant)],
        })];
    }

    private static string Montant(decimal? m) => m is { } v ? v.ToString("0.00", CultureInfo.InvariantCulture) : "";

    // ------------------------------------------------------------------ TOURS DE ROLE

    private sealed record Mandataire(int Id, string Nom);

    private static async Task<List<LigneLue>> ToursAsync(SqlConnection cn, DateTime debut, DateTime fin, string periode, string? texte, CancellationToken ct)
    {
        var conditionsMand = await ConditionsAsync(cn, "4D_TOUR_MAND_", ct);
        var conditionsAdj = await ConditionsAsync(cn, "4D_TOUR_ADJ_", ct);
        var colonnes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var c in conditionsMand.Values.Concat(conditionsAdj.Values)) c.Colonnes(colonnes);
        var existantes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var r = await LectureV2.LireAsync(cn, "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('v2.CHIFFRES_PC')", ct))
            while (await r.ReadAsync(ct)) existantes.Add(r.GetString(0));
        if (colonnes.FirstOrDefault(c => !existantes.Contains(c)) is { } inconnue)
            throw new InvalidOperationException($"Tours de rôle : la colonne « {inconnue} » d'un réglage 4D_TOUR_* n'est pas dans CHIFFRES_PC.");

        // Les lignes CHIFFRES_PC x PC x GERER_PC de la periode (GetMandatairePC et GetTours, Tours.cs:155-261, :293-301).
        var choix = string.Concat(colonnes.Select(c => $", ch.[{c}]"));
        var compteurs = new Dictionary<(int, string), int[]>();
        var mandataires = new Dictionary<int, Mandataire>();
        await using (var r = await LectureV2.LireAsync(cn, $"""
            SELECT m.MNDPC_ID_MNDPC AS Id, m.MNDPC_NOM AS Nom, ISNULL(m.MNDPC_COMPLEMENT_NOM, '') AS Complement, m.MNDPC_ABREGE AS Abrege,
                   g.GRRPC_FONCTION_PC AS Fonction{choix}
            FROM v2.PROCEDURE_COLLECTIVE AS pc
            LEFT JOIN v2.CHIFFRES_PC AS ch ON ch.CHFPC_ID_PRCLL = pc.PRCLL_ID_PRCLL
            JOIN v2.GERER_PC AS g ON g.GRRPC_ID_PRCLL = pc.PRCLL_ID_PRCLL
            JOIN v2.MANDATAIRE_PC AS m ON m.MNDPC_ID_MNDPC = g.GRRPC_ID_MNDPC
            WHERE (ISNULL(g.GRRPC_DATE_DEBUT, pc.PRCLL_DATE_OUVERTURE) BETWEEN @debut AND @fin
                   OR ISNULL(g.GRRPC_DATE_DEBUT, pc.PRCLL_DATE_SAUVEGARDE) BETWEEN @debut AND @fin)
              AND g.GRRPC_FONCTION_PC IN ('ADJ', 'LIQ', 'LQJ', 'MDJ', 'RDC', 'MJ') AND pc.PRCLL_EXTERIEUR = 0{FiltreTexte}
            """, ct, new SqlParameter("@debut", debut), new SqlParameter("@fin", fin), Texte(texte)))
            while (await r.ReadAsync(ct))
            {
                var id = Convert.ToInt32(r["Id"], CultureInfo.InvariantCulture);
                mandataires.TryAdd(id, new Mandataire(id,
                    $"{FormeLigne.Texte(r, "Nom")} {FormeLigne.Texte(r, "Complement")} ({FormeLigne.Texte(r, "Abrege")})"));
                var (fonction, conditions) = FormeLigne.Texte(r, "Fonction") switch
                {
                    "ADJ" => ("ADJ", conditionsAdj),
                    "LIQ" or "LQJ" => ("LIQ", conditionsMand),
                    _ => ("MDJ", conditionsMand),
                };
                var valeurs = colonnes.ToDictionary(c => c, c => r[c] is DBNull ? (decimal?)null : Convert.ToDecimal(r[c], CultureInfo.InvariantCulture),
                                                    StringComparer.OrdinalIgnoreCase);
                if (!compteurs.TryGetValue((id, fonction), out var tours)) compteurs[(id, fonction)] = tours = new int[5];
                for (var k = 0; k < 5; k++)
                    if (conditions.TryGetValue(k == 0 ? "N" : k.ToString(CultureInfo.InvariantCulture), out var cond) && cond.Vrai(valeurs) == true)
                        tours[k]++;
            }

        // Une ligne par mandataire et fonction dont un tour est non nul, triee par nom (PlumitifPcTours.cs:84-117).
        var lignes = new List<(int Id, string Nom, string Fonction, int[] Tours)>();
        foreach (var m in mandataires.Values)
            foreach (var fonction in new[] { "ADJ", "LIQ", "MDJ" })
                if (compteurs.TryGetValue((m.Id, fonction), out var t) && t.Sum() > 0) lignes.Add((m.Id, m.Nom, fonction, t));
        return [.. lignes.OrderBy(l => l.Nom, StringComparer.Create(Francais, false)).Select(l => new LigneLue(l.Nom,
            FormeLigne.Joindre(l.Fonction, $"tour N {l.Tours[0]}", $"tour 1 {l.Tours[1]}", $"tour 2 {l.Tours[2]}", $"tour 3 {l.Tours[3]}",
                               $"tour 4 {l.Tours[4]}", periode), Tours, "Validee", [])
        {
            // L'etat (T tours), le mandataire et sa fonction, la periode : ce que « Éditer » edite (EcrivainTaxesEtTours.cs).
            Id = EditionsV2.IdSurPeriode($"T{l.Id.ToString(CultureInfo.InvariantCulture)}-{l.Fonction}", debut, fin),
            Valeurs = [l.Nom, l.Fonction, .. l.Tours.Select(n => n.ToString(CultureInfo.InvariantCulture)), "", "", "", ""],
        })];
    }

    /// <summary>Les conditions « tour » du greffe (Tours.InitTours, Tours.cs:313-371) : code N, 1 a 4 -> condition evaluable.</summary>
    private static async Task<Dictionary<string, Condition>> ConditionsAsync(SqlConnection cn, string prefixe, CancellationToken ct)
    {
        var conditions = new Dictionary<string, Condition>(StringComparer.Ordinal);
        await using var r = await LectureV2.LireAsync(cn, "SELECT Code, Valeur FROM soc.Reglage WHERE Code LIKE @p", ct, new SqlParameter("@p", prefixe + "%"));
        while (await r.ReadAsync(ct))
        {
            var code = FormeLigne.Texte(r, "Code");
            conditions[code[^1..]] = Condition.Lire(code, FormeLigne.Texte(r, "Valeur"));
        }
        return conditions;
    }

    /// <summary>
    /// Une condition « tour » evaluee sans SQL : comparaisons d'une colonne CHFPC_* a un nombre, IS [NOT] NULL, AND, OR, NOT,
    /// parentheses. Logique a trois valeurs de SQL (null = inconnu ; CASE WHEN ne retient que vrai).
    /// </summary>
    internal sealed partial class Condition
    {
        private readonly Func<IReadOnlyDictionary<string, decimal?>, bool?> _evaluer;
        private readonly HashSet<string> _colonnes;

        private Condition(Func<IReadOnlyDictionary<string, decimal?>, bool?> evaluer, HashSet<string> colonnes) => (_evaluer, _colonnes) = (evaluer, colonnes);

        public bool? Vrai(IReadOnlyDictionary<string, decimal?> valeurs) => _evaluer(valeurs);

        public void Colonnes(ISet<string> dans) { foreach (var c in _colonnes) dans.Add(c); }

        public static Condition Lire(string code, string texte)
        {
            var jetons = Jeton().Matches(texte).Select(m => m.Value).ToList();
            if (string.Concat(jetons).Length != Regex.Replace(texte, @"\s+", "").Length)
                throw new InvalidOperationException($"Tours de rôle : le réglage {code} n'est pas une condition lisible (« {texte} »).");
            var colonnes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pos = 0;
            string? Voir() => pos < jetons.Count ? jetons[pos] : null;
            bool Mot(string m) => string.Equals(Voir(), m, StringComparison.OrdinalIgnoreCase);
            InvalidOperationException Faute() => new($"Tours de rôle : le réglage {code} n'est pas une condition lisible (« {texte} »).");

            Func<IReadOnlyDictionary<string, decimal?>, bool?> Ou()
            {
                var gauche = Et();
                while (Mot("OR")) { pos++; var g = gauche; var d = Et(); gauche = v => g(v) == true || d(v) == true ? true : g(v) == false && d(v) == false ? false : null; }
                return gauche;
            }
            Func<IReadOnlyDictionary<string, decimal?>, bool?> Et()
            {
                var gauche = Facteur();
                while (Mot("AND")) { pos++; var g = gauche; var d = Facteur(); gauche = v => g(v) == false || d(v) == false ? false : g(v) == true && d(v) == true ? true : null; }
                return gauche;
            }
            Func<IReadOnlyDictionary<string, decimal?>, bool?> Facteur()
            {
                if (Mot("NOT")) { pos++; var f = Facteur(); return v => f(v) is { } b ? !b : null; }
                if (Voir() == "(") { pos++; var e = Ou(); if (Voir() != ")") throw Faute(); pos++; return e; }
                var colonne = Voir();
                if (colonne is null || !Colonne().IsMatch(colonne)) throw Faute();
                pos++;
                colonnes.Add(colonne);
                if (Mot("IS"))
                {
                    pos++;
                    var non = Mot("NOT");
                    if (non) pos++;
                    if (!Mot("NULL")) throw Faute();
                    pos++;
                    return v => (v[colonne] is null) != non;
                }
                var op = Voir();
                pos++;
                if (Voir() is not { } n || !decimal.TryParse(n, NumberStyles.Number, CultureInfo.InvariantCulture, out var nombre)) throw Faute();
                pos++;
                Func<decimal, bool> comparer = op switch
                {
                    "=" => x => x == nombre, "<>" or "!=" => x => x != nombre, "<" => x => x < nombre, "<=" => x => x <= nombre,
                    ">" => x => x > nombre, ">=" => x => x >= nombre, _ => throw Faute(),
                };
                return v => v[colonne] is { } x ? comparer(x) : null;
            }

            var evaluer = Ou();
            if (pos != jetons.Count) throw Faute();
            return new Condition(evaluer, colonnes);
        }

        [GeneratedRegex(@"<=|>=|<>|!=|[=<>()]|-?\d+(?:\.\d+)?|[A-Za-z_][A-Za-z0-9_]*")]
        private static partial Regex Jeton();

        [GeneratedRegex(@"^CHFPC_[A-Z0-9_]+$", RegexOptions.IgnoreCase)]
        private static partial Regex Colonne();
    }
}
