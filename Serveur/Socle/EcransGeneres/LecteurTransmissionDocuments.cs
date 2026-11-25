using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Transmission » (PROC_TRANSDOC) : les documents dematerialises recus du tribunal digital et du coffre-fort du greffe,
/// en attente d'integration. Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_TRANSDOC\PROC_TRANSDOC. Le meme ecran sert deux
/// listes selon son parametre TypeDoc (TypeProcDematDoc.cs:46-70) : DOC_TRIDIG, les documents du tribunal digital, et
/// DCOFFRE, ceux deposes au coffre-fort du greffe par un AJMJ. A l'ouverture, LoadDGV (Etapes\OPE_LISTE_FLUX_DOC.cs:624-663)
/// lit « SELECT * FROM DEMANDE WHERE DMND_CODE_PROSS = <TypeDoc> AND DMND_ETAT_DEMANDE = 'Q' » (l. 629-631), puis, par
/// demande, sa fiche de document : DEMAT_TRIBUNAL_DIGITAL_CREFO (DTDCREFO_ID_DMND) pour DOC_TRIDIG, DEMAT_CFEC_AJMJ
/// (CFECAJ_ID_DMND) pour DCOFFRE ; une demande sans fiche n'a pas de ligne (l. 673-674 et 693-694). La fiche est la premiere
/// que rend le SELECT sans ORDER BY de GetDematTridCREFOByDemande / GetDematCfecAjmjByDemande (DematTriDigCREFO.cs:340,
/// DematCfecAjmj.cs:108) : ici, la plus petite cle. Colonnes de la grille V2 (l. 676-704) : recue le (date de creation de la
/// fiche), n° de demande, libelle (DMND_NOM), affaire (DTDCREFO_NUM_AFFAIRE / CFECAJ_NUMERO_AFF, editable), et pour le
/// coffre-fort le type de document et le SIREN. Dans la base V3, ces trois tables sont les tables V2 telles quelles (v2.*).
///
/// FILTRES : code processus DOC_TRIDIG ou DCOFFRE, demande qualifiee (Q), avec sa fiche de document. Ordre : celui de la
/// V2, dont le SELECT n'a pas d'ORDER BY -- les demandes dans l'ordre de leur cle, c'est-a-dire d'arrivee ; ici la date de
/// reception de la fiche, puis la demande.
///
/// GROUPES : « Tribunal digital » pour DOC_TRIDIG, « Coffre-fort numérique » pour DCOFFRE.
///
/// REGLE D'ETAT (le numero d'affaire de la fiche : la seule chose que l'ecran enregistre, SauveNumAffaire l. 586-622) :
///   numero d'affaire vide            -> AFaire « A transmettre » : aucune marche ;
///   numero d'affaire renseigne       -> EnCours « Numero saisi » : numero d'affaire (0). Il peut venir du depot (24 des 25
///                                       documents du coffre-fort en attente dans RIG_DEV, lu le 25/11/2026, l'ont des
///                                       l'arrivee) : la base ne dit pas qui l'a saisi.
/// « Enregistre » (Validee) n'a pas de ligne : enregistrer le numero ne change pas l'etat de la demande, qui reste dans la
/// liste ; une demande integree (son processus _GED la valide) n'est plus qualifiee et sort de la liste V2. Les marches
/// « Visualisation » et « Visualisation judiciaire » ne laissent aucune trace dans la base (la V2 retient les documents
/// ouverts en memoire, _filesAlreadyRead l. 27).
/// </summary>
internal sealed class LecteurTransmissionDocuments : ILecteurEcran
{
    public string Procedure => "PROC_TRANSDOC";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string TribunalDigital = "Tribunal digital", CoffreFort = "Coffre-fort numérique";

    /// <summary>Le code processus des documents du tribunal digital (TypeProcDematDoc.cs:65) ; l'autre est DCOFFRE.</summary>
    private const string CodeTribunal = "DOC_TRIDIG";

    private const string Requete = """
        SELECT TOP (@plafond) d.DMND_ID_DMND, d.DMND_NUM_DEMANDE, d.DMND_NOM, d.DMND_CODE_PROSS,
               COALESCE(t.Recue, c.Recue) AS Recue, COALESCE(t.Affaire, c.Affaire) AS Affaire, c.TypeDocument, c.Siren,
               COUNT(*) OVER () AS Total
        FROM v2.DEMANDE AS d
        OUTER APPLY (SELECT TOP (1) f.DTDCREFO_DATE_CREATION AS Recue, f.DTDCREFO_NUM_AFFAIRE AS Affaire
                     FROM v2.DEMAT_TRIBUNAL_DIGITAL_CREFO AS f
                     WHERE d.DMND_CODE_PROSS = 'DOC_TRIDIG' AND f.DTDCREFO_ID_DMND = d.DMND_ID_DMND
                     ORDER BY f.DTDCREFO_ID_DTDCREFO) AS t
        OUTER APPLY (SELECT TOP (1) f.CFECAJ_DATE_CREATION AS Recue, f.CFECAJ_NUMERO_AFF AS Affaire,
                            f.CFECAJ_TYPE_DOC AS TypeDocument, f.CFECAJ_NUM_SIREN AS Siren
                     FROM v2.DEMAT_CFEC_AJMJ AS f
                     WHERE d.DMND_CODE_PROSS = 'DCOFFRE' AND f.CFECAJ_ID_DMND = d.DMND_ID_DMND
                     ORDER BY f.CFECAJ_ID_CFECAJ) AS c
        WHERE d.DMND_CODE_PROSS IN ('DOC_TRIDIG', 'DCOFFRE') AND d.DMND_ETAT_DEMANDE = 'Q'
          AND COALESCE(t.Recue, c.Recue) IS NOT NULL
        ORDER BY COALESCE(t.Recue, c.Recue), d.DMND_ID_DMND
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var affaire = FormeLigne.Texte(r, "Affaire");
        var (etat, faites) = Etat(affaire);
        var numero = FormeLigne.Texte(r, "DMND_NUM_DEMANDE");
        var libelle = FormeLigne.Texte(r, "DMND_NOM");
        var siren = FormeLigne.Siren(FormeLigne.Texte(r, "Siren"));
        var precisions = FormeLigne.Joindre($"reçu le {FormeLigne.Date(FormeLigne.DateDe(r, "Recue"))}",
                                            FormeLigne.Texte(r, "TypeDocument"),
                                            affaire.Length > 0 ? $"affaire {affaire}" : "numéro d'affaire à saisir",
                                            siren.Length > 0 ? $"SIREN {siren}" : null,
                                            $"demande n° {numero}");
        var groupe = FormeLigne.Texte(r, "DMND_CODE_PROSS") == CodeTribunal ? TribunalDigital : CoffreFort;
        return new LigneLue(libelle.Length > 0 ? libelle : $"Demande n° {numero}", precisions, groupe, etat, faites);
    }

    /// <summary>La regle d'etat (en tete du fichier) : etat du descripteur et marches franchies.</summary>
    internal static (string Etat, int[] Faites) Etat(string affaire)
        => affaire.Length == 0 ? ("AFaire", []) : ("EnCours", [0]);
}
