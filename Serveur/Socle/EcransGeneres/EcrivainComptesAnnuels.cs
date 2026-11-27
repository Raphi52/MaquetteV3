using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Comptes annuels » (PROC_DCA) : « Valider le dépôt » des comptes annuels d'UN exercice, au guichet (etat Validee). Mode d'emploi :
/// EcrivainEcran.cs ; la liste : LecteurComptesAnnuels.cs ; le depot commun : DepotsRcsV2.cs. Plan « RIG v3 100 % fonctionnel », lot 16,
/// 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_DCA (fiche : proc_dca.md, Detail OPE). Chemins relatifs a ce
/// dossier ; RigMetier = D:\RigApplication\Source\Outils\DLL\RigMetier.
///   SAISIES : « Dossier RCS › Numéro de gestion » (etape etpRcs : la DCA porte sur un dossier RCS, MetierBase DossierRCS,
///   PROC_DCA.cs:136-139) ; « Exercices › Date de clôture » (la grille des exercices : un exercice non depose de cette date, sinon la
///   ligne vide ajoutee en fin de grille, PROC_DCA.cs:837, qui devient un exercice neuf) ; « Exercices › Bilan confidentiel » et
///   « Résultat confidentiel » (cases de la grille, ExerciceDca.cs:50-55) ; « Exercices › Date d'arrivée » (ultDateArrivee de
///   OPE_ACTES, dans l'etape des exercices, MetierObligatoire, OPE_ACTES.Designer.cs:104). La date de depot est toujours celle du jour
///   (FT 14088, ultDateDepot = DateTime.Today, OPE_ACTES.cs:68-69).
///   CONTROLES, avant toute ecriture (un refus n'ecrit rien) :
///     0. « Valider le dépôt » n'ecrit qu'un depot accepte : l'etape « Réclamation ou refus » remplie (etpReclamRefus, visible en
///        reclamation seulement, proc_dca.md:250) ou l'etape « Pièces jointes » remplie (pieces du DCADEMAT, proc_dca.md:432) refuse,
///        ces deux suites n'etant pas reprises (plus bas) ; toute autre saisie remplie hors des cinq SAISIES lues refuse aussi
///        (SaisiesLues : jamais un depot valide qui ignore ce que l'agent a saisi) ;
///     1. le numero, la date de cloture et la date d'arrivee sont obligatoires (sans exercice coche : « Vous devez cocher la case « DCA »
///        ou « DCACO »... », ETP_EXERCICES.cs:274-277 ; la V3 n'a pas de case, la date de cloture la remplace) ; une date d'arrivee apres
///        le jour du geste refuse, au mot pres (OPE_ACTES.cs:153-157) ;
///     2. le dossier RCS existe (etpRcs) ; son type RCS a une serie de depot de comptes (ActionRcs.AddDepotRcs, RigMetier
///        ActionRcs\ActionRcs.cs:194-218 : B1..D2 → RDC / B, A1 A2 → RD5 / K, AC → RD6 / G (INPI), I1 I2 → RD7 / J (INPI)) ;
///     3. un exercice DEJA depose a la meme date de cloture refuse le depot, sauf si le dernier DCA de cette date etait confidentiel et
///        que le nouveau ne l'est pas, ou avait le resultat confidentiel et que le nouveau n'a ni bilan ni resultat confidentiel
///        (ETP_EXERCICES.cs:161-180), message au mot pres ;
///     4. un exercice sous injonction de deposer (relance IDCA de sa diligence, Exercice.Injonction, RigMetier Depot\Exercice.cs:268-274) :
///        RIG v2 y traite le retrait du role ou le depot hors delai et sa facture judiciaire (ETP_EXERCICES.cs:548-560, 578-586), que la V3
///        ne reprend pas : refuse, deposer dans RIG v2 ;
///     5. numerotation (RA1, puis la serie du depot) : un numero refuse, rien d'ecrit.
///   AVERTISSEMENTS (non bloquants dans RIG v2), au mot pres dans le message de reussite : un autre exercice non depose a +/- 2 mois
///   (ETP_EXERCICES.cs:187-195) ; une date d'arrivee de plus de trois mois (OPE_ACTES.cs:159-163).
///   CE QUI S'ECRIT (validation, ETP_EXERCICES.cs:365-399 et 517-615 ; DossierRCS.AddDCA, RigMetier _Dossier\DossierRCS.cs:205-258) :
///     - l'exercice : neuf (numero d'ordre 1, type « 1 » pour le premier exercice du dossier sinon « 2 » -- regle 01EREXE, compte des
///       exercices en base avant le geste --, etat DCA « 00 », etat info « 1 »), ou celui de la grille ; dans les deux cas dates limites
///       d'AGO (cloture + delai de tenue) et de depot (+ 2 mois) au dernier jour du mois, depose, plus de demande de reclamation. Delai de
///       tenue (InscritPM.GetDelaiTenueAgo, InscritPM.cs:537-539) : 6 mois pour une personne physique ; pour une personne morale,
///       INSPM_DELAI_TENUE_AGO si sa forme juridique a R_FORMEJURID_DELAI_TENUE_AGO_VARIABLE, sinon 6 ;
///     - le depot (DepotsRcsV2.EcrireAsync) : action du registre « DCA » (etat 2, etat info 2), depot numerote lie a l'exercice, saisie
///       simplifiee, etat VAX « 0101 », dates de mise a jour du jour, confidentialite (ActionRcs.cs:220-245) ; il ANNULE le dernier DCA de
///       l'exercice, qui le REMPLACE (DossierRCS.cs:232-236) ; les pieces BILA (1), CONFI (2) si bilan confidentiel, CONFI (2) et RESUL (3)
///       si resultat confidentiel -- deux CONFI quand les deux sont coches, comme RIG v2 (DossierRCS.cs:243-251) ; l'evenement RCS DCA ;
///       la demande DCA validee ;
///     - la diligence de l'exercice, s'il en a une, close regularisee : date de regularisation, etat « 9 » (ETP_EXERCICES.cs:563-567).
///   NON REPRIS (nommes ; le message le dit) : la facture du depot et le tableau de facturation (etpFactuGestcom), les relances
///   facturees (ETP_EXERCICES.cs:526-544) ; les editions Lot1/Lot2 et la lettre d'avertissement (DossierRCS.cs:298-303) ; l'annonce
///   BODACC d'une personne morale (AddBodaccDCA, DossierRCS.cs:261-264) ; le depot consolide (DCACO) et la presentation simplifiee (pas de
///   case dans la saisie V3) ; le DCA dematerialise (DCADEMAT : il arrive avec son flux INPI ou CREFO) ; la reclamation et le refus
///   (RECLAMATION_REFUS_A_FACTURER : l'etape remplie refuse, controle 0) ; le pre-remplissage de la date d'arrivee
///   (Arrivee.GetDateArriveeForProcessusRCS, RigMetier Demande\Arrivee.cs:77-94 : la date saisie a l'ARRIVEE, sinon celle du dernier DCA
///   valide par l'agent dans la journee, lue dans LIGNE_RAPP_GENE, que la V3 n'ecrit pas) -- l'agent la saisit ; la grille des
///   exercices non deposes proposee, l'exercice coche d'office et la confidentialite reprise du dernier depot (PROC_DCA.cs:837-859 :
///   la saisie V3 n'a pas de grille, l'agent saisit la date de cloture et coche les confidentialites) ; la recherche du dossier par
///   SIREN (MetierTypeNumeros « Gestion=*;Siren=LAST », PROC_DCA.cs:601 : la saisie V3 n'a que le numero de gestion) ; le redepot d'un exercice deja depose depuis la grille des deposes ; l'avertissement « Affaire non
///   regularisee pour l'exercice » (PROC_DCA.cs:1975-1981), qui suppose l'injonction refusee au controle 4.
/// Traces : famille « dossier » -- le depot (« a cree ») et l'exercice (« a cree » ou « a modifie »).
/// </summary>
// fix-ok: mesure le 27/11/2026 (banc-r4-rouge.log, PROC_DCA.gestes blocs 6 a 9) -- la date d'arrivee etait celle du jour, sans
// source (RIG v2 la fait saisir), et les etapes « Réclamation ou refus » et « Pièces jointes », saisissables a l'ecran, n'etaient pas
// lues : un motif de refus donnait un depot valide. Une seule cause, des saisies non lues : les controles 0 et 1 les lisent.
internal sealed class EcrivainComptesAnnuels : IEcrivainEcran
{
    internal const string NomEcran = "Comptes annuels";

