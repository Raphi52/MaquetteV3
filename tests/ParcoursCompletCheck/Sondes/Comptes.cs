using RigV3.Contrats.Socle;
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Comptes des agents » (PROC_UTILISATEUR, Pont/EcranComptes.cs) : « Créer un compte » (SOC-004, la saisie GESTUTIL de RIG v2,
/// OPE_EDITION_UTILISATEUR ; écriture : Serveur/Socle/Comptes/ServiceComptes.CreerAsync), joué par le pont au nom d'un agent qui a le
/// rôle de gestion des droits, puis relu par le pont : le compte, son service, son rôle et sa ligne au journal des gestes. Jumeau
/// refusé : le même compte Windows une seconde fois (« déjà pris », OPE_EDITION_UTILISATEUR.cs:156), rien de plus n'est créé.
/// Lot 13 du plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU : la gestion des comptes exige le rôle « Gestion des habilitations » (RoutesDroits.RoleGestionDesDroits, SOC-002 CA04,
/// ModuleComptes.cs) -- un rôle, pas une fonction : l'agent qui LIT les écrans faits main (parcours.lecteur, Program.cs, toutes les
/// fonctions) ne l'a pas et recevait 403 (tableau du 28/11). Le jeu le lui donne, et crée le gestionnaire de la sonde, agent du
/// greffe (ni Amitel ni administrateur), avec ce rôle et le rôle BANC qui l'admet.
/// </summary>
public sealed class SondeComptes : ISondeEcran
{
    private const string Gestionnaire = "sonde.habilit13", Cree = "sonde.cree13", Lecteur = "parcours.lecteur";

    public string Code => "PROC_UTILISATEUR";

    public string? Jeu => $"""
        INSERT INTO soc.CompteRole (IdCompte, Role)
        SELECT c.Id, '{RoutesDroits.RoleGestionDesDroits}' FROM soc.Compte c
        WHERE c.CompteWindows = '{Lecteur}'
          AND NOT EXISTS (SELECT 1 FROM soc.CompteRole r WHERE r.IdCompte = c.Id AND r.Role = '{RoutesDroits.RoleGestionDesDroits}');
        INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
        SELECT TOP 1 '{Gestionnaire}', 'HABILIT', 'Sonde', 'SHABIL13', 'Mme', CodeGreffe, Id, 0 FROM soc.Service ORDER BY Id;
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte WHERE CompteWindows = '{Gestionnaire}';
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, '{RoutesDroits.RoleGestionDesDroits}' FROM soc.Compte WHERE CompteWindows = '{Gestionnaire}';
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var client = c.ClientDe(Gestionnaire);
        var ouvert = await EcranComptes.OuvrirAsync(client);
        if (ouvert.Mode != "serveur") return new(false, false, $"Comptes des agents ouvert par le pont : {ouvert.Mode} « {ouvert.Message} »");
        if (ouvert.Services.FirstOrDefault() is not { } service) return new(false, false, "Comptes des agents : aucun service du greffe à donner au compte");

        var demande = new DemandeCompte(null, Cree, "SONDE", "Treize", "Mme", service.Id, ["BANC"], null);
        var vue = await EcranComptes.CreerAsync(client, demande);
        if (vue.Mode != "serveur" || vue.Echec) return new(false, false, $"Créer un compte : {vue.Mode} refusé « {vue.Message} »");

        var relu = await EcranComptes.OuvrirAsync(client);
        var compte = relu.Comptes.SingleOrDefault(x => x.CompteWindows == Cree);
        var trace = relu.Journal.Any(l => l.Ecran == EcranComptes.NomEcran && l.Objet == $"le compte {Cree}");
        var double_ = await EcranComptes.CreerAsync(client, demande);
        var toujoursUn = (await EcranComptes.OuvrirAsync(client)).Comptes.Count(x => x.CompteWindows == Cree) == 1;
        if (compte is { Nom: "SONDE", Prenom: "Treize", Civilite: "Mme", Desactive: false } && compte.IdService == service.Id
            && compte.CodeGreffe == c.Greffe && compte.Roles.Contains("BANC") && trace && double_.Echec && toujoursUn)
            return new(true, true, $"Créer un compte fait (« {vue.Message} »), relu par le pont : {compte.CompteWindows}, {compte.Prenom} {compte.Nom}, " +
                                   $"service {compte.Service}, greffe {compte.CodeGreffe}, rôle BANC, au journal des gestes ; recréé : refusé « {double_.Message} »");
        return new(true, false, $"Créer un compte rendu fait, mais relu : compte {(compte is null ? "absent" : $"{compte.Prenom} {compte.Nom}, service {compte.IdService}, greffe {compte.CodeGreffe}, rôles {string.Join("/", compte.Roles)}")}, " +
                                $"journal {(trace ? "tracé" : "sans ligne")}, recréé {(double_.Echec ? "refusé" : "ACCEPTÉ")} « {double_.Message} »");
    }
}
