// fix-ok: pas un correctif -- décision du lot 9 (28/11/2026, notée au plan) : un écran sans geste d'écriture prouve sa recherche avec
// une jumelle écartée par critère ; ajout de l'affaire jumelle 4100403 (dossier 2026B41402) et de sa vérification, en trois retouches.
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Historique de l'ancien RIG » (PROC_HISTORIQUE_ANCIEN_RIG, Pont/EcranHistoriqueV2.cs) : écran NEUF de la V3, EN LECTURE SEULE (REC-010,
/// REC-011 : « aucune route ne modifie une ancienne affaire », Serveur/Recopie/HistoriqueAffaires.cs) ; RIG v2 n'a ni cet écran ni de geste
/// d'écriture sur lui (sa recherche par ancien numéro, PROC_CORRESPMIG, y est reprise comme critère). La sonde prouve donc ses DONNÉES et sa
/// RECHERCHE, comme une forme sans geste d'écriture d'un écran généré (tests/ParcoursCompletCheck/Program.cs, LectureSeule) :
///   - DonneesAsync : la lecture attend un numéro ; par le SIREN de l'entreprise du jeu, le pont rend ses deux affaires lues dans la base
///     du greffe, l'ancienne marquée « ancien RIG » (rec.Correspondance) ;
///   - GesteAsync : la recherche par numéro de gestion du dossier rend les mêmes affaires, et la demande liée s'ouvre en lecture
///     (REC-011 CA03) avec son lien « née de » vers sa mère.
/// La recopie doit être EN SERVICE pour le greffe (rec.Remontee, sinon 409) : la sonde en pose la ligne le temps de sa mesure, puis
/// l'enlève, pour ne rien changer aux autres lignes du parcours. Lot 11 du plan « RIG v3 complet, mesuré par le banc du parcours
/// complet » (28/11/2026).
///
/// LE JEU (identifiants 41004xx, lot 11) : l'entreprise SIREN 410040101 et son dossier 2026B41401 ; l'affaire 4100401 (V3, deux demandes :
/// D2641100411 qualifiée et sa fille D2641100413) et l'affaire 4100402 de l'ancien RIG (sa demande validée D2541100412) ; la jumelle
/// 4100403 d'un autre dossier (2026B41402, autre SIREN), que les deux recherches écartent ; les colonnes de
/// v2.DEMANDE, v2.AFFAIRE et v2.ARRIVEE que l'historique lit, aux types de RIG_DEV (INFORMATION_SCHEMA de SQL-DEV\DEV, 28/11/2026).
/// </summary>
public sealed class SondeHistoriqueV2 : ISondeEcran
{
    private const string Siren = "410040101", Gestion = "2026B41401", Jumeau = "2026B41402", BaseV2 = "RIG_SONDE_LOT11";
    private const int Fille = 4100413, Mere = 4100411, Jumelle = 4100403;
    private bool _recopiePosee;

    public string Code => "PROC_HISTORIQUE_ANCIEN_RIG";

