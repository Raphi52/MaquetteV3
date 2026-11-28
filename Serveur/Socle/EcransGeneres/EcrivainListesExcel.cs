using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Export Excel » (PROC_LISTEEXCEL, forme Etat) : « Lancer l'export Excel » met la liste comptee dans la FILE DES EDITIONS. Mode
/// d'emploi : EcrivainEcran.cs ; le tableau et son comptage : LecteurListesExcel.cs (Id d'une ligne comptee = son type, sa periode et
/// ses criteres, LecteurListesExcel.Comptage). Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 5, 28/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_LISTEEXCEL\UTIL\Operations\OPE_RECHERCHE.cs (proc_listeexcel.md) :
///   - le bouton « Excel » n'est actif qu'avec un compte de 1 a 65 536 lignes (TextNbOccurence_ChangementValeur, :465-499) : grise a 0
///     (:485), et au-dela de 65 536 RIG v2 le dit, au mot pres (:467-470, ses trois lignes jointes ici par une espace) ;
///   - l'ecran est ouvert sur UN type de liste (MetierParametres, le parametre du menu) : un clic exporte une liste ; la V3, qui compte
///     tous les types a la fois, refuse plusieurs types et demande d'en choisir un ;
///   - LanceEditionExcel (:440-463) lance EXECUTE_EXCEL avec la requete de la liste (PARAMETRAGE_LISTE ou la liste choisie), sa clause
///     WHERE et le titre du classeur : le libelle de la liste (PARAMETRAGE_LISTE.PRMLS_LIBELLE du type, GetParametrageListeXls, :224) suivi
///     de « du <debut> » et, si la fin est saisie, « au <fin> » (:246) ; « jusqu'au <debut> » pour AFF (:257). Le compte est celui du
///     dernier changement de critere (:55-101) : la V3 recompte, dans la transaction du geste, par la meme requete que l'ecran.
/// LA V3 : RIG v2 ouvre le classeur dans Excel sur le poste, sans rien ecrire en base ; une edition de RIG v3 est une demande dans la file
/// des editions (decisions des lots 1 et 3 du plan, EditionsV2.SansParametre) : une demande d'edition REPORT_SQL (une requete mise en
/// tableau, DemandeEdition.eTypeGeneration, Edition\DemandeEdition.cs:188-190) par liste, au titre de RIG v2, processus LISTEEXCEL, source
/// « Export Excel », sans demande V2 (VALIDER est cache, FORM_LISTEEXCEL.cs:20 : la demande du processus n'est jamais validee).
/// Le libelle de la liste : PRMLS_LIBELLE quand la base porte PARAMETRAGE_LISTE et la ligne du type (RIG_DEV : les sept) ; a defaut celui
/// du type que l'ecran montre (PARAMETRAGE_LISTE_XLS_TYPE_LISTE), comme le comptage, qui ne lit pas non plus PARAMETRAGE_LISTE.
/// NON REPRIS (nomme) : la requete du classeur (parametre SQL d'EXECUTE_EXCEL) -- ses colonnes sont des expressions SQL stockees dans
/// PARAMETRAGE_LISTE_XLS, que RIG v3 ne recopie ni n'execute ; le choix d'une liste parametree (ses colonnes) pour l'export.
/// Trace : famille « demande », l'edition creee, « a cree ».
/// </summary>
internal sealed class EcrivainListesExcel : IEcrivainEcran
{
    public string Procedure => "PROC_LISTEEXCEL";
    public string Ecran => "Export Excel";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur (ModifierEnvois.cs), au mot pres.</summary>
    internal const string Lancer = "Lancer l'export Excel";

    /// <summary>Le code du processus de RIG v2 et la source de la demande d'edition.</summary>
    internal const string Processus = "LISTEEXCEL", Source = "Export Excel";

