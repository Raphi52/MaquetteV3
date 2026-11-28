using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Refacturation » (PROC_FACTREEDIT) : les factures deja emises dont l'edition peut etre relancee. Mode d'emploi : LecteurEcran.cs.
/// Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026. La reedition (« Valider ») : EcrivainRefacturation.cs (plan-f4fb097f, lot 4).
///
/// SOURCE (ancien RIG) : OPE_REVIEW.OnProcessusEventBeforeChild (D:\RigApplication\Source\RIG\DLL\Processus\PROC_FACTREEDIT ; le Detail
/// OPE de proc_factreedit.md fait foi, OPE_REVIEW.cs:72-88) : FACTURE jointe a sa DEMANDE (FACTU_NUM_DEMANDE = DMND_NUM_DEMANDE) et a
/// ses DEMANDE_EDITION, filtrees DMNED_CODE_PROCESSUS = 'FACT' et DMNED_PHASE_TRAITEMENT IN ('E', 'P', 'G', 'GT') -- une edition de
/// facture qui a abouti -- et FACTU_DATE_CREATION entre le debut (1900 a defaut, :79) et la fin (maintenant a defaut, :80).
/// ECART DIT : la V2 exige un client (FACTU_ID_CLIEN = &lt;client&gt;, :78 ; sans client la requete echoue) avant d'afficher la liste ;
/// la liste de la V3 n'a pas de critere : elle montre les factures de tous les clients, le client en colonne (CLIEN_ABREGE, la
/// colonne Client de la grille). Une facture a plusieurs editions n'a qu'une ligne (la V2 la repetait, sans DISTINCT).
/// Dans la base V3, CLIENT est la fiche rec.Client. ORDRE : les plus recentes d'abord. ETAT : AFaire « À rééditer » (la V2 coche toutes
/// les factures par defaut, :19 ; aucune trace de reedition n'est lue ici).
/// CELLULES : les colonnes de la grille V2 (OPE_REVIEW.cs:43-57) -- numero, date (jj/mm/aaaa), montant TTC (decimal exact) -- et le client.
/// LA REEDITION (« Valider ») : EcrivainRefacturation.cs, lot 4 du plan-f4fb097f (28/11/2026). RIG v2 (DoGenererLesDemandesEditions,
/// OPE_REVIEW.cs:236-254) donne la derniere edition de facture (maquette FACT_n_IMP) de chaque facture cochee au tableau d'edition, qui en
/// cree une COPIE, nouvelle demande d'edition (TreeViewDestinataire.cs:2682 -> DemandeEdition.CreateNouvelleDemandeEdition), puis valide
/// le tableau (TableauEditionLot2Parametre._sauveDiffereLot2, :347-425). Le lot 12 l'avait laissee non branchee, faute d'un socle
/// « tableau d'edition » ; la decision du chef du plan (« une edition = une demande dans la file des editions ») l'a tranchee : la V3
/// ecrit la copie. Ce que la validation du tableau ajoute -- le groupe d'impression (_creerNumGroupe, :447-505), les numeros LR/LRAR
/// (DemandeEdition.SauveValidation, DemandeEdition.cs:1392-1405), la ligne DEMANDE_EDITION_HISTO (:1406-1407) -- reste non repris, nomme
/// dans l'ecrivain.
/// </summary>
internal sealed class LecteurRefacturation : ILecteurEcran
{
    public string Procedure => "PROC_FACTREEDIT";

    internal const string Groupe = "Factures émises";

    private const string Requete = """
        SELECT TOP (@plafond) f.FACTU_NUMERO_FACTURE, f.FACTU_DATE_CREATION, f.FACTU_MONTANT_TTC, c.Abrege, c.Nom, COUNT(*) OVER () AS Total
        FROM v2.FACTURE AS f
        INNER JOIN rec.Client AS c ON c.Id = f.FACTU_ID_CLIEN
        WHERE f.FACTU_DATE_CREATION >= '19000101' AND f.FACTU_DATE_CREATION < @demain
          AND EXISTS (SELECT 1 FROM v2.DEMANDE AS d INNER JOIN v2.DEMANDE_EDITION AS de ON de.DMNED_ID_DMND = d.DMND_ID_DMND
                      WHERE d.DMND_NUM_DEMANDE = f.FACTU_NUM_DEMANDE AND de.DMNED_CODE_PROCESSUS = 'FACT'
                        AND de.DMNED_PHASE_TRAITEMENT IN ('E', 'P', 'G', 'GT'))
        ORDER BY f.FACTU_DATE_CREATION DESC, f.FACTU_NUMERO_FACTURE
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond), new SqlParameter("@demain", aujourdhui.Date.AddDays(1)));

    // fix-ok: mesure au banc (27/11/2026, F12E005 datee de demain retenue) -- la borne « fin du jour » en AddTicks(-1) est arrondie au
    // lendemain 0 h par le datetime de SQL Server ; la fin de periode de la V2 (DateTime.Now) se lit ici « avant demain ».

    private static LigneLue Ligne(SqlDataReader r)
    {
        var numero = FormeLigne.Texte(r, "FACTU_NUMERO_FACTURE");
        var date = FormeLigne.Date(FormeLigne.DateDe(r, "FACTU_DATE_CREATION"));
        var montant = CriteresConsultation.Decimal(r, "FACTU_MONTANT_TTC");
        var client = FormeLigne.Texte(r, "Abrege");
        return new LigneLue($"Facture {numero}", FormeLigne.Joindre("émise le " + date, CriteresConsultation.Euros(montant) + " TTC", client, FormeLigne.Texte(r, "Nom")),
            Groupe, "AFaire", [])
        {
            Id = numero,
            Valeurs = [numero, date, CriteresConsultation.MontantFiche(montant), client],
        };
    }
}
