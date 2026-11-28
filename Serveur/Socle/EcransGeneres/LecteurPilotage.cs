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
/// la requete, ecrites sans schema, sont lues dans v2.* de la base V3 (la copie de l'ancien RIG). Une table reprise en fiche rec.*
/// (DOSSIER_RCS -> rec.DossierRcs, ACTION_RCS -> rec.ActionRegistre, CLIENT -> rec.Client, CATEGORIE -> rec.TypeDemande... :
/// Serveur/Recopie/FichesV3.cs) est lue SOUS SES NOMS V2, par la correspondance colonne par colonne que la recopie tient deja
/// (RegleRecopie.Correspondance, la meme lecture « AS » ses noms V2 que FichesRecopiees.cs) : la requete de RIG v2 reste telle
/// quelle, seule sa table devient « (SELECT x.[NumeroGestion] AS [DSSRC_NUM_GESTION], ... FROM rec.DossierRcs AS x) ». La cle
/// V2 n'y est que si la fiche garde l'identifiant de la V2 (copiee par le transfert : CATEGORIE, UTILISATEUR ont celui de la V3).
/// ENCAISS (TraiterEncaiss, :365-407) : chaque ligne de la requete est un client ; un client « C » (compte) compte son
/// emolument, un client divers le reste de sa ventilation sur l'affaire apres ses debours, plafonne a l'emolument (:380-401).
/// GROUPE : « Feuille RCS » (1), « Feuille judiciaire » (2) ; une autre feuille n'est pas generee (garde :273). ORDRE : feuille,
/// ligne, colonne (RIG v2 n'en a pas : PilotageRequetes.cs:33 sans ORDER BY). ETAT : « Calculé » (Validee) ou « Non calculé »
/// (Rejetee), la cause dans la colonne Calcul.
///
/// CE QUE LA V3 NE CALCULE PAS, nomme dans la colonne Calcul de la ligne : une requete qui lit une table absente de la base V3,
/// ou une colonne d'une fiche que la V3 n'a pas reprise (propre a la V2, ou traduite d'une base a l'autre : l'agent d'un client)
/// -- nommees ; une requete qui n'est pas un SELECT seul (« declare ... ») -- LectureV2 la refuse. fix-ok: mesure au controle
/// final du lot 9 (27/11/2026) -- 56 cellules RCS sur 91 restaient « non calcule » parce que la V3 refusait toute table reprise
/// en fiche, alors que FichesV3.cs donne la correspondance de chacune de leurs colonnes ; ENCAISS (7 cellules) n'etait pas
/// repris. « Visualiser » (EcrivainPilotage.cs, lot 1 du plan-f4fb097f) ecrit la Demande XPILOTAGE du travail de fond (:108-121) et
/// place le classeur dans la file des editions ; le classeur lui-meme (le modele XPILOTAGE.XLT, ses feuilles) et ARCHIVES ne sont pas
/// produits.
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

    /// <summary>La requete d'un client divers dans ENCAISS (TraiterEncaiss, OPE_GENERER.cs:385-387), ses identifiants en
    /// parametres (RIG v2 les concatene).</summary>
    private const string RequeteEncaiss =
        "select (select sum(venti_montant) from ventilation where venti_id_clien = @client and venti_id_affre = @affaire) as MontantVentil, " +
        "sum(artut_montant_inpi + artut_montant_bodacc + artut_montant_autre) as MontantDebours from article_utilise " +
        "where artut_id_clien = @client and artut_id_affre = @affaire";

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
            var cause = Cause(c, tables, out var sql, out var sqlEncaiss);
            if (cause is not null)
            {
                lignes.Add(new LigneLue(nom, $"non calculé : {cause}", groupe, "Rejetee", [])
                {
                    Id = EditionsV2.IdSurPeriode(c.Id.ToString(CultureInfo.InvariantCulture), debut, fin),
                    Valeurs = [nom, place, "", "", $"non calculé : {cause}"],
                });
                continue;
            }
            var (valeur, erreur, lue) = await ValeurAsync(baseGreffe, sql!, sqlEncaiss, debut, finJour, ct);
            // L'annee precedente : sauf « PASANNEEPREC » quand une ligne a ete lue (:297-300, :310).
            string n1;
            if (c.Traitement == "PASANNEEPREC" && lue) n1 = "sans objet";
            else
            {
                var (v1, e1, _) = await ValeurAsync(baseGreffe, sql!, sqlEncaiss, debutN1, finN1, ct);
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
                // La cellule (PILOT_ID_PILOT) et la periode calculee : ce que « Visualiser » edite (EcrivainPilotage.cs).
                Id = EditionsV2.IdSurPeriode(c.Id.ToString(CultureInfo.InvariantCulture), debut, fin),
                Valeurs = [nom, place, erreur is null ? valeur : "erreur", n1, calcul],
            });
        }
        return new LignesLues(lignes, cellules.Count);
    }

    /// <summary>
    /// Pourquoi la requete ne se calcule pas dans la base V3 (null : elle se calcule), et sinon la requete traduite : une table
    /// absente, une colonne de fiche non reprise, une requete qui n'est pas un SELECT seul ; pour ENCAISS, la requete d'un client
    /// divers aussi.
    /// </summary>
    private static string? Cause(Cellule c, HashSet<string> tablesV2, out string? sql, out string? sqlEncaiss)
    {
        sql = sqlEncaiss = null;
        if (Traduire(c.Sql, tablesV2, out var traduite) is { } cause) return cause;
        try { LectureV2.ExigerLecture(traduite); }
        catch (InvalidOperationException) { return "la requête de RIG v2 n'est pas un SELECT seul"; }
        if (c.Traitement == "ENCAISS")
        {
            if (Traduire(RequeteEncaiss, tablesV2, out var encaiss) is { } causeEncaiss)
                return $"calcul d'encaissement (TraiterEncaiss) : {causeEncaiss}";
            sqlEncaiss = encaiss;
        }
        sql = traduite;
        return null;
    }

    /// <summary>
    /// La requete de RIG v2 sur la base V3 : chaque table de FROM / JOIN lue dans v2.*, ou sous ses noms V2 si elle est reprise
    /// en fiche (VueV2) ; la cause (null : tout se lit) nomme les tables absentes et les colonnes de fiche non reprises.
    /// </summary>
    internal static string? Traduire(string requete, HashSet<string> tablesV2, out string traduite)
    {
        var absentes = new List<string>();
        var fiches = new Dictionary<string, FicheV3>(StringComparer.OrdinalIgnoreCase);
        traduite = TableDeLaRequete().Replace(requete, m =>
        {
            var (mot, table, indication) = (m.Groups["mot"].Value, m.Groups["table"].Value, m.Groups["indication"].Value);
            if (tablesV2.Contains(table)) return $"{mot} v2.{table}{indication}";
            if (FicheDe(table) is { } f)
            {
                fiches[table] = f;
                // Une table derivee veut un alias : celui de la requete s'il suit, sinon le nom de la table (les colonnes
                // qualifiees « DOSSIER_RCS.DSSRC_... » restent justes). L'indication (NOLOCK) passe dedans : une table derivee
                // n'en prend pas.
                var suite = AliasQuiSuit().Match(requete, m.Index + m.Length);
                var alias = suite.Success && !MotsApresTable.Contains(suite.Groups["alias"].Value);
                return $"{mot} {VueV2(f, indication)}{(alias ? "" : $" AS {table}")}";
            }
            absentes.Add(table.ToUpperInvariant());
            return m.Value;
        });
        var causes = absentes.Distinct().Select(t => $"{t} (absente de la base V3)").ToList();
        foreach (var (_, f) in fiches)
        {
            var lues = ColonnesV2(f);
            var premiere = f.Recopie.Correspondance![0].V2;
            var prefixe = premiere[..(premiere.IndexOf('_') + 1)];
            var manquantes = Regex.Matches(requete, $@"\b{Regex.Escape(prefixe)}\w+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Select(x => x.Value.ToUpperInvariant()).Distinct().Where(col => !lues.ContainsKey(col)).ToList();
            if (manquantes.Count > 0)
                causes.Add($"{string.Join(", ", manquantes)} de {f.Recopie.TableV2} (colonne non reprise dans {f.Recopie.TableV3})");
        }
        return causes.Count == 0 ? null : $"la requête de RIG v2 lit {string.Join(", ", causes)}";
    }

    private static FicheV3? FicheDe(string tableV2)
        => ReglesFichesV3.Fiches.FirstOrDefault(f => f.Recopie.Correspondance is not null
                                                    && string.Equals(f.Recopie.TableV2, tableV2, StringComparison.OrdinalIgnoreCase));

    /// <summary>Les colonnes de la fiche lues sous leur nom V2 (nom V2 -> nom V3) : la correspondance sans les colonnes
    /// traduites (un agent, un service : leur valeur n'est pas celle de la V2), et la cle quand la fiche garde celle de la V2 --
    /// copiee par le transfert, dont la regle la nomme en dernier (FicheV3.Transfert).</summary>
    private static Dictionary<string, string> ColonnesV2(FicheV3 f)
    {
        var colonnes = f.Recopie.Correspondance!.Where(c => c.Traduction is null)
            .ToDictionary(c => c.V2.ToUpperInvariant(), c => c.V3, StringComparer.OrdinalIgnoreCase);
        if (f.CopieeParLeTransfert) colonnes[f.Transfert.Colonnes[^1].ToUpperInvariant()] = f.Recopie.CleV3 ?? "Id";
        return colonnes;
    }

    /// <summary>La fiche rec.* lue sous ses noms V2 : « (SELECT x.[NumeroGestion] AS [DSSRC_NUM_GESTION], ... FROM rec.DossierRcs AS x) ».</summary>
    private static string VueV2(FicheV3 f, string indication)
        => $"(SELECT {string.Join(", ", ColonnesV2(f).Select(c => $"x.[{c.Value}] AS [{c.Key}]"))} FROM {f.Recopie.TableV3} AS x{indication})";

    private static readonly HashSet<string> MotsApresTable = new(StringComparer.OrdinalIgnoreCase)
    {
        "WHERE", "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER", "JOIN", "ON", "GROUP", "ORDER", "HAVING", "UNION", "EXCEPT",
        "INTERSECT", "WITH", "OPTION",
    };

    /// <summary>La valeur de la cellule : la colonne NB (NbAsync), ou le calcul ENCAISS si sa requete de client divers est donnee.</summary>
    private static Task<(string Valeur, string? Erreur, bool Lue)> ValeurAsync(SqlConnection baseGreffe, string sql, string? sqlEncaiss,
                                                                             DateTime debut, DateTime fin, CancellationToken ct)
        => sqlEncaiss is null ? NbAsync(baseGreffe, sql, debut, fin, ct) : EncaissAsync(baseGreffe, sql, sqlEncaiss, debut, fin, ct);

    /// <summary>
    /// ENCAISS (TraiterEncaiss, OPE_GENERER.cs:365-407), sur toutes les lignes de la requete : un client « C » ajoute son
    /// emolument (:381-382) ; un client divers, le reste de sa ventilation sur l'affaire apres ses debours, s'il est positif et
    /// au plus l'emolument (:384-400). Aucune ligne : la cellule reste vide (:289, rien n'est ecrit).
    /// </summary>
    private static async Task<(string Valeur, string? Erreur, bool Lue)> EncaissAsync(SqlConnection baseGreffe, string sql, string sqlEncaiss,
                                                                                    DateTime debut, DateTime fin, CancellationToken ct)
    {
        try
        {
            var clients = new List<(string Type, double Emolument, object Client, object Affaire)>();
            await using (var r = await LectureV2.LireAsync(baseGreffe, sql, ct,
                             new SqlParameter("@DateMin", System.Data.SqlDbType.DateTime) { Value = debut },
                             new SqlParameter("@DateMax", System.Data.SqlDbType.DateTime) { Value = fin }))
            {
                // Les colonnes que TraiterEncaiss lit (:381-387) : sans l'une d'elles, RIG v2 tombe en erreur (:306-308).
                var noms = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (ColonnesEncaiss.FirstOrDefault(c => !noms.Contains(c)) is { } absente) return ("", $"pas de colonne {absente}", false);
                while (await r.ReadAsync(ct))
                    clients.Add((FormeLigne.Texte(r, "CLIEN_TYPE"), Flottant(r["MontantEmol"]), r["IdClient"], r["IdAffre"]));
            }
            if (clients.Count == 0) return ("", null, false);
            double resultat = 0;
            foreach (var (type, emolument, client, affaire) in clients)
            {
                if (type == "C") { resultat += emolument; continue; }
                await using var r = await LectureV2.LireAsync(baseGreffe, sqlEncaiss, ct, new SqlParameter("@client", client), new SqlParameter("@affaire", affaire));
                if (!await r.ReadAsync(ct)) continue;
                var reste = Flottant(r["MontantVentil"]) - Flottant(r["MontantDebours"]);
                if (reste > 0) resultat += Math.Min(reste, emolument);
            }
            return (Nombre(resultat), null, true);
        }
        catch (SqlException e)
        {
            return ("", e.Message, false);
        }
    }

    private static readonly string[] ColonnesEncaiss = ["CLIEN_TYPE", "MontantEmol", "IdClient", "IdAffre"];

    private static double Flottant(object v) => v is DBNull or null ? 0 : Convert.ToDouble(v, CultureInfo.InvariantCulture);

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

    // Une table d'une clause FROM ou JOIN, ecrite sans schema ou en dbo (« FROM DOSSIER_RCS », « JOIN [dbo].[INSTANCE] »), et
    // son indication de verrou (« ARTICLE_UTILISE WITH (nolock) », les requetes ENCAISS).
    [GeneratedRegex(@"\b(?<mot>FROM|JOIN)\s+(?:\[?dbo\]?\.)?\[?(?<table>[A-Za-z_][A-Za-z0-9_]*)\]?(?![\w.(])(?<indication>\s+WITH\s*\(\s*NOLOCK\s*\))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TableDeLaRequete();

    // Ce qui suit une table : son alias (« DOSSIER_RCS d », « CLIENT AS c ») ou un mot de la requete (WHERE, INNER...).
    [GeneratedRegex(@"\G\s+(?:AS\s+)?(?<alias>[A-Za-z_]\w*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AliasQuiSuit();
}
