using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// LA FICHE D'UNE LIGNE DES ECRANS D'AUDIENCE : ce qu'une audience, un cabinet ou une affaire au role montre une fois depliee
/// (point 8 du mode d'emploi, LecteurEcran.cs ; la forme Audience la lit en lecture, web/src/ecrans/formes/commun.tsx FicheLue).
/// Plan « RIG v3 100 % fonctionnel », lot 7, 27/11/2026. Lecteurs : LecteurComposition.cs, LecteurPreparationAudience.cs,
/// LecteurConvocations.cs, LecteurMiseAuPointChambres.cs, LecteurRetourAudience.cs ; sections et libelles : leurs descripteurs
/// (Pont/EcransGeneres/Surveiller.cs), au mot pres.
///
/// REGLES COMMUNES :
///   - un champ que la base du greffe ne porte pas n'est PAS rendu : l'ecran l'ecrit « Non lu dans la base par la V3 » (commun.tsx,
///     NON_LU), et chaque methode dit lequel et pourquoi ; un champ rendu vide est lu, et vide dans la base ;
///   - aucun champ n'est modifiable : ces ecrans n'ont pas de geste Enregistrer (leurs gestes prennent les parametres de l'audience,
///     EcrivainComposition.cs, EcrivainRetourAudience.cs...) ;
///   - les parties, les evenements et les decisions d'une instance se lisent par les requetes de la fiche de l'instance
///     (LecteurInstanceJudiciaire.cs), sans les recopier.
/// </summary>
// fix-ok: controle final du lot 7 (27/11/2026, capture c21.png) -- deplier une affaire des cinq ecrans d'audience affichait « La
// procédure PROC_MPCS ne lit pas encore de fiche » (ModuleEcransGeneres.cs:55-56) : aucun de leurs lecteurs n'etait IFicheEcran.
internal static class FichesAudienceV2
{
    /// <summary>Une audience ou un cabinet (AUDIENCE_CABINET ; AUDNC_AUDIENCE = 0 pour un cabinet, proc_retaud.md:223) et son libelle
    /// (AudiencesV2.Libelle).</summary>
    private sealed record Audience(string Libelle, DateTime? Date, bool EstAudience);

    private static async Task<Audience?> AudienceAsync(SqlConnection cn, int id, CancellationToken ct)
    {
        await using var r = await LectureV2.LireAsync(cn, $$"""
            SELECT a.AUDNC_DATE, CONVERT(bit, ISNULL(a.AUDNC_AUDIENCE, 1)) AS EstAudience, {{AudiencesV2.Libelle}} AS Libelle
            FROM v2.AUDIENCE_CABINET AS a
            {{AudiencesV2.Jointures}}
            WHERE a.AUDNC_ID_ADNC = @id
            """, ct, new SqlParameter("@id", id));
        return await r.ReadAsync(ct) ? new Audience(FormeLigne.Texte(r, "Libelle"), FormeLigne.DateDe(r, "AUDNC_DATE"), r["EstAudience"] is true) : null;
    }

    private static bool Entier(string texte, out int n) => int.TryParse(texte, NumberStyles.None, CultureInfo.InvariantCulture, out n);

