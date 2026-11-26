using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Instance judiciaire » (PROC_MJUD, dans l'ecran reuni « Corriger une affaire judiciaire ») : les instances
/// judiciaires du greffe, une recherche par numero de role et nom courant, et la FICHE d'une instance par sections. Modes
/// d'emploi : LecteurEcran.cs (lecture), EcrivainInstanceJudiciaire.cs (enregistrer). Plan « RIG v3 100 % fonctionnel »,
/// lot 2, 26/11/2026 : l'ecran pilote du socle fonctionnel.
///
/// SOURCE (ancien RIG) : PROC_MJUD (D:\RigApplication\Source\RIG\DLL\Processus\PROC_MJUD, fiche proc_mjud.md) modifie UNE
/// instance existante (MetierCreateAffaire = false, PROC_MJUD.Designer.cs:322), choisie par l'etape de recherche d'instance.
///
/// FILTRE DE LA LISTE : la recherche d'instance de MJUD ne propose que les numeros de role
/// « Instance=JROP, JRIP, JRCT, JRPC, JRREF, JRJC, JRORC, JRDD » (PROC_MJUD.Designer.cs:271, MetierParamNumMetiers), que la
/// recherche lit comme « CODE_AFFAIRE.R_CODEAFFAIR_TYPE_NUM_ROLE in (...) » (RigOcxRecherche\ClassesRecherche\
/// RechercheInstance.cs:150-154, jointure CODE_AFFAIRE sur INSTN_CODE_AFFAIRE). JRPR et JRMC (prevention, MAH/CON) sont le
/// mode MODIPR, pas MJUD (PROC_MJUD.cs:50-52). ORDRE : « order by instn_num_role desc » (FormRechercheInstance.cs:633).
///
/// RECHERCHE (IRechercheEcran) : les criteres « Numéro de rôle » et « Nom courant » du descripteur sont les deux premiers de
/// la recherche V2 (FormRechercheInstance.cs:425-426, txtNumRole et txtNomCourant). La V2 les cherche dans l'index des mots
/// directeurs (RIG_MOTS_DIRECTEUR_PCINST.MDPCI_MOTS, codes NR et NI : Mots.cs:602-613, RechercheInstance.cs:236), tire de ces
/// colonnes ; la V3 cherche dans les colonnes elles-memes, INSTN_NUM_ROLE et INSTN_NOM_COURANT (LIKE, sans casse, blancs du
/// numero retires comme FormateNumRole, FormRechercheInstance.cs:996). Le champ de recherche cherche l'un OU l'autre.
///
/// GROUPES (descripteur) : « En référé » = type de numero JRREF (NUMERO_METIER : « n° de rôle des référés ») ; les sept
/// autres types -> « Au fond ».
///
/// REGLE D'ETAT :
///   - « Verrouillée » (EnAttente) : une autre demande tient l'instance (VerrouDemandeV2, sequenceur 2J) -- la V2 n'entre
///     pas en saisie (PROC_MJUD.cs:126-133) ;
///   - « Modifiée » (Validee) : une demande MJUD de la V2 validee sur l'instance (DOSSIER_DEMANDE 2J, DMND_CODE_PROSS MJUD,
///     MODIPR ou MJUD_TRIDIG -- les trois codes du processus, proc_mjud.md:6 --, DMND_ETAT_DEMANDE V), ou un geste
///     « a modifie » de cet ecran au journal des gestes de la V3 (soc.Journal) ;
///   - « À modifier » (AFaire) : toute autre. « En cours » n'a pas de ligne : une demande en cours sur l'instance est le
///     verrou lui-meme.
///
/// FICHE (IFicheEcran) : les sections du descripteur que la V3 lit -- Instance (OPE_INSTANCE, l'etape « Instance » de
/// MJUD), Parties, Appels d'affaire, Décision et chef de demande (les decisions), Événements. Non lus, et donc absents de la
/// fiche : le chef de demande et la reponse du defendeur, la facturation, la note sur l'instance (NOTA_PROCEDURE, absente de
/// RIG_DEV), le calendrier et le coffre-fort.
/// </summary>
internal sealed class LecteurInstanceJudiciaire : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_MJUD";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string AuFond = "Au fond", EnRefere = "En référé";

    /// <summary>Les criteres du descripteur cherches dans la base.</summary>
    internal const string CritereRole = "Numéro de rôle", CritereNom = "Nom courant";

    /// <summary>Les sections et champs de la fiche, aux titres et libelles du descripteur (ModifierAffaire.cs).</summary>
    internal const string SectionInstance = "Instance", SectionParties = "Parties", SectionAppels = "Appels d'affaire",
                          SectionDecision = "Décision et chef de demande", SectionEvenements = "Événements";

    internal const string ChampRole = "Numéro de rôle", ChampNom = "Nom courant", ChampCodeAffaire = "Code affaire",
                          ChampNature = "Nature affaire", ChampActe = "Acte de saisine", ChampDateActe = "Date de l'acte",
                          ChampSaisine = "Saisine tribunal", ChampEnrolement = "Enrôlement",
                          ChampDelivrance = "Délivrance de l'acte de saisine", ChampIpEuropeenne = "IP Européenne",
                          ChampNote = "Note sur l'affaire";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereRole, CritereNom];

    // Les types de numero de role de MJUD (PROC_MJUD.Designer.cs:271).
    private const string TypesMjud = "('JROP', 'JRIP', 'JRCT', 'JRPC', 'JRREF', 'JRJC', 'JRORC', 'JRDD')";

    // Le verrou (VerrouDemandeV2, Demande.cs:1290-1297) et la modification deja faite, ligne par ligne.
    private static readonly string Requete = $$"""
        SELECT TOP (@plafond) i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, i.INSTN_DATE_ENROLEMENT,
               ca.R_CODEAFFAIR_TYPE_NUM_ROLE, na.R_NATUREAFFA_LIBELLE_ECRAN, v.DMND_NUM_DEMANDE AS Verrou,
               CASE WHEN EXISTS (SELECT 1 FROM v2.DEMANDE AS d JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = d.DMND_ID_DMND
                                 WHERE dd.DSSDM_CODE_SEQUENCEUR = '{{VerrouDemandeV2.SequenceurInstance}}'
                                   AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(100), i.INSTN_ID_INSTN)
                                   AND d.DMND_CODE_PROSS IN ('MJUD', 'MODIPR', 'MJUD_TRIDIG') AND d.DMND_ETAT_DEMANDE = 'V')
                      OR EXISTS (SELECT 1 FROM soc.Journal AS j
                                 WHERE j.Ecran = @ecran AND j.Verbe = 'a modifie' AND j.IdObjet = CONVERT(varchar(40), i.INSTN_ID_INSTN))
                    THEN 1 ELSE 0 END AS Modifiee,
               COUNT(*) OVER () AS Total
        FROM v2.INSTANCE AS i
        JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
        LEFT JOIN v2.NATURE_AFFAIRE AS na ON na.R_NATUREAFFA_VALEUR = i.INSTN_NATURE_AFFAIRE
        OUTER APPLY (SELECT TOP (1) d.DMND_NUM_DEMANDE FROM v2.DEMANDE AS d
                     JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = d.DMND_ID_DMND
                     WHERE dd.DSSDM_CODE_SEQUENCEUR = '{{VerrouDemandeV2.SequenceurInstance}}'
                       AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(100), i.INSTN_ID_INSTN)
                       AND (d.DMND_EN_COURS = 1 OR d.DMND_ETAT_DEMANDE IN ('W', 'I', 'N'))
                     ORDER BY d.DMND_ID_DMND) AS v
        WHERE ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {{TypesMjud}} /*recherche*/
        ORDER BY i.INSTN_NUM_ROLE DESC, i.INSTN_ID_INSTN DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", ""), Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@ecran", EcrivainInstanceJudiciaire.NomEcran));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond), new("@ecran", EcrivainInstanceJudiciaire.NomEcran) };
        var filtres = new List<string>();
        // Le numero de role sans ses blancs (FormateNumRole) ; le nom courant tel quel. LIKE sans casse (collation du greffe).
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
        var modifiee = Convert.ToInt32(r["Modifiee"], CultureInfo.InvariantCulture) == 1;
        var etat = verrou.Length > 0 ? "EnAttente" : modifiee ? "Validee" : "AFaire";
        var enrolement = FormeLigne.DateDe(r, "INSTN_DATE_ENROLEMENT");
        var precisions = FormeLigne.Joindre(
            role.Length > 0 ? $"Rôle {role}" : null,
            FormeLigne.Texte(r, "R_NATUREAFFA_LIBELLE_ECRAN"),
            enrolement is null ? null : $"enrôlée le {FormeLigne.Date(enrolement)}",
            verrou.Length > 0 ? $"bloquée par la demande {verrou}" : null);
        var groupe = FormeLigne.Texte(r, "R_CODEAFFAIR_TYPE_NUM_ROLE") == "JRREF" ? EnRefere : AuFond;
        var nom = nomCourant.Length > 0 ? nomCourant : $"Instance n° {(role.Length > 0 ? role : FormeLigne.Texte(r, "INSTN_ID_INSTN"))}";
        return new LigneLue(nom, precisions, groupe, etat, []) { Id = FormeLigne.Texte(r, "INSTN_ID_INSTN") };
    }

    // ------------------------------------------------------------------ LA FICHE

    /// <summary>L'instance et ce que l'ecrivain doit savoir d'elle : ses valeurs et ce qui s'y modifie.</summary>
    internal sealed record InstanceLue(int Id, string Role, string NomCourant, string TypeInstance, string CodeAffaire, string Nature,
                                       string Acte, bool Assignation, DateTime? DateActe, DateTime? Saisine, DateTime? Enrolement,
                                       string Delivrance, bool IpEuropeenne, string? Note, int NbAppels, DateTime? DateActeDefendeurs)
    {
        /// <summary>
        /// Les champs que la V2 laisse saisir sur CETTE instance en modification (OPE_INSTANCE.MAJControleSuivantLesCas) :
        ///   - Saisine tribunal : toujours (Designer :180, obligatoire) ;
        ///   - Date de l'acte : si l'acte de saisine n'est pas une assignation (ultDateActe.Visible =
        ///     instance.bDateActeASaisirInInstance, OPE_INSTANCE.cs:1704 ; Instance.cs:3343-3346 ; Assignation = ISO « 01 »,
        ///     CODE_ACTE_PROCE.cs:39-43) ;
        ///   - Note sur l'affaire : types 07, 09, JC et 08 (OPE_INSTANCE.cs:1717, :1741) ; type RP si une note existe deja ou si
        ///     l'instance a un appel d'affaire (:1707-1708 ; les deux autres conditions, IsOPdePC et le code affaire INJDCA ou
        ///     INJRBE, ne sont pas reprises : la note n'y est pas modifiable dans la V3) ;
        ///   - IP Européenne : type IP (chkIPEuropeenne.Visible = true, :1746).
        /// Jamais modifiables en modification : code affaire, nature, acte de saisine (MetierReadOnly, :1681-1685), enrolement
        /// (DateEnrolement.MetierReadOnly, OPE_INSTANCE.Designer.cs:147), delivrance (non reprise : elle suit l'acte).
        /// </summary>
        public IReadOnlySet<string> Modifiables
        {
            get
            {
                var m = new HashSet<string>(StringComparer.Ordinal) { ChampSaisine };
                if (!Assignation) m.Add(ChampDateActe);
                if (TypeInstance is "07" or "09" or "JC" or "08" || (TypeInstance == "RP" && (Note is not null || NbAppels > 0))) m.Add(ChampNote);
                if (TypeInstance == "IP") m.Add(ChampIpEuropeenne);
                return m;
            }
        }

        /// <summary>La date de l'acte que la V2 controle : celle des defendeurs pour une assignation (la plus ancienne de leurs
        /// actes, Instance.GetDateActeAssignation, Instance.cs:3878-3890), sinon celle de l'instance (OPE_INSTANCE.cs:1326-1331).</summary>
        public DateTime? DateActeControlee(DateTime? dateActe) => Assignation ? DateActeDefendeurs : dateActe;
    }

    /// <summary>L'instance <paramref name="id"/>, lue sur <paramref name="cn"/> (dans la transaction d'un geste si
    /// <paramref name="tx"/>, verrouillee en ecriture jusqu'a sa fin) ; null si elle n'existe pas ou n'est pas de MJUD.</summary>
    internal static async Task<InstanceLue?> LireInstanceAsync(SqlConnection cn, SqlTransaction? tx, int id, CancellationToken ct)
    {
        var sql = $"""
            SELECT i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, i.INSTN_TYPE_INSTANCE, i.INSTN_CODE_AFFAIRE,
                   ca.R_CODEAFFAIR_LIBELLE_ECRAN, na.R_NATUREAFFA_LIBELLE_ECRAN, ac.R_CODEACTEPR_LIBELLE_ECRAN, ac.R_CODEACTEPR_ISO,
                   i.INSTN_DATE_ACTE, i.INSTN_DATE_SAISINE_GREFFE, i.INSTN_DATE_ENROLEMENT, dl.R_DELIVRANCE_LIBELLE_ECRAN,
                   i.INSTN_IP_EUROPEENNE, i.INSTN_NOTE_AUDIENCE,
                   (SELECT COUNT(*) FROM v2.APPEL_AFFAIRE AS a WHERE a.APPAF_ID_INSTN = i.INSTN_ID_INSTN) AS NbAppels,
                   (SELECT MIN(p.PRT_DATE_ACTE) FROM v2.PARTIE AS p WHERE p.PRT_ID_INSTN = i.INSTN_ID_INSTN AND p.PRT_QUALIF_PARTIE LIKE 'F%') AS ActeDefendeurs
            FROM v2.INSTANCE AS i {(tx is null ? "" : "WITH (UPDLOCK, HOLDLOCK)")}
            JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
            LEFT JOIN v2.NATURE_AFFAIRE AS na ON na.R_NATUREAFFA_VALEUR = i.INSTN_NATURE_AFFAIRE
            LEFT JOIN v2.CODE_ACTE_PROCE AS ac ON ac.R_CODEACTEPR_VALEUR = i.INSTN_CODE_ACTE_PROCE
            LEFT JOIN v2.DELIVRANCE AS dl ON dl.R_DELIVRANCE_VALEUR = i.INSTN_DELIVRANCE
            WHERE i.INSTN_ID_INSTN = @id AND ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {TypesMjud}
            """;
        LectureV2.ExigerLecture(sql);
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var iso = FormeLigne.Texte(r, "R_CODEACTEPR_ISO");
        return new InstanceLue(
            id, FormeLigne.Texte(r, "INSTN_NUM_ROLE"), FormeLigne.Texte(r, "INSTN_NOM_COURANT"), FormeLigne.Texte(r, "INSTN_TYPE_INSTANCE"),
            FormeLigne.Joindre(FormeLigne.Texte(r, "INSTN_CODE_AFFAIRE"), FormeLigne.Texte(r, "R_CODEAFFAIR_LIBELLE_ECRAN")),
            FormeLigne.Texte(r, "R_NATUREAFFA_LIBELLE_ECRAN"), FormeLigne.Texte(r, "R_CODEACTEPR_LIBELLE_ECRAN"),
            Assignation: iso == "01",
            FormeLigne.DateDe(r, "INSTN_DATE_ACTE"), FormeLigne.DateDe(r, "INSTN_DATE_SAISINE_GREFFE"), FormeLigne.DateDe(r, "INSTN_DATE_ENROLEMENT"),
            FormeLigne.Texte(r, "R_DELIVRANCE_LIBELLE_ECRAN"), r["INSTN_IP_EUROPEENNE"] is true,
            r["INSTN_NOTE_AUDIENCE"] is string note ? note : null,
            Convert.ToInt32(r["NbAppels"], CultureInfo.InvariantCulture), FormeLigne.DateDe(r, "ActeDefendeurs"));
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        if (await LireInstanceAsync(baseGreffe, null, n, ct) is not { } i) return null;
        var verrou = await VerrouDemandeV2.MotifAsync(baseGreffe, null, "Instance", VerrouDemandeV2.SequenceurInstance, id, ct);
        var modifiables = verrou is null ? i.Modifiables : new HashSet<string>();
        ChampFiche Champ(string libelle, string valeur) => new(libelle, valeur) { Modifiable = modifiables.Contains(libelle) };

        var instance = new SectionFiche(SectionInstance,
        [
            Champ(ChampRole, i.Role), Champ(ChampNom, i.NomCourant), Champ(ChampCodeAffaire, i.CodeAffaire), Champ(ChampNature, i.Nature),
            Champ(ChampActe, i.Acte), Champ(ChampDateActe, FormeLigne.DateFiche(i.DateActe)), Champ(ChampSaisine, FormeLigne.DateFiche(i.Saisine)),
            Champ(ChampEnrolement, FormeLigne.DateFiche(i.Enrolement)), Champ(ChampDelivrance, i.Delivrance),
            Champ(ChampIpEuropeenne, FormeLigne.Case(i.IpEuropeenne)), Champ(ChampNote, i.Note ?? ""),
        ]);
        return new FicheEcran(Procedure, id, i.NomCourant.Length > 0 ? i.NomCourant : $"Instance n° {i.Role}",
            [instance, await PartiesAsync(baseGreffe, n, ct), await AppelsAsync(baseGreffe, n, maintenant, ct),
             await DecisionsAsync(baseGreffe, n, ct), await EvenementsAsync(baseGreffe, n, ct)])
        {
            LectureSeule = verrou is not null,
            Motif = verrou,
        };
    }

    /// <summary>Les lignes d'une sous-liste (Tableau) : une requete, une cellule par colonne.</summary>
    private static async Task<string[][]> LignesAsync(SqlConnection cn, string sql, int id, Func<SqlDataReader, string[]> ligne, CancellationToken ct)
    {
        var lignes = new List<string[]>();
        await using var r = await LectureV2.LireAsync(cn, sql, ct, new SqlParameter("@id", id));
        while (await r.ReadAsync(ct)) lignes.Add(ligne(r));
        return [.. lignes];
    }

    // PARTIES : demandeurs PRT_QUALIF_PARTIE 'D%', defendeurs 'F%', intervenants 'I%', par PRT_NUM_ORDRE ; la designation comme
    // la recherche V2 l'ecrit : civilite (sauf PM et ND) puis designation (FormRechercheInstance.cs:318-333).
    private static readonly string[] ColonnesParties = ["Désignation", "SIREN", "Présentation"];

    private static async Task<SectionFiche> PartiesAsync(SqlConnection cn, int id, CancellationToken ct)
    {
        var toutes = await LignesAsync(cn, """
            SELECT LEFT(p.PRT_QUALIF_PARTIE, 1) AS Qualite, p.PRT_CIVILITE, p.PRT_DESIGNATION, p.PRT_NOM, p.PRT_SIREN, p.PRT_MODE_PRESENTATION
            FROM v2.PARTIE AS p WHERE p.PRT_ID_INSTN = @id ORDER BY p.PRT_NUM_ORDRE, p.PRT_ID_PRT
            """, id, r =>
        {
            var civilite = FormeLigne.Texte(r, "PRT_CIVILITE");
            var designation = FormeLigne.Texte(r, "PRT_DESIGNATION") is { Length: > 0 } d ? d : FormeLigne.Texte(r, "PRT_NOM");
            var nom = civilite is "" or "PM" or "ND" ? designation : $"{civilite} {designation}";
            return [FormeLigne.Texte(r, "Qualite"), nom, FormeLigne.Siren(FormeLigne.Texte(r, "PRT_SIREN")), FormeLigne.Texte(r, "PRT_MODE_PRESENTATION")];
        }, ct);
        ChampFiche Liste(string libelle, string qualite) => new(libelle, "")
        {
            Colonnes = ColonnesParties,
            Lignes = [.. toutes.Where(l => l[0] == qualite).Select(l => l[1..])],
        };
        return new SectionFiche(SectionParties, [Liste("Demandeurs", "D"), Liste("Défendeurs", "F"), Liste("Intervenants", "I")]);
    }

    // APPELS D'AFFAIRE : par date d'audience (Instance.ListeAppelAffaire, Instance.cs:1900-1915) ; passe ou futur selon
    // l'audience (AudienceCabinet.ModeAudienceCabinet : date passee sans heure, ou date+heure atteinte) ; le dernier appel
    // passe et le prochain (PROC_MJUD.cs:208-222).
    private static async Task<SectionFiche> AppelsAsync(SqlConnection cn, int id, DateTime maintenant, CancellationToken ct)
    {
        var appels = await LignesAsync(cn, """
            SELECT a.AUDNC_DATE, a.AUDNC_HEURE
            FROM v2.APPEL_AFFAIRE AS aa JOIN v2.AUDIENCE_CABINET AS a ON a.AUDNC_ID_ADNC = aa.APPAF_ID_ADNC
            WHERE aa.APPAF_ID_INSTN = @id AND a.AUDNC_DATE IS NOT NULL
            ORDER BY a.AUDNC_DATE, a.AUDNC_HEURE, aa.APPAF_NUM_ORDRE
            """, id, r =>
        {
            var date = FormeLigne.DateDe(r, "AUDNC_DATE")!.Value;
            var heure = FormeLigne.Texte(r, "AUDNC_HEURE");
            var passe = heure.Length == 5 && TimeSpan.TryParse(heure + ":00", CultureInfo.InvariantCulture, out var h)
                ? date.Date.Add(h) <= maintenant
                : heure.Length == 0 && date.Date < maintenant.Date;
            // fix-ok: mesure sur le bureau cache (26/11/2026) -- les cellules des sous-listes (appels, decisions, evenements)
            // s'affichaient « 2026-07-29 » : une cellule se lit comme l'agent l'ecrit (31/12/2026) ; seul un champ date
            // (Dernier appel, Prochain appel) garde le format d'une fiche, que l'ecran met en jj/mm/aaaa.
            return [FormeLigne.Date(date), heure, passe ? "passé" : "futur", FormeLigne.DateFiche(date)];
        }, ct);
        return new SectionFiche(SectionAppels,
        [
            new("Dernier appel", appels.LastOrDefault(a => a[2] == "passé")?[3] ?? ""),
            new("Prochain appel", appels.FirstOrDefault(a => a[2] == "futur")?[3] ?? ""),
            new("Liste des appels", "") { Colonnes = ["Date d'audience", "Heure", "Appel"], Lignes = [.. appels.Select(a => a[..3])] },
        ]);
    }

    // DECISIONS : la date rendue, sinon prevue, et le type avec son libelle (FormRechercheInstance.cs:336-337), par ordre.
    private static async Task<SectionFiche> DecisionsAsync(SqlConnection cn, int id, CancellationToken ct)
        => new(SectionDecision,
        [
            new("Décisions judiciaires", "")
            {
                Colonnes = ["Date", "Type de décision"],
                Lignes = await LignesAsync(cn, """
                    SELECT ISNULL(d.DCJDC_DATE, d.DCJDC_DATE_PREVUE) AS Quand, d.DCJDC_TYPE_DECISION, t.R_TYPEDECISI_LIBELLE_ECRAN
                    FROM v2.DECISION_JUDICIAIRE AS d LEFT JOIN v2.TYPE_DECISION AS t ON t.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
                    WHERE d.DCJDC_ID_INSTN = @id ORDER BY d.DCJDC_NUM_ORDRE, d.DCJDC_ID_DCJDC
                    """, id, r => [FormeLigne.Date(FormeLigne.DateDe(r, "Quand")),
                                   FormeLigne.Joindre(FormeLigne.Texte(r, "DCJDC_TYPE_DECISION"), FormeLigne.Texte(r, "R_TYPEDECISI_LIBELLE_ECRAN"))], ct),
            },
        ]);

    // EVENEMENTS : survenus (EVNPR_SURVENU = 1) ; futurs ET expires ensemble (survenu 0, PROC_MJUD.cs:259-260), par
    // EVNPR_NUM_ORDRE (Instance.cs:513-538) ; la date, l'evenement et son libelle (CODE_EVE_PRO).
    private static async Task<SectionFiche> EvenementsAsync(SqlConnection cn, int id, CancellationToken ct)
    {
        var tous = await LignesAsync(cn, """
            SELECT e.EVNPR_SURVENU, e.EVNPR_DELAI_EXPIRE, e.EVNPR_DATE, e.EVNPR_CODE_EVE_PRO, c.R_CODEEVEPRO_LIBELLE_ECRAN
            FROM v2.EVENEMENT_PROCEDURE AS e LEFT JOIN v2.CODE_EVE_PRO AS c ON c.R_CODEEVEPRO_VALEUR = e.EVNPR_CODE_EVE_PRO
            WHERE e.EVNPR_ID_INSTN = @id ORDER BY e.EVNPR_NUM_ORDRE, e.EVNPR_ID_EVNPR
            """, id, r => [r["EVNPR_SURVENU"] is true ? "1" : "0", r["EVNPR_DELAI_EXPIRE"] is true ? "expiré" : "",
                           FormeLigne.Date(FormeLigne.DateDe(r, "EVNPR_DATE")),
                           FormeLigne.Joindre(FormeLigne.Texte(r, "EVNPR_CODE_EVE_PRO"), FormeLigne.Texte(r, "R_CODEEVEPRO_LIBELLE_ECRAN"))], ct);
        return new SectionFiche(SectionEvenements,
        [
            new("Événements survenus", "") { Colonnes = ["Date", "Événement"], Lignes = [.. tous.Where(e => e[0] == "1").Select(e => new[] { e[2], e[3] })] },
            new("Événements futurs", "") { Colonnes = ["Date", "Événement", "Délai"], Lignes = [.. tous.Where(e => e[0] == "0").Select(e => new[] { e[2], e[3], e[1] })] },
        ]);
    }
}
