using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Délibérés » (PROC_LISTE_DELIBERES) : les affaires en delibere depuis plus de N jours a une date de reference, sans decision
/// depuis. Un ETAT en lecture seule (aucune ecriture dans RIG v2). Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 %
/// fonctionnel », lot 7, 26/11/2026.
///
/// SOURCE (ancien RIG) : il n'y a pas de fiche proc_*.md ; le code fait foi -- Source\Judiciaire\Exe\PROC_LISTE_DELIBERES\
/// frmListeDelibere.cs (NewTraitement, :154-262) et frmListeDelibere.Designer.cs (les valeurs par defaut) :
///   - les instances des types coches (Contentieux « 07 » seul coche par defaut, Designer :513-521), enrolees au plus tard a la
///     date de reference (:162), dont la DERNIERE DATE DE DELIBERE existe -- le plus recent AUDNC_DATE de leurs appels dont le code
///     plumitif est un delibere non repousse (R_CODEPLUMIT_DELIBERE = 1, R_CODEPLUMIT_DELIBERE_REPOUSSE = 0), au plus tard a la
///     date de reference (getDerniereDateDelib, :139-144) -- et SANS decision judiciaire datee (DCJDC_DATE, sinon
///     DCJDC_DATE_PREVUE) depuis ce delibere et avant la date de reference (getDerniereDJ, :145-151) ;
///   - retenues si la date de reference moins la date du delibere atteint la duree (60 jours par defaut, Designer :557 ; :176) ;
///   - la date de reference par defaut : le dernier jour du mois precedent (constructeur, :61-63) ;
///   - colonnes (Designer :126-200) : n° de role, n° joints (l'instance principale et les jointes, :178-190), nom de l'affaire,
///     date du delibere, code plumitif du delibere (:191-199), duree, type et date de la decision prevue (la premiere DJ depuis le
///     delibere, sinon la date de la derniere audience, :207-236), juge de l'instance, localisation. ORDRE : role decroissant (:170).
/// Une ligne, un etat « À rendre » (AFaire) : la liste ne retient que des deliberes non rendus.
///
/// CE QUE LA V3 NE REPREND PAS : les parametres de la recherche (date, duree, types, juge, enrolement minimale) ne partent pas a la
/// base -- la forme Etat ne cherche pas (web/src/ecrans/formes/FormeEtat.tsx) : la liste est celle des valeurs par defaut de RIG v2 ;
/// « Editer le résultat » (un document Word, AfficherWord, :263-322) et « Délibérés repoussés non rendus » (un classeur Excel,
/// _GenererExcelDeliberer, :482-503) produisent un fichier sur le poste : la V3 n'a pas encore d'edition de ce genre.
/// </summary>
internal sealed class LecteurListeDeliberes : ILecteurEcran
{
    public string Procedure => "PROC_LISTE_DELIBERES";

    /// <summary>Le groupe et l'etat du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string Groupe = "Délibérés non rendus";

    /// <summary>Les valeurs par defaut de RIG v2 (frmListeDelibere.Designer.cs:513-521, :557).</summary>
    internal const int DureeParDefaut = 60;

    internal const string TypesParDefaut = "('07')";

    // La date de delibere d'une instance (getDerniereDateDelib, :139-144), au plus tard a @ref.
    private const string DateDelib = """
        (SELECT MAX(a.AUDNC_DATE) FROM v2.APPEL_AFFAIRE AS aa JOIN v2.AUDIENCE_CABINET AS a ON a.AUDNC_ID_ADNC = aa.APPAF_ID_ADNC
         JOIN v2.CODE_PLUMITIF AS cp ON cp.R_CODEPLUMIT_VALEUR = aa.APPAF_CODE_PLUMITIF
         WHERE aa.APPAF_ID_INSTN = i.INSTN_ID_INSTN AND cp.R_CODEPLUMIT_DELIBERE = 1 AND cp.R_CODEPLUMIT_DELIBERE_REPOUSSE = 0 AND a.AUDNC_DATE <= @ref)
        """;

