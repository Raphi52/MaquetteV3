using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Courriers revenus NPAI » (PROC_TRAITEMENTNPAI) : les courriers revenus « n'habite pas a l'adresse indiquee » et
/// l'action menee sur chacun. Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RCS\Exe\PROC_TraitementNpai\FrmProcTraitementNpai.cs. L'ecran V2 n'a
/// pas de liste : on flashe le code Data d'UN courrier revenu, puis on choisit une action. Choisir l'action cree une
/// demande de code processus « NPAI » sur le dossier (affichageDiriLS l. 1172-1180, et SiegeLR, SiegeMof, SiegeRof,
/// Enreg : Demande.CreateDemande(..., "NPAI", ...), DMND_ID_DSRCS = le dossier), qualifiee (Q). Ses parametres
/// (DMND_TEXTE_PARAMETRE, « cle=valeur;... ») portent l'action, typeNPAI, et l'exercice d'une relance de comptes
/// annuels, LstIdExer, repris de la demande flashee (parametreDemandeTypeNpai, l. 2152-2170). Valider clot la demande
/// en V (validerDiriLS l. 1483, validerEnreg l. 1525...) ; abandonner l'action ou fermer l'ecran la passe en X
/// (btAnnule_Click l. 1101-1107, _RetourEtatInitial l. 242-251). Ces demandes sont donc le registre des courriers
/// revenus traites : la liste de cet ecran. Dans la base V3, DEMANDE et MODELE_PROCESSUS sont les tables V2 telles
/// quelles (v2.*), DOSSIER_RCS rec.DossierRcs, ENTREPRISE rec.Entreprise.
///
/// LE COURRIER D'ORIGINE. La V2 note sur la demande flashee « <action>=<n° de la demande NPAI> »
/// (notifierAncienneDemande, l. 2173-2181) : c'est le seul lien entre un courrier revenu et le courrier parti. Un
/// courrier envoye par une demande NPAI peut revenir a son tour : la demande d'origine est alors une demande NPAI, et
/// l'on remonte (au plus 3 crans : RIG_DEV, lu le 25/11/2026, n'en a pas plus -- 1 106 SASSOSCI, 547 SDCA en direct,
/// 799 et 157 au 2e cran, 2 au 3e). Le libelle du courrier d'origine est celui de son processus
/// (MODELE_PROCESSUS.MDPRC_LIBELLE, que la V2 affiche « Processus <code> - <libelle> », l. 383-385).
/// Deux lectures et non une : la base V3 ne recopie que la cle primaire des tables V2 (CopieTelleQuelle.Ddl), une
/// recherche par dossier ligne a ligne y parcourrait toute v2.DEMANDE pour chaque courrier ; ici, une passe ramene les
/// demandes qui portent une action NPAI dans les dossiers concernes, et le lien se lit valeur exacte en memoire.
///
/// FILTRES : code processus NPAI ; une demande abandonnee (X) n'est pas une ligne. Ordre : ce qui est en cours d'abord,
/// puis ce qui est traite ; dans chaque bloc, le plus recent en tete.
///
/// GROUPES : « Relances de comptes annuels » quand le courrier d'origine est une relance de la surveillance des comptes
/// annuels (processus SDCA, « Surveillances des depots de comptes annuels ») ou que la demande porte des exercices
/// (LstIdExer) ; « Formalites » pour tout autre courrier revenu (dans RIG_DEV, surtout la surveillance SASSOSCI : le
/// libelle du courrier d'origine le dit dans les precisions).
///
/// REGLE D'ETAT (DMND_ETAT_DEMANDE) :
///   Q Qualifiee          -> EnCours « En cours » : courrier flashe, societe et formalite vues, action pas encore
///                           validee (0, 1) ;
///   V Validee            -> Validee « Traite » : courrier, societe, action (0, 1, 2), plus l'impression (3) quand
///                           l'action imprime un courrier (DiriLS, SiegeLR, SiegeMof, SiegeRof :
///                           ImpressionImmediateEnBatch, l. 1484, 1636, 1703, 1782 ; pas Enreg) ;
///   X                    -> pas de ligne ;
///   tout autre code      -> EnCours, courrier flashe (0), avec le code de l'ancien RIG dans les precisions.
/// « A traiter » (AFaire) n'a pas de ligne : un courrier revenu laisse une trace dans la base quand on le flashe.
/// </summary>
internal sealed class LecteurCourriersNpai : ILecteurEcran
{
    public string Procedure => "PROC_TRAITEMENTNPAI";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Formalites = "Formalités", RelancesComptes = "Relances de comptes annuels";

