using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// Ce qui REFERENCE un mandataire dans la base du greffe : la liste de RIG v2, Mandataire.GetLiaisonEntite
/// (D:\RigApplication\Source\Outils\DLL\RigMetier\JUDICIAIRE\Mandataire.cs:477-490), table par table, avec la colonne que la
/// fusion de RIG v2 reaffecte (OPE_SELECTION_ACTEUR.Mandataires.cs:236-372, PROC_RAPPACTEUR). Plan « RIG v3 100 % fonctionnel »,
/// lot 14, 27/11/2026. Lu par la suppression d'un mandataire (EcrivainMandataires.cs : un mandataire reference ne se supprime
/// pas) et par « Rapprocher des acteurs » (LecteurRapprochementActeurs.cs : l'apercu ; EcrivainRapprochementActeurs.cs : la
/// reprise).
/// Les requetes de RIG v2 (citees par liaison) : CreancierPC.cs:201-202, Partie.cs:610-611, CreanceTransitoire.cs:745,
/// Repertorie.cs:91, EvenementProcedure.cs:1540-1543, VidigedIndex.cs:253-256, DecisionJudiciaire.cs:782-785,
/// DematCfecAjmjVidiged.cs:130-133 ; le type de deposant « MANDATAIRE » est celui que GetLiaisonEntite passe.
/// </summary>
internal static class LiaisonsMandataire
{
    /// <summary>Une liaison : son libelle, sa table, sa cle, la colonne qui porte le mandataire et la condition de type.</summary>
    internal sealed record Liaison(string Libelle, string Table, string Cle, string Colonne, string? Type)
    {
        /// <summary>La condition SQL sur le mandataire <c>@id</c> (et le type de deposant s'il y en a un).</summary>
        public string Condition => Type is null ? $"{Colonne} = @id" : $"{Type} = 'MANDATAIRE' AND {Colonne} = @id";
    }

    /// <summary>Les liaisons, dans l'ordre de la reprise de RIG v2 (OPE_SELECTION_ACTEUR.Mandataires.cs:236-372).</summary>
    internal static readonly Liaison[] Toutes =
    [
        new("Créanciers de procédure collective", "CREANCIER_PC", "CREPC_ID_CREPC", "CREPC_ID_MNDTR", null),
        new("Parties (avocat postulant)", "PARTIE", "PRT_ID_PRT", "PRT_ID_MNDTR_POS", null),
        new("Parties (avocat plaidant)", "PARTIE", "PRT_ID_PRT", "PRT_ID_MNDTR_PLAI", null),
        new("Créances transitoires", "CREANCE_TRANSITOIRE", "CRTRA_ID_CRTRA", "CRTRA_ID_MNDTR", null),
        new("Répertoriés", "REPERTORIE", "PRTRP_ID_PRTRP", "PRTRP_ID_MNDTR_POS", null),
        new("Dépôts dématérialisés (AJMJ)", "DEMAT_CFEC_AJMJ_VIDIGED", "CFECAJV_ID_CFECAJV", "CFECAJV_ID_DEPOSANT", "CFECAJV_TYPE_DEPOSANT"),
        new("Événements de procédure (déposant)", "EVENEMENT_PROCEDURE", "EVNPR_ID_EVNPR", "EVNPR_ID_DEPOSANT_GED", "EVNPR_TYPE_DEPOSANT_GED"),
        new("Documents GED (déposant)", "VIDIGED_INDEX", "VGIDX_ID_VGIDX", "VGIDX_ID_DEPOSANT", "VGIDX_TYPE_DEPOSANT"),
        new("Décisions (déposant GED)", "DECISION_JUDICIAIRE", "DCJDC_ID_DCJDC", "DCJDC_ID_DEPOSANT_GED", "DCJDC_TYPE_DEPOSANT_GED"),
    ];

    /// <summary>Le nombre de lignes de chaque liaison du mandataire <c>@id</c>, une ligne par liaison, dans l'ordre.</summary>
    internal static readonly string RequeteComptes = string.Join("\nUNION ALL ", Toutes.Select((l, i) =>
        $"SELECT {i} AS Rang, COUNT(*) AS Nombre FROM v2.{l.Table} WHERE {l.Condition}")) + "\nORDER BY Rang";

    /// <summary>Les affaires ou le mandataire est l'avocat d'une partie (le chemin de RIG v2 vers les destinataires d'edition,
    /// _RecupIdDestEditLot2Mandataire, OPE_SELECTION_ACTEUR.Mandataires.cs:417-430) : numero de role et nom.</summary>
    internal const string RequeteAffaires = """
        SELECT DISTINCT i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT
        FROM v2.PARTIE AS p JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = p.PRT_ID_INSTN
        WHERE p.PRT_ID_MNDTR_POS = @id OR p.PRT_ID_MNDTR_PLAI = @id
        ORDER BY i.INSTN_NUM_ROLE, i.INSTN_ID_INSTN
        """;

    /// <summary>Les comptes des liaisons, lus par <paramref name="lire"/> (la lecture d'un lecteur ou celle d'un geste).</summary>
    internal static async Task<(Liaison Liaison, int Nombre)[]> CompterAsync(Func<string, SqlParameter[], Task<SqlDataReader>> lire, int id,
                                                                             CancellationToken ct)
    {
        var comptes = new List<(Liaison, int)>();
        await using var r = await lire(RequeteComptes, [new SqlParameter("@id", id)]);
        while (await r.ReadAsync(ct)) comptes.Add((Toutes[r.GetInt32(0)], r.GetInt32(1)));
        return [.. comptes];
    }

    /// <summary>« 2 parties (avocat postulant), 1 décision (déposant GED) » : les liaisons non vides, en mots ; vide sans liaison.</summary>
    internal static string EnMots(IEnumerable<(Liaison Liaison, int Nombre)> comptes)
        => string.Join(", ", comptes.Where(c => c.Nombre > 0).Select(c => $"{c.Nombre} × {c.Liaison.Libelle.ToLowerInvariant()}"));
}
