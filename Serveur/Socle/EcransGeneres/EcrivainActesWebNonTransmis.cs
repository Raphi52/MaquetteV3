using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Actes web non transmis » (PROC_ACTREJ_CMD_WEB) : « Annuler » la commande web d'UN acte. Mode d'emploi : EcrivainEcran.cs ; la
/// liste : LecteurActesWebNonTransmis.cs (Id = l'acte depose). Plan « RIG v3 100 % fonctionnel », lot 16, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_ACTREJ_CMD_WEB\PROC_ACTREJ_CMD_WEB\RESULTAT\OPE_RESULTATS.cs,
/// _tsmiAnnuler_Click (l. 84-117), l'entree « Annuler » du menu de la ligne (OPE_RESULTATS.Designer.cs:154) :
///   1. le flux de la commande (colIdCmdwebFlux) est annule dans la base EDI du greffe (RigGreffe.CodeGreffeBaseEdi) :
///      FLUENT_ETAT = Annule (0, FluxEntrant.cs:353), FLUENT_ERREUR = « Motif d'annulation : Annulé par <agent> le <date> »
///      (l. 89-99) ; un flux introuvable leve « Le flux entrant <id> n'existe pas dans <base EDI> » (l. 95) ;
///   2. puis « update ACTE_DEPOSE SET ACTDP_CMDWEB_FLUX_ID = null » sur l'acte (l. 102-110) : l'acte n'est plus commande par le web
///      et sort de la liste (RefreshRejets, l. 110 ; le lecteur ne garde que ACTDP_CMDWEB_FLUX_ID non nul).
/// CE QUE LA V3 ECRIT : le 2, dans la base du greffe. Le 1 est une base EXTERNE (la base EDI du greffe, ses flux entrants : ni
///   recopiee dans la base V3 ni servie par le serveur V3) : jamais appelee, la partie est SIMULEE et le message le dit ; le flux
///   que la V2 aurait annule est nomme dans la trace (son id). L'acte, lui, porte l'etat V2 que la V2 ecrit.
/// CONTROLES, avant toute ecriture :
///   1. UNE seule ligne : « if (_dgvActs.SelectedRows.Count != 1) return; » (l. 85) -- la V2 ne fait rien sur plusieurs lignes ;
///      message sans source V2 (la V2 se tait) ;
///   2. l'acte existe et est ENCORE dans la liste : commande web posee (ACTDP_CMDWEB_FLUX_ID) et pas encore dans la GED
///      (ACTDP_GUID_VIDIGED nul) -- la V2 n'agit que sur les lignes de sa grille (_SqlBase, PROC_ACTREJ_CMD_WEB.cs:36-48) ; messages
///      sans source V2.
/// NON REPRIS (nommes) : « Envoyer au VGED » (_tsmiSendToVged_Click, l. 61-80) ouvre la visionneuse GED du depot ou du dossier
///   (processus VGED) pour y ranger l'acte : un autre ecran, rien n'est ecrit par ce geste ; « Afficher le détail de la commande »
///   (l. 119-122) lit le flux dans la base EDI, absente de la V3 ; « Copier » copie la cellule.
/// Traces : famille « dossier », l'acte en mots, l'id du dossier (sinon de l'acte), « a modifie », la commande web avant et apres.
/// </summary>
internal sealed class EcrivainActesWebNonTransmis : IEcrivainEcran
{
    public string Procedure => "PROC_ACTREJ_CMD_WEB";
    public string Ecran => "Actes web non transmis";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur, au mot pres : tsmiAnnuler (OPE_RESULTATS.Designer.cs:154).</summary>
    internal const string Annuler = "Annuler";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Annuler) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        // 1. Une seule ligne (l. 85).
        if (ids.Length != 1)
            return IssueGeste.Refuser(ids.Length == 0
                ? "Aucun acte coché : rien n'est annulé."
                : "« Annuler » ne vaut que pour UN acte à la fois, comme dans RIG v2 : cochez une seule ligne.");

        // 2. L'acte, encore commande par le web et hors de la GED.
        var id = ids[0];
        int idActe = 0, flux = 0;
        int? idDossier = null;
        string depot = "", guid = "";
        var trouve = false;
        if (int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out idActe))
            await using (var r = await geste.LireAsync("""
                SELECT ad.ACTDP_CMDWEB_FLUX_ID, CAST(ad.ACTDP_GUID_VIDIGED AS varchar(36)) AS Guid, p.DPTRC_NUM_DEPOT,
                       (SELECT TOP (1) a.IdDossier FROM rec.ActionRegistre AS a WHERE a.IdDepot = p.DPTRC_ID_DPTRC AND a.IdDossier IS NOT NULL ORDER BY a.Id) AS IdDossier
                FROM v2.ACTE_DEPOSE AS ad WITH (UPDLOCK) LEFT JOIN v2.DEPOT_RCS AS p ON p.DPTRC_ID_DPTRC = ad.ACTDP_ID_DPTRC
                WHERE ad.ACTDP_ID_ACDPS = @id
                """, ct, new SqlParameter("@id", idActe)))
                if (await r.ReadAsync(ct))
                {
                    trouve = true;
                    flux = r["ACTDP_CMDWEB_FLUX_ID"] is DBNull ? 0 : Convert.ToInt32(r["ACTDP_CMDWEB_FLUX_ID"], CultureInfo.InvariantCulture);
                    guid = r["Guid"] is DBNull ? "" : FormeLigne.Texte(r, "Guid");
                    depot = FormeLigne.Texte(r, "DPTRC_NUM_DEPOT");
                    idDossier = r["IdDossier"] is int d ? d : null;
                }
        if (!trouve) return IssueGeste.Refuser($"Acte introuvable dans la base du greffe ({id}).");
        if (flux == 0) return IssueGeste.Refuser($"L'acte {id} du dépôt {depot} n'a plus de commande web : elle est déjà annulée.");
        if (guid.Length > 0) return IssueGeste.Refuser($"L'acte {id} du dépôt {depot} est déjà rangé dans la GED : sa commande web n'est plus en attente.");

        // OPE_RESULTATS.cs:104 (la base EDI, l. 89-99, n'est pas appelee : simulee).
        await geste.EcrireAsync("UPDATE v2.ACTE_DEPOSE SET ACTDP_CMDWEB_FLUX_ID = NULL WHERE ACTDP_ID_ACDPS = @id", ct, new SqlParameter("@id", idActe));
        var trace = new TraceGeste("dossier", $"l'acte déposé {idActe} du dépôt {depot}",
            (idDossier ?? idActe).ToString(CultureInfo.InvariantCulture), "a modifie",
            new Dictionary<string, string> { ["Commande web"] = $"flux {flux}" },
            new Dictionary<string, string>
            {
                ["Commande web"] = "annulée",
                ["Flux EDI"] = $"flux {flux} : annulation simulée (base EDI du greffe hors de la V3, non appelée)",
            });
        return IssueGeste.Ecrit(
            $"Commande web de l'acte du dépôt {depot} annulée : l'acte sort de la liste. Simulé : le flux {flux} n'est pas annulé dans la base EDI du greffe " +
            "(hors de la V3, non appelée) ; RIG v2 y écrit « Annulé ».",
            [idActe.ToString(CultureInfo.InvariantCulture)], trace);
    }
}
