using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Mon accueil » (PROC_ACCUEIL_PARAM, Pont/EcranParametresAccueilBase.cs) : « Enregistrer mes réglages d'accueil », le geste que RIG v2
/// joue à la fermeture de l'onglet (FORM_SKIN_ACCUEIL_PARAM.cs:97-103, _SaveParametrage :620-648, User.SetParametre ; écriture :
/// Serveur/Socle/EcransMain/EcritureParametresAccueil.cs), joué par le pont puis relu par le pont : un réglage existant prend la nouvelle
/// valeur (sans seconde ligne), un réglage absent est créé, et les animations d'un agent non administrateur ne s'écrivent pas (:640).
/// Jumeau refusé : une largeur hors des bornes du contrôle de RIG v2 (1000 à 10000, Designer.cs:1201-1210), rien n'est écrit.
/// Lot 13 du plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU : v2.PARAMETRE_UTILISATEUR si absente, aux types de RIG_DEV (RigDatabase\dbo\Tables\PARAMETRE_UTILISATEUR.sql) ; l'agent de la
/// sonde, ni Amitel ni administrateur, avec son compte RIG v2 (soc.Compte.IdV2 4300001) et une largeur maximale déjà enregistrée (1600).
/// </summary>
public sealed class SondeParametresAccueil : ISondeEcran
{
    private const string Agent = "sonde.accueil13";
    private const int IdV2 = 4300001;
    private const string Largeur = "ConsoleAccueil_Form_MaxWidth", Intervalle = "ConsoleAccueil_IntervalSlideshowAlertes",
                         Autofocus = "ConsoleAccueil_AutofocusOnClickIconEtpOpe", Animations = "ConsoleAccueil_AllowAnimations";

    public string Code => "PROC_ACCUEIL_PARAM";

    public string? Jeu => JeuSonde.EnDeuxTemps($"""
        IF OBJECT_ID(N'v2.PARAMETRE_UTILISATEUR') IS NULL
            CREATE TABLE v2.PARAMETRE_UTILISATEUR (PRMTR_ID_UTLST int NULL, PRMTR_NOM_PARAMETRE varchar(255) NOT NULL,
                PRMTR_VALEUR_PARAMETRE varchar(8000) NULL, PRMTR_ID_PRMTR int IDENTITY(1, 1) NOT NULL PRIMARY KEY NONCLUSTERED);
        INSERT INTO soc.Compte (IdV2, CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
        SELECT TOP 1 {IdV2}, '{Agent}', 'ACCUEIL', 'Sonde', 'SACCU13', 'M.', CodeGreffe, Id, 0 FROM soc.Service ORDER BY Id;
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte WHERE CompteWindows = '{Agent}';
        """, $"""
        INSERT INTO v2.PARAMETRE_UTILISATEUR (PRMTR_ID_UTLST, PRMTR_NOM_PARAMETRE, PRMTR_VALEUR_PARAMETRE) VALUES ({IdV2}, '{Largeur}', '1600');
        """);

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var client = c.ClientDe(Agent);
        var avant = await EcranParametresAccueil.ReglagesAsync(client, c.Greffe);
        if (avant.Mode != "serveur" || !avant.AgentV2 || avant.Valeurs.GetValueOrDefault(Largeur) != "1600")
            return new(false, false, $"Mon accueil lu par le pont : {avant.Mode} « {avant.Message} », largeur « {avant.Valeurs.GetValueOrDefault(Largeur)} »");

        var r = await EcranParametresAccueil.EnregistrerAsync(client, c.Greffe, new Dictionary<string, string>
        {
            [Largeur] = "1800", [Intervalle] = "7000", [Autofocus] = "false", [Animations] = "True",
        });
        if (!r.Fait) return new(false, false, $"Enregistrer mes réglages d'accueil : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");

        var jumeau = await EcranParametresAccueil.EnregistrerAsync(client, c.Greffe, new Dictionary<string, string> { [Largeur] = "900" });
        var relu = (await EcranParametresAccueil.ReglagesAsync(client, c.Greffe)).Valeurs;
        var lignesLargeur = await c.Valeur($"SELECT COUNT(*) FROM v2.PARAMETRE_UTILISATEUR WHERE PRMTR_ID_UTLST = {IdV2} AND PRMTR_NOM_PARAMETRE = '{Largeur}'");
        return relu.GetValueOrDefault(Largeur) == "1800" && relu.GetValueOrDefault(Intervalle) == "7000" && relu.GetValueOrDefault(Autofocus) == "False"
               && !relu.ContainsKey(Animations) && lignesLargeur == "1" && !jumeau.Fait
            ? new(true, true, $"Enregistrer mes réglages d'accueil fait (« {r.Message} »), relu par le pont : largeur 1600 → {relu[Largeur]} (une ligne), " +
                              $"intervalle {relu[Intervalle]} créé, autofocus {relu[Autofocus]}, animations non écrites (agent non administrateur) ; " +
                              $"largeur 900 : refusé « {jumeau.Message} »")
            : new(true, false, $"Enregistrer rendu fait, mais relu : {string.Join(", ", relu.Select(v => $"{v.Key}={v.Value}"))} ({lignesLargeur} ligne(s) largeur) ; " +
                               $"jumeau {(jumeau.Fait ? "ACCEPTÉ" : "refusé")} « {jumeau.Message} »");
    }
}
