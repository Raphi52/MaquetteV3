using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurInstanceJudiciaire;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Instance judiciaire » (PROC_MJUD) : ENREGISTRER une instance corrigee dans sa fiche. Mode d'emploi des ecrivains :
/// EcrivainEcran.cs ; la fiche et ce qui s'y modifie : LecteurInstanceJudiciaire.cs (InstanceLue.Modifiables). Plan
/// « RIG v3 100 % fonctionnel », lot 2, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_MJUD n'a pas d'operation propre (proc_mjud.md:188) ; l'instance se corrige dans l'etape
/// « Instance » (Rig_Etape_Instance\INSTANCE\Saisie\ETP_INSTANCE\OPE_INSTANCE.cs), sauvee par Instance.Sauve.
///
/// CONTROLES DE RIG v2, dans l'ordre, avant toute ecriture (un refus n'ecrit rien) :
///   1. l'instance existe et est une instance de MJUD (sinon « Instance introuvable ») ;
///   2. elle n'est pas verrouillee par une autre demande : le message de RIG v2 « Instance bloquée (n°), demande "…" : … par … »
///      (VerrouDemandeV2 ; PROC_MJUD.cs:126-133 n'entre pas en saisie) ;
///   3. chaque champ envoye se modifie sur CETTE instance (InstanceLue.Modifiables, d'apres OPE_INSTANCE.cs:1681-1746) ;
///   4. Saisine tribunal et Date de l'acte sont obligatoires (MetierObligatoire, OPE_INSTANCE.Designer.cs:168, :180) ;
///   5. VerifierDates (OPE_INSTANCE.cs:1325-1350), avec les valeurs saisies : « La date de l'acte doit être inférieure ou
///      égale à la date d'enrôlement » ; « La date de saisine du Tribunal doit être inférieure ou égale à la date
///      d'enrôlement » ; « La date de l'acte doit être inférieure ou égale à la date de saisine du Tribunal ». Pour une
///      assignation, la date de l'acte controlee est celle des defendeurs (InstanceLue.DateActeControlee).
///
/// CE QUI S'ECRIT (v2.INSTANCE) : les champs modifies, et comme Instance.Sauve (Instance.cs:3895-3910) : INSTN_DATE_MAJ_INFOG
/// a maintenant si INSTN_TOP_EXPERIAN ou si la derniere mise a jour date d'un jour ou plus ; INSTN_NUMERO_SOURCE pose s'il
/// est vide (AmiSystem.GetGuid : un GUID en majuscules, AmiSystem.cs:481-484). Trace : famille « affaire », « a modifie »,
/// avant et apres des champs modifies.
///
/// NON REPRIS (nommes) : la demande MJUD que RIG v2 cree et valide pour chaque modification (la V3 trace au journal des
/// gestes) ; MAJNomInstance (le nom courant se recalcule des parties, que cet ecran ne modifie pas) ; la facturation
/// (MetierFacture = false par defaut, PROC_MJUD.Designer.cs:323) ; le declencheur V2 de TOP_EXPERIAN (absent de la base V3).
/// </summary>
internal sealed class EcrivainInstanceJudiciaire : IEcrivainEcran
{
    /// <summary>Le nom de l'ecran au journal des gestes (le titre du descripteur) : le lecteur le relit pour l'etat « Modifiée ».</summary>
    internal const string NomEcran = "Instance judiciaire";

    public string Procedure => "PROC_MJUD";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    /// <summary>Les champs ecrits, leur colonne de v2.INSTANCE.</summary>
    private static readonly (string Champ, string Colonne)[] Colonnes =
    [
        (ChampDateActe, "INSTN_DATE_ACTE"), (ChampSaisine, "INSTN_DATE_SAISINE_GREFFE"),
        (ChampIpEuropeenne, "INSTN_IP_EUROPEENNE"), (ChampNote, "INSTN_NOTE_AUDIENCE"),
    ];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // 1. L'instance, verrouillee en ecriture jusqu'a la fin de la transaction.
        if (geste.Demande.Ids is not [var id] || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            || await LireInstanceAsync(geste.Base, geste.Transaction, n, ct) is not { } i)
            return IssueGeste.Refuser($"Instance introuvable dans la base du greffe ({string.Join(", ", geste.Demande.Ids)}).");

