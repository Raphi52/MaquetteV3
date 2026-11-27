using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Signature électronique » (PROC_SIGNJUD) : les decisions a faire signer par le juge (envoi au parapheur Oodrive) puis a
/// contresigner par le greffier (CertSign). Mode d'emploi : LecteurEcran.cs ; les gestes : EcrivainSignatureElectronique.cs. Plan
/// « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_SIGNJUD\SignJud\MetierSignJud.cs (proc_signjud.md, Detail OPE, qui fait foi) --
/// _GetSqlDecisionsASigner (:415-613) : DECISION_JUDICIAIRE JOIN INSTANCE, LEFT JOIN DOCUMENT_DOSSIER (le document REDA,
/// DCMDS_VALEUR_ID_METIER = DCJDC_ID_DCJDC), JUGE et GREFFIER_ASSERMENTE de la REDA sinon de la decision (COALESCE, :460-479),
/// OODRIVE_DOCUMENT_SIGNE (OODDS_ID_DCJDC) ; la date de decision COALESCE(DCJDC_DATE, prononce, prevue, delibere) (:450), triee par
/// cette date puis le role. Les deux onglets, aux etats coches par defaut (:43-47, :59-64) :
///   - « Pour le juge » : les decisions signables (:501-509) -- sans document REDA, non rendues, d'un type SANS redaction ; ou avec un
///     document REDA a l'etat de redaction « révisé à rendre » (3) ; ou deja passees par Oodrive -- aux etats A envoyer (aucune ligne
///     Oodrive), A corriger, En erreur ; la V3 y ajoute « chez le juge » (EN_ATTENTE, EN_COURS_JUGE : l'etat « Envoyer » du juge) ;
///   - « Pour le greffier » : les decisions passees par Oodrive (OODDS_ETAT IS NOT NULL) a contresigner (SIGNE_JUGE,
///     EN_COURS_GREFFIER : l'etat « AEnvoyer » du greffier, :683-692).
/// Une decision n'est que dans un groupe : celui qui doit agir. La V3 garde les decisions contresignees AUJOURD'HUI (SIGNE_GREFFIER,
/// OODDS_DATE_MAJ_GREFFIER du jour) : le bilan de RIG v2 apres le traitement (_ResultatsTraiterDecision, OPE_RESULTAT_SIGNJUD.cs:
/// 202-257) ; RIG v2 relance ensuite sa recherche, ou elles n'entrent plus.
///
/// REGLE D'ETAT (etats Oodrive OODRIVE_DOCUMENT_SIGNE_ETAT, conversion :643-693 ; une ligne cochable si CanSignature, :345-357) :
///   - AFaire « À traiter » : aucune ligne Oodrive (juge : a envoyer) ; SIGNE_JUGE ou EN_COURS_GREFFIER (greffier : a contresigner) ;
///   - EnCours « À corriger » : A_CORRIGER (le juge la recoit de nouveau : CanSignature du juge admet ACorriger) ;
///   - EnAttente « Chez le juge » : EN_ATTENTE ou EN_COURS_JUGE (ni le juge ni le greffier ne la cochent) ;
///   - Validee « Signée » : SIGNE_GREFFIER ;
///   - Rejetee « En erreur » : tout autre etat (ERREUR_JUGE, ERREUR_GREFFIER, ERREUR_AUTRE ; « Erreur sur le traitement de la
///     signature », SignJudDecision.cs:508 : non cochable).
/// Id = DCJDC_ID_DCJDC.
///
/// NON REPRIS (nomme) : les criteres de la recherche (juge, greffier, audience, dates, types de role, etats) : la forme File n'en
/// saisit pas, la liste est celle des etats coches par defaut ; les compteurs GED (requete, document, annexes) et les couleurs.
/// </summary>
internal sealed class LecteurSignatureElectronique : ILecteurEcran
{
    public string Procedure => "PROC_SIGNJUD";

    /// <summary>Les groupes du descripteur (ModifierEnvois.cs), au mot pres : les onglets de RIG v2.</summary>
    internal const string PourLeJuge = "Pour le juge", PourLeGreffier = "Pour le greffier";

