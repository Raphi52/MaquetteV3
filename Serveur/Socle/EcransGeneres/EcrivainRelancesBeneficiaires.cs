using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurRelancesBeneficiaires;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Relances beneficiaires » (PROC_DBE_RELANCE) : « Générer les courriers de relance » fait la relance 1 des dossiers coches
/// (lignes « A relancer », Id « dossier:<id> »). Mode d'emploi : EcrivainEcran.cs ; la liste et la regle « A relancer » :
/// LecteurRelancesBeneficiaires.cs. Plan « RIG v3 100 % fonctionnel », lot 8, 26/11/2026.
///
/// SOURCE (ancien RIG) : le moteur de relance qui tourne, PROC_RSURV (SURVEILLANCES\PROC_RSURV\PROC_RSURV.cs), nature SDBE,
/// relance 1 : la branche des natures sans exercice de Sauvegarder (l. 159-166), les parametres de la demande (l. 197-209), ses
/// courriers (GenererLesEditionsRelance, l. 320-380), la demande du lot (l. 225-226) et la cloture des demandes a la validation
/// (l. 245-247) ; les reglages par numero de relance dans NATURE_DILIGENCE_PARAM (nature SDBE, processus RSURV). La classe de la
/// fiche proc_dbe_relance.md, PROC_RDBE, ne compile plus (LecteurRelancesBeneficiaires.cs, en tete).
///
/// CONTROLES, dans l'ordre, avant toute ecriture :
///   1. au plus 1500 relances a la fois (DoVerifier, PROC_RSURV.cs:53-57), au message de RIG v2 ;
///   2. chaque ligne cochee est un dossier A RELANCER (la regle du lecteur, relue dans la transaction) : RIG v2 ne propose
///      pas les autres (sa recherche ne les selectionne pas) ; une diligence deja relancee, ou un dossier qui ne l'est plus,
///      est refuse, sans rien ecrire.
///
/// CE QUI S'ECRIT, par dossier (la branche « else » de Sauvegarder, l. 159-166) :
///   - rec.ActionRegistre : une action du dossier (trt.Dmnd.AddActionRcs, Demande.cs:1149-1153 -> ActionRcs.CreateActionRcs,
///     ActionRcs.cs:45-54) : code processus = la nature « SDBE », arrivee du jour, numero d'arrivee RA1 de l'annee, etat info
///     « 1 » (etatInfo.processusRIG, Metier.cs:16), etat « 2 » (ActionValidee, ActionRcs.cs:18) ;
///   - v2.DILIGENCE_RCS : la diligence (action.AddDiligenceRcs("G", nature, "NP", etat, "2"), ActionRcs.cs:391-404) : origine
///     « G », nature SDBE, motif « NP », en cours « 1 » (ligne cochee), etat relance « 2 » ; etat EDI « 1 » (eEtatRelance.AEnvoyer)
///     seulement si le reglage EnvoiEdi de la nature vaut « 1 » (l. 193-195 ; SDBE : 0 dans RIG_DEV) ;
///   - v2.RELANCE_RCS : la relance 1 (dlg.AddRelanceRcs(NumRelance, "3", "1", type, aujourd'hui), l. 164) : auteur « 3 »
///     (greffe), intervenant « 1 », type = le reglage TypeRelance, « S » sans reglage (l. 334-335), date limite = aujourd'hui +
///     DelaiRelance (« Mois=1 » dans RIG_DEV ; RelancesRsurv.Limite), non facturee ;
///   - v2.DEMANDE : une demande RSURV validee du dossier, « Relance 1 SDBE : <designation> » (l. 344-345), numero indicatif =
///     numero de gestion, fille de la demande du lot, portant l'action (DMND_ID_ACTRC), parametres « NumRelance=1;
///     NatureDiligence=SDBE;IdDiligenceRcs=<id> » (l. 207-209) ; et UNE demande du lot, « Relances du groupe <groupe de la
///     nature> - n occurence(s) » (l. 225-226 ; le groupe : NATURE_DILIGENCE.R_NATUREDILI_ID_NDILGRP -> NATURE_DILIGENCE_GROUPE),
///     validee ;
///   - v2.DEMANDE_EDITION : le courrier, si le reglage CodeParamEdition de la relance 1 designe un parametre d'edition de la base
///     (RelancesRsurv.CourrierAsync ; sans lui, RIG v2 ne cree aucun courrier, l. 365 -- la V3 non plus, et le dit).
/// Traces : famille « dossier », « a cree », la relance 1 du dossier.
///
/// NON REPRIS (nomme) : les lignes DECOCHEES de la recherche (RIG v2 leur cree une diligence close « 8 » sans courrier,
/// l. 160-161 : la V3 ne recoit que les lignes cochees) ; la valeur attendue de la diligence (DLGRC_VALEUR_ATTENDUE, InfosCourrier
/// de la recherche, l. 189-191) ; l'arrivee et la ligne de rapprochement de Demande.CreateDemande (Demande.cs:479-517) ; la liste
/// Aspose des dossiers relances (EditionListAspose « 1 », CodeParamEditionListAspose LRNRBE_1_LIST_IMP) ; l'adresse et le
/// premier dirigeant du destinataire (l. 383-399 : rec.Adresse non lue ici) ; le document Word du courrier, que la file
/// d'edition genere.
/// </summary>
internal sealed class EcrivainRelancesBeneficiaires : IEcrivainEcran
{
    public string Procedure => "PROC_DBE_RELANCE";
    public string Ecran => "Relances bénéficiaires";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    private const string Nature = "SDBE";

