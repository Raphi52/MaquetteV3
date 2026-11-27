using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.Numerotation;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurOrdonnancesASigner;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Ordonnances à signer » (PROC_ORDOSIGN) : « Signer la sélection » rend les decisions cochees et ECRIT ce que RIG v2 ecrit a la
/// signature, dans la base du greffe. Mode d'emploi : EcrivainEcran.cs ; la liste et sa regle d'etat : LecteurOrdonnancesASigner.cs.
/// Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_ORDOSIGN\ETP_ORDONNANCES\OPE_LISTE_ORDONNANCES.cs (proc_ordosign.md, Detail
/// OPE). Cocher « DJ_RENDUE » puis Valider : la generation (_DoGenererLignesEditionStandard, :2581-2960), puis la sauvegarde
/// (_DoSauver, :3311-3402). Par ordonnance cochee, dans l'ordre de RIG v2 :
///   1. LA DECISION -- la date de la cellule DATE_DJ, pre-remplie au jour (_date_choix_utilisateur = DateTime.Now, :134, :409 ; la forme
///      File ne saisit pas de date : la V3 prend ce defaut), posee si DCJDC_DATE est vide (:2705) ; le numero de minute
///      « Numerotation.Generer(R_TYPEDECISI_NUM_REPERTOIRE, annee, quantieme) » s'il manque et que le type a une serie (:2706-2708,
///      :3324-3327) -- en V3 num.Attribuer, dans la transaction du geste ;
///   2. L'EVENEMENT DE PROCEDURE du type de decision (R_TYPEDECISI_CODE_EVE_PRO, :2690) : _DoGenererEvenementsProcedure (:2538-2579) --
///      le survenu du jour sur l'instance, s'il n'y en a pas deja un du meme code ce jour (:2546-2551), et son futur
///      (AddEvenementProcedureSurvenu cree le futur, IEntiteJudiciaire.cs:132-152) : EvenementsProcedureV2.SurvenuAsync (lot 7) ;
///   3. SES EDITIONS (evePRo.CreateDemandeEdition, :2849 ; EvenementProcedure.cs:2607-2690) : EditionsEvenementV2 (lot 7) calcule les
///      maquettes CEP_EDITION du code et les pose dans la file des editions (v2.DEMANDE_EDITION, phase C) sur la demande de l'instance ;
///      une maquette que la V3 ne reprend pas REFUSE le geste avec son nom (pas d'edition ecrite en silence) ;
///   4. LES MESURES LIEES (_DoSauver, :3365-3387) : pour chaque decision de l'instance dont le type porte une mesure liee
///      (R_TYPEDECISI_TYPE_MESURE_LIE), sa premiere mesure, creee sinon (DecisionJudiciaire.AddMesure, DecisionJudiciaire.cs:1075-1085 ;
///      Mesure.CreateMesure, Mesure.cs:488-496 : numero source GUID, rang 1, type ; l'instance, :3370), ouverte a la date de la decision
///      (:3379) et numerotee JMEXP de son annee si elle n'a pas de numero (:3380-3382) ;
///   5. LA DEMANDE de l'instance (Instance.CreateAndLockDemande(false, « ORDOSIGN »), Instance.cs:4718-4730 ; nommee du nom courant de
///      l'instance, :2681-2683) : ses numeros = le role, puis les minutes et les mesures de l'instance (:3361-3384), le dossier de la PC
///      ou de la prevention liee (DossierDemandeAddSequenceurLie, Instance.cs:4761-4767), sa demande mere (:3390), validee (:3391) ;
///      la demande mere est celle du processus, « Décision rendue » ou « Décisions rendues (n) » (:3348-3349) ;
///   6. LES COURRIELS -- le numero de minute a l'adresse du reglage REDA_MAIL_NUM_MINUTE_TO (Outils.GestionEnvoiMailMinuteAttributionOu-
///      Annulation, :3334 ; JUDICIAIRE\Fonctions diverses\Outils.cs:200-222) et la confirmation « Traitement de votre dossier concernant une
///      ordonnance » si MAIL_AUTO_ORDOSIGN vaut 1, son defaut (:3396-3401) : SIMULES, rien ne sort du poste ; le message le dit.
/// CONTROLES, avant toute ecriture, dans l'ordre de RIG v2 (un seul refus et rien n'est ecrit, pas meme pour les autres lignes) :
///   1. un geste connu, au moins une ordonnance cochee ;
///   2. chaque ordonnance est dans la liste a signer (une case de la recherche la charge, DCJDC_DATE IS NULL) ;
///   3. « Juge RCS » : refuse -- sa generation (_DoGenererLignesEditionJugeRCS, :1989+) attribue des numeros de depot DAS/DPR et
///      cree des actes RCS que la V3 ne reprend pas ;
///   4. le verrou de RIG v2 sur l'instance (_DemandeEnCours, :1597-1602), au message de VerrouDemandeV2 ;
///   5. la date (_DoVerifierDate, :1161-1195) : si un numero de minute de 10 caracteres existe deja, la date doit tomber le jour qu'il
///      encode (_VerifierNumMinute, :1197-1214) ; sinon elle doit etre comprise entre la date de saisine du greffe et le jour ;
///   6. ce que la signature de RIG v2 ecrirait et que la V3 ne sait pas ecrire refuse en le nommant : le changement de juge-commissaire
///      (« OP (changement de juge) » : EtreJugeCommEnAttente, :2786-2798, :3338-3345) ; la facture (:2734-2841 : des articles tarifes non
///      factures de l'affaire, ou la reedition LOT2_FACTURE de sa derniere demande facturee) ; l'evenement de la PC ou de la prevention
///      liee (R_CODEEVEPRO_CODE_EVE_PRO_PC, :2554-2574) ; la piece numerisee de l'ordonnance (VIDIGED_INDEX de la decision :
///      _NouvellePieceNumerisee, :2896-2901, :2940-2951, :3567-3587).
/// Traces : famille « affaire », « a modifie », la date de decision, le numero de minute, et ce que la signature a ecrit.
///
/// NON REPRIS (nomme) : le depot RCS induit (Instance.CreerDepotInduitOPE, Instance.cs:5008+ : il depend du parametrage OP RCS du code
/// evenement RCS du code affaire, qui n'est pas dans la base V3 du greffe) ; le decoupage des editions par destinataire du mode Lot 3
/// (DecouperDemandeEditionParDestinataireModeLot3) et l'ordre des editions des demandeurs (:2911-2960 : formule executoire, copies
/// executoires) ; l'exemption SURV_NO_FACT_BY_TYPE_DEC (:2738-2745 : une injonction des surveillances SDCA n'est pas facturee) -- la V3
/// refuse alors comme pour toute facture ; le controle du chef de demande PROAG (_DoVerifierReportAssembleeGenerale, :1234-1261) ; le
/// controle des intervenants factures et des evenements geres (_verifierEvePro, :2482-2530) ; l'alerte « ne pourra pas être imprimée »
/// (:1613-1625, une confirmation : la V3 n'imprime rien) ; la colonne LRAR (l'evenement LR du code, :2692-2696 : la forme File ne la
/// saisit pas) ; une decision de l'instance sans date ne recoit ni date d'ouverture ni numero de mesure (RIG v2 y lirait une date vide).
/// </summary>
internal sealed class EcrivainOrdonnancesASigner : IEcrivainEcran
{
    public string Procedure => "PROC_ORDOSIGN";
    public string Ecran => "Ordonnances à signer";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur, au mot pres (le bouton Valider de FORM_ORDOSIGN).</summary>
    internal const string Signer = "Signer la sélection";

