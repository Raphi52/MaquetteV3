using System.IO;
using System.Linq;
using System.Text.Json;

namespace RigV3Desktop;

/// <summary>
/// Le TYPE d'une actualite : ce qui le rend lisible en un coup d'oeil. Cinq
/// types seulement -- au-dela, personne ne retient le code couleur. La
/// couleur ne vit que dans le FILET (charte), jamais en aplat.
/// <paramref name="JoursDeVie"/> est la duree proposee a la redaction :
/// une fraude ne s'eteint pas toute seule (0 = epinglee), un retabli ne
/// reste qu'une journee.
/// </summary>
public sealed record TypeActualite(string Code, string Libelle, string Filet,
                                  int Gravite, int JoursDeVie)
{
    public const string Fraude = "fraude";
    public const string Panne = "panne";
    public const string Procedure = "procedure";
    public const string Info = "info";
    public const string Retabli = "retabli";

    /// <summary>Les cinq types, du plus grave au moins grave.</summary>
    public static readonly TypeActualite[] Tous =
    [
        new(Fraude,    "Fraude",    "act-fraude",    4, 0),
        new(Panne,     "Panne",     "act-panne",     3, 30),
        new(Procedure, "Procedure", "act-procedure", 2, 14),
        new(Info,      "Info",      "act-info",      1, 7),
        new(Retabli,   "Retabli",   "act-retabli",   0, 1)
    ];

    /// <summary>Un code inconnu (fichier partage ecrit a la main, type retire
    /// plus tard) retombe sur « Info » : la tuile affiche le message plutot
    /// que de le perdre.</summary>
    public static TypeActualite De(string? code)
        => Tous.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase))
           ?? Tous.First(t => t.Code == Info);

    /// <summary>La date de fin proposee a la redaction. Null = epingle.</summary>
    public DateTime? FinProposee(DateTime depuis)
        => JoursDeVie == 0 ? null : depuis.AddDays(JoursDeVie);
}

/// <summary>
/// Une ACTUALITE : un message REDIGE par le greffe, affiche sur
/// l'accueil de tout le monde. A ne pas confondre avec une alerte
/// (<see cref="AlerteGreffe"/>) qui est CALCULEE sur les dossiers : ici
/// personne ne calcule rien, quelqu'un ECRIT (« L'INPI est HS »).
/// <paramref name="Statut"/> choisit le filet de couleur, comme partout
/// ailleurs dans l'accueil (charte : la couleur ne vit que dans les filets).
/// <paramref name="Expire"/> est la date APRES laquelle l'actualite ne
/// s'affiche plus : sans elle, une actualite de panne resterait a l'ecran des
/// mois apres le retablissement du service.
/// </summary>
public sealed record Actualite(string Id, string Titre, string Message, string Statut,
                              string Auteur, DateTime Publie, DateTime? Expire,
                              string Type = TypeActualite.Info)
{
    public static readonly string[] Statuts = ["alerte", "action", "attente", "ok"];

    /// <summary>La fiche du type porte : le libelle lu, la couleur du filet,
    /// la gravite de tri et la duree de vie par defaut.</summary>
    public TypeActualite Fiche => TypeActualite.De(Type);

    /// <summary>Poids de gravite : sert a trier, le plus grave en tete. Il vient
    /// du TYPE (une fraude passe devant une info), pas du filet de la tuile.</summary>
    public int Gravite => Fiche.Gravite;

    /// <summary>Encore affichable a cette date ? Publie dans le passe et pas expire.</summary>
    public bool Actif(DateTime maintenant)
        => Publie <= maintenant && (Expire is null || Expire.Value > maintenant);

    /// <summary>« 16/09 08:40 » — l'horodatage lu dans la tuile.</summary>
    public string Horodatage => Publie.ToString("dd/MM HH:mm");
}

/// <summary>D'ou vient ce qu'on affiche : le fichier partage, le dernier bon
/// contenu garde en local (reseau injoignable), ou rien du tout.</summary>
public enum SourceActualites { Partage, Cache, Aucune }

/// <summary>
/// Le resultat d'une lecture : les actualites ACTIVES, d'ou ils viennent, et
/// de QUAND ils datent. L'etat degrade ne se tait pas -- <see cref="Etat"/>
/// est la phrase affichee sous la tuile : un reseau injoignable doit se VOIR,
/// sinon l'utilisateur croit lire l'actualite du jour alors qu'il lit celle
/// d'avant-hier.
/// </summary>
public sealed record LectureActualites(Actualite[] Actifs, SourceActualites Source,
                                      DateTime? Date, string Etat);

