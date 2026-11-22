namespace RigV3.Contrats.Recopie;

// Les echanges de l'historique des deux logiciels (lot 11 de la phase 2 du plan de reconstruction, 2026-11-21) :
//   REC-010  l'historique d'une entreprise ou d'un client, affaires des deux origines, en lecture ;
//   REC-011  les liens entre une demande de la V3 et une demande d'une ancienne affaire ;
//   REC-018  une arrivee de courrier enregistree une fois, ses demandes des deux logiciels ;
//   REC-009  la recherche par ancien numero (anciens logiciels) et les fiches d'anciennes reprises ;
//   REC-007  le retour d'un greffe beta a l'ancien RIG (exploitation d'Amitel).
// Le serveur les publie (Serveur/Recopie/ModuleHistorique.cs), l'ecran les lit (Pont/EcranHistoriqueV2.cs).
// Mode d'emploi : docs/reconstruction/recopie.md, sections du lot 11.

/// <summary>Les adresses. Toutes portent le greffe (tout vit dans sa base V3).</summary>
public static class RoutesHistorique
{
    /// <summary>GET ?critere=siren|gestion|client|arrivee&amp;valeur=... -> <see cref="VueHistorique"/> (agent admis du greffe).</summary>
    public const string Historique = "/recopie/{greffe}/historique";

    /// <summary>GET -> <see cref="DemandeHistorique"/> : une demande et ses liens, en lecture (REC-011 CA03).</summary>
    public const string Demande = "/recopie/{greffe}/demandes/{id}";

    /// <summary>GET -> <see cref="ArriveeDeuxLogiciels"/> : une arrivee par son numero, ses demandes des deux logiciels (REC-018 CA03).</summary>
    public const string Arrivee = "/recopie/{greffe}/arrivees/{numero}";

    /// <summary>GET ?numero=... (ancien numero) ou ?rig=... (numero RIG) -> <see cref="AncienNumeroVue"/>[] (REC-009 CA01, CA02).</summary>
    public const string AnciensNumeros = "/recopie/{greffe}/anciens-numeros";

    /// <summary>GET -> <see cref="FicheAncienneRepriseVue"/>[] : les fiches de remplissage des anciennes reprises (REC-009 CA03).</summary>
    public const string AnciennesReprises = "/recopie/{greffe}/anciennes-reprises";

    /// <summary>GET -> <see cref="PreparationRetour"/> ; POST <see cref="DemandeRetour"/> -> <see cref="BilanRetour"/> (REC-007,
    /// exploitation d'Amitel ; la decision n'est prise que par la personne designee, CA08).</summary>
    public const string Retour = "/recopie/{greffe}/retour";

    /// <summary>PUT <see cref="DesignationRetour"/> : la personne designee chez Amitel pour decider le retour (CA08).</summary>
    public const string ResponsableRetour = "/recopie/{greffe}/retour/responsable";

    /// <summary>PUT <see cref="RegleTypeRetourDemandee"/> : le type de la V2 d'un type cree dans la V3 seule (CA06).</summary>
    public const string TypeRetour = "/recopie/{greffe}/retour/types";

    public static string Pour(string route, string greffe) => route.Replace("{greffe}", Uri.EscapeDataString(greffe));
}

/// <summary>Les deux logiciels, tels que l'agent les lit.</summary>
public static class Origines
{
    public const string V2 = "ancien RIG";
    public const string V3 = "V3";
    /// <summary>La marque d'une ancienne affaire (REC-010 CA01).</summary>
    public const string MarqueAncienne = "traitée dans l'ancien RIG";
    public const string MarqueNouvelle = "traitée dans la V3";
}

/// <summary>Un lien entre deux demandes (REC-011).</summary>
/// <param name="Lien">Ce que l'agent lit : « bloquée par », « née de », « bloque », « a engendré ».</param>
/// <param name="Logiciel">Le logiciel de la demande liee (<see cref="Origines.V2"/> ou <see cref="Origines.V3"/>).</param>
/// <param name="Leve">Le blocage est leve (la demande bloquante est validee ou abandonnee, REC-011 CA02) ; il reste lisible.</param>
public sealed record LienDemandeVue(string Lien, int IdDemande, string? Numero, string Logiciel, string Etat, string EtatLibelle, bool Leve, DateTime? LeveLe);

