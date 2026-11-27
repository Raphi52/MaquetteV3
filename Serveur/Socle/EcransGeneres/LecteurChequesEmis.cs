using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Cheques emis » (PROC_EDCHQEMIS) : l'etat de controle des remboursements emis (trop-percus et notifications). Mode d'emploi :
/// LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026. Lecture seule, comme la V2 (consultation pure, Detail OPE
/// de proc_edchqemis.md) : VOIR ouvre l'etat Crystal, sans ecriture -- la V3 montre ses lignes, sans l'etat imprime.
///
/// SOURCE (ancien RIG) : EmisTropPercus.LancerEmisTropPercus (D:\RigApplication\Source\DllRapportCrystal\RigCrystalFactu\
/// EmisTropPercus.cs:93-107) : ENCAISSEMENT x VENTILATION x CLIENT x AFFAIRE x DEMANDE (de l'affaire), DOSSIER_RCS en LEFT JOIN, x
/// MODE_PAIEMENT, WHERE R_MODEPAIEME_TYPE = 'RBT' et ENCMT_DATE_ENCAISSEMENT dans la periode (24 h incluses) ; l'origine : les deux
/// cases cochees -> NTF ou NULL ; Notifications seule -> NTF ; Trop-perçus seule -> NULL (:103-106) ; une ligne par encaissement
/// (dedoublonnage de :148-157, ORDER BY ENCMT_ID_ENCMT). Le nom affiche : le commentaire de l'encaissement pour une notification du
/// compte DGREFFE, sinon le nom du client (:137-141) ; le numero de gestion, a defaut l'abrege du client (:94).
/// L'OUVERTURE : les deux dates a aujourd'hui et les deux cases cochees (OPE_EDCHQEMIS.cs:34-37, Designer :114, :123). La copie de
/// developpement n'a aucun remboursement du jour : la recherche par periode les retrouve.
/// RECHERCHE (IRechercheEcran) : les Parametres du descripteur (« Période », « Trop-perçus », « Notifications »). Une case cochee
/// (« oui ») restreint a son origine ; aucune case cochee = les deux (le defaut de la V2, qui interdit de tout decocher : 0 element).
/// Dans la base V3, CLIENT est rec.Client, DOSSIER_RCS rec.DossierRcs. GROUPES : « Trop-perçus » (origine vide) / « Notifications »
/// (NTF). ETAT : AFaire « À contrôler ». CELLULES : remboursement, montant (decimal exact).
/// </summary>
internal sealed class LecteurChequesEmis : IRechercheEcran
{
    public string Procedure => "PROC_EDCHQEMIS";

    internal const string TropPercus = "Trop-perçus", Notifications = "Notifications", CriterePeriode = "Période";

    public IReadOnlyCollection<string> Criteres { get; } = [CriterePeriode, TropPercus, Notifications];

    private const string Requete = """
        SELECT TOP (@plafond) e.ENCMT_ID_ENCMT, e.ENCMT_NUMERO_TITRE, e.ENCMT_MONTANT, e.ENCMT_DATE_ENCAISSEMENT, e.ENCMT_ORIGINE, e.ENCMT_COMMENTAIRE,
               x.Abrege, x.Nom, x.NumeroGestion, COUNT(*) OVER () AS Total
        FROM v2.ENCAISSEMENT AS e
        INNER JOIN v2.MODE_PAIEMENT AS mp ON mp.R_MODEPAIEME_VALEUR = e.ENCMT_MODE_PAIEMENT
        CROSS APPLY (SELECT TOP (1) c.Abrege, c.Nom, s.NumeroGestion
                     FROM v2.VENTILATION AS v
                     INNER JOIN rec.Client AS c ON c.Id = v.VENTI_ID_CLIEN
                     INNER JOIN v2.AFFAIRE AS a ON a.AFFRE_ID_AFFRE = v.VENTI_ID_AFFRE
                     INNER JOIN v2.DEMANDE AS d ON d.DMND_ID_AFFRE = a.AFFRE_ID_AFFRE
                     LEFT JOIN rec.DossierRcs AS s ON s.Id = d.DMND_ID_DSRCS
                     WHERE v.VENTI_ID_ENCMT = e.ENCMT_ID_ENCMT
                     ORDER BY s.NumeroGestion DESC, v.VENTI_ID_VENTI) AS x
        WHERE mp.R_MODEPAIEME_TYPE = 'RBT' AND e.ENCMT_DATE_ENCAISSEMENT >= @debut AND e.ENCMT_DATE_ENCAISSEMENT < @finExclue /*origine*/
        ORDER BY e.ENCMT_ID_ENCMT
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, aujourdhui.Date, aujourdhui.Date, true, true, plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        DateTime debut = aujourdhui.Date, fin = aujourdhui.Date;
        if (recherche.Critere(CriterePeriode) is { } periode)
        {
            var bornes = periode.Split('|');
            debut = CriteresConsultation.Date(bornes[0]) ?? debut;
            fin = (bornes.Length > 1 ? CriteresConsultation.Date(bornes[1]) : null) ?? debut;
        }
        var trop = CriteresConsultation.OuiNon(recherche.Critere(TropPercus)) == true;
        var notif = CriteresConsultation.OuiNon(recherche.Critere(Notifications)) == true;
        if (!trop && !notif) trop = notif = true;
        return Lire(baseGreffe, debut, fin, trop, notif, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, DateTime debut, DateTime fin, bool trop, bool notif, int plafond, CancellationToken ct)
    {
        var origine = notif ? (trop ? " AND (e.ENCMT_ORIGINE = 'NTF' OR e.ENCMT_ORIGINE IS NULL)" : " AND e.ENCMT_ORIGINE = 'NTF'") : " AND e.ENCMT_ORIGINE IS NULL";
        return LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*origine*/", origine, StringComparison.Ordinal), Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@debut", debut), new SqlParameter("@finExclue", fin.AddDays(1)));
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var notification = FormeLigne.Texte(r, "ENCMT_ORIGINE") == "NTF";
        var titre = FormeLigne.Texte(r, "ENCMT_NUMERO_TITRE");
        var nom = titre.Length > 0 ? $"Remboursement {titre}" : $"Remboursement n° {FormeLigne.Texte(r, "ENCMT_ID_ENCMT")}";
        var montant = CriteresConsultation.Decimal(r, "ENCMT_MONTANT");
        var beneficiaire = notification && FormeLigne.Texte(r, "Abrege") == "DGREFFE" ? FormeLigne.Texte(r, "ENCMT_COMMENTAIRE") : FormeLigne.Texte(r, "Nom");
        var gestion = FormeLigne.Texte(r, "NumeroGestion") is { Length: > 0 } g ? FormeLigne.NumeroGestion(g) : FormeLigne.Texte(r, "Abrege");
        return new LigneLue(nom, FormeLigne.Joindre(notification ? "Notification" : "Trop-perçu", CriteresConsultation.Euros(montant),
                "le " + FormeLigne.Date(FormeLigne.DateDe(r, "ENCMT_DATE_ENCAISSEMENT")), beneficiaire, gestion),
            notification ? Notifications : TropPercus, "AFaire", [])
        {
            Id = FormeLigne.Texte(r, "ENCMT_ID_ENCMT"),
            Valeurs = [nom, CriteresConsultation.MontantFiche(montant)],
        };
    }
}
