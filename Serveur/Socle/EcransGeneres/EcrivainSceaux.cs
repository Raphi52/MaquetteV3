using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Sceaux » (PROC_SCEAU) : MODIFIER et SUPPRIMER un element de fusion, dans la grille. Mode d'emploi : EcrivainEcran.cs ; les lignes
/// et la fiche : LecteurSceaux.cs. Plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026.
///
/// SOURCE (ancien RIG) : Edition\OPE_EDITION.cs (D:\RigApplication\Source\RIG\DLL\Processus\PROC_SCEAU) et son .Designer.cs.
///   - MODIFIER = Valider (l. 91-116) d'un sceau existant. Ce qui se saisit : le rognage (IntRogner, obligatoire, chiffres seuls :
///     MetierRegExSaisie « [0-9] », .Designer.cs:192-193) et le texte de recherche (TextRecherche, facultatif, .Designer.cs:131,
///     GDRG_CHAINE_RECHERCHEE, l. 109). Ce qui ne se saisit pas : la reference, figee une fois creee (TextReference.MetierReadOnly =
///     !add, l. 243) ; la largeur, verrouillee (IntLargeurMax.MetierReadOnly, .Designer.cs:170) ; le fichier et sa date d'archivage
///     (le document GED). La HAUTEUR se saisit dans la V2, mais la changer -- ou cocher « dimensions du lot 1 », qui la met a 24 mm --
///     recalcule la largeur SUR L'IMAGE (AmiImage.ZoomLargeur sur le fichier extrait, l. 69, l. 281) : la V3 ne decode pas l'image,
///     elle refuse ce changement en le disant. Processus.Verifier refuse aussi un sceau dont l'image n'est pas dans la GED (gedRig
///     null : « Le fichier est obligatoire », l. 163-166 ; « Parcourir » n'est ouvert qu'a l'ajout, l. 241).
///     Valider ecrit (ElementFusion.Sauve, RigMetier\Edition\ElementFusion.cs:94-112, une transaction) : ELEMENT_FUSION -- le rognage,
///     ELMFS_VERSION = ' ', ELMFS_PAR_DEFAUT = 1 (l. 99-100) ; et le document GED_RIG de l'image -- GDRG_PATH_VIRTUEL « Element
///     externe\ », l'agent (UTLST_LOGIN : son compte Windows) et l'instant d'archivage, l'application « PROC_SCEAU »
///     (Processus.MetierCodeProcessus), la reference « ELEMENT_FUSION.ELMFS_NUM_DOCUMENT », l'extension « gif » et le texte de
///     recherche (l. 103-109). Le fichier de l'image n'est pas touche. La base de dev le confirme : ses six sceaux portent ces valeurs
///     (extension « gif » meme pour un .png). La V2 active Valider sur un changement (l. 54-58) : sans changement, rien a enregistrer.
///   - SUPPRIMER (l. 176-181) : apres la confirmation « Attention, la suppression de ce sceau impactera l'ensemble des maquettes
///     l'utilisant. Voulez-vous continuer ? », ElementFusion.Delete supprime la ligne ELEMENT_FUSION ; le document GED de l'image N'EST
///     PAS supprime (ElementFusion.cs:124-126, bloc gedRig.Delete commente) : la V3 fait de meme. Aucun controle ne refuse.
/// NON REPRIS (nommes) : AJOUTER -- il importe le fichier de l'image choisi sur le poste (« Parcourir », GedRig.CreateGedRig sur un
/// chemin local, l. 93-96 ; « Le fichier est obligatoire », l. 163-166) ; « Mettre à jour » (une nouvelle image du poste, l. 126-140) et
/// « Récupérer la dernière version » (une copie de l'image sur le poste, l. 142-154) : des fichiers du poste. Changer la hauteur :
/// voir plus haut.
/// TRACE : famille « reglage », « a modifie » / « a supprime ».
/// </summary>
// fix-ok: l'ancienne cause « Valider re-archive le document GED » etait fausse (objection du juge, 27/11/2026) : Valider ne pose que
// des colonnes d'ELEMENT_FUSION et de GED_RIG (OPE_EDITION.cs:99-111, relu) ; les six sceaux de la base de dev portent ces valeurs.
internal sealed class EcrivainSceaux : IEcrivainEcran
{
    public string Procedure => "PROC_SCEAU";
    public string Ecran => "Sceaux";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Modifier, TypeGeste.Supprimer];

    /// <summary>La section de la fiche qui porte le texte de recherche (Pont/EcransGeneres/Configurer.cs, PROC_SCEAU).</summary>
    internal const string SectionImage = "Image", ChampRecherche = "Texte de recherche";
    internal const string ColNom = "Nom externe", ColFichier = "Fichier", ColHauteur = "Hauteur max", ColLargeur = "Largeur max",
        ColRogner = "Rogner", ColArchive = "Date archivage", ChampLot1 = "Dimensions du lot 1";

    internal const string NomFige = "Le nom externe ne se modifie pas : RIG v2 le fige une fois le sceau enregistré.";
    internal const string HauteurSurImage = "La hauteur ne se modifie pas ici : RIG v2 recalcule alors la largeur sur l'image du sceau, que la V3 ne décode pas. Seuls le rognage et le texte de recherche s'enregistrent.";
    internal const string LargeurCalculee = "La largeur se calcule sur l'image, elle ne se saisit pas : RIG v2 la verrouille.";
    internal const string FichierObligatoire = "Le fichier est obligatoire : l'image de ce sceau n'est pas dans la GED du greffe.";
    internal const string RognerChiffres = "« Rogner » : des chiffres seuls, un nombre de millimètres.";
    internal const string RienAEnregistrer = "Rien à enregistrer : aucune valeur ne change.";

    private sealed record Sceau(int Id, string Reference, string Numero, int Hauteur, int Largeur, int Rogner, bool EnGed, string Recherche);

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
        => geste.Demande.Type == TypeGeste.Modifier ? ModifierAsync(geste, ct) : SupprimerAsync(geste, ct);

    private static async Task<IssueGeste> ModifierAsync(GesteEnCours geste, CancellationToken ct)
    {
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length != 1) return IssueGeste.Refuser("Choisissez une seule ligne.");
        var id = ids[0];
        if (await SceauAsync(geste, id, ct) is not { } e) return IssueGeste.Refuser($"Sceau introuvable dans la base du greffe ({id}).");
        // Ce qui ne se saisit pas (en tete du fichier), avant les controles de Valider.
        if (Cellule(geste, ColNom) is { } nom && nom != e.Reference) return IssueGeste.Refuser(NomFige);
        if (Cellule(geste, ColFichier) is not null) return IssueGeste.Refuser($"« {ColFichier} » se lit, ne se saisit pas.");
        if (Cellule(geste, ColArchive) is not null) return IssueGeste.Refuser($"« {ColArchive} » se lit, ne se saisit pas.");
        if (Cellule(geste, ColHauteur) is { } h && h != Texte(e.Hauteur)) return IssueGeste.Refuser(HauteurSurImage);
        if (geste.Valeur("Dimensions", ChampLot1) is not null) return IssueGeste.Refuser(HauteurSurImage);
        if (Cellule(geste, ColLargeur) is { } l && l != Texte(e.Largeur)) return IssueGeste.Refuser(LargeurCalculee);
        // Processus.Verifier : le rognage obligatoire, saisi en chiffres (.Designer.cs:192-193) ; puis DoVerifier : l'image en GED.
        var rogner = e.Rogner;
        if (Cellule(geste, ColRogner) is { } saisi)
        {
            if (saisi.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ColRogner));
            if (!saisi.All(char.IsAsciiDigit) || !int.TryParse(saisi, NumberStyles.None, CultureInfo.InvariantCulture, out rogner))
                return IssueGeste.Refuser(RognerChiffres);
        }
        var recherche = geste.Valeur(SectionImage, ChampRecherche)?.Trim() ?? e.Recherche;
        if (!e.EnGed) return IssueGeste.Refuser(FichierObligatoire);
        if (rogner == e.Rogner && recherche == e.Recherche) return IssueGeste.Refuser(RienAEnregistrer);
        // ElementFusion.Sauve : le document GED puis l'element, dans la transaction du geste (OPE_EDITION.cs:99-111).
        await geste.EcrireAsync("""
            UPDATE v2.GED_RIG SET GDRG_PATH_VIRTUEL = 'Element externe\', GDRG_LOGIN_NT_ARCHIVAGE = @login, GDRG_DATE_ARCHIVAGE = @maintenant,
                                  GDRG_APPLICATION_ARCHIVAGE = 'PROC_SCEAU', GDRG_REFERENCE = 'ELEMENT_FUSION.ELMFS_NUM_DOCUMENT',
                                  GDRG_EXTENSION = 'gif', GDRG_CHAINE_RECHERCHEE = @recherche
            WHERE GDRG_NUM_DOCUMENT = @numero
            """, ct, new SqlParameter("@login", geste.Agent.CompteWindows), new SqlParameter("@maintenant", geste.Maintenant),
            new SqlParameter("@recherche", recherche.Length == 0 ? DBNull.Value : recherche), new SqlParameter("@numero", e.Numero));
        await geste.EcrireAsync("""
            UPDATE v2.ELEMENT_FUSION SET ELMFS_ROGNER_MM = @rogner, ELMFS_VERSION = ' ', ELMFS_PAR_DEFAUT = 1 WHERE ELMFS_ID_ELMFS = @id
            """, ct, new SqlParameter("@rogner", rogner), new SqlParameter("@id", e.Id));
        return IssueGeste.Ecrit($"Sceau {e.Reference} enregistré : son image est marquée archivée par vous à cette date, comme dans RIG v2.", [id],
            new TraceGeste("reglage", $"le sceau {e.Reference}", id, "a modifie", Etat(e.Rogner, e.Recherche), Etat(rogner, recherche)));
    }

    private static async Task<IssueGeste> SupprimerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (await SceauAsync(geste, id, ct) is not { } e) return IssueGeste.Refuser($"Sceau introuvable dans la base du greffe ({id}).");
        await geste.EcrireAsync("DELETE FROM v2.ELEMENT_FUSION WHERE ELMFS_ID_ELMFS = @id", ct, new SqlParameter("@id", e.Id));
        return IssueGeste.Ecrit($"Sceau {e.Reference} supprimé : les maquettes qui l'utilisent ne le trouveront plus (son image reste dans la GED, comme dans RIG v2).",
            [id], new TraceGeste("reglage", $"le sceau {e.Reference}", id, "a supprime",
                new Dictionary<string, string> { ["Référence"] = e.Reference, ["Document GED"] = e.Numero }, null));
    }

    /// <summary>Le sceau <paramref name="id"/> et son document GED (absent : EnGed faux), sous verrou ; null s'il n'existe pas.</summary>
    private static async Task<Sceau?> SceauAsync(GesteEnCours geste, string id, CancellationToken ct)
    {
        if (CriteresConsultation.Entier(id) is not { } n) return null;
        await using var r = await geste.LireAsync("""
            SELECT e.ELMFS_REFERENCE, e.ELMFS_NUM_DOCUMENT, e.ELMFS_HAUTEUR_MAX_MM, e.ELMFS_LARGEUR_MAX_MM, e.ELMFS_ROGNER_MM,
                   g.GDRG_NUM_DOCUMENT, g.GDRG_CHAINE_RECHERCHEE
            FROM v2.ELEMENT_FUSION AS e WITH (UPDLOCK)
            LEFT JOIN v2.GED_RIG AS g WITH (UPDLOCK) ON g.GDRG_NUM_DOCUMENT = e.ELMFS_NUM_DOCUMENT
            WHERE e.ELMFS_ID_ELMFS = @id
            """, ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        return new Sceau(n, FormeLigne.Texte(r, "ELMFS_REFERENCE"), FormeLigne.Texte(r, "ELMFS_NUM_DOCUMENT"), Entier(r, 2), Entier(r, 3), Entier(r, 4),
            !r.IsDBNull(5), FormeLigne.Texte(r, "GDRG_CHAINE_RECHERCHEE"));
    }

    private static int Entier(SqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);

    private static string Texte(int mm) => mm.ToString(CultureInfo.InvariantCulture);

    private static string? Cellule(GesteEnCours geste, string colonne) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v.Trim() : null;

    private static Dictionary<string, string> Etat(int rogner, string recherche) => new()
    {
        ["Rogner"] = $"{Texte(rogner)} mm", ["Texte de recherche"] = recherche,
    };
}
