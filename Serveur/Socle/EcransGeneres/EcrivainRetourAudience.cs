using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// Les gestes du « Retour d'audience » (PROC_RETAUD) sur les affaires cochees au role : LecteurRetourAudience.cs. Mode d'emploi :
/// EcrivainEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 7, 26/11/2026.
///
/// « Marquer les décisions comme rendues » -- le menu de la grille, MarquerDecisionsRenduesContextMenu_Click
/// (OPE_LIST_APPEL_AFFAIRE.cs:2217-2279) :
///   - seulement si TOUTES les affaires cochees sont non traitees et prononcees (:2222 ; le menu est grise sinon, :1948) ;
///   - les codes plumitif « J » (jugement rendu) et « O » (ordonnance rendue) doivent exister (:2224-2226) ;
///   - par affaire : J s'il est compatible avec le type de l'instance, sinon O, sinon rien (CODE_PLUMITIF.EstCompatibleInstance,
///     CODE_PLUMITIF.cs:76-97, type traduit Instance.TypeInstanceTraduite, Instance.cs:967-983) ; APPAF_CODE_PLUMITIF = ce code,
///     APPAF_ETAT_RETOUR = « P » si le code est PRONONCE, « M » sinon (:2240-2248) ;
///   - si le code porte un evenement (R_CODEPLUMIT_CODE_EVE_PRO), un evenement de procedure SURVENU sur l'instance, date de
///     l'audience (:2253-2270 ; EvenementProcedure.CreateEvenementProcedure, EvenementProcedure.cs:1721-1757 : type du code, numero
///     source GUID, piece numerisee si le code la demande pour une instance) ; son rang = le nombre d'evenements de l'instance
///     (Instance.AddEvenementProcedure, Instance.cs:594-596).
///
/// « Renvoyer » -- le resultat d'un renvoi saisi en masse sur les affaires cochees (PROC_MJUDS, le meme resultat pour toutes ;
/// OPE_RESULTAT.cs:448-700) : « Résultat » = un code plumitif de RENVOI (R_CODEPLUMIT_RENVOI = 1, :478), utilisable en saisie et
/// compatible avec chaque instance (PROC_MJUDS.cs:600-625) ; APPAF_ETAT_RETOUR comme ci-dessus (:609-616) ; puis le PROCHAIN APPEL
/// (:655-700) : si le code dit « avec audience » (R_CODEPLUMIT_AVEC_AUDIENCE = 1) ou « avec cabinet » (0), l'audience de la
/// « Prochaine audience » (date), de la chambre du code (« = » : celle de l'audience en cours), recoit un appel d'affaire neuf :
/// type de classement du code, etat de retour « A », numero d'ordre = nombre d'appels de l'instance + 1, composition de
/// l'audience si elle n'en a qu'une (AppelAffaire.CreateAppelAffaire, AppelAffaire.cs:675-702). Puis la DECISION JUDICIAIRE
/// (ApplyModifications multi, PROC_MJUDS.cs:1186-1193) : RIG v2 n'ouvre l'etape decision que pour un code prononce, delibere ou
/// delibere repousse (displayDJ, OPE_RESULTAT.cs:472-473) -- un renvoi simple la masque et la vide (:488-497) et aucune decision ne
/// s'ecrit (GetDecisionJudiciaireSaisie, PROC_MJUDS.cs:1322-1330 : etape visible ET type saisi). Pour un code qui l'ouvre, un
/// « Type de décision » de la liste de saisie (la meme operation que la mise au point des chambres, OPE_DECISION_JUDICIAIRE_MJUDS)
/// cree, sur chaque instance dont l'appel n'a pas deja une decision deliberee (:1189), une DECISION_JUDICIAIRE neuve avec sa
/// « Date prévue » : AffectDecisionJudiciaireDirect (PROC_MJUDS.cs:1587-1593), ecrite par EcrivainMiseAuPointChambres.CreerDecisionAsync,
/// avec la MESURE du type lie au type de decision quand il en porte un (AffectDecisionJudiciaire et GetMesureSaisie,
/// PROC_MJUDS.cs:1499-1509, :1365-1373 ; R_TYPEDECISI_TYPE_MESURE_LIE, OPE_DECISION_JUDICIAIRE_MJUDS.cs:487-494), et son EDITION
/// (GenererLesDemandesEdition, PROC_MJUDS.cs:1260-1271 ; DecisionJudiciaire.CreateDemandeEdition, DecisionJudiciaire.cs:1656-1678) :
/// le reglage d'edition du type, ecrit par EcrivainMiseAuPointChambres.EditionDecisionAsync (son en-tete : L'EDITION DE LA DECISION,
/// meme liste de destinataires), sans code-barre (aucune case a l'ecran ; cachee en lot, OPE_DECISION_JUDICIAIRE_MJUDS.cs:629) ; un type sans reglage, ou
/// dont le reglage manque, n'en a pas. Elle se rattache a la demande MJUDS de l'instance et passe avant les editions des evenements
/// (:1272-1281). La decision n'a pas de juge (non saisi a l'ecran) : la regle de signature dematerialisee qui coupe l'impression
/// (JUG_SIGNATURE_DEMAT_TYPE_INSTANCES, :1265-1267) ne s'applique pas.
/// Mesure du 26/11/2026 (SQL-DEV\DEV, RIG_DEV) : aucun des 250 codes de renvoi n'y est prononce ni delibere, l'etape n'y est jamais ouverte.
/// L'EVENEMENT DU CODE au renvoi : PROC_MJUDS cree l'evenement survenu du code a la date de l'audience (OPE_RESULTAT.cs:857-908) et,
/// par lui, son evenement FUTUR lie (OPE_EVENEMENT.cs:1587-1608), puis les copie sur chaque instance (AffectEvenementProcedure,
/// PROC_MJUDS.cs:1210, :1557-1576) : les deux sont ecrits par EvenementsProcedureV2.SurvenuAsync (calcul des dates en tete de
/// EvenementsProcedureV2.cs). Mesure du 26/11/2026 (RIG_DEV) : 246 des 250 codes de renvoi portent un evenement, aucun de ces evenements n'a de code
/// futur -- sur la copie de dev, un renvoi n'ecrit donc que le survenu ; le futur se prouve au banc (PROC_RETAUD.gestes).
/// LA DEMANDE (« Renvoyer » seulement) : PROC_MJUDS cree une demande MJUDS par instance a son ouverture et la valide a la sauvegarde
/// (LockInstances, PROC_MJUDS.cs:986-1004, :760) ; les editions s'y rattachent (DemandeAsync). « Marquer » n'en cree pas.
/// LA FACTURE ET LES EDITIONS de chaque evenement ecrit, le survenu puis son futur (PROC_MJUDS.cs:744-747, :1273-1281) : un article a
/// facturer refuse le renvoi avec son nom (EvenementsProcedureV2.FactureNonRepriseAsync : la V3 n'a pas de moteur de facturation) ;
/// les editions se calculent par EditionsEvenementV2 (maquettes CEP_EDITION du code, destinataires, source « EP n° <rang> »), sans
/// groupage automatique (:1278), et vont dans la file des editions, instance par instance dans l'ordre des roles (:1259). Une branche
/// que EditionsEvenementV2 ne calcule pas refuse le renvoi avec son nom -- d'abord les destinataires PARTIES et AVOCATS de l'instance
/// (D, F, I, AVx, POx, PLx : RecupDestEditLot2FROMInstance, DestinatairesEditionsLot2.cs:1232-1519), et les envois LRAR. Rien n'est
/// enregistre a moitie : un renvoi sans ses convocations ne s'ecrit pas. Mesure du 27/11/2026 (RIG_DEV) : 180 des 250 codes de renvoi
/// utilisables en saisie portent des editions, presque toutes vers les parties ou les avocats (AVF : 86 codes, AVD;AVF;AVI : 33,
/// D;POD;F;POF : 22 dont RCAB, D,LRAR;POD;F,LRAR;POF : 11 ; aucun n'a que des destinataires repris) -- ceux-la se renvoient encore
/// dans RIG v2 ; les 70 autres n'ont ni edition ni article : la V3 les renvoie.
/// LE VERROU (« Renvoyer » seulement) : PROC_MJUDS verrouille chaque instance a son ouverture (PROC_MJUDS.cs:210, LockInstances
/// :959-1034) ; une instance tenue par une autre demande est refusee aux messages de RIG v2 (:1014-1027), ceux de la mise au point
/// des chambres (EcrivainMiseAuPointChambres.RefusVerrouAsync). « Marquer » ecrit sans PROC_MJUDS, depuis la grille
/// (OPE_LIST_APPEL_AFFAIRE.cs:2217-2279) : RIG v2 n'y verrouille rien.
/// L'APPEL A VENIR : le prochain appel ne s'ajoute qu'a une instance qui n'a pas deja un appel a une audience a venir
/// (AUDNC_DATE > aujourd'hui, PROC_MJUDS.cs:1176-1182) ; les autres recoivent le resultat, sans nouvel appel.
///
/// CONTROLE COMMUN (CanDoActions, OPE_LIST_APPEL_AFFAIRE.cs:1356-1369) : sur une audience passee (IsAudiencePassee, :1343-1349),
/// « actions possibles uniquement si l'audience comporte des compositions » (:1586), quand sa chambre l'impose
/// (R_CHAMBRE_AVEC_COMPO).
///
/// CE QUE LA V3 NE REPREND PAS (nomme, rien en silence, avec la raison) :
///   - l'arrivee et la ligne du rapport general de la demande MJUDS (Demande.cs:480, :512) : la V3 ne les ecrit pour aucune demande
///     (CreationV2.CreerDemandeAsync) ; ni le sequenceur « 2W » que portent les demandes MJUDS des instances d'injonction de RIG_DEV,
///     absent du code de PROC_MJUDS (seuls 2J, 2P et 2R, :995-1002) ;
///   - pour « Marquer », l'evenement FUTUR lie a l'evenement du code : RIG v2 le calcule en memoire (AddEvenementProcedureSurvenu,
///     IEntiteJudiciaire.cs:132-151, :144) mais ne l'enregistre pas -- seuls l'appel et l'evenement survenu sont sauves
///     (OPE_LIST_APPEL_AFFAIRE.cs:2254, :2263-2268), et EvenementProcedure.Sauve (:2218-) ne sauve pas les evenements lies. Le texte
///     de l'evenement (ConstructionTexte) non plus (EvenementsProcedureV2.cs) ;
///   - le CALENDRIER de procedure (PROC_MJUDS.cs:1163-1174 ; AffectCalendrier, :1461-1497) : RIG v2 recopie la LISTE des etapes que
///     l'agent a saisies (GetEtapesCalendrierSaisies, :1313-), une operation par etape. Un ecran genere ne saisit pas de sous-liste
///     (web/src/ecrans/formes/commun.tsx:374) : sans cette saisie, il n'y a pas de calendrier a ecrire ;
///   - le choix d'une maquette pour un type de decision sans reglage d'edition (GetCodeMaquetteSaisi, PROC_MJUDS.cs:1343-1352) : sans
///     champ a l'ecran, aucune edition, comme RIG v2 quand rien n'est choisi (EcrivainMiseAuPointChambres, meme limite) ;
///   - la reponse RPVA aux demandes de renvoi (OPE_RESULTAT.cs:627-650) : service externe ; le controle des deliberes repousses et
///     son courriel a la hotline (:571-603) : courriel externe.
/// </summary>
internal sealed class EcrivainRetourAudience : IEcrivainEcran
{
    public string Procedure => "PROC_RETAUD";

