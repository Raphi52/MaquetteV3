namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// LES DOCUMENTS DE LA GED DU GREFFE (table GED_RIG de RIG v2, recopiee telle quelle en v2.GED_RIG), tels que les lisent
/// « Documents » (LecteurDocumentsGed), « Voir un PDF » (LecteurPiecesPdf) et « Voir une image » (LecteurImagesGed). Plan
/// « RIG v3 100 % fonctionnel », lot 6, 26/11/2026.
///
/// SOURCE : PROC_RECHERCHE_GED, OPE_GED._initListviewTree (D:\RigApplication\Source\RIG\DLL\Processus\PROC_RECHERCHE_GED\
/// Etapes\OPE_GED.cs:48-72) : chaque categorie de l'arbre est une jointure de GED_RIG vers la table qui porte le document, et
/// son « Orphelin » est le document qui DECLARE cette table dans GDRG_REFERENCE (« MAQUETTE.MQTT_NUM_DOCUMENT »...) sans
/// qu'elle le porte encore (proc_recherche_ged.md:305-312). La V3 range chaque document dans UNE categorie, celle que nomme sa
/// reference : la table nommee le porte -> sa categorie ; elle ne le porte plus -> « Orphelins » ; aucune reference -> la racine
/// « \ » de la V2, « Sans référence » ; une reference vers une autre table -> « Autres documents ». Le libelle (la colonne NOM
/// de la V2) est celui de la categorie : code de maquette, reference de l'element, libelle de l'edition, role et type de
/// decision, numero de facture, numero de liaison SIREN.
///
/// LE FICHIER : GED_RIG porte le document lui-meme (GDRG_FICHIER, l'archive ZIP que GedRig.GetFile decompresse,
/// RigMetier\Divers\GedRig.cs:600-660 ; et sa version PDF, GDRG_FICHIER_PDF), pas un chemin. Dans la copie de developpement ces
/// octets sont un document FICTIF de meme format et de meme taille (Outils\Anonymisation\Documents.cs, SOC-021).
/// </summary>
internal static class DocumentsGed
{
    /// <summary>Les categories, au mot pres celles du descripteur de « Documents » (Trouver.cs), dans l'ordre de l'arbre V2.</summary>
    internal const string SansReference = "Sans référence", Elements = "Éléments de fusion", Maquettes = "Maquettes",
                          Editions = "Documents d'édition", Reda = "Documents REDA", Factures = "Factures", Siren = "Documents SIREN",
                          Orphelins = "Orphelins", Autres = "Autres documents";

    internal static readonly string[] Categories = [SansReference, Elements, Maquettes, Editions, Reda, Factures, Siren, Orphelins, Autres];

    // La categorie (le rang dans Categories) et le libelle de chaque document, par sa reference (OPE_GED.cs:49-71). Le
    // document est « g ».
    internal const string Categorie = """
        CASE WHEN g.GDRG_REFERENCE IS NULL THEN 0
             WHEN g.GDRG_REFERENCE = 'ELEMENT_FUSION.ELMFS_NUM_DOCUMENT'
                  THEN CASE WHEN EXISTS (SELECT 1 FROM v2.ELEMENT_FUSION AS x WHERE x.ELMFS_NUM_DOCUMENT = g.GDRG_NUM_DOCUMENT) THEN 1 ELSE 7 END
             WHEN g.GDRG_REFERENCE = 'MAQUETTE.MQTT_NUM_DOCUMENT'
                  THEN CASE WHEN EXISTS (SELECT 1 FROM v2.MAQUETTE AS x WHERE x.MQTT_NUM_DOCUMENT = g.GDRG_NUM_DOCUMENT) THEN 2 ELSE 7 END
             WHEN g.GDRG_REFERENCE = 'DEMANDE_EDITION.DMNED_NUM_DOCUMENT_EDITION'
                  THEN CASE WHEN EXISTS (SELECT 1 FROM v2.DEMANDE_EDITION AS x WHERE x.DMNED_NUM_DOCUMENT_EDITION = g.GDRG_NUM_DOCUMENT) THEN 3 ELSE 7 END
             WHEN g.GDRG_REFERENCE = 'DEMANDE_EDITION.DMNED_NUM_DOCUMENT_FLUX'
                  THEN CASE WHEN EXISTS (SELECT 1 FROM v2.DEMANDE_EDITION AS x WHERE x.DMNED_NUM_DOCUMENT_FLUX = g.GDRG_NUM_DOCUMENT) THEN 3 ELSE 7 END
             WHEN g.GDRG_REFERENCE IN ('DOCUMENT_DOSSIER.DCMDS_NUM_DOCUMENT_0', 'DOCUMENT_DOSSIER.DCMDS_NUM_DOCUMENT_1', 'DOCUMENT_DOSSIER.DCMDS_NUM_DOCUMENT_2')
                  THEN CASE WHEN EXISTS (SELECT 1 FROM v2.DOCUMENT_DOSSIER AS x WHERE g.GDRG_NUM_DOCUMENT IN (x.DCMDS_NUM_DOCUMENT_0, x.DCMDS_NUM_DOCUMENT_1, x.DCMDS_NUM_DOCUMENT_2)) THEN 4 ELSE 7 END
             WHEN g.GDRG_REFERENCE = 'FACTURE.FACTU_NUMERO_GED_DONNEES_XML'
                  THEN CASE WHEN EXISTS (SELECT 1 FROM v2.FACTURE AS x WHERE x.FACTU_NUMERO_GED_DONNEES_XML = g.GDRG_NUM_DOCUMENT) THEN 5 ELSE 7 END
             WHEN g.GDRG_REFERENCE IN ('SIREN_EN_LIGNE.SIRLG_GUID_XML_ENVOYE', 'SIREN_EN_LIGNE.SIRLG_GUID_XML_RECU')
                  THEN CASE WHEN EXISTS (SELECT 1 FROM v2.SIREN_EN_LIGNE AS x WHERE g.GDRG_NUM_DOCUMENT IN (x.SIRLG_GUID_XML_ENVOYE, x.SIRLG_GUID_XML_RECU)) THEN 6 ELSE 7 END
             ELSE 8 END
        """;

