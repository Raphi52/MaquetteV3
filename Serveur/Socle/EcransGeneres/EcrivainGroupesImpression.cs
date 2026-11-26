using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurGroupesImpression;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Relancer un lot » (PROC_RELANCELOT) : les trois gestes de RIG v2 sur un groupe d'impression, qui en RELANCENT l'etat dans
/// la base ; rien ne sort du poste. Mode d'emploi : EcrivainEcran.cs ; la liste et sa regle d'etat : LecteurGroupesImpression.cs.
/// Plan « RIG v3 100 % fonctionnel », lot 9, 26/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_RELANCELOT\ETP_EDITGRP_\OPE_EDITGRP.cs (proc_relancelot.md, Detail OPE) :
///   - « Imprimer » (bouton IMPRIMER, _basculerEtatP(false), :170-209) : le groupe passe a la phase P (Pret a imprimer), dans
///     une transaction ; puis, hors transaction, RIG v2 envoie le message batch « EditionLot2ImpressionGroupe » au service
///     d'impression (RelancerEditionsManquantes, :211-228) ;
///   - « Tout réimprimer » (IMPRIMER_AUTRE, _basculerEtatP(true)) : TOUTES les editions du groupe (DMNED_NUM_GROUPE) passent a la
///     phase P, puis le groupe ; pas de message batch (:185-193, :204-206) ;
///   - « Figer le groupe » (FIGER, _cloturer, :231-260) : date de fin = maintenant et phase P (la cloture REPASSE par P pour une
///     impression finale, elle n'ecrit pas E).
/// CONTROLES, avant toute ecriture, au mot de la regle de RIG v2 (ses boutons sont grises, sans message) :
///   1. un geste connu, au moins un groupe coche ;
///   2. chaque groupe existe, dans un lot que l'agent voit (pas une file Amitel, OPE_RECHERCHELOT.cs:38-39) ;
///   3. Imprimer et Tout reimprimer : phase E ou PZ (lstImpressionOk, :117-125) ; Figer : phase I (:116) -- un seul groupe
///      hors regle et rien n'est ecrit, pas meme pour les autres.
/// Traces : famille « demande » (RIG v2 trace chaque action par une Demande RELANCELOT, FORM_RELANCELOT.TraceAction, :56-68),
/// « a modifie », la phase (et la date de fin) avant et apres.
///
/// NON REPRIS (nomme) : le message batch au service d'impression (BatchMessage.SendDemande « EditionLot2ImpressionGroupe ») --
/// rien ne part du poste, la phase P ecrite est ce que ce service relit ; la Demande « seule » RELANCELOT validee de
/// TraceAction (une trace, remplacee par le journal des gestes : decision du chef du plan) ; le choix Oui/Non/Annuler qu'un agent
/// Amitel recoit a la cloture (« Non » passe les editions en E sans les imprimer, :242-251) : la V3 ne pose pas la question et
/// fait le « Oui », le seul chemin d'un agent du greffe ; « Valider » (modifier l'imprimante du groupe, :271-286) demande une
/// saisie que la forme File n'a pas ; « Réinitialiser » n'est jamais actif dans RIG v2 (:120-126) et n'est pas offert.
/// </summary>
internal sealed class EcrivainGroupesImpression : IEcrivainEcran
{
    public string Procedure => "PROC_RELANCELOT";
    public string Ecran => "Relancer un lot";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les gestes du descripteur, au mot pres (les boutons de OPE_EDITGRP).</summary>
    internal const string Imprimer = "Imprimer", ToutReimprimer = "Tout réimprimer", Figer = "Figer le groupe";

    private const string Groupe = """
        SELECT g.GPIMP_NUM_GROUPE, g.GPIMP_NOM_LOT, g.GPIMP_PHASE_TRAITEMENT, g.GPIMP_DATE_FIN
        FROM v2.GROUPE_IMPRESSION AS g JOIN v2.LOT AS l ON l.LOT_NOM = g.GPIMP_NOM_LOT
        WHERE g.GPIMP_NUM_GROUPE = @g AND UPPER(l.LOT_NOM) NOT LIKE 'AMI%'
        """;

