using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Libérer une affaire » (PROC_LIBAFFAIRE) : TRAITER libere les affaires cochees. Mode d'emploi : EcrivainEcran.cs ; la liste :
/// LecteurAffairesVerrouillees.cs. Plan « RIG v3 100 % fonctionnel », lot 4, 26/11/2026.
///
/// SOURCE (ancien RIG) : OPE_LISTAFFAIRE.UpdateUser (OPE_LISTAFFAIRE.cs:74-86), declenche par le bouton TRAITER
/// (FORM_LIBAFFAIRE.cs:20, :33) : pour chaque ligne cochee, « UPDATE AFFAIRE SET AFFRE_ID_UTLST_EN_COURS = null WHERE
/// AFFRE_ID_AFFRE = &lt;id&gt; » (:79). Aucune autre colonne, aucune confirmation, aucun controle (VERIFIER masque,
/// FORM_LIBAFFAIRE.cs:18) : la liberation est immediate et sans retour.
///
/// CONTROLES : RIG v2 n'en a pas. La V3 refuse seulement ce que la base ne porte pas : un identifiant absent de v2.AFFAIRE
/// (« Affaire introuvable »), ou des affaires TOUTES deja liberees depuis la lecture de la liste (rien a ecrire). Une affaire
/// deja liberee parmi d'autres est sautee, comme l'UPDATE de RIG v2 qui la laisse a NULL.
///
/// CE QUI S'ECRIT : v2.AFFAIRE.AFFRE_ID_UTLST_EN_COURS = NULL, en une transaction pour toutes les affaires (RIG v2 ouvre une
/// connexion par ligne, :80 : un echec au milieu laisse les premieres liberees ; ici tout ou rien). Trace : famille « affaire »,
/// « a modifie », l'utilisateur qui la tenait avant, aucun apres.
/// NON REPRIS (nomme) : la demande LIBAFFAIRE que le moteur de RIG v2 ouvre et clot (une trace sans facture : le journal des
/// gestes la remplace) ; le badge du nombre d'affaires bloquees sur l'onglet (OPE_LISTAFFAIRE.cs:58-64), que la tuile
/// « Verrouillée » de l'ecran compte deja.
/// </summary>
internal sealed class EcrivainAffairesVerrouillees : IEcrivainEcran
{
    public string Procedure => "PROC_LIBAFFAIRE";
    public string Ecran => "Libérer une affaire";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Confirmer];

    private const string Lecture = """
        SELECT a.AFFRE_ID_AFFRE AS Id, a.AFFRE_ID_UTLST_EN_COURS AS Tenue, c.Civilite, c.Prenom, c.Nom
        FROM v2.AFFAIRE AS a WITH (UPDLOCK, HOLDLOCK)
        LEFT JOIN soc.Compte AS c ON c.IdV2 = a.AFFRE_ID_UTLST_EN_COURS
        WHERE a.AFFRE_ID_AFFRE = @id
        """;

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids.Length == 0) return IssueGeste.Refuser("Aucune affaire cochée : rien n'est libéré.");
        var liberees = new List<(string Id, string Utilisateur)>();
        foreach (var id in geste.Demande.Ids.Distinct(StringComparer.Ordinal))
        {
            if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return IssueGeste.Refuser($"Affaire introuvable dans la base du greffe ({id}).");
            string? utilisateur;
            await using (var r = await geste.LireAsync(Lecture, ct, new SqlParameter("@id", n)))
            {
                if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Affaire introuvable dans la base du greffe ({id}).");
                utilisateur = r["Tenue"] is DBNull ? null
                    : string.Join(" ", new[] { FormeLigne.Texte(r, "Civilite"), FormeLigne.Texte(r, "Prenom"), FormeLigne.Texte(r, "Nom") }
                                        .Where(m => m.Length > 0)) is { Length: > 0 } u ? u : $"l'utilisateur n° {FormeLigne.Texte(r, "Tenue")}";
            }
            if (utilisateur is null) continue;   // deja liberee depuis la lecture de la liste : l'UPDATE de RIG v2 n'y change rien
            await geste.EcrireAsync("UPDATE v2.AFFAIRE SET AFFRE_ID_UTLST_EN_COURS = NULL WHERE AFFRE_ID_AFFRE = @id", ct, new SqlParameter("@id", n));
            liberees.Add((id, utilisateur));
        }
        if (liberees.Count == 0)
            return IssueGeste.Refuser("Rien à libérer : les affaires cochées ne sont plus verrouillées (la liste a changé depuis sa lecture).");

        var message = liberees.Count == 1
            ? $"Affaire n° {liberees[0].Id} libérée dans la base du greffe (elle était tenue par {liberees[0].Utilisateur})."
            : $"{liberees.Count} affaires libérées dans la base du greffe : n° {string.Join(", ", liberees.Select(l => l.Id))}.";
        return IssueGeste.Ecrit(message, [.. liberees.Select(l => l.Id)],
            [.. liberees.Select(l => new TraceGeste("affaire", $"l'affaire n° {l.Id}", l.Id, "a modifie",
                new { Verrou = l.Utilisateur }, new { Verrou = (string?)null }))]);
    }
}
