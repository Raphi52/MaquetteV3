using System.Text.Json;
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Commandes web » (PROC_COMMANDES_WEB, Pont/EcranCommandesWeb.cs + EcranCommandesWebBase.cs) : « Mettre en attente » une
/// commande à traiter, avec son motif, comme l'écran le fait (choisir la commande, ouvrir la bande, saisir le motif, Decider) ; règles
/// de NéoRIG (TraiterCommandeWebService.MettreEnAttenteAsync ; écriture : Serveur/Socle/EcransMain/EcritureCommandesWeb.cs), jouée par
/// le pont puis relue par le pont dans une session NEUVE : la commande est sous la tuile « En attente », avec son motif. Lot 7 du plan
/// « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU : COMMANDE_WEB (absente : 500 au 28/11) créée si absente, aux types de NéoRIG (D:\RIG-V3\Documentation\scripts\
/// 1824-creer-commande-web.sql:47-52), les colonnes du traitement ajoutées si un jeu ne les a pas ; la commande 3700001 à traiter.
/// </summary>
public sealed class SondeCommandesWeb : ISondeEcran
{
    private const int Id = 3700001;
    private const string Motif = "Pièce manquante (sonde du parcours)";

    public string Code => "PROC_COMMANDES_WEB";

    public string? Jeu => """
        IF OBJECT_ID('v2.COMMANDE_WEB') IS NULL
            CREATE TABLE v2.COMMANDE_WEB (CMDWB_ID_CMDWB int NOT NULL PRIMARY KEY, CMDWB_REFERENCE_COMMANDE varchar(50) NULL,
                CMDWB_NUMERO_SUIVI varchar(50) NULL, CMDWB_DATE_COMMANDE datetime NULL, CMDWB_FAMILLE tinyint NULL, CMDWB_TYPE_PRODUIT varchar(10) NULL,
                CMDWB_ORIGINE varchar(5) NULL, CMDWB_SIREN varchar(9) NULL, CMDWB_DENOMINATION varchar(250) NULL, CMDWB_NOM_REQUERANT varchar(250) NULL,
                CMDWB_ETAT char(1) NOT NULL DEFAULT ('A'), CMDWB_MOTIF varchar(500) NULL, CMDWB_DATE_TRAITEMENT datetime NULL);
        IF COL_LENGTH('v2.COMMANDE_WEB', 'CMDWB_ID_UTLST') IS NULL ALTER TABLE v2.COMMANDE_WEB ADD CMDWB_ID_UTLST int NULL;
        IF COL_LENGTH('v2.COMMANDE_WEB', 'CMDWB_DATE_MODIFICATION') IS NULL
            ALTER TABLE v2.COMMANDE_WEB ADD CMDWB_DATE_MODIFICATION datetime NOT NULL DEFAULT (getdate());
        EXEC sp_executesql N'
        INSERT INTO v2.COMMANDE_WEB (CMDWB_ID_CMDWB, CMDWB_REFERENCE_COMMANDE, CMDWB_NUMERO_SUIVI, CMDWB_DATE_COMMANDE, CMDWB_FAMILLE,
            CMDWB_TYPE_PRODUIT, CMDWB_ORIGINE, CMDWB_SIREN, CMDWB_DENOMINATION, CMDWB_NOM_REQUERANT, CMDWB_ETAT)
        VALUES (3700001, ''SONDE-LOT7-1'', ''SONDE-LOT7-1-1'', DATEADD(hour, 8, @aujourdhui), 2, ''ET-CP'', ''IG'', ''437000001'',
                ''SONDE COMMANDE WEB'', ''REQUERANT SONDE'', ''A'');',
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
            await EcranCommandesWeb.AgirAsync(c.Client, c.Greffe, vue.Session, "Choisir", V(Id));
            await EcranCommandesWeb.AgirAsync(c.Client, c.Greffe, vue.Session, "OuvrirBande", V("MettreEnAttente"));
            await EcranCommandesWeb.AgirAsync(c.Client, c.Greffe, vue.Session, "Texte", V(new { champ = "MotifSaisi", valeur = Motif }));
            var apres = await EcranCommandesWeb.AgirAsync(c.Client, c.Greffe, vue.Session, "Decider", V("MettreEnAttente"));
            if (apres.Message is not { Severite: "succes" } m)
                return new(false, false, $"Mettre en attente {Id} : refusé « {apres.Message?.Texte} »");

            // Relue dans une session NEUVE : les commandes viennent de la base, pas de la memoire de la premiere.
            var relue = await EcranCommandesWeb.OuvrirAsync(c.Client, c.Greffe);
            try
            {
                await EcranCommandesWeb.AgirAsync(c.Client, c.Greffe, relue.Session, "ChoisirOnglet", V("EnAttente"));
                var panneau = (await EcranCommandesWeb.AgirAsync(c.Client, c.Greffe, relue.Session, "Choisir", V(Id))).Panneau;
                return panneau is { MotifAttente: Motif } && relue.Tuiles.Any(t => t.Etat == "EnAttente" && t.Nombre >= 1)
                    ? new(true, true, $"Mettre en attente fait (« {m.Texte} »), relu par le pont : commande {Id} sous « En attente », motif « {panneau.MotifAttente} »")
                    : new(true, false, $"Mettre en attente rendu fait, mais relu : panneau {(panneau is null ? "absent" : $"motif « {panneau.MotifAttente} », état « {panneau.Etat?.Libelle} »")}");
            }
            finally { EcranCommandesWeb.Fermer(relue.Session); }
        }
        finally { EcranCommandesWeb.Fermer(vue.Session); }
    }
}
