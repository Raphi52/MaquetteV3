using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Remise en banque » (PROC_REMBANQ) : les encaissements de la periode, a remettre ou deja remis. Mode d'emploi : LecteurEcran.cs ;
/// les gestes : EcrivainRemiseEnBanque.cs. Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_REMBANQ (le Detail OPE de proc_rembanq.md fait foi).
///   - LA PERIODE : aujourd'hui (_datePeriode = DateTime.Today, PROC_REMBANQ.cs:13 ; les deux dates posees :32), de 0 h a 24 h
///     (TreeViewModePaiement._GetEncaissement, TreeViewModePaiement.cs:262-263, ToSQL00Heure / ToSQL24Heure). La copie de
///     developpement ne garde que les encaissements les plus anciens : elle n'en a aucun du jour (Copie.cs:351-353) ;
///   - A REMETTRE : ENCMT_REMIS IS NULL (meme requete) -- l'invariant du Detail OPE (« a remettre ⟺ ENCMT_REMIS is null ») ;
///   - LES MODES : R_MODEPAIEME_UTILISABLE_SAISIE = 1, hors REG et PV (OPE_MODEPAIEMENT.cs:43) ; un mode sans encaissement n'a
///     pas de noeud (:250) -- ici, pas de ligne ;
///   - LES UTILISATEURS : tous coches par defaut (OPE_CRITERES.cs:112, GetUsers rend null = aucun filtre, :175). Le choix d'un
///     utilisateur n'est pas repris (la liste n'a pas de critere).
/// AJOUT DE LA V3, dit : les encaissements de la periode DEJA remis (ENCMT_REMIS > 0) restent dans la liste, a l'etat « Remis en
/// banque » avec leur numero de remis : c'est le numero que la V2 fait saisir pour reediter ou annuler un remis
/// (OPE_ACTIONS._controlRemis, OPE_ACTIONS.cs:192-213) -- la V3 le montre au lieu de le faire taper. Un remis negatif (brouillon,
/// RemiseEnBanque.cs:179) n'est jamais ecrit dans ENCAISSEMENT : il n'a pas de ligne.
///
/// ORDRE : a remettre d'abord, puis par libelle de mode (un noeud par mode dans la V2), date, identifiant. La V2 ne trie pas les
/// feuilles (_GetEncaissement n'a pas d'ORDER BY).
/// REGLE D'ETAT : ENCMT_REMIS IS NULL -> AFaire « À remettre » ; remis et ENCMT_DATE_COMPTABILISATION IS NULL -> EnCours « Remis en
/// banque » (encore annulable : l'annulation ne detache que les non comptabilises, OPE_ACTIONS.cs:203-204) ; remis et comptabilise ->
/// Validee « Remis et comptabilisé » (la V2 refuse de l'annuler : « ... est déjà comptabilisé », OPE_ACTIONS.cs:279).
/// fix-ok: mesure a la lecture de web/src/ecrans/formes/commun.tsx:78 (figee = ni AFaire ni EnCours) -- un remis encore
/// annulable mis en Validee ne se cochait plus, et « Annuler le remis » ne pouvait plus le viser : il est EnCours.
/// CELLULES (Colonnes du descripteur) : encaissement, mode de paiement, montant (decimal exact, « 0.00 »), numero de remis.
/// </summary>
internal sealed class LecteurRemiseEnBanque : ILecteurEcran
{
    public string Procedure => "PROC_REMBANQ";

    internal const string Groupe = "Encaissements de la période";

    private const string Requete = """
        SELECT TOP (@plafond) e.ENCMT_ID_ENCMT, e.ENCMT_NUMERO_TITRE, e.ENCMT_DATE_ENCAISSEMENT, e.ENCMT_MONTANT, e.ENCMT_DESIGNATION_PAYEUR,
               e.ENCMT_REMIS, e.ENCMT_DATE_COMPTABILISATION, mp.R_MODEPAIEME_LIBELLE_ECRAN AS Mode, COUNT(*) OVER () AS Total
        FROM v2.ENCAISSEMENT AS e
        INNER JOIN v2.MODE_PAIEMENT AS mp ON mp.R_MODEPAIEME_VALEUR = e.ENCMT_MODE_PAIEMENT
        WHERE e.ENCMT_DATE_ENCAISSEMENT >= @debut AND e.ENCMT_DATE_ENCAISSEMENT < @finExclue
          AND mp.R_MODEPAIEME_UTILISABLE_SAISIE = 1 AND mp.R_MODEPAIEME_VALEUR NOT IN ('REG', 'PV')
          AND (e.ENCMT_REMIS IS NULL OR e.ENCMT_REMIS > 0)
        ORDER BY CASE WHEN e.ENCMT_REMIS IS NULL THEN 0 ELSE 1 END, mp.R_MODEPAIEME_LIBELLE_ECRAN, e.ENCMT_DATE_ENCAISSEMENT, e.ENCMT_ID_ENCMT
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@debut", aujourdhui.Date), new SqlParameter("@finExclue", aujourdhui.Date.AddDays(1)));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var id = FormeLigne.Texte(r, "ENCMT_ID_ENCMT");
        var titre = FormeLigne.Texte(r, "ENCMT_NUMERO_TITRE");
        var montant = CriteresConsultation.Decimal(r, "ENCMT_MONTANT");
        var remis = CriteresConsultation.EntierDe(r, "ENCMT_REMIS");
        var mode = FormeLigne.Texte(r, "Mode");
        var nom = titre.Length > 0 ? $"Encaissement {titre}" : $"Encaissement n° {id}";
        var precisions = FormeLigne.Joindre(mode, CriteresConsultation.Euros(montant), "le " + FormeLigne.Date(FormeLigne.DateDe(r, "ENCMT_DATE_ENCAISSEMENT")),
            FormeLigne.Texte(r, "ENCMT_DESIGNATION_PAYEUR"), remis is { } n ? $"remis n° {n}" : "à remettre");
        var etat = remis is null ? "AFaire" : FormeLigne.DateDe(r, "ENCMT_DATE_COMPTABILISATION") is null ? "EnCours" : "Validee";
        return new LigneLue(nom, precisions, Groupe, etat, [])
        {
            Id = id,
            Valeurs = [nom, mode, CriteresConsultation.MontantFiche(montant), remis?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""],
        };
    }
}
