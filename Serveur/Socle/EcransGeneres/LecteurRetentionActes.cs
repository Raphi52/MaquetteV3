using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Retention d'actes » (PROC_ACTRET) : les depots de comptes annuels CONFIDENTIELS que la V2 retient avant l'envoi a
/// Infogreffe, parce que toutes leurs pieces ne sont pas pretes. Mode d'emploi des lecteurs : LecteurEcran.cs. Plan « RIG v3
/// 100 % fonctionnel », lot 8, 26/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_ACTRET\Metier.cs (fiche proc_actret.md).
///   FROM (l. 91-100) : DEPOT_RCS JOIN ACTION_RCS JOIN DOSSIER_RCS JOIN CATEGORIE_DEPOT (R_CATEGORIED_COMPTES_ANNUEL = 1 : « pour
///   ne pas mettre en retention les actes BEN », l. 96), et les trois pieces du depot par leur abrege : BILA (bilan), CONFI
///   (declaration de confidentialite), RESUL (compte de resultat).
///   WHERE (l. 121-159) : depot non remplace (DPTRC_NUM_DEPOT_REMPLACE IS NULL) ; au moins une piece PRETE (abrege BILA, CONFI ou
///   RESUL, ACTDP_EDI_INFOGREFFE_Etat_GEIDE '1', numerisee -- ACTDP_GUID_VIDIGED --, Image_ATransferer 1) ; et le compte des
///   pieces pretes n'est pas celui qu'attend la confidentialite : 2 (BILA + CONFI) pour un bilan confidentiel
///   (DPTRC_DIFFUSABILITE_CONFIDENTIELLE 1), 3 (BILA + CONFI + RESUL) pour un compte de resultat confidentiel
///   (DPTRC_RESULTAT_CONFIDENTIEL 1). Un depot sans confidentialite n'est jamais retenu.
///   ORDER BY Confidentialite, DPTRC_NUM_DEPOT DESC (l. 109). A l'ouverture, aucun critere (anomalies, numeros : vides).
///   Les libelles des pieces sont les CASE de la V2 au mot pres (l. 15-69) : « Absent(e) du depot », « Non numerise(e) »,
///   « Numerise(e) », suivis de « (marque(e) a ne pas envoyer) » si l'etat GEIDE n'est pas 1 ou si l'image n'est pas a
///   transferer -- deux fois si les deux, comme la V2 ; « NC » pour le compte de resultat d'un depot dont le resultat n'est
///   pas confidentiel.
/// Dans la base V3, ACTION_RCS est rec.ActionRegistre (IdDepot), DOSSIER_RCS rec.DossierRcs (Serveur/Recopie/FichesV3.cs) ;
/// DEPOT_RCS, ACTE_DEPOSE et CATEGORIE_DEPOT sont les tables V2 (v2.*). Ecart assume (le meme que LecteurActesWebNonTransmis) :
/// la V2 joint ACTION_RCS ligne a ligne et montrait un depot a deux actions deux fois ; ici une ligne par depot (sa premiere
/// action qui a un dossier).
///
/// GROUPE : la confidentialite, la premiere colonne de la grille V2 et sa cle de tri (Metier.cs:10-14) : « Bilan »
/// (DPTRC_DIFFUSABILITE_CONFIDENTIELLE 1), sinon « Compte de resultat ».
///
/// REGLE D'ETAT (les trois anomalies que la recherche V2 sait filtrer, OPE_SEARCH : 1 non numerisees, 2 absentes, 3 marquees
/// a ne pas envoyer -- Metier.cs:191-205) :
///   une piece attendue absente du depot ou non numerisee -> AFaire  « Piece manquante » (le depot attend une piece) ;
///   sinon une piece marquee a ne pas envoyer             -> EnCours « Piece a ne pas envoyer » (le greffe l'a ecartee) ;
///   sinon                                                -> Validee « Pieces completes ». La liste V2 n'en montre jamais : un
///   depot dont toutes les pieces attendues sont pretes n'est plus retenu. La tuile reste a 0.
///
/// GESTES : aucun. Le « Renvoyer a Infogreffe » du menu de la V2 ne fait RIEN : le corps de _RenvoyerAInfogreffe est en
/// commentaire (RESULTATS\OPE_RESULTATS.cs:89-97), et l'entree est masquee a l'ouverture du menu (l. 43). « Voir dans VGED »
/// (l. 63-85) ouvre la GED, un autre ecran : rien a ecrire. Pas d'ecrivain, donc ; l'ecran le dit (« Demonstration »).
/// </summary>
internal sealed class LecteurRetentionActes : ILecteurEcran
{
    public string Procedure => "PROC_ACTRET";

    /// <summary>Les groupes du descripteur, au mot pres (Metier.cs:8-9).</summary>
    internal const string Bilan = "Bilan", CompteResultat = "Compte de résultat";

    // Une piece prete (Metier.cs:129-132, 141-144, 153-156).
    private const string Prete = """
        t.ACTDP_ID_DPTRC = p.DPTRC_ID_DPTRC AND t.ACTDP_EDI_INFOGREFFE_Etat_GEIDE = '1' AND t.ACTDP_GUID_VIDIGED IS NOT NULL
        AND t.ACTDP_EDI_INFOGREFFE_Image_ATransferer = 1
        """;

