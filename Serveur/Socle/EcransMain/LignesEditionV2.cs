using System.Text.RegularExpressions;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// UNE LIGNE D'EDITION « LOT UN » DE RIG v2 (v2.LIGNE_EDITION) : l'edition qu'une demande porte quand RIG v2 l'ajoute par son code de
/// modele de document (Demande.AddLigneEdition(codeModeleDoc, imprimante, bac, nombre, differe, sceau, rectoVerso),
/// Outils\DLL\RigMetier\Demande\Demande.cs:1423-1440), puis l'imprime en lot. Partagee par les gestes faits main du lot 11 du plan
/// « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026) : la liste des numeros chrono manquants (LNUMCM, modele
/// ALNUMCM_1_IMP) et le bordereau d'archivage (ARCHIRAD, modele BGEDANNEX_1_IMP).
///
/// SOURCE (ancien RIG) : LigneEdition.CreateLigneEdition (Outils\DLL\RigMetier\Edition\LigneEdition.cs:254-258) -- puis
/// CreateLigneEditionSansBac (:171-245) : la demande, le code du modele, la transmission (CodeTransmission, :74-80 : les trois lettres
/// apres « _&lt;chiffres&gt;_ » du code, « IMP » sinon), l'imprimante (LGEDT_DESTINATION_PHYSIQUE), le numero d'ordre (le rang de la ligne
/// dans la demande), le nombre d'exemplaires, differe ou non, SetEstNonImprime (LGEDT_ETAT_EDT « I », LGEDT_ETAT « 0 », :1294-1298),
/// LGEDT_ETAT_TRS « I », le sceau, LGEDT_NOMBRE_MODIF et LGEDT_WORD_VISIBLE faux, recto verso ; le bac (LGEDT_IMP_BAC). Les colonnes
/// NOT NULL que la V2 laisse a leur DEFAULT (sys.default_constraints de LIGNE_EDITION sur RIG_DEV, lu le 28/11/2026 : LGEDT_IMPRIMER
/// ((1)), les autres drapeaux ((0))) recoivent ce defaut, que la base V3 ne recopie pas. Une colonne absente de la base (un banc)
/// n'est pas ecrite.
/// NON REPRIS (nomme) : LGEDT_ID_UTLST (l'utilisateur V2 de l'agent, Utilisateur.GetUtilisateurCourantInDBInt : la V3 identifie l'agent
/// par son compte, la trace est le journal des gestes) ; LGEDT_ID_INFDS (le destinataire, pose par le tableau des editions de RIG v2 a
/// la validation : la ligne attend son traitement, comme une ligne que la V2 n'a pas encore imprimee).
/// </summary>
internal static class LignesEditionV2
{
    /// <summary>Le sceau que RIG v2 passe quand l'appelant n'en choisit pas (« aucun » : PROC_ARCHIRAD.cs:206).</summary>
    internal const string SansSceau = "aucun";

    /// <summary>CodeTransmission (LigneEdition.cs:74-80).</summary>
    internal static string Transmission(string codeModeleDoc)
        => Regex.Match(codeModeleDoc, ".*_[0-9]*_(...)") is { Success: true } m ? m.Groups[1].Value : "IMP";

    /// <summary>Ajoute a la demande <paramref name="idDemande"/> la ligne d'edition de <paramref name="codeModeleDoc"/>, de rang
    /// <paramref name="numOrdre"/>, un exemplaire, non differee, bac NORMAL, sans recto verso ; rend son identifiant.</summary>
    internal static async Task<int> AjouterAsync(GesteEnCours geste, int idDemande, string codeModeleDoc, short numOrdre, string? imprimante,
                                                string sceau, CancellationToken ct)
    {
        // CE QUI FAIT LA LIGNE : toujours écrit. Une base qui n'a pas une de ces colonnes lève (rien d'écrit) : une ligne sans sa
        // demande, son modèle ou son état n'est pas une édition (décision du lot 7 du plan, 28/11/2026 : l'écrivain ne filtre jamais les
        // colonnes essentielles -- mesuré : la v2.LIGNE_EDITION du banc du parcours n'avait pas LGEDT_CODE_MODELE_DOC).
        var valeurs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["LGEDT_ID_DMND"] = idDemande, ["LGEDT_CODE_MODELE_DOC"] = codeModeleDoc, ["LGEDT_TRANSMISSION"] = Transmission(codeModeleDoc),
            ["LGEDT_NUM_ORDRE"] = numOrdre, ["LGEDT_NOMBRE"] = (short)1, ["LGEDT_ETAT_EDT"] = "I", ["LGEDT_ETAT"] = "0", ["LGEDT_ETAT_TRS"] = "I",
            ["LGEDT_SCEAU"] = sceau, ["LGEDT_IMP_BAC"] = "NORMAL",
        };
        // Le reste : l'imprimante, et les drapeaux à leur DEFAULT de RIG_DEV (voir l'en-tête), écrits si la base les porte.
        var accessoires = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["LGEDT_DESTINATION_PHYSIQUE"] = string.IsNullOrWhiteSpace(imprimante) ? null : imprimante, ["LGEDT_DIFFERE"] = false,
            ["LGEDT_NOMBRE_MODIF"] = false, ["LGEDT_WORD_VISIBLE"] = false, ["LGEDT_RECTO_VERSO"] = false,
            ["LGEDT_IMPRIMER"] = true, ["LGEDT_TRAITEE"] = false, ["LGEDT_RETENTION"] = false, ["LGEDT_HORS_EDITION_MQT"] = false,
            ["LGEDT_EN_COURS"] = false, ["LGEDT_A_EDITER"] = false, ["LGEDT_A_TRANSMETTRE"] = false, ["LGEDT_FICHIER_SOURCE"] = false,
            ["LGEDT_CERTIFICAT_ELECTRONIQUE"] = false, ["LGEDT_EST_PDF"] = false, ["LGEDT_NON_SUPPRESSIBLE"] = false,
            ["LGEDT_NON_MODIFIABLE"] = false,
        };
        var presentes = await EditionsV2.ColonnesAsync(geste, "v2.LIGNE_EDITION", ct);
        foreach (var (colonne, valeur) in accessoires.Where(a => presentes.Contains(a.Key))) valeurs[colonne] = valeur;
        return await geste.InsererAsync("v2.LIGNE_EDITION", "LGEDT_ID_LGEDT", valeurs, ct);
    }
}
