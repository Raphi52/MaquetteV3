using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// L'entree de l'ecran « Demandes » (web/src/ecrans/EcranDemandes.tsx) : la base du greffe de la session, lue par le serveur
/// (Serveur/Socle/EcransMain/LectureDemandes.cs, filtres de RIG v2 cites), et la demonstration (EcranDemandes.Exemple)
/// seulement SANS serveur. Plan « RIG v3 100 % fonctionnel », lot 17 (26/11/2026) : jusqu'ici l'ecran montrait JeuDemo meme
/// branche au serveur de dev.
///
/// Les dossiers « valides » au controle final (<c>valides</c>) sont ceux de la demonstration (la coquille les calcule sur
/// JeuDemo) : dans la base, l'etat de la demande est celui de RIG v2 (DMND_ETAT_DEMANDE), ils ne s'y appliquent pas.
/// « Alerte » n'a pas de colonne dans RIG v2 (DemandeDataGridView.cs ne colore aucune ligne ; l'alerte de RIG v2 est une
/// requete de l'accueil, MetierProcDemande.cs:196-210) : une demande lue n'est jamais en alerte.
///
/// Le geste « Supprimer l'état en cours » (menu de la grille de RIG v2) s'ecrit dans la base du greffe par le serveur
/// (<see cref="SupprimerEtatEnCoursAsync"/>, Serveur/Socle/EcransMain/EcritureDemandes.cs) ; sans serveur, rien n'est ecrit
/// (plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026).
/// </summary>
public static partial class EcranDemandes
{
    [JSInvokable("EcranDemandes.Trouver")]
    public static Task<VueDemandes> Trouver(CriteresDemandes criteres, string[] valides)
        => TrouverAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, criteres, valides);

    /// <summary>« Supprimer l'état en cours » de la demande <paramref name="numero"/> : elle n'est plus en cours, son etat ne
    /// change pas.</summary>
    [JSInvokable("EcranDemandes.SupprimerEtatEnCours")]
    public static Task<ResultatGeste> SupprimerEtatEnCours(string numero)
        => SupprimerEtatEnCoursAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, numero);

    /// <summary>Le geste sur <paramref name="serveur"/> et le greffe <paramref name="greffe"/> : sans eux, « Démonstration ».</summary>
    public static async Task<ResultatGeste> SupprimerEtatEnCoursAsync(ClientServeur? serveur, string? greffe, string? numero)
    {
        if (serveur is null || string.IsNullOrWhiteSpace(greffe))
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        if (string.IsNullOrWhiteSpace(numero))
            return new ResultatGeste(false, "Aucune demande choisie : rien n'est modifié.");
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesDemandes.SupprimerEtatEnCours, greffe),
            new GesteEcran(TypeGeste.Agir, [numero.Trim()], new Dictionary<string, string>()) { Nom = "Supprimer l'état en cours" });
    }

    /// <summary>Les demandes du greffe <paramref name="greffe"/> lues sur <paramref name="serveur"/> (null : la demonstration).</summary>
    public static async Task<VueDemandes> TrouverAsync(ClientServeur? serveur, string? greffe, CriteresDemandes c, string[] valides)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les demandes", (s, g) =>
            s.EnvoyerAsync<RechercheDemandes, DemandesLues>(RoutesEcransMain.Pour(RoutesEcransMain.Demandes, g),
                new RechercheDemandes(c.CritDemande, c.CritClient, c.CritProcessus, c.CritDu, c.CritAu, c.CritEtats ?? [],
                                      c.CritEnCours, Math.Clamp(c.CritTopMax, 1, RoutesEcransMain.Plafond))));
        if (lu.Mode == LectureEcransMain.Demo) return Exemple(c, valides);
        if (lu.Valeur is not { } v) return new VueDemandes([], [], [], 0, [], lu.Mode, lu.Message);
        // « Alerte uniquement » : aucune demande lue n'est en alerte (voir l'en-tete), la liste est vide, pas toute la base.
        if (c.CritAlerte) return new VueDemandes(v.Traitements, v.Etats, [], 0, v.LibellesTraitements, lu.Mode);
        return new VueDemandes(
            v.Traitements, v.Etats,
            [.. v.Lignes.Select(d => new LigneDemande(
                d.Numero, d.Nom, d.Dossier, d.Siren, d.Client, d.Traitement, d.Etat, Statut: "", d.EnCours,
                Creation: d.Creation?.ToString("dd/MM/yyyy") ?? "", Validation: d.Validation?.ToString("dd/MM/yyyy") ?? "",
                d.Utilisateur, Alerte: false, d.TraitementLibelle))],
            v.Total, v.LibellesTraitements, lu.Mode);
    }
}
