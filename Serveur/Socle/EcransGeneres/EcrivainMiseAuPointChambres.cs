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
///   - LA COMPOSITION (:1204-1208 ; AffectComposition, :1547-1555) : APPAF_ID_CMAUD de chaque appel = la composition choisie dans
///     la liste des compositions de l'audience (OPE_RESULTAT de PROC_MJUDS, :314-340 : valeur CMPST_ID_CMAUD, synonyme
///     CMPST_NUM_ORDRE, montree pour une audience seulement, :139-143). La V3 la designe par son numero d'ordre (le synonyme que
///     l'agent tape dans la liste de RIG v2) et la cherche dans l'audience de chaque appel : sans composition de ce numero, refus ;
///   - LE PROCHAIN APPEL (:1231-1238 ; AffectProchainAppel, :1612-1615 ; AffectAppelAffaireDirect, :1698-1703) : un appel neuf a
///     l'audience saisie, SEULEMENT si l'instance n'a pas deja un appel a une audience a venir (AUDNC_DATE > aujourd'hui, :1234) ;
///     numero d'ordre = le plus grand de l'instance + 1 (1 au moins), etat de retour « A » (l'appel vide du lot, :285-290), type
///     de classement saisi (APPAF_TYPE_CLASSEMENT est NOT NULL, APPEL_AFFAIRE.sql:7), sans composition (OPE_APP_AFFAIRE ne la
///     montre que pour un appel passe, OPE_APP_AFFAIRE.cs:355, :414). RIG v2 choisit l'audience dans une liste
///     (OPE_APP_AFFAIRE.SelectAudienceOuCabinet) ; la V3 la designe par sa date : un jour a plusieurs audiences est refuse,
///     comme au renvoi du retour d'audience (EcrivainRetourAudience.AudiencesDuJourAsync), et un cabinet ne se choisit pas ;
///   - LA DECISION JUDICIAIRE (:1241-1251 ; AffectDecisionJudiciaire, :1617-1627 ; AffectDecisionJudiciaireDirect, :1705-1710) :
///     si un type de decision est saisi et que l'appel n'a pas deja une decision deliberee (DecisionsJudiciairesDeliberés,
///     AppelAffaire.cs:315-322 : DCJDC_ID_APPAF_DELIBERER = l'appel), une DECISION_JUDICIAIRE neuve sur l'instance : numero
///     d'ordre = le plus grand + 1 (1 au moins), type, date prevue, numero source neuf (DupliquerPourAffectationEnMasse,
///     DecisionJudiciaire.cs:355-395), categorie « I » et etats « 0 » du constructeur (DecisionJudiciaire.cs:305-322), les
///     DEFAULT de RIG_DEV pour le reste (DECISION_JUDICIAIRE.sql:18-39) que la copie de dev ne porte pas. La date de la decision
///     (DCJDC_DATE) n'est pas saisie en lot : le champ est masque tant qu'elle est vide hors juge-commissaire
///     (OPE_DECISION_JUDICIAIRE_MJUDS.cs:479) ;
///   - LA MESURE jointe a la decision (AffectDecisionJudiciaire, :1617-1627 ; GetMesureSaisie, :1474-1490) : RIG v2 ne la montre
///     que pour un type de decision qui porte une mesure liee (R_TYPEDECISI_TYPE_MESURE_LIE, OPE_DECISION_JUDICIAIRE_MJUDS.cs:487-494)
///     et n'en recopie en lot que le TYPE : une MESURE neuve sur l'instance et la decision, numero d'ordre 1, numero source neuf
///     (Mesure.CreateMesure, Mesure.cs:488-496 ; DecisionJudiciaire.Sauve, :1594-1598). Rien a saisir : le type de decision suffit ;
///   - L'EDITION DE LA DECISION (DoGenererLesEditions hors VALIDATION_RESULTAT, PROC_MPCS.cs:757-762 ; GenererLesDemandesEdition,
///     :1337-1346, instances dans l'ordre des roles) : chaque decision creee passe par DecisionJudiciaire.CreateDemandeEdition
///     (DecisionJudiciaire.cs:1640-1705). Sans code-barre en lot (la case n'est visible qu'en affaire unique,
///     OPE_DECISION_JUDICIAIRE_MJUDS.cs:640-646 : EditCodeBarreCoche faux), le reglage d'edition du type (R_TYPEDECISI_PARAMETRE_EDITION)
///     donne la demande d'edition (CreateDemandeEditionByParametreGeneration, DemandeEdition.cs : null si le reglage n'existe pas) :
///     source DECI_JUDICIAIRE et numero source de la decision, libelle du document (LIBELLE_DOCUMENT du reglage, sinon PRMED_LIBELLE :
///     ParametreEdition.LibelleDocument), code CFE-CP et nature Tribunal digital du reglage, libelle source = le type de decision,
///     LISTE_ENTITE_DESTINATION = les destinataires de l'instance (DestinatairesEditionsLot2.listeEntites = RecupDestEditLot2(true) :
///     le document interne COCHE en tete, SetDocumentInterneVisibility(true), DestinatairesEditionsLot2.cs:147-156, puis les autres
///     decoches), le courriel du premier coche (le document interne : aucun). Les parametres du reglage passent sur la colonne qu'ils
///     nomment, le reste dans DMNED_PARAMETRE_GENERATION (CorrigeParametre, DemandeEditionPourServices.cs:514-540 : la regle de
///     EditionsEvenementV2.DeposerAsync), puis CreateDemandeEdition repose le libelle et le groupage automatique (DemandeEdition.cs:
///     554-558) : un reglage a GROUPAGE_AUTOMATIQUE="0" reste groupe. Elle entre dans la file des editions en phase « C », ecrite
///     par DeposerAsync comme les editions du renvoi. Un type sans reglage n'a pas d'edition : 121 des 350 types utilisables en
///     saisie de RIG_DEV en portent un (lu le 27/11/2026) ;
///   - LA DEMANDE : creee par instance a l'ouverture et validee a la sauvegarde (LockInstances, :1019-1037 ; ValidationSauver,
///     :769-772), sans facture en lot (MetierFacture = false, proc_mpcs.md:42). L'edition de la decision s'y rattache : pour une
///     instance qui recoit une edition, la demande MPCS s'ecrit comme celle du renvoi (EcrivainRetourAudience.DemandeAsync) ; pour les
///     autres, elle n'est qu'une trace et le journal des gestes la remplace (decision du chef).
///
/// CE QUE LA V3 NE REPREND PAS (nomme, rien en silence, avec la raison) :
///   - le CALENDRIER de procedure (:1218-1229 ; AffectCalendrier, :1579-1610) : RIG v2 recopie sur chaque instance la LISTE des
///     etapes que l'agent a saisies (GetEtapesCalendrierSaisies, :1390-1397 : une operation OPE_ETAPE_CALENDRIER par etape, avec sa
///     date et son appel d'affaire). Un ecran genere ne saisit pas de sous-liste (web/src/ecrans/formes/commun.tsx:374 : un Tableau
///     se lit avec l'objet, il ne se saisit pas) : sans cette saisie, il n'y a pas de calendrier a ecrire ;
///   - les details de la mesure (juge, expert, dates : OPE_LIEN_DJ_MESURE) : en lot, RIG v2 ne les recopie pas non plus (:1619-1625) ;
///   - la numerotation des minutes, reservee aux instances de juge-commissaire (EJC_GenererNumeroMinute, :830-870 : aucune PC) ;
///   - pour l'edition de la decision : les destinataires PC, prevention, parties, avocats, juges et instances liees de la liste
///     (RecupDestEditLot2FROMInstance, DestinatairesEditionsLot2.cs:1232-1519) -- la liste ne porte que le document interne coche et
///     les destinataires institutionnels decoches, comme les editions du renvoi (EditionsEvenementV2.cs, en tete) ; le choix d'une
///     maquette pour un type sans reglage d'edition (CodeMaquetteSaisi, OPE_DECISION_JUDICIAIRE_MJUDS.cs:120-126, :846-856 : en lot
///     d'instances de juge-commissaire seulement) : sans champ a l'ecran, aucune edition, comme RIG v2 quand rien n'est choisi ;
///   - le brouillon REDA et le lancement de PROC_REDA (document genere, :918-983), les publicites de la « validation avec pub »
///     (BODACC, JAL) et la mise a jour RPVA (UpdateRPVA) : services externes, qu'un ecran de la V3 n'appelle jamais ;
///   - la validation d'UNE affaire (mode SingleInstance : parties, evenements, facturation), qui passe par les ecrans de l'affaire.
/// </summary>
internal sealed class EcrivainMiseAuPointChambres : IEcrivainEcran
{
    public string Procedure => "PROC_MPCS";

