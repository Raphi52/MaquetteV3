using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurRepertoire;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Répertoire » (PROC_MODIF_REPER) : ENREGISTRER un repertorie corrige dans sa fiche. Mode d'emploi : EcrivainEcran.cs ; la
/// fiche : LecteurRepertoire.cs. Plan « RIG v3 100 % fonctionnel », lot 3, 26/11/2026.
///
/// SOURCE (ancien RIG) : OPE_EDITION_MODIF_REPER (PROC_MODIF_REPER\Edition\OPE_EDITION_MODIF_REPER.cs), DoVerifier (:66-79) puis
/// Valider (:208-307), en modification (un repertorie existant : _modeCrea faux, :385).
///
/// CONTROLES DE RIG v2, dans l'ordre, avant toute ecriture (un refus n'ecrit rien) :
///   1. le repertorie existe (sinon « Répertorié introuvable ») ; seuls les champs de la section « Fiche » se modifient ;
///   2. obligatoires : abrege et civilite (MetierObligatoire, OPE_EDITION_MODIF_REPER.Designer.cs:304, :253) ;
///   3. la taille des colonnes (RIG_DEV : abrege 16, designation 512, adresse mail 70, civilite 7) ;
///   4. DoVerifier (:67-77) : un AUTRE repertorie du greffe porte deja l'abrege -> « Un répertorié avec cet abrégé existe déjà.
///      L'abrégé doit être unique. » (la V2 n'a pas d'autre controle : ni designation obligatoire, ni format de l'adresse
///      mail, contrairement aux sections anterieures de la fiche, que le « Detail OPE » corrige).
///
/// CE QUI S'ECRIT, dans la transaction, comme Valider :
///   - v2.REPERTORIE : les colonnes modifiees ; et la remise en ordre du mode de presentation (:210-226) : mode a saisie de
///     representant -> plus de client (PRTRP_ID_CLIEN) ; autre mode -> plus de mandataire postulant (PRTRP_ID_MNDTR_POS) ;
///     sans mode -> ni l'un ni l'autre ; texte d'assistance efface si le mode ne le demande pas (R_MODEPRESEN_SAISIE_ASSISTANT,
///     GestionModePresentation :348-353) ;
///   - v2.PARTIE : abrege ou civilite change et parties liees -> PRT_ABREGE des parties du repertorie = le nouvel abrege
///     (Partie.UpdatePartiesRepertorie, Partie.cs:1607-1615 ; :245-251) ;
///   - v2.CODE_AFFAIRE : abrege change -> R_CODEAFFAIR_ABREGE_DEMANDEUR passe de l'ancien au nouvel abrege
///     (CODE_AFFAIRE.UpdateAbregeDemandeur ; :255-261).
///   Trace : famille « reglage », « a modifie », avant et apres.
///
/// NON REPRIS (nommes) : la demande de tracabilite MODIFREPER creee et cloturee d'un coup (GestionDemande, :314-318 ; non
/// facturee, PROC_MODIF_REPER.cs:11-12), remplacee par le journal des gestes ; l'ajout et la suppression d'un repertorie (le
/// bouton AJOUTER et la barre de l'OPE, :86-152), qu'une Fiche ne fait pas ; la civilite appliquee aux parties cochees
/// (OPE_EDITION_PARTIES.Valider, :50-86), une Fiche ne cochant pas les lignes d'une sous-liste ; les autres zones de la
/// fiche V2 (adresse, client, mandataire postulant, mode de presentation...), absentes du descripteur.
/// </summary>
internal sealed class EcrivainRepertoire : IEcrivainEcran
{
    /// <summary>Le nom de l'ecran au journal des gestes (le titre du descripteur) : le lecteur le relit pour l'etat.</summary>
    internal const string NomEcran = "Repertoire";

