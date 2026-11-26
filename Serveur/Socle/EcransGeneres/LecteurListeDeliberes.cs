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
/// LA RECHERCHE (IRechercheEcran : la forme Etat envoie le texte et les parametres a la base, web/src/ecrans/formes/FormeEtat.tsx:9-13) :
/// les criteres de RIG v2, qui relance sa requete (NewTraitement, :154-262) :
///   - « Délibérés au » : la date de reference (dtpReference, :161-171) ; vide = le dernier jour du mois precedent (:61-63) ;
///   - « Durée du délibéré en jours » (txtDureeDelib, :176) ; vide = 60. Une duree qui n'est pas un entier : RIG v2 refuse de
///     chercher (« La durée du délibéré est incorrect, veuillez la corriger », :90-94) -- la V3 ne rend aucune ligne ;
///   - les types d'instance, trois cases comme RIG v2 (chkbContentieux « 07 », chkbProcedureCollective « 08 », chkbRefere « 09 »,
///     Designer :491-521 ; TypeInstanceGenererSQL, :352-372) : les cases cochees ; aucune cochee = Contentieux, la case cochee par
///     defaut de RIG v2 (Designer :513) -- les cases de la V3 partent decochees, RIG v2 refuse « aucun type » (:96-99) ;
///   - « Juge de l'instance » (rcboJuge, :173-175) : le juge ACTIF de ce libelle (« NOM Prenom », la liste de RIG v2, :67) ;
///   - « Date d'enrôlement min » (dtpEnrolement, :176-178) : enrolee ce jour ou apres ;
///   - le texte : le numero de role ou le nom de l'affaire.
///
/// CE QUE LA V3 NE REPREND PAS : « Editer le résultat » (un document Word, AfficherWord, :263-322) et « Délibérés repoussés non rendus »
/// (un classeur Excel, _GenererExcelDeliberer, :482-503) produisent un fichier sur le poste : la V3 n'a pas encore d'edition de ce genre.
/// </summary>
internal sealed class LecteurListeDeliberes : IRechercheEcran
{
    public string Procedure => "PROC_LISTE_DELIBERES";

    /// <summary>Le groupe et l'etat du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string Groupe = "Délibérés non rendus";

    /// <summary>Les valeurs par defaut de RIG v2 (frmListeDelibere.Designer.cs:513-521, :557).</summary>
    internal const int DureeParDefaut = 60;

    /// <summary>Les parametres du descripteur (Surveiller.cs), au mot pres : les libelles de frmListeDelibere.Designer.cs.</summary>
    internal const string CritereReference = "Délibérés au", CritereDuree = "Durée du délibéré en jours", CritereContentieux = "Contentieux",
                          CriterePc = "Procédures Collectives", CritereRefere = "Référés", CritereJuge = "Juge de l'instance",
                          CritereEnrolement = "Date d'enrôlement min";

    /// <summary>Les cases des types d'instance et leur code (Tag, Designer :491, :505, :521).</summary>
    private static readonly (string Case, string Type)[] Types = [(CritereContentieux, "07"), (CriterePc, "08"), (CritereRefere, "09")];

    public IReadOnlyCollection<string> Criteres { get; } =
        [CritereReference, CritereDuree, CritereContentieux, CriterePc, CritereRefere, CritereJuge, CritereEnrolement];

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
        WHERE i.INSTN_TYPE_INSTANCE IN (/*types*/) AND i.INSTN_DATE_ENROLEMENT <= @ref AND d.DateDelib IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM v2.DECISION_JUDICIAIRE AS x WHERE x.DCJDC_ID_INSTN = i.INSTN_ID_INSTN
                          AND ISNULL(x.DCJDC_DATE, x.DCJDC_DATE_PREVUE) >= d.DateDelib AND ISNULL(x.DCJDC_DATE, x.DCJDC_DATE_PREVUE) < @ref)
          AND DATEDIFF(day, d.DateDelib, @ref) >= @duree /*filtres*/
        ORDER BY i.INSTN_NUM_ROLE DESC, i.INSTN_ID_INSTN DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => ChercherAsync(baseGreffe, new RechercheEcran(null, new Dictionary<string, string>()), aujourdhui, plafond, ct);

    // fix-ok: second controle du lot 7 (26/11/2026) -- les criteres de frmListeDelibere.cs (:161-178) n'atteignaient pas la base : ce
    // lecteur n'etait pas IRechercheEcran, alors que la forme Etat envoie ses parametres a un lecteur qui cherche (FormeEtat.tsx:9-13).
    // Une seule cause, portee par trois remplacements (en-tete et criteres, requete parametree, recherche).
    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        // La date de reference : saisie, sinon le dernier jour du mois precedent (frmListeDelibere.cs:61-63).
        var reference = CriteresConsultation.Date(recherche.Critere(CritereReference)) ?? new DateTime(aujourdhui.Year, aujourdhui.Month, 1).AddDays(-1);
        var duree = DureeParDefaut;
        // Une duree illisible : RIG v2 ne cherche pas (:90-94).
        if (recherche.Critere(CritereDuree) is { } saisie && !int.TryParse(saisie, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out duree))
            return Task.FromResult(new LignesLues([], 0));
        var types = Types.Where(t => recherche.Critere(t.Case) == "oui").Select(t => t.Type).DefaultIfEmpty("07").ToList();
        var filtres = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            filtres.Ajouter("i.INSTN_NUM_ROLE LIKE @texte OR i.INSTN_NOM_COURANT LIKE @texte", ("@texte", FormeLigne.Contient(recherche.Texte)));
        if (recherche.Critere(CritereJuge) is { } juge)
            filtres.Ajouter("i.INSTN_ID_JUG IN (SELECT JUG_ID_JUG FROM v2.JUGE WHERE JUG_ACTIF = 1 AND JUG_NOM + ISNULL(' ' + JUG_PRENOM, '') = @juge)",
                            ("@juge", juge));
        if (CriteresConsultation.Date(recherche.Critere(CritereEnrolement)) is { } enrolement)
            filtres.Ajouter("i.INSTN_DATE_ENROLEMENT >= @enrolement", ("@enrolement", enrolement.Date));
        var sql = Requete.Replace("/*types*/", string.Join(", ", types.Select((_, k) => $"@type{k}")), StringComparison.Ordinal)
                         .Replace("/*filtres*/", filtres.EnSuite, StringComparison.Ordinal);
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, filtres.Parametres(
            [new SqlParameter("@plafond", plafond), new SqlParameter("@ref", reference.Date), new SqlParameter("@duree", duree),
             .. types.Select((t, k) => new SqlParameter($"@type{k}", t))]));
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
