using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Retours CFE-NET » (PROC_CFENET_RET) : les retours XML de la CCI (CFE-NET) en erreur, a suivre par le greffe. Mode d'emploi :
/// LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026. Correction greffe et Amitel : EcrivainRetoursCfenet.cs.
/// Rien n'est recu ni envoye ici : les retours sont deja dans la base du greffe (CFE_LIASSE_XML), deposes par le service d'echange.
///
/// SOURCE (ancien RIG) : PROC_CFENET_RET.RefreshResultats et _Sql (D:\RigApplication\Source\RIG\DLL\Processus\PROC_CFENET_RET\
/// PROC_CFENET_RET\PROC_CFENET_RET.cs:43-166), aux criteres de l'ouverture (proc_cfenet_ret.md, OPE_REJET / OPE_TRAITER / OPE_MASQUER) :
///   - le domaine : LIAXML_CFENETRET_ETAT renseigne, passage CFE direct vers la CCI (PSSGC_CCI = 1, PSSGC_DIRECT = 1), jointures de _Sql
///     (CFE_LIASSE_XML_FILE, ACTION_RCS -> rec.ActionRegistre, DOSSIER_RCS -> rec.DossierRcs, PASSAGE_CFE ; PASSAGE_CFE_COR en LEFT) ;
///   - « Erreurs » cochees d'office (OPE_REJET.cs:36-39) : KoRejetDefinitif, KoRejetDoublon, KoFonctionnel, et KoPdfAbsent SEULEMENT
///     au-dela du delai CFENET_RET_NB_JOURS_PDF_ABSENT (2 jours par defaut) depuis l'envoi, et pas deja numerise (PSSGC_VIDI_GUID_LIASSE1
///     NULL : « PDF deja numerise » decoche, OPE_MASQUER) -- PROC.cs:112-122. Le reglage est lu dans soc.Reglage (la table PARAMETRAGE de
///     la V2 y est reprise, SOC-003) ; le jour est le parametre aujourdhui (le getdate() de la V2) ;
///   - « Traités » decoches (OPE_TRAITER, aucun defaut) ; corrections du greffe masquees (LIAXML_CFENETRET_COR_GREF_DATE NULL, :143) ;
///     traitees par Amitel masquees (LIAXML_CFENETRET_TRAITE_AMI_DATE NULL, :146-149) ;
///   - tri : LIAXML_CFENETRET_DATE_TRAITE decroissante (:96) ; a date egale, le numero du retour (ordre stable).
/// RECHERCHE : le numero saisi, EGAL au numero de gestion, au SIREN ou a un numero de liaison (PSSGC ou PSFCO), comme la V2 (:100-103).
/// La file V3 n'a pas de panneau de criteres : les cases de la V2 gardent leur valeur d'ouverture.
/// GROUPES : l'etat du retour, au libelle de la grille V2 (CASE de _Sql) : « Rejet définitif », « Rejet doublon », « Erreur
/// fonctionnelle », « PDF feuillet1 absent ».
/// ETAT : tout retour de cette liste est AFaire « À traiter » : la correction du greffe le fait sortir de la liste (elle est masquee a
/// l'ouverture), comme la V2.
/// </summary>
internal sealed class LecteurRetoursCfenet : IRechercheEcran
{
    public string Procedure => "PROC_CFENET_RET";

    /// <summary>Les etats d'erreur de la V2 et leurs libelles de grille (CASE de _Sql, PROC_CFENET_RET.cs:55-63), groupes du descripteur.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Erreurs = new Dictionary<string, string>
    {
        ["KoRejetDefinitif"] = "Rejet définitif", ["KoRejetDoublon"] = "Rejet doublon", ["KoFonctionnel"] = "Erreur fonctionnelle",
        ["KoPdfAbsent"] = "PDF feuillet1 absent",
    };

    public IReadOnlyCollection<string> Criteres { get; } = [];

