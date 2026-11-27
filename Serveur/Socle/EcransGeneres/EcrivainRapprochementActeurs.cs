using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurRapprochementActeurs;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Rapprocher des acteurs » (PROC_RAPPACTEUR) : CONFIRMER la fusion -- les acteurs coches (« a supprimer ») sont remplaces partout
/// par l'acteur garde, puis supprimes. Mode d'emploi : EcrivainEcran.cs ; les lignes et l'apercu : LecteurRapprochementActeurs.cs.
/// Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026.
///
/// SOURCE (ancien RIG) : OPE_SELECTION_ACTEUR de PROC_RAPPACTEUR, _VerifierMandataires et _DeleteMandataires
/// (Operations\Mandataire\OPE_SELECTION_ACTEUR.Mandataires.cs:188-372), _SaveEdiLot2 et _CreerDemande (OPE_SELECTION_ACTEUR.cs:262-325).
/// Pour chaque mandataire coche, dans UNE transaction (la V2 : « UpdateLink »+type) :
///   1. ses liaisons (Mandataire.GetLiaisonEntite, LiaisonsMandataire.cs) passent a l'acteur garde, table par table, dans l'ordre de la
///      V2, et la trace SQL de la V2 est composee comme elle (ActeurSupp.GetSqlUpdate + CloseIN, ActeurSupp.cs:162-177 : « update
///      PARTIE set PRT_ID_MNDTR_POS = 12 where PRT_ID_PRT in (1, 2) ») ; sans liaison, « Aucune mise à jour à faire » (Res.cs:37) ;
///   2. la piste d'audit ACTEUR_SUPP : table MANDATAIRE, id supprime, id garde, la trace, la date, l'auteur, les deux noms
///      (ActeurSupp.CreateActeurSupp, .Mandataires.cs:140-142 ; ActeurSupp.cs:72-100) ;
///   3. les destinataires d'edition des affaires ou il etait avocat (DESTINATAIRES_EDITIONS_LOT2.DESED_XML_DESTEDIT) : chaque ENTITE
///      CODE="MANDATAIRE" VALEUR=&lt;supprime&gt; prend VALEUR=&lt;garde&gt; (_SaveEdiLot2, OPE_SELECTION_ACTEUR.cs:262-295 ; le chemin
///      PARTIE -> instance -> destinataires, .Mandataires.cs:417-430) ;
///   4. PUIS le mandataire est supprime (m.Delete(cnx), .Mandataires.cs:149).
/// CONTROLES, avant toute ecriture : au moins un acteur coche (« Il faut sélectionner au moins un mandataire à supprimer dans la
/// grille », Res.cs:33) ; aucun mandataire actif qui a un numero CNBF (Res.cs:35, au mot pres) ; un acteur garde, du meme type, qui
/// existe et qui n'est pas coche ; la V2 ne garde qu'un mandataire actif repertorie (le filtre « ACTIF » de la liste « a garder » :
/// MNDTR_ACTIF = 1 et MNDTR_ABREGE renseigne, _RechercherMandatairesSQL, .Mandataires.cs:166-176).
/// L'acteur garde se designe par son numero (l'id de sa ligne, « M-12 » ou « 12 ») ou son abrege, dans « Acteur à garder ».
/// NON REPRIS (nommes) : la fusion des JUGES (12 tables et le nettoyage des instances et decisions, .Juges.cs:194-460) et des
/// MANDATAIRES PC (7 tables et la regeneration des editions de la procedure, .MandatairesPC.cs:208-330) -- refusee, l'ecran le dit ;
/// le document Word recapitulatif (Aspose, _CreerDocumentWord, OPE_SELECTION_ACTEUR.cs:348) dans le repertoire commun du registre ;
/// la demande « Rapprochement acteur » creee et cloturee d'un coup (_CreerDemande) -- une trace, que le journal des gestes remplace
/// (decision du plan) ; l'acteur garde actif non repertorie (la V2 ne l'offre pas, la V3 le refuse).
/// TRACE : famille « reglage », « a supprime » par mandataire fusionne (avant : son nom et ses liaisons ; apres : l'acteur garde).
/// </summary>
internal sealed class EcrivainRapprochementActeurs : IEcrivainEcran
{
    public string Procedure => "PROC_RAPPACTEUR";
    public string Ecran => "Rapprocher des acteurs";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Confirmer];

    internal const string SectionGarde = "Acteur à garder", ChampGarde = "Acteur à garder";
    /// <summary>Res.cs:33, {0} = « mandataire ».</summary>
    internal const string SelectionnezSvp = "Il faut sélectionner au moins un mandataire à supprimer dans la grille";
    /// <summary>Res.cs:37.</summary>
    internal const string AucuneMiseAJour = "Aucune mise à jour à faire";

    /// <summary>Pourquoi la fusion d'un juge ou d'un mandataire PC n'est pas ecrite par RIG v3.</summary>
    internal static string NonRepris(string groupe) => groupe == Juges
        ? "La fusion des juges (sièges, audiences, instances, décisions, mesures… : 12 tables, PROC_RAPPACTEUR) n'est pas encore reprise dans RIG v3."
        : "La fusion des mandataires PC (gestion des PC, dépôts, payeurs… et la régénération des éditions de la procédure, PROC_RAPPACTEUR) n'est pas encore reprise dans RIG v3.";

    private sealed record Mandataire(int Id, string Nom, string Abrege, string Cnbf, bool Actif);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var choisis = geste.Demande.Ids.Select(i => (Id: i, Lu: LecteurRapprochementActeurs.Lire(i))).ToArray();
        if (choisis.Length == 0) return IssueGeste.Refuser(SelectionnezSvp);
        if (choisis.Where(c => c.Lu is null).Select(c => c.Id).FirstOrDefault() is { } inconnu)
            return IssueGeste.Refuser($"Acteur introuvable dans la base du greffe ({inconnu}).");
        var groupes = choisis.Select(c => c.Lu!.Value.Groupe).Distinct().ToArray();
        if (groupes.Length > 1) return IssueGeste.Refuser("Un seul type d'acteur à la fois : juges, mandataires ou mandataires PC (RIG v2).");
        if (groupes[0] != Mandataires) return IssueGeste.Refuser(NonRepris(groupes[0]));

        var supprimes = new List<Mandataire>();
        foreach (var (id, lu) in choisis)
            supprimes.Add(await LireAsync(geste, lu!.Value.Id, ct) ?? throw new InvalidOperationException($"Mandataire {id} lu puis disparu."));
        // _VerifierMandataires (.Mandataires.cs:194-202) : un message par mandataire actif a numero CNBF.
        if (supprimes.FirstOrDefault(m => m.Actif && m.Cnbf.Length > 0) is { } protege)
            return IssueGeste.Refuser(string.Format(CultureInfo.InvariantCulture, EcrivainMandataires.ActifAvecCnbf, protege.Nom));

        var saisie = geste.Valeur(SectionGarde, ChampGarde)?.Trim() ?? "";
        if (saisie.Length == 0) return IssueGeste.Refuser($"« {ChampGarde} » est obligatoire : le numéro ou l'abrégé du mandataire qui reprend les affaires.");
        var garde = await GardeAsync(geste, saisie, ct);
        if (garde is null) return IssueGeste.Refuser($"« {ChampGarde} » : aucun mandataire ne porte le numéro ou l'abrégé « {saisie} ».");
        if (supprimes.Any(m => m.Id == garde.Id)) return IssueGeste.Refuser($"{garde.Nom} ne peut pas être à la fois gardé et supprimé.");
        if (!garde.Actif || garde.Abrege.Length == 0)
            return IssueGeste.Refuser($"{garde.Nom} ne peut pas être gardé : RIG v2 ne garde qu'un mandataire actif et répertorié (avec un abrégé).");

        var auteur = await AuteurAsync(geste, ct);
        var traces = new List<TraceGeste>();
        foreach (var m in supprimes)
        {
            // Les affaires ou il est avocat, AVANT la reprise des parties (le chemin de _RecupIdDestEditLot2Mandataire).
            var instances = new List<int>();
            await using (var r = await geste.LireAsync("SELECT DISTINCT PRT_ID_INSTN FROM v2.PARTIE WHERE PRT_ID_MNDTR_POS = @id OR PRT_ID_MNDTR_PLAI = @id",
                             ct, new SqlParameter("@id", m.Id)))
                while (await r.ReadAsync(ct)) instances.Add(r.GetInt32(0));

            var requete = new StringBuilder();
            var reprises = new List<string>();
            foreach (var liaison in LiaisonsMandataire.Toutes)
            {
                var ids = new List<string>();
                await using (var r = await geste.LireAsync($"SELECT {liaison.Cle} FROM v2.{liaison.Table} WHERE {liaison.Condition} ORDER BY {liaison.Cle}",
                                 ct, new SqlParameter("@id", m.Id)))
                    while (await r.ReadAsync(ct)) ids.Add(Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture)!);
                if (ids.Count == 0) continue;
                await geste.EcrireAsync($"UPDATE v2.{liaison.Table} SET {liaison.Colonne} = @garde WHERE {liaison.Condition}", ct,
                    new SqlParameter("@garde", garde.Id), new SqlParameter("@id", m.Id));
                // ActeurSupp.GetSqlUpdate + CloseIN (ActeurSupp.cs:162-177), le texte de la V2.
                requete.Append(CultureInfo.InvariantCulture, $"update {liaison.Table} set {liaison.Colonne} = {garde.Id} where {liaison.Cle} in ({string.Join(", ", ids)})\r\n");
                reprises.Add($"{ids.Count} × {liaison.Libelle.ToLowerInvariant()}");
            }
            if (requete.Length == 0) requete.Append(AucuneMiseAJour);

            await geste.InsererAsync("v2.ACTEUR_SUPP", "ACTSUP_ID_ACTSUP", new Dictionary<string, object?>
            {
                ["ACTSUP_TABLE_SUPP"] = "MANDATAIRE", ["ACTSUP_ID_SUPP"] = m.Id, ["ACTSUP_ID_REMPL"] = garde.Id, ["ACTSUP_REQUETE"] = requete.ToString(),
                ["ACTSUP_DATE_CREATION"] = geste.Maintenant, ["ACTSUP_AUTEUR"] = auteur, ["ACTSUP_SUPP_DESIGNATION"] = NomAbrege(m),
                ["ACTSUP_REMPL_DESIGNATION"] = NomAbrege(garde),
            }, ct);
            await DestinatairesEditionAsync(geste, instances, m.Id, garde.Id, ct);
            await geste.EcrireAsync("DELETE FROM v2.MANDATAIRE WHERE MNDTR_ID_MNDTR = @id", ct, new SqlParameter("@id", m.Id));
            traces.Add(new TraceGeste("reglage", $"le mandataire {m.Nom}", m.Id.ToString(CultureInfo.InvariantCulture), "a supprime",
                new { Mandataire = NomAbrege(m), Liaisons = reprises }, new { RemplacePar = NomAbrege(garde), IdGarde = garde.Id }));
        }
        var noms = string.Join(", ", supprimes.Select(NomAbrege));
        return IssueGeste.Ecrit($"{noms} {(supprimes.Count > 1 ? "ont été remplacés" : "a été remplacé")} par {NomAbrege(garde)} : " +
                                $"{(traces.Count > 1 ? "leurs affaires lui sont reportées" : "ses affaires lui sont reportées")}.",
            [.. supprimes.Select(m => $"M-{m.Id}")], [.. traces]);
    }

    /// <summary>_NomCompletAbrege de la V2 : le nom, puis l'abrege entre parentheses s'il y en a un.</summary>
    private static string NomAbrege(Mandataire m) => m.Abrege.Length > 0 ? $"{m.Nom} ({m.Abrege})" : m.Nom;

    private static async Task<Mandataire?> LireAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT m.MNDTR_ID_MNDTR, m.MNDTR_CIVILITE, m.MNDTR_NOM, m.MNDTR_ABREGE, m.MNDTR_NUMERO_CNBF, m.MNDTR_ACTIF, c.R_CIVILITE_ABREGE_EDIT
            FROM v2.MANDATAIRE AS m WITH (UPDLOCK) LEFT JOIN v2.CIVILITE AS c ON c.R_CIVILITE_VALEUR = m.MNDTR_CIVILITE
            WHERE m.MNDTR_ID_MNDTR = @id
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        return new Mandataire(r.GetInt32(0),
            LecteurMandataires.NomComplet(FormeLigne.Texte(r, "MNDTR_CIVILITE"), FormeLigne.Texte(r, "R_CIVILITE_ABREGE_EDIT"), FormeLigne.Texte(r, "MNDTR_NOM")),
            FormeLigne.Texte(r, "MNDTR_ABREGE"), FormeLigne.Texte(r, "MNDTR_NUMERO_CNBF"), r.GetBoolean(r.GetOrdinal("MNDTR_ACTIF")));
    }

    /// <summary>Le mandataire garde : « M-12 », « 12 » ou un abrege.</summary>
    private static async Task<Mandataire?> GardeAsync(GesteEnCours geste, string saisie, CancellationToken ct)
    {
        var numero = LecteurRapprochementActeurs.Lire(saisie) is { Groupe: Mandataires } lu ? lu.Id : CriteresConsultation.Entier(saisie);
        if (numero is { } n && await LireAsync(geste, n, ct) is { } parNumero) return parNumero;
        int? id = null;
        await using (var r = await geste.LireAsync("SELECT TOP (1) MNDTR_ID_MNDTR FROM v2.MANDATAIRE WHERE MNDTR_ABREGE = @a ORDER BY MNDTR_ID_MNDTR", ct,
                         new SqlParameter("@a", saisie)))
            if (await r.ReadAsync(ct)) id = r.GetInt32(0);
        return id is { } trouve ? await LireAsync(geste, trouve, ct) : null;
    }

    /// <summary>L'auteur de la piste d'audit : l'utilisateur RIG v2 de l'agent (« Nom Prénom (ABREGE) », NomPrenomAbrege), sinon son compte.</summary>
    private static async Task<string> AuteurAsync(GesteEnCours geste, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT CONCAT(Nom, ' ', Prenom, ' (', Abrege, ')') FROM soc.Compte WHERE CompteWindows = @c", ct,
            new SqlParameter("@c", geste.Agent.CompteWindows));
        return await r.ReadAsync(ct) && !r.IsDBNull(0) ? r.GetString(0) : geste.Agent.CompteWindows;
    }

    /// <summary>_SaveEdiLot2 (OPE_SELECTION_ACTEUR.cs:262-295) : dans le XML des destinataires d'edition de chaque affaire, l'entite
    /// MANDATAIRE du supprime designe desormais le garde.</summary>
    private static async Task DestinatairesEditionAsync(GesteEnCours geste, List<int> instances, int supprime, int garde, CancellationToken ct)
    {
        if (instances.Count == 0) return;
        var aEcrire = new List<(int Id, string Xml)>();
        await using (var r = await geste.LireAsync($"""
                         SELECT DESED_ID_DESED, DESED_XML_DESTEDIT FROM v2.DESTINATAIRES_EDITIONS_LOT2
                         WHERE DESED_ID_INSTN IN ({string.Join(", ", instances)}) AND DESED_XML_DESTEDIT IS NOT NULL
                         """, ct))
        {
            while (await r.ReadAsync(ct))
            {
                var xml = XDocument.Parse(r.GetString(1));
                var entites = xml.Descendants().Where(e => (string?)e.Attribute("CODE") == "MANDATAIRE"
                                                           && (string?)e.Attribute("VALEUR") == supprime.ToString(CultureInfo.InvariantCulture)).ToList();
                if (entites.Count == 0) continue;
                foreach (var e in entites) e.SetAttributeValue("VALEUR", garde.ToString(CultureInfo.InvariantCulture));
                aEcrire.Add((r.GetInt32(0), xml.ToString(SaveOptions.DisableFormatting)));
            }
        }
        foreach (var (id, xml) in aEcrire)
            await geste.EcrireAsync("UPDATE v2.DESTINATAIRES_EDITIONS_LOT2 SET DESED_XML_DESTEDIT = @xml WHERE DESED_ID_DESED = @id", ct,
                new SqlParameter("@xml", xml), new SqlParameter("@id", id));
    }
}
