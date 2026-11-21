// fix-ok: la V2 signe GTA/E le 31/12 a 22h (SQLQuery_Deploy_Certification_FULL.sql l.2074 et l.2381) ; ClotureNocturne doit donc devoir l exercice, prouve par tests/SocleChainesCheck
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Supervision;

namespace RigV3.Serveur.Socle.Chaines;

/// <summary>
/// LES CLOTURES AUTOMATIQUES (SOC-018 CA05 T02).
///
/// La V2 : le job planifie Job_Exec_Hash_Totaux_FactEnc, tous les jours a 22h, lance SP_INSERT_Totaux_Factures_Periodiques
/// et SP_INSERT_Totaux_Encaissements_Periodiques (le jour, GTP ; le mois, GTM, le dernier jour du mois ; l'exercice, GTA/E,
/// le 31/12), puis signe (mode 2). Un jour ou le job ne tourne pas n'est jamais rattrape.
///
/// La V3 : le planificateur (<see cref="PlanificateurNocturne"/>) demande a cette tache, pour chaque greffe, ce qui est du ;
/// <see cref="ServiceChaines.CloturerAsync"/> le fait, dans l'ordre. Est du, dans un greffe dont les chaines sont ouvertes :
///   - chaque JOUR depuis l'ouverture jusqu'au jour en cours si l'heure reglee (<see cref="CleHeure"/>, 22:00 comme le job de la
///     V2, lue a l'heure du greffe : le fuseau de soc.Certification) est passee, sinon jusqu'a la veille ;
///   - le MOIS, apres le dernier jour du mois (a partir de l'heure reglee ce jour-la) ;
///   - l'EXERCICE (GTA/E), apres le jour et le mois du 31/12, a partir de l'heure reglee ce soir-la, comme le job de la V2
///     (SP_INSERT_Totaux_*_Periodiques, le 31/12) : un greffe ou personne ne clique a quand meme son total annuel. La route
///     n'a pas eu besoin d'etre adaptee : elle accepte deja de clore l'exercice le 31/12 lui-meme (heure du greffe) et
///     refuse avant. Clore l'exercice ecrit aussi l'ARCHIVE FISCALE scellee (V3 seulement, la V2 la fait a part) : elle
///     reste dans le meme geste ; la PURGE du journal reste un geste humain.
/// Un jour oublie (serveur arrete) est du tant qu'il n'est pas clos : il est rattrape au passage suivant, avant les
/// suivants ; on ne clot jamais deux fois (une periode close n'est plus du, et la route refuse la seconde). Une copie
/// anonymisee n'est pas close (ses chaines sont cassees par l'anonymisation).
///
/// Une erreur dans un greffe arrete SES clotures (le jour oublie reste du et passe avant les suivants) et va au journal
/// technique avec son code ; les autres greffes ne s'en apercoivent pas, et le traitement ne passe pas « en erreur ».
/// </summary>
public sealed class ClotureNocturne(ServiceChaines chaines, CatalogueGreffes catalogue, JournalErreurs journal, ReglagesSupervision supervision,
                                    IConfiguration configuration) : ITraitementDeFond, ITachePlanifiee
{
    public const string NomTraitement = "chaines-clotures";

    /// <summary>L'heure du greffe a partir de laquelle on clot le jour (« hh:mm », 22:00 par defaut comme le job de la V2).</summary>
    public const string CleHeure = "Chaines:ClotureHeure";

    /// <summary>Qui signe l'evenement 50 : la partie serveur, pas une personne (le journal des gestes est celui des agents).</summary>
    public const string Auteur = "Clôture automatique (partie serveur de la V3)";

    public string Nom => NomTraitement;

    string ITachePlanifiee.Traitement => NomTraitement;

    private readonly TimeSpan heure = LireHeure(configuration[CleHeure]);

    private static TimeSpan LireHeure(string? brut)
    {
        if (string.IsNullOrWhiteSpace(brut)) return new TimeSpan(22, 0, 0);
        if (!TimeSpan.TryParseExact(brut.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out var h) || h >= TimeSpan.FromDays(1))
            throw new InvalidOperationException($"{CleHeure} = « {brut} » : une heure « hh:mm » est attendue (22:00 par defaut).");
        return h;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Ce qui est du
    // -----------------------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<string>> ChargesDuesAsync(FicheGreffe greffe, DateTime maintenantUtc, CancellationToken ct)
    {
        await using var cn = await catalogue.OuvrirBaseV3Async(greffe.Code, ct);
        if (await ServiceChaines.CopieAnonymiseeAsync(cn, null, ct) is not null) return [];
        var dues = await ClotureesDuesAsync(cn, maintenantUtc, ct);
        return dues.Count == 0 ? [] : [JsonSerializer.Serialize(dues)];
    }

    /// <summary>Les clotures dues, dans l'ordre : par date, le jour avant le mois du meme soir.</summary>
    internal async Task<List<DemandeCloture>> ClotureesDuesAsync(SqlConnection cn, DateTime maintenantUtc, CancellationToken ct)
    {
        DateTime ouverteLe;
        string fuseau;
        await using (var cmd = new SqlCommand(
            "IF OBJECT_ID(N'soc.Certification', N'U') IS NULL SELECT CAST(NULL AS datetime2), CAST(NULL AS varchar(64)) ELSE EXEC (N'SELECT OuverteLe, Fuseau FROM soc.Certification WHERE Id = 1');", cn))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await l.ReadAsync(ct) || l.IsDBNull(0)) return [];   // chaines pas ouvertes : rien a clore
            (ouverteLe, fuseau) = (l.GetDateTime(0), l.GetString(1));
        }
        var tz = TimeZoneInfo.FindSystemTimeZoneById(fuseau);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(maintenantUtc, DateTimeKind.Utc), tz);
        var cible = DateOnly.FromDateTime(local.TimeOfDay >= heure ? local : local.AddDays(-1));
        var debut = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ouverteLe, DateTimeKind.Utc), tz));
        var clos = new HashSet<string>(StringComparer.Ordinal);
        await using (var cmd = new SqlCommand("SELECT Type + ':' + Periode FROM soc.GrandTotalFactures WHERE Type IN (@j, @m, @e);", cn))
        {
            cmd.Parameters.AddWithValue("@j", PeriodesCloture.TypeJour);
            cmd.Parameters.AddWithValue("@m", PeriodesCloture.TypeMois);
            cmd.Parameters.AddWithValue("@e", PeriodesCloture.TypeExercice);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct)) clos.Add(l.GetString(0));
        }
        var dues = new List<DemandeCloture>();
        for (var jour = debut; jour <= cible; jour = jour.AddDays(1))
        {
            if (!clos.Contains(Cle(PeriodesCloture.Jour, jour)))
                dues.Add(new DemandeCloture(PeriodesCloture.Jour, jour.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            if (jour.AddDays(1).Day == 1 && !clos.Contains(Cle(PeriodesCloture.Mois, jour)))
                dues.Add(new DemandeCloture(PeriodesCloture.Mois, jour.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            if (jour is { Month: 12, Day: 31 } && !clos.Contains(Cle(PeriodesCloture.Exercice, jour)))
                dues.Add(new DemandeCloture(PeriodesCloture.Exercice, jour.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }
        return dues;
    }

    private static string Cle(string periode, DateOnly jour) => periode switch
    {
        PeriodesCloture.Jour => $"{PeriodesCloture.TypeJour}:{jour:yyyyMMdd}",
        PeriodesCloture.Mois => $"{PeriodesCloture.TypeMois}:{jour:yyyyMM}",
        _ => $"{PeriodesCloture.TypeExercice}:{jour:yyyy}",
    };

    // -----------------------------------------------------------------------------------------------------------------
    // Les faire
    // -----------------------------------------------------------------------------------------------------------------

    public async Task TraiterAsync(ElementFile element, CancellationToken ct)
    {
        var greffe = element.CodeGreffe ?? "?";
        try
        {
            var demandes = JsonSerializer.Deserialize<List<DemandeCloture>>(element.Charge)
                           ?? throw new InvalidOperationException("Charge de clôture illisible.");
            await using var cn = await catalogue.OuvrirBaseV3Async(greffe, ct);
            // Une copie anonymisee ne se clot pas, meme si l'element a ete depose avant qu'elle soit marquee.
            if (await ServiceChaines.CopieAnonymiseeAsync(cn, null, ct) is not null) return;
            foreach (var demande in demandes)
            {
                if (await EstCloseAsync(cn, demande, ct)) continue;   // deja close par une personne ou un autre serveur
                try { await chaines.CloturerAsync(cn, demande, Auteur, null, null, ct); }
                catch (RefusChaines)
                {
                    // Close entre-temps (une personne, un autre serveur) : rien a refaire ; sinon c'est un vrai refus, il remonte.
                    if (!await EstCloseAsync(cn, demande, ct)) throw;
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Le greffe ne repond pas ou refuse : ses clotures restent dues (le planificateur les redepose, dans l'ordre) ;
            // on ne leve pas, sinon le moteur arreterait le traitement pour TOUS les greffes.
            await journal.EcrireAsync(ErreurAEcrire.Depuis(e, Suivis.Nouveau(), NiveauxErreur.Erreur, OriginesErreur.Traitement, element.CodeGreffe, null,
                supervision.Site, $"traitement {NomTraitement}"), CancellationToken.None);
        }
    }

    private static async Task<bool> EstCloseAsync(SqlConnection cn, DemandeCloture demande, CancellationToken ct)
    {
        var jour = DateOnly.ParseExact(demande.Date!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return await ServiceChaines.ScalaireAsync<int>(cn, null,
            "SELECT COUNT(*) FROM soc.GrandTotalFactures WHERE Type + ':' + Periode = @cle;", ct,
            new SqlParameter("@cle", System.Data.SqlDbType.VarChar, 20) { Value = Cle(demande.Periode!, jour) }) > 0;
    }
}
