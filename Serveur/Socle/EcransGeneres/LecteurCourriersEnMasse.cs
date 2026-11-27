using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Courriers en masse » (PROC_COURRIERM) : les instances et les procedures collectives d'une audience (ou d'un cabinet), a qui
/// generer en lot leur fiche ou une seconde edition. Mode d'emploi : LecteurEcran.cs ; les gestes : EcrivainCourriersEnMasse.cs.
/// Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// FORME « Audience » (et non « File ») : la recherche de RIG v2 part d'UNE audience ou d'un cabinet choisi par sa date, sa chambre
/// ou sa section (OPE_RECHERCHE_COURRIERM_INSTANCE.cs:147-185, proc_courrierm.md Detail OPE) ; ses affaires se chargent dans la
/// grille, on coche « fiche » ou « seconde édition », puis on genere. C'est la forme Audience (une audience choisie, ses affaires,
/// un geste sur les affaires cochees).
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_COURRIERM\Recherches\ :
///   - volet « Instances » : MetierProcInstances._CreerRequeteSQLAudienceCabinet (:39-52) -- INSTANCE JOIN APPEL_AFFAIRE JOIN
///     AUDIENCE_CABINET de l'audience, code affaire, nature, acte et date de l'acte, tries APPAF_NUM_APPEL puis INSTN_NUM_ROLE ; une
///     instance deja dans la grille n'y entre pas deux fois (OPE_RESULTAT_COURRIERM_INSTANCE.ChargerDataGridView, :72) ;
///   - volet « Procédures » : MetierProcProcedure._CreerRequeteSQLAudienceCabinet (:47-73) -- les procedures collectives des
///     instances de l'audience (INSTANCE.INSTN_ID_PRCLL), date d'ouverture COALESCE(retablissement, sauvegarde, ouverture), tries
///     APPAF_NUM_APPEL puis PRCLL_NUM_PC ; une par procedure ;
///   - l'audience : AUDNC_AUDIENCE = 1 pour une audience, 0 pour un cabinet (le mode, :155) ; la date de la recherche par defaut :
///     [jour - INTERV_AUD_DATE_MIN_PREAUD ; jour + INTERV_AUD_DATE_MAX_PREAUD] (INTERV_CAB_* pour un cabinet), 3 jours de part et
///     d'autre sans reglage (_GetParametrageDates, :375 ; _MettreDateAudienceDefaut, :426-431) -- AudiencesV2.FenetreAsync.
///     RIG v2 exige UNE audience (« Plusieurs audiences ... compléter les critères », :175-184) : la V3 montre les affaires de
///     toutes celles du jour choisi, l'audience dans les precisions.
///
/// REGLE D'ETAT : RIG v2 ne marque pas une affaire deja traitee ; la base le dit -- une demande COURRIERM de l'affaire creee
/// aujourd'hui (Instance / ProcedureCollective.CreateDemande, sequenceur 2J / 2P), avec sa demande d'edition :
///   - EnCours « Courrier du jour généré » : une telle demande existe -- l'edition et sa file sont dans les precisions ; RIG v2
///     permet de regenerer (aucun controle) : la ligne reste cochable ;
///   - AFaire « Sans courrier du jour » : aucune.
/// Id = « I-<INSTN_ID_INSTN> » ou « P-<PRCLL_ID_PRCLL> ».
///
/// NON REPRIS (nomme) : les volets « Préventions » et « Mesures », que RIG v2 ne trouve que par leur NUMERO (« WHERE PREVE_NUM_PRV =
/// », MetierProcPrevention.cs:38 ; « WHERE MSINS_NUM_MESURE = », MetierProcMesure.cs:30), une saisie que l'ecran d'audience n'a
/// pas : leurs groupes restent vides ; la recherche d'une instance par son numero (sous-mode NUMERO, :272) ; les badges.
/// </summary>
internal sealed class LecteurCourriersEnMasse : IRechercheEcran
{
    public string Procedure => "PROC_COURRIERM";

    /// <summary>Les groupes du descripteur (ModifierEnvois.cs), au mot pres : les volets de RIG v2.</summary>
    internal const string Instances = "Instances", Procedures = "Procédures";