        // 2. Le verrou de RIG v2, relu dans la transaction.
        if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance, id, ct) is { } verrou)
            return IssueGeste.Refuser(verrou);

        // 3. Ce que l'ecran envoie : des champs de la section Instance que CETTE instance laisse modifier.
        var modifiables = i.Modifiables;
        var saisis = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (cle, valeur) in geste.Demande.Valeurs)
        {
            var libelle = cle.StartsWith(GesteEcran.Cle(SectionInstance, ""), StringComparison.Ordinal) ? cle[GesteEcran.Cle(SectionInstance, "").Length..] : null;
            if (libelle is null || !modifiables.Contains(libelle))
                return IssueGeste.Refuser($"« {cle} » ne se modifie pas sur l'instance {i.Role} dans RIG v2.");
            saisis[libelle] = valeur.Trim();
        }

        // 4. Les valeurs saisies, lues ; les obligatoires.
        DateTime? dateActe = i.DateActe, saisine = i.Saisine;
        foreach (var (champ, valeur) in saisis.Where(s => s.Key is ChampDateActe or ChampSaisine))
        {
            if (valeur.Length == 0) return IssueGeste.Refuser($"« {champ} » est obligatoire.");
            if (!DateTime.TryParseExact(valeur, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return IssueGeste.Refuser($"« {champ} » : date illisible ({valeur}).");
            if (champ == ChampDateActe) dateActe = d; else saisine = d;
        }

        // 5. VerifierDates (OPE_INSTANCE.cs:1325-1350), sur les valeurs saisies.
        var controlee = i.DateActeControlee(dateActe);
        var erreurs = new List<string>();
        if (controlee > i.Enrolement) erreurs.Add("La date de l'acte doit être inférieure ou égale à la date d'enrôlement");
        if (saisine > i.Enrolement) erreurs.Add("La date de saisine du Tribunal doit être inférieure ou égale à la date d'enrôlement");
        if (saisine is not null && controlee > saisine) erreurs.Add("La date de l'acte doit être inférieure ou égale à la date de saisine du Tribunal");
        if (erreurs.Count > 0) return IssueGeste.Refuser(string.Join(". ", erreurs) + ".");

        // Ce qui change vraiment (une valeur egale a celle de la base ne s'ecrit pas).
        var avant = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ChampDateActe] = FormeLigne.DateFiche(i.DateActe), [ChampSaisine] = FormeLigne.DateFiche(i.Saisine),
            [ChampIpEuropeenne] = FormeLigne.Case(i.IpEuropeenne), [ChampNote] = i.Note ?? "",
        };
        var changes = saisis.Where(s => avant[s.Key] != s.Value).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        if (changes.Count == 0) return IssueGeste.Refuser($"Aucun champ n'est modifié : l'instance {i.Role} garde ses valeurs.");

        // Ecriture, comme Instance.Sauve.
        var affectations = new List<string>();
        var parametres = new List<SqlParameter> { new("@id", n), new("@maintenant", geste.Maintenant),
                                                  new("@guid", Guid.NewGuid().ToString().ToUpperInvariant()) };
        foreach (var (champ, colonne) in Colonnes.Where(c => changes.ContainsKey(c.Champ)))
        {
            affectations.Add($"{colonne} = @{colonne}");
            object valeur = champ switch
            {
                ChampDateActe or ChampSaisine => DateTime.ParseExact(changes[champ], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                ChampIpEuropeenne => changes[champ] == "oui",
                _ => changes[champ].Length == 0 ? DBNull.Value : changes[champ],
            };
            parametres.Add(new SqlParameter($"@{colonne}", valeur));
        }
        // INSTN_DATE_MAJ_INFOG : « if (TOP_EXPERIAN || (DateTime.Now - INSTN_DATE_MAJ_INFOG).Days != 0) » (Instance.cs:3903-3905) ;
        // une date vide est loin dans le passe (DBDateTime nul = DateTime.MinValue), donc posee.
        affectations.Add("""
            INSTN_DATE_MAJ_INFOG = CASE WHEN INSTN_TOP_EXPERIAN = 1 OR INSTN_DATE_MAJ_INFOG IS NULL
                                          OR ABS(DATEDIFF_BIG(second, INSTN_DATE_MAJ_INFOG, @maintenant)) >= 86400
                                        THEN @maintenant ELSE INSTN_DATE_MAJ_INFOG END
            """);
        affectations.Add("INSTN_NUMERO_SOURCE = ISNULL(INSTN_NUMERO_SOURCE, @guid)");
        var touchees = await geste.EcrireAsync($"UPDATE v2.INSTANCE SET {string.Join(", ", affectations)} WHERE INSTN_ID_INSTN = @id",
                                               ct, [.. parametres]);
        if (touchees != 1) throw new InvalidOperationException($"Instance {n} : {touchees} ligne(s) mise(s) a jour au lieu d'une.");

        var champs = string.Join(", ", Colonnes.Where(c => changes.ContainsKey(c.Champ)).Select(c => $"« {c.Champ} »"));
        return IssueGeste.Ecrit($"Instance {i.Role} enregistrée dans la base du greffe : {champs}.", [id],
            new TraceGeste("affaire", $"l'instance {i.Role}", id, "a modifie",
                changes.Keys.ToDictionary(c => c, c => avant[c]), changes));
    }
}
