using System.Globalization;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Recopie;

/// <summary>Un type de demande de la V3 qui n'a pas d'equivalent dans une base V2 (cree dans la V3 seule), et les demandes
/// de la V3 qui en ont un : a lister au retour d'un greffe dans la V2 (REC-007), jamais a ranger sous un autre type.</summary>
public sealed record TypeSansEquivalent(int IdV3, string Reference, byte Niveau, string LibelleEcran);

/// <summary>
/// LES TYPES DE DEMANDE, RECONNUS PAR LEUR REFERENCE (REC-019). Dans la V2, un type est un numero (CATEGORIE.CTGR_ID_CTGR),
/// et le meme numero designe un autre type d'une base a l'autre (RIG_DEV : 1417 = MODIFREPER, 2136 = EDIT_BODPR ;
/// RIG_RECETTE : 1417 = APPLI, 2136 = EDS ; lu le 2026-11-20). Dans la V3, un type a SON identifiant (rec.TypeDemande.Id) et
/// se retrouve par sa reference, son niveau et ses parents ; le numero V2 n'est que dans rec.Correspondance de la base
/// (CA01). Une demande de la V2 (DMND_ID_CTGR) a le type que la correspondance de SA base donne a son numero (CA02).
/// </summary>
public static class TypesDemande
{
    /// <summary>Le numero du type dans la base V2, ou null : un type cree dans la V3 seule n'en a pas (CA03).</summary>
    public static async Task<int?> NumeroV2Async(SqlConnection v3, string baseV2, int idTypeV3, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand("SELECT TOP (1) IdV2 FROM rec.Correspondance WHERE BaseV2 = @b AND TableV2 = N'CATEGORIE' " +
                                             "AND TableV3 = N'rec.TypeDemande' AND IdV3 = @id ORDER BY Le;", v3);
        cmd.Parameters.AddWithValue("@b", baseV2);
        cmd.Parameters.AddWithValue("@id", idTypeV3.ToString(CultureInfo.InvariantCulture));
        return await cmd.ExecuteScalarAsync(ct) is string s ? int.Parse(s, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Le type V3 d'un numero de la base V2 (le type d'une demande de la V2), par la correspondance de CETTE base ;
    /// null si le numero n'y est pas. Jamais par le numero seul ni par une reference devinee.</summary>
    public static async Task<int?> TypeV3Async(SqlConnection v3, string baseV2, int numeroV2, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand("SELECT IdV3 FROM rec.Correspondance WHERE BaseV2 = @b AND TableV2 = N'CATEGORIE' AND IdV2 = @n " +
                                             "AND TableV3 = N'rec.TypeDemande';", v3);
        cmd.Parameters.AddWithValue("@b", baseV2);
        cmd.Parameters.AddWithValue("@n", numeroV2.ToString(CultureInfo.InvariantCulture));
        return await cmd.ExecuteScalarAsync(ct) is string s ? int.Parse(s, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Les types de la V3 sans equivalent dans la base V2 (CA03) : listes, jamais ranges sous un autre type.</summary>
    public static async Task<IReadOnlyList<TypeSansEquivalent>> SansEquivalentAsync(SqlConnection v3, string baseV2, CancellationToken ct = default)
    {
        var liste = new List<TypeSansEquivalent>();
        await using var cmd = new SqlCommand("""
            SELECT t.Id, t.Reference, t.Niveau, t.LibelleEcran FROM rec.TypeDemande AS t
            WHERE NOT EXISTS (SELECT 1 FROM rec.Correspondance AS c WHERE c.BaseV2 = @b AND c.TableV2 = N'CATEGORIE' AND c.TableV3 = N'rec.TypeDemande'
                              AND c.IdV3 = CONVERT(nvarchar(200), t.Id))
            ORDER BY t.Reference, t.Niveau, t.Id;
            """, v3);
        cmd.Parameters.AddWithValue("@b", baseV2);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct)) liste.Add(new TypeSansEquivalent(l.GetInt32(0), l.GetString(1), l.GetByte(2), l.GetString(3)));
        return liste;
    }
}

/// <summary>
/// LE TRANSFERT DES TYPES DE DEMANDE PAR LEUR REFERENCE (REC-019 T02) : chaque type de la base V2 recoit un identifiant de la
/// V3 ; son numero V2 va dans rec.Correspondance de la base, et nulle part ailleurs (CA01). Ses parents dans l'arbre
/// (domaine, famille, activite) sont traduits par cette correspondance, son service par soc.Service.IdV2 (la reprise des
/// comptes passe avant, rang 20) ; un parent ou un service absent de la base est un lien casse herite, note dans
/// rec.LienCasse, jamais devine. Le deroulement du type (CTGR_ID_MDPRC) reste dans la V2 (CA04). Deux types encore
/// indiscernables (meme reference, meme niveau, memes parents) sont transferes tous les deux et listes par la vue
/// rec.TypesDemandeIndiscernables pour le referent (CA05) : jamais fusionnes, jamais choisis au hasard.
/// </summary>
internal sealed class RepriseTypesDemande : IRepriseTransfert
{
    public string Nom => "types de demande par reference";
    public string Story => "REC-019";
    public int Rang => 30;
    public bool ApresLesTables => false;

