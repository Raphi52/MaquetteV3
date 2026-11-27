using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Formalites INPI » (PROC_GINPI) : le rapprochement du montant attendu de l'INPI et des paiements recus, par affaire. Mode
/// d'emploi : LecteurEcran.cs ; le geste : EcrivainFormalitesInpi.cs. Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_GINPI -- MetierGinpi.RechercherGnpi (Metier\MetierGinpi.cs:
/// 59-246 ; le Detail OPE de proc_ginpi.md fait foi), aux criteres de l'ouverture (OPE_SAISIE.DoInitZoneSaisie, Saisie\OPE_SAISIE.cs:
/// 64-71) : le MOIS PRECEDENT (du 1er au dernier jour, 24 h) et les deux criteres coches « En attente de paiement de l'INPI » et
/// « Paiement INPI partiel » -- les deux etats que la regularisation vise (DataGridViewResultat.cs:135-145). « Non soldées
/// uniquement » n'est pas coche (toutes les lignes).
///   - perimetre : AFFRE_INPI_MONTANT_TTC renseigne ; affaire creee dans la periode sans facture, OU une facture de l'affaire creee
///     dans la periode (MetierGinpi.cs:88-90) ;
///   - en attente : aucune ventilation d'un encaissement INPI (ENCMT_EXT_SOURCE = 'INPI'), une facture > 0 et un montant attendu > 0 ;
///   - partiel : recu INPI - attendu &lt; -0,05 ET recu INPI - facture &lt; -0,05 (:94-131) ;
///   - l'etiquette de la ligne (:205-232), dans l'ordre du code, la derniere qui vaut gagne : en attente si attendu > 0, rien
///     recu de l'INPI et facture > 0 ; partiel si recu INPI > 0 et les deux ecarts &lt; -0,05 ; sinon « Indéfini ». Solde = recu
///     INPI + recu autre - facture (:203). Montants en decimal exact (money), tolerance de 0,05 € comme la V2.
/// GROUPES : facture > 0 -> « Formalités payantes », sinon « Formalités gratuites ». ETAT : en attente ou partiel -> EnCours
/// « Anomalie à régulariser » ; Indéfini -> AFaire « À traiter ». ORDRE : par affaire (la V2 n'a pas d'ORDER BY).
/// CELLULES : les colonnes de la grille V2 (DataGridViewResultat._InitColumns, :45-120) jusqu'au n° de liaison (:55, la colonne
/// NumLiaison de MetierGinpi.cs:69-74 : le DMND_NUM_LIAISON d'une demande de l'affaire qui a une liasse DEMAT_LIASSE_CFE, sinon un
/// DEMAT_DCA, sinon un DEMAT_DAS, portant un numero national INPI ; vide sinon), puis les montants et l'etat. Les deux dates de la
/// grille (« Date facture/affaire », « Date réception ») ne sont pas des colonnes de la V3.
/// NON REPRIS (nomme) : les autres criteres (Non traité, Paiement autre reçu, total, gratuites, à relancer, trop perçu,
/// OPE_SAISIE.cs) -- la liste d'une file s'ouvre sur les criteres de l'ouverture et n'a pas d'autre critere ; l'export Excel
/// (le bouton de l'etape Resultat) -- un classeur ecrit sur le poste de l'agent, aucune ligne de la base : la V3 n'a pas d'export de liste.
/// </summary>
internal sealed class LecteurFormalitesInpi : ILecteurEcran
{
    public string Procedure => "PROC_GINPI";

    internal const string Payantes = "Formalités payantes", Gratuites = "Formalités gratuites";
    internal const string EnAttente = "En attente de paiement de l'INPI", Partiel = "Paiement INPI partiel", Indefini = "Indéfini";

    // fix-ok: mesure au banc (27/11/2026, PROC_GINPI.attendu rouge, 2205301 absente) -- NOT EXISTS sur un SELECT SUM(...) sans GROUP BY
    // rend toujours une ligne (la somme NULL) : le critere « en attente » ne valait jamais. L'existence se lit sur les lignes, pas sur la somme.
    private const string VentilationInpi = "SELECT 1 FROM v2.VENTILATION AS v INNER JOIN v2.ENCAISSEMENT AS e ON e.ENCMT_ID_ENCMT = v.VENTI_ID_ENCMT";
    private const string Ventile = "SELECT SUM(v.VENTI_MONTANT) FROM v2.VENTILATION AS v INNER JOIN v2.ENCAISSEMENT AS e ON e.ENCMT_ID_ENCMT = v.VENTI_ID_ENCMT";
    private const string Facture = "SELECT ISNULL(SUM(f.FACTU_MONTANT_TTC), 0) FROM v2.FACTURE AS f INNER JOIN v2.DEMANDE AS d ON d.DMND_NUM_DEMANDE = f.FACTU_NUM_DEMANDE WHERE d.DMND_ID_AFFRE = a.AFFRE_ID_AFFRE";

