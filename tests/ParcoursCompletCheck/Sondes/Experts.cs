using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Experts » (PROC_EXPERT, Pont/EcranExpertsBase.cs) : « Enregistrer la fiche » d'un expert, le « Valider » de OPE_EDITION_EXPERT de
/// RIG v2 (écriture : Serveur/Socle/EcransMain/EcritureExperts.cs), joué par le pont puis relu par le pont : la fiche porte le nom, le
/// téléphone et le courriel saisis, et le participant coché de sa mesure reprend le nom et le courriel (la cascade de RIG v2). Lot 9 du
/// plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LES JUMEAUX, refusés sans écriture (journal des gestes inchangé) : un téléphone hors du format de RIG v2 (ULT_TEXT_TELEPHONE) ; le
/// participant d'un AUTRE expert coché (il n'est pas dans les listes de cette fiche). Et ce participant reste tel quel après le geste.
///
/// LE JEU : EXPERT (absente de la base du banc) créée aux types de RIG_DEV (copie de développement, INFORMATION_SCHEMA du 28/11/2026) ; les
/// colonnes de PARTICIPANT et de MESURE que la lecture et l'écriture lisent, ajoutées si absentes ; deux experts, une mesure en cours, un
/// participant de chacun. Identifiants 39003xx (lot 9).
/// </summary>
public sealed class SondeExperts : ISondeEcran
{
    private const string Expert = "3900301", Participant = "3900321", AutreParticipant = "3900322";

    public string Code => "PROC_EXPERT";

