namespace RigV3.Contrats.EcransMain;

// « Recherche d'affaires » (PROC_RECHAFF ; web/src/ecrans/EcranRechAffaires.tsx), ecran fait main lu dans la base du greffe
// (plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureAffaires.cs.

/// <summary>L'adresse de la lecture : POST <see cref="RechercheAffaires"/>, rend <see cref="AffairesLues"/>.</summary>
public static class RoutesAffaires
{
    public const string Affaires = "/ecrans-main/{greffe}/affaires";

    /// <summary>POST, le geste « rattacher une demande a l'affaire choisie » (GesteEcran : Ids = [l'affaire], valeur
    /// <see cref="RattachementDemande.Demande"/>) ; ecrit par Serveur/Socle/EcransMain/EcritureRechAffaires.cs (lot 8 du plan « parcours
    /// complet », 28/11/2026).</summary>
    public const string Rattacher = "/ecrans-main/{greffe}/affaires/rattacher";
}

/// <summary>
/// Le champ du geste « rattacher une demande » : RIG v2 recoit la demande en parametre du processus (PROC_RECHAFF.cs:20-26,
/// NumDemandeInput) et la rattache a l'affaire validee (DoValider, :33-61) ; l'ecran de la V3 la saisit sous ce titre.
/// </summary>
public static class RattachementDemande
{
    public const string Section = "Rattachement";
    public const string Demande = "N° de demande";
}

/// <summary>
/// Les criteres que la base applique : le numero (d'affaire, de demande ou de facture : OPE_CRITERES de PROC_RECHAFF), le
/// libelle ou le SIREN, et ceux de l'instance de l'affaire (id, role, code affaire, nom courant, periode de saisine).
/// </summary>
public sealed record RechercheAffaires(string Numero, string Libelle, string IdInstance, string Role, string CodeAffaire, string Nom,
                                       DateTime? SaisineDu, DateTime? SaisineAu, int Plafond);

/// <summary>
/// Une affaire (AFFAIRE) : son numero, son libelle, sa date de creation, son solde (Affaire.GetSolde : ventilations moins articles
/// utilises), les numeros de ses demandes et de leurs factures, l'instance qui lui est rattachee (vide sans instance) et le SIREN
/// du dossier RCS de ses demandes.
/// </summary>
public sealed record AffaireLue(int Id, string Libelle, DateTime? Creation, decimal Solde, string Demandes, string Factures, string IdInstance,
                                string Role, string CodeAffaire, string Nom, DateTime? Saisine, string Siren, bool AvecDossierRcs);

/// <summary>Les affaires lues (au plus le plafond) et leur total.</summary>
public sealed record AffairesLues(AffaireLue[] Lignes, int Total);
