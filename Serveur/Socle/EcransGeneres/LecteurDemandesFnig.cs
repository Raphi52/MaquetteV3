using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Demandes FNIG » (PROC_FNIG) : les demandes du greffe au fichier national des interdictions de gerer.
/// Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : PROC_FNIG (RIG\DLL\Processus\PROC_FNIG) et ses etapes
/// RIG\DLL\EtapeJudiciaire\Rig_Etape_Instance\ENTITE_JUDICIAIRE (ALL)\Saisie\ETP_FNIG. A l'ouverture,
/// OPE_CRITERE_RECHERCHE.DoChargerDonnees (CRITERE_RECHERCHE\OPE_CRITERE_RECHERCHE.cs:52-56) lance la recherche sans case
/// d'etat cochee, sans date et sans nom : Outils\DLL\RigMetier\CasierJudiciaire\CasierJudiciaireFnig.cs:261-302
/// (GetCasierJudiciaireByEtatFnig) ne pose alors AUCUN filtre -- SELECT TOP 100 ... FROM CASIER_JUDICIAIRE_FNIG ORDER BY
/// CFNIG_ID_CFNIG DESC, sur la base du greffe. Ici le meme ordre, au plafond de l'ecran. Les entreprises de la demande :
/// CASIER_JUDICIAIRE_FNIG_ENTREPRISE (CFNET_ID_CFNIG) ; une entreprise laissee vide (ni denomination ni SIREN) ne compte
/// pas, la V2 la retire a l'enregistrement (EDITION\OPE_EDITION.cs:226-231).
///
/// GROUPES : CFNIG_TYPE_DEMANDE_FNIG (eCasierJudiciaireTypeDemandeFnig) -- Ajout, Modification, Desactivation ; une demande
/// sans type est un « Ajout » (OPE_EDITION.cs:217-218 : une demande jamais enregistree devient un Ajout).
///
/// REGLE D'ETAT (CFNIG_ETAT_FNIG, eCasierJudiciaireEtatFnig ; marches du descripteur : 0 Personne, 1 Jugement,
/// 2 Entreprises, 3 Envoi). Valider passe par Verifier (OPE_EDITION.cs:215) : une demande validee a sa personne et son
/// jugement (0, 1) ; les entreprises (2) s'il en reste une renseignee.
///   Envoye      -> Validee « Envoyee » : exportee pour le FNIG, envoi fait (0, 1, 3) ;
///   Erreur      -> Rejetee « En erreur » : l'envoi a echoue, CFNIG_ERREUR_ENVOI_FNIG (0, 1, 3) ;
///   ADesactiver -> EnAttente « A desactiver » : une desactivation a envoyer (0, 1) ;
///   AEnvoyer    -> EnCours « En saisie » : validee (OPE_EDITION.cs:243-248), pas encore envoyee par le traitement de
///                  fond (0, 1) ;
///   sans etat   -> AFaire « Nouvelle demande » : jamais validee, aucune marche.
/// </summary>
internal sealed class LecteurDemandesFnig : ILecteurEcran
{
    public string Procedure => "PROC_FNIG";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Ajout = "Ajout", Modification = "Modification", Desactivation = "Désactivation";

