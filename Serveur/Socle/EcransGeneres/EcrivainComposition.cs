using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// Le geste de la « Composition » (PROC_COMPO) sur l'audience cochee (LecteurComposition.cs) : « Valider » enregistre sa
/// composition -- les juges, le greffier, le parquetier. Mode d'emploi : EcrivainEcran.cs. Plan « RIG v3 100 % fonctionnel »,
/// lot 7, 26/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_COMPO\ (proc_compo.md, le Detail OPE fait foi).
///   - UNE audience a la fois (ParamMultiSelect = false, PROC_COMPO.cs:146), une AUDIENCE et non un cabinet (recherche de type
///     audience, PROC_COMPO.cs:134 ; BtnCompoAudience.Visible = AUDNC_AUDIENCE, proc_retaud.md:406) ;
///   - les juges : 1 au moins, 99 au plus (ETP_COMPO_AUDIENCE.cs:240-241) ; « Un juge est requis au minimum » (:123), « Un juge
///     a été sélectionné 2 fois pour une composition » (:108) ; un juge se choisit dans la liste ULT_COMBO_JUGE (abrege non vide,
///     utilisable en saisie = JUG_ACTIF : ULT_COMBO_JUGE.cs:128-132, Juge.cs:88) ;
///   - le greffier : un seul, obligatoire (:249-250) ; « Le greffier est obligatoire » (:137) ; liste ULT_COMBO_GREFFIER
///     (GRASS_ABREGE non vide, utilisable en saisie = GRASS_ACTIF, ULT_COMBO_GREFFIER.cs:18-25) ;
///   - le parquetier : zero ou un (:256-257) ; liste ULT_COMBO_PARQUETIER (PRQTR_ABREGE non vide, PRQTR_ACTIF,
///     ULT_COMBO_PARQUETIER.cs:18-25) ;
///   - l'ecriture (DoPreparerSauvegarde, ETP_COMPO_AUDIENCE.cs:299-343 ; Composition.Sauve, Composition.cs:348-383) : sans
///     composition, RIG v2 en propose une vide (PROC_COMPO.cs:216-217) ; a l'enregistrement de l'audience, ses compositions sont
///     renumerotees a partir de 1 (AudienceCabinet.cs:1338-1350), CMPST_PAR_DEFAUT = (numero d'ordre 1) (Composition.cs:359) ;
///     CMPST_ID_GRASS = le greffier, CMPST_ID_PRQTR = le parquetier ou rien ; les siegeants (SIEGER) sont repris dans l'ordre saisi,
///     renumerotes a partir de 1, titre « PAU » pour le premier et « JAU » pour les suivants (Composition.cs:365-379) ; un siegeant
///     de trop est supprime. Un siegeant existant garde sa ligne (ope_JUGE.sieger reutilise, ETP_COMPO_AUDIENCE.cs:314) : seul son
///     juge change.
///
/// Les juges, le greffier et le parquetier se designent par leur ABREGE, celui que montrent les listes de RIG v2 (SYNONYME des
/// ULT_COMBO_*) ; les juges dans l'ordre, separes par une virgule, le premier preside.
///
/// CE QUE LA V3 NE REPREND PAS (nomme, rien en silence) : une audience a PLUSIEURS compositions (une etape ETP_COMPO_AUDIENCE par
/// composition en RIG v2) -- la V3 refuse plutot que de choisir ; les invitations calendrier envoyees aux juges et au greffier
/// quand la composition change (AudienceCabinetInvitation, PROC_COMPO.cs:53-125 : un courriel, service externe, jamais appele) ;
/// la demande V2 « Composition du ... » (PROC_COMPO.cs:207-212), creee et validee d'un coup, sans facture : le journal des
/// gestes la remplace.
/// </summary>
internal sealed class EcrivainComposition : IEcrivainEcran
{
    public string Procedure => "PROC_COMPO";

