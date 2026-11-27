using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Sceaux » (PROC_SCEAU) : les elements de fusion du greffe (sceaux, signatures, logos), ELEMENT_FUSION, et leur image en GED. Mode
/// d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026. Supprimer : EcrivainSceaux.cs.
///
/// SOURCE (ancien RIG) : PROC_SCEAU (D:\RigApplication\Source\RIG\DLL\Processus\PROC_SCEAU ; Detail OPE de proc_sceau.md, qui fait foi).
/// La liste : ElementFusion.GetAllElementFusion (Recherche\OPE_RECHERCHE.cs LoadLstElementFusion ; ElementFusion.Cache.cs:40-46), TOUS
/// les elements du greffe, sans filtre ; la V2 ne trie pas (l'ordre du cache) : l'ecran V3 trie par nom externe. Les colonnes de la V2
/// (InitColumn) : le fichier (gedRig.GDRG_NOM_FICHIER), le nom externe (ELMFS_REFERENCE), la hauteur, la largeur et le rognage en mm,
/// la date d'archivage (gedRig.GDRG_DATE_ARCHIVAGE) ; l'image est le document GED de numero ELMFS_NUM_DOCUMENT (ElementFusion.cs:40-51).
/// RECHERCHE : le champ cherche le nom externe ou le fichier ; criteres « Nom externe », « Fichier ».
/// GROUPES : hauteur de 24 mm -> « Hauteur de 24 mm » (la case « dimensions sceau lot 1 », cochee si la hauteur vaut HAUTEUR_SCEAU_MM = 24,
/// Edition\OPE_EDITION.cs:18, :268), sinon « Autre hauteur ».
/// ETAT : sans numero de document -> AFaire « À compléter » (« Le fichier est obligatoire », OPE_EDITION.cs:163-166) ; numero sans document
/// dans la GED du greffe -> EnCours « Image absente de la GED » ; sinon Validee « Enregistré ».
/// VALEURS : dans l'ordre des Colonnes (Nom externe, Fichier, Hauteur max, Largeur max, Rogner, Date archivage).
/// </summary>
internal sealed class LecteurSceaux : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_SCEAU";

    internal const string Hauteur24 = "Hauteur de 24 mm", AutreHauteur = "Autre hauteur";
    internal const string ColNom = "Nom externe", ColFichier = "Fichier";

    public IReadOnlyCollection<string> Criteres { get; } = [ColNom, ColFichier];

    private const string Requete = """
        SELECT TOP (@plafond) e.ELMFS_ID_ELMFS, e.ELMFS_REFERENCE, e.ELMFS_NUM_DOCUMENT, e.ELMFS_HAUTEUR_MAX_MM, e.ELMFS_LARGEUR_MAX_MM,
               e.ELMFS_ROGNER_MM, g.GDRG_NUM_DOCUMENT, g.GDRG_NOM_FICHIER, g.GDRG_DATE_ARCHIVAGE, COUNT(*) OVER () AS Total
        FROM v2.ELEMENT_FUSION AS e
        LEFT JOIN v2.GED_RIG AS g ON g.GDRG_NUM_DOCUMENT = e.ELMFS_NUM_DOCUMENT
        WHERE 1 = 1 /*filtres*/
        ORDER BY e.ELMFS_REFERENCE, e.ELMFS_ID_ELMFS
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("(e.ELMFS_REFERENCE LIKE @texte OR g.GDRG_NOM_FICHIER LIKE @texte)", ("@texte", FormeLigne.Contient(recherche.Texte)));
        if (recherche.Critere(ColNom) is { } nom) f.Ajouter("e.ELMFS_REFERENCE LIKE @nom", ("@nom", FormeLigne.Contient(nom)));
        if (recherche.Critere(ColFichier) is { } fichier) f.Ajouter("g.GDRG_NOM_FICHIER LIKE @fichier", ("@fichier", FormeLigne.Contient(fichier)));
        return Lire(baseGreffe, f, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));

    private static string Mm(SqlDataReader r, string colonne) => r[colonne] is DBNull ? "" : Convert.ToString(r[colonne], CultureInfo.InvariantCulture) ?? "";

    private static LigneLue Ligne(SqlDataReader r)
    {
        var nom = FormeLigne.Texte(r, "ELMFS_REFERENCE");
        var numero = FormeLigne.Texte(r, "ELMFS_NUM_DOCUMENT");
        var enGed = r["GDRG_NUM_DOCUMENT"] is not DBNull;
        var fichier = FormeLigne.Texte(r, "GDRG_NOM_FICHIER");
        var archive = FormeLigne.Date(FormeLigne.DateDe(r, "GDRG_DATE_ARCHIVAGE"));
        var (hauteur, largeur, rogner) = (Mm(r, "ELMFS_HAUTEUR_MAX_MM"), Mm(r, "ELMFS_LARGEUR_MAX_MM"), Mm(r, "ELMFS_ROGNER_MM"));
        // La regle d'etat (en tete du fichier).
        var etat = numero.Length == 0 ? "AFaire" : !enGed ? "EnCours" : "Validee";
        var precisions = FormeLigne.Joindre($"{hauteur} × {largeur} mm", rogner is "" or "0" ? null : $"rogné de {rogner} mm",
            numero.Length == 0 ? "sans image" : !enGed ? "image absente de la GED" : FormeLigne.Joindre(fichier, archive.Length > 0 ? $"archivé le {archive}" : null));
        return new LigneLue(nom, precisions, hauteur == "24" ? Hauteur24 : AutreHauteur, etat, [])
        {
            Id = FormeLigne.Texte(r, "ELMFS_ID_ELMFS"),
            Valeurs = [nom, fichier, hauteur, largeur, rogner, archive],
        };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (CriteresConsultation.Entier(id) is not { } n) return null;
        await using var r = await LectureV2.LireAsync(baseGreffe, """
            SELECT e.ELMFS_REFERENCE, e.ELMFS_NUM_DOCUMENT, e.ELMFS_HAUTEUR_MAX_MM, e.ELMFS_LARGEUR_MAX_MM, e.ELMFS_ROGNER_MM,
                   g.GDRG_NUM_DOCUMENT, g.GDRG_NOM_FICHIER, g.GDRG_DATE_ARCHIVAGE
            FROM v2.ELEMENT_FUSION AS e LEFT JOIN v2.GED_RIG AS g ON g.GDRG_NUM_DOCUMENT = e.ELMFS_NUM_DOCUMENT
            WHERE e.ELMFS_ID_ELMFS = @id
            """, ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        var nom = FormeLigne.Texte(r, "ELMFS_REFERENCE");
        var hauteur = Mm(r, "ELMFS_HAUTEUR_MAX_MM");
        var numero = FormeLigne.Texte(r, "ELMFS_NUM_DOCUMENT");
        return new FicheEcran(Procedure, id, nom,
        [
            new("Référence", [new ChampFiche(ColNom, nom)]),
            new("Dimensions", [new ChampFiche("Hauteur max", hauteur), new ChampFiche("Largeur max", Mm(r, "ELMFS_LARGEUR_MAX_MM")),
                               new ChampFiche("Rogner", Mm(r, "ELMFS_ROGNER_MM")), new ChampFiche("Dimensions du lot 1", FormeLigne.Case(hauteur == "24"))]),
            new("Image", [new ChampFiche(ColFichier, r["GDRG_NUM_DOCUMENT"] is DBNull ? (numero.Length == 0 ? "sans image" : $"document {numero} absent de la GED") : FormeLigne.Texte(r, "GDRG_NOM_FICHIER")),
                          new ChampFiche("Date archivage", FormeLigne.Date(FormeLigne.DateDe(r, "GDRG_DATE_ARCHIVAGE")))]),
        ]);
    }
}