    public string Ecran => "Retour d'audience";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Decider];

    /// <summary>Les gestes du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string MarquerRendues = "Marquer les décisions comme rendues", Renvoyer = "Renvoyer";

    /// <summary>Les parametres du descripteur que « Renvoyer » lit.</summary>
    internal const string ParametreResultat = "Résultat", ParametreProchaine = "Prochaine audience", ParametreType = "Type de décision",
                          ParametreDatePrevue = "Date prévue";

    /// <summary>Une affaire au role, telle que les gestes la lisent.</summary>
    private sealed record Appel(int Id, int IdInstance, string Role, string TypeInstance, string? CodePlumitif, string EtatRetour, bool Prononce,
                                int IdAudience, DateTime? DateAudience, string? Heure, string? Chambre, bool AvecCompo, int Compositions);

    /// <summary>Un code plumitif et ce que les gestes en lisent.</summary>
    /// <param name="OuvreDecision">Prononce, delibere ou delibere repousse : RIG v2 ouvre l'etape decision (OPE_RESULTAT.cs:472-473).</param>
    internal sealed record CodePlumitif(string Code, bool Prononce, bool Renvoi, bool OuvreDecision, string? CodeEvePro, string? AvecAudience,
                                       string? TypeClassement, string? Chambre, HashSet<string> Compatibles);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var d = geste.Demande;
        if (d.Ids.Length == 0) return IssueGeste.Refuser("Cochez au moins une affaire du rôle.");
        var appels = new List<Appel>();
        foreach (var id in d.Ids)
        {
            if (await LireAppelAsync(geste, id, ct) is not { } a) return IssueGeste.Refuser($"L'affaire « {id} » n'est pas au rôle d'une audience de la base du greffe.");
            appels.Add(a);
        }
        // CanDoActions (OPE_LIST_APPEL_AFFAIRE.cs:1356-1369, :1586).
        foreach (var a in appels.Where(a => Passee(a, geste.Maintenant) && a.AvecCompo && a.Compositions == 0).Take(1))
            return IssueGeste.Refuser($"Audience passée sans composition (rôle {a.Role}) : actions possibles uniquement si l'audience comporte des compositions.");
        return d.Nom switch
        {
            MarquerRendues => await MarquerAsync(geste, appels, ct),
            Renvoyer => await RenvoyerAsync(geste, appels, ct),
            _ => IssueGeste.Refuser($"Le geste « {d.Nom} » n'existe pas au retour d'audience de RIG v2."),
        };
    }

    // ------------------------------------------------------------------ MARQUER LES DECISIONS COMME RENDUES

    private static async Task<IssueGeste> MarquerAsync(GesteEnCours geste, List<Appel> appels, CancellationToken ct)
    {
        if (appels.FirstOrDefault(a => a.CodePlumitif is not null || !a.Prononce) is { } horsMenu)
            return IssueGeste.Refuser($"« {MarquerRendues} » ne s'applique qu'aux prononcés non encore traités : le rôle {horsMenu.Role} "
                                      + (horsMenu.CodePlumitif is not null ? $"a déjà le résultat {horsMenu.CodePlumitif}." : "n'est pas un prononcé."));
        var j = await LireCodeAsync(geste, "J", ct);
        var o = await LireCodeAsync(geste, "O", ct);
        if (j is null || o is null) return IssueGeste.Refuser("Les codes plumitif « J » (jugement rendu) et « O » (ordonnance rendue) ne sont pas dans la base du greffe.");
        var traces = new List<TraceGeste>();
        var ids = new List<string>();
        var ignores = new List<string>();
        foreach (var a in appels)
        {
            var code = j.Compatibles.Contains(Traduit(a.TypeInstance)) ? j : o.Compatibles.Contains(Traduit(a.TypeInstance)) ? o : null;
            if (code is null) { ignores.Add(a.Role); continue; }
            traces.Add((await PoserResultatAsync(geste, a, code, avecFutur: false, ct)).Trace);
            ids.Add(a.Id.ToString(CultureInfo.InvariantCulture));
        }
        if (traces.Count == 0)
            return IssueGeste.Refuser($"Ni « J » ni « O » n'est compatible avec le type d'instance de : {string.Join(", ", ignores)}.");
        var message = $"{traces.Count} décision{(traces.Count > 1 ? "s" : "")} marquée{(traces.Count > 1 ? "s" : "")} comme rendue{(traces.Count > 1 ? "s" : "")}"
                      + (ignores.Count > 0 ? $" ; sans code compatible, laissée{(ignores.Count > 1 ? "s" : "")} à traiter : {string.Join(", ", ignores)}." : ".");
        return IssueGeste.Ecrit(message, [.. ids], [.. traces]);
    }

    // ------------------------------------------------------------------ RENVOYER

    private static async Task<IssueGeste> RenvoyerAsync(GesteEnCours geste, List<Appel> appels, CancellationToken ct)
    {
        if (appels.FirstOrDefault(a => a.CodePlumitif is not null) is { } traite)
            return IssueGeste.Refuser($"Le rôle {traite.Role} a déjà le résultat {traite.CodePlumitif} : seules les affaires non traitées se renvoient.");
        // Le verrou des instances a l'ouverture de PROC_MJUDS (PROC_MJUDS.cs:210, :959-1034), avant toute saisie.
        if (await EcrivainMiseAuPointChambres.RefusVerrouAsync(geste, appels.Select(a => a.IdInstance), ct) is { } verrou) return IssueGeste.Refuser(verrou);
        var saisi =geste.Demande.Valeurs.TryGetValue(ParametreResultat, out var r) ? r.Trim() : "";
        if (saisi.Length == 0) return IssueGeste.Refuser($"« {ParametreResultat} » est obligatoire : le code plumitif du renvoi.");
        if (await LireCodeAsync(geste, saisi, ct, saisieSeulement: true) is not { } code)
            return IssueGeste.Refuser($"Le code plumitif « {saisi} » n'est pas utilisable en saisie dans la base du greffe.");
        if (!code.Renvoi) return IssueGeste.Refuser($"Le code plumitif « {code.Code} » n'est pas un renvoi.");
        if (appels.FirstOrDefault(a => !code.Compatibles.Contains(Traduit(a.TypeInstance))) is { } incompatible)
            return IssueGeste.Refuser($"Le code plumitif « {code.Code} » n'est pas compatible avec le type d'instance du rôle {incompatible.Role}.");

        // La decision judiciaire (voir l'en-tete) : l'etape decision de RIG v2 n'existe que pour un code qui l'ouvre.
        // fix-ok: controle du lot 7 (26/11/2026) -- « Renvoyer » n'ecrivait jamais la decision que PROC_MJUDS ecrit en lot
        // (PROC_MJUDS.cs:1186-1193, :1322-1330) quand le code ouvre l'etape decision (OPE_RESULTAT.cs:472-473) ; une seule cause,
        // portee par plusieurs remplacements (en-tete, parametres, code plumitif, ecriture).
        // fix-ok: second controle du lot 7 (26/11/2026) -- « Renvoyer » sautait deux controles de PROC_MJUDS lus dans le code V2 : le
        // verrou des instances (:210, :959-1034) et l'appel deja a venir (:1176-1182) ; et la mesure liee au type de decision
        // (:1499-1509) ; une cause par remplacement (verrou, boucle d'ecriture, aides partagees avec la mise au point des chambres).
        // fix-ok: relance du lot 7 (27/11/2026) -- « Renvoyer » n'ecrivait pas l'evenement FUTUR lie que PROC_MJUDS copie avec le
        // survenu (PROC_MJUDS.cs:1557-1576) ; mesure : PROC_RETAUD.gestes, blocs R17E et R17D rouges (3 echecs) avec avecFutur: false,
        // verts avec avecFutur: true. Calcul ecrit une fois dans EvenementsProcedureV2.cs (en-tete, appel, trace).
        var saisiType = geste.Demande.Valeurs.TryGetValue(ParametreType, out var t) ? t.Trim() : "";
        var saisiDatePrevue = geste.Demande.Valeurs.TryGetValue(ParametreDatePrevue, out var dp) ? dp.Trim() : "";
        DateTime? datePrevue = null;
        string? mesureLiee = null;
        if (saisiType.Length > 0)
        {
            if (!code.OuvreDecision)
                return IssueGeste.Refuser($"Le code plumitif « {code.Code} » n'ouvre pas la décision judiciaire : RIG v2 ne la saisit au renvoi "
                                          + "que pour un code prononcé ou délibéré.");
            if (!await EcrivainMiseAuPointChambres.TypeDecisionSaisissableAsync(geste, saisiType, ct))
                return IssueGeste.Refuser($"Le type de décision « {saisiType} » n'est pas utilisable en saisie dans la base du greffe.");
            if (saisiDatePrevue.Length > 0 && (datePrevue = AudiencesV2.Date(saisiDatePrevue)) is null)
                return IssueGeste.Refuser($"« {ParametreDatePrevue} » n'est pas une date : « {saisiDatePrevue} ».");
            mesureLiee = await EcrivainMiseAuPointChambres.MesureLieeAsync(geste, saisiType, ct);
        }
        else if (saisiDatePrevue.Length > 0)
            return IssueGeste.Refuser($"« {ParametreDatePrevue} » se rapporte à la décision : saisissez aussi le « {ParametreType} ».");

        // Le prochain appel (OPE_RESULTAT.cs:655-700) : seulement pour un code « avec audience » (1) ou « avec cabinet » (0).
        int? idProchaine = null;
        string? libelleProchaine = null;
        if (code.AvecAudience is "0" or "1")
        {
            var jour = AudiencesV2.Date(geste.Demande.Valeurs.TryGetValue(ParametreProchaine, out var p) ? p : null);
            if (jour is null) return IssueGeste.Refuser($"« {ParametreProchaine} » est obligatoire pour le code « {code.Code} » : la date de l'audience de renvoi.");
            if (code.TypeClassement is null) return IssueGeste.Refuser($"Le code plumitif « {code.Code} » ne porte pas de type de classement pour le prochain appel.");
            var chambre = code.Chambre == "=" ? appels[0].Chambre : code.Chambre;
            var trouvees = await AudiencesDuJourAsync(geste, jour.Value, code.AvecAudience == "1", chambre, ct);
            var genre = code.AvecAudience == "1" ? "audience" : "cabinet";
            if (trouvees.Count == 0)
                return IssueGeste.Refuser($"Aucun{(genre == "audience" ? "e" : "")} {genre} le {FormeLigne.Date(jour)}{(chambre is null ? "" : $" pour la chambre {chambre}")} dans la base du greffe.");
            if (trouvees.Count > 1)
                return IssueGeste.Refuser($"Plusieurs {genre}s le {FormeLigne.Date(jour)}{(chambre is null ? "" : $" pour la chambre {chambre}")} : la V3 ne sait pas encore choisir laquelle.");
            (idProchaine, libelleProchaine) = trouvees[0];
        }

        // fix-ok: troisieme controle du lot 7 (27/11/2026) -- « Renvoyer » n'ecrivait ni la demande de PROC_MJUDS ni les editions de
        // l'evenement du code (GenererLesDemandesEdition, PROC_MJUDS.cs:1273-1281) et ne le disait pas ; mesure : PROC_RETAUD.gestes,
        // blocs 27 a 30 rouges sans la demande, les editions et le refus de la facture, verts avec.
        // La demande de chaque instance, a l'ouverture de PROC_MJUDS (LockInstances, PROC_MJUDS.cs:986-1004), avant toute ecriture.
        var traces = new List<TraceGeste>();
        var demandes = new Dictionary<int, (int Id, string Numero, InstanceRenvoi Instance)>();
        foreach (var a in appels)
        {
            if (demandes.ContainsKey(a.IdInstance)) continue;
            var instance = await LireInstanceAsync(geste, a.IdInstance, ct);
            var (numero, refusNumero) = await CreationV2.NumeroAsync(geste, "DMD", geste.Maintenant.Year, ct);
            if (numero is null) return IssueGeste.Refuser(refusNumero!);
            demandes[a.IdInstance] = (await DemandeAsync(geste, CodeProcessus, a.IdInstance, a.Role, instance, numero, ct), numero, instance);
        }

        var ids = new List<string>();
        var decisions = 0;
        var dejaAppelees = new List<string>();
        // Rang : 0 l'edition d'une decision, 1 celle d'un evenement -- pour une instance, RIG v2 met les decisions avant les
        // evenements (GenererLesDemandesEdition, PROC_MJUDS.cs:1260-1270 puis :1272-1281).
        var editions = new List<(string Role, int Rang, int IdDemande, EditionsEvenementV2.Edition Edition)>();
        foreach (var a in appels)
        {
            var (trace, evenement) = await PoserResultatAsync(geste, a, code, avecFutur: true, ct);
            traces.Add(trace);
            ids.Add(a.Id.ToString(CultureInfo.InvariantCulture));
            // La facture puis les editions de chaque evenement ecrit, le survenu et son futur (EvenementProcedureAffected,
            // PROC_MJUDS.cs:1557-1576 ; DoGenererLesArticles :744-747, GenererLesDemandesEdition :1273-1281). Un refus annule tout.
            var demande = demandes[a.IdInstance];
            foreach (var eve in evenement?.Evenements ?? [])
            {
                if (await EvenementsProcedureV2.FactureNonRepriseAsync(geste, demande.Instance.Editions, eve.Code, "le renvoi", ct) is { } facture)
                    return IssueGeste.Refuser(facture);
                var calcul = await EditionsEvenementV2.CalculerAsync(geste, demande.Instance.Editions, eve, "le renvoi", ct);
                if (calcul.NonRepris is { } nonRepris) return IssueGeste.Refuser(nonRepris);
                // Sans groupage automatique, pour les editions des evenements seulement (PROC_MJUDS.cs:1278).
                foreach (var e in calcul.Editions) e.Colonnes["DMNED_GROUPAGE_AUTOMATIQUE"] = false;
                editions.AddRange(calcul.Editions.Select(e => (a.Role, 1, demande.Id, e)));
            }
            // Le prochain appel, sauf a une instance deja appelee a une audience a venir (PROC_MJUDS.cs:1176-1182).
            if (idProchaine is { } adnc)
            {
                if (await AppelAVenirAsync(geste, a.IdInstance, ct)) dejaAppelees.Add(a.Role);
                else traces.Add(await ProchainAppelAsync(geste, a, adnc, libelleProchaine!, code.TypeClassement!, ct));
            }
            // Pas de seconde decision sur un appel deja delibere (DecisionsJudiciairesDeliberés, PROC_MJUDS.cs:1189).
            if (saisiType.Length > 0 && await DelibereesAsync(geste, a.Id, ct) == 0)
            {
                var decision = await EcrivainMiseAuPointChambres.CreerDecisionAsync(geste, a.IdInstance, a.Role, saisiType, datePrevue, mesureLiee, ct);
                traces.AddRange(decision.Traces);
                decisions++;
                // L'edition de la decision (DecisionJudiciaire.CreateDemandeEdition, PROC_MJUDS.cs:1262-1270 : sans code-barre ni
                // maquette choisie en lot), rattachee a la demande MJUDS de l'instance ; la meme que la mise au point des chambres.
                if (await EcrivainMiseAuPointChambres.EditionDecisionAsync(geste, saisiType, decision.NumeroSource, ct) is { } edition)
                    editions.Add((a.Role, 0, demande.Id, edition));
            }
        }
        // Les editions dans la file, instance par instance dans l'ordre des roles (InstanceModifieurs.OrderBy(INSTN_NUM_ROLE),
        // PROC_MJUDS.cs:1259), la decision avant les evenements (:1260-1281).
        // fix-ok: contrôle du lot 7 (27/11/2026) -- « Renvoyer » créait la décision sans son édition (GenererLesDemandesEdition,
        // PROC_MJUDS.cs:1260-1271) ; mesure : PROC_RETAUD.gestes, bloc commenté 31 rouge (« 2 éditions », aucune DECI_JUDICIAIRE).
        var deposees = new Dictionary<int, List<int>>();
        foreach (var (_, _, idDemande, e) in editions.OrderBy(x => x.Role, StringComparer.Ordinal).ThenBy(x => x.Rang))
        {
            var depot = await EditionsEvenementV2.DeposerAsync(geste, idDemande, e, ct);
            if (!deposees.TryGetValue(idDemande, out var liste)) deposees[idDemande] = liste = [];
            liste.Add(depot.Id);
        }
        foreach (var (idInstance, demande) in demandes)
            traces.Add(new TraceGeste("affaire", $"la demande {demande.Numero} du rôle {demande.Instance.Editions.Role}",
                demande.Id.ToString(CultureInfo.InvariantCulture), "a cree", null,
                new { Demande = demande.Numero, Processus = CodeProcessus, Editions = deposees.GetValueOrDefault(demande.Id) ?? [] }));

        var n = appels.Count;
        var message = $"{n} affaire{(n > 1 ? "s" : "")} renvoyée{(n > 1 ? "s" : "")} ({code.Code})" + (libelleProchaine is null ? "" : $" à l'{libelleProchaine}");
        if (dejaAppelees.Count > 0)
            message += $" ; sans nouvel appel, déjà appelée{(dejaAppelees.Count > 1 ? "s" : "")} à une audience à venir : {string.Join(", ", dejaAppelees)}";
        if (saisiType.Length > 0)
            message += decisions == 0 ? " ; aucune décision ajoutée (déjà délibérées)"
                : $" ; {decisions} décision{(decisions > 1 ? "s" : "")} {saisiType} créée{(decisions > 1 ? "s" : "")}"
                  + (mesureLiee is null ? "" : $" avec sa mesure {mesureLiee}")
                  + (decisions < n ? $", {n - decisions} déjà délibérée{(n - decisions > 1 ? "s" : "")}" : "");
        if (editions.Count > 0)
            message += editions.Count == 1 ? " ; 1 édition placée dans la file des éditions" : $" ; {editions.Count} éditions placées dans la file des éditions";
        return IssueGeste.Ecrit(message + ".", [.. ids], [.. traces]);
    }

    /// <summary>Le code du processus du renvoi en masse (PROC_MJUDS.Designer.cs:242), celui de ses demandes.</summary>
    internal const string CodeProcessus = "MJUDS";

    /// <summary>L'instance telle que la demande et les editions du renvoi la lisent (et celles de la mise au point des chambres,
    /// EcrivainMiseAuPointChambres.cs).</summary>
    internal sealed record InstanceRenvoi(int? IdAffaire, string NomCourant, string RepGeneral, int? IdPc, int? IdPrevention,
                                         EditionsEvenementV2.Instance Editions);

    internal static async Task<InstanceRenvoi> LireInstanceAsync(GesteEnCours geste, int idInstance, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT INSTN_NUM_ROLE, INSTN_ID_AFFRE, INSTN_NOM_COURANT, INSTN_NUM_REP_GENERAL, INSTN_ID_PRCLL, INSTN_ID_PREVE, INSTN_NUMERO_SOURCE,
                   INSTN_NATURE_AFFAIRE, INSTN_NAT_AFF_PART, INSTN_CODE_ACTE_PROCE
            FROM v2.INSTANCE WHERE INSTN_ID_INSTN = @i
            """, ct, new SqlParameter("@i", idInstance));
        await r.ReadAsync(ct);
        string? Texte(string c) => FormeLigne.Texte(r, c) is { Length: > 0 } t ? t : null;
        int? Id(string c) => r[c] is DBNull ? null : Convert.ToInt32(r[c], CultureInfo.InvariantCulture);
        return new InstanceRenvoi(Id("INSTN_ID_AFFRE"), FormeLigne.Texte(r, "INSTN_NOM_COURANT"), FormeLigne.Texte(r, "INSTN_NUM_REP_GENERAL"),
            Id("INSTN_ID_PRCLL"), Id("INSTN_ID_PREVE"),
            new EditionsEvenementV2.Instance(idInstance, FormeLigne.Texte(r, "INSTN_NUM_ROLE"), Texte("INSTN_NUMERO_SOURCE"),
                                             Texte("INSTN_NATURE_AFFAIRE"), Texte("INSTN_NAT_AFF_PART"), Texte("INSTN_CODE_ACTE_PROCE")));
    }

    /// <summary>
    /// La demande de PROC_MJUDS pour une instance (LockInstances, PROC_MJUDS.cs:986-1004 ; Instance.CreateAndLockDemande ;
    /// Demande.CreateDemande(idAffaire, nom, indicatif, code), Demande.cs:476-535) : l'affaire de l'instance, son nom courant, son role
    /// en numero indicatif, « role repertoire » en numeros (string.Join(" ", ...), :993), au code MJUDS, numerotee dans la serie DMD,
    /// validee a la sauvegarde (Cloturer(Validee), :760) donc ecrite a l'etat « V » ; ses dossiers : 2J = l'instance, puis 2P = sa PC,
    /// sinon 2R = sa prevention (:995-1002). Lu dans RIG_DEV le 27/11/2026 : DMND_CODE_PROSS = 'MJUDS', etat V, DMND_ID_AFFRE pose ;
    /// 15 563 des 34 948 demandes MJUDS portent des editions. La mise au point des chambres cree la sienne de meme, au code MPCS
    /// (LockInstances, PROC_MPCS.cs:1019-1037 : CreateAndLockDemande, indicatif = role, numeros = role et repertoire, 2J puis 2P
    /// sinon 2R), pour y rattacher l'edition de la decision (EcrivainMiseAuPointChambres.cs).
    /// </summary>
    // fix-ok: contrôle du lot 7 (27/11/2026) -- « Valider la décision » de PROC_MPCS créait la décision sans son édition, faute
    // de demande MPCS où la rattacher (DecisionJudiciaire.CreateDemandeEdition(idDemande…), PROC_MPCS.cs:1337-1346) : cette demande
    // est la même que celle du renvoi, d'où le code processus et l'instance en paramètres ; le renvoi garde « MJUDS ».
    internal static async Task<int> DemandeAsync(GesteEnCours geste, string codeProcessus, int idInstance, string role, InstanceRenvoi instance,
                                                 string numero, CancellationToken ct)
    {
        var id = await CreationV2.InsererAsync(geste, "v2.DEMANDE", "DMND_ID_DMND", new Dictionary<string, object?>
        {
            ["DMND_NUM_DEMANDE"] = numero, ["DMND_ETAT_DEMANDE"] = "V", ["DMND_CODE_PROSS"] = codeProcessus, ["DMND_DATE_CREATION"] = geste.Maintenant,
            ["DMND_EN_COURS"] = false, ["DMND_NOM_UTILISATEUR"] = geste.Agent.NomAffiche, ["DMND_NOM"] = instance.NomCourant,
            ["DMND_NUM_INDICATIF"] = role, ["DMND_LES_NUMEROS"] = string.Join(" ", role, instance.RepGeneral),
            ["DMND_ID_AFFRE"] = instance.IdAffaire,
        }, ct);
        var dossiers = new List<(string Sequenceur, int Valeur)> { (VerrouDemandeV2.SequenceurInstance, idInstance) };
        if (instance.IdPc is { } pc) dossiers.Add(("2P", pc));
        else if (instance.IdPrevention is { } prevention) dossiers.Add(("2R", prevention));
        foreach (var (sequenceur, valeur) in dossiers)
            await geste.InsererAsync("v2.DOSSIER_DEMANDE", "DSSDM_ID_DSSDM", new Dictionary<string, object?>
            {
                ["DSSDM_ID_DMND"] = id, ["DSSDM_CODE_SEQUENCEUR"] = sequenceur,
                ["DSSDM_VALEUR_SEQUENCEUR"] = valeur.ToString(CultureInfo.InvariantCulture),
            }, ct);
        return id;
    }

    /// <summary>Les decisions deja deliberees de l'appel (AppelAffaire.DecisionsJudiciairesDeliberés, AppelAffaire.cs:315-322 :
    /// DCJDC_ID_APPAF_DELIBERER = l'appel).</summary>
    private static async Task<int> DelibereesAsync(GesteEnCours geste, int idAppel, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.DECISION_JUDICIAIRE WHERE DCJDC_ID_APPAF_DELIBERER = @a", ct,
                                                  new SqlParameter("@a", idAppel));
        await r.ReadAsync(ct);
        return r.GetInt32(0);
    }

    /// <summary>L'instance a-t-elle deja un appel a une audience a venir (« AUDNC_DATE &gt; DateTime.Today », PROC_MJUDS.cs:1180 et
    /// PROC_MPCS.cs:1234) ? Le jour vient du geste (<see cref="GesteEnCours.Maintenant"/>), jamais de GETDATE().</summary>
    internal static async Task<bool> AppelAVenirAsync(GesteEnCours geste, int idInstance, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT COUNT(*) FROM v2.APPEL_AFFAIRE AS aa JOIN v2.AUDIENCE_CABINET AS a ON a.AUDNC_ID_ADNC = aa.APPAF_ID_ADNC
            WHERE aa.APPAF_ID_INSTN = @i AND a.AUDNC_DATE > @aujourdhui
            """, ct, new SqlParameter("@i", idInstance), new SqlParameter("@aujourdhui", geste.Maintenant.Date));
        await r.ReadAsync(ct);
        return r.GetInt32(0) > 0;
    }

    /// <summary>Les audiences (ou cabinets) du jour, de la chambre donnee s'il y en a une : id et libelle.</summary>
    internal static async Task<List<(int Id, string Libelle)>> AudiencesDuJourAsync(GesteEnCours geste, DateTime jour, bool audience, string? chambre,
                                                                                   CancellationToken ct)
    {
        var trouvees = new List<(int, string)>();
        await using var r = await geste.LireAsync($$"""
            SELECT a.AUDNC_ID_ADNC AS Id, {{AudiencesV2.Libelle}} AS Libelle
            FROM v2.AUDIENCE_CABINET AS a {{AudiencesV2.Jointures}}
            WHERE a.AUDNC_DATE >= @jour AND a.AUDNC_DATE < DATEADD(day, 1, @jour) AND a.AUDNC_AUDIENCE = @audience
              AND (@chambre IS NULL OR a.AUDNC_CHAMBRE = @chambre)
            ORDER BY a.AUDNC_HEURE, a.AUDNC_ID_ADNC
            """, ct, new SqlParameter("@jour", jour), new SqlParameter("@audience", audience),
            new SqlParameter("@chambre", (object?)chambre ?? DBNull.Value));
        while (await r.ReadAsync(ct)) trouvees.Add((Convert.ToInt32(r["Id"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "Libelle")));
        return trouvees;
    }

    /// <summary>Ajoute l'appel d'affaire du renvoi (AppelAffaire.CreateAppelAffaire, AppelAffaire.cs:675-702).</summary>
    private static async Task<TraceGeste> ProchainAppelAsync(GesteEnCours geste, Appel a, int idAudience, string libelle, string typeClassement,
                                                             CancellationToken ct)
    {
        int ordre;
        int? composition = null;
        await using (var r = await geste.LireAsync("""
            SELECT (SELECT COUNT(*) FROM v2.APPEL_AFFAIRE WHERE APPAF_ID_INSTN = @instance) AS Appels,
                   (SELECT COUNT(*) FROM v2.COMPOSITION WHERE CMPST_ID_ADNC = @audience) AS Compositions,
                   (SELECT MIN(CMPST_ID_CMAUD) FROM v2.COMPOSITION WHERE CMPST_ID_ADNC = @audience) AS Composition
            """, ct, new SqlParameter("@instance", a.IdInstance), new SqlParameter("@audience", idAudience)))
        {
            await r.ReadAsync(ct);
            ordre = Convert.ToInt32(r["Appels"], CultureInfo.InvariantCulture) + 1;
            if (Convert.ToInt32(r["Compositions"], CultureInfo.InvariantCulture) == 1) composition = Convert.ToInt32(r["Composition"], CultureInfo.InvariantCulture);
        }
        return await InsererAppelAsync(geste, a.IdInstance, a.Role, ordre, idAudience, composition, typeClassement, libelle, ct);
    }

    /// <summary>Ecrit un appel d'affaire neuf a venir, etat de retour « A » : celui du renvoi (AppelAffaire.CreateAppelAffaire) et
    /// celui de la mise au point des chambres (PROC_MPCS.cs:285-290), chacun avec son numero d'ordre et sa composition.</summary>
    internal static async Task<TraceGeste> InsererAppelAsync(GesteEnCours geste, int idInstance, string role, int ordre, int idAudience,
                                                             int? composition, string typeClassement, string libelle, CancellationToken ct)
    {
        var id = await geste.InsererAsync("v2.APPEL_AFFAIRE", "APPAF_ID_APPAF", new Dictionary<string, object?>
        {
            ["APPAF_ID_INSTN"] = idInstance, ["APPAF_NUM_ORDRE"] = (short)ordre, ["APPAF_ID_ADNC"] = idAudience, ["APPAF_ID_CMAUD"] = composition,
            ["APPAF_TYPE_CLASSEMENT"] = typeClassement, ["APPAF_ETAT_RETOUR"] = "A",
            ["APPAF_ETAT_PREPARATION"] = "0",   // DEFAULT ((0)) de RIG_DEV (APPEL_AFFAIRE.sql), que la copie de dev ne porte pas
        }, ct);
        return new TraceGeste("affaire", $"l'appel du rôle {role} à l'{libelle}", id.ToString(CultureInfo.InvariantCulture), "a cree", null,
                              new { Instance = role, Audience = libelle, TypeClassement = typeClassement, NumOrdre = ordre });
    }

    // ------------------------------------------------------------------ LE RESULTAT D'UN APPEL

    /// <summary>Pose le resultat <paramref name="code"/> sur l'appel (code plumitif, etat de retour) et l'evenement du code, date de
    /// l'audience ; avec <paramref name="avecFutur"/> (le renvoi, PROC_MJUDS), aussi son evenement futur lie (EvenementsProcedureV2).</summary>
    private static async Task<(TraceGeste Trace, EvenementsProcedureV2.Ecrits? Evenement)> PoserResultatAsync(GesteEnCours geste, Appel a,
        CodePlumitif code, bool avecFutur, CancellationToken ct)
    {
        var etat = code.Prononce ? "P" : "M";
        await geste.EcrireAsync("UPDATE v2.APPEL_AFFAIRE SET APPAF_CODE_PLUMITIF = @code, APPAF_ETAT_RETOUR = @etat WHERE APPAF_ID_APPAF = @id", ct,
            new SqlParameter("@code", code.Code), new SqlParameter("@etat", etat), new SqlParameter("@id", a.Id));
        EvenementsProcedureV2.Ecrits? evenement = null;
        if (code.CodeEvePro is { } eve) evenement = await EvenementsProcedureV2.SurvenuAsync(geste, a.IdInstance, eve, a.DateAudience?.Date, avecFutur, ct);
        return (new TraceGeste("affaire", $"l'appel du rôle {a.Role}", a.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
            new { Resultat = a.CodePlumitif, EtatRetour = a.EtatRetour },
            new { Resultat = code.Code, EtatRetour = etat, Evenement = code.CodeEvePro, IdEvenement = evenement?.IdSurvenu,
                  EvenementFutur = evenement?.CodeFutur, IdEvenementFutur = evenement?.IdFutur }), evenement);
    }

    // ------------------------------------------------------------------ LECTURES

    private static async Task<Appel?> LireAppelAsync(GesteEnCours geste, string id, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        await using var r = await geste.LireAsync("""
            SELECT aa.APPAF_ID_APPAF, aa.APPAF_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_TYPE_INSTANCE, aa.APPAF_CODE_PLUMITIF, aa.APPAF_ETAT_RETOUR,
                   CONVERT(bit, ISNULL(tc.R_TYPECLASSE_PRONONCE, 0)) AS Prononce, a.AUDNC_ID_ADNC, a.AUDNC_DATE, a.AUDNC_HEURE, a.AUDNC_CHAMBRE,
                   CONVERT(bit, ISNULL(ch.R_CHAMBRE_AVEC_COMPO, 0)) AS AvecCompo,
                   (SELECT COUNT(*) FROM v2.COMPOSITION AS c WHERE c.CMPST_ID_ADNC = a.AUDNC_ID_ADNC) AS Compositions
            FROM v2.APPEL_AFFAIRE AS aa WITH (UPDLOCK, HOLDLOCK)
            JOIN v2.AUDIENCE_CABINET AS a ON a.AUDNC_ID_ADNC = aa.APPAF_ID_ADNC
            JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = aa.APPAF_ID_INSTN
            LEFT JOIN v2.TYPE_CLASSEMENT AS tc ON tc.R_TYPECLASSE_VALEUR = aa.APPAF_TYPE_CLASSEMENT
            LEFT JOIN v2.CHAMBRE AS ch ON ch.R_CHAMBRE_VALEUR = a.AUDNC_CHAMBRE
            WHERE aa.APPAF_ID_APPAF = @id
            """, ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        string? Opt(string c) => FormeLigne.Texte(r, c) is { Length: > 0 } s ? s : null;
        return new Appel(n, Convert.ToInt32(r["APPAF_ID_INSTN"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "INSTN_NUM_ROLE"),
            FormeLigne.Texte(r, "INSTN_TYPE_INSTANCE"), Opt("APPAF_CODE_PLUMITIF"), FormeLigne.Texte(r, "APPAF_ETAT_RETOUR"), r["Prononce"] is true,
            Convert.ToInt32(r["AUDNC_ID_ADNC"], CultureInfo.InvariantCulture), FormeLigne.DateDe(r, "AUDNC_DATE"), Opt("AUDNC_HEURE"), Opt("AUDNC_CHAMBRE"),
            r["AvecCompo"] is true, Convert.ToInt32(r["Compositions"], CultureInfo.InvariantCulture));
    }

    internal static async Task<CodePlumitif?> LireCodeAsync(GesteEnCours geste, string code, CancellationToken ct, bool saisieSeulement = false)
    {
        await using var r = await geste.LireAsync("""
            SELECT R_CODEPLUMIT_VALEUR, R_CODEPLUMIT_PRONONCE, R_CODEPLUMIT_RENVOI, R_CODEPLUMIT_DELIBERE, R_CODEPLUMIT_DELIBERE_REPOUSSE,
                   R_CODEPLUMIT_CODE_EVE_PRO, R_CODEPLUMIT_AVEC_AUDIENCE,
                   R_CODEPLUMIT_TYPE_CLASSEMENT, R_CODEPLUMIT_CHAMBRE, R_CODEPLUMIT_UTILISABLE_SAISIE,
                   R_CODEPLUMIT_COMPATIBLE_CX, R_CODEPLUMIT_COMPATIBLE_PC, R_CODEPLUMIT_COMPATIBLE_REF, R_CODEPLUMIT_COMPATIBLE_JC,
                   R_CODEPLUMIT_COMPATIBLE_PRV, R_CODEPLUMIT_COMPATIBLE_OP
            FROM v2.CODE_PLUMITIF WHERE R_CODEPLUMIT_VALEUR = @c
            """, ct, new SqlParameter("@c", code));
        if (!await r.ReadAsync(ct)) return null;
        if (saisieSeulement && r["R_CODEPLUMIT_UTILISABLE_SAISIE"] is not true) return null;
        string? Opt(string c) => FormeLigne.Texte(r, c) is { Length: > 0 } s ? s : null;
        // EstCompatibleInstance (CODE_PLUMITIF.cs:76-97) : IP n'est jamais compatible, un type inconnu non plus.
        var compatibles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (type, colonne) in new[] { ("CX", "R_CODEPLUMIT_COMPATIBLE_CX"), ("PC", "R_CODEPLUMIT_COMPATIBLE_PC"), ("REF", "R_CODEPLUMIT_COMPATIBLE_REF"),
                                                ("JC", "R_CODEPLUMIT_COMPATIBLE_JC"), ("PRV", "R_CODEPLUMIT_COMPATIBLE_PRV"), ("OP", "R_CODEPLUMIT_COMPATIBLE_OP") })
            if (r[colonne] is true) compatibles.Add(type);
        var prononce = r["R_CODEPLUMIT_PRONONCE"] is true;
        return new CodePlumitif(FormeLigne.Texte(r, "R_CODEPLUMIT_VALEUR"), prononce, r["R_CODEPLUMIT_RENVOI"] is true,
            prononce || r["R_CODEPLUMIT_DELIBERE"] is true || r["R_CODEPLUMIT_DELIBERE_REPOUSSE"] is true, Opt("R_CODEPLUMIT_CODE_EVE_PRO"), Opt("R_CODEPLUMIT_AVEC_AUDIENCE"), Opt("R_CODEPLUMIT_TYPE_CLASSEMENT"), Opt("R_CODEPLUMIT_CHAMBRE"), compatibles);
    }

    /// <summary>Le type d'instance traduit (Instance.TypeInstanceTraduite, Instance.cs:967-983).</summary>
    internal static string Traduit(string typeInstance) => typeInstance switch
    {
        "07" => "CX",
        "08" => "PC",
        "09" => "REF",
        "PR" => "PRV",
        "RP" => "OP",
        _ => typeInstance,
    };

    /// <summary>L'audience est-elle passee (IsAudiencePassee, OPE_LIST_APPEL_AFFAIRE.cs:1343-1349 ; ModeAudienceCabinet,
    /// AudienceCabinet.cs:410-426) : sa date et son heure sont avant maintenant, ou elle est d'aujourd'hui ou d'avant sans heure.</summary>
    private static bool Passee(Appel a, DateTime maintenant) => Passee(a.DateAudience, a.Heure, maintenant);

    internal static bool Passee(DateTime? dateAudience, string? heureAudience, DateTime maintenant)
    {
        if (dateAudience is not { } jour) return false;
        if (heureAudience is null || !TimeSpan.TryParse(heureAudience, CultureInfo.InvariantCulture, out var heure)) return jour.Date <= maintenant.Date;
        return jour.Date.Add(heure) <= maintenant;
    }
}
