using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurRelancesComptesAnnuels;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Relances comptes annuels » (PROC_DCA_RELANCE) : « Valider » envoie la relance SUIVANTE (2 ou 3) des diligences cochees
/// dont la relance precedente est echue. Mode d'emploi : EcrivainEcran.cs ; la liste et la regle « A relancer » :
/// LecteurRelancesComptesAnnuels.cs. Plan « RIG v3 100 % fonctionnel », lot 8, 26/11/2026.
///
/// SOURCE (ancien RIG) : le moteur de relance qui tourne, PROC_RSURV (SURVEILLANCES\PROC_RSURV\PROC_RSURV.cs), branche SDCA
/// d'une relance N >= 2 (Sauvegarder, l. 79-131), ses courriers (GenererLesEditionsRelance, l. 320-380) et la cloture de ses
/// demandes a la validation (l. 245-247) ; ses reglages par numero de relance dans NATURE_DILIGENCE_PARAM (nature SDCA,
/// processus RSURV). PROC_RDCA (la classe de la fiche proc_dca_relance.md, qui ne compile plus : LecteurRelancesComptesAnnuels)
/// donne le nom de la demande du lot (PROC_RDCA.cs:115-119).
///
/// CONTROLES, dans l'ordre, avant toute ecriture :
///   1. au plus 1500 relances a la fois (DoVerifier, PROC_RSURV.cs:53-57), au message de RIG v2 ;
///   2. chaque ligne cochee est une diligence SDCA dont la relance suivante est DUE (la regle du lecteur, relue dans la
///      transaction). RIG v2 ne propose pas les autres (ses requetes ne les selectionnent pas) : la V3 refuse, sans rien ecrire ;
///   3. la relance N n'existe pas deja sur la diligence : RIG v2 l'avertit au message cite (PROC_RSURV.cs:121-122) et ne la
///      cree pas ; la V3 refuse avec ce message, sans rien ecrire.
///
/// CE QUI S'ECRIT, par diligence (la relance N = son etat relance / 2 + 1) :
///   - v2.DILIGENCE_RCS : DLGRC_ETAT_RELANCE = N x 2 (l. 110) et DLGRC_ETAT_EDI « 1 » (eEtatRelance.AEnvoyer, l. 113 ;
///     EtatRelance.cs:4) ;
///   - v2.RELANCE_RCS : la relance N (DiligenceRcs.AddRelanceRcs, l. 124) : auteur « 4 » (juge RCS, l. 99), intervenant « 1 »
///     (l. 79), date du jour, type = le reglage TypeRelance de la relance N, « S » (lettre simple) sans reglage (l. 334-335),
///     date limite = aujourd'hui + le reglage DelaiRelance (« Jour=15 », « Mois=1 » : NatureDiligence.CalculDateLimiteRelance,
///     NatureDiligence.cs:233-249), sans limite sans reglage (l. 125), non facturee ;
///   - v2.EXERCICE, pour chaque exercice a relancer de la diligence : EXRCC_DATE_RELANCE = aujourd'hui, EXRCC_ETAT_DCA =
///     l'etat relance deux fois (« 44 », l. 128-129) ;
///   - v2.DEMANDE : une demande du dossier, numerotee DMD, processus RSURV, nommee « Relance N SDCA (clotures) : dossier »
///     (l. 344-345), fille de la demande du lot (DMND_ID_DMND_MERE, l. 348), ses parametres (LstIdExer, NumRelance,
///     NatureDiligence, IdDiligenceRcs : l. 130, 207-209, au format ListeParametre « cle=valeur;... »), validee (l. 246) ;
///     et UNE demande du lot, « Relance des DCA - n occurence(s) » (PROC_RDCA.cs:115-119), validee ;
///   - v2.DEMANDE_EDITION : le courrier de la relance, si le reglage CodeParamEdition de la relance N designe un parametre
///     d'edition (v2.PARAMETRE_EDITION) -- sans lui, RIG v2 ne cree aucun courrier (l. 365) : la V3 non plus, et le dit. Les
///     valeurs de DemandeEdition.CreateDemandeEdition (DemandeEdition.cs) et CreateDemandeEditionByParametreGeneration :
///     phase « C » (a generer), papier NORMAL, media IMPR, un exemplaire, libelle = celui du parametre + « SDCA » (l. 366),
///     recto-verso et affichage avant impression du parametre, file d'impression (reglage FileImpression), destinataire = le
///     dossier. Le courrier lui-meme (le document Word) n'est pas produit : la file d'edition le generera.
/// Traces : famille « dossier », « a modifie », la relance avant et apres.
///
/// NON REPRIS (nomme) : l'arrivee et la ligne de rapprochement que Demande.CreateDemande cree avec chaque demande
/// (Demande.cs:479-517) ; le rattachement de l'action RCS a la demande (trt.Dmnd.AddActionRcs, l. 115 : rec.ActionRegistre n'a
/// pas de colonne de demande) ; le destinataire « premier dirigeant » de la relance 2 (reglage DestinataireRelance, l. 391) : a
/// faire, v2.DIRIGEANT_PP/PM sont dans la base V3 (mesure le 26/11/2026) ; la liste Aspose des dossiers relances (EditionListAspose) ; l'adresse du destinataire
/// (etablissement referent : rec.Adresse non lue ici).
/// </summary>
internal sealed class EcrivainRelancesComptesAnnuels : IEcrivainEcran
{
    public string Procedure => "PROC_DCA_RELANCE";
    public string Ecran => "Relances comptes annuels";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le refus de volume de RIG v2 (PROC_RSURV.cs:55-56), au mot pres.</summary>
    internal const int RelancesMax = 1500;
    internal const string MessageVolume = "Vous ne pouvez sélectionner que 1500 relances simultanéments";

