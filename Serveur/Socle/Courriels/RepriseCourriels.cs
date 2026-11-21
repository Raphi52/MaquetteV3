using Microsoft.Data.SqlClient;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Courriels;

/// <summary>Ce qu'une reprise des courriels a copie.</summary>
public sealed record BilanRepriseCourriels(int Modeles, int Listes, int ListesParDefaut);

/// <summary>
/// LA REPRISE DES MODELES DE COURRIEL D'UN GREFFE, V2 -> V3 (SOC-006 T05, CA01). Meme principe que la reprise des
/// reglages (<see cref="RepriseReglages"/>) : la V2 se lit par <see cref="LectureV2"/> (un SELECT seul), la V3 est
/// remplacee dans UNE transaction (chaque repetition du transfert repart de la V2), rien n'est interprete.
///   dbo.EMAIL_MODEL                      -> soc.ModeleMessage
///   dbo.LISTE_DISTRIBUTION_MAIL          -> soc.ListeDiffusion       (identifiants gardes)
///   dbo.EMAIL_MODEL_LISTE_DISTRIB_DEFAUT -> soc.ModeleMessageListe   (identifiants gardes)
/// L'habillage (MAIL_TEMPLATE) est repris par SOC-003 (soc.ModeleCourriel). Une colonne absente d'une base V2 (les deux
/// de la signature n'existent pas partout : RIG_DEV les a, le projet versionne non) prend la valeur par defaut de la V3.
/// Les courriels deja envoyes (EMAIL_ENVOYE) et leurs traces ne sont pas repris ici.
/// </summary>
public static class RepriseCourriels
{
    private static readonly (string V2, string V3)[] ColonnesModele =
    [
        ("EMAILMOD_TYPE", "Type"), ("EMAILMOD_SUJET", "Sujet"), ("EMAILMOD_CORPS", "Corps"), ("EMAILMOD_CORPS_HTML", "CorpsHtml"),
        ("EMAILMOD_FROM", "Expediteur"), ("EMAILMOD_REPLY", "Reponse"), ("EMAILMOD_COPY_TO", "Copie"),
        ("EMAILMOD_LOT_1", "Lot1"), ("EMAILMOD_LOT_2", "Lot2"), ("EMAILMOD_ONLY_AMITEL", "ReserveAmitel"),
        ("EMAILMOD_LIBELLE_ECRAN", "LibelleEcran"), ("EMAILMOD_INSCRIPTION", "Inscription"),
        ("EMAILMOD_UTILISABLE_EN_SAISIE", "UtilisableEnSaisie"), ("EMAILMOD_COMPTA", "Comptabilite"),
        ("EMAILMOD_DEST_DEFAUT", "DestinataireDefaut"), ("EMAILMOD_IS_ENVOI_AUTOMATIQUE", "EnvoiAutomatique"),
        ("EMAILMOD_SIGNATURE_ACTIVE", "SignatureActive"), ("EMAILMOD_SIGNATURE", "Signature"),
    ];

    private static readonly (string V2, string V3)[] ColonnesListe =
    [
        ("LSTDM_ID_LSTDM", "Id"), ("LSTDM_CODE", "Code"), ("LSTDM_LIBELLE", "Libelle"), ("LSTDM_ADR_MAILS", "Adresses"),
        ("LSTDM_RCS", "Rcs"), ("LSTDM_JUDICIAIRE", "Judiciaire"), ("LSTDM_PRIVE", "Prive"), ("LSTDM_ACTIF", "Actif"),
    ];

    private static readonly (string V2, string V3)[] ColonnesLien =
    [
        ("EMLDD_ID_EMLDD", "Id"), ("EMLDD_TYPE_EMAILMOD", "TypeModele"), ("EMLDD_ID_LSTDM", "IdListe"),
    ];

    private static readonly (string Source, string Cible, (string V2, string V3)[] Colonnes)[] Tables =
    [
        ("dbo.EMAIL_MODEL", "soc.ModeleMessage", ColonnesModele),
        ("dbo.LISTE_DISTRIBUTION_MAIL", "soc.ListeDiffusion", ColonnesListe),
        ("dbo.EMAIL_MODEL_LISTE_DISTRIB_DEFAUT", "soc.ModeleMessageListe", ColonnesLien),
    ];