    // Le libelle d'une piece (Metier.cs:15-69), « Absent du depot » / « Non numerise » / « Numerise » + marques.
    private static string Piece(string alias, string absent, string nonNumerise, string numerise, string marque) => $"""
        CASE WHEN {alias}.ACTDP_ID_ACDPS IS NULL THEN '{absent}'
             ELSE CASE WHEN {alias}.ACTDP_GUID_VIDIGED IS NULL THEN '{nonNumerise}' ELSE '{numerise}' END
                  + CASE WHEN {alias}.ACTDP_EDI_INFOGREFFE_Etat_GEIDE <> '1' THEN '  ({marque} à ne pas envoyer)' ELSE '' END
                  + CASE WHEN {alias}.ACTDP_EDI_INFOGREFFE_Image_ATransferer <> 1 THEN '  ({marque} à ne pas envoyer)' ELSE '' END
        END
        """;

    private static readonly string Requete = $"""
        SELECT TOP (@plafond) p.DPTRC_NUM_DEPOT, p.DPTRC_DATE, s.NumeroGestion, e.Denomination,
               CASE WHEN p.DPTRC_DIFFUSABILITE_CONFIDENTIELLE = 1 THEN '{Bilan}' ELSE '{CompteResultat}' END AS Confidentialite,
               {Piece("Confi", "Absente du dépôt", "Non numérisée", "Numérisée", "marquée")} AS DeclaConfi,
               {Piece("Bila", "Absent du dépôt", "Non numérisé", "Numérisé", "marqué")} AS Bilan,
               CASE WHEN p.DPTRC_RESULTAT_CONFIDENTIEL <> 1 THEN 'NC'
                    ELSE {Piece("Resul", "Absent du dépôt", "Non numérisé", "Numérisé", "marqué")} END AS CompteResultat,
               COUNT(*) OVER () AS Total
        FROM v2.DEPOT_RCS AS p
        CROSS APPLY (SELECT TOP (1) d.NumeroGestion, d.IdEntreprise FROM rec.ActionRegistre AS a
                     JOIN rec.DossierRcs AS d ON d.Id = a.IdDossier
                     WHERE a.IdDepot = p.DPTRC_ID_DPTRC ORDER BY a.Id) AS s
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        JOIN v2.CATEGORIE_DEPOT AS c ON c.R_CATEGORIED_VALEUR = p.DPTRC_CATEGORIE_DEPOT AND c.R_CATEGORIED_COMPTES_ANNUEL = 1
        OUTER APPLY (SELECT TOP (1) * FROM v2.ACTE_DEPOSE AS x WHERE x.ACTDP_ID_DPTRC = p.DPTRC_ID_DPTRC AND x.ACTDP_ABREGE_PIECE = 'BILA' ORDER BY x.ACTDP_ID_ACDPS) AS Bila
        OUTER APPLY (SELECT TOP (1) * FROM v2.ACTE_DEPOSE AS x WHERE x.ACTDP_ID_DPTRC = p.DPTRC_ID_DPTRC AND x.ACTDP_ABREGE_PIECE = 'CONFI' ORDER BY x.ACTDP_ID_ACDPS) AS Confi
        OUTER APPLY (SELECT TOP (1) * FROM v2.ACTE_DEPOSE AS x WHERE x.ACTDP_ID_DPTRC = p.DPTRC_ID_DPTRC AND x.ACTDP_ABREGE_PIECE = 'RESUL' ORDER BY x.ACTDP_ID_ACDPS) AS Resul
        WHERE p.DPTRC_NUM_DEPOT_REMPLACE IS NULL
          AND EXISTS (SELECT 1 FROM v2.ACTE_DEPOSE AS t WHERE {Prete} AND t.ACTDP_ABREGE_PIECE IN ('BILA', 'CONFI', 'RESUL'))
          AND ((p.DPTRC_DIFFUSABILITE_CONFIDENTIELLE = 1
                AND (SELECT COUNT(*) FROM v2.ACTE_DEPOSE AS t WHERE {Prete} AND t.ACTDP_ABREGE_PIECE IN ('BILA', 'CONFI')) <> 2)
               OR (p.DPTRC_RESULTAT_CONFIDENTIEL = 1
                AND (SELECT COUNT(*) FROM v2.ACTE_DEPOSE AS t WHERE {Prete} AND t.ACTDP_ABREGE_PIECE IN ('BILA', 'CONFI', 'RESUL')) <> 3))
        ORDER BY Confidentialite, p.DPTRC_NUM_DEPOT DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        string bilan = FormeLigne.Texte(r, "Bilan"), confi = FormeLigne.Texte(r, "DeclaConfi"), resul = FormeLigne.Texte(r, "CompteResultat");
        var nom = FormeLigne.Joindre(FormeLigne.Texte(r, "Denomination"), FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion")));
        var precisions = FormeLigne.Joindre($"Dépôt {FormeLigne.Texte(r, "DPTRC_NUM_DEPOT")} du {FormeLigne.Date(FormeLigne.DateDe(r, "DPTRC_DATE"))}",
                                            $"Bilan : {bilan}", $"Déclaration de confidentialité : {confi}", $"Compte de résultat : {resul}");
        return new LigneLue(nom, precisions, FormeLigne.Texte(r, "Confidentialite"), Etat(bilan, confi, resul), []);
    }

    /// <summary>La regle d'etat (en tete du fichier), sur les libelles des trois pieces.</summary>
    internal static string Etat(params string[] pieces)
        => pieces.Any(p => p.StartsWith("Absent", StringComparison.Ordinal) || p.StartsWith("Non numérisé", StringComparison.Ordinal)) ? "AFaire"
         : pieces.Any(p => p.Contains("à ne pas envoyer", StringComparison.Ordinal)) ? "EnCours"
         : "Validee";
}
