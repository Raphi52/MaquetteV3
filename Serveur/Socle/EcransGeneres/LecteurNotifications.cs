using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Notifications » (PROC_VNOTIF) : les notifications echangees avec les autres greffes, emises (RCS, judiciaire) et
/// recues. Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_VNOTIF, fiche Brain proc_vnotif.md, sur la base du
/// greffe. La V2 a deux onglets, « Notifications emises » et « Notifications recues », et CHACUN lance sa recherche a
/// l'ouverture (RECHERCHE_NOTIFS\OPE_RECHERCHE_NOTIFS_EMISES.cs:37-42, OPE_RECHERCHE_NOTIFS_RECUES.cs:42-48), apres que
/// ses criteres ont ete poses : le moteur initialise les operations filles avant la mere
/// (RigAutomate\OPERATION\Operation.moteur.cs:100-115). Les criteres a l'ouverture :
///   - dates : du jour moins 30 jours a 0 h au jour a 24 h (Commun\OPE_DATES.cs:83-86 ; bornes 00:00:00.000 et
///     23:59:59.998, RigTools\SQL\DBObject.cs:541-546) ;
///   - etats coches : 2, 5, 8 pour les emises (ETAT_TRAITMENT : A envoyer, Refuse Greffe, Erreur envoi), 1, 8 pour les
///     recues (ETAT_DEMAT_NOTIF : En Attente, Erreur envoi) (Commun\OPE_ETAT.cs:67-92) ;
///   - greffes : tous coches (Commun\OPE_GREFFES.cs:66-77), donc aucun filtre (Metier\MetierProcVNotif.cs:138, :261, :391) ;
///   - service : « Tous » (PROC_VNOTIF_PARAM.ParamService, Commun\OPE_SERVICE.cs:24-27) : les emises sont la requete RCS
///     UNION la requete judiciaire (MetierProcVNotif.cs:31-35), les recues sans filtre de type (l. 367-371).
/// Les requetes : MetierProcVNotif.cs:69-91 (emises RCS : NOTIFICATION_RCS_XML JOIN ACTION_RCS JOIN DOSSIER_RCS JOIN
/// ETAT_TRAITMENT), :182-207 (emises judiciaires : NOTIFICATION_JUD_XML, dossier, procedure collective et non-inscrit en
/// LEFT JOIN), :321-342 (recues : DEMAT_NOTIF, demande et dossier en LEFT JOIN). La date filtree est NTFXML_DATE_GENERATION
/// et NTFJUDXML_DATE_GENERATION pour les emises (le type de date choisi est ignore, TypeDate = "" force, l. 102 et l. 226),
/// DMTNTF_DATE_CREATION pour les recues (type « RECEPTION », OPE_RECHERCHE_NOTIFS_RECUES.cs:46 et l. 356-364). Ordre de la
/// V2 : la date de reception, croissante (l. 216 et l. 351) ; ici le numero departage.
/// Dans la base V3, ACTION_RCS est rec.ActionRegistre, DOSSIER_RCS rec.DossierRcs ; la designation est celle de la fiche
/// rec.Entreprise du dossier (la V2 la recompose depuis INSCRIT_PM et INSCRIT_PP, l. 72 et l. 185) ; les autres tables
/// sont les tables V2 telles quelles (v2.*).
///
/// GROUPES : le bloc de la requete -- emises RCS « Émises (RCS) », emises judiciaires « Émises (judiciaire) », recues
/// « Reçues ».
///
/// REGLE D'ETAT (code d'etat de la notification ; marches du descripteur : 0 Recherche, 1 Contenu). Les etats que la V2 ne
/// coche pas a l'ouverture (envoyee, receptionnee, validee, cloturee, traitee...) n'entrent pas dans la liste : aucune ligne
/// n'est « Traitée ».
///   emise a l'etat 2 « A envoyer »                 -> EnCours « En cours » : generee, l'envoi est au traitement EDI (0) ;
///   emise a l'etat 5 « Refuse Greffe » ou 8 « Erreur envoi »
///                                                  -> AFaire « À traiter » : refusee par le greffe destinataire ou non
///                                                     partie, le motif (NTFXML_LIBELLE_REJET, NTFJUDXML_ERREUR) affiche ;
///   recue a l'etat 1 « En Attente » ou 8 « Erreur envoi »
///                                                  -> AFaire « À traiter » : le greffe ne l'a pas encore traitee.
/// Les etats que seule la RECHERCHE fait entrer (critere « État », lot 6 du plan-676a4b19), libelles de ETAT_TRAITMENT et
/// ETAT_DEMAT_NOTIF : emise 3 « Envoyé » ou 4 « Réceptionné », recue 3 ou 4 -> EnCours « En cours » (en route) ; emise
/// 6 « Validé Greffe » ou 10 « Clôturé », recue 2 « Traité Greffe » -> Validee « Traitée » ; 9 « Rejet Docapost » -> AFaire.
/// </summary>
// fix-ok: la cause des editions de ce fichier par le lot 6 (plan-676a4b19) -- la liste ne se cherchait pas et ne s'ouvrait pas
// (ILecteurEcran seul) ; les filtres d'etat de chaque bloc sont devenus des marques remplacees, pour que la recherche les
// remplace sans changer la liste d'ouverture (jeu PROC_VNOTIF.attendu inchange, vert au banc EcransGeneresBase).
internal sealed class LecteurNotifications : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_VNOTIF";

    /// <summary>Les groupes du descripteur, au mot pres, par bloc de la requete (0, 1, 2).</summary>
    internal static readonly string[] Groupes = ["Émises (RCS)", "Émises (judiciaire)", "Reçues"];

    // Bloc : 0 emises RCS, 1 emises judiciaires, 2 recues (l'ordre d'affichage, et le groupe de la ligne).
    private const string Requete = """
        SELECT TOP (@plafond) l.*, COUNT(*) OVER () AS Total
        FROM (SELECT 0 AS Bloc, n.NTFXML_ID_NTFXML AS Identifiant, n.NTFXML_TYPE_NOTIFICATION AS TypeNotif,
                     CAST(NULL AS varchar(10)) AS NumPc, e.Denomination AS Designation, s.NumeroGestion AS NumGestion,
                     n.NTFXML_CODE_GREFFE AS CodeGreffe, n.NTFXML_DATE_GENERATION AS DateFiltre, n.NTFXML_DATE_ENVOI AS DateEnvoi,
                     n.NTFXML_DATE_RECEPTION AS DateReception, n.NTFXML_ETAT_TRAITMENT AS CodeEtat,
                     t.R_ETATTRAITM_LIBELLE_ECRAN AS LibelleEtat, LEFT(n.NTFXML_LIBELLE_REJET, 160) AS Rejet
              FROM v2.NOTIFICATION_RCS_XML AS n
              JOIN rec.ActionRegistre AS a ON a.Id = n.NTFXML_ID_ACTRC
              JOIN rec.DossierRcs AS s ON s.Id = a.IdDossier
              JOIN v2.ETAT_TRAITMENT AS t ON t.R_ETATTRAITM_VALEUR = n.NTFXML_ETAT_TRAITMENT
              LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
              WHERE /*etat-rcs*/
                AND n.NTFXML_DATE_GENERATION >= @debut AND n.NTFXML_DATE_GENERATION < @finExclue /*rcs*/
              UNION ALL
              SELECT 1, j.NTFJUDXML_ID_NTFJUDXML, j.NTFJUDXML_TYPE_NOTIFICATION, pc.PRCLL_NUM_PC,
                     COALESCE(NULLIF(e.Denomination, ''), NULLIF(LTRIM(RTRIM(ni.NINSC_DENOMINATION)), ''),
                              LTRIM(RTRIM(ISNULL(ni.NINSC_NOM_USAGE, '') + ' ' + ISNULL(ni.NINSC_PRENOMS, '')))),
                     COALESCE(s.NumeroGestion, ni.NINSC_NUM_NON_INSCRIT), j.NTFJUDXML_CODE_GREFFE, j.NTFJUDXML_DATE_GENERATION,
                     j.NTFJUDXML_DATE_ENVOI, j.NTFJUDXML_DATE_RECEPTION, j.NTFJUDXML_ETAT_TRAITMENT, t.R_ETATTRAITM_LIBELLE_ECRAN,
                     LEFT(j.NTFJUDXML_ERREUR, 160)
              FROM v2.NOTIFICATION_JUD_XML AS j
              LEFT JOIN rec.DossierRcs AS s ON s.Id = j.NTFJUDXML_ID_DSRCS
              LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
              LEFT JOIN v2.ETAT_TRAITMENT AS t ON t.R_ETATTRAITM_VALEUR = j.NTFJUDXML_ETAT_TRAITMENT
              LEFT JOIN v2.PROCEDURE_COLLECTIVE AS pc ON pc.PRCLL_ID_PRCLL = j.NTFJUDXML_ID_PRCLL
              LEFT JOIN v2.NON_INSCRIT AS ni ON ni.NINSC_ID_NINSC = j.NTFJUDXML_ID_NINSC
              WHERE /*etat-jud*/
                AND j.NTFJUDXML_DATE_GENERATION >= @debut AND j.NTFJUDXML_DATE_GENERATION < @finExclue /*jud*/
              UNION ALL
              SELECT 2, r.DMTNTF_ID_DMTNTF, r.DMTNTF_TYPE_NOTIF, NULL, e.Denomination,
                     ISNULL(s.NumeroGestion, r.DMTNTF_NUM_GESTION), r.DMTNTF_CODE_GREFFE_EMETTEUR, r.DMTNTF_DATE_CREATION,
                     r.DMTNTF_DATE_RECEPTION_AT, r.DMTNTF_DATE_CREATION, r.DMTNTF_ETAT_TRAITEMENT, t.R_ETATDEMATN_LIBELLE_ECRAN, NULL
              FROM v2.DEMAT_NOTIF AS r
              LEFT JOIN v2.DEMANDE AS d ON d.DMND_ID_DMND = r.DMTNTF_ID_DMND
              LEFT JOIN rec.DossierRcs AS s ON s.Id = d.DMND_ID_DSRCS
              LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
              LEFT JOIN v2.ETAT_DEMAT_NOTIF AS t ON t.R_ETATDEMATN_VALEUR = r.DMTNTF_ETAT_TRAITEMENT
              WHERE /*etat-recue*/
                AND r.DMTNTF_DATE_CREATION >= @debut AND r.DMTNTF_DATE_CREATION < @finExclue /*recue*/) AS l
        ORDER BY l.Bloc, l.DateReception, l.Identifiant
        """;

    // Les etats coches a l'ouverture (en tete du fichier).
    private const string EtatsRcs = "n.NTFXML_ETAT_TRAITMENT IN ('2', '5', '8')", EtatsJud = "j.NTFJUDXML_ETAT_TRAITMENT IN ('2', '5', '8')",
                         EtatsRecues = "r.DMTNTF_ETAT_TRAITEMENT IN ('1', '8')";

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Assembler(EtatsRcs, EtatsJud, EtatsRecues, "", "", ""), Ligne, ct,
            new SqlParameter("@plafond", plafond),
            // Du jour moins 30 jours a 0 h au jour a 24 h (en tete du fichier) : « < lendemain 0 h » vaut « <= 23:59:59.998 ».
            new SqlParameter("@debut", System.Data.SqlDbType.DateTime) { Value = aujourdhui.Date.AddDays(-30) },
            new SqlParameter("@finExclue", System.Data.SqlDbType.DateTime) { Value = aujourdhui.Date.AddDays(1) });

    private static string Assembler(string etatRcs, string etatJud, string etatRecue, string rcs, string jud, string recue)
        => Requete.Replace("/*etat-rcs*/", etatRcs, StringComparison.Ordinal).Replace("/*etat-jud*/", etatJud, StringComparison.Ordinal)
                  .Replace("/*etat-recue*/", etatRecue, StringComparison.Ordinal).Replace("/*rcs*/", rcs, StringComparison.Ordinal)
                  .Replace("/*jud*/", jud, StringComparison.Ordinal).Replace("/*recue*/", recue, StringComparison.Ordinal);

    // ------------------------------------------------------------------ LA RECHERCHE (plan « RIG v3 100 % fonctionnel », lot 6)

    internal const string CritereDebut = "Date de début", CritereFin = "Date de fin", CritereEtat = "État", CritereGreffe = "Greffe";

    /// <summary>
    /// Les criteres de la V2 (Commun\OPE_DATES, OPE_ETAT, OPE_GREFFES ; en tete du fichier), saisis a la place de leurs valeurs
    /// d'ouverture : « Date de début » et « Date de fin » (la date filtree de chaque bloc, de 0 h a 24 h), « État » (le libelle de
    /// l'etat, LIKE : il remplace les etats coches a l'ouverture, une notification envoyee ou cloturee se retrouve ainsi),
    /// « Greffe » (le code du greffe destinataire ou emetteur). Non repris : l'origine (alerte) et le service RCS / judiciaire.
    /// Le champ de recherche cherche le numero de la notification, la designation ou le numero de gestion.
    /// </summary>
    public IReadOnlyCollection<string> Criteres { get; } = [CritereDebut, CritereFin, CritereEtat, CritereGreffe];

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var debut = CriteresConsultation.Date(recherche.Critere(CritereDebut)) ?? aujourdhui.Date.AddDays(-30);
        var fin = CriteresConsultation.Date(recherche.Critere(CritereFin)) ?? aujourdhui.Date;
        parametres.Add(new SqlParameter("@debut", System.Data.SqlDbType.DateTime) { Value = debut });
        parametres.Add(new SqlParameter("@finExclue", System.Data.SqlDbType.DateTime) { Value = fin.AddDays(1) });
        string etatRcs = EtatsRcs, etatJud = EtatsJud, etatRecue = EtatsRecues;
        if (recherche.Critere(CritereEtat) is { } etat)
        {
            etatRcs = "t.R_ETATTRAITM_LIBELLE_ECRAN LIKE @etat";
            etatJud = "t.R_ETATTRAITM_LIBELLE_ECRAN LIKE @etat";
            etatRecue = "t.R_ETATDEMATN_LIBELLE_ECRAN LIKE @etat";
            parametres.Add(new SqlParameter("@etat", FormeLigne.Contient(etat)));
        }
        var (rcs, jud, recue) = ("", "", "");
        if (recherche.Critere(CritereGreffe) is { } greffe)
        {
            rcs += " AND n.NTFXML_CODE_GREFFE = @greffe";
            jud += " AND j.NTFJUDXML_CODE_GREFFE = @greffe";
            recue += " AND r.DMTNTF_CODE_GREFFE_EMETTEUR = @greffe";
            parametres.Add(new SqlParameter("@greffe", greffe.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            rcs += " AND (CONVERT(varchar(20), n.NTFXML_ID_NTFXML) = @tn OR e.Denomination LIKE @tl OR s.NumeroGestion = @tn)";
            jud += " AND (CONVERT(varchar(20), j.NTFJUDXML_ID_NTFJUDXML) = @tn OR e.Denomination LIKE @tl OR ni.NINSC_DENOMINATION LIKE @tl " +
                   "OR s.NumeroGestion = @tn OR ni.NINSC_NUM_NON_INSCRIT = @tn OR pc.PRCLL_NUM_PC = @tn)";
            recue += " AND (CONVERT(varchar(20), r.DMTNTF_ID_DMTNTF) = @tn OR e.Denomination LIKE @tl OR ISNULL(s.NumeroGestion, r.DMTNTF_NUM_GESTION) = @tn)";
            parametres.Add(new SqlParameter("@tn", CriteresConsultation.Numero(recherche.Texte)));
            parametres.Add(new SqlParameter("@tl", FormeLigne.Contient(recherche.Texte)));
        }
        return LectureEcran.LireAsync(baseGreffe, Assembler(etatRcs, etatJud, etatRecue, rcs, jud, recue), Ligne, ct, [.. parametres]);
    }

    // ------------------------------------------------------------------ LA FICHE (lot 6)

    internal const string SectionContenu = "Contenu";

    internal const string ChampIdentifiant = "Identifiant", ChampType = "Type", ChampGreffe = "Greffe", ChampEnvoi = "Date d'envoi",
                          ChampReception = "Date de réception", ChampDossier = "Dossier", ChampEtat = "État", ChampMotif = "Motif de rejet";

    // La fiche d'une notification : son bloc (E emise RCS, J emise judiciaire, R recue) et son numero.
    private const string FicheRcs = """
        SELECT n.NTFXML_ID_NTFXML AS Identifiant, n.NTFXML_TYPE_NOTIFICATION AS TypeNotif, n.NTFXML_CODE_GREFFE AS CodeGreffe,
               n.NTFXML_DATE_ENVOI AS DateEnvoi, n.NTFXML_DATE_RECEPTION AS DateReception, s.NumeroGestion AS Dossier,
               e.Denomination AS Designation, COALESCE(t.R_ETATTRAITM_LIBELLE_ECRAN, n.NTFXML_ETAT_TRAITMENT) AS Etat,
               n.NTFXML_LIBELLE_REJET AS Motif
        FROM v2.NOTIFICATION_RCS_XML AS n
        LEFT JOIN rec.ActionRegistre AS a ON a.Id = n.NTFXML_ID_ACTRC
        LEFT JOIN rec.DossierRcs AS s ON s.Id = a.IdDossier
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        LEFT JOIN v2.ETAT_TRAITMENT AS t ON t.R_ETATTRAITM_VALEUR = n.NTFXML_ETAT_TRAITMENT
        WHERE n.NTFXML_ID_NTFXML = @id
        """;

    private const string FicheJud = """
        SELECT j.NTFJUDXML_ID_NTFJUDXML AS Identifiant, j.NTFJUDXML_TYPE_NOTIFICATION AS TypeNotif, j.NTFJUDXML_CODE_GREFFE AS CodeGreffe,
               j.NTFJUDXML_DATE_ENVOI AS DateEnvoi, j.NTFJUDXML_DATE_RECEPTION AS DateReception,
               COALESCE(s.NumeroGestion, ni.NINSC_NUM_NON_INSCRIT) AS Dossier,
               COALESCE(NULLIF(e.Denomination, ''), NULLIF(LTRIM(RTRIM(ni.NINSC_DENOMINATION)), '')) AS Designation,
               COALESCE(t.R_ETATTRAITM_LIBELLE_ECRAN, j.NTFJUDXML_ETAT_TRAITMENT) AS Etat, j.NTFJUDXML_ERREUR AS Motif
        FROM v2.NOTIFICATION_JUD_XML AS j
        LEFT JOIN rec.DossierRcs AS s ON s.Id = j.NTFJUDXML_ID_DSRCS
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        LEFT JOIN v2.NON_INSCRIT AS ni ON ni.NINSC_ID_NINSC = j.NTFJUDXML_ID_NINSC
        LEFT JOIN v2.ETAT_TRAITMENT AS t ON t.R_ETATTRAITM_VALEUR = j.NTFJUDXML_ETAT_TRAITMENT
        WHERE j.NTFJUDXML_ID_NTFJUDXML = @id
        """;

    private const string FicheRecue = """
        SELECT r.DMTNTF_ID_DMTNTF AS Identifiant, r.DMTNTF_TYPE_NOTIF AS TypeNotif, r.DMTNTF_CODE_GREFFE_EMETTEUR AS CodeGreffe,
               NULL AS DateEnvoi, r.DMTNTF_DATE_CREATION AS DateReception, ISNULL(s.NumeroGestion, r.DMTNTF_NUM_GESTION) AS Dossier,
               e.Denomination AS Designation, COALESCE(t.R_ETATDEMATN_LIBELLE_ECRAN, r.DMTNTF_ETAT_TRAITEMENT) AS Etat, NULL AS Motif
        FROM v2.DEMAT_NOTIF AS r
        LEFT JOIN v2.DEMANDE AS d ON d.DMND_ID_DMND = r.DMTNTF_ID_DMND
        LEFT JOIN rec.DossierRcs AS s ON s.Id = d.DMND_ID_DSRCS
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        LEFT JOIN v2.ETAT_DEMAT_NOTIF AS t ON t.R_ETATDEMATN_VALEUR = r.DMTNTF_ETAT_TRAITEMENT
        WHERE r.DMTNTF_ID_DMTNTF = @id
        """;

    private static readonly char[] Lettres = ['E', 'J', 'R'];

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (id.Length < 3 || id[1] != '-' || Array.IndexOf(Lettres, id[0]) is var bloc && bloc < 0
            || CriteresConsultation.Entier(id[2..]) is not { } n) return null;
        await using var r = await RigV3.Serveur.Socle.Greffes.LectureV2.LireAsync(baseGreffe, bloc switch { 0 => FicheRcs, 1 => FicheJud, _ => FicheRecue },
            ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        var designation = FormeLigne.Texte(r, "Designation");
        return new FicheEcran(Procedure, id, FormeLigne.Joindre($"Notification {n}", designation),
        [
            new SectionFiche(SectionContenu,
            [
                new(ChampIdentifiant, FormeLigne.Texte(r, "Identifiant")), new(ChampType, FormeLigne.Texte(r, "TypeNotif")),
                new(ChampGreffe, FormeLigne.Texte(r, "CodeGreffe")),
                new(ChampEnvoi, FormeLigne.DateFiche(FormeLigne.DateDe(r, "DateEnvoi"))),
                new(ChampReception, FormeLigne.DateFiche(FormeLigne.DateDe(r, "DateReception"))),
                new(ChampDossier, FormeLigne.Joindre(FormeLigne.NumeroGestion(FormeLigne.Texte(r, "Dossier")), designation)),
                new(ChampEtat, FormeLigne.Texte(r, "Etat")), new(ChampMotif, FormeLigne.Texte(r, "Motif")),
            ]),
        ]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var bloc = Convert.ToInt32(r["Bloc"]);
        var code = FormeLigne.Texte(r, "CodeEtat");
        var emise = bloc < 2;

        // La regle d'etat (en tete du fichier ; les etats que seule la recherche fait entrer, lot 6, en dessous).
        (string Etat, int[] Faites) regle = (emise, code) switch
        {
            (true, "2" or "3" or "4") or (false, "3" or "4") => ("EnCours", [0]),
            (true, "6" or "10") or (false, "2") => ("Validee", [0]),
            _ => ("AFaire", []),
        };

        var libelle = FormeLigne.Texte(r, "LibelleEtat");
        var greffe = FormeLigne.Texte(r, "CodeGreffe");
        var date = FormeLigne.Date(FormeLigne.DateDe(r, "DateFiltre"));
        var envoi = FormeLigne.Date(FormeLigne.DateDe(r, "DateEnvoi"));
        var numPc = FormeLigne.Texte(r, "NumPc");
        var type = FormeLigne.Texte(r, "TypeNotif");

        var nom = FormeLigne.Joindre($"Notification {FormeLigne.Texte(r, "Identifiant")}", FormeLigne.Texte(r, "Designation"));
        var precisions = FormeLigne.Joindre(
            FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumGestion")),
            numPc.Length > 0 ? $"PC {numPc}" : null,
            type.Length > 0 ? $"type {type}" : null,
            greffe.Length > 0 ? (emise ? $"vers le greffe {greffe}" : $"du greffe {greffe}") : null,
            date.Length > 0 ? (emise ? $"générée le {date}" : $"reçue le {date}") : null,
            emise && envoi.Length > 0 ? $"envoyée le {envoi}" : null,
            libelle.Length > 0 ? libelle : $"état {code}",
            FormeLigne.Texte(r, "Rejet"));
        return new LigneLue(nom, precisions, Groupes[bloc], regle.Etat, regle.Faites)
        {
            Id = $"{Lettres[bloc]}-{FormeLigne.Texte(r, "Identifiant")}",
        };
    }
}
