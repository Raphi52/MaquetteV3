using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Statistiques cour d'appel » (PROC_STATCA) : le tableau mensuel « Activité commerciale » des affaires du greffe, CALCULE
/// dans la base. Un ETAT en lecture seule : les quatre lignes de categorie du tableau de RIG v2, ses sept colonnes ; la ligne
/// « totaux » est le pied du tableau (la forme Etat totalise chaque colonne Nombre, comme TotalColonne, OPE_GENERER.cs:596-601).
/// Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 9, 26/11/2026.
///
/// SOURCE (ancien RIG) : proc_statca.md (Detail OPE, qui fait foi) et Source\RIG\DLL\Processus\PROC_STATCA\OPE_GENERER.cs
/// (ChargerDataGrid, :132-593), dont chaque requete est reprise ici telle quelle, aux tables v2.* et aux dates en parametres :
///   - la categorie se lit dans le numero de role : J « contentieux général », F « affaires de procédure collective », R
///     « référés commerciaux », IP « injonctions de payer » (LIKE '%J[0-9]%'..., :101-108, :182-235) ;
///   - B en cours au debut : enrolees avant le debut, sans decision « terminante » (ni avant-dire-droit, ni sursis a statuer, ni
///     expertise) rendue ou prevue au plus tard au debut (:182-235) ; C nouvelles : enrolees dans la periode (:245-290) ; D
///     terminees : au moins une decision terminante dans la periode, enrolees au plus tard a la fin (:300-365) ; E jugements ADD :
///     une decision avant-dire-droit PREVUE dans la periode (:375-432) ; F stock = (B + C) - (D + E), calcule (:439-451, :603-607) ;
///     G deliberes de plus de deux mois : decisions RENDUES dans la periode dont le prononce suit le delibere de deux mois complets
///     au moins (dbo.NbMoisComplet, ecrite ici en ligne, sa definition lue dans RIG_DEV le 26/11/2026), 0 pour les IP (:457-522) ;
///     H decisions a delivrer : appel au plumitif « d... » (J), « dp... » (F), « df... » (R), hors « di », sans decision saisie,
///     0 pour les IP (:528-577) -- l'ecart J / F / R est celui du code (🐞 a trancher par le metier, la fiche le dit) ;
///   - les quatre compteurs du haut de l'ecran (:146-174), sur PROCEDURE_COLLECTIVE hors exterieur : redressements (ouverts dans la
///     periode, hors LIJI), liquidations (LJ ou LIJI prononcees dans la periode, solution 1 ou 2), clotures CIA et CEP -- portes par
///     les precisions de la ligne « affaires de procédure collective » (la forme Etat n'a qu'un tableau) ;
///   - la periode par defaut : le mois precedent complet (InitDate, OPE_RECHERCHE.cs:37-64) ; les dates sont des jours a 0 h, comme
///     les dates de l'ecran V2 converties (CONVERT(DATETIME, ..., 103)) : la fin est le dernier jour a 0 h (le code le fait ainsi) ;
///   - les gardes de LoadData (OPE_RECHERCHE.cs:79-98) : debut <= fin, l'une et l'autre au plus aujourd'hui -- sinon RIG v2 ne
///     calcule rien, la V3 non plus (aucune ligne).
/// GROUPE : « Activité commerciale » (le titre de l'etape, :585). ETAT : « Généré » (Validee).
/// L'export Word du tableau (PrintWord, :610-771) est une demande dans la file des editions : EcrivainStatistiquesCourAppel.cs (lot 1 du
/// plan-f4fb097f), par l'Id de la ligne (la categorie et la periode : EditionsV2.IdSurPeriode).
/// </summary>
internal sealed class LecteurStatistiquesCourAppel : IRechercheEcran
{
    public string Procedure => "PROC_STATCA";

    /// <summary>Le groupe et le critere du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string Groupe = "Activité commerciale";
    internal const string CriterePeriode = "Période";

