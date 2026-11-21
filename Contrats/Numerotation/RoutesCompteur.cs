namespace RigV3.Contrats.Numerotation;

/// <summary>
/// Les adresses du compteur central des numeros (stories NUM-001, NUM-004, NUM-008) :
/// le serveur les publie (Serveur/Numerotation/Compteur/ModuleCompteur.cs), l'ecran les appelle.
/// Un acte de la V3 qui numerote ne passe pas par ces routes mais par le service
/// DistributeurNumeros, dans la transaction de l'acte (docs/reconstruction/numerotation-compteur.md).
///
/// Chaque adresse porte le code du greffe, <c>{greffe}</c> (lot 8) : le serveur ouvre la base V3 de
/// CE greffe par le catalogue commun et y juge l'agent (session Windows). 401 sans session, 403 pour
/// un compte refuse dans ce greffe, 404 pour un greffe absent du catalogue, 503 si sa base ne repond pas.
/// L'adresse a appeler : <see cref="Pour"/>.
/// </summary>
public static class RoutesCompteur
{
    /// <summary>GET : les compteurs de la base du greffe (<see cref="CompteurNumeros"/>[]) ; « ?type=JPC » pour une seule serie.</summary>
    public const string Compteurs = "/numerotation/{greffe}/compteurs";

    /// <summary>POST <see cref="DemandeNumero"/> : le numero suivant d'une serie du greffe (<see cref="NumeroAttribue"/>).</summary>
    public const string Numeros = "/numerotation/{greffe}/numeros";

    /// <summary>POST <see cref="DemandeRestitution"/> : rendre un numero donne par erreur (<see cref="ResultatRestitution"/>).</summary>
    public const string Restitutions = "/numerotation/{greffe}/restitutions";

    /// <summary>
    /// La fonction (droit) qu'exige <see cref="Restitutions"/> : XNUMERO, la procedure de menu de la V2
    /// « Reattribution de numero metier » (PROCESSUS_ET_FONCTIONALITE ; roles RCSNIV03 et RCSNIV05 sur RIG_DEV).
    /// Sans elle : 403 et rien n'est rendu (NUM-004 T04, T08). Lire les compteurs n'exige que d'etre admis.
    /// </summary>
    public const string FonctionReattribution = "XNUMERO";

    /// <summary>GET : la liste des numeros manquants du registre (<see cref="NumeroManquant"/>[], NUM-013 T04) ;
    /// « ?serie=RC1 » et « ?annee=2026 » la restreignent.</summary>
    public const string Manquants = "/numerotation/{greffe}/manquants";

    /// <summary>
    /// La fonction qu'exige <see cref="Manquants"/> : LNUMCM, la procedure de menu de la V2 « Liste des numeros chrono
    /// manquants » (PROCESSUS_ET_FONCTIONALITE ; roles RCSNIV01 et RCSNIV05 sur RIG_DEV). Sans elle : 403.
    /// </summary>
    public const string FonctionNumerosManquants = "LNUMCM";

    /// <summary>L'adresse d'une de ces routes pour le greffe <paramref name="greffe"/> (son code au catalogue).</summary>
    public static string Pour(string route, string greffe)
        => route.Replace("{greffe}", Uri.EscapeDataString(greffe));
}

/// <summary>
/// Les codes rendus par le programme d'attribution et la restitution, ceux de la V2 compris
/// (-2 = type inconnu, GENERER_NUMERO.sql:54). 0 = fait ; un refus est negatif (NUM-004 CA09).
/// </summary>
public static class CodesNumerotation
{
    /// <summary>Numero donne, ou numero rendu / journalise.</summary>
    public const int Ok = 0;

    /// <summary>Serveur seulement (HTTP 400) : demande incomplete, le programme n'a pas ete appele
    /// (le -1 de Numerotation.Restituer de la V2 : « la procedure n'a pas pu etre jouee »).</summary>
    public const int NonJouee = -1;

    /// <summary>Type de numero inconnu (comme la V2) : rien n'est ecrit.</summary>
    public const int TypeInconnu = -2;

    /// <summary>Restitution : le numero ne suit le format d'aucun compteur de sa serie ; rien n'est ecrit.</summary>
    public const int NumeroIllisible = -3;

    /// <summary>Restitution : numero lisible mais jamais donne (au-dela du compteur) ; refus journalise.</summary>
    public const int JamaisDonne = -4;

    /// <summary>Attribution : le chrono suivant depasse la taille du format, ou la fin de la plage de
    /// La Poste (FinPlage, NUM-017 CA02) ; refus journalise.</summary>
    public const int SeriePleine = -5;

    /// <summary>Attribution : annee au-dela de l'annee suivante (NUM-005 CA04, migration NUM-005_reprise) ;
    /// refus journalise, rien n'est consomme. Les compteurs d'annees futures repris de la V2 ne servent plus.</summary>
    public const int AnneeHorsLimite = -6;

    /// <summary>Serveur seulement (HTTP 503) : le compteur est tenu par une autre transaction au-dela
    /// du delai d'attente (erreur SQL 51001) ; rien n'est consomme, la demande peut etre refaite.</summary>
    public const int CompteurOccupe = -9;
}
