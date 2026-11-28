using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Créer une audience » (PROC_AUDIENCE, écran fait main web/src/ecrans/EcranAudiences.tsx) : le geste CRÉER L'AUDIENCE, la saisie
/// OPE_CREATION_AUDIENCE de RIG v2, écrite dans la base du greffe. Jouée par la route POST RoutesCreationAudience.Creer (ModuleAudiences.cs)
/// dans la transaction et le journal des gestes du socle (EcrivainsEcrans.JouerAsync, sa version pour un écran fait main). Plan « RIG v3
/// complet, mesuré par le banc du parcours complet », lot 9, 28/11/2026 ; la lecture : LectureAudiences.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source) :
///   - RIG\DLL\Processus\PROC_AUDIENCE\Processus\PROC_AUDIENCE.cs:48-62 : une audience neuve (AudienceCabinet.CreateAudience,
///     Outils\DLL\RigMetier\JUDICIAIRE\AudienceCabinet.cs:1254-1267 : AUDNC_AUDIENCE = 1, type, chambre, section, date, heure) ;
///   - les OBLIGATOIRES (Etapes\OPE_CREATION_AUDIENCE.Designer.cs:88, 98, 108, 120) : date, heure, type ; la chambre l'est quand le type
///     le demande (R_TYPEAUDCAB_PARAMETRAGE_CHAMBRE, OPE_CREATION_AUDIENCE.cs:65-74), la section quand la chambre le demande
///     (R_CHAMBRE_PARAMETRAGE_SECTION, :76-83) et qu'elle a des sections ; message de RIG v2 « Champ obligatoire » et le titre du champ
///     (RigAutomate\ULT\Ult.moteur.cs:14, :516-517) ;
///   - les sections offertes sont celles dont le code contient celui de la chambre (:84-98) ; une liste ne propose que ses valeurs
///     utilisables en saisie, un type d'AUDIENCE (R_TYPEAUDCAB_AUDIENCE = 1 : un cabinet se crée dans PROC_CABINET) : une valeur hors
///     liste est refusée (RIG v2 n'a pas de message : la valeur n'est pas dans la liste) ;
///   - les types de classement admis, codes séparés par « ; » (OPE_TYPE_CLASSEMENT_ADMIS.TypesClassementAdmis, EtapeJudiciaire\
///     Rig_Etape_Instance\INSTANCE\Saisie\ETP_APP_AFFAIRE\GESTION_AUD_CAB\OPE_TYPE_CLASSEMENT_ADMIS.cs:31-40 ; écrits à la sauvegarde,
///     OPE_CREATION_AUDIENCE.cs:116-121) ; un code absent de TYPE_CLASSEMENT : « Impossible de trouver le Type de Classement souhaité. »
///     (:197) ;
///   - le DOUBLON (OPE_CREATION_AUDIENCE.cs:127-137 ; AudienceCabinet.GetAudience(chambre, date, heure, section), :881-905) : une audience
///     (AUDNC_AUDIENCE = 1) à la même chambre, date, heure et section -> « L'audience saisie existe déjà. Merci de vérifier votre saisie. » ;
///   - les valeurs par défaut (Outils\DLL\RigBaseGreffe\GREFFE\AUDIENCE_CABINET.cs:240-271) : 0 appel, numéros d'appel non attribués,
///     publique, non diffusée, disponible sur le web ; l'état « 1 » de l'audience et du cabinet, celui de toutes les audiences de RIG_DEV
///     (mesuré le 28/11/2026 sur la copie de développement ; même choix qu'EcrivainCabinets.cs pour PROC_CABINET).
/// NON REPRIS (nommés) : la DEMANDE RIG v2 « Création : » + l'audience (PROC_AUDIENCE.DoSauveBeforeEtape, :74-85) -- une trace, que le
/// journal des gestes remplace (décision du plan, comme l'arrivée du lot 7) ; les invitations de calendrier au juge et au greffier de la
/// composition (_GererInvitationsJuges / _GererInvitationsGreffiers, :81-155) -- un courriel, service externe, et une audience neuve n'a
/// pas encore de composition ; le préremplissage des classements admis par le paramétrage de la chambre ou de la section
/// (R_*_ORDRE_PAR_TYPE, OPE_TYPE_CLASSEMENT_ADMIS.cs:105-140) : l'écran V3 fait cocher les types admis.
/// TRACE : famille « reglage » (comme les cabinets de la même table), « a cree ».
/// </summary>
public static partial class EcritureAudiences
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Créer une audience";

    /// <summary>Le message de RIG v2 d'un champ obligatoire vide (Ult.moteur.cs:14).</summary>
    public const string ChampObligatoire = "Champ obligatoire";

    /// <summary>Le doublon (OPE_CREATION_AUDIENCE.cs:135).</summary>
    public const string AudienceExistante = "L'audience saisie existe déjà. Merci de vérifier votre saisie.";

    /// <summary>Un type de classement absent de TYPE_CLASSEMENT (OPE_TYPE_CLASSEMENT_ADMIS.cs:197).</summary>
    public const string ClassementIntrouvable = "Impossible de trouver le Type de Classement souhaité.";

    /// <summary>Les gestes écrits : tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Creer];

    /// <summary>L'audience saisie.</summary>
    private sealed record Saisie(DateTime Date, string Heure, string Type, string? Chambre, string? Section, string? Classements, bool Publique,
                                 bool NonDisponibleWeb);

    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // Les obligatoires, dans l'ordre des champs de l'écran (Designer.cs:88, 98, 108).
        var dateTexte = Valeur(geste, RoutesCreationAudience.ChampDate);
        if (dateTexte.Length == 0) return Obligatoire(RoutesCreationAudience.ChampDate);
        if (!DateTime.TryParseExact(dateTexte, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return IssueGeste.Refuser($"« {RoutesCreationAudience.ChampDate} » n'est pas une date : {dateTexte}.");
        var heure = Valeur(geste, RoutesCreationAudience.ChampHeure);
        if (heure.Length == 0) return Obligatoire(RoutesCreationAudience.ChampHeure);
        if (!Heure().IsMatch(heure)) return IssueGeste.Refuser($"« {RoutesCreationAudience.ChampHeure} » : une heure HH:MM ({heure}).");
        var type = Valeur(geste, RoutesCreationAudience.ChampType);
        if (type.Length == 0) return Obligatoire(RoutesCreationAudience.ChampType);

        string libelleType;
        bool chambreExigee;
        await using (var r = await geste.LireAsync("""
            SELECT R_TYPEAUDCAB_LIBELLE_ECRAN, R_TYPEAUDCAB_PARAMETRAGE_CHAMBRE FROM v2.TYPE_AUD_CAB
            WHERE R_TYPEAUDCAB_VALEUR = @t AND R_TYPEAUDCAB_AUDIENCE = 1 AND R_TYPEAUDCAB_UTILISABLE_SAISIE = 1
            """, ct, new SqlParameter("@t", type)))
        {
            if (!await r.ReadAsync(ct))
                return IssueGeste.Refuser($"« {RoutesCreationAudience.ChampType} » : « {type} » n'est pas un type d'audience de la liste (TYPE_AUD_CAB).");
            libelleType = FormeLigne.Texte(r, "R_TYPEAUDCAB_LIBELLE_ECRAN");
            chambreExigee = r["R_TYPEAUDCAB_PARAMETRAGE_CHAMBRE"] is true;
        }

        var chambre = Valeur(geste, RoutesCreationAudience.ChampChambre);
        var sectionExigee = false;
        string? libelleChambre = null;
        if (chambre.Length == 0)
        {
            if (chambreExigee) return Obligatoire(RoutesCreationAudience.ChampChambre);
        }
        else
        {
            await using var r = await geste.LireAsync("""
                SELECT R_CHAMBRE_LIBELLE_ECRAN, R_CHAMBRE_PARAMETRAGE_SECTION FROM v2.CHAMBRE WHERE R_CHAMBRE_VALEUR = @c AND R_CHAMBRE_UTILISABLE_SAISIE = 1
                """, ct, new SqlParameter("@c", chambre));
            if (!await r.ReadAsync(ct))
                return IssueGeste.Refuser($"« {RoutesCreationAudience.ChampChambre} » : « {chambre} » n'est pas une chambre de la liste (CHAMBRE).");
            libelleChambre = FormeLigne.Texte(r, "R_CHAMBRE_LIBELLE_ECRAN");
            sectionExigee = r["R_CHAMBRE_PARAMETRAGE_SECTION"] is true;
        }

        // Les sections de la chambre : leur code contient celui de la chambre (OPE_CREATION_AUDIENCE.cs:84-98).
        var section = Valeur(geste, RoutesCreationAudience.ChampSection);
        var sectionsDeLaChambre = chambre.Length == 0 ? 0 : await CompterAsync(geste,
            "SELECT COUNT(*) FROM v2.SECTION WHERE R_SECTION_UTILISABLE_SAISIE = 1 AND CHARINDEX(@c, R_SECTION_VALEUR) > 0", ct,
            new SqlParameter("@c", chambre));
        if (section.Length == 0)
        {
            if (sectionExigee && sectionsDeLaChambre > 0) return Obligatoire(RoutesCreationAudience.ChampSection);
        }
        else if (chambre.Length == 0 || await CompterAsync(geste, """
                     SELECT COUNT(*) FROM v2.SECTION WHERE R_SECTION_VALEUR = @s AND R_SECTION_UTILISABLE_SAISIE = 1 AND CHARINDEX(@c, R_SECTION_VALEUR) > 0
                     """, ct, new SqlParameter("@s", section), new SqlParameter("@c", chambre)) == 0)
            return IssueGeste.Refuser(chambre.Length == 0
                ? $"« {RoutesCreationAudience.ChampSection} » : une section se choisit dans celles de la chambre ; aucune chambre n'est choisie."
                : $"« {RoutesCreationAudience.ChampSection} » : « {section} » n'est pas une section de la chambre {chambre} (SECTION).");

        // Les types de classement admis : chacun dans TYPE_CLASSEMENT, la liste dans les 128 caractères de la colonne.
        var classements = Valeur(geste, RoutesCreationAudience.ChampClassements)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var c in classements)
            if (await CompterAsync(geste, "SELECT COUNT(*) FROM v2.TYPE_CLASSEMENT WHERE R_TYPECLASSE_VALEUR = @c AND R_TYPECLASSE_UTILISABLE_SAISIE = 1",
                    ct, new SqlParameter("@c", c)) == 0)
                return IssueGeste.Refuser($"{ClassementIntrouvable} ({c})");
        var admis = classements.Length == 0 ? null : string.Join(";", classements);
        if (admis is { Length: > 128 })
            return IssueGeste.Refuser($"« {RoutesCreationAudience.ChampClassements} » : {admis.Length} caractères, la colonne en porte 128 (AUDNC_TYPE_CLASSEMENT_ADMIS).");

        var publique = Oui(Valeur(geste, RoutesCreationAudience.ChampPublique), parDefaut: true);
        var nonWeb = Oui(Valeur(geste, RoutesCreationAudience.ChampNonDisponibleWeb), parDefaut: false);
        if (publique is null || nonWeb is null)
            return IssueGeste.Refuser($"« {RoutesCreationAudience.ChampPublique} » et « {RoutesCreationAudience.ChampNonDisponibleWeb} » : oui ou non.");
        var s = new Saisie(date.Date, heure, type, chambre.Length == 0 ? null : chambre, section.Length == 0 ? null : section, admis,
                           publique.Value, nonWeb.Value);

        // Le doublon : AudienceCabinet.GetAudience(chambre, date, heure, section), une valeur vide = NULL (DBString, :881-905).
        if (await CompterAsync(geste, """
                SELECT COUNT(*) FROM v2.AUDIENCE_CABINET WITH (UPDLOCK, HOLDLOCK)
                WHERE AUDNC_AUDIENCE = 1 AND ISNULL(AUDNC_CHAMBRE, '') = @c AND AUDNC_DATE = @d AND ISNULL(AUDNC_HEURE, '') = @h
                  AND ISNULL(AUDNC_SECTION, '') = @s
                """, ct, new SqlParameter("@c", chambre), new SqlParameter("@d", s.Date), new SqlParameter("@h", heure),
                new SqlParameter("@s", section)) > 0)
            return IssueGeste.Refuser(AudienceExistante);

        var id = await geste.InsererAsync("v2.AUDIENCE_CABINET", "AUDNC_ID_ADNC", new Dictionary<string, object?>
        {
            ["AUDNC_AUDIENCE"] = true,
            ["AUDNC_TYPE_AUD_CAB"] = s.Type,
            ["AUDNC_TYPE_CLASSEMENT_ADMIS"] = s.Classements,
            ["AUDNC_DATE"] = s.Date,
            ["AUDNC_HEURE"] = s.Heure,
            ["AUDNC_NOMBRE_APPELS"] = (short)0,
            ["AUDNC_NUM_APPEL_ATTRIB"] = false,
            ["AUDNC_PUBLIQUE"] = s.Publique,
            ["AUDNC_CHAMBRE"] = s.Chambre,
            ["AUDNC_SECTION"] = s.Section,
            ["AUDNC_ETAT_AUDIENCE"] = "1",
            ["AUDNC_ETAT_CABINET"] = "1",
            ["AUDNC_TOP_DIFFUSION"] = false,
            ["AUDNC_NON_DISPONIBLE_WEB"] = s.NonDisponibleWeb,
        }, ct);
        var texteId = id.ToString(CultureInfo.InvariantCulture);
        var quoi = $"l'audience du {FormeLigne.Date(s.Date)} à {s.Heure}";
        return IssueGeste.Ecrit($"Audience créée : {libelleType} du {FormeLigne.Date(s.Date)} à {s.Heure}"
                                + (libelleChambre is null ? "." : $", {libelleChambre}."), [texteId],
            new TraceGeste("reglage", quoi, texteId, "a cree", null, s));
    }

    private static IssueGeste Obligatoire(string champ) => IssueGeste.Refuser($"{ChampObligatoire} : {champ}.");

    private static string Valeur(GesteEnCours geste, string cle)
        => geste.Demande.Valeurs.TryGetValue(cle, out var v) ? v?.Trim() ?? "" : "";

    /// <summary>« oui » / « non » d'une case ; vide : <paramref name="parDefaut"/> ; autre chose : null.</summary>
    private static bool? Oui(string texte, bool parDefaut) => texte.ToLowerInvariant() switch
    {
        "" => parDefaut,
        "oui" or "true" or "1" => true,
        "non" or "false" or "0" => false,
        _ => null,
    };

    private static async Task<int> CompterAsync(GesteEnCours geste, string sql, CancellationToken ct, params SqlParameter[] p)
    {
        await using var r = await geste.LireAsync(sql, ct, p);
        await r.ReadAsync(ct);
        return r.GetInt32(0);
    }

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex Heure();
}