    public string? Jeu => $"""
        IF OBJECT_ID('v2.EXPERT') IS NULL
            CREATE TABLE v2.EXPERT (EXPRT_ID_EXPRT int NOT NULL PRIMARY KEY, EXPRT_ID_ADRBS int NULL, EXPRT_ABREGE varchar(16) NOT NULL,
                EXPRT_CIVILITE varchar(7) NULL, EXPRT_NOM varchar(70) NULL, EXPRT_COMPLEMENT_NOM varchar(70) NULL, EXPRT_QUALIF_PROFESSION varchar(5) NULL,
                EXPRT_DATE_NAISSANCE datetime NULL, EXPRT_ETAT_INFO varchar(1) NOT NULL, EXPRT_DATE_MAJ datetime NOT NULL,
                EXPRT_ADRESSE_MAIL varchar(70) NULL, EXPRT_TELEPHONE varchar(15) NULL, EXPRT_FAX varchar(15) NULL, EXPRT_EST_ACTIF bit NOT NULL);
        {ColonnesLot9.Ajouter("EXPERT", ("EXPRT_ID_ADRBS", "int NULL"), ("EXPRT_CIVILITE", "varchar(7) NULL"), ("EXPRT_NOM", "varchar(70) NULL"),
            ("EXPRT_COMPLEMENT_NOM", "varchar(70) NULL"), ("EXPRT_QUALIF_PROFESSION", "varchar(5) NULL"), ("EXPRT_DATE_NAISSANCE", "datetime NULL"),
            ("EXPRT_ETAT_INFO", "varchar(1) NOT NULL CONSTRAINT DF_P9_EXP_ETAT DEFAULT ('0')"), ("EXPRT_DATE_MAJ", "datetime NOT NULL CONSTRAINT DF_P9_EXP_MAJ DEFAULT (GETDATE())"),
            ("EXPRT_ADRESSE_MAIL", "varchar(70) NULL"), ("EXPRT_TELEPHONE", "varchar(15) NULL"), ("EXPRT_FAX", "varchar(15) NULL"),
            ("EXPRT_EST_ACTIF", "bit NOT NULL CONSTRAINT DF_P9_EXP_ACTIF DEFAULT (1)"))}
        IF OBJECT_ID('v2.MESURE') IS NULL
            CREATE TABLE v2.MESURE (MSINS_ID_MSINS int NOT NULL PRIMARY KEY, MSINS_ID_INSTN int NOT NULL, MSINS_ID_DCJDC int NOT NULL, MSINS_NUM_MESURE varchar(11) NULL);
        {ColonnesLot9.Ajouter("MESURE", ("MSINS_NUM_MESURE", "varchar(11) NULL"), ("MSINS_DATE_CLOTURE", "datetime NULL"))}
        IF OBJECT_ID('v2.PARTICIPANT') IS NULL
            CREATE TABLE v2.PARTICIPANT (PRTCP_ID_PRTCP int NOT NULL PRIMARY KEY, PRTCP_ID_MSINS int NOT NULL, PRTCP_NOM varchar(70) NOT NULL);
        {ColonnesLot9.Ajouter("PARTICIPANT", ("PRTCP_ID_EXPRT", "int NULL"), ("PRTCP_ID_ADRBS", "int NULL"),
            ("PRTCP_CIVILITE", "varchar(7) NOT NULL CONSTRAINT DF_P9_PRT_CIV DEFAULT ('')"), ("PRTCP_COMPLEMENT_NOM", "varchar(70) NULL"),
            ("PRTCP_ACTIF", "bit NOT NULL CONSTRAINT DF_P9_PRT_ACTIF DEFAULT (1)"), ("PRTCP_QUALIF_PROFESSION", "varchar(5) NULL"),
            ("PRTCP_ADRESSE_MAIL", "varchar(70) NULL"))}
        EXEC sp_executesql N'
        INSERT INTO v2.EXPERT (EXPRT_ID_EXPRT, EXPRT_ABREGE, EXPRT_CIVILITE, EXPRT_NOM, EXPRT_COMPLEMENT_NOM, EXPRT_ETAT_INFO, EXPRT_DATE_MAJ, EXPRT_EST_ACTIF)
            VALUES (3900301, ''S39EXP'', ''MR'', ''EXPERT SONDE'', ''Cabinet sonde'', ''0'', @aujourdhui, 1),
                   (3900302, ''S39AUT'', ''MR'', ''AUTRE EXPERT'', NULL, ''0'', @aujourdhui, 1);
        INSERT INTO v2.MESURE (MSINS_ID_MSINS, MSINS_ID_INSTN, MSINS_ID_DCJDC, MSINS_NUM_MESURE, MSINS_DATE_CLOTURE) VALUES (3900311, 0, 0, ''M39EXP'', NULL);
        INSERT INTO v2.PARTICIPANT (PRTCP_ID_PRTCP, PRTCP_ID_MSINS, PRTCP_ID_EXPRT, PRTCP_CIVILITE, PRTCP_NOM, PRTCP_COMPLEMENT_NOM, PRTCP_ACTIF)
            VALUES (3900321, 3900311, 3900301, ''MR'', ''EXPERT SONDE'', ''Cabinet sonde'', 1),
                   (3900322, 3900311, 3900302, ''MR'', ''AUTRE EXPERT'', NULL, 1);
        ', N'@aujourdhui datetime', @aujourdhui;
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var fiche = new FicheExpert(Nom: "EXPERT SONDE MODIFIE", Tel: "01 23 45 67 89", Mail: "expert.sonde@exemple.fr", Participants: [Participant]);

        // Jumeaux refusés, rien d'écrit.
        var journal = await c.Journal();
        var telFaux = await EcranExperts.EnregistrerAsync(c.Client, c.Greffe, Expert, fiche with { Tel = "12" });
        var autre = await EcranExperts.EnregistrerAsync(c.Client, c.Greffe, Expert, fiche with { Participants = [AutreParticipant] });
        if (telFaux.Fait || telFaux.Message != "« Téléphone » : Numéro de téléphone invalide." || autre.Fait || !autre.Message.Contains(AutreParticipant)
            || await c.Journal() != journal)
            return new(false, false, $"Enregistrer la fiche, jumeaux : téléphone « 12 » {(telFaux.Fait ? "fait" : "refusé")} « {telFaux.Message} », "
                                     + $"participant d'un autre expert {(autre.Fait ? "fait" : "refusé")} « {autre.Message} »");

        var r = await EcranExperts.EnregistrerAsync(c.Client, c.Greffe, Expert, fiche);
        if (!r.Fait) return new(false, false, $"Enregistrer la fiche de l'expert : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");

        var vue = await EcranExperts.ExpertsAsync(c.Client, c.Greffe);
        var e = vue.Experts.FirstOrDefault(x => x.Cle == Expert);
        var m = vue.Mesures.FirstOrDefault(x => x.Participant == Participant);
        var a = vue.Mesures.FirstOrDefault(x => x.Participant == AutreParticipant);
        var mailParticipant = await c.Valeur($"SELECT PRTCP_ADRESSE_MAIL FROM v2.PARTICIPANT WHERE PRTCP_ID_PRTCP = {Participant}");
        return vue.Mode == "serveur" && e is { Nom: "EXPERT SONDE MODIFIE", Tel: "01 23 45 67 89", Mail: "expert.sonde@exemple.fr", Abrege: "S39EXP" }
               && m is { NomParticipant: "EXPERT SONDE MODIFIE", NumMesure: "M39EXP", EnCours: true } && mailParticipant == "expert.sonde@exemple.fr"
               && a is { NomParticipant: "AUTRE EXPERT" }
            ? new(true, true, $"Enregistrer la fiche fait (« {r.Message} »), relue par le pont : {e.Abrege} {e.Nom}, {e.Tel}, {e.Mail} ; le participant "
                              + $"coché de la mesure {m.NumMesure} reprend la fiche, celui d'un autre expert inchangé ; jumeaux refusés sans écriture")
            : new(true, false, $"Enregistrer la fiche rendu fait, mais relue : {vue.Mode}, expert « {e?.Nom} / {e?.Tel} / {e?.Mail} », participant "
                               + $"« {m?.NomParticipant} / {mailParticipant} », autre « {a?.NomParticipant} »");
    }
}
