using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Actes web non transmis » (PROC_ACTREJ_CMD_WEB) : les actes deposes qu'une commande web attend et qui ne sont pas
/// encore dans la GED. Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_ACTREJ_CMD_WEB\PROC_ACTREJ_CMD_WEB\
/// PROC_ACTREJ_CMD_WEB.cs, _SqlBase (l. 36-48, FDDA 16939) : DEPOT_RCS JOIN ACTE_DEPOSE (ACTDP_ID_DPTRC) JOIN ACTION_RCS
/// (ACTRC_ID_DPTRC) JOIN DOSSIER_RCS LEFT JOIN ENTREPRISE, ou ACTDP_CMDWEB_FLUX_ID IS NOT NULL (l'acte est commande par
/// le web) AND ACTDP_GUID_VIDIGED IS NULL (il n'est pas encore range dans la GED). La description de l'acte :
/// ACTDP_DESCRIPTION, sinon le libelle de son abrege de piece (ABREGE_PIECE.R_ABREGEPIEC_DEVELOPPE_EDIT), le meme CASE.
/// A l'ouverture, aucun critere (RefreshRejets, l. 53-64 : numero de gestion et de depot vides). Dans la base V3,
/// DOSSIER_RCS est rec.DossierRcs, ENTREPRISE rec.Entreprise, ACTION_RCS rec.ActionRegistre (IdDepot) ; DEPOT_RCS,
/// ACTE_DEPOSE et ABREGE_PIECE sont les tables V2 telles quelles (v2.*).
/// Ecart assume : la V2 joint ACTION_RCS ligne a ligne, un depot a deux actions y montrait deux fois l'acte ; ici une
/// ligne par acte (la premiere action du depot qui a un dossier). Ordre : la V2 n'en impose aucun (pas d'ORDER BY) ;
/// ici la commande la plus recente en tete.
///
/// GROUPES : « Commandes du jour » = commande (ACTDP_CMDWEB_DATE) du jour de @aujourdhui ; sinon « Commandes plus
/// anciennes ».
///
/// REGLE D'ETAT. Toute ligne est « A transmettre » (AFaire, aucune marche) : la liste de la V2 ne garde QUE les actes
/// encore hors de la GED. Les deux gestes la vident : « Envoyer au VGED » range l'acte (ACTDP_GUID_VIDIGED pose,
/// OPE_RESULTATS.cs:63-79) et « Annuler » efface la commande (ACTDP_CMDWEB_FLUX_ID = NULL, OPE_RESULTATS.cs:84-117).
/// L'etat du flux de la commande (annule, en cours) est dans la base EDI du greffe (FluxEntrant, CodeGreffeBaseEdi),
/// pas dans la base V3 : il n'est pas lu.
/// </summary>
internal sealed class LecteurActesWebNonTransmis : ILecteurEcran
{
    public string Procedure => "PROC_ACTREJ_CMD_WEB";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string DuJour = "Commandes du jour", PlusAnciennes = "Commandes plus anciennes";

    private const string Requete = """
        SELECT TOP (@plafond) ad.ACTDP_CMDWEB_DATE, p.DPTRC_NUM_DEPOT, s.NumeroGestion, s.Siren, e.Denomination,
               CASE WHEN ad.ACTDP_DESCRIPTION <> '' THEN ad.ACTDP_DESCRIPTION
                    ELSE (SELECT ap.R_ABREGEPIEC_DEVELOPPE_EDIT FROM v2.ABREGE_PIECE AS ap
                          WHERE ap.R_ABREGEPIEC_VALEUR = ad.ACTDP_ABREGE_PIECE) END AS Description,
               CASE WHEN CAST(ad.ACTDP_CMDWEB_DATE AS date) = CAST(@aujourdhui AS date) THEN 1 ELSE 0 END AS DuJour,
               COUNT(*) OVER () AS Total
        FROM v2.DEPOT_RCS AS p
        JOIN v2.ACTE_DEPOSE AS ad ON ad.ACTDP_ID_DPTRC = p.DPTRC_ID_DPTRC
        CROSS APPLY (SELECT TOP (1) d.NumeroGestion, d.Siren, d.IdEntreprise FROM rec.ActionRegistre AS a
                     JOIN rec.DossierRcs AS d ON d.Id = a.IdDossier
                     WHERE a.IdDepot = p.DPTRC_ID_DPTRC ORDER BY a.Id) AS s
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        WHERE ad.ACTDP_CMDWEB_FLUX_ID IS NOT NULL AND ad.ACTDP_GUID_VIDIGED IS NULL
        ORDER BY ad.ACTDP_CMDWEB_DATE DESC, ad.ACTDP_ID_ACDPS DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = aujourdhui });

    private static LigneLue Ligne(SqlDataReader r)
    {
        var commande = FormeLigne.DateDe(r, "ACTDP_CMDWEB_DATE");
        var siren = FormeLigne.Siren(FormeLigne.Texte(r, "Siren"));
        var nom = FormeLigne.Joindre(FormeLigne.Texte(r, "Denomination"), FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion")));
        var precisions = FormeLigne.Joindre($"Dépôt {FormeLigne.Texte(r, "DPTRC_NUM_DEPOT")}", FormeLigne.Texte(r, "Description"),
                                            siren.Length > 0 ? $"SIREN {siren}" : null,
                                            commande is null ? null : $"commande du {FormeLigne.Date(commande)}");
        // La regle d'etat (en tete du fichier) : toute ligne de la liste est a transmettre, aucune marche franchie.
        return new LigneLue(nom, precisions, Convert.ToInt32(r["DuJour"]) == 1 ? DuJour : PlusAnciennes, "AFaire", []);
    }
}
