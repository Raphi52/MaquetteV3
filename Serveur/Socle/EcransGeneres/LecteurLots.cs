using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Lots » (PROC_GESTLOT) : la table des LOTS d'impression du greffe (v2.LOT), a ajouter, modifier ou desactiver. Un
/// REFERENTIEL : une ligne par lot, ses cellules dans l'ordre des colonnes du descripteur. Mode d'emploi : LecteurEcran.cs ; les
/// gestes : EcrivainLots.cs. Plan « RIG v3 100 % fonctionnel », lot 9, 26/11/2026.
///
/// SOURCE (ancien RIG) : proc_gestlot.md (Detail OPE, qui fait foi) et Source\RIG\DLL\Processus\PROC_GESTLOT\ :
///   - tous les lots du greffe, sans filtre : OPE_RECHERCHE.DoChargerDonnees -> Lot.GetListeLot, « select * from lot order by
///     lot_nom » (Lot.cs:97-109) ; la recherche plein texte (nom + imprimante, OPE_RECHERCHE.cs:62-64) et les cases actif/inactif
///     filtrent EN MEMOIRE (:105-127) : ici, le filtre de l'ecran, sur les lignes lues ;
///   - les champs de la fiche du lot (OPE_AJOUT._link, :116-129) que le descripteur montre : nom, imprimante, impression
///     automatique et son heure (ToShortTimeString, :124-125), actif, direct.
/// GROUPE : « Lots actifs » (LOT_ACTIF = 1), « Lots inactifs » sinon (les deux cases de la recherche V2). ETAT : tout lot lu est
/// « Enregistré » (Validee) : il est dans la table. Id = LOT_NOM, la cle de la table (et des gestes).
/// CE QUE LA V3 NE MONTRE PAS (nomme) : le tri automatique, la visibilite dans les editions differees, la taille postscript
/// (reservee a Amitel), le parametre d'edition et l'impression supplementaire (lots « AMI* ») -- des champs de la fiche V2 que le
/// descripteur n'a pas.
/// </summary>
internal sealed class LecteurLots : ILecteurEcran
{
    public string Procedure => "PROC_GESTLOT";

    /// <summary>Les groupes du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string Actifs = "Lots actifs", Inactifs = "Lots inactifs";

    /// <summary>Les colonnes du descripteur, dans l'ordre (les cellules des lignes, les champs des gestes).</summary>
    internal const string ColNom = "Nom du lot", ColImprimante = "Imprimante", ColImpressionAuto = "Impression automatique",
                          ColHeure = "Heure d'impression", ColActif = "Lot actif", ColDirect = "Lot direct";

    private const string Requete = """
        SELECT TOP (@plafond) LOT_NOM, LOT_NOM_IMPRIMANTE, LOT_IMPRESSION_AUTO, LOT_HEURE_IMPRESSION_AUTO, LOT_ACTIF, LOT_DIRECT,
               COUNT(*) OVER () AS Total
        FROM v2.LOT
        ORDER BY LOT_NOM
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    /// <summary>Une case de la grille : « oui » ou « non » (l'ecran ecrit Oui / Non).</summary>
    internal static string Case(bool coche) => coche ? "oui" : "non";

    /// <summary>L'heure telle que RIG v2 la montre (ToShortTimeString, OPE_AJOUT.cs:124) ; vide sans heure.</summary>
    internal static string Heure(DateTime? d) => d is { } v ? v.ToString("HH:mm", CultureInfo.InvariantCulture) : "";

    private static bool Bit(SqlDataReader r, string colonne) => r[colonne] is bool b && b;

    private static LigneLue Ligne(SqlDataReader r)
    {
        var nom = FormeLigne.Texte(r, "LOT_NOM");
        var imprimante = FormeLigne.Texte(r, "LOT_NOM_IMPRIMANTE");
        var auto = Bit(r, "LOT_IMPRESSION_AUTO");
        var heure = Heure(FormeLigne.DateDe(r, "LOT_HEURE_IMPRESSION_AUTO"));
        var actif = Bit(r, "LOT_ACTIF");
        var direct = Bit(r, "LOT_DIRECT");
        var precisions = FormeLigne.Joindre(imprimante.Length > 0 ? $"imprimante {imprimante}" : "sans imprimante",
                                            auto ? $"impression automatique à {heure}" : null, actif ? "actif" : "inactif",
                                            direct ? "lot direct" : null);
        return new LigneLue(nom, precisions, actif ? Actifs : Inactifs, "Validee", [])
        {
            Id = nom,
            Valeurs = [nom, imprimante, Case(auto), heure, Case(actif), Case(direct)],
        };
    }
}
