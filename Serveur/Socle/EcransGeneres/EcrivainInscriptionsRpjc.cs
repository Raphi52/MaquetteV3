using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « EDI RPJC inscriptions » (PROC_EDI_RPJC_INS) : « Traiter la demande » d'inscription cochee (rattacher l'avocat a ses mandataires)
/// ou « Rejeter la demande ». Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurInscriptionsRpjc.cs (Id = la demande,
/// RPJCI_ID_RPJCI). Plan « RIG v3 100 % fonctionnel », lot 16, 27/11/2026. Meme table que « EDI RPVA » (EcrivainInscriptionsRpva.cs,
/// lot 15), mais un autre programme de la V2, aux regles a lui : rien n'est partage.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\Judiciaire\Exe\PROC_EDI_RPJC_INS.
///   L'OUVERTURE (frmChoixTrt.cs, Traiter, l. 248-305), UNE demande, dans cet ordre : un agent la tient (RPJCI_USER_EN_COURS, l. 255-257) ;
///     deja acceptee (A) : une MODIFICATION ne se retraite pas (l. 266-269), une inscription s'ouvre « deja traitee » (l. 270-272) ;
///     refusee (R) : refus avec son motif (l. 281-283) ; une modification en attente : « pas encore gérée » (l. 298-302). Le refus
///     « traitee par un autre utilisateur » (l. 287-289) compare la liste chargee a la demande relue : la V3 relit seulement, une
///     decision prise entre-temps suit donc les branches A et R ci-dessus. La V2 y pose puis efface son nom (l. 259-260, 312-313) : la V3 n'ouvre
///     rien. Les messages sont ceux de la V2 au mot pres, un saut de ligne (« \r\n ») ecrit en espace, comme EcrivainInscriptionsRpva.
///   « Traiter la demande » (frmTrt.cs, cmdTraiter, l. 470-640 ; le bouton VALIDER, l. 893-894) : la V2 montre la grille des mandataires
///     trouves par la recherche de l'agent (cases « Traiter », « Principal ») et celle des structures (« TraiterStruct »). Ici, les
///     saisies « Mandataires traités », « Mandataire principal » et « Structures traitées » les nomment par leur abrege (ou leur
///     numero, MNDTR_ID_MNDTR), separes par des virgules.
///   - controles (l. 482-530), au mot pres : le principal coche a traiter (l. 497-499), actif et avec abrege (l. 491-494) ; un
///     principal, un seul (l. 506-527) -- une demande deja traitee s'enregistre sans principal si rien n'est a traiter (l. 513-518) ;
///   - ecriture, mandataires traites (l. 540-566) : MNDTR_NUMERO_CNBF = le CNBF de la demande ; le CNBF de structure, le SIREN, les
///     courriels de la demande (RPJCI_MAIL, puis « / » MAIL2 et MAIL3 non vides, l. 145) s'ils sont renseignes ; un traite actif qui
///     n'est pas le principal est DESACTIVE ; structures (l. 573-586) : CNBF a NULL, CNBF de structure de la demande, SIREN s'il est
///     renseigne ; les instances ou un traite plaide ou postule (PARTIE) : INSTN_DATE_MAJ_INFOG = maintenant (l. 559-565, 596-600) --
///     a recentraliser sur Infogreffe, par un traitement a part de la V2, jamais appele d'ici (partie externe simulee) ; la demande,
///     si elle n'etait pas deja traitee (l. 602-610) : RPJCI_ETAT = '2', date, 'A', l'agent (son login) ; enfin, tout AUTRE mandataire au
///     CNBF de la demande, avec abrege, est desactive (l. 612-623). Une chaine vide s'ecrit NULL (DBString, RigTools\SQL\DBObject.cs:165-166).
///   « Rejeter la demande » (Rejeter, l. 673-694 ; frmRejet.cs:56-63, groupe « Motif du rejet », Designer l. 144) : le motif est
///     obligatoire, 300 caracteres au plus (RPJCI_MOTIF_TRAITEMENT_RIG de RIG_DEV) ; RPJCI_ETAT = '2', date, 'R', le motif, l'agent.
/// NON REPRIS (nommes) : la recherche des mandataires par nom (l'agent les nomme) ; « Créer mandataire » (frmAbrege, AJOUTER_USER,
///   l. 899-900) et le rapprochement d'acteurs (frmRapproActeurs, l. 880) : d'autres ecrans de la V3 (Mandataires, Rapprochement).
/// Traces : famille « reglage » (les inscriptions des avocats du greffe), « a modifie », la demande et chaque mandataire, avant et apres.
/// </summary>
internal sealed class EcrivainInscriptionsRpjc : IEcrivainEcran
{
    public string Procedure => "PROC_EDI_RPJC_INS";
    public string Ecran => "EDI RPJC inscriptions";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les gestes et saisies du descripteur (FORM_EDI_RPJC_INS « &amp;Traiter la demande », frmTrt.cs:79-80, frmRejet.Designer l. 144).</summary>
    internal const string Traiter = "Traiter la demande", Rejeter = "Rejeter la demande", Traites = "Mandataires traités",
        Principal = "Mandataire principal", Structures = "Structures traitées", Motif = "Motif du rejet";

