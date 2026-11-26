using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransMain.LectureDemandes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Consultation judiciaire » (web/src/ecrans/EcranConsultationJud.tsx) : les enrolements dematerialises recus par le RPVA,
/// lus dans la base V3 du greffe. LECTURE SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026 (marche du
/// lot 17).
///
/// SOURCE (ancien RIG) : PROC_CJUD en mode CJUDINFOG (fiche proc_cjud.md:97-118, :249-283) -- l'objet est l'enregistrement
/// DEMAT_RPJC_ENR de la demande (OPE_CJUDINFOG.cs:66-73, DematRPJCEnr.GetDematRPJCEnrByDemande). Les champs de l'ecran V2 :
///   - l'etat du flux RPJCE_ETAT (ULT_COMBO_ETAT_DEMAT, OPE_CJUDINFOG.cs:96), libelle de eDematEtat
///     (Outils\DLL\RigMetier\JUDICIAIRE\DematEtat.cs:13-49) ;
///   - le traitement du greffe RPJCE_ETAT_TRAITEMENT_RIG (ult_ComboBoxEtatTraitement, :83) : « A » Accepté, « R » Refusé
///     (ULT_COMBO_ACCEPTE_REFUSE.cs:20-21) ; vide tant que le greffe ne l'a pas traite (« En attente ») ;
///   - le type et le motif de rejet (RPJCE_TYPE_REJET_RIG, RPJCE_MOTIF_REJET_RIG, :84), les dates de creation et de
///     traitement (ult_DateCreation, ult_DateTraitement) ;
///   - l'instance creee a l'acceptation (RPJCE_ID_INSTN : role et nom courant) et la demande RIG qui porte l'enregistrement
///     (RPJCE_ID_DMND).
/// LE XML (« Visualiser le XML », ult_ButtonvisuXML, OPE_CJUDINFOG.cs:119-128) : la V2 ecrit RPJCE_FLUX de la base du greffe
/// dans un fichier .xml et l'ouvre (PROC_CJUD.cs:437-455, RPJCE_FLUXGetFile). RIG v3 le lit A LA DEMANDE (LireXmlAsync), un
/// enrolement a la fois : la liste ne transporte pas les flux. Les octets sont de l'UTF-8 (XmlDocument.Load de la V2 sans
/// declaration ; les flux de RIG_DEV n'en ont pas, mesure le 26/11/2026).
/// NON LUS, nommes : le nombre de pieces (ult_LabelNbPiece, OPE_CJUDINFOG.cs:106-113) vient du flux entrant de la base EDI du
/// greffe (FluxEntrant.GetFluxEntrant, CodeGreffeEdi), absente de la base V3 ; les anomalies (ETP_CJUDINFOG.Anomalie) sont
/// calculees par la V2 en lisant le XML pour creer l'instance (Instance.CreateInstanceFromXmlRPJCEnr, PROC_CJUD.cs:461), lecture
/// que RIG v3 n'a pas portee.
/// ORDRE : du plus recent au plus ancien (date de creation), comme une file de reception.
/// </summary>
public static class LectureConsultationJud
{
    /// <summary>Le nombre d'enrolements montres.</summary>
    public const int Plafond = 500;

    /// <summary>Libelles de eDematEtat (DematEtat.cs:17-47), par code.</summary>
    internal static readonly IReadOnlyDictionary<string, string> EtatsDemat = new Dictionary<string, string>
    {
        ["1"] = "Intégré avec succès", ["2"] = "Traité par le greffe", ["3"] = "Envoyé", ["4"] = "Validé",
        ["8"] = "Erreur d'envoi", ["9"] = "Rejeté", ["10"] = "Ne pas envoyer",
    };

    private const string Requete = """
        SELECT TOP (@plafond) r.RPJCE_ID_RPJCE, r.RPJCE_REFERENCE_DEMANDE, r.RPJCE_ETAT, r.RPJCE_ETAT_TRAITEMENT_RIG,
               r.RPJCE_TYPE_REJET_RIG, r.RPJCE_DATE_CREATION, r.RPJCE_DATE_TRAITEMENT_RIG, r.RPJCE_MOTIF_REJET_RIG,
               i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, d.DMND_NUM_DEMANDE, COUNT(*) OVER () AS Total
        FROM v2.DEMAT_RPJC_ENR AS r
        LEFT JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = r.RPJCE_ID_INSTN
        LEFT JOIN v2.DEMANDE AS d ON d.DMND_ID_DMND = r.RPJCE_ID_DMND
        ORDER BY r.RPJCE_DATE_CREATION DESC, r.RPJCE_ID_RPJCE DESC
        """;

    /// <summary>Les enrolements RPJC du greffe, au plus <paramref name="plafond"/>.</summary>
    public static async Task<EnrolementsLus> LireAsync(SqlConnection baseGreffe, CancellationToken ct, int plafond = Plafond)
    {
        var lignes = new List<EnrolementLu>();
        var total = 0;
        await using var lu = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@plafond", SqlDbType.Int) { Value = plafond });
        while (await lu.ReadAsync(ct))
        {
            total = Convert.ToInt32(lu["Total"], CultureInfo.InvariantCulture);
            var etat = Chaine(lu, "RPJCE_ETAT");
            lignes.Add(new EnrolementLu(Convert.ToInt32(lu["RPJCE_ID_RPJCE"], CultureInfo.InvariantCulture), Chaine(lu, "RPJCE_REFERENCE_DEMANDE"),
                Chaine(lu, "INSTN_NUM_ROLE"), Chaine(lu, "INSTN_NOM_COURANT"), EtatsDemat.TryGetValue(etat, out var libelle) ? libelle : etat,
                Traitement(Chaine(lu, "RPJCE_ETAT_TRAITEMENT_RIG")), Chaine(lu, "RPJCE_TYPE_REJET_RIG"), lu["RPJCE_DATE_CREATION"] as DateTime?,
                lu["RPJCE_DATE_TRAITEMENT_RIG"] as DateTime?, Chaine(lu, "RPJCE_MOTIF_REJET_RIG"), Chaine(lu, "DMND_NUM_DEMANDE")));
        }
        return new EnrolementsLus([.. lignes], total);
    }

    /// <summary>Le XML de l'enrolement <paramref name="id"/> (RPJCE_FLUX) : Existe faux s'il n'est pas dans la base, Xml null
    /// s'il n'a pas de flux.</summary>
    public static async Task<FluxEnrolementLu> LireXmlAsync(SqlConnection baseGreffe, int id, CancellationToken ct)
    {
        await using var lu = await LectureV2.LireAsync(baseGreffe, "SELECT RPJCE_FLUX FROM v2.DEMAT_RPJC_ENR WHERE RPJCE_ID_RPJCE = @id", ct,
            new SqlParameter("@id", SqlDbType.Int) { Value = id });
        if (!await lu.ReadAsync(ct)) return new FluxEnrolementLu(id, false, null);
        if (lu["RPJCE_FLUX"] is not byte[] flux) return new FluxEnrolementLu(id, true, null);
        using var texte = new StreamReader(new MemoryStream(flux), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return new FluxEnrolementLu(id, true, await texte.ReadToEndAsync(ct));
    }

    /// <summary>« A » Accepté, « R » Refusé (ULT_COMBO_ACCEPTE_REFUSE.cs:20-21) ; rien : pas encore traite.</summary>
    private static string Traitement(string code) => code switch { "A" => "Accepté", "R" => "Refusé", _ => "En attente" };
}
