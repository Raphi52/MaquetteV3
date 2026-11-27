using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Kbis XML rejetes » (PROC_REJETSKBISXML) : « Annuler le rejet » des Kbis XML coches. Mode d'emploi : EcrivainEcran.cs ; la
/// liste : LecteurKbisXmlRejetes.cs (Id = le Kbis XML). Plan « RIG v3 100 % fonctionnel », lot 16, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RCS\Exe\PROC_RejetsKbisXml\FrmRejetsKbisXml.cs, tsmiAnnulerRejet_Click
/// (l. 159-184), sur les lignes choisies de la grille (plusieurs : MultiSelect en mode Amitel, l. 143) : par Kbis,
/// KBIS_CATEGORIE_ERREUR = '' et KBIS_LIBELLE_ERREUR = NULL (l. 168-170), puis la liste est relue (l. 180) -- le rejet en sort, le
/// lecteur ne garde que les Kbis dont le libelle d'erreur est pose.
/// CONTROLES, avant toute ecriture :
///   1. au moins un Kbis coche (l. 160 : le geste porte sur la selection) ;
///   2. l'equipe Amitel seule : l'entree du menu n'est visible qu'en mode Amitel (cmsRejetsKbis_Opening, l. 255-256 :
///      colAmitelVisible), que seul le raccourci cache Ctrl+Maj+V ouvre (FrmRejetsKbisXml_KeyDown, l. 141-144). La V2 n'a pas de
///      message (l'entree est cachee) : la V3 refuse en le disant, avec le message du lot 8 pour le meme geste des actes rejetes
///      (EcrivainActesRejetes.MessageAmitel) ; le compte V3 porte le drapeau (soc.Compte.EstAmitel) ;
///   3. le Kbis existe, sinon le message de la V2 au mot pres (l. 172 : « Impossible d'annuler le rejet correspondant au kbis
///      d'id.<id> car une erreur est survenue durant sa récupération. ») ;
///   4. son rejet est encore pose (libelle d'erreur non nul, la condition du lecteur et de la grille V2, _ConstruireRequete
///      l. 299-338) : la V2 n'agit que sur les lignes de sa grille ; message sans source V2.
/// ECART ASSUME : la V2 continue ligne a ligne et liste les erreurs a la fin (l. 164-181) ; la V3 ecrit tout ou rien, dans la
/// transaction du geste (un Kbis introuvable refuse tout le lot coche).
/// NON REPRIS (nomme) : « Extrait Kbis » (tsmiKbisXml_Click, l. 152-158) affiche le Kbis, il n'ecrit rien. Rien d'autre n'est ecrit
/// par la V2 (ni demande, ni edition, ni journal).
/// Traces : famille « dossier », le Kbis en mots, l'id du dossier, « a modifie », la categorie et le libelle avant et apres.
/// </summary>
internal sealed class EcrivainKbisXmlRejetes : IEcrivainEcran
{
    public string Procedure => "PROC_REJETSKBISXML";
    public string Ecran => "Kbis XML rejetés";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur : l'entree tsmiAnnulerRejet, « Annuler le rejet sélectionné... » (l. 254).</summary>
    internal const string Annuler = "Annuler le rejet";

    /// <summary>Le message de la V2 pour un Kbis introuvable (l. 172), au mot pres.</summary>
    internal static string Introuvable(string id)
        => $"Impossible d'annuler le rejet correspondant au kbis d'id.{id} car une erreur est survenue durant sa récupération.";

    private sealed record Kbis(int Id, int IdDossier, string Numero, string Categorie, string Libelle);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Annuler) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        // 1. Une selection.
        if (ids.Length == 0) return IssueGeste.Refuser("Aucun Kbis coché : rien n'est annulé.");
        // 2. Le mode Amitel.
        if (!geste.Agent.EstAmitel) return IssueGeste.Refuser(EcrivainActesRejetes.MessageAmitel);

        var kbis = new List<Kbis>();
        foreach (var id in ids)
        {
            // 3. Le Kbis existe.
            Kbis? k = null;
            if (int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                await using (var r = await geste.LireAsync("""
                    SELECT k.KBIS_ID_DSRCS, k.KBIS_CATEGORIE_ERREUR, k.KBIS_LIBELLE_ERREUR, s.NumeroGestion
                    FROM v2.KBIS_XML AS k WITH (UPDLOCK) LEFT JOIN rec.DossierRcs AS s ON s.Id = k.KBIS_ID_DSRCS
                    WHERE k.KBIS_ID_KBIS = @id
                    """, ct, new SqlParameter("@id", n)))
                    if (await r.ReadAsync(ct))
                        k = new Kbis(n, Convert.ToInt32(r["KBIS_ID_DSRCS"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "NumeroGestion"),
                                     FormeLigne.Texte(r, "KBIS_CATEGORIE_ERREUR"), r["KBIS_LIBELLE_ERREUR"] is DBNull ? "" : FormeLigne.Texte(r, "KBIS_LIBELLE_ERREUR"));
            if (k is null) return IssueGeste.Refuser(Introuvable(id));
            // 4. Le rejet est encore pose.
            if (k.Libelle.Length == 0)
                return IssueGeste.Refuser($"Le rejet du Kbis {id} du dossier {FormeLigne.NumeroGestion(k.Numero)} est déjà annulé : il n'est plus dans la liste.");
            kbis.Add(k);
        }

        var traces = new List<TraceGeste>();
        foreach (var k in kbis)
        {
            // tsmiAnnulerRejet_Click, l. 168-170.
            await geste.EcrireAsync("UPDATE v2.KBIS_XML SET KBIS_CATEGORIE_ERREUR = '', KBIS_LIBELLE_ERREUR = NULL WHERE KBIS_ID_KBIS = @id", ct,
                new SqlParameter("@id", k.Id));
            traces.Add(new TraceGeste("dossier", $"le Kbis XML {k.Id} du dossier {FormeLigne.NumeroGestion(k.Numero)}",
                k.IdDossier.ToString(CultureInfo.InvariantCulture), "a modifie",
                new Dictionary<string, string> { ["Catégorie d'erreur"] = k.Categorie, ["Erreur"] = k.Libelle },
                new Dictionary<string, string> { ["Catégorie d'erreur"] = "", ["Erreur"] = "" }));
        }
        var nb = kbis.Count;
        return IssueGeste.Ecrit(nb > 1 ? $"{nb} rejets de Kbis XML annulés : ils sortent de la liste." : "Rejet du Kbis XML annulé : il sort de la liste.",
            [.. kbis.Select(k => k.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }
}
