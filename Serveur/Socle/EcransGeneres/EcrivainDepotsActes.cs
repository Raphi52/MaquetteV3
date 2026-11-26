using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Dépôts d'actes RCS » (PROC_DAS) : ENREGISTRER un depot d'actes VALIDE sur un dossier RCS ou un non-inscrit. Mode d'emploi :
/// EcrivainEcran.cs ; la liste : LecteurDepotsActes.cs ; le commun : DepotsRcsV2.cs. Plan « RIG v3 100 % fonctionnel », lot 5,
/// 26/11/2026.
///
/// CONTROLES DE RIG v2, avant toute ecriture (un refus n'ecrit rien) :
///   1. le dossier : l'etape de recherche (ETP_RCS) est obligatoire -- sans entite, rien ne se valide (PROC_DAS.cs:458-459) ; le
///      numero doit designer un dossier RCS ou un non-inscrit de la base (PROC_DAS.cs:409-414) ;
///   2. les champs obligatoires (MetierObligatoire) : Date de dépôt (ultDateDepot, proc_das.md:303), Code de l'évènement
///      (ultCboCodeEvenement, :325), Pièce et Date de l'acte (ultAbregePiece, ultDateActe, :339) ; le code et la piece sont ceux
///      des listes de l'etape (v2.CODE_EVENEMENT_RCS, v2.ABREGE_PIECE) ;
///   3. les codes qui modifient un exercice social (NACO, PEEXE, PEXE, EXER : ETP_DEPOT.cs:486-509) exigent une modification de
///      l'exercice que la V3 ne reprend pas : refuses, avec la regle de RIG v2 ;
///   4. la numerotation donne ses numeros (arrivee RA1, depot RD1 a RD4 selon le type RCS) : sinon refus, rien d'ecrit.
/// La date d'arrivee vide est celle du jour (pre-remplie dans RIG v2, proc_das.md:303 ; DateTime.Now, PROC_DASBEN.cs:1038).
///
/// CE QUI S'ECRIT (validation, ETP_DEPOT.cs:343-455, DepotsRcsV2.EcrireAsync) : l'action du registre (rec.ActionRegistre), le
/// depot numerote (v2.DEPOT_RCS), son evenement RCS (v2.EVENEMENT_RCS), son acte depose (v2.ACTE_DEPOSE), la demande DAS
/// validee (v2.DEMANDE). Trace : famille « dossier », « a cree », le depot et ses numeros.
///
/// NON REPRIS (nommes ; le message le dit) : la facture (articles du depot, refus et reclamations non factures 21-RCDASRET /
/// 21-RCDASREJ, proc_das.md:224) -- la demande est validee sans facture ; les editions (certificat, lettres) ; l'annonce BODACC
/// (OPE_BODACC, ETP_DEPOT.cs:431-437) ; les exercices (ci-dessus) ; le circuit dematerialise (DematDAS) ; le refus et la
/// reclamation (lettres, RECLAMATION_REFUS_A_FACTURER) ; plusieurs evenements ou plusieurs actes (la saisie V3 en porte un).
/// </summary>
internal sealed class EcrivainDepotsActes : IEcrivainEcran
{
    internal const string NomEcran = "Dépôts d'actes RCS";

    /// <summary>Les etapes et champs du descripteur PROC_DAS (Ouvrir.cs), au mot pres.</summary>
    internal const string EtapeDossier = "Dossier", EtapeDepot = "Dépôt", EtapeEvenement = "Évènement RCS", EtapeActes = "Actes déposés";

    internal const string ChampNumero = "N° de gestion ou de non-inscrit", ChampDateDepot = "Date de dépôt", ChampDateArrivee = "Date d'arrivée",
                          ChampGratuit = "Dépôt gratuit INPI", ChampCode = "Code de l'évènement", ChampDateEffet = "Date d'effet",
                          ChampTexte = "Texte", ChampPiece = "Pièce", ChampDateActe = "Date de l'acte", ChampDescription = "Description";

