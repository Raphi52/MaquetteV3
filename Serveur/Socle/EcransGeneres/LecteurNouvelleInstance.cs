using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Nouvelle instance » (PROC_XAFF, forme Fiche) : RETROUVER une instance par son numero de role, puis la CORRIGER. Modes
/// d'emploi : LecteurEcran.cs (lecture, recherche, fiche) ; l'enregistrement : EcrivainNouvelleInstance.cs. Plan « RIG v3 100 %
/// fonctionnel », lot 5, 26/11/2026.
///
/// POURQUOI UNE FICHE ET NON UNE SAISIE : malgre son libelle de menu, PROC_XAFF ne cree pas d'instance. Son etape d'entree
/// (ETP_INSTANCE_RECHERCHE) retrouve une instance EXISTANTE par son numero (OPE_INSTANCE_RECHERCHE.cs, txtNumeroMetier ->
/// Instance.GetInstancesByNumRole, ULT_TEXT_NUMERO_METIER.cs:435-470), la seule etape propre au PROC la corrige
/// (ETP_CORRECT_INSTANCE, MetierObligatoire), et MetierCreateAffaire = false (PROC_XAFF.Designer.cs). La creation numerotee
/// d'une instance est dans PROC_IPE et PROC_CORDJRCS (OPE_INSTANCE.CallPrepSauve).
///
/// FILTRE DE LA LISTE : les numeros de role de l'etape de recherche, « Instance=JROP, JRIP, JRCT, JRPC, JRREF, JRJC, JRMDE,
/// JRORC » (PROC_XAFF.Designer.cs, EtapeRechercheInstance.MetierParamNumMetiers), lus comme
/// CODE_AFFAIRE.R_CODEAFFAIR_TYPE_NUM_ROLE in (...) (RechercheInstance.cs:150-154). Le mode PR (JRPR, JRMC, PROC_XAFF.cs:46-48)
/// n'a pas d'entree au menu. ORDRE : numero de role decroissant (FormRechercheInstance.cs:633).
///
/// RECHERCHE (IRechercheEcran) : « Numéro de rôle » et « Nom courant », les deux premiers criteres de la recherche d'instance
/// de RIG v2 (FormRechercheInstance.cs:425-426), cherches dans les colonnes (blancs du numero retires, FormateNumRole :996),
/// comme PROC_MJUD (LecteurInstanceJudiciaire.cs).
///
/// GROUPES : « Référé » = type JRREF ; « Fond » = les sept autres.
/// REGLE D'ETAT :
///   - « Verrouillée » (EnAttente) : une autre demande tient l'instance (VerrouDemandeV2, sequenceur 2J) ;
///   - « Corrigée » (Validee) : une demande XAFF ou XAFFPR validee sur l'instance (DOSSIER_DEMANDE 2J, V), ou un geste
///     « a modifie » de cet ecran au journal des gestes ;
///   - « À corriger » (AFaire) : toute autre.
///
/// FICHE (IFicheEcran) : « Corriger l'instance » (les onze champs que OPE_CORRECT_INSTANCE relie, OPE_CORRECT_INSTANCE.cs:33-43,
/// aux titres de son Designer), puis les sous-listes que PROC_XAFF.SetPhase charge (PROC_XAFF.cs:76-155) : parties, appels,
/// decisions, evenements survenus, futurs ET expires ensemble (:152-153). Non lus : le calendrier de procedure et le chef de
/// demande.
/// </summary>
internal sealed class LecteurNouvelleInstance : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_XAFF";

    internal const string Refere = "Référé", Fond = "Fond";
    internal const string CritereRole = "Numéro de rôle", CritereNom = "Nom courant";

    /// <summary>Les sections et champs de la fiche, aux titres du descripteur (Ouvrir.cs) et du Designer de RIG v2.</summary>
    internal const string SectionInstance = "Corriger l'instance", SectionParties = "Parties", SectionAppels = "Appels de l'affaire",
                          SectionDecisions = "Décisions judiciaires", SectionEvenements = "Événements";

    internal const string ChampRole = "Numéro de rôle", ChampCodeAffaire = "Code affaire", ChampNature = "Nature de l'affaire",
                          ChampNatureParticuliere = "Nature particulière de l'affaire", ChampActe = "Acte de saisine",
                          ChampEnrolement = "Date d'enrôlement", ChampSaisine = "Date saisine tribunal", ChampPremiereAudience = "Date première audience",
                          ChampNomCourant = "Nom courant", ChampDateActe = "Date acte introductif", ChampDelivrance = "Délivrance de l'acte",
                          ChampTexteActe = "Texte sur l'acte introductif";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereRole, CritereNom];

    internal const string TypesXaff = "('JROP', 'JRIP', 'JRCT', 'JRPC', 'JRREF', 'JRJC', 'JRMDE', 'JRORC')";

    private static readonly string Requete = $$"""
        SELECT TOP (@plafond) i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, i.INSTN_DATE_ENROLEMENT,
               ca.R_CODEAFFAIR_TYPE_NUM_ROLE, ca.R_CODEAFFAIR_LIBELLE_ECRAN, v.DMND_NUM_DEMANDE AS Verrou,
               CASE WHEN EXISTS (SELECT 1 FROM v2.DEMANDE AS d JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = d.DMND_ID_DMND
                                 WHERE dd.DSSDM_CODE_SEQUENCEUR = '{{VerrouDemandeV2.SequenceurInstance}}'
                                   AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(100), i.INSTN_ID_INSTN)
                                   AND d.DMND_CODE_PROSS IN ('XAFF', 'XAFFPR') AND d.DMND_ETAT_DEMANDE = 'V')
                      OR EXISTS (SELECT 1 FROM soc.Journal AS j
                                 WHERE j.Ecran = @ecran AND j.Verbe = 'a modifie' AND j.IdObjet = CONVERT(varchar(40), i.INSTN_ID_INSTN))
                    THEN 1 ELSE 0 END AS Corrigee,
               COUNT(*) OVER () AS Total
        FROM v2.INSTANCE AS i
        JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
        OUTER APPLY (SELECT TOP (1) d.DMND_NUM_DEMANDE FROM v2.DEMANDE AS d
                     JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = d.DMND_ID_DMND
                     WHERE dd.DSSDM_CODE_SEQUENCEUR = '{{VerrouDemandeV2.SequenceurInstance}}'
                       AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(100), i.INSTN_ID_INSTN)
                       AND (d.DMND_EN_COURS = 1 OR d.DMND_ETAT_DEMANDE IN ('W', 'I', 'N'))
                     ORDER BY d.DMND_ID_DMND) AS v
        WHERE ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {{TypesXaff}} /*recherche*/
        ORDER BY i.INSTN_NUM_ROLE DESC, i.INSTN_ID_INSTN DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", ""), Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@ecran", EcrivainNouvelleInstance.NomEcran));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond), new("@ecran", EcrivainNouvelleInstance.NomEcran) };
        var filtres = new List<string>();
        string Role(string v) => FormeLigne.Contient(v.Replace(" ", "", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add("(i.INSTN_NUM_ROLE LIKE @texteRole OR i.INSTN_NOM_COURANT LIKE @texteNom)");
            parametres.Add(new SqlParameter("@texteRole", Role(recherche.Texte)));
            parametres.Add(new SqlParameter("@texteNom", FormeLigne.Contient(recherche.Texte)));
        }
        if (recherche.Critere(CritereRole) is { } role)
        {
            filtres.Add("i.INSTN_NUM_ROLE LIKE @role");
            parametres.Add(new SqlParameter("@role", Role(role)));
        }
        if (recherche.Critere(CritereNom) is { } nom)
        {
            filtres.Add("i.INSTN_NOM_COURANT LIKE @nom");
            parametres.Add(new SqlParameter("@nom", FormeLigne.Contient(nom)));
        }
        var sql = Requete.Replace("/*recherche*/", filtres.Count == 0 ? "" : " AND " + string.Join(" AND ", filtres));
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var role = FormeLigne.Texte(r, "INSTN_NUM_ROLE");
        var nomCourant = FormeLigne.Texte(r, "INSTN_NOM_COURANT");
        var verrou = FormeLigne.Texte(r, "Verrou");
        var corrigee = Convert.ToInt32(r["Corrigee"], CultureInfo.InvariantCulture) == 1;
        var enrolement = FormeLigne.DateDe(r, "INSTN_DATE_ENROLEMENT");
        var precisions = FormeLigne.Joindre(
            role.Length > 0 ? $"Rôle {role}" : null,
            FormeLigne.Texte(r, "R_CODEAFFAIR_LIBELLE_ECRAN"),
            enrolement is null ? null : $"enrôlée le {FormeLigne.Date(enrolement)}",
            verrou.Length > 0 ? $"bloquée par la demande {verrou}" : null);
        return new LigneLue(nomCourant.Length > 0 ? nomCourant : $"Instance n° {(role.Length > 0 ? role : FormeLigne.Texte(r, "INSTN_ID_INSTN"))}",
            precisions, FormeLigne.Texte(r, "R_CODEAFFAIR_TYPE_NUM_ROLE") == "JRREF" ? Refere : Fond,
            verrou.Length > 0 ? "EnAttente" : corrigee ? "Validee" : "AFaire", [])
        { Id = FormeLigne.Texte(r, "INSTN_ID_INSTN") };
    }

    // ------------------------------------------------------------------ LA FICHE

    /// <summary>L'instance telle que OPE_CORRECT_INSTANCE la relie (OPE_CORRECT_INSTANCE.cs:33-43).</summary>
    internal sealed record InstanceXaff(int Id, string Role, string TypeInstance, string CodeAffaire, string Nature, string NatureParticuliere,
                                        string Acte, DateTime? Enrolement, DateTime? Saisine, DateTime? PremiereAudience, string NomCourant,
                                        DateTime? DateActe, string Delivrance, string TexteActe)
    {
        /// <summary>Les valeurs de la fiche, par champ (format des champs de l'ecran).</summary>
        public IReadOnlyDictionary<string, string> Valeurs => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ChampRole] = Role, [ChampCodeAffaire] = CodeAffaire, [ChampNature] = Nature, [ChampNatureParticuliere] = NatureParticuliere,
            [ChampActe] = Acte, [ChampEnrolement] = FormeLigne.DateFiche(Enrolement), [ChampSaisine] = FormeLigne.DateFiche(Saisine),
            [ChampPremiereAudience] = FormeLigne.DateFiche(PremiereAudience), [ChampNomCourant] = NomCourant,
            [ChampDateActe] = FormeLigne.DateFiche(DateActe), [ChampDelivrance] = Delivrance, [ChampTexteActe] = TexteActe,
        };
    }

    /// <summary>
    /// Les champs que la V3 enregistre (EcrivainNouvelleInstance) : ceux de OPE_CORRECT_INSTANCE SAUF code affaire, nature, nature
    /// particuliere et acte de saisine -- RIG v2 revalide nature et acte contre les listes du code affaire
    /// (ComboCodeAffaire_ChangementValeur, OPE_CORRECT_INSTANCE.cs:46-93 ; CODE_AFFAIRE.GetLstNaturesAffairesPossible et
    /// GetLstCodesActesProcePossible, CODE_AFFAIRE.cs:89-160, expressions R_CODEAFFAIR_NATURE_AFF_OUI / NON), regle que la V3 ne
    /// reprend pas encore : ces quatre champs restent en lecture.
    /// </summary>
    internal static readonly IReadOnlySet<string> Modifiables = new HashSet<string>(StringComparer.Ordinal)
    {
        ChampEnrolement, ChampSaisine, ChampPremiereAudience, ChampNomCourant, ChampDateActe, ChampDelivrance, ChampTexteActe,
    };

    /// <summary>L'instance <paramref name="id"/> de XAFF (dans la transaction d'un geste si <paramref name="tx"/>, verrouillee en
    /// ecriture) ; null si elle n'existe pas ou n'est pas d'un type de numero de role de XAFF.</summary>
    internal static async Task<InstanceXaff?> LireInstanceAsync(SqlConnection cn, SqlTransaction? tx, int id, CancellationToken ct)
    {
        var sql = $"""
            SELECT i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_TYPE_INSTANCE, i.INSTN_CODE_AFFAIRE, ca.R_CODEAFFAIR_LIBELLE_ECRAN,
                   i.INSTN_NATURE_AFFAIRE, na.R_NATUREAFFA_LIBELLE_ECRAN, i.INSTN_NAT_AFF_PART, i.INSTN_CODE_ACTE_PROCE,
                   ac.R_CODEACTEPR_LIBELLE_ECRAN, i.INSTN_DATE_ENROLEMENT, i.INSTN_DATE_SAISINE_GREFFE, i.INSTN_PREMIERE_AUDIENCE,
                   i.INSTN_NOM_COURANT, i.INSTN_DATE_ACTE, i.INSTN_DELIVRANCE, i.INSTN_TEXTE_ACTE
            FROM v2.INSTANCE AS i {(tx is null ? "" : "WITH (UPDLOCK, HOLDLOCK)")}
            JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
            LEFT JOIN v2.NATURE_AFFAIRE AS na ON na.R_NATUREAFFA_VALEUR = i.INSTN_NATURE_AFFAIRE
            LEFT JOIN v2.CODE_ACTE_PROCE AS ac ON ac.R_CODEACTEPR_VALEUR = i.INSTN_CODE_ACTE_PROCE
            WHERE i.INSTN_ID_INSTN = @id AND ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {TypesXaff}
            """;
        LectureV2.ExigerLecture(sql);
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new InstanceXaff(id, FormeLigne.Texte(r, "INSTN_NUM_ROLE"), FormeLigne.Texte(r, "INSTN_TYPE_INSTANCE"),
            FormeLigne.Joindre(FormeLigne.Texte(r, "INSTN_CODE_AFFAIRE"), FormeLigne.Texte(r, "R_CODEAFFAIR_LIBELLE_ECRAN")),
            FormeLigne.Joindre(FormeLigne.Texte(r, "INSTN_NATURE_AFFAIRE"), FormeLigne.Texte(r, "R_NATUREAFFA_LIBELLE_ECRAN")),
            FormeLigne.Texte(r, "INSTN_NAT_AFF_PART"),
            FormeLigne.Joindre(FormeLigne.Texte(r, "INSTN_CODE_ACTE_PROCE"), FormeLigne.Texte(r, "R_CODEACTEPR_LIBELLE_ECRAN")),
            FormeLigne.DateDe(r, "INSTN_DATE_ENROLEMENT"), FormeLigne.DateDe(r, "INSTN_DATE_SAISINE_GREFFE"),
            FormeLigne.DateDe(r, "INSTN_PREMIERE_AUDIENCE"), FormeLigne.Texte(r, "INSTN_NOM_COURANT"), FormeLigne.DateDe(r, "INSTN_DATE_ACTE"),
            FormeLigne.Texte(r, "INSTN_DELIVRANCE"), FormeLigne.Texte(r, "INSTN_TEXTE_ACTE"));
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        if (await LireInstanceAsync(baseGreffe, null, n, ct) is not { } i) return null;
        var verrou = await VerrouDemandeV2.MotifAsync(baseGreffe, null, "Instance", VerrouDemandeV2.SequenceurInstance, id, ct);
        var champs = i.Valeurs.Select(v => new ChampFiche(v.Key, v.Value) { Modifiable = verrou is null && Modifiables.Contains(v.Key) }).ToArray();
        return new FicheEcran(Procedure, id, i.NomCourant.Length > 0 ? i.NomCourant : $"Instance n° {i.Role}",
            [new SectionFiche(SectionInstance, champs), await PartiesAsync(baseGreffe, n, ct), await AppelsAsync(baseGreffe, n, ct),
             await DecisionsAsync(baseGreffe, n, ct), await EvenementsAsync(baseGreffe, n, ct)])
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

    // PARTIES : demandeurs PRT_QUALIF_PARTIE 'D%' (D, DOIP), defendeurs 'F%', intervenants 'I%' (Partie.cs:823-842), par ordre ;
    // la civilite devant la designation sauf PM et ND (FormRechercheInstance.cs:318-333).
    private static async Task<SectionFiche> PartiesAsync(SqlConnection cn, int id, CancellationToken ct)
    {
        var toutes = await LignesAsync(cn, """
            SELECT LEFT(p.PRT_QUALIF_PARTIE, 1) AS Qualite, p.PRT_CIVILITE, p.PRT_DESIGNATION, p.PRT_NOM, p.PRT_SIREN
            FROM v2.PARTIE AS p WHERE p.PRT_ID_INSTN = @id ORDER BY p.PRT_NUM_ORDRE, p.PRT_ID_PRT
            """, id, r =>
        {
            var civilite = FormeLigne.Texte(r, "PRT_CIVILITE");
            var designation = FormeLigne.Texte(r, "PRT_DESIGNATION") is { Length: > 0 } d ? d : FormeLigne.Texte(r, "PRT_NOM");
            return [FormeLigne.Texte(r, "Qualite"), civilite is "" or "PM" or "ND" ? designation : $"{civilite} {designation}",
                    FormeLigne.Siren(FormeLigne.Texte(r, "PRT_SIREN"))];
        }, ct);
        ChampFiche Liste(string libelle, string qualite) => new(libelle, "")
        {
            Colonnes = ["Désignation", "SIREN"], Lignes = [.. toutes.Where(l => l[0] == qualite).Select(l => l[1..])],
        };
        return new SectionFiche(SectionParties, [Liste("Demandeurs", "D"), Liste("Défendeurs", "F"), Liste("Intervenants", "I")]);
    }

    // APPELS : par date d'audience (Instance.ListeAppelAffaire, Instance.cs:1900-1915).
    private static async Task<SectionFiche> AppelsAsync(SqlConnection cn, int id, CancellationToken ct)
        => new(SectionAppels,
        [
            new("Appels de l'affaire", "")
            {
                Colonnes = ["Date d'audience", "Heure"],
                Lignes = await LignesAsync(cn, """
                    SELECT a.AUDNC_DATE, a.AUDNC_HEURE
                    FROM v2.APPEL_AFFAIRE AS aa JOIN v2.AUDIENCE_CABINET AS a ON a.AUDNC_ID_ADNC = aa.APPAF_ID_ADNC
                    WHERE aa.APPAF_ID_INSTN = @id ORDER BY a.AUDNC_DATE, a.AUDNC_HEURE, aa.APPAF_NUM_ORDRE
                    """, id, r => [FormeLigne.Date(FormeLigne.DateDe(r, "AUDNC_DATE")), FormeLigne.Texte(r, "AUDNC_HEURE")], ct),
            },
        ]);

    // DECISIONS : date rendue, sinon prevue, et type (FormRechercheInstance.cs:336-337).
    private static async Task<SectionFiche> DecisionsAsync(SqlConnection cn, int id, CancellationToken ct)
        => new(SectionDecisions,
        [
            new("Décisions judiciaires", "")
            {
                Colonnes = ["Date", "Type de décision"],
                Lignes = await LignesAsync(cn, """
                    SELECT ISNULL(d.DCJDC_DATE, d.DCJDC_DATE_PREVUE) AS Quand, d.DCJDC_TYPE_DECISION
                    FROM v2.DECISION_JUDICIAIRE AS d WHERE d.DCJDC_ID_INSTN = @id ORDER BY d.DCJDC_NUM_ORDRE, d.DCJDC_ID_DCJDC
                    """, id, r => [FormeLigne.Date(FormeLigne.DateDe(r, "Quand")), FormeLigne.Texte(r, "DCJDC_TYPE_DECISION")], ct),
            },
        ]);

    // EVENEMENTS : survenus ; futurs ET expires dans la meme liste (PROC_XAFF.cs:152-153).
    private static async Task<SectionFiche> EvenementsAsync(SqlConnection cn, int id, CancellationToken ct)
    {
        var tous = await LignesAsync(cn, """
            SELECT e.EVNPR_SURVENU, e.EVNPR_DATE, e.EVNPR_CODE_EVE_PRO
            FROM v2.EVENEMENT_PROCEDURE AS e WHERE e.EVNPR_ID_INSTN = @id ORDER BY e.EVNPR_NUM_ORDRE, e.EVNPR_ID_EVNPR
            """, id, r => [r["EVNPR_SURVENU"] is true ? "1" : "0", FormeLigne.Date(FormeLigne.DateDe(r, "EVNPR_DATE")), FormeLigne.Texte(r, "EVNPR_CODE_EVE_PRO")], ct);
        return new SectionFiche(SectionEvenements,
        [
            new("Événements survenus", "") { Colonnes = ["Date", "Événement"], Lignes = [.. tous.Where(e => e[0] == "1").Select(e => e[1..])] },
            new("Événements futurs et expirés", "") { Colonnes = ["Date", "Événement"], Lignes = [.. tous.Where(e => e[0] == "0").Select(e => e[1..])] },
        ]);
    }
}
