using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.Numerotation;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurOrdonnancesASigner;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Ordonnances à signer » (PROC_ORDOSIGN) : « Signer la sélection » rend les decisions cochees, dans la base du greffe. Mode
/// d'emploi : EcrivainEcran.cs ; la liste et sa regle d'etat : LecteurOrdonnancesASigner.cs. Plan « RIG v3 100 % fonctionnel »,
/// lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_ORDOSIGN\ETP_ORDONNANCES\OPE_LISTE_ORDONNANCES.cs (proc_ordosign.md, Detail
/// OPE). Cocher « DJ_RENDUE » puis Valider :
///   - la date de la decision est celle de la cellule DATE_DJ, pre-remplie a la date du jour (_date_choix_utilisateur =
///     DateTime.Now, :134 et :409) ; la forme File ne saisit pas de date : la V3 prend ce defaut ;
///   - _DoGenererLignesEditionStandard (:2700-2712) : « if (dj.DCJDC_DATE.isNull) dj.DCJDC_DATE = date saisie » ; la decision entre
///     dans _lstDJMinuteAttribuee si elle n'a pas de numero de minute et que son type de decision a une serie
///     (R_TYPEDECISI_NUM_REPERTOIRE) ;
///   - _DoSauver (:3302-3330) : le numero de minute « Numerotation.Generer(R_TYPEDECISI_NUM_REPERTOIRE, annee, quantieme de la
///     date) », pose s'il manque (DCJDC_NUM_REPERTOIRE.isNull), puis la decision est sauvee. En V3 : num.Attribuer (serie par jour),
///     dans la transaction du geste.
/// CONTROLES, avant toute ecriture, dans l'ordre de RIG v2 (un seul refus et rien n'est ecrit, pas meme pour les autres lignes) :
///   1. un geste connu, au moins une ordonnance cochee ;
///   2. chaque ordonnance est dans la liste a signer (une case de la recherche la charge, DCJDC_DATE IS NULL) ;
///   3. « Juge RCS » : refuse -- sa generation (_DoGenererLignesEditionJugeRCS, :1989+) attribue des numeros de depot DAS/DPR et
///      cree des actes RCS que la V3 ne reprend pas ;
///   4. le verrou de RIG v2 sur l'instance (_DemandeEnCours, :1597-1602), au message de VerrouDemandeV2 ;
///   5. la date (_DoVerifierDate, :1161-1195) : si un numero de minute de 10 caracteres existe deja, la date doit tomber le jour qu'il
///      encode (_VerifierNumMinute, :1197-1214) ; sinon elle doit etre comprise entre la date de saisine du greffe et le jour.
/// Traces : famille « affaire », « a modifie », la date de decision et le numero de minute avant et apres.
///
/// NON REPRIS (nomme) : les editions de la signature (notifications, copies, formule executoire, factures Lot 2 : _DoGenererLignes-
/// EditionStandard, :2215+) et les evenements de procedure (_DoGenererEvenementsProcedure) ; le courriel d'attribution des minutes
/// (Outils.GestionEnvoiMailMinuteAttributionOuAnnulation, :3325) et le courriel automatique (MAIL_AUTO_ORDOSIGN, :3387-3392) -- rien
/// ne sort du poste ; la demande ORDOSIGN par instance, creee a la coche et validee a la sauvegarde (:3339-3384) : une trace,
/// remplacee par le journal des gestes (decision du chef du plan) ; les mesures liees numerotees JMEXP (:3356-3377) ; le changement
/// de juge commissaire (EtreJugeCommEnAttente, :1767-1811, :3329-3336) ; le controle du chef de demande PROAG
/// (_DoVerifierReportAssembleeGenerale, :1234-1261) : le lien code evenement RCS -> parametrage OP RCS (CODE_EVE_RCS) n'est pas
/// dans la base V3 du greffe ; le controle des intervenants factures et des evenements geres (_verifierEvePro, :2482-2530) ;
/// l'alerte « ne pourra pas être imprimée » (:1613-1625, une confirmation : la V3 n'imprime rien).
/// </summary>
internal sealed class EcrivainOrdonnancesASigner : IEcrivainEcran
{
    public string Procedure => "PROC_ORDOSIGN";
    public string Ecran => "Ordonnances à signer";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur, au mot pres (le bouton Valider de FORM_ORDOSIGN).</summary>
    internal const string Signer = "Signer la sélection";

    /// <summary>Le titre de l'etape des ordonnances (OPE_LISTE_ORDONNANCES.cs:444, « Liste des ordonnances à signer ») : RIG v2
    /// l'ecrit en tete du refus de _VerifierNumMinute.</summary>
    private const string TitreEtape = "Liste des ordonnances à signer";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Signer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser("Aucune ordonnance cochée : rien n'est signé.");

