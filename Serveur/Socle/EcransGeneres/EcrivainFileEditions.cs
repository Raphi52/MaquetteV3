using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « File des éditions » (PROC_POOLEDITION) : « Imprimer » les éditions cochées, sur une imprimante du greffe et en un nombre
/// d'exemplaires. Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurFileEditions.cs (Id = la demande d'édition). Plan « RIG v3
/// 100 % fonctionnel », lot 16, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\EXE\Accueil\PROC_PoolEdition\PoolEditionProprietes.cs, la fenêtre « Propriétés
/// du document » (Designer l. 888) d'une édition de la liste, validée sans reprise (« Aucune reprise », l. 244-246) : _Valider
/// (l. 206-327) réécrit les options de l'édition, dont « Imprimer : » (chkImprimer -> DMNED_IMPRESSION, l. 227), « Imprimante : »
/// (cbxImprimante -> DMNED_IMPRIMANTE, l. 233) et « Nombre d'exemplaires : » (nudNbExemplaires -> DMNED_NOMBRE_EXEMPLAIRE, l. 234).
/// Le service d'édition de RIG v2 imprime ensuite l'édition à son émission, sur cette imprimante : la V3 n'appelle aucune
/// imprimante (partie externe, simulée : le message le dit) ; ce qu'elle écrit est l'état V2 de l'édition, que ce service relit.
/// CONTRÔLES, avant toute écriture :
///   1. au moins une édition cochée ;
///   2. l'édition existe : « Impossible de valider ces changements. L'edition est introuvable : <id> » (l. 222-224), au mot près ;
///   3. l'imprimante : celle saisie, sinon celle de l'édition ; une édition à imprimer doit en avoir une qui existe dans la liste du
///      greffe (IMPRIMANTE_EDITION : ImprimanteLot2.IsImprimanteExistante, ImprimanteLot2.cs:301-310, la liste de cbxImprimante,
///      l. 339-343) : « L'imprimante n'existe pas ou n'a pas été définie. » (l. 238-242), au mot près ;
///   4. le nombre d'exemplaires : celui saisi, sinon celui de l'édition ; un entier de 0 à 100, les bornes par défaut du compteur de
///      la V2 (RigNumericUpDown hérite de NumericUpDown, RigNumericUpDown.cs:12 ; le Designer n'en pose pas d'autres, l. 471-477) ;
///      message sans source V2 ;
///   5. l'édition n'est pas déjà émise (phase « E ») : sans reprise, la V2 n'imprime plus une édition émise ; la réimprimer est une
///      REPRISE (« Simple réémission », l. 247-315), non reprise ici (ci-dessous) ; message sans source V2.
/// NON REPRIS (nommés) : les autres options de la fenêtre (libellé, afficher avant émission, file différée, recto verso, agrafage,
///   type de papier, destinataire, adresses) ; les reprises « Simple réémission », « Regénération du document » et « Regénération
///   des données » (une nouvelle demande d'édition, la copie de ses documents dans la GED et son groupe d'impression :
///   TableauEditionLot2Parametre.SauveDiffere) ; « Supprimer une édition » (PoolEdition.cs:930-970 : DEMANDE_EDITION_SUPPRIMEE,
///   recommandé, journal des événements).
/// Traces : famille « demande », l'édition en mots, son id, « a modifie », les options d'impression avant et après.
/// </summary>
internal sealed class EcrivainFileEditions : IEcrivainEcran
{
    public string Procedure => "PROC_POOLEDITION";
    public string Ecran => "File des éditions";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste et les saisies du descripteur, au mot près (les libellés de la fenêtre de la V2, Designer l. 256, 506, 467).</summary>
    internal const string Imprimer = "Imprimer", Imprimante = "Imprimante", Exemplaires = "Nombre d'exemplaires";

    /// <summary>Les messages de _Valider, au mot près (PoolEditionProprietes.cs:223 et :239).</summary>
    internal static string Introuvable(string id) => $"Impossible de valider ces changements. L'edition est introuvable : {id}";
    internal const string SansImprimante = "L'imprimante n'existe pas ou n'a pas été définie.";

