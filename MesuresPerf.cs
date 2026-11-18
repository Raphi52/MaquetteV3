namespace RigV3Desktop;

/// <summary>
/// Une ligne du widget performance, telle qu'affichee.
/// <para>
/// <b>Objectif</b> : la cible a atteindre, exprimee comme <see cref="Part"/>
/// sur la MEME echelle 0-100 %, pour que la vue puisse poser le repere au
/// bon endroit sans refaire le calcul. <b>ObjectifTexte</b> est ce repere
/// ecrit en clair a cote de la valeur (« obj. 20 », « obj. &lt;= 7 j »).
/// <b>Atteinte</b> est le pourcentage d'atteinte de cet objectif : c'est LUI
/// qui donne la couleur, pas la part brute.
/// </para>
/// </summary>
public sealed record MesurePerf(string Libelle, string Valeur, int Part, string Statut,
                                int Objectif, string ObjectifTexte, int Atteinte);

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

    /// <summary>
    /// OBJECTIFS de service, figes ici et nulle part ailleurs. Ils ne sont
    /// pas des mesures : ce sont les cibles que le greffe se donne, reprises
    /// du suivi d'activite de RIG v2. Les changer se fait ICI.
    /// </summary>
    private const double CibleTauxTraites   = 0.85;  // 85 % des demandes traitees
    private const double CibleHorsDelai     = 0;     // aucune demande hors delai legal
    private const double CibleRejets        = 1;     // au plus 1 rejet
    private const double CibleTauxNumerises = 0.90;  // 90 % des actes numerises
    private const double CibleDelaiJours    = 5;     // 5 jours de traitement en moyenne
    private const double CibleEnCoursPart   = 0.40;  // au plus 40 % du stock en cours

    private static int Part(double valeur, double plafond)
        => plafond <= 0 ? 0 : (int)Math.Clamp(valeur / plafond * 100.0, 0, 100);

    /// <summary>
    /// Une jauge qui monte est BONNE pour un debit (dossiers traites) et
    /// MAUVAISE pour un retard : le sens est donc porte par la ligne.
    /// <para>
    /// La couleur ne vient PAS de la hauteur de la jauge mais de l'ECART A
    /// L'OBJECTIF : une mesure basse peut etre excellente (hors delai) et une
    /// mesure haute peut etre insuffisante (dossiers traites). On calcule donc
    /// une ATTEINTE en pourcentage de la cible, et le statut en decoule.
    /// </para>
    /// </summary>
    private static MesurePerf Ligne(string libelle, string valeur, double brut, double plafond,
                                    double cible, string cibleTexte, bool plusCestMieux = false)
    {
        var part = Part(brut, plafond);
        var objectif = Part(cible, plafond);

        // Atteinte : « ou en est-on par rapport au but », toujours dans le
        // meme sens (100 % = objectif tenu), que la cible soit un plancher a
        // atteindre ou un plafond a ne pas depasser.
        double atteinte;
        if (plusCestMieux)
            atteinte = cible <= 0 ? 100 : brut / cible * 100.0;
        else if (brut <= cible)
            atteinte = 100;
        else
            atteinte = cible <= 0 ? 0 : cible / brut * 100.0;

        var atteinteBornee = (int)Math.Clamp(atteinte, 0, 999);
        var statut = atteinteBornee >= 100 ? "ok"
                   : atteinteBornee >= 85 ? "attente"
                   : atteinteBornee >= 60 ? "action"
                   : "alerte";

        return new MesurePerf(libelle, valeur, part, statut, objectif, cibleTexte, atteinteBornee);
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

        // Les cibles sont DERIVEES des objectifs de service ci-dessus et du
        // volume reel du jour : un objectif de 85 % vaut un nombre different
        // selon le stock de demandes.
        var cibleTraites   = Math.Round(total * CibleTauxTraites);
        var cibleNumerises = Math.Round(actes.Length * CibleTauxNumerises);
        var cibleEnCours   = Math.Round(total * CibleEnCoursPart);

        return
        [
            Ligne("Dossiers traites",     $"{traites} / {total}",            traites,    total,
                  cibleTraites,   $"obj. {cibleTraites:0}",   plusCestMieux: true),
            Ligne("Dossiers en cours",    enCours.ToString("N0"),            enCours,    total,
                  cibleEnCours,   $"obj. <= {cibleEnCours:0}"),
            Ligne("Hors delai legal",     horsDelai.ToString("N0"),          horsDelai,  Math.Max(enCours, 1),
                  CibleHorsDelai, "obj. 0"),
            Ligne("Rejets",               rejetes.ToString("N0"),            rejetes,    total,
                  CibleRejets,    $"obj. <= {CibleRejets:0}"),
            Ligne("Pieces numerisees",    $"{numerises} / {actes.Length}",   numerises,  actes.Length,
                  cibleNumerises, $"obj. {cibleNumerises:0}", plusCestMieux: true),
            Ligne("Delai moyen",          delaiMoyen.ToString("0.0") + " j", delaiMoyen, DelaiLegalJours * 2,
                  CibleDelaiJours, $"obj. <= {CibleDelaiJours:0} j")
        ];
    }
}
