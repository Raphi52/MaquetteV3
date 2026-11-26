using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurPartiesInstance;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Parties d'une instance » (PROC_PARTIES) : ENREGISTRER le nom et l'aide juridictionnelle d'une partie dans sa fiche. Mode d'emploi :
/// EcrivainEcran.cs ; la fiche : LecteurPartiesInstance.cs. Plan « RIG v3 100 % fonctionnel », lot 3, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_PARTIES ouvre la saisie d'une partie existante (proc_parties.md:176 : « la saisie est donc
/// ouverte » ; en PARTIES, MajDisabledUlts rend tous les champs saisissables a une partie deja enregistree,
/// OPE_PARTIE.cs:843-853) ; la seule regle que PROC_PARTIES porte lui-meme est l'aide juridictionnelle (« Detail OPE »,
/// Verification et Sauvegarde) ; la partie se sauve avec son instance (AddSqlBaseASauver(Instance), PROC_PARTIES.cs:205).
///
/// CE QUI SE MODIFIE ICI : le nom (txtDesignation -> PRT_DESIGNATION, OPE_PARTIE.cs:387) et l'aide juridictionnelle
/// (OPE_PARTIE.cs:390-428). Le reste d'OPE_PARTIE ne l'est pas, une raison pour chacun : l'adresse est une fiche reprise
/// (rec.Adresse) que le descripteur montre en une ligne, pas en champs de saisie ; le dossier RCS, le non-inscrit et le SIREN
/// se posent par une recherche Infogreffe ou RCS (OPE_PARTIE.cs:991-1056 ; Infogreffe est un service externe) ; le
/// mandataire et le repertorie se choisissent dans leurs tables avec leurs propres controles (abrege unique,
/// OPE_PARTIE.cs:1967-1975), un choix dans une liste que le descripteur ne porte pas (« Mandataire » est un texte lu).
///
/// CONTROLES DE RIG v2, dans l'ordre, avant toute ecriture (un refus n'ecrit rien) :
///   1. la partie existe et est dans la liste (sinon « Partie introuvable ») ;
///   2. seuls le nom (section « Identité ») et les champs de la section « Aide juridictionnelle » se modifient ici ; le nom
///      est obligatoire (txtDesignation.MetierObligatoire, OPE_PARTIE.Designer.cs:175) et tient dans PRT_DESIGNATION
///      (varchar(512) dans RIG_DEV) ;
///   3. Partielle, Totale et le numero ne se saisissent que si la case AJ est cochee : RIG v2 les masque sinon
///      (ChangementValeurAideJuridictionnelle, OPE_PARTIE.cs:1540-1550) ;
///   4. DoVerifier (PROC_PARTIES.cs:189-200 ; OPE_PARTIE.cs:218-224) : case AJ cochee et Partielle == Totale (aucune ou les
///      deux) -> « Lorsque la case Aide juridictionnelle est cochée, il faut choisir entre AJ Partielle et AJ Totale » ;
///   5. le numero d'AJ tient dans sa colonne (PRT_AIDE_JURIDIC_NUM, varchar(30) dans RIG_DEV).
///
/// CE QUI S'ECRIT :
///   - v2.PARTIE : PRT_AIDE_JURIDIC_TOTALE, _PARTIELLE, _NUM ; case AJ decochee -> numero vide, Partielle et Totale
///     decochees (SauvegardeAideJuridictionnelle, OPE_PARTIE.cs:226-232) ; et comme Partie.Sauve (Partie.cs:1409-1415) : mode
///     de presentation « A » s'il est vide, numero source pose s'il est vide (un GUID en majuscules), PRT_NOM = PRT_DESIGNATION ;
///     le nom saisi va dans PRT_DESIGNATION et PRT_NOM ;
///   - v2.INSTANCE de la partie, comme Instance.Sauve (Instance.cs:3903-3909) : INSTN_DATE_MAJ_INFOG a maintenant si
///     INSTN_TOP_EXPERIAN ou si la derniere mise a jour date d'un jour ou plus ; INSTN_NUMERO_SOURCE pose s'il est vide ;
///     INSTN_NOM_COURANT recalcule (MAJNomInstance, Instance.cs:849-962) quand le premier demandeur ou le premier defendeur
///     change de nom : « demandeur # defendeur », ou, si le nom a ete retouche a la main, l'ancien nom seul y est remplace.
///   Trace : famille « affaire », « a modifie », avant et apres des champs modifies ; une seconde pour le nom de l'instance.
///
/// NON REPRIS (nommes) : la demande PARTIES (non facturee, PROC_PARTIES.Designer.cs:83 : une trace, remplacee par le journal
/// des gestes) ; la renumerotation des parties a chaque lecture de la liste (Instance.cs:1236-1250 : la V3 lit l'ordre sans le
/// reecrire) ; les mots directeurs de l'instance, que PARTIES ne regenere pas (l'etape instance est DontSave,
/// PROC_PARTIES.cs:30 ; OPE_INSTANCE.cs:1424-1433).
/// </summary>
internal sealed class EcrivainPartiesInstance : IEcrivainEcran
{
    /// <summary>Le nom de l'ecran au journal des gestes (le titre du descripteur) : le lecteur le relit pour l'etat.</summary>
    internal const string NomEcran = "Parties d'une instance";

