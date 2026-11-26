using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Dépôts bénéficiaires » (PROC_DASBEN) : ENREGISTRER un depot VALIDE du document relatif au beneficiaire effectif, sur un
/// dossier RCS ou un non-inscrit. Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurDepotsBeneficiaires.cs ; le commun :
/// DepotsRcsV2.cs. Plan « RIG v3 100 % fonctionnel », lot 5, 26/11/2026.
///
/// CONTROLES DE RIG v2, avant toute ecriture (un refus n'ecrit rien) :
///   1. l'entreprise : un dossier RCS ou un non-inscrit de la base (ModeDASBEN DossierRCS / NonInscrit) ;
///   2. les champs obligatoires (MetierObligatoire) : Date de dépôt, Date d'arrivée, Code événement
///      (PROC_DASBEN\ETP_DEPOT\OPE_ACTES.Designer.cs:80, :91, :135), Acte et Date de l'acte (OPE_ACTE_DEPOSE.Designer.cs:69, :78) ;
///      l'acte est un abrege de piece (v2.ABREGE_PIECE) ;
///   3. le code evenement est un de ceux que la liste propose (OPE_ACTES.cs:33-55) : un BEN, BENH ou BENM deja depose →
///      BENM seul ; sinon, pour un dossier, immatricule avant le 01/08/2017 → BENH ou BENM, apres → BEN ou BENM
///      (DossierRCS.GetCodeEvenementDASBEN, DossierRCS.cs:2500-2510) ; pour un non-inscrit, les codes « 1099 »
///      (R_CODEEVENEM_AMITEL = '1099') ;
///   4. la numerotation donne ses numeros (arrivee RA1, depot RDB) : sinon refus, rien d'ecrit.
///
/// CE QUI S'ECRIT (PROC_DASBEN.cs:1034-1077, ETP_DEPOT.cs:74-97) : l'action du registre « DASBEN » ; le depot RDB de l'annee
/// du depot, categorie N, non numerise, saisie simplifiee, sans envoi INPI, etat VAX 0101, gratuit pour le code BEN (ticket
/// 7402) ; l'evenement RCS au texte « Dépôt modificatif ou complémentaire relatif au bénéficiaire effectif » ; l'acte depose ;
/// la demande DASBEN validee. Trace : famille « dossier », « a cree ».
///
/// NON REPRIS (nommes ; le message le dit) : la facture (articles, refus et reclamations non factures) ; les editions ; les
/// pieces dematerialisees (OPE_DEMAT) et le flux ACIS ; le montant saisi (il va a la facture) ; les dates de mise a jour
/// Infogreffe / Intergreffe du depot (PROC_DASBEN.cs:1075-1076) ; la reprise de la saisie pour un autre depot.
/// </summary>
internal sealed class EcrivainDepotsBeneficiaires : IEcrivainEcran
{
    internal const string NomEcran = "Dépôts bénéficiaires";

    /// <summary>Les etapes et champs du descripteur PROC_DASBEN (Ouvrir.cs), au mot pres.</summary>
    internal const string EtapeEntreprise = "Entreprise", EtapeActes = "Actes", EtapeActe = "Acte déposé";

    internal const string ChampEntreprise = "Entreprise", ChampDateArrivee = "Date d'arrivée", ChampDateDepot = "Date de dépôt",
                          ChampCode = "Code événement", ChampActe = "Acte", ChampDateActe = "Date de l'acte";

    /// <summary>Le texte de l'evenement RCS d'un depot de beneficiaires (PROC_DASBEN\ETP_DEPOT\ETP_DEPOT.cs:80).</summary>
    internal const string TexteEvenement = "Dépôt modificatif ou complémentaire relatif au bénéficiaire effectif";

