using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Ordonnances à signer » (PROC_ORDOSIGN) : les decisions judiciaires que le juge ou le greffe signe en lot. Mode d'emploi :
/// LecteurEcran.cs ; le geste : EcrivainOrdonnancesASigner.cs. Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_ORDOSIGN\ETP_ORDONNANCES\OPE_LISTE_ORDONNANCES.cs (proc_ordosign.md, Detail
/// OPE, qui fait foi). Chaque case de la recherche (ETP_RECHERCHE\OPE_RECHERCHE.Designer.cs, MetierTitre) charge une liste par sa
/// requete ; RIG v2 n'en montre qu'une a la fois (_UpdateUlt decoche les autres, :220-406), la V3 les montre toutes, une case = un
/// GROUPE du descripteur. Socle commun de chaque requete : DECISION_JUDICIAIRE JOIN INSTANCE, « DCJDC_DATE IS NULL » (decision non
/// rendue), triee DCJDC_DATE_PREVUE puis INSTN_NUM_ROLE :
///   « OP » (_ChargerOP, :408-447) : RP, sans PC ni prevention, enrolee depuis ORDOSIGN_DATE_DEBUT_OP, code affaire different de
///     ceux des injonctions DCA et RBE ;
///   « OP (liée à une PC) » (_ChargerOPPC, :579-618) : RP avec PC, depuis ORDOSIGN_DATE_DEBUT_OP, code affaire different des
///     injonctions DCA, RBE et du changement de juge ;
///   « OP (changement de juge) » (_ChargerOPCHJ, :620-659) : RP avec PC, code affaire = CHJCM_CODE_AFFAIRE ;
///   « Prévention » (_ChargerPRV, :661-695) : avec prevention, depuis ORDOSIGN_DATE_DEBUT_PRV, tout type ;
///   « IP » (_ChargerIP, :697-731) : IP sans PC ni prevention, depuis ORDOSIGN_DATE_DEBUT_IP ;
///   « JC » (_ChargerJC, :733-768) : JC sans prevention, depuis ORDOSIGN_DATE_DEBUT_JC ;
///   « Juge RCS » (_ChargerRC, :770-805) : RC sans PC, depuis ORDOSIGN_DATE_DEBUT_IP (sic, :782), sans relance RCS liee ;
///   « Injonction DCA » / « Injonction RBE » (_ChargerINJDCA / _ChargerINJRBE, :449-526) : RP sans PC ni prevention, code affaire =
///     celui de la nature de diligence SDCA / SDBE (NATURE_DILIGENCE_PARAM CritereMetier CodeAffaire, :137-151), depuis
///     ORDOSIGN_DATE_DEBUT_IDCA / _IDBE ;
///   « Injonctions juge commis » (_ChargerJugeCommis, :528-577) : sans PC ni prevention, code affaire = celui d'une nature de
///     diligence du groupe JugeCommis (NATURE_DILIGENCE.R_NATUREDILI_ID_NDILGRP), depuis ORDOSIGN_DATE_DEBUT_IDBE (sic, :553).
/// Une borne absente des reglages laisse sa case grisee (« ult_chb_ip.Enabled = false », :155-174) : sa liste est vide. Les reglages
/// (PARAMETRAGE de RIG v2) sont dans soc.Reglage. Une decision que plusieurs cases chargeraient est rangee dans la plus precise :
/// injonctions, changement de juge, juge commis, OP liee a une PC, OP, prevention, IP, JC, juge RCS.
///
/// REGLE D'ETAT :
///   - EnAttente « Demande en cours » : une demande de RIG v2 verrouille l'instance (VerrouDemandeV2) -- RIG v2 peint la ligne en
///     rouge et refuse de la cocher (_DemandeEnCours, :1597-1602) ; le motif est dans les precisions ;
///   - AFaire « À signer » : la decision n'est pas rendue (DCJDC_DATE IS NULL) ;
///   - Validee « Signée » : la decision a ete rendue AUJOURD'HUI (DCJDC_DATE = le jour) -- l'ecran de synthese de RIG v2 apres la
///     signature (ETP_RESUME\OPE_RESUME.cs:11-32 : role, affaire, type, date, numero de minute). RIG v2 ne relit une ordonnance
///     signee un autre jour que par son numero de role (« N° rôle d'une ordonnance à rééditer », _ChargerInstanceDejaSigne,
///     :807-885), que la forme File ne saisit pas.
/// Id = DCJDC_ID_DCJDC.
///
/// NON REPRIS (nomme) : la recherche « N° rôle d'une ordonnance à rééditer » (une saisie que la forme File n'a pas) ; la couleur
/// « numerisee / non numerisee » (VIDIGED_INDEX, OPE_RECHERCHE.Designer.cs:165-186) ; le parametrage des copies (FORM_ORDOCOPIE).
/// </summary>
internal sealed class LecteurOrdonnancesASigner : ILecteurEcran
{
    public string Procedure => "PROC_ORDOSIGN";

