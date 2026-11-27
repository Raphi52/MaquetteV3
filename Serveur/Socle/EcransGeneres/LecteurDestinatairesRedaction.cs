using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Destinataires » (PROC_DEST_REDA) : les destinataires des editions d'une decision redigee (parties, mandataires, destinataires
/// divers) et l'edition que chacun recoit, avec la date de delivrance des grosses. Mode d'emploi : LecteurEcran.cs ; le geste :
/// EcrivainDestinatairesRedaction.cs. Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_DEST_REDA\ (proc_dest_reda.md, Detail OPE, qui fait foi). RIG v2 ouvre UNE
/// decision (« idDecisonJudiciaire », PROC_DEST_REDA.cs:24-27) depuis l'impression legale (OPE_IMPLEG.cs:238-253) ou la redaction
/// (OPE_DESTINATAIRES) ; il lui faut son document REDA (ETP_DEST_REDA.GetDocumentReda, :60-64 : sans document, l'ecran echoue) --
/// DOCUMENT_DOSSIER de code entite REDA_DECI_JUD. Les destinataires (DestinataireREDA.cs:730-822) et la colonne qui porte leur
/// edition (EditionJugement, :176-215) :
///   - les parties de l'instance : PARTIE.PRT_EDITION_JUGEMENT ;
///   - leur mandataire postulant et plaidant : PRT_MANDATAIRE_EDITION_JUGEMENT, PRT_MANDATAIRE_PLAI_EDITION_JUGEMENT (sur la PARTIE) ;
///   - les destinataires divers de la decision : DESTINATAIRE_DIVERS.DTDIV_EDITION_JUGEMENT.
/// Le greffe et le document interne ne sont jamais enregistres depuis cet ecran (_EnregistrerDestinataires, :428-429) : ils ne sont
/// pas listes. L'edition : G grosse, C copie, E expedition, L lettre de notification (dans cet ordre), X aucune, D « par defaut »
/// (calculee par RIG v2 d'apres le mode de presentation de la partie et le type de decision, RefreshEditions, :403-517).
///
/// LA LISTE : les destinataires des decisions qui ont un document REDA, la plus recente en tete. REGLE D'ETAT -- la date de
/// delivrance des grosses (DOCUMENT_DOSSIER.DCMDS_DATE_IMPRESSION_MINUTE) est « obligatoire pour continuer » (OPE_DEST_REDA.cs:244-
/// 251 ; sans elle, RIG v2 verrouille les autres operations, :399) :
///   - AFaire « Date de délivrance à saisir » : le document n'a pas cette date ;
///   - EnCours « Délivrance datée » : il l'a. RIG v2 laisse changer l'edition a tout moment : les deux restent modifiables.
/// Id = « P-&lt;partie&gt;-&lt;decision&gt; », « M-... » (mandataire postulant), « L-... » (plaidant), « D-&lt;destinataire divers&gt; ».
///
/// NON REPRIS (nomme) : le calcul de l'edition « par defaut » (D, RefreshEditions) : la V3 ecrit « D » tel quel et l'affiche ; les
/// destinataires institutionnels et le greffe ; l'ajout et la suppression d'un destinataire divers (FormAjouterDestinataire, menu
/// « Supprimer ») : la forme Fiche n'a ni Ajouter ni Supprimer.
/// </summary>
internal sealed class LecteurDestinatairesRedaction : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_DEST_REDA";

    /// <summary>Les groupes du descripteur (ModifierEnvois.cs), au mot pres.</summary>
    internal const string Parties = "Parties", Mandataires = "Mandataires", Divers = "Destinataires divers";

    /// <summary>Les sections et champs du descripteur, au mot pres.</summary>
    internal const string SectionEdition = "Édition du jugement", ChampEdition = "Édition", ChampGrosse = "Grosse", ChampCopie = "Copie",
                          ChampExpedition = "Expédition", ChampLettre = "Lettre de notification",
                          SectionDate = "Date de délivrance des grosses", ChampDate = "Date de délivrance des grosses",
                          SectionSupplements = "Suppléments", ChampGrosses = "Grosses supplémentaires", ChampCopies = "Copies supplémentaires",
                          ChampExpeditions = "Expéditions supplémentaires",
                          SectionDecision = "Décision", ChampRole = "Numéro de rôle", ChampType = "Type de décision", CritereRole = "Numéro de rôle";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereRole];

    // Les decisions REDA (le filtre se pose a la place de /*filtre*/, dans chacune des quatre branches). Pas de WITH : la porte de lecture
    // V2 n'admet qu'une requete qui commence par SELECT (LectureV2.ExigerLecture).
    private const string Decisions = """
        (SELECT d.DCJDC_ID_DCJDC, d.DCJDC_ID_INSTN, i.INSTN_NUM_ROLE, ISNULL(td.R_TYPEDECISI_LIBELLE_ECRAN, ISNULL(d.DCJDC_TYPE_DECISION, '')) AS TypeDecision,
                doc.DCMDS_ID_DCMDS, doc.DCMDS_DATE_IMPRESSION_MINUTE, doc.DCMDS_SUPPLEMENT_GROSSE, doc.DCMDS_SUPPLEMENT_COPIE, doc.DCMDS_SUPPLEMENT_EXPEDI
         FROM v2.DECISION_JUDICIAIRE AS d
         JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
         LEFT JOIN v2.TYPE_DECISION AS td ON td.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
         CROSS APPLY (SELECT TOP (1) x.DCMDS_ID_DCMDS, x.DCMDS_DATE_IMPRESSION_MINUTE, x.DCMDS_SUPPLEMENT_GROSSE, x.DCMDS_SUPPLEMENT_COPIE, x.DCMDS_SUPPLEMENT_EXPEDI
                      FROM v2.DOCUMENT_DOSSIER AS x WHERE x.DCMDS_VALEUR_ID_METIER = d.DCJDC_ID_DCJDC AND x.DCMDS_CODE_ENTITE = 'REDA_DECI_JUD'
                      ORDER BY x.DCMDS_ID_DCMDS DESC) AS doc
         WHERE 1 = 1 /*filtre*/)
        """;

    // Les destinataires : une ligne par partie, par mandataire (postulant, plaidant) et par destinataire divers des decisions REDA.
    private const string Destinataires = $$"""
        SELECT TOP (@plafond) dest.*, COUNT(*) OVER () AS Total FROM (
            SELECT dj.*, 0 AS Rang, 'P' AS Sorte, p.PRT_ID_PRT AS IdDestinataire, ISNULL(NULLIF(p.PRT_DESIGNATION, ''), p.PRT_NOM) AS Nom, p.PRT_EDITION_JUGEMENT AS Edition
            FROM {{Decisions}} AS dj JOIN v2.PARTIE AS p ON p.PRT_ID_INSTN = dj.DCJDC_ID_INSTN
            UNION ALL
            SELECT dj.*, 1, 'M', p.PRT_ID_PRT, m.MNDTR_NOM, p.PRT_MANDATAIRE_EDITION_JUGEMENT
            FROM {{Decisions}} AS dj JOIN v2.PARTIE AS p ON p.PRT_ID_INSTN = dj.DCJDC_ID_INSTN JOIN v2.MANDATAIRE AS m ON m.MNDTR_ID_MNDTR = p.PRT_ID_MNDTR_POS
            UNION ALL
            SELECT dj.*, 1, 'L', p.PRT_ID_PRT, m.MNDTR_NOM, p.PRT_MANDATAIRE_PLAI_EDITION_JUGEMENT
            FROM {{Decisions}} AS dj JOIN v2.PARTIE AS p ON p.PRT_ID_INSTN = dj.DCJDC_ID_INSTN JOIN v2.MANDATAIRE AS m ON m.MNDTR_ID_MNDTR = p.PRT_ID_MNDTR_PLAI
            UNION ALL
            SELECT dj.*, 2, 'D', dv.DTDIV_ID_DTDIV, dv.DTDIV_DESIGNATION, dv.DTDIV_EDITION_JUGEMENT
            FROM {{Decisions}} AS dj JOIN v2.DESTINATAIRE_DIVERS AS dv ON dv.DTDIV_ID_DCJDC = dj.DCJDC_ID_DCJDC
        ) AS dest /*id*/
        ORDER BY dest.DCJDC_ID_DCJDC DESC, dest.Rang, dest.Sorte, dest.IdDestinataire
        """;

    /// <summary>Un destinataire, avec le document REDA de sa decision.</summary>
    internal sealed record Destinataire(string Id, char Sorte, int IdDestinataire, int IdDecision, string Role, string TypeDecision, string Nom,
                                        string Edition, int IdDocument, DateTime? Delivrance, string SupplementGrosse, string SupplementCopie,
                                        string SupplementExpedition);

    public async Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => await ChercherAsync(baseGreffe, new RechercheEcran(null, new Dictionary<string, string>()), aujourdhui, plafond, ct);

    public async Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var filtres = new List<string>();
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add("(i.INSTN_NUM_ROLE LIKE @texte OR i.INSTN_NOM_COURANT LIKE @texte)");
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        if (recherche.Critere(CritereRole) is { } role)
        {
            filtres.Add("i.INSTN_NUM_ROLE LIKE @role");
            parametres.Add(new SqlParameter("@role", FormeLigne.Contient(role.Replace(" ", "", StringComparison.Ordinal))));
        }
        var sql = Destinataires.Replace("/*filtre*/", string.Concat(filtres.Select(f => " AND " + f)), StringComparison.Ordinal).Replace("/*id*/", "", StringComparison.Ordinal);
        var (lignes, total) = await LireDestinatairesAsync((s, p) => LectureV2.LireAsync(baseGreffe, s, ct, p), sql, parametres, ct);
        return new LignesLues([.. lignes.Select(Ligne)], total);
    }

    private static LigneLue Ligne(Destinataire d)
    {
        var precisions = FormeLigne.Joindre($"Rôle {d.Role}", d.TypeDecision,
            d.Sorte switch { 'M' => "mandataire postulant", 'L' => "mandataire plaidant", 'D' => "destinataire divers", _ => "partie" },
            LibelleEdition(d.Edition), d.Delivrance is { } dt ? $"grosses délivrées le {FormeLigne.Date(dt)}" : "date de délivrance à saisir");
        var groupe = d.Sorte switch { 'P' => Parties, 'D' => Divers, _ => Mandataires };
        return new LigneLue(d.Nom.Length > 0 ? d.Nom : d.Role, precisions, groupe, d.Delivrance is null ? "AFaire" : "EnCours", []) { Id = d.Id };
    }

    /// <summary>L'edition en mots (la grammaire G, C, E, L de RedaDestinatairesDataGridView, :295-303).</summary>
    internal static string LibelleEdition(string code) => code switch
    {
        "" or "X" => "aucune édition",
        "D" => "édition par défaut",
        _ => string.Join(" + ", code.Select(c => c switch { 'G' => "grosse", 'C' => "copie", 'E' => "expédition", 'L' => "lettre de notification", _ => c.ToString() })),
    };

    /// <summary>Le destinataire <paramref name="id"/> ; null s'il n'existe pas. <paramref name="lire"/> : le SELECT du lecteur ou du geste.</summary>
    internal static async Task<Destinataire?> DestinataireAsync(Func<string, SqlParameter[], Task<SqlDataReader>> lire, string id, CancellationToken ct)
    {
        var morceaux = id.Split('-');
        if (morceaux.Length < 2 || morceaux[0] is not ("P" or "M" or "L" or "D") || !int.TryParse(morceaux[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return null;
        int? dj = morceaux.Length > 2 && int.TryParse(morceaux[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : null;
        var filtre = morceaux[0] == "D" ? "" : dj is null ? null : " AND d.DCJDC_ID_DCJDC = @dj";
        if (filtre is null) return null;
        var parametres = new List<SqlParameter> { new("@plafond", 1), new("@sorte", morceaux[0]), new("@dest", n) };
        if (dj is { } x) parametres.Add(new SqlParameter("@dj", x));
        var sql = Destinataires.Replace("/*filtre*/", filtre, StringComparison.Ordinal)
                               .Replace("/*id*/", "WHERE dest.Sorte = @sorte AND dest.IdDestinataire = @dest", StringComparison.Ordinal);
        var (lignes, _) = await LireDestinatairesAsync(lire, sql, parametres, ct);
        return lignes.FirstOrDefault();
    }

    private static async Task<(List<Destinataire>, int)> LireDestinatairesAsync(Func<string, SqlParameter[], Task<SqlDataReader>> lire, string sql,
        List<SqlParameter> parametres, CancellationToken ct)
    {
        var lignes = new List<Destinataire>();
        var total = 0;
        await using var r = await lire(sql, [.. parametres]);
        while (await r.ReadAsync(ct))
        {
            total = Convert.ToInt32(r["Total"], CultureInfo.InvariantCulture);
            var sorte = FormeLigne.Texte(r, "Sorte")[0];
            var idDest = Convert.ToInt32(r["IdDestinataire"], CultureInfo.InvariantCulture);
            var idDj = Convert.ToInt32(r["DCJDC_ID_DCJDC"], CultureInfo.InvariantCulture);
            lignes.Add(new Destinataire(sorte == 'D' ? $"D-{idDest}" : $"{sorte}-{idDest}-{idDj}", sorte, idDest, idDj, FormeLigne.Texte(r, "INSTN_NUM_ROLE"),
                FormeLigne.Texte(r, "TypeDecision"), FormeLigne.Texte(r, "Nom"), FormeLigne.Texte(r, "Edition"),
                Convert.ToInt32(r["DCMDS_ID_DCMDS"], CultureInfo.InvariantCulture), FormeLigne.DateDe(r, "DCMDS_DATE_IMPRESSION_MINUTE"),
                FormeLigne.Texte(r, "DCMDS_SUPPLEMENT_GROSSE"), FormeLigne.Texte(r, "DCMDS_SUPPLEMENT_COPIE"), FormeLigne.Texte(r, "DCMDS_SUPPLEMENT_EXPEDI")));
        }
        return (lignes, total);
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (await DestinataireAsync((s, p) => LectureV2.LireAsync(baseGreffe, s, ct, p), id, ct) is not { } d) return null;
        var code = d.Edition;
        return new FicheEcran(Procedure, id, d.Nom.Length > 0 ? d.Nom : d.Role,
        [
            new SectionFiche(SectionDecision, [new(ChampRole, d.Role), new(ChampType, d.TypeDecision)]),
            new SectionFiche(SectionEdition,
            [
                new(ChampEdition, code) { Modifiable = true },
                new(ChampGrosse, FormeLigne.Case(code.Contains('G'))) { Modifiable = true },
                new(ChampCopie, FormeLigne.Case(code.Contains('C'))) { Modifiable = true },
                new(ChampExpedition, FormeLigne.Case(code.Contains('E'))) { Modifiable = true },
                new(ChampLettre, FormeLigne.Case(code.Contains('L'))) { Modifiable = true },
            ]),
            new SectionFiche(SectionDate, [new(ChampDate, FormeLigne.DateFiche(d.Delivrance)) { Modifiable = true }]),
            new SectionFiche(SectionSupplements,
            [
                new(ChampGrosses, d.SupplementGrosse) { Modifiable = true },
                new(ChampCopies, d.SupplementCopie) { Modifiable = true },
                new(ChampExpeditions, d.SupplementExpedition) { Modifiable = true },
            ]),
        ]);
    }
}