    /// <summary>Le domaine et les jointures de _Sql (PROC_CFENET_RET.cs:66-74), partages avec l'ecrivain.</summary>
    internal const string Domaine = """
        FROM v2.CFE_LIASSE_XML AS x
        LEFT JOIN v2.PASSAGE_CFE_COR AS pc ON pc.PSFCO_ID_LIAFILE = x.LIAXML_ID_LIAFILE
        JOIN v2.CFE_LIASSE_XML_FILE AS lf ON lf.LIAFILE_ID_LIAFILE = x.LIAXML_ID_LIAFILE
        JOIN rec.ActionRegistre AS a ON a.Id = lf.LIAFILE_ID_ACTRC
        JOIN rec.DossierRcs AS d ON d.Id = a.IdDossier
        JOIN v2.PASSAGE_CFE AS p ON p.PSSGC_ID_PSSCF = a.IdPassageCfe
        WHERE x.LIAXML_CFENETRET_ETAT IS NOT NULL AND p.PSSGC_CCI = 1 AND p.PSSGC_DIRECT = 1
        """;

    private const string Requete = """
        SELECT TOP (@plafond) x.LIAXML_ID_LIAXML, x.LIAXML_CFENETRET_ETAT, x.LIAXML_DATE_ENVOI, x.LIAXML_CFENETRET_DATE_RECEP_XML,
               x.LIAXML_CFENETRET_DATE_TRAITE, LEFT(x.LIAXML_CFENETRET_DETAIL, 160) AS Detail, d.NumeroGestion, d.Siren,
               CASE WHEN pc.PSFCO_ID_PSFCO IS NOT NULL THEN pc.PSFCO_NUM_LIAISON ELSE p.PSSGC_NUM_LIAISON END AS NumLiaison,
               COUNT(*) OVER () AS Total
        /*domaine*/
          AND (x.LIAXML_CFENETRET_ETAT IN ('KoRejetDefinitif', 'KoRejetDoublon', 'KoFonctionnel')
               OR (x.LIAXML_CFENETRET_ETAT = 'KoPdfAbsent' AND p.PSSGC_VIDI_GUID_LIASSE1 IS NULL
                   AND DATEDIFF(DAY, x.LIAXML_DATE_ENVOI, @aujourdhui) >
                       ISNULL((SELECT TOP (1) TRY_CONVERT(int, r.Valeur) FROM soc.Reglage AS r WHERE r.Code = 'CFENET_RET_NB_JOURS_PDF_ABSENT'), 2)))
          AND x.LIAXML_CFENETRET_COR_GREF_DATE IS NULL
          AND x.LIAXML_CFENETRET_TRAITE_AMI_DATE IS NULL /*filtres*/
        ORDER BY x.LIAXML_CFENETRET_DATE_TRAITE DESC, x.LIAXML_ID_LIAXML
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), aujourdhui, plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        // Le numero, EGAL (PROC_CFENET_RET.cs:100-103) ; le numero de gestion s'ecrit aussi « 2001 B 00411 » a l'ecran.
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("(d.NumeroGestion = @numero OR d.Siren = @numero OR p.PSSGC_NUM_LIAISON = @numero OR pc.PSFCO_NUM_LIAISON = @numero)",
                ("@numero", recherche.Texte.Replace(" ", "", StringComparison.Ordinal)));
        return Lire(baseGreffe, f, aujourdhui, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe,
            Requete.Replace("/*domaine*/", Domaine, StringComparison.Ordinal).Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond), new SqlParameter("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = aujourdhui.Date }));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var etat = FormeLigne.Texte(r, "LIAXML_CFENETRET_ETAT");
        var envoi = FormeLigne.Date(FormeLigne.DateDe(r, "LIAXML_DATE_ENVOI"));
        var recu = FormeLigne.Date(FormeLigne.DateDe(r, "LIAXML_CFENETRET_DATE_RECEP_XML"));
        var traite = FormeLigne.Date(FormeLigne.DateDe(r, "LIAXML_CFENETRET_DATE_TRAITE"));
        var precisions = FormeLigne.Joindre(
            FormeLigne.Texte(r, "NumLiaison") is { Length: > 0 } liaison ? $"liaison {liaison}" : null,
            FormeLigne.Texte(r, "Siren") is { Length: > 0 } siren ? $"SIREN {FormeLigne.Siren(siren)}" : null,
            envoi.Length > 0 ? $"envoyée le {envoi}" : null, recu.Length > 0 ? $"retour reçu le {recu}" : null,
            traite.Length > 0 ? $"traité par la CCI le {traite}" : null, FormeLigne.Texte(r, "Detail"));
        return new LigneLue(FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion")), precisions,
            Erreurs.TryGetValue(etat, out var groupe) ? groupe : etat, "AFaire", [])
        {
            Id = FormeLigne.Texte(r, "LIAXML_ID_LIAXML"),
        };
    }
}
