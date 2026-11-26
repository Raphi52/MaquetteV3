using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// L'entree de l'ecran « Télétransmissions » (web/src/ecrans/EcranTeletransmission.tsx) : le courrier arrive du greffe de la
/// session, lu par le serveur comme « Arrivée du courrier » (Serveur/Socle/EcransMain/LectureArrivees.cs), filtre sur le mode
/// (« EDI » : ARRV_MODE_ARRIVEE de MODE_ARRIVEE, RIG_DEV ; « TOUS » : sans filtre), le plus recent en tete ; la demonstration
/// (EcranTeletransmission.Exemple) seulement SANS serveur. Plan « RIG v3 100 % fonctionnel », lot 17 (26/11/2026).
/// </summary>
public static partial class EcranTeletransmission
{
    [JSInvokable("EcranTeletransmission.Flux")]
    public static Task<VueTeletransmission> Flux(string mode) => FluxAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, mode);

    /// <summary>Les flux du greffe <paramref name="greffe"/> lus sur <paramref name="serveur"/> (null : la demonstration).</summary>
    public static async Task<VueTeletransmission> FluxAsync(ClientServeur? serveur, string? greffe, string mode)
    {
        var lu = await EcranArrivee.LireAsync(serveur, greffe, "les flux reçus",
            new RechercheArrivees(null, null, null, mode == "TOUS" ? null : mode, null, null, null, false, false, RoutesEcransMain.Plafond));
        if (lu.Mode == LectureEcransMain.Demo) return Exemple(mode);
        if (lu.Valeur is not { } v) return new VueTeletransmission([], SansFlux, lu.Mode, lu.Message);
        var flux = v.Lignes.Select(a => new LigneFlux(a.Numero, a.Instant?.ToString("dd/MM/yyyy HH:mm") ?? "", a.ModeLibelle, a.Nom,
                                                      a.References, a.NumDemande, a.Numerise)).ToArray();
        return new VueTeletransmission(flux, flux.Length > 0 ? $"dernier flux : {flux[0].Horodatage}" : SansFlux, lu.Mode);
    }
}