    /// <summary>Les groupes du descripteur (ModifierEnvois.cs), au mot pres : les cases de la recherche de RIG v2.</summary>
    internal const string Op = "OP", OpPc = "OP (liée à une PC)", OpChangementJuge = "OP (changement de juge)", Prevention = "Prévention",
                          Ip = "IP", Jc = "JC", JugeRcs = "Juge RCS", InjonctionDca = "Injonction DCA", InjonctionRbe = "Injonction RBE",
                          JugeCommis = "Injonctions juge commis";

    /// <summary>Les groupes dans l'ordre du descripteur (le rang de la requete).</summary>
    internal static readonly string[] Groupes = [Op, OpPc, OpChangementJuge, Prevention, Ip, Jc, JugeRcs, InjonctionDca, InjonctionRbe, JugeCommis];

    /// <summary>Les bornes et codes affaire de la recherche (OPE_LISTE_ORDONNANCES.DoChargerDonnees, :136-174).</summary>
    internal sealed record Reglages(DateTime? DebutOp, DateTime? DebutIp, DateTime? DebutJc, DateTime? DebutPrv, DateTime? DebutIdca,
                                    DateTime? DebutIdbe, string? AffaireDca, string? AffaireRbe, string? AffaireChangementJuge, string AffairesJugeCommis);

    /// <summary>Une decision de la liste.</summary>
    internal sealed record Ordonnance(int Id, int IdInstance, string Role, string Nom, string Groupe, DateTime? Date, DateTime? Prevue,
                                      string Minute, string TypeDecision, string LibelleType, string SerieMinute, DateTime? Saisine, string? Verrou);

