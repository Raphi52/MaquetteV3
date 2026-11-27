using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Bordereaux LR » (PROC_BORDLR, forme Etat) : les bordereaux de lettres recommandees du greffe, avec ou sans accuse de
/// reception, et -- sur une periode saisie -- le bordereau a editer de chaque type avec son nombre de plis. Mode d'emploi des
/// lecteurs : LecteurEcran.cs ; le geste : EcrivainBordereauxLr.cs (plan « RIG v3 100 % fonctionnel », lot 11, 27/11/2026).
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_BORDLR (proc_bordlr.md, Detail OPE). L'ecran V2 n'a pas
/// de liste : on saisit une periode (dates et heures) et un utilisateur, il COMPTE les plis a mettre au bordereau a chaque saisie
/// (MetierProcBordlr.GetCountDemandeEdition, :86-92, requete _CreeRequete :45-61), une fois par etape (avec AR, sans AR), et
/// VALIDER edite le bordereau (une demande d'edition BORD_LRAR / BORD_LR, EcrivainBordereauxLr.cs).
///
/// SANS PERIODE (LireAsync, ou une recherche par texte seul) -- LES BORDEREAUX DEJA EDITES, deux traces :
///   - les demandes du processus, DEMANDE.DMND_CODE_PROSS = 'BORDLR', sauf les supprimees (etat X, Demande.eEtatDemande,
///     Outils\DLL\RigMetier\Demande\Demande.cs:21-42), avec leur edition (DMNED_ID_DMND : GenererEdition recoit
///     Processus.demande.DMND_ID_DMND_DEMANDE, OPE_BORDLR_AVEC_AR.cs:68, OPE_BORDLR_SANS_AR.cs:63) -- une ligne par edition, une
///     demande sans edition fait une ligne. 27 dans RIG_DEV, toutes validees, de 2010 a 2019 (lu le 25/11/2026) ;
///   - les editions « Bordereau LRAR-n » / « Bordereau LR-n » sans demande : celles que RIG v3 edite (la demande du processus,
///     creee et validee d'un coup, sans facture, n'est qu'une trace : le journal des gestes la remplace, decision du plan). Le
///     libelle « -n » est celui de CET ecran (« Bordereau LRAR- » + NumOrdre, OPE_BORDLR_AVEC_AR.cs:68) : les 15 783 editions
///     « bordereau LRAR » / « bordereau LR » de RIG_DEV sans demande ni « -n » sont les bordereaux des lots de recommandes
///     imprimes, pas ceux de cet ecran ; elles ne sont pas lues ici (la « File des editions », LecteurFileEditions.cs, les lit).
///   Ordre : la validation (sinon la creation) la plus recente en tete, puis la demande, puis l'edition.
///   Groupe (le libelle de l'edition, toute casse) : « Bordereau LR-n » -> « Sans accuse de reception », tout autre -> « Avec
///   accuse de reception » ; une demande sans edition ne dit pas son type : « Avec accuse de reception », le bordereau que la V2
///   propose toujours (l'etape sans AR est masquee des que la numerotation LRN a demarre, PROC_BORDLR.cs:74-79).
///   Etat : demande non validee (Q, I, W...) -> AFaire « A editer » ; validee ou edition V3, edition emise (phase E) -> Validee
///   « Edite » ; edition pas encore emise, ou demande validee sans edition -> EnCours « En cours ».
///   Le nombre de plis d'un bordereau edite n'est pas lu : RIG v2 ne le garde que dans la requete de l'edition (le XML
///   DMNED_PARAMETRE_GENERATION, SQL), que le service d'edition execute ; la cellule reste vide.
///
/// AVEC UNE PERIODE (ChercherAsync, « Periode » remplie) -- LE BORDEREAU A EDITER, une ligne par type, comme les deux etapes de la
/// V2 : le nombre de demandes d'edition DEMANDE_EDITION INNER JOIN LRAR_INFOS (DMNED_NUM_LRAR = LRARI_NUM_LRAR) ou
///   LRARI_TYPE = 'LRAR' (avec AR) ou 'LR' (sans AR) -- la constante _lrarType de chaque OPE (OPE_BORDLR_AVEC_AR.cs:21,
///     OPE_BORDLR_SANS_AR.cs:20) ;
///   DMNED_DATE_PRE_EMISSION entre le debut et la fin : sans heure, de 00:00 (ToSQL00Heure) a 23:59:59.998 (ToSQL24Heure,
///     RigTools\SQL\DBObject.cs:541-546), sinon a l'heure « hh:mm » saisie (AdapteDate, MetierProcBordlr.cs:130-137) ; une fin
///     vide prend la date de debut (ult_date_debut_ChangementValeur, OPE_BORDLR_AVEC_AR.cs:82) ;
///   DMNED_UTILISATEUR IS NOT NULL (tous), ou = l'utilisateur saisi (le getter User, OPE_BORDLR_AVEC_AR.cs:45-56).
/// Etat AFaire « A editer », groupe du type. Une debut apres la fin ne compte rien (la V2 refuse et efface la fin, « La date de
/// debut doit etre plus petit que la date de fin. », OPE_BORDLR_AVEC_AR.cs:87) : la ligne le dit. Sans AR une fois la
/// numerotation LRN demarree (NUMERO_METIER LRN, CHRONO_DEBUT = 1 : num.Compteur.Debut dans la base V3, PROC_BORDLR.cs:74-78),
/// l'etape passe en consultation : la ligne le dit et le geste la refuse. La ligne porte l'objet du geste dans son Id (le type, les
/// deux bornes, l'utilisateur) : l'ecran d'un Etat n'envoie pas ses parametres au geste, la ligne si.
/// NON REPRIS : la case « Mes recommandes » (l'utilisateur courant, _me.UTLST_LOGIN) -- la lecture ne connait pas l'agent ; son
/// identifiant se saisit dans « Utilisateur ».
/// </summary>
internal sealed class LecteurBordereauxLr : IRechercheEcran
{
    public string Procedure => "PROC_BORDLR";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string AvecAr = "Avec accusé de réception", SansAr = "Sans accusé de réception";

