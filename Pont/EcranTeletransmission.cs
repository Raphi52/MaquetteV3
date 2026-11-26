namespace RigV3Desktop.Pont;

/// <summary>Une ligne de la grille des flux teletransmis.</summary>
public sealed record LigneFlux(string Numero, string Horodatage, string ModeLisible, string Nom,
                               string References, string NumDemande, bool Numerise);

/// <summary>Ce que l'ecran affiche : la grille, et la phrase du canal (« dernier flux : … »).
/// <paramref name="Mode"/> : « serveur » (base du greffe), « demo » (sans serveur : l'ecran marque « exemple »), ou
/// « sans-greffe », « refuse », « erreur » avec leur <paramref name="Message"/> et aucune ligne (Pont/LectureEcranMain.cs).</summary>
public sealed record VueTeletransmission(LigneFlux[] Flux, string Dernier, string Mode = "demo", string? Message = null);

/// <summary>
/// Pont de l'ecran PROC_TELETRANS (web/src/ecrans/EcranTeletransmission.tsx) :
/// le suivi des flux TELETRANSMIS (EDI) recus au greffe. Les lignes viennent du
/// courrier arrive (table ARRIVEE) : un flux EDI est une arrivee de mode « EDI ».
/// Branche au serveur, l'ecran lit la base du greffe (EcranTeletransmissionBase.cs,
/// plan « RIG v3 100 % fonctionnel », lot 17) ; SANS serveur, il lit ici JeuDemo,
/// marque « exemple ». Ce fichier se compile seul dans AlertesCheck (Exemple).
///
/// Ce calcul vivait dans le @code d'EcranTeletransmission.razor ; il est
/// recopie ici a l'identique (filtre par mode, plus recent en tete, phrase du
/// dernier flux). Metier/ n'est pas modifie.
/// </summary>
public static partial class EcranTeletransmission
{
    /// <summary>La phrase du canal quand aucun flux n'est arrive.</summary>
    public const string SansFlux = "verifier le connecteur du greffe";

    /// <summary>La demonstration : les arrivees de JeuDemo du mode <paramref name="mode"/> (« TOUS » : toutes).</summary>
    public static VueTeletransmission Exemple(string mode)
    {
        var flux = JeuDemo.Arrivees
            .Where(a => mode == "TOUS" || a.Mode == mode)
            .OrderByDescending(a => a.Instant)
            .Select(a => new LigneFlux(a.Numero, a.Horodatage, a.ModeLisible, a.Nom,
                                       a.References, a.NumDemande, a.Numerise))
            .ToArray();

        var dernier = flux.Length > 0
            ? $"dernier flux : {flux[0].Horodatage}"
            : SansFlux;

        return new VueTeletransmission(flux, dernier);
    }
}
