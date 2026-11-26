using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Demandes » (PROC_DEMANDE, web/src/ecrans/EcranDemandes.tsx) : la recherche multi-criteres des demandes, lue dans la
/// base V3 du greffe (v2.DEMANDE, copie telle quelle, et les fiches reprises). LECTURE SEULE (LectureV2).
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_DEMANDE\Metier\MetierProcDemande.cs, qui construit
/// la requete de la grille (_ConstruireRequeteDemande, :112-178), et la fiche proc_demande.md (§4 a §16).
///
/// COLONNES (MetierProcDemande.SQLSelect :58-77 ; DemandeDataGridView.cs :78-272) :
///   numero DMND_NUM_DEMANDE, nom DMND_NOM, dossier DMND_NUM_INDICATIF, traitement DMND_CODE_PROSS, en cours DMND_EN_COURS,
///   creation / validation DMND_DATE_CREATION / DMND_DATE_VALIDATION ; Siren = DOSSIER_RCS.DSSRC_SIREN par DMND_ID_DSRCS (:71,
///   fiche rec.DossierRcs) ; etat = ETAT_DEMANDE.R_ETATDEMAND_LIBELLE_ECRAN (:72-75) ; utilisateur = UTLST_NOM + ' ' +
///   UTLST_PRENOM par DMND_ID_UTLST (:70, :268-270 ; fiche soc.Compte, IdV2 = UTLST_ID_UTLST) ; client = civilite, nom,
///   prenom et (abrege) du client de la FACTURE de la demande (SQLSelectChampNomClient :93-100 ; jointures :276, :285 ; fiche
///   rec.Client). Une demande a plusieurs factures donnait une ligne par facture dans RIG v2 (LEFT JOIN) : ici une ligne,
///   avec le client de la derniere facture creee. Le libelle du traitement est celui de la liste « Processus »
///   (OPE_SEARCH_BLOC_2.cs:51, PROCF_LIBELLE ; fiche soc.Fonction).
///
/// FILTRES (_ConstruireClauseWhereFiltres :625-704, _ConstruireClauseWhereRequeteDemandes :731-808) :
///   - Processus : DMND_CODE_PROSS = code (:652-656) ;
///   - Du / Au sur la date de CREATION (le type de date « C » de l'ecran, :663-669) ; Du SEUL = ce jour-la seulement
///     (>= 00h ET <= 24h du meme jour, :671-679), comme RIG v2 ;
///   - Etats : les libelles coches, traduits par ETAT_DEMANDE en DMND_ETAT_DEMANDE IN (...) (:639-640) ; « Traitement EDI »
///     = la demande a une ligne EDI_DOSSIER (:633-634, OPE_ETATS_DMD.cs:70) ;
///   - En cours uniquement : DMND_EN_COURS = 1 (:631-632, l'etat « EC » de RIG v2) ;
///   - Client : des chiffres = numero de compte (CLIEN_NUMERO_COMPTE =, :707-713) ; sinon debut du code abrege OU du nom
///     (CLIEN_ABREGE / CLIEN_NOM LIKE 'x%', :714-726) ;
///   - Demande : RIG v2 DEVINE le type du numero tape (_DetectTypeRecherche et la table numero_metier, :376-459) puis
///     applique UN filtre. Ici les quatre filtres de RIG v2 qu'un texte libre peut viser sont essayes ensemble : numero de
///     demande (DMND_NUM_DEMANDE =, :739-741), numero de dossier (DMND_NUM_INDICATIF =, :742-745, sans le calcul des
///     numeros « compatibles »), SIREN (dossier RCS, :756-758), mot du nom (:786-799 : CONTAINS plein texte dans RIG v2 ;
///     la base V3 n'a pas de catalogue plein texte, d'ou un LIKE). Les numeros de facture, de liaison, de depot et de chrono
///     de RIG v2 ne sont PAS cherches (non branches, dit dans le compte rendu du lot 17).
///   - Une demande « En attente pieces » (etat Y, Demande.cs:50) n'est montree dans RIG v2 que si cet etat est coche
///     (OPE_RESULTATS.cs:1194-1205), et la case n'est offerte qu'aux agents d'Amitel (OPE_ETATS_DMD.cs:72-74) : l'ecran ne
///     l'offre pas, ces demandes ne sont pas montrees.
/// ORDRE : DMND_ID_DMND decroissant, la plus recente en tete (:149). PLAFOND : « Nombre maxi. » (TOP, :183-187).
/// </summary>
public static class LectureDemandes
{
    /// <summary>Le libelle de l'etat « Traitement EDI » de la liste de RIG v2 (OPE_ETATS_DMD.cs:70).</summary>
    public const string EtatTraitementEdi = "Traitement EDI";