    /// <summary>Le processus des relances de comptes annuels (MODELE_PROCESSUS de RIG_DEV).</summary>
    private const string SurveillanceComptes = "SDCA";

    /// <summary>Les actions de la V2, cles de typeNPAI et de la note posee sur la demande flashee (getLibelleTypeNpai,
    /// l. 2215-2234 ; SiegeRof n'y est pas : le libelle de son bouton, l. 635).</summary>
    internal static readonly IReadOnlyDictionary<string, string> Actions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["DiriLS"] = "Lettre simple aux dirigeants",
        ["SiegeLR"] = "LRAR au siège de la société",
        ["SiegeMof"] = "Mention d'Office de cessation",
        ["SiegeRof"] = "Formalité de radiation d'office",
        ["Enreg"] = "Enregistrement du NPAI",
    };

    // Les demandes qui portent la note d'une action NPAI, dans les dossiers qui ont une demande NPAI ; la plus recente
    // d'abord (une demande NPAI notee deux fois garde la note la plus recente).
    private const string RequeteOrigines = """
        SELECT o.DMND_ID_DMND, o.DMND_CODE_PROSS, o.DMND_TEXTE_PARAMETRE, m.MDPRC_LIBELLE
        FROM v2.DEMANDE AS o
        OUTER APPLY (SELECT TOP (1) mp.MDPRC_LIBELLE FROM v2.MODELE_PROCESSUS AS mp
                     WHERE mp.MDPRC_CODE_PROSS = o.DMND_CODE_PROSS ORDER BY mp.MDPRC_ID_MDPRC) AS m
        WHERE o.DMND_ID_DSRCS IN (SELECT n.DMND_ID_DSRCS FROM v2.DEMANDE AS n WHERE n.DMND_CODE_PROSS = 'NPAI')
          AND (o.DMND_TEXTE_PARAMETRE LIKE '%DiriLS=%' OR o.DMND_TEXTE_PARAMETRE LIKE '%SiegeLR=%'
               OR o.DMND_TEXTE_PARAMETRE LIKE '%SiegeMof=%' OR o.DMND_TEXTE_PARAMETRE LIKE '%SiegeRof=%'
               OR o.DMND_TEXTE_PARAMETRE LIKE '%Enreg=%')
        ORDER BY o.DMND_ID_DMND DESC
        """;

    private const string Requete = """
        SELECT TOP (@plafond) d.DMND_ID_DMND, d.DMND_ETAT_DEMANDE, d.DMND_NUM_DEMANDE, d.DMND_NOM, d.DMND_TEXTE_PARAMETRE,
               d.DMND_DATE_CREATION, d.DMND_DATE_VALIDATION, s.NumeroGestion, e.Denomination, COUNT(*) OVER () AS Total
        FROM v2.DEMANDE AS d
        LEFT JOIN rec.DossierRcs AS s ON s.Id = d.DMND_ID_DSRCS
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        WHERE d.DMND_CODE_PROSS = 'NPAI' AND d.DMND_ETAT_DEMANDE <> 'X'
        ORDER BY CASE WHEN d.DMND_ETAT_DEMANDE = 'V' THEN 1 ELSE 0 END,
                 COALESCE(d.DMND_DATE_VALIDATION, d.DMND_DATE_CREATION) DESC, d.DMND_ID_DMND DESC
        """;

    /// <summary>La demande dont le courrier est revenu : son processus et son libelle, et son identifiant.</summary>
    private sealed record Origine(int Id, string Code, string Libelle);

    public async Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        // n° de demande NPAI -> la demande qui porte sa note.
        var notees = new Dictionary<int, Origine>();
        await using (var r = await LectureV2.LireAsync(baseGreffe, RequeteOrigines, ct))
            while (await r.ReadAsync(ct))
            {
                var origine = new Origine(Convert.ToInt32(r["DMND_ID_DMND"]), FormeLigne.Texte(r, "DMND_CODE_PROSS"),
                                          FormeLigne.Texte(r, "MDPRC_LIBELLE"));
                foreach (var (cle, valeur) in Parametres(FormeLigne.Texte(r, "DMND_TEXTE_PARAMETRE")))
                    if (Actions.ContainsKey(cle) && int.TryParse(valeur, out var npai) && npai != origine.Id)
                        notees.TryAdd(npai, origine);
            }
        return await LectureEcran.LireAsync(baseGreffe, Requete, r => Ligne(r, notees), ct, new SqlParameter("@plafond", plafond));
    }

    private static LigneLue Ligne(SqlDataReader r, IReadOnlyDictionary<int, Origine> notees)
    {
        var parametres = Parametres(FormeLigne.Texte(r, "DMND_TEXTE_PARAMETRE"));
        var type = parametres.GetValueOrDefault("typeNPAI", "");
        var etatV2 = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE");
        var (etat, faites) = Etat(etatV2, type);
        var date = etat == "Validee"
            ? FormeLigne.DateDe(r, "DMND_DATE_VALIDATION") ?? FormeLigne.DateDe(r, "DMND_DATE_CREATION")
            : FormeLigne.DateDe(r, "DMND_DATE_CREATION");
        var quand = date is null ? null : etat == "Validee" ? $"traité le {FormeLigne.Date(date)}" : $"commencé le {FormeLigne.Date(date)}";
        var origine = CourrierDOrigine(Convert.ToInt32(r["DMND_ID_DMND"]), notees);
        var precisions = FormeLigne.Joindre(Actions.GetValueOrDefault(type, type),
                                            origine is null ? null : $"courrier d'origine : {(origine.Libelle.Length > 0 ? origine.Libelle : origine.Code)}",
                                            quand, $"demande n° {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}",
                                            etatV2 is "Q" or "V" ? null : $"état « {etatV2} » de l'ancien RIG");
        var dossier = FormeLigne.Joindre(FormeLigne.Texte(r, "Denomination"), FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion")));
        var nom = dossier.Length > 0 ? dossier : FormeLigne.Texte(r, "DMND_NOM");
        var relance = origine?.Code == SurveillanceComptes
                      || (parametres.TryGetValue("LstIdExer", out var exercices) && exercices.Length > 0);
        // L'id : la demande NPAI (DMND_ID_DMND), que « Annuler » vise (EcrivainCourriersNpai.cs, lot 16).
        return new LigneLue(nom, precisions, relance ? RelancesComptes : Formalites, etat, faites)
        {
            Id = Convert.ToInt32(r["DMND_ID_DMND"]).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Le premier courrier d'origine qui n'est pas lui-meme une demande NPAI, en remontant au plus 3 crans ;
    /// null sans note.</summary>
    private static Origine? CourrierDOrigine(int npai, IReadOnlyDictionary<int, Origine> notees)
    {
        Origine? origine = null;
        for (var (id, crans) = (npai, 0); crans < 3 && notees.TryGetValue(id, out var o); (id, crans) = (o.Id, crans + 1))
        {
            origine = o;
            if (o.Code != "NPAI") break;
        }
        return origine;
    }

    /// <summary>La regle d'etat (en tete du fichier) : etat du descripteur et marches franchies.</summary>
    internal static (string Etat, int[] Faites) Etat(string etatV2, string typeNpai) => etatV2 switch
    {
        "Q" => ("EnCours", [0, 1]),
        "V" => ("Validee", Actions.ContainsKey(typeNpai) && !typeNpai.Equals("Enreg", StringComparison.OrdinalIgnoreCase) ? [0, 1, 2, 3] : [0, 1, 2]),
        _ => ("EnCours", [0]),
    };

    /// <summary>Les parametres d'une demande V2, « cle=valeur;cle=valeur », lus comme AmiSystem.ListeParametre
    /// (ListeParametre.cs:209-245) : separateur « ; », cle sans casse, la derniere valeur d'une cle repetee.</summary>
    internal static IReadOnlyDictionary<string, string> Parametres(string texte)
    {
        var parametres = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var morceau in texte.Split(';', StringSplitOptions.RemoveEmptyEntries))
            if (morceau.Trim().Split('=', 2) is [var cle, var valeur])
                parametres[cle] = valeur;
        return parametres;
    }
}
