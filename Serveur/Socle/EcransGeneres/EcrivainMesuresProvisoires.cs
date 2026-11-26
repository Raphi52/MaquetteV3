using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurMesuresProvisoires;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Mesure provisoire » (PROC_MESIN) : ENREGISTRER une mesure modifiee. Mode d'emploi : EcrivainEcran.cs ; la liste et la fiche :
/// LecteurMesuresProvisoires.cs. Plan « RIG v3 100 % fonctionnel », lot 5, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_MESIN ne surcharge ni DoVerifier ni DoSauve (proc_mesin.md, « Périmètre réel du PROC ») : la mesure
/// se sauve par le moteur, champs relies par OPE_MESURE.DoInitZoneSaisie (OPE_MESURE.cs:45-63) aux colonnes de MESURE
/// (RigBaseGreffe\GREFFE\MESURE.cs:249-287).
///
/// CONTROLES DE RIG v2, avant toute ecriture (un refus n'ecrit rien) :
///   1. la mesure tenue par une autre demande (sequenceur 2F, Mesure.Sequenceur, Mesure.cs:941) : RIG v2 ne l'ouvre qu'en lecture
///      (PROC_MESIN.cs:72-85) -- refus au message du verrou (VerrouDemandeV2) ;
///   2. un champ hors de ceux que la V3 enregistre (LecteurMesuresProvisoires.Modifiables) : numero et ouverture sont en lecture dans
///      RIG v2 (OPE_MESURE.Designer.cs:91, :112) ; les quatre dates de rapport et de diligences ajoutent un evenement de procedure
///      dans RIG v2 (OPE_MESURE.cs:95-142), non repris ;
///   3. le type de mesure est obligatoire (ultComboTypeMesure.MetierObligatoire, OPE_MESURE.Designer.cs:100) et pris dans la liste
///      des types du greffe (TYPE_MESURE, Mesure.TypeMesure, Mesure.cs:824-835) ;
///   4. dates et montant lisibles (MSINS_COUT_PREV_EXPERTISE est un entier, MESURE.cs:280).
///
/// CE QUI S'ECRIT : les colonnes changees de v2.MESURE, et MSINS_NUMERO_SOURCE si elle manque (GUID en majuscules, comme
/// Instance.Sauve pour INSTN_NUMERO_SOURCE). La demande MESIN : le journal des gestes la remplace -- RIG v2 la cree et la valide d'un
/// coup, sans facture au niveau du processus (MetierFacture = false, PROC_MESIN.Designer.cs:196-198) : une trace (decision du plan,
/// comme MJUD). Trace : famille « affaire », « a modifie », avant / apres.
///
/// NON REPRIS (nommes) : les evenements de procedure que les dates de rapport et de diligences ajoutent (ci-dessus) ; la facturation
/// MESIN (EtapeFacturation, PROC_MESIN.cs:143) ; les parties et les evenements, lus seulement ; le compte de la mesure (« Gérer le
/// compte », OPE_MESURE.cs:310, programme VB6).
/// </summary>
internal sealed class EcrivainMesuresProvisoires : IEcrivainEcran
{
    internal const string NomEcran = "Mesure provisoire";

    public string Procedure => "PROC_MESIN";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    private enum Sorte { Texte, Date, Entier }

