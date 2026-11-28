using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « EDI RPJC » (PROC_EDI_RPJC_TRCNC) : le geste de la grille « Affaires en cours de traitement » de la V2, « Annuler traitement », qui
/// rend une transmission prise en main a la liste des affaires a traiter. Mode d'emploi : EcrivainEcran.cs ; la liste :
/// LecteurTraitementRpjc.cs. Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 2, 28/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_EDI_RPJC_TRCNC (proc_edi_rpjc_trcnc.md) :
///   - la grille (Affaires en cours\OPE_AFFAIRE_EN_COURS.cs:16-19) : DEMAT_RPJC_TRCNC a l'etat '1', un agent dessus
///     (RPJCC_USER_EN_COURS NOT NULL), un mandataire ACTIF AVEC ABREGE pour son CNBF et une instance pour son role -- le geste ne vise
///     que ces lignes ;
///   - le bouton « Annuler traitement » (l. 126-140) : LockUtilisateur.InscritUtilisateur(id, null), soit
///     « UPDATE DEMAT_RPJC_TRCNC SET RPJCC_USER_EN_COURS = NULL WHERE RPJCC_ID_RPJCC = id » (LockUtilisateur.cs:7-12), puis les deux
///     grilles se relisent : la transmission revient dans « Affaires a traiter » ;
///   - le controle (l. 160-161) : « on n'autorise pas a l'utilisateur courant d'annuler un traitement si ce n'est pas le sien » -- le
///     bouton est fige quand RPJCC_USER_EN_COURS n'est pas, en majuscules, AmiEnvironment.UserName (le compte Windows de l'agent,
///     Appelant.CompteWindows). La V2 n'a pas de message pour ce bouton fige : le refus reprend le sien pour le meme verrou
///     (OPE_AFFAIRE_A_TRAITER.cs:233-236), « Cette demande d'inscription est en cours de traitement par l'utilisateur ... ».
/// La V2 n'ecrit rien d'autre (ni demande, ni date). Plusieurs lignes cochees = plusieurs clics sur le bouton : chacune est controlee
/// AVANT toute ecriture, une seule refusee et rien n'est ecrit.
/// NON REPRIS (nommes) : integrer ou refuser une transmission (OPE_AFFAIRE_A_TRAITER.cs:640-915). Les deux ne traitent que les lignes
/// dont les pieces ont ete VISUALISEES (l. 668-687 : une ligne non vue est sautee) ; ces pieces sont extraites du flux entrant de la
/// base EDI (FluxEntrant.GetFluxEntrant(..., Common.CodeGreffeEdi), l. 492), absente de la base du greffe, et integrer les insere en
/// GED. La prise en main (cocher Integrer ou Refuser pose le verrou, l. 242) n'est pas un geste a part : la V2 la retire en quittant
/// sans valider (UnlockSelections, l. 623-631).
/// TRACE : famille « affaire », « a modifie », l'utilisateur en cours avant et apres.
/// </summary>
internal sealed class EcrivainTraitementRpjc : IEcrivainEcran
{
    public string Procedure => "PROC_EDI_RPJC_TRCNC";
    public string Ecran => "EDI RPJC";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur, au mot pres (le bouton « Annuler traitement » de la V2).</summary>
    internal const string AnnulerTraitement = "Annuler le traitement";

    // La grille « Affaires en cours de traitement » de la V2 (OPE_AFFAIRE_EN_COURS.cs:16-19), restreinte a une transmission.
    private const string EnCours = """
        SELECT TOP (1) c.RPJCC_USER_EN_COURS, c.RPJCC_NUMERO_AFFAIRE
        FROM v2.DEMAT_RPJC_TRCNC AS c
        WHERE c.RPJCC_ID_RPJCC = @id AND c.RPJCC_ETAT = '1' AND c.RPJCC_USER_EN_COURS IS NOT NULL
          AND EXISTS (SELECT 1 FROM v2.MANDATAIRE AS m WHERE m.MNDTR_NUMERO_CNBF = c.RPJCC_CNBF_TRANSMISSION AND m.MNDTR_ACTIF = 1
                      AND m.MNDTR_ABREGE IS NOT NULL)
          AND EXISTS (SELECT 1 FROM v2.INSTANCE AS i WHERE i.INSTN_NUM_ROLE = c.RPJCC_NUMERO_AFFAIRE)
        """;

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != AnnulerTraitement)
            return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser("Cochez au moins une affaire en cours de traitement.");

        // Les controles de la V2, ligne par ligne, AVANT toute ecriture.
        var visees = new List<(int Id, string Utilisateur, string Role)>();
        foreach (var texte in ids)
        {
            if (!int.TryParse(texte, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                return IssueGeste.Refuser($"Affaire introuvable parmi les affaires en cours de traitement ({texte}).");
            string utilisateur, role;
            await using (var r = await geste.LireAsync(EnCours, ct, new SqlParameter("@id", id)))
            {
                if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Affaire introuvable parmi les affaires en cours de traitement ({id}).");
                utilisateur = FormeLigne.Texte(r, "RPJCC_USER_EN_COURS");
                role = FormeLigne.Texte(r, "RPJCC_NUMERO_AFFAIRE");
            }
            // OPE_AFFAIRE_EN_COURS.cs:160-161 : seul l'agent qui tient l'affaire annule son traitement (comparaison en majuscules).
            if (!string.Equals(utilisateur.Trim(), geste.Agent.CompteWindows.Trim(), StringComparison.OrdinalIgnoreCase))
                return IssueGeste.Refuser($"Cette demande d'inscription est en cours de traitement par l'utilisateur {utilisateur}");
            visees.Add((id, utilisateur, role));
        }

        var traces = new List<TraceGeste>();
        foreach (var (id, utilisateur, role) in visees)
        {
            // LockUtilisateur.InscritUtilisateur(id, null), LockUtilisateur.cs:7-12.
            await geste.EcrireAsync("UPDATE v2.DEMAT_RPJC_TRCNC SET RPJCC_USER_EN_COURS = NULL WHERE RPJCC_ID_RPJCC = @id", ct,
                new SqlParameter("@id", id));
            traces.Add(new TraceGeste("affaire", $"la transmission RPJC {id} du rôle {role}", id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new Dictionary<string, string> { ["Utilisateur en cours"] = utilisateur },
                new Dictionary<string, string> { ["Utilisateur en cours"] = "" }));
        }
        var roles = string.Join(", ", visees.Select(v => v.Role).Distinct(StringComparer.Ordinal));
        return IssueGeste.Ecrit(visees.Count == 1
                ? $"Traitement annulé : l'affaire {roles} revient dans les affaires à traiter."
                : $"Traitements annulés ({visees.Count}) : les affaires {roles} reviennent dans les affaires à traiter.",
            [.. visees.Select(v => v.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }
}
