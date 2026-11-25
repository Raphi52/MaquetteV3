using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Retours LRAR » (PROC_RETOURLRAR) : les retours de lettres recommandees saisis au greffe (accuse recu, NPAI, refus,
/// non reclame, non envoye). Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_RETOURLRAR. L'ecran V2 n'a pas de liste : on
/// scanne le code-barre d'UN pli (ETP_RETOURLRAR_RECEPTION\OPE_RETOURLRAR_RECEPTION.cs, recept l. 105-125), on choisit sa
/// modalite et sa date (ou « date non renseignee »), et l'ecran les ecrit sur le pli : LRAR_INFOS.LRARI_TYPE_DATE et
/// LRARI_DATE_RECEPTION (TraitementReception l. 818-821 ; ChangementDateReception l. 910-956 pour une modification). Un pli
/// sans modalite n'est pas encore revenu (IsNullOrEmpty(LRARI_TYPE_DATE), l. 707). Les plis dont la modalite est ecrite sont
/// donc le registre des retours saisis : la liste de cet ecran -- comme les demandes NPAI pour LecteurCourriersNpai, sur le
/// meme ecran reuni. Le libelle d'une modalite et son drapeau « a gerer » sont dans PARAM_LRAR_INFOS (PRMLI_CODE =
/// LRARI_TYPE_DATE), que la V2 affiche « <libelle> <date> » sur le pli (l. 805-808). Dans la base V3, LRAR_INFOS et
/// PARAM_LRAR_INFOS sont les tables V2 telles quelles (v2.*).
///
/// FILTRES : une modalite saisie (LRARI_TYPE_DATE ni NULL ni vide) ; un pli papier, LRARI_TYPE LRAR ou LR, le filtre des
/// recherches de cet ecran (l. 487, 498, 511). Les plis CIPREMIUM_LRAR (lettre recommandee electronique) ont leur retour
/// ecrit par le flux de La Poste (Edi\DLL\EdiMetier\EdiPlieuse\CmdPlieuseRetourLaPosteEdiEntrant.cs:134), pas a cet ecran.
/// Ordre : les retours sans date d'abord (une date reste a saisir), puis la date de reception la plus recente en tete, puis
/// le numero du pli (la V2 range ses plis par numero decroissant, l. 515).
///
/// GROUPES (LRARI_TYPE_DATE, codes de eTypeReceptionLrar, Outils\DLL\RigMetier\Edition\eTypeReceptionLrar.cs) :
///   RECEPTION -> « Recus » ; NPAI, REFUSE, INCOMPLET -> « NPAI, refuses ou incomplets » ; NON_RECLAME -> « Non reclames » ;
///   PAS_ENVOYE -> « Non envoyes ». Le descripteur n'a pas de groupe pour PLI_PERDU (pose par La Poste sur les plis
///   electroniques seulement : aucun pli LRAR ou LR de RIG_DEV ne l'a, lu le 25/11/2026) ni pour un code inconnu : ils vont
///   dans « NPAI, refuses ou incomplets », les retours anormaux, avec leur code dans les precisions.
///
/// REGLE D'ETAT (marches du descripteur : 0 Pli scanne, 1 Modalite, 2 Date de reception, 3 Remise en emission, 4 Evenement) :
///   modalite et date de reception     -> Validee « Retour enregistre » : pli, modalite, date (0, 1, 2) ;
///   modalite sans date                -> EnCours « En cours » : pli et modalite (0, 1) -- la case « date non renseignee »
///                                        (l. 818), que la V2 affiche « date non precisee sur l'imprime » (Constantes.cs:9) ;
///   plus la remise en emission (3) quand la modalite n'est pas « a gerer » (PRMLI_A_GERER = 0, PAS_ENVOYE dans RIG_DEV) :
///   la V2 remet alors en file les demandes d'edition du pli (l. 825-835).
/// « A saisir » (AFaire) n'a pas de ligne : un pli dont l'accuse n'est pas revenu n'a pas de retour a saisir. La marche
/// « Evenement lie » (passer en survenu l'evenement futur d'une instance, PasserEPlieEnSurvenu l. 986) ne laisse pas de
/// trace sur le pli : elle n'est jamais cochee.
/// </summary>
internal sealed class LecteurRetoursLrar : ILecteurEcran
{
    public string Procedure => "PROC_RETOURLRAR";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Recus = "Reçus", Anomalies = "NPAI, refusés ou incomplets", NonReclames = "Non réclamés",
                          NonEnvoyes = "Non envoyés";

    /// <summary>Ce que la V2 ecrit a la place d'une date absente (PROC_RETOURLRAR\Resources\Constantes.cs:9).</summary>
    private const string DateNonPrecisee = "date non précisée sur l'imprimé";

    private const string Requete = """
        SELECT TOP (@plafond) l.LRARI_NUM_LRAR, l.LRARI_TYPE, l.LRARI_TYPE_DATE, l.LRARI_DATE_RECEPTION, l.LRARI_NUM_LRAR_RENVOYE,
               p.PRMLI_LIBELLE, p.PRMLI_A_GERER, COUNT(*) OVER () AS Total
        FROM v2.LRAR_INFOS AS l
        LEFT JOIN v2.PARAM_LRAR_INFOS AS p ON p.PRMLI_CODE = l.LRARI_TYPE_DATE
        WHERE l.LRARI_TYPE IN ('LRAR', 'LR') AND l.LRARI_TYPE_DATE IS NOT NULL AND l.LRARI_TYPE_DATE <> ''
        ORDER BY CASE WHEN l.LRARI_DATE_RECEPTION IS NULL THEN 0 ELSE 1 END, l.LRARI_DATE_RECEPTION DESC, l.LRARI_NUM_LRAR DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var modalite = FormeLigne.Texte(r, "LRARI_TYPE_DATE");
        var date = FormeLigne.DateDe(r, "LRARI_DATE_RECEPTION");
        // PRMLI_A_GERER : null sans parametrage de la modalite (code inconnu) -- pas de remise en emission connue.
        var remise = r["PRMLI_A_GERER"] is bool aGerer && !aGerer;
        var (etat, faites) = Etat(date is not null, remise);
        var libelle = FormeLigne.Texte(r, "PRMLI_LIBELLE");
        // « Recu le » + date, comme la V2 ; sans date, le libelle sans son « le » final, puis la mention de la V2.
        var retour = libelle.Length == 0
            ? FormeLigne.Joindre($"modalité « {modalite} » de l'ancien RIG", date is null ? DateNonPrecisee : $"le {FormeLigne.Date(date)}")
            : date is null
                ? FormeLigne.Joindre(libelle.EndsWith(" le", StringComparison.Ordinal) ? libelle[..^3] : libelle, DateNonPrecisee)
                : $"{libelle} {FormeLigne.Date(date)}";
        var renvoye = FormeLigne.Texte(r, "LRARI_NUM_LRAR_RENVOYE");
        var precisions = FormeLigne.Joindre(retour,
                                            remise ? "demandes d'édition remises en file d'émission" : null,
                                            renvoye.Length > 0 ? $"renvoyé sous le n° {renvoye}" : null,
                                            FormeLigne.Texte(r, "LRARI_TYPE") == "LR" ? "LR sans accusé de réception" : null);
        return new LigneLue($"Pli {FormeLigne.Texte(r, "LRARI_NUM_LRAR")}", precisions, Groupe(modalite), etat, faites);
    }

    /// <summary>Le groupe d'une modalite (en tete du fichier).</summary>
    internal static string Groupe(string modalite) => modalite switch
    {
        "RECEPTION" => Recus,
        "NON_RECLAME" => NonReclames,
        "PAS_ENVOYE" => NonEnvoyes,
        _ => Anomalies,
    };

    /// <summary>La regle d'etat (en tete du fichier) : etat du descripteur et marches franchies.</summary>
    internal static (string Etat, int[] Faites) Etat(bool date, bool remise)
    {
        int[] faites = (date, remise) switch
        {
            (true, true) => [0, 1, 2, 3],
            (true, false) => [0, 1, 2],
            (false, true) => [0, 1, 3],
            (false, false) => [0, 1],
        };
        return (date ? "Validee" : "EnCours", faites);
    }
}
