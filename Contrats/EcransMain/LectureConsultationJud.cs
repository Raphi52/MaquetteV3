namespace RigV3.Contrats.EcransMain;

// « Consultation judiciaire » (PROC_CJUD, mode CJUDINFOG ; web/src/ecrans/EcranConsultationJud.tsx), ecran fait main lu dans
// la base du greffe (plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026 ; marche du lot 17). Le lecteur :
// Serveur/Socle/EcransMain/LectureConsultationJud.cs.

/// <summary>Les adresses de la lecture : GET, les enrolements RPJC recus (<see cref="EnrolementsLus"/>) ; GET, le XML d'UN
/// enrolement (<see cref="FluxEnrolementLu"/>), lu a la demande comme « Visualiser le XML » de la V2.</summary>
public static class RoutesConsultationJud
{
    public const string Enrolements = "/ecrans-main/{greffe}/consultation-jud";

    public const string Xml = "/ecrans-main/{greffe}/consultation-jud/{id}/xml";

    /// <summary>L'adresse du XML de l'enrolement <paramref name="id"/> du greffe <paramref name="greffe"/>.</summary>
    public static string XmlPour(string greffe, int id) =>
        RoutesEcransMain.Pour(Xml, greffe).Replace("{id}", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>
/// Un enrolement dematerialise recu par le RPVA (DEMAT_RPJC_ENR) : sa reference de demande, le role et le nom courant de
/// l'instance creee a son acceptation (vides avant), l'etat du flux (libelle de eDematEtat), le traitement du greffe
/// (« Accepté », « Refusé », ou « En attente » tant que la V2 n'a rien pose), le type et le motif de rejet, ses dates et le
/// numero de la demande RIG qui le porte.
/// </summary>
public sealed record EnrolementLu(int Id, string Reference, string Role, string Denomination, string EtatDemat, string EtatTraitement,
                                  string TypeRejet, DateTime? Creation, DateTime? Traitement, string MotifRejet, string Demande);

/// <summary>Les enrolements lus (au plus le plafond) et leur total.</summary>
public sealed record EnrolementsLus(EnrolementLu[] Lignes, int Total);

/// <summary>Le flux XML d'un enrolement (RPJCE_FLUX) : <paramref name="Existe"/> faux si l'enrolement n'est plus dans la base ;
/// <paramref name="Xml"/> null s'il n'a pas de flux.</summary>
public sealed record FluxEnrolementLu(int Id, bool Existe, string? Xml);