    /// <summary>Copie les modeles et les listes de la base V2 du greffe dans sa base V3 (remplace l'existant).</summary>
    public static async Task<BilanRepriseCourriels> ReprendreAsync(SqlConnection v2, SqlConnection v3, CancellationToken ct = default)
    {
        // Les colonnes presentes dans la V2 sont lues AVANT la transaction : rien n'est touche si la V2 ne repond pas.
        var presentes = new List<(string Source, string Cible, (string V2, string V3)[] Colonnes)>();
        foreach (var (source, cible, colonnes) in Tables)
            presentes.Add((source, cible, await PresentesAsync(v2, source, colonnes, ct)));

        await using var tx = (SqlTransaction)await v3.BeginTransactionAsync(ct);
        await using (var del = new SqlCommand("DELETE FROM soc.ModeleMessageListe; DELETE FROM soc.ListeDiffusion; DELETE FROM soc.ModeleMessage;", v3, tx))
            await del.ExecuteNonQueryAsync(ct);
        foreach (var (source, cible, colonnes) in presentes.Where(t => t.Colonnes.Length > 0))
        {
            await using var r = await LectureV2.LireAsync(v2, $"SELECT {string.Join(", ", colonnes.Select(c => c.V2))} FROM {source}", ct);
            using var bulk = new SqlBulkCopy(v3, SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.CheckConstraints, tx)
            {
                DestinationTableName = cible,
                BulkCopyTimeout = 600,
            };
            foreach (var (a, b) in colonnes) bulk.ColumnMappings.Add(a, b);
            await bulk.WriteToServerAsync(r, ct);
        }
        var bilan = new BilanRepriseCourriels(
            await CompterAsync(v3, tx, "soc.ModeleMessage", ct),
            await CompterAsync(v3, tx, "soc.ListeDiffusion", ct),
            await CompterAsync(v3, tx, "soc.ModeleMessageListe", ct));
        await tx.CommitAsync(ct);
        return bilan;
    }

    /// <summary>
    /// Compare la V2 et la V3 (CA01), ligne par ligne et colonne par colonne : type, objet, texte, format, expediteur,
    /// reponse, copie, destinataire par defaut, chaque case. Texte au caractere pres, case a sa valeur.
    /// </summary>
    public static async Task<IReadOnlyList<EcartReprise>> ComparerAsync(SqlConnection v2, SqlConnection v3, CancellationToken ct = default)
    {
        var ecarts = new List<EcartReprise>();
        foreach (var (source, cible, toutes) in Tables)
        {
            var colonnes = await PresentesAsync(v2, source, toutes, ct);
            var table = source["dbo.".Length..];
            var lignesV2 = new Dictionary<string, object?[]>(StringComparer.Ordinal);
            if (colonnes.Length > 0)
                await using (var r = await LectureV2.LireAsync(v2, $"SELECT {string.Join(", ", colonnes.Select(c => c.V2))} FROM {source}", ct))
                    while (await r.ReadAsync(ct)) lignesV2[Cle(r)] = Valeurs(r);
            var lignesV3 = new Dictionary<string, object?[]>(StringComparer.Ordinal);
            var lecture = colonnes.Length > 0 ? colonnes : toutes[..1];
            await using (var cmd = new SqlCommand($"SELECT {string.Join(", ", lecture.Select(c => c.V3))} FROM {cible};", v3))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct)) lignesV3[Cle(r)] = Valeurs(r);

            foreach (var (cle, a) in lignesV2)
            {
                if (!lignesV3.TryGetValue(cle, out var b)) { ecarts.Add(new(table, cle, "(ligne)", "presente", "(absente en V3)")); continue; }
                for (var i = 1; i < colonnes.Length; i++)
                    if (!RepriseReglages.MemeValeur(a[i], b[i]))
                        ecarts.Add(new(table, cle, colonnes[i].V2, RepriseReglages.Texte(a[i]), RepriseReglages.Texte(b[i])));
            }
            foreach (var cle in lignesV3.Keys.Where(k => !lignesV2.ContainsKey(k)))
                ecarts.Add(new(table, cle, "(ligne)", "(absente en V2)", "presente"));
        }
        return ecarts;
    }

    /// <summary>Les colonnes de <paramref name="colonnes"/> que la table V2 porte ; aucune si la table n'existe pas.</summary>
    private static async Task<(string V2, string V3)[]> PresentesAsync(SqlConnection v2, string table, (string V2, string V3)[] colonnes, CancellationToken ct)
    {
        var noms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var r = await LectureV2.LireAsync(v2, "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@t)", ct,
                         new SqlParameter("@t", System.Data.SqlDbType.NVarChar, 256) { Value = table }))
            while (await r.ReadAsync(ct)) noms.Add(r.GetString(0));
        // La cle (1re colonne) doit y etre : sinon la table n'est pas celle qu'on croit, on ne copie rien.
        return noms.Contains(colonnes[0].V2) ? colonnes.Where(c => noms.Contains(c.V2)).ToArray() : [];
    }

    private static string Cle(SqlDataReader r) => Convert.ToString(r.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private static object?[] Valeurs(SqlDataReader r)
    {
        var v = new object?[r.FieldCount];
        for (var i = 0; i < v.Length; i++) v[i] = r.IsDBNull(i) ? null : r.GetValue(i);
        return v;
    }

    private static async Task<int> CompterAsync(SqlConnection cn, SqlTransaction tx, string table, CancellationToken ct)
    {
        await using var cmd = new SqlCommand($"SELECT COUNT(*) FROM {table};", cn, tx);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
