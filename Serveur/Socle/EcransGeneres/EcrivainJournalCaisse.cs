using System.Globalization;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Journal de caisse » (PROC_JCAISSE) : « Éditer » pose le journal des encaissements affiches dans la FILE DES EDITIONS. Mode d'emploi :
/// EcrivainEcran.cs ; les lignes : LecteurJournalCaisse.cs (Id = l'encaissement) ; l'etat et le droit : EditionsEtatsV2.cs (en tete).
/// Plan-f4fb097f, lot 4, 28/11/2026.
///
/// SOURCE (ancien RIG) : FORM_JCAISSE.OnButtonClick (EDITER, seul bouton : FORM_JCAISSE.cs:23-25, :42-48) -> OPE_CRITERESGENERAUX.GetData
/// (D:\RigApplication\Source\RIG\DLL\Processus\PROC_JCAISSE\PROC_JCAISSE\OPE_CRITERESGENERAUX.cs:143-221) -> OPE_METHODETRI.Editer
/// (OPE_METHODETRI.cs:83-109) -> JournalCaisse.LancerJournalCaisse puis AfficherCrystal (DllRapportCrystal\RigCrystalFactu\JournalCaisse.cs).
/// CONTROLES, avant toute ecriture :
///   1. le droit d'executer JCAISSE (EditionsEtatsV2.ProcessusAutoriseAsync ; FORM_ACCUEIL.cs:1624, au mot pres) ;
///   2. au moins un encaissement : sinon « Aucun élément trouvé » (JournalCaisse.cs:216-218, au mot pres). Les gardes « au moins un
///      utilisateur » / « au moins un mode de paiement » (OPE_CRITERESGENERAUX.cs:178, :199-200) n'ont pas d'objet : la V3 n'a pas ces
///      listes a cocher, un filtre vide vaut « tous » (LecteurJournalCaisse, en tete) ;
///   3. chaque ligne est au journal : l'encaissement existe, son mode est dans MODE_PAIEMENT, et il a une ventilation sur une affaire et un
///      client de la base (les INNER JOIN de GetData, :168-175, comme le lecteur) -- sinon refus, message sans source V2.
/// L'ETAT : titre « Journal de caisse de la période du jj/mm/aaaa au jj/mm/aaaa » (JournalCaisse.cs:186) ; l'etat Crystal du tri
/// (OPE_METHODETRI.cs:85-98 -> JournalCaisse.cs:188-201) : « Par mode de paiement » (defaut, _InitValeur :56) Expresso_JCaisseMode.rpt,
/// « Par utilisateur » Expresso_JCaisse.rpt, « Par montant » Expresso_JCaisseParMontant.rpt, « Par date » Expresso_JCaisseParDate.rpt --
/// la V2 envoie « Par date » au mode 3 (ParMontant, ecart de la fiche, proc_jcaisse.md:291) ; la V3 fait ce que le libelle dit, comme son
/// lecteur. REQUETE : la requete de GetData (:168-175), sur les encaissements edites. TOTAL : la somme des ENCMT_MONTANT, un encaissement
/// compte une fois (le lecteur, en tete).
/// LA PERIODE ET LE TRI : la saisie « Période » (« du|au ») et « Tri » si l'ecran les envoie ; l'Etat de la V3 n'envoie que les lignes
/// affichees (FormeEtat.tsx, un geste par groupe) : la periode est alors celle des encaissements edites (du premier au dernier), le tri
/// celui de la V2 par defaut. NON REPRIS (nommes) : la mention « (Utilisateur : nom prenom) » d'un journal a un seul agent coche
/// (JournalCaisse.cs:187, oneUser) -- la V3 n'a pas la liste des agents coches.
/// TRACE : famille « demande », « a cree », l'etat pose (titre, etat Crystal, nombre d'encaissements, total).
/// </summary>
internal sealed class EcrivainJournalCaisse : IEcrivainEcran
{
    public string Procedure => "PROC_JCAISSE";
    public string Ecran => "Journal de caisse";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    internal const string Editer = "Éditer", Processus = "JCAISSE";
    internal const string HorsJournal = "Un encaissement n'est plus au journal de caisse (supprimé, sans ventilation sur une affaire et un client, ou mode de paiement inconnu) : relisez le journal.";

