using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Chaines;

/// <summary>
/// Les lectures de la consultation des chaines (SOC-018 CA07) : l'etat des 7 chaines, le journal des evenements filtre
/// par type et par date, le catalogue des codes, les grands totaux. Rien n'y ecrit (la V2 : PROC_CERTIF, onglets
/// « Journal non purgeable », « Journal purgeable », « Grands totaux Factures », « Grands totaux Encaissements »).
/// </summary>
public static class LectureChaines
{
    /// <summary>Au plus autant d'evenements par lecture du journal : un exercice entier se lit par periodes.</summary>
    public const int MaxEvenements = 500;

    public static async Task<EtatCertification> EtatAsync(SqlConnection cn, string greffe, CancellationToken ct = default)
    {
        bool ouverte = false;
        DateTime? ouverteLe = null;
        string? fuseau = null;
        await using (var cmd = new SqlCommand("SELECT OuverteLe, Fuseau FROM soc.Certification WHERE Id = 1;", cn))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
            if (await l.ReadAsync(ct)) { ouverte = true; ouverteLe = DateTime.SpecifyKind(l.GetDateTime(0), DateTimeKind.Utc); fuseau = l.GetString(1); }

        var chaines = new List<EtatChaine>();
        await using (var cmd = new SqlCommand("""
            WITH m (Chaine, Maillons, Dernier) AS (
                SELECT 'FAC', COUNT(*), MAX(SigneeLe) FROM soc.FactureSignee
                UNION ALL SELECT 'ENC', COUNT(*), MAX(SigneLe) FROM soc.EncaissementSigne
                UNION ALL SELECT 'GTF', COUNT(*), MAX(CalculeLe) FROM soc.GrandTotalFactures
                UNION ALL SELECT 'GTE', COUNT(*), MAX(CalculeLe) FROM soc.GrandTotalEncaissements
                UNION ALL SELECT 'DUP', COUNT(*), MAX(SigneLe) FROM soc.DuplicataSigne
                UNION ALL SELECT 'JNP', COUNT(*), MAX(SigneLe) FROM soc.EvenementTechnique WHERE Chaine = 'JNP'
                UNION ALL SELECT 'JP',  COUNT(*), MAX(SigneLe) FROM soc.EvenementTechnique WHERE Chaine = 'JP')
            -- Le journal purgeable repart a chaque exercice : son dernier rang est celui de l'exercice le plus recent.
            SELECT m.Chaine, m.Maillons,
                   ISNULL((SELECT TOP (1) e.DerniereSeq FROM soc.EtatChaine e WHERE e.Chaine = m.Chaine ORDER BY e.Exercice DESC), 0), m.Dernier
            FROM m;
            """, cn))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
        {
            var lus = new Dictionary<string, EtatChaine>();
            while (await l.ReadAsync(ct))
            {
                var code = l.GetString(0);
                lus[code] = new EtatChaine(code, ChainesSignees.Libelle(code), l.GetInt32(1), l.GetInt64(2),
                    l.IsDBNull(3) ? null : DateTime.SpecifyKind(l.GetDateTime(3), DateTimeKind.Utc));
            }
            chaines.AddRange(ChainesSignees.Toutes.Select(c => lus[c.Code]));
        }
        var enAttente = await ServiceChaines.ScalaireAsync<int>(cn, null, "SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Seq IS NULL;", ct);
        var cle = await ServiceChaines.ScalaireOuNulAsync<string>(cn, null, "SELECT TOP (1) Empreinte FROM soc.CleSignature ORDER BY Id DESC;", ct);
        var archives = new List<int>();
        await using (var cmd = new SqlCommand("SELECT Exercice FROM soc.ArchiveFiscale ORDER BY Exercice;", cn))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
            while (await l.ReadAsync(ct)) archives.Add(l.GetInt16(0));
        // Active des l'ouverture, sans reglage (CA06) : elle ne depend de rien d'autre que l'ouverture.
        return new EtatCertification(greffe, ouverte, ouverte, ouverteLe, fuseau, cle?.Trim(), enAttente, [.. chaines], [.. archives]);
    }

