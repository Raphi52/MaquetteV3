using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Valider la décision » de la « Mise au point chambres » (PROC_MPCS) sur les affaires de procedure collective cochees au role
/// (LecteurMiseAuPointChambres.cs) : la validation EN MASSE de RIG v2 (mode MultiInstance, plusieurs appels d'affaire). Mode
/// d'emploi : EcrivainEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 7, 26/11/2026.
///
/// SOURCE (ancien RIG, Source\RIG\DLL\Processus\PROC_RETAUD\PROC_MPCS\PROC_MPCS.cs, le code fait foi sur proc_mpcs.md) :
///   - L'OUVERTURE : le retour d'audience lance PROC_MPCS sur les appels coches quand TOUTES leurs instances ont une procedure
///     collective et qu'aucune n'est traitee (OPE_LIST_APPEL_AFFAIRE.cs:1409-1412), apres CanDoActions (:1356-1369, :1586 : une
///     audience passee sans composition, quand sa chambre l'impose, n'autorise aucune action) ;
///   - LE VERROU : une demande par instance (LockInstances, PROC_MPCS.cs:1006-1067, Instance.CreateAndLockDemande) ; une instance
///     tenue par une autre demande est refusee au message de RIG v2 (VerrouDemandeV2). RIG v2 propose de continuer sur les
///     instances libres (:1053-1060) : la V3 refuse et nomme les instances bloquees, l'agent les decoche ;
///   - LE RESULTAT (ApplyModifications, mode multi, :1197-1216 ; AffectCodePlumitif, :1558-1577) : le code plumitif saisi se
///     pose sur chaque appel qui n'en a pas encore (APPAF_CODE_PLUMITIF), APPAF_ETAT_RETOUR = « P » si le code est PRONONCE,
///     « M » sinon. Le code est choisi dans la liste de saisie (utilisable en saisie) et doit convenir au type de l'instance
///     (CODE_PLUMITIF.EstCompatibleInstance, CODE_PLUMITIF.cs:76-97) -- la meme lecture que le retour d'audience ;
///   - LA DECISION JUDICIAIRE (:1238-1248 ; AffectDecisionJudiciaire, :1612-1622 ; AffectDecisionJudiciaireDirect, :1705-1710) :
///     si un type de decision est saisi et que l'appel n'a pas deja une decision deliberee (DecisionsJudiciairesDeliberés,
///     AppelAffaire.cs:315-322 : DCJDC_ID_APPAF_DELIBERER = l'appel), une DECISION_JUDICIAIRE neuve sur l'instance : numero
///     d'ordre = le plus grand + 1 (1 au moins), type, date prevue, numero source neuf (DupliquerPourAffectationEnMasse,
///     DecisionJudiciaire.cs:355-395), categorie « I » et etats « 0 » du constructeur (DecisionJudiciaire.cs:305-322), les
///     DEFAULT de RIG_DEV pour le reste (DECISION_JUDICIAIRE.sql:18-39) que la copie de dev ne porte pas. La date de la decision
///     (DCJDC_DATE) n'est pas saisie en lot : le champ est masque tant qu'elle est vide hors juge-commissaire
///     (OPE_DECISION_JUDICIAIRE_MJUDS.cs:479) ;
///   - LA DEMANDE : creee par instance a l'ouverture et validee a la sauvegarde (ValidationSauver, PROC_MPCS.cs:769-772), sans
///     facture en lot (MetierFacture = false, proc_mpcs.md:42) : le journal des gestes la remplace (decision du chef).
///
/// CE QUE LA V3 NE REPREND PAS (nomme, rien en silence, avec la raison) : la composition choisie dans le resultat
/// (AffectComposition, :1204-1208) -- RIG v2 la prend dans la liste des compositions de l'audience, et une liste de descripteur a
/// des choix fixes (Champ.Liste, Pont/EcransGeneres/Descripteur.cs:148) ; le calendrier de procedure (etape non portee) ; le
/// prochain appel saisi en lot (AffectProchainAppel, :1231-1238, seulement si l'instance n'a pas d'appel futur) -- non repris par
/// ce lot, l'ecriture existe (EcrivainRetourAudience, « Renvoyer ») ; la mesure associee a la decision (GetMesureSaisie,
/// :1474-1490 ; DecisionJudiciaire.AddMesure) -- aucun ecrivain V3 ne cree une mesure (EcrivainMesuresProvisoires ne fait que la
/// modifier) ; la numerotation des minutes, reservee aux instances de juge-commissaire (EJC_GenererNumeroMinute, :830-870 : aucune PC) ; le
/// brouillon REDA et le lancement de PROC_REDA (document genere, :918-983) ; les publicites de la « validation avec pub »
/// (BODACC, JAL : services externes) ; la mise a jour RPVA (UpdateRPVA, service externe) ; la validation d'UNE affaire (mode
/// SingleInstance : parties, evenements, facturation), qui passe par les ecrans de l'affaire.
/// </summary>
internal sealed class EcrivainMiseAuPointChambres : IEcrivainEcran
{
    public string Procedure => "PROC_MPCS";

