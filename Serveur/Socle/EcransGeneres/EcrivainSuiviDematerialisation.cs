using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// Les gestes de « Suivi dématérialisation » (PROC_SDEMATJUD), une File : « Valider » met à imprimer les pièces numérisées
/// reçues des lignes JAL cochées, comme RIG v2 ; « Éditer les détails » est refusé (un classeur Excel). Lignes :
/// LecteurSuiviDematerialisation (l'Id d'une ligne JAL : « publicité:document de parution:document de facture », 0 sans
/// document). Mode d'emploi : EcrivainEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 9, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_SDEMATJUD\Resultat\OPE_RESULTAT_JAL.cs (OPE_RESULTAT_BODACC.cs est le
/// même code) et Outils\DLL\RigMetier :
///   - CE QUI SE COCHE : les cases « Editer facture » et « Editer parution » d'une ligne, actives seulement si son document de
///     facture (FACTJAL) ou de parution (JAL) est reçu en GED (Resultat\Control\DataGridViewSdematJud.cs:428-443), et cachées
///     sur l'onglet BODACC (ModifierColonneJalBodacc, :253-260). La V3 n'a qu'une case par ligne : cocher une ligne JAL coche
///     ses deux cases, celle d'un document reçu seulement. Une ligne sans document ou une annonce BODACC ne compte pas ;
///   - LES CONTROLES (DoVerifier, :106-130), au mot près : aucune ligne retenue -> « Vous devez sélectionner au moins une ligne »
///     (AUCUNE_SELECTION, :22) ; plus de 200 affaires -> « Veuillez sélectionner au maximum 200 lignes » (:124-126) ;
///   - CE QUI S'ECRIT (_GenererLesDemandesEditions, :133-200 ; DoSauver, :88-91 ; _Sauver, :284-288) :
///       la demande du traitement (code SDEMATJUD, PROCESSUS_ET_FONCTIONALITE de RIG_DEV), nommée « Génération d'édition pour
///       N instance(s) » (:89) et validée ;
///       UNE demande par affaire (le numéro de PC, sinon celui de la prévention), créée sur la PC ou la prévention
///       (CreateDemande, :156-169), fille de la demande du traitement (DMND_ID_DMND_MERE, :171), validée (:286) -- une ligne
///       dont l'événement n'a ni PC ni prévention n'a pas de demande : RIG v2 la passe sans rien écrire (:170), la V3 aussi ;
///       pour chaque document coché, sa demande d'édition (_NouvellePieceNumerisee, :301-317 ;
///       DemandeEdition.CreateDemandeEditionPieceNumerisee, Edition\DemandeEdition.cs:2608-2660) : d'abord la RÉFÉRENCE du
///       document en GED RIG (GedRig.CreateGedRigFromVidiged, Divers\GedRig.cs:231-239 -- une ligne GED_RIG qui pointe le
///       document VIDIGED par son GUID, base JUDICIAIRE, son extension ou « pdf » ; AUCUN fichier n'est copié, il est tiré de
///       VIDIGED à la lecture, :379-380), puis la demande d'édition : les valeurs de CreateDemandeEdition (:536-562) et celles
///       de la pièce numérisée (:2628-2653), libellé « <nature> numérisée » (VIDIGED_PARAMETRAGE, NATURE_DOC) ou « Document
///       numérisé » (:303-309), un exemplaire, sans recto verso, l'imprimante par défaut de l'agent (UTLST_IMPRIMANTE_DEFAUT,
///       :316 -- dans la V3, sa préférence « Imprimante », reprise de cette colonne par ReprisePreferences.cs:29), phase « G »
///       portée à « P » (prête à imprimer) à l'enregistrement (RequeteSauve, :1124-1131 : ni affichage avant impression, et
///       impression demandée).
/// CE QUI NE SORT PAS DU POSTE : l'impression. Dans RIG v2, une demande d'édition « P » est prise par le service d'impression,
///   absent de la V3 : rien n'est imprimé ni envoyé, le message le dit. Aucune publicité ne change d'état : RIG v2 n'en écrit pas.
/// CE QUE LA V3 NE REPREND PAS (nommé) : les paramètres XML de la demande d'édition (DMNED_PARAMETRE_GENERATION, construits par
///   InitialiserParametres et SetParametreOuChamp, :2120-2175 : NE_PAS_GENERER, TYPE_MEDIA, LISTE_ENTITE_DESTINATION, CLE) --
///   leurs valeurs utiles sont les colonnes écrites (média, imprimante, exemplaires) ; l'application d'archivage de la
///   référence GED (GDRG_APPLICATION_ARCHIVAGE, le nom de l'exécutable RIG v2) reste vide ; le tableau d'édition que RIG v2
///   montre avant d'enregistrer ; « Vérifier », qui ne fait que les contrôles ci-dessus sans rien écrire (« Valider » les fait).
/// fix-ok: mesuré au contrôle final du lot 9 (27/11/2026) -- le passage précédent disait la pièce « copiée » de VIDIGED ; GedRig.cs:
/// 231-239 n'écrit qu'une référence, que la V3 peut écrire : le geste est donc branché.
/// </summary>
internal sealed class EcrivainSuiviDematerialisation : IEcrivainEcran
{
    public string Procedure => "PROC_SDEMATJUD";
    public string Ecran => "Suivi dématérialisation";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les gestes du descripteur (Surveiller.cs), au mot près.</summary>
    internal const string Valider = "Valider", EditerDetails = "Éditer les détails";

