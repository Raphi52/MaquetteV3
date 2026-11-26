using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Recopie;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Pilotage » (PROC_XPILOTAGE) : le tableau de pilotage de la cour d'appel, CALCULE dans la base du greffe. Un ETAT en
/// lecture seule : une ligne par cellule pilotee, sa valeur sur la periode et sur la meme periode un an plus tot. Mode d'emploi :
/// LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 9, 26/11/2026.
///
/// SOURCE (ancien RIG) : proc_xpilotage.md (Detail OPE, qui fait foi) et Source\RIG\DLL\Processus\PROC_XPILOTAGE\OPE_GENERER.cs :
///   - le tableau n'est pas code : chaque ligne de la table PILOTAGE_REQUETES (lue entiere, PilotageRequetes.cs:31-44) est UNE
///     cellule -- sa requete PILOT_SQL (ignoree si vide, :282), sa feuille PILOT_FEUILLE (« 1 » RCS, « 2 » judiciaire, :259-276),
///     sa place PILOT_LIGNE / PILOT_COLONNE, son libelle, son traitement PILOT_TRAITEMENT ;
///   - la periode par defaut : le mois precedent complet (InitDate, :123-143), la fin portee a 23:59:59 (:253-254) ; les jetons
///     @DateMin / @DateMax de la requete recoivent les bornes (:284-285) ; la meme requete rejouee un an plus tot (AddYears(-1),
///     :170-171, :310-333) donne la colonne PILOT_COLONNE + 1, sauf en « PASANNEEPREC » quand une ligne a ete lue (:297-300) ;
///   - la valeur lue est la colonne NB de la premiere ligne, ecrite si non nulle (:290-292) ; une requete en erreur n'arrete pas
///     le tableau, son libelle est signale (:306-308).
/// Les jetons deviennent des PARAMETRES SQL (RIG v2 les concatene en texte, culture du poste : 🐞 de la fiche), et les tables de
/// la requete, ecrites sans schema, sont lues dans v2.* de la base V3 (la copie de l'ancien RIG).
/// GROUPE : « Feuille RCS » (1), « Feuille judiciaire » (2) ; une autre feuille n'est pas generee (garde :273). ORDRE : feuille,
/// ligne, colonne (RIG v2 n'en a pas : PilotageRequetes.cs:33 sans ORDER BY). ETAT : « Calculé » (Validee) ou « Non calculé »
/// (Rejetee), la cause dans la colonne Calcul.
///
/// CE QUE LA V3 NE CALCULE PAS, nomme dans la colonne Calcul de la ligne : une requete qui lit une table reprise en fiche rec.*
/// (DOSSIER_RCS -> rec.DossierRcs, ACTION_RCS, CLIENT, CATEGORIE, INSCRIPTION... : Serveur/Recopie/FichesV3.cs) ou absente de la
/// base V3 -- la requete de RIG v2 ne la trouve pas, et la traduire serait une autre requete ; une requete qui n'est pas un SELECT
/// seul (« declare ... ») -- LectureV2 la refuse ; le traitement « ENCAISS » (TraiterEncaiss, :365-407, un calcul ligne par ligne
/// sur les clients et les debours). Le classeur Excel (le modele XPILOTAGE.XLT, les feuilles, ARCHIVES) et la Demande XPILOTAGE
/// du travail de fond (:108-121) ne sont pas produits : la V3 n'a pas encore d'edition de ce genre.
/// </summary>
internal sealed partial class LecteurPilotage : IRechercheEcran
{
    public string Procedure => "PROC_XPILOTAGE";

