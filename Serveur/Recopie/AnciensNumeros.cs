// fix-ok: fichier NEUF du lot 11 ; mesure par la compilation du banc RecopieRetourCheck (CS0122) : l'adresse inconnue et le numero
// d'erreur de la lecture seule etaient des constantes de la reprise, interne au serveur -- elles vivent dans AnciensNumeros (publique).
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Recopie;

namespace RigV3.Serveur.Recopie;

// ============================================================
// LES DONNEES REPRISES D'ANCIENS LOGICIELS ET LEURS ANCIENS NUMEROS (REC-009 ; lot 11, 2026-11-21).
//
// Des greffes sont venus a RIG d'un autre logiciel (Dedale, Alinea, MyGreffe, Replay, GAGI, une base Oracle) : la V2 garde,
// pour chaque fiche reprise, son ancien numero (trois tables de correspondance) et, pour une reprise base a base, la trace
// de la reprise (quatre tables MIGRATION_*). Les reprises de 2017 ont aussi cree des fiches pour combler un lien casse :
// un client « Fake data <numero> », un client « cree automatiquement par la migration RIG », une adresse inconnue
// (Migration Infogreffe Informatique\2017\...\MigrationClient.cs:208-215, ClientExtended.cs:21,51).
//
// Le transfert (REC-001) les prend toutes, TELLES QUELLES et en lecture seule :
//   - les correspondances des anciens numeros dans rec.AncienNumero (T01), ligne pour ligne (deux des trois tables n'ont pas de
//     cle primaire dans la V2, seulement leur identite : pas de copie telle quelle possible, REC-001 T03) ;
//   - les tables de reprise base a base et la correspondance des articles Replay dans v2.* (CA04, CA05), meme vides, leur
//     nombre de lignes au rapport du transfert ; un declencheur y refuse toute ecriture ;
//   - les fiches de remplissage repérées et listees dans rec.FicheAncienneReprise (CA03) : aucune n'est supprimee ni corrigee.
// La recherche par ancien numero et par numero RIG (CA01, CA02) reprend celle de la V2 (PROC_CORRESPMIG, OPE_RECHERCHE.cs ;
// PROC_CORRESP_MIGRATION, PROC_CORRESP_ORACLE, PROC_CORRESP_GAGI) : la fiche RIG, son numero, l'ancien numero et le numero
// Infogreffe. Mode d'emploi : docs/reconstruction/recopie.md, section « Les anciens logiciels (REC-009) ».
// ============================================================

/// <summary>Les tables de reprise base a base et la correspondance des articles Replay : telles quelles, en lecture seule
/// (REC-009 CA04, CA05). Colonnes relevees dans le projet de base de la V2 (RigDatabase\dbo\Tables\*.sql, 2026-11-21) ; elles
/// peuvent manquer dans une base (Facultative), le transfert le dit.</summary>
internal sealed class ReglesRec009 : IReglesTransfert
{
    public static readonly IReadOnlyList<string> Tables = ["MIGRATION_REFERENCE", "MIGRATION_TABLE", "MIGRATION_CHAMP", "MIGRATION_ID", "ARTICLE_CORRESP_REPLAY"];

