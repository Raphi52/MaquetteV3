namespace RigV3Desktop.Pont;

/// <summary>Une ligne du catalogue des courriers divers : les colonnes que l'ecran affiche
/// (v2 : OPE_RECHERCHE_COURRIER_DIVERS.cs, colonnes COUJU_*).</summary>
public sealed record CourrierDiversVue(string Code, string Libelle, string TypeEntite, string ParamEdition,
                                       bool AvecDestinataire, bool Actif);

/// <summary>Le catalogue : les types d'entite du filtre et tous les modeles, actifs ou non.</summary>
public sealed record CatalogueCourriersVue(string[] Types, CourrierDiversVue[] Courriers);

/// <summary>
/// Pont de l'ecran PROC_COURRIER_DIVERS (web/src/ecrans/EcranCourriersDivers.tsx) : le
/// CATALOGUE des modeles, ouvert par l'accueil. Il lit la liste que la page du dossier
/// propose (Metier/CourriersDivers.cs) : une seule liste, deux lecteurs.
/// </summary>
public static class EcranCourriersDivers
{
    [JSInvokable("EcranCourriersDivers.Catalogue")]
    public static CatalogueCourriersVue Catalogue()
        => new(CourriersDivers.TypesEntite,
               CourriersDivers.Modeles
                   .Select(m => new CourrierDiversVue(m.Code, m.Libelle, m.TypeEntite, m.ParamEdition,
                                                      m.AvecDestinataire, m.Actif))
                   .ToArray());
}
