using System.Text.Json;
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Commandes web » (PROC_COMMANDES_WEB, Pont/EcranCommandesWeb.cs + EcranCommandesWebBase.cs) : VALIDER une commande sur le dossier
/// cherché DANS LA BASE, comme l'écran le fait -- choisir la commande (le pont cherche son dossier par le SIREN reçu, la recherche de
/// « Réquisitions », REQUI de RIG v2 : Serveur/Socle/EcransMain/LectureEntreprisesRequi.cs), retenir le dossier trouvé, Decider
/// « Valider » ; règles de NéoRIG (TraiterCommandeWebService.ValiderAsync ; écriture : Serveur/Socle/EcransMain/EcritureCommandesWeb.cs,
/// sources citées). Relue par le pont dans une session NEUVE : la commande est sous la tuile « Validée », rattachée au dossier retenu
/// par la demande de sa validation. Lot 7 du plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026) ; la mise en
/// attente (geste secondaire) est prouvée par tests/EcransMainBaseCheck/ParcoursLot7.cs.
///
/// UN CERTIFICAT DE NON-FAILLITE (famille 5) : son escalier se fait en base (dossier concerné, certificat, édition). Un état
/// d'endettement, non : ses types d'inscription commandés viennent du flux XML de la commande, que la V3 ne lit pas
/// (SessionCommandesWeb.SansTypesRecus) ; le serveur le validerait (ParcoursLot7.cs, 3700402), l'écran ne peut pas l'y conduire.
///
/// LE JEU : les tables de la recherche des dossiers (celles de SondeRequisitions, rejouées : chaque ordre est « si absente ») ;
/// COMMANDE_WEB (absente : 500 au 28/11) créée si absente, aux types de NéoRIG (D:\RIG-V3\Documentation\scripts\
/// 1824-creer-commande-web.sql:25-52), les colonnes du traitement ajoutées si un jeu ne les a pas ; ce que Valider écrit dans DEMANDE et
/// AFFAIRE (RigDatabase\dbo\Tables\DEMANDE.sql, AFFAIRE.sql) ; la commande 3700001 À TRAITER, au SIREN et à la dénomination d'un dossier
/// RCS de la base du banc, en cours (ni radié ni en procédure collective : son certificat de non-faillite est délivrable).
/// </summary>
// fix-ok: refus du chef (main bad949c) -- la sonde prouvait « Mettre en attente », un geste secondaire ; elle prouve maintenant le geste
// principal, Valider, sur le dossier cherche en base (vu rouge avant la correction : « Valider » refuse SansEcriture).
public sealed class SondeCommandesWeb : ISondeEcran
{
    private const int Id = 3700001;

    public string Code => "PROC_COMMANDES_WEB";

