using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurSuppressionInstance;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Supprimer une instance » (PROC_XXAFF) : VALIDER supprime l'instance cochee de la base V3 du greffe, comme RIG v2. Mode
/// d'emploi : EcrivainEcran.cs ; la liste et l'apercu : LecteurSuppressionInstance.cs. Plan « RIG v3 100 % fonctionnel », lot 4,
/// 26/11/2026.
///
/// SOURCE (ancien RIG) : la suppression est PHYSIQUE. L'etape resume de l'instance, supprimee (MetierDeleteAuto,
/// PROC_XXAFF.cs:61), passe ETP_INSTANCE_SUMMARY.CanDelete (Instance.CanDeleteInstance, Instance.cs:4136-4229), puis a la
/// validation OPE_INSTANCE_SUMMARY.DoSauve appelle instance.DeleteCascade(cnx, "XXAFF", demande) (OPE_INSTANCE_SUMMARY.cs:113) :
/// Instance.cs:4237-4670, en UNE transaction (« SuppInstance »). La V3 rejoue ces ecritures dans la base V3 seulement (Cascade,
/// dans l'ordre de RIG v2) ; une table absente de la base V3 n'a rien a effacer et se saute (nommee dans la fiche, aucune ligne).
///
/// CONTROLES DE RIG v2, avant toute ecriture : une instance (celle de la demande) ; dans la recherche de XXAFF ; pas tenue par une
/// autre demande (VerrouDemandeV2, la selection OPE_SELECT_ENTITE_JUDICIAIRE.cs:122) ; CanDeleteInstance, toutes ses raisons, au
/// message de RIG v2 (« La suppression est impossible pour la ou les raison(s) suivante(s) : », ETP_INSTANCE_SUMMARY.cs:46).
///
/// LES NUMEROS : RIG v2 les restitue si la case « Libérer le numéro d'instance » est cochee (Numerotation.Restituer, procedure
/// RESTITUER_NUMERO, OPE_INSTANCE_LIBERER_NUM_METIER.cs:101-113). Dans la V3, les numeros sont tenus par num.Restituer
/// (Base\migrations\NUM-001_compteurs.sql), hors des tables v2.* et rec.* qu'un ecran genere ecrit (EcrivainEcran.cs, point 3) :
/// la V3 ne les rend pas. Case cochee : refus, rien n'est supprime ; case decochee : RIG v2 supprime sans rendre, apres son
/// avertissement (« Attention vous avez choisi de ne pas libérer les numéros... », :96-98), que le message reprend.
/// NON REPRIS (nomme) : le courriel envoye a chaque suppression (SendMail, :112, :120-156 -- service externe, jamais appele : le
/// message le dit) ; la demande XXAFF de RIG v2, une trace sans facture (MetierFacture = false, PROC_XXAFF.cs:25) que le journal
/// des gestes remplace (la cascade met quand meme a jour les AUTRES demandes de l'instance, comme RIG v2).
/// Trace : famille « affaire », « a supprime », l'instance avant (role, nom, numeros, lignes effacees par table).
/// </summary>
internal sealed class EcrivainSuppressionInstance : IEcrivainEcran
{
    public string Procedure => "PROC_XXAFF";
    public string Ecran => "Supprimer une instance";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Confirmer];

    /// <summary>
    /// Une ecriture de DeleteCascade : la table V2, la table V3, les tables que sa condition lit, et l'ecriture (DELETE ou UPDATE)
    /// sur les lignes de <see cref="Ou"/> (parametres @id = l'instance, @dossier = son dossier judiciaire, @role, @texte).
    /// </summary>
    internal sealed record Etape(string TableV2, string TableV3, string Ou, string[] Lues, string? Mise = null, bool PremiereSeule = false, string? Cle = null);

    /// <summary>Les ecritures de Instance.DeleteCascade, dans son ordre (Instance.cs, ligne citee pour chacune).</summary>
    internal static readonly Etape[] Cascade =
    [
        new("PARTIE", "v2.PARTIE", "PRT_ID_INSTN = @id", []),                                                      // :4250-4260
        new("ETAPE_CALENDRIER", "v2.ETAPE_CALENDRIER", "ETPCA_ID_INSTN = @id", []),                                // :4262-4270
        new("APPEL_AFFAIRE", "v2.APPEL_AFFAIRE", "APPAF_ID_INSTN = @id", []),                                      // :4272-4280
        new("VOIE_RECOURS", "v2.VOIE_RECOURS",                                                                     // :4282-4291
            "VRCRS_ID_DCJDC IN (SELECT DCJDC_ID_DCJDC FROM v2.DECISION_JUDICIAIRE WHERE DCJDC_ID_INSTN = @id)", ["v2.DECISION_JUDICIAIRE"]),
        new("OODRIVE_DOCUMENT_SIGNE", "v2.OODRIVE_DOCUMENT_SIGNE",                                                 // :4297-4306 (lien a NULL)
            "OODDS_ID_DCJDC IN (SELECT DCJDC_ID_DCJDC FROM v2.DECISION_JUDICIAIRE WHERE DCJDC_ID_INSTN = @id)", ["v2.DECISION_JUDICIAIRE"],
            Mise: "OODDS_ID_DCJDC = NULL"),
        new("DECISION_JUDICIAIRE", "v2.DECISION_JUDICIAIRE", "DCJDC_ID_INSTN = @id", []),                          // :4293-4311
        new("NOTE_PROCEDURE", "v2.NOTE_PROCEDURE", "NTPRC_ID_INSTN = @id", []),                                    // :4313-4322
        new("DEPOT_PC", "v2.DEPOT_PC",                                                                             // :4324-4332, DepotPC.cs:104-105
            "DPTPC_ID_EVNPR IN (SELECT EVNPR_ID_EVNPR FROM v2.EVENEMENT_PROCEDURE WHERE EVNPR_ID_INSTN = @id)", ["v2.EVENEMENT_PROCEDURE"]),
        new("EVENEMENT_PROCEDURE", "v2.EVENEMENT_PROCEDURE", "EVNPR_ID_INSTN = @id", []),                          // :4334-4342
        new("CHEF_DECISION", "v2.CHEF_DECISION",                                                                   // :4344-4356
            "CHDCS_ID_CHFDM IN (SELECT CHFDM_ID_CHFDM FROM v2.CHEF_DEMANDE WHERE CHFDM_ID_INSTN = @id)", ["v2.CHEF_DEMANDE"]),
        new("CHEF_DEMANDE", "v2.CHEF_DEMANDE", "CHFDM_ID_INSTN = @id", []),                                        // :4357-4361
        // :4363-4369 -- UNE correspondance, la premiere de ORARI_ID_RIG = l'instance (CorrespOracleRig.cs:69, sans filtre de table).
        new("CORRESP_ORACLE_RIG", "v2.CORRESP_ORACLE_RIG", "ORARI_ID_RIG = @id", [], PremiereSeule: true, Cle: "ORARI_ID_ORARI"),
        new("DESTINATAIRES_EDITIONS_LOT2", "v2.DESTINATAIRES_EDITIONS_LOT2", "DESED_ID_INSTN = @id", [],          // :4371-4377
            PremiereSeule: true, Cle: "DESED_ID_DESED"),
        new("DEMEURE_RCS", "v2.DEMEURE_RCS", "DEMRCS_ID_INSTN = @id", []),                                         // :4379-4387
        // :4391-4394 (Fiche Team 12088) -- l'etat de relance des diligences des injonctions IDCA de l'instance.
        new("DILIGENCE_RCS", "v2.DILIGENCE_RCS",
            "DLGRC_ID_DLGRC IN (SELECT RLCRC_ID_DLGRC FROM v2.RELANCE_RCS WHERE RLCRC_TYPE_RELANCE = 'IDCA' AND RLCRC_ID_INSTN = @id)", ["v2.RELANCE_RCS"],
            Mise: "DLGRC_ETAT_RELANCE = (SELECT COUNT(*) FROM v2.RELANCE_RCS AS rr WHERE rr.RLCRC_ID_DLGRC = v2.DILIGENCE_RCS.DLGRC_ID_DLGRC AND ISNULL(rr.RLCRC_ID_INSTN, '') <> @id) * 2"),
        // :4396-4399 -- l'etat DCA des exercices de ces diligences (concatenation de l'etat de relance, comme RIG v2).
        new("EXERCICE", "v2.EXERCICE",
            "EXRCC_ID_DLGRC IN (SELECT RLCRC_ID_DLGRC FROM v2.RELANCE_RCS WHERE RLCRC_TYPE_RELANCE = 'IDCA' AND RLCRC_ID_INSTN = @id)", ["v2.RELANCE_RCS", "v2.DILIGENCE_RCS"],
            Mise: "EXRCC_ETAT_DCA = (SELECT dl.DLGRC_ETAT_RELANCE + dl.DLGRC_ETAT_RELANCE FROM v2.DILIGENCE_RCS AS dl WHERE dl.DLGRC_ID_DLGRC = v2.EXERCICE.EXRCC_ID_DLGRC)"),
        new("RELANCE_RCS", "v2.RELANCE_RCS", "RLCRC_ID_INSTN = @id AND RLCRC_TYPE_RELANCE = 'IDCA'", []),         // :4402-4409
        // :4411-4417 -- l'action du registre de l'instance (ACTION_RCS, reprise en rec.ActionRegistre, FichesV3.cs) : la premiere.
        new("ACTION_RCS", "rec.ActionRegistre", "IdInstance = @id", [], PremiereSeule: true, Cle: "Id"),
        new("REPONSE_DEFENDEUR", "v2.REPONSE_DEFENDEUR", "RPNDF_ID_INSTN = @id", []),                              // :4430-4438
        // :4440-4448 -- les etapes du calendrier une seconde fois (si l'instance avait des reponses) : deja effacees plus haut.
        new("RIG_MOTS_DIRECTEUR_DATE_AUDIENCE", "v2.RIG_MOTS_DIRECTEUR_DATE_AUDIENCE",                              // :4466-4468
            "MDAUD_ID_MDPCI IN (SELECT MDPCI_ID_MDPCI FROM v2.RIG_MOTS_DIRECTEUR_PCINST WHERE MDPCI_ID_INSTN = @id)", ["v2.RIG_MOTS_DIRECTEUR_PCINST"]),
        new("RIG_MOTS_DIRECTEUR_PCINST", "v2.RIG_MOTS_DIRECTEUR_PCINST", "MDPCI_ID_INSTN = @id", []),              // :4469
        new("SITE_LOCAL_RETOUR_AUDIENCE", "v2.SITE_LOCAL_RETOUR_AUDIENCE", "SLRAU_ID_INSTN = @id", []),            // :4471-4479
        // :4481-4494 -- 🐞 RIG v2 ne detache les dematerialisations Tribunal digital que si l'instance avait des retours d'audience
        // (« if (sites.Count > 0) ») : reproduit tel quel (Ecrire, garde SitesAvant).
        new("DEMAT_TRIBUNAL_DIGITAL_CREFO", "v2.DEMAT_TRIBUNAL_DIGITAL_CREFO", "DTDCREFO_ID_INSTN = @id", [],
            Mise: "DTDCREFO_ID_INSTN = NULL, DTDCREFO_MOTIF_REJET = @motifCrefo"),
        new("DEMAT_RPJC_ENR", "v2.DEMAT_RPJC_ENR", "RPJCE_ID_INSTN = @id", []),                                    // :4496-4504
        new("VIDIGED_DJ", "v2.VIDIGED_DJ", "VGDDJ_ID_INSTN = @id", []),                                            // :4506-4514
        new("WEB_DEMANDE", "v2.WEB_DEMANDE", "WDMND_ID_INSTN = @id", [], PremiereSeule: true, Cle: "WDMND_ID_WDMND"), // :4516-4522
        new("MESURE", "v2.MESURE", "MSINS_ID_INSTN = @id", []),                                                    // :4524-4532
        new("INSTANCE_JUGE", "v2.INSTANCE_JUGE", "JUGIN_ID_INSTN = @id", []),                                      // :4534-4542
        new("INSTANCE", "v2.INSTANCE", "INSTN_ID_INSTN = @id", []),                                                // :4569
        // :4573-4579 -- le dossier judiciaire, s'il n'est plus reference par aucune instance.
        new("DOSSIER_JUDICIAIRE", "v2.DOSSIER_JUDICIAIRE",
            "DSJUD_ID_DSJUD = @dossier AND NOT EXISTS (SELECT 1 FROM v2.INSTANCE AS o WHERE o.INSTN_ID_DSJUD = @dossier)", ["v2.INSTANCE"]),
        // :4590-4601 -- les demandes de l'instance (sequenceur 2J) : son role retire des numeros, le nom prefixe.
        new("DEMANDE", "v2.DEMANDE",
            "DMND_ID_DMND IN (SELECT DSSDM_ID_DMND FROM v2.DOSSIER_DEMANDE WHERE DSSDM_VALEUR_SEQUENCEUR = @texte AND DSSDM_CODE_SEQUENCEUR = '2J')", ["v2.DOSSIER_DEMANDE"],
            Mise: "DMND_LES_NUMEROS = CASE WHEN LTRIM(RTRIM(REPLACE(DMND_LES_NUMEROS, @role, ''))) = '' THEN NULL ELSE LTRIM(RTRIM(REPLACE(DMND_LES_NUMEROS, @role, ''))) END, " +
                  "DMND_NOM = SUBSTRING('Instance  supprimée : ' + DMND_NOM, 1, 255)"),
        // :4603-4619 -- celles sans facture, et sans PC ni prevention (2P / 2R), passent « Supprimée » (X), plus en cours.
        new("DEMANDE", "v2.DEMANDE",
            "DMND_ID_DMND IN (SELECT DSSDM_ID_DMND FROM v2.DOSSIER_DEMANDE WHERE DSSDM_VALEUR_SEQUENCEUR = @texte AND DSSDM_CODE_SEQUENCEUR = '2J') " +
            "AND DMND_NUM_DEMANDE NOT IN (SELECT f.FACTU_NUM_DEMANDE FROM v2.FACTURE AS f WHERE f.FACTU_NUM_DEMANDE = v2.DEMANDE.DMND_NUM_DEMANDE) " +
            "AND DMND_ID_DMND NOT IN (SELECT dd.DSSDM_ID_DMND FROM v2.DOSSIER_DEMANDE AS dd WHERE dd.DSSDM_CODE_SEQUENCEUR IN ('2P', '2R'))",
            ["v2.DOSSIER_DEMANDE", "v2.FACTURE"], Mise: "DMND_ETAT_DEMANDE = 'X', DMND_EN_COURS = 0"),
    ];

    /// <summary>Le motif que RIG v2 ecrit sur une dematerialisation Tribunal digital detachee (Instance.cs:4489-4490), au mot pres.</summary>
    internal static string MotifCrefo(int id)
        => "On a supprimé l'instance " + id.ToString(CultureInfo.InvariantCulture) + ", veuillez regarder dans la table INSTANCE_SUPPRIMEE les actions qui ont été exécutés";

    /// <summary>Ce qu'une etape touche : sa table V2, la table V3, le nombre de lignes.</summary>
    internal sealed record Effacees(string Table, string TableV3, int Lignes);

    /// <summary>Les tables (v2.* et rec.*) presentes dans la base du greffe.</summary>
    private static async Task<HashSet<string>> TablesPresentesAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
    {
        const string sql = "SELECT s.name + '.' + t.name AS Nom FROM sys.tables AS t JOIN sys.schemas AS s ON s.schema_id = t.schema_id WHERE s.name IN ('v2', 'rec')";
        LectureV2.ExigerLecture(sql);
        await using var cmd = new SqlCommand(sql, cn, tx);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var noms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await r.ReadAsync(ct)) noms.Add(FormeLigne.Texte(r, "Nom"));
        return noms;
    }

    private static string Condition(Etape e) => e.PremiereSeule
        ? $"{e.Cle} = (SELECT TOP (1) {e.Cle} FROM {e.TableV3} WHERE {e.Ou} ORDER BY {e.Cle})"
        : e.Ou;

    private static SqlParameter[] Parametres(int id, int? dossier, string role) =>
    [
        new("@id", id), new("@dossier", (object?)dossier ?? DBNull.Value), new("@texte", id.ToString(CultureInfo.InvariantCulture)),
        new("@role", role), new("@motifCrefo", MotifCrefo(id)),
    ];

    /// <summary>
    /// L'apercu (la fiche) : les lignes que chaque etape toucherait, en lecture seule. Une table absente de la base compte 0. Les
    /// dematerialisations Tribunal digital ne comptent que si l'instance a des retours d'audience (la garde de RIG v2).
    /// </summary>
    internal static async Task<List<Effacees>> CompterCascadeAsync(SqlConnection cn, SqlTransaction? tx, int id, CancellationToken ct)
    {
        var presentes = await TablesPresentesAsync(cn, tx, ct);
        int? dossier = null;
        var role = "";
        if (presentes.Contains("v2.INSTANCE"))
        {
            await using var cmd = new SqlCommand("SELECT INSTN_ID_DSJUD, INSTN_NUM_ROLE FROM v2.INSTANCE WHERE INSTN_ID_INSTN = @id", cn, tx);
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct)) { dossier = r[0] is int d ? d : null; role = FormeLigne.Texte(r, "INSTN_NUM_ROLE"); }
        }
        var comptes = new List<Effacees>();
        var sites = 0;
        foreach (var e in Cascade)
        {
            var n = 0;
            if (presentes.Contains(e.TableV3) && e.Lues.All(presentes.Contains) && !(e.TableV2 == "DOSSIER_JUDICIAIRE" && dossier is null))
            {
                // Avant la suppression de l'instance, son dossier est encore reference par elle : on compte les AUTRES instances.
                var ou = e.TableV2 == "DOSSIER_JUDICIAIRE"
                    ? "DSJUD_ID_DSJUD = @dossier AND NOT EXISTS (SELECT 1 FROM v2.INSTANCE AS o WHERE o.INSTN_ID_DSJUD = @dossier AND o.INSTN_ID_INSTN <> @id)"
                    : Condition(e);
                var sql = $"SELECT COUNT(*) FROM {e.TableV3} WHERE {ou}";
                LectureV2.ExigerLecture(sql);
                await using var cmd = new SqlCommand(sql, cn, tx);
                cmd.Parameters.AddRange(Parametres(id, dossier, role));
                n = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            }
            if (e.TableV2 == "SITE_LOCAL_RETOUR_AUDIENCE") sites = n;
            if (e.TableV2 == "DEMAT_TRIBUNAL_DIGITAL_CREFO" && sites == 0) n = 0;
            comptes.Add(new Effacees(e.TableV2 + (e.Mise is null ? "" : " (mise à jour)"), e.TableV3, n));
        }
        return comptes;
    }

    private static readonly string Lecture = $"""
        SELECT {Diagnostic}
        FROM v2.INSTANCE AS i WITH (UPDLOCK, HOLDLOCK)
        {Jointures}
        WHERE i.INSTN_ID_INSTN = @id AND ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {Types}
        """;

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // Une instance : celle de la demande de RIG v2.
        if (geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray() is not [var id]
            || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            return IssueGeste.Refuser("Une suppression vise UNE instance (celle de la demande de RIG v2) : cocher une seule instance.");

        string role, nom;
        int? dossier;
        List<string> raisons;
        List<(string Type, string Numero)> numeros;
        int annee;
        await using (var r = await geste.LireAsync(Lecture, ct, new SqlParameter("@id", n)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Instance introuvable dans la base du greffe ({id}).");
            role = FormeLigne.Texte(r, "Role");
            nom = FormeLigne.Texte(r, "NomCourant");
            dossier = r["IdDossier"] is int d ? d : null;
            raisons = Raisons(r);
            (numeros, annee) = NumerosALiberer(r);
        }
        if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance, id, ct) is { } verrou)
            return IssueGeste.Refuser(verrou);
        // Le message de RIG v2 met une raison par ligne (ETP_INSTANCE_SUMMARY.cs:46-47) ; l'annonce de l'ecran est d'une ligne :
        // les raisons s'y suivent, chacune ouverte par son tiret.
        if (raisons.Count > 0)
            return IssueGeste.Refuser("La suppression est impossible pour la ou les raison(s) suivante(s) : " + string.Join(" ", raisons.Select(x => x.Trim())));
        if (geste.Valeur(SectionNumeros, ChampLiberer) == "oui")
            return IssueGeste.Refuser($"La V3 ne rend pas les numéros de l'instance {role} (RIG v2 les restitue par RESTITUER_NUMERO ; la V3 les tient " +
                                      $"hors des tables qu'un écran écrit) : décocher « {ChampLiberer} » pour la supprimer sans les rendre, comme RIG v2 le permet après son avertissement.");

        // DeleteCascade, dans son ordre.
        var presentes = await TablesPresentesAsync(geste.Base, geste.Transaction, ct);
        var effacees = new List<Effacees>();
        var sites = 0;
        foreach (var e in Cascade)
        {
            if (!presentes.Contains(e.TableV3) || !e.Lues.All(presentes.Contains) || (e.TableV2 == "DOSSIER_JUDICIAIRE" && dossier is null)) continue;
            if (e.TableV2 == "DEMAT_TRIBUNAL_DIGITAL_CREFO" && sites == 0) continue;   // la garde de RIG v2 (Instance.cs:4486)
            var sql = e.Mise is null
                ? $"DELETE FROM {e.TableV3} WHERE {Condition(e)}"
                : $"UPDATE {e.TableV3} SET {e.Mise} WHERE {Condition(e)}";
            var lignes = await geste.EcrireAsync(sql, ct, Parametres(n, dossier, role));
            if (e.TableV2 == "SITE_LOCAL_RETOUR_AUDIENCE") sites = lignes;
            if (lignes > 0) effacees.Add(new Effacees(e.TableV2 + (e.Mise is null ? "" : " (mise à jour)"), e.TableV3, lignes));
        }
        if (!effacees.Any(e => e.Table == "INSTANCE"))
            throw new InvalidOperationException($"Instance {n} : la ligne de v2.INSTANCE n'a pas ete supprimee.");

        var liees = effacees.Where(e => e.Table != "INSTANCE").Sum(e => e.Lignes);
        var rendus = string.Join(", ", numeros.Where(x => x.Numero.Length > 0).Select(x => $"{x.Type} {x.Numero}"));
        return IssueGeste.Ecrit(
            $"Instance {role} supprimée de la base du greffe ({liees} ligne{(liees > 1 ? "s" : "")} liée{(liees > 1 ? "s" : "")} effacée{(liees > 1 ? "s" : "")} ou mise{(liees > 1 ? "s" : "")} à jour). " +
            $"Numéros non rendus ({rendus}, année {annee}) : « Attention vous avez choisi de ne pas libérer les numéros. » " +
            "Le courriel de suppression de RIG v2 n'est pas envoyé par la V3.",
            [id],
            new TraceGeste("affaire", $"l'instance {role}", id, "a supprime",
                new
                {
                    Role = role, Nom = nom, Numeros = numeros.Select(x => $"{x.Type} {x.Numero}").ToArray(), Annee = annee,
                    Lignes = effacees.Select(e => $"{e.Table} : {e.Lignes}").ToArray(),
                }, null));
    }
}