    /// <summary>Les parametres du descripteur, au mot pres (les criteres que l'Etat envoie a la base).</summary>
    internal const string CriterePeriode = "Période", CritereHeureDebut = "Heure de début", CritereHeureFin = "Heure de fin",
                          CritereUtilisateur = "Utilisateur";

    /// <summary>Le message de RIG v2 sur une periode a l'envers (OPE_BORDLR_AVEC_AR.cs:87, OPE_BORDLR_SANS_AR.cs:83).</summary>
    internal const string PeriodeInversee = "La date de début doit être plus petit que la date de fin.";

    /// <summary>Le bordereau sans AR quand la numerotation LRN a demarre (PROC_BORDLR.cs:74-78, ETP_BORDLR_SANS_AR.cs:28-33).</summary>
    internal const string SansArEnConsultation = "consultation seulement : la numérotation LRN a démarré, RIG v2 masque la saisie du bordereau sans AR";

    public IReadOnlyCollection<string> Criteres { get; } = [CriterePeriode, CritereHeureDebut, CritereHeureFin, CritereUtilisateur];

    // Les bordereaux deja edites (en tete du fichier) : les demandes BORDLR avec leur edition, puis les editions V3 sans demande.
    private const string RequeteEdites = """
        SELECT TOP (@plafond) b.*, COUNT(*) OVER () AS Total
        FROM (
            SELECT d.DMND_ID_DMND, d.DMND_NUM_DEMANDE, d.DMND_ETAT_DEMANDE, d.DMND_NOM_UTILISATEUR,
                   de.DMNED_ID_DMNED, de.DMNED_LIBELLE_DOCUMENT, de.DMNED_PHASE_TRAITEMENT,
                   CASE WHEN d.DMND_ETAT_DEMANDE = 'V' THEN COALESCE(d.DMND_DATE_VALIDATION, d.DMND_DATE_CREATION) ELSE d.DMND_DATE_CREATION END AS Quand,
                   COALESCE(d.DMND_DATE_VALIDATION, d.DMND_DATE_CREATION) AS Ordre
            FROM v2.DEMANDE AS d
            LEFT JOIN v2.DEMANDE_EDITION AS de ON de.DMNED_ID_DMND = d.DMND_ID_DMND
            WHERE d.DMND_CODE_PROSS = 'BORDLR' AND d.DMND_ETAT_DEMANDE <> 'X'
            UNION ALL
            SELECT NULL, NULL, NULL, de.DMNED_UTILISATEUR, de.DMNED_ID_DMNED, de.DMNED_LIBELLE_DOCUMENT, de.DMNED_PHASE_TRAITEMENT,
                   de.DMNED_DATE_CREATION, de.DMNED_DATE_CREATION
            FROM v2.DEMANDE_EDITION AS de
            WHERE de.DMNED_ID_DMND IS NULL
              AND (de.DMNED_LIBELLE_DOCUMENT LIKE 'bordereau LRAR-%' OR de.DMNED_LIBELLE_DOCUMENT LIKE 'bordereau LR-%')
        ) AS b
        WHERE 1 = 1 /*texte*/
        ORDER BY b.Ordre DESC, b.DMND_ID_DMND DESC, b.DMNED_ID_DMNED
        """;

