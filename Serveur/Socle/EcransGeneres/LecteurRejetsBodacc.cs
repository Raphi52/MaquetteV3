using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Surveillance BODACC » (PROC_SURVBOD) : le CONTROLE QUALITE des annonces BODACC -- les annonces publiees dont le controle
/// qualite porte une remarque pas encore verifiee. Une FILE. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel »,
/// lot 9, 26/11/2026.
///
/// SOURCE (ancien RIG) : proc_survbod.md (Detail OPE, qui fait foi : la « requete principale » du haut de la fiche est inventee) et
/// la requete reelle, Source\RIG\DLL\Processus\PROC_SURVBOD\...\OPE_REJETS_BODACC.cs:12-32 :
///   - BODACC dont BDCCR_CONTROLE_QUALITE est renseigne et BDCCR_CONTROLE_QUALITE_VERIFIE = 0 ;
///   - publiees du 01/01/2008 a aujourd'hui 23:59:59 (la borne basse est en dur dans RIG v2) ;
///   - d'un type dont le bordereau est AP, ANU, AU, B, BR ou C (TYPE_BODACC.R_TYPEBODACC_BORDEREAU) ;
///   - rattachees a la chaine RCS par l'action (ACTRC_ID_BDCRC) : numero de gestion du dossier, sinon du non-inscrit ; numero
///     chronologique de l'inscription, sinon numero du depot ; ordre « Chrono, BDCCR_DATE_PUBLICITE ».
///   ACTION_RCS et DOSSIER_RCS sont les fiches rec.ActionRegistre et rec.DossierRcs de la V3 (Serveur/Recopie/FichesV3.cs).
/// GROUPE : « Rejets du contrôle qualité » (le titre de l'etape, PROC_SURVBOD.cs:11-14). ETAT : la colonne Xaction de RIG v2 --
/// « M » (Modifier) si l'annonce est rattachee a un dossier RCS : « À corriger », AFaire ; sans dossier (un non-inscrit, ou une annonce
/// sans action), le bouton est masque (MasquerBoutonsVides, :270-272) et LaunchXAction ne fait rien (:181) : « Sans dossier RCS »,
/// Rejetee.
///
/// LES GESTES : EcrivainSurveillanceBodacc.cs (lot 1 du plan-f4fb097f) -- « Modifier » cree la Demande XACTION qualifiee du dossier ;
/// l'ancien processus VB6 XACTION qu'elle ouvre en modale (:192-252) n'a pas d'equivalent V3 ; « Word » et « Imprimer » placent la liste
/// des controles qualite dans la file des editions. Rien, dans ce processus, ne pose BDCCR_CONTROLE_QUALITE_VERIFIE : c'est le traitement
/// XACTION qui le fait.
/// </summary>
internal sealed class LecteurRejetsBodacc : ILecteurEcran
{
    public string Procedure => "PROC_SURVBOD";

    /// <summary>Le groupe du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string Rejets = "Rejets du contrôle qualité";

    private const string Requete = """
        SELECT TOP (@plafond) b.BDCCR_ID_BDCRC, b.BDCCR_DATE_PUBLICITE, b.BDCCR_TYPE_BODACC, b.BDCCR_CONTROLE_QUALITE,
               CASE WHEN s.NumeroGestion IS NULL THEN ni.NINSC_NUM_NON_INSCRIT ELSE s.NumeroGestion END AS NumGestion,
               CASE WHEN ir.INSRC_NUM_CHRONO IS NULL THEN p.DPTRC_NUM_DEPOT ELSE ir.INSRC_NUM_CHRONO END AS Chrono,
               CASE WHEN s.NumeroGestion IS NULL THEN '' ELSE 'M' END AS Xaction,
               COUNT(*) OVER () AS Total
        FROM v2.BODACC AS b
        JOIN v2.TYPE_BODACC AS t ON b.BDCCR_TYPE_BODACC = t.R_TYPEBODACC_VALEUR
        LEFT JOIN rec.ActionRegistre AS a ON b.BDCCR_ID_BDCRC = a.IdAnnonceBodacc
        LEFT JOIN v2.DEPOT_RCS AS p ON a.IdDepot = p.DPTRC_ID_DPTRC
        LEFT JOIN v2.INSCRIPTION_RCS AS ir ON a.IdInscriptionRegistre = ir.INSRC_ID_INSRC
        LEFT JOIN v2.NON_INSCRIT AS ni ON a.IdNonInscrit = ni.NINSC_ID_NINSC
        LEFT JOIN rec.DossierRcs AS s ON a.IdDossier = s.Id
        WHERE b.BDCCR_CONTROLE_QUALITE IS NOT NULL AND b.BDCCR_CONTROLE_QUALITE_VERIFIE = 0
          AND b.BDCCR_DATE_PUBLICITE >= '20080101' AND b.BDCCR_DATE_PUBLICITE < @demain
          AND t.R_TYPEBODACC_BORDEREAU IN ('AP', 'ANU', 'AU', 'B', 'BR', 'C')
        ORDER BY Chrono, b.BDCCR_DATE_PUBLICITE, b.BDCCR_ID_BDCRC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond),
                                  new SqlParameter("@demain", System.Data.SqlDbType.DateTime) { Value = aujourdhui.Date.AddDays(1) });

    private static LigneLue Ligne(SqlDataReader r)
    {
        var dossier = FormeLigne.Texte(r, "Xaction") == "M";
        var numero = FormeLigne.Texte(r, "NumGestion");
        var nom = FormeLigne.Joindre($"Annonce {FormeLigne.Texte(r, "BDCCR_TYPE_BODACC")} du {FormeLigne.Date(FormeLigne.DateDe(r, "BDCCR_DATE_PUBLICITE"))}",
                                     dossier ? FormeLigne.NumeroGestion(numero) : numero.Length > 0 ? $"non-inscrit {numero}" : null);
        var precisions = FormeLigne.Joindre(FormeLigne.Texte(r, "Chrono") is { Length: > 0 } chrono ? $"chrono {chrono}" : null,
                                            FormeLigne.Texte(r, "BDCCR_CONTROLE_QUALITE"), dossier ? null : "sans dossier RCS : pas de correction");
        return new LigneLue(nom, precisions, Rejets, dossier ? "AFaire" : "Rejetee", [])
        {
            Id = Convert.ToInt32(r["BDCCR_ID_BDCRC"], System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}