    /// <summary>Le code du traitement dans RIG v2 (PROCESSUS_ET_FONCTIONALITE.PROCF_CODE_PROC de RIG_DEV, lu le 27/11/2026).</summary>
    internal const string CodeProcessus = "SDEMATJUD";

    private const string AucuneSelection = "Vous devez sélectionner au moins une ligne";
    private const int MaxAffaires = 200;

    // Un document reçu d'une publicité, de la nature de sa case (JAL : parution, FACTJAL : facture), et l'affaire de son
    // événement ; le libellé de sa nature (VidigedParametrage.RecupLibelle : NULL si la nature n'y est pas).
    private const string Document = """
        SELECT v.VGIDX_ID_VGIDX, v.VGIDX_GUID_DOCUMENT, v.VGIDX_EXTENSION, pc.PRCLL_ID_PRCLL, pv.PREVE_ID_PREVE,
               pc.PRCLL_NUM_PC, pc.PRCLL_NOM_AFFAIRE, pv.PREVE_NUM_PRV, pv.PREVE_NOM_AFFAIRE,
               (SELECT TOP (1) n.VGPRM_LIBELLE FROM v2.VIDIGED_PARAMETRAGE AS n WHERE n.VGPRM_TYPE = 'NATURE_DOC' AND n.VGPRM_CODE = v.VGIDX_NATURE_DOC) AS Nature
        FROM v2.VIDIGED_INDEX AS v
        JOIN v2.PUBLICITE AS p ON p.PBLCT_ID_PBLCT = v.VGIDX_ID_PBLCT
        JOIN v2.EVENEMENT_PROCEDURE AS e ON e.EVNPR_ID_EVNPR = p.PBLCT_ID_EVNPR
        LEFT JOIN v2.PROCEDURE_COLLECTIVE AS pc ON pc.PRCLL_ID_PRCLL = e.EVNPR_ID_PRCLL
        LEFT JOIN v2.PREVENTION AS pv ON pv.PREVE_ID_PREVE = e.EVNPR_ID_PREVE
        WHERE v.VGIDX_ID_VGIDX = @doc AND v.VGIDX_ID_PBLCT = @pub AND v.VGIDX_NATURE_DOC = @nature
        """;

    private sealed record Piece(int Vidiged, Guid Guid, string Extension, string Libelle);

    private sealed record Affaire(string Numero, string Nom, string Sequenceur, int Id, List<Piece> Pieces);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var nom = geste.Demande.Nom;
        if (nom == EditerDetails)
            return IssueGeste.Refuser("« Éditer les détails » produit un classeur Excel du résultat (ResultatRechercheRapport de RIG v2) : la V3 ne produit pas de classeur, rien n'est édité.");
        if (nom != Valider) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {nom}.");

