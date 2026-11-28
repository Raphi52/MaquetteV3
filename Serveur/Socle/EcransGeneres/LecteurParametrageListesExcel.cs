using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Listes Excel » (PROC_PARAM_LISTEEXCEL, forme Consultation) : les listes Excel parametrees du greffe -- chaque type de liste que
/// l'« Export Excel » (PROC_LISTEEXCEL, LecteurListesExcel.cs) sait sortir, ses champs SELECT et sa clause FROM. Mode d'emploi :
/// LecteurEcran.cs. Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 2, 28/11/2026.
///
/// SOURCE (ancien RIG) : le plugin du menu, Source\RIG\DLL\Processus\PROC_PARAM_LISTEEXCEL, est une COQUILLE (aucune etape,
/// PROC_PARAM_LISTEEXCEL.Designer.cs:25-28 ; proc_param_listeexcel.md, « Nature reelle du plugin »). La fonction est dans le programme
/// client du meme nom de classe, D:\RigApplication\Source\Judiciaire\Exe\RIG_CLIENT_PARAM_LISTEEXCEL (que la fiche n'a pas lu) :
///   - LA LISTE (Recherche\OPE_RECHERCHE.cs:47-50) : « SELECT * FROM PARAMETRAGE_LISTE WHERE (PRMLS_LISTE LIKE '%LISTEXLS_%') ORDER BY
///     PRMLS_VALEUR », colonnes « Type de liste » (PRMLS_VALEUR) et « Libellé » (PRMLS_LIBELLE), l. 40-45 ;
///   - LA RECHERCHE (« Recherche par code », OPE_RECHERCHE.Designer.cs:72 ; l. 54, 82-98) : plein texte AmiFullText sur « valeur
///     libelle », en debut de mot (eTypeRechercheFullText.StartWith, AmiFullText.cs:8) : chaque mot saisi doit commencer un mot du
///     texte (FindIntersect, AmiFullTextObj.cs:71-96). Les deux textes passent par AmiString.NoAccentPourCompareSql (AmiString.cs:424-456) :
///     minuscules, sans accent, tout ce qui n'est ni lettre, ni chiffre, ni « _ » devient un blanc. La V2 filtre en memoire la liste
///     chargee ; ici la base filtre, sur la meme regle (Mots, puis un LIKE par mot sous une collation insensible a la casse et aux
///     accents, le mot precede d'un caractere qui n'est ni lettre, ni chiffre, ni « _ »). Les criteres « Type de liste » et « Libellé »
///     (les deux colonnes de la grille) appliquent la meme regle a leur seule colonne ;
///   - LA FICHE (Edition\) : la liste (OPE_EDITION, « Code » et « Libellé », .Designer.cs:69-78) ; ses champs SELECT
///     (ParametrageListe.ListParametrageListeXls = GetAllParametrageListeXlsByType(valeur, onlySelect: true), ParametrageListe.cs:81-90,
///     ParametrageListeXls.cs:56-61), montres par ordre initial (Edition\OPE_LISTE.cs:72, OrderBy PRMLX_NUM_ORDRE_INIT) dans la grille « Ordre
///     des champs » aux colonnes « Code champs », « Libellé », « SQL », « Ordre » (ListViewOrderColumn.cs:73-76, l'ordre = NUM_ORDRE_INIT,
///     l. 32) ; sa clause « From » (OPE_LISTE.Designer.cs:84 ; GetParametrageListeXlsFromByTypeListe, ParametrageListeXls.cs:44-46 :
///     PRMLX_TYPE_TEXTE = 'FROM').
/// GROUPES : la famille d'objets que l'export sort pour ce type, les « #region » du switch de PROC_LISTEEXCEL
///   (Source\RIG\DLL\Processus\PROC_LISTEEXCEL\UTIL\Operations\OPE_RECHERCHE.cs:76-221) : Instance (INS IDJ IEP IEF SDJ GIE AFF),
///   Mesure (MEP MC MES MEF MDC), Prévention (PRV VEP VEF), Procédure (PEF PEP PC PCC PSES), Bodacc + Jal (JAL BOD), Divers (VR). Un
///   type que ce switch ne nomme pas (l'export ne le compte pas) va dans « Divers », ses precisions le disent.
/// ETAT : le controle de l'export de la V2, « 2 champs minimum sont requis » (OPE_LISTE.DoVerifier, Edition\OPE_LISTE.cs:147-150) --
///   aucun champ SELECT -> AFaire « Sans champ » ; un seul -> EnCours « Un seul champ » ; deux ou plus -> Validee « Exportable ».
/// NON REPRIS (nomme) : l'edition. La V2 n'ECRIT RIEN dans la base : « Ajouter », la saisie d'un champ (OPE_CHAMP.Valider,
///   « Code obligatoire », « Libellé obligatoire », « SQL obligatoire ») et l'ordre des champs ne changent que la memoire de l'ecran.
///   Ecrire directement ces lignes laisserait tout agent du greffe poser du texte SQL que l'export execute (PRMLX_TEXTE) : la V3 ne le
///   fait pas, la fiche est en lecture. Le seul bouton de la barre, EXPORT (FORM_PARAM_LISTEEXCEL.cs:18-33), qui ecrit un SCRIPT
///   « exportListeExcel_<type>.sql » sur le poste (OPE_LISTE.Exporter, Edition\OPE_LISTE.cs:90-145) : EcrivainParametrageListesExcel.cs.
/// </summary>
internal sealed class LecteurParametrageListesExcel : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_PARAM_LISTEEXCEL";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Instances = "Instances", Mesures = "Mesures", Preventions = "Préventions", Procedures = "Procédures",
                          Parutions = "BODACC et JAL", Divers = "Divers";

    /// <summary>Les criteres du descripteur, au mot pres : les deux colonnes de la grille de la V2 (OPE_RECHERCHE.cs:42-43).</summary>
    internal const string CritereType = "Type de liste", CritereLibelle = "Libellé";

    /// <summary>La recherche de la V2 lit les deux colonnes ensemble ; chaque critere cherche dans la sienne, a la meme regle.</summary>
    public IReadOnlyCollection<string> Criteres { get; } = [CritereType, CritereLibelle];

    // Une ligne par liste parametree, avec son nombre de champs SELECT et la presence de sa clause FROM. Le filtre est celui de la V2 au
    // caractere pres (OPE_RECHERCHE.cs:48) : son « _ » est le joker du LIKE, « LISTEXLS » doit etre suivi d'un caractere. fix-ok: lu le
    // 28/11/2026 dans OPE_RECHERCHE.cs:48 -- une premiere ecriture echappait ce « _ » ([_]), plus stricte que la V2.
    internal const string Filtre = "l.PRMLS_LISTE LIKE '%LISTEXLS_%'";

    private const string Requete = $"""
        SELECT TOP (@plafond) l.PRMLS_ID_PRMLS, l.PRMLS_VALEUR, l.PRMLS_LIBELLE,
               (SELECT COUNT(*) FROM v2.PARAMETRAGE_LISTE_XLS AS c WHERE c.PRMLX_TYPE_LISTE = l.PRMLS_VALEUR AND c.PRMLX_TYPE_TEXTE = 'SELECT') AS Champs,
               CASE WHEN EXISTS (SELECT 1 FROM v2.PARAMETRAGE_LISTE_XLS AS f WHERE f.PRMLX_TYPE_LISTE = l.PRMLS_VALEUR AND f.PRMLX_TYPE_TEXTE = 'FROM')
                    THEN 1 ELSE 0 END AS AvecFrom,
               COUNT(*) OVER () AS Total
        FROM v2.PARAMETRAGE_LISTE AS l
        WHERE {Filtre} /*filtres*/
        ORDER BY l.PRMLS_VALEUR, l.PRMLS_ID_PRMLS
        """;

    // Le texte cherche de la V2 : « valeur libelle », precede d'un blanc (AmiFullTextObj.Add, l. 60-64), sous une collation insensible
    // a la casse et aux accents (NoAccentPourCompareSql) ; un critere, sa seule colonne.
    private static string Cherche(string colonnes) => $"(' ' + {colonnes}) COLLATE Latin1_General_CI_AI";

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        var k = 0;
        void ChercherMots(string? saisie, string colonnes)
        {
            foreach (var mot in Mots(saisie))
            {
                // Le mot commence un mot du texte : un caractere qui n'est ni lettre, ni chiffre, ni « _ » le precede (le blanc de tete compte).
                var nom = $"@mot{k++}";
                f.Ajouter($"{Cherche(colonnes)} LIKE {nom}", (nom, "%[^0-9a-z_]" + mot.Replace("_", "[_]", StringComparison.Ordinal) + "%"));
            }
        }
        ChercherMots(recherche.Texte, "ISNULL(l.PRMLS_VALEUR, '') + ' ' + ISNULL(l.PRMLS_LIBELLE, '')");
        ChercherMots(recherche.Critere(CritereType), "ISNULL(l.PRMLS_VALEUR, '')");
        ChercherMots(recherche.Critere(CritereLibelle), "ISNULL(l.PRMLS_LIBELLE, '')");
        return Lire(baseGreffe, f, plafond, ct);
    }

    /// <summary>
    /// Les mots de la recherche de la V2 : NoAccentPourCompareSql (AmiString.cs:424-456 -- minuscules, accents retires, tout autre
    /// caractere qu'une lettre, un chiffre ou « _ » remplace par un blanc ; « œ » donne « oe »), puis coupes aux blancs.
    /// </summary>
    internal static string[] Mots(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return [];
        var sb = new StringBuilder(texte.Length);
        foreach (var c in texte.Replace("œ", "oe", StringComparison.Ordinal).Replace("Œ", "oe", StringComparison.Ordinal)
                     .Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var m = char.ToLowerInvariant(c);
            sb.Append(m is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' ? m : ' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));

    /// <summary>La famille d'objets de l'export pour un type (PROC_LISTEEXCEL OPE_RECHERCHE.cs:76-221) ; null : un type qu'il ne nomme pas.</summary>
    internal static string? Famille(string type) => type switch
    {
        "INS" or "IDJ" or "IEP" or "IEF" or "SDJ" or "GIE" or "AFF" => Instances,
        "MEP" or "MC" or "MES" or "MEF" or "MDC" => Mesures,
        "PRV" or "VEP" or "VEF" => Preventions,
        "PEF" or "PEP" or "PC" or "PCC" or "PSES" => Procedures,
        "JAL" or "BOD" => Parutions,
        "VR" => Divers,
        _ => null,
    };

    /// <summary>La regle d'etat (en tete) : le controle « 2 champs minimum sont requis » de l'export.</summary>
    internal static string Etat(int champs) => champs switch { 0 => "AFaire", 1 => "EnCours", _ => "Validee" };

    private static string Champs(int n) => n switch { 0 => "aucun champ", 1 => "1 champ", _ => $"{n} champs" };

    private static LigneLue Ligne(SqlDataReader r)
    {
        var type = FormeLigne.Texte(r, "PRMLS_VALEUR");
        var libelle = FormeLigne.Texte(r, "PRMLS_LIBELLE");
        var champs = Convert.ToInt32(r["Champs"], CultureInfo.InvariantCulture);
        var famille = Famille(type);
        var precisions = FormeLigne.Joindre($"type {type}", Champs(champs),
            Convert.ToInt32(r["AvecFrom"], CultureInfo.InvariantCulture) == 1 ? null : "sans clause FROM",
            famille is null ? "type que l'export Excel ne sort pas" : null);
        return new LigneLue(libelle.Length > 0 ? libelle : type, precisions, famille ?? Divers, Etat(champs), [])
        {
            Id = FormeLigne.Texte(r, "PRMLS_ID_PRMLS"),
        };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (CriteresConsultation.Entier(id) is not { } n) return null;
        string type, libelle;
        await using (var r = await LectureV2.LireAsync(baseGreffe, $"""
                         SELECT l.PRMLS_VALEUR, l.PRMLS_LIBELLE FROM v2.PARAMETRAGE_LISTE AS l
                         WHERE l.PRMLS_ID_PRMLS = @id AND {Filtre}
                         """, ct, new SqlParameter("@id", n)))
        {
            if (!await r.ReadAsync(ct)) return null;
            type = FormeLigne.Texte(r, "PRMLS_VALEUR");
            libelle = FormeLigne.Texte(r, "PRMLS_LIBELLE");
        }
        var champs = new List<string[]>();
        var from = "";
        await using (var r = await LectureV2.LireAsync(baseGreffe, """
                         SELECT c.PRMLX_CODE_CHAMP, c.PRMLX_LIBELLE, c.PRMLX_TEXTE, c.PRMLX_NUM_ORDRE_INIT, c.PRMLX_TYPE_TEXTE
                         FROM v2.PARAMETRAGE_LISTE_XLS AS c
                         WHERE c.PRMLX_TYPE_LISTE = @type AND c.PRMLX_TYPE_TEXTE IN ('SELECT', 'FROM')
                         ORDER BY CASE c.PRMLX_TYPE_TEXTE WHEN 'FROM' THEN 1 ELSE 0 END, c.PRMLX_NUM_ORDRE_INIT, c.PRMLX_CODE_CHAMP
                         """, ct, new SqlParameter("@type", type)))
        {
            while (await r.ReadAsync(ct))
            {
                if (FormeLigne.Texte(r, "PRMLX_TYPE_TEXTE") == "FROM") { if (from.Length == 0) from = FormeLigne.Texte(r, "PRMLX_TEXTE"); continue; }
                champs.Add([FormeLigne.Texte(r, "PRMLX_CODE_CHAMP"), FormeLigne.Texte(r, "PRMLX_LIBELLE"), FormeLigne.Texte(r, "PRMLX_TEXTE"),
                    FormeLigne.Texte(r, "PRMLX_NUM_ORDRE_INIT")]);
            }
        }
        return new FicheEcran(Procedure, id, libelle.Length > 0 ? libelle : type,
        [
            new("Édition", [new ChampFiche("Code", type), new ChampFiche("Libellé", libelle)]),
            new("Liste des champs",
            [
                new ChampFiche("Ordre des champs", Champs(champs.Count)) { Colonnes = ["Code champs", "Libellé", "SQL", "Ordre"], Lignes = [.. champs] },
                new ChampFiche("From", from),
            ]),
        ]);
    }
}
