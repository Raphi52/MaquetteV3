namespace RigV3Desktop.Pont;

/// <summary>Une categorie de depot (dbo.CATEGORIE_DEPOT) : code envoye, libelle lu.</summary>
public sealed record OptionActes(string Code, string Libelle);

/// <summary>Les criteres saisis a gauche de l'ecran (memes noms que l'ancien @code).</summary>
public sealed record CriteresActes(string CritActeCritere, string CritAbrege, string CritTypeDepot,
                                   DateOnly? CritDu, DateOnly? CritAu, bool CritNonNumerisables,
                                   bool CritAnomalies, int CritTopMax);

/// <summary>Une ligne de la grille, colonnes de RESULTAT/DataGridviewActes.cs (v2).</summary>
public sealed record LigneActe(int Rang, string Dossier, string Depot, string Denomination,
                               string CategorieLisible, string Abrege, string Description,
                               string Depose, string Acte, string Cloture, bool Confidentiel,
                               bool Demat, string Anomalie, bool Numerisable);

/// <summary>Ce que l'ecran affiche : la liste des categories et la grille.</summary>
public sealed record VueActes(OptionActes[] Categories, LigneActe[] Lignes);

/// <summary>
/// Pont de l'ecran PROC_ACTES_NUM (web/src/ecrans/EcranActes.tsx) : les actes
/// numerises, lus dans JeuDemo.Actes.
///
/// Le filtre vivait dans le @code d'EcranActes.razor (propriete Trouves) ; il est
/// recopie ici a l'identique, regles v2 comprises : les actes NON numerisables
/// sont masques tant que la case « Lister actes non numerisables » n'est pas
/// cochee. Metier/ n'est pas modifie.
/// </summary>
public static class EcranActes
{
    [JSInvokable("EcranActes.Trouver")]
    public static VueActes Trouver(CriteresActes criteres)
        => new(JeuDemo.CategoriesDepot.Select(c => new OptionActes(c.Code, c.Libelle)).ToArray(),
               Trouves(criteres)
                   .Select(a => new LigneActe(a.Rang, a.Dossier, a.Depot, a.Denomination, a.CategorieLisible,
                                              a.Abrege, a.Description, a.Depose, a.Acte, a.Cloture,
                                              a.Confidentiel, a.Demat, a.Anomalie ?? string.Empty, a.Numerisable))
                   .ToArray());

    private static ActeNum[] Trouves(CriteresActes c)
    {
        IEnumerable<ActeNum> q = JeuDemo.Actes;

        if (!string.IsNullOrWhiteSpace(c.CritActeCritere))
        {
            var t = c.CritActeCritere.Trim();
            q = q.Where(a => a.Dossier.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || a.Depot.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || a.Denomination.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(c.CritAbrege))
        {
            var t = c.CritAbrege.Trim();
            q = q.Where(a => a.Abrege.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(c.CritTypeDepot)) q = q.Where(a => a.Categorie == c.CritTypeDepot);
        if (c.CritDu is { } du) q = q.Where(a => DateOnly.FromDateTime(a.DateDepot) >= du);
        if (c.CritAu is { } au) q = q.Where(a => DateOnly.FromDateTime(a.DateDepot) <= au);

        // "Lister actes non numerisables" AJOUTE ces actes a la liste :
        // dans RIG v2 ils sont masques tant que la case n est pas cochee.
        if (!c.CritNonNumerisables) q = q.Where(a => a.Numerisable);
        if (c.CritAnomalies) q = q.Where(a => !string.IsNullOrEmpty(a.Anomalie));

        return q.Take(Math.Clamp(c.CritTopMax, 1, 500)).ToArray();
    }
}
