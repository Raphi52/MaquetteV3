using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurRelancesComptesAnnuels;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Relances comptes annuels » (PROC_DCA_RELANCE) : « Valider » envoie la relance 1 des dossiers coches (lignes « A relancer »
/// d'Id « dossier:<id> ») et la relance SUIVANTE (2 ou 3) des diligences cochees dont la relance precedente est echue. Mode
/// d'emploi : EcrivainEcran.cs ; la liste et la regle « A relancer » : LecteurRelancesComptesAnnuels.cs. Plan « RIG v3 100 %
/// fonctionnel », lot 8, 26/11/2026.
///
/// SOURCE (ancien RIG) : le moteur de relance qui tourne, PROC_RSURV (SURVEILLANCES\PROC_RSURV\PROC_RSURV.cs), branche SDCA
/// d'une relance N >= 2 (Sauvegarder, l. 79-131), ses courriers (GenererLesEditionsRelance, l. 320-380) et la cloture de ses
/// demandes a la validation (l. 245-247) ; ses reglages par numero de relance dans NATURE_DILIGENCE_PARAM (nature SDCA,
/// processus RSURV). PROC_RDCA (la classe de la fiche proc_dca_relance.md, qui ne compile plus : LecteurRelancesComptesAnnuels)
/// donne le nom de la demande du lot (PROC_RDCA.cs:115-119).
///
/// CONTROLES, dans l'ordre, avant toute ecriture :
///   1. au plus 1500 relances a la fois (DoVerifier, PROC_RSURV.cs:53-57), au message de RIG v2 ;
///   2. chaque ligne cochee est un dossier dont des exercices sont dans la recherche de la relance 1, ou une diligence SDCA dont
///      la relance suivante est DUE (les regles du lecteur, relues dans la transaction). RIG v2 ne propose pas les autres (ses
///      requetes ne les selectionnent pas) : la V3 refuse, sans rien ecrire ;
///   3. la relance N n'existe pas deja sur la diligence : RIG v2 l'avertit au message cite (PROC_RSURV.cs:121-122) et ne la
///      cree pas ; la V3 refuse avec ce message, sans rien ecrire.
///
/// CE QUI S'ECRIT POUR LA RELANCE 1, par dossier coche (Sauvegarder, branche SDCA, NumRelance 1, l. 79-131) :
///   - rec.ActionRegistre : l'action SDCA du dossier -- la seule action SDCA d'etat « 2 » qu'il a deja (DossierRCS.
///     ActionsRcsSDCAEtat2, DossierRCS.cs:1635-1640), sinon une neuve (ActionRcs.CreateActionRcs, l. 91 : code processus « SDCA »,
///     arrivee du jour, numero RA1, etat info « 1 », etat « 2 ») ; la meme pour tous les exercices du dossier (« un autre exercice
///     du dossier peut deja avoir ete relance et on a donc deja pu creer l'action RCS SDCA », l. 88) ;
///   - v2.DILIGENCE_RCS : UNE diligence par exercice (DiligenceRcs.CreateDiligenceRcs("G", "SDCA", "NP", "1", "2"), l. 86) :
///     origine « G », motif « NP », en cours, etat relance « 2 » (l. 110), etat EDI « 1 » (l. 113) ; l'exercice la porte
///     (exer.CreerDiligence : EXRCC_ID_DLGRC) ;
///   - v2.RELANCE_RCS : sa relance 1 (l. 123-125) : auteur « 3 » (greffe, l. 93), intervenant « 1 », type = le reglage
///     TypeRelance, « S » sans reglage, date limite = aujourd'hui + DelaiRelance (« Mois=1 » dans RIG_DEV), non facturee ;
///   - v2.EXERCICE : EXRCC_DATE_RELANCE = aujourd'hui, EXRCC_ETAT_DCA = « 22 » (l. 128-129) ;
///   - v2.DEMANDE : la demande RSURV validee du dossier, « Relance 1 SDCA (clotures) : designation », portant l'action, parametres
///     LstIdExer, NumRelance=1, NatureDiligence, IdDiligenceRcs (la derniere diligence creee, l. 207-209) ; le courrier
///     (CodeParamEdition de la relance 1 : LOT2_RGDCA dans RIG_DEV), comme pour les relances suivantes ci-dessous.
///   Un exercice deja lie a une diligence (close, ou en cours sans relance) en recoit une neuve, et RIG v2 l'avertit (l. 82-85) :
///   la V3 ajoute son avertissement au message, au mot pres.
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
/// La demande du dossier porte l'action RCS de la diligence (trt.Dmnd.AddActionRcs, l. 115 : DMND_ID_ACTRC).
/// NON REPRIS (nomme) : l'arrivee et la ligne de rapprochement que Demande.CreateDemande cree avec chaque demande
/// (Demande.cs:479-517) ; le destinataire « premier dirigeant » de la relance 2 (reglage DestinataireRelance, l. 391) : a
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
        SELECT g.DLGRC_ID_DLGRC, g.DLGRC_ETAT_RELANCE, g.DLGRC_ID_ACTRC, s.Id AS IdDossier, s.NumeroGestion, s.Siren, e.Civilite, e.Denomination,
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

    // Le dossier coche pour la relance 1, et ses exercices dans la recherche de la relance 1 (la regle du lecteur).
    private const string DossierRelance1 = """
        SELECT s.Id, s.NumeroGestion, s.Siren, e.Civilite, e.Denomination
        FROM rec.DossierRcs AS s LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        WHERE s.Id = @id
        """;

    private const string ExercicesRelance1 = $"""
        SELECT xd.EXRCC_ID_EXRCC, xd.EXRCC_DATE_CLOTURE, xd.EXRCC_ID_DLGRC
        FROM v2.EXERCICE AS xd JOIN rec.DossierRcs AS s ON s.Id = xd.EXRCC_ID_DSRCS
        WHERE s.Id = @id AND {ExerciceRelance1Sql}
        ORDER BY xd.EXRCC_DATE_CLOTURE, xd.EXRCC_ID_EXRCC
        """;

    // L'action SDCA d'etat « 2 » du dossier, s'il n'en a qu'une (DossierRCS.ActionsRcsSDCAEtat2, DossierRCS.cs:1635-1640).
    private const string ActionSdca = """
        SELECT MIN(a.Id) AS Id, COUNT(*) AS Nombre FROM rec.ActionRegistre AS a
        WHERE a.IdDossier = @id AND a.CodeProcessus = 'SDCA' AND a.EtatAction = '2'
        """;

    /// <summary>Un dossier coche pour la relance 1 : son nom a l'ecran, la designation de RIG v2, ses exercices trouves.</summary>
    private sealed record DossierR1(int IdDossier, string NumeroGestion, string Nom, string Designation,
                                    List<(int Id, DateTime Cloture, int? Diligence)> Exercices);

    // Nom : la ligne de l'ecran (designation et SIREN) ; Designation : celle de RIG v2, civilite et denomination
    // (DossierRCS.DesignationShortAvecCivilite), dans le nom de la demande et le destinataire du courrier.
    private sealed record Ligne(int Id, string EtatRelance, int IdAction, int IdDossier, string NumeroGestion, string Nom, string Designation, int Relance);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser("Aucune relance cochée : rien n'est relancé.");
        // 1. Le volume (DoVerifier).
        if (ids.Length > RelancesMax) return IssueGeste.Refuser(MessageVolume);
        var jour = geste.Maintenant.Date;

        // 2. et 3. Chaque ligne : un dossier de la relance 1, ou une diligence dont la relance est due et pas deja faite.
        var lignes = new List<(Ligne L, List<(int Id, DateTime Cloture)> Exercices)>();
        var dossiers = new List<DossierR1>();
        foreach (var id in ids)
        {
            if (id.StartsWith(PrefixeDossier, StringComparison.Ordinal))
            {
                if (!int.TryParse(id[PrefixeDossier.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var idDossier))
                    return IssueGeste.Refuser($"Relance introuvable dans la base du greffe ({id}).");
                DossierR1? d = null;
                await using (var r = await geste.LireAsync(DossierRelance1, ct, new SqlParameter("@id", idDossier)))
                    if (await r.ReadAsync(ct))
                        d = new DossierR1(idDossier, FormeLigne.Texte(r, "NumeroGestion"), FormeRelance.NomDossier(r),
                                          string.Join(' ', new[] { FormeLigne.Texte(r, "Civilite"), FormeLigne.Texte(r, "Denomination") }.Where(x => x.Length > 0)), []);
                if (d is null) return IssueGeste.Refuser($"Relance introuvable dans la base du greffe ({id}).");
                await using (var r = await geste.LireAsync(ExercicesRelance1, ct, new SqlParameter("@id", idDossier), Jour(jour)))
                    while (await r.ReadAsync(ct))
                        d.Exercices.Add(((int)r["EXRCC_ID_EXRCC"], (DateTime)r["EXRCC_DATE_CLOTURE"], r["EXRCC_ID_DLGRC"] is int g ? g : null));
                if (d.Exercices.Count == 0)
                    return IssueGeste.Refuser($"{d.Nom} : aucune relance n'est due (aucun exercice du dossier n'est dans la recherche de la relance 1).");
                dossiers.Add(d);
                continue;
            }
            if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var idDiligence))
                return IssueGeste.Refuser($"Relance introuvable dans la base du greffe ({id}).");
            Ligne? l = null;
            await using (var r = await geste.LireAsync(Diligence, ct, new SqlParameter("@id", idDiligence), Jour(jour)))
                if (await r.ReadAsync(ct))
                    l = new Ligne(idDiligence, FormeLigne.Texte(r, "DLGRC_ETAT_RELANCE"), (int)r["DLGRC_ID_ACTRC"], (int)r["IdDossier"],
                                  FormeLigne.Texte(r, "NumeroGestion"), FormeRelance.NomDossier(r),
                                  string.Join(' ', new[] { FormeLigne.Texte(r, "Civilite"), FormeLigne.Texte(r, "Denomination") }.Where(x => x.Length > 0)),
                                  r["RelanceDue"] is int n ? n : 0);
            if (l is null) return IssueGeste.Refuser($"Relance introuvable dans la base du greffe ({id}).");
            if (l.Relance == 0)
                return IssueGeste.Refuser($"{l.Nom} : aucune relance n'est due (la relance précédente n'est pas échue, ou la diligence est close).");
            var exercices = new List<(int, DateTime)>();
            await using (var r = await geste.LireAsync(Exercices, ct, new SqlParameter("@id", idDiligence), Jour(jour)))
                while (await r.ReadAsync(ct)) exercices.Add(((int)r["EXRCC_ID_EXRCC"], (DateTime)r["EXRCC_DATE_CLOTURE"]));
            if (Convert.ToInt32(await RelancesRsurv.Scalaire(geste, RelanceExistante, ct, new SqlParameter("@id", idDiligence), new SqlParameter("@num", l.Relance)),
                                CultureInfo.InvariantCulture) > 0)
                return IssueGeste.Refuser(
                    $"L'exercice {FormeLigne.Date(exercices.FirstOrDefault().Item2)} du dossier {l.NumeroGestion} est déjà lié à une relance {l.Relance}.");
            lignes.Add((l, exercices));
        }

        // La demande du lot (PROC_RDCA.cs:115-119), validee avec ses filles (PROC_RSURV.cs:245-247).
        var lot = await RelancesRsurv.DemandeAsync(geste, null, $"Relance des DCA - {lignes.Count + dossiers.Count} occurence(s)", null, null, null, null, ct);
        if (lot.Refus is not null) return IssueGeste.Refuser(lot.Refus);

        var traces = new List<TraceGeste>();
        var courriers = new List<string>();
        var faites = new List<int>();
        var idsFaits = new List<string>();
        var avertissements = new List<string>();

        // La relance 1 des dossiers coches (PROC_RSURV.cs:79-131, NumRelance 1).
        if (dossiers.Count > 0)
        {
            var reglages1 = await RelancesRsurv.ReglagesAsync(geste, "SDCA", 1, ct);
            var limite1 = RelancesRsurv.Limite(reglages1.Delai, jour);
            foreach (var d in dossiers)
            {
                int action;
                var (actionExistante, nombre) = (0, 0);
                await using (var r = await geste.LireAsync(ActionSdca, ct, new SqlParameter("@id", d.IdDossier)))
                    if (await r.ReadAsync(ct)) (actionExistante, nombre) = (r["Id"] is int a ? a : 0, (int)r["Nombre"]);
                string? arrivee = null;
                if (nombre == 1) action = actionExistante;
                else
                {
                    var (numero, refusArrivee) = await CreationV2.NumeroAsync(geste, "RA1", jour.Year, ct);
                    if (numero is null) return IssueGeste.Refuser(refusArrivee!);
                    arrivee = numero;
                    action = await geste.InsererAsync("rec.ActionRegistre", "Id", new Dictionary<string, object?>
                    {
                        ["IdDossier"] = d.IdDossier, ["NumeroArrivee"] = numero, ["DateArrivee"] = jour, ["CodeProcessus"] = "SDCA",
                        ["EtatInfo"] = "1", ["EtatAction"] = "2", ["SurNotification"] = false,
                    }, ct);
                }
                var derniere = 0;
                foreach (var (idExercice, cloture, diligenceAvant) in d.Exercices)
                {
                    if (diligenceAvant is not null)
                        avertissements.Add($"L'exercice {FormeLigne.Date(cloture)} du dossier {d.NumeroGestion} est déjà lié à une diligence.");
                    derniere = await geste.InsererAsync("v2.DILIGENCE_RCS", "DLGRC_ID_DLGRC", new Dictionary<string, object?>
                    {
                        ["DLGRC_ID_ACTRC"] = action, ["DLGRC_ORIGINE_INFO"] = "G", ["DLGRC_NATURE_DILIGENCE"] = "SDCA", ["DLGRC_MOTIF_DILIGENCE"] = "NP",
                        ["DLGRC_ETAT_DILIGENCE"] = "1", ["DLGRC_ETAT_RELANCE"] = "2", ["DLGRC_ETAT_EDI"] = "1",
                    }, ct);
                    await geste.InsererAsync("v2.RELANCE_RCS", "RLCRC_ID_RLCRC", new Dictionary<string, object?>
                    {
                        ["RLCRC_ID_DLGRC"] = derniere, ["RLCRC_NUM_ORDRE"] = (short)1, ["RLCRC_AUTEUR"] = "3", ["RLCRC_INTERVENANT"] = "1",
                        ["RLCRC_DATE_RELANCE"] = jour, ["RLCRC_TYPE_RELANCE"] = reglages1.Type, ["RLCRC_DATE_LIMITE"] = limite1, ["RLCRC_FACTURE"] = false,
                    }, ct);
                    await geste.EcrireAsync("""
                        UPDATE v2.EXERCICE SET EXRCC_ID_DLGRC = @dlg, EXRCC_DATE_RELANCE = @jour, EXRCC_ETAT_DCA = '22' WHERE EXRCC_ID_EXRCC = @id
                        """, ct, new SqlParameter("@dlg", derniere), new SqlParameter("@jour", jour), new SqlParameter("@id", idExercice));
                    idsFaits.Add(derniere.ToString(CultureInfo.InvariantCulture));
                }
                var clotures = $" ({string.Join(",", d.Exercices.Select(x => FormeLigne.Date(x.Cloture)))})";
                var parametres = $"LstIdExer={string.Concat(d.Exercices.Select(x => x.Id.ToString(CultureInfo.InvariantCulture) + ","))};"
                               + $"NumRelance=1;NatureDiligence=SDCA;IdDiligenceRcs={derniere}";
                var fille = await RelancesRsurv.DemandeAsync(geste, d.IdDossier, $"Relance 1 SDCA{clotures} : {d.Designation}", d.NumeroGestion, lot.Id,
                                                             parametres, action, ct);
                if (fille.Refus is not null) return IssueGeste.Refuser(fille.Refus);
                var courrier = await RelancesRsurv.CourrierAsync(geste, fille.Id!.Value, d.IdDossier, d.Designation, "SDCA", reglages1, ct);
                if (courrier is not null) courriers.Add(courrier);
                faites.Add(1);
                traces.Add(new TraceGeste("dossier", $"le dossier {d.NumeroGestion}", d.IdDossier.ToString(CultureInfo.InvariantCulture), "a cree", null,
                    new Dictionary<string, string>
                    {
                        ["Relance"] = $"relance 1 du {FormeLigne.Date(jour)}{(limite1 is { } l1 ? $", limite {FormeLigne.Date(l1)}" : "")}",
                        ["Exercices"] = string.Join(", ", d.Exercices.Select(x => FormeLigne.Date(x.Cloture))),
                        ["Action"] = arrivee is null ? $"action SDCA {action} reprise" : $"arrivée {arrivee}", ["Demande"] = fille.Numero!,
                        ["Courrier"] = courrier ?? "aucun : pas de paramètre d'édition",
                    }));
            }
        }

        foreach (var (l, exercices) in lignes)
        {
            var reglages = await RelancesRsurv.ReglagesAsync(geste, "SDCA", l.Relance, ct);
            var etatRelance = (l.Relance * 2).ToString(CultureInfo.InvariantCulture);
            var limite = RelancesRsurv.Limite(reglages.Delai, jour);

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
            var fille = await RelancesRsurv.DemandeAsync(geste, l.IdDossier, $"Relance {l.Relance} SDCA{clotures} : {l.Designation}", l.NumeroGestion, lot.Id,
                parametres, l.IdAction, ct);
            if (fille.Refus is not null) return IssueGeste.Refuser(fille.Refus);

            var courrier = await RelancesRsurv.CourrierAsync(geste, fille.Id!.Value, l.IdDossier, l.Designation, "SDCA", reglages, ct);
            if (courrier is not null) courriers.Add(courrier);
            traces.Add(new TraceGeste("dossier", $"le dossier {l.NumeroGestion}", l.IdDossier.ToString(CultureInfo.InvariantCulture), "a modifie",
                new Dictionary<string, string> { ["Relance"] = $"relance {l.Relance - 1} échue", ["Etat relance"] = l.EtatRelance },
                new Dictionary<string, string>
                {
                    ["Relance"] = $"relance {l.Relance} du {FormeLigne.Date(jour)}{(limite is { } d ? $", limite {FormeLigne.Date(d)}" : "")}",
                    ["Etat relance"] = etatRelance, ["Demande"] = fille.Numero!, ["Courrier"] = courrier ?? "aucun : pas de paramètre d'édition",
                }));
        }

        faites.AddRange(lignes.Select(x => x.L.Relance));
        idsFaits.AddRange(lignes.Select(x => x.L.Id.ToString(CultureInfo.InvariantCulture)));
        var message = Message(faites, courriers);
        return IssueGeste.Ecrit(avertissements.Count == 0 ? message : $"{message} {string.Join(" ", avertissements)}", [.. idsFaits], [.. traces]);
    }

    /// <summary>« Relances 2 et 3 faites pour 2 dossiers : 2 courriers « … », « … » en file d'édition. »</summary>
    internal static string Message(IEnumerable<int> relances, IReadOnlyCollection<string> courriers)
    {
        var faites = relances.ToList();
        var numeros = faites.Distinct().Order().Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList();
        var quoi = numeros.Count == 1 ? $"Relance {numeros[0]} faite" : $"Relances {string.Join(", ", numeros[..^1])} et {numeros[^1]} faites";
        var n = faites.Count;
        return $"{quoi} pour {n} dossier{(n > 1 ? "s" : "")} : {RelancesRsurv.Courriers(courriers)}";
    }

    private static SqlParameter Jour(DateTime jour) => new("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = jour };
}