    /// <summary>Les etapes et champs du descripteur PROC_DCA (FonctionsRetrouvees.cs), au mot pres.</summary>
    internal const string EtapeDossier = "Dossier RCS", EtapeExercices = "Exercices", EtapePieces = "Pièces jointes",
                          EtapeReclamation = "Réclamation ou refus";

    internal const string ChampNumero = "Numéro de gestion", ChampCloture = "Date de clôture", ChampBilanConfidentiel = "Bilan confidentiel",
                          ChampResultatConfidentiel = "Résultat confidentiel", ChampArrivee = "Date d'arrivée";

    /// <summary>Les refus d'une etape remplie que « Valider le dépôt » n'ecrit pas (controle 0).</summary>
    internal const string ReclamationNonReprise = "« Réclamation ou refus » est rempli : la réclamation et le refus d'un dépôt de comptes ne sont " +
                                                  "pas repris par la V3, à faire dans RIG v2 ; « Valider le dépôt » n'enregistre qu'un dépôt accepté.",
                          PiecesNonReprises = "« Pièces jointes » est rempli : les pièces jointes ne servent qu'au dépôt dématérialisé (INPI, CREFO), " +
                                              "que la V3 ne reprend pas ; au guichet, les pièces déposées suivent les cases de confidentialité.";