    /// <summary>Les groupes et les criteres du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string FeuilleRcs = "Feuille RCS", FeuilleJudiciaire = "Feuille judiciaire";
    internal const string CritereDebut = "Date minimale", CritereFin = "Date maximale", CritereFeuille = "Feuille";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDebut, CritereFin, CritereFeuille];

    private const string Requetes = """
        SELECT PILOT_ID_PILOT, PILOT_FEUILLE, PILOT_LIGNE, PILOT_COLONNE, PILOT_LIBELLE, PILOT_TRAITEMENT, PILOT_SQL
        FROM v2.PILOTAGE_REQUETES
        WHERE LTRIM(RTRIM(ISNULL(PILOT_SQL, ''))) <> '' AND LTRIM(RTRIM(PILOT_FEUILLE)) IN ('1', '2') /*texte*/
        ORDER BY PILOT_FEUILLE, PILOT_LIGNE, PILOT_COLONNE, PILOT_ID_PILOT
        """;

    private const string TablesV2 = "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('v2')";

    /// <summary>La periode par defaut de RIG v2 : le mois precedent complet (InitDate, OPE_GENERER.cs:123-143).</summary>
    internal static (DateTime Debut, DateTime Fin) MoisPrecedent(DateTime aujourdhui)
    {
        var debut = new DateTime(aujourdhui.Year, aujourdhui.Month, 1).AddMonths(-1);
        return (debut, debut.AddMonths(1).AddDays(-1));
    }

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var (debut, fin) = MoisPrecedent(aujourdhui);
        return CalculerAsync(baseGreffe, debut, fin, null, null, plafond, ct);
    }

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var (debutDefaut, finDefaut) = MoisPrecedent(aujourdhui);
        var debut = CriteresConsultation.Date(recherche.Critere(CritereDebut)) ?? debutDefaut;
        var fin = CriteresConsultation.Date(recherche.Critere(CritereFin)) ?? finDefaut;
        // Les gardes de GenererDocument (OPE_GENERER.cs:159-162) : debut <= fin, l'une et l'autre au plus aujourd'hui. RIG v2
        // refuse de generer : ici, aucune cellule n'est calculee.
        if (debut > fin || debut > aujourdhui.Date || fin > aujourdhui.Date) return Task.FromResult(new LignesLues([], 0));
        var feuille = recherche.Critere(CritereFeuille) switch
        {
            "RCS" => "1",
            "Judiciaire" => "2",
            _ => null,
        };
        return CalculerAsync(baseGreffe, debut, fin, feuille, string.IsNullOrWhiteSpace(recherche.Texte) ? null : recherche.Texte, plafond, ct);
    }

    private sealed record Cellule(int Id, string Feuille, int? Ligne, int? Colonne, string Libelle, string Traitement, string Sql);

    private async Task<LignesLues> CalculerAsync(SqlConnection baseGreffe, DateTime debut, DateTime fin, string? feuille, string? texte,
                                                 int plafond, CancellationToken ct)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var r = await LectureV2.LireAsync(baseGreffe, TablesV2, ct))
            while (await r.ReadAsync(ct)) tables.Add(r.GetString(0));

        var filtres = "";
        var parametres = new List<SqlParameter>();
        if (feuille is not null) { filtres += " AND LTRIM(RTRIM(PILOT_FEUILLE)) = @feuille"; parametres.Add(new SqlParameter("@feuille", feuille)); }
        if (texte is not null) { filtres += " AND PILOT_LIBELLE LIKE @texte"; parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(texte))); }
        var cellules = new List<Cellule>();
        await using (var r = await LectureV2.LireAsync(baseGreffe, Requetes.Replace("/*texte*/", filtres, StringComparison.Ordinal), ct, [.. parametres]))
            while (await r.ReadAsync(ct))
                cellules.Add(new Cellule(Convert.ToInt32(r["PILOT_ID_PILOT"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "PILOT_FEUILLE"),
                    r["PILOT_LIGNE"] as int?, r["PILOT_COLONNE"] as int?, FormeLigne.Texte(r, "PILOT_LIBELLE"),
                    FormeLigne.Texte(r, "PILOT_TRAITEMENT").ToUpperInvariant(), FormeLigne.Texte(r, "PILOT_SQL")));

        // La fin portee a 23:59:59 (OPE_GENERER.cs:253-254), et la meme periode un an plus tot (:170-171).
        var finJour = fin.Date.AddDays(1).AddSeconds(-1);
        var (debutN1, finN1) = (debut.AddYears(-1), fin.AddYears(-1).Date.AddDays(1).AddSeconds(-1));
        var periode = $"du {FormeLigne.Date(debut)} au {FormeLigne.Date(fin)}";
        var lignes = new List<LigneLue>();
        foreach (var c in cellules.Take(plafond))
        {
            var groupe = c.Feuille == "1" ? FeuilleRcs : FeuilleJudiciaire;
            var nom = c.Libelle.Length > 0 ? c.Libelle : $"Cellule {c.Ligne}, {c.Colonne}";
            var place = $"ligne {c.Ligne} · colonne {c.Colonne}";
            var cause = Cause(c, tables, out var sql);
            if (cause is not null)
            {
                lignes.Add(new LigneLue(nom, $"non calculé : {cause}", groupe, "Rejetee", [])
                {
                    Valeurs = [nom, place, "", "", $"non calculé : {cause}"],
                });
                continue;
            }
            var (valeur, erreur, lue) = await NbAsync(baseGreffe, sql!, debut, finJour, ct);
            // L'annee precedente : sauf « PASANNEEPREC » quand une ligne a ete lue (:297-300, :310).
            string n1;
            if (c.Traitement == "PASANNEEPREC" && lue) n1 = "sans objet";
            else
            {
                var (v1, e1, _) = await NbAsync(baseGreffe, sql!, debutN1, finN1, ct);
                n1 = e1 is not null ? "erreur" : v1;
                erreur ??= e1;
            }
            var calcul = erreur is null ? $"calculé {periode}" : $"requête de RIG v2 en erreur : {erreur}";
            // Les precisions portent aussi les deux valeurs : ce que le banc compare (le texte de la ligne, pas ses cellules) ; la
            // periode, elle, est dans la colonne Calcul. fix-ok: lu dans tests/EcransGeneresBaseCheck/Program.cs:187 (Texte = nom,
            // precisions, groupe, etat : les Valeurs ne sont pas comparees) et :188-194 ({M-1} = meme jour du mois precedent, pas le
            // 1er) -- les chiffres doivent etre dans les precisions, la periode du mois precedent ne peut pas y etre.
            var precisions = FormeLigne.Joindre($"période {(erreur is null ? (valeur.Length > 0 ? valeur : "vide") : "erreur")}",
                                                $"un an plus tôt {(n1.Length > 0 ? n1 : "vide")}", erreur is null ? null : calcul);
            lignes.Add(new LigneLue(nom, precisions, groupe, erreur is null ? "Validee" : "Rejetee", [])
            {
                Valeurs = [nom, place, erreur is null ? valeur : "erreur", n1, calcul],
            });
        }
        return new LignesLues(lignes, cellules.Count);
    }

    /// <summary>
    /// Pourquoi la requete ne se calcule pas dans la base V3 (null : elle se calcule), et sinon la requete aux tables v2.* : une
    /// table de FROM / JOIN qui n'est pas dans v2.* (reprise en fiche rec.*, ou absente), une requete qui n'est pas un SELECT seul,
    /// le traitement ENCAISS.
    /// </summary>
    private static string? Cause(Cellule c, HashSet<string> tablesV2, out string? sql)
    {
        sql = null;
        if (c.Traitement == "ENCAISS") return "calcul d'encaissement ligne par ligne (TraiterEncaiss) non repris";
        var manquantes = new List<string>();
        var traduite = TableDeLaRequete().Replace(c.Sql, m =>
        {
            var table = m.Groups["table"].Value;
            if (tablesV2.Contains(table)) return $"{m.Groups["mot"].Value} v2.{table}";
            manquantes.Add(table.ToUpperInvariant());
            return m.Value;
        });
        if (manquantes.Count > 0)
        {
            var noms = manquantes.Distinct().Select(t => ReglesFichesV3.Fiches.FirstOrDefault(f => string.Equals(f.Recopie.TableV2, t, StringComparison.OrdinalIgnoreCase)) is { } f
                ? $"{t} (reprise en {f.Recopie.TableV3})" : $"{t} (absente de la base V3)");
            return $"la requête de RIG v2 lit {string.Join(", ", noms)}";
        }
        try { LectureV2.ExigerLecture(traduite); }
        catch (InvalidOperationException) { return "la requête de RIG v2 n'est pas un SELECT seul"; }
        sql = traduite;
        return null;
    }

    /// <summary>La colonne NB de la premiere ligne (OPE_GENERER.cs:290-292) : (valeur, erreur, une ligne lue).</summary>
    private static async Task<(string Valeur, string? Erreur, bool Lue)> NbAsync(SqlConnection baseGreffe, string sql, DateTime debut, DateTime fin,
                                                                               CancellationToken ct)
    {
        try
        {
            await using var r = await LectureV2.LireAsync(baseGreffe, sql, ct,
                new SqlParameter("@DateMin", System.Data.SqlDbType.DateTime) { Value = debut },
                new SqlParameter("@DateMax", System.Data.SqlDbType.DateTime) { Value = fin });
            if (!await r.ReadAsync(ct)) return ("", null, false);
            var k = Enumerable.Range(0, r.FieldCount).FirstOrDefault(i => string.Equals(r.GetName(i), "NB", StringComparison.OrdinalIgnoreCase), -1);
            if (k < 0) return ("", "pas de colonne NB", true);
            return (r.IsDBNull(k) ? "" : Nombre(r.GetValue(k)), null, true);
        }
        catch (SqlException e)
        {
            return ("", e.Message, false);
        }
    }

    private static string Nombre(object v) => v switch
    {
        decimal d => d.ToString("0.####", CultureInfo.GetCultureInfo("fr-FR")),
        double d => d.ToString("0.####", CultureInfo.GetCultureInfo("fr-FR")),
        float f => f.ToString("0.####", CultureInfo.GetCultureInfo("fr-FR")),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    // Une table d'une clause FROM ou JOIN, ecrite sans schema ou en dbo (« FROM DOSSIER_RCS », « JOIN [dbo].[INSTANCE] »).
    [GeneratedRegex(@"\b(?<mot>FROM|JOIN)\s+(?:\[?dbo\]?\.)?\[?(?<table>[A-Za-z_][A-Za-z0-9_]*)\]?(?![\w.(])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TableDeLaRequete();
}
