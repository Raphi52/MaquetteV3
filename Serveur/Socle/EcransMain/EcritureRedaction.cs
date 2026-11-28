using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Rédaction d'une décision » (PROC_REDA) : le geste VALIDER LA DECISION -- ses listes, ses dates et ses deux cases --, ecrit dans la
/// base du greffe. Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 8, 28/11/2026. Route : ModuleRedaction.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RIG\DLL\Processus\PROC_REDA\ETP_REDA\OPE_DECISION_JUDICIAIRE.cs) :
///   - les champs lies a DECISION_JUDICIAIRE (:97-106) : ressort, avant dire droit, contradictoire, execution provisoire (listes
///     OBLIGATOIRES, Designer :194-225), sens general (liste facultative, Designer :271), date prevue, date, decision publique et debats
///     publics (cases, Designer :280-288) ; chaque liste prend ses valeurs dans sa table (ULT_COMBO_*, Rig_Ult_Metier\COMBO\JUDICIAIRE) ;
///   - Valider (Toolbar_ButtonClick, :147-170) : CallVerifier, puis DecisionJudiciaire.Sauve dans la transaction « SaveREDA_DJ ».
///   - la decision est celle d'une instance que la redaction ouvre (series JRCT, JRPC, JRREF : LectureRedaction.Series).
/// NON REPRIS, nomme : le type de decision ne change pas par ce geste (ses controles de mesure liee et de numero de minute, DoVerifier
/// :290-323, restent a RIG v2) ; la date de delivrance des grosses du document de redaction (:166-172), la demande de l'etape (:175-176),
/// les avertissements de numero de minute (VerifierNumMinute, :325-345 : de simples « attention ») et la creation d'une decision.
/// </summary>
internal static class EcritureRedaction
{
    /// <summary>Le nom de l'ecran au journal des gestes.</summary>
    public const string Ecran = "Rédaction d'une décision";

    /// <summary>Une liste de la decision : son champ, sa colonne de DECISION_JUDICIAIRE, sa table et sa colonne de valeur, obligatoire ou non.</summary>
    private sealed record Liste(string Champ, string Colonne, string Table, string Valeur, bool Obligatoire);

    private static readonly Liste[] Listes =
    [
        new(ValidationDecision.SensGeneral, "DCJDC_SENS_GENERAL", "v2.SENS_GENERAL", "R_SENSGENERA_VALEUR", false),
        new(ValidationDecision.Ressort, "DCJDC_RESSORT", "v2.RESSORT", "R_RESSORT_VALEUR", true),
        new(ValidationDecision.AvantDireDroit, "DCJDC_AVANT_DIRE_DROIT", "v2.AVANT_DIRE_DROIT", "R_AVANTDIRED_VALEUR", true),
        new(ValidationDecision.Contradictoire, "DCJDC_CONTRADICTOIRE", "v2.CONTRADICTOIRE", "R_CONTRADICT_VALEUR", true),
        new(ValidationDecision.ExecutionProvisoire, "DCJDC_EXECUTION_PROVISOIRE", "v2.EXECUTION_PROVISOIRE", "R_EXECUTIONP_VALEUR", true),
    ];

    /// <summary>Les gestes ecrits : tout autre type est refuse par la route (GesteFaitMain, rien d'ecrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Enregistrer];

