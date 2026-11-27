namespace RigV3.Contrats.EcransMain;

// « Edition des maquettes » (PROC_EDIT_MAQUETTE, web/src/ecrans/EcranMaquettesEdition.tsx), écran fait main lu dans la base du greffe
// (plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureMaquettesEdition.cs.

/// <summary>L'adresse de la lecture : GET, les maquettes du greffe et leurs paramètres d'édition (<see cref="MaquettesLues"/>).</summary>
public static class RoutesMaquettesEdition
{
    public const string Maquettes = "/ecrans-main/{greffe}/maquettes-edition";
}

/// <summary>
/// Une table liée à un paramètre d'édition, comme la liste « Tables liées » de RIG v2 (TableLiee.GetTableLiee,
/// PROC_EDIT_MAQUETTE\Classe\TableLiee.cs:94-111) : son code, son libellé et le TYPE de la table qui la porte (« CODE EVE PRO »,
/// « TYPE DECISION », « VIDIGED PARAMETRAGE », « ULT DE PROCESSUS », « COURRIER JUDICIAIRE »).
/// </summary>
public sealed record TableLieeLue(string Code, string Libelle, string Type);

/// <summary>
/// Un paramètre d'édition de RIG v2 (une ligne de PARAMETRE_EDITION), avec les champs que montre OPE_PARAM_EDT_MAJ : code et libellé,
/// code processus, nature, imprimante, papier, les quatre lots, exemplaires, cases (recto-verso, agrafage, afficher avant impression,
/// lecture seule, impression, garder l'édition en lot 1), type de génération et requête SQL ; puis ses tables liées.
/// </summary>
public sealed record ParametreEditionLu(string Code, string Libelle, string CodeProcessus, string NatureEdition, string Imprimante,
    string TypePapier, string Lot, string LotLrar, string LotIlrar, string LotSubstitution, int? NombreExemplaires, bool RectoVerso,
    bool Agrafage, bool AfficherAvantImpression, bool LectureSeule, bool Impression, bool GarderEditionLot1, string TypeGeneration,
    string RequeteSql, TableLieeLue[] TablesLiees);

/// <summary>
/// Une maquette de RIG v2 (une ligne de MAQUETTE) : les colonnes de la liste d'OPE_MAQUETTE (code, libellé, type :
/// OPE_MAQUETTE.cs:692-694), le format de son document (MQTT_EXTENSION), l'entête au format plieuse, et ses paramètres d'édition
/// (Maquette.LstParametreEdition, PRMED_CODE_MAQUETTE = son code).
/// </summary>
public sealed record MaquetteLue(string Code, string Libelle, string Type, string Extension, bool EnteteFormatPlieuse,
    ParametreEditionLu[] Parametres);

/// <summary>Les maquettes lues, dans l'ordre du code, et leur total.</summary>
public sealed record MaquettesLues(MaquetteLue[] Lignes, int Total);