    public string Procedure => "PROC_DASBEN";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Creer];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        string S(string etape, string champ) => Saisi(geste, etape, champ);
        foreach (var (etape, champ) in new[]
                 {
                     (EtapeEntreprise, ChampEntreprise), (EtapeActes, ChampDateArrivee), (EtapeActes, ChampDateDepot),
                     (EtapeActes, ChampCode), (EtapeActe, ChampActe), (EtapeActe, ChampDateActe),
                 })
            if (S(etape, champ).Length == 0) return IssueGeste.Refuser(Obligatoire($"{etape} › {champ}"));
        foreach (var (etape, champ) in new[] { (EtapeActes, ChampDateArrivee), (EtapeActes, ChampDateDepot), (EtapeActe, ChampDateActe) })
            if (Date(S(etape, champ)) is null) return IssueGeste.Refuser($"« {etape} › {champ} » : date illisible ({S(etape, champ)}).");

        var numero = S(EtapeEntreprise, ChampEntreprise).Replace(" ", "", StringComparison.Ordinal);
        if (await DepotsRcsV2.TrouverAsync(geste, numero, ct) is not { } entite) return IssueGeste.Refuser(DepotsRcsV2.Introuvable(numero));
        var code = S(EtapeActes, ChampCode);
        var proposes = await CodesProposesAsync(geste, entite, ct);
        if (!proposes.Contains(code, StringComparer.Ordinal))
            return IssueGeste.Refuser($"Le code événement « {code} » n'est pas proposé pour {entite.Designation} ({entite.Numero}) : " +
                                      $"{(proposes.Count == 0 ? "aucun code" : string.Join(", ", proposes))}.");
        var acte = S(EtapeActe, ChampActe);
        if (!await DepotsRcsV2.PieceExisteAsync(geste, acte, ct)) return IssueGeste.Refuser($"Acte inconnu dans la base du greffe : « {acte} ».");

        var (depot, refus) = await DepotsRcsV2.EcrireAsync(geste, new DepotsRcsV2.DepotSaisi("DASBEN", entite,
            Date(S(EtapeActes, ChampDateArrivee))!.Value, Date(S(EtapeActes, ChampDateDepot))!.Value, "N", "RDB", EnvoiInpi: false,
            Gratuit: code == "BEN", SaisieSimplifiee: true, EtatVax: "0101", code, DateEffet: null, TexteEvenement, acte,
            Date(S(EtapeActe, ChampDateActe))!.Value, DescriptionActe: null), ct);
        if (depot is null) return IssueGeste.Refuser(refus!);
        return IssueGeste.Ecrit(
            $"Dépôt {depot.NumeroDepot} enregistré dans la base du greffe pour {entite.Designation} ({entite.Numero}), arrivée {depot.NumeroArrivee} ; " +
            "la facture, les éditions et les pièces dématérialisées ne sont pas reprises par la V3.",
            [depot.IdDemande.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            DepotsRcsV2.Trace("dépôt bénéficiaires", depot, entite, code));
    }

    /// <summary>Les codes que la liste de RIG v2 propose pour <paramref name="e"/> (OPE_ACTES.cs:33-55, DossierRCS.cs:2500-2510).</summary>
    private static async Task<IReadOnlyList<string>> CodesProposesAsync(GesteEnCours geste, DepotsRcsV2.EntiteRcs e, CancellationToken ct)
    {
        await using (var r = await geste.LireAsync($"""
            SELECT TOP (1) 1 FROM rec.ActionRegistre AS a JOIN v2.ACTE_DEPOSE AS ad ON ad.ACTDP_ID_DPTRC = a.IdDepot
            WHERE a.{(e.EstDossier ? "IdDossier" : "IdNonInscrit")} = @id AND ad.ACTDP_ABREGE_PIECE IN ('BEN', 'BENH', 'BENM')
            """, ct, new SqlParameter("@id", e.Id)))
            if (await r.ReadAsync(ct)) return ["BENM"];
        if (e.EstDossier) return e.Immatriculation < new DateTime(2017, 8, 1) ? ["BENH", "BENM"] : ["BEN", "BENM"];
        var codes = new List<string>();
        await using (var r = await geste.LireAsync("SELECT R_CODEEVENEM_VALEUR FROM v2.CODE_EVENEMENT_RCS WHERE R_CODEEVENEM_AMITEL = '1099' ORDER BY R_CODEEVENEM_VALEUR", ct))
            while (await r.ReadAsync(ct)) codes.Add(r.GetString(0).Trim());
        return codes;
    }
}
