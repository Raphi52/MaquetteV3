using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurFacturationExpresso;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Facturation Expresso » (PROC_PARAMEXPRESSO) : ENREGISTRER la valeur d'un parametre de facturation, depuis sa fiche. Mode d'emploi :
/// EcrivainEcran.cs ; les lignes et la fiche : LecteurFacturationExpresso.cs. Plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026.
///
/// SOURCE (ancien RIG) : PROC_PARAMEXPRESSO.DoValider (PROC_PARAMEXPRESSO.cs:39-54) -> MetierParametrage.SaveAll
/// (Metier\MetierParametrage.cs:39-58) : pour chaque cle PARAMETRAGE changee, Parametrage.UpdateValeurParametrage(cle, valeur) (:42-47),
/// soit la VALEUR d'un reglage existant de soc.Reglage en V3. Chaque operation pose sa valeur par son controle (SetValue, cite dans
/// <see cref="Ecrite"/>) : un montant ecrit decimal.ToString() en culture francaise (« 1,30 », la forme des valeurs de RIG_DEV), une
/// case « 1 » / « 0 », le pied de page « GREFFE_PARAMETRE » ou « CODE_GREFFE » (ses deux boutons radio, OPE_PIED_PAGE.Designer.cs:108-111),
/// un mode de paiement le code d'un mode (ComboBoxModePaiement), un texte tel quel. La V3 tient ces controles a l'enregistrement : une
/// valeur que le controle de la V2 ne laisserait pas saisir est refusee, rien n'est ecrit.
/// ECRITURE : par GesteEnCours.ModifierReglageAsync, l'ecriture partagee des reglages (EcritureReglage.cs, lot 14 ; decision du chef du
/// 27/11/2026 : un geste ne change que la valeur d'un reglage existant, par cette seule porte) -- droit de modifier le greffe exige, ni
/// creation ni suppression, auteur et date poses, ligne du journal rendue au socle.
/// NON REPRIS (nommes) : les colonnes GREFFE_PARAMETRE (GRPAR_*, :49-51, soc.ParametreGreffe en V3 : la decision n'ouvre que soc.Reglage) ;
/// la creation d'EXPRESSO_TYPE_PIED_PAGE absent a l'ouverture (OPE_PIED_PAGE.cs:22-29 : ni creation de reglage) ; les liens mode / banque
/// (MetierBanques.SaveAll), que « Modes de paiement » ecrit deja (EcrivainModesPaiement, meme table). La demande « Modification du
/// paramétrage en base » de la V2 (:52-58), creee et close d'un coup, n'est qu'une trace : le journal des gestes la tient (decision du plan).
/// </summary>
internal sealed class EcrivainFacturationExpresso : IEcrivainEcran
{
    public string Procedure => "PROC_PARAMEXPRESSO";
    public string Ecran => "Facturation Expresso";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (id.StartsWith(PrefixeMode, StringComparison.Ordinal))
            return IssueGeste.Refuser($"La banque du mode {id[PrefixeMode.Length..]} s'enregistre dans « Modes de paiement » : rien n'est enregistré ici.");
        if (!Parametres.Any(p => p.Cle == id)) return IssueGeste.Refuser($"Paramètre inconnu de Facturation Expresso : « {id} ».");
        if (!Ecrite.TryGetValue(id, out var controle))
            return IssueGeste.Refuser(id.StartsWith("GRPAR_", StringComparison.Ordinal)
                ? $"{id} est une colonne de GREFFE_PARAMETRE (paramètre du greffe) : la V3 n'écrit par un écran que la valeur d'un réglage. Rien n'est enregistré."
                : $"{id} n'est que lu par Facturation Expresso dans RIG v2 : rien n'est enregistré.");
        var cle = $"{SectionParametre} › {ChampValeur}";
        if (!geste.Demande.Valeurs.TryGetValue(cle, out var saisie)) return IssueGeste.Refuser($"Aucun champ n'est modifié : {id} garde sa valeur.");

        var valeur = await NormaliserAsync(geste, controle.Saisie, saisie.Trim(), ct);
        if (valeur.Refus is { } refus) return IssueGeste.Refuser(refus.Replace("{cle}", id, StringComparison.Ordinal));

        return await geste.ModifierReglageAsync(id, valeur.Ecrite!, ct) switch
        {
            ReglageEcrit.SansLeDroit s => IssueGeste.Refuser(s.Motif),
            ReglageEcrit.Absent => IssueGeste.Refuser($"Le réglage {id} n'existe pas dans la base du greffe : la V3 ne crée pas de réglage. Rien n'est enregistré."),
            ReglageEcrit.Modifie m when m.Avant.Valeur == m.Apres.Valeur => IssueGeste.Refuser($"Rien à enregistrer : {id} vaut déjà « {m.Apres.Valeur} »."),
            ReglageEcrit.Modifie m => IssueGeste.Ecrit($"Paramètre {id} enregistré : {EnMots(m.Apres.Valeur)} (avant : {EnMots(m.Avant.Valeur)}).", [id], m.Trace),
            _ => IssueGeste.Refuser($"Le réglage {id} n'a pas pu être enregistré."),
        };
    }

    /// <summary>Une valeur dans un message : entre guillemets, ou « vide ».</summary>
    private static string EnMots(string? valeur) => string.IsNullOrEmpty(valeur) ? "vide" : $"« {valeur} »";

    /// <summary>La valeur que le controle de la V2 aurait ecrite, ou le refus (« {cle} » : la cle du parametre).</summary>
    private static async Task<(string? Ecrite, string? Refus)> NormaliserAsync(GesteEnCours geste, Saisie saisie, string texte, CancellationToken ct)
    {
        switch (saisie)
        {
            case Saisie.Montant:
                // Un montant saisi « 1,30 », « 1.30 » ou « 1 » ; ecrit comme la V2 (decimal du controle, culture francaise, deux decimales).
                if (!decimal.TryParse(texte.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var montant))
                    return (null, $"{{cle}} est un montant (contrôle montant de RIG v2) : « {texte} » n'en est pas un. Rien n'est enregistré.");
                return (montant.ToString("0.00", Francais), null);
            case Saisie.Case:
                return texte.ToLowerInvariant() switch
                {
                    "oui" or "1" or "true" => ("1", null),
                    "" or "non" or "0" or "false" => ("0", null),
                    _ => (null, $"{{cle}} est une case (« 1 » ou « 0 » dans RIG v2) : « {texte} » n'en est pas une. Rien n'est enregistré."),
                };
            case Saisie.PiedDePage:
                return texte is "GREFFE_PARAMETRE" or "CODE_GREFFE"
                    ? (texte, null)
                    : (null, $"{{cle}} vaut « GREFFE_PARAMETRE » (adresse saisie) ou « CODE_GREFFE » (adresse du code greffe) : « {texte} » n'est pas un choix de RIG v2. Rien n'est enregistré.");
            case Saisie.Mode:
                if (texte.Length == 0) return ("", null);
                await using (var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.MODE_PAIEMENT WHERE R_MODEPAIEME_VALEUR = @m", ct, new SqlParameter("@m", texte)))
                {
                    await r.ReadAsync(ct);
                    if (r.GetInt32(0) == 0) return (null, $"{{cle}} est un mode de paiement du greffe : « {texte} » n'en est pas un. Rien n'est enregistré.");
                }
                return (texte, null);
            default:
                return (texte, null);
        }
    }
}
