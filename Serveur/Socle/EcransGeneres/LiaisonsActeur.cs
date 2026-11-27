using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// Ce qui REFERENCE un acteur judiciaire -- juge, parquetier, greffier assermente, mandataire de procedure collective, intervenant de
/// procedure collective -- dans la base du greffe, table par table, avec la colonne que « Remplacer un acteur » reporte sur le remplacant.
/// Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026. Lu par LecteurRemplacementActeur.cs (les acteurs lies, l'apercu) et
/// EcrivainRemplacementActeur.cs (la reprise).
///
/// SOURCE (ancien RIG) : les listes GetLiaisonEntite de chaque acteur (D:\RigApplication\Source\Outils\DLL\RigMetier\JUDICIAIRE\) --
/// Juge.cs (12 liaisons), Parquetier.cs (2), GreffierAssermente.cs (4), MandatairePC.cs (7), IntervenantPCReper.cs (1) -- et la colonne que
/// OPE_SUPP_ACTEUR_JUD._UpdateLink reecrit pour chacune (D:\RigApplication\Source\RIG\DLL\Processus\PROC_SUPP_ACTEUR_JUD\
/// OPE_SUPP_ACTEUR_JUD.cs:796-985). Les libelles sont ceux de la V2 (TEXTE_SIEGER, TEXTE_INSTANCE..., l. 15-30). Un type de deposant GED
/// est celui que GetLiaisonEntite passe (« JUGE » ; « MANDATAIRE_PC » ou « ADMINISTRATEUR »). Pour un greffier, la V2 liste les decisions
/// par DCJDC_ID_GRASS et ne reecrit que cette colonne (l. 845-852) : la V3 fait de meme.
/// </summary>
internal static class LiaisonsActeur
{
    /// <summary>Une liaison : le libelle de la V2, la table, sa cle, la colonne qui porte l'acteur, et les types de deposant admis.</summary>
    internal sealed record Liaison(string Libelle, string Table, string Cle, string Colonne, string? ColonneType = null, string[]? Types = null)
    {
        /// <summary>La condition SQL sur l'acteur <paramref name="acteur"/> (une expression : « @id » ou « a.JUG_ID_JUG »).</summary>
        public string Condition(string acteur)
            => ColonneType is null ? $"{Colonne} = {acteur}"
                                   : $"{ColonneType} IN ({string.Join(", ", Types!.Select(t => $"'{t}'"))}) AND {Colonne} = {acteur}";
    }

    /// <summary>Un type d'acteur : sa table, sa cle, son nom, son abrege, sa colonne « actif », son groupe d'ecran et ses liaisons.</summary>
    /// <param name="Code">Le code du type dans l'identifiant d'une ligne (la table V2, celle qu'ACTEUR_SUPP nomme).</param>
    /// <param name="Libelle">Le mot de la V2 (TEXTE_JUGE, TEXTE_PARQUTIER..., OPE_SUPP_ACTEUR_JUD.cs:7-12), avec sa majuscule.</param>
    /// <param name="Aucun">La fin du message V2 « Vous n'avez pas sélectionné ... » (DoVerifier, l. 117-175).</param>
    /// <param name="LeMeme">Le milieu du message V2 « Vous ne pouvez pas sélectionner le même ... que celui que vous allez supprimer ».</param>
    /// <param name="Disparu">Le message V2 du remplacant supprime entre-temps (null : la V2 n'a pas ce controle pour ce type).</param>
    internal sealed record TypeActeur(string Code, string Libelle, string Groupe, string Table, string Cle, string Nom, string Abrege, string Actif,
                                      string Aucun, string LeMeme, string? Disparu, Liaison[] Liaisons);

    internal const string Magistrats = "Magistrats et greffe", MandatairesPc = "Mandataires PC", IntervenantsPc = "Intervenants PC";

    private static readonly string[] DeposantJuge = ["JUGE"], DeposantMandatairePc = ["MANDATAIRE_PC", "ADMINISTRATEUR"];