    /// <summary>Les messages de RIG v2, au mot pres (frmChoixTrt.cs, frmTrt.cs, frmRejet.cs).</summary>
    internal const string Aucune = "Vous devez sélectionner une demande à traiter",                                                     // frmChoixTrt.cs:250
        EnCoursPar = "Cette demande d'inscription est en cours de traitement par l'utilisateur ",                                   // l. 256
        ModificationTraitee = "Cette demande de modification a déjà été traitée. Vous ne pouvez pas re-traiter une demande de modification déjà traitée.", // l. 268
        Refusee = "Vous ne pouvez pas traiter une demande d'inscription déjà refusée. Cette demande d'inscription a été refusée pour le motif suivant :  ", // l. 283
        ModificationNonGeree = "Attention la modification des demandes d'inscriptions n'est pas encore gérée. Veuillez contacter AMITEL",  // l. 302
        PrincipalInactif = "Le mandataire principal doit être actif et avec abrégé.",                                               // frmTrt.cs:492
        PrincipalNonTraite = "Le mandataire principal doit être remis a traiter.",                                                  // l. 497
        SansPrincipal = "Vous devez cocher un mandataire principal.",                                                               // l. 510, 515
        DeuxPrincipaux = "Vous ne pouvez cocher qu'un seul mandataire principal.",                                                 // l. 524
        SansSelection = "Vous devez sélectionner un ou plusieurs mandataire avant de cliquer sur le bouton traiter.",                // l. 628
        Reussi = "Mise à jour effectuée avec succés.",                                                                              // l. 632
        SansMotif = "Vous devez saisir un motif de rejet pour valider.",                                                            // frmRejet.cs:58
        Rejete = "Rejet effectué avec succés.";                                                                                     // frmTrt.cs:692

    private sealed record Demande(int Id, string? Decision, string TypeMaj, string Motif, string TenuePar, string Cnbf, string CnbfStructure,
                                  string Siren, string Courriels, string Nom);

