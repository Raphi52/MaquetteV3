using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Navires » (PROC_RECHERCHE_BATEAU) : la recherche des navires du registre et la fiche d'un navire. Mode d'emploi :
/// LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026. Lecture seule (la V2 renvoie sa selection a l'ecran
/// appelant, sans rien ecrire).
///
/// SOURCE (ancien RIG) : PROC_RECHERCHE_BATEAU, OPE_RECHERCHE_BATEAU (le Detail OPE de proc_recherche_bateau.md fait foi) :
///   - LoadLstBateau (OPE_RECHERCHE_BATEAU.cs:138-166) charge les seuls NAVIRES (BTEAU_IS_NAVIRE), et pour un bateau qui n'est
///     pas le dernier de son numero d'enregistrement (BTEAU_IS_LAST faux) le nom du plus recent (« Nom le plus récent ») ; puis
///     Rechercher() (:104-136) peuple la grille DES L'OUVERTURE, sans critere : la liste a l'ouverture de la V3 est la meme ;
///   - Rechercher() : le nom (recherche plein texte sur « NumEnr Nom ») ET le numero d'enregistrement (une partie suffit) se
///     cumulent ; la case « Afficher l'historique » decochee masque les anciens noms (un navire dont un bateau plus recent
///     porte le numero) ;
///   - la colonne des inscriptions compte, historique affiche, celles du bateau ; sinon celles de tout son numero
///     d'enregistrement (ultDataGridViewListBateau.AddNavire, proc_recherche_bateau.md:199-204). La V2 les compte « pour l'etat
///     d'endettement » a la date de travail WINS (GetInscriptionsForEtatEndettement) : ce filtre de date n'est pas repris, la
///     V3 compte toutes les inscriptions du navire.
///   Dans la base V3, BATEAU est la fiche rec.Bateau et INSCRIPTION rec.Inscription (REC-014, Serveur/Recopie/FichesV3.cs). Le
///   port est le CODE du quartier maritime (BTEAU_PORT) : son libelle est dans QUARTIER_MARITIME de la base commune
///   (COMMUN_RIG, RigMetier\Inscription\QuartierMaritime.cs:39-41), absente de la base V3 du greffe.
///
/// RECHERCHE (IRechercheEcran) : « Nom du navire » (LIKE sur le nom, ou le numero suivi du nom comme l'index de la V2),
/// « Numéro d'enregistrement » (LIKE), « Afficher l'historique » (oui : les anciens noms entrent dans la liste). Le champ de
/// recherche cherche le nom ou le numero.
///
/// GROUPES : « Nom actuel » (le dernier bateau de son numero) / « Ancien nom ».
/// REGLE D'ETAT (les inscriptions comptees, en tete) :
///   aucune inscription                 -> AFaire « Sans inscription » ;
///   des inscriptions, nom actuel       -> Validee « Inscrit » ;
///   des inscriptions, ancien nom       -> EnCours « Inscrit sous un ancien nom ».
///
/// FICHE (IFicheEcran), Id = BTEAU_ID_BTEAU : « Détails » (les colonnes du referentiel bateau) et « Inscriptions » (celles du
/// navire, la plus recente en tete, comme l'info-bulle de la V2 triee par INEDT_DATE_INSCRIPTION).
/// </summary>
internal sealed class LecteurNavires : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_RECHERCHE_BATEAU";

    internal const string NomActuel = "Nom actuel", AncienNom = "Ancien nom";

    internal const string CritereNom = "Nom du navire", CritereNumero = "Numéro d'enregistrement", CritereHistorique = "Afficher l'historique";

    internal const string SectionDetails = "Détails", SectionInscriptions = "Inscriptions";

    internal const string ChampNumero = "Numéro d'enregistrement", ChampNom = "Nom du navire", ChampRecent = "Nom le plus récent",
                          ChampPort = "Port (quartier maritime)", ChampImmat = "Date d'immatriculation", ChampLieu = "Lieu d'immatriculation",
                          ChampProprietaire = "Propriétaire", ChampOmi = "Numéro OMI", ChampLongueur = "Longueur", ChampJauge = "Jauge",
                          ChampNationalite = "Nationalité", ChampInscriptions = "Inscriptions du navire";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereNom, CritereNumero, CritereHistorique];

    // Le nom le plus recent : celui du dernier bateau du meme numero (Bateau.GetLastBateauByNumEnr).
    private const string Requete = """
        SELECT TOP (@plafond) b.Id, b.NumeroEnregistrement, b.Nom, b.Port, b.EstDerniere,
               (SELECT TOP (1) d.Nom FROM rec.Bateau AS d WHERE d.NumeroEnregistrement = b.NumeroEnregistrement AND d.EstDerniere = 1
                ORDER BY d.Id DESC) AS NomRecent,
               (SELECT COUNT(*) FROM rec.Inscription AS i WHERE i.IdBateau = b.Id) AS InscriptionsBateau,
               (SELECT COUNT(*) FROM rec.Inscription AS i JOIN rec.Bateau AS o ON o.Id = i.IdBateau
                WHERE o.NumeroEnregistrement = b.NumeroEnregistrement) AS InscriptionsNumero,
               COUNT(*) OVER () AS Total
        FROM rec.Bateau AS b
        WHERE b.EstNavire = 1 /*filtres*/
        ORDER BY b.Nom, b.NumeroEnregistrement, b.Id
        """;

    // Historique decoche : les seuls noms actuels (en tete du fichier).
    private const string SansHistorique = " AND ISNULL(b.EstDerniere, 0) = 1";

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), historique: false, plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("b.Nom LIKE @texte OR b.NumeroEnregistrement LIKE @texte", ("@texte", FormeLigne.Contient(recherche.Texte)));
        if (recherche.Critere(CritereNom) is { } nom)
            f.Ajouter("CONCAT(b.NumeroEnregistrement, ' ', b.Nom) LIKE @nom", ("@nom", FormeLigne.Contient(nom)));
        if (recherche.Critere(CritereNumero) is { } numero)
            f.Ajouter("b.NumeroEnregistrement LIKE @numero", ("@numero", FormeLigne.Contient(numero)));
        return Lire(baseGreffe, f, CriteresConsultation.OuiNon(recherche.Critere(CritereHistorique)) == true, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, bool historique, int plafond, CancellationToken ct)
    {
        var sql = Requete.Replace("/*filtres*/", (historique ? "" : SansHistorique) + f.EnSuite, StringComparison.Ordinal);
        return LectureEcran.LireAsync(baseGreffe, sql, historique ? LigneHistorique : LigneActuelle, ct, f.Parametres(new SqlParameter("@plafond", plafond)));
    }

    private static LigneLue LigneActuelle(SqlDataReader r) => Ligne(r, historique: false);

    private static LigneLue LigneHistorique(SqlDataReader r) => Ligne(r, historique: true);

    private static LigneLue Ligne(SqlDataReader r, bool historique)
    {
        var actuel = CriteresConsultation.Vrai(r, "EstDerniere");
        var numero = FormeLigne.Texte(r, "NumeroEnregistrement");
        var recent = FormeLigne.Texte(r, "NomRecent");
        // Historique affiche : les inscriptions du bateau ; sinon, celles de tout le numero (en tete du fichier).
        var inscriptions = Convert.ToInt32(r[historique ? "InscriptionsBateau" : "InscriptionsNumero"], System.Globalization.CultureInfo.InvariantCulture);
        var port = FormeLigne.Texte(r, "Port");
        var precisions = FormeLigne.Joindre(
            numero.Length > 0 ? $"Numéro d'enregistrement {numero}" : null,
            !actuel && recent.Length > 0 ? $"nom le plus récent : {recent}" : null,
            port.Length > 0 ? $"port {port}" : null,
            inscriptions == 0 ? "aucune inscription" : $"{inscriptions} inscription{(inscriptions > 1 ? "s" : "")}");
        // La regle d'etat (en tete du fichier).
        var etat = inscriptions == 0 ? "AFaire" : actuel ? "Validee" : "EnCours";
        return new LigneLue(FormeLigne.Texte(r, "Nom"), precisions, actuel ? NomActuel : AncienNom, etat, []) { Id = FormeLigne.Texte(r, "Id") };
    }

    // ------------------------------------------------------------------ LA FICHE

    private const string Fiche = """
        SELECT b.Id, b.NumeroEnregistrement, b.Nom, b.Port, b.DateImmatriculation, b.LieuImmatriculation, b.Proprietaire, b.NumeroOmi,
               b.Longueur, b.Jauge, b.Nationalite, b.EstDerniere,
               (SELECT TOP (1) d.Nom FROM rec.Bateau AS d WHERE d.NumeroEnregistrement = b.NumeroEnregistrement AND d.EstDerniere = 1
                ORDER BY d.Id DESC) AS NomRecent
        FROM rec.Bateau AS b
        WHERE b.Id = @id
        """;

    private const string Inscriptions = """
        SELECT i.NumeroInscription, i.TypeInscription, i.DateInscription, i.DatePeremption
        FROM rec.Inscription AS i
        WHERE i.IdBateau = @id
        ORDER BY i.DateInscription DESC, i.Id DESC
        """;

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (CriteresConsultation.Entier(id) is not { } n) return null;
        string nom;
        ChampFiche[] details;
        await using (var r = await LectureV2.LireAsync(baseGreffe, Fiche, ct, new SqlParameter("@id", n)))
        {
            if (!await r.ReadAsync(ct)) return null;
            nom = FormeLigne.Texte(r, "Nom");
            details =
            [
                new(ChampNumero, FormeLigne.Texte(r, "NumeroEnregistrement")), new(ChampNom, nom),
                new(ChampRecent, CriteresConsultation.Vrai(r, "EstDerniere") ? "" : FormeLigne.Texte(r, "NomRecent")),
                new(ChampPort, FormeLigne.Texte(r, "Port")),
                new(ChampImmat, FormeLigne.DateFiche(FormeLigne.DateDe(r, "DateImmatriculation"))),
                new(ChampLieu, FormeLigne.Texte(r, "LieuImmatriculation")), new(ChampProprietaire, FormeLigne.Texte(r, "Proprietaire")),
                new(ChampOmi, FormeLigne.Texte(r, "NumeroOmi")), new(ChampLongueur, FormeLigne.Texte(r, "Longueur")),
                new(ChampJauge, FormeLigne.Texte(r, "Jauge")), new(ChampNationalite, FormeLigne.Texte(r, "Nationalite")),
            ];
        }
        var inscriptions = await CriteresConsultation.SousListeAsync(baseGreffe, Inscriptions,
            r => [FormeLigne.Texte(r, "NumeroInscription"), FormeLigne.Texte(r, "TypeInscription"),
                  FormeLigne.Date(FormeLigne.DateDe(r, "DateInscription")), FormeLigne.Date(FormeLigne.DateDe(r, "DatePeremption"))],
            ct, new SqlParameter("@id", n));
        return new FicheEcran(Procedure, id, nom.Length > 0 ? nom : $"Navire n° {n}",
        [
            new SectionFiche(SectionDetails, details),
            new SectionFiche(SectionInscriptions,
                [CriteresConsultation.Tableau(ChampInscriptions, ["N° d'inscription", "Type", "Inscrite le", "Périme le"], inscriptions)]),
        ]);
    }
}