    public string Procedure => "PROC_MODIF_REPER";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    /// <summary>Les champs ecrits : leur colonne de v2.REPERTORIE, sa taille dans RIG_DEV (0 : une case), et s'il est obligatoire.</summary>
    private static readonly (string Champ, string Colonne, int Taille, bool Obligatoire)[] Colonnes =
    [
        (ChampAbrege, "PRTRP_ABREGE", 16, true), (ChampDesignation, "PRTRP_DESIGNATION", 512, false),
        (ChampMail, "PRTRP_ADRESSE_MAIL", 70, false), (ChampCivilite, "PRTRP_CIVILITE", 7, true), (ChampActif, "PRTRP_ACTIF", 0, false),
    ];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // 1. Le repertorie, verrouille en ecriture jusqu'a la fin de la transaction ; les champs de la fiche.
        if (geste.Demande.Ids is not [var id] || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            || await LireRepertorieAsync(geste.Base, geste.Transaction, n, ct) is not { } rep)
            return IssueGeste.Refuser($"Répertorié introuvable dans la base du greffe ({string.Join(", ", geste.Demande.Ids)}).");
        var saisis = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (cle, valeur) in geste.Demande.Valeurs)
        {
            var colonne = Colonnes.FirstOrDefault(c => GesteEcran.Cle(SectionRepertorie, c.Champ) == cle);
            if (colonne.Champ is null) return IssueGeste.Refuser($"« {cle} » ne se modifie pas dans la fiche d'un répertorié de RIG v2.");
            saisis[colonne.Champ] = valeur.Trim();
        }

        // 2. et 3. Obligatoires, tailles.
        foreach (var (champ, _, taille, obligatoire) in Colonnes.Where(c => saisis.ContainsKey(c.Champ)))
        {
            var v = saisis[champ];
            if (obligatoire && v.Length == 0) return IssueGeste.Refuser($"« {champ} » est obligatoire.");
            if (taille > 0 && v.Length > taille) return IssueGeste.Refuser($"« {champ} » : {taille} caractères au plus ({v.Length} saisis).");
        }

        var avant = rep.Valeurs;
        var changes = saisis.Where(s => avant[s.Key] != s.Value).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        if (changes.Count == 0) return IssueGeste.Refuser($"Aucun champ n'est modifié : le répertorié {avant[ChampAbrege]} garde ses valeurs.");

        // 4. DoVerifier : l'abrege est unique dans le greffe (Repertorie.GetRepertorieByAbrege, egalite SQL).
        var ancienAbrege = avant[ChampAbrege];
        var abrege = changes.GetValueOrDefault(ChampAbrege, ancienAbrege);
        await using (var r = await geste.LireAsync("SELECT COUNT(*) AS N FROM v2.REPERTORIE WITH (UPDLOCK, HOLDLOCK) WHERE PRTRP_ABREGE = @abrege AND PRTRP_ID_PRTRP <> @id",
                                                    ct, new SqlParameter("@abrege", abrege), new SqlParameter("@id", n)))
            if (await r.ReadAsync(ct) && Convert.ToInt32(r["N"], CultureInfo.InvariantCulture) > 0)
                return IssueGeste.Refuser("Un répertorié avec cet abrégé existe déjà. L'abrégé doit être unique.");

        // Valider : le repertorie, avec la remise en ordre du mode de presentation (:210-226).
        var affectations = new List<string>
        {
            """
            PRTRP_ID_CLIEN = CASE WHEN EXISTS (SELECT 1 FROM v2.MODE_PRESENTATION AS mp WHERE mp.R_MODEPRESEN_VALEUR = PRTRP_MODE_PRESENTATION
                                                AND mp.R_MODEPRESEN_SAISIE_REPRESENTANT = 0) THEN PRTRP_ID_CLIEN END,
            PRTRP_ID_MNDTR_POS = CASE WHEN EXISTS (SELECT 1 FROM v2.MODE_PRESENTATION AS mp WHERE mp.R_MODEPRESEN_VALEUR = PRTRP_MODE_PRESENTATION
                                                    AND mp.R_MODEPRESEN_SAISIE_REPRESENTANT = 1) THEN PRTRP_ID_MNDTR_POS END,
            PRTRP_TEXTE_ASSISTANCE = CASE WHEN EXISTS (SELECT 1 FROM v2.MODE_PRESENTATION AS mp WHERE mp.R_MODEPRESEN_VALEUR = PRTRP_MODE_PRESENTATION
                                                        AND mp.R_MODEPRESEN_SAISIE_ASSISTANT = 1) THEN PRTRP_TEXTE_ASSISTANCE END
            """,
        };
        var parametres = new List<SqlParameter> { new("@id", n) };
        foreach (var (champ, colonne, _, _) in Colonnes.Where(c => changes.ContainsKey(c.Champ)))
        {
            affectations.Add($"{colonne} = @{colonne}");
            var v = changes[champ];
            object valeur = champ == ChampActif ? v == "oui" : v.Length == 0 ? DBNull.Value : v;
            parametres.Add(new SqlParameter($"@{colonne}", valeur));
        }
        var touchees = await geste.EcrireAsync($"UPDATE v2.REPERTORIE SET {string.Join(", ", affectations)} WHERE PRTRP_ID_PRTRP = @id", ct, [.. parametres]);
        if (touchees != 1) throw new InvalidOperationException($"Repertorie {n} : {touchees} ligne(s) mise(s) a jour au lieu d'une.");

        // Les parties liees (:245-251) et les codes affaire (:255-261).
        if (changes.ContainsKey(ChampAbrege) || changes.ContainsKey(ChampCivilite))
            await geste.EcrireAsync("UPDATE v2.PARTIE SET PRT_ABREGE = @abrege WHERE PRT_ID_PRTRP = @id", ct,
                                    new SqlParameter("@abrege", abrege), new SqlParameter("@id", n));
        if (changes.ContainsKey(ChampAbrege))
            await geste.EcrireAsync("UPDATE v2.CODE_AFFAIRE SET R_CODEAFFAIR_ABREGE_DEMANDEUR = @nouvel WHERE R_CODEAFFAIR_ABREGE_DEMANDEUR = @ancien", ct,
                                    new SqlParameter("@nouvel", abrege), new SqlParameter("@ancien", ancienAbrege));

        return IssueGeste.Ecrit($"Répertorié {abrege} enregistré dans la base du greffe : " +
                                $"{string.Join(", ", Colonnes.Where(c => changes.ContainsKey(c.Champ)).Select(c => $"« {c.Champ} »"))}.",
            [id], new TraceGeste("reglage", $"le répertorié {abrege}", id, "a modifie", changes.Keys.ToDictionary(c => c, c => avant[c]), changes));
    }
}