    internal static readonly TypeActeur[] Types =
    [
        new("JUGE", "Juge", Magistrats, "JUGE", "JUG_ID_JUG", "CONCAT_WS(' ', a.JUG_NOM, a.JUG_PRENOM)", "a.JUG_ABREGE", "a.JUG_ACTIF",
            "de juge", "même juge", null,
        [
            new("Siéger", "SIEGER", "SGR_ID_SGR", "SGR_ID_JUG"),
            new("Audience cabinet", "AUDIENCE_CABINET", "AUDNC_ID_ADNC", "AUDNC_ID_JUG"),
            new("Instance", "INSTANCE", "INSTN_ID_INSTN", "INSTN_ID_JUG"),
            new("Décision judiciaire", "DECISION_JUDICIAIRE", "DCJDC_ID_DCJDC", "DCJDC_ID_JUG"),
            new("Décision déposant GED", "DECISION_JUDICIAIRE", "DCJDC_ID_DCJDC", "DCJDC_ID_DEPOSANT_GED", "DCJDC_TYPE_DEPOSANT_GED", DeposantJuge),
            new("Juge PC", "ETRE_JUGE_COMM", "ETJCM_ID_ETJCM", "ETJCM_ID_JUG"),
            new("Mesure", "MESURE", "MSINS_ID_MSINS", "MSINS_ID_JUG"),
            new("Juge instance", "INSTANCE_JUGE", "JUGIN_ID_JUGIN", "JUGIN_ID_JUG"),
            new("Evènement procédure déposant GED", "EVENEMENT_PROCEDURE", "EVNPR_ID_EVNPR", "EVNPR_ID_DEPOSANT_GED", "EVNPR_TYPE_DEPOSANT_GED", DeposantJuge),
            new("Vidiged index", "VIDIGED_INDEX", "VGIDX_ID_VGIDX", "VGIDX_ID_DEPOSANT", "VGIDX_TYPE_DEPOSANT", DeposantJuge),
            new("Signataire REDA", "DOCUMENT_DOSSIER", "DCMDS_ID_DCMDS", "DCMDS_ID_JUG"),
            new("Coffre reçu", "DEMAT_CFEC_AJMJ_VIDIGED", "CFECAJV_ID_CFECAJV", "CFECAJV_ID_DEPOSANT", "CFECAJV_TYPE_DEPOSANT", DeposantJuge),
        ]),
        new("PARQUETIER", "Parquetier", Magistrats, "PARQUETIER", "PRQTR_ID_PRQTR", "CONCAT_WS(' ', a.PRQTR_NOM, a.PRQTR_PRENOM)", "a.PRQTR_ABREGE",
            "a.PRQTR_ACTIF", "de parquetier", "même parquetier", "Le parquetier que vous avez choisi n'existe plus, il a été supprimé",
        [
            new("Composition", "COMPOSITION", "CMPST_ID_CMAUD", "CMPST_ID_PRQTR"),
            new("Audience cabinet", "AUDIENCE_CABINET", "AUDNC_ID_ADNC", "AUDNC_ID_PRQTR"),
        ]),
        new("GREFFIER_ASSERMENTE", "Greffier assermenté", Magistrats, "GREFFIER_ASSERMENTE", "GRASS_ID_GRASS", "CONCAT_WS(' ', a.GRASS_NOM, a.GRASS_PRENOM)",
            "a.GRASS_ABREGE", "a.GRASS_ACTIF", "de greffier assermenté", "même greffier assermenté",
            "Le greffier que vous avez choisi n'existe plus, il a été supprimé",
        [
            new("Composition", "COMPOSITION", "CMPST_ID_CMAUD", "CMPST_ID_GRASS"),
            new("Audience cabinet", "AUDIENCE_CABINET", "AUDNC_ID_ADNC", "AUDNC_ID_GRASS"),
            new("Décision judiciaire", "DECISION_JUDICIAIRE", "DCJDC_ID_DCJDC", "DCJDC_ID_GRASS"),
            new("Signataire REDA", "DOCUMENT_DOSSIER", "DCMDS_ID_DCMDS", "DCMDS_ID_GRASS"),
        ]),
        new("MANDATAIRE_PC", "Mandataire PC", MandatairesPc, "MANDATAIRE_PC", "MNDPC_ID_MNDPC", "CONCAT_WS(' ', a.MNDPC_NOM, a.MNDPC_COMPLEMENT_NOM)",
            "a.MNDPC_ABREGE", "a.MNDPC_ACTIF", "de mandataire PC", "même mandataire PC",
            "Le mandataire PC que vous avez choisi n'existe plus, il a été supprimé",
        [
            new("Mandataire PC", "GERER_PC", "GRRPC_ID_GRRPC", "GRRPC_ID_MNDPC"),
            new("Evènement procédure déposant GED", "EVENEMENT_PROCEDURE", "EVNPR_ID_EVNPR", "EVNPR_ID_DEPOSANT_GED", "EVNPR_TYPE_DEPOSANT_GED", DeposantMandatairePc),
            new("Vidiged index", "VIDIGED_INDEX", "VGIDX_ID_VGIDX", "VGIDX_ID_DEPOSANT", "VGIDX_TYPE_DEPOSANT", DeposantMandatairePc),
            new("Démat CFEC", "DEMAT_CFEC", "CFEC_ID_CFEC", "CFEC_ID_MANDATAIRE_PC"),
            new("Payeur", "PAYEUR", "PYEUR_ID_PYEUR", "PYEUR_ID_MNDPC"),
            new("Décision déposant GED", "DECISION_JUDICIAIRE", "DCJDC_ID_DCJDC", "DCJDC_ID_DEPOSANT_GED", "DCJDC_TYPE_DEPOSANT_GED", DeposantMandatairePc),
            new("Coffre reçu", "DEMAT_CFEC_AJMJ_VIDIGED", "CFECAJV_ID_CFECAJV", "CFECAJV_ID_DEPOSANT", "CFECAJV_TYPE_DEPOSANT", DeposantMandatairePc),
        ]),
        // Pas de colonne « actif » : la V2 propose tout intervenant PC (CboUtilisableEnSaisie => true, IntervenantPCReper.cs:310-314).
        // fix-ok: mesure au banc le 27/11/2026 -- cet Actif constant « 1 », trie par EcrivainRemplacementActeur.ResoudreAsync, rendait
        // « A constant expression was encountered in the ORDER BY list » (erreur 500, blocs 7-8 de PROC_SUPP_ACTEUR_JUD.gestes) : le tri
        // l'ecarte ; les deux retouches precedentes de ce fichier etaient la condition EXISTS (alias inutile) et ce commentaire.
        new("INTERVENANT_PC_REPER", "Intervenant PC", IntervenantsPc, "INTERVENANT_PC_REPER", "INTRPR_ID_INTRPR",
            "CONCAT_WS(' ', a.INTRPR_NOM, a.INTRPR_COMPLEMENT_NOM)", "a.INTRPR_ABREGE", "1", "d'intervenant PC reper", "même intervenant PC reper",
            "L'intervenant PC que vous avez choisi n'existe plus, il a été supprimé",
        [
            new("Intervenant PC", "INTERVENANT_PC", "INTRP_ID_INTRP", "INTRP_ID_INTRPR"),
        ]),
    ];

