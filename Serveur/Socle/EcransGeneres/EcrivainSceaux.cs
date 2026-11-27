using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Sceaux » (PROC_SCEAU) : SUPPRIMER un element de fusion, dans la grille. Mode d'emploi : EcrivainEcran.cs ; les lignes :
/// LecteurSceaux.cs. Plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026.
///
/// SOURCE (ancien RIG) : Edition\OPE_EDITION.Supprimer (D:\RigApplication\Source\RIG\DLL\Processus\PROC_SCEAU, l. 174-179) : apres la
/// confirmation « Attention, la suppression de ce sceau impactera l'ensemble des maquettes l'utilisant. Voulez-vous continuer ? »,
/// ElementFusion.Delete supprime la ligne ELEMENT_FUSION ; le document GED de l'image N'EST PAS supprime (ElementFusion.cs:124-126, bloc
/// gedRig.Delete commente) : la V3 fait de meme. Aucun controle de la V2 ne refuse la suppression.
/// NON REPRIS (nommes) : AJOUTER et MODIFIER. Ajouter importe le fichier de l'image dans la GED (GedRig.CreateGedRig sur un chemin local,
/// OPE_EDITION.cs:93-96 ; « Le fichier est obligatoire », :163-166) ; modifier la hauteur recalcule la largeur sur l'image
/// (AmiImage.ZoomLargeur, :69, :281) et Valider re-archive le document GED (:99-109) : la V3 n'importe ni ne decode d'image ici. Ces
/// deux gestes restent a l'ecran de la V2.
/// TRACE : famille « reglage », « a supprime ».
/// </summary>
internal sealed class EcrivainSceaux : IEcrivainEcran
{
    public string Procedure => "PROC_SCEAU";
    public string Ecran => "Sceaux";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Supprimer];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (CriteresConsultation.Entier(id) is not { } n) return IssueGeste.Refuser($"Sceau introuvable dans la base du greffe ({id}).");
        string reference, numero;
        await using (var r = await geste.LireAsync("SELECT ELMFS_REFERENCE, ELMFS_NUM_DOCUMENT FROM v2.ELEMENT_FUSION WITH (UPDLOCK) WHERE ELMFS_ID_ELMFS = @id", ct,
                         new SqlParameter("@id", n)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Sceau introuvable dans la base du greffe ({id}).");
            reference = FormeLigne.Texte(r, "ELMFS_REFERENCE");
            numero = FormeLigne.Texte(r, "ELMFS_NUM_DOCUMENT");
        }
        await geste.EcrireAsync("DELETE FROM v2.ELEMENT_FUSION WHERE ELMFS_ID_ELMFS = @id", ct, new SqlParameter("@id", n));
        return IssueGeste.Ecrit($"Sceau {reference} supprimé : les maquettes qui l'utilisent ne le trouveront plus (son image reste dans la GED, comme dans RIG v2).",
            [id], new TraceGeste("reglage", $"le sceau {reference}", id, "a supprime",
                new Dictionary<string, string> { ["Référence"] = reference, ["Document GED"] = numero }, null));
    }
}
