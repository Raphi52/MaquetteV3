using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Envoi JAL / BODACC » (PROC_ENV_JAL, forme Operation) : les publicites judiciaires a envoyer au journal d'annonces legales, et
/// celles deja envoyees, comme la recherche de l'ancien RIG. Mode d'emploi des lecteurs : LecteurEcran.cs (plan « RIG v3 100 %
/// fonctionnel », lot 11, 27/11/2026).
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_ENV_JAL ; la recherche : Operations\OPE_RECH_ANNONCES.cs ;
/// les requetes : Outils\DLL\RigMetier\JUDICIAIRE\GenerationXML IPubliciteJudiciaire\LigneJal.cs (cite « LigneJal »). Fiche :
/// D:\RigApplication\Documentation\reference\proc\proc_env_jal.md, Detail OPE §2.
///
/// LE MODE : le menu ouvre PROC_ENV_JAL sans ModeAnnonce ; tout ModeAnnonce autre que « BODACC » est le mode JAL
/// (PROC_ENV_JAL.cs:52-69, proc_env_jal.md:157). Ce lecteur lit donc le mode JAL : la table PUBLICITE. Les BODACC judiciaires du
/// mode BODACC sont lus par « Publications BODACC » (LecteurPublicationsBodacc.cs).
///
/// LES LIGNES (GetJals, OPE_RECH_ANNONCES.cs:439-531 ; GetJalsEPSupprime, :409-437) :
///   - une publicite d'un evenement d'une procedure collective (LigneJal.SQLSelectFromJoinWhereToSendPc, :225-242) puis d'une
///     prevention (SQLSelectFromJoinWhereToSendPrv, :244-262) -- PROCEDURE_COLLECTIVE / PREVENTION, EVENEMENT_PROCEDURE, PUBLICITE ;
///   - « a envoyer » : PBLCT_DATE_ENVOI_ELECTRONIQUE IS NULL ; « deja envoyee » : IS NOT NULL (:451-453, :485-487 ; le filtre par
///     PBLCT_ETAT_PUBLICITE est en commentaire dans la V2, :447-449) ;
///   - et, avec les deja envoyees, les publicites dont l'evenement a ete SUPPRIME (PBLCT_ID_EVNPR NULL), retrouvees par la
///     publicite qui les suit (PBLCT_ID_PREC_JAL), procedures collectives seules (LigneJal.SQLJoinWhereEPSuppr, :203-210).
/// LES FILTRES (Rechercher, :331-372) :
///   - la PERIODE sur la date d'envoi prevue PBLCT_DATE_ENVOI, du debut a 0 h a la fin a 24 h (:455-459). A l'ouverture, la V2
///     pre-remplit le debut avec le reglage JAL_DATE_DEBUT_ENVOI (aujourd'hui s'il ne se lit pas) et la fin avec aujourd'hui
///     (RecuperationDesDates, :275-293) ; un critere de date vide vaut donc la date que la V2 pre-remplit. Sans le reglage, la V2
///     dit « Parametrage des dates manquant ! Veuillez contacter AMITEL. » et ferme l'ecran : aucune ligne. Une fin avant le debut
///     est refusee par la V2 (« La date de fin ne peut pas etre superieur a la date de debut », :146) : aucune ligne ;
///   - le JOURNAL destinataire PBLCT_DESTINATAIRE, egal (:461-463) ;
///   - le NUMERO de PC (PRCLL_NUM_PC) ou de prevention (PREVE_NUM_PRV), egal (:465-467, :504-506). La V2 choisit PC ou PRV par
///     des boutons radio (ultGRBModeRecherche) ; ici le numero se compare aux deux, un numero de PC n'etant pas celui d'une PRV ;
///   - le NUMERO DE BORDEREAU (le texte de recherche, ou le critere) remplace les dates : les publicites du bordereau, PC et PRV
///     (GetJalsToRemove, :533-560 ; « une recherche par bordereau est exclusive d'une recherche par dates », proc_env_jal.md:196).
/// L'ORDRE : a envoyer d'abord, dans l'ordre du reglage JAL_ORDER_BY (:512) ; puis les deja envoyees, par journal, procedure
///   exterieure, code et date d'evenement, numero (LigneJal.SQLOrderByAlreadySent, :219-223) ; puis les evenements supprimes, par
///   date d'envoi, code et date d'evenement (SQLOrderByAlreadySentEPSuppr, :212-217). Le reglage JAL_ORDER_BY est concatene tel
///   quel dans la requete V2 : ici, seuls ses noms de colonnes connus (Ordres) sont repris, un autre mot est ignore -- jamais de
///   texte de reglage dans le SQL. Le numero de publicite departage les ex aequo (la V2 n'en dit rien).
/// GROUPES : « Procedures collectives » (la partie PC, et les evenements supprimes) ; « Preventions » (la partie PRV).
/// REGLE D'ETAT :
///   - AFaire « A envoyer » : pas de date d'envoi electronique ;
///   - EnCours « Bordereau genere » : envoyee, sur un bordereau qui existe et n'est pas encore transmis (BRDRJ_ETAT_XML <> « 3 ») --
///     la V2 permet encore d'annuler ce bordereau (OPE_RECH_ANNONCES.cs:176-181) ;
///   - Validee « Envoyee » : envoyee, bordereau transmis par EDI (BRDRJ_ETAT_XML = « 3 », annulation interdite, :180-181), ou
///     sans bordereau a annuler.
/// PRECISIONS : l'evenement (son libelle, CODE_EVE_PRO), la date d'envoi prevue, le journal (DESTINATAIRE_INSTITUTIONNEL, sinon son
///   code), la date d'envoi electronique, le bordereau ; la procedure exterieure (PRCLL_EXTERIEUR, en rouge dans la V2) et
///   l'evenement supprime (en violet) le disent (JalRigDataGridView.cs:331-373, proc_env_jal.md §5).
/// NON REPRIS (nomme) : les lignes non cochables et leurs couleurs autres (annonce d'origine en cours de parution, debiteurs
///   multiples, texte JAL absent en EDI : JalRigDataGridView.cs:312-355), le nombre de documents generes (NB_DOCS_GENERES). Les
///   gestes : aucun ecrivain, voir le descripteur (Pont/EcransGeneres/ModifierEnvois.cs).
/// </summary>
internal sealed class LecteurEnvoiJal : IRechercheEcran
{
    public string Procedure => "PROC_ENV_JAL";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Pc = "Procédures collectives", Preventions = "Préventions";

