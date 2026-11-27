using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurCabinets;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Cabinets » (PROC_CABINET) : AJOUTER et MODIFIER un cabinet, dans la grille. La V2 n'a pas de suppression dans PROC_CABINET : la
/// V3 n'en invente pas. Mode d'emploi : EcrivainEcran.cs ; les lignes : LecteurCabinets.cs. Plan « RIG v3 100 % fonctionnel », lot 14,
/// 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_CABINET, messages au mot pres :
///   - OBLIGATOIRES : date et heure d'audience, type de cabinet (MetierObligatoire, OPE_CREATION_CABINET.Designer.cs:86, 95, 105) ; le
///     code cabinet l'est quand le type le demande (R_TYPEAUDCAB_PARAMETRAGE_CABINET, OPE_CREATION_CABINET.cs:51-58). Type, code, juge
///     et greffier se choisissent dans leurs listes (TYPE_AUD_CAB, CODE_CABINET, JUGE, GREFFIER_ASSERMENTE) : une valeur hors liste est
///     refusee ;
///   - DOUBLON (OnProcessusEventAfterChild, l. 74-86) : un AUTRE cabinet au meme code, a la meme date, a la meme heure et au meme juge
///     (AudienceCabinet.GetCabinet, AudienceCabinet.cs:1074-1093, AUDNC_AUDIENCE = 0) -> « Le cabinet saisi existe déjà. Merci de
///     vérifier votre saisie. » ;
///   - EN MODIFICATION d'un cabinet qui a des affaires appelees (APPEL_AFFAIRE.APPAF_ID_ADNC), la date, le type et le code sont en lecture
///     seule, et le juge aussi s'il est actif (DoInitZoneSaisie, l. 31-38) : les changer est refuse ;
///   - CREATION (PROC_CABINET.DoInitObject, l. 38-47 ; AudienceCabinet.CreateCabinet, l. 1277-1290) : AUDNC_AUDIENCE = 0, les autres
///     colonnes a leur valeur par defaut de la V2 (AUDIENCE_CABINET.cs de RigBaseGreffe : nombre d'appels 0, publique, etats « 1 »).
///   - l'heure est « HH:MM » (varchar(5)).
/// NON REPRIS (nommes) : les invitations de calendrier envoyees au juge et au greffier (AudienceCabinetInvitation, l. 90-91, 121-207) --
/// un envoi de courriel, service externe que la V3 n'appelle pas ; le classement admis (OPE_TYPE_CLASSEMENT_ADMIS, l. 71), une sous-saisie
/// que la grille ne porte pas : il se lit dans la fiche et reste tel quel ; la demande RIG v2 (« Création : » / « Modification (id) : »,
/// PROC_CABINET.cs:56-60) -- une trace, que le journal des gestes remplace (decision du plan).
/// TRACE : famille « reglage », « a cree » / « a modifie ».
/// </summary>
internal sealed partial class EcrivainCabinets : IEcrivainEcran
{
    public string Procedure => "PROC_CABINET";
    public string Ecran => "Cabinets";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Modifier];

    internal const string CabinetExistant = "Le cabinet saisi existe déjà. Merci de vérifier votre saisie.";

    private sealed record Cabinet(int Id, DateTime? Date, string Heure, string Type, string Code, int? Juge, int? Greffier);

    private static string? Cellule(GesteEnCours geste, string colonne) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v.Trim() : null;

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        Cabinet? avant = null;
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (geste.Demande.Type == TypeGeste.Modifier
            && (CriteresConsultation.Entier(id) is not { } n || (avant = await LireAsync(geste, n, ct)) is null))
            return IssueGeste.Refuser($"Cabinet introuvable dans la base du greffe ({id}).");
        var base_ = avant ?? new Cabinet(0, null, "", "", "", null, null);
        DateTime? date = base_.Date;
        if (Cellule(geste, ColDate) is { } d)
        {
            if (d.Length == 0) date = null;
            else if (CriteresConsultation.Date(d) is { } lue) date = lue;
            else return IssueGeste.Refuser($"« {ColDate} » : une date (jj/mm/aaaa) ({d}).");
        }
        var heure = Cellule(geste, ColHeure) ?? base_.Heure;
        var type = Cellule(geste, ColType) ?? base_.Type;
        var code = Cellule(geste, ColCode) ?? base_.Code;
        var juge = base_.Juge;
        if (Cellule(geste, ColJuge) is { } js)
        {
            if (js.Length == 0) juge = null;
            else if (await ActeurAsync(geste, "v2.JUGE", "JUG_ID_JUG", "JUG_ABREGE", "JUG_NOM", "JUG_PRENOM", js, ct) is { } j) juge = j;
            else return IssueGeste.Refuser($"« {ColJuge} » : aucun juge ne porte l'abrégé, le numéro ou le nom « {js} ».");
        }
        var greffier = base_.Greffier;
        if (Cellule(geste, ColGreffier) is { } gs)
        {
            if (gs.Length == 0) greffier = null;
            else if (await ActeurAsync(geste, "v2.GREFFIER_ASSERMENTE", "GRASS_ID_GRASS", "GRASS_ABREGE", "GRASS_NOM", "GRASS_PRENOM", gs, ct) is { } g) greffier = g;
            else return IssueGeste.Refuser($"« {ColGreffier} » : aucun greffier ne porte l'abrégé, le numéro ou le nom « {gs} ».");
        }
        var apres = base_ with { Date = date, Heure = heure, Type = type, Code = code, Juge = juge, Greffier = greffier };
        if (avant is not null && apres == avant) return IssueGeste.Refuser("Rien à enregistrer : aucune valeur ne change.");
        // Les obligatoires (Designer.cs:86, 95, 105).
        if (apres.Date is null) return IssueGeste.Refuser($"« {ColDate} » est obligatoire.");
        if (apres.Heure.Length == 0) return IssueGeste.Refuser($"« {ColHeure} » est obligatoire.");
        if (!Heure().IsMatch(apres.Heure)) return IssueGeste.Refuser($"« {ColHeure} » : une heure HH:MM ({apres.Heure}).");
        if (apres.Type.Length == 0) return IssueGeste.Refuser($"« {ColType} » est obligatoire.");
        bool? codeExige;
        await using (var r = await geste.LireAsync("SELECT R_TYPEAUDCAB_PARAMETRAGE_CABINET FROM v2.TYPE_AUD_CAB WHERE R_TYPEAUDCAB_VALEUR = @t", ct,
                         new SqlParameter("@t", apres.Type)))
            codeExige = await r.ReadAsync(ct) ? !r.IsDBNull(0) && r.GetBoolean(0) : null;
        if (codeExige is null) return IssueGeste.Refuser($"« {ColType} » : « {apres.Type} » n'est pas un type de cabinet de la liste.");
        if (codeExige == true && apres.Code.Length == 0)
            return IssueGeste.Refuser($"« {ColCode} » est obligatoire pour le type {apres.Type} (RIG v2 l'exige pour ce type).");
        if (apres.Code.Length > 0 && await CompterAsync(geste, "SELECT COUNT(*) FROM v2.CODE_CABINET WHERE R_CODECABINE_VALEUR = @c", ct,
                new SqlParameter("@c", apres.Code)) == 0)
            return IssueGeste.Refuser($"« {ColCode} » : « {apres.Code} » n'est pas un code cabinet de la liste.");
        // Un cabinet aux affaires appelees : date, type, code et juge actif en lecture seule (DoInitZoneSaisie, l. 31-38).
        if (avant is not null && await CompterAsync(geste, "SELECT COUNT(*) FROM v2.APPEL_AFFAIRE WHERE APPAF_ID_ADNC = @id", ct,
                new SqlParameter("@id", avant.Id)) > 0)
        {
            if (apres.Date != avant.Date || apres.Type != avant.Type || apres.Code != avant.Code)
                return IssueGeste.Refuser("Ce cabinet a des affaires appelées : sa date, son type et son code ne se modifient plus (RIG v2 les met en lecture seule).");
            if (apres.Juge != avant.Juge && avant.Juge is { } ancien
                && await CompterAsync(geste, "SELECT COUNT(*) FROM v2.JUGE WHERE JUG_ID_JUG = @j AND JUG_ACTIF = 1", ct, new SqlParameter("@j", ancien)) > 0)
                return IssueGeste.Refuser("Ce cabinet a des affaires appelées et un juge actif : le juge ne se modifie plus (RIG v2 le met en lecture seule).");
        }
        // Le doublon (l. 79-83 ; GetCabinet, AudienceCabinet.cs:1074-1093).
        if (await CompterAsync(geste, """
                SELECT COUNT(*) FROM v2.AUDIENCE_CABINET
                WHERE AUDNC_AUDIENCE = 0 AND ISNULL(AUDNC_CODE_CABINET, '') = @code AND AUDNC_DATE = @date AND AUDNC_HEURE = @heure
                  AND (AUDNC_ID_JUG = @juge OR (@juge IS NULL AND AUDNC_ID_JUG IS NULL)) AND AUDNC_ID_ADNC <> @id
                """, ct, new SqlParameter("@code", apres.Code), new SqlParameter("@date", apres.Date), new SqlParameter("@heure", apres.Heure),
                new SqlParameter("@juge", System.Data.SqlDbType.Int) { Value = (object?)apres.Juge ?? DBNull.Value }, new SqlParameter("@id", apres.Id)) > 0)
            return IssueGeste.Refuser(CabinetExistant);
        if (avant is null)
        {
            var nouveau = await geste.InsererAsync("v2.AUDIENCE_CABINET", "AUDNC_ID_ADNC", new Dictionary<string, object?>
            {
                ["AUDNC_AUDIENCE"] = false, ["AUDNC_TYPE_AUD_CAB"] = apres.Type, ["AUDNC_CODE_CABINET"] = apres.Code.Length == 0 ? null : apres.Code,
                ["AUDNC_DATE"] = apres.Date, ["AUDNC_HEURE"] = apres.Heure, ["AUDNC_ID_JUG"] = apres.Juge, ["AUDNC_ID_GRASS"] = apres.Greffier,
                ["AUDNC_NOMBRE_APPELS"] = (short)0, ["AUDNC_NUM_APPEL_ATTRIB"] = false, ["AUDNC_PUBLIQUE"] = true, ["AUDNC_ETAT_AUDIENCE"] = "1",
                ["AUDNC_ETAT_CABINET"] = "1", ["AUDNC_TOP_DIFFUSION"] = false, ["AUDNC_NON_DISPONIBLE_WEB"] = false,
            }, ct);
            var idNouveau = nouveau.ToString(CultureInfo.InvariantCulture);
            return IssueGeste.Ecrit($"Cabinet créé : {apres.Type} du {FormeLigne.Date(apres.Date)} à {apres.Heure}.", [idNouveau],
                new TraceGeste("reglage", $"le cabinet du {FormeLigne.Date(apres.Date)} à {apres.Heure}", idNouveau, "a cree", null, apres));
        }
        await geste.EcrireAsync("""
            UPDATE v2.AUDIENCE_CABINET SET AUDNC_DATE = @date, AUDNC_HEURE = @heure, AUDNC_TYPE_AUD_CAB = @type, AUDNC_CODE_CABINET = @code,
                AUDNC_ID_JUG = @juge, AUDNC_ID_GRASS = @greffier
            WHERE AUDNC_ID_ADNC = @id AND AUDNC_AUDIENCE = 0
            """, ct, new SqlParameter("@date", apres.Date), new SqlParameter("@heure", apres.Heure), new SqlParameter("@type", apres.Type),
            new SqlParameter("@code", apres.Code.Length == 0 ? DBNull.Value : apres.Code),
            new SqlParameter("@juge", System.Data.SqlDbType.Int) { Value = (object?)apres.Juge ?? DBNull.Value },
            new SqlParameter("@greffier", System.Data.SqlDbType.Int) { Value = (object?)apres.Greffier ?? DBNull.Value }, new SqlParameter("@id", apres.Id));
        return IssueGeste.Ecrit($"Cabinet du {FormeLigne.Date(apres.Date)} à {apres.Heure} enregistré.", [id],
            new TraceGeste("reglage", $"le cabinet du {FormeLigne.Date(apres.Date)} à {apres.Heure}", id, "a modifie", avant, apres));
    }

    private static async Task<int> CompterAsync(GesteEnCours geste, string sql, CancellationToken ct, params SqlParameter[] p)
    {
        await using var r = await geste.LireAsync(sql, ct, p);
        await r.ReadAsync(ct);
        return r.GetInt32(0);
    }

    /// <summary>Un juge ou un greffier saisi par son abrege, son numero ou son nom (« NOM Prenom ») ; null s'il n'existe pas.</summary>
    private static async Task<int?> ActeurAsync(GesteEnCours geste, string table, string cle, string abrege, string nom, string prenom, string saisi,
                                                CancellationToken ct)
    {
        await using var r = await geste.LireAsync($"""
            SELECT TOP (1) {cle} FROM {table}
            WHERE {abrege} = @s OR CAST({cle} AS varchar(12)) = @s OR CONCAT_WS(' ', {nom}, {prenom}) = @s
            ORDER BY CASE WHEN {abrege} = @s THEN 0 ELSE 1 END, {cle}
            """, ct, new SqlParameter("@s", saisi));
        return await r.ReadAsync(ct) ? r.GetInt32(0) : null;
    }

    private static async Task<Cabinet?> LireAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT AUDNC_DATE, AUDNC_HEURE, AUDNC_TYPE_AUD_CAB, AUDNC_CODE_CABINET, AUDNC_ID_JUG, AUDNC_ID_GRASS
            FROM v2.AUDIENCE_CABINET WITH (UPDLOCK) WHERE AUDNC_ID_ADNC = @id AND AUDNC_AUDIENCE = 0
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        return new Cabinet(id, FormeLigne.DateDe(r, "AUDNC_DATE"), FormeLigne.Texte(r, "AUDNC_HEURE"), FormeLigne.Texte(r, "AUDNC_TYPE_AUD_CAB"),
            FormeLigne.Texte(r, "AUDNC_CODE_CABINET"), r.IsDBNull(4) ? null : r.GetInt32(4), r.IsDBNull(5) ? null : r.GetInt32(5));
    }

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex Heure();
}
