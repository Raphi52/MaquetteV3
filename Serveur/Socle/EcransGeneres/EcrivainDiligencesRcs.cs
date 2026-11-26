using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurDiligencesRcs;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Diligences RCS » (PROC_MDIL) : ENREGISTRER une diligence corrigee dans sa fiche. Mode d'emploi : EcrivainEcran.cs ; la
/// fiche : LecteurDiligencesRcs.cs. Plan « RIG v3 100 % fonctionnel », lot 3, 26/11/2026.
///
/// SOURCE (ancien RIG) : OPE_DILIGENCE_RCS (PROC_MDIL\OPE_DILIGENCE_RCS.cs:28-34, zones liees aux colonnes DLGRC_*), sauvee
/// par ETP_DILIGENCE_RCS.DoSauveAfterOperation -> DiligenceRcs.Sauve (ETP_DILIGENCE_RCS.cs:47-58 ; DiligenceRcs.cs:410-430,
/// qui n'ecrit rien d'autre que la diligence, ses relances et ses parametres inchanges).
///
/// CONTROLES DE RIG v2, dans l'ordre, avant toute ecriture (un refus n'ecrit rien) :
///   1. la diligence existe et a un dossier (sinon « Diligence introuvable ») ; seuls les champs des sections Diligence, État
///      et Régularisation se modifient (les parametres et les relances sont des sous-listes en lecture ici) ;
///   2. « Nature de la diligence » est obligatoire (ultComboNatureDiligence.MetierObligatoire, OPE_DILIGENCE_RCS.Designer.cs:96) ;
///   3. les listes n'exigent pas une valeur du referentiel (MetierValeurInexistanteErreur = false, Designer :98, :107, :116,
///      :125) : seule la taille de la colonne borne la saisie (RIG_DEV : origine 3, nature 10, motif 5, etats 1 caractere) ;
///   4. la date de regularisation est une date.
///
/// CE QUI S'ECRIT (v2.DILIGENCE_RCS) : les colonnes des champs modifies. Trace : famille « dossier », « a modifie », avant et
/// apres.
///
/// NON REPRIS (nommes) : la demande MDIL (« Modification des diligences … », PROC_MDIL.cs:28-30), non facturee
/// (MetierFacture = false, PROC_MDIL.Designer.cs:64) : une trace, remplacee par le journal des gestes ; l'action RCS de la
/// diligence (OPE_ACTION_RCS : code processus et etat de l'action), que le descripteur ne porte pas ; la suppression d'une
/// diligence (ETP_DILIGENCE_RCS.cs:50-52), qu'une Fiche ne fait pas.
/// </summary>
internal sealed class EcrivainDiligencesRcs : IEcrivainEcran
{
    /// <summary>Le nom de l'ecran au journal des gestes (le titre du descripteur) : le lecteur le relit pour l'etat.</summary>
    internal const string NomEcran = "Diligences RCS";

    public string Procedure => "PROC_MDIL";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    /// <summary>Les champs ecrits : leur section, leur colonne de v2.DILIGENCE_RCS et sa taille dans RIG_DEV (0 : sans borne).</summary>
    private static readonly (string Section, string Champ, string Colonne, int Taille)[] Colonnes =
    [
        (SectionDiligence, ChampOrigine, "DLGRC_ORIGINE_INFO", 3), (SectionDiligence, ChampNature, "DLGRC_NATURE_DILIGENCE", 10),
        (SectionDiligence, ChampMotif, "DLGRC_MOTIF_DILIGENCE", 5), (SectionEtat, ChampEtatDiligence, "DLGRC_ETAT_DILIGENCE", 1),
        (SectionEtat, ChampEtatRelance, "DLGRC_ETAT_RELANCE", 1), (SectionRegularisation, ChampDateRegularisation, "DLGRC_DATE_REGULARISATION", 0),
        (SectionRegularisation, ChampValeurAttendue, "DLGRC_VALEUR_ATTENDUE", 0),
    ];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // 1. La diligence, verrouillee en ecriture jusqu'a la fin de la transaction ; les champs qui s'enregistrent.
        if (geste.Demande.Ids is not [var id] || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            || await LireDiligenceAsync(geste.Base, geste.Transaction, n, ct) is not { } d)
            return IssueGeste.Refuser($"Diligence introuvable dans la base du greffe ({string.Join(", ", geste.Demande.Ids)}).");
        var saisis = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (cle, valeur) in geste.Demande.Valeurs)
        {
            var colonne = Colonnes.FirstOrDefault(c => GesteEcran.Cle(c.Section, c.Champ) == cle);
            if (colonne.Champ is null) return IssueGeste.Refuser($"« {cle} » ne se modifie pas dans une diligence de RIG v2.");
            saisis[colonne.Champ] = colonne.Champ == ChampValeurAttendue ? valeur : valeur.Trim();
        }

        // 2. La nature est obligatoire.
        if (saisis.TryGetValue(ChampNature, out var nature) && nature.Length == 0)
            return IssueGeste.Refuser($"« {ChampNature} » est obligatoire.");

        // 3. et 4. La taille des colonnes ; la date.
        foreach (var (_, champ, _, taille) in Colonnes.Where(c => saisis.ContainsKey(c.Champ)))
        {
            var v = saisis[champ];
            if (taille > 0 && v.Length > taille)
                return IssueGeste.Refuser($"« {champ} » : {taille} caractère{(taille > 1 ? "s" : "")} au plus ({v.Length} saisis).");
            if (champ == ChampDateRegularisation && v.Length > 0
                && !DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return IssueGeste.Refuser($"« {champ} » : date illisible ({v}).");
        }

        // Ce qui change vraiment (une valeur egale a celle de la base ne s'ecrit pas).
        var avant = d.Valeurs;
        var changes = saisis.Where(s => avant[s.Key] != s.Value).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        if (changes.Count == 0) return IssueGeste.Refuser($"Aucun champ n'est modifié : la diligence {d.Nature} du dossier {d.Dossier} garde ses valeurs.");

        var affectations = new List<string>();
        var parametres = new List<SqlParameter> { new("@id", n) };
        foreach (var (_, champ, colonne, _) in Colonnes.Where(c => changes.ContainsKey(c.Champ)))
        {
            affectations.Add($"{colonne} = @{colonne}");
            var v = changes[champ];
            object valeur = v.Length == 0 ? DBNull.Value
                : champ == ChampDateRegularisation ? DateTime.ParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture) : v;
            parametres.Add(new SqlParameter($"@{colonne}", valeur));
        }
        var touchees = await geste.EcrireAsync($"UPDATE v2.DILIGENCE_RCS SET {string.Join(", ", affectations)} WHERE DLGRC_ID_DLGRC = @id",
                                               ct, [.. parametres]);
        if (touchees != 1) throw new InvalidOperationException($"Diligence {n} : {touchees} ligne(s) mise(s) a jour au lieu d'une.");

        var nom = $"la diligence {changes.GetValueOrDefault(ChampNature, d.Nature)} du dossier {d.Dossier}";
        return IssueGeste.Ecrit($"Diligence {changes.GetValueOrDefault(ChampNature, d.Nature)} du dossier {d.Dossier} enregistrée dans la base du greffe : " +
                                $"{string.Join(", ", Colonnes.Where(c => changes.ContainsKey(c.Champ)).Select(c => $"« {c.Champ} »"))}.",
            [id], new TraceGeste("dossier", nom, id, "a modifie", changes.Keys.ToDictionary(c => c, c => avant[c]), changes));
    }
}
