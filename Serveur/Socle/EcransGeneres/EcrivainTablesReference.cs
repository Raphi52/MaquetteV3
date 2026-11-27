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
///   - MODIFIER (_Save, l. 421-462, puis SQLBase_Handler.Sauve) : le libelle ecran et « Utilisable en saisie », que les trois
///     formulaires lient (OPE_AFFICHE_SQLBASE_PC_ETAT.cs:71,81 ; _PC_ETAT_ZONE.cs:76,79 ; _DEMAT_CFEC_AJMJ_TYPE_DEPOT.cs:45,47) -- le
///     libelle d'un type AJMJ par un agent Amitel seul (MetierReadOnly, .Designer.cs:88, leve l. 54) ; TOUS les champs d'une zone
///     (_PC_ETAT_ZONE.cs:75-95 : ZoneAsync, memes controles qu'a sa creation, unicite table / attribut comprise : DoVerifier,
///     l. 123-137, joue a chaque enregistrement) ; l'evenement de procedure d'un type AJMJ (l. 46 ; facultatif, l. 27-28 ; un code avec
///     depot PC : .Designer.cs:98, ULT_COMBO_CODE_EVE_PRO.cs:79).
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
///     depot AJMJ : RIG v2 n'en cree pas (NouvelEnregistrement = false, FactoryHelper.cs:83 ; NOUVEAU cache,
///     OPE_AFFICHE_REF_CFEC_AJMJ_TYPE_DEPOT.cs:31).
///   - DUPLIQUER (_Dupliquer, l. 335, 355-370 ; SQLBase_Handler.Copie) : un AJOUTER dont Ids porte la ligne copiee ; voir AjouterAsync.
///   - SUPPRIMER (_Delete, l. 491-512 ; SQLBase_Handler.Delete(true), l. 87-100) -- jamais un type de depot AJMJ, dont RIG v2 cache
///     SUPPRIMER (et DUPLIQUER : OPE_AFFICHE_REF_CFEC_AJMJ_TYPE_DEPOT.cs:32-33 ; pas de message, la V3 refuse en le disant) : les
///     collections liees d'un etat de PC d'abord (etats
///     suivants PC_ETAT_SUIVANT.PETSV_AVANT, decisions PC_ETAT_DJ_EP, saisie PC_ETAT_SAISIE : OPE_AFFICHE_TABLE_REF_PC_ETAT.cs:34), puis
///     l'enregistrement. Un code CITE AILLEURS fait echouer la suppression sur les cles etrangeres de RIG_DEV
///     (Source\BaseDeDonnées\RigDatabase\dbo\Tables : PC_ETAT_HISTO.PETHT_PCETAT et PC_ETAT_SUIVANT.PETSV_APRES -> PC_ETAT ;
///     PC_ETAT_SAISIE.PETSA_PC_ETAT_ZONE -> PC_ETAT_ZONE),
///     et la V2 dit alors « La suppression de l'enregistrement courant a échoué. » (l. 506) : la V3 controle ces citations AVANT d'ecrire
///     (la base V3 n'a pas recopie les cles etrangeres), refuse avec ce message et nomme la citation.
/// NON REPRIS (nommes) : les autres champs d'un ETAT DE PC (regime, modalites, cases, code cloture, statistiques) et ses collections
/// (etats suivants, decisions, saisie), en lecture : changer une modalite AJ ou PA y recalcule les decisions et la saisie rattachees
/// (Modalite_Changed, OPE_AFFICHE_SQLBASE_PC_ETAT.cs:111-114 -> _PC_ETAT_DJ_EP.cs:393-401, _PC_ETAT_SAISIE.cs:706-714), enregistrees
/// avec l'etat par SQLBase_Handler_PC_Etat ; les quatre cases s'excluent et se montrent l'une par l'autre (l. 89-119, 135-161) ; le
/// regime des statistiques est reserve a Amitel (l. 52-55) -- la grille V3 ne porte ni ces listes ni ces dependances. De meme leur
/// saisie a la creation d'un etat (il prend ceux de CreateEtat, ou ceux de l'etat copie) ; le verrou SqlLock de l'enregistrement
/// ouvert (_CheckLock).
/// TRACE : famille « reglage », « a cree » / « a modifie » / « a supprime ».
/// </summary>
// fix-ok: mesure par le controle final du lot 14 (27/11/2026, reprise) -- AjouterAsync refusait toute zone (« RIG v3 ne la cree pas
// encore ») et aucun geste ne dupliquait, alors que RIG v2 cree une zone (FactoryHelper.cs:78) et duplique (OPE_TABLE_REF_BUTTONS_BAR.cs:335) ;
// prouve au banc : 17 blocs zone / copie verts, et rouges des que la copie est ignoree (Ids non lus).
// fix-ok: 27/11/2026 -- les deux retouches apres la creation remplacent un motif positionnel sur le tuple nullable de Id() (« is not var
// (t, x) », ecrit avant toute compilation, dont la reaction a un id illisible n'etait pas verifiee) par la forme du depot « is not { } cle » ;
// compiler.ps1 = 0 ensuite, et les refus « Code introuvable » sont joues par le banc.
// fix-ok: controle final du lot 14 (27/11/2026, 2e reprise) -- SupprimerAsync effacait un type de depot AJMJ, que RIG v2 ne supprime
// pas (OPE_AFFICHE_REF_CFEC_AJMJ_TYPE_DEPOT.cs:32), et ModifierAsync ne prenait d'une zone que libelle et case
// (_PC_ETAT_ZONE.cs:75-95) : refus et zone entiere (ZoneAsync), prouves au banc (PROC_GTABREF.gestes, blocs « 2e reprise »).
internal sealed class EcrivainTablesReference : IEcrivainEcran
{
    public string Procedure => "PROC_GTABREF";
    public string Ecran => "Tables de référence";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Modifier, TypeGeste.Supprimer];

    /// <summary>OPE_TABLE_REF_BUTTONS_BAR._Delete, l. 506.</summary>
    internal const string SuppressionEchouee = "La suppression de l'enregistrement courant a échoué.";

    /// <summary>La section du formulaire d'un type de depot AJMJ (Configurer.cs) et son champ modifiable.</summary>
    internal const string SectionTypeDepot = "Champs d'un type de dépôt", ChampEvenement = "Évènement de procédure";

    /// <summary>Le libelle d'un type AJMJ hors equipe Amitel : la V2 le montre en lecture seule (.Designer.cs:88, l. 54), sans message.</summary>
    internal const string LibelleReserveAmitel = "Le libellé d'un type de dépôt AJMJ ne se modifie que par l'équipe Amitel : RIG v2 le montre en lecture seule.";

    /// <summary>Un champ d'un etat de PC : la fiche le lit, la V3 ne l'enregistre pas (en-tete, NON REPRIS).</summary>
    internal const string EtatEnLecture = "Les champs d'un état de procédure collective se règlent dans RIG v2 : changer une modalité y recalcule les décisions et la saisie rattachées à l'état.";

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
        // Les champs de section : ceux du formulaire de la table que RIG v2 y modifie (en-tete), aucun autre.
        foreach (var champ in geste.Demande.Valeurs.Keys.Where(k => k.Contains(" › ", StringComparison.Ordinal)))
        {
            if (champ.StartsWith(GesteEcran.Cle(SectionZone, ""), StringComparison.Ordinal) && t.Nom != "PC_ETAT_ZONE")
                return IssueGeste.Refuser($"Les champs d'une zone ne vont qu'à une zone (PC_ETAT_ZONE), pas à {t.Liste}.");
            if (t.Nom == "PC_ETAT") return IssueGeste.Refuser(EtatEnLecture);
            var permis = t.Nom == "PC_ETAT_ZONE"
                ? ChampsZone.Any(z => champ == GesteEcran.Cle(SectionZone, z.Libelle)) || champ == GesteEcran.Cle(SectionZone, ChampDesactivation)
                : champ == GesteEcran.Cle(SectionTypeDepot, ChampEvenement);
            if (!permis) return IssueGeste.Refuser($"« {champ} » ne se modifie pas dans {t.Liste} : RIG v2 ne l'y modifie pas.");
        }
        var apres = avant with { Libelle = Cellule(geste, ColLibelle) ?? avant.Libelle, Saisie = Coche(Cellule(geste, ColSaisie)) ?? avant.Saisie };
        if (LibelleAmitel(t) && apres.Libelle != avant.Libelle && !geste.Agent.EstAmitel) return IssueGeste.Refuser(LibelleReserveAmitel);
        var ensemble = new List<string> { $"{t.Libelle} = @l", $"{t.Saisie} = @s" };
        var parametres = new List<SqlParameter> { new("@l", apres.Libelle), new("@s", apres.Saisie), new("@c", avant.Valeur) };
        object? propresAvant = null, propresApres = null;
        if (t.Nom == "PC_ETAT_ZONE")
        {
            // Une zone : tout son formulaire, controle comme a sa creation ; ce qui n'est pas saisi garde sa valeur de la base.
            var ligne = await LigneZoneAsync(geste, avant.Valeur, ct);
            var (zone, refusZone) = await ZoneAsync(geste, avant.Valeur, ligne, ct);
            if (zone is null) return IssueGeste.Refuser(refusZone!);
            DateTime? finAvant = ligne.TryGetValue("R_PCETATZONE_DATE_DESACTIVATION", out var fin) && fin is DateTime f ? f : null;
            var changes = zone.Champs.Where(kv => kv.Value != Texte(ligne, kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (zone.Desactivation != finAvant)
                changes["R_PCETATZONE_DATE_DESACTIVATION"] = zone.Desactivation?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
            foreach (var (colonne, valeur) in zone.Champs)
            {
                ensemble.Add($"{colonne} = @{colonne}");
                parametres.Add(new SqlParameter($"@{colonne}", valeur.Length > 0 ? valeur : DBNull.Value));
            }
            ensemble.Add("R_PCETATZONE_DATE_DESACTIVATION = @fin");
            parametres.Add(new SqlParameter("@fin", (object?)zone.Desactivation ?? DBNull.Value));
            if (changes.Count > 0)
                (propresAvant, propresApres) = (changes.ToDictionary(kv => kv.Key, kv => Texte(ligne, kv.Key)), changes);
        }
        else if (t.Nom == "DEMAT_CFEC_AJMJ_TYPE_DEPOT")
        {
            string? evenementAvant;
            await using (var r = await geste.LireAsync("SELECT CFECAJTD_CODE_EVE_PRO FROM v2.DEMAT_CFEC_AJMJ_TYPE_DEPOT WHERE CFECAJTD_VALEUR = @c", ct,
                new SqlParameter("@c", avant.Valeur)))
                evenementAvant = await r.ReadAsync(ct) && !r.IsDBNull(0) && r.GetString(0).Trim().Length > 0 ? r.GetString(0).Trim() : null;
            var evenement = evenementAvant;
            if (geste.Demande.Valeurs.TryGetValue(GesteEcran.Cle(SectionTypeDepot, ChampEvenement), out var saisi))
            {
                saisi = saisi.Trim();
                if (saisi.Length == 0) evenement = null;
                else if (await DepotsPcV2.CodeAsync(geste, saisi, ct) is { } codeEve) evenement = codeEve.Code;
                else return IssueGeste.Refuser($"« {ChampEvenement} » : « {saisi} » n'est pas un code d'évènement avec dépôt PC de la base du greffe (la liste de RIG v2 ne propose que ceux-là).");
            }
            ensemble.Add("CFECAJTD_CODE_EVE_PRO = @evenement");
            parametres.Add(new SqlParameter("@evenement", (object?)evenement ?? DBNull.Value));
            if (evenement != evenementAvant) (propresAvant, propresApres) = (new { Evenement = evenementAvant }, new { Evenement = evenement });
        }
        if (apres == avant && propresApres is null) return IssueGeste.Refuser("Rien à enregistrer : aucune valeur ne change.");
        if (VerifierLibelle(t, apres.Libelle) is { } refus) return IssueGeste.Refuser(refus);
        if (t.DateModif is not null)
        {
            ensemble.Add($"{t.DateModif} = @date");
            ensemble.Add($"{t.AuteurModif} = @auteur");
            parametres.Add(new SqlParameter("@date", geste.Maintenant));
            parametres.Add(new SqlParameter("@auteur", await AuteurAsync(geste, ct)));
        }
        await geste.EcrireAsync($"UPDATE v2.{t.Nom} SET {string.Join(", ", ensemble)} WHERE {t.Valeur} = @c", ct, [.. parametres]);
        return IssueGeste.Ecrit($"{t.Liste} {avant.Valeur} enregistré : {apres.Libelle}.", [id],
            new TraceGeste("reglage", $"le code {avant.Valeur} de {t.Nom}", id, "a modifie",
                new { Code = avant, Champs = propresAvant }, new { Code = apres, Champs = propresApres }));
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

    /// <summary>Une ligne de v2.PC_ETAT_ZONE telle quelle (colonne -> valeur) ; vide si le code n'y est pas.</summary>
    private static async Task<Dictionary<string, object?>> LigneZoneAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        var ligne = new Dictionary<string, object?>(StringComparer.Ordinal);
        await using var r = await geste.LireAsync("SELECT * FROM v2.PC_ETAT_ZONE WHERE R_PCETATZONE_VALEUR = @c", ct, new SqlParameter("@c", code));
        if (await r.ReadAsync(ct))
            for (var i = 0; i < r.FieldCount; i++) ligne[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        return ligne;
    }

    private static string Texte(Dictionary<string, object?> ligne, string colonne)
        => ligne.TryGetValue(colonne, out var v) && v is not null ? Convert.ToString(v, CultureInfo.InvariantCulture)!.Trim() : "";

    /// <summary>Les champs d'une zone tels qu'ils s'enregistrent (colonne -> texte, vide = NULL) et sa date de desactivation.</summary>
    private sealed record ZoneSaisie(Dictionary<string, string> Champs, DateTime? Desactivation);

    /// <summary>
    /// Les champs de la zone <paramref name="code"/> : ceux saisis, sinon ceux de <paramref name="depart"/> (la zone copiee, ou la zone
    /// modifiee), controles comme RIG v2 -- obligatoires, longueurs, type et etat de ses listes, et l'unicite table / attribut avec
    /// les AUTRES zones (DoVerifier, OPE_AFFICHE_SQLBASE_PC_ETAT_ZONE.cs:123-137, son message ; joue a chaque enregistrement, creation
    /// comme modification). Rend la zone, ou le refus.
    /// </summary>
    private static async Task<(ZoneSaisie? Zone, string? Refus)> ZoneAsync(GesteEnCours geste, string code, Dictionary<string, object?> depart,
                                                                           CancellationToken ct)
    {
        var champs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (libelle, colonne, longueur, obligatoire) in ChampsZone)
        {
            var v = ChampZone(geste, libelle) ?? Texte(depart, colonne);
            if (obligatoire && v.Length == 0) return (null, CreationV2.Obligatoire(libelle));
            if (libelle == "Type" && v.Length > 0)
            {
                if (TypesZone.FirstOrDefault(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase)) is not { } type)
                    return (null, $"« Type » : « {v} » n'est pas un type de zone de RIG v2 ({string.Join(", ", TypesZone)}).");
                v = type;
            }
            if (libelle == "État" && v.Length > 0)
            {
                // La liste de RIG v2 enregistre le rang de l'etat ; l'ecran le montre par son libelle.
                var rang = Array.FindIndex(EtatsZone, e => string.Equals(e, v, StringComparison.OrdinalIgnoreCase));
                if (rang < 0 && !(int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out rang) && rang < EtatsZone.Length))
                    return (null, $"« État » : « {v} » n'est pas un état de RIG v2 ({string.Join(", ", EtatsZone)}).");
                v = rang.ToString(CultureInfo.InvariantCulture);
            }
            if (v.Length > longueur) return (null, $"« {libelle} » : {longueur} caractères au plus.");
            champs[colonne] = v;
        }
        DateTime? desactivation = depart.TryGetValue("R_PCETATZONE_DATE_DESACTIVATION", out var fin) && fin is DateTime d ? d : null;
        if (ChampZone(geste, ChampDesactivation) is { } saisie)
        {
            if (saisie.Length == 0) desactivation = null;
            else if (DateTime.TryParseExact(saisie, ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) desactivation = date;
            else return (null, $"« {ChampDesactivation} » : « {saisie} » n'est pas une date.");
        }
        await using (var u = await geste.LireAsync("""
            SELECT COUNT(*) FROM v2.PC_ETAT_ZONE WITH (UPDLOCK)
            WHERE R_PCETATZONE_TABLE = @t AND R_PCETATZONE_ATTRIBUT = @a AND R_PCETATZONE_VALEUR <> @c
            """, ct, new SqlParameter("@t", champs["R_PCETATZONE_TABLE"]), new SqlParameter("@a", champs["R_PCETATZONE_ATTRIBUT"]),
            new SqlParameter("@c", code)))
        {
            await u.ReadAsync(ct);
            if (u.GetInt32(0) > 0)
                return (null, "Cette zone ne pourra pas être sauvegardée en base de données car un enregistrement ayant les mêmes valeurs de "
                              + "\"Table\" et d'\"Attribut\" existe déjà en base de données.");
        }
        return (new ZoneSaisie(champs, desactivation), null);
    }

    /// <summary>
    /// Une zone neuve ou copiee (ZoneAsync). Historique (SousOPE_HISTORIQUE) : une zone neuve prend la date et l'auteur de creation
    /// (MiseAJourChampsCreation, l. 59-67 ; appele par SetNewMetierBase, OPE_TABLE_REF_BUTTONS_BAR.cs:242) ; une copie garde ceux de la
    /// zone copiee (Copie ne les exclut pas) ; les deux prennent la date et l'auteur de modification, que la saisie du code pose
    /// (OnUltFormulaireChangementValeur, l. 298).
    /// </summary>
    private static async Task<IssueGeste> AjouterZoneAsync(GesteEnCours geste, TableRef t, Code nouveau, Code? source, CancellationToken ct)
    {
        var copie = source is null ? new Dictionary<string, object?>(StringComparer.Ordinal) : await LigneZoneAsync(geste, source.Valeur, ct);
        var (zone, refus) = await ZoneAsync(geste, nouveau.Valeur, copie, ct);
        if (zone is null) return IssueGeste.Refuser(refus!);
        var champs = zone.Champs;
        var auteur = await AuteurAsync(geste, ct);
        var creation = source is null ? geste.Maintenant : copie.TryGetValue("R_PCETATZONE_DATE_CREATION", out var c) && c is DateTime dc ? dc : geste.Maintenant;
        var auteurCreation = source is null ? auteur : Texte(copie, "R_PCETATZONE_AUTEUR_CREATION") is { Length: > 0 } a ? a : null;
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
            new SqlParameter("@fin", (object?)zone.Desactivation ?? DBNull.Value), new SqlParameter("@etat", champs["R_PCETATZONE_ETAT"]),
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
        // Le type de depot AJMJ ne se supprime jamais (ModificationSeule) : aucune citation a compter.
        _ => [],
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
        if (ModificationSeule(t))
            return IssueGeste.Refuser($"RIG v2 ne supprime pas un enregistrement de {t.Liste} : la table s'ouvre sans « Supprimer ».");
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
