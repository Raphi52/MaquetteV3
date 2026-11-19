namespace RigV3Desktop.Pont;

/// <summary>Une ligne de la grille des dossiers verrouilles.</summary>
public sealed record LigneVerrou(string Dossier, string Nom, string Statut, string Etat,
                                 string Utilisateur, int Age);

/// <summary>Un detenteur de verrous et le nombre de dossiers qu'il retient.</summary>
public sealed record DetenteurVerrous(string Utilisateur, int Nombre);

/// <summary>Ce que l'ecran affiche : la grille et, a gauche, ses detenteurs.</summary>
public sealed record VueVerrous(LigneVerrou[] Lignes, DetenteurVerrous[] Detenteurs);

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
/// affichait. Metier/ n'est pas modifie.
/// </summary>
public static class EcranVerrous
{
    [JSInvokable("EcranVerrous.Verrouilles")]
    public static VueVerrous Verrouilles(string utilisateur, bool moiCompris)
    {
        var lignes = JeuDemo.Demandes
            .Where(d => d.EnCours && !string.IsNullOrWhiteSpace(d.Utilisateur))
            .Where(d => moiCompris || d.Utilisateur != utilisateur)
            .OrderBy(d => d.Creation2)
            .Select(d => new LigneVerrou(d.Dossier, d.Nom, d.Statut, d.Etat, d.Utilisateur,
                                         (int)(JeuDemo.Aujourdhui - d.Creation2).TotalDays))
            .ToArray();

        var detenteurs = lignes
            .GroupBy(l => l.Utilisateur)
            .OrderBy(g => g.Key)
            .Select(g => new DetenteurVerrous(g.Key, g.Count()))
            .ToArray();

        return new VueVerrous(lignes, detenteurs);
    }
}
