using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.Numerotation;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Impression légale » (PROC_IMPLEG) : « Imprimer la sélection » fait ce que RIG v2 ecrit a la validation de l'impression legale --
/// la decision « Révisé à rendre » passe « Rendu avant signature », et l'expertise liee recoit son numero -- dans la base du greffe.
/// Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurImpressionLegale.cs. Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_IMPLEG\PROC_IMPLEG\Etapes - Operations\OPE_IMPLEG.cs (proc_impleg.md, Detail
/// OPE) -- _ValidationSauver (:405-447), pour chaque decision cochee :
///   - si son type de decision a un type de mesure lie (R_TYPEDECISI_TYPE_MESURE_LIE : une expertise) : la mesure de la decision
///     recoit la date d'ouverture = la date de la decision et, sans numero, le numero JMEXP de l'annee de la decision
///     (Numerotation.Generer(« JMEXP », annee), :421-424 ; en V3 num.Attribuer) ;
///   - si elle est « Révisé à rendre » (3) : DocumentREDA.UpdateEtats(RenduAvantSignature) (Outils\DLL\RigReda\Public\DocumentREDA.cs:
///     428-470) -- etat de redaction 4, diffusion 2 (copie ou expedition), stabilite 2 (modifiable avant signature), puis
///     Instance.UpdateCentralisation (INSTN_TOP_EXPERIAN = 0, INSTN_DATE_MAJ_INFOG = maintenant).
/// CONTROLES : un geste connu ; « Veuillez sélectionner au moins une décision pour continuer » (DoVerifier, :299-301, au mot pres) ;
/// chaque decision est dans la liste (etat 3 ou 4, sur l'appel de son prononce).
/// Traces : famille « affaire », « a modifie », l'etat de redaction et le numero d'expertise avant et apres.
///
/// NON REPRIS (nomme) : les editions de l'impression legale -- ImpressionREDA.TraiterImpression(ImpressionLegaleEnMasse) produit les
/// documents REDA (Word) et leurs demandes d'edition, regroupees sous « Impression des minutes » (:364-404) : la V3 n'a pas le moteur
/// REDA, RIEN N'EST IMPRIME (le message le dit) ; la regeneration des depens (case « dépens », :315-328) ; l'imprimante
/// (REDA_IMPRIMANTE_IMPLEG) ; la demande IMPLEG par instance (type 2J), creee et validee d'un coup : une trace, remplacee par le
/// journal des gestes ; la creation d'une mesure absente (AddMesure, :411-415 -- « code à laisser temporairement ») : sans mesure,
/// pas d'expertise a numeroter ; l'ecran recapitulatif des expertises (ETP_RECAP) : le message les donne.
/// </summary>
internal sealed class EcrivainImpressionLegale : IEcrivainEcran
{
    public string Procedure => "PROC_IMPLEG";
    public string Ecran => "Impression légale";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Decider];

    /// <summary>Le geste du descripteur, au mot pres (le bouton EDITION de FORM_IMPLEG).</summary>
    internal const string Imprimer = "Imprimer la sélection";

    private sealed record Decision(int Id, int IdInstance, string Role, string Etat, DateTime? Date, string TypeMesure, int? IdMesure, string NumeroMesure);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Imprimer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser("Veuillez sélectionner au moins une décision pour continuer");

        var decisions = new List<Decision>();
        foreach (var id in ids)
        {
            Decision? d = null;
            if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                await using (var r = await geste.LireAsync("""
                    SELECT d.DCJDC_ID_DCJDC, d.DCJDC_ID_INSTN, i.INSTN_NUM_ROLE, d.DCJDC_ETAT_REDACTION, ISNULL(d.DCJDC_DATE, d.DCJDC_DATE_PREVUE) AS DateDecision,
                           td.R_TYPEDECISI_TYPE_MESURE_LIE, m.MSINS_ID_MSINS, m.MSINS_NUM_MESURE
                    FROM v2.DECISION_JUDICIAIRE AS d
                    JOIN v2.APPEL_AFFAIRE AS ap ON ap.APPAF_ID_APPAF = d.DCJDC_ID_APPAF_PRONONCER
                    JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
                    JOIN v2.TYPE_DECISION AS td ON td.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
                    OUTER APPLY (SELECT TOP (1) x.MSINS_ID_MSINS, x.MSINS_NUM_MESURE FROM v2.MESURE AS x WHERE x.MSINS_ID_DCJDC = d.DCJDC_ID_DCJDC ORDER BY x.MSINS_ID_MSINS) AS m
                    WHERE d.DCJDC_ID_DCJDC = @id AND (d.DCJDC_ETAT_REDACTION = '3' OR d.DCJDC_ETAT_REDACTION = '4')
                    """, ct, new SqlParameter("@id", n)))
                    if (await r.ReadAsync(ct))
                        d = new Decision(n, Convert.ToInt32(r["DCJDC_ID_INSTN"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "INSTN_NUM_ROLE"),
                            FormeLigne.Texte(r, "DCJDC_ETAT_REDACTION"), FormeLigne.DateDe(r, "DateDecision"), FormeLigne.Texte(r, "R_TYPEDECISI_TYPE_MESURE_LIE"),
                            r["MSINS_ID_MSINS"] is DBNull ? null : Convert.ToInt32(r["MSINS_ID_MSINS"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "MSINS_NUM_MESURE"));
            if (d is null) return IssueGeste.Refuser($"Décision introuvable parmi celles à imprimer de l'audience ({id}).");
            decisions.Add(d);
        }

        var traces = new List<TraceGeste>();
        var expertises = new List<string>();
        foreach (var d in decisions)
        {
            var numero = d.NumeroMesure;
            if (d.TypeMesure.Length > 0 && d.IdMesure is { } mesure)
            {
                if (numero.Length == 0)
                {
                    var annee = (d.Date ?? geste.Maintenant).Year;
                    var n = await geste.AttribuerNumeroAsync(new DemandeNumero("JMEXP", annee), ct);
                    if (n.Code != CodesNumerotation.Ok || string.IsNullOrEmpty(n.Numero))
                        return IssueGeste.Refuser($"Numéro d'expertise JMEXP {annee} du rôle {d.Role} : {n.Message ?? "aucun numéro donné"} (code {n.Code}).");
                    numero = n.Numero;
                    expertises.Add($"{d.Role} : expertise {numero}");
                }
                await geste.EcrireAsync("UPDATE v2.MESURE SET MSINS_DATE_OUVERTURE = @date, MSINS_NUM_MESURE = @num WHERE MSINS_ID_MSINS = @m", ct,
                    new SqlParameter("@date", (object?)d.Date ?? DBNull.Value), new SqlParameter("@num", numero), new SqlParameter("@m", mesure));
            }
            if (d.Etat == "3")
            {
                await geste.EcrireAsync("UPDATE v2.DECISION_JUDICIAIRE SET DCJDC_ETAT_REDACTION = '4', DCJDC_ETAT_DIFFUSION = '2', DCJDC_ETAT_STABILITE = '2' WHERE DCJDC_ID_DCJDC = @id",
                    ct, new SqlParameter("@id", d.Id));
                await geste.EcrireAsync("UPDATE v2.INSTANCE SET INSTN_TOP_EXPERIAN = 0, INSTN_DATE_MAJ_INFOG = @m WHERE INSTN_ID_INSTN = @i", ct,
                    new SqlParameter("@m", geste.Maintenant), new SqlParameter("@i", d.IdInstance));
            }
            traces.Add(new TraceGeste("affaire", $"l'impression légale de la décision du rôle {d.Role}", d.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { EtatRedaction = LecteurImpressionLegale.LibelleEtat(d.Etat, ""), Expertise = d.NumeroMesure },
                new { EtatRedaction = LecteurImpressionLegale.LibelleEtat("4", ""), Expertise = numero }));
        }
        var nombre = decisions.Count;
        var message = (nombre > 1 ? $"{nombre} décisions passées" : "Décision passée") + " « Impression légale effectuée »"
            + (expertises.Count == 0 ? "" : $" ; {string.Join(", ", expertises)}")
            + ". Rien n'est imprimé : la V3 n'a pas le moteur REDA de RIG v2 qui produit les documents.";
        return IssueGeste.Ecrit(message, [.. decisions.Select(d => d.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }
}
