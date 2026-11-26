using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Injonctions de payer » (PROC_SURVIP) : les injonctions de payer (IP) arrivees a echeance, a traiter en masse. Mode
/// d'emploi des lecteurs : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 8, 26/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_SURVIP\Etape\OPE_RECHERCHE.cs, les quatre requetes
/// de chargementTraitements (l. 278-306), dans cet ordre, un groupe chacune (le TypeTrt de la V2, PROC_SURVIP.cs:18-23) :
///   « Titre executoire »       _GestionTitreExecutoire (l. 487-522) : evenement futur IOIP/IPOPOS (IPReceptionOpposition) ;
///                              en plus, decision anterieure a la loi du 1er mars 2022 (DCJDC_DATE nulle ou plus ancienne,
///                              l. 505-506 : « apres il n'y a plus d'executoire ») ;
///   « IP Non avenue »          _GestionNonAvenue (l. 450-485) : evenement futur ISIGNI/IPSIGNIF (IPSignification) ;
///   « Caducite »               _GestionCaducite (l. 524-554) : evenement futur ICONSOIP/IPCONSOPOS (IPReceptionConsignation) ;
///   « Certif. non opposition » _GestionCertifNonOpposition (l. 556-590) : evenement futur IPCERTNOPP.
/// (codes : EvenementProcedure.typeEvenementProToBD, RigMetier\JUDICIAIRE\EvenementProcedure.cs:585-604.) Regle commune :
/// instance de type IP, non europeenne (INSTN_IP_EUROPEENNE 0), l'evenement futur ni survenu ni expire
/// (EVNPR_SURVENU 0, EVNPR_DELAI_EXPIRE 0), echu (COALESCE(EVNPR_DATE_MAXI, EVNPR_DATE_FIXE, EVNPR_DATE_MINI) < aujourd'hui
/// a 0 h -- le delai supplementaire de chaque case vaut 0 a l'ouverture), et AUCUN des evenements survenus qui closent ce
/// type, la liste de codes de chaque requete au mot pres. ORDER BY INSTN_NUM_ROLE dans chaque groupe. La V2 ne charge un type
/// que si sa case est cochee ; l'ecran V3 les montre tous, un groupe chacun.
/// Les colonnes de la ligne (RemplirLigneResult, l. 593-635) : n° de role, date de la 1re decision (ou sa date prevue), 1er
/// demandeur et 1er defendeur (PARTIE, qualifiant D% / F%, par ordre), date de signification (et « a personne »), date
/// d'opposition. Dans la base V3, INSTANCE, EVENEMENT_PROCEDURE, DECISION_JUDICIAIRE et PARTIE sont les tables V2 (v2.*).
/// Ecart assume : une instance a deux decisions avant 2022 apparaissait deux fois en titre executoire (LEFT JOIN
/// DECISION_JUDICIAIRE, l. 494) ; ici une ligne par evenement futur, retenue si l'instance n'a aucune decision ou en a une sans
/// date ou anterieure au 1er mars 2022 -- exactement les lignes que le LEFT JOIN retenait.
///
/// REGLE D'ETAT : toute ligne est « A traiter » (AFaire) -- la V2 ne liste que les echeances a traiter ; une IP traitee voit
/// son evenement futur expire (PasserEnExpirerEveProFutur*, OPE_TRAITEMENTS.cs:718-820) et sort de la liste.
///
/// GESTES : « Appliquer les traitements » N'EST PAS BRANCHE (pas d'ecrivain). Le traitement de la V2 (PreparationTraitements,
/// OPE_TRAITEMENTS.cs:585-905 ; fiche proc_survip.md, Detail OPE) est une transaction sur plusieurs objets : verrou d'une demande
/// SURVIP par instance, expiration de l'evenement futur avec un code « expire » resolu dans CODE_EVE_PRO selon LRAR / LR,
/// creation d'une instance OP (IPCADSO, IPNAVSO) numerotee (role, repertoire JGAFF, dossier judiciaire JRDSJ) et jointe a
/// l'IP, demandes d'edition, mot du directeur. Il demande son propre lot ; tant qu'il manque, l'ecran le dit
/// (« Demonstration »), rien n'est ecrit.
/// </summary>
internal sealed class LecteurSurveillanceIp : ILecteurEcran
{
    public string Procedure => "PROC_SURVIP";

