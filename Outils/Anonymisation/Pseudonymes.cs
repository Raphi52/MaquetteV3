using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RigV3.Outils.Anonymisation;

// ============================================================
// LES VALEURS FICTIVES DE LA COPIE ANONYMISEE (SOC-021 CA01, CA02).
//
// Chaque valeur personnelle est remplacee MOT PAR MOT (un mot = une suite de lettres
// et de chiffres) par une PERMUTATION A CLE :
//  - meme mot => meme faux mot, dans toutes les tables et toutes les colonnes : un client,
//    la partie, la facture et le journal qui portent « DUPONT » portent tous le meme
//    faux nom, les jointures et les recherches restent justes (CA02) ;
//  - deux mots differents => deux faux mots differents (bijection) : deux personnes ne
//    fusionnent pas, un index unique (UTILISATEUR.UTLST_LOGIN) tient toujours ;
//  - aucun mot ne reste lui-meme (derangement : un seul cycle) ;
//  - meme longueur, meme forme : une majuscule reste une majuscule, une minuscule une
//    minuscule, une lettre accentuee une lettre accentuee, un chiffre un chiffre ; espaces,
//    tirets, apostrophes, @ et points restent a leur place (CA01) ;
//  - insensible a la casse comme la base (SQL_Latin1_General_CP1_CI_AS) : « Dupont » et
//    « DUPONT » donnent les memes lettres ; sensible aux accents, comme elle.
//
// La permutation est un reseau de Feistel a 10 tours (la construction de FF1, NIST SP 800-38G)
// sur la valeur du mot en base mixte (26 lettres, 34 lettres accentuees de CP1252, 10 chiffres),
// avec HMAC-SHA256 comme fonction de tour et une marche de cycle pour rester dans le domaine.
// Ce n'est pas un chiffrement certifie : c'est une pseudonymisation (RGPD art. 4.5) dont la
// cle n'est jamais ecrite. Cle ALEATOIRE par defaut, effacee de la memoire en fin de copie :
// c'est elle, la « table de correspondance », et elle est detruite (SOC-021 T01).
// Une GRAINE fixe rend la copie reproductible -- et donc reversible par qui la connait :
// l'outil la refuse sur une source qui n'est pas sur ce poste (voir Copie.cs).
// ============================================================

public sealed class Pseudonymes : IDisposable
{
    private const string Lettres = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    /// <summary>Les majuscules accentuees de CP1252 (la page des colonnes varchar de la V2) dont la
    /// minuscule est aussi dans CP1252 : une valeur fictive ne devient jamais « ? » en varchar.</summary>
    private const string Accentuees = "ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖØÙÚÛÜÝÞŒŠŽŸ";
    private const string Chiffres = "0123456789";
    private const int Tours = 10;
    /// <summary>34^12 &lt; 2^63 : un morceau de 12 caracteres tient dans un entier 64 bits.</summary>
    private const int MorceauMax = 12;