    /// <summary>Les champs ecrits et leur colonne de v2.MESURE (OPE_MESURE.cs:45-63).</summary>
    private static readonly (string Champ, string Colonne, Sorte Sorte)[] Colonnes =
    [
        (ChampType, "MSINS_TYPE_MESURE", Sorte.Texte), (ChampSansSuite, "MSINS_DATE_SANS_SUITE", Sorte.Date),
        (ChampCaducite, "MSINS_DATE_CADUCITE", Sorte.Date), (ChampPrevisionRapport, "MSINS_DATE_PREV_DEPOT_RAPPORT", Sorte.Date),
        (ChampCout, "MSINS_COUT_PREV_EXPERTISE", Sorte.Entier), (ChampInformations, "MSINS_INFORMATIONS", Sorte.Texte),
    ];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids is not [var id] || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            || await LireMesureAsync(geste.Base, geste.Transaction, n, ct) is not { } m)
            return IssueGeste.Refuser($"Mesure introuvable dans la base du greffe ({string.Join(", ", geste.Demande.Ids)}).");
        if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Mesure", Sequenceur, id, ct) is { } verrou)
            return IssueGeste.Refuser(verrou);
        var nom = m.Numero.Length > 0 ? m.Numero : id;

        var prefixe = GesteEcran.Cle(SectionMesure, "");
        var saisis = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (cle, valeur) in geste.Demande.Valeurs)
        {
            var libelle = cle.StartsWith(prefixe, StringComparison.Ordinal) ? cle[prefixe.Length..] : null;
            if (libelle is null || !Modifiables.Contains(libelle))
                return IssueGeste.Refuser($"« {cle} » ne se modifie pas sur la mesure {nom} dans la V3.");
            saisis[libelle] = valeur.Trim();
        }
        foreach (var (champ, _, sorte) in Colonnes.Where(c => saisis.ContainsKey(c.Champ)))
        {
            var v = saisis[champ];
            if (sorte == Sorte.Date && v.Length > 0 && CreationV2.Date(v) is null) return IssueGeste.Refuser($"« {champ} » : date illisible ({v}).");
            if (sorte == Sorte.Entier && v.Length > 0 && !int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return IssueGeste.Refuser($"« {champ} » : montant illisible ({v}).");
        }
        if (saisis.TryGetValue(ChampType, out var type))
        {
            if (type.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ChampType));
            await using var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.TYPE_MESURE WHERE R_TYPEMESURE_VALEUR = @t", ct, new SqlParameter("@t", type));
            await r.ReadAsync(ct);
            if (r.GetInt32(0) == 0) return IssueGeste.Refuser($"« {ChampType} » : « {type} » n'est pas un type de mesure de la table TYPE_MESURE.");
        }

        var avant = m.Valeurs;
        var changes = saisis.Where(s => avant[s.Key] != s.Value).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        if (changes.Count == 0) return IssueGeste.Refuser($"Aucun champ n'est modifié : la mesure {nom} garde ses valeurs.");

        var affectations = new List<string>();
        var parametres = new List<SqlParameter> { new("@id", n), new("@guid", Guid.NewGuid().ToString().ToUpperInvariant()) };
        foreach (var (champ, colonne, sorte) in Colonnes.Where(c => changes.ContainsKey(c.Champ)))
        {
            affectations.Add($"{colonne} = @{colonne}");
            var v = changes[champ];
            parametres.Add(new SqlParameter($"@{colonne}", v.Length == 0 ? DBNull.Value : sorte switch
            {
                Sorte.Date => CreationV2.Date(v)!.Value,
                Sorte.Entier => int.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture),
                _ => v,
            }));
        }
        affectations.Add("MSINS_NUMERO_SOURCE = ISNULL(MSINS_NUMERO_SOURCE, @guid)");
        var touchees = await geste.EcrireAsync($"UPDATE v2.MESURE SET {string.Join(", ", affectations)} WHERE MSINS_ID_MSINS = @id", ct, [.. parametres]);
        if (touchees != 1) throw new InvalidOperationException($"Mesure {n} : {touchees} ligne(s) mise(s) a jour au lieu d'une.");

        var champs = string.Join(", ", Colonnes.Where(c => changes.ContainsKey(c.Champ)).Select(c => $"« {c.Champ} »"));
        return IssueGeste.Ecrit($"Mesure {nom} modifiée dans la base du greffe : {champs}.", [id],
            new TraceGeste("affaire", $"la mesure {nom}", id, "a modifie", changes.Keys.ToDictionary(c => c, c => avant[c]), changes));
    }
}