    public string Procedure => "PROC_PARTIES";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    /// <summary>La taille de PRT_AIDE_JURIDIC_NUM (INFORMATION_SCHEMA.COLUMNS de RIG_DEV, lu le 26/11/2026).</summary>
    private const int TailleNumero = 30;

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // 1. La partie, verrouillee en ecriture jusqu'a la fin de la transaction.
        if (geste.Demande.Ids is not [var id] || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            || await LirePartieAsync(geste.Base, geste.Transaction, n, ct) is not { } p)
            return IssueGeste.Refuser($"Partie introuvable dans la base du greffe ({string.Join(", ", geste.Demande.Ids)}).");

        // 2. Le nom et les champs de l'aide juridictionnelle, et eux seuls.
        var section = GesteEcran.Cle(SectionAj, "");
        var saisis = new Dictionary<string, string>(StringComparer.Ordinal);
        string? nomSaisi = null;
        foreach (var (cle, valeur) in geste.Demande.Valeurs)
        {
            if (cle == GesteEcran.Cle(SectionIdentite, ChampNom)) { nomSaisi = valeur.Trim(); continue; }
            var libelle = cle.StartsWith(section, StringComparison.Ordinal) ? cle[section.Length..] : null;
            if (libelle is not (ChampAj or ChampPartielle or ChampTotale or ChampNumeroAj))
                return IssueGeste.Refuser($"« {cle} » ne se modifie pas dans les parties de RIG v2 depuis cet écran.");
            saisis[libelle] = valeur.Trim();
        }
        if (nomSaisi is { Length: 0 }) return IssueGeste.Refuser($"« {ChampNom} » est obligatoire.");
        if (nomSaisi is { Length: > TailleDesignation })
            return IssueGeste.Refuser($"« {ChampNom} » : {TailleDesignation} caractères au plus ({nomSaisi.Length} saisis).");
        var designation = nomSaisi ?? p.Designation;
        bool Case(string champ, bool actuelle) => saisis.TryGetValue(champ, out var v) ? v == "oui" : actuelle;
        var aj = Case(ChampAj, p.Aj);
        var partielle = Case(ChampPartielle, p.Partielle);
        var totale = Case(ChampTotale, p.Totale);
        var numero = saisis.TryGetValue(ChampNumeroAj, out var num) ? num : p.NumeroAj;

        // 3. Partielle, Totale et le numero sont masques tant que la case AJ n'est pas cochee.
        if (!aj && saisis.Keys.FirstOrDefault(c => c != ChampAj && (c == ChampNumeroAj ? numero.Length > 0 : saisis[c] == "oui")) is { } masque)
            return IssueGeste.Refuser($"« {masque} » ne se saisit que si la case « {ChampAj} » est cochée (RIG v2 la masque sinon).");

        // 4. DoVerifier (PROC_PARTIES.cs:192).
        if (aj && partielle == totale)
            return IssueGeste.Refuser("Lorsque la case Aide juridictionnelle est cochée, il faut choisir entre AJ Partielle et AJ Totale.");

        // 5. La colonne du numero.
        if (numero.Length > TailleNumero)
            return IssueGeste.Refuser($"« {ChampNumeroAj} » : {TailleNumero} caractères au plus ({numero.Length} saisis).");

