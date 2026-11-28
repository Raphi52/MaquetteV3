using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurMentionsEnMarge;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Mentions en marge » (PROC_MMA) : ENREGISTRER la decision d'une demande MMA -- « Valider » ecrit la mention en marge de
/// l'inscription et valide la demande, dans la base du greffe. Mode d'emploi : EcrivainEcran.cs ; la liste et la fiche :
/// LecteurMentionsEnMarge.cs. Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 6, 28/11/2026.
///
/// SOURCE (ancien RIG) : RIG\DLL\Processus\Inscriptions\PROC_MMA (PROC_MMA.cs, proc_mma.md), sa mere PROC_INSCRIPTION_GENERIQUE
/// (EtapeRCS\Rig_Etape_Endettement\Generique), l'operation de la mention OPE_MENTION_MARGINALE et OPEGS_MENTION_MARGINALE
/// (ETP_INSCRIPTION\OPEGS_MENTION_MARGINALE), les entites RigMetier\Inscription\Inscription.cs et MentionMarginale.cs.
///
/// CONTROLES, avant toute ecriture, dans l'ordre (un refus n'ecrit rien) :
///   1. la demande est une demande MMA rattachee a son inscription (PROC_MMA.cs:56-58) ;
///   2. elle n'est pas close : validee, terminee ou refusee, RIG v2 la rouvre en consultation (rien ne s'y enregistre) ;
///   3. une decision est choisie (les boutons Valider, Réclamer, Refuser, Attendre de la V2, etpReclamRefus, PROC_MMA.cs:25-27) ;
///   4. Réclamer, Refuser et Attendre : NON REPRIS, refuses en le disant -- RIG v2 y saisit les motifs de l'evenement attendu
///      (ETP_RECLAM_REFUS : EVENEMENT_ATTENDU, EVENEMENT_ATTENDU_DETAIL, MOTIF_EVENEMENT ; un motif non survenu est exige pour degriser
///      ces boutons, ETP_RECLAM_REFUS.cs:291, :442-443) et edite la lettre de reclamation, de refus (et sa LRAR, sa facture) ou de mise
///      en attente (LOT2_RECLAM_INS, LOT2_REFUS_INS, LOT2_ATTENTE_INS : PROC_INSCRIPTION_GENERIQUE.cs:632-650, PROC_MMA.cs:197-226),
///      que la V3 ne reprend pas ;
///   5. Valider : la mention est obligatoire (SetMetierObligatoireOpeMentionMarginale(true), PROC_MMA.cs:37 ; proc_mma.md « on ne
///      peut pas valider un MMA sans saisir une mention ») -- son type et son texte (ultTypeMention et ultTexte MetierObligatoire,
///      OPE_MENTION_MARGINALE.Designer.cs:85, :97) ; le type est un code de TYPE_MMA (la combo, TYPE_MMA.GetTYPE_MMA,
///      OPE_MENTION_MARGINALE.cs:118) ; la date, si elle est saisie, est une date ;
///   6. une inscription MARITIME (un bateau) est refusee : sa validation suit l'etat de transmission blockchain des hypotheques
///      maritimes (Inscription.IsInscriptionBlockchain, Inscription.cs:876-879 ; PROC_MMA.cs:122-131), que la V3 ne reprend pas.
///
/// CE QUI S'ECRIT (Valider, DoSauveAfterEtape PROC_MMA.cs:114-139 et Inscription.Sauve, Inscription.cs:1829-1846) :
///   - la MENTION (v2.MENTION_MARGINALE) : l'inscription, le type, le texte, la date saisie ou, vide, la date de travail des
///     inscriptions « WINS » (Inscription.cs:1838-1839 ; le jour si la base n'en a pas), le numero de registre de la serie « EAR » de
///     son annee (Numerotation.Generer, :1841-1842 -- num.Attribuer dans la transaction du geste), « ne pas editer dans le
///     certificat » decoche (MNMRG_ETAT_EDIT_C, defaut de RIG_DEV) ;
///   - sa FORMALITE (v2.FORMALITE_ENDETTEMENT), une par mention neuve (OPEGS_MENTION_MARGINALE.cs:163-168) : la date de travail, la
///     mention et son numero de registre (MentionMarginale.Sauve, MentionMarginale.cs:232-240) ;
///   - la DEMANDE (v2.DEMANDE) validee : etat V, plus en cours, date de validation ; son nom suivi de « - » et des debiteurs
///     (DesignationDebiteursSansCivilite : denomination ou designation, et « (SIREN) », separes par « / », Inscription.cs:571-578) ;
///     ses numeros = le SIREN du premier debiteur ; DMND_ID_FMEDT = la formalite de la derniere mention (PROC_MMA.cs:118-119, :134-138).
/// TRACE : famille « dossier », « a modifie », la demande et la mention ecrite.
///
/// NON REPRIS (nommes ; le message le dit) : la FACTURE -- l'article de l'inscription (Inscription.GetCodeArticle) et le certificat
/// « 21-98 » pour les types V, N, G, O, K, NP (DoGetArticles, PROC_MMA.cs:229-270) : la demande est validee sans facture, comme les
/// depots d'actes du lot 5 (EcrivainDepotsActes.cs) ; les EDITIONS de la validation (LETTRE_ENVOI_ENDETTE, LOT2_CMMA_1_IMP, bordereau de
/// numerisation, PROC_MMA.cs:172-196) ; le client de facturation cherche par le premier creancier (PROC_MMA.cs:90-97) ; la date de
/// peremption d'un nantissement judiciaire (mention NJD, PROC_MMA.cs:148-160) ; le verrou d'une demande en cours de saisie sur un autre
/// poste (la V3 ne sait pas qui la tient).
/// </summary>
internal sealed class EcrivainMentionsEnMarge : IEcrivainEcran
{
    public string Procedure => "PROC_MMA";
    public string Ecran => "Mentions en marge";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    /// <summary>Les decisions du descripteur (ModifierAffaire.cs), au mot pres.</summary>
    internal const string Valider = "Valider", Reclamer = "Réclamer", Refuser = "Refuser", Attendre = "Attendre";

