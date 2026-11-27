using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Radiations d'office » (PROC_ROFSURVEILLANCES) : les dossiers a radier d'office a l'issue d'une surveillance.
/// Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RCS\Exe\PROC_ROFSurveillances\FrmProcROFSurveillances.cs. L'ecran
/// V2 coche des surveillances et lance une requete par surveillance (_rechercheDossiers, l. 300), puis retire les
/// dossiers d'une procedure collective (_exclusionPC, l. 1081) et trie par surveillance puis numero de gestion
/// (_orderBy, l. 724, tri par defaut). Le descripteur (FonctionsRetrouvees.cs) en garde trois, un groupe chacune :
///   S123-124 « Dissolution par survenance du terme » (_requeteS123124, l. 1055) : personne morale dont le terme
///            (INSPM_EXPIRATION_PM) est passe de 15 mois, sans mention d'office MOTER diffusable ;
///   S123-131 « Dissolution depuis plus de trois ans » : dissolution (ou fin de sa prorogation) de plus de 3 ans ;
///            avec le reglage ROFSURV_S123_136 a « 1 » (l. 115), seuls les dossiers RELANCES dont la relance a
///            expire (_requeteS123131, l. 822) ; sinon (case decochee par defaut, Designer), ceux qui n'ont PAS de
///            relance encore en cours (_requeteS123131SansRelance, l. 852) ;
///   SSAC2A   « Sans activite » (_requeteSsac2a, l. 975) : immatricule provisoirement sans activite, relance expiree.
/// Communs : dossier non radie, sans procedure collective (DSSRC_PROCEDURE_PC) ; une relance « expiree » est la
/// premiere (RLCRC_NUM_ORDRE = 1) d'une diligence active (DLGRC_ETAT_DILIGENCE = 1, DLGRC_ETAT_RELANCE = 2) dont la
/// date limite est passee. Dans la base V3, DOSSIER_RCS est rec.DossierRcs et ACTION_RCS rec.ActionRegistre
/// (Serveur/Recopie/FichesV3.cs) ; DILIGENCE_RCS, RELANCE_RCS, INSCRIT_PM, MENTION_OBSERVATION, PROCEDURE_COLLECTIVE,
/// CODE_SOLUTION, CODE_CLOTURE sont les tables V2 telles quelles (v2.*).
/// Non repris : l'option « Exclure les dossiers provoquant une notification » (_exclusionNotif, decochee par
/// defaut) et les autres surveillances de l'ecran V2 (S123-129-2, S123-130, S123-136, S123-171, 90P...).
///
/// REGLE D'ETAT. Radier, dans la V2, cree une demande « ROF » sur le dossier (_radiationOffice, l. 391-393 :
/// Demande.CreateDemande(..., "ROF", ...), DMND_ID_DSRCS = le dossier). L'etat d'une ligne est celui de la
/// derniere demande ROF du dossier creee depuis la relance (ou depuis le terme), hors demande supprimee (X) :
///   aucune                       -> AFaire  « A radier » : aucune marche franchie ;
///   V Validee, T Terminee        -> Validee « Lettre editee » : toutes les marches (0 a 3) ;
///   tout autre etat (Q, A, I...) -> EnCours « En cours » : surveillance et dossier vus (0, 1).
/// Un dossier radie (DateRadiation) sort de la liste, comme dans la V2 : une radiation achevee n'y reste pas.
/// </summary>
internal sealed class LecteurRadiationsOffice : ILecteurEcran
{
    public string Procedure => "PROC_ROFSURVEILLANCES";

    /// <summary>Les groupes du descripteur, au mot pres, par code de surveillance de la V2.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Groupes = new Dictionary<string, string>
    {
        ["S123-124"] = "Dissolution par survenance du terme",
        ["S123-131"] = "Dissolution depuis plus de trois ans",
        ["SSAC2A"] = "Sans activité",
    };

    /// <summary>« Ne selectionner que les societes ayant fait l'objet d'une relance » : lu « == "1" »
    /// (FrmProcROFSurveillances.cs:115-117) ; absent, la case reste decochee (Designer).</summary>
    internal static readonly LectureReglage RelanceSeule = new("ROFSURV_S123_136", "", "FrmProcROFSurveillances.cs:115");

