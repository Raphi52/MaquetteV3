using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Codes chefs IP » (PROC_PARAM_IP) : la liste ORDONNEE des codes chefs proposes pour les injonctions de payer, rangee dans le reglage
/// PARAMETRAGE « IP_CODE_CHEF » (codes separes par des « ; »). Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 14,
/// 27/11/2026.
///
/// OU EST LE REGLAGE : la table PARAMETRAGE de RIG v2 n'est PAS recopiee en v2.PARAMETRAGE ; elle est reprise dans soc.Reglage
/// (SOC-003, Serveur/Recopie/ReprisesPhase1.cs:47, 86 ; meme lecture qu'AudiencesV2.cs:76). Le lecteur la lit la. fix-ok: mesure le
/// 27/11/2026 -- la 1re version lisait v2.PARAMETRAGE, absente de la base V3 (ReprisesPhase1.cs:86 « PARAMETRAGE » -> « soc.Reglage » ;
/// le lot 9 a vu la meme erreur « Invalid object name 'v2.PARAMETRAGE' » sur la copie de dev, PROC_REJETS_BILANS.sql:35-38) : lecture
/// deplacee sur soc.Reglage, ecrivain retire (il aurait ecrit une table absente, et soc.* est hors du socle des gestes).
/// LES GESTES : EcrivainCodesChefsIp.cs -- ajouter un code (CODE_CHEF commencant par IP, absent de la liste, en fin : OPE_PARAM_IP.cs:43-60),
/// le retirer, le monter ou le descendre d'un rang (RigListViewUpDown._MoveItem) ; liste vide refusee, « La liste ne contient aucun code
/// chef » (ETP_PARAM_IP.cs:43-50) ; la liste jointe par « ; » sans « ; » final (l. 52-68). Ils ecrivent soc.Reglage par
/// geste.ModifierReglageAsync, la meme ecriture que la route des reglages (Serveur/Socle/Greffes/EcritureReglage.cs ; decision du chef
/// msg-cab1a60f, 27/11/2026), au nom de l'agent admis comme tout geste (la V2 n'y controle aucun droit). Ils repartent de la liste
/// que lit cet ecran (CodesRangesAsync, ResolusAsync) : ce que l'agent voit est ce qui est reecrit.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_PARAM_IP\ETP_PARAM_IP.
///   - LA LISTE : ETP_PARAM_IP.DoInitGraphique (l. 14-26) -- Parametrage.GetValeurParametrage("IP_CODE_CHEF") coupe sur « ; », chaque code
///     resolu dans CODE_CHEF (CODE_CHEF.GetCODE_CHEF) ; un code introuvable n'est PAS montre (« if (bdChef != null) », l. 20) et disparait a
///     la sauvegarde suivante. Colonnes « Code chef » et « Libellé » (R_CODECHEF_LIBELLE_ECRAN, OPE_PARAM_IP.cs:11-18) ; l'ordre est celui
///     de la liste (RigListViewUpDown : l'agent le change par monter / descendre) -- la colonne « Ordre » le dit.
///   - LES GROUPES : « Codes IP proposables » -- un code qui commence par IP, le seul que la liste deroulante d'ajout propose
///     (« WHERE R_CODECHEF_VALEUR like 'IP%' », OPE_PARAM_IP.cs:26) ; « Autres codes » -- un code deja range qui ne commence pas par IP
///     (la V2 le garde a l'ouverture).
///   - L'ETAT : tout code montre est enregistre (Validee) : la V2 ne range que des codes resolus.
///   - LA RECHERCHE : le champ cherche le code ou le libelle (la V2 n'a pas de recherche : sa liste est courte et complete).
/// Une seule requete ne rend pas la liste DANS SON ORDRE (le reglage est une chaine) : la valeur du reglage est lue (soc.Reglage), coupee
/// ici, puis les codes resolus dans v2.CODE_CHEF -- deux SELECT seuls, par LectureV2.
/// VALEURS : dans l'ordre des Colonnes (Code chef, Libellé, Ordre).
/// </summary>
internal sealed class LecteurCodesChefsIp : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_PARAM_IP";

    internal const string Reglage = "IP_CODE_CHEF";
    internal const string Proposables = "Codes IP proposables", Autres = "Autres codes";
    internal const string ColCode = "Code chef", ColLibelle = "Libellé", ColOrdre = "Ordre";

    public IReadOnlyCollection<string> Criteres { get; } = [ColCode, ColLibelle];

    /// <summary>La valeur du reglage IP_CODE_CHEF, coupee comme la V2 (Split(';'), l. 16) ; null si le reglage n'existe pas.</summary>
    internal static async Task<string[]?> CodesRangesAsync(Func<string, SqlParameter[], Task<SqlDataReader>> lire, CancellationToken ct)
    {
        await using var r = await lire("SELECT Valeur FROM soc.Reglage WHERE Code = @code", [new SqlParameter("@code", Reglage)]);
        if (!await r.ReadAsync(ct)) return null;
        return (r.IsDBNull(0) ? "" : r.GetString(0)).Split(';').Select(c => c.Trim()).ToArray();
    }

    /// <summary>Les codes ranges qui existent dans CODE_CHEF, dans l'ordre du reglage : (code, libelle).</summary>
    internal static async Task<List<(string Code, string Libelle)>> ResolusAsync(Func<string, SqlParameter[], Task<SqlDataReader>> lire, string[] codes,
                                                                             CancellationToken ct)
    {
        var connus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var demandes = codes.Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (demandes.Length > 0)
        {
            var parametres = demandes.Select((c, i) => new SqlParameter($"@c{i}", c)).ToArray();
            await using var r = await lire(
                $"SELECT R_CODECHEF_VALEUR, R_CODECHEF_LIBELLE_ECRAN FROM v2.CODE_CHEF WHERE R_CODECHEF_VALEUR IN ({string.Join(", ", parametres.Select(p => p.ParameterName))})",
                parametres);
            while (await r.ReadAsync(ct)) connus[r.GetString(0).Trim()] = r.IsDBNull(1) ? "" : r.GetString(1).Trim();
        }
        return [.. codes.Where(c => connus.ContainsKey(c)).Select(c => (c, connus[c]))];
    }

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, null, plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        bool Contient(string valeur, string? cherche) => cherche is null || valeur.Contains(cherche.Trim(), StringComparison.OrdinalIgnoreCase);
        var texte = string.IsNullOrWhiteSpace(recherche.Texte) ? null : recherche.Texte;
        return Lire(baseGreffe, l => (texte is null || Contient(l.Code, texte) || Contient(l.Libelle, texte))
                                     && Contient(l.Code, recherche.Critere(ColCode)) && Contient(l.Libelle, recherche.Critere(ColLibelle)), plafond, ct);
    }

    private static async Task<LignesLues> Lire(SqlConnection baseGreffe, Func<(string Code, string Libelle), bool>? garder, int plafond, CancellationToken ct)
    {
        Task<SqlDataReader> lire(string sql, SqlParameter[] p) => LectureV2.LireAsync(baseGreffe, sql, ct, p);
        if (await CodesRangesAsync(lire, ct) is not { } codes) return new LignesLues([], 0);
        var resolus = await ResolusAsync(lire, codes, ct);
        var lignes = resolus.Select((l, i) => (l, Rang: i + 1)).Where(x => garder is null || garder(x.l)).ToList();
        return new LignesLues([.. lignes.Take(plafond).Select(x => Ligne(x.l.Code, x.l.Libelle, x.Rang))], lignes.Count);
    }

    private static LigneLue Ligne(string code, string libelle, int rang)
    {
        var ip = code.StartsWith("IP", StringComparison.OrdinalIgnoreCase);
        var ordre = rang.ToString(CultureInfo.InvariantCulture);
        return new LigneLue(code, FormeLigne.Joindre(libelle, $"rang {ordre}"), ip ? Proposables : Autres, "Validee", [])
        {
            Id = code,
            Valeurs = [code, libelle, ordre],
        };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        Task<SqlDataReader> lire(string sql, SqlParameter[] p) => LectureV2.LireAsync(baseGreffe, sql, ct, p);
        if (await CodesRangesAsync(lire, ct) is not { } codes) return null;
        var resolus = await ResolusAsync(lire, codes, ct);
        var rang = resolus.FindIndex(l => string.Equals(l.Code, id, StringComparison.OrdinalIgnoreCase));
        if (rang < 0) return null;
        var (code, libelle) = resolus[rang];
        return new FicheEcran(Procedure, id, code,
        [
            new("Ajout", [new ChampFiche(ColCode, code), new ChampFiche(ColLibelle, libelle)]),
            new("Ordre", [new ChampFiche(ColOrdre, (rang + 1).ToString(CultureInfo.InvariantCulture)),
                          new ChampFiche("Liste enregistrée", string.Join(" ; ", resolus.Select(l => l.Code)))]),
        ]);
    }
}
