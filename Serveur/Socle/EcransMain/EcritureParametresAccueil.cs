using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Mon accueil » (PROC_ACCUEIL_PARAM, écran fait main web/src/ecrans/EcranParametresAccueil.tsx) : le geste ENREGISTRER MES RÉGLAGES
/// D'ACCUEIL, celui que RIG v2 joue à la fermeture de l'onglet. Joué par la route POST RoutesParametresAccueil.Enregistrer
/// (ModuleParametresAccueil.cs) dans la transaction et le journal des gestes du socle (EcrivainsEcrans.JouerAsync, sa version pour un
/// écran fait main) : mêmes gardes d'écriture (v2.* seulement). La lecture : LectureParametresAccueil.cs.
/// Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 13, 28/11/2026.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source) :
///   - RIG\DLL\Processus\PROC_ACCUEIL_PARAM\FORM_SKIN_ACCUEIL_PARAM.cs : OnFormClosed (:97-103) appelle _SaveParametrage (:620-648), qui
///     écrit chaque réglage par User.SetParametre(clé ConsoleAccueil_*, valeur.ToString()) ; seules les clés que l'écran V3 montre sont
///     écrites ici (<see cref="Ecrites"/>) : l'intervalle des alertes (:623), la largeur et la hauteur maximales (:624-625), la colonne
///     et le sens du tri des alertes (:635-636), les animations -- pour un ADMINISTRATEUR seulement (:640 ; un autre agent : la clé
///     n'est pas écrite, comme la V2) --, l'autofocus (:641), la molette (:642) et la date libre (:643) ;
///   - les types et bornes de RIG v2 : entiers (_intervalSlideshowAlertes, _indexColTriAlertes, _ordreTriColAlertes 0 ascendant /
///     1 descendant, :28-31) et cases (bool.ToString() : « True » / « False », :30, :32-33) ; l'intervalle, la largeur et la hauteur
///     sont des NumericUpDown bornés de 1000 à 10000 (FORM_SKIN_ACCUEIL_PARAM.Designer.cs:617-626, :1167-1176, :1201-1210) : la V2 ne
///     peut pas en enregistrer d'autre, la V3 refuse (RIG v2 n'a pas de message : le contrôle borne la saisie) ;
///   - Outils\DLL\RigMetier\Habilitation\Utilisateur.cs, SetParametre (:170-182) : le réglage existant de l'agent prend la valeur
///     (PRMTR_VALEUR_PARAMETRE), sinon une ligne neuve (ParametreUtilisateur.CreateParametreUtilisateur, Divers\ParametreUtilisateur.cs:60-66 :
///     PRMTR_ID_UTLST, PRMTR_NOM_PARAMETRE, PRMTR_VALEUR_PARAMETRE ; PRMTR_ID_PRMTR est l'IDENTITY de la table, RigDatabase\dbo\Tables\
///     PARAMETRE_UTILISATEUR.sql) ; la sauvegarde (Sauve) écrit le tout d'un coup.
/// L'agent RIG v2 est soc.Compte.IdV2 (UTLST_ID_UTLST) ; sans lui, rien ne s'enregistre (refus).
/// NON REPRIS (nommés) : le thème, les processus de démarrage, l'onglet et le sous-menu par défaut, la barre d'outils, le zoom PDF
/// (:622, :626-638, :639, :644 : l'écran V3 ne les montre pas), les imprimantes PaperCut (_SavePapercutParametres, service externe) et
/// le plein écran (_SaveParamZoneSplitter, hors PARAMETRE_UTILISATEUR).
/// TRACE : famille « compte » (les réglages sont ceux du compte de l'agent), « a modifie ».
/// </summary>
public static class EcritureParametresAccueil
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Mon accueil";

    /// <summary>La clé des animations, réservée aux administrateurs (FORM_SKIN_ACCUEIL_PARAM.cs:640).</summary>
    public const string CleAnimations = "ConsoleAccueil_AllowAnimations";

    /// <summary>Les gestes écrits : tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Enregistrer];

    private enum Sorte { Case, Entier, Borne, Sens }

    /// <summary>Les clés que RIG v2 écrit à la fermeture de l'onglet ET que l'écran V3 montre, avec leur type V2.</summary>
    public static readonly IReadOnlyList<string> Ecrites =
    [
        "ConsoleAccueil_IntervalSlideshowAlertes", "ConsoleAccueil_Form_MaxWidth", "ConsoleAccueil_Form_MaxHeight",
        "ConsoleAccueil_IndexColTriAlertes", "ConsoleAccueil_OrdreTriColAlertes", CleAnimations,
        "ConsoleAccueil_AutofocusOnClickIconEtpOpe", "ConsoleAccueil_InterdireMoletteComboCpp", "ConsoleAccueil_DateSaisieLibreCpp",
    ];

    private static Sorte SorteDe(string cle) => cle switch
    {
        "ConsoleAccueil_IntervalSlideshowAlertes" or "ConsoleAccueil_Form_MaxWidth" or "ConsoleAccueil_Form_MaxHeight" => Sorte.Borne,
        "ConsoleAccueil_IndexColTriAlertes" => Sorte.Entier,
        "ConsoleAccueil_OrdreTriColAlertes" => Sorte.Sens,
        _ => Sorte.Case,
    };

    /// <summary>Enregistre les réglages d'accueil reçus (Valeurs du geste : clé ConsoleAccueil_* -> valeur) pour l'agent appelant.</summary>
    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var recus = geste.Demande.Valeurs.Where(v => v.Key.Length > 0).ToDictionary(v => v.Key.Trim(), v => (v.Value ?? "").Trim(), StringComparer.Ordinal);
        if (recus.Count == 0) return IssueGeste.Refuser("Aucun réglage d'accueil à enregistrer.");
        if (recus.Keys.FirstOrDefault(k => !Ecrites.Contains(k)) is { } inconnue)
            return IssueGeste.Refuser($"Le réglage « {inconnue} » n'est pas un réglage d'accueil que RIG v2 enregistre (FORM_SKIN_ACCUEIL_PARAM.cs:620-648).");

        // Les contrôles d'abord : un seul réglage hors des valeurs de RIG v2, et rien n'est écrit.
        var aEcrire = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (cle, valeur) in recus)
        {
            if (cle == CleAnimations && !geste.Agent.EstAdministrateur) continue;   // :640 : la V2 ne l'écrit pas pour un autre agent
            var normale = Normaliser(SorteDe(cle), valeur);
            if (normale is null) return IssueGeste.Refuser(Refus(cle, valeur));
            aEcrire[cle] = normale;
        }

        int? idV2;
        await using (var r = await geste.LireAsync("SELECT IdV2 FROM soc.Compte WHERE Id = @id", ct,
                         new SqlParameter("@id", SqlDbType.Int) { Value = geste.Agent.Id }))
            idV2 = await r.ReadAsync(ct) && !r.IsDBNull(0) ? r.GetInt32(0) : null;
        if (idV2 is not { } utilisateur)
            return IssueGeste.Refuser("Vous n'avez pas de compte RIG v2 : vos réglages d'accueil n'ont pas où s'enregistrer (PARAMETRE_UTILISATEUR).");
        if (aEcrire.Count == 0)
            return IssueGeste.Refuser("Rien à enregistrer : les animations ne s'enregistrent que pour un administrateur (FORM_SKIN_ACCUEIL_PARAM.cs:640).");

        var avant = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var r = await geste.LireAsync($"""
                         SELECT PRMTR_NOM_PARAMETRE, PRMTR_VALEUR_PARAMETRE FROM v2.PARAMETRE_UTILISATEUR
                         WHERE PRMTR_ID_UTLST = @idV2 AND PRMTR_NOM_PARAMETRE IN ({string.Join(", ", aEcrire.Keys.Select((_, i) => "@c" + i))})
                         ORDER BY PRMTR_ID_PRMTR
                         """, ct, [new SqlParameter("@idV2", SqlDbType.Int) { Value = utilisateur },
                                   .. aEcrire.Keys.Select((c, i) => new SqlParameter("@c" + i, SqlDbType.VarChar, 255) { Value = c })]))
            while (await r.ReadAsync(ct)) avant[r.GetString(0)] = r.IsDBNull(1) ? "" : r.GetString(1).Trim();

        foreach (var (cle, valeur) in aEcrire)
        {
            // Utilisateur.SetParametre (:170-182) : le réglage existant prend la valeur ; sinon une ligne neuve.
            var modifies = await geste.EcrireAsync(
                "UPDATE v2.PARAMETRE_UTILISATEUR SET PRMTR_VALEUR_PARAMETRE = @v WHERE PRMTR_ID_UTLST = @idV2 AND PRMTR_NOM_PARAMETRE = @c", ct,
                new SqlParameter("@v", SqlDbType.VarChar, 8000) { Value = valeur },
                new SqlParameter("@idV2", SqlDbType.Int) { Value = utilisateur },
                new SqlParameter("@c", SqlDbType.VarChar, 255) { Value = cle });
            if (modifies == 0)
                await geste.InsererAsync("v2.PARAMETRE_UTILISATEUR", "PRMTR_ID_PRMTR", new Dictionary<string, object?>
                {
                    ["PRMTR_ID_UTLST"] = utilisateur,
                    ["PRMTR_NOM_PARAMETRE"] = cle,
                    ["PRMTR_VALEUR_PARAMETRE"] = valeur,
                }, ct);
        }

        var ecartee = recus.ContainsKey(CleAnimations) && !aEcrire.ContainsKey(CleAnimations)
            ? " Les animations ne s'enregistrent que pour un administrateur." : "";
        return IssueGeste.Ecrit($"{aEcrire.Count} réglage(s) d'accueil enregistré(s).{ecartee}", [.. aEcrire.Keys],
            new TraceGeste("compte", $"les réglages d'accueil de {geste.Agent.NomAffiche}", utilisateur.ToString(CultureInfo.InvariantCulture),
                "a modifie", aEcrire.Keys.Where(avant.ContainsKey).ToDictionary(k => k, k => avant[k]), aEcrire));
    }

    /// <summary>La valeur telle que RIG v2 l'écrit (ToString de son type), ou null si la V2 ne peut pas la tenir.</summary>
    private static string? Normaliser(Sorte sorte, string valeur)
    {
        if (sorte == Sorte.Case)
            return bool.TryParse(valeur, out var b) ? b.ToString() : null;
        if (!int.TryParse(valeur, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return null;
        return sorte switch
        {
            Sorte.Borne when n is < 1000 or > 10000 => null,
            Sorte.Entier when n < 0 => null,
            Sorte.Sens when n is not (0 or 1) => null,
            _ => n.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static string Refus(string cle, string valeur) => SorteDe(cle) switch
    {
        Sorte.Case => $"« {cle} » est une case de RIG v2 : True ou False, pas « {valeur} ».",
        Sorte.Borne => $"« {cle} » : un nombre entier de 1000 à 10000 (bornes de RIG v2), pas « {valeur} ».",
        Sorte.Sens => $"« {cle} » : 0 (ascendant) ou 1 (descendant), pas « {valeur} ».",
        _ => $"« {cle} » : un nombre entier positif, pas « {valeur} ».",
    };
}
