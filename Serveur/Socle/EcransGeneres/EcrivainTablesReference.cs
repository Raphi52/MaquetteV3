using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurTablesReference;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Tables de référence » (PROC_GTABREF) : MODIFIER, AJOUTER et SUPPRIMER un code, dans la grille. Mode d'emploi : EcrivainEcran.cs ;
/// les lignes et les tables : LecteurTablesReference.cs. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_GTABREF\PROC_TABREF_DLL -- la barre de boutons
/// (Operations\OPE_TABLE_REF_BUTTONS_BAR.cs) et le formulaire de chaque table ; messages au mot pres :
///   - MODIFIER (_Save, l. 421-462, puis SQLBase_Handler.Sauve) : le libelle ecran et « Utilisable en saisie », les deux champs que les
///     trois formulaires lient (OPE_AFFICHE_SQLBASE_PC_ETAT.cs:71,81 ; _PC_ETAT_ZONE.cs:76,79 ; _DEMAT_CFEC_AJMJ_TYPE_DEPOT.cs:45,47).
///     Pour PC_ETAT_ZONE et le type de depot AJMJ, la date et l'auteur de modification (« Nom Prénom » de l'utilisateur, sinon le
///     compte Windows : SousOPE_HISTORIQUE.MiseAJourChampsModification, l. 49-57). Le CODE ne se renomme pas : en changer, dans la V2,
///     ouvre l'enregistrement de ce code (ult_ComboBox_Valeur_ChangementValeur -> InvokeValeurChampPkModifiee,
///     OPE_AFFICHE_SQLBASE_PC_ETAT.cs:169-203) ; la V3 le refuse et le dit. La table d'un code ne change pas non plus.
///   - AJOUTER : un ETAT DE PC seulement. PC_ETAT.CreateEtat(valeur, libelle, "False", "False") (RigMetier\TableRef\PC_ETAT.cs:168-188 :
///     ouverture et cloture faux, mise a jour de la PC vrai, les autres cases a leur defaut de RigBaseGreffe\GREFFE\PC_ETAT.cs) ; refus d'un
///     code deja porte (« Un enregistrement nommé X existe déjà. Il n'est pas possible de créer un nouvel enregistrement avec ce nom. »,
///     l. 182-183) et d'un code trop long (DoVerifier, l. 205-215). Le type de depot AJMJ : RIG v2 n'en cree pas (NouvelEnregistrement =
///     false, FactoryHelper.cs:86-88) ; la zone : refusee et nommee ci-dessous.
///   - SUPPRIMER (_Delete, l. 491-512 ; SQLBase_Handler.Delete(true), l. 87-100) : les collections liees d'un etat de PC d'abord (etats
///     suivants PC_ETAT_SUIVANT.PETSV_AVANT, decisions PC_ETAT_DJ_EP, saisie PC_ETAT_SAISIE : OPE_AFFICHE_TABLE_REF_PC_ETAT.cs:34), puis
///     l'enregistrement. Un code CITE AILLEURS fait echouer la suppression sur les cles etrangeres de RIG_DEV
///     (Source\BaseDeDonnées\RigDatabase\dbo\Tables : PC_ETAT_HISTO.PETHT_PCETAT et PC_ETAT_SUIVANT.PETSV_APRES -> PC_ETAT ;
///     PC_ETAT_SAISIE.PETSA_PC_ETAT_ZONE -> PC_ETAT_ZONE ; DEMAT_CFEC_AJMJ.CFECAJ_DEMAT_CFEC_AJMJ_TYPE_DEPOT -> DEMAT_CFEC_AJMJ_TYPE_DEPOT),
///     et la V2 dit alors « La suppression de l'enregistrement courant a échoué. » (l. 506) : la V3 controle ces citations AVANT d'ecrire
///     (la base V3 n'a pas recopie les cles etrangeres), refuse avec ce message et nomme la citation.
/// NON REPRIS (nommes) : l'AJOUT d'une zone -- PC_ETAT_ZONE exige son synonyme, son code ISO, ses textes d'edition simplifie et developpe,
/// sa table, son attribut, son type et son etat (NOT NULL, RigBaseGreffe\GREFFE\PC_ETAT_ZONE.cs), que la grille ne porte pas ; les autres
/// champs des formulaires (regime, cases d'ouverture / cloture / conversion / extension et leurs exclusions, textes d'edition, evenement de
/// procedure) et les collections d'un etat de PC (etats suivants, decisions, saisie) : la fiche les montre en lecture ; « Dupliquer »
/// (SQLBase_Handler.Copie) ; le verrou SqlLock de l'enregistrement ouvert (_CheckLock).
/// TRACE : famille « reglage », « a cree » / « a modifie » / « a supprime ».
/// </summary>
// fix-ok: 27/11/2026 -- les deux retouches apres la creation remplacent un motif positionnel sur le tuple nullable de Id() (« is not var
// (t, x) », ecrit avant toute compilation, dont la reaction a un id illisible n'etait pas verifiee) par la forme du depot « is not { } cle » ;
// compiler.ps1 = 0 ensuite, et les refus « Code introuvable » sont joues par le banc.
internal sealed class EcrivainTablesReference : IEcrivainEcran
{
    public string Procedure => "PROC_GTABREF";
    public string Ecran => "Tables de référence";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Modifier, TypeGeste.Supprimer];

    /// <summary>OPE_TABLE_REF_BUTTONS_BAR._Delete, l. 506.</summary>
    internal const string SuppressionEchouee = "La suppression de l'enregistrement courant a échoué.";

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct) => geste.Demande.Type switch
    {
        TypeGeste.Ajouter => AjouterAsync(geste, ct),
        TypeGeste.Supprimer => SupprimerAsync(geste, ct),
        _ => ModifierAsync(geste, ct),
    };

    private sealed record Code(string Table, string Valeur, string Libelle, bool Saisie);

    private static string? Cellule(GesteEnCours geste, string colonne) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v.Trim() : null;

    private static bool? Coche(string? valeur) => valeur is null ? null : valeur.ToLowerInvariant() is "oui" or "true" or "1";

    private static async Task<Code?> LireAsync(GesteEnCours geste, TableRef t, string valeur, CancellationToken ct)
    {
        await using var r = await geste.LireAsync($"""
            SELECT {t.Valeur}, ISNULL({t.Libelle}, ''), CAST({t.Saisie} AS bit) FROM v2.{t.Nom} WITH (UPDLOCK) WHERE {t.Valeur} = @c
            """, ct, new SqlParameter("@c", valeur));
        return await r.ReadAsync(ct) ? new Code(t.Liste, r.GetString(0).Trim(), r.GetString(1).Trim(), !r.IsDBNull(2) && r.GetBoolean(2)) : null;
    }

    /// <summary>Le libelle et sa longueur (RigBaseGreffe) ; null s'il est bon.</summary>
    private static string? VerifierLibelle(TableRef t, string libelle)
        => libelle.Length == 0 ? CreationV2.Obligatoire(ColLibelle)
         : libelle.Length > t.LongueurLibelle ? $"« {ColLibelle} » : {t.LongueurLibelle} caractères au plus." : null;

    /// <summary>L'auteur de l'historique (SousOPE_HISTORIQUE, l. 51-56) : « Nom Prénom » de l'agent, sinon son compte Windows.</summary>
    private static async Task<string> AuteurAsync(GesteEnCours geste, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT CONCAT(Nom, ' ', Prenom) FROM soc.Compte WHERE CompteWindows = @c", ct,
            new SqlParameter("@c", geste.Agent.CompteWindows));
        return await r.ReadAsync(ct) && !r.IsDBNull(0) && r.GetString(0).Trim().Length > 0 ? r.GetString(0).Trim() : geste.Agent.CompteWindows;
    }

    private static async Task<IssueGeste> ModifierAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (Id(id) is not { } cle || await LireAsync(geste, cle.Table, cle.Code, ct) is not { } avant)
            return IssueGeste.Refuser($"Code introuvable dans les tables de référence du greffe ({id}).");
        var t = cle.Table;
        if (Cellule(geste, ColCode) is { } code && code != avant.Valeur)
            return IssueGeste.Refuser($"Le code {avant.Valeur} ne se renomme pas : dans RIG v2, saisir un autre code ouvre l'enregistrement de ce code.");
        if (Cellule(geste, ColTable) is { Length: > 0 } table && Table(table) != t)
            return IssueGeste.Refuser($"Un code reste dans sa table ({t.Liste}).");
        var apres = avant with { Libelle = Cellule(geste, ColLibelle) ?? avant.Libelle, Saisie = Coche(Cellule(geste, ColSaisie)) ?? avant.Saisie };
        if (apres == avant) return IssueGeste.Refuser("Rien à enregistrer : aucune valeur ne change.");
        if (VerifierLibelle(t, apres.Libelle) is { } refus) return IssueGeste.Refuser(refus);
        var historique = t.DateModif is null ? "" : $", {t.DateModif} = @date, {t.AuteurModif} = @auteur";
        await geste.EcrireAsync($"UPDATE v2.{t.Nom} SET {t.Libelle} = @l, {t.Saisie} = @s{historique} WHERE {t.Valeur} = @c", ct,
            new SqlParameter("@l", apres.Libelle), new SqlParameter("@s", apres.Saisie), new SqlParameter("@c", avant.Valeur),
            new SqlParameter("@date", geste.Maintenant), new SqlParameter("@auteur", await AuteurAsync(geste, ct)));
        return IssueGeste.Ecrit($"{t.Liste} {avant.Valeur} enregistré : {apres.Libelle}.", [id],
            new TraceGeste("reglage", $"le code {avant.Valeur} de {t.Nom}", id, "a modifie", avant, apres));
    }

    private static async Task<IssueGeste> AjouterAsync(GesteEnCours geste, CancellationToken ct)
    {
        var saisieTable = Cellule(geste, ColTable) ?? "";
        if (saisieTable.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ColTable));
        if (Table(saisieTable) is not { } t)
            return IssueGeste.Refuser($"« {ColTable} » : « {saisieTable} » n'est pas une table que RIG v2 ouvre ({string.Join(", ", Tables.Select(x => x.Liste))}).");
        if (!t.Creation)
            return IssueGeste.Refuser($"RIG v2 ne crée pas d'enregistrement dans {t.Liste} : la table s'ouvre sans « Nouvel enregistrement ».");
        if (t.Nom == "PC_ETAT_ZONE")
            return IssueGeste.Refuser("Une zone se crée avec son synonyme, son code ISO, ses textes d'édition, sa table, son attribut, son type et son état, "
                                      + "que la grille ne porte pas : RIG v3 ne la crée pas encore.");
        var valeur = Cellule(geste, ColCode) ?? "";
        if (valeur.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ColCode));
        if (valeur.Length > t.LongueurCode) return IssueGeste.Refuser($"Longueur champ \"Valeur\" trop grande ({t.LongueurCode} max).");
        if (await LireAsync(geste, t, valeur, ct) is not null)
            return IssueGeste.Refuser($"Un enregistrement nommé {valeur} existe déjà. Il n'est pas possible de créer un nouvel enregistrement avec ce nom.");
        var libelle = Cellule(geste, ColLibelle) ?? "";
        if (VerifierLibelle(t, libelle) is { } refus) return IssueGeste.Refuser(refus);
        var saisie = Coche(Cellule(geste, ColSaisie)) ?? true;
        // PC_ETAT.CreateEtat(valeur, libelle, "False", "False") : ouverture et cloture faux, MAJ_PC vrai ; les autres cases a leur defaut
        // (RigBaseGreffe\GREFFE\PC_ETAT.cs : resolution du plan, audience future, conversion LJ, extension faux ; a sauver vrai).
        await geste.EcrireAsync("""
            INSERT INTO v2.PC_ETAT (R_PCETAT_VALEUR, R_PCETAT_LIBELLE_ECRAN, R_PCETAT_OUVERTURE, R_PCETAT_CLOTURE, R_PCETAT_RESOLUTION_PLAN,
                                    R_PCETAT_MAJ_PC, R_PCETAT_AUD_FUTURE, R_PCETAT_CONVERSION_LJ, R_PCETAT_EXTENSION, R_PCETAT_ASAUVER,
                                    R_PCETAT_UTILISABLE_SAISIE)
            VALUES (@c, @l, 0, 0, 0, 1, 0, 0, 0, 1, @s)
            """, ct, new SqlParameter("@c", valeur), new SqlParameter("@l", libelle), new SqlParameter("@s", saisie));
        var id = $"{t.Nom}|{valeur}";
        return IssueGeste.Ecrit($"{t.Liste} {valeur} créé : {libelle}.", [id],
            new TraceGeste("reglage", $"le code {valeur} de {t.Nom}", id, "a cree", null, new Code(t.Liste, valeur, libelle, saisie)));
    }

    /// <summary>Les citations qui font echouer la suppression dans RIG v2 (cles etrangeres de RIG_DEV) : (table V2, condition, ce qu'elle dit).</summary>
    private static (string Table, string Condition, string Quoi)[] Citations(TableRef t) => t.Nom switch
    {
        "PC_ETAT" =>
        [
            ("PC_ETAT_HISTO", "PETHT_PCETAT = @c", "dans l'historique des états de procédures collectives"),
            ("PC_ETAT_SUIVANT", "PETSV_APRES = @c AND PETSV_AVANT <> @c", "comme état suivant d'un autre état"),
        ],
        "PC_ETAT_ZONE" => [("PC_ETAT_SAISIE", "PETSA_PC_ETAT_ZONE = @c", "dans la saisie d'un état de procédure collective")],
        _ => [("DEMAT_CFEC_AJMJ", "CFECAJ_DEMAT_CFEC_AJMJ_TYPE_DEPOT = @c", "par des dépôts reçus des AJMJ")],
    };

    private static async Task<int> CompterAsync(GesteEnCours geste, string table, string condition, string valeur, CancellationToken ct)
    {
        // Une table que la base du greffe n'a pas ne cite rien (OBJECT_ID nul) : la requete n'est pas lancee.
        await using (var o = await geste.LireAsync("SELECT OBJECT_ID(@t)", ct, new SqlParameter("@t", $"v2.{table}")))
            if (!await o.ReadAsync(ct) || o.IsDBNull(0)) return 0;
        await using var r = await geste.LireAsync($"SELECT COUNT(*) FROM v2.{table} WHERE {condition}", ct, new SqlParameter("@c", valeur));
        await r.ReadAsync(ct);
        return r.GetInt32(0);
    }

    private static async Task<IssueGeste> SupprimerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (Id(id) is not { } cle || await LireAsync(geste, cle.Table, cle.Code, ct) is not { } code)
            return IssueGeste.Refuser($"Code introuvable dans les tables de référence du greffe ({id}).");
        var t = cle.Table;
        foreach (var (table, condition, quoi) in Citations(t))
            if (await CompterAsync(geste, table, condition, code.Valeur, ct) is var n and > 0)
                return IssueGeste.Refuser($"{SuppressionEchouee} Le code {code.Valeur} est utilisé {quoi} ({n.ToString(CultureInfo.InvariantCulture)} ligne(s) {table}).");
        var liees = new List<string>();
        if (t.Nom == "PC_ETAT")
            foreach (var (table, colonne) in new[] { ("PC_ETAT_SUIVANT", "PETSV_AVANT"), ("PC_ETAT_DJ_EP", "PETDJ_ETAT"), ("PC_ETAT_SAISIE", "PETSA_ETAT") })
            {
                if (await CompterAsync(geste, table, $"{colonne} = @c", code.Valeur, ct) == 0) continue;
                var n = await geste.EcrireAsync($"DELETE FROM v2.{table} WHERE {colonne} = @c", ct, new SqlParameter("@c", code.Valeur));
                liees.Add($"{n.ToString(CultureInfo.InvariantCulture)} × {table}");
            }
        await geste.EcrireAsync($"DELETE FROM v2.{t.Nom} WHERE {t.Valeur} = @c", ct, new SqlParameter("@c", code.Valeur));
        return IssueGeste.Ecrit($"{t.Liste} {code.Valeur} supprimé{(liees.Count > 0 ? $", avec {string.Join(", ", liees)}" : "")}.", [id],
            new TraceGeste("reglage", $"le code {code.Valeur} de {t.Nom}", id, "a supprime", new { Code = code, Liees = liees }, null));
    }
}
