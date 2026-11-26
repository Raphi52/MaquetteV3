using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurMotsDirecteurs;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Mots directeurs » (PROC_MDJUD) : ENREGISTRER la chaine de mots directeurs d'une affaire dans sa fiche. Mode d'emploi :
/// EcrivainEcran.cs ; la fiche : LecteurMotsDirecteurs.cs. Plan « RIG v3 100 % fonctionnel », lot 3, 26/11/2026.
///
/// SOURCE (ancien RIG) : OPE_MD (D:\RigApplication\Source\RIG\DLL\Processus\PROC_MDJUD\OPE_MD.cs), DoVerifier (:89-120) puis
/// DoSauve (:77-87), sur l'enregistrement RIG_MOTS_DIRECTEUR_PCINST de l'affaire (RigMotsDirecteurPCInst.cs:46-62).
///
/// CONTROLES DE RIG v2, dans l'ordre, avant toute ecriture (un refus n'ecrit rien) :
///   1. l'affaire existe, est de MDJUD et a son enregistrement de mots directeurs (sinon « Affaire introuvable ») : la V3 ne
///      fait que MODIFIER cet enregistrement (la V2 chargerait un objet vide, « new RigMotsDirecteurPCInst(sql) », dont aucun lien
///      vers l'affaire n'est pose dans OPE_MD) ; seul le champ « Mots directeurs › Mots directeurs » s'enregistre (le numero et
///      le nom sont en lecture, OPE_MD.cs:38-40) ;
///   2. DoVerifier, sur la chaine SAISIE (avant normalisation, comme la V2), mots separes par un espace (Split(' '), :90),
///      les vides ignores (:106-108) : « Le mot '…' doit faire au moins 3 caractères » (:110-111) ; un prefixe (2 premiers
///      caracteres en majuscules, :113) hors des codes de la famille (CodeMotsDirecteurs.cs) : « Le mot '…' doit commencer
///      par un code mot directeur existant » (:116) -- une ERREUR pour un mot nouveau, un AVERTISSEMENT seulement pour un mot
///      deja present dans la valeur initiale (:114, comparaison sensible a la casse comme la V2). Les avertissements
///      n'empechent pas l'enregistrement : le message de l'ecran les rappelle.
///
/// CE QUI S'ECRIT (v2.RIG_MOTS_DIRECTEUR_PCINST.MDPCI_MOTS) : la chaine normalisee par DoSauve (Normaliser). Trace : famille
/// « affaire », « a modifie », avant et apres.
///
/// NON REPRIS (nommes) : la demande MDJUD / MDJUDPR que RIG v2 enregistre a l'ouverture (OPE_MD.cs:64-69) et valide sans
/// facture (MetierFacture = false, PROC_MDJUD.Designer.cs:57) : ce n'est qu'une trace, la V3 trace au journal des gestes ;
/// la creation d'un enregistrement de mots directeurs absent (point 1). fix-ok: mesure le 26/11/2026 (grep des jeux du banc) --
/// la liste ne porte plus que des affaires munies de leur enregistrement (CROSS APPLY, LecteurMotsDirecteurs) : le refus
/// « aucun mot directeur enregistré » etait devenu inatteignable, il rejoint « Affaire introuvable ».
/// </summary>
internal sealed partial class EcrivainMotsDirecteurs : IEcrivainEcran
{
    /// <summary>Le nom de l'ecran au journal des gestes (le titre du descripteur).</summary>
    internal const string NomEcran = "Mots directeurs";

    public string Procedure => "PROC_MDJUD";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    /// <summary>
    /// La chaine que DoSauve enregistre (OPE_MD.cs:78-85) : Trim, toute suite de blancs reduite a un espace, puis chaque mot
    /// ENTIEREMENT alphabetique (« ^[a-zA-Z]*$ ») de plus de deux lettres : deux premieres lettres en majuscules, le reste en
    /// minuscules. Un mot qui porte un chiffre ou un accent reste tel quel (comme la V2).
    /// </summary>
    internal static string Normaliser(string mots)
    {
        var chaine = Blancs().Replace(mots.Trim(), " ");
        return string.Join(" ", chaine.Split(' ').Select(mot => Alphabetique().Replace(mot,
            m => mot.Length > 2 ? m.Value[..2].ToUpperInvariant() + m.Value[2..].ToLowerInvariant() : mot)));
    }

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // 1. L'affaire et son enregistrement, verrouille en ecriture jusqu'a la fin de la transaction.
        if (geste.Demande.Ids is not [var id] || await LireAffaireAsync(geste.Base, geste.Transaction, id, ct) is not { } a)
            return IssueGeste.Refuser($"Affaire introuvable dans la base du greffe ({string.Join(", ", geste.Demande.Ids)}).");
        var cleMots = GesteEcran.Cle(SectionMots, ChampMots);
        if (geste.Demande.Valeurs.Keys.FirstOrDefault(c => c != cleMots) is { } autre)
            return IssueGeste.Refuser($"« {autre} » ne se modifie pas dans les mots directeurs de RIG v2.");
        if (geste.Valeur(SectionMots, ChampMots) is not { } saisie)
            return IssueGeste.Refuser($"Aucun champ n'est modifié : {a.Famille.Article} {a.Numero} garde ses mots directeurs.");

        // 2. DoVerifier (OPE_MD.cs:89-120), sur la valeur saisie.
        var initiaux = a.Mots.Split(' ');
        var erreurs = new List<string>();
        var avertissements = new List<string>();
        foreach (var mot in saisie.Split(' ').Where(m => m.Length > 0))
        {
            if (mot.Length < 3) { erreurs.Add($"Le mot '{mot}' doit faire au moins 3 caractères"); continue; }
            var prefixe = mot[..2].ToUpperInvariant();
            if (a.Famille.Prefixes.Any(p => p.Code == prefixe)) continue;
            (initiaux.Contains(mot) ? avertissements : erreurs).Add($"Le mot '{mot}' doit commencer par un code mot directeur existant");
        }
        if (erreurs.Count > 0) return IssueGeste.Refuser(string.Join(". ", erreurs) + ".");

        // DoSauve (OPE_MD.cs:77-87) : la chaine normalisee. Une chaine egale a celle de la base ne s'ecrit pas.
        var normalisee = Normaliser(saisie);
        if (normalisee == a.Mots)
            return IssueGeste.Refuser($"Aucun champ n'est modifié : {a.Famille.Article} {a.Numero} garde ses mots directeurs.");
        var touchees = await geste.EcrireAsync("UPDATE v2.RIG_MOTS_DIRECTEUR_PCINST SET MDPCI_MOTS = @mots WHERE MDPCI_ID_MDPCI = @id", ct,
                                               new SqlParameter("@mots", normalisee), new SqlParameter("@id", a.IdMots));
        if (touchees != 1) throw new InvalidOperationException($"Mots directeurs {a.IdMots} : {touchees} ligne(s) mise(s) a jour au lieu d'une.");

        var message = $"Mots directeurs de {a.Famille.Article} {a.Numero} enregistrés dans la base du greffe.";
        if (avertissements.Count > 0) message += " Avertissement : " + string.Join(". ", avertissements) + ".";
        return IssueGeste.Ecrit(message, [id],
            new TraceGeste("affaire", $"les mots directeurs de {a.Famille.Article} {a.Numero}", id, "a modifie",
                new Dictionary<string, string> { [ChampMots] = a.Mots }, new Dictionary<string, string> { [ChampMots] = normalisee }));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Blancs();

    [GeneratedRegex("^[a-zA-Z]*$")]
    private static partial Regex Alphabetique();
}
