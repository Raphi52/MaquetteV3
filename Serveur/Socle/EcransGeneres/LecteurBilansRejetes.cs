using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Bilans en rejet » (PROC_REJETS_BILANS) : les bilans deposes que la saisie des comptes a rejetes, a corriger ou a renvoyer.
/// Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026. Lecture seule : les gestes de la V2
/// ouvrent d'autres ecrans (XDCA, VGED) ou renvoient le bilan a Infogreffe (un flux EDI) -- non repris, nommes.
///
/// SOURCE (ancien RIG) : OPE_REJETS_BILANS (D:\RigApplication\Source\RIG\DLL\Processus\PROC_REJETS_BILANS\ETP_REJETS_BILANS\
/// OPE_REJETS_BILANS.cs ; le Detail OPE de proc_rejets_bilans.md fait foi). La requete (proc_rejets_bilans.md:80-96) :
///   DOSSIER_RCS JOIN ACTION_RCS (ACTRC_ID_DSRCS) JOIN DEPOT_RCS (ACTRC_ID_DPTRC) JOIN ACTE_DEPOSE (ACTDP_ID_DPTRC)
///   JOIN EXERCICE (DPTRC_ID_EXRCC), ORDER BY DPTRC_DATE DESC, DSSRC_NUM_GESTION DESC.
/// Filtres (_GetWhere, OPE_REJETS_BILANS.cs:126-200) :
///   - ACTDP_SAISIE_BILAN_CodeRejet IN (les codes de rejet) : sans motif choisi, ChiffresSeuils.GetCodesMotifsRejet()
///     (RigMetier\Depot\ChiffresSeuils.cs:181-203) -- le reglage CODES_MOTIFS_REJETS est dans la base commune (COMMUN_RIG,
///     absente de la base V3) : la liste par defaut s'applique, 3, 7, 10, 16, 17, C7, D7, E7 (ToFriendlyString, :241-243) ;
///   - la date de debut du greffe (reglage VDCAREJ_DATE_DEBUT, PARAMETRAGE de la V2 repris dans soc.Reglage) :
///     CONVERT(DATE, ACTDP_SAISIE_BILAN_DateSaisieTerminee) >= elle, si elle est posee (proc_rejets_bilans.md:421).
///     fix-ok: v2.PARAMETRAGE n'existe pas dans la base V3 (erreur « Invalid object name » mesuree sur la copie de dev le
///     26/11/2026, serveur 5096) : PARAMETRAGE est repris dans soc.Reglage (Serveur/Socle/Greffes/RepriseReglages.cs:57).
///   La designation est celle de la fiche rec.Entreprise du dossier (la V2 la lit dans INSCRIT_PM / INSCRIT_PP) ; DOSSIER_RCS est
///   rec.DossierRcs et ACTION_RCS rec.ActionRegistre (Serveur/Recopie/FichesV3.cs).
/// LA LISTE A L'OUVERTURE : celle de la V2, qui charge la grille des que l'ecran s'ouvre (RigAutomate Operation.moteur.cs:74-75 :
/// InitProcessus appelle DoChargerDonnees), aux codes par defaut et a la date de debut du greffe.
///
/// RECHERCHE (IRechercheEcran) : le numero metier de OPE_SEARCH (OPE_REJETS_BILANS.cs:140-158), saisi dans le champ de recherche
/// -- la forme File n'a pas de panneau de criteres (FormeFile.tsx:22), elle n'envoie que ce texte. La V2 choisit la colonne par le
/// format du numero (SIREN -> DSSRC_SIREN, numero de gestion -> DSSRC_NUM_GESTION, sinon numero de depot -> DPTRC_NUM_DEPOT) et
/// compare a EGALITE : ici, l'egalite sur l'une des trois colonnes (blancs retires), un numero n'ayant qu'un format. Les filtres de
/// la liste (codes de rejet, date de debut) restent. fix-ok: controle final du lot 6 (27/11/2026) -- ce lecteur ne cherchait pas :
/// le texte ne filtrait que les 200 lignes chargees. Non cherches (nommes) : les codes motifs choisis (une liste a choix multiples,
/// que les ecrans V3 n'ont pas, Descripteur.cs) et les dates de cloture (OPE_DATE_CLOTURE) -- la forme File n'envoie aucun critere.
/// Pas de fiche (IFicheEcran) : la V2 n'en a pas, elle montre l'apercu PDF du bilan (ultApercuBilan) -- le meme manque que
/// « Voir un PDF » (LecteurPiecesGed.cs : un service qui rend le fichier et une visionneuse).
///
/// GROUPES (ChiffresSeuils.IsRenvoiToInfogreffeAllowed, ChiffresSeuils.cs:226-237) : « Renvoi possible » -- les motifs 3, 7, A7,
/// B7, C7, D7, E7, que la V2 laisse renvoyer a Infogreffe apres correction -- ; « Rejet de fond » -- les autres.
/// REGLE D'ETAT :
///   une demande XDCA de la V2 en cours sur l'action du bilan (DMND_ID_ACTRC, DMND_CODE_PROSS XDCA, non validee) -> EnCours
///   « En correction » ; sinon AFaire « Rejeté ». Un bilan corrige puis renvoye perd son code de rejet (la V2 le vide,
///   proc_rejets_bilans.md:449) : il sort de la liste, « Corrigé » n'a donc pas de ligne.
/// </summary>
internal sealed class LecteurBilansRejetes : IRechercheEcran
{
    public string Procedure => "PROC_REJETS_BILANS";