/// <summary>Une demande, des deux logiciels, en lecture.</summary>
/// <param name="Id">Sa ligne dans la base V3.</param>
/// <param name="IdV2">Son identifiant dans l'ancien RIG ; null pour une demande de la V3 jamais recopiee.</param>
/// <param name="Etat">Le code d'etat de la V2 (DMND_ETAT_DEMANDE : V, Q, W...).</param>
/// <param name="EtatLibelle">Ce que l'agent lit (« validée », « en attente »).</param>
/// <param name="ValideeLe">Sa date de validation (REC-010 CA02, REC-007 CA02).</param>
/// <param name="Bloquee">Elle est bloquee (DMND_INHIBEE) ; <paramref name="TexteBlocage"/> dit par quoi.</param>
public sealed record DemandeHistorique(int Id, string? IdV2, string Logiciel, string? Numero, string? Libelle, string Etat, string EtatLibelle,
                                       DateTime? CreeeLe, DateTime? ValideeLe, string? Type, string? NumeroArrivee, bool Bloquee, string? TexteBlocage,
                                       LienDemandeVue[] Liens);

/// <summary>Un envoi ou une reception tracee dans la base des echanges (EDI_GREFFE) pour une demande (REC-010 CA04).</summary>
/// <param name="Sens">« envoi » (FLUX_SORTANT) ou « réception » (FLUX_ENTRANT).</param>
/// <param name="Etat">L'etat du flux dans la base des echanges (son numero, tel qu'elle le tient).</param>
/// <param name="EtatLibelle">Ce que l'agent lit : le libelle de la base des echanges (FLUX_SORTANT_ETAT, FLUX_ENTRANT_ETAT), comme la
/// V2 l'affiche (FluxSortantEtats.GetLibelle) ; le numero seul si la base n'a pas de libelle pour lui.</param>
/// <param name="EnErreur">Le flux a echoue (<see cref="EtatEnErreur"/>) : l'ecran le marque, avec <paramref name="Erreur"/>.</param>
public sealed record EchangeHistorique(string Sens, int Id, string CodeEdi, int Etat, string EtatLibelle, bool EnErreur, DateTime Le, string? Reference,
                                       string? Erreur, string? Demande)
{
    /// <summary>
    /// Un etat d'echec de la base des echanges : 6 « Erreur d'extraction » (envoi) et 7 « Erreur d'integration » (envoi et reception),
    /// les seuls des enums de la V2 (Source\Edi\DLL\EdiTools\FluxSortant.cs:349 FluxSortantEtat, FluxEntrant.cs:349 FluxEntrantEtat) ;
    /// 0 annule, 1 extrait et 2 integre ne le sont pas.
    /// </summary>
    public static bool EtatEnErreur(int etat) => etat is 6 or 7;
}

/// <summary>Une affaire de l'historique.</summary>
/// <param name="Logiciel">Ou elle se traite (<see cref="Origines.V2"/> : une ancienne affaire, en lecture seule dans la V3).</param>
/// <param name="Marque"><see cref="Origines.MarqueAncienne"/> ou <see cref="Origines.MarqueNouvelle"/> (REC-010 CA01).</param>
/// <param name="Etat">« en cours » si une de ses demandes l'est, sinon « terminée ».</param>
/// <param name="Client">Le client tel que la fiche de la V3 le dit aujourd'hui (la V3 fait foi, REC-010 CA05).</param>
/// <param name="EchangesNote">Pourquoi les echanges ne sont pas listes (base des echanges non reglee ou injoignable) ; null sinon.</param>
public sealed record AffaireHistorique(int Id, string? IdV2, string Logiciel, string Marque, string Etat, string? Libelle, DateTime? CreeeLe,
                                       string? Client, string? ClientCode, string[] Dossiers, DemandeHistorique[] Demandes,
                                       EchangeHistorique[] Echanges, string? EchangesNote);

/// <summary>L'historique d'une entreprise, d'un dossier, d'un client ou d'une arrivee.</summary>
/// <param name="Critere">« siren », « gestion », « client » ou « arrivee ».</param>
/// <param name="Titre">Ce qui a ete trouve (« SOCIETE DUPONT — 512 345 678 ») ; null si rien.</param>
public sealed record VueHistorique(string Critere, string Valeur, string? Titre, AffaireHistorique[] Affaires, int Anciennes, int Nouvelles, string? Note);

