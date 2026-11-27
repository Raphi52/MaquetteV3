using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Injonctions comptes annuels » (PROC_DCA_INJ) : les dossiers a enjoindre de deposer leurs comptes annuels, les injonctions
/// qui attendent leur depot, et les depots recus depuis une injonction. Mode d'emploi des lecteurs : LecteurEcran.cs ; le geste :
/// EcrivainInjonctionsComptesAnnuels.cs (moteur InjonctionsIsurv.cs).
///
/// SOURCE (ancien RIG). L'injonction qui TOURNE est le moteur generique des surveillances PROC_ISURV
/// (D:\RigApplication\Source\RIG\DLL\Processus\SURVEILLANCES\PROC_ISURV\PROC_ISURV.cs, « Injonctions des surveillances », menu
/// ISURV visible dans RIG_DEV), nature SDCA. La classe que la fiche proc_dca_inj.md nomme, PROC_INJDCA (SURVEILLANCES\PROC_SDCA\
/// PROC_DCA_INJ\PROC_INJDCA.cs), herite de PROC_SURVEILLANCE_INJ, absente des sources, et son projet ne compile plus
/// (Source\CompilationFull\PowerShell\buildErrors.txt:116) : elle ne sert ici qu'a nommer la relance « IDCA ». Enjoindre cree, par
/// exercice, une instance et, sur la diligence SDCA de l'exercice, une relance « IDCA » qui porte cette instance
/// (Exercice.CreerRelanceInjonctionDCA, Exercice.cs:350-369). Trois sortes de lignes :
///
///   « Relance restee sans effet » (AFaire « A traiter ») : un DOSSIER (Id « dossier:<id> ») dont des exercices sont dans la recherche
///       de l'injonction (ExerciceAEnjoindreSql) : la recherche ISURV de RIG v2 pour SDCA, qui n'a pas de requete a elle et prend celle
///       de la relance 1 (NatureDiligence.parametre.cs:620-626 ; LecteurRelancesComptesAnnuels.ExerciceRechercheSql), enveloppee pour
///       la relance NbRelancesMax + 1 = 2 (CriteresRechercheSurveillance.cs:603-615 ; NbRelancesMax « 1 » pour SDCA dans RIG_DEV) :
///       la diligence SDCA de l'exercice en cours (DLGRC_ETAT_DILIGENCE 1), relance 1 faite (DLGRC_ETAT_RELANCE 2), sa relance 1
///       echue (RLCRC_NUM_ORDRE 1, RLCRC_DATE_LIMITE avant le jour, compare a la date seule : DBDate ; parametre.cs:768-797), etat
///       DCA « 2% » (parametre.cs:880-884), et aucune relance IDCA avec instance sur la diligence de l'exercice (le test des
///       injonctions, parametre.cs:836-845). Un meme dossier peut aussi etre « A relancer » a l'ecran des relances (relance 2 due) :
///       RIG v2 le propose aux deux moteurs, et l'injonction le sort des relances (LecteurRelancesComptesAnnuels, filtre IDCA) ;
///   « Injonction a suivre » (EnCours « En cours ») : une relance IDCA avec instance (l'injonction emise) sur une diligence SDCA
///       encore en cours (« 1 ») : le depot n'est pas encore venu la regulariser (DiligenceRcs.cs:506-508) ; une ligne par
///       injonction (RIG v2 en emet une par exercice) ;
///   les DEPOTS recus depuis une injonction (le traitement de la fiche proc_dca_inj.md : reception, exercices, facturation,
///       reclamation ou refus, validation ; demande DCA ou DCADEMAT de v2.DEMANDE) : un exercice est sous injonction quand sa
///       diligence a une relance IDCA avec instance (PROC_DCA.cs:2092 HasOneInjonction) ; valider le depot de cet exercice regularise
///       l'injonction (PROC_DCA\ETP_EXERCICES\ETP_EXERCICES.cs:132-135, demande SURV_REGUL). Une demande de depot d'un dossier sous
///       injonction, creee le jour de l'injonction ou apres, et au plus tard a la regularisation de sa diligence
///       (DLGRC_DATE_REGULARISATION) : les depots des exercices suivants ne repondent plus a l'injonction (RIG_DEV, 25/11/2026 :
///       9 679 demandes avec cette borne, 29 496 sans). Une demande supprimee (X) n'est pas une ligne (comme PROC_DCA,
///       LecteurComptesAnnuels.cs). Le depot se valide a l'ecran des comptes annuels (PROC_DCA) : ces lignes n'ont pas de geste ici.
/// Le dossier : relance -> diligence (RLCRC_ID_DLGRC) -> action (DLGRC_ID_ACTRC -> rec.ActionRegistre) -> rec.DossierRcs.
///
/// GROUPE : « Relance restee sans effet », « Injonction a suivre » ; pour un depot, reclamation (N) ou refus (O) -> « Reclamations
/// et refus » ; sinon DCADEMAT -> « Depots dematerialises », DCA -> « Depots papier ».
/// REGLE D'ETAT d'un depot (DMND_ETAT_DEMANDE, valeurs de v2.ETAT_DEMANDE, lues comme LecteurComptesAnnuels.cs) :
///   A Arrivee, Q Qualifiee, R Reservee -> AFaire  « A traiter » ;  N Reclamation -> EnCours « En cours » ;
///   V Validee, T Terminee              -> Validee « Deposee » ;    O Refus       -> Rejetee « Refusee » ;
///   tout autre (I, W, ...)             -> EnCours « En cours ».
/// Faites (forme File : l'ecran ne les lit pas) : aucune a enjoindre ; dossier (0), cabinet et decision (1), injonction (2) une
/// fois emise ; depot (3) une fois traite.
/// Ordre : a enjoindre (dossier croissant, comme parametre.cs:938), depots a traiter, injonctions a suivre, depots en cours,
/// reclamations, puis depots clos ; le plus recent en tete.
/// </summary>
internal sealed class LecteurInjonctionsComptesAnnuels : ILecteurEcran
{
    public string Procedure => "PROC_DCA_INJ";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string SansEffet = "Relance restée sans effet", ASuivre = "Injonction à suivre",
                          Dematerialises = "Dépôts dématérialisés", Papier = "Dépôts papier", ReclamationsRefus = "Réclamations et refus";

