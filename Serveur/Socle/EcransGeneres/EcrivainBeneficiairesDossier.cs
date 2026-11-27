using System.Globalization;
using System.Text;
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
///      date de naissance, pays de naissance ; et le lieu de naissance, l'une ou l'autre de ses deux zones (la commune en
///      France, OPE_IDENTITE.cs:99 ; le lieu etranger, Designer :134 et OPE_IDENTITE.cs:113) ;
///   3. la commune de naissance (pays « FRA », lieu renseigne) : RIG v2 la retrouve dans COMMUNE par son nom
///      (ULT_COMBO_COMMUNE_NAISSANCE._GetIdValeurFind, :52-76 : R_COMMUNE_COMMUNE, sans accents, tirets ni apostrophes,
///      AmiString.NoAccentPourCompareSql ; plusieurs homonymes -> la ville sans arrondissement 75000/69000/13000, sinon celle
///      du departement saisi). Saisie et retrouvee, elle s'enregistre sous son nom de la table (OPE_IDENTITE.cs:118-121) et,
///      le departement non saisi, le remplit (_SetDepartement, :132-139). Introuvable : le texte saisi s'enregistre tel quel
///      (:122-124), sans controle de compatibilite -- comme RIG v2, qui ne controle qu'une commune retrouvee (:75-78) ;
///   4. DoVerifier (OPE_IDENTITE.cs:71-93), sur les valeurs saisies : pays « FRA » et departement vide -> « La saisie du
///      département est obligatoire quand le pays de naissance est France » ; commune retrouvee d'un autre departement ->
///      « Le département 75 n'est pas compatible avec la commune NICE » (:79-84) ; pays renseigne autre que « FRA » et
///      departement saisi -> « Le département ne doit pas être renseigné quand le pays de naissance n'est pas France » ;
///   5. la taille des colonnes (RIG_DEV : civilite 10, patronyme, prenoms et nom d'usage 100, departement 3, pays 50, lieu de
///      naissance 50, nationalite 50) et la date ;
///   6. la nationalite (facultative, ultNationalite sans MetierObligatoire, Designer :155-156) changee est dans la liste de la
///      V2 : NATIONALITE, ses valeurs utilisables en saisie (ULT_COMBO_NATIONALITE.cs:15-17 ; comme les listes du Répertoire,
///      EcrivainRepertoire.UtilisableEnSaisieAsync).
///
/// L'ADRESSE SE LIT SEULEMENT : ultAdresse (Ult_Adresse, OPE_IDENTITE.cs:60) ouvre la recherche d'adresse de RIG, un ecran a
/// part qui rend l'identifiant d'une ADRESSE_BASE partagee (Ult_Adresse.cs:283-301) ; la V3 n'a pas cet ecran (meme raison
/// que pour le Répertoire, EcrivainRepertoire.cs).
///
/// CE QUI S'ECRIT (v2.BENEFICIAIRE_EFFECTIF) : les colonnes des champs modifies ; un pays vide a l'ouverture vaut « FRA »
/// (OPE_IDENTITE.cs:62-64) et s'enregistre ainsi. Trace : famille « dossier », « a modifie », avant et apres.
///
/// NON REPRIS (nommes) : la mise a jour du Kbis XML apres validation (ETP_LIST_BE._MajKbisXml, ETP_LIST.cs:63) ; la
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
        (ChampPays, "BENEF_PAYS", 50, true), (ChampLieu, "BENEF_LIEU_NAISSANCE", 50, true), (ChampNationalite, "BENEF_NATIONALITE", 50, false),
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
            if (colonne.Champ is null)
                return IssueGeste.Refuser(cle == GesteEcran.Cle(SectionIdentite, ChampAdresse)
                    ? "L'adresse d'un bénéficiaire se choisit dans la recherche d'adresse de RIG v2, que la V3 n'a pas encore : elle ne se modifie pas dans la fiche."
                    : $"« {cle} » ne se modifie pas dans l'identité d'un bénéficiaire de RIG v2.");
            saisis[colonne.Champ] = valeur.Trim();
        }
        var valeurs = new Dictionary<string, string>(b.Valeurs, StringComparer.Ordinal);
        foreach (var (champ, valeur) in saisis) valeurs[champ] = valeur;

        // 2. et 5. Obligatoires, tailles, date.
        foreach (var (champ, _, taille, obligatoire) in Colonnes.Where(c => saisis.ContainsKey(c.Champ)))
        {
            var v = saisis[champ];
            if (obligatoire && v.Length == 0) return IssueGeste.Refuser($"« {champ} » est obligatoire.");
            if (taille > 0 && v.Length > taille) return IssueGeste.Refuser($"« {champ} » : {taille} caractères au plus ({v.Length} saisis).");
            if (champ == ChampNaissance && !DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return IssueGeste.Refuser($"« {champ} » : date illisible ({v}).");
        }

        // 3. La commune de naissance, retrouvee comme la liste de RIG v2 ; saisie, elle prend son nom de la table et remplit le
        //    departement non saisi (OPE_IDENTITE.cs:118-121, :132-139).
        var commune = valeurs[ChampPays] == "FRA" && valeurs[ChampLieu].Length > 0
            ? await CommuneAsync(geste, valeurs[ChampLieu], valeurs[ChampDepartement], ct) : null;
        if (commune is { } saisie && saisis.ContainsKey(ChampLieu))
        {
            saisis[ChampLieu] = valeurs[ChampLieu] = saisie.Nom;
            if (!saisis.ContainsKey(ChampDepartement)) saisis[ChampDepartement] = valeurs[ChampDepartement] = saisie.Departement;
        }

        // 4. DoVerifier (OPE_IDENTITE.cs:71-93), sur les valeurs saisies.
        if (valeurs[ChampPays] == "FRA" && valeurs[ChampDepartement].Length == 0)
            return IssueGeste.Refuser("La saisie du département est obligatoire quand le pays de naissance est France.");
        if (commune is { } retrouvee && valeurs[ChampDepartement].Length > 0 && retrouvee.Departement != valeurs[ChampDepartement])
            return IssueGeste.Refuser($"Le département {valeurs[ChampDepartement]} n'est pas compatible avec la commune {retrouvee.Nom}.");
        if (valeurs[ChampPays] is { Length: > 0 } pays && pays != "FRA" && valeurs[ChampDepartement].Length > 0)
            return IssueGeste.Refuser("Le département ne doit pas être renseigné quand le pays de naissance n'est pas France.");

        var changes = saisis.Where(s => b.Valeurs[s.Key] != s.Value).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        if (changes.Count == 0) return IssueGeste.Refuser($"Aucun champ n'est modifié : le bénéficiaire {b.Nom} garde ses valeurs.");

        // 6. La nationalite changee, dans la liste de la V2.
        if (changes.GetValueOrDefault(ChampNationalite) is { Length: > 0 } nationalite
            && !await EcrivainRepertoire.UtilisableEnSaisieAsync(geste, "v2.NATIONALITE", "R_NATIONALIT", nationalite, ct))
            return IssueGeste.Refuser($"« {ChampNationalite} » : « {nationalite} » n'est pas dans la liste des nationalités utilisables du greffe.");

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

    /// <summary>Une commune de la table COMMUNE de RIG v2 : son nom (R_COMMUNE_COMMUNE) et son departement.</summary>
    private sealed record Commune(string Nom, string Departement);

    /// <summary>La commune nommee <paramref name="lieu"/>, retrouvee comme ULT_COMBO_COMMUNE_NAISSANCE._GetIdValeurFind (:52-76) ;
    /// null si aucune, ou si les homonymes ne se departagent pas.</summary>
    private static async Task<Commune?> CommuneAsync(GesteEnCours geste, string lieu, string departement, CancellationToken ct)
    {
        var trouvees = new List<(Commune Commune, string CodePostal)>();
        await using (var r = await geste.LireAsync("""
            SELECT R_COMMUNE_COMMUNE, R_COMMUNE_CODE_POSTAL, R_COMMUNE_DEPARTEMENT FROM v2.COMMUNE
            WHERE UPPER(R_COMMUNE_COMMUNE) = UPPER(@nom) ORDER BY R_COMMUNE_ID_COMMN
            """, ct, new SqlParameter("@nom", NomCommune(lieu))))
            while (await r.ReadAsync(ct))
                trouvees.Add((new Commune(FormeLigne.Texte(r, "R_COMMUNE_COMMUNE"), FormeLigne.Texte(r, "R_COMMUNE_DEPARTEMENT")),
                              FormeLigne.Texte(r, "R_COMMUNE_CODE_POSTAL")));
        if (trouvees.Count == 1) return trouvees[0].Commune;
        if (trouvees.Count == 0) return null;
        // Plusieurs : la ville sans arrondissement (MetierAllowVilleSansArrond, OPE_IDENTITE.cs:44 ; :59-64), puis le
        // departement saisi (« #departement » dans le texte de recherche, :67-73).
        if (trouvees.Where(t => t.CodePostal is "69000" or "75000" or "13000").ToList() is [var ville]) return ville.Commune;
        if (departement.Length > 0 && trouvees.Where(t => t.Commune.Departement.StartsWith(departement, StringComparison.Ordinal)).ToList() is [var duDepartement])
            return duDepartement.Commune;
        return null;
    }

    /// <summary>Le nom compare a R_COMMUNE_COMMUNE, ecrit comme RIG v2 l'ecrit (ULT_COMBO_COMMUNE_NAISSANCE.cs:86-88) : sans
    /// accents (AmiString.NoAccentPourCompareSql, :442-456 : lettres et chiffres gardes, le reste en espace), tirets et
    /// apostrophes en espaces.</summary>
    internal static string NomCommune(string lieu)
    {
        var nom = new StringBuilder(lieu.Length);
        foreach (var c in lieu.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is '\u0153' or '\u0152') nom.Append("oe");
            else nom.Append(char.IsAsciiLetterOrDigit(c) || c == '\u00B0' ? char.ToLowerInvariant(c) : ' ');
        }
        return nom.ToString().Trim();
    }
}
