using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurSignatureElectronique;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Signature électronique » (PROC_SIGNJUD) : « Envoyer au juge », « Signer les décisions » (le greffier) et « Corriger ». RIG v2 y
/// appelle deux services externes, que la V3 N'APPELLE PAS : le parapheur Oodrive (envoi au juge) et l'outil CertSign (signature du
/// greffier). La V3 SIMULE leur partie externe -- aucun document, aucun courriel, aucune signature -- et ecrit dans la base du greffe
/// l'etat que RIG v2 ecrit pour la decision ; chaque message le dit (« simulation »). Mode d'emploi : EcrivainEcran.cs ; la liste :
/// LecteurSignatureElectronique.cs. Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_SIGNJUD\ (proc_signjud.md, Detail OPE) --
///   - « Envoyer au juge » (MetierSignJud.TraiterDecisionsASignerParJuge, :883-1011) : une decision deja passee par Oodrive (a
///     corriger) en est detachee (OODDS_ID_DCJDC = NULL, :929-936), puis une ligne OODRIVE_DOCUMENT_SIGNE neuve (OodriveDocumentSignature.
///     Create : etat EN_ATTENTE, date de creation, abreges du juge et du greffier ; fichier « <role>_<type>.pdf », decision, instance).
///     SIMULE : la preparation du PDF (SignJudDocument, data-matrix, DetectionCabJud.exe), son stockage (BDStockage) et l'envoi au juge
///     (OodriveDocumentSignature.SendMailJuge ou le site local) ; une decision envoyee reste « Chez le juge » jusqu'au retour d'Oodrive ;
///   - « Signer les décisions » (TraiterDecisionsASignerParGreffier, :1017-1140) : CertSign signe, RIG v2 relit l'etat SIGNE_GREFFIER,
///     met le document signe en GED et met a jour la centralisation de l'instance (Instance.UpdateCentralisation, :1107). SIMULE :
///     l'appel de CertSign (la V3 ecrit SIGNE_GREFFIER et la date de mise a jour du greffier, comme apres une signature reussie) ;
///     NON REPRIS : la mise en GED du document signe (VidigedIndex) ;
///   - « Corriger » (le menu de la grille, DataGridViewSignJudDecision.cs:292-349 ; SignJudDecision.UpdateOodriveEtatToCorrection,
///     :459-495) : la ligne Oodrive passe A_CORRIGER ; tant que le juge n'a pas signe (EN_ATTENTE, EN_COURS_JUGE), tout le lot du meme
///     dossier Oodrive du juge (OODDS_GUID_DOSSIER_OO_JUGE) aussi. Aucun service externe.
/// CONTROLES, avant toute ecriture (un refus et rien n'est ecrit) -- les erreurs metier de SignJudDecision.UpdateErreursMetier
/// (:500-553), au mot pres, et CanSignature (:345-357) :
///   - commun : « Pas de juge », « Pas de greffier », « Date prévue vide » (hors REDA) ;
///   - envoi : la decision est a envoyer ou a corriger ; « Plusieurs décisions éditées, allez en POOL_EDIT pour n'en garder qu'une »,
///     « Pas d'édition de la décision », « Édition de la décision en REDA et hors REDA », « Pas de mail pour le juge », « Le juge n'a pas
///     de clé électronique paramétré », « Le juge n'est pas paramétré pour signer électroniquement ce type d'instance », et pour une
///     decision a corriger « L'ORDOSIGN a été fait » / « L'état de rédaction en REDA n'est pas correct » ;
///   - signature : la decision est signee par le juge ; « Vous n'êtes pas le greffier signataire » (l'agent n'est pas l'utilisateur
///     RIG v2 du greffier, soc.Compte.IdV2), « Utilisateur RIG non paramétré pour le greffier », « Pièce numérisée non cochée dans la
///     décision » ;
///   - correction : la decision est passee par Oodrive, ni signee par le greffier ni deja a corriger (_CanChangeEtatCorrection, :448-451).
/// Traces : famille « affaire », « a modifie », l'etat Oodrive avant et apres (« a cree » pour une ligne Oodrive neuve).
///
/// NON REPRIS (nomme) : la demande RIG creee par lot reussi (Demande.GestionDemandeForProcessus, OPE_RESULTAT_SIGNJUD.cs:218) : une
/// trace, remplacee par le journal des gestes ; le lotissement par juge (NbDjLotEnvoiJuge) : un envoi = les decisions cochees ; les
/// controles de requete en GED (« Pas de requête en GED », « Plusieurs requêtes en GED ») ; les positions des signatures.
/// </summary>
internal sealed class EcrivainSignatureElectronique : IEcrivainEcran
{
    public string Procedure => "PROC_SIGNJUD";
    public string Ecran => "Signature électronique";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les gestes du descripteur, au mot pres (le bouton TRAITER relibelle selon l'onglet, OPE_RESULTAT_SIGNJUD.cs:64-73, et
    /// le menu « Corriger »).</summary>
    internal const string EnvoyerAuJuge = "Envoyer au juge", Signer = "Signer les décisions", Corriger = "Corriger";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var nom = geste.Demande.Nom;
        if (nom is not (EnvoyerAuJuge or Signer or Corriger)) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser("Aucune décision cochée : rien n'est traité.");

        int? utilisateur = null;
        if (nom == Signer)
            await using (var r = await geste.LireAsync("SELECT IdV2 FROM soc.Compte WHERE CompteWindows = @c", ct, new SqlParameter("@c", geste.Agent.CompteWindows)))
                if (await r.ReadAsync(ct) && !r.IsDBNull(0)) utilisateur = Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture);

