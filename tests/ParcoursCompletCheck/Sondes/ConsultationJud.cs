using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Consultation judiciaire » (PROC_CJUD, Pont/EcranConsultationJud*.cs) : refuser un enrolement RPVA (« Saisie de la demande » de
/// RIG v2, OPE_CJUDINFOG), puis le relire par le pont : traite « Refusé », son type et son motif de rejet. Lot 8 du plan « parcours
/// complet » (28/11/2026). Le jeu cree v2.DEMAT_RPJC_ENR aux types de RIG_DEV (absente des jeux des ecrans generes : le 500
/// « Invalid object name » du 28/11) et un enrolement integre (1), non traite, porte par sa demande en cours ; identifiants 38000xx.
/// </summary>
public sealed class SondeConsultationJud : ISondeEcran
{
    private const string Enrolement = "3800001", Motif = "Assignation absente du dépôt (sonde du parcours)";

    public string Code => "PROC_CJUD";

    public string? Jeu => """
        IF OBJECT_ID(N'v2.DEMAT_RPJC_ENR') IS NULL
            CREATE TABLE v2.DEMAT_RPJC_ENR (RPJCE_ID_RPJCE int NOT NULL PRIMARY KEY, RPJCE_DATE_CREATION datetime NULL, RPJCE_ETAT varchar(2) NULL,
                RPJCE_ETAT_TRAITEMENT_RIG varchar(2) NULL, RPJCE_DATE_TRAITEMENT_RIG datetime NULL, RPJCE_TYPE_REJET_RIG varchar(100) NULL,
                RPJCE_MOTIF_REJET_RIG varchar(max) NULL, RPJCE_REFERENCE_DEMANDE varchar(10) NULL, RPJCE_ID_DMND int NULL, RPJCE_FLUX varbinary(max) NULL,
                RPJCE_ID_INSTN int NULL);
        EXEC sp_executesql N'
        INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_EN_COURS, DMND_CODE_PROSS, DMND_DATE_CREATION)
            VALUES (3800101, ''D3800101'', ''I'', 1, ''CJUDINFOG'', @aujourdhui);
        INSERT INTO v2.DEMAT_RPJC_ENR (RPJCE_ID_RPJCE, RPJCE_DATE_CREATION, RPJCE_ETAT, RPJCE_REFERENCE_DEMANDE, RPJCE_ID_DMND)
            VALUES (3800001, @aujourdhui, ''1'', ''RPVA38001'', 3800101);
        ', N'@aujourdhui datetime', @aujourdhui;
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var r = await EcranConsultationJud.TraiterAsync(c.Client, c.Greffe, Enrolement, "R", "MQPIECE", Motif);
        if (!r.Fait) return new(false, false, $"Refuser l'enrôlement {Enrolement} : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");
        var vue = await EcranConsultationJud.VueAsync(c.Client, c.Greffe);
        var ligne = vue.Lignes.FirstOrDefault(l => l.Id == Enrolement);
        var demande = await c.Valeur("SELECT DMND_ETAT_DEMANDE FROM v2.DEMANDE WHERE DMND_ID_DMND = 3800101");
        return ligne is { EtatTraitement: "Refusé", TypeRejet: "MQPIECE", MotifRejet: Motif, EtatDemat: "Traité par le greffe" } && demande == "V"
            ? new(true, true, $"Refuser l'enrôlement fait (« {r.Message} »), relu par le pont : {ligne.EtatTraitement}, {ligne.EtatDemat}, demande validée")
            : new(true, false, $"Refuser l'enrôlement rendu fait, mais relu {(ligne is null ? "absent" : $"« {ligne.EtatTraitement} / {ligne.TypeRejet} / {ligne.EtatDemat} »")}, demande {demande}");
    }
}
