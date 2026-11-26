using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurInjonctionsBeneficiaires;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Injonctions beneficiaires » (PROC_DBE_INJ) : « Générer les injonctions » enjoint les entreprises cochees (lignes « A enjoindre »
/// du groupe « Relance restee sans effet », Id = la diligence SDBE), avec le cabinet et la decision saisis. Mode d'emploi :
/// EcrivainEcran.cs ; la liste : LecteurInjonctionsBeneficiaires.cs ; le moteur (ce qui s'ecrit, ce qui n'est pas repris) :
/// InjonctionsIsurv.cs. Plan « RIG v3 100 % fonctionnel », lot 8, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_ISURV, nature SDBE -- une injonction par dossier ; la relance d'injonction IDBE s'ecrit sur la diligence
/// SDBE du dossier (CreerRelanceInjonctionDBE, DossierRCS.cs:1821-1838). La classe de la fiche proc_dbe_inj.md, PROC_IDBE, est masquee
/// du menu de RIG v2 (FORM_IDBE, PROCF_VISIBLE_DANS_MENU 0, RIG_DEV lu le 26/11/2026) et herite de PROC_SURVEILLANCE_INJ, absente des
/// sources : ISURV la remplace.
///
/// CONTROLES, dans l'ordre, avant toute ecriture (un refus n'ecrit rien) :
///   1. au moins une ligne cochee : « Aucun élément selectionné » (Verifier, PROC_ISURV.cs:221-223) ;
///   2. les reglages ISURV SDBE et l'etape « Cabinet et decision » (InjonctionsIsurv.PreparerAsync) ;
///   3. chaque ligne est une diligence SDBE dont la relance est restee sans effet (la regle du lecteur, RelanceSansEffet, relue dans
///      la transaction) : une injonction deja emise n'est pas renouvelee (le test des injonctions de la recherche ISURV,
///      NatureDiligence.parametre.cs:846-853) ;
///   4. chaque dossier a son intervenant (InjonctionsIsurv.DossierAsync).
/// Traces : famille « affaire », « a cree », une par instance.
/// </summary>
internal sealed class EcrivainInjonctionsBeneficiaires : IEcrivainEcran
{
    public string Procedure => "PROC_DBE_INJ";
    public string Ecran => "Injonctions bénéficiaires";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    private const string Nature = "SDBE";

    // La diligence cochee, son dossier, et si sa relance est restee sans effet.
    private const string Diligence = $"""
        SELECT s.Id, s.NumeroGestion, s.Siren, e.Denomination, CASE WHEN {RelanceSansEffet} THEN 1 ELSE 0 END AS AEnjoindre
        FROM v2.DILIGENCE_RCS AS g
        JOIN rec.ActionRegistre AS a ON a.Id = g.DLGRC_ID_ACTRC
        JOIN rec.DossierRcs AS s ON s.Id = a.IdDossier
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        WHERE g.DLGRC_ID_DLGRC = @id AND g.DLGRC_NATURE_DILIGENCE = 'SDBE'
        """;

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser(InjonctionsIsurv.AucunElement);
        var (contexte, refus) = await InjonctionsIsurv.PreparerAsync(geste, Nature, ct);
        if (contexte is null) return IssueGeste.Refuser(refus!);

        var lignes = new List<(InjonctionsIsurv.Dossier Dossier, int Diligence)>();
        foreach (var id in ids)
        {
            if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var idDiligence))
                return IssueGeste.Refuser($"Diligence introuvable dans la base du greffe ({id}).");
            int? idDossier = null;
            var nom = id;
            var aEnjoindre = false;
            await using (var r = await geste.LireAsync(Diligence, ct, new SqlParameter("@id", idDiligence),
                             new SqlParameter("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = geste.Maintenant.Date }))
                if (await r.ReadAsync(ct))
                {
                    idDossier = Convert.ToInt32(r["Id"], CultureInfo.InvariantCulture);
                    nom = FormeRelance.NomDossier(r);
                    aEnjoindre = r["AEnjoindre"] is int a && a == 1;
                }
            if (idDossier is null) return IssueGeste.Refuser($"Diligence introuvable dans la base du greffe ({id}).");
            if (!aEnjoindre)
                return IssueGeste.Refuser($"{nom} : rien à enjoindre (seule une relance restée sans effet, sans injonction émise, s'enjoint).");
            var (dossier, refusDossier) = await InjonctionsIsurv.DossierAsync(geste, idDossier.Value, ct);
            if (dossier is null) return IssueGeste.Refuser(refusDossier!);
            lignes.Add((dossier, idDiligence));
        }

        var (lot, refusLot) = await InjonctionsIsurv.LotAsync(geste, Nature, lignes.Count, ct);
        if (lot is null) return IssueGeste.Refuser(refusLot!);
        var faites = new List<InjonctionsIsurv.Enjointe>();
        foreach (var (dossier, diligence) in lignes)
        {
            var (faite, refusInjonction) = await InjonctionsIsurv.EnjoindreAsync(geste, contexte, dossier, diligence, null, null, lot.Value, ct);
            if (faite is null) return IssueGeste.Refuser(refusInjonction!);
            faites.Add(faite);
        }
        return IssueGeste.Ecrit(InjonctionsIsurv.Message(faites, lignes.Count, contexte.Saisie),
                                [.. faites.Select(f => f.IdInstance.ToString(CultureInfo.InvariantCulture))], [.. faites.Select(f => f.Trace)]);
    }
}
