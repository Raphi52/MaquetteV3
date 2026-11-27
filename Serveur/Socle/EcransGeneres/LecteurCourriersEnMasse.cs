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
///   - volet « Préventions » : RIG v2 ne trouve une prevention que par son NUMERO (la zone ultTextNumeroMetier du volet, en mode
///     « NUMERO » : OPE_RECHERCHE_COURRIERM_PREVENTION.cs:26-33, :74-85), puis MetierProcPrevention._CreerRequete (:20-38) : « WHERE
///     PREVE_NUM_PRV = <numero> », le premier juge-commis JCP en mission, le premier mandataire MAH ou CON en mission (« CON » ecrit
///     « CONC »), la date d'ouverture, la solution et la cloture (synonyme : libelle, et leur date). En V3 : le parametre « N° de
///     prévention » ; sans numero, le volet est vide, comme a l'ouverture de RIG v2 ;
///   - volet « Mesures » : de meme par « N° de mesure » (OPE_RECHERCHE_COURRIERM_MESURE ; MetierProcMesure._CreerRequete, :20-30) --
///     « WHERE MSINS_NUM_MESURE = <numero> », le premier participant actif (l'expert), les dates d'ouverture et de cloture, le type
///     de mesure ; la mesure se nomme par son instance (Mesure.NomDemande = GetNomCourantMesure, Mesure.cs:39-42, :192-196).
///
/// NON REPRIS (nomme) : la recherche d'une instance par son numero (sous-mode NUMERO, :272) ; la recherche generale des preventions
/// (FORM_RECHERCHE_PREVENTION, :37-48 : RIG v2 n'en garde que la derniere, :61) ; le juge du volet Mesures, que RIG v2 joint par
/// « JUG_ID_JUG = MSINS_ID_MSINS » (MetierProcMesure.cs:29 : l'identifiant de la MESURE pris pour celui d'un juge, un defaut de
/// RIG v2 qui afficherait un juge sans rapport) ; les badges ; l'accumulation de recherches successives dans la grille (:66-69,
/// « déjà dans la liste ») : la V3 montre la recherche en cours.
/// </summary>
internal sealed class LecteurCourriersEnMasse : IRechercheEcran
{
    public string Procedure => "PROC_COURRIERM";

    /// <summary>Les groupes du descripteur (ModifierEnvois.cs), au mot pres : les volets de RIG v2.</summary>
    internal const string Instances = "Instances", Procedures = "Procédures", Preventions = "Préventions", Mesures = "Mesures";

