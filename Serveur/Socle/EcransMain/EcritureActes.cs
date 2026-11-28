using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Actes » (PROC_ACTES_NUM, écran fait main web/src/ecrans/EcranActes.tsx) : le geste « Définir le document comme non numérisable » (ou
/// « numérisable ») d'un acte, le menu de la grille de RIG v2. Joué par la route POST RoutesActes.SansDocument (ModuleActes.cs) dans la
/// transaction et le journal des gestes du socle (EcrivainsEcrans.JouerAsync). Plan « RIG v3 complet, mesuré par le banc du parcours
/// complet », lot 10, 28/11/2026 ; la lecture : LectureActes.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RIG\DLL\Processus\PROC_ACTES_NUM\PROC_ACTES_NUM\RESULTAT\OPE_RESULTAT.cs) :
///   - le DROIT (FDDA 16961, _OpenContextMenu :176-186) : les deux menus ne sont offerts qu'avec la fonction ACTENUM_NUMERISABLE
///     (Utilisateur.ALaFonctionnalite ; la V3 : soc.Droit, EditionsEtatsV2.ProcessusAutoriseAsync) ;
///   - le MENU offert (:183-184) : « non numérisable » quand la grille liste les actes numérisables (ACTDP_SANS_DOCUMENT = 0), « numérisable »
///     quand elle liste les non numérisables (case « Lister actes non numérisables », = 1) : chaque geste n'existe que dans l'état contraire ;
///   - UNE ligne choisie (tsmiAvecDocument_Click / tsmiSansDocument_Click :253-262 : SelectedRows.Count != 1, rien) ;
///   - l'ÉCRITURE en mode Acte (_ModifierSansDocument :276-279) : l'acte (ActeDepose.GetActeDepose) reçoit ACTDP_SANS_DOCUMENT et est sauvé.
/// NON REPRIS (nommés) : le mode Bilan (:264-274, tous les actes du dépôt) -- l'écran V3 ne lit pas ce mode (vue ViewDcaEnAnomalie absente
/// de la base V3, LectureActes.cs) ; la régénération du Kbis XML du dossier (DossierRCS.MiseAjourKBisXml, :273, :281) -- la V3 ne stocke pas
/// le Kbis XML de RIG v2, elle compose ses extraits à la demande.
/// TRACE : famille « dossier », « a modifie ».
/// </summary>
public static class EcritureActes
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Actes";

    /// <summary>La fonction de RIG v2 qui ouvre les deux menus (OPE_RESULTAT.cs:180).</summary>
    public const string Fonction = "ACTENUM_NUMERISABLE";

    /// <summary>Les gestes écrits : Agir (le Nom dit lequel) ; tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Agir];

    /// <summary>Pose ACTDP_SANS_DOCUMENT de l'acte Ids[0] selon le Nom du geste.</summary>
    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        bool sansDocument;
        if (geste.Demande.Nom == RoutesActes.NonNumerisable) sansDocument = true;
        else if (geste.Demande.Nom == RoutesActes.Numerisable) sansDocument = false;
        else return IssueGeste.Refuser($"« {geste.Demande.Nom} » n'est pas un geste de cet écran : seuls « {RoutesActes.NonNumerisable} » et « {RoutesActes.Numerisable} » s'enregistrent.");
        if (geste.Demande.Ids is not [var texte] || !int.TryParse(texte?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser("Choisissez un acte, un seul : rien n'est modifié.");
        if (!await EditionsEtatsV2.ProcessusAutoriseAsync(geste, Fonction, ct))
            return IssueGeste.Refuser($"Définir un document comme numérisable ou non demande la fonction {Fonction} (RIG v2 n'offre ces menus qu'avec elle) : rien n'est modifié.");

        bool avant;
        string abrege, depot;
        await using (var r = await geste.LireAsync("""
            SELECT ad.ACTDP_SANS_DOCUMENT, ad.ACTDP_ABREGE_PIECE, p.DPTRC_NUM_DEPOT
            FROM v2.ACTE_DEPOSE AS ad WITH (UPDLOCK)
            LEFT JOIN v2.DEPOT_RCS AS p ON p.DPTRC_ID_DPTRC = ad.ACTDP_ID_DPTRC
            WHERE ad.ACTDP_ID_ACDPS = @id
            """, ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Acte introuvable dans la base du greffe (n° {id}) : rien n'est modifié.");
            avant = r["ACTDP_SANS_DOCUMENT"] is true;
            abrege = FormeLigne.Texte(r, "ACTDP_ABREGE_PIECE").Trim();
            depot = FormeLigne.Texte(r, "DPTRC_NUM_DEPOT").Trim();
        }
        var quoi = $"l'acte {abrege}{(depot.Length > 0 ? " du dépôt " + depot : "")}";
        // _OpenContextMenu :183-184 : « non numérisable » n'est offert que sur un acte numérisable, et l'inverse.
        if (avant == sansDocument)
            return IssueGeste.Refuser(sansDocument
                ? $"{Majuscule(quoi)} est déjà non numérisable : RIG v2 n'offre que « {RoutesActes.Numerisable} »."
                : $"{Majuscule(quoi)} est déjà numérisable : RIG v2 n'offre que « {RoutesActes.NonNumerisable} ».");

        await geste.EcrireAsync("UPDATE v2.ACTE_DEPOSE SET ACTDP_SANS_DOCUMENT = @sans WHERE ACTDP_ID_ACDPS = @id", ct,
            new SqlParameter("@sans", sansDocument), new SqlParameter("@id", id));

        var idTexte = id.ToString(CultureInfo.InvariantCulture);
        return IssueGeste.Ecrit(
            sansDocument ? $"{Majuscule(quoi)} est défini comme non numérisable." : $"{Majuscule(quoi)} est défini comme numérisable.",
            [idTexte],
            new TraceGeste("dossier", quoi, idTexte, "a modifie", new { SansDocument = avant }, new { SansDocument = sansDocument }));
    }

    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
