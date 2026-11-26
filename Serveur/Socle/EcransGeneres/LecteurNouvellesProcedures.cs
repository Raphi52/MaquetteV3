using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Nouvelle proc. collective » (PROC_NPC, forme Saisie) : les saisies de creation de procedure collective (NPC) ou de
/// prevention (NPR). Modes d'emploi : LecteurEcran.cs ; le geste : EcrivainNouvellesProcedures.cs. Plan « RIG v3 100 %
/// fonctionnel », lot 5, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_NPC, deux codes processus (NPC, et NPR en mode prevention : proc_npc.md « Codes processus ») ;
/// une saisie est une DEMANDE NPC ou NPR, portee apres sauvegarde par sa PC (DOSSIER_DEMANDE 2P, ProcedureCollective.cs:812)
/// ou sa prevention (2R), son indicatif = le numero de la PC (PROC_NPC.cs:150-154). Requete commune : DepotsPcV2.Requete
/// (demandes supprimees X et terminees T ecartees). GROUPES : « Prévention (PRV) » pour NPR ou une demande portee en 2R,
/// sinon « Procédure collective (PC) ».
///
/// REGLE D'ETAT et MARCHES (rangs du descripteur : 0 Procédure, 6 Notification CFEC, 7 Facturation) : validee (V) →
/// « Enregistrée », procedure et facturation ; refusee (O, Demande.cs:40-41 : la notification CFEC refusee fait refuser la
/// demande, PROC_NPC.cs:161-163) → « Refusée (CFEC) », la notification ; sinon la procedure seule si la demande la porte
/// (« En cours »), aucune sinon (« À saisir »). Une PC enregistree sans numero (aucune date de jugement) se dit telle.
/// ORDRE : la plus recente d'abord.
/// </summary>
internal sealed class LecteurNouvellesProcedures : ILecteurEcran
{
    public string Procedure => "PROC_NPC";

    internal const string GroupePc = "Procédure collective (PC)", GroupePrevention = "Prévention (PRV)";

    private static readonly string Requete = DepotsPcV2.Requete("'NPC', 'NPR'", filles: false);

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var etatV2 = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE");
        var sequenceur = FormeLigne.Texte(r, "Sequenceur");
        var prevention = FormeLigne.Texte(r, "DMND_CODE_PROSS") == "NPR" || sequenceur == "2R";
        var (etat, faites) = etatV2 switch
        {
            "V" => ("Validee", new[] { 0, 7 }),
            "O" => ("Rejetee", new[] { 6 }),
            _ => sequenceur.Length > 0 ? ("EnCours", new[] { 0 }) : ("AFaire", Array.Empty<int>()),
        };
        var indicatif = FormeLigne.Texte(r, "DMND_NUM_INDICATIF");
        var precisions = FormeLigne.Joindre(
            indicatif.Length > 0 ? (prevention ? $"Prévention {indicatif}" : $"Procédure {indicatif}")
                : sequenceur.Length > 0 ? (prevention ? "Prévention sans numéro" : "Procédure sans numéro")
                : $"demande {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}",
            etatV2 switch { "I" => "interrompue", "O" => "refusée", _ => null });
        var nom = FormeLigne.Texte(r, "DMND_NOM");
        return new LigneLue(nom.Length > 0 ? nom : $"Demande {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}", precisions,
            prevention ? GroupePrevention : GroupePc, etat, faites)
        { Id = FormeLigne.Texte(r, "DMND_ID_DMND") };
    }
}
