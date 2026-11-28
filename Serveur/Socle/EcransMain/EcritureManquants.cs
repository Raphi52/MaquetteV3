using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Numéros manquants » (PROC_NUMEROS_MANQUANTS, écran fait main web/src/ecrans/EcranManquants.tsx) : le geste ÉDITER LA LISTE de la
/// procédure LNUMCM de RIG v2 (« Liste des numéros chrono manquants », PROCVB6 : ses sources VB6 ne sont pas dans le dépôt). Joué par la
/// route POST RoutesManquants.Editer (ModuleManquants.cs, réservée à la fonction LNUMCM comme la lecture) dans la transaction et le
/// journal des gestes du socle. Lot 11 du plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// SOURCE (RIG_DEV, SQL-DEV\DEV, lu le 28/11/2026, faute de sources VB6) : MODELE_PROCESSUS décrit LNUMCM par une seule étape, ALNUMCM
/// « Editer la liste des numéros chono manquants » (type DOC) ; MODELE_LI_EDITION y associe le modèle ALNUMCM, transmission IMP ; les
/// demandes que la procédure a laissées (D2406600531, D2408600873) sont VALIDÉES, au processus LNUMCM, sur une affaire, sans nom ni
/// dossier, et portent chacune UNE ligne d'édition ALNUMCM_1_IMP (transmission IMP, bac NORMAL, sceau « sceau-et-signature », non
/// imprimée : LGEDT_ETAT_EDT « I », LGEDT_ETAT_TRS « I »). C'est ce que le geste écrit : une affaire et sa demande
/// (CreationV2.CreerDemandeAsync, validée maintenant), puis la ligne (LignesEditionV2).
/// NON REPRIS (nommé) : le calcul de la V2 qui remplissait NUMERO_CHRONO_MANQUANT avant d'éditer -- la V3 tient ses numéros manquants
/// elle-même (la vue num.NumeroManquant, celle que l'écran lit) : la liste imprimée est celle de l'écran ; l'imprimante (« Brouillon » sur
/// RIG_DEV : le choix du poste).
/// TRACE : famille « demande », « a cree ».
/// </summary>
public static class EcritureManquants
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Numéros manquants";

    /// <summary>Le code processus de RIG v2, aussi la fonction qui ouvre l'écran.</summary>
    public const string CodeProcessus = "LNUMCM";

    /// <summary>Le modèle de document de la liste (LIGNE_EDITION de RIG_DEV).</summary>
    public const string Modele = "ALNUMCM_1_IMP";

    /// <summary>Le sceau des lignes ALNUMCM_1_IMP de RIG_DEV.</summary>
    public const string Sceau = "sceau-et-signature";

    /// <summary>Les gestes écrits : tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Agir];

    /// <summary>La demande LNUMCM validée et sa ligne d'édition de la liste.</summary>
    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var (demande, refus) = await CreationV2.CreerDemandeAsync(geste, CodeProcessus, "V", ct);
        if (demande is null) return IssueGeste.Refuser($"{refus} Rien n'est enregistré.");
        await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_DATE_VALIDATION = @m WHERE DMND_ID_DMND = @id", ct,
            new SqlParameter("@m", geste.Maintenant), new SqlParameter("@id", demande.Id));
        var ligne = await LignesEditionV2.AjouterAsync(geste, demande.Id, Modele, 1, null, Sceau, ct);
        return IssueGeste.Ecrit($"Liste des numéros chrono manquants à imprimer (demande {demande.Numero}).", [EditionsV2.Texte(demande.Id)],
            new TraceGeste("demande", $"la demande {demande.Numero}", EditionsV2.Texte(demande.Id), "a cree", null,
                new { Demande = demande.Numero, Processus = CodeProcessus, Edition = Modele, IdLigneEdition = ligne }));
    }
}