    private sealed record Edition(int Id, string Libelle, string Phase, bool Imprime, string Imprimante, int? Exemplaires);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Imprimer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        // 1. Une sélection.
        if (ids.Length == 0) return IssueGeste.Refuser("Aucune édition cochée : rien n'est imprimé.");
        var imprimanteSaisie = Saisi(geste, Imprimante);
        var exemplairesSaisis = Saisi(geste, Exemplaires);
        int? exemplaires = null;
        if (exemplairesSaisis.Length > 0)
        {
            // 4. Les bornes du compteur de la V2.
            if (!int.TryParse(exemplairesSaisis, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n > 100)
                return IssueGeste.Refuser($"« {Exemplaires} » : un nombre entier de 0 à 100, comme le compteur de RIG v2 ({exemplairesSaisis} saisi).");
            exemplaires = n;
        }

        var editions = new List<(Edition Avant, string Imprimante, int Exemplaires)>();
        foreach (var id in ids)
        {
            // 2. L'édition existe.
            if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || await LireAsync(geste, n, ct) is not { } e)
                return IssueGeste.Refuser(Introuvable(id));
            // 5. Déjà émise.
            if (e.Phase == "E")
                return IssueGeste.Refuser($"L'édition n° {e.Id} (« {e.Libelle} ») est déjà émise : la réimprimer est une reprise de RIG v2 (« Simple réémission »), que la V3 ne fait pas.");
            // 3. L'imprimante, dans la liste du greffe.
            var imprimante = imprimanteSaisie.Length > 0 ? imprimanteSaisie : e.Imprimante;
            if (!await ImprimanteExisteAsync(geste, imprimante, ct)) return IssueGeste.Refuser(SansImprimante);
            editions.Add((e, imprimante, exemplaires ?? e.Exemplaires ?? 1));
        }

        var traces = new List<TraceGeste>();
        foreach (var (e, imprimante, nb) in editions)
        {
            // _Valider, l. 227, 233, 234.
            await geste.EcrireAsync("""
                UPDATE v2.DEMANDE_EDITION SET DMNED_IMPRESSION = 1, DMNED_IMPRIMANTE = @imprimante, DMNED_NOMBRE_EXEMPLAIRE = @nb
                WHERE DMNED_ID_DMNED = @id
                """, ct, new SqlParameter("@imprimante", imprimante), new SqlParameter("@nb", nb), new SqlParameter("@id", e.Id));
            traces.Add(new TraceGeste("demande", $"l'édition n° {e.Id} « {e.Libelle} »", e.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
                Options(e.Imprime, e.Imprimante, e.Exemplaires), Options(true, imprimante, nb)));
        }
        var premier = editions[0];
        var quoi = editions.Count > 1
            ? $"{editions.Count} éditions à imprimer"
            : $"Édition n° {premier.Avant.Id} « {premier.Avant.Libelle} » à imprimer";
        return IssueGeste.Ecrit(
            $"{quoi} sur « {premier.Imprimante} » ({premier.Exemplaires} exemplaire{(premier.Exemplaires > 1 ? "s" : "")}) : le service d'édition de RIG v2 " +
            "l'imprime à son émission. Aucune imprimante n'est appelée d'ici.",
            [.. editions.Select(e => e.Avant.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }

    private static Dictionary<string, string> Options(bool imprime, string imprimante, int? exemplaires) => new()
    {
        ["Imprimer"] = imprime ? "oui" : "non", ["Imprimante"] = imprimante,
        ["Nombre d'exemplaires"] = exemplaires?.ToString(CultureInfo.InvariantCulture) ?? "",
    };

    private static string Saisi(GesteEnCours geste, string libelle) => geste.Demande.Valeurs.TryGetValue(libelle, out var v) ? v.Trim() : "";

    private static async Task<Edition?> LireAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT DMNED_ID_DMNED, DMNED_LIBELLE_DOCUMENT, DMNED_PHASE_TRAITEMENT, DMNED_IMPRESSION, DMNED_IMPRIMANTE, DMNED_NOMBRE_EXEMPLAIRE
            FROM v2.DEMANDE_EDITION WITH (UPDLOCK) WHERE DMNED_ID_DMNED = @id
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        return new Edition(id, FormeLigne.Texte(r, "DMNED_LIBELLE_DOCUMENT"), FormeLigne.Texte(r, "DMNED_PHASE_TRAITEMENT"),
            r["DMNED_IMPRESSION"] is true, FormeLigne.Texte(r, "DMNED_IMPRIMANTE"),
            r["DMNED_NOMBRE_EXEMPLAIRE"] is DBNull ? null : Convert.ToInt32(r["DMNED_NOMBRE_EXEMPLAIRE"], CultureInfo.InvariantCulture));
    }

    /// <summary>ImprimanteLot2.IsImprimanteExistante (ImprimanteLot2.cs:301-310) : le nom logique est dans IMPRIMANTE_EDITION. Une base
    /// sans cette table n'a aucune imprimante définie.</summary>
    private static async Task<bool> ImprimanteExisteAsync(GesteEnCours geste, string imprimante, CancellationToken ct)
    {
        if (imprimante.Length == 0) return false;
        await using (var t = await geste.LireAsync("SELECT OBJECT_ID(N'v2.IMPRIMANTE_EDITION')", ct))
            if (!await t.ReadAsync(ct) || t.IsDBNull(0)) return false;
        await using var r = await geste.LireAsync("SELECT 1 FROM v2.IMPRIMANTE_EDITION WHERE IMPED_NOM_LOGIQUE = @nom", ct, new SqlParameter("@nom", imprimante));
        return await r.ReadAsync(ct);
    }
}
