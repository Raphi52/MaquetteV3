using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Demandes » (PROC_DEMANDE, écran fait main web/src/ecrans/EcranDemandes.tsx) : le geste SUPPRIMER L'ÉTAT EN COURS d'une demande,
/// le menu de la grille de RIG v2. Joué par la route POST RoutesDemandes.SupprimerEtatEnCours (ModuleDemandes.cs) dans la transaction
/// et le journal des gestes du socle (EcrivainsEcrans.JouerAsync, sa version pour un écran fait main) : mêmes gardes d'écriture (v2.*
/// seulement). Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026 ; la lecture : LectureDemandes.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source) :
///   - RIG\DLL\Processus\PROC_DEMANDE\RESULTATS\OPE_RESULTATS.cs, tsmiDeleteEtatEnCoursDemande_Click (:814-833) : le menu « Supprimer
///     l'état en cours » (OPE_RESULTATS.Designer.cs:481), offert pour toute demande sélectionnée seule et à tout agent
///     (_OpenContextMenuDemandes, :205, :213). Une demande qui n'est PAS en cours : rien (:818) ; RIG v2 n'a pas de message, la V3 refuse
///     et le dit. En cours : pour un agent du greffe (pas d'Amitel, :822), une ligne du rapport général LIGNE_RAPP_GENE est créée
///     (Demande.CreateAndReturnLigneRappGene, Outils\DLL\RigMetier\Demande\Demande.cs:616-624 ; LigneRappGene.CreateLigneRappGene,
///     Demande\LigneRappGene.cs:53-74 : la demande, la date du jour, ligne 1, l'heure de début) au nom de l'agent, son poste et le menu
///     (:825-826) ; puis Demande.Cloturer(son ÉTAT ACTUEL) (:829 ; Demande.cs:738-760) : DMND_EN_COURS = 0, l'état ne change pas,
///     l'affaire de la demande est déverrouillée (DeverrouillageAffaire, Demande.cs:1775-1781 ; Facture\Affaire.cs:269-283 :
///     AFFRE_ID_UTLST_EN_COURS = NULL), la ligne du rapport est close (LigneRappGene.Cloturer, LigneRappGene.cs:82-92 : heure de fin,
///     état de sortie = l'état de la demande).
/// LE POSTE : RIG v2 écrit « machine/session Windows/Menu 'Supprimer état en cours' » ; la V3 n'a pas de poste côté serveur, elle écrit
/// « RIG v3/compte Windows de l'agent/Menu 'Supprimer état en cours' ». L'agent : son identifiant RIG v2 (soc.Compte.IdV2), vide pour un
/// compte sans identifiant V2.
/// NON REPRIS : la sauvegarde complète de l'objet Demande (Demande.Sauve, ses tables liées) : seules les colonnes que ce geste change
/// sont écrites.
/// TRACE : famille « demande », « a modifie ».
/// </summary>
public static class EcritureDemandes
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Demandes";

    /// <summary>Le libellé du menu de RIG v2 (OPE_RESULTATS.Designer.cs:481).</summary>
    public const string Geste = "Supprimer l'état en cours";

    /// <summary>Les gestes écrits : tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Agir];

    /// <summary>Retire l'état en cours de la demande Ids[0] (son numéro).</summary>
    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var numero = geste.Demande.Ids.FirstOrDefault()?.Trim() ?? "";
        if (numero.Length == 0) return IssueGeste.Refuser("Aucune demande choisie : rien n'est modifié.");

        int id;
        string etat;
        bool enCours;
        int? affaire;
        await using (var r = await geste.LireAsync("""
            SELECT DMND_ID_DMND, DMND_ETAT_DEMANDE, DMND_EN_COURS, DMND_ID_AFFRE
            FROM v2.DEMANDE WITH (UPDLOCK)
            WHERE DMND_NUM_DEMANDE = @numero
            """, ct, new SqlParameter("@numero", numero)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Demande introuvable dans la base du greffe ({numero}).");
            id = Convert.ToInt32(r["DMND_ID_DMND"], CultureInfo.InvariantCulture);
            etat = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE").Trim();
            enCours = r["DMND_EN_COURS"] is true;
            affaire = r["DMND_ID_AFFRE"] is int a ? a : null;
        }
        // OPE_RESULTATS.cs:818 : une demande qui n'est pas en cours ne change pas.
        if (!enCours) return IssueGeste.Refuser($"La demande {numero} n'est pas en cours : il n'y a pas d'état en cours à supprimer.");

        var maintenant = geste.Maintenant;
        int? ligneRapport = null;
        if (!geste.Agent.EstAmitel)
        {
            int? idV2;
            await using (var r = await geste.LireAsync("SELECT IdV2 FROM soc.Compte WHERE CompteWindows = @c", ct,
                             new SqlParameter("@c", geste.Agent.CompteWindows)))
                idV2 = await r.ReadAsync(ct) && r["IdV2"] is int v ? v : null;
            // Créée (:823-826) puis close par Demande.Cloturer (LigneRappGene.Cloturer) dans la même transaction : une seule écriture.
            ligneRapport = await geste.InsererAsync("v2.LIGNE_RAPP_GENE", "LRPGN_ID_LRPGN", new Dictionary<string, object?>
            {
                ["LRPGN_ID_DMND"] = id,
                ["LRPGN_ID_UTLST"] = idV2,
                ["LRPGN_DATE_EFFECTIVE"] = maintenant.Date,
                ["LRPGN_NUM_LIGNE"] = 1,
                ["LRPGN_HEURE_DEBUT_SAISIE"] = maintenant,
                ["LRPGN_HEURE_FIN"] = maintenant,
                ["LRPGN_ETAT_DEMANDE_SORTIE"] = etat,
                ["LRPGN_SERVEUR_APPLI"] = $"RIG v3/{geste.Agent.CompteWindows}/Menu 'Supprimer état en cours'",
            }, ct);
        }

        await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_EN_COURS = 0 WHERE DMND_ID_DMND = @id", ct, new SqlParameter("@id", id));
        var affaireDeverrouillee = affaire is { } idAffaire && await geste.EcrireAsync(
            "UPDATE v2.AFFAIRE SET AFFRE_ID_UTLST_EN_COURS = NULL WHERE AFFRE_ID_AFFRE = @a AND AFFRE_ID_UTLST_EN_COURS IS NOT NULL", ct,
            new SqlParameter("@a", idAffaire)) > 0;

        return IssueGeste.Ecrit($"La demande {numero} n'est plus en cours (état inchangé).", [numero],
            new TraceGeste("demande", $"la demande {numero}", id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { EnCours = true, Etat = etat },
                new { EnCours = false, Etat = etat, LigneRapport = ligneRapport, AffaireDeverrouillee = affaireDeverrouillee }));
    }
}
