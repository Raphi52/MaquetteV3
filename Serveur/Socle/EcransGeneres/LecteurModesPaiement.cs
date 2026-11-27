using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Modes de paiement » (PROC_GESTMODEPAIEMENT, code processus GPAIEMENT) : le referentiel MODE_PAIEMENT du greffe, en grille.
/// Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026. Ajouter, Modifier, Supprimer :
/// EcrivainModesPaiement.cs.
///
/// SOURCE (ancien RIG) : PROC_GESTMODEPAIEMENT (D:\RigApplication\Source\RIG\DLL\Processus\PROC_GESTMODEPAIEMENT ; Detail OPE de
/// proc_gestmodepaiement.md, qui fait foi). La liste : TOUS les modes du greffe, MODE_PAIEMENT.GetAllMODE_PAIEMENT
/// (OPE_MODEPAIEMENTLIST.cs:53-54), sans filtre (la table entiere, TableRef.cs:118-125 : « table + clauseWhere », aucune clause
/// pour MODE_PAIEMENT) ; la V2 ne trie pas (l'ordre du dictionnaire, celui de la cle) : l'ecran V3 trie par code. La grille de la
/// V2 montre le code, le libelle et « actif » (ModePaiementRigDataGridView.cs:15-40) ; le detail lie aussi le type, la banque
/// (MODE_PAIEMENT_BANQUE, :63, :77), « edition du remis visible » et « groupe pour le transfert en comptabilite »
/// (OPE_MODEPAIEMENTDETAIL.cs:65-82).
/// RECHERCHE : le champ cherche le code ou le libelle ; criteres « Code », « Libellé », « Type » (la V2 n'a pas de recherche : sa
/// liste est complete).
/// GROUPES : « Actifs » / « Inactifs » (R_MODEPAIEME_UTILISABLE_SAISIE, la colonne « Actif » de la grille V2).
/// ETAT : libelle de moins de 6 caracteres -> AFaire « À compléter » (la V2 refuse de l'enregistrer ainsi, ControlsValidation,
/// :299-301) ; banque requise mais absente -> EnCours « Banque à associer » (la combo Banque est obligatoire et visible sauf pour
/// le code REG et le couple type DIV / code PV, :70-81) ; sinon Validee « Enregistré ».
/// VALEURS : dans l'ordre des Colonnes du descripteur (Code, Libellé, Type, Banque, Actif, Édition du remis visible, Groupé pour
/// le transfert en comptabilité).
/// </summary>
internal sealed class LecteurModesPaiement : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_GESTMODEPAIEMENT";

    internal const string Actifs = "Actifs", Inactifs = "Inactifs";
    internal const string ColCode = "Code", ColLibelle = "Libellé", ColType = "Type", ColBanque = "Banque", ColActif = "Actif",
                          ColRemis = "Édition du remis visible", ColGroupe = "Groupé pour le transfert en comptabilité";

    /// <summary>Les types de la combo de la V2, au code pres (ULT_COMBO_TYPE_MODE_PAIEMENT.cs:19-25).</summary>
    internal static readonly string[] Types = ["CB", "CH", "DIV", "ES", "RBT", "REG", "VI"];

    /// <summary>La banque n'est pas demandee pour ce mode (OPE_MODEPAIEMENTDETAIL.cs:70) : code REG, ou type DIV et code PV.</summary>
    internal static bool SansBanque(string code, string type) => code == "REG" || (type == "DIV" && code == "PV");

    public IReadOnlyCollection<string> Criteres { get; } = [ColCode, ColLibelle, ColType];

    private const string Requete = """
        SELECT TOP (@plafond) m.R_MODEPAIEME_VALEUR, m.R_MODEPAIEME_LIBELLE_ECRAN, m.R_MODEPAIEME_TYPE, m.R_MODEPAIEME_UTILISABLE_SAISIE,
               m.R_MODEPAIEME_REMIS_VISIBLE, m.R_MODEPAIEME_GROUPE, b.BANQU_CODE, COUNT(*) OVER () AS Total
        FROM v2.MODE_PAIEMENT AS m
        LEFT JOIN v2.MODE_PAIEMENT_BANQUE AS mb ON mb.MODBQ_MODE_PAIEMENT = m.R_MODEPAIEME_VALEUR
        LEFT JOIN v2.BANQUE AS b ON b.BANQU_ID_BANQU = mb.MODBQ_ID_BANQU
        WHERE 1 = 1 /*filtres*/
        ORDER BY m.R_MODEPAIEME_VALEUR
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("(m.R_MODEPAIEME_VALEUR LIKE @texte OR m.R_MODEPAIEME_LIBELLE_ECRAN LIKE @texte)", ("@texte", FormeLigne.Contient(recherche.Texte)));
        if (recherche.Critere(ColCode) is { } code) f.Ajouter("m.R_MODEPAIEME_VALEUR LIKE @code", ("@code", FormeLigne.Contient(code)));
        if (recherche.Critere(ColLibelle) is { } libelle) f.Ajouter("m.R_MODEPAIEME_LIBELLE_ECRAN LIKE @libelle", ("@libelle", FormeLigne.Contient(libelle)));
        if (recherche.Critere(ColType) is { } type) f.Ajouter("m.R_MODEPAIEME_TYPE = @type", ("@type", type.Trim()));
        return Lire(baseGreffe, f, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));

    private static bool Vrai(SqlDataReader r, string colonne) => r[colonne] is not DBNull && Convert.ToBoolean(r[colonne], CultureInfo.InvariantCulture);

    private static LigneLue Ligne(SqlDataReader r)
    {
        var code = FormeLigne.Texte(r, "R_MODEPAIEME_VALEUR");
        var libelle = FormeLigne.Texte(r, "R_MODEPAIEME_LIBELLE_ECRAN");
        var type = FormeLigne.Texte(r, "R_MODEPAIEME_TYPE");
        var banque = FormeLigne.Texte(r, "BANQU_CODE");
        var actif = Vrai(r, "R_MODEPAIEME_UTILISABLE_SAISIE");
        var remis = Vrai(r, "R_MODEPAIEME_REMIS_VISIBLE");
        var groupe = Vrai(r, "R_MODEPAIEME_GROUPE");
        // La regle d'etat (en tete du fichier).
        var sansBanque = SansBanque(code, type);
        var etat = libelle.Length < 6 ? "AFaire" : !sansBanque && banque.Length == 0 ? "EnCours" : "Validee";
        var precisions = FormeLigne.Joindre($"code {code}", type.Length > 0 ? $"type {type}" : null,
            sansBanque ? "sans banque" : banque.Length > 0 ? $"banque {banque}" : "banque à associer",
            libelle.Length < 6 ? "libellé trop court" : null, actif ? null : "inactif");
        return new LigneLue(libelle.Length > 0 ? libelle : code, precisions, actif ? Actifs : Inactifs, etat, [])
        {
            Id = code,
            Valeurs = [code, libelle, type, banque, FormeLigne.Case(actif), FormeLigne.Case(remis), FormeLigne.Case(groupe)],
        };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        await using var r = await LectureV2.LireAsync(baseGreffe, """
            SELECT m.R_MODEPAIEME_VALEUR, m.R_MODEPAIEME_LIBELLE_ECRAN, m.R_MODEPAIEME_TYPE, m.R_MODEPAIEME_UTILISABLE_SAISIE,
                   m.R_MODEPAIEME_REMIS_VISIBLE, m.R_MODEPAIEME_GROUPE, b.BANQU_CODE, b.BANQU_NOM,
                   (SELECT COUNT(*) FROM v2.ENCAISSEMENT AS e WHERE e.ENCMT_MODE_PAIEMENT = m.R_MODEPAIEME_VALEUR) AS Encaissements
            FROM v2.MODE_PAIEMENT AS m
            LEFT JOIN v2.MODE_PAIEMENT_BANQUE AS mb ON mb.MODBQ_MODE_PAIEMENT = m.R_MODEPAIEME_VALEUR
            LEFT JOIN v2.BANQUE AS b ON b.BANQU_ID_BANQU = mb.MODBQ_ID_BANQU
            WHERE m.R_MODEPAIEME_VALEUR = @id
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        var code = FormeLigne.Texte(r, "R_MODEPAIEME_VALEUR");
        var libelle = FormeLigne.Texte(r, "R_MODEPAIEME_LIBELLE_ECRAN");
        var type = FormeLigne.Texte(r, "R_MODEPAIEME_TYPE");
        var encaissements = Convert.ToInt32(r["Encaissements"], CultureInfo.InvariantCulture);
        // Code et type en lecture seule des qu'un encaissement porte le code, et pour PV (ControlsUltReadOnly, :322-324).
        var verrouille = encaissements > 0 || code == "PV";
        static ChampFiche M(string libelle, string valeur, bool modifiable = true) => new(libelle, valeur) { Modifiable = modifiable };
        var banque = FormeLigne.Texte(r, "BANQU_CODE");
        return new FicheEcran(Procedure, code, libelle.Length > 0 ? libelle : code,
        [
            // Le code est l'identite de la ligne : il ne se change pas dans la grille (EcrivainModesPaiement, en tete).
            new("Code", [M(ColCode, code, modifiable: false)]),
            new("Libellé", [M(ColLibelle, libelle)]),
            new("Type et banque", [M(ColType, type, modifiable: !verrouille),
                                   M(ColBanque, SansBanque(code, type) ? "" : banque.Length > 0 ? banque : "Aucune", modifiable: !SansBanque(code, type))]),
            new("Options", [M(ColActif, FormeLigne.Case(Vrai(r, "R_MODEPAIEME_UTILISABLE_SAISIE"))), M(ColRemis, FormeLigne.Case(Vrai(r, "R_MODEPAIEME_REMIS_VISIBLE"))),
                            M(ColGroupe, FormeLigne.Case(Vrai(r, "R_MODEPAIEME_GROUPE")))]),
            new("Utilisation", [new ChampFiche("Encaissements", encaissements == 0 ? "aucun : le mode peut être supprimé"
                : $"{encaissements} encaissement(s) : code et type verrouillés, suppression impossible")]),
        ]);
    }
}
