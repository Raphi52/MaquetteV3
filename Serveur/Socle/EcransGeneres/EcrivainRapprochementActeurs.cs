using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurRapprochementActeurs;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Rapprocher des acteurs » (PROC_RAPPACTEUR) : CONFIRMER la fusion -- les acteurs coches (« a supprimer ») sont remplaces partout
/// par l'acteur garde, puis supprimes. Les trois types de la V2 : mandataires, juges, mandataires de procedure collective. Mode d'emploi :
/// EcrivainEcran.cs ; les lignes et l'apercu : LecteurRapprochementActeurs.cs. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026.
///
/// SOURCE (ancien RIG) : OPE_SELECTION_ACTEUR de PROC_RAPPACTEUR (D:\RigApplication\Source\RIG\DLL\Processus\PROC_RAPPACTEUR\Operations) --
/// _VerifierMandataires / _DeleteMandataires (Mandataire\OPE_SELECTION_ACTEUR.Mandataires.cs:188-372), _VerifierJuges / _DeleteJuges
/// (Juge\OPE_SELECTION_ACTEUR.Juges.cs:174-460), _VerifierMandatairesPC / _DeleteMandatairesPC (Mandataire PC\
/// OPE_SELECTION_ACTEUR.MandatairesPC.cs:170-345), _SaveEdiLot2 et _CreerDemande (OPE_SELECTION_ACTEUR.cs:262-325).
/// Pour chaque acteur coche, dans UNE transaction (la V2 : « UpdateLink »+type) :
///   1. ses liaisons passent a l'acteur garde, table par table, dans l'ordre de la V2 pour ce type, et la trace SQL de la V2 est
///      composee comme elle (ActeurSupp.GetSqlUpdate + CloseIN, ActeurSupp.cs:162-177 : « update PARTIE set PRT_ID_MNDTR_POS = 12 where
///      PRT_ID_PRT in (1, 2) ») ; sans liaison, « Aucune mise à jour à faire » (Res.cs:37). Les liaisons : LiaisonsMandataire.cs
///      (Mandataire.GetLiaisonEntite) ; LiaisonsActeur.cs pour un juge et un mandataire PC (Juge.GetLiaisonEntite, MandatairePC.
///      GetLiaisonEntite), dans l'ordre de _DeleteJuges (.Juges.cs:213-417) et de _DeleteMandatairesPC (.MandatairesPC.cs:205-320) ;
///   2. la piste d'audit ACTEUR_SUPP : la table (MANDATAIRE, JUGE, MANDATAIRE_PC), l'id supprime, l'id garde, la trace, la date,
///      l'auteur, les deux noms (ActeurSupp.CreateActeurSupp, .Mandataires.cs:140-142, .Juges.cs:424-428, .MandatairesPC.cs:328-331) ;
///   3. les destinataires d'edition (DESTINATAIRES_EDITIONS_LOT2.DESED_XML_DESTEDIT) : chaque ENTITE CODE=&lt;type&gt; VALEUR=&lt;supprime&gt;
///      prend VALEUR=&lt;garde&gt; (_SaveEdiLot2, OPE_SELECTION_ACTEUR.cs:262-295). Lesquels : les affaires ou le mandataire est avocat
///      (PARTIE -> instance, .Mandataires.cs:417-430) ; les PC et preventions dont le juge est juge-commissaire (ETRE_JUGE_COMM,
///      _RecupIdDestEditLot2Juge, .Juges.cs:466-480) ; celles que le mandataire PC gere (GERER_PC, _RecupIdDestEditLot2MandatairePC,
///      .MandatairesPC.cs:366-385). La V2 les lit par une connexion de lecture a part, donc AVANT la reprise : la V3 les releve avant ;
///   4. PUIS l'acteur est supprime (m.Delete(cnx), .Mandataires.cs:149, .Juges.cs:434, .MandatairesPC.cs:337).
/// CONTROLES, avant toute ecriture, messages au mot pres : au moins un acteur coche (« Il faut sélectionner au moins un mandataire à
/// supprimer dans la grille », Res.cs:33) ; un seul type a la fois (la V2 travaille sur le type choisi) ; mandataire : aucun actif qui a un
/// numero CNBF (Res.cs:35) ; juge : aucun qui est le juge par defaut des injonctions (reglage IP_JUGE, Res.TEXTE_JUGE_EN_PARAM_IP,
/// Res.cs:32) -- la V2 actuelle compare ce reglage a l'acteur GARDE (.Juges.cs:178), l'outil d'origine au juge a SUPPRIMER
/// (Judiciaire\Exe\PROC_RAPPRO_ACTEURS\frmRapproActeurs.cs:583-590, « si on veut remplacer un juge on va vérifier qu'il n'est pas
/// paramétré dans PARAMIP ») et le message dit « que vous voulez supprimer » : la V3 controle les juges coches ; un acteur garde, du meme
/// type, qui existe et qui n'est pas coche, et REPERTORIE (avec un abrege) : la liste « a garder » de la V2 exige l'abrege dans ses deux
/// filtres, « ACTIF » et « TOUS » (_RechercherMandatairesSQL .Mandataires.cs:166-176, _RechercherJugesSQL .Juges.cs:135-171,
/// _RechercherMandatairesPcSQL .MandatairesPC.cs:127-166) -- un acteur garde inactif est donc admis, comme avec « TOUS ».
/// L'acteur garde se designe par son numero (l'id de sa ligne, « M-12 », « J-12 », « P-12 », ou « 12 ») ou son abrege, dans « Acteur à
/// garder ».
/// NON REPRIS (nommes) : la regeneration complete des destinataires d'edition d'une PC apres la reprise d'un juge-commissaire ou d'un
/// mandataire PC (procedureCollective.destinataireEditionLot2.Sauve, .Juges.cs:296-300, .MandatairesPC.cs:246-251 : le XML est recalcule
/// depuis toute la procedure par RIG v2, calcul que la V3 n'a pas) -- seule la substitution de _SaveEdiLot2 est faite ; le document Word
/// recapitulatif (Aspose, _CreerDocumentWord, OPE_SELECTION_ACTEUR.cs:348) dans le repertoire commun du registre ; la demande
/// « Rapprochement acteur » creee et cloturee d'un coup (_CreerDemande) -- une trace, que le journal des gestes remplace (decision du
/// plan) ; les caches de RIG v2 (CacheSqlBase.Clear).
/// TRACE : famille « reglage », « a supprime » par acteur fusionne (avant : son nom et ses liaisons ; apres : l'acteur garde).
/// </summary>
// fix-ok: mesure le 27/11/2026 a la lecture de ce fichier et du code V2 -- (1) la fusion d'un juge ou d'un mandataire PC rendait le refus
// « n'est pas encore reprise » (NonRepris, bloc 2 de PROC_RAPPACTEUR.gestes) alors que _DeleteJuges / _DeleteMandatairesPC existent en V2 ;
// (2) l'acteur garde inactif etait refuse, or la liste « a garder » de la V2 a un filtre « TOUS » (OPE_GARDER_MANDATAIRE.cs:71,
// OPE_GARDER_JUGE.cs:71) dont la seule exigence est l'abrege ; (3) les destinataires d'edition se relevaient par instance seulement.
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
    /// <summary>Res.TEXTE_JUGE_EN_PARAM_IP, Res.cs:32.</summary>
    internal const string JugeEnParamIp = "Le juge que vous voulez supprimer est paramétré comme juge par défaut dans le processus PARAMIP";

    /// <summary>
    /// L'ordre des liaisons de la V2 pour un juge (_DeleteJuges, .Juges.cs:213-417) et un mandataire PC (_DeleteMandatairesPC,
    /// .MandatairesPC.cs:205-320), par libelle de LiaisonsActeur.cs ; puis la cle que la trace cite dans « where &lt;cle&gt; in » quand la
    /// V2 n'y met pas la cle de la table : le juge deposant GED d'une decision cite DCJDC_ID_DEPOSANT_GED (.Juges.cs:280).
    /// </summary>
    private static readonly (string TypeCode, string Groupe, (string Libelle, string? CleTrace)[] Ordre)[] Fusions =
    [
        ("JUGE", Juges,
        [
            ("Siéger", null), ("Audience cabinet", null), ("Décision judiciaire", null), ("Décision déposant GED", "DCJDC_ID_DEPOSANT_GED"),
            ("Juge PC", null), ("Mesure", null), ("Evènement procédure déposant GED", null), ("Vidiged index", null), ("Juge instance", null),
            ("Signataire REDA", null), ("Coffre reçu", null), ("Instance", null),
        ]),
        ("MANDATAIRE_PC", MandatairesPc,
        [
            ("Evènement procédure déposant GED", null), ("Vidiged index", null), ("Mandataire PC", null), ("Démat CFEC", null), ("Payeur", null),
            ("Coffre reçu", null), ("Décision déposant GED", null),
        ]),
    ];

    /// <summary>Le type de LiaisonsActeur et ses liaisons dans l'ordre de la V2, pour le groupe <paramref name="groupe"/> (Juges, Mandataires PC).</summary>
    internal static (LiaisonsActeur.TypeActeur Type, (LiaisonsActeur.Liaison Liaison, string CleTrace)[] Liaisons) Fusion(string groupe)
    {
        var (code, _, ordre) = Fusions.Single(f => f.Groupe == groupe);
        var t = LiaisonsActeur.Type(code)!;
        return (t, [.. ordre.Select(o => (t.Liaisons.Single(l => l.Libelle == o.Libelle), o.CleTrace ?? t.Liaisons.Single(l => l.Libelle == o.Libelle).Cle))]);
    }

    /// <summary>Les destinataires d'edition que la fusion de l'acteur <c>@id</c> met a jour (_RecupIdDestEditLot2*), releves AVANT la reprise.</summary>
    private static string DestinatairesDe(string groupe) => groupe switch
    {
        Juges => """
            SELECT d.DESED_ID_DESED FROM v2.DESTINATAIRES_EDITIONS_LOT2 AS d
            WHERE d.DESED_ID_PRCLL IN (SELECT ETJCM_ID_PRCLL FROM v2.ETRE_JUGE_COMM WHERE ETJCM_ID_JUG = @id)
               OR d.DESED_ID_PREVE IN (SELECT ETJCM_ID_PREVE FROM v2.ETRE_JUGE_COMM WHERE ETJCM_ID_JUG = @id AND ETJCM_ID_PRCLL IS NULL)
            ORDER BY d.DESED_ID_DESED
            """,
        MandatairesPc => """
            SELECT d.DESED_ID_DESED FROM v2.DESTINATAIRES_EDITIONS_LOT2 AS d
            WHERE d.DESED_ID_PRCLL IN (SELECT GRRPC_ID_PRCLL FROM v2.GERER_PC WHERE GRRPC_ID_MNDPC = @id)
               OR d.DESED_ID_PREVE IN (SELECT GRRPC_ID_PREVE FROM v2.GERER_PC WHERE GRRPC_ID_MNDPC = @id AND GRRPC_ID_PRCLL IS NULL)
            ORDER BY d.DESED_ID_DESED
            """,
        _ => """
            SELECT d.DESED_ID_DESED FROM v2.DESTINATAIRES_EDITIONS_LOT2 AS d
            WHERE d.DESED_ID_INSTN IN (SELECT PRT_ID_INSTN FROM v2.PARTIE WHERE PRT_ID_MNDTR_POS = @id OR PRT_ID_MNDTR_PLAI = @id)
            ORDER BY d.DESED_ID_DESED
            """,
    };

    private sealed record Mandataire(int Id, string Nom, string Abrege, string Cnbf, bool Actif);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var choisis = geste.Demande.Ids.Select(i => (Id: i, Lu: LecteurRapprochementActeurs.Lire(i))).ToArray();
        if (choisis.Length == 0) return IssueGeste.Refuser(SelectionnezSvp);
        if (choisis.Where(c => c.Lu is null).Select(c => c.Id).FirstOrDefault() is { } inconnu)
            return IssueGeste.Refuser($"Acteur introuvable dans la base du greffe ({inconnu}).");
        var groupes = choisis.Select(c => c.Lu!.Value.Groupe).Distinct().ToArray();
        if (groupes.Length > 1) return IssueGeste.Refuser("Un seul type d'acteur à la fois : juges, mandataires ou mandataires PC (RIG v2).");
        if (groupes[0] != Mandataires) return await FusionnerActeursAsync(geste, groupes[0], [.. choisis.Select(c => c.Lu!.Value.Id)], ct);

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
        if (garde.Abrege.Length == 0)
            return IssueGeste.Refuser($"{garde.Nom} ne peut pas être gardé : RIG v2 ne garde qu'un mandataire répertorié (avec un abrégé).");

        var auteur = await EcrivainRemplacementActeur.AuteurAsync(geste, ct);
        var traces = new List<TraceGeste>();
        foreach (var m in supprimes)
        {
            var destinataires = await DestinatairesAsync(geste, Mandataires, m.Id, ct);
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
            await SubstituerDansDestinatairesAsync(geste, destinataires, "MANDATAIRE", m.Id, garde.Id, ct);
            await geste.EcrireAsync("DELETE FROM v2.MANDATAIRE WHERE MNDTR_ID_MNDTR = @id", ct, new SqlParameter("@id", m.Id));
            traces.Add(new TraceGeste("reglage", $"le mandataire {m.Nom}", m.Id.ToString(CultureInfo.InvariantCulture), "a supprime",
                new { Mandataire = NomAbrege(m), Liaisons = reprises }, new { RemplacePar = NomAbrege(garde), IdGarde = garde.Id }));
        }
        return Ecrit(supprimes.Select(NomAbrege).ToArray(), NomAbrege(garde), $"M-{garde.Id}", traces);
    }

    /// <summary>La fusion d'un ou plusieurs JUGES ou MANDATAIRES PC (_VerifierJuges et _DeleteJuges, _VerifierMandatairesPC et
    /// _DeleteMandatairesPC) : memes etapes que pour un mandataire, sur les liaisons de LiaisonsActeur.cs.</summary>
    private static async Task<IssueGeste> FusionnerActeursAsync(GesteEnCours geste, string groupe, int[] ids, CancellationToken ct)
    {
        var (t, liaisons) = Fusion(groupe);
        var mot = t.Libelle.ToLowerInvariant();
        var supprimes = new List<EcrivainRemplacementActeur.Acteur>();
        foreach (var id in ids)
            supprimes.Add(await EcrivainRemplacementActeur.ActeurAsync(geste, t, id, ct)
                          ?? throw new InvalidOperationException($"{t.Libelle} {id} lu puis disparu."));
        // _VerifierJuges (.Juges.cs:174-189) : le juge par defaut des injonctions (dbo.GetParametrage('IP_JUGE'), repris dans soc.Reglage) ne
        // se supprime pas.
        if (groupe == Juges && await ReglageAsync(geste, "IP_JUGE", ct) is { Length: > 0 } ipJuge
            && supprimes.Any(j => j.Id.ToString(CultureInfo.InvariantCulture) == ipJuge))
            return IssueGeste.Refuser(JugeEnParamIp);

        var saisie = geste.Valeur(SectionGarde, ChampGarde)?.Trim() ?? "";
        if (saisie.Length == 0) return IssueGeste.Refuser($"« {ChampGarde} » est obligatoire : le numéro ou l'abrégé du {mot} qui reprend les affaires.");
        var numero = LecteurRapprochementActeurs.Lire(saisie) is { } lu && lu.Groupe == groupe ? lu.Id.ToString(CultureInfo.InvariantCulture) : saisie;
        if (await EcrivainRemplacementActeur.ResoudreAsync(geste, t, numero, ct) is not { } idGarde)
            return IssueGeste.Refuser($"« {ChampGarde} » : aucun {mot} ne porte le numéro ou l'abrégé « {saisie} ».");
        var garde = (await EcrivainRemplacementActeur.ActeurAsync(geste, t, idGarde, ct))!;
        if (supprimes.Any(a => a.Id == garde.Id)) return IssueGeste.Refuser($"{garde.NomComplet} ne peut pas être à la fois gardé et supprimé.");
        if (garde.Abrege.Length == 0)
            return IssueGeste.Refuser($"{garde.NomComplet} ne peut pas être gardé : RIG v2 ne garde qu'un {mot} répertorié (avec un abrégé).");

        var auteur = await EcrivainRemplacementActeur.AuteurAsync(geste, ct);
        var traces = new List<TraceGeste>();
        foreach (var a in supprimes)
        {
            var destinataires = await DestinatairesAsync(geste, groupe, a.Id, ct);
            var requete = new StringBuilder();
            var reprises = new List<string>();
            foreach (var (liaison, cleTrace) in liaisons)
            {
                var lignes = new List<string>();
                await using (var r = await geste.LireAsync($"SELECT {liaison.Cle} FROM v2.{liaison.Table} WHERE {liaison.Condition("@id")} ORDER BY {liaison.Cle}", ct,
                                 new SqlParameter("@id", a.Id)))
                    while (await r.ReadAsync(ct)) lignes.Add(Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture)!);
                if (lignes.Count == 0) continue;
                await geste.EcrireAsync($"UPDATE v2.{liaison.Table} SET {liaison.Colonne} = @garde WHERE {liaison.Condition("@id")}", ct,
                    new SqlParameter("@garde", garde.Id), new SqlParameter("@id", a.Id));
                requete.Append(CultureInfo.InvariantCulture, $"update {liaison.Table} set {liaison.Colonne} = {garde.Id} where {cleTrace} in ({string.Join(", ", lignes)})\r\n");
                reprises.Add($"{lignes.Count} × {liaison.Libelle.ToLowerInvariant()}");
            }
            if (requete.Length == 0) requete.Append(AucuneMiseAJour);
            await geste.InsererAsync("v2.ACTEUR_SUPP", "ACTSUP_ID_ACTSUP", new Dictionary<string, object?>
            {
                ["ACTSUP_TABLE_SUPP"] = t.Table, ["ACTSUP_ID_SUPP"] = a.Id, ["ACTSUP_ID_REMPL"] = garde.Id, ["ACTSUP_REQUETE"] = requete.ToString(),
                ["ACTSUP_DATE_CREATION"] = geste.Maintenant, ["ACTSUP_AUTEUR"] = auteur, ["ACTSUP_SUPP_DESIGNATION"] = Couper(a.NomComplet),
                ["ACTSUP_REMPL_DESIGNATION"] = Couper(garde.NomComplet),
            }, ct);
            await SubstituerDansDestinatairesAsync(geste, destinataires, t.Code, a.Id, garde.Id, ct);
            await geste.EcrireAsync($"DELETE FROM v2.{t.Table} WHERE {t.Cle} = @id", ct, new SqlParameter("@id", a.Id));
            traces.Add(new TraceGeste("reglage", $"le {mot} {a.NomComplet}", a.Id.ToString(CultureInfo.InvariantCulture), "a supprime",
                new { Acteur = a.NomComplet, Liaisons = reprises }, new { RemplacePar = garde.NomComplet, IdGarde = garde.Id }));
        }
        return Ecrit(supprimes.Select(a => a.NomComplet).ToArray(), garde.NomComplet, $"{Prefixe(groupe)}-{garde.Id}", traces);
    }

    private static IssueGeste Ecrit(string[] noms, string garde, string idGarde, List<TraceGeste> traces)
        => IssueGeste.Ecrit($"{string.Join(", ", noms)} {(noms.Length > 1 ? "ont été remplacés" : "a été remplacé")} par {garde} : " +
                            $"{(noms.Length > 1 ? "leurs affaires lui sont reportées" : "ses affaires lui sont reportées")}.",
            [idGarde], [.. traces]);

    private static string Couper(string texte) => texte.Length <= 200 ? texte : texte[..200];

    /// <summary>_NomCompletAbrege de la V2 : le nom, puis l'abrege entre parentheses s'il y en a un.</summary>
    private static string NomAbrege(Mandataire m) => m.Abrege.Length > 0 ? $"{m.Nom} ({m.Abrege})" : m.Nom;

    /// <summary>La valeur d'un reglage du greffe (PARAMETRAGE de la V2, repris dans soc.Reglage) ; null s'il n'existe pas.</summary>
    private static async Task<string?> ReglageAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT Valeur FROM soc.Reglage WHERE Code = @c", ct, new SqlParameter("@c", code));
        return await r.ReadAsync(ct) && !r.IsDBNull(0) ? r.GetString(0).Trim() : null;
    }

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

    /// <summary>Les destinataires d'edition que touche la fusion de l'acteur <paramref name="id"/>, AVANT la reprise (_RecupIdDestEditLot2*).</summary>
    private static async Task<List<int>> DestinatairesAsync(GesteEnCours geste, string groupe, int id, CancellationToken ct)
    {
        var ids = new List<int>();
        await using var r = await geste.LireAsync(DestinatairesDe(groupe), ct, new SqlParameter("@id", id));
        while (await r.ReadAsync(ct)) ids.Add(r.GetInt32(0));
        return ids;
    }

    /// <summary>_SaveEdiLot2 (OPE_SELECTION_ACTEUR.cs:262-295) : dans le XML des destinataires d'edition <paramref name="destinataires"/>,
    /// chaque entite <paramref name="code"/> du supprime designe desormais le garde.</summary>
    private static async Task SubstituerDansDestinatairesAsync(GesteEnCours geste, List<int> destinataires, string code, int supprime, int garde, CancellationToken ct)
    {
        if (destinataires.Count == 0) return;
        var aEcrire = new List<(int Id, string Xml)>();
        await using (var r = await geste.LireAsync($"""
                         SELECT DESED_ID_DESED, DESED_XML_DESTEDIT FROM v2.DESTINATAIRES_EDITIONS_LOT2
                         WHERE DESED_ID_DESED IN ({string.Join(", ", destinataires)}) AND DESED_XML_DESTEDIT IS NOT NULL
                         """, ct))
        {
            while (await r.ReadAsync(ct))
            {
                var xml = XDocument.Parse(r.GetString(1));
                var entites = xml.Descendants().Where(e => (string?)e.Attribute("CODE") == code
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