    /// <summary>Le prefixe de l'Id d'un dossier a enjoindre.</summary>
    internal const string PrefixeDossier = "dossier:";

    /// <summary>
    /// L'exercice xd du dossier s est dans la recherche de l'injonction ISURV SDCA (en tete du fichier). Parametre : @aujourdhui.
    /// L'ecrivain la relit dans la transaction du geste.
    /// </summary>
    internal const string ExerciceAEnjoindreSql = $"""
        {LecteurRelancesComptesAnnuels.ExerciceRechercheSql}
        AND xd.EXRCC_ETAT_DCA LIKE '2%'
        AND EXISTS (SELECT 1 FROM v2.DILIGENCE_RCS AS gi WHERE gi.DLGRC_ID_DLGRC = xd.EXRCC_ID_DLGRC AND gi.DLGRC_NATURE_DILIGENCE = 'SDCA'
                    AND gi.DLGRC_ETAT_DILIGENCE = '1' AND gi.DLGRC_ETAT_RELANCE = '2'
                    AND EXISTS (SELECT 1 FROM v2.RELANCE_RCS AS ri WHERE ri.RLCRC_ID_DLGRC = gi.DLGRC_ID_DLGRC AND ri.RLCRC_NUM_ORDRE = 1
                                AND ri.RLCRC_DATE_LIMITE < @aujourdhui))
        AND NOT EXISTS (SELECT 1 FROM v2.RELANCE_RCS AS rj WHERE rj.RLCRC_ID_DLGRC = xd.EXRCC_ID_DLGRC AND rj.RLCRC_TYPE_RELANCE = 'IDCA'
                        AND rj.RLCRC_ID_INSTN IS NOT NULL)
        """;

