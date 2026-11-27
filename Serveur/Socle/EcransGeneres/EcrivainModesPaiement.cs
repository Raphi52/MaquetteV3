using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurModesPaiement;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Modes de paiement » (PROC_GESTMODEPAIEMENT) : AJOUTER, MODIFIER et SUPPRIMER un mode de paiement, dans la grille. Mode d'emploi :
/// EcrivainEcran.cs ; les lignes : LecteurModesPaiement.cs. Plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026.
///
/// SOURCE (ancien RIG) : OPE_MODEPAIEMENTDETAIL (D:\RigApplication\Source\RIG\DLL\Processus\PROC_GESTMODEPAIEMENT), Valider (l. 204-240),
/// Supprimer (l. 246-272), ControlsValidation (l. 279-315), ControlsUltReadOnly (l. 322-328), _getRequete (l. 336-360) ; l'entite
/// MODE_PAIEMENT (Outils\DLL\RigMetier\TableRef\MODE_PAIEMENT.cs:56-73) ; messages au mot pres, leurs « \n » en blancs.
///   - code obligatoire, 5 caracteres au plus ; libelle et type obligatoires (MetierObligatoire / MaxLength, Designer et Detail OPE
///     :65-69) ; le type est un code de la combo (ULT_COMBO_TYPE_MODE_PAIEMENT.cs:19-25) ; la banque, un code de BANQUE ou « Aucune »
///     (la combo, :77), sauf pour un mode sans banque (code REG, ou type DIV et code PV, :70) ;
///   - ControlsValidation, message cumule « Erreur à la création : - ... » (le meme titre en modification, comme la V2) : code deja
///     porte, libelle de moins de 6 caracteres, libelle deja porte par un autre mode ;
///   - CREATION : actif par defaut (CreateModePaiement, MODE_PAIEMENT.cs:58), « groupe » faux pour les types RBT et VI, vrai sinon
///     (ult_ComboBoxType_ChangementValeur, :177-181) quand l'ecran ne l'envoie pas ;
///   - toute sauvegarde reecrit ISO, synonyme et abrege d'edition = le code, les trois libelles d'edition = le libelle, la date et
///     l'auteur de creation = maintenant et l'agent (MODE_PAIEMENT.Sauve, :63-72 : la V2 le fait aussi en modification) ;
///   - le lien banque (MODE_PAIEMENT_BANQUE, _getRequete) : « Aucune » le supprime, sinon il est cree ou mis a jour ;
///   - MODIFICATION : le type ne change plus des qu'un encaissement porte le code, ni pour PV (ControlsUltReadOnly) ;
///   - SUPPRESSION refusee si un encaissement porte le code (l. 250-251) ; sinon le lien banque puis le mode (l. 255-259),
///     « Suppression réussie » (l. 264).
/// NON REPRIS (nommes) : le CHANGEMENT DE CODE d'un mode enregistre -- la V2 le fait par sauvegarde puis suppression de l'ancien
/// (l. 222-236), en comparant un ISO a un code (bug releve par la fiche) : ici le code est l'identite de la ligne, refuse avec un
/// message qui dit de supprimer puis d'ajouter ; la demande « GPAIEMENT » creee et cloturee d'un coup a chaque geste (l. 213, 227,
/// 262) -- une trace, que le journal des gestes remplace (decision du plan, socle-courriels.md:270).
/// TRACE : famille « reglage », « a cree » / « a modifie » / « a supprime ».
/// </summary>
internal sealed class EcrivainModesPaiement : IEcrivainEcran
{
    public string Procedure => "PROC_GESTMODEPAIEMENT";
    public string Ecran => "Modes de paiement";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Modifier, TypeGeste.Supprimer];

    internal const string TitreErreurs = "Erreur à la création :";
    internal const string CodeUtilise = "- Le code est déjà utilisé.";
    internal const string LibelleCourt = "- Le libellé saisi est trop court. Il doit faire au moins 6 caractères.";
    internal const string LibelleUtilise = "- Le libellé est déjà utilisé.";
    internal const string AssocieEncaissements = "Ce mode de paiement est actuellement associé à un ou plusieurs encaissements";
    internal const string Aucune = "Aucune";

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct) => geste.Demande.Type switch
    {
        TypeGeste.Ajouter => AjouterAsync(geste, ct),
        TypeGeste.Supprimer => SupprimerAsync(geste, ct),
        _ => ModifierAsync(geste, ct),
    };

    private sealed record Mode(string Code, string Libelle, string Type, string Banque, bool Actif, bool Remis, bool Groupe);

    private static string? Cellule(GesteEnCours geste, string colonne) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v.Trim() : null;

    private static bool? Case(string? valeur) => valeur is null ? null : valeur == "oui";

    /// <summary>Les champs obligatoires et la combo (l'ecran les tient a la V2 ; ici avant toute lecture).</summary>
    private static string? Obligatoires(Mode m)
    {
        if (m.Code.Length == 0) return $"« {ColCode} » est obligatoire.";
        if (m.Code.Length > 5) return $"« {ColCode} » : 5 caractères au plus ({m.Code}).";
        if (m.Libelle.Length == 0) return $"« {ColLibelle} » est obligatoire.";
        if (m.Libelle.Length > 35) return $"« {ColLibelle} » : 35 caractères au plus.";
        if (m.Type.Length == 0) return $"« {ColType} » est obligatoire.";
        if (!Types.Contains(m.Type)) return $"« {ColType} » : {m.Type} n'est pas un type de la liste ({string.Join(", ", Types)}).";
        if (!SansBanque(m.Code, m.Type) && m.Banque.Length == 0) return $"« {ColBanque} » est obligatoire (un code de banque, ou « {Aucune} »).";
        return null;
    }

    /// <summary>ControlsValidation (l. 279-315) : le message cumule de la V2, ou null.</summary>
    private static async Task<string?> ControlesAsync(GesteEnCours geste, Mode m, bool creation, CancellationToken ct)
    {
        var erreurs = new List<string>();
        // Unicite du code : en creation seulement (en modification le code ne change pas, voir l'en-tete).
        if (creation && await CompterAsync(geste, "SELECT COUNT(*) FROM v2.MODE_PAIEMENT WHERE R_MODEPAIEME_VALEUR = @v", ("@v", m.Code), ct) > 0)
            erreurs.Add(CodeUtilise);
        if (m.Libelle.Length < 6) erreurs.Add(LibelleCourt);
        // Le libelle porte par un AUTRE mode (ISO different, l. 304) : l'ISO vaut le code (Sauve, MODE_PAIEMENT.cs:64).
        else if (await CompterAsync(geste, "SELECT COUNT(*) FROM v2.MODE_PAIEMENT WHERE R_MODEPAIEME_LIBELLE_ECRAN = @l AND R_MODEPAIEME_ISO <> @v",
                     ("@l", m.Libelle), ct, ("@v", m.Code)) > 0)
            erreurs.Add(LibelleUtilise);
        return erreurs.Count == 0 ? null : $"{TitreErreurs} {string.Join(" ", erreurs)}";
    }

    private static async Task<int> CompterAsync(GesteEnCours geste, string sql, (string Nom, object Valeur) p, CancellationToken ct,
                                                (string Nom, object Valeur)? p2 = null)
    {
        var parametres = new List<SqlParameter> { new(p.Nom, p.Valeur) };
        if (p2 is { } autre) parametres.Add(new SqlParameter(autre.Nom, autre.Valeur));
        await using var r = await geste.LireAsync(sql, ct, [.. parametres]);
        await r.ReadAsync(ct);
        return r.GetInt32(0);
    }

    private static Task<int> EncaissementsAsync(GesteEnCours geste, string code, CancellationToken ct)
        => CompterAsync(geste, "SELECT COUNT(*) FROM v2.ENCAISSEMENT WHERE ENCMT_MODE_PAIEMENT = @v", ("@v", code), ct);

    /// <summary>L'id de la banque choisie (« SELECT BANQU_ID_BANQU FROM BANQUE WHERE BANQU_CODE = », l. 339) ; null : « Aucune ».</summary>
    private static async Task<(int? Id, string? Refus)> BanqueAsync(GesteEnCours geste, Mode m, CancellationToken ct)
    {
        if (SansBanque(m.Code, m.Type) || m.Banque == Aucune) return (null, null);
        await using var r = await geste.LireAsync("SELECT BANQU_ID_BANQU FROM v2.BANQUE WHERE BANQU_CODE = @c", ct, new SqlParameter("@c", m.Banque));
        return await r.ReadAsync(ct) ? (r.GetInt32(0), null) : (null, $"« {ColBanque} » : la banque {m.Banque} n'existe pas dans la base du greffe.");
    }

    /// <summary>Le nom de l'agent, comme UTLST_NOM de la V2 (MODE_PAIEMENT.cs:71) : soc.Compte.Nom, 16 caracteres (la colonne).</summary>
    private static async Task<string> AuteurAsync(GesteEnCours geste, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT Nom FROM soc.Compte WHERE CompteWindows = @c", ct, new SqlParameter("@c", geste.Agent.CompteWindows));
        var nom = await r.ReadAsync(ct) && r[0] is string s && s.Trim().Length > 0 ? s.Trim() : geste.Agent.CompteWindows;
        return nom.Length > 16 ? nom[..16] : nom;
    }

    /// <summary>_getRequete (l. 336-360) : le lien banque supprime (« Aucune »), cree ou mis a jour.</summary>
    private static async Task LierBanqueAsync(GesteEnCours geste, Mode m, int? idBanque, CancellationToken ct)
    {
        if (idBanque is not { } id)
        {
            await geste.EcrireAsync("DELETE FROM v2.MODE_PAIEMENT_BANQUE WHERE MODBQ_MODE_PAIEMENT = @v", ct, new SqlParameter("@v", m.Code));
            return;
        }
        var maj = await geste.EcrireAsync("UPDATE v2.MODE_PAIEMENT_BANQUE SET MODBQ_ID_BANQU = @b WHERE MODBQ_MODE_PAIEMENT = @v", ct,
            new SqlParameter("@b", id), new SqlParameter("@v", m.Code));
        if (maj == 0)
            await geste.EcrireAsync("INSERT INTO v2.MODE_PAIEMENT_BANQUE (MODBQ_MODE_PAIEMENT, MODBQ_ID_BANQU) VALUES (@v, @b)", ct,
                new SqlParameter("@v", m.Code), new SqlParameter("@b", id));
    }

    /// <summary>Les colonnes que MODE_PAIEMENT.Sauve reecrit a chaque sauvegarde (MODE_PAIEMENT.cs:64-71).</summary>
    private static SqlParameter[] Sauve(Mode m, string auteur, DateTime maintenant) =>
    [
        new("@v", m.Code), new("@l", m.Libelle), new("@t", m.Type), new("@actif", m.Actif), new("@remis", m.Remis), new("@groupe", m.Groupe),
        new("@maintenant", maintenant), new("@auteur", auteur),
    ];

    private static async Task<IssueGeste> AjouterAsync(GesteEnCours geste, CancellationToken ct)
    {
        var type = Cellule(geste, ColType) ?? "";
        var m = new Mode(Cellule(geste, ColCode) ?? "", Cellule(geste, ColLibelle) ?? "", type, Cellule(geste, ColBanque) ?? "",
            Case(Cellule(geste, ColActif)) ?? true, Case(Cellule(geste, ColRemis)) ?? false,
            Case(Cellule(geste, ColGroupe)) ?? type is not ("RBT" or "VI"));
        if (Obligatoires(m) is { } manque) return IssueGeste.Refuser(manque);
        if (await ControlesAsync(geste, m, creation: true, ct) is { } erreurs) return IssueGeste.Refuser(erreurs);
        var (idBanque, refusBanque) = await BanqueAsync(geste, m, ct);
        if (refusBanque is not null) return IssueGeste.Refuser(refusBanque);
        await geste.EcrireAsync("""
            INSERT INTO v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR, R_MODEPAIEME_LIBELLE_ECRAN, R_MODEPAIEME_ISO, R_MODEPAIEME_SYNONYME,
                R_MODEPAIEME_ABREGE_EDIT, R_MODEPAIEME_SIMPLIFIE_EDIT, R_MODEPAIEME_DEVELOPPE_EDIT, R_MODEPAIEME_EXHAUSTIF_EDIT,
                R_MODEPAIEME_DATE_CREATION, R_MODEPAIEME_AUTEUR_CREATION, R_MODEPAIEME_UTILISABLE_SAISIE, R_MODEPAIEME_GROUPE,
                R_MODEPAIEME_REMIS_VISIBLE, R_MODEPAIEME_TYPE)
            VALUES (@v, @l, @v, @v, @v, @l, @l, @l, @maintenant, @auteur, @actif, @groupe, @remis, @t)
            """, ct, Sauve(m, await AuteurAsync(geste, ct), geste.Maintenant));
        await LierBanqueAsync(geste, m, idBanque, ct);
        return IssueGeste.Ecrit($"Mode de paiement {m.Code} créé : {m.Libelle}.", [m.Code],
            new TraceGeste("reglage", $"le mode de paiement {m.Code}", m.Code, "a cree", null, m));
    }

    private static async Task<IssueGeste> ModifierAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (await LireAsync(geste, id, ct) is not { } avant) return IssueGeste.Refuser($"Mode de paiement introuvable dans la base du greffe ({id}).");
        if (Cellule(geste, ColCode) is { } nouveauCode && nouveauCode != avant.Code)
            return IssueGeste.Refuser($"Le code d'un mode de paiement enregistré ne se change pas ici : RIG v2 le change par une sauvegarde puis une suppression de l'ancien (OPE_MODEPAIEMENTDETAIL.cs:234-235). Supprimez {avant.Code} puis ajoutez {nouveauCode}.");
        var apres = avant with
        {
            Libelle = Cellule(geste, ColLibelle) ?? avant.Libelle, Type = Cellule(geste, ColType) ?? avant.Type,
            Banque = Cellule(geste, ColBanque) ?? avant.Banque, Actif = Case(Cellule(geste, ColActif)) ?? avant.Actif,
            Remis = Case(Cellule(geste, ColRemis)) ?? avant.Remis, Groupe = Case(Cellule(geste, ColGroupe)) ?? avant.Groupe,
        };
        if (apres == avant) return IssueGeste.Refuser("Rien à enregistrer : aucune valeur ne change.");
        if (Obligatoires(apres) is { } manque) return IssueGeste.Refuser(manque);
        // ControlsUltReadOnly (l. 322-328) : type verrouille des qu'un encaissement porte le code, et pour PV.
        if (apres.Type != avant.Type && (avant.Code == "PV" || await EncaissementsAsync(geste, avant.Code, ct) > 0))
            return IssueGeste.Refuser($"Le type du mode {avant.Code} ne se modifie plus : {(avant.Code == "PV" ? "le mode PV est réservé" : "des encaissements l'utilisent")} (RIG v2 le met en lecture seule).");
        if (await ControlesAsync(geste, apres, creation: false, ct) is { } erreurs) return IssueGeste.Refuser(erreurs);
        var (idBanque, refusBanque) = await BanqueAsync(geste, apres, ct);
        if (refusBanque is not null) return IssueGeste.Refuser(refusBanque);
        await geste.EcrireAsync("""
            UPDATE v2.MODE_PAIEMENT SET R_MODEPAIEME_LIBELLE_ECRAN = @l, R_MODEPAIEME_ISO = @v, R_MODEPAIEME_SYNONYME = @v,
                R_MODEPAIEME_ABREGE_EDIT = @v, R_MODEPAIEME_SIMPLIFIE_EDIT = @l, R_MODEPAIEME_DEVELOPPE_EDIT = @l, R_MODEPAIEME_EXHAUSTIF_EDIT = @l,
                R_MODEPAIEME_DATE_CREATION = @maintenant, R_MODEPAIEME_AUTEUR_CREATION = @auteur, R_MODEPAIEME_UTILISABLE_SAISIE = @actif,
                R_MODEPAIEME_GROUPE = @groupe, R_MODEPAIEME_REMIS_VISIBLE = @remis, R_MODEPAIEME_TYPE = @t
            WHERE R_MODEPAIEME_VALEUR = @v
            """, ct, Sauve(apres, await AuteurAsync(geste, ct), geste.Maintenant));
        // Le lien banque n'est reecrit que s'il est demande (un mode sans banque n'en a pas, :70-72).
        if (!SansBanque(apres.Code, apres.Type) && apres.Banque != avant.Banque) await LierBanqueAsync(geste, apres, idBanque, ct);
        return IssueGeste.Ecrit($"Mode de paiement {apres.Code} enregistré.", [apres.Code],
            new TraceGeste("reglage", $"le mode de paiement {apres.Code}", apres.Code, "a modifie", avant, apres));
    }

    private static async Task<IssueGeste> SupprimerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (await LireAsync(geste, id, ct) is not { } m) return IssueGeste.Refuser($"Mode de paiement introuvable dans la base du greffe ({id}).");
        if (await EncaissementsAsync(geste, m.Code, ct) > 0) return IssueGeste.Refuser(AssocieEncaissements);
        await geste.EcrireAsync("DELETE FROM v2.MODE_PAIEMENT_BANQUE WHERE MODBQ_MODE_PAIEMENT = @v", ct, new SqlParameter("@v", m.Code));
        await geste.EcrireAsync("DELETE FROM v2.MODE_PAIEMENT WHERE R_MODEPAIEME_VALEUR = @v", ct, new SqlParameter("@v", m.Code));
        return IssueGeste.Ecrit($"Suppression réussie : le mode de paiement {m.Code} ({m.Libelle}) est supprimé.", [m.Code],
            new TraceGeste("reglage", $"le mode de paiement {m.Code}", m.Code, "a supprime", m, null));
    }

    private static async Task<Mode?> LireAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT m.R_MODEPAIEME_VALEUR, m.R_MODEPAIEME_LIBELLE_ECRAN, m.R_MODEPAIEME_TYPE, m.R_MODEPAIEME_UTILISABLE_SAISIE,
                   m.R_MODEPAIEME_REMIS_VISIBLE, m.R_MODEPAIEME_GROUPE, b.BANQU_CODE
            FROM v2.MODE_PAIEMENT AS m WITH (UPDLOCK)
            LEFT JOIN v2.MODE_PAIEMENT_BANQUE AS mb ON mb.MODBQ_MODE_PAIEMENT = m.R_MODEPAIEME_VALEUR
            LEFT JOIN v2.BANQUE AS b ON b.BANQU_ID_BANQU = mb.MODBQ_ID_BANQU
            WHERE m.R_MODEPAIEME_VALEUR = @v
            """, ct, new SqlParameter("@v", code));
        if (!await r.ReadAsync(ct)) return null;
        static bool B(SqlDataReader r, string c) => r[c] is not DBNull && Convert.ToBoolean(r[c], CultureInfo.InvariantCulture);
        var c = FormeLigne.Texte(r, "R_MODEPAIEME_VALEUR");
        var t = FormeLigne.Texte(r, "R_MODEPAIEME_TYPE");
        var banque = FormeLigne.Texte(r, "BANQU_CODE");
        return new Mode(c, FormeLigne.Texte(r, "R_MODEPAIEME_LIBELLE_ECRAN"), t, SansBanque(c, t) ? "" : banque.Length > 0 ? banque : Aucune,
            B(r, "R_MODEPAIEME_UTILISABLE_SAISIE"), B(r, "R_MODEPAIEME_REMIS_VISIBLE"), B(r, "R_MODEPAIEME_GROUPE"));
    }
}