    private static readonly string Requete = $$"""
        SELECT TOP (@plafond) i.INSTN_ID_INSTN AS Id, i.INSTN_NUM_ROLE AS Role, i.INSTN_NOM_COURANT AS NomAffaire, d.DateDelib,
               DATEDIFF(day, d.DateDelib, @ref) AS Duree, LTRIM(RTRIM(j.JUG_NOM + ISNULL(' ' + j.JUG_PRENOM, ''))) AS Juge,
               (SELECT TOP (1) cp.R_CODEPLUMIT_VALEUR FROM v2.APPEL_AFFAIRE AS aa JOIN v2.AUDIENCE_CABINET AS a ON a.AUDNC_ID_ADNC = aa.APPAF_ID_ADNC
                JOIN v2.CODE_PLUMITIF AS cp ON cp.R_CODEPLUMIT_VALEUR = aa.APPAF_CODE_PLUMITIF
                WHERE aa.APPAF_ID_INSTN = i.INSTN_ID_INSTN AND cp.R_CODEPLUMIT_DELIBERE = 1 AND cp.R_CODEPLUMIT_DELIBERE_REPOUSSE = 0
                  AND a.AUDNC_DATE <= @ref ORDER BY a.AUDNC_DATE DESC) AS CodePlumitif,
               STUFF((SELECT ',' + x.INSTN_NUM_ROLE FROM v2.INSTANCE AS x
                      WHERE x.INSTN_ID_INSTN_INSTANCE_PRINCIPA = i.INSTN_ID_INSTN OR x.INSTN_ID_INSTN = i.INSTN_ID_INSTN_INSTANCE_PRINCIPA
                      ORDER BY x.INSTN_ID_INSTN FOR XML PATH('')), 1, 1, '') AS Joints,
               dj.TypeDj, dj.DateDj, dj.Localisation,
               (SELECT TOP (1) a.AUDNC_DATE FROM v2.APPEL_AFFAIRE AS aa JOIN v2.AUDIENCE_CABINET AS a ON a.AUDNC_ID_ADNC = aa.APPAF_ID_ADNC
                WHERE aa.APPAF_ID_INSTN = i.INSTN_ID_INSTN ORDER BY a.AUDNC_DATE DESC) AS DerniereAudience,
               CASE WHEN dj.IdDj IS NULL THEN 0 ELSE 1 END AS AvecDj,
               COUNT(*) OVER () AS Total
        FROM v2.INSTANCE AS i
        LEFT JOIN v2.JUGE AS j ON j.JUG_ID_JUG = i.INSTN_ID_JUG
        CROSS APPLY (SELECT {{DateDelib}} AS DateDelib) AS d
        OUTER APPLY (SELECT TOP (1) x.DCJDC_ID_DCJDC AS IdDj, td.R_TYPEDECISI_SYNONYME AS TypeDj, ISNULL(x.DCJDC_DATE, x.DCJDC_DATE_PREVUE) AS DateDj,
                            x.DCJDC_LOCALISATION AS Localisation
                     FROM v2.DECISION_JUDICIAIRE AS x JOIN v2.TYPE_DECISION AS td ON td.R_TYPEDECISI_VALEUR = x.DCJDC_TYPE_DECISION
                     WHERE x.DCJDC_ID_INSTN = i.INSTN_ID_INSTN AND ISNULL(x.DCJDC_DATE, x.DCJDC_DATE_PREVUE) >= d.DateDelib
                     ORDER BY ISNULL(x.DCJDC_DATE, x.DCJDC_DATE_PREVUE)) AS dj
        WHERE i.INSTN_TYPE_INSTANCE IN {{TypesParDefaut}} AND i.INSTN_DATE_ENROLEMENT <= @ref AND d.DateDelib IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM v2.DECISION_JUDICIAIRE AS x WHERE x.DCJDC_ID_INSTN = i.INSTN_ID_INSTN
                          AND ISNULL(x.DCJDC_DATE, x.DCJDC_DATE_PREVUE) >= d.DateDelib AND ISNULL(x.DCJDC_DATE, x.DCJDC_DATE_PREVUE) < @ref)
          AND DATEDIFF(day, d.DateDelib, @ref) >= @duree
        ORDER BY i.INSTN_NUM_ROLE DESC, i.INSTN_ID_INSTN DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        // Le dernier jour du mois precedent (frmListeDelibere.cs:61-63).
        var reference = new DateTime(aujourdhui.Year, aujourdhui.Month, 1).AddDays(-1);
        return LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@ref", reference), new SqlParameter("@duree", DureeParDefaut));
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var role = FormeLigne.Texte(r, "Role");
        var dateDelib = FormeLigne.DateDe(r, "DateDelib");
        var duree = Convert.ToInt32(r["Duree"], CultureInfo.InvariantCulture);
        var avecDj = Convert.ToInt32(r["AvecDj"], CultureInfo.InvariantCulture) == 1;
        // Sans DJ depuis le delibere : la date prevue est celle de la derniere audience, sans type ni localisation (:224-236).
        var datePrevue = avecDj ? FormeLigne.DateDe(r, "DateDj") : FormeLigne.DateDe(r, "DerniereAudience");
        var nom = FormeLigne.Texte(r, "NomAffaire") is { Length: > 0 } n ? n : $"Instance n° {role}";
        // Les dates (delibere, reference) sont dans les colonnes ; les precisions disent la duree, que la liste mesure.
        // fix-ok: lu dans tests/EcransGeneresBaseCheck/Program.cs:413 -- un .attendu n'ecrit une date que relative au jour ({J-5},
        // {M-1}) ; la reference « dernier jour du mois precedent » ne s'y ecrit pas, donc une date dans les precisions ne se
        // verifiait pas. La duree, elle, ne depend que du jeu.
        var precisions = FormeLigne.Joindre($"Rôle {role}", $"{duree} jours de délibéré",
            FormeLigne.Texte(r, "Juge") is { Length: > 0 } j ? $"juge {j}" : null);
        return new LigneLue(nom, precisions, Groupe, "AFaire", [])
        {
            Id = FormeLigne.Texte(r, "Id"),
            Valeurs =
            [
                role, FormeLigne.Texte(r, "Joints"), nom, FormeLigne.DateFiche(dateDelib), FormeLigne.Texte(r, "CodePlumitif"),
                duree.ToString(CultureInfo.InvariantCulture), avecDj ? FormeLigne.Texte(r, "TypeDj") : "", FormeLigne.DateFiche(datePrevue),
                FormeLigne.Texte(r, "Juge"), avecDj ? FormeLigne.Texte(r, "Localisation") : "",
            ],
        };
    }
}
