using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Mon accueil » (PROC_ACCUEIL_PARAM, web/src/ecrans/EcranParametresAccueil.tsx) : les réglages d'accueil RIG v2 de L'AGENT
/// appelant, lus dans la base V3 du greffe (v2.PARAMETRE_UTILISATEUR recopiée, l'agent par soc.Compte.IdV2). LECTURE SEULE
/// (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 20, 27/11/2026, marche du lot 17.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_ACCUEIL_PARAM\FORM_SKIN_ACCUEIL_PARAM.cs -- chaque
/// réglage est une clé AmiConstantes.Utilisateur.ParametreConsoleAccueil (Outils\AmiDLL\AmiConstantes\Utilisateur.cs:11-54) lue
/// par User.GetParametre (_LoadParamGeneral, :531-583) et écrite par User.SetParametre à la FERMETURE de l'onglet
/// (_SaveParametrage, :620-648, appelé par OnFormClosed :97-103) dans PARAMETRE_UTILISATEUR (PRMTR_ID_UTLST,
/// PRMTR_NOM_PARAMETRE, PRMTR_VALEUR_PARAMETRE). Profils : les animations ne se lisent et ne s'écrivent que pour un
/// administrateur (:552-561, :640) ; la molette et la date libre ne se lisent que pour Amitel (:565-575).
/// Seules les clés que l'écran V3 montre sont lues (<see cref="Cles"/>). La V3 ne les APPLIQUE pas : ce sont des réglages du poste
/// WinForms de RIG v2 (animations, plein écran, molette des listes…) ; la reprise des préférences n'en garde que le thème, les
/// favoris, l'écran de démarrage et l'imprimante (Serveur/Socle/Preferences/ReprisePreferences.cs:24-28).
/// </summary>
public static class LectureParametresAccueil
{
    /// <summary>Les clés lues : celles des réglages que l'écran V3 montre.</summary>
    public static readonly IReadOnlyList<string> Cles =
    [
        "ConsoleAccueil_AllowAnimations", "ConsoleAccueil_AutofocusOnClickIconEtpOpe", "ConsoleAccueil_Colorier1LigneSur2",
        "ConsoleAccueil_DateSaisieLibreCpp", "ConsoleAccueil_InterdireMoletteComboCpp", "ConsoleAccueil_IntervalSlideshowAlertes",
        "ConsoleAccueil_Form_MaxWidth", "ConsoleAccueil_Form_MaxHeight", "ConsoleAccueil_IndexColTriAlertes",
        "ConsoleAccueil_OrdreTriColAlertes",
    ];

    private static readonly string Requete = $"""
        SELECT p.PRMTR_NOM_PARAMETRE, p.PRMTR_VALEUR_PARAMETRE
        FROM v2.PARAMETRE_UTILISATEUR AS p
        WHERE p.PRMTR_ID_UTLST = @idV2 AND p.PRMTR_NOM_PARAMETRE IN ({string.Join(", ", Cles.Select(c => $"'{c}'"))})
        ORDER BY p.PRMTR_ID_PRMTR
        """;

    /// <summary>Les réglages de l'agent <paramref name="idCompte"/> (soc.Compte.Id), avec ses profils RIG v2.</summary>
    public static async Task<ParametresAccueilLus> LireAsync(SqlConnection baseGreffe, int idCompte, bool estAdministrateur, bool estAmitel,
                                                            CancellationToken ct)
    {
        int? idV2;
        await using (var cmd = new SqlCommand("SELECT IdV2 FROM soc.Compte WHERE Id = @id;", baseGreffe))
        {
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = idCompte });
            idV2 = await cmd.ExecuteScalarAsync(ct) is int v ? v : null;
        }
        var valeurs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (idV2 is null) return new ParametresAccueilLus(valeurs, estAdministrateur, estAmitel, AgentV2: false);
        await using var r = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@idV2", SqlDbType.Int) { Value = idV2.Value });
        // Une cle en double : la V3 garde la plus recente (identifiant PRMTR_ID_PRMTR le plus grand).
        while (await r.ReadAsync(ct))
            valeurs[r.GetString(0)] = r.IsDBNull(1) ? "" : r.GetString(1).Trim();
        return new ParametresAccueilLus(valeurs, estAdministrateur, estAmitel, AgentV2: true);
    }
}
