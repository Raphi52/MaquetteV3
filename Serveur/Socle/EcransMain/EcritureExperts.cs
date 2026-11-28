using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Experts » (PROC_EXPERT, écran fait main web/src/ecrans/EcranExperts.tsx) : le geste ENREGISTRER LA FICHE d'un expert, le « Valider »
/// de la fiche OPE_EDITION_EXPERT de RIG v2, écrit dans la base du greffe. Joué par la route POST RoutesFicheExpert.Enregistrer
/// (ModuleExperts.cs) dans la transaction et le journal des gestes du socle (EcrivainsEcrans.JouerAsync). Plan « RIG v3 complet, mesuré par
/// le banc du parcours complet », lot 9, 28/11/2026 ; la lecture : LectureExperts.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RIG\DLL\Processus\PROC_EXPERT\Edition) :
///   - OPE_EDITION_EXPERT.cs:117-167 (Valider) : Verifier, puis dans UNE transaction les participants cochés, puis l'expert (Expert.Sauve,
///     Outils\DLL\RigMetier\JUDICIAIRE\Expert.cs:354-366 : les champs liés, rien d'autre en modification -- EXPRT_ETAT_INFO et
///     EXPRT_DATE_MAJ ne s'écrivent qu'à la création, :139-143) ;
///   - les champs liés (_LinkDonnees, :254-265) ; les OBLIGATOIRES (OPE_EDITION_EXPERT.Designer.cs:95, 104, 208) : abrégé, nom, civilité,
///     message « Champ obligatoire » et le titre du champ (RigAutomate\ULT\Ult.moteur.cs:14, :516-517) ; PROC_EXPERT ne contrôle rien
///     d'autre (son DoVerifier est en commentaire, :101-102 : pas de contrôle d'abrégé unique ici) ;
///   - la civilité et la qualification se choisissent dans leurs listes (ULT_COMBO_CIVILITE -> CIVILITE, ULT_COMBO_QUALIF_PROFESSION ->
///     QUALIF_PROFESSION, Rig_Ult_Metier\COMBO\RCS) : une valeur hors liste est refusée ;
///   - le téléphone et le fax (ULT_TEXT_TELEPHONE : RigTools\TOOLS\RegEx.cs:33, « Numéro de téléphone invalide ») et le courriel
///     (ULT_TEXT_MAIL : RegEx.Email, :17-19, « Courriel invalide ») : contrôles bloquants de Ult_Text_Internal.DoVerifier (:342-362) ;
///   - LA CASCADE (_GestionCascadeUpdate, :180-205) : chaque participant COCHÉ dans les listes de mesures de l'expert reprend sa civilité,
///     son nom, son complément, son courriel, sa qualification et son adresse (PRTCP_ID_ADRBS) ; un participant d'un autre expert n'est pas
///     dans ces listes (Participant.GetListeParticipantJoinMesureByExpert) : refusé.
/// ÉCART NOMMÉ : RIG v2 tronque en silence une valeur trop longue (TronqueChaines, Expert.cs:356-358) ; la V3 la refuse avec la longueur
/// de la colonne (RIG_DEV), pour ne pas écrire autre chose que la saisie.
/// NON REPRIS (nommés) : la création d'un expert (bouton Ajouter : la fiche V3 s'ouvre sur un expert existant) ; la DEMANDE « Modification
/// d'un expert : » + l'abrégé (_CreerDemande, :169-178) -- une trace, que le journal des gestes remplace (décision du plan) ; la remise à
/// jour des destinataires d'éditions de la mesure (mesure.destinataireEditionLot2.Sauve, :137), une liste XML des éditions que la V3 ne
/// tient pas ; la date de naissance et l'adresse, que la fiche V3 montre sans les éditer.
/// TRACE : famille « reglage » (un acteur du greffe, comme les référentiels), « a modifie ».
/// </summary>
public static class EcritureExperts
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Experts";

    /// <summary>Les gestes écrits : tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Enregistrer];

    /// <summary>La fiche : ses champs, tels que la base les porte.</summary>
    private sealed record Fiche(string Civilite, string Abrege, string Nom, string ComplementNom, string Telephone, string Fax, string Mail,
                                string Qualification, bool Actif, int? IdAdresse);

    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var cle = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (!int.TryParse(cle, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser($"Expert introuvable dans la base du greffe ({cle}).");
        Fiche avant;
        await using (var r = await geste.LireAsync("""
            SELECT EXPRT_CIVILITE, EXPRT_ABREGE, EXPRT_NOM, EXPRT_COMPLEMENT_NOM, EXPRT_TELEPHONE, EXPRT_FAX, EXPRT_ADRESSE_MAIL,
                   EXPRT_QUALIF_PROFESSION, CAST(EXPRT_EST_ACTIF AS bit) AS Actif, EXPRT_ID_ADRBS
            FROM v2.EXPERT WITH (UPDLOCK) WHERE EXPRT_ID_EXPRT = @id
            """, ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Expert introuvable dans la base du greffe ({cle}).");
            avant = new Fiche(FormeLigne.Texte(r, "EXPRT_CIVILITE"), FormeLigne.Texte(r, "EXPRT_ABREGE"), FormeLigne.Texte(r, "EXPRT_NOM"),
                FormeLigne.Texte(r, "EXPRT_COMPLEMENT_NOM"), FormeLigne.Texte(r, "EXPRT_TELEPHONE"), FormeLigne.Texte(r, "EXPRT_FAX"),
                FormeLigne.Texte(r, "EXPRT_ADRESSE_MAIL"), FormeLigne.Texte(r, "EXPRT_QUALIF_PROFESSION"), r["Actif"] is true,
                r["EXPRT_ID_ADRBS"] is int a ? a : null);
        }

        var actif = avant.Actif;
        if (Valeur(geste, RoutesFicheExpert.ChampActif) is { } texteActif)
        {
            if (ControlesSaisieV2.Oui(texteActif) is not { } b) return IssueGeste.Refuser($"« {RoutesFicheExpert.ChampActif} » : oui ou non ({texteActif}).");
            actif = b;
        }
        var apres = avant with
        {
            Civilite = Valeur(geste, RoutesFicheExpert.ChampCivilite) ?? avant.Civilite,
            Abrege = Valeur(geste, RoutesFicheExpert.ChampAbrege) ?? avant.Abrege,
            Nom = Valeur(geste, RoutesFicheExpert.ChampNom) ?? avant.Nom,
            ComplementNom = Valeur(geste, RoutesFicheExpert.ChampComplementNom) ?? avant.ComplementNom,
            Telephone = Valeur(geste, RoutesFicheExpert.ChampTelephone) ?? avant.Telephone,
            Fax = Valeur(geste, RoutesFicheExpert.ChampFax) ?? avant.Fax,
            Mail = Valeur(geste, RoutesFicheExpert.ChampMail) ?? avant.Mail,
            Qualification = Valeur(geste, RoutesFicheExpert.ChampQualification) ?? avant.Qualification,
            Actif = actif,
        };

        // Verifier : les obligatoires dans l'ordre de la fiche (abrégé, nom, civilité), puis les formats et les listes.
        foreach (var (champ, valeur) in new[] { (RoutesFicheExpert.ChampAbrege, apres.Abrege), (RoutesFicheExpert.ChampNom, apres.Nom),
                                                (RoutesFicheExpert.ChampCivilite, apres.Civilite) })
            if (valeur.Length == 0) return IssueGeste.Refuser($"{ControlesSaisieV2.ChampObligatoire} : {champ}.");
        foreach (var (champ, valeur, taille) in new[] { (RoutesFicheExpert.ChampAbrege, apres.Abrege, 16), (RoutesFicheExpert.ChampNom, apres.Nom, 70),
                     (RoutesFicheExpert.ChampComplementNom, apres.ComplementNom, 70), (RoutesFicheExpert.ChampTelephone, apres.Telephone, 15),
                     (RoutesFicheExpert.ChampFax, apres.Fax, 15), (RoutesFicheExpert.ChampMail, apres.Mail, 70) })
            if (valeur.Length > taille) return IssueGeste.Refuser($"« {champ} » : {valeur.Length} caractères, la colonne en porte {taille}.");
        if (apres.Telephone.Length > 0 && !ControlesSaisieV2.Telephone.IsMatch(apres.Telephone))
            return IssueGeste.Refuser($"« {RoutesFicheExpert.ChampTelephone} » : {ControlesSaisieV2.TelephoneInvalide}.");
        if (apres.Fax.Length > 0 && !ControlesSaisieV2.Telephone.IsMatch(apres.Fax))
            return IssueGeste.Refuser($"« {RoutesFicheExpert.ChampFax} » : {ControlesSaisieV2.TelephoneInvalide}.");
        if (apres.Mail.Length > 0 && !ControlesSaisieV2.Courriel.IsMatch(apres.Mail))
            return IssueGeste.Refuser($"« {RoutesFicheExpert.ChampMail} » : Courriel invalide.");
        if (apres.Civilite != avant.Civilite && !await ControlesSaisieV2.DansLaListeAsync(geste, "v2.CIVILITE", "R_CIVILITE", apres.Civilite, ct))
            return IssueGeste.Refuser($"« {RoutesFicheExpert.ChampCivilite} » : « {apres.Civilite} » n'est pas une civilité de la liste (CIVILITE).");
        if (apres.Qualification != avant.Qualification && apres.Qualification.Length > 0
            && !await ControlesSaisieV2.DansLaListeAsync(geste, "v2.QUALIF_PROFESSION", "R_QUALIFPROF", apres.Qualification, ct))
            return IssueGeste.Refuser($"« {RoutesFicheExpert.ChampQualification} » : « {apres.Qualification} » n'est pas une qualification de la liste (QUALIF_PROFESSION).");

        // Les participants cochés : de cet expert seulement (les listes de ses mesures).
        var participants = (Valeur(geste, RoutesFicheExpert.ChampParticipants) ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToArray();
        var idsParticipants = new List<int>();
        foreach (var p in participants)
        {
            if (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var ip)
                || await ControlesSaisieV2.CompterAsync(geste, "SELECT COUNT(*) FROM v2.PARTICIPANT WHERE PRTCP_ID_PRTCP = @p AND PRTCP_ID_EXPRT = @e", ct,
                       new SqlParameter("@p", ip), new SqlParameter("@e", id)) == 0)
                return IssueGeste.Refuser($"« {RoutesFicheExpert.ChampParticipants} » : le participant {p} n'est pas dans les mesures de cet expert.");
            idsParticipants.Add(ip);
        }

        if (apres == avant && idsParticipants.Count == 0) return IssueGeste.Refuser("Rien à enregistrer : aucune valeur ne change.");

        foreach (var ip in idsParticipants)
            await geste.EcrireAsync("""
                UPDATE v2.PARTICIPANT SET PRTCP_CIVILITE = @civ, PRTCP_NOM = @nom, PRTCP_COMPLEMENT_NOM = @comp, PRTCP_ADRESSE_MAIL = @mail,
                    PRTCP_QUALIF_PROFESSION = @qualif, PRTCP_ID_ADRBS = @adr
                WHERE PRTCP_ID_PRTCP = @p AND PRTCP_ID_EXPRT = @e
                """, ct, new SqlParameter("@civ", apres.Civilite), new SqlParameter("@nom", apres.Nom), ControlesSaisieV2.OuNull("@comp", apres.ComplementNom),
                ControlesSaisieV2.OuNull("@mail", apres.Mail), ControlesSaisieV2.OuNull("@qualif", apres.Qualification),
                new SqlParameter("@adr", System.Data.SqlDbType.Int) { Value = (object?)apres.IdAdresse ?? DBNull.Value },
                new SqlParameter("@p", ip), new SqlParameter("@e", id));
        await geste.EcrireAsync("""
            UPDATE v2.EXPERT SET EXPRT_CIVILITE = @civ, EXPRT_ABREGE = @abr, EXPRT_NOM = @nom, EXPRT_COMPLEMENT_NOM = @comp, EXPRT_TELEPHONE = @tel,
                EXPRT_FAX = @fax, EXPRT_ADRESSE_MAIL = @mail, EXPRT_QUALIF_PROFESSION = @qualif, EXPRT_EST_ACTIF = @actif
            WHERE EXPRT_ID_EXPRT = @id
            """, ct, new SqlParameter("@civ", apres.Civilite), new SqlParameter("@abr", apres.Abrege), new SqlParameter("@nom", apres.Nom),
            ControlesSaisieV2.OuNull("@comp", apres.ComplementNom), ControlesSaisieV2.OuNull("@tel", apres.Telephone), ControlesSaisieV2.OuNull("@fax", apres.Fax),
            ControlesSaisieV2.OuNull("@mail", apres.Mail), ControlesSaisieV2.OuNull("@qualif", apres.Qualification), new SqlParameter("@actif", apres.Actif),
            new SqlParameter("@id", id));

        var quoi = $"l'expert {apres.Abrege}";
        var traces = new List<TraceGeste> { new("reglage", quoi, cle, "a modifie", avant, apres) };
        traces.AddRange(idsParticipants.Select(ip => new TraceGeste("reglage", $"le participant {ip} (expert {apres.Abrege})",
            ip.ToString(CultureInfo.InvariantCulture), "a modifie", null, new { apres.Civilite, apres.Nom, apres.ComplementNom, apres.Mail, apres.Qualification })));
        return IssueGeste.Ecrit($"Expert {apres.Abrege} enregistré" + (idsParticipants.Count == 0 ? "." : $", {idsParticipants.Count} participant(s) mis à jour."),
            [cle], [.. traces]);
    }

    private static string? Valeur(GesteEnCours geste, string cle)
        => geste.Demande.Valeurs.TryGetValue(cle, out var v) ? v?.Trim() ?? "" : null;
}

/// <summary>
/// Les contrôles de saisie de RIG v2 communs aux fiches d'acteurs (« Experts », « Acteurs ») : ceux des champs Ult de l'ancien RIG,
/// cités. Plan « parcours complet », lot 9, 28/11/2026.
/// </summary>
internal static class ControlesSaisieV2
{
    /// <summary>Le message d'un champ obligatoire vide (RigAutomate\ULT\Ult.moteur.cs:14).</summary>
    public const string ChampObligatoire = "Champ obligatoire";

    /// <summary>ULT_TEXT_TELEPHONE (Rig_Ult_Metier\TEXT\ULT_TEXT_TELEPHONE.cs) : son message.</summary>
    public const string TelephoneInvalide = "Numéro de téléphone invalide";

    /// <summary>RigTools\TOOLS\RegEx.cs:33 (TeléphoneFixeOuMobile).</summary>
    public static readonly Regex Telephone = new(@"^((\+|00)33[\s\.]?|0)[1-9]([\s\.]?\d{2}){4}$", RegexOptions.CultureInvariant);

    private const string EmailSimple = @"([a-zA-Z0-9]+(([\.\-_+][a-zA-Z0-9]+)+)?)@(([a-zA-Z0-9]+[\.\-_])+[a-zA-Z]+)";

    /// <summary>RigTools\TOOLS\RegEx.cs:17-19 (Email : simple ou « nom &lt;adresse&gt; »).</summary>
    public static readonly Regex Courriel = new("^" + EmailSimple + "$|^[^;<]*<" + EmailSimple + ">$", RegexOptions.CultureInvariant);

    /// <summary>« oui » / « non » d'une case ; autre chose : null.</summary>
    public static bool? Oui(string texte) => texte.ToLowerInvariant() switch
    {
        "oui" or "true" or "1" => true,
        "non" or "false" or "0" => false,
        _ => null,
    };

    /// <summary>Une valeur vide s'écrit NULL (DBString vide de RIG v2).</summary>
    public static SqlParameter OuNull(string nom, string valeur) => new(nom, valeur.Length == 0 ? DBNull.Value : valeur);

    /// <summary>La valeur est-elle une ligne de la table de référence (colonne &lt;prefixe&gt;_VALEUR) ?</summary>
    public static async Task<bool> DansLaListeAsync(GesteEnCours geste, string table, string prefixe, string valeur, CancellationToken ct)
        => await CompterAsync(geste, $"SELECT COUNT(*) FROM {table} WHERE {prefixe}_VALEUR = @v", ct, new SqlParameter("@v", valeur)) > 0;

    public static async Task<int> CompterAsync(GesteEnCours geste, string sql, CancellationToken ct, params SqlParameter[] p)
    {
        await using var r = await geste.LireAsync(sql, ct, p);
        await r.ReadAsync(ct);
        return r.GetInt32(0);
    }
}