    /// <summary>Le n° de liaison (MetierGinpi.cs:69-74) : la cascade liasse, DCA, DAS ; « TOP 1 » sans ORDER BY dans la V2, ici la
    /// premiere demande de l'affaire par id.</summary>
    private const string Liaison = """
        ISNULL((SELECT TOP (1) d.DMND_NUM_LIAISON FROM v2.DEMANDE AS d INNER JOIN v2.DEMAT_LIASSE_CFE AS l ON l.DMTLCF_ID_DMND = d.DMND_ID_DMND
                WHERE d.DMND_ID_AFFRE = a.AFFRE_ID_AFFRE AND d.DMND_NUM_LIAISON IS NOT NULL AND l.DMTLCF_NUMERO_NATIONAL_INPI IS NOT NULL ORDER BY d.DMND_ID_DMND),
        ISNULL((SELECT TOP (1) d.DMND_NUM_LIAISON FROM v2.DEMANDE AS d INNER JOIN v2.DEMAT_DCA AS c ON c.DMTDCA_ID_DMND = d.DMND_ID_DMND
                WHERE d.DMND_ID_AFFRE = a.AFFRE_ID_AFFRE AND d.DMND_NUM_LIAISON IS NOT NULL AND c.DMTDCA_NUMERO_NATIONAL_INPI IS NOT NULL ORDER BY d.DMND_ID_DMND),
        ISNULL((SELECT TOP (1) d.DMND_NUM_LIAISON FROM v2.DEMANDE AS d INNER JOIN v2.DEMAT_DAS AS s ON s.DMTDAS_ID_DMND = d.DMND_ID_DMND
                WHERE d.DMND_ID_AFFRE = a.AFFRE_ID_AFFRE AND d.DMND_NUM_LIAISON IS NOT NULL AND s.DMTDAS_NUMERO_NATIONAL_INPI IS NOT NULL ORDER BY d.DMND_ID_DMND), '')))
        """;

    /// <summary>Les colonnes calculees de MetierGinpi (:68-87), pour une affaire « a ».</summary>
    internal const string Colonnes = $"""
        a.AFFRE_ID_AFFRE, a.AFFRE_INPI_MONTANT_TTC, a.AFFRE_DATE_CREATION, {Liaison} AS NumLiaison,
               ({Facture}) AS TotalFacture,
               (SELECT COUNT(*) FROM v2.DEMANDE AS d WHERE d.DMND_ID_AFFRE = a.AFFRE_ID_AFFRE AND d.DMND_ETAT_DEMANDE IN ('V', 'X', 'T', 'O')) AS NbDemandesValidees,
               (SELECT TOP (1) f.FACTU_NUMERO_FACTURE FROM v2.FACTURE AS f INNER JOIN v2.DEMANDE AS d ON d.DMND_NUM_DEMANDE = f.FACTU_NUM_DEMANDE
                WHERE d.DMND_ID_AFFRE = a.AFFRE_ID_AFFRE ORDER BY f.FACTU_DATE_CREATION DESC) AS NumLastFact,
               ISNULL(({Ventile} WHERE ISNULL(e.ENCMT_EXT_SOURCE, '') = 'INPI' AND v.VENTI_ID_AFFRE = a.AFFRE_ID_AFFRE), 0) AS RecuINPI,
               ISNULL(({Ventile} WHERE ISNULL(e.ENCMT_EXT_SOURCE, '') <> 'INPI' AND v.VENTI_ID_AFFRE = a.AFFRE_ID_AFFRE), 0) AS RecuAutre
        """;

