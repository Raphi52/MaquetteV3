using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Supprimer une instance » (PROC_XXAFF, dans l'ecran reuni « Defaire une affaire ») : les instances que RIG v2 laisse
/// supprimer, ce que la suppression emporte et ce qui l'empeche. Mode d'emploi : LecteurEcran.cs ; le geste :
/// EcrivainSuppressionInstance.cs. Plan « RIG v3 100 % fonctionnel », lot 4, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_XXAFF (le Detail OPE de proc_xxaff.md fait foi : le role reel est la suppression d'une instance,
/// MetierDescription « Supprimer une instance », PROC_XXAFF.Designer.cs:103). La recherche ne propose que
/// « Instance=JRCT,JRIP,JRJC,JROP,JRPC,JRREF,JRDD,JRMDE,JRORC » (PROC_XXAFF.Designer.cs:75), lus comme
/// CODE_AFFAIRE.R_CODEAFFAIR_TYPE_NUM_ROLE (RechercheInstance.cs:150-154). ORDRE : « order by instn_num_role desc »
/// (FormRechercheInstance.cs:633).
///
/// GROUPES (descripteur) : « En référé » = numero de role JRREF, « Au fond » les autres (le classement de « Instance
/// judiciaire », LecteurInstanceJudiciaire).
/// REGLE D'ETAT :
///   - « Verrouillée » (EnAttente) : une autre demande tient l'instance (VerrouDemandeV2, sequenceur 2J) -- la selection de RIG v2
///     la refuse (OPE_SELECT_ENTITE_JUDICIAIRE.cs:122) ;
///   - « Bloquée » (Rejetee) : Instance.CanDeleteInstance refuse (Instance.cs:4136-4229 ; ETP_INSTANCE_SUMMARY.CanDelete, :41-51) --
///     chaque raison, au mot pres, est dans la fiche (Raisons) ;
///   - « À supprimer » (AFaire) sinon. Une instance supprimee n'est plus dans la base : elle sort de la liste.
///
/// RECHERCHE (IRechercheEcran) : « Numéro d'instance » (le numero de role) et « Nom courant » (FormRechercheInstance.cs:425-426) ;
/// « Date de saisine » (INSTN_DATE_SAISINE_GREFFE, au jour pres, saisie jj/mm/aaaa ou aaaa-mm-jj).
///
/// FICHE (IFicheEcran), l'apercu de RIG v2 avant la validation : « Résumé » (l'instance, OPE_INSTANCE_SUMMARY), « Numéros à
/// libérer » (la liste de OPE_INSTANCE_LIBERER_NUM_METIER._InitZoneSaisie, OPE_INSTANCE_LIBERER_NUM_METIER.cs:44-65 : JGAFF si
/// le repertoire general est rempli, le numero de role a son type, JRDSJ si le dossier judiciaire n'a que cette instance ; annee
/// = celle de la saisine), « Contrôles » (les raisons de CanDeleteInstance) et « Suppression » (les lignes que DeleteCascade
/// efface, table par table : EcrivainSuppressionInstance.Cascade).
/// </summary>
internal sealed class LecteurSuppressionInstance : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_XXAFF";

    internal const string AuFond = "Au fond", EnRefere = "En référé";

    internal const string CritereNumero = "Numéro d'instance", CritereNom = "Nom courant", CritereSaisine = "Date de saisine";

    /// <summary>Les sections et champs du descripteur (ModifierAffaire.cs).</summary>
    internal const string SectionResume = "Résumé", SectionNumeros = "Numéros à libérer", SectionControles = "Contrôles",
                          SectionSuppression = "Suppression";

    internal const string ChampResume = "Résumé de l'instance", ChampLiberer = "Libérer le numéro d'instance",
                          ChampNumeros = "Numéros de l'instance", ChampRaisons = "Raisons de RIG v2", ChampEffacees = "Lignes effacées";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereNumero, CritereNom, CritereSaisine];

    /// <summary>Les types de numero de role que XXAFF propose (PROC_XXAFF.Designer.cs:75).</summary>
    internal const string Types = "('JRCT', 'JRIP', 'JRJC', 'JROP', 'JRPC', 'JRREF', 'JRDD', 'JRMDE', 'JRORC')";

    /// <summary>
    /// Ce que CanDeleteInstance lit d'une instance i (jointe a son code affaire ca), une colonne par controle de RIG v2
    /// (Instance.cs:4136-4229). Les decisions sont celles de ListeDecisionJudiciaire, type renseigne (Instance.cs:1674) ; leurs
    /// documents, DOCUMENT_DOSSIER.DCMDS_VALEUR_ID_METIER (DecisionJudiciaire.cs:1237) ; leur GED, VIDIGED_INDEX.VGIDX_ID_DCJDC
    /// (VidigedIndex.cs:216). Le controle du numero (VerifierCoherenceNumero, Instance.cs) appelle dbo.VerifTypeMetier, une
    /// fonction de la base V2 absente de la base V3 : la V3 la refait (NumeroConforme) sur la ligne de NUMERO_METIER qu'elle lit.
    /// </summary>
    internal static readonly string Diagnostic = """
        i.INSTN_ID_INSTN AS Id, i.INSTN_NUM_ROLE AS Role, i.INSTN_NOM_COURANT AS NomCourant, i.INSTN_NUM_REP_GENERAL AS RepGeneral,
        i.INSTN_DATE_SAISINE_GREFFE AS Saisine, i.INSTN_CODE_AFFAIRE AS CodeAffaire, ca.R_CODEAFFAIR_TYPE_NUM_ROLE AS TypeRole,
        nm.NMMTR_STRUCTURE_NUMERO AS Structure, nm.NMMTR_SEPARATEUR AS Separateur, nm.NMMTR_QUANTIEME AS Quantieme,
        nm.NMMTR_LETTRE_CATEGORIE AS Lettre, nm.NMMTR_TAILLE_CHRONO AS TailleChrono, nm.NMMTR_CODAGE_ANNEE AS CodageAnnee,
        nm.NMMTR_CODE_GREFFE AS CodeGreffe, nm.NMMTR_ID_NMMTR AS IdNumeroMetier,
        i.INSTN_ID_DSJUD AS IdDossier, dsj.DSJUD_NUM_DOSSIER AS NumDossier,
        (SELECT COUNT(*) FROM v2.INSTANCE AS o WHERE o.INSTN_ID_DSJUD = i.INSTN_ID_DSJUD) AS InstancesDuDossier,
        CASE WHEN EXISTS (SELECT 1 FROM v2.VIDIGED_INDEX AS g WHERE g.VGIDX_TYPE_ENTITE = 'INSTANCE'
                          AND (g.VGIDX_ID_ENTITE = i.INSTN_ID_INSTN OR g.VGIDX_ID_ENTITE = i.INSTN_ID_INSTN_INSTANCE_PRINCIPA
                               OR g.VGIDX_ID_ENTITE IN (SELECT f.INSTN_ID_INSTN FROM v2.INSTANCE AS f WHERE f.INSTN_ID_INSTN_INSTANCE_PRINCIPA = i.INSTN_ID_INSTN)))
             THEN 1 ELSE 0 END AS Ged,
        CASE WHEN EXISTS (SELECT 1 FROM v2.DECISION_JUDICIAIRE AS dj WHERE dj.DCJDC_ID_INSTN = i.INSTN_ID_INSTN AND dj.DCJDC_TYPE_DECISION IS NOT NULL
                          AND dj.DCJDC_NUM_REPERTOIRE IS NOT NULL
                          AND NOT EXISTS (SELECT 1 FROM v2.DOCUMENT_DOSSIER AS dd WHERE dd.DCMDS_VALEUR_ID_METIER = dj.DCJDC_ID_DCJDC)) THEN 1 ELSE 0 END AS DjRepertoire,
        CASE WHEN EXISTS (SELECT 1 FROM v2.DECISION_JUDICIAIRE AS dj WHERE dj.DCJDC_ID_INSTN = i.INSTN_ID_INSTN AND dj.DCJDC_TYPE_DECISION IS NOT NULL
                          AND (dj.DCJDC_NUM_REPERTOIRE IS NOT NULL
                               OR EXISTS (SELECT 1 FROM v2.DOCUMENT_DOSSIER AS dd WHERE dd.DCMDS_VALEUR_ID_METIER = dj.DCJDC_ID_DCJDC))) THEN 1 ELSE 0 END AS DjReda,
        CASE WHEN EXISTS (SELECT 1 FROM v2.DECISION_JUDICIAIRE AS dj JOIN v2.VIDIGED_INDEX AS g ON g.VGIDX_ID_DCJDC = dj.DCJDC_ID_DCJDC
                          WHERE dj.DCJDC_ID_INSTN = i.INSTN_ID_INSTN AND dj.DCJDC_TYPE_DECISION IS NOT NULL) THEN 1 ELSE 0 END AS DjGed,
        CASE WHEN EXISTS (SELECT 1 FROM v2.DECISION_JUDICIAIRE AS dj WHERE dj.DCJDC_ID_INSTN = i.INSTN_ID_INSTN AND dj.DCJDC_TYPE_DECISION IS NOT NULL
                          AND (dj.DCJDC_ID_APPAF_DELIBERER IS NOT NULL OR dj.DCJDC_ID_APPAF_PRONONCER IS NOT NULL)) THEN 1 ELSE 0 END AS DjAppels,
        CASE WHEN EXISTS (SELECT 1 FROM v2.DEPOT_PC AS dp JOIN v2.EVENEMENT_PROCEDURE AS ep ON ep.EVNPR_ID_EVNPR = dp.DPTPC_ID_EVNPR
                          WHERE ep.EVNPR_ID_INSTN = i.INSTN_ID_INSTN) THEN 1 ELSE 0 END AS Depots,
        m.INSTN_NUM_ROLE AS RoleMere, CASE WHEN m.INSTN_ID_INSTN IS NULL THEN 0 ELSE 1 END AS AvecMere,
        STUFF((SELECT ',' + f.INSTN_NUM_ROLE FROM v2.INSTANCE AS f WHERE f.INSTN_ID_INSTN_INSTANCE_PRINCIPA = i.INSTN_ID_INSTN
               ORDER BY f.INSTN_ID_INSTN FOR XML PATH(''), TYPE).value('.', 'varchar(max)'), 1, 1, '') AS RolesFilles,
        (SELECT COUNT(*) FROM v2.INSTANCE AS f WHERE f.INSTN_ID_INSTN_INSTANCE_PRINCIPA = i.INSTN_ID_INSTN) AS Filles,
        (SELECT ISNULL(SUM(au.ARTUT_MONTANT_TTC), 0) FROM v2.ARTICLE_UTILISE AS au WHERE au.ARTUT_ID_AFFRE = i.INSTN_ID_AFFRE) AS Factures,
        (SELECT ISNULL(SUM(ve.VENTI_MONTANT), 0) FROM v2.VENTILATION AS ve WHERE ve.VENTI_ID_AFFRE = i.INSTN_ID_AFFRE) AS Provisions,
        (SELECT COUNT(*) FROM v2.MESURE AS ms JOIN v2.DECISION_JUDICIAIRE AS dj ON dj.DCJDC_ID_DCJDC = ms.MSINS_ID_DCJDC
         WHERE dj.DCJDC_ID_INSTN = i.INSTN_ID_INSTN AND dj.DCJDC_TYPE_DECISION IS NOT NULL) AS Mesures,
        STUFF((SELECT ',' + ISNULL(ms.MSINS_NUM_MESURE, '') FROM v2.MESURE AS ms JOIN v2.DECISION_JUDICIAIRE AS dj ON dj.DCJDC_ID_DCJDC = ms.MSINS_ID_DCJDC
               WHERE dj.DCJDC_ID_INSTN = i.INSTN_ID_INSTN AND dj.DCJDC_TYPE_DECISION IS NOT NULL
               ORDER BY ms.MSINS_ID_MSINS FOR XML PATH(''), TYPE).value('.', 'varchar(max)'), 1, 1, '') AS NumerosMesures,
        CASE WHEN EXISTS (SELECT 1 FROM v2.CREANCE_PC AS cr WHERE cr.CRNPC_ID_CRNPC = i.INSTN_ID_CRNPC) THEN 1 ELSE 0 END AS Creance
        """;

    /// <summary>Les jointures du diagnostic : code affaire, ligne de NUMERO_METIER du type et de l'annee de saisine (la derniere
    /// ecrite si plusieurs : dbo.VerifTypeMetier en lit une sans ordre), dossier judiciaire, mere.</summary>
    internal const string Jointures = """
        JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
        OUTER APPLY (SELECT TOP (1) n.* FROM v2.NUMERO_METIER AS n
                     WHERE n.NMMTR_TYPE_METIER = ca.R_CODEAFFAIR_TYPE_NUM_ROLE AND n.NMMTR_ANNEE = YEAR(i.INSTN_DATE_SAISINE_GREFFE)
                     ORDER BY n.NMMTR_ID_NMMTR DESC) AS nm
        LEFT JOIN v2.DOSSIER_JUDICIAIRE AS dsj ON dsj.DSJUD_ID_DSJUD = i.INSTN_ID_DSJUD
        LEFT JOIN v2.INSTANCE AS m ON m.INSTN_ID_INSTN = i.INSTN_ID_INSTN_INSTANCE_PRINCIPA
        """;

    private static readonly string Requete = $$"""
        SELECT TOP (@plafond) {{Diagnostic}}, v.DMND_NUM_DEMANDE AS Verrou, COUNT(*) OVER () AS Total
        FROM v2.INSTANCE AS i
        {{Jointures}}
        OUTER APPLY (SELECT TOP (1) d.DMND_NUM_DEMANDE FROM v2.DEMANDE AS d
                     JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = d.DMND_ID_DMND
                     WHERE dd.DSSDM_CODE_SEQUENCEUR = '{{VerrouDemandeV2.SequenceurInstance}}'
                       AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(100), i.INSTN_ID_INSTN)
                       AND (d.DMND_EN_COURS = 1 OR d.DMND_ETAT_DEMANDE IN ('W', 'I', 'N'))
                     ORDER BY d.DMND_ID_DMND) AS v
        WHERE ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {{Types}} /*recherche*/
        ORDER BY i.INSTN_NUM_ROLE DESC, i.INSTN_ID_INSTN DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", ""), Ligne, ct, new SqlParameter("@plafond", plafond));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var filtres = new List<string>();
        static string Role(string v) => FormeLigne.Contient(v.Replace(" ", "", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add("(i.INSTN_NUM_ROLE LIKE @texteRole OR i.INSTN_NOM_COURANT LIKE @texteNom)");
            parametres.Add(new SqlParameter("@texteRole", Role(recherche.Texte)));
            parametres.Add(new SqlParameter("@texteNom", FormeLigne.Contient(recherche.Texte)));
        }
        if (recherche.Critere(CritereNumero) is { } numero)
        {
            filtres.Add("i.INSTN_NUM_ROLE LIKE @role");
            parametres.Add(new SqlParameter("@role", Role(numero)));
        }
        if (recherche.Critere(CritereNom) is { } nom)
        {
            filtres.Add("i.INSTN_NOM_COURANT LIKE @nom");
            parametres.Add(new SqlParameter("@nom", FormeLigne.Contient(nom)));
        }
        if (recherche.Critere(CritereSaisine) is { } saisine)
        {
            // Une date illisible ne retrouve rien (plutot que tout).
            var lue = DateTime.TryParseExact(saisine, ["dd/MM/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d);
            filtres.Add(lue ? "CONVERT(date, i.INSTN_DATE_SAISINE_GREFFE) = @saisine" : "1 = 0");
            if (lue) parametres.Add(new SqlParameter("@saisine", d.Date));
        }
        var sql = Requete.Replace("/*recherche*/", filtres.Count == 0 ? "" : " AND " + string.Join(" AND ", filtres));
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var role = FormeLigne.Texte(r, "Role");
        var verrou = FormeLigne.Texte(r, "Verrou");
        var raisons = Raisons(r);
        var etat = verrou.Length > 0 ? "EnAttente" : raisons.Count > 0 ? "Rejetee" : "AFaire";
        var precisions = FormeLigne.Joindre(
            role.Length > 0 ? $"Rôle {role}" : null,
            FormeLigne.DateDe(r, "Saisine") is { } s ? $"saisi le {FormeLigne.Date(s)}" : null,
            verrou.Length > 0 ? $"bloquée par la demande {verrou}" : raisons.Count > 0 ? $"{raisons.Count} raison{(raisons.Count > 1 ? "s" : "")} de RIG v2 l'empêche{(raisons.Count > 1 ? "nt" : "")}" : null);
        var id = FormeLigne.Texte(r, "Id");
        var nom = FormeLigne.Texte(r, "NomCourant") is { Length: > 0 } n ? n : $"Instance n° {(role.Length > 0 ? role : id)}";
        return new LigneLue(nom, precisions, FormeLigne.Texte(r, "TypeRole") == "JRREF" ? EnRefere : AuFond, etat, []) { Id = id };
    }

    // ------------------------------------------------------------------ CANDELETEINSTANCE

    private const string Fleche = " -> ";

    /// <summary>
    /// Les raisons de Instance.CanDeleteInstance (Instance.cs:4136-4229), dans son ordre et a ses mots (le separateur
    /// « Environment.NewLine + "\t      -> " » de RIG v2 devient « -> » sur une ligne) ; vide : la suppression est permise.
    /// </summary>
    internal static List<string> Raisons(SqlDataReader r)
    {
        var raisons = new List<string>();
        bool Oui(string colonne) => Convert.ToInt32(r[colonne], CultureInfo.InvariantCulture) == 1;
        if (Oui("Ged"))
            raisons.Add(" - L'instance que vous voulez supprimer est reliée à un document en GED." + Fleche + "Vous devez supprimer le lien entre le document et l'instance en GED.");
        if (Oui("DjRepertoire"))
            raisons.Add(" - L'instance que vous voulez supprimer contient une décision judiciaire." + Fleche + "Vous devez la supprimer en MJUD.");
        else if (Oui("DjReda"))
            raisons.Add(" - L'instance que vous voulez supprimer contient une décision judiciaire dont le jugement est généré en REDA." + Fleche + "Vous devez supprimer le jugement en REDA et la décision en MJUD.");
        else if (Oui("DjGed"))
            raisons.Add(" - L'instance que vous voulez supprimer contient une décision judiciaire reliée à un document en GED." + Fleche + "Vous devez supprimer le lien entre le document et la décision en GED.");
        else if (Oui("DjAppels"))
            raisons.Add(" - L'instance que vous voulez supprimer contient une décision judiciaire reliée à des appels d'affaires." + Fleche + "Vous devez supprimer l'étape décision judiciaire en MJUD.");
        if (Oui("Depots"))
            raisons.Add(" - L'instance que vous voulez supprimer contient un ou plusieurs dépot(s)." + Fleche + "Vous devez le ou les supprimer en MJUD.");

        // Le code affaire existe toujours ici (jointure de la recherche) : CanDeleteInstance:4163-4207.
        var role = FormeLigne.Texte(r, "Role");
        var saisine = FormeLigne.DateDe(r, "Saisine");
        var anneeRole = -1;
        // int.TryParse(INSTN_NUM_ROLE.Trim().Substring(0, 4)) : un numero de moins de 4 caracteres leverait dans RIG v2 ; ici -1.
        if (role.Length >= 4) _ = int.TryParse(role[..4], NumberStyles.Integer, CultureInfo.InvariantCulture, out anneeRole);
        if (saisine is { } s && role.Length > 0 && s.Year != anneeRole)
            raisons.Add(" - L'année de la date saisine Tribunal n'est pas cohérente avec l'année du numéro de l'instance à supprimer." + Fleche + "Veuillez contacter Amitel.");
        else if (FormeLigne.Texte(r, "TypeRole").Length == 0)
            raisons.Add("Le code affaire de l'entité n'a pas de type de numéro de rôle." + Fleche + "Veuillez contacter AMITEL.");
        else if (!NumeroConforme(r))
            raisons.Add(" - Le numéro de l'instance que vous voulez supprimer n'est pas conforme avec le code affaire saisi.");
        else
        {
            if (Oui("AvecMere"))
                raisons.Add(" - L'instance que vous voulez supprimer est liée à une autre instance." + Fleche + "Vous devez casser ce lien pour supprimer l'instance en lancant le processus JOIN sur le numéro de rôle " + FormeLigne.Texte(r, "RoleMere") + ".");
            if (Convert.ToInt32(r["Filles"], CultureInfo.InvariantCulture) > 0)
                raisons.Add(" - L'instance que vous voulez supprimer est l'instance principale d'une (ou plusieurs) autre(s) instance(s)." + Fleche + "Vous devez casser ce lien pour supprimer l'instance en lancant le processus JOIN sur le (ou les) numéro(s) de rôle " + FormeLigne.Texte(r, "RolesFilles") + ".");
            if (Convert.ToDecimal(r["Factures"], CultureInfo.InvariantCulture) != 0)
                raisons.Add(" - Une ou plusieurs factures sont attachées à l'instance que vous voulez supprimer." + Fleche + "Veuillez annuler les factures concernées (par un avoir ou en les mettant à zéro) au sein d'Expresso ou contacter le service Hot-line Facturation d'Amitel.");
            if (Convert.ToDecimal(r["Provisions"], CultureInfo.InvariantCulture) != 0)
                raisons.Add(" - Une ou plusieurs provisions sont attachées à l'instance que vous voulez supprimer." + Fleche + "Veuillez annuler ou modifier l'encaissement de l'affaire concernée au sein d'Expresso ou contacter le service Hot-line Facturation d'Amitel.");
            if (Convert.ToInt32(r["Mesures"], CultureInfo.InvariantCulture) > 0)
                raisons.Add(" - L'instance que vous voulez supprimer est liée à une (ou plusieurs) mesure(s)." + Fleche + "Vous devez supprimer la (ou les) mesure(s) de numéro(s) " + FormeLigne.Texte(r, "NumerosMesures") + ".");
        }
        if (Oui("Creance"))
            raisons.Add(" - Une créance PC existe pour l'instance que vous voulez supprimer." + Fleche + "Veuillez contacter Amitel.");
        return raisons;
    }

    /// <summary>
    /// Instance.VerifierCoherenceNumero : faux sans date de saisine ni numero de role ; sinon dbo.VerifTypeMetier(type, annee de
    /// saisine, numero, greffe), refaite ici sur la ligne de NUMERO_METIER lue (structure : A annee, S separateur, C chrono, L
    /// lettre, Q quantieme, G code greffe, K etoile ; une structure absente laisse le numero conforme, comme la boucle SQL qui ne
    /// tourne pas). Le code greffe compare est celui de la ligne NUMERO_METIER (NMMTR_CODE_GREFFE) : la base V3 ne porte pas le
    /// code du greffe que RIG v2 passe (_codeGreffe).
    /// </summary>
    internal static bool NumeroConforme(SqlDataReader r)
    {
        var role = FormeLigne.Texte(r, "Role");
        if (FormeLigne.DateDe(r, "Saisine") is not { } saisine || role.Length == 0) return false;
        var structure = r["Structure"] is string st ? st : null;
        if (structure is null) return true;
        var annee = saisine.Year.ToString(CultureInfo.InvariantCulture);
        string Sous(int debut, int longueur) => debut - 1 >= role.Length || longueur <= 0 ? "" : role.Substring(debut - 1, Math.Min(longueur, role.Length - debut + 1));
        var codage = r["CodageAnnee"] is DBNull ? 0 : Convert.ToInt32(r["CodageAnnee"], CultureInfo.InvariantCulture);
        var separateur = r["Separateur"] is string sep ? sep : null;
        var lettre = r["Lettre"] is string l ? l : null;
        var quantieme = r["Quantieme"] is DBNull ? null : Convert.ToString(r["Quantieme"], CultureInfo.InvariantCulture);
        var taille = r["TailleChrono"] is DBNull ? 0 : Convert.ToInt32(r["TailleChrono"], CultureInfo.InvariantCulture);
        var greffe = r["CodeGreffe"] is string g ? g : "";
        var position = 1;
        foreach (var c in structure)
        {
            switch (c)
            {
                case 'A':
                    if (Sous(position, codage) != (codage <= 0 ? "" : annee[^Math.Min(codage, annee.Length)..])) return false;
                    position += codage;
                    break;
                case 'S':
                    // len(NULL) est NULL en SQL : un separateur absent fait echouer la comparaison.
                    if (separateur is null || Sous(position, separateur.TrimEnd().Length) != separateur.TrimEnd()) return false;
                    position += separateur.TrimEnd().Length;
                    break;
                case 'C':
                    if (!EstNumerique(Sous(position, taille))) return false;
                    position += taille;
                    break;
                case 'L':
                    if (lettre is null || Sous(position, lettre.TrimEnd().Length) != lettre.TrimEnd()) return false;
                    position += lettre.TrimEnd().Length;
                    break;
                case 'Q':
                    if (quantieme is null || Sous(position, quantieme.Length) != quantieme) return false;
                    position += quantieme.Length;
                    break;
                case 'G':
                    if (Sous(position, 4) != greffe) return false;
                    position += 4;
                    break;
                case 'K':
                    if (Sous(position, 1) != "*") return false;
                    position += 1;
                    break;
            }
        }
        return true;
    }

    // isnumeric() de SQL Server sur des chiffres : vrai pour un entier ecrit en chiffres (le chrono d'un numero de role).
    private static bool EstNumerique(string s) => s.Length > 0 && s.Trim().Length > 0 && s.Trim().All(char.IsAsciiDigit);

    /// <summary>Les numeros que OPE_INSTANCE_LIBERER_NUM_METIER restituerait (OPE_INSTANCE_LIBERER_NUM_METIER.cs:46-65) :
    /// (type, numero), puis l'annee de restitution (0 sans saisine).</summary>
    internal static (List<(string Type, string Numero)> Numeros, int Annee) NumerosALiberer(SqlDataReader r)
    {
        var numeros = new List<(string, string)>();
        var rep = FormeLigne.Texte(r, "RepGeneral");
        if (rep.Length > 0) numeros.Add(("JGAFF", rep));
        numeros.Add((FormeLigne.Texte(r, "TypeRole"), FormeLigne.Texte(r, "Role")));
        if (r["IdDossier"] is int && Convert.ToInt32(r["InstancesDuDossier"], CultureInfo.InvariantCulture) == 1)
            numeros.Add(("JRDSJ", FormeLigne.Texte(r, "NumDossier")));
        return (numeros, FormeLigne.DateDe(r, "Saisine")?.Year ?? 0);
    }

    // ------------------------------------------------------------------ LA FICHE

    private static readonly string Fiche = $"""
        SELECT {Diagnostic}, i.INSTN_DATE_ENROLEMENT AS Enrolement,
               COALESCE(na.R_NATUREAFFA_LIBELLE_ECRAN, i.INSTN_NATURE_AFFAIRE) AS Nature
        FROM v2.INSTANCE AS i
        {Jointures}
        LEFT JOIN v2.NATURE_AFFAIRE AS na ON na.R_NATUREAFFA_VALEUR = i.INSTN_NATURE_AFFAIRE
        WHERE i.INSTN_ID_INSTN = @id AND ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {Types}
        """;

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        string[] resume;
        List<string> raisons;
        List<(string Type, string Numero)> numeros;
        int annee;
        string nom;
        await using (var r = await LectureV2.LireAsync(baseGreffe, Fiche, ct, new SqlParameter("@id", n)))
        {
            if (!await r.ReadAsync(ct)) return null;
            var role = FormeLigne.Texte(r, "Role");
            nom = FormeLigne.Texte(r, "NomCourant") is { Length: > 0 } nc ? nc : $"Instance n° {role}";
            resume = [role, FormeLigne.Texte(r, "RepGeneral"), FormeLigne.Texte(r, "CodeAffaire"), FormeLigne.Texte(r, "Nature"),
                      FormeLigne.Date(FormeLigne.DateDe(r, "Saisine")), FormeLigne.Date(FormeLigne.DateDe(r, "Enrolement"))];
            raisons = Raisons(r);
            (numeros, annee) = NumerosALiberer(r);
        }
        var motif = await VerrouDemandeV2.MotifAsync(baseGreffe, null, "Instance", VerrouDemandeV2.SequenceurInstance, id, ct);
        var effacees = await EcrivainSuppressionInstance.CompterCascadeAsync(baseGreffe, null, n, ct);
        return new FicheEcran(Procedure, id, nom,
        [
            new SectionFiche(SectionResume, [new(ChampResume, "")
            {
                Colonnes = ["Numéro de rôle", "Répertoire général", "Code affaire", "Nature affaire", "Saisine tribunal", "Enrôlement"],
                Lignes = [resume],
            }]),
            new SectionFiche(SectionNumeros,
            [
                // Cochee par defaut dans RIG v2 (OPE_INSTANCE_LIBERER_NUM_METIER.cs:47) ; la V3 ne rend pas les numeros (geste).
                new(ChampLiberer, "oui"),
                new(ChampNumeros, "") { Colonnes = ["Type", "Numéro", "Année"], Lignes = [.. numeros.Select(x => new[] { x.Type, x.Numero, annee.ToString(CultureInfo.InvariantCulture) })] },
            ]),
            new SectionFiche(SectionControles, [new(ChampRaisons, "")
            {
                Colonnes = ["Raison"],
                Lignes = raisons.Count == 0 ? [["Aucune : RIG v2 permet la suppression."]] : [.. raisons.Select(x => new[] { x.Trim() })],
            }]),
            new SectionFiche(SectionSuppression, [new(ChampEffacees, "")
            {
                Colonnes = ["Table", "Lignes"], Lignes = [.. effacees.Where(e => e.Lignes > 0).Select(e => new[] { e.Table, e.Lignes.ToString(CultureInfo.InvariantCulture) })],
            }]),
        ])
        {
            LectureSeule = motif is not null,
            Motif = motif,
        };
    }
}