    /// <summary>Les parametres de l'audience (le descripteur), que la recherche lit ; et les numeros des volets Préventions et
    /// Mesures (la zone de numero de chaque volet de RIG v2).</summary>
    internal const string CritereDate = "Date de l'audience", CritereChambre = "Chambre", CritereMode = "Mode",
                          CriterePrevention = "N° de prévention", CritereMesure = "N° de mesure";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDate, CritereChambre, CritereMode, CriterePrevention, CritereMesure];

    /// <summary>Le courrier du jour d'une entite : la derniere demande d'edition d'une demande COURRIERM creee aujourd'hui sur son
    /// sequenceur (la regle d'etat, ci-dessus). Pose dans un OUTER APPLY : x.Volet et x.IdObjet sont ceux de la ligne.</summary>
    private const string CourrierDuJour = """
        OUTER APPLY (SELECT TOP (1) de.DMNED_LIBELLE_DOCUMENT, de.DMNED_LOT, de.DMNED_IMPRIMANTE, COUNT(*) OVER () AS NombreCourriers
                     FROM v2.DEMANDE AS dm
                     JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = dm.DMND_ID_DMND
                     JOIN v2.DEMANDE_EDITION AS de ON de.DMNED_ID_DMND = dm.DMND_ID_DMND
                     WHERE dm.DMND_CODE_PROSS = 'COURRIERM' AND dm.DMND_DATE_CREATION >= @jour
                       AND dd.DSSDM_CODE_SEQUENCEUR = CASE x.Volet WHEN 0 THEN '2J' WHEN 1 THEN '2P' WHEN 2 THEN '2R' ELSE '2F' END
                       AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(100), x.IdObjet)
                     ORDER BY de.DMNED_ID_DMNED DESC) AS c
        """;

    /// <summary>Le volet Préventions (MetierProcPrevention._CreerRequete, :20-38), par son numero.</summary>
    private const string RequetePrevention = $$"""
        SELECT TOP (@plafond) x.*, c.DMNED_LIBELLE_DOCUMENT, c.DMNED_LOT, c.DMNED_IMPRIMANTE, c.NombreCourriers, COUNT(*) OVER () AS Total
        FROM (SELECT 2 AS Volet, p.PREVE_ID_PREVE AS IdObjet, p.PREVE_NUM_PRV AS Numero, p.PREVE_NOM_AFFAIRE AS Nom,
                     (SELECT TOP (1) ej.ETJCM_FONCTION_PC + ' : ' + j.JUG_NOM + ISNULL(' ' + j.JUG_PRENOM, '')
                      FROM v2.ETRE_JUGE_COMM AS ej JOIN v2.JUGE AS j ON ej.ETJCM_ID_JUG = j.JUG_ID_JUG
                      WHERE ej.ETJCM_FIN_MISSION = 0 AND ej.ETJCM_FONCTION_PC = 'JCP' AND ej.ETJCM_ID_PREVE = p.PREVE_ID_PREVE
                      ORDER BY ej.ETJCM_NUM_ORDRE) AS Juge1,
                     (SELECT TOP (1) REPLACE(g.GRRPC_FONCTION_PC, 'CON', 'CONC') + ' : ' + m.MNDPC_NOM + ISNULL(' ' + m.MNDPC_COMPLEMENT_NOM, '')
                      FROM v2.GERER_PC AS g JOIN v2.MANDATAIRE_PC AS m ON g.GRRPC_ID_MNDPC = m.MNDPC_ID_MNDPC
                      WHERE g.GRRPC_FIN_MISSION = 0 AND g.GRRPC_FONCTION_PC IN ('MAH', 'CON') AND g.GRRPC_ID_PREVE = p.PREVE_ID_PREVE
                      ORDER BY g.GRRPC_NUM_ORDRE) AS Mandataire1,
                     p.PREVE_DATE_OUVERTURE AS Ouverture,
                     ISNULL(s.R_CODESOLUTI_SYNONYME + ' : ' + s.R_CODESOLUTI_LIBELLE_ECRAN, '') AS Solution, p.PREVE_DATE_SOLUTION AS DateSolution,
                     ISNULL(cl.R_CODECLOTUR_SYNONYME + ' : ' + cl.R_CODECLOTUR_LIBELLE_ECRAN, '') AS Cloture, p.PREVE_DATE_CLOTURE AS DateCloture
              FROM v2.PREVENTION AS p
              LEFT JOIN v2.CODE_SOLUTION AS s ON p.PREVE_CODE_SOLUTION = s.R_CODESOLUTI_VALEUR
              LEFT JOIN v2.CODE_CLOTURE AS cl ON p.PREVE_CODE_CLOTURE = cl.R_CODECLOTUR_VALEUR
              WHERE p.PREVE_NUM_PRV = @numero) AS x
        {{CourrierDuJour}}
        ORDER BY x.Numero, x.IdObjet
        """;

    /// <summary>Le volet Mesures (MetierProcMesure._CreerRequete, :20-30), par son numero ; le nom : celui de l'instance liee.</summary>
    private const string RequeteMesure = $$"""
        SELECT TOP (@plafond) x.*, c.DMNED_LIBELLE_DOCUMENT, c.DMNED_LOT, c.DMNED_IMPRIMANTE, c.NombreCourriers, COUNT(*) OVER () AS Total
        FROM (SELECT 3 AS Volet, m.MSINS_ID_MSINS AS IdObjet, m.MSINS_NUM_MESURE AS Numero, i.INSTN_NOM_COURANT AS Nom,
                     (SELECT TOP (1) pa.PRTCP_NOM + ISNULL(' ' + pa.PRTCP_COMPLEMENT_NOM, '') FROM v2.PARTICIPANT AS pa
                      WHERE pa.PRTCP_ID_MSINS = m.MSINS_ID_MSINS AND pa.PRTCP_ACTIF = 1 ORDER BY pa.PRTCP_NUM_ORDRE) AS Expert1,
                     m.MSINS_DATE_OUVERTURE AS Ouverture, m.MSINS_DATE_CLOTURE AS DateCloture,
                     t.R_TYPEMESURE_SYNONYME + ' : ' + t.R_TYPEMESURE_LIBELLE_ECRAN AS TypeMesure
              FROM v2.MESURE AS m
              LEFT JOIN v2.TYPE_MESURE AS t ON m.MSINS_TYPE_MESURE = t.R_TYPEMESURE_VALEUR
              LEFT JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = m.MSINS_ID_INSTN
              WHERE m.MSINS_NUM_MESURE = @numero) AS x
        {{CourrierDuJour}}
        ORDER BY x.Numero, x.IdObjet
        """;

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
        {{CourrierDuJour}}
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
        var affaires = await LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);

        // fix-ok: constate par le chef du plan sur 8bc0a99 (27/11/2026) -- les groupes « Préventions » et « Mesures » restaient vides ;
        // RIG v2 les remplit par la zone de numero de chaque volet (OPE_RECHERCHE_COURRIERM_PREVENTION.cs:74-85), d'ou ces deux lectures.
        var lignes = new List<LigneLue>(affaires.Lignes);
        var total = affaires.Total;
        foreach (var (critere, requete) in new[] { (CriterePrevention, RequetePrevention), (CritereMesure, RequeteMesure) })
        {
            if (recherche.Critere(critere) is not { } numero) continue;
            var lus = await LectureEcran.LireAsync(baseGreffe, requete, Ligne, ct,
                new SqlParameter("@plafond", plafond), new SqlParameter("@jour", aujourdhui.Date), new SqlParameter("@numero", numero));
            lignes.AddRange(lus.Lignes);
            total += lus.Total;
        }
        return new LignesLues(lignes, total);
    }

    /// <summary>Le volet d'une ligne (la colonne Volet des requetes) : son groupe et le prefixe de son Id.</summary>
    private static (string Groupe, string Prefixe) Volet(int volet) => volet switch
    {
        0 => (Instances, "I-"), 1 => (Procedures, "P-"), 2 => (Preventions, "R-"), _ => (Mesures, "F-"),
    };

    private static LigneLue Ligne(SqlDataReader r)
    {
        var volet = Convert.ToInt32(r["Volet"], CultureInfo.InvariantCulture);
        if (volet >= 2) return LignePreventionOuMesure(r, volet);
        var instance = volet == 0;
        var numero = FormeLigne.Texte(r, "Numero");
        var (courrier, courriers) = Courrier(r);
        var appel = FormeLigne.Texte(r, "Appel");
        var precisions = FormeLigne.Joindre(instance ? $"Rôle {numero}" : $"PC {numero}", FormeLigne.Texte(r, "Detail"),
            FormeLigne.Texte(r, "Audience").TrimEnd(' ', '-'), appel.Length > 0 ? $"appel {appel}" : null, courrier);
        var nom = FormeLigne.Texte(r, "Nom");
        return new LigneLue(nom.Length > 0 ? nom : numero, precisions, instance ? Instances : Procedures, courriers > 0 ? "EnCours" : "AFaire", [])
        {
            Id = (instance ? "I-" : "P-") + FormeLigne.Texte(r, "IdObjet"),
        };
    }

    /// <summary>Le courrier du jour en mots (le OUTER APPLY CourrierDuJour) ; null sans courrier.</summary>
    private static (string? Texte, int Nombre) Courrier(SqlDataReader r)
    {
        var courriers = r["NombreCourriers"] is DBNull ? 0 : Convert.ToInt32(r["NombreCourriers"], CultureInfo.InvariantCulture);
        if (courriers == 0) return (null, 0);
        var sortie = FormeLigne.Texte(r, "DMNED_LOT") is { Length: > 0 } lot ? $"file « {lot} »"
                   : FormeLigne.Texte(r, "DMNED_IMPRIMANTE") is { Length: > 0 } imp ? $"imprimante « {imp} »" : null;
        return (FormeLigne.Joindre(courriers > 1 ? $"{courriers} courriers du jour, dernier « {FormeLigne.Texte(r, "DMNED_LIBELLE_DOCUMENT")} »"
                                                 : $"courrier du jour « {FormeLigne.Texte(r, "DMNED_LIBELLE_DOCUMENT")} »", sortie), courriers);
    }

    /// <summary>Une prevention (les colonnes de la grille de RIG v2 : RigDataGridViewPrevention, OPE_RESULTAT_COURRIERM_PREVENTION.cs:
    /// 85-96) ou une mesure (MetierProcMesure, :40-47).</summary>
    private static LigneLue LignePreventionOuMesure(SqlDataReader r, int volet)
    {
        static string? Date(string quoi, DateTime? d) => d is null ? null : $"{quoi} le {FormeLigne.Date(d)}";
        static string? Code(string texte, DateTime? d) => texte.Length == 0 ? null : d is null ? texte : $"{texte} le {FormeLigne.Date(d)}";
        var numero = FormeLigne.Texte(r, "Numero");
        var (courrier, nombre) = Courrier(r);
        var (groupe, prefixe) = Volet(volet);
        string precisions;
        if (volet == 2)
        {
            var solution = FormeLigne.Texte(r, "Solution");
            var cloture = FormeLigne.Texte(r, "Cloture");
            precisions = FormeLigne.Joindre($"PRV {numero}", Date("ouverte", FormeLigne.DateDe(r, "Ouverture")),
                FormeLigne.Texte(r, "Juge1") is { Length: > 0 } juge ? juge : null, FormeLigne.Texte(r, "Mandataire1") is { Length: > 0 } mand ? mand : null,
                solution.Length > 0 ? "solution " + Code(solution, FormeLigne.DateDe(r, "DateSolution")) : null,
                cloture.Length > 0 ? "clôture " + Code(cloture, FormeLigne.DateDe(r, "DateCloture")) : null, courrier);
        }
        else
            precisions = FormeLigne.Joindre($"Mesure {numero}", FormeLigne.Texte(r, "TypeMesure") is { Length: > 0 } type ? type : null,
                FormeLigne.Texte(r, "Expert1") is { Length: > 0 } expert ? $"expert {expert}" : null, Date("ouverte", FormeLigne.DateDe(r, "Ouverture")),
                Date("close", FormeLigne.DateDe(r, "DateCloture")), courrier);
        var nom = FormeLigne.Texte(r, "Nom");
        return new LigneLue(nom.Length > 0 ? nom : numero, precisions, groupe, nombre > 0 ? "EnCours" : "AFaire", [])
        {
            Id = prefixe + FormeLigne.Texte(r, "IdObjet"),
        };
    }
}
