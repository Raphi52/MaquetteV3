using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Dossier judiciaire » (PROC_VJUD) : retrouver une instance, une procedure collective ou une prevention, et lire sa fiche --
/// informations, parties, decisions, evenements. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 6,
/// 26/11/2026. Lecture seule : toutes les OPE de la V2 sont des variantes « _READONLY » (proc_vjud.md:109).
///
/// SOURCE (ancien RIG) : PROC_VJUD (le Detail OPE de proc_vjud.md fait foi). Une MetierBase polymorphe -- Instance,
/// ProcedureCollective ou Prevention (PROC_VJUD.cs:17-19) --, passee en parametre (TypeMetierBaseVisu, IdMetierBaseVisu) ou
/// retrouvee par l'etape de recherche d'entite judiciaire (ETP_ENTITE_JUD_RECHERCHE, proc_vjud.md:111-119). Les criteres de la
/// V3 sont ceux de cette recherche : « Type d'entité », « Numéro » (numero de role INSTN_NUM_ROLE, de PC PRCLL_NUM_PC, de
/// prevention PREVE_NUM_PRV, blancs retires) et « Nom » (INSTN_NOM_COURANT, PRCLL_NOM_AFFAIRE, PREVE_NOM_AFFAIRE). Une instance
/// rattachee a une PC affiche aussi sa PC (la cascade de OnAutomateEvent, proc_vjud.md:121-131) : sa fiche nomme la PC.
///
/// LA LISTE A L'OUVERTURE : AUCUNE, comme RIG v2, qui ouvre sur la recherche d'entite judiciaire sans affaire passee en
/// parametre (proc_vjud.md:111-119). fix-ok: controle final du lot 6 (27/11/2026) -- une liste « affaires du jour » s'y
/// affichait, sans source V2 (regle du plan : sans source, ne pas l'ajouter) ; le banc l'exigeait (au moins une ligne a
/// l'ouverture), il lit desormais les lignes des recherches d'un ecran qui s'ouvre vide. La recherche porte sur toutes les affaires.
///
/// GROUPES : « Instance », « Procédure collective », « Prévention ».
/// REGLE D'ETAT (les evenements de l'affaire, EVENEMENT_PROCEDURE ; les etapes « Événements survenus / futurs » de la V2) :
///   aucun evenement                                   -> AFaire « Sans événement » ;
///   un evenement attendu (EVNPR_DATE vide)            -> EnCours « Événement à venir » ;
///   tous ses evenements survenus (EVNPR_DATE)         -> Validee « Événements survenus ».
///
/// FICHE (IFicheEcran), Id « I-12 », « P-7 » ou « R-3 » : « Informations » (l'OPE principale de chaque etape), « Parties »
/// (PARTIE de l'instance ; une PC ou une prevention n'en a pas dans cette table), « Audiences et décisions » (DECISION_JUDICIAIRE
/// de l'instance) et « Événements » (survenus, puis futurs). Non lus (nommes) : les appels, les dispositifs, le coffre-fort, les
/// notes et les debiteurs multiples (deja lus par « Débiteurs multiples », LecteurDebiteursMultiples).
/// </summary>
internal sealed class LecteurDossierJudiciaire : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_VJUD";

    internal static readonly string[] Groupes = ["Instance", "Procédure collective", "Prévention"];

    internal const string CritereType = "Type d'entité", CritereNumero = "Numéro", CritereNom = "Nom";

    internal const string SectionInformations = "Informations", SectionParties = "Parties", SectionDecisions = "Audiences et décisions",
                          SectionEvenements = "Événements";

    internal const string ChampNumero = "Numéro", ChampNom = "Nom de l'affaire", ChampCode = "Code affaire", ChampOuverture = "Date d'ouverture",
                          ChampActe = "Date de l'acte", ChampPc = "Procédure collective rattachée", ChampParties = "Parties de l'affaire",
                          ChampDecisions = "Décisions judiciaires", ChampSurvenus = "Événements survenus", ChampFuturs = "Événements futurs";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereType, CritereNumero, CritereNom];

    // Les evenements d'une affaire (la colonne visee : EVNPR_ID_INSTN, EVNPR_ID_PRCLL, EVNPR_ID_PREVE).
    private static string Evenements(string colonne, string id) => $"""
        (SELECT COUNT(*) FROM v2.EVENEMENT_PROCEDURE AS e WHERE e.{colonne} = {id}) AS NbEvenements,
        (SELECT COUNT(*) FROM v2.EVENEMENT_PROCEDURE AS e WHERE e.{colonne} = {id} AND e.EVNPR_DATE IS NULL) AS NbAVenir
        """;

    // Bloc : 0 instance, 1 PC, 2 prevention. /*i*/, /*p*/, /*r*/ : les filtres de chaque bloc.
    private static readonly string Requete = $"""
        SELECT TOP (@plafond) l.*, COUNT(*) OVER () AS Total
        FROM (SELECT 0 AS Bloc, i.INSTN_ID_INSTN AS Id, i.INSTN_NUM_ROLE AS Numero, i.INSTN_NOM_COURANT AS Nom,
                     i.INSTN_DATE_ENROLEMENT AS DateCle, i.INSTN_CODE_AFFAIRE AS Code, {Evenements("EVNPR_ID_INSTN", "i.INSTN_ID_INSTN")}
              FROM v2.INSTANCE AS i WHERE 1 = 1 /*i*/
              UNION ALL
              SELECT 1, p.PRCLL_ID_PRCLL, p.PRCLL_NUM_PC, p.PRCLL_NOM_AFFAIRE, p.PRCLL_DATE_OUVERTURE, NULL, {Evenements("EVNPR_ID_PRCLL", "p.PRCLL_ID_PRCLL")}
              FROM v2.PROCEDURE_COLLECTIVE AS p WHERE 1 = 1 /*p*/
              UNION ALL
              SELECT 2, r.PREVE_ID_PREVE, r.PREVE_NUM_PRV, r.PREVE_NOM_AFFAIRE, r.PREVE_DATE_OUVERTURE, NULL, {Evenements("EVNPR_ID_PREVE", "r.PREVE_ID_PREVE")}
              FROM v2.PREVENTION AS r WHERE 1 = 1 /*r*/) AS l
        ORDER BY l.Bloc, l.Numero DESC, l.Id DESC
        """;

    // Aucune liste a l'ouverture : RIG v2 ouvre sur la recherche (en tete du fichier).
    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Task.FromResult(new LignesLues([], 0));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var i = new List<string>();
        var p = new List<string>();
        var r = new List<string>();
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        void Ajouter(string clauseI, string clauseP, string clauseR, string nom, object valeur)
        {
            i.Add(clauseI); p.Add(clauseP); r.Add(clauseR);
            parametres.Add(new SqlParameter(nom, valeur));
        }
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            parametres.Add(new SqlParameter("@tl", FormeLigne.Contient(recherche.Texte)));
            Ajouter("(REPLACE(i.INSTN_NUM_ROLE, ' ', '') = @tn OR i.INSTN_NOM_COURANT LIKE @tl)",
                    "(REPLACE(p.PRCLL_NUM_PC, ' ', '') = @tn OR p.PRCLL_NOM_AFFAIRE LIKE @tl)",
                    "(REPLACE(r.PREVE_NUM_PRV, ' ', '') = @tn OR r.PREVE_NOM_AFFAIRE LIKE @tl)", "@tn", CriteresConsultation.Numero(recherche.Texte));
        }
        if (recherche.Critere(CritereNumero) is { } numero)
            Ajouter("REPLACE(i.INSTN_NUM_ROLE, ' ', '') = @numero", "REPLACE(p.PRCLL_NUM_PC, ' ', '') = @numero",
                    "REPLACE(r.PREVE_NUM_PRV, ' ', '') = @numero", "@numero", CriteresConsultation.Numero(numero));
        if (recherche.Critere(CritereNom) is { } nom)
            Ajouter("i.INSTN_NOM_COURANT LIKE @nom", "p.PRCLL_NOM_AFFAIRE LIKE @nom", "r.PREVE_NOM_AFFAIRE LIKE @nom", "@nom", FormeLigne.Contient(nom));
        if (recherche.Critere(CritereType) is { } type)
        {
            // Le type d'entite : un groupe de l'ecran (Instance, Procédure collective, Prévention), son debut suffit.
            var t = type.Trim();
            bool Vise(string groupe) => groupe.StartsWith(t, StringComparison.OrdinalIgnoreCase);
            if (!Vise(Groupes[0])) i.Add("1 = 0");
            if (!Vise(Groupes[1])) p.Add("1 = 0");
            if (!Vise(Groupes[2])) r.Add("1 = 0");
        }
        static string Et(List<string> c) => c.Count == 0 ? "" : " AND " + string.Join(" AND ", c);
        var sql = Requete.Replace("/*i*/", Et(i), StringComparison.Ordinal).Replace("/*p*/", Et(p), StringComparison.Ordinal)
                         .Replace("/*r*/", Et(r), StringComparison.Ordinal);
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    private static readonly char[] Lettres = ['I', 'P', 'R'];

    private static LigneLue Ligne(SqlDataReader r)
    {
        var bloc = System.Convert.ToInt32(r["Bloc"], System.Globalization.CultureInfo.InvariantCulture);
        var numero = FormeLigne.Texte(r, "Numero");
        var evenements = System.Convert.ToInt32(r["NbEvenements"], System.Globalization.CultureInfo.InvariantCulture);
        var aVenir = System.Convert.ToInt32(r["NbAVenir"], System.Globalization.CultureInfo.InvariantCulture);
        var date = FormeLigne.DateDe(r, "DateCle");
        var nom = FormeLigne.Joindre(
            bloc switch { 0 => $"Rôle {numero}", 1 => $"PC {numero}", _ => $"Prévention {numero}" },
            FormeLigne.Texte(r, "Nom"));
        var precisions = FormeLigne.Joindre(
            Groupes[bloc],
            FormeLigne.Texte(r, "Code") is { Length: > 0 } code ? $"code affaire {code}" : null,
            date is { } d ? (bloc == 0 ? $"enrôlée le {FormeLigne.Date(d)}" : $"ouverte le {FormeLigne.Date(d)}") : null,
            evenements == 0 ? "aucun événement" : aVenir > 0 ? $"{aVenir} événement{(aVenir > 1 ? "s" : "")} à venir" : $"{evenements} événement{(evenements > 1 ? "s" : "")} survenu{(evenements > 1 ? "s" : "")}");
        // La regle d'etat (en tete du fichier).
        var etat = evenements == 0 ? "AFaire" : aVenir > 0 ? "EnCours" : "Validee";
        return new LigneLue(nom, precisions, Groupes[bloc], etat, []) { Id = $"{Lettres[bloc]}-{FormeLigne.Texte(r, "Id")}" };
    }

    // ------------------------------------------------------------------ LA FICHE

    private const string FicheInstance = """
        SELECT i.INSTN_NUM_ROLE AS Numero, i.INSTN_NOM_COURANT AS Nom, i.INSTN_CODE_AFFAIRE AS Code, i.INSTN_DATE_ENROLEMENT AS Ouverture,
               i.INSTN_DATE_ACTE AS Acte, pc.PRCLL_NUM_PC AS Pc
        FROM v2.INSTANCE AS i
        LEFT JOIN v2.PROCEDURE_COLLECTIVE AS pc ON pc.PRCLL_ID_PRCLL = i.INSTN_ID_PRCLL
        WHERE i.INSTN_ID_INSTN = @id
        """;

    private const string FichePc = """
        SELECT p.PRCLL_NUM_PC AS Numero, p.PRCLL_NOM_AFFAIRE AS Nom, NULL AS Code, p.PRCLL_DATE_OUVERTURE AS Ouverture,
               p.PRCLL_DATE_ACTE AS Acte, NULL AS Pc
        FROM v2.PROCEDURE_COLLECTIVE AS p WHERE p.PRCLL_ID_PRCLL = @id
        """;

    private const string FichePrevention = """
        SELECT r.PREVE_NUM_PRV AS Numero, r.PREVE_NOM_AFFAIRE AS Nom, NULL AS Code, r.PREVE_DATE_OUVERTURE AS Ouverture,
               r.PREVE_DATE_ACTE AS Acte, NULL AS Pc
        FROM v2.PREVENTION AS r WHERE r.PREVE_ID_PREVE = @id
        """;

    private const string Parties = """
        SELECT p.PRT_QUALIF_PARTIE, COALESCE(NULLIF(p.PRT_DESIGNATION, ''), p.PRT_NOM) AS Designation
        FROM v2.PARTIE AS p WHERE p.PRT_ID_INSTN = @id ORDER BY p.PRT_QUALIF_PARTIE, p.PRT_ID_PRT
        """;

    private const string Decisions = """
        SELECT d.DCJDC_DATE, d.DCJDC_DATE_PREVUE, COALESCE(t.R_TYPEDECISI_LIBELLE_ECRAN, d.DCJDC_TYPE_DECISION) AS Type, d.DCJDC_NUM_REPERTOIRE
        FROM v2.DECISION_JUDICIAIRE AS d
        LEFT JOIN v2.TYPE_DECISION AS t ON t.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
        WHERE d.DCJDC_ID_INSTN = @id
        ORDER BY ISNULL(d.DCJDC_DATE, d.DCJDC_DATE_PREVUE) DESC, d.DCJDC_ID_DCJDC DESC
        """;

    // Survenus : EVNPR_DATE ; futurs : sans elle, a leurs dates mini et maxi.
    private const string Evenement = """
        SELECT COALESCE(c.R_CODEEVEPRO_LIBELLE_ECRAN, e.EVNPR_CODE_EVE_PRO) AS Libelle, e.EVNPR_DATE, e.EVNPR_DATE_MINI, e.EVNPR_DATE_MAXI
        FROM v2.EVENEMENT_PROCEDURE AS e
        LEFT JOIN v2.CODE_EVE_PRO AS c ON c.R_CODEEVEPRO_VALEUR = e.EVNPR_CODE_EVE_PRO
        WHERE e./*colonne*/ = @id AND e.EVNPR_DATE IS /*survenu*/ NULL
        ORDER BY ISNULL(e.EVNPR_DATE, e.EVNPR_DATE_MAXI) DESC, e.EVNPR_ID_EVNPR DESC
        """;

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (id.Length < 3 || id[1] != '-' || Array.IndexOf(Lettres, id[0]) is var bloc && bloc < 0
            || CriteresConsultation.Entier(id[2..]) is not { } n) return null;
        string nom;
        ChampFiche[] informations;
        await using (var r = await LectureV2.LireAsync(baseGreffe, bloc switch { 0 => FicheInstance, 1 => FichePc, _ => FichePrevention }, ct,
                         new SqlParameter("@id", n)))
        {
            if (!await r.ReadAsync(ct)) return null;
            var numero = FormeLigne.Texte(r, "Numero");
            nom = FormeLigne.Joindre(bloc switch { 0 => $"Rôle {numero}", 1 => $"PC {numero}", _ => $"Prévention {numero}" }, FormeLigne.Texte(r, "Nom"));
            informations =
            [
                new(ChampNumero, numero), new(ChampNom, FormeLigne.Texte(r, "Nom")), new(ChampCode, FormeLigne.Texte(r, "Code")),
                new(ChampOuverture, FormeLigne.DateFiche(FormeLigne.DateDe(r, "Ouverture"))),
                new(ChampActe, FormeLigne.DateFiche(FormeLigne.DateDe(r, "Acte"))), new(ChampPc, FormeLigne.Texte(r, "Pc")),
            ];
        }
        var colonne = bloc switch { 0 => "EVNPR_ID_INSTN", 1 => "EVNPR_ID_PRCLL", _ => "EVNPR_ID_PREVE" };
        string[] Evt(SqlDataReader e) =>
        [
            FormeLigne.Texte(e, "Libelle"), FormeLigne.Date(FormeLigne.DateDe(e, "EVNPR_DATE")),
            FormeLigne.Date(FormeLigne.DateDe(e, "EVNPR_DATE_MINI")), FormeLigne.Date(FormeLigne.DateDe(e, "EVNPR_DATE_MAXI")),
        ];
        var survenus = await CriteresConsultation.SousListeAsync(baseGreffe,
            Evenement.Replace("/*colonne*/", colonne, StringComparison.Ordinal).Replace("/*survenu*/", "NOT", StringComparison.Ordinal), Evt, ct,
            new SqlParameter("@id", n));
        var futurs = await CriteresConsultation.SousListeAsync(baseGreffe,
            Evenement.Replace("/*colonne*/", colonne, StringComparison.Ordinal).Replace("/*survenu*/", "", StringComparison.Ordinal), Evt, ct,
            new SqlParameter("@id", n));
        var sections = new List<SectionFiche> { new(SectionInformations, informations) };
        if (bloc == 0)
        {
            var parties = await CriteresConsultation.SousListeAsync(baseGreffe, Parties,
                p => [FormeLigne.Texte(p, "PRT_QUALIF_PARTIE"), FormeLigne.Texte(p, "Designation")], ct, new SqlParameter("@id", n));
            var decisions = await CriteresConsultation.SousListeAsync(baseGreffe, Decisions,
                d => [FormeLigne.Date(FormeLigne.DateDe(d, "DCJDC_DATE")), FormeLigne.Date(FormeLigne.DateDe(d, "DCJDC_DATE_PREVUE")),
                      FormeLigne.Texte(d, "Type"), FormeLigne.Texte(d, "DCJDC_NUM_REPERTOIRE")], ct, new SqlParameter("@id", n));
            sections.Add(new SectionFiche(SectionParties, [CriteresConsultation.Tableau(ChampParties, ["Qualité", "Partie"], parties)]));
            sections.Add(new SectionFiche(SectionDecisions,
                [CriteresConsultation.Tableau(ChampDecisions, ["Rendue le", "Prévue le", "Type", "N° de minute"], decisions)]));
        }
        string[] colonnesEvt = ["Événement", "Le", "Au plus tôt", "Au plus tard"];
        sections.Add(new SectionFiche(SectionEvenements,
            [CriteresConsultation.Tableau(ChampSurvenus, colonnesEvt, survenus), CriteresConsultation.Tableau(ChampFuturs, colonnesEvt, futurs)]));
        return new FicheEcran(Procedure, id, nom, [.. sections]);
    }
}
