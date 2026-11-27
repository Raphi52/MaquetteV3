using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Publications BODACC » (PROC_GESTBOD) : « Renvoyer » place les annonces rejetees cochees a renvoyer, comme la mise a jour des
/// rejets de RIG v2. Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurPublicationsBodacc.cs (plan « RIG v3 100 % fonctionnel »,
/// lot 11, 27/11/2026).
///
/// SOURCE (ancien RIG) : Saisie\OPE_LST_LISTE_REJ.DoSauver (:51-57), en mode « MiseAJourDesRejets » (OPE_LST_MAJ_REJ, _MajRejet = true) :
/// pour chaque ligne dont la case « renvoyer » est cochee, BDCCR_ETAT_BODACC = '3' (« rectif. (rejet) a envoyer ») et l'annonce est
/// sauvee. CONTROLES : au moins une annonce cochee ; une annonce de la liste des rejets (etat 9, type P%, la requete du lecteur) -- une
/// annonce deja a renvoyer (etat 3) n'est plus dans la liste de la V2.
/// LA PARTIE EXTERNE : aucune ici -- l'envoi au BODACC est le geste de « Envoi JAL / BODACC », qui reprend les annonces a l'etat 3.
/// NON REPRIS (nomme) : « Vérifier » d'un certificat (etat 6, OPE_LST_CERTIF_BOD.DoGenererLesEditions, :57-59) et les editions
/// (certificats par destinataire, liste des rejets, liste des controles, :49-91 et OPE_LST_LISTE_REJ.cs:63-106) : leurs listes exigent une
/// date ou un numero de parution, que la forme File ne saisit pas (LecteurPublicationsBodacc.cs).
/// </summary>
internal sealed class EcrivainPublicationsBodacc : IEcrivainEcran
{
    public string Procedure => "PROC_GESTBOD";

    public string Ecran => "Publications BODACC";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    internal const string Renvoyer = "Renvoyer";
    internal const string SansLigne = "Aucune annonce cochée : rien n'est renvoyé.";

    private static string Introuvable(string id) => $"Annonce introuvable parmi les rejets BODACC à rectifier ({id}) : relisez la liste.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Renvoyer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser(SansLigne);

        var annonces = new List<(int Id, string Nom)>();
        foreach (var id in ids)
        {
            if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return IssueGeste.Refuser(Introuvable(id));
            LigneLue? ligne = null;
            await using (var r = await geste.LireAsync(LecteurPublicationsBodacc.Requete.Replace("/*annonce*/", "AND b.BDCCR_ID_BDCRC = @id", StringComparison.Ordinal),
                             ct, new SqlParameter("@plafond", 1), new SqlParameter("@id", n)))
                if (await r.ReadAsync(ct)) ligne = LecteurPublicationsBodacc.Ligne(r);
            // Une annonce a renvoyer (etat 3) n'est plus dans la liste des rejets de la V2 (en tete).
            if (ligne is null || ligne.Etat != "AFaire") return IssueGeste.Refuser(Introuvable(id));
            annonces.Add((n, ligne.Nom));
        }

        var traces = new List<TraceGeste>();
        foreach (var (id, nom) in annonces)
        {
            await geste.EcrireAsync("UPDATE v2.BODACC SET BDCCR_ETAT_BODACC = '3' WHERE BDCCR_ID_BDCRC = @id AND BDCCR_ETAT_BODACC = '9'", ct,
                                    new SqlParameter("@id", id));
            traces.Add(new TraceGeste("affaire", $"l'annonce BODACC {id} ({nom})", id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new Dictionary<string, string> { ["Etat"] = "9 - rejet Bodacc, à rectifier" },
                new Dictionary<string, string> { ["Etat"] = "3 - rectif. (rejet) à envoyer" }));
        }
        var nb = annonces.Count;
        return IssueGeste.Ecrit($"{(nb > 1 ? $"{nb} annonces" : "1 annonce")} à renvoyer : le prochain envoi BODACC (« Envoi JAL / BODACC ») "
                                + (nb > 1 ? "les reprend." : "la reprend."),
            [.. annonces.Select(a => a.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }
}
