using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Publications BODACC » (PROC_GESTBOD, forme Operation) : « Valider » enregistre les annonces cochees comme le bouton VALIDER de
/// RIG v2 dans la liste de chacune -- un rejet est place a renvoyer, un certificat de parution est verifie. Mode d'emploi :
/// EcrivainEcran.cs ; les listes : LecteurPublicationsBodacc.cs (plan « RIG v3 100 % fonctionnel », lot 11, 27/11/2026, reprise).
///
/// SOURCES (ancien RIG) :
///   - REJETS : Saisie\OPE_LST_LISTE_REJ.DoSauver (:51-57), en mode « MiseAJourDesRejets » (OPE_LST_MAJ_REJ, _MajRejet = true) : pour
///     chaque ligne cochee « renvoyer », BDCCR_ETAT_BODACC = '3' (« rectif. (rejet) a envoyer »). Une annonce deja a renvoyer (etat 3)
///     n'est plus dans la liste de la V2 : introuvable.
///   - CERTIFICATS DE PARUTION : Saisie\Certificats parution Bodacc\OPE_LST_CERTIF_BOD (DoGenererLesEditions, :49-59) : pour chaque ligne
///     cochee « verifier » dont l'etat n'est pas deja 6, BDCCR_ETAT_BODACC = '6' (« publicite faite et verifiee »). Une annonce deja
///     verifiee n'est pas reecrite (:57) : la V3 la nomme.
///   - CONTROLES NON CONFORMES : la V2 n'en edite que la liste (Saisie\Liste des controles non conformes\OPE_LST_CTRLNC_BOD, :85-95),
///     rien ne s'ecrit : une annonce de cette liste est refusee.
/// CONTROLES : au moins une annonce cochee ; chaque annonce relue DANS la transaction, dans sa liste (la requete du lecteur).
/// LA PARTIE EXTERNE : aucune -- l'envoi au BODACC est le geste de « Envoi JAL / BODACC », qui reprend les annonces a l'etat 3.
/// NON REPRIS (nomme) : les EDITIONS (le certificat par destinataire CERTIF_BOD / CERTIF_BODPR, OPE_LST_CERTIF_BOD.cs:61-89 ; la liste
/// des rejets, OPE_LST_LISTE_REJ.cs:63-106 ; la liste des controles) : la case « editer » de la V2, une deuxieme case par ligne que
/// l'Operation ne porte pas.
/// </summary>
internal sealed class EcrivainPublicationsBodacc : IEcrivainEcran
{
    public string Procedure => "PROC_GESTBOD";

    public string Ecran => "Publications BODACC";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Confirmer];

    /// <summary>Le bouton du descripteur, au mot pres.</summary>
    internal const string Valider = "Valider";

    internal const string SansLigne = "Aucune annonce cochée : rien n'est enregistré.";
    internal const string ControleSansEcriture = "Un contrôle non conforme ne s'enregistre pas : RIG v2 n'en édite que la liste. Décochez-le.";

    private static string Introuvable(string id) => $"Annonce introuvable dans sa liste BODACC ({id}) : relisez la liste.";

    private sealed record Annonce(int Id, char Liste, string Nom, string Etat);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Valider) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser(SansLigne);

        var annonces = new List<Annonce>();
        foreach (var id in ids)
        {
            if (id.Length < 3 || id[1] != '-' || LecteurPublicationsBodacc.RequeteDe(id[0]) is not { } requete
                || !int.TryParse(id[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return IssueGeste.Refuser(Introuvable(id));
            if (id[0] == 'Q') return IssueGeste.Refuser(ControleSansEcriture);
            Annonce? annonce = null;
            await using (var r = await geste.LireAsync(requete.Replace("/*filtre*/", "AND b.BDCCR_ID_BDCRC = @id", StringComparison.Ordinal),
                             ct, new SqlParameter("@plafond", 1), new SqlParameter("@id", n)))
                if (await r.ReadAsync(ct))
                    annonce = new Annonce(n, id[0], LecteurPublicationsBodacc.Ligne(r).Nom, FormeLigne.Texte(r, "BDCCR_ETAT_BODACC"));
            // Une annonce a renvoyer (etat 3) n'est plus dans la liste des rejets de la V2 (en tete).
            if (annonce is null || (annonce.Liste == 'R' && annonce.Etat != "9")) return IssueGeste.Refuser(Introuvable(id));
            annonces.Add(annonce);
        }

        var traces = new List<TraceGeste>();
        var (renvoyees, verifiees, deja) = (0, 0, new List<string>());
        foreach (var a in annonces)
        {
            var idTexte = a.Id.ToString(CultureInfo.InvariantCulture);
            if (a.Liste == 'R')
            {
                await geste.EcrireAsync("UPDATE v2.BODACC SET BDCCR_ETAT_BODACC = '3' WHERE BDCCR_ID_BDCRC = @id AND BDCCR_ETAT_BODACC = '9'", ct,
                                        new SqlParameter("@id", a.Id));
                traces.Add(new TraceGeste("affaire", $"l'annonce BODACC {idTexte} ({a.Nom})", idTexte, "a modifie",
                    new Dictionary<string, string> { ["Etat"] = "9 - rejet Bodacc, à rectifier" },
                    new Dictionary<string, string> { ["Etat"] = "3 - rectif. (rejet) à envoyer" }));
                renvoyees++;
            }
            else if (a.Etat == "6") deja.Add(idTexte);
            else
            {
                await geste.EcrireAsync("UPDATE v2.BODACC SET BDCCR_ETAT_BODACC = '6' WHERE BDCCR_ID_BDCRC = @id AND BDCCR_ETAT_BODACC <> '6'", ct,
                                        new SqlParameter("@id", a.Id));
                traces.Add(new TraceGeste("affaire", $"l'annonce BODACC {idTexte} ({a.Nom})", idTexte, "a modifie",
                    new Dictionary<string, string> { ["Etat"] = a.Etat == "10" ? "10 - rectif. (rejet) fait, à vérifier" : "5 - publicité faite, à vérifier" },
                    new Dictionary<string, string> { ["Etat"] = "6 - publicité faite et vérifiée" }));
                verifiees++;
            }
        }
        if (traces.Count == 0)
            return IssueGeste.Refuser(deja.Count > 1 ? $"Les annonces {string.Join(", ", deja)} sont déjà vérifiées : rien n'est enregistré."
                                                     : $"L'annonce {deja[0]} est déjà vérifiée : rien n'est enregistré.");
        var phrases = new List<string>();
        if (renvoyees > 0)
            phrases.Add($"{(renvoyees > 1 ? $"{renvoyees} annonces" : "1 annonce")} à renvoyer : le prochain envoi BODACC (« Envoi JAL / BODACC ») "
                        + (renvoyees > 1 ? "les reprend." : "la reprend."));
        if (verifiees > 0) phrases.Add($"{(verifiees > 1 ? $"{verifiees} parutions vérifiées" : "1 parution vérifiée")}.");
        if (deja.Count > 0) phrases.Add($"Déjà vérifiée{(deja.Count > 1 ? "s" : "")}, inchangée{(deja.Count > 1 ? "s" : "")} : {string.Join(", ", deja)}.");
        return IssueGeste.Ecrit(string.Join(" ", phrases), [.. ids], [.. traces]);
    }
}
