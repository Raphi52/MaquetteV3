using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Relances beneficiaires » (PROC_DBE_RELANCE) : les entreprises a relancer, ou relancees, pour n'avoir pas declare leurs
/// beneficiaires effectifs. Mode d'emploi des lecteurs : LecteurEcran.cs ; le geste de relance : EcrivainRelancesBeneficiaires.cs.
///
/// SOURCE (ancien RIG). La fiche proc_dbe_relance.md (Brain, reference/proc) nomme la classe : PROC_RDBE
/// (D:\RigApplication\Source\RIG\DLL\Processus\SURVEILLANCES\PROC_SRBE\PROC_SRBE\PROC_DBE_RELANCE\PROC_RDBE.cs), qui ne compile
/// plus (PROC_SRBE, Source\CompilationFull\PowerShell\buildErrors.txt:110). La relance qui TOURNE est le moteur generique
/// PROC_RSURV (SURVEILLANCES\PROC_RSURV\PROC_RSURV.cs), nature SDBE : relancer un dossier y cree une action RCS, UNE diligence
/// de nature « SDBE » (etat diligence « 1 », etat relance « 2 ») et sa relance n° 1 (PROC_RSURV.cs:159-166). Une ligne de
/// l'ecran = un dossier a relancer, ou une diligence SDBE, son dossier par l'action (DLGRC_ID_ACTRC -> rec.ActionRegistre ->
/// rec.DossierRcs, Serveur/Recopie/FichesV3.cs).
///
/// FILTRES DES DILIGENCES. Nature SDBE. Une diligence passee a l'injonction (relance « IDBE » portant une instance :
/// DossierRCS.cs:1821 CreerRelanceInjonctionDBE), ou dont la relance est restee sans effet (delai expire, la condition
/// LecteurInjonctionsBeneficiaires.RelanceSansEffet), est a l'ecran « Injonctions beneficiaires » : la marche « Suivi »
/// du descripteur le dit (« Si la relance reste sans effet, l'injonction DBE prend la suite »), et la V2 n'a qu'une
/// relance SDBE (NATURE_DILIGENCE_PARAM SDBE / RSURV : seule la requete n° 1 existe, RIG_DEV).
///
/// LES DOSSIERS A RELANCER (lot 8, plan « RIG v3 100 % fonctionnel », 26/11/2026). La relance 1 de PROC_RSURV cherche les
/// dossiers par la requete de reglage NATURE_DILIGENCE_PARAM SDBE / RSURV / 1 (SqlSelect de RIG_DEV, lu le 26/11/2026),
/// enveloppee par le moteur (NatureDiligence._GetResultatRechercheRig, NatureDiligence.parametre.cs:615-934). Une ligne
/// « A relancer » = un tel dossier, Id « dossier:<id> ». La V3 n'execute pas le texte SQL d'un reglage (aucun SQL libre) : la
/// requete de RIG_DEV est traduite ici, table par table (DossierARelancerSql). Ses conditions :
///   - le SqlSelect : dossier non radie ; une inscription PM (v2.INSCRIT_PM) non cotee en bourse (INSPM_SOCI_COTEE 0), de forme
///     juridique a traiter (FORME_JURIDIQUE_UTI.R_FORMEJURID_TYPE_BEN <> 0) ; pas de siege (ETABLISSEMENT SPM) dans un pays de
///     l'Union europeenne hors France (PAYS.R_PAYS_CEE) ; pas de demande SRBE, SRBE_RAPPEL ou SRBE_OLD validee ; pas de demande
///     DAS, DASBEN, %[MR][BCD]1% qualifiee, en attente, interrompue ou en reclamation (Q, W, I, N) ; pas de depot de categorie
///     « N » ni d'acte « BEN » ; pas d'instance d'injonction OPINJRBE dont le dossier est partie ; pas de formalite d'un
///     evenement BEN (CODE_EVENEMENT_RCS.R_CODEEVENEM_ISBENEF) ;
///   - l'enveloppe du moteur, avec les criteres par defaut de la nature (CriteresRechercheSurveillance.
///     GetCriteresRechercheSurveillanceByNatDil, CriteresRechercheSurveillance.cs:513-628, reglages SDBE de RIG_DEV) : types de
///     dossier B1, C1, D1 (TypeDossier « B,C,D », :50-58) ; AfficherOpeEntreprise « 1 » : pas EIRL, pas de cessation, pas de
///     dissolution, pas provisoirement sans activite (ProvSansActivite 0), societe non expiree (SocieteExpiree 0)
///     (parametre.cs:655-678 ; le GETDATE() de la V2 lu au jour de l'ecran) ; aucune diligence SDBE en cours deja relancee
///     (testExistenceDiligenceRcsActive, vrai pour RSURV : OPE_RESULTATS.cs:60 ; parametre.cs:784-807) ; pas de procedure
///     collective, ou seulement des solutions et clotures « surveillance RCS » (InclureDossierAvecProcedure absent,
///     parametre.cs:819-837) ; le dossier n'est pas exclu de la nature (DILIGENCE_RCS_EXCLUE, parametre.cs:917-931).
///   Ordre de la V2 : numero de dossier croissant (parametre.cs:938).
/// NON REPRIS de la recherche (nomme) : ses criteres saisis a l'ecran (numeros de gestion, dates d'immatriculation, cases
/// d'entreprise) -- la liste rend la recherche par defaut ; le plafond NbDossiersMax (1000) -- celui de l'ecran s'applique.
///
/// GROUPE. « DBE non deposee » : la relance de la V2 vise une entreprise sans depot ni formalite de beneficiaires
/// (la requete RSURV / 1 ci-dessus). Rien dans l'ancien RIG ne distingue une declaration INCOMPLETE (motif de diligence
/// toujours « NP » non pertinent, MOTIF_DILIGENCE de RIG_DEV) : le descripteur n'a que ce groupe (lot 8, 26/11/2026).
///
/// REGLE D'ETAT :
///   dossier a relancer (ci-dessus)                   -> AFaire  « A relancer » : aucune marche ;
///   diligence sans relance envoyee (ni RELANCE_RCS, ni etat relance « ... faite » : FormeRelance.Envoyee), dont les etats
///   relance 1, 3, 5 (« A faire ... ») : RIG v2 ne les relance pas (une diligence SDBE en cours d'etat relance > 0 ecarte son
///   dossier de la recherche, parametre.cs:798-806, et la V2 n'a pas de relance 2 SDBE)
///                                                    -> EnCours « En cours » : entreprise et declaration (0, 1) ;
///   relance envoyee                                  -> Validee « Relancee » : entreprise, declaration, courrier
///                                                       (0, 1, 2), plus le suivi (3) si la diligence est close
///                                                       (8 ou 9 : DiligenceRcs.cs:506-508 la clot regularisee).
/// Ordre : a relancer (dossier croissant), en cours, relancees en cours, puis closes ; la relance la plus recente en tete.
/// Id : « dossier:<id du dossier> » pour un dossier a relancer, l'id de la diligence sinon.
///
/// RECHERCHE (IRechercheEcran) : le critere « Numéro de gestion ou SIREN » du descripteur, celui de la recherche de RIG v2
/// (numero de gestion ou liste de SIREN, NatureDiligence.parametre.cs:643-651), et le champ de recherche (denomination, SIREN
/// ou numero de gestion, blancs retires) : sur les dossiers a relancer comme sur les diligences.
/// </summary>
internal sealed class LecteurRelancesBeneficiaires : IRechercheEcran
{
    public string Procedure => "PROC_DBE_RELANCE";

