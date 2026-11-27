using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Retours LRAR » (PROC_RETOURLRAR, forme Operation) : les retours de lettres recommandees saisis au greffe (accuse recu, NPAI,
/// refus, non reclame, non envoye), et le pli SCANNE, cherche par son code-barre, a qui l'on saisit son retour. Mode d'emploi des
/// lecteurs : LecteurEcran.cs ; le geste : EcrivainRetoursLrar.cs (plan « RIG v3 100 % fonctionnel », lot 11, 27/11/2026).
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_RETOURLRAR. L'ecran V2 n'a pas de liste : on scanne le
/// code-barre d'UN pli (ETP_RETOURLRAR_RECEPTION\OPE_RETOURLRAR_RECEPTION.cs, recept l. 105-157), on choisit sa modalite et sa date
/// (ou « date non renseignee »), et l'ecran les ecrit sur le pli : LRAR_INFOS.LRARI_TYPE_DATE et LRARI_DATE_RECEPTION
/// (TraitementReception l. 790-842 ; ChangementDateReception l. 899-984 pour une modification). Un pli sans modalite n'est pas
/// encore revenu (IsNullOrEmpty(LRARI_TYPE_DATE), l. 707). Le libelle d'une modalite et son drapeau « a gerer » sont dans
/// PARAM_LRAR_INFOS (PRMLI_CODE = LRARI_TYPE_DATE), que la V2 affiche « <libelle> <date> » sur le pli (l. 805-808). Dans la base V3,
/// LRAR_INFOS, PARAM_LRAR_INFOS et DEMANDE_EDITION sont les tables V2 telles quelles (v2.*).
///
/// LA LISTE (LireAsync) : les plis dont la modalite est saisie, le registre des retours -- comme les demandes NPAI pour
/// LecteurCourriersNpai, sur le meme ecran reuni. FILTRES : une modalite saisie (LRARI_TYPE_DATE ni NULL ni vide) ; un pli papier,
/// LRARI_TYPE LRAR ou LR, le filtre des recherches de cet ecran (l. 487, 498, 511). Les plis CIPREMIUM_LRAR (lettre recommandee
/// electronique) ont leur retour ecrit par le flux de La Poste (Edi\DLL\EdiMetier\EdiPlieuse\CmdPlieuseRetourLaPosteEdiEntrant.cs:134),
/// pas a cet ecran. Ordre : les retours sans date d'abord (une date reste a saisir), puis la date de reception la plus recente en
/// tete, puis le numero du pli (la V2 range ses plis par numero decroissant, l. 515).
///
/// LE PLI SCANNE (ChercherAsync) : le texte ou le critere « Code-barre du pli », EGAL au numero du pli (« WHERE LRARI_NUM_LRAR = »
/// du code-barre scanne, l. 165-172 et 487-512), LRAR ou LR, revenu ou non : un pli sans retour est la ligne « A saisir ». Il porte
/// la demande d'edition qui l'a emis (DEMANDE_EDITION.DMNED_NUM_LRAR, l. 168) ; un pli sans demande d'edition, la V2 ne le reconnait
/// pas (« Ce code a barre n'est pas reconnu... », l. 133) : la ligne le dit, le geste le refuse.
///
/// GROUPES (LRARI_TYPE_DATE, codes de eTypeReceptionLrar, Outils\DLL\RigMetier\Edition\eTypeReceptionLrar.cs) :
///   RECEPTION -> « Recus » ; NPAI, REFUSE, INCOMPLET -> « NPAI, refuses ou incomplets » ; NON_RECLAME -> « Non reclames » ;
///   PAS_ENVOYE -> « Non envoyes » ; aucune modalite -> « Sans retour ». PLI_PERDU (pose par La Poste sur les plis electroniques
///   seulement : aucun pli LRAR ou LR de RIG_DEV ne l'a, lu le 25/11/2026) et un code inconnu vont dans « NPAI, refuses ou
///   incomplets », les retours anormaux, avec leur code dans les precisions.
/// REGLE D'ETAT : modalite et date -> Validee « Retour enregistre » ; modalite sans date (la case « date non renseignee », l. 818,
///   que la V2 affiche « date non precisee sur l'imprime », Constantes.cs:9) -> EnCours « Date a saisir » ; aucune modalite ->
///   AFaire « A saisir ». La remise en emission (modalite « pas a gerer », PRMLI_A_GERER = 0, PAS_ENVOYE dans RIG_DEV : la V2 remet
///   en file les demandes d'edition du pli, l. 825-835) est dite dans les precisions.
/// </summary>
internal sealed class LecteurRetoursLrar : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_RETOURLRAR";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Recus = "Reçus", Anomalies = "NPAI, refusés ou incomplets", NonReclames = "Non réclamés",
                          NonEnvoyes = "Non envoyés", SansRetour = "Sans retour";

    /// <summary>Le critere du descripteur : le code-barre scanne.</summary>
    internal const string CritereCodeBarre = "Code-barre du pli";

    /// <summary>Ce que la V2 ecrit a la place d'une date absente (PROC_RETOURLRAR\Resources\Constantes.cs:9).</summary>
    internal const string DateNonPrecisee = "date non précisée sur l'imprimé";

    /// <summary>Le refus de la V2 pour un pli qu'aucune demande d'edition n'a emis (OPE_RETOURLRAR_RECEPTION.cs:133).</summary>
    internal const string NonReconnu = "Ce code à barre n'est pas reconnu dans base de données du greffe.";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereCodeBarre];

    // Le pli, son parametrage de modalite et la premiere demande d'edition qui l'a emis (plus petit id).
    private const string Requete = """
        SELECT TOP (@plafond) l.LRARI_NUM_LRAR, l.LRARI_TYPE, l.LRARI_TYPE_DATE, l.LRARI_DATE_RECEPTION, l.LRARI_NUM_LRAR_RENVOYE,
               p.PRMLI_LIBELLE, p.PRMLI_A_GERER, e.DMNED_ID_DMNED, e.DMNED_LIBELLE_DOCUMENT, e.DMNED_DESIGNATION_DESTINATAIRE,
               e.DMNED_DATE_PRE_EMISSION, COUNT(*) OVER () AS Total
        FROM v2.LRAR_INFOS AS l
        LEFT JOIN v2.PARAM_LRAR_INFOS AS p ON p.PRMLI_CODE = l.LRARI_TYPE_DATE
        OUTER APPLY (SELECT TOP 1 de.DMNED_ID_DMNED, de.DMNED_LIBELLE_DOCUMENT, de.DMNED_DESIGNATION_DESTINATAIRE, de.DMNED_DATE_PRE_EMISSION
                     FROM v2.DEMANDE_EDITION AS de WHERE de.DMNED_NUM_LRAR = l.LRARI_NUM_LRAR ORDER BY de.DMNED_ID_DMNED) AS e
        WHERE l.LRARI_TYPE IN ('LRAR', 'LR') /*filtre*/
        ORDER BY CASE WHEN l.LRARI_DATE_RECEPTION IS NULL THEN 0 ELSE 1 END, l.LRARI_DATE_RECEPTION DESC, l.LRARI_NUM_LRAR DESC
        """;

    private const string Saisis = "AND l.LRARI_TYPE_DATE IS NOT NULL AND l.LRARI_TYPE_DATE <> ''";

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtre*/", Saisis, StringComparison.Ordinal), Ligne, ct,
                                  new SqlParameter("@plafond", plafond));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var code = recherche.Critere(CritereCodeBarre) ?? recherche.Texte?.Trim();
        if (string.IsNullOrEmpty(code)) return LireAsync(baseGreffe, aujourdhui, plafond, ct);
        return LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtre*/", "AND l.LRARI_NUM_LRAR = @code", StringComparison.Ordinal), Ligne, ct,
                                      new SqlParameter("@plafond", plafond), new SqlParameter("@code", code));
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        await using var r = await LectureV2.LireAsync(baseGreffe, Requete.Replace("/*filtre*/", "AND l.LRARI_NUM_LRAR = @code", StringComparison.Ordinal), ct,
                                                     new SqlParameter("@plafond", 1), new SqlParameter("@code", id));
        if (!await r.ReadAsync(ct)) return null;
        // La section « Retour » du descripteur, aux options de ses boutons radio (EcrivainRetoursLrar.Modalites) : ce que le pli porte.
        var date = FormeLigne.DateDe(r, "LRARI_DATE_RECEPTION");
        var modalite = FormeLigne.Texte(r, "LRARI_TYPE_DATE");
        var option = EcrivainRetoursLrar.Modalites.FirstOrDefault(m => m.Value == modalite).Key ?? modalite;
        return new FicheEcran(Procedure, id, $"Pli {FormeLigne.Texte(r, "LRARI_NUM_LRAR")}",
        [
            new SectionFiche("Retour",
            [
                new ChampFiche("Modalité de retour", option),
                new ChampFiche("Date de réception", FormeLigne.DateFiche(date)),
                new ChampFiche("Date non renseignée", FormeLigne.Case(modalite.Length > 0 && date is null)),
            ]),
        ]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var numero = FormeLigne.Texte(r, "LRARI_NUM_LRAR");
        var modalite = FormeLigne.Texte(r, "LRARI_TYPE_DATE");
        var date = FormeLigne.DateDe(r, "LRARI_DATE_RECEPTION");
        var lr = FormeLigne.Texte(r, "LRARI_TYPE") == "LR" ? "LR sans accusé de réception" : null;
        if (modalite.Length == 0)
        {
            // Pas encore revenu (l. 707) : la demande d'edition qui l'a emis, ou le refus de la V2 s'il n'en a pas.
            var emis = FormeLigne.DateDe(r, "DMNED_DATE_PRE_EMISSION");
            var precisionsSans = r["DMNED_ID_DMNED"] is DBNull
                ? FormeLigne.Joindre(NonReconnu, lr)
                : FormeLigne.Joindre(FormeLigne.Texte(r, "DMNED_LIBELLE_DOCUMENT"),
                                     FormeLigne.Texte(r, "DMNED_DESIGNATION_DESTINATAIRE") is { Length: > 0 } a ? $"à {a}" : null,
                                     emis is null ? null : $"émis le {FormeLigne.Date(emis)}", "aucun retour saisi", lr);
            return new LigneLue($"Pli {numero}", precisionsSans, SansRetour, "AFaire", []) { Id = numero };
        }
        // PRMLI_A_GERER : null sans parametrage de la modalite (code inconnu) -- pas de remise en emission connue.
        var remise = r["PRMLI_A_GERER"] is bool aGerer && !aGerer;
        var libelle = FormeLigne.Texte(r, "PRMLI_LIBELLE");
        // « Recu le » + date, comme la V2 ; sans date, le libelle sans son « le » final, puis la mention de la V2.
        var retour = libelle.Length == 0
            ? FormeLigne.Joindre($"modalité « {modalite} » de l'ancien RIG", date is null ? DateNonPrecisee : $"le {FormeLigne.Date(date)}")
            : date is null ? FormeLigne.Joindre(Sans(libelle), DateNonPrecisee) : $"{libelle} {FormeLigne.Date(date)}";
        var renvoye = FormeLigne.Texte(r, "LRARI_NUM_LRAR_RENVOYE");
        var precisions = FormeLigne.Joindre(retour,
                                            remise ? "demandes d'édition remises en file d'émission" : null,
                                            renvoye.Length > 0 ? $"renvoyé sous le n° {renvoye}" : null, lr);
        return new LigneLue($"Pli {numero}", precisions, Groupe(modalite), date is null ? "EnCours" : "Validee", []) { Id = numero };
    }

    /// <summary>Le libelle d'une modalite sans son « le » final (« Recu le » -> « Recu »).</summary>
    internal static string Sans(string libelle) => libelle.EndsWith(" le", StringComparison.Ordinal) ? libelle[..^3] : libelle;

    /// <summary>Le groupe d'une modalite (en tete du fichier).</summary>
    internal static string Groupe(string modalite) => modalite switch
    {
        "" => SansRetour,
        "RECEPTION" => Recus,
        "NON_RECLAME" => NonReclames,
        "PAS_ENVOYE" => NonEnvoyes,
        _ => Anomalies,
    };
}
