using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// Ce que Instance.Sauve de RIG v2 ecrit EN PLUS des champs changes, quand un geste ne change que des colonnes propres a
/// l'instance (un lien vers une PC ou une prevention, une jonction) : les ecrivains de « Liens entre PC » et « Jonction
/// d'instances » (plan « RIG v3 100 % fonctionnel », lot 4, 26/11/2026). SOURCE : Outils\DLL\RigMetier\JUDICIAIRE\
/// Instance.cs:3895-3975 :
///   - INSTN_DATE_MAJ_INFOG = maintenant si INSTN_TOP_EXPERIAN, ou si la derniere mise a jour date d'un jour ou plus
///     (:3903-3905 ; une date vide vaut DateTime.MinValue, donc posee) ;
///   - INSTN_NUMERO_SOURCE = un GUID en majuscules s'il est vide (:3907, AmiSystem.GetGuid) ;
///   - sans code calendrier, ni date de debut ni etape de calendrier (:3967-3974).
/// Non repris : MAJNomInstance (le nom courant se recalcule des parties, que ces gestes ne changent pas) et ce que Sauve
/// enregistre des sous-objets charges (parties, decisions, appels : non charges par ces gestes, RIG v2 ne les reecrit pas).
/// L'ecrivain de « Instance judiciaire » (EcrivainInstanceJudiciaire.cs, lot 2) ecrit les deux premiers effets a sa facon.
/// </summary>
internal static class SauveInstanceV2
{
    /// <summary>Les affectations a ajouter a l'UPDATE v2.INSTANCE du geste ; parametres : <see cref="Parametres"/>.</summary>
    internal const string Effets = """
        INSTN_DATE_MAJ_INFOG = CASE WHEN INSTN_TOP_EXPERIAN = 1 OR INSTN_DATE_MAJ_INFOG IS NULL
                                      OR ABS(DATEDIFF_BIG(second, INSTN_DATE_MAJ_INFOG, @maintenant)) >= 86400
                                    THEN @maintenant ELSE INSTN_DATE_MAJ_INFOG END,
        INSTN_NUMERO_SOURCE = ISNULL(INSTN_NUMERO_SOURCE, @guid),
        INSTN_DATE_DEBUT_CAL = CASE WHEN INSTN_CALENDRIER IS NULL THEN NULL ELSE INSTN_DATE_DEBUT_CAL END,
        INSTN_NUM_ETAPE_CAL = CASE WHEN INSTN_CALENDRIER IS NULL THEN NULL ELSE INSTN_NUM_ETAPE_CAL END
        """;

    /// <summary>Les parametres de <see cref="Effets"/> : l'instant du geste et un GUID neuf.</summary>
    internal static SqlParameter[] Parametres(DateTime maintenant)
        => [new("@maintenant", maintenant), new("@guid", Guid.NewGuid().ToString().ToUpperInvariant())];
}
