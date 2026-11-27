using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Rapprocher des acteurs » (PROC_RAPPACTEUR) : les acteurs judiciaires qu'on peut fusionner -- juges, mandataires, mandataires de
/// procedure collective --, un groupe par type. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026.
/// La fusion : EcrivainRapprochementActeurs.cs.
///
/// SOURCE (ancien RIG) : PROC_RAPPACTEUR (D:\RigApplication\Source\RIG\DLL\Processus\PROC_RAPPACTEUR ; Detail OPE de
/// proc_rappacteur.md, qui fait foi). L'ecran de la V2 choisit un TYPE (juges, mandataires, mandataires PC), cherche par nom, et
/// montre la grille des acteurs « a supprimer » parmi TOUS les acteurs de ce type (filtre « TOUS », OPE_SELECTION_ACTEUR.Juges.cs:58),
/// par les requetes _RechercherJugesSQL / _RechercherMandatairesSQL / _RechercherMandatairesPCSQL (OPE_SELECTION_ACTEUR.Juges.cs:135,
/// .Mandataires.cs:130-180, .MandatairesPC.cs:128-133). La V3 montre les trois types a la fois, un groupe chacun, plafonne par groupe
/// (LignesLues.TotalParGroupe) : un type long ne masque pas les autres.
/// RECHERCHE : « Type d'acteur » (Juges, Mandataires, Mandataires PC) et « Nom » : chaque mot cherche dans le nom, le prenom et l'abrege,
/// insensible a la casse et aux accents, les mots s'ajoutant (_Collate, OPE_SELECTION_ACTEUR.cs:169) ; le champ de recherche fait de meme.
/// NOM : celui de la V2, dbo.GetCiviliteNomPrenom (l'abrege d'edition de la civilite, le nom, le prenom ou le complement de nom).
/// ETAT (ce qui decide si l'acteur peut etre coche « a supprimer ») :
///   - Validee « Protégé (actif, n° CNBF) » : un mandataire actif qui a un numero CNBF -- la V2 refuse de le supprimer
///     (Res.TEXTE_MANDATAIRE_ACTIF_AVEC_CNBF, _VerifierMandataires, .Mandataires.cs:195) : la ligne ne se coche pas ;
///   - AFaire « Actif » : un acteur actif (JUG_ACTIF, MNDTR_ACTIF, MNDPC_ACTIF = 1) ;
///   - EnCours « Inactif » : un acteur inactif.
/// L'id d'une ligne est « M-&lt;id&gt; », « J-&lt;id&gt; » ou « P-&lt;id&gt; » : les trois tables ont chacune leur numerotation.
/// FICHE (l'apercu, avant confirmation) : « Liaisons reprises » -- pour un mandataire, ce qui le reference (Mandataire.GetLiaisonEntite,
/// LiaisonsMandataire.cs) et les affaires ou il est avocat (le chemin des destinataires d'edition, .Mandataires.cs:417-430) : ce que la
/// fusion reportera sur l'acteur garde. Pour un juge ou un mandataire PC, ses liaisons (LiaisonsActeur.cs, dans l'ordre de la fusion
/// V2) et ses affaires (instances d'un juge, procedures collectives d'un mandataire PC).
/// </summary>
internal sealed class LecteurRapprochementActeurs : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_RAPPACTEUR";

    internal const string Juges = "Juges", Mandataires = "Mandataires", MandatairesPc = "Mandataires PC";
    internal const string CritereType = "Type d'acteur", CritereNom = "Nom";
    internal const string SectionLiaisons = "Liaisons reprises", ChampLiaisons = "Liaisons", ChampAffaires = "Affaires touchées";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereType, CritereNom];

    /// <summary>Le prefixe d'id de chaque type.</summary>
    internal static string Prefixe(string groupe) => groupe switch { Juges => "J", MandatairesPc => "P", _ => "M" };

    /// <summary>« M-12 » -> (Mandataires, 12) ; null si l'id n'est pas d'un type connu.</summary>
    internal static (string Groupe, int Id)? Lire(string id)
    {
        var morceaux = id.Split('-', 2);
        if (morceaux.Length != 2 || CriteresConsultation.Entier(morceaux[1]) is not { } n) return null;
        return morceaux[0] switch { "J" => (Juges, n), "M" => (Mandataires, n), "P" => (MandatairesPc, n), _ => null };
    }

    // Les trois listes de la V2, reunies : le type (Rang), l'id, le nom de la V2, l'abrege, le CNBF (mandataires), l'etat actif.
    // Sous-requetes, pas de WITH : LectureV2 n'accepte qu'un SELECT (LectureV2.ExigerLecture). fix-ok: mesure au banc du lot 14
    // (« Lecture V2 refusee : seule une requete SELECT seule est permise », sur un WITH) ; puis, a la lecture des lignes obtenues, un
    // nom vide rendait « M. » et la civilite PM precedait le nom d'un mandataire PC : meme regle que LecteurMandataires (abrege a
    // defaut de nom, PM et ND jamais devant le nom).
    private const string Requete = """
        SELECT * FROM (
        SELECT *, ROW_NUMBER() OVER (PARTITION BY Rang ORDER BY Tri, Id) AS Place, COUNT(*) OVER (PARTITION BY Rang) AS ParGroupe,
               COUNT(*) OVER () AS Total
        FROM (
            SELECT 0 AS Rang, 'J' AS Type, j.JUG_ID_JUG AS Id, CONCAT_WS(' ', CASE WHEN j.JUG_CIVILITE IN ('PM', 'ND') THEN NULL ELSE c.R_CIVILITE_ABREGE_EDIT END, j.JUG_NOM, j.JUG_PRENOM) AS Nom,
                   j.JUG_NOM AS Tri, j.JUG_ABREGE AS Abrege, CAST(NULL AS varchar(6)) AS Cnbf, j.JUG_ACTIF AS Actif,
                   CONCAT(j.JUG_NOM, ' ', j.JUG_PRENOM, ' ', j.JUG_ABREGE) AS Cherche
            FROM v2.JUGE AS j LEFT JOIN v2.CIVILITE AS c ON c.R_CIVILITE_VALEUR = j.JUG_CIVILITE
            UNION ALL
            SELECT 1, 'M', m.MNDTR_ID_MNDTR,
                   CASE WHEN ISNULL(m.MNDTR_NOM, '') = '' THEN m.MNDTR_ABREGE WHEN m.MNDTR_CIVILITE IN ('PM', 'ND') THEN m.MNDTR_NOM ELSE CONCAT_WS(' ', c.R_CIVILITE_ABREGE_EDIT, m.MNDTR_NOM) END,
                   m.MNDTR_NOM, m.MNDTR_ABREGE, m.MNDTR_NUMERO_CNBF, m.MNDTR_ACTIF, CONCAT(m.MNDTR_NOM, ' ', m.MNDTR_ABREGE)
            FROM v2.MANDATAIRE AS m LEFT JOIN v2.CIVILITE AS c ON c.R_CIVILITE_VALEUR = m.MNDTR_CIVILITE
            UNION ALL
            SELECT 2, 'P', p.MNDPC_ID_MNDPC, CONCAT_WS(' ', CASE WHEN p.MNDPC_CIVILITE IN ('PM', 'ND') THEN NULL ELSE c.R_CIVILITE_ABREGE_EDIT END, p.MNDPC_NOM, p.MNDPC_COMPLEMENT_NOM),
                   p.MNDPC_NOM, p.MNDPC_ABREGE, NULL, p.MNDPC_ACTIF, CONCAT(p.MNDPC_NOM, ' ', p.MNDPC_ABREGE)
            FROM v2.MANDATAIRE_PC AS p LEFT JOIN v2.CIVILITE AS c ON c.R_CIVILITE_VALEUR = p.MNDPC_CIVILITE
        ) AS acteurs WHERE 1 = 1 /*filtres*/
        ) AS numerotes WHERE Place <= @plafond ORDER BY Rang, Place
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lister(baseGreffe, new FiltresRecherche(), plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (recherche.Critere(CritereType) is { } type)
        {
            var rang = type.Trim().ToUpperInvariant() switch
            {
                "JUGES" or "JUGE" => 0, "MANDATAIRES" or "MANDATAIRE" => 1, "MANDATAIRES PC" or "MANDATAIRE PC" or "MANDATAIRES_PC" => 2, _ => -1,
            };
            f.Ajouter("Rang = @rang", ("@rang", rang));
        }
        // _Collate (OPE_SELECTION_ACTEUR.cs:169) : chaque mot est cherche (nom, prenom, abrege), sans casse ni accents ; les mots s'ajoutent.
        var mots = $"{recherche.Texte} {recherche.Critere(CritereNom)}".Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var k = 0; k < mots.Length; k++)
            f.Ajouter($"Cherche COLLATE SQL_Latin1_General_CP1_CI_AI LIKE @mot{k}", ($"@mot{k}", FormeLigne.Contient(mots[k])));
        return Lister(baseGreffe, f, plafond, ct);
    }

    private static async Task<LignesLues> Lister(SqlConnection baseGreffe, FiltresRecherche f, int plafond, CancellationToken ct)
    {
        var lignes = new List<LigneLue>();
        var parGroupe = new Dictionary<string, int>(StringComparer.Ordinal) { [Juges] = 0, [Mandataires] = 0, [MandatairesPc] = 0 };
        await using var r = await LectureV2.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));
        while (await r.ReadAsync(ct))
        {
            var groupe = FormeLigne.Texte(r, "Type") switch { "J" => Juges, "P" => MandatairesPc, _ => Mandataires };
            parGroupe[groupe] = Convert.ToInt32(r["ParGroupe"], CultureInfo.InvariantCulture);
            var actif = Convert.ToBoolean(r["Actif"], CultureInfo.InvariantCulture);
            var abrege = FormeLigne.Texte(r, "Abrege");
            var cnbf = FormeLigne.Texte(r, "Cnbf");
            var protege = actif && cnbf.Length > 0;
            var precisions = FormeLigne.Joindre(abrege.Length > 0 ? $"abrégé {abrege}" : "sans abrégé", cnbf.Length > 0 ? $"CNBF {cnbf}" : null,
                actif ? "actif" : "inactif", protege ? "ne se supprime pas (actif, n° CNBF)" : null);
            lignes.Add(new LigneLue(FormeLigne.Texte(r, "Nom"), precisions, groupe, protege ? "Validee" : actif ? "AFaire" : "EnCours", [])
            {
                Id = $"{FormeLigne.Texte(r, "Type")}-{FormeLigne.Texte(r, "Id")}",
            });
        }
        return new LignesLues(lignes, parGroupe.Values.Sum()) { TotalParGroupe = parGroupe };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (Lire(id) is not { } acteur) return null;
        var (table, cle, nom) = acteur.Groupe switch
        {
            Juges => ("v2.JUGE", "JUG_ID_JUG", "CONCAT_WS(' ', JUG_NOM, JUG_PRENOM)"),
            MandatairesPc => ("v2.MANDATAIRE_PC", "MNDPC_ID_MNDPC", "CONCAT_WS(' ', MNDPC_NOM, MNDPC_COMPLEMENT_NOM)"),
            _ => ("v2.MANDATAIRE", "MNDTR_ID_MNDTR", "MNDTR_NOM"),
        };
        string nomLu;
        await using (var r = await LectureV2.LireAsync(baseGreffe, $"SELECT {nom} AS Nom FROM {table} WHERE {cle} = @id", ct, new SqlParameter("@id", acteur.Id)))
        {
            if (!await r.ReadAsync(ct)) return null;
            nomLu = FormeLigne.Texte(r, "Nom");
        }
        // Un juge ou un mandataire PC : ses liaisons (LiaisonsActeur.cs) dans l'ordre de la fusion V2, et ses affaires (instances d'un juge,
        // procedures collectives d'un mandataire PC) -- les memes que l'apercu de « Remplacer un acteur ».
        if (acteur.Groupe != Mandataires)
        {
            var (t, ordre) = EcrivainRapprochementActeurs.Fusion(acteur.Groupe);
            var parLiaison = (await LiaisonsActeur.CompterAsync(t, (sql, p) => LectureV2.LireAsync(baseGreffe, sql, ct, p), acteur.Id, ct))
                .ToDictionary(c => c.Liaison, c => c.Nombre);
            var liees = ordre.Select(o => (o.Liaison.Libelle, Nombre: parLiaison[o.Liaison])).Where(c => c.Nombre > 0).ToArray();
            var desAffaires = new List<string[]>();
            await using (var r = await LectureV2.LireAsync(baseGreffe, LiaisonsActeur.RequeteAffaires(t), ct, new SqlParameter("@id", acteur.Id)))
                while (await r.ReadAsync(ct)) desAffaires.Add([FormeLigne.Texte(r, "Numero"), FormeLigne.Texte(r, "Libelle")]);
            return new FicheEcran(Procedure, id, nomLu,
            [
                new(SectionLiaisons,
                [
                    new ChampFiche(ChampLiaisons, liees.Length == 0 ? "aucune liaison" : string.Join(", ", liees.Select(c => $"{c.Nombre} × {c.Libelle.ToLowerInvariant()}")))
                    {
                        Colonnes = ["Liaison", "Nombre"], Lignes = [.. liees.Select(c => new[] { c.Libelle, c.Nombre.ToString(CultureInfo.InvariantCulture) })],
                    },
                    new ChampFiche(ChampAffaires, desAffaires.Count == 0 ? "aucune affaire" : $"{desAffaires.Count} affaire{(desAffaires.Count > 1 ? "s" : "")}")
                    {
                        Colonnes = ["Numéro", "Affaire"], Lignes = [.. desAffaires],
                    },
                ]),
            ]);
        }
        var comptes = await LiaisonsMandataire.CompterAsync((sql, p) => LectureV2.LireAsync(baseGreffe, sql, ct, p), acteur.Id, ct);
        var affaires = new List<string[]>();
        await using (var r = await LectureV2.LireAsync(baseGreffe, LiaisonsMandataire.RequeteAffaires, ct, new SqlParameter("@id", acteur.Id)))
            while (await r.ReadAsync(ct)) affaires.Add([FormeLigne.Texte(r, "INSTN_NUM_ROLE"), FormeLigne.Texte(r, "INSTN_NOM_COURANT")]);
        return new FicheEcran(Procedure, id, nomLu,
        [
            new(SectionLiaisons,
            [
                new ChampFiche(ChampLiaisons, LiaisonsMandataire.EnMots(comptes) is { Length: > 0 } mots ? mots : "aucune liaison")
                {
                    Colonnes = ["Liaison", "Nombre"],
                    Lignes = [.. comptes.Where(c => c.Nombre > 0).Select(c => new[] { c.Liaison.Libelle, c.Nombre.ToString(CultureInfo.InvariantCulture) })],
                },
                new ChampFiche(ChampAffaires, affaires.Count == 0 ? "aucune affaire" : $"{affaires.Count} affaire{(affaires.Count > 1 ? "s" : "")}")
                {
                    Colonnes = ["Numéro de rôle", "Affaire"], Lignes = [.. affaires],
                },
            ]),
        ]);
    }
}
