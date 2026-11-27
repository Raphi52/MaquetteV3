using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurDestinatairesRedaction;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Destinataires » (PROC_DEST_REDA) : « Enregistrer » ecrit l'edition d'un destinataire, et sur le document REDA de sa decision la
/// date de delivrance des grosses et les supplements, dans la base du greffe. Mode d'emploi : EcrivainEcran.cs ; la fiche :
/// LecteurDestinatairesRedaction.cs. Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_DEST_REDA\ (proc_dest_reda.md, Detail OPE) -- le bouton VALIDER de l'operation
/// (OPE_DEST_REDA.Toolbar_ButtonClick, :217-280) :
///   - l'edition : les quatre cases recomposent le code dans l'ordre G, C, E, L, X sans aucune (MetierDataGridView_CellContentClick,
///     :282-308) ; le code se normalise « T » -> « GC », vide -> « X » (DestinataireREDA.EditionJugement, :193-197) ; il s'ecrit sur la
///     colonne du destinataire (_EnregistrerDestinataires, :415-466 ; _EnregistrerMandataire, :468-487) ;
///   - le document REDA (DOCUMENT_DOSSIER) : les supplements DCMDS_SUPPLEMENT_GROSSE / _COPIE / _EXPEDI et la date de delivrance des
///     grosses DCMDS_DATE_IMPRESSION_MINUTE (:223-243) ; une date vide s'enregistre vide, avec l'avertissement « La date de délivrance
///     des grosses est obligatoire pour continuer. » (:244-251) -- un avertissement, pas un refus.
/// CONTROLES, avant toute ecriture :
///   1. le destinataire existe (sa decision a un document REDA) ;
///   2. une edition faite des seules lettres G, C, E, L (une fois chacune), ou X, ou D (par defaut) -- les valeurs de la liste
///      ULT_COMBO_EDITION_JUGEMENT que l'ecran recompose ;
///   3. un supplement est un nombre (ult_int_*_supp : des champs entiers) ;
///   4. quelque chose change, sinon « Veuillez modifier des données. » (:264).
/// Traces : famille « affaire », « a modifie », l'edition, la date et les supplements avant et apres.
///
/// NON REPRIS (nomme) : document.ReinitialiserGUIDs et le rechargement du document (la V3 relit la base) ; l'ajout et la suppression
/// d'un destinataire divers (la forme Fiche n'a ni Ajouter ni Supprimer).
/// </summary>
internal sealed class EcrivainDestinatairesRedaction : IEcrivainEcran
{
    public string Procedure => "PROC_DEST_REDA";
    public string Ecran => "Destinataires";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    /// <summary>L'avertissement de RIG v2 sans date de delivrance (OPE_DEST_REDA.cs:246).</summary>
    internal const string DateObligatoire = "La date de délivrance des grosses est obligatoire pour continuer.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (await DestinataireAsync((sql, p) => geste.LireAsync(sql, ct, p), id, ct) is not { } d)
            return IssueGeste.Refuser($"Destinataire introuvable parmi ceux des décisions rédigées ({id}).");

        // 2. L'edition : les cases recomposent le code (G, C, E, L) ; sinon le code saisi.
        var edition = d.Edition;
        string? Case(string libelle) => geste.Valeur(SectionEdition, libelle);
        if (new[] { ChampGrosse, ChampCopie, ChampExpedition, ChampLettre }.Any(c => Case(c) is not null))
        {
            bool Coche(string libelle, char lettre) => Case(libelle) is { } v ? v.Trim() == "oui" : d.Edition.Contains(lettre);
            var code = string.Concat(Coche(ChampGrosse, 'G') ? "G" : "", Coche(ChampCopie, 'C') ? "C" : "", Coche(ChampExpedition, 'E') ? "E" : "",
                                     Coche(ChampLettre, 'L') ? "L" : "");
            edition = code.Length == 0 ? "X" : code;
        }
        else if (geste.Valeur(SectionEdition, ChampEdition) is { } saisi)
        {
            var s = saisi.Trim().ToUpperInvariant();
            edition = s switch { "" => "X", "T" => "GC", _ => s };
        }
        if (!(edition is "X" or "D" || (edition.All(c => c is 'G' or 'C' or 'E' or 'L') && edition.Distinct().Count() == edition.Length)))
            return IssueGeste.Refuser($"Édition « {edition} » inconnue : G (grosse), C (copie), E (expédition), L (lettre de notification), X (aucune) ou D (par défaut).");