    private sealed record Mandataire(int Id, string Nom, string Abrege, bool Actif, string Cnbf, string CnbfStructure, string Siren, string Courriel);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var nomGeste = geste.Demande.Nom;
        if (nomGeste != Traiter && nomGeste != Rejeter) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {nomGeste}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser(Aucune);
        if (ids.Length > 1) return IssueGeste.Refuser("RIG v2 traite une demande d'inscription à la fois : cochez-en une seule.");
        if (!int.TryParse(ids[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || await LireDemandeAsync(geste, id, ct) is not { } d)
            return IssueGeste.Refuser($"Demande d'inscription introuvable dans la base du greffe ({ids[0]}).");

        // L'ouverture (frmChoixTrt.cs:248-305), dans l'ordre de la V2.
        if (d.TenuePar.Length > 0) return IssueGeste.Refuser(EnCoursPar + d.TenuePar);
        var dejaTraitee = false;
        if (d.Decision == "A")
        {
            if (d.TypeMaj == "M") return IssueGeste.Refuser(ModificationTraitee);
            dejaTraitee = true;
        }
        else if (d.Decision == "R") return IssueGeste.Refuser(Refusee + d.Motif);
        else if (d.TypeMaj == "M") return IssueGeste.Refuser(ModificationNonGeree);

        var compte = geste.Agent.CompteWindows.Length > 255 ? geste.Agent.CompteWindows[..255] : geste.Agent.CompteWindows;
        var avantDemande = new Dictionary<string, string?> { ["Decision"] = d.Decision };
        var objet = $"la demande d'inscription RPJC n° {d.Id} ({d.Nom})";

        if (nomGeste == Rejeter)
        {
            var motif = Saisi(geste, Motif);
            if (motif.Length == 0) return IssueGeste.Refuser(SansMotif);
            if (motif.Length > 300) return IssueGeste.Refuser($"« {Motif} » : 300 caractères au plus (RPJCI_MOTIF_TRAITEMENT_RIG).");
            await geste.EcrireAsync("""
                UPDATE v2.DEMAT_RPJC_INS SET RPJCI_ETAT = '2', RPJCI_DATE_TRAITEMENT_RIG = @maintenant, RPJCI_ETAT_TRAITEMENT_RIG = 'R',
                       RPJCI_MOTIF_TRAITEMENT_RIG = @motif, RPJCI_USER_TRAITEMENT_RIG = @agent
                WHERE RPJCI_ID_RPJCI = @id
                """, ct, new SqlParameter("@maintenant", System.Data.SqlDbType.DateTime) { Value = geste.Maintenant },
                new SqlParameter("@motif", motif), new SqlParameter("@agent", compte), new SqlParameter("@id", d.Id));
            return IssueGeste.Ecrit(Rejete, [ids[0]],
                new TraceGeste("reglage", objet, ids[0], "a modifie", avantDemande, new Dictionary<string, string?> { ["Decision"] = "R", ["Motif"] = motif }));
        }

        // « Traiter la demande » : les mandataires nommes.
        var (traites, refusT) = await MandatairesAsync(geste, Saisi(geste, Traites), ct);
        if (traites is null) return IssueGeste.Refuser(refusT!);
        var (principaux, refusP) = await MandatairesAsync(geste, Saisi(geste, Principal), ct);
        if (principaux is null) return IssueGeste.Refuser(refusP!);
        var (structures, refusS) = await MandatairesAsync(geste, Saisi(geste, Structures), ct);
        if (structures is null) return IssueGeste.Refuser(refusS!);
        // Les controles (frmTrt.cs:482-530) : chaque principal, puis leur nombre.
        foreach (var p in principaux)
        {
            if (traites.All(t => t.Id != p.Id)) return IssueGeste.Refuser(PrincipalNonTraite);
            if (!p.Actif || p.Abrege.Length == 0) return IssueGeste.Refuser(PrincipalInactif);
        }
        if (principaux.Count > 1) return IssueGeste.Refuser(DeuxPrincipaux);
        if (principaux.Count == 0 && (!dejaTraitee || traites.Count > 0)) return IssueGeste.Refuser(SansPrincipal);
        // Rien a traiter (une demande deja traitee, sans principal) : la V2 valide sans rien ecrire, puis le dit (l. 627-629).
        if (traites.Count == 0 && structures.Count == 0) return IssueGeste.Refuser(SansSelection);
        var principal = principaux.FirstOrDefault();

        var maintenant = new SqlParameter("@maintenant", System.Data.SqlDbType.DateTime) { Value = geste.Maintenant };
        var traces = new List<TraceGeste>();
        var instances = new List<int>();
        foreach (var m in traites)
        {
            // Un traite qui n'est pas le principal est desactive ; le principal garde son etat (l. 550-555).
            var actif = principal?.Id == m.Id && m.Actif;
            await geste.EcrireAsync("""
                UPDATE v2.MANDATAIRE SET MNDTR_NUMERO_CNBF = @cnbf,
                       MNDTR_NUMERO_CNBF_STRUCTURE = CASE WHEN @structure IS NULL THEN MNDTR_NUMERO_CNBF_STRUCTURE ELSE @structure END,
                       MNDTR_SIREN = CASE WHEN @siren IS NULL THEN MNDTR_SIREN ELSE @siren END,
                       MNDTR_ADRESSE_MAIL = CASE WHEN @courriels IS NULL THEN MNDTR_ADRESSE_MAIL ELSE @courriels END,
                       MNDTR_ACTIF = @actif
                WHERE MNDTR_ID_MNDTR = @id
                """, ct, Valeur("@cnbf", d.Cnbf), Valeur("@structure", d.CnbfStructure), Valeur("@siren", d.Siren),
                Valeur("@courriels", d.Courriels), new SqlParameter("@actif", actif), new SqlParameter("@id", m.Id));
            traces.Add(Trace(m, d.Cnbf, d.CnbfStructure.Length > 0 ? d.CnbfStructure : m.CnbfStructure, actif));
            instances.Add(m.Id);
        }
        foreach (var s in structures)
        {
            await geste.EcrireAsync("""
                UPDATE v2.MANDATAIRE SET MNDTR_NUMERO_CNBF = NULL, MNDTR_NUMERO_CNBF_STRUCTURE = @structure,
                       MNDTR_SIREN = CASE WHEN @siren IS NULL THEN MNDTR_SIREN ELSE @siren END
                WHERE MNDTR_ID_MNDTR = @id
                """, ct, Valeur("@structure", d.CnbfStructure), Valeur("@siren", d.Siren), new SqlParameter("@id", s.Id));
            traces.Add(Trace(s, "", d.CnbfStructure, s.Actif));
            instances.Add(s.Id);
        }
        if (instances.Count > 0)
        {
            var marques = string.Join(", ", instances.Select((_, k) => $"@m{k}"));
            await geste.EcrireAsync($"""
                UPDATE v2.INSTANCE SET INSTN_DATE_MAJ_INFOG = @maintenant
                WHERE INSTN_ID_INSTN IN (SELECT p.PRT_ID_INSTN FROM v2.PARTIE AS p
                                         WHERE p.PRT_ID_MNDTR_POS IN ({marques}) OR p.PRT_ID_MNDTR_PLAI IN ({marques}))
                """, ct, [maintenant, .. instances.Select((m, k) => new SqlParameter($"@m{k}", m))]);
        }
        if (!dejaTraitee)
        {
            await geste.EcrireAsync("""
                UPDATE v2.DEMAT_RPJC_INS SET RPJCI_ETAT = '2', RPJCI_DATE_TRAITEMENT_RIG = @maintenant, RPJCI_ETAT_TRAITEMENT_RIG = 'A',
                       RPJCI_USER_TRAITEMENT_RIG = @agent
                WHERE RPJCI_ID_RPJCI = @id
                """, ct, new SqlParameter("@maintenant", System.Data.SqlDbType.DateTime) { Value = geste.Maintenant },
                new SqlParameter("@agent", compte), new SqlParameter("@id", d.Id));
            traces.Insert(0, new TraceGeste("reglage", objet, ids[0], "a modifie", avantDemande, new Dictionary<string, string?> { ["Decision"] = "A" }));
        }
        // Apres la validation (l. 612-623) : tout autre mandataire du CNBF, avec abrege, est desactive.
        if (traites.Count > 0 && principal is not null)
            await geste.EcrireAsync("""
                UPDATE v2.MANDATAIRE SET MNDTR_ACTIF = 0
                WHERE MNDTR_NUMERO_CNBF = @cnbf AND MNDTR_ABREGE IS NOT NULL AND MNDTR_ID_MNDTR <> @principal AND MNDTR_ACTIF = 1
                """, ct, Valeur("@cnbf", d.Cnbf), new SqlParameter("@principal", principal.Id));
        return IssueGeste.Ecrit(Reussi + " Les instances des mandataires traités sont à recentraliser sur Infogreffe : la V3 ne l'appelle pas (envoi simulé).",
            [ids[0]], [.. traces]);
    }

    private static string Saisi(GesteEnCours geste, string libelle) => geste.Demande.Valeurs.TryGetValue(libelle, out var v) ? v.Trim() : "";

    private static SqlParameter Valeur(string nom, string valeur) => new(nom, valeur.Length == 0 ? DBNull.Value : valeur);

    private static TraceGeste Trace(Mandataire m, string cnbf, string structure, bool actif)
        => new("reglage", $"le mandataire {m.Nom}{(m.Abrege.Length > 0 ? $" ({m.Abrege})" : "")}", m.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
               new Dictionary<string, string?> { ["CNBF"] = m.Cnbf, ["CNBF structure"] = m.CnbfStructure, ["Actif"] = m.Actif ? "oui" : "non" },
               new Dictionary<string, string?> { ["CNBF"] = cnbf, ["CNBF structure"] = structure, ["Actif"] = actif ? "oui" : "non" });

    /// <summary>La demande <paramref name="id"/>, verrouillee pour le geste ; null si elle n'est pas dans la base.</summary>
    private static async Task<Demande?> LireDemandeAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT RPJCI_ETAT_TRAITEMENT_RIG, RPJCI_TYPE_MAJ, RPJCI_MOTIF_TRAITEMENT_RIG, RPJCI_USER_EN_COURS, RPJCI_NUMERO_CNBF,
                   RPJCI_NUMERO_CNBF_STRUCTURE, RPJCI_NUMERO_SIREN, RPJCI_MAIL, RPJCI_MAIL2, RPJCI_MAIL3, RPJCI_NOM, RPJCI_PRENOM,
                   RPJCI_DENOMINATION
            FROM v2.DEMAT_RPJC_INS WITH (UPDLOCK) WHERE RPJCI_ID_RPJCI = @id
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        string T(string colonne) => FormeLigne.Texte(r, colonne).Trim();
        // Les courriels comme txtAdressesMail (frmTrt.cs:145) : MAIL, puis « / » MAIL2 et MAIL3 non vides.
        var courriels = T("RPJCI_MAIL") + (T("RPJCI_MAIL2").Length > 0 ? " / " + T("RPJCI_MAIL2") : "") + (T("RPJCI_MAIL3").Length > 0 ? " / " + T("RPJCI_MAIL3") : "");
        var nom = $"{T("RPJCI_NOM")} {T("RPJCI_PRENOM")}".Trim();
        return new Demande(id, T("RPJCI_ETAT_TRAITEMENT_RIG") is { Length: > 0 } dec ? dec : null, T("RPJCI_TYPE_MAJ"), T("RPJCI_MOTIF_TRAITEMENT_RIG"),
            T("RPJCI_USER_EN_COURS"), T("RPJCI_NUMERO_CNBF"), T("RPJCI_NUMERO_CNBF_STRUCTURE"), T("RPJCI_NUMERO_SIREN"), courriels,
            nom.Length > 0 ? nom : T("RPJCI_DENOMINATION"));
    }

