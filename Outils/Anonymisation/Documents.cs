using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace RigV3.Outils.Anonymisation;

/// <summary>Le format d'un document, reconnu a ses premiers octets PAR LE SERVEUR SOURCE (<see cref="DocumentsFictifs.ExpressionSignature"/>).</summary>
public enum FormatDocument { Pdf, PostScript, Tiff, Png, Jpeg, Gif, Bmp, Zip, Ole2, Rtf, Gzip, Utf16, Xml, Json, Texte, Autre }

/// <summary>
/// Ce que la source dit d'un document, et rien d'autre : son format, une marque d'octets UTF-8 en tete (<paramref name="Bom"/>),
/// sa taille en octets. Etiquette ecrite par le serveur : « Pdf:105131 », « Json+bom:5897 ».
/// </summary>
public readonly partial record struct SignatureDocument(FormatDocument Format, bool Bom, long Taille)
{
    public string Etiquette => $"{Format}{(Bom ? "+bom" : "")}:{Taille.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Lit une etiquette rendue par <see cref="DocumentsFictifs.ExpressionSignature"/> ; faux pour tout autre texte
    /// (le controle compte alors « AUTRE » : rien d'autre qu'une etiquette ne doit sortir de la source).</summary>
    public static bool TryLire(string? etiquette, out SignatureDocument s)
    {
        s = default;
        if (etiquette is null || Forme().Match(etiquette) is not { Success: true } m) return false;
        if (!Enum.TryParse<FormatDocument>(m.Groups[1].Value, out var f) || !long.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            return false;
        s = new SignatureDocument(f, m.Groups[2].Success, n);
        return true;
    }

    [GeneratedRegex(@"^([A-Za-z0-9]+)(\+bom)?:([0-9]{1,12})$")]
    private static partial Regex Forme();
}

/// <summary>
/// LES DOCUMENTS FICTIFS (SOC-021, lot 18 : regle des binaires decidee par le chef).
///
/// Une colonne binaire qui porte un document (GED, Kbis, flux EDI, pieces numerisees) ne passe jamais dans la copie :
///  1. la source ne rend que la SIGNATURE du document -- son format, reconnu a ses premiers octets PAR LE SERVEUR, et sa
///     taille (<see cref="ExpressionSignature"/>) ; aucun octet du document ne sort de la source, meme en memoire ;
///  2. la copie recoit a sa place un document FICTIF du meme format, complete a la MEME taille (<see cref="Fabriquer"/>) :
///     PDF lisible « Document anonymisé — copie de travail », PostScript qui l'imprime, image blanche (TIFF, PNG, JPEG, GIF,
///     BMP), archive ZIP qui le contient, XML, JSON ou texte qui le disent ; sinon des octets neutres derriere la
///     signature du format (OLE2, GZIP), ou des zeros (format inconnu). NULL reste NULL, un document vide reste vide.
///
/// Le document fictif ne depend que de la signature : deux documents de meme format et de meme taille recoivent le
/// meme (un lien par un binaire tient). Il ne contient rien de la source.
/// fix-ok: lot 18 (2026-11-22) -- GED_RIG.GDRG_FICHIER passait en clair dans la copie (CatalogueRegles.Pour ne traitait que
/// textes et XML) ; mesure RIG_DEV : 131 673 documents (ZIP 119 645, XML 11 156, PDF 869), 12 colonnes binaires.
/// </summary>
public static class DocumentsFictifs
{
    public const string TexteFictif = "Document anonymisé — copie de travail";
    /// <summary>Le meme en ASCII seul, pour les formats qui n'ont pas l'accent ou le tiret long (PostScript, PNG).</summary>
    public const string TexteFictifAscii = "Document anonymise - copie de travail";

    /// <summary>
    /// L'expression SQL de la signature d'une colonne (deja entre crochets) : NULL si elle est NULL, sinon
    /// « Format[+bom]:taille ». La MEME expression sert a la copie (lecture de la source), au controle (des deux cotes) et
    /// au banc : un format se reconnait partout de la meme facon. Chaque format a sa signature d'au moins un octet ; une
    /// valeur vide (0 octet) est « Autre:0 ».
    /// </summary>
    public static string ExpressionSignature(string colonne)
    {
        string S(int n) => $"SUBSTRING({colonne}, 1, {n})";
        return $"CASE WHEN {colonne} IS NULL THEN NULL ELSE CONCAT(CASE " +
               $"WHEN {S(4)} = 0x25504446 THEN 'Pdf' " +
               $"WHEN {S(2)} = 0x2521 OR {S(4)} = 0xC5D0D3C6 THEN 'PostScript' " +
               $"WHEN {S(4)} IN (0x49492A00, 0x4D4D002A) THEN 'Tiff' " +
               $"WHEN {S(8)} = 0x89504E470D0A1A0A THEN 'Png' " +
               $"WHEN {S(3)} = 0xFFD8FF THEN 'Jpeg' " +
               $"WHEN {S(6)} IN (0x474946383761, 0x474946383961) THEN 'Gif' " +
               $"WHEN {S(2)} = 0x424D THEN 'Bmp' " +
               $"WHEN {S(4)} IN (0x504B0304, 0x504B0506) THEN 'Zip' " +
               $"WHEN {S(8)} = 0xD0CF11E0A1B11AE1 THEN 'Ole2' " +
               $"WHEN {S(5)} = 0x7B5C727466 THEN 'Rtf' " +
               $"WHEN {S(2)} = 0x1F8B THEN 'Gzip' " +
               $"WHEN {S(2)} IN (0xFFFE, 0xFEFF) THEN 'Utf16' " +
               $"WHEN {S(1)} = 0x3C OR {S(4)} = 0xEFBBBF3C THEN 'Xml' " +
               $"WHEN {S(1)} IN (0x7B, 0x5B) OR {S(4)} IN (0xEFBBBF7B, 0xEFBBBF5B) THEN 'Json' " +
               $"WHEN {S(1)} BETWEEN 0x20 AND 0x7E OR {S(1)} IN (0x09, 0x0A, 0x0D) OR {S(3)} = 0xEFBBBF THEN 'Texte' " +
               "ELSE 'Autre' END, " +
               $"CASE WHEN {S(3)} = 0xEFBBBF THEN '+bom' ELSE '' END, ':', DATALENGTH({colonne})) END";
    }

    /// <summary>Les formats dont la signature fait au moins trois octets : une valeur courte qui la porte est un document,
    /// pas une empreinte ou une cle tiree au hasard (ControleCopie, binaires gardes).</summary>
    public static readonly IReadOnlySet<FormatDocument> SignaturesFortes = new HashSet<FormatDocument>
    {
        FormatDocument.Pdf, FormatDocument.Tiff, FormatDocument.Png, FormatDocument.Jpeg, FormatDocument.Gif,
        FormatDocument.Zip, FormatDocument.Ole2, FormatDocument.Rtf,
    };

    /// <summary>Le document fictif d'une signature : meme format (meme signature de tete), meme taille, aucun octet de la source.</summary>
    public static byte[] Fabriquer(SignatureDocument s)
    {
        if (s.Taille <= 0) return [];
        if (s.Taille > Array.MaxLength) throw new InvalidOperationException($"Document de {s.Taille} octets : au-dela de ce qu'un tableau .NET contient.");
        var t = (int)s.Taille;
        return s.Format switch
        {
            FormatDocument.Pdf => Pdf(t),
            FormatDocument.PostScript => PostScript(t),
            FormatDocument.Tiff => Rempli(Tiff(), t, "II*\0"u8),
            FormatDocument.Png => Png(t),
            FormatDocument.Jpeg => Rempli(Jpeg(), t, [0xFF, 0xD8, 0xFF]),
            FormatDocument.Gif => Rempli(Gif(), t, "GIF89a"u8),
            FormatDocument.Bmp => Bmp(t),
            FormatDocument.Zip => Zip(t),
            FormatDocument.Ole2 => Rempli([], t, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
            FormatDocument.Gzip => Rempli([], t, [0x1F, 0x8B]),
            FormatDocument.Rtf => Texte(t, s.Bom, @"{\rtf1\ansi\deff0{\fonttbl{\f0 Helvetica;}}\f0 Document anonymis\'e9 \emdash  copie de travail\par}"u8, "{\\rtf"u8),
            FormatDocument.Utf16 => Utf16(t),
            FormatDocument.Xml => Texte(t, s.Bom, Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<document-anonymise>{TexteFictif}</document-anonymise>\n"), "<"u8),
            FormatDocument.Json => Texte(t, s.Bom, Encoding.UTF8.GetBytes($"{{\"document\":\"{TexteFictif}\"}}\n"), "{"u8),
            FormatDocument.Texte => Texte(t, s.Bom, Encoding.UTF8.GetBytes(TexteFictif + "\r\n"), "D"u8),
            _ => new byte[t],
        };
    }

    // ------------------------------------------------------------------ remplissage

    /// <summary>Le coeur suivi de zeros s'il tient ; sinon la seule signature du format suivie de zeros.</summary>
    private static byte[] Rempli(ReadOnlySpan<byte> coeur, int taille, ReadOnlySpan<byte> signature)
    {
        var b = new byte[taille];
        (coeur.Length > 0 && coeur.Length <= taille ? coeur : signature[..Math.Min(signature.Length, taille)]).CopyTo(b);
        return b;
    }

    /// <summary>Un texte (XML, JSON, RTF, texte) : marque UTF-8 si la source en avait une, le contenu, puis des espaces.
    /// Trop court pour le contenu : son debut (<paramref name="minimal"/>), qui garde la signature, puis des espaces.</summary>
    private static byte[] Texte(int taille, bool bom, ReadOnlySpan<byte> contenu, ReadOnlySpan<byte> minimal)
    {
        var b = new byte[taille];
        b.AsSpan().Fill((byte)' ');
        var i = 0;
        if (bom) { "﻿"u8[..Math.Min(3, taille)].CopyTo(b); i = Math.Min(3, taille); }
        var reste = taille - i;
        (contenu.Length > 0 && contenu.Length <= reste ? contenu : minimal[..Math.Min(minimal.Length, reste)]).CopyTo(b.AsSpan(i));
        return b;
    }

    private static byte[] Utf16(int taille)
    {
        var b = new byte[taille];
        for (var i = 0; i + 1 < taille; i += 2) { b[i] = (byte)' '; b[i + 1] = 0; }
        if (taille % 2 == 1) b[^1] = (byte)' ';
        var contenu = Encoding.Unicode.GetBytes("﻿" + TexteFictif + "\r\n");
        contenu.AsSpan(0, Math.Min(contenu.Length, taille & ~1)).CopyTo(b);
        if (taille >= 2) { b[0] = 0xFF; b[1] = 0xFE; }
        return b;
    }

    /// <summary>Des lignes de commentaire (« % » puis des espaces) : n'importe quelle longueur, 0 comprise.</summary>
    private static void Commentaires(Span<byte> zone)
    {
        zone.Fill((byte)' ');
        for (var debut = 0; debut < zone.Length; debut += 72)
        {
            var fin = Math.Min(debut + 72, zone.Length) - 1;
            zone[fin] = (byte)'\n';
            if (fin > debut) zone[debut] = (byte)'%';
        }
    }

    // ------------------------------------------------------------------ PDF et PostScript

    /// <summary>
    /// Un PDF d'une page A4 qui ecrit <see cref="TexteFictif"/> (Helvetica, WinAnsi : « é » = \351, « — » = \227). Le
    /// remplissage est fait de lignes de commentaire entre l'en-tete et les objets ; les positions de la table xref en
    /// tiennent compte, et « startxref » s'ecrit sur dix chiffres : la longueur ne depend pas du remplissage.
    /// </summary>
    private static byte[] Pdf(int taille)
    {
        var tete = Latin1("%PDF-1.4\n%âãÏÓ\n");
        var flux = Latin1("BT /F1 18 Tf 72 760 Td (Document anonymis\\351 \\227 copie de travail) Tj ET\n");
        string[] objets =
        [
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n",
            "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n",
            $"5 0 obj\n<< /Length {flux.Length - 1} >>\nstream\n" + Encoding.Latin1.GetString(flux) + "endstream\nendobj\n",
        ];
        byte[] Corps(int remplissage)
        {
            var sb = new StringBuilder();
            var positions = new List<int>();
            var pos = tete.Length + remplissage;
            foreach (var o in objets) { positions.Add(pos); sb.Append(o); pos += Encoding.Latin1.GetByteCount(o); }
            var xref = pos;
            sb.Append("xref\n0 6\n0000000000 65535 f \n");
            foreach (var p in positions) sb.Append(p.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            sb.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n").Append(xref.ToString("D10", CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
            return Latin1(sb.ToString());
        }
        var longueur = tete.Length + Corps(0).Length;
        if (taille < longueur) return Texte(taille, false, [], "%PDF-1.4\n%"u8);
        var b = new byte[taille];
        tete.CopyTo(b, 0);
        Commentaires(b.AsSpan(tete.Length, taille - longueur));
        Corps(taille - longueur).CopyTo(b, tete.Length + taille - longueur);
        return b;
    }

    /// <summary>Un PostScript d'une page qui imprime le texte (Helvetica recodee ISO Latin 1 : « é » ; pas de tiret long),
    /// complete par des lignes de commentaire avant « %%EOF ».</summary>
    private static byte[] PostScript(int taille)
    {
        var tete = Latin1(
            "%!PS-Adobe-3.0\n%%Title: Document anonymise\n%%Pages: 1\n%%EndComments\n" +
            "/Helvetica findfont dup length dict begin {1 index /FID ne {def} {pop pop} ifelse} forall " +
            "/Encoding ISOLatin1Encoding def currentdict end /Helvetica-Latin1 exch definefont pop\n" +
            "/Helvetica-Latin1 findfont 18 scalefont setfont 72 760 moveto (Document anonymis\\351 - copie de travail) show\nshowpage\n");
        var fin = "%%EOF\n"u8;
        if (taille < tete.Length + fin.Length) return Texte(taille, false, [], "%!PS"u8);
        var b = new byte[taille];
        tete.CopyTo(b, 0);
        Commentaires(b.AsSpan(tete.Length, taille - tete.Length - fin.Length));
        fin.CopyTo(b.AsSpan(taille - fin.Length));
        return b;
    }

    private static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);

    // ------------------------------------------------------------------ images blanches d'un point

    /// <summary>TIFF petit-boutiste, 1 x 1, niveaux de gris 8 bits, point blanc ; 123 octets.</summary>
    private static byte[] Tiff()
    {
        var b = new byte[8 + 2 + 9 * 12 + 4 + 1];
        "II*\0"u8.CopyTo(b);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8), 9);
        (ushort Tag, ushort Type, uint Valeur)[] champs =
            [(256, 3, 1), (257, 3, 1), (258, 3, 8), (259, 3, 1), (262, 3, 1), (273, 4, (uint)b.Length - 1), (277, 3, 1), (278, 3, 1), (279, 4, 1)];
        for (var i = 0; i < champs.Length; i++)
        {
            var e = b.AsSpan(10 + i * 12);
            BinaryPrimitives.WriteUInt16LittleEndian(e, champs[i].Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(e[2..], champs[i].Type);
            BinaryPrimitives.WriteUInt32LittleEndian(e[4..], 1);
            if (champs[i].Type == 3) BinaryPrimitives.WriteUInt16LittleEndian(e[8..], (ushort)champs[i].Valeur);
            else BinaryPrimitives.WriteUInt32LittleEndian(e[8..], champs[i].Valeur);
        }
        b[^1] = 0xFF;
        return b;
    }

    /// <summary>PNG 1 x 1 gris blanc ; le remplissage est le texte d'un morceau « tEXt » (Comment), avant IEND.</summary>
    private static byte[] Png(int taille)
    {
        using var zlib = new MemoryStream();
        using (var z = new ZLibStream(zlib, CompressionLevel.Optimal, leaveOpen: true)) z.Write([0x00, 0xFF]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, 1);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), 1);
        ihdr[8] = 8;
        var debut = new List<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        debut.AddRange(Morceau("IHDR", ihdr));
        debut.AddRange(Morceau("IDAT", zlib.ToArray()));
        var iend = Morceau("IEND", []);
        var cle = Latin1("Comment\0" + TexteFictifAscii);
        var sansTexte = debut.Count + iend.Length;
        if (taille < sansTexte + 12 + cle.Length) return Rempli([.. debut, .. iend], taille, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var texte = new byte[taille - sansTexte - 12];
        texte.AsSpan().Fill((byte)' ');
        cle.CopyTo(texte, 0);
        return [.. debut, .. Morceau("tEXt", texte), .. iend];
    }

    private static byte[] Morceau(string type, byte[] donnees)
    {
        var b = new byte[12 + donnees.Length];
        BinaryPrimitives.WriteUInt32BigEndian(b, (uint)donnees.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(b, 4);
        donnees.CopyTo(b, 8);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8 + donnees.Length), Crc32(b.AsSpan(4, 4 + donnees.Length)));
        return b;
    }

    /// <summary>
    /// JPEG de base 8 x 8 gris blanc, ecrit a la main : quantification a 1, un seul code de Huffman par table -- DC de
    /// categorie 10 (coefficient 1016 = 8 x 127, le blanc decale de -128), AC fin de bloc. Donnees : 0 1111111000 0, puis
    /// des 1 jusqu'a l'octet (0x7F 0x0F).
    /// </summary>
    private static byte[] Jpeg()
    {
        var b = new List<byte> { 0xFF, 0xD8 };
        b.AddRange([0xFF, 0xDB, 0x00, 0x43, 0x00]);
        b.AddRange(Enumerable.Repeat((byte)1, 64));
        b.AddRange([0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x08, 0x00, 0x08, 0x01, 0x01, 0x11, 0x00]);
        b.AddRange([0xFF, 0xC4, 0x00, 0x14, 0x00, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x0A]);
        b.AddRange([0xFF, 0xC4, 0x00, 0x14, 0x10, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x00]);
        b.AddRange([0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]);
        b.AddRange([0x7F, 0x0F]);
        b.AddRange([0xFF, 0xD9]);
        return [.. b];
    }

    /// <summary>GIF89a 1 x 1, palette blanc et noir, point blanc ; 35 octets.</summary>
    private static byte[] Gif() =>
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00, 0x3B,
    ];

    /// <summary>BMP 1 x 1, 24 bits, point blanc ; la taille du fichier (en tete) est la taille demandee.</summary>
    private static byte[] Bmp(int taille)
    {
        if (taille < 58) return Rempli([], taille, "BM"u8);
        var b = new byte[taille];
        "BM"u8.CopyTo(b);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(2), (uint)taille);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(10), 54);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(18), 1);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(28), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(34), 4);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(38), 2835);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(42), 2835);
        b[54] = b[55] = b[56] = 0xFF;
        return b;
    }

    // ------------------------------------------------------------------ ZIP

    private const string NomDansZip = "document-anonyme.txt";

    /// <summary>
    /// Une archive ZIP d'un fichier texte non compresse (« document-anonyme.txt » : le texte, puis des espaces jusqu'a la
    /// taille). Trop petite pour un fichier : une archive vide dont le commentaire remplit ; plus petite encore : la
    /// signature puis des zeros.
    /// </summary>
    private static byte[] Zip(int taille)
    {
        var nom = Encoding.ASCII.GetBytes(NomDansZip);
        var cadre = 30 + nom.Length + 46 + nom.Length + 22;
        if (taille < cadre)
        {
            if (taille < 22 || taille - 22 > ushort.MaxValue) return Rempli([], taille, [0x50, 0x4B, 0x03, 0x04]);
            var vide = new byte[taille];
            vide.AsSpan(22).Fill((byte)' ');
            BinaryPrimitives.WriteUInt32LittleEndian(vide, 0x06054B50);
            BinaryPrimitives.WriteUInt16LittleEndian(vide.AsSpan(20), (ushort)(taille - 22));
            return vide;
        }
        var b = new byte[taille];
        var n = taille - cadre;
        var contenu = b.AsSpan(30 + nom.Length, n);
        contenu.Fill((byte)' ');
        var texte = Encoding.UTF8.GetBytes(TexteFictif + "\r\n");
        texte.AsSpan(0, Math.Min(texte.Length, n)).CopyTo(contenu);
        var crc = Crc32(contenu);

        // Commun a l'en-tete local (apres sa signature) et a l'entree centrale (apres « version de l'auteur ») : version
        // requise @0, drapeaux @2, methode @4 (0 : stocke), heure @6, date @8 (1980-01-01), crc @10, tailles @14 et @18, nom @22.
        void Commun(Span<byte> e)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(e, 20);
            BinaryPrimitives.WriteUInt16LittleEndian(e[8..], 0x21);
            BinaryPrimitives.WriteUInt32LittleEndian(e[10..], crc);
            BinaryPrimitives.WriteUInt32LittleEndian(e[14..], (uint)n);
            BinaryPrimitives.WriteUInt32LittleEndian(e[18..], (uint)n);
            BinaryPrimitives.WriteUInt16LittleEndian(e[22..], (ushort)nom.Length);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(b, 0x04034B50);
        Commun(b.AsSpan(4));
        nom.CopyTo(b, 30);
        var central = 30 + nom.Length + n;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(central), 0x02014B50);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(central + 4), 20);
        Commun(b.AsSpan(central + 6));
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(central + 36), 1);   // fichier texte
        nom.CopyTo(b, central + 46);
        var fin = central + 46 + nom.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(fin), 0x06054B50);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(fin + 8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(fin + 10), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(fin + 12), (uint)(46 + nom.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(fin + 16), (uint)central);
        return b;
    }

    // ------------------------------------------------------------------ CRC-32 (ZIP, PNG)

    private static readonly uint[] TableCrc = [.. Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    })];

    public static uint Crc32(ReadOnlySpan<byte> donnees)
    {
        var c = 0xFFFFFFFFu;
        foreach (var o in donnees) c = TableCrc[(c ^ o) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
