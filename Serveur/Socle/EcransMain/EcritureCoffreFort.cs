using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Coffre-fort » (PROC_COFFRE_FORT, écran fait main web/src/ecrans/EcranCoffreFort.tsx) : le geste RE-TRANSMETTRE des dépôts déjà
/// transmis au coffre-fort électronique (bouton « Re-transmettre » de RIG v2). Joué par la route POST RoutesCoffreFort.Retransmission
/// (ModuleCoffreFort.cs) dans la transaction et le journal des gestes du socle (EcrivainsEcrans.JouerAsync). Plan « RIG v3 complet, mesuré
/// par le banc du parcours complet », lot 10, 28/11/2026 ; la lecture : LectureCoffreFort.cs (« Déjà transmis », DEMAT_CFEC).
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RIG\DLL\Processus\PROC_COFFRE_FORT) :
///   - le BOUTON (FORM_COFFRE_FORT.cs:30, :76-80) appelle OPE_TREEVIEW_RETRANSMETTRE.reTransmettre (Recherche\Resultat\
///     OPE_TREEVIEW_RETRANSMETTRE.cs:507-640) sur les dépôts COCHÉS de l'arbre « à re-transmettre », celui des entités qui ont déjà un
///     dépôt DEMAT_CFEC (RemplitTreeview :99, idCFEC renseigné) ;
///   - une ligne en ERREUR ne se coche pas (RemplitTreeview :99-127, ses lignes « erreur » sont désactivées) : un dépôt à l'état 2 ou 3
///     (« En attente de transmission », :125-126), un évènement ou une décision numérisée sans document dans la GED (« Document non
///     numérisé », :103-106), une décision sans copie générée (« Pas de copie générée », :108-111) -- la V3 refuse, rien d'écrit ;
///   - l'ÉCRITURE de chaque dépôt (:583-612) : CFEC_USER_TRAITEMENT_RIG = l'utilisateur (son nom, « . » remplacé par « _ »),
///     CFEC_DATE_TRAITEMENT_RIG = maintenant, CFEC_ETAT = « 2 » (« Traité par le greffe » : le service du coffre-fort le reprend),
///     CFEC_GUID_DOCUMENT = le DERNIER document de l'entité : la copie JGTC de la décision (DEMANDE_EDITION, :589-590), l'index GED de
///     l'évènement hors audience et cabinet (VIDIGED_INDEX.VGIDX_ID_EVNPR, :591-592) ou de la décision numérisée (VGIDX_ID_DCJDC, :593-594) ;
///     pour un autre type (édition, audience), RIG v2 n'en cherche pas et pose un document vide (:588, :604) : la V3 fait de même.
///     selectOneString lit la première ligne sans ordre : la V3 prend l'index le plus récent (VGIDX_ID_VGIDX le plus grand).
/// NON REPRIS (nommés) : le compte rendu Word « Transmission effectuée le … par … » enregistré dans le dossier commun CoffreFort du greffe
/// (:532-570, :620-635), document de poste ; le contrôle « Nature Doc Ged non paramétré » (:114-117, correspondance Experian de
/// VIDIGED_PARAMETRAGE et DEMAT_CFEC_PARAMETRAGE) : un dépôt de l'historique a déjà été transmis avec sa nature (CFEC_NATURE_DOC et le
/// nom du document dans le coffre en viennent) ; l'envoi lui-même, fait par le service du coffre-fort électronique (externe, jamais appelé).
/// TRACE : famille « affaire », « a modifie », une par dépôt.
/// </summary>
public static class EcritureCoffreFort
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Coffre-fort";

    /// <summary>L'état posé : « 2 - Traité par le greffe » (OPE_TREEVIEW_RETRANSMETTRE.cs:585).</summary>
    public const string EtatTraite = "2";

    /// <summary>Les gestes écrits : Agir « Re-transmettre » ; tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Agir];

    /// <summary>Re-transmet les dépôts Ids.</summary>
    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != RoutesCoffreFort.Retransmettre)
            return IssueGeste.Refuser($"« {geste.Demande.Nom} » n'est pas un geste de cet écran : seul « {RoutesCoffreFort.Retransmettre} » s'enregistre.");
        if (EditionsEtatsV2.Entiers(geste.Demande.Ids.Select(i => i?.Trim() ?? "")) is not { Length: > 0 } ids)
            return IssueGeste.Refuser("Cochez au moins un dépôt de l'historique : rien n'est re-transmis.");

        var utilisateur = geste.Agent.CompteWindows.Replace(".", "_", StringComparison.Ordinal).Trim();
        if (utilisateur.Length > 255) utilisateur = utilisateur[..255];
        var maintenant = geste.Maintenant;
        var traces = new List<TraceGeste>();
        foreach (var id in ids)
        {
            string etat, type, numero, nature;
            int entite;
            Guid? avant;
            await using (var r = await geste.LireAsync("""
                SELECT CFEC_ETAT, CFEC_TYPE_ENTITE, CFEC_ID_ENTITE, CFEC_NUM_ENTITE, CFEC_NATURE_DOC, CFEC_GUID_DOCUMENT
                FROM v2.DEMAT_CFEC WITH (UPDLOCK) WHERE CFEC_ID_CFEC = @id
                """, ct, new SqlParameter("@id", id)))
            {
                if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Dépôt introuvable dans l'historique du coffre-fort (n° {id}) : rien n'est re-transmis.");
                etat = FormeLigne.Texte(r, "CFEC_ETAT").Trim();
                type = FormeLigne.Texte(r, "CFEC_TYPE_ENTITE").Trim();
                entite = r["CFEC_ID_ENTITE"] is int e ? e : 0;
                numero = FormeLigne.Texte(r, "CFEC_NUM_ENTITE").Trim();
                nature = FormeLigne.Texte(r, "CFEC_NATURE_DOC").Trim();
                avant = r["CFEC_GUID_DOCUMENT"] is Guid g ? g : null;
            }
            var quoi = $"le dépôt {numero} {nature}".TrimEnd();
            if (etat is "2" or "3")
                return IssueGeste.Refuser($"{Majuscule(quoi)} : En attente de transmission. Rien n'est re-transmis.");

            Guid? document = null;
            if (type == "DECISION_JUDICIAIRE")
            {
                document = await GuidAsync(geste, """
                    SELECT TOP (1) TRY_CAST(CAST(de.DMNED_NUM_DOCUMENT_EDITION AS varchar(40)) AS uniqueidentifier)
                    FROM v2.DEMANDE_EDITION AS de INNER JOIN v2.DECISION_JUDICIAIRE AS dj ON de.DMNED_VALEUR_ID_SOURCE = CAST(dj.DCJDC_ID_DCJDC AS varchar(40))
                    WHERE dj.DCJDC_ID_DCJDC = @entite AND (de.DMNED_CODE_ENTITE_SOURCE = 'reda_deci_jud' OR de.DMNED_CODE_ENTITE_SOURCE = 'DECISION_JUD')
                      AND de.DMNED_TYPE_DOCUMENT = 'JGTC'
                    ORDER BY de.DMNED_ID_DMNED DESC
                    """, entite, ct);
                if (document is null) return IssueGeste.Refuser($"{Majuscule(quoi)} : Pas de copie générée. Rien n'est re-transmis.");
            }
            else if (type == "EVENEMENT_PROCEDURE")
            {
                document = await GuidAsync(geste, """
                    SELECT TOP (1) x.VGIDX_GUID_DOCUMENT
                    FROM v2.EVENEMENT_PROCEDURE AS ep LEFT JOIN v2.VIDIGED_INDEX AS x
                      ON x.VGIDX_ID_EVNPR = ep.EVNPR_ID_EVNPR AND x.VGIDX_TYPE_ENTITE <> 'AUDIENCE' AND x.VGIDX_TYPE_ENTITE <> 'CABINET'
                    WHERE ep.EVNPR_ID_EVNPR = @entite
                    ORDER BY x.VGIDX_ID_VGIDX DESC
                    """, entite, ct);
                if (document is null) return IssueGeste.Refuser($"{Majuscule(quoi)} : Document non numérisé. Rien n'est re-transmis.");
            }
            else if (type == "DECISION_JUDICIAIRE_NUM")
            {
                document = await GuidAsync(geste, """
                    SELECT TOP (1) x.VGIDX_GUID_DOCUMENT
                    FROM v2.DECISION_JUDICIAIRE AS dj LEFT JOIN v2.VIDIGED_INDEX AS x ON x.VGIDX_ID_DCJDC = dj.DCJDC_ID_DCJDC
                    WHERE dj.DCJDC_ID_DCJDC = @entite
                    ORDER BY x.VGIDX_ID_VGIDX DESC
                    """, entite, ct);
                if (document is null) return IssueGeste.Refuser($"{Majuscule(quoi)} : Document non numérisé. Rien n'est re-transmis.");
            }

            await geste.EcrireAsync("""
                UPDATE v2.DEMAT_CFEC SET CFEC_USER_TRAITEMENT_RIG = @utilisateur, CFEC_DATE_TRAITEMENT_RIG = @maintenant, CFEC_ETAT = @etat,
                    CFEC_GUID_DOCUMENT = @document
                WHERE CFEC_ID_CFEC = @id
                """, ct, new SqlParameter("@utilisateur", utilisateur), new SqlParameter("@maintenant", maintenant),
                new SqlParameter("@etat", EtatTraite), new SqlParameter("@document", (object?)document ?? DBNull.Value), new SqlParameter("@id", id));
            traces.Add(new TraceGeste("affaire", quoi, id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { Etat = etat, Document = avant },
                new { Etat = EtatTraite, Document = document, Traitement = maintenant.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), Utilisateur = utilisateur }));
        }

        return IssueGeste.Ecrit(ids.Length == 1
                ? "1 dépôt re-transmis : il est à l'état « 2 - Traité par le greffe », le service du coffre-fort le reprend."
                : $"{ids.Length} dépôts re-transmis : ils sont à l'état « 2 - Traité par le greffe », le service du coffre-fort les reprend.",
            [.. ids.Select(i => i.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }

    private static async Task<Guid?> GuidAsync(GesteEnCours geste, string sql, int entite, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(sql, ct, new SqlParameter("@entite", entite));
        return await r.ReadAsync(ct) && r.GetValue(0) is Guid g ? g : null;
    }

    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
