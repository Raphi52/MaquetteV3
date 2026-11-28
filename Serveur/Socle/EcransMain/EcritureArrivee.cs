using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Contrats.Numerotation;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Arrivée du courrier » (PROC_ARRIVEE, écran fait main web/src/ecrans/EcranArrivee.tsx) : le geste ENREGISTRER UNE ARRIVÉE, la
/// saisie OPE_ARRIVEE de RIG v2. Joué par la route POST RoutesArrivee.Enregistrer (ModuleArrivee.cs) dans la transaction et le journal
/// des gestes du socle (EcrivainsEcrans.JouerAsync, sa version pour un écran fait main) : mêmes gardes d'écriture (v2.* seulement).
/// Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026 ; la lecture : LectureArrivees.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source) :
///   - RIG\DLL\Processus\PROC_ARRIVEE\OPE_ARRIVEE.cs : au chargement de la saisie, l'arrivée est CRÉÉE en base, mode « PTT »
///     (DoChargerDonnees, :54-57 ; mode et date proposés : DoInitZoneSaisie, :50-53) ; sa case courrier vaut « A » (:28-31) ; le mode
///     et la date choisis la mettent à jour (ultModeArrivee_ChangementValeur, :58-66 ; ultDateArrivee_ChangementValeur, :67-75) ;
///     PROC_ARRIVEE.DoSauveAfterEtape la sauve à la validation (PROC_ARRIVEE.cs:131-135, _SauverArrivees :167-170) ;
///   - Outils\DLL\RigMetier\Demande\Arrivee.cs, CreateArrivee (:28-42) : ARRV_MODE_ARRIVEE, ARRV_DATEHEURE = maintenant, et le
///     numéro ARRV_NUM_ARRIVEE = Numerotation.Generer(greffe, « ARRD », année, quantième de ARRV_DATEHEURE) -- ici num.Attribuer de la
///     base V3 (geste.AttribuerNumeroAsync, série ARRD par jour, NUM-005_reprise.sql:986), dans la transaction : un geste refusé ne
///     consomme aucun numéro ;
///   - le combo du mode (Rig_Ult_Metier\COMBO\RCS\ULT_COMBO_MODE_ARRIVEE.cs:8-10, Outils.makeSqlStandard :3-18) liste MODE_ARRIVEE
///     avec sa colonne SAISIE = R_MODEARRIVE_UTILISABLE_SAISIE : un mode absent ou non utilisable en saisie n'est pas offert, la V3
///     le refuse (RIG v2 n'a pas de message : la valeur n'est pas dans la liste).
/// LA DATE : celle de la saisie à minuit quand l'agent en choisit une autre que le jour (le Ult_Date de RIG v2 porte une date seule,
/// :52, :67-75) ; le jour même, l'instant de la création (CreateArrivee, :33). Le numéro est toujours celui du jour de la saisie (:34).
/// NON REPRIS (nommés, compte rendu du lot 7) : la DEMANDE du processus ARRIVEE et l'AFFAIRE créée au chargement (OPE_ARRIVEE.cs:55-56,
/// PROC_ARRIVEE._SauverDemandeArrivee :171-176), puis les demandes saisies sous l'arrivée (OPE_DEMANDE, jusqu'à 99) et leurs éditions
/// (attestation provisoire de dépôt, bordereau de numérisation : PROC_ARRIVEE.DoGenererLesDemandesEditions :77-130) : ce geste
/// enregistre l'arrivée seule, comme la V2 l'écrit au chargement de la saisie.
/// TRACE : famille « demande », « a cree ».
/// </summary>
public static class EcritureArrivee
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Arrivée du courrier";

    /// <summary>Le mode proposé par RIG v2 (OPE_ARRIVEE.DoInitZoneSaisie, :51).</summary>
    public const string ModeParDefaut = "PTT";

    /// <summary>La case courrier d'une arrivée saisie (OPE_ARRIVEE.cs:30).</summary>
    public const string CaseCourrier = "A";

    /// <summary>La série des numéros d'arrivée (Arrivee.cs:34).</summary>
    public const string Serie = "ARRD";

    /// <summary>Les gestes écrits : tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Creer];

    /// <summary>Enregistre l'arrivée du mode et de la date saisis (Valeurs du geste).</summary>
    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var mode = (Valeur(geste, RoutesArrivee.ChampMode) is { Length: > 0 } m ? m : ModeParDefaut).ToUpperInvariant();
        DateTime? date = null;
        if (Valeur(geste, RoutesArrivee.ChampDate) is { Length: > 0 } texte)
        {
            if (!DateTime.TryParseExact(texte, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return IssueGeste.Refuser($"« {RoutesArrivee.ChampDate} » n'est pas une date : {texte}.");
            date = d;
        }

        string libelle;
        await using (var r = await geste.LireAsync(
            "SELECT R_MODEARRIVE_LIBELLE_ECRAN FROM v2.MODE_ARRIVEE WHERE R_MODEARRIVE_VALEUR = @mode AND R_MODEARRIVE_UTILISABLE_SAISIE = 1",
            ct, new SqlParameter("@mode", mode)))
        {
            if (!await r.ReadAsync(ct))
                return IssueGeste.Refuser($"Le mode d'arrivée « {mode} » n'est pas dans la liste des modes utilisables en saisie (table MODE_ARRIVEE).");
            libelle = FormeLigne.Texte(r, "R_MODEARRIVE_LIBELLE_ECRAN").Trim();
        }

        var maintenant = geste.Maintenant;
        var n = await geste.AttribuerNumeroAsync(new DemandeNumero(Serie, maintenant.Year, maintenant.DayOfYear), ct);
        if (n.Code != CodesNumerotation.Ok || string.IsNullOrEmpty(n.Numero))
            return IssueGeste.Refuser($"Numérotation {Serie} : {n.Message ?? "aucun numéro donné"} (code {n.Code}). L'arrivée n'est pas enregistrée.");

        var instant = date is { } jour && jour.Date != maintenant.Date ? jour.Date : maintenant;
        var id = await geste.InsererAsync("v2.ARRIVEE", "ARRV_ID_ARRV", new Dictionary<string, object?>
        {
            ["ARRV_NUM_ARRIVEE"] = n.Numero,
            ["ARRV_MODE_ARRIVEE"] = mode,
            ["ARRV_DATEHEURE"] = instant,
            ["ARRV_CASE_COURRIER"] = CaseCourrier,
            ["ARRV_PAIEMENT_JOINT"] = false,
        }, ct);

        var apres = new { Numero = n.Numero, Mode = mode, DateHeure = instant.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), Case = CaseCourrier };
        return IssueGeste.Ecrit($"Arrivée n°{n.Numero} enregistrée ({libelle}, {instant:dd/MM/yyyy}).", [n.Numero],
            new TraceGeste("demande", $"l'arrivée {n.Numero}", id.ToString(CultureInfo.InvariantCulture), "a cree", null, apres));
    }

    private static string? Valeur(GesteEnCours geste, string cle)
        => geste.Demande.Valeurs.TryGetValue(cle, out var v) ? v?.Trim() : null;
}
