using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Actes rejetes » (PROC_ACTREJ) : les deux gestes du menu de la ligne de la V2, sur les actes coches. Mode d'emploi :
/// EcrivainEcran.cs ; la liste : LecteurActesRejetes.cs. Plan « RIG v3 100 % fonctionnel », lot 8, 26/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_ACTREJ\RESULTATS\OPE_RESULTATS.cs.
///   « Renvoyer a Infogreffe » (_RenvoyerAInfogreffe, l. 112-121) : UPDATE ACTE_DEPOSE SET ACTDP_EDI_INFOGREFFE_Etat_GEIDE =
///   '1' (eEtatDematGed.Prepare, ETAT_TRAITMENT.cs:54), ACTDP_EDI_INFOGREFFE_Image_ATransferer = 1, par acte coche
///   (ACTDP_ID_ACDPS, l. 131-139) ; l'acte repart dans la file d'envoi GEIDE et quitte la liste (RefreshRejets, l. 120).
///   « Annuler le rejet » (_AnnulerRejets, l. 122-130) : UPDATE ... SET ACTDP_EDI_INFOGREFFE_Etat_GEIDE = '99'
///   (eEtatDematGed.AnnulationAmitel, ETAT_TRAITMENT.cs:94, FDDA 17005) ; l'image a transferer n'est pas touchee.
///
/// CONTROLES, avant toute ecriture :
///   1. au moins un acte coche (l. 113, 123 : rien sans selection) ;
///   2. chaque acte coche est un acte de la base encore REJETE (etat 9) : la V2 n'agit que sur les lignes de sa grille, qui
///      ne montre que l'etat 9 (MetierActRejets.cs:81). Un acte deja renvoye ou annule est refuse, rien n'est ecrit ;
///   3. « Annuler le rejet » est reserve a l'equipe Amitel : la V2 n'affiche l'entree qu'a un utilisateur Amitel
///      (OPE_RESULTATS.cs:38-41, Utilisateur.IsUserAmitel). La V2 n'a pas de message (l'entree est cachee) : la V3 refuse en
///      le disant. Le compte V3 porte le meme drapeau (soc.Compte.EstAmitel, repris de UTILISATEUR par RepriseDroits).
///
/// NON REPRIS (nomme) : la V2 avale toute erreur d'ecriture ligne a ligne (catch vide, l. 141-142) ; la V3 ecrit tout ou rien,
/// dans la transaction du geste. Rien d'autre n'est ecrit par la V2 (ni demande, ni edition, ni journal).
/// Traces : famille « dossier », « a modifie », l'etat GEIDE avant et apres.
/// </summary>
internal sealed class EcrivainActesRejetes : IEcrivainEcran
{
    public string Procedure => "PROC_ACTREJ";
    public string Ecran => "Actes rejetés";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les gestes du descripteur, au mot pres (le menu de la ligne de la V2, OPE_RESULTATS.Designer.cs).</summary>
    internal const string Renvoyer = "Renvoyer à Infogreffe", Annuler = "Annuler le rejet";

    /// <summary>Les etats GEIDE ecrits (eEtatDematGed, ETAT_TRAITMENT.cs:54 et :94).</summary>
    internal const string EtatPrepare = "1", EtatAnnulationAmitel = "99";

    internal const string MessageAmitel = "L'annulation d'un rejet est réservée à l'équipe Amitel.";

    private const string Acte = """
        SELECT ad.ACTDP_ID_ACDPS, ad.ACTDP_EDI_INFOGREFFE_Etat_GEIDE AS Etat, p.DPTRC_NUM_DEPOT, p.DPTRC_ID_DPTRC,
               (SELECT TOP (1) a.IdDossier FROM rec.ActionRegistre AS a WHERE a.IdDepot = p.DPTRC_ID_DPTRC AND a.IdDossier IS NOT NULL ORDER BY a.Id) AS IdDossier
        FROM v2.ACTE_DEPOSE AS ad JOIN v2.DEPOT_RCS AS p ON p.DPTRC_ID_DPTRC = ad.ACTDP_ID_DPTRC
        WHERE ad.ACTDP_ID_ACDPS = @id
        """;

    private sealed record Ligne(int Id, string Depot, int? IdDossier);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var nom = geste.Demande.Nom;
        if (nom is not (Renvoyer or Annuler)) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        // 1. Une selection.
        if (ids.Length == 0) return IssueGeste.Refuser("Aucun acte coché : rien n'est fait.");
        // 3. L'annulation, reservee a l'equipe Amitel.
        if (nom == Annuler && !geste.Agent.EstAmitel) return IssueGeste.Refuser(MessageAmitel);

        // 2. Chaque acte, encore rejete.
        var lignes = new List<Ligne>();
        foreach (var id in ids)
        {
            if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return IssueGeste.Refuser($"Acte introuvable dans la base du greffe ({id}).");
            Ligne? l = null;
            var etat = "";
            await using (var r = await geste.LireAsync(Acte, ct, new SqlParameter("@id", n)))
                if (await r.ReadAsync(ct))
                {
                    etat = FormeLigne.Texte(r, "Etat");
                    l = new Ligne(n, FormeLigne.Texte(r, "DPTRC_NUM_DEPOT"), r["IdDossier"] is int d ? d : null);
                }
            if (l is null) return IssueGeste.Refuser($"Acte introuvable dans la base du greffe ({id}).");
            if (etat != LecteurActesRejetes.EtatRejete)
                return IssueGeste.Refuser($"L'acte {id} du dépôt {l.Depot} n'est plus rejeté : il a déjà été renvoyé ou annulé.");
            lignes.Add(l);
        }

        var nouvelEtat = nom == Renvoyer ? EtatPrepare : EtatAnnulationAmitel;
        var traces = new List<TraceGeste>();
        foreach (var l in lignes)
        {
            if (nom == Renvoyer)
                await geste.EcrireAsync("""
                    UPDATE v2.ACTE_DEPOSE SET ACTDP_EDI_INFOGREFFE_Etat_GEIDE = @etat, ACTDP_EDI_INFOGREFFE_Image_ATransferer = 1
                    WHERE ACTDP_ID_ACDPS = @id
                    """, ct, new SqlParameter("@etat", nouvelEtat), new SqlParameter("@id", l.Id));
            else
                await geste.EcrireAsync("UPDATE v2.ACTE_DEPOSE SET ACTDP_EDI_INFOGREFFE_Etat_GEIDE = @etat WHERE ACTDP_ID_ACDPS = @id", ct,
                    new SqlParameter("@etat", nouvelEtat), new SqlParameter("@id", l.Id));
            traces.Add(new TraceGeste("dossier", $"l'acte déposé {l.Id} du dépôt {l.Depot}",
                (l.IdDossier ?? l.Id).ToString(CultureInfo.InvariantCulture), "a modifie",
                new Dictionary<string, string> { ["Etat GEIDE"] = "9 (rejeté par Infogreffe)" },
                new Dictionary<string, string>
                {
                    ["Etat GEIDE"] = nom == Renvoyer ? "1 (à renvoyer à Infogreffe)" : "99 (rejet annulé par Amitel)",
                }));
        }
        var n2 = lignes.Count;
        var message = nom == Renvoyer
            ? $"{(n2 > 1 ? $"{n2} actes déposés renvoyés" : "Acte déposé renvoyé")} à Infogreffe : {(n2 > 1 ? "ils repartent" : "il repart")} dans la file d'envoi."
            : $"{(n2 > 1 ? $"{n2} rejets annulés" : "Rejet annulé")}.";
        return IssueGeste.Ecrit(message, [.. lignes.Select(l => l.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }
}
