using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Mots directeurs » (PROC_MDJUD, dans l'ecran reuni « Corriger une affaire judiciaire ») : les affaires judiciaires et leurs
/// mots directeurs, une recherche par numero, nom et famille, et la FICHE d'une affaire. Modes d'emploi : LecteurEcran.cs,
/// EcrivainMotsDirecteurs.cs. Plan « RIG v3 100 % fonctionnel », lot 3, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_MDJUD (D:\RigApplication\Source\RIG\DLL\Processus\PROC_MDJUD, fiche proc_mdjud.md, le « Detail
/// OPE » fait foi) edite UNE chaine de mots (colonne MDPCI_MOTS de RIG_MOTS_DIRECTEUR_PCINST, un enregistrement par affaire,
/// lie par MDPCI_ID_INSTN, MDPCI_ID_PRCLL ou MDPCI_ID_PREVE : RigMotsDirecteurPCInst.cs:46-62), pas une grille.
///
/// FILTRE DE LA LISTE : les affaires que la recherche de MDJUD propose, ses deux variantes reunies (FORM_MDJUD et FORM_MDJUDPR,
/// ETP_RECHERCHE_AFFAIRE.cs:26-31) :
///   - MDJUD  : « Instance=JROP,JRIP,JRCT,JRPC,JRREF,JRJC,JRORC,JRDD;ProcedureCollective=* » ;
///   - MDJUDPR : « Instance=JRPR,JRMC;Prevention=* ».
/// Les instances par leur type de numero de role (CODE_AFFAIRE.R_CODEAFFAIR_TYPE_NUM_ROLE, comme LecteurInstanceJudiciaire),
/// toutes les PC et toutes les preventions, QUI ONT leur enregistrement de mots directeurs : la V3 ne fait que le modifier
/// (EcrivainMotsDirecteurs, point 2) -- une affaire sans enregistrement n'aurait aucun geste, elle n'est pas proposee (ecart
/// nomme : la recherche de la V2 la trouverait). ORDRE : famille (instances, PC, preventions), puis numero decroissant
/// (« order by instn_num_role desc », FormRechercheInstance.cs:633 ; le meme ordre pour les numeros de PC et de prevention).
///
/// GROUPES (descripteur) : « Instances », « Procédures collectives », « Préventions » -- la famille de l'affaire, celle que
/// OPE_MD lit sur le type de MetierBase (OPE_MD.cs:35, :44, :54).
///
/// REGLE D'ETAT (la V3 ne garde pas la demande MDJUD, qui n'est qu'une trace : EcrivainMotsDirecteurs.cs) :
///   - « Enregistrés » (Validee) : la chaine de mots (MDPCI_MOTS) n'est pas vide ;
///   - « À compléter » (AFaire) : la chaine est vide.
///   « En cours » n'a pas de ligne : MDJUD n'interrompt pas (MetierInterrompable = false, PROC_MDJUD.Designer.cs:58).
///
/// RECHERCHE (IRechercheEcran) : « Numéro de rôle, de PC ou de prévention » et « Nom courant » sont les deux zones de l'affaire
/// (_ultNumAffaire, _ultNomAffaire, OPE_MD.cs:38-40, :47-49, :57-59) ; « Famille d'affaire » est la famille du parametrage de
/// recherche (ETP_RECHERCHE_AFFAIRE.cs:27, :30), cherchee dans le nom du groupe (« prév » trouve les préventions). Le champ de
/// recherche cherche le numero OU le nom.
///
/// FICHE (IFicheEcran) : Affaire (numero et nom, en lecture : OPE_MD.cs:38-40), Mots directeurs (la chaine, modifiable),
/// Contrôle (la legende des prefixes de la famille, DataGridViewCodeMD.cs:61-131, codes et libelles de CodeMotsDirecteurs.cs:10-69),
/// Enregistrement (la chaine telle que DoSauve l'enregistrerait, OPE_MD.cs:77-87).
///
/// VERROU : aucun. La recherche de MDJUD (OPE_ENTITE_JUD_RECHERCHE) ne lit pas le verrou de l'affaire (aucun GetLock dans
/// ETP_ENTITE_JUD_RECHERCHE ni dans PROC_MDJUD : seuls PROC_MJUD, PROC_MPC et les processus qui appellent Lock le posent).
/// </summary>
internal sealed class LecteurMotsDirecteurs : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_MDJUD";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Instances = "Instances", Pcs = "Procédures collectives", Preventions = "Préventions";

    /// <summary>Les criteres du descripteur cherches dans la base.</summary>
    internal const string CritereNumero = "Numéro de rôle, de PC ou de prévention", CritereNom = "Nom courant", CritereFamille = "Famille d'affaire";

    /// <summary>Les sections et champs de la fiche, aux titres et libelles du descripteur (ModifierAffaire.cs).</summary>
    internal const string SectionAffaire = "Affaire", SectionMots = "Mots directeurs", SectionControle = "Contrôle", SectionEnregistrement = "Enregistrement";

    internal const string ChampNumero = "Numéro de l'affaire", ChampNom = "Nom courant", ChampMots = "Mots directeurs",
                          ChampPrefixes = "Préfixes reconnus", ChampNormalisee = "Chaîne normalisée";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereNumero, CritereNom, CritereFamille];

    /// <summary>Une famille d'affaire : sa lettre d'identifiant, son groupe, et les prefixes que DoVerifier accepte.</summary>
    internal sealed record Famille(char Lettre, string Groupe, string Article, (string Code, string Libelle)[] Prefixes);

    // Les prefixes reconnus, dans l'ordre des enums (CodeMotsDirecteurs.cs:10-69), avec leur [Description].
    internal static readonly Famille Instance = new('I', Instances, "l'instance",
    [
        ("NR", "Numéro de l'Instance"), ("NI", "Nom de l'Instance"), ("MN", "Nom des demandeurs"), ("MS", "Siren des demandeurs"),
        ("MA", "Adresse des demandeurs"), ("MM", "Nom des mandataires des demandeurs"), ("FN", "Nom des défendeurs"),
        ("FS", "Siren des défendeurs"), ("FA", "Adresse des défendeurs"), ("MF", "Nom des mandataires des defendeurs"),
        ("DD", "Date des décisions Judiciaires"), ("JG", "Nom du juge rapporteur"), ("JU", "Nom du juge"),
        ("MI", "Nom des intervenants"), ("SI", "Siren des intervenants"),
    ]);
    internal static readonly Famille Pc = new('P', Pcs, "la PC",
    [
        ("SE", "Numéro Siren"), ("NP", "Numéro de la PC"), ("NA", "Nom de la PC"), ("DE", "Désignation de l'entreprise"),
        ("DI", "Nom des dirigeants"), ("JU", "Nom du juge commissaire ou juge commissaire suppléant"),
    ]);
    internal static readonly Famille Prevention = new('R', Preventions, "la prévention",
    [
        ("NA", "Nom de l'affaire"), ("SE", "Numéro Siren"), ("NV", "Numéro de la prévention"), ("PA", "Nom de la prévention"),
        ("DE", "Désignation de l'entreprise"), ("DI", "Nom des dirigeants"),
    ]);

    private static Famille? FamilleDe(char lettre) => lettre switch { 'I' => Instance, 'P' => Pc, 'R' => Prevention, _ => null };

    // Les types de numero de role des deux variantes (ETP_RECHERCHE_AFFAIRE.cs:27, :30).
    private const string TypesInstance = "('JROP', 'JRIP', 'JRCT', 'JRPC', 'JRREF', 'JRJC', 'JRORC', 'JRDD', 'JRPR', 'JRMC')";

    // Les affaires des trois familles, une ligne chacune ; m = leur enregistrement de mots directeurs (le premier, comme
    // « new RigMotsDirecteurPCInst(sql) » qui lit la premiere ligne rendue).
    internal static readonly string Affaires = $"""
        SELECT 0 AS Rang, 'I' AS Lettre, i.INSTN_ID_INSTN AS Id, i.INSTN_NUM_ROLE AS Numero, i.INSTN_NOM_COURANT AS Nom
        FROM v2.INSTANCE AS i JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
        WHERE ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {TypesInstance}
        UNION ALL
        SELECT 1, 'P', p.PRCLL_ID_PRCLL, p.PRCLL_NUM_PC, p.PRCLL_NOM_AFFAIRE FROM v2.PROCEDURE_COLLECTIVE AS p
        UNION ALL
        SELECT 2, 'R', r.PREVE_ID_PREVE, r.PREVE_NUM_PRV, r.PREVE_NOM_AFFAIRE FROM v2.PREVENTION AS r
        """;

    internal const string Enregistrement = """
        CROSS APPLY (SELECT TOP (1) md.MDPCI_ID_MDPCI, md.MDPCI_MOTS FROM v2.RIG_MOTS_DIRECTEUR_PCINST AS md
                     WHERE (a.Lettre = 'I' AND md.MDPCI_ID_INSTN = a.Id) OR (a.Lettre = 'P' AND md.MDPCI_ID_PRCLL = a.Id)
                        OR (a.Lettre = 'R' AND md.MDPCI_ID_PREVE = a.Id)
                     ORDER BY md.MDPCI_ID_MDPCI) AS m
        """;

    private static readonly string Requete = $$"""
        SELECT TOP (@plafond) a.Lettre, a.Id, a.Numero, a.Nom, m.MDPCI_ID_MDPCI, m.MDPCI_MOTS, COUNT(*) OVER () AS Total
        FROM ({{Affaires}}) AS a
        {{Enregistrement}}
        WHERE 1 = 1 /*recherche*/
        ORDER BY a.Rang, a.Numero DESC, a.Id DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", ""), Ligne, ct, new SqlParameter("@plafond", plafond));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var filtres = new List<string>();
        // Le numero sans ses blancs (FormateNumRole, FormRechercheInstance.cs:996) ; le nom tel quel. LIKE sans casse.
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
        if (recherche.Critere(CritereNom) is { } nom)
        {
            filtres.Add("a.Nom LIKE @nom");
            parametres.Add(new SqlParameter("@nom", FormeLigne.Contient(nom)));
        }
        if (recherche.Critere(CritereFamille) is { } famille)
        {
            // La famille cherchee dans le nom de son groupe, sans casse ni accent (« prev » trouve « Préventions »).
            var lettres = new[] { Instance, Pc, Prevention }
                .Where(f => CultureInfo.InvariantCulture.CompareInfo.IndexOf(f.Groupe, famille.Trim(),
                            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
                .Select(f => $"'{f.Lettre}'").ToList();
            filtres.Add(lettres.Count == 0 ? "1 = 0" : $"a.Lettre IN ({string.Join(", ", lettres)})");
        }
        var sql = Requete.Replace("/*recherche*/", filtres.Count == 0 ? "" : " AND " + string.Join(" AND ", filtres));
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var famille = FamilleDe(FormeLigne.Texte(r, "Lettre")[0])!;
        var numero = FormeLigne.Texte(r, "Numero");
        var nom = FormeLigne.Texte(r, "Nom");
        var mots = FormeLigne.Texte(r, "MDPCI_MOTS");
        var nombre = mots.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var precisions = FormeLigne.Joindre(
            famille == Instance ? $"Instance · rôle {numero}" : famille == Pc ? $"PC {numero}" : $"Prévention {numero}",
            nombre == 0 ? "chaîne vide" : nombre == 1 ? "1 mot directeur" : $"{nombre} mots directeurs");
        return new LigneLue(nom.Length > 0 ? nom : numero, precisions, famille.Groupe, mots.Length > 0 ? "Validee" : "AFaire", [])
        {
            Id = $"{famille.Lettre}-{FormeLigne.Texte(r, "Id")}",
        };
    }

    // ------------------------------------------------------------------ LA FICHE

    /// <summary>L'affaire et son enregistrement de mots directeurs.</summary>
    internal sealed record AffaireLue(Famille Famille, int Id, string Numero, string Nom, int IdMots, string Mots);

    /// <summary>L'affaire <paramref name="id"/> (« I-123 », « P-45 », « R-6 ») et ses mots, lus sur <paramref name="cn"/> (dans la
    /// transaction d'un geste si <paramref name="tx"/>, verrouilles en ecriture jusqu'a sa fin) ; null si elle n'existe pas, n'est
    /// pas de MDJUD ou n'a pas d'enregistrement de mots directeurs (CROSS APPLY : la liste et la fiche n'en montrent pas).
    /// fix-ok: mesure le 26/11/2026 (grep des jeux du banc) -- PROC_MJUD.sql, PROC_VNOTIF.sql et Program.cs posent des instances
    /// et des PC sans mots directeurs : en OUTER APPLY elles entraient dans cette liste, sans geste possible.</summary>
    internal static async Task<AffaireLue?> LireAffaireAsync(SqlConnection cn, SqlTransaction? tx, string id, CancellationToken ct)
    {
        if (id.Length < 3 || id[1] != '-' || FamilleDe(id[0]) is not { } famille
            || !int.TryParse(id[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        var sql = $"""
            SELECT a.Numero, a.Nom, m.MDPCI_ID_MDPCI, m.MDPCI_MOTS
            FROM ({Affaires}) AS a
            {(tx is null ? Enregistrement : Enregistrement.Replace("v2.RIG_MOTS_DIRECTEUR_PCINST AS md", "v2.RIG_MOTS_DIRECTEUR_PCINST AS md WITH (UPDLOCK, HOLDLOCK)"))}
            WHERE a.Lettre = @lettre AND a.Id = @id
            """;
        LectureV2.ExigerLecture(sql);
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@lettre", famille.Lettre.ToString());
        cmd.Parameters.AddWithValue("@id", n);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new AffaireLue(famille, n, FormeLigne.Texte(r, "Numero"), FormeLigne.Texte(r, "Nom"),
                              Convert.ToInt32(r["MDPCI_ID_MDPCI"], CultureInfo.InvariantCulture), r["MDPCI_MOTS"] is string mots ? mots : "");
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (await LireAffaireAsync(baseGreffe, null, id, ct) is not { } a) return null;
        return new FicheEcran(Procedure, id, a.Nom.Length > 0 ? a.Nom : a.Numero,
        [
            new SectionFiche(SectionAffaire, [new(ChampNumero, a.Numero), new(ChampNom, a.Nom)]),
            new SectionFiche(SectionMots, [new(ChampMots, a.Mots) { Modifiable = true }]),
            new SectionFiche(SectionControle,
            [
                new(ChampPrefixes, "") { Colonnes = ["Code", "Description"], Lignes = [.. a.Famille.Prefixes.Select(p => new[] { p.Code, p.Libelle })] },
            ]),
            new SectionFiche(SectionEnregistrement, [new(ChampNormalisee, EcrivainMotsDirecteurs.Normaliser(a.Mots))]),
        ]);
    }
}
