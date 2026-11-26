using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Documents » (PROC_RECHERCHE_GED) : les documents de la GED du greffe, par categorie, et les proprietes de l'un d'eux.
/// Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026. Les categories : DocumentsGed.cs.
///
/// SOURCE (ancien RIG) : PROC_RECHERCHE_GED, OPE_GED (le Detail OPE de proc_recherche_ged.md fait foi). La requete de resultats
/// (_ActualiserResultats, OPE_GED.cs:367-378) : « SELECT DISTINCT TOP <N> GDRG_NUM_DOCUMENT, GDRG_EXTENSION,
/// GDRG_LOGIN_NT_EXTRACTION, GDRG_DATE_EXTRACTION, GDRG_DATE_ARCHIVAGE, GDRG_PATH_EXTRACTION, GDRG_NOM_FICHIER, <NOM> FROM
/// GED_RIG ... ORDER BY GDRG_LOGIN_NT_EXTRACTION DESC, GDRG_DATE_EXTRACTION DESC, GDRG_DATE_ARCHIVAGE DESC », N = 100 par defaut
/// (ult_IntTop), sans pagination (proc_recherche_ged.md:314-316). La V2 n'affiche rien tant qu'aucune categorie n'est choisie
/// (OPE_GED.cs:367-368) ; l'ecran V3 montre toutes les categories a la fois, chaque document dans SA categorie (DocumentsGed),
/// le critere « Catégorie » refaisant le choix de l'arbre. Le tri est celui de la V2 (les documents extraits en tete) ; le numero
/// departage.
///
/// RECHERCHE (IRechercheEcran) : « Catégorie » (une categorie, au libelle de l'ecran), « Numéro du document » (GDRG_NUM_DOCUMENT
/// =, la recherche exacte de ult_TbxNumGed, OPE_GED.cs:446) et « Login d'extraction » (GDRG_LOGIN_NT_EXTRACTION LIKE,
/// ult_tbxUtilisateur, OPE_GED.cs:456 ; la V2 n'en montrait que le premier document, un defaut nomme proc_recherche_ged.md:351,
/// la V3 les montre tous). Le champ de recherche cherche le numero, le nom de fichier ou le libelle (l'un OU l'autre).
///
/// REGLE D'ETAT (les colonnes d'extraction de GED_RIG, GedRig.Extract / UndoExtract, proc_recherche_ged.md:328-333) :
///   orphelin (sa reference ne le porte plus)       -> AFaire « Orphelin » : l'outil sert a les retrouver pour les nettoyer ;
///   extrait (GDRG_LOGIN_NT_EXTRACTION renseigne)  -> EnCours « Extrait » : reserve par un agent, pas encore re-archive ;
///   autre                                         -> Validee « Archivé ».
///
/// FICHE (IFicheEcran), Id = GDRG_NUM_DOCUMENT : « Propriétés » (OPE_PROPRIETES et la ligne de la V2) et « Extraction ». Les
/// gestes de la V2 -- extraire, recuperer la derniere version, archiver, annuler l'extraction, supprimer (GedRig.cs, tableau de
/// proc_recherche_ged.md:326-333) -- ne sont pas repris : ils lisent et ecrivent des fichiers sur le poste, que l'ecran V3 n'a pas.
/// </summary>
internal sealed class LecteurDocumentsGed : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_RECHERCHE_GED";

    internal const string CritereCategorie = "Catégorie", CritereNumero = "Numéro du document", CritereLogin = "Login d'extraction";

    internal const string SectionProprietes = "Propriétés", SectionExtraction = "Extraction";

    internal const string ChampNumero = "Numéro du document", ChampCategorie = "Catégorie", ChampLibelle = "Libellé",
                          ChampFichier = "Nom du fichier", ChampExtension = "Extension", ChampReference = "Référence",
                          ChampArchivage = "Date d'archivage", ChampArchivePar = "Archivé par", ChampTaille = "Taille",
                          ChampPages = "Nombre de pages", ChampLogin = "Login d'extraction", ChampDateExtraction = "Date d'extraction",
                          ChampRepertoire = "Répertoire d'extraction";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereCategorie, CritereNumero, CritereLogin];

    private const string Requete = $"""
        SELECT TOP (@plafond) l.*, COUNT(*) OVER () AS Total
        FROM (SELECT g.GDRG_NUM_DOCUMENT, g.GDRG_EXTENSION, g.GDRG_LOGIN_NT_EXTRACTION, g.GDRG_DATE_EXTRACTION, g.GDRG_DATE_ARCHIVAGE,
                     g.GDRG_NOM_FICHIER, g.GDRG_REFERENCE, {DocumentsGed.Categorie} AS Categorie, {DocumentsGed.Libelle} AS Libelle
              FROM v2.GED_RIG AS g
              WHERE 1 = 1 /*filtres*/) AS l
        WHERE 1 = 1 /*categorie*/
        ORDER BY l.GDRG_LOGIN_NT_EXTRACTION DESC, l.GDRG_DATE_EXTRACTION DESC, l.GDRG_DATE_ARCHIVAGE DESC, l.GDRG_NUM_DOCUMENT
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), null, plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter($"g.GDRG_NUM_DOCUMENT LIKE @texte OR g.GDRG_NOM_FICHIER LIKE @texte OR {DocumentsGed.Libelle} LIKE @texte",
                ("@texte", FormeLigne.Contient(recherche.Texte)));
        if (recherche.Critere(CritereNumero) is { } numero)
            f.Ajouter("g.GDRG_NUM_DOCUMENT = @numero", ("@numero", numero.Trim()));
        if (recherche.Critere(CritereLogin) is { } login)
            f.Ajouter("g.GDRG_LOGIN_NT_EXTRACTION LIKE @login", ("@login", FormeLigne.Contient(login)));
        // La categorie : son rang dans DocumentsGed.Categories (une categorie inconnue ne rend rien).
        int? categorie = recherche.Critere(CritereCategorie) is { } c
            ? Array.FindIndex(DocumentsGed.Categories, x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase)) : null;
        return Lire(baseGreffe, f, categorie, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, int? categorie, int plafond, CancellationToken ct)
    {
        var sql = Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal)
                         .Replace("/*categorie*/", categorie is null ? "" : " AND l.Categorie = @categorie", StringComparison.Ordinal);
        var autres = new List<SqlParameter> { new("@plafond", plafond) };
        if (categorie is { } k) autres.Add(new SqlParameter("@categorie", k));
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, f.Parametres([.. autres]));
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var numero = FormeLigne.Texte(r, "GDRG_NUM_DOCUMENT");
        var categorie = Convert.ToInt32(r["Categorie"], System.Globalization.CultureInfo.InvariantCulture);
        var login = FormeLigne.Texte(r, "GDRG_LOGIN_NT_EXTRACTION");
        var extension = FormeLigne.Texte(r, "GDRG_EXTENSION");
        var reference = FormeLigne.Texte(r, "GDRG_REFERENCE");
        var nom = DocumentsGed.Nom(FormeLigne.Texte(r, "Libelle"), FormeLigne.Texte(r, "GDRG_NOM_FICHIER"), numero);
        var precisions = FormeLigne.Joindre(
            $"Document {numero}",
            extension.Length > 0 ? extension.ToUpperInvariant() : null,
            $"archivé le {FormeLigne.Date(FormeLigne.DateDe(r, "GDRG_DATE_ARCHIVAGE"))}",
            login.Length > 0 ? $"extrait par {login} le {FormeLigne.Date(FormeLigne.DateDe(r, "GDRG_DATE_EXTRACTION"))}" : null,
            categorie == 7 ? $"référence supprimée ({reference})" : categorie == 8 ? reference : null);
        // La regle d'etat (en tete du fichier).
        var etat = categorie == 7 ? "AFaire" : login.Length > 0 ? "EnCours" : "Validee";
        return new LigneLue(nom, precisions, DocumentsGed.Categories[categorie], etat, []) { Id = numero };
    }

    // ------------------------------------------------------------------ LA FICHE

    private const string Fiche = $"""
        SELECT g.GDRG_NUM_DOCUMENT, g.GDRG_EXTENSION, g.GDRG_LOGIN_NT_EXTRACTION, g.GDRG_DATE_EXTRACTION, g.GDRG_DATE_ARCHIVAGE,
               g.GDRG_LOGIN_NT_ARCHIVAGE, g.GDRG_NOM_FICHIER, g.GDRG_REFERENCE, g.GDRG_PATH_EXTRACTION, g.GDRG_NB_PAGE,
               DATALENGTH(g.GDRG_FICHIER) AS Taille, {DocumentsGed.Categorie} AS Categorie, {DocumentsGed.Libelle} AS Libelle
        FROM v2.GED_RIG AS g
        WHERE g.GDRG_NUM_DOCUMENT = @id
        """;

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        await using var r = await LectureV2.LireAsync(baseGreffe, Fiche, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        var numero = FormeLigne.Texte(r, "GDRG_NUM_DOCUMENT");
        var libelle = FormeLigne.Texte(r, "Libelle");
        var fichier = FormeLigne.Texte(r, "GDRG_NOM_FICHIER");
        var categorie = Convert.ToInt32(r["Categorie"], System.Globalization.CultureInfo.InvariantCulture);
        var taille = r["Taille"] is DBNull ? (long?)null : Convert.ToInt64(r["Taille"], System.Globalization.CultureInfo.InvariantCulture);
        return new FicheEcran(Procedure, id, DocumentsGed.Nom(libelle, fichier, numero),
        [
            new SectionFiche(SectionProprietes,
            [
                new(ChampNumero, numero), new(ChampCategorie, DocumentsGed.Categories[categorie]), new(ChampLibelle, libelle),
                new(ChampFichier, fichier), new(ChampExtension, FormeLigne.Texte(r, "GDRG_EXTENSION")),
                new(ChampReference, FormeLigne.Texte(r, "GDRG_REFERENCE")),
                new(ChampArchivage, FormeLigne.DateFiche(FormeLigne.DateDe(r, "GDRG_DATE_ARCHIVAGE"))),
                new(ChampArchivePar, FormeLigne.Texte(r, "GDRG_LOGIN_NT_ARCHIVAGE")),
                new(ChampTaille, DocumentsGed.Taille(taille)), new(ChampPages, FormeLigne.Texte(r, "GDRG_NB_PAGE")),
            ]),
            new SectionFiche(SectionExtraction,
            [
                new(ChampLogin, FormeLigne.Texte(r, "GDRG_LOGIN_NT_EXTRACTION")),
                new(ChampDateExtraction, FormeLigne.DateFiche(FormeLigne.DateDe(r, "GDRG_DATE_EXTRACTION"))),
                new(ChampRepertoire, FormeLigne.Texte(r, "GDRG_PATH_EXTRACTION")),
            ]),
        ]);
    }
}
