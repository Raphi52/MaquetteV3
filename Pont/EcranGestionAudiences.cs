namespace RigV3Desktop.Pont;

/// <summary>Une ligne de l'ecran (les champs de « Audience » dans web/src/ecrans/EcranGestionAudiences.tsx) ; date = jj/mm/aaaa.
/// <see cref="Cabinet"/> : null dans la demonstration (elle ne distingue pas les modes), sinon le mode de RIG v2. Les codes
/// (<see cref="CodeChambre"/>, <see cref="CodeCab"/>, <see cref="IdJuge"/>) : ceux que la recherche du serveur recoit (vides dans la
/// demonstration).</summary>
public sealed record LigneGestionAudience(string Jour, string Date, string Heure, string Type, string CodeCabinet, string TypeCabinet,
                                          string Juge, string Section, string Chambre, string Greffier, string TypeClassement, bool Public,
                                          string AuteurSuppression, string DateSuppression, string EnvoiSuppression, bool? Cabinet = null,
                                          string CodeChambre = "", string CodeCab = "", int? IdJuge = null);

/// <summary>Ce que l'ecran montre : ses lignes, d'ou elles viennent (Pont/LectureEcranMain.cs) et leur total.</summary>
public sealed record VueGestionAudiences(LigneGestionAudience[] Lignes, string Mode = "demo", string? Message = null, int Total = 0);

/// <summary>
/// Pont de l'ecran PROC_GESTAUDS (web/src/ecrans/EcranGestionAudiences.tsx). L'ecran declarait ses audiences dans le .tsx : elles
/// sont ici la DEMONSTRATION, rendue sans serveur et marquee « exemple » a l'ecran. Branche au serveur, les audiences et cabinets
/// supprimes de la base du greffe (EcranGestionAudiencesBase.cs). Plan « RIG v3 100 % fonctionnel », lot 7, 26/11/2026.
/// </summary>
public static partial class EcranGestionAudiences
{
    /// <summary>Les cinq audiences de demonstration (celles que l'ecran declarait), sans serveur.</summary>
    public static VueGestionAudiences Exemple() => new(
    [
        new("Lundi", "14/09/2026", "09h00", "Audience de procedure", "CAB-01", "Cabinet du president", "Mme LEROY", "1re section", "1re chambre", "M. PETIT", "Renvoi", true, "", "", ""),
        new("Lundi", "14/09/2026", "10h30", "Audience publique", "CAB-01", "Cabinet du president", "Mme LEROY", "1re section", "1re chambre", "M. PETIT", "Mise en delibere", true, "", "", ""),
        new("Mardi", "15/09/2026", "14h00", "Cabinet du juge", "CAB-03", "Cabinet du rapporteur", "M. DUVAL", "2e section", "2e chambre", "Mme FAURE", "Renvoi", false, "", "", ""),
        new("Jeudi", "17/09/2026", "09h30", "Audience publique", "", "", "M. DUVAL", "2e section", "2e chambre", "", "Radiation", true, "rvilain", "02/09/2026", "Envoye"),
        new("Vendredi", "18/09/2026", "11h00", "Audience de procedure", "CAB-02", "Cabinet du president", "Mme LEROY", "3e section", "1re chambre", "M. PETIT", "Clos", true, "", "", ""),
    ], LectureEcransMain.Demo, null, 5);
}
