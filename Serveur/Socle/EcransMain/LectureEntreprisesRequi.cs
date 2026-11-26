using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransMain.LectureDemandes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Réquisitions » (web/src/ecrans/EcranRequisitions.tsx) : la recherche des entreprises, lue dans la base V3 du greffe.
/// LECTURE SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026 (marche du lot 17).
///
/// SOURCE (ancien RIG) : PROC_REQUI, OPE_REQUI_RECHERCHE.Rechercher (branche Feature/RCS/275_Refonte_Requi de D:\RigApplication,
/// Source\RIG\DLL\Processus\PROC_REQUI\Opérations\OPE_REQUI_RECHERCHE.cs:140-256) : la raison sociale, le SIREN (blancs retires),
/// l'adresse, le numero de voie, le code postal et la ville, « Inclure les supprimes », la limite de dossiers. La V2 les passe a
/// son moteur RechercheDossier (mots, phonetique, niveaux de fidelite) : RIG v3 reprend la requete de « Dossier RCS »
/// (LecteurDossierRcs.cs, PROC_VDOSSIER) -- designation qui CONTIENT la raison sociale, SIREN egal --, et l'adresse du siege
/// (rec.Adresse de l'entreprise : voie et ville contiennent, code postal commence par, numero de voie egal).
/// NOMBRE D'INSCRIPTIONS (colonne « Nb Inscriptions », OPE_REQUI_RESULTATS.cs:363-366 de la branche 275 ; frmREQUI.cs:513-517) :
/// RechercheDossier.NombreInscription (Source\Outils\DLL\RigRecherche\Recherche\RechercheDossier.cs:127-165) -- les inscriptions
/// dont un debiteur (DEBITEUR_END) est l'entreprise, a l'adresse du siege s'il en a une, ni perimees ni radiees (RADIATION_TOTALE)
/// et diffusees a la date de fraicheur du domaine ENDETT (RechercheDossier.cs:252 et :313, RecuperationDate :180-228 : la date de
/// fraicheur du type, de son activite puis de son domaine, sinon maintenant).
/// PROCEDURE COLLECTIVE (ce qui bloque le certificat de non-faillite, EntrepriseRequi.ProcedureCollective -> CnfPossible) :
/// VerifSiPC(entreprise, « ENTR ») de OPE_REQUI_PANIER.cs:379 (regle OPE_REQUI_RESULTATS.cs:579-616) -- deux PC ou plus de
/// l'entreprise bloquent ; une seule bloque sauf si elle est non diffusable sans non-inscrit, si son code de cloture arrete la
/// diffusion (R_CODECLOTUR_ARRET_DIFF_INFO) ou si son etat est « N ». Le rouge de la designation (VerifSiPC « DOSS ») n'est pas
/// porte a part : l'ecran n'a qu'un drapeau.
/// NON REPRIS, nommes : la phonetique et l'extension de la recherche, le dirigeant et l'activite (le moteur de la V2), les
/// etablissements secondaires (« Tous les etablissements », TODO #275 de la V2 elle-meme), les non-inscrits.
/// ORDRE : designation, puis numero de gestion.
/// </summary>
public static class LectureEntreprisesRequi
{
    /// <summary>La limite de dossiers de OPE_REQUI_RECHERCHE (50 a 500).</summary>
    public const int LimiteMin = 50, LimiteMax = 500;

    private const string Nom = "COALESCE(NULLIF(LTRIM(RTRIM(e.Denomination)), ''), LTRIM(RTRIM(CONCAT(e.Patronyme, ' ', e.Prenom))))";

