using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Dépôts d'actes RCS » (PROC_DAS, forme Saisie) : les saisies de depot d'actes du greffe, sous le formulaire de depot.
/// Modes d'emploi : LecteurEcran.cs ; le commun : DepotsRcsV2.cs ; le geste : EcrivainDepotsActes.cs. Plan « RIG v3 100 %
/// fonctionnel », lot 5, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_DAS (fiche proc_das.md) -- une saisie est une DEMANDE de code processus DAS ; son depot est celui
/// de son action du registre (DMND_ID_ACTRC → ACTION_RCS.ACTRC_ID_DPTRC, ETP_DEPOT.cs:403 et :453). GROUPES : « Non-inscrit »
/// si la demande porte le non-inscrit (DMND_ID_AUTRE, ETP_DEPOT.cs:446-449), sinon « Dossier RCS ».
///
/// REGLE D'ETAT (DepotsRcsV2.Ligne) et MARCHES (rangs du descripteur : 0 Dossier, 1 Dépôt, 2 Évènement RCS, 3 Actes déposés,
/// 5 Facturation obligatoires) : validee (V) → « Enregistré » ; refus (O) → « Refusé » (le chemin refus de ETP_DEPOT.cs:366-385) ;
/// sinon les marches lues (dossier, depot, evenement, acte). ORDRE : la plus recente d'abord.
/// </summary>
internal sealed class LecteurDepotsActes : ILecteurEcran
{
    public string Procedure => "PROC_DAS";

    private static readonly string Requete = DepotsRcsV2.Requete("DAS");

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, r => DepotsRcsV2.Ligne(r, [0, 1, 2, 3, 5], 0, 1, 2, 3, "Rejetee"), ct,
            new SqlParameter("@plafond", plafond));
}
