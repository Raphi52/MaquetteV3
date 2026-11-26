using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// DEPOSER SUR UNE PROCEDURE COLLECTIVE COMME RIG v2 : ce que « Dépôts d'événement PC » (PROC_DEP, processus DEPPC) et
/// « Dépôts PC en masse » (PROC_DEPPCM) partagent (plan « RIG v3 100 % fonctionnel », lot 5, 26/11/2026) -- la PC par son
/// numero, le code d'evenement avec depot, et l'ecriture d'un evenement SURVENU et de son depot numerote. Lecteurs :
/// LecteurDepotsEvenementPc.cs, LecteurDepotsPcEnMasse.cs ; ecrivains : EcrivainDepotsEvenementPc.cs,
/// EcrivainDepotsPcEnMasse.cs.
///
/// SOURCE (ancien RIG) :
///   - la PC se designe par son numero (PRCLL_NUM_PC ; OPE_ENTITE_JUD_RECHERCHE_DEPPCM.cs:121-123) ; une PC tenue par une
///     autre demande n'entre pas en saisie (procedureCollective.Lock, PROC_DEPPC.cs:45-53 ; sequenceur 2P,
///     ProcedureCollective.cs:812) ;
///   - les codes proposes : ceux qui portent un depot PC (R_CODEEVEPRO_AVEC_DEPOT_PC = 1, ULT_COMBO_CODE_EVE_PRO.cs:79 ; le
///     depot est obligatoire : MetierDepotPcObligatoire, PROC_DEPPC.cs:33-36, ETP_DEPPCM.cs:49) ;
///   - l'evenement : CreateEvenementProcedure(pc, code, survenu) (EvenementProcedure.cs) -- survenu, delai non expire, numero
///     source GUID, type d'evenement du code, piece numerisee si le code le demande pour une PC
///     (R_CODEEVEPRO_AVEC_PIECE_NUMERISE_PCPRV, nature et deposant GED du code) ; ajoute a la PC puis TOUS les evenements de la
///     PC renumerotes : survenu, puis date, puis date maxi / mini / fixe (ProcedureCollective.cs:567-575,
///     EvenementProcedure.DateMaxiMiniouFixe) ;
///   - le depot : categorie du code (R_CODEEVEPRO_CATEGORIE_DEPOT, OPE_DEPOT.cs:180-181), date = date de travail
///     (OPE_DEPOT.cs:192), numero dans la serie de la categorie (CATEGORIE_DEPOT.R_CATEGORIED_TYPE_NUM_DEPOT, annee du depot,
///     OPE_DEPOT.cs:219-223), numero source GUID (DepotPC.cs:24), etat info 1 (DepotPC.cs:257) ;
///   - la demande, sur la PC : nom = nom de l'affaire, indicatif et numeros = numero de PC, sequenceur 2P
///     (ProcedureCollective.CreateDemande, ProcedureCollective.cs:811-814), validee.
/// </summary>
internal static class DepotsPcV2
{
    /// <summary>Le sequenceur d'une procedure collective dans DOSSIER_DEMANDE (ProcedureCollective.cs:812).</summary>
    internal const string SequenceurPc = "2P";

    /// <summary>La PC qui recoit le depot.</summary>
    internal sealed record Pc(int Id, string Numero, string Nom, bool Forfait);

