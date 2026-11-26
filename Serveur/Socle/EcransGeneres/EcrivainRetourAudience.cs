using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// Les gestes du « Retour d'audience » (PROC_RETAUD) sur les affaires cochees au role : LecteurRetourAudience.cs. Mode d'emploi :
/// EcrivainEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 7, 26/11/2026.
///
/// « Marquer les décisions comme rendues » -- le menu de la grille, MarquerDecisionsRenduesContextMenu_Click
/// (OPE_LIST_APPEL_AFFAIRE.cs:2217-2279) :
///   - seulement si TOUTES les affaires cochees sont non traitees et prononcees (:2222 ; le menu est grise sinon, :1948) ;
///   - les codes plumitif « J » (jugement rendu) et « O » (ordonnance rendue) doivent exister (:2224-2226) ;
///   - par affaire : J s'il est compatible avec le type de l'instance, sinon O, sinon rien (CODE_PLUMITIF.EstCompatibleInstance,
///     CODE_PLUMITIF.cs:76-97, type traduit Instance.TypeInstanceTraduite, Instance.cs:967-983) ; APPAF_CODE_PLUMITIF = ce code,
///     APPAF_ETAT_RETOUR = « P » si le code est PRONONCE, « M » sinon (:2240-2248) ;
///   - si le code porte un evenement (R_CODEPLUMIT_CODE_EVE_PRO), un evenement de procedure SURVENU sur l'instance, date de
///     l'audience (:2253-2270 ; EvenementProcedure.CreateEvenementProcedure, EvenementProcedure.cs:1721-1757 : type du code, numero
///     source GUID, piece numerisee si le code la demande pour une instance) ; son rang = le nombre d'evenements de l'instance
///     (Instance.AddEvenementProcedure, Instance.cs:594-596).
///
/// « Renvoyer » -- le resultat d'un renvoi saisi en masse sur les affaires cochees (PROC_MJUDS, le meme resultat pour toutes ;
/// OPE_RESULTAT.cs:448-700) : « Résultat » = un code plumitif de RENVOI (R_CODEPLUMIT_RENVOI = 1, :478), utilisable en saisie et
/// compatible avec chaque instance (PROC_MJUDS.cs:600-625) ; APPAF_ETAT_RETOUR comme ci-dessus (:609-616) ; puis le PROCHAIN APPEL
/// (:655-700) : si le code dit « avec audience » (R_CODEPLUMIT_AVEC_AUDIENCE = 1) ou « avec cabinet » (0), l'audience de la
/// « Prochaine audience » (date), de la chambre du code (« = » : celle de l'audience en cours), recoit un appel d'affaire neuf :
/// type de classement du code, etat de retour « A », numero d'ordre = nombre d'appels de l'instance + 1, composition de
/// l'audience si elle n'en a qu'une (AppelAffaire.CreateAppelAffaire, AppelAffaire.cs:675-702). Puis la DECISION JUDICIAIRE
/// (ApplyModifications multi, PROC_MJUDS.cs:1186-1193) : RIG v2 n'ouvre l'etape decision que pour un code prononce, delibere ou
/// delibere repousse (displayDJ, OPE_RESULTAT.cs:472-473) -- un renvoi simple la masque et la vide (:488-497) et aucune decision ne
/// s'ecrit (GetDecisionJudiciaireSaisie, PROC_MJUDS.cs:1322-1330 : etape visible ET type saisi). Pour un code qui l'ouvre, un
/// « Type de décision » de la liste de saisie (la meme operation que la mise au point des chambres, OPE_DECISION_JUDICIAIRE_MJUDS)
/// cree, sur chaque instance dont l'appel n'a pas deja une decision deliberee (:1189), une DECISION_JUDICIAIRE neuve avec sa
/// « Date prévue » : AffectDecisionJudiciaireDirect (PROC_MJUDS.cs:1587-1593), ecrite par EcrivainMiseAuPointChambres.CreerDecisionAsync.
/// Mesure du 26/11/2026 (SQL-DEV\DEV, RIG_DEV) : aucun des 250 codes de renvoi n'y est prononce ni delibere, l'etape n'y est jamais ouverte.
///
/// CONTROLE COMMUN (CanDoActions, OPE_LIST_APPEL_AFFAIRE.cs:1356-1369) : sur une audience passee (IsAudiencePassee, :1343-1349),
/// « actions possibles uniquement si l'audience comporte des compositions » (:1586), quand sa chambre l'impose
/// (R_CHAMBRE_AVEC_COMPO).
///
/// CE QUE LA V3 NE REPREND PAS (nomme, rien en silence, avec la raison) :
///   - la demande V2 de PROC_MJUDS (creee et validee d'un coup, sans facture pour un renvoi) : le journal des gestes la remplace ;
///   - au RENVOI, l'evenement FUTUR lie a l'evenement du code (creationEvenementFutur, EvenementProcedure.cs:1769-1806 : code
///     R_CODEEVEPRO_CODE_EVE_PRO_FUTUR, dates en mois, jours ou semaines, Outils.cs:520-) que PROC_MJUDS enregistre
///     (AffectEvenementProcedure, PROC_MJUDS.cs:1210) : aucun ecrivain V3 ne porte encore ce calcul. Pour « Marquer », RIG v2 ne
///     l'enregistre pas : seuls l'appel et l'evenement survenu sont sauves (OPE_LIST_APPEL_AFFAIRE.cs:2254, :2263-2268), et
///     EvenementProcedure.Sauve (:2218-) ne sauve pas les evenements lies. Le texte de l'evenement (ConstructionTexte) non plus ;
///   - la mesure associee a la decision (GetMesureSaisie, PROC_MJUDS.cs:1365-1373) et le calendrier de procedure : leurs etapes ne
///     sont pas portees (comme a la mise au point des chambres) ;
///   - les EDITIONS (GenererLesDemandesEdition, PROC_MJUDS.cs:1257-1284) : celle de la decision (DecisionJudiciaire.CreateDemandeEdition)
///     et celles des evenements (EvenementProcedure.CreateDemandeEdition, EvenementProcedure.cs:2607, puis GetAllEditions, :2801 :
///     maquettes CEP_EDITION du code et destinataires de l'instance). La V3 sait ecrire une demande d'edition a parametre connu
///     (EcrivainRelancesComptesAnnuels), pas calculer les editions d'un evenement ou d'une decision ;
///   - la reponse RPVA aux demandes de renvoi (OPE_RESULTAT.cs:627-650) : service externe ; le controle des deliberes repousses et
///     son courriel a la hotline (:571-603) : courriel externe.
/// </summary>
internal sealed class EcrivainRetourAudience : IEcrivainEcran
{
    public string Procedure => "PROC_RETAUD";

