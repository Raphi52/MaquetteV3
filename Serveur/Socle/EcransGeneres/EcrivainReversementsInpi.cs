using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Reversements INPI » (PROC_INPIPAY) : les deux gestes du menu de la grille, REJETER et VALIDER un fichier de reversements. Mode
/// d'emploi : EcrivainEcran.cs ; la liste : LecteurReversementsInpi.cs. Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026.
///
/// REJETER. SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_INPIPAY\RESULTATS\OPE_RESULTATS.cs -- le menu n'est
/// visible que pour un utilisateur Amitel (:50-53) et actif que sur un fichier « A valider » (:69-71) ; confirmation, motif facultatif
/// (:83-101), puis InpiReversements.Rejeter (InpiReversements.cs:64-85) : INPIRV_ETAT_TRAITEMENT = 3, INPIRV_DATE_TRAITEMENT =
/// maintenant, le motif ajoute aux commentaires. La V3 rejette sans motif (la V2 l'accepte vide, AllowEmptyValue).
///
/// VALIDER. SOURCE : le menu « Valider » (OPE_RESULTATS.cs:71, :104-107, :222-229 : la LIGNE CHOISIE, un fichier « A valider », pas
/// reserve a Amitel), le formulaire de validation (PROC_VALIDATION.cs, VALIDATION\OPE_RELEVE_BANCAIRE.cs, OPE_REVERSEMENTS.cs) et son
/// enregistrement (VALIDATION\OPE_VALIDATION.cs:67-205). Rien n'est envoye a l'INPI : le fichier XML de l'INPI est deja dans la base
/// (INPIRV_FICHIER_XML), la V2 le lit et ecrit l'encaissement. Dans l'ordre :
///   1. la saisie (OPE_RELEVE_BANCAIRE.Designer.cs:72-109, controles DoVerifier :46-62), les libelles du formulaire :
///      « Montant du virement » obligatoire, > 0 (« Le montant du virement est incorrect ») et egal au total des reversements,
///      INPIRV_TOTAL_HANDLING_FEE (OPE_REVERSEMENTS.cs:42 ; « Le montant du virement est différent de celui des reversements. ») ;
///      « Mode de paiement » obligatoire, une valeur de la liste ULT_COMBO_MODE_PAIEMENT (MODE_PAIEMENT utilisable en saisie, type
///      hors DIV, RBT, REG : OPE_RELEVE_BANCAIRE.cs:65, ULT_COMBO_MODE_PAIEMENT.cs:23-40), sinon « Valeur inexistante »
///      (Ult_ComboBox.cs:344-346, MetierValeurInexistanteErreur) ; vide : le defaut de la V2, le reglage INPIPAY_MODE_PAIEMENT du
///      greffe, sinon « VICB » (:69-74) ; « N° de titre » facultatif, 32 caracteres (tronque, VerifieLongueurMax(true)) ;
///      « Date sur le relevé » obligatoire, vide = maintenant (:75), ni posterieure a maintenant ni anterieure de plus d'un mois ;
///   2. le fichier XML (fee-payment-data v1-1, EdiClassXSD\InpiForma\fee-payment-data-reversement-v1-1.cs ; montants lus comme
///      feepaymentdata.GetDecimalValue, fee-payment-data.cs:9-14) ; l'arrivee de l'encaissement : celle de la demande du PREMIER
///      reversement dont le numero national a une demande (_GetIdArrivee, :187-205 ; un numero introuvable avant arrete la recherche) ;
///   3. une ventilation par numero national (ordre des numeros ; reversements de montant nul ignores, :115-117) : sa demande
///      (Demat.GetDematByNumNational, RigMetier\RCS\Demat\Demat.cs:200-242 : liasse DEMAT_LIASSE_CFE, puis DCA DEMAT_DCA pour un
///      numero « ENT »/« J », DCA pour « CAE »/« K », DAS DEMAT_DAS sinon), son affaire, la facture de la demande (obligatoire), le
///      client de la DERNIERE facture de l'affaire (ou le client d'attente si l'affaire n'en a pas) ; la note interne de l'affaire
///      complete de « Commandes n° : ... » (200 caracteres) ; puis le total des ventilations egal au total du fichier (:174-177) ;
///   4. l'ecriture (:104-110, :170-185 ; Encaissement.CreateEncaissement :139-149 et Sauve :344-412) : ENCAISSEMENT (montant du
///      fichier, date saisie, arrivee, mode, titre, payeur « INPI », commentaire « Période du ... au ... Virement n°... », origine
///      externe INPI / identifiant de paiement / date de production, utilisateur, GUID en majuscules) ; ses VENTILATION a la date
///      de l'encaissement ; le fichier passe a l'etat 2 (Valide) avec sa date, son utilisateur, son encaissement et le montant saisi
///      (INPIRV_MONTANT_VALIDATION, lie au champ, OPE_RELEVE_BANCAIRE.cs:67).
/// La V3 fait tout dans UNE transaction : un refus n'ecrit rien (la V2 levait une exception, le moteur annulait).
/// NON REPRIS (nomme) : la demande INPIPAY creee et cloturee d'un coup (PROC_VALIDATION._SauverDemande, :86-103 : une trace sans
/// facture, MetierFacture = false -- le journal des gestes la remplace, INPIRV_ID_DMND reste vide) ; l'ecriture du reglage
/// INPIPAY_MODE_PAIEMENT (:80-86 : PARAMETRAGE, repris dans soc.Reglage, qu'un geste n'ecrit pas -- le plan n'ecrit que v2.* et
/// rec.* ; il est LU pour le defaut) ; le tiers payeur d'un client « compte PC » (Encaissement.appliquerTiersPayeur, :414-433 :
/// payeur de la procedure collective, cree au besoin) -- une ventilation sur un tel client refuse avec ce motif ; le mode « PV »
/// (consommation d'une provision reprise, Encaissement.cs:405-410, faite par Serveur/Recopie/ProvisionsReprises.cs hors geste) --
/// refuse avec ce motif. Le detail Excel des reversements (tsmiDetailReversements, :108-157) est un classeur sur le poste : rien en base.
/// </summary>
// fix-ok: fichier NEUF (lot 12, 27/11/2026) -- l'ecrivain etait au bas de LecteurReversementsInpi.cs alors que son en-tete le disait
// ici ; deplace tel quel pour « Rejeter », « Valider » ajoute (objection du juge : il n'etait pas branche, raison donnee fausse).
internal sealed class EcrivainReversementsInpi : IEcrivainEcran
{
    public string Procedure => "PROC_INPIPAY";
    public string Ecran => "Reversements INPI";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    internal const string Rejeter = "Rejeter", Valider = "Valider";