    // La diligence cochee, son dossier et sa relance due (la regle du lecteur) ; une diligence passee a l'injonction en est
    // exclue, comme de la liste.
    private const string Diligence = $"""
        SELECT g.DLGRC_ID_DLGRC, g.DLGRC_ETAT_RELANCE, s.Id AS IdDossier, s.NumeroGestion, s.Siren, e.Civilite, e.Denomination,
               {RelanceDueSql} AS RelanceDue
        FROM v2.DILIGENCE_RCS AS g
        JOIN rec.ActionRegistre AS a ON a.Id = g.DLGRC_ID_ACTRC
        JOIN rec.DossierRcs AS s ON s.Id = a.IdDossier
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        WHERE g.DLGRC_ID_DLGRC = @id AND g.DLGRC_NATURE_DILIGENCE = 'SDCA'
          AND NOT EXISTS (SELECT 1 FROM v2.RELANCE_RCS AS i WHERE i.RLCRC_ID_DLGRC = g.DLGRC_ID_DLGRC
                          AND i.RLCRC_TYPE_RELANCE = 'IDCA' AND i.RLCRC_ID_INSTN IS NOT NULL)
        """;

    // Les exercices a relancer de la diligence (la regle du lecteur).
    private const string Exercices = $"""
        SELECT xd.EXRCC_ID_EXRCC, xd.EXRCC_DATE_CLOTURE
        FROM v2.EXERCICE AS xd JOIN v2.DILIGENCE_RCS AS g ON g.DLGRC_ID_DLGRC = xd.EXRCC_ID_DLGRC
        WHERE g.DLGRC_ID_DLGRC = @id AND {ExerciceARelancerSql}
        ORDER BY xd.EXRCC_DATE_CLOTURE, xd.EXRCC_ID_EXRCC
        """;

    private const string RelanceExistante = "SELECT COUNT(*) FROM v2.RELANCE_RCS WHERE RLCRC_ID_DLGRC = @id AND RLCRC_NUM_ORDRE = @num";

