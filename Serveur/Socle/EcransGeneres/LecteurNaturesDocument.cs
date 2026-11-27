using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Natures de document » (PROC_GED_NATURE_DOC) : les natures de document de la GED du greffe, table VIDIGED_PARAMETRAGE de type
/// NATURE_DOC. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026. Ajouter, Modifier, Supprimer :
/// EcrivainNaturesDocument.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_GEDJUD\PROC_GED_NATURE_DOC.
///   - LA LISTE : OPE_GED_NATURE_DOC_READONLY.LoadNatures (l. 98-105) -- VidigedParametrage.GetListVidigedParametrageByType("NATURE_DOC"),
///     triee par code (Rechercher, l. 123) ; colonnes « Code », « Libellé », « Type de document » (l. 44-46, le libelle ecran de
///     WEBS_DOC_REGROUPEMENT, VidigedParametrage.cs:327-333). VIDIGED_PARAMETRAGE est dans la base du greffe (le cache la lit par la
///     connexion du greffe, Cache.cs:82, RigGreffe.GlobalSqlRigConnection) : la base V3 l'a recopiee.
///   - LA RECHERCHE : plein texte sur le code et le libelle (ConstructFullText, l. 107-112) ; criteres Code et Libelle.
///   - LES GROUPES : « Associées à un document » / « Sans document » -- le controle de suppression de la V2 (une nature citee par un
///     document VIDIGED_INDEX.VGIDX_NATURE_DOC ne se supprime pas, OPE_GED_NATURE_DOC_EDIT.cs:116-119) : le groupe dit d'avance si la
///     suppression sera refusee.
///   - L'ETAT : libelle vide -> AFaire « À compléter » (« Le libellé est manquant », OPE_GED_NATURE_DOC_EDIT.cs:242) ; sinon Validee.
///   - LA FICHE : les champs de la saisie V2 (ChargerInfos, l. 193-226) ; le code ne se modifie pas (ult_txt_code.MetierReadOnly, l. 202),
///     le libelle et le type de document oui ; les autres reglages GED se lisent (EcrivainNaturesDocument.cs dit pourquoi).
/// VALEURS : dans l'ordre des Colonnes (Code, Libellé, Type de document).
/// </summary>
internal sealed class LecteurNaturesDocument : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_GED_NATURE_DOC";

    internal const string Associees = "Associées à un document", SansDocument = "Sans document";
    internal const string ColCode = "Code", ColLibelle = "Libellé", ColType = "Type de document";
    internal const string NatureDoc = "NATURE_DOC", NatureDocDeposant = "NATURE_DOC_DEPOSANT";

    public IReadOnlyCollection<string> Criteres { get; } = [ColCode, ColLibelle];

    private const string Requete = """
        SELECT TOP (@plafond) n.VGPRM_ID_VGPRM, n.VGPRM_CODE, n.VGPRM_LIBELLE, w.R_WEBSDOCREG_LIBELLE_ECRAN AS TypeDocument,
               (SELECT COUNT(*) FROM v2.VIDIGED_INDEX AS d WHERE d.VGIDX_NATURE_DOC = n.VGPRM_CODE) AS Documents, COUNT(*) OVER () AS Total
        FROM v2.VIDIGED_PARAMETRAGE AS n LEFT JOIN v2.WEBS_DOC_REGROUPEMENT AS w ON w.R_WEBSDOCREG_VALEUR = n.VGPRM_WEBS_DOC_REGROUPEMENT
        WHERE n.VGPRM_TYPE = 'NATURE_DOC' /*filtres*/
        ORDER BY n.VGPRM_CODE, n.VGPRM_ID_VGPRM
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("n.VGPRM_CODE LIKE @texte OR n.VGPRM_LIBELLE LIKE @texte", ("@texte", FormeLigne.Contient(recherche.Texte)));
        if (recherche.Critere(ColCode) is { } code) f.Ajouter("n.VGPRM_CODE LIKE @code", ("@code", FormeLigne.Contient(code)));
        if (recherche.Critere(ColLibelle) is { } libelle) f.Ajouter("n.VGPRM_LIBELLE LIKE @libelle", ("@libelle", FormeLigne.Contient(libelle)));
        return Lire(baseGreffe, f, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var code = FormeLigne.Texte(r, "VGPRM_CODE");
        var libelle = FormeLigne.Texte(r, "VGPRM_LIBELLE");
        var type = FormeLigne.Texte(r, "TypeDocument");
        var documents = Convert.ToInt32(r["Documents"], System.Globalization.CultureInfo.InvariantCulture);
        var precisions = FormeLigne.Joindre($"code {code}", type, documents > 0 ? $"{documents} document(s)" : "aucun document");
        return new LigneLue(libelle.Length > 0 ? libelle : code, precisions, documents > 0 ? Associees : SansDocument,
            libelle.Length == 0 ? "AFaire" : "Validee", [])
        {
            Id = FormeLigne.Texte(r, "VGPRM_ID_VGPRM"),
            Valeurs = [code, libelle, type],
        };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (CriteresConsultation.Entier(id) is not { } n) return null;
        await using var r = await LectureV2.LireAsync(baseGreffe, """
            SELECT n.VGPRM_CODE, n.VGPRM_LIBELLE, w.R_WEBSDOCREG_LIBELLE_ECRAN AS TypeDocument, n.VGPRM_EXPERIAN, n.VGPRM_EXPERIAN_TRCNC_EP_D,
                   n.VGPRM_EXPERIAN_TRCNC_EP_F, n.VGPRM_EXPERIAN_COFFRE, n.VGPRM_EXPERIAN_GEIDE, n.VGPRM_COPIE_CONFORME_CENTRALISATION,
                   n.VGPRM_PARAMETRE_EDITION_CFEC, n.VGPRM_TRIBUNAL_DIGITAL_NATURE_DOC,
                   (SELECT TOP (1) d.VGPRM_LIBELLE FROM v2.VIDIGED_PARAMETRAGE AS d
                    WHERE d.VGPRM_TYPE = 'NATURE_DOC_DEPOSANT' AND d.VGPRM_CODE = n.VGPRM_CODE ORDER BY d.VGPRM_ID_VGPRM) AS Deposant,
                   (SELECT COUNT(*) FROM v2.VIDIGED_INDEX AS x WHERE x.VGIDX_NATURE_DOC = n.VGPRM_CODE) AS Documents
            FROM v2.VIDIGED_PARAMETRAGE AS n LEFT JOIN v2.WEBS_DOC_REGROUPEMENT AS w ON w.R_WEBSDOCREG_VALEUR = n.VGPRM_WEBS_DOC_REGROUPEMENT
            WHERE n.VGPRM_ID_VGPRM = @id AND n.VGPRM_TYPE = 'NATURE_DOC'
            """, ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        static ChampFiche M(string libelle, string valeur) => new(libelle, valeur) { Modifiable = true };
        static ChampFiche L(string libelle, string valeur) => new(libelle, valeur);
        var code = FormeLigne.Texte(r, "VGPRM_CODE");
        var libelle = FormeLigne.Texte(r, "VGPRM_LIBELLE");
        return new FicheEcran(Procedure, id, libelle.Length > 0 ? libelle : code,
        [
            new("Nature", [L(ColCode, code), M(ColLibelle, libelle), M(ColType, FormeLigne.Texte(r, "TypeDocument"))]),
            new("Paramétrage GED",
            [
                L("Nature Infogreffe", FormeLigne.Texte(r, "VGPRM_EXPERIAN")),
                L("Événement de dépôt (demande)", FormeLigne.Texte(r, "VGPRM_EXPERIAN_TRCNC_EP_D")),
                L("Événement de dépôt (défaut)", FormeLigne.Texte(r, "VGPRM_EXPERIAN_TRCNC_EP_F")),
                L("Dépôt au coffre-fort", FormeLigne.Texte(r, "VGPRM_EXPERIAN_COFFRE")),
                L("Centralisation GEIDE", FormeLigne.Texte(r, "VGPRM_EXPERIAN_GEIDE")),
                L("Copie conforme", FormeLigne.Texte(r, "VGPRM_COPIE_CONFORME_CENTRALISATION")),
                L("Édition supplémentaire au coffre-fort", FormeLigne.Texte(r, "VGPRM_PARAMETRE_EDITION_CFEC")),
                L("Nature Tribunal digital", FormeLigne.Texte(r, "VGPRM_TRIBUNAL_DIGITAL_NATURE_DOC")),
                L("Type de déposant GED", FormeLigne.Texte(r, "Deposant")),
            ]),
            new("Utilisations", [L("Documents GED", $"{FormeLigne.Texte(r, "Documents")} document(s) de cette nature")]),
        ]);
    }
}