    private const string Requete = $"""
        SELECT TOP (@limite) s.Id, s.Siren, s.NumeroGestion, {Nom} AS Designation, e.FormeJuridique,
               CASE WHEN NULLIF(LTRIM(RTRIM(e.Denomination)), '') IS NULL THEN 'PP' ELSE 'PM' END AS Personne,
               LTRIM(RTRIM(CONCAT(a.NumeroVoie, ' ', a.TypeVoie, ' ', a.NomVoie))) AS Adresse, a.CodePostal, a.Localite,
               CASE WHEN s.DateRadiation IS NULL THEN 0 ELSE 1 END AS Radie, COUNT(*) OVER () AS Total,
               (SELECT COUNT(*) FROM rec.Inscription AS i
                JOIN v2.DEBITEUR_END AS b ON b.DBEND_ID_INSCR = i.Id
                LEFT JOIN v2.RADIATION_TOTALE AS r ON r.RDTTL_ID_INSCR = i.Id
                WHERE b.DBEND_ID_RPRTR = s.IdEntreprise AND (a.Id IS NULL OR b.DBEND_ID_ADRBS = a.Id)
                  AND (i.DatePeremption >= f.Jour OR i.DatePeremption IS NULL) AND i.DateDiffusion <= f.Jour
                  AND (r.RDTTL_ID_RDTTL IS NULL OR r.RDTTL_DATE_RADIATION > f.Jour)) AS NbInscriptions,
               CASE WHEN (SELECT COUNT(*) FROM v2.PROCEDURE_COLLECTIVE AS p WHERE p.PRCLL_ID_RPRTR = s.IdEntreprise) >= 2 THEN 1
                    WHEN EXISTS (SELECT 1 FROM v2.PROCEDURE_COLLECTIVE AS p
                                 LEFT JOIN v2.CODE_CLOTURE AS cl ON cl.R_CODECLOTUR_VALEUR = p.PRCLL_CODE_CLOTURE
                                 WHERE p.PRCLL_ID_RPRTR = s.IdEntreprise
                                   AND NOT (ISNULL(p.PRCLL_NON_DIFFUSABLE, 0) = 1 AND p.PRCLL_ID_NINSC IS NULL)
                                   AND ISNULL(cl.R_CODECLOTUR_ARRET_DIFF_INFO, 0) = 0 AND ISNULL(p.PRCLL_ETAT_PC, '') <> 'N') THEN 1
                    ELSE 0 END AS ProcedureCollective
        FROM rec.DossierRcs AS s
        JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        LEFT JOIN rec.Adresse AS a ON a.Id = e.IdAdresse
        CROSS JOIN (SELECT COALESCE((SELECT TOP (1) COALESCE(db.DTTRV_DATE_FRAICHEUR, da.DTTRV_DATE_FRAICHEUR, dd.DTTRV_DATE_FRAICHEUR)
                                     FROM rec.TypeDemande AS t
                                     LEFT JOIN rec.TypeDemande AS td ON td.Id = t.IdDomaine
                                     LEFT JOIN rec.TypeDemande AS ta ON ta.Id = t.IdActivite
                                     LEFT JOIN v2.DATE_TRAVAIL AS db ON db.DTTRV_CODE_DATE_TRAVAIL = t.CodeDateTravail
                                     LEFT JOIN v2.DATE_TRAVAIL AS da ON da.DTTRV_CODE_DATE_TRAVAIL = ta.CodeDateTravail
                                     LEFT JOIN v2.DATE_TRAVAIL AS dd ON dd.DTTRV_CODE_DATE_TRAVAIL = td.CodeDateTravail
                                     WHERE t.Reference = 'ENDETT'), GETDATE()) AS Jour) AS f
        WHERE (@raison = '' OR {Nom} LIKE '%' + @raison + '%')
          AND (@siren = '' OR s.Siren = @siren)
          AND (@numVoie = '' OR a.NumeroVoie = @numVoie)
          AND (@adresse = '' OR a.NomVoie LIKE '%' + @adresse + '%')
          AND (@codePostal = '' OR a.CodePostal LIKE @codePostal + '%')
          AND (@ville = '' OR a.Localite LIKE '%' + @ville + '%')
          AND (@radies = 1 OR s.DateRadiation IS NULL)
        ORDER BY {Nom}, s.NumeroGestion
        """;

    /// <summary>Les dossiers de <paramref name="r"/> ; aucun critere cherchable : aucune ligne.</summary>
    public static async Task<EntreprisesLues> LireAsync(SqlConnection baseGreffe, RechercheEntreprises r, CancellationToken ct)
    {
        string T(string? s) => (s ?? "").Trim();
        var siren = T(r.Siren).Replace(" ", "");
        if (T(r.RaisonSociale).Length == 0 && siren.Length == 0 && T(r.NumVoie).Length == 0 && T(r.Adresse).Length == 0
            && T(r.CodePostal).Length == 0 && T(r.Ville).Length == 0)
            return new EntreprisesLues([], 0);
        var lignes = new List<EntrepriseLue>();
        var total = 0;
        await using var lu = await LectureV2.LireAsync(baseGreffe, Requete, ct,
            new SqlParameter("@limite", SqlDbType.Int) { Value = Math.Clamp(r.Limite, LimiteMin, LimiteMax) },
            new SqlParameter("@raison", SqlDbType.VarChar, 512) { Value = T(r.RaisonSociale) },
            new SqlParameter("@siren", SqlDbType.VarChar, 14) { Value = siren },
            new SqlParameter("@numVoie", SqlDbType.VarChar, 15) { Value = T(r.NumVoie) },
            new SqlParameter("@adresse", SqlDbType.VarChar, 70) { Value = T(r.Adresse) },
            new SqlParameter("@codePostal", SqlDbType.VarChar, 10) { Value = T(r.CodePostal) },
            new SqlParameter("@ville", SqlDbType.VarChar, 70) { Value = T(r.Ville) },
            new SqlParameter("@radies", SqlDbType.Bit) { Value = r.InclureRadies });
        while (await lu.ReadAsync(ct))
        {
            total = Convert.ToInt32(lu["Total"], CultureInfo.InvariantCulture);
            lignes.Add(new EntrepriseLue(Convert.ToInt32(lu["Id"], CultureInfo.InvariantCulture), Chaine(lu, "Siren"), Chaine(lu, "NumeroGestion"),
                Chaine(lu, "Designation"), Chaine(lu, "Personne"), Chaine(lu, "FormeJuridique"), Chaine(lu, "Adresse"), Chaine(lu, "CodePostal"), Chaine(lu, "Localite"),
                Convert.ToInt32(lu["Radie"], CultureInfo.InvariantCulture) == 1, Convert.ToInt32(lu["NbInscriptions"], CultureInfo.InvariantCulture),
                Convert.ToInt32(lu["ProcedureCollective"], CultureInfo.InvariantCulture) == 1));
        }
        return new EntreprisesLues([.. lignes], total);
    }
}