    public string Ecran => "Composition";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Decider];

    /// <summary>Le geste du descripteur (Surveiller.cs), au mot pres : le bouton VALIDER de RIG v2.</summary>
    internal const string Valider = "Valider";

    /// <summary>Les parametres du descripteur que le geste lit.</summary>
    internal const string ParametreJuges = "Juges", ParametreGreffier = "Greffier", ParametreParquetier = "Parquetier";

    /// <summary>Un juge, un greffier ou un parquetier choisi : son id et son abrege.</summary>
    private sealed record Personne(int Id, string Abrege);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var d = geste.Demande;
        if (d.Nom != Valider) return IssueGeste.Refuser($"Le geste « {d.Nom} » n'existe pas à la composition de RIG v2.");
        if (d.Ids.Length == 0) return IssueGeste.Refuser("Cochez l'audience à composer.");
        // ParamMultiSelect = false (PROC_COMPO.cs:146).
        if (d.Ids.Length > 1) return IssueGeste.Refuser("Une seule audience se compose à la fois.");
        if (!int.TryParse(d.Ids[0], NumberStyles.None, CultureInfo.InvariantCulture, out var idAudience))
            return IssueGeste.Refuser($"L'audience « {d.Ids[0]} » n'est pas dans la base du greffe.");

        string libelle;
        bool audience;
        await using (var r = await geste.LireAsync($$"""
            SELECT a.AUDNC_AUDIENCE, {{AudiencesV2.Libelle}} AS Libelle
            FROM v2.AUDIENCE_CABINET AS a WITH (UPDLOCK, HOLDLOCK) {{AudiencesV2.Jointures}}
            WHERE a.AUDNC_ID_ADNC = @id
            """, ct, new SqlParameter("@id", idAudience)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"L'audience « {idAudience} » n'est pas dans la base du greffe.");
            audience = r["AUDNC_AUDIENCE"] is true;
            libelle = FormeLigne.Texte(r, "Libelle");
        }
        // Un cabinet n'a pas de composition (PROC_COMPO.cs:134 ; proc_retaud.md:406).
        if (!audience) return IssueGeste.Refuser("Un cabinet n'a pas de composition : seules les audiences se composent.");

        // LES JUGES (ETP_COMPO_AUDIENCE.cs:100-126, :240-241).
        var abreges = Liste(d.Valeurs.TryGetValue(ParametreJuges, out var j) ? j : null);
        if (abreges.Count == 0) return IssueGeste.Refuser("Un juge est requis au minimum");
        if (abreges.Count > 99) return IssueGeste.Refuser("Une composition compte 99 juges au plus.");
        var juges = new List<Personne>();
        foreach (var abrege in abreges)
        {
            if (await PersonneAsync(geste, "v2.JUGE", "JUG_ID_JUG", "JUG_ABREGE", "JUG_ACTIF", abrege, ct) is not { } juge)
                return IssueGeste.Refuser($"Le juge « {abrege} » n'est pas utilisable en saisie dans la base du greffe (abrégé inconnu, en double ou inactif).");
            if (juges.Any(x => x.Id == juge.Id)) return IssueGeste.Refuser("Un juge a été sélectionné 2 fois pour une composition");
            juges.Add(juge);
        }

        // LE GREFFIER (ETP_COMPO_AUDIENCE.cs:130-141, :249-250).
        var saisiGreffier = d.Valeurs.TryGetValue(ParametreGreffier, out var g) ? g.Trim() : "";
        if (saisiGreffier.Length == 0) return IssueGeste.Refuser("Le greffier est obligatoire");
        if (Liste(saisiGreffier).Count > 1) return IssueGeste.Refuser("Une composition n'a qu'un greffier.");
        if (await PersonneAsync(geste, "v2.GREFFIER_ASSERMENTE", "GRASS_ID_GRASS", "GRASS_ABREGE", "GRASS_ACTIF", saisiGreffier, ct) is not { } greffier)
            return IssueGeste.Refuser($"Le greffier « {saisiGreffier} » n'est pas utilisable en saisie dans la base du greffe (abrégé inconnu, en double ou inactif).");

        // LE PARQUETIER, facultatif (:256-257).
        var saisiParquetier = d.Valeurs.TryGetValue(ParametreParquetier, out var p) ? p.Trim() : "";
        Personne? parquetier = null;
        if (saisiParquetier.Length > 0)
        {
            if (Liste(saisiParquetier).Count > 1) return IssueGeste.Refuser("Une composition a zéro ou un parquetier.");
            parquetier = await PersonneAsync(geste, "v2.PARQUETIER", "PRQTR_ID_PRQTR", "PRQTR_ABREGE", "PRQTR_ACTIF", saisiParquetier, ct);
            if (parquetier is null) return IssueGeste.Refuser($"Le parquetier « {saisiParquetier} » n'est pas utilisable en saisie dans la base du greffe (abrégé inconnu, en double ou inactif).");
        }

        // LA COMPOSITION de l'audience : la creer (PROC_COMPO.cs:216-217, numero 1 : AudienceCabinet.cs:1338-1350) ou reprendre la seule.
        var compositions = new List<(int Id, short Ordre, int Greffier, int? Parquetier)>();
        await using (var r = await geste.LireAsync("""
            SELECT CMPST_ID_CMAUD, CMPST_NUM_ORDRE, CMPST_ID_GRASS, CMPST_ID_PRQTR FROM v2.COMPOSITION WITH (UPDLOCK, HOLDLOCK)
            WHERE CMPST_ID_ADNC = @id ORDER BY CMPST_NUM_ORDRE, CMPST_ID_CMAUD
            """, ct, new SqlParameter("@id", idAudience)))
            while (await r.ReadAsync(ct))
                compositions.Add((Convert.ToInt32(r["CMPST_ID_CMAUD"], CultureInfo.InvariantCulture), Convert.ToInt16(r["CMPST_NUM_ORDRE"], CultureInfo.InvariantCulture),
                                  Convert.ToInt32(r["CMPST_ID_GRASS"], CultureInfo.InvariantCulture),
                                  r["CMPST_ID_PRQTR"] is DBNull ? null : Convert.ToInt32(r["CMPST_ID_PRQTR"], CultureInfo.InvariantCulture)));
        if (compositions.Count > 1)
            return IssueGeste.Refuser($"Cette audience a {compositions.Count} compositions : la V3 ne sait pas encore les distinguer.");

        int idComposition;
        object? avant = null;
        var siegeants = new List<(int Id, int Juge)>();
        if (compositions.Count == 0)
        {
            idComposition = await geste.InsererAsync("v2.COMPOSITION", "CMPST_ID_CMAUD", new Dictionary<string, object?>
            {
                ["CMPST_ID_ADNC"] = idAudience, ["CMPST_NUM_ORDRE"] = (short)1, ["CMPST_ID_GRASS"] = greffier.Id,
                ["CMPST_ID_PRQTR"] = parquetier?.Id, ["CMPST_PAR_DEFAUT"] = true,
            }, ct);
        }
        else
        {
            var c = compositions[0];
            idComposition = c.Id;
            await using (var r = await geste.LireAsync("""
                SELECT SGR_ID_SGR, SGR_ID_JUG FROM v2.SIEGER WITH (UPDLOCK, HOLDLOCK) WHERE SGR_ID_CMAUD = @c ORDER BY SGR_NUM_ORDRE, SGR_ID_SGR
                """, ct, new SqlParameter("@c", c.Id)))
                while (await r.ReadAsync(ct))
                    siegeants.Add((Convert.ToInt32(r["SGR_ID_SGR"], CultureInfo.InvariantCulture), Convert.ToInt32(r["SGR_ID_JUG"], CultureInfo.InvariantCulture)));
            avant = new { Juges = siegeants.Select(s => s.Juge).ToArray(), Greffier = c.Greffier, Parquetier = c.Parquetier };
            // La seule composition est renumerotee 1 (AudienceCabinet.cs:1338-1350), donc par defaut (Composition.cs:359).
            await geste.EcrireAsync("""
                UPDATE v2.COMPOSITION SET CMPST_ID_GRASS = @g, CMPST_ID_PRQTR = @p, CMPST_NUM_ORDRE = 1, CMPST_PAR_DEFAUT = 1
                WHERE CMPST_ID_CMAUD = @c
                """, ct, new SqlParameter("@g", greffier.Id), new SqlParameter("@p", (object?)parquetier?.Id ?? DBNull.Value), new SqlParameter("@c", c.Id));
        }

        // LES SIEGEANTS (Composition.cs:365-379) : l'ordre saisi, 1..n, « PAU » puis « JAU » ; un siegeant existant garde sa ligne.
        for (var i = 0; i < juges.Count; i++)
        {
            var ordre = (short)(i + 1);
            var titre = ordre == 1 ? "PAU" : "JAU";
            if (i < siegeants.Count)
                await geste.EcrireAsync("UPDATE v2.SIEGER SET SGR_ID_JUG = @j, SGR_NUM_ORDRE = @o, SGR_TITRE_SIEGEANT = @t WHERE SGR_ID_SGR = @s", ct,
                    new SqlParameter("@j", juges[i].Id), new SqlParameter("@o", ordre), new SqlParameter("@t", titre), new SqlParameter("@s", siegeants[i].Id));
            else
                await geste.InsererAsync("v2.SIEGER", "SGR_ID_SGR", new Dictionary<string, object?>
                {
                    ["SGR_ID_CMAUD"] = idComposition, ["SGR_ID_JUG"] = juges[i].Id, ["SGR_NUM_ORDRE"] = ordre, ["SGR_TITRE_SIEGEANT"] = titre,
                }, ct);
        }
        foreach (var s in siegeants.Skip(juges.Count))
            await geste.EcrireAsync("DELETE FROM v2.SIEGER WHERE SGR_ID_SGR = @s", ct, new SqlParameter("@s", s.Id));

        var trace = new TraceGeste("affaire", $"la composition de l'{libelle}", idComposition.ToString(CultureInfo.InvariantCulture),
            compositions.Count == 0 ? "a cree" : "a modifie", avant,
            new { Juges = juges.Select(x => x.Abrege).ToArray(), Greffier = greffier.Abrege, Parquetier = parquetier?.Abrege });
        var n = juges.Count;
        return IssueGeste.Ecrit($"Composition enregistrée : {n} juge{(n > 1 ? "s" : "")}, greffier {greffier.Abrege}"
                                + (parquetier is null ? "." : $", parquetier {parquetier.Abrege}."),
                                [idAudience.ToString(CultureInfo.InvariantCulture)], trace);
    }

    /// <summary>Les abreges saisis, separes par une virgule ou un point-virgule, dans l'ordre.</summary>
    private static List<string> Liste(string? saisie)
        => (saisie ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>La personne de la liste de RIG v2 : abrege non vide, utilisable en saisie (colonne ACTIF) ; null si l'abrege est
    /// inconnu, inactif, ou porte par deux personnes (la V3 ne choisit pas a la place de l'agent).</summary>
    private static async Task<Personne?> PersonneAsync(GesteEnCours geste, string table, string colonneId, string colonneAbrege, string colonneActif,
                                                       string abrege, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(
            $"SELECT TOP (2) {colonneId} AS Id, {colonneAbrege} AS Abrege FROM {table} WHERE {colonneAbrege} = @a AND {colonneActif} = 1 ORDER BY {colonneId}",
            ct, new SqlParameter("@a", abrege));
        if (!await r.ReadAsync(ct)) return null;
        var trouvee = new Personne(Convert.ToInt32(r["Id"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "Abrege"));
        return await r.ReadAsync(ct) ? null : trouvee;
    }
}