    /// <summary>Le type d'acteur de code <paramref name="code"/> (« JUGE »...) ; null s'il n'en est pas un.</summary>
    internal static TypeActeur? Type(string code) => Types.FirstOrDefault(t => t.Code == code);

    /// <summary>« au moins une liaison » pour l'acteur de la ligne « a » (la cle du type).</summary>
    internal static string AuMoinsUne(TypeActeur t)
        => string.Join(" OR ", t.Liaisons.Select(l => $"EXISTS (SELECT 1 FROM v2.{l.Table} WHERE {l.Condition($"a.{t.Cle}")})"));

    /// <summary>Le nombre de lignes de chaque liaison de l'acteur <c>@id</c>, une ligne par liaison, dans l'ordre de la V2.</summary>
    internal static async Task<(Liaison Liaison, int Nombre)[]> CompterAsync(TypeActeur t, Func<string, SqlParameter[], Task<SqlDataReader>> lire, int id,
                                                                             CancellationToken ct)
    {
        var sql = string.Join("\nUNION ALL ", t.Liaisons.Select((l, i) => $"SELECT {i} AS Rang, COUNT(*) AS Nombre FROM v2.{l.Table} WHERE {l.Condition("@id")}"))
                  + "\nORDER BY Rang";
        var comptes = new List<(Liaison, int)>();
        await using var r = await lire(sql, [new SqlParameter("@id", id)]);
        while (await r.ReadAsync(ct)) comptes.Add((t.Liaisons[r.GetInt32(0)], r.GetInt32(1)));
        return [.. comptes];
    }