    // Les morceaux de la requete : la relance « expiree » et son contraire, les memes dans chaque branche.
    private const string RelanceExpiree =
        "g.DLGRC_ETAT_DILIGENCE = '1' AND g.DLGRC_ETAT_RELANCE = '2' AND r.RLCRC_NUM_ORDRE = 1 AND r.RLCRC_DATE_LIMITE <= @aujourdhui";
    private const string Relances =
        "JOIN rec.ActionRegistre AS a ON a.IdDossier = s.Id JOIN v2.DILIGENCE_RCS AS g ON g.DLGRC_ID_ACTRC = a.Id " +
        "JOIN v2.RELANCE_RCS AS r ON r.RLCRC_ID_DLGRC = g.DLGRC_ID_DLGRC";
    private const string DissouteDepuisTroisAns =
        "((p.INSPM_DATE_EXPIRATION_PRORO_DISS IS NOT NULL AND DATEADD(YEAR, 3, p.INSPM_DATE_EXPIRATION_PRORO_DISS) <= @aujourdhui) " +
        "OR (p.INSPM_DATE_EXPIRATION_PRORO_DISS IS NULL AND DATEADD(YEAR, 3, p.INSPM_DATE_FORMALITE_DISSOLUTION) <= @aujourdhui))";

    // S123-124 (_requeteS123124) : le LEFT JOIN ... MNTOB_CODE_MENTION IS NULL de la V2, ecrit NOT EXISTS.
    private const string S123124 = """
        SELECT 'S123-124' AS Surveillance, s.Id AS IdDossier, s.NumeroGestion, s.Siren, s.IdEntreprise, s.DateMaj,
               CAST(0 AS bit) AS AvecRelance, CAST(NULL AS datetime) AS DateRelance, p.INSPM_EXPIRATION_PM AS DateLimite
        FROM rec.DossierRcs AS s JOIN v2.INSCRIT_PM AS p ON p.INSPM_ID_DSRCS = s.Id
        WHERE s.DateRadiation IS NULL AND s.ProcedureCollective IS NULL
          AND NOT EXISTS (SELECT 1 FROM v2.MENTION_OBSERVATION AS mo
                          WHERE mo.MNTOB_ID_DSRCS = s.Id AND mo.MNTOB_CODE_MENTION = 'MOTER' AND mo.MNTOB_DIFFUSABLE = 1)
          AND DATEADD(MONTH, 15, p.INSPM_EXPIRATION_PM) <= @aujourdhui
        """;

    // S123-131 avec relance (_requeteS123131).
    private static readonly string S123131Relance = $"""
        SELECT DISTINCT 'S123-131' AS Surveillance, s.Id AS IdDossier, s.NumeroGestion, s.Siren, s.IdEntreprise, s.DateMaj,
               CAST(1 AS bit) AS AvecRelance, r.RLCRC_DATE_RELANCE AS DateRelance, r.RLCRC_DATE_LIMITE AS DateLimite
        FROM rec.DossierRcs AS s JOIN v2.INSCRIT_PM AS p ON p.INSPM_ID_DSRCS = s.Id {Relances}
        WHERE s.DateRadiation IS NULL AND s.TypeRcs NOT IN ('A2', 'B2', 'C2', 'D2', 'I2') AND s.ProcedureCollective IS NULL
          AND g.DLGRC_NATURE_DILIGENCE = 'S123-131' AND {RelanceExpiree} AND {DissouteDepuisTroisAns}
        """;

