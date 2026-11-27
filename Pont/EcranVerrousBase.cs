using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// L'entrée de l'écran « Dossiers verrouillés » (web/src/ecrans/EcranVerrous.tsx). Plan « RIG v3 100 % fonctionnel », lot 9
/// (27/11/2026), sur la marche du lot 17 (Pont/LectureEcranMain.cs).
///
/// Branché au serveur, l'écran montre les demandes de RIG v2 qui VERROUILLENT leurs objets (Serveur/Socle/EcransMain/
/// LectureVerrous.cs : en cours, ou en attente, interrompue, en réclamation -- Demande.GetOldLock de RIG v2) : le dossier
/// (numéro indicatif de la demande), son nom, l'état, qui la tient (DMND_NOM_UTILISATEUR) et depuis combien de jours (date de
/// création). « Mon login » écarte les demandes de cet agent, sans tenir compte de la casse (le login Windows de RIG v2), sauf
/// « Inclure mes propres dossiers » -- la règle de l'écran, inchangée. Le point coloré : « attente » pour une demande en cours,
/// « alerte » pour une demande interrompue ou en réclamation, que RIG v2 garde verrouillée hors de toute session. SANS serveur :
/// la démonstration (Exemple), marquée « exemple ». En panne : le motif et aucune ligne.
/// </summary>
public static partial class EcranVerrous
{
    [JSInvokable("EcranVerrous.Verrouilles")]
    public static Task<VueVerrous> Verrouilles(string utilisateur, bool moiCompris)
        => VerrouillesAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, utilisateur, moiCompris, DateTime.Today);

    /// <summary>Les verrous du greffe <paramref name="greffe"/> (null serveur : la démonstration).</summary>
    public static async Task<VueVerrous> VerrouillesAsync(ClientServeur? serveur, string? greffe, string utilisateur, bool moiCompris, DateTime aujourdhui)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les dossiers verrouillés", (s, g) =>
            s.LireAsync<VerrousLus>(RoutesEcransMain.Pour(RoutesVerrous.Verrous, g)));
        if (lu.Mode == LectureEcransMain.Demo) return Exemple(utilisateur, moiCompris);
        if (lu.Valeur is not { } v) return new VueVerrous([], [], lu.Mode, lu.Message);
        var moi = (utilisateur ?? "").Trim();
        LigneVerrou[] lignes =
        [
            .. v.Lignes
                .Where(d => moiCompris || !string.Equals(d.Utilisateur, moi, StringComparison.OrdinalIgnoreCase))
                .Select(d => new LigneVerrou(d.Dossier.Length > 0 ? d.Dossier : d.Demande, d.Nom,
                    d.Etat == "En cours" ? "attente" : "alerte", $"{d.Etat} · {d.Traitement} ({d.Demande})", d.Utilisateur,
                    d.Creation is { } c ? Math.Max(0, (int)(aujourdhui.Date - c.Date).TotalDays) : 0)),
        ];
        return new VueVerrous(lignes, Detenteurs(lignes), lu.Mode, null);
    }
}
