using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Ordonnance d'enrôlement » (PROC_CORDJRCS) : CREER une ordonnance RCS -- son instance NUMEROTEE, son demandeur, son
/// defendeur et sa demande. Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurOrdonnancesEnrolement.cs ; la creation :
/// CreationV2.CreerSaisieInstanceAsync. Plan « RIG v3 100 % fonctionnel », lot 5, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_CORDJRCS.DoInitObject (fiche proc_cordjrcs.md, « Détail OPE ») cree l'instance vide
/// (Instance.CreateInstance, PROC_CORDJRCS.cs:182), un demandeur, un defendeur et un intervenant (cache) par Partie.CreatePartie
/// (:187-203, qualifiants Demandeur / Défendeur = D / F, Partie.cs:823-830), date d'enrolement = maintenant (:214) ; l'etape
/// Instance (ETP_INSTANCE) ne propose que les codes affaire de type RC (OPE_INSTANCE.cs:1649-1650 ; la meme garde pour un code
/// recu en parametre, PROC_CORDJRCS.cs:220-222), et numerote a la sauvegarde (OPE_INSTANCE.CallPrepSauve) ; la demande recoit
/// numero de role, numeros et nom (DoSauveAfterEtape, :251-258).
///
/// NON REPRIS (nommes ; le message le dit) : la decision judiciaire et la mise a jour du dossier RCS ou du non-inscrit a la
/// validation, le chef de demande, la facture (RCORDO, OPREQREFUS, JRECLAM, frais postaux 801 / 806 : PROC_CORDJRCS.cs:26-40,
/// fiche :102), les editions et le courriel d'enrolement (OPE_INSTANCE.cs:1602-1606, service externe). La demande reste
/// interrompue : la reprendre dans RIG v2 les termine.
/// </summary>
internal sealed class EcrivainOrdonnancesEnrolement : IEcrivainEcran
{
    internal const string NomEcran = "Ordonnance d'enrôlement";

    private static readonly SaisieInstanceV2 Saisie = new("CORDJRCS", "RC", "D", "F", "Ordonnance d'enrôlement",
        "une ordonnance d'enrôlement", "la décision, la mise à jour du dossier RCS, la facture et les éditions");

    public string Procedure => "PROC_CORDJRCS";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Creer];

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct) => CreerSaisieInstanceAsync(geste, Saisie, ct);
}
