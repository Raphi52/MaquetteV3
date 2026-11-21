using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Identite;

/// <summary>
/// LES GREFFES QUE L'AGENT PEUT OUVRIR DEPUIS SON POSTE (lot 10 du plan de reconstruction, 2026-11-21).
///
/// La fenetre de connexion de la V2 (RigFormLogin.cs, _ChargerListeCodeGreffe :158-241) parcourt les greffes
/// declares pour le poste et ne propose que ceux dont la base est en ligne et ou le compte Windows de l'agent
/// existe sans etre desactive (:183-185) ; le jugement complet (role, service) vient apres le choix
/// (CheckUser). Ici, le poste envoie ses codes (Pont/SessionAgent.cs) et le serveur rend, pour chacun, ce
/// qu'il en est : il est seul a atteindre les bases des greffes (catalogue commun, lot 4). Rien n'est ecrit.
/// Une session Windows suffit (401 sans elle) : l'agent n'est encore admis nulle part.
/// </summary>
internal sealed class ModuleOuverture : IModuleServeur
{
    /// <summary>La colonne soc.Greffe.CodeGreffe : un code plus long n'y est pas.</summary>
    private const int LongueurCode = 20;

    public void Brancher(IEndpointRouteBuilder routes)
        => routes.MapGet(RoutesOuverture.GreffesDuPoste, GreffesDuPoste).RequireAuthorization();

    private static async Task<IResult> GreffesDuPoste(HttpContext contexte, CatalogueGreffes catalogue, CancellationToken ct)
    {
        // RequireAuthorization garantit une session ; sans compte lisible, rien n'est cherche.
        if (ServiceIdentite.CompteDe(contexte.User) is not { } compte) return Results.Unauthorized();
        var codes = Codes(contexte.Request.Query[RoutesOuverture.ParametreCodes].ToString());
        if (codes.Count == 0)
            return Results.BadRequest(new RefusGreffe($"Aucun code greffe demandé (paramètre {RoutesOuverture.ParametreCodes}=3801,8002)."));
        if (codes.Count > RoutesOuverture.CodesAuPlus)
            return Results.BadRequest(new RefusGreffe($"{codes.Count} codes greffe demandés : {RoutesOuverture.CodesAuPlus} au plus."));

        var fiches = (await catalogue.ListerAsync(ct)).ToDictionary(f => f.Code, StringComparer.OrdinalIgnoreCase);
        // Une base par greffe, ouvertes ENSEMBLE : un greffe muet (10 s de delai de connexion) ne fait pas
        // attendre les autres au-dela du delai du client de l'ecran (30 s).
        var greffes = await Task.WhenAll(codes.Select(code => JugerAsync(code, fiches.GetValueOrDefault(code), compte, catalogue, ct)));
        return Results.Ok(new ChoixDuGreffe(compte, greffes));
    }

    private static async Task<GreffeDuPoste> JugerAsync(string code, FicheGreffe? fiche, string compte,
                                                        CatalogueGreffes catalogue, CancellationToken ct)
    {
        if (fiche is null || code.Length > LongueurCode) return new(code, null, EtatsGreffeDuPoste.HorsCatalogue);
        ServiceIdentite.LigneCompte? ligne;
        try
        {
            await using var cn = await catalogue.OuvrirBaseV3Async(fiche.Code, ct);
            ligne = await ServiceIdentite.LireCompteAsync(cn, compte, ct);
        }
        catch (GreffeInconnu) { return new(code, null, EtatsGreffeDuPoste.HorsCatalogue); }   // retire entre-temps
        catch (BaseGreffeInjoignable) { return new(code, null, EtatsGreffeDuPoste.BaseInjoignable); }
        return ligne switch
        {
            null => new(fiche.Code, null, EtatsGreffeDuPoste.CompteAbsent),
            { Desactive: true } => new(fiche.Code, null, EtatsGreffeDuPoste.CompteDesactive),
            _ => new(fiche.Code, fiche.Nom, EtatsGreffeDuPoste.Ouvrable),
        };
    }

    /// <summary>Les codes de la demande, dans leur ordre, sans doublon ni vide (« 3801, 8002;3801 » -> 3801, 8002).</summary>
    internal static List<string> Codes(string? brut)
        => [.. (brut ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                           .Distinct(StringComparer.OrdinalIgnoreCase)];
}
