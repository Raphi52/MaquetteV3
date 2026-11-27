using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Retours LRAR » (PROC_RETOURLRAR) : « Enregistrer le retour » ecrit la modalite et la date de retour sur le pli scanne, comme la
/// reception de RIG v2. Mode d'emploi : EcrivainEcran.cs ; la liste et le pli scanne : LecteurRetoursLrar.cs (plan « RIG v3 100 %
/// fonctionnel », lot 11, 27/11/2026).
///
/// SOURCE (ancien RIG) : ETP_RETOURLRAR_RECEPTION\OPE_RETOURLRAR_RECEPTION.cs. Chaque pli coche est un pli scanne (recept, l. 105-157),
/// avec la modalite et la date de la section « Retour » -- les boutons radio (« Reception » par defaut, l. 56), la date et la case
/// « Date non renseignee ». CONTROLES, avant toute ecriture, au mot pres de la V2 :
///   1. aucun pli : « Veuillez saisir le numéro du recommandé » (l. 155) ;
///   2. ni date ni case « Date non renseignee » : « Si vous n'avez pas de date renseignée, il faut cocher la case 'Date non
///      renseignée'. » (FT 14369, l. 107-110) ;
///   3. un pli qu'aucune demande d'edition n'a emis (la requete de RechercheJudiciaire, l. 165-172, ne rend rien) : « Ce code à barre
///      n'est pas reconnu dans base de données du greffe. » (l. 133) ;
///   4. un pli d'une surveillance (son evenement de procedure, EVT_PROC, est celui d'une instance qui a une relance RCS :
///      LrarInfo.DiligenceRcsRattachee, Outils\DLL\RigMetier\Edition\LrarInfo.cs:202-212, et IsLRARSurveillance, l. 445-451) :
///      « Ce LRAR doit être traité via le processus RNSURV. » (l. 126, l. 182).
/// ECRIT, pli par pli :
///   - pas encore revenu (TraitementReception, l. 790-842) : LRARI_TYPE_DATE = la modalite, LRARI_DATE_RECEPTION = la date (NULL avec la
///     case) ;
///   - deja revenu (ChangementDateReception, l. 899-984) : la V2 demande « Voulez-vous écraser les anciennes données ... » ; la
///     confirmation de l'operation en tient lieu, puis la date et la modalite sont ecrites et la V2 dit « Date <modalite> mise à jour » ;
///   - dans les deux cas, une modalite « pas a gerer » (PARAM_LRAR_INFOS.PRMLI_A_GERER = 0) remet en emission les demandes d'edition
///     du pli (GetDemandeEditionDicoByNumLrar, l. 825-835 et 960-970) : DMNED_DATE_PRE_EMISSION = 01/01/1900, DMNED_NUM_GROUPE et
///     DMNED_LOT vides, DMNED_PHASE_TRAITEMENT = « E ».
/// L'EVENEMENT FUTUR LIE (lot 11, reprise) : une RECEPTION d'un pli emis par un evenement de procedure d'une INSTANCE (sa demande
/// d'edition, EVT_PROC) dont le futur lie (GetEvenementProcedureFuturLie, EvenementProcedure.cs:1473-1490 : non survenu, non expire,
/// EVNPR_NUMERO_SOURCE_EP_LIE = le numero source de l'evenement, le premier par numero d'ordre) est un accuse de reception d'opposition
/// (IAROIP ou IPAROPOS, typeEvenementPro :610-627) passe ce futur en SURVENU quand tous les AUTRES plis LRAR du meme evenement sont deja
/// recus (OPE_RETOURLRAR_TREEVIEW.CreerNoeudEP, :172-199 ; un pli coche dans le meme geste compte comme recu) :
///   - un autre pli revenu autrement (NPAI, refuse...) : le retour s'ecrit, le futur reste, et la V2 dit « Impossible de mettre à jour
///     l'évènement procédure futur lié <code> de l'instance <role>. Les recommandés liés à cette instance n'ont pas tous été
///     réceptionnés. » (:189) ; un autre pli pas encore revenu : le futur reste, sans message (:192) ;
///   - l'instance verrouillee par une demande (lockInstance, OPE_RETOURLRAR_RECEPTION.cs:759-790, CreateAndLockDemande) : la V2 ne
///     saisit pas le retour et dit « Impossible de mettre à jour le recommandé sur l'instance ¤<role>¤ car il y a une demande en
///     cours. » suivi du motif (:782) -- un refus, avant toute ecriture (VerrouDemandeV2) ;
///   - sinon, apres le retour (PasserEPlieEnSurvenu, :986-1017) : un futur de reception de consignation deja la sur l'instance
///     (ICONSOIP / IPCONSOPOS, :1007-1012) est relie au futur qui passe, et aucun autre futur n'est cree ; puis le futur passe en
///     survenu a la date de reception (aujourd'hui avec « Date non renseignée », :1001), avec son propre futur lie sinon
///     (PasserEnSurvenuEveProFutur, EvenementsProcedureV2.PasserEnSurvenuAsync) ; la V2 dit « Mise à jour de l'instance <role>.
///     L'évènement procédure futur lié <code> est passé en survenu. » (:1019). La demande RETOURLRAR de lockInstance, creee puis
///     validee dans le meme geste (_demande.Cloturer, :1020) : une trace, que le journal des gestes remplace (decision du chef,
///     plan-676a4b19).
/// NON REPRIS (nomme) : le pli dont l'evenement a ete supprime (ChangementDateEveProSupprime, l. 1087) suit la regle commune ; le controle des dates d'un AR d'opposition sur une IP (l. 243-258) ; le courriel a la hotline judiciaire quand
/// une modalite « pas a gerer » change (l. 930-950) -- un envoi externe : NON envoye, la V3 le dit dans son message (simule).
/// </summary>
internal sealed class EcrivainRetoursLrar : IEcrivainEcran
{
    public string Procedure => "PROC_RETOURLRAR";

