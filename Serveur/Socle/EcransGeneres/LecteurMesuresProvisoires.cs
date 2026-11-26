using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Mesure provisoire » (PROC_MESIN, forme Fiche) : RETROUVER une mesure existante, puis la MODIFIER. Modes d'emploi :
/// LecteurEcran.cs (lecture, recherche, fiche) ; l'enregistrement : EcrivainMesuresProvisoires.cs. Plan « RIG v3 100 %
/// fonctionnel », lot 5, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_MESIN (D:\RigApplication\Source\RIG\DLL\Processus\PROC_MESIN, fiche proc_mesin.md, « Détail OPE »
/// qui fait foi). L'objet est la MESURE (table MESURE, prefixe MSINS_, Mesure.cs:55-64 ; colonnes de
/// RigBaseGreffe\GREFFE\MESURE.cs:249-287), jamais creee ici (MetierCreateAffaire = false, proc_mesin.md:211). Son instance :
/// MSINS_ID_INSTN (Mesure.instanceLiee, Mesure.cs:653-665). Son verrou : le sequenceur « 2F » (Mesure.Sequenceur, Mesure.cs:941),
/// pose par mesure.Lock (PROC_MESIN.cs:72) -- VerrouDemandeV2.
///
/// LA LISTE : RIG v2 n'en a pas (l'etape d'entree cherche UN numero, OPE_MESURE_RECHERCHE.Designer.cs:80
/// « Instance=*;Mesure=*; ») ; la V3 montre toutes les mesures, la plus recente d'abord (MSINS_ID_MSINS decroissant), pour que la
/// recherche ait ou trouver.
///
/// RECHERCHE (IRechercheEcran) : « N° de la mesure ou instance », le seul critere de l'etape de recherche
/// (OPE_MESURE_RECHERCHE.Designer.cs:80) : le numero de mesure (MSINS_NUM_MESURE) ou le numero de role de son instance
/// (OPE_MESURE_RECHERCHE.cs:76-83), blancs retires.
///
/// GROUPES : « Expertise » = type de mesure EXP* (la distinction que fait OPE_MESURE.GetCodeEvePro, OPE_MESURE.cs:149) ;
/// « Autre mesure » = les autres.
/// REGLE D'ETAT :
///   - « Verrouillée » (EnAttente) : une autre demande tient la mesure (sequenceur 2F) -- RIG v2 l'ouvre alors en lecture seule
///     (EtapeMesureReadOnly, PROC_MESIN.cs:81-85) ;
///   - « Modifiée » (Validee) : une demande MESIN validee sur la mesure (DOSSIER_DEMANDE 2F, V), ou un geste « a modifie » de cet
///     ecran au journal des gestes ;
///   - « À modifier » (AFaire) : toute autre.
///
/// FICHE (IFicheEcran) : la mesure (champs de OPE_MESURE.DoInitZoneSaisie, OPE_MESURE.cs:45-63, titres du descripteur), puis ce que
/// PROC_MESIN.SetPhase charge (PROC_MESIN.cs:93-147) : demandeurs, defendeurs, intervenants de l'instance, evenements survenus,
/// futurs ET expires dans la meme liste (:135-136). Non lue : la facturation MESIN (articles, :143).
/// </summary>
internal sealed class LecteurMesuresProvisoires : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_MESIN";

    internal const string Sequenceur = "2F";
    internal const string Expertise = "Expertise", AutreMesure = "Autre mesure";
    internal const string CritereNumero = "N° de la mesure ou instance";

    internal const string SectionMesure = "Mesure", SectionDemandeurs = "Demandeurs", SectionDefendeurs = "Défendeurs",
                          SectionIntervenants = "Intervenants", SectionSurvenus = "Événements survenus",
                          SectionFuturs = "Événements futurs et expirés";

    internal const string ChampNumero = "N° de mesure", ChampType = "Type de mesure", ChampOuverture = "Ouverture",
                          ChampSansSuite = "Classement sans suite", ChampCaducite = "Caducité",
                          ChampLimiteRapport = "Limite de dépôt du rapport", ChampDepotRapport = "Dépôt du rapport",
                          ChampPrevisionRapport = "Prévision de dépôt du rapport", ChampCout = "Coût prévu de l'expertise",
                          ChampLimiteDiligences = "Limite de dépôt de l'état des diligences",
                          ChampDepotDiligences = "Dépôt de l'état des diligences", ChampInformations = "Informations";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereNumero];

    private static readonly string Requete = $$"""
        SELECT TOP (@plafond) m.MSINS_ID_MSINS, m.MSINS_NUM_MESURE, m.MSINS_TYPE_MESURE, t.R_TYPEMESURE_LIBELLE_ECRAN,
               m.MSINS_DATE_OUVERTURE, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, v.DMND_NUM_DEMANDE AS Verrou,
               CASE WHEN EXISTS (SELECT 1 FROM v2.DEMANDE AS d JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = d.DMND_ID_DMND
                                 WHERE dd.DSSDM_CODE_SEQUENCEUR = '{{Sequenceur}}'
                                   AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(100), m.MSINS_ID_MSINS)
                                   AND d.DMND_CODE_PROSS = 'MESIN' AND d.DMND_ETAT_DEMANDE = 'V')
                      OR EXISTS (SELECT 1 FROM soc.Journal AS j
                                 WHERE j.Ecran = @ecran AND j.Verbe = 'a modifie' AND j.IdObjet = CONVERT(varchar(40), m.MSINS_ID_MSINS))
                    THEN 1 ELSE 0 END AS Modifiee,
               COUNT(*) OVER () AS Total
        FROM v2.MESURE AS m
        LEFT JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = m.MSINS_ID_INSTN
        LEFT JOIN v2.TYPE_MESURE AS t ON t.R_TYPEMESURE_VALEUR = m.MSINS_TYPE_MESURE
        OUTER APPLY (SELECT TOP (1) d.DMND_NUM_DEMANDE FROM v2.DEMANDE AS d
                     JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = d.DMND_ID_DMND
                     WHERE dd.DSSDM_CODE_SEQUENCEUR = '{{Sequenceur}}'
                       AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(100), m.MSINS_ID_MSINS)
                       AND (d.DMND_EN_COURS = 1 OR d.DMND_ETAT_DEMANDE IN ('W', 'I', 'N'))
                     ORDER BY d.DMND_ID_DMND) AS v
        WHERE 1 = 1 /*recherche*/
        ORDER BY m.MSINS_ID_MSINS DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", ""), Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@ecran", EcrivainMesuresProvisoires.NomEcran));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond), new("@ecran", EcrivainMesuresProvisoires.NomEcran) };
        var filtres = new List<string>();
        var n = 0;
        // Le numero de mesure ou le numero de role de son instance (OPE_MESURE_RECHERCHE.cs:76-83), blancs retires.
        void Numero(string valeur)
        {
            filtres.Add($"(m.MSINS_NUM_MESURE LIKE @n{n} OR i.INSTN_NUM_ROLE LIKE @n{n})");
            parametres.Add(new SqlParameter($"@n{n++}", FormeLigne.Contient(valeur.Replace(" ", "", StringComparison.Ordinal))));
        }
        if (!string.IsNullOrWhiteSpace(recherche.Texte)) Numero(recherche.Texte);
        if (recherche.Critere(CritereNumero) is { } numero) Numero(numero);
        var sql = Requete.Replace("/*recherche*/", filtres.Count == 0 ? "" : " AND " + string.Join(" AND ", filtres));
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var numero = FormeLigne.Texte(r, "MSINS_NUM_MESURE");
        var type = FormeLigne.Texte(r, "MSINS_TYPE_MESURE");
        var role = FormeLigne.Texte(r, "INSTN_NUM_ROLE");
        var nom = FormeLigne.Texte(r, "INSTN_NOM_COURANT");
        var verrou = FormeLigne.Texte(r, "Verrou");
        var ouverture = FormeLigne.DateDe(r, "MSINS_DATE_OUVERTURE");
        var precisions = FormeLigne.Joindre(
            numero.Length > 0 ? $"Mesure {numero}" : null,
            FormeLigne.Texte(r, "R_TYPEMESURE_LIBELLE_ECRAN") is { Length: > 0 } libelle ? libelle : type,
            ouverture is null ? null : $"ouverte le {FormeLigne.Date(ouverture)}",
            role.Length > 0 ? $"instance {role}" : null,
            verrou.Length > 0 ? $"bloquée par la demande {verrou}" : null);
        var modifiee = Convert.ToInt32(r["Modifiee"], CultureInfo.InvariantCulture) == 1;
        return new LigneLue(nom.Length > 0 ? nom : $"Mesure n° {(numero.Length > 0 ? numero : FormeLigne.Texte(r, "MSINS_ID_MSINS"))}", precisions,
            type.StartsWith("EXP", StringComparison.Ordinal) ? Expertise : AutreMesure,
            verrou.Length > 0 ? "EnAttente" : modifiee ? "Validee" : "AFaire", [])
        { Id = FormeLigne.Texte(r, "MSINS_ID_MSINS") };
    }

    // ------------------------------------------------------------------ LA FICHE

    /// <summary>La mesure telle que OPE_MESURE la relie (OPE_MESURE.cs:45-63).</summary>
    internal sealed record MesureLue(int Id, int? IdInstance, string Numero, string Type, DateTime? Ouverture, DateTime? SansSuite,
                                     DateTime? Caducite, DateTime? LimiteRapport, DateTime? DepotRapport, DateTime? PrevisionRapport,
                                     int? Cout, DateTime? LimiteDiligences, DateTime? DepotDiligences, string Informations, string NomInstance)
    {
        /// <summary>Les valeurs de la fiche, par champ (format des champs de l'ecran).</summary>
        public IReadOnlyDictionary<string, string> Valeurs => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ChampNumero] = Numero, [ChampType] = Type, [ChampOuverture] = FormeLigne.DateFiche(Ouverture),
            [ChampSansSuite] = FormeLigne.DateFiche(SansSuite), [ChampCaducite] = FormeLigne.DateFiche(Caducite),
            [ChampLimiteRapport] = FormeLigne.DateFiche(LimiteRapport), [ChampDepotRapport] = FormeLigne.DateFiche(DepotRapport),
            [ChampPrevisionRapport] = FormeLigne.DateFiche(PrevisionRapport),
            [ChampCout] = Cout?.ToString(CultureInfo.InvariantCulture) ?? "",
            [ChampLimiteDiligences] = FormeLigne.DateFiche(LimiteDiligences), [ChampDepotDiligences] = FormeLigne.DateFiche(DepotDiligences),
            [ChampInformations] = Informations,
        };
    }

    /// <summary>
    /// Les champs que la V3 enregistre (EcrivainMesuresProvisoires). En lecture : le numero et l'ouverture (MetierReadOnly,
    /// OPE_MESURE.Designer.cs:91 et :112), et les quatre dates de rapport et de diligences -- les changer, dans RIG v2, ajoute ou
    /// retire un evenement de procedure de la mesure (ultDateLimiteDepotRapport_ChangementValeur et suivants, OPE_MESURE.cs:95-142 ;
    /// AddEvePro / DeleteEveProIfNew, codes DEPOTRAP / PROROMIS / DEPOTDIL de TYPE_OPERATION), regle que la V3 ne reprend pas encore.
    /// </summary>
    internal static readonly IReadOnlySet<string> Modifiables = new HashSet<string>(StringComparer.Ordinal)
    {
        ChampType, ChampSansSuite, ChampCaducite, ChampPrevisionRapport, ChampCout, ChampInformations,
    };

    /// <summary>La mesure <paramref name="id"/> (dans la transaction d'un geste si <paramref name="tx"/>, verrouillee en ecriture) ;
    /// null si elle n'existe pas.</summary>
    internal static async Task<MesureLue?> LireMesureAsync(SqlConnection cn, SqlTransaction? tx, int id, CancellationToken ct)
    {
        var sql = $"""
            SELECT m.MSINS_ID_MSINS, m.MSINS_ID_INSTN, m.MSINS_NUM_MESURE, m.MSINS_TYPE_MESURE, m.MSINS_DATE_OUVERTURE, m.MSINS_DATE_SANS_SUITE,
                   m.MSINS_DATE_CADUCITE, m.MSINS_DATE_LIM_DEPOT_RAPPORT, m.MSINS_DATE_DEPOT_RAPPORT, m.MSINS_DATE_PREV_DEPOT_RAPPORT,
                   m.MSINS_COUT_PREV_EXPERTISE, m.MSINS_DATE_LIM_DEPOT_DILIGENCES, m.MSINS_DATE_DEPOT_DILIGENCES, m.MSINS_INFORMATIONS,
                   i.INSTN_NOM_COURANT
            FROM v2.MESURE AS m {(tx is null ? "" : "WITH (UPDLOCK, HOLDLOCK)")}
            LEFT JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = m.MSINS_ID_INSTN
            WHERE m.MSINS_ID_MSINS = @id
            """;
        LectureV2.ExigerLecture(sql);
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new MesureLue(id, r["MSINS_ID_INSTN"] is int instance ? instance : null, FormeLigne.Texte(r, "MSINS_NUM_MESURE"),
            FormeLigne.Texte(r, "MSINS_TYPE_MESURE"), FormeLigne.DateDe(r, "MSINS_DATE_OUVERTURE"), FormeLigne.DateDe(r, "MSINS_DATE_SANS_SUITE"),
            FormeLigne.DateDe(r, "MSINS_DATE_CADUCITE"), FormeLigne.DateDe(r, "MSINS_DATE_LIM_DEPOT_RAPPORT"),
            FormeLigne.DateDe(r, "MSINS_DATE_DEPOT_RAPPORT"), FormeLigne.DateDe(r, "MSINS_DATE_PREV_DEPOT_RAPPORT"),
            r["MSINS_COUT_PREV_EXPERTISE"] is int cout ? cout : null, FormeLigne.DateDe(r, "MSINS_DATE_LIM_DEPOT_DILIGENCES"),
            FormeLigne.DateDe(r, "MSINS_DATE_DEPOT_DILIGENCES"), FormeLigne.Texte(r, "MSINS_INFORMATIONS"), FormeLigne.Texte(r, "INSTN_NOM_COURANT"));
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        if (await LireMesureAsync(baseGreffe, null, n, ct) is not { } m) return null;
        // Verrou a l'ouverture ; tenue par une autre demande, la mesure reste en lecture seule (PROC_MESIN.cs:72-85).
        var verrou = await VerrouDemandeV2.MotifAsync(baseGreffe, null, "Mesure", Sequenceur, id, ct);
        var champs = m.Valeurs.Select(v => new ChampFiche(v.Key, v.Value) { Modifiable = verrou is null && Modifiables.Contains(v.Key) }).ToArray();
        var parties = m.IdInstance is { } instance ? await PartiesAsync(baseGreffe, instance, ct) : [];
        var evenements = await EvenementsAsync(baseGreffe, n, ct);
        SectionFiche Parties(string section, string qualite) => new(section,
            [new ChampFiche(section, "") { Colonnes = ["Désignation", "SIREN"], Lignes = [.. parties.Where(l => l[0] == qualite).Select(l => l[1..])] }]);
        SectionFiche Evenements(string section, bool survenus) => new(section,
            [new ChampFiche(section, "") { Colonnes = ["Date", "Événement"], Lignes = [.. evenements.Where(e => (e[0] == "1") == survenus).Select(e => e[1..])] }]);
        return new FicheEcran(Procedure, id, m.NomInstance.Length > 0 ? m.NomInstance : $"Mesure n° {m.Numero}",
            [new SectionFiche(SectionMesure, champs), Parties(SectionDemandeurs, "D"), Parties(SectionDefendeurs, "F"),
             Parties(SectionIntervenants, "I"), Evenements(SectionSurvenus, true), Evenements(SectionFuturs, false)])
        {
            LectureSeule = verrou is not null,
            Motif = verrou,
        };
    }

    private static async Task<string[][]> LignesAsync(SqlConnection cn, string sql, int id, Func<SqlDataReader, string[]> ligne, CancellationToken ct)
    {
        var lignes = new List<string[]>();
        await using var r = await LectureV2.LireAsync(cn, sql, ct, new SqlParameter("@id", id));
        while (await r.ReadAsync(ct)) lignes.Add(ligne(r));
        return [.. lignes];
    }

    // PARTIES de l'instance de la mesure (instance.ListePartiesDemandeur..., PROC_MESIN.cs:102-123) : qualifiant D%, F%, I%
    // (Partie.cs:823-842), par ordre ; la civilite devant la designation sauf PM et ND (FormRechercheInstance.cs:318-333).
    private static Task<string[][]> PartiesAsync(SqlConnection cn, int idInstance, CancellationToken ct)
        => LignesAsync(cn, """
            SELECT LEFT(p.PRT_QUALIF_PARTIE, 1) AS Qualite, p.PRT_CIVILITE, p.PRT_DESIGNATION, p.PRT_NOM, p.PRT_SIREN
            FROM v2.PARTIE AS p WHERE p.PRT_ID_INSTN = @id ORDER BY p.PRT_NUM_ORDRE, p.PRT_ID_PRT
            """, idInstance, r =>
        {
            var civilite = FormeLigne.Texte(r, "PRT_CIVILITE");
            var designation = FormeLigne.Texte(r, "PRT_DESIGNATION") is { Length: > 0 } d ? d : FormeLigne.Texte(r, "PRT_NOM");
            return [FormeLigne.Texte(r, "Qualite"), civilite is "" or "PM" or "ND" ? designation : $"{civilite} {designation}",
                    FormeLigne.Siren(FormeLigne.Texte(r, "PRT_SIREN"))];
        }, ct);

    // EVENEMENTS de la mesure (EVNPR_ID_MSINS) : survenus ; futurs ET expires ensemble (Mesure.cs:260-283, PROC_MESIN.cs:135-136),
    // par numero d'ordre.
    private static Task<string[][]> EvenementsAsync(SqlConnection cn, int id, CancellationToken ct)
        => LignesAsync(cn, """
            SELECT e.EVNPR_SURVENU, e.EVNPR_DATE, e.EVNPR_CODE_EVE_PRO
            FROM v2.EVENEMENT_PROCEDURE AS e WHERE e.EVNPR_ID_MSINS = @id ORDER BY e.EVNPR_NUM_ORDRE, e.EVNPR_ID_EVNPR
            """, id, r => [r["EVNPR_SURVENU"] is true ? "1" : "0", FormeLigne.Date(FormeLigne.DateDe(r, "EVNPR_DATE")), FormeLigne.Texte(r, "EVNPR_CODE_EVE_PRO")], ct);
}
