using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Recherche d'affaires » (PROC_RECHAFF, Pont/EcranRechAffaires*.cs) : rattacher une demande à l'affaire choisie (le Valider de
/// PROC_RECHAFF reçu avec une demande, PROC_RECHAFF.cs:33-61 ; écriture : Serveur/Socle/EcransMain/EcritureRechAffaires.cs), joué par
/// le pont puis relu par la recherche du pont : cherchée par son numéro, la demande est dans l'affaire choisie, avec la ventilation et
/// l'article de sa facture (le solde suit). Lot 8 du plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU (identifiants 38006xx) : l'affaire d'origine 3800611 n'a que la demande D3800621 (ses ventilations suivent), une
/// ventilation de 50 € ; la facture F3800621 de la demande porte un article de 20 € ; l'affaire choisie 3800612 est vide. Tables et
/// colonnes créées si absentes, aux types de RIG_DEV (RigDatabase\dbo\Tables\VENTILATION.sql, ARTICLE_UTILISE.sql).
/// </summary>
public sealed class SondeRechAffaires : ISondeEcran
{
    private const string Demande = "D3800621", Cible = "3800612";

    public string Code => "PROC_RECHAFF";

    public string? Jeu => """
        IF OBJECT_ID(N'v2.AFFAIRE') IS NULL CREATE TABLE v2.AFFAIRE (AFFRE_ID_AFFRE int NOT NULL PRIMARY KEY, AFFRE_DATE_CREATION datetime NULL);
        IF COL_LENGTH('v2.AFFAIRE', 'AFFRE_LIBELLE') IS NULL ALTER TABLE v2.AFFAIRE ADD AFFRE_LIBELLE varchar(70) NULL;
        IF COL_LENGTH('v2.AFFAIRE', 'AFFRE_ID_UTLST_EN_COURS') IS NULL ALTER TABLE v2.AFFAIRE ADD AFFRE_ID_UTLST_EN_COURS int NULL;
        IF OBJECT_ID(N'v2.VENTILATION') IS NULL
            CREATE TABLE v2.VENTILATION (VENTI_ID_VENTI int NOT NULL PRIMARY KEY, VENTI_ID_AFFRE int NULL, VENTI_MONTANT money NOT NULL);
        IF OBJECT_ID(N'v2.FACTURE') IS NULL
            CREATE TABLE v2.FACTURE (FACTU_NUMERO_FACTURE varchar(25) NOT NULL PRIMARY KEY, FACTU_NUM_DEMANDE varchar(32) NOT NULL);
        IF OBJECT_ID(N'v2.ARTICLE_UTILISE') IS NULL
            CREATE TABLE v2.ARTICLE_UTILISE (ARTUT_ID_ARTUT int NOT NULL PRIMARY KEY, ARTUT_ID_AFFRE int NULL, ARTUT_MONTANT_TTC money NOT NULL);
        IF COL_LENGTH('v2.ARTICLE_UTILISE', 'ARTUT_NUMERO_FACTURE') IS NULL ALTER TABLE v2.ARTICLE_UTILISE ADD ARTUT_NUMERO_FACTURE varchar(25) NULL;
        EXEC sp_executesql N'
        INSERT INTO v2.AFFAIRE (AFFRE_ID_AFFRE, AFFRE_DATE_CREATION, AFFRE_LIBELLE) VALUES
            (3800611, @aujourdhui, ''SONDE RECHAFF ORIGINE''), (3800612, @aujourdhui, ''SONDE RECHAFF CIBLE'');
        INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_ID_AFFRE, DMND_EN_COURS, DMND_DATE_CREATION)
            VALUES (3800621, ''D3800621'', ''V'', ''IPE'', 3800611, 0, @aujourdhui);
        INSERT INTO v2.VENTILATION (VENTI_ID_VENTI, VENTI_ID_AFFRE, VENTI_MONTANT) VALUES (3800631, 3800611, 50);
        INSERT INTO v2.FACTURE (FACTU_NUMERO_FACTURE, FACTU_NUM_DEMANDE) VALUES (''F3800621'', ''D3800621'');
        INSERT INTO v2.ARTICLE_UTILISE (ARTUT_ID_ARTUT, ARTUT_ID_AFFRE, ARTUT_MONTANT_TTC, ARTUT_NUMERO_FACTURE) VALUES (3800641, 3800611, 20, ''F3800621'');
        ', N'@aujourdhui datetime', @aujourdhui;
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var r = await EcranRechAffaires.ValiderAsync(c.Client, c.Greffe, Cible, Demande);
        if (!r.Fait) return new(false, false, $"Rattacher {Demande} à l'affaire {Cible} : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");
        var vue = await EcranRechAffaires.TrouverAsync(c.Client, c.Greffe,
            new CriteresAffaires(Demande, "", "", "", "", "", "", "", "", null, null), null, true, null);
        return vue is { Mode: "serveur", Lignes: [var a] } && a.Numero == Cible && a.Demandes == Demande && a.Factures == "F3800621" && a.SoldeValeur == 30m
            ? new(true, true, $"Rattacher une demande fait (« {r.Message} »), relu par la recherche du pont : {Demande} dans l'affaire {a.Numero}, solde {a.Solde}")
            : new(true, false, $"Rattacher rendu fait, mais la recherche de {Demande} relit {vue.Mode} : "
                               + string.Join(" ; ", vue.Lignes.Select(l => $"affaire {l.Numero} ({l.Demandes}, solde {l.SoldeValeur})")));
    }
}
