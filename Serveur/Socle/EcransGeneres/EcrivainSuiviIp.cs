using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurSuiviIp;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Suivi des preventions » (PROC_IPSUIVI) : « Valider » sur la fiche d'UNE injonction de payer (Id = l'instance IP, la cle de
/// LecteurSuiviIp.cs) enregistre la date saisie -- desistement, signification (et « à personne »), certificat de non-opposition,
/// opposition, reception de la LRAR -- comme RIG v2 : l'evenement attendu SURVIENT, l'evenement suivant est cree, ses editions
/// partent en file. Mode d'emploi : EcrivainEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 8, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_IPSUIVI\Etape\OPE_IPSUIVI.cs -- le choix du traitement
/// (_TypeTraitement, :168-226), les dates saisies (ult_Signification_ChangementValeur :2769-2875, ult_Opposition_ChangementValeur
/// :2877-2935, ult_Certif_Non_Opposition_ChangementValeur :2950-3017), leurs controles (VerifierSaisie, :2696-2766), les
/// traitements de PreparerObjectsEtEditions (:1762-2140), les grisages du chargement (Remplir_IP, :845-1125, et _VerifierCoherenceIP,
/// :1149-1160), une seule date par validation (_SetULTsAccessibility, :1196-1250, sauf signification + certificat, :101-103), la
/// note (:2150) et la demande du processus validee (MajListeDemande, :2492-2520) ; le moteur des evenements de RigMetier
/// (JUDICIAIRE\EvenementProcedure.cs : PasserEnSurvenuEveProFutur :1968-1997, creationEvenementFutur :1769-1810 ; les delais du
/// futur, Fonctions diverses\Outils.cs:520-565) ; les editions : EditionsEvenement.cs.
///
/// CONTROLES, dans l'ordre de RIG v2, avant toute ecriture (un refus n'ecrit rien) :
///   1. l'instance est une IP de la base (InstanceIP_EnCours n'accepte qu'une IP, :135-143) ; le verrou de RIG v2 (VerrouDemandeV2) ;
///   2. les alertes de Remplir_IP au mot pres (:976-1050) : desistement survenu, IP europeenne, IP rejetee ; decision sans date --
///      « Date de la décision vide, vous devez aller en ORDOSIGN. » sauf pour le desistement (:1004-1013) ; opposition enrolee (instance
///      TIP1 jointe, :1014-1021) ; aucun evenement futur (:1024-1050) ; une IP close (evenement de fin survenu, :1112-1114) ; les
///      anomalies de _VerifierCoherenceIP (futurs de l'autre loi, plusieurs futurs du meme type) ;
///   3. les dates que RIG v2 laisse saisir sur CETTE IP (Modifiable de la fiche, ZonesModifiables) ; une seule a la fois ;
///   4. les controles de saisie, au mot pres (VerifierSaisie) ; « Date incorrect » pour le desistement (:1764-1767).
///
/// CE QUI S'ECRIT (loi « nouvelle » = decision acceptee rendue a partir du 01/03/2022, DecisionJudiciaireExtentions.cs:13, :29-34) :
///   Desistement   : l'evenement IPDESIN SURVENU a la date (:1768-1770) ; la note « Information » completee de « Désistement »
///                   (:3307-3310) ;
///   Signification : l'evenement attendu IPSIGNIF (nouvelle loi) ou ISIGNI SURVIENT a la date, « à personne » pose (cree s'il
///                   n'est pas attendu, :2795-2803) ; son evenement suivant est cree (IPCERTNOPP, IOIP...) sauf si une opposition
///                   ou un certificat est deja survenu ou un certificat deja attendu (:2809-2822) ;
///   Certificat    : l'evenement IPCERTNOPP attendu SURVIENT (ou est cree survenu) ; l'opposition IPOPOS attendue EXPIRE
///                   (:1887-1910) ;
///   Opposition    : l'evenement attendu IPOPOS (nouvelle loi) ou IOIP SURVIENT avec son suivant (IPAROPOS) ; le certificat attendu
///                   EXPIRE (:2894-2925, :1962-1970) ;
///   Reception LRAR: l'evenement attendu IAROIP / IPAROPOS SURVIENT, avec son suivant (la provision attendue) si aucune provision
///                   n'est survenue (:1981-2024) ;
///   et toujours : les EDITIONS de l'evenement survenu (CreateDemandeEdition ; EditionsEvenement.cs) et la v2.DEMANDE « IPSUIVI »
///   validee, liee a l'IP (2J). Un evenement suivant porte le numero source de celui qui l'a cree (EVNPR_NUMERO_SOURCE_EP_LIE) et
///   les dates du code (mois, jours ou semaines de CODE_EVE_PRO apres la date survenue).
///
/// NON REPRIS (refuse en le disant, rien n'est ecrit) : la PROVISION (et la LRAR saisie avec elle) -- dans les delais, RIG v2
/// enrole l'opposition au fond (instance J « TIP1 », demande OIPE, evenements de creation) et facture la provision ; hors delai, il
/// constate la caducite (OP « IPCADSO » ou evenement du reglage) (:1981-2100, :3029-3100) ; une signification de l'ancienne loi
/// apres sa date maxi, sans opposition (non-avenue : OP « IPNAVSO » ou evenement du reglage, :1830-1866) ; une opposition de
/// l'ancienne loi apres sa date maxi (titre executoire, LRAR / LR, :1926-1966) ; le second executoire (« Editer exécutoire »,
/// IPExecu) ; la facture du certificat ; les pieces scannees et le code-barre ; le tableau recapitulatif (:2520-2600). Ce que les
/// editions ne reprennent pas est dit par EditionsEvenement.
/// </summary>
internal sealed class EcrivainSuiviIp : IEcrivainEcran
{
    public string Procedure => "PROC_IPSUIVI";
    public string Ecran => "Suivi des préventions";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    /// <summary>« Date incorrect » : l'erreur de la date de desistement (OPE_IPSUIVI.cs:1766), au mot pres.</summary>
    internal const string DateIncorrecte = "Date incorrect";

