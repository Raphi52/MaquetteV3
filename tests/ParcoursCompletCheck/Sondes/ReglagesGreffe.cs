using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Réglages du greffe » (PROC_REGLAGES_GREFFE, Pont/EcranReglages.cs) : modifier la valeur d'un reglage, puis la relire.
/// La modification est reservee a la regle de la V2 (ServiceDroits.PeutModifierLeGreffe : administrateur d'Amitel ou un compte
/// nomme, OPE_EDITER_GREFFE.cs:30-38) : le jeu cree le compte nomme « jouguet », admis dans le greffe, et son propre reglage,
/// pour ne changer aucun reglage qu'un jeu des ecrans generes lit (CERTIF_BOD_DEST, PROC_GESTBOD).
/// </summary>
public sealed class SondeReglagesGreffe : ISondeEcran
{
    private const string Compte = "jouguet", Reglage = "SONDE_PARCOURS";

    public string Code => "PROC_REGLAGES_GREFFE";

    public string? Jeu => $"""
        INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
        SELECT TOP 1 '{Compte}', 'JOUGUET', 'Sonde', 'SJOUGUET', 'M.', CodeGreffe, Id, 0 FROM soc.Service ORDER BY Id;
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte WHERE CompteWindows = '{Compte}';
        INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('{Reglage}', 'Sonde du parcours complet', 'avant');
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var client = c.ClientDe(Compte);
        var vue = await EcranReglages.ModifierAsync(client, c.Greffe, Reglage, "apres la sonde");
        if (vue.Mode != "serveur" || vue.Echec)
            return new(false, false, $"Modifier {Reglage} : {vue.Mode}{(vue.Echec ? " refusé" : "")} « {vue.Message} »");
        var relue = await EcranReglages.OuvrirAsync(client, c.Greffe);
        var valeur = relue.Reglages.FirstOrDefault(r => r.Code == Reglage)?.Valeur;
        return valeur == "apres la sonde"
            ? new(true, true, $"Modifier un réglage fait, relu par le pont (« {valeur} »)")
            : new(true, false, $"Modifier un réglage rendu fait, mais relu « {valeur ?? "absent"} »");
    }
}
