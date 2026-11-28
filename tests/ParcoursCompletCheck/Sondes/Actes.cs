using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Actes numérisés » (PROC_ACTES_NUM, Pont/EcranActes.cs + EcranActesBase.cs) : « Définir le document comme non numérisable » puis
/// « … comme numérisable », le menu de la grille de RIG v2 (OPE_RESULTAT.cs:253-284 ; écriture : Serveur/Socle/EcransMain/EcritureActes.cs),
/// joués par le pont puis relus par le pont : l'acte quitte la liste des numérisables et entre dans celle des non numérisables (case
/// « Lister actes non numérisables »), puis revient. Le JUMEAU refusé : le même geste par l'agent du banc (banc.ecrans), sans la fonction
/// ACTENUM_NUMERISABLE (OPE_RESULTAT.cs:180-185), rien d'écrit ni de journal. Lot 10 du plan « RIG v3 complet, mesuré par le banc du
/// parcours complet » (28/11/2026).
///
/// LE JEU : les colonnes que la lecture lit et que les jeux des écrans générés n'ont pas (ACTDP_SANS_DOCUMENT : 500 au 28/11,
/// R_CATEGORIED_LIBELLE_ECRAN, R_ABREGEPIEC_SYNONYME), aux types de RIG_DEV (RigDatabase\dbo\Tables\ACTE_DEPOSE.sql, CATEGORIE_DEPOT.sql,
/// ABREGE_PIECE.sql) ; un dépôt de 2015 (2015D400001, catégorie Y hors comptes annuels), son action au registre et un acte de statuts
/// (4000001), sans dossier ; la fonction ACTENUM_NUMERISABLE (absente du catalogue du banc), un rôle qui l'a et le compte
/// « sonde.l10.actes ».
/// </summary>
public sealed class SondeActes : ISondeEcran
{
    private const string Compte = "sonde.l10.actes", Depot = "2015D400001", Acte = "4000001";

    public string Code => "PROC_ACTES_NUM";