    /// <summary>Les groupes du descripteur : les TypeTrt de la V2 (PROC_SURVIP.cs:18-23), au mot pres.</summary>
    internal const string TitreExecutoire = "Titre exécutoire", NonAvenue = "IP Non avenue", Caducite = "Caducité",
                          CertifNonOpposition = "Certif. non opposition";

    /// <summary>La loi du 1er mars 2022 : apres elle, plus de titre executoire (OPE_RECHERCHE.cs:74, 505-506).</summary>
    internal static readonly DateTime NouvelleLoi2022 = new(2022, 3, 1);

    // Une branche par type : (rang, groupe, codes de l'evenement futur, codes survenus qui le closent, condition en plus).
    private static readonly (int Rang, string Groupe, string Futur, string Clos, string Plus)[] Types =
    [
        (0, TitreExecutoire, "'IOIP','IPOPOS'",
            "'ICAUSEFIN','IDESIN','IPDESIN','IEXECU','IPEXECUT','IPEXECUTLR','IPEXECUTR','IOIP','IPOPOS','IAROIP','IPAROPOS','ICONSOIP','IPCONSOPOS','ICADUORDO','IPCAD','IPCCAD','IPCCADLR','IPCCADR'",
            """
            AND (NOT EXISTS (SELECT 1 FROM v2.DECISION_JUDICIAIRE AS dj WHERE dj.DCJDC_ID_INSTN = i.INSTN_ID_INSTN)
                 OR EXISTS (SELECT 1 FROM v2.DECISION_JUDICIAIRE AS dj WHERE dj.DCJDC_ID_INSTN = i.INSTN_ID_INSTN
                            AND (dj.DCJDC_DATE IS NULL OR dj.DCJDC_DATE < @loi2022)))
            """),
        (1, NonAvenue, "'ISIGNI','IPSIGNIF'",
            "'ICAUSEFIN','IDESIN','IPDESIN','INAORDO','IPNAV','IPCNAV','IPCNAVLR','IPCNAVR','ISIGNI','IPSIGNIF','IOIP','IPOPOS','IAROIP','IPAROPOS','IEXECU','IPEXECUT','IPEXECUTLR','IPEXECUTR','ICONSOIP','IPCONSOPOS','ICADUORDO','IPCAD','IPCCAD','IPCCADLR','IPCCADR'",
            ""),
        (2, Caducite, "'ICONSOIP','IPCONSOPOS'",
            "'ICAUSEFIN','IDESIN','IPDESIN','ICADUORDO','IPCAD','IPCCAD','IPCCADLR','IPCCADR','ICONSOIP','IPCONSOPOS'",
            ""),
        (3, CertifNonOpposition, "'IPCERTNOPP'",
            "'ICAUSEFIN','IDESIN','IPDESIN','ICADUORDO','IPCAD','IPCCAD','IPCCADLR','IPCCADR','ICONSOIP','IPCONSOPOS','IOIP','IPOPOS'",
            ""),
    ];

