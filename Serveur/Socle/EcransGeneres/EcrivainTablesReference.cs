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
///   - AJOUTER (« Nouvel enregistrement », offert si CBO_TableRef.NouvelEnregistrement : OPE_SELECTION_TABLE.cs:89 ; FactoryHelper.cs:71-79).
///     Un ETAT DE PC : PC_ETAT.CreateEtat(valeur, libelle, "False", "False") (RigMetier\TableRef\PC_ETAT.cs:168-188 : ouverture et
///     cloture faux, mise a jour de la PC vrai, les autres cases a leur defaut de RigBaseGreffe\GREFFE\PC_ETAT.cs). Une ZONE, avec les
///     champs de son formulaire saisis sous la ligne (Configurer.cs, SectionsAjout) : AjouterZoneAsync. Refus d'un code deja porte
///     (« Un enregistrement nommé X existe déjà. Il n'est pas possible de créer un nouvel enregistrement avec ce nom. »,
///     OPE_AFFICHE_SQLBASE_GENERIQUE.cs:134-137) et d'un code trop long (DoVerifier, OPE_AFFICHE_SQLBASE_PC_ETAT.cs:205-215). Le type de
///     depot AJMJ : RIG v2 n'en cree pas (NouvelEnregistrement = false, FactoryHelper.cs:83).
///   - DUPLIQUER (_Dupliquer, l. 335, 355-370 ; SQLBase_Handler.Copie) : un AJOUTER dont Ids porte la ligne copiee ; voir AjouterAsync.
///   - SUPPRIMER (_Delete, l. 491-512 ; SQLBase_Handler.Delete(true), l. 87-100) : les collections liees d'un etat de PC d'abord (etats
///     suivants PC_ETAT_SUIVANT.PETSV_AVANT, decisions PC_ETAT_DJ_EP, saisie PC_ETAT_SAISIE : OPE_AFFICHE_TABLE_REF_PC_ETAT.cs:34), puis
///     l'enregistrement. Un code CITE AILLEURS fait echouer la suppression sur les cles etrangeres de RIG_DEV
///     (Source\BaseDeDonnées\RigDatabase\dbo\Tables : PC_ETAT_HISTO.PETHT_PCETAT et PC_ETAT_SUIVANT.PETSV_APRES -> PC_ETAT ;
///     PC_ETAT_SAISIE.PETSA_PC_ETAT_ZONE -> PC_ETAT_ZONE ; DEMAT_CFEC_AJMJ.CFECAJ_DEMAT_CFEC_AJMJ_TYPE_DEPOT -> DEMAT_CFEC_AJMJ_TYPE_DEPOT),
///     et la V2 dit alors « La suppression de l'enregistrement courant a échoué. » (l. 506) : la V3 controle ces citations AVANT d'ecrire
///     (la base V3 n'a pas recopie les cles etrangeres), refuse avec ce message et nomme la citation.
/// NON REPRIS (nommes) : la MODIFICATION des autres champs des formulaires (regime, cases d'ouverture / cloture / conversion / extension et
/// leurs exclusions, textes d'edition d'une zone, evenement de procedure) et des collections d'un etat de PC (etats suivants, decisions,
/// saisie) : la fiche les montre en lecture, une zone les saisit a sa creation ou sa copie ; la saisie des champs d'un etat de PC a sa
/// creation (il prend ceux de CreateEtat, ou ceux de l'etat copie) ; le verrou SqlLock de l'enregistrement ouvert (_CheckLock).
/// TRACE : famille « reglage », « a cree » / « a modifie » / « a supprime ».
/// </summary>
// fix-ok: mesure par le controle final du lot 14 (27/11/2026, reprise) -- AjouterAsync refusait toute zone (« RIG v3 ne la cree pas
// encore ») et aucun geste ne dupliquait, alors que RIG v2 cree une zone (FactoryHelper.cs:78) et duplique (OPE_TABLE_REF_BUTTONS_BAR.cs:335) ;
// prouve au banc : 17 blocs zone / copie verts, et rouges des que la copie est ignoree (Ids non lus).
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

    /// <summary>
    /// AJOUTER un code, ou le DUPLIQUER (Ids = [la ligne copiee]). Nouveau : la table saisie (ou, vide, celle des champs d'une zone
    /// saisis dans l'onglet des zones). Copie (_Dupliquer, l. 355-370 ; SetNewMetierBase, l. 247-249 : SQLbase_Handler.Copie) :
    /// la table de la ligne copiee, tous ses champs sauf la cle (ChampsNonDuplicables = ChampsID : RigMetier\TableRef\PC_ETAT.GtabRef.cs:16-24,
    /// PC_ETAT_ZONE.IGtabRef.cs:16-24), ce qui est saisi les remplacant ; les collections d'un etat (etats suivants, decisions, saisie)
    /// ne sont pas copiees (Copie ne parcourt que ListeChamps, SQLBase_Handler_PC_Etat.cs:66-77).
    /// </summary>
    private static async Task<IssueGeste> AjouterAsync(GesteEnCours geste, CancellationToken ct)
    {
        TableRef t;
        Code? source = null;
        if (geste.Demande.Ids.FirstOrDefault() is { } idSource)
        {
            if (Id(idSource) is not { } cle || await LireAsync(geste, cle.Table, cle.Code, ct) is not { } lu)
                return IssueGeste.Refuser($"Code introuvable dans les tables de référence du greffe ({idSource}).");
            (t, source) = (cle.Table, lu);
            if (Cellule(geste, ColTable) is { Length: > 0 } table && Table(table) != t)
                return IssueGeste.Refuser($"Un code reste dans sa table ({t.Liste}).");
            // Le type de depot AJMJ : ni « Nouvel enregistrement » (FactoryHelper.cs:83) ni copie (son SQLBase_Handler n'a pas de
            // Copie : celle de la base rend null, SQLBase_Handler.cs:102-104).
            if (!t.Creation)
                return IssueGeste.Refuser($"RIG v2 ne duplique pas un enregistrement de {t.Liste} : la table n'a ni « Nouvel enregistrement » ni copie.");
        }
        else
        {
            var saisieTable = Cellule(geste, ColTable) ?? "";
            if (saisieTable.Length == 0 && ChampsZoneSaisis(geste)) saisieTable = "PC_ETAT_ZONE";
            if (saisieTable.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ColTable));
            if (Table(saisieTable) is not { } saisie)
                return IssueGeste.Refuser($"« {ColTable} » : « {saisieTable} » n'est pas une table que RIG v2 ouvre ({string.Join(", ", Tables.Select(x => x.Liste))}).");
            t = saisie;
            if (!t.Creation)
                return IssueGeste.Refuser($"RIG v2 ne crée pas d'enregistrement dans {t.Liste} : la table s'ouvre sans « Nouvel enregistrement ».");
        }
        if (t.Nom != "PC_ETAT_ZONE" && ChampsZoneSaisis(geste))
            return IssueGeste.Refuser($"Les champs d'une zone ne vont qu'à une zone (PC_ETAT_ZONE), pas à {t.Liste}.");
        var valeur = Cellule(geste, ColCode) ?? "";
        if (valeur.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ColCode));
        if (valeur.Length > t.LongueurCode) return IssueGeste.Refuser($"Longueur champ \"Valeur\" trop grande ({t.LongueurCode} max).");
        if (await LireAsync(geste, t, valeur, ct) is not null)
            return IssueGeste.Refuser($"Un enregistrement nommé {valeur} existe déjà. Il n'est pas possible de créer un nouvel enregistrement avec ce nom.");
        var libelle = Cellule(geste, ColLibelle) ?? source?.Libelle ?? "";
        if (VerifierLibelle(t, libelle) is { } refus) return IssueGeste.Refuser(refus);
        var saisissable = Coche(Cellule(geste, ColSaisie)) ?? source?.Saisie ?? true;
        var nouveau = new Code(t.Liste, valeur, libelle, saisissable);
        if (t.Nom == "PC_ETAT_ZONE") return await AjouterZoneAsync(geste, t, nouveau, source, ct);
        if (source is null)
        {
            // PC_ETAT.CreateEtat(valeur, libelle, "False", "False") : ouverture et cloture faux, MAJ_PC vrai ; les autres cases a leur
            // defaut (RigBaseGreffe\GREFFE\PC_ETAT.cs : resolution du plan, audience future, conversion LJ, extension faux ; a sauver vrai).
            await geste.EcrireAsync("""
                INSERT INTO v2.PC_ETAT (R_PCETAT_VALEUR, R_PCETAT_LIBELLE_ECRAN, R_PCETAT_OUVERTURE, R_PCETAT_CLOTURE, R_PCETAT_RESOLUTION_PLAN,
                                        R_PCETAT_MAJ_PC, R_PCETAT_AUD_FUTURE, R_PCETAT_CONVERSION_LJ, R_PCETAT_EXTENSION, R_PCETAT_ASAUVER,
                                        R_PCETAT_UTILISABLE_SAISIE)
                VALUES (@c, @l, 0, 0, 0, 1, 0, 0, 0, 1, @s)
                """, ct, new SqlParameter("@c", valeur), new SqlParameter("@l", libelle), new SqlParameter("@s", saisissable));
        }
        else
        {
            // La copie d'un etat : ses champs (RigBaseGreffe\GREFFE\PC_ETAT.cs, ListeChamps), sauf la cle ; libelle et saisie saisis.
            await geste.EcrireAsync($"""
                INSERT INTO v2.PC_ETAT (R_PCETAT_VALEUR, R_PCETAT_LIBELLE_ECRAN, R_PCETAT_UTILISABLE_SAISIE, {ChampsEtatCopies})
                SELECT @c, @l, @s, {ChampsEtatCopies} FROM v2.PC_ETAT WHERE R_PCETAT_VALEUR = @source
                """, ct, new SqlParameter("@c", valeur), new SqlParameter("@l", libelle), new SqlParameter("@s", saisissable),
                new SqlParameter("@source", source.Valeur));
        }
        return Cree(t, nouveau, source);
    }

    /// <summary>Les champs d'un etat de PC que sa copie reprend tels quels (RigBaseGreffe\GREFFE\PC_ETAT.cs, ListeChamps, hors cle, libelle et saisie).</summary>
    private const string ChampsEtatCopies = "R_PCETAT_OUVERTURE, R_PCETAT_CLOTURE, R_PCETAT_MODALITE_REGIME, R_PCETAT_MODALITE_PA, R_PCETAT_MODALITE_AJ, "
        + "R_PCETAT_REGIME_PC_STATS, R_PCETAT_CODE_PROC, R_PCETAT_RESOLUTION_PLAN, R_PCETAT_MAJ_PC, R_PCETAT_AUD_FUTURE, R_PCETAT_CODE_CLOTURE, "
        + "R_PCETAT_CONVERSION_LJ, R_PCETAT_EXTENSION, R_PCETAT_ASAUVER";

    private static IssueGeste Cree(TableRef t, Code nouveau, Code? source)
    {
        var id = $"{t.Nom}|{nouveau.Valeur}";
        return IssueGeste.Ecrit(source is null
                ? $"{t.Liste} {nouveau.Valeur} créé : {nouveau.Libelle}."
                : $"{t.Liste} {nouveau.Valeur} créé, copie de {source.Valeur} : {nouveau.Libelle}.", [id],
            new TraceGeste("reglage", $"le code {nouveau.Valeur} de {t.Nom}", id, "a cree", null, new { Code = nouveau, Copie = source?.Valeur }));
    }

    /// <summary>La section des champs d'une zone (Configurer.cs, PROC_GTABREF), saisie a la creation et a la copie d'une zone.</summary>
    internal const string SectionZone = "Champs d'une zone";

    /// <summary>
    /// Les champs saisis d'une zone : libelle (Configurer.cs), colonne et longueur (RigDatabase\dbo\Tables\PC_ETAT_ZONE.sql),
    /// obligatoire (MetierObligatoire : SousOPE_ENREGISTREMENT.Designer.cs:102-111 synonyme et ISO, SousOPE_EDITION.Designer.cs:83-92
    /// simplifie et developpe, OPE_AFFICHE_SQLBASE_PC_ETAT_ZONE.Designer.cs:78-96 table, attribut et type,
    /// SousOPE_HISTORIQUE.Designer.cs:126 etat).
    /// </summary>
    private static readonly (string Libelle, string Colonne, int Longueur, bool Obligatoire)[] ChampsZone =
    [
        ("Synonyme", "R_PCETATZONE_SYNONYME", 16, true), ("ISO", "R_PCETATZONE_ISO", 20, true),
        ("Abrégé pour édition", "R_PCETATZONE_ABREGE_EDIT", 5, false), ("Simplifié pour édition", "R_PCETATZONE_SIMPLIFIE_EDIT", 100, true),
        ("Développé pour édition", "R_PCETATZONE_DEVELOPPE_EDIT", 1000, true), ("Exhaustif pour édition", "R_PCETATZONE_EXHAUSTIF_EDIT", int.MaxValue, false),
        ("Table de la zone", "R_PCETATZONE_TABLE", 50, true), ("Attribut", "R_PCETATZONE_ATTRIBUT", 50, true),
        ("Type", "R_PCETATZONE_TYPE", 50, true), ("État", "R_PCETATZONE_ETAT", 1, true),
    ];

    private const string ChampDesactivation = "Date de désactivation";

    /// <summary>Les types d'une zone, la liste de RIG v2 (PC_ETAT_ZONE.eTypeZone, RigMetier\TableRef\PC_ETAT_ZONE.cs:55-63).</summary>
    private static readonly string[] TypesZone = ["DATE", "DATETIME", "HEURE", "INT", "TEXT", "MULTILIGNE", "MONTANT"];

    private static string? ChampZone(GesteEnCours geste, string libelle)
        => geste.Demande.Valeurs.TryGetValue(GesteEcran.Cle(SectionZone, libelle), out var v) ? v.Trim() : null;

    private static bool ChampsZoneSaisis(GesteEnCours geste)
        => geste.Demande.Valeurs.Any(kv => kv.Key.StartsWith(GesteEcran.Cle(SectionZone, ""), StringComparison.Ordinal) && kv.Value.Trim().Length > 0);

    /// <summary>
    /// Une zone neuve ou copiee : ses champs saisis (sinon ceux de la zone copiee), controles comme RIG v2 -- obligatoires, longueurs,
    /// type et etat de ses listes, et l'unicite table / attribut (DoVerifier, OPE_AFFICHE_SQLBASE_PC_ETAT_ZONE.cs:123-137, son message).
    /// Historique (SousOPE_HISTORIQUE) : une zone neuve prend la date et l'auteur de creation (MiseAJourChampsCreation, l. 59-67 ; appele
    /// par SetNewMetierBase, OPE_TABLE_REF_BUTTONS_BAR.cs:242) ; une copie garde ceux de la zone copiee (Copie ne les exclut pas) ; les deux
    /// prennent la date et l'auteur de modification, que la saisie du code pose (OnUltFormulaireChangementValeur, l. 298).
    /// </summary>
    private static async Task<IssueGeste> AjouterZoneAsync(GesteEnCours geste, TableRef t, Code nouveau, Code? source, CancellationToken ct)
    {
        var copie = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (source is not null)
        {
            await using var r = await geste.LireAsync("SELECT * FROM v2.PC_ETAT_ZONE WHERE R_PCETATZONE_VALEUR = @c", ct, new SqlParameter("@c", source.Valeur));
            if (await r.ReadAsync(ct))
                for (var i = 0; i < r.FieldCount; i++) copie[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        }
        string Copiee(string colonne) => copie.TryGetValue(colonne, out var v) && v is not null ? Convert.ToString(v, CultureInfo.InvariantCulture)!.Trim() : "";
        var champs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (libelle, colonne, longueur, obligatoire) in ChampsZone)
        {
            var v = ChampZone(geste, libelle) ?? Copiee(colonne);
            if (obligatoire && v.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(libelle));
            if (libelle == "Type" && v.Length > 0)
            {
                if (TypesZone.FirstOrDefault(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase)) is not { } type)
                    return IssueGeste.Refuser($"« Type » : « {v} » n'est pas un type de zone de RIG v2 ({string.Join(", ", TypesZone)}).");
                v = type;
            }
            if (libelle == "État" && v.Length > 0)
            {
                // La liste de RIG v2 enregistre le rang de l'etat ; l'ecran le montre par son libelle.
                var rang = Array.FindIndex(EtatsZone, e => string.Equals(e, v, StringComparison.OrdinalIgnoreCase));
                if (rang < 0 && !(int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out rang) && rang < EtatsZone.Length))
                    return IssueGeste.Refuser($"« État » : « {v} » n'est pas un état de RIG v2 ({string.Join(", ", EtatsZone)}).");
                v = rang.ToString(CultureInfo.InvariantCulture);
            }
            if (v.Length > longueur) return IssueGeste.Refuser($"« {libelle} » : {longueur} caractères au plus.");
            champs[colonne] = v;
        }
        DateTime? desactivation = copie.TryGetValue("R_PCETATZONE_DATE_DESACTIVATION", out var fin) && fin is DateTime d ? d : null;
        if (ChampZone(geste, ChampDesactivation) is { } saisie)
        {
            if (saisie.Length == 0) desactivation = null;
            else if (DateTime.TryParseExact(saisie, ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) desactivation = date;
            else return IssueGeste.Refuser($"« {ChampDesactivation} » : « {saisie} » n'est pas une date.");
        }
        await using (var u = await geste.LireAsync("""
            SELECT COUNT(*) FROM v2.PC_ETAT_ZONE WITH (UPDLOCK)
            WHERE R_PCETATZONE_TABLE = @t AND R_PCETATZONE_ATTRIBUT = @a AND R_PCETATZONE_VALEUR <> @c
            """, ct, new SqlParameter("@t", champs["R_PCETATZONE_TABLE"]), new SqlParameter("@a", champs["R_PCETATZONE_ATTRIBUT"]),
            new SqlParameter("@c", nouveau.Valeur)))
        {
            await u.ReadAsync(ct);
            if (u.GetInt32(0) > 0)
                return IssueGeste.Refuser("Cette zone ne pourra pas être sauvegardée en base de données car un enregistrement ayant les mêmes valeurs de "
                                          + "\"Table\" et d'\"Attribut\" existe déjà en base de données.");
        }
        var auteur = await AuteurAsync(geste, ct);
        var creation = source is null ? geste.Maintenant : copie.TryGetValue("R_PCETATZONE_DATE_CREATION", out var c) && c is DateTime dc ? dc : geste.Maintenant;
        var auteurCreation = source is null ? auteur : Copiee("R_PCETATZONE_AUTEUR_CREATION") is { Length: > 0 } a ? a : null;
        object Ou(string colonne) => champs[colonne] is { Length: > 0 } v ? v : DBNull.Value;
        await geste.EcrireAsync("""
            INSERT INTO v2.PC_ETAT_ZONE (R_PCETATZONE_VALEUR, R_PCETATZONE_LIBELLE_ECRAN, R_PCETATZONE_SYNONYME, R_PCETATZONE_ISO, R_PCETATZONE_ABREGE_EDIT,
                R_PCETATZONE_SIMPLIFIE_EDIT, R_PCETATZONE_DEVELOPPE_EDIT, R_PCETATZONE_EXHAUSTIF_EDIT, R_PCETATZONE_DATE_CREATION,
                R_PCETATZONE_AUTEUR_CREATION, R_PCETATZONE_DATE_MODIF, R_PCETATZONE_AUTEUR_MODIF, R_PCETATZONE_DATE_DESACTIVATION, R_PCETATZONE_ETAT,
                R_PCETATZONE_UTILISABLE_SAISIE, R_PCETATZONE_TABLE, R_PCETATZONE_ATTRIBUT, R_PCETATZONE_TYPE)
            VALUES (@c, @l, @synonyme, @iso, @abrege, @simplifie, @developpe, @exhaustif, @creation, @auteurCreation, @modif, @auteur, @fin, @etat, @s,
                    @table, @attribut, @type)
            """, ct, new SqlParameter("@c", nouveau.Valeur), new SqlParameter("@l", nouveau.Libelle),
            new SqlParameter("@synonyme", champs["R_PCETATZONE_SYNONYME"]), new SqlParameter("@iso", champs["R_PCETATZONE_ISO"]),
            new SqlParameter("@abrege", Ou("R_PCETATZONE_ABREGE_EDIT")), new SqlParameter("@simplifie", champs["R_PCETATZONE_SIMPLIFIE_EDIT"]),
            new SqlParameter("@developpe", champs["R_PCETATZONE_DEVELOPPE_EDIT"]), new SqlParameter("@exhaustif", Ou("R_PCETATZONE_EXHAUSTIF_EDIT")),
            new SqlParameter("@creation", creation), new SqlParameter("@auteurCreation", (object?)auteurCreation ?? DBNull.Value),
            new SqlParameter("@modif", geste.Maintenant), new SqlParameter("@auteur", auteur),
            new SqlParameter("@fin", (object?)desactivation ?? DBNull.Value), new SqlParameter("@etat", champs["R_PCETATZONE_ETAT"]),
            new SqlParameter("@s", nouveau.Saisie), new SqlParameter("@table", champs["R_PCETATZONE_TABLE"]),
            new SqlParameter("@attribut", champs["R_PCETATZONE_ATTRIBUT"]), new SqlParameter("@type", champs["R_PCETATZONE_TYPE"]));
        return Cree(t, nouveau, source);
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
