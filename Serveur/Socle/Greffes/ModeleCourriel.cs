using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.Greffes;

/// <summary>
/// L'habillage des courriels du greffe (SOC-003 CA06), choisi comme la V2
/// (D:\RigApplication\Source\Outils\DLL\RigMetier\Mail\MailTemplateStore.cs:16-43) :
/// le modele « SHELL » de la base d'abord ; s'il manque ou s'il est vide, le fichier
/// designe par le reglage SEND_EMAIL_TEMPLATE_PATH ; si le chemin est vide, introuvable
/// ou illisible, "" -- l'envoi garde alors son rendu historique, sans habillage
/// (EmailEnvoye.cs:475-478). L'envoi lui-meme n'est pas encore construit en V3.
/// </summary>
public static class ModeleCourriel
{
    /// <summary>Code de l'unique modele d'habillage du greffe (MailTemplateStore.CodeShell).</summary>
    public const string CodeShell = "SHELL";

    /// <summary>Le chemin du fichier de repli ; vide si absent (MailTemplateStore.cs:33).</summary>
    internal static readonly LectureReglage CheminFichier =
        new("SEND_EMAIL_TEMPLATE_PATH", "", @"D:\RigApplication\Source\Outils\DLL\RigMetier\Mail\MailTemplateStore.cs:33");

    /// <summary>Le modele effectif du greffe : base, sinon fichier, sinon "".</summary>
    public static async Task<string> LireAvecRepliFichierAsync(SqlConnection baseGreffe, CancellationToken ct = default)
    {
        var contenu = await LireAsync(baseGreffe, CodeShell, ct);
        return string.IsNullOrEmpty(contenu) ? await LireFichierAsync(baseGreffe, ct) : contenu;
    }

    /// <summary>Le modele de la base seule ; "" s'il manque.</summary>
    public static async Task<string> LireAsync(SqlConnection baseGreffe, string code, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand("SELECT Contenu FROM soc.ModeleCourriel WHERE Code = @c;", baseGreffe);
        cmd.Parameters.Add(new SqlParameter("@c", System.Data.SqlDbType.VarChar, 40) { Value = code });
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is string s ? s : "";
    }

    /// <summary>Le fichier de repli seul (MailTemplateStore.LireFichierGed2) ; "" s'il manque.</summary>
    public static async Task<string> LireFichierAsync(SqlConnection baseGreffe, CancellationToken ct = default)
    {
        var chemin = await CheminFichier.LireTexteAsync(baseGreffe, ct);
        if (string.IsNullOrEmpty(chemin) || !(File.Exists(chemin) || Directory.Exists(chemin))) return "";
        try
        {
            return await File.ReadAllTextAsync(chemin, ct);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Comme la V2 (MailTemplateStore.cs:39-41) : illisible = pas d'habillage, et la
            // cause part au journal de l'exploitant au lieu d'etre perdue.
            Console.Error.WriteLine($"ModeleCourriel : fichier {chemin} illisible ({e.Message}) ; envoi sans habillage.");
            return "";
        }
    }
}
