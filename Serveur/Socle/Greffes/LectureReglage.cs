using System.Reflection;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.Greffes;

// ============================================================
// LIRE UN REGLAGE DU GREFFE COMME LA V2 (SOC-003 CA02, CA03, CA04 ; lot 4).
//
// La V2 garde chaque reglage en TEXTE (PARAMETRAGE.PARAM_VALEUR varchar(1000)) et
// chaque fonction l'interprete a sa facon. Dans RIG_DEV (lu le 2026-11-20) : 272 oui /
// non ecrits 0 ou 1, et 33 autrement (OUI, NON, O, N, True, False, Vrai, Faux, false).
// La V3 reprend le texte TEL QUEL et lit chaque reglage avec la regle de la fonction
// V2 qui le lit -- une normalisation serait une decision du referent Socle.
//
// Les regles relevees dans le code V2 :
//  * EgalUn : « == "1" », exact (Parametrage.cs:163-166, IsGreffeCfeVirtuel) ;
//  * EgalOUI : « == "OUI" », exact (Message_Regent_V2016-02_Xml.cs:649 ; ListeLots.vb:634) ;
//  * ConversionDBBool : la lecture d'une case a cocher (SqlRigDataReader.GetBool ->
//    DBBool.SetObj, RigTools\SQL\DBObject.cs:444-475) : bool.TryParse (« True »,
//    « false »...), sinon la PREMIERE lettre -- '1', 'Y', 'O' = oui ; '0', 'N' = non ;
//    tout le reste = non. « Vrai » y vaut donc NON : c'est la V2, on la reproduit.
// Ces trois regles sont celles du code .NET. Les reglages ecrits « Vrai » / « Faux »
// (CMANDM_TOUJOURS_OP...) sont ecrits ET relus par des ecrans VB6 (RIG_CLIENT_CHGT_MAND_MASSE,
// frmParametrage.frm:1284 et 1590), qui convertissent selon la langue de Windows : cette
// regle-la n'est pas encore reproduite (docs/reconstruction/socle-greffes.md) ; ne pas leur
// appliquer ConversionDBBool.
//
// Le texte d'un reglage, comme Parametrage.GetValeurParametrage (Parametrage.cs:98-102) :
// absent de la base -> la valeur par defaut DE LA FONCTION ; present mais NULL -> ""
// (DBString vide, DBObject.cs:113-115), PAS la valeur par defaut.
//
// QUI LIT QUOI (CA04). Une fonction de la V3 qui lit un reglage le DECLARE par un champ
//     internal static readonly LectureReglage CheminModele = new("SEND_EMAIL_TEMPLATE_PATH", "", "MailTemplateStore.cs:33");
// dans sa propre classe, n'importe ou dans le serveur. Toutes() les trouve par
// reflexion, comme les modules : aucune liste commune a tenir. Un reglage repris que
// personne ne declare figure dans la liste remise au referent Socle (RepriseReglages).
// ============================================================

/// <summary>Comment une fonction V2 transforme le texte d'un reglage en oui / non.</summary>
public enum RegleOuiNon
{
    /// <summary>Oui si le texte vaut exactement « 1 » (Parametrage.cs:164).</summary>
    EgalUn,

    /// <summary>Oui si le texte vaut exactement « OUI » (Message_Regent_V2016-02_Xml.cs:649).</summary>
    EgalOUI,

    /// <summary>Conversion d'une case a cocher V2 (DBObject.cs:444-475).</summary>
    ConversionDBBool,
}

/// <summary>La lecture d'UN reglage par une fonction de la V3, avec la valeur par defaut de la V2.</summary>
/// <param name="Code">Le code du reglage (PARAM_CODE).</param>
/// <param name="SiAbsent">Ce que la fonction V2 prend quand le reglage manque dans la base.</param>
/// <param name="SourceV2">La ligne du code V2 qui le lit (fichier:ligne).</param>
public sealed record LectureReglage(string Code, string SiAbsent, string SourceV2)
{
    /// <summary>Le texte du reglage : absent -> <see cref="SiAbsent"/> ; NULL -> "" (comme la V2).</summary>
    public async Task<string> LireTexteAsync(SqlConnection baseGreffe, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand("SELECT Valeur FROM soc.Reglage WHERE Code = @c;", baseGreffe);
        cmd.Parameters.Add(new SqlParameter("@c", System.Data.SqlDbType.VarChar, 100) { Value = Code });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return SiAbsent;
        return r.IsDBNull(0) ? "" : r.GetString(0);
    }

    /// <summary>Le reglage lu en oui / non avec la regle de la fonction V2.</summary>
    public async Task<bool> LireOuiNonAsync(SqlConnection baseGreffe, RegleOuiNon regle, CancellationToken ct = default)
        => OuiNon(await LireTexteAsync(baseGreffe, ct), regle);

    /// <summary>Le texte d'un reglage lu en oui / non, exactement comme la regle V2 le lit.</summary>
    public static bool OuiNon(string? texte, RegleOuiNon regle) => regle switch
    {
        RegleOuiNon.EgalUn => texte == "1",
        RegleOuiNon.EgalOUI => texte == "OUI",
        RegleOuiNon.ConversionDBBool => ConversionDBBool(texte),
        _ => throw new ArgumentOutOfRangeException(nameof(regle), regle, null),
    };

    // DBBool.SetObj (RigTools\SQL\DBObject.cs:444-475), sur un texte : vide ou NULL = NULL,
    // dont la valeur est false (_valeurNull, DBObject.cs:426-428).
    private static bool ConversionDBBool(string? texte)
    {
        if (string.IsNullOrEmpty(texte)) return false;
        if (bool.TryParse(texte, out var v)) return v;
        return texte[0] switch
        {
            '1' or 'Y' or 'O' => true,
            _ => false, // '0', 'N' et tout le reste
        };
    }

    /// <summary>Les reglages que la V3 lit : tout champ statique de type <see cref="LectureReglage"/>
    /// du serveur (CA04). Par code, sans doublon.</summary>
    public static IReadOnlyList<LectureReglage> Toutes()
        => [.. typeof(LectureReglage).Assembly.GetTypes()
                .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(f => f.FieldType == typeof(LectureReglage))
                .Select(f => (LectureReglage)f.GetValue(null)!)
                .DistinctBy(l => l.Code, StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l.Code, StringComparer.OrdinalIgnoreCase)];
}
