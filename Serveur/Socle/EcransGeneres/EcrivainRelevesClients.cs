using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Releve de compte » (PROC_RELCLI) : « Valider les soldes » sur les comptes coches. Mode d'emploi : EcrivainEcran.cs ; la liste :
/// LecteurRelevesClients.cs. Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026.
///
/// SOURCE (ancien RIG) : PROC_RELCLI.DoVerifier puis DoValider (D:\RigApplication\Source\RIG\DLL\Processus\PROC_RELCLI\PROC_RELCLI.cs
/// :70-124, :161-251 ; Detail OPE de proc_relcli.md:111-205). Les CONTROLES, dans l'ordre de la V2 (chacun son message, au mot pres) :
///   1. le droit comptable : un des ROLES de l'agent porte la fonction EXPCOMPTA (:72, roles seuls : HabilitationRoleUserProcessus),
///      sinon « Droits insuffisants » (:73). Dans la V3 : soc.CompteRole joint a soc.RoleFonction (reprise des HABILIT_*) ;
///   2. au moins un compte coche (:75-76) ;
///   3. la periode de l'etape Edition : debut au 1er de son mois, fin au dernier jour de son mois (OPE_EDITION.cs:120, :126), par
///      defaut le mois precedent (DoInitZoneSaisie, :96-97) ; debut apres la fin : refus (:78-79).
/// Puis les ECRITURES (v2.CLIENT_SOLDE seule, ClientSolde.CreateClientSolde : date, montant, client, utilisateur V2) :
///   a. COMPLETION des soldes anterieurs des comptes COCHES (MetierReleveClient.CreerSoldesPrecedentsSiNecessaire, :573-600) : un compte
///      qui a un solde ET une ecriture (facture, ventilation) d'un mois anterieur a son plus ancien solde recoit un solde par mois, a
///      rebours jusqu'au mois de sa premiere ecriture : solde du mois = solde suivant + factures TTC - ventilations sur [mois, date du
///      solde suivant[, arrondi a 2 decimales (:594). Toujours faite, periode achevee ou non (PROC_RELCLI.cs:165-170) ;
///   b. VALIDATION de la periode, seulement si sa fin precede le 1er du mois courant (:198 ; sinon l'avertissement de :81-82, et la V2
///      ne fait que les editions) : la limite = le 1er du mois de la plus petite date entre la fin de periode, la premiere facture et le
///      premier encaissement NON comptabilises a cette date (GetBlockingUncomptaItems, :131-134 : FACTU_DATE_COMPTABILISATION et
///      ENCMT_DATE_COMPTABILISATION vides, ENCMT_REMIS vide ; Facture.cs:438-442, Encaissement.cs:201-205), chaque piece bloquante
///      nommee par l'avertissement de la V2 (:106-120) ; pour TOUS les comptes actifs C hors PC (GetClientsPourValidationPeriode,
///      MetierReleveClient.cs:649-662 -- pas la seule selection), de la date de depart (_GetActualStartingDate, PROC_RELCLI.cs:323-336 :
///      le mois du dernier solde, sinon de la premiere ecriture, sinon de la derniere modification du compte) jusqu'a la limite, un
///      solde par mois (:222-229) = Client.SoldeClient (Client.cs:1181-1195) : dernier solde + ventilations - factures TTC sur
///      [date du dernier solde, mois[, arrondi a 2 decimales ; sans solde, ventilations - factures depuis 1900.
/// Montants en decimal exact (money) : la V2 les relit en double avant de les remettre en decimal (Client.cs:1209, :1230) ; pour des
/// montants au centime, le resultat est le meme. Un compte sans aucune date de depart n'est pas valide, il est nomme (la V2 partirait
/// du 01/01/1800, DBDateTime.ValeurNull, DBObject.cs:531). La V2 traite chaque compte a part (un echec est journalise et nomme au bilan, :232-247) ;
/// la V3 ecrit tout ou rien, dans la transaction du geste. Ordre des soldes a date egale : la V2 lit TOP 1 sans cle de departage
/// (ClientSolde.cs:35-36, :45-46, limite nommee par proc_relcli.md:181) ; la V3 departage par CLISO_ID_CLISO.
/// NON REPRIS (nomme) : les EDITIONS des releves que la V2 genere ensuite (base.DoValider -> DoGenererLesEditions,
/// proc_relcli.md:207-260) : un etat Crystal (Expresso_ReleveCompte.rpt) range en GED, dont la DEMANDE_EDITION pointe le document
/// (DMNED_NUM_DOCUMENT_EDITION = GDRG_NUM_DOCUMENT) -- sans document, une telle ligne ne pointerait sur rien ; l'exclusion des
/// clients crediteurs sans mouvement (:84-94), qui ne porte que sur ces editions.
/// </summary>
internal sealed class EcrivainRelevesClients : IEcrivainEcran
{
    public string Procedure => "PROC_RELCLI";
    public string Ecran => "Relevé de compte";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    internal const string Valider = "Valider les soldes", DateDebut = "Date de début", DateFin = "Date de fin";

