using Microsoft.JSInterop;

namespace RigV3Desktop.Pont;

/// <summary>Les criteres saisis a gauche de l'ecran (memes noms que l'ancien @code).</summary>
public sealed record CriteresDemandes(string CritDemande, string CritClient, string CritProcessus,
                                      DateOnly? CritDu, DateOnly? CritAu, string[] CritEtats,
                                      bool CritAlerte, bool CritEnCours, int CritTopMax);

/// <summary>
/// Une ligne de la grille, colonnes de PROC_DEMANDE/DemandeDataGridView.cs (v2).
/// Etat, Statut, EnCours et Validation sont ceux AFFICHES : la validation au
/// controle final prime sur l'etat porte par la demande.
/// </summary>
public sealed record LigneDemande(string Numero, string Nom, string Dossier, string Siren, string Client,
                                  string Traitement, string Etat, string Statut, bool EnCours,
                                  string Creation, string Validation, string Utilisateur, bool Alerte);

/// <summary>Ce que l'ecran affiche : les listes de choix, la grille et le total connu.</summary>
public sealed record VueDemandes(string[] Traitements, string[] Etats, LigneDemande[] Lignes, int Total);

/// <summary>
/// Pont de l'ecran PROC_DEMANDE (web/src/ecrans/EcranDemandes.tsx) : les demandes
/// recues, lues dans JeuDemo.Demandes -- la meme liste que la simulation EDI et
/// l'accueil.
///
/// Le filtre et l'affichage de l'etat vivaient dans le @code et le balisage
/// d'EcranDemandes.razor ; ils sont recopies ici a l'identique. La coquille ne
/// peut pas passer sa fonction EstValide a React : elle passe la LISTE des
/// dossiers valides (<paramref name="valides"/>), calculee par cette fonction,
/// et ce pont repond « valide » pour exactement ces dossiers-la.
/// Metier/ n'est pas modifie.
/// </summary>
public static class EcranDemandes
{
    [JSInvokable("EcranDemandes.Trouver")]
    public static VueDemandes Trouver(CriteresDemandes criteres, string[] valides)
    {
        var dossiersValides = new HashSet<string>(valides ?? []);
        bool EstValide(string dossier) => dossiersValides.Contains(dossier);

        var lignes = Trouvees(criteres, EstValide)
            .Select(dem =>
            {
                var valide = EstValide(dem.Dossier);
                return new LigneDemande(
                    dem.Numero, dem.Nom, dem.Dossier, dem.Siren, dem.Client, dem.Traitement,
                    Etat: valide ? "Validee" : dem.Etat,
                    Statut: valide ? "ok" : dem.Statut,
                    EnCours: !valide && dem.EnCours,
                    dem.Creation,
                    Validation: valide && string.IsNullOrEmpty(dem.Validation)
                        ? JeuDemo.Aujourdhui.ToString("dd/MM/yyyy")
                        : dem.Validation,
                    dem.Utilisateur,
                    Alerte: dem.Alerte && !valide);
            })
            .ToArray();

        return new VueDemandes(
            JeuDemo.Demandes.Select(d => d.Traitement).Distinct().Order().ToArray(),
            JeuDemo.EtatsDemande,
            lignes,
            JeuDemo.Demandes.Length);
    }

    private static DemandeRcs[] Trouvees(CriteresDemandes c, Func<string, bool> EstValide)
    {
        var CritEtats = new HashSet<string>(c.CritEtats ?? []);
        // L etat affiche : la validation au controle final prime.
        string EtatReel(DemandeRcs d) => EstValide(d.Dossier) ? "Validee" : d.Etat;

        IEnumerable<DemandeRcs> q = JeuDemo.Demandes;

        if (!string.IsNullOrWhiteSpace(c.CritDemande))
        {
            var t = c.CritDemande.Trim();
            q = q.Where(d => d.Numero.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || d.Nom.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(c.CritClient))
        {
            var t = c.CritClient.Trim();
            q = q.Where(d => d.Client.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(c.CritProcessus)) q = q.Where(d => d.Traitement == c.CritProcessus);
        if (c.CritDu is { } du) q = q.Where(d => DateOnly.FromDateTime(d.Creation2) >= du);
        if (c.CritAu is { } au) q = q.Where(d => DateOnly.FromDateTime(d.Creation2) <= au);
        if (CritEtats.Count > 0) q = q.Where(d => CritEtats.Contains(EtatReel(d)));
        if (c.CritAlerte) q = q.Where(d => d.Alerte);
        if (c.CritEnCours) q = q.Where(d => d.EnCours && !EstValide(d.Dossier));

        return q.Take(Math.Clamp(c.CritTopMax, 1, 500)).ToArray();
    }
}