        // 3. Le document REDA : la date et les supplements.
        var date = d.Delivrance;
        if (geste.Valeur(SectionDate, ChampDate) is { } dateSaisie)
        {
            if (dateSaisie.Trim().Length == 0) date = null;
            else if (CreationV2.Date(dateSaisie.Trim()) is { } lue) date = lue;
            else return IssueGeste.Refuser($"« {ChampDate} » : date illisible ({dateSaisie}).");
        }
        string? Supplement(string libelle, string avant, out string? refus)
        {
            refus = null;
            if (geste.Valeur(SectionSupplements, libelle) is not { } v) return avant;
            var t = v.Trim();
            if (t.Length > 0 && !int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out _)) refus = $"« {libelle} » : un nombre est attendu ({v}).";
            return t;
        }
        var grosses = Supplement(ChampGrosses, d.SupplementGrosse, out var r1);
        var copies = Supplement(ChampCopies, d.SupplementCopie, out var r2);
        var expeditions = Supplement(ChampExpeditions, d.SupplementExpedition, out var r3);
        if ((r1 ?? r2 ?? r3) is { } refusSupplement) return IssueGeste.Refuser(refusSupplement);

        // 4. Quelque chose change.
        var editionChange = edition != d.Edition;
        var documentChange = date != d.Delivrance || grosses != d.SupplementGrosse || copies != d.SupplementCopie || expeditions != d.SupplementExpedition;
        if (!editionChange && !documentChange) return IssueGeste.Refuser("Veuillez modifier des données.");

        if (editionChange)
        {
            var (table, colonne, cle, valeurCle) = d.Sorte switch
            {
                'D' => ("v2.DESTINATAIRE_DIVERS", "DTDIV_EDITION_JUGEMENT", "DTDIV_ID_DTDIV", d.IdDestinataire),
                'M' => ("v2.PARTIE", "PRT_MANDATAIRE_EDITION_JUGEMENT", "PRT_ID_PRT", d.IdDestinataire),
                'L' => ("v2.PARTIE", "PRT_MANDATAIRE_PLAI_EDITION_JUGEMENT", "PRT_ID_PRT", d.IdDestinataire),
                _ => ("v2.PARTIE", "PRT_EDITION_JUGEMENT", "PRT_ID_PRT", d.IdDestinataire),
            };
            await geste.EcrireAsync($"UPDATE {table} SET {colonne} = @e WHERE {cle} = @k", ct, new SqlParameter("@e", edition), new SqlParameter("@k", valeurCle));
        }
        if (documentChange)
            await geste.EcrireAsync("""
                UPDATE v2.DOCUMENT_DOSSIER SET DCMDS_DATE_IMPRESSION_MINUTE = @date, DCMDS_SUPPLEMENT_GROSSE = @g, DCMDS_SUPPLEMENT_COPIE = @c,
                    DCMDS_SUPPLEMENT_EXPEDI = @x WHERE DCMDS_ID_DCMDS = @doc
                """, ct, new SqlParameter("@date", (object?)date ?? DBNull.Value), new SqlParameter("@g", grosses), new SqlParameter("@c", copies),
                new SqlParameter("@x", expeditions), new SqlParameter("@doc", d.IdDocument));

        var trace = new TraceGeste("affaire", $"le destinataire {d.Nom} de la décision du rôle {d.Role}", d.Id, "a modifie",
            new { Edition = d.Edition, Delivrance = FormeLigne.Date(d.Delivrance), Grosses = d.SupplementGrosse, Copies = d.SupplementCopie, Expeditions = d.SupplementExpedition },
            new { Edition = edition, Delivrance = FormeLigne.Date(date), Grosses = grosses, Copies = copies, Expeditions = expeditions });
        var message = $"Destinataire {d.Nom} enregistré : {LibelleEdition(edition)}." + (date is null ? " " + DateObligatoire : "");
        return IssueGeste.Ecrit(message, [d.Id], trace);
    }
}