    /// <summary>Les mandataires nommes par leur abrege ou leur numero, separes par des virgules ; null et le refus si l'un n'est pas
    /// dans la base, ou si un abrege en designe plusieurs (la grille de la V2 les montre chacun : la V3 demande son numero).</summary>
    private static async Task<(List<Mandataire>?, string?)> MandatairesAsync(GesteEnCours geste, string saisie, CancellationToken ct)
    {
        var liste = new List<Mandataire>();
        foreach (var jeton in saisie.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var numero = int.TryParse(jeton, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
            await using var r = await geste.LireAsync("""
                SELECT TOP (2) MNDTR_ID_MNDTR, MNDTR_NOM, MNDTR_ABREGE, MNDTR_ACTIF, MNDTR_NUMERO_CNBF, MNDTR_NUMERO_CNBF_STRUCTURE, MNDTR_SIREN,
                       MNDTR_ADRESSE_MAIL
                FROM v2.MANDATAIRE WITH (UPDLOCK)
                WHERE (@numero IS NOT NULL AND MNDTR_ID_MNDTR = @numero) OR (@numero IS NULL AND MNDTR_ABREGE = @abrege)
                ORDER BY MNDTR_ID_MNDTR
                """, ct, new SqlParameter("@numero", System.Data.SqlDbType.Int) { Value = (object?)numero ?? DBNull.Value }, new SqlParameter("@abrege", jeton));
            var trouves = new List<Mandataire>();
            while (await r.ReadAsync(ct))
                trouves.Add(new Mandataire(Convert.ToInt32(r["MNDTR_ID_MNDTR"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "MNDTR_NOM"),
                    FormeLigne.Texte(r, "MNDTR_ABREGE"), r["MNDTR_ACTIF"] is true, FormeLigne.Texte(r, "MNDTR_NUMERO_CNBF"),
                    FormeLigne.Texte(r, "MNDTR_NUMERO_CNBF_STRUCTURE"), FormeLigne.Texte(r, "MNDTR_SIREN"), FormeLigne.Texte(r, "MNDTR_ADRESSE_MAIL")));
            if (trouves.Count == 0) return (null, $"Mandataire « {jeton} » introuvable dans la base du greffe.");
            if (trouves.Count > 1) return (null, $"L'abrégé « {jeton} » désigne plusieurs mandataires : donnez son numéro.");
            if (liste.All(m => m.Id != trouves[0].Id)) liste.Add(trouves[0]);
        }
        return (liste, null);
    }
}