/// <summary>Une arrivee de courrier et ses demandes des deux logiciels (REC-018 CA03).</summary>
/// <param name="IdV2">Son identifiant dans l'ancien RIG (la meme arrivee y est, meme numero, REC-018 CA01) ; null si pas encore recopiee.</param>
public sealed record ArriveeDeuxLogiciels(int Id, string? IdV2, string? Numero, string Mode, DateTime? Le, string? Nom, string? References,
                                          DemandeHistorique[] Demandes, int DansAncienRig);

/// <summary>Une correspondance d'un ancien logiciel (REC-009 CA01, CA02).</summary>
/// <param name="Logiciel">« migration » (Dedale, Alinea, MyGreffe, Replay...), « Oracle » ou « GAGI ».</param>
/// <param name="TableRig">La sorte de fiche RIG (DOSSIER_RCS, INSCRIPTION, PROCEDURE_COLLECTIVE...).</param>
/// <param name="SorteFiche">Ce que l'agent lit (« dossier RCS », « procédure collective »).</param>
/// <param name="IdV3">La fiche de la V3 (rec.Correspondance) ; null si sa table n'est pas encore reprise par la V3.</param>
public sealed record AncienNumeroVue(string Logiciel, string TableRig, string SorteFiche, string? AncienId, string? AncienNumero, int? IdRig,
                                     string? NumeroRig, string? NumeroInfogreffe, string? TableV3, string? IdV3);

/// <summary>Une fiche creee par une ancienne reprise pour combler un lien casse (REC-009 CA03).</summary>
public sealed record FicheAncienneRepriseVue(string TableV2, string IdV2, string? TableV3, string? IdV3, string Motif, string? Libelle);

/// <summary>La personne designee pour decider le retour (REC-007 CA08) : son compte de session.</summary>
public sealed record DesignationRetour(string Compte);

/// <summary>La regle d'un type de demande cree dans la V3 seule : le type de la V2 sous lequel ses demandes vont (REC-007 CA06).</summary>
public sealed record RegleTypeRetourDemandee(int IdTypeV3, int IdTypeV2);

/// <summary>La demande de retour (REC-007 CA08) : son motif, obligatoire.</summary>
public sealed record DemandeRetour(string Motif);

/// <summary>Un type de demande de la V3 sans equivalent dans la V2 (REC-007 CA06).</summary>
/// <param name="IdTypeV2">Le type de la V2 choisi ; null tant qu'aucune regle n'est choisie (le retour ne demarre pas).</param>
public sealed record TypeAuRetour(int IdTypeV3, string? Reference, string? Libelle, int Demandes, int? IdTypeV2);

/// <summary>Une table qui pointe vers une affaire ou une demande et qui a des lignes rattachees a la V3.</summary>
/// <param name="Sort">« reste dans la V3, renvoi dans la V2 » (facturation, C19) ou « sans regle de retour » (refus).</param>
public sealed record LignesDependantes(string TableV2, long Lignes, string Sort);

/// <summary>Ce que le retour ferait, et ce qui l'empeche (lu avant de decider).</summary>
/// <param name="EnBeta">Le greffe est beta et sa beta n'est pas finie (NUM-022 : rigv3.Pont de la base V2).</param>
/// <param name="FinBeta">La fin de sa beta (le jour J des autres greffes) ; null hors beta.</param>
/// <param name="Responsable">Le compte designe (CA08) ; null si personne ne l'est.</param>
/// <param name="Refus">Ce qui empeche le retour ; vide : il peut partir.</param>
public sealed record PreparationRetour(string Greffe, string BaseV2, string Etat, bool EnBeta, DateTime? FinBeta, string? Responsable,
                                       int Affaires, int Demandes, TypeAuRetour[] TypesSansEquivalent, LignesDependantes[] Dependantes,
                                       string[] Refus);

/// <summary>Le compte rendu d'un retour.</summary>
/// <param name="Etat">« refusee », « faite » ou « arretee » (rec.DecisionRetour).</param>
public sealed record BilanRetour(int IdDecision, string Etat, string Auteur, DateTime Le, string Motif, string? Detail, int Affaires, int Demandes,
                                 int Renvois, long DureeMs);