    /// <summary>Les valeurs des cinq listes de la fiche, lues dans leurs tables (ULT_COMBO_*, Outils.makeSqlStandard : VALEUR et
    /// LIBELLE_ECRAN), LECTURE SEULE ; par valeur (RIG v2 n'en donne pas l'ordre). Le libelle est la colonne _LIBELLE_ECRAN de la table.</summary>
    public static async Task<ListeDecisionLue[]> ListesAsync(SqlConnection baseGreffe, CancellationToken ct)
    {
        var listes = new List<ListeDecisionLue>();
        foreach (var l in Listes)
        {
            var libelle = l.Valeur.Replace("_VALEUR", "_LIBELLE_ECRAN", StringComparison.Ordinal);
            var valeurs = new List<ValeurListeLue>();
            await using var r = await LectureV2.LireAsync(baseGreffe, $"SELECT {l.Valeur} AS Valeur, {libelle} AS Libelle FROM {l.Table} ORDER BY {l.Valeur}", ct);
            while (await r.ReadAsync(ct)) valeurs.Add(new ValeurListeLue(FormeLigne.Texte(r, "Valeur").Trim(), FormeLigne.Texte(r, "Libelle").Trim()));
            listes.Add(new ListeDecisionLue(l.Champ, l.Obligatoire, [.. valeurs]));
        }
        return [.. listes];
    }

    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids is not [var texte] || !int.TryParse(texte, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser("Choisissez une décision de la liste.");
        var avant = new Dictionary<string, string>(StringComparer.Ordinal);
        DateTime? date, datePrevue;
        bool debats, publique;
        string role;
        await using (var r = await geste.LireAsync($"""
            SELECT d.DCJDC_SENS_GENERAL, d.DCJDC_RESSORT, d.DCJDC_AVANT_DIRE_DROIT, d.DCJDC_CONTRADICTOIRE, d.DCJDC_EXECUTION_PROVISOIRE,
                   d.DCJDC_DATE, d.DCJDC_DATE_PREVUE, CONVERT(bit, ISNULL(d.DCJDC_DEBAT_PUBLIC, 0)) AS Debats,
                   CONVERT(bit, ISNULL(d.DCJDC_DECISION_PUBLIQUE, 0)) AS Publique, i.INSTN_NUM_ROLE
            FROM v2.DECISION_JUDICIAIRE AS d
            JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
            JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
            WHERE d.DCJDC_ID_DCJDC = @id AND ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {LectureRedaction.Series}
            """, ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser("Cette décision n'est plus dans la base du greffe : relisez la liste.");
            foreach (var l in Listes) avant[l.Champ] = FormeLigne.Texte(r, l.Colonne).Trim();
            date = r["DCJDC_DATE"] as DateTime?;
            datePrevue = r["DCJDC_DATE_PREVUE"] as DateTime?;
            debats = r["Debats"] is true;
            publique = r["Publique"] is true;
            role = FormeLigne.Texte(r, "INSTN_NUM_ROLE").Trim();
        }

        // Les valeurs apres le geste : celles envoyees, sinon celles de la base (un champ absent ne change pas).
        string Champ(string libelle, string base_) => (geste.Valeur(ValidationDecision.Section, libelle) ?? base_).Trim();
        var apres = Listes.ToDictionary(l => l.Champ, l => Champ(l.Champ, avant[l.Champ]), StringComparer.Ordinal);
        foreach (var l in Listes)
        {
            var v = apres[l.Champ];
            if (v.Length == 0)
            {
                if (l.Obligatoire) return IssueGeste.Refuser(Obligatoire(l.Champ));
                continue;
            }
            await using var r = await geste.LireAsync($"SELECT COUNT(*) FROM {l.Table} WHERE {l.Valeur} = @v", ct, new SqlParameter("@v", v));
            await r.ReadAsync(ct);
            if (r.GetInt32(0) == 0) return IssueGeste.Refuser($"« {v} » n'est pas une valeur de la liste « {l.Champ} ».");
        }
        if (!LireDate(geste.Valeur(ValidationDecision.Section, ValidationDecision.Date), date, out var dateApres))
            return IssueGeste.Refuser($"« {ValidationDecision.Date} » n'est pas une date (aaaa-mm-jj).");
        if (!LireDate(geste.Valeur(ValidationDecision.Section, ValidationDecision.DatePrevue), datePrevue, out var prevueApres))
            return IssueGeste.Refuser($"« {ValidationDecision.DatePrevue} » n'est pas une date (aaaa-mm-jj).");
        var debatsApres = LireCase(geste.Valeur(ValidationDecision.Section, ValidationDecision.DebatsPublics), debats);
        var publiqueApres = LireCase(geste.Valeur(ValidationDecision.Section, ValidationDecision.DecisionPublique), publique);

        var parametres = new List<SqlParameter> { new("@id", id), new("@date", (object?)dateApres ?? DBNull.Value),
            new("@prevue", (object?)prevueApres ?? DBNull.Value), new("@debats", debatsApres), new("@publique", publiqueApres) };
        var champs = new List<string> { "DCJDC_DATE = @date", "DCJDC_DATE_PREVUE = @prevue", "DCJDC_DEBAT_PUBLIC = @debats", "DCJDC_DECISION_PUBLIQUE = @publique" };
        for (var k = 0; k < Listes.Length; k++)
        {
            champs.Add($"{Listes[k].Colonne} = @l{k}");
            parametres.Add(new SqlParameter($"@l{k}", apres[Listes[k].Champ] is { Length: > 0 } v ? v : DBNull.Value));
        }
        await geste.EcrireAsync($"UPDATE v2.DECISION_JUDICIAIRE SET {string.Join(", ", champs)} WHERE DCJDC_ID_DCJDC = @id", ct, [.. parametres]);

        static string? Jour(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return IssueGeste.Ecrit("Décision judiciaire enregistrée.", [id.ToString(CultureInfo.InvariantCulture)],
            new TraceGeste("affaire", $"la décision n° {id} de l'instance {role}", id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { Listes = avant, Date = Jour(date), DatePrevue = Jour(datePrevue), DebatsPublics = debats, DecisionPublique = publique },
                new { Listes = apres, Date = Jour(dateApres), DatePrevue = Jour(prevueApres), DebatsPublics = debatsApres, DecisionPublique = publiqueApres }));
    }

    /// <summary>Une date envoyee (aaaa-mm-jj ou jj/mm/aaaa ; vide = aucune), ou celle de la base si le champ est absent.</summary>
    private static bool LireDate(string? envoye, DateTime? base_, out DateTime? lue)
    {
        lue = base_;
        if (envoye is null) return true;
        if (envoye.Trim().Length == 0) { lue = null; return true; }
        if (DateTime.TryParseExact(envoye.Trim(), ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            lue = d.Date;
            return true;
        }
        return false;
    }

    /// <summary>Une case envoyee (« 1 », « true », « oui » = cochee), ou celle de la base si le champ est absent.</summary>
    private static bool LireCase(string? envoye, bool base_)
        => envoye is null ? base_ : envoye.Trim().ToLowerInvariant() is "1" or "true" or "oui";
}
