using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Courriers divers » (PROC_COURRIER_DIVERS, Pont/EcranCourriersDivers.cs) : « Ajouter un courrier divers » puis Valider (RIG v2 :
/// OPE_EDITION_COURRIER_DIVERS.Valider, :81-94 ; écriture : Serveur/Socle/EcransMain/EcritureCourriersDivers.cs), joué par le pont puis
/// relu dans le catalogue du pont ; la demande validée « Création du courrier divers : CD38N » est relue en base. Lot 8 du plan
/// « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU (identifiants 38007xx) : v2.COURRIER_JUDICIAIRE aux types de RIG_DEV (RigDatabase\dbo\Tables\COURRIER_JUDICIAIRE.sql, absente
/// des jeux des écrans générés) et un modèle CD38 ; les paramètres d'édition CONVOC20 (EDIT_INS) et LETTREPC20 (EDIT_PC) de
/// PROC_COURRIERM.sql, posés ici s'ils manquent (aucun paramètre neuf dans les listes des maquettes).
/// </summary>
public sealed class SondeCourriersDivers : ISondeEcran
{
    private const string Nouveau = "CD38N", Libelle = "Lettre de la sonde du parcours";

    public string Code => "PROC_COURRIER_DIVERS";

    public string? Jeu => """
        IF OBJECT_ID(N'v2.PARAMETRE_EDITION') IS NULL
            CREATE TABLE v2.PARAMETRE_EDITION (PRMED_CODE varchar(40) NOT NULL PRIMARY KEY, PRMED_LIBELLE varchar(100) NOT NULL);
        IF COL_LENGTH('v2.PARAMETRE_EDITION', 'PRMED_CODE_PROCESSUS') IS NULL ALTER TABLE v2.PARAMETRE_EDITION ADD PRMED_CODE_PROCESSUS varchar(40) NULL;
        IF OBJECT_ID(N'v2.COURRIER_JUDICIAIRE') IS NULL
            CREATE TABLE v2.COURRIER_JUDICIAIRE (COUJU_ID_COUJU int IDENTITY(3800701, 1) NOT NULL PRIMARY KEY, COUJU_CODE varchar(20) NOT NULL,
                COUJU_LIBELLE varchar(100) NOT NULL, COUJU_CODE_PRMED varchar(40) NOT NULL, COUJU_WITH_DEST bit NOT NULL DEFAULT (1),
                COUJU_ACTIF bit NOT NULL DEFAULT (1), COUJU_TYPE_ENTITE varchar(100) NULL);
        EXEC sp_executesql N'
        IF NOT EXISTS (SELECT 1 FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = ''CONVOC20'')
            INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_CODE_PROCESSUS) VALUES (''CONVOC20'', ''Convocation du banc lot 10'', ''EDIT_INS'');
        IF NOT EXISTS (SELECT 1 FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = ''LETTREPC20'')
            INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_CODE_PROCESSUS) VALUES (''LETTREPC20'', ''Lettre procédure du banc lot 10'', ''EDIT_PC'');
        INSERT INTO v2.COURRIER_JUDICIAIRE (COUJU_CODE, COUJU_LIBELLE, COUJU_CODE_PRMED, COUJU_WITH_DEST, COUJU_ACTIF, COUJU_TYPE_ENTITE)
            VALUES (''CD38'', ''Convocation de la sonde'', ''CONVOC20'', 1, 1, ''INSTANCE'');';
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var champs = new Dictionary<string, string>
        {
            ["Code"] = Nouveau, ["Libellé"] = Libelle, ["Type entité"] = "PROCEDURE_COLLECTIVE", ["Paramètre édition"] = "LETTREPC20",
            ["Avec destinataire"] = "0", ["Actif"] = "1",
        };
        var r = await EcranCourriersDivers.EnregistrerAsync(c.Client, c.Greffe, null, champs);
        if (!r.Fait) return new(false, false, $"Créer le courrier divers {Nouveau} : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");
        var vue = await EcranCourriersDivers.CatalogueAsync(c.Client, c.Greffe);
        var lu = vue.Courriers.FirstOrDefault(x => x.Code == Nouveau);
        var demande = await c.Valeur("SELECT TOP (1) DMND_ETAT_DEMANDE FROM v2.DEMANDE WHERE DMND_NOM = 'Création du courrier divers : CD38N' AND DMND_CODE_PROSS = 'PROC_COURRIER_DIVERS'");
        return vue.Mode == "serveur" && lu is { Libelle: Libelle, TypeEntite: "PROCEDURE_COLLECTIVE", ParamEdition: "LETTREPC20", AvecDestinataire: false, Actif: true }
               && lu.Id.Length > 0 && demande == "V"
            ? new(true, true, $"Créer un courrier divers fait (« {r.Message} »), relu dans le catalogue du pont : {lu.Code}, {lu.ParamEdition}, sans destinataire ; demande validée")
            : new(true, false, $"Créer un courrier divers rendu fait, mais relu {vue.Mode} {(lu is null ? "absent" : $"« {lu.Libelle} / {lu.TypeEntite} / {lu.ParamEdition} »")}, demande « {demande} »");
    }
}
