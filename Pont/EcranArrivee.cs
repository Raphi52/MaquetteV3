namespace RigV3Desktop.Pont;

/// <summary>Une entree de liste deroulante : le code envoye, le libelle lu.</summary>
public sealed record OptionArrivee(string Code, string Libelle);

/// <summary>Les criteres saisis a gauche de l'ecran (memes noms que l'ancien @code).</summary>
public sealed record CriteresArrivee(string CritNumArrivee, string CritNomArrivee, string CritReferences,
                                     string CritMode, string CritCase, DateOnly? CritDu, DateOnly? CritAu,
                                     bool CritPaiement, bool CritNonNumerise, int CritTopMax);

/// <summary>Une ligne du courrier arrive, colonnes de la table ARRIVEE.</summary>
public sealed record LigneArrivee(string Numero, string Horodatage, string ModeLisible, string Nom,
                                  string References, string Case, bool PaiementJoint, string NumDemande,
                                  string TypeDemande, string Utilisateur, bool Numerise, bool Attestation);

/// <summary>Ce que l'ecran affiche : la liste des modes, la grille et ses comptes.</summary>
public sealed record VueArrivee(OptionArrivee[] Modes, LigneArrivee[] Lignes, int Total, int ANumeriser);

/// <summary>
/// Pont de l'ecran PROC_ARRIVEE (web/src/ecrans/EcranArrivee.tsx) : le courrier
/// arrive. Les lignes sont celles de JeuDemo.Arrivees -- l'ecran n'a pas sa
/// copie : c'est la meme liste que lisent la simulation EDI et l'accueil.
///
/// Le filtre vivait dans le @code d'EcranArrivee.razor (propriete Trouvees) ; il
/// est recopie ici a l'identique, dans le meme ordre et avec les memes
/// comparaisons .NET (OrdinalIgnoreCase). Metier/ n'est pas modifie.
/// </summary>
public static class EcranArrivee
{
    [JSInvokable("EcranArrivee.Trouver")]
    public static VueArrivee Trouver(CriteresArrivee criteres)
    {
        var trouvees = Trouvees(criteres);
        return new VueArrivee(
            JeuDemo.ModesArrivee.Select(m => new OptionArrivee(m.Code, m.Libelle)).ToArray(),
            trouvees.Select(a => new LigneArrivee(a.Numero, a.Horodatage, a.ModeLisible, a.Nom,
                                                  a.References, a.Case_, a.PaiementJoint, a.NumDemande,
                                                  a.TypeDemande, a.Utilisateur, a.Numerise, a.Attestation))
                    .ToArray(),
            JeuDemo.Arrivees.Length,
            trouvees.Count(a => !a.Numerise));
    }

    private static Arrivee[] Trouvees(CriteresArrivee c)
    {
        IEnumerable<Arrivee> q = JeuDemo.Arrivees;

        if (!string.IsNullOrWhiteSpace(c.CritNumArrivee))
        {
            var t = c.CritNumArrivee.Trim();
            q = q.Where(a => a.Numero.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || a.NumDemande.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(c.CritNomArrivee))
        {
            var t = c.CritNomArrivee.Trim();
            q = q.Where(a => a.Nom.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(c.CritReferences))
        {
            var t = c.CritReferences.Trim();
            q = q.Where(a => a.References.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(c.CritMode)) q = q.Where(a => a.Mode == c.CritMode);
        if (!string.IsNullOrWhiteSpace(c.CritCase))
        {
            var t = c.CritCase.Trim();
            q = q.Where(a => a.Case_.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        if (c.CritPaiement) q = q.Where(a => a.PaiementJoint);
        if (c.CritNonNumerise) q = q.Where(a => !a.Numerise);
        if (c.CritDu is { } du) q = q.Where(a => DateOnly.FromDateTime(a.Instant) >= du);
        if (c.CritAu is { } au) q = q.Where(a => DateOnly.FromDateTime(a.Instant) <= au);

        return q.Take(Math.Clamp(c.CritTopMax, 1, 500)).ToArray();
    }
}
