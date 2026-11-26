using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Dépôts bénéficiaires » (PROC_DASBEN, forme Saisie) : les saisies de depot du document relatif au beneficiaire effectif.
/// Modes d'emploi : LecteurEcran.cs ; le commun : DepotsRcsV2.cs ; le geste : EcrivainDepotsBeneficiaires.cs. Plan « RIG v3
/// 100 % fonctionnel », lot 5, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_DASBEN (fiche proc_dasben.md) -- une saisie est une DEMANDE de code processus DASBEN
/// (ACTRC_CODE_PROSS = "DASBEN", PROC_DASBEN.cs:1044) ; son depot est celui de son action du registre. GROUPES : « Non-inscrit »
/// si la demande porte le non-inscrit (DMND_ID_AUTRE, PROC_DASBEN\ETP_DEPOT\ETP_DEPOT.cs:92-95), sinon « Dossier RCS ».
///
/// REGLE D'ETAT (DepotsRcsV2.Ligne) et MARCHES (rangs du descripteur : 0 Entreprise, 2 Actes, 3 Acte déposé obligatoires ;
/// 5 Numérotation) : validee (V) → « Enregistré », les obligatoires et le numero ; sinon les marches lues (entreprise, numero
/// de depot, evenement, acte). Pas d'etat refuse a l'ecran : la fiche n'en donne pas (descripteur). La copie de RIG_DEV n'a
/// aucune demande DASBEN (lu le 26/11/2026) : la liste est vide tant que la V3 n'en cree pas.
/// </summary>
internal sealed class LecteurDepotsBeneficiaires : ILecteurEcran
{
    public string Procedure => "PROC_DASBEN";

    private static readonly string Requete = DepotsRcsV2.Requete("DASBEN");

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, r => DepotsRcsV2.Ligne(r, [0, 2, 3, 5], 0, 5, 2, 3, null), ct,
            new SqlParameter("@plafond", plafond));
}
