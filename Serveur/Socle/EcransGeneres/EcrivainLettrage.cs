using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurLettrage;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Lettrage » (PROC_VLETTRAGE) : LETTRER et DÉLETTRER les clients affiches. Mode d'emploi : EcrivainEcran.cs ; les lignes :
/// LecteurLettrage.cs. Plan « RIG v3 100 % fonctionnel », lot 13, 27/11/2026.
///
/// SOURCE (ancien RIG) : l'ecran PROC_VLETTRAGE de la V2 n'ecrit rien (proc_vlettrage.md:46). Le lettrage V2 est celui de l'entite
/// Lettrage (Outils\DLL\RigMetier\Facture\Lettrage.cs), joue par la comptabilisation du jour (PROC_COMPTABILISATION\
/// OPE_COMPTABILISATION.cs:285-288, :643-646, :669-672) et la moulinette TMS_COMPTA (Program.cs:192-195, :276-297). La V3 en fait
/// les deux gestes de l'ecran, pour chaque ligne envoyee, a l'instant du geste (dateMax de la V2 = maintenant) :
///   LETTRER un client compte ou PC (« C-<client> ») = Lettrage.LettrerClientCompte(client, maintenant, AnnuleRemplace = false)
///   (:396-561) : si le client a deja des lettrages, le COMPLEMENT -- les factures et ventilations comptabilisees APRES la date du
///   dernier lettrage (:420-438) ; sinon toutes les factures et ventilations comptabilisees, et, pour un compte hors PC, le premier
///   solde connu (CLIENT_SOLDE au plus tard le 01/01/2019, ClientSolde.GetClientSolde, :528-548) devient un premier lettrage :
///   un solde crediteur en encaissement, un solde debiteur en emoluments factures ;
///   LETTRER un client divers sur une affaire (« D-<client>-<affaire> ») = Lettrage.LettrerAffaire (:586-609) : rien si l'affaire a
///   deja un lettrage (:596-604) ; sinon ses factures (FACTURE x DEMANDE.DMND_ID_AFFRE, Facture.GetFactureByAffaire, Facture.cs:420-
///   426) et ses ventilations ;
///   puis TraiterLettrageMasse (:563-584) : factures et ventilations dans l'ordre des dates (facture si FACTU_DATE_CREATION <
///   VENTI_DATE_VENTILATION), chacune par LettrerFacture (:295-394) ou LettrerEncaissement (:64-134) : une ligne LETTRAGE par facture
///   (TTC, BODACC / INPI / autres = sommes de ses ARTICLE_UTILISE, TVA de la facture, emoluments = le reste) et par ventilation
///   (son montant), le regle reparti par poste dans l'ordre BODACC, INPI, autres, TVA, emoluments (calculMontantReaffect, :639-654),
///   un avoir ou un encaissement negatif annulant dans l'ordre inverse (calculMontantAnnul, :656-671) ; une facture a 0, non
///   comptabilisee ou deja lettree (LTTRG_NUMERO_FACTURE), une ventilation deja lettree (LTTRG_ID_VENTI), ne s'ecrivent pas ;
///   DÉLETTRER = la branche « AnnuleRemplace » de ces deux methodes (:412-419, :597-602) : les lettrages du client (de l'affaire pour
///   un divers) dates avant maintenant sont supprimes. La V2 n'appelle jamais cette branche seule : elle relettre aussitot
///   (« Si lettrage déjà fait on annule et refait », :411). Délettrer puis Lettrer est donc l'« annule et refait » de RIG v2.
/// CONTROLES, avant toute ecriture : un geste inconnu, aucune ligne, une ligne illisible, un client absent de la base ; le client
/// d'attente, le client d'annulation et DGREFFE, que la V2 ne lettre pas (:68-72, :300-302, :405-411, :590-594) ; un divers sans
/// affaire. RIG v2 sort alors en silence ; la V3 refuse et dit pourquoi. Un Lettrer qui n'a rien a ecrire, un Délettrer sans
/// lettrage : refus, rien d'ecrit.
/// ORDRE : la V2 lit ses listes sans ORDER BY (Facture.GetFacturesByClient, Ventilation.GetVentilationByClient) ; la V3 trie par
/// date puis identifiant, pour un resultat reproductible.
/// TRACE : famille « client », « a cree » (lettrages ajoutes) ou « a supprime » (lettrages retires), avant et apres : le nombre de
/// lettrages du client et leurs sommes facture / regle.
/// </summary>
internal sealed class EcrivainLettrage : IEcrivainEcran
{
    public string Procedure => "PROC_VLETTRAGE";
    public string Ecran => "Lettrage";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    internal const string Lettrer = "Lettrer", Delettrer = "Délettrer";
    internal const string SansLigne = "Aucun client à lettrer : filtrez la liste sur les clients voulus.";
    internal const string RienALettrer = "Rien à lettrer : toutes les factures et ventilations comptabilisées de ces clients le sont déjà.";
    internal const string RienADelettrer = "Aucun lettrage à annuler pour ces clients.";

