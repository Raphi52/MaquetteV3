using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.Greffes;

/// <summary>
/// L'ECRITURE D'UN REGLAGE DU GREFFE (soc.Reglage, la reprise de PARAMETRAGE : RepriseReglages.cs), la SEULE de la V3. Deux
/// chemins l'appellent : la route PUT <see cref="RoutesGreffes.Reglage"/> (ModuleGreffes, ecran « Reglages du greffe ») et le geste
/// d'un ecran genere qui ecrit SON reglage comme la V2 (<see cref="GesteEnCours.ModifierReglageAsync"/> : « Codes chefs IP »,
/// IP_CODE_CHEF, ETP_PARAM_IP.DoSauveAfterOperation). Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026 : extraite de la route,
/// sans seconde copie, pour que le lot 15 s'en serve aussi.
///
/// CE QU'ELLE FAIT, comme Parametrage.UpdateValeurParametrage (Parametrage.cs:103-121), DANS la transaction de l'appelant :
///   1. le droit de modifier le greffe (<see cref="ServiceDroits.PeutModifierLeGreffe"/>, OPE_EDITER_GREFFE.cs:30-38) : sinon rien
///      n'est lu ni ecrit ;
///   2. le reglage doit EXISTER : un code absent ne se cree pas (ni INSERT, ni DELETE : la valeur d'un code existant seulement) ;
///   3. Valeur, AuteurModif (le NOM de l'agent, <see cref="AuteurReglage"/>, coupe a 50 comme PARAM_AUTEUR_MODIF) et DateModif ;
///   4. relit la ligne ecrite.
/// Elle n'ecrit PAS le journal des gestes : elle rend sa ligne (<see cref="ReglageEcrit.Modifie.Trace"/>). La route l'ecrit
/// (JournalGestes.EcrireAsync, avec l'ecran de la saisie) ; un geste la rend a son socle, qui l'ecrit au nom de l'agent
/// (EcrivainEcran.cs, point 5) -- une seule ligne par modification dans les deux cas.
/// </summary>
internal static class EcritureReglage
{
    /// <summary>Modifie la valeur du reglage <paramref name="code"/> du greffe ouvert sur <paramref name="cn"/>, dans
    /// <paramref name="tx"/> ; l'appelant valide ou annule. <paramref name="auteur"/> : <see cref="AuteurReglage"/>.</summary>
    public static async Task<ReglageEcrit> ModifierAsync(SqlConnection cn, SqlTransaction tx, Appelant moi, string auteur, string code,
                                                       string? valeur, CancellationToken ct)
    {
        if (!ServiceDroits.PeutModifierLeGreffe(moi)) return new ReglageEcrit.SansLeDroit(ModuleGreffes.MotifSansLeDroit(moi));
        if ((await ModuleGreffes.LireReglagesAsync(cn, tx, code, ct)).FirstOrDefault() is not { } avant) return new ReglageEcrit.Absent();
        await using (var cmd = new SqlCommand(
            "UPDATE soc.Reglage SET Valeur = @v, AuteurModif = @a, DateModif = GETDATE() WHERE Code = @c;", cn, tx))
        {
            cmd.Parameters.Add(new SqlParameter("@v", System.Data.SqlDbType.VarChar, 1000) { Value = (object?)valeur ?? DBNull.Value });
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

    /// <summary>L'appelant n'a pas le droit de modifier le greffe : rien n'est lu ni ecrit ; <paramref name="Motif"/> le dit.</summary>
    public sealed record SansLeDroit(string Motif) : ReglageEcrit;

    /// <summary>Le reglage n'existe pas dans la base du greffe : rien n'est ecrit (un reglage ne se cree pas).</summary>
    public sealed record Absent : ReglageEcrit;

    /// <summary>La valeur est ecrite : la ligne <paramref name="Avant"/>, et <paramref name="Apres"/> relue.</summary>
    public sealed record Modifie(ReglageGreffe Avant, ReglageGreffe Apres) : ReglageEcrit
    {
        /// <summary>La ligne du journal des gestes : famille « reglage », « a modifie », la valeur avant et apres. Le code d'un
        /// reglage va jusqu'a 58 caracteres dans RIG_DEV (soc.Journal.IdObjet en tient 40) : il est dans l'objet, ecrit en entier.</summary>
        public TraceGeste Trace => new(ModuleGreffes.FamilleReglage, $"le reglage {Apres.Code}", null, "a modifie",
            new { valeur = Avant.Valeur }, new { valeur = Apres.Valeur });
    }
}