    /// <summary>Les codes evenement qui portent un exercice (ETP_DEPOT.cs:488-497).</summary>
    private static readonly HashSet<string> CodesExercice = new(StringComparer.Ordinal) { "NACO", "PEEXE", "PEXE", "EXER" };

    public string Procedure => "PROC_DAS";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Creer];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        string S(string etape, string champ) => Saisi(geste, etape, champ);
        foreach (var (etape, champ) in new[]
                 {
                     (EtapeDossier, ChampNumero), (EtapeDepot, ChampDateDepot), (EtapeEvenement, ChampCode),
                     (EtapeActes, ChampPiece), (EtapeActes, ChampDateActe),
                 })
            if (S(etape, champ).Length == 0) return IssueGeste.Refuser(Obligatoire($"{etape} › {champ}"));
        DateTime? Lue(string etape, string champ) => S(etape, champ) is { Length: > 0 } v ? Date(v) : null;
        foreach (var (etape, champ) in new[] { (EtapeDepot, ChampDateDepot), (EtapeDepot, ChampDateArrivee), (EtapeEvenement, ChampDateEffet), (EtapeActes, ChampDateActe) })
            if (S(etape, champ).Length > 0 && Lue(etape, champ) is null)
                return IssueGeste.Refuser($"« {etape} › {champ} » : date illisible ({S(etape, champ)}).");

        var numero = S(EtapeDossier, ChampNumero).Replace(" ", "", StringComparison.Ordinal);
        if (await DepotsRcsV2.TrouverAsync(geste, numero, ct) is not { } entite) return IssueGeste.Refuser(DepotsRcsV2.Introuvable(numero));
        var code = S(EtapeEvenement, ChampCode);
        if (!await DepotsRcsV2.CodeEvenementExisteAsync(geste, code, ct))
            return IssueGeste.Refuser($"Code de l'évènement inconnu dans la base du greffe : « {code} ».");
        if (CodesExercice.Contains(code))
            return IssueGeste.Refuser($"Le code « {code} » modifie l'exercice social du dossier (non-approbation des comptes ou date de clôture, " +
                                      "règle de RIG v2) : l'exercice n'est pas repris par la V3, déposer dans RIG v2.");
        var piece = S(EtapeActes, ChampPiece);
        if (!await DepotsRcsV2.PieceExisteAsync(geste, piece, ct)) return IssueGeste.Refuser($"Pièce inconnue dans la base du greffe : « {piece} ».");

        var (categorie, serie, inpi) = DepotsRcsV2.NumerotationDepotActes(entite);
        var gratuit = S(EtapeDepot, ChampGratuit) is "true" or "1" or "oui" or "Oui";
        var (depot, refus) = await DepotsRcsV2.EcrireAsync(geste, new DepotsRcsV2.DepotSaisi("DAS", entite,
            Lue(EtapeDepot, ChampDateArrivee) ?? geste.Maintenant.Date, Lue(EtapeDepot, ChampDateDepot)!.Value, categorie, serie, inpi, gratuit,
            SaisieSimplifiee: false, EtatVax: null, code, Lue(EtapeEvenement, ChampDateEffet), S(EtapeEvenement, ChampTexte), piece,
            Lue(EtapeActes, ChampDateActe)!.Value, S(EtapeActes, ChampDescription)), ct);
        if (depot is null) return IssueGeste.Refuser(refus!);
        return IssueGeste.Ecrit(
            $"Dépôt {depot.NumeroDepot} enregistré dans la base du greffe pour {entite.Designation} ({entite.Numero}), arrivée {depot.NumeroArrivee} ; " +
            "la facture, les éditions et l'annonce BODACC ne sont pas reprises par la V3.",
            [depot.IdDemande.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            DepotsRcsV2.Trace("dépôt d'actes", depot, entite, code));
    }
}
