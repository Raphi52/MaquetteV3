using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using RigV3.Contrats.Socle;

namespace RigV3.Serveur.Socle.Chaines;

/// <summary>
/// Ou vit la cle PRIVEE qui signe les chaines d'un greffe. Seul le chemin de signature l'ouvre ; la verification
/// n'en a pas besoin : elle lit la cle PUBLIQUE en base (soc.CleSignature).
/// </summary>
public interface ICoffreCles
{
    /// <summary>La cle ECDSA P-256 du greffe, creee au premier usage.</summary>
    ECDsa CleDuGreffe(string codeGreffe);

    /// <summary>Le nom de son conteneur, ecrit en base a cote de la cle publique (pour l'exploitation et l'auditeur).</summary>
    string Conteneur(string codeGreffe);
}

/// <summary>
/// La cle de chaque greffe dans un conteneur CNG NON EXPORTABLE du profil du compte de service de la partie serveur,
/// comme la V2 (CertifExpressoApp48\SignatureEcdsa.cs : « RIG_NF525_P256_ » + greffe, ECDSA P-256). Le nom est propre
/// a la V3 : la cle de la V2 reste celle de la V2 (CA02).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CoffreClesCng : ICoffreCles
{
    public const string PrefixeConteneur = "RIGV3_NF525_P256_";
    private readonly ConcurrentDictionary<string, ECDsa> ouvertes = new(StringComparer.OrdinalIgnoreCase);

    public string Conteneur(string codeGreffe) => PrefixeConteneur + codeGreffe;

    public ECDsa CleDuGreffe(string codeGreffe) => ouvertes.GetOrAdd(codeGreffe, code =>
    {
        var nom = Conteneur(code);
        var fournisseur = CngProvider.MicrosoftSoftwareKeyStorageProvider;
        CngKey cle;
        if (CngKey.Exists(nom, fournisseur)) cle = CngKey.Open(nom, fournisseur);
        else
        {
            try
            {
                cle = CngKey.Create(CngAlgorithm.ECDsaP256, nom,
                    new CngKeyCreationParameters { Provider = fournisseur, ExportPolicy = CngExportPolicies.None });
            }
            catch (CryptographicException)
            {
                // Deux serveurs qui signent pour la premiere fois au meme instant : l'autre vient de la creer.
                cle = CngKey.Open(nom, fournisseur);
            }
        }
        return new ECDsaCng(cle);
    });
}

/// <summary>La signature d'un maillon : ECDSA P-256 sur SHA-256 de la pre-image (UTF-8), « E1: » + Base64Url (r||s).</summary>
public static class Signatures
{
    /// <summary>Le prefixe d'algorithme (version 1), le meme que la V2.</summary>
    public const string Prefixe = "E1:";
    public const string Algorithme = "ECDSA P-256 SHA-256";

    public static string Signer(ECDsa cle, string preimage)
    {
        byte[] octets;
        // Une cle CNG partagee entre les requetes : une signature a la fois.
        lock (cle) octets = cle.SignData(Encoding.UTF8.GetBytes(preimage), HashAlgorithmName.SHA256);
        return Prefixe + Base64Url(octets);
    }

    /// <summary>Vrai si <paramref name="signature"/> est celle de <paramref name="preimage"/> pour la cle publique
    /// <paramref name="clePublique"/> (SubjectPublicKeyInfo). Sans prefixe E1: ou illisible : faux (defaut d'integrite).</summary>
    public static bool Verifier(ECDsa clePublique, string preimage, string? signature)
    {
        if (string.IsNullOrEmpty(signature) || !signature.StartsWith(Prefixe, StringComparison.Ordinal)) return false;
        byte[] octets;
        try { octets = DepuisBase64Url(signature[Prefixe.Length..]); }
        catch (FormatException) { return false; }
        return clePublique.VerifyData(Encoding.UTF8.GetBytes(preimage), octets, HashAlgorithmName.SHA256);
    }

    /// <summary>La cle publique d'une ligne de soc.CleSignature, prete a verifier.</summary>
    public static ECDsa ClePublique(byte[] spki)
    {
        var cle = ECDsa.Create();
        cle.ImportSubjectPublicKeyInfo(spki, out _);
        return cle;
    }

    /// <summary>L'empreinte d'une cle publique ou d'une archive : SHA-256, en hexadecimal minuscule.</summary>
    public static string Empreinte(byte[] octets) => Convert.ToHexStringLower(SHA256.HashData(octets));

