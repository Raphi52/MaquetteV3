using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Dépôts judiciaires » (PROC_DEPOTJUD, ecran fait main web/src/ecrans/EcranDepotsJud.tsx) : le geste NOUVEL EVENEMENT SURVENU d'une
/// instance de la serie JROP et son DEPOT numerote, ecrits dans la base du greffe. Plan « RIG v3 complet, mesure par le banc du
/// parcours complet », lot 8, 28/11/2026. Route : ModuleDepotsJud.cs ; la lecture : LectureDepotsJud.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RIG\DLL) :
///   - Processus\PROC_DEPOTJUD : l'objet est l'instance (PROC_DEPOTJUD.cs:19), son etape « Evénement survenu » (ETP_EVENEMENT_SURVENU,
///     depot obligatoire : PROC_DEPOTJUD.Designer.cs:66-75) ; la facturation n'apparait que si l'instance n'a aucun client
///     (PROC_DEPOTJUD.cs:57-64, Designer :40), et RIG v2 valide le processus sans elle quand la facturation est decochee dans la barre
///     d'outils (« ... ou la facturation doit être décochée », EtapeFacture\Rig_Etape_Facture\OPE_FACTU_GESTCOM.cs:947-956) ;
///   - les codes proposes (Rig_Ult_Metier\COMBO\JUDICIAIRE\ULT_COMBO_CODE_EVE_PRO.cs:76-104) : avec depot d'instance
///     (R_CODEEVEPRO_AVEC_DEPOT = 1, :78) et, pour DEPOTJUD, compatibles avec le type traduit de l'instance
///     (R_CODEEVEPRO_COMPATIBLE_&lt;type&gt; = 1, :87-96 ; Instance.TypeInstanceTraduite, RigMetier\JUDICIAIRE\Instance.cs:967-983), au
///     format des listes (Outils.makeSqlStandard : utilisables en saisie) ; le code est obligatoire (« Code évènement de procédure »,
///     OPE_EVENEMENT.Designer.cs:360-362) ;
///   - l'evenement survenu (ETP_EVENEMENT_INSTANCE_SURVENU.cs:33-37 ; EvenementProcedure.CreateEvenementProcedure) et le futur lie que
///     son code porte : EvenementsProcedureV2.SurvenuAsync (lot 7 du plan precedent), comme la saisie d'un survenu date
///     (OPE_EVENEMENT.cs:1587-1608) ;
///   - le depot (OPE_DEPOT.cs) : categorie = R_CODEEVEPRO_CATEGORIE_DEPOT_INS du code (:177-178), date = celle de l'evenement (:192-193),
///     numero dans la serie de la categorie (R_CATEGORIED_TYPE_NUM_DEPOT, annee du depot, :219-223), lie a l'evenement
///     (Evenement.AddDepotPC, :226) ; les colonnes de DEPOT_PC comme DepotsPcV2 (numero source GUID, etat info 1) ;
///   - la demande du processus, validee, sur l'instance : dans son affaire, a son nom courant et son role (Instance.CreateDemande,
///     Instance.cs:4697-4715 ; EcrivainSurveillanceIp.DemandeInstanceAsync).
/// REFUS (rien d'ecrit) : aucune instance JROP a ce role ; code absent ou hors de la liste ; date vide ou illisible ; code sans categorie
/// de depot d'instance ou categorie sans serie (le depot etant obligatoire).
/// NON REPRIS (nommes ; le message le dit) : la FACTURE -- la V3 n'a pas de moteur de facturation : le depot s'ecrit comme RIG v2 le
/// valide facturation decochee, et le message le dit pour une instance sans client ; le certificat de depot et les editions de
/// l'evenement (OPE_DEPOT.cs:234-280) ; le texte
/// calcule de l'evenement ; les numeros des PC, mesures et prevention liees dans « les numeros » de la demande (Instance.cs:4699-4712 :
/// le role seul) ; aucun ordre pour la liste des codes (RIG v2 n'en donne pas : ici par code). Un type d'instance sans colonne
/// COMPATIBLE dans CODE_EVE_PRO (RC) n'a aucun code.
/// TRACE : famille « affaire », « a cree ».
/// </summary>
internal static class EcritureDepotsJud
{
    /// <summary>Le nom de l'ecran au journal des gestes.</summary>
    public const string Ecran = "Dépôts judiciaires";

    /// <summary>Le code du processus de RIG v2 (DMND_CODE_PROSS).</summary>
    public const string Processus = "DEPOTJUD";

    /// <summary>Les gestes ecrits : tout autre type est refuse par la route (GesteFaitMain, rien d'ecrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Creer];

    /// <summary>Les types traduits qui ont leur colonne R_CODEEVEPRO_COMPATIBLE_* (CODE_EVE_PRO de RIG_DEV).</summary>
    private static readonly HashSet<string> Compatibles = ["OP", "IP", "JC", "CX", "REF", "PC", "PRV", "EXP"];