    /// <summary>Les demandes filtrees, plafonnees et comptees sur v2.DEMANDE seule ; les filtres s'ajoutent a la fin.</summary>
    private const string Trouvees = """
        SELECT x.DMND_NUM_DEMANDE, x.DMND_NOM, x.DMND_NUM_INDICATIF, x.DMND_CODE_PROSS, x.DMND_EN_COURS,
               x.DMND_DATE_CREATION, x.DMND_DATE_VALIDATION, x.Siren, x.Total, f.Libelle AS ProcessusLibelle,
               ISNULL(e.R_ETATDEMAND_LIBELLE_ECRAN, x.DMND_ETAT_DEMANDE) AS EtatLibelle,
               u.Nom AS UtilisateurNom, u.Prenom AS UtilisateurPrenom,
               c.Civilite AS ClientCivilite, c.Nom AS ClientNom, c.Prenom AS ClientPrenom, c.Abrege AS ClientAbrege
        FROM (SELECT TOP (@plafond) d.DMND_ID_DMND, d.DMND_NUM_DEMANDE, d.DMND_NOM, d.DMND_NUM_INDICATIF, d.DMND_CODE_PROSS,
                     d.DMND_ETAT_DEMANDE, d.DMND_EN_COURS, d.DMND_DATE_CREATION, d.DMND_DATE_VALIDATION, d.DMND_ID_UTLST,
                     s.Siren, COUNT(*) OVER () AS Total
              FROM v2.DEMANDE AS d
              LEFT JOIN rec.DossierRcs AS s ON s.Id = d.DMND_ID_DSRCS
              WHERE d.DMND_ETAT_DEMANDE <> 'Y'
        """;

    /// <summary>Ce qui s'affiche, pour les lignes rendues seulement : etat, traitement, agent, client de la derniere facture.</summary>
    private const string Affichage = """

              ORDER BY d.DMND_ID_DMND DESC) AS x
        LEFT JOIN v2.ETAT_DEMANDE AS e ON e.R_ETATDEMAND_VALEUR = x.DMND_ETAT_DEMANDE
        LEFT JOIN soc.Fonction AS f ON f.Code = x.DMND_CODE_PROSS
        LEFT JOIN soc.Compte AS u ON u.IdV2 = x.DMND_ID_UTLST
        OUTER APPLY (SELECT TOP (1) cl.Civilite, cl.Nom, cl.Prenom, cl.Abrege
                     FROM v2.FACTURE AS fa JOIN rec.Client AS cl ON cl.Id = fa.FACTU_ID_CLIEN
                     WHERE fa.FACTU_NUM_DEMANDE = x.DMND_NUM_DEMANDE
                     ORDER BY fa.FACTU_DATE_CREATION DESC, fa.FACTU_NUMERO_FACTURE DESC) AS c
        ORDER BY x.DMND_ID_DMND DESC
        """;

    /// <summary>Le client de RIG v2 (filtre :705-728), sur toute facture de la demande (la jointure LEFT de RIG v2, :285).</summary>
    private const string ClientDeLaDemande =
        " AND EXISTS (SELECT 1 FROM v2.FACTURE AS fa JOIN rec.Client AS cl ON cl.Id = fa.FACTU_ID_CLIEN" +
        " WHERE fa.FACTU_NUM_DEMANDE = d.DMND_NUM_DEMANDE AND ";