    /// <summary>La date de depart du premier solde d'un compte (« new DateTime(2019, 1, 1) », Lettrage.cs:531).</summary>
    private static readonly DateTime DepartSolde = new(2019, 1, 1);

    /// <summary>Une ligne LETTRAGE en memoire : les montants que les calculs de la V2 relisent (Lettrage.cs:101-111).</summary>
    private sealed class Ligne
    {
        public decimal MtFacture, MtEncaissement, FactHt, FactTva, FactBodacc, FactInpi, FactAutre, RegleHt, RegleTva, RegleBodacc, RegleInpi, RegleAutre;
    }

    private sealed record Client(int Id, string Type, bool EstComptePc, string Nom, string Abrege, string NumeroCompte)
    {
        public string Libelle => FormeLigne.Joindre(Abrege, Nom);
    }

    private sealed record Facture(string Numero, DateTime Date, decimal Ttc, decimal Tva, int? IdAffaire);
    private sealed record Ventil(int Id, DateTime DateVentilation, DateTime DateEncaissement, decimal Montant, int? IdAffaire);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var nom = geste.Demande.Nom;
        if (nom != Lettrer && nom != Delettrer) return IssueGeste.Refuser($"« {nom} » n'est pas un geste du lettrage : « {Lettrer} » ou « {Delettrer} ».");
        if (geste.Demande.Ids.Length == 0) return IssueGeste.Refuser(SansLigne);

        // Les controles d'abord, sur toutes les lignes : un refus n'ecrit rien.
        var cibles = new List<(string Id, Client Client, int? Affaire)>();
        foreach (var id in geste.Demande.Ids)
        {
            if (Lire(id) is not { } cle) return IssueGeste.Refuser($"Ligne de lettrage illisible ({id}).");
            if (await ClientAsync(geste, cle.Client, ct) is not { } client) return IssueGeste.Refuser($"Client introuvable dans la base du greffe ({id}).");
            if (client.NumeroCompte == CompteAttente) return IssueGeste.Refuser($"{client.Libelle} : RIG v2 ne lettre pas le client d'attente.");
            if (client.Nom == NomAnnulation) return IssueGeste.Refuser($"{client.Libelle} : RIG v2 ne lettre pas le client d'annulation.");
            if (client.Abrege == AbregeDgreffe) return IssueGeste.Refuser($"{client.Libelle} : RIG v2 ne lettre pas le client DGREFFE.");
            if (client.Type == "D" && cle.Affaire is null) return IssueGeste.Refuser($"{client.Libelle} : un client divers se lettre par affaire (RIG v2, LettrerAffaire).");
            cibles.Add((id, client, client.Type == "D" ? cle.Affaire : null));
        }

