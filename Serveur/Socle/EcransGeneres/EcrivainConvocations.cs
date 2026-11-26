using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Citer par huissier » des « Convocations » (PROC_GCONV) sur les instances cochees du cabinet (LecteurConvocations.cs : une
/// ligne = instance-demande d'edition-relance-exercice). Mode d'emploi : EcrivainEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 7,
/// 26/11/2026 ; editions de la citation le 27/11/2026.
///
/// SOURCE (ancien RIG, Source\RIG\DLL\Processus\PROC_GCONV\PROC_GCONV\ETP_CITATIONS\OPE_CITATIONS.cs ; proc_gconv.md, Detail OPE) :
///   - LES LIGNES : une LRAR revenue « RECEPTION » ou « NPAI » est grisee, non cochable (:94-95, TreeViewAudience.cs:286-287) ;
///   - LES CONTROLES (Verifier, :251-320), dans l'ordre : la maquette du code de citation (SURV_CEP_CITATION_HUISS, « CITATION » par
///     defaut, Surveillances.cs:423-425) doit avoir un destinataire de type huissier (CEP_EDITION.CEPED_DESTI_MODE_ENVOI, sauts de
///     ligne lus comme « ; », vide = « DI » ; « H », « H,... » ou « HUI ») ; l'huissier est obligatoire pour chaque instance cochee ;
///     une instance cochee deux fois est refusee (doublons de numero de role). Messages au mot pres ;
///   - L'HUISSIER : saisi une fois pour les lignes cochees -- RIG v2 le recopie de la ligne racine sur les lignes vides
///     (TreeViewAudience.cs:186-201) ; un identifiant numerique est un intervenant (INTERVENANT_PC_REPER), sinon un destinataire
///     institutionnel (DTINS_CODE) (:347-349). La liste de RIG v2 ne propose que ceux de la base : un autre est refuse ;
///   - LA DEMANDE (DoPreparationObjetsBeforeOperation, :241-249 ; createDemande, :500-505) : une demande « GCONV instance <role> » par
///     instance, au code GCONV, numerotee dans la serie DMD, sans affaire (Demande.CreateDemande(nom, indicatif, code, cnx),
///     Demande.cs:546 ; lu dans RIG_DEV le 27/11/2026 : DMND_CODE_PROSS = 'GCONV', DMND_ID_AFFRE NULL), avec le role et le dossier RCS ;
///     validee a la sauvegarde (Cloturer("V"), :330), donc ecrite a l'etat « V » ;
///   - L'EVENEMENT (:245, Sauve :323-332) : un evenement de procedure SURVENU au code de citation, date du jour, ajoute a l'instance
///     (EvenementsProcedureV2.SurvenuAsync, sans futur : la citation n'en cree pas) ;
///   - LES EDITIONS (GenererLesEditions, :334-459), dans la file des editions (EditionsEvenementV2) : les editions de l'evenement
///     (CreateDemandeEdition, :359), rattachees a la demande ; l'evenement n'est pas encore sauve quand elles se calculent
///     (DoGenererLesEditions avant DoSauver, :519-522), d'ou « (EP sans n° ordre) » dans leur libelle source -- c'est ce qu'ont les
///     editions d'une citation de RIG_DEV (demandes d'edition 4448155 a 4448158, lues le 27/11/2026). Puis, pour chacune : un
///     huissier INTERVENANT s'ajoute coche quand l'edition vise un huissier (« INST_H » coche, :367-381) ; tout autre huissier coche
///     est decoche (:384-386) ; un huissier qui a un nom de coffre fait passer l'edition au coffre, sans impression (:388-391) ; la case
///     « Éditer le Kbis » ajoute, POUR CHAQUE edition, un KBIS du dossier RCS (reglage LOT1_KBISXML_1_PDF, type RIG_LOT_UN, source
///     DOSSIER_RCS, imprime, memes destinataires, au coffre si l'edition l'est, :436-457).
///
/// CE QUE LA V3 NE REPREND PAS (nomme, rien en silence, avec la raison) :
///   - ce que EditionsEvenementV2 ne calcule pas (son en-tete) : une maquette qui en a besoin REFUSE la citation, avec son nom ;
///   - la COPIE CONFORME de l'ordonnance (:397-433) : RIG v2 la tire de la GED (DemandeEdition.CreateDemandeEditionPieceNumerisee,
///     DemandeEdition.cs:2608-2660 : GedRig.CreateGedRigFromVidiged), que la V3 ne reprend pas ; RIG v2 cite sans elle quand elle
///     manque (simple avertissement, :274-284) : la citation s'enregistre et le message le dit ;
///   - la FACTURE de la diligence (GenererFacture, :214-238 ; son edition LOT2_FACTURE, :461-489) : les articles de l'evenement
///     (EvenementProcedure.CreateArticles, EvenementProcedure.cs:2443-2493), le client tire des parties (Client.CreateClientInstance)
///     et la facture (Facture.CreateFactureSimpleFactureOuTarife, Facture.cs:162) sont le moteur de facturation, que la V3 n'a pas ;
///     RIG v2 cite meme quand la facture echoue (erreur avalee, courriel a la hotline, :232-236) : le message le dit ; ses controles
///     (dossier RCS, premier dirigeant, :286-304) ne gardent qu'elle ;
///   - l'adresse d'un huissier INTERVENANT (interv.Adresse.AdresseMultiLignesPourEdition, :352) : la V3 ne recompose pas l'adresse
///     d'edition d'un intervenant ; son destinataire s'ajoute sans adresse ;
///   - l'arrivee et la ligne du rapport general de la demande (Demande.cs:480, :512) : la V3 ne les ecrit pour aucune demande
///     (CreationV2.CreerDemandeAsync).
/// </summary>
internal sealed class EcrivainConvocations : IEcrivainEcran
{
    public string Procedure => "PROC_GCONV";

    public string Ecran => "Convocations";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Decider];

    /// <summary>Le geste (le Valider du descripteur, Surveiller.cs) et ses parametres, au mot pres. La case Kbis est
    /// CheckBoxEditerKbis de RIG v2 (OPE_CITATIONS.cs:436).</summary>
    internal const string Citer = "Citer par huissier", ParametreHuissier = "Huissier", ParametreKbis = "Éditer le Kbis";

    /// <summary>Ce que RIG v2 produit aussi a la citation et que la V3 ne fait pas (voir l'en-tete) : le message du geste le dit.</summary>
    // fix-ok: controle du lot 7 (26/11) puis (27/11) -- la raison donnee (« pas de moteur d'editions ») etait fausse, puis les editions
    // de la citation manquaient alors que la V3 ecrit des demandes d'edition : elles sont faites (EditionsEvenementV2) ; restent la copie
    // conforme (GED) et la facture (moteur de facturation), que RIG v2 sait lui-meme omettre (:274-284, :232-236).
    internal const string NonProduites =
        "La copie conforme de l'ordonnance et la facture ne sont pas produites : RIG v2 tire la copie de la GED et facture les articles "
        + "de la citation par son moteur de facturation, que RIG v3 ne reprend pas.";

    /// <summary>Le reglage d'edition du KBIS de la citation (OPE_CITATIONS.cs:437).</summary>
    internal const string ReglageKbis = "LOT1_KBISXML_1_PDF";

    private sealed record Ligne(int IdInstance, string Role, string? TypeDate, int? IdDossier, EditionsEvenementV2.Instance Instance);

    private sealed record Huissier(string Nom, bool EstIntervenant, string Adresse, string Email, string NomCoffre);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var d = geste.Demande;
        if (d.Nom != Citer) return IssueGeste.Refuser($"Le geste « {d.Nom} » n'existe pas aux convocations de RIG v2.");
        if (d.Ids.Length == 0) return IssueGeste.Refuser("Cochez au moins une instance.");
        var lignes = new List<Ligne>();
        foreach (var id in d.Ids)
        {
            if (await LireLigneAsync(geste, id, ct) is not { } l) return IssueGeste.Refuser($"L'instance « {id} » n'est pas une convocation de la base du greffe.");
            if (l.TypeDate is "RECEPTION" or "NPAI")
                return IssueGeste.Refuser($"La LRAR de l'instance {l.Role} est revenue ({l.TypeDate}) : sa ligne est grisée, elle ne se cite pas.");
            lignes.Add(l);
        }

        // Verifier (OPE_CITATIONS.cs:251-320).
        var code = await CodeCitationAsync(geste, ct);
        if (!await MaquetteAvecHuissierAsync(geste, code, ct))
            return IssueGeste.Refuser("Il faut paramétrer un destinataire de type H sur la maquette du code événement CITATION.");
        var saisi = d.Valeurs.TryGetValue(ParametreHuissier, out var h) ? h.Trim() : "";
        if (saisi.Length == 0) return IssueGeste.Refuser("La saisie de l'huissier est obligatoire pour l'instance " + lignes[0].Role);
        var doublons = lignes.GroupBy(l => l.Role).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (doublons.Count > 0)
        {
            var liste = string.Join(";", doublons);
            return IssueGeste.Refuser(doublons.Count > 1 ? $"Les instances suivantes {liste} ont été cochées plusieurs fois."
                                                         : $"L'instance suivante {liste} a été cochée plusieurs fois.");
        }
        if (await HuissierAsync(geste, saisi, ct) is not { } huissier)
            return IssueGeste.Refuser($"L'huissier « {saisi} » n'est pas dans la base du greffe.");
        var kbis = d.Valeurs.TryGetValue(ParametreKbis, out var k) && k == "oui";
        (SortedDictionary<string, string> Parametres, string CodeDocument, bool Impression)? reglageKbis = null;
        if (kbis)
        {
            reglageKbis = await EditionsEvenementV2.ReglageEditionAsync(geste, ReglageKbis, ct);
            if (reglageKbis is null) return IssueGeste.Refuser($"Le réglage d'édition {ReglageKbis} du Kbis n'est pas dans la base du greffe.");
            // RIG v2 lit node.Dossier sans garde (:442) ; son message pour un dossier introuvable (:301).
            if (lignes.FirstOrDefault(l => l.IdDossier is null) is { } sansDossier)
                return IssueGeste.Refuser("La récupération du dossier RCS pour l'instance " + sansDossier.Role + " a échoué.");
        }

        // DoPreparationObjetsBeforeOperation, GenererLesEditions puis Sauve (:241-249, :334-459, :323-332).
        var traces = new List<TraceGeste>();
        var editions = 0;
        foreach (var l in lignes)
        {
            var (numero, refus) = await CreationV2.NumeroAsync(geste, "DMD", geste.Maintenant.Year, ct);
            if (numero is null) return IssueGeste.Refuser(refus!);
            var idDemande = await CreationV2.InsererAsync(geste, "v2.DEMANDE", "DMND_ID_DMND", new Dictionary<string, object?>
            {
                ["DMND_NUM_DEMANDE"] = numero, ["DMND_ETAT_DEMANDE"] = "V", ["DMND_CODE_PROSS"] = "GCONV", ["DMND_DATE_CREATION"] = geste.Maintenant,
                ["DMND_EN_COURS"] = false, ["DMND_NOM_UTILISATEUR"] = geste.Agent.NomAffiche, ["DMND_NOM"] = "GCONV instance " + l.Role,
                ["DMND_NUM_INDICATIF"] = l.Role, ["DMND_LES_NUMEROS"] = l.Role, ["DMND_ID_DSRCS"] = l.IdDossier,
            }, ct);

            var ecrits = await EvenementsProcedureV2.SurvenuAsync(geste, l.IdInstance, code, geste.Maintenant.Date, avecFutur: false, ct);
            var calcul = await EditionsEvenementV2.CalculerAsync(geste, l.Instance,
                new EditionsEvenementV2.Evenement(code, ecrits.NumeroSource, geste.Maintenant.Date, null, await AvecPieceAsync(geste, ecrits.IdSurvenu, ct)), ct);
            if (calcul.NonRepris is { } nonRepris) return IssueGeste.Refuser(nonRepris);

            var ids = new List<int>();
            foreach (var e in calcul.Editions)
            {
                AjusterHuissier(e, huissier);
                var depot = await EditionsEvenementV2.DeposerAsync(geste, idDemande, e, ct);
                ids.Add(depot.Id);
                if (reglageKbis is not { } rk) continue;
                var edKbis = new EditionsEvenementV2.Edition
                {
                    Parametres = new SortedDictionary<string, string>(rk.Parametres, StringComparer.Ordinal), Destinataires = e.Destinataires,
                    Impression = true, TypeGeneration = "RIG_LOT_UN", LibelleImpose = "KBIS",
                };
                edKbis.Colonnes["DMNED_CODE_ENTITE_SOURCE"] = "DOSSIER_RCS";
                edKbis.Colonnes["DMNED_VALEUR_ID_SOURCE"] = l.IdDossier!.Value.ToString(CultureInfo.InvariantCulture);
                edKbis.Colonnes["DMNED_IMPRESSION"] = true;
                if (depot.Coffre)
                {
                    edKbis.Colonnes["DMNED_ENVOI_AU_COFFRE"] = true;
                    edKbis.Colonnes["DMNED_IMPRESSION"] = depot.Impression;
                    edKbis.Colonnes["DMNED_CODE_CFECP"] = depot.CodeCfecp;
                }
                ids.Add((await EditionsEvenementV2.DeposerAsync(geste, idDemande, edKbis, ct)).Id);
            }
            editions += ids.Count;
            traces.Add(new TraceGeste("affaire", $"la citation du rôle {l.Role}", ecrits.IdSurvenu.ToString(CultureInfo.InvariantCulture), "a cree", null,
                new { Instance = l.Role, Evenement = code, Date = geste.Maintenant.Date, Huissier = huissier.Nom, Demande = numero, Editions = ids }));
        }
        var n = lignes.Count;
        var file = editions switch
        {
            0 => "Aucune édition n'est paramétrée pour la citation.",
            1 => "1 édition placée dans la file des éditions.",
            _ => $"{editions} éditions placées dans la file des éditions.",
        };
        return IssueGeste.Ecrit($"{n} citation{(n > 1 ? "s" : "")} par huissier enregistrée{(n > 1 ? "s" : "")} ({huissier.Nom}). {file} {NonProduites}",
                                [.. d.Ids], [.. traces]);
    }

    /// <summary>L'huissier choisi dans les destinataires d'une edition (OPE_CITATIONS.cs:364-391) : un intervenant s'ajoute coche si
    /// l'edition vise un huissier ; tout autre huissier coche est decoche ; un nom de coffre fait passer l'edition au coffre.</summary>
    private static void AjusterHuissier(EditionsEvenementV2.Edition e, Huissier huissier)
    {
        if (huissier.EstIntervenant && e.Destinataires.Any(x => x.ParDefaut == "1" && x.Code == "INST_H"))
            e.Destinataires.Add(new EditionsEvenementV2.Destinataire
            {
                ParDefaut = "1", Code = "INST_H", Valeur = huissier.Nom, Libelle = huissier.Nom, Adresse = huissier.Adresse, Email = huissier.Email,
                NomCoffre = huissier.NomCoffre,
            });
        foreach (var autre in e.Destinataires.Where(x => x.ParDefaut == "1" && x.Code == "INST_H" && x.Valeur != huissier.Nom))
            autre.ParDefaut = "0";
        if (huissier.NomCoffre.Length > 0)
        {
            e.Colonnes["DMNED_ENVOI_AU_COFFRE"] = true;
            e.Colonnes["DMNED_IMPRESSION"] = false;
        }
    }

    /// <summary>Le code d'evenement de la citation (Surveillances.GetCodeEveProCitationHuissier, Surveillances.cs:423-425).</summary>
    private static async Task<string> CodeCitationAsync(GesteEnCours geste, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT Valeur FROM soc.Reglage WHERE Code = 'SURV_CEP_CITATION_HUISS'", ct);
        return await r.ReadAsync(ct) && FormeLigne.Texte(r, "Valeur") is { Length: > 0 } v ? v : "CITATION";
    }

    /// <summary>Un destinataire de type huissier sur la maquette du code (Verifier, OPE_CITATIONS.cs:252-263).</summary>
    private static async Task<bool> MaquetteAvecHuissierAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        var modes = new List<string>();
        await using (var r = await geste.LireAsync("SELECT CEPED_DESTI_MODE_ENVOI FROM v2.CEP_EDITION WHERE CEPED_CODE_EVE_PRO = @c", ct,
                         new SqlParameter("@c", code)))
            while (await r.ReadAsync(ct)) modes.Add(FormeLigne.Texte(r, "CEPED_DESTI_MODE_ENVOI"));
        return modes.Any(m => (m.Length == 0 ? "DI" : m.Replace("\r\n", ";", StringComparison.Ordinal)).Split(';')
                                 .Any(x => x == "H" || x.StartsWith("H,", StringComparison.Ordinal) || x == "HUI"));
    }

    /// <summary>L'huissier saisi : un intervenant (id numerique) ou un destinataire institutionnel (OPE_CITATIONS.cs:347-354) -- son
    /// nom, son courriel et son nom de coffre (les colonnes brutes, :353-354).</summary>
    private static async Task<Huissier?> HuissierAsync(GesteEnCours geste, string saisi, CancellationToken ct)
    {
        var numerique = int.TryParse(saisi, NumberStyles.None, CultureInfo.InvariantCulture, out var id);
        await using var r = numerique
            ? await geste.LireAsync("""
                SELECT INTRPR_NOM AS Nom, '' AS Adresse, INTRPR_ADRESSE_MAIL AS Email, INTRPR_NOM_COFFRE AS Coffre
                FROM v2.INTERVENANT_PC_REPER WHERE INTRPR_ID_INTRPR = @id
                """, ct, new SqlParameter("@id", id))
            : await geste.LireAsync("""
                SELECT DTINS_CODE AS Nom, DTINS_BLOC_ADRESSE AS Adresse, DTINS_ADRESSE_MAIL AS Email, DTINS_NOM_COFFRE AS Coffre
                FROM v2.DESTINATAIRE_INSTITUTIONNEL WHERE DTINS_CODE = @c
                """, ct, new SqlParameter("@c", saisi));
        return await r.ReadAsync(ct)
            ? new Huissier(FormeLigne.Texte(r, "Nom"), numerique, FormeLigne.Texte(r, "Adresse"), FormeLigne.Texte(r, "Email"), FormeLigne.Texte(r, "Coffre"))
            : null;
    }

    /// <summary>La piece numerisee de l'evenement ecrit (EVNPR_AVEC_PIECE_NUMERISE, posee d'apres son code).</summary>
    private static async Task<bool> AvecPieceAsync(GesteEnCours geste, int idEvenement, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT EVNPR_AVEC_PIECE_NUMERISE FROM v2.EVENEMENT_PROCEDURE WHERE EVNPR_ID_EVNPR = @id", ct,
                                                  new SqlParameter("@id", idEvenement));
        return await r.ReadAsync(ct) && r[0] is true;
    }

    /// <summary>Une ligne du lecteur (« instance-demande d'edition-relance-exercice ») : l'instance, le retour de sa LRAR, et le
    /// dossier RCS de sa relance (le meme chemin que LecteurConvocations : diligence, action, dossier).</summary>
    private static async Task<Ligne?> LireLigneAsync(GesteEnCours geste, string id, CancellationToken ct)
    {
        var parts = id.Split('-');
        if (parts.Length < 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var instance)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var edition)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var relance)) return null;
        await using var r = await geste.LireAsync("""
            SELECT i.INSTN_NUM_ROLE, l.LRARI_TYPE_DATE, s.Id AS IdDossier, i.INSTN_NUMERO_SOURCE, i.INSTN_NATURE_AFFAIRE, i.INSTN_NAT_AFF_PART,
                   i.INSTN_CODE_ACTE_PROCE
            FROM v2.INSTANCE AS i WITH (UPDLOCK, HOLDLOCK)
            JOIN v2.DEMANDE_EDITION AS de ON de.DMNED_ID_DMNED = @edition
            JOIN v2.LRAR_INFOS AS l ON l.LRARI_NUM_LRAR = de.DMNED_NUM_LRAR
            LEFT JOIN v2.RELANCE_RCS AS rr ON rr.RLCRC_ID_RLCRC = @relance
            LEFT JOIN v2.DILIGENCE_RCS AS g ON g.DLGRC_ID_DLGRC = rr.RLCRC_ID_DLGRC
            LEFT JOIN rec.ActionRegistre AS ar ON ar.Id = g.DLGRC_ID_ACTRC
            LEFT JOIN rec.DossierRcs AS s ON s.Id = ar.IdDossier
            WHERE i.INSTN_ID_INSTN = @instance
            """, ct, new SqlParameter("@instance", instance), new SqlParameter("@edition", edition), new SqlParameter("@relance", relance));
        if (!await r.ReadAsync(ct)) return null;
        string? Texte(string c) => FormeLigne.Texte(r, c) is { Length: > 0 } t ? t : null;
        var role = FormeLigne.Texte(r, "INSTN_NUM_ROLE");
        return new Ligne(instance, role, Texte("LRARI_TYPE_DATE"), r["IdDossier"] is DBNull ? null : Convert.ToInt32(r["IdDossier"], CultureInfo.InvariantCulture),
            new EditionsEvenementV2.Instance(instance, role, Texte("INSTN_NUMERO_SOURCE"), Texte("INSTN_NATURE_AFFAIRE"), Texte("INSTN_NAT_AFF_PART"),
                                             Texte("INSTN_CODE_ACTE_PROCE")));
    }
}
