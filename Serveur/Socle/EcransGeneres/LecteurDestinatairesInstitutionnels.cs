using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Destinataires institutionnels » (PROC_GDEST_INST) : le parquet, les administrations et les organismes a qui le
/// greffe adresse ses courriers et ses envois dematerialises. Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\Judiciaire\Exe\PROC_GDEST_INST\frmListeDestInst.cs. A l'ouverture
/// (l. 46), Rechercher (l. 77-138) lit « Select * From DESTINATAIRE_INSTITUTIONNEL » sans critere (type, code et nom
/// vides), sur la base du greffe, dans l'ordre de sa cle (DTINS_CODE, sans ORDER BY) ; un destinataire inactif
/// (DTINS_ACTIF = 0) est grise (l. 132-136). Le type DTINS_TYPE est le code d'une ENTITE « INST_<type> », dont le libelle
/// est ENTT_LIBELLE (l. 59). Aucun filtre ici non plus.
///
/// GROUPES : le type du destinataire, range par ce qu'il est. Les types sont ceux de l'ENTITE INST_* (RIG_DEV, lu le
/// 25/11/2026) :
///   « Parquet et juridictions » : PROC (Procureur), TGI (Tribunal de grande instance), CJ (Casier judiciaire) ;
///   « Administrations »         : DDFP (Directeur departemental des finances), RDI (Recette divisionnaire des impots),
///                                 TPG (Tresor public general), PREFI (Prefecture), HDD (Hotel du departement), HDP (Hotel
///                                 de police), DESTPR (destinataires de l'enquete de prevention : impots, Urssaf, MSA...) ;
///   « Organismes »              : tout autre type -- BARR (Barreau), AJMJ, H (Huissier), BOD (Bodacc), CC (Chambre de
///                                 commerce), JAL (journal d'annonces legales), RCS, RM, et un type cree par le greffe
///                                 (frmAddType).
///
/// REGLE D'ETAT (marches du descripteur : 0 Identite, 1 Courrier, 2 Courriel et coffre-fort, 3 Nouveau type) :
///   - Identite (0) : code, nom, type ET adresse renseignes -- frmAddDestInst.cs:253-257 refuse d'enregistrer sans eux
///     (« Le code, le nom, l'adresse et le type sont obligatoires ») ; une ligne ancienne peut ne pas avoir d'adresse ;
///   - Courrier (1) : une civilite developpee ou une formule de politesse (DTINS_CIVILITE_DEVELOPPEE,
///     DTINS_FORMULE_POLITESSE) ;
///   - Courriel et coffre-fort (2) : une adresse courriel ou un coffre-fort (DTINS_ADRESSE_MAIL, DTINS_NOM_COFFRE) ;
///   - Nouveau type (3) : la creation d'un type (frmAddType), pas une donnee du destinataire : jamais franchie.
///   DTINS_ACTIF = 0                    -> EnAttente « Inactif » (marches franchies telles quelles) ;
///   identite complete                  -> Validee « Enregistre » (seule marche obligatoire : 0) ;
///   identite incomplete, autre marche  -> EnCours « En saisie » ;
///   identite incomplete, rien d'autre  -> AFaire « A completer ».
/// </summary>
internal sealed class LecteurDestinatairesInstitutionnels : ILecteurEcran
{
    public string Procedure => "PROC_GDEST_INST";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Justice = "Parquet et juridictions", Administrations = "Administrations", Organismes = "Organismes";

    private static readonly HashSet<string> TypesJustice = new(StringComparer.OrdinalIgnoreCase) { "PROC", "TGI", "CJ" };
    private static readonly HashSet<string> TypesAdministrations =
        new(StringComparer.OrdinalIgnoreCase) { "DDFP", "RDI", "TPG", "PREFI", "HDD", "HDP", "DESTPR" };

    private const string Requete = """
        SELECT TOP (@plafond) d.DTINS_CODE, d.DTINS_LIBELLE, d.DTINS_TYPE, d.DTINS_BLOC_ADRESSE, d.DTINS_CIVILITE_DEVELOPPEE,
               d.DTINS_FORMULE_POLITESSE, d.DTINS_ADRESSE_MAIL, d.DTINS_NOM_COFFRE, d.DTINS_DATE_DEMARRAGE_COFFRE,
               d.DTINS_ACTIF, t.ENTT_LIBELLE, COUNT(*) OVER () AS Total
        FROM v2.DESTINATAIRE_INSTITUTIONNEL AS d
        LEFT JOIN v2.ENTITE AS t ON t.ENTT_CODE_ENTITE = 'INST_' + d.DTINS_TYPE
        ORDER BY d.DTINS_CODE
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var code = FormeLigne.Texte(r, "DTINS_CODE");
        var nom = FormeLigne.Texte(r, "DTINS_LIBELLE");
        var type = FormeLigne.Texte(r, "DTINS_TYPE");
        var adresse = FormeLigne.Texte(r, "DTINS_BLOC_ADRESSE");
        var coffre = FormeLigne.Texte(r, "DTINS_NOM_COFFRE");
        var depuis = FormeLigne.Date(FormeLigne.DateDe(r, "DTINS_DATE_DEMARRAGE_COFFRE"));
        var courriel = FormeLigne.Texte(r, "DTINS_ADRESSE_MAIL");

        // La regle d'etat (en tete du fichier).
        var identite = code.Length > 0 && nom.Length > 0 && type.Length > 0 && adresse.Length > 0;
        var courrier = FormeLigne.Texte(r, "DTINS_CIVILITE_DEVELOPPEE").Length > 0 || FormeLigne.Texte(r, "DTINS_FORMULE_POLITESSE").Length > 0;
        var envois = courriel.Length > 0 || coffre.Length > 0;
        int[] faites = [.. new[] { (identite, 0), (courrier, 1), (envois, 2) }.Where(m => m.Item1).Select(m => m.Item2)];
        var etat = !Convert.ToBoolean(r["DTINS_ACTIF"]) ? "EnAttente"
            : identite ? "Validee"
            : faites.Length > 0 ? "EnCours"
            : "AFaire";
        var groupe = TypesJustice.Contains(type) ? Justice : TypesAdministrations.Contains(type) ? Administrations : Organismes;

        var libelleType = FormeLigne.Texte(r, "ENTT_LIBELLE");
        var precisions = FormeLigne.Joindre(
            code,
            libelleType.Length > 0 ? libelleType : type,
            adresse.Length == 0 ? "adresse à compléter" : null,
            courriel.Length > 0 ? "courriel" : null,
            coffre.Length > 0 ? (depuis.Length > 0 ? $"coffre-fort {coffre} depuis le {depuis}" : $"coffre-fort {coffre}") : null);
        return new LigneLue(nom.Length > 0 ? nom : code, precisions, groupe, etat, faites);
    }
}