    public string? Jeu => new SondeRequisitions().Jeu + """

        IF OBJECT_ID('v2.COMMANDE_WEB') IS NULL
            CREATE TABLE v2.COMMANDE_WEB (CMDWB_ID_CMDWB int NOT NULL PRIMARY KEY, CMDWB_REFERENCE_COMMANDE varchar(100) NULL,
                CMDWB_NUMERO_SUIVI varchar(100) NULL, CMDWB_DATE_COMMANDE datetime NULL, CMDWB_FAMILLE tinyint NULL, CMDWB_TYPE_PRODUIT varchar(10) NULL,
                CMDWB_ORIGINE varchar(3) NULL, CMDWB_SIREN varchar(9) NULL, CMDWB_DENOMINATION varchar(255) NULL, CMDWB_NOM_REQUERANT varchar(250) NULL,
                CMDWB_ETAT char(1) NOT NULL DEFAULT ('A'), CMDWB_MOTIF varchar(500) NULL, CMDWB_DATE_TRAITEMENT datetime NULL);
        IF COL_LENGTH('v2.COMMANDE_WEB', 'CMDWB_ID_UTLST') IS NULL ALTER TABLE v2.COMMANDE_WEB ADD CMDWB_ID_UTLST int NULL;
        IF COL_LENGTH('v2.COMMANDE_WEB', 'CMDWB_DATE_MODIFICATION') IS NULL
            ALTER TABLE v2.COMMANDE_WEB ADD CMDWB_DATE_MODIFICATION datetime NOT NULL DEFAULT (getdate());
        IF COL_LENGTH('v2.COMMANDE_WEB', 'CMDWB_ID_DMND') IS NULL ALTER TABLE v2.COMMANDE_WEB ADD CMDWB_ID_DMND int NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_UTLST') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_UTLST int NULL;
        EXEC sp_executesql N'
        INSERT INTO v2.COMMANDE_WEB (CMDWB_ID_CMDWB, CMDWB_REFERENCE_COMMANDE, CMDWB_NUMERO_SUIVI, CMDWB_DATE_COMMANDE, CMDWB_FAMILLE,
            CMDWB_TYPE_PRODUIT, CMDWB_ORIGINE, CMDWB_SIREN, CMDWB_DENOMINATION, CMDWB_NOM_REQUERANT, CMDWB_ETAT)
        SELECT TOP (1) 3700001, ''SONDE-LOT7-1'', ''SONDE-LOT7-1-1'', DATEADD(hour, 8, @aujourdhui), 5, ''FA-CF'', ''IG'', s.Siren,
               e.Denomination, ''REQUERANT SONDE'', ''A''
        FROM rec.DossierRcs AS s JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        WHERE s.DateRadiation IS NULL AND s.Siren LIKE ''[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'' AND LEN(e.Denomination) >= 4
          AND NOT EXISTS (SELECT 1 FROM v2.PROCEDURE_COLLECTIVE AS p WHERE p.PRCLL_ID_RPRTR = s.IdEntreprise)
          AND NOT EXISTS (SELECT 1 FROM rec.DossierRcs AS d WHERE d.Siren = s.Siren AND d.Id <> s.Id)
        ORDER BY s.Id;',
            N'@aujourdhui datetime', @aujourdhui;
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        static JsonElement V(object? v) => JsonSerializer.SerializeToElement(v);
        var vue = await EcranCommandesWeb.OuvrirAsync(c.Client, c.Greffe);
        try
        {
            if (vue.Mode != "serveur" || vue.Lignes.All(l => l.Id != Id))
                return new(false, false, $"La commande {Id} n'est pas lue « à traiter » par le pont ({vue.Mode} « {vue.Provenance} »)");
            var choisie = await EcranCommandesWeb.AgirAsync(c.Client, c.Greffe, vue.Session, "Choisir", V(Id));
            if (choisie.Panneau?.Rapprochement is not { } rapprochement
                || rapprochement.Entites.FirstOrDefault(e => e.SirenConcorde == true && !e.Radie) is not { } dossier)
                return new(false, false, $"Rapprochement de {Id} par le pont : aucun dossier au SIREN reçu lu en base "
                                       + $"(« {choisie.Panneau?.Rapprochement?.Message} », {choisie.Panneau?.Rapprochement?.Entites.Length ?? 0} dossier(s))");
            await EcranCommandesWeb.AgirAsync(c.Client, c.Greffe, vue.Session, "Retenir", V(dossier.Cle));
            var apres = await EcranCommandesWeb.AgirAsync(c.Client, c.Greffe, vue.Session, "Decider", V("Valider"));
            if (apres.Message is not { Severite: "succes" } m)
                return new(false, false, $"Valider {Id} sur le dossier {dossier.Gestion} : refusé « {apres.Message?.Texte} »");

            // Relue dans une session NEUVE : les commandes viennent de la base, pas de la memoire de la premiere.
            var relue = await EcranCommandesWeb.OuvrirAsync(c.Client, c.Greffe);
            try
            {
                await EcranCommandesWeb.AgirAsync(c.Client, c.Greffe, relue.Session, "ChoisirOnglet", V("Validee"));
                var panneau = (await EcranCommandesWeb.AgirAsync(c.Client, c.Greffe, relue.Session, "Choisir", V(Id))).Panneau;
                var attendu = $"Rattachée au dossier {dossier.Gestion} · demande n° ";
                return panneau is { Etat.Libelle: var etat, Traitee: { Rejetee: false, Rattachement: { } rattachement } } && rattachement.StartsWith(attendu, StringComparison.Ordinal)
                       && relue.Lignes.All(l => l.Id != Id)
                    ? new(true, true, $"Valider fait (« {m.Texte[..m.Texte.IndexOf(')', StringComparison.Ordinal)]}) »), relu par le pont : commande {Id} « {etat} », "
                                    + $"« {rattachement} », hors de la tuile « À traiter »")
                    : new(true, false, $"Valider rendu fait, mais relu : panneau {(panneau is null ? "absent" : $"état « {panneau.Etat?.Libelle} », rattachement « {panneau.Traitee?.Rattachement} »")}");
            }
            finally { EcranCommandesWeb.Fermer(relue.Session); }
        }
        finally { EcranCommandesWeb.Fermer(vue.Session); }
    }
}
