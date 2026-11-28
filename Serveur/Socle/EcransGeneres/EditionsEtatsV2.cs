using System.Globalization;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// LES ETATS DE RIG v2 EDITES DANS LA FILE DES EDITIONS, et le DROIT D'EXECUTER UN PROCESSUS (plan-f4fb097f, lot 4, 28/11/2026).
/// Utilise par EcrivainJournalCaisse.cs, EcrivainJournalVente.cs et EcrivainChequesEmis.cs (l'etat) ; EcrivainRefacturation.cs (le droit).
///
/// LE DROIT (ancien RIG) : FORM_ACCUEIL._ExecuteProcessus (D:\RigApplication\Source\RIG\DLL\Processus\PROC_ACCUEIL\FORM_ACCUEIL.cs:1606-1626)
/// n'ouvre un processus que si Utilisateur.ALaFonctionnalite(code) (Outils\DLL\RigMetier\Habilitation\Utilisateur.cs:298-345) ; sinon
/// « Vous n'êtes pas autorisé à exécuter le processus '&lt;code&gt;'. » (:1624). La V3 tient cette reponse dans UN modele, la vue soc.Droit
/// (SOC-002_droits.sql:52-59) : les fonctions des roles du compte, plus ses droits directs repris de l'ancien systeme et des menus de
/// comptabilite (RepriseDroits.ReponsesV2, l'ordre de ALaFonctionnalite). C'est le seul droit que la V2 exige de ces ecrans : leur code
/// ne controle rien d'autre (aucun appel de droit dans PROC_JCAISSE, PROC_JVENTE, PROC_EDCHQEMIS, PROC_FACTREEDIT).
///
/// L'ETAT (decision du chef du plan, lot 4 : « une edition = une demande dans la file des editions, comme EcrivainFileEditions.cs ») :
/// RIG v2 genere ces etats Crystal a l'ecran (RigCrystalReport, D:\RigApplication\Source\DllRapportCrystal\RigCrystalFactu), sans
/// DEMANDE_EDITION ; RIG v3 n'a pas de generateur Crystal (hors perimetre du plan : l'impression reelle des editions). Le geste pose
/// donc l'etat dans la file des editions (v2.DEMANDE_EDITION, phase C « creee ») par EditionsV2.DemandeEditionAsync (les valeurs de
/// creation de DemandeEdition.CreateDemandeEdition, DemandeEdition.cs:536-570), sans demande V2 ni entite source (DMNED_ID_DMND vide,
/// comme les registres judiciaires : la trace est le journal des gestes), type FUSION_CRYSTAL, le processus V2 (DMNED_CODE_PROCESSUS),
/// et la liste de parametres que la V2 donne a son etat (_GetLstParam : CodeGreffe, TitreEtat, NOMRPT, REQUETE -- JournalCaisse.cs:241-247,
/// JournalVente.cs:598-603, EmisTropPercus.cs:200-206), serialisee comme ListeParametre.GetXmlParametre (Outils\AmiDLL\AmiSystem\
/// ListeParametre.cs:310-331 : un element PARAMETRES, un attribut par cle, cles triees) dans DMNED_PARAMETRE_GENERATION. REQUETE relit
/// EXACTEMENT les lignes editees (la V2 : sa table temporaire ##..., remplie par sa requete) ; NB_LIGNES et TOTAL, ajoutes par la V3,
/// portent ce que l'etat compte et totalise (le banc les compare a un SELECT).
/// NON REPRIS (nommes) : CodeGreffe et les parametres de pied de page du greffe (GreffeAdresseComplete, GreffeVille, GreffeTelephone,
/// GreffeNom) -- la demande est dans la base DU greffe, le generateur les relit de lui ; l'apercu Crystal a l'ecran -- l'ecran de la V3
/// montre deja les lignes et leurs totaux.
/// </summary>
internal static class EditionsEtatsV2
{
    /// <summary>Le refus de la V2 (FORM_ACCUEIL.cs:1624), au mot pres.</summary>
    internal static string NonAutorise(string processus) => $"Vous n'êtes pas autorisé à exécuter le processus '{processus}'.";

    /// <summary>Un etat sans ligne (RigCrystalReport.NbLignes == 0 : JournalCaisse.cs:216-218, JournalVente.cs:578-579), au mot pres.</summary>
    internal const string AucunElement = "Aucun élément trouvé";

