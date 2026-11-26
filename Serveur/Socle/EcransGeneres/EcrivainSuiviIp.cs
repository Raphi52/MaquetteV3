using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurSuiviIp;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Suivi des preventions » (PROC_IPSUIVI) : « Valider » sur la fiche d'UNE injonction de payer (Id = l'instance IP, la cle de
/// LecteurSuiviIp.cs) enregistre son DESISTEMENT. Mode d'emploi : EcrivainEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 8,
/// 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_IPSUIVI\Etape\OPE_IPSUIVI.cs -- le traitement
/// IPDesistement de PreparerObjectsEtEditions (:1762-1772), les conditions qui ouvrent la case « Désistement » au chargement de
/// l'IP (Remplir_IP, :976-1055), la note de l'instance (:2150) et la demande du processus validee (MajListeDemande, :2492-2520).
///
/// CONTROLES, dans l'ordre de RIG v2, avant toute ecriture (un refus n'ecrit rien) :
///   1. l'instance est une IP de la base (InstanceIP_EnCours n'accepte qu'une IP, :135-143) ;
///   2. le verrou de RIG v2 sur l'instance (la demande du processus, sequenceur 2J ; VerrouDemandeV2), relu dans la transaction ;
///   3. seule la date « Désistement » s'enregistre : toute autre saisie est refusee en le disant (en tete de LecteurSuiviIp.cs) ;
///   4. les alertes de Remplir_IP, au mot pres, qui ferment la fiche : un desistement deja survenu (IPDESIN, :978-988), une IP
///      europeenne (:990-996), une IP rejetee -- pas de decision, ou de type non accepte (IsAccepter,
///      DecisionJudiciaireExtentions.cs:18-24 ; :997-1003) ; une decision sans date laisse le seul desistement (:1004-1013) ; sinon
///      une opposition deja enrolee (instance TIP1 jointe, :1014-1021) et une IP sans evenement futur (:1024-1050) n'offrent pas
///      la case « Désistement » (MetierReadOnly, :785, :1027) ;
///   5. « Date incorrect » si la date de desistement precede la date de l'IP (la date de la decision, sinon sa date prevue,
///      :849-858 ; :1764-1767).
///
/// CE QUI S'ECRIT (:1768-1770, :2150, MajListeDemande) :
///   - v2.EVENEMENT_PROCEDURE : l'evenement IPDESIN SURVENU a la date saisie (EvenementProcedure.CreateEvenementProcedure ;
///     EcrivainRetourAudience.EvenementSurvenuAsync) -- il clot l'IP (la regle d'etat du lecteur, et les listes de PROC_SURVIP) ;
///   - v2.INSTANCE.INSTN_NOTE_AUDIENCE : la note « Information » complete du mot « Désistement » (ult_Desistement_ChangementValeur,
///     :3307-3310) ;
///   - v2.DEMANDE « IPSUIVI », validee, du nom, de l'indicatif et des numeros de l'IP, et son lien 2J a l'instance.
/// NON REPRIS (nommes, le message le dit) : les editions de l'evenement (epDesistement.CreateDemandeEdition : maquettes CEP_EDITION
/// et destinataires -- meme reserve qu'EcrivainRetourAudience) et le tableau recapitulatif (:2520-2600) ; les AUTRES dates de la
/// fiche (signification, opposition, LRAR, provision, certificat de non-opposition) : leurs evenements futurs lies, l'instance J
/// ou OP et la facture (PreparerObjectsEtEditions, :1774-2140) ne sont pas encore repris par la V3.
/// </summary>
internal sealed class EcrivainSuiviIp : IEcrivainEcran
{
    public string Procedure => "PROC_IPSUIVI";
    public string Ecran => "Suivi des préventions";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    /// <summary>« Date incorrect » : l'erreur de la date de desistement (OPE_IPSUIVI.cs:1766), au mot pres.</summary>
    internal const string DateIncorrecte = "Date incorrect";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // 1. L'IP.
        if (geste.Demande.Ids is not [var id] || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            return IssueGeste.Refuser($"Injonction de payer introuvable dans la base du greffe ({string.Join(", ", geste.Demande.Ids)}).");
        var colonnesIns = await RelancesRsurv.ColonnesAsync(geste, "v2.INSTANCE", ct);
        string? role = null, nomCourant = null, note = null;
        bool europeenne = false, accepte = false, decisionDatee = false, futurs = false, desiste = false, oppositionEnrolee = false;
        string? roleOpposition = null;
        DateTime? dateIp = null;
        int? affaire = null;
        await using (var r = await geste.LireAsync($"""
            SELECT i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, {(colonnesIns.Contains("INSTN_ID_AFFRE") ? "i.INSTN_ID_AFFRE" : "CAST(NULL AS int)")} AS Affaire, {(colonnesIns.Contains("INSTN_NOTE_AUDIENCE") ? "i.INSTN_NOTE_AUDIENCE" : "CAST(NULL AS varchar(max))")} AS Note,
                   CAST(ISNULL(i.INSTN_IP_EUROPEENNE, 0) AS bit) AS Europeenne,
                   dj.DCJDC_DATE, dj.DCJDC_DATE_PREVUE, CAST(ISNULL(td.R_TYPEDECISI_ACCEPTE, 0) AS bit) AS Accepte,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM v2.EVENEMENT_PROCEDURE AS e WHERE e.EVNPR_ID_INSTN = i.INSTN_ID_INSTN
                                          AND ISNULL(e.EVNPR_SURVENU, 0) = 0 AND ISNULL(e.EVNPR_DELAI_EXPIRE, 0) = 0) THEN 1 ELSE 0 END AS bit) AS Futurs,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM v2.EVENEMENT_PROCEDURE AS e WHERE e.EVNPR_ID_INSTN = i.INSTN_ID_INSTN
                                          AND e.EVNPR_SURVENU = 1 AND e.EVNPR_CODE_EVE_PRO = 'IPDESIN') THEN 1 ELSE 0 END AS bit) AS Desiste,
                   {(colonnesIns.Contains("INSTN_ID_INSTN_INSTANCE_PRINCIPA")
                       ? "(SELECT TOP (1) o.INSTN_NUM_ROLE FROM v2.INSTANCE AS o WHERE o.INSTN_ID_INSTN_INSTANCE_PRINCIPA = i.INSTN_ID_INSTN AND o.INSTN_CODE_AFFAIRE = 'TIP1' ORDER BY o.INSTN_ID_INSTN DESC)"
                       : "CAST(NULL AS varchar(11))")} AS Opposition
            FROM v2.INSTANCE AS i WITH (UPDLOCK, HOLDLOCK)
            OUTER APPLY (SELECT TOP (1) d.DCJDC_DATE, d.DCJDC_DATE_PREVUE, d.DCJDC_TYPE_DECISION FROM v2.DECISION_JUDICIAIRE AS d
                         WHERE d.DCJDC_ID_INSTN = i.INSTN_ID_INSTN ORDER BY d.DCJDC_NUM_ORDRE, d.DCJDC_ID_DCJDC) AS dj
            LEFT JOIN v2.TYPE_DECISION AS td ON td.R_TYPEDECISI_VALEUR = dj.DCJDC_TYPE_DECISION
            WHERE i.INSTN_ID_INSTN = @id AND i.INSTN_TYPE_INSTANCE = 'IP'
            """, ct, new SqlParameter("@id", n)))
            if (await r.ReadAsync(ct))
            {
                role = FormeLigne.Texte(r, "INSTN_NUM_ROLE");
                nomCourant = FormeLigne.Texte(r, "INSTN_NOM_COURANT");
                affaire = r["Affaire"] is DBNull ? null : Convert.ToInt32(r["Affaire"], CultureInfo.InvariantCulture);
                note = FormeLigne.Texte(r, "Note");
                europeenne = r["Europeenne"] is true;
                accepte = r["Accepte"] is true;
                var date = FormeLigne.DateDe(r, "DCJDC_DATE");
                decisionDatee = date is not null;
                dateIp = date ?? FormeLigne.DateDe(r, "DCJDC_DATE_PREVUE");
                futurs = r["Futurs"] is true;
                desiste = r["Desiste"] is true;
                roleOpposition = FormeLigne.Texte(r, "Opposition") is { Length: > 0 } o ? o : null;
                oppositionEnrolee = roleOpposition is not null;
            }
        if (role is null) return IssueGeste.Refuser($"Injonction de payer introuvable dans la base du greffe ({id}).");

        // 2. Le verrou de RIG v2.
        if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance, id, ct) is { } verrou)
            return IssueGeste.Refuser(verrou);

        // 3. Seul le desistement s'enregistre.
        var cle = GesteEcran.Cle(SectionActes, ChampDesistement);
        foreach (var k in geste.Demande.Valeurs.Keys)
            if (!string.Equals(k, cle, StringComparison.Ordinal))
                return IssueGeste.Refuser($"« {k} » : la V3 ne reprend pas encore cette saisie de RIG v2 (événements futurs liés, instance J ou OP, facture) ; seul le désistement s'enregistre.");
        var saisie = geste.Demande.Valeurs.TryGetValue(cle, out var v) ? v.Trim() : "";
        if (saisie.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire($"{SectionActes} › {ChampDesistement}"));

        // 4. Les alertes de Remplir_IP.
        if (desiste) return IssueGeste.Refuser("Désistement IP, traitement impossible en IPSUIVI vous devez aller en MJUD.");
        if (europeenne) return IssueGeste.Refuser("Injonction de payer européenne, traitement impossible en IPSUIVI vous devez aller en MJUD.");
        if (!accepte) return IssueGeste.Refuser("IP rejetée, traitement impossible en IPSUIVI vous devez aller en MJUD.");
        if (decisionDatee)
        {
            if (oppositionEnrolee)
                return IssueGeste.Refuser($"Opposition à injonction de payer enrôlée sous le numéro : {roleOpposition}. RIG v2 n'offre pas le désistement (OPE_IPSUIVI.cs:1014-1021).");
            if (!futurs)
                return IssueGeste.Refuser("Aucun évènement futur : RIG v2 n'offre pas le désistement sur cette IP (OPE_IPSUIVI.cs:1024-1029).");
        }

        // 5. La date.
        if (AudiencesV2.Date(saisie) is not { } dateDesistement) return IssueGeste.Refuser(DateIncorrecte);
        if (dateIp is { } dip && dateDesistement < dip.Date) return IssueGeste.Refuser(DateIncorrecte);

        // L'evenement, la note, la demande.
        var idEvenement = await EcrivainRetourAudience.EvenementSurvenuAsync(geste, n, "IPDESIN", dateDesistement, ct);
        var noteAvant = note ?? "";
        var noteApres = noteAvant.Contains("Désistement", StringComparison.Ordinal) ? noteAvant : (noteAvant + " Désistement").Trim();
        if (colonnesIns.Contains("INSTN_NOTE_AUDIENCE") && noteApres != noteAvant)
            await geste.EcrireAsync("UPDATE v2.INSTANCE SET INSTN_NOTE_AUDIENCE = @note WHERE INSTN_ID_INSTN = @id", ct,
                new SqlParameter("@note", noteApres), new SqlParameter("@id", n));
        var (idDemande, numeroDemande, refusDemande) = await EcrivainSurveillanceIp.DemandeInstanceAsync(geste, "IPSUIVI", n, affaire, nomCourant ?? role, role, ct);
        if (idDemande is null) return IssueGeste.Refuser(refusDemande!);

        return IssueGeste.Ecrit(
            $"Désistement du {FormeLigne.Date(dateDesistement)} enregistré sur l'IP {role} (événement IPDESIN). Non repris par la V3 : les éditions de l'événement et le tableau récapitulatif.",
            [id],
            new TraceGeste("affaire", $"l'injonction de payer {role}", id, "a modifie",
                new Dictionary<string, string?> { ["Desistement"] = null, ["Information"] = noteAvant },
                new Dictionary<string, string?>
                {
                    ["Desistement"] = FormeLigne.Date(dateDesistement), ["Evenement"] = $"IPDESIN n° {idEvenement}", ["Information"] = noteApres,
                    ["Demande"] = numeroDemande,
                }));
    }
}
