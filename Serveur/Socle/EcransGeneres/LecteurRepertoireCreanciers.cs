using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Representation » (PROC_CREANREP) : le REPERTOIRE DES CREANCIERS (v2.CREANCIER_REPERTO), une table de reference. Mode d'emploi :
/// LecteurEcran.cs ; les gestes : EcrivainRepertoireCreanciers.cs. Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026.
///
/// LE ROLE REEL (le code fait foi, la fiche proc_creanrep.md n'a qu'un « contexte métier probable ») : D:\RigApplication\Source\RIG\
/// DLL\Processus\Inscriptions\PROC_CREANREP -- OPE_SELECTION_CREANREP choisit un creancier repertorie, desactives compris
/// (« AvecLesDesactive=1 », OPE_SELECTION_CREANREP.cs:11), ou en cree un ; OPE_CREANREP saisit ses champs (OPE_CREANREP.cs:51-62 :
/// civilite, SIREN, code abrege, patronyme, nom d'usage, prenoms, forme juridique, RCS (ville), adresse, desactive) et DoSauve
/// l'enregistre (:91-95). Ce n'est pas la designation d'un representant des creanciers : la forme est un Referentiel.
/// LA LISTE : tous les creanciers repertories, desactives compris, par code abrege. L'adresse est la fiche reprise rec.Adresse
/// (ADRESSE_BASE, Serveur/Recopie/FichesV3.cs) : sa voie, son code postal, sa localite.
/// GROUPES : « Personnes morales » (civilite PM, OPE_CREANREP.cs:_RefreshAffichage) / « Personnes physiques ». ETAT : CRRPR_SUPPRIME
/// -> EnCours « Désactivé » (il reste modifiable : la V2 le choisit et le reactive, OPE_SELECTION_CREANREP.cs:11) ; sinon AFaire « Actif ».
/// CELLULES : dans l'ordre des Colonnes du descripteur (Facturer.cs), aux libelles du formulaire de la V2 (OPE_CREANREP.Designer.cs).
/// </summary>
internal sealed class LecteurRepertoireCreanciers : ILecteurEcran
{
    public string Procedure => "PROC_CREANREP";

    internal const string Morales = "Personnes morales", Physiques = "Personnes physiques";

    private const string Requete = """
        SELECT TOP (@plafond) c.CRRPR_ID_CRRPR, c.CRRPR_ABREGE, c.CRRPR_CIVILITE, c.CRRPR_NOM, c.CRRPR_PRENOMS, c.CRRPR_NOM_USAGE, c.CRRPR_NUM_SIREN,
               c.CRRPR_RCS_VILLE, c.CRRPR_SUPPRIME, a.NomVoie, a.CodePostal, a.Localite, COUNT(*) OVER () AS Total
        FROM v2.CREANCIER_REPERTO AS c
        LEFT JOIN rec.Adresse AS a ON a.Id = c.CRRPR_ID_ADRBS
        ORDER BY c.CRRPR_ABREGE, c.CRRPR_ID_CRRPR
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        string T(string c) => FormeLigne.Texte(r, c);
        var desactive = CriteresConsultation.Vrai(r, "CRRPR_SUPPRIME");
        var morale = T("CRRPR_CIVILITE") == "PM";
        var nom = T("CRRPR_NOM").Length > 0 ? T("CRRPR_NOM") : T("CRRPR_ABREGE");
        return new LigneLue(nom, FormeLigne.Joindre(T("CRRPR_ABREGE"), FormeLigne.Siren(T("CRRPR_NUM_SIREN")), T("Localite"), desactive ? "désactivé" : null),
            morale ? Morales : Physiques, desactive ? "EnCours" : "AFaire", [])
        {
            Id = T("CRRPR_ID_CRRPR"),
            Valeurs = [T("CRRPR_ABREGE"), T("CRRPR_CIVILITE"), T("CRRPR_NOM"), T("CRRPR_PRENOMS"), T("CRRPR_NOM_USAGE"), T("CRRPR_NUM_SIREN"),
                       T("CRRPR_RCS_VILLE"), T("NomVoie"), T("CodePostal"), T("Localite"), FormeLigne.Case(desactive)],
        };
    }
}