    /// <summary>L'agent a la fonction <paramref name="processus"/> (soc.Droit : roles et droits directs) -- ALaFonctionnalite de la V2.</summary>
    internal static async Task<bool> ProcessusAutoriseAsync(GesteEnCours geste, string processus, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT COUNT(*) FROM soc.Droit WHERE IdCompte = @agent AND Fonction = @fonction", ct,
            new SqlParameter("@agent", geste.Agent.Id), new SqlParameter("@fonction", processus));
        return await r.ReadAsync(ct) && r.GetInt32(0) > 0;
    }

    /// <summary>Des identifiants d'ecran en entiers (une liste IN sans texte saisi) ; null si l'un d'eux n'en est pas un.</summary>
    internal static int[]? Entiers(IEnumerable<string> ids)
    {
        var lus = new List<int>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
            lus.Add(n);
        }
        return [.. lus];
    }

    /// <summary>Un etat a poser : son titre (TitreEtat de la V2), son fichier Crystal (NOMRPT), la requete de ses lignes (REQUETE), le
    /// nombre de lignes, leur total, et d'autres totaux de l'etat (le HT d'un journal de vente : TOTAL_HT).</summary>
    internal sealed record Etat(string Processus, string Titre, string NomRpt, string Requete, int Lignes, decimal Total,
                                IReadOnlyDictionary<string, decimal>? Autres = null);

    /// <summary>
    /// Pose <paramref name="etat"/> dans la file des editions (en tete) et rend l'id de la demande d'edition. Les colonnes que la base
    /// du greffe n'a pas (un banc) sont laissees, comme EditionsV2.DemandeEditionAsync.
    /// </summary>
    internal static async Task<int> PoserAsync(GesteEnCours geste, Etat etat, CancellationToken ct)
    {
        var parametre = new EditionsV2.ParametreEdition(etat.Processus, etat.Titre, "FUSION_CRYSTAL", null, null);
        var id = await EditionsV2.DemandeEditionAsync(geste, null, parametre, Couper(etat.Titre, 255), null, null, null, null, ct);
        var colonnes = await EditionsV2.ColonnesAsync(geste, "v2.DEMANDE_EDITION", ct);
        var apres = new List<(string Colonne, object Valeur)>();
        if (colonnes.Contains("DMNED_CODE_PROCESSUS")) apres.Add(("DMNED_CODE_PROCESSUS", etat.Processus));
        if (colonnes.Contains("DMNED_PARAMETRE_GENERATION")) apres.Add(("DMNED_PARAMETRE_GENERATION", ListeParametre(etat)));
        if (apres.Count > 0)
            await geste.EcrireAsync(
                $"UPDATE v2.DEMANDE_EDITION SET {string.Join(", ", apres.Select(a => $"{a.Colonne} = @{a.Colonne}"))} WHERE DMNED_ID_DMNED = @id", ct,
                [.. apres.Select(a => new SqlParameter("@" + a.Colonne, a.Valeur)), new SqlParameter("@id", id)]);
        return id;
    }

    /// <summary>La liste de parametres de l'etat, comme ListeParametre.GetXmlParametre (en tete) : &lt;PARAMETRES cle="valeur" ... /&gt;,
    /// cles triees comme la V2 (OrderBy de la culture).</summary>
    internal static string ListeParametre(Etat etat)
    {
        var parametres = new Dictionary<string, string>
        {
            ["TitreEtat"] = etat.Titre, ["NOMRPT"] = etat.NomRpt, ["REQUETE"] = etat.Requete,
            ["NB_LIGNES"] = etat.Lignes.ToString(CultureInfo.InvariantCulture), ["TOTAL"] = etat.Total.ToString("0.00", CultureInfo.InvariantCulture),
        };
        foreach (var (cle, montant) in etat.Autres ?? new Dictionary<string, decimal>()) parametres[cle] = montant.ToString("0.00", CultureInfo.InvariantCulture);
        return new XElement("PARAMETRES", parametres.OrderBy(p => p.Key, StringComparer.InvariantCulture).Select(p => new XAttribute(p.Key, p.Value)))
            .ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Une date de titre, comme la V2 (Date.ToString("dd/MM/yyyy")).</summary>
    internal static string Jour(DateTime d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Couper(string s, int n) => s.Length > n ? s[..n] : s;
}
