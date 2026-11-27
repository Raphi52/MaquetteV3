using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// Une piece dematerialisee, telle que l'affiche web/src/ecrans/EcranDocDemat.tsx (Piece) : Date en aaaa-mm-jj, Etat « En
/// vigueur » ou « Supprimée » (DMCOPJ_SUPPRIME), Fichier = DMCOPJ_NOM_FICHIER, Infogreffe = DMCOPJ_ETAT_INFOGREFFE.
/// </summary>
public sealed record PieceDematVue(string Id, string Dossier, string Demande, string Demat, string Abrege, string Libelle, string Date,
    string Etat, string Fichier, string Infogreffe);

/// <summary>Ce que l'ecran affiche, et d'ou viennent les pieces (Pont/LectureEcranMain.cs). Mode « demo » : aucune piece, l'ecran
/// garde sa demonstration, marquee « exemple ».</summary>
public sealed record VueDocDemat(PieceDematVue[] Pieces, int Total, string Mode, string? Message);

/// <summary>
/// L'entree de l'ecran « Documents dématérialisés » (web/src/ecrans/EcranDocDemat.tsx). Plan « RIG v3 100 % fonctionnel »,
/// lot 11 (27/11/2026), sur la marche du lot 17 (Pont/LectureEcranMain.cs).
///
/// Branche au serveur, l'ecran montre les pieces dematerialisees du greffe (Serveur/Socle/EcransMain/LectureDocDemat.cs :
/// DEMAT_CREFO_PJ et son type, la demande de sa demat), groupees par dossier. SANS serveur : aucune piece, mode « demo » -- la
/// demonstration reste celle de l'ecran, marquee « exemple ». En panne : le motif et aucune piece.
/// </summary>
public static class EcranDocDemat
{
    [JSInvokable("EcranDocDemat.Pieces")]
    public static Task<VueDocDemat> Pieces() => PiecesAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Les pieces du greffe <paramref name="greffe"/> (null serveur : la demonstration de l'ecran).</summary>
    public static async Task<VueDocDemat> PiecesAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les pièces dématérialisées", (s, g) =>
            s.LireAsync<PiecesDematLues>(RoutesEcransMain.Pour(RoutesDocDemat.Pieces, g)));
        if (lu.Valeur is not { } v) return new VueDocDemat([], 0, lu.Mode, lu.Message);
        return new VueDocDemat([.. v.Lignes.Select(Vue)], v.Total, lu.Mode, null);
    }

    private static PieceDematVue Vue(PieceDematLue p) => new(
        p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Dossier, p.Demande, p.Demat, p.Abrege, p.Libelle,
        p.DatePiece?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "",
        p.Supprimee ? "Supprimée" : "En vigueur", p.Fichier, p.EtatInfogreffe);
}