        // 1. Les documents cochés : ceux qu'une ligne JAL a reçus (une case active, DataGridViewSdematJud.cs:428-443).
        var affaires = new Dictionary<string, Affaire>(StringComparer.Ordinal);
        foreach (var id in geste.Demande.Ids.Distinct(StringComparer.Ordinal))
        {
            var parties = id.Split(':');
            if (parties.Length != 3 || !Entier(parties[0], out var publicite) || !Entier(parties[1], out var parution) || !Entier(parties[2], out var facture))
                continue; // une annonce BODACC n'a pas d'Id : rien d'autre ne vient ici que d'une ligne JAL
            // L'ordre de RIG v2 : la facture, puis la parution (:182-193).
            foreach (var (doc, nature) in new[] { (facture, "FACTJAL"), (parution, "JAL") })
            {
                if (doc == 0) continue;
                await using var r = await geste.LireAsync(Document, ct, new SqlParameter("@doc", doc), new SqlParameter("@pub", publicite),
                    new SqlParameter("@nature", nature));
                if (!await r.ReadAsync(ct)) continue; // le document n'est plus celui de la ligne : sa case n'existe plus
                // L'affaire : la PC de l'evenement, sinon sa prevention (ProcedureCollective / Prevention.GetXxx, :156-169) -- une
                // affaire absente de la base ne donne pas de demande, comme dans RIG v2 (GetXxx rend null).
                var (numero, nomAffaire, sequenceur, idAffaire) = r["PRCLL_ID_PRCLL"] is int pc
                    ? (FormeLigne.Texte(r, "PRCLL_NUM_PC").Trim(), FormeLigne.Texte(r, "PRCLL_NOM_AFFAIRE"), LecteurProceduresCollectives.SequenceurPc, pc)
                    : r["PREVE_ID_PREVE"] is int pv
                        ? (FormeLigne.Texte(r, "PREVE_NUM_PRV").Trim(), FormeLigne.Texte(r, "PREVE_NOM_AFFAIRE"), LecteurProceduresCollectives.SequenceurPrevention, pv)
                        : ("", "", "", 0);
                var libelleNature = r["Nature"] as string;
                // VidigedParametrage.RecupLibelle rend null si la nature n'y est pas : RIG v2 ecrit alors « null + " numérisée" »
                // (OPE_RESULTAT_JAL.cs:303-309), c'est-a-dire « numérisée » precede d'un blanc. Repris tel quel, sans le blanc.
                var libelle = libelleNature == "" ? "Document numérisé" : $"{libelleNature} numérisée".Trim();
                var extension = FormeLigne.Texte(r, "VGIDX_EXTENSION").Trim();
                // La reference GED pointe le document VIDIGED par son GUID (GedRig.cs:233) : sans GUID, RIG v2 echoue a
                // l'enregistrement (GDRG_GUID_VIDIGED est un uniqueidentifier) ; la V3 le dit avant d'ecrire.
                if (!Guid.TryParse(FormeLigne.Texte(r, "VGIDX_GUID_DOCUMENT"), out var guid))
                    return IssueGeste.Refuser($"Le document {doc} reçu en GED n'a pas de GUID VIDIGED : sa pièce numérisée ne peut pas être référencée, rien n'est mis à imprimer.");
                var piece = new Piece(doc, guid, extension.Length > 0 ? extension : "pdf", libelle);
                // Une ligne sans PC ni prevention : pas de demande, pas d'edition (:156-170).
                if (idAffaire == 0) continue;
                var cle = $"{sequenceur}:{numero}";
                if (!affaires.TryGetValue(cle, out var a)) affaires[cle] = a = new Affaire(numero, nomAffaire, sequenceur, idAffaire, []);
                if (a.Pieces.All(p => p.Vidiged != doc)) a.Pieces.Add(piece);
            }
        }

        // 2. Les controles de DoVerifier (:106-130).
        if (affaires.Count == 0) return IssueGeste.Refuser(AucuneSelection);
        if (affaires.Count > MaxAffaires) return IssueGeste.Refuser("Veuillez sélectionner au maximum 200 lignes");

        // 3. L'imprimante par defaut de l'agent (UTLST_IMPRIMANTE_DEFAUT, :316) : sa preference « Imprimante » dans la V3.
        string? imprimante = null;
        await using (var r = await geste.LireAsync("SELECT Valeur FROM soc.Preference WHERE IdCompte = @id AND Nom = 'Imprimante'", ct,
                         new SqlParameter("@id", geste.Agent.Id)))
            if (await r.ReadAsync(ct)) imprimante = TexteJson(r.GetString(0));

