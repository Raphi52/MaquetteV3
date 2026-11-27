using System.Globalization;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Bordereaux LR » (PROC_BORDLR) : « Éditer le bordereau » met le bordereau de la periode dans la FILE DES EDITIONS
/// (v2.DEMANDE_EDITION), comme VALIDER de RIG v2. Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurBordereauxLr.cs (plan
/// « RIG v3 100 % fonctionnel », lot 11, 27/11/2026).
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_BORDLR (proc_bordlr.md, Detail OPE). Le geste vise les lignes « a editer »
/// de la periode (une par type, l'objet dans l'Id : AEditer, LecteurBordereauxLr.cs) ; les bordereaux deja edites ne s'editent pas
/// une seconde fois (la V2 n'a pas de liste). CONTROLES, avant toute ecriture, sur le nombre de plis RECOMPTE dans la transaction
/// (NbOccurence, recompte a chaque saisie, MetierProcBordlr.GetCountDemandeEdition) :
///   1. rien a editer -- aucune ligne a editer, ou aucun pli : « Validation inutile vous n'avez rien a éditer. Cliquez sur le bouton
///      Quitter. » (PROC_BORDLR.DoVerifier, :60-61), au mot pres ;
///   2. au moins 1 000 plis : « Impossible  de lancer l'édition nombre de recommandé supérieur à 1000. » (:64-65, double espace
///      compris) ;
///   3. le bordereau sans AR une fois la numerotation LRN demarree : l'etape est en lecture seule (MasqueSaisie,
///      ETP_BORDLR_SANS_AR.cs:28-33, « ne pas incrementer ») -- refus, la V2 n'ayant pas de message, le motif le dit ;
///   4. le reglage d'edition BORD_LRAR / BORD_LR absent : CreateDemandeEditionByParametreGeneration rend null
///      (DemandeEdition.cs:2580-2584) et la V2 n'ajoute rien (OPE_BORDLR_AVEC_AR.cs:69) -- refus, rien d'ecrit.
/// ECRIT (DoGenererLesDemandesEditions, OPE_BORDLR_AVEC_AR.cs:66-73 / OPE_BORDLR_SANS_AR.cs:61-68 ; MetierProcBordlr.GenererEdition,
/// :117-122) : UNE demande d'edition par type, au libelle « Bordereau LRAR-1 » / « Bordereau LR-1 » (« - » + NumOrdre, le rang de
/// l'operation dans son etape : une par etape), aux valeurs de creation de DemandeEdition.CreateDemandeEdition (DemandeEdition.cs:
/// 536-570 : papier NORMAL, media IMPR, emission N, phase C « creee », un exemplaire, impression, groupage automatique, l'agent comme
/// utilisateur et interlocuteur), au type de generation et a la maquette du reglage, et au XML de generation du reglage dont la V2
/// remplace le SQL par la requete du bordereau (SetParametreOuChamp("SQL", ...), MetierProcBordlr.cs:120 ; requete
/// _CreerSQLSelectGenererEdition + _CreeRequeteGenererEdition, :15-78) -- c'est elle qui dit quels plis le bordereau porte.
/// LA PARTIE EXTERNE (simulee, etiquetee) : l'impression du bordereau (etat Crystal BORDLRAR_LOT2) est faite par le service d'edition
/// de RIG v2, qui relit la file des editions ; RIG v3 ne l'appelle pas et n'imprime rien.
/// NON REPRIS (nomme) : la demande V2 du processus (BORDLR, creee et validee d'un coup, sans facture : une trace, que le journal des
/// gestes remplace -- decision du plan) ; DMNED_ID_DMND reste donc vide. La case « Mes recommandes » (LecteurBordereauxLr.cs).
/// </summary>
internal sealed class EcrivainBordereauxLr : IEcrivainEcran
{
    public string Procedure => "PROC_BORDLR";

    public string Ecran => "Bordereaux LR";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur, au mot pres.</summary>
    internal const string Editer = "Éditer le bordereau";

    /// <summary>Les messages de RIG v2 (PROC_BORDLR.DoVerifier, :61 et :65), au mot pres.</summary>
    internal const string RienAEditer = "Validation inutile vous n'avez rien a éditer. Cliquez sur le bouton Quitter.";
    internal const string TropDePlis = "Impossible  de lancer l'édition nombre de recommandé supérieur à 1000.";

    internal const string SansArEnConsultation =
        "Bordereau sans AR en consultation : la numérotation LRN a démarré, RIG v2 masque sa saisie (rien n'est édité).";

    private static string SansReglage(string code)
        => $"Le bordereau n'est pas édité : le réglage d'édition {code} n'existe pas dans ce greffe, et RIG v2 ne crée alors aucune édition.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Editer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        // Les lignes a editer ; une ligne d'un bordereau deja edite n'est pas un objet de ce geste (en tete).
        var objets = geste.Demande.Ids.Select(AEditer.DeId).OfType<AEditer>().DistinctBy(o => o.Id).ToList();
        if (objets.Count == 0) return IssueGeste.Refuser(RienAEditer);