    public IReadOnlyCollection<string> Criteres { get; } = [CriterePeriode];

    // Une decision « terminante » (OPE_GENERER.cs:187 et suivantes) ; t : TYPE_DECISION en LEFT JOIN, comme RIG v2.
    private const string Terminante = "ISNULL(d.DCJDC_AVANT_DIRE_DROIT, '0') <> '1' AND t.R_TYPEDECISI_SURSIS_STATUER = 0 AND t.R_TYPEDECISI_ADD_EXPERTISE = 0";

    // dbo.NbMoisComplet(@dStart, @dEnd) de RIG_DEV : DATEDIFF(MONTH) moins un si le mois n'est pas complet.
    private const string MoisComplets = """
        (DATEDIFF(MONTH, CAST(xd.AUDNC_DATE AS date), CAST(xp.AUDNC_DATE AS date))
         - CASE WHEN DATEADD(MONTH, DATEDIFF(MONTH, CAST(xd.AUDNC_DATE AS date), CAST(xp.AUDNC_DATE AS date)), CAST(xd.AUDNC_DATE AS date))
                     > CAST(xp.AUDNC_DATE AS date) THEN 1 ELSE 0 END)
        """;

    private static readonly string Requete = $$"""
        SELECT c.Rang, c.Libelle,
          (SELECT COUNT(DISTINCT i.INSTN_NUM_ROLE) FROM v2.INSTANCE AS i
           WHERE i.INSTN_NUM_ROLE LIKE c.Motif AND i.INSTN_DATE_ENROLEMENT < @debut
             AND (SELECT COUNT(*) FROM v2.DECISION_JUDICIAIRE AS d LEFT JOIN v2.TYPE_DECISION AS t ON d.DCJDC_TYPE_DECISION = t.R_TYPEDECISI_VALEUR
                  WHERE d.DCJDC_ID_INSTN = i.INSTN_ID_INSTN AND ISNULL(d.DCJDC_DATE, d.DCJDC_DATE_PREVUE) <= @debut AND {{Terminante}}) = 0) AS EnCours,
          (SELECT COUNT(DISTINCT i.INSTN_NUM_ROLE) FROM v2.INSTANCE AS i
           WHERE i.INSTN_NUM_ROLE LIKE c.Motif AND i.INSTN_DATE_ENROLEMENT >= @debut AND i.INSTN_DATE_ENROLEMENT <= @fin) AS Nouvelles,
          (SELECT COUNT(DISTINCT i.INSTN_NUM_ROLE) FROM v2.INSTANCE AS i
           WHERE i.INSTN_NUM_ROLE LIKE c.Motif AND i.INSTN_DATE_ENROLEMENT <= @fin
             AND (SELECT COUNT(*) FROM v2.DECISION_JUDICIAIRE AS d LEFT JOIN v2.TYPE_DECISION AS t ON d.DCJDC_TYPE_DECISION = t.R_TYPEDECISI_VALEUR
                  WHERE d.DCJDC_ID_INSTN = i.INSTN_ID_INSTN AND ISNULL(d.DCJDC_DATE, d.DCJDC_DATE_PREVUE) >= @debut
                    AND ISNULL(d.DCJDC_DATE, d.DCJDC_DATE_PREVUE) <= @fin AND {{Terminante}}) > 0) AS Terminees,
          (SELECT COUNT(DISTINCT i.INSTN_NUM_ROLE) FROM v2.INSTANCE AS i
           JOIN v2.DECISION_JUDICIAIRE AS d ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN JOIN v2.TYPE_DECISION AS t ON d.DCJDC_TYPE_DECISION = t.R_TYPEDECISI_VALEUR
           WHERE i.INSTN_NUM_ROLE LIKE c.Motif AND d.DCJDC_DATE_PREVUE >= @debut AND d.DCJDC_DATE_PREVUE <= @fin AND i.INSTN_DATE_ENROLEMENT <= @fin
             AND (t.R_TYPEDECISI_ADD_EXPERTISE = 1 OR t.R_TYPEDECISI_SURSIS_STATUER = 1 OR d.DCJDC_AVANT_DIRE_DROIT = '1')) AS Jugements,
          CASE WHEN c.SansIndicateurs = 1 THEN 0 ELSE
          (SELECT COUNT(DISTINCT d.DCJDC_ID_DCJDC) FROM v2.INSTANCE AS i
           JOIN v2.DECISION_JUDICIAIRE AS d ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
           JOIN v2.APPEL_AFFAIRE AS ad ON d.DCJDC_ID_APPAF_DELIBERER = ad.APPAF_ID_APPAF JOIN v2.AUDIENCE_CABINET AS xd ON ad.APPAF_ID_ADNC = xd.AUDNC_ID_ADNC
           JOIN v2.APPEL_AFFAIRE AS ap ON d.DCJDC_ID_APPAF_PRONONCER = ap.APPAF_ID_APPAF JOIN v2.AUDIENCE_CABINET AS xp ON ap.APPAF_ID_ADNC = xp.AUDNC_ID_ADNC
           WHERE i.INSTN_NUM_ROLE LIKE c.Motif AND d.DCJDC_DATE >= @debut AND d.DCJDC_DATE <= @fin AND {{MoisComplets}} >= 2) END AS Deliberes,
          CASE WHEN c.SansIndicateurs = 1 THEN 0 ELSE
          (SELECT COUNT(DISTINCT i.INSTN_NUM_ROLE) FROM v2.INSTANCE AS i
           JOIN v2.APPEL_AFFAIRE AS a ON i.INSTN_ID_INSTN = a.APPAF_ID_INSTN LEFT JOIN v2.DECISION_JUDICIAIRE AS d ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
           WHERE i.INSTN_NUM_ROLE LIKE c.Motif AND i.INSTN_DATE_ENROLEMENT <= @fin AND a.APPAF_CODE_PLUMITIF LIKE c.Plumitif
             AND a.APPAF_CODE_PLUMITIF <> 'di' AND d.DCJDC_ID_DCJDC IS NULL) END AS ADelivrer
        FROM (VALUES (0, N'contentieux général', '%J[0-9]%', 'd%', 0), (1, N'affaires de procédure collective', '%F[0-9]%', 'dp%', 0),
                     (2, N'référés commerciaux', '%R[0-9]%', 'df%', 0), (3, N'injonctions de payer', '%IP[0-9]%', '', 1))
             AS c (Rang, Libelle, Motif, Plumitif, SansIndicateurs)
        ORDER BY c.Rang
        """;