    private const string Requete = $"""
        SELECT TOP (@plafond) {Colonnes}, COUNT(*) OVER () AS Total
        FROM v2.AFFAIRE AS a
        WHERE a.AFFRE_INPI_MONTANT_TTC IS NOT NULL
          AND EXISTS (SELECT 1 FROM v2.DEMANDE AS dd LEFT JOIN v2.FACTURE AS ff ON dd.DMND_NUM_DEMANDE = ff.FACTU_NUM_DEMANDE
                      WHERE dd.DMND_ID_AFFRE = a.AFFRE_ID_AFFRE
                        AND ((a.AFFRE_DATE_CREATION >= @debut AND a.AFFRE_DATE_CREATION < @finExclue AND ff.FACTU_DATE_CREATION IS NULL)
                             OR (ff.FACTU_DATE_CREATION >= @debut AND ff.FACTU_DATE_CREATION < @finExclue))
                        AND ((NOT EXISTS ({VentilationInpi} WHERE v.VENTI_ID_AFFRE = a.AFFRE_ID_AFFRE AND e.ENCMT_EXT_SOURCE = 'INPI')
                              AND ISNULL(ff.FACTU_MONTANT_TTC, 0) > 0 AND a.AFFRE_INPI_MONTANT_TTC > 0)
                             OR ((({Ventile} WHERE v.VENTI_ID_AFFRE = a.AFFRE_ID_AFFRE AND e.ENCMT_EXT_SOURCE = 'INPI') - a.AFFRE_INPI_MONTANT_TTC < -0.05)
                                 AND (({Ventile} WHERE v.VENTI_ID_AFFRE = a.AFFRE_ID_AFFRE AND e.ENCMT_EXT_SOURCE = 'INPI') - ({Facture}) < -0.05))))
        ORDER BY a.AFFRE_ID_AFFRE
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var debut = new DateTime(aujourdhui.Year, aujourdhui.Month, 1).AddMonths(-1);
        return LectureEcran.LireAsync(baseGreffe, Requete, r => Ligne(Rapprochement.Lire(r)), ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@debut", debut), new SqlParameter("@finExclue", debut.AddMonths(1)));
    }

    /// <summary>Les montants d'une affaire et son etiquette (MetierGinpi.cs:186-232, criteres de l'ouverture).</summary>
    internal sealed record Rapprochement(int IdAffaire, decimal Prevu, decimal Facture, decimal RecuInpi, decimal RecuAutre, string? NumFacture,
                                         string NumLiaison = "")
    {
        public decimal Solde => RecuInpi + RecuAutre - Facture;

        public string Etat
        {
            get
            {
                var etat = Indefini;
                if (Prevu > 0 && RecuInpi == 0 && Facture > 0) etat = EnAttente;
                if (RecuInpi > 0 && RecuInpi - Prevu < -0.05m && RecuInpi - Facture < -0.05m) etat = Partiel;
                return etat;
            }
        }

        public static Rapprochement Lire(SqlDataReader r) => new(r.GetInt32(0), CriteresConsultation.Decimal(r, "AFFRE_INPI_MONTANT_TTC") ?? 0m,
            CriteresConsultation.Decimal(r, "TotalFacture") ?? 0m, CriteresConsultation.Decimal(r, "RecuINPI") ?? 0m,
            CriteresConsultation.Decimal(r, "RecuAutre") ?? 0m, r["NumLastFact"] as string, FormeLigne.Texte(r, "NumLiaison"));
    }

    private static LigneLue Ligne(Rapprochement x)
    {
        string M(decimal m) => CriteresConsultation.MontantFiche(m);
        var etat = x.Etat;
        var nom = $"Affaire n° {x.IdAffaire}";
        return new LigneLue(nom, FormeLigne.Joindre(x.NumFacture is { Length: > 0 } f ? "facture " + f : "sans facture",
                x.NumLiaison.Length > 0 ? "liaison " + x.NumLiaison : null,
                "attendu " + CriteresConsultation.Euros(x.Prevu), "reçu INPI " + CriteresConsultation.Euros(x.RecuInpi),
                x.RecuAutre != 0 ? "reçu autre " + CriteresConsultation.Euros(x.RecuAutre) : null, "solde " + CriteresConsultation.Euros(x.Solde), etat),
            x.Facture > 0 ? Payantes : Gratuites, etat == Indefini ? "AFaire" : "EnCours", [])
        {
            Id = x.IdAffaire.ToString(CultureInfo.InvariantCulture),
            Valeurs = [nom, x.NumFacture ?? "", x.NumLiaison, M(x.Facture), M(x.Prevu), M(x.RecuInpi), M(x.RecuAutre), M(x.Solde), etat],
        };
    }
}