        var lrn = await LecteurBordereauxLr.LrnDemarreeAsync(geste.Base, geste.Transaction, ct);
        var aEditer = new List<(AEditer Objet, int Plis)>();
        foreach (var o in objets)
        {
            if (o.Type == AEditer.Lr && lrn) return IssueGeste.Refuser(SansArEnConsultation);
            var plis = o.Inversee ? 0 : await LecteurBordereauxLr.CompterAsync(geste.Base, geste.Transaction, o, ct);
            if (plis >= 1000) return IssueGeste.Refuser(TropDePlis);
            if (plis > 0) aEditer.Add((o, plis));
        }
        // « aucune operation n'a NbOccurence > 0 » (PROC_BORDLR.cs:54-55, :60) : rien a editer.
        if (aEditer.Count == 0) return IssueGeste.Refuser(RienAEditer);

        var compte = geste.Agent.CompteWindows.Length > 32 ? geste.Agent.CompteWindows[..32] : geste.Agent.CompteWindows;
        var presentes = await EditionsV2.ColonnesAsync(geste, "v2.DEMANDE_EDITION", ct);
        var crees = new List<string>();
        var traces = new List<TraceGeste>();
        var libelles = new List<string>();
        foreach (var (o, plis) in aEditer)
        {
            var code = o.Type == AEditer.Lr ? "BORD_LR" : "BORD_LRAR";
            var reglage = await ReglageAsync(geste, code, ct);
            if (reglage is null) return IssueGeste.Refuser(SansReglage(code));
            var libelle = $"Bordereau {o.Type}-1";
            var valeurs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["DMNED_DATE_CREATION"] = geste.Maintenant, ["DMNED_TYPE_PAPIER"] = "NORMAL", ["DMNED_MEDIA"] = "IMPR",
                ["DMNED_ETAT_EMISSION"] = "N", ["DMNED_PHASE_TRAITEMENT"] = "C", ["DMNED_UTILISATEUR"] = compte, ["DMNED_INTERLOCUTEUR"] = compte,
                ["DMNED_NOMBRE_EXEMPLAIRE"] = (short)1, ["DMNED_IMPRESSION"] = true, ["DMNED_PAPIER_TRAME"] = false, ["DMNED_AGRAFAGE"] = false,
                ["DMNED_RECTO_VERSO"] = false, ["DMNED_AFF_AVANT_IMPRESSION"] = false, ["DMNED_LIBELLE_DOCUMENT"] = libelle,
                ["DMNED_GROUPAGE_AUTOMATIQUE"] = true, ["DMNED_RESUME"] = false, ["DMNED_READ_ONLY"] = false,
                ["DMNED_TYPE_GENERATION"] = reglage.TypeGeneration, ["DMNED_CODE_MAQUETTE"] = reglage.Maquette,
                ["DMNED_CODE_CFECP"] = reglage.Cfecp, ["DMNED_PARAMETRE_GENERATION"] = Generation(reglage.Liste, o),
                // NOT NULL dans la base V3 sans le defaut ((0)) de RIG_DEV : la V2 les laisse a ce defaut (EditionsV2.cs).
                ["DMNED_ENVOI_PAR_MAIL"] = false, ["DMNED_HAS_NUMLRAR"] = false, ["DMNED_IS_NON_SUPPRESSIBLE"] = false,
                ["DMNED_HAS_NUMLR"] = false, ["DMNED_MASQUER_COFFRE_FORT"] = false,
            };
            var id = await geste.InsererAsync("v2.DEMANDE_EDITION", "DMNED_ID_DMNED",
                valeurs.Where(v => presentes.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value), ct);
            var idTexte = id.ToString(CultureInfo.InvariantCulture);
            crees.Add(idTexte);
            var periode = $"du {FormeLigne.Date(o.Du)} au {FormeLigne.Date(o.Au)}";
            libelles.Add($"{libelle} ({plis} pli{(plis > 1 ? "s" : "")}, {periode})");
            traces.Add(new TraceGeste("demande", $"le bordereau {libelle}", idTexte, "a cree", null,
                new { Document = libelle, Plis = plis, Periode = periode, Utilisateur = o.Utilisateur ?? "tous", File = "file des éditions" }));
        }
        var message = $"{string.Join(", ", libelles)} : placé dans la file des éditions. Impression simulée : le service d'édition de "
                    + "RIG v2 imprime le bordereau, RIG v3 ne l'appelle pas.";
        return IssueGeste.Ecrit(message, [.. crees], [.. traces]);
    }

    /// <summary>Le reglage d'edition : son type de generation, sa maquette, son code CFE-CP et sa liste de parametres (XML).</summary>
    private sealed record Reglage(string TypeGeneration, object? Maquette, object? Cfecp, string Liste);

    private static async Task<Reglage?> ReglageAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        var parametre = await EditionsV2.ParametreAsync(geste, code, ct);
        if (parametre is null) return null;
        var colonnes = await EditionsV2.ColonnesAsync(geste, "v2.PARAMETRE_EDITION", ct);
        string Colonne(string nom) => colonnes.Contains(nom) ? nom : $"NULL AS {nom}";
        await using var r = await geste.LireAsync($"SELECT {Colonne("PRMED_CODE_MAQUETTE")}, {Colonne("PRMED_LISTE")} FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = @code",
                                                  ct, new SqlParameter("@code", code));
        await r.ReadAsync(ct);
        return new Reglage(parametre.TypeGeneration, r["PRMED_CODE_MAQUETTE"] is DBNull ? null : r["PRMED_CODE_MAQUETTE"], parametre.Cfecp,
                           FormeLigne.Texte(r, "PRMED_LISTE"));
    }

    /// <summary>
    /// Le XML de generation : la liste du reglage dont l'attribut SQL est la requete du bordereau (MetierProcBordlr.GenererEdition,
    /// :120) ; une liste illisible ou absente : « &lt;PARAMETRES SQL="..." /&gt; ».
    /// </summary>
    internal static string Generation(string liste, AEditer o)
    {
        XElement xml;
        try { xml = liste.Length > 0 ? XElement.Parse(liste) : new XElement("PARAMETRES"); }
        catch (System.Xml.XmlException) { xml = new XElement("PARAMETRES"); }
        xml.SetAttributeValue("SQL", RequeteEdition(o));
        return xml.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// La requete de l'etat du bordereau, ecrite comme la V2 (MetierProcBordlr._CreerSQLSelectGenererEdition, :15-43, et
    /// _CreeRequeteGenererEdition, :63-78) : le service d'edition de RIG v2 l'execute. Les bornes au format de DBDateTime.ToSQL
    /// (RigTools\SQL\DBObject.cs:539) ; l'utilisateur, une valeur lue, ses apostrophes doublees.
    /// </summary>
    internal static string RequeteEdition(AEditer o)
    {
        static string Date(DateTime d) => $"convert(DateTime2, '{d.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture)}', 121)";
        var utilisateur = o.Utilisateur is null ? " IS NOT NULL" : $"='{o.Utilisateur.Replace("'", "''", StringComparison.Ordinal)}'";
        // Les deux branches de la V2 visent la meme colonne de compte de suivi (:17-21, proc_bordlr.md « a trancher ») : reprise telle quelle.
        const string numCpteSuivi = "info_greffe.lrar_num_cpte_suivi";
        return "SELECT dmned_id_dmned, dmned_designation_destinataire, CONVERT(VARCHAR (5000), dmned_bloc_adresse_destinataire),"
             + "dmned_parametre_generation, lrari_num_lrar AS NUM_RECOMMANDE, info_greffe.ligne_adresse + ' '  + info_greffe.code_postal + ' ' + info_greffe.ville AS AdresseCompleteGreffe,"
             + "Replace(Upper(info_greffe.ville), 'CEDEX', ''),  '" + o.Type + "',  gpimp_nom_lot AS NomLot,"
             + "dbo.GetAdresseLigneGreffe('') AS ADRESSE_LIGNE_GREFFE,"
             + "dbo.GetCodePostalGreffe('') AS CODE_POSTAL_GREFFE, "
             + "dbo.GetLocalitePostaleGreffe('') AS LOCALITE_POSTALE_GREFFE, "
             + "info_greffe.lrar_raison_sociale AS RAISON_SOCIALE, "
             + "info_greffe.lrar_site_depot AS SITE_DEPOT, "
             + "info_greffe.lrar_num_coclico AS NUM_COCLICO, "
             + numCpteSuivi + " AS NUM_CPTE_SUIVI "
             + "FROM info_greffe "
             + "CROSS JOIN demande_edition "
             + "INNER JOIN code_greffe "
             + "ON r_codegreffe_valeur = info_greffe.code_greffe "
             + "LEFT OUTER JOIN groupe_impression "
             + "ON dmned_num_groupe = gpimp_num_groupe "
             + "INNER JOIN lrar_infos "
             + "ON dmned_num_lrar = lrari_num_lrar "
             + "Where LRARI_TYPE ='" + o.Type + "' "
             + " AND DMNED_DATE_PRE_EMISSION >=" + Date(o.Du)
             + " AND DMNED_DATE_PRE_EMISSION <= " + Date(o.Au)
             + " AND DMNED_UTILISATEUR" + utilisateur
             + " ORDER BY dmned_id_dmned ";
    }
}
