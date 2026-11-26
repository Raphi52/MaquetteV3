using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Valider » de la « Préparation d'audience » (PROC_PREAUD) sur les audiences ou cabinets coches (LecteurPreparationAudience.cs) :
/// les NUMEROS D'APPEL de leurs affaires, calcules et enregistres comme RIG v2. Mode d'emploi : EcrivainEcran.cs. Plan « RIG v3 100 %
/// fonctionnel », lot 7, 26/11/2026.
///
/// SOURCE (ancien RIG, Source\RIG\DLL\Processus\PROC_PREAUD\ et AudienceCabinet.cs ; les noms de fichiers de proc_preaud.md
/// -- OPE_REC.cs, OPE_EDIT.cs -- sont OPE_RECHERCHE_PLUMITIF.cs et OPE_EDIT_PLUMITIF.cs dans le code) :
///   - QUAND : numeros non attribues (AUDNC_NUM_APPEL_ATTRIB = 0) : « Le calcul sera exécuté automatiquement à la validation »,
///     la case « Recalculer les n° d'appel » est forcee et cachee (OPE_RECHERCHE_PLUMITIF.cs:37-39, :103-106) ; attribues : la case
///     est offerte (:107-110). Case cochee : ordre recalcule (CalculOrdreAppelsAffaires(RecupOrdreTypeClass(true), true), :64-70)
///     puis tous les numeros (ConstructNumAppaf). Sinon l'ordre des numeros existants (:121-122) et, a la sauvegarde, la
///     numerotation seulement si un appel n'a pas de numero (DoPreparerSauvegarde, OPE_EDIT_PLUMITIF.cs:291-300). Attribues, tous
///     presents, case non cochee : rien ne change -- le message de RIG v2 (:36, :38) ;
///   - LE TRI (TriAppelsAffaires, AudienceCabinet.cs:1533-1584) : sans recalcul, d'abord le numero existant (2 premiers caracteres,
///     puis les 3 suivants ; un appel sans numero prend le plus grand de son type, et « +1 » colle) ; si le calcul est par type de
///     classement, le rang du type (RecupOrdreTypeClass, :1717-1764 : les types hors AUDNC_TYPE_CLASSEMENT_ADMIS par code, puis les
///     admis dans leur ordre) ; puis l'heure d'appel (APPAF_HEURE_APPEL, sinon l'heure de l'audience) ; puis le role de l'instance
///     principale quand elle est au role de la meme audience, sinon le sien (InstancePrincipalePresenteDansAudience, Instance.cs:
///     1033-1040), l'id de l'instance principale, le role -- croissants si « instances croissantes », sinon decroissants (sauf l'id) ;
///   - LES REGLAGES DU CALCUL (GetBoolCalculForAppaf, :1653-1669) : un cabinet lit son code cabinet (R_CODECABINE_CALCUL_NUM_APPEL,
///     R_CODECABINE_PREAUD_INS_ASC), une audience sa section si elle en a une, sinon sa chambre (R_SECTION_* / R_CHAMBRE_*) ;
///   - LES NUMEROS (ConstructNumAppaf, :1592-1631) : « 01_001 », « 01_002 »... ; par type de classement, le prefixe change avec le
///     type (« 02_001 ») ; 4 chiffres au-dela de 999 appels ; puis AUDNC_NUM_APPEL_ATTRIB = 1. L'audience est sauvee meme en
///     brouillon (DoSauver, OPE_EDIT_PLUMITIF.cs:321-324) : le geste n'attend aucune edition.
/// Les comparaisons de texte sont celles de .NET dans RIG v2 (OrderBy sur des chaines : culture du poste, fr-FR) ; un numero
/// absent compte pour une chaine vide (RIG v2 leve une exception sur un type dont aucun appel n'est numerote).
///
/// CE QUE LA V3 NE REPREND PAS (nomme, rien en silence) : le PLUMITIF -- un etat Crystal (PlumitifPreaud, RigCrystalPlumitif)
/// exporte en PDF, archive en GED puis remis a la file des editions (CreateDdeEdition, OPE_EDIT_PLUMITIF.cs:420-500). Sans ce PDF,
/// RIG v2 lui-meme ne met rien en file (:424-426 : LancerPlumitif, puis « if (string.IsNullOrEmpty(pathPdf)) return null ») ; la V3
/// n'a pas le moteur Crystal : aucun PDF, donc aucune demande d'edition, et le message du geste le dit (PlumitifNonGenere) ; l'envoi
/// par courriel ou au coffre (services externes) ; le code-barre VIDIGED de l'audience (CreateVidiged) ; l'ordre des types de
/// classement deplace a la main (OPE_TC) ; la demande V2 du processus (le journal des gestes la remplace, sans facture).
/// </summary>
internal sealed class EcrivainPreparationAudience : IEcrivainEcran
{
    public string Procedure => "PROC_PREAUD";

