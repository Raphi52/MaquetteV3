namespace RigV3.Serveur;

/// <summary>
/// Un MODULE du serveur : les routes d'un sujet (une story ou un petit groupe de
/// stories), dans UN fichier. Toute classe non abstraite du serveur qui
/// implemente cette interface est trouvee au demarrage (<see cref="ServeurRig.Decouvrir"/>) :
/// il n'y a aucune liste a completer.
/// Exemple : <c>Sante/ModuleSante.cs</c>. Il lui faut un constructeur sans parametre ;
/// ce dont il a besoin (la base...) arrive dans les parametres de ses routes.
/// </summary>
public interface IModuleServeur
{
    /// <summary>Inscrit les services propres au module (facultatif). Appele avant le demarrage.</summary>
    void Inscrire(IServiceCollection services, IConfiguration configuration) { }

    /// <summary>Publie les routes du module. Deux modules qui publient la meme route
    /// (meme adresse, meme verbe) arretent le demarrage en se nommant.</summary>
    void Brancher(IEndpointRouteBuilder routes);
}
