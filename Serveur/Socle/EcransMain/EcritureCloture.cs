using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

// fix-ok: pas un correctif -- lot 11 du plan-f4fb097f, refus du chef du 28/11/2026 (mesuré : la sonde ne jouait que le courrier, mode 5,
// secondaire) : ajout du geste principal, l'enrôlement sur saisine d'office (EnrolerAsync) ; retouches d'un même ajout (ids, message, en-tête).

/// <summary>
/// « Clôture » (PROC_CLOT, écran fait main web/src/ecrans/EcranCloture.tsx) : les gestes de l'« Examen des clôtures » de RIG v2 sur les
/// procédures collectives cochées de la grille « Sélection PC », joués par les routes de ModuleCloture.cs dans la transaction et le journal
/// des gestes du socle (GesteFaitMain) :
///   - le PRINCIPAL, l'ENRÔLEMENT SUR SAISINE D'OFFICE (mode 1, <see cref="EnrolerAsync"/>, route RoutesCloture.EnrolementSo) ;
///   - le second, le COURRIER AUX DÉBITEURS (mode 5, <see cref="JouerAsync"/>, route RoutesCloture.CourrierDebiteurs), décrit ci-dessous.
/// Lot 11 du plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026) ; la lecture : LectureCloture.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RIG\DLL\Processus\PROC_CLOT\PROC_CLOT) :
///   - le code de l'évènement : ParamCLOT_CodeEvePro du mode 5 = le réglage CLOT_EVE_PRO_INCITR (PROC_CLOT.cs:229-235 ; CLOTINCITR
///     « incitation requête en clôture » sur RIG_DEV, lu le 28/11/2026). Sans lui, RIG v2 valide sans RIEN écrire
///     (« if (codeEP != null) », 1-4-5 Selection PC\OPE_SELECT_PC.cs:1763-1764) : la V3 le refuse et le dit, plutôt qu'un faux « fait » ;
///   - les contrôles (OPE_SELECT_PC.Verifier, :1633-1736) : une PC verrouillée est DÉCOCHÉE avec l'avertissement « La PC n°… a été
///     décochée car verrouillée : … » (:1667-1683 ; VerifierLOCK, :877-905, ProcColl.GetLock : « Procédure collective bloquée … ») et les
///     autres sont traitées ; aucune PC à traiter : « Aucune procédure collective n'a été selectionnée pour les traitements
///     demandés. » (:1731-1735). Une PC cochée est une PC de la grille (LoadDGVProcedures, :441-454 : celles de LectureCloture) ;
///   - pour chaque PC (Traitements, :1761-1775) : sa demande « Courriers aux Débiteurs : » + son nom, indicatif le n° de PC, ses numéros,
///     processus « CLOT » (sMemoryTraitementsMASSEClotPC, PROC_CLOT.cs:726-730) ; RIG_DEV en garde deux, validées, sur une affaire, liées
///     à la PC (DOSSIER_DEMANDE 2P) -- lues le 28/11/2026 ; l'évènement SURVENU du code à la date du jour
///     (EvenementProcedure.CreateEvenementProcedure(PC, code, true, false, aujourd'hui) : le type du code, un numéro source GUID ;
///     AddEvenementProcedure renumérote les évènements de la PC, comme DepotsPcV2) ; la facture (GenererFacture, :2352-2365) ;
///   - les éditions (Editions, :1979-1990) : evePro.CreateDemandeEdition(demande) -- celles du code pour une PC (CEP_EDITION,
///     CEPED_TYPE_EP « P » : INCIT_RDEB_CLOT_PC sur RIG_DEV), dans la file des éditions (EditionsV2.DemandeEditionAsync) : entité source
///     EVT_PROC = le numéro source de l'évènement, la maquette du paramètre, processus EDIT_EP (les éditions de RIG_DEV, lues le 28/11/2026).
/// LA FACTURE : la V3 n'a pas de moteur de facturation. Le bouton FACTURATION enfoncé (<see cref="RoutesCloture.ChampFacturer"/> « oui »)
/// sur une PC hors forfait dont le code porte un article (CEP_ARTICLE, type P) est REFUSÉ (rien d'écrit) ; relâché, RIG v2 ne facture
/// pas (« if (!EtapeSelectionPC.MetierFacture) return; », :2353).
/// NON REPRIS (nommés) : une édition à condition (CEPED_CONDITION) est refusée ; les destinataires de l'édition (LISTE_ENTITE_DESTINATION,
/// les dirigeants « DIR » de CEP_EDITION), que le tableau des éditions de RIG v2 pose ; le second DOSSIER_DEMANDE des demandes de RIG_DEV ;
/// les modes de clôture « Liste préparatoire » (4, une édition sans écriture), « Enrôlement sur requête » et « Convocations à imprimer »
/// (2 et 3 : étapes 2-3 Enrolement Requete\OPE_ENROLEMENT_REQUETE.cs et OPE_RESULTATS.cs, une requête saisie PC par PC).
/// TRACE : famille « affaire », « a cree », une par PC.
/// </summary>
public static class EcritureCloture
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Clôture";

    /// <summary>Le code processus des demandes du courrier (PROC_CLOT.cs:728).</summary>
    public const string CodeProcessus = "CLOT";

    /// <summary>Le réglage du code de l'évènement du courrier aux débiteurs (PROC_CLOT.cs:229-235).</summary>
    public const string ReglageEvenement = "CLOT_EVE_PRO_INCITR";

    /// <summary>OPE_SELECT_PC.cs:1732-1733, au mot près.</summary>
    public const string AucuneSelection = "Aucune procédure collective n'a été selectionnée pour les traitements demandés.";

    /// <summary>Les gestes écrits : tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Agir];

    private sealed record Pc(int Id, string Numero, string Nom, bool AuForfait);

    /// <summary>Le courrier aux débiteurs sur les PC <c>Ids</c> du geste.</summary>
    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var ids = new List<int>();
        foreach (var texte in geste.Demande.Ids)
        {
            if (!int.TryParse(texte, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                return IssueGeste.Refuser($"« {texte} » n'est pas l'identifiant d'une procédure collective.");
            if (!ids.Contains(id)) ids.Add(id);
        }
        if (ids.Count == 0) return IssueGeste.Refuser(AucuneSelection);

        var code = await ReglageAsync(geste, ReglageEvenement, ct);
        if (code.Length == 0)
            return IssueGeste.Refuser($"Le courrier aux débiteurs n'est pas paramétré (réglage {ReglageEvenement}) : RIG v2 n'écrit alors rien. " +
                                      "Rien n'est enregistré.");
        string typeEvenement;
        await using (var r = await geste.LireAsync("SELECT R_CODEEVEPRO_TYPE_EVE_PRO FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c", ct,
                         new SqlParameter("@c", code)))
        {
            if (!await r.ReadAsync(ct))
                return IssueGeste.Refuser($"Le code d'évènement « {code} » du réglage {ReglageEvenement} n'existe pas dans la base du greffe : rien n'est enregistré.");
            typeEvenement = FormeLigne.Texte(r, "R_CODEEVEPRO_TYPE_EVE_PRO").Trim();
        }

        // Les éditions de l'évènement pour une PC (CEP_EDITION, type « P ») ; une condition n'est pas reprise.
        var editions = new List<string>();
        await using (var r = await geste.LireAsync("""
            SELECT CEPED_CODE_EDITION, ISNULL(CEPED_CONDITION, '') AS Condition FROM v2.CEP_EDITION
            WHERE CEPED_CODE_EVE_PRO = @c AND CEPED_TYPE_EP = 'P' ORDER BY CEPED_ID_CEPED
            """, ct, new SqlParameter("@c", code)))
            while (await r.ReadAsync(ct))
            {
                var condition = FormeLigne.Texte(r, "Condition").Trim();
                if (condition.Length > 0)
                    return IssueGeste.Refuser($"La condition « {condition} » de l'édition {FormeLigne.Texte(r, "CEPED_CODE_EDITION").Trim()} de l'évènement {code} " +
                                              "n'est pas reprise par RIG v3 : rien n'est enregistré, faire le courrier aux débiteurs dans RIG v2.");
                editions.Add(FormeLigne.Texte(r, "CEPED_CODE_EDITION").Trim());
            }

        // Les PC cochées : celles de la grille « Sélection PC » (LectureCloture), puis leur verrou (une PC verrouillée est décochée).
        var codes = new List<SqlParameter>();
        foreach (var (nom, reglage) in new[] { ("@code1", LectureCloture.CodeAffaireSo), ("@code2", LectureCloture.CodeAffaire),
                                              ("@code3", LectureCloture.CodeAffaireAv2006) })
            codes.Add(new SqlParameter(nom, System.Data.SqlDbType.VarChar, 40)
            {
                Value = await ReglageAsync(geste, reglage.Code, ct) is { Length: > 0 } c ? c : DBNull.Value,
            });
        var aTraiter = new List<Pc>();
        var decochees = new List<string>();
        foreach (var id in ids)
        {
            Pc? pc = null;
            await using (var r = await geste.LireAsync($"""
                SELECT p.PRCLL_ID_PRCLL, p.PRCLL_NUM_PC, p.PRCLL_NOM_AFFAIRE, p.PRCLL_FORFAIT_FACTURE FROM v2.PROCEDURE_COLLECTIVE AS p
                WHERE p.PRCLL_ID_PRCLL = @id AND {LectureCloture.AExaminer}
                """, ct, [new SqlParameter("@id", id), .. codes.Select(p => new SqlParameter(p.ParameterName, p.SqlDbType, p.Size) { Value = p.Value })]))
                if (await r.ReadAsync(ct))
                    pc = new Pc(id, FormeLigne.Texte(r, "PRCLL_NUM_PC").Trim(), FormeLigne.Texte(r, "PRCLL_NOM_AFFAIRE").Trim(), r["PRCLL_FORFAIT_FACTURE"] is true);
            if (pc is null)
                return IssueGeste.Refuser($"La procédure collective {id} n'est pas dans la sélection de la clôture (PC du greffe, non clôturée, " +
                                          "avec un examen de clôture à venir, sans instance de clôture) : rien n'est enregistré.");
            if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Procédure collective", LecteurProceduresCollectives.SequenceurPc,
                                                 EditionsV2.Texte(id), ct) is { } verrou)
                decochees.Add($"La PC n°{pc.Numero} a été décochée car verrouillée : {verrou}");
            else aTraiter.Add(pc);
        }
        if (aTraiter.Count == 0) return IssueGeste.Refuser(string.Join(" ", [.. decochees, AucuneSelection]));

        // La facture (GenererFacture, :2352-2365) : bouton FACTURATION enfoncé, PC hors forfait, un article sur le code.
        if (string.Equals(geste.Demande.Valeurs.GetValueOrDefault(RoutesCloture.ChampFacturer)?.Trim(), "oui", StringComparison.OrdinalIgnoreCase)
            && aTraiter.Any(p => !p.AuForfait))
            await using (var r = await geste.LireAsync("""
                SELECT TOP (1) CEPAR_CODE_ARTICLE FROM v2.CEP_ARTICLE WHERE CEPAR_CODE_EVE_PRO = @c AND CEPAR_TYPE_EP = 'P' ORDER BY CEPAR_ID_CEPAR
                """, ct, new SqlParameter("@c", code)))
                if (await r.ReadAsync(ct))
                    return IssueGeste.Refuser($"La facture de l'article {FormeLigne.Texte(r, "CEPAR_CODE_ARTICLE").Trim()} de l'évènement {code} n'est pas " +
                                              "reprise par RIG v3 : rien n'est enregistré, faire le courrier aux débiteurs dans RIG v2.");

        var jour = geste.Maintenant.Date;
        var traces = new List<TraceGeste>();
        var demandes = new List<string>();
        var editees = 0;
        foreach (var pc in aTraiter)
        {
            var affaire = await geste.InsererAsync("v2.AFFAIRE", "AFFRE_ID_AFFRE",
                new Dictionary<string, object?> { ["AFFRE_DATE_CREATION"] = geste.Maintenant }, ct);
            var (idDemande, numero, refus) = await EditionsV2.DemandeValideeAsync(geste, CodeProcessus,
                new EditionsV2.ObjetDemande(affaire, $"Courriers aux Débiteurs : {pc.Nom}", pc.Numero, pc.Numero,
                                            LecteurProceduresCollectives.SequenceurPc, EditionsV2.Texte(pc.Id)), ct);
            if (refus is not null) return IssueGeste.Refuser($"{refus} Rien n'est enregistré.");

            var numeroSource = Guid.NewGuid().ToString().ToUpperInvariant();
            var evenement = await CreationV2.InsererAsync(geste, "v2.EVENEMENT_PROCEDURE", "EVNPR_ID_EVNPR", new Dictionary<string, object?>
            {
                ["EVNPR_ID_PRCLL"] = pc.Id, ["EVNPR_CODE_EVE_PRO"] = code, ["EVNPR_DATE"] = jour, ["EVNPR_SURVENU"] = true,
                ["EVNPR_DELAI_EXPIRE"] = false, ["EVNPR_NUMERO_SOURCE"] = numeroSource,
                ["EVNPR_TYPE_EVE_PRO"] = typeEvenement.Length > 0 ? typeEvenement[..1] : null,
            }, ct);
            await geste.EcrireAsync("""
                UPDATE v2.EVENEMENT_PROCEDURE SET EVNPR_NUM_ORDRE = n.Ordre
                FROM (SELECT EVNPR_ID_EVNPR, CONVERT(smallint, ROW_NUMBER() OVER (ORDER BY ISNULL(EVNPR_SURVENU, 0), EVNPR_DATE,
                             COALESCE(EVNPR_DATE_MAXI, EVNPR_DATE_MINI, EVNPR_DATE_FIXE), EVNPR_ID_EVNPR)) AS Ordre
                      FROM v2.EVENEMENT_PROCEDURE WHERE EVNPR_ID_PRCLL = @pc AND EVNPR_CODE_EVE_PRO IS NOT NULL) AS n
                WHERE n.EVNPR_ID_EVNPR = v2.EVENEMENT_PROCEDURE.EVNPR_ID_EVNPR
                """, ct, new SqlParameter("@pc", pc.Id));

            var lesEditions = new List<string>();
            foreach (var edition in editions)
            {
                // CreateDemandeEditionByParametreGeneration rend null sans parametre (DemandeEdition.cs:2580-2584) : aucune édition.
                if (await EditionsV2.ParametreAsync(geste, edition, ct) is not { } parametre) continue;
                var maquette = await MaquetteAsync(geste, edition, ct);
                await EditionsV2.DemandeEditionAsync(geste, idDemande, parametre, null, "EVT_PROC", numeroSource, null, null, ct,
                    new Dictionary<string, object?> { ["DMNED_CODE_MAQUETTE"] = maquette, ["DMNED_CODE_PROCESSUS"] = "EDIT_EP" });
                lesEditions.Add(parametre.Libelle);
                editees++;
            }
            demandes.Add(EditionsV2.Texte(idDemande));
            traces.Add(new TraceGeste("affaire", $"la PC {pc.Numero}", EditionsV2.Texte(pc.Id), "a cree", null, new
            {
                Pc = pc.Numero, Evenement = code, Date = jour.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), IdEvenement = evenement,
                Demande = numero, Editions = lesEditions,
            }));
        }

        var faites = string.Join(", ", aTraiter.Select(p => p.Numero));
        var message = $"Courrier aux débiteurs : {aTraiter.Count} procédure(s) collective(s) traitée(s) ({faites}), évènement {code} du " +
                      $"{jour:dd/MM/yyyy}, {editees} courrier(s) dans la file des éditions.";
        if (decochees.Count > 0) message += " " + string.Join(" ", decochees);
        return IssueGeste.Ecrit(message, [.. aTraiter.Select(p => EditionsV2.Texte(p.Id))], [.. traces]);
    }

    // ================================================================ L'ENRÔLEMENT SUR SAISINE D'OFFICE (mode 1) : LE GESTE PRINCIPAL

    /// <summary>Le code processus des demandes d'enrôlement (Demande.CreateDemande(.., "EF", ..), OPE_SELECT_PC.cs:1832 ; les demandes
    /// 7028286 et 7028301 de RIG_DEV, lues le 28/11/2026, sont « EF » et validées).</summary>
    public const string CodeProcessusEnrolement = "EF";

    /// <summary>Le lien de la demande d'enrôlement à son évènement de convocation (DOSSIER_DEMANDE « 2V » des demandes EF de RIG_DEV :
    /// 2V 1717516 = l'évènement ED_CLOTLJ de l'instance 497988, lu le 28/11/2026), à côté de 2J (l'instance) et 2P (la PC).</summary>
    public const string SequenceurEvenement = "2V";

    /// <summary>Les gestes de l'enrôlement : une création (les instances) ; tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> GestesEnrolement = [TypeGeste.Creer];

    /// <summary>« Pas d'audience selectionnée » (OPE_SELECT_PC.cs:1693-1694), au mot près, sans le nom du liquidateur.</summary>
    public const string SansAudience = "En enrôlement sur SO, vous devez selectionner une audience pour le liquidateur ";

    private sealed record CodeAffaireSo(string Valeur, string TypeNumRole, string TypeInstance, string? Nature, string? Acte, string NatAffPart);

    private sealed record PcAEnroler(int Id, string Numero, string Nom, bool AuForfait, string? DebiteurPrincipal, int? Adresse, int? Dossier,
                                     int? NonInscrit, int? Repertoire, string Liquidateur, int Liquidateurs, int Huissiers);

    /// <summary>
    /// L'ENRÔLEMENT SUR SAISINE D'OFFICE des PC <c>Ids</c> du geste : le mode 1 de RIG v2, celui qu'il présélectionne quand l'examen
    /// des clôtures s'ouvre depuis l'alerte (OPE_EXAMEN_CLOT.cs:57-59). SOURCE : 1-4-5 Selection PC\OPE_SELECT_PC.cs.
    /// CONTRÔLES (Verifier, :1633-1736), dans l'ordre de RIG v2, un refus n'écrit rien :
    ///   - les paramétrages (:1640-1663) : le code affaire CLOT_CODE_AFFAIRE_SO (ParamCLOT_CodeAffaire, PROC_CLOT.cs:84-93) et sa nature
    ///     (R_CODEAFFAIR_NATURE_AFF, :120-128) ; l'évènement de convocation, CLOT_EVE_PRO_CONVSO_LRAR ou CLOT_EVE_PRO_CONVSO_HUI selon
    ///     CLOT_MODE_SO (absent ou « …LRAR… » = LRAR, sinon huissier : :152-163, :219-230) ; le demandeur ne manque jamais
    ///     (DEMANDEUR_SO_CIVILITE, défaut PM ; DEMANDEUR_SO_DESIGNATION, défaut « Saisine d'office » : Partie.cs:1552-1575) ;
    ///   - chaque PC cochée est une PC de la grille (LectureCloture.AExaminer) avec UN liquidateur en mission (une PC sans liquidateur est
    ///     bloquée, :534 ; avec plusieurs, sa ligne porte « [n] nom » et ne rejoint aucune ligne d'enrôlement, :624-627 et :1841 : RIG v2
    ///     lui crée une instance SANS appel -- la V3 refuse plutôt) ; une PC verrouillée est décochée (:1667-1683) ;
    ///   - l'audience (« Verif Tableau Enrolement », :1689-1705) : choisie par sa date et son heure, comme la grille (UltColDateAudience /
    ///     UltColHeureAudience, :1180-1300 ; AudienceCabinet.GetListAudienceCabinetParDate : AUDNC_DATE du jour, AUDNC_AUDIENCE = 1, l'heure
    ///     si elle est saisie) -- une seule est prise ; plusieurs : « N audiences correspondent au(x) critère(s), veuillez lancer la
    ///     recherche. » (:1353) ; aucune : RIG v2 ouvre la création d'audience (:1317-1344), que la V3 ne reprend pas ; le type de classement
    ///     (défaut CLOT_SO_TYPECLA, :826-828) ;
    ///   - en mode huissier (:1711-1726), un huissier par PC : celui de la PC (INTERVENANT_PC « HUI », :641-664) ;
    ///   - la facture (GenererFacture, :2352-2365) : FACTURATION enfoncé sur une PC hors forfait dont l'évènement porte un article est
    ///     REFUSÉ (la V3 n'a pas de moteur de facturation).
    /// CE QUI S'ÉCRIT, par PC (Traitements, :1776-1892 ; Sauvegarde, :2435-2482) :
    ///   - l'INSTANCE (Instance.CreateInstance(code, nature, "0", "1"), :1787-1811) : saisine, enrôlement et acte du jour, l'acte de saisine
    ///     du code affaire (sinon le premier acte de saisine d'office, ISO 20 : CODE_ACTE_PROCE.SaisineOffice), un numéro source GUID, le type
    ///     d'instance du code, la PC ; le n° de rôle de la série du code et le répertoire JGAFF, de l'année (:1805-1811) ; l'affaire de la
    ///     demande (:2466) ; son nom courant « demandeur # défendeur » (MAJNomInstance, :1826) ;
    ///   - les PARTIES (:1816-1824) : le demandeur du réglage (« D », ordre 1) ; le défendeur « F » = la PC (Instance.GetPartiesPossiblesFromPC,
    ///     Instance.cs:1437-1463) -- la désignation PRCLL_NOM_AFFAIRE (PRCLL_NOM_DEBITEUR_PRINCIPAL s'il y a d'autres débiteurs) sans sa
    ///     civilité en tête, l'adresse, le dossier, le non-inscrit et le répertoire de la PC ;
    ///   - la DEMANDE validée « EF » : créée « Enrôl sur SO : … » (:1832) puis, à la sauvegarde, nommée du nom de l'instance, indicatif le rôle,
    ///     numéros « rôle répertoire » (:2471-2477 ; RIG_DEV, demande 7028286 : « SERRANO # La SAS SOMANE », « 2026F00671 2026003953 ») ;
    ///     ses liens 2J (instance), 2P (PC), 2V (convocation) ;
    ///   - l'APPEL n° 1 de l'instance à l'audience choisie (AddAppelAffaire, :1856-1861) : le type de classement, l'heure d'appel saisie, la
    ///     composition quand l'audience n'en a qu'une, état de retour « A » (EcrivainRetourAudience.InsererAppelAsync ; RIG_DEV, appel de
    ///     l'instance 497988 : « A », préparation « 0 ») ;
    ///   - l'évènement de CONVOCATION survenu du jour sur l'instance (AddEvenementProcedureSurvenu, :1871-1872 : EvenementsProcedureV2) et
    ///     ses éditions dans la file (Editions, :1918-1972 : EditionsEvenement, ce qu'elles ne reprennent pas est dit) ;
    ///   - le 4CLO de la PC SURVENU : le premier évènement 4CLO futur seul passe survenu, daté du jour (:1881-1886).
    /// NON REPRIS (nommés) : une audience par liquidateur (la V3 prend la même pour toutes les PC cochées) ; la création d'audience ; le
    /// dépôt d'un évènement « avec dépôt » (:1873-1878 : refusé) ; l'huissier choisi dans la grille et son remplacement dans les
    /// destinataires (:1926-1962) ; la civilité et le SIREN de l'entreprise de la PC (ENTREPRISE, que la base V3 ne lit pas : civilité PM,
    /// la valeur de RIG v2 sans entreprise, :1443-1445) ; les autres débiteurs de la PC (refusé) ; le récapitulatif des enrôlements
    /// (RecapitulatifSaisine, CLOT_RECAP) ; la facture.
    /// TRACE : famille « affaire », « a cree » l'instance, et l'appel (InsererAppelAsync).
    /// </summary>
    public static async Task<IssueGeste> EnrolerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var ids = new List<int>();
        foreach (var texte in geste.Demande.Ids)
        {
            if (!int.TryParse(texte, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                return IssueGeste.Refuser($"« {texte} » n'est pas l'identifiant d'une procédure collective.");
            if (!ids.Contains(id)) ids.Add(id);
        }
        if (ids.Count == 0) return IssueGeste.Refuser(AucuneSelection);
        string Saisi(string champ) => geste.Demande.Valeurs.GetValueOrDefault(champ)?.Trim() ?? "";
        const string Paramjud = "Veuillez vous rendre en PARAMJUD afin de finaliser les paramétrages du CLOT.";

        // Les paramétrages (:1640-1663).
        var valeurCode = await ReglageAsync(geste, LectureCloture.CodeAffaireSo.Code, ct);
        CodeAffaireSo? code = null;
        if (valeurCode.Length > 0)
            await using (var r = await geste.LireAsync("""
                SELECT R_CODEAFFAIR_VALEUR, R_CODEAFFAIR_TYPE_NUM_ROLE, R_CODEAFFAIR_TYPE_INSTANCE, R_CODEAFFAIR_NATURE_AFF, R_CODEAFFAIR_CODE_ACTE_PRO,
                       R_CODEAFFAIR_NAT_AFF_PART FROM v2.CODE_AFFAIRE WHERE R_CODEAFFAIR_VALEUR = @c
                """, ct, new SqlParameter("@c", valeurCode)))
                if (await r.ReadAsync(ct))
                {
                    string? T(string c) => FormeLigne.Texte(r, c).Trim() is { Length: > 0 } s ? s : null;
                    code = new CodeAffaireSo(T("R_CODEAFFAIR_VALEUR")!, T("R_CODEAFFAIR_TYPE_NUM_ROLE") ?? "", T("R_CODEAFFAIR_TYPE_INSTANCE") ?? "",
                                             T("R_CODEAFFAIR_NATURE_AFF"), T("R_CODEAFFAIR_CODE_ACTE_PRO"), T("R_CODEAFFAIR_NAT_AFF_PART") ?? "0A");
                }
        if (code is null) return IssueGeste.Refuser($"Le code affaire n'a pas été paramétré. {Paramjud}");
        if (code.Nature is null)
            return IssueGeste.Refuser($"La nature d'affaire n'a pas été paramétrée - ni dans les paramétrages CLOT, ni dans le Code Affaire '{code.Valeur}'. {Paramjud}");
        var mode = await ReglageAsync(geste, "CLOT_MODE_SO", ct);
        var parHuissier = mode.Length > 0 && !mode.Contains("LRAR", StringComparison.Ordinal);
        var codeEve = await ReglageAsync(geste, parHuissier ? "CLOT_EVE_PRO_CONVSO_HUI" : "CLOT_EVE_PRO_CONVSO_LRAR", ct);
        bool? avecDepot = null;
        if (codeEve.Length > 0)
            await using (var r = await geste.LireAsync("""
                SELECT CONVERT(bit, CASE WHEN ISNULL(R_CODEEVEPRO_AVEC_DEPOT, 0) = 1 AND R_CODEEVEPRO_CATEGORIE_DEPOT_INS IS NOT NULL THEN 1 ELSE 0 END) AS Depot
                FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c
                """, ct, new SqlParameter("@c", codeEve)))
                if (await r.ReadAsync(ct)) avecDepot = r["Depot"] is true;
        if (avecDepot is null) return IssueGeste.Refuser($"L'évènement de procédure n'a pas été paramétré. {Paramjud}");
        if (avecDepot == true)
            return IssueGeste.Refuser($"Le dépôt de l'évènement {codeEve} (R_CODEEVEPRO_AVEC_DEPOT, OPE_SELECT_PC.cs:1873-1878) n'est pas repris par RIG v3 : " +
                                      "rien n'est enregistré, faire l'enrôlement sur saisine d'office dans RIG v2.");
        var civiliteDemandeur = await ReglageAsync(geste, "DEMANDEUR_SO_CIVILITE", ct) is { Length: > 0 } cd ? cd : "PM";
        var demandeur = await ReglageAsync(geste, "DEMANDEUR_SO_DESIGNATION", ct) is { Length: > 0 } dd ? dd : "Saisine d'office";
        var acte = code.Acte ?? await RelancesRsurv.Scalaire(geste,
            "SELECT TOP (1) R_CODEACTEPR_VALEUR FROM v2.CODE_ACTE_PROCE WHERE R_CODEACTEPR_ISO = '20' ORDER BY R_CODEACTEPR_VALEUR", ct) as string;
        if (string.IsNullOrWhiteSpace(acte))
            return IssueGeste.Refuser($"Aucun acte de saisine d'office (ISO 20) dans la base du greffe ni sur le code affaire {code.Valeur} : rien n'est enregistré.");

        // Les PC cochées : de la grille, un liquidateur, pas verrouillées (:1667-1683) ; en mode huissier, un huissier (:1711-1726).
        var codes = new List<(string Nom, object Valeur)>();
        foreach (var (nom, reglage) in new[] { ("@code1", LectureCloture.CodeAffaireSo), ("@code2", LectureCloture.CodeAffaire),
                                              ("@code3", LectureCloture.CodeAffaireAv2006) })
            codes.Add((nom, await ReglageAsync(geste, reglage.Code, ct) is { Length: > 0 } c ? c : DBNull.Value));
        var autresDebiteurs = await RelancesRsurv.Scalaire(geste, "SELECT OBJECT_ID(N'v2.DEBITEUR_PC_AUTRE')", ct) is int;
        var aEnroler = new List<PcAEnroler>();
        var decochees = new List<string>();
        foreach (var id in ids)
        {
            PcAEnroler? pc = null;
            await using (var r = await geste.LireAsync($"""
                SELECT p.PRCLL_ID_PRCLL, p.PRCLL_NUM_PC, p.PRCLL_NOM_AFFAIRE, p.PRCLL_FORFAIT_FACTURE, p.PRCLL_NOM_DEBITEUR_PRINCIPAL, p.PRCLL_ID_ADRBS,
                       p.PRCLL_ID_DSRCS, p.PRCLL_ID_NINSC, p.PRCLL_ID_RPRTR,
                       (SELECT COUNT(*) FROM v2.GERER_PC AS g
                        WHERE g.GRRPC_ID_PRCLL = p.PRCLL_ID_PRCLL AND g.GRRPC_FONCTION_PC = 'LIQ' AND g.GRRPC_FIN_MISSION = 0) AS Liquidateurs,
                       (SELECT TOP (1) LTRIM(RTRIM(ISNULL(mp.MNDPC_CIVILITE + ' ', '') + mp.MNDPC_NOM + ISNULL(' ' + mp.MNDPC_COMPLEMENT_NOM, '')))
                        FROM v2.GERER_PC AS g JOIN v2.MANDATAIRE_PC AS mp ON mp.MNDPC_ID_MNDPC = g.GRRPC_ID_MNDPC
                        WHERE g.GRRPC_ID_PRCLL = p.PRCLL_ID_PRCLL AND g.GRRPC_FONCTION_PC = 'LIQ' AND g.GRRPC_FIN_MISSION = 0
                        ORDER BY g.GRRPC_NUM_ORDRE, g.GRRPC_ID_GRRPC) AS Liquidateur,
                       (SELECT COUNT(*) FROM v2.INTERVENANT_PC AS h WHERE h.INTRP_ID_PRCLL = p.PRCLL_ID_PRCLL AND h.INTRP_FONCTION_PC = 'HUI') AS Huissiers
                FROM v2.PROCEDURE_COLLECTIVE AS p
                WHERE p.PRCLL_ID_PRCLL = @id AND {LectureCloture.AExaminer}
                """, ct, [new SqlParameter("@id", id), .. codes.Select(c => new SqlParameter(c.Nom, System.Data.SqlDbType.VarChar, 40) { Value = c.Valeur })]))
                if (await r.ReadAsync(ct))
                {
                    int? Id(string c) => r[c] is DBNull ? null : Convert.ToInt32(r[c], CultureInfo.InvariantCulture);
                    pc = new PcAEnroler(id, FormeLigne.Texte(r, "PRCLL_NUM_PC").Trim(), FormeLigne.Texte(r, "PRCLL_NOM_AFFAIRE").Trim(),
                        r["PRCLL_FORFAIT_FACTURE"] is true, FormeLigne.Texte(r, "PRCLL_NOM_DEBITEUR_PRINCIPAL").Trim() is { Length: > 0 } dp ? dp : null,
                        Id("PRCLL_ID_ADRBS"), Id("PRCLL_ID_DSRCS"), Id("PRCLL_ID_NINSC"), Id("PRCLL_ID_RPRTR"), FormeLigne.Texte(r, "Liquidateur").Trim(),
                        Convert.ToInt32(r["Liquidateurs"], CultureInfo.InvariantCulture), Convert.ToInt32(r["Huissiers"], CultureInfo.InvariantCulture));
                }
            if (pc is null)
                return IssueGeste.Refuser($"La procédure collective {id} n'est pas dans la sélection de la clôture (PC du greffe, non clôturée, " +
                                          "avec un examen de clôture à venir, sans instance de clôture) : rien n'est enregistré.");
            if (pc.Liquidateurs == 0) return IssueGeste.Refuser($"La PC n°{pc.Numero} : {EcranClotureSansLiquidateur} Rien n'est enregistré.");
            if (pc.Liquidateurs > 1)
                return IssueGeste.Refuser($"La PC n°{pc.Numero} a {pc.Liquidateurs} liquidateurs : RIG v2 l'enrôle sans appel à l'audience (sa ligne « [{pc.Liquidateurs}] " +
                                          $"{pc.Liquidateur} » ne rejoint aucun liquidateur de la grille d'enrôlement, OPE_SELECT_PC.cs:1841). Non repris par RIG v3 : " +
                                          "rien n'est enregistré, faire cet enrôlement dans RIG v2.");
            if (autresDebiteurs && await RelancesRsurv.Scalaire(geste, "SELECT COUNT(*) FROM v2.DEBITEUR_PC_AUTRE WHERE DBTAT_ID_PRCLL = @pc", ct,
                                                                 new SqlParameter("@pc", id)) is int n && n > 0)
                return IssueGeste.Refuser($"La PC n°{pc.Numero} a d'autres débiteurs : une partie défenderesse par débiteur (Instance.cs:1465-1489) n'est pas " +
                                          "reprise par RIG v3 : rien n'est enregistré, faire cet enrôlement dans RIG v2.");
            if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Procédure collective", LecteurProceduresCollectives.SequenceurPc,
                                                 EditionsV2.Texte(id), ct) is { } verrou)
                decochees.Add($"La PC n°{pc.Numero} a été décochée car verrouillée : {verrou}");
            else aEnroler.Add(pc);
        }
        if (aEnroler.Count == 0) return IssueGeste.Refuser(string.Join(" ", [.. decochees, AucuneSelection]));
        var liquidateurs = string.Join(", ", aEnroler.Select(p => p.Liquidateur).Distinct());

        // L'audience et le classement de la ligne du liquidateur (:1689-1705), l'audience lue par sa date et son heure (:1180-1369).
        if (Saisi(RoutesCloture.ChampDateAudience).Length == 0) return IssueGeste.Refuser(SansAudience + liquidateurs);
        if (AudiencesV2.Date(Saisi(RoutesCloture.ChampDateAudience)) is not { } dateAudience)
            return IssueGeste.Refuser($"« {RoutesCloture.ChampDateAudience} » : date illisible ({Saisi(RoutesCloture.ChampDateAudience)}).");
        var heureAudience = Saisi(RoutesCloture.ChampHeureAudience) is { Length: > 0 } ha ? ha : null;
        var audiences = new List<(int Id, string Libelle, int Compositions, int? Composition)>();
        await using (var r = await geste.LireAsync($"""
            SELECT a.AUDNC_ID_ADNC, {AudiencesV2.Libelle} AS Libelle,
                   (SELECT COUNT(*) FROM v2.COMPOSITION AS c WHERE c.CMPST_ID_ADNC = a.AUDNC_ID_ADNC) AS Compositions,
                   (SELECT MIN(c.CMPST_ID_CMAUD) FROM v2.COMPOSITION AS c WHERE c.CMPST_ID_ADNC = a.AUDNC_ID_ADNC) AS Composition
            FROM v2.AUDIENCE_CABINET AS a {AudiencesV2.Jointures}
            WHERE a.AUDNC_DATE >= @jour AND a.AUDNC_DATE < DATEADD(day, 1, @jour) AND a.AUDNC_AUDIENCE = 1 AND (@heure IS NULL OR a.AUDNC_HEURE = @heure)
            ORDER BY a.AUDNC_HEURE, a.AUDNC_ID_ADNC
            """, ct, new SqlParameter("@jour", dateAudience), new SqlParameter("@heure", (object?)heureAudience ?? DBNull.Value)))
            while (await r.ReadAsync(ct))
                audiences.Add((Convert.ToInt32(r["AUDNC_ID_ADNC"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "Libelle"),
                               Convert.ToInt32(r["Compositions"], CultureInfo.InvariantCulture),
                               r["Composition"] is DBNull ? null : Convert.ToInt32(r["Composition"], CultureInfo.InvariantCulture)));
        var criteres = $"le {dateAudience:dd/MM/yyyy}{(heureAudience is null ? "" : $" à {heureAudience}")}";
        if (audiences.Count == 0)
            return IssueGeste.Refuser($"Aucune audience {criteres} dans la base du greffe : RIG v2 ouvre alors la création d'audience (PROC_AUDIENCE, " +
                                      "OPE_SELECT_PC.cs:1317-1344), que la V3 ne reprend pas. Rien n'est enregistré.");
        if (audiences.Count > 1)
            return IssueGeste.Refuser($"{audiences.Count} audiences correspondent au(x) critère(s), veuillez lancer la recherche. " +
                                      $"Précisez « {RoutesCloture.ChampHeureAudience} » ({criteres}).");
        var audience = audiences[0];
        var classement = Saisi(RoutesCloture.ChampClassement) is { Length: > 0 } tc ? tc : await ReglageAsync(geste, "CLOT_SO_TYPECLA", ct);
        if (classement.Length == 0)
            return IssueGeste.Refuser("En enrôlement sur SO, vous devez spécifier un type de classement pour les audiences selectionnées pour le liquidateur " + liquidateurs);
        if (await RelancesRsurv.Scalaire(geste, "SELECT R_TYPECLASSE_VALEUR FROM v2.TYPE_CLASSEMENT WHERE R_TYPECLASSE_VALEUR = @t", ct,
                                         new SqlParameter("@t", classement)) is null or DBNull)
            return IssueGeste.Refuser($"Le type de classement « {classement} » n'est pas dans la base du greffe.");
        var heureAppel = Saisi(RoutesCloture.ChampHeureAppel) is { Length: > 0 } hp ? hp : null;
        if (heureAppel is { Length: > 5 }) return IssueGeste.Refuser($"« {RoutesCloture.ChampHeureAppel} » : cinq caractères au plus (« 09:30 »).");
        if (parHuissier && aEnroler.FirstOrDefault(p => p.Huissiers == 0) is { } sansHuissier)
            return IssueGeste.Refuser($"Certaines affaires du liquidateur {sansHuissier.Liquidateur} n'ont pas d'huissier renseigné. Vous devez renseigner le champ " +
                                      $"'Huissier si vide dans la liste du haut' pour le liquidateur {sansHuissier.Liquidateur} (PC n°{sansHuissier.Numero}). " +
                                      "Le choix d'un huissier dans la grille d'enrôlement n'est pas repris par RIG v3 : rien n'est enregistré.");
        if (string.Equals(Saisi(RoutesCloture.ChampFacturer), "oui", StringComparison.OrdinalIgnoreCase) && aEnroler.Any(p => !p.AuForfait)
            && await EvenementsProcedureV2.FactureNonRepriseAsync(geste, new EditionsEvenementV2.Instance(0, "", null, code.Nature, code.NatAffPart, acte),
                                                                   codeEve, "l'enrôlement sur saisine d'office", ct) is { } facture)
            return IssueGeste.Refuser(facture);

        // Les civilités, ôtées du début de la désignation du défendeur (Instance.cs:1451-1457).
        var civilites = new List<string>();
        if (await RelancesRsurv.Scalaire(geste, "SELECT OBJECT_ID(N'v2.CIVILITE')", ct) is int)
            await using (var r = await geste.LireAsync("SELECT R_CIVILITE_DEVELOPPE_EDIT, R_CIVILITE_SIMPLIFIE_EDIT FROM v2.CIVILITE ORDER BY R_CIVILITE_VALEUR", ct))
                while (await r.ReadAsync(ct))
                    foreach (var c in new[] { FormeLigne.Texte(r, "R_CIVILITE_DEVELOPPE_EDIT"), FormeLigne.Texte(r, "R_CIVILITE_SIMPLIFIE_EDIT") })
                        if (c.Length > 0) civilites.Add(c);

        var jour = geste.Maintenant.Date;
        var traces = new List<TraceGeste>();
        var bilans = new List<EditionsEvenement.Bilan>();
        var faites = new List<string>();
        var instances = new List<string>();
        foreach (var pc in aEnroler)
        {
            var (role, refusRole) = await CreationV2.NumeroAsync(geste, code.TypeNumRole, geste.Maintenant.Year, ct);
            if (role is null) return IssueGeste.Refuser($"{refusRole} Rien n'est enregistré.");
            var (repertoire, refusRep) = await CreationV2.NumeroAsync(geste, "JGAFF", geste.Maintenant.Year, ct);
            if (repertoire is null) return IssueGeste.Refuser($"{refusRep} Rien n'est enregistré.");
            var defendeur = pc.DebiteurPrincipal ?? pc.Nom;
            foreach (var c in civilites)
                if (defendeur.StartsWith(c, StringComparison.Ordinal)) defendeur = defendeur[c.Length..].Trim();
            var nom = CreationV2.NomCourant(demandeur, defendeur);

            var affaire = await geste.InsererAsync("v2.AFFAIRE", "AFFRE_ID_AFFRE", new Dictionary<string, object?> { ["AFFRE_DATE_CREATION"] = geste.Maintenant }, ct);
            var instance = await CreationV2.InsererAsync(geste, "v2.INSTANCE", "INSTN_ID_INSTN", new Dictionary<string, object?>
            {
                ["INSTN_ID_AFFRE"] = affaire, ["INSTN_ID_PRCLL"] = pc.Id, ["INSTN_CODE_AFFAIRE"] = code.Valeur, ["INSTN_NATURE_AFFAIRE"] = code.Nature,
                ["INSTN_NAT_AFF_PART"] = code.NatAffPart, ["INSTN_CODE_ACTE_PROCE"] = acte, ["INSTN_DATE_ACTE"] = jour, ["INSTN_DATE_SAISINE_GREFFE"] = jour,
                ["INSTN_DATE_ENROLEMENT"] = jour, ["INSTN_TYPE_INSTANCE"] = code.TypeInstance, ["INSTN_ETAT_INSTANCE"] = "0", ["INSTN_ETAT_INFO"] = "1",
                ["INSTN_NUM_ROLE"] = role, ["INSTN_NUM_REP_GENERAL"] = repertoire, ["INSTN_NOM_COURANT"] = nom,
                ["INSTN_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(),
            }, ct);
            foreach (var (civilite, designation, qualif, liens) in new[]
                     {
                         (civiliteDemandeur, demandeur, "D", (Adresse: (int?)null, Dossier: (int?)null, NonInscrit: (int?)null, Repertoire: (int?)null)),
                         ("PM", defendeur, "F", (pc.Adresse, pc.Dossier, pc.NonInscrit, pc.Repertoire)),
                     })
                await CreationV2.InsererAsync(geste, "v2.PARTIE", "PRT_ID_PRT", new Dictionary<string, object?>
                {
                    ["PRT_ID_INSTN"] = instance, ["PRT_NUM_ORDRE"] = (short)1, ["PRT_CIVILITE"] = civilite, ["PRT_DESIGNATION"] = designation,
                    ["PRT_NOM"] = designation, ["PRT_QUALIF_PARTIE"] = qualif, ["PRT_EDITION_JUGEMENT"] = "D", ["PRT_MANDATAIRE_EDITION_JUGEMENT"] = "D",
                    ["PRT_MANDATAIRE_PLAI_EDITION_JUGEMENT"] = "D", ["PRT_ID_ADRBS"] = liens.Adresse, ["PRT_ID_DSRCS"] = liens.Dossier,
                    ["PRT_ID_NINSC"] = liens.NonInscrit, ["PRT_ID_RPRTR"] = liens.Repertoire,
                }, ct);

            var idInstance = instance.ToString(CultureInfo.InvariantCulture);
            var (idDemande, numero, refus) = await EditionsV2.DemandeValideeAsync(geste, CodeProcessusEnrolement,
                new EditionsV2.ObjetDemande(affaire, nom, role, $"{role} {repertoire}", VerrouDemandeV2.SequenceurInstance, idInstance), ct);
            if (refus is not null) return IssueGeste.Refuser($"{refus} Rien n'est enregistré.");
            traces.Add(await EcrivainRetourAudience.InsererAppelAsync(geste, instance, role, 1, audience.Id,
                                                                     audience.Compositions == 1 ? audience.Composition : null, classement, audience.Libelle, ct));
            if (heureAppel is not null)
                await geste.EcrireAsync("UPDATE v2.APPEL_AFFAIRE SET APPAF_HEURE_APPEL = @h WHERE APPAF_ID_INSTN = @i AND APPAF_NUM_ORDRE = 1", ct,
                    new SqlParameter("@h", heureAppel), new SqlParameter("@i", instance));
            var convocation = await EvenementsProcedureV2.SurvenuAsync(geste, instance, codeEve, jour, avecFutur: true, ct);
            foreach (var (sequenceur, valeur) in new[] { (LecteurProceduresCollectives.SequenceurPc, EditionsV2.Texte(pc.Id)),
                                                         (SequenceurEvenement, convocation.IdSurvenu.ToString(CultureInfo.InvariantCulture)) })
                await geste.InsererAsync("v2.DOSSIER_DEMANDE", "DSSDM_ID_DSSDM", new Dictionary<string, object?>
                {
                    ["DSSDM_ID_DMND"] = idDemande, ["DSSDM_CODE_SEQUENCEUR"] = sequenceur, ["DSSDM_VALEUR_SEQUENCEUR"] = valeur,
                }, ct);
            bilans.Add(await EditionsEvenement.CreerAsync(geste, idDemande, numero, convocation.IdSurvenu, ct));

            // Le 4CLO de la PC survenu (:1881-1886) : le premier évènement 4CLO futur seul.
            var examen = await RelancesRsurv.Scalaire(geste, """
                SELECT TOP (1) EVNPR_ID_EVNPR FROM v2.EVENEMENT_PROCEDURE
                WHERE EVNPR_ID_PRCLL = @pc AND EVNPR_CODE_EVE_PRO LIKE '4CLO%' AND EVNPR_SURVENU = 0 AND EVNPR_DELAI_EXPIRE = 0
                ORDER BY EVNPR_NUM_ORDRE, EVNPR_ID_EVNPR
                """, ct, new SqlParameter("@pc", pc.Id)) as int?;
            if (examen is { } idExamen)
                await geste.EcrireAsync("UPDATE v2.EVENEMENT_PROCEDURE SET EVNPR_DATE = @d, EVNPR_SURVENU = 1 WHERE EVNPR_ID_EVNPR = @e", ct,
                    new SqlParameter("@d", jour), new SqlParameter("@e", idExamen));

            faites.Add($"{role} (PC {pc.Numero})");
            instances.Add(idInstance);
            traces.Add(new TraceGeste("affaire", $"l'instance {role}", idInstance, "a cree", null, new
            {
                NumeroRole = role, Repertoire = repertoire, Pc = pc.Numero, NomCourant = nom, CodeAffaire = code.Valeur, Demande = numero,
                Audience = audience.Libelle, TypeClassement = classement, HeureAppel = heureAppel, Convocation = codeEve,
                IdConvocation = convocation.IdSurvenu, Examen4Clo = examen,
            }));
        }

        var message = $"Enrôlement sur saisine d'office : {faites.Count} instance(s) de clôture créée(s), {string.Join(", ", faites)}, appelée(s) à " +
                      $"l'{audience.Libelle} (classement {classement}{(heureAppel is null ? "" : $", appel à {heureAppel}")}) ; convocation {codeEve} du " +
                      $"{jour:dd/MM/yyyy}, examen de clôture (4CLO) survenu.{EditionsEvenement.Message(bilans)}";
        if (decochees.Count > 0) message += " " + string.Join(" ", decochees);
        return IssueGeste.Ecrit(message, [.. instances], [.. traces]);
    }

    /// <summary>La phrase de RIG v2 pour une PC sans liquidateur (OPE_SELECT_PC.cs:534), celle de la grille (Pont/EcranClotureBase.cs).</summary>
    private const string EcranClotureSansLiquidateur = "Pas de liquidateur présent dans la procédure collective.";

    /// <summary>Le texte d'un réglage (soc.Reglage, reprise de PARAMETRAGE), lu dans la transaction du geste ; vide s'il manque.</summary>
    internal static async Task<string> ReglageAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT Valeur FROM soc.Reglage WHERE Code = @c", ct, new SqlParameter("@c", code));
        return await r.ReadAsync(ct) && r["Valeur"] is string v ? v.Trim() : "";
    }

    /// <summary>La maquette du paramètre d'édition <paramref name="code"/> (PRMED_CODE_MAQUETTE) ; null si la base ne la porte pas.</summary>
    internal static async Task<string?> MaquetteAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        if (!(await EditionsV2.ColonnesAsync(geste, "v2.PARAMETRE_EDITION", ct)).Contains("PRMED_CODE_MAQUETTE")) return null;
        await using var r = await geste.LireAsync("SELECT PRMED_CODE_MAQUETTE FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = @c", ct, new SqlParameter("@c", code));
        return await r.ReadAsync(ct) && r["PRMED_CODE_MAQUETTE"] is string m ? m.Trim() : null;
    }
}
