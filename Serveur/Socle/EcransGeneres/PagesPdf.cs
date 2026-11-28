using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// Les PAGES d'un fichier PDF et leur rotation, et une rotation ENREGISTREE dans le fichier (« Pivoter des pages », PROC_PDF_WRITER :
/// EcrivainPivoterPages.cs, LecteurPivoterPages.cs ; plan « RIG v3 complet, mesure par le banc du parcours complet », lot 5, 28/11/2026).
///
/// CE QUE FAIT RIG v2 : AmiAsposePdf.RotationPagesPdf (D:\RigApplication\Source\Outils\AmiDLL\AmiAspose\AmiAsposePdf.cs:2314-2341)
/// ouvre le PDF avec Aspose, pose sur chaque page a tourner sa rotation (RotationPage, :2262-2300 : la rotation de la page plus celle
/// demandee, ramenee entre 0 et 359, « (((degreRotation + rotationOrigine) % 360) + 360) % 360 », :2284) et enregistre un nouveau
/// fichier. La rotation d'une page PDF est l'entree /Rotate de son dictionnaire, heritable d'un noeud /Pages (ISO 32000-1, 7.7.3.3,
/// tableau 30) : c'est elle qu'Aspose change (Page.Rotate).
/// LA V3, sans bibliotheque PDF au serveur : une MISE A JOUR INCREMENTALE (ISO 32000-1, 7.5.6) -- le fichier d'origine est garde tel
/// quel, octet pour octet ; a sa suite, chaque page tournee est reecrite (meme numero d'objet, son dictionnaire d'origine avec /Rotate
/// pose ou remplace), puis une section de renvois du meme genre que la derniere du fichier (table classique, 7.5.4, ou flux de renvois,
/// 7.5.8) qui pointe sur la precedente (/Prev), et startxref. Tout lecteur PDF lit la derniere version de chaque objet.
/// LU : table de renvois classique et flux de renvois (/W, /Index, FlateDecode et predicteurs PNG), fichiers hybrides (/XRefStm),
/// chaines /Prev, objets ranges dans un flux d'objets (/ObjStm, 7.5.7), arbre des pages (/Kids) et /Rotate herite.
/// NON LU (le fichier est dit illisible, rien n'est ecrit) : un PDF chiffre (/Encrypt : ses objets reecrits devraient l'etre avec la cle
/// du document), un filtre autre que FlateDecode sur un flux de renvois ou d'objets, un fichier sans renvois ni arbre de pages lisibles.
/// </summary>
internal static class PagesPdf
{
    /// <summary>Un fichier que ce lecteur ne lit pas : <see cref="Exception.Message"/> dit pourquoi, en mots.</summary>
    internal sealed class PdfIllisible(string motif) : Exception(motif);

    /// <summary>La rotation de chaque page, dans l'ordre du document (0, 90, 180 ou 270).</summary>
    internal static int[] Rotations(byte[] pdf) => [.. new Document(pdf).Pages().Select(p => p.Rotation)];

    /// <summary>
    /// <paramref name="pdf"/> avec la rotation <paramref name="ajouts"/> (numero de page a partir de 1 -> degres ajoutes, + horaire)
    /// enregistree, comme RotationPage d'Aspose ; leve <see cref="PdfIllisible"/> si le fichier ne se lit pas ou si une page n'existe pas.
    /// </summary>
    internal static byte[] Pivoter(byte[] pdf, IReadOnlyDictionary<int, int> ajouts)
    {
        var document = new Document(pdf);
        var pages = document.Pages();
        var hors = ajouts.Keys.Where(n => n < 1 || n > pages.Count).ToArray();
        if (hors.Length > 0)
            throw new PdfIllisible($"page {hors[0]} absente du document ({pages.Count} page{(pages.Count > 1 ? "s" : "")})");
        var reecrits = new SortedDictionary<int, (int Gen, byte[] Corps)>();
        foreach (var (numero, ajout) in ajouts)
        {
            var page = pages[numero - 1];
            var rotation = (((ajout + page.Rotation) % 360) + 360) % 360;
            if (rotation == page.Rotation) continue;
            reecrits[page.Objet] = (page.Gen, document.AvecRotation(page.Objet, rotation));
        }
        return reecrits.Count == 0 ? pdf : document.MiseAJour(reecrits);
    }