    public string Ecran => "Retour d'audience";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Decider];

    /// <summary>Les gestes du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string MarquerRendues = "Marquer les décisions comme rendues", Renvoyer = "Renvoyer";

    /// <summary>Les parametres du descripteur que « Renvoyer » lit.</summary>
    internal const string ParametreResultat = "Résultat", ParametreProchaine = "Prochaine audience", ParametreType = "Type de décision",
                          ParametreDatePrevue = "Date prévue";

    /// <summary>Une affaire au role, telle que les gestes la lisent.</summary>
    private sealed record Appel(int Id, int IdInstance, string Role, string TypeInstance, string? CodePlumitif, string EtatRetour, bool Prononce,
                                int IdAudience, DateTime? DateAudience, string? Heure, string? Chambre, bool AvecCompo, int Compositions);

    /// <summary>Un code plumitif et ce que les gestes en lisent.</summary>
    /// <param name="OuvreDecision">Prononce, delibere ou delibere repousse : RIG v2 ouvre l'etape decision (OPE_RESULTAT.cs:472-473).</param>
    internal sealed record CodePlumitif(string Code, bool Prononce, bool Renvoi, bool OuvreDecision, string? CodeEvePro, string? AvecAudience,
                                       string? TypeClassement, string? Chambre, HashSet<string> Compatibles);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var d = geste.Demande;
        if (d.Ids.Length == 0) return IssueGeste.Refuser("Cochez au moins une affaire du rôle.");
        var appels = new List<Appel>();
        foreach (var id in d.Ids)
        {
            if (await LireAppelAsync(geste, id, ct) is not { } a) return IssueGeste.Refuser($"L'affaire « {id} » n'est pas au rôle d'une audience de la base du greffe.");
            appels.Add(a);
        }
        // CanDoActions (OPE_LIST_APPEL_AFFAIRE.cs:1356-1369, :1586).
        foreach (var a in appels.Where(a => Passee(a, geste.Maintenant) && a.AvecCompo && a.Compositions == 0).Take(1))
            return IssueGeste.Refuser($"Audience passée sans composition (rôle {a.Role}) : actions possibles uniquement si l'audience comporte des compositions.");
        return d.Nom switch
        {
            MarquerRendues => await MarquerAsync(geste, appels, ct),
            Renvoyer => await RenvoyerAsync(geste, appels, ct),
            _ => IssueGeste.Refuser($"Le geste « {d.Nom} » n'existe pas au retour d'audience de RIG v2."),
        };
    }

    // ------------------------------------------------------------------ MARQUER LES DECISIONS COMME RENDUES

    private static async Task<IssueGeste> MarquerAsync(GesteEnCours geste, List<Appel> appels, CancellationToken ct)
    {
        if (appels.FirstOrDefault(a => a.CodePlumitif is not null || !a.Prononce) is { } horsMenu)
            return IssueGeste.Refuser($"« {MarquerRendues} » ne s'applique qu'aux prononcés non encore traités : le rôle {horsMenu.Role} "
                                      + (horsMenu.CodePlumitif is not null ? $"a déjà le résultat {horsMenu.CodePlumitif}." : "n'est pas un prononcé."));
        var j = await LireCodeAsync(geste, "J", ct);
        var o = await LireCodeAsync(geste, "O", ct);
        if (j is null || o is null) return IssueGeste.Refuser("Les codes plumitif « J » (jugement rendu) et « O » (ordonnance rendue) ne sont pas dans la base du greffe.");
        var traces = new List<TraceGeste>();
        var ids = new List<string>();
        var ignores = new List<string>();
        foreach (var a in appels)
        {
            var code = j.Compatibles.Contains(Traduit(a.TypeInstance)) ? j : o.Compatibles.Contains(Traduit(a.TypeInstance)) ? o : null;
            if (code is null) { ignores.Add(a.Role); continue; }
            traces.Add(await PoserResultatAsync(geste, a, code, ct));
            ids.Add(a.Id.ToString(CultureInfo.InvariantCulture));
        }
        if (traces.Count == 0)
            return IssueGeste.Refuser($"Ni « J » ni « O » n'est compatible avec le type d'instance de : {string.Join(", ", ignores)}.");
        var message = $"{traces.Count} décision{(traces.Count > 1 ? "s" : "")} marquée{(traces.Count > 1 ? "s" : "")} comme rendue{(traces.Count > 1 ? "s" : "")}"
                      + (ignores.Count > 0 ? $" ; sans code compatible, laissée{(ignores.Count > 1 ? "s" : "")} à traiter : {string.Join(", ", ignores)}." : ".");
        return IssueGeste.Ecrit(message, [.. ids], [.. traces]);
    }

    // ------------------------------------------------------------------ RENVOYER

    private static async Task<IssueGeste> RenvoyerAsync(GesteEnCours geste, List<Appel> appels, CancellationToken ct)
    {
        if (appels.FirstOrDefault(a => a.CodePlumitif is not null) is { } traite)
            return IssueGeste.Refuser($"Le rôle {traite.Role} a déjà le résultat {traite.CodePlumitif} : seules les affaires non traitées se renvoient.");
        var saisi = geste.Demande.Valeurs.TryGetValue(ParametreResultat, out var r) ? r.Trim() : "";
        if (saisi.Length == 0) return IssueGeste.Refuser($"« {ParametreResultat} » est obligatoire : le code plumitif du renvoi.");
        if (await LireCodeAsync(geste, saisi, ct, saisieSeulement: true) is not { } code)
            return IssueGeste.Refuser($"Le code plumitif « {saisi} » n'est pas utilisable en saisie dans la base du greffe.");
        if (!code.Renvoi) return IssueGeste.Refuser($"Le code plumitif « {code.Code} » n'est pas un renvoi.");
        if (appels.FirstOrDefault(a => !code.Compatibles.Contains(Traduit(a.TypeInstance))) is { } incompatible)
            return IssueGeste.Refuser($"Le code plumitif « {code.Code} » n'est pas compatible avec le type d'instance du rôle {incompatible.Role}.");

        // La decision judiciaire (voir l'en-tete) : l'etape decision de RIG v2 n'existe que pour un code qui l'ouvre.
        // fix-ok: controle du lot 7 (26/11/2026) -- « Renvoyer » n'ecrivait jamais la decision que PROC_MJUDS ecrit en lot
        // (PROC_MJUDS.cs:1186-1193, :1322-1330) quand le code ouvre l'etape decision (OPE_RESULTAT.cs:472-473) ; une seule cause,
        // portee par plusieurs remplacements (en-tete, parametres, code plumitif, ecriture).
        var saisiType = geste.Demande.Valeurs.TryGetValue(ParametreType, out var t) ? t.Trim() : "";
        var saisiDatePrevue = geste.Demande.Valeurs.TryGetValue(ParametreDatePrevue, out var dp) ? dp.Trim() : "";
        DateTime? datePrevue = null;
        if (saisiType.Length > 0)
        {
            if (!code.OuvreDecision)
                return IssueGeste.Refuser($"Le code plumitif « {code.Code} » n'ouvre pas la décision judiciaire : RIG v2 ne la saisit au renvoi "
                                          + "que pour un code prononcé ou délibéré.");
            if (!await EcrivainMiseAuPointChambres.TypeDecisionSaisissableAsync(geste, saisiType, ct))
                return IssueGeste.Refuser($"Le type de décision « {saisiType} » n'est pas utilisable en saisie dans la base du greffe.");
            if (saisiDatePrevue.Length > 0 && (datePrevue = AudiencesV2.Date(saisiDatePrevue)) is null)
                return IssueGeste.Refuser($"« {ParametreDatePrevue} » n'est pas une date : « {saisiDatePrevue} ».");
        }
        else if (saisiDatePrevue.Length > 0)
            return IssueGeste.Refuser($"« {ParametreDatePrevue} » se rapporte à la décision : saisissez aussi le « {ParametreType} ».");

        // Le prochain appel (OPE_RESULTAT.cs:655-700) : seulement pour un code « avec audience » (1) ou « avec cabinet » (0).
        int? idProchaine = null;
        string? libelleProchaine = null;
        if (code.AvecAudience is "0" or "1")
        {
            var jour = AudiencesV2.Date(geste.Demande.Valeurs.TryGetValue(ParametreProchaine, out var p) ? p : null);
            if (jour is null) return IssueGeste.Refuser($"« {ParametreProchaine} » est obligatoire pour le code « {code.Code} » : la date de l'audience de renvoi.");
            if (code.TypeClassement is null) return IssueGeste.Refuser($"Le code plumitif « {code.Code} » ne porte pas de type de classement pour le prochain appel.");
            var chambre = code.Chambre == "=" ? appels[0].Chambre : code.Chambre;
            var trouvees = await AudiencesDuJourAsync(geste, jour.Value, code.AvecAudience == "1", chambre, ct);
            var genre = code.AvecAudience == "1" ? "audience" : "cabinet";
            if (trouvees.Count == 0)
                return IssueGeste.Refuser($"Aucun{(genre == "audience" ? "e" : "")} {genre} le {FormeLigne.Date(jour)}{(chambre is null ? "" : $" pour la chambre {chambre}")} dans la base du greffe.");
            if (trouvees.Count > 1)
                return IssueGeste.Refuser($"Plusieurs {genre}s le {FormeLigne.Date(jour)}{(chambre is null ? "" : $" pour la chambre {chambre}")} : la V3 ne sait pas encore choisir laquelle.");
            (idProchaine, libelleProchaine) = trouvees[0];
        }

        var traces = new List<TraceGeste>();
        var ids = new List<string>();
        var decisions = 0;
        foreach (var a in appels)
        {
            traces.Add(await PoserResultatAsync(geste, a, code, ct));
            ids.Add(a.Id.ToString(CultureInfo.InvariantCulture));
            if (idProchaine is { } adnc) traces.Add(await ProchainAppelAsync(geste, a, adnc, libelleProchaine!, code.TypeClassement!, ct));
            // Pas de seconde decision sur un appel deja delibere (DecisionsJudiciairesDeliberés, PROC_MJUDS.cs:1189).
            if (saisiType.Length > 0 && await DelibereesAsync(geste, a.Id, ct) == 0)
            {
                traces.Add(await EcrivainMiseAuPointChambres.CreerDecisionAsync(geste, a.IdInstance, a.Role, saisiType, datePrevue, ct));
                decisions++;
            }
        }
        var n = appels.Count;
        var message = $"{n} affaire{(n > 1 ? "s" : "")} renvoyée{(n > 1 ? "s" : "")} ({code.Code})" + (libelleProchaine is null ? "" : $" à l'{libelleProchaine}");
        if (saisiType.Length > 0)
            message += decisions == 0 ? " ; aucune décision ajoutée (déjà délibérées)"
                : $" ; {decisions} décision{(decisions > 1 ? "s" : "")} {saisiType} créée{(decisions > 1 ? "s" : "")}"
                  + (decisions < n ? $", {n - decisions} déjà délibérée{(n - decisions > 1 ? "s" : "")}" : "");
        return IssueGeste.Ecrit(message + ".", [.. ids], [.. traces]);
    }

    /// <summary>Les decisions deja deliberees de l'appel (AppelAffaire.DecisionsJudiciairesDeliberés, AppelAffaire.cs:315-322 :
    /// DCJDC_ID_APPAF_DELIBERER = l'appel).</summary>
    private static async Task<int> DelibereesAsync(GesteEnCours geste, int idAppel, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.DECISION_JUDICIAIRE WHERE DCJDC_ID_APPAF_DELIBERER = @a", ct,
                                                  new SqlParameter("@a", idAppel));
        await r.ReadAsync(ct);
        return r.GetInt32(0);
    }

    /// <summary>Les audiences (ou cabinets) du jour, de la chambre donnee s'il y en a une : id et libelle.</summary>
    private static async Task<List<(int Id, string Libelle)>> AudiencesDuJourAsync(GesteEnCours geste, DateTime jour, bool audience, string? chambre,
                                                                                   CancellationToken ct)
    {
        var trouvees = new List<(int, string)>();
        await using var r = await geste.LireAsync($$"""
            SELECT a.AUDNC_ID_ADNC AS Id, {{AudiencesV2.Libelle}} AS Libelle
            FROM v2.AUDIENCE_CABINET AS a {{AudiencesV2.Jointures}}
            WHERE a.AUDNC_DATE >= @jour AND a.AUDNC_DATE < DATEADD(day, 1, @jour) AND a.AUDNC_AUDIENCE = @audience
              AND (@chambre IS NULL OR a.AUDNC_CHAMBRE = @chambre)
            ORDER BY a.AUDNC_HEURE, a.AUDNC_ID_ADNC
            """, ct, new SqlParameter("@jour", jour), new SqlParameter("@audience", audience),
            new SqlParameter("@chambre", (object?)chambre ?? DBNull.Value));
        while (await r.ReadAsync(ct)) trouvees.Add((Convert.ToInt32(r["Id"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "Libelle")));
        return trouvees;
    }

    /// <summary>Ajoute l'appel d'affaire du renvoi (AppelAffaire.CreateAppelAffaire, AppelAffaire.cs:675-702).</summary>
    private static async Task<TraceGeste> ProchainAppelAsync(GesteEnCours geste, Appel a, int idAudience, string libelle, string typeClassement,
                                                             CancellationToken ct)
    {
        int ordre;
        int? composition = null;
        await using (var r = await geste.LireAsync("""
            SELECT (SELECT COUNT(*) FROM v2.APPEL_AFFAIRE WHERE APPAF_ID_INSTN = @instance) AS Appels,
                   (SELECT COUNT(*) FROM v2.COMPOSITION WHERE CMPST_ID_ADNC = @audience) AS Compositions,
                   (SELECT MIN(CMPST_ID_CMAUD) FROM v2.COMPOSITION WHERE CMPST_ID_ADNC = @audience) AS Composition
            """, ct, new SqlParameter("@instance", a.IdInstance), new SqlParameter("@audience", idAudience)))
        {
            await r.ReadAsync(ct);
            ordre = Convert.ToInt32(r["Appels"], CultureInfo.InvariantCulture) + 1;
            if (Convert.ToInt32(r["Compositions"], CultureInfo.InvariantCulture) == 1) composition = Convert.ToInt32(r["Composition"], CultureInfo.InvariantCulture);
        }
        var id = await geste.InsererAsync("v2.APPEL_AFFAIRE", "APPAF_ID_APPAF", new Dictionary<string, object?>
        {
            ["APPAF_ID_INSTN"] = a.IdInstance, ["APPAF_NUM_ORDRE"] = (short)ordre, ["APPAF_ID_ADNC"] = idAudience, ["APPAF_ID_CMAUD"] = composition,
            ["APPAF_TYPE_CLASSEMENT"] = typeClassement, ["APPAF_ETAT_RETOUR"] = "A",
            ["APPAF_ETAT_PREPARATION"] = "0",   // DEFAULT ((0)) de RIG_DEV (APPEL_AFFAIRE.sql), que la copie de dev ne porte pas
        }, ct);
        return new TraceGeste("affaire", $"l'appel du rôle {a.Role} à l'{libelle}", id.ToString(CultureInfo.InvariantCulture), "a cree", null,
                              new { Instance = a.Role, Audience = libelle, TypeClassement = typeClassement, NumOrdre = ordre });
    }

    // ------------------------------------------------------------------ LE RESULTAT D'UN APPEL

    /// <summary>Pose le resultat <paramref name="code"/> sur l'appel (code plumitif, etat de retour) et l'evenement du code.</summary>
    private static async Task<TraceGeste> PoserResultatAsync(GesteEnCours geste, Appel a, CodePlumitif code, CancellationToken ct)
    {
        var etat = code.Prononce ? "P" : "M";
        await geste.EcrireAsync("UPDATE v2.APPEL_AFFAIRE SET APPAF_CODE_PLUMITIF = @code, APPAF_ETAT_RETOUR = @etat WHERE APPAF_ID_APPAF = @id", ct,
            new SqlParameter("@code", code.Code), new SqlParameter("@etat", etat), new SqlParameter("@id", a.Id));
        int? evenement = null;
        if (code.CodeEvePro is { } eve) evenement = await EvenementSurvenuAsync(geste, a.IdInstance, eve, a.DateAudience?.Date, ct);
        return new TraceGeste("affaire", $"l'appel du rôle {a.Role}", a.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
            new { Resultat = a.CodePlumitif, EtatRetour = a.EtatRetour },
            new { Resultat = code.Code, EtatRetour = etat, Evenement = code.CodeEvePro, IdEvenement = evenement });
    }

    /// <summary>Un evenement de procedure SURVENU sur l'instance, a la date donnee : celui du code plumitif, date de l'audience
    /// (OPE_LIST_APPEL_AFFAIRE.cs:2253-2270), ou la citation de PROC_GCONV, date du jour (OPE_CITATIONS.cs:245) --
    /// EvenementProcedure.CreateEvenementProcedure (EvenementProcedure.cs:1721-1757).</summary>
    internal static async Task<int> EvenementSurvenuAsync(GesteEnCours geste, int idInstance, string codeEve, DateTime? date, CancellationToken ct)
    {
        string? type = null, nature = null, deposant = null;
        var piece = false;
        await using (var r = await geste.LireAsync("""
            SELECT R_CODEEVEPRO_TYPE_EVE_PRO, CONVERT(bit, ISNULL(R_CODEEVEPRO_AVEC_PIECE_NUMERISE_INSMES, 0)) AS Piece,
                   R_CODEEVEPRO_NATURE_DOC_GED, R_CODEEVEPRO_TYPE_DEPOSANT_GED
            FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c
            """, ct, new SqlParameter("@c", codeEve)))
            if (await r.ReadAsync(ct))
            {
                type = FormeLigne.Texte(r, "R_CODEEVEPRO_TYPE_EVE_PRO") is { Length: > 0 } t ? t[..1] : null;
                piece = r["Piece"] is true;
                nature = FormeLigne.Texte(r, "R_CODEEVEPRO_NATURE_DOC_GED") is { Length: > 0 } n ? n : null;
                deposant = FormeLigne.Texte(r, "R_CODEEVEPRO_TYPE_DEPOSANT_GED") is { Length: > 0 } dp ? dp : null;
            }
        int rang;
        await using (var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.EVENEMENT_PROCEDURE WHERE EVNPR_ID_INSTN = @i", ct, new SqlParameter("@i", idInstance)))
        {
            await r.ReadAsync(ct);
            rang = r.GetInt32(0) + 1;
        }
        return await CreationV2.InsererAsync(geste, "v2.EVENEMENT_PROCEDURE", "EVNPR_ID_EVNPR", new Dictionary<string, object?>
        {
            ["EVNPR_ID_INSTN"] = idInstance, ["EVNPR_CODE_EVE_PRO"] = codeEve, ["EVNPR_DATE"] = date, ["EVNPR_SURVENU"] = true,
            ["EVNPR_DELAI_EXPIRE"] = false, ["EVNPR_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(), ["EVNPR_TYPE_EVE_PRO"] = type,
            ["EVNPR_NUM_ORDRE"] = (short)rang, ["EVNPR_AVEC_PIECE_NUMERISE"] = piece,
            ["EVNPR_NATURE_DOC_GED"] = piece ? nature : null, ["EVNPR_TYPE_DEPOSANT_GED"] = piece ? deposant : null,
        }, ct);
    }

    // ------------------------------------------------------------------ LECTURES

    private static async Task<Appel?> LireAppelAsync(GesteEnCours geste, string id, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        await using var r = await geste.LireAsync("""
            SELECT aa.APPAF_ID_APPAF, aa.APPAF_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_TYPE_INSTANCE, aa.APPAF_CODE_PLUMITIF, aa.APPAF_ETAT_RETOUR,
                   CONVERT(bit, ISNULL(tc.R_TYPECLASSE_PRONONCE, 0)) AS Prononce, a.AUDNC_ID_ADNC, a.AUDNC_DATE, a.AUDNC_HEURE, a.AUDNC_CHAMBRE,
                   CONVERT(bit, ISNULL(ch.R_CHAMBRE_AVEC_COMPO, 0)) AS AvecCompo,
                   (SELECT COUNT(*) FROM v2.COMPOSITION AS c WHERE c.CMPST_ID_ADNC = a.AUDNC_ID_ADNC) AS Compositions
            FROM v2.APPEL_AFFAIRE AS aa WITH (UPDLOCK, HOLDLOCK)
            JOIN v2.AUDIENCE_CABINET AS a ON a.AUDNC_ID_ADNC = aa.APPAF_ID_ADNC
            JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = aa.APPAF_ID_INSTN
            LEFT JOIN v2.TYPE_CLASSEMENT AS tc ON tc.R_TYPECLASSE_VALEUR = aa.APPAF_TYPE_CLASSEMENT
            LEFT JOIN v2.CHAMBRE AS ch ON ch.R_CHAMBRE_VALEUR = a.AUDNC_CHAMBRE
            WHERE aa.APPAF_ID_APPAF = @id
            """, ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        string? Opt(string c) => FormeLigne.Texte(r, c) is { Length: > 0 } s ? s : null;
        return new Appel(n, Convert.ToInt32(r["APPAF_ID_INSTN"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "INSTN_NUM_ROLE"),
            FormeLigne.Texte(r, "INSTN_TYPE_INSTANCE"), Opt("APPAF_CODE_PLUMITIF"), FormeLigne.Texte(r, "APPAF_ETAT_RETOUR"), r["Prononce"] is true,
            Convert.ToInt32(r["AUDNC_ID_ADNC"], CultureInfo.InvariantCulture), FormeLigne.DateDe(r, "AUDNC_DATE"), Opt("AUDNC_HEURE"), Opt("AUDNC_CHAMBRE"),
            r["AvecCompo"] is true, Convert.ToInt32(r["Compositions"], CultureInfo.InvariantCulture));
    }

    internal static async Task<CodePlumitif?> LireCodeAsync(GesteEnCours geste, string code, CancellationToken ct, bool saisieSeulement = false)
    {
        await using var r = await geste.LireAsync("""
            SELECT R_CODEPLUMIT_VALEUR, R_CODEPLUMIT_PRONONCE, R_CODEPLUMIT_RENVOI, R_CODEPLUMIT_DELIBERE, R_CODEPLUMIT_DELIBERE_REPOUSSE,
                   R_CODEPLUMIT_CODE_EVE_PRO, R_CODEPLUMIT_AVEC_AUDIENCE,
                   R_CODEPLUMIT_TYPE_CLASSEMENT, R_CODEPLUMIT_CHAMBRE, R_CODEPLUMIT_UTILISABLE_SAISIE,
                   R_CODEPLUMIT_COMPATIBLE_CX, R_CODEPLUMIT_COMPATIBLE_PC, R_CODEPLUMIT_COMPATIBLE_REF, R_CODEPLUMIT_COMPATIBLE_JC,
                   R_CODEPLUMIT_COMPATIBLE_PRV, R_CODEPLUMIT_COMPATIBLE_OP
            FROM v2.CODE_PLUMITIF WHERE R_CODEPLUMIT_VALEUR = @c
            """, ct, new SqlParameter("@c", code));
        if (!await r.ReadAsync(ct)) return null;
        if (saisieSeulement && r["R_CODEPLUMIT_UTILISABLE_SAISIE"] is not true) return null;
        string? Opt(string c) => FormeLigne.Texte(r, c) is { Length: > 0 } s ? s : null;
        // EstCompatibleInstance (CODE_PLUMITIF.cs:76-97) : IP n'est jamais compatible, un type inconnu non plus.
        var compatibles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (type, colonne) in new[] { ("CX", "R_CODEPLUMIT_COMPATIBLE_CX"), ("PC", "R_CODEPLUMIT_COMPATIBLE_PC"), ("REF", "R_CODEPLUMIT_COMPATIBLE_REF"),
                                                ("JC", "R_CODEPLUMIT_COMPATIBLE_JC"), ("PRV", "R_CODEPLUMIT_COMPATIBLE_PRV"), ("OP", "R_CODEPLUMIT_COMPATIBLE_OP") })
            if (r[colonne] is true) compatibles.Add(type);
        var prononce = r["R_CODEPLUMIT_PRONONCE"] is true;
        return new CodePlumitif(FormeLigne.Texte(r, "R_CODEPLUMIT_VALEUR"), prononce, r["R_CODEPLUMIT_RENVOI"] is true,
            prononce || r["R_CODEPLUMIT_DELIBERE"] is true || r["R_CODEPLUMIT_DELIBERE_REPOUSSE"] is true, Opt("R_CODEPLUMIT_CODE_EVE_PRO"), Opt("R_CODEPLUMIT_AVEC_AUDIENCE"), Opt("R_CODEPLUMIT_TYPE_CLASSEMENT"), Opt("R_CODEPLUMIT_CHAMBRE"), compatibles);
    }

    /// <summary>Le type d'instance traduit (Instance.TypeInstanceTraduite, Instance.cs:967-983).</summary>
    internal static string Traduit(string typeInstance) => typeInstance switch
    {
        "07" => "CX",
        "08" => "PC",
        "09" => "REF",
        "PR" => "PRV",
        "RP" => "OP",
        _ => typeInstance,
    };

    /// <summary>L'audience est-elle passee (IsAudiencePassee, OPE_LIST_APPEL_AFFAIRE.cs:1343-1349 ; ModeAudienceCabinet,
    /// AudienceCabinet.cs:410-426) : sa date et son heure sont avant maintenant, ou elle est d'aujourd'hui ou d'avant sans heure.</summary>
    private static bool Passee(Appel a, DateTime maintenant) => Passee(a.DateAudience, a.Heure, maintenant);

    internal static bool Passee(DateTime? dateAudience, string? heureAudience, DateTime maintenant)
    {
        if (dateAudience is not { } jour) return false;
        if (heureAudience is null || !TimeSpan.TryParse(heureAudience, CultureInfo.InvariantCulture, out var heure)) return jour.Date <= maintenant.Date;
        return jour.Date.Add(heure) <= maintenant;
    }
}