/// <summary>
/// Lecture et ecriture du fichier PARTAGE des actualites, avec repli hors
/// ligne. Instanciable (et non statique) pour que les controles puissent la
/// pointer ailleurs que sur le reseau du greffe.
/// </summary>
public sealed class DepotActualites
{
    /// <summary>RIGV3_BULLETINS : le fichier partage (reseau). Sans lui, un
    /// chemin local dans le profil : la V1 doit s'ouvrir sans reseau configure.</summary>
    public static string CheminPartageParDefaut =>
        Environment.GetEnvironmentVariable("RIGV3_BULLETINS") is { Length: > 0 } sur
            ? sur
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                           "RigV3", "bulletins-partage.json");

    /// <summary>RIGV3_BULLETINS_CACHE : le dernier bon contenu, garde en local.</summary>
    public static string CheminCacheParDefaut =>
        Environment.GetEnvironmentVariable("RIGV3_BULLETINS_CACHE") is { Length: > 0 } sur
            ? sur
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                           "RigV3", "bulletins-cache.json");

    public string CheminPartage { get; init; } = CheminPartageParDefaut;
    public string CheminCache { get; init; } = CheminCacheParDefaut;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// Lit le fichier partage. S'il est injoignable ou illisible, se replie
    /// sur le dernier bon contenu local et le DIT dans Etat. Ne leve jamais :
    /// l'accueil doit s'ouvrir meme sans reseau.
    /// </summary>
    public LectureActualites Lire(DateTime maintenant)
    {
        try
        {
            if (File.Exists(CheminPartage))
            {
                var texte = File.ReadAllText(CheminPartage);
                var lus = Deserialiser(texte);
                if (lus is not null)
                {
                    EcrireCache(texte);
                    return Resultat(lus, SourceActualites.Partage,
                                    File.GetLastWriteTime(CheminPartage), maintenant);
                }
            }
        }
        catch (Exception)
        {
            // Partage injoignable (reseau coupe, droits) : on ne bloque pas
            // l'accueil, on se replie -- et le repli se VOIT a l'ecran.
        }

        try
        {
            if (File.Exists(CheminCache))
            {
                var lus = Deserialiser(File.ReadAllText(CheminCache));
                if (lus is not null)
                    return Resultat(lus, SourceActualites.Cache,
                                    File.GetLastWriteTime(CheminCache), maintenant);
            }
        }
        catch (Exception) { }

        return new LectureActualites([], SourceActualites.Aucune, null,
                                    "aucune actualite — source partagee jamais lue");
    }

    /// <summary>Ecrit le fichier partage (ecran de saisie). Rend false si le
    /// reseau refuse : le redacteur doit savoir que son actualite n'est PAS partie.</summary>
    public bool Ecrire(IEnumerable<Actualite> actualites)
    {
        try
        {
            var texte = JsonSerializer.Serialize(actualites.ToArray(), Options);
            Directory.CreateDirectory(Path.GetDirectoryName(CheminPartage)!);
            File.WriteAllText(CheminPartage, texte);
            EcrireCache(texte);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Toutes les actualites du depot, expires compris : l'ecran de
    /// saisie doit voir ce qu'il modifie, pas seulement ce qui s'affiche.</summary>
    public Actualite[] Tous()
    {
        foreach (var chemin in new[] { CheminPartage, CheminCache })
        {
            try
            {
                if (File.Exists(chemin) && Deserialiser(File.ReadAllText(chemin)) is { } lus)
                    return lus;
            }
            catch (Exception) { }
        }
        return [];
    }

    private static Actualite[]? Deserialiser(string texte)
        => JsonSerializer.Deserialize<Actualite[]>(texte);

    private void EcrireCache(string texte)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CheminCache)!);
            File.WriteAllText(CheminCache, texte);
        }
        catch (Exception) { }
    }

    private static LectureActualites Resultat(Actualite[] lus, SourceActualites source,
                                             DateTime date, DateTime maintenant)
    {
        var actifs = lus.Where(b => b.Actif(maintenant))
                        .OrderByDescending(b => b.Gravite)
                        .ThenByDescending(b => b.Publie)
                        .ToArray();
        var etat = source == SourceActualites.Partage
            ? $"a jour — {date:dd/MM HH:mm}"
            : $"actualites du {date:dd/MM HH:mm} — source partagee injoignable";
        return new LectureActualites(actifs, source, date, etat);
    }

    /// <summary>
    /// Les deux actualites dictees par le metier (CNG, INPI), servant de
    /// contenu de depart quand aucun fichier partage n'existe encore.
    /// </summary>
    public static Actualite[] Exemples(DateTime maintenant) =>
    [
        new("cng-fraude", "Alerte CNG fraude : Ahamed CHERIFI",
            "Signalement CNG : vigilance sur toute formalite au nom de Ahamed CHERIFI. "
            + "Ne pas traiter sans validation du responsable.",
            "alerte", "Greffe", maintenant.Date.AddHours(8).AddMinutes(40),
            null, TypeActualite.Fraude),
        new("inpi-dcademat", "L'INPI est HS : depots de comptes suspendus",
            "L'INPI nous informe que leur service rencontre un souci concernant les depots "
            + "de comptes. De ce fait nous avons suspendu la remontee des DCADEMAT en "
            + "attendant leurs correctifs.",
            "attente", "Service informatique", maintenant.Date.AddHours(9).AddMinutes(15),
            maintenant.Date.AddDays(30), TypeActualite.Panne)
    ];
}