    public string? Jeu => $"""
        IF NOT EXISTS (SELECT 1 FROM soc.Fonction WHERE Code = 'ACTENUM_NUMERISABLE')
            INSERT INTO soc.Fonction (Code, Libelle, TypeV2) VALUES ('ACTENUM_NUMERISABLE', 'ACTENUM_NUMERISABLE', 'FONC');
        IF NOT EXISTS (SELECT 1 FROM soc.Role WHERE Nom = 'SONDE_L10')
            INSERT INTO soc.Role (Nom, Libelle) VALUES ('SONDE_L10', 'Sonde du parcours complet, lot 10');
        IF NOT EXISTS (SELECT 1 FROM soc.RoleFonction WHERE Role = 'SONDE_L10' AND Fonction = 'ACTENUM_NUMERISABLE')
            INSERT INTO soc.RoleFonction (Role, Fonction) VALUES ('SONDE_L10', 'ACTENUM_NUMERISABLE');
        IF NOT EXISTS (SELECT 1 FROM soc.Compte WHERE CompteWindows = '{Compte}')
        BEGIN
            INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
            SELECT TOP 1 '{Compte}', 'ACTES', 'Sonde L10', 'SL10ACT', 'M.', CodeGreffe, Id, 0 FROM soc.Service ORDER BY Id;
            INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, r.Nom FROM soc.Compte CROSS JOIN (VALUES ('BANC'), ('SONDE_L10')) AS r (Nom)
                WHERE CompteWindows = '{Compte}';
        END
        IF OBJECT_ID('v2.CATEGORIE_DEPOT') IS NULL CREATE TABLE v2.CATEGORIE_DEPOT (R_CATEGORIED_VALEUR varchar(3) NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.CATEGORIE_DEPOT', 'R_CATEGORIED_LIBELLE_ECRAN') IS NULL
            ALTER TABLE v2.CATEGORIE_DEPOT ADD R_CATEGORIED_LIBELLE_ECRAN varchar(35) NOT NULL DEFAULT ('');
        IF COL_LENGTH('v2.CATEGORIE_DEPOT', 'R_CATEGORIED_COMPTES_ANNUEL') IS NULL ALTER TABLE v2.CATEGORIE_DEPOT ADD R_CATEGORIED_COMPTES_ANNUEL bit NULL;
        IF OBJECT_ID('v2.ABREGE_PIECE') IS NULL CREATE TABLE v2.ABREGE_PIECE (R_ABREGEPIEC_VALEUR varchar(16) NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.ABREGE_PIECE', 'R_ABREGEPIEC_SYNONYME') IS NULL ALTER TABLE v2.ABREGE_PIECE ADD R_ABREGEPIEC_SYNONYME varchar(16) NOT NULL DEFAULT ('');
        IF COL_LENGTH('v2.ABREGE_PIECE', 'R_ABREGEPIEC_DEVELOPPE_EDIT') IS NULL ALTER TABLE v2.ABREGE_PIECE ADD R_ABREGEPIEC_DEVELOPPE_EDIT varchar(256) NULL;
        IF OBJECT_ID('v2.DEPOT_RCS') IS NULL
            CREATE TABLE v2.DEPOT_RCS (DPTRC_ID_DPTRC int NOT NULL PRIMARY KEY, DPTRC_NUM_DEPOT varchar(12) NOT NULL, DPTRC_DATE datetime NULL,
                DPTRC_CATEGORIE_DEPOT varchar(1) NULL);
        IF OBJECT_ID('v2.ACTE_DEPOSE') IS NULL
            CREATE TABLE v2.ACTE_DEPOSE (ACTDP_ID_ACDPS int NOT NULL PRIMARY KEY, ACTDP_ID_DPTRC int NULL, ACTDP_NUM_ORDRE int NULL,
                ACTDP_ABREGE_PIECE varchar(16) NOT NULL, ACTDP_DESCRIPTION varchar(256) NULL, ACTDP_DATE datetime NULL, ACTDP_GUID_VIDIGED uniqueidentifier NULL);
        IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_SANS_DOCUMENT') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_SANS_DOCUMENT bit NOT NULL DEFAULT (0);
        IF OBJECT_ID('v2.DEMANDE') IS NULL
            CREATE TABLE v2.DEMANDE (DMND_ID_DMND int NOT NULL PRIMARY KEY, DMND_NUM_DEMANDE varchar(11) NOT NULL, DMND_ETAT_DEMANDE varchar(1) NOT NULL);
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_ACTRC') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_ACTRC int NULL;
        IF OBJECT_ID('v2.DEMAT_DAS') IS NULL CREATE TABLE v2.DEMAT_DAS (DMTDAS_ID_DMTDAS int NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.DEMAT_DAS', 'DMTDAS_ID_DMND') IS NULL ALTER TABLE v2.DEMAT_DAS ADD DMTDAS_ID_DMND int NULL;
        IF OBJECT_ID('v2.DEMAT_DCA') IS NULL CREATE TABLE v2.DEMAT_DCA (DMTDCA_ID_DMTDCA int NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.DEMAT_DCA', 'DMTDCA_ID_DMND') IS NULL ALTER TABLE v2.DEMAT_DCA ADD DMTDCA_ID_DMND int NULL;
        IF OBJECT_ID('v2.DEMAT_LIASSE_CFE') IS NULL CREATE TABLE v2.DEMAT_LIASSE_CFE (DMTLCF_ID_DMTLCF int NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.DEMAT_LIASSE_CFE', 'DMTLCF_ID_DMND') IS NULL ALTER TABLE v2.DEMAT_LIASSE_CFE ADD DMTLCF_ID_DMND int NULL;
        EXEC sp_executesql N'
        IF NOT EXISTS (SELECT 1 FROM v2.CATEGORIE_DEPOT WHERE R_CATEGORIED_VALEUR = ''Y'')
            INSERT INTO v2.CATEGORIE_DEPOT (R_CATEGORIED_VALEUR, R_CATEGORIED_COMPTES_ANNUEL) VALUES (''Y'', 0);
        UPDATE v2.CATEGORIE_DEPOT SET R_CATEGORIED_LIBELLE_ECRAN = ''Dépôt d''''actes'' WHERE R_CATEGORIED_VALEUR = ''Y'' AND R_CATEGORIED_LIBELLE_ECRAN = '''';
        IF NOT EXISTS (SELECT 1 FROM v2.ABREGE_PIECE WHERE R_ABREGEPIEC_VALEUR = ''L10STAT'')
            INSERT INTO v2.ABREGE_PIECE (R_ABREGEPIEC_VALEUR, R_ABREGEPIEC_SYNONYME, R_ABREGEPIEC_DEVELOPPE_EDIT) VALUES (''L10STAT'', ''STAT'', ''Statuts mis à jour'');
        INSERT INTO v2.DEPOT_RCS (DPTRC_ID_DPTRC, DPTRC_NUM_DEPOT, DPTRC_DATE, DPTRC_CATEGORIE_DEPOT) VALUES (4000001, ''{Depot}'', ''2015-06-15'', ''Y'');
        INSERT INTO v2.ACTE_DEPOSE (ACTDP_ID_ACDPS, ACTDP_ID_DPTRC, ACTDP_NUM_ORDRE, ACTDP_ABREGE_PIECE, ACTDP_DESCRIPTION, ACTDP_DATE, ACTDP_SANS_DOCUMENT)
            VALUES ({Acte}, 4000001, 1, ''L10STAT'', ''Statuts mis à jour (sonde L10)'', ''2015-06-10'', 0);
        INSERT INTO rec.ActionRegistre (IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification) VALUES (4000001, ''DEPOT'', ''V'', ''V'', 0);';
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        Task<VueActes> Liste(bool nonNumerisables)
            => EcranActes.TrouverAsync(c.Client, c.Greffe, new CriteresActes(Depot, "", "", null, null, nonNumerisables, false, 50));
        string Lu(VueActes v) => $"{v.Mode}, {v.Lignes.Length} ligne(s){(v.Message is { } m && v.Mode != "serveur" ? " — " + m : "")}";

        var depart = await Liste(false);
        if (depart is not { Mode: "serveur", Lignes: [{ Id: Acte, Numerisable: true }] })
            return new(false, false, $"l'acte {Acte} du dépôt {Depot} n'est pas lu numérisable avant le geste ({Lu(depart)})");

        // Le jumeau refusé : banc.ecrans n'a pas la fonction ACTENUM_NUMERISABLE.
        var journal = await c.Journal();
        var refus = await EcranActes.ModifierSansDocumentAsync(c.Client, c.Greffe, Acte, sansDocument: true);
        if (refus.Fait || await c.Journal() != journal || (await Liste(false)).Lignes.Length != 1)
            return new(false, false, $"non numérisable sans la fonction : {(refus.Fait ? "fait" : "refusé")} « {refus.Message} »");

        var agent = c.ClientDe(Compte);
        var r = await EcranActes.ModifierSansDocumentAsync(agent, c.Greffe, Acte, sansDocument: true);
        if (!r.Fait) return new(false, false, $"Définir le document comme non numérisable : refusé « {r.Message} »");
        var numerisables = await Liste(false);
        var nonNumerisables = await Liste(true);
        if (numerisables.Lignes.Length != 0 || nonNumerisables is not { Lignes: [{ Id: Acte, Numerisable: false }] })
            return new(true, false, $"non numérisable rendu fait (« {r.Message} »), mais relu : numérisables {Lu(numerisables)}, non numérisables {Lu(nonNumerisables)}");

        // Le même geste une seconde fois : RIG v2 ne l'offre pas (OPE_RESULTAT.cs:183) ; puis l'inverse, qui rend l'acte numérisable.
        var deja = await EcranActes.ModifierSansDocumentAsync(agent, c.Greffe, Acte, sansDocument: true);
        var retour = await EcranActes.ModifierSansDocumentAsync(agent, c.Greffe, Acte, sansDocument: false);
        var final = await Liste(false);
        return !deja.Fait && retour.Fait && final.Lignes is [{ Id: Acte, Numerisable: true }]
            ? new(true, true, $"non numérisable fait (« {r.Message} »), relu par le pont dans « Lister actes non numérisables » ; refait refusé ; "
                              + $"numérisable fait, relu dans la liste ; refus sans ACTENUM_NUMERISABLE « {refus.Message} »")
            : new(true, false, $"non numérisable fait et relu, mais refait {(deja.Fait ? "accepté" : "refusé")}, numérisable "
                               + $"{(retour.Fait ? "fait" : "refusé « " + retour.Message + " »")}, relu {Lu(final)}");
    }
}
