using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// Une banque, telle que l'affiche web/src/ecrans/EcranBanques.tsx (Banque) : l'adresse en lignes (ligne, suite, « code postal
/// localité »), le compteur des lettres chèques en texte, les modes de paiement par leur code (Banque.GetListeModePaiementBanque).
/// </summary>
public sealed record BanqueVue(string Code, string Designation, string Compte, string Journal, string Adresse, string MaquetteCheque,
    string[] Modes, string NumLettreCheque, bool ChequeFournisseur, bool ChequeNotification, bool ChequeTropPercu);

/// <summary>Ce que l'écran affiche, et d'où viennent les banques (Pont/LectureEcranMain.cs). Mode « demo » : aucune banque, l'écran
/// garde sa démonstration, marquée « exemple ».</summary>
public sealed record VueBanques(BanqueVue[] Banques, int Total, string Mode, string? Message);

/// <summary>
/// L'entrée de l'écran « Gestion des banques » (PROC_GESTBANQ, web/src/ecrans/EcranBanques.tsx). Plan « RIG v3 100 % fonctionnel »,
/// lot 12 (27/11/2026), sur la marche du lot 17 (Pont/LectureEcranMain.cs). L'écran ne lisait rien dans Metier/ : sa démonstration
/// vit dans le .tsx.
///
/// Branché au serveur, l'écran montre les banques du greffe (Serveur/Socle/EcransMain/LectureBanques.cs : BANQUE et ses modes de
/// paiement). SANS serveur : aucune banque, mode « demo » -- la démonstration reste celle de l'écran, marquée « exemple ». En panne :
/// le motif et aucune banque. Les gestes de la fiche (créer, modifier, supprimer une banque) ne sont pas branchés (compte rendu du lot 12).
/// </summary>
public static class EcranBanques
{
    [JSInvokable("EcranBanques.Banques")]
    public static Task<VueBanques> Banques() => BanquesAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Les banques du greffe <paramref name="greffe"/> (null serveur : la démonstration de l'écran).</summary>
    public static async Task<VueBanques> BanquesAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les banques", (s, g) =>
            s.LireAsync<BanquesLues>(RoutesEcransMain.Pour(RoutesBanques.Banques, g)));
        if (lu.Valeur is not { } v) return new VueBanques([], 0, lu.Mode, lu.Message);
        return new VueBanques([.. v.Lignes.Select(Vue)], v.Total, lu.Mode, null);
    }

    private static BanqueVue Vue(BanqueLue b) => new(b.Code, b.Nom, b.Compte, b.Journal,
        string.Join("\n", new[] { b.LigneAdresse, b.SuiteAdresse, $"{b.CodePostal} {b.Localite}".Trim() }.Where(l => l.Length > 0)),
        b.ModeleLettreCheque, b.Modes, b.CompteurLettreCheque?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
        b.PourFournisseur, b.PourNotification, b.PourTropPercu);
}
