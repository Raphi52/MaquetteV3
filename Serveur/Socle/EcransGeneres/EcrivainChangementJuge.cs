using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurChangementJuge;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Changer le juge-commissaire » (PROC_CHM) : VALIDER fabrique, pour chaque PC cochee, l'ordonnance du president qui change son
/// juge-commissaire, et le changement EN ATTENTE que la saisie de l'ordonnance signee appliquera (MPC). Mode d'emploi :
/// EcrivainEcran.cs ; la liste : LecteurChangementJuge.cs. Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 6,
/// 28/11/2026.
///
/// SOURCE (ancien RIG) : RIG\DLL\Processus\PROC_CHM\PROC_CHJCM -- PROC_CHJCM.cs (DoVerifier, _GenererOrdonnance, _PreparerNumeros,
/// _Sauver, _ValidationSauver), Metier\Ordonnance\Ordonnance.cs (CreateOrdonnance :242-294, GenrerNumeros :176-186),
/// Metier\Processus\ChangementJugeParametres.cs (VerifierParametrage :225-277), Saisie\OPE_SAISIE_CHJCM.cs et
/// Saisie\Control\OPE_DECISION_JUDICIAIRE.cs (leurs DoVerifier).
///
/// CONTROLES, avant toute ecriture, dans l'ordre (un refus n'ecrit rien) :
///   1. une PC au moins est cochee (« Aucune procédure collective sélectionnée. », PROC_CHJCM.cs:171) ;
///   2. le PARAMETRAGE (VerifierParametrage, ChangementJugeParametres.cs:160-277 ; une erreur grise VALIDER, PROC_CHJCM.cs:60-66) :
///      les quatre reglages DEMANDEUR_SO_CIVILITE, DEMANDEUR_SO_DESIGNATION, CHJCM_CODE_AFFAIRE, CHJCM_MODE_FACTU (PARAMETRAGE, repris
///      dans soc.Reglage), la civilite dans CIVILITE, le code affaire dans CODE_AFFAIRE, une ordonnance du president (type RP), sa
///      nature et son acte de saisine (une saisine d'office), ses evenements de creation sans depot ni piece numerisee, le mode T ou F
///      -- messages au mot pres ;
///   3. le juge a remplacer : le critere est exige (« Vous devez saisir un juge à remplacer pour lancer la recherche »,
///      OPE_RECHERCHE_CHJCM.cs:99-104) ; la V3 le cherche (nom, prenom, abrege) parmi les juges en mission dans la fonction cherchee
///      sur les PC cochees, et le veut unique (la combo de la V2 en designe un) ;
///   4. la SAISIE : le nouveau juge et la date de l'acte (MetierObligatoire, OPE_SAISIE_CHJCM.Designer.cs:77, :90), la date jamais apres
///      le jour (:83), le nouveau juge different de l'ancien (:59) ; le type de decision, la date prevue, la nature et le deposant GED
///      (MetierObligatoire, OPE_DECISION_JUDICIAIRE.Designer.cs:80, :92, :142, :152), le type avec une edition parametree et la serie
///      de minutes JGOP (:62-71), la date prevue jamais avant la date de l'acte (:91-95) ;
///   5. chaque PC est dans la recherche du juge a remplacer, sans erreur (UpdateErreursMetier, LecteurChangementJuge.ErreursBloquantes :
///      une ligne en erreur ne se coche pas) ni changement deja en attente ;
///   6. une PC verrouillee par une demande de RIG v2 (ProcedureCollective.Lock, Ordonnance.cs:258-262) n'est pas traitee -- la V2 la
///      saute et le dit (_CheckOrdonnances, PROC_CHJCM.cs:207-220) ; toutes verrouillees : refus, au message de la V2 ;
///   7. ce que la V3 ne reprend pas refuse en le nommant : l'evenement de la PC lie a un evenement de creation
///      (R_CODEEVEPRO_CODE_EVE_PRO_PC, Ordonnance.cs:285-289), la FACTURE d'une PC hors forfait (Ordonnance.GenererFacture, :188-215 :
///      un article de l'evenement, EvenementsProcedureV2.FactureNonRepriseAsync), un defendeur que le lien « PC » prend ailleurs que dans
///      la PC (LIEN_CODEACTEPROCE_ENTITEPC : dirigeants...), une maquette d'edition d'evenement non reprise (EditionsEvenementV2).
///
/// CE QUI S'ECRIT, par PC traitee, comme Ordonnance.CreateOrdonnance et PROC_CHJCM._Sauver / _ValidationSauver :
///   - l'AFFAIRE (Affaire.CreateAffaire) et l'INSTANCE du code affaire parametre (CreationV2.CreerInstanceAsync : etats « 0 » / « 1 »,
///     dates de l'acte et de saisine = la date saisie, enrolement = le jour, numero de role de la serie du code affaire et repertoire
///     general JGAFF de l'annee de saisine -- GenrerNumeros, :176-186 ; num.Attribuer dans la transaction du geste), rattachee a la PC
///     (INSTN_ID_PRCLL) ;
///   - les PARTIES : le demandeur parametre (civilite et designation, :269) ; en defendeurs, la PC (Instance.GetPartiesPossiblesFromPC,
///     Instance.cs:1430-1490 : civilite, SIREN et repertoire de son entreprise, sinon « PM » ; designation = le debiteur principal s'il y
///     a des extensions, le nom de l'affaire sinon, sans sa civilite ; son adresse, son dossier RCS, son non-inscrit), puis chacune de ses
///     extensions (DEBITEUR_AUTRE) ;
///   - la DECISION (DecisionJudiciaire.CreateDecisionJudiciaire, rang 1) : le type, la date prevue, le juge de la decision, la piece
///     numerisee, la nature et le deposant GED (type « AUTRE ») (:274-283) ;
///   - les EVENEMENTS de creation du code affaire, survenus au jour, et leurs editions (:286-290 ; EvenementsProcedureV2,
///     EditionsEvenementV2, comme EcrivainOrdonnancesASigner.cs) ;
///   - la DEMANDE CHJCM de l'instance, validee (_ValidationSauver :300-312 : nom courant, role, numeros), fille de la demande du
///     processus (« Une ordonnance générée pour le changement du juge-commissaire titulaire 'X' par 'Y' »), liee a la PC (son verrou) ;
///   - l'EDITION de la decision (DecisionJudiciaire.CreateDemandeEdition : le parametre d'edition du type de decision ; pas d'impression
///     si le juge signe en dematerialise pour ce type d'instance, Ordonnance.cs:96-101) ;
///   - le CHANGEMENT EN ATTENTE (EtreJugeCommEnAttente.CreateEtreJugeCommEnAttente, EtreJugeCommEnAttente.cs:123-134 ;
///     PROC_CHJCM.cs:252-261) : la decision, la PC, l'ancien et le nouveau juge, « JCT » ou « JCS », l'auteur et la date.
/// TRACE : famille « affaire », « a cree », par ordonnance.
///
/// NON REPRIS (nommes ; le message le dit) : la facture (refusee plus haut des qu'un article s'applique) ; les mots directeurs de
/// l'instance (GenererMotDirecteur, :232-238) ; le rapport Excel archive dans le repertoire commun (_GenererRapport, :327-361) ; le
/// greffier de la decision (DCJDC_ID_GRASS : l'ecran ne le saisit pas) ; l'ecriture des parametres de l'etape Paramètres (Sauver,
/// ChangementJugeParametres.cs:304-315) -- un parametre envoye different du reglage est refuse.
/// </summary>
internal sealed class EcrivainChangementJuge : IEcrivainEcran
{
    public string Procedure => "PROC_CHM";
    public string Ecran => "Changer le juge-commissaire";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Confirmer];

    /// <summary>Le geste du descripteur (le bouton VALIDER de FORM_CHJCM).</summary>
    internal const string Valider = "Valider";

    /// <summary>Les sections et champs du descripteur (ModifierAffaire.cs), au mot pres.</summary>
    internal const string SectionParametres = "Paramètres", SectionNouveauJuge = "Nouveau juge", SectionDecision = "Décision";

    internal const string ChampCodeAffaire = "Code affaire", ChampModeFacturation = "Mode de facturation", ChampNouveauJuge = "Nouveau juge-commissaire",
                          ChampDateActe = "Date de l'acte", ChampTypeDecision = "Type de décision", ChampDatePrevue = "Date prévue",
                          ChampJugeDecision = "Juge de la décision", ChampNatureGed = "Nature doc GED", ChampDeposantGed = "Nom déposant GED";

    /// <summary>Les reglages du processus (ChangementJugeParametres.cs:14-17).</summary>
    internal const string ParamCivilite = "DEMANDEUR_SO_CIVILITE", ParamDesignation = "DEMANDEUR_SO_DESIGNATION",
                          ParamCodeAffaire = "CHJCM_CODE_AFFAIRE", ParamModeFactu = "CHJCM_MODE_FACTU";

    /// <summary>Le code processus de la demande (MetierCodeProcessus).</summary>
    internal const string CodeProcessus = "CHJCM";

    private const string Objet = "le changement de juge-commissaire";

    private sealed record Juge(int Id, string Nom);

    private sealed record Pc(int Id, string Numero, string NomAffaire, string? DebiteurPrincipal, bool Forfait, int? IdEntreprise, int? IdAdresse,
                             int? IdDossierRcs, int? IdNonInscrit);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Valider) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return IssueGeste.Refuser("Aucune procédure collective sélectionnée.");

        // 2. Le parametrage.
        var (parametres, refusParametres) = await ParametresAsync(geste, ct);
        if (parametres is null) return IssueGeste.Refuser(refusParametres!);
        var codeAffaire = parametres.Value.Code;

        // 3. Le juge a remplacer, dans la fonction cherchee.
        var suppleant = (geste.Demande.Valeurs.GetValueOrDefault(CritereStatut) ?? "").Trim().StartsWith("supp", StringComparison.OrdinalIgnoreCase);
        var critereJuge = (geste.Demande.Valeurs.GetValueOrDefault(CritereJuge) ?? "").Trim();
        if (critereJuge.Length == 0) return IssueGeste.Refuser("Vous devez saisir un juge à remplacer pour lancer la recherche");
        var aRemplacer = await JugesAsync(geste, $"""
            SELECT DISTINCT j.JUG_ID_JUG, j.JUG_NOM, j.JUG_PRENOM FROM v2.JUGE AS j
            JOIN v2.ETRE_JUGE_COMM AS ej ON ej.ETJCM_ID_JUG = j.JUG_ID_JUG
            WHERE ej.ETJCM_FIN_MISSION = 0 AND ej.ETJCM_FONCTION_PC IN {Fonctions(suppleant)}
              AND ej.ETJCM_ID_PRCLL IN ({string.Join(", ", ids.Select((_, k) => "@pc" + k))})
              AND (j.JUG_NOM LIKE @juge OR j.JUG_PRENOM LIKE @juge OR j.JUG_ABREGE LIKE @juge)
            """, ct, [new SqlParameter("@juge", FormeLigne.Contient(critereJuge)), .. ids.Select((id, k) => new SqlParameter("@pc" + k, Entier(id) ?? 0))]);
        if (aRemplacer.Count == 0)
            return IssueGeste.Refuser($"Aucune PC cochée n'a « {critereJuge} » pour juge-commissaire {(suppleant ? "suppléant" : "titulaire")} en mission.");
        if (aRemplacer.Count > 1)
            return IssueGeste.Refuser($"Plusieurs juges répondent à « {critereJuge} » ({string.Join(", ", aRemplacer.Select(j => j.Nom))}) : saisir le juge à remplacer par son abrégé.");
        var ancien = aRemplacer[0];

        // 4. La saisie.
        string S(string section, string champ) => CreationV2.Saisi(geste, section, champ);
        if (S(SectionNouveauJuge, ChampNouveauJuge).Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ChampNouveauJuge));
        if (S(SectionNouveauJuge, ChampDateActe).Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ChampDateActe));
        if (CreationV2.Date(S(SectionNouveauJuge, ChampDateActe)) is not { } dateActe)
            return IssueGeste.Refuser($"« {ChampDateActe} » : date illisible ({S(SectionNouveauJuge, ChampDateActe)}).");
        if (dateActe.Date > geste.Maintenant.Date) return IssueGeste.Refuser("La date de l'acte doit être inférieure ou égale à la date du jour.");
        if (await JugeSaisiAsync(geste, S(SectionNouveauJuge, ChampNouveauJuge), ct) is not { } nouveau)
            return IssueGeste.Refuser($"« {ChampNouveauJuge} » : aucun juge de la base du greffe ne répond seul à « {S(SectionNouveauJuge, ChampNouveauJuge)} » (son abrégé, ou son nom).");
        if (nouveau.Id == ancien.Id) return IssueGeste.Refuser("Le juge à remplacer et le nouveau juge sont les mêmes.");
        foreach (var champ in new[] { ChampTypeDecision, ChampDatePrevue, ChampNatureGed, ChampDeposantGed })
            if (S(SectionDecision, champ).Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(champ));
        var typeDecision = S(SectionDecision, ChampTypeDecision);
        string? parametreEdition = null, serieMinute = null;
        var typeConnu = false;
        await using (var r = await geste.LireAsync(
                         "SELECT R_TYPEDECISI_PARAMETRE_EDITION, R_TYPEDECISI_NUM_REPERTOIRE FROM v2.TYPE_DECISION WHERE R_TYPEDECISI_VALEUR = @t", ct,
                         new SqlParameter("@t", typeDecision)))
            if (await r.ReadAsync(ct))
            {
                typeConnu = true;
                parametreEdition = FormeLigne.Texte(r, "R_TYPEDECISI_PARAMETRE_EDITION") is { Length: > 0 } p ? p : null;
                serieMinute = FormeLigne.Texte(r, "R_TYPEDECISI_NUM_REPERTOIRE");
            }
        if (!typeConnu) return IssueGeste.Refuser($"« {ChampTypeDecision} » : {typeDecision} n'est pas un type de décision de la base du greffe.");
        if (parametreEdition is null) return IssueGeste.Refuser($"La décision {typeDecision} n'a d'édition paramétrée.");
        if (serieMinute != "JGOP")
            return IssueGeste.Refuser($"Le type de numéros pour l'affectation d'un numéros de minute est incorrect pour la décision {typeDecision}. " +
                                      "Le type de numéros paramétré doit être : n° de répertoire général des ordo. du président.");
        if (CreationV2.Date(S(SectionDecision, ChampDatePrevue)) is not { } datePrevue)
            return IssueGeste.Refuser($"« {ChampDatePrevue} » : date illisible ({S(SectionDecision, ChampDatePrevue)}).");
        if (datePrevue < dateActe) return IssueGeste.Refuser("La date prévue doit supérieure ou égale à la date de l'acte.");
        Juge? jugeDecision = null;
        if (S(SectionDecision, ChampJugeDecision).Length > 0 && (jugeDecision = await JugeSaisiAsync(geste, S(SectionDecision, ChampJugeDecision), ct)) is null)
            return IssueGeste.Refuser($"« {ChampJugeDecision} » : aucun juge de la base du greffe ne répond seul à « {S(SectionDecision, ChampJugeDecision)} ».");

        // 5. et 6. Les PC : dans la recherche, sans erreur ni attente ; les verrouillees sont sautees.
        var aTraiter = new List<Pc>();
        var bloquees = new List<string>();
        foreach (var id in ids)
        {
            if (Entier(id) is not { } n) return IssueGeste.Refuser($"Procédure collective introuvable dans la base du greffe ({id}).");
            string numero;
            await using (var r = await geste.LireAsync(RequetePc(suppleant), ct, new SqlParameter("@juge", ancien.Id), new SqlParameter("@id", n)))
            {
                if (!await r.ReadAsync(ct))
                    return IssueGeste.Refuser($"La PC {id} n'a pas {ancien.Nom} pour juge-commissaire {(suppleant ? "suppléant" : "titulaire")} en mission : elle n'est pas dans la recherche.");
                numero = FormeLigne.Texte(r, "NumPc");
                if (ErreursBloquantes(r).FirstOrDefault() is { } erreur) return IssueGeste.Refuser($"PC {numero} : {erreur}.");
                if (Convert.ToInt32(r["EnAttente"], CultureInfo.InvariantCulture) == 1) return IssueGeste.Refuser($"PC {numero} : {ErreurEnAttente}.");
            }
            if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Procédure collective", DepotsPcV2.SequenceurPc,
                    n.ToString(CultureInfo.InvariantCulture), ct) is { } verrou)
            {
                bloquees.Add($"{numero} - {verrou}");
                continue;
            }
            aTraiter.Add(await PcAsync(geste, n, ct));
        }
        if (aTraiter.Count == 0)
            return IssueGeste.Refuser((bloquees.Count == 1 ? "La procédure collective suivante est bloquée et ne sera pas traitée :" :
                                          "Les procédures collectives suivantes sont bloquées et ne seront pas traitées :") + " " + string.Join(" ; ", bloquees));

        // 7. Ce que la V3 ne reprend pas : les defendeurs hors de la PC, l'evenement de la PC, la facture.
        if (await EntitesLienPcAsync(geste, ct) is { } entitesHorsPc)
            return IssueGeste.Refuser($"Le lien « PC » (LIEN_CODEACTEPROCE_ENTITEPC) prend aussi les défendeurs {entitesHorsPc}, que la V3 ne reprend pas : faire {Objet} dans RIG v2.");
        foreach (var eve in parametres.Value.Evenements.Where(e => e.CodePc.Length > 0))
            return IssueGeste.Refuser($"L'événement {eve.CodePc} de la procédure collective, lié à l'événement {eve.Code} du code affaire {codeAffaire.Valeur}, " +
                                      $"n'est pas repris par RIG v3 : rien n'est enregistré, faire {Objet} dans RIG v2.");

        // ECRITURES. La demande du processus (_ValidationSauver :300-305), mere des demandes par instance.
        var fonction = suppleant ? "suppléant" : "titulaire";
        var titre = aTraiter.Count == 1 ? "Une ordonnance générée" : $"{aTraiter.Count} ordonnances générées";
        var (idMere, numeroMere, refusMere) = await EditionsV2.DemandeProcessusValideeAsync(geste, CodeProcessus,
            $"{titre} pour le changement du juge-commissaire {fonction} '{ancien.Nom}' par '{nouveau.Nom}'", ct);
        if (refusMere is not null) return IssueGeste.Refuser(refusMere);
        var parametre = await EditionsV2.ParametreAsync(geste, parametreEdition, ct);
        var signatureDemat = await SignatureDematAsync(geste, jugeDecision, codeAffaire.TypeInstance, ct);
        var auteur = await AuteurAsync(geste, ct);
        var traces = new List<TraceGeste>();
        var roles = new List<string>();
        var editions = 0;
        foreach (var pc in aTraiter.OrderBy(p => p.Numero, StringComparer.Ordinal))
        {
            // L'affaire, l'instance numerotee, le demandeur parametre et la PC en defendeur (Ordonnance.cs:250-272).
            var idAffaire = await geste.InsererAsync("v2.AFFAIRE", "AFFRE_ID_AFFRE", new Dictionary<string, object?> { ["AFFRE_DATE_CREATION"] = geste.Maintenant }, ct);
            var defendeurs = await DefendeursAsync(geste, pc, ct);
            var saisie = new CreationV2.InstanceSaisie(codeAffaire.Valeur, parametres.Value.Nature, "", parametres.Value.Acte, dateActe, dateActe,
                                                       geste.Maintenant.Date, null);
            var (instance, refusInstance) = await CreationV2.CreerInstanceAsync(geste, codeAffaire, saisie, idAffaire,
                new CreationV2.PartieSaisie(parametres.Value.Civilite, parametres.Value.Designation, ""), "D", defendeurs[0].Partie, "F", ct);
            if (instance is null) return IssueGeste.Refuser(refusInstance!);
            await geste.EcrireAsync("UPDATE v2.INSTANCE SET INSTN_ID_PRCLL = @pc WHERE INSTN_ID_INSTN = @i", ct,
                new SqlParameter("@pc", pc.Id), new SqlParameter("@i", instance.Id));
            await CompleterDefendeursAsync(geste, instance.Id, defendeurs, ct);

            // La decision (Ordonnance.cs:274-283 ; les valeurs du constructeur comme EcrivainMiseAuPointChambres).
            var numeroSourceDecision = Guid.NewGuid().ToString().ToUpperInvariant();
            var idDecision = await geste.InsererAsync("v2.DECISION_JUDICIAIRE", "DCJDC_ID_DCJDC", new Dictionary<string, object?>
            {
                ["DCJDC_ID_INSTN"] = instance.Id, ["DCJDC_NUM_ORDRE"] = (short)1, ["DCJDC_TYPE_DECISION"] = typeDecision, ["DCJDC_DATE_PREVUE"] = datePrevue,
                ["DCJDC_NUMERO_SOURCE"] = numeroSourceDecision, ["DCJDC_ID_JUG"] = jugeDecision?.Id,
                ["DCJDC_AVEC_PIECE_NUMERISE"] = true, ["DCJDC_NATURE_DOC_GED"] = S(SectionDecision, ChampNatureGed),
                ["DCJDC_TYPE_DEPOSANT_GED"] = "AUTRE", ["DCJDC_NOM_DEPOSANT_GED"] = S(SectionDecision, ChampDeposantGed),
                ["DCJDC_CAT_DECISION"] = "I", ["DCJDC_EXECUTION_PROVISOIRE"] = "0", ["DCJDC_RESSORT"] = "0", ["DCJDC_AVANT_DIRE_DROIT"] = "0",
                ["DCJDC_CONTRADICTOIRE"] = "0", ["DCJDC_ETAT_REDACTION"] = "0", ["DCJDC_ETAT_DIFFUSION"] = "0", ["DCJDC_ETAT_STABILITE"] = "0",
                ["DCJDC_ETAT_VALIDITE"] = "0", ["DCJDC_POST_DECISION"] = false, ["DCJDC_REDIGEE_GREFFE"] = true,
                ["DCJDC_COPIE_EXECUTOIRE_DEMANDEUR"] = false, ["DCJDC_DECISION_PUBLIQUE"] = false, ["DCJDC_DEBAT_PUBLIC"] = false,
            }, ct);

            // Les evenements de creation du code affaire, survenus au jour, et leurs editions ; la facture d'une PC hors forfait.
            var inst = new EditionsEvenementV2.Instance(instance.Id, instance.NumRole, null, parametres.Value.Nature, codeAffaire.NatAffPart, parametres.Value.Acte);
            var aDeposer = new List<EditionsEvenementV2.Edition>();
            foreach (var eve in parametres.Value.Evenements)
            {
                if (!pc.Forfait && await EvenementsProcedureV2.FactureNonRepriseAsync(geste, inst, eve.Code, Objet, ct) is { } facture)
                    return IssueGeste.Refuser(facture);
                var ecrits = await EvenementsProcedureV2.SurvenuAsync(geste, instance.Id, eve.Code, geste.Maintenant.Date, avecFutur: true, ct);
                var calcul = await EditionsEvenementV2.CalculerAsync(geste, inst, ecrits.Evenements[0], Objet, ct);
                if (calcul.NonRepris is { } nonRepris) return IssueGeste.Refuser(nonRepris);
                aDeposer.AddRange(calcul.Editions);
            }

            // La demande de l'instance, validee, fille de celle du processus, liee a la PC (Instance.Demande_LesNumeros, Instance.cs:425-433).
            var numeros = $"{instance.NumRole} {instance.NumRepGeneral} {pc.Numero}".Trim();
            var (idDemande, numeroDemande, refusDemande) = await EditionsV2.DemandeValideeAsync(geste, CodeProcessus,
                new EditionsV2.ObjetDemande(idAffaire, instance.NomCourant, instance.NumRole, numeros, VerrouDemandeV2.SequenceurInstance,
                                            instance.Id.ToString(CultureInfo.InvariantCulture)), ct);
            if (refusDemande is not null) return IssueGeste.Refuser(refusDemande);
            await EditionsV2.LierAsync(geste, idDemande, idMere, [(DepotsPcV2.SequenceurPc, pc.Id.ToString(CultureInfo.InvariantCulture))], ct);
            foreach (var e in aDeposer) await EditionsEvenementV2.DeposerAsync(geste, idDemande, e, ct);
            if (parametre is not null)
            {
                var idEdition = await EditionsV2.DemandeEditionAsync(geste, idDemande, parametre, null, "INSTANCE", numeroSourceDecision, null, null, ct);
                if (signatureDemat)
                    await geste.EcrireAsync("UPDATE v2.DEMANDE_EDITION SET DMNED_IMPRESSION = 0 WHERE DMNED_ID_DMNED = @e", ct, new SqlParameter("@e", idEdition));
            }
            editions += aDeposer.Count + (parametre is null ? 0 : 1);

            // Le changement en attente (PROC_CHJCM.cs:252-261).
            await geste.InsererAsync("v2.ETRE_JUGE_COMM_EN_ATTENTE", "ETJCMA_ID_ETJCMA", new Dictionary<string, object?>
            {
                ["ETJCMA_ID_DCJDC"] = idDecision, ["ETJCMA_ID_PRCLL"] = pc.Id, ["ETJCMA_ID_JUG_A_REMPLACER"] = ancien.Id, ["ETJCMA_ID_JUG_NOUVEAU"] = nouveau.Id,
                ["ETJCMA_TYPE_CHANGEMENT_JUGE"] = suppleant ? "JCS" : "JCT", ["ETJCMA_DATE_CREATION"] = geste.Maintenant, ["ETJCMA_AUTEUR_CREATION"] = auteur,
            }, ct);
            roles.Add(instance.NumRole);
            traces.Add(new TraceGeste("affaire", $"l'ordonnance du rôle {instance.NumRole} (PC {pc.Numero})", instance.Id.ToString(CultureInfo.InvariantCulture),
                "a cree", null, new
                {
                    Pc = pc.Numero, Role = instance.NumRole, Repertoire = instance.NumRepGeneral, Decision = typeDecision, DatePrevue = FormeLigne.Date(datePrevue),
                    JugeARemplacer = ancien.Nom, NouveauJuge = nouveau.Nom, Fonction = suppleant ? "JCS" : "JCT", Demande = numeroDemande, DemandeMere = numeroMere,
                }));
        }

        var message = $"{titre} pour le changement du juge-commissaire {fonction} '{ancien.Nom}' par '{nouveau.Nom}' : " +
                      $"rôle{(roles.Count > 1 ? "s" : "")} {string.Join(", ", roles)} ; changement de juge en attente de l'ordonnance signée" +
                      (editions == 0 ? "" : editions == 1 ? " ; 1 édition dans la file des éditions" : $" ; {editions} éditions dans la file des éditions") + "." +
                      (bloquees.Count == 0 ? "" : $" Non traitée{(bloquees.Count > 1 ? "s" : "")}, bloquée{(bloquees.Count > 1 ? "s" : "")} : {string.Join(" ; ", bloquees)}.") +
                      " Les mots directeurs et le rapport Excel ne sont pas repris par la V3.";
        return IssueGeste.Ecrit(message, [.. aTraiter.Select(p => p.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }

    private static int? Entier(string s) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    private sealed record Evenement(string Code, string CodePc);

    private readonly record struct Parametres(CreationV2.CodeAffaire Code, string Nature, string Acte, string Civilite, string Designation,
                                              IReadOnlyList<Evenement> Evenements);

    /// <summary>VerifierParametrage (ChangementJugeParametres.cs:160-277), aux messages de la V2 ; un parametre envoye par l'ecran
    /// different du reglage est refuse (l'etape Paramètres de la V2 les sauve, non reprise).</summary>
    private static async Task<(Parametres? Valeurs, string? Refus)> ParametresAsync(GesteEnCours geste, CancellationToken ct)
    {
        var reglages = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var r = await geste.LireAsync("SELECT Code, Valeur FROM soc.Reglage WHERE Code IN (@a, @b, @c, @d)", ct,
                         new SqlParameter("@a", ParamCivilite), new SqlParameter("@b", ParamDesignation), new SqlParameter("@c", ParamCodeAffaire),
                         new SqlParameter("@d", ParamModeFactu)))
            while (await r.ReadAsync(ct)) reglages[FormeLigne.Texte(r, "Code")] = FormeLigne.Texte(r, "Valeur");
        // fix-ok: mesure le 28/11/2026 (dotnet msbuild -t:Compile, CS0136) -- la boucle nommait « code » comme le code affaire plus bas.
        foreach (var reglage in new[] { ParamCivilite, ParamDesignation, ParamCodeAffaire, ParamModeFactu })
            if (!reglages.ContainsKey(reglage)) return (null, $"Le paramètre '{reglage}' n'existe pas dans la table 'PARAMETRAGE'.");
        var civilite = reglages[ParamCivilite];
        var designation = reglages[ParamDesignation];
        var code = reglages[ParamCodeAffaire];
        var mode = reglages[ParamModeFactu];
        if (CreationV2.Saisi(geste, SectionParametres, ChampCodeAffaire) is { Length: > 0 } codeEnvoye && codeEnvoye != code
            || CreationV2.Saisi(geste, SectionParametres, ChampModeFacturation) is { Length: > 0 } modeEnvoye && modeEnvoye != mode)
            return (null, "Les paramètres du processus (code affaire, mode de facturation) se changent dans l'étape Paramètres de RIG v2 : la V3 prend ceux du greffe.");
        if (civilite.Length == 0) return (null, "La civilité du demandeur n'est pas paramétré dans le processus 'PARAMJUD'.");
        await using (var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.CIVILITE WHERE R_CIVILITE_VALEUR = @c", ct, new SqlParameter("@c", civilite)))
            if (await r.ReadAsync(ct) && r.GetInt32(0) == 0) return (null, "La civilité du demandeur n'existe pas dans la table de référence 'CIVILITE'.");
        if (designation.Length == 0) return (null, "La désignation du demandeur n'est pas paramétré dans le processus 'PARAMJUD'.");
        if (code.Length == 0) return (null, "Le code affaire n'est pas paramétré dans le processus 'CHJCM'.");
        string nature = "", acte = "", evenements = "";
        await using (var r = await geste.LireAsync("""
            SELECT R_CODEAFFAIR_NATURE_AFF, R_CODEAFFAIR_CODE_ACTE_PRO, R_CODEAFFAIR_CODE_EVE_PRO_CREA FROM v2.CODE_AFFAIRE WHERE R_CODEAFFAIR_VALEUR = @c
            """, ct, new SqlParameter("@c", code)))
            if (await r.ReadAsync(ct))
            {
                nature = FormeLigne.Texte(r, "R_CODEAFFAIR_NATURE_AFF");
                acte = FormeLigne.Texte(r, "R_CODEAFFAIR_CODE_ACTE_PRO");
                evenements = FormeLigne.Texte(r, "R_CODEAFFAIR_CODE_EVE_PRO_CREA");
            }
        if (await CreationV2.CodeAffaireAsync(geste, code, acte, ct) is not { } codeAffaire)
            return (null, $"Le code affaire '{code}' n'existe pas dans la table de référence 'CODE_AFFAIRE.'");
        if (codeAffaire.TypeInstance != "RP")
            return (null, $"Le type d'instance '{codeAffaire.TypeInstance}' paramétré dans le code affaire '{code}' n'est pas une ordonnance du président.");
        if (nature.Length == 0) return (null, $"Il n'y a pas de nature d'affaire paramétré pour le code affaire '{code}'.");
        await using (var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.NATURE_AFFAIRE WHERE R_NATUREAFFA_VALEUR = @n", ct, new SqlParameter("@n", nature)))
            if (await r.ReadAsync(ct) && r.GetInt32(0) == 0) return (null, $"La nature d'affaire '{nature}' n'existe pas dans la table de référence 'NATURE_AFFAIRE'.");
        if (acte.Length == 0) return (null, $"Il n'y a pas de code acte de saisine paramétré pour le code affaire '{code}'.");
        // Une saisine d'office : l'ISO de l'acte vaut « 20 » (CODE_ACTE_PROCE.SaisineOffice, TableRef\CODE_ACTE_PROCE.cs:45-49).
        bool? office = null;
        await using (var r = await geste.LireAsync("SELECT R_CODEACTEPR_ISO FROM v2.CODE_ACTE_PROCE WHERE R_CODEACTEPR_VALEUR = @a", ct,
                         new SqlParameter("@a", acte)))
            if (await r.ReadAsync(ct)) office = FormeLigne.Texte(r, "R_CODEACTEPR_ISO") == "20";
        if (office is null) return (null, $"Le code acte de saisine '{acte}' n'existe pas dans la table de référence 'CODE_ACTE_PROCE'.");
        if (office == false)
            return (null, $"Le code acte de saisine '{acte}' paramétré dans le code affaire '{code}' n'est pas une saisine d'office.");
        // Les evenements de creation : ceux du code (separes par « ; ») qui existent (CodesEveProCreations, CODE_AFFAIRE.cs:282-299).
        var lus = new List<Evenement>();
        foreach (var eve in evenements.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            await using var r = await geste.LireAsync("""
                SELECT R_CODEEVEPRO_AVEC_DEPOT, R_CODEEVEPRO_AVEC_PIECE_NUMERISE_INSMES, R_CODEEVEPRO_CODE_EVE_PRO_PC FROM v2.CODE_EVE_PRO
                WHERE R_CODEEVEPRO_VALEUR = @e
                """, ct, new SqlParameter("@e", eve));
            if (!await r.ReadAsync(ct)) continue;
            if (r["R_CODEEVEPRO_AVEC_DEPOT"] is true)
                return (null, $"Le code événement de procédure '{eve}' est paramétré avec un dépôt, cela n'est pas géré dans le processus.");
            if (r["R_CODEEVEPRO_AVEC_PIECE_NUMERISE_INSMES"] is true)
                return (null, $"Le code événement de procédure '{eve}' est paramétré avec pièce numérisée sur l'instance, cela n'est pas géré dans le processus.");
            lus.Add(new Evenement(eve, FormeLigne.Texte(r, "R_CODEEVEPRO_CODE_EVE_PRO_PC")));
        }
        if (mode is not ("T" or "F")) return (null, $"Le mode de facturation {mode} n'est pas valide.");
        return (new Parametres(codeAffaire, nature, acte, civilite, designation, lus), null);
    }

    private static async Task<List<Juge>> JugesAsync(GesteEnCours geste, string sql, CancellationToken ct, SqlParameter[] parametres)
    {
        var juges = new List<Juge>();
        await using var r = await geste.LireAsync(sql, ct, parametres);
        while (await r.ReadAsync(ct))
            juges.Add(new Juge(Convert.ToInt32(r["JUG_ID_JUG"], CultureInfo.InvariantCulture),
                // NomCompletSansCivilite (Juge.cs:48-52) : le nom puis le prenom.
                $"{FormeLigne.Texte(r, "JUG_NOM")} {FormeLigne.Texte(r, "JUG_PRENOM")}".Trim()));
        return juges;
    }

    /// <summary>Un juge saisi : son abrege exact, sinon un seul juge dont le nom ou le prenom le contient ; null sinon.</summary>
    private static async Task<Juge?> JugeSaisiAsync(GesteEnCours geste, string saisi, CancellationToken ct)
    {
        var parAbrege = await JugesAsync(geste, "SELECT JUG_ID_JUG, JUG_NOM, JUG_PRENOM FROM v2.JUGE WHERE JUG_ABREGE = @s", ct, [new SqlParameter("@s", saisi)]);
        if (parAbrege.Count == 1) return parAbrege[0];
        var parNom = await JugesAsync(geste, "SELECT JUG_ID_JUG, JUG_NOM, JUG_PRENOM FROM v2.JUGE WHERE JUG_NOM LIKE @s OR JUG_PRENOM LIKE @s", ct,
            [new SqlParameter("@s", FormeLigne.Contient(saisi))]);
        return parNom.Count == 1 ? parNom[0] : null;
    }

    private static async Task<Pc> PcAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT p.PRCLL_ID_PRCLL, p.PRCLL_NUM_PC, p.PRCLL_NOM_AFFAIRE, p.PRCLL_NOM_DEBITEUR_PRINCIPAL, p.PRCLL_FORFAIT_FACTURE, p.PRCLL_ID_RPRTR,
                   p.PRCLL_ID_ADRBS, p.PRCLL_ID_DSRCS, p.PRCLL_ID_NINSC
            FROM v2.PROCEDURE_COLLECTIVE AS p WITH (UPDLOCK) WHERE p.PRCLL_ID_PRCLL = @id
            """, ct, new SqlParameter("@id", id));
        await r.ReadAsync(ct);
        int? E(string c) => r[c] is DBNull ? null : Convert.ToInt32(r[c], CultureInfo.InvariantCulture);
        return new Pc(id, FormeLigne.Texte(r, "PRCLL_NUM_PC"), FormeLigne.Texte(r, "PRCLL_NOM_AFFAIRE"),
            FormeLigne.Texte(r, "PRCLL_NOM_DEBITEUR_PRINCIPAL") is { Length: > 0 } d ? d : null, r["PRCLL_FORFAIT_FACTURE"] is true,
            E("PRCLL_ID_RPRTR"), E("PRCLL_ID_ADRBS"), E("PRCLL_ID_DSRCS"), E("PRCLL_ID_NINSC"));
    }

    /// <summary>Les entites du lien « PC » autres que la PC elle-meme (ListeEntitesPC, LIEN_CODEACTEPROCE_ENTITEPC.cs:14-26 : la liste,
    /// plus le code) ; null si le lien ne prend que la PC (ou si la base n'a pas la table : la liste par defaut, le code seul).</summary>
    private static async Task<string?> EntitesLienPcAsync(GesteEnCours geste, CancellationToken ct)
    {
        await using (var t = await geste.LireAsync("SELECT OBJECT_ID(N'v2.LIEN_CODEACTEPROCE_ENTITEPC')", ct))
            if (!await t.ReadAsync(ct) || t.IsDBNull(0)) return null;
        await using var r = await geste.LireAsync("SELECT LCAPE_LST_ENTITE_PC FROM v2.LIEN_CODEACTEPROCE_ENTITEPC WHERE LCAPE_CODE = 'PC'", ct);
        if (!await r.ReadAsync(ct)) return null;
        var autres = FormeLigne.Texte(r, "LCAPE_LST_ENTITE_PC").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(e => e != "PC").ToArray();
        return autres.Length == 0 ? null : string.Join(", ", autres);
    }

    private sealed record Defendeur(CreationV2.PartieSaisie Partie, int? IdEntreprise, int? IdAdresse, int? IdDossierRcs, int? IdNonInscrit);

    /// <summary>Les defendeurs pris de la PC (GetPartiesPossiblesFromPC, Instance.cs:1430-1490) : la PC, puis ses extensions.</summary>
    private static async Task<List<Defendeur>> DefendeursAsync(GesteEnCours geste, Pc pc, CancellationToken ct)
    {
        var civilites = new List<(string Developpe, string Simplifie)>();
        await using (var r = await geste.LireAsync("SELECT R_CIVILITE_DEVELOPPE_EDIT, R_CIVILITE_SIMPLIFIE_EDIT FROM v2.CIVILITE", ct))
            while (await r.ReadAsync(ct)) civilites.Add((FormeLigne.Texte(r, "R_CIVILITE_DEVELOPPE_EDIT"), FormeLigne.Texte(r, "R_CIVILITE_SIMPLIFIE_EDIT")));
        // La designation sans sa civilite (Instance.cs:1449-1455) : la premiere civilite qui la commence, developpee puis simplifiee.
        string SansCivilite(string designation)
        {
            foreach (var (developpe, simplifie) in civilites)
            {
                if (developpe.Length > 0 && designation.StartsWith(developpe, StringComparison.Ordinal)) return designation[developpe.Length..].Trim();
                if (simplifie.Length > 0 && designation.StartsWith(simplifie, StringComparison.Ordinal)) return designation[simplifie.Length..].Trim();
            }
            return designation;
        }
        async Task<(string Civilite, string Siren)> EntrepriseAsync(int? id)
        {
            if (id is not { } n) return ("PM", "");
            await using var r = await geste.LireAsync("SELECT Civilite, Siren FROM rec.Entreprise WHERE Id = @id", ct, new SqlParameter("@id", n));
            return await r.ReadAsync(ct) ? (FormeLigne.Texte(r, "Civilite"), FormeLigne.Texte(r, "Siren")) : ("PM", "");
        }
        var extensions = new List<(int? Rprtr, string Nom, int? Adresse, int? Dossier, int? NonInscrit)>();
        await using (var t = await geste.LireAsync("SELECT OBJECT_ID(N'v2.DEBITEUR_AUTRE')", ct))
            if (await t.ReadAsync(ct) && !t.IsDBNull(0))
            {
                await t.CloseAsync();
                await using var r = await geste.LireAsync("""
                    SELECT DBTAT_ID_RPRTR, DBTAT_NOM_DEBITEUR, DBTAT_ID_ADRBS, DBTAT_ID_DSRCS, DBTAT_ID_NINSC FROM v2.DEBITEUR_AUTRE
                    WHERE DBTAT_ID_PRCLL = @pc ORDER BY DBTAT_NUM_ORDRE, DBTAT_ID_DBTAT
                    """, ct, new SqlParameter("@pc", pc.Id));
                int? E(SqlDataReader x, string c) => x[c] is DBNull ? null : Convert.ToInt32(x[c], CultureInfo.InvariantCulture);
                while (await r.ReadAsync(ct))
                    extensions.Add((E(r, "DBTAT_ID_RPRTR"), FormeLigne.Texte(r, "DBTAT_NOM_DEBITEUR"), E(r, "DBTAT_ID_ADRBS"), E(r, "DBTAT_ID_DSRCS"), E(r, "DBTAT_ID_NINSC")));
            }
        var (civilitePc, sirenPc) = await EntrepriseAsync(pc.IdEntreprise);
        var designationPc = extensions.Count > 0 ? pc.DebiteurPrincipal ?? "" : pc.NomAffaire;
        var defendeurs = new List<Defendeur>
        {
            new(new CreationV2.PartieSaisie(civilitePc, SansCivilite(designationPc), sirenPc), pc.IdEntreprise, pc.IdAdresse, pc.IdDossierRcs, pc.IdNonInscrit),
        };
        foreach (var x in extensions)
        {
            var (civilite, siren) = await EntrepriseAsync(x.Rprtr);
            defendeurs.Add(new(new CreationV2.PartieSaisie(civilite, SansCivilite(x.Nom), siren), x.Rprtr, x.Adresse, x.Dossier, x.NonInscrit));
        }
        return defendeurs;
    }

    /// <summary>Le premier defendeur (ecrit par CreerInstanceAsync) recoit ses liens ; les extensions s'ajoutent, numerotees a la suite
    /// (Partie.CreatePartie(retour.Count + 1), Instance.cs:1438, :1460).</summary>
    private static async Task CompleterDefendeursAsync(GesteEnCours geste, int idInstance, List<Defendeur> defendeurs, CancellationToken ct)
    {
        await geste.EcrireAsync("""
            UPDATE v2.PARTIE SET PRT_ID_RPRTR = @rprtr, PRT_ID_ADRBS = @adresse, PRT_ID_DSRCS = @dossier, PRT_ID_NINSC = @ninsc
            WHERE PRT_ID_INSTN = @i AND PRT_QUALIF_PARTIE = 'F'
            """, ct, new SqlParameter("@rprtr", (object?)defendeurs[0].IdEntreprise ?? DBNull.Value),
            new SqlParameter("@adresse", (object?)defendeurs[0].IdAdresse ?? DBNull.Value), new SqlParameter("@dossier", (object?)defendeurs[0].IdDossierRcs ?? DBNull.Value),
            new SqlParameter("@ninsc", (object?)defendeurs[0].IdNonInscrit ?? DBNull.Value), new SqlParameter("@i", idInstance));
        for (var k = 1; k < defendeurs.Count; k++)
        {
            var d = defendeurs[k];
            await CreationV2.InsererAsync(geste, "v2.PARTIE", "PRT_ID_PRT", new Dictionary<string, object?>
            {
                ["PRT_ID_INSTN"] = idInstance, ["PRT_NUM_ORDRE"] = (short)(k + 1), ["PRT_CIVILITE"] = d.Partie.Civilite, ["PRT_DESIGNATION"] = d.Partie.Designation,
                ["PRT_NOM"] = d.Partie.Designation, ["PRT_SIREN"] = d.Partie.Siren.Length > 0 ? d.Partie.Siren : null, ["PRT_QUALIF_PARTIE"] = "F",
                ["PRT_EDITION_JUGEMENT"] = "D", ["PRT_MANDATAIRE_EDITION_JUGEMENT"] = "D", ["PRT_MANDATAIRE_PLAI_EDITION_JUGEMENT"] = "D",
                ["PRT_ID_RPRTR"] = d.IdEntreprise, ["PRT_ID_ADRBS"] = d.IdAdresse, ["PRT_ID_DSRCS"] = d.IdDossierRcs, ["PRT_ID_NINSC"] = d.IdNonInscrit,
            }, ct);
        }
    }

    /// <summary>Le juge de la decision signe en dematerialise ce type d'instance (JUG_SIGNATURE_DEMAT_TYPE_INSTANCES, Ordonnance.cs:96-98).</summary>
    private static async Task<bool> SignatureDematAsync(GesteEnCours geste, Juge? juge, string typeInstance, CancellationToken ct)
    {
        if (juge is null || !(await EditionsV2.ColonnesAsync(geste, "v2.JUGE", ct)).Contains("JUG_SIGNATURE_DEMAT_TYPE_INSTANCES")) return false;
        await using var r = await geste.LireAsync("SELECT JUG_SIGNATURE_DEMAT_TYPE_INSTANCES FROM v2.JUGE WHERE JUG_ID_JUG = @j", ct, new SqlParameter("@j", juge.Id));
        return await r.ReadAsync(ct) && FormeLigne.Texte(r, "JUG_SIGNATURE_DEMAT_TYPE_INSTANCES").Contains(typeInstance, StringComparison.Ordinal);
    }

    /// <summary>L'auteur du changement en attente : « nom prenom » de l'utilisateur (EtreJugeCommEnAttente.cs:131), le nom affiche de
    /// l'agent en V3 ; « (inconnu) » sans nom.</summary>
    private static async Task<string> AuteurAsync(GesteEnCours geste, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT Nom, Prenom FROM soc.Compte WHERE CompteWindows = @c", ct, new SqlParameter("@c", geste.Agent.CompteWindows));
        var nom = await r.ReadAsync(ct) ? $"{FormeLigne.Texte(r, "Nom")} {FormeLigne.Texte(r, "Prenom")}".Trim() : "";
        return nom.Length > 0 ? (nom.Length > 128 ? nom[..128] : nom) : "(inconnu)";
    }
}
