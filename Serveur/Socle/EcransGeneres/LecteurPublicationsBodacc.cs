using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Publications BODACC » (PROC_GESTBOD, forme File) : les annonces BODACC des procedures rejetees par le BODACC, a rectifier ou
/// remises a renvoyer. Mode d'emploi des lecteurs : LecteurEcran.cs ; le geste : EcrivainPublicationsBodacc.cs (plan « RIG v3 100 %
/// fonctionnel », lot 11, 27/11/2026).
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_GESTBOD (proc_gestbod.md, Detail OPE, qui fait foi : la seule table est BODACC,
/// jointe a EVENEMENT_PROCEDURE, PROCEDURE_COLLECTIVE et PREVENTION). Quatre modes ; un seul ouvre sa liste SANS critere : la liste et
/// la mise a jour des rejets (Entree\OPE_LST_REJETS.MajSelectionBodacc, :55-90 -- la periode n'est ajoutee que si une date « du » est
/// saisie) : BODACC LEFT JOIN EVENEMENT_PROCEDURE (BDCCR_ID_EVNPR = EVNPR_ID_EVNPR), BDCCR_ETAT_BODACC = '9' (« rejet Bodacc, a
/// rectifier », ETAT_BODACC de RIG_DEV) et BDCCR_TYPE_BODACC LIKE 'P%'. L'affaire est celle de la procedure collective, sinon de la
/// prevention de l'evenement (RejetsBod.cs:41-71, comme CertifBod.cs:42-73) ; la V2 n'ordonne pas cette requete : ici, l'annonce.
/// Dans la base V3, BODACC est la table V2 telle quelle (Serveur/Recopie/FichesV3.cs, REC-015).
///
/// LES ANNONCES « A RENVOYER » : l'etat 3 (« rectif. (rejet) a envoyer ») est ce que la mise a jour des rejets ecrit (OPE_LST_LISTE_REJ
/// .DoSauver, :51-57) ; la V2 les retire de sa liste au rechargement. La V3 les garde dans le groupe des rejets, etat « A renvoyer »,
/// figees : c'est la relecture du geste, jusqu'au prochain envoi BODACC (Envoi JAL / BODACC, qui les passe a 4, OPE_A_ENVOYER.cs:1342-1358).
///
/// GROUPES : « Rejets ». « Certificats de parution » (etats 5, 6, 10) et « Controles non conformes » (BDCCR_CONTROLE_QUALITE) ne
/// s'ouvrent dans la V2 que sur une date ou un numero de parution (OPE_CERTIF_BOD.cs:80-117, OPE_LST_CTRLNC.cs:50-81) : la forme File de
/// cet ecran ne saisit pas de critere, ils restent vides (nomme dans le rendu du lot).
/// REGLE D'ETAT : etat 9 -> AFaire « A traiter » ; etat 3 -> EnAttente « A renvoyer ».
/// </summary>
internal sealed class LecteurPublicationsBodacc : ILecteurEcran
{
    public string Procedure => "PROC_GESTBOD";

    /// <summary>Le groupe du descripteur, au mot pres.</summary>
    internal const string Rejets = "Rejets";

    /// <summary>La requete ; « /*annonce*/ » recoit le filtre d'une annonce (l'ecrivain).</summary>
    internal const string Requete = """
        SELECT TOP (@plafond) b.BDCCR_ID_BDCRC, b.BDCCR_ETAT_BODACC, b.BDCCR_DATE_ENVOI, b.BDCCR_NUMERO_ANNONCE, b.BDCCR_MOTIF_REJET_BODACC_1,
               ev.EVNPR_CODE_EVE_PRO, ev.EVNPR_DATE, pc.PRCLL_NUM_PC, pc.PRCLL_NOM_AFFAIRE, pr.PREVE_NUM_PRV, pr.PREVE_NOM_AFFAIRE,
               COUNT(*) OVER () AS Total
        FROM v2.BODACC AS b
        LEFT JOIN v2.EVENEMENT_PROCEDURE AS ev ON b.BDCCR_ID_EVNPR = ev.EVNPR_ID_EVNPR
        LEFT JOIN v2.PROCEDURE_COLLECTIVE AS pc ON ev.EVNPR_ID_PRCLL = pc.PRCLL_ID_PRCLL
        LEFT JOIN v2.PREVENTION AS pr ON ev.EVNPR_ID_PREVE = pr.PREVE_ID_PREVE
        WHERE b.BDCCR_ETAT_BODACC IN ('9', '3') AND b.BDCCR_TYPE_BODACC LIKE 'P%' /*annonce*/
        ORDER BY b.BDCCR_ID_BDCRC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*annonce*/", "", StringComparison.Ordinal), Ligne, ct,
                                  new SqlParameter("@plafond", plafond));

    internal static LigneLue Ligne(SqlDataReader r)
    {
        var id = FormeLigne.Texte(r, "BDCCR_ID_BDCRC");
        var pc = FormeLigne.Texte(r, "PRCLL_NUM_PC");
        var prv = FormeLigne.Texte(r, "PREVE_NUM_PRV");
        // La prevention l'emporte quand les deux sont la (« le dernier renseigne gagne », CertifBod.cs:61-71).
        var affaire = prv.Length > 0 ? FormeLigne.Texte(r, "PREVE_NOM_AFFAIRE") : FormeLigne.Texte(r, "PRCLL_NOM_AFFAIRE");
        var ep = FormeLigne.Texte(r, "EVNPR_CODE_EVE_PRO");
        var envoi = FormeLigne.DateDe(r, "BDCCR_DATE_ENVOI");
        var aRenvoyer = FormeLigne.Texte(r, "BDCCR_ETAT_BODACC") == "3";
        var precisions = FormeLigne.Joindre(
            prv.Length > 0 ? $"prévention {prv}" : pc.Length > 0 ? $"procédure {pc}" : null,
            ep.Length > 0 ? FormeLigne.Joindre($"EP {ep}", FormeLigne.DateDe(r, "EVNPR_DATE") is { } d ? $"du {FormeLigne.Date(d)}" : null) : null,
            envoi is null ? null : $"envoyée le {FormeLigne.Date(envoi)}",
            FormeLigne.Texte(r, "BDCCR_NUMERO_ANNONCE") is { Length: > 0 } n ? $"annonce n° {n}" : null,
            FormeLigne.Texte(r, "BDCCR_MOTIF_REJET_BODACC_1") is { Length: > 0 } m ? $"rejet : {m}" : null,
            aRenvoyer ? "à renvoyer au prochain envoi" : "rejetée, à rectifier");
        var nom = affaire.Length > 0 ? affaire : $"Annonce BODACC n° {id}";
        return new LigneLue(nom, precisions, Rejets, aRenvoyer ? "EnAttente" : "AFaire", []) { Id = id };
    }
}
