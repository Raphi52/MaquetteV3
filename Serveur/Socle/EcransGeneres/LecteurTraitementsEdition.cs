using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Relancer un fichier » (PROC_RELANCEFILE) : les GROUPES (traitements d'edition, TRAITEMENT_EDITION) des files d'edition de la
/// chaine d'impression « lot 1 » (FILE_EDITION -> TRAITEMENT_EDITION -> LIGNE_EDITION). Une FILE. Mode d'emploi : LecteurEcran.cs.
/// Plan « RIG v3 100 % fonctionnel », lot 9, 26/11/2026.
///
/// SOURCE (ancien RIG) : proc_relancefile.md (Detail OPE, qui fait foi) et Source\Outils\DLL\RigMetier\Edition\ :
///   - les files : FileEdition.GetAllFileEdition, « order by FLEDT_NUM_ORDRE » (FileEdition.cs:132-140), sauf les files Amitel pour
///     un agent du greffe (OPE_RECHERCHELOT.cs:38-40 ; IsFileAmitel = FLEDT_NOM_RIG commence par « AMI », FileEdition.cs:68-72) ;
///   - les groupes d'une file : TraitementEdition.GetTraitementEditionFromFileEdition (TraitementEdition.cs:167-187), « TRTED_DATE_CLOTURE
///     is NULL or between debut and fin », tries « TRTED_FIN_TRANSMISSION desc, TRTED_ID_TRTED » ; le filtre par etat est commente
///     dans RIG v2 (OPE_RECHERCHE.cs:64-83) ;
///   - un groupe clos et vide n'est pas affiche (NombreEditions == 0 && IsCloture, OPE_LISTVIEWGRP.cs:152-155) ; ses editions : les
///     LIGNE_EDITION de ses destinataires (INFO_DESTINATAIRE.INFDS_ID_TRTED) et de leur demande (SelectLigneEdition, :334-382) ;
///     clos = date de cloture posee ET TRTED_CLOTURE (IsCloture, :487-490).
/// GROUPE : « Groupes ouverts » / « Groupes clôturés ». ETAT -- les boutons de RIG v2 (OPE_EDITGRP._enabledButtons, :186-207) :
/// un groupe ouvert se cloture ou se relance (« Ouvert », AFaire) ; un groupe clos se reimprime : « Clôturé, en transmission »
/// (EnCours) tant que TRTED_FIN_TRANSMISSION n'est pas pose, « Transmis » (Validee) ensuite.
///
/// CE QUE LA V3 NE REPREND PAS (nomme) : la plage de dates (aujourd'hui 7 h -> maintenant, OPE_RECHERCHE.cs:128-143), la file
/// choisie et le numero de groupe -- la forme File ne transmet aucun critere : RIG v2 dates effacees (plage ouverte, MinValue /
/// MaxValue, :22-56), toutes files ; les gestes, sans ecrivain : « Relancer via editrcs » lance un executable externe sans
/// parametre (_relancer, :237-240) ; « Cloturer le groupe » (TraitementEdition.Cloturer, :224-254 : comptes des editions non
/// editees, fin de transmission, ouverture du groupe suivant de la file) et « Tout réimprimer » (_basculerEtatP, :254-392 :
/// generation des documents en batch puis remise en file des lignes) ne sont pas encore ecrits par la V3.
/// </summary>
internal sealed class LecteurTraitementsEdition : ILecteurEcran
{
    public string Procedure => "PROC_RELANCEFILE";

    /// <summary>Les groupes du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string Ouverts = "Groupes ouverts", Clotures = "Groupes clôturés";

    private const string Requete = """
        SELECT TOP (@plafond) t.TRTED_ID_TRTED, ISNULL(NULLIF(LTRIM(RTRIM(f.FLEDT_NOM_RIG)), ''), f.FLEDT_LIBELLE) AS NomFile,
               t.TRTED_DATE_OUVERTURE, t.TRTED_DATE_CLOTURE, t.TRTED_IMPRIMANTE1, n.NombreEditions,
               CASE WHEN t.TRTED_DATE_CLOTURE IS NOT NULL AND t.TRTED_CLOTURE = 1 THEN 1 ELSE 0 END AS Clos,
               ISNULL(t.TRTED_FIN_TRANSMISSION, 0) AS FinTransmission, COUNT(*) OVER () AS Total
        FROM v2.TRAITEMENT_EDITION AS t
        JOIN v2.FILE_EDITION AS f ON f.FLEDT_ID_FLEDT = t.TRTED_ID_FLEDT
        CROSS APPLY (SELECT COUNT(*) AS NombreEditions FROM v2.DEMANDE AS d
                     JOIN v2.LIGNE_EDITION AS l ON l.LGEDT_ID_DMND = d.DMND_ID_DMND
                     JOIN v2.INFO_DESTINATAIRE AS i ON i.INFDS_ID_INFDS = l.LGEDT_ID_INFDS
                     WHERE i.INFDS_ID_TRTED = t.TRTED_ID_TRTED) AS n
        WHERE UPPER(ISNULL(f.FLEDT_NOM_RIG, '')) NOT LIKE 'AMI%'
          AND NOT (n.NombreEditions = 0 AND t.TRTED_DATE_CLOTURE IS NOT NULL AND t.TRTED_CLOTURE = 1)
        ORDER BY f.FLEDT_NUM_ORDRE, f.FLEDT_ID_FLEDT, t.TRTED_FIN_TRANSMISSION DESC, t.TRTED_ID_TRTED
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var clos = Convert.ToInt32(r["Clos"], System.Globalization.CultureInfo.InvariantCulture) == 1;
        var transmis = r["FinTransmission"] is true;
        var editions = Convert.ToInt32(r["NombreEditions"], System.Globalization.CultureInfo.InvariantCulture);
        var id = Convert.ToInt32(r["TRTED_ID_TRTED"], System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ouverture = FormeLigne.DateDe(r, "TRTED_DATE_OUVERTURE");
        var cloture = FormeLigne.DateDe(r, "TRTED_DATE_CLOTURE");
        var precisions = FormeLigne.Joindre(editions > 1 ? $"{editions} éditions" : $"{editions} édition",
                                            ouverture is null ? null : $"ouvert le {FormeLigne.Date(ouverture)}",
                                            clos ? $"clôturé le {FormeLigne.Date(cloture)}" : null,
                                            clos ? (transmis ? "transmission finie" : "en transmission") : null,
                                            FormeLigne.Texte(r, "TRTED_IMPRIMANTE1"));
        var etat = !clos ? "AFaire" : transmis ? "Validee" : "EnCours";
        return new LigneLue($"File {FormeLigne.Texte(r, "NomFile")} · groupe {id}", precisions, clos ? Clotures : Ouverts, etat, []) { Id = id };
    }
}
