namespace RigV3Desktop.Pont;

/// <summary>Une ligne de la grille des dossiers verrouilles.</summary>
public sealed record LigneVerrou(string Dossier, string Nom, string Statut, string Etat,
                                 string Utilisateur, int Age);

/// <summary>Un detenteur de verrous et le nombre de dossiers qu'il retient.</summary>
public sealed record DetenteurVerrous(string Utilisateur, int Nombre);

/// <summary>Ce que l'ecran affiche : la grille et, a gauche, ses detenteurs ; d'ou viennent les lignes (Pont/LectureEcranMain.cs).</summary>
public sealed record VueVerrous(LigneVerrou[] Lignes, DetenteurVerrous[] Detenteurs, string Mode = "demo", string? Message = null);

/// <summary>
/// Pont de l'ecran PROC_VERROUS (web/src/ecrans/EcranVerrous.tsx) : les dossiers
/// OUVERTS PAR UN TIERS. Les lignes sont CALCULEES sur les demandes connues
/// (JeuDemo), jamais ecrites en dur : une demande encore en cours portee par un
/// autre login est un verrou -- la meme regle que l'alerte « dossier(s) ouvert(s)
/// par un tiers » de Metier/AlertesGreffe.cs, qui mene a cet ecran.
///
/// Ce calcul vivait dans le @code d'EcranVerrous.razor ; il est recopie ici a
/// l'identique (filtre, tri par date de creation, age en jours, detenteurs
/// ranges par nom) pour que l'ecran React affiche exactement ce que Blazor
/// affichait. Metier/ n'est pas modifie. Ce calcul ne vaut plus que SANS serveur (lot 9 du plan « RIG v3 100 %
/// fonctionnel », 27/11/2026) : branche, l'ecran lit les demandes qui verrouillent dans la base du greffe
/// (Pont/EcranVerrousBase.cs).
/// </summary>
public static partial class EcranVerrous
{
    /// <summary>La demonstration (JeuDemo), marquee « exemple » par l'ecran.</summary>
    public static VueVerrous Exemple(string utilisateur, bool moiCompris)
    {
        var lignes = JeuDemo.Demandes
            .Where(d => d.EnCours && !string.IsNullOrWhiteSpace(d.Utilisateur))
            .Where(d => moiCompris || d.Utilisateur != utilisateur)
            .OrderBy(d => d.Creation2)
            .Select(d => new LigneVerrou(d.Dossier, d.Nom, d.Statut, d.Etat, d.Utilisateur,
                                         (int)(JeuDemo.Aujourdhui - d.Creation2).TotalDays))
            .ToArray();

        return new VueVerrous(lignes, Detenteurs(lignes));
    }

    /// <summary>Les detenteurs, ranges par nom, et le nombre de dossiers de chacun.</summary>
    internal static DetenteurVerrous[] Detenteurs(LigneVerrou[] lignes)
        => [.. lignes.GroupBy(l => l.Utilisateur).OrderBy(g => g.Key).Select(g => new DetenteurVerrous(g.Key, g.Count()))];
}
