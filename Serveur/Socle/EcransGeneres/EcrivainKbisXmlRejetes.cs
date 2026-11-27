using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Kbis XML rejetés » (PROC_REJETSKBISXML) : « Annuler le rejet » des Kbis cochés. Mode d'emploi : EcrivainEcran.cs ; la liste :
/// LecteurKbisXmlRejetes.cs (Id = le Kbis). Plan « RIG v3 100 % fonctionnel », lot 16, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RCS\Exe\PROC_RejetsKbisXml\FrmRejetsKbisXml.cs, tsmiAnnulerRejet_Click (l. 159-184),
/// par Kbis coché (colIdKbis) : KbisXml.GetKbisXml, puis KBIS_CATEGORIE_ERREUR = "" et KBIS_LIBELLE_ERREUR = NULL, KbisXml.Sauve. Le
/// rejet quitte la liste (le lecteur ne lit que KBIS_LIBELLE_ERREUR non nul), le Kbis lui-même ne change pas.
/// CONTRÔLES, avant toute écriture :
///   1. au moins un Kbis coché (le menu de la V2 n'a l'entrée qu'avec une sélection, cmsRejetsKbis_Opening l. 248-256) ;
///   2. réservé à l'équipe Amitel : la V2 ne montre « Annuler » qu'en mode Amitel (colAmitelVisible, l. 255), basculé par le raccourci
///      caché Ctrl+Maj+V (l. 140-143). Pas de message dans la V2 (l'entrée est cachée) : celui d'EcrivainActesRejetes, le même cas ;
///   3. chaque Kbis existe : « Impossible d'annuler le rejet correspondant au kbis d'id.<id> car une erreur est survenue durant sa
///      récupération. » (l. 174), au mot près ;
///   4. chaque Kbis est encore un rejet de la liste (libellé d'erreur posé, dernier Kbis du dossier) : la V2 n'agit que sur les lignes de sa
///      grille. Message sans source V2.
/// DIFFÉRENCE ASSUMÉE (nommée) : la V2 annule ligne à ligne et rassemble les erreurs dans un message final (l. 165-182) ; la V3 écrit tout
/// ou rien, dans la transaction du geste : un Kbis en défaut refuse le lot, rien n'est écrit.
/// NON REPRIS (nommé) : « Kbis Xml » (tsmiKbisXml_Click, l. 152-158) et le bouton « Extrait Kbis » de la barre affichent le Kbis : un
/// affichage, pas une écriture -- il n'a pas de geste ici.
/// Traces : famille « dossier », le Kbis en mots, l'id du dossier, « a modifie », la catégorie et le libellé d'erreur avant et après.
/// </summary>
internal sealed class EcrivainKbisXmlRejetes : IEcrivainEcran
{
    public string Procedure => "PROC_REJETSKBISXML";
    public string Ecran => "Kbis XML rejetés";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur, au mot près : tsmiAnnulerRejet (« Annuler le rejet sélectionné... », l. 254).</summary>
    internal const string Annuler = "Annuler le rejet";

    /// <summary>Le refus de la V2 quand le Kbis ne se relit pas (FrmRejetsKbisXml.cs:174), au mot près.</summary>
    internal static string Introuvable(string id) =>
        $"Impossible d'annuler le rejet correspondant au kbis d'id.{id} car une erreur est survenue durant sa récupération.";

    private sealed record Rejet(int Id, int IdDossier, string Numero, string Categorie, string Libelle);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Annuler) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        // 1. Une sélection.
        if (ids.Length == 0) return IssueGeste.Refuser("Aucun Kbis coché : rien n'est annulé.");
        // 2. Le mode Amitel de la V2.
        if (!geste.Agent.EstAmitel) return IssueGeste.Refuser(EcrivainActesRejetes.MessageAmitel);

        var rejets = new List<Rejet>();
        foreach (var id in ids)
        {
            // 3. Le Kbis existe.
            if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || await LireAsync(geste, n, ct) is not { } k)
                return IssueGeste.Refuser(Introuvable(id));
            // 4. Encore un rejet de la liste.
            if (k.Libelle is null || !k.Dernier)
                return IssueGeste.Refuser($"Le Kbis {id} du dossier {FormeLigne.NumeroGestion(k.Rejet.Numero)} n'est plus rejeté : son rejet est déjà annulé, ou un Kbis plus récent l'a remplacé.");
            rejets.Add(k.Rejet);
        }

        var traces = new List<TraceGeste>();
        foreach (var k in rejets)
        {
            // tsmiAnnulerRejet_Click, l. 170-172.
            await geste.EcrireAsync("UPDATE v2.KBIS_XML SET KBIS_CATEGORIE_ERREUR = '', KBIS_LIBELLE_ERREUR = NULL WHERE KBIS_ID_KBIS = @id", ct,
                new SqlParameter("@id", k.Id));
            traces.Add(new TraceGeste("dossier", $"le Kbis XML {k.Id} du dossier {FormeLigne.NumeroGestion(k.Numero)}",
                k.IdDossier.ToString(CultureInfo.InvariantCulture), "a modifie",
                new Dictionary<string, string> { ["Catégorie d'erreur"] = k.Categorie, ["Erreur"] = k.Libelle },
                new Dictionary<string, string> { ["Catégorie d'erreur"] = "", ["Erreur"] = "(aucune : rejet annulé)" }));
        }
        var nb = rejets.Count;
        return IssueGeste.Ecrit(nb > 1 ? $"{nb} rejets de Kbis XML annulés : ils quittent la liste." : "Rejet du Kbis XML annulé : il quitte la liste.",
            [.. rejets.Select(k => k.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }

    /// <summary>Le Kbis <paramref name="id"/>, verrouillé pour le geste ; null s'il n'existe pas. Libelle null : plus de rejet.</summary>
    private static async Task<(Rejet Rejet, string? Libelle, bool Dernier)?> LireAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT k.KBIS_ID_DSRCS, k.KBIS_CATEGORIE_ERREUR, k.KBIS_LIBELLE_ERREUR, k.KBIS_IS_LAST, s.NumeroGestion
            FROM v2.KBIS_XML AS k WITH (UPDLOCK) LEFT JOIN rec.DossierRcs AS s ON s.Id = k.KBIS_ID_DSRCS
            WHERE k.KBIS_ID_KBIS = @id
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        var libelle = r["KBIS_LIBELLE_ERREUR"] is DBNull ? null : FormeLigne.Texte(r, "KBIS_LIBELLE_ERREUR");
        return (new Rejet(id, Convert.ToInt32(r["KBIS_ID_DSRCS"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "NumeroGestion"),
                          FormeLigne.Texte(r, "KBIS_CATEGORIE_ERREUR"), libelle ?? ""),
                libelle, r["KBIS_IS_LAST"] is true);
    }
}
