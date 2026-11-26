using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Actes rejetes » (PROC_ACTREJ) : les actes deposes qu'Infogreffe a rejetes a l'integration GEIDE. Mode d'emploi des
/// lecteurs : LecteurEcran.cs ; les gestes : EcrivainActesRejetes.cs. Plan « RIG v3 100 % fonctionnel », lot 8, 26/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_ACTREJ\Metier\MetierActRejets.cs (fiche
/// proc_actrej.md). FROM (l. 36-43) : ACTE_DEPOSE JOIN DEPOT_RCS JOIN ACTION_RCS JOIN DOSSIER_RCS ; la description de l'acte
/// (l. 44-50) : ACTDP_DESCRIPTION, sinon le libelle de son abrege (ABREGE_PIECE.R_ABREGEPIEC_DEVELOPPE_EDIT). WHERE (l. 81,
/// invariant de l'ecran) : ACTDP_EDI_INFOGREFFE_Etat_GEIDE = '9' (eEtatDematGed.RejeteExtelia, ETAT_TRAITMENT.cs:82), les seuls
/// actes rejetes. ORDER BY la date du compte rendu d'integration GEIDE, la plus recente en tete (l. 66). A l'ouverture, les
/// criteres par defaut (dossier, depot et dates vides) ; la limite « 200 resultats » de la fiche n'est jamais un TOP dans la
/// V2 (proc_actrej.md:603) : ici le plafond de l'ecran. Dans la base V3, ACTION_RCS est rec.ActionRegistre (IdDepot),
/// DOSSIER_RCS rec.DossierRcs ; ACTE_DEPOSE, DEPOT_RCS et ABREGE_PIECE sont les tables V2. Ecart assume (celui de
/// LecteurActesWebNonTransmis) : une ligne par acte, avec la premiere action du depot qui a un dossier.
///
/// GROUPE : « Rejets Infogreffe » (le seul de la fiche). REGLE D'ETAT : toute ligne est « Rejete » (AFaire) -- la liste ne
/// garde que l'etat 9. Les deux gestes de la V2 la font SORTIR de la liste a la relecture (RefreshRejets, OPE_RESULTATS.cs:120,
/// 129) : « Renvoyer a Infogreffe » la remet a l'etat 1 (Prepare), « Annuler le rejet » a 99 (AnnulationAmitel). Id =
/// ACTDP_ID_ACDPS, la cle des gestes (colIdActeDepose, OPE_RESULTATS.cs:134).
/// </summary>
internal sealed class LecteurActesRejetes : ILecteurEcran
{
    public string Procedure => "PROC_ACTREJ";

    /// <summary>Le groupe du descripteur, au mot pres.</summary>
    internal const string Rejets = "Rejets Infogreffe";

    /// <summary>L'etat GEIDE d'un acte rejete par Infogreffe (eEtatDematGed.RejeteExtelia, ETAT_TRAITMENT.cs:82).</summary>
    internal const string EtatRejete = "9";

    private const string Requete = """
        SELECT TOP (@plafond) ad.ACTDP_ID_ACDPS, ad.ACTDP_EDI_INFOGREFFE_Date_Compte_Rendu_Integration_GEIDE AS DateRejet,
               ad.ACTDP_EDI_INFOGREFFE_Date_Envoi_GEIDE AS DateEnvoi, ad.ACTDP_EDI_INFOGREFFE_Motif_Rejet_GEIDE AS Motif,
               p.DPTRC_NUM_DEPOT, s.NumeroGestion, e.Denomination,
               CASE WHEN ad.ACTDP_DESCRIPTION <> '' THEN ad.ACTDP_DESCRIPTION
                    ELSE (SELECT ap.R_ABREGEPIEC_DEVELOPPE_EDIT FROM v2.ABREGE_PIECE AS ap
                          WHERE ap.R_ABREGEPIEC_VALEUR = ad.ACTDP_ABREGE_PIECE) END AS Description,
               COUNT(*) OVER () AS Total
        FROM v2.ACTE_DEPOSE AS ad
        JOIN v2.DEPOT_RCS AS p ON p.DPTRC_ID_DPTRC = ad.ACTDP_ID_DPTRC
        CROSS APPLY (SELECT TOP (1) d.NumeroGestion, d.IdEntreprise FROM rec.ActionRegistre AS a
                     JOIN rec.DossierRcs AS d ON d.Id = a.IdDossier
                     WHERE a.IdDepot = p.DPTRC_ID_DPTRC ORDER BY a.Id) AS s
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        WHERE ad.ACTDP_EDI_INFOGREFFE_Etat_GEIDE = '9'
        ORDER BY ad.ACTDP_EDI_INFOGREFFE_Date_Compte_Rendu_Integration_GEIDE DESC, ad.ACTDP_ID_ACDPS DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var nom = FormeLigne.Joindre(FormeLigne.Texte(r, "Denomination"), FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion")));
        var rejet = FormeLigne.DateDe(r, "DateRejet");
        var envoi = FormeLigne.DateDe(r, "DateEnvoi");
        var precisions = FormeLigne.Joindre($"Dépôt {FormeLigne.Texte(r, "DPTRC_NUM_DEPOT")}", FormeLigne.Texte(r, "Description"),
                                            envoi is null ? null : $"envoyé le {FormeLigne.Date(envoi)}",
                                            rejet is null ? null : $"rejeté le {FormeLigne.Date(rejet)}", FormeLigne.Texte(r, "Motif"));
        // La regle d'etat (en tete du fichier) : toute ligne est rejetee, a renvoyer.
        return new LigneLue(nom, precisions, Rejets, "AFaire", [])
        {
            Id = Convert.ToInt32(r["ACTDP_ID_ACDPS"], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        };
    }
}
