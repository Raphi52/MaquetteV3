using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Lettrage » (PROC_VLETTRAGE) : par client, ce qui est facture, regle et restant du dans les lettrages de la periode, et ce qui
/// reste a lettrer. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 13, 27/11/2026. Les gestes Lettrer et
/// Délettrer : EcrivainLettrage.cs.
///
/// SOURCE (ancien RIG) : PROC_VLETTRAGE est un prototype (proc_vlettrage.md:156, Detail OPE) : ses requetes ignorent les criteres et
/// ses agregats sont jetes (OPE_CRITEREGENERAUX.cs:119-125, OPE_RESULTAT.cs:86, :106). Sa fiche demande de les brancher sur les
/// agregats de l'entite Lettrage (proc_vlettrage.md:123, :150-153), ce que fait la V3 :
///   Lettrage.GetLettrageTotalParType (Outils\DLL\RigMetier\Facture\Lettrage.cs:208-259) : LETTRAGE INNER JOIN CLIENT, par type de
///   client (« C » hors PC : clien_type = 'C' AND clien_is_compte_pc = 0 ; « D » : clien_type = 'D' ; « PC » : clien_type = 'C' AND
///   clien_is_compte_pc = 1, :219-231), LTTRG_DATE >= debut 0 h et < fin 24 h (:235), sommes de LTTRG_MT_FACTURE (facture) et
///   LTTRG_MT_ENCAISSEMENT (regle) ; la grille V2 donne une ligne par client, et par client ET affaire pour un client divers
///   (OPE_RESULTAT._GetClientDivers, « abrégé/idAffaire », :99-128), et une colonne « Restant » (RigDataGridViewResultat.cs:14-97).
/// Dans la base V3, CLIENT est la fiche rec.Client (REC-013 : Type, EstComptePc).
///
/// UNE LIGNE = un client compte (« C-<client> ») ou un client divers sur une affaire (« D-<client>-<affaire> »), la cle de
/// Lettrage.LettrerClientCompte / LettrerAffaire. Elle entre si elle a, dans la periode, un lettrage (LTTRG_DATE) ou quelque chose a
/// lettrer : une facture comptabilisee (FACTU_DATE_CREATION, FACTU_DATE_COMPTABILISATION renseignee) ou une ventilation d'un
/// encaissement comptabilise (ENCMT_DATE_ENCAISSEMENT), non encore lettree (LTTRG_NUMERO_FACTURE, LTTRG_ID_VENTI : les verifications
/// « déjà lettré » de LettrerFacture, :305-306, et LettrerEncaissement, :74-76). Le client d'attente (CLIEN_NUMERO_COMPTE = 99999999,
/// Client.cs:22), le client « Client Divers Special ANNULATION » et le client DGREFFE ne se lettrent pas (:68-72) : ils n'entrent pas.
///
/// LA LISTE A L'OUVERTURE : la periode par defaut de la V2, aujourd'hui -> aujourd'hui (OPE_CRITEREGENERAUX._initValeurParDefaut,
/// :20-23), toutes categories cochees (:30-36). La copie de developpement garde les plus anciens lettrages (2019).
/// PARAMETRES (IRechercheEcran) : « Période » (« du|au », une borne vide reprend aujourd'hui) et « Catégorie de client » (« Compte
/// hors PC », « Divers » ou « PC », une a la fois). Le champ de recherche cherche dans l'abrege, le nom ou le compte du client.
/// Non repris (nommes) : les filtres « État PC » et « Type PC » (OPE_FILTRE_PC), que la V2 ne lit nulle part (proc_vlettrage.md:109) ;
/// le detail par poste HT, TVA, BODACC, INPI, debours de la grille V2 (proc_vlettrage.md:113) -- il est ecrit par Lettrer.
///
/// GROUPES : les trois categories de la V2 (« Compte hors PC », « Divers », « PC »).
/// REGLE D'ETAT : quelque chose a lettrer -> AFaire « À lettrer » ; sinon restant (facture - regle) non nul -> EnCours « Lettré,
/// non soldé » ; sinon Validee « Soldé ».
/// VALEURS : Client, Facturé, Payé, Restant -- sommes de la periode, en decimal exact.
/// </summary>
internal sealed class LecteurLettrage : IRechercheEcran
{
    public string Procedure => "PROC_VLETTRAGE";

    internal const string Compte = "Compte hors PC", Divers = "Divers", Pc = "PC";
    internal const string CriterePeriode = "Période", CritereCategorie = "Catégorie de client";

    /// <summary>Le compte du client d'attente (Client.NUM_COMPTE_CLIENT_ATTENTE, Client.cs:22) et les clients que la V2 ne lettre pas.</summary>
    internal const string CompteAttente = "99999999", NomAnnulation = "Client Divers Special ANNULATION", AbregeDgreffe = "DGREFFE";

    public IReadOnlyCollection<string> Criteres { get; } = [CriterePeriode, CritereCategorie];

