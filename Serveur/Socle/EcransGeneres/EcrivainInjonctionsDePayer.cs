using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Injonction de payer » (PROC_IPE) : CREER une injonction de payer -- son instance NUMEROTEE, son demandeur, son defendeur et
/// sa demande. Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurInjonctionsDePayer.cs ; le commun : CreationV2.cs. Plan
/// « RIG v3 100 % fonctionnel », lot 5, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_IPE.DoInitObject cree l'instance (Instance.CreateInstance, PROC_IPE.cs:153), date d'enrolement =
/// maintenant (:155), UN demandeur et UN defendeur (:158-168, qualifiants DemandeurIP / DéfendeurIP = DOIP / FOIP,
/// Partie.cs:827-829) ; l'etape Instance (ETP_INSTANCE) les sauve et numerote (OPE_INSTANCE.CallPrepSauve, CreationV2) ; la
/// demande recoit numero de role, numeros et nom (DoSauveAfterEtape, PROC_IPE.cs:304-307).
///
/// CONTROLES DE RIG v2, avant toute ecriture (un refus n'ecrit rien) :
///   1. les champs obligatoires (MetierObligatoire) : Code affaire, Nature affaire, Nature particulière de l'affaire, Acte de
///      saisine, Saisine tribunal, Date de l'acte (OPE_INSTANCE.Designer.cs) -- la date de l'acte n'est pas exigee pour une
///      assignation (ISO 01 : elle est sur le defendeur, OPE_INSTANCE.cs:1326-1329) ; Civilité et Désignation de chaque partie
///      (OPE_PARTIE.Designer.cs) ;
///   2. le code affaire est un de ceux que RIG v2 propose a IPE : type d'instance IP (OPE_INSTANCE.cs:1663-1664) ;
///   3. VerifierDates (OPE_INSTANCE.cs:1325-1350), l'enrolement etant maintenant ;
///   4. la numerotation donne un numero (num.Attribuer : type inconnu ou serie pleine = refus, rien d'ecrit).
///
/// CE QUI S'ECRIT : v2.AFFAIRE, v2.DEMANDE (IPE, numero DMD) et son DOSSIER_DEMANDE 2J -> l'instance ; v2.INSTANCE (numero de
/// role JRIP du code affaire, repertoire JGAFF) ; v2.PARTIE demandeur et defendeur. La demande est INTERROMPUE (I) : c'est
/// l'etat que RIG v2 laisse a une IP saisie et sauvee avant les etapes que la V3 ne reprend pas encore -- la reprendre dans RIG
/// v2 les termine. Trace : famille « affaire », « a cree », l'instance et ses numeros.
///
/// NON REPRIS (nommes ; le message le dit) : la decision preparee (ETP_PREP_DECISION, obligatoire, PROC_IPE.Designer.cs:77-83)
/// et le chef de demande ; la facture IPREQ / IPREQSUP (DoGenererLesArticles, PROC_IPE.cs:261-299) ; l'edition du KBis ou du
/// certificat de non-inscription du defendeur (PROC_IPE.cs:218-255) ; le circuit Tribunal digital (IPE_TRIDIG, service externe) ;
/// la duplication en masse d'une IP precedente (PROC_IPE.cs:127-149).
/// </summary>
internal sealed class EcrivainInjonctionsDePayer : IEcrivainEcran
{
    internal const string NomEcran = "Injonction de payer";

    /// <summary>IPE : codes affaire de type IP (OPE_INSTANCE.cs:1663-1664), parties DemandeurIP / DéfendeurIP (Partie.cs:827-829).</summary>
    private static readonly SaisieInstanceV2 Saisie = new("IPE", "IP", "DOIP", "FOIP", "Injonction de payer",
        "une injonction de payer", "la décision préparée, la facture et les éditions");

    public string Procedure => "PROC_IPE";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Creer];

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct) => CreerSaisieInstanceAsync(geste, Saisie, ct);
}