    // Le rang de la case qui charge la decision (null : aucune), dans l'ordre de precision de l'en-tete ; le groupe s'en deduit.
    // « <> @code » laisse de cote un code affaire NULL, comme le SQL de RIG v2 (« INSTN_CODE_AFFAIRE <> 'X' »).
    private const string Liste = """
        SELECT TOP (@plafond) o.*, v.DMND_NUM_DEMANDE AS Verrou, COUNT(*) OVER () AS Total
        FROM (
            SELECT d.DCJDC_ID_DCJDC, d.DCJDC_DATE, d.DCJDC_DATE_PREVUE, d.DCJDC_NUM_REPERTOIRE, d.DCJDC_TYPE_DECISION,
                   td.R_TYPEDECISI_LIBELLE_ECRAN, td.R_TYPEDECISI_NUM_REPERTOIRE,
                   i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, i.INSTN_DATE_SAISINE_GREFFE,
                   CASE
                     WHEN i.INSTN_TYPE_INSTANCE = 'RP' AND i.INSTN_ID_PRCLL IS NULL AND i.INSTN_ID_PREVE IS NULL AND i.INSTN_DATE_ENROLEMENT >= @dIdca
                          AND i.INSTN_CODE_AFFAIRE = @cDca AND (@cRbe IS NULL OR i.INSTN_CODE_AFFAIRE <> @cRbe) THEN 7
                     WHEN i.INSTN_TYPE_INSTANCE = 'RP' AND i.INSTN_ID_PRCLL IS NULL AND i.INSTN_ID_PREVE IS NULL AND i.INSTN_DATE_ENROLEMENT >= @dIdbe
                          AND i.INSTN_CODE_AFFAIRE = @cRbe AND (@cDca IS NULL OR i.INSTN_CODE_AFFAIRE <> @cDca) THEN 8
                     WHEN i.INSTN_TYPE_INSTANCE = 'RP' AND i.INSTN_ID_PRCLL IS NOT NULL AND i.INSTN_DATE_ENROLEMENT >= @dOp
                          AND i.INSTN_CODE_AFFAIRE = @cChj AND (@cDca IS NULL OR i.INSTN_CODE_AFFAIRE <> @cDca)
                          AND (@cRbe IS NULL OR i.INSTN_CODE_AFFAIRE <> @cRbe) THEN 2
                     WHEN i.INSTN_ID_PRCLL IS NULL AND i.INSTN_ID_PREVE IS NULL AND i.INSTN_DATE_ENROLEMENT >= @dIdbe
                          AND CHARINDEX(',' + i.INSTN_CODE_AFFAIRE + ',', @cJugeCommis) > 0 THEN 9
                     WHEN i.INSTN_TYPE_INSTANCE = 'RP' AND i.INSTN_ID_PRCLL IS NOT NULL AND i.INSTN_DATE_ENROLEMENT >= @dOp
                          AND (@cDca IS NULL OR i.INSTN_CODE_AFFAIRE <> @cDca) AND (@cRbe IS NULL OR i.INSTN_CODE_AFFAIRE <> @cRbe)
                          AND (@cChj IS NULL OR i.INSTN_CODE_AFFAIRE <> @cChj) THEN 1
                     WHEN i.INSTN_TYPE_INSTANCE = 'RP' AND i.INSTN_ID_PRCLL IS NULL AND i.INSTN_ID_PREVE IS NULL AND i.INSTN_DATE_ENROLEMENT >= @dOp
                          AND (@cDca IS NULL OR i.INSTN_CODE_AFFAIRE <> @cDca) AND (@cRbe IS NULL OR i.INSTN_CODE_AFFAIRE <> @cRbe) THEN 0
                     WHEN i.INSTN_ID_PREVE IS NOT NULL AND i.INSTN_DATE_ENROLEMENT >= @dPrv THEN 3
                     WHEN i.INSTN_TYPE_INSTANCE = 'IP' AND i.INSTN_ID_PRCLL IS NULL AND i.INSTN_ID_PREVE IS NULL AND i.INSTN_DATE_ENROLEMENT >= @dIp THEN 4
                     WHEN i.INSTN_TYPE_INSTANCE = 'JC' AND i.INSTN_ID_PREVE IS NULL AND i.INSTN_DATE_ENROLEMENT >= @dJc THEN 5
                     WHEN i.INSTN_TYPE_INSTANCE = 'RC' AND i.INSTN_ID_PRCLL IS NULL AND i.INSTN_DATE_ENROLEMENT >= @dIp
                          AND NOT EXISTS (SELECT 1 FROM v2.RELANCE_RCS AS rr WHERE rr.RLCRC_ID_INSTN = i.INSTN_ID_INSTN) THEN 6
                   END AS Rang
            FROM v2.DECISION_JUDICIAIRE AS d
            JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
            LEFT JOIN v2.TYPE_DECISION AS td ON td.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
            WHERE (d.DCJDC_DATE IS NULL OR (d.DCJDC_DATE >= @jour AND d.DCJDC_DATE < @lendemain)) /*id*/
        ) AS o
        OUTER APPLY (SELECT TOP (1) dm.DMND_NUM_DEMANDE FROM v2.DEMANDE AS dm
                     JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = dm.DMND_ID_DMND
                     WHERE dd.DSSDM_CODE_SEQUENCEUR = '2J' AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(100), o.INSTN_ID_INSTN)
                       AND (dm.DMND_EN_COURS = 1 OR dm.DMND_ETAT_DEMANDE IN ('W', 'I', 'N'))
                     ORDER BY dm.DMND_ID_DMND) AS v
        WHERE o.Rang IS NOT NULL
        ORDER BY o.Rang, o.DCJDC_DATE_PREVUE, o.INSTN_NUM_ROLE, o.DCJDC_ID_DCJDC
        """;

