using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Facturation Expresso » (PROC_PARAMEXPRESSO) : les parametres de facturation du greffe, un par ligne, et l'association des modes de
/// paiement a leur banque. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026.
///
/// SOURCE (ancien RIG) : PROC_PARAMEXPRESSO (D:\RigApplication\Source\RIG\DLL\Processus\PROC_PARAMEXPRESSO ; Detail OPE de
/// proc_paramexpresso.md, qui fait foi).
///   - un parametre se lit « PARAMETRAGE d'abord, sinon la colonne de GREFFE_PARAMETRE de meme nom » (MetierParametrage.GetValue,
///     Metier\MetierParametrage.cs:71-92). La V3 a repris PARAMETRAGE dans soc.Reglage (Code, Valeur) et GREFFE_PARAMETRE dans
///     soc.ParametreGreffe (une ligne par colonne) : SOC-003, Serveur\Recopie\ReprisesPhase1.cs:47-50 ;
///   - les cles : TOUTES celles que les operations de la V2 lisent (GetValue), une operation par groupe, dans l'ordre de ses lignes --
///     <see cref="Parametres"/> cite chacune ; puis l'association banque de chaque mode ACTIF, par valeur, groupe « Banques et modes »
///     (MetierBanques, Metier\MetierBanques.cs:38-57 : MODE_PAIEMENT x MODE_PAIEMENT_BANQUE x BANQUE). La grille du deversement
///     comptable (OPE_DEVERSEMENT_COMPTABLE, GridViewDeversementComptable) lit d'autres tables que les parametres : elle n'est pas ici.
/// ETAT : parametre renseigne (ou mode associe a une banque) -> Validee « Enregistré » ; sinon AFaire « À renseigner » (GetValue rend
/// alors une chaine vide, MetierParametrage.cs:77).
/// FICHE (IFicheEcran) : la ligne d'un parametre (Id = sa cle) s'ouvre sur « Paramètre » (cle, valeur, source) ; la valeur n'est
/// MODIFIABLE que pour une cle PARAMETRAGE que la V2 ecrit (<see cref="Ecrite"/>) et que la base du greffe porte (soc.Reglage) : c'est
/// ce qu'EcrivainFacturationExpresso.cs enregistre. Une colonne GREFFE_PARAMETRE (GRPAR_*) se lit seulement : la decision du chef
/// (27/11/2026) n'ouvre aux gestes que la valeur d'un reglage existant. La ligne d'un mode (Id « MODE:<code> ») s'ouvre sur sa banque,
/// en lecture : la meme association s'ecrit par « Modes de paiement » (EcrivainModesPaiement, meme table MODE_PAIEMENT_BANQUE).
/// fix-ok: pas un correctif -- lecture completee (les 37 cles que les operations V2 lisent, mesurees par grep de GetValue/SetValue dans
/// PROC_PARAMEXPRESSO le 27/11/2026 ; l'essai precedent n'en lisait que 17) et fiche ajoutee pour l'ecriture partagee d'un reglage
/// publiee par le lot 14 (5e8d50d, EcritureReglage.cs).
/// </summary>
internal sealed class LecteurFacturationExpresso : IFicheEcran
{
    public string Procedure => "PROC_PARAMEXPRESSO";

    internal const string Generaux = "Paramètres généraux", CorpsFacture = "Corps de facture", PiedDePage = "Pied de page",
        LettresCheques = "Lettres chèques", Judiciaire = "Spécificités judiciaires", Comptabilite = "Comptabilité", Banques = "Banques et modes";

    /// <summary>Les titres de la fiche (ceux du descripteur PROC_PARAMEXPRESSO, Pont/EcransGeneres/Configurer.cs).</summary>
    internal const string SectionParametre = "Paramètre", ChampCle = "Paramètre", ChampValeur = "Valeur", ChampSource = "Source",
        SectionBanque = "Banque du mode", ChampMode = "Mode de paiement", ChampBanque = "Banque";

    /// <summary>L'Id d'une ligne de mode : ce prefixe et le code du mode.</summary>
    internal const string PrefixeMode = "MODE:";

