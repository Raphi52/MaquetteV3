using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Relances comptes annuels » (PROC_DCA_RELANCE) : les dossiers relances pour n'avoir pas depose leurs comptes
/// annuels. Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG). La fiche proc_dca_relance.md (Brain, reference/proc) nomme la classe : PROC_RDCA
/// (D:\RigApplication\Source\RIG\DLL\Processus\SURVEILLANCES\PROC_SDCA\PROC_DCA_RELANCE\PROC_RDCA.cs). Relancer un
/// dossier y cree UNE diligence de nature « SDCA » (etat diligence « 1 », etat relance « 2 », l. 47), rattachee a une
/// action du dossier (l. 49-53) et aux exercices relances (exer.CreerDiligence, l. 91 : EXERCICE.EXRCC_ID_DLGRC), puis
/// une relance RELANCE_RCS par courrier (l. 93-97) : n° 1 lettre simple « S » (limite + 1 mois, l. 55-58), n° 2 mise en
/// demeure « SMD » (etat relance 4, l. 66-71), n° 3 avis au parquet « APRQ » (etat relance 6, l. 72-78). Une ligne de
/// l'ecran = une diligence SDCA (le dossier relance), son dossier par l'action (DLGRC_ID_ACTRC -> rec.ActionRegistre ->
/// rec.DossierRcs, Serveur/Recopie/FichesV3.cs).
///
/// FILTRES. Nature SDCA. Une diligence passee a l'injonction (une relance « IDCA » portant une instance : le test de
/// l'ancien RIG, PROC_DCA.cs:2092 HasOneInjonction) n'est plus une relance : elle est a l'ecran « Injonctions comptes
/// annuels ». Les exercices jamais relances (le premier envoi que la V2 cherche par la requete parametree
/// NATURE_DILIGENCE_PARAM SDCA / RSURV / 1 : forme juridique, dirigeants, procedures collectives) ne sont pas encore
/// repris : ils n'ont pas de diligence.
///
/// GROUPE (la reponse au courrier, la premiere qui s'applique) :
///   diligence close regularisee (DLGRC_ETAT_DILIGENCE « 9 ») -> « Depot tardif » : les comptes ont ete deposes apres
///                       la relance (DiligenceRcs.cs:506-508, CloreDiligence : date de regularisation, etat 9) ;
///   un exercice relance porte une demande de reclamation (EXRCC_ID_DMND_RECLA) -> « Reclamation » ;
///   une demande DCA ou DCADEMAT refusee (etat O) creee depuis la premiere relance -> « Refus » ;
///   sinon -> « Sans reponse ».
///
/// REGLE D'ETAT (DLGRC_ETAT_DILIGENCE : 1 en cours, 8 clos non regularise, 9 clos regularise ; DLGRC_ETAT_RELANCE :
/// valeurs de ETAT_RELANCE, 1/3/5 « A faire ... », 2/4/6/8 « ... faite » -- RIG_DEV, lu le 25/11/2026) :
///   en cours, etat relance 1, 3 ou 5          -> AFaire  « A relancer » : aucune marche ;
///   aucune relance envoyee, ou en cours avec reclamation -> EnCours « En cours » : dossier, exercices (0, 1), plus
///                                                ce qui est fait ;
///   relance envoyee (en cours ou close)       -> Validee « Relance » : dossier, exercices, courrier (0, 1, 3) ;
/// une relance est « envoyee » si la diligence en a une (RELANCE_RCS) OU si son etat relance le dit (2, 4, 6, 8 :
/// « ... faite ») -- la copie de dev ne garde que 200 lignes de RELANCE_RCS, l'etat de la diligence fait foi ;
/// et dans tous les cas hors AFaire : facturation (2) si une relance est facturee (RLCRC_FACTURE), reponse (4) si le
/// groupe n'est pas « Sans reponse ». Ordre : a relancer, en cours, relances en cours, puis closes ; la relance la
/// plus recente en tete.
/// </summary>
internal sealed class LecteurRelancesComptesAnnuels : ILecteurEcran
{
    public string Procedure => "PROC_DCA_RELANCE";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string SansReponse = "Sans réponse", Reclamation = "Réclamation", Refus = "Refus", DepotTardif = "Dépôt tardif";

