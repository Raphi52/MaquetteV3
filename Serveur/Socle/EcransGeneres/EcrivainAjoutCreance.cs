using System.Globalization;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Ajouter une créance » (PROC_ADDCREANCE) : CREER une creance d'une procedure collective, comme le formulaire d'ajout de RIG v2
/// lance avec la PC (parametre IdPC) et integre aussitot (Integrer = vrai par defaut, PROC_ADDCREANCE.cs:160-166). Mode d'emploi :
/// EcrivainEcran.cs ; la liste : LecteurCreancesTransitoires.cs ; l'integration : CreancesPcV2.cs. Plan « RIG v3 100 % fonctionnel »,
/// lot 12, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_INTEGCREANCE\ADDCREA\ -- PROC_ADDCREANCE.cs (creation de la
/// transitoire :195-214, sauvegarde et integration :302-339), ETP_CREANCE\OPE_CREANCE.cs (montants, dates, mode, controles :298-361),
/// ETP_CREANCIER\OPE_CREANCIER(.Designer).cs (civilite, designation, adresse : MetierObligatoire).
/// CONTROLES, avant toute ecriture, au message de RIG v2 :
///   - le numero de la PC (le parametre IdPC de l'appelant ; sans lui la V2 echoue « PC absente en base », IntegrationCreances.cs:110) ;
///   - Civilité, Désignation, Adresse : obligatoires (OPE_CREANCIER.Designer.cs, MetierObligatoire) -- « « X » est obligatoire. » ;
///   - au moins un montant (OPE_CREANCE.cs:315-320) ; un montant illisible est refuse (la case montant de la V2 ne l'accepte pas) ;
///   - l'etat de la creance : une valeur de ETAT_CREANCE (la liste de la V2, CreanceTransitoire.cs:862) ;
///   - puis l'integration (CreancesPcV2) : son erreur annule tout, comme la V2 qui supprime la transitoire et leve l'erreur
///     (PROC_ADDCREANCE.cs:401-408). La date de saisine n'est pas saisie a l'ajout (HideDateSaisine, PROC_ADDCREANCE.cs:246).
/// CE QUI S'ECRIT : v2.CREANCE_TRANSITOIRE (les valeurs saisies, mode admission « 0 », contestation ou defaut « 2 »,
/// PROC_ADDCREANCE.cs:203-213, fichier « AMITEL » = CreanceTransitoire.DEFAULT_FILE_NAME), puis ce qu'ecrit l'integration en
/// admission (CreancesPcV2 : affaire, demande INTCREA, creancier PC, creance PC, PC, transitoire integree).
/// NON REPRIS (nomme) : la demande propre au formulaire (« Ajout &lt;créancier&gt; », :347-360, quand il en a une), le mandataire et le
/// cabinet saisis, l'adresse en fiche (ses lignes restent sur la transitoire), et ce que CreancesPcV2 nomme.
/// </summary>
internal sealed class EcrivainAjoutCreance : IEcrivainEcran
{
    public string Procedure => "PROC_ADDCREANCE";
    public string Ecran => "Ajouter une créance";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Creer];

    /// <summary>Les marches et les champs du descripteur PROC_ADDCREANCE (Facturer.cs), au mot pres.</summary>
    internal const string EtapePc = "Procédure collective", EtapeCreancier = "Créancier", EtapeMode = "Mode", EtapeMontants = "Montants",
                          EtapeCreance = "Créance", EtapeDates = "Dates";
    internal const string ChampPc = "Numéro de PC", ChampCivilite = "Civilité", ChampDesignation = "Désignation", ChampSiren = "N° SIREN",
                          ChampReferences = "Références", ChampAdresse = "Adresse", ChampCodePostal = "Code postal", ChampVille = "Ville",
                          ChampContestation = "Contestation", ChampDefaut = "Défaut de réponse", ChampEtat = "Etat", ChampNature = "Nature",
                          ChampObservations = "Observations", ChampDateActe = "Date acte";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        string S(string etape, string champ) => Saisi(geste, etape, champ);
        if (S(EtapePc, ChampPc).Length == 0) return IssueGeste.Refuser(Obligatoire($"{EtapePc} › {ChampPc}"));
        foreach (var champ in new[] { ChampCivilite, ChampDesignation, ChampAdresse })
            if (S(EtapeCreancier, champ).Length == 0) return IssueGeste.Refuser(Obligatoire($"{EtapeCreancier} › {champ}"));

        var montants = new Dictionary<string, decimal?>();
        foreach (var (libelle, suffixe) in CreancesPcV2.Montants)
        {
            var saisi = S(EtapeMontants, libelle);
            if (saisi.Length == 0) { montants[suffixe] = null; continue; }
            if (CriteresConsultation.Montant(saisi) is not { } m)
                return IssueGeste.Refuser($"« {EtapeMontants} › {libelle} » : montant illisible ({saisi}).");
            montants[suffixe] = decimal.Round(m, 2, MidpointRounding.AwayFromZero);
        }
        if (montants.Values.All(m => m is null)) return IssueGeste.Refuser(CreancesPcV2.MontantObligatoire);

        var contestee = CriteresConsultation.OuiNon(S(EtapeMode, ChampContestation)) == true;
        var defaut = !contestee && CriteresConsultation.OuiNon(S(EtapeMode, ChampDefaut)) == true;
        var etat = S(EtapeCreance, ChampEtat);
        if (etat.Length == 0) etat = contestee || defaut ? "2" : "0";
        else
        {
            await using var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.ETAT_CREANCE WHERE R_ETATCREANC_VALEUR = @e", ct,
                new Microsoft.Data.SqlClient.SqlParameter("@e", etat));
            await r.ReadAsync(ct);
            if (r.GetInt32(0) == 0)
                return IssueGeste.Refuser($"« {EtapeCreance} › {ChampEtat} » : « {etat} » n'est pas un état de créance du greffe (ETAT_CREANCE).");
        }
        var dateSaisie = S(EtapeDates, ChampDateActe);
        var dateActe = dateSaisie.Length > 0 ? Date(dateSaisie) : null;
        if (dateSaisie.Length > 0 && dateActe is null)
            return IssueGeste.Refuser($"« {EtapeDates} › {ChampDateActe} » : date illisible ({dateSaisie}).");

        string? Ou(string etape, string champ) => S(etape, champ) is { Length: > 0 } v ? v : null;
        var valeurs = new Dictionary<string, object?>
        {
            ["CRTRA_DATE_CREATION"] = geste.Maintenant, ["CRTRA_NUM_PC"] = CriteresConsultation.Numero(S(EtapePc, ChampPc)),
            ["CRTRA_CIVILITE_CR"] = S(EtapeCreancier, ChampCivilite), ["CRTRA_NOM_CR"] = S(EtapeCreancier, ChampDesignation),
            ["CRTRA_SIREN"] = Ou(EtapeCreancier, ChampSiren)?.Replace(" ", "", StringComparison.Ordinal), ["CRTRA_REFERENCES_CR"] = Ou(EtapeCreancier, ChampReferences),
            ["CRTRA_LIGNE_ADRESSE_CR"] = S(EtapeCreancier, ChampAdresse), ["CRTRA_CP_CR"] = Ou(EtapeCreancier, ChampCodePostal),
            ["CRTRA_VILLE_CR"] = Ou(EtapeCreancier, ChampVille), ["CRTRA_NATURE_CREANCE_ADM"] = Ou(EtapeCreance, ChampNature),
            ["CRTRA_OBSERVATIONS"] = Ou(EtapeCreance, ChampObservations), ["CRTRA_ETAT_CREANCE"] = etat,
            ["CRTRA_CREANCE_IS_CONTESTE"] = contestee, ["CRTRA_DEFAUT_REPONSE"] = defaut, ["CRTRA_NOM_FICHIER"] = "AMITEL",
            ["CRTRA_DATE_ACTE"] = dateActe,
        };
        foreach (var (suffixe, m) in montants) valeurs["CRTRA_" + suffixe] = m;
        var id = await geste.InsererAsync("v2.CREANCE_TRANSITOIRE", "CRTRA_ID_CRTRA", valeurs, ct);

        var transitoire = await CreancesPcV2.TransitoireAsync(geste, id, ct)
            ?? throw new InvalidOperationException($"Creance transitoire {id} : introuvable juste apres son insertion.");
        var (creance, erreur, _) = await CreancesPcV2.IntegrerAdmissionAsync(geste, transitoire, ct);
        if (creance is null) return IssueGeste.Refuser(erreur!);

        // fix-ok: mesure a l'ecriture du jeu PROC_ADDCREANCE.gestes -- le numero de demande (serie DMD de num.Attribuer) depend de
        // l'etat de la base et des gestes deja joues : un message qui le citait n'etait pas reproductible ; la trace le garde.
        var admis = transitoire.Admis.Where(a => a.Key != "REJETE" && a.Value is not null).Sum(a => a.Value!.Value);
        return IssueGeste.Ecrit(
            $"Créance n° {creance.NumeroOrdre} de la PC {creance.NumPc} ajoutée dans la base du greffe pour {transitoire.Nom} " +
            $"(admis {CriteresConsultation.Euros(admis)}) ; l'avis d'admission n'est pas édité par la V3.",
            [creance.IdCreancePc.ToString(CultureInfo.InvariantCulture)], CreancesPcV2.Trace(transitoire, creance));
    }
}
