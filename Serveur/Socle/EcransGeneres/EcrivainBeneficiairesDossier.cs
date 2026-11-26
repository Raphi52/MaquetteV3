using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurBeneficiairesDossier;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Bénéficiaires d'un dossier » (PROC_BE) : ENREGISTRER l'identite d'un beneficiaire effectif dans sa fiche. Mode d'emploi :
/// EcrivainEcran.cs ; la fiche : LecteurBeneficiairesDossier.cs. Plan « RIG v3 100 % fonctionnel », lot 3, 26/11/2026.
///
/// SOURCE (ancien RIG) : OPE_IDENTITE (PROC_BE\OPE_IDENTITE.cs, zones liees :52-60, DoVerifier :69-95), sauvee par OPE_BE.DoSauve
/// pour un beneficiaire existant : Beneficiaire_Effectif.Sauve (OPE_BE.cs:149-165 ; Beneficiaire_Effectif.cs:614-633).
///
/// CONTROLES DE RIG v2, dans l'ordre, avant toute ecriture (un refus n'ecrit rien) :
///   1. le beneficiaire existe et n'est pas partant (sinon « Bénéficiaire introuvable ») ; seuls les champs de l'identite ;
///   2. obligatoires (MetierObligatoire, OPE_IDENTITE.Designer.cs:89, :107, :125, :144) : patronyme, prenoms de l'etat civil,
///      date de naissance, pays de naissance ;
///   3. DoVerifier (OPE_IDENTITE.cs:71-93), sur les valeurs saisies : pays « FRA » et departement vide -> « La saisie du
///      département est obligatoire quand le pays de naissance est France » ; pays renseigne autre que « FRA » et
///      departement saisi -> « Le département ne doit pas être renseigné quand le pays de naissance n'est pas France » ;
///   4. la taille des colonnes (RIG_DEV : civilite 10, patronyme, prenoms et nom d'usage 100, departement 3, pays 50) et la date.
///
/// CE QUI S'ECRIT (v2.BENEFICIAIRE_EFFECTIF) : les colonnes des champs modifies ; un pays vide a l'ouverture vaut « FRA »
/// (OPE_IDENTITE.cs:62-64) et s'enregistre ainsi. Trace : famille « dossier », « a modifie », avant et apres.
///
/// NON REPRIS (nommes) : la compatibilite departement / commune de naissance (OPE_IDENTITE.cs:75-86), la commune n'etant pas
/// un champ du descripteur ; la mise a jour du Kbis XML apres validation (ETP_LIST_BE._MajKbisXml, ETP_LIST.cs:63) ; la
/// demande BE (« Modification bénéficiaire effectif … », non facturee : PROC_BE.Designer.cs:89), une trace remplacee par le
/// journal des gestes ; l'ajout et la suppression d'un beneficiaire (modes AddBe et ListBe), qu'une Fiche ne fait pas.
///
/// LES DEUX AUTRES SECTIONS DE LA FICHE, ET POURQUOI ELLES NE S'ENREGISTRENT PAS :
///   - « Modalités de contrôle » : RIG v2 la saisit (OPE_MODALITES_CONTROLE, table BEN_EFFEC_MODAL_CTRL), mais son masque
///     n'a aucun champ fixe : il est construit en code selon le type de declaration du dossier -- societe, GIE ou association,
///     placement collectif, ou rien (OPE_MODALITES_CONTROLE.cs:131-193 ; proc_be.md:526-544), une vingtaine de pourcentages
///     et de cases BENMC_* differents. Le descripteur la porte en sous-liste (Champ.Tableau), et une sous-liste se LIT dans
///     une Fiche sans s'y saisir (FicheEcran.cs, ChampFiche.Lignes ; FormeFiche.tsx n'envoie jamais un Tableau). La brancher
///     demande un champ par colonne BENMC_* de chaque masque : un descripteur a trois variantes que la forme Fiche n'a pas ;
///   - « Acte déposé » : RIG v2 n'y ecrit rien. Les deux boutons ne font que montrer : VisualiserActeBe ouvre l'acte depose
///     dans VidiGED (GED externe), VisualiserJsonDibe reaffiche le JSON brut du service DIBE garde dans la demande
///     (PROC_BE.cs:217-238 ; proc_be.md:367-377). Aucun geste a brancher.
/// </summary>
internal sealed class EcrivainBeneficiairesDossier : IEcrivainEcran
{
    /// <summary>Le nom de l'ecran au journal des gestes (le titre du descripteur) : le lecteur le relit pour l'etat.</summary>
    internal const string NomEcran = "Beneficiaires d'un dossier";

