using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Recopie;

/// <summary>L'etat du dernier transfert d'une base au matin (REC-005 CA04).</summary>
/// <param name="Etat">« termine », « interrompu » (incident, ou arret brutal : en cours sans personne derriere),
/// « arrete » (un ecart), « en cours » (il tourne), « refuse » ou « aucun transfert ».</param>
/// <param name="PointDeReprise">Interrompu : la table et le lot d'ou il repartira.</param>
public sealed record EtatTransfertBase(string BaseV3, string? BaseV2, int? IdTransfert, string Etat, string? Motif, string? PointDeReprise,
                                       int Reprises, long? DureeMs, DateTime? Debut, DateTime? Fin, int? Provisoires);

/// <summary>
/// LE RAPPORT DU MATIN DU TRANSFERT (REC-005 CA04) : pour chaque base V3, le dernier transfert, « termine » ou
/// « interrompu » avec son point de reprise. Lecture seule des bases V3 ; aucune base V2 n'est ouverte.
/// </summary>
public static class RapportDuMatin
{
    public static async Task<EtatTransfertBase> LireAsync(string connexionV3, CancellationToken ct = default)
    {
        var baseV3 = new SqlConnectionStringBuilder(connexionV3).InitialCatalog;
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT TOP (1) Id, BaseV2, Etat, Motif, PointDeReprise, Reprises, DureeMs, Debut, Fin, Provisoires,
                   APPLOCK_TEST(N'public', N'rigv3-transfert:' + BaseV2, N'Exclusive', N'Session')
            FROM rec.Transfert ORDER BY CASE WHEN Etat = 'refuse' THEN 1 ELSE 0 END, Id DESC;
            """, cn);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        if (!await l.ReadAsync(ct)) return new EtatTransfertBase(baseV3, null, null, "aucun transfert", null, null, 0, null, null, null, null);
        var id = l.GetInt32(0);
        var etat = l.GetString(2) switch
        {
            "fini" => "termine",
            // Verrou libre : plus personne ne transfere cette base, le processus est mort en route.
            "en cours" when l.GetInt32(10) == 1 => "interrompu",
            var e => e,
        };
        var r = new EtatTransfertBase(baseV3, l.GetString(1), id, etat, l.IsDBNull(3) ? null : l.GetString(3), l.IsDBNull(4) ? null : l.GetString(4),
                                      l.GetInt32(5), l.IsDBNull(6) ? null : l.GetInt64(6), l.GetDateTime(7), l.IsDBNull(8) ? null : l.GetDateTime(8),
                                      l.IsDBNull(9) ? null : l.GetInt32(9));
        await l.CloseAsync();
        if (etat == "interrompu" && (r.PointDeReprise is null || r.Motif is null))
            r = r with
            {
                Motif = r.Motif ?? "arret brutal : le processus du transfert a disparu sans fermer son journal",
                PointDeReprise = r.PointDeReprise ?? await JournalTransfert.PointDeRepriseAsync(cn, id, ct) ?? "aucune ligne ecrite",
            };
        return r;
    }

    public static string Texte(IEnumerable<EtatTransfertBase> bases)
    {
        var s = new System.Text.StringBuilder();
        var liste = bases.ToList();
        s.AppendLine($"Rapport du matin du transfert : {liste.Count(b => b.Etat == "termine")} base(s) terminee(s) sur {liste.Count}");
        foreach (var b in liste)
        {
            s.Append($"  {b.BaseV2 ?? "?"} -> {b.BaseV3} : {b.Etat.ToUpperInvariant()}");
            if (b.IdTransfert is { } id) s.Append($" (transfert {id}{(b.Reprises > 0 ? $", {b.Reprises} reprise(s)" : "")}" +
                                                  $"{(b.DureeMs is { } ms ? $", {ms / 1000.0:0.0} s" : "")}{(b.Provisoires > 0 ? $", {b.Provisoires} regles PROVISOIRES" : "")})");
            s.AppendLine();
            if (b.Etat == "interrompu") s.AppendLine($"      point de reprise : {b.PointDeReprise} — relancer le meme transfert reprend de la");
            if (b.Etat is "interrompu" or "arrete" or "refuse" && b.Motif is not null) s.AppendLine($"      motif : {b.Motif}");
        }
        return s.ToString();
    }
}
