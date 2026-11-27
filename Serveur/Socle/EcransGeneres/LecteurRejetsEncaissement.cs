using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Rejets d'encaissement » (PROC_REJET) : les cheques qu'un rejet peut viser, et les rejets enregistres. Mode d'emploi :
/// LecteurEcran.cs ; le geste : EcrivainRejetsEncaissement.cs. Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026.
///
/// SOURCE (ancien RIG) : la V2 ouvre PROC_REJET sur UN encaissement, depuis le menu « Rejeter cet encaissement » de la gestion
/// commerciale (D:\RigApplication\Source\RIG\DLL\Processus\PROC_GCOM\GcomTreeViewEngine\ActionMenus\MenuEncaissement.cs:156-163,
/// :355-363) ; ce menu n'est permis que pour un CHEQUE (R_MODEPAIEME_TYPE = « CH », :124, :161), jamais une provision (PV, :162).
/// La V3 liste donc ces cheques (groupe « Chèques », AFaire « À rejeter »), les plus recents d'abord, pour en choisir un ; un
/// cheque deja rejete (un rejet au meme titre, meme date et meme montant -- le seul lien que la V2 garde, Detail OPE de
/// proc_rejet.md) n'y est plus. Les rejets enregistres (ENCAISSEMENT_RECUS_REJETES, EncaissementRecuRejete) suivent, groupe
/// « Rejets enregistrés », Validee « Rejet enregistré », avec leur motif. La copie de developpement garde les encaissements les
/// plus anciens : les cheques listes sont ceux-la.
/// </summary>
internal sealed class LecteurRejetsEncaissement : ILecteurEcran
{
    public string Procedure => "PROC_REJET";

    internal const string Cheques = "Chèques", Rejets = "Rejets enregistrés";

    private const string Requete = """
        SELECT TOP (@plafond) x.*, COUNT(*) OVER () AS Total FROM (
            SELECT 0 AS Rang, e.ENCMT_ID_ENCMT AS Id, e.ENCMT_NUMERO_TITRE AS Titre, e.ENCMT_DATE_ENCAISSEMENT AS DateEncmt, e.ENCMT_MONTANT AS Montant,
                   e.ENCMT_DESIGNATION_PAYEUR AS Payeur, e.ENCMT_DESIGNATION_BANQUE AS Banque, mp.R_MODEPAIEME_LIBELLE_ECRAN AS Mode,
                   e.ENCMT_DATE_COMPTABILISATION AS Comptabilise, CONVERT(varchar(150), NULL) AS Motif, CONVERT(datetime, NULL) AS DateRejet
            FROM v2.ENCAISSEMENT AS e
            INNER JOIN v2.MODE_PAIEMENT AS mp ON mp.R_MODEPAIEME_VALEUR = e.ENCMT_MODE_PAIEMENT
            WHERE mp.R_MODEPAIEME_TYPE = 'CH' AND e.ENCMT_MODE_PAIEMENT <> 'PV'
              AND NOT EXISTS (SELECT 1 FROM v2.ENCAISSEMENT_RECUS_REJETES AS j WHERE j.ENCRR_NUM_TITRE = e.ENCMT_NUMERO_TITRE
                              AND j.ENCRR_DATE_ENCMT = e.ENCMT_DATE_ENCAISSEMENT AND j.ENCRR_MONTANT = e.ENCMT_MONTANT)
            UNION ALL
            SELECT 1, j.ENCRR_ID_ENCRR, j.ENCRR_NUM_TITRE, j.ENCRR_DATE_ENCMT, j.ENCRR_MONTANT, j.ENCRR_DESIGNATION_PAYEUR, j.ENCRR_DESIGNATION_BANQUE,
                   NULL, NULL, j.ENCRR_RAISON_REJET, j.ENCRR_DATE_CREATION
            FROM v2.ENCAISSEMENT_RECUS_REJETES AS j
        ) AS x
        ORDER BY x.Rang, COALESCE(x.DateRejet, x.DateEncmt) DESC, x.Id DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var rejet = Convert.ToInt32(r["Rang"], System.Globalization.CultureInfo.InvariantCulture) == 1;
        var titre = FormeLigne.Texte(r, "Titre");
        var id = FormeLigne.Texte(r, "Id");
        var nom = rejet ? $"Rejet n° {id}" + (titre.Length > 0 ? $" · chèque {titre}" : "") : titre.Length > 0 ? $"Chèque n° {titre}" : $"Encaissement n° {id}";
        var montant = CriteresConsultation.Euros(CriteresConsultation.Decimal(r, "Montant"));
        var precisions = rejet
            ? FormeLigne.Joindre("rejeté le " + FormeLigne.Date(FormeLigne.DateDe(r, "DateRejet")), montant, FormeLigne.Texte(r, "Payeur"), "motif : " + FormeLigne.Texte(r, "Motif"))
            : FormeLigne.Joindre(FormeLigne.Texte(r, "Mode"), montant, "encaissé le " + FormeLigne.Date(FormeLigne.DateDe(r, "DateEncmt")), FormeLigne.Texte(r, "Payeur"),
                FormeLigne.Texte(r, "Banque"), FormeLigne.DateDe(r, "Comptabilise") is null ? "non comptabilisé" : "comptabilisé");
        return new LigneLue(nom, precisions, rejet ? Rejets : Cheques, rejet ? "Validee" : "AFaire", []) { Id = rejet ? null : id };
    }
}