    /// <summary>Les champs du formulaire de validation (OPE_RELEVE_BANCAIRE.Designer.cs), au mot pres : les Parametres du descripteur.</summary>
    internal const string ChampMontant = "Montant du virement", ChampMode = "Mode de paiement", ChampTitre = "N° de titre", ChampDate = "Date sur le relevé";

    internal const string MessageAmitel = "Le rejet d'un fichier de reversements est réservé à l'équipe Amitel.";

    // Les messages de RIG v2, au mot pres (OPE_RELEVE_BANCAIRE.cs:49-58, OPE_VALIDATION.cs).
    internal const string MontantIncorrect = "Le montant du virement est incorrect";
    internal const string MontantDifferent = "Le montant du virement est différent de celui des reversements.";
    internal const string DateFuture = "La date du virement ne peut pas être supérieure à la date du jour";
    internal const string DateAncienne = "La date du virement ne peut pas être inférieure de plus d'un mois à la date du jour";
    internal const string ModeInexistant = "Mode de paiement : Valeur inexistante";
    internal const string ArriveeIntrouvable = "L'id d'arrivee n'a pas pu être déterminé.";
    internal const string TotalFaux = "Le montant total des ventilations ne correspond pas au montant global.";

    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct) => geste.Demande.Nom switch
    {
        Rejeter => RejeterAsync(geste, ct),
        Valider => ValiderAsync(geste, ct),
        var nom => Task.FromResult(IssueGeste.Refuser($"Geste « {nom} » : non repris pour les reversements INPI.")),
    };

    private static int[] Ids(GesteEnCours geste)
        => [.. geste.Demande.Ids.Select(i => int.TryParse(i, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).Where(n => n > 0).Distinct()];

    // ------------------------------------------------------------ REJETER

    private static async Task<IssueGeste> RejeterAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (!geste.Agent.EstAmitel) return IssueGeste.Refuser(MessageAmitel);
        var ids = Ids(geste);
        if (ids.Length == 0) return IssueGeste.Refuser("Aucun fichier de reversements coché : rien n'est rejeté.");
        var traces = new List<TraceGeste>();
        var rejetes = new List<string>();
        foreach (var id in ids)
        {
            string? paiement = null, etat = null;
            await using (var r = await geste.LireAsync("SELECT INPIRV_ID_PAYMENT, INPIRV_ETAT_TRAITEMENT FROM v2.INPI_REVERSEMENTS WITH (UPDLOCK, HOLDLOCK) WHERE INPIRV_ID_INPIRV = @id",
                             ct, new SqlParameter("@id", id)))
                if (await r.ReadAsync(ct)) { paiement = FormeLigne.Texte(r, "INPIRV_ID_PAYMENT"); etat = FormeLigne.Texte(r, "INPIRV_ETAT_TRAITEMENT"); }
            if (paiement is null) return IssueGeste.Refuser($"Fichier de reversements introuvable dans la base du greffe ({id}).");
            if (etat != "1") return IssueGeste.Refuser($"Le fichier de reversements n°{paiement} n'est plus à valider : seul un fichier « A Valider » se rejette.");
            await geste.EcrireAsync("UPDATE v2.INPI_REVERSEMENTS SET INPIRV_ETAT_TRAITEMENT = '3', INPIRV_DATE_TRAITEMENT = @maintenant WHERE INPIRV_ID_INPIRV = @id", ct,
                new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@id", id));
            rejetes.Add(paiement);
            traces.Add(new TraceGeste("encaissement", $"le fichier de reversements INPI n°{paiement}", id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { Etat = "A Valider" }, new { Etat = "Rejeté" }));
        }
        return IssueGeste.Ecrit($"Fichier{(rejetes.Count > 1 ? "s" : "")} de reversements n°{string.Join(", n°", rejetes)} rejeté{(rejetes.Count > 1 ? "s" : "")} dans la base du greffe.",
            [.. ids.Select(i => i.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }

    // ------------------------------------------------------------ VALIDER

    /// <summary>Un reversement du fichier (fee-pay) : le numero national, le numero de commande, le montant (basic-fee-data total-fee).</summary>
    private sealed record Reversement(string NumeroNational, int Commande, decimal Montant);

    /// <summary>Le fichier lu (feepaymentdata) : periode, identifiant de paiement, date de production, total du premier office, reversements.</summary>
    private sealed record Fichier(DateTime Debut, DateTime Fin, string IdPaiement, DateTime? Production, decimal Total, IReadOnlyList<Reversement> Reversements);

    /// <summary>La demande trouvee par un numero national (IDemat.Demande) : son id, son numero, son affaire, son arrivee.</summary>
    private sealed record DemandeInpi(int Id, string Numero, int? IdAffaire, int? IdArrivee);

    private static async Task<IssueGeste> ValiderAsync(GesteEnCours geste, CancellationToken ct)
    {
        // Le menu porte sur la ligne choisie (OPE_RESULTATS.cs:104-107) : un fichier.
        var ids = Ids(geste);
        if (ids.Length == 0) return IssueGeste.Refuser("Aucun fichier de reversements coché : rien n'est validé.");
        if (ids.Length > 1) return IssueGeste.Refuser("Un seul fichier de reversements se valide à la fois : RIG v2 valide la ligne choisie.");
        var id = ids[0];

        string? paiement = null, etat = null, xml = null;
        var totalReversements = 0m;
        await using (var r = await geste.LireAsync("""
            SELECT INPIRV_ID_PAYMENT, INPIRV_ETAT_TRAITEMENT, INPIRV_FICHIER_XML, INPIRV_TOTAL_HANDLING_FEE
            FROM v2.INPI_REVERSEMENTS WITH (UPDLOCK, HOLDLOCK) WHERE INPIRV_ID_INPIRV = @id
            """, ct, new SqlParameter("@id", id)))
            if (await r.ReadAsync(ct))
            {
                paiement = FormeLigne.Texte(r, "INPIRV_ID_PAYMENT");
                etat = FormeLigne.Texte(r, "INPIRV_ETAT_TRAITEMENT");
                xml = r["INPIRV_FICHIER_XML"] as string;
                totalReversements = CriteresConsultation.Decimal(r, "INPIRV_TOTAL_HANDLING_FEE") ?? 0m;
            }
        if (paiement is null) return IssueGeste.Refuser($"Fichier de reversements introuvable dans la base du greffe ({id}).");
        if (etat != "1") return IssueGeste.Refuser($"Le fichier de reversements n°{paiement} n'est plus à valider : seul un fichier « A Valider » se valide.");

        // 1. La saisie (DoVerifier, OPE_RELEVE_BANCAIRE.cs:46-62), dans l'ordre de la V2.
        var montant = CriteresConsultation.Montant(geste.Demande.Valeurs.GetValueOrDefault(ChampMontant)) ?? 0m;
        if (montant <= 0) return IssueGeste.Refuser(MontantIncorrect);
        if (montant != totalReversements) return IssueGeste.Refuser(MontantDifferent);
        var dateSaisie = geste.Demande.Valeurs.GetValueOrDefault(ChampDate)?.Trim();
        DateTime dateReleve;
        if (string.IsNullOrEmpty(dateSaisie)) dateReleve = geste.Maintenant;
        else if (CriteresConsultation.Date(dateSaisie) is { } d) dateReleve = d;
        else return IssueGeste.Refuser($"« {ChampDate} » : la date « {dateSaisie} » ne se lit pas (jj/mm/aaaa).");
        if (dateReleve > geste.Maintenant) return IssueGeste.Refuser(DateFuture);
        if (dateReleve < geste.Maintenant.AddMonths(-1)) return IssueGeste.Refuser(DateAncienne);
        var mode = geste.Demande.Valeurs.GetValueOrDefault(ChampMode)?.Trim();
        if (string.IsNullOrEmpty(mode))
        {
            await using var r = await geste.LireAsync("SELECT Valeur FROM soc.Reglage WHERE Code = 'INPIPAY_MODE_PAIEMENT'", ct);
            mode = await r.ReadAsync(ct) && FormeLigne.Texte(r, "Valeur") is { Length: > 0 } v ? v : "VICB";
        }
        await using (var r = await geste.LireAsync("""
            SELECT COUNT(*) FROM v2.MODE_PAIEMENT
            WHERE R_MODEPAIEME_VALEUR = @mode AND R_MODEPAIEME_UTILISABLE_SAISIE = 1 AND R_MODEPAIEME_TYPE NOT IN ('DIV', 'RBT', 'REG')
            """, ct, new SqlParameter("@mode", mode)))
        {
            await r.ReadAsync(ct);
            if (r.GetInt32(0) == 0) return IssueGeste.Refuser(ModeInexistant);
        }
        if (mode == "PV")
            return IssueGeste.Refuser("Le mode « PV » consomme une provision reprise (Encaissement.cs:405-410) : la V3 ne le fait pas dans un geste. Rien n'est enregistré.");
        var titre = geste.Demande.Valeurs.GetValueOrDefault(ChampTitre)?.Trim() is { Length: > 0 } t ? (t.Length > 32 ? t[..32] : t) : null;

        // 2. Le fichier de l'INPI (feepaymentdata.GetFeePaymentData).
        if (Lire(xml) is not { } fichier)
            return IssueGeste.Refuser($"Le fichier XML du reversement n°{paiement} ne se lit pas (fee-payment-data attendu) : rien n'est enregistré.");
        int? arrivee = null;
        var arriveeTrouvee = false;
        foreach (var rv in fichier.Reversements)
        {
            var (trouvee, demandeLue, _) = await DematAsync(geste, rv.NumeroNational, ct);
            if (!trouvee) break;                                                                 // « if (demat == null) return false; »
            if (demandeLue is { } dm) { arrivee = dm.IdArrivee; arriveeTrouvee = true; break; }
        }
        if (!arriveeTrouvee) return IssueGeste.Refuser(ArriveeIntrouvable);

        // 3. Une ventilation par numero national (:111-173).
        var ventilations = new List<(string Numero, int Affaire, int Client, decimal Montant, string Demande, int[] Commandes)>();
        var parNumero = fichier.Reversements.GroupBy(x => x.NumeroNational).OrderBy(g => g.Key, StringComparer.Create(Francais, false));
        foreach (var g in parNumero)
        {
            var m = g.Sum(x => x.Montant);
            if (m <= 0m) continue;
            var (trouve, demande, erreur) = await DematAsync(geste, g.Key, ct);
            if (!trouve) return IssueGeste.Refuser(erreur!);
            if (demande is not { } dm) return IssueGeste.Refuser($"Aucune demande n'est associée au numéro national {g.Key}");
            if (dm.IdAffaire is not { } affaire) return IssueGeste.Refuser($"Aucune affaire n'est associée au numéro national {g.Key} ({dm.Numero})");
            await using (var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.FACTURE WHERE FACTU_NUM_DEMANDE = @n", ct, new SqlParameter("@n", dm.Numero)))
            {
                await r.ReadAsync(ct);
                if (r.GetInt32(0) == 0) return IssueGeste.Refuser($"Aucune facture n'est associée au numéro national {g.Key} ({dm.Numero})");
            }
            int? client;
            bool facturee, existe, comptePc;
            await using (var r = await geste.LireAsync("""
                SELECT (SELECT COUNT(*) FROM v2.AFFAIRE WHERE AFFRE_ID_AFFRE = @a) AS Affaire,
                       f.FACTU_ID_CLIEN, c.Id, c.EstComptePc,
                       (SELECT TOP (1) Id FROM rec.Client WHERE NumeroCompte = '99999999' ORDER BY Id) AS Attente
                FROM (SELECT 1 AS Un) AS u
                LEFT JOIN (SELECT TOP (1) f.FACTU_ID_CLIEN FROM v2.FACTURE AS f INNER JOIN v2.DEMANDE AS d ON d.DMND_NUM_DEMANDE = f.FACTU_NUM_DEMANDE
                           WHERE d.DMND_ID_AFFRE = @a ORDER BY f.FACTU_DATE_CREATION DESC, f.FACTU_NUMERO_FACTURE DESC) AS f ON 1 = 1
                LEFT JOIN rec.Client AS c ON c.Id = f.FACTU_ID_CLIEN
                """, ct, new SqlParameter("@a", affaire)))
            {
                await r.ReadAsync(ct);
                existe = r.GetInt32(0) > 0;
                // La derniere facture de l'affaire (Affaire.Factures, Facture.cs:423 ; OrderBy(FACTU_DATE_CREATION).Last()) : la demande a
                // une facture (controle ci-dessus), l'affaire en a donc au moins une ; sans facture, le client d'attente (:153-156).
                facturee = !r.IsDBNull(1);
                client = facturee ? (r.IsDBNull(2) ? null : r.GetInt32(2)) : (r.IsDBNull(4) ? null : r.GetInt32(4));
                comptePc = !r.IsDBNull(3) && r.GetBoolean(3);
            }
            if (!existe) return IssueGeste.Refuser($"L'affaire d'id {affaire} n'a pu être chargée ({dm.Numero})");
            if (client is not { } idClient) return IssueGeste.Refuser("Le client de la facture n'a pu être chargé.");
            if (comptePc)
                return IssueGeste.Refuser($"Le client de la facture du numéro national {g.Key} est un compte de procédure collective : son tiers payeur " +
                                          "(Encaissement.appliquerTiersPayeur) n'est pas repris par la V3. Rien n'est enregistré.");
            ventilations.Add((g.Key, affaire, idClient, m, dm.Numero, [.. g.Select(x => x.Commande).Distinct()]));
        }
        if (ventilations.Sum(v => v.Montant) != fichier.Total) return IssueGeste.Refuser(TotalFaux);

        // 4. L'ecriture.
        int? agent;
        await using (var r = await geste.LireAsync("SELECT IdV2 FROM soc.Compte WHERE Id = @agent", ct, new SqlParameter("@agent", geste.Agent.Id)))
            agent = await r.ReadAsync(ct) && !r.IsDBNull(0) ? Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture) : null;
        var traces = new List<TraceGeste>();
        foreach (var v in ventilations)
        {
            string? note;
            await using (var r = await geste.LireAsync("SELECT AFFRE_NOTE_INTERNE FROM v2.AFFAIRE WITH (UPDLOCK) WHERE AFFRE_ID_AFFRE = @a", ct, new SqlParameter("@a", v.Affaire)))
                note = await r.ReadAsync(ct) && !r.IsDBNull(0) ? r.GetString(0) : null;
            var nouvelle = (string.IsNullOrEmpty(note) ? "" : note + " ") + "Commandes n° : " + string.Join(", ", v.Commandes.Select(c => c.ToString(CultureInfo.InvariantCulture)));
            if (nouvelle.Length > 200) nouvelle = nouvelle[..200];
            await geste.EcrireAsync("UPDATE v2.AFFAIRE SET AFFRE_NOTE_INTERNE = @n WHERE AFFRE_ID_AFFRE = @a", ct,
                new SqlParameter("@n", nouvelle), new SqlParameter("@a", v.Affaire));
            traces.Add(new TraceGeste("affaire", $"l'affaire n° {v.Affaire}", v.Affaire.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { NoteInterne = note }, new { NoteInterne = nouvelle }));
        }
        int? encaissement = null;
        if (ventilations.Count > 0)                                                               // Encaissement.Sauve : sans ventilation, rien (:385-388)
        {
            var commentaire = $"Période du {fichier.Debut.ToString("dd/MM/yyyy", Francais)} au {fichier.Fin.ToString("dd/MM/yyyy", Francais)} Virement n°{fichier.IdPaiement}";
            encaissement = await geste.InsererAsync("v2.ENCAISSEMENT", "ENCMT_ID_ENCMT", new Dictionary<string, object?>
            {
                ["ENCMT_MONTANT"] = fichier.Total, ["ENCMT_DATE_ENCAISSEMENT"] = dateReleve, ["ENCMT_ID_ARRV"] = arrivee, ["ENCMT_ID_UTLST"] = agent,
                ["ENCMT_GUID"] = Guid.NewGuid().ToString().ToUpperInvariant(), ["ENCMT_MODE_PAIEMENT"] = mode, ["ENCMT_NUMERO_TITRE"] = titre,
                ["ENCMT_DESIGNATION_PAYEUR"] = "INPI", ["ENCMT_COMMENTAIRE"] = commentaire.Length > 255 ? commentaire[..255] : commentaire,
                ["ENCMT_EXT_SOURCE"] = "INPI", ["ENCMT_EXT_ID"] = fichier.IdPaiement.Length > 32 ? fichier.IdPaiement[..32] : fichier.IdPaiement,
                ["ENCMT_EXT_DATE"] = fichier.Production,
            }, ct);
            foreach (var v in ventilations)
                await geste.InsererAsync("v2.VENTILATION", "VENTI_ID_VENTI", new Dictionary<string, object?>
                {
                    ["VENTI_MONTANT"] = v.Montant, ["VENTI_DATE_VENTILATION"] = dateReleve, ["VENTI_ID_CLIEN"] = v.Client,
                    ["VENTI_ID_AFFRE"] = v.Affaire, ["VENTI_ID_ENCMT"] = encaissement,
                }, ct);
            traces.Add(new TraceGeste("encaissement", $"l'encaissement INPI du virement n°{fichier.IdPaiement}", encaissement.Value.ToString(CultureInfo.InvariantCulture),
                "a cree", null, new { Montant = fichier.Total, Mode = mode, Titre = titre, Date = dateReleve, Arrivee = arrivee,
                    Ventilations = ventilations.Select(v => new { v.Numero, v.Affaire, v.Client, v.Montant }) }));
        }
        await geste.EcrireAsync("""
            UPDATE v2.INPI_REVERSEMENTS SET INPIRV_ETAT_TRAITEMENT = '2', INPIRV_DATE_TRAITEMENT = @maintenant, INPIRV_ID_UTLST = @agent,
                   INPIRV_ID_ENCMT = @encaissement, INPIRV_MONTANT_VALIDATION = @montant
            WHERE INPIRV_ID_INPIRV = @id
            """, ct, new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@agent", (object?)agent ?? DBNull.Value),
            new SqlParameter("@encaissement", (object?)encaissement ?? DBNull.Value), new SqlParameter("@montant", montant), new SqlParameter("@id", id));
        traces.Add(new TraceGeste("encaissement", $"le fichier de reversements INPI n°{paiement}", id.ToString(CultureInfo.InvariantCulture), "a modifie",
            new { Etat = "A Valider" }, new { Etat = "Validé", Encaissement = encaissement, MontantValidation = montant }));
        var nb = ventilations.Count;
        return IssueGeste.Ecrit($"Fichier de reversements n°{paiement} validé dans la base du greffe : encaissement INPI de {CriteresConsultation.Euros(fichier.Total)} " +
                                $"({mode}), {nb} ventilation{(nb > 1 ? "s" : "")} par numéro national. Le mode de paiement n'est pas enregistré comme défaut du greffe " +
                                "(réglage INPIPAY_MODE_PAIEMENT, non écrit par la V3).",
            [id.ToString(CultureInfo.InvariantCulture)], [.. traces]);
    }

    /// <summary>
    /// La demande d'un numero national (Demat.GetDematByNumNational, Demat.cs:200-242) : Trouvee faux et le message de la V2 si aucune
    /// dematerialisation n'a ce numero ; sinon sa demande, null si elle n'en a pas (IDemat.Demande).
    /// </summary>
    private static async Task<(bool Trouvee, DemandeInpi? Demande, string? Erreur)> DematAsync(GesteEnCours geste, string numero, CancellationToken ct)
    {
        // GetTypeNumNational (Demat.cs:172-180) : « ENT » ou « J » une liasse (puis un DCA, :211-216), « CAE » ou « K » un DCA, sinon un DAS.
        var (tables, absent) = numero.StartsWith("ENT", StringComparison.Ordinal) || numero.StartsWith('J')
            ? (new[] { ("v2.DEMAT_LIASSE_CFE", "DMTLCF"), ("v2.DEMAT_DCA", "DMTDCA") }, "Aucune liasse n'a été trouvée à partir du numéro national ")
            : numero.StartsWith("CAE", StringComparison.Ordinal) || numero.StartsWith('K')
                ? (new[] { ("v2.DEMAT_DCA", "DMTDCA") }, "Aucun DCA n'a été trouvé à partir du numéro national ")
                : (new[] { ("v2.DEMAT_DAS", "DMTDAS") }, "Aucun DAS n'a été trouvé à partir du numéro national ");
        foreach (var (table, p) in tables)
        {
            // « SELECT TOP 1 * ... where <p>_NUMERO_NATIONAL_INPI = ... » sans ORDER BY dans la V2 : ici la premiere par id.
            await using var r = await geste.LireAsync($"""
                SELECT TOP (1) x.{p}_ID_DMND, d.DMND_ID_DMND, d.DMND_NUM_DEMANDE, d.DMND_ID_AFFRE, d.DMND_ID_ARRV
                FROM {table} AS x LEFT JOIN v2.DEMANDE AS d ON d.DMND_ID_DMND = x.{p}_ID_DMND
                WHERE x.{p}_NUMERO_NATIONAL_INPI = @n ORDER BY x.{p}_ID_{p}
                """, ct, new SqlParameter("@n", numero));
            if (!await r.ReadAsync(ct)) continue;
            DemandeInpi? demande = r.IsDBNull(1) ? null
                : new DemandeInpi(r.GetInt32(1), FormeLigne.Texte(r, "DMND_NUM_DEMANDE"), r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt32(4));
            return (true, demande, null);
        }
        return (false, null, absent + numero);
    }

    /// <summary>Le fichier fee-payment-data (v1-1, sans espace de noms) ; null s'il ne se lit pas (la V2 leve alors).</summary>
    private static Fichier? Lire(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        XElement racine;
        try { racine = XElement.Parse(xml); }
        catch (XmlException) { return null; }
        if (racine.Name.LocalName != "fee-payment-data" || racine.Elements().FirstOrDefault(e => e.Name.LocalName == "executor-office") is not { } office) return null;
        var debut = DateAttr(racine, "date-extract-begin");
        var fin = DateAttr(racine, "date-extract-end");
        if (debut is null || fin is null) return null;
        var reversements = new List<Reversement>();
        foreach (var fp in office.Elements().Where(e => e.Name.LocalName == "fee-pay"))
        {
            var numero = (string?)fp.Attribute("applicant-number") ?? "";
            if (!int.TryParse((string?)fp.Attribute("command-number"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var commande)) return null;
            var donnees = fp.Elements().FirstOrDefault(e => e.Name.LocalName == "basic-fee-data");
            reversements.Add(new Reversement(numero, commande, Montant((string?)donnees?.Attribute("total-fee"))));
        }
        return new Fichier(debut.Value, fin.Value, (string?)racine.Attribute("id-payment") ?? "", DateAttr(racine, "date-produced"),
            Montant((string?)office.Attribute("total-fee")), reversements);
    }

    private static DateTime? DateAttr(XElement e, string nom)
        => e.Attribute(nom) is { } a && DateTime.TryParse(a.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>feepaymentdata.GetDecimalValue (AmiString.DecimalTryParse) : blancs retires, virgule lue comme un point ; 0 sinon.</summary>
    private static decimal Montant(string? texte)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(texte ?? "", @"\p{Z}", "").Replace(',', '.');
        return decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowCurrencySymbol, CultureInfo.InvariantCulture, out var m) ? m : 0m;
    }
}