    /// <summary>Le controle de saisie de la V2 pour une cle PARAMETRAGE qu'elle ECRIT (SetValue) ; la forme de la valeur ecrite.</summary>
    internal enum Saisie
    {
        /// <summary>Un montant (_ultMontant…) : ecrit par decimal.ToString() en culture francaise (« 1,30 » dans RIG_DEV).</summary>
        Montant,
        /// <summary>Une case (_ultCheckBox…) : ecrit « 1 » ou « 0 ».</summary>
        Case,
        /// <summary>Un texte libre (_ultText…) : ecrit tel quel.</summary>
        Texte,
        /// <summary>Le choix du pied de page (_ultGroupRadioButtonPiedDePage) : « GREFFE_PARAMETRE » ou « CODE_GREFFE ».</summary>
        PiedDePage,
        /// <summary>Un mode de paiement (ComboBoxModePaiement) : le code d'un mode de MODE_PAIEMENT.</summary>
        Mode,
    }

    /// <summary>
    /// Les cles PARAMETRAGE que les operations de la V2 ECRIVENT (SetValue), et leur controle -- les seules dont la fiche rend la
    /// valeur modifiable. Les GRPAR_* sont des colonnes de GREFFE_PARAMETRE ; EXPRESSO_COMPTA_CLASSE_AUTORISE n'est que lu (MetierCompta.cs:35).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, (Saisie Saisie, string Source)> Ecrite = new Dictionary<string, (Saisie, string)>(StringComparer.Ordinal)
    {
        ["EXPRESSO_RELANCE"] = (Saisie.Montant, "OPE_FACTURATION.cs:37-40, :130"),
        ["EXPRESSO_REMBOURSEMENT"] = (Saisie.Montant, "OPE_FACTURATION.cs:42-45, :126"),
        ["EXPRESSO_EDITIONS_SYNTHETIQUES"] = (Saisie.Case, "OPE_FACTURATION.cs:68-70, :102"),
        ["EXPRESSO_RELEVE_MAIL_ENVOI"] = (Saisie.Texte, "OPE_FACTURATION.cs:72-74, :98"),
        ["EXPRESSO_RELEVE_MAIL_COPIE"] = (Saisie.Texte, "OPE_FACTURATION.cs:76-78, :94"),
        ["EXPRESSO_TYPE_PIED_PAGE"] = (Saisie.PiedDePage, "OPE_PIED_PAGE.cs:22, :54 ; OPE_PIED_PAGE.Designer.cs:108-111"),
        ["EXPRESSO_DEMANDE_TPG_GLOBALE"] = (Saisie.Case, "OPE_SPECIFICITE_JUDICIAIRE.cs:37-39, :110"),
        ["SIREN_GREFFE"] = (Saisie.Texte, "OPE_COMPTABILITE_PARAMETRES_GENERAUX.cs:30-32, :100"),
        ["EXPRESSO_MODE_PAIEMENT_PERTE"] = (Saisie.Mode, "OPE_ASSOCIATION_BANQUES.cs:27-28, :42"),
        ["EXPRESSO_MODE_PAIEMENT_PROFIT"] = (Saisie.Mode, "OPE_ASSOCIATION_BANQUES.cs:29-30, :37"),
    };