    public IEnumerable<RegleTable> Regles =>
    [
        RegleTable.TelleQuelle("MIGRATION_REFERENCE", "REC-009", "MGRRF_ID_MGRRF", "MGRRF_SERVEUR_SOURCE", "MGRRF_BASE_SOURCE",
            "MGRRF_SERVEUR_DESTINATION", "MGRRF_BASE_DESTINATION", "MGRRF_GREFFE_SOURCE", "MGRRF_GREFFE_DESTINATION") with { Facultative = true },
        RegleTable.TelleQuelle("MIGRATION_TABLE", "REC-009", "MGRTB_ID_MGRRF", "MGRTB_TABLE_ALIAS", "MGRTB_TABLE_NOM", "MGRTB_PRIMARY_KEY",
            "MGRTB_FILTRE", "MGRTB_REFERENCE") with { Facultative = true },
        RegleTable.TelleQuelle("MIGRATION_CHAMP", "REC-009", "MGRCH_ID_MGRRF", "MGRCH_TABLE_ALIAS", "MGRCH_CHAMP_ALIAS", "MGRCH_CHAMP_NOM",
            "MGRCH_CHAMP_TYPE", "MGRCH_FK_TABLE_LIEE", "MGRCH_NM_TYPE_METIER", "MGRCH_NM_ANNEE", "MGRCH_NM_CHAR_POSITION", "MGRCH_NM_CHAR_VALEUR",
            "MGRCH_VF_VALEUR", "MGRCH_NM_UNICITE") with { Facultative = true },
        RegleTable.TelleQuelle("MIGRATION_ID", "REC-009", "MGRID_ID_MGRRF", "MGRID_TABLE_ALIAS", "MGRID_ID_ANCIEN", "MGRID_ID_NOUVEAU",
            "MGRID_FUSION", "MGRID_ERR_NUMERO", "MGRID_ERR_DESCRIPTION") with { Facultative = true },
        RegleTable.TelleQuelle("ARTICLE_CORRESP_REPLAY", "REC-009", "ARTCR_ID_ARTCR", "ARTCR_CODE_ARTICLE_BASE_REPLAY",
            "ARTCR_CODE_ARTICLE_CORRESP_RIG", "ARTCR_DATE_DEBUT", "ARTCR_DATE_FIN") with { Facultative = true },
    ];
}

/// <summary>
/// LA REPRISE DES ANCIENS NUMEROS (REC-009 T02), apres les tables : les trois tables de correspondance dans rec.AncienNumero,
/// une ligne pour une ligne ; les fiches de remplissage des anciennes reprises listees ; les tables de reprise en lecture seule.
/// </summary>
internal sealed class RepriseAnciensNumeros : IRepriseTransfert
{
    public string Nom => "anciens numeros";
    public string Story => "REC-009";
    public int Rang => 60;
    public bool ApresLesTables => true;

    /// <summary>Une table de correspondance de la V2 : son prefixe de colonnes, l'ancien identifiant et l'ancien numero.</summary>
    internal sealed record Source(string Table, string Logiciel, string P, string AncienId, string AncienNumero)
    {
        public IReadOnlyList<string> Colonnes =>
            [$"{P}_ID_{P}", $"{P}_CODE_GREFFE", $"{P}_TABLE", AncienId, AncienNumero, $"{P}_ID_RIG", $"{P}_NUMERO_RIG", $"{P}_NUMERO_INFOGREFFE"];
    }

    internal static readonly IReadOnlyList<Source> Sources =
    [
        new("CORRESP_MIGRATION_RIG", "migration", "MIGRI", "MIGRI_NO_MIG", "MIGRI_NUMERO_MIG"),
        new("CORRESP_ORACLE_RIG", "Oracle", "ORARI", "ORARI_NO_ORACLE", "ORARI_NUMERO_ORACLE"),
        new("CORRESP_GAGI_RIG", "GAGI", "GAGRI", "GAGRI_ID_GAGI", "GAGRI_NUMERO_GAGI"),
    ];

    /// <summary>Le libelle des « Fake data » de 2017 (MigrationClient.cs:211) et la note des clients qu'elles creaient
    /// (MigrationClient.cs:215, ClientExtended.cs:21 : « Client crée automatiquement par la migration RIG »).</summary>
    public const string FakeData = "Fake data%";
    public const string NoteMigration = "Client cr%automatiquement par la migration%";
    private const string AdresseInconnue = AnciensNumeros.AdresseInconnue;
    private const int ErreurLectureSeule = AnciensNumeros.ErreurLectureSeule;

    public IEnumerable<RegleTable> Regles => Sources.Select(s => new RegleTable(s.Table, SorteRegle.Reprise, Story, s.Colonnes, Facultative: true));