        // 4. La demande du traitement, puis une demande par affaire et une demande d'edition par document.
        var (traitement, refus) = await CreerDemandeAsync(geste, CodeProcessus, "V", ct);
        if (traitement is null) return IssueGeste.Refuser(refus!);
        var n = affaires.Count;
        await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_NOM = @nom WHERE DMND_ID_DMND = @id", ct,
            new SqlParameter("@nom", $"Génération d'édition pour {n} instance{(n > 1 ? "s" : "")}"), new SqlParameter("@id", traitement.Id));
        var login = geste.Agent.CompteWindows;
        var traces = new List<TraceGeste>();
        var ids = new List<string>();
        var editions = 0;
        foreach (var a in affaires.Values)
        {
            var (demande, refusDemande) = await CreerDemandeAsync(geste, CodeProcessus, "V", ct);
            if (demande is null) return IssueGeste.Refuser(refusDemande!);
            await RattacherAsync(geste, demande, a.Nom, a.Numero, a.Numero, a.Sequenceur, a.Id.ToString(CultureInfo.InvariantCulture), ct);
            await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_ID_DMND_MERE = @mere WHERE DMND_ID_DMND = @id", ct,
                new SqlParameter("@mere", traitement.Id), new SqlParameter("@id", demande.Id));
            foreach (var p in a.Pieces)
            {
                var numeroDocument = Guid.NewGuid().ToString().ToUpperInvariant();
                await InsererAsync(geste, "v2.GED_RIG", "GDRG_ID_GED_RIG", new Dictionary<string, object?>
                {
                    ["GDRG_NUM_DOCUMENT"] = numeroDocument, ["GDRG_GUID_VIDIGED"] = p.Guid, ["GDRG_BASE_VIDIGED"] = "JUDICIAIRE",
                    ["GDRG_EXTENSION"] = Tronque(p.Extension, 10), ["GDRG_LOGIN_NT_ARCHIVAGE"] = Tronque(login, 35),
                    ["GDRG_DATE_ARCHIVAGE"] = geste.Maintenant,
                }, ct);
                await InsererAsync(geste, "v2.DEMANDE_EDITION", "DMNED_ID_DMNED", new Dictionary<string, object?>
                {
                    ["DMNED_ID_DMND"] = demande.Id, ["DMNED_DATE_CREATION"] = geste.Maintenant, ["DMNED_TYPE_PAPIER"] = "",
                    ["DMNED_MEDIA"] = "IMPR", ["DMNED_ETAT_EMISSION"] = "N", ["DMNED_PHASE_TRAITEMENT"] = "P",
                    ["DMNED_UTILISATEUR"] = Tronque(login, 32), ["DMNED_INTERLOCUTEUR"] = Tronque(login, 32),
                    ["DMNED_NOMBRE_EXEMPLAIRE"] = (short)1, ["DMNED_IMPRESSION"] = true, ["DMNED_PAPIER_TRAME"] = false, ["DMNED_AGRAFAGE"] = false,
                    ["DMNED_RECTO_VERSO"] = false, ["DMNED_AFF_AVANT_IMPRESSION"] = false, ["DMNED_TYPE_GENERATION"] = "EMISSION_SIMPLE",
                    ["DMNED_LIBELLE_DOCUMENT"] = Tronque(p.Libelle, 255), ["DMNED_GROUPAGE_AUTOMATIQUE"] = true, ["DMNED_RESUME"] = false,
                    ["DMNED_READ_ONLY"] = false, ["DMNED_PARAMETRAGE_COPIE_CONFORME"] = "", ["DMNED_CODE_ENTITE_SOURCE"] = "VIDIGED_INDEX",
                    ["DMNED_VALEUR_ID_SOURCE"] = p.Vidiged.ToString(CultureInfo.InvariantCulture), ["DMNED_NUM_DOCUMENT_EDITION"] = numeroDocument,
                    ["DMNED_IMPRIMANTE"] = imprimante, ["DMNED_LOT"] = "", ["DMNED_DESIGNATION_DESTINATAIRE"] = "Document interne",
                }, ct);
                editions++;
            }
            ids.Add(demande.Id.ToString(CultureInfo.InvariantCulture));
            traces.Add(new TraceGeste("demande", $"la demande {demande.Numero} de l'affaire {a.Numero}", demande.Id.ToString(CultureInfo.InvariantCulture),
                "a cree", null, new Dictionary<string, string>
                {
                    ["Pièces numérisées à imprimer"] = string.Join(", ", a.Pieces.Select(p => p.Libelle)),
                    ["Imprimante"] = imprimante ?? "",
                    ["Demande du traitement"] = traitement.Numero,
                }));
        }

        var message = $"{(editions > 1 ? $"{editions} pièces numérisées mises" : "1 pièce numérisée mise")} à imprimer pour "
                    + $"{(n > 1 ? $"{n} affaires" : "1 affaire")}. Rien n'est imprimé ni envoyé : "
                    + "le service d'impression de RIG v2 n'existe pas dans la V3, rien ne sort du poste.";
        return IssueGeste.Ecrit(message, [.. ids], [.. traces]);
    }

    private static bool Entier(string s, out int n) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n);

    private static string Tronque(string s, int longueur) => s.Length <= longueur ? s : s[..longueur];

    /// <summary>La valeur d'une preference (JSON, ServicePreferences) : une chaine JSON, sinon le texte tel quel.</summary>
    private static string? TexteJson(string valeur)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(valeur) is { ValueKind: JsonValueKind.String } e ? e.GetString() : valeur;
        }
        catch (JsonException)
        {
            return valeur;
        }
    }
}
