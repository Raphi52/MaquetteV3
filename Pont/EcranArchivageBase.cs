using System.Globalization;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>Un dossier radie, aux champs de l'ecran (web/src/ecrans/EcranArchivage.tsx, LigneArchi) ; date aaaa-mm-jj.</summary>
public sealed record LigneArchivage(string Dossier, string Denomination, string Type, string Radiation);

/// <summary>Ce que l'ecran Archivage affiche, et d'ou (Pont/LectureEcranMain.cs).</summary>
/// <param name="Motif">Pourquoi rien n'est cherche (un critere obligatoire de RIG v2 manque), sinon null.</param>
/// <param name="Types">Les types RCS de RIG v2 (OPE_ARCHRSEL.cs:63-68), que l'ecran propose quand il lit la base.</param>
public sealed record VueArchivage(string Mode, string? Message, LigneArchivage[] Lignes, int Total, string? Motif, string[] Types,
                                  int MaxBordereau);

/// <summary>
/// L'entree de l'ecran « Archivage / radiation » (PROC_ARCHIRAD). Plan « RIG v3 100 % fonctionnel », lot 20 (27/11/2026), sur la
/// marche du lot 17. L'ecran n'avait pas de pont : ses formalites de demonstration sont dans le .tsx.
///
/// Branche au serveur, l'ecran montre les dossiers radies dans la periode, du type RCS choisi (ou de la liste de numeros), qui
/// n'ont pas encore leur « Historique des FORMALITES » en GED : ceux que compte la recherche de RIG v2
/// (Serveur/Socle/EcransMain/LectureArchivage.cs). « Preparer le bordereau » N'ECRIT PAS : RIG v2 cree une demande par dossier,
/// ses editions de bordereau et l'annexe GED (PROC_ARCHIRAD.cs:44-61, :150-200), que la V3 n'edite pas encore ; l'ecran le
/// dit (web/src/ecrans/EcranArchivage.tsx, SansBordereau), sans appel au pont. SANS serveur : mode « demo », aucune ligne
/// (l'ecran garde ses exemples, marques). En panne : le motif et aucune ligne.
/// </summary>
public static class EcranArchivage
{
    [JSInvokable("EcranArchivage.Dossiers")]
    public static Task<VueArchivage> Dossiers(string? du, string? au, string? typeRcs, string? liste)
        => DossiersAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, new RechercheArchivage(Jour(du), Jour(au), typeRcs, liste));

    /// <summary>Les dossiers radies du greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<VueArchivage> DossiersAsync(ClientServeur? serveur, string? greffe, RechercheArchivage recherche)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les dossiers à archiver", (s, g) =>
            s.EnvoyerAsync<RechercheArchivage, DossiersArchivageLus>(RoutesEcransMain.Pour(RoutesArchivage.Dossiers, g), recherche));
        string[] types = [.. RoutesArchivage.TypesRcs];
        if (lu.Valeur is not { } v) return new VueArchivage(lu.Mode, lu.Message, [], 0, null, types, RoutesArchivage.MaxBordereau);
        return new VueArchivage(lu.Mode, null,
            [.. v.Lignes.Select(d => new LigneArchivage(d.Numero, d.Designation, d.TypeRcs,
                                                        d.Radiation?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ""))],
            v.Total, v.Motif, types, RoutesArchivage.MaxBordereau);
    }

    private static DateOnly? Jour(string? iso)
        => DateOnly.TryParseExact(iso ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
