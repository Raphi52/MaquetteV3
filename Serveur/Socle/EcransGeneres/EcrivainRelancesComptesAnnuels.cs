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

        // 2. et 3. Chaque diligence : relance due, et pas deja faite.
        var lignes = new List<(Ligne L, List<(int Id, DateTime Cloture)> Exercices)>();
        foreach (var id in ids)
        {
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
        var lot = await RelancesRsurv.DemandeAsync(geste, null, $"Relance des DCA - {lignes.Count} occurence(s)", null, null, null, null, ct);
        if (lot.Refus is not null) return IssueGeste.Refuser(lot.Refus);

        var traces = new List<TraceGeste>();
        var courriers = new List<string>();
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
        return $"{quoi} pour {n} dossier{(n > 1 ? "s" : "")} : {RelancesRsurv.Courriers(courriers)}";
    }

    private static SqlParameter Jour(DateTime jour) => new("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = jour };
}
