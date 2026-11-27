using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Reversements INPI » (PROC_INPIPAY) : les fichiers de reversements de l'INPI (v2.INPI_REVERSEMENTS) a valider. Mode d'emploi :
/// LecteurEcran.cs ; le geste : EcrivainReversementsInpi.cs. Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026.
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

/// <summary>
/// « Reversements INPI » (PROC_INPIPAY) : REJETER un fichier de reversements (le menu « Rejeter » de la grille). Mode d'emploi :
/// EcrivainEcran.cs.
///
/// SOURCE (ancien RIG) : RESULTATS\OPE_RESULTATS.cs -- le menu n'est visible que pour un utilisateur Amitel (:50-53) et actif que sur
/// un fichier « A valider » (:69-71) ; confirmation, motif facultatif (:83-101), puis InpiReversements.Rejeter (InpiReversements.cs:
/// 64-85) : INPIRV_ETAT_TRAITEMENT = 3, INPIRV_DATE_TRAITEMENT = maintenant, le motif ajoute aux commentaires. La V3 rejette sans
/// motif (une file n'a pas de saisie : la V2 l'accepte, AllowEmptyValue).
/// NON REPRIS (nomme) : VALIDER un fichier (FORM_VALIDATION, OPE_VALIDATION.DoSauve, VALIDATION\OPE_VALIDATION.cs:67-205) : il lit le
/// fichier XML de l'INPI (feepaymentdata), retrouve la demande de chaque numero national, cree l'encaissement et ses ventilations
/// au client de la derniere facture, sur un mode, un titre et une date saisis -- la V3 ne lit pas encore ce fichier.
/// </summary>
internal sealed class EcrivainReversementsInpi : IEcrivainEcran
{
    public string Procedure => "PROC_INPIPAY";
    public string Ecran => "Reversements INPI";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    internal const string MessageAmitel = "Le rejet d'un fichier de reversements est réservé à l'équipe Amitel.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != "Rejeter") return IssueGeste.Refuser($"Geste « {geste.Demande.Nom} » : non repris pour les reversements INPI.");
        if (!geste.Agent.EstAmitel) return IssueGeste.Refuser(MessageAmitel);
        var ids = geste.Demande.Ids.Select(i => int.TryParse(i, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).Where(n => n > 0).Distinct().ToList();
        if (ids.Count == 0) return IssueGeste.Refuser("Aucun fichier de reversements coché : rien n'est rejeté.");
        var traces = new List<TraceGeste>();
        var rejetes = new List<string>();
        foreach (var id in ids)
        {
            string? paiement = null, etat = null;
            await using (var r = await geste.LireAsync("SELECT INPIRV_ID_PAYMENT, INPIRV_ETAT_TRAITEMENT FROM v2.INPI_REVERSEMENTS WITH (UPDLOCK, HOLDLOCK) WHERE INPIRV_ID_INPIRV = @id",
                             ct, new SqlParameter("@id", id)))
                if (await r.ReadAsync(ct)) { paiement = FormeLigne.Texte(r, "INPIRV_ID_PAYMENT"); etat = FormeLigne.Texte(r, "INPIRV_ETAT_TRAITEMENT"); }
            if (paiement is null) return IssueGeste.Refuser($"Fichier de reversements introuvable dans la base du greffe ({id}).");
            if (etat != "1") return IssueGeste.Refuser($"Le fichier de reversements n°{paiement} n'est plus à valider : seul un fichier « A Valider » se rejette.");
            await geste.EcrireAsync("UPDATE v2.INPI_REVERSEMENTS SET INPIRV_ETAT_TRAITEMENT = '3', INPIRV_DATE_TRAITEMENT = @maintenant WHERE INPIRV_ID_INPIRV = @id", ct,
                new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@id", id));
            rejetes.Add(paiement);
            traces.Add(new TraceGeste("encaissement", $"le fichier de reversements INPI n°{paiement}", id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { Etat = "A Valider" }, new { Etat = "Rejeté" }));
        }
        return IssueGeste.Ecrit($"Fichier{(rejetes.Count > 1 ? "s" : "")} de reversements n°{string.Join(", n°", rejetes)} rejeté{(rejetes.Count > 1 ? "s" : "")} dans la base du greffe.",
            [.. ids.Select(i => i.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }
}
