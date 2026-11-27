using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Dates de travail » (PROC_GESTDATE) : les dates de travail ACTIVES du greffe, table DATE_TRAVAIL. Mode d'emploi : LecteurEcran.cs.
/// Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026. Modifier : EcrivainDatesTravail.cs (la V2 n'ajoute ni ne supprime de date).
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_GESTDATE.
///   - LA LISTE : ETP_CATEGORIE.DoChargerDonnees (l. 10-12) -- DateTravail.GetAllDateTravailActive, « SELECT * FROM DATE_TRAVAIL WHERE
///     DTTRV_ACTIF = 1 » (Outils\DLL\RigMetier\Divers\DateTravail.cs:48) ; chaque date a son bloc, titre « code - libelle »
///     (OPE_DATE_TRAVAIL.LinkDonnees, l. 40), la date de travail, la date de fraicheur et leurs deux cases « automatique » (l. 41-44).
///   - LES GROUPES : « RCS et endettement » -- les codes WINS et DATW, les seuls que la V2 controle (« FDDA 12660 : Interdire date
///     superieure (juste pour RCS et Endett pour l'instant) », OPE_DATE_TRAVAIL.DoVerifier, l. 49-50) ; « Autres dates ».
///   - L'ETAT, la regle de DoVerifier (l. 50-70) sur la date du jour (le parametre aujourdhui) : WINS ou DATW dont la date de travail
///     depasse aujourd'hui -> AFaire « Date à corriger » (l'erreur bloquante de la V2) ; WINS ou DATW dont la date de fraicheur depasse
///     aujourd'hui -> EnCours « Fraîcheur à vérifier » (l'avertissement « très dangereux ») ; sinon Validee « À jour ».
///   - ORDRE : par code (la V2 garde l'ordre de la requete, sans ORDER BY : la V3 fixe le code pour un ordre stable).
/// VALEURS : dans l'ordre des Colonnes (Code, Libellé, Date de travail, Date de fraîcheur, Date de travail automatique, Date de fraîcheur
/// automatique, Ancienne date de travail).
/// </summary>
internal sealed class LecteurDatesTravail : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_GESTDATE";

    internal const string Controlees = "RCS et endettement", Autres = "Autres dates";
    internal const string ColCode = "Code", ColLibelle = "Libellé", ColDate = "Date de travail", ColFraicheur = "Date de fraîcheur",
                          ColAuto = "Date de travail automatique", ColAutoFraicheur = "Date de fraîcheur automatique",
                          ColAncienne = "Ancienne date de travail";

    public IReadOnlyCollection<string> Criteres { get; } = [ColCode, ColLibelle];

    /// <summary>Les codes que la V2 controle (OPE_DATE_TRAVAIL.cs:50).</summary>
    internal static bool Controlee(string code) => code is "WINS" or "DATW";

    private const string Requete = """
        SELECT TOP (@plafond) d.DTTRV_CODE_DATE_TRAVAIL, d.DTTRV_LIBELLE, d.DTTRV_DATE_TRAVAIL, d.DTTRV_DATE_FRAICHEUR, d.DTTRV_AUTOMATIQUE,
               d.DTTRV_AUTOMATIQUE_FRAICHEUR, d.DTTRV_ANCIENNE_DATE_TRAVA, COUNT(*) OVER () AS Total
        FROM v2.DATE_TRAVAIL AS d
        WHERE d.DTTRV_ACTIF = 1 /*filtres*/
        ORDER BY d.DTTRV_CODE_DATE_TRAVAIL
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), aujourdhui, plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("d.DTTRV_CODE_DATE_TRAVAIL LIKE @texte OR d.DTTRV_LIBELLE LIKE @texte", ("@texte", FormeLigne.Contient(recherche.Texte)));
        if (recherche.Critere(ColCode) is { } code) f.Ajouter("d.DTTRV_CODE_DATE_TRAVAIL LIKE @code", ("@code", FormeLigne.Contient(code)));
        if (recherche.Critere(ColLibelle) is { } libelle) f.Ajouter("d.DTTRV_LIBELLE LIKE @libelle", ("@libelle", FormeLigne.Contient(libelle)));
        return Lire(baseGreffe, f, aujourdhui, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), r => Ligne(r, aujourdhui.Date), ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));

    private static LigneLue Ligne(SqlDataReader r, DateTime aujourdhui)
    {
        var code = FormeLigne.Texte(r, "DTTRV_CODE_DATE_TRAVAIL");
        var libelle = FormeLigne.Texte(r, "DTTRV_LIBELLE");
        var date = FormeLigne.DateDe(r, "DTTRV_DATE_TRAVAIL");
        var fraicheur = FormeLigne.DateDe(r, "DTTRV_DATE_FRAICHEUR");
        var auto = Convert.ToBoolean(r["DTTRV_AUTOMATIQUE"], CultureInfo.InvariantCulture);
        var autoFraicheur = Convert.ToBoolean(r["DTTRV_AUTOMATIQUE_FRAICHEUR"], CultureInfo.InvariantCulture);
        var controlee = Controlee(code);
        var etat = controlee && date > aujourdhui ? "AFaire" : controlee && fraicheur > aujourdhui ? "EnCours" : "Validee";
        var precisions = FormeLigne.Joindre($"code {code}", $"travail {FormeLigne.Date(date)}",
            fraicheur is null ? "fraîcheur non renseignée" : $"fraîcheur {FormeLigne.Date(fraicheur)}", auto ? "automatique" : null);
        return new LigneLue(libelle.Length > 0 ? libelle : code, precisions, controlee ? Controlees : Autres, etat, [])
        {
            Id = code,
            Valeurs = [code, libelle, FormeLigne.DateFiche(date), FormeLigne.DateFiche(fraicheur), FormeLigne.Case(auto), FormeLigne.Case(autoFraicheur),
                       FormeLigne.DateFiche(FormeLigne.DateDe(r, "DTTRV_ANCIENNE_DATE_TRAVA"))],
        };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        await using var r = await LectureV2.LireAsync(baseGreffe, """
            SELECT DTTRV_CODE_DATE_TRAVAIL, DTTRV_LIBELLE, DTTRV_DATE_TRAVAIL, DTTRV_DATE_FRAICHEUR, DTTRV_AUTOMATIQUE, DTTRV_AUTOMATIQUE_FRAICHEUR,
                   DTTRV_ANCIENNE_DATE_TRAVA
            FROM v2.DATE_TRAVAIL WHERE DTTRV_CODE_DATE_TRAVAIL = @id AND DTTRV_ACTIF = 1
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        static ChampFiche M(string libelle, string valeur) => new(libelle, valeur) { Modifiable = true };
        var code = FormeLigne.Texte(r, "DTTRV_CODE_DATE_TRAVAIL");
        var libelle = FormeLigne.Texte(r, "DTTRV_LIBELLE");
        return new FicheEcran(Procedure, id, $"{code} - {libelle}",
        [
            new("Date de travail", [new ChampFiche(ColCode, code), new ChampFiche(ColLibelle, libelle),
                                    M(ColDate, FormeLigne.DateFiche(FormeLigne.DateDe(r, "DTTRV_DATE_TRAVAIL"))),
                                    M(ColAuto, FormeLigne.Case(Convert.ToBoolean(r["DTTRV_AUTOMATIQUE"], CultureInfo.InvariantCulture))),
                                    new ChampFiche(ColAncienne, FormeLigne.DateFiche(FormeLigne.DateDe(r, "DTTRV_ANCIENNE_DATE_TRAVA")))]),
            new("Date de fraîcheur", [M(ColFraicheur, FormeLigne.DateFiche(FormeLigne.DateDe(r, "DTTRV_DATE_FRAICHEUR"))),
                                      M(ColAutoFraicheur, FormeLigne.Case(Convert.ToBoolean(r["DTTRV_AUTOMATIQUE_FRAICHEUR"], CultureInfo.InvariantCulture)))]),
        ]);
    }
}