    // Nom : la ligne de l'ecran (designation et SIREN) ; Designation : celle de RIG v2, civilite et denomination
    // (DossierRCS.DesignationShortAvecCivilite), dans le nom de la demande et le destinataire du courrier.
    private sealed record Ligne(int Id, string EtatRelance, int IdDossier, string NumeroGestion, string Nom, string Designation, int Relance);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser("Aucune relance cochée : rien n'est relancé.");
        // 1. Le volume (DoVerifier).
        if (ids.Length > RelancesMax) return IssueGeste.Refuser(MessageVolume);
        var jour = geste.Maintenant.Date;

        // 2. et 3. Chaque diligence : relance due, et pas deja faite.
        var lignes = new List<(Ligne L, List<(int Id, DateTime Cloture)> Exercices)>();
        foreach (var id in ids)
        {
            if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var idDiligence))
                return IssueGeste.Refuser($"Relance introuvable dans la base du greffe ({id}).");
            Ligne? l = null;
            await using (var r = await geste.LireAsync(Diligence, ct, new SqlParameter("@id", idDiligence), Jour(jour)))
                if (await r.ReadAsync(ct))
                    l = new Ligne(idDiligence, FormeLigne.Texte(r, "DLGRC_ETAT_RELANCE"), (int)r["IdDossier"],
                                  FormeLigne.Texte(r, "NumeroGestion"), FormeRelance.NomDossier(r),
                                  string.Join(' ', new[] { FormeLigne.Texte(r, "Civilite"), FormeLigne.Texte(r, "Denomination") }.Where(x => x.Length > 0)),
                                  r["RelanceDue"] is int n ? n : 0);
            if (l is null) return IssueGeste.Refuser($"Relance introuvable dans la base du greffe ({id}).");
            if (l.Relance == 0)
                return IssueGeste.Refuser($"{l.Nom} : aucune relance n'est due (la relance précédente n'est pas échue, ou la diligence est close).");
            var exercices = new List<(int, DateTime)>();
            await using (var r = await geste.LireAsync(Exercices, ct, new SqlParameter("@id", idDiligence), Jour(jour)))
                while (await r.ReadAsync(ct)) exercices.Add(((int)r["EXRCC_ID_EXRCC"], (DateTime)r["EXRCC_DATE_CLOTURE"]));
            if (Convert.ToInt32(await Scalaire(geste, RelanceExistante, ct, new SqlParameter("@id", idDiligence), new SqlParameter("@num", l.Relance)),
                                CultureInfo.InvariantCulture) > 0)
                return IssueGeste.Refuser(
                    $"L'exercice {FormeLigne.Date(exercices.FirstOrDefault().Item2)} du dossier {l.NumeroGestion} est déjà lié à une relance {l.Relance}.");
            lignes.Add((l, exercices));
        }

        // La demande du lot (PROC_RDCA.cs:115-119), validee avec ses filles (PROC_RSURV.cs:245-247).
        var lot = await DemandeAsync(geste, null, $"Relance des DCA - {lignes.Count} occurence(s)", null, null, null, ct);
        if (lot.Refus is not null) return IssueGeste.Refuser(lot.Refus);

        var traces = new List<TraceGeste>();
        var courriers = new List<string>();
        foreach (var (l, exercices) in lignes)
        {
            var reglages = await ReglagesAsync(geste, l.Relance, ct);
            var etatRelance = (l.Relance * 2).ToString(CultureInfo.InvariantCulture);
            var limite = Limite(reglages.Delai, jour);

            await geste.EcrireAsync("UPDATE v2.DILIGENCE_RCS SET DLGRC_ETAT_RELANCE = @etat, DLGRC_ETAT_EDI = '1' WHERE DLGRC_ID_DLGRC = @id", ct,
                new SqlParameter("@etat", etatRelance), new SqlParameter("@id", l.Id));
            await geste.InsererAsync("v2.RELANCE_RCS", "RLCRC_ID_RLCRC", new Dictionary<string, object?>
            {
                ["RLCRC_ID_DLGRC"] = l.Id, ["RLCRC_NUM_ORDRE"] = (short)l.Relance, ["RLCRC_AUTEUR"] = "4", ["RLCRC_INTERVENANT"] = "1",
                ["RLCRC_DATE_RELANCE"] = jour, ["RLCRC_TYPE_RELANCE"] = reglages.Type, ["RLCRC_DATE_LIMITE"] = limite, ["RLCRC_FACTURE"] = false,
            }, ct);
            foreach (var (idExercice, _) in exercices)
                await geste.EcrireAsync("UPDATE v2.EXERCICE SET EXRCC_DATE_RELANCE = @jour, EXRCC_ETAT_DCA = @etatDca WHERE EXRCC_ID_EXRCC = @id", ct,
                    new SqlParameter("@jour", jour), new SqlParameter("@etatDca", etatRelance + etatRelance), new SqlParameter("@id", idExercice));

            var clotures = exercices.Count == 0 ? "" : $" ({string.Join(",", exercices.Select(x => FormeLigne.Date(x.Cloture)))})";
            var parametres = $"LstIdExer={string.Concat(exercices.Select(x => x.Id.ToString(CultureInfo.InvariantCulture) + ","))};"
                           + $"NumRelance={l.Relance};NatureDiligence=SDCA;IdDiligenceRcs={l.Id}";
            var fille = await DemandeAsync(geste, l, $"Relance {l.Relance} SDCA{clotures} : {l.Designation}", l.NumeroGestion, lot.Id, parametres, ct);
            if (fille.Refus is not null) return IssueGeste.Refuser(fille.Refus);

            var courrier = await CourrierAsync(geste, fille.Id!.Value, l, reglages, ct);
            if (courrier is not null) courriers.Add(courrier);
            traces.Add(new TraceGeste("dossier", $"le dossier {l.NumeroGestion}", l.IdDossier.ToString(CultureInfo.InvariantCulture), "a modifie",
                new Dictionary<string, string> { ["Relance"] = $"relance {l.Relance - 1} échue", ["Etat relance"] = l.EtatRelance },
                new Dictionary<string, string>
                {
                    ["Relance"] = $"relance {l.Relance} du {FormeLigne.Date(jour)}{(limite is { } d ? $", limite {FormeLigne.Date(d)}" : "")}",
                    ["Etat relance"] = etatRelance, ["Demande"] = fille.Numero!, ["Courrier"] = courrier ?? "aucun : pas de paramètre d'édition",
                }));
        }

        return IssueGeste.Ecrit(Message(lignes.Select(x => x.L.Relance), courriers),
                                [.. lignes.Select(x => x.L.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }

    /// <summary>« Relances 2 et 3 faites pour 2 dossiers : 2 courriers « … », « … » en file d'édition. »</summary>
    internal static string Message(IEnumerable<int> relances, IReadOnlyCollection<string> courriers)
    {
        var faites = relances.ToList();
        var numeros = faites.Distinct().Order().Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList();
        var quoi = numeros.Count == 1 ? $"Relance {numeros[0]} faite" : $"Relances {string.Join(", ", numeros[..^1])} et {numeros[^1]} faites";
        var n = faites.Count;
        return $"{quoi} pour {n} dossier{(n > 1 ? "s" : "")} : "
             + (courriers.Count == 0
                 ? "aucun courrier, aucun paramètre d'édition n'est réglé pour cette relance."
                 : $"{courriers.Count} courrier{(courriers.Count > 1 ? "s" : "")} « {string.Join(" », « ", courriers.Distinct())} » en file d'édition.");
    }

    private static SqlParameter Jour(DateTime jour) => new("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = jour };

    private static async Task<object?> Scalaire(GesteEnCours geste, string sql, CancellationToken ct, params SqlParameter[] p)
    {
        await using var r = await geste.LireAsync(sql, ct, p);
        return await r.ReadAsync(ct) ? r.GetValue(0) : null;
    }

    // ------------------------------------------------------------ LES REGLAGES DE LA RELANCE N (NATURE_DILIGENCE_PARAM)

    /// <summary>Les reglages de la relance N, nature SDCA, processus RSURV : type de relance, delai, parametre d'edition et file
    /// d'impression. Une base sans la table de reglages les laisse vides : relance « S », sans limite ni courrier.</summary>
    private sealed record Reglages(string Type, string? Delai, string? CodeEdition, string? File);

    private static async Task<Reglages> ReglagesAsync(GesteEnCours geste, int relance, CancellationToken ct)
    {
        var valeurs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (await Scalaire(geste, "SELECT OBJECT_ID(N'v2.NATURE_DILIGENCE_PARAM')", ct) is not DBNull and not null)
            await using (var r = await geste.LireAsync("""
                SELECT NADIP_CODE_PARAM, NADIP_VALEUR_PARAM FROM v2.NATURE_DILIGENCE_PARAM
                WHERE NADIP_NATURE_DILIGENCE = 'SDCA' AND NADIP_CODE_PROCESSUS = 'RSURV' AND NADIP_NUM_ORDRE = @num
                  AND NADIP_CODE_PARAM IN ('TypeRelance', 'DelaiRelance', 'CodeParamEdition', 'FileImpression')
                """, ct, new SqlParameter("@num", relance)))
                while (await r.ReadAsync(ct)) valeurs[FormeLigne.Texte(r, "NADIP_CODE_PARAM")] = FormeLigne.Texte(r, "NADIP_VALEUR_PARAM");
        string? Lu(string code) => valeurs.TryGetValue(code, out var v) && v.Length > 0 ? v : null;
        return new Reglages(Lu("TypeRelance") ?? "S", Lu("DelaiRelance"), Lu("CodeParamEdition"), Lu("FileImpression"));
    }

    /// <summary>La date limite d'apres le reglage DelaiRelance (« Jour=15 », « Mois=1 », « Annee=1 ») : la derniere cle lue
    /// l'emporte, comme NatureDiligence.CalculDateLimiteRelance (NatureDiligence.cs:233-249) ; null sans reglage.</summary>
    internal static DateTime? Limite(string? delai, DateTime jour)
    {
        if (string.IsNullOrWhiteSpace(delai)) return null;
        var cles = delai.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=', 2))
                        .Where(x => x.Length == 2).ToDictionary(x => x[0].Trim(), x => x[1].Trim(), StringComparer.Ordinal);
        DateTime? limite = null;
        if (cles.TryGetValue("Jour", out var j) && int.TryParse(j, CultureInfo.InvariantCulture, out var nj)) limite = jour.AddDays(nj);
        if (cles.TryGetValue("Mois", out var m) && int.TryParse(m, CultureInfo.InvariantCulture, out var nm)) limite = jour.AddMonths(nm);
        if (cles.TryGetValue("Annee", out var a) && int.TryParse(a, CultureInfo.InvariantCulture, out var na)) limite = jour.AddYears(na);
        return limite;
    }

    // ------------------------------------------------------------ LA DEMANDE ET LE COURRIER

    /// <summary>Une demande RSURV validee (Demande.CreateDemande puis Cloturer(Validee)) : du dossier de <paramref name="l"/>,
    /// ou celle du lot (l null).</summary>
    private static async Task<(int? Id, string? Numero, string? Refus)> DemandeAsync(GesteEnCours geste, Ligne? l, string nom, string? indicatif,
        int? mere, string? parametres, CancellationToken ct)
    {
        var (numero, refus) = await CreationV2.NumeroAsync(geste, "DMD", geste.Maintenant.Year, ct);
        if (numero is null) return (null, null, refus);
        var id = await CreationV2.InsererAsync(geste, "v2.DEMANDE", "DMND_ID_DMND", new Dictionary<string, object?>
        {
            ["DMND_NUM_DEMANDE"] = numero, ["DMND_ETAT_DEMANDE"] = "V", ["DMND_CODE_PROSS"] = "RSURV",
            ["DMND_ID_DSRCS"] = l?.IdDossier, ["DMND_NOM"] = nom.Length > 255 ? nom[..255] : nom,
            ["DMND_NUM_INDICATIF"] = indicatif, ["DMND_LES_NUMEROS"] = indicatif, ["DMND_ID_DMND_MERE"] = mere,
            ["DMND_TEXTE_PARAMETRE"] = parametres, ["DMND_DATE_CREATION"] = geste.Maintenant, ["DMND_DATE_VALIDATION"] = geste.Maintenant,
            ["DMND_EN_COURS"] = false, ["DMND_NOM_UTILISATEUR"] = geste.Agent.NomAffiche,
        }, ct);
        return (id, numero, null);
    }

    /// <summary>Le courrier de la relance (v2.DEMANDE_EDITION), si son parametre d'edition existe ; son libelle, null sinon.</summary>
    private static async Task<string?> CourrierAsync(GesteEnCours geste, int idDemande, Ligne l, Reglages reglages, CancellationToken ct)
    {
        if (reglages.CodeEdition is null || await Scalaire(geste, "SELECT OBJECT_ID(N'v2.PARAMETRE_EDITION')", ct) is DBNull or null) return null;
        // Les colonnes du parametre que la table du greffe porte (une base de banc n'en a qu'une partie).
        var colonnesParametre = await ColonnesAsync(geste, "v2.PARAMETRE_EDITION", ct);
        string Colonne(string nom) => colonnesParametre.Contains(nom) ? nom : $"NULL AS {nom}";
        (string Libelle, bool RectoVerso, bool Afficher, object? Cfecp, string TypeGeneration)? parametre = null;
        await using (var r = await geste.LireAsync($"""
            SELECT PRMED_LIBELLE, PRMED_RECTO_VERSO, PRMED_AFFICHER_AVANT_IMPRESSION, PRMED_CODE_CFECP,
                   {Colonne("PRMED_TYPE_GENERATION")}, {Colonne("PRMED_LISTE")}
            FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = @code
            """, ct, new SqlParameter("@code", reglages.CodeEdition)))
            if (await r.ReadAsync(ct))
                parametre = (FormeLigne.Texte(r, "PRMED_LIBELLE"), r["PRMED_RECTO_VERSO"] is true, r["PRMED_AFFICHER_AVANT_IMPRESSION"] is true,
                             r["PRMED_CODE_CFECP"] is DBNull ? null : r["PRMED_CODE_CFECP"],
                             TypeGeneration(FormeLigne.Texte(r, "PRMED_TYPE_GENERATION"), FormeLigne.Texte(r, "PRMED_LISTE")));
        if (parametre is not { } p) return null;
        var libelle = $"{p.Libelle} SDCA";
        var valeurs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DMNED_ID_DMND"] = idDemande, ["DMNED_DATE_CREATION"] = geste.Maintenant, ["DMNED_TYPE_PAPIER"] = "NORMAL",
            ["DMNED_MEDIA"] = "IMPR", ["DMNED_ETAT_EMISSION"] = "N", ["DMNED_PHASE_TRAITEMENT"] = "C",
            ["DMNED_UTILISATEUR"] = geste.Agent.CompteWindows, ["DMNED_INTERLOCUTEUR"] = geste.Agent.CompteWindows,
            ["DMNED_NOMBRE_EXEMPLAIRE"] = 1, ["DMNED_IMPRESSION"] = true, ["DMNED_PAPIER_TRAME"] = false, ["DMNED_AGRAFAGE"] = false,
            ["DMNED_RECTO_VERSO"] = p.RectoVerso, ["DMNED_AFF_AVANT_IMPRESSION"] = p.Afficher, ["DMNED_LIBELLE_DOCUMENT"] = libelle,
            ["DMNED_GROUPAGE_AUTOMATIQUE"] = true, ["DMNED_RESUME"] = false, ["DMNED_READ_ONLY"] = false,
            ["DMNED_CODE_ENTITE_SOURCE"] = "DOSSIER_RCS", ["DMNED_VALEUR_ID_SOURCE"] = l.IdDossier.ToString(CultureInfo.InvariantCulture),
            ["DMNED_CODE_CFECP"] = p.Cfecp, ["DMNED_LOT"] = reglages.File, ["DMNED_DESIGNATION_DESTINATAIRE"] = l.Designation,
            // Le type de generation du parametre (CreateDemandeEditionByParametreGeneration, DemandeEdition.cs:2588-2589).
            ["DMNED_TYPE_GENERATION"] = p.TypeGeneration,
            // fix-ok: mesure le 26/11/2026 (banc reuni au lot 6 de main, erreur 515 « Cannot insert the value NULL into column
            // 'DMNED_MASQUER_COFFRE_FORT' ») -- RIG_DEV donne a ces colonnes NOT NULL le defaut ((0)) (INFORMATION_SCHEMA de
            // SQL-DEV\DEV), que la base V3 n'a pas repris : la V2 les laisse a ce defaut, ecrit ici en clair (comme
            // EcrivainRegistresJudiciaires). La copie de dev n'a pas montre le defaut : aucun courrier n'y est regle.
            ["DMNED_ENVOI_PAR_MAIL"] = false, ["DMNED_HAS_NUMLRAR"] = false, ["DMNED_IS_NON_SUPPRESSIBLE"] = false,
            ["DMNED_HAS_NUMLR"] = false, ["DMNED_MASQUER_COFFRE_FORT"] = false,
        };
        // Les colonnes que la table du greffe porte (une base de banc n'en a qu'une partie) : celles de RIG v2 sinon.
        var presentes = await ColonnesAsync(geste, "v2.DEMANDE_EDITION", ct);
        await geste.InsererAsync("v2.DEMANDE_EDITION", "DMNED_ID_DMNED",
            valeurs.Where(v => presentes.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value), ct);
        return libelle;
    }

    /// <summary>Les colonnes de <paramref name="table"/> dans la base du greffe.</summary>
    private static async Task<HashSet<string>> ColonnesAsync(GesteEnCours geste, string table, CancellationToken ct)
    {
        var colonnes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var r = await geste.LireAsync("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@table)", ct,
                                                  new SqlParameter("@table", table));
        while (await r.ReadAsync(ct)) colonnes.Add(r.GetString(0));
        return colonnes;
    }

    /// <summary>Les types de generation de RIG v2 (DemandeEdition.eTypeGeneration, DemandeEdition.cs:160-206), par valeur.</summary>
    private static readonly string[] TypesGeneration =
        ["0", "EMISSION_SIMPLE", "FUSION_CRYSTAL", "FUSION_CRYSTAL_NET", "FUSION_RIG", "5", "KBIS", "REPORT_SQL", "RIG_LOT_UN", "FACTURE",
         "FUSION_ASPOSE", "REDA_CREATE", "REDA_EDITION", "REDA_DEPEND", "REDA_LETTRE", "BORDEREAU"];

    /// <summary>Le type de generation que la V2 ecrit : celui du parametre d'edition (sa colonne, ou l'attribut TYPE_GENERATION de
    /// sa liste XML), lu comme Enum.TryParse sans casse ni nombre hors enum (GetTypeGenerationFromString, DemandeEdition.cs:207-211),
    /// « 0 » s'il ne se lit pas ; INFOGREFFE est un alias de FUSION_RIG (l. 172).</summary>
    internal static string TypeGeneration(string colonne, string liste)
    {
        var brut = colonne.Length > 0 ? colonne
                 : System.Text.RegularExpressions.Regex.Match(liste, "TYPE_GENERATION=\"([^\"]*)\"") is { Success: true } m ? m.Groups[1].Value : "";
        brut = brut.Trim();
        if (brut.Equals("INFOGREFFE", StringComparison.OrdinalIgnoreCase)) return "FUSION_RIG";
        if (int.TryParse(brut, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return n >= 0 && n < TypesGeneration.Length ? TypesGeneration[n] : n.ToString(CultureInfo.InvariantCulture);
        return TypesGeneration.FirstOrDefault(t => t.Equals(brut, StringComparison.OrdinalIgnoreCase) && !char.IsAsciiDigit(t[0])) ?? "0";
    }
}
