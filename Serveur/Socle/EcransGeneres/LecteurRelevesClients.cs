using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Releve de compte » (PROC_RELCLI) : les comptes clients debiteurs a qui editer un releve. Mode d'emploi : LecteurEcran.cs. Plan
/// « RIG v3 100 % fonctionnel », lot 12, 27/11/2026. Le geste « Valider les soldes » : EcrivainRelevesClients.cs.
///
/// SOURCE (ancien RIG) : MetierReleveClient.DoSearch (D:\RigApplication\Source\RIG\DLL\Processus\PROC_RELCLI ; le Detail OPE de
/// proc_relcli.md, « 1bis. La requete de recherche », fait foi), aux criteres de l'ouverture (SearchCriteriaReleveClient.cs:15-18) :
///   - population : CLIEN_IS_COMPTE_PC = 0 et CLIEN_RELEVE_FACTURE = 1 (MetierReleveClient.cs:114) ; statut actif « A » ; types
///     Standard (TYPE 'C', GROUPE &lt;&gt; 'GR', pas de compte web) et Greffes (TYPE 'C', GROUPE 'GR') (:117-125) ;
///   - le DERNIER solde valide (CLIENT_SOLDE, le plus recent ; un client sans solde n'est pas liste, INNER JOIN) ;
///   - solde courant = ce solde + ventilations posterieures - factures TTC posterieures (:129-179), en decimal exact (money) ;
///   - debiteur au-dela du rompu du greffe : solde courant &lt;= -GRPAR_ROMPU (GREFFE_PARAMETRE, OPE_CRITERES.cs:30 ; 1,52 € par defaut
///     dans RIG_DEV, DF_GREFFE_PARAMETRE_GRPAR_ROMPU) ; ordre : code abrege. La V3 n'a pas de v2.GREFFE_PARAMETRE : ses colonnes
///     sont reprises une par ligne dans soc.ParametreGreffe (RepriseReglages.cs:14, SOC-003). fix-ok: mesure sur la copie de dev
///     (27/11/2026, « Invalid object name v2.GREFFE_PARAMETRE », erreur 500 a l'ecran) -- le rompu se lit dans soc.ParametreGreffe.
/// Dans la base V3, CLIENT est la fiche rec.Client (REC-013). GROUPES : Standard / Greffes. ETAT : AFaire « À éditer » (l'edition et
/// la validation des soldes n'ont pas de trace lue ici).
/// NON REPRIS (nomme) : l'apercu et l'edition des releves (etats Crystal, envoi par courriel) -- la raison en tete
/// d'EcrivainRelevesClients.cs.
/// </summary>
internal sealed class LecteurRelevesClients : ILecteurEcran
{
    public string Procedure => "PROC_RELCLI";

    internal const string Standard = "Standard", Greffes = "Greffes";

    // fix-ok: mesure au banc (27/11/2026) -- LectureV2.ExigerLecture refuse une requete qui commence par WITH (seul un SELECT passe) :
    // le dernier solde se lit en sous-requete (ROW_NUMBER), le meme calcul que la requete optimisee de la V2.
    private const string Requete = """
        SELECT TOP (@plafond) c.*, c.CLISO_MONTANT + c.Ventilations - c.Factures AS SoldeCourant, COUNT(*) OVER () AS Total
        FROM (SELECT cl.Id, cl.Abrege, cl.Nom, cl.NumeroCompte, cl.Groupe, ls.CLISO_DATE_SOLDE, ls.CLISO_MONTANT,
                     ISNULL((SELECT SUM(f.FACTU_MONTANT_TTC) FROM v2.FACTURE AS f WHERE f.FACTU_ID_CLIEN = cl.Id AND f.FACTU_DATE_CREATION > ls.CLISO_DATE_SOLDE), 0) AS Factures,
                     ISNULL((SELECT SUM(v.VENTI_MONTANT) FROM v2.VENTILATION AS v WHERE v.VENTI_ID_CLIEN = cl.Id AND v.VENTI_DATE_VENTILATION > ls.CLISO_DATE_SOLDE), 0) AS Ventilations
              FROM rec.Client AS cl
              INNER JOIN (SELECT CLISO_ID_CLIEN, CLISO_DATE_SOLDE, CLISO_MONTANT,
                                 ROW_NUMBER() OVER (PARTITION BY CLISO_ID_CLIEN ORDER BY CLISO_DATE_SOLDE DESC) AS rn
                          FROM v2.CLIENT_SOLDE) AS ls ON ls.CLISO_ID_CLIEN = cl.Id AND ls.rn = 1
              WHERE cl.EstComptePc = 0 AND cl.ReleveFacture = 1 AND cl.Statut = 'A'
                AND ((cl.Type = 'C' AND cl.Groupe <> 'GR' AND cl.EstCompteWeb = 0) OR (cl.Type = 'C' AND cl.Groupe = 'GR'))) AS c
        WHERE c.CLISO_MONTANT + c.Ventilations - c.Factures <= -ISNULL((SELECT TOP (1) CONVERT(money, Valeur) FROM soc.ParametreGreffe WHERE Colonne = 'GRPAR_ROMPU'), 1.52)
        ORDER BY c.Abrege, c.Id
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var solde = CriteresConsultation.Decimal(r, "SoldeCourant") ?? 0m;
        var precisions = FormeLigne.Joindre(FormeLigne.Texte(r, "Abrege"), "compte " + FormeLigne.Texte(r, "NumeroCompte"),
            "solde débiteur " + CriteresConsultation.Euros(-solde),
            $"dernier solde validé le {FormeLigne.Date(FormeLigne.DateDe(r, "CLISO_DATE_SOLDE"))} : {CriteresConsultation.Euros(CriteresConsultation.Decimal(r, "CLISO_MONTANT"))}");
        return new LigneLue(FormeLigne.Texte(r, "Nom"), precisions, FormeLigne.Texte(r, "Groupe") == "GR" ? Greffes : Standard, "AFaire", [])
        {
            Id = FormeLigne.Texte(r, "Id"),
        };
    }
}
