namespace RigV3.Contrats.EcransMain;

// « Experts » (PROC_EXPERT, web/src/ecrans/EcranExperts.tsx) : le GESTE de l'écran, enregistrer la fiche d'un expert (plan « RIG v3
// complet, mesuré par le banc du parcours complet », lot 9, 28/11/2026). La lecture est RoutesExperts.Experts ; l'écriture :
// Serveur/Socle/EcransMain/EcritureExperts.cs, sa route : Serveur/Socle/EcransMain/ModuleExperts.cs.

/// <summary>L'adresse du geste « Enregistrer la fiche » d'un expert et les clés de ses valeurs (les titres des champs de RIG v2,
/// PROC_EXPERT\Edition\OPE_EDITION_EXPERT.Designer.cs:96-209).</summary>
public static class RoutesFicheExpert
{
    /// <summary>
    /// POST un geste (<c>RigV3.Contrats.EcransGeneres.GesteEcran</c>, Type = Enregistrer, Ids = [le numéro de l'expert]) : les champs de la
    /// fiche ; un champ absent garde la valeur de la base. Rend un <c>ResultatGeste</c> (422 : le refus de RIG v2, rien d'écrit).
    /// </summary>
    public const string Enregistrer = "/ecrans-main/{greffe}/experts/enregistrer";

    public const string ChampCivilite = "Civilité", ChampAbrege = "Abrégé", ChampNom = "Nom", ChampComplementNom = "Complément nom",
                        ChampTelephone = "Téléphone", ChampFax = "Fax", ChampMail = "Mail", ChampQualification = "Qualifiant profession",
                        ChampActif = "Actif";

    /// <summary>Les participants (PRTCP_ID_PRTCP, <see cref="MesureExpertLue.IdParticipant"/>) que l'agent coche dans les listes de
    /// mesures, séparés par « ; » : ils reprennent la civilité, le nom, le complément, le courriel, la qualification et l'adresse de l'expert
    /// (OPE_EDITION_EXPERT._GestionCascadeUpdate).</summary>
    public const string ChampParticipants = "Mesures à mettre à jour";
}