    /// <summary>Les demandes qui repondent a <paramref name="r"/>, et les listes de choix de l'ecran.</summary>
    public static async Task<DemandesLues> LireAsync(SqlConnection baseGreffe, RechercheDemandes r, CancellationToken ct)
    {
        var etats = await EtatsAsync(baseGreffe, ct);
        var (codes, libelles) = await TraitementsAsync(baseGreffe, ct);

        var sql = new StringBuilder(Trouvees);
        var p = new List<SqlParameter> { new("@plafond", SqlDbType.Int) { Value = Math.Clamp(r.Plafond, 1, RoutesEcransMain.Plafond) } };

        if (!string.IsNullOrWhiteSpace(r.Processus))
        {
            sql.Append(" AND d.DMND_CODE_PROSS = @processus");
            p.Add(Texte("@processus", r.Processus.Trim()));
        }
        if (r.Du is { } du)
        {
            // Du seul : ce jour-la (MetierProcDemande.cs:671-679) ; Du et Au : la periode, bornes comprises (:680-697).
            var au = r.Au ?? du;
            sql.Append(" AND d.DMND_DATE_CREATION >= @du AND d.DMND_DATE_CREATION < @lendemainAu");
            p.Add(new SqlParameter("@du", SqlDbType.DateTime) { Value = du.ToDateTime(TimeOnly.MinValue) });
            p.Add(new SqlParameter("@lendemainAu", SqlDbType.DateTime) { Value = au.AddDays(1).ToDateTime(TimeOnly.MinValue) });
        }
        else if (r.Au is { } auSeul)
        {
            sql.Append(" AND d.DMND_DATE_CREATION < @lendemainAu");
            p.Add(new SqlParameter("@lendemainAu", SqlDbType.DateTime) { Value = auSeul.AddDays(1).ToDateTime(TimeOnly.MinValue) });
        }
        var cochees = (r.Etats ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        if (cochees.Remove(EtatTraitementEdi))
            sql.Append(" AND d.DMND_ID_DMND IN (SELECT ed.EDIDO_ID_DMND FROM v2.EDI_DOSSIER AS ed)");
        var valeurs = cochees.Select(l => etats.FirstOrDefault(e => e.Libelle == l).Valeur).ToList();
        if (valeurs.Count > 0)
        {
            // Un libelle inconnu ne correspond a aucune valeur : il ne rend rien (il ne s'ignore pas en silence).
            var noms = new List<string>();
            for (var i = 0; i < valeurs.Count; i++)
            {
                noms.Add($"@etat{i}");
                p.Add(Texte($"@etat{i}", valeurs[i] ?? "\u0000"));
            }
            sql.Append($" AND d.DMND_ETAT_DEMANDE IN ({string.Join(", ", noms)})");
        }
        if (r.EnCours) sql.Append(" AND d.DMND_EN_COURS = 1");
        if (!string.IsNullOrWhiteSpace(r.Client))
        {
            var client = r.Client.Trim();
            var chiffres = client.Replace(" ", "", StringComparison.Ordinal);
            if (chiffres.Length > 0 && chiffres.All(char.IsAsciiDigit))
            {
                sql.Append(ClientDeLaDemande + "cl.NumeroCompte = @compte)");
                p.Add(Texte("@compte", chiffres));
            }
            else
            {
                sql.Append(ClientDeLaDemande + "(cl.Abrege LIKE @client OR cl.Nom LIKE @client))");
                p.Add(Texte("@client", Debut(client)));
            }
        }
        if (!string.IsNullOrWhiteSpace(r.Demande))
        {
            var texte = r.Demande.Trim();
            sql.Append(" AND (d.DMND_NUM_DEMANDE = @numero OR d.DMND_NUM_INDICATIF = @numero OR s.Siren = @numero OR d.DMND_NOM LIKE @nom)");
            p.Add(Texte("@numero", texte.Replace(" ", "", StringComparison.Ordinal)));
            p.Add(Texte("@nom", Contient(texte)));
        }
        sql.Append(Affichage);

        var lignes = new List<DemandeLue>();
        var total = 0;
        await using (var lu = await LectureV2.LireAsync(baseGreffe, sql.ToString(), ct, [.. p]))
        {
            while (await lu.ReadAsync(ct))
            {
                total = Convert.ToInt32(lu["Total"], CultureInfo.InvariantCulture);
                var code = Chaine(lu, "DMND_CODE_PROSS");
                lignes.Add(new DemandeLue(
                    Chaine(lu, "DMND_NUM_DEMANDE"), Chaine(lu, "DMND_NOM"), Chaine(lu, "DMND_NUM_INDICATIF"), Chaine(lu, "Siren"),
                    NomClient(Chaine(lu, "ClientCivilite"), Chaine(lu, "ClientNom"), Chaine(lu, "ClientPrenom"), Chaine(lu, "ClientAbrege")),
                    code, Chaine(lu, "ProcessusLibelle") is { Length: > 0 } libelle ? libelle : code,
                    Chaine(lu, "EtatLibelle"), lu["DMND_EN_COURS"] is true,
                    lu["DMND_DATE_CREATION"] as DateTime?, lu["DMND_DATE_VALIDATION"] as DateTime?,
                    Joindre(Chaine(lu, "UtilisateurNom"), Chaine(lu, "UtilisateurPrenom"))));
            }
        }
        return new DemandesLues([.. lignes], total, codes, libelles, [.. etats.Select(e => e.Libelle), EtatTraitementEdi]);
    }

    /// <summary>Les etats de la liste de RIG v2 : ETAT_DEMANDE tel quel (OPE_ETATS_DMD.cs:63-66), dans l'ordre de la table.</summary>
    private static async Task<List<(string Valeur, string Libelle)>> EtatsAsync(SqlConnection cn, CancellationToken ct)
    {
        var etats = new List<(string, string)>();
        await using var lu = await LectureV2.LireAsync(cn,
            "SELECT R_ETATDEMAND_VALEUR, R_ETATDEMAND_LIBELLE_ECRAN FROM v2.ETAT_DEMANDE", ct);
        while (await lu.ReadAsync(ct)) etats.Add((Chaine(lu, "R_ETATDEMAND_VALEUR"), Chaine(lu, "R_ETATDEMAND_LIBELLE_ECRAN")));
        return etats;
    }

    /// <summary>
    /// La liste « Processus » de RIG v2 : les procedures (type DLL, EXE, PROCVB6 -- un type illisible compte pour PROCVB6),
    /// pas les fonctions (FONC, FONCVB6) : OPE_SEARCH_BLOC_2.cs:43-61, ProcessusEtFonctionalite.TypeProc. Reprise dans
    /// soc.Fonction (SOC-002, PROCF_TYPE -> TypeV2) ; une fonction neuve de la V3 (« V3 ») n'est pas un traitement V2.
    /// </summary>
    private static async Task<(string[] Codes, string[] Libelles)> TraitementsAsync(SqlConnection cn, CancellationToken ct)
    {
        var codes = new List<string>();
        var libelles = new List<string>();
        await using var lu = await LectureV2.LireAsync(cn,
            "SELECT Code, Libelle FROM soc.Fonction WHERE TypeV2 NOT IN ('FONC', 'FONCVB6', 'V3') ORDER BY Code", ct);
        while (await lu.ReadAsync(ct))
        {
            codes.Add(Chaine(lu, "Code"));
            libelles.Add(Chaine(lu, "Libelle"));
        }
        return ([.. codes], [.. libelles]);
    }

    /// <summary>Le client comme RIG v2 l'ecrit : « civilite nom prenom (abrege) », sans les morceaux absents (:93-100).</summary>
    internal static string NomClient(string civilite, string nom, string prenom, string abrege)
        => nom.Length == 0 ? "" : Joindre(civilite, nom, prenom) + (abrege.Length > 0 ? $" ({abrege})" : "");

    private static string Joindre(params string[] morceaux) => string.Join(" ", morceaux.Where(m => m.Length > 0));

    internal static string Chaine(SqlDataReader r, string colonne)
        => r[colonne] is string s ? s.Trim() : r[colonne] is DBNull ? "" : Convert.ToString(r[colonne], CultureInfo.InvariantCulture)?.Trim() ?? "";

    internal static SqlParameter Texte(string nom, string valeur) => new(nom, SqlDbType.VarChar, 512) { Value = valeur };

    /// <summary>Un motif LIKE « commence par » <paramref name="texte"/>, ses caracteres speciaux echappes.</summary>
    internal static string Debut(string texte) => Echapper(texte.Trim()) + "%";

    /// <summary>Un motif LIKE « contient » <paramref name="texte"/>, ses caracteres speciaux echappes.</summary>
    internal static string Contient(string texte) => "%" + Echapper(texte.Trim()) + "%";

    private static string Echapper(string t)
        => t.Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal).Replace("_", "[_]", StringComparison.Ordinal);
}