    private static readonly IReadOnlyDictionary<string, string> EtatParTri = new Dictionary<string, string>
    {
        [LecteurJournalCaisse.TriMode] = "Expresso_JCaisseMode.rpt", [LecteurJournalCaisse.TriUtilisateur] = "Expresso_JCaisse.rpt",
        [LecteurJournalCaisse.TriMontant] = "Expresso_JCaisseParMontant.rpt", [LecteurJournalCaisse.TriDate] = "Expresso_JCaisseParDate.rpt",
    };

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Editer) return IssueGeste.Refuser($"Geste « {geste.Demande.Nom} » inconnu du journal de caisse : RIG v2 n'a que « {Editer} ».");
        // 1. Le droit d'executer le processus.
        if (!await EditionsEtatsV2.ProcessusAutoriseAsync(geste, Processus, ct)) return IssueGeste.Refuser(EditionsEtatsV2.NonAutorise(Processus));
        // 2. Au moins un encaissement.
        if (geste.Demande.Ids.Length == 0) return IssueGeste.Refuser(EditionsEtatsV2.AucunElement);
        if (EditionsEtatsV2.Entiers(geste.Demande.Ids) is not { } ids) return IssueGeste.Refuser(HorsJournal);
        var tri = geste.Demande.Valeurs.GetValueOrDefault(LecteurJournalCaisse.CritereTri) is { Length: > 0 } t ? t : LecteurJournalCaisse.TriMode;
        if (!EtatParTri.TryGetValue(tri, out var nomRpt))
            return IssueGeste.Refuser($"« {LecteurJournalCaisse.CritereTri} » : {string.Join(", ", EtatParTri.Keys)} ({tri} saisi).");

        // 3. Chaque ligne au journal (les jointures de GetData) ; les ids sont des entiers relus ci-dessus.
        var liste = string.Join(", ", ids);
        var dates = new List<DateTime>();
        var total = 0m;
        await using (var r = await geste.LireAsync($"""
            SELECT e.ENCMT_DATE_ENCAISSEMENT, e.ENCMT_MONTANT
            FROM v2.ENCAISSEMENT AS e
            INNER JOIN v2.MODE_PAIEMENT AS mp ON mp.R_MODEPAIEME_VALEUR = e.ENCMT_MODE_PAIEMENT
            WHERE e.ENCMT_ID_ENCMT IN ({liste})
              AND EXISTS (SELECT 1 FROM v2.VENTILATION AS vt INNER JOIN v2.AFFAIRE AS a ON a.AFFRE_ID_AFFRE = vt.VENTI_ID_AFFRE
                          INNER JOIN rec.Client AS c ON c.Id = vt.VENTI_ID_CLIEN WHERE vt.VENTI_ID_ENCMT = e.ENCMT_ID_ENCMT)
            """, ct))
        {
            while (await r.ReadAsync(ct))
            {
                dates.Add(r.GetDateTime(0));
                total += r.GetDecimal(1);
            }
        }
        if (dates.Count != ids.Length) return IssueGeste.Refuser(HorsJournal);

        var (du, au) = geste.Demande.Valeurs.GetValueOrDefault(LecteurJournalCaisse.CriterePeriode) is { Length: > 0 } periode
            ? LecteurJournalCaisse.Periode(periode, geste.Maintenant.Date)
            : (dates.Min().Date, dates.Max().Date);
        var titre = $"Journal de caisse de la période du {EditionsEtatsV2.Jour(du)} au {EditionsEtatsV2.Jour(au)}";
        var requete = "SELECT ENCMT_DATE_ENCAISSEMENT, CASE R_MODEPAIEME_TYPE WHEN 'RBT' THEN CLIENT.CLIEN_NOM ELSE ENCMT_DESIGNATION_PAYEUR END AS test, "
            + "ENCMT_MONTANT, ENCMT_DESIGNATION_BANQUE, ENCMT_NUMERO_TITRE, ENCMT_MODE_PAIEMENT, AFFRE_ID_AFFRE, UTLST_LOGIN, ENCMT_ID_ENCMT, VENTI_ID_ENCMT, "
            + "VENTI_ID_AFFRE, ENCMT_ID_UTLST, UTLST_ID_UTLST, UTLST_NOM, UTLST_PRENOM FROM ENCAISSEMENT LEFT JOIN UTILISATEUR ON ENCMT_ID_UTLST = UTLST_ID_UTLST "
            + "INNER JOIN VENTILATION ON ENCMT_ID_ENCMT = VENTI_ID_ENCMT INNER JOIN AFFAIRE ON VENTI_ID_AFFRE = AFFRE_ID_AFFRE "
            + "INNER JOIN CLIENT ON VENTI_ID_CLIEN = CLIEN_ID_CLIEN INNER JOIN MODE_PAIEMENT ON ENCMT_MODE_PAIEMENT = R_MODEPAIEME_VALEUR "
            + $"WHERE ENCMT_ID_ENCMT IN ({liste})";
        var etat = new EditionsEtatsV2.Etat(Processus, titre, nomRpt, requete, ids.Length, total);
        var id = await EditionsEtatsV2.PoserAsync(geste, etat, ct);

        var idTexte = id.ToString(CultureInfo.InvariantCulture);
        var encaissements = $"{ids.Length} encaissement{(ids.Length > 1 ? "s" : "")}";
        var trace = new TraceGeste("demande", $"l'édition « {titre} »", idTexte, "a cree", null,
            new { Etat = nomRpt, Encaissements = ids.Length, Total = total, File = "file des éditions" });
        return IssueGeste.Ecrit($"{titre} : {encaissements}, {CriteresConsultation.Euros(total)}, placé dans la file des éditions.", [idTexte], trace);
    }
}
