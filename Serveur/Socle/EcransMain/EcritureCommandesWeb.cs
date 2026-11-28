using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Commandes web » (PROC_COMMANDES_WEB, écran fait main web/src/ecrans/EcranCommandesWeb.tsx) : le geste METTRE EN ATTENTE une
/// commande, écrit dans la base du greffe. Joué par la route POST RoutesTraitementCommandesWeb.MettreEnAttente (ModuleCommandesWeb.cs)
/// dans la transaction et le journal des gestes du socle (EcrivainsEcrans.JouerAsync, sa version pour un écran fait main) : mêmes
/// gardes d'écriture (v2.* seulement). Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026 ; la lecture :
/// LectureCommandesWeb.cs.
///
/// SOURCE : PAS D'ÉCRAN RIG v2 (LectureCommandesWeb.cs) : l'écran et la table COMMANDE_WEB sont ceux de NéoRIG (D:\RIG-V3, lu sans y
/// toucher), dont la V3 reprend les règles :
///   - le geste : TraiterCommandeWebService.MettreEnAttenteAsync (src\RIG.Application\Rcs\CommandesWeb\TraiterCommandeWebService.cs
///     :310-315), puis TraiterAsync (:345-551) : le motif normalisé avant toute écriture (:371), la commande relue dans la transaction
///     (:382-384, absente : CommandesWebErrors.Introuvable), la transition vérifiée (:387) ;
///   - les règles (src\RIG.Domain\Rcs\CommandesWeb\TraitementCommandeWeb.cs) : une commande À TRAITER (A) se met en attente, une
///     commande DÉJÀ EN ATTENTE (T) est refusée « Cette commande est déjà en attente : validez-la ou rejetez-la. », une commande
///     validée, rejetée ou saisie au guichet (V, R, S) « Cette commande n'est plus à traiter… » (EstAutorisee :62-67, Verifier
///     :70-76, messages :134-140) ; le motif est facultatif, sans espaces de bord, vide = NULL, au plus 500 caractères (« Le motif ne
///     peut pas dépasser 500 caractères. », NormaliserMotif :82-96, :146-148) ;
///   - l'écriture (src\RIG.Infrastructure\Persistence\Repositories\CommandeWebTraitementRepository.cs, EnregistrerTraitementAsync
///     :48-63) : CMDWB_ETAT = 'T', CMDWB_MOTIF, CMDWB_DATE_TRAITEMENT = NULL (une mise en attente n'est pas un traitement,
///     TraiterCommandeWebService.cs:459), CMDWB_ID_UTLST = l'opérateur (son identifiant RIG v2, soc.Compte.IdV2 ; vide s'il n'en a pas,
///     IdentifierOperateurAsync :778-790), CMDWB_DATE_MODIFICATION = maintenant, SOUS CONDITION de l'état lu (WHERE … CMDWB_ETAT =
///     'A') : 0 ligne = traitée entre-temps sur un autre poste (:464-465), même refus ;
///   - la demande de la commande ne change pas : CloturerDemandeAsync ne fait rien pour une mise en attente (:561). Aucune notification
///     ni édition (elles suivent un rejet ou une validation, :325-343).
/// Types des colonnes : Documentation\scripts\1824-creer-commande-web.sql:47-52.
/// NON REPRIS ICI : Valider et Rejeter (éditions des certificats, facture, réponse à Infogreffe : Pont/EcranCommandesWebBase.cs).
/// TRACE : famille « demande », « a modifie ».
/// </summary>
public static class EcritureCommandesWeb
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Commandes web";

    /// <summary>La longueur de CMDWB_MOTIF (TransitionsCommandeWeb.LongueurMaxMotif).</summary>
    public const int LongueurMaxMotif = 500;

    // Les messages de NéoRIG, au mot près (TraitementCommandeWeb.cs:134-148, CommandesWebErrors.cs:35-37).
    public const string Introuvable = "Cette commande n'existe plus. Actualisez la liste.";
    public const string DejaTraitee = "Cette commande n'est plus à traiter : elle a été traitée entre-temps, sur un autre poste. Actualisez la liste.";
    public const string DejaEnAttente = "Cette commande est déjà en attente : validez-la ou rejetez-la.";
    public static readonly string MotifTropLong = $"Le motif ne peut pas dépasser {LongueurMaxMotif} caractères.";

    /// <summary>Les gestes écrits : tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Decider];

    /// <summary>Met en attente la commande Ids[0], avec le motif saisi.</summary>
    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (!int.TryParse(geste.Demande.Ids.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser(Introuvable);
        // Le motif d'abord, sans base (TraiterCommandeWebService.cs:371).
        var motif = geste.Demande.Valeurs.TryGetValue(RoutesTraitementCommandesWeb.ChampMotif, out var saisi) && !string.IsNullOrWhiteSpace(saisi)
            ? saisi.Trim() : null;
        if (motif is { Length: > LongueurMaxMotif }) return IssueGeste.Refuser(MotifTropLong);

        string etat, reference;
        await using (var r = await geste.LireAsync(
            "SELECT CMDWB_ETAT, CMDWB_REFERENCE_COMMANDE, CMDWB_MOTIF FROM v2.COMMANDE_WEB WITH (UPDLOCK) WHERE CMDWB_ID_CMDWB = @id",
            ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser(Introuvable);
            etat = FormeLigne.Texte(r, "CMDWB_ETAT").Trim();
            reference = FormeLigne.Texte(r, "CMDWB_REFERENCE_COMMANDE").Trim();
        }
        if (etat == "T") return IssueGeste.Refuser(DejaEnAttente);
        if (etat != "A") return IssueGeste.Refuser(DejaTraitee);

        int? operateur;
        await using (var r = await geste.LireAsync("SELECT IdV2 FROM soc.Compte WHERE CompteWindows = @c", ct,
                         new SqlParameter("@c", geste.Agent.CompteWindows)))
            operateur = await r.ReadAsync(ct) && r["IdV2"] is int v ? v : null;

        var ecrites = await geste.EcrireAsync("""
            UPDATE v2.COMMANDE_WEB
            SET CMDWB_ETAT = 'T', CMDWB_MOTIF = @motif, CMDWB_DATE_TRAITEMENT = NULL, CMDWB_ID_UTLST = @operateur,
                CMDWB_DATE_MODIFICATION = @maintenant
            WHERE CMDWB_ID_CMDWB = @id AND CMDWB_ETAT = 'A'
            """, ct,
            new SqlParameter("@motif", (object?)motif ?? DBNull.Value), new SqlParameter("@operateur", (object?)operateur ?? DBNull.Value),
            new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@id", id));
        if (ecrites == 0) return IssueGeste.Refuser(DejaTraitee);

        var nom = reference.Length > 0 ? reference : id.ToString(CultureInfo.InvariantCulture);
        return IssueGeste.Ecrit($"Commande {nom} mise en attente.", [id.ToString(CultureInfo.InvariantCulture)],
            new TraceGeste("demande", $"la commande web {nom}", id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { Etat = "A" }, new { Etat = "T", Motif = motif }));
    }
}
