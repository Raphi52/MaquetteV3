using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurInjonctionsComptesAnnuels;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Injonctions comptes annuels » (PROC_DCA_INJ) : « Générer les injonctions » enjoint les dossiers coches (lignes « A traiter » du
/// groupe « Relance restee sans effet », Id « dossier:<id> »), avec le cabinet et la decision saisis. Mode d'emploi : EcrivainEcran.cs ;
/// la liste : LecteurInjonctionsComptesAnnuels.cs ; le moteur (ce qui s'ecrit, ce qui n'est pas repris) : InjonctionsIsurv.cs. Plan
/// « RIG v3 100 % fonctionnel », lot 8, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_ISURV, nature SDCA -- UNE injonction PAR EXERCICE trouve du dossier (« on créé 1 instance par exercice et
/// non pas par dossier », PROC_ISURV.cs:224-233), triees par date de cloture (reglage TriDateCloture « 1 » dans RIG_DEV).
///
/// CONTROLES, dans l'ordre, avant toute ecriture (un refus n'ecrit rien) :
///   1. au moins une ligne cochee : « Aucun élément selectionné » (Verifier, PROC_ISURV.cs:221-223) ;
///   2. les reglages ISURV SDCA et l'etape « Cabinet et decision » (InjonctionsIsurv.PreparerAsync) ;
///   3. chaque ligne est un dossier encore A ENJOINDRE (la recherche du lecteur, relue dans la transaction) : RIG v2 ne propose pas
///      les autres ; un depot ou une injonction deja emise n'est pas enjoint ;
///   4. chaque dossier a son intervenant (InjonctionsIsurv.DossierAsync).
/// Traces : famille « affaire », « a cree », une par instance.
/// </summary>
internal sealed class EcrivainInjonctionsComptesAnnuels : IEcrivainEcran
{
    public string Procedure => "PROC_DCA_INJ";
    public string Ecran => "Injonctions comptes annuels";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    private const string Nature = "SDCA";

    // Les exercices a enjoindre du dossier, par date de cloture (TriDateCloture).
    private const string Exercices = $"""
        SELECT xd.EXRCC_ID_EXRCC, xd.EXRCC_DATE_CLOTURE, xd.EXRCC_ID_DLGRC
        FROM rec.DossierRcs AS s JOIN v2.EXERCICE AS xd ON {ExerciceAEnjoindreSql}
        WHERE s.Id = @id
        ORDER BY xd.EXRCC_DATE_CLOTURE, xd.EXRCC_ID_EXRCC
        """;

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser(InjonctionsIsurv.AucunElement);
        var (contexte, refus) = await InjonctionsIsurv.PreparerAsync(geste, Nature, ct);
        if (contexte is null) return IssueGeste.Refuser(refus!);
        var dossiers = new List<(InjonctionsIsurv.Dossier Dossier, List<(int Exercice, DateTime Cloture, int Diligence)> Exercices)>();
        foreach (var id in ids)
        {
            if (!id.StartsWith(PrefixeDossier, StringComparison.Ordinal)
                || !int.TryParse(id[PrefixeDossier.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var idDossier))
                return IssueGeste.Refuser($"Seul un dossier à enjoindre s'enjoint : un dépôt ou une injonction déjà émise ne l'est pas ({id}).");
            // 3. Encore a enjoindre : ses exercices dans la recherche de l'injonction.
            var exercices = new List<(int, DateTime, int)>();
            await using (var r = await geste.LireAsync(Exercices, ct, new SqlParameter("@id", idDossier),
                             new SqlParameter("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = geste.Maintenant.Date }))
                while (await r.ReadAsync(ct))
                    exercices.Add((Convert.ToInt32(r["EXRCC_ID_EXRCC"], CultureInfo.InvariantCulture), (DateTime)r["EXRCC_DATE_CLOTURE"],
                                   Convert.ToInt32(r["EXRCC_ID_DLGRC"], CultureInfo.InvariantCulture)));
            if (exercices.Count == 0)
            {
                string? nom = null;
                await using (var r = await geste.LireAsync("""
                    SELECT s.NumeroGestion, s.Siren, e.Denomination FROM rec.DossierRcs AS s LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
                    WHERE s.Id = @id
                    """, ct, new SqlParameter("@id", idDossier)))
                    if (await r.ReadAsync(ct)) nom = FormeRelance.NomDossier(r);
                return IssueGeste.Refuser(nom is null
                    ? $"Dossier introuvable dans la base du greffe ({idDossier})."
                    : $"{nom} : aucun exercice à enjoindre (le dossier n'est plus dans la recherche des injonctions : relance 1 échue, sans dépôt ni injonction).");
            }
            // 4. Son intervenant.
            var (dossier, refusDossier) = await InjonctionsIsurv.DossierAsync(geste, idDossier, ct);
            if (dossier is null) return IssueGeste.Refuser(refusDossier!);
            dossiers.Add((dossier, exercices));
        }

        var (lot, refusLot) = await InjonctionsIsurv.LotAsync(geste, Nature, dossiers.Sum(d => d.Exercices.Count), ct);
        if (lot is null) return IssueGeste.Refuser(refusLot!);
        var faites = new List<InjonctionsIsurv.Enjointe>();
        foreach (var (dossier, exercices) in dossiers)
            foreach (var (exercice, cloture, diligence) in exercices)
            {
                var (faite, refusInjonction) = await InjonctionsIsurv.EnjoindreAsync(geste, contexte, dossier, diligence, exercice, cloture, lot.Value, ct);
                if (faite is null) return IssueGeste.Refuser(refusInjonction!);
                faites.Add(faite);
            }
        return IssueGeste.Ecrit(InjonctionsIsurv.Message(faites, dossiers.Count, contexte.Saisie),
                                [.. faites.Select(f => f.IdInstance.ToString(CultureInfo.InvariantCulture))], [.. faites.Select(f => f.Trace)]);
    }
}