    /// <summary>Un champ au moins de l'etape <paramref name="etape"/> est-il saisi ?</summary>
    private static bool Remplie(GesteEnCours geste, string etape) => geste.Demande.Valeurs.Any(v =>
        v.Key.StartsWith(GesteEcran.Cle(etape, ""), StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(v.Value));

    /// <summary>Les seules saisies que « Valider le dépôt » lit (controles 1 a 5). Toute autre saisie remplie refuse le geste (controle 0) :
    /// un champ de l'ecran que l'ecrivain ignorerait donnerait un depot valide qui ne dit pas ce que l'agent a saisi (le « SIREN », la
    /// « Dénomination » et l'« Exercice » de l'ecran, mesure le 27/11/2026 : PROC_DCA.gestes bloc 10, rouge avant ce controle).</summary>
    private static readonly HashSet<string> SaisiesLues =
    [
        GesteEcran.Cle(EtapeDossier, ChampNumero), GesteEcran.Cle(EtapeExercices, ChampCloture),
        GesteEcran.Cle(EtapeExercices, ChampBilanConfidentiel), GesteEcran.Cle(EtapeExercices, ChampResultatConfidentiel),
        GesteEcran.Cle(EtapeExercices, ChampArrivee),
    ];

    internal static string SaisieNonLue(string cle) =>
        $"« {cle} » n'est pas lu par « Valider le dépôt » de la V3 : le dépôt l'ignorerait, rien n'est enregistré.";

    public string Procedure => "PROC_DCA";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Creer];

    /// <summary>Le refus de la meme date de cloture (ETP_EXERCICES.cs:176-177), au mot pres (sans le retour a la ligne final).</summary>
    internal static string MemeCloture(DateTime cloture) =>
        $"Il existe un exercice déposé à la même date de clôture que l'exercice non déposé clos le {cloture:dd/MM/yyyy}.";

    private sealed record ExerciceLu(int Id, DateTime Cloture, bool Depose, int? IdDiligence);

