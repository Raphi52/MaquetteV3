using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurDestinatairesRedaction;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Destinataires » (PROC_DEST_REDA) : les trois gestes de la grille des destinataires d'une decision redigee, dans la base du greffe --
/// « Modifier » l'edition d'un destinataire et, sur le document REDA de sa decision, la date de delivrance des grosses et les
/// supplements ; « Ajouter » un destinataire divers ; « Supprimer » un destinataire divers. Mode d'emploi : EcrivainEcran.cs ; la grille :
/// LecteurDestinatairesRedaction.cs. Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_DEST_REDA\OPE_DEST_REDA.cs (proc_dest_reda.md, Detail OPE) et la fenetre
/// d'ajout Outils\DLL\RigReda\Internal\FormAjouterDestinataire.cs.
///   MODIFIER -- le bouton VALIDER de l'operation (Toolbar_ButtonClick, :217-280) :
///   - l'edition : le code s'ecrit dans l'ordre G, C, E, L, celui que les cases de la grille recomposent (MetierDataGridView_
///     CellContentClick, :282-308) ; « T » vaut « GC », vide vaut « X » (DestinataireREDA.EditionJugement, :193-197) ; il s'ecrit sur la
///     colonne du destinataire (_EnregistrerDestinataires, :415-466 ; _EnregistrerMandataire, :468-487) ;
///   - le document REDA (DOCUMENT_DOSSIER) : les supplements DCMDS_SUPPLEMENT_GROSSE / _COPIE / _EXPEDI et la date de delivrance des
///     grosses DCMDS_DATE_IMPRESSION_MINUTE (:223-243) ; une date vide s'enregistre vide, avec l'avertissement « La date de délivrance
///     des grosses est obligatoire pour continuer. » (:244-251) -- un avertissement, pas un refus.
///   AJOUTER -- le menu « Ajouter » (_menuAjouter, :146-148 ; _AjouterDestinataire, :405-413) ouvre FormAjouterDestinataire, dont le
///   bouton « Ajouter » saisit un destinataire libre, libelle ET adresse remplis, refuse un doublon (« Ce destinataire est déjà dans la
///   liste ! », rigBtnAjouter_Click, :117-131) ; au retour (form_ShowModalClosed, :150-168), chaque destinataire devient un
///   DESTINATAIRE_DIVERS de la decision (DestinataireREDA(DestinatairePlus, decision), DestinataireREDA.cs:390-399 : DTDIV_ID_DCJDC,
///   DTDIV_TYPE = le code -- vide pour une saisie libre --, DTDIV_DESIGNATION, DTDIV_BLOC_ADRESSE aux « | » changes en retours a la
///   ligne), d'edition « X » s'il n'en a pas (:160), enregistre aussitot (:161).
///   SUPPRIMER -- le menu « Supprimer » (_menuSupprimer, :129-144) : un destinataire divers seulement (« Impossible de supprimer les
///   destinataires de l'instance », :134-135), supprime de DESTINATAIRE_DIVERS apres confirmation (:137-138).
/// CONTROLES, avant toute ecriture :
///   1. le destinataire existe (sa decision a un document REDA) ; pour un ajout, le role a UNE decision redigee (RIG v2 ajoute depuis la
///      decision ouverte, :409) ;
///   2. une edition faite des seules lettres G, C, E, L (une fois chacune), ou X, ou D (par defaut) -- les valeurs de la liste
///      ULT_COMBO_EDITION_JUGEMENT que la grille recompose ;
///   3. un supplement est un nombre (ult_int_*_supp : des champs entiers) ;
///   4. Modifier : quelque chose change, sinon « Veuillez modifier des données. » (:264).
/// Traces : famille « affaire » ; « a modifie » (l'edition, la date et les supplements avant et apres), « a cree » (le destinataire
/// ajoute), « a supprime » (le destinataire supprime).
///
/// NON REPRIS (nomme) : document.ReinitialiserGUIDs et le rechargement du document (la V3 relit la base) ; dans la fenetre d'ajout, le
/// choix parmi les destinataires de l'instance (DestinatairesEditionsLot2.listeEntites hors parties, :61-90) : seule la saisie libre est
/// reprise ; la confirmation de la suppression (une boite de dialogue de RIG v2).
/// </summary>
internal sealed class EcrivainDestinatairesRedaction : IEcrivainEcran
{
    public string Procedure => "PROC_DEST_REDA";
    public string Ecran => "Destinataires";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Modifier, TypeGeste.Supprimer];

    /// <summary>L'avertissement de RIG v2 sans date de delivrance (OPE_DEST_REDA.cs:246).</summary>
    internal const string DateObligatoire = "La date de délivrance des grosses est obligatoire pour continuer.";

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct) => geste.Demande.Type switch
    {
        TypeGeste.Ajouter => AjouterAsync(geste, ct),
        TypeGeste.Supprimer => SupprimerAsync(geste, ct),
        _ => ModifierAsync(geste, ct),
    };

    /// <summary>La cellule saisie de la colonne <paramref name="colonne"/> ; null si la grille ne l'envoie pas (inchangee).</summary>
    private static string? Cellule(GesteEnCours geste, string colonne) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v : null;

    /// <summary>L'edition saisie, normalisee comme RIG v2 (« T » -> « GC », vide -> « X », lettres dans l'ordre G, C, E, L) ; le refus si
    /// elle sort de la grammaire de la grille.</summary>
    private static (string? Code, string? Refus) Edition(string saisie)
    {
        var s = saisie.Trim().ToUpperInvariant();
        s = s switch { "" => "X", "T" => "GC", _ => s };
        if (s is "X" or "D") return (s, null);
        if (s.All(c => c is 'G' or 'C' or 'E' or 'L') && s.Distinct().Count() == s.Length)
            return (string.Concat("GCEL".Where(s.Contains)), null);
        return (null, $"Édition « {s} » inconnue : G (grosse), C (copie), E (expédition), L (lettre de notification), X (aucune) ou D (par défaut).");
    }

    /// <summary>Le document REDA tel que la saisie le laisse : la date et les trois supplements (une colonne absente garde sa valeur).</summary>
    private sealed record Document(DateTime? Date, string Grosses, string Copies, string Expeditions);

    private static (Document? Apres, string? Refus) LireDocument(GesteEnCours geste, Document avant)
    {
        var date = avant.Date;
        if (Cellule(geste, ColDate) is { } dateSaisie)
        {
            if (dateSaisie.Trim().Length == 0) date = null;
            else if (CreationV2.Date(dateSaisie.Trim()) is { } lue) date = lue;
            else return (null, $"« {ColDate} » : date illisible ({dateSaisie}).");
        }
        string Supplement(string colonne, string ancien, ref string? refus)
        {
            if (Cellule(geste, colonne) is not { } v) return ancien;
            var t = v.Trim();
            if (t.Length > 0 && !int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out _)) refus ??= $"« {colonne} » : un nombre est attendu ({v}).";
            return t;
        }
        string? r = null;
        var apres = new Document(date, Supplement(ColGrosses, avant.Grosses, ref r), Supplement(ColCopies, avant.Copies, ref r),
                                 Supplement(ColExpeditions, avant.Expeditions, ref r));
        return r is null ? (apres, null) : (null, r);
    }

    private static Task EcrireDocumentAsync(GesteEnCours geste, int idDocument, Document d, CancellationToken ct)
        => geste.EcrireAsync("""
            UPDATE v2.DOCUMENT_DOSSIER SET DCMDS_DATE_IMPRESSION_MINUTE = @date, DCMDS_SUPPLEMENT_GROSSE = @g, DCMDS_SUPPLEMENT_COPIE = @c,
                DCMDS_SUPPLEMENT_EXPEDI = @x WHERE DCMDS_ID_DCMDS = @doc
            """, ct, new SqlParameter("@date", (object?)d.Date ?? DBNull.Value), new SqlParameter("@g", d.Grosses), new SqlParameter("@c", d.Copies),
            new SqlParameter("@x", d.Expeditions), new SqlParameter("@doc", idDocument));

    private static object Etat(string edition, Document d)
        => new { Edition = edition, Delivrance = FormeLigne.Date(d.Date), Grosses = d.Grosses, Copies = d.Copies, Expeditions = d.Expeditions };

    private static async Task<IssueGeste> ModifierAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (await DestinataireAsync((sql, p) => geste.LireAsync(sql, ct, p), id, ct) is not { } d)
            return IssueGeste.Refuser($"Destinataire introuvable parmi ceux des décisions rédigées ({id}).");

        var edition = d.Edition;
        if (Cellule(geste, ColEdition) is { } saisie)
        {
            var (code, refus) = Edition(saisie);
            if (refus is not null) return IssueGeste.Refuser(refus);
            edition = code!;
        }
        var avant = new Document(d.Delivrance, d.SupplementGrosse, d.SupplementCopie, d.SupplementExpedition);
        var (lu, refusDocument) = LireDocument(geste, avant);
        if (refusDocument is not null || lu is not { } apres) return IssueGeste.Refuser(refusDocument!);

        var editionChange = edition != d.Edition;
        var documentChange = apres != avant;
        if (!editionChange && !documentChange) return IssueGeste.Refuser("Veuillez modifier des données.");

        if (editionChange)
        {
            var (table, colonne, cle) = d.Sorte switch
            {
                'D' => ("v2.DESTINATAIRE_DIVERS", "DTDIV_EDITION_JUGEMENT", "DTDIV_ID_DTDIV"),
                'M' => ("v2.PARTIE", "PRT_MANDATAIRE_EDITION_JUGEMENT", "PRT_ID_PRT"),
                'L' => ("v2.PARTIE", "PRT_MANDATAIRE_PLAI_EDITION_JUGEMENT", "PRT_ID_PRT"),
                _ => ("v2.PARTIE", "PRT_EDITION_JUGEMENT", "PRT_ID_PRT"),
            };
            await geste.EcrireAsync($"UPDATE {table} SET {colonne} = @e WHERE {cle} = @k", ct, new SqlParameter("@e", edition), new SqlParameter("@k", d.IdDestinataire));
        }
        if (documentChange) await EcrireDocumentAsync(geste, d.IdDocument, apres, ct);

        var trace = new TraceGeste("affaire", $"le destinataire {d.Nom} de la décision du rôle {d.Role}", d.Id, "a modifie", Etat(d.Edition, avant), Etat(edition, apres));
        var message = $"Destinataire {d.Nom} enregistré : {LibelleEdition(edition)}." + (apres.Date is null ? " " + DateObligatoire : "");
        return IssueGeste.Ecrit(message, [d.Id], trace);
    }

    private static async Task<IssueGeste> AjouterAsync(GesteEnCours geste, CancellationToken ct)
    {
        // FormAjouterDestinataire.rigBtnAjouter_Click (:119) : le libelle et l'adresse, tous deux remplis.
        var libelle = Cellule(geste, ColDestinataire)?.Trim() ?? "";
        var adresse = (Cellule(geste, ColAdresse) ?? "").Replace("|", Environment.NewLine, StringComparison.Ordinal).Trim();
        if (libelle.Length == 0 || adresse.Length == 0)
            return IssueGeste.Refuser("Le libellé et l'adresse du destinataire sont obligatoires : RIG v2 n'ajoute pas un destinataire sans eux.");
        var role = (Cellule(geste, ColRole) ?? "").Replace(" ", "", StringComparison.Ordinal).Trim();
        if (role.Length == 0) return IssueGeste.Refuser($"« {ColRole} » : le rôle de la décision rédigée qui reçoit le destinataire est obligatoire.");
        var decisions = await DecisionsDuRoleAsync((sql, p) => geste.LireAsync(sql, ct, p), role, ct);
        if (decisions.Count == 0) return IssueGeste.Refuser($"Aucune décision rédigée (document REDA) pour le rôle {role} : RIG v2 n'ouvre les destinataires que d'une décision rédigée.");
        if (decisions.Count > 1)
            return IssueGeste.Refuser($"Le rôle {role} a {decisions.Count} décisions rédigées : RIG v2 ajoute le destinataire depuis la décision ouverte, ajoutez-le depuis RIG v2.");
        var (decision, idDocument) = decisions[0];

        var edition = "X";
        if (Cellule(geste, ColEdition) is { } saisie && saisie.Trim().Length > 0)
        {
            var (code, refus) = Edition(saisie);
            if (refus is not null) return IssueGeste.Refuser(refus);
            edition = code!;
        }

        // Le doublon (FormAjouterDestinataire._Existe, :136-143 : meme code, libelle et adresse) parmi les destinataires divers de la decision.
        await using (var r = await geste.LireAsync("""
            SELECT COUNT(*) FROM v2.DESTINATAIRE_DIVERS WHERE DTDIV_ID_DCJDC = @dj AND ISNULL(DTDIV_TYPE, '') = '' AND DTDIV_DESIGNATION = @l
                AND REPLACE(ISNULL(DTDIV_BLOC_ADRESSE, ''), CHAR(13), '') = REPLACE(@a, CHAR(13), '')
            """, ct, new SqlParameter("@dj", decision), new SqlParameter("@l", libelle), new SqlParameter("@a", adresse)))
        {
            await r.ReadAsync(ct);
            if (r.GetInt32(0) > 0) return IssueGeste.Refuser("Ce destinataire est déjà dans la liste !");
        }

        // La date et les supplements de la decision, si la ligne ajoutee les porte (les memes colonnes que Modifier).
        Document? avant = null, apres = null;
        if (new[] { ColDate, ColGrosses, ColCopies, ColExpeditions }.Any(c => !string.IsNullOrWhiteSpace(Cellule(geste, c))))
        {
            await using (var r = await geste.LireAsync("""
                SELECT DCMDS_DATE_IMPRESSION_MINUTE, DCMDS_SUPPLEMENT_GROSSE, DCMDS_SUPPLEMENT_COPIE, DCMDS_SUPPLEMENT_EXPEDI FROM v2.DOCUMENT_DOSSIER WHERE DCMDS_ID_DCMDS = @doc
                """, ct, new SqlParameter("@doc", idDocument)))
            {
                await r.ReadAsync(ct);
                avant = new Document(FormeLigne.DateDe(r, "DCMDS_DATE_IMPRESSION_MINUTE"), FormeLigne.Texte(r, "DCMDS_SUPPLEMENT_GROSSE"),
                                     FormeLigne.Texte(r, "DCMDS_SUPPLEMENT_COPIE"), FormeLigne.Texte(r, "DCMDS_SUPPLEMENT_EXPEDI"));
            }
            // Une cellule vide de la ligne ajoutee ne vide pas le document : seules les cellules remplies s'appliquent.
            var (lu, refus) = LireDocument(geste, avant);
            if (refus is not null) return IssueGeste.Refuser(refus);
            apres = new Document(string.IsNullOrWhiteSpace(Cellule(geste, ColDate)) ? avant.Date : lu!.Date,
                                 string.IsNullOrWhiteSpace(Cellule(geste, ColGrosses)) ? avant.Grosses : lu!.Grosses,
                                 string.IsNullOrWhiteSpace(Cellule(geste, ColCopies)) ? avant.Copies : lu!.Copies,
                                 string.IsNullOrWhiteSpace(Cellule(geste, ColExpeditions)) ? avant.Expeditions : lu!.Expeditions);
        }

        var id = await geste.InsererAsync("v2.DESTINATAIRE_DIVERS", "DTDIV_ID_DTDIV", new Dictionary<string, object?>
        {
            ["DTDIV_ID_DCJDC"] = decision, ["DTDIV_TYPE"] = null, ["DTDIV_DESIGNATION"] = libelle, ["DTDIV_BLOC_ADRESSE"] = adresse,
            ["DTDIV_EDITION_JUGEMENT"] = edition,
        }, ct);
        if (apres is not null && apres != avant) await EcrireDocumentAsync(geste, idDocument, apres, ct);

        var idLigne = "D-" + id.ToString(CultureInfo.InvariantCulture);
        var trace = new TraceGeste("affaire", $"le destinataire {libelle} de la décision du rôle {role}", idLigne, "a cree", null,
            new { Destinataire = libelle, Adresse = adresse, Edition = edition });
        return IssueGeste.Ecrit($"Destinataire {libelle} ajouté à la décision du rôle {role} : {LibelleEdition(edition)}.", [idLigne], trace);
    }

    private static async Task<IssueGeste> SupprimerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (await DestinataireAsync((sql, p) => geste.LireAsync(sql, ct, p), id, ct) is not { } d)
            return IssueGeste.Refuser($"Destinataire introuvable parmi ceux des décisions rédigées ({id}).");
        if (d.Sorte != 'D') return IssueGeste.Refuser("Impossible de supprimer les destinataires de l'instance");
        await geste.EcrireAsync("DELETE FROM v2.DESTINATAIRE_DIVERS WHERE DTDIV_ID_DTDIV = @id", ct, new SqlParameter("@id", d.IdDestinataire));
        var trace = new TraceGeste("affaire", $"le destinataire {d.Nom} de la décision du rôle {d.Role}", d.Id, "a supprime",
            new { Destinataire = d.Nom, Adresse = d.Adresse, Edition = d.Edition }, null);
        return IssueGeste.Ecrit($"Destinataire {d.Nom} supprimé de la décision du rôle {d.Role}.", [d.Id], trace);
    }
}
