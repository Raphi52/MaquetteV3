using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RigV3.Passage;

/// <summary>
/// Joue un script ecrit pour sqlcmd (Base\v2\bascule.sql, retour.sql, fin-de-beta.sql) comme sqlcmd le ferait,
/// pour que le passage lance LES scripts du lot 7 et non une copie, sans dependre du client sqlcmd du poste
/// (absent des serveurs, et refuse par le garde-fou des agents) : variables « $(Nom) », inclusions « :r fichier »,
/// « :on error exit », lots coupes par « GO », tous joues sur UNE connexion (une transaction ouverte dans un lot
/// court sur les suivants). Toute autre commande « : » est refusee plutot qu'ignoree. Une erreur arrete le script :
/// la connexion est fermee et sa transaction annulee par le serveur (comme « :on error exit »).
/// Le texte est lu en UTF-8 (sqlcmd -f 65001) : les scripts verifient eux-memes qu'ils l'ont ete.
/// Meme sous-ensemble que le lecteur du banc NumerotationV2Check (tests/NumerotationV2Check/Sqlcmd.cs).
/// </summary>
public static class ScriptSqlcmd
{
    private static readonly Regex Variable = new(@"\$\((?<nom>[A-Za-z_][A-Za-z0-9_]*)\)", RegexOptions.CultureInvariant);
    private static readonly Regex Go = new(@"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Inclure = new(@"^\s*:r\s+(?<f>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SurErreur = new(@"^\s*:on\s+error\s+exit\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Commande = new(@"^\s*:", RegexOptions.CultureInvariant);

    /// <summary>Les lots du script, variables remplacees et inclusions deroulees.</summary>
    public static IReadOnlyList<string> Lots(string fichier, IReadOnlyDictionary<string, string> variables)
    {
        var lots = new List<string>();
        var courant = new StringBuilder();
        Derouler(Path.GetFullPath(fichier), variables, lots, courant, 0);
        if (courant.ToString().Trim().Length > 0) lots.Add(courant.ToString());
        return lots;
    }

    private static void Derouler(string fichier, IReadOnlyDictionary<string, string> variables, List<string> lots,
                                 StringBuilder courant, int profondeur)
    {
        if (profondeur > 5) throw new InvalidOperationException($"Inclusions trop profondes : {fichier}");
        var texte = File.ReadAllText(fichier, Encoding.UTF8).TrimStart('﻿');
        foreach (var brute in texte.Replace("\r\n", "\n").Split('\n'))
        {
            var ligne = Variable.Replace(brute, m => variables.TryGetValue(m.Groups["nom"].Value, out var v)
                ? v
                : throw new InvalidOperationException($"{Path.GetFileName(fichier)} : variable sqlcmd « {m.Groups["nom"].Value} » non definie."));
            if (Go.IsMatch(ligne))
            {
                if (courant.ToString().Trim().Length > 0) lots.Add(courant.ToString());
                courant.Clear();
                continue;
            }
            var inclusion = Inclure.Match(ligne);
            if (inclusion.Success)
            {
                Derouler(Path.Combine(Path.GetDirectoryName(fichier)!, inclusion.Groups["f"].Value.Trim('"')), variables, lots, courant, profondeur + 1);
                continue;
            }
            if (SurErreur.IsMatch(ligne)) continue;
            if (Commande.IsMatch(ligne))
                throw new InvalidOperationException($"{Path.GetFileName(fichier)} : commande sqlcmd non comprise : {ligne.Trim()}");
            courant.Append(ligne).Append('\n');
        }
    }

    /// <summary>Joue le script sur <paramref name="connexion"/> (sans pool : une erreur ferme vraiment la session et
    /// annule sa transaction). Rend les messages PRINT ; une erreur leve la SqlException du serveur.</summary>
    public static async Task<IReadOnlyList<string>> JouerAsync(string connexion, string fichier, IReadOnlyDictionary<string, string> variables,
                                                               CancellationToken ct = default)
    {
        var lots = Lots(fichier, variables);
        var messages = new List<string>();
        await using var cn = new SqlConnection(new SqlConnectionStringBuilder(connexion) { Pooling = false }.ConnectionString);
        cn.InfoMessage += (_, e) => messages.Add(e.Message);
        await cn.OpenAsync(ct);
        foreach (var lot in lots)
        {
            await using var cmd = new SqlCommand(lot, cn) { CommandTimeout = 600 };
            await using var lecteur = await cmd.ExecuteReaderAsync(ct);
            do { while (await lecteur.ReadAsync(ct)) { } } while (await lecteur.NextResultAsync(ct));
        }
        return messages;
    }
}
