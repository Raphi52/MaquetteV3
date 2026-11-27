using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.Greffes;

/// <summary>
/// L'ECRITURE D'UN REGLAGE DU GREFFE (soc.Reglage, la reprise de PARAMETRAGE : RepriseReglages.cs), la SEULE de la V3. Deux
/// chemins l'appellent : la route PUT <see cref="RoutesGreffes.Reglage"/> (ModuleGreffes, ecran « Reglages du greffe ») et le geste
/// d'un ecran genere qui ecrit SON reglage comme la V2 (<see cref="GesteEnCours.ModifierReglageAsync"/> : « Codes chefs IP »,
/// IP_CODE_CHEF, ETP_PARAM_IP.DoSauveAfterOperation ; « Facturation Expresso »). Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026 :
/// extraite de la route, sans seconde copie, pour que le lot 15 s'en serve aussi.
///
/// CE QU'ELLE FAIT, comme Parametrage.UpdateValeurParametrage (Parametrage.cs:103-121), DANS la transaction de l'appelant :
///   1. le reglage doit EXISTER : un code absent ne se cree pas (GetParametrage null -> false, l. 110 ; ni INSERT, ni DELETE : la
///      valeur d'un code existant seulement) ;
///   2. une valeur de plus de 1 000 caracteres est REFUSEE, rien n'est ecrit : PARAMETRAGE.Verifier (PARAMETRAGE.cs:242, PARAM_VALEUR
///      varchar(1000)) leve TROPGRAND, « PARAMETRAGE.PARAM_VALEUR est limité à 1000 caractères » (DBObject.cs:993-994, libelle
///      table.champ de DBchamps.cs:260-262). Le parametre varchar(1000) de l'UPDATE la coupait sans rien dire : la colonne
///      soc.Reglage.Valeur en tient 1 000 (SOC-003_reglages.sql:18) ;
///   3. Valeur, AuteurModif (le NOM de l'agent, <see cref="AuteurReglage"/>, coupe a 50 comme PARAM_AUTEUR_MODIF) et DateModif ;
///   4. relit la ligne ecrite.
/// LE DROIT N'EST PAS ICI : il est celui du chemin. La route « Reglages du greffe » exige le droit de modifier le greffe
/// (ServiceDroits.PeutModifierLeGreffe, OPE_EDITER_GREFFE.cs:30-38 -- l'outil d'exploitation PROC_GREFFE) ; un geste d'ecran suit la
/// regle de tous les gestes, l'agent admis (EcrivainEcran.cs, « Le droit ») : PROC_PARAM_IP et PROC_PARAMEXPRESSO ne controlent aucun
/// droit dans la V2 (grep droit/habilit/admin : rien). Refus du chef du 27/11/2026.
/// Elle n'ecrit PAS le journal des gestes : elle rend sa ligne (<see cref="ReglageEcrit.Modifie.Trace"/>). La route l'ecrit
/// (JournalGestes.EcrireAsync, avec l'ecran de la saisie) ; un geste la rend a son socle, qui l'ecrit au nom de l'agent
/// (EcrivainEcran.cs, point 5) -- une seule ligne par modification dans les deux cas.
/// </summary>
internal static class EcritureReglage
{
    /// <summary>La taille de soc.Reglage.Valeur (SOC-003_reglages.sql:18), celle de PARAMETRAGE.PARAM_VALEUR (PARAMETRAGE.cs:54).</summary>
    internal const int LongueurMax = 1000;

    /// <summary>Le message de la V2 pour une valeur trop longue, au mot pres (DBObject.cs:993-994).</summary>
    internal const string MotifTropLong = "PARAMETRAGE.PARAM_VALEUR est limité à 1000 caractères";

    /// <summary>Modifie la valeur du reglage <paramref name="code"/> du greffe ouvert sur <paramref name="cn"/>, dans
    /// <paramref name="tx"/> ; l'appelant valide ou annule, et a deja juge le droit. <paramref name="auteur"/> : <see cref="AuteurReglage"/>.</summary>
    public static async Task<ReglageEcrit> ModifierAsync(SqlConnection cn, SqlTransaction tx, string auteur, string code, string? valeur,
                                                       CancellationToken ct)
    {
        if ((await ModuleGreffes.LireReglagesAsync(cn, tx, code, ct)).FirstOrDefault() is not { } avant) return new ReglageEcrit.Absent();
        if (valeur is { Length: > LongueurMax }) return new ReglageEcrit.TropLong(MotifTropLong);
        await using (var cmd = new SqlCommand(
            "UPDATE soc.Reglage SET Valeur = @v, AuteurModif = @a, DateModif = GETDATE() WHERE Code = @c;", cn, tx))
        {
            cmd.Parameters.Add(new SqlParameter("@v", System.Data.SqlDbType.VarChar, LongueurMax) { Value = (object?)valeur ?? DBNull.Value });
            // varchar(50) comme PARAM_AUTEUR_MODIF : la V2 coupe aussi ses textes a la taille de la colonne.
            cmd.Parameters.Add(new SqlParameter("@a", System.Data.SqlDbType.VarChar, 50) { Value = auteur.Length > 50 ? auteur[..50] : auteur });
            cmd.Parameters.Add(new SqlParameter("@c", System.Data.SqlDbType.VarChar, 100) { Value = code });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        return new ReglageEcrit.Modifie(avant, (await ModuleGreffes.LireReglagesAsync(cn, tx, code, ct)).Single());
    }
}

/// <summary>Ce que rend <see cref="EcritureReglage.ModifierAsync"/>.</summary>
public abstract record ReglageEcrit
{
    private ReglageEcrit() { }

    /// <summary>Le reglage n'existe pas dans la base du greffe : rien n'est ecrit (un reglage ne se cree pas).</summary>
    public sealed record Absent : ReglageEcrit;

    /// <summary>La valeur depasse les 1 000 caracteres de la colonne : rien n'est ecrit ; <paramref name="Motif"/>, celui de la V2.</summary>
    public sealed record TropLong(string Motif) : ReglageEcrit;

    /// <summary>La valeur est ecrite : la ligne <paramref name="Avant"/>, et <paramref name="Apres"/> relue.</summary>
    public sealed record Modifie(ReglageGreffe Avant, ReglageGreffe Apres) : ReglageEcrit
    {
        /// <summary>La ligne du journal des gestes : famille « reglage », « a modifie », la valeur avant et apres. Le code d'un
        /// reglage va jusqu'a 58 caracteres dans RIG_DEV (soc.Journal.IdObjet en tient 40) : il est dans l'objet, ecrit en entier.</summary>
        public TraceGeste Trace => new(ModuleGreffes.FamilleReglage, $"le reglage {Apres.Code}", null, "a modifie",
            new { valeur = Avant.Valeur }, new { valeur = Apres.Valeur });
    }
}