    /// <summary>Les parametres de l'audience (le descripteur), que la recherche lit.</summary>
    internal const string CritereDate = "Date de l'audience", CritereChambre = "Chambre", CritereMode = "Mode";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDate, CritereChambre, CritereMode];

    // Les audiences choisies (le filtre se pose a la place de /*filtre*/, deux fois : une par volet). Pas de WITH : la porte de lecture
    // V2 n'admet qu'une requete qui commence par SELECT (LectureV2.ExigerLecture).
    private const string Audiences = $$"""
        (SELECT a.AUDNC_ID_ADNC, a.AUDNC_DATE, {{AudiencesV2.Libelle}} AS Audience
         FROM v2.AUDIENCE_CABINET AS a
         {{AudiencesV2.Jointures}}
         WHERE /*filtre*/)
        """;

    private const string Requete = $$"""
        SELECT TOP (@plafond) x.Volet, x.IdObjet, x.Numero, x.Nom, x.Audience, x.Appel, x.Detail, c.DMNED_LIBELLE_DOCUMENT, c.DMNED_LOT,
               c.DMNED_IMPRIMANTE, c.NombreCourriers, COUNT(*) OVER () AS Total
        FROM (
            SELECT * FROM (
                SELECT 0 AS Volet, i.INSTN_ID_INSTN AS IdObjet, i.INSTN_NUM_ROLE AS Numero, i.INSTN_NOM_COURANT AS Nom, aud.AUDNC_DATE,
                       aud.AUDNC_ID_ADNC, aud.Audience, ap.APPAF_NUM_APPEL AS Appel,
                       ISNULL(ca.R_CODEAFFAIR_SYNONYME + ' : ' + ISNULL(ca.R_CODEAFFAIR_LIBELLE_ECRAN, ''), i.INSTN_CODE_AFFAIRE) AS Detail,
                       ROW_NUMBER() OVER (PARTITION BY i.INSTN_ID_INSTN ORDER BY aud.AUDNC_DATE, aud.AUDNC_ID_ADNC, ap.APPAF_NUM_APPEL) AS Rang
                FROM {{Audiences}} AS aud
                JOIN v2.APPEL_AFFAIRE AS ap ON ap.APPAF_ID_ADNC = aud.AUDNC_ID_ADNC
                JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = ap.APPAF_ID_INSTN
                LEFT JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
                WHERE 1 = 1 /*texte*/
            ) AS ins WHERE ins.Rang = 1
            UNION ALL
            SELECT * FROM (
                SELECT 1 AS Volet, p.PRCLL_ID_PRCLL AS IdObjet, p.PRCLL_NUM_PC AS Numero, p.PRCLL_NOM_AFFAIRE AS Nom, aud.AUDNC_DATE,
                       aud.AUDNC_ID_ADNC, aud.Audience, ap.APPAF_NUM_APPEL AS Appel,
                       'ouverte le ' + CONVERT(varchar(10), COALESCE(p.PRCLL_DATE_RETABL_PROF, p.PRCLL_DATE_SAUVEGARDE, p.PRCLL_DATE_OUVERTURE), 103) AS Detail,
                       ROW_NUMBER() OVER (PARTITION BY p.PRCLL_ID_PRCLL ORDER BY aud.AUDNC_DATE, aud.AUDNC_ID_ADNC, ap.APPAF_NUM_APPEL) AS Rang
                FROM {{Audiences}} AS aud
                JOIN v2.APPEL_AFFAIRE AS ap ON ap.APPAF_ID_ADNC = aud.AUDNC_ID_ADNC
                JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = ap.APPAF_ID_INSTN
                JOIN v2.PROCEDURE_COLLECTIVE AS p ON p.PRCLL_ID_PRCLL = i.INSTN_ID_PRCLL
                WHERE 1 = 1 /*textepc*/
            ) AS pc WHERE pc.Rang = 1
        ) AS x
        OUTER APPLY (SELECT TOP (1) de.DMNED_LIBELLE_DOCUMENT, de.DMNED_LOT, de.DMNED_IMPRIMANTE, COUNT(*) OVER () AS NombreCourriers
                     FROM v2.DEMANDE AS dm
                     JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = dm.DMND_ID_DMND
                     JOIN v2.DEMANDE_EDITION AS de ON de.DMNED_ID_DMND = dm.DMND_ID_DMND
                     WHERE dm.DMND_CODE_PROSS = 'COURRIERM' AND dm.DMND_DATE_CREATION >= @jour
                       AND dd.DSSDM_CODE_SEQUENCEUR = CASE x.Volet WHEN 0 THEN '2J' ELSE '2P' END
                       AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(100), x.IdObjet)
                     ORDER BY de.DMNED_ID_DMNED DESC) AS c
        ORDER BY x.Volet, x.AUDNC_DATE, x.AUDNC_ID_ADNC, x.Appel, x.Numero, x.IdObjet
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => ChercherAsync(baseGreffe, new RechercheEcran(null, new Dictionary<string, string>()), aujourdhui, plafond, ct);

    public async Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var cabinet = AudiencesV2.Mode(recherche.Critere(CritereMode)) == false;
        var (du, au) = cabinet
            ? await AudiencesV2.FenetreAsync(baseGreffe, aujourdhui, "INTERV_CAB_DATE_MIN_PREAUD", "INTERV_CAB_DATE_MAX_PREAUD", 3, 3, ct)
            : await AudiencesV2.FenetreAsync(baseGreffe, aujourdhui, "INTERV_AUD_DATE_MIN_PREAUD", "INTERV_AUD_DATE_MAX_PREAUD", 3, 3, ct);
        var parametres = new List<SqlParameter> { new("@plafond", plafond), new("@jour", aujourdhui.Date) };
        var sql = Requete.Replace("/*filtre*/", AudiencesV2.Filtre(recherche, CritereDate, CritereChambre, CritereMode, true, du, au, parametres),
                                  StringComparison.Ordinal);
        // Le texte de la recherche filtre les affaires chargees (numero ou nom), dans la base.
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            sql = sql.Replace("/*texte*/", "AND (i.INSTN_NUM_ROLE LIKE @texte OR i.INSTN_NOM_COURANT LIKE @texte)", StringComparison.Ordinal)
                     .Replace("/*textepc*/", "AND (p.PRCLL_NUM_PC LIKE @texte OR p.PRCLL_NOM_AFFAIRE LIKE @texte)", StringComparison.Ordinal);
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        return await LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var instance = Convert.ToInt32(r["Volet"], CultureInfo.InvariantCulture) == 0;
        var numero = FormeLigne.Texte(r, "Numero");
        var courriers = r["NombreCourriers"] is DBNull ? 0 : Convert.ToInt32(r["NombreCourriers"], CultureInfo.InvariantCulture);
        var sortie = FormeLigne.Texte(r, "DMNED_LOT") is { Length: > 0 } lot ? $"file « {lot} »"
                   : FormeLigne.Texte(r, "DMNED_IMPRIMANTE") is { Length: > 0 } imp ? $"imprimante « {imp} »" : null;
        var courrier = courriers == 0 ? null
            : FormeLigne.Joindre(courriers > 1 ? $"{courriers} courriers du jour, dernier « {FormeLigne.Texte(r, "DMNED_LIBELLE_DOCUMENT")} »"
                                               : $"courrier du jour « {FormeLigne.Texte(r, "DMNED_LIBELLE_DOCUMENT")} »", sortie);
        var appel = FormeLigne.Texte(r, "Appel");
        var precisions = FormeLigne.Joindre(instance ? $"Rôle {numero}" : $"PC {numero}", FormeLigne.Texte(r, "Detail"),
            FormeLigne.Texte(r, "Audience").TrimEnd(' ', '-'), appel.Length > 0 ? $"appel {appel}" : null, courrier);
        var nom = FormeLigne.Texte(r, "Nom");
        return new LigneLue(nom.Length > 0 ? nom : numero, precisions, instance ? Instances : Procedures, courriers > 0 ? "EnCours" : "AFaire", [])
        {
            Id = (instance ? "I-" : "P-") + FormeLigne.Texte(r, "IdObjet"),
        };
    }
}
