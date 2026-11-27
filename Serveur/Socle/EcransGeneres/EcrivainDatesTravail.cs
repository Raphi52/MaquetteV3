using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurDatesTravail;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Dates de travail » (PROC_GESTDATE) : MODIFIER une date de travail, dans la grille -- la V2 n'en ajoute ni n'en supprime (elle ne
/// montre que des blocs de dates existantes, ETP_CATEGORIE.cs:10-12). Mode d'emploi : EcrivainEcran.cs ; les lignes :
/// LecteurDatesTravail.cs. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026.
///
/// SOURCE (ancien RIG) : OPE_DATE_TRAVAIL (D:\RigApplication\Source\RIG\DLL\Processus\PROC_GESTDATE), messages au mot pres :
///   - le code et le libelle ne se saisissent pas (titre du bloc, LinkDonnees, l. 40) ; la date de travail est obligatoire (colonne NOT NULL) ;
///   - VERIFIER (DoVerifier, l. 48-72), pour WINS et DATW seulement (« juste pour RCS et Endett ») : une date de travail apres aujourd'hui
///     -> refus « La date de travail ne peut pas être supérieure à la date du jour » (ErreurValidation Erreur) ; une date de fraicheur
///     apres aujourd'hui -> AVERTISSEMENT (Warning : l'enregistrement passe) « Attention : Il est très dangereux que la date de fraîcheur
///     soit supérieure à la date de travail. », repris dans le message de l'ecran ;
///   - ENREGISTRER (DoSauve, l. 77-101) : si la date de travail change, l'ancienne va dans DTTRV_ANCIENNE_DATE_TRAVA (l. 80-82) ; les deux
///     cases « automatique » s'ecrivent (l. 83-84) ; le nom de la demande dit chaque changement « ancien => nouveau » (l. 88-100) : la V3
///     le met dans le message de l'ecran et au journal des gestes (avant / apres).
/// NON REPRIS (nommes) : les commandes d'etat d'endettement en attente (WINS : CbEditCommande, l. 104-116) et leurs editions (KBIS,
/// certificat de non-faillite, recapitulatif, EditerEtatEndettementCommande, l. 134-...) -- des editions, que les ecrans generes ne
/// produisent pas ; la demande RIG v2 du processus -- une trace, que le journal des gestes remplace (decision du plan).
/// TRACE : famille « reglage », « a modifie ».
/// </summary>
internal sealed class EcrivainDatesTravail : IEcrivainEcran
{
    public string Procedure => "PROC_GESTDATE";
    public string Ecran => "Dates de travail";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Modifier];

    internal const string DateFuture = "La date de travail ne peut pas être supérieure à la date du jour";
    internal const string FraicheurFuture = "Attention : Il est très dangereux que la date de fraîcheur soit supérieure à la date de travail.";

    private sealed record DateTravail(string Code, string Libelle, DateTime Date, DateTime? Fraicheur, bool Auto, bool AutoFraicheur, DateTime? Ancienne);

    private static string? Cellule(GesteEnCours geste, string colonne) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v.Trim() : null;

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var code = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (await LireAsync(geste, code, ct) is not { } avant)
            return IssueGeste.Refuser($"Date de travail introuvable dans la base du greffe ({code}).");
        if (Cellule(geste, ColCode) is { } c && c != avant.Code || Cellule(geste, ColLibelle) is { } l && l != avant.Libelle)
            return IssueGeste.Refuser("Le code et le libellé d'une date de travail ne se modifient pas (RIG v2 les montre en titre).");
        if (Cellule(geste, ColAncienne) is { } anc && anc != FormeLigne.DateFiche(avant.Ancienne))
            return IssueGeste.Refuser("L'ancienne date de travail ne se saisit pas : RIG v2 l'écrit quand la date de travail change.");
        DateTime date = avant.Date;
        if (Cellule(geste, ColDate) is { } d)
        {
            if (d.Length == 0) return IssueGeste.Refuser($"« {ColDate} » est obligatoire.");
            if (CriteresConsultation.Date(d) is not { } lue) return IssueGeste.Refuser($"« {ColDate} » : une date (jj/mm/aaaa) ({d}).");
            date = lue;
        }
        DateTime? fraicheur = avant.Fraicheur;
        if (Cellule(geste, ColFraicheur) is { } f)
        {
            if (f.Length == 0) fraicheur = null;
            else if (CriteresConsultation.Date(f) is { } lue) fraicheur = lue;
            else return IssueGeste.Refuser($"« {ColFraicheur} » : une date (jj/mm/aaaa) ({f}).");
        }
        var apres = avant with
        {
            Date = date, Fraicheur = fraicheur,
            Auto = Cellule(geste, ColAuto) is { } a ? a == "oui" : avant.Auto,
            AutoFraicheur = Cellule(geste, ColAutoFraicheur) is { } af ? af == "oui" : avant.AutoFraicheur,
        };
        // DoSauve (l. 80-82) : l'ancienne date de travail, si elle change.
        if (apres.Date != avant.Date) apres = apres with { Ancienne = avant.Date };
        if (apres == avant) return IssueGeste.Refuser("Rien à enregistrer : aucune valeur ne change.");
        var aujourdhui = geste.Maintenant.Date;
        var avertissement = "";
        if (Controlee(avant.Code))
        {
            if (apres.Date > aujourdhui) return IssueGeste.Refuser(DateFuture);
            if (apres.Fraicheur > aujourdhui) avertissement = " " + FraicheurFuture;
        }
        await geste.EcrireAsync("""
            UPDATE v2.DATE_TRAVAIL SET DTTRV_DATE_TRAVAIL = @date, DTTRV_DATE_FRAICHEUR = @fraicheur, DTTRV_AUTOMATIQUE = @auto,
                DTTRV_AUTOMATIQUE_FRAICHEUR = @autoFraicheur, DTTRV_ANCIENNE_DATE_TRAVA = @ancienne
            WHERE DTTRV_CODE_DATE_TRAVAIL = @code
            """, ct, new SqlParameter("@date", apres.Date), new SqlParameter("@fraicheur", (object?)apres.Fraicheur ?? DBNull.Value),
            new SqlParameter("@auto", apres.Auto), new SqlParameter("@autoFraicheur", apres.AutoFraicheur),
            new SqlParameter("@ancienne", (object?)apres.Ancienne ?? DBNull.Value), new SqlParameter("@code", avant.Code));
        return IssueGeste.Ecrit($"Mise à jour {avant.Libelle} : {Changements(avant, apres)}{avertissement}", [avant.Code],
            new TraceGeste("reglage", $"la date de travail {avant.Code}", avant.Code, "a modifie", avant, apres));
    }

    /// <summary>Le nom de demande de la V2 (DoSauve, l. 88-100) : chaque changement « ancien => nouveau ».</summary>
    private static string Changements(DateTravail avant, DateTravail apres)
    {
        var morceaux = new List<string>();
        if (apres.Date != avant.Date) morceaux.Add($"Date travail {FormeLigne.Date(avant.Date)} => {FormeLigne.Date(apres.Date)}");
        if (apres.Fraicheur != avant.Fraicheur) morceaux.Add($"Date fraicheur {FormeLigne.Date(avant.Fraicheur)} => {FormeLigne.Date(apres.Fraicheur)}");
        if (apres.Auto != avant.Auto) morceaux.Add($"Date de travail automatique {(avant.Auto ? 1 : 0)} => {(apres.Auto ? 1 : 0)}");
        if (apres.AutoFraicheur != avant.AutoFraicheur)
            morceaux.Add($"Date de fraicheur automatique {(avant.AutoFraicheur ? 1 : 0)} => {(apres.AutoFraicheur ? 1 : 0)}");
        return string.Join("; ", morceaux) + ".";
    }

    private static async Task<DateTravail?> LireAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT DTTRV_CODE_DATE_TRAVAIL, DTTRV_LIBELLE, DTTRV_DATE_TRAVAIL, DTTRV_DATE_FRAICHEUR, DTTRV_AUTOMATIQUE, DTTRV_AUTOMATIQUE_FRAICHEUR,
                   DTTRV_ANCIENNE_DATE_TRAVA
            FROM v2.DATE_TRAVAIL WITH (UPDLOCK) WHERE DTTRV_CODE_DATE_TRAVAIL = @code AND DTTRV_ACTIF = 1
            """, ct, new SqlParameter("@code", code));
        if (!await r.ReadAsync(ct)) return null;
        return new DateTravail(FormeLigne.Texte(r, "DTTRV_CODE_DATE_TRAVAIL"), FormeLigne.Texte(r, "DTTRV_LIBELLE"),
            FormeLigne.DateDe(r, "DTTRV_DATE_TRAVAIL") ?? DateTime.MinValue, FormeLigne.DateDe(r, "DTTRV_DATE_FRAICHEUR"),
            r.GetBoolean(r.GetOrdinal("DTTRV_AUTOMATIQUE")), r.GetBoolean(r.GetOrdinal("DTTRV_AUTOMATIQUE_FRAICHEUR")),
            FormeLigne.DateDe(r, "DTTRV_ANCIENNE_DATE_TRAVA"));
    }
}
