using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Base;
using RigV3.Serveur.Recopie;

namespace RigV3.Comparaison;

/// <summary>La ligne de commande de l'outil (en tete de Program.cs), appelable par le banc sans processus a part.</summary>
public static class LigneDeCommande
{
    public static async Task<int> ExecuterAsync(string[] args, TextWriter sortie, CancellationToken ct = default)
    {
        var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var liste = args.Skip(1).ToList();
        for (var i = 0; i < liste.Count; i++)
        {
            if (!liste[i].StartsWith("--", StringComparison.Ordinal)) { sortie.WriteLine($"option inattendue : {liste[i]}"); return 3; }
            o[liste[i][2..]] = i + 1 < liste.Count && !liste[i + 1].StartsWith("--", StringComparison.Ordinal) ? liste[++i] : "";
        }
        if (args.FirstOrDefault() != "comparer" || !o.TryGetValue("v2", out var v2) || !o.TryGetValue("v3", out var v3))
        {
            sortie.WriteLine("Usage : RigV3.Comparaison comparer --v2 <chaine> --v3 <chaine> [--tables A,B | --toutes] [--detail 1000] [--conservation 90] [--exploitation]");
            return 3;
        }
        if (!o.ContainsKey("exploitation") && !(BaseJetable.EstServeurLocal(v2) && BaseJetable.EstServeurLocal(v3)))
        {
            sortie.WriteLine($"Refuse : {new SqlConnectionStringBuilder(v2).DataSource} / {new SqlConnectionStringBuilder(v3).DataSource} n'est pas le " +
                             "serveur de ce poste. Le controle d'une base de greffe se lance chez Amitel, avec --exploitation.");
            return 3;
        }
        IReadOnlyList<string>? tables = null;
        if (o.TryGetValue("tables", out var t)) tables = t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        else if (o.ContainsKey("toutes")) tables = await TablesTransfereesAsync(v2, v3, ct);
        var rapport = await ComparaisonBases.ComparerAsync(new OptionsComparaison(v2, v3)
        {
            Tables = tables,
            DetailMax = o.TryGetValue("detail", out var d) ? int.Parse(d, CultureInfo.InvariantCulture) : 1000,
            ConservationJours = o.TryGetValue("conservation", out var c) ? int.Parse(c, CultureInfo.InvariantCulture) : 90,
            Suivre = m => sortie.WriteLine($"{DateTime.Now:HH:mm:ss} {m}"),
        }, ct);
        sortie.WriteLine();
        sortie.WriteLine(ComparaisonBases.Texte(rapport));
        return rapport.CodeSortie;
    }

    /// <summary>Chaque table que le transfert a ecrite pour cette base V2 (rec.Correspondance de la base V3).</summary>
    public static async Task<IReadOnlyList<string>> TablesTransfereesAsync(string v2, string v3, CancellationToken ct)
    {
        var tables = new List<string>();
        await using var cn = new SqlConnection(v3);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT DISTINCT TableV2 FROM rec.Correspondance WHERE BaseV2 = @b ORDER BY TableV2;", cn) { CommandTimeout = 0 };
        cmd.Parameters.AddWithValue("@b", new SqlConnectionStringBuilder(v2).InitialCatalog);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct)) tables.Add(l.GetString(0));
        return tables;
    }
}