    /// <summary>Le nombre de plis d'un bordereau a editer (MetierProcBordlr._CreeRequete, :45-61), parametre.</summary>
    internal const string RequetePlis = """
        SELECT COUNT(*)
        FROM v2.DEMANDE_EDITION AS de
        INNER JOIN v2.LRAR_INFOS AS l ON de.DMNED_NUM_LRAR = l.LRARI_NUM_LRAR
        WHERE l.LRARI_TYPE = @type AND de.DMNED_DATE_PRE_EMISSION >= @du AND de.DMNED_DATE_PRE_EMISSION <= @au
          AND de.DMNED_UTILISATEUR IS NOT NULL AND (@utilisateur IS NULL OR de.DMNED_UTILISATEUR = @utilisateur)
        """;

    /// <summary>« 1 » quand la numerotation LRN a demarre (NMMTR_CHRONO_DEBUT = 1, PROC_BORDLR.cs:74-77 ; num.Compteur.Debut,
    /// NUM-001) : le compteur LRN le plus recent.</summary>
    internal const string RequeteLrn = "SELECT TOP 1 Debut FROM num.Compteur WHERE Type = 'LRN' ORDER BY Annee DESC, Jour DESC";

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, RequeteEdites.Replace("/*texte*/", "", StringComparison.Ordinal), Ligne, ct,
                                  new SqlParameter("@plafond", plafond));

    public async Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        if (recherche.Critere(CriterePeriode) is not { } periode)
        {
            // Sans periode : les bordereaux edites, cherches par leur nom, leur utilisateur ou leur numero de demande.
            if (string.IsNullOrWhiteSpace(recherche.Texte)) return await LireAsync(baseGreffe, aujourdhui, plafond, ct);
            return await LectureEcran.LireAsync(baseGreffe,
                RequeteEdites.Replace("/*texte*/", "AND (b.DMNED_LIBELLE_DOCUMENT LIKE @texte OR b.DMND_NUM_DEMANDE LIKE @texte OR b.DMND_NOM_UTILISATEUR LIKE @texte)",
                                      StringComparison.Ordinal),
                Ligne, ct, new SqlParameter("@plafond", plafond), new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        var aEditer = AEditer.Lire(periode, recherche.Critere(CritereHeureDebut), recherche.Critere(CritereHeureFin), recherche.Critere(CritereUtilisateur));
        if (aEditer is null) return new LignesLues([], 0);
        var lrn = await LrnDemarreeAsync(baseGreffe, null, ct);
        var lignes = new List<LigneLue>();
        foreach (var type in new[] { AEditer.Lrar, AEditer.Lr })
        {
            var objet = aEditer with { Type = type };
            var plis = objet.Inversee ? 0 : await CompterAsync(baseGreffe, null, objet, ct);
            lignes.Add(LigneAEditer(objet, plis, type == AEditer.Lr && lrn));
        }
        return new LignesLues(lignes, lignes.Count);
    }

    /// <summary>Vrai si la numerotation LRN a demarre (en tete du fichier) ; une base sans compteur LRN : non.</summary>
    internal static async Task<bool> LrnDemarreeAsync(SqlConnection baseGreffe, SqlTransaction? transaction, CancellationToken ct)
    {
        LectureV2.ExigerLecture(RequeteLrn);
        await using var cmd = new SqlCommand(RequeteLrn, baseGreffe, transaction) { CommandTimeout = 120 };
        return await cmd.ExecuteScalarAsync(ct) is int debut && debut == 1;
    }

    /// <summary>Le nombre de plis du bordereau <paramref name="objet"/> (RequetePlis).</summary>
    internal static async Task<int> CompterAsync(SqlConnection baseGreffe, SqlTransaction? transaction, AEditer objet, CancellationToken ct)
    {
        LectureV2.ExigerLecture(RequetePlis);
        await using var cmd = new SqlCommand(RequetePlis, baseGreffe, transaction) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@type", objet.Type);
        cmd.Parameters.AddWithValue("@du", objet.Du);
        cmd.Parameters.AddWithValue("@au", objet.Au);
        cmd.Parameters.AddWithValue("@utilisateur", (object?)objet.Utilisateur ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    /// <summary>La ligne du bordereau a editer (en tete du fichier).</summary>
    internal static LigneLue LigneAEditer(AEditer objet, int plis, bool enConsultation)
    {
        var sansAr = objet.Type == AEditer.Lr;
        var periode = $"du {FormeLigne.Date(objet.Du)}{Heure(objet.Du)} au {FormeLigne.Date(objet.Au)}{Heure(objet.Au)}";
        var qui = objet.Utilisateur ?? "tous les utilisateurs";
        var precisions = FormeLigne.Joindre(objet.Inversee ? PeriodeInversee : periode,
            plis > 1 ? $"{plis} plis" : $"{plis} pli", qui, enConsultation ? SansArEnConsultation : null);
        var nom = $"{(sansAr ? "Bordereau LR" : "Bordereau LRAR")} à éditer";
        return new LigneLue(nom, precisions, sansAr ? SansAr : AvecAr, "AFaire", [])
        {
            Id = objet.Id,
            Valeurs = [nom, objet.Inversee ? PeriodeInversee : periode, qui, plis.ToString(CultureInfo.InvariantCulture),
                       enConsultation ? "consultation seulement" : "à éditer"],
        };
    }

    // « 14:30 » saisie : « à 14 h 30 » ; une borne de journee entiere (00:00 ou 23:59:59.998) : rien.
    private static string Heure(DateTime d)
        => d.TimeOfDay == TimeSpan.Zero || d.TimeOfDay >= new TimeSpan(0, 23, 59, 59, 0) ? "" : $" à {d:HH} h {d:mm}";

    private static LigneLue Ligne(SqlDataReader r)
    {
        var demande = r["DMND_ID_DMND"] is not DBNull;
        var validee = !demande || FormeLigne.Texte(r, "DMND_ETAT_DEMANDE") == "V";
        var edition = r["DMNED_ID_DMNED"] is not DBNull;
        var libelle = FormeLigne.Texte(r, "DMNED_LIBELLE_DOCUMENT");
        var phase = FormeLigne.Texte(r, "DMNED_PHASE_TRAITEMENT");
        var sansAr = SansAccuse(libelle);
        var etat = Etat(validee, edition, phase);
        var quand = FormeLigne.DateDe(r, "Quand");
        var utilisateur = Utilisateur(FormeLigne.Texte(r, "DMND_NOM_UTILISATEUR"));
        var etatEdition = !edition ? (validee ? "aucune édition retrouvée, avec ou sans AR inconnu" : "à valider")
                                   : phase == "E" ? "édité" : $"édition en file (phase « {phase} » de l'ancien RIG)";
        var precisions = FormeLigne.Joindre(
            quand is null ? null : $"{(demande ? validee ? "validé" : "commencé" : "créé")} le {FormeLigne.Date(quand)}",
            utilisateur.Length > 0 ? $"par {utilisateur}" : null,
            edition ? FormeLigne.Joindre(demande ? libelle : null, etatEdition) : etatEdition);
        var nom = demande ? $"Bordereau {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}" : libelle;
        var id = demande ? $"D-{FormeLigne.Texte(r, "DMND_ID_DMND")}" : $"E-{FormeLigne.Texte(r, "DMNED_ID_DMNED")}";
        return new LigneLue(nom, precisions, sansAr ? SansAr : AvecAr, etat, [])
        {
            Id = id,
            Valeurs = [nom, "", utilisateur, "", etatEdition],
        };
    }

    /// <summary>Un bordereau sans AR : libelle « Bordereau LR-n » (OPE_BORDLR_SANS_AR.cs:63), en toute casse.</summary>
    internal static bool SansAccuse(string libelle)
        => libelle.StartsWith("bordereau LR-", StringComparison.OrdinalIgnoreCase);

    /// <summary>L'utilisateur de DMND_NOM_UTILISATEUR, ecrit « poste/utilisateur[/MOTEUR] » par la V2 (27 demandes BORDLR
    /// sur 27 dans RIG_DEV) : le second morceau ; une autre forme (le compte d'une edition V3), telle quelle.</summary>
    internal static string Utilisateur(string nom)
    {
        var morceaux = nom.Split('/');
        return morceaux.Length >= 2 && morceaux[1].Trim().Length > 0 ? morceaux[1].Trim() : nom;
    }

    /// <summary>La regle d'etat d'un bordereau edite (en tete du fichier).</summary>
    internal static string Etat(bool validee, bool edition, string phase)
        => !validee ? "AFaire" : edition && phase == "E" ? "Validee" : "EnCours";
}

/// <summary>
/// L'objet d'un bordereau a editer : son type (LRAR, LR), ses bornes et son utilisateur (null : tous). Il voyage dans l'Id de sa
/// ligne (« A|LRAR|20260302000000000|20260302235959997|GESA ») jusqu'au geste, qui le recompte (LecteurBordereauxLr.cs, en tete).
/// </summary>
internal sealed record AEditer(string Type, DateTime Du, DateTime Au, string? Utilisateur)
{
    internal const string Lrar = "LRAR", Lr = "LR";
    private const string Format = "yyyyMMddHHmmssfff";

    /// <summary>Vrai : le debut est apres la fin (la V2 refuse la saisie, OPE_BORDLR_AVEC_AR.cs:87).</summary>
    internal bool Inversee => Du > Au;

    internal string Id => string.Join('|', "A", Type, Du.ToString(Format, CultureInfo.InvariantCulture), Au.ToString(Format, CultureInfo.InvariantCulture), Utilisateur ?? "");

    /// <summary>L'objet d'une ligne « a editer » ; null pour toute autre ligne (un bordereau deja edite).</summary>
    internal static AEditer? DeId(string id)
    {
        var p = id.Split('|', 5);
        if (p.Length != 5 || p[0] != "A" || p[1] is not (Lrar or Lr)) return null;
        if (!DateTime.TryParseExact(p[2], Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var du)
            || !DateTime.TryParseExact(p[3], Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var au)) return null;
        return new AEditer(p[1], du, au, p[4].Length == 0 ? null : p[4]);
    }

    /// <summary>
    /// Les bornes saisies (MetierProcBordlr._CreeRequete, :45-61) : la periode « du|au » de l'ecran, une fin vide = le debut
    /// (OPE_BORDLR_AVEC_AR.cs:82) ; sans heure « hh:mm », 00:00 et 23:59:59.998 (ToSQL00Heure / ToSQL24Heure). Null sans debut.
    /// Le type est pose ensuite (une ligne par type).
    /// </summary>
    internal static AEditer? Lire(string periode, string? heureDebut, string? heureFin, string? utilisateur)
    {
        var bornes = periode.Split('|');
        var du = CriteresConsultation.Date(bornes[0]);
        var au = bornes.Length > 1 ? CriteresConsultation.Date(bornes[1]) : null;
        if (du is null && au is null) return null;
        var jourDebut = du ?? au!.Value;
        var jourFin = au ?? jourDebut;
        var debut = Heure(heureDebut) is { } h1 ? jourDebut.Add(h1) : jourDebut;
        var fin = Heure(heureFin) is { } h2 ? jourFin.Add(h2) : jourFin.AddDays(1).AddMilliseconds(-2);
        return new AEditer(Lrar, debut, fin, string.IsNullOrWhiteSpace(utilisateur) ? null : utilisateur.Trim());
    }

    // TimeSpan.TryParseExact(heure, "hh\\:mm") de la V2 (AdapteDate, MetierProcBordlr.cs:130-137).
    private static TimeSpan? Heure(string? texte)
        => TimeSpan.TryParseExact(texte?.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out var h) ? h : null;
}
