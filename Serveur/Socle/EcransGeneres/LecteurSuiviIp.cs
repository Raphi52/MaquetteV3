using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Suivi des preventions » (PROC_IPSUIVI) : le suivi d'UNE injonction de payer apres son ordonnance -- les dates des actes
/// qui la suivent (signification, opposition, accuse de reception, provision, certificat de non-opposition). Mode d'emploi des
/// lecteurs : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 8, 26/11/2026.
///
/// FORME : FICHE, pas File (decision du chef, « formes a revoir » : proc_ipsuivi.md:173). Le « Detail OPE » de la fiche fait
/// foi : IPSUIVI est « Injonction de Payer -- SUIVI », l'ecran de suivi post-decision d'UNE instance de type IP
/// (proc_ipsuivi.md:173) ; elle s'ouvre par son numero (parametre NumRole, PROC_IPSUIVI.cs:20-26, ou le champ « N° IP »,
/// ETP_IPSUIVI.cs:14-15). La partie haute de la fiche (dix etats NonTraite... Transfere, tables IP_SUIVI) ne correspond pas
/// au code (proc_ipsuivi.md:165-171) : elle n'est pas reprise. Le titre du menu reste celui que Raphael a valide.
///
/// LIGNES : les instances de type IP (InstanceIP_EnCours n'accepte qu'une IP, OPE_IPSUIVI.cs:135-143), la plus recente en
/// tete ; Id = INSTN_ID_INSTN, la cle de la fiche. Recherche : le « N° IP » (le numero de role), et le texte sur le numero ou
/// le nom courant. Dans la base V3, INSTANCE, EVENEMENT_PROCEDURE, DECISION_JUDICIAIRE et PARTIE sont les tables V2 (v2.*).
///
/// LA FICHE (OPE_IPSUIVI.cs:845-945, le chargement de l'IP) :
///   « Date IP » : la 1re decision judiciaire, sa date sinon sa date prevue (l. 849-858) ;
///   « Demandeur », « Defendeur » : la 1re partie de qualite D / F (_ChargementPartie ; PRT_NOM tronque a 30 dans le recap
///   V2, l. 532-541 ; ici la designation entiere) ;
///   « Signification » et « A personne » : le dernier evenement SURVENU ISIGNI/IPSIGNIF (EVNPR_DATE, EVNPR_SIGNIF_A_PERSONNE),
///   sinon le dernier EXPIRE (sa date maxi, fixe ou mini : « la date affichee est la date d'expiration », l. 885-898) ;
///   « Opposition » : survenu IOIP/IPOPOS, sinon expire (l. 900-912) ; « Reception LRAR » : survenu IAROIP/IPAROPOS
///   (l. 914-920) ; « Provision » : survenu ICONSOIP/IPCONSOPOS, sinon expire (l. 921-931) ; « Certificat de non-opposition » :
///   survenu IPCERTNOPP (l. 932-944 ; la V2 ne montre le champ que pour une IP « nouvelle loi » acceptee -- ici toujours, vide
///   sans evenement). Codes : EvenementProcedure.typeEvenementProToBD (EvenementProcedure.cs:585-604). La V2 avertit quand
///   deux evenements survenus du meme type existent (l. 881-882...) ; la V3 prend le dernier.
///
/// GROUPE : « Injonctions de payer » (la V2 n'en a pas). REGLE D'ETAT (les evenements survenus de l'IP, memes codes) :
///   un evenement qui clot l'IP -- fin (ICAUSEFIN, IDESIN, IPDESIN), executoire (IEXECU, IPEXECUT%), non avenue (INAORDO,
///   IPNAV, IPCNAV%), caducite (ICADUORDO, IPCAD, IPCCAD%) -> Validee « Close » ;
///   sinon une signification survenue                 -> EnCours « Signifiee » ;
///   sinon                                            -> AFaire  « A signifier ».
///
/// GESTES : « Valider » enregistre la date saisie -- desistement, signification (et « à personne »), certificat de
/// non-opposition, opposition, reception de la LRAR -- comme RIG v2 (EcrivainSuiviIp.cs, lot 8, 27/11/2026). La fiche ne rend
/// modifiables que les dates que RIG v2 laisse saisir sur CETTE IP (ZonesModifiables : les grisages de Remplir_IP,
/// OPE_IPSUIVI.cs:976-1125) ; l'etat de l'IP (EtatIp) est le meme pour la fiche et pour l'ecrivain. La PROVISION reste a saisir
/// dans RIG v2 (enrolement de l'opposition au fond et facture, en tete d'EcrivainSuiviIp.cs) : la V3 la refuse en le disant.
/// </summary>
internal sealed class LecteurSuiviIp : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_IPSUIVI";

    /// <summary>Le groupe, les sections et les champs du descripteur, au mot pres.</summary>
    internal const string Groupe = "Injonctions de payer";
    internal const string SectionIp = "Injonction de payer", SectionActes = "Actes après l'ordonnance";
    internal const string CritereNumero = "N° IP", ChampDesistement = "Désistement";
    internal const string ChampSignification = "Signification", ChampAPersonne = "À personne", ChampOpposition = "Opposition",
                          ChampAr = "Réception LRAR", ChampProvision = "Provision", ChampCertificat = "Certificat de non-opposition";

    /// <summary>Les champs de la section des actes, dans l'ordre de la fiche (le descripteur, Surveiller.cs).</summary>
    internal static readonly string[] ChampsSaisis =
        [ChampSignification, ChampAPersonne, ChampOpposition, ChampAr, ChampProvision, ChampCertificat, ChampDesistement];

    /// <summary>La date d'application de la reforme des IP (DecisionJudiciaireExtentions.DateReformeIP, :13).</summary>
    internal static readonly DateTime DateReformeIp = new(2022, 3, 1);

    /// <summary>
    /// L'etat d'une IP tel que Remplir_IP le charge (OPE_IPSUIVI.cs:845-1125) : la 1re decision (date, date prevue, acceptee), les
    /// alertes, et pour chaque acte la date affichee (le survenu, sinon la date d'expiration de l'expire), s'il est survenu et s'il
    /// est encore attendu (futur), avec sa date maxi. NouvelleLoi = _isDjAccepterEtIpNouvelleLoi (:865).
    /// </summary>
    internal sealed record EtatIp(string Role, string? NomCourant, int? Affaire, string? Note, bool Europeenne, bool Accepte, DateTime? DateDecision,
        DateTime? DateIp, bool Desiste, bool Fin, string? OppositionEnrolee, int NbFuturs,
        DateTime? Signification, DateTime? MaxiSignification, DateTime? Opposition, bool OppositionSurvenue, DateTime? MaxiOpposition,
        DateTime? ReceptionAr, string? CodeFuturAr, DateTime? Provision, bool ProvisionSurvenue, DateTime? Certificat, bool FuturCertificat,
        int NbFuturIsigni, int NbFuturIpsignif, int NbFuturIoip, int NbFuturIpopos, int NbFuturAr, int NbFuturConso, int NbFuturCertif)
    {
        internal bool NouvelleLoiDecision => DateDecision is { } d && d >= DateReformeIp;
        internal bool NouvelleLoi => Accepte && NouvelleLoiDecision;
    }

    private const string Futur = "ISNULL(e.EVNPR_SURVENU, 0) = 0 AND ISNULL(e.EVNPR_DELAI_EXPIRE, 0) = 0";

    private static string Nb(string etat, string codes) => $"""
        (SELECT COUNT(*) FROM v2.EVENEMENT_PROCEDURE AS e WHERE e.EVNPR_ID_INSTN = i.INSTN_ID_INSTN AND {etat} AND e.EVNPR_CODE_EVE_PRO IN ({codes}))
        """;

    /// <summary>La requete de l'etat d'UNE IP (@id), selon les colonnes que la base du greffe porte ; <paramref name="verrouiller"/> :
    /// la ligne de l'instance est verrouillee pour la transaction du geste.</summary>
    internal static string RequeteEtat(HashSet<string> colIns, bool verrouiller) => $"""
        SELECT i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT,
               {(colIns.Contains("INSTN_ID_AFFRE") ? "i.INSTN_ID_AFFRE" : "CAST(NULL AS int)")} AS Affaire,
               {(colIns.Contains("INSTN_NOTE_AUDIENCE") ? "i.INSTN_NOTE_AUDIENCE" : "CAST(NULL AS varchar(max))")} AS Note,
               CAST(ISNULL({(colIns.Contains("INSTN_IP_EUROPEENNE") ? "i.INSTN_IP_EUROPEENNE" : "0")}, 0) AS bit) AS Europeenne,
               dj.DCJDC_DATE, dj.DCJDC_DATE_PREVUE, CAST(ISNULL(td.R_TYPEDECISI_ACCEPTE, 0) AS bit) AS Accepte,
               CAST(CASE WHEN {Nb(Survenu, "'IPDESIN'")} > 0 THEN 1 ELSE 0 END AS bit) AS Desiste,
               CAST(CASE WHEN {Nb(Survenu, "'ICAUSEFIN','IDESIN','IPDESIN'")} > 0 THEN 1 ELSE 0 END AS bit) AS Fin,
               {(colIns.Contains("INSTN_ID_INSTN_INSTANCE_PRINCIPA") && colIns.Contains("INSTN_CODE_AFFAIRE")
                   ? "(SELECT TOP (1) o.INSTN_NUM_ROLE FROM v2.INSTANCE AS o WHERE o.INSTN_ID_INSTN_INSTANCE_PRINCIPA = i.INSTN_ID_INSTN AND o.INSTN_CODE_AFFAIRE = 'TIP1' ORDER BY o.INSTN_ID_INSTN DESC)"
                   : "CAST(NULL AS varchar(11))")} AS Tip1,
               (SELECT COUNT(*) FROM v2.EVENEMENT_PROCEDURE AS e WHERE e.EVNPR_ID_INSTN = i.INSTN_ID_INSTN AND {Futur}) AS NbFuturs,
               COALESCE({Dernier(Signif, "e.EVNPR_DATE", Survenu)}, {Dernier(Signif, DateExpiree, Expire)}) AS Signification,
               {Dernier(Signif, DateExpiree, Futur)} AS MaxiSignification,
               COALESCE({Dernier(Oppos, "e.EVNPR_DATE", Survenu)}, {Dernier(Oppos, DateExpiree, Expire)}) AS Opposition,
               CAST(CASE WHEN {Nb(Survenu, Oppos)} > 0 THEN 1 ELSE 0 END AS bit) AS OppositionSurvenue,
               {Dernier(Oppos, DateExpiree, Futur)} AS MaxiOpposition,
               {Dernier(Ar, "e.EVNPR_DATE", Survenu)} AS ReceptionAr,
               {Dernier(Ar, "e.EVNPR_CODE_EVE_PRO", Futur)} AS CodeFuturAr,
               COALESCE({Dernier(Conso, "e.EVNPR_DATE", Survenu)}, {Dernier(Conso, DateExpiree, Expire)}) AS Provision,
               CAST(CASE WHEN {Nb(Survenu, Conso)} > 0 THEN 1 ELSE 0 END AS bit) AS ProvisionSurvenue,
               {Dernier(Certif, "e.EVNPR_DATE", Survenu)} AS Certificat,
               {Nb(Futur, "'ISIGNI'")} AS NbIsigni, {Nb(Futur, "'IPSIGNIF'")} AS NbIpsignif, {Nb(Futur, "'IOIP'")} AS NbIoip,
               {Nb(Futur, "'IPOPOS'")} AS NbIpopos, {Nb(Futur, Ar)} AS NbAr, {Nb(Futur, Conso)} AS NbConso, {Nb(Futur, Certif)} AS NbCertif
        FROM v2.INSTANCE AS i{(verrouiller ? " WITH (UPDLOCK, HOLDLOCK)" : "")}
        OUTER APPLY (SELECT TOP (1) d.DCJDC_DATE, d.DCJDC_DATE_PREVUE, d.DCJDC_TYPE_DECISION FROM v2.DECISION_JUDICIAIRE AS d
                     WHERE d.DCJDC_ID_INSTN = i.INSTN_ID_INSTN ORDER BY d.DCJDC_NUM_ORDRE, d.DCJDC_ID_DCJDC) AS dj
        LEFT JOIN v2.TYPE_DECISION AS td ON td.R_TYPEDECISI_VALEUR = dj.DCJDC_TYPE_DECISION
        WHERE i.INSTN_ID_INSTN = @id AND i.INSTN_TYPE_INSTANCE = 'IP'
        """;

    /// <summary>L'etat lu par <see cref="RequeteEtat"/>.</summary>
    internal static EtatIp EtatDe(SqlDataReader r)
    {
        DateTime? D(string c) => FormeLigne.DateDe(r, c);
        int N(string c) => Convert.ToInt32(r[c], CultureInfo.InvariantCulture);
        var decision = D("DCJDC_DATE");
        return new EtatIp(FormeLigne.Texte(r, "INSTN_NUM_ROLE"), FormeLigne.Texte(r, "INSTN_NOM_COURANT") is { Length: > 0 } nc ? nc : null,
            r["Affaire"] is DBNull ? null : Convert.ToInt32(r["Affaire"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "Note") is { Length: > 0 } note ? note : null,
            r["Europeenne"] is true, r["Accepte"] is true, decision, decision ?? D("DCJDC_DATE_PREVUE"), r["Desiste"] is true, r["Fin"] is true,
            FormeLigne.Texte(r, "Tip1") is { Length: > 0 } t ? t : null, N("NbFuturs"),
            D("Signification"), D("MaxiSignification"), D("Opposition"), r["OppositionSurvenue"] is true, D("MaxiOpposition"),
            D("ReceptionAr"), FormeLigne.Texte(r, "CodeFuturAr").Trim() is { Length: > 0 } ca ? ca : null, D("Provision"), r["ProvisionSurvenue"] is true,
            D("Certificat"), N("NbCertif") > 0,
            N("NbIsigni"), N("NbIpsignif"), N("NbIoip"), N("NbIpopos"), N("NbAr"), N("NbConso"), N("NbCertif"));
    }

    /// <summary>
    /// Les dates que RIG v2 laisse saisir sur cette IP (les grisages de Remplir_IP, OPE_IPSUIVI.cs:976-1125) : aucune si l'IP est
    /// desistee, europeenne ou rejetee, si une opposition est enrolee, sans evenement attendu, close ou incoherente
    /// (_VerifierCoherenceIP) ; le desistement seul si la decision n'a pas de date ; sinon, selon ses evenements ATTENDUS
    /// (:1056-1112) -- signification attendue : la signification (si elle n'a pas de date) et l'opposition (si aucune n'est
    /// survenue) ; opposition attendue : l'opposition ; LRAR attendue : la LRAR (sans date), la provision (si aucune n'est survenue),
    /// la signification (sans date) ; provision attendue : la provision (sans date), la signification (sans date) ; et, nouvelle loi,
    /// la signification sans date (:1115-1118) et le certificat sans opposition ni certificat, ou attendu sans date (:930-933,
    /// :1105-1108). « À personne » va avec la signification ; le desistement, des qu'un evenement est attendu (:1053).
    /// </summary>
    internal static HashSet<string> ZonesModifiables(EtatIp e)
    {
        var zones = new HashSet<string>(StringComparer.Ordinal);
        if (e.Desiste || e.Europeenne || !e.Accepte) return zones;
        if (e.DateDecision is null) { zones.Add(ChampDesistement); return zones; }
        if (e.OppositionEnrolee is not null || e.NbFuturs == 0 || e.Fin) return zones;
        if (e.NbFuturIsigni + e.NbFuturIpsignif > 1 || e.NbFuturIoip + e.NbFuturIpopos > 1 || e.NbFuturAr > 1 || e.NbFuturConso > 1 || e.NbFuturCertif > 1
            || (e.NouvelleLoiDecision && (e.NbFuturIsigni > 0 || e.NbFuturIoip > 0)) || (!e.NouvelleLoiDecision && (e.NbFuturIpsignif > 0 || e.NbFuturIpopos > 0)))
            return zones;
        zones.Add(ChampDesistement);
        if (e.NbFuturIsigni + e.NbFuturIpsignif > 0)
        {
            if (e.Signification is null) zones.Add(ChampSignification);
            if (!e.OppositionSurvenue) zones.Add(ChampOpposition);
        }
        if (e.NbFuturIoip + e.NbFuturIpopos > 0) zones.Add(ChampOpposition);
        if (e.NbFuturAr > 0)
        {
            if (!e.ProvisionSurvenue) zones.Add(ChampProvision);
            if (e.ReceptionAr is null) zones.Add(ChampAr);
            if (e.Signification is null) zones.Add(ChampSignification);
        }
        if (e.NbFuturConso > 0)
        {
            if (e.Provision is null) zones.Add(ChampProvision);
            if (e.Signification is null) zones.Add(ChampSignification);
        }
        if (e.NouvelleLoi)
        {
            if (e.Signification is null) zones.Add(ChampSignification);
            if (e.FuturCertificat ? e.Certificat is null : e.Opposition is null && e.Certificat is null) zones.Add(ChampCertificat);
        }
        if (zones.Contains(ChampSignification)) zones.Add(ChampAPersonne);
        return zones;
    }

    public IReadOnlyCollection<string> Criteres { get; } = [CritereNumero];

    // Le dernier evenement survenu (ou expire) d'une liste de codes, pour l'instance i.
    private static string Dernier(string codes, string colonne, string etat) => $"""
        (SELECT TOP (1) {colonne} FROM v2.EVENEMENT_PROCEDURE AS e WHERE e.EVNPR_ID_INSTN = i.INSTN_ID_INSTN
         AND {etat} AND e.EVNPR_CODE_EVE_PRO IN ({codes}) ORDER BY e.EVNPR_NUM_ORDRE DESC, e.EVNPR_ID_EVNPR DESC)
        """;

    private const string Survenu = "e.EVNPR_SURVENU = 1", Expire = "e.EVNPR_DELAI_EXPIRE = 1";
    private const string DateExpiree = "COALESCE(e.EVNPR_DATE_MAXI, e.EVNPR_DATE_MINI, e.EVNPR_DATE_FIXE)";
    private const string Signif = "'ISIGNI','IPSIGNIF'", Oppos = "'IOIP','IPOPOS'", Ar = "'IAROIP','IPAROPOS'",
                         Conso = "'ICONSOIP','IPCONSOPOS'", Certif = "'IPCERTNOPP'";

    private static readonly string Colonnes = $"""
        i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT,
        (SELECT TOP (1) COALESCE(dj.DCJDC_DATE, dj.DCJDC_DATE_PREVUE) FROM v2.DECISION_JUDICIAIRE AS dj
         WHERE dj.DCJDC_ID_INSTN = i.INSTN_ID_INSTN ORDER BY dj.DCJDC_NUM_ORDRE, dj.DCJDC_ID_DCJDC) AS DateIp,
        (SELECT TOP (1) COALESCE(NULLIF(p.PRT_DESIGNATION, ''), p.PRT_NOM) FROM v2.PARTIE AS p
         WHERE p.PRT_ID_INSTN = i.INSTN_ID_INSTN AND p.PRT_QUALIF_PARTIE LIKE 'D%' ORDER BY p.PRT_NUM_ORDRE, p.PRT_ID_PRT) AS Demandeur,
        (SELECT TOP (1) COALESCE(NULLIF(p.PRT_DESIGNATION, ''), p.PRT_NOM) FROM v2.PARTIE AS p
         WHERE p.PRT_ID_INSTN = i.INSTN_ID_INSTN AND p.PRT_QUALIF_PARTIE LIKE 'F%' ORDER BY p.PRT_NUM_ORDRE, p.PRT_ID_PRT) AS Defendeur,
        COALESCE({Dernier(Signif, "e.EVNPR_DATE", Survenu)}, {Dernier(Signif, DateExpiree, Expire)}) AS Signification,
        COALESCE({Dernier(Signif, "e.EVNPR_SIGNIF_A_PERSONNE", Survenu)}, {Dernier(Signif, "e.EVNPR_SIGNIF_A_PERSONNE", Expire)}) AS APersonne,
        COALESCE({Dernier(Oppos, "e.EVNPR_DATE", Survenu)}, {Dernier(Oppos, DateExpiree, Expire)}) AS Opposition,
        {Dernier(Ar, "e.EVNPR_DATE", Survenu)} AS ReceptionLrar,
        COALESCE({Dernier(Conso, "e.EVNPR_DATE", Survenu)}, {Dernier(Conso, DateExpiree, Expire)}) AS Provision,
        {Dernier(Certif, "e.EVNPR_DATE", Survenu)} AS Certificat,
        {Dernier("'IPDESIN'", "e.EVNPR_DATE", Survenu)} AS Desistement,
        CASE WHEN EXISTS (SELECT 1 FROM v2.EVENEMENT_PROCEDURE AS e WHERE e.EVNPR_ID_INSTN = i.INSTN_ID_INSTN AND e.EVNPR_SURVENU = 1
                          AND (e.EVNPR_CODE_EVE_PRO IN ('ICAUSEFIN','IDESIN','IPDESIN','IEXECU','INAORDO','IPNAV','ICADUORDO','IPCAD')
                               OR e.EVNPR_CODE_EVE_PRO LIKE 'IPEXECUT%' OR e.EVNPR_CODE_EVE_PRO LIKE 'IPCNAV%'
                               OR e.EVNPR_CODE_EVE_PRO LIKE 'IPCCAD%')) THEN 'Validee'
             WHEN EXISTS (SELECT 1 FROM v2.EVENEMENT_PROCEDURE AS e WHERE e.EVNPR_ID_INSTN = i.INSTN_ID_INSTN AND e.EVNPR_SURVENU = 1
                          AND e.EVNPR_CODE_EVE_PRO IN ({Signif})) THEN 'EnCours'
             ELSE 'AFaire' END AS Etat
        """;

    private static readonly string Requete = $"""
        SELECT TOP (@plafond) {Colonnes}, COUNT(*) OVER () AS Total
        FROM v2.INSTANCE AS i
        WHERE i.INSTN_TYPE_INSTANCE = 'IP' /*recherche*/
        ORDER BY i.INSTN_ID_INSTN DESC
        """;

    private static readonly string UneIp = $"""
        SELECT {Colonnes} FROM v2.INSTANCE AS i WHERE i.INSTN_TYPE_INSTANCE = 'IP' AND i.INSTN_ID_INSTN = @id
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", "", StringComparison.Ordinal), Ligne, ct,
                                  new SqlParameter("@plafond", plafond));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var filtres = new List<string>();
        // Le texte : le numero de role ou le nom courant ; le « N° IP » : le numero de role (blancs retires, comme la saisie V2).
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add("(i.INSTN_NUM_ROLE LIKE @texte OR i.INSTN_NOM_COURANT LIKE @texte)");
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        if (recherche.Critere(CritereNumero) is { } numero)
        {
            filtres.Add("i.INSTN_NUM_ROLE LIKE @numero");
            parametres.Add(new SqlParameter("@numero", FormeLigne.Contient(numero.Replace(" ", "", StringComparison.Ordinal))));
        }
        var sql = Requete.Replace("/*recherche*/", filtres.Count == 0 ? "" : " AND " + string.Join(" AND ", filtres), StringComparison.Ordinal);
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        // fix-ok: extension mesuree (lot 8, 27/11/2026, objection du controle final : OPE_IPSUIVI.cs:1774-2140 ecrit ces dates) --
        // la fiche ne rendait modifiable que le desistement ; elle rend maintenant modifiable chaque date que Remplir_IP laisse
        // saisir, selon l'etat de l'IP lu ici (le meme que l'ecrivain relit), et non une regle par champ.
        async Task<HashSet<string>> Colonnes(string table)
        {
            var c = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var rc = await LectureV2.LireAsync(baseGreffe, "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@t)", ct, new SqlParameter("@t", table));
            while (await rc.ReadAsync(ct)) c.Add(rc.GetString(0));
            return c;
        }
        HashSet<string> zones = [];
        await using (var re = await LectureV2.LireAsync(baseGreffe, RequeteEtat(await Colonnes("v2.INSTANCE"), verrouiller: false), ct, new SqlParameter("@id", n)))
            if (await re.ReadAsync(ct)) zones = ZonesModifiables(EtatDe(re));
        await using var r = await LectureV2.LireAsync(baseGreffe, UneIp, ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        var ligne = Ligne(r);
        string D(string colonne) => FormeLigne.DateFiche(FormeLigne.DateDe(r, colonne));
        return new FicheEcran(Procedure, id, ligne.Nom,
        [
            new SectionFiche(SectionIp,
            [
                new ChampFiche(CritereNumero, FormeLigne.Texte(r, "INSTN_NUM_ROLE")), new ChampFiche("Date IP", D("DateIp")),
                new ChampFiche("Demandeur", FormeLigne.Texte(r, "Demandeur")), new ChampFiche("Défendeur", FormeLigne.Texte(r, "Defendeur")),
            ]),
            // Chaque date est modifiable si RIG v2 la laisse saisir sur cette IP (ZonesModifiables) ; l'ecrivain le reverifie.
            new SectionFiche(SectionActes,
            [
                new ChampFiche(ChampSignification, D("Signification")) { Modifiable = zones.Contains(ChampSignification) },
                new ChampFiche(ChampAPersonne, FormeLigne.Case(r["APersonne"] as bool?)) { Modifiable = zones.Contains(ChampAPersonne) },
                new ChampFiche(ChampOpposition, D("Opposition")) { Modifiable = zones.Contains(ChampOpposition) },
                new ChampFiche(ChampAr, D("ReceptionLrar")) { Modifiable = zones.Contains(ChampAr) },
                new ChampFiche(ChampProvision, D("Provision")) { Modifiable = zones.Contains(ChampProvision) },
                new ChampFiche(ChampCertificat, D("Certificat")) { Modifiable = zones.Contains(ChampCertificat) },
                // Le desistement (ult_Desistement, OPE_IPSUIVI.cs:978-981) : la date du dernier IPDESIN survenu.
                new ChampFiche(ChampDesistement, D("Desistement")) { Modifiable = zones.Contains(ChampDesistement) },
            ]),
        ]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        string demandeur = FormeLigne.Texte(r, "Demandeur"), defendeur = FormeLigne.Texte(r, "Defendeur");
        var parties = demandeur.Length > 0 && defendeur.Length > 0 ? $"{demandeur} c/ {defendeur}" : FormeLigne.Texte(r, "INSTN_NOM_COURANT");
        var nom = FormeLigne.Joindre($"IP {FormeLigne.Texte(r, "INSTN_NUM_ROLE")}", parties);
        DateTime? date(string c) => FormeLigne.DateDe(r, c);
        var precisions = FormeLigne.Joindre(
            date("DateIp") is { } d ? $"ordonnance du {FormeLigne.Date(d)}" : null,
            date("Signification") is { } s ? $"signification {FormeLigne.Date(s)}" : null,
            date("Opposition") is { } o ? $"opposition {FormeLigne.Date(o)}" : null,
            date("Provision") is { } p ? $"provision {FormeLigne.Date(p)}" : null,
            date("Certificat") is { } c ? $"certificat de non-opposition {FormeLigne.Date(c)}" : null);
        return new LigneLue(nom, precisions, Groupe, FormeLigne.Texte(r, "Etat"), [])
        {
            Id = Convert.ToInt32(r["INSTN_ID_INSTN"], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        };
    }
}