    public static string Base64Url(byte[] octets) => Convert.ToBase64String(octets).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static byte[] DepuisBase64Url(string texte)
    {
        var b = texte.Replace('-', '+').Replace('_', '/');
        b += (b.Length % 4) switch { 2 => "==", 3 => "=", 0 => "", _ => throw new FormatException("Base64Url invalide.") };
        return Convert.FromBase64String(b);
    }
}

/// <summary>
/// LES PRE-IMAGES : ce qui est signe pour chaque maillon. La meme fonction sert a signer et a verifier : un champ ajoute
/// d'un seul cote casserait toute la chaine (C19). Chaque pre-image porte la chaine, le rang, « O » ou « N » (un maillon
/// precedent existe ou non) et la signature du precedent, puis les champs de la piece -- ceux de la V2
/// (SP_HashFactureR12, SP_HashEncaissement13, SP_HashDuplicata, SP_HashGrandTotaux_*, SP_HashJETR19). Chaque champ est
/// ecrit « longueur:valeur; » : aucun texte (un motif avec des virgules) ne peut deplacer une frontiere de champ, ce
/// que permettait la concatenation par virgules de la V2. Montants et dates en format invariant, figes ici.
/// </summary>
public static class Preimages
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Facture(long seq, string? precedente, string numero, string nature, DateTime emiseLe, decimal montantTtc,
                                 string client, string? codePostal, string? tvaIntra, IEnumerable<(decimal Taux, decimal Montant)> lignes)
        => Canon(ChainesSignees.Factures, seq, precedente, numero, nature, Date(emiseLe), Montant(montantTtc), client, codePostal ?? "",
                 tvaIntra ?? "", string.Join("|", lignes.Select(l => Montant(l.Taux) + ":" + Montant(l.Montant))));

    public static string Encaissement(long seq, string? precedente, string numero, DateTime encaisseLe, decimal montant)
        => Canon(ChainesSignees.Encaissements, seq, precedente, numero, Montant(montant), Date(encaisseLe));

    public static string Duplicata(long seq, string? precedente, string numeroFacture, string signatureFacture, int rang,
                                   DateTime imprimeLe, string motif, string compte)
        => Canon(ChainesSignees.Duplicatas, seq, precedente, numeroFacture, signatureFacture, rang.ToString(Inv), Date(imprimeLe), motif, compte);

    public static string GrandTotal(string chaine, long seq, string? precedente, string type, string periode, decimal total,
                                    decimal cumulReel, decimal cumulAbsolu, int nbPieces, DateTime calculeLe, string? information,
                                    IEnumerable<(decimal Taux, decimal Montant)> parTaux)
        => Canon(chaine, seq, precedente, type, periode, Montant(total), Montant(cumulReel), Montant(cumulAbsolu), nbPieces.ToString(Inv),
                 Instant(calculeLe), information ?? "", string.Join("|", parTaux.Select(t => Montant(t.Taux) + ":" + Montant(t.Montant))));

    public static string Evenement(string chaine, long seq, string? precedente, long id, int exercice, DateTime instant, short code,
                                   string libelle, string detail, string auteur)
        => Canon(chaine, seq, precedente, id.ToString(Inv), exercice.ToString(Inv), Instant(instant), code.ToString(Inv), libelle, detail, auteur);

    /// <summary>Le sceau d'une archive fiscale : son empreinte, ce qu'elle couvre et l'evenement 30 qui l'enregistre.</summary>
    public static string Sceau(string greffe, int exercice, string empreinte, int nbPieces, long seqJnp, long seqJp, long idEvenement)
        => Canon("ARCHIVE", exercice, null, greffe, empreinte, nbPieces.ToString(Inv), seqJnp.ToString(Inv), seqJp.ToString(Inv),
                 idEvenement.ToString(Inv));

    public static string Date(DateTime d) => d.ToString("yyyyMMddHHmmss", Inv);
    public static string Instant(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss.fff", Inv);
    public static string Montant(decimal m) => m.ToString("0.00", Inv);

    private static string Canon(string chaine, long seq, string? precedente, params string[] champs)
    {
        var sb = new StringBuilder();
        void Champ(string v) => sb.Append(v.Length.ToString(Inv)).Append(':').Append(v).Append(';');
        Champ(chaine);
        Champ(seq.ToString(Inv));
        Champ(precedente is null ? "N" : "O");
        Champ(precedente ?? "");
        foreach (var c in champs) Champ(c);
        return sb.ToString();
    }
}