    // ------------------------------------------------------------------------------------------------------------------------------
    // PROC_COMPO (une ligne = une audience, LecteurComposition.cs). La composition de l'audience : COMPOSITION (CMPST_ID_ADNC), ses
    // siegeants SIEGER par SGR_NUM_ORDRE (le premier preside, « PAU » : Composition.cs:365-379), le greffier GREFFIER_ASSERMENTE et le
    // parquetier PARQUETIER, nommes par leur abrege comme les listes ULT_COMBO_* de RIG v2 (ETP_COMPO_AUDIENCE.cs:100-141) : ce que
    // « Valider » enregistre (EcrivainComposition.cs). Plusieurs compositions : la premiere par numero d'ordre (la V3 n'en saisit
    // qu'une, EcrivainComposition les refuse). NON RENDU : « Invitations » -- RIG v2 les envoie par courriel au changement de
    // composition, sans trace dans les tables de la base.
    internal static async Task<FicheEcran?> CompositionAsync(SqlConnection cn, string procedure, string id, CancellationToken ct)
    {
        if (!Entier(id, out var n) || await AudienceAsync(cn, n, ct) is not { } a) return null;
        int? composition = null;
        string greffier = "", parquetier = "";
        await using (var r = await LectureV2.LireAsync(cn, """
            SELECT TOP (1) c.CMPST_ID_CMAUD, g.GRASS_ABREGE, p.PRQTR_ABREGE
            FROM v2.COMPOSITION AS c
            LEFT JOIN v2.GREFFIER_ASSERMENTE AS g ON g.GRASS_ID_GRASS = c.CMPST_ID_GRASS
            LEFT JOIN v2.PARQUETIER AS p ON p.PRQTR_ID_PRQTR = c.CMPST_ID_PRQTR
            WHERE c.CMPST_ID_ADNC = @id
            ORDER BY c.CMPST_NUM_ORDRE, c.CMPST_ID_CMAUD
            """, ct, new SqlParameter("@id", n)))
            if (await r.ReadAsync(ct))
            {
                composition = Convert.ToInt32(r["CMPST_ID_CMAUD"], CultureInfo.InvariantCulture);
                greffier = FormeLigne.Texte(r, "GRASS_ABREGE");
                parquetier = FormeLigne.Texte(r, "PRQTR_ABREGE");
            }
        var juges = new List<string>();
        if (composition is { } c)
            await using (var r = await LectureV2.LireAsync(cn, """
                SELECT j.JUG_ABREGE FROM v2.SIEGER AS s JOIN v2.JUGE AS j ON j.JUG_ID_JUG = s.SGR_ID_JUG
                WHERE s.SGR_ID_CMAUD = @c ORDER BY s.SGR_NUM_ORDRE, s.SGR_ID_SGR
                """, ct, new SqlParameter("@c", c)))
                while (await r.ReadAsync(ct)) juges.Add(FormeLigne.Texte(r, "JUG_ABREGE"));
        return new FicheEcran(procedure, id, a.Libelle,
        [
            new SectionFiche("Audience", [new("Audience ou cabinet", a.Libelle), new("Date de l'audience", FormeLigne.DateFiche(a.Date))]),
            new SectionFiche("Juges", [new("Juge 1", juges.FirstOrDefault() ?? ""), new("Juges suivants", string.Join(", ", juges.Skip(1)))]),
            new SectionFiche("Greffier", [new("Greffier", greffier)]),
            new SectionFiche("Parquetier", [new("Parquetier", parquetier)]),
        ]);
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // PROC_PREAUD (une ligne = une audience ou un cabinet, LecteurPreparationAudience.cs). « Audience » ou « Cabinet » selon
    // AUDNC_AUDIENCE (le mode A / P cherche des audiences, C des cabinets, ETP.cs:54-62). L'ordre des affaires : la liste de RIG v2
    // (PROC_PREAUD\CALCUL_ORDRE\OPE_ORDRE_INSTANCE.cs:50-59 ses colonnes, :67-99 ses valeurs) -- n° de role et nom courant de
    // l'instance, heure d'appel (APPAF_HEURE_APPEL), type de classement, n° d'appel -- dans l'ordre de AudienceCabinet.AppelAffaires
    // (:36), celui que lit EcrivainPreparationAudience.cs (par APPAF_ID_APPAF). NON RENDUS : la colonne « Juge » (juge-commissaire,
    // ou mandataire en section sanction : regle de :67-107 non reprise), les colonnes mandataire et administrateur du mode PC
    // (:119-154) ; « Options », « Destinataires » et « Génération » sont des choix du plumitif, que la V3 ne genere pas.
    private static readonly string[] ColonnesOrdre = ["N° rôle", "Nom", "Heure", "Type", "N° appel"];

    internal static async Task<FicheEcran?> PreparationAsync(SqlConnection cn, string procedure, string id, CancellationToken ct)
    {
        if (!Entier(id, out var n) || await AudienceAsync(cn, n, ct) is not { } a) return null;
        var lignes = new List<string[]>();
        await using (var r = await LectureV2.LireAsync(cn, """
            SELECT i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, aa.APPAF_HEURE_APPEL, aa.APPAF_TYPE_CLASSEMENT, aa.APPAF_NUM_APPEL
            FROM v2.APPEL_AFFAIRE AS aa LEFT JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = aa.APPAF_ID_INSTN
            WHERE aa.APPAF_ID_ADNC = @id
            ORDER BY aa.APPAF_ID_APPAF
            """, ct, new SqlParameter("@id", n)))
            while (await r.ReadAsync(ct))
                lignes.Add([FormeLigne.Texte(r, "INSTN_NUM_ROLE"), FormeLigne.Texte(r, "INSTN_NOM_COURANT"), FormeLigne.Texte(r, "APPAF_HEURE_APPEL"),
                            FormeLigne.Texte(r, "APPAF_TYPE_CLASSEMENT"), FormeLigne.Texte(r, "APPAF_NUM_APPEL")]);
        return new FicheEcran(procedure, id, a.Libelle,
        [
            new SectionFiche("Audience", [new("Audience", a.EstAudience ? a.Libelle : ""), new("Cabinet", a.EstAudience ? "" : a.Libelle)]),
            new SectionFiche("Ordre des affaires", [new("Affaires dans l'ordre d'appel", "") { Colonnes = ColonnesOrdre, Lignes = [.. lignes] }]),
        ]);
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // PROC_GCONV (une ligne = une instance du cabinet et sa LRAR, LecteurConvocations.cs : Id « instance-demande d'edition-relance-
    // exercice »).
    //   - « Audience cabinet » : les cabinets ou l'instance est appelee (AUDNC_AUDIENCE = 0). L'Id de la ligne ne nomme pas le cabinet ;
    //     RIG v2 n'en charge qu'un a la fois (OPE_CITATIONS.cs:157-162) : un seul en general, sa date ; plusieurs, leurs libelles ;
    //   - « Instances » : le noeud de l'arbre de RIG v2 (OPE_CITATIONS.cs:83-95) -- coche d'office si la LRAR porte une solution de
    //     citation, sans reception ni NPAI, et sans citation (la regle d'etat du lecteur) ; la date de la LRAR (DMNED_DATE_CREATION de
    //     sa demande d'edition) et son type de date (PARAM_LRAR_INFOS, sinon le code) ;
    //   - « Éditions » : la citation (le premier evenement SURV_CEP_CITATION_HUISS de l'instance, :83-89) et les editions que « Citer par
    //     huissier » met dans la file, dans les demandes « GCONV instance <role> » (EcrivainConvocations.cs ; OPE_CITATIONS.cs:334-459) ;
    //     le KBIS a part, source DOSSIER_RCS (:436-457).
    //   NON RENDUS : « Huissier » (RIG v2 ne l'ecrit que dans la liste des destinataires de chaque edition, LISTE_ENTITE_DESTINATION,
    //   pas sur l'instance) ; « Copie conforme », « Copie de l'ordonnance » et « Facturation » (la V3 ne les produit pas).
    internal static async Task<FicheEcran?> ConvocationAsync(SqlConnection cn, string procedure, string id, string codeCitation, CancellationToken ct)
    {
        var parts = id.Split('-');
        if (parts.Length < 2 || !Entier(parts[0], out var instance) || !Entier(parts[1], out var lrar)) return null;
        string role, nom, typeDate;
        DateTime? dateLrar, citee;
        bool coche;
        await using (var r = await LectureV2.LireAsync(cn, """
            SELECT i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, de.DMNED_DATE_CREATION, l.LRARI_SLN_CITATION, l.LRARI_TYPE_DATE, pl.PRMLI_LIBELLE,
                   cit.EVNPR_DATE AS DateCitation
            FROM v2.INSTANCE AS i
            JOIN v2.DEMANDE_EDITION AS de ON de.DMNED_ID_DMNED = @lrar
            JOIN v2.LRAR_INFOS AS l ON l.LRARI_NUM_LRAR = de.DMNED_NUM_LRAR
            LEFT JOIN v2.PARAM_LRAR_INFOS AS pl ON pl.PRMLI_CODE = l.LRARI_TYPE_DATE
            OUTER APPLY (SELECT TOP (1) c.EVNPR_DATE FROM v2.EVENEMENT_PROCEDURE AS c WHERE c.EVNPR_ID_INSTN = i.INSTN_ID_INSTN
                           AND c.EVNPR_CODE_EVE_PRO = @citation ORDER BY c.EVNPR_NUM_ORDRE, c.EVNPR_ID_EVNPR) AS cit
            WHERE i.INSTN_ID_INSTN = @instance
            """, ct, new SqlParameter("@lrar", lrar), new SqlParameter("@instance", instance), new SqlParameter("@citation", codeCitation)))
        {
            if (!await r.ReadAsync(ct)) return null;
            role = FormeLigne.Texte(r, "INSTN_NUM_ROLE");
            nom = FormeLigne.Texte(r, "INSTN_NOM_COURANT");
            dateLrar = FormeLigne.DateDe(r, "DMNED_DATE_CREATION");
            citee = FormeLigne.DateDe(r, "DateCitation");
            var code = FormeLigne.Texte(r, "LRARI_TYPE_DATE");
            typeDate = FormeLigne.Texte(r, "PRMLI_LIBELLE") is { Length: > 0 } lib ? lib : code;
            coche = code is not ("RECEPTION" or "NPAI") && citee is null && r["LRARI_SLN_CITATION"] is true;
        }

        var cabinets = new List<(string Libelle, DateTime? Date)>();
        await using (var r = await LectureV2.LireAsync(cn, $$"""
            SELECT a.AUDNC_DATE, {{AudiencesV2.Libelle}} AS Libelle
            FROM v2.APPEL_AFFAIRE AS aa JOIN v2.AUDIENCE_CABINET AS a ON a.AUDNC_ID_ADNC = aa.APPAF_ID_ADNC
            {{AudiencesV2.Jointures}}
            WHERE aa.APPAF_ID_INSTN = @instance AND a.AUDNC_AUDIENCE = 0
            ORDER BY a.AUDNC_DATE, a.AUDNC_HEURE, a.AUDNC_ID_ADNC
            """, ct, new SqlParameter("@instance", instance)))
            while (await r.ReadAsync(ct)) cabinets.Add((FormeLigne.Texte(r, "Libelle"), FormeLigne.DateDe(r, "AUDNC_DATE")));

        int editions = 0, kbis = 0;
        await using (var r = await LectureV2.LireAsync(cn, """
            SELECT ISNULL(SUM(CASE WHEN e.DMNED_CODE_ENTITE_SOURCE = 'DOSSIER_RCS' THEN 0 ELSE 1 END), 0) AS Editions,
                   ISNULL(SUM(CASE WHEN e.DMNED_CODE_ENTITE_SOURCE = 'DOSSIER_RCS' THEN 1 ELSE 0 END), 0) AS Kbis
            FROM v2.DEMANDE AS d JOIN v2.DEMANDE_EDITION AS e ON e.DMNED_ID_DMND = d.DMND_ID_DMND
            WHERE d.DMND_CODE_PROSS = 'GCONV' AND d.DMND_NOM = 'GCONV instance ' + @role
            """, ct, new SqlParameter("@role", role)))
            if (await r.ReadAsync(ct))
            {
                editions = Convert.ToInt32(r["Editions"], CultureInfo.InvariantCulture);
                kbis = Convert.ToInt32(r["Kbis"], CultureInfo.InvariantCulture);
            }
        static string DansLaFile(int n) => n switch { 0 => "", 1 => "1 édition dans la file des éditions", _ => $"{n} éditions dans la file des éditions" };

        return new FicheEcran(procedure, id, nom.Length > 0 ? nom : $"Instance n° {role}",
        [
            new SectionFiche("Audience cabinet",
            [
                new("Audience cabinet", string.Join(" ; ", cabinets.Select(x => x.Libelle))),
                new("Date de l'audience", cabinets.Count == 1 ? FormeLigne.DateFiche(cabinets[0].Date) : ""),
            ]),
            new SectionFiche("Instances", [new("Case cochée", FormeLigne.Case(coche)), new("Date de LRAR", FormeLigne.DateFiche(dateLrar)), new("Type de date", typeDate)]),
            new SectionFiche("Éditions",
            [
                new("Citation", citee is null ? "" : FormeLigne.Joindre($"citée le {FormeLigne.Date(citee)}", DansLaFile(editions) is { Length: > 0 } f ? f : null)),
                new("KBIS", DansLaFile(kbis)),
            ]),
        ]);
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // UN APPEL D'AFFAIRE (une ligne de PROC_MPCS et de PROC_RETAUD) : la requete de la grille du retour d'audience de RIG v2
    // (OPE_LIST_APPEL_AFFAIRE.cs:561-592), sur l'appel seul -- n° de role, n° d'appel, resultat (APPAF_CODE_PLUMITIF), prochain appel
    // (:581), composition (« Compo. », :583), modele de jugement et n° de minute de la decision PRONONCEE a cet appel, avec son document
    // REDA_DECI_JUD (:584-586), solde de l'affaire (:587-589) ; l'audience et son libelle ; la decision DELIBEREE a cet appel
    // (DecisionsJudiciairesDeliberés, AppelAffaire.cs:315-322) ; une publicite en attente (EVENEMENT_PROCEDURE_EN_ATTENTE, :598) ; la
    // procedure collective et la date d'enrolement de l'instance.
    private sealed record Appel(int IdInstance, string Role, string Nom, DateTime? Enrolement, string NumeroPc, string NumeroAppel, string Resultat,
                                DateTime? ProchainAppel, string? Composition, string Audience, bool EstAudience, string Modele, string Minute,
                                string Deliberee, bool Publicite, decimal? Solde);

    private static readonly string RequeteAppel = $$"""
        SELECT i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, i.INSTN_DATE_ENROLEMENT, pc.PRCLL_NUM_PC, aa.APPAF_NUM_APPEL,
               aa.APPAF_TYPE_CLASSEMENT, aa.APPAF_CODE_PLUMITIF, CONVERT(bit, ISNULL(a.AUDNC_AUDIENCE, 1)) AS EstAudience,
               {{AudiencesV2.Libelle}} AS Audience,
               (SELECT TOP (1) a2.AUDNC_DATE FROM v2.APPEL_AFFAIRE AS aa2
                JOIN v2.AUDIENCE_CABINET AS a2 ON a2.AUDNC_ID_ADNC = aa2.APPAF_ID_ADNC AND a2.AUDNC_DATE > a.AUDNC_DATE
                WHERE aa2.APPAF_ID_INSTN = i.INSTN_ID_INSTN ORDER BY a2.AUDNC_DATE) AS ProchainAppel,
               {{LecteurRetourAudience.Composition}} AS Compo,
               jp.Modele, jp.Minute, dl.DCJDC_TYPE_DECISION AS TypeDeliberee, dl.R_TYPEDECISI_LIBELLE_ECRAN AS LibelleDeliberee, dl.Quand AS QuandDeliberee,
               CASE WHEN EXISTS (SELECT 1 FROM v2.EVENEMENT_PROCEDURE_EN_ATTENTE AS e
                                 WHERE e.EVNPA_ID_APPAF_DELIBERER = aa.APPAF_ID_APPAF OR e.EVNPA_ID_APPAF_PRONONCER = aa.APPAF_ID_APPAF)
                    THEN 1 ELSE 0 END AS Publicite,
               CASE WHEN EXISTS (SELECT 1 FROM v2.ARTICLE_UTILISE AS au JOIN rec.Client AS cl ON cl.Id = au.ARTUT_ID_CLIEN AND cl.Type = 'C'
                                 WHERE au.ARTUT_ID_AFFRE = i.INSTN_ID_AFFRE)
                      OR EXISTS (SELECT 1 FROM v2.VENTILATION AS v JOIN rec.Client AS cl ON cl.Id = v.VENTI_ID_CLIEN AND cl.Type = 'C'
                                   AND cl.NumeroCompte <> '99999999' WHERE v.VENTI_ID_AFFRE = i.INSTN_ID_AFFRE)
                    THEN NULL
                    ELSE ISNULL((SELECT SUM(v.VENTI_MONTANT) FROM v2.VENTILATION AS v WHERE v.VENTI_ID_AFFRE = i.INSTN_ID_AFFRE), 0)
                       - ISNULL((SELECT SUM(au.ARTUT_MONTANT_TTC) FROM v2.ARTICLE_UTILISE AS au WHERE au.ARTUT_ID_AFFRE = i.INSTN_ID_AFFRE), 0)
               END AS Solde
        FROM v2.APPEL_AFFAIRE AS aa
        JOIN v2.AUDIENCE_CABINET AS a ON a.AUDNC_ID_ADNC = aa.APPAF_ID_ADNC
        JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = aa.APPAF_ID_INSTN
        LEFT JOIN v2.PROCEDURE_COLLECTIVE AS pc ON pc.PRCLL_ID_PRCLL = i.INSTN_ID_PRCLL
        OUTER APPLY (SELECT TOP (1) dc.DCMDS_CODE_MODELE_DOCUMENT AS Modele, dj.DCJDC_NUM_REPERTOIRE AS Minute
                     FROM v2.DECISION_JUDICIAIRE AS dj
                     JOIN v2.DOCUMENT_DOSSIER AS dc ON dc.DCMDS_VALEUR_ID_METIER = dj.DCJDC_ID_DCJDC AND dc.DCMDS_CODE_ENTITE = 'REDA_DECI_JUD'
                     WHERE dj.DCJDC_ID_APPAF_PRONONCER = aa.APPAF_ID_APPAF ORDER BY dj.DCJDC_NUM_ORDRE) AS jp
        OUTER APPLY (SELECT TOP (1) dj.DCJDC_TYPE_DECISION, t.R_TYPEDECISI_LIBELLE_ECRAN, ISNULL(dj.DCJDC_DATE, dj.DCJDC_DATE_PREVUE) AS Quand
                     FROM v2.DECISION_JUDICIAIRE AS dj LEFT JOIN v2.TYPE_DECISION AS t ON t.R_TYPEDECISI_VALEUR = dj.DCJDC_TYPE_DECISION
                     WHERE dj.DCJDC_ID_APPAF_DELIBERER = aa.APPAF_ID_APPAF ORDER BY dj.DCJDC_NUM_ORDRE, dj.DCJDC_ID_DCJDC) AS dl
        {{AudiencesV2.Jointures}}
        WHERE aa.APPAF_ID_APPAF = @id
        """;

    private static async Task<Appel?> AppelAsync(SqlConnection cn, int id, CancellationToken ct)
    {
        await using var r = await LectureV2.LireAsync(cn, RequeteAppel, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        var deliberee = FormeLigne.Texte(r, "TypeDeliberee") is { Length: > 0 } type
            ? FormeLigne.Joindre(type, FormeLigne.Texte(r, "LibelleDeliberee"),
                                 FormeLigne.DateDe(r, "QuandDeliberee") is { } q ? $"le {FormeLigne.Date(q)}" : null)
            : "";
        return new Appel(Convert.ToInt32(r["INSTN_ID_INSTN"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "INSTN_NUM_ROLE"),
            FormeLigne.Texte(r, "INSTN_NOM_COURANT"), FormeLigne.DateDe(r, "INSTN_DATE_ENROLEMENT"), FormeLigne.Texte(r, "PRCLL_NUM_PC"),
            LecteurRetourAudience.NumeroAppel(FormeLigne.Texte(r, "APPAF_NUM_APPEL"), FormeLigne.Texte(r, "APPAF_TYPE_CLASSEMENT")),
            FormeLigne.Texte(r, "APPAF_CODE_PLUMITIF"), FormeLigne.DateDe(r, "ProchainAppel"), LecteurRetourAudience.TexteComposition(r),
            FormeLigne.Texte(r, "Audience"), r["EstAudience"] is true, FormeLigne.Texte(r, "Modele"), FormeLigne.Texte(r, "Minute"), deliberee,
            Convert.ToInt32(r["Publicite"], CultureInfo.InvariantCulture) == 1,
            r["Solde"] is DBNull ? null : Convert.ToDecimal(r["Solde"], CultureInfo.InvariantCulture));
    }

    private static string NomDe(Appel a) => a.Nom.Length > 0 ? a.Nom : $"Instance n° {a.Role}";

    // ------------------------------------------------------------------------------------------------------------------------------
    // PROC_MPCS (une ligne = un appel d'affaire d'une instance avec procedure collective, LecteurMiseAuPointChambres.cs). Les etapes de
    // RIG v2 (proc_mpcs.md : ETP_AFFAIRES_SELECTIONNEES, puis en affaire unique l'instance, les parties, la decision, les evenements,
    // la facturation, le prochain appel) :
    //   - « Affaires » : l'appel tel que la grille du retour d'audience le montre (role, n° d'appel, PC, resultat, composition) et le
    //     n° de minute de la decision prononcee a cet appel (OPE_LIST_APPEL_AFFAIRE.cs:584-586) ;
    //   - « Instance », « Parties », « Événements » : la fiche de l'instance (LecteurInstanceJudiciaire : role, enrolement, parties
    //     par qualite, evenements survenus puis futurs -- les « à venir » de RIG v2) ;
    //   - « Décision judiciaire » › « Décision existante » : la decision deja deliberee a cet appel (AppelAffaire.cs:315-322) -- RIG v2
    //     n'en cree pas d'autre (PROC_MPCS.cs:1244) ;
    //   - « Prochain appel » : la premiere audience posterieure ou l'instance est appelee (:581).
    //   NON RENDUS : « Nouvelle décision » et « Type de décision » (des saisies : les parametres du geste) ; « Articles » (facturation
    //   de l'affaire unique, absente en lot, proc_mpcs.md:42).
    internal static async Task<FicheEcran?> MiseAuPointAsync(SqlConnection cn, string procedure, string id, CancellationToken ct)
    {
        if (!Entier(id, out var n) || await AppelAsync(cn, n, ct) is not { } a) return null;
        var evenements = await LecteurInstanceJudiciaire.EvenementsAsync(cn, a.IdInstance, ct);
        return new FicheEcran(procedure, id, NomDe(a),
        [
            new SectionFiche("Affaires",
            [
                new("Affaires sélectionnées", FormeLigne.Joindre($"Rôle {a.Role}", a.NumeroAppel.Length > 0 ? $"appel n° {a.NumeroAppel}" : null,
                    a.NumeroPc.Length > 0 ? $"PC {a.NumeroPc}" : null, a.Audience, a.Resultat.Length > 0 ? $"résultat {a.Resultat}" : "résultat à saisir",
                    a.Composition)),
                new("Numéro de minute", a.Minute),
            ]),
            new SectionFiche("Instance", [new("Numéro de rôle", a.Role), new("Date d'enrôlement", FormeLigne.DateFiche(a.Enrolement))]),
            await LecteurInstanceJudiciaire.PartiesAsync(cn, a.IdInstance, ct),
            new SectionFiche("Décision judiciaire", [new("Décision existante", a.Deliberee)]),
            new SectionFiche("Événements", [evenements.Champs[0], evenements.Champs[1] with { Libelle = "Événements à venir" }]),
            new SectionFiche("Prochain appel", [new("Prochain appel", a.ProchainAppel is { } p ? FormeLigne.Date(p) : "")]),
        ]);
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // PROC_RETAUD (une ligne = un appel d'affaire, LecteurRetourAudience.cs). Les colonnes de la grille de RIG v2 pour cet appel
    // (OPE_LIST_APPEL_AFFAIRE.cs:561-592) :
    //   - « Audience » ou « Cabinet » selon AUDNC_AUDIENCE ;
    //   - « Résultat par affaire » : resultat (APPAF_CODE_PLUMITIF), prochaine audience (« prochain appel », :581), modele de jugement
    //     et n° de minute (:584-586) ;
    //   - « Décision » : les decisions judiciaires de l'instance, sur lesquelles ouvre le menu « Décision judiciaire » de la ligne
    //     (PROC_MJUDS ajoute la sienne a Instance.ListeDecisionJudiciaire, PROC_MPCS.cs:1705-1710 la meme) : la liste de la fiche de
    //     l'instance (LecteurInstanceJudiciaire) ; la procedure collective (son numero) ;
    //   - « Facturation » › « Solde » : la colonne « solde » (:587-589) -- vide quand un client en compte (type C) porte des articles
    //     ou une ventilation de l'affaire, comme RIG v2 ;
    //   - « Publicités » › « Publicité » : un evenement de procedure en attente porte l'appel (:598).
    //   NON RENDUS : « Modification des parties » (une entree de menu, pas une valeur) ; « Tarif » (la derniere demande tarifee ou
    //   facturee, :590-592 : la V3 ne tient pas les factures de RIG v2, aucun code de Serveur\ ne cree de facture) ; « Impression
    //   légale » (aucune colonne de la grille ne la porte).
    internal static async Task<FicheEcran?> RetourAsync(SqlConnection cn, string procedure, string id, CancellationToken ct)
    {
        if (!Entier(id, out var n) || await AppelAsync(cn, n, ct) is not { } a) return null;
        var decisions = await LecteurInstanceJudiciaire.DecisionsAsync(cn, a.IdInstance, ct);
        return new FicheEcran(procedure, id, NomDe(a),
        [
            new SectionFiche("Audience", [new("Audience", a.EstAudience ? a.Audience : ""), new("Cabinet", a.EstAudience ? "" : a.Audience)]),
            new SectionFiche("Résultat par affaire",
            [
                new("Résultat", a.Resultat), new("Prochaine audience", a.ProchainAppel is { } p ? FormeLigne.Date(p) : ""),
                new("Modèle de jugement", a.Modele), new("Numéro de minute", a.Minute),
            ]),
            new SectionFiche("Décision", [decisions.Champs[0] with { Libelle = "Décision judiciaire" }, new("Procédure collective", a.NumeroPc)]),
            new SectionFiche("Facturation", [new("Solde", a.Solde is { } s ? s.ToString("0.00", CultureInfo.InvariantCulture) : "")]),
            new SectionFiche("Publicités", [new("Publicité", a.Publicite ? "en attente" : "")]),
        ]);
    }
}