        var traces = new List<TraceGeste>();
        var faits = new List<string>();
        var total = 0;
        foreach (var (id, client, affaire) in cibles)
        {
            var avant = await EtatAsync(geste, client.Id, affaire, ct);
            var n = nom == Lettrer ? await LettrerAsync(geste, client, affaire, ct) : await DelettrerAsync(geste, client.Id, affaire, ct);
            if (n == 0) continue;
            total += n;
            var apres = await EtatAsync(geste, client.Id, affaire, ct);
            var objet = affaire is { } a ? $"le lettrage du client {client.Libelle}, affaire {a}" : $"le lettrage du client {client.Libelle}";
            traces.Add(new TraceGeste("client", objet, id, nom == Lettrer ? "a cree" : "a supprime", avant, apres));
            faits.Add(id);
        }
        if (total == 0) return IssueGeste.Refuser(nom == Lettrer ? RienALettrer : RienADelettrer);
        var message = nom == Lettrer
            ? $"{total} lettrage{(total > 1 ? "s" : "")} écrit{(total > 1 ? "s" : "")} pour {faits.Count} client{(faits.Count > 1 ? "s" : "")}."
            : $"{total} lettrage{(total > 1 ? "s" : "")} annulé{(total > 1 ? "s" : "")} pour {faits.Count} client{(faits.Count > 1 ? "s" : "")}.";
        return IssueGeste.Ecrit(message, [.. faits], [.. traces]);
    }

    private static (int Client, int? Affaire)? Lire(string id)
    {
        var morceaux = id.Split('-');
        static int? Entier(string s) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
        return morceaux switch
        {
            ["C", var c] when Entier(c) is { } client => (client, null),
            ["D", var c] when Entier(c) is { } client => (client, null),
            ["D", var c, var a] when Entier(c) is { } client && Entier(a) is { } affaire => (client, affaire),
            _ => null,
        };
    }

    private static async Task<Client?> ClientAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT Id, Type, EstComptePc, Nom, Abrege, NumeroCompte FROM rec.Client WHERE Id = @id", ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        return new Client(id, FormeLigne.Texte(r, "Type"), CriteresConsultation.Vrai(r, "EstComptePc"), FormeLigne.Texte(r, "Nom"),
            FormeLigne.Texte(r, "Abrege"), FormeLigne.Texte(r, "NumeroCompte"));
    }

    /// <summary>Ce que la trace retient : le nombre de lettrages et leurs sommes facture / regle.</summary>
    private static async Task<object> EtatAsync(GesteEnCours geste, int client, int? affaire, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT COUNT(*) AS N, ISNULL(SUM(LTTRG_MT_FACTURE), 0) AS F, ISNULL(SUM(LTTRG_MT_ENCAISSEMENT), 0) AS R
            FROM v2.LETTRAGE WHERE LTTRG_ID_CLIEN = @c AND (@a IS NULL OR LTTRG_ID_AFFRE = @a)
            """, ct, new SqlParameter("@c", client), new SqlParameter("@a", (object?)affaire ?? DBNull.Value));
        await r.ReadAsync(ct);
        return new { Lettrages = r.GetInt32(0), Facture = CriteresConsultation.MontantFiche(r.GetDecimal(1)), Regle = CriteresConsultation.MontantFiche(r.GetDecimal(2)) };
    }

    // ------------------------------------------------------------------ DÉLETTRER

    private static Task<int> DelettrerAsync(GesteEnCours geste, int client, int? affaire, CancellationToken ct)
        => geste.EcrireAsync("DELETE FROM v2.LETTRAGE WHERE LTTRG_ID_CLIEN = @c AND (@a IS NULL OR LTTRG_ID_AFFRE = @a) AND LTTRG_DATE < @max", ct,
            new SqlParameter("@c", client), new SqlParameter("@a", (object?)affaire ?? DBNull.Value), new SqlParameter("@max", geste.Maintenant));

    // ------------------------------------------------------------------ LETTRER

    private static async Task<int> LettrerAsync(GesteEnCours geste, Client client, int? affaire, CancellationToken ct)
    {
        var max = geste.Maintenant;
        var deja = await LettragesAsync(geste, client.Id, affaire, ct);
        List<Facture> factures;
        List<Ventil> ventils;
        var ecrits = 0;
        if (affaire is { } idAffaire)
        {
            // LettrerAffaire : rien si l'affaire a deja un lettrage avant dateMax (:596-604).
            if (await DernierLettrageAsync(geste, client.Id, idAffaire, max, ct) is not null) return 0;
            factures = await FacturesAsync(geste, client.Id, idAffaire, max, null, false, ct);
            ventils = await VentilationsAsync(geste, client.Id, idAffaire, max, null, false, ct);
        }
        else
        {
            var dernier = await DernierLettrageAsync(geste, client.Id, null, max, ct);
            factures = await FacturesAsync(geste, client.Id, null, max, dernier, true, ct);
            ventils = await VentilationsAsync(geste, client.Id, null, max, dernier, true, ct);
            if (dernier is null && !client.EstComptePc && await PremierSoldeAsync(geste, client.Id, ct) is { } solde)
            {
                var l = new Ligne();
                if (solde.Montant > 0) l.MtEncaissement = solde.Montant;
                else { l.MtFacture = -solde.Montant; l.FactHt = -solde.Montant; }
                await InsererAsync(geste, client.Id, null, solde.Date, l, null, null, ct);
                deja.Add(l);
                ecrits++;
                factures = factures.Where(f => f.Date >= solde.Date).ToList();
                ventils = ventils.Where(v => v.DateEncaissement >= solde.Date).ToList();
            }
        }

        // TraiterLettrageMasse (:563-584).
        int i = 0, j = 0;
        while (i < factures.Count || j < ventils.Count)
        {
            var facture = i < factures.Count && (j >= ventils.Count || factures[i].Date < ventils[j].DateVentilation);
            if (facture) ecrits += await LettrerFactureAsync(geste, client, factures[i++], deja, ct);
            else ecrits += await LettrerEncaissementAsync(geste, client, ventils[j++], deja, ct);
        }
        return ecrits;
    }

    private static async Task<List<Ligne>> LettragesAsync(GesteEnCours geste, int client, int? affaire, CancellationToken ct)
    {
        var liste = new List<Ligne>();
        await using var r = await geste.LireAsync("""
            SELECT LTTRG_MT_FACTURE, LTTRG_MT_ENCAISSEMENT, LTTRG_FACT_HT, LTTRG_FACT_TVA, LTTRG_FACT_BODACC, LTTRG_FACT_INPI, LTTRG_FACT_AUTRE,
                   LTTRG_REGLE_HT, LTTRG_REGLE_TVA, LTTRG_REGLE_BODACC, LTTRG_REGLE_INPI, LTTRG_REGLE_AUTRE
            FROM v2.LETTRAGE WHERE LTTRG_ID_CLIEN = @c AND (@a IS NULL OR LTTRG_ID_AFFRE = @a)
            """, ct, new SqlParameter("@c", client), new SqlParameter("@a", (object?)affaire ?? DBNull.Value));
        while (await r.ReadAsync(ct))
            liste.Add(new Ligne
            {
                MtFacture = r.GetDecimal(0), MtEncaissement = r.GetDecimal(1), FactHt = r.GetDecimal(2), FactTva = r.GetDecimal(3),
                FactBodacc = r.GetDecimal(4), FactInpi = r.GetDecimal(5), FactAutre = r.GetDecimal(6), RegleHt = r.GetDecimal(7),
                RegleTva = r.GetDecimal(8), RegleBodacc = r.GetDecimal(9), RegleInpi = r.GetDecimal(10), RegleAutre = r.GetDecimal(11),
            });
        return liste;
    }

    private static async Task<DateTime?> DernierLettrageAsync(GesteEnCours geste, int client, int? affaire, DateTime max, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT MAX(LTTRG_DATE) FROM v2.LETTRAGE WHERE LTTRG_ID_CLIEN = @c AND (@a IS NULL OR LTTRG_ID_AFFRE = @a) AND LTTRG_DATE < @max",
            ct, new SqlParameter("@c", client), new SqlParameter("@a", (object?)affaire ?? DBNull.Value), new SqlParameter("@max", max));
        await r.ReadAsync(ct);
        return r.IsDBNull(0) ? null : r.GetDateTime(0);
    }

    /// <summary>Les factures du client (de l'affaire pour un divers) creees avant <paramref name="max"/> ; un compte ne prend que les
    /// comptabilisees, apres <paramref name="dernier"/> s'il a deja un lettrage (Lettrage.cs:427-433).</summary>
    private static async Task<List<Facture>> FacturesAsync(GesteEnCours geste, int client, int? affaire, DateTime max, DateTime? dernier, bool compte,
                                                         CancellationToken ct)
    {
        var liste = new List<Facture>();
        await using var r = await geste.LireAsync("""
            SELECT f.FACTU_NUMERO_FACTURE, f.FACTU_DATE_CREATION, f.FACTU_MONTANT_TTC, ISNULL(f.FACTU_MONTANT_TVA, 0) AS Tva, d.DMND_ID_AFFRE
            FROM v2.FACTURE AS f LEFT JOIN v2.DEMANDE AS d ON d.DMND_NUM_DEMANDE = f.FACTU_NUM_DEMANDE
            WHERE f.FACTU_ID_CLIEN = @c AND f.FACTU_DATE_CREATION < @max AND (@a IS NULL OR d.DMND_ID_AFFRE = @a)
              AND (@compte = 0 OR (f.FACTU_DATE_COMPTABILISATION IS NOT NULL AND (@dernier IS NULL OR f.FACTU_DATE_COMPTABILISATION > @dernier)))
            ORDER BY f.FACTU_DATE_CREATION, f.FACTU_NUMERO_FACTURE
            """, ct, new SqlParameter("@c", client), new SqlParameter("@max", max), new SqlParameter("@a", (object?)affaire ?? DBNull.Value),
            new SqlParameter("@compte", compte), new SqlParameter("@dernier", (object?)dernier ?? DBNull.Value));
        while (await r.ReadAsync(ct))
            liste.Add(new Facture(r.GetString(0), r.GetDateTime(1), r.GetDecimal(2), r.GetDecimal(3), r.IsDBNull(4) ? null : r.GetInt32(4)));
        return liste;
    }

    /// <summary>Les ventilations du client (de l'affaire pour un divers) dont l'encaissement precede <paramref name="max"/> ; un compte
    /// ne prend que les encaissements comptabilises, apres <paramref name="dernier"/> (Lettrage.cs:427-433).</summary>
    private static async Task<List<Ventil>> VentilationsAsync(GesteEnCours geste, int client, int? affaire, DateTime max, DateTime? dernier, bool compte,
                                                            CancellationToken ct)
    {
        var liste = new List<Ventil>();
        await using var r = await geste.LireAsync("""
            SELECT v.VENTI_ID_VENTI, ISNULL(v.VENTI_DATE_VENTILATION, e.ENCMT_DATE_ENCAISSEMENT) AS DateVentilation, e.ENCMT_DATE_ENCAISSEMENT,
                   v.VENTI_MONTANT, v.VENTI_ID_AFFRE
            FROM v2.VENTILATION AS v INNER JOIN v2.ENCAISSEMENT AS e ON e.ENCMT_ID_ENCMT = v.VENTI_ID_ENCMT
            WHERE v.VENTI_ID_CLIEN = @c AND e.ENCMT_DATE_ENCAISSEMENT < @max AND (@a IS NULL OR v.VENTI_ID_AFFRE = @a)
              AND (@compte = 0 OR (e.ENCMT_DATE_COMPTABILISATION IS NOT NULL AND (@dernier IS NULL OR e.ENCMT_DATE_COMPTABILISATION > @dernier)))
            ORDER BY DateVentilation, v.VENTI_ID_VENTI
            """, ct, new SqlParameter("@c", client), new SqlParameter("@max", max), new SqlParameter("@a", (object?)affaire ?? DBNull.Value),
            new SqlParameter("@compte", compte), new SqlParameter("@dernier", (object?)dernier ?? DBNull.Value));
        while (await r.ReadAsync(ct))
            liste.Add(new Ventil(r.GetInt32(0), r.GetDateTime(1), r.GetDateTime(2), r.GetDecimal(3), r.IsDBNull(4) ? null : r.GetInt32(4)));
        return liste;
    }

    /// <summary>Le dernier solde du client au plus tard le 01/01/2019 (ClientSolde.GetClientSolde, ClientSolde.cs:31-40).</summary>
    private static async Task<(DateTime Date, decimal Montant)?> PremierSoldeAsync(GesteEnCours geste, int client, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT TOP (1) CLISO_DATE_SOLDE, CLISO_MONTANT FROM v2.CLIENT_SOLDE
            WHERE CLISO_ID_CLIEN = @c AND CLISO_DATE_SOLDE <= @depart ORDER BY CLISO_DATE_SOLDE DESC
            """, ct, new SqlParameter("@c", client), new SqlParameter("@depart", DepartSolde));
        return await r.ReadAsync(ct) ? (r.GetDateTime(0), r.GetDecimal(1)) : null;
    }

    private static async Task<bool> ExisteAsync(GesteEnCours geste, string sql, SqlParameter p, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(sql, ct, p);
        return await r.ReadAsync(ct);
    }

    /// <summary>LettrerFacture (Lettrage.cs:295-394).</summary>
    private static async Task<int> LettrerFactureAsync(GesteEnCours geste, Client client, Facture f, List<Ligne> deja, CancellationToken ct)
    {
        if (f.Ttc == 0) return 0;
        if (await ExisteAsync(geste, "SELECT 1 FROM v2.LETTRAGE WHERE LTTRG_NUMERO_FACTURE = @n", new SqlParameter("@n", f.Numero), ct)) return 0;
        if (!await ExisteAsync(geste, "SELECT 1 FROM v2.FACTURE WHERE FACTU_NUMERO_FACTURE = @n AND FACTU_DATE_COMPTABILISATION IS NOT NULL",
                new SqlParameter("@n", f.Numero), ct)) return 0;
        var l = new Ligne { MtFacture = f.Ttc, FactTva = f.Tva };
        await using (var r = await geste.LireAsync("""
            SELECT ISNULL(SUM(ARTUT_MONTANT_BODACC), 0), ISNULL(SUM(ARTUT_MONTANT_INPI), 0), ISNULL(SUM(ARTUT_MONTANT_AUTRE), 0)
            FROM v2.ARTICLE_UTILISE WHERE ARTUT_NUMERO_FACTURE = @n
            """, ct, new SqlParameter("@n", f.Numero)))
        {
            await r.ReadAsync(ct);
            l.FactBodacc = r.GetDecimal(0); l.FactInpi = r.GetDecimal(1); l.FactAutre = r.GetDecimal(2);
        }
        l.FactHt = l.MtFacture - (l.FactBodacc + l.FactInpi + l.FactAutre + l.FactTva);

        decimal tFactHt = deja.Sum(x => x.FactHt) + l.FactHt, tFactTva = deja.Sum(x => x.FactTva) + l.FactTva,
                tFactBodacc = deja.Sum(x => x.FactBodacc) + l.FactBodacc, tFactInpi = deja.Sum(x => x.FactInpi) + l.FactInpi,
                tFactAutre = deja.Sum(x => x.FactAutre) + l.FactAutre;
        decimal tRegHt = deja.Sum(x => x.RegleHt), tRegTva = deja.Sum(x => x.RegleTva), tRegBodacc = deja.Sum(x => x.RegleBodacc),
                tRegInpi = deja.Sum(x => x.RegleInpi), tRegAutre = deja.Sum(x => x.RegleAutre);
        var dispo = deja.Sum(x => x.MtEncaissement) - tRegHt - tRegTva - tRegBodacc - tRegInpi - tRegAutre;

        if (f.Ttc > 0)
        {
            if (dispo > 0)
            {
                if (tFactBodacc > 0) l.RegleBodacc = Reaffecter(tRegBodacc, tFactBodacc, ref dispo);
                if (tFactInpi > 0) l.RegleInpi = Reaffecter(tRegInpi, tFactInpi, ref dispo);
                if (tFactAutre > 0) l.RegleAutre = Reaffecter(tRegAutre, tFactAutre, ref dispo);
                if (tFactTva > 0) l.RegleTva = Reaffecter(tRegTva, tFactTva, ref dispo);
                if (tFactHt > 0) l.RegleHt = Reaffecter(tRegHt, tFactHt, ref dispo);
            }
        }
        else
        {
            // Un avoir (:342-388) : ce qui est regle au-dela du facture se retire du poste, puis se reaffecte.
            decimal aReaffecter = 0;
            if (tRegHt > tFactHt) { aReaffecter += tRegHt - tFactHt; l.RegleHt = tFactHt - tRegHt; }
            if (tRegTva > tFactTva) { aReaffecter += tRegTva - tFactTva; l.RegleTva = tFactTva - tRegTva; }
            if (tRegBodacc > tFactBodacc) { aReaffecter += tRegBodacc - tFactBodacc; l.RegleBodacc = tFactBodacc - tRegBodacc; }
            if (tRegInpi > tFactInpi) { aReaffecter += tRegInpi - tFactInpi; l.RegleInpi = tFactInpi - tRegInpi; }
            if (tRegAutre > tFactAutre) { aReaffecter += tRegAutre - tFactAutre; l.RegleAutre = tFactAutre - tRegAutre; }
            if (l.RegleBodacc == 0) l.RegleBodacc = Reaffecter(tRegBodacc, tFactBodacc, ref aReaffecter);
            if (l.RegleInpi == 0) l.RegleInpi = Reaffecter(tRegInpi, tFactInpi, ref aReaffecter);
            if (l.RegleAutre == 0) l.RegleAutre = Reaffecter(tRegAutre, tFactAutre, ref aReaffecter);
            if (l.RegleTva == 0) l.RegleTva = Reaffecter(tRegTva, tFactTva, ref aReaffecter);
            if (l.RegleHt == 0) l.RegleHt = Reaffecter(tRegHt, tFactHt, ref aReaffecter);
        }
        await InsererAsync(geste, client.Id, client.Type == "D" ? f.IdAffaire : null, f.Date, l, f.Numero, null, ct);
        deja.Add(l);
        return 1;
    }

    /// <summary>LettrerEncaissement (Lettrage.cs:64-134).</summary>
    private static async Task<int> LettrerEncaissementAsync(GesteEnCours geste, Client client, Ventil v, List<Ligne> deja, CancellationToken ct)
    {
        if (await ExisteAsync(geste, "SELECT 1 FROM v2.LETTRAGE WHERE LTTRG_ID_VENTI = @v", new SqlParameter("@v", v.Id), ct)) return 0;
        var l = new Ligne { MtEncaissement = v.Montant };
        decimal tFactHt = deja.Sum(x => x.FactHt), tFactTva = deja.Sum(x => x.FactTva), tFactBodacc = deja.Sum(x => x.FactBodacc),
                tFactInpi = deja.Sum(x => x.FactInpi), tFactAutre = deja.Sum(x => x.FactAutre);
        decimal tRegHt = deja.Sum(x => x.RegleHt), tRegTva = deja.Sum(x => x.RegleTva), tRegBodacc = deja.Sum(x => x.RegleBodacc),
                tRegInpi = deja.Sum(x => x.RegleInpi), tRegAutre = deja.Sum(x => x.RegleAutre);
        var aAffecter = v.Montant;
        if (aAffecter > 0)
        {
            if (tFactBodacc > 0) l.RegleBodacc = Reaffecter(tRegBodacc, tFactBodacc, ref aAffecter);
            if (tFactInpi > 0) l.RegleInpi = Reaffecter(tRegInpi, tFactInpi, ref aAffecter);
            if (tFactAutre > 0) l.RegleAutre = Reaffecter(tRegAutre, tFactAutre, ref aAffecter);
            if (tFactTva > 0) l.RegleTva = Reaffecter(tRegTva, tFactTva, ref aAffecter);
            if (tFactHt > 0) l.RegleHt = Reaffecter(tRegHt, tFactHt, ref aAffecter);
        }
        else
        {
            // Un encaissement negatif annule dans l'ordre inverse, avec les gardes de la V2 telles quelles (:122-128).
            if (tRegHt > 0) l.RegleHt = Annuler(tRegHt, ref aAffecter);
            if (tFactTva > 0) l.RegleTva = Annuler(tRegTva, ref aAffecter);
            if (tFactAutre > 0) l.RegleAutre = Annuler(tRegAutre, ref aAffecter);
            if (tFactInpi > 0) l.RegleInpi = Annuler(tRegInpi, ref aAffecter);
            if (tFactBodacc > 0) l.RegleBodacc = Annuler(tRegBodacc, ref aAffecter);
        }
        await InsererAsync(geste, client.Id, client.Type == "D" ? v.IdAffaire : null, v.DateEncaissement, l, null, v.Id, ct);
        deja.Add(l);
        return 1;
    }

    /// <summary>calculMontantReaffect (Lettrage.cs:639-654).</summary>
    private static decimal Reaffecter(decimal totalRegle, decimal totalFacture, ref decimal aReaffecter)
    {
        if (aReaffecter == 0 || totalRegle >= totalFacture) return 0;
        var du = totalFacture - totalRegle;
        if (aReaffecter > du) { aReaffecter -= du; return du; }
        var montant = aReaffecter;
        aReaffecter = 0;
        return montant;
    }

    /// <summary>calculMontantAnnul (Lettrage.cs:656-671).</summary>
    private static decimal Annuler(decimal totalRegle, ref decimal aAffecter)
    {
        if (aAffecter == 0 || totalRegle <= 0) return 0;
        if (Math.Abs(aAffecter) > totalRegle) { aAffecter -= totalRegle; return -totalRegle; }
        var montant = aAffecter;
        aAffecter = 0;
        return montant;
    }

    private static Task<int> InsererAsync(GesteEnCours geste, int client, int? affaire, DateTime date, Ligne l, string? facture, int? ventilation, CancellationToken ct)
        => geste.InsererAsync("v2.LETTRAGE", "LTTRG_ID_LTTRG", new Dictionary<string, object?>
        {
            ["LTTRG_ID_AFFRE"] = affaire, ["LTTRG_ID_CLIEN"] = client, ["LTTRG_DATE"] = date,
            ["LTTRG_MT_FACTURE"] = l.MtFacture, ["LTTRG_MT_ENCAISSEMENT"] = l.MtEncaissement,
            ["LTTRG_FACT_HT"] = l.FactHt, ["LTTRG_FACT_TVA"] = l.FactTva, ["LTTRG_FACT_BODACC"] = l.FactBodacc,
            ["LTTRG_FACT_INPI"] = l.FactInpi, ["LTTRG_FACT_AUTRE"] = l.FactAutre,
            ["LTTRG_REGLE_HT"] = l.RegleHt, ["LTTRG_REGLE_TVA"] = l.RegleTva, ["LTTRG_REGLE_BODACC"] = l.RegleBodacc,
            ["LTTRG_REGLE_INPI"] = l.RegleInpi, ["LTTRG_REGLE_AUTRE"] = l.RegleAutre,
            ["LTTRG_NUMERO_FACTURE"] = facture, ["LTTRG_ID_VENTI"] = ventilation,
        }, ct);
}
