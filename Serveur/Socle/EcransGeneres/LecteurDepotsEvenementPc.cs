using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Dépôts d'événement PC » (PROC_DEP, forme Saisie) : les saisies de depot d'evenement sur une procedure collective (DEPPC)
/// ou une prevention (DEPPR). Modes d'emploi : LecteurEcran.cs ; le commun : DepotsPcV2.cs ; le geste :
/// EcrivainDepotsEvenementPc.cs. Plan « RIG v3 100 % fonctionnel », lot 5, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_DEP (fiche proc_dep.md) -- deux processus : PROC_DEPPC (MetierBase = la PC, PROC_DEPPC.cs:15) et
/// PROC_DEPPRV (la prevention) ; une saisie est une DEMANDE DEPPC ou DEPPR portee par sa PC (DOSSIER_DEMANDE 2P,
/// ProcedureCollective.cs:812) ou sa prevention (2R). GROUPES : « Prévention » pour DEPPR ou une demande portee en 2R, sinon
/// « Procédure collective ».
///
/// REGLE D'ETAT et MARCHES (rangs du descripteur : 0 Procédure, 2 Événement survenu, 4 Facturation obligatoires) : validee (V)
/// → « Enregistré », les trois ; en attente (W) → « Verrouillée » ; sinon la procedure seule si la demande la porte (« En
/// cours »), aucune sinon (« À saisir »). Le depot ne se rattache pas a la demande dans RIG v2 (il pend a l'evenement de la PC) :
/// il n'est pas relu ici, le message du geste le donne. ORDRE : la plus recente d'abord.
/// </summary>
internal sealed class LecteurDepotsEvenementPc : ILecteurEcran
{
    public string Procedure => "PROC_DEP";

    internal const string GroupePc = "Procédure collective", GroupePrevention = "Prévention";

    private static readonly string Requete = DepotsPcV2.Requete("'DEPPC', 'DEPPR'", filles: false);

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var etatV2 = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE");
        var sequenceur = FormeLigne.Texte(r, "Sequenceur");
        var prevention = FormeLigne.Texte(r, "DMND_CODE_PROSS") == "DEPPR" || sequenceur == "2R";
        var (etat, faites) = etatV2 switch
        {
            "V" => ("Validee", new[] { 0, 2, 4 }),
            "W" => ("EnAttente", sequenceur.Length > 0 ? new[] { 0 } : []),
            _ => sequenceur.Length > 0 ? ("EnCours", new[] { 0 }) : ("AFaire", Array.Empty<int>()),
        };
        var indicatif = FormeLigne.Texte(r, "DMND_NUM_INDICATIF");
        var precisions = FormeLigne.Joindre(
            indicatif.Length == 0 ? $"demande {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}" : (prevention ? $"Prévention {indicatif}" : $"Procédure {indicatif}"),
            etatV2 switch { "I" => "interrompu", "W" => "en attente", _ => null });
        var nom = FormeLigne.Texte(r, "DMND_NOM");
        return new LigneLue(nom.Length > 0 ? nom : $"Demande {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}", precisions,
            prevention ? GroupePrevention : GroupePc, etat, faites)
        { Id = FormeLigne.Texte(r, "DMND_ID_DMND") };
    }
}