    public string? Jeu => JeuSonde.EnDeuxTemps(JeuSonde.Demandes + """
        IF COL_LENGTH('v2.DEMANDE', 'DMND_LIBELLE_ECRAN') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_LIBELLE_ECRAN varchar(35) NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_LIBELLE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_LIBELLE varchar(70) NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_CTGR') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_CTGR int NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_ARRV') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_ARRV int NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_INHIBEE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_INHIBEE bit NOT NULL DEFAULT (0);
        IF COL_LENGTH('v2.DEMANDE', 'DMND_TEXTE_INHIBITEUR') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_TEXTE_INHIBITEUR varchar(255) NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_DMND_INHIBITRICE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_DMND_INHIBITRICE int NULL;
        IF COL_LENGTH('v2.AFFAIRE', 'AFFRE_LIBELLE') IS NULL ALTER TABLE v2.AFFAIRE ADD AFFRE_LIBELLE varchar(70) NULL;
        IF COL_LENGTH('v2.AFFAIRE', 'AFFRE_ID_CLIEN') IS NULL ALTER TABLE v2.AFFAIRE ADD AFFRE_ID_CLIEN int NULL;
        IF OBJECT_ID('v2.ARRIVEE') IS NULL
            CREATE TABLE v2.ARRIVEE (ARRV_NUM_ARRIVEE varchar(11) NULL, ARRV_MODE_ARRIVEE varchar(5) NOT NULL, ARRV_DATEHEURE datetime NULL,
                ARRV_NOM varchar(255) NULL, ARRV_REFERENCES varchar(35) NULL, ARRV_PAIEMENT_JOINT bit NOT NULL DEFAULT (0),
                ARRV_CASE_COURRIER varchar(10) NULL, ARRV_ID_ARRV int IDENTITY(3700001, 1) NOT NULL PRIMARY KEY);
        """, $"""
        SET IDENTITY_INSERT rec.Entreprise ON;
        INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES (4100401, '{Siren}', '', N'SONDE HISTORIQUE SARL', 'C', 0),
            (4100402, '410040202', '', N'SONDE HISTORIQUE JUMELLE', 'C', 0);
        SET IDENTITY_INSERT rec.Entreprise OFF;
        SET IDENTITY_INSERT rec.DossierRcs ON;
        INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
            ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
            SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise)
        VALUES (4100401, 4100401, 'B1', '{Gestion}', '{Siren}', '20250105', 'C', 0, 0, '20250105', 'A', 'C', 0, 0, 0, 0, 0, 0, 0),
               (4100402, 4100402, 'B1', '{Jumeau}', '410040202', '20250105', 'C', 0, 0, '20250105', 'A', 'C', 0, 0, 0, 0, 0, 0, 0);
        SET IDENTITY_INSERT rec.DossierRcs OFF;
        INSERT INTO v2.AFFAIRE (AFFRE_ID_AFFRE, AFFRE_DATE_CREATION, AFFRE_LIBELLE) VALUES
            (4100401, DATEADD(DAY, -40, @aujourdhui), 'MODIFICATIONS SONDE LOT 11'), (4100402, DATEADD(DAY, -400, @aujourdhui), 'IMMATRICULATION SONDE LOT 11'),
            ({Jumelle}, DATEADD(DAY, -30, @aujourdhui), 'JUMELLE SONDE LOT 11');
        INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_ID_AFFRE, DMND_ID_DSRCS, DMND_LIBELLE, DMND_DATE_CREATION,
            DMND_DATE_VALIDATION, DMND_ID_DMND_MERE, DMND_CODE_PROSS) VALUES
            ({Mere}, 'D2641100411', 'Q', 4100401, 4100401, 'MODIFICATION DIRIGEANT', DATEADD(DAY, -40, @aujourdhui), NULL, NULL, 'MODIF'),
            (4100412, 'D2541100412', 'V', 4100402, 4100401, 'IMMATRICULATION', DATEADD(DAY, -400, @aujourdhui), DATEADD(DAY, -398, @aujourdhui), NULL, 'IMMAT'),
            ({Fille}, 'D2641100413', 'Q', 4100401, 4100401, 'DEPOT DES COMPTES', DATEADD(DAY, -38, @aujourdhui), NULL, {Mere}, 'DEPACT'),
            (4100414, 'D2641100414', 'Q', {Jumelle}, 4100402, 'MODIFICATION JUMELLE', DATEADD(DAY, -30, @aujourdhui), NULL, NULL, 'MODIF');
        INSERT INTO rec.Correspondance (BaseV2, TableV2, IdV2, TableV3, IdV3, IdTransfert) VALUES
            ('{BaseV2}', 'AFFAIRE', '914002', 'v2.AFFAIRE', '4100402', 0), ('{BaseV2}', 'DEMANDE', '914012', 'v2.DEMANDE', '4100412', 0);
        """);

