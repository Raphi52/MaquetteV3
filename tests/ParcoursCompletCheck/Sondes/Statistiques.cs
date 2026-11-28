using RigV3.Contrats.EcransMain;
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Statistiques » (PROC_STAT, Pont/EcranStatistiques.cs) : le bouton « Excel » de RIG v2 sur le volet « Statistiques internes » de l'année
/// (OPE_STATISTIQUES_INTERNES.cs:25-48 ; écriture : Serveur/Socle/EcransMain/EcritureStatistiques.cs), joué par le pont après le calcul du
/// volet, puis relu par le pont de la « File des éditions » (PROC_POOLEDITION, Pont/EcranGenere.cs) : la demande d'édition du classeur
/// « Statistiques internes du 01-01-aaaa au jj-mm-aaaa » y est, créée. Le jumeau refusé : la période inversée, au message de RIG v2
/// (OPE_DATES.cs:44), rien d'écrit. Lot 11 du plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU : le paramètre d'édition STAT_INTERNES de RIG_DEV (FUSION_RIG, maquette LOT2_STAT_INTERNES, lu le 28/11/2026) s'il manque, et les
/// colonnes de v2.DEMANDE_EDITION que l'édition écrit, aux types de RIG_DEV.
/// </summary>
public sealed class SondeStatistiques : ISondeEcran
{
    public string Code => "PROC_STAT";

    public string? Jeu => JeuSonde.EnDeuxTemps("""
        IF OBJECT_ID(N'v2.PARAMETRE_EDITION') IS NULL
            CREATE TABLE v2.PARAMETRE_EDITION (PRMED_CODE varchar(40) NOT NULL PRIMARY KEY, PRMED_LIBELLE varchar(100) NOT NULL,
                PRMED_TYPE_GENERATION varchar(15) NULL, PRMED_LISTE varchar(4000) NOT NULL);
        IF COL_LENGTH('v2.PARAMETRE_EDITION', 'PRMED_CODE_MAQUETTE') IS NULL ALTER TABLE v2.PARAMETRE_EDITION ADD PRMED_CODE_MAQUETTE varchar(40) NULL;
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_CODE_MAQUETTE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_CODE_MAQUETTE varchar(40) NULL;
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_CODE_PROCESSUS') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_CODE_PROCESSUS varchar(40) NULL;
        """, """
        IF NOT EXISTS (SELECT 1 FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = 'STAT_INTERNES')
            INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_TYPE_GENERATION, PRMED_LISTE, PRMED_CODE_MAQUETTE)
            VALUES ('STAT_INTERNES', 'Stats Internes', 'FUSION_RIG', '', 'LOT2_STAT_INTERNES');
        """);

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var debut = new DateTime(c.Jour.Year, 1, 1).ToString("yyyy-MM-dd");
        var fin = c.Jour.ToString("yyyy-MM-dd");
        var calcul = await EcranStatistiques.CalculerAsync(c.Client, c.Greffe, VoletsStatistiques.Internes, debut, fin, null);
        if (calcul.Mode != "serveur" || calcul.Tableaux.Length == 0)
            return new(false, false, $"le volet « Statistiques internes » ne se calcule pas ({calcul.Mode}{(calcul.Message is null ? "" : " « " + calcul.Message + " »")})");

        var journal = await c.Journal();
        var refus = await EcranStatistiques.CreerClasseurAsync(c.Client, c.Greffe, VoletsStatistiques.Internes, fin, debut);
        if (refus.Fait || refus.Message != "La date de début ne peut pas être supérieur à la date de fin" || await c.Journal() != journal)
            return new(false, false, $"jumeau refusé attendu (période inversée), rendu : {(refus.Fait ? "fait" : "refusé")} « {refus.Message} »");

        var r = await EcranStatistiques.CreerClasseurAsync(c.Client, c.Greffe, VoletsStatistiques.Internes, debut, fin);
        if (!r.Fait || r.Ids is not [var id]) return new(false, false, $"Excel : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");
        if (await c.Journal() == journal) return new(true, false, "Excel rendu fait sans trace au journal des gestes");

        var libelle = $"Statistiques internes du 01-01-{c.Jour.Year} au {c.Jour:dd-MM-yyyy}";
        var file = await EcranGenere.LignesAsync(c.Client, c.Greffe, "PROC_POOLEDITION");
        var ligne = file.Lignes.FirstOrDefault(l => l.Id == id);
        return file.Mode == "base" && ligne is not null && ligne.Precisions.Contains(libelle, StringComparison.Ordinal)
            ? new(true, true, $"Excel fait (« {r.Message} »), relu par le pont de la File des éditions : « {ligne.Precisions} » ; jumeau refusé : " +
                              "la période inversée, au message de RIG v2")
            : new(true, false, $"Excel rendu fait (demande d'édition {id}), mais la File des éditions relue ({file.Mode}, {file.Lignes.Length} ligne(s)) " +
                               $"ne la montre pas avec « {libelle} »{(ligne is null ? "" : $" (lue : « {ligne.Precisions} »)")}");
    }
}
