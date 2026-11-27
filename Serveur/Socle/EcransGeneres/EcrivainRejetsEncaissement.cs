using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Rejets d'encaissement » (PROC_REJET) : CONFIRMER le rejet d'UN cheque, puis ce que la gestion commerciale de RIG v2 fait de
/// l'encaissement rejete. Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurRejetsEncaissement.cs. Plan « RIG v3 100 %
/// fonctionnel », lot 12, 27/11/2026.
///
/// SOURCE (ancien RIG), dans l'ordre :
///   1. le menu « Rejeter cet encaissement » (PROC_GCOM\...\MenuEncaissement.cs:156-163) : un cheque (« Cet encaissement n'est pas
///      un chèque »), pas une provision (« Impossible de rejeter un encaissement provision »), aucun client ventile en sommeil
///      (CLIEN_STATUT = 'S', :198-201 : « Il y a au moins un client ventilé en sommeil ») ; le droit « expert comptable » n'est
///      pas encore exige par la V3 (meme reserve que tout ecran genere, EcrivainEcran.cs) ;
///   2. PROC_REJET (D:\RigApplication\Source\RIG\DLL\Processus\PROC_REJET ; Detail OPE de proc_rejet.md) : l'encaissement
///      (« Encaissement #N manque dans la base de données. Fin d'exécution. », PROC_REJET.cs:45), le destinataire pre-rempli par le
///      client de la PREMIERE ventilation (OPE_INFORMATIONS_REJET.cs:31-42 : nom et prenom, e-mail, adresse en lignes separees par
///      « | »), le motif OBLIGATOIRE (:51-53 : « Le motif de rejet est vide ») ; ecrit ENCAISSEMENT_RECUS_REJETES
///      (EncaissementRecuRejete.CreateFromEncaissement : titre, date, montant, payeur, banque ; date de creation = maintenant) ;
///   3. au retour (RejectEncaissementAfterProc, MenuEncaissement.cs:381-390) : NON comptabilise -> SUPPRIME (DoDeleteEncaissement,
///      :252-322 : refuse si une affaire ventilee est verrouillee, au message de la V2 ; ses ventilations puis l'encaissement) ;
///      comptabilise -> ANNULE (DoCancelEncaissement -> GCOM_Extensions.AnnulerEncaissement, GCOM_Extensions.cs:44-71) : un
///      encaissement OPPOSE en mode REG (meme payeur, banque, titre, origine, arrivee ; montant negatif ; date = maintenant ;
///      commentaire « Annulation de l'encaissement n° T du jj/mm/aaaa »), une ventilation negative par ventilation d'origine
///      (meme client, affaire, tiers payeur, date TPG), et les ventilations du client d'attente (compte 99999999) passees au client
///      « Client Divers Special ANNULATION » (Client.cs:176-215). Le solde du client et de l'affaire retombe a zero sur ce cheque.
/// La V3 fait les trois dans UNE transaction : un refus n'ecrit rien (la V2 gardait le rejet si la suppression echouait ensuite).
/// NON REPRIS (nomme) : la lettre de rejet (etat Crystal Expresso_Rejet.rpt, PDF, archivage GED, demande d'edition CREJET), la
/// demande REJET cloturee et la demande ENCAISS de la suppression (des traces : le journal des gestes les remplace) ; refuses car
/// non repris : un encaissement d'origine NTF (le lien de notification remis a zero, :267-269) ou rattache a un reversement INPI
/// (:271-283).
/// </summary>
internal sealed class EcrivainRejetsEncaissement : IEcrivainEcran
{
    public string Procedure => "PROC_REJET";
    public string Ecran => "Rejets d'encaissement";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Confirmer];

    /// <summary>Les sections et champs du descripteur PROC_REJET (Facturer.cs), au mot pres.</summary>
    internal const string SectionDestinataire = "Destinataire", SectionMotif = "Motif";
    internal const string ChampContact = "Contact", ChampAdresse = "Adresse", ChampEmail = "E-mail", ChampMotif = "Motif du rejet";

    private sealed record Encaissement(int Id, decimal Montant, DateTime Date, string? Payeur, string? Titre, string? Mode, string? Banque,
        DateTime? Comptabilise, string? Origine, int? Arrivee, string? CompteRemis, string? Guid, int? IdAdresseRecu, string? Type);

    private sealed record Ventilation(int Id, int? Client, int? Affaire, int? Payeur, decimal Montant, DateTime? DateTpg);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids.Length != 1 || !int.TryParse(geste.Demande.Ids[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser("Un rejet se fait sur un seul encaissement : RIG v2 l'ouvre depuis l'encaissement choisi.");
        Encaissement? e = null;
        await using (var r = await geste.LireAsync("""
            SELECT e.ENCMT_ID_ENCMT, e.ENCMT_MONTANT, e.ENCMT_DATE_ENCAISSEMENT, e.ENCMT_DESIGNATION_PAYEUR, e.ENCMT_NUMERO_TITRE, e.ENCMT_MODE_PAIEMENT,
                   e.ENCMT_DESIGNATION_BANQUE, e.ENCMT_DATE_COMPTABILISATION, e.ENCMT_ORIGINE, e.ENCMT_ID_ARRV, e.ENCMT_COMPTE_BANCAIRE_REMIS, e.ENCMT_GUID,
                   e.ENCMT_RECU_ID_ADRBS, mp.R_MODEPAIEME_TYPE
            FROM v2.ENCAISSEMENT AS e WITH (UPDLOCK, HOLDLOCK)
            LEFT JOIN v2.MODE_PAIEMENT AS mp ON mp.R_MODEPAIEME_VALEUR = e.ENCMT_MODE_PAIEMENT
            WHERE e.ENCMT_ID_ENCMT = @id
            """, ct, new SqlParameter("@id", id)))
            if (await r.ReadAsync(ct))
            {
                string? T(string c) => FormeLigne.Texte(r, c) is { Length: > 0 } t ? t : null;
                e = new Encaissement(id, CriteresConsultation.Decimal(r, "ENCMT_MONTANT") ?? 0m, r.GetDateTime(2), T("ENCMT_DESIGNATION_PAYEUR"),
                    T("ENCMT_NUMERO_TITRE"), T("ENCMT_MODE_PAIEMENT"), T("ENCMT_DESIGNATION_BANQUE"), FormeLigne.DateDe(r, "ENCMT_DATE_COMPTABILISATION"),
                    T("ENCMT_ORIGINE"), CriteresConsultation.EntierDe(r, "ENCMT_ID_ARRV"), T("ENCMT_COMPTE_BANCAIRE_REMIS"), T("ENCMT_GUID"),
                    CriteresConsultation.EntierDe(r, "ENCMT_RECU_ID_ADRBS"), T("R_MODEPAIEME_TYPE"));
            }
        if (e is null) return IssueGeste.Refuser($"Encaissement #{id} manque dans la base de données. Fin d'exécution.");
        if (e.Type != "CH") return IssueGeste.Refuser("Cet encaissement n'est pas un chèque");
        if (e.Mode == "PV") return IssueGeste.Refuser("Impossible de rejeter un encaissement provision");

        var ventilations = new List<Ventilation>();
        await using (var r = await geste.LireAsync("""
            SELECT VENTI_ID_VENTI, VENTI_ID_CLIEN, VENTI_ID_AFFRE, VENTI_ID_PYEUR, VENTI_MONTANT, VENTI_DATE_DEMANDE_TPG
            FROM v2.VENTILATION WITH (UPDLOCK, HOLDLOCK) WHERE VENTI_ID_ENCMT = @id ORDER BY VENTI_ID_VENTI
            """, ct, new SqlParameter("@id", id)))
            while (await r.ReadAsync(ct))
                ventilations.Add(new Ventilation(r.GetInt32(0), CriteresConsultation.EntierDe(r, "VENTI_ID_CLIEN"), CriteresConsultation.EntierDe(r, "VENTI_ID_AFFRE"),
                    CriteresConsultation.EntierDe(r, "VENTI_ID_PYEUR"), CriteresConsultation.Decimal(r, "VENTI_MONTANT") ?? 0m, FormeLigne.DateDe(r, "VENTI_DATE_DEMANDE_TPG")));
        var clients = ventilations.Where(v => v.Client is not null).Select(v => v.Client!.Value).Distinct().ToList();
        if (clients.Count > 0)
        {
            await using var r = await geste.LireAsync($"SELECT COUNT(*) FROM rec.Client WHERE Id IN ({string.Join(", ", clients)}) AND Statut = 'S'", ct);
            await r.ReadAsync(ct);
            if (r.GetInt32(0) > 0) return IssueGeste.Refuser("Il y a au moins un client ventilé en sommeil");
        }

        // Le destinataire : ce que l'ecran envoie, sinon le client de la premiere ventilation (OPE_INFORMATIONS_REJET.cs:31-42).
        var motif = CreationV2.Saisi(geste, SectionMotif, ChampMotif);
        if (motif.Length == 0) return IssueGeste.Refuser("Le motif de rejet est vide");
        if (motif.Length > 150) return IssueGeste.Refuser($"« {SectionMotif} › {ChampMotif} » : 150 caractères au plus.");
        string? contact = null, email = null, adresse = null;
        if (ventilations.FirstOrDefault()?.Client is { } premier)
            await using (var r = await geste.LireAsync("""
                SELECT TOP (1) c.Nom, c.Prenom, c.Email, a.Id AS IdAdresse, a.PreAdresse, a.NumeroVoie, a.TypeVoie, a.NomVoie, a.SuiteAdresse, a.CodePostal,
                       a.Localite, ca.LigneAdresse AS LigneCa, ca.SuiteAdresse AS SuiteCa, ca.CodePostal AS CpCa, ca.Localite AS LocaliteCa
                FROM rec.Client AS c
                LEFT JOIN rec.ClientAdresse AS ca ON ca.IdClient = c.Id
                LEFT JOIN rec.Adresse AS a ON a.Id = ca.IdAdresse
                WHERE c.Id = @c ORDER BY ca.ParDefaut DESC, ca.Id
                """, ct, new SqlParameter("@c", premier)))
                if (await r.ReadAsync(ct))
                {
                    contact = FormeLigne.Joindre(FormeLigne.Texte(r, "Nom"), FormeLigne.Texte(r, "Prenom")).Replace(" · ", " ", StringComparison.Ordinal);
                    email = FormeLigne.Texte(r, "Email");
                    var voie = string.Join(" ", new[] { "NumeroVoie", "TypeVoie", "NomVoie" }.Select(c => FormeLigne.Texte(r, c)).Where(m => m.Length > 0));
                    var ville = string.Join(" ", new[] { "CodePostal", "Localite" }.Select(c => FormeLigne.Texte(r, c)).Where(m => m.Length > 0));
                    // L adresse de base du client (AdresseMultiLignesPourEcran) ; a defaut, les lignes de son adresse client.
                    adresse = r["IdAdresse"] is DBNull
                        ? string.Join("|", new[] { FormeLigne.Texte(r, "LigneCa"), FormeLigne.Texte(r, "SuiteCa"),
                              string.Join(" ", new[] { FormeLigne.Texte(r, "CpCa"), FormeLigne.Texte(r, "LocaliteCa") }.Where(m => m.Length > 0)) }.Where(m => m.Length > 0))
                        : string.Join("|", new[] { FormeLigne.Texte(r, "PreAdresse"), voie, FormeLigne.Texte(r, "SuiteAdresse"), ville }.Where(m => m.Length > 0));
                }
        string? Saisi(string champ, string? defaut) => CreationV2.Saisi(geste, SectionDestinataire, champ) is { Length: > 0 } s ? s : defaut is { Length: > 0 } ? defaut : null;
        contact = Saisi(ChampContact, contact);
        email = Saisi(ChampEmail, email);
        adresse = Saisi(ChampAdresse, adresse)?.Replace("\r\n", "|", StringComparison.Ordinal).Replace("\n", "|", StringComparison.Ordinal);

        // Les controles de la suite de la V2 (avant toute ecriture : un refus n'ecrit rien).
        if (e.Comptabilise is null)
        {
            await using (var r = await geste.LireAsync($"""
                SELECT TOP (1) a.AFFRE_ID_AFFRE, a.AFFRE_ID_UTLST_EN_COURS, c.Civilite, c.Prenom, c.Nom
                FROM v2.AFFAIRE AS a LEFT JOIN soc.Compte AS c ON c.IdV2 = a.AFFRE_ID_UTLST_EN_COURS
                WHERE a.AFFRE_ID_AFFRE IN ({string.Join(", ", ventilations.Where(v => v.Affaire is not null).Select(v => v.Affaire!.Value).DefaultIfEmpty(0))})
                  AND a.AFFRE_ID_UTLST_EN_COURS IS NOT NULL ORDER BY a.AFFRE_ID_AFFRE
                """, ct))
                if (await r.ReadAsync(ct))
                {
                    var qui = string.Join(" ", new[] { "Nom", "Prenom" }.Select(c => FormeLigne.Texte(r, c)).Where(m => m.Length > 0));
                    return IssueGeste.Refuser($"Impossible de supprimer l'encaissement car l'affaire numéro {r.GetInt32(0)} est verouillée par l'utilisateur " +
                                              $"{(qui.Length > 0 ? qui : "n° " + FormeLigne.Texte(r, "AFFRE_ID_UTLST_EN_COURS"))}.");
                }
            if (e.Origine == "NTF")
                return IssueGeste.Refuser("Cet encaissement vient d'une notification : RIG v2 remet alors à zéro son lien de notification, ce que la V3 ne fait pas encore. Rien n'est enregistré.");
            await using (var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.INPI_REVERSEMENTS WHERE INPIRV_ID_ENCMT = @id", ct, new SqlParameter("@id", id)))
            {
                await r.ReadAsync(ct);
                if (r.GetInt32(0) > 0)
                    return IssueGeste.Refuser("Cet encaissement est rattaché à un reversement INPI : RIG v2 l'en détache avant de le supprimer, ce que la V3 ne fait pas encore. Rien n'est enregistré.");
            }
        }

        var rejet = await geste.InsererAsync("v2.ENCAISSEMENT_RECUS_REJETES", "ENCRR_ID_ENCRR", new Dictionary<string, object?>
        {
            ["ENCRR_NUM_TITRE"] = e.Titre, ["ENCRR_DATE_CREATION"] = geste.Maintenant, ["ENCRR_DATE_ENCMT"] = e.Date, ["ENCRR_MONTANT"] = e.Montant,
            ["ENCRR_RAISON_REJET"] = motif, ["ENCRR_DESIGNATION_BANQUE"] = e.Banque, ["ENCRR_DESIGNATION_PAYEUR"] = e.Payeur,
            ["ENCRR_CLIENT"] = Couper(contact, 255), ["ENCRR_CLIENT_ADRESSE"] = Couper(adresse, 255), ["ENCRR_CLIENT_EMAIL"] = Couper(email, 255),
        }, ct);
        var traces = new List<TraceGeste>
        {
            new("encaissement", $"le rejet n° {rejet} du chèque {e.Titre ?? id.ToString(CultureInfo.InvariantCulture)}", rejet.ToString(CultureInfo.InvariantCulture),
                "a cree", null, new { Encaissement = id, e.Montant, Motif = motif, Contact = contact, Email = email, Adresse = adresse }),
        };
        string suite;
        if (e.Comptabilise is null)
        {
            await geste.EcrireAsync("DELETE FROM v2.VENTILATION WHERE VENTI_ID_ENCMT = @id", ct, new SqlParameter("@id", id));
            await geste.EcrireAsync("DELETE FROM v2.ENCAISSEMENT WHERE ENCMT_ID_ENCMT = @id", ct, new SqlParameter("@id", id));
            traces.Add(new TraceGeste("encaissement", $"l'encaissement n° {id}", id.ToString(CultureInfo.InvariantCulture), "a supprime",
                new { e.Montant, e.Payeur, Date = e.Date, Ventilations = ventilations.Select(v => new { v.Id, v.Client, v.Affaire, v.Montant }) }, null));
            suite = $"l'encaissement n° {id}, non comptabilisé, est supprimé " + ventilations.Count switch
            {
                0 => "(il n'avait pas de ventilation)", 1 => "avec sa ventilation", var n => $"avec ses {n} ventilations",
            };
        }
        else
        {
            int? attente = null, annulation = null, agent = null;
            await using (var r = await geste.LireAsync("""
                SELECT (SELECT TOP (1) Id FROM rec.Client WHERE NumeroCompte = '99999999' ORDER BY Id),
                       (SELECT TOP (1) Id FROM rec.Client WHERE Nom = 'Client Divers Special ANNULATION' AND Type = 'D' ORDER BY Id),
                       (SELECT IdV2 FROM soc.Compte WHERE Id = @agent)
                """, ct, new SqlParameter("@agent", geste.Agent.Id)))
            {
                await r.ReadAsync(ct);
                attente = r.IsDBNull(0) ? null : r.GetInt32(0);
                annulation = r.IsDBNull(1) ? null : r.GetInt32(1);
                agent = r.IsDBNull(2) ? null : Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture);
            }
            if (attente is { } cliAttente && ventilations.Any(v => v.Client == cliAttente) && annulation is null)
                return IssueGeste.Refuser("Le client « Client Divers Special ANNULATION » manque dans la base du greffe : l'annulation de l'encaissement (ventilé au client d'attente) ne peut pas se faire. Rien n'est enregistré.");
            var oppose = await geste.InsererAsync("v2.ENCAISSEMENT", "ENCMT_ID_ENCMT", new Dictionary<string, object?>
            {
                ["ENCMT_MONTANT"] = -e.Montant, ["ENCMT_DATE_ENCAISSEMENT"] = geste.Maintenant, ["ENCMT_ID_ARRV"] = e.Arrivee,
                ["ENCMT_COMMENTAIRE"] = $"Annulation de l'encaissement {(e.Titre is null ? "" : "n° " + e.Titre)} du {FormeLigne.Date(e.Date)}",
                ["ENCMT_COMPTE_BANCAIRE_REMIS"] = e.CompteRemis, ["ENCMT_DESIGNATION_BANQUE"] = e.Banque, ["ENCMT_DESIGNATION_PAYEUR"] = e.Payeur,
                ["ENCMT_GUID"] = e.Guid, ["ENCMT_MODE_PAIEMENT"] = "REG", ["ENCMT_NUMERO_TITRE"] = e.Titre, ["ENCMT_ORIGINE"] = e.Origine,
                ["ENCMT_RECU_ID_ADRBS"] = e.IdAdresseRecu, ["ENCMT_ID_UTLST"] = agent,
            }, ct);
            if (attente is { } a && annulation is { } b)
                await geste.EcrireAsync("UPDATE v2.VENTILATION SET VENTI_ID_CLIEN = @annul WHERE VENTI_ID_ENCMT = @id AND VENTI_ID_CLIEN = @attente", ct,
                    new SqlParameter("@annul", b), new SqlParameter("@id", id), new SqlParameter("@attente", a));
            foreach (var v in ventilations)
                await geste.InsererAsync("v2.VENTILATION", "VENTI_ID_VENTI", new Dictionary<string, object?>
                {
                    ["VENTI_MONTANT"] = -v.Montant, ["VENTI_DATE_VENTILATION"] = geste.Maintenant,
                    ["VENTI_ID_CLIEN"] = v.Client == attente && annulation is not null ? annulation : v.Client, ["VENTI_DATE_DEMANDE_TPG"] = v.DateTpg,
                    ["VENTI_ID_AFFRE"] = v.Affaire, ["VENTI_ID_ENCMT"] = oppose, ["VENTI_ID_PYEUR"] = v.Payeur,
                }, ct);
            traces.Add(new TraceGeste("encaissement", $"l'encaissement n° {oppose}", oppose.ToString(CultureInfo.InvariantCulture), "a cree", null,
                new { Annule = id, Montant = -e.Montant, Mode = "REG", Ventilations = ventilations.Count }));
            suite = $"l'encaissement n° {id}, comptabilisé, est annulé par l'encaissement n° {oppose} de {CriteresConsultation.Euros(-e.Montant)} " +
                    $"et {ventilations.Count} ventilation{(ventilations.Count > 1 ? "s" : "")} opposée{(ventilations.Count > 1 ? "s" : "")}";
        }
        return IssueGeste.Ecrit($"Rejet n° {rejet} du chèque {e.Titre ?? "sans numéro"} ({CriteresConsultation.Euros(e.Montant)}) enregistré dans la base du greffe ; " +
                                $"{suite}. La lettre de rejet n'est pas éditée par la V3.",
            [rejet.ToString(CultureInfo.InvariantCulture)], [.. traces]);
    }

    private static string? Couper(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];
}