    private sealed record Ligne(string Numero, string Lot, string Phase, DateTime? Fin);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var nom = geste.Demande.Nom;
        // 1. Un geste connu, une selection.
        if (nom is not (Imprimer or ToutReimprimer or Figer)) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser("Aucun groupe coché : rien n'est relancé.");

        // 2. et 3. Chaque groupe, dans la phase ou RIG v2 active le bouton.
        var lignes = new List<Ligne>();
        foreach (var id in ids)
        {
            Ligne? l = null;
            await using (var r = await geste.LireAsync(Groupe, ct, new SqlParameter("@g", id)))
                if (await r.ReadAsync(ct))
                    l = new Ligne(FormeLigne.Texte(r, "GPIMP_NUM_GROUPE"), FormeLigne.Texte(r, "GPIMP_NOM_LOT"),
                                  FormeLigne.Texte(r, "GPIMP_PHASE_TRAITEMENT"), FormeLigne.DateDe(r, "GPIMP_DATE_FIN"));
            if (l is null) return IssueGeste.Refuser($"Groupe d'impression introuvable dans la base du greffe ({id}).");
            var permis = nom == Figer ? l.Phase == PhaseInitial : l.Phase is PhaseEmis or PhasePretErreur;
            if (!permis)
                return IssueGeste.Refuser(nom == Figer
                    ? $"Le groupe {l.Numero} du lot {l.Lot} est « {LibellePhase(l.Phase)} » : RIG v2 ne fige qu'un groupe « Initial »."
                    : $"Le groupe {l.Numero} du lot {l.Lot} est « {LibellePhase(l.Phase)} » : RIG v2 n'imprime qu'un groupe émis ou en erreur.");
            lignes.Add(l);
        }

        var traces = new List<TraceGeste>();
        var editions = 0;
        foreach (var l in lignes)
        {
            if (nom == ToutReimprimer)
                editions += await geste.EcrireAsync("UPDATE v2.DEMANDE_EDITION SET DMNED_PHASE_TRAITEMENT = @p WHERE DMNED_NUM_GROUPE = @g", ct,
                    new SqlParameter("@p", PhasePret), new SqlParameter("@g", l.Numero));
            if (nom == Figer)
                await geste.EcrireAsync("UPDATE v2.GROUPE_IMPRESSION SET GPIMP_DATE_FIN = @fin, GPIMP_PHASE_TRAITEMENT = @p WHERE GPIMP_NUM_GROUPE = @g", ct,
                    new SqlParameter("@fin", geste.Maintenant), new SqlParameter("@p", PhasePret), new SqlParameter("@g", l.Numero));
            else
                await geste.EcrireAsync("UPDATE v2.GROUPE_IMPRESSION SET GPIMP_PHASE_TRAITEMENT = @p WHERE GPIMP_NUM_GROUPE = @g", ct,
                    new SqlParameter("@p", PhasePret), new SqlParameter("@g", l.Numero));
            var avant = new Dictionary<string, string> { ["Phase"] = LibellePhase(l.Phase) };
            var apres = new Dictionary<string, string> { ["Phase"] = LibellePhase(PhasePret) };
            if (nom == Figer)
            {
                avant["Date de fin"] = FormeLigne.Date(l.Fin);
                apres["Date de fin"] = FormeLigne.Date(geste.Maintenant);
            }
            traces.Add(new TraceGeste("demande", $"le groupe d'impression {l.Numero} du lot {l.Lot}", l.Numero, "a modifie", avant, apres));
        }

        var n = lignes.Count;
        var groupes = n > 1 ? $"{n} groupes" : "Le groupe";
        var message = nom switch
        {
            Figer => $"{groupes} {(n > 1 ? "sont figés" : "est figé")} : prêt{(n > 1 ? "s" : "")} à l'émission pour l'impression finale.",
            ToutReimprimer => $"{groupes} {(n > 1 ? "sont remis" : "est remis")} à imprimer avec "
                            + $"{(editions == 0 ? "aucune édition" : editions == 1 ? "1 édition" : $"{editions} éditions")} (prêt à l'émission).",
            _ => $"{groupes} {(n > 1 ? "sont" : "est")} remis à imprimer (prêt à l'émission). "
               + "Le message au service d'impression n'est pas envoyé : rien ne sort du poste.",
        };
        return IssueGeste.Ecrit(message, [.. lignes.Select(l => l.Numero)], [.. traces]);
    }
}