    private const string Requete = """
        SELECT TOP (@plafond) f.CFNIG_ID_CFNIG, f.CFNIG_NOM, f.CFNIG_PRENOM, f.CFNIG_DATE_JUGEMENT, f.CFNIG_DATE_FIN_INTERDICTION,
               f.CFNIG_DATE_ENVOI, f.CFNIG_ETAT_FNIG, f.CFNIG_TYPE_DEMANDE_FNIG, LEFT(f.CFNIG_ERREUR_ENVOI_FNIG, 160) AS Erreur,
               e.Entreprises, p.CFNET_DENOMINATION, p.CFNET_SIREN, COUNT(*) OVER () AS Total
        FROM v2.CASIER_JUDICIAIRE_FNIG AS f
        OUTER APPLY (SELECT COUNT(*) AS Entreprises FROM v2.CASIER_JUDICIAIRE_FNIG_ENTREPRISE AS x
                     WHERE x.CFNET_ID_CFNIG = f.CFNIG_ID_CFNIG
                       AND (ISNULL(x.CFNET_DENOMINATION, '') <> '' OR ISNULL(x.CFNET_SIREN, '') <> '')) AS e
        OUTER APPLY (SELECT TOP (1) x.CFNET_DENOMINATION, x.CFNET_SIREN
                     FROM v2.CASIER_JUDICIAIRE_FNIG_ENTREPRISE AS x
                     WHERE x.CFNET_ID_CFNIG = f.CFNIG_ID_CFNIG
                       AND (ISNULL(x.CFNET_DENOMINATION, '') <> '' OR ISNULL(x.CFNET_SIREN, '') <> '')
                     ORDER BY x.CFNET_ID_CFNET) AS p
        ORDER BY f.CFNIG_ID_CFNIG DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var entreprises = Convert.ToInt32(r["Entreprises"]);
        // Les marches franchies d'une demande validee, plus les entreprises (2) s'il en reste une renseignee, dans l'ordre.
        int[] Marches(params int[] marches) => entreprises > 0 ? [.. marches.Append(2).Order()] : marches;
        var envoyee = FormeLigne.Date(FormeLigne.DateDe(r, "CFNIG_DATE_ENVOI"));
        var erreur = FormeLigne.Texte(r, "Erreur");

        // La regle d'etat (en tete du fichier).
        (string Etat, int[] Faites, string Statut) regle = FormeLigne.Texte(r, "CFNIG_ETAT_FNIG") switch
        {
            "Envoye" => ("Validee", Marches(0, 1, 3), envoyee.Length > 0 ? $"envoyée le {envoyee}" : "envoyée"),
            "Erreur" => ("Rejetee", Marches(0, 1, 3), erreur.Length > 0 ? $"envoi refusé : {erreur}" : "envoi refusé"),
            "ADesactiver" => ("EnAttente", Marches(0, 1), "désactivation à envoyer"),
            "AEnvoyer" => ("EnCours", Marches(0, 1), "à envoyer au FNIG"),
            _ => ("AFaire", [], "à valider"),
        };
        var groupe = FormeLigne.Texte(r, "CFNIG_TYPE_DEMANDE_FNIG") switch
        {
            "Modification" => Modification,
            "Desactivation" => Desactivation,
            _ => Ajout,
        };

        var nom = $"{FormeLigne.Texte(r, "CFNIG_NOM")} {FormeLigne.Texte(r, "CFNIG_PRENOM")}".Trim();
        if (nom.Length == 0) nom = $"Demande n° {FormeLigne.Texte(r, "CFNIG_ID_CFNIG")}";
        var jugement = FormeLigne.Date(FormeLigne.DateDe(r, "CFNIG_DATE_JUGEMENT"));
        var fin = FormeLigne.Date(FormeLigne.DateDe(r, "CFNIG_DATE_FIN_INTERDICTION"));
        // La premiere entreprise renseignee : sa denomination, sinon son SIREN.
        var entreprise = FormeLigne.Texte(r, "CFNET_DENOMINATION") is { Length: > 0 } denomination ? denomination
            : FormeLigne.Texte(r, "CFNET_SIREN") is { Length: > 0 } siren ? $"SIREN {FormeLigne.Siren(siren)}" : null;
        var precisions = FormeLigne.Joindre(
            jugement.Length > 0 ? $"jugement du {jugement}" : null,
            fin.Length > 0 ? $"interdiction jusqu'au {fin}" : null,
            entreprises > 1 ? $"{entreprise} et {entreprises - 1} autre(s)" : entreprise,
            regle.Statut);
        return new LigneLue(nom, precisions, groupe, regle.Etat, regle.Faites);
    }
}
