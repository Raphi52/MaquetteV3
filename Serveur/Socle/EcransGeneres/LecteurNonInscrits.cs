using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Non-inscrits » (PROC_CNI, forme Saisie) : les créations de non-inscrits du greffe, sous le formulaire de création. Mode d'emploi :
/// LecteurEcran.cs ; le geste : EcrivainNonInscrits.cs. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026.
///
/// SOURCE (ancien RIG) : PROC_CNI (Source\RIG\DLL\Processus\PROC_CNI\PROC_CNI.cs ; l'operation Source\RIG\DLL\EtapeRCS\
/// Rig_Etape_Dossier_Rcs\ETP_CNI\OPE_CNI.cs). RIG v2 n'a pas de liste : PROC_CNI est un formulaire de creation. Comme les autres
/// saisies (lot 5, LecteurDepotsActes.cs), une ligne est une DEMANDE du processus CNI, au nom que la V2 lui donne a la validation
/// (OPE_CNI.cs:173-176 : « civilité patronyme prénoms » pour une personne physique, la denomination pour une personne morale). La
/// demande ne porte pas d'identifiant du non-inscrit (OPE_CNI ne pose pas DMND_ID_AUTRE) : la ligne dit ce que la demande dit.
/// GROUPES : « Personne physique » si le nom de la demande commence par une civilite de personne physique de la table CIVILITE
/// (R_CIVILITE_TOUJOURS_PP, OPE_CNI.cs:47-52, la discriminante PP/PM de la V2 ; son abrege d'edition ouvre le nom, l. 173) ; sinon
/// « Personne morale ».
/// REGLE D'ETAT ET MARCHES (rangs du descripteur : 0 Type et identité, 2 Adresse obligatoires ; 1 Naissance) : demande validee (V) ->
/// « Créé », marches 0 et 2 (la V2 ne valide pas sans type, civilite ni adresse : OPE_CNI.Designer.cs:123,178,198) ; sinon la saisie a
/// ete interrompue avant DoSauve (rien n'est ecrit par la V2) -> « En saisie », marche 0. ORDRE : la plus recente d'abord.
/// </summary>
// fix-ok: mesure le 27/11/2026 -- compiler.ps1 rendait « error CS0103: Le nom 'LectureV2' n'existe pas » (using
// RigV3.Serveur.Socle.Greffes absent) ; l'autre retouche retire une colonne lue mais jamais utilisee. compiler.ps1 = 0 ensuite.
internal sealed class LecteurNonInscrits : ILecteurEcran
{
    public string Procedure => "PROC_CNI";

    internal const string PersonnePhysique = "Personne physique", PersonneMorale = "Personne morale";

    private const string Requete = """
        SELECT TOP (@plafond) d.DMND_ID_DMND, d.DMND_NUM_DEMANDE, d.DMND_NOM, d.DMND_ETAT_DEMANDE, d.DMND_DATE_CREATION, COUNT(*) OVER () AS Total
        FROM v2.DEMANDE AS d
        WHERE d.DMND_CODE_PROSS = 'CNI'
        ORDER BY d.DMND_DATE_CREATION DESC, d.DMND_ID_DMND DESC
        """;

    public async Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        // Les abreges d'edition des civilites TOUJOURS PP (la table CIVILITE de la base du greffe), pour classer chaque demande.
        var abregesPp = new List<string>();
        await using (var t = await LectureV2.LireAsync(baseGreffe, "SELECT OBJECT_ID(N'v2.CIVILITE')", ct))
            if (await t.ReadAsync(ct) && !t.IsDBNull(0))
            {
                await t.CloseAsync();
                await using var c = await LectureV2.LireAsync(baseGreffe,
                    "SELECT DISTINCT RTRIM(R_CIVILITE_ABREGE_EDIT) FROM v2.CIVILITE WHERE R_CIVILITE_TOUJOURS_PP = 1 AND R_CIVILITE_ABREGE_EDIT IS NOT NULL", ct);
                while (await c.ReadAsync(ct)) if (c.GetString(0).Length > 0) abregesPp.Add(c.GetString(0));
            }
        return await LectureEcran.LireAsync(baseGreffe, Requete, r => Ligne(r, abregesPp), ct, new SqlParameter("@plafond", plafond));
    }

    private static LigneLue Ligne(SqlDataReader r, List<string> abregesPp)
    {
        var nom = FormeLigne.Texte(r, "DMND_NOM");
        var numero = FormeLigne.Texte(r, "DMND_NUM_DEMANDE");
        var validee = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE") == "V";
        var pp = abregesPp.Any(a => nom.StartsWith(a + " ", StringComparison.Ordinal));
        var creee = r["DMND_DATE_CREATION"] is DateTime d ? d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) : null;
        return new LigneLue(nom.Length > 0 ? nom : $"Demande {numero}",
            FormeLigne.Joindre($"demande {numero}", creee is null ? null : $"du {creee}", validee ? "non-inscrit créé" : "saisie interrompue"),
            pp ? PersonnePhysique : PersonneMorale, validee ? "Validee" : "EnCours", validee ? [0, 2] : [0])
        {
            Id = FormeLigne.Texte(r, "DMND_ID_DMND"),
        };
    }
}
