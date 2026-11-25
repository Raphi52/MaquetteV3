using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Mandataires » (PROC_MANDATAIRE) : les mandataires et praticiens du greffe (avocats, administrateurs, huissiers,
/// societes), la table MANDATAIRE de la base du greffe. Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : PROC_MANDATAIRE (D:\RigApplication\Source\RIG\DLL\Processus\PROC_Mandataire) n'edite qu'UN
/// mandataire, ouvert par son id ou en creation (fiche proc_mandataire.md, « DoInitObject ») ; la liste d'ou on l'ouvre est
/// la recherche des mandataires, D:\RigApplication\Source\RIG\DLL\RigOcxRecherche\FormRecherche_MANDATAIRE.cs:155-212 :
/// « SELECT TOP n ... From MANDATAIRE Where 1=1 [criteres] ORDER BY ordreSelect asc, MNDTR_NOM asc », ou ordreSelect met
/// les actifs (MNDTR_ACTIF = 1) avant les inactifs. Ses criteres (nom, CNBF, CNBF de structure, SIREN) sont ceux de la
/// recherche de l'ecran V3 ; la V2 en exige un (l. 159-160), l'ecran V3 ouvre sans : tous les mandataires, sans filtre, dans
/// l'ordre de la V2 (puis l'id, pour un ordre stable entre homonymes). Un inactif reste liste, apres les actifs : ses
/// precisions le disent. (La V2 grise et refuse au choix les lignes ACTIVES, l. 289-321 : test inverse, non repris.)
///
/// NOM : celui de la V2, Mandataire.NomComplet (Outils\DLL\RigMetier\JUDICIAIRE\Mandataire.cs:42) = Outils.FormerNomComplet
/// (Outils\DLL\RigMetier\JUDICIAIRE\Fonctions diverses\Outils.cs:663-669) : l'abrege d'edition de la civilite
/// (CIVILITE.R_CIVILITE_ABREGE_EDIT : « Me », « M. », « Mme ») puis le nom, sauf pour PM et ND (le nom seul).
///
/// GROUPES : MNDTR_CIVILITE = 'PM' -> « Personne morale » (la V2 pose la civilite PM a une personne morale :
/// OPE_SAISIE_MANDATAIRE.cs:212-222, et CIVILITE.R_CIVILITE_TOUJOURS_PP = 0 pour elle seule, RIG_DEV lu le 25/11/2026) ;
/// toute autre civilite -> « Personne physique », y compris ND (« civilite non determinee », 21 384 des 83 039 mandataires de
/// RIG_DEV), que ses precisions nomment.
///
/// REGLE D'ETAT (marches du descripteur : 0 Identite, 1 Adresse et contacts, 2 Abrege, 3 Client de facturation,
/// 4 Qualification ; seule l'identite est obligatoire) :
///   - Identite (0) : civilite ET designation (MNDTR_CIVILITE, MNDTR_NOM) renseignees -- les deux champs obligatoires de la
///     fiche (proc_mandataire.md, « Controles reels ») ;
///   - Adresse et contacts (1) : une adresse (MNDTR_ID_ADRBS), un telephone, un fax ou un courriel ;
///   - Abrege (2) : un abrege (MNDTR_ABREGE) : le mandataire est « repertorie » (OPE_SAISIE_MANDATAIRE.cs:124) ;
///   - Client de facturation (3) : MNDTR_ID_CLIEN renseigne et different de -1 (la V2 lit -1 comme « aucun client »,
///     OPE_SAISIE_MANDATAIRE.cs:98) ;
///   - Qualification (4) : une qualification de la profession ou des infos complementaires (MNDTR_QUALIF_PROFESSION,
///     MNDTR_TEXTE).
///   identite complete                  -> Validee « Enregistre » ;
///   identite incomplete, autre marche  -> EnCours « En saisie » ;
///   identite incomplete, rien d'autre  -> AFaire « A completer ».
/// </summary>
internal sealed class LecteurMandataires : ILecteurEcran
{
    public string Procedure => "PROC_MANDATAIRE";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string PersonnePhysique = "Personne physique", PersonneMorale = "Personne morale";

    private const string Requete = """
        SELECT TOP (@plafond) m.MNDTR_ID_MNDTR, m.MNDTR_CIVILITE, m.MNDTR_NOM, m.MNDTR_ABREGE, m.MNDTR_SIREN, m.MNDTR_NUMERO_CNBF,
               m.MNDTR_ID_ADRBS, m.MNDTR_TELEPHONE, m.MNDTR_FAX, m.MNDTR_ADRESSE_MAIL, m.MNDTR_ID_CLIEN,
               m.MNDTR_QUALIF_PROFESSION, m.MNDTR_TEXTE, m.MNDTR_ACTIF, c.R_CIVILITE_ABREGE_EDIT,
               COUNT(*) OVER () AS Total
        FROM v2.MANDATAIRE AS m
        LEFT JOIN v2.CIVILITE AS c ON c.R_CIVILITE_VALEUR = m.MNDTR_CIVILITE
        ORDER BY CASE WHEN m.MNDTR_ACTIF = 1 THEN 0 ELSE 1 END, m.MNDTR_NOM, m.MNDTR_ID_MNDTR
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var civilite = FormeLigne.Texte(r, "MNDTR_CIVILITE");
        var nom = FormeLigne.Texte(r, "MNDTR_NOM");
        var abrege = FormeLigne.Texte(r, "MNDTR_ABREGE");
        var siren = FormeLigne.Texte(r, "MNDTR_SIREN");
        var cnbf = FormeLigne.Texte(r, "MNDTR_NUMERO_CNBF");
        var actif = Convert.ToBoolean(r["MNDTR_ACTIF"]);
        var morale = civilite.Equals("PM", StringComparison.OrdinalIgnoreCase);
        var nonDeterminee = civilite.Equals("ND", StringComparison.OrdinalIgnoreCase);

        // La regle d'etat (en tete du fichier).
        var identite = civilite.Length > 0 && nom.Length > 0;
        var contacts = r["MNDTR_ID_ADRBS"] is not DBNull || FormeLigne.Texte(r, "MNDTR_TELEPHONE").Length > 0
                       || FormeLigne.Texte(r, "MNDTR_FAX").Length > 0 || FormeLigne.Texte(r, "MNDTR_ADRESSE_MAIL").Length > 0;
        var client = r["MNDTR_ID_CLIEN"] is int idClient && idClient != -1;
        var qualification = FormeLigne.Texte(r, "MNDTR_QUALIF_PROFESSION").Length > 0 || FormeLigne.Texte(r, "MNDTR_TEXTE").Length > 0;
        int[] faites = [.. new[] { (identite, 0), (contacts, 1), (abrege.Length > 0, 2), (client, 3), (qualification, 4) }
            .Where(m => m.Item1).Select(m => m.Item2)];
        var etat = identite ? "Validee" : faites.Length > 0 ? "EnCours" : "AFaire";

        // Outils.FormerNomComplet : l'abrege d'edition de la civilite puis le nom, sauf PM et ND.
        var nomComplet = morale || nonDeterminee ? nom : $"{FormeLigne.Texte(r, "R_CIVILITE_ABREGE_EDIT")} {nom}".Trim();
        var affiche = nom.Length > 0 ? nomComplet : abrege.Length > 0 ? abrege : $"Mandataire {FormeLigne.Texte(r, "MNDTR_ID_MNDTR")}";
        var precisions = FormeLigne.Joindre(
            civilite.Length == 0 ? "civilité à saisir" : nonDeterminee ? "civilité non déterminée" : null,
            nom.Length == 0 ? "désignation à saisir" : null,
            abrege.Length > 0 ? $"abrégé {abrege}" : "non répertorié",
            siren.Length > 0 ? $"SIREN {FormeLigne.Siren(siren)}" : null,
            cnbf.Length > 0 ? $"CNBF {cnbf}" : null,
            actif ? null : "inactif");
        return new LigneLue(affiche, precisions, morale ? PersonneMorale : PersonnePhysique, etat, faites);
    }
}
