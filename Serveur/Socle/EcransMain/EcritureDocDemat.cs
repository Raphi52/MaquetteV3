using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Documents dématérialisés » (PROC_DOC_DEMAT, écran fait main web/src/ecrans/EcranDocDemat.tsx) : les gestes SUPPRIMER une pièce
/// (la mettre en état « Supprimé ») et la REMETTRE EN VIGUEUR, du menu de l'arbre de RIG v2. Joués par la route POST RoutesDocDemat.Etat
/// (ModuleDocDemat.cs) dans la transaction et le journal des gestes du socle (EcrivainsEcrans.JouerAsync). Plan « RIG v3 complet, mesuré
/// par le banc du parcours complet », lot 10, 28/11/2026 ; la lecture : LectureDocDemat.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RIG\DLL\Processus\PROC_DOC_DEMAT\PROC_DOC_DEMAT) :
///   - le MENU (Controls\UltTreeViewDocDemat.cs, CmsItem_Opening :230-242) : « Supprimer » n'est offert que pour une pièce en vigueur
///     (:236, :241), « Remettre en vigueur » que pour une pièce supprimée (:238) ;
///   - l'écran ouvert HORS d'une demande en cours de saisie est en LECTURE SEULE (IsReadOnly) : c'est le cas de l'écran V3, ouvert depuis
///     le menu sur toutes les pièces du greffe. Là, seule une personne d'AMITEL écrit dans la base (FDDA 16752 : tsmiSupprimer_Click :291,
///     tsmiRemettreEnVigueur_Click :306) ; pour les autres, « Supprimer » est grisé (:236) et « Remettre en vigueur » ne change que l'arbre
///     affiché, rien n'est enregistré (UnDeletedDocumentAndNotifyRig, ViewModelDocuments.Notify.cs:207-230, sans sauvegarde en lecture
///     seule) : la V3 refuse, rien d'écrit ;
///   - l'ÉCRITURE (ViewModelDocuments.cs) : SupprimerDocument (:757-769) pose DMCOPJ_SUPPRIME = 1 et DMCOPJ_DATE_SUPPRIME = maintenant ;
///     RemettreEnVigueurDocument (:775-787) pose DMCOPJ_SUPPRIME = 0 et DMCOPJ_DATE_SUPPRIME vide. Rien d'autre : la ligne reste (la
///     lecture la montre « Supprimée »).
/// NON REPRIS : les deux confirmations de RIG v2 (« Mettre la pièce '…' en état 'Supprimé' dans la base de données du greffe ? » puis
/// « En êtes-vous sûr ? », :292-293, :307-308) sont posées par l'écran (EcranDocDemat.tsx), pas par le serveur ; le mode édition d'une
/// demande (suppression en mémoire, puis « Valider » qui réordonne toutes les pièces de la démat, ViewModelDocuments.Notify.cs:97-121) :
/// l'écran V3 n'ouvre pas de demande en saisie.
/// TRACE : famille « demande », « a modifie ».
/// </summary>
public static class EcritureDocDemat
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Documents dématérialisés";

    /// <summary>Les gestes écrits : Supprimer, et Agir « Remettre en vigueur » ; tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Supprimer, TypeGeste.Agir];

    /// <summary>Le refus d'un agent qui n'est pas d'Amitel, l'écran étant en lecture seule (UltTreeViewDocDemat.cs:236, :291, :306).</summary>
    public const string ReserveAmitel = "Hors d'une demande en cours de saisie, l'arbre des pièces est en lecture seule : seule une personne "
                                      + "d'Amitel met une pièce en état « Supprimé » ou la remet en vigueur dans la base du greffe. Rien n'est modifié.";

    /// <summary>Met la pièce Ids[0] en état « Supprimé » (Supprimer), ou la remet en vigueur (Agir « Remettre en vigueur »).</summary>
    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var supprimer = geste.Demande.Type == TypeGeste.Supprimer;
        if (!supprimer && geste.Demande.Nom != RoutesDocDemat.RemettreEnVigueur)
            return IssueGeste.Refuser($"« {geste.Demande.Nom} » n'est pas un geste de cet écran : seuls « Supprimer » et « {RoutesDocDemat.RemettreEnVigueur} » s'enregistrent.");
        if (geste.Demande.Ids is not [var texte] || !int.TryParse(texte?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser("Choisissez une pièce, une seule : rien n'est modifié.");
        if (!geste.Agent.EstAmitel) return IssueGeste.Refuser(ReserveAmitel);

        bool supprimee;
        string libelle;
        await using (var r = await geste.LireAsync("""
            SELECT p.DMCOPJ_SUPPRIME, p.DMCOPJ_TYPE_PJ_CREFO, t.R_TYPEPJCREF_LIBELLE_ECRAN
            FROM v2.DEMAT_CREFO_PJ AS p WITH (UPDLOCK)
            LEFT JOIN v2.TYPE_PJ_CREFO AS t ON t.R_TYPEPJCREF_VALEUR = p.DMCOPJ_TYPE_PJ_CREFO
            WHERE p.DMCOPJ_ID_DMCOPJ = @id
            """, ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Pièce introuvable dans la base du greffe (n° {id}) : rien n'est modifié.");
            supprimee = r["DMCOPJ_SUPPRIME"] is true;
            var l = FormeLigne.Texte(r, "R_TYPEPJCREF_LIBELLE_ECRAN").Trim();
            libelle = l.Length > 0 ? l : FormeLigne.Texte(r, "DMCOPJ_TYPE_PJ_CREFO").Trim();
        }
        // CmsItem_Opening (:236, :238) : chaque geste n'est offert que dans l'état contraire.
        if (supprimer && supprimee)
            return IssueGeste.Refuser($"La pièce « {libelle} » est déjà en état « Supprimé » : RIG v2 n'offre que « {RoutesDocDemat.RemettreEnVigueur} ».");
        if (!supprimer && !supprimee)
            return IssueGeste.Refuser($"La pièce « {libelle} » est en vigueur : RIG v2 n'offre que « Supprimer ».");

        var maintenant = geste.Maintenant;
        await geste.EcrireAsync("UPDATE v2.DEMAT_CREFO_PJ SET DMCOPJ_SUPPRIME = @supprime, DMCOPJ_DATE_SUPPRIME = @date WHERE DMCOPJ_ID_DMCOPJ = @id", ct,
            new SqlParameter("@supprime", supprimer), new SqlParameter("@date", supprimer ? maintenant : DBNull.Value), new SqlParameter("@id", id));

        var idTexte = id.ToString(CultureInfo.InvariantCulture);
        return IssueGeste.Ecrit(
            supprimer ? $"La pièce « {libelle} » est en état « Supprimé » dans la base du greffe." : $"La pièce « {libelle} » est remise en vigueur.",
            [idTexte],
            new TraceGeste("demande", $"la pièce dématérialisée « {libelle} »", idTexte, "a modifie",
                new { Supprimee = supprimee }, new { Supprimee = supprimer, DateSuppression = supprimer ? maintenant.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : null }));
    }
}
