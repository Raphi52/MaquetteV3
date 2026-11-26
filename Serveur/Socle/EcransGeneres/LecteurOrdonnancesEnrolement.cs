using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Ordonnance d'enrôlement » (PROC_CORDJRCS, forme Saisie) : les ordonnances RCS saisies au greffe, sous le formulaire de
/// creation. Modes d'emploi : LecteurEcran.cs ; la creation : EcrivainOrdonnancesEnrolement.cs. Plan « RIG v3 100 % fonctionnel »,
/// lot 5, 26/11/2026.
///
/// SOURCE (ancien RIG) : une saisie est une DEMANDE de code processus CORDJRCS (PROC_CORDJRCS.Designer.cs, MetierCodeProcessus) ;
/// son instance est celle de l'affaire de la demande (OPE_INSTANCE.CallPrepSauve, OPE_INSTANCE.cs:1468-1469).
///
/// GROUPES (descripteur), lus de l'etat de la demande (ETAT_DEMANDE de RIG_DEV) : « Refus » = refus (O), le chemin refus du
/// processus (OPREQREFUS, fiche proc_cordjrcs.md:102) ; « Réclamation » = reclamation (N, JRECLAM) ; « Enrôlement sur requête » =
/// les autres.
/// REGLE D'ETAT et MARCHES (rangs du descripteur : 0 Instance, 1 Demandeur, 2 Défendeur, 3 Décision, obligatoires) :
///   - validee (V) : « Enregistrée » (Validee), les quatre marches -- le moteur de RIG v2 ne valide qu'etapes obligatoires
///     franchies ;
///   - sinon, les marches lues dans la base (l'instance, un demandeur D, un defendeur F, une decision de l'instance) : aucune ->
///     « À rédiger » (AFaire) ; toutes les obligatoires -> Validee ; sinon « En cours » (EnCours). Les demandes supprimees (X) ne
///     sont pas montrees.
/// ORDRE : la plus recente d'abord.
/// </summary>
internal sealed class LecteurOrdonnancesEnrolement : ILecteurEcran
{
    public string Procedure => "PROC_CORDJRCS";

    internal const string SurRequete = "Enrôlement sur requête", Refus = "Refus", Reclamation = "Réclamation";

    private const string Requete = """
        SELECT TOP (@plafond) d.DMND_ID_DMND, d.DMND_NUM_DEMANDE, d.DMND_ETAT_DEMANDE, i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE,
               i.INSTN_NOM_COURANT, i.INSTN_DATE_SAISINE_GREFFE,
               CASE WHEN EXISTS (SELECT 1 FROM v2.PARTIE AS p WHERE p.PRT_ID_INSTN = i.INSTN_ID_INSTN AND p.PRT_QUALIF_PARTIE = 'D') THEN 1 ELSE 0 END AS Demandeur,
               CASE WHEN EXISTS (SELECT 1 FROM v2.PARTIE AS p WHERE p.PRT_ID_INSTN = i.INSTN_ID_INSTN AND p.PRT_QUALIF_PARTIE = 'F') THEN 1 ELSE 0 END AS Defendeur,
               CASE WHEN EXISTS (SELECT 1 FROM v2.DECISION_JUDICIAIRE AS dj WHERE dj.DCJDC_ID_INSTN = i.INSTN_ID_INSTN) THEN 1 ELSE 0 END AS Decision,
               COUNT(*) OVER () AS Total
        FROM v2.DEMANDE AS d
        OUTER APPLY (SELECT TOP (1) x.* FROM v2.INSTANCE AS x WHERE x.INSTN_ID_AFFRE = d.DMND_ID_AFFRE ORDER BY x.INSTN_ID_INSTN) AS i
        WHERE d.DMND_CODE_PROSS = 'CORDJRCS' AND d.DMND_ETAT_DEMANDE <> 'X'
        ORDER BY d.DMND_ID_DMND DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var etatV2 = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE");
        var faites = new List<int>();
        if (r["INSTN_ID_INSTN"] is not DBNull) faites.Add(0);
        if (Convert.ToInt32(r["Demandeur"], CultureInfo.InvariantCulture) == 1) faites.Add(1);
        if (Convert.ToInt32(r["Defendeur"], CultureInfo.InvariantCulture) == 1) faites.Add(2);
        if (Convert.ToInt32(r["Decision"], CultureInfo.InvariantCulture) == 1) faites.Add(3);
        var (etat, marches) = etatV2 == "V" || faites.Count == 4 ? ("Validee", new[] { 0, 1, 2, 3 })
            : faites.Count == 0 ? ("AFaire", Array.Empty<int>()) : ("EnCours", faites.ToArray());
        var role = FormeLigne.Texte(r, "INSTN_NUM_ROLE");
        var nom = FormeLigne.Texte(r, "INSTN_NOM_COURANT");
        var saisine = FormeLigne.DateDe(r, "INSTN_DATE_SAISINE_GREFFE");
        var precisions = FormeLigne.Joindre(
            role.Length > 0 ? $"Rôle {role}" : $"demande {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}",
            saisine is null ? null : $"saisine du {FormeLigne.Date(saisine)}",
            etatV2 == "I" ? "interrompue" : null);
        var groupe = etatV2 switch { "O" => Refus, "N" => Reclamation, _ => SurRequete };
        return new LigneLue(nom.Length > 0 ? nom : $"Demande {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}", precisions, groupe, etat, marches)
        { Id = FormeLigne.Texte(r, "DMND_ID_DMND") };
    }
}