    /// <summary>La date de travail des inscriptions (DateTravail.GetDateTravailByCodeDateTravail(« WINS »), Inscription.cs:1839).</summary>
    internal const string DateTravailInscriptions = "WINS";

    /// <summary>La serie du numero de registre d'une mention (Numerotation.Generer(« EAR »), Inscription.cs:1842).</summary>
    internal const string SerieRegistre = "EAR";

    internal const string NonRepris = "la facture (article de l'inscription, certificat 21-98) et les éditions (lettre d'envoi, certificat de mention en marge, " +
                                      "bordereau de numérisation) ne sont pas reprises par la V3.";

    private sealed record Demande(int Id, string Numero, string Nom, string Etat, string LibelleEtat, int IdInscription, string NumeroInscription, bool Maritime);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // 1. La demande MMA et son inscription, verrouillees jusqu'a la fin de la transaction.
        if (geste.Demande.Ids is not [var id] || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            || await LireDemandeAsync(geste, n, ct) is not { } d)
            return IssueGeste.Refuser($"Demande de mention en marge introuvable dans la base du greffe ({string.Join(", ", geste.Demande.Ids)}).");

        // 2. Close : RIG v2 la rouvre en consultation.
        if (d.Etat is "V" or "T" or "O")
            return IssueGeste.Refuser($"La demande {d.Numero} est close (« {d.LibelleEtat} ») : RIG v2 la rouvre en consultation, rien ne s'y enregistre.");