    /// <summary>Les parametres lus, par groupe : chaque operation de la V2 et les lignes de ses GetValue.</summary>
    internal static readonly (string Groupe, string Cle)[] Parametres =
    [
        // OPE_FACTURATION.cs:32-76
        (Generaux, "GRPAR_ROMPU"), (Generaux, "EXPRESSO_RELANCE"), (Generaux, "EXPRESSO_REMBOURSEMENT"), (Generaux, "GRPAR_ADRESSE_IS_PRINCIPALE"),
        (Generaux, "GRPAR_COMMENTAIRE_COMPTE"), (Generaux, "GRPAR_COMMENTAIRE_COMPTE_PC"), (Generaux, "GRPAR_COMMENTAIRE_DIVERS"),
        (Generaux, "GRPAR_RELEVE_CLT_IS_SOLDE"), (Generaux, "EXPRESSO_EDITIONS_SYNTHETIQUES"), (Generaux, "EXPRESSO_RELEVE_MAIL_ENVOI"),
        (Generaux, "EXPRESSO_RELEVE_MAIL_COPIE"),
        // OPE_CORPS_FACTURE.cs:17-22 (les deux lignes de chaque mention, OPE_FACTURE_TEXT_2_LINES)
        (CorpsFacture, "GRPAR_PUB_COMPTE_LIGNE1"), (CorpsFacture, "GRPAR_PUB_COMPTE_LIGNE2"), (CorpsFacture, "GRPAR_PUB_DIVERS_LIGNE1"),
        (CorpsFacture, "GRPAR_PUB_DIVERS_LIGNE2"),
        // OPE_PIED_PAGE.cs:18-42
        (PiedDePage, "GRPAR_NUM_TVA_INTRACOMM"), (PiedDePage, "EXPRESSO_TYPE_PIED_PAGE"), (PiedDePage, "GRPAR_GREFFE_ADR_COMPLETE"),
        // OPE_LETTRES_CHEQUES.cs:15-18
        (LettresCheques, "GRPAR_USE_LC_TROP_PERCU"), (LettresCheques, "GRPAR_DUPLICATA_LETTRE_CHEQUE"),
        // OPE_SPECIFICITE_JUDICIAIRE.cs:21-57
        (Judiciaire, "GRPAR_FACTURER_M_AU_ROLE"), (Judiciaire, "GRPAR_EDIT_FACT_COMPTE_PC"), (Judiciaire, "GRPAR_ID_PAYEUR_DEFAUT"),
        (Judiciaire, "GRPAR_GESTION_TP_SIMPLIFIE"), (Judiciaire, "EXPRESSO_DEMANDE_TPG_GLOBALE"), (Judiciaire, "GRPAR_TPG_FONCTION_SIGNATAIRE_ORDONNANCE"),
        (Judiciaire, "GRPAR_TPG_NOM_SIGNATAIRE_ORDONNANCE"), (Judiciaire, "GRPAR_TPG_NOM_GREFFIER"), (Judiciaire, "GRPAR_TPG_USE_SIGNATURE_JC"),
        (Judiciaire, "GRPAR_LIEU_CONTEST_CREANCE"),
        // OPE_COMPTABILITE_PARAMETRES_GENERAUX.cs:17-42, puis MetierCompta.cs:35
        (Comptabilite, "GRPAR_COMPTA_TRESO_USED"), (Comptabilite, "GRPAR_COMPTA_TYPE_COMPTA"), (Comptabilite, "SIREN_GREFFE"),
        (Comptabilite, "GRPAR_COMPTA_BLOCAGE_AUTO"), (Comptabilite, "GRPAR_MODE_COMPTABILISATION"), (Comptabilite, "EXPRESSO_COMPTA_CLASSE_AUTORISE"),
        // OPE_ASSOCIATION_BANQUES.cs:27-30 (les modes de perte et de profit), avant l'association banque de chaque mode
        (Banques, "EXPRESSO_MODE_PAIEMENT_PERTE"), (Banques, "EXPRESSO_MODE_PAIEMENT_PROFIT"),
    ];

