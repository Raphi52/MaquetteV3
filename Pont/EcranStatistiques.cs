// fix-ok: pas un correctif -- lot 11 du plan-f4fb097f (28/11/2026) : le geste « Excel » (mesuré au 28/11 : PROC_STAT « geste non
// prouvé ») ; la troisième retouche replace son commentaire dans le <summary> de la classe (XML de documentation bien formé).
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>Un tableau du volet, tel que l'ecran l'affiche (Contrats/EcransMain/LectureStatistiques.cs).</summary>
public sealed record TableauStatVue(string Titre, string[] Colonnes, LigneStatVue[] Lignes, string? Cause);

/// <summary>Une ligne : son libelle et ses cellules, dans l'ordre des colonnes qui suivent la premiere.</summary>
public sealed record LigneStatVue(string Libelle, string[] Valeurs);

/// <summary>Ce que l'ecran affiche : les tableaux du volet, le refus de RIG v2, le nombre de PC (semestrielles), et d'ou ils viennent.</summary>
public sealed record VueStatistiques(TableauStatVue[] Tableaux, string? Refus, string? NombrePc, string Mode = "demo", string? Message = null);

/// <summary>
/// Pont de l'ecran PROC_STAT (web/src/ecrans/EcranStatistiques.tsx). Branche au serveur, il rend les volets de RIG v2 CALCULES
/// dans la base du greffe (Serveur/Socle/EcransMain/LectureStatistiques.cs, plan « RIG v3 100 % fonctionnel », lot 9,
/// 27/11/2026) ; SANS serveur, la demonstration ci-dessous, marquee « exemple » par l'ecran -- les memes tableaux et libelles que
/// le calcul, des chiffres d'exemple. En panne : le motif et aucun tableau (Pont/LectureEcranMain.cs).
///
/// LE GESTE (plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 11, 28/11/2026) : le bouton « Excel » de RIG v2, le
/// classeur du volet sur sa periode -- <see cref="CreerClasseurAsync"/>, pose dans la file des editions par le serveur
/// (Serveur/Socle/EcransMain/EcritureStatistiques.cs). Sans serveur, rien n'est ecrit (« Démonstration »).
/// </summary>
public static class EcranStatistiques
{
    [JSInvokable("EcranStatistiques.Excel")]
    public static Task<ResultatGeste> Excel(string volet, string debut, string fin)
        => CreerClasseurAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, volet, debut, fin);

    /// <summary>Le classeur <paramref name="volet"/> du <paramref name="debut"/> au <paramref name="fin"/> (aaaa-mm-jj) ; sans serveur ni
    /// greffe, « Démonstration ». Le resultat porte la demande d'edition (Ids).</summary>
    public static async Task<ResultatGeste> CreerClasseurAsync(ClientServeur? serveur, string? greffe, string volet, string debut, string fin)
    {
        if (serveur is null || string.IsNullOrWhiteSpace(greffe))
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesStatistiques.Excel, greffe),
            new GesteEcran(TypeGeste.Agir, [], new Dictionary<string, string>
            {
                [RoutesStatistiques.ChampVolet] = volet?.Trim() ?? "", [RoutesStatistiques.ChampDebut] = debut?.Trim() ?? "",
                [RoutesStatistiques.ChampFin] = fin?.Trim() ?? "",
            }) { Nom = "Excel" });
    }

    [JSInvokable("EcranStatistiques.Calculer")]
    public static Task<VueStatistiques> Calculer(string volet, string debut, string fin, string? niveau)
        => CalculerAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, volet, debut, fin, niveau);

    /// <summary>Le volet <paramref name="volet"/> du greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<VueStatistiques> CalculerAsync(ClientServeur? serveur, string? greffe, string volet, string debut, string fin, string? niveau)
    {
        if (!DateOnly.TryParse(debut, System.Globalization.CultureInfo.InvariantCulture, out var du)
            || !DateOnly.TryParse(fin, System.Globalization.CultureInfo.InvariantCulture, out var au))
            return new VueStatistiques([], "Saisissez la date de début et la date de fin.", null, serveur is null ? LectureEcransMain.Demo : "serveur");
        var recherche = new RechercheStatistiques(volet, du.ToDateTime(TimeOnly.MinValue), au.ToDateTime(TimeOnly.MinValue), niveau);
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les statistiques", (s, g) =>
            s.EnvoyerAsync<RechercheStatistiques, StatistiquesLues>(RoutesEcransMain.Pour(RoutesStatistiques.Statistiques, g), recherche));
        if (lu.Mode == LectureEcransMain.Demo) return Exemple(volet);
        if (lu.Valeur is not { } v) return new VueStatistiques([], null, null, lu.Mode, lu.Message);
        return new VueStatistiques(
            [.. v.Tableaux.Select(t => new TableauStatVue(t.Titre, t.Colonnes, [.. t.Lignes.Select(l => new LigneStatVue(l.Libelle, l.Valeurs))], t.Cause))],
            v.Refus, v.NombrePc, lu.Mode, null);
    }

    /// <summary>La demonstration d'un volet : les tableaux du calcul, des chiffres d'exemple (aucun ne vient d'un greffe).</summary>
    public static VueStatistiques Exemple(string volet)
    {
        static LigneStatVue L(string libelle, params string[] valeurs) => new(libelle, valeurs);
        string[] nombre = ["Indicateur", "Nombre"];
        TableauStatVue[] tableaux = volet switch
        {
            VoletsStatistiques.Web =>
            [
                new("Ouvertures, transformations et plans", ["Famille", "CA < 153", "CA 153 à 763", "CA 763 à 3049", "CA ≥ 3049", "Salariés : 0 ou inconnu", "1 à 10", "11 à 50", "51 à 100", "Plus de 100", "Total"],
                    [L("Ouvertures RJ", "6", "3", "1", "0", "4", "5", "1", "0", "0", "10"), L("Ouvertures LJ", "14", "2", "0", "0", "11", "5", "0", "0", "0", "16")], null),
            ],
            VoletsStatistiques.RepartitionsJcOp =>
            [
                new("Jugements de contentieux (JC)", ["Nature d'affaire", "Nombre"], [L("40a - Exemple de nature", "3"), L("Total", "3")], null),
                new("Ordonnances (OP)", ["Nature d'affaire", "Nombre"], [L("Total", "0")], null),
            ],
            VoletsStatistiques.ActivitesCa or VoletsStatistiques.ActivitesSalaries =>
            [
                new("Ouvertures par activités, niveau 1", ["Code", "Activité", "Plage 1", "Plage 2", "Plage 3", "Plage 4", "Total"],
                    [L("G", "Commerce (exemple)", "4", "1", "0", "0", "5"), L("TOTAL", "", "4", "1", "0", "0", "5")], null),
            ],
            VoletsStatistiques.Semestrielles =>
            [
                new("Administrateurs judiciaires", ["Nom", "Nb affaires", "Nb salariés", "Chiffre d'affaires"], [L("Me Exemple", "2", "12", "350000"), L("Total", "2", "12", "350000")], null),
            ],
            _ =>
            [
                new("Statistiques judiciaires", nombre, [L("Affaires enrôlées", "42"), L("Affaires en file R enrôlées", "7"), L("Injonctions de payer", "18")], null),
                new("Procédures collectives", nombre, [L("Ouvertures de procédures RJ", "5"), L("Ouvertures de procédures LJ", "12")], null),
            ],
        };
        return new VueStatistiques(tableaux, null, volet == VoletsStatistiques.Semestrielles ? "2" : null);
    }
}