    public async Task<BilanRepriseTransfert> ExecuterAsync(ContexteReprise contexte, CancellationToken ct)
    {
        var instantane = Sql.Q(new SqlConnectionStringBuilder(contexte.ConnexionInstantane).InitialCatalog);
        await using var cn = new SqlConnection(contexte.ConnexionV3);
        await cn.OpenAsync(ct);
        async Task<bool> Existe(string table, string? colonne = null)
        {
            await using var cmd = new SqlCommand(colonne is null
                ? $"SELECT OBJECT_ID(N'{instantane.Trim('[', ']')}.dbo.{table}', N'U');"
                : $"SELECT COL_LENGTH(N'{instantane.Trim('[', ']')}.dbo.{table}', N'{colonne}');", cn);
            return await cmd.ExecuteScalarAsync(ct) is not (null or DBNull);
        }

        var presentes = new List<Source>();
        foreach (var s in Sources) if (await Existe(s.Table)) presentes.Add(s);
        // Le catalogue de l'etat fige se lit AVANT la transaction : une commande sans elle y est refusee par SqlClient (mesure : le
        // transfert du banc RecopieRetourCheck « interrompu », « BeginExecuteReader implique que la commande comporte une transaction »).
        var clients = await Existe("CLIENT", "CLIEN_NOM");
        var notes = await Existe("CLIENT", "CLIEN_BLOC_NOTES");
        var adresses = await Existe("ADRESSE_BASE", "ADRBS_NOM_VOIE");
        var portees = presentes.Select(s => new TablePortee(s.Table, "rec.AncienNumero",
            $"SELECT IdV2, CONVERT(nvarchar(200), Id) FROM rec.AncienNumero WHERE TableV2 = N'{s.Table}'")).ToList();
        await using (var deja = new SqlCommand("SELECT (SELECT COUNT_BIG(*) FROM rec.AncienNumero) + (SELECT COUNT_BIG(*) FROM rec.FicheAncienneReprise);", cn))
            if ((long)(await deja.ExecuteScalarAsync(ct))! > 0)
                return new BilanRepriseTransfert(true, "anciens numeros et fiches d'anciennes reprises deja dans la base V3", portees, []);

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var comptes = new List<string>();
        foreach (var s in presentes)
        {
            await using var cmd = new SqlCommand($"""
                INSERT INTO rec.AncienNumero (Logiciel, TableV2, IdV2, CodeGreffe, TableRig, AncienId, AncienNumero, IdRig, NumeroRig, NumeroInfogreffe)
                SELECT @l, @t, CONVERT(nvarchar(20), x.{s.P}_ID_{s.P}), x.{s.P}_CODE_GREFFE, x.{s.P}_TABLE, CONVERT(varchar(100), x.{s.AncienId}),
                       x.{s.AncienNumero}, x.{s.P}_ID_RIG, x.{s.P}_NUMERO_RIG, x.{s.P}_NUMERO_INFOGREFFE
                FROM {instantane}.dbo.{s.Table} AS x;
                """, cn, tx) { CommandTimeout = 0 };
            cmd.Parameters.AddWithValue("@l", s.Logiciel);
            cmd.Parameters.AddWithValue("@t", s.Table);
            comptes.Add($"{s.Table} {await cmd.ExecuteNonQueryAsync(ct)}");
        }

        // CA03 : les fiches de remplissage, reperees dans l'etat fige de la V2, listees pour le greffe.
        var remplissage = new List<string>();
        if (clients)
        {
            var note = notes ? $" OR c.CLIEN_BLOC_NOTES LIKE N'{NoteMigration}'" : "";
            remplissage.Add($"""
                SELECT N'CLIENT', CONVERT(nvarchar(200), c.CLIEN_ID_CLIEN),
                       CASE WHEN c.CLIEN_NOM LIKE N'{FakeData}' THEN N'client « Fake data » d''une ancienne reprise'
                            ELSE N'client créé automatiquement par une ancienne reprise' END,
                       CONVERT(nvarchar(400), c.CLIEN_NOM)
                FROM {instantane}.dbo.CLIENT AS c WHERE c.CLIEN_NOM LIKE N'{FakeData}'{note}
                """);
        }
        if (adresses)
            remplissage.Add($"""
                SELECT N'ADRESSE_BASE', CONVERT(nvarchar(200), a.ADRBS_ID_ADRBS), N'adresse inconnue d''une ancienne reprise',
                       CONVERT(nvarchar(400), CONCAT(a.ADRBS_NOM_VOIE, N' ', a.ADRBS_CODE_POSTAL, N' ', a.ADRBS_LOCALITE_POSTALE))
                FROM {instantane}.dbo.ADRESSE_BASE AS a WHERE a.ADRBS_NOM_VOIE = N'{AdresseInconnue}'
                """);
        var fiches = 0;
        if (remplissage.Count > 0)
        {
            await using var cmd = new SqlCommand("INSERT INTO rec.FicheAncienneReprise (TableV2, IdV2, Motif, Libelle, IdTransfert) SELECT x.*, @tr FROM (" +
                                                 string.Join(" UNION ALL ", remplissage) + ") AS x (TableV2, IdV2, Motif, Libelle);", cn, tx) { CommandTimeout = 0 };
            cmd.Parameters.AddWithValue("@tr", contexte.IdTransfert);
            fiches = await cmd.ExecuteNonQueryAsync(ct);
        }

        // CA04, CA05 : lecture seule dans la V3 (un declencheur par table : CREATE TRIGGER doit ouvrir son lot).
        var gardees = new List<string>();
        foreach (var t in ReglesRec009.Tables.Select(t => "v2." + t).Append("rec.AncienNumero"))
        {
            await using (var existe = new SqlCommand("SELECT OBJECT_ID(@t, N'U');", cn, tx))
            {
                existe.Parameters.AddWithValue("@t", t);
                if (await existe.ExecuteScalarAsync(ct) is null or DBNull) continue;
            }
            var (schema, nom) = (t.Split('.')[0], t.Split('.')[1]);
            await using var cmd = new SqlCommand($"""
                CREATE OR ALTER TRIGGER {Sql.Q(schema)}.{Sql.Q($"TR_{nom}_LectureSeule")} ON {Sql.Q(schema)}.{Sql.Q(nom)} AFTER INSERT, UPDATE, DELETE
                AS
                BEGIN
                    -- Pose par Serveur\Recopie\AnciensNumeros.cs (REC-009 CA04, CA05) : la trace des anciennes reprises ne se modifie pas.
                    SET NOCOUNT ON;
                    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
                    THROW {ErreurLectureSeule}, N'{nom} : trace d''une reprise d''un ancien logiciel, en lecture seule dans la V3 (REC-009).', 1;
                END;
                """, cn, tx);
            await cmd.ExecuteNonQueryAsync(ct);
            gardees.Add(t);
        }
        await tx.CommitAsync(ct);
        return new BilanRepriseTransfert(false,
            $"anciens numeros : {(comptes.Count == 0 ? "aucune table de correspondance dans cette base" : string.Join(", ", comptes))} ; " +
            $"{fiches} fiche(s) d'anciennes reprises listee(s) (rec.FicheAncienneReprise) ; lecture seule : {string.Join(", ", gardees)}", portees, []);
    }
}