    private const string Requete = $"""
        SELECT TOP (@plafond) l.*, COUNT(*) OVER () AS Total
        FROM (
        SELECT 0 AS Rang, 'E' AS Sorte, s.Id AS IdDossier, CAST(NULL AS varchar(10)) AS DMND_CODE_PROSS, CAST(NULL AS varchar(1)) AS DMND_ETAT_DEMANDE,
               CAST(NULL AS datetime) AS DMND_DATE_CREATION, CAST(NULL AS datetime) AS DMND_DATE_VALIDATION,
               s.NumeroGestion, s.Siren, e.Denomination, CAST(NULL AS datetime) AS DateInjonction, CAST(NULL AS varchar(20)) AS Role,
               x.Exercices, x.Cloture, CAST(NULL AS int) AS IdDemande
        FROM rec.DossierRcs AS s
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        CROSS APPLY (SELECT COUNT(*) AS Exercices, MAX(xd.EXRCC_DATE_CLOTURE) AS Cloture FROM v2.EXERCICE AS xd WHERE {ExerciceAEnjoindreSql}) AS x
        WHERE x.Exercices > 0
        UNION ALL
        SELECT 2, 'I', s.Id, NULL, NULL, NULL, NULL, s.NumeroGestion, s.Siren, e.Denomination, r.RLCRC_DATE_RELANCE, i.INSTN_NUM_ROLE,
               NULL, (SELECT MAX(ex.EXRCC_DATE_CLOTURE) FROM v2.EXERCICE AS ex WHERE ex.EXRCC_ID_DLGRC = g.DLGRC_ID_DLGRC), r.RLCRC_ID_RLCRC
        FROM v2.RELANCE_RCS AS r
        JOIN v2.DILIGENCE_RCS AS g ON g.DLGRC_ID_DLGRC = r.RLCRC_ID_DLGRC AND g.DLGRC_NATURE_DILIGENCE = 'SDCA' AND g.DLGRC_ETAT_DILIGENCE = '1'
        JOIN rec.ActionRegistre AS a ON a.Id = g.DLGRC_ID_ACTRC
        JOIN rec.DossierRcs AS s ON s.Id = a.IdDossier
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        LEFT JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = r.RLCRC_ID_INSTN
        WHERE r.RLCRC_TYPE_RELANCE = 'IDCA' AND r.RLCRC_ID_INSTN IS NOT NULL
        UNION ALL
        SELECT CASE WHEN d.DMND_ETAT_DEMANDE IN ('A', 'Q', 'R') THEN 1
                    WHEN d.DMND_ETAT_DEMANDE = 'N' THEN 4
                    WHEN d.DMND_ETAT_DEMANDE IN ('V', 'T', 'O') THEN 5
                    ELSE 3 END,
               'D', s.Id, d.DMND_CODE_PROSS, d.DMND_ETAT_DEMANDE, d.DMND_DATE_CREATION, d.DMND_DATE_VALIDATION,
               s.NumeroGestion, s.Siren, e.Denomination, inj.DateInjonction, NULL, NULL, NULL, d.DMND_ID_DMND
        FROM v2.DEMANDE AS d
        JOIN rec.DossierRcs AS s ON s.Id = d.DMND_ID_DSRCS
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        CROSS APPLY (SELECT MAX(r.RLCRC_DATE_RELANCE) AS DateInjonction
                     FROM rec.ActionRegistre AS a
                     JOIN v2.DILIGENCE_RCS AS g ON g.DLGRC_ID_ACTRC = a.Id AND g.DLGRC_NATURE_DILIGENCE = 'SDCA'
                     JOIN v2.RELANCE_RCS AS r ON r.RLCRC_ID_DLGRC = g.DLGRC_ID_DLGRC AND r.RLCRC_TYPE_RELANCE = 'IDCA'
                                              AND r.RLCRC_ID_INSTN IS NOT NULL
                     WHERE a.IdDossier = s.Id AND r.RLCRC_DATE_RELANCE <= d.DMND_DATE_CREATION
                       AND (g.DLGRC_DATE_REGULARISATION IS NULL OR d.DMND_DATE_CREATION <= g.DLGRC_DATE_REGULARISATION)) AS inj
        WHERE d.DMND_CODE_PROSS IN ('DCA', 'DCADEMAT') AND d.DMND_ETAT_DEMANDE <> 'X' AND inj.DateInjonction IS NOT NULL
        ) AS l
        ORDER BY l.Rang, CASE WHEN l.Rang = 0 THEN l.IdDossier END, COALESCE(l.DMND_DATE_CREATION, l.DateInjonction) DESC, l.IdDemande DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond),
            new SqlParameter("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = aujourdhui.Date });

    private static LigneLue Ligne(SqlDataReader r)
    {
        var nom = FormeRelance.NomDossier(r);
        var cloture = FormeLigne.DateDe(r, "Cloture");
        switch (FormeLigne.Texte(r, "Sorte"))
        {
            case "E":
                var n = Convert.ToInt32(r["Exercices"], System.Globalization.CultureInfo.InvariantCulture);
                return new LigneLue(nom, FormeLigne.Joindre($"{n} exercice{(n > 1 ? "s" : "")} à enjoindre", $"clôture du {FormeLigne.Date(cloture)}",
                                                            "relance 1 échue"), SansEffet, "AFaire", [])
                    { Id = PrefixeDossier + Convert.ToString(r["IdDossier"], System.Globalization.CultureInfo.InvariantCulture) };
            case "I":
                return new LigneLue(nom, FormeLigne.Joindre($"Injonction du {FormeLigne.Date(FormeLigne.DateDe(r, "DateInjonction"))}",
                                                            FormeLigne.Texte(r, "Role") is { Length: > 0 } role ? $"rôle {role}" : null,
                                                            cloture is null ? null : $"clôture du {FormeLigne.Date(cloture)}", "non régularisée"),
                                    ASuivre, "EnCours", [0, 1, 2]);
        }
        var etatV2 = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE");
        var (etat, faites) = Etat(etatV2);
        var groupe = etatV2 is "N" or "O" ? ReclamationsRefus
                   : FormeLigne.Texte(r, "DMND_CODE_PROSS") == "DCADEMAT" ? Dematerialises : Papier;
        var creee = FormeLigne.DateDe(r, "DMND_DATE_CREATION");
        var close = FormeLigne.DateDe(r, "DMND_DATE_VALIDATION") ?? creee;
        var quand = etatV2 switch
        {
            "V" or "T" => $"déposé le {FormeLigne.Date(close)}",
            "N" => $"réclamation du {FormeLigne.Date(close)}",
            "O" => $"refusé le {FormeLigne.Date(close)}",
            _ => $"reçu le {FormeLigne.Date(creee)}",
        };
        var inconnu = etatV2 is "A" or "Q" or "R" or "I" or "W" or "N" or "V" or "T" or "O" ? null : $"état « {etatV2} » de l'ancien RIG";
        return new LigneLue(nom, FormeLigne.Joindre($"Injonction du {FormeLigne.Date(FormeLigne.DateDe(r, "DateInjonction"))}", quand, inconnu),
                            groupe, etat, faites);
    }

    /// <summary>La regle d'etat d'un depot (en tete du fichier) : etat du descripteur et marches franchies.</summary>
    internal static (string Etat, int[] Faites) Etat(string etatV2) => etatV2 switch
    {
        "A" or "Q" or "R" => ("AFaire", [0, 1, 2]),
        "N" => ("EnCours", [0, 1, 2, 3]),
        "V" or "T" => ("Validee", [0, 1, 2, 3]),
        "O" => ("Rejetee", [0, 1, 2, 3]),
        _ => ("EnCours", [0, 1, 2]),
    };
}