    /// <summary>La recopie en service le temps de la mesure (rec.Remontee) ; posée seulement si le greffe n'en a pas.</summary>
    private async Task RecopieEnServiceAsync(ContexteSonde c)
    {
        if (await c.Valeur("SELECT COUNT(*) FROM rec.Remontee") != "0") return;
        await c.Sql($"INSERT INTO rec.Remontee (BaseV2, Version, IdTransfert) VALUES ('{BaseV2}', 0, 0);");
        _recopiePosee = true;
    }

    public Task<PreuveSonde>? DonneesAsync(ContexteSonde c) => DonneesBaseAsync(c);

    private async Task<PreuveSonde> DonneesBaseAsync(ContexteSonde c)
    {
        await RecopieEnServiceAsync(c);
        var v = await EcranHistoriqueV2.ChercherAsync(c.Client, c.Greffe, "siren", Siren);
        if (v is not { Mode: "serveur", Historique: { } h })
            return new(false, false, $"ChercherAsync(siren {Siren}) : {v.Mode}{(v.Message is null ? "" : " « " + v.Message + " »")}");
        var nouvelle = h.Affaires.FirstOrDefault(a => a.Id == 4100401);
        var ancienne = h.Affaires.FirstOrDefault(a => a.Id == 4100402);
        return nouvelle is { Demandes.Length: 2 } && ancienne is { IdV2: "914002", Demandes: [{ IdV2: "914012" }] } && h.Affaires.All(a => a.Id != Jumelle)
            ? new(true, true, $"base (pont EcranHistoriqueV2.ChercherAsync, SIREN {Siren} : « {h.Titre} », {h.Affaires.Length} affaires dont " +
                              $"{h.Anciennes} de l'ancien RIG, {h.Affaires.Sum(a => a.Demandes.Length)} demandes)")
            : new(false, false, $"ChercherAsync(siren {Siren}) lue de la base, mais sans les affaires du jeu : " +
                                $"{string.Join(", ", h.Affaires.Select(a => $"{a.Id} ({a.Logiciel}, {a.Demandes.Length} demande(s))"))}");
    }

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        try
        {
            await RecopieEnServiceAsync(c);
            var g = await EcranHistoriqueV2.ChercherAsync(c.Client, c.Greffe, "gestion", Gestion);
            if (g is not { Mode: "serveur", Historique: { } h })
                return new(false, false, $"lecture seule : recherche par n° de gestion {Gestion} : {g.Mode} « {g.Message} »");
            var ids = h.Affaires.Select(a => a.Id).Order().ToArray();
            var fille = await EcranHistoriqueV2.DemandeAsync(c.Client, c.Greffe, Fille);
            var lien = fille?.Liens.FirstOrDefault(l => l.Lien == "née de");
            return ids.SequenceEqual([4100401, 4100402]) && fille is { Numero: "D2641100413" } && lien is { IdDemande: Mere, Numero: "D2641100411" }
                ? new(true, true, $"lecture seule (écran neuf de la V3, aucun geste d'écriture dans RIG v2) : recherche « dossier {Gestion} » jouée, " +
                                  $"{ids.Length} affaire(s) relue(s) exactement dont l'ancienne {h.Affaires.First(a => a.Id == 4100402).Marque}, la jumelle " +
                                  $"{Jumelle} du dossier {Jumeau} écartée ; la demande {fille.Numero} ouverte en lecture, « née de » {lien.Numero}")
                : new(true, false, $"lecture seule : recherche « dossier {Gestion} » relue fausse (affaires {string.Join(",", ids)} ; demande " +
                                   $"{fille?.Numero ?? "introuvable"}, lien {lien?.Numero ?? "absent"})");
        }
        finally
        {
            if (_recopiePosee) await c.Sql($"DELETE FROM rec.Remontee WHERE BaseV2 = '{BaseV2}';");
            _recopiePosee = false;
        }
    }
}
