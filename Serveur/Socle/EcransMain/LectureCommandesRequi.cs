using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransMain.LectureDemandes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Réquisitions » (PROC_REQUI) : la commande terminée relue dans la base du greffe, LECTURE SEULE (LectureV2) -- ce que le geste
/// « Terminer » (EcritureRequisitions.cs) a écrit : la demande REQUI, ses lignes de COMMANDE, les demandes LPLURI de la même affaire
/// et leurs lignes d'édition. Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026.
/// </summary>
// fix-ok: 500 de cette relecture mesure au banc du parcours le 28/11/2026 (sonde MESURE-LOT7) : « Invalid column name
// 'LGEDT_CODE_MODELE_DOC' », la v2.LIGNE_EDITION du banc n'avait pas la colonne ; corrige dans le Jeu de la sonde et dans
// EcritureRequisitions.LigneEditionAsync. Le filtre des demandes essaye ici a ete retire : la mesure l'a refute (0 autre demande).
public static class LectureCommandesRequi
{
    private const string Demande = """
        SELECT d.DMND_ID_DMND, d.DMND_NUM_DEMANDE, d.DMND_NOM, d.DMND_ETAT_DEMANDE, d.DMND_EN_COURS, d.DMND_ID_DSRCS, d.DMND_ID_AFFRE
        FROM v2.DEMANDE AS d WHERE d.DMND_NUM_DEMANDE = @numero AND d.DMND_CODE_PROSS = 'REQUI'
        """;

    private const string Lignes = """
        SELECT CMMDE_TYPE, CMMDE_QTE, CMMDE_ID_DSRCS, CMMDE_GUICHET FROM v2.COMMANDE WHERE CMMDE_ID_DMND = @id ORDER BY CMMDE_ID_CMMDE
        """;

    private const string Lpluri = """
        SELECT d.DMND_ID_DMND, d.DMND_NUM_DEMANDE, d.DMND_NOM, d.DMND_ETAT_DEMANDE, d.DMND_EN_COURS, d.DMND_ID_DSRCS, d.DMND_ID_AFFRE
        FROM v2.DEMANDE AS d WHERE d.DMND_ID_AFFRE = @affaire AND d.DMND_CODE_PROSS = 'LPLURI' ORDER BY d.DMND_ID_DMND
        """;

    private const string Editions = """
        SELECT LGEDT_ID_DMND, LGEDT_CODE_MODELE_DOC, LGEDT_NOMBRE, LGEDT_DESTINATION_PHYSIQUE FROM v2.LIGNE_EDITION
        WHERE LGEDT_ID_DMND = @id ORDER BY LGEDT_NUM_ORDRE, LGEDT_ID_LGEDT
        """;

    public static async Task<CommandeRequiLue> LireAsync(SqlConnection baseGreffe, CommandeRequiDemandee demandee, CancellationToken ct)
    {
        var requi = await DemandesAsync(baseGreffe, Demande, new SqlParameter("@numero", demandee.NumeroDemande.Trim()), ct);
        if (requi.Count == 0) return new CommandeRequiLue(null, [], []);
        var (idRequi, affaire, lue) = requi[0];

        var lignes = new List<LigneCommandeRequiLue>();
        await using (var r = await LectureV2.LireAsync(baseGreffe, Lignes, ct, new SqlParameter("@id", idRequi)))
            while (await r.ReadAsync(ct))
                lignes.Add(new LigneCommandeRequiLue(Chaine(r, "CMMDE_TYPE"), r["CMMDE_QTE"] is DBNull ? 0 : Convert.ToInt32(r["CMMDE_QTE"], CultureInfo.InvariantCulture),
                    r["CMMDE_ID_DSRCS"] is int d ? d : null, r["CMMDE_GUICHET"] is true));

        var lpluri = affaire is { } a ? await DemandesAsync(baseGreffe, Lpluri, new SqlParameter("@affaire", a), ct) : [];
        return new CommandeRequiLue(lue, [.. lignes], [.. lpluri.Select(x => x.Lue)]);
    }

    private static async Task<List<(int Id, int? Affaire, DemandeRequiLue Lue)>> DemandesAsync(SqlConnection cn, string sql, SqlParameter cle,
                                                                                               CancellationToken ct)
    {
        var lues = new List<(int Id, int? Affaire, DemandeRequiLue Lue)>();
        await using (var r = await LectureV2.LireAsync(cn, sql, ct, cle))
            while (await r.ReadAsync(ct))
                lues.Add((Convert.ToInt32(r["DMND_ID_DMND"], CultureInfo.InvariantCulture), r["DMND_ID_AFFRE"] is int a ? a : null,
                          new DemandeRequiLue(Chaine(r, "DMND_NUM_DEMANDE"), Chaine(r, "DMND_NOM"), Chaine(r, "DMND_ETAT_DEMANDE"),
                                              r["DMND_EN_COURS"] is true, r["DMND_ID_DSRCS"] is int d ? d : null, [])));
        for (var i = 0; i < lues.Count; i++)
        {
            var editions = new List<EditionRequiLue>();
            await using (var r = await LectureV2.LireAsync(cn, Editions, ct, new SqlParameter("@id", lues[i].Id)))
                while (await r.ReadAsync(ct))
                    editions.Add(new EditionRequiLue(Chaine(r, "LGEDT_CODE_MODELE_DOC"), Convert.ToInt32(r["LGEDT_NOMBRE"], CultureInfo.InvariantCulture),
                                                     Chaine(r, "LGEDT_DESTINATION_PHYSIQUE")));
            lues[i] = lues[i] with { Lue = lues[i].Lue with { Editions = [.. editions] } };
        }
        return lues;
    }
}
