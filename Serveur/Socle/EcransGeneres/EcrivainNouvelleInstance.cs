using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurNouvelleInstance;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Nouvelle instance » (PROC_XAFF) : ENREGISTRER une instance corrigee. Mode d'emploi : EcrivainEcran.cs ; la fiche et ce qui s'y
/// modifie : LecteurNouvelleInstance.cs (Modifiables). Plan « RIG v3 100 % fonctionnel », lot 5, 26/11/2026.
///
/// SOURCE (ancien RIG) : ETP_CORRECT_INSTANCE / OPE_CORRECT_INSTANCE (PROC_XAFF\ETP_CORRECT_INSTANCE) ; DoSauve =
/// instance.Sauve puis demande.Sauve (OPE_CORRECT_INSTANCE.cs:95-98).
///
/// CONTROLES DE RIG v2, avant toute ecriture :
///   1. l'instance existe et est d'un type de numero de role de XAFF (« Instance introuvable ») ;
///   2. elle n'est pas tenue par une autre demande (VerrouDemandeV2, sequenceur 2J) ;
///   3. chaque champ envoye se modifie dans la V3 (LecteurNouvelleInstance.Modifiables) ;
///   4. Date d'enrôlement et Date saisine tribunal sont obligatoires (MetierObligatoire, OPE_CORRECT_INSTANCE.Designer.cs:102,
///      :112) ; une date se lit ;
///   5. « Délivrance de l'acte » est un code de la table DELIVRANCE (ComboDelivranceActe, liste de la table).
/// RIG v2 ne controle pas l'ordre des dates dans cette etape (aucun VerifierDates dans OPE_CORRECT_INSTANCE) : la V3 non plus.
///
/// CE QUI S'ECRIT (v2.INSTANCE) : les champs modifies, et comme Instance.Sauve (Instance.cs:3895-3910, repris par
/// EcrivainInstanceJudiciaire) : INSTN_DATE_MAJ_INFOG a maintenant si TOP_EXPERIAN ou derniere mise a jour d'un jour ou plus ;
/// INSTN_NUMERO_SOURCE s'il est vide. Trace : famille « affaire », « a modifie », avant et apres.
///
/// NON REPRIS (nommes) : la demande XAFF, trace sans facture (MetierFacture = false, PROC_XAFF.Designer.cs) que le journal des
/// gestes remplace ; la correction du code affaire, de la nature, de la nature particuliere et de l'acte de saisine (leur
/// revalidation contre les listes du code affaire n'est pas reprise : LecteurNouvelleInstance.Modifiables) ; les sous-listes
/// (parties, appels, decisions, evenements) que XAFF charge en saisie, lues ici en lecture seule.
/// </summary>
internal sealed class EcrivainNouvelleInstance : IEcrivainEcran
{
    internal const string NomEcran = "Nouvelle instance";

