using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Articles tarifaires » (PROC_ARTICLE) : la grille des articles du greffe, un article par code, sa derniere version. Mode
/// d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 13, 27/11/2026. Ajouter et Modifier : EcrivainArticles.cs.
///
/// SOURCE (ancien RIG) : PROC_ARTICLE (D:\RigApplication\Source\RIG\DLL\Processus\PROC_ARTICLE ; Detail OPE de proc_article.md). La
/// V2 n'a pas de liste : elle ouvre UN article recu en parametre (« codeArticle »), sa DERNIERE version, « select * from ARTICLE where
/// ARTCL_CODE_ARTICLE_RIG = … order by ARTCL_DATE_APPLICATION desc » (PROC_ARTICLE.cs:99-105), et sans code elle en cree un (:109-
/// 118). La liste de la V3 est donc celle de ces cibles : la derniere version de chaque code (ROW_NUMBER par ARTCL_CODE_ARTICLE_RIG,
/// le meme ordre qu'a la reconstitution de la lignee, :133), des deux types que le formulaire sait ouvrir, article principal (PR) et
/// titre (TI) (OPE_DONNEES_COMMUNES_IDENTIFICATION.cs:62-69). Les charges (BO, IN, D*) n'en sont pas : la V2 ne les propose que
/// comme charges liees (PROC_ARTICLE.cs:159).
/// Le prix d'un principal est ARTCL_PRIX_UHT ; un titre n'a pas de prix propre (SetTypeChosenTitre masque le montant UHT,
/// proc_article.md:167) : son prix est la somme quantite x prix de ses composants (ARTICLE_COMPOSITION, la regle de
/// LecteurTarifs.cs).
///
/// LA LISTE A L'OUVERTURE : les articles actifs (« AND ARTCL_STATUT = 'A' » sans le parametre inclureSommeil, :100, :131), par code.
/// RECHERCHE (IRechercheEcran) : le champ cherche le code ou le libelle ; « Type d'article » (PR ou TI, ou « Article principal » /
/// « Titre composé ») ; « Statut » (A, S, ou « tous » : le parametre inclureSommeil de la V2).
///
/// GROUPES : « Articles principaux » (PR) et « Titres composés » (TI).
/// REGLE D'ETAT : en sommeil (ARTCL_STATUT = S) -> AFaire « En sommeil » ; date d'application a venir (> aujourd'hui : la V2 ne
/// lit que « ARTCL_DATE_APPLICATION <= GETDATE() », :136) -> EnCours « À venir » ; sinon Validee « En vigueur ».
/// VALEURS : dans l'ordre des Colonnes (Libellé, Code de l'article, Type d'article, Période de validité, Prix HT) : la grille les
/// ouvre en modification. Montant en decimal exact.
/// </summary>
internal sealed class LecteurArticles : IRechercheEcran
{
    public string Procedure => "PROC_ARTICLE";

    internal const string Principaux = "Articles principaux", Titres = "Titres composés";
    internal const string Principal = "Article principal", Titre = "Titre composé";
    internal const string CritereType = "Type d'article", CritereStatut = "Statut";

