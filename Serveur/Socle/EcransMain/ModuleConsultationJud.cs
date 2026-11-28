using RigV3.Contrats.EcransMain;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// Les routes propres a « Consultation judiciaire » (PROC_CJUD) apres le lot 20 : le geste « traiter l'enrolement »
/// (EcritureConsultationJud.cs). Sa lecture reste dans ModuleEcransMain (lot 6). Plan « parcours complet », lot 8, 28/11/2026.
/// </summary>
internal sealed class ModuleConsultationJud : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => GesteFaitMain.Publier(routes, RoutesConsultationJud.Traiter, EcritureConsultationJud.Ecran, nameof(EcritureConsultationJud), EcritureConsultationJud.Gestes,
            EcritureConsultationJud.JouerAsync);
}