    public string Procedure => "PROC_BE";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    /// <summary>Les champs ecrits : leur colonne de v2.BENEFICIAIRE_EFFECTIF, sa taille dans RIG_DEV, et s'il est obligatoire.</summary>
    private static readonly (string Champ, string Colonne, int Taille, bool Obligatoire)[] Colonnes =
    [
        (ChampCivilite, "BENEF_CIVILITE", 10, false), (ChampPatronyme, "BENEF_PATRONYME", 100, true),
        (ChampPrenoms, "BENEF_PRENOM_ETAT_CIVIL", 100, true), (ChampNomUsage, "BENEF_NOM_USAGE", 100, false),
        (ChampNaissance, "BENEF_DATE_NAISSANCE", 0, true), (ChampDepartement, "BENEF_DEPARTEMENT_NAISS", 3, false),
        (ChampPays, "BENEF_PAYS", 50, true),
    ];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // 1. Le beneficiaire, verrouille en ecriture jusqu'a la fin de la transaction ; les champs de l'identite.
        if (geste.Demande.Ids is not [var id] || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            || await LireBeneficiaireAsync(geste.Base, geste.Transaction, n, ct) is not { } b)
            return IssueGeste.Refuser($"Bénéficiaire introuvable dans la base du greffe ({string.Join(", ", geste.Demande.Ids)}).");
        var saisis = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (cle, valeur) in geste.Demande.Valeurs)
        {
            var colonne = Colonnes.FirstOrDefault(c => GesteEcran.Cle(SectionIdentite, c.Champ) == cle);
            if (colonne.Champ is null) return IssueGeste.Refuser($"« {cle} » ne se modifie pas dans l'identité d'un bénéficiaire de RIG v2.");
            saisis[colonne.Champ] = valeur.Trim();
        }
        var valeurs = new Dictionary<string, string>(b.Valeurs, StringComparer.Ordinal);
        foreach (var (champ, valeur) in saisis) valeurs[champ] = valeur;

        // 2. et 4. Obligatoires, tailles, date.
        foreach (var (champ, _, taille, obligatoire) in Colonnes.Where(c => saisis.ContainsKey(c.Champ)))
        {
            var v = saisis[champ];
            if (obligatoire && v.Length == 0) return IssueGeste.Refuser($"« {champ} » est obligatoire.");
            if (taille > 0 && v.Length > taille) return IssueGeste.Refuser($"« {champ} » : {taille} caractères au plus ({v.Length} saisis).");
            if (champ == ChampNaissance && !DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return IssueGeste.Refuser($"« {champ} » : date illisible ({v}).");
        }

        // 3. DoVerifier (OPE_IDENTITE.cs:71-93), sur les valeurs saisies.
        if (valeurs[ChampPays] == "FRA" && valeurs[ChampDepartement].Length == 0)
            return IssueGeste.Refuser("La saisie du département est obligatoire quand le pays de naissance est France.");
        if (valeurs[ChampPays] is { Length: > 0 } pays && pays != "FRA" && valeurs[ChampDepartement].Length > 0)
            return IssueGeste.Refuser("Le département ne doit pas être renseigné quand le pays de naissance n'est pas France.");

        var changes = saisis.Where(s => b.Valeurs[s.Key] != s.Value).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        if (changes.Count == 0) return IssueGeste.Refuser($"Aucun champ n'est modifié : le bénéficiaire {b.Nom} garde ses valeurs.");

        var affectations = new List<string>();
        var parametres = new List<SqlParameter> { new("@id", n) };
        // Le pays affiche « FRA » quand la base est vide : la V2 l'enregistre (OPE_IDENTITE.cs:62-64), la V3 aussi. fix-ok: relu
        // le 26/11/2026 -- le premier jet posait toujours ce CASE puis le retirait par position quand le pays changeait ; il ne
        // se pose plus que si le pays n'est pas saisi (meme ecriture, sans dependre de l'ordre de la liste).
        if (!changes.ContainsKey(ChampPays)) affectations.Add("BENEF_PAYS = CASE WHEN ISNULL(BENEF_PAYS, '') = '' THEN 'FRA' ELSE BENEF_PAYS END");
        foreach (var (champ, colonne, _, _) in Colonnes.Where(c => changes.ContainsKey(c.Champ)))
        {
            affectations.Add($"{colonne} = @{colonne}");
            var v = changes[champ];
            object valeur = v.Length == 0 ? DBNull.Value
                : champ == ChampNaissance ? DateTime.ParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture) : v;
            parametres.Add(new SqlParameter($"@{colonne}", valeur));
        }
        var touchees = await geste.EcrireAsync($"UPDATE v2.BENEFICIAIRE_EFFECTIF SET {string.Join(", ", affectations)} WHERE BENEF_ID_BENEF = @id",
                                               ct, [.. parametres]);
        if (touchees != 1) throw new InvalidOperationException($"Beneficiaire {n} : {touchees} ligne(s) mise(s) a jour au lieu d'une.");

        var nom = Titre(valeurs[ChampPatronyme], valeurs[ChampPrenoms], null);
        return IssueGeste.Ecrit($"Bénéficiaire {nom} du dossier {b.Dossier} enregistré dans la base du greffe : " +
                                $"{string.Join(", ", Colonnes.Where(c => changes.ContainsKey(c.Champ)).Select(c => $"« {c.Champ} »"))}.",
            [id], new TraceGeste("dossier", $"le bénéficiaire effectif {nom} du dossier {b.Dossier}", id, "a modifie",
                changes.Keys.ToDictionary(c => c, c => b.Valeurs[c]), changes));
    }
}