    private sealed record Page(int Objet, int Gen, int Rotation);

    // ---- Objets PDF (ISO 32000-1, 7.3) : ce qu'il faut pour lire l'arbre des pages et les renvois. ----

    private abstract record Obj(int Debut, int Fin);
    private sealed record Nom(string Valeur, int Debut, int Fin) : Obj(Debut, Fin);
    private sealed record Nombre(string Texte, int Debut, int Fin) : Obj(Debut, Fin)
    {
        internal long Entier => long.TryParse(Texte, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
            ? n : (long)double.Parse(Texte, CultureInfo.InvariantCulture);
    }
    private sealed record Reference(int Objet, int Gen, int Debut, int Fin) : Obj(Debut, Fin);
    private sealed record Tableau(List<Obj> Elements, int Debut, int Fin) : Obj(Debut, Fin);
    private sealed record Dico(Dictionary<string, Obj> Entrees, int Debut, int Fin) : Obj(Debut, Fin)
    {
        internal Obj? this[string cle] => Entrees.GetValueOrDefault(cle);
    }
    private sealed record Autre(int Debut, int Fin) : Obj(Debut, Fin);   // chaine, booleen, null : jamais interpretes ici

    /// <summary>Un objet indirect lu : sa valeur, le tampon ou il est ecrit (le fichier, ou un flux d'objets decode) et son flux.</summary>
    private sealed record Indirect(Obj Valeur, byte[] Source, byte[]? Flux);

    /// <summary>Un renvoi : type 1 = position dans le fichier, type 2 = rang dans un flux d'objets, type 0 = objet libre.</summary>
    private readonly record struct Renvoi(int Type, long Champ2, int Champ3);

    /// <summary>Un PDF lu : ses renvois (le plus recent d'abord), sa remorque (trailer) la plus recente et la position de sa section.</summary>
    private sealed class Document
    {
        private readonly byte[] pdf;
        private readonly Dictionary<int, Renvoi> renvois = [];
        private readonly Dictionary<int, Indirect> lus = [];
        private readonly Dictionary<int, (byte[] Tampon, int[] Positions)> fluxObjets = [];
        private readonly Dico remorque;
        private readonly long derniereSection;
        private readonly bool derniereEnFlux;

        internal Document(byte[] pdf)
        {
            this.pdf = pdf;
            if (pdf.Length < 8 || !Commence(pdf, 0, "%PDF-")) throw new PdfIllisible("le fichier ne commence pas par l'en-tête %PDF");
            derniereSection = Startxref();
            Dico? premiere = null;
            var vues = new HashSet<long>();
            var section = (long?)derniereSection;
            while (section is { } s && vues.Add(s))
            {
                var (dico, enFlux, precedente) = LireSection(s);
                if (premiere is null) { premiere = dico; derniereEnFlux = enFlux; }
                section = precedente;
            }
            remorque = premiere ?? throw new PdfIllisible("aucune table de renvois");
            if (remorque["Encrypt"] is not null)
                throw new PdfIllisible("le PDF est chiffré (/Encrypt) : RIG v3 ne réécrit pas ses objets");
        }

        /// <summary>Les pages, dans l'ordre de l'arbre (/Root -> /Pages -> /Kids), avec leur /Rotate propre ou herite.</summary>
        internal List<Page> Pages()
        {
            var catalogue = Resoudre(remorque["Root"]) as Dico ?? throw new PdfIllisible("catalogue (/Root) introuvable");
            var pages = new List<Page>();
            Parcourir(catalogue["Pages"] as Reference ?? throw new PdfIllisible("arbre des pages (/Pages) introuvable"), 0, pages, []);
            if (pages.Count == 0) throw new PdfIllisible("aucune page");
            return pages;
        }