    /// <summary>L'alerte de Remplir_IP sans evenement futur (OPE_IPSUIVI.cs:1031), au mot pres.</summary>
    internal const string AucunFutur = "Aucun évènement futur, traitement impossible en IPSUIVI vous devez aller en MJUD.";

    private const string UneSeuleDate =
        "Une seule date par validation : RIG v2 grise les autres dès qu'une date est saisie (OPE_IPSUIVI.cs:1196-1250), sauf la signification avec le certificat de non-opposition (:101-103).";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // 1. L'IP et le verrou.
        if (geste.Demande.Ids is not [var id] || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            return IssueGeste.Refuser($"Injonction de payer introuvable dans la base du greffe ({string.Join(", ", geste.Demande.Ids)}).");
        var colIns = await RelancesRsurv.ColonnesAsync(geste, "v2.INSTANCE", ct);
        var colEve = await RelancesRsurv.ColonnesAsync(geste, "v2.EVENEMENT_PROCEDURE", ct);
        EtatIp? etat = null;
        await using (var r = await geste.LireAsync(RequeteEtat(colIns, verrouiller: true), ct, new SqlParameter("@id", n)))
            if (await r.ReadAsync(ct)) etat = EtatDe(r);
        if (etat is null) return IssueGeste.Refuser($"Injonction de payer introuvable dans la base du greffe ({id}).");
        if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance, id, ct) is { } verrou)
            return IssueGeste.Refuser(verrou);

        // Les saisies : les champs de la section des actes, par libelle.
        var saisies = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (cle, valeur) in geste.Demande.Valeurs)
        {
            var champ = ChampsSaisis.FirstOrDefault(c => GesteEcran.Cle(SectionActes, c) == cle);
            if (champ is null) return IssueGeste.Refuser($"« {cle} » n'est pas une date de la fiche que RIG v2 laisse saisir.");
            saisies[champ] = valeur.Trim();
        }
        var dates = saisies.Keys.Where(c => c != ChampAPersonne).ToList();
        if (dates.Count == 0)
            return IssueGeste.Refuser(saisies.ContainsKey(ChampAPersonne)
                ? $"« {ChampAPersonne} » se saisit avec la date de signification (OPE_IPSUIVI.cs:2861-2864)."
                : "Aucune date saisie.");
        foreach (var c in dates)
            if (saisies[c].Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire($"{SectionActes} › {c}"));

        // 2. Les alertes de Remplir_IP.
        if (etat.Desiste) return IssueGeste.Refuser("Désistement IP, traitement impossible en IPSUIVI vous devez aller en MJUD.");
        if (etat.Europeenne) return IssueGeste.Refuser("Injonction de payer européenne, traitement impossible en IPSUIVI vous devez aller en MJUD.");
        if (!etat.Accepte) return IssueGeste.Refuser("IP rejetée, traitement impossible en IPSUIVI vous devez aller en MJUD.");
        if (etat.DateDecision is null && dates.Any(c => c != ChampDesistement))
            return IssueGeste.Refuser("Date de la décision vide, vous devez aller en ORDOSIGN.");
        if (etat.DateDecision is not null)
        {
            if (etat.OppositionEnrolee is { } tip1) return IssueGeste.Refuser($"Opposition à injonction de payer enrôlée sous le numéro : {tip1}.");
            if (etat.NbFuturs == 0) return IssueGeste.Refuser(AucunFutur);
            if (etat.Fin) return IssueGeste.Refuser("L'IP est close (événement de fin survenu) : RIG v2 grise toutes ses dates (OPE_IPSUIVI.cs:1112-1114).");
            if (Anomalies(etat) is { Count: > 0 } anomalies)
                return IssueGeste.Refuser($"Vous devez corriger {(anomalies.Count > 1 ? "les anomalies" : "l'anomalie")} ci-dessous en MJUD : "
                                          + Environment.NewLine + string.Join(Environment.NewLine, anomalies));
        }

        // 3. Les dates que RIG v2 laisse saisir, une a la fois.
        var modifiables = ZonesModifiables(etat);
        foreach (var c in saisies.Keys)
            if (!modifiables.Contains(c))
                return IssueGeste.Refuser($"« {SectionActes} › {c} » n'est pas modifiable sur cette IP : RIG v2 la grise (OPE_IPSUIVI.cs:976-1120).");
        if (dates.Count > 1 && !(dates.Count == 2 && dates.Contains(ChampSignification) && dates.Contains(ChampCertificat) && etat.NouvelleLoi))
            return IssueGeste.Refuser(UneSeuleDate);

        // 4. Les controles de saisie.
        DateTime? Lue(string champ) => saisies.TryGetValue(champ, out var v) ? AudiencesV2.Date(v) : null;
        foreach (var c in dates)
            if (Lue(c) is null) return IssueGeste.Refuser(c == ChampDesistement ? DateIncorrecte : $"« {SectionActes} › {c} » : date illisible ({saisies[c]}).");
        var dateIp = etat.DateIp?.Date;
        var signification = Lue(ChampSignification) ?? etat.Signification;
        var opposition = Lue(ChampOpposition) ?? etat.Opposition;
        if (Lue(ChampDesistement) is { } desistement && dateIp is { } dip0 && desistement < dip0) return IssueGeste.Refuser(DateIncorrecte);
        if (Lue(ChampSignification) is { } s)
        {
            if (dateIp is { } dip && s < dip) return IssueGeste.Refuser("La date de signification ne peut pas être inférieure à la date de l'IP");
            if (opposition is { } o && s > o) return IssueGeste.Refuser("La date de signification ne peut pas être supérieure à la date d'opposition");
            if (!etat.NouvelleLoi && etat.Opposition is null && etat.MaxiSignification is { } maxi && s > maxi)
                return IssueGeste.Refuser($"Signification après la date maxi ({FormeLigne.Date(maxi)}) : RIG v2 rend l'IP non avenue (OP « IPNAVSO » ou événement du réglage, OPE_IPSUIVI.cs:1830-1866) ; la V3 ne le reprend pas encore ici (« Injonctions de payer » applique la non-avenue à l'échéance).");
        }
        if (Lue(ChampCertificat) is { } cert && dateIp is { } dip2 && cert < dip2)
            return IssueGeste.Refuser("La date du certificat de non opposition ne peut pas être inférieure à la date de l'IP");
        if (Lue(ChampOpposition) is { } op)
        {
            if (dateIp is { } dip && op < dip) return IssueGeste.Refuser("La date de l'opposition ne peut pas être inférieure à la date de l'IP");
            if (signification is { } sg && op < sg) return IssueGeste.Refuser("La date de l'opposition ne peut pas être inférieure à la date de la signification");
            if (!etat.NouvelleLoi && etat.MaxiOpposition is { } maxi && op > maxi)
                return IssueGeste.Refuser($"Opposition après la date maxi ({FormeLigne.Date(maxi)}) : RIG v2 traite l'opposition hors délai de l'ancienne loi (titre exécutoire, LRAR ou LR, OPE_IPSUIVI.cs:1926-1966) ; la V3 ne le reprend pas encore.");
        }
        if (Lue(ChampAr) is { } ar)
        {
            if (dateIp is { } dip && ar < dip) return IssueGeste.Refuser("La date de réception du LRAR ne peut pas être inférieur à la date de l'IP");
            if (opposition is { } o && ar < o) return IssueGeste.Refuser("La date de réception du LRAR ne peut pas être inférieur à la date de l'opposition");
            if (etat.Provision is { } pv && ar > pv) return IssueGeste.Refuser("La date de provision ne peut pas être supérieur à la date de réception du LRAR");
        }
        if (saisies.ContainsKey(ChampProvision))
            return IssueGeste.Refuser("« Provision » : dans les délais, RIG v2 enrôle l'opposition au fond (instance J « TIP1 », demande OIPE) et facture la provision ; "
                                      + "hors délai, il constate la caducité (OPE_IPSUIVI.cs:2028-2100, :3029-3100). La V3 ne le reprend pas encore : rien n'est enregistré.");

        // Les ecritures : la demande d'abord (les editions s'y rattachent), puis chaque date dans l'ordre de _TypeTraitement.
        var (idDemande, numeroDemande, refusDemande) = await EcrivainSurveillanceIp.DemandeInstanceAsync(geste, "IPSUIVI", n, etat.Affaire,
                                                                                                          etat.NomCourant ?? etat.Role, etat.Role, ct);
        if (idDemande is null) return IssueGeste.Refuser(refusDemande!);
        var faits = new List<string>();
        var bilans = new List<EditionsEvenement.Bilan>();
        var apres = new Dictionary<string, string?> { ["Demande"] = numeroDemande };
        async Task Editions(int idEvenement) => bilans.Add(await EditionsEvenement.CreerAsync(geste, idDemande.Value, numeroDemande, idEvenement, ct));

        if (Lue(ChampDesistement) is { } dd)
        {
            var idEve = await EcrivainRetourAudience.EvenementSurvenuAsync(geste, n, "IPDESIN", dd, ct);
            await Editions(idEve);
            var noteAvant = etat.Note ?? "";
            var noteApres = noteAvant.Contains("Désistement", StringComparison.Ordinal) ? noteAvant : (noteAvant + " Désistement").Trim();
            if (colIns.Contains("INSTN_NOTE_AUDIENCE") && noteApres != noteAvant)
                await geste.EcrireAsync("UPDATE v2.INSTANCE SET INSTN_NOTE_AUDIENCE = @note WHERE INSTN_ID_INSTN = @id", ct,
                    new SqlParameter("@note", noteApres), new SqlParameter("@id", n));
            faits.Add($"désistement du {FormeLigne.Date(dd)} (événement IPDESIN)");
            apres["Desistement"] = FormeLigne.Date(dd);
            apres["Information"] = noteApres;
        }
        if (Lue(ChampSignification) is { } ds)
        {
            var code = etat.NouvelleLoi ? "IPSIGNIF" : "ISIGNI";
            var aPersonne = saisies.TryGetValue(ChampAPersonne, out var ap) && ap.ToLowerInvariant() is "oui" or "true" or "1";
            // Le suivant (le certificat attendu) se cree meme avec le certificat saisi : RIG v2 le cree a la saisie de la signification
            // (:2809-2822), puis le traitement du certificat le fait survenir (:1891-1896).
            var lie = !etat.OppositionSurvenue && etat.Certificat is null && !etat.FuturCertificat;
            var (idEve, suivant) = await SurvenirAsync(geste, n, code, ds, aPersonne, lie, colEve, ct);
            await Editions(idEve);
            faits.Add($"signification du {FormeLigne.Date(ds)}{(aPersonne ? " à personne" : "")} (événement {code}{(suivant is null ? "" : $", {suivant} attendu")})");
            apres["Signification"] = FormeLigne.Date(ds);
        }
        if (Lue(ChampCertificat) is { } dc)
        {
            var (idEve, _) = await SurvenirAsync(geste, n, "IPCERTNOPP", dc, null, false, colEve, ct);
            await Editions(idEve);
            await ExpirerFutursAsync(geste, n, "IPOPOS", colEve, ct);
            faits.Add($"certificat de non-opposition du {FormeLigne.Date(dc)} (événement IPCERTNOPP)");
            apres["Certificat"] = FormeLigne.Date(dc);
        }
        if (Lue(ChampOpposition) is { } dop)
        {
            var code = etat.NouvelleLoi ? "IPOPOS" : "IOIP";
            var (idEve, suivant) = await SurvenirAsync(geste, n, code, dop, null, true, colEve, ct);
            await Editions(idEve);
            await ExpirerFutursAsync(geste, n, "IPCERTNOPP", colEve, ct);
            faits.Add($"opposition du {FormeLigne.Date(dop)} (événement {code}{(suivant is null ? "" : $", {suivant} attendu")})");
            apres["Opposition"] = FormeLigne.Date(dop);
        }
        if (Lue(ChampAr) is { } dar)
        {
            var code = etat.CodeFuturAr ?? (etat.NouvelleLoi ? "IPAROPOS" : "IAROIP");
            var (idEve, suivant) = await SurvenirAsync(geste, n, code, dar, null, etat.Provision is null, colEve, ct);
            await Editions(idEve);
            faits.Add($"réception de la LRAR du {FormeLigne.Date(dar)} (événement {code}{(suivant is null ? "" : $", {suivant} attendu")})");
            apres["ReceptionLrar"] = FormeLigne.Date(dar);
        }

        var message = $"Enregistré sur l'IP {etat.Role} : {string.Join(" ; ", faits)}.{EditionsEvenement.Message(bilans)} Non repris par la V3 : le tableau récapitulatif.";
        return IssueGeste.Ecrit(message, [id],
            new TraceGeste("affaire", $"l'injonction de payer {etat.Role}", id, "a modifie",
                new Dictionary<string, string?> { ["Information"] = etat.Note }, apres));
    }

    /// <summary>Les anomalies de _VerifierCoherenceIP (OPE_IPSUIVI.cs:1149-1176), au mot pres.</summary>
    private static List<string> Anomalies(EtatIp e)
    {
        var messages = new List<string>();
        if (e.NouvelleLoiDecision && e.NbFuturIsigni > 0) messages.Add("- Vous avez des évènements futurs 'ISIGNI' au lieu de 'IPSIGNIF'");
        if (!e.NouvelleLoiDecision && e.NbFuturIpsignif > 0) messages.Add("- Vous avez des évènements futurs 'IPSIGNIF' au lieu de 'ISIGNI'");
        if (e.NouvelleLoiDecision && e.NbFuturIoip > 0) messages.Add("- Vous avez des évènements futurs 'IOIP' au lieu de 'IPOPOS'");
        if (!e.NouvelleLoiDecision && e.NbFuturIpopos > 0) messages.Add("- Vous avez des évènements futurs 'IPOPOS' au lieu de 'IOIP'");
        if (e.NbFuturIsigni + e.NbFuturIpsignif > 1) messages.Add("- Vous avez plusieurs évènements futurs de signification ('ISIGNI','IPSIGNIF')");
        if (e.NbFuturIoip + e.NbFuturIpopos > 1) messages.Add("- Vous avez plusieurs évènements futurs d'opposition ('IOIP','IPOPOS')");
        if (e.NbFuturAr > 1) messages.Add("- Vous avez plusieurs évènements futurs de LRAR provision ('IAROIP','IPAROPOS')");
        if (e.NbFuturConso > 1) messages.Add("- Vous avez plusieurs évènements futurs de provision ('ICONSOIP','IPCONSOPOS')");
        if (e.NbFuturCertif > 1) messages.Add("- Vous avez plusieurs évènements futurs de certificat de non opposition ('IPCERTNOPP')");
        return messages;
    }

    // ------------------------------------------------------------------ LES EVENEMENTS

    /// <summary>L'evenement attendu <paramref name="code"/> de l'instance SURVIENT a <paramref name="date"/> (PasserEnSurvenuEveProFutur,
    /// EvenementProcedure.cs:1968-1997 : dates mini, maxi et fixe effacees, texte a reconstruire) ; sans evenement attendu, il est
    /// d'abord cree attendu a cette date (AddEvenementProcedureFuturOuExpire("", code, false, date), OPE_IPSUIVI.cs:2795-2803).
    /// <paramref name="lie"/> : son suivant (R_CODEEVEPRO_CODE_EVE_PRO_FUTUR) est cree attendu, aux dates du code
    /// (creationEvenementFutur, :1769-1810). Rend l'id survenu et le code du suivant cree.</summary>
    private static async Task<(int Id, string? Suivant)> SurvenirAsync(GesteEnCours geste, int idInstance, string code, DateTime date, bool? aPersonne,
        bool lie, HashSet<string> colEve, CancellationToken ct)
    {
        var idFutur = await RelancesRsurv.Scalaire(geste, """
            SELECT TOP (1) EVNPR_ID_EVNPR FROM v2.EVENEMENT_PROCEDURE
            WHERE EVNPR_ID_INSTN = @i AND EVNPR_CODE_EVE_PRO = @c AND ISNULL(EVNPR_SURVENU, 0) = 0 AND ISNULL(EVNPR_DELAI_EXPIRE, 0) = 0
            ORDER BY EVNPR_NUM_ORDRE, EVNPR_ID_EVNPR
            """, ct, new SqlParameter("@i", idInstance), new SqlParameter("@c", code)) is int f ? f : (int?)null;
        var id = idFutur ?? await EvenementAttenduAsync(geste, idInstance, code, date, null, null, null, colEve, ct);
        var sets = new List<string> { "EVNPR_SURVENU = 1", "EVNPR_DATE = @d", "EVNPR_DATE_MAXI = NULL", "EVNPR_DATE_MINI = NULL", "EVNPR_DATE_FIXE = NULL" };
        if (colEve.Contains("EVNPR_TEXTE")) sets.Add("EVNPR_TEXTE = NULL");
        if (aPersonne is { } p && colEve.Contains("EVNPR_SIGNIF_A_PERSONNE")) sets.Add($"EVNPR_SIGNIF_A_PERSONNE = {(p ? 1 : 0)}");
        await geste.EcrireAsync($"UPDATE v2.EVENEMENT_PROCEDURE SET {string.Join(", ", sets)} WHERE EVNPR_ID_EVNPR = @id", ct,
            new SqlParameter("@d", date), new SqlParameter("@id", id));
        if (!lie) return (id, null);

        // Le suivant et ses dates (Outils.CalculDate{Maxi,Mini,Fixe}EveProSurvenuInstanceMesure : mois, sinon jours, sinon semaines).
        var colCode = await RelancesRsurv.ColonnesAsync(geste, "v2.CODE_EVE_PRO", ct);
        if (!colCode.Contains("R_CODEEVEPRO_CODE_EVE_PRO_FUTUR")) return (id, null);
        string Col(string c) => colCode.Contains(c) ? c : $"CAST(NULL AS int) AS {c}";
        string? suivant = null;
        DateTime? maxi = null, mini = null, fixe = null;
        await using (var r = await geste.LireAsync($"""
            SELECT R_CODEEVEPRO_CODE_EVE_PRO_FUTUR,
                   {Col("R_CODEEVEPRO_MOIS_DATE_MAXI")}, {Col("R_CODEEVEPRO_JOURS_DATE_MAXI")}, {Col("R_CODEEVEPRO_SEMAINES_DATE_MAXI")},
                   {Col("R_CODEEVEPRO_MOIS_DATE_MINI")}, {Col("R_CODEEVEPRO_JOURS_DATE_MINI")}, {Col("R_CODEEVEPRO_SEMAINES_DATE_MINI")},
                   {Col("R_CODEEVEPRO_MOIS_DATE_FIXE")}, {Col("R_CODEEVEPRO_JOURS_DATE_FIXE")}, {Col("R_CODEEVEPRO_SEMAINES_DATE_FIXE")}
            FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c
            """, ct, new SqlParameter("@c", code)))
            if (await r.ReadAsync(ct))
            {
                suivant = FormeLigne.Texte(r, "R_CODEEVEPRO_CODE_EVE_PRO_FUTUR").Trim() is { Length: > 0 } s ? s : null;
                DateTime? Delai(string borne)
                {
                    int? N(string c) => r[c] is DBNull ? null : Convert.ToInt32(r[c], CultureInfo.InvariantCulture);
                    if (N($"R_CODEEVEPRO_MOIS_DATE_{borne}") is { } m) return date.AddMonths(m);
                    if (N($"R_CODEEVEPRO_JOURS_DATE_{borne}") is { } j) return date.AddDays(j);
                    if (N($"R_CODEEVEPRO_SEMAINES_DATE_{borne}") is { } sem) return date.AddDays(sem * 7);
                    return null;
                }
                (maxi, mini, fixe) = (Delai("MAXI"), Delai("MINI"), Delai("FIXE"));
            }
        if (suivant is null) return (id, null);
        var source = await RelancesRsurv.Scalaire(geste, "SELECT EVNPR_NUMERO_SOURCE FROM v2.EVENEMENT_PROCEDURE WHERE EVNPR_ID_EVNPR = @id", ct,
                                                  new SqlParameter("@id", id)) as string;
        await EvenementAttenduAsync(geste, idInstance, suivant, maxi, mini, fixe, source, colEve, ct);
        return (id, suivant);
    }

    /// <summary>Un evenement ATTENDU (futur) sur l'instance : Instance.AddEvenementProcedureFuturOuExpire (Instance.cs:571-600), numero
    /// d'ordre = nombre d'evenements + 1 (AddEvenementProcedure, :588-594), numero source neuf, lie a <paramref name="lie"/>.</summary>
    private static async Task<int> EvenementAttenduAsync(GesteEnCours geste, int idInstance, string code, DateTime? maxi, DateTime? mini, DateTime? fixe,
        string? lie, HashSet<string> colEve, CancellationToken ct)
    {
        var type = await RelancesRsurv.Scalaire(geste, "SELECT R_CODEEVEPRO_TYPE_EVE_PRO FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c", ct,
                                                new SqlParameter("@c", code)) as string;
        var rang = Convert.ToInt32(await RelancesRsurv.Scalaire(geste, "SELECT COUNT(*) FROM v2.EVENEMENT_PROCEDURE WHERE EVNPR_ID_INSTN = @i", ct,
                                                                new SqlParameter("@i", idInstance)), CultureInfo.InvariantCulture) + 1;
        var valeurs = new Dictionary<string, object?>
        {
            ["EVNPR_ID_INSTN"] = idInstance, ["EVNPR_CODE_EVE_PRO"] = code, ["EVNPR_SURVENU"] = false, ["EVNPR_DELAI_EXPIRE"] = false,
            ["EVNPR_DATE_MAXI"] = maxi, ["EVNPR_DATE_MINI"] = mini, ["EVNPR_DATE_FIXE"] = fixe, ["EVNPR_NUM_ORDRE"] = (short)rang,
            ["EVNPR_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(), ["EVNPR_NUMERO_SOURCE_EP_LIE"] = lie,
            ["EVNPR_TYPE_EVE_PRO"] = type is { Length: > 0 } t ? t[..1] : null,
        };
        return await CreationV2.InsererAsync(geste, "v2.EVENEMENT_PROCEDURE", "EVNPR_ID_EVNPR",
            valeurs.Where(v => colEve.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value), ct);
    }

    /// <summary>Les evenements attendus <paramref name="code"/> de l'instance EXPIRENT, texte a reconstruire (OPE_IPSUIVI.cs:1902-1907,
    /// :1964-1969).</summary>
    private static Task ExpirerFutursAsync(GesteEnCours geste, int idInstance, string code, HashSet<string> colEve, CancellationToken ct)
        => geste.EcrireAsync($"""
               UPDATE v2.EVENEMENT_PROCEDURE SET EVNPR_DELAI_EXPIRE = 1{(colEve.Contains("EVNPR_TEXTE") ? ", EVNPR_TEXTE = NULL" : "")}
               WHERE EVNPR_ID_INSTN = @i AND EVNPR_CODE_EVE_PRO = @c AND ISNULL(EVNPR_SURVENU, 0) = 0 AND ISNULL(EVNPR_DELAI_EXPIRE, 0) = 0
               """, ct, new SqlParameter("@i", idInstance), new SqlParameter("@c", code));
}