    // Le dossier coche, s'il est encore a relancer (la regle du lecteur).
    private const string Dossier = $"""
        SELECT s.Id, s.NumeroGestion, s.Siren, e.Civilite, e.Denomination,
               CASE WHEN {DossierARelancerSql} THEN 1 ELSE 0 END AS ARelancer
        FROM rec.DossierRcs AS s
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        WHERE s.Id = @id
        """;

    // La diligence d'une ligne qui n'est pas un dossier a relancer : son dossier, pour le message du refus.
    private const string Diligence = """
        SELECT s.NumeroGestion, s.Siren, e.Denomination
        FROM v2.DILIGENCE_RCS AS g
        JOIN rec.ActionRegistre AS a ON a.Id = g.DLGRC_ID_ACTRC
        JOIN rec.DossierRcs AS s ON s.Id = a.IdDossier
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        WHERE g.DLGRC_ID_DLGRC = @id AND g.DLGRC_NATURE_DILIGENCE = 'SDBE'
        """;

    /// <summary>Le dossier a relancer : son nom a l'ecran, et la designation de RIG v2 (civilite et denomination,
    /// DossierRCS.DesignationShortAvecCivilite) pour la demande et le courrier.</summary>
    private sealed record Ligne(int IdDossier, string NumeroGestion, string Nom, string Designation);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser("Aucune relance cochée : rien n'est relancé.");
        // 1. Le volume (DoVerifier).
        if (ids.Length > EcrivainRelancesComptesAnnuels.RelancesMax) return IssueGeste.Refuser(EcrivainRelancesComptesAnnuels.MessageVolume);
        var jour = geste.Maintenant.Date;

