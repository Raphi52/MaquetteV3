using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Modifier une créance » (PROC_MODCREANCE) : les creances ADMISES des procedures collectives (v2.CREANCE_PC), une fiche par
/// creance. Mode d'emploi : LecteurEcran.cs ; le geste : EcrivainModificationCreance.cs. Plan « RIG v3 100 % fonctionnel », lot 12,
/// 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_INTEGCREANCE\MODCREA\ -- la PC choisie (ETP_RECHERCHE_PC),
/// verrouillee (PROC_MODCREANCE.cs:46-61), puis l'onglet Admissions : une ligne par creance PC de la PC (ProcedureCollective.
/// ListeCreancesPC, OPE_ADMISSIONS_DGV_MODCREANCE.cs:36-40, :295-337), ses colonnes « Créancier », « N° SIREN » et ses montants
/// admis « Privilégié échu »... « Rejeté » (:146-264), modifiables dans la grille (colsMontants_ChangementValeur, :428-441). La V3
/// fait une ligne par creance PC (la grille de la V2), groupee « Admissions », et ouvre sa fiche (la ligne de la grille).
/// LE VERROU : la PC tenue par une autre demande (ProcedureCollective.Lock, VerrouDemandeV2, sequenceur « 2P ») s'ouvre en lecture
/// seule, au message de RIG v2 (PROC_MODCREANCE.cs:56-60, EtpProcCollReadOnly) ; la ligne est « Verrouillée par un autre ».
/// ORDRE : les PC les plus recemment ouvertes d'abord (la recherche de PC, proc_modcreance.md « ORDER BY PRCLL_DATE_OUVERTURE
/// DESC »), puis numero de PC et numero d'ordre de la creance.
/// RECHERCHE (IRechercheEcran) : « Numéro de la procédure » (PRCLL_NUM_PC LIKE) et « Nom de l'affaire » (PRCLL_NOM_AFFAIRE LIKE), les
/// criteres de OPE_RECHERCHE_PC (proc_modcreance.md, Requetes SQL) ; le texte cherche aussi le creancier.
/// NON REPRIS (nomme) : les onglets Contestations et Défauts de réponse (des INSTANCES, que « Corriger une affaire » ouvre deja),
/// la date de depot et la suppression d'une creance (avec son instance, OPE_ADMISSIONS_DGV_MODCREANCE.cs:559-640).
/// </summary>
internal sealed class LecteurModificationCreance : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_MODCREANCE";

    internal const string Groupe = "Admissions";
    internal const string SectionPc = "Procédure collective", SectionCreance = "Créance", SectionMontants = "Montants admis";
    internal const string CritereNumero = "Numéro de la procédure", CritereNom = "Nom de l'affaire";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereNumero, CritereNom];

    // Le verrou de RIG v2 (VerrouDemandeV2.cs, Demande.GetOldLock : DOSSIER_DEMANDE au sequenceur de la PC, demande en cours ou en
    // attente, interrompue, en reclamation) -- ici pour l'etat de la ligne ; la fiche relit le motif par VerrouDemandeV2.MotifAsync.
    private const string Requete = """
        SELECT TOP (@plafond) c.CRNPC_ID_CRNPC, c.CRNPC_NUM_ORDRE, p.PRCLL_ID_PRCLL, p.PRCLL_NUM_PC, p.PRCLL_NOM_AFFAIRE, p.PRCLL_DATE_OUVERTURE,
               cr.CREPC_NOM, c.CRNPC_ADMIS_PRIVI_ECHU, c.CRNPC_ADMIS_PRIVI_NONECHU, c.CRNPC_ADMIS_PRIVI_PROVISIONNEL, c.CRNPC_ADMIS_CHIRO_ECHU,
               c.CRNPC_ADMIS_CHIRO_NONECHU, c.CRNPC_ADMIS_CHIRO_PROVISIONNEL, c.CRNPC_ADMIS_SUPER_PRIVI_ECHU, c.CRNPC_REJETE,
               CASE WHEN EXISTS (SELECT 1 FROM v2.DOSSIER_DEMANDE AS dd INNER JOIN v2.DEMANDE AS d ON d.DMND_ID_DMND = dd.DSSDM_ID_DMND
                                 WHERE dd.DSSDM_CODE_SEQUENCEUR = '2P' AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(20), p.PRCLL_ID_PRCLL)
                                   AND (d.DMND_EN_COURS = 1 OR d.DMND_ETAT_DEMANDE IN ('W', 'I', 'N'))) THEN 1 ELSE 0 END AS Verrouillee,
               COUNT(*) OVER () AS Total
        FROM v2.CREANCE_PC AS c
        INNER JOIN v2.PROCEDURE_COLLECTIVE AS p ON p.PRCLL_ID_PRCLL = c.CRNPC_ID_PRCLL
        LEFT JOIN v2.CREANCIER_PC AS cr ON cr.CREPC_ID_CREPC = c.CRNPC_ID_CREPC
        WHERE 1 = 1 /*filtres*/
        ORDER BY p.PRCLL_DATE_OUVERTURE DESC, p.PRCLL_NUM_PC, c.CRNPC_NUM_ORDRE, c.CRNPC_ID_CRNPC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", "", StringComparison.Ordinal), Ligne, ct, new SqlParameter("@plafond", plafond));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (recherche.Critere(CritereNumero) is { } numero) f.Ajouter("p.PRCLL_NUM_PC LIKE @numero", ("@numero", FormeLigne.Contient(numero)));
        if (recherche.Critere(CritereNom) is { } nom) f.Ajouter("p.PRCLL_NOM_AFFAIRE LIKE @nom", ("@nom", FormeLigne.Contient(nom)));
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("p.PRCLL_NUM_PC LIKE @texte OR p.PRCLL_NOM_AFFAIRE LIKE @texte OR cr.CREPC_NOM LIKE @texte", ("@texte", FormeLigne.Contient(recherche.Texte)));
        return LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));
    }

    private static decimal? Admis(SqlDataReader r)
    {
        var lus = CreancesPcV2.Montants.Where(m => m.Suffixe != "REJETE").Select(m => CriteresConsultation.Decimal(r, "CRNPC_" + m.Suffixe)).Where(m => m is not null).ToList();
        return lus.Count == 0 ? null : lus.Sum();
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var verrouillee = Convert.ToInt32(r["Verrouillee"], CultureInfo.InvariantCulture) == 1;
        var rejete = CriteresConsultation.Decimal(r, "CRNPC_REJETE");
        var nom = $"PC {FormeLigne.Texte(r, "PRCLL_NUM_PC")}, créance n° {FormeLigne.Texte(r, "CRNPC_NUM_ORDRE")}";
        var precisions = FormeLigne.Joindre(FormeLigne.Texte(r, "CREPC_NOM") is { Length: > 0 } c ? c : "créancier inconnu",
            FormeLigne.Texte(r, "PRCLL_NOM_AFFAIRE"), Admis(r) is { } a ? "admis " + CriteresConsultation.Euros(a) : "aucun montant admis",
            rejete is { } j ? "rejeté " + CriteresConsultation.Euros(j) : null, verrouillee ? "verrouillée par une autre demande" : null);
        return new LigneLue(nom, precisions, Groupe, verrouillee ? "EnAttente" : "AFaire", []) { Id = FormeLigne.Texte(r, "CRNPC_ID_CRNPC") };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        string nom, idPc;
        SectionFiche[] sections;
        await using (var r = await LectureV2.LireAsync(baseGreffe, """
            SELECT c.CRNPC_NUM_ORDRE, c.CRNPC_ETAT_CREANCE, c.CRNPC_NATURE_CREANCE_ADM, p.PRCLL_ID_PRCLL, p.PRCLL_NUM_PC, p.PRCLL_NOM_AFFAIRE,
                   p.PRCLL_DATE_OUVERTURE, cr.CREPC_NOM, cr.CREPC_SIREN, c.CRNPC_ADMIS_PRIVI_ECHU, c.CRNPC_ADMIS_PRIVI_NONECHU,
                   c.CRNPC_ADMIS_PRIVI_PROVISIONNEL, c.CRNPC_ADMIS_CHIRO_ECHU, c.CRNPC_ADMIS_CHIRO_NONECHU, c.CRNPC_ADMIS_CHIRO_PROVISIONNEL,
                   c.CRNPC_ADMIS_SUPER_PRIVI_ECHU, c.CRNPC_REJETE
            FROM v2.CREANCE_PC AS c
            INNER JOIN v2.PROCEDURE_COLLECTIVE AS p ON p.PRCLL_ID_PRCLL = c.CRNPC_ID_PRCLL
            LEFT JOIN v2.CREANCIER_PC AS cr ON cr.CREPC_ID_CREPC = c.CRNPC_ID_CREPC
            WHERE c.CRNPC_ID_CRNPC = @id
            """, ct, new SqlParameter("@id", n)))
        {
            if (!await r.ReadAsync(ct)) return null;
            idPc = FormeLigne.Texte(r, "PRCLL_ID_PRCLL");
            nom = $"PC {FormeLigne.Texte(r, "PRCLL_NUM_PC")}, créance n° {FormeLigne.Texte(r, "CRNPC_NUM_ORDRE")}";
            sections =
            [
                new(SectionPc, [
                    new ChampFiche("Numéro", FormeLigne.Texte(r, "PRCLL_NUM_PC")),
                    new ChampFiche("Nom de l'affaire", FormeLigne.Texte(r, "PRCLL_NOM_AFFAIRE")),
                    new ChampFiche("Date d'ouverture", FormeLigne.DateFiche(FormeLigne.DateDe(r, "PRCLL_DATE_OUVERTURE"))),
                ]),
                new(SectionCreance, [
                    new ChampFiche("Créancier", FormeLigne.Texte(r, "CREPC_NOM")),
                    new ChampFiche("N° SIREN", FormeLigne.Siren(FormeLigne.Texte(r, "CREPC_SIREN"))),
                    new ChampFiche("N° d'ordre", FormeLigne.Texte(r, "CRNPC_NUM_ORDRE")),
                    new ChampFiche("Etat", FormeLigne.Texte(r, "CRNPC_ETAT_CREANCE")),
                    new ChampFiche("Nature", FormeLigne.Texte(r, "CRNPC_NATURE_CREANCE_ADM")),
                ]),
                new(SectionMontants, [.. CreancesPcV2.Montants.Select(m =>
                    new ChampFiche(m.Libelle, CriteresConsultation.MontantFiche(CriteresConsultation.Decimal(r, "CRNPC_" + m.Suffixe))) { Modifiable = true })]),
            ];
        }
        var motif = await VerrouDemandeV2.MotifAsync(baseGreffe, null, "Procédure collective", DepotsPcV2.SequenceurPc, idPc, ct);
        if (motif is not null)
            sections = [.. sections.Select(s => s with { Champs = [.. s.Champs.Select(c => c with { Modifiable = false })] })];
        return new FicheEcran(Procedure, id, nom, sections) { LectureSeule = motif is not null, Motif = motif };
    }
}
