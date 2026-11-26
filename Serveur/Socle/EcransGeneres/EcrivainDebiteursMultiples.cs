using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurDebiteursMultiples;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Débiteurs multiples » (PROC_DEBITEURS_MULTIPLES) : ENREGISTRER une extension (un debiteur additionnel d'une PC). Mode
/// d'emploi : EcrivainEcran.cs ; la liste et la fiche : LecteurDebiteursMultiples.cs. Plan « RIG v3 100 % fonctionnel », lot 4,
/// 26/11/2026.
///
/// SOURCE (ancien RIG) : OPE_DEBITEUR en mode extension (le MetierBase de l'etape est un DebiteurPCAutre,
/// OPE_DEBITEUR.cs:103-120) : la date d'extension (DBTAT_DATE_EXTENSION, obligatoire, OPE_DEBITEUR.Designer.cs:126), le lien avec
/// le debiteur principal (DBTAT_LIEN_PRINCIPALE, obligatoire, :148, valeurs de LIEN_PRINCIPALE, ULT_COMBO_LIEN_PRINCIPALE.cs:18),
/// l'inscription au registre des metiers (DBTAT_INSCRIT_REGISTRE_METIER) et, case cochee (MAJAffichage, :151-153), son numero
/// (DBTAT_NUM_RM), son departement (DBTAT_DEPARTEMENT_RM, obligatoire, :184, 3 caracteres) et le RM dependant
/// (DBTAT_RM_DEPENDANT, un code de DESTINATAIRE_INSTITUTIONNEL de type RM, OPE_DEBITEUR.cs:82-83).
///
/// CONTROLES : le debiteur est une extension (le principal est en lecture seule dans cet ecran, MAJAffichage :140-150) ; chaque
/// champ envoye est un de ceux-la ; les obligatoires de RIG v2 (date, lien ; departement si la case est cochee) ; la valeur d'une
/// liste existe dans sa table du greffe. RIG v2 ne verrouille pas la PC (aucun Lock dans ce processus ni dans le moteur).
///
/// CE QUI S'ECRIT : les colonnes changees de v2.DEBITEUR_AUTRE. La validation de RIG v2 recalcule ensuite le nom de l'affaire et
/// les dirigeants et etablissements de la PC (DoPreparationObjetsBeforeEtape, PROC_DEBITEURS_MULTIPLES.cs:65-70) : ils ne
/// changent qu'avec le nom, l'entreprise ou la suppression d'un debiteur (ProcedureCollective.cs:3602-3724), que ce geste ne
/// touche pas -- rien a recalculer. Trace : famille « affaire », « a modifie », avant et apres des champs changes.
/// NON REPRIS (nomme) : ajouter ou retirer une extension, changer son entreprise ou son nom (il faut choisir une entreprise du
/// registre et reecrire le nom de l'affaire et les dirigeants, ProcedureCollective.cs:3602-3724) ; l'affectation de patrimoine
/// d'un EIRL (VerifAffectationDePatrimoine, OPE_DEBITEUR.cs:161-197) ; la demande DEBITEURS_MULTIPLES que le moteur cree
/// (createDemande, PROC_DEBITEURS_MULTIPLES.cs:31), une trace sans facture que le journal des gestes remplace.
/// </summary>
internal sealed class EcrivainDebiteursMultiples : IEcrivainEcran
{
    public string Procedure => "PROC_DEBITEURS_MULTIPLES";
    public string Ecran => "Débiteurs multiples";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    /// <summary>Les champs ecrits, leur section et leur colonne de v2.DEBITEUR_AUTRE.</summary>
    private static readonly (string Section, string Champ, string Colonne)[] Colonnes =
    [
        (SectionIdentite, ChampDateExtension, "DBTAT_DATE_EXTENSION"), (SectionIdentite, ChampLien, "DBTAT_LIEN_PRINCIPALE"),
        (SectionRm, ChampInscrit, "DBTAT_INSCRIT_REGISTRE_METIER"), (SectionRm, ChampNumRm, "DBTAT_NUM_RM"),
        (SectionRm, ChampDepartement, "DBTAT_DEPARTEMENT_RM"), (SectionRm, ChampDependant, "DBTAT_RM_DEPENDANT"),
    ];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids is not [var id] || await LireDebiteurAsync(geste.Base, geste.Transaction, id, ct) is not { } d)
            return IssueGeste.Refuser($"Débiteur introuvable dans la base du greffe ({string.Join(", ", geste.Demande.Ids)}).");
        if (d.Principal) return IssueGeste.Refuser($"Fiche en lecture seule : {MotifPrincipal}.");

        // Les champs envoyes : ceux d'une extension que ce geste ecrit.
        var saisis = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (cle, valeur) in geste.Demande.Valeurs)
        {
            var c = Colonnes.FirstOrDefault(c => GesteEcran.Cle(c.Section, c.Champ) == cle);
            if (c.Champ is null) return IssueGeste.Refuser($"« {cle} » ne se modifie pas sur {d.EnMots} dans cet écran.");
            saisis[c.Champ] = valeur.Trim();
        }

        var avant = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ChampDateExtension] = FormeLigne.DateFiche(d.Extension), [ChampLien] = d.Lien, [ChampInscrit] = FormeLigne.Case(d.InscritRm),
            [ChampNumRm] = d.NumRm, [ChampDepartement] = d.Departement, [ChampDependant] = d.Dependant,
        };
        var apres = new Dictionary<string, string>(avant, StringComparer.Ordinal);
        foreach (var (champ, valeur) in saisis) apres[champ] = valeur;

        // Les obligatoires de RIG v2 (OPE_DEBITEUR.Designer.cs:126, :148 ; :184 quand la case est cochee, seul cas ou il s'affiche).
        foreach (var champ in new[] { ChampDateExtension, ChampLien }.Concat(apres[ChampInscrit] == "oui" ? [ChampDepartement] : []))
            if (apres[champ].Length == 0) return IssueGeste.Refuser($"« {champ} » est obligatoire.");

        var parametres = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (champ, valeur) in saisis.Where(s => s.Value != avant[s.Key]))
        {
            switch (champ)
            {
                case ChampDateExtension:
                    if (!DateTime.TryParseExact(valeur, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        return IssueGeste.Refuser($"« {champ} » : date illisible ({valeur}).");
                    parametres[champ] = date;
                    break;
                case ChampLien:
                    // La liste porte le libelle ; la colonne, la valeur de LIEN_PRINCIPALE.
                    await using (var r = await geste.LireAsync("SELECT TOP (1) R_LIENPRINCI_VALEUR FROM v2.LIEN_PRINCIPALE WHERE R_LIENPRINCI_LIBELLE_ECRAN = @l",
                                                               ct, new SqlParameter("@l", valeur)))
                    {
                        if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"« {champ} » : « {valeur} » n'est pas un lien de la table LIEN_PRINCIPALE du greffe.");
                        parametres[champ] = FormeLigne.Texte(r, "R_LIENPRINCI_VALEUR");
                    }
                    break;
                case ChampInscrit:
                    parametres[champ] = valeur == "oui";
                    break;
                case ChampNumRm:
                    if (valeur.Length > 10) return IssueGeste.Refuser($"« {champ} » : 10 caractères au plus.");
                    parametres[champ] = valeur.Length == 0 ? DBNull.Value : valeur;
                    break;
                case ChampDepartement:
                    if (valeur.Length > 3) return IssueGeste.Refuser($"« {champ} » : 3 caractères au plus.");
                    parametres[champ] = valeur.Length == 0 ? DBNull.Value : valeur;
                    break;
                case ChampDependant:
                    if (valeur.Length > 0)
                    {
                        await using var r = await geste.LireAsync("SELECT TOP (1) 1 FROM v2.DESTINATAIRE_INSTITUTIONNEL WHERE DTINS_TYPE = 'RM' AND DTINS_CODE = @c",
                                                                  ct, new SqlParameter("@c", valeur));
                        if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"« {champ} » : « {valeur} » n'est pas un registre des métiers du greffe (DESTINATAIRE_INSTITUTIONNEL).");
                    }
                    parametres[champ] = valeur.Length == 0 ? DBNull.Value : valeur;
                    break;
            }
        }
        if (parametres.Count == 0) return IssueGeste.Refuser($"Aucun champ n'est modifié : {d.EnMots} garde ses valeurs.");

        var ecrites = Colonnes.Where(c => parametres.ContainsKey(c.Champ)).ToList();
        var touchees = await geste.EcrireAsync(
            $"UPDATE v2.DEBITEUR_AUTRE SET {string.Join(", ", ecrites.Select(c => $"{c.Colonne} = @{c.Colonne}"))} WHERE DBTAT_ID_DBTAT = @id", ct,
            [new SqlParameter("@id", d.Id), .. ecrites.Select(c => new SqlParameter($"@{c.Colonne}", parametres[c.Champ]))]);
        if (touchees != 1) throw new InvalidOperationException($"Debiteur {d.Id} : {touchees} ligne(s) mise(s) a jour au lieu d'une.");

        var champs = ecrites.Select(c => c.Champ).ToList();
        return IssueGeste.Ecrit($"Débiteur {d.Nom} (PC {d.NumPc}) enregistré dans la base du greffe : {string.Join(", ", champs.Select(c => $"« {c} »"))}.", [id],
            new TraceGeste("affaire", d.EnMots, id, "a modifie",
                champs.ToDictionary(c => c, c => avant[c]), champs.ToDictionary(c => c, c => apres[c])));
    }
}
