using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Injonction de payer » (PROC_IPE, forme Saisie) : les saisies d'injonction de payer du greffe, sous le formulaire de
/// creation. Modes d'emploi : LecteurEcran.cs ; la creation : EcrivainInjonctionsDePayer.cs. Plan « RIG v3 100 % fonctionnel »,
/// lot 5, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_IPE (D:\RigApplication\Source\RIG\DLL\Processus\PROC_IPE, fiche proc_ipe.md). Une saisie est une
/// DEMANDE de code processus IPE, ou IPE_TRIDIG en circuit Tribunal digital (PROC_IPE.cs:87-90, le suffixe « _TRIDIG ») ;
/// son instance est celle de l'affaire de la demande (OPE_INSTANCE.CallPrepSauve : INSTN_ID_AFFRE = l'affaire de la demande,
/// OPE_INSTANCE.cs:1468-1469).
///
/// GROUPES (descripteur) : « Saisie directe » = IPE ; « Tribunal digital » = IPE_TRIDIG.
///
/// REGLE D'ETAT (DMND_ETAT_DEMANDE, ETAT_DEMANDE de RIG_DEV) et MARCHES FRANCHIES (rangs du descripteur : 0 Instance,
/// 1 Demandeur, 2 Défendeur, 3 Décision, obligatoires) :
///   - « Enregistrée » (Validee) : la demande est validee (V) -- le moteur de RIG v2 ne valide qu'etapes obligatoires
///     franchies : les quatre sont faites ;
///   - « Refusée » (Rejetee) : refus (O), le chemin refus de PROC_IPE (PROC_IPE.cs:261-277) ;
///   - « En attente du tribunal » (EnAttente) : en attente (W) ;
///   - « En cours » (EnCours) : interrompue (I) ou en cours (DMND_EN_COURS) ; ses marches sont lues dans la base : l'instance,
///     un demandeur (DOIP), un defendeur (FOIP) (Partie.cs:823-830), une decision de l'instance ;
///   - toute autre demande (supprimee X...) n'est pas une saisie a montrer.
/// ORDRE : la plus recente d'abord (DMND_ID_DMND decroissant).
/// </summary>
internal sealed class LecteurInjonctionsDePayer : ILecteurEcran
{
    public string Procedure => "PROC_IPE";

    internal const string SaisieDirecte = "Saisie directe", TribunalDigital = "Tribunal digital";

    private const string Requete = """
        SELECT TOP (@plafond) d.DMND_ID_DMND, d.DMND_NUM_DEMANDE, d.DMND_CODE_PROSS, d.DMND_ETAT_DEMANDE,
               CONVERT(bit, ISNULL(d.DMND_EN_COURS, 0)) AS EnCours, d.DMND_DATE_CREATION, i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE,
               i.INSTN_NOM_COURANT, i.INSTN_DATE_SAISINE_GREFFE,
               CASE WHEN EXISTS (SELECT 1 FROM v2.PARTIE AS p WHERE p.PRT_ID_INSTN = i.INSTN_ID_INSTN AND p.PRT_QUALIF_PARTIE = 'DOIP') THEN 1 ELSE 0 END AS Demandeur,
               CASE WHEN EXISTS (SELECT 1 FROM v2.PARTIE AS p WHERE p.PRT_ID_INSTN = i.INSTN_ID_INSTN AND p.PRT_QUALIF_PARTIE = 'FOIP') THEN 1 ELSE 0 END AS Defendeur,
               CASE WHEN EXISTS (SELECT 1 FROM v2.DECISION_JUDICIAIRE AS dj WHERE dj.DCJDC_ID_INSTN = i.INSTN_ID_INSTN) THEN 1 ELSE 0 END AS Decision,
               COUNT(*) OVER () AS Total
        FROM v2.DEMANDE AS d
        OUTER APPLY (SELECT TOP (1) x.* FROM v2.INSTANCE AS x WHERE x.INSTN_ID_AFFRE = d.DMND_ID_AFFRE ORDER BY x.INSTN_ID_INSTN) AS i
        WHERE d.DMND_CODE_PROSS IN ('IPE', 'IPE_TRIDIG')
          AND (d.DMND_ETAT_DEMANDE IN ('V', 'O', 'W', 'I') OR d.DMND_EN_COURS = 1)
        ORDER BY d.DMND_ID_DMND DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var etatV2 = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE");
        var aInstance = r["INSTN_ID_INSTN"] is not DBNull;
        var faites = new List<int>();
        if (aInstance) faites.Add(0);
        if (Convert.ToInt32(r["Demandeur"], CultureInfo.InvariantCulture) == 1) faites.Add(1);
        if (Convert.ToInt32(r["Defendeur"], CultureInfo.InvariantCulture) == 1) faites.Add(2);
        if (Convert.ToInt32(r["Decision"], CultureInfo.InvariantCulture) == 1) faites.Add(3);
        var (etat, faitesLigne) = etatV2 switch
        {
            "V" => ("Validee", new[] { 0, 1, 2, 3 }),
            "O" => ("Rejetee", faites.ToArray()),
            "W" => ("EnAttente", faites.ToArray()),
            _ => faites.Count == 0 ? ("AFaire", Array.Empty<int>()) : ("EnCours", faites.ToArray()),
        };
        var role = FormeLigne.Texte(r, "INSTN_NUM_ROLE");
        var nom = FormeLigne.Texte(r, "INSTN_NOM_COURANT");
        var saisine = FormeLigne.DateDe(r, "INSTN_DATE_SAISINE_GREFFE");
        var precisions = FormeLigne.Joindre(
            // Le numero de role une fois donne, celui de la demande avant (la demande se lit dans la fiche de RIG v2).
            role.Length > 0 ? $"Rôle {role}" : $"demande {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}",
            saisine is null ? null : $"saisine du {FormeLigne.Date(saisine)}",
            etatV2 == "I" ? "interrompue" : null);
        return new LigneLue(nom.Length > 0 ? nom : $"Demande {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}", precisions,
            FormeLigne.Texte(r, "DMND_CODE_PROSS") == "IPE_TRIDIG" ? TribunalDigital : SaisieDirecte, etat, faitesLigne)
        { Id = FormeLigne.Texte(r, "DMND_ID_DMND") };
    }
}
