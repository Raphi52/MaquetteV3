using System.Text;
using System.Xml;

namespace RigV3.Outils.Anonymisation;

/// <summary>
/// Applique une <see cref="Regle"/> a une valeur lue dans la base source, AVANT qu'elle soit ecrite dans
/// la copie : une valeur personnelle n'arrive jamais en clair sur l'instance de test.
/// </summary>
public sealed class Anonymiseur(Pseudonymes pseudonymes, CatalogueRegles catalogue)
{
    /// <summary>
    /// La valeur de la copie. <paramref name="autreColonne"/> donne la valeur d'une autre colonne de la
    /// meme ligne (la table decrite par un XML du journal). <paramref name="accepteNull"/> : la colonne
    /// accepte NULL (sinon <see cref="Traitement.Vider"/> ecrit une chaine vide).
    /// </summary>
    public object Appliquer(Regle regle, object valeur, bool accepteNull, Func<string, object?> autreColonne)
    {
        if (valeur is DBNull || !regle.Anonymise) return valeur;
        switch (regle.Traitement)
        {
            case Traitement.Vider:
                return accepteNull ? DBNull.Value : valeur is string ? "" : valeur;
            case Traitement.DateNaissance:
                return valeur switch
                {
                    DateTime d => pseudonymes.Date(d),
                    DateTimeOffset o => new DateTimeOffset(pseudonymes.Date(o.DateTime), o.Offset),
                    string s => pseudonymes.DateTexte(s),
                    _ => valeur,
                };
            case Traitement.Xml when valeur is string xml:
                var table = regle.ColonneTable is null ? null : autreColonne(regle.ColonneTable) as string;
                return Xml(xml, table?.Trim());
            default:
                return valeur is string texte ? Texte(regle.Traitement, texte) ?? (accepteNull ? (object)DBNull.Value : "") : valeur;
        }
    }

    /// <summary>Une valeur texte (colonne ou valeur dans un XML) ; null = a retirer (vider).</summary>
    public string? Texte(Traitement t, string v) => t switch
    {
        Traitement.Garder => v,
        Traitement.Vider => null,
        Traitement.Courriel => pseudonymes.Courriel(v),
        Traitement.Fichier => pseudonymes.Fichier(v),
        Traitement.Telephone => pseudonymes.Telephone(v),
        Traitement.DateNaissance => pseudonymes.DateTexte(v),
        _ => pseudonymes.Texte(v),
    };

    /// <summary>
    /// Un XML (fragment accepte : le journal en ecrit un element par ligne touchee). Noms d'elements et
    /// d'attributs gardes ; chaque attribut et chaque texte traites par la regle de la colonne qu'ils
    /// portent dans <paramref name="table"/> (la colonne AUDIT_TABLE de la ligne) ; un attribut a vider
    /// est retire (le journal omet deja les colonnes NULL). Commentaires : remplaces mot a mot.
    /// </summary>
    public string Xml(string xml, string? table)
    {
        var lecture = new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment, DtdProcessing = DtdProcessing.Prohibit };
        var ecriture = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment, OmitXmlDeclaration = true, Indent = false };
        var sb = new StringBuilder(xml.Length);
        using (var r = XmlReader.Create(new StringReader(xml), lecture))
        using (var w = XmlWriter.Create(sb, ecriture))
        {
            var pile = new Stack<string>();
            while (r.Read())
            {
                switch (r.NodeType)
                {
                    case XmlNodeType.Element:
                        var vide = r.IsEmptyElement;
                        w.WriteStartElement(r.Prefix, r.LocalName, r.NamespaceURI);
                        if (r.MoveToFirstAttribute())
                        {
                            do
                            {
                                if (r.NamespaceURI == "http://www.w3.org/2000/xmlns/") continue;   // l'ecrivain les redeclare
                                var v = Texte(catalogue.PourXml(table, r.LocalName).Traitement, r.Value);
                                if (v is not null) w.WriteAttributeString(r.Prefix, r.LocalName, r.NamespaceURI, v);
                            }
                            while (r.MoveToNextAttribute());
                            r.MoveToElement();
                        }
                        if (vide) w.WriteEndElement();
                        else pile.Push(r.LocalName);
                        break;
                    case XmlNodeType.EndElement:
                        w.WriteFullEndElement();
                        pile.Pop();
                        break;
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                        // Texte d'un element : la regle de l'element ; texte hors element : remplace.
                        var regle = pile.Count > 0 ? catalogue.PourXml(table, pile.Peek()).Traitement : Traitement.Mots;
                        w.WriteString(Texte(regle, r.Value) ?? "");
                        break;
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        w.WriteWhitespace(r.Value);
                        break;
                    case XmlNodeType.Comment:
                        w.WriteComment(pseudonymes.Texte(r.Value));
                        break;
                    case XmlNodeType.ProcessingInstruction:
                        w.WriteProcessingInstruction(r.Name, r.Value);
                        break;
                }
            }
        }
        return sb.ToString();
    }
}
