namespace RigV3.Contrats.Numerotation;

/// <summary>
/// Les adresses du compteur central des numeros (stories NUM-001, NUM-004, NUM-008) :
/// le serveur les publie (Serveur/Numerotation/Compteur/ModuleCompteur.cs), l'ecran les appelle.
/// Un acte de la V3 qui numerote ne passe pas par ces routes mais par le service
/// DistributeurNumeros, dans la transaction de l'acte (docs/reconstruction/numerotation-compteur.md).
/// </summary>
public static class RoutesCompteur
{
    /// <summary>GET : les compteurs de la base (<see cref="CompteurNumeros"/>[]) ; « ?type=JPC » pour une seule serie.</summary>
    public const string Compteurs = "/numerotation/compteurs";

    /// <summary>POST <see cref="DemandeNumero"/> : le numero suivant d'une serie (<see cref="NumeroAttribue"/>).</summary>
    public const string Numeros = "/numerotation/numeros";

    /// <summary>POST <see cref="DemandeRestitution"/> : rendre un numero donne par erreur (<see cref="ResultatRestitution"/>).</summary>
    public const string Restitutions = "/numerotation/restitutions";
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
