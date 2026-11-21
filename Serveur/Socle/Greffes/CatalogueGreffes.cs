using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;

namespace RigV3.Serveur.Socle.Greffes;

// ============================================================
// LE CATALOGUE COMMUN DES GREFFES (SOC-007, lot 4 du plan de reconstruction).
//
// Il vit dans la base que le serveur ouvre (BaseRig, ConnectionStrings:RigV3) :
// la base COMMUNE de la V3. Il dit, pour chaque greffe, sur quelle instance sont
// ses deux bases et comment elles s'appellent.
//
// LA REGLE DE CA04. Une route qui a besoin de la base d'un greffe la demande ICI,
// et nulle part ailleurs :
//     async (string code, CatalogueGreffes c, CancellationToken ct) =>
//     { await using var cn = await c.OuvrirBaseV3Async(code, ct); ... }
// Aucune chaine de connexion de greffe n'est ecrite dans la configuration : elle ne
// porte que la FACON de s'authentifier (Greffes:Authentification, la meme pour toutes
// les instances ; par defaut la session Windows du serveur, comme la V2 :
// bases-et-roles.md:18). Une base qui ne repond pas leve BaseGreffeInjoignable
// (503), sans repli sur une autre base.
// ============================================================

/// <summary>Le greffe n'est pas au catalogue (404).</summary>
public sealed class GreffeInconnu(string code)
    : Exception($"Le greffe « {code} » n'est pas au catalogue commun.")
{
    public string Code { get; } = code;
}

/// <summary>La base d'un greffe ne repond pas (503) : on le dit, sans repli sur une autre.</summary>
public sealed class BaseGreffeInjoignable(string code, string serveur, string nomBase, string cause, Exception? interne = null)
    : Exception($"Base {nomBase} du greffe {code} injoignable sur {serveur} : {cause}", interne)
{
    public string Code { get; } = code;
    public string Serveur { get; } = serveur;
    public string Base { get; } = nomBase;
}

/// <summary>Ce qu'une modification du catalogue ecrit DANS sa transaction : son geste au journal des gestes
/// (SOC-016, lot 9), avec la fiche avant et apres.</summary>
public delegate Task EcritureDuGeste(SqlConnection cn, SqlTransaction tx, FicheGreffe avant, FicheGreffe apres, CancellationToken ct);

public sealed class CatalogueGreffes(BaseRig commune, IConfiguration configuration)
{
    /// <summary>Cle de configuration : comment s'authentifier aupres des instances des greffes
    /// (une chaine SANS serveur ni base, ex. « Integrated Security=True;TrustServerCertificate=True »).</summary>
    public const string CleAuthentification = "Greffes:Authentification";

    /// <summary>Par defaut : la session Windows du compte de service (V2 : bases-et-roles.md:18).</summary>
    public const string AuthentificationParDefaut = "Integrated Security=True;TrustServerCertificate=True";

    private readonly string Authentification =
        string.IsNullOrWhiteSpace(configuration[CleAuthentification]) ? AuthentificationParDefaut : configuration[CleAuthentification]!;

    private const string Colonnes =
        "CodeGreffe, Nom, Serveur, BaseV2, BaseV3, BaseV3CreeeLe, EstBeta, EstOutreMer, Fuseau, PassageLocal, ModifiePar, ModifieLe";