    public string Ecran => "Mise au point chambres";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Decider];

    /// <summary>Le geste (le Valider du descripteur, Surveiller.cs) et ses parametres, au mot pres.</summary>
    internal const string Valider = "Valider la décision", ParametreResultat = "Résultat", ParametreType = "Type de décision",
                          ParametreDatePrevue = "Date prévue";

    /// <summary>Une affaire cochee, telle que le geste la lit.</summary>
    private sealed record Appel(int Id, int IdInstance, string Role, string TypeInstance, bool AvecPc, string? CodePlumitif, string EtatRetour,
                                DateTime? DateAudience, string? Heure, bool AvecCompo, int Compositions, int Deliberees);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var d = geste.Demande;
        if (d.Nom != Valider) return IssueGeste.Refuser($"Le geste « {d.Nom} » n'existe pas à la mise au point des chambres de RIG v2.");
        if (d.Ids.Length == 0) return IssueGeste.Refuser("Cochez au moins une affaire du rôle.");
        var appels = new List<Appel>();
        foreach (var id in d.Ids)
        {
            if (await LireAppelAsync(geste, id, ct) is not { } a) return IssueGeste.Refuser($"L'affaire « {id} » n'est pas au rôle d'une audience de la base du greffe.");
            appels.Add(a);
        }
        // L'ouverture de PROC_MPCS (OPE_LIST_APPEL_AFFAIRE.cs:1409-1412).
        if (appels.FirstOrDefault(a => !a.AvecPc || a.CodePlumitif is not null) is { } hors)
            return IssueGeste.Refuser($"Le rôle {hors.Role} n'est pas une affaire de procédure collective non traitée : "
                                      + "la mise au point des chambres ne s'ouvre que sur elles.");
        // CanDoActions (OPE_LIST_APPEL_AFFAIRE.cs:1356-1369, :1586).
        foreach (var a in appels.Where(a => EcrivainRetourAudience.Passee(a.DateAudience, a.Heure, geste.Maintenant) && a.AvecCompo && a.Compositions == 0).Take(1))
            return IssueGeste.Refuser($"Audience passée sans composition (rôle {a.Role}) : actions possibles uniquement si l'audience comporte des compositions.");

        // Le verrou de chaque instance (LockInstances, PROC_MPCS.cs:1006-1067).
        var bloquees = new List<string>();
        foreach (var a in appels)
            if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance,
                    a.IdInstance.ToString(CultureInfo.InvariantCulture), ct) is { } motif)
                bloquees.Add(motif);
        // Les messages de RIG v2 (PROC_MPCS.cs:1047-1058), sur une ligne : la V3 affiche le motif de chaque instance a la suite.
        if (bloquees.Count == appels.Count)
            return IssueGeste.Refuser("Toutes les instances sélectionnées sont bloquées : " + string.Join(" ; ", bloquees));
        if (bloquees.Count > 0)
            return IssueGeste.Refuser("Certaines instances sont bloquées, décochez-les pour travailler sur les autres : " + string.Join(" ; ", bloquees));

        // Ce qui est saisi.
        var saisiResultat = Saisi(geste, ParametreResultat);
        var saisiType = Saisi(geste, ParametreType);
        var saisiDatePrevue = Saisi(geste, ParametreDatePrevue);
        if (saisiResultat.Length == 0 && saisiType.Length == 0)
            return IssueGeste.Refuser($"Saisissez un « {ParametreResultat} » ou un « {ParametreType} » : rien n'est à valider.");
        EcrivainRetourAudience.CodePlumitif? code = null;
        if (saisiResultat.Length > 0)
        {
            code = await EcrivainRetourAudience.LireCodeAsync(geste, saisiResultat, ct, saisieSeulement: true);
            if (code is null) return IssueGeste.Refuser($"Le code plumitif « {saisiResultat} » n'est pas utilisable en saisie dans la base du greffe.");
            if (appels.FirstOrDefault(a => !code.Compatibles.Contains(EcrivainRetourAudience.Traduit(a.TypeInstance))) is { } incompatible)
                return IssueGeste.Refuser($"Le code plumitif « {code.Code} » n'est pas compatible avec le type d'instance du rôle {incompatible.Role}.");
        }
        DateTime? datePrevue = null;
        if (saisiType.Length > 0)
        {
            if (!await TypeDecisionSaisissableAsync(geste, saisiType, ct))
                return IssueGeste.Refuser($"Le type de décision « {saisiType} » n'est pas utilisable en saisie dans la base du greffe.");
            if (saisiDatePrevue.Length > 0 && (datePrevue = AudiencesV2.Date(saisiDatePrevue)) is null)
                return IssueGeste.Refuser($"« {ParametreDatePrevue} » n'est pas une date : « {saisiDatePrevue} ».");
        }
        else if (saisiDatePrevue.Length > 0)
            return IssueGeste.Refuser($"« {ParametreDatePrevue} » se rapporte à la décision : saisissez aussi le « {ParametreType} ».");

        // Les ecritures, appel par appel (ApplyModifications, mode multi).
        var traces = new List<TraceGeste>();
        var ids = new List<string>();
        var decisions = 0;
        foreach (var a in appels)
        {
            ids.Add(a.Id.ToString(CultureInfo.InvariantCulture));
            if (code is not null) traces.Add(await PoserResultatAsync(geste, a, code, ct));
            if (saisiType.Length > 0 && a.Deliberees == 0)
            {
                traces.Add(await CreerDecisionAsync(geste, a.IdInstance, a.Role, saisiType, datePrevue, ct));
                decisions++;
            }
        }
        if (traces.Count == 0)
            return IssueGeste.Refuser($"Chaque affaire cochée a déjà une décision délibérée : aucune décision « {saisiType} » n'est ajoutée.");
        var n = appels.Count;
        var morceaux = new List<string>();
        if (code is not null) morceaux.Add($"résultat {code.Code} posé sur {n} affaire{(n > 1 ? "s" : "")}");
        if (saisiType.Length > 0)
            morceaux.Add(decisions == 0 ? "aucune décision ajoutée (déjà délibérées)"
                : $"{decisions} décision{(decisions > 1 ? "s" : "")} {saisiType} créée{(decisions > 1 ? "s" : "")}"
                  + (decisions < n ? $", {n - decisions} déjà délibérée{(n - decisions > 1 ? "s" : "")}" : ""));
        var message = string.Join(" ; ", morceaux);
        return IssueGeste.Ecrit(char.ToUpperInvariant(message[0]) + message[1..] + ".", [.. ids], [.. traces]);
    }

    private static string Saisi(GesteEnCours geste, string parametre)
        => geste.Demande.Valeurs.TryGetValue(parametre, out var v) ? v.Trim() : "";

    /// <summary>Pose le code plumitif et l'etat de retour (AffectCodePlumitif, PROC_MPCS.cs:1558-1577) : sans evenement, le lot ne
    /// genere pas les evenements survenus (:1268-1279, mis en commentaire dans RIG v2).</summary>
    private static async Task<TraceGeste> PoserResultatAsync(GesteEnCours geste, Appel a, EcrivainRetourAudience.CodePlumitif code, CancellationToken ct)
    {
        var etat = code.Prononce ? "P" : "M";
        await geste.EcrireAsync("UPDATE v2.APPEL_AFFAIRE SET APPAF_CODE_PLUMITIF = @code, APPAF_ETAT_RETOUR = @etat WHERE APPAF_ID_APPAF = @id", ct,
            new SqlParameter("@code", code.Code), new SqlParameter("@etat", etat), new SqlParameter("@id", a.Id));
        return new TraceGeste("affaire", $"l'appel du rôle {a.Role}", a.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
            new { Resultat = a.CodePlumitif, EtatRetour = a.EtatRetour }, new { Resultat = code.Code, EtatRetour = etat });
    }

    /// <summary>La decision judiciaire du lot sur l'instance de l'appel (AffectDecisionJudiciaireDirect, PROC_MPCS.cs:1705-1710) ; la
    /// meme ecriture sert au renvoi du retour d'audience (PROC_MJUDS.cs:1587-1593, EcrivainRetourAudience).</summary>
    // fix-ok: controle du lot 7 (26/11/2026) -- le renvoi du retour d'audience doit ecrire la meme decision (PROC_MJUDS.cs:1186-1193) :
    // cette ecriture est partagee plutot que recopiee ; l'en-tete donne en plus la raison de chaque manque (composition, prochain
    // appel, mesure), mesuree dans PROC_MPCS.cs et Descripteur.cs:148.
    internal static async Task<TraceGeste> CreerDecisionAsync(GesteEnCours geste, int idInstance, string role, string type, DateTime? datePrevue,
                                                              CancellationToken ct)
    {
        int ordre;
        await using (var r = await geste.LireAsync("SELECT ISNULL(MAX(DCJDC_NUM_ORDRE), 0) + 1 FROM v2.DECISION_JUDICIAIRE WHERE DCJDC_ID_INSTN = @i", ct,
                         new SqlParameter("@i", idInstance)))
        {
            await r.ReadAsync(ct);
            ordre = Math.Max(1, Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture));
        }
        var id = await geste.InsererAsync("v2.DECISION_JUDICIAIRE", "DCJDC_ID_DCJDC", new Dictionary<string, object?>
        {
            ["DCJDC_ID_INSTN"] = idInstance, ["DCJDC_NUM_ORDRE"] = (short)ordre, ["DCJDC_TYPE_DECISION"] = type, ["DCJDC_DATE_PREVUE"] = datePrevue,
            ["DCJDC_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(),
            // Le constructeur de DecisionJudiciaire (DecisionJudiciaire.cs:305-322).
            ["DCJDC_CAT_DECISION"] = "I", ["DCJDC_EXECUTION_PROVISOIRE"] = "0", ["DCJDC_RESSORT"] = "0", ["DCJDC_AVANT_DIRE_DROIT"] = "0",
            ["DCJDC_CONTRADICTOIRE"] = "0", ["DCJDC_ETAT_REDACTION"] = "0", ["DCJDC_ETAT_DIFFUSION"] = "0", ["DCJDC_ETAT_STABILITE"] = "0",
            // Les DEFAULT de RIG_DEV (DECISION_JUDICIAIRE.sql:18-39), que la copie de dev ne porte pas.
            ["DCJDC_ETAT_VALIDITE"] = "0", ["DCJDC_POST_DECISION"] = false, ["DCJDC_REDIGEE_GREFFE"] = true, ["DCJDC_AVEC_PIECE_NUMERISE"] = false,
            ["DCJDC_COPIE_EXECUTOIRE_DEMANDEUR"] = false, ["DCJDC_DECISION_PUBLIQUE"] = false, ["DCJDC_DEBAT_PUBLIC"] = false,
        }, ct);
        return new TraceGeste("affaire", $"la décision du rôle {role}", id.ToString(CultureInfo.InvariantCulture), "a cree", null,
            new { Instance = role, Type = type, DatePrevue = datePrevue, NumOrdre = ordre });
    }

    /// <summary>Le type est-il dans la liste de saisie (utilisable en saisie) : la liste de OPE_DECISION_JUDICIAIRE_MJUDS, commune a la
    /// mise au point des chambres et au renvoi du retour d'audience.</summary>
    internal static async Task<bool> TypeDecisionSaisissableAsync(GesteEnCours geste, string type, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT COUNT(*) FROM v2.TYPE_DECISION WHERE R_TYPEDECISI_VALEUR = @t AND R_TYPEDECISI_UTILISABLE_SAISIE = 1
            """, ct, new SqlParameter("@t", type));
        await r.ReadAsync(ct);
        return r.GetInt32(0) > 0;
    }

    private static async Task<Appel?> LireAppelAsync(GesteEnCours geste, string id, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        await using var r = await geste.LireAsync("""
            SELECT aa.APPAF_ID_APPAF, aa.APPAF_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_TYPE_INSTANCE,
                   CONVERT(bit, CASE WHEN i.INSTN_ID_PRCLL IS NULL THEN 0 ELSE 1 END) AS AvecPc, aa.APPAF_CODE_PLUMITIF, aa.APPAF_ETAT_RETOUR,
                   a.AUDNC_DATE, a.AUDNC_HEURE, CONVERT(bit, ISNULL(ch.R_CHAMBRE_AVEC_COMPO, 0)) AS AvecCompo,
                   (SELECT COUNT(*) FROM v2.COMPOSITION AS c WHERE c.CMPST_ID_ADNC = a.AUDNC_ID_ADNC) AS Compositions,
                   (SELECT COUNT(*) FROM v2.DECISION_JUDICIAIRE AS dj WHERE dj.DCJDC_ID_APPAF_DELIBERER = aa.APPAF_ID_APPAF) AS Deliberees
            FROM v2.APPEL_AFFAIRE AS aa WITH (UPDLOCK, HOLDLOCK)
            JOIN v2.AUDIENCE_CABINET AS a ON a.AUDNC_ID_ADNC = aa.APPAF_ID_ADNC
            JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = aa.APPAF_ID_INSTN
            LEFT JOIN v2.CHAMBRE AS ch ON ch.R_CHAMBRE_VALEUR = a.AUDNC_CHAMBRE
            WHERE aa.APPAF_ID_APPAF = @id
            """, ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        string? Opt(string c) => FormeLigne.Texte(r, c) is { Length: > 0 } s ? s : null;
        return new Appel(n, Convert.ToInt32(r["APPAF_ID_INSTN"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "INSTN_NUM_ROLE"),
            FormeLigne.Texte(r, "INSTN_TYPE_INSTANCE"), r["AvecPc"] is true, Opt("APPAF_CODE_PLUMITIF"), FormeLigne.Texte(r, "APPAF_ETAT_RETOUR"),
            FormeLigne.DateDe(r, "AUDNC_DATE"), Opt("AUDNC_HEURE"), r["AvecCompo"] is true,
            Convert.ToInt32(r["Compositions"], CultureInfo.InvariantCulture), Convert.ToInt32(r["Deliberees"], CultureInfo.InvariantCulture));
    }
}