        // 3. et 4. La decision.
        var decision = geste.Valeur(SectionDecision, ChampDecision)?.Trim() ?? "";
        if (decision.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ChampDecision));
        if (decision is Reclamer or Refuser or Attendre)
            return IssueGeste.Refuser($"« {decision} » n'est pas repris par la V3 : RIG v2 y saisit les motifs de l'événement attendu et édite la lettre " +
                                      $"de {(decision == Reclamer ? "réclamation" : decision == Refuser ? "refus" : "mise en attente")}. " +
                                      $"{(decision == Refuser ? "Le refus est facturé. " : "")}Traiter cette demande dans RIG v2.");
        if (decision != Valider)
            return IssueGeste.Refuser($"« {ChampDecision} » : {decision} n'est pas une décision de l'écran ({Valider}, {Reclamer}, {Refuser}, {Attendre}).");

        // 5. La mention, obligatoire : type et texte ; le type est un code de TYPE_MMA ; la date saisie est une date.
        var type = geste.Valeur(SectionMention, ChampType)?.Trim() ?? "";
        var texte = geste.Valeur(SectionMention, ChampMention)?.Trim() ?? "";
        if (type.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ChampType));
        if (texte.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ChampMention));
        string? libelleType = null;
        await using (var r = await geste.LireAsync("SELECT R_TYPEMMA_LIBELLE_ECRAN FROM v2.TYPE_MMA WHERE R_TYPEMMA_VALEUR = @t", ct, new SqlParameter("@t", type)))
            if (await r.ReadAsync(ct)) libelleType = FormeLigne.Texte(r, "R_TYPEMMA_LIBELLE_ECRAN");
        if (libelleType is null) return IssueGeste.Refuser($"« {ChampType} » : {type} n'est pas un type de mention de la base du greffe (TYPE_MMA).");
        var dateSaisie = geste.Valeur(SectionMention, ChampDate)?.Trim() ?? "";
        DateTime? date = null;
        if (dateSaisie.Length > 0)
        {
            date = CreationV2.Date(dateSaisie);
            if (date is null) return IssueGeste.Refuser($"« {ChampDate} » : date illisible ({dateSaisie}).");
        }

        // 6. Une hypotheque maritime suit la blockchain des inscriptions maritimes.
        if (d.Maritime)
            return IssueGeste.Refuser($"L'inscription {d.NumeroInscription} est maritime : sa mention suit l'état de transmission blockchain des hypothèques " +
                                      "maritimes, que la V3 ne reprend pas. Valider cette demande dans RIG v2.");

        // Ecritures : la mention, sa formalite, la demande validee.
        var travail = await DateTravailAsync(geste, ct);
        var dateMention = date ?? travail;
        var (registre, refusNumero) = await CreationV2.NumeroAsync(geste, SerieRegistre, dateMention.Year, ct);
        if (registre is null) return IssueGeste.Refuser(refusNumero!);
        var idMention = await geste.InsererAsync("v2.MENTION_MARGINALE", "MNMRG_ID_MNMRG", new Dictionary<string, object?>
        {
            ["MNMRG_ID_INSCR"] = d.IdInscription, ["MNMRG_DATE"] = dateMention, ["MNMRG_TYPE_MMA"] = type, ["MNMRG_TEXTE_MENTION"] = texte,
            ["MNMRG_NUM_REGISTRE"] = registre, ["MNMRG_ETAT_EDIT_C"] = false,
        }, ct);
        var idFormalite = await geste.InsererAsync("v2.FORMALITE_ENDETTEMENT", "FMEDT_ID_FMEDT", new Dictionary<string, object?>
        {
            ["FMEDT_ID_MNMRG"] = idMention, ["FMEDT_DATE_FORMALITE"] = travail, ["FMEDT_NUM_REGISTRE"] = registre,
            ["FMEDT_TRAITEMENT_SURV"] = false, ["FMEDT_IS_DEMAT"] = false, ["FMEDT_VIDI_ARCHIVAGE_ETAT"] = "0",
        }, ct);
        var (debiteurs, siren) = await DebiteursAsync(geste, d.IdInscription, ct);
        var nom = (d.Nom.Length > 0 ? d.Nom : $"MMA sur l'inscription {d.NumeroInscription}") + " - " + debiteurs;
        if (nom.Length > 255) nom = nom[..255];
        await geste.EcrireAsync("""
            UPDATE v2.DEMANDE SET DMND_ETAT_DEMANDE = 'V', DMND_EN_COURS = 0, DMND_DATE_VALIDATION = @maintenant, DMND_NOM = @nom,
                DMND_LES_NUMEROS = @siren, DMND_ID_FMEDT = @formalite
            WHERE DMND_ID_DMND = @id
            """, ct, new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@nom", nom),
            new SqlParameter("@siren", (object?)siren ?? DBNull.Value), new SqlParameter("@formalite", idFormalite), new SqlParameter("@id", d.Id));

        var idTexte = d.Id.ToString(CultureInfo.InvariantCulture);
        return IssueGeste.Ecrit(
            $"Mention en marge « {libelleType} » enregistrée sur l'inscription {d.NumeroInscription} (registre {registre}, du {FormeLigne.Date(dateMention)}) ; " +
            $"demande {d.Numero} validée. {char.ToUpperInvariant(NonRepris[0])}{NonRepris[1..]}",
            [idTexte],
            new TraceGeste("dossier", $"la demande MMA {d.Numero} (inscription {d.NumeroInscription})", idTexte, "a modifie",
                new { Etat = d.LibelleEtat, Nom = d.Nom },
                new { Etat = "Validée", Nom = nom, Mention = new { Type = type, Texte = texte, Date = FormeLigne.Date(dateMention), Registre = registre }, Formalite = idFormalite }));
    }

    private static async Task<Demande?> LireDemandeAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT d.DMND_ID_DMND, d.DMND_NUM_DEMANDE, d.DMND_NOM, d.DMND_ETAT_DEMANDE, e.R_ETATDEMAND_LIBELLE_ECRAN, i.Id AS IdInscription,
                   i.NumeroInscription, i.IdBateau
            FROM v2.DEMANDE AS d WITH (UPDLOCK, HOLDLOCK)
            JOIN rec.Inscription AS i WITH (UPDLOCK) ON i.Id = d.DMND_ID_INSCR
            LEFT JOIN v2.ETAT_DEMANDE AS e ON e.R_ETATDEMAND_VALEUR = d.DMND_ETAT_DEMANDE
            WHERE d.DMND_ID_DMND = @id AND d.DMND_CODE_PROSS = 'MMA'
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        return new Demande(id, FormeLigne.Texte(r, "DMND_NUM_DEMANDE"), FormeLigne.Texte(r, "DMND_NOM"), FormeLigne.Texte(r, "DMND_ETAT_DEMANDE"),
            FormeLigne.Texte(r, "R_ETATDEMAND_LIBELLE_ECRAN"), Convert.ToInt32(r["IdInscription"], CultureInfo.InvariantCulture),
            FormeLigne.Texte(r, "NumeroInscription"), r["IdBateau"] is not DBNull);
    }

    /// <summary>La date de travail des inscriptions (WINS, active) ; le jour du geste si la base n'en a pas.</summary>
    private static async Task<DateTime> DateTravailAsync(GesteEnCours geste, CancellationToken ct)
    {
        await using (var t = await geste.LireAsync("SELECT OBJECT_ID(N'v2.DATE_TRAVAIL')", ct))
            if (!await t.ReadAsync(ct) || t.IsDBNull(0)) return geste.Maintenant.Date;
        await using var r = await geste.LireAsync(
            "SELECT DTTRV_DATE_TRAVAIL FROM v2.DATE_TRAVAIL WHERE DTTRV_CODE_DATE_TRAVAIL = @code AND DTTRV_ACTIF = 1", ct,
            new SqlParameter("@code", DateTravailInscriptions));
        return await r.ReadAsync(ct) && r[0] is DateTime d ? d.Date : geste.Maintenant.Date;
    }

    /// <summary>DesignationDebiteursSansCivilite (Inscription.cs:571-578) et le SIREN du PREMIER debiteur, vide s'il n'en a pas
    /// (DebiteursEnd.FirstOrDefault()?.DBEND_NUM_SIREN, PROC_MMA.cs:120). La V2 lit les debiteurs sans ORDER BY (Inscription.cs:521) :
    /// l'ordre de la cle, DBEND_ID_DBEND.</summary>
    private static async Task<(string Designation, string? Siren)> DebiteursAsync(GesteEnCours geste, int idInscription, CancellationToken ct)
    {
        var morceaux = new List<string>();
        string? siren = null;
        await using var r = await geste.LireAsync("""
            SELECT DBEND_DENOMINATION, DBEND_DESIGNATION, DBEND_NUM_SIREN FROM v2.DEBITEUR_END WHERE DBEND_ID_INSCR = @i ORDER BY DBEND_ID_DBEND
            """, ct, new SqlParameter("@i", idInscription));
        while (await r.ReadAsync(ct))
        {
            var denomination = FormeLigne.Texte(r, "DBEND_DENOMINATION");
            var numero = FormeLigne.Texte(r, "DBEND_NUM_SIREN");
            if (morceaux.Count == 0) siren = numero.Length > 0 ? numero : null;
            morceaux.Add((denomination.Length > 0 ? denomination : FormeLigne.Texte(r, "DBEND_DESIGNATION")) + (numero.Length > 0 ? $" ({numero})" : ""));
        }
        return (string.Join(" / ", morceaux), siren);
    }
}
