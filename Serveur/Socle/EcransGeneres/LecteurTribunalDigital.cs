using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Tribunal digital » (PROC_VTRID) : les documents envoyes par le greffe au tribunal digital, et les demandes recues du
/// portail. Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_VTRID, fiche Brain proc_vtrid.md, sur la base du
/// greffe. Deux onglets, chacun avec sa recherche ; la V2 ouvre la grille vide (_Reinitialiser -> _EffacerResultats,
/// Tab Documents transmis\OPE_RECHERCHE_DOCUMENTS_TRANSMIS.cs:71-82 et 234-253 ; Tab Demandes recues\
/// OPE_RECHERCHE_DEMANDES_RECUES.cs:45-46 et 76-89) et attend le clic sur « Rechercher », ses criteres pre-remplis.
/// L'ecran V3 lit la liste a ces criteres pre-remplis, comme LecteurRenvoisRpjc et LecteurTraitementRpjc :
///   - « Documents transmis » (onglet par defaut) : critere « DATE » (l. 30 et 249), du jour moins 7 jours a 0 h au jour
///     a 24 h sur TDE_DATE_ENVOI (l. 235-236 ; DataGridViewDocTransmis.cs:210-219), au plus 200 (l. 246).
///     TRIBUNAL_DIGITAL_ENVOI JOIN DEMANDE_EDITION_TRIBUNAL_DIGITAL (DETD_ID_TDE) JOIN DEMANDE_EDITION (DETD_ID_DMNED),
///     la plus recente creee en tete (DataGridViewDocTransmis.cs:187-206). La V2 rend une ligne par couple envoi x edition
///     mais les affiche toutes avec la PREMIERE edition de l'envoi (ListDemandeEditionTribunalDigital.FirstOrDefault(),
///     l. 117-125 ; chargee sans ORDER BY, DemandeEditionTribunalDigital.cs:99-103, donc dans l'ordre de la cle) : un envoi
///     groupe y apparait en lignes identiques. Ici, une ligne par envoi, avec sa premiere edition (DETD_ID_DETD le plus
///     petit), triee sur la date de creation de celle-ci ;
///   - « Demandes reçues » : case « En attente » seule cochee (OPE_RECHERCHE_DEMANDES_RECUES.cs:79-81), aucun type de
///     formalite, aucune date (l. 77 et OPE_DATES_DEMANDES_RECUES.cs:19-23), au plus 200 (l. 82) : DEMAT_TRIBUNAL_DIGITAL_CREFO
///     ou DTDCREFO_ETAT_TRAITEMENT_RIG IS NULL, la plus recente recue en tete (DataGridViewDemandesRecus.cs:269-338).
/// Tables V2 telles quelles (v2.*). Le message de la V2 sur les editions pas encore envoyees (DETD_ID_TDE NULL,
/// OPE_RECHERCHE_DOCUMENTS_TRANSMIS.cs « NbNonTraites ») est un compteur, pas une liste : pas de ligne.
///
/// GROUPES : l'onglet -- « Documents transmis », « Demandes reçues ».
///
/// REGLE D'ETAT (sections du descripteur : 0 Documents transmis, 1 Demandes recues, 2 Preuve et dossier -- la recherche
/// « Criteres » de la V2 est le panneau des criteres depuis le lot 1 du plan-676a4b19, qui l'a retiree des sections).
/// TDE_ETAT suit le flux EDI TRID : 2 pret a envoyer (Edi\...\EdiFluxTRID\TRIDEdiSortantDETD.cs:85), 3 envoye
/// (TRIDEdiSortant.cs:136), 8 erreur d'envoi (:138), 4 compte rendu OK et 9 compte rendu KO, son erreur dans
/// TDE_ERREUR_SES (TRIDEdiEntrantCR.cs:223-241) ; la date du compte rendu est TDE_DATE_CR.
///   envoi avec une erreur (TDE_ERREUR_SES), ou a l'etat 8 ou 9
///                                -> AFaire « Non traité » : la V2 l'ecrit en rouge (DataGridViewDocTransmis.cs:129-131) ;
///   envoi sans compte rendu      -> EnCours « En cours » : envoye, le compte rendu attendu, en orange (l. 136-138) (0) ;
///   compte rendu OK (etat 4)     -> Validee « Traité » : la preuve de depot se telecharge (IsCompteRenduOk,
///                                   RigMetier\JUDICIAIRE\TribunalDigitalEnvoi.cs:14 ; OPE_RESULTAT_DOCUMENTS_TRANSMIS) (0, 2) ;
///   autre compte rendu           -> Validee « Traité », sans la preuve (0) ;
///   demande recue (en attente)   -> AFaire « Non traité » : pas encore acceptee ni refusee dans RIG, en orange
///                                   (DataGridViewDemandesRecus.cs:232-234). Les acceptees et refusees, decochees a
///                                   l'ouverture, n'entrent pas dans la liste.
/// </summary>
// fix-ok: la cause des editions de ce fichier par le lot 6 (plan-676a4b19) -- la liste ne se cherchait pas et ne s'ouvrait pas
// (ILecteurEcran seul) ; le filtre des demandes en attente et les filtres de chaque onglet sont devenus des marques remplacees,
// pour que la recherche les remplace sans changer la liste d'ouverture (jeu PROC_VTRID.attendu inchange).
internal sealed class LecteurTribunalDigital : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_VTRID";

    /// <summary>Les groupes du descripteur, au mot pres, par bloc de la requete (0, 1).</summary>
    internal static readonly string[] Groupes = ["Documents transmis", "Demandes reçues"];

    /// <summary>Les types de formalite du portail (TypeFormalite, Outils\DLL\RigMetier\JUDICIAIRE\EDI\XSD\TriDig\
    /// xsd_enum.cs:110-132), libelles de la V2 (Utils.ConvertiDescriptionFormalite).</summary>
    internal static readonly IReadOnlyDictionary<string, string> Formalites = new Dictionary<string, string>
    {
        ["TD_ASSIGNATION"] = "Assignation",
        ["TD_OPP_INJ_PAYER"] = "Opposition en injonction de payer",
        ["TD_OUV_PROC_COLL"] = "Ouverture d'une procédure collective",
        ["TD_RQT_INJ_PAYER"] = "Requête en injonction de payer",
        ["TD_RQT_JUGE_COMM"] = "Requête au juge commissaire",
        ["TD_RQT_PRESIDENT"] = "Requête au président du tribunal",
        ["TD_TRANS_DOCUMENT"] = "Transmission de document",
    };

    // Bloc : 0 documents transmis, 1 demandes recues (l'ordre d'affichage, et le groupe de la ligne).
    private const string Requete = """
        SELECT TOP (@plafond) l.*, COUNT(*) OVER () AS Total
        FROM (SELECT 0 AS Bloc, t.TDE_ID_TDE AS Identifiant, t.TDE_NUMEROS_ENVOI AS Numero, t.TDE_NUM_AFFAIRE AS Affaire,
                     t.TDE_DATE_ENVOI AS DateEnvoi, t.TDE_DATE_SES_AR AS DateAccuse, t.TDE_DATE_CR AS DateCompteRendu,
                     t.TDE_ETAT AS Etat, LEFT(t.TDE_ERREUR_SES, 160) AS Erreur, p.DMNED_LIBELLE_DOCUMENT AS Document,
                     p.DMNED_DESIGNATION_DESTINATAIRE AS Destinataire, p.DMNED_DATE_CREATION AS DateTri,
                     CAST(NULL AS varchar(20)) AS Formalite
              FROM v2.TRIBUNAL_DIGITAL_ENVOI AS t
              CROSS APPLY (SELECT TOP (1) de.DMNED_LIBELLE_DOCUMENT, de.DMNED_DESIGNATION_DESTINATAIRE, de.DMNED_DATE_CREATION
                           FROM v2.DEMANDE_EDITION_TRIBUNAL_DIGITAL AS x
                           JOIN v2.DEMANDE_EDITION AS de ON de.DMNED_ID_DMNED = x.DETD_ID_DMNED
                           WHERE x.DETD_ID_TDE = t.TDE_ID_TDE
                           ORDER BY x.DETD_ID_DETD) AS p
              WHERE t.TDE_DATE_ENVOI >= @debut AND t.TDE_DATE_ENVOI < @finExclue /*envoi*/
              UNION ALL
              SELECT 1, c.DTDCREFO_ID_DTDCREFO, c.DTDCREFO_NUM_CREFO, c.DTDCREFO_NUM_AFFAIRE, NULL, NULL, NULL, c.DTDCREFO_ETAT_TRAITEMENT_RIG,
                     LEFT(c.DTDCREFO_ERREUR_SES, 160), NULL, NULL, c.DTDCREFO_DATE_CREATION, c.DTDCREFO_TYPE_FORMALITE
              FROM v2.DEMAT_TRIBUNAL_DIGITAL_CREFO AS c
              WHERE /*etat-demande*/ /*demande*/) AS l
        ORDER BY l.Bloc, l.DateTri DESC, l.Identifiant DESC
        """;

    // Les demandes recues en attente, seules cochees a l'ouverture (en tete du fichier).
    private const string EnAttente = "c.DTDCREFO_ETAT_TRAITEMENT_RIG IS NULL";

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Assembler(EnAttente, "", ""), Ligne, ct,
            new SqlParameter("@plafond", plafond),
            // Du jour moins 7 jours a 0 h au jour a 24 h (en tete du fichier) : « < lendemain 0 h » vaut « <= 23:59:59.998 ».
            new SqlParameter("@debut", System.Data.SqlDbType.DateTime) { Value = aujourdhui.Date.AddDays(-7) },
            new SqlParameter("@finExclue", System.Data.SqlDbType.DateTime) { Value = aujourdhui.Date.AddDays(1) });

    private static string Assembler(string etatDemande, string envoi, string demande)
        => Requete.Replace("/*etat-demande*/", etatDemande, StringComparison.Ordinal).Replace("/*envoi*/", envoi, StringComparison.Ordinal)
                  .Replace("/*demande*/", demande, StringComparison.Ordinal);

    // ------------------------------------------------------------------ LA RECHERCHE (plan « RIG v3 100 % fonctionnel », lot 6)

    internal const string CritereDebut = "Date de début", CritereFin = "Date de fin", CritereRole = "Numéro de rôle",
                          CritereTraitees = "Demandes traitées";

    /// <summary>
    /// Les criteres de chaque onglet de la V2, saisis a la place de leur valeur d'ouverture : « Date de début » / « Date de fin »
    /// (la date d'envoi des documents transmis, critere DATE, OPE_RECHERCHE_DOCUMENTS_TRANSMIS.cs:235-236 ; la date de reception
    /// des demandes, OPE_DATES_DEMANDES_RECUES), « Numéro de rôle » (le numero d'affaire, LIKE), « Demandes traitées » (oui : les
    /// demandes acceptees ou refusees entrent aussi, les cases decochees a l'ouverture, OPE_RECHERCHE_DEMANDES_RECUES.cs:79-81).
    /// Le champ de recherche cherche le numero d'envoi ou de demande, le role ou le libelle du document.
    /// </summary>
    public IReadOnlyCollection<string> Criteres { get; } = [CritereDebut, CritereFin, CritereRole, CritereTraitees];

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var debut = CriteresConsultation.Date(recherche.Critere(CritereDebut));
        var fin = CriteresConsultation.Date(recherche.Critere(CritereFin));
        parametres.Add(new SqlParameter("@debut", System.Data.SqlDbType.DateTime) { Value = debut ?? aujourdhui.Date.AddDays(-7) });
        parametres.Add(new SqlParameter("@finExclue", System.Data.SqlDbType.DateTime) { Value = (fin ?? aujourdhui.Date).AddDays(1) });
        var etat = CriteresConsultation.OuiNon(recherche.Critere(CritereTraitees)) == true ? "1 = 1" : EnAttente;
        var (envoi, demande) = ("", "");
        // Les demandes n'ont pas de date a l'ouverture ; une date saisie les borne sur leur reception.
        if (debut is not null) demande += " AND c.DTDCREFO_DATE_CREATION >= @debut";
        if (fin is not null) demande += " AND c.DTDCREFO_DATE_CREATION < @finExclue";
        if (recherche.Critere(CritereRole) is { } role)
        {
            envoi += " AND REPLACE(t.TDE_NUM_AFFAIRE, ' ', '') LIKE @role";
            demande += " AND REPLACE(c.DTDCREFO_NUM_AFFAIRE, ' ', '') LIKE @role";
            parametres.Add(new SqlParameter("@role", FormeLigne.Contient(CriteresConsultation.Numero(role))));
        }
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            envoi += " AND (t.TDE_NUMEROS_ENVOI LIKE @texte OR t.TDE_NUM_AFFAIRE LIKE @texte OR p.DMNED_LIBELLE_DOCUMENT LIKE @texte)";
            demande += " AND (c.DTDCREFO_NUM_CREFO LIKE @texte OR c.DTDCREFO_NUM_AFFAIRE LIKE @texte)";
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        return LectureEcran.LireAsync(baseGreffe, Assembler(etat, envoi, demande), Ligne, ct, [.. parametres]);
    }

    // ------------------------------------------------------------------ LA FICHE (lot 6)

    internal const string SectionEnvoi = "Documents transmis", SectionDemande = "Demandes reçues";

    internal const string ChampNumeroEnvoi = "Numéro de l'envoi", ChampRole = "Numéro de rôle", ChampDocument = "Document",
                          ChampDestinataire = "Destinataire", ChampDateEnvoi = "Date d'envoi", ChampCompteRendu = "Compte rendu d'envoi",
                          ChampDemande = "Demande", ChampFormalite = "Type de formalité", ChampRecue = "Reçue le", ChampErreur = "Erreur";

    private const string FicheEnvoi = """
        SELECT t.TDE_ID_TDE, t.TDE_NUMEROS_ENVOI, t.TDE_NUM_AFFAIRE, t.TDE_DATE_ENVOI, t.TDE_DATE_CR, t.TDE_ETAT, t.TDE_ERREUR_SES,
               p.DMNED_LIBELLE_DOCUMENT, p.DMNED_DESIGNATION_DESTINATAIRE
        FROM v2.TRIBUNAL_DIGITAL_ENVOI AS t
        OUTER APPLY (SELECT TOP (1) de.DMNED_LIBELLE_DOCUMENT, de.DMNED_DESIGNATION_DESTINATAIRE
                     FROM v2.DEMANDE_EDITION_TRIBUNAL_DIGITAL AS x
                     JOIN v2.DEMANDE_EDITION AS de ON de.DMNED_ID_DMNED = x.DETD_ID_DMNED
                     WHERE x.DETD_ID_TDE = t.TDE_ID_TDE ORDER BY x.DETD_ID_DETD) AS p
        WHERE t.TDE_ID_TDE = @id
        """;

    private const string FicheDemande = """
        SELECT c.DTDCREFO_NUM_CREFO, c.DTDCREFO_NUM_AFFAIRE, c.DTDCREFO_TYPE_FORMALITE, c.DTDCREFO_DATE_CREATION, c.DTDCREFO_ERREUR_SES
        FROM v2.DEMAT_TRIBUNAL_DIGITAL_CREFO AS c WHERE c.DTDCREFO_ID_DTDCREFO = @id
        """;

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (id.Length < 3 || id[1] != '-' || id[0] is not ('T' or 'C') || CriteresConsultation.Entier(id[2..]) is not { } n) return null;
        var envoi = id[0] == 'T';
        await using var r = await RigV3.Serveur.Socle.Greffes.LectureV2.LireAsync(baseGreffe, envoi ? FicheEnvoi : FicheDemande, ct,
            new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        if (envoi)
        {
            var numero = FormeLigne.Texte(r, "TDE_NUMEROS_ENVOI");
            var compteRendu = FormeLigne.Joindre(
                FormeLigne.DateDe(r, "TDE_DATE_CR") is { } cr ? $"le {FormeLigne.Date(cr)}" : "en attente",
                FormeLigne.Texte(r, "TDE_ETAT") == "4" ? "OK" : null, PremiereLigne(FormeLigne.Texte(r, "TDE_ERREUR_SES")));
            return new FicheEcran(Procedure, id, numero.Length > 0 ? $"Envoi {numero}" : $"Envoi n° {n}",
            [
                new SectionFiche(SectionEnvoi,
                [
                    new(ChampNumeroEnvoi, numero), new(ChampRole, FormeLigne.Texte(r, "TDE_NUM_AFFAIRE")),
                    new(ChampDocument, FormeLigne.Texte(r, "DMNED_LIBELLE_DOCUMENT")),
                    new(ChampDestinataire, FormeLigne.Texte(r, "DMNED_DESIGNATION_DESTINATAIRE")),
                    new(ChampDateEnvoi, FormeLigne.DateFiche(FormeLigne.DateDe(r, "TDE_DATE_ENVOI"))), new(ChampCompteRendu, compteRendu),
                ]),
            ]);
        }
        var formalite = FormeLigne.Texte(r, "DTDCREFO_TYPE_FORMALITE");
        return new FicheEcran(Procedure, id, $"Demande {FormeLigne.Texte(r, "DTDCREFO_NUM_CREFO")}",
        [
            new SectionFiche(SectionDemande,
            [
                new(ChampDemande, FormeLigne.Texte(r, "DTDCREFO_NUM_CREFO")), new(ChampRole, FormeLigne.Texte(r, "DTDCREFO_NUM_AFFAIRE")),
                new(ChampFormalite, Formalites.TryGetValue(formalite, out var libelle) ? libelle : formalite),
                new(ChampRecue, FormeLigne.DateFiche(FormeLigne.DateDe(r, "DTDCREFO_DATE_CREATION"))),
                new(ChampErreur, PremiereLigne(FormeLigne.Texte(r, "DTDCREFO_ERREUR_SES"))),
            ]),
        ]);
    }

    private static LigneLue Ligne(SqlDataReader r)
        => Convert.ToInt32(r["Bloc"]) == 0 ? Envoi(r) : Demande(r);

    private static LigneLue Envoi(SqlDataReader r)
    {
        var etat = FormeLigne.Texte(r, "Etat");
        var erreur = PremiereLigne(FormeLigne.Texte(r, "Erreur"));
        var compteRendu = FormeLigne.Date(FormeLigne.DateDe(r, "DateCompteRendu"));
        var accuse = FormeLigne.Date(FormeLigne.DateDe(r, "DateAccuse"));

        // La regle d'etat (en tete du fichier).
        (string Etat, int[] Faites, string Statut) regle =
            erreur.Length > 0 || etat is "8" or "9"
                ? ("AFaire", [], erreur.Length > 0 ? $"en erreur : {erreur}" : etat == "8" ? "erreur d'envoi" : "compte rendu KO")
            : compteRendu.Length == 0
                ? ("EnCours", [0], FormeLigne.Joindre(accuse.Length > 0 ? $"accusé de réception le {accuse}" : null, "compte rendu en attente"))
            : etat == "4"
                ? ("Validee", [0, 2], $"compte rendu OK le {compteRendu}")
                : ("Validee", [0], $"compte rendu le {compteRendu}");

        var numero = FormeLigne.Texte(r, "Numero");
        var affaire = FormeLigne.Texte(r, "Affaire");
        var envoye = FormeLigne.Date(FormeLigne.DateDe(r, "DateEnvoi"));
        var nom = FormeLigne.Joindre(
            numero.Length > 0 ? $"Envoi {numero}" : $"Envoi n° {FormeLigne.Texte(r, "Identifiant")}",
            FormeLigne.Texte(r, "Document"));
        var precisions = FormeLigne.Joindre(
            affaire.Length > 0 ? $"Rôle {affaire}" : null,
            FormeLigne.Texte(r, "Destinataire"),
            envoye.Length > 0 ? $"envoyé le {envoye}" : null,
            regle.Statut);
        return new LigneLue(nom, precisions, Groupes[0], regle.Etat, regle.Faites) { Id = $"T-{FormeLigne.Texte(r, "Identifiant")}" };
    }

    private static LigneLue Demande(SqlDataReader r)
    {
        var affaire = FormeLigne.Texte(r, "Affaire");
        var recue = FormeLigne.Date(FormeLigne.DateDe(r, "DateTri"));
        var formalite = FormeLigne.Texte(r, "Formalite");
        var erreur = PremiereLigne(FormeLigne.Texte(r, "Erreur"));
        // L'etat de traitement RIG : vide a l'ouverture (en attente) ; « A » acceptee, « R » refusee, que seule la recherche
        // « Demandes traitées » fait entrer (lot 6) -- elles sont alors « Traité ».
        var traitement = FormeLigne.Texte(r, "Etat");
        var nom = FormeLigne.Joindre($"Demande {FormeLigne.Texte(r, "Numero")}", affaire.Length > 0 ? $"Rôle {affaire}" : null);
        var precisions = FormeLigne.Joindre(
            recue.Length > 0 ? $"Reçue le {recue}" : null,
            Formalites.TryGetValue(formalite, out var libelle) ? libelle : formalite,
            erreur.Length > 0 ? $"en erreur : {erreur}" : null,
            traitement switch { "" => "en attente de traitement", "A" => "acceptée", "R" => "refusée", var t => $"traitée ({t})" });
        // La regle d'etat (en tete du fichier) : une demande en attente attend le greffe ; une demande traitee l'est.
        return new LigneLue(nom, precisions, Groupes[1], traitement.Length == 0 ? "AFaire" : "Validee", [])
        {
            Id = $"C-{FormeLigne.Texte(r, "Identifiant")}",
        };
    }

    /// <summary>Une erreur SES tient parfois sur plusieurs lignes (TRIDEdiEntrantCR.cs:236-241) : la premiere.</summary>
    private static string PremiereLigne(string texte)
        => texte.Split(['\r', '\n'], 2, StringSplitOptions.RemoveEmptyEntries) is [var premiere, ..] ? premiere.Trim() : texte;
}