    /// <summary>Une decision de la liste, avec ce que les controles de RIG v2 lisent (SignJudDecision.UpdateErreursMetier).</summary>
    internal sealed record Decision(int Id, int IdInstance, string Role, string Nom, string TypeInstance, string LibelleType, DateTime? DateDecision,
                                    DateTime? Prevue, DateTime? Rendue, bool Reda, string EtatRedaction, string? EtatOodrive, string LibelleEtat,
                                    int? IdOodrive, string? GuidJuge, string Motif, int? IdJuge, string Juge, string AbregeJuge, string MailJuge,
                                    string TypesDematJuge, int? IdGreffier, string Greffier, string AbregeGreffier, int? UtilisateurGreffier,
                                    bool PieceNumerisee, int EditionsHorsReda, int EditionsReda);

    internal const string Requete = """
        SELECT TOP (@plafond) x.*, COUNT(*) OVER () AS Total
        FROM (
            SELECT d.DCJDC_ID_DCJDC, d.DCJDC_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, i.INSTN_TYPE_INSTANCE, td.R_TYPEDECISI_LIBELLE_ECRAN,
                   COALESCE(d.DCJDC_DATE, apr.AUDNC_DATE, d.DCJDC_DATE_PREVUE, ade.AUDNC_DATE) AS DATE_DECISION, d.DCJDC_DATE_PREVUE, d.DCJDC_DATE,
                   CONVERT(bit, CASE WHEN dd.DCMDS_VALEUR_ID_METIER IS NULL THEN 0 ELSE 1 END) AS Reda, d.DCJDC_ETAT_REDACTION,
                   o.OODDS_ETAT, oe.OODDS_LIBELLE, o.OODDS_ID_OODDS, CONVERT(varchar(36), o.OODDS_GUID_DOSSIER_OO_JUGE) AS GuidJuge, o.OODDS_MOTIF_REJET,
                   COALESCE(jr.JUG_ID_JUG, jd.JUG_ID_JUG) AS IdJuge,
                   LTRIM(RTRIM(COALESCE(jr.JUG_NOM, jd.JUG_NOM, '') + ' ' + COALESCE(jr.JUG_PRENOM, jd.JUG_PRENOM, ''))) AS Juge,
                   COALESCE(jr.JUG_ABREGE, jd.JUG_ABREGE, '') AS AbregeJuge, COALESCE(jr.JUG_ADRESSE_MAIL, jd.JUG_ADRESSE_MAIL, '') AS MailJuge,
                   COALESCE(jr.JUG_SIGNATURE_DEMAT_TYPE_INSTANCES, jd.JUG_SIGNATURE_DEMAT_TYPE_INSTANCES, '') AS TypesDematJuge,
                   COALESCE(gr.GRASS_ID_GRASS, gd.GRASS_ID_GRASS) AS IdGreffier,
                   LTRIM(RTRIM(COALESCE(gr.GRASS_NOM, gd.GRASS_NOM, '') + ' ' + COALESCE(gr.GRASS_PRENOM, gd.GRASS_PRENOM, ''))) AS Greffier,
                   COALESCE(gr.GRASS_ABREGE, gd.GRASS_ABREGE, '') AS AbregeGreffier, COALESCE(gr.GRASS_ID_UTLST, gd.GRASS_ID_UTLST) AS UtilisateurGreffier,
                   d.DCJDC_AVEC_PIECE_NUMERISE,
                   (SELECT COUNT(*) FROM v2.DEMANDE_EDITION AS e WHERE e.DMNED_VALEUR_ID_SOURCE = d.DCJDC_NUMERO_SOURCE AND e.DMNED_CODE_ENTITE_SOURCE = 'DECI_JUDICIAIRE'
                      AND e.DMNED_CODE_MAQUETTE NOT LIKE '%[_]CODE[_]BARRE[_]DJ') AS EditionsHorsReda,
                   (SELECT COUNT(*) FROM v2.DEMANDE_EDITION AS e WHERE e.DMNED_VALEUR_ID_SOURCE = CAST(d.DCJDC_ID_DCJDC AS varchar(40)) AND e.DMNED_TYPE_DOCUMENT = 'JGTM') AS EditionsReda,
                   CASE WHEN o.OODDS_ETAT IN ('SIGNE_JUGE', 'EN_COURS_GREFFIER', 'SIGNE_GREFFIER') THEN 1 ELSE 0 END AS Onglet
            FROM v2.DECISION_JUDICIAIRE AS d
            JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
            LEFT JOIN v2.TYPE_DECISION AS td ON td.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
            LEFT JOIN v2.APPEL_AFFAIRE AS ape ON ape.APPAF_ID_APPAF = d.DCJDC_ID_APPAF_DELIBERER
            LEFT JOIN v2.AUDIENCE_CABINET AS ade ON ade.AUDNC_ID_ADNC = ape.APPAF_ID_ADNC
            LEFT JOIN v2.APPEL_AFFAIRE AS app ON app.APPAF_ID_APPAF = d.DCJDC_ID_APPAF_PRONONCER
            LEFT JOIN v2.AUDIENCE_CABINET AS apr ON apr.AUDNC_ID_ADNC = app.APPAF_ID_ADNC
            OUTER APPLY (SELECT TOP (1) dm.DCMDS_VALEUR_ID_METIER, dm.DCMDS_ID_JUG, dm.DCMDS_ID_GRASS FROM v2.DOCUMENT_DOSSIER AS dm
                         WHERE dm.DCMDS_VALEUR_ID_METIER = d.DCJDC_ID_DCJDC ORDER BY dm.DCMDS_ID_DCMDS DESC) AS dd
            LEFT JOIN v2.JUGE AS jr ON jr.JUG_ID_JUG = dd.DCMDS_ID_JUG
            LEFT JOIN v2.JUGE AS jd ON jd.JUG_ID_JUG = d.DCJDC_ID_JUG
            LEFT JOIN v2.GREFFIER_ASSERMENTE AS gr ON gr.GRASS_ID_GRASS = dd.DCMDS_ID_GRASS
            LEFT JOIN v2.GREFFIER_ASSERMENTE AS gd ON gd.GRASS_ID_GRASS = d.DCJDC_ID_GRASS
            LEFT JOIN v2.OODRIVE_DOCUMENT_SIGNE AS o ON o.OODDS_ID_DCJDC = d.DCJDC_ID_DCJDC
            LEFT JOIN v2.OODRIVE_DOCUMENT_SIGNE_ETAT AS oe ON oe.OODDS_ETAT = o.OODDS_ETAT
            WHERE ((dd.DCMDS_VALEUR_ID_METIER IS NULL AND d.DCJDC_DATE IS NULL AND td.R_TYPEDECISI_DECISION_AVEC_REDACTION = 0)
                   OR (dd.DCMDS_VALEUR_ID_METIER IS NOT NULL AND d.DCJDC_ETAT_REDACTION = '3')
                   OR o.OODDS_ETAT IS NOT NULL)
              AND (o.OODDS_ETAT IS NULL OR o.OODDS_ETAT <> 'SIGNE_GREFFIER' OR (o.OODDS_DATE_MAJ_GREFFIER >= @jour AND o.OODDS_DATE_MAJ_GREFFIER < @lendemain))
              /*id*/
        ) AS x
        ORDER BY x.Onglet, x.DATE_DECISION, x.INSTN_NUM_ROLE, x.DCJDC_ID_DCJDC
        """;

