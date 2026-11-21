using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Courriels;

/// <summary>
/// Les valeurs du greffe qu'un courriel de la V2 contient, lues la ou elles sont aujourd'hui (SOC-006 CA02).
/// </summary>
/// <param name="InfoGreffe">La ligne INFO_GREFFE du greffe (colonne -> texte) : les etiquettes [ADRESSE_MAIL_JUDICIAIRE]…
/// des adresses des modeles (SendEmail.filtrerMail). Les colonnes de mot de passe n'y sont pas (voir <see cref="LireAsync"/>).</param>
/// <param name="VilleTribunal">{{VILLE_TRIBUNAL}} : PARAM_EDITIONS.PRMED_VILLE_MAJ (RigGreffe.ParamEdition.VilleMaj).</param>
/// <param name="NomGreffier">{{NOM_GREFFIER}} : GREFFE_PARAMETRE.GRPAR_TPG_NOM_GREFFIER.</param>
/// <param name="Logo">Le fichier du logo du greffe (RigGreffe.getImage("logo")) ; null s'il n'y en a pas.</param>
/// <param name="Habillage">L'habillage du greffe (modele SHELL en base, sinon fichier du reglage) ; "" sans habillage.</param>
/// <param name="Manques">Ce qui n'a pas pu etre lu, en clair (la V2 le journalisait et mettait "" : EmailEnvoye._ValeurGreffe).</param>
public sealed record ValeursGreffe(
    IReadOnlyDictionary<string, string> InfoGreffe,
    string VilleTribunal,
    string NomGreffier,
    string? Logo,
    string Habillage,
    IReadOnlyList<string> Manques)
{
    /// <summary>Les jetons de l'habillage (EmailEnvoye.cs:503-506 ; MESSAGE_PERSONNALISE est ajoute au rendu).</summary>
    public IReadOnlyDictionary<string, string> Jetons => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["VILLE_TRIBUNAL"] = VilleTribunal,
        ["NOM_GREFFIER"] = NomGreffier,
    };
}

/// <summary>
/// OU LA V3 LIT LES VALEURS DU GREFFE D'UN COURRIEL. Chacune la ou la V2 la lit, dans la base qui la tient AUJOURD'HUI :
///
/// | Valeur | V2 | V3 |
/// |---|---|---|
/// | habillage | MAIL_TEMPLATE, sinon fichier SEND_EMAIL_TEMPLATE_PATH | soc.ModeleCourriel (SOC-003), meme repli : <see cref="ModeleCourriel.LireAvecRepliFichierAsync"/> |
/// | {{NOM_GREFFIER}} | GREFFE_PARAMETRE.GRPAR_TPG_NOM_GREFFIER | soc.ParametreGreffe (SOC-003), base V3 du greffe |
/// | logo | GRF_PATH_EDITION_LOT1 + code + \Images\logo.* (RigGreffe.cs:299-302, 1661-1677) | GRF_PATH_EDITION_LOT1 de soc.GreffeInfoV2 (SOC-003), base commune, memes regles de fichier |
/// | {{VILLE_TRIBUNAL}} | PARAM_EDITIONS.PRMED_VILLE_MAJ | la base V2 du greffe, en lecture : PARAM_EDITIONS n'est pas encore dans la V3 (SED-001) |
/// | [ETIQUETTE] des adresses | INFO_GREFFE (InfoGreffe.GetProperty) | la base V2 du greffe, en lecture : INFO_GREFFE n'est pas encore dans la V3 (TRA-025) |
///
/// La base V2 s'ouvre par le catalogue (<see cref="CatalogueGreffes.OuvrirBaseV2Async"/>, intention de lecture) et ne
/// se lit que par <see cref="LectureV2"/> (un SELECT seul). Quand TRA-025 et SED-001 reprendront ces deux tables dans la
/// V3, seules ces deux lectures changeront de base.
/// </summary>
public static partial class LectureValeursGreffe
{
    /// <summary>Les colonnes que l'on ne met jamais dans un courriel : les mots de passe d'INFO_GREFFE (PWD_SQL, PWD_GREFFE).
    /// La V2 les aurait rendus pour une etiquette [PWD_SQL] ; la V3 les ecarte (ecart voulu, socle-courriels.md).</summary>
    [GeneratedRegex("^PWD_", RegexOptions.IgnoreCase)] private static partial Regex ColonneSecrete();

    [GeneratedRegex(@"\[([^[\]]*)\]")] private static partial Regex Etiquette();

