using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Modifier une créance » (PROC_MODCREANCE) : ENREGISTRER les montants admis d'une creance PC, la ligne de la grille Admissions de
/// RIG v2. Mode d'emploi : EcrivainEcran.cs ; la fiche : LecteurModificationCreance.cs. Plan « RIG v3 100 % fonctionnel », lot 12,
/// 27/11/2026.
///
/// SOURCE (ancien RIG) : PROC_INTEGCREANCE\MODCREA\Admissions\OPE_ADMISSIONS_DGV_MODCREANCE.cs -- une cellule montant change le
/// montant de la creance (colsMontants_ChangementValeur, :428-441 : vide -> NULL), VALIDER enregistre les creances
/// (_SauvegardeCreancesPC, :559-584 : creance.Sauve, CreancePC.cs:205-225 -- la creance et son creancier, sans autre colonne).
/// CONTROLES : la PC verrouillee par une autre demande -> refus au message de RIG v2 (ProcedureCollective.Lock a la selection,
/// PROC_MODCREANCE.cs:56-60 ; VerrouDemandeV2, relu dans la transaction) ; un montant illisible -> refus (la cellule montant de la
/// V2 ne l'accepte pas). La V2 n'a pas d'autre controle sur ces montants (la fiche proc_modcreance.md cite « montant >= 0 » sur
/// une colonne CRNCE_MONTANT qui n'existe pas : non repris).
/// CE QUI S'ECRIT : UPDATE v2.CREANCE_PC des seules colonnes changees (montants en decimal exact, 2 decimales). Trace : famille
/// « affaire », les montants avant et apres.
/// NON REPRIS (nomme) : la date de depot (l'evenement de l'instance, :561-579) et la suppression d'une creance (avec son instance).
/// </summary>
internal sealed class EcrivainModificationCreance : IEcrivainEcran
{
    public string Procedure => "PROC_MODCREANCE";
    public string Ecran => "Modifier une créance";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids.Length != 1 || !int.TryParse(geste.Demande.Ids[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser("Une seule créance s'enregistre à la fois : ouvrez sa fiche.");
        var avant = new Dictionary<string, decimal?>();
        string numPc, idPc, ordre;
        await using (var r = await geste.LireAsync($"""
            SELECT p.PRCLL_ID_PRCLL, p.PRCLL_NUM_PC, c.CRNPC_NUM_ORDRE, {string.Join(", ", CreancesPcV2.Montants.Select(m => "c.CRNPC_" + m.Suffixe))}
            FROM v2.CREANCE_PC AS c WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN v2.PROCEDURE_COLLECTIVE AS p ON p.PRCLL_ID_PRCLL = c.CRNPC_ID_PRCLL
            WHERE c.CRNPC_ID_CRNPC = @id
            """, ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Créance introuvable dans la base du greffe ({id}).");
            idPc = FormeLigne.Texte(r, "PRCLL_ID_PRCLL");
            numPc = FormeLigne.Texte(r, "PRCLL_NUM_PC");
            ordre = FormeLigne.Texte(r, "CRNPC_NUM_ORDRE");
            foreach (var (_, suffixe) in CreancesPcV2.Montants) avant[suffixe] = CriteresConsultation.Decimal(r, "CRNPC_" + suffixe);
        }
        if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Procédure collective", DepotsPcV2.SequenceurPc, idPc, ct) is { } motif)
            return IssueGeste.Refuser(motif);

        var changes = new Dictionary<string, decimal?>();
        foreach (var (libelle, suffixe) in CreancesPcV2.Montants)
        {
            if (geste.Valeur(LecteurModificationCreance.SectionMontants, libelle) is not { } saisi) continue;
            decimal? montant = null;
            if (saisi.Trim().Length > 0)
            {
                if (CriteresConsultation.Montant(saisi) is not { } m)
                    return IssueGeste.Refuser($"« {LecteurModificationCreance.SectionMontants} › {libelle} » : montant illisible ({saisi}).");
                montant = decimal.Round(m, 2, MidpointRounding.AwayFromZero);
            }
            if (montant != avant[suffixe]) changes[suffixe] = montant;
        }
        if (changes.Count == 0) return IssueGeste.Refuser("Aucun montant n'est modifié : la créance garde les valeurs de la base.");

        var parametres = new List<SqlParameter> { new("@id", id) };
        var sets = changes.Select((c, k) =>
        {
            parametres.Add(new SqlParameter($"@m{k}", System.Data.SqlDbType.Money) { Value = (object?)c.Value ?? DBNull.Value });
            return $"CRNPC_{c.Key} = @m{k}";
        }).ToList();
        await geste.EcrireAsync($"UPDATE v2.CREANCE_PC SET {string.Join(", ", sets)} WHERE CRNPC_ID_CRNPC = @id", ct, [.. parametres]);

        string Libelle(string suffixe) => CreancesPcV2.Montants.First(m => m.Suffixe == suffixe).Libelle;
        var detail = string.Join(", ", changes.Select(c => $"{Libelle(c.Key)} {(c.Value is { } v ? CriteresConsultation.Euros(v) : "vide")}"));
        return IssueGeste.Ecrit($"Créance n° {ordre} de la PC {numPc} enregistrée dans la base du greffe : {detail}.",
            [id.ToString(CultureInfo.InvariantCulture)],
            new TraceGeste("affaire", $"la créance n° {ordre} de la PC {numPc}", id.ToString(CultureInfo.InvariantCulture), "a modifie",
                changes.ToDictionary(c => Libelle(c.Key), c => avant[c.Key]), changes.ToDictionary(c => Libelle(c.Key), c => c.Value)));
    }
}
