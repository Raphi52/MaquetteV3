using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Procédure collective » (PROC_MPC, dans l'ecran reuni « Corriger une affaire judiciaire ») : les procedures collectives et
/// les preventions du greffe, une recherche par numero, instance liee et debiteur, et la FICHE (en lecture) d'une PC ou d'une
/// prevention. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 3, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_MPC (D:\RigApplication\Source\RIG\DLL\Processus\PROC_MPC, fiche proc_mpc.md) modifie une PC
/// (mode MPC) ou une prevention (mode MODPR, FORM_MODPR).
///
/// FILTRE DE LA LISTE : ce que la recherche de MPC propose, ses deux modes reunis : « Instance=*;ProcedureCollective=* »
/// (PROC_MPC.Designer.cs:453) et « Instance=JRPR, JRMC;Prevention=* » (PROC_MPC.cs:74) -- toutes les PC et toutes les
/// preventions (une instance choisie mene a sa PC ou a sa prevention, PROC_MPC.cs:145-151 : la ligne est la PC ou la
/// prevention). ORDRE : PC puis preventions, numero decroissant.
///
/// GROUPES (descripteur) : « Procédure collective (PC) » / « Prévention (PRV) ».
///
/// REGLE D'ETAT :
///   - « Verrouillée » (EnAttente) : une autre demande tient la PC (sequenceur 2P, ProcedureCollective.cs:23) ou la prevention
///     (2R, Prevention.cs:23) -- le verrou que MPC pose a la selection (PROC_MPC.cs:141-151), lu par VerrouDemandeV2 ;
///   - « Modifiée » (Validee) : une demande MPC ou MODPR validee (V) sur la PC ou la prevention (DOSSIER_DEMANDE 2P / 2R), ou
///     un geste « a modifie » de cet ecran au journal des gestes de la V3 (soc.Journal, IdObjet = l'Id de la ligne) ;
///   - « À modifier » (AFaire) sinon. « En cours » : la demande en cours EST le verrou. « Notification refusée » (une
///     notification dematerialisee refusee, PROC_MPC.cs DoValider) n'a pas de ligne : la V3 ne lit pas DEMAT_NOTIF ici.
///
/// RECHERCHE (IRechercheEcran) : « Numéro de PC ou de prévention » (PRCLL_NUM_PC / PREVE_NUM_PRV), « Instance liée » (le numero
/// de role d'une instance rattachee, INSTN_ID_PRCLL / INSTN_ID_PREVE), « Débiteur » (le nom de l'affaire, PRCLL_NOM_AFFAIRE /
/// PREVE_NOM_AFFAIRE) ; le champ de recherche cherche le numero OU le nom.
///
/// FICHE (IFicheEcran) : Entité judiciaire (numero, instance liee) et Procédure (dates cles, date d'ouverture, date de cessation
/// des paiements, mode). Lecture seule, motif a l'appui : tenue par une autre demande (RIG v2 n'entre pas en saisie,
/// PROC_MPC.cs:141-151 ; message OPE_SELECT_ENTITE_JUDICIAIRE.cs:122), ou PC dont la modification est FACTUREE (ProcedureLue).
///
/// GESTES : EcrivainProceduresCollectives.cs, pour ce que RIG v2 modifie SANS FACTURE. fix-ok: mesure le 26/11/2026 -- le
/// premier jet disait toute modification de PC facturee ; PROC_MPC.cs:482-503 ne facture qu'une PC hors forfait dont le reglage
/// MPC_TYPE_FACTURATION_* vaut F ou T, jamais une PC au forfait ni une prevention (RIG_DEV : 10 272 PC au forfait sur 28 423,
/// 2 338 preventions, les deux reglages a « F »).
/// </summary>
internal sealed class LecteurProceduresCollectives : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_MPC";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Pc = "Procédure collective (PC)", Prv = "Prévention (PRV)";

    /// <summary>Les criteres du descripteur cherches dans la base.</summary>
    internal const string CritereNumero = "Numéro de PC ou de prévention", CritereInstance = "Instance liée", CritereDebiteur = "Débiteur";

    /// <summary>Les sections et champs de la fiche, aux titres et libelles du descripteur (ModifierAffaire.cs).</summary>
    internal const string SectionEntite = "Entité judiciaire", SectionProcedure = "Procédure";

    internal const string ChampNumero = "Numéro de PC", ChampInstance = "Instance liée", ChampDates = "Dates clés", ChampMode = "Mode (PC ou prévention)",
                          ChampOuverture = "Date d'ouverture", ChampCessation = "Date de cessation des paiements";

    /// <summary>Les codes sequenceurs de RIG v2 (ProcedureCollective.cs:23, Prevention.cs:23).</summary>
    internal const string SequenceurPc = "2P", SequenceurPrevention = "2R";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereNumero, CritereInstance, CritereDebiteur];

    // Le type en clair : le libelle ecran de TYPE_PC (ProcedureCollective.cs:2608-2612, TYPE_PC.GetTYPE_PC) ou de TYPE_PRV ; le
    // code si la table n'a pas la valeur (RIG_DEV en a d'anciennes : « 4 », « 11 »...).
    private const string Affaires = """
        SELECT 'P' AS Lettre, '2P' AS Sequenceur, p.PRCLL_ID_PRCLL AS Id, p.PRCLL_NUM_PC AS Numero, p.PRCLL_NOM_AFFAIRE AS Nom,
               COALESCE(tpc.R_TYPEPC_LIBELLE_ECRAN, p.PRCLL_TYPE_PC) AS Type, p.PRCLL_DATE_OUVERTURE AS Ouverture
        FROM v2.PROCEDURE_COLLECTIVE AS p
        LEFT JOIN v2.TYPE_PC AS tpc ON tpc.R_TYPEPC_VALEUR = p.PRCLL_TYPE_PC
        UNION ALL
        SELECT 'R', '2R', r.PREVE_ID_PREVE, r.PREVE_NUM_PRV, r.PREVE_NOM_AFFAIRE,
               COALESCE(tpr.R_TYPEPRV_LIBELLE_ECRAN, r.PREVE_TYPE_PRV), r.PREVE_DATE_OUVERTURE
        FROM v2.PREVENTION AS r
        LEFT JOIN v2.TYPE_PRV AS tpr ON tpr.R_TYPEPRV_VALEUR = r.PREVE_TYPE_PRV
        """;

    private const string Requete = $"""
        SELECT TOP (@plafond) a.Lettre, a.Id, a.Numero, a.Nom, a.Type, a.Ouverture, v.DMND_NUM_DEMANDE AS Verrou,
               CASE WHEN EXISTS (SELECT 1 FROM v2.DEMANDE AS d JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = d.DMND_ID_DMND
                                 WHERE dd.DSSDM_CODE_SEQUENCEUR = a.Sequenceur AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(100), a.Id)
                                   AND d.DMND_CODE_PROSS IN ('MPC', 'MODPR') AND d.DMND_ETAT_DEMANDE = 'V')
                      OR EXISTS (SELECT 1 FROM soc.Journal AS j
                                 WHERE j.Ecran = @ecran AND j.Verbe = 'a modifie' AND j.IdObjet = a.Lettre + '-' + CONVERT(varchar(40), a.Id))
                    THEN 1 ELSE 0 END AS Modifiee,
               COUNT(*) OVER () AS Total
        FROM ({Affaires}) AS a
        OUTER APPLY (SELECT TOP (1) d.DMND_NUM_DEMANDE FROM v2.DEMANDE AS d
                     JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = d.DMND_ID_DMND
                     WHERE dd.DSSDM_CODE_SEQUENCEUR = a.Sequenceur AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(100), a.Id)
                       AND (d.DMND_EN_COURS = 1 OR d.DMND_ETAT_DEMANDE IN ('W', 'I', 'N'))
                     ORDER BY d.DMND_ID_DMND) AS v
        WHERE 1 = 1 /*recherche*/
        ORDER BY a.Lettre, a.Numero DESC, a.Id DESC
        """;

    // Une instance rattachee a la PC ou a la prevention a (INSTN_ID_PRCLL / INSTN_ID_PREVE).
    private const string InstanceLiee = """
        EXISTS (SELECT 1 FROM v2.INSTANCE AS il WHERE il.INSTN_NUM_ROLE LIKE @instance
                AND ((a.Lettre = 'P' AND il.INSTN_ID_PRCLL = a.Id) OR (a.Lettre = 'R' AND il.INSTN_ID_PREVE = a.Id)))
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", ""), Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@ecran", EcrivainProceduresCollectives.NomEcran));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond), new("@ecran", EcrivainProceduresCollectives.NomEcran) };
        var filtres = new List<string>();
        string Numero(string v) => FormeLigne.Contient(v.Replace(" ", "", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add("(a.Numero LIKE @texteNumero OR a.Nom LIKE @texteNom)");
            parametres.Add(new SqlParameter("@texteNumero", Numero(recherche.Texte)));
            parametres.Add(new SqlParameter("@texteNom", FormeLigne.Contient(recherche.Texte)));
        }
        if (recherche.Critere(CritereNumero) is { } numero)
        {
            filtres.Add("a.Numero LIKE @numero");
            parametres.Add(new SqlParameter("@numero", Numero(numero)));
        }
        if (recherche.Critere(CritereInstance) is { } instance)
        {
            filtres.Add(InstanceLiee);
            parametres.Add(new SqlParameter("@instance", Numero(instance)));
        }
        if (recherche.Critere(CritereDebiteur) is { } debiteur)
        {
            filtres.Add("a.Nom LIKE @debiteur");
            parametres.Add(new SqlParameter("@debiteur", FormeLigne.Contient(debiteur)));
        }
        var sql = Requete.Replace("/*recherche*/", filtres.Count == 0 ? "" : " AND " + string.Join(" AND ", filtres));
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var pc = FormeLigne.Texte(r, "Lettre") == "P";
        var numero = FormeLigne.Texte(r, "Numero");
        var verrou = FormeLigne.Texte(r, "Verrou");
        var modifiee = Convert.ToInt32(r["Modifiee"], CultureInfo.InvariantCulture) == 1;
        var ouverture = FormeLigne.DateDe(r, "Ouverture");
        var precisions = FormeLigne.Joindre(
            numero.Length > 0 ? $"{(pc ? "PC" : "Prévention")} {numero}" : null, FormeLigne.Texte(r, "Type"),
            ouverture is null ? null : $"ouverte le {FormeLigne.Date(ouverture)}",
            verrou.Length > 0 ? $"bloquée par la demande {verrou}" : null);
        var nom = FormeLigne.Texte(r, "Nom") is { Length: > 0 } n ? n : $"{(pc ? "PC" : "Prévention")} n° {(numero.Length > 0 ? numero : FormeLigne.Texte(r, "Id"))}";
        return new LigneLue(nom, precisions, pc ? Pc : Prv, verrou.Length > 0 ? "EnAttente" : modifiee ? "Validee" : "AFaire", [])
        {
            Id = $"{(pc ? 'P' : 'R')}-{FormeLigne.Texte(r, "Id")}",
        };
    }

    // ------------------------------------------------------------------ LA FICHE

    /// <summary>
    /// La PC ou la prevention, telle que la fiche et l'ecrivain la lisent : ses dates, ce qui decide de la facturation de sa
    /// modification (PROC_MPC.cs:482-503), ce que les controles de RIG v2 lisent (OPE_PROCEDURE_COLLECTIVE.cs:867-872,
    /// OPE_PREVENTION.cs:410-417) et ce que la sauvegarde d'une PC recalcule (ProcedureCollective.cs:2950-2981).
    /// </summary>
    internal sealed record ProcedureLue(bool Pc, int Id, string Numero, string Nom, string Instance, DateTime? Creation,
                                        DateTime? Ouverture, DateTime? Cessation, DateTime? Solution, DateTime? Cloture)
    {
        /// <summary>« la PC 2026P00012 » / « la prévention 2026V00007 », pour les messages et le journal.</summary>
        public string EnMots => $"{(Pc ? "la PC" : "la prévention")} {(Numero.Length > 0 ? Numero : $"n° {Id}")}";

        public bool AuForfait { get; init; }
        public bool ClientEnCompte { get; init; }
        public string? Facturation { get; init; }
        public bool AvecAdresse { get; init; }
        public bool AvecEntreprise { get; init; }

        // Ce que ProcedureCollective.Sauve recalcule (PC seulement) : null = colonne NULL (le isNull de RIG v2).
        public string? EtatPc { get; init; }
        public string? TypePc { get; init; }
        public string? CodeCloture { get; init; }
        public string? CodeSolution { get; init; }
        public string? CodeSolution2 { get; init; }
        public DateTime? RetablissementProfessionnel { get; init; }
        public DateTime? Sauvegarde { get; init; }
        public DateTime? SortieDeCrise { get; init; }

        /// <summary>Le reglage qui decide de la facturation d'une PC hors forfait, selon son client (PROC_MPC.cs:485-491).</summary>
        public string ReglageFacturation => ClientEnCompte ? "MPC_TYPE_FACTURATION_CLIENT_EN_COMPTE" : "MPC_TYPE_FACTURATION_CLIENT_DIVERS";

        /// <summary>
        /// Pourquoi la V3 ne la modifie pas : sa modification est FACTUREE dans RIG v2 -- une PC hors forfait dont le reglage
        /// vaut « F » ou « T » (PROC_MPC.cs:482-500) ; une PC au forfait (:483-484) et une prevention (:501-502) ne le sont
        /// jamais. Null : pas facturee. Sans point final, comme le message de verrou : la fiche ajoute le sien
        /// (FormeFiche.tsx:81, « Fiche en lecture seule : {motif}. »).
        /// </summary>
        public string? MotifFacturee => Pc && !AuForfait && Facturation is "F" or "T"
            ? $"La modification de {EnMots} est facturée dans RIG v2 (PC hors forfait, réglage {ReglageFacturation} = « {Facturation} ») : " +
              "la V3 ne reprend pas la facturation, elle ne se modifie donc pas ici"
            : null;

        /// <summary>Le champ de la section Procedure que RIG v2 laisse modifier sans facture ni forfait recalcule : la date
        /// de cessation des paiements d'une PC (OPE_PROCEDURE_COLLECTIVE.cs:202), la date d'ouverture d'une prevention
        /// (OPE_PREVENTION.cs:86).</summary>
        public string ChampModifiable => Pc ? ChampCessation : ChampOuverture;
    }

    // Lu sur la PC : son client (rec.Client, reprise REC-013 de CLIENT) et le reglage du greffe (soc.Reglage, reprise SOC-003 de
    // PARAMETRAGE) qui decident de la facturation, comme PROC_MPC.cs:482-500.
    private const string FichePc = """
        SELECT p.PRCLL_NUM_PC AS Numero, p.PRCLL_NOM_AFFAIRE AS Nom, p.PRCLL_DATE_CREATION AS Creation, p.PRCLL_DATE_OUVERTURE AS Ouverture,
               p.PRCLL_DATE_CESS_PAIEMENT AS Cessation, p.PRCLL_DATE_SOLUTION AS Solution, p.PRCLL_DATE_CLOTURE AS Cloture,
               p.PRCLL_FORFAIT_FACTURE AS Forfait, c.Type AS TypeClient,
               (SELECT rg.Valeur FROM soc.Reglage AS rg
                WHERE rg.Code = CASE WHEN c.Type = 'C' THEN 'MPC_TYPE_FACTURATION_CLIENT_EN_COMPTE' ELSE 'MPC_TYPE_FACTURATION_CLIENT_DIVERS' END) AS Facturation,
               CASE WHEN p.PRCLL_ID_ADRBS IS NULL THEN 0 ELSE 1 END AS AvecAdresse,
               CASE WHEN p.PRCLL_ID_RPRTR IS NULL THEN 0 ELSE 1 END AS AvecEntreprise,
               p.PRCLL_ETAT_PC AS EtatPc, p.PRCLL_TYPE_PC AS TypePc, p.PRCLL_CODE_CLOTURE AS CodeCloture,
               p.PRCLL_CODE_SOLUTION AS CodeSolution, p.PRCLL_CODE_SOLUTION_2 AS CodeSolution2,
               p.PRCLL_DATE_RETABL_PROF AS Retablissement, p.PRCLL_DATE_SAUVEGARDE AS Sauvegarde,
               p.PRCLL_DATE_TRAITEMENT_SORTIE_CRISE AS SortieDeCrise,
               (SELECT TOP (1) il.INSTN_NUM_ROLE FROM v2.INSTANCE AS il WHERE il.INSTN_ID_PRCLL = p.PRCLL_ID_PRCLL ORDER BY il.INSTN_ID_INSTN) AS Instance
        FROM v2.PROCEDURE_COLLECTIVE AS p /*verrou*/
        LEFT JOIN rec.Client AS c ON c.Id = p.PRCLL_ID_CLIEN
        WHERE p.PRCLL_ID_PRCLL = @id
        """;

    private const string FichePrevention = """
        SELECT r.PREVE_NUM_PRV AS Numero, r.PREVE_NOM_AFFAIRE AS Nom, r.PREVE_DATE_CREATION AS Creation, r.PREVE_DATE_OUVERTURE AS Ouverture,
               r.PREVE_DATE_CESS_PAIEMENT AS Cessation, r.PREVE_DATE_SOLUTION AS Solution, r.PREVE_DATE_CLOTURE AS Cloture,
               CASE WHEN r.PREVE_ID_ADRBS IS NULL THEN 0 ELSE 1 END AS AvecAdresse,
               CASE WHEN r.PREVE_ID_DSRCS IS NULL AND r.PREVE_ID_NINSC IS NULL THEN 0 ELSE 1 END AS AvecEntreprise,
               (SELECT TOP (1) il.INSTN_NUM_ROLE FROM v2.INSTANCE AS il WHERE il.INSTN_ID_PREVE = r.PREVE_ID_PREVE ORDER BY il.INSTN_ID_INSTN) AS Instance
        FROM v2.PREVENTION AS r /*verrou*/
        WHERE r.PREVE_ID_PREVE = @id
        """;

    /// <summary>La PC ou la prevention <paramref name="id"/> (« P-12 », « R-7 », comme LigneLue.Id), lue sur <paramref name="cn"/>
    /// (dans la transaction d'un geste si <paramref name="tx"/>, verrouillee en ecriture jusqu'a sa fin) ; null si absente.</summary>
    internal static async Task<ProcedureLue?> LireProcedureAsync(SqlConnection cn, SqlTransaction? tx, string id, CancellationToken ct)
    {
        if (id.Length < 3 || id[1] != '-' || id[0] is not ('P' or 'R')
            || !int.TryParse(id[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        var pc = id[0] == 'P';
        var sql = (pc ? FichePc : FichePrevention).Replace("/*verrou*/", tx is null ? "" : "WITH (UPDLOCK, HOLDLOCK)");
        LectureV2.ExigerLecture(sql);
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@id", n);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        string? Code(string colonne) => r[colonne] is DBNull ? null : Convert.ToString(r[colonne], CultureInfo.InvariantCulture);
        var lue = new ProcedureLue(pc, n, FormeLigne.Texte(r, "Numero"), FormeLigne.Texte(r, "Nom"), FormeLigne.Texte(r, "Instance"),
            FormeLigne.DateDe(r, "Creation"), FormeLigne.DateDe(r, "Ouverture"), FormeLigne.DateDe(r, "Cessation"),
            FormeLigne.DateDe(r, "Solution"), FormeLigne.DateDe(r, "Cloture"))
        {
            AvecAdresse = Convert.ToInt32(r["AvecAdresse"], CultureInfo.InvariantCulture) == 1,
            AvecEntreprise = Convert.ToInt32(r["AvecEntreprise"], CultureInfo.InvariantCulture) == 1,
        };
        return !pc ? lue : lue with
        {
            AuForfait = r["Forfait"] is true,
            ClientEnCompte = FormeLigne.Texte(r, "TypeClient") == "C",
            Facturation = Code("Facturation"),
            EtatPc = Code("EtatPc"), TypePc = Code("TypePc"),
            CodeCloture = Code("CodeCloture"), CodeSolution = Code("CodeSolution"), CodeSolution2 = Code("CodeSolution2"),
            RetablissementProfessionnel = FormeLigne.DateDe(r, "Retablissement"), Sauvegarde = FormeLigne.DateDe(r, "Sauvegarde"),
            SortieDeCrise = FormeLigne.DateDe(r, "SortieDeCrise"),
        };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (await LireProcedureAsync(baseGreffe, null, id, ct) is not { } p) return null;
        // Tenue par une autre demande (PROC_MPC.cs:141-151), puis facturee (PROC_MPC.cs:482-500) : lecture seule, avec le motif.
        var motif = await VerrouDemandeV2.MotifAsync(baseGreffe, null, p.Pc ? "Procédure collective" : "Prévention",
                                                     p.Pc ? SequenceurPc : SequenceurPrevention, p.Id.ToString(CultureInfo.InvariantCulture), ct)
                    ?? p.MotifFacturee;
        ChampFiche Date(string libelle, DateTime? valeur)
            => new(libelle, FormeLigne.DateFiche(valeur)) { Modifiable = motif is null && libelle == p.ChampModifiable };
        // La date d'ouverture et celle de cessation ont leur champ ; le tableau garde les autres dates cles.
        string[][] dates = [["Création", FormeLigne.Date(p.Creation)], ["Solution", FormeLigne.Date(p.Solution)], ["Clôture", FormeLigne.Date(p.Cloture)]];
        return new FicheEcran(Procedure, id, p.Nom.Length > 0 ? p.Nom : $"{(p.Pc ? "PC" : "Prévention")} n° {p.Numero}",
        [
            new SectionFiche(SectionEntite, [new(ChampNumero, p.Numero), new(ChampInstance, p.Instance)]),
            new SectionFiche(SectionProcedure,
            [
                new(ChampDates, "") { Colonnes = ["Date", "Valeur"], Lignes = dates },
                Date(ChampOuverture, p.Ouverture), Date(ChampCessation, p.Cessation),
                new(ChampMode, p.Pc ? "PC" : "Prévention"),
            ]),
        ])
        {
            LectureSeule = motif is not null,
            Motif = motif,
        };
    }
}