    public string Ecran => "Préparation d'audience";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Decider];

    /// <summary>Le geste (le Valider du descripteur, Surveiller.cs) et la case de RIG v2 (OPE_RECHERCHE_PLUMITIF.Designer.cs:95).</summary>
    internal const string Valider = "Valider", ParametreRecalculer = "Recalculer les n° d'appel";

    /// <summary>Ce que RIG v2 fait aussi a la validation et que la V3 ne fait pas : le dire a l'agent, pas seulement dans ce code.</summary>
    // fix-ok: l'en-tete disait « l'ecran le dit » alors que le seul message du geste ne parlait que des numeros d'appel (lu le
    // 26/11, controle du lot 7 : « dire nommement pourquoi » le plumitif n'est pas mis en file) ; la cause V2 est :424-426.
    internal const string PlumitifNonGenere = "Le plumitif n'est pas généré : RIG v3 n'a pas l'état Crystal qui le produit dans RIG v2.";

    private static readonly StringComparer Texte = StringComparer.Create(CultureInfo.GetCultureInfo("fr-FR"), ignoreCase: false);

    private sealed record Audience(int Id, string Libelle, bool EstAudience, bool Attribues, string? Heure, string? Admis,
                                   bool ParTypeClassement, bool InstancesCroissantes);

    private sealed record Appel(int Id, string Role, string? Type, bool TypeConnu, string Numero, string? HeureAppel, int IdPrincipale,
                                string? RolePrincipale)
    {
        public string NouveauNumero { get; set; } = "";
    }

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var d = geste.Demande;
        if (d.Nom != Valider) return IssueGeste.Refuser($"Le geste « {d.Nom} » n'existe pas à la préparation d'audience de RIG v2.");
        if (d.Ids.Length == 0) return IssueGeste.Refuser("Cochez au moins une audience ou un cabinet.");
        var recalculer = (d.Valeurs.TryGetValue(ParametreRecalculer, out var c) ? c.Trim() : "").ToLowerInvariant() is "oui" or "true" or "1";
        var audiences = new List<Audience>();
        foreach (var id in d.Ids)
        {
            if (await LireAudienceAsync(geste, id, ct) is not { } a) return IssueGeste.Refuser($"L'audience « {id} » n'est pas dans la base du greffe.");
            audiences.Add(a);
        }
        // Rien ne change : numeros attribues, tous presents, case non cochee (OPE_RECHERCHE_PLUMITIF.cs:36, :38).
        var calculs = new List<(Audience Audience, List<Appel> Appels, bool Renumeroter)>();
        foreach (var a in audiences)
        {
            var appels = await LireAppelsAsync(geste, a, ct);
            var renumeroter = !a.Attribues || recalculer;
            if (!renumeroter && appels.All(x => x.Numero.Length > 0))
                return IssueGeste.Refuser($"{a.Libelle} : Les numéros d'appel ont déjà été attribués. Vous pouvez cependant relancer le calcul en cochant la case appropriée.");
            calculs.Add((a, appels, renumeroter));
        }

        var traces = new List<TraceGeste>();
        var total = 0;
        foreach (var (a, appels, renumeroter) in calculs)
        {
            var tries = Trier(a, appels, renumeroter);
            Numeroter(a, tries);
            foreach (var x in tries.Where(x => x.NouveauNumero != x.Numero))
                await geste.EcrireAsync("UPDATE v2.APPEL_AFFAIRE SET APPAF_NUM_APPEL = @n WHERE APPAF_ID_APPAF = @id", ct,
                    new SqlParameter("@n", x.NouveauNumero), new SqlParameter("@id", x.Id));
            await geste.EcrireAsync("UPDATE v2.AUDIENCE_CABINET SET AUDNC_NUM_APPEL_ATTRIB = 1 WHERE AUDNC_ID_ADNC = @id", ct, new SqlParameter("@id", a.Id));
            total += tries.Count;
            traces.Add(new TraceGeste("affaire", a.Libelle, a.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { NumerosAttribues = a.Attribues, Numeros = tries.Select(x => $"{x.Role}={x.Numero}").ToArray() },
                new { NumerosAttribues = true, Recalcul = renumeroter, Numeros = tries.Select(x => $"{x.Role}={x.NouveauNumero}").ToArray() }));
        }
        var n = calculs.Count;
        return IssueGeste.Ecrit($"Numéros d'appel attribués : {total} affaire{(total > 1 ? "s" : "")} sur {n} audience{(n > 1 ? "s" : "")}. {PlumitifNonGenere}",
                                [.. calculs.Select(x => x.Audience.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }

    /// <summary>TriAppelsAffaires (AudienceCabinet.cs:1533-1584).</summary>
    private static List<Appel> Trier(Audience a, List<Appel> appels, bool renumeroter)
    {
        var types = OrdreTypes(a, appels, renumeroter);
        string MaxDuType(Appel x) => appels.Where(y => y.Type == x.Type).Select(y => y.Numero).Max(Texte) ?? "";
        IOrderedEnumerable<Appel>? tri = null;
        if (!renumeroter)
            tri = appels.OrderBy(x => Gauche(x.Numero.Length > 0 ? x.Numero : MaxDuType(x), 2), Texte)
                        .ThenBy(x => SousChaine(x.Numero, 3, 3) is { Length: > 0 } s ? s : SousChaine(MaxDuType(x), 3, 3) + 1, Texte);
        if (a.ParTypeClassement)
        {
            string Rang(Appel x) => types.FindIndex(t => t == x.Type).ToString(CultureInfo.InvariantCulture);
            tri = tri is null ? appels.OrderBy(Rang, Texte) : tri.ThenBy(Rang, Texte);
        }
        string Heure(Appel x) => x.HeureAppel ?? a.Heure ?? "";
        tri = tri is null ? appels.OrderBy(Heure, Texte) : tri.ThenBy(Heure, Texte);
        string RoleTri(Appel x) => x.RolePrincipale ?? x.Role;
        tri = a.InstancesCroissantes
            ? tri.ThenBy(RoleTri, Texte).ThenBy(x => x.IdPrincipale).ThenBy(x => x.Role, Texte)
            : tri.ThenByDescending(RoleTri, Texte).ThenBy(x => x.IdPrincipale).ThenByDescending(x => x.Role, Texte);
        return [.. tri];
    }

    /// <summary>RecupOrdreTypeClass (AudienceCabinet.cs:1717-1764) : les types connus de TYPE_CLASSEMENT seulement.</summary>
    private static List<string?> OrdreTypes(Audience a, List<Appel> appels, bool pourRecalcul)
    {
        var connus = appels.Where(x => x.TypeConnu).Select(x => x.Type).ToHashSet();
        if (!pourRecalcul) return [.. appels.Select(x => x.Type).Distinct().Where(connus.Contains)];
        var admis = (a.Admis ?? "").Split(';').Where(x => x.Length > 0).ToList();
        var types = appels.Select(x => x.Type).OrderBy(x => x, Texte).Distinct().Where(x => !admis.Contains(x ?? "")).ToList();
        foreach (var t in admis) if (appels.Any(x => x.Type == t)) types.Add(t);
        return [.. types.Where(connus.Contains)];
    }

    /// <summary>ConstructNumAppaf (AudienceCabinet.cs:1592-1631).</summary>
    private static void Numeroter(Audience a, List<Appel> tries)
    {
        var format = tries.Count > 999 ? "0000" : "000";
        string? typeEnCours = null;
        var debut = true;
        var prefixe = 0;
        var suffixe = 0;
        foreach (var x in tries)
        {
            if (a.ParTypeClassement)
            {
                if (debut || x.Type != typeEnCours) { prefixe++; suffixe = 1; typeEnCours = x.Type; }
                else suffixe++;
            }
            else { prefixe = 1; suffixe++; }
            debut = false;
            x.NouveauNumero = prefixe.ToString("00", CultureInfo.InvariantCulture) + "_" + suffixe.ToString(format, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>AmiString.Left et AmiString.Substring (AmiString.cs:383-399), un texte absent valant « ».</summary>
    private static string Gauche(string texte, int lg) => lg >= texte.Length ? texte : texte[..lg];

    private static string SousChaine(string texte, int debut, int lg)
        => debut >= texte.Length ? "" : texte.Substring(debut, Math.Min(lg, texte.Length - debut));

    private static async Task<Audience?> LireAudienceAsync(GesteEnCours geste, string id, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        await using var r = await geste.LireAsync($$"""
            SELECT a.AUDNC_ID_ADNC, {{AudiencesV2.Libelle}} AS Libelle, CONVERT(bit, ISNULL(a.AUDNC_AUDIENCE, 1)) AS EstAudience,
                   CONVERT(bit, ISNULL(a.AUDNC_NUM_APPEL_ATTRIB, 0)) AS Attribues, a.AUDNC_HEURE, a.AUDNC_TYPE_CLASSEMENT_ADMIS,
                   CONVERT(bit, CASE WHEN a.AUDNC_AUDIENCE = 0 THEN ISNULL(cc.R_CODECABINE_CALCUL_NUM_APPEL, 0)
                                     WHEN se.R_SECTION_VALEUR IS NOT NULL THEN ISNULL(se.R_SECTION_CALCUL_NUM_APPEL, 0)
                                     ELSE ISNULL(ch.R_CHAMBRE_CALCUL_NUM_APPEL, 0) END) AS ParType,
                   CONVERT(bit, CASE WHEN a.AUDNC_AUDIENCE = 0 THEN ISNULL(cc.R_CODECABINE_PREAUD_INS_ASC, 0)
                                     WHEN se.R_SECTION_VALEUR IS NOT NULL THEN ISNULL(se.R_SECTION_PREAUD_INS_ASC, 0)
                                     ELSE ISNULL(ch.R_CHAMBRE_PREAUD_INS_ASC, 0) END) AS Croissant
            FROM v2.AUDIENCE_CABINET AS a WITH (UPDLOCK, HOLDLOCK)
            {{AudiencesV2.Jointures}}
            LEFT JOIN v2.CODE_CABINET AS cc ON cc.R_CODECABINE_VALEUR = a.AUDNC_CODE_CABINET
            WHERE a.AUDNC_ID_ADNC = @id
            """, ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        string? Opt(string col) => FormeLigne.Texte(r, col) is { Length: > 0 } s ? s : null;
        return new Audience(n, FormeLigne.Texte(r, "Libelle"), r["EstAudience"] is true, r["Attribues"] is true, Opt("AUDNC_HEURE"),
                            Opt("AUDNC_TYPE_CLASSEMENT_ADMIS"), r["ParType"] is true, r["Croissant"] is true);
    }

    /// <summary>Les appels de l'audience, avec le role de l'instance principale quand elle est au role de la meme audience.</summary>
    private static async Task<List<Appel>> LireAppelsAsync(GesteEnCours geste, Audience a, CancellationToken ct)
    {
        var appels = new List<Appel>();
        await using var r = await geste.LireAsync("""
            SELECT aa.APPAF_ID_APPAF, i.INSTN_NUM_ROLE, aa.APPAF_TYPE_CLASSEMENT, aa.APPAF_NUM_APPEL, aa.APPAF_HEURE_APPEL,
                   CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM v2.TYPE_CLASSEMENT AS tc WHERE tc.R_TYPECLASSE_VALEUR = aa.APPAF_TYPE_CLASSEMENT)
                                     THEN 1 ELSE 0 END) AS TypeConnu,
                   ISNULL(i.INSTN_ID_INSTN_INSTANCE_PRINCIPA, 0) AS IdPrincipale,
                   (SELECT p.INSTN_NUM_ROLE FROM v2.INSTANCE AS p WHERE p.INSTN_ID_INSTN = i.INSTN_ID_INSTN_INSTANCE_PRINCIPA
                      AND EXISTS (SELECT 1 FROM v2.APPEL_AFFAIRE AS ap WHERE ap.APPAF_ID_INSTN = p.INSTN_ID_INSTN AND ap.APPAF_ID_ADNC = aa.APPAF_ID_ADNC)) AS RolePrincipale
            FROM v2.APPEL_AFFAIRE AS aa WITH (UPDLOCK, HOLDLOCK)
            JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = aa.APPAF_ID_INSTN
            WHERE aa.APPAF_ID_ADNC = @id
            ORDER BY aa.APPAF_ID_APPAF
            """, ct, new SqlParameter("@id", a.Id));
        while (await r.ReadAsync(ct))
        {
            string? Opt(string col) => FormeLigne.Texte(r, col) is { Length: > 0 } s ? s : null;
            appels.Add(new Appel(Convert.ToInt32(r["APPAF_ID_APPAF"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "INSTN_NUM_ROLE"),
                Opt("APPAF_TYPE_CLASSEMENT"), r["TypeConnu"] is true, FormeLigne.Texte(r, "APPAF_NUM_APPEL"), Opt("APPAF_HEURE_APPEL"),
                Convert.ToInt32(r["IdPrincipale"], CultureInfo.InvariantCulture), Opt("RolePrincipale")));
        }
        return appels;
    }
}
