using System.Globalization;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Nouvelle proc. collective » (PROC_NPC, processus NPC) : CREER une procedure collective EXTERIEURE (jugee par une autre
/// juridiction) dans la base du greffe. Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurNouvellesProcedures.cs ; le
/// commun : CreationPcV2.cs. Plan « RIG v3 100 % fonctionnel », lot 5, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_NPC cree une PC vide pre-marquee exterieure (CreateProcedureCollective(CodeGreffe) puis
/// PRCLL_EXTERIEUR = true, PROC_NPC.cs:51-53 ; fiche proc_npc.md « Mode NPC »), saisie par l'etape « Procédure collective »
/// (Rig_Etape_Instance\PROCEDURE_COLLECTIVE\Saisie\ETP_PROCEDURE_COLLECTIVE).
///
/// CONTROLES DE RIG v2, avant toute ecriture (un refus n'ecrit rien) :
///   1. champs obligatoires de l'etape (OPE_PROCEDURE_COLLECTIVE.Designer.cs, MetierObligatoire, ceux qui s'affichent pour
///      une PC exterieure) : « Nom de l'affaire » (:185) et « Juridiction extérieure » (:592, visible quand la PC est
///      exterieure, OPE_PROCEDURE_COLLECTIVE.cs:468) ; le debiteur, sans lequel la PC n'a pas d'entreprise (PRCLL_ID_RPRTR,
///      NOT NULL sur RIG_DEV) ;
///   2. le debiteur designe un dossier RCS ou un non-inscrit de la base ; la juridiction est une de la liste
///      (ULT_COMBO_JURIDICTION_EXTERIEURE.cs:17, table JURIDICTION_EXTERIEURE) ;
///   3. la date du jugement remplit UNE des quatre dates de l'etape : la nature dit laquelle (une date sans nature est
///      illisible ; sans date, la PC s'enregistre sans numero, comme la V2, OPE_PROCEDURE_COLLECTIVE.cs:1094) ;
///   4. l'adresse du client a facturer (Client.cs:963) ; la numerotation JPCEX de l'annee (sinon refus, rien d'ecrit).
///
/// CE QUI S'ECRIT (CreationPcV2.CreerAsync) : la demande NPC validee sur la PC (2P), le client « compte PC » et son adresse
/// (ou le client qui porte deja l'abrege), la PC exterieure numerotee. Trace : famille « affaire », « a cree ».
/// La PC creee se retrouve par la recherche de « Procédure collective » (PROC_MPC, LecteurProceduresCollectives.cs).
///
/// NON REPRIS (nommes ; le message le dit) : ceux de CreationPcV2 (facture, editions, etablissements, dirigeants, organes,
/// chiffres, evenements, note) ; la notification CFEC dematerialisee (idDematNotif, PROC_NPC.cs:80 : service externe, la V3
/// ne la recoit pas) ; la prevention (mode NPR, PROC_NPC.cs:105-140).
/// </summary>
internal sealed class EcrivainNouvellesProcedures : IEcrivainEcran
{
    internal const string NomEcran = "Nouvelle proc. collective";

    /// <summary>L'etape et les champs du descripteur PROC_NPC (Ouvrir.cs), au mot pres.</summary>
    internal const string EtapeProcedure = "Procédure";

    internal const string ChampDebiteur = "Débiteur", ChampNom = "Nom de l'affaire", ChampNature = "Nature",
        ChampDate = "Date du jugement", ChampJuridiction = "Juridiction extérieure";

    public string Procedure => "PROC_NPC";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Creer];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        string S(string champ) => Saisi(geste, EtapeProcedure, champ);
        foreach (var champ in new[] { ChampDebiteur, ChampNom, ChampJuridiction })
            if (S(champ).Length == 0) return IssueGeste.Refuser(Obligatoire($"{EtapeProcedure} › {champ}"));

        var dateSaisie = S(ChampDate);
        var date = dateSaisie.Length > 0 ? Date(dateSaisie) : null;
        if (dateSaisie.Length > 0 && date is null)
            return IssueGeste.Refuser($"« {EtapeProcedure} › {ChampDate} » : date illisible ({dateSaisie}).");
        var nature = CreationPcV2.Natures.FirstOrDefault(n => n.Libelle == S(ChampNature));
        if (S(ChampNature).Length > 0 && nature is null)
            return IssueGeste.Refuser($"« {EtapeProcedure} › {ChampNature} » : « {S(ChampNature)} » n'est pas une des dates de l'étape.");
        if ((nature is null) != (date is null))
            return IssueGeste.Refuser($"« {EtapeProcedure} › {ChampNature} » et « {ChampDate} » vont ensemble : la nature dit quelle date de la procédure le jugement remplit.");

        var numero = S(ChampDebiteur).Replace(" ", "", StringComparison.Ordinal);
        if (await CreationPcV2.DebiteurAsync(geste, numero, ct) is not { } debiteur)
            return IssueGeste.Refuser(CreationPcV2.DebiteurIntrouvable(numero));
        var juridiction = S(ChampJuridiction);
        if (!await CreationPcV2.JuridictionExisteAsync(geste, juridiction, ct))
            return IssueGeste.Refuser($"La juridiction extérieure « {juridiction} » n'existe pas dans la base du greffe.");

        var saisie = new CreationPcV2.PcSaisie(debiteur, S(ChampNom), nature, date, Exterieure: true, juridiction);
        var (pc, refus) = await CreationPcV2.CreerAsync(geste, "NPC", saisie, ct);
        if (pc is null) return IssueGeste.Refuser(refus!);

        var client = pc.ClientExistant ? $"client existant {pc.Abrege}" : (pc.Numero is null ? "client compte PC créé" : $"client compte PC {pc.Abrege} créé");
        var quoi = pc.Numero is null
            ? "Procédure collective extérieure enregistrée sans numéro (aucune date de jugement)"
            : $"Procédure collective extérieure {pc.Numero} enregistrée";
        return IssueGeste.Ecrit(
            $"{quoi} dans la base du greffe pour {saisie.NomAffaire} ({debiteur.Numero}, juridiction {juridiction}), {client} ; " +
            "la facture, les éditions, les établissements, dirigeants et organes ne sont pas repris par la V3.",
            [pc.Demande.Id.ToString(CultureInfo.InvariantCulture)],
            CreationPcV2.Trace(pc, saisie));
    }
}
