using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Ouvrir un dossier » (OUVERTURE_DOSSIER, Pont/EcranOuvertureDossier.cs + EcranOuvertureDossierBase.cs). Lot 7 du plan « RIG v3
/// complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU : la colonne R_FORMEJURID_ABREGE_FRANCAIS (varchar(16) dans RIG_DEV, RigDatabase\dbo\Tables\FORME_JURIDIQUE_UTI.sql) que la
/// lecture des dossiers ouverts joint (LectureDossiersOuverts.cs) manquait à la base du banc (500 au 28/11) : ajoutée si absente. Les
/// données de l'écran se mesurent alors seules (EcranOuvertureDossier.VueAsync, « serveur »).
///
/// LE GESTE RESTE ROUGE, sans faux vert : « Ouvrir » n'a pas d'origine RIG v2 -- aucun écran de l'ancienne application ne crée un
/// dossier RCS hors d'une formalité d'immatriculation (EcranOuvertureDossierBase.SansEcritureV2 ; aucune procédure OUVERTURE_DOSSIER
/// dans D:\RigApplication\Source\RIG\DLL\Processus). La sonde joue le geste par le pont et rend son refus.
/// </summary>
public sealed class SondeOuvertureDossier : ISondeEcran
{
    public string Code => "OUVERTURE_DOSSIER";

    public string? Jeu => """
        IF OBJECT_ID('v2.FORME_JURIDIQUE_UTI') IS NULL
            CREATE TABLE v2.FORME_JURIDIQUE_UTI (R_FORMEJURID_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_FORMEJURID_LIBELLE_ECRAN varchar(35) NOT NULL);
        IF COL_LENGTH('v2.FORME_JURIDIQUE_UTI', 'R_FORMEJURID_LIBELLE_ECRAN') IS NULL
            ALTER TABLE v2.FORME_JURIDIQUE_UTI ADD R_FORMEJURID_LIBELLE_ECRAN varchar(35) NULL;
        IF COL_LENGTH('v2.FORME_JURIDIQUE_UTI', 'R_FORMEJURID_ABREGE_FRANCAIS') IS NULL
            ALTER TABLE v2.FORME_JURIDIQUE_UTI ADD R_FORMEJURID_ABREGE_FRANCAIS varchar(16) NULL;
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var r = await EcranOuvertureDossier.OuvrirAsync(c.Client, c.Greffe,
            new FicheOuverture("Guichet", "437000001", DateOnly.FromDateTime(c.Jour),
                               "SONDE OUVERTURE", "SARL", 1000m, "EUR"));
        return new(false, false, $"aucun geste RIG v2 à reproduire : « Ouvrir » répond « {r.Message} » (un dossier RCS naît d'une formalité " +
                                 "d'immatriculation dans RIG v2) ; la ligne reste rouge sur son geste, sans faux vert");
    }
}
