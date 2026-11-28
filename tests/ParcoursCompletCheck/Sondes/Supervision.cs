using System.IO;
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Supervision » (PROC_SUPERVISION, Pont/EcranSupervision.cs) : écran NEUF de la V3, réservé à l'exploitation d'Amitel (SOC-011, SOC-017).
/// Son geste principal, le bouton fort de l'écran (« Chercher », web/src/ecrans/EcranSupervision.tsx) : retrouver une erreur par
/// l'identifiant de suivi que l'agent dicte à l'assistance (SOC-017 CA02). La sonde joue le parcours entier par le pont :
///   1. une erreur d'écran part au journal technique (SOC-017 T03 : EcranSupervision.SignalerAsync, que le répartiteur du pont appelle
///      quand une méthode lève ; le serveur l'ÉCRIT dans soc.ErreurTechnique de la base commune et rend son identifiant de suivi) ;
///   2. la recherche par cet identifiant (EcranSupervision.RechercherAsync) relit EXACTEMENT cette erreur : programme, message, greffe,
///      origine « écran » ; une jumelle signalée juste après (autre identifiant) en est écartée.
/// L'autre geste écrit de l'écran, « Relancer » un traitement (SOC-011 CA04, ModuleSupervision.cs), exige un traitement de fond arrêté en
/// erreur (Serveur/Socle/Supervision/Traitements.cs) : seuls les traitements de nuit (clôture des chaînes, comparaison de la recopie)
/// sont inscrits, hors périmètre du plan ; il n'est pas joué ici.
/// Les routes de la supervision sont réservées à Amitel : la sonde lit au nom de parcours.lecteur, créé Amitel dans la base commune par
/// le banc (Program.cs, AgentLecteur). Lot 13 du plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
/// </summary>
public sealed class SondeSupervision : ISondeEcran
{
    private const string Amitel = "parcours.lecteur";

    public string Code => "PROC_SUPERVISION";

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var client = c.ClientDe(Amitel);
        const string programme = "EcranComptes.Ouvrir";
        var erreur = new InvalidOperationException("Sonde du parcours complet (lot 13) : erreur d'écran à retrouver par son suivi.");
        var suivi = await EcranSupervision.SignalerAsync(client, programme, erreur, c.Greffe);
        if (suivi is null) return new(false, false, "Signaler une erreur d'écran : le serveur n'a rendu aucun identifiant de suivi");
        var jumelle = await EcranSupervision.SignalerAsync(client, "EcranCompteurs.Rendre", new InvalidOperationException("Jumelle de la sonde (lot 13)."), c.Greffe);

        var vue = await EcranSupervision.RechercherAsync(client, new RechercheErreurs(suivi.Suivi, null, null, null, null));
        await PauseAsync(suivi.Suivi, jumelle?.Suivi);
        if (vue.Mode != "serveur") return new(true, false, $"Chercher {suivi.Suivi} : {vue.Mode} « {vue.Message} »");
        var trouvees = vue.Erreurs;
        return trouvees is [var e] && e.Suivi == suivi.Suivi && e.Programme == programme && e.Message == erreur.Message
               && e.CodeGreffe == c.Greffe && e.Origine == RigV3.Contrats.Socle.OriginesErreur.Ecran
               && jumelle is not null && jumelle.Suivi != suivi.Suivi
            ? new(true, true, $"Erreur d'écran signalée (suivi {suivi.Suivi}), retrouvée par « Chercher » : 1 ligne, {e.Programme}, greffe {e.CodeGreffe}, " +
                              $"origine {e.Origine}, « {e.Message} » ; jumelle {jumelle.Suivi} écartée (« {vue.Message} »)")
            : new(true, false, $"Chercher {suivi.Suivi} : {trouvees.Length} ligne(s) " +
                               $"({string.Join(" ; ", trouvees.Select(x => $"{x.Suivi} {x.Programme} {x.CodeGreffe} {x.Origine}"))}), jumelle {jumelle?.Suivi ?? "non signalée"}");
    }

    /// <summary>
    /// La preuve en image (seule la base commune du banc a un agent Amitel admis : parcours.lecteur) : RIGV3_SONDE_SUPERVISION_PAUSE = un
    /// chemin de fichier ; la sonde y écrit les deux identifiants de suivi, puis attend qu'il soit effacé (15 min au plus), le temps de
    /// brancher un serveur sur la base commune du banc (fichier de garde du banc) et d'y capturer l'écran. Sans la variable : rien.
    /// </summary>
    // fix-ok: sur les données de dev, la supervision refuse tout agent sauf celui de l'utilisateur (403 mesuré le 28/11 sur 9 comptes
    // Amitel de la copie, curl /socle/supervision/etat) : la capture passe par la base du banc.
    private static async Task PauseAsync(string suivi, string? jumelle)
    {
        if (Environment.GetEnvironmentVariable("RIGV3_SONDE_SUPERVISION_PAUSE") is not { Length: > 0 } pause) return;
        await File.WriteAllTextAsync(pause, $"suivi={suivi}\njumelle={jumelle}\n");
        var limite = DateTime.Now.AddMinutes(15);
        while (File.Exists(pause) && DateTime.Now < limite) await Task.Delay(1000);
    }
}
