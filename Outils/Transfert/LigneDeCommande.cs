using Microsoft.Data.SqlClient;
using RigV3.Base;
using RigV3.Serveur.Recopie;

namespace RigV3.Transfert;

/// <summary>La ligne de commande de l'outil (en tete de Program.cs), appelable par le banc sans processus a part.</summary>
public static class LigneDeCommande
{
    public static async Task<int> ExecuterAsync(string[] args, TextWriter sortie, CancellationToken ct = default)
    {
        Dictionary<string, string> options;
        try { options = Options(args.Skip(1)); }
        catch (ArgumentException e) { sortie.WriteLine(e.Message); return 3; }
        string? Option(string cle) => options.GetValueOrDefault(cle);

        switch (args.FirstOrDefault())
        {
            case "transferer" when Option("v2") is { } v2 && Option("v3") is { } v3:
            {
                if (!options.ContainsKey("exploitation") && !(BaseJetable.EstServeurLocal(v2) && BaseJetable.EstServeurLocal(v3)))
                {
                    sortie.WriteLine($"Refuse : {new SqlConnectionStringBuilder(v2).DataSource} / {new SqlConnectionStringBuilder(v3).DataSource} n'est pas le " +
                                     "serveur de ce poste. Le transfert d'une base de greffe se lance chez Amitel, avec --exploitation.");
                    return 3;
                }
                var lot = Option("lot") is { } l ? int.Parse(l) : 50_000;
                var rapport = await MoteurTransfert.TransfererAsync(new OptionsTransfert(v2, v3)
                {
                    TailleLot = lot,
                    Suivre = e => sortie.WriteLine($"{e.Le:HH:mm:ss} {e.Genre,-10} {e.Message}"),
                }, ct);
                sortie.WriteLine();
                sortie.WriteLine(Texte(rapport));
                return rapport.CodeSortie;
            }
            case "inventaire" when Option("v2") is { } v2:
            {
                var tables = Option("tables") is { } t
                    ? t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : ReglesTransfert.Decouvrir().Regles.Select(r => r.Table).Order(StringComparer.Ordinal).ToArray();
                await using var cn = new SqlConnection(new SqlConnectionStringBuilder(v2) { ApplicationIntent = ApplicationIntent.ReadOnly }.ConnectionString);
                await cn.OpenAsync(ct);
                var catalogue = await CatalogueV2.LireAsync(cn, ct);
                sortie.WriteLine($"// Colonnes relevees dans {cn.Database} ({cn.DataSource}) le {DateTime.Now:yyyy-MM-dd}.");
                foreach (var nom in tables)
                    sortie.WriteLine(catalogue.Tables.TryGetValue(nom, out var table)
                        ? $"(\"{table.Nom}\", [{string.Join(", ", table.Colonnes.Select(c => $"\"{c.Nom}\""))}]),"
                        : $"// {nom} : absente de {cn.Database}");
                return 0;
            }
            default:
                sortie.WriteLine("Usage : RigV3.Transfert transferer --v2 <chaine> --v3 <chaine> [--lot 50000] [--exploitation]");
                sortie.WriteLine("        RigV3.Transfert inventaire --v2 <chaine> [--tables A,B]");
                return 3;
        }
    }

    /// <summary>Le rapport de fin de nuit d'une base (REC-001 T04) : lignes lues, ecrites, rejetees, duree, table par table.</summary>
    public static string Texte(RapportTransfert r)
    {
        var s = new System.Text.StringBuilder();
        s.AppendLine($"Transfert {r.IdTransfert} de {r.BaseV2} : {r.Etat.ToUpperInvariant()} en {r.DureeMs / 1000.0:0.0} s" +
                     (r.InstantaneLe is { } le ? $", etat fige du {le:yyyy-MM-dd HH:mm:ss}" : ""));
        if (r.Motif is not null) s.AppendLine("  " + r.Motif);
        s.AppendLine($"  {"table",-32} {"regle",-12} {"lues",10} {"ecrites",10} {"rejetees",9} {"liens",6} {"ms",8}");
        foreach (var l in r.Tables)
            s.AppendLine($"  {l.TableV2,-32} {l.Regle,-12} {l.Lues,10} {(l.Ecrites?.ToString() ?? "-"),10} {(l.Rejetees?.ToString() ?? "-"),9} " +
                         $"{(l.LiensCasses?.ToString() ?? "-"),6} {l.DureeMs,8}");
        return s.ToString();
    }

    private static Dictionary<string, string> Options(IEnumerable<string> args)
    {
        var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var liste = args.ToList();
        for (var i = 0; i < liste.Count; i++)
        {
            if (!liste[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"option inattendue : {liste[i]}");
            var cle = liste[i][2..];
            o[cle] = i + 1 < liste.Count && !liste[i + 1].StartsWith("--", StringComparison.Ordinal) ? liste[++i] : "";
        }
        return o;
    }
}
