using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Formalites INPI » (PROC_GINPI) : « Régulariser l'anomalie » d'une affaire. Mode d'emploi : EcrivainEcran.cs ; la liste :
/// LecteurFormalitesInpi.cs. Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026.
///
/// SOURCE (ancien RIG) : PROC_GINPI\Resultat\DataGridViewResultat.cs -- le menu contextuel n'est propose que sur une ligne « En attente
/// de paiement de l'INPI » ou « Paiement INPI partiel » (:135-145), sur la ligne choisie ; apres confirmation (:155), « UPDATE AFFAIRE
/// SET AFFRE_INPI_MONTANT_TTC = &lt;montant recu de l'INPI&gt; WHERE AFFRE_ID_AFFRE = &lt;id&gt; » (:159-160) -- la SEULE ecriture du
/// plugin --, puis « Anomalie régularisée avec succès. » (:164). La V3 relit les montants de l'affaire dans la transaction
/// (LecteurFormalitesInpi.Rapprochement) au lieu de prendre ceux de la grille.
/// CONTROLES : une seule affaire (la ligne du clic droit) ; une affaire hors de ces deux etats n'a pas le menu dans la V2 : refus.
/// </summary>
internal sealed class EcrivainFormalitesInpi : IEcrivainEcran
{
    public string Procedure => "PROC_GINPI";
    public string Ecran => "Formalités INPI";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids.Length != 1 || !int.TryParse(geste.Demande.Ids[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser("Une anomalie se régularise sur une affaire à la fois : le menu de RIG v2 vise la ligne choisie.");
        LecteurFormalitesInpi.Rapprochement? x = null;
        await using (var r = await geste.LireAsync($"""
            SELECT {LecteurFormalitesInpi.Colonnes} FROM v2.AFFAIRE AS a WITH (UPDLOCK, HOLDLOCK)
            WHERE a.AFFRE_ID_AFFRE = @id AND a.AFFRE_INPI_MONTANT_TTC IS NOT NULL
            """, ct, new SqlParameter("@id", id)))
            if (await r.ReadAsync(ct)) x = LecteurFormalitesInpi.Rapprochement.Lire(r);
        if (x is null) return IssueGeste.Refuser($"Affaire n° {id} : aucun montant attendu de l'INPI dans la base du greffe.");
        if (x.Etat is not (LecteurFormalitesInpi.EnAttente or LecteurFormalitesInpi.Partiel))
            return IssueGeste.Refuser($"Affaire n° {id} : « Régulariser l'anomalie » ne vaut que pour une affaire « {LecteurFormalitesInpi.EnAttente} » " +
                                      $"ou « {LecteurFormalitesInpi.Partiel} » (le menu de RIG v2).");
        await geste.EcrireAsync("UPDATE v2.AFFAIRE SET AFFRE_INPI_MONTANT_TTC = @recu WHERE AFFRE_ID_AFFRE = @id", ct,
            new SqlParameter("@recu", System.Data.SqlDbType.Money) { Value = x.RecuInpi }, new SqlParameter("@id", id));
        return IssueGeste.Ecrit($"Anomalie régularisée avec succès. Affaire n° {id} : le montant attendu de l'INPI passe de {CriteresConsultation.Euros(x.Prevu)} " +
                                $"à {CriteresConsultation.Euros(x.RecuInpi)}.",
            [id.ToString(CultureInfo.InvariantCulture)],
            new TraceGeste("affaire", $"l'affaire n° {id}", id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { MontantAttenduInpi = x.Prevu, Etat = x.Etat }, new { MontantAttenduInpi = x.RecuInpi }));
    }
}