    public string Ecran => "Mise au point chambres";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Decider];

    /// <summary>Le code du processus (PROC_MPCS.cs:266), celui de ses demandes.</summary>
    internal const string CodeProcessus = "MPCS";

    /// <summary>Le geste (le Valider du descripteur, Surveiller.cs) et ses parametres, au mot pres.</summary>
    internal const string Valider = "Valider la décision", ParametreResultat = "Résultat", ParametreType = "Type de décision",
                          ParametreDatePrevue = "Date prévue", ParametreComposition = "Composition", ParametreProchaine = "Prochaine audience",
                          ParametreClassement = "Type de classement";

    /// <summary>Une affaire cochee, telle que le geste la lit.</summary>
    private sealed record Appel(int Id, int IdInstance, string Role, string TypeInstance, bool AvecPc, string? CodePlumitif, string EtatRetour,
                                int IdAudience, DateTime? DateAudience, string? Heure, bool AvecCompo, int Compositions, int Deliberees);

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
        if (await RefusVerrouAsync(geste, appels.Select(a => a.IdInstance), ct) is { } verrou) return IssueGeste.Refuser(verrou);

        // Ce qui est saisi.
        var saisiResultat = Saisi(geste, ParametreResultat);
        var saisiType = Saisi(geste, ParametreType);
        var saisiDatePrevue = Saisi(geste, ParametreDatePrevue);
        var saisiComposition = Saisi(geste, ParametreComposition);
        var saisiProchaine = Saisi(geste, ParametreProchaine);
        var saisiClassement = Saisi(geste, ParametreClassement);
        if (saisiResultat.Length == 0 && saisiType.Length == 0 && saisiComposition.Length == 0 && saisiProchaine.Length == 0 && saisiClassement.Length == 0)
            return IssueGeste.Refuser($"Saisissez un « {ParametreResultat} », une « {ParametreComposition} », une « {ParametreProchaine} » ou un "
                                      + $"« {ParametreType} » : rien n'est à valider.");

        // La composition de l'audience de chaque appel, par son numero d'ordre (le synonyme de la liste de RIG v2, OPE_RESULTAT.cs:340).
        var compositions = new Dictionary<int, int>();
        short? numeroComposition = null;
        if (saisiComposition.Length > 0)
        {
            if (!short.TryParse(saisiComposition, NumberStyles.None, CultureInfo.InvariantCulture, out var numero))
                return IssueGeste.Refuser($"« {ParametreComposition} » se désigne par son numéro dans l'audience : « {saisiComposition} » n'en est pas un.");
            numeroComposition = numero;
            foreach (var a in appels)
            {
                if (await CompositionAsync(geste, a.IdAudience, numero, ct) is not { } idComposition)
                    return IssueGeste.Refuser($"L'audience du rôle {a.Role} n'a pas de composition n° {numero}.");
                compositions[a.Id] = idComposition;
            }
        }
        EcrivainRetourAudience.CodePlumitif? code = null;
        if (saisiResultat.Length > 0)
        {
            code = await EcrivainRetourAudience.LireCodeAsync(geste, saisiResultat, ct, saisieSeulement: true);
            if (code is null) return IssueGeste.Refuser($"Le code plumitif « {saisiResultat} » n'est pas utilisable en saisie dans la base du greffe.");
            if (appels.FirstOrDefault(a => !code.Compatibles.Contains(EcrivainRetourAudience.Traduit(a.TypeInstance))) is { } incompatible)
                return IssueGeste.Refuser($"Le code plumitif « {code.Code} » n'est pas compatible avec le type d'instance du rôle {incompatible.Role}.");
        }
        DateTime? datePrevue = null;
        string? mesureLiee = null;
        if (saisiType.Length > 0)
        {
            if (!await TypeDecisionSaisissableAsync(geste, saisiType, ct))
                return IssueGeste.Refuser($"Le type de décision « {saisiType} » n'est pas utilisable en saisie dans la base du greffe.");
            if (saisiDatePrevue.Length > 0 && (datePrevue = AudiencesV2.Date(saisiDatePrevue)) is null)
                return IssueGeste.Refuser($"« {ParametreDatePrevue} » n'est pas une date : « {saisiDatePrevue} ».");
            mesureLiee = await MesureLieeAsync(geste, saisiType, ct);
        }
        else if (saisiDatePrevue.Length > 0)
            return IssueGeste.Refuser($"« {ParametreDatePrevue} » se rapporte à la décision : saisissez aussi le « {ParametreType} ».");

        // Le prochain appel (AffectProchainAppel, PROC_MPCS.cs:1231-1238) : l'audience du jour saisi et son type de classement.
        (int Id, string Libelle)? prochaine = null;
        if (saisiProchaine.Length > 0)
        {
            if (AudiencesV2.Date(saisiProchaine) is not { } jour) return IssueGeste.Refuser($"« {ParametreProchaine} » n'est pas une date : « {saisiProchaine} ».");
            if (saisiClassement.Length == 0)
                return IssueGeste.Refuser($"« {ParametreClassement} » est obligatoire pour le prochain appel (APPEL_AFFAIRE.APPAF_TYPE_CLASSEMENT).");
            if (!await ClassementExisteAsync(geste, saisiClassement, ct))
                return IssueGeste.Refuser($"Le type de classement « {saisiClassement} » n'est pas dans la base du greffe.");
            var trouvees = await EcrivainRetourAudience.AudiencesDuJourAsync(geste, jour, audience: true, chambre: null, ct);
            if (trouvees.Count == 0) return IssueGeste.Refuser($"Aucune audience le {FormeLigne.Date(jour)} dans la base du greffe.");
            if (trouvees.Count > 1) return IssueGeste.Refuser($"Plusieurs audiences le {FormeLigne.Date(jour)} : la V3 ne sait pas encore choisir laquelle.");
            prochaine = trouvees[0];
        }
        else if (saisiClassement.Length > 0)
            return IssueGeste.Refuser($"« {ParametreClassement} » se rapporte au prochain appel : saisissez aussi la « {ParametreProchaine} ».");

        // Les ecritures, appel par appel, dans l'ordre de ApplyModifications (mode multi, PROC_MPCS.cs:1204-1251).
        // fix-ok: contrôle du lot 7 (27/11/2026) -- « Valider la décision » créait la décision sans son édition et ne le disait pas
        // (DoGenererLesEditions, PROC_MPCS.cs:757-762 ; GenererLesDemandesEdition, :1337-1346) ; mesure : PROC_MPCS.gestes, bloc 27
        // rouge sans EditionDecisionAsync (aucune ligne DECI_JUDICIAIRE, aucune demande MPCS), vert avec ; bloc 12 (T17 sans
        // réglage) : aucune édition, aucune demande.
        var traces = new List<TraceGeste>();
        var ids = new List<string>();
        var decisions = 0;
        var appeles = 0;
        var demandes = new Dictionary<int, (int Id, string Numero, string Role)>();
        var editions = new List<(string Role, int IdDemande, EditionsEvenementV2.Edition Edition)>();
        foreach (var a in appels)
        {
            ids.Add(a.Id.ToString(CultureInfo.InvariantCulture));
            if (compositions.TryGetValue(a.Id, out var idComposition)) traces.Add(await PoserCompositionAsync(geste, a, idComposition, ct));
            if (code is not null) traces.Add(await PoserResultatAsync(geste, a, code, ct));
            if (prochaine is { } p && !await EcrivainRetourAudience.AppelAVenirAsync(geste, a.IdInstance, ct))
            {
                traces.Add(await ProchainAppelAsync(geste, a, p.Id, p.Libelle, saisiClassement, ct));
                appeles++;
            }
            if (saisiType.Length > 0 && a.Deliberees == 0)
            {
                var decision = await CreerDecisionAsync(geste, a.IdInstance, a.Role, saisiType, datePrevue, mesureLiee, ct);
                traces.AddRange(decision.Traces);
                decisions++;
                // L'edition de la decision, rattachee a la demande MPCS de l'instance (en tete : L'EDITION DE LA DECISION, LA DEMANDE).
                if (await EditionDecisionAsync(geste, saisiType, decision.NumeroSource, ct) is { } edition)
                {
                    if (!demandes.TryGetValue(a.IdInstance, out var demande))
                    {
                        var (numero, refusNumero) = await CreationV2.NumeroAsync(geste, "DMD", geste.Maintenant.Year, ct);
                        if (numero is null) return IssueGeste.Refuser(refusNumero!);
                        var instance = await EcrivainRetourAudience.LireInstanceAsync(geste, a.IdInstance, ct);
                        demandes[a.IdInstance] = demande =
                            (await EcrivainRetourAudience.DemandeAsync(geste, CodeProcessus, a.IdInstance, a.Role, instance, numero, ct), numero, a.Role);
                    }
                    editions.Add((a.Role, demande.Id, edition));
                }
            }
        }
        // Les editions dans la file, instance par instance dans l'ordre des roles (GenererLesDemandesEdition, PROC_MPCS.cs:1339).
        var deposees = new Dictionary<int, List<int>>();
        foreach (var (_, idDemande, e) in editions.OrderBy(x => x.Role, StringComparer.Ordinal))
        {
            var depot = await EditionsEvenementV2.DeposerAsync(geste, idDemande, e, ct);
            if (!deposees.TryGetValue(idDemande, out var liste)) deposees[idDemande] = liste = [];
            liste.Add(depot.Id);
        }
        foreach (var demande in demandes.Values)
            traces.Add(new TraceGeste("affaire", $"la demande {demande.Numero} du rôle {demande.Role}", demande.Id.ToString(CultureInfo.InvariantCulture),
                "a cree", null, new { Demande = demande.Numero, Processus = CodeProcessus, Editions = deposees.GetValueOrDefault(demande.Id) ?? [] }));
        var n = appels.Count;
        if (traces.Count == 0)
        {
            // Rien d'ecrit : seuls un prochain appel et une decision etaient saisis, et chaque instance les a deja (:1234, :1243).
            var rien = new List<string>();
            if (prochaine is not null) rien.Add("chaque affaire cochée a déjà un appel à une audience à venir : aucun prochain appel n'est ajouté");
            if (saisiType.Length > 0) rien.Add($"chaque affaire cochée a déjà une décision délibérée : aucune décision « {saisiType} » n'est ajoutée");
            var motif = string.Join(" ; ", rien);
            return IssueGeste.Refuser(char.ToUpperInvariant(motif[0]) + motif[1..] + ".");
        }
        var morceaux = new List<string>();
        if (numeroComposition is { } c) morceaux.Add($"composition n° {c} posée sur {n} affaire{(n > 1 ? "s" : "")}");
        if (code is not null) morceaux.Add($"résultat {code.Code} posé sur {n} affaire{(n > 1 ? "s" : "")}");
        if (prochaine is { } pa)
            morceaux.Add(appeles == 0 ? "aucun prochain appel ajouté (déjà appelées à une audience à venir)"
                : $"{appeles} prochain{(appeles > 1 ? "s" : "")} appel{(appeles > 1 ? "s" : "")} à l'{pa.Libelle}"
                  + (appeles < n ? $", {n - appeles} déjà appelée{(n - appeles > 1 ? "s" : "")} à une audience à venir" : ""));
        if (saisiType.Length > 0)
            morceaux.Add(decisions == 0 ? "aucune décision ajoutée (déjà délibérées)"
                : $"{decisions} décision{(decisions > 1 ? "s" : "")} {saisiType} créée{(decisions > 1 ? "s" : "")}"
                  + (mesureLiee is null ? "" : $" avec sa mesure {mesureLiee}")
                  + (decisions < n ? $", {n - decisions} déjà délibérée{(n - decisions > 1 ? "s" : "")}" : ""));
        if (editions.Count > 0)
            morceaux.Add(editions.Count == 1 ? "1 édition placée dans la file des éditions" : $"{editions.Count} éditions placées dans la file des éditions");
        var message = string.Join(" ; ", morceaux);
        return IssueGeste.Ecrit(char.ToUpperInvariant(message[0]) + message[1..] + ".", [.. ids], [.. traces]);
    }

    /// <summary>
    /// Le refus du verrou des instances, au message de RIG v2 ; null si aucune n'est tenue. Les deux traitements en lot du retour
    /// d'audience verrouillent chaque instance a l'ouverture et ont les memes messages : la mise au point des chambres (LockInstances,
    /// PROC_MPCS.cs:1006-1067, messages :1047-1058) et le renvoi (PROC_MJUDS.cs:210, LockInstances :959-1034, messages :1014-1027).
    /// RIG v2 propose de continuer sur les instances libres ; la V3 refuse et nomme les instances tenues, l'agent les decoche.
    /// </summary>
    internal static async Task<string?> RefusVerrouAsync(GesteEnCours geste, IEnumerable<int> idsInstances, CancellationToken ct)
    {
        var instances = idsInstances.Distinct().ToList();
        var bloquees = new List<string>();
        foreach (var id in instances)
            if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance,
                    id.ToString(CultureInfo.InvariantCulture), ct) is { } motif)
                bloquees.Add(motif);
        // Sur une ligne : la V3 affiche le motif de chaque instance a la suite.
        if (bloquees.Count == 0) return null;
        return bloquees.Count == instances.Count
            ? "Toutes les instances sélectionnées sont bloquées : " + string.Join(" ; ", bloquees)
            : "Certaines instances sont bloquées, décochez-les pour travailler sur les autres : " + string.Join(" ; ", bloquees);
    }

    /// <summary>La composition n° <paramref name="numero"/> de l'audience (CMPST_NUM_ORDRE, le synonyme de la liste de RIG v2).</summary>
    private static async Task<int?> CompositionAsync(GesteEnCours geste, int idAudience, short numero, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT TOP (1) CMPST_ID_CMAUD FROM v2.COMPOSITION WHERE CMPST_ID_ADNC = @a AND CMPST_NUM_ORDRE = @n ORDER BY CMPST_ID_CMAUD
            """, ct, new SqlParameter("@a", idAudience), new SqlParameter("@n", numero));
        return await r.ReadAsync(ct) ? Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Pose la composition sur l'appel (AffectComposition, PROC_MPCS.cs:1547-1555).</summary>
    private static async Task<TraceGeste> PoserCompositionAsync(GesteEnCours geste, Appel a, int idComposition, CancellationToken ct)
    {
        int? avant;
        await using (var r = await geste.LireAsync("SELECT APPAF_ID_CMAUD FROM v2.APPEL_AFFAIRE WHERE APPAF_ID_APPAF = @id", ct, new SqlParameter("@id", a.Id)))
            avant = await r.ReadAsync(ct) && r.GetValue(0) is not DBNull ? Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture) : null;
        await geste.EcrireAsync("UPDATE v2.APPEL_AFFAIRE SET APPAF_ID_CMAUD = @c WHERE APPAF_ID_APPAF = @id", ct,
            new SqlParameter("@c", idComposition), new SqlParameter("@id", a.Id));
        return new TraceGeste("affaire", $"l'appel du rôle {a.Role}", a.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
            new { Composition = avant }, new { Composition = idComposition });
    }

    /// <summary>Le type de classement existe-t-il (la liste de OPE_APP_AFFAIRE lit TYPE_CLASSEMENT).</summary>
    private static async Task<bool> ClassementExisteAsync(GesteEnCours geste, string classement, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.TYPE_CLASSEMENT WHERE R_TYPECLASSE_VALEUR = @t", ct, new SqlParameter("@t", classement));
        await r.ReadAsync(ct);
        return r.GetInt32(0) > 0;
    }

    /// <summary>Le prochain appel du lot (AffectAppelAffaireDirect, PROC_MPCS.cs:1698-1703) : numero d'ordre = le plus grand de
    /// l'instance + 1 (1 au moins), etat de retour « A » (:285-290), sans composition.</summary>
    private static async Task<TraceGeste> ProchainAppelAsync(GesteEnCours geste, Appel a, int idAudience, string libelle, string typeClassement,
                                                             CancellationToken ct)
    {
        int ordre;
        await using (var r = await geste.LireAsync("SELECT ISNULL(MAX(APPAF_NUM_ORDRE), 0) + 1 FROM v2.APPEL_AFFAIRE WHERE APPAF_ID_INSTN = @i", ct,
                         new SqlParameter("@i", a.IdInstance)))
        {
            await r.ReadAsync(ct);
            ordre = Math.Max(1, Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture));
        }
        return await EcrivainRetourAudience.InsererAppelAsync(geste, a.IdInstance, a.Role, ordre, idAudience, null, typeClassement, libelle, ct);
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
    /// <param name="mesureLiee">Le type de mesure lie au type de decision (<see cref="MesureLieeAsync"/>) : une MESURE de ce type est
    /// jointe a la decision (AffectDecisionJudiciaire, PROC_MPCS.cs:1617-1627 ; PROC_MJUDS.cs:1499-1509).</param>
    // fix-ok: controle du lot 7 (26/11/2026) -- le renvoi du retour d'audience doit ecrire la meme decision (PROC_MJUDS.cs:1186-1193) :
    // cette ecriture est partagee plutot que recopiee. Second controle : la composition, le prochain appel et la mesure liee
    // s'ecrivent comme PROC_MPCS (:1204-1251, :1617-1627) ; seul le calendrier reste, raison mesuree (commun.tsx:374).
    internal static async Task<DecisionCreee> CreerDecisionAsync(GesteEnCours geste, int idInstance, string role, string type,
                                                                 DateTime? datePrevue, string? mesureLiee, CancellationToken ct)
    {
        var numeroSource = Guid.NewGuid().ToString().ToUpperInvariant();
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
            ["DCJDC_NUMERO_SOURCE"] = numeroSource,
            // Le constructeur de DecisionJudiciaire (DecisionJudiciaire.cs:305-322).
            ["DCJDC_CAT_DECISION"] = "I", ["DCJDC_EXECUTION_PROVISOIRE"] = "0", ["DCJDC_RESSORT"] = "0", ["DCJDC_AVANT_DIRE_DROIT"] = "0",
            ["DCJDC_CONTRADICTOIRE"] = "0", ["DCJDC_ETAT_REDACTION"] = "0", ["DCJDC_ETAT_DIFFUSION"] = "0", ["DCJDC_ETAT_STABILITE"] = "0",
            // Les DEFAULT de RIG_DEV (DECISION_JUDICIAIRE.sql:18-39), que la copie de dev ne porte pas.
            ["DCJDC_ETAT_VALIDITE"] = "0", ["DCJDC_POST_DECISION"] = false, ["DCJDC_REDIGEE_GREFFE"] = true, ["DCJDC_AVEC_PIECE_NUMERISE"] = false,
            ["DCJDC_COPIE_EXECUTOIRE_DEMANDEUR"] = false, ["DCJDC_DECISION_PUBLIQUE"] = false, ["DCJDC_DEBAT_PUBLIC"] = false,
        }, ct);
        var decision = new TraceGeste("affaire", $"la décision du rôle {role}", id.ToString(CultureInfo.InvariantCulture), "a cree", null,
            new { Instance = role, Type = type, DatePrevue = datePrevue, NumOrdre = ordre });
        if (mesureLiee is null) return new DecisionCreee(id, numeroSource, [decision]);
        // La mesure du type lie (Mesure.CreateMesure, Mesure.cs:488-496 : numero source neuf, numero d'ordre 1, type) ; l'instance et
        // la decision lui sont posees a la sauvegarde de la decision (DecisionJudiciaire.Sauve, DecisionJudiciaire.cs:1594-1598).
        var idMesure = await geste.InsererAsync("v2.MESURE", "MSINS_ID_MSINS", new Dictionary<string, object?>
        {
            ["MSINS_ID_INSTN"] = idInstance, ["MSINS_ID_DCJDC"] = id, ["MSINS_NUM_ORDRE"] = (short)1, ["MSINS_TYPE_MESURE"] = mesureLiee,
            ["MSINS_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(),
        }, ct);
        return new DecisionCreee(id, numeroSource, [decision, new TraceGeste("affaire", $"la mesure de la décision du rôle {role}",
            idMesure.ToString(CultureInfo.InvariantCulture), "a cree", null, new { Instance = role, Decision = id, Type = mesureLiee })]);
    }

    /// <summary>La decision ecrite : son id, son numero source (la source de son edition) et ses traces.</summary>
    internal sealed record DecisionCreee(int Id, string NumeroSource, IReadOnlyList<TraceGeste> Traces);

    /// <summary>
    /// L'edition de la decision du type <paramref name="type"/> (DecisionJudiciaire.CreateDemandeEdition sans code-barre,
    /// DecisionJudiciaire.cs:1656-1678 ; en tete : L'EDITION DE LA DECISION) ; null si le type n'a pas de reglage d'edition ou si ce
    /// reglage n'est pas dans la base (CreateDemandeEditionByParametreGeneration rend null), comme RIG v2 sans maquette choisie.
    /// </summary>
    internal static async Task<EditionsEvenementV2.Edition?> EditionDecisionAsync(GesteEnCours geste, string type, string numeroSource, CancellationToken ct)
    {
        string code;
        await using (var r = await geste.LireAsync("SELECT R_TYPEDECISI_PARAMETRE_EDITION FROM v2.TYPE_DECISION WHERE R_TYPEDECISI_VALEUR = @t", ct,
                         new SqlParameter("@t", type)))
            code = await r.ReadAsync(ct) ? FormeLigne.Texte(r, "R_TYPEDECISI_PARAMETRE_EDITION") : "";
        if (code.Length == 0) return null;                                                     // R_TYPEDECISI_PARAMETRE_EDITION != "" (:1661)
        if (await EditionsV2.ParametreAsync(geste, code, ct) is not { } parametre) return null;
        var reglage = await EditionsEvenementV2.ReglageEditionAsync(geste, code, ct);
        var parametres = reglage?.Parametres ?? new SortedDictionary<string, string>(StringComparer.Ordinal);
        // listeEntites (DestinatairesEditionsLot2.cs:59-63) : le document interne coche en tete, puis les destinataires decoches.
        var destinataires = new List<EditionsEvenementV2.Destinataire> { new() { ParDefaut = "1" } };
        destinataires.AddRange(await EditionsEvenementV2.DestinatairesInstanceAsync(geste, ct));
        var edition = new EditionsEvenementV2.Edition
        {
            Parametres = parametres, Destinataires = destinataires, Impression = true, TypeGeneration = parametre.TypeGeneration,
            // ParametreEdition.LibelleDocument : LIBELLE_DOCUMENT du reglage, sinon PRMED_LIBELLE.
            LibelleImpose = parametres.TryGetValue("LIBELLE_DOCUMENT", out var libelle) && libelle.Length > 0 ? libelle : parametre.Libelle,
            LibelleSource = type,                                                                // DMNED_LIBELLE_SOURCE (:1666)
            Courriel = destinataires.First(x => x.ParDefaut == "1").Email,                       // :1672-1676
        };
        // Pose apres les parametres : le groupage de CreateDemandeEdition (DemandeEdition.cs:558), la source et le reglage
        // (CreateDemandeEditionByParametreGeneration : DMNED_CODE_ENTITE_SOURCE, DMNED_VALEUR_ID_SOURCE, CFE-CP, Tribunal digital).
        edition.Colonnes["DMNED_GROUPAGE_AUTOMATIQUE"] = true;
        edition.Colonnes["DMNED_CODE_ENTITE_SOURCE"] = "DECI_JUDICIAIRE";
        edition.Colonnes["DMNED_VALEUR_ID_SOURCE"] = numeroSource;
        edition.Colonnes["DMNED_CODE_CFECP"] = parametre.Cfecp;
        edition.Colonnes["DMNED_TRIBUNAL_DIGITAL_NATURE_DOC"] = parametre.NatureTribunalDigital;
        return edition;
    }

    /// <summary>Le type de mesure lie au type de decision (R_TYPEDECISI_TYPE_MESURE_LIE) : RIG v2 montre et joint une mesure de ce type
    /// a la decision (OPE_DECISION_JUDICIAIRE_MJUDS.cs:487-494) ; null sans mesure liee.</summary>
    internal static async Task<string?> MesureLieeAsync(GesteEnCours geste, string type, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT R_TYPEDECISI_TYPE_MESURE_LIE FROM v2.TYPE_DECISION WHERE R_TYPEDECISI_VALEUR = @t", ct,
                                                  new SqlParameter("@t", type));
        return await r.ReadAsync(ct) && FormeLigne.Texte(r, "R_TYPEDECISI_TYPE_MESURE_LIE") is { Length: > 0 } m ? m : null;
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
                   a.AUDNC_ID_ADNC, a.AUDNC_DATE, a.AUDNC_HEURE, CONVERT(bit, ISNULL(ch.R_CHAMBRE_AVEC_COMPO, 0)) AS AvecCompo,
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
            Convert.ToInt32(r["AUDNC_ID_ADNC"], CultureInfo.InvariantCulture), FormeLigne.DateDe(r, "AUDNC_DATE"), Opt("AUDNC_HEURE"), r["AvecCompo"] is true,
            Convert.ToInt32(r["Compositions"], CultureInfo.InvariantCulture), Convert.ToInt32(r["Deliberees"], CultureInfo.InvariantCulture));
    }
}