        var decisions = new List<Decision>();
        foreach (var id in ids)
        {
            Decision? d = null;
            if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                d = (await DecisionsAsync((sql, p) => geste.LireAsync(sql, ct, p), geste.Maintenant, 1, n, ct)).Lignes.FirstOrDefault();
            if (d is null) return IssueGeste.Refuser($"Décision introuvable dans la liste des décisions à signer ({id}).");
            if (Erreur(nom, d, utilisateur) is { } erreur) return IssueGeste.Refuser($"Rôle {d.Role} : {erreur}");
            decisions.Add(d);
        }

        var traces = new List<TraceGeste>();
        foreach (var d in decisions)
        {
            var idTexte = d.Id.ToString(CultureInfo.InvariantCulture);
            var objet = $"la signature de la décision du rôle {d.Role}";
            if (nom == EnvoyerAuJuge)
            {
                if (d.IdOodrive is { } ancien)
                    await geste.EcrireAsync("UPDATE v2.OODRIVE_DOCUMENT_SIGNE SET OODDS_ID_DCJDC = NULL WHERE OODDS_ID_OODDS = @o", ct, new SqlParameter("@o", ancien));
                await geste.InsererAsync("v2.OODRIVE_DOCUMENT_SIGNE", "OODDS_ID_OODDS", new Dictionary<string, object?>
                {
                    ["OODDS_DATE_CREATION"] = geste.Maintenant, ["OODDS_ETAT"] = "EN_ATTENTE", ["OODDS_NOM_FICHIER"] = $"{d.Role}_{d.LibelleType}.pdf",
                    ["OODDS_ABREGE_JUGE"] = d.AbregeJuge, ["OODDS_ABREGE_GREFFIER"] = d.AbregeGreffier, ["OODDS_SIGNATURE_JUGE"] = "SIGNJUD_IMAGE",
                    ["OODDS_SIGNATURE_GREFFIER"] = "SIGNJUD_IMAGE", ["OODDS_ID_DCJDC"] = d.Id, ["OODDS_ID_INSTN"] = d.IdInstance,
                }, ct);
                traces.Add(new TraceGeste("affaire", objet, idTexte, d.IdOodrive is null ? "a cree" : "a modifie",
                    d.IdOodrive is null ? null : new { Etat = d.EtatOodrive }, new { Etat = "EN_ATTENTE", Oodrive = "simulé : rien n'est envoyé" }));
            }
            else if (nom == Signer)
            {
                await geste.EcrireAsync("UPDATE v2.OODRIVE_DOCUMENT_SIGNE SET OODDS_ETAT = 'SIGNE_GREFFIER', OODDS_DATE_MAJ_GREFFIER = @m WHERE OODDS_ID_OODDS = @o", ct,
                    new SqlParameter("@m", geste.Maintenant), new SqlParameter("@o", d.IdOodrive!.Value));
                // Instance.UpdateCentralisation (Instance.cs:4682-4686), apres la mise en GED de RIG v2 (:1107).
                await geste.EcrireAsync("UPDATE v2.INSTANCE SET INSTN_TOP_EXPERIAN = 0, INSTN_DATE_MAJ_INFOG = @m WHERE INSTN_ID_INSTN = @i", ct,
                    new SqlParameter("@m", geste.Maintenant), new SqlParameter("@i", d.IdInstance));
                traces.Add(new TraceGeste("affaire", objet, idTexte, "a modifie", new { Etat = d.EtatOodrive }, new { Etat = "SIGNE_GREFFIER", CertSign = "simulé : rien n'est signé" }));
            }
            else
            {
                // Le lot du juge tant qu'il n'a pas signe (:468-482) ; sinon la decision seule.
                var lot = d.EtatOodrive is "EN_ATTENTE" or "EN_COURS_JUGE" && d.GuidJuge is not null;
                var n = lot
                    ? await geste.EcrireAsync("UPDATE v2.OODRIVE_DOCUMENT_SIGNE SET OODDS_ETAT = 'A_CORRIGER' WHERE OODDS_GUID_DOSSIER_OO_JUGE = @g AND OODDS_ETAT NOT IN ('SIGNE_GREFFIER', 'A_CORRIGER')",
                                              ct, new SqlParameter("@g", Guid.Parse(d.GuidJuge!)))
                    : await geste.EcrireAsync("UPDATE v2.OODRIVE_DOCUMENT_SIGNE SET OODDS_ETAT = 'A_CORRIGER' WHERE OODDS_ID_OODDS = @o", ct, new SqlParameter("@o", d.IdOodrive!.Value));
                traces.Add(new TraceGeste("affaire", objet, idTexte, "a modifie", new { Etat = d.EtatOodrive }, new { Etat = "A_CORRIGER", Documents = n }));
            }
        }

