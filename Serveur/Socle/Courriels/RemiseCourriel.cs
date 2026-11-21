using System.Net.Mail;

namespace RigV3.Serveur.Socle.Courriels;

/// <summary>
/// Les deux facons dont un courriel quitte le serveur, avec la bibliotheque de .NET (System.Net.Mail, celle de la V2 :
/// RigGreffe.SendMail, RigGreffe.cs:1135-1140) :
///  - <see cref="RemettreAsync"/> : au serveur de messagerie (SMTP) ;
///  - <see cref="DeposerAsync"/> : dans la boite de depot, un fichier .eml par envoi, que l'on ouvre avec n'importe quel
///    logiciel de messagerie. Rien ne sort du serveur.
/// Le choix entre les deux n'est pas ici : <see cref="OptionsCourriels.Decider"/>.
/// </summary>
public static class RemiseCourriel
{
    /// <summary>Le delai laisse au serveur de messagerie (la V2 gardait celui de SmtpClient, 100 s).</summary>
    public static readonly TimeSpan Delai = TimeSpan.FromSeconds(30);

    /// <summary>Remet <paramref name="message"/> au serveur <paramref name="hote"/> ; un refus leve <see cref="SmtpException"/>.</summary>
    public static async Task RemettreAsync(MailMessage message, string hote, int port, CancellationToken ct)
    {
        using var smtp = new SmtpClient(hote, port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = (int)Delai.TotalMilliseconds,
        };
        await smtp.SendMailAsync(message, ct);
    }

    /// <summary>
    /// Range <paramref name="message"/> dans <paramref name="depot"/>, sous le nom « &lt;suivi&gt;.eml », et rend son chemin.
    /// SmtpClient nomme lui-meme le fichier qu'il ecrit : il l'ecrit dans un dossier a lui seul, puis le fichier est renomme.
    /// </summary>
    public static async Task<string> DeposerAsync(MailMessage message, string depot, Guid suivi, CancellationToken ct)
    {
        var dossier = Directory.CreateDirectory(Path.Combine(depot, suivi.ToString("N"))).FullName;
        using (var smtp = new SmtpClient
        {
            DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
            PickupDirectoryLocation = dossier,
        })
            await smtp.SendMailAsync(message, ct);
        var ecrit = Directory.GetFiles(dossier).Single();
        var fichier = Path.Combine(Path.GetFullPath(depot), suivi.ToString("D") + ".eml");
        File.Move(ecrit, fichier);
        Directory.Delete(dossier);
        return fichier;
    }
}
