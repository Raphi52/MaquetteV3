using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Reversements INPI » (PROC_INPIPAY) : les fichiers de reversements de l'INPI (v2.INPI_REVERSEMENTS) a valider. Mode d'emploi :
/// LecteurEcran.cs ; les gestes (Rejeter, Valider) : EcrivainReversementsInpi.cs. Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_INPIPAY (le Detail OPE de proc_inpipay.md fait foi ; le corps
/// de la fiche cite des colonnes inventees) -- la requete Metier.CreerRequete (Metier\Metier.cs:12-60) : INPI_REVERSEMENTS LEFT JOIN
/// DEMANDE (INPIRV_ID_DMND), UTILISATEUR, ENCAISSEMENT, WHERE INPIRV_ETAT_TRAITEMENT IN (&lt;etats coches&gt;), ORDER BY
/// INPIRV_DATE_EXTRACT_BEGIN. A l'ouverture, seul « A valider » (1) est coche (OPE_SEARCH_BLOC_1.DoInitEvent, :45-47). Les dates de
/// OPE_DATES ne filtrent pas la requete (_ConstruireClauseWhere ne les lit pas).
/// LIGNE : l'identifiant de paiement, la periode d'extraction, le nombre de reversements et le montant total traite
/// (INPIRV_TOTAL_HANDLING_AMOUNT), la demande liee. ETAT (eEtatInpiReversements, InpiReversements.cs:87-101) : 1 -> AFaire « À valider »,
/// 2 -> Validee, 3 -> Rejetee. L'utilisateur (UTILISATEUR, soc.Compte dans la V3) n'est pas affiche.
/// </summary>
internal sealed class LecteurReversementsInpi : ILecteurEcran
{
    public string Procedure => "PROC_INPIPAY";

    internal const string Groupe = "Virements INPI";

    private const string Requete = """
        SELECT TOP (@plafond) r.INPIRV_ID_INPIRV, r.INPIRV_ID_PAYMENT, r.INPIRV_DATE_EXTRACT_BEGIN, r.INPIRV_DATE_EXTRACT_END, r.INPIRV_NB_REVERSEMENTS,
               r.INPIRV_TOTAL_HANDLING_AMOUNT, r.INPIRV_ETAT_TRAITEMENT, d.DMND_NUM_DEMANDE, COUNT(*) OVER () AS Total
        FROM v2.INPI_REVERSEMENTS AS r
        LEFT JOIN v2.DEMANDE AS d ON d.DMND_ID_DMND = r.INPIRV_ID_DMND
        WHERE r.INPIRV_ETAT_TRAITEMENT IN ('1')
        ORDER BY r.INPIRV_DATE_EXTRACT_BEGIN, r.INPIRV_ID_INPIRV
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var nb = CriteresConsultation.EntierDe(r, "INPIRV_NB_REVERSEMENTS");
        var precisions = FormeLigne.Joindre(
            $"extraction du {FormeLigne.Date(FormeLigne.DateDe(r, "INPIRV_DATE_EXTRACT_BEGIN"))} au {FormeLigne.Date(FormeLigne.DateDe(r, "INPIRV_DATE_EXTRACT_END"))}",
            nb is { } n ? $"{n} reversement{(n > 1 ? "s" : "")}" : null,
            CriteresConsultation.Decimal(r, "INPIRV_TOTAL_HANDLING_AMOUNT") is { } m ? CriteresConsultation.Euros(m) : null,
            FormeLigne.Texte(r, "DMND_NUM_DEMANDE") is { Length: > 0 } dm ? "demande " + dm : null);
        var etat = FormeLigne.Texte(r, "INPIRV_ETAT_TRAITEMENT") switch { "2" => "Validee", "3" => "Rejetee", _ => "AFaire" };
        return new LigneLue($"Reversement INPI {FormeLigne.Texte(r, "INPIRV_ID_PAYMENT")}", precisions, Groupe, etat, [])
        {
            Id = Convert.ToString(r["INPIRV_ID_INPIRV"], CultureInfo.InvariantCulture),
        };
    }
}
