namespace RigV3Desktop.Pont;

/// <summary>Une ligne de la grille des flux teletransmis.</summary>
public sealed record LigneFlux(string Numero, string Horodatage, string ModeLisible, string Nom,
                               string References, string NumDemande, bool Numerise);

/// <summary>Ce que l'ecran affiche : la grille, et la phrase du canal (« dernier flux : … »).</summary>
public sealed record VueTeletransmission(LigneFlux[] Flux, string Dernier);

/// <summary>
/// Pont de l'ecran PROC_TELETRANS (web/src/ecrans/EcranTeletransmission.tsx) :
/// le suivi des flux TELETRANSMIS (EDI) recus au greffe. Les lignes viennent du
/// courrier arrive (JeuDemo, table ARRIVEE) : un flux EDI est une arrivee de
/// mode « EDI ».
///
/// Ce calcul vivait dans le @code d'EcranTeletransmission.razor ; il est
/// recopie ici a l'identique (filtre par mode, plus recent en tete, phrase du
/// dernier flux). Metier/ n'est pas modifie.
/// </summary>
public static class EcranTeletransmission
{
    [JSInvokable("EcranTeletransmission.Flux")]
    public static VueTeletransmission Flux(string mode)
    {
        var flux = JeuDemo.Arrivees
            .Where(a => mode == "TOUS" || a.Mode == mode)
            .OrderByDescending(a => a.Instant)
            .Select(a => new LigneFlux(a.Numero, a.Horodatage, a.ModeLisible, a.Nom,
                                       a.References, a.NumDemande, a.Numerise))
            .ToArray();

        var dernier = flux.Length > 0
            ? $"dernier flux : {flux[0].Horodatage}"
            : "verifier le connecteur du greffe";

        return new VueTeletransmission(flux, dernier);
    }
}