    private readonly byte[] _cle;
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);

    /// <summary>Vrai si la copie se refait a l'identique avec la meme graine (banc seulement).</summary>
    public bool Reproductible { get; }

    private Pseudonymes(byte[] cle, bool reproductible)
    {
        _cle = cle;
        Reproductible = reproductible;
    }

    /// <summary>Cle aleatoire, jamais ecrite : la copie n'est pas reversible une fois l'outil ferme.</summary>
    public static Pseudonymes Aleatoire() => new(RandomNumberGenerator.GetBytes(32), false);

    /// <summary>Cle tiree d'une graine : copie reproductible (et reversible par qui a la graine).</summary>
    public static Pseudonymes DepuisGraine(string graine)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes("RIGV3-SOC-021|" + graine)), true);

    /// <summary>Efface la cle de la memoire : plus personne ne peut refaire la correspondance.</summary>
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_cle);
        _cache.Clear();
    }

    // ------------------------------------------------------------------ les regles de valeur

    /// <summary>Texte : chaque mot remplace, le reste (espaces, ponctuation) garde.</summary>
    public string Texte(string texte) => RemplacerMots(texte, null);

    /// <summary>
    /// Courriel(s) : comme <see cref="Texte"/>, mais l'extension du domaine (« fr », « com ») est
    /// gardee pour que l'adresse reste une adresse ; marche aussi sur une liste « a@b.fr; c@d.fr ».
    /// </summary>
    public string Courriel(string texte)
    {
        bool[]? protege = null;
        foreach (Match m in Extension().Matches(texte))
        {
            protege ??= new bool[texte.Length];
            var g = m.Groups["ext"];
            for (var i = g.Index; i < g.Index + g.Length; i++) protege[i] = true;
        }
        return RemplacerMots(texte, protege);
    }

    /// <summary>
    /// Telephone : les chiffres sont permutes ENSEMBLE (« 06 12 34 56 78 » et « 0612345678 » donnent
    /// les memes chiffres), les deux premiers gardes des qu'il y en a six (fixe, portable, indicatif) ;
    /// separateurs a leur place ; un mot (« poste ») remplace comme un texte.
    /// </summary>
    public string Telephone(string texte)
    {
        var positions = new List<int>();
        for (var i = 0; i < texte.Length; i++)
            if (texte[i] is >= '0' and <= '9') positions.Add(i);
        if (positions.Count == 0) return Texte(texte);
        var garder = positions.Count >= 6 ? 2 : 0;
        var chiffres = new string([.. positions.Skip(garder).Select(p => texte[p])]);
        var faux = Permuter("tel", chiffres);
        var sortie = texte.ToCharArray();
        for (var k = 0; k < faux.Length; k++) sortie[positions[garder + k]] = faux[k];
        // Les lettres eventuelles (« poste 12 ») : mot par mot, les chiffres deja traites restent.
        var sb = new StringBuilder(sortie.Length);
        for (var i = 0; i < sortie.Length;)
        {
            if (!char.IsLetter(sortie[i])) { sb.Append(sortie[i++]); continue; }
            var j = i;
            while (j < sortie.Length && char.IsLetter(sortie[j])) j++;
            sb.Append(Mot(new string(sortie, i, j - i)));
            i = j;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Date de naissance : un autre jour de la MEME annee (l'age reste vraisemblable), heure gardee ;
    /// meme date => meme fausse date, jamais la date d'origine.
    /// </summary>
    public DateTime Date(DateTime d)
    {
        var jours = DateTime.IsLeapYear(d.Year) ? 366UL : 365UL;
        var image = Deranger("date|" + d.Year.ToString(CultureInfo.InvariantCulture), (ulong)(d.DayOfYear - 1), jours);
        return new DateTime(d.Year, 1, 1, 0, 0, 0, d.Kind).AddDays(image).Add(d.TimeOfDay);
    }

    /// <summary>Date ecrite en texte (attribut XML « 1975-03-12T00:00:00 ») : le jour change, le reste
    /// du texte est garde ; un texte qui n'est pas une date est traite comme un texte.</summary>
    public string DateTexte(string texte)
    {
        if (texte.Length >= 10 && DateTime.TryParseExact(texte[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d))
            return Date(d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + texte[10..];
        return Texte(texte);
    }

    // ------------------------------------------------------------------ le mot

    /// <summary>Un mot (lettres et chiffres) : son image par la permutation, casse d'origine reappliquee.</summary>
    public string Mot(string mot)
    {
        var majuscules = new char[mot.Length];
        for (var i = 0; i < mot.Length; i++) majuscules[i] = char.ToUpperInvariant(mot[i]);
        var faux = Permuter("mot", new string(majuscules));
        var sortie = new char[mot.Length];
        for (var i = 0; i < mot.Length; i++)
            sortie[i] = AlphabetDe(majuscules[i]) is null ? mot[i]          // caractere hors alphabet : garde tel quel
                      : char.IsLower(mot[i]) ? char.ToLowerInvariant(faux[i])
                      : faux[i];
        return new string(sortie);
    }

    private string RemplacerMots(string texte, bool[]? protege)
    {
        var sb = new StringBuilder(texte.Length);
        for (var i = 0; i < texte.Length;)
        {
            if (!char.IsLetterOrDigit(texte[i]) || (protege?[i] ?? false)) { sb.Append(texte[i++]); continue; }
            var j = i;
            while (j < texte.Length && char.IsLetterOrDigit(texte[j]) && !(protege?[j] ?? false)) j++;
            sb.Append(Mot(texte.Substring(i, j - i)));
            i = j;
        }
        return sb.ToString();
    }

    private static string? AlphabetDe(char majuscule) =>
        majuscule is >= 'A' and <= 'Z' ? Lettres
        : majuscule is >= '0' and <= '9' ? Chiffres
        : Accentuees.Contains(majuscule) ? Accentuees
        : null;

    /// <summary>
    /// La permutation d'une suite de caracteres en majuscules : chaque position garde son alphabet,
    /// les positions hors alphabet sont laissees ; par morceaux de 12 positions (chacun permute avec
    /// son rang dans la cle de tour : le tout reste une bijection).
    /// </summary>
    private string Permuter(string domaine, string majuscules)
    {
        var cleCache = domaine + "\u0001" + majuscules;
        if (_cache.TryGetValue(cleCache, out var connu)) return connu;

        var sortie = majuscules.ToCharArray();
        var positions = new List<int>(majuscules.Length);
        for (var i = 0; i < majuscules.Length; i++)
            if (AlphabetDe(majuscules[i]) is not null) positions.Add(i);

        for (int debut = 0, morceau = 0; debut < positions.Count; debut += MorceauMax, morceau++)
        {
            var n = Math.Min(MorceauMax, positions.Count - debut);
            ulong valeur = 0, taille = 1;
            var motif = new StringBuilder(domaine).Append('|').Append(morceau).Append('|');
            for (var k = 0; k < n; k++)
            {
                var a = AlphabetDe(majuscules[positions[debut + k]])!;
                valeur = valeur * (ulong)a.Length + (ulong)a.IndexOf(majuscules[positions[debut + k]]);
                taille *= (ulong)a.Length;
                motif.Append(a.Length switch { 26 => 'L', 10 => '9', _ => 'A' });
            }
            var image = Deranger(motif.ToString(), valeur, taille);
            for (var k = n - 1; k >= 0; k--)
            {
                var a = AlphabetDe(majuscules[positions[debut + k]])!;
                sortie[positions[debut + k]] = a[(int)(image % (ulong)a.Length)];
                image /= (ulong)a.Length;
            }
        }
        var resultat = new string(sortie);
        // Le cache borne le cout (un nom revient des milliers de fois) ; vide au-dela d'un million
        // d'entrees pour borner la memoire sur une base de production.
        if (_cache.Count >= 1_000_000) _cache.Clear();
        _cache[cleCache] = resultat;
        return resultat;
    }

    // ------------------------------------------------------------------ la permutation sur [0, n)

    /// <summary>
    /// Derangement de [0, n) : x -> P^-1((P(x) + 1) mod n), ou P est une bijection a cle. C'est un seul
    /// cycle de longueur n : aucune valeur n'est son propre image (n &gt; 1), et c'est une bijection.
    /// </summary>
    private ulong Deranger(string tweak, ulong x, ulong n)
    {
        if (n < 2) return x;
        var t = Encoding.UTF8.GetBytes(tweak);
        var p = Bijection(t, x, n, inverse: false);
        return Bijection(t, (p + 1) % n, n, inverse: true);
    }

    /// <summary>Feistel sur le plus petit nombre pair de bits qui couvre n, puis marche de cycle :
    /// on reapplique tant que l'image sort de [0, n) -- c'est encore une bijection de [0, n).</summary>
    private ulong Bijection(byte[] tweak, ulong x, ulong n, bool inverse)
    {
        var bits = Math.Max(2, 64 - BitOperations.LeadingZeroCount(n - 1));
        if (bits % 2 == 1) bits++;
        do x = inverse ? Dechiffrer(tweak, x, bits) : Chiffrer(tweak, x, bits);
        while (x >= n);
        return x;
    }

    private ulong Chiffrer(byte[] tweak, ulong x, int bits)
    {
        var moitie = bits / 2;
        var masque = (1UL << moitie) - 1;
        ulong g = x >> moitie, d = x & masque;
        for (var tour = 0; tour < Tours; tour++)
        {
            var nouveau = g ^ (Tour(tweak, tour, bits, d) & masque);
            g = d;
            d = nouveau;
        }
        return (g << moitie) | d;
    }

    private ulong Dechiffrer(byte[] tweak, ulong y, int bits)
    {
        var moitie = bits / 2;
        var masque = (1UL << moitie) - 1;
        ulong g = y >> moitie, d = y & masque;
        for (var tour = Tours - 1; tour >= 0; tour--)
        {
            var ancien = d ^ (Tour(tweak, tour, bits, g) & masque);
            d = g;
            g = ancien;
        }
        return (g << moitie) | d;
    }

    private ulong Tour(byte[] tweak, int tour, int bits, ulong moitie)
    {
        Span<byte> entree = stackalloc byte[tweak.Length + 10];
        tweak.CopyTo(entree);
        entree[tweak.Length] = (byte)tour;
        entree[tweak.Length + 1] = (byte)bits;
        BinaryPrimitives.WriteUInt64LittleEndian(entree[(tweak.Length + 2)..], moitie);
        Span<byte> h = stackalloc byte[32];
        HMACSHA256.HashData(_cle, entree, h);
        return BinaryPrimitives.ReadUInt64LittleEndian(h);
    }

    /// <summary>
    /// L'extension d'un domaine de courriel GARDEE : apres « @…. », 2 a 4 lettres en fin de mot (fr, com, info).
    /// Une extension plus longue (« .avocat », « .paris », ou une faute de saisie) peut designer quelqu'un : elle
    /// est remplacee comme un mot. Et une extension gardee reste plus courte que le plus petit mot que cherche le
    /// controle (<see cref="ControleCopie.MotMin"/> = 5) : il ne la prend jamais pour une valeur restee en clair.
    /// fix-ok: mesure du 2026-11-21, RIG_DEV.CLIENT.CLIEN_EMAIL -- 10 adresses sur 2 000 ont une extension de 5 a
    /// 7 lettres ; gardees, le controle les retrouvait a leur place (5 « fuites » dans le banc).
    /// </summary>
    private static Regex Extension() => _extension;
    private static readonly Regex _extension =
        new(@"@(?:[\p{L}\p{N}_-]+\.)+(?<ext>\p{L}{2,4})(?![\p{L}\p{N}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