    private const string Requete = """
        SELECT TOP (@plafond) l.*, COUNT(*) OVER () AS Total FROM (
            SELECT p.Rang, p.Groupe, p.Cle, COALESCE(r.Valeur, CONVERT(nvarchar(1000), g.Valeur)) AS Valeur,
                   CASE WHEN r.Code IS NOT NULL THEN 'réglage' WHEN g.Colonne IS NOT NULL THEN 'paramètre du greffe' END AS Source
            FROM (VALUES /*parametres*/) AS p (Rang, Groupe, Cle)
            LEFT JOIN soc.Reglage AS r ON r.Code = p.Cle
            LEFT JOIN soc.ParametreGreffe AS g ON g.Colonne = p.Cle
            UNION ALL
            -- MetierBanques : les modes ACTIFS, par valeur, et leur banque (aucune : la banque nulle de la V2, MetierBanques.cs:40-41)
            SELECT 1000, @banques, m.R_MODEPAIEME_VALEUR, b.BANQU_CODE, 'banque du mode ' + m.R_MODEPAIEME_LIBELLE_ECRAN
            FROM v2.MODE_PAIEMENT AS m
            LEFT JOIN v2.MODE_PAIEMENT_BANQUE AS mb ON mb.MODBQ_MODE_PAIEMENT = m.R_MODEPAIEME_VALEUR
            LEFT JOIN v2.BANQUE AS b ON b.BANQU_ID_BANQU = mb.MODBQ_ID_BANQU
            WHERE m.R_MODEPAIEME_UTILISABLE_SAISIE = 1
        ) AS l
        WHERE 1 = 1 /*filtre*/
        ORDER BY l.Rang, l.Cle
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, plafond, null, ct, Ligne);

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        // La meme requete, filtree sur l'Id : la valeur et la source brutes de la ligne (la forme Fiche n'a pas de colonnes).
        string valeur = "", source = "";
        var lues = await Lire(baseGreffe, 1, id, ct, r => { valeur = FormeLigne.Texte(r, "Valeur"); source = FormeLigne.Texte(r, "Source"); return Ligne(r); });
        if (lues.Lignes.FirstOrDefault() is not { } ligne) return null;
        if (id.StartsWith(PrefixeMode, StringComparison.Ordinal))
            return new FicheEcran(Procedure, id, ligne.Nom,
                [new SectionFiche(SectionBanque, [new ChampFiche(ChampMode, id[PrefixeMode.Length..]), new ChampFiche(ChampBanque, valeur)])]);
        // Modifiable : une cle que la V2 ecrit ET que soc.Reglage porte (ni creation, ni GREFFE_PARAMETRE : decision du chef).
        var modifiable = Ecrite.ContainsKey(id) && source == "réglage";
        return new FicheEcran(Procedure, id, ligne.Nom,
            [new SectionFiche(SectionParametre, [new ChampFiche(ChampCle, id), new ChampFiche(ChampValeur, valeur) { Modifiable = modifiable },
                new ChampFiche(ChampSource, source.Length > 0 ? source : "absent de la base du greffe")])]);
    }

    /// <summary>Les lignes ; <paramref name="id"/> non nul : la seule ligne de cet Id (une cle, ou « MODE:<code> »).</summary>
    private static Task<LignesLues> Lire(SqlConnection baseGreffe, int plafond, string? id, CancellationToken ct, Func<SqlDataReader, LigneLue> ligne)
    {
        // La liste des parametres, en parametres SQL (aucune valeur concatenee).
        var valeurs = string.Join(", ", Parametres.Select((p, i) => $"({i}, @g{i}, @c{i})"));
        var filtre = id is null ? "" : id.StartsWith(PrefixeMode, StringComparison.Ordinal) ? "AND l.Rang >= 1000 AND l.Cle = @id" : "AND l.Rang < 1000 AND l.Cle = @id";
        var sql = Requete.Replace("/*parametres*/", valeurs, StringComparison.Ordinal).Replace("/*filtre*/", filtre, StringComparison.Ordinal);
        var parametres = new List<SqlParameter> { new("@plafond", plafond), new("@banques", Banques) };
        if (id is not null)
            parametres.Add(new SqlParameter("@id", id.StartsWith(PrefixeMode, StringComparison.Ordinal) ? id[PrefixeMode.Length..] : id));
        for (var i = 0; i < Parametres.Length; i++)
        {
            parametres.Add(new SqlParameter($"@g{i}", Parametres[i].Groupe));
            parametres.Add(new SqlParameter($"@c{i}", Parametres[i].Cle));
        }
        return LectureEcran.LireAsync(baseGreffe, sql, ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var mode = Convert.ToInt32(r["Rang"], System.Globalization.CultureInfo.InvariantCulture) >= 1000;
        var cle = FormeLigne.Texte(r, "Cle");
        var valeur = FormeLigne.Texte(r, "Valeur");
        var source = FormeLigne.Texte(r, "Source");
        var precisions = mode
            ? FormeLigne.Joindre(source, valeur.Length > 0 ? $"banque {valeur}" : "aucune banque")
            : FormeLigne.Joindre(valeur.Length > 0 ? valeur : "non renseigné", source.Length > 0 ? source : null);
        return new LigneLue(mode ? $"Mode {cle}" : cle, precisions, FormeLigne.Texte(r, "Groupe"), valeur.Length > 0 ? "Validee" : "AFaire", [])
        {
            Id = mode ? PrefixeMode + cle : cle,
        };
    }
}
