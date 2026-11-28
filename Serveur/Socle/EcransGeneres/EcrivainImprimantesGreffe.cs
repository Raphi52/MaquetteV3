using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurImprimantesGreffe;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Imprimantes du greffe » (PROC_IMP_WIN) : AJOUTER et MODIFIER une imprimante lot 1 ou lot 2 du greffe, dans la grille. Mode
/// d'emploi : EcrivainEcran.cs ; les lignes : LecteurImprimantesGreffe.cs. Plan « RIG v3 complet, mesure par le banc du parcours
/// complet », lot 6, 28/11/2026.
///
/// SOURCE (ancien RIG) : Outils\Impression\PROC_IMP_WIN\ETP_IMP_LOTS -- la saisie OPE_SAISIE_IMP_LOT1 et OPE_SAISIE_IMP_LOT2, bouton
/// « Enregistrer » (_Valider) ; les entites RigMetier\Edition\ImprimanteLot1.cs, ImprimanteLot2.cs et TagPostscript.cs.
///   - OBLIGATOIRES : « Nom » et « Redirigée vers l'imprimante physique » (MetierObligatoire, OPE_SAISIE_IMP_LOT1.Designer.cs:93, :103 ;
///     OPE_SAISIE_IMP_LOT2.Designer.cs:95, :105) ; en ajout, le lot dit l'onglet, donc la table. Tailles : celles de RIG_DEV
///     (R_IMPRIMANTE_VALEUR 50, R_IMPRIMANTE_EMPLACEMENT_IMP 100 ; IMPED_NOM_LOGIQUE et IMPED_NOM_PHYSIQUE 255) ;
///   - AJOUT LOT 1 (OPE_SAISIE_IMP_LOT1.cs:131-134, ImprimanteLot1.CreateImprimanteLot1, ImprimanteLot1.cs:66-79) : valeur, libelle et
///     synonyme = le nom ; emplacement = l'imprimante physique ; differe et active saisis (decoches par defaut) ; utilisable en saisie
///     faux ; etat « 0 » ;
///   - AJOUT LOT 2 (OPE_SAISIE_IMP_LOT2.cs:185-193, ImprimanteLot2.CreateImprimanteLot2, ImprimanteLot2.cs:276-283) : nom logique et
///     physique ; puis les trois types de papier d'office, les tags BAC_NORMAL, BAC_SECURISE et BAC_LRAR (_AjouterTag,
///     TagPostscript.CreateTagPostscript(« BAC_ » + type, nom, « », faux), :208-211) -- l'ajout n'envoie pas de types (:218-220) ;
///   - MODIFICATION : le nom est en lecture seule (ultNomImprimante.MetierReadOnly = Imprimante != null, LOT1.cs:110, LOT2.cs:121) ;
///     lot 1 : physique, differe, active ; lot 2 : physique et types de papier -- des types changes, la V2 supprime tous les tags
///     « bac_ » de l'imprimante puis cree un tag « BAC_ » + type par type de la liste separee par des virgules (:194-206) ; un type
///     saisi est mis en majuscules, son prefixe « BAC_ » retire, un doublon refuse (« Le type de papier '...' existe déjà. »,
///     _AjouterTypePapier, :148-155).
/// REFUS AJOUTES PAR LA V3 (nommes) : une imprimante deja portee par la table -- la V2 n'a pas de controle et l'insertion casse sur la
/// cle (PK_IMPRIMANTE_PS, PK_IMPRIMANTE_EDITION de RIG_DEV) ; une colonne d'un lot sur l'autre (types de papier en lot 1, differe ou
/// active en lot 2 : la saisie de la V2 n'a pas ces champs).
/// NON REPRIS (nommes) : la SUPPRESSION -- la V2 demande d'abord une imprimante de remplacement, vers laquelle elle renomme les appels
/// de GROUPE_IMPRESSION (OPE_IMP_LOT1.cs:208-226, OPE_IMP_LOT2.cs:166-212 : SaisieBox obligatoire) : la grille ne la demande pas,
/// Supprimer reste une demonstration ; la liste des imprimantes physiques proposees (ImprimanteWindows.GetAllImprimantesWindows, base
/// commune et serveur Windows) : la V3 prend le nom saisi sans le controler ; la confirmation « Ajouter l'imprimante '...' dans le
/// greffe ... ? » (une question, pas un controle) ; les autres onglets (base commune, LecteurImprimantesGreffe.cs).
/// TRACE : famille « reglage », « a cree » / « a modifie ».
/// </summary>
internal sealed class EcrivainImprimantesGreffe : IEcrivainEcran
{
    public string Procedure => "PROC_IMP_WIN";
    public string Ecran => "Imprimantes du greffe";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Modifier];

    /// <summary>Les types de papier d'une imprimante lot 2 ajoutee (OPE_SAISIE_IMP_LOT2.cs:190-192).</summary>
    internal static readonly string[] TypesAjout = ["NORMAL", "SECURISE", "LRAR"];

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct) => geste.Demande.Type switch
    {
        TypeGeste.Ajouter => AjouterAsync(geste, ct),
        _ => ModifierAsync(geste, ct),
    };

    private static string? Cellule(GesteEnCours geste, string colonne) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v.Trim() : null;

    private static string? Trop(string libelle, string valeur, int taille)
        => valeur.Length > taille ? $"« {libelle} » : {taille} caractères au plus ({valeur.Length} saisis)." : null;

    /// <summary>Les types de papier saisis (« normal, A3 ») : en majuscules, sans « BAC_ », dans l'ordre ; le doublon refuse.</summary>
    internal static (string[] Types, string? Refus) Types(string saisie)
    {
        var types = new List<string>();
        foreach (var brut in saisie.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var type = brut.ToUpperInvariant().Replace("BAC_", "", StringComparison.Ordinal);
            if (types.Contains(type)) return ([], $"Le type de papier '{type}' existe déjà.");
            types.Add(type);
        }
        return ([.. types], null);
    }

    private static async Task<IssueGeste> AjouterAsync(GesteEnCours geste, CancellationToken ct)
    {
        var nom = Cellule(geste, ColNom) ?? "";
        var physique = Cellule(geste, ColPhysique) ?? "";
        var lot = Cellule(geste, ColLot) ?? "";
        if (nom.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ColNom));
        if (physique.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ColPhysique));
        if (lot.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ColLot));
        if (lot is not (Lot1 or Lot2)) return IssueGeste.Refuser($"« {ColLot} » : {lot} n'est ni « {Lot1} » ni « {Lot2} ».");
        var lot1 = lot == Lot1;
        if ((Trop(ColNom, nom, lot1 ? 50 : 255) ?? Trop(ColPhysique, physique, lot1 ? 100 : 255)) is { } tropLong) return IssueGeste.Refuser(tropLong);
        if (!lot1 && (Cellule(geste, ColDifferee) is { Length: > 0 } || Cellule(geste, ColActive) is { Length: > 0 }))
            return IssueGeste.Refuser($"« {ColDifferee} » et « {ColActive} » ne valent que pour une imprimante {Lot1} : la saisie lot 2 de RIG v2 ne les a pas.");
        if (lot1 && Cellule(geste, ColTypes) is { Length: > 0 })
            return IssueGeste.Refuser($"« {ColTypes} » ne vaut que pour une imprimante {Lot2} : la saisie lot 1 de RIG v2 ne l'a pas.");
        if (await LireAsync((sql, p) => geste.LireAsync(sql, ct, p), $"{(lot1 ? "1" : "2")}:{nom}") is not null)
            return IssueGeste.Refuser($"L'imprimante {lot.ToLowerInvariant()} '{nom}' existe déjà dans le greffe.");

        Imprimante ajoutee;
        if (lot1)
        {
            ajoutee = new Imprimante(Lot1, nom, physique, Cellule(geste, ColDifferee) == "oui", Cellule(geste, ColActive) == "oui", "", false);
            await geste.EcrireAsync("""
                INSERT INTO v2.IMPRIMANTE_PS (R_IMPRIMANTE_VALEUR, R_IMPRIMANTE_LIBELLE_ECRAN, R_IMPRIMANTE_SYNONYME, R_IMPRIMANTE_ACTIVATION,
                    R_IMPRIMANTE_DIFFEREE, R_IMPRIMANTE_EMPLACEMENT_IMP, R_IMPRIMANTE_UTILISABLE_SAISIE, R_IMPRIMANTE_ETAT, R_IMPRIMANTE_CONFIG_TYPE)
                VALUES (@nom, @nom, @nom, @active, @differee, @physique, 0, '0', 0)
                """, ct, new SqlParameter("@nom", nom), new SqlParameter("@active", ajoutee.Active), new SqlParameter("@differee", ajoutee.Differee),
                new SqlParameter("@physique", physique));
        }
        else
        {
            ajoutee = new Imprimante(Lot2, nom, physique, false, true, string.Join(", ", TypesAjout.Order(StringComparer.Ordinal)), false);
            await geste.EcrireAsync("INSERT INTO v2.IMPRIMANTE_EDITION (IMPED_NOM_LOGIQUE, IMPED_NOM_PHYSIQUE, IMPED_ONLY_AMITEL) VALUES (@nom, @physique, 0)", ct,
                new SqlParameter("@nom", nom), new SqlParameter("@physique", physique));
            foreach (var type in TypesAjout) await AjouterTagAsync(geste, nom, type, ct);
        }
        return IssueGeste.Ecrit($"Imprimante {lot.ToLowerInvariant()} '{nom}' ajoutée dans le greffe, redirigée vers {physique}" +
                                (lot1 ? "." : $", types de papier {string.Join(", ", TypesAjout)}."),
            [ajoutee.Id], new TraceGeste("reglage", $"l'imprimante {lot.ToLowerInvariant()} {nom}", ajoutee.Id, "a cree", null, ajoutee));
    }

    private static async Task<IssueGeste> ModifierAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (await LireAsync((sql, p) => geste.LireAsync(sql, ct, p), id) is not { } avant)
            return IssueGeste.Refuser($"Imprimante introuvable dans la base du greffe ({id}).");
        var lot1 = avant.Lot == Lot1;
        if (Cellule(geste, ColNom) is { } nom && nom != avant.Nom)
            return IssueGeste.Refuser($"Le nom d'une imprimante enregistrée ne se change pas : RIG v2 le met en lecture seule. Ajoutez '{nom}' puis supprimez '{avant.Nom}' dans RIG v2.");
        if (Cellule(geste, ColLot) is { } lot && lot != avant.Lot)
            return IssueGeste.Refuser($"Une imprimante ne change pas de lot : le lot 1 et le lot 2 sont deux tables de RIG v2 (IMPRIMANTE_PS, IMPRIMANTE_EDITION).");
        if (!lot1 && (Cellule(geste, ColDifferee) is not null || Cellule(geste, ColActive) is not null))
            return IssueGeste.Refuser($"« {ColDifferee} » et « {ColActive} » ne valent que pour une imprimante {Lot1} : la saisie lot 2 de RIG v2 ne les a pas.");
        if (lot1 && Cellule(geste, ColTypes) is not null)
            return IssueGeste.Refuser($"« {ColTypes} » ne vaut que pour une imprimante {Lot2} : la saisie lot 1 de RIG v2 ne l'a pas.");
        var physique = Cellule(geste, ColPhysique) ?? avant.Physique;
        if (physique.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ColPhysique));
        if (Trop(ColPhysique, physique, lot1 ? 100 : 255) is { } tropLong) return IssueGeste.Refuser(tropLong);
        string[]? types = null;
        if (!lot1 && Cellule(geste, ColTypes) is { } saisis)
        {
            var (lus, refus) = Types(saisis);
            if (refus is not null) return IssueGeste.Refuser(refus);
            types = lus;
        }
        var apres = avant with
        {
            Physique = physique,
            Differee = lot1 && Cellule(geste, ColDifferee) is { } d ? d == "oui" : avant.Differee,
            Active = lot1 && Cellule(geste, ColActive) is { } a ? a == "oui" : avant.Active,
            TypesPapier = types is null ? avant.TypesPapier : string.Join(", ", types.Order(StringComparer.Ordinal)),
        };
        if (apres == avant) return IssueGeste.Refuser("Rien à enregistrer : aucune valeur ne change.");

        if (lot1)
            await geste.EcrireAsync("""
                UPDATE v2.IMPRIMANTE_PS SET R_IMPRIMANTE_EMPLACEMENT_IMP = @physique, R_IMPRIMANTE_DIFFEREE = @differee, R_IMPRIMANTE_ACTIVATION = @active
                WHERE R_IMPRIMANTE_VALEUR = @nom
                """, ct, new SqlParameter("@physique", apres.Physique), new SqlParameter("@differee", apres.Differee),
                new SqlParameter("@active", apres.Active), new SqlParameter("@nom", avant.Nom));
        else
        {
            await geste.EcrireAsync("UPDATE v2.IMPRIMANTE_EDITION SET IMPED_NOM_PHYSIQUE = @physique WHERE IMPED_NOM_LOGIQUE = @nom", ct,
                new SqlParameter("@physique", apres.Physique), new SqlParameter("@nom", avant.Nom));
            if (types is not null && apres.TypesPapier != avant.TypesPapier)
            {
                // Tous les tags « bac_ » de l'imprimante, puis un par type (OPE_SAISIE_IMP_LOT2.cs:194-206) ; les autres tags restent.
                await geste.EcrireAsync("DELETE FROM v2.TAG_POSTSCRIPT WHERE TAGPS_NOM_IMPRIMANTE = @nom AND TAGPS_NOM_TAG LIKE 'BAC[_]%'", ct,
                    new SqlParameter("@nom", avant.Nom));
                foreach (var type in types) await AjouterTagAsync(geste, avant.Nom, type, ct);
            }
        }
        return IssueGeste.Ecrit($"Imprimante {avant.Lot.ToLowerInvariant()} '{avant.Nom}' enregistrée dans le greffe.", [avant.Id],
            new TraceGeste("reglage", $"l'imprimante {avant.Lot.ToLowerInvariant()} {avant.Nom}", avant.Id, "a modifie", avant, apres));
    }

    /// <summary>TagPostscript.CreateTagPostscript(« BAC_ » + type, imprimante, « », faux) puis Save (TagPostscript.cs:36-45, :68-88).</summary>
    private static Task<int> AjouterTagAsync(GesteEnCours geste, string imprimante, string type, CancellationToken ct)
        => geste.EcrireAsync("""
            INSERT INTO v2.TAG_POSTSCRIPT (TAGPS_NOM_TAG, TAGPS_NOM_IMPRIMANTE, TAGPS_CHAINE_POSTSCRIPT, TAGPS_BAC_MANUEL) VALUES (@tag, @imprimante, '', 0)
            """, ct, new SqlParameter("@tag", "BAC_" + type), new SqlParameter("@imprimante", imprimante));
}