        var nombre = decisions.Count;
        var message = nom switch
        {
            EnvoyerAuJuge => (nombre > 1 ? $"{nombre} décisions envoyées" : "Décision envoyée") + " au juge pour signature (simulation : Oodrive n'est pas appelé, aucun document n'est envoyé).",
            Signer => (nombre > 1 ? $"{nombre} décisions contresignées" : "Décision contresignée") + " par le greffier (simulation : CertSign n'est pas appelé, rien n'est signé ni mis en GED).",
            _ => (nombre > 1 ? $"{nombre} décisions passées" : "Décision passée") + " « à corriger » : elle repart au juge.",
        };
        return IssueGeste.Ecrit(message, [.. decisions.Select(d => d.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }

    /// <summary>La premiere erreur metier de RIG v2 qui interdit le geste <paramref name="geste"/> sur <paramref name="d"/> ; null sinon
    /// (SignJudDecision.UpdateErreursMetier, :500-553, et CanSignature, :345-357).</summary>
    internal static string? Erreur(string geste, Decision d, int? utilisateur)
    {
        if (geste == Corriger)
            return d.EtatOodrive is null || d.EtatOodrive is "SIGNE_GREFFIER" or "A_CORRIGER"
                ? $"la décision {(d.EtatOodrive is null ? "n'est pas passée par Oodrive" : d.EtatOodrive == "A_CORRIGER" ? "est déjà à corriger" : "est signée par le greffier")} : elle ne passe pas « à corriger »."
                : null;
        var (groupe, etat) = EtatDe(d.EtatOodrive);
        if (geste == EnvoyerAuJuge && !(groupe == PourLeJuge && etat is "AFaire" or "EnCours"))
            return "la décision n'est ni à envoyer ni à corriger : elle ne part pas au juge.";
        if (geste == Signer && !(groupe == PourLeGreffier && etat == "AFaire"))
            return "la décision n'est pas signée par le juge : le greffier ne la contresigne pas.";
        if (d.IdJuge is null) return "Pas de juge";
        if (d.IdGreffier is null) return "Pas de greffier";
        var editionReda = d.EditionsReda > 0;
        if (d.Prevue is null && !editionReda) return "Date prévue vide";
        if (geste == EnvoyerAuJuge)
        {
            if (d.EditionsHorsReda > 1) return "Plusieurs décisions éditées, allez en POOL_EDIT pour n'en garder qu'une";
            if (d.EditionsHorsReda == 0 && d.EditionsReda == 0) return "Pas d'édition de la décision";
            if (d.EditionsHorsReda != 0 && d.EditionsReda != 0) return "Édition de la décision en REDA et hors REDA";
            if (d.MailJuge.Length == 0) return "Pas de mail pour le juge";
            if (d.TypesDematJuge.Length == 0) return "Le juge n'a pas de clé électronique paramétré";
            if (!d.TypesDematJuge.Contains(d.TypeInstance, StringComparison.Ordinal)) return "Le juge n'est pas paramétré pour signer électroniquement ce type d'instance";
            if (d.EtatOodrive == "A_CORRIGER")
            {
                if (d.EtatRedaction is "" or "0" && d.Rendue is not null) return "L'ORDOSIGN a été fait";
                if (d.EtatRedaction is not ("" or "0" or "3")) return "L'état de rédaction en REDA n'est pas correct";
            }
            return null;
        }
        if (d.UtilisateurGreffier is null) return "Utilisateur RIG non paramétré pour le greffier";
        if (d.UtilisateurGreffier != utilisateur) return "Vous n'êtes pas le greffier signataire";
        if (!d.PieceNumerisee) return "Pièce numérisée non cochée dans la décision";
        return null;
    }
}
