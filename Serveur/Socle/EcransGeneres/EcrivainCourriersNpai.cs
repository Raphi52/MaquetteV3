using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Courriers revenus NPAI » (PROC_TRAITEMENTNPAI) : « Annuler » l'action NPAI restee en cours. Mode d'emploi : EcrivainEcran.cs ; la
/// liste : LecteurCourriersNpai.cs (Id = la demande NPAI). Plan « RIG v3 100 % fonctionnel », lot 16, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RCS\Exe\PROC_TraitementNpai\FrmProcTraitementNpai.cs. Choisir une action cree la
/// demande NPAI qualifiee (Q) du dossier (affichageDiriLS l. 1172-1180 et ses soeurs) ; le bouton « Annuler » (btAnnule,
/// FrmProcTraitementNpai.Designer.cs:391) la passe en X et la sort du traitement : DMND_ETAT_DEMANDE = « X », DMND_EN_COURS = faux
/// (btAnnule_Click, l. 1101-1107) ; fermer l'ecran fait de meme pour une demande encore qualifiee (_Quitter, l. 258-269, et
/// _RetourEtatInitial, l. 242-251). Une demande NPAI encore « Q » dans la base est donc une action que personne n'a validee ni annulee
/// (ecran ferme brutalement) : la V3 la clot comme la V2 l'aurait fait. Le lecteur ne montre pas les X : la ligne quitte la liste.
/// CONTROLES, avant toute ecriture :
///   1. au moins une ligne cochee ;
///   2. chaque demande est une demande NPAI encore QUALIFIEE (« Q ») : la V2 n'annule que l'action en cours (_Quitter teste « Q »,
///      l. 261) ; une demande validee ou dans un autre etat est refusee (messages sans source V2 : le bouton n'existe que sur l'action
///      ouverte).
/// NON REPRIS (nomme) : « Valider » une action. La V2 valide l'action qu'elle vient d'ouvrir sur le courrier FLASHE (le code Data ou le
///   numero de demande saisi) : elle note sur la demande d'origine « <action>=<n° NPAI> » (notifierAncienneDemande, l. 2173-2181) et,
///   selon l'action, edite la lettre ou la LRAR (ImpressionImmediateEnBatch, l. 1484...), cree la mention d'office ou la formalite
///   de radiation. Une demande Q laissee dans la base ne porte pas la demande d'origine (elle n'est notee qu'a la validation) : la V3
///   ne peut pas la valider sans inventer ce lien ; flasher un courrier revenu n'a pas de geste dans une file (aucune ligne a cocher).
/// Traces : famille « demande », la demande NPAI en mots, son id, « a modifie », l'etat avant et apres.
/// </summary>
internal sealed class EcrivainCourriersNpai : IEcrivainEcran
{
    public string Procedure => "PROC_TRAITEMENTNPAI";
    public string Ecran => "Courriers revenus NPAI";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur, au mot pres : le bouton btAnnule (FrmProcTraitementNpai.Designer.cs:391).</summary>
    internal const string Annuler = "Annuler";

    private sealed record Demande(int Id, string Numero, string Etat, string Code);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Annuler) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        // 1. Une selection.
        if (ids.Length == 0) return IssueGeste.Refuser("Aucun courrier coché : rien n'est annulé.");

        // 2. Chaque demande, une action NPAI encore qualifiee.
        var demandes = new List<Demande>();
        foreach (var id in ids)
        {
            Demande? d = null;
            if (int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                await using (var r = await geste.LireAsync("""
                    SELECT DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS FROM v2.DEMANDE WITH (UPDLOCK) WHERE DMND_ID_DMND = @id
                    """, ct, new SqlParameter("@id", n)))
                    if (await r.ReadAsync(ct))
                        d = new Demande(n, FormeLigne.Texte(r, "DMND_NUM_DEMANDE"), FormeLigne.Texte(r, "DMND_ETAT_DEMANDE"), FormeLigne.Texte(r, "DMND_CODE_PROSS"));
            if (d is null || d.Code != "NPAI") return IssueGeste.Refuser($"Courrier revenu introuvable dans la base du greffe ({id}).");
            if (d.Etat != "Q")
                return IssueGeste.Refuser(d.Etat == "V"
                    ? $"La demande n° {d.Numero} est déjà traitée : RIG v2 n'annule que l'action en cours."
                    : $"La demande n° {d.Numero} n'est pas une action en cours (état « {d.Etat} » de l'ancien RIG) : RIG v2 n'annule que l'action qualifiée.");
            demandes.Add(d);
        }

        var traces = new List<TraceGeste>();
        foreach (var d in demandes)
        {
            // btAnnule_Click, l. 1104-1106.
            await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_ETAT_DEMANDE = 'X', DMND_EN_COURS = 0 WHERE DMND_ID_DMND = @id", ct,
                new SqlParameter("@id", d.Id));
            traces.Add(new TraceGeste("demande", $"la demande NPAI n° {d.Numero}", d.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new Dictionary<string, string> { ["Etat"] = "Q (qualifiée : action en cours)" },
                new Dictionary<string, string> { ["Etat"] = "X (supprimée : action annulée)" }));
        }
        var nb = demandes.Count;
        return IssueGeste.Ecrit(
            nb > 1 ? $"{nb} actions NPAI annulées : leurs demandes passent à « supprimée », comme le bouton « Annuler » de RIG v2."
                   : $"Action NPAI annulée : la demande n° {demandes[0].Numero} passe à « supprimée », comme le bouton « Annuler » de RIG v2.",
            [.. demandes.Select(d => d.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }
}
