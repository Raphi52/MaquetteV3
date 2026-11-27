using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Tables de référence » (PROC_GTABREF) : les codes des tables de référence que RIG v2 ouvre vraiment à l'édition. Mode d'emploi :
/// LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026. Modifier, Ajouter, Supprimer : EcrivainTablesReference.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_GTABREF\PROC_TABREF_DLL.
///   - LES TABLES : la liste _tables d'OPE_SELECTION_TABLE (l. 17-27) -- PC_ETAT, PC_ETAT_ZONE et DEMAT_CFEC_AJMJ_TYPE_DEPOT, SEULES
///     actives ; NATURE_AFFAIRE, CODE_ACTE_PROCE, JOURNAL_ANNONCES et JURIDICTION_EXTERIEURE y sont en commentaire, donc jamais
///     montrees (proc_gtabref.md, « Périmètre ») : la V3 ne les montre pas non plus. Les trois sont des tables de la base du greffe
///     (RigBaseGreffe\GREFFE\PC_ETAT.cs, PC_ETAT_ZONE.cs, DEMAT_CFEC_AJMJ_TYPE_DEPOT.cs), recopiees en v2.* par la base V3.
///   - LES LIGNES : la requete de la liste des enregistrements de chaque table (FactoryHelper.TEXTE_REQUETE_VALEUR, l. 4-9) -- le code
///     (VALEUR), le libelle ecran, « (! Non saisissable) » devant le libelle quand UTILISABLE_SAISIE = 0, triee par code. La V3 les met
///     l'une sous l'autre, dans l'ordre de _tables.
///   - LES GROUPES : une table par groupe, sous la description de son CBO_TableRef (FactoryHelper.CreateTableRef, l. 66-109).
///   - L'ETAT : libelle vide -> AFaire « Libellé à saisir » (le libelle est NOT NULL mais peut etre vide) ; non saisissable -> EnCours ;
///     sinon Validee « Utilisable en saisie ».
///   - LA FICHE : les champs du formulaire de la table (OPE_AFFICHE_SQLBASE_PC_ETAT.cs:64-82, OPE_AFFICHE_SQLBASE_PC_ETAT_ZONE.cs:75-95,
///     OPE_AFFICHE_SQLBASE_DEMAT_CFEC_AJMJ_TYPE_DEPOT.cs:44-52) ; le libelle et « Utilisable en saisie » se modifient, le reste se lit.
/// L'id d'une ligne est « &lt;TABLE&gt;|&lt;code&gt; » : les trois tables ont chacune leurs codes.
/// VALEURS : dans l'ordre des Colonnes (Code, Libellé, Table, Utilisable en saisie).
/// </summary>
// fix-ok: mesure au banc le 27/11/2026 (EcransGeneresBaseCheck, « la fiche PC_ETAT|G14C a les sections et champs du descripteur ;
// fautifs : Champs de la table > Régime… ») -- la fiche rendait une section « Champs de la table » dont les champs varient par table,
// et le descripteur n'en declarait aucun : une section par table, ses champs declares (Configurer.cs), « Table de la zone » distinct
// de la colonne « Table ».
internal sealed class LecteurTablesReference : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_GTABREF";

    internal const string ColCode = "Code", ColLibelle = "Libellé", ColTable = "Table", ColSaisie = "Utilisable en saisie";
    internal const string NonSaisissable = "(! Non saisissable) ";

    /// <summary>
    /// Une table de reference de la V2 : son nom, le libelle de sa liste (3e argument de CBO_TableRef), sa description (le groupe), le
    /// prefixe de ses colonnes, la longueur de son code et de son libelle (RigBaseGreffe), et si RIG v2 y cree un enregistrement
    /// (CBO_TableRef.NouvelEnregistrement, FactoryHelper.cs:71-89).
    /// </summary>
    internal sealed record TableRef(string Nom, string Liste, string Groupe, string Valeur, string Libelle, string Saisie, int LongueurCode,
                                    int LongueurLibelle, bool Creation, string? DateModif, string? AuteurModif);

    internal static readonly TableRef[] Tables =
    [
        new("PC_ETAT", "PC_ETAT", "État de la procédure collective", "R_PCETAT_VALEUR", "R_PCETAT_LIBELLE_ECRAN", "R_PCETAT_UTILISABLE_SAISIE",
            20, 200, Creation: true, DateModif: null, AuteurModif: null),
        new("PC_ETAT_ZONE", "PC_ETAT_ZONE", "Zones à afficher selon l’état de la procédure collective", "R_PCETATZONE_VALEUR",
            "R_PCETATZONE_LIBELLE_ECRAN", "R_PCETATZONE_UTILISABLE_SAISIE", 10, 50, Creation: true, "R_PCETATZONE_DATE_MODIF",
            "R_PCETATZONE_AUTEUR_MODIF"),
        new("DEMAT_CFEC_AJMJ_TYPE_DEPOT", "TYPE_DEPOT_AJMJ", "Types des dépôts envoyés par les AJMJ", "CFECAJTD_VALEUR", "CFECAJTD_LIBELLE_ECRAN",
            "CFECAJTD_UTILISABLE_SAISIE", 10, 500, Creation: false, "CFECAJTD_DATE_MODIFICATION", "CFECAJTD_AUTEUR_MODIFICATION"),
    ];

    /// <summary>La table d'un nom saisi : son nom V2, le libelle de sa liste ou sa description, sans casse ; null sinon.</summary>
    internal static TableRef? Table(string saisie) => Tables.FirstOrDefault(t =>
        string.Equals(t.Nom, saisie, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Liste, saisie, StringComparison.OrdinalIgnoreCase)
        || string.Equals(t.Groupe, saisie, StringComparison.OrdinalIgnoreCase));

    /// <summary>« PC_ETAT|01 » -> (PC_ETAT, « 01 ») ; null si l'id n'est pas lisible.</summary>
    internal static (TableRef Table, string Code)? Id(string id)
    {
        var i = id.IndexOf('|', StringComparison.Ordinal);
        return i > 0 && Table(id[..i]) is { } t && i < id.Length - 1 ? (t, id[(i + 1)..]) : null;
    }

    public IReadOnlyCollection<string> Criteres { get; } = [ColTable, ColCode, ColLibelle];

    /// <summary>Les trois tables l'une sous l'autre : chaque partie, la requete de FactoryHelper (code, libelle, saisissable).</summary>
    private static string Requete(string filtres) => $"""
        SELECT TOP (@plafond) l.TableRef, l.Rang, l.Valeur, l.Libelle, l.Saisie, COUNT(*) OVER () AS Total
        FROM ({string.Join(" UNION ALL ", Tables.Select((t, i) => $"""
            SELECT '{t.Nom}' AS TableRef, {i} AS Rang, CAST({t.Valeur} AS varchar(20)) AS Valeur, CAST({t.Libelle} AS varchar(500)) AS Libelle,
                   CAST({t.Saisie} AS bit) AS Saisie
            FROM v2.{t.Nom}
            """))}) AS l
        WHERE 1 = 1 {filtres}
        ORDER BY l.Rang, l.Valeur
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        // La recherche de la V2 : la colonne RECHERCHE de la liste, « code libelle » (FactoryHelper.cs:9).
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("ISNULL(l.Valeur, '') + ' ' + ISNULL(l.Libelle, '') LIKE @texte", ("@texte", FormeLigne.Contient(recherche.Texte)));
        if (recherche.Critere(ColTable) is { } table)
        {
            var noms = Tables.Where(t => t.Nom.Contains(table, StringComparison.OrdinalIgnoreCase) || t.Liste.Contains(table, StringComparison.OrdinalIgnoreCase)
                                         || t.Groupe.Contains(table, StringComparison.OrdinalIgnoreCase)).Select(t => $"'{t.Nom}'").ToArray();
            f.Ajouter(noms.Length == 0 ? "1 = 0" : $"l.TableRef IN ({string.Join(", ", noms)})");
        }
        if (recherche.Critere(ColCode) is { } code) f.Ajouter("l.Valeur LIKE @code", ("@code", FormeLigne.Contient(code)));
        if (recherche.Critere(ColLibelle) is { } libelle) f.Ajouter("l.Libelle LIKE @libelle", ("@libelle", FormeLigne.Contient(libelle)));
        return Lire(baseGreffe, f, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete(f.EnSuite), Ligne, ct, f.Parametres(new SqlParameter("@plafond", plafond)));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var t = Table(FormeLigne.Texte(r, "TableRef"))!;
        var code = FormeLigne.Texte(r, "Valeur");
        var libelle = FormeLigne.Texte(r, "Libelle");
        var saisie = !r.IsDBNull(r.GetOrdinal("Saisie")) && r.GetBoolean(r.GetOrdinal("Saisie"));
        var affiche = (saisie ? "" : NonSaisissable) + (libelle.Length > 0 ? libelle : code);
        return new LigneLue(affiche, FormeLigne.Joindre($"code {code}", t.Liste, saisie ? null : "non saisissable"), t.Groupe,
            libelle.Length == 0 ? "AFaire" : saisie ? "Validee" : "EnCours", [])
        {
            Id = $"{t.Nom}|{code}",
            Valeurs = [code, libelle, t.Liste, saisie ? "oui" : "non"],
        };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (Id(id) is not { } cle) return null;
        var (t, code) = cle;
        await using var r = await LectureV2.LireAsync(baseGreffe, $"SELECT * FROM v2.{t.Nom} WHERE {t.Valeur} = @c", ct, new SqlParameter("@c", code));
        if (!await r.ReadAsync(ct)) return null;
        string V(string colonne)
        {
            int i;
            try { i = r.GetOrdinal(colonne); } catch (IndexOutOfRangeException) { return ""; }
            if (r.IsDBNull(i)) return "";
            return r.GetValue(i) switch
            {
                bool b => b ? "oui" : "non",
                DateTime d => d.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                var v => Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? "",
            };
        }
        static ChampFiche M(string libelle, string valeur) => new(libelle, valeur) { Modifiable = true };
        static ChampFiche L(string libelle, string valeur) => new(libelle, valeur);
        var libelle = V(t.Libelle);
        var commun = new SectionFiche("Enregistrement", [L(ColTable, t.Liste), L(ColCode, code), M(ColLibelle, libelle), M(ColSaisie, V(t.Saisie))]);
        SectionFiche propre = t.Nom switch
        {
            // OPE_AFFICHE_SQLBASE_PC_ETAT.cs:64-82 : regime, cases exclusives (ouverture / cloture / conversion / extension), resolution du
            // plan, code cloture, mise a jour de la PC, a sauver, audience future, regime des statistiques, code procedure.
            "PC_ETAT" => new("Champs d'un état de PC",
            [
                L("Régime", V("R_PCETAT_MODALITE_REGIME")), L("Modalité PA", V("R_PCETAT_MODALITE_PA")), L("Modalité AJ", V("R_PCETAT_MODALITE_AJ")),
                L("Ouverture", V("R_PCETAT_OUVERTURE")), L("Clôture", V("R_PCETAT_CLOTURE")), L("Conversion LJ", V("R_PCETAT_CONVERSION_LJ")),
                L("Extension", V("R_PCETAT_EXTENSION")), L("Résolution du plan", V("R_PCETAT_RESOLUTION_PLAN")), L("Code clôture", V("R_PCETAT_CODE_CLOTURE")),
                L("Mise à jour de la PC", V("R_PCETAT_MAJ_PC")), L("À sauver", V("R_PCETAT_ASAUVER")), L("Audience future", V("R_PCETAT_AUD_FUTURE")),
                L("Régime PC (statistiques)", V("R_PCETAT_REGIME_PC_STATS")), L("Code procédure", V("R_PCETAT_CODE_PROC")),
            ]),
            // OPE_AFFICHE_SQLBASE_PC_ETAT_ZONE.cs:75-95 : enregistrement, edition, table / attribut / type ; l'etat par son libelle
            // de la liste V2 (eTableRefEtat, SousOPE_HISTORIQUE.cs:39-47), la date de desactivation au format d'un champ date :
            // « Dupliquer » une zone reprend ces valeurs dans ses champs (FormeReferentiel.tsx).
            "PC_ETAT_ZONE" => new("Champs d'une zone",
            [
                L("Synonyme", V("R_PCETATZONE_SYNONYME")), L("ISO", V("R_PCETATZONE_ISO")), L("Abrégé pour édition", V("R_PCETATZONE_ABREGE_EDIT")),
                L("Simplifié pour édition", V("R_PCETATZONE_SIMPLIFIE_EDIT")), L("Développé pour édition", V("R_PCETATZONE_DEVELOPPE_EDIT")),
                L("Exhaustif pour édition", V("R_PCETATZONE_EXHAUSTIF_EDIT")), L("Table de la zone", V("R_PCETATZONE_TABLE")), L("Attribut", V("R_PCETATZONE_ATTRIBUT")),
                L("Type", V("R_PCETATZONE_TYPE")), L("État", EtatZone(V("R_PCETATZONE_ETAT"))),
                L("Date de désactivation", r["R_PCETATZONE_DATE_DESACTIVATION"] is DateTime fin ? fin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""),
            ]),
            // OPE_AFFICHE_SQLBASE_DEMAT_CFEC_AJMJ_TYPE_DEPOT.cs:44-52 : evenement de procedure, historique.
            _ => new("Champs d'un type de dépôt",
            [
                L("Évènement de procédure", V("CFECAJTD_CODE_EVE_PRO")),
                L("Création", FormeLigne.Joindre(V("CFECAJTD_DATE_CREATION"), V("CFECAJTD_AUTEUR_CREATION"))),
                L("Modification", FormeLigne.Joindre(V("CFECAJTD_DATE_MODIFICATION"), V("CFECAJTD_AUTEUR_MODIFICATION"))),
            ]),
        };
        // La creation et la derniere modification d'une zone (SousOPE_HISTORIQUE), a part : RIG les pose, la copie ne les saisit pas.
        SectionFiche[] sections = t.Nom == "PC_ETAT_ZONE"
            ? [commun, propre, new("Historique d'une zone",
              [
                  L("Création", FormeLigne.Joindre(V("R_PCETATZONE_DATE_CREATION"), V("R_PCETATZONE_AUTEUR_CREATION"))),
                  L("Modification", FormeLigne.Joindre(V("R_PCETATZONE_DATE_MODIF"), V("R_PCETATZONE_AUTEUR_MODIF"))),
              ])]
            : [commun, propre];
        return new FicheEcran(Procedure, id, libelle.Length > 0 ? libelle : code, sections);
    }

    /// <summary>Les etats d'une table de reference, dans l'ordre de la liste RIG v2 : la valeur enregistree est le rang
    /// (eTableRefEtat, RigMetier\TableRef\TableRefTypes.cs:5-16 ; _ChargerComboEtats, SousOPE_HISTORIQUE.cs:39-47).</summary>
    internal static readonly string[] EtatsZone =
        ["En test", "En vigueur pour tout", "En vigueur si maj après création", "Applicable si maj avant archivage", "Inactif"];

    /// <summary>Le libelle d'un etat enregistre (« 1 » -> « En vigueur pour tout ») ; une valeur hors liste reste telle quelle.</summary>
    internal static string EtatZone(string valeur)
        => int.TryParse(valeur, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n < EtatsZone.Length ? EtatsZone[n] : valeur;
}