    // S123-131 sans relance (_requeteS123131SansRelance) : la V2 joint action_rcs (le dossier en a une) et ecarte
    // une relance S123-131 encore en cours (date limite a venir) ; la date limite affichee = dissolution + 3 mois.
    private static readonly string S123131SansRelance = $"""
        SELECT DISTINCT 'S123-131' AS Surveillance, s.Id AS IdDossier, s.NumeroGestion, s.Siren, s.IdEntreprise, s.DateMaj,
               CAST(0 AS bit) AS AvecRelance, p.INSPM_DATE_FORMALITE_DISSOLUTION AS DateRelance,
               DATEADD(MONTH, 3, p.INSPM_DATE_FORMALITE_DISSOLUTION) AS DateLimite
        FROM rec.DossierRcs AS s JOIN v2.INSCRIT_PM AS p ON p.INSPM_ID_DSRCS = s.Id
        WHERE s.DateRadiation IS NULL AND s.TypeRcs NOT IN ('A2', 'B2', 'C2', 'D2', 'I2') AND s.ProcedureCollective IS NULL
          AND EXISTS (SELECT 1 FROM rec.ActionRegistre AS a0 WHERE a0.IdDossier = s.Id)
          AND NOT EXISTS (SELECT 1 FROM rec.ActionRegistre AS a JOIN v2.DILIGENCE_RCS AS g ON g.DLGRC_ID_ACTRC = a.Id
                          JOIN v2.RELANCE_RCS AS r ON r.RLCRC_ID_DLGRC = g.DLGRC_ID_DLGRC
                          WHERE a.IdDossier = s.Id AND g.DLGRC_NATURE_DILIGENCE = 'S123-131' AND g.DLGRC_ETAT_DILIGENCE = '1'
                            AND g.DLGRC_ETAT_RELANCE = '2' AND r.RLCRC_NUM_ORDRE = 1 AND r.RLCRC_DATE_LIMITE >= @aujourdhui)
          AND {DissouteDepuisTroisAns}
        """;

    // SSAC2A (_requeteSsac2a) : INSPM_DATE_DISSOLUTION « = '' or is null », tel quel.
    private static readonly string Ssac2a = $"""
        SELECT 'SSAC2A' AS Surveillance, s.Id AS IdDossier, s.NumeroGestion, s.Siren, s.IdEntreprise, s.DateMaj,
               CAST(1 AS bit) AS AvecRelance, r.RLCRC_DATE_RELANCE AS DateRelance, r.RLCRC_DATE_LIMITE AS DateLimite
        FROM rec.DossierRcs AS s JOIN v2.INSCRIT_PM AS p ON p.INSPM_ID_DSRCS = s.Id {Relances}
        WHERE s.DateRadiation IS NULL AND s.TypeRcs NOT IN ('A2', 'B2', 'C2', 'D2')
          AND s.DateFormaliteCessation IS NULL AND s.DateCessation IS NULL AND s.ProvisoirementSans = 1
          AND (p.INSPM_DATE_DISSOLUTION = '' OR p.INSPM_DATE_DISSOLUTION IS NULL)
          AND g.DLGRC_NATURE_DILIGENCE = 'SSAC2A' AND {RelanceExpiree} AND s.ProcedureCollective IS NULL
        """;

