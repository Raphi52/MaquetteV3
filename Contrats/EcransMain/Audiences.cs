namespace RigV3.Contrats.EcransMain;

// « Créer une audience » (PROC_AUDIENCE, web/src/ecrans/EcranAudiences.tsx) : le GESTE de l'écran, créer une audience (plan « RIG v3
// complet, mesuré par le banc du parcours complet », lot 9, 28/11/2026). La lecture est RoutesAudiences.Programmees ; l'écriture :
// Serveur/Socle/EcransMain/EcritureAudiences.cs, sa route : Serveur/Socle/EcransMain/ModuleAudiences.cs.

/// <summary>L'adresse du geste « Créer l'audience » et les clés de ses valeurs (les titres des champs de RIG v2,
/// OPE_CREATION_AUDIENCE.Designer.cs:90-154).</summary>
public static class RoutesCreationAudience
{
    /// <summary>
    /// POST un geste (<c>RigV3.Contrats.EcransGeneres.GesteEcran</c>, Type = Creer, Ids vide) : la saisie OPE_CREATION_AUDIENCE de RIG
    /// v2 ; les listes portent leur CODE (<see cref="OptionAudience"/>). Rend un <c>ResultatGeste</c> dont Ids = [l'identifiant de
    /// l'audience créée] (422 : le refus de RIG v2, rien d'écrit).
    /// </summary>
    public const string Creer = "/ecrans-main/{greffe}/audiences/creer";

    /// <summary>La date, aaaa-mm-jj.</summary>
    public const string ChampDate = "Date";

    /// <summary>L'heure, HH:MM.</summary>
    public const string ChampHeure = "Heure";

    /// <summary>Le code du type d'audience (TYPE_AUD_CAB).</summary>
    public const string ChampType = "Type d'Audience";

    /// <summary>Le code de la chambre (CHAMBRE).</summary>
    public const string ChampChambre = "Chambre";

    /// <summary>Le code de la section (SECTION).</summary>
    public const string ChampSection = "Section";

    /// <summary>Les codes des types de classement admis, séparés par « ; » (OPE_TYPE_CLASSEMENT_ADMIS.TypesClassementAdmis).</summary>
    public const string ChampClassements = "Types de classement admis";

    /// <summary>« oui » / « non » (vide : oui, la valeur par défaut de RIG v2).</summary>
    public const string ChampPublique = "Publique";

    /// <summary>« oui » / « non » (vide : non).</summary>
    public const string ChampNonDisponibleWeb = "Non disponible dans les choix d'Audience WEB";
}
