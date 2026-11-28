using RigV3.Contrats.Socle;
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Certificat NF525 de la V3 » (PROC_CERTIFICAT, Pont/EcranCertificat.cs) : écran NEUF de la V3, sans équivalent RIG v2 (son certificat
/// circule en pièce jointe). Son seul geste écrit : Amitel ENREGISTRE le certificat que l'organisme accrédité lui a délivré (SOC-019 T03 ;
/// PUT RoutesCertification.Enregistrer, Serveur/Socle/Certification/ModuleCertification.cs : soc.CertificatLogiciel de la base commune et
/// journal des gestes), joué par le pont (EcranCertificat.EnregistrerAsync) puis relu par le pont pour le greffe du banc (SOC-019 CA04) :
/// numéro, organisme, état « valide ». Jumeau refusé : le même certificat sans accréditation (« seul le certificat d'un organisme
/// accrédité s'enregistre », RegleCertificat.RefusEnregistrement) ; le certificat relu reste le premier.
/// L'enregistrement est réservé à l'exploitation d'Amitel (agent marqué Amitel dans la base commune) : la sonde le joue au nom de
/// parcours.lecteur, que le banc crée Amitel dans la base commune ET dans celle du greffe (Program.cs, AgentLecteur).
/// Lot 13 du plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
/// </summary>
public sealed class SondeCertificat : ISondeEcran
{
    private const string Amitel = "parcours.lecteur";

    public string Code => EcranCertificat.Code;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var client = c.ClientDe(Amitel);
        var jour = DateOnly.FromDateTime(c.Jour);
        var majeure = string.Join('.', EcranSignalement.Version.Split('.').Take(2));
        var certificat = new Certificat(RoutesCertification.LogicielV3, "Organisme de la sonde du parcours", "COFRAC 5-0013",
                                        $"SONDE-13-{jour:yyyyMMdd}", "NF525", majeure, jour.AddDays(-1), jour.AddYears(1));

        var vue = await EcranCertificat.EnregistrerAsync(client, c.Greffe, certificat);
        if (vue.Mode != "serveur") return new(false, false, $"Enregistrer le certificat : {vue.Mode} « {vue.Message} »");

        var jumeau = await EcranCertificat.EnregistrerAsync(client, c.Greffe, certificat with { Numero = "JUMEAU-13", Accreditation = " " });
        var relu = await EcranCertificat.OuvrirAsync(c.Client, c.Greffe);
        var lu = relu.Etat?.Certificat;
        return relu.Mode == "serveur" && lu == certificat && relu.Etat!.Etat == EtatsCertificat.Valide && jumeau.Mode == "refuse"
            ? new(true, true, $"Enregistrer le certificat fait, relu par le pont pour le greffe {c.Greffe} : n°{lu.Numero}, {lu.Organisme} ({lu.Accreditation}), " +
                              $"version {lu.VersionCouverte}, état « {relu.Etat.Etat} » ; sans accréditation : refusé « {jumeau.Message} »")
            : new(true, false, $"Enregistrer le certificat rendu fait, mais relu : {relu.Mode}, n°{lu?.Numero ?? "aucun"}, état « {relu.Etat?.Etat} » ; " +
                               $"jumeau {jumeau.Mode} « {jumeau.Message} »");
    }
}
