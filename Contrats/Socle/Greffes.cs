namespace RigV3.Contrats.Socle;

// Les echanges du catalogue des greffes et de leurs reglages (lot 4 du plan de
// reconstruction : SOC-003, SOC-007, SOC-008 ; mode d'emploi :
// docs/reconstruction/socle-greffes.md). Le serveur les publie
// (Serveur/Socle/Greffes/ModuleGreffes.cs).

/// <summary>Les adresses du catalogue des greffes. {code} = code greffe (« 8002 »).</summary>
public static class RoutesGreffes
{
    /// <summary>GET : les fiches de tous les greffes du catalogue (SOC-007).</summary>
    public const string Liste = "/socle/greffes";

    /// <summary>GET : la fiche d'un greffe (SOC-007 CA01, CA03).</summary>
    public const string Fiche = "/socle/greffes/{code}";

    /// <summary>GET : les modifications de la fiche, la plus recente en premier (SOC-007 CA06).</summary>
    public const string Historique = "/socle/greffes/{code}/historique";

    /// <summary>PUT <see cref="SaisiePassage"/> : date et heure de passage, heure locale (SOC-007 CA05).</summary>
    public const string Passage = "/socle/greffes/{code}/passage";

    /// <summary>PUT <see cref="SaisieFuseau"/> : le fuseau horaire du greffe (SOC-007 T04).</summary>
    public const string Fuseau = "/socle/greffes/{code}/fuseau";

    /// <summary>GET : la base V3 du greffe repond-elle ? (SOC-007 CA04 ; 503 si non, sans repli).</summary>
    public const string Base = "/socle/greffes/{code}/base";

    /// <summary>GET : les reglages du greffe, lus dans SA base V3 (SOC-003).</summary>
    public const string Reglages = "/socle/greffes/{code}/reglages";

    /// <summary>GET un reglage ; PUT <see cref="SaisieReglage"/> le modifie, auteur et date gardes (SOC-003 CA07).</summary>
    public const string Reglage = "/socle/greffes/{code}/reglages/{reglage}";

    /// <summary>GET : la liste remise au referent Socle apres une reprise (SOC-003 CA04, CA05).</summary>
    public const string Reprise = "/socle/greffes/{code}/reprise";

    /// <summary>L'adresse d'une route pour un greffe (et un reglage) donnes.</summary>
    public static string Pour(string route, string code, string? reglage = null)
        => route.Replace("{code}", Uri.EscapeDataString(code))
                .Replace("{reglage}", Uri.EscapeDataString(reglage ?? ""));
}

/// <summary>La fiche d'un greffe dans le catalogue commun (SOC-007).</summary>
/// <param name="Serveur">L'instance SQL Server de SES DEUX bases (V2 et V3, decision Q4).</param>
/// <param name="BaseV3CreeeLe">Date de creation de la base V3 (SOC-008) ; null tant qu'elle n'existe pas.</param>
/// <param name="Fuseau">Nom Windows du fuseau (« SA Western Standard Time ») ; null s'il reste a saisir.</param>
/// <param name="DecalageUtcMinutes">Decalage du fuseau sur UTC, hors heure d'ete (-240 = UTC-4) ; null sans fuseau.</param>
/// <param name="PassageLocal">Date et heure de passage, en heure LOCALE du greffe ; null si non saisie.</param>
/// <param name="PassageFait">L'heure de passage est-elle atteinte (heure locale du greffe) ? Elle ne change plus alors.</param>
public sealed record FicheGreffe(
    string Code,
    string Nom,
    string Serveur,
    string BaseV2,
    string BaseV3,
    DateTime? BaseV3CreeeLe,
    bool EstBeta,
    bool EstOutreMer,
    string? Fuseau,
    int? DecalageUtcMinutes,
    DateTime? PassageLocal,
    bool PassageFait,
    string ModifiePar,
    DateTime ModifieLe);

/// <summary>Une modification de la fiche (SOC-007 CA06) : l'etat APRES le changement.</summary>
/// <param name="Operation">« C » creation, « M » modification.</param>
public sealed record ModificationGreffe(
    int Numero,
    string Operation,
    string ModifiePar,
    DateTime ModifieLe,
    string BaseV3,
    bool EstBeta,
    bool EstOutreMer,
    string? Fuseau,
    DateTime? PassageLocal);

/// <summary>Saisie de la date de passage : « 2027-03-01T08:00 », heure locale du greffe, sans decalage.</summary>
public sealed record SaisiePassage(string PassageLocal);

/// <summary>Saisie du fuseau : un nom Windows connu (« Romance Standard Time », « SA Western Standard Time »).</summary>
public sealed record SaisieFuseau(string Fuseau);

/// <summary>Ce que la route <see cref="RoutesGreffes.Base"/> dit de la base V3 d'un greffe.</summary>
/// <param name="Joignable">La base a repondu.</param>
/// <param name="MigrationsJouees">Migrations inscrites a son journal (sa version de schema).</param>
/// <param name="Detail">La cause, quand elle ne repond pas.</param>
public sealed record EtatBaseGreffe(
    string Code,
    string Serveur,
    string Base,
    bool Joignable,
    int MigrationsJouees,
    string? DerniereMigration,
    string? Detail);

/// <summary>Un reglage du greffe (SOC-003) : la valeur reste le TEXTE de la V2.</summary>
public sealed record ReglageGreffe(
    string Code,
    string Libelle,
    string? Valeur,
    string? Utilisation,
    string? AuteurCreation,
    DateTime? DateCreation,
    string? AuteurModif,
    DateTime? DateModif);

/// <summary>Nouvelle valeur d'un reglage (texte, null permis comme en V2).</summary>
public sealed record SaisieReglage(string? Valeur);

/// <summary>La liste remise au referent Socle apres une reprise (SOC-003 T05).</summary>
/// <param name="Reglages">Nombre de reglages repris dans la base V3.</param>
/// <param name="NonLus">Reglages gardes qu'aucune fonction de la V3 ne lit (CA04) : le referent tranche.</param>
/// <param name="CheminsInjoignables">Chemins reseau que les serveurs de la V3 n'atteignent pas (CA05), laisses tels quels.</param>
public sealed record RapportReprise(
    string Code,
    int Reglages,
    IReadOnlyList<string> NonLus,
    IReadOnlyList<CheminInjoignable> CheminsInjoignables);

/// <summary>Un reglage dont le chemin reseau ne repond pas depuis le serveur de la V3.</summary>
public sealed record CheminInjoignable(string Reglage, string Chemin, string Cause);

/// <summary>Un refus du serveur, avec sa raison en clair (404, 401, 409, 503...).</summary>
public sealed record RefusGreffe(string Motif);
