using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Transmission » (PROC_TRANSDOC) : « Modifier » une ligne de la grille enregistre son NUMERO D'AFFAIRE, la seule colonne que RIG v2
/// laisse saisir. Mode d'emploi : EcrivainEcran.cs ; la grille : LecteurTransmissionDocuments.cs (plan « RIG v3 100 % fonctionnel »,
/// lot 11, 27/11/2026).
///
/// SOURCE (ancien RIG) : Etapes\OPE_LISTE_FLUX_DOC.SauveNumAffaire (:586-622), appele par ACTUALISER et a la sortie : pour chaque ligne
/// dont le numero d'affaire (colNumRole) a change, la fiche de document de sa demande -- DematTriDigCREFO.GetDematTridCREFOByDemande
/// pour le tribunal digital (DTDCREFO_NUM_AFFAIRE), DematCfecAjmj.GetDematCfecAjmjByDemande pour le coffre-fort (CFECAJ_NUMERO_AFF),
/// la premiere fiche (la plus petite cle, comme la grille) -- recoit la valeur saisie, telle quelle (aucun controle de format dans la
/// V2 ; vide efface le numero). La demande reste qualifiee : elle reste dans la grille.
/// CONTROLES : la demande est une ligne de la grille (DOC_TRIDIG ou DCOFFRE, qualifiee, avec sa fiche) ; seul le numero d'affaire
/// s'enregistre (les autres colonnes sont en lecture dans la V2).
/// NON REPRIS (nomme) : « Visualisation » (ouvrir le document) et le lancement de VJUD / VJUDPR (un autre ecran) n'ecrivent rien dans
/// la V2 ; il n'y a ni ajout ni suppression de ligne dans la V2 -- l'ecran le dit (« Demonstration »).
/// </summary>
internal sealed class EcrivainTransmissionDocuments : IEcrivainEcran
{
    public string Procedure => "PROC_TRANSDOC";

    public string Ecran => "Transmission";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Modifier];

    /// <summary>La colonne editable du descripteur, au mot pres.</summary>
    internal const string Colonne = "Numéro d'affaire";

    internal const string Introuvable = "Document introuvable parmi les documents à transmettre : relisez la liste.";
    internal const string SeulNumero = "Seul le numéro d'affaire s'enregistre : RIG v2 n'en laisse saisir aucune autre colonne.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids.Length != 1 || !int.TryParse(geste.Demande.Ids[0], NumberStyles.None, CultureInfo.InvariantCulture, out var demande))
            return IssueGeste.Refuser(Introuvable);
        if (!geste.Demande.Valeurs.TryGetValue(Colonne, out var saisi)) return IssueGeste.Refuser(SeulNumero);
        var numero = saisi.Trim();

        string? code = null, nom = null, avant = null;
        await using (var r = await geste.LireAsync(LecteurTransmissionDocuments.Requete.Replace("/*demande*/", "AND d.DMND_ID_DMND = @demande", StringComparison.Ordinal),
                         ct, new SqlParameter("@plafond", 1), new SqlParameter("@demande", demande)))
            if (await r.ReadAsync(ct))
                (code, nom, avant) = (FormeLigne.Texte(r, "DMND_CODE_PROSS"), FormeLigne.Texte(r, "DMND_NOM"), FormeLigne.Texte(r, "Affaire"));
        if (code is null) return IssueGeste.Refuser(Introuvable);

        // La premiere fiche de la demande (la plus petite cle), comme la grille.
        var ecrit = code == "DOC_TRIDIG"
            ? await geste.EcrireAsync("""
                UPDATE v2.DEMAT_TRIBUNAL_DIGITAL_CREFO SET DTDCREFO_NUM_AFFAIRE = @n
                WHERE DTDCREFO_ID_DTDCREFO = (SELECT MIN(f.DTDCREFO_ID_DTDCREFO) FROM v2.DEMAT_TRIBUNAL_DIGITAL_CREFO AS f WHERE f.DTDCREFO_ID_DMND = @d)
                """, ct, new SqlParameter("@n", numero.Length == 0 ? DBNull.Value : numero), new SqlParameter("@d", demande))
            : await geste.EcrireAsync("""
                UPDATE v2.DEMAT_CFEC_AJMJ SET CFECAJ_NUMERO_AFF = @n
                WHERE CFECAJ_ID_CFECAJ = (SELECT MIN(f.CFECAJ_ID_CFECAJ) FROM v2.DEMAT_CFEC_AJMJ AS f WHERE f.CFECAJ_ID_DMND = @d)
                """, ct, new SqlParameter("@n", numero.Length == 0 ? DBNull.Value : numero), new SqlParameter("@d", demande));
        if (ecrit == 0) return IssueGeste.Refuser(Introuvable);
        var document = string.IsNullOrEmpty(nom) ? $"la demande {demande}" : nom;
        var idTexte = demande.ToString(CultureInfo.InvariantCulture);
        return IssueGeste.Ecrit(numero.Length == 0 ? $"Numéro d'affaire effacé : {document}." : $"Numéro d'affaire {numero} enregistré : {document}.",
            [idTexte], new TraceGeste("dossier", $"le document reçu {document}", idTexte, "a modifie",
                new Dictionary<string, string> { [Colonne] = avant ?? "" }, new Dictionary<string, string> { [Colonne] = numero }));
    }
}
