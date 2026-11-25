using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Bordereaux LR » (PROC_BORDLR) : les bordereaux de lettres recommandees du greffe, avec ou sans accuse de reception.
/// Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_BORDLR. L'ecran V2 n'a pas de liste : on saisit
/// une periode et un utilisateur, il compte les plis de la periode (OPE_BORDLR_AVEC_AR.cs:76-93,
/// Metier\MetierProcBordlr.cs:86-92), et la validation edite le bordereau. Chaque validation laisse deux traces : la
/// demande du processus, DEMANDE.DMND_CODE_PROSS = 'BORDLR' (27 dans RIG_DEV, toutes validees, de 2010 a 2019, lu le
/// 25/11/2026), et l'edition du bordereau, une DEMANDE_EDITION rattachee a cette demande (DMNED_ID_DMND : GenererEdition
/// recoit Processus.demande.DMND_ID_DMND_DEMANDE, OPE_BORDLR_AVEC_AR.cs:68, OPE_BORDLR_SANS_AR.cs:63), libellee
/// « Bordereau LRAR-n » ou « Bordereau LR-n ». Les demandes BORDLR sont donc le registre des bordereaux : la liste de cet
/// ecran -- comme les retours saisis pour LecteurRetoursLrar, sur le meme ecran reuni. Dans la base V3, DEMANDE et
/// DEMANDE_EDITION sont les tables V2 telles quelles (v2.*). La fiche du Brain (reference/proc/proc_bordlr.md) ne decrit
/// aucune liste.
///
/// FILTRES : DMND_CODE_PROSS = 'BORDLR' ; pas les demandes supprimees (etat X, Demande.eEtatDemande,
/// Outils\DLL\RigMetier\Demande\Demande.cs:21-42). Une ligne par edition de la demande (une validation peut editer les
/// deux bordereaux, avec et sans AR : DoVerifier, PROC_BORDLR.cs:52-67, n'exige qu'un des deux) ; une demande sans
/// edition fait une ligne. Ordre : la validation (sinon la creation) la plus recente en tete, puis la demande, puis
/// l'edition.
///
/// GROUPES (le libelle de l'edition, OPE_BORDLR_AVEC_AR.cs:68 et OPE_BORDLR_SANS_AR.cs:63 ; RIG_DEV l'ecrit en minuscule,
/// « bordereau LRAR-1 ») : « Bordereau LR-n » -> « Sans accuse de reception » ; tout autre -> « Avec accuse de reception ».
/// Une demande sans edition ne dit pas son type : elle va dans « Avec accuse de reception », le bordereau que la V2 propose
/// toujours (l'etape sans AR est masquee des que la numerotation LRN a demarre, PROC_BORDLR.cs:74-79 et
/// ETP_BORDLR_SANS_AR.cs:28-33) et le seul edite dans RIG_DEV (21 editions sur 21) ; ses precisions disent que le type
/// est inconnu.
///
/// REGLE D'ETAT (marches du descripteur : 0 Periode, 1 Utilisateur, 2 Bordereau avec AR, 3 Bordereau sans AR, 4 Edition) :
///   demande non validee (Q, I, W...)            -> AFaire « A preparer » : aucune marche -- la V2 n'ecrit la periode que
///                                                  dans l'edition, generee a la validation (OPE_BORDLR_AVEC_AR.cs:67) ;
///   validee, edition emise (phase E)            -> Validee « Edite » : periode, bordereau, edition (0, 2 ou 3, 4) ;
///   validee, edition pas encore emise           -> EnCours « En cours » : periode et bordereau (0, 2 ou 3), et la phase de
///                                                  l'edition (phases de LecteurFileEditions) dans les precisions ;
///   validee, sans edition                       -> EnCours : la periode (0), la validation l'exige (PROC_BORDLR.cs:60-62).
/// La marche « Utilisateur » (1) n'est jamais cochee : le choix n'est ecrit que dans la requete de l'edition
/// (DMNED_UTILISATEUR, MetierProcBordlr.cs:59), pas dans une colonne.
/// </summary>
internal sealed class LecteurBordereauxLr : ILecteurEcran
{
    public string Procedure => "PROC_BORDLR";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string AvecAr = "Avec accusé de réception", SansAr = "Sans accusé de réception";

