using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace NumerotationV2Check;

/// <summary>
/// Joue un script écrit pour sqlcmd (Base\v2\bascule.sql, retour.sql...) comme sqlcmd le ferait, pour
/// que le banc prouve LES scripts que lancera l'exploitation, pas une copie : variables « $(Nom) »,
/// inclusions « :r fichier », « :on error exit », lots coupés par « GO », tous joués sur UNE connexion
/// (une transaction ouverte dans un lot court sur les suivants). Seul ce sous-ensemble est compris ;
/// toute autre commande « : » est refusée plutôt qu'ignorée. Une erreur arrête le script (comme
/// « :on error exit ») : la connexion est fermée, sa transaction annulée par le serveur.
/// « :r » se lit depuis le dossier du fichier qui l'écrit ; sqlcmd, lui, depuis le dossier courant :
/// le mode d'emploi fait lancer les scripts depuis Base\v2, où les deux reviennent au même.
/// </summary>
internal static class Sqlcmd
{
    private static readonly Regex Variable = new(@"\$\((?<nom>[A-Za-z_][A-Za-z0-9_]*)\)", RegexOptions.CultureInvariant);
    private static readonly Regex Go = new(@"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Inclure = new(@"^\s*:r\s+(?<f>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SurErreur = new(@"^\s*:on\s+error\s+exit\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Commande = new(@"^\s*:", RegexOptions.CultureInvariant);

    /// <summary>Ce qu'un script a rendu : ses résultats (une liste de lignes par résultat) et ses PRINT.</summary>
    public sealed record Sortie(IReadOnlyList<IReadOnlyList<object?[]>> Resultats, IReadOnlyList<string> Messages);

    /// <summary>Les lots du script, variables remplacées et inclusions déroulées.</summary>
    /// <param name="encodage">La lecture des fichiers : UTF-8 (option -f 65001) par défaut ; un autre encodage
    /// rejoue un lancement sans cette option (page de code du poste).</param>
    public static IReadOnlyList<string> Lots(string fichier, IReadOnlyDictionary<string, string> variables, Encoding? encodage = null)
    {
        var lots = new List<string>();
        var courant = new StringBuilder();
        Derouler(Path.GetFullPath(fichier), variables, lots, courant, 0, encodage ?? Encoding.UTF8);
        if (courant.ToString().Trim().Length > 0) lots.Add(courant.ToString());
        return lots;
    }

    private static void Derouler(string fichier, IReadOnlyDictionary<string, string> variables, List<string> lots,
                                 StringBuilder courant, int profondeur, Encoding encodage)
    {
        if (profondeur > 5) throw new InvalidOperationException($"Inclusions trop profondes : {fichier}");
        var texte = File.ReadAllText(fichier, encodage).TrimStart('\uFEFF');
        foreach (var brute in texte.Replace("\r\n", "\n").Split('\n'))
        {
            var ligne = Variable.Replace(brute, m => variables.TryGetValue(m.Groups["nom"].Value, out var v)
                ? v
                : throw new InvalidOperationException($"{Path.GetFileName(fichier)} : variable sqlcmd « {m.Groups["nom"].Value} » non définie."));
            if (Go.IsMatch(ligne))
            {
                if (courant.ToString().Trim().Length > 0) lots.Add(courant.ToString());
                courant.Clear();
                continue;
            }
            var inclusion = Inclure.Match(ligne);
            if (inclusion.Success)
            {
                var nom = inclusion.Groups["f"].Value.Trim('"');
                Derouler(Path.Combine(Path.GetDirectoryName(fichier)!, nom), variables, lots, courant, profondeur + 1, encodage);
                continue;
            }
            if (SurErreur.IsMatch(ligne)) continue;
            if (Commande.IsMatch(ligne))
                throw new InvalidOperationException($"{Path.GetFileName(fichier)} : commande sqlcmd non comprise par le banc : {ligne.Trim()}");
            courant.Append(ligne).Append('\n');
        }
    }

    /// <summary>Joue le script sur <paramref name="connexion"/> (sans pool : une erreur ferme vraiment la session).</summary>
    public static async Task<Sortie> JouerAsync(string connexion, string fichier, IReadOnlyDictionary<string, string> variables,
                                                Encoding? encodage = null)
    {
        var lots = Lots(fichier, variables, encodage);
        var resultats = new List<IReadOnlyList<object?[]>>();
        var messages = new List<string>();
        await using var cn = new SqlConnection(new SqlConnectionStringBuilder(connexion) { Pooling = false }.ConnectionString);
        cn.InfoMessage += (_, e) => messages.Add(e.Message);
        await cn.OpenAsync();
        foreach (var lot in lots)
        {
            await using var cmd = new SqlCommand(lot, cn) { CommandTimeout = 300 };
            await using var lecteur = await cmd.ExecuteReaderAsync();
            do
            {
                if (lecteur.FieldCount == 0) continue;
                var lignes = new List<object?[]>();
                while (await lecteur.ReadAsync())
                {
                    var valeurs = new object?[lecteur.FieldCount];
                    lecteur.GetValues(valeurs!);
                    for (var i = 0; i < valeurs.Length; i++) if (valeurs[i] is DBNull) valeurs[i] = null;
                    lignes.Add(valeurs);
                }
                resultats.Add(lignes);
            } while (await lecteur.NextResultAsync());
        }
        return new Sortie(resultats, messages);
    }
}
