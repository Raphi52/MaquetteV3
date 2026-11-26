using System.Globalization;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Dépôts d'événement PC » (PROC_DEP) : ENREGISTRER le depot d'un evenement survenu sur une procedure collective (processus
/// DEPPC). Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurDepotsEvenementPc.cs ; le commun : DepotsPcV2.cs. Plan « RIG v3
/// 100 % fonctionnel », lot 5, 26/11/2026.
///
/// CONTROLES DE RIG v2, avant toute ecriture (un refus n'ecrit rien) :
///   1. la procedure est obligatoire (l'etape de recherche ; sans elle, pas de saisie, PROC_DEPPC.cs:41-58) et designe une PC de
///      la base ; une PC tenue par une autre demande reste en lecture seule (procedureCollective.Lock, PROC_DEPPC.cs:45-53) :
///      refus au message de RIG v2 (VerrouDemandeV2) ;
///   2. le code de l'evenement et sa date sont obligatoires (OPE_EVENEMENT) ; le code est un de ceux que la liste propose :
///      avec depot PC (ULT_COMBO_CODE_EVE_PRO.cs:79, le depot etant obligatoire : PROC_DEPPC.cs:33-36) ;
///   3. la numerotation donne le numero de depot (serie de la categorie du code) : sinon refus, rien d'ecrit.
///
/// CE QUI S'ECRIT (DepotsPcV2.EcrireAsync) : la demande DEPPC validee sur la PC (2P), l'evenement survenu, la renumerotation des
/// evenements de la PC, le depot numerote. Trace : famille « affaire », « a cree ».
///
/// NON REPRIS (nommes ; le message le dit) : la facture (forfait de la PC, sinon articles DEPPC : PROC_DEPPC.cs:66-72) ; le
/// certificat de depot et les editions (OPE_DEPOT.cs:234-280) ; le texte calcule de l'evenement (ConstructionTexte), les
/// evenements a venir automatiques, les mentions et l'annonce BODACC qu'un evenement peut declencher ; la saisie des organes
/// et dirigeants ; le depot sur une PREVENTION (PROC_DEPPRV, DEPPR) -- la liste le lit, la V3 ne l'ecrit pas.
/// </summary>
internal sealed class EcrivainDepotsEvenementPc : IEcrivainEcran
{
    internal const string NomEcran = "Dépôts d'événement PC";

    /// <summary>Les etapes et champs du descripteur PROC_DEP (Ouvrir.cs), au mot pres.</summary>
    internal const string EtapeProcedure = "Procédure", EtapeEvenement = "Événement survenu";

    internal const string ChampNumero = "Numéro de procédure", ChampCode = "Code de l'événement", ChampDate = "Date";

    public string Procedure => "PROC_DEP";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Creer];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        string S(string etape, string champ) => Saisi(geste, etape, champ);
        foreach (var (etape, champ) in new[] { (EtapeProcedure, ChampNumero), (EtapeEvenement, ChampCode), (EtapeEvenement, ChampDate) })
            if (S(etape, champ).Length == 0) return IssueGeste.Refuser(Obligatoire($"{etape} › {champ}"));
        if (Date(S(EtapeEvenement, ChampDate)) is not { } date)
            return IssueGeste.Refuser($"« {EtapeEvenement} › {ChampDate} » : date illisible ({S(EtapeEvenement, ChampDate)}).");

        var numero = S(EtapeProcedure, ChampNumero).Replace(" ", "", StringComparison.Ordinal);
        if (await DepotsPcV2.TrouverAsync(geste, numero, ct) is not { } pc)
            return IssueGeste.Refuser($"Aucune procédure collective ne porte le numéro « {numero} » dans la base du greffe.");
        if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Procédure collective", DepotsPcV2.SequenceurPc,
                pc.Id.ToString(CultureInfo.InvariantCulture), ct) is { } verrou)
            return IssueGeste.Refuser(verrou);
        var codeSaisi = S(EtapeEvenement, ChampCode);
        if (await DepotsPcV2.CodeAsync(geste, codeSaisi, ct) is not { } code) return IssueGeste.Refuser(DepotsPcV2.CodeNonPropose(codeSaisi));

        var (depot, refus) = await DepotsPcV2.EcrireAsync(geste, "DEPPC", pc, code, date, mere: null, ct);
        if (depot is null) return IssueGeste.Refuser(refus!);
        return IssueGeste.Ecrit(
            $"Dépôt {depot.NumeroDepot} enregistré dans la base du greffe sur la procédure {pc.Numero} ({code.Code} du {date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}) ; " +
            (pc.Forfait ? "procédure au forfait, sans facture" : "la facture") + ", le certificat de dépôt et les éditions ne sont pas repris par la V3.",
            [depot.IdDemande.ToString(CultureInfo.InvariantCulture)],
            DepotsPcV2.Trace("dépôt d'événement PC", depot, pc, code, date));
    }
}