        var date = geste.Maintenant.Date;
        var reglages = await ReglagesAsync((sql, p) => geste.LireAsync(sql, ct, p), ct);
        var aSigner = new List<Ordonnance>();
        foreach (var id in ids)
        {
            Ordonnance? o = null;
            if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                o = (await OrdonnancesAsync((sql, p) => geste.LireAsync(sql, ct, p), reglages, date, 1, n, ct)).Lignes.FirstOrDefault();
            if (o is null) return IssueGeste.Refuser($"Ordonnance introuvable dans la liste à signer ({id}).");
            if (o.Date is { } signee) return IssueGeste.Refuser($"L'ordonnance du rôle {o.Role} est déjà signée (décision du {FormeLigne.Date(signee)}).");
            if (o.Groupe == JugeRcs)
                return IssueGeste.Refuser($"L'ordonnance du rôle {o.Role} est un « Juge RCS » : sa signature crée des dépôts RCS que la V3 ne reprend pas, la signer dans RIG v2.");
            if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance,
                    o.IdInstance.ToString(CultureInfo.InvariantCulture), ct) is { } verrou)
                return IssueGeste.Refuser(verrou);
            if (Date(o, date) is { } erreur) return IssueGeste.Refuser(erreur);
            aSigner.Add(o);
        }

        var traces = new List<TraceGeste>();
        var minutes = new List<string>();
        foreach (var o in aSigner)
        {
            await geste.EcrireAsync("UPDATE v2.DECISION_JUDICIAIRE SET DCJDC_DATE = @date WHERE DCJDC_ID_DCJDC = @id AND DCJDC_DATE IS NULL", ct,
                new SqlParameter("@date", date), new SqlParameter("@id", o.Id));
            var minute = o.Minute;
            if (minute.Length == 0 && o.SerieMinute.Length > 0)
            {
                var numero = await geste.AttribuerNumeroAsync(new DemandeNumero(o.SerieMinute, date.Year, date.DayOfYear), ct);
                if (numero.Code != CodesNumerotation.Ok || string.IsNullOrEmpty(numero.Numero))
                    return IssueGeste.Refuser($"Numéro de minute {o.SerieMinute} du rôle {o.Role} : {numero.Message ?? "aucun numéro donné"} (code {numero.Code}).");
                minute = numero.Numero;
                await geste.EcrireAsync("UPDATE v2.DECISION_JUDICIAIRE SET DCJDC_NUM_REPERTOIRE = @minute WHERE DCJDC_ID_DCJDC = @id AND DCJDC_NUM_REPERTOIRE IS NULL", ct,
                    new SqlParameter("@minute", minute), new SqlParameter("@id", o.Id));
                minutes.Add(minute);
            }
            traces.Add(new TraceGeste("affaire", $"l'ordonnance du rôle {o.Role}", o.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new Dictionary<string, string> { ["Date de la décision"] = "", ["Numéro de minute"] = o.Minute },
                new Dictionary<string, string> { ["Date de la décision"] = FormeLigne.Date(date), ["Numéro de minute"] = minute }));
        }

        var nombre = aSigner.Count;
        var message = (nombre > 1 ? $"{nombre} ordonnances signées" : "Ordonnance signée") + $" le {FormeLigne.Date(date)}"
            + (minutes.Count == 0 ? "" : minutes.Count == 1 ? $", minute {minutes[0]}" : $", minutes {string.Join(", ", minutes)}")
            + ". Les éditions et les courriels de la signature ne sont pas produits par la V3.";
        return IssueGeste.Ecrit(message, [.. aSigner.Select(o => o.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }

    /// <summary>Le refus de _DoVerifierDate (OPE_LISTE_ORDONNANCES.cs:1161-1214) pour la date <paramref name="date"/> ; null si elle va.</summary>
    internal static string? Date(Ordonnance o, DateTime date)
    {
        if (o.Minute.Length == 10)
        {
            // _VerifierNumMinute : les deux chiffres de l'annee et le quantieme encodes dans la minute (AAQQQCCCCC).
            if (o.Minute[..2] != date.Year.ToString(CultureInfo.InvariantCulture)[2..]
                || !int.TryParse(o.Minute.AsSpan(2, 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantieme) || quantieme != date.DayOfYear)
            {
                var annee = int.Parse(date.Year.ToString(CultureInfo.InvariantCulture)[..2] + o.Minute[..2], CultureInfo.InvariantCulture);
                var jour = int.TryParse(o.Minute.AsSpan(2, 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var q) ? q : 1;
                var attendue = new DateTime(annee, 1, 1).AddDays(jour - 1);
                return $"{TitreEtape} - attention, un numéro de minute ({o.Minute}) est déjà attribué, la date de décision doit être le {FormeLigne.Date(attendue)}";
            }
            return null;
        }
        if (o.Saisine is { } saisine && date < saisine)
            return $"La date de la décision judiciaire doit être comprise entre la date de saisine du greffe ({FormeLigne.Date(saisine)}) et la date du jour";
        return null;
    }
}
