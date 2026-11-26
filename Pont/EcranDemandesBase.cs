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
/// </summary>
public static partial class EcranDemandes
{
    [JSInvokable("EcranDemandes.Trouver")]
    public static Task<VueDemandes> Trouver(CriteresDemandes criteres, string[] valides)
        => TrouverAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, criteres, valides);

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
