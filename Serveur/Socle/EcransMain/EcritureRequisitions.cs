using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Réquisitions » (PROC_REQUI, écran fait main web/src/ecrans/EcranRequisitions.tsx) : le geste TERMINER LA COMMANDE DE DOCUMENTS
/// de RIG v2, sur sa branche SANS FACTURE remise AU GUICHET. Joué par la route POST RoutesTerminerRequisitions.Terminer
/// (ModuleRequisitions.cs) dans la transaction et le journal des gestes du socle (EcrivainsEcrans.JouerAsync) : gardes d'écriture
/// v2.* seulement. Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026 ; la relecture :
/// LectureCommandesRequi.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RCS\Exe\PROC_REQUI) :
///   - l'affaire et la demande REQUI créées à l'ouverture (frmREQUI.cs InitDemande :143-155) : Affaire.CreateAffaire(« REQUI »)
///     (Outils\DLL\RigMetier\Facture\Affaire.cs:122-134 : date de création, agent créateur, libellé) et Demande.CreateDemande(affaire,
///     « REQUI », « », « REQUI ») (Outils\DLL\RigMetier\Demande\Demande.cs:535 puis :476-524 : numéro DMD, état qualifiée « Q », en cours,
///     l'agent) ;
///   - « Terminer » (frmREQUI.cs Terminer :1399-2401) : une commande sans document refuse (:1408-1412, son message au mot près) ;
///     la fenêtre d'impression (frmGestionImprimante.cs) rend toute commande non facturable (:398-400, « On veut pouvoir rendre toute
///     commande non facturable »), sans client il faut la case « Guichet » (:221-224, son message) ; l'imprimante proposée est le
///     réglage REQUI_PRINTER_DEFAULT, sinon « Proximité » (:178), le sceau celui de la catégorie LEGAL (CTGR_FAMILLE_DOSSIER, :186-196) ;
///     non facturable : aucune facture (:1810) ; au guichet : aucune lettre d'envoi (:1938) ;
///   - la table COMMANDE (TraiterTableCommande :2478-2491, InsererCommande :2528-2543) : une ligne par document, sur la demande REQUI,
///     le dossier, provenance « REQUI », guichet ; un certificat de non-inscription RSAC écrit CNRAC PUIS CNRCS (:2487-2488, tel quel) ;
///   - par commande (:1960-1992) : une demande LPLURI « REQUI de la &lt;n°&gt; - &lt;dénomination&gt; » sur l'affaire, avec le dossier,
///     l'entreprise et le numéro de gestion -- la première les reporte sur la demande REQUI ; ses lignes d'édition
///     (Demande.AddLigneEdition, Demande.cs:1423-1439 ; LigneEdition.CreateLigneEditionSansBac, LigneEdition.cs:172-243 : agent, modèle,
///     transmission tirée du code (CodeTransmission :74-79), imprimante, rang, nombre, non différée, non imprimée, sceau, recto verso,
///     bac SECURISE) : Kbis KBISXML_1_PDF (:1997-2000), historique CHISTO_1_IMP (:2029-2035), liste DAS LDAS_1_IMP (:2038-2045), non-faillite
///     ACNF_1_IMP et la pseudo-variable DATE_CERTIFICAT (:2049-2069), non-inscription RSAC CNIRC_2_IMP (:2101-2113) ; puis Cloturer (« V »,
///     plus en cours : Demande.cs:766, :735-756 ; frmREQUI.cs:2335) ;
///   - la demande REQUI prend pour nom la liste des demandes LPLURI et se clôture (:2340-2343).
/// NON REPRIS (nommés, refus lisibles) : la FACTURATION (tarification RIG_CliSerD_FactTrt, facture, ligne FACT_3_IMP :1800-1910) et le
/// choix des CLIENTS facturé et destinataire (lettre d'envoi EFORMA1_3_IMP, INFO_DESTINATAIRE :1914-1949) : RIG v3 ne facture pas, seule
/// une commande non facturable remise au guichet se termine ; le certificat de non-inscription RCS (établissement du dossier :2083-2099),
/// les états d'inscriptions (SELECTION, PRECISER_SELECTION, ELEMENT_ID :2240-2330) et les documents absents de la commande de RIG v2
/// (non-opposition, décisions, navires, bénéficiaires effectifs de la V3) refusent. Ni l'ARRIVÉE ni la ligne du rapport général que
/// CreateDemande crée en plus (Demande.cs:480, :512), ni la catégorie (DMND_ID_CTGR) : comme CreationV2.CreerDemandeAsync. L'IMPRESSION
/// (RIG_Print_Demande.exe :2346-2380) est hors du périmètre du plan : les lignes d'édition attendent le service d'édition.
/// TRACE : famille « demande », « a cree ».
/// </summary>
// fix-ok: fichier neuf du lot 7 (plan-f4fb097f) ; cause mesuree au banc ParcoursCompletCheck du 28/11/2026 : PROC_REQUI « Terminer
// n'est pas repris : la V3 n'ecrit rien en base » (Pont/EcranRequisitions.cs, Terminer, TODO #275). L'agent des colonnes *_ID_UTLST
// est soc.Compte.IdV2 : Appelant.Id est l'identifiant du compte V3 (ServiceDroits.cs:117), pas celui de l'utilisateur V2.
public static partial class EcritureRequisitions
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Réquisitions";

    /// <summary>Les gestes écrits : tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Creer];

    /// <summary>frmREQUI.cs:1410, au mot près.</summary>
    public const string SansDocument = "Aucun document n'a été sélectionné. Veuillez sélectionner un document avant de valider.";

    /// <summary>frmGestionImprimante.cs:222, au mot près, et ce que la V3 ne reprend pas.</summary>
    public const string SansGuichet = "Vous devez selectionner un client ou cocher la case 'Guichet'. RIG v3 ne reprend pas encore le choix des "
                                    + "clients facturé et destinataire : seule une commande remise au guichet se termine.";

    /// <summary>L'imprimante proposée par RIG v2 sans réglage REQUI_PRINTER_DEFAULT (frmGestionImprimante.cs:178).</summary>
    public const string ImprimanteParDefaut = "Proximité";

    /// <summary>Le bac des documents de la commande (frmREQUI.cs:1997-2113, eBac.SECURISE).</summary>
    public const string Bac = "SECURISE";

    private sealed record Commande(int Numero, int Dossier, int Kbis, int Historique, int Das, int Cnf, DateTime DateCnf, int Cnrcs,
                                   int Cnrsac, DateTime DateCnrsac, int Inscriptions, int Autres)
    {
        public bool EstVide => Kbis + Historique + Das + Cnf + Cnrcs + Cnrsac + Inscriptions + Autres == 0;
    }

    /// <summary>Termine la commande des Valeurs du geste (RoutesTerminerRequisitions.Cle).</summary>
    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var commandes = new List<Commande>();
        for (var n = 1; Valeur(geste, n, RoutesTerminerRequisitions.Dossier) is { Length: > 0 } dossier; n++)
        {
            if (!int.TryParse(dossier, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idDossier))
                return IssueGeste.Refuser($"La commande n°{n} ne porte pas un dossier de la base du greffe ({dossier}).");
            commandes.Add(new Commande(n, idDossier, Nombre(geste, n, RoutesTerminerRequisitions.Kbis),
                Nombre(geste, n, RoutesTerminerRequisitions.Historique), Nombre(geste, n, RoutesTerminerRequisitions.Das),
                Nombre(geste, n, RoutesTerminerRequisitions.CertifNonFaillite), Jour(geste, n, RoutesTerminerRequisitions.DateCertifNonFaillite),
                Nombre(geste, n, RoutesTerminerRequisitions.CertifNonRcs), Nombre(geste, n, RoutesTerminerRequisitions.CertifNonRsac),
                Jour(geste, n, RoutesTerminerRequisitions.DateCertifNonRsac), Nombre(geste, n, RoutesTerminerRequisitions.Inscriptions),
                Nombre(geste, n, RoutesTerminerRequisitions.AutresDocuments)));
        }
        if (commandes.Count == 0) return IssueGeste.Refuser("Aucune commande n'a été trouvée.");
        foreach (var c in commandes)
        {
            if (c.EstVide) return IssueGeste.Refuser(SansDocument);
            if (c.Cnrcs > 0)
                return IssueGeste.Refuser($"Commande n°{c.Numero} : le certificat de non-inscription RCS (lu sur l'établissement du dossier dans RIG v2) "
                                        + "n'est pas encore repris par RIG v3. Rien n'est enregistré.");
            if (c.Inscriptions > 0)
                return IssueGeste.Refuser($"Commande n°{c.Numero} : les états d'inscriptions (sélection des inscriptions de RIG v2) ne sont pas encore "
                                        + "repris par RIG v3. Rien n'est enregistré.");
            if (c.Autres > 0)
                return IssueGeste.Refuser($"Commande n°{c.Numero} : la commande de RIG v2 ne porte ni non-opposition, ni décisions, ni navires, ni "
                                        + "bénéficiaires effectifs. Retirez-les pour terminer. Rien n'est enregistré.");
        }
        if (Valeur(geste, RoutesTerminerRequisitions.ChampGuichet) != "1") return IssueGeste.Refuser(SansGuichet);

        // Les dossiers commandés : l'entreprise, le numéro de gestion et la dénomination (RecupNomDossier, frmREQUI.cs).
        var dossiers = new Dictionary<int, (int? Entreprise, string Gestion, string Nom)>();
        foreach (var id in commandes.Select(c => c.Dossier).Distinct())
        {
            await using var r = await geste.LireAsync("""
                SELECT s.IdEntreprise, s.NumeroGestion,
                       COALESCE(NULLIF(LTRIM(RTRIM(e.Denomination)), ''), LTRIM(RTRIM(CONCAT(e.Patronyme, ' ', e.Prenom)))) AS Nom
                FROM rec.DossierRcs AS s LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise WHERE s.Id = @id
                """, ct, new SqlParameter("@id", id));
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Le dossier {id} n'est pas dans la base du greffe. Rien n'est enregistré.");
            dossiers[id] = (r["IdEntreprise"] is DBNull ? null : Convert.ToInt32(r["IdEntreprise"], CultureInfo.InvariantCulture),
                            FormeLigne.Texte(r, "NumeroGestion").Trim(), FormeLigne.Texte(r, "Nom").Trim());
        }

        var imprimante = await TexteAsync(geste, "SELECT TOP (1) Valeur FROM soc.Reglage WHERE Code = 'REQUI_PRINTER_DEFAULT'", ct) is { Length: > 0 } p
            ? p : ImprimanteParDefaut;
        var sceau = await TexteAsync(geste, "SELECT TOP (1) FamilleDossier FROM rec.TypeDemande WHERE Abrege = 'LEGAL'", ct);
        // L'agent dans les tables de RIG v2 (Utilisateur.GetUtilisateurCourantInDBInt) : son utilisateur V2 (soc.Compte.IdV2, comme
        // EcritureDemandes.cs) ; vide pour un compte sans utilisateur V2.
        int? agentV2;
        await using (var r = await geste.LireAsync("SELECT IdV2 FROM soc.Compte WHERE CompteWindows = @c", ct,
                         new SqlParameter("@c", geste.Agent.CompteWindows)))
            agentV2 = await r.ReadAsync(ct) && r["IdV2"] is int v ? v : null;

        // InitDemande (frmREQUI.cs:143-155) : l'affaire et la demande REQUI.
        var maintenant = geste.Maintenant;
        var affaire = await geste.InsererAsync("v2.AFFAIRE", "AFFRE_ID_AFFRE", new Dictionary<string, object?>
        {
            ["AFFRE_DATE_CREATION"] = maintenant, ["AFFRE_ID_UTLST_CREATION"] = agentV2, ["AFFRE_LIBELLE"] = "REQUI",
        }, ct);
        var (requi, refus) = await DemandeAsync(geste, affaire, "REQUI", "REQUI", ct);
        if (requi is null) return IssueGeste.Refuser($"{refus} La commande n'est pas enregistrée.");

        // TraiterTableCommande (:2478-2491) : une ligne de COMMANDE par document, sur la demande REQUI.
        foreach (var c in commandes)
            foreach (var (type, qte) in new[] { ("KBIS", c.Kbis), ("HISTO", c.Historique), ("LDAS", c.Das), ("CNF", c.Cnf), ("CNRAC", c.Cnrsac), ("CNRCS", c.Cnrsac) })
                if (qte > 0)
                    await geste.InsererAsync("v2.COMMANDE", "CMMDE_ID_CMMDE", new Dictionary<string, object?>
                    {
                        ["CMMDE_DATE"] = maintenant, ["CMMDE_TYPE"] = type, ["CMMDE_QTE"] = (short)qte, ["CMMDE_ID_DMND"] = requi.Value.Id,
                        ["CMMDE_ID_DSRCS"] = c.Dossier, ["CMMDE_ID_NINSC"] = null, ["CMMDE_PROVENANCE"] = "REQUI", ["CMMDE_GUICHET"] = true,
                    }, ct);

        // Les demandes LPLURI (:1960-2335), une par commande, et leurs lignes d'édition.
        var colonnesEdition = await EditionsV2.ColonnesAsync(geste, "v2.LIGNE_EDITION", ct);
        var numeros = new List<string>();
        var editions = 0;
        foreach (var c in commandes)
        {
            var (entreprise, gestion, nom) = dossiers[c.Dossier];
            var nomDemande = $"REQUI de la {requi.Value.Numero} - {nom}";
            if (nomDemande.Length > 255) nomDemande = nomDemande[..252] + "...";
            var (demande, refusLpluri) = await DemandeAsync(geste, affaire, nomDemande, "LPLURI", ct);
            if (demande is null) return IssueGeste.Refuser($"{refusLpluri} La commande n'est pas enregistrée.");
            await RattacherAsync(geste, demande.Value.Id, c.Dossier, entreprise, gestion, ct);
            if (c.Numero == 1) await RattacherAsync(geste, requi.Value.Id, c.Dossier, entreprise, gestion, ct);
            numeros.Add(demande.Value.Numero);

            var rang = 0;
            async Task EditionAsync(string modele, int nombre)
            {
                await LigneEditionAsync(geste, colonnesEdition, agentV2, demande.Value.Id, modele, ++rang, nombre, imprimante, sceau, ct);
                editions++;
            }
            if (c.Kbis > 0) await EditionAsync("KBISXML_1_PDF", c.Kbis);
            if (c.Historique > 0) await EditionAsync("CHISTO_1_IMP", c.Historique);
            if (c.Das > 0) await EditionAsync("LDAS_1_IMP", c.Das);
            var dateCertificat = false;
            if (c.Cnf > 0)
            {
                await PseudoVariableAsync(geste, demande.Value.Id, c.DateCnf, ct);
                dateCertificat = true;
                await EditionAsync("ACNF_1_IMP", c.Cnf);
            }
            if (c.Cnrsac > 0)
            {
                if (!dateCertificat) await PseudoVariableAsync(geste, demande.Value.Id, c.DateCnrsac, ct);
                await EditionAsync("CNIRC_2_IMP", c.Cnrsac);
            }
            await CloturerAsync(geste, demande.Value.Id, null, ct);
        }
        var liste = string.Join(";", numeros);
        await CloturerAsync(geste, requi.Value.Id, liste.Length > 255 ? liste[..252] + "..." : liste, ct);

        var apres = new { Demande = requi.Value.Numero, Demandes = liste, Imprimante = imprimante, Sceau = sceau, Guichet = true, Facture = false };
        return IssueGeste.Ecrit($"Votre commande est terminée : demande {requi.Value.Numero} ({commandes.Count} commande(s), demande(s) {liste}, "
                              + $"{editions} édition(s) pour l'imprimante « {imprimante} »). Remise au guichet, sans facture : RIG v3 ne facture pas encore. "
                              + "L'impression attend le service d'édition.",
            [requi.Value.Numero],
            new TraceGeste("demande", $"la commande de réquisitions {requi.Value.Numero}", requi.Value.Id.ToString(CultureInfo.InvariantCulture),
                           "a cree", null, apres));
    }

    /// <summary>Demande.CreateDemande (Demande.cs:476-524) : numéro DMD, qualifiée « Q », en cours, l'agent, sur l'affaire.</summary>
    private static async Task<((int Id, string Numero)? Demande, string? Refus)> DemandeAsync(GesteEnCours geste, int affaire, string nom,
        string codeProcessus, CancellationToken ct)
    {
        var (numero, refus) = await CreationV2.NumeroAsync(geste, "DMD", geste.Maintenant.Year, ct);
        if (numero is null) return (null, refus);
        var id = await CreationV2.InsererAsync(geste, "v2.DEMANDE", "DMND_ID_DMND", new Dictionary<string, object?>
        {
            ["DMND_NUM_DEMANDE"] = numero, ["DMND_ETAT_DEMANDE"] = "Q", ["DMND_EN_COURS"] = true, ["DMND_ID_AFFRE"] = affaire,
            ["DMND_NOM"] = nom, ["DMND_NUM_INDICATIF"] = "", ["DMND_CODE_PROSS"] = codeProcessus, ["DMND_DATE_CREATION"] = geste.Maintenant,
            ["DMND_NOM_UTILISATEUR"] = geste.Agent.NomAffiche,
        }, ct);
        return ((id, numero), null);
    }

    /// <summary>Le dossier, l'entreprise et le numéro de gestion de la commande sur la demande (frmREQUI.cs:1968-1990).</summary>
    private static Task<int> RattacherAsync(GesteEnCours geste, int demande, int dossier, int? entreprise, string gestion, CancellationToken ct)
        => geste.EcrireAsync("""
            UPDATE v2.DEMANDE SET DMND_ID_DSRCS = @dossier, DMND_ID_AUTRE = @entreprise, DMND_NUM_INDICATIF = LEFT(@gestion, 14)
            WHERE DMND_ID_DMND = @id
            """, ct, new SqlParameter("@dossier", dossier), new SqlParameter("@entreprise", (object?)entreprise ?? DBNull.Value),
            new SqlParameter("@gestion", gestion), new SqlParameter("@id", demande));

    /// <summary>Demande.Cloturer (Demande.cs:766, :735-756) : validée « V », plus en cours ; <paramref name="nom"/> : le nouveau nom.</summary>
    private static Task<int> CloturerAsync(GesteEnCours geste, int demande, string? nom, CancellationToken ct)
        => geste.EcrireAsync("""
            UPDATE v2.DEMANDE SET DMND_ETAT_DEMANDE = 'V', DMND_EN_COURS = 0, DMND_NOM = COALESCE(@nom, DMND_NOM) WHERE DMND_ID_DMND = @id
            """, ct, new SqlParameter("@nom", (object?)nom ?? DBNull.Value), new SqlParameter("@id", demande));

    /// <summary>La pseudo-variable DATE_CERTIFICAT de la demande (frmREQUI.cs:2053-2056 : la date courte du poste, jj/mm/aaaa).</summary>
    private static Task<int> PseudoVariableAsync(GesteEnCours geste, int demande, DateTime date, CancellationToken ct)
        => geste.InsererAsync("v2.PSEUDO_VARIABLE", "PSDVR_CPT", new Dictionary<string, object?>
        {
            ["PSDVR_ID_DEMANDE"] = demande, ["PSDVR_NOM_VARIABLE"] = "DATE_CERTIFICAT",
            ["PSDVR_VALEUR_VARIABLE"] = date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        }, ct);

    /// <summary>
    /// Demande.AddLigneEdition (Demande.cs:1423-1439) -> LigneEdition.CreateLigneEdition (LigneEdition.cs:254-258 et :172-243). Les
    /// colonnes NOT NULL que RIG_DEV remplit par ses DEFAULT (LIGNE_EDITION.sql) reçoivent ces défauts : la base V3 ne les porte pas
    /// (CreationV2.DefautsRigDev). Les colonnes qui DISENT la ligne (demande, modèle, transmission, rang, nombre) sont toujours écrites :
    /// une table qui ne les porte pas refuse le geste ; les autres, seulement si la table les porte (une base de banc n'en a qu'une partie,
    /// EditionsV2.DemandeEditionAsync).
    /// </summary>
    // fix-ok: mesure au banc du parcours le 28/11/2026 (sonde MESURE-LOT7) -- la v2.LIGNE_EDITION du banc, creee par le jeu d'un autre
    // ecran, n'avait pas LGEDT_CODE_MODELE_DOC : le filtre par colonnes presentes ecrivait la ligne SANS son modele, la relecture rendait
    // 500 « Invalid column name 'LGEDT_CODE_MODELE_DOC' ». Les colonnes essentielles ne sont plus filtrees.
    private static Task<int> LigneEditionAsync(GesteEnCours geste, HashSet<string> colonnes, int? agentV2, int demande, string modele, int rang, int nombre,
                                               string imprimante, string? sceau, CancellationToken ct)
    {
        var essentielles = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["LGEDT_ID_DMND"] = demande, ["LGEDT_CODE_MODELE_DOC"] = modele, ["LGEDT_TRANSMISSION"] = Transmission(modele),
            ["LGEDT_NUM_ORDRE"] = (short)rang, ["LGEDT_NOMBRE"] = (short)nombre,
        };
        var valeurs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["LGEDT_ID_UTLST"] = agentV2, ["LGEDT_DESTINATION_PHYSIQUE"] = imprimante,
            ["LGEDT_DIFFERE"] = false, ["LGEDT_ETAT_EDT"] = "I", ["LGEDT_ETAT"] = "0", ["LGEDT_ETAT_TRS"] = "I",
            ["LGEDT_SCEAU"] = sceau, ["LGEDT_NOMBRE_MODIF"] = false, ["LGEDT_WORD_VISIBLE"] = false, ["LGEDT_RECTO_VERSO"] = true,
            ["LGEDT_IMP_BAC"] = Bac,
            // Les DEFAULT de RIG_DEV (LIGNE_EDITION.sql).
            ["LGEDT_TRAITEE"] = false, ["LGEDT_RETENTION"] = false, ["LGEDT_HORS_EDITION_MQT"] = false, ["LGEDT_EN_COURS"] = false,
            ["LGEDT_A_EDITER"] = false, ["LGEDT_A_TRANSMETTRE"] = false, ["LGEDT_FICHIER_SOURCE"] = false, ["LGEDT_CERTIFICAT_ELECTRONIQUE"] = false,
            ["LGEDT_EST_PDF"] = false, ["LGEDT_IMPRIMER"] = true, ["LGEDT_NON_SUPPRESSIBLE"] = false, ["LGEDT_NON_MODIFIABLE"] = false,
        };
        foreach (var (colonne, valeur) in valeurs)
            if (colonnes.Contains(colonne)) essentielles[colonne] = valeur;
        return geste.InsererAsync("v2.LIGNE_EDITION", "LGEDT_ID_LGEDT", essentielles, ct);
    }

    /// <summary>LigneEdition.CodeTransmission (LigneEdition.cs:74-79) : les trois lettres après « _&lt;chiffres&gt;_ », sinon « IMP ».</summary>
    internal static string Transmission(string modele) => CodeTransmission().Match(modele) is { Success: true } m ? m.Groups[1].Value : "IMP";

    [GeneratedRegex(".*_[0-9]*_(...)")]
    private static partial Regex CodeTransmission();

    private static async Task<string?> TexteAsync(GesteEnCours geste, string sql, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(sql, ct);
        return await r.ReadAsync(ct) && r[0] is not DBNull ? Convert.ToString(r[0], CultureInfo.InvariantCulture)?.Trim() : null;
    }

    private static string? Valeur(GesteEnCours geste, string cle)
        => geste.Demande.Valeurs.TryGetValue(cle, out var v) ? v?.Trim() : null;

    private static string? Valeur(GesteEnCours geste, int n, string champ) => Valeur(geste, RoutesTerminerRequisitions.Cle(n, champ));

    private static int Nombre(GesteEnCours geste, int n, string champ)
        => int.TryParse(Valeur(geste, n, champ), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0;

    private static DateTime Jour(GesteEnCours geste, int n, string champ)
        => CreationV2.Date(Valeur(geste, n, champ) ?? "") ?? geste.Maintenant.Date;
}
