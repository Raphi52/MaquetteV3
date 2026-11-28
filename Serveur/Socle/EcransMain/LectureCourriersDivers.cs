using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransMain.LectureDemandes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Courriers divers » (web/src/ecrans/EcranCourriersDivers.tsx) : le CATALOGUE des modeles, lu dans la base V3 du greffe. LECTURE
/// SEULE (LectureV2). Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 8, 28/11/2026.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source) :
///   - la liste : tous les modeles de COURRIER_JUDICIAIRE, actifs ou non (CourrierJudiciaire.GetAllCourrierJudiciaire,
///     Outils\DLL\RigMetier\JUDICIAIRE\CourrierJudiciaire.cs:39-41 ; OPE_RECHERCHE_COURRIER_DIVERS.LoadLstCourrierJudiciaire,
///     RIG\DLL\Processus\PROC_COURRIER_DIVERS\Recherche\OPE_RECHERCHE_COURRIER_DIVERS.cs:181-186), et ses colonnes (:171-179) ;
///   - les parametres d'edition de la fiche, par type d'entite (ULT_COMBO_PARAMETRE_EDITION.ReloadByType, RIG\DLL\Rig_Ult_Metier\COMBO\
///     EDITION\ULT_COMBO_PARAMETRE_EDITION.cs:24-36 : PRMED_CODE_PROCESSUS = EDIT_INS, EDIT_MESUR, EDIT_PC ou EDIT_PRV ; une instance de
///     prevention n'en a aucun, « EDIT_ »).
/// ORDRE : par code (RIG v2 n'en donne pas : la liste suit sa recherche plein texte).
/// </summary>
public static class LectureCourriersDivers
{
    /// <summary>Le processus de parametre d'edition de chaque type d'entite (ReloadByType, :26-31) ; INSTANCE_PREVENTION : aucun.</summary>
    public static readonly IReadOnlyDictionary<string, string> ProcessusParType = new Dictionary<string, string>
    {
        ["INSTANCE"] = "EDIT_INS", ["MESURE"] = "EDIT_MESUR", ["PROCEDURE_COLLECTIVE"] = "EDIT_PC", ["PREVENTION"] = "EDIT_PRV",
    };

    /// <summary>Les types d'entite de la liste de RIG v2 (ULT_COMBO_TYPE_ENTITE_JUDICIAIRE.cs:13-18, rangee par valeur).</summary>
    public static readonly string[] TypesEntite = ["INSTANCE", "INSTANCE_PREVENTION", "MESURE", "PREVENTION", "PROCEDURE_COLLECTIVE"];

    private const string Modeles = """
        SELECT COUJU_ID_COUJU, COUJU_CODE, COUJU_LIBELLE, COUJU_TYPE_ENTITE, COUJU_CODE_PRMED,
               CONVERT(bit, ISNULL(COUJU_WITH_DEST, 1)) AS AvecDestinataire, CONVERT(bit, ISNULL(COUJU_ACTIF, 1)) AS Actif
        FROM v2.COURRIER_JUDICIAIRE ORDER BY COUJU_CODE, COUJU_ID_COUJU
        """;

    private const string Parametres = """
        SELECT PRMED_CODE, ISNULL(PRMED_LIBELLE, '') AS PRMED_LIBELLE, PRMED_CODE_PROCESSUS FROM v2.PARAMETRE_EDITION
        WHERE PRMED_CODE_PROCESSUS IN ('EDIT_INS', 'EDIT_MESUR', 'EDIT_PC', 'EDIT_PRV') ORDER BY PRMED_CODE
        """;

    public static async Task<CourriersDiversLus> LireAsync(SqlConnection baseGreffe, CancellationToken ct)
    {
        var courriers = new List<CourrierDiversLu>();
        await using (var lu = await LectureV2.LireAsync(baseGreffe, Modeles, ct))
            while (await lu.ReadAsync(ct))
                courriers.Add(new CourrierDiversLu(Convert.ToInt32(lu["COUJU_ID_COUJU"], CultureInfo.InvariantCulture), Chaine(lu, "COUJU_CODE"),
                    Chaine(lu, "COUJU_LIBELLE"), Chaine(lu, "COUJU_TYPE_ENTITE"), Chaine(lu, "COUJU_CODE_PRMED"), lu["AvecDestinataire"] is true,
                    lu["Actif"] is true));
        var parametres = new List<ParamEditionLu>();
        await using (var lu = await LectureV2.LireAsync(baseGreffe, Parametres, ct))
            while (await lu.ReadAsync(ct))
            {
                var processus = Chaine(lu, "PRMED_CODE_PROCESSUS");
                parametres.Add(new ParamEditionLu(Chaine(lu, "PRMED_CODE"), Chaine(lu, "PRMED_LIBELLE"),
                    ProcessusParType.FirstOrDefault(p => p.Value == processus).Key ?? ""));
            }
        return new CourriersDiversLus([.. courriers], [.. parametres]);
    }
}