    /// <summary>La PC de numero <paramref name="numero"/> ; null si la base ne la porte pas.</summary>
    internal static async Task<Pc?> TrouverAsync(GesteEnCours geste, string numero, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT TOP (1) p.PRCLL_ID_PRCLL, p.PRCLL_NUM_PC, p.PRCLL_NOM_AFFAIRE, CONVERT(bit, ISNULL(p.PRCLL_FORFAIT_FACTURE, 0)) AS Forfait
            FROM v2.PROCEDURE_COLLECTIVE AS p WHERE p.PRCLL_NUM_PC = @n ORDER BY p.PRCLL_ID_PRCLL
            """, ct, new SqlParameter("@n", numero));
        return await r.ReadAsync(ct)
            ? new Pc(Convert.ToInt32(r["PRCLL_ID_PRCLL"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "PRCLL_NUM_PC"),
                     FormeLigne.Texte(r, "PRCLL_NOM_AFFAIRE"), r["Forfait"] is true)
            : null;
    }

    /// <summary>Le code d'evenement avec depot PC : son type, sa categorie et la serie de son numero de depot, sa piece numerisee.</summary>
    internal sealed record CodeAvecDepot(string Code, string TypeEvePro, string Categorie, string? SerieDepot, bool PieceNumerisee,
                                         string? NatureDocGed, string? TypeDeposantGed);

    /// <summary>Le code <paramref name="code"/> s'il est propose (AVEC_DEPOT_PC = 1) ; null sinon.</summary>
    internal static async Task<CodeAvecDepot?> CodeAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT c.R_CODEEVEPRO_VALEUR, c.R_CODEEVEPRO_TYPE_EVE_PRO, c.R_CODEEVEPRO_CATEGORIE_DEPOT, cd.R_CATEGORIED_TYPE_NUM_DEPOT,
                   CONVERT(bit, ISNULL(c.R_CODEEVEPRO_AVEC_PIECE_NUMERISE_PCPRV, 0)) AS PieceNumerisee,
                   c.R_CODEEVEPRO_NATURE_DOC_GED, c.R_CODEEVEPRO_TYPE_DEPOSANT_GED
            FROM v2.CODE_EVE_PRO AS c LEFT JOIN v2.CATEGORIE_DEPOT AS cd ON cd.R_CATEGORIED_VALEUR = c.R_CODEEVEPRO_CATEGORIE_DEPOT
            WHERE c.R_CODEEVEPRO_VALEUR = @c AND c.R_CODEEVEPRO_AVEC_DEPOT_PC = 1
            """, ct, new SqlParameter("@c", code));
        if (!await r.ReadAsync(ct)) return null;
        string? Opt(string colonne) => r[colonne] is string s && s.Trim().Length > 0 ? s.Trim() : null;
        return new CodeAvecDepot(FormeLigne.Texte(r, "R_CODEEVEPRO_VALEUR"), FormeLigne.Texte(r, "R_CODEEVEPRO_TYPE_EVE_PRO"),
            FormeLigne.Texte(r, "R_CODEEVEPRO_CATEGORIE_DEPOT"), Opt("R_CATEGORIED_TYPE_NUM_DEPOT"), r["PieceNumerisee"] is true,
            Opt("R_CODEEVEPRO_NATURE_DOC_GED"), Opt("R_CODEEVEPRO_TYPE_DEPOSANT_GED"));
    }

    /// <summary>Le refus d'un code qui n'est pas dans la liste de l'etape (codes avec depot PC).</summary>
    internal static string CodeNonPropose(string code)
        => $"Le code « {code} » n'est pas un événement avec dépôt PC dans la base du greffe (seuls ces codes sont proposés, le dépôt étant obligatoire).";

    /// <summary>Le depot ecrit : ses numeros et sa demande.</summary>
    internal sealed record DepotEcrit(int IdEvenement, int IdDepot, string NumeroDepot, int IdDemande, string NumeroDemande);

    /// <summary>
    /// ECRIT, sur la PC <paramref name="pc"/>, la demande validee de code <paramref name="codeProcessus"/> (fille de
    /// <paramref name="mere"/> si donnee), l'evenement survenu <paramref name="code"/> du <paramref name="date"/>, la renumerotation
    /// des evenements de la PC, et le depot numerote. Un numero refuse : rien d'ecrit (la transaction du geste annule tout).
    /// </summary>
    internal static async Task<(DepotEcrit? Depot, string? Refus)> EcrireAsync(GesteEnCours geste, string codeProcessus, Pc pc,
        CodeAvecDepot code, DateTime date, int? mere, CancellationToken ct)
    {
        var dateDepot = geste.Maintenant.Date;
        if (code.SerieDepot is null)
            return (null, $"La catégorie de dépôt « {code.Categorie} » du code « {code.Code} » n'a pas de série de numéros dans la base du greffe.");
        var (numero, refus) = await NumeroAsync(geste, code.SerieDepot, dateDepot.Year, ct);
        if (numero is null) return (null, refus);

        var (demande, refusDemande) = await CreerDemandeAsync(geste, codeProcessus, "V", ct);
        if (demande is null) return (null, refusDemande);
        await RattacherAsync(geste, demande, pc.Nom, pc.Numero, pc.Numero, SequenceurPc, pc.Id.ToString(CultureInfo.InvariantCulture), ct);
        if (mere is { } idMere)
            await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_ID_DMND_MERE = @mere WHERE DMND_ID_DMND = @id", ct,
                new SqlParameter("@mere", idMere), new SqlParameter("@id", demande.Id));

        var evenement = await InsererAsync(geste, "v2.EVENEMENT_PROCEDURE", "EVNPR_ID_EVNPR", new Dictionary<string, object?>
        {
            ["EVNPR_ID_PRCLL"] = pc.Id, ["EVNPR_CODE_EVE_PRO"] = code.Code, ["EVNPR_DATE"] = date.Date, ["EVNPR_SURVENU"] = true,
            ["EVNPR_DELAI_EXPIRE"] = false, ["EVNPR_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(),
            ["EVNPR_TYPE_EVE_PRO"] = code.TypeEvePro.Length > 0 ? code.TypeEvePro[..1] : null, ["EVNPR_AVEC_PIECE_NUMERISE"] = code.PieceNumerisee,
            ["EVNPR_NATURE_DOC_GED"] = code.PieceNumerisee ? code.NatureDocGed : null,
            ["EVNPR_TYPE_DEPOSANT_GED"] = code.PieceNumerisee ? code.TypeDeposantGed : null,
        }, ct);
        // fix-ok: mesure le 26/11/2026 (banc EcransGeneresBase, 500 « ecrit : e ») -- le garde d'ecriture du socle lit la table
        // qui suit UPDATE : la cible s'ecrit par son nom v2.*, jamais par un alias.
        await geste.EcrireAsync("""
            UPDATE v2.EVENEMENT_PROCEDURE SET EVNPR_NUM_ORDRE = n.Ordre
            FROM (SELECT EVNPR_ID_EVNPR, CONVERT(smallint, ROW_NUMBER() OVER (ORDER BY ISNULL(EVNPR_SURVENU, 0), EVNPR_DATE,
                         COALESCE(EVNPR_DATE_MAXI, EVNPR_DATE_MINI, EVNPR_DATE_FIXE), EVNPR_ID_EVNPR)) AS Ordre
                  FROM v2.EVENEMENT_PROCEDURE WHERE EVNPR_ID_PRCLL = @pc AND EVNPR_CODE_EVE_PRO IS NOT NULL) AS n
            WHERE n.EVNPR_ID_EVNPR = v2.EVENEMENT_PROCEDURE.EVNPR_ID_EVNPR
            """, ct, new SqlParameter("@pc", pc.Id));
        var depot = await InsererAsync(geste, "v2.DEPOT_PC", "DPTPC_ID_DPTPC", new Dictionary<string, object?>
        {
            ["DPTPC_ID_EVNPR"] = evenement, ["DPTPC_NUM_DEPOT"] = numero, ["DPTPC_DATE"] = dateDepot, ["DPTPC_CATEGORIE_DEPOT"] = code.Categorie,
            ["DPTPC_ETAT_INFO"] = "1", ["DPTPC_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(), ["DPTPC_TOP_EXPERIAN"] = false,
        }, ct);
        return (new DepotEcrit(evenement, depot, numero, demande.Id, demande.Numero), null);
    }

    /// <summary>La trace du depot ecrit (famille « affaire » du journal des gestes : celle des affaires judiciaires).</summary>
    internal static TraceGeste Trace(string objet, DepotEcrit d, Pc pc, CodeAvecDepot code, DateTime date) =>
        new("affaire", $"{objet} {d.NumeroDepot}", d.IdDepot.ToString(CultureInfo.InvariantCulture), "a cree", null,
            new { Depot = d.NumeroDepot, Pc = pc.Numero, pc.Nom, CodeEvenement = code.Code, Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Demande = d.NumeroDemande });

    // ------------------------------------------------------------ LA LISTE DES SAISIES

    /// <summary>
    /// Les saisies de depot des codes processus <paramref name="codes"/> : les demandes, la plus recente d'abord, avec leur PC
    /// (DOSSIER_DEMANDE 2P) ou leur prevention (2R). Ecartees : supprimees (X) et terminees (T, le chemin d'echec d'une facture
    /// sans depot, OPE_ENTITE_JUD_RECHERCHE_DEPPCM.cs:421-427). <paramref name="filles"/> : les seules demandes portees par une PC
    /// ou une prevention (les dossiers du lot de DEPPCM, pas la demande du lot elle-meme).
    /// </summary>
    internal static string Requete(string codes, bool filles) => $"""
        SELECT TOP (@plafond) d.DMND_ID_DMND, d.DMND_NUM_DEMANDE, d.DMND_CODE_PROSS, d.DMND_ETAT_DEMANDE,
               CONVERT(bit, ISNULL(d.DMND_EN_COURS, 0)) AS EnCours, d.DMND_NOM, d.DMND_NUM_INDICATIF, o.DSSDM_CODE_SEQUENCEUR AS Sequenceur,
               COUNT(*) OVER () AS Total
        FROM v2.DEMANDE AS d
        OUTER APPLY (SELECT TOP (1) dd.DSSDM_CODE_SEQUENCEUR FROM v2.DOSSIER_DEMANDE AS dd
                     WHERE dd.DSSDM_ID_DMND = d.DMND_ID_DMND AND dd.DSSDM_CODE_SEQUENCEUR IN ('2P', '2R') ORDER BY dd.DSSDM_ID_DSSDM) AS o
        WHERE d.DMND_CODE_PROSS IN ({codes}) AND d.DMND_ETAT_DEMANDE NOT IN ('X', 'T'){(filles ? " AND o.DSSDM_CODE_SEQUENCEUR IS NOT NULL" : "")}
        ORDER BY d.DMND_ID_DMND DESC
        """;
}