        private void Parcourir(Reference noeud, int herite, List<Page> pages, HashSet<int> vus)
        {
            if (!vus.Add(noeud.Objet)) throw new PdfIllisible($"arbre des pages en boucle (objet {noeud.Objet})");
            var dico = Lire(noeud.Objet).Valeur as Dico ?? throw new PdfIllisible($"noeud de page {noeud.Objet} illisible");
            var rotation = dico["Rotate"] is { } r ? Angle(Resoudre(r)) : herite;
            var type = (Resoudre(dico["Type"]) as Nom)?.Valeur;
            if (type == "Pages" || (type is null && dico["Kids"] is not null))
            {
                var enfants = Resoudre(dico["Kids"]) as Tableau ?? throw new PdfIllisible($"noeud {noeud.Objet} sans /Kids");
                foreach (var enfant in enfants.Elements)
                    Parcourir(enfant as Reference ?? throw new PdfIllisible($"enfant direct dans /Kids de {noeud.Objet}"), rotation, pages, vus);
            }
            else pages.Add(new Page(noeud.Objet, renvois.TryGetValue(noeud.Objet, out var rv) && rv.Type == 1 ? rv.Champ3 : 0, rotation));
        }

        /// <summary>/Rotate ramene entre 0 et 270 ; un angle qui n'est pas un multiple de 90 compte 0, comme RotationPage (:2277).</summary>
        private static int Angle(Obj? valeur)
        {
            if (valeur is not Nombre n) return 0;
            var a = (int)(((n.Entier % 360) + 360) % 360);
            return a % 90 == 0 ? a : 0;
        }

        /// <summary>Le dictionnaire de la page <paramref name="objet"/>, /Rotate pose a <paramref name="rotation"/> (les autres octets gardes).</summary>
        internal byte[] AvecRotation(int objet, int rotation)
        {
            var lu = Lire(objet);
            var dico = lu.Valeur as Dico ?? throw new PdfIllisible($"page {objet} illisible");
            var texte = rotation.ToString(CultureInfo.InvariantCulture);
            var corps = lu.Source.AsSpan(dico.Debut, dico.Fin - dico.Debut);
            var sortie = new MemoryStream();
            if (dico.Entrees.TryGetValue("Rotate", out var ancien))
            {
                sortie.Write(corps[..(ancien.Debut - dico.Debut)]);
                sortie.Write(Encoding.ASCII.GetBytes(texte));
                sortie.Write(corps[(ancien.Fin - dico.Debut)..]);
            }
            else
            {
                sortie.Write(corps[..2]);
                sortie.Write(Encoding.ASCII.GetBytes($" /Rotate {texte} "));
                sortie.Write(corps[2..]);
            }
            return sortie.ToArray();
        }