    // Les quatre compteurs (OPE_GENERER.cs:146-174), hors procedures exterieures.
    private const string Compteurs = """
        SELECT
          (SELECT COUNT(*) FROM v2.PROCEDURE_COLLECTIVE WHERE PRCLL_EXTERIEUR = 0
             AND (PRCLL_CODE_SOLUTION IS NULL OR PRCLL_CODE_SOLUTION NOT LIKE 'LIJI%') AND (PRCLL_CODE_SOLUTION_2 IS NULL OR PRCLL_CODE_SOLUTION_2 NOT LIKE 'LIJI%')
             AND PRCLL_DATE_OUVERTURE BETWEEN @debut AND @fin) AS Rj,
          (SELECT COUNT(*) FROM v2.PROCEDURE_COLLECTIVE WHERE PRCLL_EXTERIEUR = 0
             AND (((PRCLL_CODE_SOLUTION LIKE 'LJ%' OR PRCLL_CODE_SOLUTION LIKE 'LIJI%') AND PRCLL_DATE_SOLUTION BETWEEN @debut AND @fin)
               OR ((PRCLL_CODE_SOLUTION_2 LIKE 'LJ%' OR PRCLL_CODE_SOLUTION_2 LIKE 'LIJI%') AND PRCLL_DATE_SOLUTION_2 BETWEEN @debut AND @fin))) AS Lj,
          (SELECT COUNT(*) FROM v2.PROCEDURE_COLLECTIVE WHERE PRCLL_EXTERIEUR = 0 AND PRCLL_CODE_CLOTURE = 'CIA' AND PRCLL_DATE_CLOTURE BETWEEN @debut AND @fin) AS Cia,
          (SELECT COUNT(*) FROM v2.PROCEDURE_COLLECTIVE WHERE PRCLL_EXTERIEUR = 0 AND PRCLL_CODE_CLOTURE = 'CEP' AND PRCLL_DATE_CLOTURE BETWEEN @debut AND @fin) AS Cep
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var (debut, fin) = LecteurPilotage.MoisPrecedent(aujourdhui);
        return CalculerAsync(baseGreffe, debut, fin, aujourdhui, ct);
    }

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var (debut, fin) = LecteurPilotage.MoisPrecedent(aujourdhui);
        // La periode saisie « du|au » (une borne vide garde celle du mois precedent).
        if (recherche.Critere(CriterePeriode) is { } periode)
        {
            var bornes = periode.Split('|');
            debut = CriteresConsultation.Date(bornes[0]) ?? debut;
            fin = (bornes.Length > 1 ? CriteresConsultation.Date(bornes[1]) : null) ?? fin;
        }
        return CalculerAsync(baseGreffe, debut, fin, aujourdhui, ct);
    }

    private static async Task<LignesLues> CalculerAsync(SqlConnection baseGreffe, DateTime debut, DateTime fin, DateTime aujourdhui, CancellationToken ct)
    {
        // Les gardes de LoadData (OPE_RECHERCHE.cs:85-92).
        if (debut > fin || debut > aujourdhui.Date || fin > aujourdhui.Date) return new LignesLues([], 0);
        SqlParameter[] Bornes() => [new("@debut", System.Data.SqlDbType.DateTime) { Value = debut.Date }, new("@fin", System.Data.SqlDbType.DateTime) { Value = fin.Date }];

        var compteurs = "";
        await using (var r = await LectureV2.LireAsync(baseGreffe, Compteurs, ct, Bornes()))
            if (await r.ReadAsync(ct))
                compteurs = $"Nb redressement judiciaire : {r["Rj"]} · Nb liquidations (LIJI ou LJ) : {r["Lj"]} · "
                          + $"Nb clôtures pour insuffisance d'actif : {r["Cia"]} · Nb clôtures pour extinction du passif : {r["Cep"]}";

        var titre = $"Activité commerciale du {FormeLigne.Date(debut)} au {FormeLigne.Date(fin)}";
        var lignes = new List<LigneLue>();
        await using (var r = await LectureV2.LireAsync(baseGreffe, Requete, ct, Bornes()))
            while (await r.ReadAsync(ct))
            {
                int N(string colonne) => Convert.ToInt32(r[colonne], CultureInfo.InvariantCulture);
                var (b, c, d, e) = (N("EnCours"), N("Nouvelles"), N("Terminees"), N("Jugements"));
                var libelle = FormeLigne.Texte(r, "Libelle");
                var precisions = N("Rang") == 1 ? FormeLigne.Joindre(titre, compteurs) : titre;
                lignes.Add(new LigneLue(libelle, precisions, Groupe, "Validee", [])
                {
                    // La categorie (son rang) et la periode du tableau : ce que « Word » edite (EcrivainStatistiquesCourAppel.cs).
                    Id = EditionsV2.IdSurPeriode(N("Rang").ToString(CultureInfo.InvariantCulture), debut, fin),
                    Valeurs = [libelle, .. new[] { b, c, d, e, b + c - (d + e), N("Deliberes"), N("ADelivrer") }
                                          .Select(n => n.ToString(CultureInfo.InvariantCulture))],
                });
            }
        return new LignesLues(lignes, lignes.Count);
    }
}