    internal const string Libelle = """
        COALESCE(
            (SELECT TOP (1) x.ELMFS_REFERENCE FROM v2.ELEMENT_FUSION AS x WHERE x.ELMFS_NUM_DOCUMENT = g.GDRG_NUM_DOCUMENT),
            (SELECT TOP (1) CONCAT(x.MQTT_CODE_MAQUETTE, ' (', x.MQTT_TYPE_MAQUETTE, ')') FROM v2.MAQUETTE AS x WHERE x.MQTT_NUM_DOCUMENT = g.GDRG_NUM_DOCUMENT),
            (SELECT TOP (1) x.DMNED_LIBELLE_DOCUMENT FROM v2.DEMANDE_EDITION AS x
             WHERE g.GDRG_NUM_DOCUMENT IN (x.DMNED_NUM_DOCUMENT_EDITION, x.DMNED_NUM_DOCUMENT_FLUX) ORDER BY x.DMNED_ID_DMNED),
            (SELECT TOP (1) LTRIM(RTRIM(CONCAT(i.INSTN_NUM_ROLE, ' ', d.DCJDC_TYPE_DECISION))) FROM v2.DOCUMENT_DOSSIER AS x
             JOIN v2.DECISION_JUDICIAIRE AS d ON d.DCJDC_ID_DCJDC = x.DCMDS_VALEUR_ID_METIER
             JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
             WHERE g.GDRG_NUM_DOCUMENT IN (x.DCMDS_NUM_DOCUMENT_0, x.DCMDS_NUM_DOCUMENT_1, x.DCMDS_NUM_DOCUMENT_2)),
            (SELECT TOP (1) CONCAT('Facture ', x.FACTU_NUMERO_FACTURE) FROM v2.FACTURE AS x WHERE x.FACTU_NUMERO_GED_DONNEES_XML = g.GDRG_NUM_DOCUMENT),
            (SELECT TOP (1) CONCAT('Liaison ', x.SIRLG_NUM_LIAISON) FROM v2.SIREN_EN_LIGNE AS x
             WHERE g.GDRG_NUM_DOCUMENT IN (x.SIRLG_GUID_XML_ENVOYE, x.SIRLG_GUID_XML_RECU)),
            '')
        """;

    /// <summary>Le nom affiche d'un document : son libelle, sinon son nom de fichier, sinon son numero.</summary>
    internal static string Nom(string libelle, string fichier, string numero)
        => libelle.Length > 0 ? libelle : fichier.Length > 0 ? fichier : $"Document {numero}";

    /// <summary>« 1,2 Mo », « 35 Ko », « 812 octets ».</summary>
    internal static string Taille(long? octets) => octets switch
    {
        null => "",
        >= 1024 * 1024 => $"{octets.Value / 1024d / 1024d:0.#} Mo".Replace('.', ','),
        >= 1024 => $"{octets.Value / 1024} Ko",
        _ => $"{octets} octets",
    };
}
