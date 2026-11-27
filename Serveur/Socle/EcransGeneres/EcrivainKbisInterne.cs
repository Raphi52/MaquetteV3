using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurKbisInterne;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Édition interne d'un Kbis » (PROC_XEX) : « Éditer le Kbis » valide la demande XEX du dossier choisi, comme RIG v2. Mode d'emploi :
/// EcrivainEcran.cs ; les lignes : LecteurKbisInterne.cs. Plan « RIG v3 100 % fonctionnel », lot 13, 27/11/2026.
///
/// SOURCE (ancien RIG) : PROC_XEX (proc_xex.md, Detail OPE) : code processus XEX, non facture (MetierFacture = false,
/// PROC_XEX.Designer.cs:56-58), date de travail = le jour (MetierCodeDateTravail = null, PROC_XEX.cs:18). A la selection du dossier,
/// la demande recoit DMND_ID_DSRCS, DMND_NUM_INDICATIF = DSSRC_NUM_GESTION, DMND_NOM = la designation sans civilite
/// (PROC_XEX.cs:24-27) ; DoValider refuse sans dossier (« if (MetierBase == null) return false », PROC_XEX.cs:37-40). Les demandes XEX
/// de RIG_DEV (lues le 27/11/2026) sont validees (etat V), sans DOSSIER_DEMANDE ni DEMANDE_EDITION.
/// CE QUI S'ECRIT : une demande v2.DEMANDE validee, numero DMD (EditionsV2.DemandeProcessusValideeAsync), puis son dossier, son
/// indicatif et son nom. C'est le seul ecrit de RIG v2 : la demande n'est pas qu'une trace du journal, elle porte l'historique des
/// Kbis internes du dossier (LecteurKbisInterne) -- elle s'ecrit donc comme la V2.
/// NON REPRIS (nommes) : l'extrait Kbis lui-meme, que le service Kbis de RIG v2 produit hors de ce processus (aucune demande
/// d'edition dans RIG_DEV) : jamais appele ; l'affaire de la demande (DMND_ID_AFFRE), que le moteur de RIG v2 pose et que la fiche
/// rec.DossierRcs ne porte pas.
/// TRACE : famille « dossier », « a cree », la demande et son numero.
/// </summary>
internal sealed class EcrivainKbisInterne : IEcrivainEcran
{
    public string Procedure => "PROC_XEX";
    public string Ecran => "Édition interne d'un Kbis";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Confirmer];

    internal const string SansDossier = "Choisissez un dossier RCS par son numéro de gestion : RIG v2 ne valide pas sans dossier.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids is not [var id] || CriteresConsultation.Entier(id) is not { } idDossier) return IssueGeste.Refuser(SansDossier);
        string numero, designation;
        await using (var r = await geste.LireAsync($"""
            SELECT s.NumeroGestion, {Designation} AS Designation FROM rec.DossierRcs AS s
            LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise WHERE s.Id = @id
            """, ct, new SqlParameter("@id", idDossier)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Dossier RCS introuvable dans la base du greffe ({id}).");
            numero = FormeLigne.Texte(r, "NumeroGestion");
            designation = FormeLigne.Texte(r, "Designation");
        }
        var (idDemande, numeroDemande, refus) = await EditionsV2.DemandeProcessusValideeAsync(geste, "XEX", designation, ct);
        if (refus is not null) return IssueGeste.Refuser(refus);
        await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_ID_DSRCS = @dossier, DMND_NUM_INDICATIF = @numero WHERE DMND_ID_DMND = @demande", ct,
            new SqlParameter("@dossier", idDossier), new SqlParameter("@numero", numero.Length > 14 ? numero[..14] : numero),
            new SqlParameter("@demande", idDemande));
        var trace = new TraceGeste("dossier", $"le Kbis interne du dossier {numero}", id, "a cree", null,
            new { Demande = numeroDemande, Processus = "XEX", Dossier = numero, Designation = designation });
        return IssueGeste.Ecrit($"Kbis interne du dossier {numero} édité : demande {numeroDemande} validée.", [id], trace);
    }
}
