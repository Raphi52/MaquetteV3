using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Jonction d'instances » (PROC_JOIN, dans l'ecran reuni « Nouvelle affaire judiciaire ») : les instances MERES ou joindre
/// une fille, ou d'ou en disjoindre une. Mode d'emploi : LecteurEcran.cs ; le geste : EcrivainJonctions.cs. Plan « RIG v3 100 %
/// fonctionnel », lot 4, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_JOIN (le Detail OPE de proc_join.md fait foi : pas d'evenement JONCTION, pas de controle de greffe,
/// UNE fille a la fois). L'objet du processus est la MERE (MetierBase, PROC_JOIN.cs:13), choisie par la recherche d'instance
/// « Instance=JROP, JRIP, JRCT, JRPC, JRREF, JRJC, JRMDE, JRORC, JRPR, JRMC » (PROC_JOIN.Designer.cs:38), lue comme
/// CODE_AFFAIRE.R_CODEAFFAIR_TYPE_NUM_ROLE (RechercheInstance.cs:150-154). ORDRE : « order by instn_num_role desc »
/// (FormRechercheInstance.cs:633).
///
/// GROUPES (descripteur), la place de l'instance dans les jonctions : « Instance principale » (des filles la designent,
/// INSTN_ID_INSTN_INSTANCE_PRINCIPA, Instance.GetListInstanceParInstancePrincipale, Instance.cs:3100-3106), sinon « Instance
/// jointe » (elle a une mere), sinon « Sans jonction ».
/// REGLE D'ETAT : « Verrouillée » (EnAttente) si une autre demande tient la mere (instance.Lock, PROC_JOIN.cs:49 : RIG v2 reste en
/// entree, sans saisie) ; « Libre » (AFaire) sinon.
///
/// RECHERCHE (IRechercheEcran) : « Numéro de rôle » et « Nom courant » (FormRechercheInstance.cs:425-426, comme
/// LecteurInstanceJudiciaire) ; le champ de recherche cherche l'un OU l'autre.
/// FICHE (IFicheEcran) : l'apercu de RIG v2 avant la jonction -- « Instance mère » (OPE_RECAP_INST, OPE_RECAP_INST.cs:27-39 :
/// role, nom courant, code et nature d'affaire, acte, dates) et « Instances jointes » (les filles deja jointes, que SetPhase
/// pre-charge, PROC_JOIN.cs:71-72 : role, nom, type et date de jonction). Lecture seule, motif a l'appui, si la mere est tenue.
/// </summary>
internal sealed class LecteurJonctions : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_JOIN";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Principale = "Instance principale", Jointe = "Instance jointe", SansJonction = "Sans jonction";

    internal const string CritereRole = "Numéro de rôle", CritereNom = "Nom courant";

    /// <summary>Les sections et champs du descripteur (Ouvrir.cs).</summary>
    internal const string SectionMere = "Instance mère", SectionFille = "Instance fille", SectionDisjonction = "Disjonction",
                          SectionJointes = "Instances jointes";

    internal const string ChampRecap = "Récapitulatif de la mère", ChampRoleFille = "Numéro de rôle de la fille",
                          ChampType = "Type de jonction", ChampDate = "Date de jonction", ChampDisjoindre = "Disjoindre la fille",
                          ChampJointes = "Filles jointes";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereRole, CritereNom];

    /// <summary>Les types de numero de role de la mere (PROC_JOIN.Designer.cs:38) et de la fille (:81, ETP_INST_FILLES.cs:25).</summary>
    internal const string TypesMere = "('JROP', 'JRIP', 'JRCT', 'JRPC', 'JRREF', 'JRJC', 'JRMDE', 'JRORC', 'JRPR', 'JRMC')",
                          TypesFille = "('JROP', 'JRIP', 'JRCT', 'JRPC', 'JRREF', 'JRJC')";

    private static readonly string Requete = $$"""
        SELECT TOP (@plafond) i.INSTN_ID_INSTN AS Id, i.INSTN_NUM_ROLE AS Role, i.INSTN_NOM_COURANT AS NomCourant,
               m.INSTN_NUM_ROLE AS RoleMere, (SELECT COUNT(*) FROM v2.INSTANCE AS f WHERE f.INSTN_ID_INSTN_INSTANCE_PRINCIPA = i.INSTN_ID_INSTN) AS Filles,
               v.DMND_NUM_DEMANDE AS Verrou, COUNT(*) OVER () AS Total
        FROM v2.INSTANCE AS i
        JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
        LEFT JOIN v2.INSTANCE AS m ON m.INSTN_ID_INSTN = i.INSTN_ID_INSTN_INSTANCE_PRINCIPA
        OUTER APPLY (SELECT TOP (1) d.DMND_NUM_DEMANDE FROM v2.DEMANDE AS d
                     JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = d.DMND_ID_DMND
                     WHERE dd.DSSDM_CODE_SEQUENCEUR = '{{VerrouDemandeV2.SequenceurInstance}}'
                       AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(100), i.INSTN_ID_INSTN)
                       AND (d.DMND_EN_COURS = 1 OR d.DMND_ETAT_DEMANDE IN ('W', 'I', 'N'))
                     ORDER BY d.DMND_ID_DMND) AS v
        WHERE ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {{TypesMere}} /*recherche*/
        ORDER BY i.INSTN_NUM_ROLE DESC, i.INSTN_ID_INSTN DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", ""), Ligne, ct, new SqlParameter("@plafond", plafond));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var filtres = new List<string>();
        static string Role(string v) => FormeLigne.Contient(v.Replace(" ", "", StringComparison.Ordinal));
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
        var role = FormeLigne.Texte(r, "Role");
        var roleMere = FormeLigne.Texte(r, "RoleMere");
        var filles = Convert.ToInt32(r["Filles"], CultureInfo.InvariantCulture);
        var verrou = FormeLigne.Texte(r, "Verrou");
        var id = FormeLigne.Texte(r, "Id");
        var precisions = FormeLigne.Joindre(
            role.Length > 0 ? $"Rôle {role}" : null,
            filles > 0 ? $"{filles} instance{(filles > 1 ? "s" : "")} jointe{(filles > 1 ? "s" : "")}" : null,
            roleMere.Length > 0 ? $"jointe à {roleMere}" : null,
            verrou.Length > 0 ? $"bloquée par la demande {verrou}" : null);
        var nom = FormeLigne.Texte(r, "NomCourant") is { Length: > 0 } n ? n : $"Instance n° {(role.Length > 0 ? role : id)}";
        return new LigneLue(nom, precisions, filles > 0 ? Principale : roleMere.Length > 0 ? Jointe : SansJonction,
                            verrou.Length > 0 ? "EnAttente" : "AFaire", []) { Id = id };
    }

    // ------------------------------------------------------------------ LES INSTANCES ET LA FICHE

    /// <summary>Une instance telle que les controles de jonction la lisent (OPE_JONCTION_FILLES.cs) : son role, sa date d'acte
    /// (« date de saisine » des messages, :175-193) et sa mere.</summary>
    internal sealed record InstanceJointe(int Id, string Role, string NomCourant, DateTime? DateActe, int? IdMere);

    private const string LectureInstance = """
        SELECT i.INSTN_ID_INSTN AS Id, i.INSTN_NUM_ROLE AS Role, i.INSTN_NOM_COURANT AS NomCourant, i.INSTN_DATE_ACTE AS DateActe,
               i.INSTN_ID_INSTN_INSTANCE_PRINCIPA AS IdMere
        FROM v2.INSTANCE AS i /*verrou*/
        """;

    private static InstanceJointe Lue(SqlDataReader r)
        => new((int)r["Id"], FormeLigne.Texte(r, "Role"), FormeLigne.Texte(r, "NomCourant"), FormeLigne.DateDe(r, "DateActe"),
               r["IdMere"] is int m ? m : null);

    /// <summary>L'instance <paramref name="id"/> si elle est dans la recherche des meres ; null sinon.</summary>
    internal static async Task<InstanceJointe?> LireMereAsync(SqlConnection cn, SqlTransaction? tx, string id, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        var sql = LectureInstance.Replace("/*verrou*/", tx is null ? "" : "WITH (UPDLOCK, HOLDLOCK)") +
                  $" JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE WHERE i.INSTN_ID_INSTN = @id AND ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {TypesMere}";
        return (await LireAsync(cn, tx, sql, ct, new SqlParameter("@id", n))).FirstOrDefault();
    }

    /// <summary>Les filles de la recherche de PROC_JOIN qui portent le numero de role <paramref name="role"/> (blancs retires).</summary>
    internal static async Task<List<InstanceJointe>> FillesParRoleAsync(SqlConnection cn, SqlTransaction tx, string role, CancellationToken ct)
    {
        var sql = LectureInstance.Replace("/*verrou*/", "WITH (UPDLOCK, HOLDLOCK)") +
                  $" JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE WHERE REPLACE(i.INSTN_NUM_ROLE, ' ', '') = @role AND ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {TypesFille}";
        return await LireAsync(cn, tx, sql, ct, new SqlParameter("@role", role));
    }

    /// <summary>L'instance <paramref name="id"/>, sans filtre de type (une mere, en remontant la chaine des jonctions).</summary>
    internal static async Task<InstanceJointe?> LireParIdAsync(SqlConnection cn, SqlTransaction tx, int id, CancellationToken ct)
        => (await LireAsync(cn, tx, LectureInstance.Replace("/*verrou*/", "") + " WHERE i.INSTN_ID_INSTN = @id", ct, new SqlParameter("@id", id))).FirstOrDefault();

    private static async Task<List<InstanceJointe>> LireAsync(SqlConnection cn, SqlTransaction? tx, string sql, CancellationToken ct, params SqlParameter[] p)
    {
        LectureV2.ExigerLecture(sql);
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddRange(p);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lues = new List<InstanceJointe>();
        while (await r.ReadAsync(ct)) lues.Add(Lue(r));
        return lues;
    }

    // Le recapitulatif de la mere (OPE_RECAP_INST.cs:27-39) et ses filles jointes, avec le libelle du type de jonction.
    private const string Recap = """
        SELECT i.INSTN_NUM_ROLE AS Role, i.INSTN_NOM_COURANT AS NomCourant, i.INSTN_CODE_AFFAIRE AS CodeAffaire,
               COALESCE(na.R_NATUREAFFA_LIBELLE_ECRAN, i.INSTN_NATURE_AFFAIRE) AS Nature, i.INSTN_CODE_ACTE_PROCE AS Acte,
               i.INSTN_DATE_ACTE AS DateActe, i.INSTN_DATE_ENROLEMENT AS Enrolement
        FROM v2.INSTANCE AS i
        LEFT JOIN v2.NATURE_AFFAIRE AS na ON na.R_NATUREAFFA_VALEUR = i.INSTN_NATURE_AFFAIRE
        WHERE i.INSTN_ID_INSTN = @id
        """;

    private const string Filles = """
        SELECT f.INSTN_NUM_ROLE AS Role, f.INSTN_NOM_COURANT AS NomCourant,
               COALESCE(tj.R_TYPEJONCTI_LIBELLE_ECRAN, f.INSTN_TYPE_JONCTION) AS TypeJonction, f.INSTN_DATE_JONCTION AS DateJonction
        FROM v2.INSTANCE AS f
        LEFT JOIN v2.TYPE_JONCTION AS tj ON tj.R_TYPEJONCTI_VALEUR = f.INSTN_TYPE_JONCTION
        WHERE f.INSTN_ID_INSTN_INSTANCE_PRINCIPA = @id
        ORDER BY f.INSTN_NUM_ROLE, f.INSTN_ID_INSTN
        """;

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (await LireMereAsync(baseGreffe, null, id, ct) is not { } mere) return null;
        var motif = await VerrouDemandeV2.MotifAsync(baseGreffe, null, "Instance", VerrouDemandeV2.SequenceurInstance, id, ct);
        string[] recap = [];
        await using (var r = await LectureV2.LireAsync(baseGreffe, Recap, ct, new SqlParameter("@id", mere.Id)))
            if (await r.ReadAsync(ct))
                recap = [FormeLigne.Texte(r, "Role"), FormeLigne.Texte(r, "NomCourant"), FormeLigne.Texte(r, "CodeAffaire"), FormeLigne.Texte(r, "Nature"),
                         FormeLigne.Texte(r, "Acte"), FormeLigne.Date(FormeLigne.DateDe(r, "DateActe")), FormeLigne.Date(FormeLigne.DateDe(r, "Enrolement"))];
        var filles = new List<string[]>();
        await using (var r = await LectureV2.LireAsync(baseGreffe, Filles, ct, new SqlParameter("@id", mere.Id)))
            while (await r.ReadAsync(ct))
                filles.Add([FormeLigne.Texte(r, "Role"), FormeLigne.Texte(r, "NomCourant"), FormeLigne.Texte(r, "TypeJonction"),
                            FormeLigne.Date(FormeLigne.DateDe(r, "DateJonction"))]);
        return new FicheEcran(Procedure, id, mere.NomCourant.Length > 0 ? mere.NomCourant : $"Instance n° {mere.Role}",
        [
            new SectionFiche(SectionMere, [new(ChampRecap, "")
            {
                Colonnes = ["Numéro de rôle", "Nom courant", "Code affaire", "Nature affaire", "Acte de saisine", "Date de l'acte", "Enrôlement"],
                Lignes = recap.Length == 0 ? [] : [recap],
            }]),
            // Ce que le geste saisit : rien n'est lu pour une jonction a venir.
            new SectionFiche(SectionFille, [new(ChampRoleFille, ""), new(ChampType, ""), new(ChampDate, "")]),
            new SectionFiche(SectionDisjonction, [new(ChampDisjoindre, "")]),
            new SectionFiche(SectionJointes, [new(ChampJointes, "")
            {
                Colonnes = ["Numéro de rôle", "Nom courant", "Type de jonction", "Date de jonction"], Lignes = [.. filles],
            }]),
        ])
        {
            LectureSeule = motif is not null,
            Motif = motif,
        };
    }
}
