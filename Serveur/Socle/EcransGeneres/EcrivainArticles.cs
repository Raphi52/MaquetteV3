using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurArticles;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Articles tarifaires » (PROC_ARTICLE) : AJOUTER un article principal et MODIFIER le libelle ou le prix d'un article, dans la grille.
/// Mode d'emploi : EcrivainEcran.cs ; les lignes : LecteurArticles.cs. Plan « RIG v3 100 % fonctionnel », lot 13, 27/11/2026. Les
/// cellules arrivent par libelle de colonne (« Libellé », « Prix HT »...).
///
/// SOURCE (ancien RIG) : PROC_ARTICLE (D:\RigApplication\Source\RIG\DLL\Processus\PROC_ARTICLE), la sauvegarde DoSauveAfterEtape
/// (PROC_ARTICLE.cs:322-430) : pour chaque article sauve, l'agent et la date sont reecrits (« ARTCL_ID_UTLST = utilisateur courant,
/// ARTCL_DATE_CREATION = DateTime.Now », :361-362) puis Sauve (:363).
/// MODIFIER (la derniere version, celle que la V2 ouvre, :103) : « Libellé » (ARTCL_LIBELLE) et « Prix HT » (ARTCL_PRIX_UHT).
///   Controles, avant toute ecriture : le libelle ne peut etre vide (« Le libellé principal d'un article ne doit pas être vide »,
///   OPE_DONNEES_SPECIFIQUES.cs:94-96) ; un titre n'a pas de prix propre (SetTypeChosenTitre masque le montant UHT, proc_article.md:
///   167) ; le code ne change pas hors creation (lecture seule, proc_article.md:159), ni le type d'un article existant (combo en
///   lecture seule hors creation, OPE_DONNEES_COMMUNES_IDENTIFICATION.cs:50) ; un prix illisible ; rien de change.
/// AJOUTER (le mode creation, :109-118) : un article principal (PR) ; les valeurs de creation de la V2 : prix 0 et date
///   d'application du jour par defaut (:111-112), date de creation et agent (:113-114), groupe de TVA = le plus recent des groupes
///   des articles principaux (« select top 1 ARTGR_ID_ARTGR from ARTICLE_GROUPE inner join ARTICLE_TVA … where ARTGR_TYPE_ARTICLE =
///   'PR' order by ARTTV_DATE_APPLICATION desc », :115-117) ; le code greffe est le code saisi (OPE_DONNEES_SPECIFIQUES.cs:149) ;
///   statut A, sans frais d'huissier ni regroupement (defauts de la table). Controles : le type est obligatoire et un titre n'est pas
///   cree ici (sa composition n'est pas reprise) ; « Le code article ne doit pas être vide » (OPE_DONNEES_SPECIFIQUES.cs:98-100) ;
///   un code deja porte par un article (DoVerifyUniqueness, ProcArticleExtensions.cs:15-21) ; le libelle ; « Interdit de saisir une
///   valeur d'application passée » (OPE_DONNEES_SPECIFIQUES.cs:91-92) ; aucun groupe de TVA des articles principaux.
/// L'AGENT : son identifiant RIG v2 (soc.Compte.IdV2). Une base ou ARTCL_ID_UTLST est obligatoire refuse un agent sans identifiant V2.
/// NON REPRIS (nommes) : la demande V2 du processus, creee et cloturee d'un coup pour chaque article (« Création / Modification de
/// l'article … », :359, :367) -- une trace, que le journal des gestes remplace (decision du plan) ; les versions par periode, leurs
/// liens ARTICLE_CORRESP et le clonage (OPE_HISTORISATION_ARTICLE) ; la propagation du libelle a toutes les versions (proposee par
/// la V2, OPE_DONNEES_SPECIFIQUES.cs:157-174) : seule la derniere version change ; la composition d'un titre et les charges liees ;
/// la recuperation d'un code orphelin (GetUniqueCodeArticle, PROC_ARTICLE.cs:552-607).
/// TRACE : famille « reglage », « a cree » / « a modifie », avant et apres.
/// </summary>
internal sealed class EcrivainArticles : IEcrivainEcran
{
    public string Procedure => "PROC_ARTICLE";
    public string Ecran => "Articles tarifaires";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Modifier];

    internal const string LibelleVide = "Le libellé principal d'un article ne doit pas être vide";
    internal const string CodeVide = "Le code article ne doit pas être vide";
    internal const string DatePassee = "Interdit de saisir une valeur d'application passée";
    internal const string TitreSansPrix = "Un titre composé n'a pas de prix propre : son prix est la somme de ses composants.";

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct) => geste.Demande.Type switch
    {
        TypeGeste.Ajouter => AjouterAsync(geste, ct),
        _ => ModifierAsync(geste, ct),
    };

    private static string? Cellule(GesteEnCours geste, string colonne) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v : null;

    private sealed record Article(int Id, string Code, string Libelle, string Type, decimal Prix, DateTime Application);

    private static async Task<IssueGeste> ModifierAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (CriteresConsultation.Entier(id) is not { } n || await ArticleAsync(geste, n, ct) is not { } a)
            return IssueGeste.Refuser($"Article introuvable dans la base du greffe ({id}).");

        if (Cellule(geste, ColCode) is { } code && code.Trim() != a.Code)
            return IssueGeste.Refuser($"Le code d'un article existant ne se modifie pas : « {a.Code} » est en lecture seule hors création (RIG v2).");
        if (Cellule(geste, ColType) is { } type && CodeType(type) != a.Type)
            return IssueGeste.Refuser("On ne change pas le type d'un article existant (RIG v2 : le type est en lecture seule hors création).");
        if (Cellule(geste, ColPeriode) is { } periode && periode.Trim() != Periode(a.Application))
            return IssueGeste.Refuser("La période de validité est celle de la version : les versions par période ne sont pas reprises dans RIG v3.");

        var libelle = Cellule(geste, ColLibelle)?.Trim() ?? a.Libelle;
        if (libelle.Length == 0) return IssueGeste.Refuser(LibelleVide);
        var prix = a.Prix;
        if (Cellule(geste, ColPrix) is { } saisie && saisie.Trim() != CriteresConsultation.MontantFiche(a.Prix))
        {
            if (a.Type == "TI") return IssueGeste.Refuser(TitreSansPrix);
            if (CriteresConsultation.Montant(saisie) is not { } lu) return IssueGeste.Refuser($"« {ColPrix} » : montant illisible ({saisie}).");
            prix = decimal.Round(lu, 2);
        }
        if (libelle == a.Libelle && prix == a.Prix) return IssueGeste.Refuser("Rien à enregistrer : ni le libellé ni le prix ne changent.");

        var agent = await AgentV2Async(geste, ct);
        await geste.EcrireAsync("""
            UPDATE v2.ARTICLE SET ARTCL_LIBELLE = @libelle, ARTCL_PRIX_UHT = @prix, ARTCL_DATE_CREATION = @maintenant,
                ARTCL_ID_UTLST = ISNULL(@agent, ARTCL_ID_UTLST)
            WHERE ARTCL_ID_ARTCL = @id
            """, ct, new SqlParameter("@libelle", libelle), new SqlParameter("@prix", prix), new SqlParameter("@maintenant", geste.Maintenant),
            new SqlParameter("@agent", (object?)agent ?? DBNull.Value), new SqlParameter("@id", a.Id));
        var trace = new TraceGeste("reglage", $"l'article tarifaire {a.Code}", id, "a modifie",
            new { Libelle = a.Libelle, PrixHt = CriteresConsultation.MontantFiche(a.Prix) },
            new { Libelle = libelle, PrixHt = CriteresConsultation.MontantFiche(prix) });
        return IssueGeste.Ecrit($"Article {a.Code} enregistré : {libelle}, {CriteresConsultation.Euros(prix)} HT.", [id], trace);
    }

    private static async Task<IssueGeste> AjouterAsync(GesteEnCours geste, CancellationToken ct)
    {
        var type = CodeType(Cellule(geste, ColType));
        if (type is null) return IssueGeste.Refuser($"« {ColType} » est obligatoire : {Principal} (PR).");
        if (type == "TI") return IssueGeste.Refuser("Un titre composé se crée avec sa composition, que RIG v3 ne reprend pas : créez un article principal.");
        var code = Cellule(geste, ColCode)?.Trim() ?? "";
        if (code.Length == 0) return IssueGeste.Refuser(CodeVide);
        if (code.Length > 18) return IssueGeste.Refuser($"« {ColCode} » : 18 caractères au plus ({code}).");
        var libelle = Cellule(geste, ColLibelle)?.Trim() ?? "";
        if (libelle.Length == 0) return IssueGeste.Refuser(LibelleVide);
        var application = geste.Maintenant.Date;
        if (Cellule(geste, ColPeriode) is { Length: > 0 } periode)
        {
            if (CriteresConsultation.Date(periode.Trim().Replace("depuis le ", "", StringComparison.Ordinal)) is not { } lue)
                return IssueGeste.Refuser($"« {ColPeriode} » : date d'application illisible ({periode}).");
            application = lue.Date;
        }
        if (application < geste.Maintenant.Date) return IssueGeste.Refuser(DatePassee);
        var prix = 0m;
        if (Cellule(geste, ColPrix) is { Length: > 0 } saisie)
        {
            if (CriteresConsultation.Montant(saisie) is not { } lu) return IssueGeste.Refuser($"« {ColPrix} » : montant illisible ({saisie}).");
            prix = decimal.Round(lu, 2);
        }

        await using (var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.ARTICLE WHERE ARTCL_CODE_ARTICLE_RIG = @code", ct, new SqlParameter("@code", code)))
        {
            await r.ReadAsync(ct);
            if (r.GetInt32(0) > 0) return IssueGeste.Refuser($"Le code article {code} existe déjà : RIG v2 le refuse (un code est unique).");
        }
        int groupe;
        await using (var r = await geste.LireAsync("""
            SELECT TOP (1) g.ARTGR_ID_ARTGR FROM v2.ARTICLE_GROUPE AS g INNER JOIN v2.ARTICLE_TVA AS t ON g.ARTGR_ID_ARTTV = t.ARTTV_ID_ARTTV
            WHERE g.ARTGR_TYPE_ARTICLE = 'PR' ORDER BY t.ARTTV_DATE_APPLICATION DESC
            """, ct))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser("Aucun groupe de TVA des articles principaux (ARTICLE_GROUPE) : RIG v2 ne peut pas rattacher l'article à sa TVA.");
            groupe = r.GetInt32(0);
        }
        var agent = await AgentV2Async(geste, ct);
        if (agent is null)
        {
            await using var r = await geste.LireAsync("SELECT COLUMNPROPERTY(OBJECT_ID('v2.ARTICLE'), 'ARTCL_ID_UTLST', 'AllowsNull')", ct);
            if (await r.ReadAsync(ct) && !r.IsDBNull(0) && r.GetInt32(0) == 0)
                return IssueGeste.Refuser("Votre compte n'a pas d'utilisateur RIG v2 : l'article ne peut pas porter son auteur (ARTCL_ID_UTLST).");
        }

        var nouveau = await geste.InsererAsync("v2.ARTICLE", "ARTCL_ID_ARTCL", new Dictionary<string, object?>
        {
            ["ARTCL_CODE_ARTICLE_RIG"] = code, ["ARTCL_CODE_ARTICLE_GREFFE"] = code, ["ARTCL_LIBELLE"] = libelle, ["ARTCL_PRIX_UHT"] = prix,
            ["ARTCL_TYPE_ARTICLE"] = type, ["ARTCL_DATE_APPLICATION"] = application, ["ARTCL_STATUT"] = "A",
            ["ARTCL_DATE_CREATION"] = geste.Maintenant, ["ARTCL_ID_UTLST"] = agent, ["ARTCL_ID_ARTGR"] = groupe,
            ["ARTCL_HUISSIER"] = false, ["ARTCL_REGROUPE_FACTURE"] = false,
        }, ct);
        var id = nouveau.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var trace = new TraceGeste("reglage", $"l'article tarifaire {code}", id, "a cree", null,
            new { Code = code, Libelle = libelle, PrixHt = CriteresConsultation.MontantFiche(prix), Application = FormeLigne.Date(application) });
        return IssueGeste.Ecrit($"Article {code} créé : {libelle}, {CriteresConsultation.Euros(prix)} HT, applicable {Periode(application)}.", [id], trace);
    }

    private static async Task<Article?> ArticleAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT ARTCL_ID_ARTCL, ARTCL_CODE_ARTICLE_RIG, ARTCL_LIBELLE, ARTCL_TYPE_ARTICLE, ARTCL_PRIX_UHT, ARTCL_DATE_APPLICATION
            FROM v2.ARTICLE WITH (UPDLOCK) WHERE ARTCL_ID_ARTCL = @id AND ARTCL_TYPE_ARTICLE IN ('PR', 'TI')
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        return new Article(r.GetInt32(0), FormeLigne.Texte(r, "ARTCL_CODE_ARTICLE_RIG"), FormeLigne.Texte(r, "ARTCL_LIBELLE"),
            FormeLigne.Texte(r, "ARTCL_TYPE_ARTICLE"), r.GetDecimal(4), r.GetDateTime(5));
    }

    /// <summary>L'utilisateur RIG v2 de l'agent (soc.Compte.IdV2) ; null s'il n'en a pas.</summary>
    private static async Task<int?> AgentV2Async(GesteEnCours geste, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT IdV2 FROM soc.Compte WHERE CompteWindows = @c", ct, new SqlParameter("@c", geste.Agent.CompteWindows));
        return await r.ReadAsync(ct) && !r.IsDBNull(0) ? r.GetInt32(0) : null;
    }
}
