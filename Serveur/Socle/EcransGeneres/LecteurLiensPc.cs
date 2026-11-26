using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Liens entre PC » (PROC_XLIEN, dans l'ecran reuni « Defaire une affaire ») : les instances a rattacher a une procedure
/// collective ou a une prevention, ou a en detacher. Mode d'emploi : LecteurEcran.cs ; le geste : EcrivainLiensPc.cs. Plan
/// « RIG v3 100 % fonctionnel », lot 4, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_XLIEN = DEUX processus jumeaux (le Detail OPE de proc_xlien.md fait foi) : PROC_XLIENPC (lien
/// vers une PC, colonne INSTN_ID_PRCLL) et PROC_XLIENPR (lien vers une prevention, INSTN_ID_PREVE). Chacun choisit UNE instance
/// par sa propre recherche :
///   - XLIENPC : « Instance=JROP, JRCT, JRPC, JRJC » (PROC_XLIENPC.Designer.cs:36) -> groupe « Vers une PC » ;
///   - XLIENPR : « Instance=JRPR,JRMC,JROP » (PROC_XLIENPR.Designer.cs:36) -> groupe « Vers une prévention ».
/// La recherche lit ces types comme CODE_AFFAIRE.R_CODEAFFAIR_TYPE_NUM_ROLE (RechercheInstance.cs:150-154, voir
/// LecteurInstanceJudiciaire.cs). Une instance JROP est dans les deux listes de RIG v2 : elle a une ligne dans chaque groupe
/// (Id « PC-n » et « PR-n »). ORDRE : « order by instn_num_role desc » (FormRechercheInstance.cs:633), PC puis preventions.
///
/// REGLE D'ETAT (une ligne gelee -- ni « À rattacher » ni « Rattachée » -- ne se coche pas) :
///   - « Verrouillée » (EnAttente) : une autre demande tient l'instance (sequenceur 2J, VerrouDemandeV2) -- RIG v2 reste en
///     entree, sans saisie (PROC_XLIENPC.cs:51-57) ;
///   - « Liée à l'autre type » (Rejetee) : dans « Vers une PC », l'instance porte une prevention (et inversement) -- RIG v2 le
///     dit et quitte (PROC_XLIENPC.cs:46-48, PROC_XLIENPR.cs:41-43) ;
///   - « Rattachée » (EnCours) : le lien du groupe existe (on peut en changer ou le detacher) ;
///   - « À rattacher » (AFaire) sinon.
///
/// RECHERCHE (IRechercheEcran) : « Numéro de rôle » et « Nom courant », les deux premiers criteres de la recherche d'instance de
/// RIG v2 (FormRechercheInstance.cs:425-426, cherches dans les colonnes comme LecteurInstanceJudiciaire) ; « Numéro de PC ou de
/// prévention » : le numero de l'entite deja liee (PRCLL_NUM_PC / PREVE_NUM_PRV). Le champ de recherche : role OU nom.
///
/// FICHE (IFicheEcran) : section « Lien » (le numero de la PC et celui de la prevention liees) et « Entité liée » (le volet
/// d'information de RIG v2, OPE_XLIENPC_INFOPC / OPE_XLIENPR_INFOPR : numero, nom de l'affaire, dates, solution, cloture) --
/// l'apercu de ce que le geste remplace ou detache. Lecture seule, motif a l'appui, pour une instance verrouillee ou liee a
/// l'autre type.
/// </summary>
internal sealed class LecteurLiensPc : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_XLIEN";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string VersPc = "Vers une PC", VersPrevention = "Vers une prévention";

    internal const string CritereRole = "Numéro de rôle", CritereNom = "Nom courant", CritereEntite = "Numéro de PC ou de prévention";

    /// <summary>Les sections et champs du descripteur (ModifierAffaire.cs).</summary>
    internal const string SectionLien = "Lien", SectionDetachement = "Détachement", SectionEntite = "Entité liée";

    internal const string ChampPc = "Procédure collective", ChampPrevention = "Prévention", ChampDetacher = "Détacher le lien",
                          ChampEntite = "PC ou prévention liée";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereRole, CritereNom, CritereEntite];

    /// <summary>Les types de numero de role des deux recherches de RIG v2 (Designer de PROC_XLIENPC et PROC_XLIENPR).</summary>
    internal const string TypesPc = "('JROP', 'JRCT', 'JRPC', 'JRJC')", TypesPrevention = "('JRPR', 'JRMC', 'JROP')";

    private static readonly string Requete = $$"""
        SELECT TOP (@plafond) l.Sens, i.INSTN_ID_INSTN AS Id, i.INSTN_NUM_ROLE AS Role, i.INSTN_NOM_COURANT AS NomCourant,
               pc.PRCLL_NUM_PC AS NumPc, prv.PREVE_NUM_PRV AS NumPrv,
               CASE WHEN i.INSTN_ID_PRCLL IS NULL THEN 0 ELSE 1 END AS AvecPc, CASE WHEN i.INSTN_ID_PREVE IS NULL THEN 0 ELSE 1 END AS AvecPrv,
               v.DMND_NUM_DEMANDE AS Verrou, COUNT(*) OVER () AS Total
        FROM (SELECT 'PC' AS Sens, i.INSTN_ID_INSTN FROM v2.INSTANCE AS i
              JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
              WHERE ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {{TypesPc}}
              UNION ALL
              SELECT 'PR', i.INSTN_ID_INSTN FROM v2.INSTANCE AS i
              JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
              WHERE ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {{TypesPrevention}}) AS l
        JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = l.INSTN_ID_INSTN
        LEFT JOIN v2.PROCEDURE_COLLECTIVE AS pc ON pc.PRCLL_ID_PRCLL = i.INSTN_ID_PRCLL
        LEFT JOIN v2.PREVENTION AS prv ON prv.PREVE_ID_PREVE = i.INSTN_ID_PREVE
        OUTER APPLY (SELECT TOP (1) d.DMND_NUM_DEMANDE FROM v2.DEMANDE AS d
                     JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = d.DMND_ID_DMND
                     WHERE dd.DSSDM_CODE_SEQUENCEUR = '{{VerrouDemandeV2.SequenceurInstance}}'
                       AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(100), i.INSTN_ID_INSTN)
                       AND (d.DMND_EN_COURS = 1 OR d.DMND_ETAT_DEMANDE IN ('W', 'I', 'N'))
                     ORDER BY d.DMND_ID_DMND) AS v
        WHERE 1 = 1 /*recherche*/
        ORDER BY l.Sens, i.INSTN_NUM_ROLE DESC, i.INSTN_ID_INSTN DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", ""), Ligne, ct, new SqlParameter("@plafond", plafond));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var filtres = new List<string>();
        // Les numeros sans leurs blancs (FormateNumRole, FormRechercheInstance.cs:996) ; le nom tel quel. LIKE parametre.
        static string Numero(string v) => FormeLigne.Contient(v.Replace(" ", "", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add("(i.INSTN_NUM_ROLE LIKE @texteRole OR i.INSTN_NOM_COURANT LIKE @texteNom)");
            parametres.Add(new SqlParameter("@texteRole", Numero(recherche.Texte)));
            parametres.Add(new SqlParameter("@texteNom", FormeLigne.Contient(recherche.Texte)));
        }
        if (recherche.Critere(CritereRole) is { } role)
        {
            filtres.Add("i.INSTN_NUM_ROLE LIKE @role");
            parametres.Add(new SqlParameter("@role", Numero(role)));
        }
        if (recherche.Critere(CritereNom) is { } nom)
        {
            filtres.Add("i.INSTN_NOM_COURANT LIKE @nom");
            parametres.Add(new SqlParameter("@nom", FormeLigne.Contient(nom)));
        }
        if (recherche.Critere(CritereEntite) is { } entite)
        {
            filtres.Add("(pc.PRCLL_NUM_PC LIKE @entite OR prv.PREVE_NUM_PRV LIKE @entite)");
            parametres.Add(new SqlParameter("@entite", Numero(entite)));
        }
        var sql = Requete.Replace("/*recherche*/", filtres.Count == 0 ? "" : " AND " + string.Join(" AND ", filtres));
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var pc = FormeLigne.Texte(r, "Sens") == "PC";
        var role = FormeLigne.Texte(r, "Role");
        var numPc = FormeLigne.Texte(r, "NumPc");
        var numPrv = FormeLigne.Texte(r, "NumPrv");
        var avecPc = Convert.ToInt32(r["AvecPc"], CultureInfo.InvariantCulture) == 1;
        var avecPrv = Convert.ToInt32(r["AvecPrv"], CultureInfo.InvariantCulture) == 1;
        var verrou = FormeLigne.Texte(r, "Verrou");
        var lie = pc ? avecPc : avecPrv;
        var autre = pc ? avecPrv : avecPc;
        var etat = verrou.Length > 0 ? "EnAttente" : autre ? "Rejetee" : lie ? "EnCours" : "AFaire";
        string Numero(string n) => n.Length > 0 ? n : "sans numéro";
        var precisions = FormeLigne.Joindre(
            role.Length > 0 ? $"Rôle {role}" : null,
            avecPc ? $"PC {Numero(numPc)}" : null, avecPrv ? $"prévention {Numero(numPrv)}" : null,
            !lie && !autre ? (pc ? "sans PC" : "sans prévention") : null,
            verrou.Length > 0 ? $"bloquée par la demande {verrou}" : null);
        var id = FormeLigne.Texte(r, "Id");
        var nom = FormeLigne.Texte(r, "NomCourant") is { Length: > 0 } n ? n : $"Instance n° {(role.Length > 0 ? role : id)}";
        return new LigneLue(nom, precisions, pc ? VersPc : VersPrevention, etat, []) { Id = $"{(pc ? "PC" : "PR")}-{id}" };
    }

    // ------------------------------------------------------------------ L'INSTANCE ET SA FICHE

    /// <summary>L'instance d'une ligne, telle que la fiche et l'ecrivain la lisent.</summary>
    internal sealed record InstanceLiee(bool VersPc, int Id, string Role, string NomCourant, int? IdPc, string NumPc, int? IdPrv, string NumPrv)
    {
        /// <summary>« l'instance 2026J00012 », pour les messages et le journal.</summary>
        public string EnMots => $"l'instance {(Role.Length > 0 ? Role : $"n° {Id}")}";

        /// <summary>Le lien du groupe de la ligne (PC ou prevention), et celui de l'autre type.</summary>
        public bool Lie => VersPc ? IdPc is not null : IdPrv is not null;
        public bool LieeAutreType => VersPc ? IdPrv is not null : IdPc is not null;

        /// <summary>Le message de RIG v2 quand l'instance porte le lien de l'autre type (PROC_XLIENPC.cs:47, PROC_XLIENPR.cs:42), au mot pres.</summary>
        public string MessageAutreType => VersPc
            ? "L’instance sélectionnée est liée à une prévention, vous devez aller en XLIENPR pour supprimer le lien avec la prévention."
            : "L’instance sélectionnée est liée à une procédure collective, vous devez aller en XLIENPC pour supprimer le lien avec la procédure collective";
    }

    private static readonly string LectureInstance = $$"""
        SELECT i.INSTN_ID_INSTN AS Id, i.INSTN_NUM_ROLE AS Role, i.INSTN_NOM_COURANT AS NomCourant, i.INSTN_ID_PRCLL AS IdPc,
               pc.PRCLL_NUM_PC AS NumPc, i.INSTN_ID_PREVE AS IdPrv, prv.PREVE_NUM_PRV AS NumPrv
        FROM v2.INSTANCE AS i /*verrou*/
        JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
        LEFT JOIN v2.PROCEDURE_COLLECTIVE AS pc ON pc.PRCLL_ID_PRCLL = i.INSTN_ID_PRCLL
        LEFT JOIN v2.PREVENTION AS prv ON prv.PREVE_ID_PREVE = i.INSTN_ID_PREVE
        WHERE i.INSTN_ID_INSTN = @id AND ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN /*types*/
        """;

    /// <summary>L'instance de la ligne <paramref name="id"/> (« PC-12 », « PR-7 ») si elle est dans la recherche de son processus ;
    /// null sinon. Dans la transaction d'un geste (<paramref name="tx"/>), verrouillee en ecriture jusqu'a sa fin.</summary>
    internal static async Task<InstanceLiee?> LireInstanceAsync(SqlConnection cn, SqlTransaction? tx, string id, CancellationToken ct)
    {
        var tiret = id.IndexOf('-', StringComparison.Ordinal);
        if (tiret < 0 || id[..tiret] is not ("PC" or "PR")
            || !int.TryParse(id[(tiret + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        var versPc = id[..tiret] == "PC";
        var sql = LectureInstance.Replace("/*verrou*/", tx is null ? "" : "WITH (UPDLOCK, HOLDLOCK)")
                                 .Replace("/*types*/", versPc ? TypesPc : TypesPrevention);
        LectureV2.ExigerLecture(sql);
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@id", n);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new InstanceLiee(versPc, n, FormeLigne.Texte(r, "Role"), FormeLigne.Texte(r, "NomCourant"),
            r["IdPc"] is int pc ? pc : null, FormeLigne.Texte(r, "NumPc"), r["IdPrv"] is int prv ? prv : null, FormeLigne.Texte(r, "NumPrv"));
    }

    // Le volet d'information de l'entite liee : OPE_XLIENPC_INFOPC (PC : nom de l'affaire, ouverture, solution, cloture) et
    // OPE_XLIENPR_INFOPR (prevention : nom, ouverture, cloture).
    private const string EntitePc = """
        SELECT p.PRCLL_NUM_PC AS Numero, p.PRCLL_NOM_AFFAIRE AS Nom, p.PRCLL_DATE_OUVERTURE AS Ouverture,
               p.PRCLL_CODE_SOLUTION AS Solution, p.PRCLL_CODE_CLOTURE AS Cloture
        FROM v2.PROCEDURE_COLLECTIVE AS p WHERE p.PRCLL_ID_PRCLL = @id
        """;

    private const string EntitePrv = """
        SELECT r.PREVE_NUM_PRV AS Numero, r.PREVE_NOM_AFFAIRE AS Nom, r.PREVE_DATE_OUVERTURE AS Ouverture,
               NULL AS Solution, CONVERT(varchar(10), r.PREVE_DATE_CLOTURE, 103) AS Cloture
        FROM v2.PREVENTION AS r WHERE r.PREVE_ID_PREVE = @id
        """;

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (await LireInstanceAsync(baseGreffe, null, id, ct) is not { } i) return null;
        var motif = await VerrouDemandeV2.MotifAsync(baseGreffe, null, "Instance", VerrouDemandeV2.SequenceurInstance,
                                                     i.Id.ToString(CultureInfo.InvariantCulture), ct)
                    ?? (i.LieeAutreType ? i.MessageAutreType.TrimEnd('.') : null);
        // L'entite du groupe, si elle est liee : ce que le geste remplace ou detache.
        var lignes = new List<string[]>();
        if ((i.VersPc ? i.IdPc : i.IdPrv) is { } idEntite)
        {
            await using var r = await LectureV2.LireAsync(baseGreffe, i.VersPc ? EntitePc : EntitePrv, ct, new SqlParameter("@id", idEntite));
            if (await r.ReadAsync(ct))
                lignes.Add([FormeLigne.Texte(r, "Numero"), FormeLigne.Texte(r, "Nom"), FormeLigne.Date(FormeLigne.DateDe(r, "Ouverture")),
                            FormeLigne.Texte(r, "Solution"), FormeLigne.Texte(r, "Cloture")]);
        }
        return new FicheEcran(Procedure, id, i.NomCourant.Length > 0 ? i.NomCourant : $"Instance n° {i.Role}",
        [
            new SectionFiche(SectionLien, [new(ChampPc, i.NumPc), new(ChampPrevention, i.NumPrv)]),
            new SectionFiche(SectionEntite,
                [new(ChampEntite, "") { Colonnes = ["Numéro", "Nom de l'affaire", "Ouverture", "Solution", "Clôture"], Lignes = [.. lignes] }]),
        ])
        {
            LectureSeule = motif is not null,
            Motif = motif,
        };
    }
}