    /// <summary>Lit les valeurs du greffe <paramref name="code"/> ; <paramref name="baseV3"/> est sa base V3, deja ouverte.</summary>
    public static async Task<ValeursGreffe> LireAsync(string code, SqlConnection baseV3, CatalogueGreffes catalogue, BaseRig commune,
                                                      CancellationToken ct)
    {
        var manques = new List<string>();
        var habillage = await ModeleCourriel.LireAvecRepliFichierAsync(baseV3, ct);
        var nomGreffier = await NomGreffierAsync(baseV3, ct) ?? "";
        if (nomGreffier.Length == 0) manques.Add("nom du greffier (GRPAR_TPG_NOM_GREFFIER) absent de soc.ParametreGreffe");

        string? logo = null;
        try
        {
            var dossier = await CheminEditionsAsync(commune, code, ct);
            logo = dossier is null ? null : Logo(dossier, code);
            if (logo is null) manques.Add(dossier is null ? "chemin des editions (GRF_PATH_EDITION_LOT1) absent de soc.GreffeInfoV2" : $"aucun logo sous {dossier}");
        }
        catch (SqlException e) { manques.Add($"base commune illisible pour le logo ({e.Message})"); }

        var info = new Dictionary<string, string>(StringComparer.Ordinal);
        var ville = "";
        try
        {
            await using var v2 = await catalogue.OuvrirBaseV2Async(code, ct);
            await using (var r = await LectureV2.LireAsync(v2, "SELECT * FROM dbo.INFO_GREFFE", ct))
                if (await r.ReadAsync(ct))
                    for (var i = 0; i < r.FieldCount; i++)
                        if (!ColonneSecrete().IsMatch(r.GetName(i)))
                            info[r.GetName(i)] = r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? "";
            if (info.Count == 0) manques.Add("INFO_GREFFE vide dans la base V2");
            await using (var r = await LectureV2.LireAsync(v2, "SELECT PRMED_VILLE_MAJ FROM dbo.PARAM_EDITIONS", ct))
                if (await r.ReadAsync(ct) && !r.IsDBNull(0)) ville = r.GetString(0);
            if (ville.Length == 0) manques.Add("ville du tribunal (PARAM_EDITIONS.PRMED_VILLE_MAJ) absente de la base V2");
        }
        catch (Exception e) when (e is SqlException or BaseGreffeInjoignable or GreffeInconnu)
        {
            manques.Add($"base V2 du greffe illisible ({e.Message})");
        }
        return new ValeursGreffe(info, ville, nomGreffier, logo, habillage, manques);
    }

    /// <summary>
    /// Remplace les etiquettes [COLONNE] d'une liste d'adresses par la colonne d'INFO_GREFFE (SendEmail.filtrerMail,
    /// SendEmail.cs:625-646). Une etiquette de moins de 3 caracteres reste telle quelle ; une colonne inconnue devient "",
    /// comme InfoGreffe.GetProperty. <paramref name="inconnues"/> recoit les etiquettes restees sans valeur.
    /// </summary>
    public static string ResoudreAdresses(string? liste, IReadOnlyDictionary<string, string> infoGreffe, ICollection<string>? inconnues = null)
    {
        if (string.IsNullOrEmpty(liste)) return "";
        return string.Join(";", liste.Split(';').Select(adresse => Etiquette().Replace(adresse, m =>
        {
            if (m.Value.Length < 3) return m.Value;
            var nom = m.Value[1..^1];
            if (infoGreffe.TryGetValue(nom, out var v) && v.Length > 0) return v;
            inconnues?.Add(nom);
            return "";
        })));
    }

    /// <summary>
    /// Le logo du greffe, regles de RigGreffe.getImage("logo") : dans &lt;editions&gt;\&lt;code&gt;\Images\ « logo »,
    /// puis logo.png, .gif, .jpg, .bmp ; sinon les memes dans le dossier ou « images » devient « logo ». null s'il manque.
    /// </summary>
    public static string? Logo(string cheminEditions, string code)
    {
        var images = Path.Combine(cheminEditions, code, "Images") + Path.DirectorySeparatorChar;
        foreach (var dossier in new[] { images, Regex.Replace(images, "images", "logo", RegexOptions.IgnoreCase) })
            foreach (var nom in new[] { "logo", "logo.png", "logo.gif", "logo.jpg", "logo.bmp" })
                if (File.Exists(Path.Combine(dossier, nom))) return Path.Combine(dossier, nom);
        return null;
    }

    private static async Task<string?> NomGreffierAsync(SqlConnection baseV3, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            "SELECT CONVERT(nvarchar(4000), Valeur) FROM soc.ParametreGreffe WHERE Colonne = 'GRPAR_TPG_NOM_GREFFIER';", baseV3);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    private static async Task<string?> CheminEditionsAsync(BaseRig commune, string code, CancellationToken ct)
    {
        await using var cn = await commune.OuvrirAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT COALESCE(Texte, CONVERT(nvarchar(4000), Valeur)) FROM soc.GreffeInfoV2 WHERE CodeGreffe = @c AND Colonne = 'GRF_PATH_EDITION_LOT1';", cn);
        cmd.Parameters.Add(new SqlParameter("@c", SqlDbType.VarChar, 20) { Value = code });
        return await cmd.ExecuteScalarAsync(ct) is string s && s.Trim().Length > 0 ? s.Trim() : null;
    }
}
