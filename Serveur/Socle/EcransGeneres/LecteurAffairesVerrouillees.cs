using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Libérer une affaire » (PROC_LIBAFFAIRE, dans l'ecran reuni « Defaire une affaire ») : les affaires qu'un utilisateur tient
/// verrouillees. Mode d'emploi : LecteurEcran.cs ; le geste : EcrivainAffairesVerrouillees.cs. Plan « RIG v3 100 % fonctionnel »,
/// lot 4, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_LIBAFFAIRE, operation OPE_LISTAFFAIRE (le Detail OPE de proc_libaffaire.md fait foi, la partie
/// haute decrit un plugin qui n'existe pas). La liste est OPE_LISTAFFAIRE.cs:41-44 :
///   « Select distinct AFFRE_ID_AFFRE, UTLST_CIVILITE, UTLST_PRENOM, UTLST_NOM, (DMND_NUM_DEMANDE des DEMANDE de l'affaire,
///     « N1, N2 ») from affaire INNER JOIN utilisateur ON AFFRE_ID_UTLST_EN_COURS = UTLST_ID_UTLST
///     where AFFRE_ID_UTLST_EN_COURS IS NOT NULL order by AFFRE_ID_AFFRE ».
/// La V3 lit AFFAIRE dans sa copie v2.AFFAIRE ; l'utilisateur est le compte d'agent de la V3 (UTILISATEUR -> soc.Compte,
/// FichesV3.cs, REC-017), retrouve par soc.Compte.IdV2 = UTLST_ID_UTLST (SOC-001_comptes.sql:24). Une affaire tenue par un
/// compte que la V3 ne connait pas n'est pas listee, comme l'INNER JOIN de RIG v2. Les demandes s'ordonnent par identifiant
/// (RIG v2 ne les ordonne pas : FOR XML PATH sans ORDER BY).
///
/// Ni filtre texte ni recherche dans RIG v2 (aucun DoVerifier, aucun @Recherche : proc_libaffaire.md, synthese des ecarts) :
/// ce lecteur ne cherche pas (pas d'IRechercheEcran) ; les criteres de l'ecran filtrent la liste deja lue. RIG v2 retire de
/// la liste l'affaire que l'agent a lui-meme ouverte (OPE_LISTAFFAIRE.cs:49, la demande en cours du processus) : la V3 n'ouvre
/// aucune affaire par une demande, rien a retirer.
///
/// GROUPES (descripteur) : « Avec une demande » (la colonne « Demandes » de RIG v2 est remplie) / « Sans demande ».
/// REGLE D'ETAT : toute ligne est « Verrouillée » (AFaire) -- c'est le seul critere de la liste ; une affaire liberee en sort
/// au rechargement (OPE_LISTAFFAIRE.cs:85, chargerDataGridView apres la boucle).
/// </summary>
internal sealed class LecteurAffairesVerrouillees : ILecteurEcran
{
    public string Procedure => "PROC_LIBAFFAIRE";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string AvecDemande = "Avec une demande", SansDemande = "Sans demande";

    private const string Requete = """
        SELECT TOP (@plafond) a.AFFRE_ID_AFFRE AS Id, a.AFFRE_LIBELLE AS Libelle, c.Civilite, c.Prenom, c.Nom,
               STUFF((SELECT ', ' + d.DMND_NUM_DEMANDE FROM v2.DEMANDE AS d WHERE d.DMND_ID_AFFRE = a.AFFRE_ID_AFFRE
                      ORDER BY d.DMND_ID_DMND FOR XML PATH(''), TYPE).value('.', 'varchar(max)'), 1, 2, '') AS Demandes,
               COUNT(*) OVER () AS Total
        FROM v2.AFFAIRE AS a
        JOIN soc.Compte AS c ON c.IdV2 = a.AFFRE_ID_UTLST_EN_COURS
        WHERE a.AFFRE_ID_UTLST_EN_COURS IS NOT NULL
        ORDER BY a.AFFRE_ID_AFFRE
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var id = FormeLigne.Texte(r, "Id");
        var demandes = FormeLigne.Texte(r, "Demandes");
        // L'utilisateur comme RIG v2 l'ecrit : « CIVILITE PRENOM NOM » (OPE_LISTAFFAIRE.cs:54).
        var utilisateur = string.Join(" ", new[] { FormeLigne.Texte(r, "Civilite"), FormeLigne.Texte(r, "Prenom"), FormeLigne.Texte(r, "Nom") }
                                               .Where(m => m.Length > 0));
        return new LigneLue($"Affaire n° {id}",
            FormeLigne.Joindre(FormeLigne.Texte(r, "Libelle"), $"verrouillée par {utilisateur}",
                               demandes.Length > 0 ? $"demandes {demandes}" : null),
            demandes.Length > 0 ? AvecDemande : SansDemande, "AFaire", [])
        {
            Id = id,
        };
    }
}
