using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Cabinets » (PROC_CABINET) : les cabinets du greffe -- les lignes d'AUDIENCE_CABINET qui ne sont PAS des audiences. Mode d'emploi :
/// LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026. Ajouter, Modifier : EcrivainCabinets.cs (la V2 ne supprime pas
/// de cabinet dans PROC_CABINET).
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_CABINET ; AudienceCabinet (Outils\DLL\RigMetier\JUDICIAIRE\
/// AudienceCabinet.cs).
///   - LE FILTRE : un cabinet est une ligne AUDNC_AUDIENCE = 0 (AudienceCabinet.CreateCabinet, l. 1280 ; GetCabinet, l. 1055, 1088) QUI A
///     UN TYPE : AUDNC_TYPE_AUD_CAB est « varchar(5) NOT NULL » dans RIG_DEV (AUDIENCE_CABINET.cs de RigBaseGreffe) et obligatoire a la
///     saisie (OPE_CREATION_CABINET.Designer.cs:105) -- un cabinet de RIG v2 en a toujours un. fix-ok: mesure au banc le 27/11/2026 -- sans
///     cette condition, 15 lignes de session sans type posees par d'autres jeux (AUDNC_AUDIENCE = 0, PROC_GCONV, PROC_LISTE_DELIBERES...)
///     entraient dans l'ecran, dont une datee « le 3 du mois » (PROC_LISTE_DELIBERES.sql:65) qui change d'ordre et de groupe chaque jour.
///   - LES CHAMPS (OPE_CREATION_CABINET.DoInitZoneSaisie, l. 25-30) : date et heure d'audience, type de cabinet (TYPE_AUD_CAB), code
///     cabinet (CODE_CABINET), juge (JUGE), greffier (GREFFIER_ASSERMENTE) ; le classement admis (AUDNC_TYPE_CLASSEMENT_ADMIS, l. 71).
///   - LES GROUPES : « À venir » / « Passés » -- la date du cabinet contre la date du jour, la frontiere que la V2 applique aux invitations
///     du juge et du greffier (« if (Cabinet.AUDNC_DATE.Valeur > DateTime.Today) », l. 148, 192).
///   - L'ETAT : le type, la date et l'heure sont obligatoires (MetierObligatoire, OPE_CREATION_CABINET.Designer.cs:86, 95, 105) : l'un
///     d'eux manque -> AFaire « À saisir » ; sans juge -> EnCours « En saisie » (le juge se choisit dans la liste, l. 29) ; sinon Validee.
///   - ORDRE : du plus recent au plus ancien (date, heure).
/// VALEURS : dans l'ordre des Colonnes (Date d'audience, Heure d'audience, Type de cabinet, Code cabinet, Juge, Greffier).
/// </summary>
internal sealed class LecteurCabinets : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_CABINET";

    internal const string AVenir = "À venir", Passes = "Passés";
    internal const string ColDate = "Date d'audience", ColHeure = "Heure d'audience", ColType = "Type de cabinet", ColCode = "Code cabinet",
                          ColJuge = "Juge", ColGreffier = "Greffier";

    public IReadOnlyCollection<string> Criteres { get; } = [ColDate, ColType, ColJuge];

    private const string Colonnes = """
        a.AUDNC_ID_ADNC, a.AUDNC_DATE, a.AUDNC_HEURE, a.AUDNC_TYPE_AUD_CAB, t.R_TYPEAUDCAB_LIBELLE_ECRAN AS TypeLibelle, a.AUDNC_CODE_CABINET,
        c.R_CODECABINE_LIBELLE_ECRAN AS CodeLibelle, a.AUDNC_ID_JUG, j.JUG_ABREGE, CONCAT_WS(' ', j.JUG_NOM, j.JUG_PRENOM) AS JugeNom, a.AUDNC_ID_GRASS,
        g.GRASS_ABREGE, CONCAT_WS(' ', g.GRASS_NOM, g.GRASS_PRENOM) AS GreffierNom, a.AUDNC_TYPE_CLASSEMENT_ADMIS
        """;

    private const string Jointures = """
        FROM v2.AUDIENCE_CABINET AS a
        LEFT JOIN v2.TYPE_AUD_CAB AS t ON t.R_TYPEAUDCAB_VALEUR = a.AUDNC_TYPE_AUD_CAB
        LEFT JOIN v2.CODE_CABINET AS c ON c.R_CODECABINE_VALEUR = a.AUDNC_CODE_CABINET
        LEFT JOIN v2.JUGE AS j ON j.JUG_ID_JUG = a.AUDNC_ID_JUG
        LEFT JOIN v2.GREFFIER_ASSERMENTE AS g ON g.GRASS_ID_GRASS = a.AUDNC_ID_GRASS
        """;

    // Un texte """ ... """ ne finit pas par un retour a la ligne : le blanc avant WHERE separe les jointures du filtre.
    private const string Requete = "SELECT TOP (@plafond) " + Colonnes + ", COUNT(*) OVER () AS Total " + Jointures + " " + """
        WHERE a.AUDNC_AUDIENCE = 0 AND a.AUDNC_TYPE_AUD_CAB <> '' /*filtres*/
        ORDER BY a.AUDNC_DATE DESC, a.AUDNC_HEURE DESC, a.AUDNC_ID_ADNC DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), aujourdhui, plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("a.AUDNC_CODE_CABINET LIKE @texte OR t.R_TYPEAUDCAB_LIBELLE_ECRAN LIKE @texte OR c.R_CODECABINE_LIBELLE_ECRAN LIKE @texte OR j.JUG_NOM LIKE @texte OR j.JUG_ABREGE LIKE @texte",
                ("@texte", FormeLigne.Contient(recherche.Texte)));
        if (recherche.Critere(ColDate) is { } date)
        {
            if (CriteresConsultation.Date(date) is { } d) f.Ajouter("a.AUDNC_DATE >= @jour AND a.AUDNC_DATE < DATEADD(day, 1, @jour)", ("@jour", d));
            else f.Ajouter("1 = 0");
        }
        if (recherche.Critere(ColType) is { } type)
            f.Ajouter("a.AUDNC_TYPE_AUD_CAB LIKE @type OR t.R_TYPEAUDCAB_LIBELLE_ECRAN LIKE @type", ("@type", FormeLigne.Contient(type)));
        if (recherche.Critere(ColJuge) is { } juge)
            f.Ajouter("j.JUG_NOM LIKE @juge OR j.JUG_ABREGE LIKE @juge", ("@juge", FormeLigne.Contient(juge)));
        return Lire(baseGreffe, f, aujourdhui, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), r => Ligne(r, aujourdhui.Date), ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));

    /// <summary>Le juge ou le greffier d'une ligne, comme sa liste de la V2 le montre : l'abrege, a defaut le nom.</summary>
    private static string Acteur(SqlDataReader r, string abrege, string nom)
        => FormeLigne.Texte(r, abrege) is { Length: > 0 } a ? a : FormeLigne.Texte(r, nom);

    private static LigneLue Ligne(SqlDataReader r, DateTime aujourdhui)
    {
        var date = FormeLigne.DateDe(r, "AUDNC_DATE");
        var heure = FormeLigne.Texte(r, "AUDNC_HEURE");
        var type = FormeLigne.Texte(r, "AUDNC_TYPE_AUD_CAB");
        var typeLibelle = FormeLigne.Texte(r, "TypeLibelle");
        var code = FormeLigne.Texte(r, "AUDNC_CODE_CABINET");
        var juge = Acteur(r, "JUG_ABREGE", "JugeNom");
        var greffier = Acteur(r, "GRASS_ABREGE", "GreffierNom");
        var etat = date is null || heure.Length == 0 || type.Length == 0 ? "AFaire" : juge.Length == 0 ? "EnCours" : "Validee";
        var nom = FormeLigne.Joindre(FormeLigne.Texte(r, "CodeLibelle") is { Length: > 0 } cl ? cl : code.Length > 0 ? code : typeLibelle,
            $"{FormeLigne.Date(date)} {(heure.Length > 0 ? "à " + heure : "")}".Trim());
        var precisions = FormeLigne.Joindre(typeLibelle.Length > 0 ? typeLibelle : type, juge.Length > 0 ? $"juge {juge}" : "juge à renseigner",
            greffier.Length > 0 ? $"greffier {greffier}" : null);
        return new LigneLue(nom, precisions, date > aujourdhui ? AVenir : Passes, etat, [])
        {
            Id = FormeLigne.Texte(r, "AUDNC_ID_ADNC"),
            Valeurs = [FormeLigne.DateFiche(date), heure, type, code, juge, greffier],
        };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (CriteresConsultation.Entier(id) is not { } n) return null;
        await using var r = await LectureV2.LireAsync(baseGreffe, "SELECT " + Colonnes + " " + Jointures + " WHERE a.AUDNC_ID_ADNC = @id AND a.AUDNC_AUDIENCE = 0 AND a.AUDNC_TYPE_AUD_CAB <> ''",
            ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        static ChampFiche M(string libelle, string valeur) => new(libelle, valeur) { Modifiable = true };
        var date = FormeLigne.DateDe(r, "AUDNC_DATE");
        var heure = FormeLigne.Texte(r, "AUDNC_HEURE");
        return new FicheEcran(Procedure, id, $"Cabinet du {FormeLigne.Date(date)} {heure}".Trim(),
        [
            new("Audience", [M(ColDate, FormeLigne.DateFiche(date)), M(ColHeure, heure)]),
            new("Type et code", [M(ColType, FormeLigne.Texte(r, "AUDNC_TYPE_AUD_CAB")), new ChampFiche("Libellé du type", FormeLigne.Texte(r, "TypeLibelle")),
                                 M(ColCode, FormeLigne.Texte(r, "AUDNC_CODE_CABINET")), new ChampFiche("Libellé du code", FormeLigne.Texte(r, "CodeLibelle"))]),
            new("Juge et greffier", [M(ColJuge, Acteur(r, "JUG_ABREGE", "JugeNom")), M(ColGreffier, Acteur(r, "GRASS_ABREGE", "GreffierNom"))]),
            new("Classement", [new ChampFiche("Classement admis", FormeLigne.Texte(r, "AUDNC_TYPE_CLASSEMENT_ADMIS"))]),
        ]);
    }
}
