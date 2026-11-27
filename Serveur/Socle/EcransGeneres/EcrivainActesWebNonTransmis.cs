using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Actes web non transmis » (PROC_ACTREJ_CMD_WEB) : « Annuler » la commande web de l'acte choisi. Mode d'emploi : EcrivainEcran.cs ;
/// la liste : LecteurActesWebNonTransmis.cs (Id = l'acte déposé). Plan « RIG v3 100 % fonctionnel », lot 16, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_ACTREJ_CMD_WEB\PROC_ACTREJ_CMD_WEB\RESULTAT\OPE_RESULTATS.cs,
/// _tsmiAnnuler_Click (l. 84-117), l'entrée « Annuler » du menu de la ligne (OPE_RESULTATS.Designer.cs:154) :
///   1. le flux de la commande web (FluxEntrant, dans la base EDI du greffe : RigGreffe.CodeGreffeBaseEdi) passe à l'état Annulé (0), avec
///      le motif « Motif d'annulation : Annulé par <compte> le <date> » (l. 88-98) ;
///   2. l'acte quitte la commande : « update ACTE_DEPOSE SET ACTDP_CMDWEB_FLUX_ID = null » (l. 101-106), et la liste le perd (le lecteur
///      ne garde que les actes d'une commande web).
/// CE QUE LA V3 ÉCRIT : le 2, l'état V2 de la ligne, dans la base du greffe. Le 1 est la PARTIE EXTERNE : la base EDI n'est pas dans la
/// base V3 (FLUX_ENTRANT n'est pas une table de RIG_DEV, lu le 27/11/2026) -- le flux n'est pas annulé d'ici, et le message le dit.
/// CONTRÔLES, avant toute écriture :
///   1. exactement UN acte coché : la V2 ne fait rien sans une seule ligne choisie (« if (_dgvActs.SelectedRows.Count != 1) return; »,
///      l. 85). Message sans source V2 (elle ne dit rien) ;
///   2. l'acte existe et attend encore sa commande web : flux posé, pas encore dans la GED (le filtre de la liste, _SqlBase de
///      PROC_ACTREJ_CMD_WEB.cs:36-48). Message sans source V2 : elle n'agit que sur les lignes de sa grille.
/// NON REPRIS (nommés) : « Envoyer au VGED » (_tsmiSendToVged_Click, l. 62-80) ouvre la GED du dépôt ou du dossier dans un autre
/// processus (VGED) pour y ranger l'acte : un écran, pas une écriture de celui-ci -- le descripteur ne l'offre donc pas ; « Afficher le détail de la commande » lit le flux EDI
/// (absent de la base V3) ; « Copier » copie une cellule.
/// Traces : famille « dossier », l'acte en mots, l'id du dossier, « a modifie », le flux de commande avant et après.
/// </summary>
internal sealed class EcrivainActesWebNonTransmis : IEcrivainEcran
{
    public string Procedure => "PROC_ACTREJ_CMD_WEB";
    public string Ecran => "Actes web non transmis";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur, au mot près (le menu de la ligne, OPE_RESULTATS.Designer.cs:154).</summary>
    internal const string Annuler = "Annuler";

    internal const string UneSeule = "« Annuler » ne vise qu'une commande à la fois : cochez un seul acte, comme RIG v2 (une seule ligne choisie).";

    private sealed record Acte(int Id, int Flux, string Depot, string Numero, int? IdDossier);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Annuler) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        // 1. Une seule ligne.
        if (ids.Length != 1) return IssueGeste.Refuser(UneSeule);
        // 2. L'acte, encore en attente de sa commande web.
        if (!int.TryParse(ids[0], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || await LireAsync(geste, n, ct) is not { } a)
            return IssueGeste.Refuser($"L'acte {ids[0]} n'attend plus de commande web : il est déjà rangé dans la GED, ou sa commande est annulée.");

        // _tsmiAnnuler_Click, l. 104-106.
        await geste.EcrireAsync("UPDATE v2.ACTE_DEPOSE SET ACTDP_CMDWEB_FLUX_ID = NULL WHERE ACTDP_ID_ACDPS = @id", ct, new SqlParameter("@id", a.Id));
        return IssueGeste.Ecrit(
            $"Commande web de l'acte du dépôt {a.Depot} annulée : l'acte quitte la liste. Le flux EDI n° {a.Flux.ToString(CultureInfo.InvariantCulture)} " +
            "(base EDI du greffe, hors de la V3) n'est pas annulé d'ici.",
            [a.Id.ToString(CultureInfo.InvariantCulture)],
            new TraceGeste("dossier", $"l'acte déposé {a.Id} du dépôt {a.Depot}{(a.Numero.Length > 0 ? $" ({FormeLigne.NumeroGestion(a.Numero)})" : "")}",
                (a.IdDossier ?? a.Id).ToString(CultureInfo.InvariantCulture), "a modifie",
                new Dictionary<string, string> { ["Commande web (flux)"] = a.Flux.ToString(CultureInfo.InvariantCulture) },
                new Dictionary<string, string> { ["Commande web (flux)"] = "(aucune : commande annulée ; flux EDI non annulé, hors V3)" }));
    }

    /// <summary>L'acte <paramref name="id"/> s'il attend encore sa commande web (le filtre du lecteur), verrouillé pour le geste.</summary>
    private static async Task<Acte?> LireAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT ad.ACTDP_ID_ACDPS, ad.ACTDP_CMDWEB_FLUX_ID, p.DPTRC_NUM_DEPOT, s.NumeroGestion, s.Id AS IdDossier
            FROM v2.ACTE_DEPOSE AS ad WITH (UPDLOCK)
            JOIN v2.DEPOT_RCS AS p ON p.DPTRC_ID_DPTRC = ad.ACTDP_ID_DPTRC
            OUTER APPLY (SELECT TOP (1) d.Id, d.NumeroGestion FROM rec.ActionRegistre AS a JOIN rec.DossierRcs AS d ON d.Id = a.IdDossier
                         WHERE a.IdDepot = p.DPTRC_ID_DPTRC ORDER BY a.Id) AS s
            WHERE ad.ACTDP_ID_ACDPS = @id AND ad.ACTDP_CMDWEB_FLUX_ID IS NOT NULL AND ad.ACTDP_GUID_VIDIGED IS NULL
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        return new Acte(id, Convert.ToInt32(r["ACTDP_CMDWEB_FLUX_ID"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "DPTRC_NUM_DEPOT"),
            FormeLigne.Texte(r, "NumeroGestion"), r["IdDossier"] is int d ? d : null);
    }
}
