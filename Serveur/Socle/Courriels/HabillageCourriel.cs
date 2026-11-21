using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace RigV3.Serveur.Socle.Courriels;

/// <summary>
/// Un document envoye par lien : la V2 publie la piece et met son lien dans le courriel (EmailEnvoye._getLienHttp),
/// elle ne joint pas le fichier. <paramref name="Fichier"/> est la piece d'origine (trace), <paramref name="Url"/> son lien.
/// </summary>
public sealed record DocumentCourriel(string Fichier, string Url);

/// <summary>
/// Le corps d'un courriel rendu comme la V2 le met dans son MailMessage.
/// </summary>
/// <param name="Corps">Le corps principal (MailMessage.Body, toujours en HTML) ; vide quand l'habillage porte tout.</param>
/// <param name="VueHtml">La vue HTML (AlternateView) : celle de l'habillage, ou celle du logo du rendu historique ; null sinon.</param>
/// <param name="VueTexte">La vue texte de repli (AlternateView text/plain) ; null sinon.</param>
/// <param name="Logo">Le fichier du logo embarque dans <paramref name="VueHtml"/> ; null s'il n'y en a pas.</param>
/// <param name="IdLogo">Son identifiant de contenu (cid) : « LogoGreffe » (habillage) ou « logo » (rendu historique).</param>
/// <param name="Habille">L'habillage du greffe a ete applique.</param>
public sealed record RenduCourriel(string Corps, string? VueHtml, string? VueTexte, string? Logo, string? IdLogo, bool Habille);

/// <summary>
/// LE RENDU D'UN COURRIEL, recopie de la V2 (SOC-006 CA02) :
///  - avec l'habillage du greffe (modele « SHELL » en base, sinon fichier du reglage : <c>Greffes.ModeleCourriel</c>) :
///    GreffeMailBuilder.BuildHtmlViewFromContent (D:\RigApplication\Source\Outils\DLL\RigMetier\Mail\GreffeMailBuilder.cs)
///    et EmailEnvoye._PreparerEntreesTemplate / _AppliquerTemplate (EmailEnvoye.cs:466-536) : le texte du modele tombe
///    dans {{MESSAGE_PERSONNALISE}}, les jetons {{VILLE_TRIBUNAL}}, {{NOM_GREFFIER}}, {{LISTE_DOCUMENTS_HTML}} sont
///    remplaces (un jeton inconnu devient vide), le logo du greffe est embarque sous cid:LogoGreffe ;
///  - sans habillage, ou si l'habillage ne se lit pas (XML casse) : le rendu historique d'EmailEnvoye._EnvoiLienHttp
///    (EmailEnvoye.cs:346-381) et SetLogoInMail (EmailEnvoye.cs:425-457).
/// Les balises du texte : « | » = saut de ligne, $LOGO$ = le logo, $PieceJointe$ = la place des liens.
/// </summary>
public static partial class HabillageCourriel
{
    /// <summary>GreffeMailBuilder.ContentIdLogo.</summary>
    public const string IdLogoHabillage = "LogoGreffe";

    /// <summary>Le cid du rendu historique (SetLogoInMail).</summary>
    public const string IdLogoHistorique = "logo";

    /// <summary>GreffeMailBuilder.TokenListeDocuments.</summary>
    public const string JetonListeDocuments = "{{LISTE_DOCUMENTS_HTML}}";

    /// <summary>La taille du logo du rendu historique quand le greffe n'a pas d'element de fusion LOGO_MAIL ni LOGO
    /// (SetLogoInMail : 400 x 100). Ces elements sont ceux des editions (SED-001), pas encore dans la V3.</summary>
    public const int LargeurLogo = 400, HauteurLogo = 100;

    private const string SautDeLigne = "\r\n";   // Environment.NewLine du poste Windows de la V2

    [GeneratedRegex(@"\{\{(\w+)\}\}")] private static partial Regex Jeton();
    [GeneratedRegex("[$]LOGO[$]", RegexOptions.IgnoreCase)] private static partial Regex BaliseLogo();
    [GeneratedRegex("[$]PieceJointe[$]", RegexOptions.IgnoreCase)] private static partial Regex BalisePieceJointe();
    [GeneratedRegex("[$](LOGO|PieceJointe)[$]", RegexOptions.IgnoreCase)] private static partial Regex Balises();

    /// <summary>
    /// Rend le texte <paramref name="corpsModele"/> du modele (EMAILMOD_CORPS, « | » non encore convertis).
    /// </summary>
    /// <param name="habillage">Le XML &lt;MailTemplate&gt; du greffe ; vide = pas d'habillage.</param>
    /// <param name="valeurs">VILLE_TRIBUNAL, NOM_GREFFIER (la V2 y ajoute MESSAGE_PERSONNALISE, fait ici).</param>
    /// <param name="logo">Le logo du greffe (RigGreffe.getImage("logo")) ; null ou vide = celui du &lt;LogoPath&gt; de l'habillage.</param>
    /// <param name="signaler">Recoit la cause quand l'habillage est illisible et que le rendu historique le remplace.</param>
    public static RenduCourriel Rendre(string corpsModele, bool corpsHtml, IReadOnlyList<DocumentCourriel> documents,
                                       string habillage, IReadOnlyDictionary<string, string> valeurs, string? logo,
                                       Action<string>? signaler = null)
    {
        var corps = corpsModele.Replace("|", SautDeLigne);
        if (!string.IsNullOrEmpty(habillage))
        {
            try
            {
                return AvecHabillage(corps, corpsHtml, documents, habillage, valeurs, logo);
            }
            catch (XmlException e)
            {
                // Comme la V2 (EmailEnvoye.cs:357-361) : un habillage casse ne casse pas l'envoi, il part sans habillage.
                signaler?.Invoke($"habillage illisible ({e.Message}) : rendu sans habillage");
            }
        }
        return Historique(corps, corpsHtml, documents, logo);
    }

