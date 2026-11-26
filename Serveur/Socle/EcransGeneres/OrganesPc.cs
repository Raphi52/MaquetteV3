namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// Les organes d'une procedure collective comme RIG v2 les ecrit, pour « Changer le juge-commissaire » (LecteurChangementJuge) et
/// « Changer les organes » (LecteurOrganesPc). Plan « RIG v3 100 % fonctionnel », lot 4, 26/11/2026.
///
/// SOURCE : les fonctions SQL de la base V2, absentes de la base V3 (une recopie ne porte que des tables), refaites ici en
/// sous-requetes sur v2.ETRE_JUGE_COMM, v2.JUGE, v2.GERER_PC et v2.MANDATAIRE_PC (lues dans RIG_DEV le 26/11/2026) :
///   - dbo.ListeJuge(@ID, @Fonction, @FinMission, @AffJusteNom) : les juges de la PC dans la fonction, en mission, separes par
///     « / » ; le nom seul, ou dbo.GetCiviliteNomPrenom(civilite, nom, prenom) ;
///   - dbo.ListeMandataireJudiciaire(@ID, @FinMission, @AffJusteNom) : les mandataires MDJ, RDC, LQJ, LIQ de la PC ;
///   - dbo.GetCiviliteNomPrenom : la civilite (vide pour PM et ND) passee par dbo.FirstUpper (premiere lettre en majuscule, le
///     reste en minuscules ; la V3 ne refait pas le decoupage aux tirets, aucune civilite n'en a), puis nom et prenom.
/// La V2 concatene sans ordre ; la V3 suit l'identifiant de la ligne, pour un texte stable.
/// </summary>
internal static class OrganesPc
{
    /// <summary>dbo.GetCiviliteNomPrenom sur des colonnes civilite, nom, prenom.</summary>
    private static string CiviliteNomPrenom(string civilite, string nom, string prenom) => $"""
        LTRIM(RTRIM(CASE WHEN {civilite} IN ('PM', 'ND') THEN ''
                         ELSE UPPER(LEFT(LTRIM(RTRIM(ISNULL({civilite}, ''))), 1)) + LOWER(SUBSTRING(LTRIM(RTRIM(ISNULL({civilite}, ''))), 2, 255)) + ' ' END
                    + ISNULL({nom} + ' ', '') + ISNULL({prenom} + ' ', '')))
        """;

    /// <summary>dbo.ListeJuge(p.PRCLL_ID_PRCLL, fonction, 0, justeNom) : les juges EN MISSION de la PC d'alias <paramref name="pc"/>.</summary>
    public static string Juges(string pc, string fonction, bool justeNom) => $"""
        ISNULL(STUFF((SELECT '/' + {(justeNom ? "ISNULL(j.JUG_NOM, '')" : CiviliteNomPrenom("j.JUG_CIVILITE", "j.JUG_NOM", "j.JUG_PRENOM"))}
                      FROM v2.ETRE_JUGE_COMM AS ej JOIN v2.JUGE AS j ON j.JUG_ID_JUG = ej.ETJCM_ID_JUG
                      WHERE ej.ETJCM_ID_PRCLL = {pc}.PRCLL_ID_PRCLL AND ej.ETJCM_FONCTION_PC = '{fonction}' AND ej.ETJCM_FIN_MISSION = 0
                      ORDER BY ej.ETJCM_ID_ETJCM FOR XML PATH(''), TYPE).value('.', 'varchar(max)'), 1, 1, ''), '')
        """;

    /// <summary>dbo.ListeMandataireJudiciaire(p.PRCLL_ID_PRCLL, 0, 0) : les mandataires en mission de la PC d'alias <paramref name="pc"/>.</summary>
    public static string Mandataires(string pc) => $"""
        ISNULL(STUFF((SELECT '/' + {CiviliteNomPrenom("mp.MNDPC_CIVILITE", "mp.MNDPC_NOM", "mp.MNDPC_COMPLEMENT_NOM")}
                      FROM v2.GERER_PC AS g JOIN v2.MANDATAIRE_PC AS mp ON mp.MNDPC_ID_MNDPC = g.GRRPC_ID_MNDPC
                      WHERE g.GRRPC_ID_PRCLL = {pc}.PRCLL_ID_PRCLL AND g.GRRPC_FONCTION_PC IN ('MDJ', 'RDC', 'LQJ', 'LIQ') AND g.GRRPC_FIN_MISSION = 0
                      ORDER BY g.GRRPC_ID_GRRPC FOR XML PATH(''), TYPE).value('.', 'varchar(max)'), 1, 1, ''), '')
        """;
}
