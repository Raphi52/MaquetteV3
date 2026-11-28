using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// L'entree de l'ecran « Arrivée du courrier » (web/src/ecrans/EcranArrivee.tsx) : la base du greffe de la session, lue par
/// le serveur (Serveur/Socle/EcransMain/LectureArrivees.cs, colonnes de RIG v2 citees), et la demonstration
/// (EcranArrivee.Exemple) seulement SANS serveur. Plan « RIG v3 100 % fonctionnel », lot 17 (26/11/2026).
/// Le geste « Enregistrer une arrivée » (la saisie OPE_ARRIVEE de RIG v2) s'ecrit dans la base du greffe par le serveur
/// (<see cref="EnregistrerAsync"/>, Serveur/Socle/EcransMain/EcritureArrivee.cs) ; sans serveur, rien n'est ecrit
/// (plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026).
/// </summary>
public static partial class EcranArrivee
{
    [JSInvokable("EcranArrivee.Trouver")]
    public static Task<VueArrivee> Trouver(CriteresArrivee criteres)
        => TrouverAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, criteres);

    /// <summary>« Enregistrer une arrivée » : le <paramref name="mode"/> (vide : PTT, comme RIG v2) et la <paramref name="date"/>
    /// (aaaa-mm-jj, vide : aujourd'hui). Le resultat porte le numero donne (Ids).</summary>
    [JSInvokable("EcranArrivee.Enregistrer")]
    public static Task<ResultatGeste> Enregistrer(string mode, string date)
        => EnregistrerAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, mode, date);

    /// <summary>Le geste sur <paramref name="serveur"/> et le greffe <paramref name="greffe"/> : sans eux, « Démonstration ».</summary>
    public static async Task<ResultatGeste> EnregistrerAsync(ClientServeur? serveur, string? greffe, string? mode, string? date)
    {
        if (serveur is null || string.IsNullOrWhiteSpace(greffe))
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesArrivee.Enregistrer, greffe),
            new GesteEcran(TypeGeste.Creer, [], new Dictionary<string, string>
            {
                [RoutesArrivee.ChampMode] = mode?.Trim() ?? "",
                [RoutesArrivee.ChampDate] = date?.Trim() ?? "",
            }));
    }

    /// <summary>Le courrier du greffe <paramref name="greffe"/> lu sur <paramref name="serveur"/> (null : la demonstration).</summary>
    public static async Task<VueArrivee> TrouverAsync(ClientServeur? serveur, string? greffe, CriteresArrivee c)
    {
        var lu = await LireAsync(serveur, greffe, "le courrier arrivé",
            new RechercheArrivees(c.CritNumArrivee, c.CritNomArrivee, c.CritReferences, c.CritMode, c.CritCase, c.CritDu, c.CritAu,
                                  c.CritPaiement, c.CritNonNumerise, Math.Clamp(c.CritTopMax, 1, RoutesEcransMain.Plafond)));
        if (lu.Mode == LectureEcransMain.Demo) return Exemple(c);
        if (lu.Valeur is not { } v) return new VueArrivee([], [], 0, 0, lu.Mode, lu.Message);
        return new VueArrivee([.. v.Modes.Select(m => new OptionArrivee(m.Code, m.Libelle))], [.. v.Lignes.Select(Ligne)],
                              v.Total, v.ANumeriser, lu.Mode);
    }

    /// <summary>La lecture du courrier, commune a « Arrivée du courrier » et « Télétransmissions ».</summary>
    internal static Task<LectureEcranMain<ArriveesLues>> LireAsync(ClientServeur? serveur, string? greffe, string quoi, RechercheArrivees r)
        => LectureEcransMain.LireAsync(serveur, greffe, quoi, (s, g) =>
            s.EnvoyerAsync<RechercheArrivees, ArriveesLues>(RoutesEcransMain.Pour(RoutesEcransMain.Arrivees, g), r));

    /// <summary>Une arrivee lue, aux colonnes de la grille (l'horodatage comme la demonstration : « 09/09/2026 09:12 »).</summary>
    internal static LigneArrivee Ligne(ArriveeLue a)
        => new(a.Numero, a.Instant?.ToString("dd/MM/yyyy HH:mm") ?? "", a.ModeLibelle, a.Nom, a.References, a.Case, a.PaiementJoint,
               a.NumDemande, a.TypeDemande, a.Utilisateur, a.Numerise, a.Attestation);
}