    /// <summary>
    /// Les affaires touchees par le remplacement, pour l'apercu : les instances d'un juge (instance, juge d'instance, mesure, decision),
    /// les procedures collectives d'un mandataire ou d'un intervenant PC, les audiences d'un parquetier ou d'un greffier. Deux colonnes :
    /// le numero (role, PC, date) et le libelle.
    /// </summary>
    internal static string RequeteAffaires(TypeActeur t) => t.Code switch
    {
        "JUGE" => """
            SELECT i.INSTN_NUM_ROLE AS Numero, i.INSTN_NOM_COURANT AS Libelle FROM v2.INSTANCE AS i
            WHERE i.INSTN_ID_JUG = @id OR i.INSTN_ID_INSTN IN (SELECT JUGIN_ID_INSTN FROM v2.INSTANCE_JUGE WHERE JUGIN_ID_JUG = @id)
               OR i.INSTN_ID_INSTN IN (SELECT MSINS_ID_INSTN FROM v2.MESURE WHERE MSINS_ID_JUG = @id)
               OR i.INSTN_ID_INSTN IN (SELECT DCJDC_ID_INSTN FROM v2.DECISION_JUDICIAIRE WHERE DCJDC_ID_JUG = @id)
            ORDER BY i.INSTN_NUM_ROLE, i.INSTN_ID_INSTN
            """,
        "MANDATAIRE_PC" => """
            SELECT p.PRCLL_NUM_PC AS Numero, CONCAT('procédure collective ', p.PRCLL_ID_PRCLL) AS Libelle FROM v2.PROCEDURE_COLLECTIVE AS p
            WHERE p.PRCLL_ID_PRCLL IN (SELECT GRRPC_ID_PRCLL FROM v2.GERER_PC WHERE GRRPC_ID_MNDPC = @id)
            ORDER BY p.PRCLL_NUM_PC, p.PRCLL_ID_PRCLL
            """,
        "INTERVENANT_PC_REPER" => """
            SELECT p.PRCLL_NUM_PC AS Numero, CONCAT('procédure collective ', p.PRCLL_ID_PRCLL) AS Libelle FROM v2.PROCEDURE_COLLECTIVE AS p
            WHERE p.PRCLL_ID_PRCLL IN (SELECT INTRP_ID_PRCLL FROM v2.INTERVENANT_PC WHERE INTRP_ID_INTRPR = @id)
            ORDER BY p.PRCLL_NUM_PC, p.PRCLL_ID_PRCLL
            """,
        "PARQUETIER" => """
            SELECT CONVERT(varchar(10), a.AUDNC_DATE, 103) AS Numero, CONCAT('audience ', a.AUDNC_HEURE) AS Libelle FROM v2.AUDIENCE_CABINET AS a
            WHERE a.AUDNC_ID_PRQTR = @id OR a.AUDNC_ID_ADNC IN (SELECT CMPST_ID_ADNC FROM v2.COMPOSITION WHERE CMPST_ID_PRQTR = @id)
            ORDER BY a.AUDNC_DATE, a.AUDNC_ID_ADNC
            """,
        _ => """
            SELECT CONVERT(varchar(10), a.AUDNC_DATE, 103) AS Numero, CONCAT('audience ', a.AUDNC_HEURE) AS Libelle FROM v2.AUDIENCE_CABINET AS a
            WHERE a.AUDNC_ID_GRASS = @id OR a.AUDNC_ID_ADNC IN (SELECT CMPST_ID_ADNC FROM v2.COMPOSITION WHERE CMPST_ID_GRASS = @id)
            ORDER BY a.AUDNC_DATE, a.AUDNC_ID_ADNC
            """,
    };
}