    public string Procedure => "PROC_XAFF";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    /// <summary>Les champs ecrits et leur colonne de v2.INSTANCE (OPE_CORRECT_INSTANCE.cs:33-43).</summary>
    private static readonly (string Champ, string Colonne, bool EstDate)[] Colonnes =
    [
        (ChampEnrolement, "INSTN_DATE_ENROLEMENT", true), (ChampSaisine, "INSTN_DATE_SAISINE_GREFFE", true),
        (ChampPremiereAudience, "INSTN_PREMIERE_AUDIENCE", true), (ChampNomCourant, "INSTN_NOM_COURANT", false),
        (ChampDateActe, "INSTN_DATE_ACTE", true), (ChampDelivrance, "INSTN_DELIVRANCE", false), (ChampTexteActe, "INSTN_TEXTE_ACTE", false),
    ];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids is not [var id] || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            || await LireInstanceAsync(geste.Base, geste.Transaction, n, ct) is not { } i)
            return IssueGeste.Refuser($"Instance introuvable dans la base du greffe ({string.Join(", ", geste.Demande.Ids)}).");
        if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance, id, ct) is { } verrou)
            return IssueGeste.Refuser(verrou);

        var prefixe = GesteEcran.Cle(SectionInstance, "");
        var saisis = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (cle, valeur) in geste.Demande.Valeurs)
        {
            var libelle = cle.StartsWith(prefixe, StringComparison.Ordinal) ? cle[prefixe.Length..] : null;
            if (libelle is null || !Modifiables.Contains(libelle))
                return IssueGeste.Refuser($"« {cle} » ne se modifie pas sur l'instance {i.Role} dans la V3.");
            saisis[libelle] = valeur.Trim();
        }
        foreach (var (champ, _, estDate) in Colonnes.Where(c => saisis.ContainsKey(c.Champ)))
        {
            var v = saisis[champ];
            if (v.Length == 0 && champ is ChampEnrolement or ChampSaisine) return IssueGeste.Refuser(CreationV2.Obligatoire(champ));
            if (estDate && v.Length > 0 && CreationV2.Date(v) is null) return IssueGeste.Refuser($"« {champ} » : date illisible ({v}).");
        }
        if (saisis.TryGetValue(ChampDelivrance, out var delivrance) && delivrance.Length > 0)
        {
            await using var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.DELIVRANCE WHERE R_DELIVRANCE_VALEUR = @d", ct, new SqlParameter("@d", delivrance));
            await r.ReadAsync(ct);
            if (r.GetInt32(0) == 0) return IssueGeste.Refuser($"« {ChampDelivrance} » : « {delivrance} » n'est pas une délivrance de la table DELIVRANCE.");
        }

        var avant = i.Valeurs;
        var changes = saisis.Where(s => avant[s.Key] != s.Value).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        if (changes.Count == 0) return IssueGeste.Refuser($"Aucun champ n'est modifié : l'instance {i.Role} garde ses valeurs.");

        var affectations = new List<string>();
        var parametres = new List<SqlParameter> { new("@id", n), new("@maintenant", geste.Maintenant), new("@guid", Guid.NewGuid().ToString().ToUpperInvariant()) };
        foreach (var (champ, colonne, estDate) in Colonnes.Where(c => changes.ContainsKey(c.Champ)))
        {
            affectations.Add($"{colonne} = @{colonne}");
            var v = changes[champ];
            parametres.Add(new SqlParameter($"@{colonne}", v.Length == 0 ? DBNull.Value : estDate ? CreationV2.Date(v)!.Value : v));
        }
        // Instance.Sauve (Instance.cs:3903-3905), comme EcrivainInstanceJudiciaire.
        affectations.Add("""
            INSTN_DATE_MAJ_INFOG = CASE WHEN INSTN_TOP_EXPERIAN = 1 OR INSTN_DATE_MAJ_INFOG IS NULL
                                          OR ABS(DATEDIFF_BIG(second, INSTN_DATE_MAJ_INFOG, @maintenant)) >= 86400
                                        THEN @maintenant ELSE INSTN_DATE_MAJ_INFOG END
            """);
        affectations.Add("INSTN_NUMERO_SOURCE = ISNULL(INSTN_NUMERO_SOURCE, @guid)");
        var touchees = await geste.EcrireAsync($"UPDATE v2.INSTANCE SET {string.Join(", ", affectations)} WHERE INSTN_ID_INSTN = @id", ct, [.. parametres]);
        if (touchees != 1) throw new InvalidOperationException($"Instance {n} : {touchees} ligne(s) mise(s) a jour au lieu d'une.");

        var champs = string.Join(", ", Colonnes.Where(c => changes.ContainsKey(c.Champ)).Select(c => $"« {c.Champ} »"));
        return IssueGeste.Ecrit($"Instance {i.Role} corrigée dans la base du greffe : {champs}.", [id],
            new TraceGeste("affaire", $"l'instance {i.Role}", id, "a modifie", changes.Keys.ToDictionary(c => c, c => avant[c]), changes));
    }
}
