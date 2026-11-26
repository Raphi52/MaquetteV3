using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Nouvelle formalité » (PROC_NF, forme Saisie) : les saisies d'ouverture de procedure collective sur une instance (demandes
/// NF). Modes d'emploi : LecteurEcran.cs ; le geste : EcrivainNouvellesFormalites.cs. Plan « RIG v3 100 % fonctionnel », lot 5,
/// 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_NF, code processus NF (PROC_NF.Designer.cs:348) ; la demande est celle de l'instance (MetierBase),
/// liee par son verrou (DOSSIER_DEMANDE 2J, Lock, PROC_NF.cs:55), son indicatif = le numero de role (PROC_NF.cs:52,
/// Instance.cs:420-423). Ecartees : supprimees (X) et terminees (T), comme les autres saisies de PC (DepotsPcV2.Requete).
/// Une seule famille : « Nouvelle formalité (NF) ».
///
/// REGLE D'ETAT et MARCHES (rangs du descripteur : 0 Instance, 1 Procédure collective) : validee (V) → « Ouverte », les deux ;
/// sinon l'instance seule si la demande en tient une (lien 2J : « En cours », interrompue ou en attente), aucune sinon (« À ouvrir »).
/// La PC ouverte se dit par son numero (PRCLL_NUM_PC de l'instance), ou « sans numéro ». ORDRE : la plus recente d'abord.
/// </summary>
internal sealed class LecteurNouvellesFormalites : ILecteurEcran
{
    public string Procedure => "PROC_NF";

    internal const string Groupe = "Nouvelle formalité (NF)";

    private const string Requete = """
        SELECT TOP (@plafond) d.DMND_ID_DMND, d.DMND_NUM_DEMANDE, d.DMND_ETAT_DEMANDE, d.DMND_NOM, d.DMND_NUM_INDICATIF,
               o.DSSDM_VALEUR_SEQUENCEUR AS Instance2J, pc.PRCLL_NUM_PC, CONVERT(bit, CASE WHEN pc.PRCLL_ID_PRCLL IS NULL THEN 0 ELSE 1 END) AS APc,
               COUNT(*) OVER () AS Total
        FROM v2.DEMANDE AS d
        OUTER APPLY (SELECT TOP (1) dd.DSSDM_VALEUR_SEQUENCEUR FROM v2.DOSSIER_DEMANDE AS dd
                     WHERE dd.DSSDM_ID_DMND = d.DMND_ID_DMND AND dd.DSSDM_CODE_SEQUENCEUR = '2J' ORDER BY dd.DSSDM_ID_DSSDM) AS o
        LEFT JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = TRY_CONVERT(int, o.DSSDM_VALEUR_SEQUENCEUR)
        LEFT JOIN v2.PROCEDURE_COLLECTIVE AS pc ON pc.PRCLL_ID_PRCLL = i.INSTN_ID_PRCLL
        WHERE d.DMND_CODE_PROSS = 'NF' AND d.DMND_ETAT_DEMANDE NOT IN ('X', 'T')
        ORDER BY d.DMND_ID_DMND DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var etatV2 = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE");
        var aInstance = r["Instance2J"] is not DBNull;
        var (etat, faites) = etatV2 == "V" ? ("Validee", new[] { 0, 1 })
            : aInstance ? ("EnCours", new[] { 0 }) : ("AFaire", Array.Empty<int>());
        var indicatif = FormeLigne.Texte(r, "DMND_NUM_INDICATIF");
        var numeroPc = FormeLigne.Texte(r, "PRCLL_NUM_PC");
        var precisions = FormeLigne.Joindre(
            indicatif.Length > 0 ? $"Instance {indicatif}" : $"demande {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}",
            r["APc"] is true ? (numeroPc.Length > 0 ? $"PC {numeroPc}" : "PC sans numéro") : null,
            etatV2 switch { "I" => "interrompue", "O" => "refusée", _ => null });
        var nom = FormeLigne.Texte(r, "DMND_NOM");
        return new LigneLue(nom.Length > 0 ? nom : $"Demande {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}", precisions, Groupe, etat, faites)
        { Id = FormeLigne.Texte(r, "DMND_ID_DMND") };
    }
}