    private static RenduCourriel AvecHabillage(string corps, bool corpsHtml, IReadOnlyList<DocumentCourriel> documents,
                                               string habillage, IReadOnlyDictionary<string, string> valeurs, string? logoGreffe)
    {
        var liens = Liens(documents);
        var message = corps;
        var piecesDansCorps = BalisePieceJointe().IsMatch(message);
        if (piecesDansCorps) message = BalisePieceJointe().Replace(message, _ => TableLiens(liens));
        if (BaliseLogo().IsMatch(message))
            message = BaliseLogo().Replace(message, _ => $"<img src=\"cid:{IdLogoHabillage}\" style=\"border:0;\" />");
        if (!corpsHtml) message = message.Replace(SautDeLigne, "<br/>");

        var tout = new Dictionary<string, string>(valeurs, StringComparer.OrdinalIgnoreCase) { ["MESSAGE_PERSONNALISE"] = message };

        var racine = XDocument.Parse(habillage).Root;
        var html = Element(racine, "HtmlBody");
        var logo = !string.IsNullOrEmpty(logoGreffe) ? logoGreffe : Element(racine, "LogoPath");
        // La liste des documents passe AVANT les jetons : son contenu n'est pas relu (GreffeMailBuilder.cs:69-72).
        html = html.Replace(JetonListeDocuments, LignesLiens(piecesDansCorps ? [] : liens));
        html = RemplacerJetons(html, tout);

        var texte = Balises().Replace(corps, "");
        var logoEmbarque = !string.IsNullOrEmpty(logo) && File.Exists(logo) ? logo : null;
        return new RenduCourriel("", html, texte, logoEmbarque, logoEmbarque is null ? null : IdLogoHabillage, Habille: true);
    }

    private static RenduCourriel Historique(string corps, bool corpsHtml, IReadOnlyList<DocumentCourriel> documents, string? logo)
    {
        string? vueHtml = null, vueTexte = null, logoEmbarque = null;
        string LogoDansLeCorps(string c)
        {
            // SetLogoInMail : sans fichier de logo, le corps n'a ni vue HTML ni vue texte, seulement $LOGO$ retire.
            if (!string.IsNullOrEmpty(logo) && File.Exists(logo))
            {
                var avecLogo = BaliseLogo().Replace(c, _ => $"<br/><img src=\"cid:{IdLogoHistorique}\" width=\"{LargeurLogo}\" height=\"{HauteurLogo}\"/><br/>");
                vueHtml = "<html><body>" + avecLogo.Replace(SautDeLigne, " <br/> ") + "</body></html>";
                vueTexte = c;
                logoEmbarque = logo;
            }
            return BaliseLogo().Replace(c, "");
        }

        // Contains de la V2 : sensible a la casse ici (EmailEnvoye.cs:364), contrairement a l'habillage.
        string rendu;
        if (!corps.Contains("$PieceJointe$", StringComparison.Ordinal))
        {
            var entete = EntetePieces(documents);
            rendu = corpsHtml ? LogoDansLeCorps(corps + SautDeLigne + entete)
                              : (corps + SautDeLigne + entete).Replace(SautDeLigne, "<br/>");
        }
        else
        {
            var liens = SautDeLigne + LiensSeuls(documents, 150);
            rendu = corpsHtml ? LogoDansLeCorps(corps.Replace("$PieceJointe$", liens))
                              : (corps + SautDeLigne).Replace("$PieceJointe$", liens).Replace(SautDeLigne, "<br/>");
        }
        return new RenduCourriel(rendu, vueHtml, vueTexte, logoEmbarque, logoEmbarque is null ? null : IdLogoHistorique, Habille: false);
    }