    public string Ecran => "Retours LRAR";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Confirmer];

    /// <summary>Le bouton du descripteur et la section saisie, au mot pres.</summary>
    internal const string Enregistrer = "Enregistrer le retour", Section = "Retour";

    /// <summary>Les messages de RIG v2, au mot pres (en tete du fichier).</summary>
    internal const string SansNumero = "Veuillez saisir le numéro du recommandé";
    internal const string SansDate = "Si vous n'avez pas de date renseignée, il faut cocher la case 'Date non renseignée'.";
    internal const string Surveillance = "Ce LRAR doit être traité via le processus RNSURV.";

    /// <summary>Les modalites des boutons radio (eTypeReceptionLrar, Description) et leur code ecrit dans LRARI_TYPE_DATE.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Modalites = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Réception"] = "RECEPTION", ["NPAI"] = "NPAI", ["Non réclamé"] = "NON_RECLAME", ["Pli non envoyé"] = "PAS_ENVOYE",
        ["Refusé"] = "REFUSE", ["Incomplet"] = "INCOMPLET",
    };

    // Le mot que la V2 met dans « Date <nouveau> mise à jour » (_MessageDejaSaisi, l. 857-865).
    private static string Nouveau(string code) => code switch
    {
        "RECEPTION" => "réception", "NPAI" => "réception NPAI", "NON_RECLAME" => "'non réclamé'", "PAS_ENVOYE" => "'non envoyé'",
        "INCOMPLET" => "'incomplet'", "REFUSE" => "'refusé'", _ => "",
    };

    private sealed record Pli(string Numero, string Modalite, DateTime? Date);

    /// <summary>Le futur lie d'un pli (en tete) : son instance, son evenement futur, et s'il passe en survenu ou pourquoi il reste.</summary>
    private sealed record Lie(int IdInstance, string Role, int IdFutur, string CodeFutur, string NumeroSourceFutur, bool Passe, string? Message);

    /// <summary>Les accuses de reception d'opposition (EvenementProcedure.TypeEvenement.IPReceptionAROip, :593-594, :623-625) et les
    /// receptions de consignation (IPReceptionConsignation, :587-588, :617-619).</summary>
    private static readonly string[] AccusesOpposition = ["IAROIP", "IPAROPOS"], ReceptionsConsignation = ["ICONSOIP", "IPCONSOPOS"];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Enregistrer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var numeros = geste.Demande.Ids.Select(i => i.Trim()).Where(i => i.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (numeros.Length == 0) return IssueGeste.Refuser(SansNumero);

        var choisie = geste.Valeur(Section, "Modalité de retour");
        var code = string.IsNullOrWhiteSpace(choisie) ? "RECEPTION" : Modalites.GetValueOrDefault(choisie.Trim());
        if (code is null) return IssueGeste.Refuser($"Modalité de retour inconnue de RIG v2 : « {choisie} ».");
        var sansDate = geste.Valeur(Section, "Date non renseignée") == "oui";
        var date = sansDate ? null : CriteresConsultation.Date(geste.Valeur(Section, "Date de réception"));
        if (!sansDate && date is null) return IssueGeste.Refuser(SansDate);

        // Tous les plis sont controles avant la premiere ecriture (un refus n'ecrit rien).
        var plis = new List<Pli>();
        foreach (var n in numeros)
        {
            Pli? pli = null;
            await using (var r = await geste.LireAsync("""
                SELECT l.LRARI_NUM_LRAR, l.LRARI_TYPE_DATE, l.LRARI_DATE_RECEPTION FROM v2.LRAR_INFOS AS l
                WHERE l.LRARI_NUM_LRAR = @n AND l.LRARI_TYPE IN ('LRAR', 'LR')
                  AND EXISTS (SELECT 1 FROM v2.DEMANDE_EDITION AS de WHERE de.DMNED_NUM_LRAR = l.LRARI_NUM_LRAR)
                """, ct, new SqlParameter("@n", n)))
                if (await r.ReadAsync(ct))
                    pli = new Pli(FormeLigne.Texte(r, "LRARI_NUM_LRAR"), FormeLigne.Texte(r, "LRARI_TYPE_DATE"), FormeLigne.DateDe(r, "LRARI_DATE_RECEPTION"));
            if (pli is null) return IssueGeste.Refuser(LecteurRetoursLrar.NonReconnu);
            if (await SurveillanceAsync(geste, pli.Numero, ct)) return IssueGeste.Refuser(Surveillance);
            plis.Add(pli);
        }

        // L'evenement futur lie de chaque pli, pour une RECEPTION (en tete) ; une instance verrouillee refuse avant toute ecriture.
        var lies = new Dictionary<string, Lie>(StringComparer.Ordinal);
        if (code == "RECEPTION")
            foreach (var p in plis)
                if (await LieAsync(geste, p.Numero, numeros, ct) is { } lie)
                {
                    if (lie.Passe && await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance,
                                         lie.IdInstance.ToString(CultureInfo.InvariantCulture), ct) is { } verrou)
                        return IssueGeste.Refuser($"Impossible de mettre à jour le recommandé sur l'instance ¤{lie.Role}¤ car il y a une demande en cours. {verrou}");
                    lies[p.Numero] = lie;
                }

        var parametres = await ParametresAsync(geste, ct);
        var (libelleNouveau, aGerer) = parametres.GetValueOrDefault(code, ("", true));
        var traces = new List<TraceGeste>();
        var dits = new List<string>();
        var passes = new HashSet<int>();
        foreach (var p in plis)
        {
            await geste.EcrireAsync("UPDATE v2.LRAR_INFOS SET LRARI_TYPE_DATE = @c, LRARI_DATE_RECEPTION = @d WHERE LRARI_NUM_LRAR = @n", ct,
                new SqlParameter("@c", code), new SqlParameter("@d", (object?)date ?? DBNull.Value), new SqlParameter("@n", p.Numero));
            var remises = 0;
            if (!aGerer)
                remises = await geste.EcrireAsync("""
                    UPDATE v2.DEMANDE_EDITION SET DMNED_DATE_PRE_EMISSION = @jamais, DMNED_NUM_GROUPE = NULL, DMNED_PHASE_TRAITEMENT = 'E',
                           DMNED_LOT = NULL
                    WHERE DMNED_NUM_LRAR = @n
                    """, ct, new SqlParameter("@jamais", new DateTime(1900, 1, 1)), new SqlParameter("@n", p.Numero));
            var lu = date is { } d ? $"{libelleNouveau} {FormeLigne.Date(d)}" : FormeLigne.Joindre(LecteurRetoursLrar.Sans(libelleNouveau), LecteurRetoursLrar.DateNonPrecisee);
            var deja = p.Modalite.Length > 0;
            var (ancienLibelle, ancienAGerer) = parametres.GetValueOrDefault(p.Modalite, ("", true));
            var courriel = deja && !ancienAGerer && (p.Modalite != code || p.Date != date)
                ? " Courriel de changement à la hotline judiciaire : non envoyé (simulé, RIG v3 n'envoie rien hors du poste)." : "";
            dits.Add((deja ? $"Pli {p.Numero} : date {Nouveau(code)} mise à jour ({lu})." : $"Pli {p.Numero} : {lu}.")
                     + (remises > 0 ? $" {remises} demande{(remises > 1 ? "s" : "")} d'édition remise{(remises > 1 ? "s" : "")} en file d'émission." : "")
                     + courriel);
            traces.Add(new TraceGeste("affaire", $"le retour du recommandé {p.Numero}", p.Numero, deja ? "a modifie" : "a cree",
                deja ? new { Modalite = p.Modalite, Date = FormeLigne.Date(p.Date), Libelle = ancienLibelle } : null,
                new { Modalite = code, Date = FormeLigne.Date(date), Libelle = lu, DemandesRemises = remises }));
            if (!lies.TryGetValue(p.Numero, out var l)) continue;
            if (!l.Passe)
            {
                if (l.Message is { } m) dits.Add(m);
                continue;
            }
            if (!passes.Add(l.IdFutur)) continue;
            var survenu = date ?? geste.Maintenant;
            int? consignation = null;
            await using (var r = await geste.LireAsync($"""
                SELECT TOP (1) EVNPR_ID_EVNPR FROM v2.EVENEMENT_PROCEDURE
                WHERE EVNPR_ID_INSTN = @i AND EVNPR_SURVENU = 0 AND EVNPR_DELAI_EXPIRE = 0
                  AND EVNPR_CODE_EVE_PRO IN ('{string.Join("', '", ReceptionsConsignation)}')
                ORDER BY EVNPR_NUM_ORDRE, EVNPR_ID_EVNPR
                """, ct, new SqlParameter("@i", l.IdInstance)))
                if (await r.ReadAsync(ct)) consignation = r.GetInt32(0);
            if (consignation is { } dejaLa)
                await geste.EcrireAsync("UPDATE v2.EVENEMENT_PROCEDURE SET EVNPR_NUMERO_SOURCE_EP_LIE = @s WHERE EVNPR_ID_EVNPR = @id", ct,
                    new SqlParameter("@s", l.NumeroSourceFutur), new SqlParameter("@id", dejaLa));
            var futur = await EvenementsProcedureV2.PasserEnSurvenuAsync(geste, l.IdFutur, l.IdInstance, l.CodeFutur, l.NumeroSourceFutur, survenu,
                                                                         consignation is null, ct);
            dits.Add($"Mise à jour de l'instance {l.Role}. L'évènement procédure futur lié {l.CodeFutur} est passé en survenu.");
            traces.Add(new TraceGeste("affaire", $"l'évènement {l.CodeFutur} de l'instance {l.Role}", l.IdFutur.ToString(CultureInfo.InvariantCulture),
                "a modifie", new { Survenu = false }, new { Survenu = true, Date = FormeLigne.Date(survenu), FuturCree = futur?.Code, FuturRelie = consignation }));
        }
        return IssueGeste.Ecrit(string.Join(" ", dits), [.. plis.Select(p => p.Numero)], [.. traces]);
    }

    /// <summary>
    /// Le futur lie du pli <paramref name="numero"/> (en tete) : null si le pli n'a pas d'evenement d'instance, ou que son futur lie n'est
    /// pas un accuse de reception d'opposition ; sinon s'il passe en survenu. Les plis <paramref name="duGeste"/> recoivent la meme
    /// reception dans ce geste. Une base sans ces colonnes (un banc) n'a pas de futur lie.
    /// </summary>
    private static async Task<Lie?> LieAsync(GesteEnCours geste, string numero, IReadOnlyCollection<string> duGeste, CancellationToken ct)
    {
        var evenements = await EditionsV2.ColonnesAsync(geste, "v2.EVENEMENT_PROCEDURE", ct);
        var editions = await EditionsV2.ColonnesAsync(geste, "v2.DEMANDE_EDITION", ct);
        if (!new[] { "EVNPR_NUMERO_SOURCE", "EVNPR_ID_INSTN", "EVNPR_NUMERO_SOURCE_EP_LIE", "EVNPR_SURVENU", "EVNPR_DELAI_EXPIRE", "EVNPR_NUM_ORDRE" }.All(evenements.Contains)
            || !editions.Contains("DMNED_CODE_ENTITE_SOURCE") || !editions.Contains("DMNED_VALEUR_ID_SOURCE")) return null;
        int idFutur, idInstance;
        string codeFutur, sourceFutur, source, role;
        await using (var r = await geste.LireAsync("""
            SELECT TOP (1) f.EVNPR_ID_EVNPR, f.EVNPR_CODE_EVE_PRO, f.EVNPR_NUMERO_SOURCE, f.EVNPR_ID_INSTN, ev.EVNPR_NUMERO_SOURCE AS SOURCE_EP,
                   i.INSTN_NUM_ROLE
            FROM v2.DEMANDE_EDITION AS de
            INNER JOIN v2.EVENEMENT_PROCEDURE AS ev ON ev.EVNPR_NUMERO_SOURCE = de.DMNED_VALEUR_ID_SOURCE AND de.DMNED_CODE_ENTITE_SOURCE = 'EVT_PROC'
            INNER JOIN v2.EVENEMENT_PROCEDURE AS f ON f.EVNPR_NUMERO_SOURCE_EP_LIE = ev.EVNPR_NUMERO_SOURCE AND f.EVNPR_ID_INSTN = ev.EVNPR_ID_INSTN
                                                   AND f.EVNPR_SURVENU = 0 AND f.EVNPR_DELAI_EXPIRE = 0
            INNER JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = f.EVNPR_ID_INSTN
            WHERE de.DMNED_NUM_LRAR = @n
            ORDER BY de.DMNED_ID_DMNED, f.EVNPR_NUM_ORDRE, f.EVNPR_ID_EVNPR
            """, ct, new SqlParameter("@n", numero)))
        {
            if (!await r.ReadAsync(ct)) return null;
            (idFutur, codeFutur, sourceFutur, idInstance) = (r.GetInt32(0), FormeLigne.Texte(r, "EVNPR_CODE_EVE_PRO").Trim(),
                FormeLigne.Texte(r, "EVNPR_NUMERO_SOURCE"), Convert.ToInt32(r["EVNPR_ID_INSTN"], CultureInfo.InvariantCulture));
            (source, role) = (FormeLigne.Texte(r, "SOURCE_EP"), FormeLigne.Texte(r, "INSTN_NUM_ROLE"));
        }
        if (!AccusesOpposition.Contains(codeFutur, StringComparer.Ordinal)) return null;

        // Les AUTRES plis LRAR du meme evenement (GetDemandeEditionByValeurIdSource, EVT_PROC, numero de LRAR pose), dans l'ordre.
        var autres = new List<(string Numero, string? Modalite)>();
        await using (var r = await geste.LireAsync("""
            SELECT de.DMNED_NUM_LRAR, l.LRARI_TYPE_DATE FROM v2.DEMANDE_EDITION AS de
            LEFT JOIN v2.LRAR_INFOS AS l ON l.LRARI_NUM_LRAR = de.DMNED_NUM_LRAR
            WHERE de.DMNED_VALEUR_ID_SOURCE = @s AND de.DMNED_CODE_ENTITE_SOURCE = 'EVT_PROC' AND de.DMNED_NUM_LRAR IS NOT NULL
              AND de.DMNED_NUM_LRAR <> @n
            ORDER BY de.DMNED_ID_DMNED
            """, ct, new SqlParameter("@s", source), new SqlParameter("@n", numero)))
            while (await r.ReadAsync(ct))
                autres.Add((FormeLigne.Texte(r, "DMNED_NUM_LRAR"), r["LRARI_TYPE_DATE"] is DBNull ? null : FormeLigne.Texte(r, "LRARI_TYPE_DATE")));
        foreach (var (autre, modalite) in autres)
        {
            if (duGeste.Contains(autre)) continue;
            if (modalite is null) return new Lie(idInstance, role, idFutur, codeFutur, sourceFutur, false, null);
            if (modalite != "RECEPTION")
                return new Lie(idInstance, role, idFutur, codeFutur, sourceFutur, false,
                    $"Impossible de mettre à jour l'évènement procédure futur lié {codeFutur} de l'instance {role}. Les recommandés liés à cette " +
                    "instance n'ont pas tous été réceptionnés.");
        }
        return new Lie(idInstance, role, idFutur, codeFutur, sourceFutur, true, null);
    }

    /// <summary>Le libelle et le drapeau « a gerer » de chaque modalite (PARAM_LRAR_INFOS).</summary>
    private static async Task<Dictionary<string, (string Libelle, bool AGerer)>> ParametresAsync(GesteEnCours geste, CancellationToken ct)
    {
        var parCode = new Dictionary<string, (string, bool)>(StringComparer.Ordinal);
        await using var r = await geste.LireAsync("SELECT PRMLI_CODE, PRMLI_LIBELLE, PRMLI_A_GERER FROM v2.PARAM_LRAR_INFOS", ct);
        while (await r.ReadAsync(ct)) parCode[FormeLigne.Texte(r, "PRMLI_CODE")] = (FormeLigne.Texte(r, "PRMLI_LIBELLE"), r.GetBoolean(2));
        return parCode;
    }

    /// <summary>
    /// Vrai si le pli est celui d'une surveillance : la demande d'edition est issue d'un evenement de procedure (EVT_PROC) d'une
    /// instance qui a une relance RCS (LrarInfo.DiligenceRcsRattachee, LrarInfo.cs:202-212). Une base sans ces tables ou colonnes
    /// (un banc) n'a pas de surveillance.
    /// </summary>
    private static async Task<bool> SurveillanceAsync(GesteEnCours geste, string numero, CancellationToken ct)
    {
        var evenements = await EditionsV2.ColonnesAsync(geste, "v2.EVENEMENT_PROCEDURE", ct);
        var relances = await EditionsV2.ColonnesAsync(geste, "v2.RELANCE_RCS", ct);
        var editions = await EditionsV2.ColonnesAsync(geste, "v2.DEMANDE_EDITION", ct);
        if (!evenements.Contains("EVNPR_NUMERO_SOURCE") || !evenements.Contains("EVNPR_ID_INSTN") || !relances.Contains("RLCRC_ID_INSTN")
            || !editions.Contains("DMNED_CODE_ENTITE_SOURCE") || !editions.Contains("DMNED_VALEUR_ID_SOURCE")) return false;
        await using var r = await geste.LireAsync("""
            SELECT COUNT(*) FROM v2.DEMANDE_EDITION AS de
            INNER JOIN v2.EVENEMENT_PROCEDURE AS ev ON de.DMNED_VALEUR_ID_SOURCE = ev.EVNPR_NUMERO_SOURCE AND de.DMNED_CODE_ENTITE_SOURCE = 'EVT_PROC'
            INNER JOIN v2.RELANCE_RCS AS rr ON rr.RLCRC_ID_INSTN = ev.EVNPR_ID_INSTN
            WHERE de.DMNED_NUM_LRAR = @n
            """, ct, new SqlParameter("@n", numero));
        return await r.ReadAsync(ct) && Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture) > 0;
    }
}