    /// <summary>Le titre de l'etape des ordonnances (OPE_LISTE_ORDONNANCES.cs:444, « Liste des ordonnances à signer ») : RIG v2
    /// l'ecrit en tete du refus de _VerifierNumMinute.</summary>
    private const string TitreEtape = "Liste des ordonnances à signer";

    /// <summary>Le geste nomme dans un refus de ce que la V3 ne reprend pas (« faire la signature dans RIG v2 »).</summary>
    private const string Objet = "la signature";

    /// <summary>Ce que la signature lit de l'instance et du type de decision d'une ordonnance.</summary>
    private sealed record Contexte(EditionsEvenementV2.Instance Instance, string Nom, int? IdAffaire, int? IdPc, int? IdPrevention, string CodeEve,
                                   string CodePc);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Signer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser("Aucune ordonnance cochée : rien n'est signé.");

        var date = geste.Maintenant.Date;
        var reglages = await ReglagesAsync((sql, p) => geste.LireAsync(sql, ct, p), ct);
        var aSigner = new List<(Ordonnance O, Contexte C)>();
        foreach (var id in ids)
        {
            Ordonnance? o = null;
            if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                o = (await OrdonnancesAsync((sql, p) => geste.LireAsync(sql, ct, p), reglages, date, 1, n, ct)).Lignes.FirstOrDefault();
            if (o is null) return IssueGeste.Refuser($"Ordonnance introuvable dans la liste à signer ({id}).");
            if (o.Date is { } signee) return IssueGeste.Refuser($"L'ordonnance du rôle {o.Role} est déjà signée (décision du {FormeLigne.Date(signee)}).");
            if (o.Groupe == JugeRcs)
                return IssueGeste.Refuser($"L'ordonnance du rôle {o.Role} est un « Juge RCS » : sa signature crée des dépôts RCS que la V3 ne reprend pas, la signer dans RIG v2.");
            if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance,
                    o.IdInstance.ToString(CultureInfo.InvariantCulture), ct) is { } verrou)
                return IssueGeste.Refuser(verrou);
            if (Date(o, date) is { } erreur) return IssueGeste.Refuser(erreur);
            var c = await ContexteAsync(geste, o, ct);
            if (await NonReprisAsync(geste, o, c, ct) is { } nonRepris) return IssueGeste.Refuser(nonRepris);
            aSigner.Add((o, c));
        }

        // La demande du processus, mere des demandes par instance (:3348-3349).
        var (idMere, numeroMere, refusMere) = await EditionsV2.DemandeProcessusValideeAsync(geste, "ORDOSIGN",
            aSigner.Count == 1 ? "Décision rendue" : $"Décisions rendues ({aSigner.Count})", ct);
        if (refusMere is not null) return IssueGeste.Refuser(refusMere);

        var traces = new List<TraceGeste>();
        var minutes = new List<string>();
        var editions = 0;
        var mesures = new List<string>();
        var demandes = new List<string>();
        foreach (var (o, c) in aSigner)
        {
            // 1. La decision : sa date, sa minute.
            await geste.EcrireAsync("UPDATE v2.DECISION_JUDICIAIRE SET DCJDC_DATE = @date WHERE DCJDC_ID_DCJDC = @id AND DCJDC_DATE IS NULL", ct,
                new SqlParameter("@date", date), new SqlParameter("@id", o.Id));
            var minute = o.Minute;
            if (minute.Length == 0 && o.SerieMinute.Length > 0)
            {
                var numero = await geste.AttribuerNumeroAsync(new DemandeNumero(o.SerieMinute, date.Year, date.DayOfYear), ct);
                if (numero.Code != CodesNumerotation.Ok || string.IsNullOrEmpty(numero.Numero))
                    return IssueGeste.Refuser($"Numéro de minute {o.SerieMinute} du rôle {o.Role} : {numero.Message ?? "aucun numéro donné"} (code {numero.Code}).");
                minute = numero.Numero;
                await geste.EcrireAsync("UPDATE v2.DECISION_JUDICIAIRE SET DCJDC_NUM_REPERTOIRE = @minute WHERE DCJDC_ID_DCJDC = @id AND DCJDC_NUM_REPERTOIRE IS NULL", ct,
                    new SqlParameter("@minute", minute), new SqlParameter("@id", o.Id));
                minutes.Add(minute);
            }

            // 2. et 3. L'evenement du type de decision et ses editions.
            List<EditionsEvenementV2.Edition> aDeposer = [];
            string? evenement = null;
            if (c.CodeEve.Length > 0)
            {
                var eve = await EvenementDuJourAsync(geste, o.IdInstance, c.CodeEve, date, ct);
                if (eve is null)
                {
                    var ecrits = await EvenementsProcedureV2.SurvenuAsync(geste, o.IdInstance, c.CodeEve, date, avecFutur: true, ct);
                    eve = ecrits.Evenements[0];
                    evenement = ecrits.CodeFutur is { } futur ? $"{c.CodeEve} (futur {futur})" : c.CodeEve;
                }
                else evenement = $"{c.CodeEve} (déjà survenu ce jour)";
                var calcul = await EditionsEvenementV2.CalculerAsync(geste, c.Instance, eve, Objet, ct);
                if (calcul.NonRepris is { } nonRepris) return IssueGeste.Refuser(nonRepris);
                aDeposer = [.. calcul.Editions];
            }

            // 4. Les mesures liees des decisions de l'instance.
            var mesuresInstance = await MesuresLieesAsync(geste, o.IdInstance, ct);
            if (mesuresInstance.Refus is { } refusMesure) return IssueGeste.Refuser(refusMesure);
            mesures.AddRange(mesuresInstance.Nouvelles);

            // 5. La demande de l'instance : ses numeros, ses dossiers lies, sa mere ; validee.
            var numeros = await NumerosAsync(geste, o, ct);
            var (idDemande, numeroDemande, refusDemande) = await EditionsV2.DemandeValideeAsync(geste, "ORDOSIGN",
                new EditionsV2.ObjetDemande(c.IdAffaire, c.Nom, o.Role, numeros, VerrouDemandeV2.SequenceurInstance,
                                            o.IdInstance.ToString(CultureInfo.InvariantCulture)), ct);
            if (refusDemande is not null) return IssueGeste.Refuser(refusDemande);
            var lies = new List<(string, string)>();
            if (c.IdPc is { } pc) lies.Add((DepotsPcV2.SequenceurPc, pc.ToString(CultureInfo.InvariantCulture)));
            else if (c.IdPrevention is { } prv) lies.Add((LecteurProceduresCollectives.SequenceurPrevention, prv.ToString(CultureInfo.InvariantCulture)));
            await EditionsV2.LierAsync(geste, idDemande, idMere, lies, ct);
            demandes.Add(numeroDemande!);
            foreach (var e in aDeposer) await EditionsEvenementV2.DeposerAsync(geste, idDemande, e, ct);
            editions += aDeposer.Count;

            traces.Add(new TraceGeste("affaire", $"l'ordonnance du rôle {o.Role}", o.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new Dictionary<string, string> { ["Date de la décision"] = "", ["Numéro de minute"] = o.Minute },
                new Dictionary<string, string>
                {
                    ["Date de la décision"] = FormeLigne.Date(date), ["Numéro de minute"] = minute, ["Demande"] = numeroDemande!,
                    ["Demande mère"] = numeroMere!, ["Événement"] = evenement ?? "", ["Éditions"] = aDeposer.Count.ToString(CultureInfo.InvariantCulture),
                    ["Mesures"] = string.Join(" ", mesuresInstance.Nouvelles),
                }));
        }

        var nombre = aSigner.Count;
        var envoiAuto = await ReglageAsync(geste, "MAIL_AUTO_ORDOSIGN", ct) ?? "1";
        var courriels = new List<string>();
        if (minutes.Count > 0) courriels.Add("le numéro de minute à l'adresse du réglage REDA_MAIL_NUM_MINUTE_TO");
        if (envoiAuto == "1") courriels.Add("la confirmation au demandeur (MAIL_AUTO_ORDOSIGN)");
        var message = (nombre > 1 ? $"{nombre} ordonnances signées" : "Ordonnance signée") + $" le {FormeLigne.Date(date)}"
            + (minutes.Count == 0 ? "" : minutes.Count == 1 ? $", minute {minutes[0]}" : $", minutes {string.Join(", ", minutes)}")
            + $". Demande{(demandes.Count > 1 ? "s" : "")} {string.Join(", ", demandes)}"
            + (editions == 0 ? "" : editions == 1 ? " ; 1 édition dans la file des éditions" : $" ; {editions} éditions dans la file des éditions")
            + (mesures.Count == 0 ? "" : $" ; mesure{(mesures.Count > 1 ? "s" : "")} {string.Join(", ", mesures)}") + "."
            + (courriels.Count == 0 ? "" : $" Courriels simulés, rien n'est envoyé : {string.Join(" ; ", courriels)}.");
        return IssueGeste.Ecrit(message, [.. aSigner.Select(x => x.O.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }

    /// <summary>L'instance et le type de decision de l'ordonnance, tels que la signature les lit.</summary>
    private static async Task<Contexte> ContexteAsync(GesteEnCours geste, Ordonnance o, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT i.INSTN_NOM_COURANT, i.INSTN_ID_AFFRE, i.INSTN_ID_PRCLL, i.INSTN_ID_PREVE, i.INSTN_NUMERO_SOURCE, i.INSTN_NATURE_AFFAIRE,
                   i.INSTN_NAT_AFF_PART, i.INSTN_CODE_ACTE_PROCE, td.R_TYPEDECISI_CODE_EVE_PRO, ce.R_CODEEVEPRO_CODE_EVE_PRO_PC
            FROM v2.DECISION_JUDICIAIRE AS d
            JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
            LEFT JOIN v2.TYPE_DECISION AS td ON td.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
            LEFT JOIN v2.CODE_EVE_PRO AS ce ON ce.R_CODEEVEPRO_VALEUR = td.R_TYPEDECISI_CODE_EVE_PRO
            WHERE d.DCJDC_ID_DCJDC = @id
            """, ct, new SqlParameter("@id", o.Id));
        await r.ReadAsync(ct);
        string? Texte(string colonne) => FormeLigne.Texte(r, colonne) is { Length: > 0 } t ? t : null;
        int? Entier(string colonne) => r[colonne] is DBNull ? null : Convert.ToInt32(r[colonne], CultureInfo.InvariantCulture);
        return new Contexte(
            new EditionsEvenementV2.Instance(o.IdInstance, o.Role, Texte("INSTN_NUMERO_SOURCE"), Texte("INSTN_NATURE_AFFAIRE"), Texte("INSTN_NAT_AFF_PART"),
                                             Texte("INSTN_CODE_ACTE_PROCE")),
            FormeLigne.Texte(r, "INSTN_NOM_COURANT"), Entier("INSTN_ID_AFFRE"), Entier("INSTN_ID_PRCLL"), Entier("INSTN_ID_PREVE"),
            FormeLigne.Texte(r, "R_TYPEDECISI_CODE_EVE_PRO"), FormeLigne.Texte(r, "R_CODEEVEPRO_CODE_EVE_PRO_PC"));
    }

    /// <summary>Ce que la signature de RIG v2 ecrirait et que la V3 ne sait pas ecrire (controle 6 de l'en-tete) ; null sinon.</summary>
    private static async Task<string?> NonReprisAsync(GesteEnCours geste, Ordonnance o, Contexte c, CancellationToken ct)
    {
        string Refus(string quoi, bool feminin = false) => $"{quoi} n'est pas repris{(feminin ? "e" : "")} par RIG v3 : rien n'est enregistré, faire {Objet} dans RIG v2.";
        if (o.Groupe == OpChangementJuge)
            return Refus($"Le changement de juge-commissaire de l'ordonnance du rôle {o.Role} (ETRE_JUGE_COMM_EN_ATTENTE)");
        if (c.IdAffaire is { } affaire)
        {
            // :2747-2763 : des articles tarifes non factures d'un client ; sinon :2773-2783 : la derniere demande facturee de l'affaire.
            if (await TableAsync(geste, "v2.ARTICLE_UTILISE", ct)
                && await CompteAsync(geste, "SELECT COUNT(*) FROM v2.ARTICLE_UTILISE WHERE ARTUT_ID_AFFRE = @a AND ARTUT_NUMERO_FACTURE IS NULL AND ARTUT_ID_CLIEN IS NOT NULL", affaire, ct) > 0)
                return Refus($"La facture des articles tarifés de l'affaire du rôle {o.Role}", feminin: true);
            if (await TableAsync(geste, "v2.FACTURE", ct)
                && await CompteAsync(geste, """
                    SELECT COUNT(*) FROM v2.DEMANDE AS d JOIN v2.FACTURE AS f ON f.FACTU_NUM_DEMANDE = d.DMND_NUM_DEMANDE
                    WHERE d.DMND_ID_AFFRE = @a AND d.DMND_ETAT_DEMANDE <> 'X'
                    """, affaire, ct) > 0)
                return Refus($"La réédition de la facture (LOT2_FACTURE) de l'affaire du rôle {o.Role}", feminin: true);
        }
        if (c.CodePc.Length > 0 && (c.IdPc is not null || c.IdPrevention is not null)
            && await CompteAsync(geste, "SELECT COUNT(*) FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @a", c.CodePc, ct) > 0)
            return Refus($"L'événement {c.CodePc} de la {(c.IdPc is not null ? "procédure collective" : "prévention")} liée au rôle {o.Role}");
        if (await TableAsync(geste, "v2.VIDIGED_INDEX", ct)
            && await CompteAsync(geste, "SELECT COUNT(*) FROM v2.VIDIGED_INDEX WHERE VGIDX_ID_DCJDC = @a", o.Id, ct) > 0)
            return Refus($"La pièce numérisée de l'ordonnance du rôle {o.Role} (VIDIGED_INDEX)", feminin: true);
        return null;
    }

    /// <summary>L'evenement SURVENU de ce code deja pose ce jour sur l'instance (_DoGenererEvenementsProcedure, :2546) ; null sinon.</summary>
    private static async Task<EditionsEvenementV2.Evenement?> EvenementDuJourAsync(GesteEnCours geste, int idInstance, string code, DateTime jour, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT TOP (1) EVNPR_NUMERO_SOURCE, EVNPR_DATE, EVNPR_NUM_ORDRE, CONVERT(bit, ISNULL(EVNPR_AVEC_PIECE_NUMERISE, 0)) AS Piece
            FROM v2.EVENEMENT_PROCEDURE WHERE EVNPR_ID_INSTN = @i AND EVNPR_CODE_EVE_PRO = @c AND EVNPR_SURVENU = 1 AND CONVERT(date, EVNPR_DATE) = @jour
            ORDER BY EVNPR_ID_EVNPR
            """, ct, new SqlParameter("@i", idInstance), new SqlParameter("@c", code), new SqlParameter("@jour", jour));
        if (!await r.ReadAsync(ct)) return null;
        return new EditionsEvenementV2.Evenement(code, FormeLigne.Texte(r, "EVNPR_NUMERO_SOURCE"), FormeLigne.DateDe(r, "EVNPR_DATE"),
            r["EVNPR_NUM_ORDRE"] is DBNull ? null : Convert.ToInt16(r["EVNPR_NUM_ORDRE"], CultureInfo.InvariantCulture), r["Piece"] is true);
    }

    /// <summary>Les mesures liees des decisions datees de l'instance (_DoSauver, :3365-3387) : creees s'il le faut, ouvertes a la date de
    /// leur decision, numerotees JMEXP ; rend les numeros attribues, ou le refus de la numerotation.</summary>
    private static async Task<(List<string> Nouvelles, string? Refus)> MesuresLieesAsync(GesteEnCours geste, int idInstance, CancellationToken ct)
    {
        var aTraiter = new List<(int Decision, DateTime Date, string Type, int? Mesure, string Numero, string Role)>();
        await using (var r = await geste.LireAsync("""
            SELECT d.DCJDC_ID_DCJDC, d.DCJDC_DATE, td.R_TYPEDECISI_TYPE_MESURE_LIE, m.MSINS_ID_MSINS, m.MSINS_NUM_MESURE, i.INSTN_NUM_ROLE
            FROM v2.DECISION_JUDICIAIRE AS d
            JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
            JOIN v2.TYPE_DECISION AS td ON td.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
            OUTER APPLY (SELECT TOP (1) x.MSINS_ID_MSINS, x.MSINS_NUM_MESURE FROM v2.MESURE AS x WHERE x.MSINS_ID_DCJDC = d.DCJDC_ID_DCJDC ORDER BY x.MSINS_ID_MSINS) AS m
            WHERE d.DCJDC_ID_INSTN = @i AND ISNULL(td.R_TYPEDECISI_TYPE_MESURE_LIE, '') <> '' AND d.DCJDC_DATE IS NOT NULL
            ORDER BY d.DCJDC_NUM_ORDRE, d.DCJDC_ID_DCJDC
            """, ct, new SqlParameter("@i", idInstance)))
            while (await r.ReadAsync(ct))
                aTraiter.Add((Convert.ToInt32(r["DCJDC_ID_DCJDC"], CultureInfo.InvariantCulture), (DateTime)r["DCJDC_DATE"],
                              FormeLigne.Texte(r, "R_TYPEDECISI_TYPE_MESURE_LIE"),
                              r["MSINS_ID_MSINS"] is DBNull ? null : Convert.ToInt32(r["MSINS_ID_MSINS"], CultureInfo.InvariantCulture),
                              FormeLigne.Texte(r, "MSINS_NUM_MESURE"), FormeLigne.Texte(r, "INSTN_NUM_ROLE")));
        var nouvelles = new List<string>();
        foreach (var (decision, dateDecision, type, mesure, numeroLu, role) in aTraiter)
        {
            var id = mesure ?? await geste.InsererAsync("v2.MESURE", "MSINS_ID_MSINS", new Dictionary<string, object?>
            {
                ["MSINS_ID_DCJDC"] = decision, ["MSINS_ID_INSTN"] = idInstance, ["MSINS_NUM_ORDRE"] = (short)1, ["MSINS_TYPE_MESURE"] = type,
                ["MSINS_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(),
            }, ct);
            var numero = numeroLu;
            if (numero.Length == 0)
            {
                var n = await geste.AttribuerNumeroAsync(new DemandeNumero("JMEXP", dateDecision.Year), ct);
                if (n.Code != CodesNumerotation.Ok || string.IsNullOrEmpty(n.Numero))
                    return ([], $"Numéro de mesure JMEXP {dateDecision.Year} du rôle {role} : {n.Message ?? "aucun numéro donné"} (code {n.Code}).");
                numero = n.Numero;
                nouvelles.Add(numero);
            }
            await geste.EcrireAsync("UPDATE v2.MESURE SET MSINS_DATE_OUVERTURE = @date, MSINS_NUM_MESURE = @num WHERE MSINS_ID_MSINS = @m", ct,
                new SqlParameter("@date", dateDecision), new SqlParameter("@num", numero), new SqlParameter("@m", id));
        }
        return (nouvelles, null);
    }

    /// <summary>Les numeros de la demande : le role, puis les minutes des decisions de l'instance, puis ses mesures numerotees
    /// (:3361-3384), coupes a la colonne (DMND_LES_NUMEROS, 128).</summary>
    private static async Task<string> NumerosAsync(GesteEnCours geste, Ordonnance o, CancellationToken ct)
    {
        var numeros = new List<string> { o.Role };
        await using (var r = await geste.LireAsync("""
            SELECT DCJDC_NUM_REPERTOIRE FROM v2.DECISION_JUDICIAIRE WHERE DCJDC_ID_INSTN = @i AND DCJDC_NUM_REPERTOIRE IS NOT NULL ORDER BY DCJDC_NUM_ORDRE, DCJDC_ID_DCJDC
            """, ct, new SqlParameter("@i", o.IdInstance)))
            while (await r.ReadAsync(ct)) numeros.Add(r.GetString(0));
        await using (var r = await geste.LireAsync("""
            SELECT m.MSINS_NUM_MESURE FROM v2.MESURE AS m JOIN v2.DECISION_JUDICIAIRE AS d ON d.DCJDC_ID_DCJDC = m.MSINS_ID_DCJDC
            WHERE d.DCJDC_ID_INSTN = @i AND m.MSINS_NUM_MESURE IS NOT NULL ORDER BY d.DCJDC_NUM_ORDRE, m.MSINS_ID_MSINS
            """, ct, new SqlParameter("@i", o.IdInstance)))
            while (await r.ReadAsync(ct)) numeros.Add(r.GetString(0));
        return string.Join(" ", numeros.Where(x => x.Length > 0));
    }

    private static async Task<bool> TableAsync(GesteEnCours geste, string table, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT CONVERT(bit, CASE WHEN OBJECT_ID(@t) IS NULL THEN 0 ELSE 1 END)", ct, new SqlParameter("@t", table));
        return await r.ReadAsync(ct) && r.GetBoolean(0);
    }

    private static async Task<int> CompteAsync(GesteEnCours geste, string sql, object parametre, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(sql, ct, new SqlParameter("@a", parametre));
        return await r.ReadAsync(ct) ? r.GetInt32(0) : 0;
    }

    /// <summary>Un reglage du greffe (PARAMETRAGE de RIG v2, repris dans soc.Reglage) ; null s'il est absent ou vide.</summary>
    private static async Task<string?> ReglageAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT Valeur FROM soc.Reglage WHERE Code = @c AND Valeur <> ''", ct, new SqlParameter("@c", code));
        return await r.ReadAsync(ct) && !r.IsDBNull(0) ? r.GetString(0).Trim() : null;
    }

    /// <summary>Le refus de _DoVerifierDate (OPE_LISTE_ORDONNANCES.cs:1161-1214) pour la date <paramref name="date"/> ; null si elle va.</summary>
    internal static string? Date(Ordonnance o, DateTime date)
    {
        if (o.Minute.Length == 10)
        {
            // _VerifierNumMinute : les deux chiffres de l'annee et le quantieme encodes dans la minute (AAQQQCCCCC).
            if (o.Minute[..2] != date.Year.ToString(CultureInfo.InvariantCulture)[2..]
                || !int.TryParse(o.Minute.AsSpan(2, 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantieme) || quantieme != date.DayOfYear)
            {
                var annee = int.Parse(date.Year.ToString(CultureInfo.InvariantCulture)[..2] + o.Minute[..2], CultureInfo.InvariantCulture);
                var jour = int.TryParse(o.Minute.AsSpan(2, 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var q) ? q : 1;
                var attendue = new DateTime(annee, 1, 1).AddDays(jour - 1);
                return $"{TitreEtape} - attention, un numéro de minute ({o.Minute}) est déjà attribué, la date de décision doit être le {FormeLigne.Date(attendue)}";
            }
            return null;
        }
        if (o.Saisine is { } saisine && date < saisine)
            return $"La date de la décision judiciaire doit être comprise entre la date de saisine du greffe ({FormeLigne.Date(saisine)}) et la date du jour";
        return null;
    }
}
