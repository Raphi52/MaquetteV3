using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Commandes web » (PROC_COMMANDES_WEB, écran fait main web/src/ecrans/EcranCommandesWeb.tsx) : les gestes METTRE EN ATTENTE et
/// VALIDER une commande, écrits dans la base du greffe. Joués par les routes POST RoutesTraitementCommandesWeb.MettreEnAttente et
/// .Valider (ModuleCommandesWeb.cs) dans la transaction et le journal des gestes du socle (EcrivainsEcrans.JouerAsync, sa version pour
/// un écran fait main) : mêmes gardes d'écriture (v2.* seulement). Plan « RIG v3 complet, mesuré par le banc du parcours complet »,
/// lot 7, 28/11/2026 ; la lecture : LectureCommandesWeb.cs.
///
/// SOURCE : PAS D'ÉCRAN RIG v2 (LectureCommandesWeb.cs ; D:\RigApplication\Source\Edi\Exe\SuiviCommandeWeb n'a qu'une fenêtre vide) :
/// l'écran et la table COMMANDE_WEB sont ceux de NéoRIG (D:\RIG-V3, lu sans y toucher), dont la V3 reprend les règles :
///   - les gestes : TraiterCommandeWebService (src\RIG.Application\Rcs\CommandesWeb\TraiterCommandeWebService.cs) MettreEnAttenteAsync
///     (:310-315) et ValiderAsync (:147-180), puis TraiterAsync (:345-551) : le motif normalisé avant toute écriture (:371), la cible
///     vérifiée (:374, VerifierCible), la commande relue dans la transaction (:382-384, absente : CommandesWebErrors.Introuvable), la
///     transition vérifiée (:387), une saisie au guichet sans client refusée (:391-392), le Kbis rejoué sur le dossier retenu, disparu :
///     CommandesWebErrors.CibleDisparue (:437-445, DeciderKbisAsync :835-845) ;
///   - les règles (src\RIG.Domain\Rcs\CommandesWeb\TraitementCommandeWeb.cs) : une commande À TRAITER (A) se met en attente ou se valide,
///     une commande EN ATTENTE (T) se valide mais ne se remet pas en attente (« Cette commande est déjà en attente : validez-la ou
///     rejetez-la. »), une commande validée, rejetée ou saisie au guichet (V, R, S) « Cette commande n'est plus à traiter… » (EstAutorisee
///     :62-67, Verifier :70-76, messages :134-140) ; le motif est facultatif, sans espaces de bord, vide = NULL, au plus 500 caractères
///     (NormaliserMotif :82-96) ; Valider exige une cible (VerifierCible :102-105, CibleNonRetenue :155-157) ;
///   - l'écriture (src\RIG.Infrastructure\Persistence\Repositories\CommandeWebTraitementRepository.cs, EnregistrerTraitementAsync
///     :48-63) : CMDWB_ETAT, CMDWB_MOTIF, CMDWB_DATE_TRAITEMENT (NULL pour une mise en attente, maintenant pour une validation,
///     TraiterCommandeWebService.cs:457-460), CMDWB_ID_UTLST = l'opérateur (son identifiant RIG v2, soc.Compte.IdV2 ; vide s'il n'en a
///     pas, IdentifierOperateurAsync :778-790), CMDWB_DATE_MODIFICATION = maintenant, SOUS CONDITION de l'état lu (WHERE … CMDWB_ETAT =
///     l'état lu) : 0 ligne = traitée entre-temps sur un autre poste (:464-465), même refus ;
///   - la DEMANDE : une mise en attente n'y touche pas (CloturerDemandeAsync :558-561) ; une validation (CreerDemandeDeValidationAsync
///     :629-668) abandonne la demande de réception encore ouverte (AbandonnerReceptionAsync :700-706 ; DemandeCycleDeVie.AbandonnerAsync :
///     qualifiée « Q » -> supprimée « X », sinon seulement plus en cours), crée la demande de la validation au processus CMD_WEB
///     (CodeProcessusCommandeWeb.cs:20-28), nommée « Infogreffe - N° … du jj/mm/aaaa » (NomDemandeCommandeWeb.cs:17-38), avec son auteur
///     et l'opérateur (TracerDemandeAsync, CommandeWebTraitementRepository.cs:127-143), rattachée au dossier retenu et à son numéro de
///     gestion (RenseignerCibleAsync :70-96), close validée « V », et la commande pointe sur elle (ChangerDemandeAsync :145-148). L'affaire
///     et le numéro DMD de la demande : CreationV2.CreerDemandeAsync, comme le moteur de RIG v2.
/// Types des colonnes : Documentation\scripts\1824-creer-commande-web.sql:47-52 (CMDWB_ID_DMND :27).
/// NON REPRIS, nommés dans le message de Valider : l'ÉDITION de la validation (certificats, état d'endettement, lettre d'accompagnement,
/// envoi par mail) -- NéoRIG la demande APRÈS l'enregistrement, hors de sa transaction, au service d'édition de RIG v2, et son échec ne
/// défait pas la validation (TraiterCommandeWebService.cs:20-35, :163-170 ; EditionValidationDemandeEditionV2.cs) ; elle part à
/// l'adresse de livraison du flux XML reçu (CMDWB_FLUX), que la V3 ne lit pas (LectureCommandesWeb.cs). REFUSÉS (rien d'écrit) : la
/// saisie au guichet (le choix des clients facturé et destinataire, :391-392) et le certificat de non-opposition (le contrôle de
/// délivrance, :416-425, que la V3 ne joue pas en base). REJETER (notification à Infogreffe) reste refusé par le pont.
/// TRACE : famille « demande », « a modifie ».
/// </summary>
public static class EcritureCommandesWeb
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Commandes web";

    /// <summary>La longueur de CMDWB_MOTIF (TransitionsCommandeWeb.LongueurMaxMotif).</summary>
    public const int LongueurMaxMotif = 500;

    /// <summary>Le processus des demandes de commande web (CodeProcessusCommandeWeb.CommandeWeb).</summary>
    public const string Processus = "CMD_WEB";

    // Les messages de NéoRIG, au mot près (TraitementCommandeWeb.cs:134-157, CommandesWebErrors.cs:35-37 et :62-64,
    // ClientsCommandeManuelle.cs:25-27).
    public const string Introuvable = "Cette commande n'existe plus. Actualisez la liste.";
    public const string DejaTraitee = "Cette commande n'est plus à traiter : elle a été traitée entre-temps, sur un autre poste. Actualisez la liste.";
    public const string DejaEnAttente = "Cette commande est déjà en attente : validez-la ou rejetez-la.";
    public static readonly string MotifTropLong = $"Le motif ne peut pas dépasser {LongueurMaxMotif} caractères.";
    public const string CibleNonRetenue = "Rattachez d'abord la commande à un dossier ou à un non-inscrit : l'état est édité sur ses inscriptions.";
    public const string CibleDisparue = "Le dossier retenu n'existe plus. Relancez la recherche et retenez une autre cible.";

    /// <summary>ClientsCommandeManuelleErrors.FactureManquante, et ce que la V3 ne reprend pas.</summary>
    public const string SansClients = "Choisissez un client à facturer, ou cochez « Non facturable ». RIG v3 ne reprend pas encore le choix des "
                                    + "clients d'une saisie au guichet : rien n'est enregistré.";

    /// <summary>Le certificat de non-opposition : NéoRIG rejoue le contrôle de délivrance avant d'écrire ; la V3 ne le joue pas en base.</summary>
    public const string SansControleDelivrance = "Le certificat de non-opposition se délivre après le contrôle de délivrance (date de la "
                                               + "dernière formalité du dossier), que RIG v3 ne reprend pas encore : rien n'est enregistré.";

    /// <summary>Ce que la validation n'écrit pas encore, dit à l'agent avec le geste fait.</summary>
    public const string SansEdition = "Ses documents ne sont pas édités : RIG v3 ne reprend pas encore l'édition de la validation (certificats, "
                                    + "état d'endettement, lettre d'envoi), que NéoRIG demande ensuite au service d'édition de RIG v2.";

    /// <summary>Les gestes écrits : tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Decider];

    /// <summary>Met en attente la commande Ids[0], avec le motif saisi.</summary>
    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (!int.TryParse(geste.Demande.Ids.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser(Introuvable);
        // Le motif d'abord, sans base (TraiterCommandeWebService.cs:371).
        var motif = geste.Demande.Valeurs.TryGetValue(RoutesTraitementCommandesWeb.ChampMotif, out var saisi) && !string.IsNullOrWhiteSpace(saisi)
            ? saisi.Trim() : null;
        if (motif is { Length: > LongueurMaxMotif }) return IssueGeste.Refuser(MotifTropLong);

        string etat, reference;
        await using (var r = await geste.LireAsync(
            "SELECT CMDWB_ETAT, CMDWB_REFERENCE_COMMANDE, CMDWB_MOTIF FROM v2.COMMANDE_WEB WITH (UPDLOCK) WHERE CMDWB_ID_CMDWB = @id",
            ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser(Introuvable);
            etat = FormeLigne.Texte(r, "CMDWB_ETAT").Trim();
            reference = FormeLigne.Texte(r, "CMDWB_REFERENCE_COMMANDE").Trim();
        }
        if (etat == "T") return IssueGeste.Refuser(DejaEnAttente);
        if (etat != "A") return IssueGeste.Refuser(DejaTraitee);

        var operateur = await OperateurAsync(geste, ct);
        var ecrites = await geste.EcrireAsync("""
            UPDATE v2.COMMANDE_WEB
            SET CMDWB_ETAT = 'T', CMDWB_MOTIF = @motif, CMDWB_DATE_TRAITEMENT = NULL, CMDWB_ID_UTLST = @operateur,
                CMDWB_DATE_MODIFICATION = @maintenant
            WHERE CMDWB_ID_CMDWB = @id AND CMDWB_ETAT = 'A'
            """, ct,
            new SqlParameter("@motif", (object?)motif ?? DBNull.Value), new SqlParameter("@operateur", (object?)operateur ?? DBNull.Value),
            new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@id", id));
        if (ecrites == 0) return IssueGeste.Refuser(DejaTraitee);

        var nom = reference.Length > 0 ? reference : id.ToString(CultureInfo.InvariantCulture);
        return IssueGeste.Ecrit($"Commande {nom} mise en attente.", [id.ToString(CultureInfo.InvariantCulture)],
            new TraceGeste("demande", $"la commande web {nom}", id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { Etat = "A" }, new { Etat = "T", Motif = motif }));
    }

    /// <summary>Valide la commande Ids[0] sur le dossier RCS retenu (Valeurs[ChampDossier], son identifiant).</summary>
    public static async Task<IssueGeste> ValiderAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (!int.TryParse(geste.Demande.Ids.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser(Introuvable);
        // La cible d'abord, sans base (TraiterCommandeWebService.cs:374).
        if (!geste.Demande.Valeurs.TryGetValue(RoutesTraitementCommandesWeb.ChampDossier, out var cible) || string.IsNullOrWhiteSpace(cible))
            return IssueGeste.Refuser(CibleNonRetenue);
        if (!int.TryParse(cible.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var idDossier)) return IssueGeste.Refuser(CibleDisparue);

        string etat, reference, origine;
        int famille;
        DateTime dateCommande;
        int? demandeReception;
        await using (var r = await geste.LireAsync("""
            SELECT CMDWB_ETAT, CMDWB_REFERENCE_COMMANDE, CMDWB_ORIGINE, CMDWB_FAMILLE, CMDWB_DATE_COMMANDE, CMDWB_ID_DMND
            FROM v2.COMMANDE_WEB WITH (UPDLOCK) WHERE CMDWB_ID_CMDWB = @id
            """, ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser(Introuvable);
            etat = FormeLigne.Texte(r, "CMDWB_ETAT").Trim();
            reference = FormeLigne.Texte(r, "CMDWB_REFERENCE_COMMANDE").Trim();
            origine = FormeLigne.Texte(r, "CMDWB_ORIGINE").Trim();
            famille = r["CMDWB_FAMILLE"] is DBNull ? 0 : Convert.ToInt32(r["CMDWB_FAMILLE"], CultureInfo.InvariantCulture);
            dateCommande = FormeLigne.DateDe(r, "CMDWB_DATE_COMMANDE") ?? geste.Maintenant;
            demandeReception = r["CMDWB_ID_DMND"] is DBNull ? null : Convert.ToInt32(r["CMDWB_ID_DMND"], CultureInfo.InvariantCulture);
        }
        // EstAutorisee (:62-67) : A et T se valident ; V, R, S non.
        if (etat is not ("A" or "T")) return IssueGeste.Refuser(DejaTraitee);
        if (origine == "MAN") return IssueGeste.Refuser(SansClients);
        if (famille == 13) return IssueGeste.Refuser(SansControleDelivrance);

        string gestion;
        await using (var r = await geste.LireAsync("SELECT NumeroGestion FROM rec.DossierRcs WHERE Id = @d", ct, new SqlParameter("@d", idDossier)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser(CibleDisparue);
            gestion = FormeLigne.Texte(r, "NumeroGestion").Trim();
        }

        var operateur = await OperateurAsync(geste, ct);
        var ecrites = await geste.EcrireAsync("""
            UPDATE v2.COMMANDE_WEB
            SET CMDWB_ETAT = 'V', CMDWB_MOTIF = NULL, CMDWB_DATE_TRAITEMENT = @maintenant, CMDWB_ID_UTLST = @operateur,
                CMDWB_DATE_MODIFICATION = @maintenant
            WHERE CMDWB_ID_CMDWB = @id AND CMDWB_ETAT = @etat
            """, ct,
            new SqlParameter("@operateur", (object?)operateur ?? DBNull.Value), new SqlParameter("@maintenant", geste.Maintenant),
            new SqlParameter("@id", id), new SqlParameter("@etat", etat));
        if (ecrites == 0) return IssueGeste.Refuser(DejaTraitee);

        // AbandonnerReceptionAsync (:700-706) : la demande de réception encore ouverte n'a rien produit.
        if (demandeReception is { } reception)
            await geste.EcrireAsync("""
                UPDATE v2.DEMANDE
                SET DMND_ETAT_DEMANDE = CASE WHEN DMND_ETAT_DEMANDE = 'Q' THEN 'X' ELSE DMND_ETAT_DEMANDE END, DMND_EN_COURS = 0
                WHERE DMND_ID_DMND = @reception AND DMND_EN_COURS = 1 AND LTRIM(RTRIM(DMND_CODE_PROSS)) = @processus
                """, ct, new SqlParameter("@reception", reception), new SqlParameter("@processus", Processus));

        // CreerDemandeDeValidationAsync (:629-668) : la demande de la validation, nommée, rattachée au dossier, close validée.
        var (demande, refus) = await CreationV2.CreerDemandeAsync(geste, Processus, "V", ct);
        if (demande is null) return IssueGeste.Refuser($"{refus} La commande n'est pas validée.");
        var nomDemande = NomDemande(origine, reference, dateCommande);
        await geste.EcrireAsync("""
            UPDATE v2.DEMANDE SET DMND_NOM = LEFT(@nom, 255), DMND_ID_UTLST = @operateur, DMND_ID_DSRCS = @dossier,
                                  DMND_NUM_INDICATIF = LEFT(@gestion, 14)
            WHERE DMND_ID_DMND = @demande
            """, ct, new SqlParameter("@nom", nomDemande), new SqlParameter("@operateur", (object?)operateur ?? DBNull.Value),
            new SqlParameter("@dossier", idDossier), new SqlParameter("@gestion", gestion), new SqlParameter("@demande", demande.Id));
        await geste.EcrireAsync("UPDATE v2.COMMANDE_WEB SET CMDWB_ID_DMND = @demande WHERE CMDWB_ID_CMDWB = @id", ct,
            new SqlParameter("@demande", demande.Id), new SqlParameter("@id", id));

        var nom = reference.Length > 0 ? reference : id.ToString(CultureInfo.InvariantCulture);
        return IssueGeste.Ecrit($"Commande {nom} validée et rattachée au dossier {gestion} (demande {demande.Numero}). {SansEdition}",
            [id.ToString(CultureInfo.InvariantCulture), demande.Numero],
            new TraceGeste("demande", $"la commande web {nom}", id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { Etat = etat, Demande = demandeReception }, new { Etat = "V", Dossier = gestion, Demande = demande.Numero }));
    }

    /// <summary>NomDemandeCommandeWeb.Pour (NomDemandeCommandeWeb.cs:17-38) : l'origine, la référence sans son préfixe, la date.</summary>
    internal static string NomDemande(string origine, string reference, DateTime dateCommande)
    {
        var date = dateCommande.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var tiret = reference.IndexOf('-', StringComparison.Ordinal);
        var sansPrefixe = tiret > 0 && (reference.StartsWith("IG-", StringComparison.OrdinalIgnoreCase)
                                        || reference.StartsWith("SL-", StringComparison.OrdinalIgnoreCase)
                                        || reference.StartsWith("MAN-", StringComparison.OrdinalIgnoreCase))
            ? reference[(tiret + 1)..] : reference;
        return origine switch
        {
            "SL" => $"Site local - N° {sansPrefixe} du {date}",
            "MAN" => $"Guichet — saisie du {date}",
            _ => $"Infogreffe - N° {sansPrefixe} du {date}",
        };
    }

    /// <summary>L'opérateur dans les tables de RIG v2 : l'utilisateur V2 du compte (soc.Compte.IdV2), vide s'il n'en a pas.</summary>
    private static async Task<int?> OperateurAsync(GesteEnCours geste, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT IdV2 FROM soc.Compte WHERE CompteWindows = @c", ct,
                                                  new SqlParameter("@c", geste.Agent.CompteWindows));
        return await r.ReadAsync(ct) && r["IdV2"] is int v ? v : null;
    }
}