    internal const string ColLibelle = "Libellé", ColCode = "Code de l'article", ColType = "Type d'article", ColPeriode = "Période de validité",
                          ColPrix = "Prix HT";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereType, CritereStatut];

    /// <summary>« depuis le 01/01/2025 » : la periode de validite d'une version (sa date d'application).</summary>
    internal static string Periode(DateTime? application) => application is { } d ? $"depuis le {FormeLigne.Date(d)}" : "";

    /// <summary>Le code de type d'un libelle saisi (« Article principal » ou « PR ») ; null s'il n'est ni l'un ni l'autre.</summary>
    internal static string? CodeType(string? saisie) => (saisie ?? "").Trim() switch
    {
        Principal or "PR" or "pr" => "PR",
        Titre or "TI" or "ti" => "TI",
        _ => null,
    };

    private const string Requete = """
        SELECT TOP (@plafond) n.ARTCL_ID_ARTCL, n.ARTCL_CODE_ARTICLE_RIG, n.ARTCL_LIBELLE, n.ARTCL_TYPE_ARTICLE, n.ARTCL_STATUT,
               n.ARTCL_DATE_APPLICATION,
               CASE WHEN n.ARTCL_TYPE_ARTICLE <> 'TI' THEN n.ARTCL_PRIX_UHT
                    ELSE ISNULL((SELECT SUM(c.X_ARTCP_QUANTITE * p.ARTCL_PRIX_UHT) FROM v2.ARTICLE_COMPOSITION AS c
                                 JOIN v2.ARTICLE AS p ON p.ARTCL_ID_ARTCL = c.X_ARTCP_ID_ARTCL_FILS
                                 WHERE c.X_ARTCP_ID_ARTCL_PERE = n.ARTCL_ID_ARTCL), 0) END AS PrixHt,
               COUNT(*) OVER () AS Total
        FROM (SELECT a.*, ROW_NUMBER() OVER (PARTITION BY a.ARTCL_CODE_ARTICLE_RIG ORDER BY a.ARTCL_DATE_APPLICATION DESC, a.ARTCL_ID_ARTCL DESC) AS Rang
              FROM v2.ARTICLE AS a) AS n
        WHERE n.Rang = 1 AND n.ARTCL_TYPE_ARTICLE IN ('PR', 'TI') /*filtres*/
        ORDER BY n.ARTCL_CODE_ARTICLE_RIG, n.ARTCL_ID_ARTCL
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        f.Ajouter("n.ARTCL_STATUT = 'A'");
        return Lire(baseGreffe, f, aujourdhui, plafond, ct);
    }

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("n.ARTCL_CODE_ARTICLE_RIG LIKE @texte OR n.ARTCL_LIBELLE LIKE @texte", ("@texte", FormeLigne.Contient(recherche.Texte)));
        var statut = (recherche.Critere(CritereStatut) ?? "A").Trim();
        if (!statut.Equals("tous", StringComparison.OrdinalIgnoreCase))
            f.Ajouter("n.ARTCL_STATUT = @statut", ("@statut", statut.ToUpperInvariant() switch { "ACTIF" => "A", "EN SOMMEIL" or "SOMMEIL" => "S", var s => s }));
        if (recherche.Critere(CritereType) is { } type)
        {
            if (CodeType(type) is { } code) f.Ajouter("n.ARTCL_TYPE_ARTICLE = @type", ("@type", code));
            else f.Ajouter("1 = 0");
        }
        return Lire(baseGreffe, f, aujourdhui, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), r => Ligne(r, aujourdhui), ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));

    private static LigneLue Ligne(SqlDataReader r, DateTime aujourdhui)
    {
        var titre = FormeLigne.Texte(r, "ARTCL_TYPE_ARTICLE") == "TI";
        var code = FormeLigne.Texte(r, "ARTCL_CODE_ARTICLE_RIG");
        var libelle = FormeLigne.Texte(r, "ARTCL_LIBELLE");
        var application = FormeLigne.DateDe(r, "ARTCL_DATE_APPLICATION");
        var prix = CriteresConsultation.Decimal(r, "PrixHt");
        var (etat, libelleEtat) = FormeLigne.Texte(r, "ARTCL_STATUT") == "S" ? ("AFaire", "en sommeil")
            : application > aujourdhui.Date.AddDays(1).AddTicks(-1) ? ("EnCours", "à venir") : ("Validee", "en vigueur");
        var precisions = FormeLigne.Joindre($"Article {code}", titre ? Titre : Principal, $"{CriteresConsultation.Euros(prix)} HT",
            Periode(application), libelleEtat);
        return new LigneLue(libelle, precisions, titre ? Titres : Principaux, etat, [])
        {
            Id = FormeLigne.Texte(r, "ARTCL_ID_ARTCL"),
            Valeurs = [libelle, code, titre ? Titre : Principal, Periode(application), CriteresConsultation.MontantFiche(prix)],
        };
    }
}
