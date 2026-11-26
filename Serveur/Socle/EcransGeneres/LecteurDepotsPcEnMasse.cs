using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Dépôts PC en masse » (PROC_DEPPCM, forme Saisie) : les numeros de PC des lots de depot de comptes annuels -- une demande
/// DEPPCM par PC, fille de la demande du lot. Modes d'emploi : LecteurEcran.cs ; le commun : DepotsPcV2.cs ; le geste :
/// EcrivainDepotsPcEnMasse.cs. Plan « RIG v3 100 % fonctionnel », lot 5, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_DEPPCM (fiche proc_deppcm.md:3-9 : depots de comptes annuels de PC en masse). Chaque etape porte
/// une PC ; sa demande DEPPCM est creee sur la PC (pc.CreateDemande, OPE_ENTITE_JUD_RECHERCHE_DEPPCM.cs:102-110 ; DOSSIER_DEMANDE
/// 2P) puis rattachee a la demande du lot et validee (CloturerDemande, :379-386). La demande du lot, sans PC, n'est pas une ligne.
/// GROUPE : « Comptes annuels », le seul du descripteur (la fiche, :5).
///
/// REGLE D'ETAT et MARCHES (rangs du descripteur : 0 Numéro de PC, 1 Événement, 3 Dépôt obligatoires) : validee (V) → « Dépôt
/// créé », les trois ; sinon le numero de PC seul (« En cours » : la demande est creee des que la PC est saisie). ORDRE : la plus
/// recente d'abord.
/// </summary>
internal sealed class LecteurDepotsPcEnMasse : ILecteurEcran
{
    public string Procedure => "PROC_DEPPCM";

    internal const string Groupe = "Comptes annuels";

    private static readonly string Requete = DepotsPcV2.Requete("'DEPPCM'", filles: true);

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var etatV2 = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE");
        var (etat, faites) = etatV2 == "V" ? ("Validee", new[] { 0, 1, 3 }) : ("EnCours", new[] { 0 });
        var nom = FormeLigne.Texte(r, "DMND_NOM");
        // Le numero de la demande n'est pas montre : la serie DMD porte le quantieme du jour (structure « LAQC »), la liste la
        // plus recente d'abord suffit a retrouver le depot du jour.
        var precisions = FormeLigne.Joindre($"PC {FormeLigne.Texte(r, "DMND_NUM_INDICATIF")}",
            etatV2 switch { "I" => "interrompue", "Q" => "qualifiée", _ => null });
        return new LigneLue(nom.Length > 0 ? nom : $"PC {FormeLigne.Texte(r, "DMND_NUM_INDICATIF")}", precisions, Groupe, etat, faites)
        { Id = FormeLigne.Texte(r, "DMND_ID_DMND") };
    }
}