    internal const string SansComptage =
        "« Lancer l'export Excel » n'est actif qu'après le comptage d'une période : RIG v2 le grise tant qu'aucune ligne n'est comptée.";
    internal const string UnType =
        "RIG v2 exporte une liste à la fois (son écran s'ouvre sur un type de liste) : choisissez le type de liste à exporter.";
    internal const string TropDeLignes =
        "Le nombre d'enregistrements dépasse 65536. L'affichage de ces enregistrements est impossible sous Excel. Veuillez changez vos critères de recherche.";

    internal static string AucuneLigne(string titre) => $"« {titre} » : aucune ligne à exporter, RIG v2 laisse « Excel » grisé.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Lancer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var comptages = geste.Demande.Ids.Select(LecteurListesExcel.Comptage.Lire).ToArray();
        if (comptages.Length == 0 || comptages.Any(c => c is null)) return IssueGeste.Refuser(SansComptage);
        if (comptages.Select(c => c!.Type).Distinct().Count() > 1) return IssueGeste.Refuser(UnType);
        var comptage = comptages[0]!;

        var titre = await TitreAsync(geste, comptage, ct);
        if (titre is null) return IssueGeste.Refuser(SansComptage);
        var (sql, parametres, _) = comptage.Requete();
        int nombre;
        await using (var r = await geste.LireAsync(sql, ct, parametres))
            nombre = await r.ReadAsync(ct) ? Convert.ToInt32(r["Nombre"], CultureInfo.InvariantCulture) : 0;
        if (nombre == 0) return IssueGeste.Refuser(AucuneLigne(titre));
        if (nombre > LecteurListesExcel.PlafondExcel) return IssueGeste.Refuser(TropDeLignes);

        var id = await EditionsV2.DemandeEditionAsync(geste, null, EditionsV2.SansParametre(titre, "REPORT_SQL"), titre, null, null, null, null, ct,
            new Dictionary<string, object?> { ["DMNED_CODE_PROCESSUS"] = Processus, ["DMNED_LIBELLE_SOURCE"] = Source });
        var lignes = nombre == 1 ? "1 ligne" : $"{nombre.ToString(CultureInfo.InvariantCulture)} lignes";
        return IssueGeste.Ecrit($"« {titre} » ({lignes}) : placé dans la file des éditions.", [EditionsV2.Texte(id)],
            new TraceGeste("demande", $"l'export Excel « {titre} »", EditionsV2.Texte(id), "a cree", null,
                new { Document = titre, Liste = comptage.Type, Lignes = nombre, Generation = "REPORT_SQL", File = "file des éditions" }));
    }

    /// <summary>Le titre du classeur de RIG v2 (en tete) ; null si le type n'est declare nulle part dans la base.</summary>
    private static async Task<string?> TitreAsync(GesteEnCours geste, LecteurListesExcel.Comptage comptage, CancellationToken ct)
    {
        string? libelle = null;
        if ((await EditionsV2.ColonnesAsync(geste, "v2.PARAMETRAGE_LISTE", ct)).Contains("PRMLS_LIBELLE"))
            await using (var r = await geste.LireAsync("SELECT TOP (1) PRMLS_LIBELLE FROM v2.PARAMETRAGE_LISTE WHERE PRMLS_VALEUR = @type", ct,
                             new SqlParameter("@type", comptage.Type)))
                if (await r.ReadAsync(ct) && r[0] is string lu && lu.Trim().Length > 0) libelle = lu.Trim();
        if (libelle is null)
            await using (var r = await geste.LireAsync("SELECT PRMLLXCMP_LIBELLE FROM v2.PARAMETRAGE_LISTE_XLS_TYPE_LISTE WHERE PRMLLXTYP_CODE = @type", ct,
                             new SqlParameter("@type", comptage.Type)))
                if (await r.ReadAsync(ct) && r[0] is string lu) libelle = lu.Trim();
        if (libelle is null) return null;
        var jour = (DateTime d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return comptage.Type == "AFF" ? $"{libelle} jusqu'au {jour(comptage.Du)}"
             : $"{libelle} du {jour(comptage.Du)}{(comptage.FinSaisie ? $" au {jour(comptage.Au)}" : "")}";
    }
}