    /// <summary>Les criteres du descripteur, au mot pres : les champs de la recherche V2 (OPE_RECH_ANNONCES.Designer.cs).</summary>
    internal const string CritereDebut = "Date de début", CritereFin = "Date de fin", CritereJournal = "Destinataire (JAL)",
                          CritereNumero = "Numéro de PC ou PRV", CritereBordereau = "Numéro de bordereau";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDebut, CritereFin, CritereJournal, CritereNumero, CritereBordereau];

    /// <summary>Les colonnes que JAL_ORDER_BY peut nommer (celles des requetes de LigneJal), et leur nom dans la requete ci-dessous.</summary>
    private static readonly Dictionary<string, string> Ordres = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EXTERIEUR"] = "EXTERIEUR", ["EVNPR_CODE_EVE_PRO"] = "EVNPR_CODE_EVE_PRO", ["EVNPR_DATE"] = "EVNPR_DATE", ["NUM"] = "NUM",
        ["PBLCT_DESTINATAIRE"] = "PBLCT_DESTINATAIRE", ["PBLCT_DATE_ENVOI"] = "PBLCT_DATE_ENVOI", ["PBLCT_ID_PBLCT"] = "PBLCT_ID_PBLCT",
    };

    // Partie 0 = a envoyer, 1 = deja envoyee, 2 = evenement supprime. {{Pc}}, {{Prv}}, {{Ep}} : les filtres de chaque partie ;
    // {{Ordre}} : l'ordre de la partie 0 (JAL_ORDER_BY).
    private const string Requete = """
        SELECT TOP (@plafond) a.*, b.BRDRJ_ID_BRDRJ, b.BRDRJ_ETAT_XML, d.DTINS_LIBELLE, c.R_CODEEVEPRO_LIBELLE_ECRAN, COUNT(*) OVER () AS Total
        FROM (SELECT CASE WHEN p.PBLCT_DATE_ENVOI_ELECTRONIQUE IS NULL THEN 0 ELSE 1 END AS Partie, 'PC' AS Sorte, p.PBLCT_ID_PBLCT,
                     pc.PRCLL_NUM_PC AS NUM, pc.PRCLL_NOM_AFFAIRE AS NomAffaire, ISNULL(pc.PRCLL_EXTERIEUR, 0) AS EXTERIEUR,
                     e.EVNPR_CODE_EVE_PRO, e.EVNPR_DATE, p.PBLCT_DESTINATAIRE, p.PBLCT_DATE_ENVOI, p.PBLCT_DATE_ENVOI_ELECTRONIQUE,
                     p.PBLCT_NUM_BORDEREAU
              FROM v2.PROCEDURE_COLLECTIVE AS pc
              JOIN v2.EVENEMENT_PROCEDURE AS e ON pc.PRCLL_ID_PRCLL = e.EVNPR_ID_PRCLL
              JOIN v2.PUBLICITE AS p ON e.EVNPR_ID_EVNPR = p.PBLCT_ID_EVNPR
              WHERE 1 = 1 {{Pc}}
              UNION ALL
              SELECT CASE WHEN p.PBLCT_DATE_ENVOI_ELECTRONIQUE IS NULL THEN 0 ELSE 1 END, 'PRV', p.PBLCT_ID_PBLCT,
                     pv.PREVE_NUM_PRV, pv.PREVE_NOM_AFFAIRE, 0, e.EVNPR_CODE_EVE_PRO, e.EVNPR_DATE, p.PBLCT_DESTINATAIRE, p.PBLCT_DATE_ENVOI,
                     p.PBLCT_DATE_ENVOI_ELECTRONIQUE, p.PBLCT_NUM_BORDEREAU
              FROM v2.PREVENTION AS pv
              JOIN v2.EVENEMENT_PROCEDURE AS e ON pv.PREVE_ID_PREVE = e.EVNPR_ID_PREVE
              JOIN v2.PUBLICITE AS p ON e.EVNPR_ID_EVNPR = p.PBLCT_ID_EVNPR
              WHERE 1 = 1 {{Prv}}
              UNION ALL
              SELECT 2, 'PC', p.PBLCT_ID_PBLCT, pc.PRCLL_NUM_PC, pc.PRCLL_NOM_AFFAIRE, ISNULL(pc.PRCLL_EXTERIEUR, 0), e.EVNPR_CODE_EVE_PRO,
                     e.EVNPR_DATE, p.PBLCT_DESTINATAIRE, p.PBLCT_DATE_ENVOI, p.PBLCT_DATE_ENVOI_ELECTRONIQUE, p.PBLCT_NUM_BORDEREAU
              FROM v2.PUBLICITE AS p
              JOIN v2.PUBLICITE AS lien ON p.PBLCT_ID_PBLCT = lien.PBLCT_ID_PREC_JAL
              JOIN v2.EVENEMENT_PROCEDURE AS e ON lien.PBLCT_ID_EVNPR = e.EVNPR_ID_EVNPR
              JOIN v2.PROCEDURE_COLLECTIVE AS pc ON e.EVNPR_ID_PRCLL = pc.PRCLL_ID_PRCLL
              WHERE p.PBLCT_ID_EVNPR IS NULL {{Ep}}) AS a
        OUTER APPLY (SELECT TOP (1) bj.BRDRJ_ID_BRDRJ, bj.BRDRJ_ETAT_XML FROM v2.BORDEREAU_JAL AS bj
                     WHERE bj.BRDRJ_NUM_BORDEREAU = a.PBLCT_NUM_BORDEREAU ORDER BY bj.BRDRJ_ID_BRDRJ) AS b
        LEFT JOIN v2.DESTINATAIRE_INSTITUTIONNEL AS d ON d.DTINS_CODE = a.PBLCT_DESTINATAIRE
        LEFT JOIN v2.CODE_EVE_PRO AS c ON c.R_CODEEVEPRO_VALEUR = a.EVNPR_CODE_EVE_PRO
        ORDER BY a.Partie{{Ordre}},
                 CASE WHEN a.Partie = 1 THEN a.PBLCT_DESTINATAIRE END, CASE WHEN a.Partie = 1 THEN a.EXTERIEUR END,
                 CASE WHEN a.Partie = 2 THEN a.PBLCT_DATE_ENVOI END,
                 CASE WHEN a.Partie > 0 THEN a.EVNPR_CODE_EVE_PRO END, CASE WHEN a.Partie > 0 THEN a.EVNPR_DATE END,
                 CASE WHEN a.Partie = 1 THEN a.NUM END, a.PBLCT_ID_PBLCT
        """;

    // Les filtres de periode, de journal et de numero, partie par partie (le numero de PC ne vaut pas pour une prevention).
    private const string Periode = " AND p.PBLCT_DATE_ENVOI >= @du AND p.PBLCT_DATE_ENVOI < DATEADD(DAY, 1, @au)";
    private const string Journal = " AND p.PBLCT_DESTINATAIRE = @journal";

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => ChercherAsync(baseGreffe, new RechercheEcran(null, new Dictionary<string, string>()), aujourdhui, plafond, ct);

    public async Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var reglages = await ReglagesAsync(baseGreffe, ct);
        var ordre = OrdreAEnvoyer(reglages.GetValueOrDefault("JAL_ORDER_BY"));
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var bordereau = recherche.Critere(CritereBordereau) ?? (string.IsNullOrWhiteSpace(recherche.Texte) ? null : recherche.Texte.Trim());
        string pc, prv, ep;
        if (bordereau is not null)
        {
            // Les publicites du bordereau, sans dates (GetJalsToRemove, :533-560) ; pas d'evenement supprime.
            pc = prv = " AND p.PBLCT_NUM_BORDEREAU = @bordereau";
            ep = " AND 1 = 0";
            parametres.Add(new SqlParameter("@bordereau", bordereau));
        }
        else
        {
            // Les dates que la V2 pre-remplit (RecuperationDesDates, :275-293) ; sans le reglage, l'ecran V2 se ferme.
            if (!reglages.TryGetValue("JAL_DATE_DEBUT_ENVOI", out var debutRegle)) return new LignesLues([], 0);
            var du = CriteresConsultation.Date(recherche.Critere(CritereDebut))
                     ?? (DateTime.TryParse(debutRegle, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out var d) ? d.Date : aujourdhui.Date);
            var au = CriteresConsultation.Date(recherche.Critere(CritereFin)) ?? aujourdhui.Date;
            if (au < du) return new LignesLues([], 0);
            pc = prv = ep = Periode;
            parametres.Add(new SqlParameter("@du", du));
            parametres.Add(new SqlParameter("@au", au));
        }
        if (recherche.Critere(CritereJournal) is { } journal)
        {
            pc += Journal; prv += Journal; ep += Journal;
            parametres.Add(new SqlParameter("@journal", journal));
        }
        if (recherche.Critere(CritereNumero) is { } numero)
        {
            pc += " AND pc.PRCLL_NUM_PC = @numero"; prv += " AND pv.PREVE_NUM_PRV = @numero"; ep += " AND pc.PRCLL_NUM_PC = @numero";
            parametres.Add(new SqlParameter("@numero", numero));
        }
        var sql = Requete.Replace("{{Pc}}", pc, StringComparison.Ordinal).Replace("{{Prv}}", prv, StringComparison.Ordinal)
                         .Replace("{{Ep}}", ep, StringComparison.Ordinal).Replace("{{Ordre}}", ordre, StringComparison.Ordinal);
        return await LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    /// <summary>Les reglages du greffe que cet ecran lit (PARAMETRAGE de RIG v2, repris dans soc.Reglage).</summary>
    private static async Task<Dictionary<string, string>> ReglagesAsync(SqlConnection baseGreffe, CancellationToken ct)
    {
        var reglages = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var r = await LectureV2.LireAsync(baseGreffe,
            "SELECT Code, Valeur FROM soc.Reglage WHERE Code IN ('JAL_DATE_DEBUT_ENVOI', 'JAL_ORDER_BY')", ct);
        while (await r.ReadAsync(ct))
            reglages[FormeLigne.Texte(r, "Code")] = FormeLigne.Texte(r, "Valeur");
        return reglages;
    }

    /// <summary>
    /// L'ordre de la partie « a envoyer » : les colonnes connues de JAL_ORDER_BY (« EXTERIEUR,EVNPR_CODE_EVE_PRO,EVNPR_DATE ASC,NUM »
    /// dans RIG_DEV), avec leur sens, chacune limitee a la partie 0. Un mot inconnu est ignore (Ordres, en tete de la classe).
    /// </summary>
    internal static string OrdreAEnvoyer(string? reglage)
    {
        var sb = new StringBuilder();
        foreach (var morceau in (reglage ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var mots = morceau.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var nom = mots[0].StartsWith("NewPub.", StringComparison.OrdinalIgnoreCase) ? mots[0]["NewPub.".Length..] : mots[0];
            if (!Ordres.TryGetValue(nom, out var colonne)) continue;
            var sens = mots.Length > 1 && mots[1].Equals("DESC", StringComparison.OrdinalIgnoreCase) ? " DESC" : "";
            sb.Append(CultureInfo.InvariantCulture, $", CASE WHEN a.Partie = 0 THEN a.{colonne} END{sens}");
        }
        return sb.ToString();
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var prevention = FormeLigne.Texte(r, "Sorte") == "PRV";
        var nom = FormeLigne.Joindre($"{(prevention ? "PRV" : "PC")} {FormeLigne.Texte(r, "NUM")}", FormeLigne.Texte(r, "NomAffaire"));
        var partie = Convert.ToInt32(r["Partie"], CultureInfo.InvariantCulture);
        var envoyee = FormeLigne.DateDe(r, "PBLCT_DATE_ENVOI_ELECTRONIQUE");
        var bordereau = FormeLigne.Texte(r, "PBLCT_NUM_BORDEREAU");
        var existe = r["BRDRJ_ID_BRDRJ"] is not DBNull;
        var transmis = FormeLigne.Texte(r, "BRDRJ_ETAT_XML") == "3";
        var etat = envoyee is null && partie != 2 ? "AFaire" : existe && !transmis ? "EnCours" : "Validee";

        var libelle = FormeLigne.Texte(r, "R_CODEEVEPRO_LIBELLE_ECRAN") is { Length: > 0 } lib ? lib : FormeLigne.Texte(r, "EVNPR_CODE_EVE_PRO");
        var evenement = FormeLigne.DateDe(r, "EVNPR_DATE") is { } de ? $"{libelle} du {FormeLigne.Date(de)}" : libelle;
        var journal = FormeLigne.Texte(r, "DTINS_LIBELLE") is { Length: > 0 } l ? l : FormeLigne.Texte(r, "PBLCT_DESTINATAIRE");
        var precisions = FormeLigne.Joindre(
            evenement,
            FormeLigne.DateDe(r, "PBLCT_DATE_ENVOI") is { } prevu ? $"envoi prévu le {FormeLigne.Date(prevu)}" : null,
            journal.Length > 0 ? $"à {journal}" : null,
            envoyee is { } e ? $"envoyée le {FormeLigne.Date(e)}" : null,
            bordereau.Length == 0 ? null : !existe ? $"bordereau {bordereau}" : transmis ? $"bordereau {bordereau} transmis" : $"bordereau {bordereau}, annulable",
            Convert.ToInt32(r["EXTERIEUR"], CultureInfo.InvariantCulture) != 0 ? "procédure extérieure" : null,
            partie == 2 ? "évènement de procédure supprimé" : null);
        return new LigneLue(nom, precisions, prevention ? Preventions : Pc, etat, []);
    }
}