    private static readonly FicheV3 Fiche = ReglesFichesV3.Fiches.Single(f => f.Recopie.TableV2 == "CATEGORIE");

    public IEnumerable<RegleTable> Regles =>
    [
        new("CATEGORIE", SorteRegle.Reprise, Story,
            [.. Fiche.Recopie.Correspondance!.Select(c => c.V2), .. Fiche.Recopie.ColonnesPropres, "CTGR_ID_CTGR"]),
    ];

    public async Task<BilanRepriseTransfert> ExecuterAsync(ContexteReprise contexte, CancellationToken ct)
    {
        var portee = new TablePortee("CATEGORIE", "rec.TypeDemande",
            $"SELECT IdV2, IdV3 FROM rec.Correspondance WHERE BaseV2 = {Sql.Texte(contexte.BaseV2)} AND TableV2 = N'CATEGORIE' AND TableV3 = N'rec.TypeDemande'");
        var instantane = Sql.Q(new SqlConnectionStringBuilder(contexte.ConnexionInstantane).InitialCatalog) + ".dbo.CATEGORIE";
        await using var cn = new SqlConnection(contexte.ConnexionV3);
        await cn.OpenAsync(ct);
        await using (var deja = new SqlCommand("SELECT COUNT_BIG(*) FROM rec.TypeDemande;", cn))
            if ((long)(await deja.ExecuteScalarAsync(ct))! > 0)
                return new BilanRepriseTransfert(true, "types de demande deja dans la base V3", [portee], []);

        // Les colonnes que la V3 prend telles quelles (ni parents, ni service : traduits ensuite).
        var traduites = new HashSet<string>(["CTGR_ID_CTGR_DOMAINE", "CTGR_ID_CTGR_FAMILLE", "CTGR_ID_CTGR_ACTIVITE", "CTGR_ID_SRVC"], StringComparer.OrdinalIgnoreCase);
        var simples = Fiche.Recopie.Correspondance!.Where(c => !traduites.Contains(c.V2)).ToList();
        string Q(string n) => Sql.Q(n);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await using var cmd = new SqlCommand($"""
            DECLARE @map TABLE (IdV2 int PRIMARY KEY, IdV3 int NOT NULL);
            -- MERGE et non INSERT : seul MERGE rend, ligne a ligne, le numero de la V2 avec l'identifiant que la V3 vient de donner.
            MERGE rec.TypeDemande AS t
            USING (SELECT c.CTGR_ID_CTGR, {string.Join(", ", simples.Select(m => $"c.{Q(m.V2)}"))} FROM {instantane} AS c) AS s ON 1 = 0
            WHEN NOT MATCHED THEN INSERT ({string.Join(", ", simples.Select(m => Q(m.V3)))}) VALUES ({string.Join(", ", simples.Select(m => $"s.{Q(m.V2)}"))})
            OUTPUT s.CTGR_ID_CTGR, inserted.Id INTO @map;

            UPDATE t SET IdDomaine = d.IdV3, IdFamille = f.IdV3, IdActivite = a.IdV3, IdService = sv.Id
            FROM rec.TypeDemande AS t JOIN @map AS m ON m.IdV3 = t.Id JOIN {instantane} AS c ON c.CTGR_ID_CTGR = m.IdV2
            LEFT JOIN @map AS d ON d.IdV2 = c.CTGR_ID_CTGR_DOMAINE LEFT JOIN @map AS f ON f.IdV2 = c.CTGR_ID_CTGR_FAMILLE
            LEFT JOIN @map AS a ON a.IdV2 = c.CTGR_ID_CTGR_ACTIVITE
            OUTER APPLY (SELECT TOP (1) s.Id FROM soc.Service AS s WHERE s.IdV2 = c.CTGR_ID_SRVC ORDER BY s.Id) AS sv;

            -- Un parent ou un service que la base n'a pas : lien casse herite, garde vide et note (jamais devine).
            INSERT INTO rec.LienCasse (IdTransfert, BaseV2, TableV2, IdV2, Cle, Colonnes, Valeur, TableVisee, Cause)
            SELECT @tr, @b, N'CATEGORIE', CONVERT(nvarchar(200), c.CTGR_ID_CTGR), x.Cle, x.Cle, CONVERT(nvarchar(400), x.Valeur), x.Visee, N'absente de la V2'
            FROM {instantane} AS c
            CROSS APPLY (VALUES (N'CTGR_ID_CTGR_DOMAINE', c.CTGR_ID_CTGR_DOMAINE, N'CATEGORIE'), (N'CTGR_ID_CTGR_FAMILLE', c.CTGR_ID_CTGR_FAMILLE, N'CATEGORIE'),
                                (N'CTGR_ID_CTGR_ACTIVITE', c.CTGR_ID_CTGR_ACTIVITE, N'CATEGORIE'), (N'CTGR_ID_SRVC', c.CTGR_ID_SRVC, N'SERVICE')) AS x (Cle, Valeur, Visee)
            WHERE x.Valeur IS NOT NULL AND ((x.Visee = N'CATEGORIE' AND NOT EXISTS (SELECT 1 FROM @map AS p WHERE p.IdV2 = x.Valeur))
                                         OR (x.Visee = N'SERVICE' AND NOT EXISTS (SELECT 1 FROM soc.Service AS s WHERE s.IdV2 = x.Valeur)));
            DECLARE @casses int = @@ROWCOUNT;

            INSERT INTO rec.Correspondance (BaseV2, TableV2, IdV2, TableV3, IdV3, IdTransfert)
            SELECT @b, N'CATEGORIE', CONVERT(nvarchar(200), m.IdV2), N'rec.TypeDemande', CONVERT(nvarchar(200), m.IdV3), @tr FROM @map AS m;

            SELECT CONCAT((SELECT COUNT(*) FROM @map), '|', @casses, '|', (SELECT COUNT(*) FROM rec.TypesDemandeIndiscernables),
                          '|', (SELECT COUNT(DISTINCT Reference) FROM rec.TypeDemande));
            """, cn, tx) { CommandTimeout = 600 };
        cmd.Parameters.AddWithValue("@b", contexte.BaseV2);
        cmd.Parameters.AddWithValue("@tr", contexte.IdTransfert);
        var r = ((string)(await cmd.ExecuteScalarAsync(ct))!).Split('|');
        await tx.CommitAsync(ct);
        return new BilanRepriseTransfert(false,
            $"{r[0]} types de demande ({r[3]} references), chacun avec SON identifiant de la V3, son numero V2 dans rec.Correspondance seulement ; " +
            $"{r[1]} parent(s) ou service(s) absent(s) de la V2, notes liens casses ; {r[2]} entree(s) indiscernable(s) (meme reference, niveau et " +
            "parents) listee(s) dans rec.TypesDemandeIndiscernables pour le referent", [portee], []);
    }
}