    public static async Task<CodeEvenement[]> CodesAsync(SqlConnection cn, CancellationToken ct = default)
    {
        var codes = new List<CodeEvenement>();
        await using var cmd = new SqlCommand("SELECT Code, Libelle, Purgeable, Exigibilite FROM soc.CodeEvenement ORDER BY Code;", cn);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct)) codes.Add(new CodeEvenement(l.GetInt16(0), l.GetString(1), l.GetBoolean(2), l.GetString(3)));
        return [.. codes];
    }

    /// <summary>
    /// Le journal, du plus recent au plus ancien. <paramref name="partie"/> : « np » (non purgeable), « p » (purgeable) ou
    /// null (les deux) ; un evenement pas encore signe compte dans la partie de son code. <paramref name="du"/> et
    /// <paramref name="au"/> sont des jours du greffe (bornes comprises).
    /// </summary>
    public static async Task<EvenementSigne[]> JournalAsync(SqlConnection cn, string? partie, short? code, DateOnly? du, DateOnly? au,
                                                           CancellationToken ct = default)
    {
        var fuseau = await ServiceChaines.ScalaireOuNulAsync<string>(cn, null, "SELECT Fuseau FROM soc.Certification WHERE Id = 1;", ct) ?? Fuseaux.Metropole;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(fuseau);
        DateTime? Utc(DateOnly? jour) => jour is { } j ? TimeZoneInfo.ConvertTimeToUtc(j.ToDateTime(TimeOnly.MinValue), tz) : null;
        bool? purgeable = partie?.ToLowerInvariant() switch { "np" => false, "p" => true, _ => null };

        var evenements = new List<EvenementSigne>();
        await using var cmd = new SqlCommand($"""
            SELECT TOP ({MaxEvenements}) e.Id, e.Instant, e.Code, e.Libelle, e.Detail, e.Auteur, c.Purgeable, e.Chaine, e.Exercice, e.Seq
            FROM soc.EvenementTechnique e
            JOIN soc.CodeEvenement c ON c.Code = e.Code
            WHERE (@purgeable IS NULL OR c.Purgeable = @purgeable)
              AND (@code IS NULL OR e.Code = @code)
              AND (@du IS NULL OR e.Instant >= @du)
              AND (@au IS NULL OR e.Instant < @au)
            ORDER BY e.Instant DESC, e.Id DESC;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@purgeable", SqlDbType.Bit) { Value = (object?)purgeable ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@code", SqlDbType.SmallInt) { Value = (object?)code ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@du", SqlDbType.DateTime2) { Value = (object?)Utc(du) ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@au", SqlDbType.DateTime2) { Value = (object?)Utc(au?.AddDays(1)) ?? DBNull.Value });
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct))
            evenements.Add(new EvenementSigne(l.GetInt64(0), DateTime.SpecifyKind(l.GetDateTime(1), DateTimeKind.Utc), l.GetInt16(2),
                l.GetString(3), l.GetString(4), l.GetString(5), l.GetBoolean(6), l.IsDBNull(7) ? null : l.GetString(7),
                l.IsDBNull(8) ? null : l.GetInt16(8), l.IsDBNull(9) ? null : l.GetInt64(9)));
        return [.. evenements];
    }

    /// <summary>Les grands totaux, en lecture seule, du plus recent au plus ancien ; <paramref name="chaine"/> GTF, GTE ou null (les deux).</summary>
    public static async Task<GrandTotal[]> TotauxAsync(SqlConnection cn, string? chaine, CancellationToken ct = default)
    {
        var tva = new Dictionary<long, List<TotalParTaux>>();
        await using (var cmd = new SqlCommand("SELECT IdTotal, Taux, MontantTtc FROM soc.GrandTotalFacturesTva ORDER BY IdTotal, Taux DESC;", cn))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
            while (await l.ReadAsync(ct))
            {
                if (!tva.TryGetValue(l.GetInt64(0), out var liste)) tva[l.GetInt64(0)] = liste = [];
                liste.Add(new TotalParTaux(l.GetDecimal(1), l.GetDecimal(2)));
            }
        var totaux = new List<GrandTotal>();
        await using (var cmd = new SqlCommand("""
            SELECT 'GTF', Id, Type, Periode, Total, CumulReel, CumulAbsolu, NbPieces, CalculeLe, Information, Seq FROM soc.GrandTotalFactures
            WHERE @chaine IS NULL OR @chaine = 'GTF'
            UNION ALL
            SELECT 'GTE', Id, Type, Periode, Total, CumulReel, CumulAbsolu, NbPieces, CalculeLe, Information, Seq FROM soc.GrandTotalEncaissements
            WHERE @chaine IS NULL OR @chaine = 'GTE'
            ORDER BY 1 DESC, 11 DESC;
            """, cn))
        {
            cmd.Parameters.Add(ServiceChaines.P("@chaine", SqlDbType.VarChar, 3, string.IsNullOrWhiteSpace(chaine) ? null : chaine.Trim().ToUpperInvariant()));
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct))
            {
                var code = l.GetString(0);
                var id = l.GetInt64(1);
                totaux.Add(new GrandTotal(code, l.GetString(2), l.GetString(3), l.GetDecimal(4), l.GetDecimal(5), l.GetDecimal(6), l.GetInt32(7),
                    DateTime.SpecifyKind(l.GetDateTime(8), DateTimeKind.Utc), l.IsDBNull(9) ? null : l.GetString(9), l.GetInt64(10),
                    code == ChainesSignees.TotauxFactures && tva.TryGetValue(id, out var t) ? [.. t] : []));
            }
        }
        return [.. totaux];
    }
}