/// <summary>La recherche par ancien numero et la liste des fiches d'anciennes reprises (REC-009 T03).</summary>
public static class AnciensNumeros
{
    /// <summary>L'adresse inconnue des reprises de 2017 (ClientExtended.cs:51).</summary>
    public const string AdresseInconnue = "* Adresse inconnu pour le client*";

    /// <summary>Une ecriture dans une trace d'ancienne reprise, refusee dans la V3 (REC-009 CA04, CA05).</summary>
    public const int ErreurLectureSeule = 51040;

    /// <summary>Ce que l'agent lit pour une table de fiche RIG (les six de la recherche de la V2, frmPrincipale.cs:64-71).</summary>
    public static string SorteFiche(string tableRig) => tableRig.ToUpperInvariant() switch
    {
        "DOSSIER_RCS" => "dossier RCS", "INSCRIPTION" => "inscription", "PROCEDURE_COLLECTIVE" => "procédure collective",
        "MESURE" => "mesure", "INSTANCE" => "instance", "DEBITEUR_TRANSCOD_NUMDEB" => "débiteur", _ => tableRig.ToLowerInvariant().Replace('_', ' '),
    };

    /// <summary>Par ancien numero (ou ancien identifiant) -- REC-009 CA01 -- ou par numero RIG -- CA02 ; la fiche de la V3 par
    /// rec.Correspondance (null si sa table n'est pas encore reprise par la V3).</summary>
    public static async Task<AncienNumeroVue[]> ChercherAsync(SqlConnection v3, string baseV2, string? ancien, string? rig, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ancien) && string.IsNullOrWhiteSpace(rig)) return [];
        await using var cmd = new SqlCommand("""
            SELECT TOP (200) a.Logiciel, a.TableRig, a.AncienId, a.AncienNumero, a.IdRig, a.NumeroRig, a.NumeroInfogreffe, k.TableV3, k.IdV3
            FROM rec.AncienNumero AS a
            OUTER APPLY (SELECT TOP (1) c.TableV3, c.IdV3 FROM rec.Correspondance AS c
                         WHERE c.BaseV2 = @b AND c.TableV2 = a.TableRig AND c.IdV2 = CONVERT(nvarchar(200), a.IdRig)) AS k
            WHERE (@ancien IS NOT NULL AND (a.AncienNumero = @ancien OR a.AncienId = @ancien)) OR (@rig IS NOT NULL AND a.NumeroRig = @rig)
            ORDER BY a.TableRig, a.NumeroRig, a.Id;
            """, v3);
        cmd.Parameters.AddWithValue("@b", baseV2);
        cmd.Parameters.Add(new SqlParameter("@ancien", System.Data.SqlDbType.VarChar, 100) { Value = string.IsNullOrWhiteSpace(ancien) ? DBNull.Value : ancien.Trim() });
        cmd.Parameters.Add(new SqlParameter("@rig", System.Data.SqlDbType.VarChar, 100) { Value = string.IsNullOrWhiteSpace(rig) ? DBNull.Value : rig.Trim() });
        var r = new List<AncienNumeroVue>();
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct))
        {
            string? S(int i) => l.IsDBNull(i) ? null : l.GetString(i);
            r.Add(new AncienNumeroVue(l.GetString(0), l.GetString(1), SorteFiche(l.GetString(1)), S(2), S(3), l.IsDBNull(4) ? null : l.GetInt32(4), S(5), S(6), S(7), S(8)));
        }
        return [.. r];
    }

    /// <summary>Les fiches creees par une ancienne reprise pour combler un lien casse, telles que transferees (REC-009 CA03).</summary>
    public static async Task<FicheAncienneRepriseVue[]> AnciennesReprisesAsync(SqlConnection v3, string baseV2, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT f.TableV2, f.IdV2, k.TableV3, k.IdV3, f.Motif, f.Libelle
            FROM rec.FicheAncienneReprise AS f
            OUTER APPLY (SELECT TOP (1) c.TableV3, c.IdV3 FROM rec.Correspondance AS c WHERE c.BaseV2 = @b AND c.TableV2 = f.TableV2 AND c.IdV2 = f.IdV2) AS k
            ORDER BY f.TableV2, f.Motif, f.IdV2;
            """, v3);
        cmd.Parameters.AddWithValue("@b", baseV2);
        var r = new List<FicheAncienneRepriseVue>();
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct))
            r.Add(new FicheAncienneRepriseVue(l.GetString(0), l.GetString(1), l.IsDBNull(2) ? null : l.GetString(2), l.IsDBNull(3) ? null : l.GetString(3),
                                              l.GetString(4), l.IsDBNull(5) ? null : l.GetString(5)));
        return [.. r];
    }
}