    public async Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var (decisions, total) = await DecisionsAsync((sql, p) => RigV3.Serveur.Socle.Greffes.LectureV2.LireAsync(baseGreffe, sql, ct, p), aujourdhui, plafond, null, ct);
        return new LignesLues([.. decisions.Select(Ligne)], total);
    }

    /// <summary>L'etat de la ligne (en tete) et son groupe.</summary>
    internal static (string Groupe, string Etat) EtatDe(string? oodrive) => oodrive switch
    {
        null => (PourLeJuge, "AFaire"),
        "A_CORRIGER" => (PourLeJuge, "EnCours"),
        "EN_ATTENTE" or "EN_COURS_JUGE" => (PourLeJuge, "EnAttente"),
        "SIGNE_JUGE" or "EN_COURS_GREFFIER" => (PourLeGreffier, "AFaire"),
        "SIGNE_GREFFIER" => (PourLeGreffier, "Validee"),
        _ => (PourLeJuge, "Rejetee"),
    };

    private static LigneLue Ligne(Decision d)
    {
        var (groupe, etat) = EtatDe(d.EtatOodrive);
        var precisions = FormeLigne.Joindre($"Rôle {d.Role}", d.LibelleType, d.DateDecision is { } dt ? $"décision du {FormeLigne.Date(dt)}" : "sans date",
            d.Juge.Length > 0 ? $"juge {d.Juge}" : "pas de juge", d.Greffier.Length > 0 ? $"greffier {d.Greffier}" : "pas de greffier",
            d.EtatOodrive is null ? "à envoyer au juge" : d.LibelleEtat.Length > 0 ? d.LibelleEtat : d.EtatOodrive,
            d.Motif.Length > 0 ? $"motif : {d.Motif}" : null);
        return new LigneLue(d.Nom.Length > 0 ? d.Nom : d.Role, precisions, groupe, etat, []) { Id = d.Id.ToString(CultureInfo.InvariantCulture) };
    }

    /// <summary>Les decisions de la liste (toutes, ou la seule <paramref name="id"/>) et leur nombre ; <paramref name="lire"/> execute un
    /// SELECT (celui du lecteur, ou celui du geste dans sa transaction).</summary>
    internal static async Task<(List<Decision> Lignes, int Total)> DecisionsAsync(Func<string, SqlParameter[], Task<SqlDataReader>> lire, DateTime aujourdhui,
        int plafond, int? id, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond), new("@jour", aujourdhui.Date), new("@lendemain", aujourdhui.Date.AddDays(1)) };
        var sql = Requete;
        if (id is { } n)
        {
            sql = sql.Replace("/*id*/", "AND d.DCJDC_ID_DCJDC = @id", StringComparison.Ordinal);
            parametres.Add(new SqlParameter("@id", n));
        }
        static int? Entier(SqlDataReader r, string c) => r[c] is DBNull ? null : Convert.ToInt32(r[c], CultureInfo.InvariantCulture);
        var lignes = new List<Decision>();
        var total = 0;
        await using var r = await lire(sql, [.. parametres]);
        while (await r.ReadAsync(ct))
        {
            total = Convert.ToInt32(r["Total"], CultureInfo.InvariantCulture);
            lignes.Add(new Decision(Convert.ToInt32(r["DCJDC_ID_DCJDC"], CultureInfo.InvariantCulture), Convert.ToInt32(r["DCJDC_ID_INSTN"], CultureInfo.InvariantCulture),
                FormeLigne.Texte(r, "INSTN_NUM_ROLE"), FormeLigne.Texte(r, "INSTN_NOM_COURANT"), FormeLigne.Texte(r, "INSTN_TYPE_INSTANCE"),
                FormeLigne.Texte(r, "R_TYPEDECISI_LIBELLE_ECRAN"), FormeLigne.DateDe(r, "DATE_DECISION"), FormeLigne.DateDe(r, "DCJDC_DATE_PREVUE"),
                FormeLigne.DateDe(r, "DCJDC_DATE"), r["Reda"] is true, FormeLigne.Texte(r, "DCJDC_ETAT_REDACTION"),
                r["OODDS_ETAT"] is DBNull ? null : FormeLigne.Texte(r, "OODDS_ETAT"), FormeLigne.Texte(r, "OODDS_LIBELLE"), Entier(r, "OODDS_ID_OODDS"),
                r["GuidJuge"] is DBNull ? null : FormeLigne.Texte(r, "GuidJuge"), FormeLigne.Texte(r, "OODDS_MOTIF_REJET"),
                Entier(r, "IdJuge"), FormeLigne.Texte(r, "Juge"), FormeLigne.Texte(r, "AbregeJuge"), FormeLigne.Texte(r, "MailJuge"),
                FormeLigne.Texte(r, "TypesDematJuge"), Entier(r, "IdGreffier"), FormeLigne.Texte(r, "Greffier"), FormeLigne.Texte(r, "AbregeGreffier"),
                Entier(r, "UtilisateurGreffier"), r["DCJDC_AVEC_PIECE_NUMERISE"] is true,
                Convert.ToInt32(r["EditionsHorsReda"], CultureInfo.InvariantCulture), Convert.ToInt32(r["EditionsReda"], CultureInfo.InvariantCulture)));
        }
        return (lignes, total);
    }
}