        // 2. Chaque ligne : un dossier encore a relancer.
        var lignes = new List<Ligne>();
        foreach (var id in ids)
        {
            if (!id.StartsWith(PrefixeDossier, StringComparison.Ordinal))
            {
                // Une diligence : deja relancee (ou en cours) -- RIG v2 n'a qu'une relance SDBE.
                if (int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var idDiligence))
                    await using (var r = await geste.LireAsync(Diligence, ct, new SqlParameter("@id", idDiligence)))
                        if (await r.ReadAsync(ct))
                            return IssueGeste.Refuser($"{FormeRelance.NomDossier(r)} : aucune relance n'est due (RIG v2 ne relance que les dossiers sans relance des bénéficiaires en cours).");
                return IssueGeste.Refuser($"Relance introuvable dans la base du greffe ({id}).");
            }
            if (!int.TryParse(id[PrefixeDossier.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var idDossier))
                return IssueGeste.Refuser($"Relance introuvable dans la base du greffe ({id}).");
            Ligne? l = null;
            var aRelancer = false;
            await using (var r = await geste.LireAsync(Dossier, ct, new SqlParameter("@id", idDossier), Jour(jour)))
                if (await r.ReadAsync(ct))
                {
                    l = new Ligne(idDossier, FormeLigne.Texte(r, "NumeroGestion"), FormeRelance.NomDossier(r),
                                  string.Join(' ', new[] { FormeLigne.Texte(r, "Civilite"), FormeLigne.Texte(r, "Denomination") }.Where(x => x.Length > 0)));
                    aRelancer = r["ARelancer"] is int a && a == 1;
                }
            if (l is null) return IssueGeste.Refuser($"Relance introuvable dans la base du greffe ({id}).");
            if (!aRelancer)
                return IssueGeste.Refuser($"{l.Nom} : aucune relance n'est due (le dossier n'est plus dans la recherche des bénéficiaires à relancer).");
            lignes.Add(l);
        }

        // La demande du lot (PROC_RSURV.cs:225-226), validee avec ses filles (l. 245-247).
        var lot = await RelancesRsurv.DemandeAsync(geste, null, $"Relances du groupe {await GroupeAsync(geste, ct)} - {lignes.Count} occurence(s)",
                                                   null, null, null, null, ct);
        if (lot.Refus is not null) return IssueGeste.Refuser(lot.Refus);

        var reglages = await RelancesRsurv.ReglagesAsync(geste, Nature, 1, ct);
        var limite = RelancesRsurv.Limite(reglages.Delai, jour);
        var envoiEdi = await EnvoiEdiAsync(geste, ct);
        var traces = new List<TraceGeste>();
        var courriers = new List<string>();
        var diligences = new List<string>();
        foreach (var l in lignes)
        {
            var (arrivee, refusArrivee) = await CreationV2.NumeroAsync(geste, "RA1", jour.Year, ct);
            if (arrivee is null) return IssueGeste.Refuser(refusArrivee!);
            var action = await geste.InsererAsync("rec.ActionRegistre", "Id", new Dictionary<string, object?>
            {
                ["IdDossier"] = l.IdDossier, ["NumeroArrivee"] = arrivee, ["DateArrivee"] = jour, ["CodeProcessus"] = Nature,
                ["EtatInfo"] = "1", ["EtatAction"] = "2", ["SurNotification"] = false,
            }, ct);
            var diligence = new Dictionary<string, object?>
            {
                ["DLGRC_ID_ACTRC"] = action, ["DLGRC_ORIGINE_INFO"] = "G", ["DLGRC_NATURE_DILIGENCE"] = Nature, ["DLGRC_MOTIF_DILIGENCE"] = "NP",
                ["DLGRC_ETAT_DILIGENCE"] = "1", ["DLGRC_ETAT_RELANCE"] = "2",
            };
            if (envoiEdi) diligence["DLGRC_ETAT_EDI"] = "1";
            var idDiligence = await geste.InsererAsync("v2.DILIGENCE_RCS", "DLGRC_ID_DLGRC", diligence, ct);
            await geste.InsererAsync("v2.RELANCE_RCS", "RLCRC_ID_RLCRC", new Dictionary<string, object?>
            {
                ["RLCRC_ID_DLGRC"] = idDiligence, ["RLCRC_NUM_ORDRE"] = (short)1, ["RLCRC_AUTEUR"] = "3", ["RLCRC_INTERVENANT"] = "1",
                ["RLCRC_DATE_RELANCE"] = jour, ["RLCRC_TYPE_RELANCE"] = reglages.Type, ["RLCRC_DATE_LIMITE"] = limite, ["RLCRC_FACTURE"] = false,
            }, ct);

            var parametres = $"NumRelance=1;NatureDiligence={Nature};IdDiligenceRcs={idDiligence}";
            var fille = await RelancesRsurv.DemandeAsync(geste, l.IdDossier, $"Relance 1 {Nature} : {l.Designation}", l.NumeroGestion, lot.Id,
                                                         parametres, action, ct);
            if (fille.Refus is not null) return IssueGeste.Refuser(fille.Refus);
            var courrier = await RelancesRsurv.CourrierAsync(geste, fille.Id!.Value, l.IdDossier, l.Designation, Nature, reglages, ct);
            if (courrier is not null) courriers.Add(courrier);
            diligences.Add(idDiligence.ToString(CultureInfo.InvariantCulture));
            traces.Add(new TraceGeste("dossier", $"le dossier {l.NumeroGestion}", l.IdDossier.ToString(CultureInfo.InvariantCulture), "a cree", null,
                new Dictionary<string, string>
                {
                    ["Relance"] = $"relance 1 des bénéficiaires du {FormeLigne.Date(jour)}{(limite is { } d ? $", limite {FormeLigne.Date(d)}" : "")}",
                    ["Diligence"] = idDiligence.ToString(CultureInfo.InvariantCulture), ["Arrivee"] = arrivee, ["Demande"] = fille.Numero!,
                    ["Courrier"] = courrier ?? "aucun : pas de paramètre d'édition",
                }));
        }

