namespace RigV3.Contrats.Socle;

/// <summary>L'adresse du journal des gestes (SOC-016).</summary>
public static class RoutesJournal
{
    /// <summary>GET ?agent=&amp;objet=&amp;du=&amp;au= (dates yyyy-MM-dd, bornes comprises) -> <see cref="VueJournal"/>,
    /// du plus recent au plus ancien. Le chef de greffe (fonction <see cref="FonctionJournalDuGreffe"/>)
    /// voit tout le greffe ; un autre agent ne voit que ses propres gestes (CA03).</summary>
    public const string Gestes = "/socle/journal";

    /// <summary>La fonction qui ouvre le journal de TOUT le greffe (migration SOC-016_gestes.sql).</summary>
    public const string FonctionJournalDuGreffe = "JOURNAL_GESTES";
}

/// <summary>Une ligne du journal des gestes ; <paramref name="Instant"/> en UTC.</summary>
/// <param name="Phrase">Le geste en clair : « Jean Dupont a modifie le compte mdurand ».</param>
/// <param name="Source">« v3 » (un agent), « recopie-v2 » (la recopie, au nom du compte V2) ou « passage ».</param>
/// <param name="Avant">Les valeurs avant, en JSON ; null pour une creation.</param>
/// <param name="Apres">Les valeurs apres, en JSON ; null pour une suppression.</param>
public sealed record LigneJournal(
    long Id,
    DateTime Instant,
    string Phrase,
    string Qui,
    string Verbe,
    string Famille,
    string Objet,
    string? Ecran,
    string? Avant,
    string? Apres,
    string Source);

/// <summary>La reponse de <see cref="RoutesJournal.Gestes"/>.</summary>
/// <param name="ToutLeGreffe">Vrai pour le chef de greffe ; faux : seulement les gestes de l'appelant.</param>
public sealed record VueJournal(bool ToutLeGreffe, LigneJournal[] Lignes);
