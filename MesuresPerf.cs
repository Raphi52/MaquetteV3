namespace RigV3Desktop;

/// <summary>Une ligne du widget performance, telle qu'affichee.</summary>
public sealed record MesurePerf(string Libelle, string Valeur, int Part, string Statut);

/// <summary>
/// Mesures d'ACTIVITE du greffe : ce que le widget doit montrer, c'est le
/// traitement des DOSSIERS, pas la sante technique du process (threads,
/// memoire, file du pool). Les grandeurs sont CALCULEES sur les dossiers
/// connus (JeuDemo : demandes, actes) ; rien n'est ecrit en dur.
/// Grandeurs reprises de l'accueil de RIG v2 (PROC_ACCUEIL) : dossiers
/// traites, en cours, hors delai legal, rejets, pieces numerisees, delai
/// moyen de traitement.
/// </summary>
public sealed class MesuresPerf
{
    /// <summary>Delai legal de traitement d'une demande, en jours.</summary>
    private const int DelaiLegalJours = 7;

    private static int Part(double valeur, double plafond)
        => plafond <= 0 ? 0 : (int)Math.Clamp(valeur / plafond * 100.0, 0, 100);

    /// <summary>
    /// Une jauge qui monte est BONNE pour un debit (dossiers traites) et
    /// MAUVAISE pour un retard : le sens est donc porte par la ligne.
    /// </summary>
    private static MesurePerf Ligne(string libelle, string valeur, double brut, double plafond,
                                    bool plusCestMieux = false)
    {
        var part = Part(brut, plafond);
        var gravite = plusCestMieux ? 100 - part : part;
        var statut = gravite >= 85 ? "alerte" : gravite >= 60 ? "action" : gravite >= 30 ? "attente" : "ok";
        return new MesurePerf(libelle, valeur, part, statut);
    }

    public MesurePerf[] Relever()
    {
        var demandes = JeuDemo.Demandes;
        var total = demandes.Length;

        var traites = demandes.Count(d => d.Etat == "Validee");
        var enCours = demandes.Count(d => d.EnCours);
        var rejetes = demandes.Count(d => d.Etat == "Rejetee");

        var horsDelai = demandes.Count(d => d.EnCours
            && (JeuDemo.Aujourdhui - d.Creation2).TotalDays > DelaiLegalJours);

        var actes = JeuDemo.Actes;
        var numerises = actes.Count(a => a.Demat);

        var clos = demandes.Where(d => d.Etat == "Validee").ToArray();
        var delaiMoyen = clos.Length == 0
            ? 0
            : clos.Average(d => (DateTime.TryParse(d.Validation, out var v)
                ? v - d.Creation2
                : JeuDemo.Aujourdhui - d.Creation2).TotalDays);

        return
        [
            Ligne("Dossiers traites",     $"{traites} / {total}",            traites,    total, plusCestMieux: true),
            Ligne("Dossiers en cours",    enCours.ToString("N0"),            enCours,    total),
            Ligne("Hors delai legal",     horsDelai.ToString("N0"),          horsDelai,  Math.Max(enCours, 1)),
            Ligne("Rejets",               rejetes.ToString("N0"),            rejetes,    total),
            Ligne("Pieces numerisees",    $"{numerises} / {actes.Length}",   numerises,  actes.Length, plusCestMieux: true),
            Ligne("Delai moyen",          delaiMoyen.ToString("0.0") + " j", delaiMoyen, DelaiLegalJours * 2)
        ];
    }
}