        /// <summary>Le fichier d'origine, les objets <paramref name="reecrits"/> a sa suite, une section de renvois et startxref.</summary>
        internal byte[] MiseAJour(SortedDictionary<int, (int Gen, byte[] Corps)> reecrits)
        {
            var sortie = new MemoryStream();
            sortie.Write(pdf);
            if (pdf[^1] is not ((byte)'\n' or (byte)'\r')) Ecrire(sortie, "\n");
            var positions = new SortedDictionary<int, (long Position, int Gen)>();
            foreach (var (objet, (gen, corps)) in reecrits)
            {
                positions[objet] = (sortie.Position, gen);
                Ecrire(sortie, $"{objet} {gen} obj\n");
                sortie.Write(corps);
                Ecrire(sortie, "\nendobj\n");
            }
            var taille = Math.Max(Resoudre(remorque["Size"]) is Nombre n ? (int)n.Entier : 0, reecrits.Keys.Max() + 1);
            var communs = new StringBuilder();
            foreach (var cle in new[] { "Root", "Info", "ID" })
                if (remorque[cle] is { } v) communs.Append(" /").Append(cle).Append(' ').Append(Encoding.Latin1.GetString(pdf, v.Debut, v.Fin - v.Debut));
            communs.Append(" /Prev ").Append(derniereSection.ToString(CultureInfo.InvariantCulture));
            long section;
            if (derniereEnFlux)
            {
                var objetFlux = taille++;
                section = sortie.Position;
                positions[objetFlux] = (section, 0);
                var donnees = new MemoryStream();
                foreach (var (_, (position, gen)) in positions)
                {
                    donnees.WriteByte(1);
                    for (var k = 3; k >= 0; k--) donnees.WriteByte((byte)(position >> (8 * k)));
                    donnees.WriteByte((byte)(gen >> 8)); donnees.WriteByte((byte)gen);
                }
                Ecrire(sortie, $"{objetFlux} 0 obj\n<< /Type /XRef /Size {taille} /W [1 4 2] /Index [{Plages(positions.Keys)}]{communs} /Length {donnees.Length} >>\nstream\n");
                donnees.WriteTo(sortie);
                Ecrire(sortie, "\nendstream\nendobj\n");
            }
            else
            {
                section = sortie.Position;
                var table = new StringBuilder("xref\n");
                foreach (var (debut, nombre) in Suites(positions.Keys))
                {
                    table.Append(CultureInfo.InvariantCulture, $"{debut} {nombre}\n");
                    for (var o = debut; o < debut + nombre; o++)
                        table.Append(CultureInfo.InvariantCulture, $"{positions[o].Position:D10} {positions[o].Gen:D5} n\r\n");
                }
                table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {taille}{communs} >>\n");
                Ecrire(sortie, table.ToString());
            }
            Ecrire(sortie, $"startxref\n{section.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
            return sortie.ToArray();
        }

        private static IEnumerable<(int Debut, int Nombre)> Suites(IEnumerable<int> objets)
        {
            int? debut = null; var nombre = 0;
            foreach (var o in objets)
            {
                if (debut is { } d && o == d + nombre) { nombre++; continue; }
                if (debut is { } fini) yield return (fini, nombre);
                debut = o; nombre = 1;
            }
            if (debut is { } dernier) yield return (dernier, nombre);
        }

        private static string Plages(IEnumerable<int> objets) => string.Join(' ', Suites(objets).Select(s => $"{s.Debut} {s.Nombre}"));

        private static void Ecrire(Stream s, string texte) => s.Write(Encoding.ASCII.GetBytes(texte));

        // ---- Renvois (7.5.4, 7.5.8) ----

        private long Startxref()
        {
            var depuis = Math.Max(0, pdf.Length - 2048);
            for (var i = pdf.Length - 9; i >= depuis; i--)
                if (Commence(pdf, i, "startxref"))
                {
                    var l = new Lecteur(pdf, i + 9);
                    if (l.Valeur() is Nombre n && n.Entier >= 0 && n.Entier < pdf.Length) return n.Entier;
                    break;
                }
            throw new PdfIllisible("startxref absent ou hors du fichier");
        }

        private (Dico Remorque, bool EnFlux, long? Precedente) LireSection(long position)
        {
            var l = new Lecteur(pdf, (int)position);
            l.Blancs();
            if (Commence(pdf, l.Position, "xref"))
            {
                l.Position += 4;
                while (true)
                {
                    l.Blancs();
                    if (Commence(pdf, l.Position, "trailer")) { l.Position += 7; break; }
                    if (l.Valeur() is not Nombre debut || l.Valeur() is not Nombre nombre) throw new PdfIllisible("table de renvois illisible");
                    for (var k = 0; k < nombre.Entier; k++)
                    {
                        var champ2 = l.Valeur() as Nombre; var champ3 = l.Valeur() as Nombre;
                        l.Blancs();
                        var type = l.Position < pdf.Length ? pdf[l.Position++] : (byte)0;
                        if (champ2 is null || champ3 is null || type is not ((byte)'n' or (byte)'f')) throw new PdfIllisible("entrée de renvoi illisible");
                        // Une entree libre n'est pas retenue : dans un fichier hybride, la table marque libres les objets du flux /XRefStm.
                        if (type == 'n') renvois.TryAdd((int)(debut.Entier + k), new Renvoi(1, champ2.Entier, (int)champ3.Entier));
                    }
                }
                var dico = l.Valeur() as Dico ?? throw new PdfIllisible("remorque (trailer) illisible");
                // Fichier hybride (7.5.8.4) : les objets du flux /XRefStm passent avant ceux de /Prev.
                if (Resoudre(dico["XRefStm"]) is Nombre flux) LireFluxRenvois(flux.Entier);
                return (dico, false, Resoudre(dico["Prev"]) is Nombre p ? p.Entier : null);
            }
            var (dicoFlux, precedente) = LireFluxRenvois(position);
            return (dicoFlux, true, precedente);
        }

        private (Dico Dico, long? Precedente) LireFluxRenvois(long position)
        {
            var (valeur, flux) = ObjetA(pdf, (int)position, null);
            if (valeur is not Dico dico || flux is null || (Resoudre(dico["Type"]) as Nom)?.Valeur != "XRef")
                throw new PdfIllisible("section de renvois illisible (ni table « xref » ni flux /XRef à la position de startxref)");
            var w = (Resoudre(dico["W"]) as Tableau)?.Elements.Select(e => (int)((Resoudre(e) as Nombre)?.Entier ?? -1)).ToArray();
            if (w is not { Length: 3 } || w.Any(x => x < 0 || x > 8)) throw new PdfIllisible("flux de renvois sans /W lisible");
            var taille = (int)((Resoudre(dico["Size"]) as Nombre)?.Entier ?? 0);
            var index = (Resoudre(dico["Index"]) as Tableau)?.Elements.Select(e => (Resoudre(e) as Nombre)?.Entier ?? -1).ToArray() ?? [0, taille];
            var donnees = Decoder(dico, flux);
            var largeur = w.Sum();
            var pos = 0;
            for (var s = 0; s + 1 < index.Length; s += 2)
                for (var k = 0; k < index[s + 1]; k++)
                {
                    if (pos + largeur > donnees.Length) throw new PdfIllisible("flux de renvois tronqué");
                    var type = w[0] == 0 ? 1 : (int)Champ(donnees, ref pos, w[0]);
                    var champ2 = Champ(donnees, ref pos, w[1]);
                    var champ3 = (int)Champ(donnees, ref pos, w[2]);
                    if (type is 1 or 2) renvois.TryAdd((int)(index[s] + k), new Renvoi(type, champ2, champ3));
                }
            return (dico, Resoudre(dico["Prev"]) is Nombre p ? p.Entier : null);
        }

        private static long Champ(byte[] donnees, ref int pos, int largeur)
        {
            long v = 0;
            for (var k = 0; k < largeur; k++) v = (v << 8) | donnees[pos++];
            return v;
        }

        // ---- Objets indirects (7.3.10), flux d'objets (7.5.7) ----

        private Obj? Resoudre(Obj? valeur) => valeur is Reference r ? Lire(r.Objet).Valeur : valeur;

        private Indirect Lire(int objet)
        {
            if (lus.TryGetValue(objet, out var deja)) return deja;
            if (!renvois.TryGetValue(objet, out var renvoi) || renvoi.Type == 0) throw new PdfIllisible($"objet {objet} absent des renvois");
            Indirect lu;
            if (renvoi.Type == 1)
            {
                var (valeur, flux) = ObjetA(pdf, (int)renvoi.Champ2, objet);
                lu = new Indirect(valeur, pdf, flux);
            }
            else
            {
                var (tampon, positions) = FluxObjets((int)renvoi.Champ2);
                if (renvoi.Champ3 < 0 || renvoi.Champ3 >= positions.Length) throw new PdfIllisible($"objet {objet} hors de son flux d'objets");
                lu = new Indirect(new Lecteur(tampon, positions[renvoi.Champ3]).Valeur() ?? throw new PdfIllisible($"objet {objet} illisible"), tampon, null);
            }
            lus[objet] = lu;
            return lu;
        }

        private (byte[] Tampon, int[] Positions) FluxObjets(int objet)
        {
            if (fluxObjets.TryGetValue(objet, out var deja)) return deja;
            var lu = Lire(objet);
            if (lu.Valeur is not Dico dico || lu.Flux is null) throw new PdfIllisible($"flux d'objets {objet} illisible");
            var tampon = Decoder(dico, lu.Flux);
            var nombre = (int)((Resoudre(dico["N"]) as Nombre)?.Entier ?? 0);
            var premier = (int)((Resoudre(dico["First"]) as Nombre)?.Entier ?? 0);
            var l = new Lecteur(tampon, 0);
            var positions = new int[nombre];
            for (var k = 0; k < nombre; k++)
            {
                if (l.Valeur() is not Nombre || l.Valeur() is not Nombre decalage) throw new PdfIllisible($"en-tête du flux d'objets {objet} illisible");
                positions[k] = premier + (int)decalage.Entier;
            }
            return fluxObjets[objet] = (tampon, positions);
        }

        /// <summary>L'objet « n g obj ... endobj » a <paramref name="position"/> ; s'il n'y est pas, sa derniere ecriture dans le fichier
        /// (des renvois decales sont frequents, les lecteurs PDF reconstruisent de meme).</summary>
        private (Obj Valeur, byte[]? Flux) ObjetA(byte[] source, int position, int? attendu)
        {
            var l = new Lecteur(source, position);
            if (l.Valeur() is Nombre n && l.Valeur() is Nombre && l.MotCle("obj") && (attendu is null || n.Entier == attendu))
                return Corps(source, l, attendu ?? (int)n.Entier);
            if (attendu is { } numero && Chercher(numero) is { } trouve)
            {
                var l2 = new Lecteur(source, trouve);
                l2.Valeur(); l2.Valeur(); l2.MotCle("obj");
                return Corps(source, l2, numero);
            }
            throw new PdfIllisible(attendu is { } a ? $"objet {a} introuvable à la position {position}" : $"aucun objet à la position {position}");
        }

        private int? Chercher(int numero)
        {
            var motif = Encoding.ASCII.GetBytes($"{numero} ");
            for (var i = pdf.Length - motif.Length; i >= 0; i--)
            {
                if (!Commence(pdf, i, motif) || (i > 0 && pdf[i - 1] is >= (byte)'0' and <= (byte)'9')) continue;
                var l = new Lecteur(pdf, i);
                if (l.Valeur() is Nombre && l.Valeur() is Nombre && l.MotCle("obj")) return i;
            }
            return null;
        }

        private (Obj Valeur, byte[]? Flux) Corps(byte[] source, Lecteur l, int objet)
        {
            var valeur = l.Valeur() ?? throw new PdfIllisible($"objet {objet} vide");
            if (valeur is not Dico dico || !l.MotCle("stream")) return (valeur, null);
            if (l.Position < source.Length && source[l.Position] == '\r') l.Position++;
            if (l.Position < source.Length && source[l.Position] == '\n') l.Position++;
            var debut = l.Position;
            var longueur = Resoudre(dico["Length"]) is Nombre n ? (int)n.Entier : -1;
            if (longueur < 0 || debut + longueur > source.Length || !FinDeFlux(source, debut + longueur))
            {
                longueur = -1;
                for (var i = debut; i + 9 <= source.Length; i++)
                    if (Commence(source, i, "endstream")) { longueur = i - debut; break; }
                if (longueur < 0) throw new PdfIllisible($"flux de l'objet {objet} sans « endstream »");
            }
            return (dico, source[debut..(debut + longueur)]);
        }

        private static bool FinDeFlux(byte[] source, int position)
        {
            var l = new Lecteur(source, position);
            l.Blancs();
            return Commence(source, l.Position, "endstream");
        }

        // ---- Filtres (7.4.4) et predicteurs (7.4.4.4) d'un flux de renvois ou d'objets ----

        private byte[] Decoder(Dico dico, byte[] flux)
        {
            string[] filtres = Resoudre(dico["Filter"]) switch
            {
                null => [],
                Nom f => [f.Valeur],
                Tableau t => t.Elements.Select(e => (Resoudre(e) as Nom)?.Valeur ?? "?").ToArray(),
                _ => new[] { "?" },
            };
            var parametres = Resoudre(dico["DecodeParms"]) switch { Dico d => d, Tableau t when t.Elements.Count > 0 => Resoudre(t.Elements[0]) as Dico, _ => null };
            var donnees = flux;
            foreach (var filtre in filtres)
            {
                if (filtre != "FlateDecode") throw new PdfIllisible($"filtre /{filtre} non lu par RIG v3");
                donnees = Inflate(donnees);
            }
            var predicteur = parametres is null ? 1 : (int)((Resoudre(parametres["Predictor"]) as Nombre)?.Entier ?? 1);
            if (predicteur <= 1) return donnees;
            if (predicteur < 10) throw new PdfIllisible($"prédicteur {predicteur} non lu par RIG v3");
            var colonnes = (int)((Resoudre(parametres!["Columns"]) as Nombre)?.Entier ?? 1);
            var couleurs = (int)((Resoudre(parametres["Colors"]) as Nombre)?.Entier ?? 1);
            var bits = (int)((Resoudre(parametres["BitsPerComponent"]) as Nombre)?.Entier ?? 8);
            return Png(donnees, (colonnes * couleurs * bits + 7) / 8, Math.Max(1, (couleurs * bits + 7) / 8));
        }

        private static byte[] Inflate(byte[] donnees)
        {
            try
            {
                using var z = new ZLibStream(new MemoryStream(donnees), CompressionMode.Decompress);
                var sortie = new MemoryStream(); z.CopyTo(sortie); return sortie.ToArray();
            }
            catch (InvalidDataException)
            {
                try
                {
                    using var d = new DeflateStream(new MemoryStream(donnees), CompressionMode.Decompress);
                    var sortie = new MemoryStream(); d.CopyTo(sortie); return sortie.ToArray();
                }
                catch (InvalidDataException) { throw new PdfIllisible("flux compressé (FlateDecode) illisible"); }
            }
        }

        /// <summary>Les predicteurs PNG (RFC 2083, 6) : chaque rangee commence par son type de filtre.</summary>
        private static byte[] Png(byte[] donnees, int rangee, int octetsParPixel)
        {
            var sortie = new MemoryStream();
            var precedente = new byte[rangee];
            for (var pos = 0; pos + 1 + rangee <= donnees.Length; pos += 1 + rangee)
            {
                var type = donnees[pos];
                var courante = donnees.AsSpan(pos + 1, rangee).ToArray();
                for (var i = 0; i < rangee; i++)
                {
                    int gauche = i >= octetsParPixel ? courante[i - octetsParPixel] : 0, haut = precedente[i];
                    int coin = i >= octetsParPixel ? precedente[i - octetsParPixel] : 0;
                    courante[i] = (byte)(courante[i] + type switch
                    {
                        0 => 0, 1 => gauche, 2 => haut, 3 => (gauche + haut) / 2,
                        4 => Paeth(gauche, haut, coin),
                        _ => throw new PdfIllisible($"prédicteur PNG de type {type} inconnu"),
                    });
                }
                sortie.Write(courante);
                precedente = courante;
            }
            return sortie.ToArray();
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }

    private static bool Commence(byte[] source, int position, string mot) => Commence(source, position, Encoding.ASCII.GetBytes(mot));

    private static bool Commence(byte[] source, int position, byte[] mot)
        => position >= 0 && position + mot.Length <= source.Length && source.AsSpan(position, mot.Length).SequenceEqual(mot);

    /// <summary>Le lexique PDF (7.2, 7.3) sur un tampon : blancs, commentaires, et une valeur a la fois avec sa place.</summary>
    private sealed class Lecteur(byte[] source, int position)
    {
        internal int Position { get; set; } = position;

        private static bool Blanc(byte c) => c is 0 or 9 or 10 or 12 or 13 or 32;
        private static bool Delimiteur(byte c) => c is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
                                                         or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

        internal void Blancs()
        {
            while (Position < source.Length)
            {
                if (Blanc(source[Position])) Position++;
                else if (source[Position] == '%') while (Position < source.Length && source[Position] is not ((byte)'\n' or (byte)'\r')) Position++;
                else break;
            }
        }

        /// <summary>Le mot-cle <paramref name="mot"/> a la suite (« obj », « stream ») : consomme s'il y est.</summary>
        internal bool MotCle(string mot)
        {
            Blancs();
            if (!Commence(source, Position, mot)) return false;
            var fin = Position + mot.Length;
            if (fin < source.Length && !Blanc(source[fin]) && !Delimiteur(source[fin])) return false;
            Position = fin;
            return true;
        }

        internal Obj? Valeur()
        {
            Blancs();
            if (Position >= source.Length) return null;
            var debut = Position;
            var c = source[Position];
            if (c == '<' && Position + 1 < source.Length && source[Position + 1] == '<')
            {
                Position += 2;
                var entrees = new Dictionary<string, Obj>();
                while (true)
                {
                    Blancs();
                    if (Position + 1 >= source.Length) throw new PdfIllisible("dictionnaire non fermé");
                    if (source[Position] == '>' && source[Position + 1] == '>') { Position += 2; break; }
                    if (Valeur() is not Nom cle) throw new PdfIllisible("clé de dictionnaire illisible");
                    entrees[cle.Valeur] = Valeur() ?? throw new PdfIllisible($"valeur de /{cle.Valeur} absente");
                }
                return new Dico(entrees, debut, Position);
            }
            if (c == '<')
            {
                var fin = Array.IndexOf(source, (byte)'>', Position);
                if (fin < 0) throw new PdfIllisible("chaîne hexadécimale non fermée");
                Position = fin + 1;
                return new Autre(debut, Position);
            }
            if (c == '(')
            {
                var profondeur = 0;
                for (; Position < source.Length; Position++)
                {
                    if (source[Position] == '\\') { Position++; continue; }
                    if (source[Position] == '(') profondeur++;
                    else if (source[Position] == ')' && --profondeur == 0) { Position++; return new Autre(debut, Position); }
                }
                throw new PdfIllisible("chaîne non fermée");
            }
            if (c == '[')
            {
                Position++;
                var elements = new List<Obj>();
                while (true)
                {
                    Blancs();
                    if (Position >= source.Length) throw new PdfIllisible("tableau non fermé");
                    if (source[Position] == ']') { Position++; break; }
                    elements.Add(Valeur() ?? throw new PdfIllisible("tableau illisible"));
                }
                return new Tableau(elements, debut, Position);
            }
            if (c == '/')
            {
                Position++;
                while (Position < source.Length && !Blanc(source[Position]) && !Delimiteur(source[Position])) Position++;
                return new Nom(Encoding.Latin1.GetString(source, debut + 1, Position - debut - 1), debut, Position);
            }
            if (c is (byte)'+' or (byte)'-' or (byte)'.' || c is >= (byte)'0' and <= (byte)'9')
            {
                Position++;
                while (Position < source.Length && source[Position] is (byte)'.' or >= (byte)'0' and <= (byte)'9') Position++;
                var nombre = new Nombre(Encoding.ASCII.GetString(source, debut, Position - debut), debut, Position);
                // « n g R » : une reference (deux entiers puis R), sinon on revient apres le premier nombre.
                var apres = Position;
                if (Entier(nombre.Texte))
                {
                    Blancs();
                    var debutGen = Position;
                    while (Position < source.Length && source[Position] is >= (byte)'0' and <= (byte)'9') Position++;
                    var gen = Encoding.ASCII.GetString(source, debutGen, Position - debutGen);
                    if (gen.Length is > 0 and <= 9 && MotCle("R"))
                        return new Reference((int)nombre.Entier, int.Parse(gen, CultureInfo.InvariantCulture), debut, Position);
                }
                Position = apres;
                return nombre;
            }
            if (Delimiteur(c)) throw new PdfIllisible($"caractère « {(char)c} » inattendu");
            while (Position < source.Length && !Blanc(source[Position]) && !Delimiteur(source[Position])) Position++;
            var mot = Encoding.ASCII.GetString(source, debut, Position - debut);
            if (mot is "true" or "false" or "null") return new Autre(debut, Position);
            Position = debut;   // un mot-cle (obj, stream, endobj, R...) n'est pas une valeur
            return null;
        }

        private static bool Entier(string texte) => texte.Length > 0 && texte.All(ch => ch is >= '0' and <= '9');
    }
}