        // SauvegardeAideJuridictionnelle : case AJ decochee -> tout est vide.
        if (!aj) (partielle, totale, numero) = (false, false, "");
        var avant = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ChampNom] = p.Designation, [ChampAj] = FormeLigne.Case(p.Aj), [ChampPartielle] = FormeLigne.Case(p.Partielle),
            [ChampTotale] = FormeLigne.Case(p.Totale), [ChampNumeroAj] = p.NumeroAj,
        };
        var apres = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ChampNom] = designation, [ChampAj] = FormeLigne.Case(aj), [ChampPartielle] = FormeLigne.Case(partielle),
            [ChampTotale] = FormeLigne.Case(totale), [ChampNumeroAj] = numero,
        };
        var changes = apres.Keys.Where(c => avant[c] != apres[c]).ToList();
        if (changes.Count == 0) return IssueGeste.Refuser($"Aucun champ n'est modifié : la partie {p.Nom} garde ses valeurs.");

        // Le nom courant de l'instance, calcule avant que la partie ne change (MAJNomInstance compare a l'instance en base).
        var nomInstance = await NomCourantAsync(geste, p, designation, ct);

        // Ecriture, comme Partie.Sauve puis Instance.Sauve.
        var guid = new SqlParameter("@guid", Guid.NewGuid().ToString().ToUpperInvariant());
        var touchees = await geste.EcrireAsync("""
            UPDATE v2.PARTIE SET PRT_AIDE_JURIDIC_TOTALE = @totale, PRT_AIDE_JURIDIC_PARTIELLE = @partielle,
                   PRT_AIDE_JURIDIC_NUM = @numero, PRT_DESIGNATION = @designation,
                   PRT_MODE_PRESENTATION = CASE WHEN ISNULL(PRT_MODE_PRESENTATION, '') = '' THEN 'A' ELSE PRT_MODE_PRESENTATION END,
                   PRT_NUMERO_SOURCE = ISNULL(PRT_NUMERO_SOURCE, @guid), PRT_NOM = @designation
            WHERE PRT_ID_PRT = @id
            """, ct, new SqlParameter("@totale", totale), new SqlParameter("@partielle", partielle),
            new SqlParameter("@numero", numero.Length == 0 ? DBNull.Value : numero), new SqlParameter("@designation", designation),
            guid, new SqlParameter("@id", n));
        if (touchees != 1) throw new InvalidOperationException($"Partie {n} : {touchees} ligne(s) mise(s) a jour au lieu d'une.");
        // Instance.Sauve (Instance.cs:3903-3906) ; une date vide est loin dans le passe (DBDateTime nul = DateTime.MinValue).
        await geste.EcrireAsync("""
            UPDATE v2.INSTANCE SET
                   INSTN_DATE_MAJ_INFOG = CASE WHEN INSTN_TOP_EXPERIAN = 1 OR INSTN_DATE_MAJ_INFOG IS NULL
                                                 OR ABS(DATEDIFF_BIG(second, INSTN_DATE_MAJ_INFOG, @maintenant)) >= 86400
                                               THEN @maintenant ELSE INSTN_DATE_MAJ_INFOG END,
                   INSTN_NUMERO_SOURCE = ISNULL(INSTN_NUMERO_SOURCE, @guidInstance), INSTN_NOM_COURANT = @nomCourant
            WHERE INSTN_ID_INSTN = @instance
            """, ct, new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@instance", p.IdInstance),
            new SqlParameter("@guidInstance", Guid.NewGuid().ToString().ToUpperInvariant()),
            new SqlParameter("@nomCourant", (object?)nomInstance.Apres ?? DBNull.Value));

        var traces = new List<TraceGeste>
        {
            new("affaire", $"la partie {p.Nom} de l'instance {p.Role}", id, "a modifie",
                changes.ToDictionary(c => c, c => avant[c]), changes.ToDictionary(c => c, c => apres[c])),
        };
        if (nomInstance.Avant != nomInstance.Apres)
            traces.Add(new TraceGeste("affaire", $"l'instance {p.Role}", p.IdInstance.ToString(CultureInfo.InvariantCulture), "a modifie",
                new Dictionary<string, string?> { ["Nom courant"] = nomInstance.Avant }, new Dictionary<string, string?> { ["Nom courant"] = nomInstance.Apres }));
        return IssueGeste.Ecrit($"Partie {p.Nom} (instance {p.Role}) enregistrée dans la base du greffe : {string.Join(", ", changes.Select(c => $"« {c} »"))}.",
            [id], [.. traces]);
    }

    // ------------------------------------------------------------------ MAJNomInstance (Instance.cs:849-962)

    /// <summary>« &lt;Aucune partie saisie - Nom inconnu&gt; » (Instance.cs:795) et le separateur « # » (:796).</summary>
    internal const string NomCourantVide = "<Aucune partie saisie - Nom inconnu>", Separateur = " # ";

    /// <summary>La taille de INSTN_NOM_COURANT (varchar(250) dans RIG_DEV, lu le 26/11/2026) : INSTN_NOM_COURANT_LENGTH.</summary>
    private const int TailleNomCourant = 250;

    /// <summary>La taille de PRT_DESIGNATION (varchar(512) dans RIG_DEV, lu le 26/11/2026).</summary>
    private const int TailleDesignation = 512;

    /// <summary>
    /// Le nom courant de l'instance avant et apres que la partie <paramref name="p"/> prenne la designation
    /// <paramref name="designation"/> : MAJNomInstance (Instance.cs:849-860), GetNomCourantInstance (:862-876) si le nom est
    /// vide, RefreshNomInstance (:880-929) sinon. Le premier demandeur et le premier defendeur sont ceux de ListeParties
    /// (Instance.cs:1232-1269) : designation non nulle, qualite non vide, par PRT_NUM_ORDRE.
    /// </summary>
    internal static async Task<(string? Avant, string? Apres)> NomCourantAsync(GesteEnCours geste, PartieLue p, string designation,
                                                                                 CancellationToken ct)
    {
        string? nomCourant;
        await using (var r = await geste.LireAsync("SELECT INSTN_NOM_COURANT FROM v2.INSTANCE WITH (UPDLOCK, HOLDLOCK) WHERE INSTN_ID_INSTN = @i",
                         ct, new SqlParameter("@i", p.IdInstance)))
            nomCourant = await r.ReadAsync(ct) && r[0] is string s ? s : null;
        var parties = new List<(int Id, string Qualite, string Designation)>();
        await using (var r = await geste.LireAsync("""
            SELECT PRT_ID_PRT, PRT_QUALIF_PARTIE, PRT_DESIGNATION FROM v2.PARTIE
            WHERE PRT_ID_INSTN = @i AND PRT_DESIGNATION IS NOT NULL AND ISNULL(PRT_QUALIF_PARTIE, '') <> ''
            ORDER BY PRT_NUM_ORDRE, PRT_ID_PRT
            """, ct, new SqlParameter("@i", p.IdInstance)))
            while (await r.ReadAsync(ct))
                parties.Add((Convert.ToInt32(r["PRT_ID_PRT"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "PRT_QUALIF_PARTIE"),
                             Convert.ToString(r["PRT_DESIGNATION"], CultureInfo.InvariantCulture) ?? ""));
        string? Premier(char lettre, bool apres) => parties.Where(x => x.Qualite.StartsWith(lettre))
            .Select(x => apres && x.Id == p.Id ? designation : x.Designation).FirstOrDefault();
        string? dem = Premier('D', true), def = Premier('F', true);

        if (string.IsNullOrEmpty(nomCourant) || nomCourant == NomCourantVide)
            return (nomCourant, parties.Count > 0 ? NomInstance(ref dem, ref def) : NomCourantVide);
        string? ancienDem = Premier('D', false), ancienDef = Premier('F', false);
        if (dem == ancienDem && def == ancienDef) return (nomCourant, nomCourant);
        if (NomInstance(ref ancienDem, ref ancienDef) == nomCourant) return (nomCourant, NomInstance(ref dem, ref def));
        // Un nom retouche a la main : seul l'ancien nom de la partie y est remplace (Instance.cs:903-921). NomInstance vient de
        // ramener l'ancien demandeur vide a « <Inconnu> » (passage par ref, comme RIG v2).
        var morceaux = nomCourant.Split(Separateur);
        static string Remplacer(string morceau, string ancien, string? nouveau)
        {
            var motif = ancien.Length <= morceau.Length ? ancien : ancien[..morceau.Length];
            // string.Replace refuse un motif vide (ArgumentException dans RIG v2 aussi) : le morceau reste tel quel.
            return motif.Length == 0 ? morceau : morceau.Replace(motif, nouveau, StringComparison.Ordinal);
        }
        if (morceaux.Length == 1) dem = Remplacer(morceaux[0], ancienDem ?? "", dem);
        else if (morceaux.Length == 2)
        {
            if (!string.IsNullOrEmpty(ancienDem)) dem = Remplacer(morceaux[0], ancienDem, dem);
            if (!string.IsNullOrEmpty(ancienDef)) def = Remplacer(morceaux[1], ancienDef, def);
        }
        else return (nomCourant, nomCourant);
        return (nomCourant, NomInstance(ref dem, ref def));
    }

    /// <summary>_GetNomInstance (Instance.cs:931-962), parametres par ref comme RIG v2 : « demandeur # defendeur », tronque a
    /// 250 caracteres ; demandeur vide = « &lt;Inconnu&gt; ».</summary>
    internal static string NomInstance(ref string? demandeur, ref string? defendeur)
    {
        if (string.IsNullOrEmpty(demandeur) && string.IsNullOrEmpty(defendeur)) return NomCourantVide;
        demandeur = string.IsNullOrEmpty(demandeur) ? "<Inconnu>" : demandeur;
        defendeur = string.IsNullOrEmpty(defendeur) ? "" : defendeur;
        var separateur = defendeur.Length > 0 ? Separateur : "";
        var nom = demandeur + separateur + defendeur;
        if (nom.Length <= TailleNomCourant) return nom;
        var disponible = TailleNomCourant - separateur.Length;
        if (demandeur.Length > disponible / 2 && defendeur.Length > disponible / 2)
        {
            demandeur = demandeur[..(disponible / 2)];
            disponible -= demandeur.Length;
            defendeur = defendeur[..disponible];
        }
        else if (demandeur.Length > defendeur.Length) { disponible -= defendeur.Length; demandeur = demandeur[..disponible]; }
        else { disponible -= demandeur.Length; defendeur = defendeur[..disponible]; }
        return demandeur + separateur + defendeur;
    }
}
