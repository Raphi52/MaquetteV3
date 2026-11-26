using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransMain.LectureDemandes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Ouvrir un dossier » (web/src/ecrans/EcranOuvertureDossier.tsx) : le tableau des dossiers deja ouverts, lu dans la base V3
/// du greffe. LECTURE SEULE (LectureV2).
///
/// SOURCE : cet ecran n'a PAS d'origine RIG v2 (Pont/EcranOuvertureDossier.cs) ; dans RIG v2 un dossier RCS nait d'une
/// formalite d'immatriculation. Le tableau montre donc les dossiers RCS du greffe tels que RIG v2 les a enregistres, les
/// derniers immatricules en tete : la fiche rec.DossierRcs, reprise de DOSSIER_RCS (numero de gestion DSSRC_NUM_GESTION,
/// SIREN DSSRC_SIREN, date d'immatriculation DSSRC_DATE_IMMAT ; Serveur/Recopie/FichesV3.cs), son entreprise rec.Entreprise
/// (denomination ENTRP_DESIGNATION, forme ENTRP_FORME_JURIDIQUE_UTI), la forme lue par son abrege dans FORME_JURIDIQUE_UTI
/// (R_FORMEJURID_ABREGE_FRANCAIS, a defaut son libelle).
/// NON REPRIS : le capital, la monnaie et la « porte d'entree » de l'ecran, qui ne sont pas des colonnes de ces fiches.
/// </summary>
public static class LectureDossiersOuverts
{
    /// <summary>Le nombre de dossiers montres : les derniers immatricules.</summary>
    public const int Plafond = 100;

    private const string Requete = """
        SELECT TOP (@plafond) s.NumeroGestion, s.Siren, e.Denomination, e.FormeJuridique, s.DateImmatriculation,
               COALESCE(fj.R_FORMEJURID_ABREGE_FRANCAIS, fj.R_FORMEJURID_LIBELLE_ECRAN, e.FormeJuridique) AS Forme,
               COUNT(*) OVER () AS Total
        FROM rec.DossierRcs AS s
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        LEFT JOIN v2.FORME_JURIDIQUE_UTI AS fj ON fj.R_FORMEJURID_VALEUR = e.FormeJuridique
        ORDER BY s.DateImmatriculation DESC, s.Id DESC
        """;

    /// <summary>Les derniers dossiers immatricules du greffe.</summary>
    public static async Task<DossiersOuvertsLus> LireAsync(SqlConnection baseGreffe, CancellationToken ct)
    {
        var lignes = new List<DossierOuvertLu>();
        var total = 0;
        await using var lu = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@plafond", SqlDbType.Int) { Value = Plafond });
        while (await lu.ReadAsync(ct))
        {
            total = Convert.ToInt32(lu["Total"], CultureInfo.InvariantCulture);
            lignes.Add(new DossierOuvertLu(Chaine(lu, "NumeroGestion"), Chaine(lu, "Siren"), Chaine(lu, "Denomination"), Chaine(lu, "Forme"),
                                           lu["DateImmatriculation"] as DateTime?));
        }
        return new DossiersOuvertsLus([.. lignes], total);
    }
}