    /// <summary>Nom de la base V3 d'un greffe, deduit de sa base V2 (SOC-008 CA01) :
    /// RIG_AMIENS -> RIGV3_AMIENS ; un autre nom recoit le suffixe « _V3 ».</summary>
    public static string NomBaseV3(string baseV2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseV2);
        var nom = baseV2.StartsWith("RIG_", StringComparison.OrdinalIgnoreCase)
            ? "RIGV3_" + baseV2[4..]
            : baseV2 + "_V3";
        if (nom.Length > 128) throw new ArgumentException($"Nom de base V3 trop long ({nom.Length} > 128) : {nom}");
        return nom;
    }

    /// <summary>Toutes les fiches, par code.</summary>
    public async Task<IReadOnlyList<FicheGreffe>> ListerAsync(CancellationToken ct = default)
    {
        await using var cn = await commune.OuvrirAsync(ct);
        await using var cmd = new SqlCommand($"SELECT {Colonnes} FROM soc.Greffe ORDER BY CodeGreffe;", cn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var fiches = new List<FicheGreffe>();
        while (await r.ReadAsync(ct)) fiches.Add(Fiche(r));
        return fiches;
    }

    /// <summary>La fiche d'un greffe ; null s'il n'est pas au catalogue.</summary>
    public async Task<FicheGreffe?> LireAsync(string code, CancellationToken ct = default)
    {
        await using var cn = await commune.OuvrirAsync(ct);
        return await LireAsync(cn, null, code, ct);
    }

    internal static async Task<FicheGreffe?> LireAsync(SqlConnection cn, SqlTransaction? tx, string code, CancellationToken ct)
    {
        await using var cmd = new SqlCommand($"SELECT {Colonnes} FROM soc.Greffe WHERE CodeGreffe = @c;", cn, tx);
        cmd.Parameters.Add(new SqlParameter("@c", System.Data.SqlDbType.VarChar, 20) { Value = code });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Fiche(r) : null;
    }

    /// <summary>Les modifications de la fiche, la plus recente en premier (CA06).</summary>
    public async Task<IReadOnlyList<ModificationGreffe>> HistoriqueAsync(string code, CancellationToken ct = default)
    {
        await using var cn = await commune.OuvrirAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT IdHistorique, Operation, ModifiePar, ModifieLe, BaseV3, EstBeta, EstOutreMer, Fuseau, PassageLocal " +
            "FROM soc.GreffeHistorique WHERE CodeGreffe = @c ORDER BY IdHistorique DESC;", cn);
        cmd.Parameters.Add(new SqlParameter("@c", System.Data.SqlDbType.VarChar, 20) { Value = code });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var liste = new List<ModificationGreffe>();
        while (await r.ReadAsync(ct))
            liste.Add(new ModificationGreffe(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetDateTime(3), r.GetString(4),
                r.GetBoolean(5), r.GetBoolean(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetDateTime(8)));
        return liste;
    }

    /// <summary>
    /// Saisit la date et l'heure de passage, en heure LOCALE du greffe (CA05). Refusee
    /// (<see cref="InvalidOperationException"/>) si le greffe n'a pas de fuseau ou si son
    /// passage est deja fait ; la base le refuse aussi (declencheur soc.TR_Greffe_Regles).
    /// <paramref name="journal"/> s'ecrit DANS la transaction de la modification (SOC-016) : si
    /// l'un echoue, ni la fiche ni le journal ne changent.
    /// </summary>
    public async Task<FicheGreffe> SaisirPassageAsync(string code, DateTime passageLocal, string auteur,
                                                      EcritureDuGeste? journal = null, CancellationToken ct = default)
    {
        await using var cn = await commune.OuvrirAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var fiche = await LireAsync(cn, tx, code, ct) ?? throw new GreffeInconnu(code);
        if (fiche.Fuseau is null)
            throw new InvalidOperationException($"Le greffe {code} n'a pas de fuseau : le saisir avant sa date de passage (heure locale).");
        if (fiche.PassageFait)
            throw new InvalidOperationException(
                $"Le passage du greffe {code} est fait ({fiche.PassageLocal:dd/MM/yyyy HH:mm}, heure locale) : sa date ne change plus.");
        await ModifierAsync(cn, tx, code, auteur, "PassageLocal = @v",
            new SqlParameter("@v", System.Data.SqlDbType.DateTime2) { Value = DateTime.SpecifyKind(passageLocal, DateTimeKind.Unspecified) }, ct);
        return await TerminerAsync(cn, tx, code, fiche, journal, ct);
    }

    /// <summary>Saisit le fuseau (nom Windows connu de .NET et de SQL Server). Fige une fois le passage fait.
    /// <paramref name="journal"/> s'ecrit dans la meme transaction.</summary>
    public async Task<FicheGreffe> SaisirFuseauAsync(string code, string fuseau, string auteur,
                                                     EcritureDuGeste? journal = null, CancellationToken ct = default)
    {
        if (Fuseaux.Decalage(fuseau) is null)
            throw new ArgumentException($"Fuseau inconnu : « {fuseau} » (nom Windows attendu, ex. « SA Western Standard Time »).");
        await using var cn = await commune.OuvrirAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var fiche = await LireAsync(cn, tx, code, ct) ?? throw new GreffeInconnu(code);
        if (fiche.PassageFait)
            throw new InvalidOperationException($"Le passage du greffe {code} est fait : son fuseau ne change plus.");
        await ModifierAsync(cn, tx, code, auteur, "Fuseau = @v",
            new SqlParameter("@v", System.Data.SqlDbType.VarChar, 64) { Value = fuseau }, ct);
        return await TerminerAsync(cn, tx, code, fiche, journal, ct);
    }

    /// <summary>La fiche relue apres la modification, le geste ecrit au journal, puis la validation : tout ou rien.</summary>
    private static async Task<FicheGreffe> TerminerAsync(SqlConnection cn, SqlTransaction tx, string code, FicheGreffe avant,
                                                         EcritureDuGeste? journal, CancellationToken ct)
    {
        var apres = (await LireAsync(cn, tx, code, ct))!;
        if (journal is not null) await journal(cn, tx, avant, apres, ct);
        await tx.CommitAsync(ct);
        return apres;
    }

    /// <summary>Inscrit la base V3 creee pour le greffe (SOC-008 CA01).</summary>
    public async Task InscrireBaseV3Async(string code, string baseV3, string auteur, CancellationToken ct = default)
    {
        await using var cn = await commune.OuvrirAsync(ct);
        _ = await LireAsync(cn, null, code, ct) ?? throw new GreffeInconnu(code);
        await ModifierAsync(cn, null, code, auteur, "BaseV3 = @v, BaseV3CreeeLe = SYSDATETIME()",
            new SqlParameter("@v", System.Data.SqlDbType.VarChar, 128) { Value = baseV3 }, ct);
    }

    private static async Task ModifierAsync(SqlConnection cn, SqlTransaction? tx, string code, string auteur, string affectation,
                                            SqlParameter valeur, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(auteur);
        await using var cmd = new SqlCommand(
            $"UPDATE soc.Greffe SET {affectation}, ModifiePar = @auteur, ModifieLe = SYSDATETIME() WHERE CodeGreffe = @c;", cn, tx);
        cmd.Parameters.Add(valeur);
        cmd.Parameters.Add(new SqlParameter("@auteur", System.Data.SqlDbType.VarChar, 128) { Value = auteur });
        cmd.Parameters.Add(new SqlParameter("@c", System.Data.SqlDbType.VarChar, 20) { Value = code });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>La chaine de connexion a une base d'une instance de greffe : serveur et base
    /// viennent du catalogue, l'authentification de <see cref="CleAuthentification"/>.</summary>
    public string ConnexionBase(string serveur, string nomBase)
        => new SqlConnectionStringBuilder(Authentification)
        {
            DataSource = serveur,
            InitialCatalog = nomBase,
            ConnectTimeout = 10,
        }.ConnectionString;

    /// <summary>Ouvre la base V3 du greffe (CA04) : <see cref="GreffeInconnu"/> s'il n'est pas au
    /// catalogue, <see cref="BaseGreffeInjoignable"/> si elle ne repond pas.</summary>
    public async Task<SqlConnection> OuvrirBaseV3Async(string code, CancellationToken ct = default)
    {
        var fiche = await LireAsync(code, ct) ?? throw new GreffeInconnu(code);
        return await OuvrirAsync(fiche.Code, fiche.Serveur, fiche.BaseV3, lectureSeule: false, ct);
    }

    /// <summary>Ouvre la base V2 du greffe en LECTURE (comparaison de reprise) : memes refus.</summary>
    public async Task<SqlConnection> OuvrirBaseV2Async(string code, CancellationToken ct = default)
    {
        var fiche = await LireAsync(code, ct) ?? throw new GreffeInconnu(code);
        return await OuvrirAsync(fiche.Code, fiche.Serveur, fiche.BaseV2, lectureSeule: true, ct);
    }

    private async Task<SqlConnection> OuvrirAsync(string code, string serveur, string nomBase, bool lectureSeule, CancellationToken ct)
    {
        var chaine = new SqlConnectionStringBuilder(ConnexionBase(serveur, nomBase))
        {
            ApplicationIntent = lectureSeule ? ApplicationIntent.ReadOnly : ApplicationIntent.ReadWrite,
        }.ConnectionString;
        var cn = new SqlConnection(chaine);
        try
        {
            await cn.OpenAsync(ct);
            return cn;
        }
        catch (SqlException e)
        {
            await cn.DisposeAsync();
            throw new BaseGreffeInjoignable(code, serveur, nomBase, e.Message, e);
        }
    }

    private static FicheGreffe Fiche(SqlDataReader r)
    {
        var fuseau = r.IsDBNull(8) ? null : r.GetString(8);
        DateTime? passage = r.IsDBNull(9) ? null : r.GetDateTime(9);
        return new FicheGreffe(
            Code: r.GetString(0),
            Nom: r.GetString(1),
            Serveur: r.GetString(2),
            BaseV2: r.GetString(3),
            BaseV3: r.GetString(4),
            BaseV3CreeeLe: r.IsDBNull(5) ? null : r.GetDateTime(5),
            EstBeta: r.GetBoolean(6),
            EstOutreMer: r.GetBoolean(7),
            Fuseau: fuseau,
            DecalageUtcMinutes: fuseau is null ? null : (int?)Fuseaux.Decalage(fuseau)?.TotalMinutes,
            PassageLocal: passage,
            PassageFait: passage is { } p && fuseau is not null && Fuseaux.MaintenantLocal(fuseau) >= p,
            ModifiePar: r.GetString(10),
            ModifieLe: r.GetDateTime(11));
    }
}

/// <summary>
/// Les fuseaux des greffes (SOC-007 CA03). Noms Windows : SQL Server les connait
/// (sys.time_zone_info, AT TIME ZONE, des 2016) et .NET aussi (TimeZoneInfo).
/// </summary>
public static class Fuseaux
{
    /// <summary>Metropole : Paris (UTC+1, heure d'ete).</summary>
    public const string Metropole = "Romance Standard Time";

    /// <summary>
    /// Fuseau de reprise d'un greffe, par son departement (les deux ou trois premiers
    /// chiffres du code greffe). Valeurs du carnet (SOC-007 CA03) et de
    /// D:\RigApplication\Documentation\reference\90-transverse\drom.md:183-185 ; null pour un
    /// greffe d'outre-mer non prevu ici : l'exploitation le saisit (SOC-007 T04).
    /// </summary>
    public static string? ParDefaut(string codeGreffe, bool estOutreMer)
    {
        if (!estOutreMer) return Metropole;
        return codeGreffe.Length >= 3 ? codeGreffe[..3] switch
        {
            "971" => "SA Western Standard Time",   // Guadeloupe : Basse-Terre, Pointe-a-Pitre, UTC-4
            "972" => "SA Western Standard Time",   // Martinique : Fort-de-France, UTC-4
            "974" => "Mauritius Standard Time",    // La Reunion : Saint-Denis, UTC+4
            "976" => "E. Africa Standard Time",    // Mayotte : Mamoudzou, UTC+3
            "987" => "Hawaiian Standard Time",     // Polynesie : Papeete, UTC-10
            _ => null,
        } : null;
    }

    /// <summary>Decalage du fuseau sur UTC hors heure d'ete ; null s'il est inconnu.</summary>
    public static TimeSpan? Decalage(string fuseau)
        => TrouverFuseau(fuseau)?.BaseUtcOffset;

    /// <summary>L'heure locale du greffe en ce moment.</summary>
    public static DateTime MaintenantLocal(string fuseau)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TrouverFuseau(fuseau)
            ?? throw new ArgumentException($"Fuseau inconnu : {fuseau}"));

    private static TimeZoneInfo? TrouverFuseau(string fuseau)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(fuseau); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }
}
