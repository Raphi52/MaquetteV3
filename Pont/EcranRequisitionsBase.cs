using System.Globalization;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// La recherche des entreprises de « Réquisitions » (web/src/ecrans/EcranRequisitions.tsx), lue dans la base du greffe. Plan
/// « RIG v3 100 % fonctionnel », lot 6 (26/11/2026), sur la marche du lot 17 (Pont/LectureEcranMain.cs).
///
/// Avant le geste « Rechercher » (EcranRequisitions.AgirAsync), la session lit les dossiers de la base
/// (Serveur/Socle/EcransMain/LectureEntreprisesRequi.cs) par les criteres que la base cherche -- raison sociale, SIREN, numero
/// de voie, adresse, code postal, ville, « Inclure les supprimes » -- puis Rechercher() les range sous la meme limite que la V2.
/// Chaque dossier est le siege de l'entreprise, avec son nombre d'inscriptions et sa procedure collective lus par la regle de la
/// V2 (LectureEntreprisesRequi.cs). Ce que la base ne cherche pas est dit dans la note des resultats : le dirigeant et l'activite
/// (moteur RechercheDossier de RIG v2), la phonetique, les etablissements secondaires et les non-inscrits. Les controles de la V2
/// (raison sociale en chiffres, aucun critere) passent avant : rien n'est lu.
/// SANS serveur : la demonstration (JeuDemo.EntreprisesRequi). En panne : le motif et aucune ligne. Les commandes web de la zone
/// haute restent la demonstration (elles se lisent avec l'ecran « Commandes web », hors de ce lot) : l'ecran les marque.
/// </summary>
public sealed partial class SessionRequisitions
{
    /// <summary>La note des resultats lus en base : ce que la recherche ne reprend pas de RIG v2.</summary>
    public const string NoteBase = "Base du greffe : les dossiers RCS dont la dénomination contient la raison sociale, au SIREN "
                                 + "et à l'adresse du siège saisis. Phonétique, établissements secondaires et non-inscrits "
                                 + "ne sont pas encore repris de RIG v2.";

    /// <summary>La note quand seuls le dirigeant ou l'activite sont saisis.</summary>
    public const string NoteNonCherches = "Le dirigeant et l'activité se cherchent par le moteur de recherche de RIG v2 : RIG v3 ne les "
                                        + "cherche pas encore. Saisissez une raison sociale, un SIREN ou une adresse.";

    /// <summary>Ajoutee a la note quand le dirigeant ou l'activite sont saisis avec un critere cherche.</summary>
    public const string NoteDirigeantIgnore = "Le dirigeant et l'activité saisis ne sont pas cherchés.";

    /// <summary>Les entreprises lues en base pour la recherche en cours ; null : la demonstration.</summary>
    private EntrepriseRequi[]? FondsBase;

    /// <summary>Le nombre de dossiers que la base a trouves (au-dela de la limite : recherche interrompue).</summary>
    private int TotalBase;

    private string ModeResultats = LectureEcransMain.Demo;
    private string? MessageResultats;

    /// <summary>Lit les entreprises de la recherche dans la base du greffe (null serveur : la demonstration).</summary>
    internal async Task ChargerDeLaBaseAsync(ClientServeur? serveur, string? greffe)
    {
        FondsBase = null;
        TotalBase = 0;
        ModeResultats = LectureEcransMain.Demo;
        MessageResultats = null;
        // Les controles de la V2 refusent avant toute lecture ; Rechercher() dit lesquels.
        if (serveur is null || RaisonSocialeInvalide || !AuMoinsUnCritere) return;

        var recherche = new RechercheEntreprises(CritRaisonSociale, CritSiren, CritNumVoie, CritAdresse, CritCodePostal, CritVille,
                                                 CritSupprCompris, Limite);
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les entreprises", (s, g) =>
            s.EnvoyerAsync<RechercheEntreprises, EntreprisesLues>(RoutesEcransMain.Pour(RoutesRequisitions.Entreprises, g), recherche));
        ModeResultats = lu.Mode;
        if (lu.Valeur is not { } v)
        {
            FondsBase = [];
            MessageResultats = lu.Message;
            return;
        }
        var cherchables = new[] { CritRaisonSociale, CritSiren, CritNumVoie, CritAdresse, CritCodePostal, CritVille }.Any(Filtre);
        FondsBase = [.. v.Lignes.Select(DeLaBase)];
        TotalBase = v.Total;
        MessageResultats = !cherchables ? NoteNonCherches
            : Filtre(CritDirigeant) || Filtre(CritActivite) ? NoteBase + " " + NoteDirigeantIgnore : NoteBase;
    }

    private static EntrepriseRequi DeLaBase(EntrepriseLue e)
    {
        var siren = e.Siren.Length == 9 ? $"{e.Siren[..3]} {e.Siren[3..6]} {e.Siren[6..]}" : e.Siren;
        var adresse = string.Join(", ", new[] { e.Adresse, $"{e.CodePostal} {e.Ville}".Trim() }.Where(x => x.Length > 0));
        return new EntrepriseRequi(e.Id.ToString(CultureInfo.InvariantCulture), siren, e.Gestion, e.Designation, "Siege", adresse, e.Personne,
            NonInscrit: false, e.NbInscriptions, e.Radie, e.ProcedureCollective, e.CodePostal, e.Ville, Dirigeant: "", Activite: "");
    }
}