    private static readonly string Requete = $"""
        SELECT TOP (@plafond) t.Rang, t.Groupe, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, ev.EVNPR_ID_EVNPR,
               COALESCE(ev.EVNPR_DATE_MAXI, ev.EVNPR_DATE_FIXE, ev.EVNPR_DATE_MINI) AS Echeance,
               (SELECT TOP (1) COALESCE(dj.DCJDC_DATE, dj.DCJDC_DATE_PREVUE) FROM v2.DECISION_JUDICIAIRE AS dj
                WHERE dj.DCJDC_ID_INSTN = i.INSTN_ID_INSTN ORDER BY dj.DCJDC_NUM_ORDRE, dj.DCJDC_ID_DCJDC) AS DateDecision,
               (SELECT TOP (1) COALESCE(NULLIF(p.PRT_DESIGNATION, ''), p.PRT_NOM) FROM v2.PARTIE AS p
                WHERE p.PRT_ID_INSTN = i.INSTN_ID_INSTN AND p.PRT_QUALIF_PARTIE LIKE 'D%' ORDER BY p.PRT_NUM_ORDRE, p.PRT_ID_PRT) AS Demandeur,
               (SELECT TOP (1) COALESCE(NULLIF(p.PRT_DESIGNATION, ''), p.PRT_NOM) FROM v2.PARTIE AS p
                WHERE p.PRT_ID_INSTN = i.INSTN_ID_INSTN AND p.PRT_QUALIF_PARTIE LIKE 'F%' ORDER BY p.PRT_NUM_ORDRE, p.PRT_ID_PRT) AS Defendeur,
               (SELECT TOP (1) s.EVNPR_DATE FROM v2.EVENEMENT_PROCEDURE AS s WHERE s.EVNPR_ID_INSTN = i.INSTN_ID_INSTN
                AND s.EVNPR_SURVENU = 1 AND s.EVNPR_CODE_EVE_PRO IN ('ISIGNI','IPSIGNIF') ORDER BY s.EVNPR_DATE) AS Signification,
               COUNT(*) OVER () AS Total
        FROM v2.INSTANCE AS i
        JOIN v2.EVENEMENT_PROCEDURE AS ev ON ev.EVNPR_ID_INSTN = i.INSTN_ID_INSTN
        JOIN (VALUES {string.Join(", ", Types.Select(t => $"({t.Rang}, N'{t.Groupe}')"))}) AS t (Rang, Groupe) ON
             {string.Join("\n             OR ", Types.Select(t => $"""
                 (t.Rang = {t.Rang} AND ev.EVNPR_CODE_EVE_PRO IN ({t.Futur})
                  AND NOT EXISTS (SELECT 1 FROM v2.EVENEMENT_PROCEDURE AS c WHERE c.EVNPR_ID_INSTN = i.INSTN_ID_INSTN
                                  AND c.EVNPR_SURVENU = 1 AND c.EVNPR_CODE_EVE_PRO IN ({t.Clos})) {t.Plus})
                 """))}
        WHERE i.INSTN_TYPE_INSTANCE = 'IP' AND i.INSTN_IP_EUROPEENNE = 0
          AND ev.EVNPR_SURVENU = 0 AND ev.EVNPR_DELAI_EXPIRE = 0
          AND COALESCE(ev.EVNPR_DATE_MAXI, ev.EVNPR_DATE_FIXE, ev.EVNPR_DATE_MINI) < @aujourdhui
        ORDER BY t.Rang, i.INSTN_NUM_ROLE, ev.EVNPR_ID_EVNPR
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond),
            new SqlParameter("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = aujourdhui.Date },
            new SqlParameter("@loi2022", System.Data.SqlDbType.DateTime) { Value = NouvelleLoi2022 });

    private static LigneLue Ligne(SqlDataReader r)
    {
        string demandeur = FormeLigne.Texte(r, "Demandeur"), defendeur = FormeLigne.Texte(r, "Defendeur");
        var parties = demandeur.Length > 0 && defendeur.Length > 0 ? $"{demandeur} c/ {defendeur}"
                    : FormeLigne.Texte(r, "INSTN_NOM_COURANT");
        var nom = FormeLigne.Joindre($"IP {FormeLigne.Texte(r, "INSTN_NUM_ROLE")}", parties);
        var decision = FormeLigne.DateDe(r, "DateDecision");
        var signification = FormeLigne.DateDe(r, "Signification");
        var precisions = FormeLigne.Joindre(decision is null ? null : $"ordonnance du {FormeLigne.Date(decision)}",
                                            signification is null ? null : $"signifiée le {FormeLigne.Date(signification)}",
                                            $"échéance du {FormeLigne.Date(FormeLigne.DateDe(r, "Echeance"))} dépassée");
        // La regle d'etat (en tete du fichier) : toute echeance listee est a traiter.
        return new LigneLue(nom, precisions, FormeLigne.Texte(r, "Groupe"), "AFaire", []);
    }
}
