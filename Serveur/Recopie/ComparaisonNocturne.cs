using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Supervision;

namespace RigV3.Serveur.Recopie;

/// <summary>
/// LE CONTROLE DE NUIT DES DEUX BASES DE CHAQUE GREFFE (REC-006 T04), planifie comme les clotures NF525 (decision du
/// lot 14 : une ITachePlanifiee et l'ITraitementDeFond du meme nom, inscrits par AddSingleton, sans second planificateur).
///
/// Est du, pour un greffe dont la base V3 est creee et a un transfert fini : entre l'heure du controle (<see cref="CleHeure"/>,
/// 02:00 heure du greffe, APRES la sauvegarde de nuit -- proposee, a valider par l'exploitation) et l'ouverture du greffe
/// (<see cref="CleOuverture"/>, 06:00), si aucun controle n'a commence depuis 20 heures. Un soir manque (serveur arrete)
/// n'est pas rattrape en journee : le controle lit des etats figes et ne bloque personne, mais il charge le serveur ; il
/// repasse la nuit suivante. Le traitement fait <see cref="ComparaisonBases.ComparerAsync"/> ; un rapport avec un ecart
/// alerte l'exploitation par <see cref="AlertesRecopie"/> (CA04) ; les rapports de plus de 90 jours sont purges a chaque
/// controle. Une erreur dans un greffe va au journal technique et n'arrete pas le traitement des autres.
/// </summary>
public sealed class ComparaisonNocturne(CatalogueGreffes catalogue, JournalErreurs journal, ReglagesSupervision supervision,
                                        IConfiguration configuration, ILogger<ComparaisonNocturne> log) : ITraitementDeFond, ITachePlanifiee
{
    public const string NomTraitement = "recopie-comparaison";

    /// <summary>L'heure du greffe a partir de laquelle on compare (« hh:mm », 02:00 par defaut).</summary>
    public const string CleHeure = "Recopie:ComparaisonHeure";

    /// <summary>L'heure d'ouverture du greffe, apres laquelle on ne lance plus le controle de la nuit (06:00 par defaut).</summary>
    public const string CleOuverture = "Recopie:OuvertureHeure";

    /// <summary>Jours de conservation des rapports (90 par defaut, duree proposee par REC-006 CA04).</summary>
    public const string CleConservation = "Recopie:ConservationRapportsJours";

    public string Nom => NomTraitement;

    string ITachePlanifiee.Traitement => NomTraitement;

    private readonly TimeSpan heure = Heure(configuration[CleHeure], CleHeure, new TimeSpan(2, 0, 0));
    private readonly TimeSpan ouverture = Heure(configuration[CleOuverture], CleOuverture, new TimeSpan(6, 0, 0));
    private readonly int conservation = int.TryParse(configuration[CleConservation], NumberStyles.None, CultureInfo.InvariantCulture, out var j) && j > 0 ? j : 90;

    private static TimeSpan Heure(string? brut, string cle, TimeSpan defaut)
    {
        if (string.IsNullOrWhiteSpace(brut)) return defaut;
        if (!TimeSpan.TryParseExact(brut.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out var h) || h >= TimeSpan.FromDays(1))
            throw new InvalidOperationException($"{cle} = « {brut} » : une heure « hh:mm » est attendue.");
        return h;
    }

    public async Task<IReadOnlyList<string>> ChargesDuesAsync(FicheGreffe greffe, DateTime maintenantUtc, CancellationToken ct)
    {
        if (greffe.BaseV3CreeeLe is null) return [];
        var tz = TimeZoneInfo.FindSystemTimeZoneById(greffe.Fuseau ?? "Romance Standard Time");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(maintenantUtc, DateTimeKind.Utc), tz);
        if (local.TimeOfDay < heure || local.TimeOfDay >= ouverture) return [];
        await using var cn = await catalogue.OuvrirBaseV3Async(greffe.Code, ct);
        await using var cmd = new SqlCommand("""
            IF OBJECT_ID(N'rec.Comparaison', N'U') IS NULL OR OBJECT_ID(N'rec.Transfert', N'U') IS NULL SELECT 0
            ELSE EXEC (N'SELECT CASE WHEN EXISTS (SELECT 1 FROM rec.Transfert WHERE Etat = ''fini'')
                                     AND NOT EXISTS (SELECT 1 FROM rec.Comparaison WHERE Debut > DATEADD(hour, -20, SYSDATETIME())) THEN 1 ELSE 0 END');
            """, cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1 ? [local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)] : [];
    }

    public async Task TraiterAsync(ElementFile element, CancellationToken ct)
    {
        try
        {
            var fiche = await catalogue.LireAsync(element.CodeGreffe ?? "", ct) ?? throw new GreffeInconnu(element.CodeGreffe ?? "?");
            var rapport = await ComparaisonBases.ComparerAsync(new OptionsComparaison(catalogue.ConnexionBase(fiche.Serveur, fiche.BaseV2),
                                                                                      catalogue.ConnexionBase(fiche.Serveur, fiche.BaseV3))
            {
                ConservationJours = conservation,
                Journal = log,
            }, ct);
            log.LogInformation("Controle de nuit {Id} du greffe {Greffe} : {Etat}, {Ecarts} ecarts dont {EcartsDroits} de droits, {Orphelins} orphelins, {Sans} sans correspondance, {Ms} ms",
                               rapport.Id, fiche.Code, rapport.Etat, rapport.Ecarts, rapport.Tables.Where(t => t.TableV2 == DroitsRecopies.TableControle).Sum(t => t.Ecarts), rapport.Orphelins, rapport.SansCorrespondance, rapport.DureeMs);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Le greffe ne repond pas : son controle repassera la nuit suivante ; on ne leve pas, sinon le moteur arreterait
            // le traitement pour TOUS les greffes.
            await journal.EcrireAsync(ErreurAEcrire.Depuis(e, Suivis.Nouveau(), NiveauxErreur.Erreur, OriginesErreur.Traitement, element.CodeGreffe, null,
                supervision.Site, $"traitement {NomTraitement}"), CancellationToken.None);
        }
    }
}
