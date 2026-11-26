namespace RigV3.Contrats.EcransMain;

// « Réquisitions » (PROC_REQUI ; web/src/ecrans/EcranRequisitions.tsx), la recherche des entreprises lue dans la base du greffe
// (plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026 ; marche du lot 17). Le lecteur :
// Serveur/Socle/EcransMain/LectureEntreprisesRequi.cs.

/// <summary>L'adresse de la lecture : POST <see cref="RechercheEntreprises"/>, rend <see cref="EntreprisesLues"/>.</summary>
public static class RoutesRequisitions
{
    public const string Entreprises = "/ecrans-main/{greffe}/requisitions/entreprises";
}

/// <summary>Les criteres de OPE_REQUI_RECHERCHE que la base cherche, « Inclure les supprimes » et la limite de dossiers.</summary>
public sealed record RechercheEntreprises(string RaisonSociale, string Siren, string NumVoie, string Adresse, string CodePostal, string Ville,
                                          bool InclureRadies, int Limite);

/// <summary>Un dossier RCS trouve : son id, SIREN, numero de gestion, designation, « PM » (une denomination) ou « PP », forme
/// juridique, adresse du siege, radie ou non, son nombre d'inscriptions (RechercheDossier.NombreInscription de la V2) et une
/// procedure collective qui bloque le certificat de non-faillite (VerifSiPC « ENTR » de la V2).</summary>
public sealed record EntrepriseLue(int Id, string Siren, string Gestion, string Designation, string Personne, string FormeJuridique,
                                   string Adresse, string CodePostal, string Ville, bool Radie, int NbInscriptions, bool ProcedureCollective);

/// <summary>Les dossiers lus (au plus la limite) et leur total.</summary>
public sealed record EntreprisesLues(EntrepriseLue[] Lignes, int Total);