    // Les messages de la V2 (PROC_RELCLI.cs:73, :76, :79, :82), titre et detail reunis sur une ligne (l'ecran et le banc lisent une ligne).
    internal const string DroitsInsuffisants = "Droits insuffisants : seul un utilisateur ayant les droits comptables peut effectuer cette opération. " +
                                               "Si la validation de cette période est nécessaire, demandez à votre comptable d'effectuer l'opération.";
    internal const string SelectionVide = "Sélection de clients à éditer vide : aucun client n'est sélectionné dans la liste. Sélectionnez au moins un client.";
    internal const string DatesInversees = "La date de début est ultérieure à la date de fin. Veuillez corrigez votre saisie.";
    internal const string PeriodeEnCours = "La période sélectionnée n'est pas encore achevée.";

    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");
    private static string Mois(DateTime d) => d.ToString("MMMM yyyy", Francais);
    private static DateTime PremierDuMois(DateTime d) => new(d.Year, d.Month, 1);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Valider) return IssueGeste.Refuser($"Geste « {geste.Demande.Nom} » inconnu du relevé de compte.");

        // 1. Le droit comptable, par les roles de l'agent (PROC_RELCLI.cs:72).
        await using (var r = await geste.LireAsync("""
            SELECT COUNT(*) FROM soc.CompteRole AS cr JOIN soc.RoleFonction AS rf ON rf.Role = cr.Role
            WHERE cr.IdCompte = @agent AND rf.Fonction = 'EXPCOMPTA'
            """, ct, new SqlParameter("@agent", geste.Agent.Id)))
        {
            await r.ReadAsync(ct);
            if (r.GetInt32(0) == 0) return IssueGeste.Refuser(DroitsInsuffisants);
        }

        // 2. Les comptes coches (:75-76), lus dans la base.
        var ids = geste.Demande.Ids.Select(i => int.TryParse(i, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
                                   .Where(n => n > 0).Distinct().ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser(SelectionVide);

        // 3. La periode (OPE_EDITION.cs:96-97, :120, :126) ; debut apres la fin (:78-79).
        var aujourdhui = geste.Maintenant.Date;
        var debut = PremierDuMois(CriteresConsultation.Date(geste.Demande.Valeurs.GetValueOrDefault(DateDebut)) ?? PremierDuMois(aujourdhui).AddMonths(-1));
        var finSaisie = CriteresConsultation.Date(geste.Demande.Valeurs.GetValueOrDefault(DateFin)) ?? PremierDuMois(aujourdhui).AddDays(-1);
        var fin = PremierDuMois(finSaisie).AddMonths(1).AddDays(-1);
        if (debut > fin) return IssueGeste.Refuser(DatesInversees);

        var agent = await AgentV2Async(geste, ct);
        if (agent is null)
            return IssueGeste.Refuser("Votre compte n'a pas d'identifiant RIG v2 (soc.Compte.IdV2) : un solde validé porte son utilisateur. Rien n'est enregistré.");

        var traces = new List<TraceGeste>();
        var bilan = new StringBuilder();

        // a. Completion des soldes anterieurs des comptes coches (:165-196).
        var completes = 0;
        foreach (var client in await ComptesAsync(geste, $"cl.Id IN ({string.Join(", ", ids)})", aujourdhui, ct))
        {
            var soldes = await CompletionAsync(geste, client, ct);
            if (soldes.Count == 0) continue;
            await InsererSoldesAsync(geste, client.Id, soldes, agent.Value, ct);
            completes += soldes.Count;
            traces.Add(Trace(client, soldes, "completion des soldes anterieurs"));
        }
        if (completes > 0) bilan.Append($"{completes} solde{(completes > 1 ? "s" : "")} antérieur{(completes > 1 ? "s" : "")} complété{(completes > 1 ? "s" : "")} sur les comptes cochés. ");

        // b. La validation, seulement sur une periode achevee (:81-82, :198).
        if (fin >= PremierDuMois(aujourdhui))
        {
            if (traces.Count == 0)
                return IssueGeste.Refuser($"{PeriodeEnCours} Aucun solde n'est validé : RIG v2 n'y génère que les éditions des relevés, que la V3 ne produit pas.");
            bilan.Append($"{PeriodeEnCours} Aucun solde n'est validé.");
        }
        else
        {
            var limite = fin;
            var (facture, encaissement) = await PiecesBloquantesAsync(geste, fin, ct);
            var limiteF = facture?.Date ?? aujourdhui;
            var limiteE = encaissement?.Date ?? aujourdhui;
            limite = new[] { limiteE, limiteF, fin }.Min();
            var avertissement = "";
            // :106-120 : la facture d'abord, puis l'encaissement -- la V2 compare la limite a chacune (else if).
            if (limite == limiteF && facture is { } f)
                avertissement = $"La facture {f.Numero} du {f.Date:dd/MM/yyyy} n'est pas comptabilisée. La validation ne pourra pas continuer après {Mois(PremierDuMois(limite).AddMonths(-1))}.";
            else if (limite == limiteE && encaissement is { } e)
                avertissement = $"L'encaissement numéro {e.Numero} du {e.Date:dd/MM/yyyy} n'est pas compatibilisé. La validation ne pourra pas continuer après {Mois(PremierDuMois(limite).AddMonths(-1))}.";
            limite = PremierDuMois(limite);

            var valides = 0;
            var comptes = 0;
            var enErreur = new List<string>();
            foreach (var client in await ComptesAsync(geste, "cl.Type = 'C' AND cl.EstComptePc = 0 AND cl.Statut = 'A'", aujourdhui, ct))
            {
                if (client.Depart is not { } depart) { enErreur.Add(client.Abrege); continue; }
                if (depart.AddMonths(1) > limite) continue;
                var soldes = await ValidationAsync(geste, client, depart, limite, ct);
                await InsererSoldesAsync(geste, client.Id, soldes, agent.Value, ct);
                valides += soldes.Count;
                comptes++;
                traces.Add(Trace(client, soldes, "validation de la periode"));
            }
            if (avertissement.Length > 0) bilan.Append(avertissement).Append(' ');
            bilan.Append(valides == 0
                ? $"Aucun solde à valider : les comptes actifs sont déjà validés jusqu'à {Mois(limite.AddMonths(-1))}."
                : $"{valides} solde{(valides > 1 ? "s" : "")} mensuel{(valides > 1 ? "s" : "")} validé{(valides > 1 ? "s" : "")} pour {comptes} compte{(comptes > 1 ? "s" : "")} actif{(comptes > 1 ? "s" : "")}, " +
                  $"jusqu'à {Mois(limite.AddMonths(-1))}.");
            // Un compte sans aucune date de depart (ni solde, ni ecriture, ni date de modification) : la V2 lit CLIEN_DATE_DERNIERE_MODIF
            // vide comme DBDateTime.ValeurNull, le 01/01/1800 (RigTools\SQL\DBObject.cs:531), et validerait un solde a 0 par mois depuis
            // 1800. La V3 ne reprend pas ce defaut : elle nomme ces comptes, comme le bilan des comptes en echec de la V2 (:243-247).
            if (enErreur.Count > 0)
                bilan.Append($" {enErreur.Count} compte{(enErreur.Count > 1 ? "s" : "")} actif{(enErreur.Count > 1 ? "s" : "")} sans aucune date de départ " +
                             $"(ni solde, ni écriture, ni date de modification) non validé{(enErreur.Count > 1 ? "s" : "")} : {string.Join(", ", enErreur)} " +
                             "(RIG v2 les ferait partir du 01/01/1800).");
            if (traces.Count == 0) return IssueGeste.Refuser(bilan.ToString().Trim());
        }
        bilan.Append(" Les relevés (état de la V2) ne sont pas édités par la V3.");
        return IssueGeste.Ecrit(bilan.ToString().Trim(), [.. ids.Select(i => i.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }

    private static TraceGeste Trace(Compte client, List<(DateTime Date, decimal Montant)> soldes, string quoi)
        => new("client", $"le compte {client.Abrege}", client.Id.ToString(CultureInfo.InvariantCulture), "a valide",
            null, new { Quoi = quoi, Soldes = soldes.Select(s => new { Date = s.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), s.Montant }) });

    private static async Task<int?> AgentV2Async(GesteEnCours geste, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT IdV2 FROM soc.Compte WHERE Id = @agent", ct, new SqlParameter("@agent", geste.Agent.Id));
        return await r.ReadAsync(ct) && !r.IsDBNull(0) ? Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Un compte : son dernier solde a ce jour (GetClientSolde, ClientSolde.cs:31-40), son plus ancien (GetClientSoldeInitial,
    /// :42-50), sa premiere ecriture (facture ou ventilation) et sa date de depart de validation (_GetActualStartingDate).</summary>
    private sealed record Compte(int Id, string Abrege, DateTime? DernierSolde, decimal? MontantDernier, DateTime? PremierSolde, decimal? MontantPremier,
                                 DateTime? PremiereEcriture, DateTime? Modification)
    {
        /// <summary>PROC_RELCLI.cs:323-336 : le mois du dernier solde ; sans solde, celui de la premiere ecriture, sinon de la derniere
        /// modification du compte (CLIEN_DATE_DERNIERE_MODIF). Null : aucune des trois.</summary>
        public DateTime? Depart => (DernierSolde ?? PremiereEcriture ?? Modification) is { } d ? PremierDuMois(d) : null;
    }

    private static async Task<List<Compte>> ComptesAsync(GesteEnCours geste, string filtre, DateTime aujourdhui, CancellationToken ct)
    {
        var comptes = new List<Compte>();
        // GetClientsPourValidationPeriode : ORDER BY CLIEN_ABREGE (MetierReleveClient.cs:653).
        await using var r = await geste.LireAsync($"""
            SELECT cl.Id, cl.Abrege, cl.DateDerniereModification, d.CLISO_DATE_SOLDE AS DateDernier, d.CLISO_MONTANT AS MontantDernier,
                   p.CLISO_DATE_SOLDE AS DatePremier, p.CLISO_MONTANT AS MontantPremier,
                   (SELECT MIN(x.D) FROM (SELECT MIN(FACTU_DATE_CREATION) AS D FROM v2.FACTURE WHERE FACTU_ID_CLIEN = cl.Id
                                          UNION ALL SELECT MIN(VENTI_DATE_VENTILATION) FROM v2.VENTILATION WHERE VENTI_ID_CLIEN = cl.Id) AS x) AS PremiereEcriture
            FROM rec.Client AS cl
            OUTER APPLY (SELECT TOP (1) CLISO_DATE_SOLDE, CLISO_MONTANT FROM v2.CLIENT_SOLDE
                         WHERE CLISO_ID_CLIEN = cl.Id AND CLISO_DATE_SOLDE <= @aujourdhui ORDER BY CLISO_DATE_SOLDE DESC, CLISO_ID_CLISO DESC) AS d
            OUTER APPLY (SELECT TOP (1) CLISO_DATE_SOLDE, CLISO_MONTANT FROM v2.CLIENT_SOLDE
                         WHERE CLISO_ID_CLIEN = cl.Id ORDER BY CLISO_DATE_SOLDE, CLISO_ID_CLISO) AS p
            WHERE {filtre}
            ORDER BY cl.Abrege, cl.Id
            """, ct, new SqlParameter("@aujourdhui", aujourdhui));
        while (await r.ReadAsync(ct))
            comptes.Add(new Compte(r.GetInt32(0), FormeLigne.Texte(r, "Abrege").Trim(), FormeLigne.DateDe(r, "DateDernier"), CriteresConsultation.Decimal(r, "MontantDernier"),
                FormeLigne.DateDe(r, "DatePremier"), CriteresConsultation.Decimal(r, "MontantPremier"), FormeLigne.DateDe(r, "PremiereEcriture"),
                FormeLigne.DateDe(r, "DateDerniereModification")));
        return comptes;
    }

    /// <summary>Les factures TTC et les ventilations du compte, par mois, sur [depuis, jusqua[ (bornes de Client._GetTotalFactures et
    /// _GetTotalEncaissements, Client.cs:1200-1206, :1221-1227).</summary>
    private static async Task<SortedDictionary<DateTime, (decimal Factures, decimal Ventilations)>> ParMoisAsync(GesteEnCours geste, int client,
        DateTime depuis, DateTime jusqua, CancellationToken ct)
    {
        var mois = new SortedDictionary<DateTime, (decimal, decimal)>();
        await using var r = await geste.LireAsync("""
            SELECT x.Mois, SUM(x.F), SUM(x.V) FROM (
                SELECT DATEFROMPARTS(YEAR(FACTU_DATE_CREATION), MONTH(FACTU_DATE_CREATION), 1) AS Mois, CAST(FACTU_MONTANT_TTC AS money) AS F, CAST(0 AS money) AS V
                FROM v2.FACTURE WHERE FACTU_ID_CLIEN = @client AND FACTU_DATE_CREATION >= @depuis AND FACTU_DATE_CREATION < @jusqua
                UNION ALL
                SELECT DATEFROMPARTS(YEAR(VENTI_DATE_VENTILATION), MONTH(VENTI_DATE_VENTILATION), 1), CAST(0 AS money), CAST(VENTI_MONTANT AS money)
                FROM v2.VENTILATION WHERE VENTI_ID_CLIEN = @client AND VENTI_DATE_VENTILATION >= @depuis AND VENTI_DATE_VENTILATION < @jusqua) AS x
            GROUP BY x.Mois
            """, ct, new SqlParameter("@client", client), new SqlParameter("@depuis", depuis), new SqlParameter("@jusqua", jusqua));
        while (await r.ReadAsync(ct))
            mois[r.GetDateTime(0)] = (r.IsDBNull(1) ? 0m : r.GetDecimal(1), r.IsDBNull(2) ? 0m : r.GetDecimal(2));
        return mois;
    }

    /// <summary>La somme des mois de <paramref name="parMois"/> dans [de, a[ : un mois compte s'il commence avant <paramref name="a"/>
    /// (ses lignes sont deja bornees par la requete).</summary>
    private static (decimal Factures, decimal Ventilations) Somme(SortedDictionary<DateTime, (decimal Factures, decimal Ventilations)> parMois, DateTime de, DateTime a)
    {
        decimal f = 0m, v = 0m;
        foreach (var (m, (fm, vm)) in parMois)
            if (m >= PremierDuMois(de) && m < a) { f += fm; v += vm; }
        return (f, v);
    }

    /// <summary>MetierReleveClient.CreerSoldesPrecedentsSiNecessaire (:573-600).</summary>
    private static async Task<List<(DateTime, decimal)>> CompletionAsync(GesteEnCours geste, Compte client, CancellationToken ct)
    {
        var soldes = new List<(DateTime, decimal)>();
        if (client.PremierSolde is not { } dateInitial || client.MontantPremier is not { } montantInitial) return soldes;   // :575-577
        if (client.PremiereEcriture is not { } premiere) return soldes;                                                    // :581-583
        var moisPremiere = PremierDuMois(premiere);
        if (moisPremiere >= PremierDuMois(dateInitial)) return soldes;                                                      // :585-587
        var parMois = await ParMoisAsync(geste, client.Id, moisPremiere, dateInitial, ct);
        var dateSuivant = dateInitial;
        var soldeSuivant = montantInitial;
        for (var dateMois = PremierDuMois(dateInitial).AddMonths(-1); dateMois >= moisPremiere; dateMois = dateMois.AddMonths(-1))
        {
            var (f, v) = Somme(parMois, dateMois, dateSuivant);
            var soldeMois = Math.Round(soldeSuivant + f - v, 2);                                                            // :594
            soldes.Add((dateMois, soldeMois));
            soldeSuivant = soldeMois;
            dateSuivant = dateMois;
        }
        return soldes;
    }

    /// <summary>La boucle de validation d'un compte (PROC_RELCLI.cs:219-229) et Client.SoldeClient (Client.cs:1181-1195) : chaque solde cree
    /// devient le dernier solde du mois suivant.</summary>
    private static async Task<List<(DateTime, decimal)>> ValidationAsync(GesteEnCours geste, Compte client, DateTime depart, DateTime limite, CancellationToken ct)
    {
        var soldes = new List<(DateTime, decimal)>();
        var dateDernier = client.DernierSolde ?? new DateTime(1900, 1, 1);
        var dernier = client.MontantDernier ?? 0m;
        var parMois = await ParMoisAsync(geste, client.Id, dateDernier, limite, ct);
        for (var date = depart; date.AddMonths(1) <= limite;)
        {
            date = date.AddMonths(1);
            var (f, v) = Somme(parMois, dateDernier, date);
            var solde = Math.Round(dernier + v - f, 2);
            soldes.Add((date, solde));
            dernier = solde;
            dateDernier = date;
        }
        return soldes;
    }

    private sealed record Piece(string Numero, DateTime Date);

    /// <summary>GetBlockingUncomptaItems (PROC_RELCLI.cs:131-134) : la plus ancienne facture et le plus ancien encaissement non remis non
    /// comptabilises, dates au plus a la fin de periode.</summary>
    private static async Task<(Piece? Facture, Piece? Encaissement)> PiecesBloquantesAsync(GesteEnCours geste, DateTime fin, CancellationToken ct)
    {
        Piece? facture = null, encaissement = null;
        await using (var r = await geste.LireAsync("""
            SELECT TOP (1) FACTU_NUMERO_FACTURE, FACTU_DATE_CREATION FROM v2.FACTURE
            WHERE FACTU_DATE_COMPTABILISATION IS NULL AND FACTU_DATE_CREATION <= @fin ORDER BY FACTU_DATE_CREATION, FACTU_NUMERO_FACTURE
            """, ct, new SqlParameter("@fin", fin)))
            if (await r.ReadAsync(ct)) facture = new Piece(FormeLigne.Texte(r, "FACTU_NUMERO_FACTURE").Trim(), r.GetDateTime(1));
        await using (var r = await geste.LireAsync("""
            SELECT TOP (1) ENCMT_ID_ENCMT, ENCMT_DATE_ENCAISSEMENT FROM v2.ENCAISSEMENT
            WHERE ENCMT_DATE_COMPTABILISATION IS NULL AND ENCMT_REMIS IS NULL AND ENCMT_DATE_ENCAISSEMENT <= @fin
            ORDER BY ENCMT_DATE_ENCAISSEMENT, ENCMT_ID_ENCMT
            """, ct, new SqlParameter("@fin", fin)))
            if (await r.ReadAsync(ct)) encaissement = new Piece(r.GetInt32(0).ToString(CultureInfo.InvariantCulture), r.GetDateTime(1));
        return (facture, encaissement);
    }

    /// <summary>Les soldes d'un compte dans v2.CLIENT_SOLDE (ClientSolde.CreateClientSolde, ClientSolde.cs:20-29), par paquets : la cle
    /// CLISO_ID_CLISO est l'IDENTITY de RIG_DEV (SSDT CLIENT_SOLDE.sql) ; sans elle (base d'un banc), MAX + 1 sous verrou.</summary>
    private static async Task InsererSoldesAsync(GesteEnCours geste, int client, List<(DateTime Date, decimal Montant)> soldes, int agent, CancellationToken ct)
    {
        if (soldes.Count == 0) return;
        bool identite;
        await using (var r = await geste.LireAsync("SELECT COLUMNPROPERTY(OBJECT_ID('v2.CLIENT_SOLDE'), 'CLISO_ID_CLISO', 'IsIdentity')", ct))
            identite = await r.ReadAsync(ct) && !r.IsDBNull(0) && r.GetInt32(0) == 1;
        var suivant = 0;
        if (!identite)
            await using (var r = await geste.LireAsync("SELECT ISNULL(MAX(CLISO_ID_CLISO), 0) + 1 FROM v2.CLIENT_SOLDE WITH (UPDLOCK, HOLDLOCK)", ct))
            {
                await r.ReadAsync(ct);
                suivant = r.GetInt32(0);
            }
        foreach (var paquet in soldes.Chunk(400))
        {
            var valeurs = new List<string>();
            var parametres = new List<SqlParameter> { new("@client", client), new("@agent", agent) };
            for (var k = 0; k < paquet.Length; k++)
            {
                valeurs.Add(identite ? $"(@d{k}, @m{k}, @client, @agent)" : $"(@i{k}, @d{k}, @m{k}, @client, @agent)");
                parametres.Add(new SqlParameter($"@d{k}", paquet[k].Date));
                parametres.Add(new SqlParameter($"@m{k}", System.Data.SqlDbType.Money) { Value = paquet[k].Montant });
                if (!identite) parametres.Add(new SqlParameter($"@i{k}", suivant++));
            }
            await geste.EcrireAsync($"INSERT INTO v2.CLIENT_SOLDE ({(identite ? "" : "CLISO_ID_CLISO, ")}CLISO_DATE_SOLDE, CLISO_MONTANT, CLISO_ID_CLIEN, CLISO_ID_UTLST) " +
                                    $"VALUES {string.Join(", ", valeurs)}", ct, [.. parametres]);
        }
    }
}