    private const string Requete = $"""
        SELECT TOP (@plafond)
               g.DLGRC_ETAT_DILIGENCE, g.DLGRC_ETAT_RELANCE, g.DLGRC_DATE_REGULARISATION,
               s.NumeroGestion, s.Siren, e.Denomination,
               der.RLCRC_NUM_ORDRE, der.RLCRC_TYPE_RELANCE, der.RLCRC_DATE_RELANCE, der.RLCRC_DATE_LIMITE,
               x.Exercices, x.Cloture, x.Reclamation, f.Facturee, o.DateRefus,
               COUNT(*) OVER () AS Total
        FROM v2.DILIGENCE_RCS AS g
        JOIN rec.ActionRegistre AS a ON a.Id = g.DLGRC_ID_ACTRC
        JOIN rec.DossierRcs AS s ON s.Id = a.IdDossier
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        {FormeRelance.DerniereRelance}
        OUTER APPLY (SELECT COUNT(*) AS Exercices, MAX(ex.EXRCC_DATE_CLOTURE) AS Cloture, MAX(ex.EXRCC_ID_DMND_RECLA) AS Reclamation
                     FROM v2.EXERCICE AS ex WHERE ex.EXRCC_ID_DLGRC = g.DLGRC_ID_DLGRC) AS x
        {FormeRelance.Facturee}
        OUTER APPLY (SELECT MAX(d.DMND_DATE_CREATION) AS DateRefus FROM v2.DEMANDE AS d
                     WHERE d.DMND_ID_DSRCS = s.Id AND d.DMND_CODE_PROSS IN ('DCA', 'DCADEMAT') AND d.DMND_ETAT_DEMANDE = 'O'
                       AND d.DMND_DATE_CREATION >= (SELECT MIN(r1.RLCRC_DATE_RELANCE) FROM v2.RELANCE_RCS AS r1
                                                     WHERE r1.RLCRC_ID_DLGRC = g.DLGRC_ID_DLGRC)) AS o
        WHERE g.DLGRC_NATURE_DILIGENCE = 'SDCA'
          AND NOT EXISTS (SELECT 1 FROM v2.RELANCE_RCS AS i WHERE i.RLCRC_ID_DLGRC = g.DLGRC_ID_DLGRC
                          AND i.RLCRC_TYPE_RELANCE = 'IDCA' AND i.RLCRC_ID_INSTN IS NOT NULL)
        ORDER BY CASE WHEN g.DLGRC_ETAT_DILIGENCE = '1' AND g.DLGRC_ETAT_RELANCE IN ('1', '3', '5') THEN 0
                      WHEN NOT {FormeRelance.EnvoyeeSql} OR (g.DLGRC_ETAT_DILIGENCE = '1' AND x.Reclamation IS NOT NULL) THEN 1
                      WHEN g.DLGRC_ETAT_DILIGENCE = '1' THEN 2
                      ELSE 3 END,
                 der.RLCRC_DATE_RELANCE DESC, g.DLGRC_ID_DLGRC DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var etatDiligence = FormeLigne.Texte(r, "DLGRC_ETAT_DILIGENCE");
        var etatRelance = FormeLigne.Texte(r, "DLGRC_ETAT_RELANCE");
        var envoyee = FormeRelance.Envoyee(r, etatRelance);
        var dateRefus = FormeLigne.DateDe(r, "DateRefus");
        var reclamation = r["Reclamation"] is not DBNull;
        var groupe = etatDiligence == "9" ? DepotTardif : reclamation ? Reclamation : dateRefus is not null ? Refus : SansReponse;
        var (etat, faites) = Etat(etatDiligence, etatRelance, envoyee, groupe, facturee: r["Facturee"] is int f && f == 1);

        var reponse = groupe switch
        {
            DepotTardif => $"régularisé le {FormeLigne.Date(FormeLigne.DateDe(r, "DLGRC_DATE_REGULARISATION"))}",
            Reclamation => "réclamation reçue",
            Refus => $"refus du {FormeLigne.Date(dateRefus)}",
            _ => etatDiligence == "8" ? "close sans régularisation" : null,
        };
        return new LigneLue(FormeRelance.NomDossier(r),
                            FormeLigne.Joindre(Exercices(r), FormeRelance.Relance(r, etatRelance, etat), reponse),
                            groupe, etat, faites);
    }

    /// <summary>La regle d'etat (en tete du fichier) : etat du descripteur et marches franchies.</summary>
    internal static (string Etat, int[] Faites) Etat(string etatDiligence, string etatRelance, bool envoyee, string groupe, bool facturee)
    {
        var enCours = etatDiligence == "1";
        if (enCours && etatRelance is "1" or "3" or "5") return ("AFaire", []);
        var faites = new List<int> { 0, 1 };
        if (facturee) faites.Add(2);
        if (envoyee) faites.Add(3);
        if (groupe != SansReponse) faites.Add(4);
        return (!envoyee || (enCours && groupe == Reclamation) ? "EnCours" : "Validee", [.. faites]);
    }

    /// <summary>« Exercice clos le 31/12/2024 », ou leur nombre et la cloture la plus recente.</summary>
    private static string Exercices(SqlDataReader r)
    {
        var nombre = r["Exercices"] is int n ? n : 0;
        var cloture = FormeLigne.Date(FormeLigne.DateDe(r, "Cloture"));
        return nombre switch
        {
            0 => "",
            1 => $"Exercice clos le {cloture}",
            _ => $"{nombre} exercices, le dernier clos le {cloture}",
        };
    }
}

/// <summary>
/// Les morceaux communs aux lecteurs de l'ecran reuni « Relances et injonctions » (relances et injonctions des comptes
/// annuels et des beneficiaires) : la derniere relance d'une diligence, sa facturation, le nom du dossier et
/// l'ecriture d'une relance. Les tables : DILIGENCE_RCS et RELANCE_RCS de l'ancien RIG, telles quelles (v2.*).
/// </summary>
internal static class FormeRelance
{
    /// <summary>La derniere relance de la diligence g (numero d'ordre le plus haut) : alias der.</summary>
    internal const string DerniereRelance = """
        OUTER APPLY (SELECT TOP (1) rl.RLCRC_NUM_ORDRE, rl.RLCRC_TYPE_RELANCE, rl.RLCRC_DATE_RELANCE, rl.RLCRC_DATE_LIMITE
                     FROM v2.RELANCE_RCS AS rl WHERE rl.RLCRC_ID_DLGRC = g.DLGRC_ID_DLGRC
                     ORDER BY rl.RLCRC_NUM_ORDRE DESC, rl.RLCRC_ID_RLCRC DESC) AS der
        """;

    /// <summary>Une relance est envoyee : la diligence en a une (der), ou son etat relance est « ... faite » (2, 4, 6, 8,
    /// ETAT_RELANCE) -- la copie de dev ne garde que 200 lignes de RELANCE_RCS, l'etat de la diligence fait foi.</summary>
    internal static bool Envoyee(SqlDataReader r, string etatRelance)
        => r["RLCRC_NUM_ORDRE"] is not DBNull || etatRelance is "2" or "4" or "6" or "8";

    /// <summary>La condition SQL jumelle de <see cref="Envoyee"/> (diligence g, derniere relance der).</summary>
    // fix-ok: mesure sur la copie de dev (route PROC_DCA_RELANCE, 25/11/2026) -- les 274 diligences SDCA closes sortaient
    // « En cours · aucune relance envoyee » : RELANCE_RCS y est tronquee a 200 lignes, DLGRC_ETAT_RELANCE dit « faite ».
    internal const string EnvoyeeSql =
        "(der.RLCRC_NUM_ORDRE IS NOT NULL OR ISNULL(g.DLGRC_ETAT_RELANCE, '') IN ('2', '4', '6', '8'))";

    /// <summary>1 si une relance de la diligence g est facturee (RLCRC_FACTURE) : alias f, colonne Facturee.</summary>
    internal const string Facturee = """
        OUTER APPLY (SELECT MAX(CAST(rf.RLCRC_FACTURE AS int)) AS Facturee FROM v2.RELANCE_RCS AS rf
                     WHERE rf.RLCRC_ID_DLGRC = g.DLGRC_ID_DLGRC) AS f
        """;

    /// <summary>La designation du dossier et son SIREN, sinon son numero de gestion.</summary>
    internal static string NomDossier(SqlDataReader r)
    {
        var siren = FormeLigne.Siren(FormeLigne.Texte(r, "Siren"));
        return FormeLigne.Joindre(FormeLigne.Texte(r, "Denomination"),
                                  siren.Length > 0 ? $"SIREN {siren}" : FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion")));
    }

    /// <summary>
    /// La relance a faire (libelles de ETAT_RELANCE : « A faire 1ere relance »...), ou la derniere envoyee : son nom
    /// (type de relance de PROC_RDCA.cs:55-78 : S lettre simple, SMD mise en demeure, APRQ avis au parquet), sa date et
    /// sa date limite.
    /// </summary>
    internal static string Relance(SqlDataReader r, string etatRelance, string etat)
    {
        if (etat == "AFaire")
            return etatRelance switch { "1" => "1re relance à faire", "3" => "2e relance à faire", _ => "3e phase à faire" };
        if (r["RLCRC_NUM_ORDRE"] is DBNull)
            return etatRelance switch
            {
                "2" => "1re relance faite",
                "4" => "2e relance faite",
                "6" => "3e phase faite",
                "8" => "4e phase faite",
                _ => "aucune relance envoyée",
            };
        var nom = FormeLigne.Texte(r, "RLCRC_TYPE_RELANCE") switch
        {
            "SMD" => "Mise en demeure",
            "APRQ" => "Avis au parquet",
            _ => $"Relance {FormeLigne.Texte(r, "RLCRC_NUM_ORDRE")}",
        };
        var limite = FormeLigne.DateDe(r, "RLCRC_DATE_LIMITE");
        return FormeLigne.Joindre($"{nom} du {FormeLigne.Date(FormeLigne.DateDe(r, "RLCRC_DATE_RELANCE"))}",
                                  limite is null ? null : $"limite {FormeLigne.Date(limite)}");
    }
}
