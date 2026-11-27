using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Representation » (PROC_CREANREP) : AJOUTER et MODIFIER un creancier du repertoire (v2.CREANCIER_REPERTO). Mode d'emploi :
/// EcrivainEcran.cs ; la liste : LecteurRepertoireCreanciers.cs. Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026.
///
/// SOURCE (ancien RIG) : PROC_CREANREP\OPE_CREANREP.cs -- un creancier neuf (CreancierReperto.CreateCreancierReperto, :46-48) ou
/// choisi, ses champs lies (:51-62), DoVerifier (:72-90) puis DoSauve (CreancierRep.Sauve, :91-95). CONTROLES, au message de RIG v2 :
///   - Code abrégé, Civilité, Patronyme, Adresse obligatoires (MetierObligatoire, OPE_CREANREP.Designer.cs) ;
///   - le code abrege deja pris par un autre creancier (compare sans la casse, seulement s'il change) : « Le code abrégé X est déjà
///     utilisé. Merci de saisir une valeur différente. » (_ControleCodeAbrege, :128-139) ;
///   - le patronyme au-dela de 128 caracteres (:82-84) ; les longueurs de la table (code abrege 16, RCS 4, SIREN 10) : refus V3.
/// CE QUI S'ECRIT : la ligne de v2.CREANCIER_REPERTO (colonnes changees seulement pour Modifier) ; l'adresse, fiche reprise
/// rec.Adresse : creee a l'ajout (etat info « 1 », AdresseBase.cs:182), sa voie, son code postal et sa localite modifies sinon.
/// NON REPRIS (nomme) : l'avertissement « Désignation/Nom saisie et infogreffe différent » (:86-89, :140-170 : un appel au service
/// Infogreffe, jamais fait par la V3), la forme juridique et le texte du creancier (masque par la V2, :63-65), l'adresse en champs
/// detailles (numero, type de voie : la voie est saisie en une ligne).
/// </summary>
internal sealed class EcrivainRepertoireCreanciers : IEcrivainEcran
{
    public string Procedure => "PROC_CREANREP";
    public string Ecran => "Répertoire des créanciers";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Modifier];

    // Les colonnes du descripteur (Facturer.cs) et leur colonne de CREANCIER_REPERTO, avec la longueur de la table (SSDT).
    private static readonly (string Libelle, string Colonne, int Longueur)[] Champs =
    [
        ("Code abrégé", "CRRPR_ABREGE", 16), ("Civilité", "CRRPR_CIVILITE", 7), ("Patronyme", "CRRPR_NOM", 512), ("Prénoms", "CRRPR_PRENOMS", 70),
        ("Nom d'usage", "CRRPR_NOM_USAGE", 70), ("Siren", "CRRPR_NUM_SIREN", 10), ("RCS (Ville)", "CRRPR_RCS_VILLE", 4),
    ];
    private static readonly (string Libelle, string Colonne)[] ChampsAdresse = [("Adresse", "NomVoie"), ("Code postal", "CodePostal"), ("Ville", "Localite")];
    private const string Desactive = "Désactivé";

    private static string? Cellule(GesteEnCours geste, string libelle) => geste.Demande.Valeurs.TryGetValue(libelle, out var v) ? v.Trim() : null;

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct) => geste.Demande.Type switch
    {
        TypeGeste.Ajouter => AjouterAsync(geste, ct),
        TypeGeste.Modifier => ModifierAsync(geste, ct),
        var t => Task.FromResult(IssueGeste.Refuser($"Geste {t} : non repris pour le répertoire des créanciers.")),
    };

    private static async Task<string?> ControlerAsync(GesteEnCours geste, IReadOnlyDictionary<string, string> valeurs, string? abregeInitial, int? id, CancellationToken ct)
    {
        // Le patronyme a son propre controle de la V2 (128), plus court que sa colonne (512) : il sort de la boucle des longueurs.
        // fix-ok: mesure le 27/11/2026 -- une boucle qui s'arretait au patronyme laissait passer les colonnes suivantes (un Siren de
        // 11 caracteres tombe en « Erreur technique » 500 sur CRRPR_NUM_SIREN char(10)) ; PROC_CREANREP.gestes bloc 8 le garde
        // (defaut remis : 1 ECHEC ; retire : vert).
        if (valeurs.TryGetValue("Patronyme", out var patronyme) && patronyme.Length > 128)
            return "Le patronyme du créancier est trop long. Un maximum de 128 caractères est accepté.";
        foreach (var (libelle, _, longueur) in Champs.Where(c => c.Libelle != "Patronyme"))
            if (valeurs.TryGetValue(libelle, out var v) && v.Length > longueur) return $"« {libelle} » : {longueur} caractères au plus.";
        if (valeurs.TryGetValue("Code abrégé", out var abrege) && !string.Equals(abrege, abregeInitial, StringComparison.OrdinalIgnoreCase))
        {
            await using var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.CREANCIER_REPERTO WHERE UPPER(CRRPR_ABREGE) = UPPER(@a) AND CRRPR_ID_CRRPR <> @id", ct,
                new SqlParameter("@a", abrege), new SqlParameter("@id", id ?? 0));
            await r.ReadAsync(ct);
            if (r.GetInt32(0) > 0) return $"Le code abrégé {abrege} est déjà utilisé. Merci de saisir une valeur différente.";
        }
        return null;
    }

    private static async Task<IssueGeste> AjouterAsync(GesteEnCours geste, CancellationToken ct)
    {
        var valeurs = Champs.Select(c => c.Libelle).Concat(ChampsAdresse.Select(c => c.Libelle))
                            .ToDictionary(l => l, l => Cellule(geste, l) ?? "");
        foreach (var obligatoire in new[] { "Code abrégé", "Civilité", "Patronyme", "Adresse" })
            if (valeurs[obligatoire].Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(obligatoire));
        if (await ControlerAsync(geste, valeurs, null, null, ct) is { } refus) return IssueGeste.Refuser(refus);

        var adresse = await geste.InsererAsync("rec.Adresse", "Id", new Dictionary<string, object?>
        {
            ["NomVoie"] = valeurs["Adresse"], ["CodePostal"] = Vide(valeurs["Code postal"]), ["Localite"] = Vide(valeurs["Ville"]), ["EtatInfo"] = "1",
        }, ct);
        var ligne = Champs.ToDictionary(c => c.Colonne, c => (object?)Vide(valeurs[c.Libelle]));
        ligne["CRRPR_ID_ADRBS"] = adresse;
        ligne["CRRPR_SUPPRIME"] = CriteresConsultation.OuiNon(Cellule(geste, Desactive)) == true;
        var id = await geste.InsererAsync("v2.CREANCIER_REPERTO", "CRRPR_ID_CRRPR", ligne, ct);
        return IssueGeste.Ecrit($"Créancier {valeurs["Code abrégé"]} ({valeurs["Patronyme"]}) ajouté au répertoire dans la base du greffe.",
            [id.ToString(CultureInfo.InvariantCulture)],
            new TraceGeste("reglage", $"le créancier répertorié {valeurs["Code abrégé"]}", id.ToString(CultureInfo.InvariantCulture), "a cree", null,
                valeurs.Where(v => v.Value.Length > 0).ToDictionary(v => v.Key, v => v.Value)));
    }

    private static object? Vide(string v) => v.Length == 0 ? null : v;

    private static async Task<IssueGeste> ModifierAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids.Length != 1 || !int.TryParse(geste.Demande.Ids[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser("Un seul créancier se modifie à la fois.");
        var avant = new Dictionary<string, string>();
        int? idAdresse;
        await using (var r = await geste.LireAsync("""
            SELECT c.CRRPR_ABREGE, c.CRRPR_CIVILITE, c.CRRPR_NOM, c.CRRPR_PRENOMS, c.CRRPR_NOM_USAGE, c.CRRPR_NUM_SIREN, c.CRRPR_RCS_VILLE,
                   c.CRRPR_SUPPRIME, c.CRRPR_ID_ADRBS, a.NomVoie, a.CodePostal, a.Localite
            FROM v2.CREANCIER_REPERTO AS c WITH (UPDLOCK, HOLDLOCK) LEFT JOIN rec.Adresse AS a ON a.Id = c.CRRPR_ID_ADRBS
            WHERE c.CRRPR_ID_CRRPR = @id
            """, ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Créancier introuvable dans le répertoire ({id}).");
            foreach (var (libelle, colonne, _) in Champs) avant[libelle] = FormeLigne.Texte(r, colonne);
            foreach (var (libelle, colonne) in ChampsAdresse) avant[libelle] = FormeLigne.Texte(r, colonne);
            avant[Desactive] = FormeLigne.Case(CriteresConsultation.Vrai(r, "CRRPR_SUPPRIME"));
            idAdresse = CriteresConsultation.EntierDe(r, "CRRPR_ID_ADRBS");
        }
        var changes = avant.Keys.Select(l => (l, v: Cellule(geste, l))).Where(x => x.v is not null && x.v != avant[x.l]).ToDictionary(x => x.l, x => x.v!);
        if (changes.Count == 0) return IssueGeste.Refuser("Aucune colonne n'est modifiée : le créancier garde les valeurs de la base.");
        foreach (var obligatoire in new[] { "Code abrégé", "Civilité", "Patronyme", "Adresse" })
            if (changes.TryGetValue(obligatoire, out var v) && v.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(obligatoire));
        if (await ControlerAsync(geste, changes, avant["Code abrégé"], id, ct) is { } refus) return IssueGeste.Refuser(refus);

        var parametres = new List<SqlParameter> { new("@id", id) };
        var sets = new List<string>();
        foreach (var (libelle, colonne, _) in Champs.Where(c => changes.ContainsKey(c.Libelle)))
        {
            sets.Add($"{colonne} = @p{parametres.Count}");
            parametres.Add(new SqlParameter($"@p{parametres.Count}", Vide(changes[libelle]) ?? DBNull.Value));
        }
        if (changes.TryGetValue(Desactive, out var desactive))
        {
            sets.Add($"CRRPR_SUPPRIME = @p{parametres.Count}");
            parametres.Add(new SqlParameter($"@p{parametres.Count}", CriteresConsultation.OuiNon(desactive) == true));
        }
        var adresse = ChampsAdresse.Where(c => changes.ContainsKey(c.Libelle)).ToList();
        if (adresse.Count > 0)
        {
            if (idAdresse is { } a)
                await geste.EcrireAsync($"UPDATE rec.Adresse SET {string.Join(", ", adresse.Select((c, k) => $"{c.Colonne} = @a{k}"))} WHERE Id = @adresse", ct,
                    [.. adresse.Select((c, k) => new SqlParameter($"@a{k}", Vide(changes[c.Libelle]) ?? DBNull.Value)), new SqlParameter("@adresse", a)]);
            else
            {
                var nouvelle = await geste.InsererAsync("rec.Adresse", "Id", new Dictionary<string, object?>
                {
                    ["NomVoie"] = Vide(Cellule(geste, "Adresse") ?? ""), ["CodePostal"] = Vide(Cellule(geste, "Code postal") ?? ""),
                    ["Localite"] = Vide(Cellule(geste, "Ville") ?? ""), ["EtatInfo"] = "1",
                }, ct);
                sets.Add($"CRRPR_ID_ADRBS = @p{parametres.Count}");
                parametres.Add(new SqlParameter($"@p{parametres.Count}", nouvelle));
            }
        }
        if (sets.Count > 0) await geste.EcrireAsync($"UPDATE v2.CREANCIER_REPERTO SET {string.Join(", ", sets)} WHERE CRRPR_ID_CRRPR = @id", ct, [.. parametres]);
        var abrege = changes.GetValueOrDefault("Code abrégé") ?? avant["Code abrégé"];
        return IssueGeste.Ecrit($"Créancier {abrege} modifié dans le répertoire de la base du greffe : {string.Join(", ", changes.Keys.Select(k => $"« {k} »"))}.",
            [id.ToString(CultureInfo.InvariantCulture)],
            new TraceGeste("reglage", $"le créancier répertorié {abrege}", id.ToString(CultureInfo.InvariantCulture), "a modifie",
                changes.ToDictionary(c => c.Key, c => avant[c.Key]), changes));
    }
}
