using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Rappels RBE » (PROC_SRBE_RAPPEL) : les courriers de rappel et de relance du registre des beneficiaires effectifs
/// envoyes par l'ancien traitement « SRBE ». Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_SRBE_RAPPEL\PROC_SRBE\PROC_SRBE_RAPPEL.cs et la
/// fiche proc_srbe_rappel.md (Brain, reference/proc). Pour chaque dossier coche, la V2 cree une demande du processus
/// (Demande.CreateDemande(..., MetierCodeProcessus, ...), l. 146-147) rattachee au dossier (DMND_ID_DSRCS), et son
/// edition : « Rappel » si le dossier est immatricule avant le 01/08/2017, « Relance 1 » sinon (DATE_LIMITE_RAPPEL,
/// l. 20 ; arbitrage par dossier, l. 136 ; regle R1 de la fiche). Les codes de ces demandes sont ceux que PROC_IDBE
/// reconnait comme « vieux proc SRBE RAPPEL » : SRBE, SRBE_RAPPEL, SRBE_OLD (PROC_SRBE\PROC_DBE_INJ\PROC_IDBE.cs:62) ;
/// RIG_DEV n'a que « SRBE » (lu le 25/11/2026).
/// Une ligne = une de ces demandes, hors demande supprimee (X) ; son Id = la demande, dont le dossier recoit le courrier de « Éditer les
/// courriers » (EcrivainRappelsRbe.cs). La recherche des dossiers A ENVOYER n'est pas reprise :
/// ses trois requetes (DossierRCS.GetNRBEPourRappel / ...PourRelance / ...PourRappelOuRelance, OPE_RECHERCHE_CRITERES.cs:
/// 153-162) n'existent plus dans le code de l'ancien RIG (CompilationFull\PowerShell\buildErrors.txt:110, CS0117) ; la V2
/// cherche aujourd'hui ces entreprises par la relance des beneficiaires (PROC_DBE_RELANCE, nature SDBE), qui ecarte
/// d'ailleurs celles qui ont deja une demande SRBE validee (NATURE_DILIGENCE_PARAM SDBE / RSURV / 1).
///
/// GROUPE : immatriculation (rec.DossierRcs.DateImmatriculation) avant le 01/08/2017 -> « Rappel », sinon « Relance 1 ».
/// REGLE D'ETAT (DMND_ETAT_DEMANDE, valeurs de v2.ETAT_DEMANDE) :
///   A Arrivee, Q Qualifiee, R Reservee -> AFaire  « A envoyer » : aucune marche ;
///   V Validee, T Terminee              -> Validee « Courrier genere » : criteres, dossiers, courrier (0, 1, 2) ;
///   tout autre (I, W, ...)             -> EnCours « En cours » : criteres et dossier choisis (0, 1).
/// Ordre : a envoyer, en cours, puis generes ; le plus recent en tete.
/// </summary>
internal sealed class LecteurRappelsRbe : ILecteurEcran
{
    public string Procedure => "PROC_SRBE_RAPPEL";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Rappel = "Rappel", Relance1 = "Relance 1";

    /// <summary>Avant cette date d'immatriculation, un rappel ; a partir d'elle, une relance 1 (PROC_SRBE_RAPPEL.cs:20).</summary>
    internal static readonly DateTime DateLimiteRappel = new(2017, 8, 1);

    private const string Requete = """
        SELECT TOP (@plafond)
               d.DMND_ID_DMND, d.DMND_ETAT_DEMANDE, d.DMND_DATE_CREATION, d.DMND_DATE_VALIDATION,
               s.NumeroGestion, s.Siren, s.DateImmatriculation, e.Denomination,
               COUNT(*) OVER () AS Total
        FROM v2.DEMANDE AS d
        JOIN rec.DossierRcs AS s ON s.Id = d.DMND_ID_DSRCS
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        WHERE d.DMND_CODE_PROSS IN ('SRBE', 'SRBE_RAPPEL', 'SRBE_OLD') AND d.DMND_ETAT_DEMANDE <> 'X'
        ORDER BY CASE WHEN d.DMND_ETAT_DEMANDE IN ('A', 'Q', 'R') THEN 0
                      WHEN d.DMND_ETAT_DEMANDE IN ('V', 'T') THEN 2
                      ELSE 1 END,
                 d.DMND_DATE_CREATION DESC, d.DMND_ID_DMND DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var etatV2 = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE");
        var (etat, faites) = Etat(etatV2);
        var immat = FormeLigne.DateDe(r, "DateImmatriculation");
        var groupe = immat < DateLimiteRappel ? Rappel : Relance1;
        var creee = FormeLigne.DateDe(r, "DMND_DATE_CREATION");
        var (courrier, accord) = groupe == Rappel ? ("rappel", "") : ("relance 1", "e");   // le rappel, la relance
        var quand = etat switch
        {
            "Validee" => $"{courrier} généré{accord} le {FormeLigne.Date(FormeLigne.DateDe(r, "DMND_DATE_VALIDATION") ?? creee)}",
            "AFaire" => $"{courrier} à envoyer depuis le {FormeLigne.Date(creee)}",
            _ => $"{courrier} commencé{accord} le {FormeLigne.Date(creee)}",
        };
        var nom = FormeLigne.Joindre(FormeLigne.Texte(r, "Denomination"), FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion")));
        // Id = la demande SRBE : « Éditer les courriers » vise son dossier (EcrivainRappelsRbe.cs, lot 1 du plan-f4fb097f).
        return new LigneLue(nom, FormeLigne.Joindre(immat is null ? null : $"Immatriculée le {FormeLigne.Date(immat)}", quand),
                            groupe, etat, faites)
        {
            Id = Convert.ToInt32(r["DMND_ID_DMND"], System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>La regle d'etat (en tete du fichier) : etat du descripteur et marches franchies.</summary>
    internal static (string Etat, int[] Faites) Etat(string etatV2) => etatV2 switch
    {
        "A" or "Q" or "R" => ("AFaire", []),
        "V" or "T" => ("Validee", [0, 1, 2]),
        _ => ("EnCours", [0, 1]),
    };
}