    private static bool Coche(string valeur) => valeur is "true" or "1" or "oui" or "Oui";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        string S(string etape, string champ) => Saisi(geste, etape, champ);
        // 0. Les etapes d'une reclamation, d'un refus ou d'un depot dematerialise : pas un depot accepte.
        if (Remplie(geste, EtapeReclamation)) return IssueGeste.Refuser(ReclamationNonReprise);
        if (Remplie(geste, EtapePieces)) return IssueGeste.Refuser(PiecesNonReprises);
        if (geste.Demande.Valeurs.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v.Value) && !SaisiesLues.Contains(v.Key)) is { Key: { } nonLue })
            return IssueGeste.Refuser(SaisieNonLue(nonLue));
        // 1. Les saisies obligatoires.
        if (S(EtapeDossier, ChampNumero).Length == 0) return IssueGeste.Refuser(Obligatoire($"{EtapeDossier} › {ChampNumero}"));
        if (S(EtapeExercices, ChampCloture).Length == 0) return IssueGeste.Refuser(Obligatoire($"{EtapeExercices} › {ChampCloture}"));
        if (Date(S(EtapeExercices, ChampCloture)) is not { } cloture)
            return IssueGeste.Refuser($"« {EtapeExercices} › {ChampCloture} » : date illisible ({S(EtapeExercices, ChampCloture)}).");
        if (S(EtapeExercices, ChampArrivee).Length == 0) return IssueGeste.Refuser(Obligatoire($"{EtapeExercices} › {ChampArrivee}"));
        if (Date(S(EtapeExercices, ChampArrivee)) is not { } arrivee)
            return IssueGeste.Refuser($"« {EtapeExercices} › {ChampArrivee} » : date illisible ({S(EtapeExercices, ChampArrivee)}).");
        if (arrivee.Date > geste.Maintenant.Date) return IssueGeste.Refuser("La date d'arrivée doit être antérieure ou égale à la date du jour.");
        var bilanConfidentiel = Coche(S(EtapeExercices, ChampBilanConfidentiel));
        var resultatConfidentiel = Coche(S(EtapeExercices, ChampResultatConfidentiel));

        // 2. Le dossier RCS et sa serie de depot de comptes.
        var numero = S(EtapeDossier, ChampNumero).Replace(" ", "", StringComparison.Ordinal);
        if (await DepotsRcsV2.TrouverAsync(geste, numero, ct) is not { EstDossier: true } dossier)
            return IssueGeste.Refuser($"Aucun dossier RCS ne porte le numéro « {numero} » dans la base du greffe.");
        // fix-ok: relu avant compilation -- « is not var (a, b, c) » ne refuse jamais (le motif var accepte tout, null compris) : le
        // type RCS sans serie aurait plante plus loin ; « is not { } » refuse bien le null de Numerotation.
        if (Numerotation(dossier.TypeRcs) is not { } numerotation)
            return IssueGeste.Refuser($"Le type RCS « {dossier.TypeRcs} » du dossier {dossier.Numero} n'a pas de numérotation de dépôt des comptes annuels dans RIG v2.");
        var (serie, categorie, inpi) = numerotation;

        // Les exercices du dossier, verrouilles pour le geste.
        var exercices = new List<ExerciceLu>();
        await using (var r = await geste.LireAsync("""
            SELECT e.EXRCC_ID_EXRCC, e.EXRCC_DATE_CLOTURE, CONVERT(bit, ISNULL(e.EXRCC_DEPOSE, 0)) AS Depose, e.EXRCC_ID_DLGRC
            FROM v2.EXERCICE AS e WITH (UPDLOCK) WHERE e.EXRCC_ID_DSRCS = @d ORDER BY e.EXRCC_ID_EXRCC
            """, ct, new SqlParameter("@d", dossier.Id)))
            while (await r.ReadAsync(ct))
                exercices.Add(new ExerciceLu(r.GetInt32(0), r.GetDateTime(1).Date, r["Depose"] is true, r["EXRCC_ID_DLGRC"] is DBNull ? null : Convert.ToInt32(r["EXRCC_ID_DLGRC"], CultureInfo.InvariantCulture)));
        var cible = exercices.FirstOrDefault(e => !e.Depose && e.Cloture == cloture.Date);

        // 3. Un exercice deja depose a la meme date (ETP_EXERCICES.cs:161-180).
        var deposes = exercices.Where(e => e.Depose && e.Cloture == cloture.Date).Select(e => e.Id).ToArray();
        if (deposes.Length > 0 && await DernierDcaAsync(geste, deposes, ct) is { } dernier
            && !((dernier.Confidentiel && !bilanConfidentiel) || (dernier.ResultatConfidentiel && !bilanConfidentiel && !resultatConfidentiel)))
            return IssueGeste.Refuser(MemeCloture(cloture));

        // 4. L'injonction de deposer.
        if (cible?.IdDiligence is { } diligence && await ExisteAsync(geste,
                "SELECT 1 FROM v2.RELANCE_RCS WHERE RLCRC_ID_DLGRC = @d AND RLCRC_TYPE_RELANCE = 'IDCA'", diligence, ct))
            return IssueGeste.Refuser($"L'exercice clos le {cloture:dd/MM/yyyy} est sous injonction de déposer : RIG v2 traite au dépôt le retrait du rôle " +
                                      "ou le dépôt hors délai et sa facture, que la V3 ne reprend pas ; déposer dans RIG v2.");

        // L'avertissement +/- 2 mois (ETP_EXERCICES.cs:187-195), au mot pres.
        var voisin = exercices.FirstOrDefault(e => !e.Depose && e != cible
            && ((e.Cloture < cloture && e.Cloture.AddDays(60) > cloture) || (e.Cloture > cloture && e.Cloture.AddDays(-60) < cloture)));

        // L'exercice : dates limites (DossierRCS.cs:212-216, ETP_EXERCICES.cs:390-393).
        var delai = await DelaiTenueAgoAsync(geste, dossier, ct);
        var limiteAgo = DernierJour(cloture.AddMonths(delai));
        var limiteDepot = DernierJour(cloture.AddMonths(delai + 2));
        int idExercice;
        if (cible is null)
            idExercice = await geste.InsererAsync("v2.EXERCICE", "EXRCC_ID_EXRCC", new Dictionary<string, object?>
            {
                ["EXRCC_ID_DSRCS"] = dossier.Id, ["EXRCC_NUM_ORDRE"] = (short)1, ["EXRCC_TYPE_EXERCICE"] = exercices.Count == 0 ? "1" : "2",
                ["EXRCC_DATE_CLOTURE"] = cloture, ["EXRCC_DATE_LIMITE_AGO"] = limiteAgo, ["EXRCC_LIMITE_DEPOT"] = limiteDepot,
                ["EXRCC_DEPOSE"] = true, ["EXRCC_ETAT_DCA"] = "00", ["EXRCC_ETAT_INFO"] = "1",
            }, ct);
        else
        {
            idExercice = cible.Id;
            await geste.EcrireAsync("""
                UPDATE v2.EXERCICE SET EXRCC_DATE_LIMITE_AGO = @ago, EXRCC_LIMITE_DEPOT = @depot, EXRCC_DEPOSE = 1, EXRCC_ID_DMND_RECLA = NULL
                WHERE EXRCC_ID_EXRCC = @id
                """, ct, new SqlParameter("@ago", limiteAgo), new SqlParameter("@depot", limiteDepot), new SqlParameter("@id", cible.Id));
        }

        // Le dernier DCA de l'exercice, que le nouveau annule (DossierRCS.cs:224-236).
        var precedent = cible is null ? null : await DernierDcaAsync(geste, [cible.Id], ct);
        var jour = geste.Maintenant.Date;
        var enPlus = new Dictionary<string, object?>
        {
            ["DPTRC_ID_EXRCC"] = idExercice, ["DPTRC_DATE_MAJ_INTERG"] = jour, ["DPTRC_DATE_MAJ_INFOG"] = jour,
            ["DPTRC_DATE_MODIF_FLAG_TRANS"] = geste.Maintenant, ["DPTRC_DIFFUSABILITE_CONFIDENTIELLE"] = bilanConfidentiel,
            ["DPTRC_RESULTAT_CONFIDENTIEL"] = resultatConfidentiel,
        };
        if (precedent is not null) enPlus["DPTRC_NUM_DEPOT_ANNULE"] = precedent.Numero;
        var pieces = new List<DepotsRcsV2.PieceDeposee> { new("BILA", 1, true) };
        if (bilanConfidentiel) pieces.Add(new("CONFI", 2, true));
        if (resultatConfidentiel) { pieces.Add(new("CONFI", 2, true)); pieces.Add(new("RESUL", 3, false)); }

        var (depot, refus) = await DepotsRcsV2.EcrireAsync(geste, new DepotsRcsV2.DepotSaisi("DCA", dossier, arrivee.Date, jour, categorie, serie, inpi,
            Gratuit: false, SaisieSimplifiee: true, EtatVax: "0101", "DCA", DateEffet: null, TexteEvenement: null, "BILA", DateActe: null,
            DescriptionActe: null) { DepotEnPlus = enPlus, Pieces = pieces }, ct);
        if (depot is null) return IssueGeste.Refuser(refus!);
        if (precedent is not null)
            await geste.EcrireAsync("UPDATE v2.DEPOT_RCS SET DPTRC_NUM_DEPOT_REMPLACE = @n WHERE DPTRC_ID_DPTRC = @id", ct,
                new SqlParameter("@n", depot.NumeroDepot), new SqlParameter("@id", precedent.Id));
        // La diligence close regularisee (ETP_EXERCICES.cs:563-567).
        if (cible?.IdDiligence is { } aRegulariser)
            await geste.EcrireAsync("UPDATE v2.DILIGENCE_RCS SET DLGRC_DATE_REGULARISATION = @m, DLGRC_ETAT_DILIGENCE = '9' WHERE DLGRC_ID_DLGRC = @id", ct,
                new SqlParameter("@m", geste.Maintenant), new SqlParameter("@id", aRegulariser));

        var objetExercice = $"l'exercice clos le {cloture:dd/MM/yyyy} du dossier {FormeLigne.NumeroGestion(dossier.Numero)}";
        var message = $"Dépôt des comptes annuels {depot.NumeroDepot} enregistré dans la base du greffe pour {dossier.Designation} " +
                      $"({FormeLigne.NumeroGestion(dossier.Numero)}), exercice clos le {cloture:dd/MM/yyyy}, arrivée {depot.NumeroArrivee} ; " +
                      "la facture, les éditions et l'annonce BODACC ne sont pas reprises par la V3.";
        if (voisin is not null) message += $" Attention : Il existe un exercice non déposé à +/- 2 mois ({voisin.Cloture:dd/MM/yyyy})";
        if (arrivee.Date < geste.Maintenant.Date.AddMonths(-3)) message += " Attention : Veuillez contrôler la date d'arrivée qui semble erronée.";
        return IssueGeste.Ecrit(message, [depot.IdDemande.ToString(CultureInfo.InvariantCulture)],
            DepotsRcsV2.Trace("dépôt des comptes annuels", depot, dossier, "DCA"),
            new TraceGeste("dossier", objetExercice, idExercice.ToString(CultureInfo.InvariantCulture), cible is null ? "a cree" : "a modifie",
                cible is null ? null : new Dictionary<string, string> { ["Déposé"] = "non" },
                new Dictionary<string, string>
                {
                    ["Déposé"] = "oui", ["Limite d'AGO"] = limiteAgo.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    ["Limite de dépôt"] = limiteDepot.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                }));
    }

    /// <summary>La serie, la categorie et l'envoi INPI d'un depot de comptes (ActionRcs.cs:194-218) ; null pour un autre type RCS.</summary>
    internal static (string Serie, string Categorie, bool Inpi)? Numerotation(string typeRcs) => typeRcs switch
    {
        "B1" or "C1" or "D1" or "B2" or "C2" or "D2" => ("RDC", "B", false),
        "A1" or "A2" => ("RD5", "K", false),
        "AC" => ("RD6", "G", true),
        "I1" or "I2" => ("RD7", "J", true),
        _ => null,
    };

    private sealed record DepotLu(int Id, string Numero, bool Confidentiel, bool ResultatConfidentiel);

    /// <summary>Le dernier DCA des exercices <paramref name="ids"/> (Exercice.DernierDepotEvenement, Exercice.cs:429-438) : par date de
    /// depot puis identifiant decroissants.</summary>
    private static async Task<DepotLu?> DernierDcaAsync(GesteEnCours geste, int[] ids, CancellationToken ct)
    {
        var parametres = ids.Select((id, i) => new SqlParameter($"@e{i}", id)).ToArray();
        await using var r = await geste.LireAsync($"""
            SELECT TOP (1) p.DPTRC_ID_DPTRC, p.DPTRC_NUM_DEPOT,
                   CONVERT(bit, ISNULL(p.DPTRC_DIFFUSABILITE_CONFIDENTIELLE, 0)) AS Confidentiel,
                   CONVERT(bit, ISNULL(p.DPTRC_RESULTAT_CONFIDENTIEL, 0)) AS Resultat
            FROM v2.DEPOT_RCS AS p
            JOIN rec.ActionRegistre AS a ON a.IdDepot = p.DPTRC_ID_DPTRC
            JOIN v2.EVENEMENT_RCS AS ev ON ev.EVNRC_ID_ACTRC = a.Id AND ev.EVNRC_CODE_EVENEMENT_RCS = 'DCA'
            WHERE p.DPTRC_ID_EXRCC IN ({string.Join(", ", parametres.Select(p => p.ParameterName))})
            ORDER BY p.DPTRC_DATE DESC, p.DPTRC_ID_DPTRC DESC
            """, ct, parametres);
        return await r.ReadAsync(ct)
            ? new DepotLu(r.GetInt32(0), FormeLigne.Texte(r, "DPTRC_NUM_DEPOT"), r["Confidentiel"] is true, r["Resultat"] is true)
            : null;
    }

    /// <summary>Le delai de tenue de l'AGO en mois (InscritPM.GetDelaiTenueAgo, InscritPM.cs:537-539 ; 6 pour une personne physique,
    /// AddDCA, DossierRCS.cs:206).</summary>
    private static async Task<int> DelaiTenueAgoAsync(GesteEnCours geste, DepotsRcsV2.EntiteRcs dossier, CancellationToken ct)
    {
        if (dossier.PersonnePhysique) return 6;
        await using var r = await geste.LireAsync("""
            SELECT TOP (1) pm.INSPM_DELAI_TENUE_AGO, CONVERT(bit, ISNULL(fj.R_FORMEJURID_DELAI_TENUE_AGO_VARIABLE, 0)) AS Variable
            FROM v2.INSCRIT_PM AS pm LEFT JOIN v2.FORME_JURIDIQUE_UTI AS fj ON fj.R_FORMEJURID_VALEUR = pm.INSPM_FORME_JURIDIQUE_UTI
            WHERE pm.INSPM_ID_DSRCS = @d ORDER BY pm.INSPM_ID_INSPM
            """, ct, new SqlParameter("@d", dossier.Id));
        return await r.ReadAsync(ct) && r["Variable"] is true && r["INSPM_DELAI_TENUE_AGO"] is not DBNull
            ? Convert.ToInt32(r["INSPM_DELAI_TENUE_AGO"], CultureInfo.InvariantCulture)
            : 6;
    }

    private static async Task<bool> ExisteAsync(GesteEnCours geste, string sql, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(sql, ct, new SqlParameter("@d", id));
        return await r.ReadAsync(ct);
    }

    /// <summary>GetLastDayOfMonth de RIG v2 : le dernier jour du mois de <paramref name="d"/>.</summary>
    private static DateTime DernierJour(DateTime d) => new DateTime(d.Year, d.Month, 1).AddMonths(1).AddDays(-1);
}