    internal const string RenvoiPossible = "Renvoi possible", RejetDeFond = "Rejet de fond";

    /// <summary>Les codes qui permettent le renvoi a Infogreffe (IsRenvoiToInfogreffeAllowed).</summary>
    internal static readonly string[] CodesRenvoi = ["3", "7", "A7", "B7", "C7", "D7", "E7"];

    /// <summary>Aucun critere : la forme File n'envoie que le texte, le numero metier (en tete du fichier).</summary>
    public IReadOnlyCollection<string> Criteres { get; } = [];

    private const string Requete = """
        SELECT TOP (@plafond) s.NumeroGestion, s.Siren,
               COALESCE(NULLIF(LTRIM(RTRIM(e.Denomination)), ''), LTRIM(RTRIM(CONCAT(e.Patronyme, ' ', e.Prenom)))) AS Designation,
               p.DPTRC_NUM_DEPOT, p.DPTRC_DATE, x.EXRCC_DATE_CLOTURE, ad.ACTDP_SAISIE_BILAN_CodeRejet AS Code,
               ad.ACTDP_SAISIE_BILAN_MotifRejet AS Motif, ad.ACTDP_ID_ACDPS,
               CASE WHEN EXISTS (SELECT 1 FROM v2.DEMANDE AS d WHERE d.DMND_ID_ACTRC = a.Id AND d.DMND_CODE_PROSS = 'XDCA'
                                 AND d.DMND_ETAT_DEMANDE <> 'V') THEN 1 ELSE 0 END AS EnCorrection,
               COUNT(*) OVER () AS Total
        FROM rec.DossierRcs AS s
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        JOIN rec.ActionRegistre AS a ON a.IdDossier = s.Id
        JOIN v2.DEPOT_RCS AS p ON p.DPTRC_ID_DPTRC = a.IdDepot
        JOIN v2.ACTE_DEPOSE AS ad ON ad.ACTDP_ID_DPTRC = p.DPTRC_ID_DPTRC
        JOIN v2.EXERCICE AS x ON x.EXRCC_ID_EXRCC = p.DPTRC_ID_EXRCC
        WHERE ad.ACTDP_SAISIE_BILAN_CodeRejet IN ('3', '7', '10', '16', '17', 'C7', 'D7', 'E7')
          AND (@debutSaisie IS NULL OR CONVERT(date, ad.ACTDP_SAISIE_BILAN_DateSaisieTerminee) >= @debutSaisie) /*filtres*/
        ORDER BY p.DPTRC_DATE DESC, s.NumeroGestion DESC, ad.ACTDP_ID_ACDPS
        """;

    /// <summary>La date de debut de recherche du greffe (Parametrage.GetValeurParametrage, _GetDateDebutRecherche,
    /// OPE_REJETS_BILANS.cs:121-124 et :195-200) : PARAMETRAGE de la V2, soc.Reglage dans la V3 (reprise SOC-003). Absente, ou
    /// illisible comme date : aucune borne (la V2 compare a AmiString.dateTimeNull).</summary>
    internal static readonly LectureReglage DateDebut = new("VDCAREJ_DATE_DEBUT", "", "OPE_REJETS_BILANS.cs:121-124");

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        // Le numero metier, a egalite (en tete du fichier).
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("s.Siren = @numero OR s.NumeroGestion = @numero OR p.DPTRC_NUM_DEPOT = @numero",
                ("@numero", CriteresConsultation.Numero(recherche.Texte)));
        return Lire(baseGreffe, f, plafond, ct);
    }

    private static async Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, int plafond, CancellationToken ct)
    {
        var debut = CriteresConsultation.Date(await DateDebut.LireTexteAsync(baseGreffe, ct));
        return await LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond),
                new SqlParameter("@debutSaisie", System.Data.SqlDbType.Date) { Value = (object?)debut ?? DBNull.Value }));
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var code = FormeLigne.Texte(r, "Code");
        var cloture = FormeLigne.DateDe(r, "EXRCC_DATE_CLOTURE");
        var enCorrection = System.Convert.ToInt32(r["EnCorrection"], System.Globalization.CultureInfo.InvariantCulture) == 1;
        var nom = FormeLigne.Joindre(cloture is { } c ? $"Bilan {c.Year}" : "Bilan", FormeLigne.Texte(r, "Designation"));
        var precisions = FormeLigne.Joindre(
            $"dossier {FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion"))}",
            FormeLigne.Texte(r, "Siren") is { Length: > 0 } siren ? $"SIREN {FormeLigne.Siren(siren)}" : null,
            $"dépôt {FormeLigne.Texte(r, "DPTRC_NUM_DEPOT")} du {FormeLigne.Date(FormeLigne.DateDe(r, "DPTRC_DATE"))}",
            cloture is { } cl ? $"clôture au {FormeLigne.Date(cl)}" : null,
            $"motif {code}", FormeLigne.Texte(r, "Motif"),
            enCorrection ? "correction XDCA en cours" : null);
        // Le groupe et la regle d'etat (en tete du fichier).
        return new LigneLue(nom, precisions, CodesRenvoi.Contains(code) ? RenvoiPossible : RejetDeFond, enCorrection ? "EnCours" : "AFaire", [])
        {
            Id = FormeLigne.Texte(r, "ACTDP_ID_ACDPS"),
        };
    }
}