    private const string Requete = """
        SELECT TOP (@plafond) d.DMND_ID_DMND, d.DMND_NUM_DEMANDE, d.DMND_ETAT_DEMANDE, d.DMND_DATE_CREATION, d.DMND_DATE_VALIDATION,
               d.DMND_NOM_UTILISATEUR, de.DMNED_ID_DMNED, de.DMNED_LIBELLE_DOCUMENT, de.DMNED_PHASE_TRAITEMENT,
               COUNT(*) OVER () AS Total
        FROM v2.DEMANDE AS d
        LEFT JOIN v2.DEMANDE_EDITION AS de ON de.DMNED_ID_DMND = d.DMND_ID_DMND
        WHERE d.DMND_CODE_PROSS = 'BORDLR' AND d.DMND_ETAT_DEMANDE <> 'X'
        ORDER BY COALESCE(d.DMND_DATE_VALIDATION, d.DMND_DATE_CREATION) DESC, d.DMND_ID_DMND DESC, de.DMNED_ID_DMNED
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var validee = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE") == "V";
        var edition = r["DMNED_ID_DMNED"] is not DBNull;
        var libelle = FormeLigne.Texte(r, "DMNED_LIBELLE_DOCUMENT");
        var phase = FormeLigne.Texte(r, "DMNED_PHASE_TRAITEMENT");
        var sansAr = SansAccuse(libelle);
        var (etat, faites) = Etat(validee, edition, sansAr, phase);
        var quand = validee
            ? FormeLigne.DateDe(r, "DMND_DATE_VALIDATION") ?? FormeLigne.DateDe(r, "DMND_DATE_CREATION")
            : FormeLigne.DateDe(r, "DMND_DATE_CREATION");
        var utilisateur = Utilisateur(FormeLigne.Texte(r, "DMND_NOM_UTILISATEUR"));
        var precisions = FormeLigne.Joindre(
            quand is null ? null : $"{(validee ? "validé" : "commencé")} le {FormeLigne.Date(quand)}",
            utilisateur.Length > 0 ? $"par {utilisateur}" : null,
            !edition ? (validee ? "aucune édition retrouvée, avec ou sans AR inconnu" : "à valider")
                     : FormeLigne.Joindre(libelle, phase == "E" ? "édité" : $"édition en file (phase « {phase} » de l'ancien RIG)"));
        return new LigneLue($"Bordereau {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}", precisions, sansAr ? SansAr : AvecAr, etat, faites);
    }

    /// <summary>Un bordereau sans AR : libelle « Bordereau LR-n » (OPE_BORDLR_SANS_AR.cs:63), en toute casse.</summary>
    internal static bool SansAccuse(string libelle)
        => libelle.StartsWith("bordereau LR-", StringComparison.OrdinalIgnoreCase);

    /// <summary>L'utilisateur de DMND_NOM_UTILISATEUR, ecrit « poste/utilisateur[/MOTEUR] » par la V2 (27 demandes BORDLR
    /// sur 27 dans RIG_DEV) : le second morceau ; une autre forme, telle quelle.</summary>
    internal static string Utilisateur(string nom)
    {
        var morceaux = nom.Split('/');
        return morceaux.Length >= 2 && morceaux[1].Trim().Length > 0 ? morceaux[1].Trim() : nom;
    }

    /// <summary>La regle d'etat (en tete du fichier) : etat du descripteur et marches franchies.</summary>
    internal static (string Etat, int[] Faites) Etat(bool validee, bool edition, bool sansAr, string phase)
    {
        if (!validee) return ("AFaire", []);
        if (!edition) return ("EnCours", [0]);
        var bordereau = sansAr ? 3 : 2;
        return phase == "E" ? ("Validee", [0, bordereau, 4]) : ("EnCours", [0, bordereau]);
    }
}
