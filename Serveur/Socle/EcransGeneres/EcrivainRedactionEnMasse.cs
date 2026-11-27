using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.Numerotation;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Rédaction en masse » (PROC_REDA_MASSE) : « Valider au greffe » attribue le numero de minute des decisions cochees deja generees
/// (brouillon ou revision) et les passe « révisé à rendre », dans la base du greffe. Mode d'emploi : EcrivainEcran.cs ; la liste :
/// LecteurRedactionEnMasse.cs. Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_REDA_MASSE\ (proc_reda_masse.md, Detail OPE) -- la case « Validation greffe »
/// de la grille et le pipeline GenererLesDemandesEdition (PROC_REDA_MASSE.cs:265-272) : DocumentREDA.ValidationGreffe ->
/// _AttribuerNumeroMinute (Outils\DLL\RigReda\Public\DocumentREDA.cs:1675-1720) : la date de la decision ET sa date prevue = la date de
/// l'audience du prononce, puis le numero de minute « Numerotation.Generer(R_TYPEDECISI_NUM_REPERTOIRE, annee, quantieme) » (en V3
/// num.Attribuer, serie par jour) ; a la sauvegarde, MajEtatDocs (PROC_REDA_MASSE.cs:385-393) : DocumentREDA.UpdateEtats(ReviseARendre)
/// -- etat de redaction 3, diffusion 1 (non diffusable), stabilite 2 (modifiable avant signature), puis Instance.UpdateCentralisation.
/// CONTROLES, avant toute ecriture (au mot pres de RIG v2) :
///   1. un geste connu, au moins une decision cochee, chacune dans la liste de l'audience ;
///   2. la validation n'est permise qu'a l'etat 1 ou 2, ou avec la generation (CanCheckValidation, OPE_LISTE_DJ_REDA_MASSE.cs:873-875) :
///      une decision a l'etat 0 doit d'abord etre generee -- la generation (Word, modele REDA) n'est pas reprise par la V3 : refus nomme ;
///      une decision a l'etat 3 a deja sa minute ;
///   3. VerifierSaisie (:1231-1268) : « Veuillez saisir un Modèle de Document pour la(les) décision(s) suivante(s): » (le modele du
///      document REDA, DCMDS_CODE_MODELE_DOCUMENT) et « Veuillez saisir un appel affaire de délibéré et/ou de prononcé pour la(les)
///      décision(s) suivante(s): » (DCJDC_ID_APPAF_DELIBERER et _PRONONCER), suivis des numeros de role (RIG v2 passe a la ligne avant
///      eux : le message d'un geste tient sur une ligne) ;
///   4. _AttribuerNumeroMinute : « Le type de numérotation métier n'a pu être déterminé » (type de decision sans serie, :1678-1681).
/// Traces : famille « affaire », « a modifie », l'etat de redaction, la date et la minute avant et apres.
///
/// NON REPRIS (nomme) : la generation du document (GenererDocument, Word), l'impression de la minute (ImpressionREDA.TraiterImpression
/// MinuteSeuleEnMasse) et les depens (RegenererDocument) -- le moteur REDA de RIG v2 ; le courriel d'attribution de la minute
/// (Outils.GestionEnvoiMailMinuteAttributionOuAnnulation) ; la liberation d'une minute deja attribuee avant d'en donner une autre
/// (_RestituerMinutes, :1683-1689) : une decision a l'etat 1 ou 2 qui porte deja une minute est refusee, la valider dans RIG v2 ; le
/// verrou et la demande par instance pris a l'ouverture (CreateAndLockDemande, :68-87) : la V3 controle le verrou de RIG v2 au geste.
/// </summary>
internal sealed class EcrivainRedactionEnMasse : IEcrivainEcran
{
    public string Procedure => "PROC_REDA_MASSE";
    public string Ecran => "Rédaction en masse";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Decider];

    /// <summary>Le geste du descripteur, au mot pres (la case « Validation greffe » de la grille).</summary>
    internal const string Valider = "Valider au greffe";

    private sealed record Decision(int Id, int IdInstance, string Role, string Etat, string Minute, string Serie, string Modele, bool Delibere,
                                   DateTime? Prononce);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Valider) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser("Aucune décision cochée : rien n'est validé.");

        var decisions = new List<Decision>();
        foreach (var id in ids)
        {
            Decision? d = null;
            if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                await using (var r = await geste.LireAsync("""
                    SELECT d.DCJDC_ID_DCJDC, d.DCJDC_ID_INSTN, i.INSTN_NUM_ROLE, d.DCJDC_ETAT_REDACTION, d.DCJDC_NUM_REPERTOIRE, td.R_TYPEDECISI_NUM_REPERTOIRE,
                           doc.DCMDS_CODE_MODELE_DOCUMENT, d.DCJDC_ID_APPAF_DELIBERER, apr.AUDNC_DATE AS Prononce
                    FROM v2.DECISION_JUDICIAIRE AS d
                    JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
                    LEFT JOIN v2.TYPE_DECISION AS td ON td.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
                    LEFT JOIN v2.APPEL_AFFAIRE AS app ON app.APPAF_ID_APPAF = d.DCJDC_ID_APPAF_PRONONCER
                    LEFT JOIN v2.AUDIENCE_CABINET AS apr ON apr.AUDNC_ID_ADNC = app.APPAF_ID_ADNC
                    OUTER APPLY (SELECT TOP (1) x.DCMDS_CODE_MODELE_DOCUMENT FROM v2.DOCUMENT_DOSSIER AS x
                                 WHERE x.DCMDS_VALEUR_ID_METIER = d.DCJDC_ID_DCJDC AND x.DCMDS_CODE_ENTITE = 'REDA_DECI_JUD' ORDER BY x.DCMDS_ID_DCMDS DESC) AS doc
                    WHERE d.DCJDC_ID_DCJDC = @id AND d.DCJDC_ETAT_REDACTION IN ('0', '1', '2', '3') AND d.DCJDC_TYPE_DECISION IS NOT NULL
                    """, ct, new SqlParameter("@id", n)))
                    if (await r.ReadAsync(ct))
                        d = new Decision(n, Convert.ToInt32(r["DCJDC_ID_INSTN"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "INSTN_NUM_ROLE"),
                            FormeLigne.Texte(r, "DCJDC_ETAT_REDACTION"), FormeLigne.Texte(r, "DCJDC_NUM_REPERTOIRE"), FormeLigne.Texte(r, "R_TYPEDECISI_NUM_REPERTOIRE"),
                            FormeLigne.Texte(r, "DCMDS_CODE_MODELE_DOCUMENT"), r["DCJDC_ID_APPAF_DELIBERER"] is not DBNull, FormeLigne.DateDe(r, "Prononce"));
            if (d is null) return IssueGeste.Refuser($"Décision introuvable parmi celles à rédiger de l'audience ({id}).");
            if (d.Etat == "0")
                return IssueGeste.Refuser($"La décision du rôle {d.Role} n'est pas encore générée : la génération du document (modèle REDA, Word) n'est pas reprise par la V3, la générer dans RIG v2.");
            if (d.Etat == "3") return IssueGeste.Refuser($"La décision du rôle {d.Role} a déjà sa minute validée ({d.Minute}).");
            if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance,
                    d.IdInstance.ToString(CultureInfo.InvariantCulture), ct) is { } verrou)
                return IssueGeste.Refuser(verrou);
            decisions.Add(d);
        }
        // 3. VerifierSaisie : la liste des roles en defaut, comme RIG v2 (qui passe a la ligne avant elle : le message d'un geste tient
        // sur une ligne).
        if (decisions.Where(d => d.Modele.Length == 0).Select(d => d.Role).ToList() is { Count: > 0 } sansModele)
            return IssueGeste.Refuser("Veuillez saisir un Modèle de Document pour la(les) décision(s) suivante(s): " + string.Join(", ", sansModele));
        if (decisions.Where(d => !d.Delibere || d.Prononce is null).Select(d => d.Role).ToList() is { Count: > 0 } sansAppel)
            return IssueGeste.Refuser("Veuillez saisir un appel affaire de délibéré et/ou de prononcé pour la(les) décision(s) suivante(s): " + string.Join(", ", sansAppel));
        foreach (var d in decisions)
        {
            if (d.Serie.Length == 0) return IssueGeste.Refuser($"Rôle {d.Role} : Le type de numérotation métier n'a pu être déterminé");
            if (d.Minute.Length > 0)
                return IssueGeste.Refuser($"La décision du rôle {d.Role} porte déjà la minute {d.Minute} : RIG v2 la libère avant d'en donner une autre, ce que la V3 ne fait pas ici ; la valider dans RIG v2.");
        }

        var traces = new List<TraceGeste>();
        var minutes = new List<string>();
        foreach (var d in decisions)
        {
            var date = d.Prononce!.Value.Date;
            var numero = await geste.AttribuerNumeroAsync(new DemandeNumero(d.Serie, date.Year, date.DayOfYear), ct);
            if (numero.Code != CodesNumerotation.Ok || string.IsNullOrEmpty(numero.Numero))
                return IssueGeste.Refuser($"Numéro de minute {d.Serie} du rôle {d.Role} : {numero.Message ?? "aucun numéro donné"} (code {numero.Code}).");
            await geste.EcrireAsync("""
                UPDATE v2.DECISION_JUDICIAIRE SET DCJDC_DATE = @date, DCJDC_DATE_PREVUE = @date, DCJDC_NUM_REPERTOIRE = @minute,
                    DCJDC_ETAT_REDACTION = '3', DCJDC_ETAT_DIFFUSION = '1', DCJDC_ETAT_STABILITE = '2'
                WHERE DCJDC_ID_DCJDC = @id
                """, ct, new SqlParameter("@date", date), new SqlParameter("@minute", numero.Numero), new SqlParameter("@id", d.Id));
            await geste.EcrireAsync("UPDATE v2.INSTANCE SET INSTN_TOP_EXPERIAN = 0, INSTN_DATE_MAJ_INFOG = @m WHERE INSTN_ID_INSTN = @i", ct,
                new SqlParameter("@m", geste.Maintenant), new SqlParameter("@i", d.IdInstance));
            minutes.Add(numero.Numero);
            traces.Add(new TraceGeste("affaire", $"la rédaction de la décision du rôle {d.Role}", d.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { EtatRedaction = d.Etat, Minute = "" }, new { EtatRedaction = "3", Minute = numero.Numero, Date = FormeLigne.Date(date) }));
        }
        var nombre = decisions.Count;
        var message = (nombre > 1 ? $"{nombre} minutes validées au greffe ({string.Join(", ", minutes)})" : $"Minute {minutes[0]} validée au greffe")
            + " : révisé à rendre. L'impression de la minute n'est pas produite par la V3 (moteur REDA de RIG v2).";
        return IssueGeste.Ecrit(message, [.. decisions.Select(d => d.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }
}