    // Les cles (client, affaire pour un divers) de la periode : un lettrage, ou une facture / une ventilation comptabilisee non lettree.
    // fix-ok: une premiere ecriture en CTE (« WITH cles AS ») etait refusee par LectureV2.ExigerLecture, qui n'admet qu'un SELECT
    // (LectureV2.cs:24 ; banc EcransGeneresBaseCheck rouge le 27/11/2026, « Lecture V2 refusee ») : les cles sont une table derivee.
    private const string Requete = """
        SELECT TOP (@plafond) k.IdClient, k.IdAffaire, c.Abrege, c.Nom, c.NumeroCompte, c.Type, c.EstComptePc,
               ISNULL(t.Facture, 0) AS Facture, ISNULL(t.Regle, 0) AS Regle, ISNULL(t.NbLettrages, 0) AS NbLettrages,
               ISNULL(af.NbFactures, 0) + ISNULL(av.NbVentilations, 0) AS ALettrer,
               COUNT(*) OVER () AS Total
        FROM (
            SELECT l.LTTRG_ID_CLIEN AS IdClient, CASE WHEN c.Type = 'D' THEN l.LTTRG_ID_AFFRE END AS IdAffaire
            FROM v2.LETTRAGE AS l INNER JOIN rec.Client AS c ON c.Id = l.LTTRG_ID_CLIEN
            WHERE l.LTTRG_DATE >= @debut AND l.LTTRG_DATE < @finExclue
            UNION
            SELECT f.FACTU_ID_CLIEN, CASE WHEN c.Type = 'D' THEN d.DMND_ID_AFFRE END
            FROM v2.FACTURE AS f INNER JOIN rec.Client AS c ON c.Id = f.FACTU_ID_CLIEN
            LEFT JOIN v2.DEMANDE AS d ON d.DMND_NUM_DEMANDE = f.FACTU_NUM_DEMANDE
            WHERE f.FACTU_DATE_CREATION >= @debut AND f.FACTU_DATE_CREATION < @finExclue AND f.FACTU_DATE_COMPTABILISATION IS NOT NULL
              AND f.FACTU_MONTANT_TTC <> 0 AND NOT EXISTS (SELECT 1 FROM v2.LETTRAGE AS x WHERE x.LTTRG_NUMERO_FACTURE = f.FACTU_NUMERO_FACTURE)
            UNION
            SELECT v.VENTI_ID_CLIEN, CASE WHEN c.Type = 'D' THEN v.VENTI_ID_AFFRE END
            FROM v2.VENTILATION AS v INNER JOIN v2.ENCAISSEMENT AS e ON e.ENCMT_ID_ENCMT = v.VENTI_ID_ENCMT
            INNER JOIN rec.Client AS c ON c.Id = v.VENTI_ID_CLIEN
            WHERE e.ENCMT_DATE_ENCAISSEMENT >= @debut AND e.ENCMT_DATE_ENCAISSEMENT < @finExclue AND e.ENCMT_DATE_COMPTABILISATION IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM v2.LETTRAGE AS x WHERE x.LTTRG_ID_VENTI = v.VENTI_ID_VENTI)) AS k
        INNER JOIN rec.Client AS c ON c.Id = k.IdClient
        OUTER APPLY (SELECT SUM(l.LTTRG_MT_FACTURE) AS Facture, SUM(l.LTTRG_MT_ENCAISSEMENT) AS Regle, COUNT(*) AS NbLettrages
                     FROM v2.LETTRAGE AS l
                     WHERE l.LTTRG_ID_CLIEN = k.IdClient AND (k.IdAffaire IS NULL OR l.LTTRG_ID_AFFRE = k.IdAffaire)
                       AND l.LTTRG_DATE >= @debut AND l.LTTRG_DATE < @finExclue) AS t
        OUTER APPLY (SELECT COUNT(*) AS NbFactures
                     FROM v2.FACTURE AS f LEFT JOIN v2.DEMANDE AS d ON d.DMND_NUM_DEMANDE = f.FACTU_NUM_DEMANDE
                     WHERE f.FACTU_ID_CLIEN = k.IdClient AND (k.IdAffaire IS NULL OR d.DMND_ID_AFFRE = k.IdAffaire)
                       AND f.FACTU_DATE_CREATION >= @debut AND f.FACTU_DATE_CREATION < @finExclue AND f.FACTU_DATE_COMPTABILISATION IS NOT NULL
                       AND f.FACTU_MONTANT_TTC <> 0
                       AND NOT EXISTS (SELECT 1 FROM v2.LETTRAGE AS x WHERE x.LTTRG_NUMERO_FACTURE = f.FACTU_NUMERO_FACTURE)) AS af
        OUTER APPLY (SELECT COUNT(*) AS NbVentilations
                     FROM v2.VENTILATION AS v INNER JOIN v2.ENCAISSEMENT AS e ON e.ENCMT_ID_ENCMT = v.VENTI_ID_ENCMT
                     WHERE v.VENTI_ID_CLIEN = k.IdClient AND (k.IdAffaire IS NULL OR v.VENTI_ID_AFFRE = k.IdAffaire)
                       AND e.ENCMT_DATE_ENCAISSEMENT >= @debut AND e.ENCMT_DATE_ENCAISSEMENT < @finExclue
                       AND e.ENCMT_DATE_COMPTABILISATION IS NOT NULL
                       AND NOT EXISTS (SELECT 1 FROM v2.LETTRAGE AS x WHERE x.LTTRG_ID_VENTI = v.VENTI_ID_VENTI)) AS av
        WHERE c.NumeroCompte <> @attente AND c.Nom <> @annulation AND ISNULL(c.Abrege, '') <> @dgreffe /*filtres*/
        ORDER BY CASE WHEN c.Type = 'D' THEN 2 WHEN c.EstComptePc = 1 THEN 3 ELSE 1 END, c.Abrege, c.Nom, k.IdClient, k.IdAffaire
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, aujourdhui.Date, aujourdhui.Date, new FiltresRecherche(), plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var (du, au) = LecteurJournalCaisse.Periode(recherche.Critere(CriterePeriode), aujourdhui);
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("c.Abrege LIKE @texte OR c.Nom LIKE @texte OR c.NumeroCompte LIKE @texte", ("@texte", FormeLigne.Contient(recherche.Texte)));
        // Les trois types de GetLettrageTotalParType (Lettrage.cs:219-231).
        switch (recherche.Critere(CritereCategorie))
        {
            case null: break;
            case Compte: f.Ajouter("c.Type = 'C' AND c.EstComptePc = 0"); break;
            case Divers: f.Ajouter("c.Type = 'D'"); break;
            case Pc: f.Ajouter("c.Type = 'C' AND c.EstComptePc = 1"); break;
            default: f.Ajouter("1 = 0"); break;
        }
        return Lire(baseGreffe, du, au, f, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, DateTime du, DateTime au, FiltresRecherche f, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond), new SqlParameter("@debut", du), new SqlParameter("@finExclue", au.AddDays(1)),
                new SqlParameter("@attente", CompteAttente), new SqlParameter("@annulation", NomAnnulation), new SqlParameter("@dgreffe", AbregeDgreffe)));

    /// <summary>L'identifiant d'une ligne : « C-<client> » pour un client compte, « D-<client>-<affaire> » pour un client divers
    /// (« D-<client> » pour ce qu'un divers porte sans affaire : une facture dont la demande n'a pas d'affaire). fix-ok: la facture
    /// L13F002 du client divers 2300472 (PROC_JVENTE.sql) n'a pas de demande, donc pas d'affaire : la premiere ecriture lui donnait
    /// « C-2300472 », et Lettrer l'aurait traite comme un compte ; l'ecrivain refuse maintenant un divers sans affaire.</summary>
    internal static string Cle(int idClient, bool divers, int? idAffaire)
        => !divers ? $"C-{idClient}" : idAffaire is { } a ? $"D-{idClient}-{a}" : $"D-{idClient}";

    private static LigneLue Ligne(SqlDataReader r)
    {
        var idClient = CriteresConsultation.EntierDe(r, "IdClient") ?? 0;
        var idAffaire = CriteresConsultation.EntierDe(r, "IdAffaire");
        var divers = FormeLigne.Texte(r, "Type") == "D";
        var groupe = divers ? Divers : CriteresConsultation.Vrai(r, "EstComptePc") ? Pc : Compte;
        var facture = CriteresConsultation.Decimal(r, "Facture") ?? 0m;
        var regle = CriteresConsultation.Decimal(r, "Regle") ?? 0m;
        var restant = facture - regle;
        var aLettrer = CriteresConsultation.EntierDe(r, "ALettrer") ?? 0;
        var client = FormeLigne.Joindre(FormeLigne.Texte(r, "Abrege"), FormeLigne.Texte(r, "Nom"));
        var nom = !divers ? client : idAffaire is { } affaire ? $"{client} / affaire {affaire}" : $"{client} / sans affaire";
        var nbLettrages = CriteresConsultation.EntierDe(r, "NbLettrages") ?? 0;
        var (etat, libelle) = aLettrer > 0 ? ("AFaire", $"{aLettrer} élément{(aLettrer > 1 ? "s" : "")} à lettrer")
            : restant != 0 ? ("EnCours", "lettré, non soldé") : ("Validee", "soldé");
        var precisions = FormeLigne.Joindre($"compte {FormeLigne.Texte(r, "NumeroCompte")}",
            $"facturé {CriteresConsultation.Euros(facture)}", $"payé {CriteresConsultation.Euros(regle)}",
            $"restant {CriteresConsultation.Euros(restant)}", $"{nbLettrages} lettrage{(nbLettrages > 1 ? "s" : "")}", libelle);
        return new LigneLue(nom, precisions, groupe, etat, [])
        {
            Id = Cle(idClient, divers, idAffaire),
            Valeurs = [nom, CriteresConsultation.MontantFiche(facture), CriteresConsultation.MontantFiche(regle), CriteresConsultation.MontantFiche(restant)],
        };
    }
}
