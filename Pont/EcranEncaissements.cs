using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>Une ventilation de depart, telle que l'affiche web/src/ecrans/EcranEncaissements.tsx (Ventilation) : montants en CENTIMES,
/// soldes DUS (le solde de RIG v2 change de signe : positif = ce qui reste du, la convention de l'ecran).</summary>
public sealed record VentilationVue(string Identifiant, string Affaire, string Client, string Abrege, long Montant, long SoldeAffaire, long SoldeClient);

/// <summary>L'encaissement de depart d'un dossier (payeur, montant en centimes, ses ventilations) et d'ou il vient
/// (Pont/LectureEcranMain.cs). Mode « demo » : rien, l'ecran garde sa demonstration, marquee « exemple ».</summary>
public sealed record VueEncaissement(string Payeur, long Montant, VentilationVue[] Ventilations, string Mode, string? Message);

/// <summary>
/// Pont de l'ecran PROC_ENCAISS (web/src/ecrans/EcranEncaissements.tsx).
///
/// L'ecran lisait UNE chose dans Metier/ : la date du jour du jeu de demonstration, valeur de depart de la « Date de virement ». Ses
/// ventilations et sa saisie de demonstration etaient declarees dans le .razor lui-meme : elles ont suivi l'ecran dans le .tsx
/// (web/README.md, « ce qui passe par le pont »).
///
/// BRANCHE SUR LA BASE (plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026 ; marche du lot 17, Pont/LectureEcranMain.cs) : avec un
/// serveur, la date du jour est celle du poste (RIG v2 : DateTime.Today, PROC_ENCAISS.cs:87, :102), et l'ecran ouvert pour un dossier
/// part de l'encaissement que RIG v2 prepare pour ses factures (Serveur/Socle/EcransMain/LectureEncaissements.cs : une ventilation par
/// couple affaire et client, au solde du). Ouvert depuis l'accueil, RIG v2 part d'un encaissement vide (:87) : aucune ventilation.
/// Sans serveur, la demonstration, inchangee. En panne : le motif et aucune ventilation.
/// </summary>
public static class EcranEncaissements
{
    /// <summary>La date du jour au format d'un &lt;input type="date"&gt; (aaaa-mm-jj) : celle du poste avec un serveur, sinon
    /// JeuDemo.Aujourdhui.</summary>
    [JSInvokable("EcranEncaissements.DateDuJour")]
    public static string DateDuJour() => EcranFactures.DateDuJour(ClientServeur.Actif, DateTime.Today);

    /// <summary>L'encaissement de depart, pour le dossier <paramref name="dossier"/> (vide ou null : depuis l'accueil).</summary>
    [JSInvokable("EcranEncaissements.Depart")]
    public static Task<VueEncaissement> Depart(string? dossier) => DepartAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, dossier);

    /// <summary>L'encaissement de depart du greffe <paramref name="greffe"/> (null serveur : la demonstration de l'ecran).</summary>
    public static async Task<VueEncaissement> DepartAsync(ClientServeur? serveur, string? greffe, string? dossier)
    {
        if (serveur is null) return new VueEncaissement("", 0, [], LectureEcransMain.Demo, null);
        if (string.IsNullOrWhiteSpace(dossier))
        {
            // Le mode « libre » de RIG v2 : rien a lire ; le greffe ouvert dit seulement si l'ecran est bien sur la base.
            var mode = string.IsNullOrWhiteSpace(greffe) ? "sans-greffe" : "serveur";
            return new VueEncaissement("", 0, [], mode,
                mode == "serveur" ? null : "Aucun greffe n'est ouvert dans cette session : l'encaissement se lit dans la base du greffe choisi à l'ouverture de RIG.");
        }
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les factures du dossier", (s, g) =>
            s.LireAsync<EncaissementDeDepart>(RoutesEcransMain.Pour(RoutesEncaissements.Depart, g).Replace("{dossier}", Uri.EscapeDataString(dossier.Trim()), StringComparison.Ordinal)));
        if (lu.Valeur is not { } v) return new VueEncaissement("", 0, [], lu.Mode, lu.Message);
        return new VueEncaissement(v.Payeur, Centimes(v.Montant),
            [.. v.Ventilations.Select(x => new VentilationVue(x.IdAffaire.ToString(System.Globalization.CultureInfo.InvariantCulture),
                x.Affaire.Length > 0 ? $"{x.IdAffaire} — {x.Affaire}" : x.IdAffaire.ToString(System.Globalization.CultureInfo.InvariantCulture),
                x.Client, x.Abrege, Centimes(x.Montant), Centimes(-x.SoldeAffaire), Centimes(-x.SoldeClient)))],
            lu.Mode, null);
    }

    /// <summary>Un montant en centimes entiers, exact (decimal, arrondi au centime comme RIG v2 arrondit ses soldes).</summary>
    private static long Centimes(decimal montant) => (long)Math.Round(montant * 100m, MidpointRounding.ToEven);
}