    /// <summary>Le type traduit d'une instance (Instance.TypeInstanceTraduite, Instance.cs:972-982).</summary>
    internal static string Traduit(string type) => type switch
    {
        "07" => "CX", "08" => "PC", "09" => "REF", "PR" => "PRV", "RP" => "OP", _ => type,
    };

    /// <summary>L'instance JROP d'un role : ce que le geste en lit.</summary>
    private sealed record Instance(int Id, string Role, string Nom, int? IdAffaire, string Type, bool AvecClient);

    private static async Task<Instance?> InstanceAsync(Func<string, SqlParameter[], Task<SqlDataReader>> lire, string role)
    {
        await using var r = await lire("""
            SELECT TOP (1) i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, i.INSTN_ID_AFFRE,
                   COALESCE(NULLIF(LTRIM(RTRIM(i.INSTN_TYPE_INSTANCE)), ''), ca.R_CODEAFFAIR_TYPE_INSTANCE) AS TypeInstance,
                   CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM v2.DEMANDE AS d
                                                  JOIN v2.FACTURE AS fa ON fa.FACTU_NUM_DEMANDE = d.DMND_NUM_DEMANDE
                                                  JOIN rec.Client AS cl ON cl.Id = fa.FACTU_ID_CLIEN
                                                  WHERE d.DMND_ID_AFFRE = i.INSTN_ID_AFFRE)
                                     THEN 1 ELSE 0 END) AS AvecClient
            FROM v2.INSTANCE AS i
            JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
            WHERE i.INSTN_NUM_ROLE = @role AND ca.R_CODEAFFAIR_TYPE_NUM_ROLE = 'JROP'
            ORDER BY i.INSTN_ID_INSTN
            """, [new SqlParameter("@role", role)]);
        if (!await r.ReadAsync()) return null;
        return new Instance(Convert.ToInt32(r["INSTN_ID_INSTN"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "INSTN_NUM_ROLE").Trim(),
            FormeLigne.Texte(r, "INSTN_NOM_COURANT").Trim(), r["INSTN_ID_AFFRE"] is DBNull ? null : Convert.ToInt32(r["INSTN_ID_AFFRE"], CultureInfo.InvariantCulture),
            Traduit(FormeLigne.Texte(r, "TypeInstance").Trim()), r["AvecClient"] is true);
    }

    /// <summary>La requete des codes proposes pour le type traduit <paramref name="type"/> ; null s'il n'a pas de colonne COMPATIBLE.</summary>
    private static string? RequeteCodes(string type) => Compatibles.Contains(type)
        ? $"""
          SELECT R_CODEEVEPRO_VALEUR, R_CODEEVEPRO_LIBELLE_ECRAN FROM v2.CODE_EVE_PRO
          WHERE R_CODEEVEPRO_AVEC_DEPOT = 1 AND R_CODEEVEPRO_COMPATIBLE_{type} = 1 AND ISNULL(R_CODEEVEPRO_UTILISABLE_SAISIE, 1) = 1
          ORDER BY R_CODEEVEPRO_VALEUR
          """
        : null;

    /// <summary>Les codes proposes pour l'instance JROP de role <paramref name="role"/> (LECTURE SEULE) ; aucun sans instance.</summary>
    public static async Task<CodesDepotLus> CodesAsync(SqlConnection baseGreffe, string role, CancellationToken ct)
    {
        role = role.Trim();
        if (role.Length == 0) return new CodesDepotLus("", []);
        Task<SqlDataReader> Lire(string sql, SqlParameter[] p) => LectureV2.LireAsync(baseGreffe, sql, ct, p);
        if (await InstanceAsync(Lire, role) is not { } instance) return new CodesDepotLus(role, []);
        if (RequeteCodes(instance.Type) is not { } requete) return new CodesDepotLus(instance.Role, [], instance.Type);
        var codes = new List<CodeDepotLu>();
        await using var r = await Lire(requete, []);
        while (await r.ReadAsync(ct))
            codes.Add(new CodeDepotLu(FormeLigne.Texte(r, "R_CODEEVEPRO_VALEUR").Trim(), FormeLigne.Texte(r, "R_CODEEVEPRO_LIBELLE_ECRAN").Trim()));
        return new CodesDepotLus(instance.Role, [.. codes], instance.Type);
    }

    /// <summary>Ecrit l'evenement survenu et son depot sur l'instance du role donne (Ids du geste).</summary>
    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids is not [var role] || role.Trim().Length == 0) return IssueGeste.Refuser("Choisissez une instance de la liste.");
        Task<SqlDataReader> Lire(string sql, SqlParameter[] p) => geste.LireAsync(sql, ct, p);
        if (await InstanceAsync(Lire, role.Trim()) is not { } instance)
            return IssueGeste.Refuser($"Aucune instance de la série JROP ne porte le rôle « {role.Trim()} » dans la base du greffe : relisez la liste.");