    /// <summary>Le groupe du descripteur, au mot pres (le seul : voir l'en-tete).</summary>
    internal const string NonDeposee = "DBE non déposée";

    /// <summary>Le prefixe de l'Id d'un dossier a relancer (l'Id d'une diligence est son numero seul).</summary>
    internal const string PrefixeDossier = "dossier:";

    /// <summary>Le critere du descripteur que la base cherche, au mot pres.</summary>
    internal const string CritereDossier = "Numéro de gestion ou SIREN";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDossier];

    /// <summary>
    /// Le dossier s (rec.DossierRcs) est a relancer pour ses beneficiaires (relance 1) : la requete SDBE / RSURV / 1 de RIG_DEV
    /// et l'enveloppe du moteur, traduites (en tete du fichier). Parametre : @aujourdhui. L'ecrivain la relit dans la
    /// transaction du geste.
    /// </summary>
    internal const string DossierARelancerSql = """
        s.DateRadiation IS NULL AND s.TypeRcs IN ('B1', 'C1', 'D1') AND s.Eirl = 0 AND s.DateCessation IS NULL AND s.ProvisoirementSans = 0
        AND EXISTS (SELECT 1 FROM v2.INSCRIT_PM AS pm
                    JOIN v2.FORME_JURIDIQUE_UTI AS fj ON fj.R_FORMEJURID_VALEUR = pm.INSPM_FORME_JURIDIQUE_UTI
                    WHERE pm.INSPM_ID_DSRCS = s.Id AND pm.INSPM_SOCI_COTEE = 0 AND fj.R_FORMEJURID_TYPE_BEN <> 0
                      AND pm.INSPM_DATE_DISSOLUTION IS NULL AND (pm.INSPM_EXPIRATION_PM > @aujourdhui OR pm.INSPM_EXPIRATION_PM IS NULL))
        AND NOT EXISTS (SELECT 1 FROM v2.ETABLISSEMENT AS et
                        JOIN rec.Adresse AS ad ON ad.Id = et.ETBLS_ID_ADRBS AND ad.Pays <> 'FRA'
                        JOIN v2.PAYS AS py ON py.R_PAYS_VALEUR = ad.Pays AND py.R_PAYS_CEE = 1
                        WHERE et.ETBLS_ID_DSRCS = s.Id AND et.ETBLS_TYPE_ADRESSE = 'SPM')
        AND NOT EXISTS (SELECT 1 FROM v2.DEMANDE AS dr WHERE dr.DMND_ID_DSRCS = s.Id
                        AND dr.DMND_CODE_PROSS IN ('SRBE', 'SRBE_RAPPEL', 'SRBE_OLD') AND dr.DMND_ETAT_DEMANDE = 'V')
        AND NOT EXISTS (SELECT 1 FROM v2.DEMANDE AS dq WHERE dq.DMND_ID_DSRCS = s.Id
                        AND (dq.DMND_CODE_PROSS IN ('DAS', 'DASBEN') OR dq.DMND_CODE_PROSS LIKE '%[MR][BCD]1%')
                        AND dq.DMND_ETAT_DEMANDE IN ('Q', 'W', 'I', 'N'))
        AND NOT EXISTS (SELECT 1 FROM rec.ActionRegistre AS ab
                        JOIN v2.DEPOT_RCS AS dp ON dp.DPTRC_ID_DPTRC = ab.IdDepot
                        JOIN v2.ACTE_DEPOSE AS ac ON ac.ACTDP_ID_DPTRC = dp.DPTRC_ID_DPTRC
                        WHERE ab.IdDossier = s.Id AND (dp.DPTRC_CATEGORIE_DEPOT = 'N' OR ac.ACTDP_ABREGE_PIECE = 'BEN'))
        AND NOT EXISTS (SELECT 1 FROM v2.PARTIE AS pr JOIN v2.INSTANCE AS ij ON ij.INSTN_ID_INSTN = pr.PRT_ID_INSTN
                        WHERE pr.PRT_ID_DSRCS = s.Id AND ij.INSTN_NATURE_AFFAIRE = 'OPINJRBE')
        AND NOT EXISTS (SELECT 1 FROM v2.INSCRIPTION_RCS AS ir
                        JOIN rec.ActionRegistre AS ai ON ai.IdInscriptionRegistre = ir.INSRC_ID_INSRC
                        JOIN v2.EVENEMENT_RCS AS ev ON ev.EVNRC_ID_ACTRC = ai.Id
                        JOIN v2.CODE_EVENEMENT_RCS AS ce ON ce.R_CODEEVENEM_VALEUR = ev.EVNRC_CODE_EVENEMENT_RCS AND ce.R_CODEEVENEM_ISBENEF = 1
                        WHERE ai.IdDossier = s.Id)
        AND NOT EXISTS (SELECT 1 FROM rec.ActionRegistre AS aa JOIN v2.DILIGENCE_RCS AS dd ON dd.DLGRC_ID_ACTRC = aa.Id
                        WHERE aa.IdDossier = s.Id AND dd.DLGRC_NATURE_DILIGENCE = 'SDBE' AND dd.DLGRC_ETAT_DILIGENCE = '1'
                          AND TRY_CAST(dd.DLGRC_ETAT_RELANCE AS int) > 0)
        AND ((NOT EXISTS (SELECT 1 FROM v2.PROCEDURE_COLLECTIVE AS pc WHERE pc.PRCLL_ID_DSRCS = s.Id)
              AND ISNULL(LTRIM(RTRIM(s.ProcedureCollective)), '') = ''
              AND NOT EXISTS (SELECT 1 FROM v2.MENTION_OBSERVATION AS mo
                              JOIN v2.CODE_MENTION AS cm ON cm.R_CODEMENTIO_VALEUR = mo.MNTOB_CODE_MENTION
                              WHERE mo.MNTOB_ID_DSRCS = s.Id AND cm.R_CODEMENTIO_TYPE_MENTION = 'P'))
             OR (EXISTS (SELECT 1 FROM v2.PROCEDURE_COLLECTIVE AS pc WHERE pc.PRCLL_ID_DSRCS = s.Id)
                 AND NOT EXISTS (SELECT 1 FROM v2.PROCEDURE_COLLECTIVE AS pc
                                 JOIN v2.CODE_SOLUTION AS cs ON cs.R_CODESOLUTI_VALEUR = pc.PRCLL_CODE_SOLUTION
                                      AND cs.R_CODESOLUTI_SURVEILLANCE_RCS = 0
                                 WHERE pc.PRCLL_ID_DSRCS = s.Id)
                 AND NOT EXISTS (SELECT 1 FROM v2.PROCEDURE_COLLECTIVE AS pc
                                 JOIN v2.CODE_CLOTURE AS cc ON cc.R_CODECLOTUR_VALEUR = pc.PRCLL_CODE_CLOTURE
                                      AND cc.R_CODECLOTUR_SURVEILLANCE_RCS = 0
                                 WHERE pc.PRCLL_ID_DSRCS = s.Id)))
        AND NOT EXISTS (SELECT 1 FROM v2.DILIGENCE_RCS_EXCLUE AS dx WHERE dx.DLGEX_ID_DSRCS = s.Id AND dx.DLGEX_NATURE_DILIGENCE = 'SDBE')
        """;

    // Les dossiers a relancer (Rang 0), puis les diligences SDBE : sans relance envoyee (1), relancees en cours (2), closes (3).
    private const string Requete = $"""
        SELECT TOP (@plafond) l.*, COUNT(*) OVER () AS Total
        FROM (
            SELECT 0 AS Rang, s.Id AS IdDossierARelancer, CAST(NULL AS int) AS DLGRC_ID_DLGRC,
                   CAST(NULL AS varchar(1)) AS DLGRC_ETAT_DILIGENCE, CAST(NULL AS varchar(1)) AS DLGRC_ETAT_RELANCE,
                   CAST(NULL AS datetime) AS DLGRC_DATE_REGULARISATION, s.NumeroGestion, s.Siren, e.Denomination,
                   CAST(NULL AS smallint) AS RLCRC_NUM_ORDRE, CAST(NULL AS varchar(16)) AS RLCRC_TYPE_RELANCE,
                   CAST(NULL AS datetime) AS RLCRC_DATE_RELANCE, CAST(NULL AS datetime) AS RLCRC_DATE_LIMITE
            FROM rec.DossierRcs AS s
            LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
            WHERE {DossierARelancerSql} /*recherche*/
            UNION ALL
            SELECT CASE WHEN NOT {FormeRelance.EnvoyeeSql} THEN 1 WHEN g.DLGRC_ETAT_DILIGENCE = '1' THEN 2 ELSE 3 END,
                   NULL, g.DLGRC_ID_DLGRC, g.DLGRC_ETAT_DILIGENCE, g.DLGRC_ETAT_RELANCE, g.DLGRC_DATE_REGULARISATION,
                   s.NumeroGestion, s.Siren, e.Denomination,
                   der.RLCRC_NUM_ORDRE, der.RLCRC_TYPE_RELANCE, der.RLCRC_DATE_RELANCE, der.RLCRC_DATE_LIMITE
            FROM v2.DILIGENCE_RCS AS g
            JOIN rec.ActionRegistre AS a ON a.Id = g.DLGRC_ID_ACTRC
            JOIN rec.DossierRcs AS s ON s.Id = a.IdDossier
            LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
            {FormeRelance.DerniereRelance}
            WHERE g.DLGRC_NATURE_DILIGENCE = 'SDBE'
              AND NOT EXISTS (SELECT 1 FROM v2.RELANCE_RCS AS i WHERE i.RLCRC_ID_DLGRC = g.DLGRC_ID_DLGRC
                              AND i.RLCRC_TYPE_RELANCE = 'IDBE' AND i.RLCRC_ID_INSTN IS NOT NULL)
              AND NOT ({LecteurInjonctionsBeneficiaires.RelanceSansEffet}) /*recherche*/
        ) AS l
        ORDER BY l.Rang, l.IdDossierARelancer, l.RLCRC_DATE_RELANCE DESC, l.DLGRC_ID_DLGRC DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", "", StringComparison.Ordinal), Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = aujourdhui.Date });

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var filtres = new List<string>();
        var parametres = new List<SqlParameter>
        {
            new("@plafond", plafond), new("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = aujourdhui.Date },
        };
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add("(e.Denomination LIKE @texte OR s.Siren LIKE @texteNumero OR s.NumeroGestion LIKE @texteNumero)");
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
            parametres.Add(new SqlParameter("@texteNumero", FormeLigne.Contient(recherche.Texte.Replace(" ", "", StringComparison.Ordinal))));
        }
        if (recherche.Critere(CritereDossier) is { } dossier)
        {
            filtres.Add("(s.NumeroGestion = @dossier OR s.Siren = @dossier)");
            parametres.Add(new SqlParameter("@dossier", dossier.Replace(" ", "", StringComparison.Ordinal)));
        }
        var sql = Requete.Replace("/*recherche*/", filtres.Count == 0 ? "" : " AND " + string.Join(" AND ", filtres), StringComparison.Ordinal);
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        if (r["IdDossierARelancer"] is int dossier)
            return new LigneLue(FormeRelance.NomDossier(r), "Relance 1 due", NonDeposee, "AFaire", [])
            { Id = PrefixeDossier + dossier.ToString(CultureInfo.InvariantCulture) };
        var etatDiligence = FormeLigne.Texte(r, "DLGRC_ETAT_DILIGENCE");
        var etatRelance = FormeLigne.Texte(r, "DLGRC_ETAT_RELANCE");
        var (etat, faites) = Etat(etatDiligence, envoyee: FormeRelance.Envoyee(r, etatRelance));
        var suite = etatDiligence switch
        {
            "9" => $"régularisée le {FormeLigne.Date(FormeLigne.DateDe(r, "DLGRC_DATE_REGULARISATION"))}",
            "8" => "close sans régularisation",
            _ => null,
        };
        // Un etat relance « A faire ... » (1, 3, 5) d'une diligence en cours garde son libelle de ETAT_RELANCE.
        var relance = FormeRelance.Relance(r, etatRelance, etatDiligence == "1" && etatRelance is "1" or "3" or "5" ? "AFaire" : etat);
        return new LigneLue(FormeRelance.NomDossier(r), FormeLigne.Joindre(relance, suite), NonDeposee, etat, faites)
        { Id = FormeLigne.Texte(r, "DLGRC_ID_DLGRC") };
    }

    /// <summary>La regle d'etat d'une DILIGENCE (en tete du fichier ; un dossier a relancer est AFaire) : etat et marches.</summary>
    internal static (string Etat, int[] Faites) Etat(string etatDiligence, bool envoyee)
    {
        if (!envoyee) return ("EnCours", [0, 1]);
        return ("Validee", etatDiligence is "8" or "9" ? [0, 1, 2, 3] : [0, 1, 2]);
    }
}
