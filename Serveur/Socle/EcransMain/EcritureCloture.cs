using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Clôture » (PROC_CLOT, écran fait main web/src/ecrans/EcranCloture.tsx) : le geste COURRIER AUX DÉBITEURS de RIG v2, le mode de
/// clôture eModeCloture.COURRIER_DEBITEURS (5), sur les procédures collectives cochées de la grille « Sélection PC ». Joué par la route
/// POST RoutesCloture.CourrierDebiteurs (ModuleCloture.cs) dans la transaction et le journal des gestes du socle (GesteFaitMain).
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
/// les autres modes de clôture (enrôlement sur saisine d'office, liste préparatoire), que l'écran V3 ne propose pas.
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