    /// <summary>Le message a remettre : adresses, objet, et le rendu tel que la V2 le pose dans son MailMessage.</summary>
    public static MailMessage Message(string expediteur, string? reponse, string destinataires, string? copie, string sujet, RenduCourriel rendu)
    {
        var mail = new MailMessage
        {
            Sender = new MailAddress(expediteur),
            From = new MailAddress(expediteur),
            Subject = sujet,
            Body = rendu.Corps,
            IsBodyHtml = true,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
        };
        Ajouter(mail.ReplyToList, reponse);
        Ajouter(mail.To, destinataires);
        Ajouter(mail.Bcc, copie);    // EMAILMOD_COPY_TO part en copie cachee (EmailEnvoye.cs:384)
        if (rendu.VueHtml is not null)
        {
            var vue = AlternateView.CreateAlternateViewFromString(rendu.VueHtml, Encoding.UTF8, MediaTypeNames.Text.Html);
            if (rendu.Logo is not null)
                vue.LinkedResources.Add(new LinkedResource(rendu.Logo, TypeImage(rendu.Logo))
                {
                    ContentId = rendu.IdLogo,
                    TransferEncoding = TransferEncoding.Base64,
                });
            mail.AlternateViews.Add(vue);
        }
        if (rendu.VueTexte is not null)
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(rendu.VueTexte, Encoding.UTF8, MediaTypeNames.Text.Plain));
        return mail;
    }

    /// <summary>Les adresses d'une liste « a;b ; c » (EmailEnvoye.SetEmail) ; une adresse mal formee leve FormatException.</summary>
    public static IEnumerable<string> Adresses(string? liste)
        => (liste ?? "").Split(';').Select(a => a.Trim()).Where(a => a.Length > 0);

    /// <summary>
    /// Une liste d'adresses sans doublon, dans l'ordre (EmailEnvoye.NettoieListeEmail, comparaison sans la casse).
    /// </summary>
    public static string Nettoyer(string? liste)
        => string.Join(";", Adresses(liste).Distinct(StringComparer.OrdinalIgnoreCase));

    /// <summary>GreffeMailBuilder.ReplaceTokens : {{TOKEN}} remplace par sa valeur, un jeton inconnu par rien.</summary>
    public static string RemplacerJetons(string texte, IReadOnlyDictionary<string, string> valeurs)
        => string.IsNullOrEmpty(texte) ? "" : Jeton().Replace(texte, m => valeurs.TryGetValue(m.Groups[1].Value, out var v) ? v ?? "" : "");

    private static void Ajouter(MailAddressCollection collection, string? liste)
    {
        foreach (var adresse in Adresses(liste)) collection.Add(new MailAddress(adresse));
    }

    private static List<KeyValuePair<string, string>> Liens(IReadOnlyList<DocumentCourriel> documents)
        => documents.Select((d, i) => new KeyValuePair<string, string>("Lien n°" + (i + 1), d.Url)).ToList();

    /// <summary>GreffeMailBuilder.BuildDocumentsHtml : une ligne &lt;tr&gt; par document.</summary>
    private static string LignesLiens(IEnumerable<KeyValuePair<string, string>> liens)
    {
        var sb = new StringBuilder();
        foreach (var (libelle, url) in liens)
            sb.Append("<tr>")
              .Append("<td style=\"padding:10px 12px; border:1px solid #e5e7eb; font-size:14px;\">")
              .Append("<a href=\"").Append(WebUtility.HtmlEncode(url)).Append("\" style=\"color:#0055a4; text-decoration:none;\">")
              .Append(WebUtility.HtmlEncode(libelle))
              .Append("</a>")
              .Append("</td>")
              .Append("</tr>");
        return sb.ToString();
    }

    /// <summary>EmailEnvoye._FormaterLiensHtml : les memes lignes, dans la meme table que l'habillage.</summary>
    private static string TableLiens(List<KeyValuePair<string, string>> liens)
        => liens.Count == 0 ? ""
           : "<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"border-collapse:collapse;\">" + LignesLiens(liens) + "</table>";

    /// <summary>EmailEnvoye._getEntetePJ : le bloc des liens du rendu historique.</summary>
    private static string EntetePieces(IReadOnlyList<DocumentCourriel> documents)
    {
        if (documents.Count == 0) return "";
        const int largeur = 150;
        var separateur = new string('_', largeur) + SautDeLigne;
        var texte = separateur + (documents.Count > 1
            ? "Les documents PDF sont à télécharger en suivant les liens ci-dessous. Ils sont en ligne durant 30 jours. Passé ce délai, les liens seront inactifs."
            : "Le document PDF est à télécharger en suivant le lien ci-dessous. Il est en ligne durant 30 jours. Passé ce délai, le lien sera inactif.")
            + SautDeLigne;
        return texte + LiensSeuls(documents, largeur) + separateur;
    }

    /// <summary>EmailEnvoye._getLiensSeuls.</summary>
    private static string LiensSeuls(IReadOnlyList<DocumentCourriel> documents, int largeur)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < documents.Count; i++)
            sb.Append(("   <a href='" + documents[i].Url + "'> Lien n°" + (i + 1) + " </a>    ").PadRight(largeur - 8)).Append(SautDeLigne);
        return sb.ToString();
    }

    private static string Element(XElement? racine, string nom) => racine?.Element(nom)?.Value ?? "";

    /// <summary>Le type MIME du logo d'apres son extension (GreffeMailBuilder : « MIME auto-detecte »).</summary>
    private static string TypeImage(string chemin) => Path.GetExtension(chemin).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => MediaTypeNames.Image.Gif,
        ".jpg" or ".jpeg" => MediaTypeNames.Image.Jpeg,
        ".bmp" => "image/bmp",
        _ => MediaTypeNames.Application.Octet,
    };
}
