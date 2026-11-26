using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « FIBEN » (PROC_FIBEN) : les volumes que le greffe a transmis, par periode -- la transmission FIBEN mensuelle (avis RCS et
/// avis de procedure collective vers la Banque de France) et le releve BODACC trimestriel (annonces A, B, C, P). Mode d'emploi :
/// LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026. Lecture seule.
///
/// SOURCE (ancien RIG) : PROC_FIBEN ne consulte PAS la Banque de France (proc_fiben.md:3) : il compte, dans la base de chaque
/// greffe, ce qui a ete transmis, puis edite un releve de facturation (etat Crystal) :
///   - FIBEN (ReleveFIBEN._CompterGreffe, D:\RigApplication\Source\DllRapportCrystal\RigCrystalFactu\ReleveFIBEN.cs:181-210) :
///     EDI_BDF_RCS -- avis RCS hors « REGUL » (DBDRC_AVIS NOT LIKE '%  REGUL  %'), par DBDRC_DATE_ENVOI, et bornes des numeros
///     de transmission > 0 ; EDI_BDF -- avis PC crees (EDIBDF_CODE_ACTION 'C') et annules ('A'), par
///     EDIBDF_DATE_INSERTION_DANS_FICHIER ;
///   - BODACC (ReleveBODACC._CompterAnnonces, ReleveBODACC.cs:244-267) : BODACC par BDCCR_DATE_PUBLICITE, type, cout,
///     type d'annonce renseignes, compte par UPPER(LEFT(BDCCR_TYPE_BODACC, 1)) parmi A, B, C, P (TYPES_BODACC, :31).
/// La V2 compte UNE periode choisie (un mois, un trimestre), pour chaque greffe exploite de la base commune
/// (COMMUN_RIG.GREFFE, GRF_IS_EXPLOIT = 1, OPE_CRITERES.cs:55-56). La base commune est absente de la base V3 du greffe : la V3
/// compte LE greffe de la base, pour chaque periode qui a une transmission (une ligne par mois, par trimestre), et l'Etat les
/// filtre par sa periode. Non repris (nommes) : les tarifs unitaires saisis (4 et 6) et les montants qu'ils donnent, et
/// l'edition Crystal (Expresso_EDI_FIBEN.rpt, Expresso_ReleveBodacc2.rpt).
///
/// GROUPES : « Transmission FIBEN » (mensuelle) / « Relevé BODACC » (trimestriel).
/// REGLE D'ETAT (la periode et aujourd'hui) : periode qui contient aujourd'hui -> EnCours « Période en cours » (ses volumes
/// bougent encore) ; periode passee -> Validee « Période close ». « À venir » n'a pas de ligne : aucune transmission future.
/// VALEURS (les colonnes du descripteur) : debut de periode, releve, avis RCS, avis PC crees, avis PC annules, annonces A, B, C, P.
/// </summary>
internal sealed class LecteurReleveFiben : ILecteurEcran
{
    public string Procedure => "PROC_FIBEN";

    internal static readonly string[] Groupes = ["Transmission FIBEN", "Relevé BODACC"];

    private static readonly string[] Mois =
        ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"];

    // Bloc 0 : un mois (FIBEN) ; bloc 1 : un trimestre (BODACC). Debut = le premier jour de la periode.
    private const string Requete = """
        SELECT TOP (@plafond) l.*, COUNT(*) OVER () AS Total
        FROM (SELECT 0 AS Bloc, m.Debut, SUM(m.AvisRcs) AS AvisRcs, SUM(m.PcCrees) AS PcCrees, SUM(m.PcAnnules) AS PcAnnules,
                     NULL AS A, NULL AS B, NULL AS C, NULL AS P
              FROM (SELECT DATEFROMPARTS(YEAR(r.DBDRC_DATE_ENVOI), MONTH(r.DBDRC_DATE_ENVOI), 1) AS Debut,
                           CASE WHEN r.DBDRC_AVIS NOT LIKE '%  REGUL  %' THEN 1 ELSE 0 END AS AvisRcs, 0 AS PcCrees, 0 AS PcAnnules
                    FROM v2.EDI_BDF_RCS AS r WHERE r.DBDRC_DATE_ENVOI IS NOT NULL
                    UNION ALL
                    SELECT DATEFROMPARTS(YEAR(b.EDIBDF_DATE_INSERTION_DANS_FICHIER), MONTH(b.EDIBDF_DATE_INSERTION_DANS_FICHIER), 1), 0,
                           CASE WHEN b.EDIBDF_CODE_ACTION = 'C' THEN 1 ELSE 0 END, CASE WHEN b.EDIBDF_CODE_ACTION = 'A' THEN 1 ELSE 0 END
                    FROM v2.EDI_BDF AS b WHERE b.EDIBDF_DATE_INSERTION_DANS_FICHIER IS NOT NULL) AS m
              GROUP BY m.Debut
              UNION ALL
              SELECT 1, DATEFROMPARTS(YEAR(o.BDCCR_DATE_PUBLICITE), (DATEPART(QUARTER, o.BDCCR_DATE_PUBLICITE) - 1) * 3 + 1, 1), NULL, NULL, NULL,
                     SUM(CASE WHEN UPPER(LEFT(o.BDCCR_TYPE_BODACC, 1)) = 'A' THEN 1 ELSE 0 END),
                     SUM(CASE WHEN UPPER(LEFT(o.BDCCR_TYPE_BODACC, 1)) = 'B' THEN 1 ELSE 0 END),
                     SUM(CASE WHEN UPPER(LEFT(o.BDCCR_TYPE_BODACC, 1)) = 'C' THEN 1 ELSE 0 END),
                     SUM(CASE WHEN UPPER(LEFT(o.BDCCR_TYPE_BODACC, 1)) = 'P' THEN 1 ELSE 0 END)
              FROM v2.BODACC AS o
              WHERE o.BDCCR_DATE_PUBLICITE IS NOT NULL AND o.BDCCR_TYPE_BODACC IS NOT NULL AND o.BDCCR_COUT_BODACC IS NOT NULL
                AND o.BDCCR_TYPE_ANNONCE IS NOT NULL
              GROUP BY DATEFROMPARTS(YEAR(o.BDCCR_DATE_PUBLICITE), (DATEPART(QUARTER, o.BDCCR_DATE_PUBLICITE) - 1) * 3 + 1, 1)) AS l
        ORDER BY l.Bloc, l.Debut DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, r => Ligne(r, aujourdhui), ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r, DateTime aujourdhui)
    {
        var bloc = System.Convert.ToInt32(r["Bloc"], System.Globalization.CultureInfo.InvariantCulture);
        var debut = (DateTime)r["Debut"];
        var fin = debut.AddMonths(bloc == 0 ? 1 : 3);
        var periode = bloc == 0 ? $"{Mois[debut.Month - 1]} {debut.Year}" : $"{(debut.Month + 2) / 3}{((debut.Month + 2) / 3 == 1 ? "er" : "e")} trimestre {debut.Year}";
        var releve = bloc == 0 ? $"Transmission FIBEN · {periode}" : $"Relevé BODACC · {periode}";
        string N(string colonne) => FormeLigne.Texte(r, colonne);
        var precisions = bloc == 0
            ? FormeLigne.Joindre($"{N("AvisRcs")} avis RCS", $"{N("PcCrees")} avis PC créés", $"{N("PcAnnules")} annulés")
            : FormeLigne.Joindre($"A {N("A")}", $"B {N("B")}", $"C {N("C")}", $"P {N("P")}");
        // La regle d'etat (en tete du fichier).
        var etat = aujourdhui.Date >= debut && aujourdhui.Date < fin ? "EnCours" : "Validee";
        return new LigneLue(releve, precisions, Groupes[bloc], etat, [])
        {
            Valeurs = [FormeLigne.DateFiche(debut), releve, N("AvisRcs"), N("PcCrees"), N("PcAnnules"), N("A"), N("B"), N("C"), N("P")],
        };
    }
}