    public async Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var reglages = await ReglagesAsync((sql, p) => LectureV2.LireAsync(baseGreffe, sql, ct, p), ct);
        var (ordonnances, total) = await OrdonnancesAsync((sql, p) => LectureV2.LireAsync(baseGreffe, sql, ct, p), reglages, aujourdhui, plafond, null, ct);
        var lignes = new List<LigneLue>();
        foreach (var o in ordonnances)
        {
            // Le motif du verrou, au message de RIG v2 (une instance verrouillee est rare : une lecture de plus, la meme que le geste).
            var motif = o.Verrou is null ? null
                : await VerrouDemandeV2.MotifAsync(baseGreffe, null, "Instance", VerrouDemandeV2.SequenceurInstance,
                                                   o.IdInstance.ToString(CultureInfo.InvariantCulture), ct);
            lignes.Add(Ligne(o, motif));
        }
        return new LignesLues(lignes, total);
    }

    private static LigneLue Ligne(Ordonnance o, string? motif)
    {
        var etat = o.Verrou is not null ? "EnAttente" : o.Date is null ? "AFaire" : "Validee";
        var precisions = FormeLigne.Joindre($"Rôle {o.Role}", o.LibelleType.Length > 0 ? o.LibelleType : o.TypeDecision,
            o.Date is { } d ? $"signée le {FormeLigne.Date(d)}" : o.Prevue is { } p ? $"prévue le {FormeLigne.Date(p)}" : "date prévue non renseignée",
            o.Minute.Length > 0 ? $"minute {o.Minute}" : null, motif);
        return new LigneLue(o.Nom.Length > 0 ? o.Nom : o.Role, precisions, o.Groupe, etat, [])
        {
            Id = o.Id.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Les bornes et codes affaire de la recherche, lus comme RIG v2 (en tete) : <paramref name="lire"/> execute un SELECT
    /// (celui du lecteur ou celui du geste, dans sa transaction).</summary>
    internal static async Task<Reglages> ReglagesAsync(Func<string, SqlParameter[], Task<SqlDataReader>> lire, CancellationToken ct)
    {
        var valeurs = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var r = await lire("SELECT Code, Valeur FROM soc.Reglage WHERE Code LIKE 'ORDOSIGN[_]DATE[_]DEBUT[_]%' OR Code = 'CHJCM_CODE_AFFAIRE'", []))
            while (await r.ReadAsync(ct)) valeurs[FormeLigne.Texte(r, "Code")] = FormeLigne.Texte(r, "Valeur");
        DateTime? Date(string code) => valeurs.TryGetValue(code, out var v)
            && DateTime.TryParse(v, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out var d) ? d.Date : null;
        string? Texte(string code) => valeurs.TryGetValue(code, out var v) && v.Length > 0 && !string.Equals(v, "NULL", StringComparison.Ordinal) ? v : null;

        // Les codes affaire des natures de diligence (NatureDiligence.GetParametreValeur(CodeAffaire, CritereMetier)) : SDCA, SDBE, et
        // celles du groupe JugeCommis. Une base sans ces tables (un banc) n'en a aucun.
        string? dca = null, rbe = null;
        var jugeCommis = new List<string>();
        bool existe;
        await using (var r = await lire("SELECT CONVERT(bit, CASE WHEN OBJECT_ID(N'v2.NATURE_DILIGENCE_PARAM') IS NOT NULL AND OBJECT_ID(N'v2.NATURE_DILIGENCE') IS NOT NULL AND OBJECT_ID(N'v2.NATURE_DILIGENCE_GROUPE') IS NOT NULL THEN 1 ELSE 0 END)", []))
            existe = await r.ReadAsync(ct) && r.GetBoolean(0);
        if (existe)
        {
            await using var r = await lire("""
                SELECT p.NADIP_NATURE_DILIGENCE, p.NADIP_VALEUR_PARAM,
                       CONVERT(bit, CASE WHEN g.NDILGRP_CODE = 'JugeCommis' THEN 1 ELSE 0 END) AS JugeCommis
                FROM v2.NATURE_DILIGENCE_PARAM AS p
                LEFT JOIN v2.NATURE_DILIGENCE AS n ON n.R_NATUREDILI_VALEUR = p.NADIP_NATURE_DILIGENCE
                LEFT JOIN v2.NATURE_DILIGENCE_GROUPE AS g ON g.NDILGRP_ID_NDILGRP = n.R_NATUREDILI_ID_NDILGRP
                WHERE p.NADIP_TYPE_PARAM = 'CritereMetier' AND p.NADIP_CODE_PARAM = 'CodeAffaire' AND p.NADIP_VALEUR_PARAM <> ''
                ORDER BY p.NADIP_ID_NADIP
                """, []);
            while (await r.ReadAsync(ct))
            {
                var nature = FormeLigne.Texte(r, "NADIP_NATURE_DILIGENCE");
                var code = FormeLigne.Texte(r, "NADIP_VALEUR_PARAM");
                if (nature == "SDCA") dca ??= code;
                if (nature == "SDBE") rbe ??= code;
                if (r["JugeCommis"] is true && !jugeCommis.Contains(code)) jugeCommis.Add(code);
            }
        }
        return new Reglages(Date("ORDOSIGN_DATE_DEBUT_OP"), Date("ORDOSIGN_DATE_DEBUT_IP"), Date("ORDOSIGN_DATE_DEBUT_JC"),
            Date("ORDOSIGN_DATE_DEBUT_PRV"), Date("ORDOSIGN_DATE_DEBUT_IDCA"), Date("ORDOSIGN_DATE_DEBUT_IDBE"), dca, rbe,
            Texte("CHJCM_CODE_AFFAIRE"), jugeCommis.Count == 0 ? "" : "," + string.Join(",", jugeCommis) + ",");
    }

    /// <summary>Les decisions de la liste (toutes, ou la seule <paramref name="id"/>), et leur nombre.</summary>
    internal static async Task<(List<Ordonnance> Lignes, int Total)> OrdonnancesAsync(Func<string, SqlParameter[], Task<SqlDataReader>> lire,
        Reglages reglages, DateTime aujourdhui, int plafond, int? id, CancellationToken ct)
    {
        static object Valeur(object? v) => v ?? DBNull.Value;
        var parametres = new List<SqlParameter>
        {
            new("@plafond", plafond), new("@jour", aujourdhui.Date), new("@lendemain", aujourdhui.Date.AddDays(1)),
            new("@dOp", Valeur(reglages.DebutOp)), new("@dIp", Valeur(reglages.DebutIp)), new("@dJc", Valeur(reglages.DebutJc)),
            new("@dPrv", Valeur(reglages.DebutPrv)), new("@dIdca", Valeur(reglages.DebutIdca)), new("@dIdbe", Valeur(reglages.DebutIdbe)),
            new("@cDca", Valeur(reglages.AffaireDca)), new("@cRbe", Valeur(reglages.AffaireRbe)),
            new("@cChj", Valeur(reglages.AffaireChangementJuge)), new("@cJugeCommis", reglages.AffairesJugeCommis),
        };
        // Les types des parametres nuls : datetime et varchar, pour que « >= @d » et « = @c » se comparent comme les colonnes.
        foreach (var p in parametres.Where(p => p.Value is DBNull))
            p.SqlDbType = p.ParameterName.StartsWith("@d", StringComparison.Ordinal) ? System.Data.SqlDbType.DateTime : System.Data.SqlDbType.VarChar;
        var sql = Liste;
        if (id is { } un)
        {
            sql = sql.Replace("/*id*/", "AND d.DCJDC_ID_DCJDC = @id", StringComparison.Ordinal);
            parametres.Add(new SqlParameter("@id", un));
        }
        var lignes = new List<Ordonnance>();
        var total = 0;
        await using var r = await lire(sql, [.. parametres]);
        while (await r.ReadAsync(ct))
        {
            total = Convert.ToInt32(r["Total"], CultureInfo.InvariantCulture);
            lignes.Add(new Ordonnance(Convert.ToInt32(r["DCJDC_ID_DCJDC"], CultureInfo.InvariantCulture),
                Convert.ToInt32(r["INSTN_ID_INSTN"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "INSTN_NUM_ROLE"),
                FormeLigne.Texte(r, "INSTN_NOM_COURANT"), Groupes[Convert.ToInt32(r["Rang"], CultureInfo.InvariantCulture)],
                FormeLigne.DateDe(r, "DCJDC_DATE"), FormeLigne.DateDe(r, "DCJDC_DATE_PREVUE"), FormeLigne.Texte(r, "DCJDC_NUM_REPERTOIRE"),
                FormeLigne.Texte(r, "DCJDC_TYPE_DECISION"), FormeLigne.Texte(r, "R_TYPEDECISI_LIBELLE_ECRAN"),
                FormeLigne.Texte(r, "R_TYPEDECISI_NUM_REPERTOIRE"), FormeLigne.DateDe(r, "INSTN_DATE_SAISINE_GREFFE"),
                r["Verrou"] is DBNull ? null : FormeLigne.Texte(r, "Verrou")));
        }
        return (lignes, total);
    }
}
