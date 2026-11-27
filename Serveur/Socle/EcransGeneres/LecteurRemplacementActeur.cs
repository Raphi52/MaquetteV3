using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransGeneres.LiaisonsActeur;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Remplacer un acteur » (PROC_SUPP_ACTEUR_JUD) : les acteurs judiciaires qu'on ne peut supprimer qu'en reportant leurs liens sur un
/// autre -- juges, parquetiers, greffiers assermentes, mandataires PC, intervenants PC qui ont au moins une liaison -- et les remplacements
/// deja faits (ACTEUR_SUPP). Forme Operation : la ligne depliee montre, AVANT confirmation, les liaisons qui seront reprises et les affaires
/// touchees. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026. La reprise : EcrivainRemplacementActeur.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_SUPP_ACTEUR_JUD ; la V2 l'ouvre sur UN acteur, quand l'agent
/// supprime un acteur qui a des liaisons (« if (_GreffierAssermenteSelectionne.HaveLiaisonEntite()) etp.OpenFrm(); »,
/// PROC_ACTEUR_JUD\Edition\OPE_EDITION_GGREFFIER.cs:113-114 ; meme chose pour les autres types ; ETP_EDITION_ACTEUR.cs:142, parametres
/// typeActeur et idActeur). La V3 liste donc ces acteurs : HaveLiaisonEntite = au moins une liaison de GetLiaisonEntite (LiaisonsActeur.cs).
///   - LES GROUPES : « Magistrats et greffe » (juges, parquetiers, greffiers), « Mandataires PC », « Intervenants PC » (les types de la V2,
///     eTypeActeur ; l'expert a son propre ecran, GESTSUPPEXPERT, ETP_EDITION_ACTEUR.cs:142).
///   - L'ETAT : un acteur lie -> AFaire « À choisir » (son remplacant) ; un remplacement fait (une ligne ACTEUR_SUPP de l'un de ces types :
///     la trace que la V2 ecrit, OPE_SUPP_ACTEUR_JUD.cs:475-531, 986) -> Validee « Remplacé ».
///   - ORDRE : par groupe, les acteurs a remplacer d'abord, par nom ; puis les remplacements, du plus recent au plus ancien.
///   - LA FICHE (l'apercu, _GetLiaisonEntite, l. 214-398) : le type et l'acteur, les liaisons comptees par libelle de la V2, les affaires
///     touchees ; le remplacant se saisit dans la section « Remplaçant ».
/// </summary>
internal sealed class LecteurRemplacementActeur : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_SUPP_ACTEUR_JUD";

    internal const string SectionActeur = "Acteur remplacé", ChampType = "Type d'acteur", ChampActeur = "Acteur remplacé";
    internal const string SectionLiaisons = "Liaisons reprises", ChampLiaisons = "Liaisons", ChampAffaires = "Affaires touchées";
    internal const string SectionRemplacant = "Remplaçant", ChampRemplacant = "Remplaçant";

    public IReadOnlyCollection<string> Criteres { get; } = [ChampType, ChampActeur];

    /// <summary>« JUGE-2400501 » -> (type, id) ; null pour une ligne d'historique ou un identifiant illisible.</summary>
    internal static (TypeActeur Type, int Id)? Lire(string id)
    {
        var tiret = id.LastIndexOf('-');
        if (tiret <= 0 || Type(id[..tiret]) is not { } t || CriteresConsultation.Entier(id[(tiret + 1)..]) is not { } n) return null;
        return (t, n);
    }

    private static string Requete(string filtreActeurs, string filtreHistorique)
    {
        var acteurs = Types.Select((t, i) => $"""
            SELECT {i} AS Rang, '{t.Code}' AS Type, 0 AS Fait, a.{t.Cle} AS Id, {t.Nom} AS Nom, ISNULL({t.Abrege}, '') AS Abrege,
                   CAST({t.Actif} AS bit) AS Actif, CAST(NULL AS varchar(200)) AS Remplacant, CAST(NULL AS datetime) AS Le
            FROM v2.{t.Table} AS a WHERE ({AuMoinsUne(t)})
            """);
        var historique = $"""
            SELECT CASE s.ACTSUP_TABLE_SUPP {string.Join(" ", Types.Select((t, i) => $"WHEN '{t.Table}' THEN {i}"))} END AS Rang,
                   s.ACTSUP_TABLE_SUPP AS Type, 1 AS Fait, s.ACTSUP_ID_ACTSUP AS Id, s.ACTSUP_SUPP_DESIGNATION AS Nom, '' AS Abrege, CAST(0 AS bit) AS Actif,
                   s.ACTSUP_REMPL_DESIGNATION AS Remplacant, s.ACTSUP_DATE_CREATION AS Le
            FROM v2.ACTEUR_SUPP AS s WHERE s.ACTSUP_TABLE_SUPP IN ({string.Join(", ", Types.Select(t => $"'{t.Table}'"))})
            """;
        // fix-ok: mesure a la capture du 27/11/2026 (bureau cache, base de dev) -- des noms de la copie anonymisee commencent par un
        // blanc (« AHBHIR », « DNAKK » avant « AEIDX ») : la ligne les montre sans ce blanc (FormeLigne.Texte), le tri le retire aussi.
        // Les retouches precedentes de ce fichier : les NULL types de l'union et la ligne d'historique sans Id (banc).
        return $"""
            SELECT TOP (@plafond) x.Rang, x.Type, x.Fait, x.Id, x.Nom, x.Abrege, x.Actif, x.Remplacant, x.Le, COUNT(*) OVER () AS Total
            FROM ({string.Join("\nUNION ALL\n", acteurs)}
                  UNION ALL
                  {historique}) AS x
            WHERE (x.Fait = 1 {filtreHistorique}) OR (x.Fait = 0 {filtreActeurs})
            ORDER BY CASE WHEN x.Rang <= 2 THEN 0 WHEN x.Rang = 3 THEN 1 ELSE 2 END, x.Fait, CASE WHEN x.Fait = 0 THEN LTRIM(x.Nom) END,
                     x.Le DESC, x.Id DESC
            """;
    }

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete("", ""), Ligne, ct, new SqlParameter("@plafond", plafond));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var filtre = "";
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtre += " AND (x.Nom LIKE @texte OR x.Abrege LIKE @texte)";
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        if (recherche.Critere(ChampActeur) is { } nom)
        {
            filtre += " AND (x.Nom LIKE @nom OR x.Abrege LIKE @nom)";
            parametres.Add(new SqlParameter("@nom", FormeLigne.Contient(nom)));
        }
        if (recherche.Critere(ChampType) is { } type)
        {
            var codes = Types.Where(t => t.Libelle.Contains(type.Trim(), StringComparison.OrdinalIgnoreCase) || t.Groupe.Contains(type.Trim(), StringComparison.OrdinalIgnoreCase))
                             .Select(t => $"'{t.Code}'").ToArray();
            filtre += codes.Length == 0 ? " AND 1 = 0" : $" AND x.Type IN ({string.Join(", ", codes)})";
        }
        return LectureEcran.LireAsync(baseGreffe, Requete(filtre, filtre), Ligne, ct, [.. parametres]);
    }

    /// <summary>« NOM Prénom (ABR) » : NomComplet et AbregeParenthese de la V2 (Juge.cs:30-39), sans la civilite.</summary>
    internal static string NomAbrege(string nom, string abrege) => abrege.Length > 0 ? $"{nom} ({abrege})" : nom;

    private static LigneLue Ligne(SqlDataReader r)
    {
        var t = Types[Convert.ToInt32(r["Rang"], CultureInfo.InvariantCulture)];
        var nom = FormeLigne.Texte(r, "Nom");
        if (Convert.ToInt32(r["Fait"], CultureInfo.InvariantCulture) == 1)
            return new LigneLue($"{nom} → {FormeLigne.Texte(r, "Remplacant")}", FormeLigne.Joindre(t.Libelle, $"remplacé le {FormeLigne.Date(FormeLigne.DateDe(r, "Le"))}"),
                t.Groupe, "Validee", []); // sans Id : un remplacement fait ne s'ouvre ni ne se rejoue
        var actif = Convert.ToBoolean(r["Actif"], CultureInfo.InvariantCulture);
        return new LigneLue(NomAbrege(nom, FormeLigne.Texte(r, "Abrege")), FormeLigne.Joindre(t.Libelle, actif ? "actif" : "inactif"), t.Groupe, "AFaire", [])
        {
            Id = $"{t.Code}-{FormeLigne.Texte(r, "Id")}",
        };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (Lire(id) is not { } lu) return null;
        var (t, n) = lu;
        string nom;
        await using (var r = await LectureV2.LireAsync(baseGreffe, $"SELECT {t.Nom} AS Nom, ISNULL({t.Abrege}, '') AS Abrege FROM v2.{t.Table} AS a WHERE a.{t.Cle} = @id",
                         ct, new SqlParameter("@id", n)))
        {
            if (!await r.ReadAsync(ct)) return null;
            nom = NomAbrege(FormeLigne.Texte(r, "Nom"), FormeLigne.Texte(r, "Abrege"));
        }
        var comptes = await CompterAsync(t, (sql, p) => LectureV2.LireAsync(baseGreffe, sql, ct, p), n, ct);
        var affaires = new List<string[]>();
        await using (var r = await LectureV2.LireAsync(baseGreffe, RequeteAffaires(t), ct, new SqlParameter("@id", n)))
            while (await r.ReadAsync(ct)) affaires.Add([FormeLigne.Texte(r, "Numero"), FormeLigne.Texte(r, "Libelle")]);
        var liees = comptes.Where(c => c.Nombre > 0).ToArray();
        // Le titre de la V2 : « Liste des entités liées : Instance (2) Mesure (1) » (_CompleteLabel, l. 408-413).
        var resume = liees.Length == 0 ? "aucune liaison" : string.Join(" ", liees.Select(c => $"{c.Liaison.Libelle} ({c.Nombre})"));
        return new FicheEcran(Procedure, id, nom,
        [
            new(SectionActeur, [new ChampFiche(ChampType, t.Libelle), new ChampFiche(ChampActeur, nom)]),
            new(SectionLiaisons,
            [
                new ChampFiche(ChampLiaisons, resume)
                {
                    Colonnes = ["Liaison", "Nombre"], Lignes = [.. liees.Select(c => new[] { c.Liaison.Libelle, c.Nombre.ToString(CultureInfo.InvariantCulture) })],
                },
                new ChampFiche(ChampAffaires, affaires.Count == 0 ? "aucune affaire" : $"{affaires.Count} affaire{(affaires.Count > 1 ? "s" : "")}")
                {
                    Colonnes = ["Numéro", "Affaire"], Lignes = [.. affaires],
                },
            ]),
        ]);
    }
}