        return IssueGeste.Ecrit(EcrivainRelancesComptesAnnuels.Message(lignes.Select(_ => 1), courriers), [.. diligences], [.. traces]);
    }

    private static SqlParameter Jour(DateTime jour) => new("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = jour };

    /// <summary>Le libelle du groupe de la nature SDBE (NATURE_DILIGENCE.R_NATUREDILI_ID_NDILGRP -> NATURE_DILIGENCE_GROUPE :
    /// « Surveillances Président » dans RIG_DEV) ; la nature elle-meme si la base ne le porte pas.</summary>
    private static async Task<string> GroupeAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (await RelancesRsurv.Scalaire(geste, "SELECT OBJECT_ID(N'v2.NATURE_DILIGENCE_GROUPE')", ct) is DBNull or null
            || await RelancesRsurv.Scalaire(geste, "SELECT COL_LENGTH('v2.NATURE_DILIGENCE', 'R_NATUREDILI_ID_NDILGRP')", ct) is DBNull or null)
            return Nature;
        return await RelancesRsurv.Scalaire(geste, """
            SELECT gr.NDILGRP_LIBELLE FROM v2.NATURE_DILIGENCE AS n
            JOIN v2.NATURE_DILIGENCE_GROUPE AS gr ON gr.NDILGRP_ID_NDILGRP = n.R_NATUREDILI_ID_NDILGRP
            WHERE n.R_NATUREDILI_VALEUR = @nature
            """, ct, new SqlParameter("@nature", Nature)) is string libelle && libelle.Trim().Length > 0 ? libelle.Trim() : Nature;
    }

    /// <summary>Le reglage EnvoiEdi de la nature (NATURE_DILIGENCE_PARAM, processus RSURV, sans numero) vaut « 1 » (l. 193).</summary>
    private static async Task<bool> EnvoiEdiAsync(GesteEnCours geste, CancellationToken ct)
        => await RelancesRsurv.Scalaire(geste, "SELECT OBJECT_ID(N'v2.NATURE_DILIGENCE_PARAM')", ct) is not DBNull and not null
           && await RelancesRsurv.Scalaire(geste, """
               SELECT NADIP_VALEUR_PARAM FROM v2.NATURE_DILIGENCE_PARAM
               WHERE NADIP_NATURE_DILIGENCE = @nature AND NADIP_CODE_PROCESSUS = 'RSURV' AND NADIP_NUM_ORDRE IS NULL AND NADIP_CODE_PARAM = 'EnvoiEdi'
               """, ct, new SqlParameter("@nature", Nature)) is string v && v.Trim() == "1";
}
