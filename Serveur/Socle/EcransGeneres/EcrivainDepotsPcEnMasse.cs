using System.Globalization;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Dépôts PC en masse » (PROC_DEPPCM) : CREER le depot d'UNE etape du lot -- un numero de PC, son evenement et sa date. Mode
/// d'emploi : EcrivainEcran.cs ; la liste : LecteurDepotsPcEnMasse.cs ; le commun : DepotsPcV2.cs. Plan « RIG v3 100 %
/// fonctionnel », lot 5, 26/11/2026.
///
/// CONTROLES DE RIG v2, avant toute ecriture (un refus n'ecrit rien) :
///   1. le numero de PC est obligatoire : « Veuillez saisir un numéro de PC pour l'étape 1/1. » (_VerifierNumPcNull,
///      PROC_DEPPCM.cs:110-123 ; la saisie V3 porte une etape) ; il designe une PC de la base ;
///   2. le code de l'evenement et sa date sont obligatoires ; le code porte un depot PC (MetierDepotPcObligatoire, ETP_DEPPCM.cs:49 ;
///      ULT_COMBO_CODE_EVE_PRO.cs:79) ;
///   3. la numerotation donne le numero de depot : sinon refus, rien d'ecrit.
/// Le controle des doublons (meme PC, meme code, meme date entre deux etapes du lot, PROC_DEPPCM.cs:127) ne s'applique pas a une
/// etape seule.
///
/// CE QUI S'ECRIT : la demande du LOT (DEPPCM, validee) puis, sur la PC, sa demande fille DEPPCM validee (pc.CreateDemande,
/// OPE_ENTITE_JUD_RECHERCHE_DEPPCM.cs:102-110, DMND_ID_DMND_MERE = le lot, CloturerDemande :379-386), l'evenement survenu, la
/// renumerotation des evenements de la PC et le depot numerote (DepotsPcV2.EcrireAsync). Trace : famille « affaire », « a cree ».
///
/// NON REPRIS (nommes ; le message le dit) : la facture de chaque PC (OPE_FACTU_MASSE, _CreationFacture) ; l'edition « Liste des
/// dépôts PC créés » (LDEPPCM, PROC_DEPPCM.cs:41) et le certificat de depot (:337-374) ; un lot de plusieurs etapes (la saisie V3
/// en porte une par geste).
/// </summary>
internal sealed class EcrivainDepotsPcEnMasse : IEcrivainEcran
{
    internal const string NomEcran = "Dépôts PC en masse";

    /// <summary>Les etapes et champs du descripteur PROC_DEPPCM (Ouvrir.cs), au mot pres.</summary>
    internal const string EtapePc = "Numéro de PC", EtapeEvenement = "Événement";

    internal const string ChampNumero = "Numéro de PC", ChampCode = "Code événement", ChampDate = "Date";

    public string Procedure => "PROC_DEPPCM";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Creer];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        string S(string etape, string champ) => Saisi(geste, etape, champ);
        if (S(EtapePc, ChampNumero).Length == 0) return IssueGeste.Refuser("PC manquante : Veuillez saisir un numéro de PC pour l'étape 1/1.");
        foreach (var champ in new[] { ChampCode, ChampDate })
            if (S(EtapeEvenement, champ).Length == 0) return IssueGeste.Refuser(Obligatoire($"{EtapeEvenement} › {champ}"));
        if (Date(S(EtapeEvenement, ChampDate)) is not { } date)
            return IssueGeste.Refuser($"« {EtapeEvenement} › {ChampDate} » : date illisible ({S(EtapeEvenement, ChampDate)}).");

        var numero = S(EtapePc, ChampNumero).Replace(" ", "", StringComparison.Ordinal);
        if (await DepotsPcV2.TrouverAsync(geste, numero, ct) is not { } pc)
            return IssueGeste.Refuser($"Aucune procédure collective ne porte le numéro « {numero} » dans la base du greffe.");
        var codeSaisi = S(EtapeEvenement, ChampCode);
        if (await DepotsPcV2.CodeAsync(geste, codeSaisi, ct) is not { } code) return IssueGeste.Refuser(DepotsPcV2.CodeNonPropose(codeSaisi));

        var (lot, refusLot) = await CreerDemandeAsync(geste, "DEPPCM", "V", ct);
        if (lot is null) return IssueGeste.Refuser(refusLot!);
        var (depot, refus) = await DepotsPcV2.EcrireAsync(geste, "DEPPCM", pc, code, date, lot.Id, ct);
        if (depot is null) return IssueGeste.Refuser(refus!);
        return IssueGeste.Ecrit(
            $"Dépôt {depot.NumeroDepot} créé dans la base du greffe pour la PC {pc.Numero} ({code.Code} du {date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}) ; " +
            "la facture, la liste des dépôts créés et le certificat de dépôt ne sont pas repris par la V3.",
            [depot.IdDemande.ToString(CultureInfo.InvariantCulture), lot.Id.ToString(CultureInfo.InvariantCulture)],
            DepotsPcV2.Trace("dépôt PC en masse", depot, pc, code, date));
    }
}