        var code = Saisi(geste, DepotEvenement.Section, DepotEvenement.Code);
        if (code.Length == 0) return IssueGeste.Refuser(Obligatoire(DepotEvenement.Code));
        var texteDate = Saisi(geste, DepotEvenement.Section, DepotEvenement.Date);
        if (texteDate.Length == 0) return IssueGeste.Refuser(Obligatoire(DepotEvenement.Date));
        if (Date(texteDate) is not { } date) return IssueGeste.Refuser($"« {DepotEvenement.Date} » : date illisible ({texteDate}).");

        string? libelle = null;
        if (RequeteCodes(instance.Type) is { } requete)
        {
            await using var r = await Lire(requete, []);
            while (await r.ReadAsync(ct))
                if (FormeLigne.Texte(r, "R_CODEEVEPRO_VALEUR").Trim() == code) libelle = FormeLigne.Texte(r, "R_CODEEVEPRO_LIBELLE_ECRAN").Trim();
        }
        if (libelle is null)
            return IssueGeste.Refuser($"Le code « {code} » n'est pas un événement avec dépôt proposé pour l'instance {instance.Role} (type {instance.Type}) dans la base du greffe.");

        string categorie;
        string? serie;
        await using (var r = await Lire("""
            SELECT c.R_CODEEVEPRO_CATEGORIE_DEPOT_INS, cd.R_CATEGORIED_TYPE_NUM_DEPOT
            FROM v2.CODE_EVE_PRO AS c LEFT JOIN v2.CATEGORIE_DEPOT AS cd ON cd.R_CATEGORIED_VALEUR = c.R_CODEEVEPRO_CATEGORIE_DEPOT_INS
            WHERE c.R_CODEEVEPRO_VALEUR = @c
            """, [new SqlParameter("@c", code)]))
        {
            await r.ReadAsync(ct);
            categorie = FormeLigne.Texte(r, "R_CODEEVEPRO_CATEGORIE_DEPOT_INS").Trim();
            serie = FormeLigne.Texte(r, "R_CATEGORIED_TYPE_NUM_DEPOT").Trim() is { Length: > 0 } s ? s : null;
        }
        if (categorie.Length == 0)
            return IssueGeste.Refuser($"Le code « {code} » n'a pas de catégorie de dépôt d'instance dans la base du greffe : le dépôt, obligatoire, ne peut pas être numéroté.");
        if (serie is null)
            return IssueGeste.Refuser($"La catégorie de dépôt « {categorie} » du code « {code} » n'a pas de série de numéros dans la base du greffe.");
        var (numero, refus) = await NumeroAsync(geste, serie, date.Year, ct);
        if (numero is null) return IssueGeste.Refuser(refus!);

        var (idDemande, numeroDemande, refusDemande) = await EcrivainSurveillanceIp.DemandeInstanceAsync(geste, Processus, instance.Id, instance.IdAffaire,
            instance.Nom, instance.Role, ct);
        if (idDemande is null) return IssueGeste.Refuser(refusDemande!);
        var ecrits = await EvenementsProcedureV2.SurvenuAsync(geste, instance.Id, code, date, avecFutur: true, ct);
        var depot = await InsererAsync(geste, "v2.DEPOT_PC", "DPTPC_ID_DPTPC", new Dictionary<string, object?>
        {
            ["DPTPC_ID_EVNPR"] = ecrits.IdSurvenu, ["DPTPC_NUM_DEPOT"] = numero, ["DPTPC_DATE"] = date.Date, ["DPTPC_CATEGORIE_DEPOT"] = categorie,
            ["DPTPC_ETAT_INFO"] = "1", ["DPTPC_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(), ["DPTPC_TOP_EXPERIAN"] = false,
        }, ct);

        var jour = date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return IssueGeste.Ecrit(
            $"Dépôt {numero} enregistré sur l'instance {instance.Role} : {libelle} survenu le {jour}"
            + (ecrits.CodeFutur is { } futur ? $", événement à venir {futur} créé" : "")
            + (instance.AvecClient ? "" : " ; sans facture (l'instance n'a aucun client : facturation décochée, la V3 ne facture pas)")
            + " ; le certificat de dépôt et les éditions ne sont pas repris par la V3.",
            [instance.Role],
            new TraceGeste("affaire", $"le dépôt {numero}", depot.ToString(CultureInfo.InvariantCulture), "a cree", null,
                new { Depot = numero, Instance = instance.Role, CodeEvenement = code, Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                      Evenement = ecrits.IdSurvenu, Futur = ecrits.CodeFutur, Demande = numeroDemande }));
    }
}