    /// <summary>Les trois surveillances, l'exclusion des procedures collectives (_exclusionPC : une procedure dont le
    /// code de cloture ou de solution n'autorise pas la surveillance, ou qui n'a ni l'un ni l'autre, ecarte le
    /// dossier), la demande ROF qui donne l'etat, le tri de la V2 et le plafond.</summary>
    internal static string Requete(bool relanceSeule) => $"""
        SELECT TOP (@plafond) l.Surveillance, l.IdDossier, l.NumeroGestion, l.Siren, e.Denomination, l.DateMaj, l.AvecRelance,
               l.DateRelance, l.DateLimite, rof.DMND_ETAT_DEMANDE AS EtatRof, rof.DMND_DATE_CREATION AS DateRof,
               COUNT(*) OVER () AS Total
        FROM ({S123124}
              UNION ALL {(relanceSeule ? S123131Relance : S123131SansRelance)}
              UNION ALL {Ssac2a}) AS l
        LEFT JOIN rec.Entreprise AS e ON e.Id = l.IdEntreprise
        OUTER APPLY (SELECT TOP (1) d.DMND_ETAT_DEMANDE, d.DMND_DATE_CREATION FROM v2.DEMANDE AS d
                     WHERE d.DMND_ID_DSRCS = l.IdDossier AND d.DMND_CODE_PROSS = 'ROF' AND d.DMND_ETAT_DEMANDE <> 'X'
                       AND d.DMND_DATE_CREATION >= COALESCE(l.DateRelance, l.DateLimite)
                     ORDER BY d.DMND_ID_DMND DESC) AS rof
        WHERE NOT EXISTS (SELECT 1 FROM v2.PROCEDURE_COLLECTIVE AS pc
                          LEFT JOIN v2.CODE_SOLUTION AS so ON so.R_CODESOLUTI_VALEUR = pc.PRCLL_CODE_SOLUTION
                          LEFT JOIN v2.CODE_CLOTURE AS cl ON cl.R_CODECLOTUR_VALEUR = pc.PRCLL_CODE_CLOTURE
                          WHERE pc.PRCLL_ID_DSRCS = l.IdDossier
                            AND ((ISNULL(pc.PRCLL_CODE_CLOTURE, '') <> '' AND ISNULL(cl.R_CODECLOTUR_SURVEILLANCE_RCS, 0) = 0)
                              OR (ISNULL(pc.PRCLL_CODE_SOLUTION, '') <> '' AND ISNULL(so.R_CODESOLUTI_SURVEILLANCE_RCS, 0) = 0)
                              OR (ISNULL(pc.PRCLL_CODE_CLOTURE, '') = '' AND ISNULL(pc.PRCLL_CODE_SOLUTION, '') = '')))
        ORDER BY l.Surveillance, l.NumeroGestion
        """;

    public async Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var relanceSeule = await RelanceSeule.LireOuiNonAsync(baseGreffe, RegleOuiNon.EgalUn, ct);
        return await LectureEcran.LireAsync(baseGreffe, Requete(relanceSeule), Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = aujourdhui });
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var surveillance = FormeLigne.Texte(r, "Surveillance");
        var etatRof = r["EtatRof"] is DBNull ? null : FormeLigne.Texte(r, "EtatRof");
        var (etat, faites) = Etat(etatRof);
        var relance = FormeLigne.DateDe(r, "DateRelance");
        var limite = FormeLigne.DateDe(r, "DateLimite");
        var avecRelance = r["AvecRelance"] is true;

        var motif = surveillance == "S123-124" ? $"Terme survenu le {FormeLigne.Date(limite)}"
                  : avecRelance ? $"Relance du {FormeLigne.Date(relance)} expirée le {FormeLigne.Date(limite)}"
                  : $"Dissolution déclarée le {FormeLigne.Date(relance)} · sans relance en cours";
        // _informationsDossier (l. 1538) : « Le dossier a ete mis a jour apres la derniere relance ».
        var misAJour = relance is { } rl && FormeLigne.DateDe(r, "DateMaj") is { } maj && maj.Date > rl ? "mis à jour après la dernière relance" : null;
        var radiation = etat switch
        {
            "EnCours" => $"radiation commencée le {FormeLigne.Date(FormeLigne.DateDe(r, "DateRof"))}",
            "Validee" => $"radiation du {FormeLigne.Date(FormeLigne.DateDe(r, "DateRof"))}",
            _ => null,
        };
        var nom = FormeLigne.Joindre(FormeLigne.Texte(r, "Denomination"), FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion")));
        // L'id : le dossier (DSSRC_ID_DSRCS), que « Radier d'office » vise (EcrivainRadiationsOffice.cs, lot 16).
        return new LigneLue(nom, FormeLigne.Joindre(motif, misAJour, radiation), Groupes[surveillance], etat, faites)
        {
            Id = Convert.ToInt32(r["IdDossier"], System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>La regle d'etat (en tete du fichier) : l'etat de la derniere demande ROF, ou null sans demande.</summary>
    internal static (string Etat, int[] Faites) Etat(string? etatRof) => etatRof switch
    {
        null => ("AFaire", []),
        "V" or "T" => ("Validee", [0, 1, 2, 3]),
        _ => ("EnCours", [0, 1]),
    };
}
