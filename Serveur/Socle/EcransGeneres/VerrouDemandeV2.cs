using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// LE VERROU DE RIG v2 sur un objet (instance, procedure collective, mesure... : tout ISqlLockableByDemande), lu dans la
/// base V3 du greffe (tables v2.DEMANDE, v2.DOSSIER_DEMANDE, v2.ETAT_DEMANDE recopiees). Plan « RIG v3 100 % fonctionnel »,
/// lot 2, 26/11/2026 : un lecteur de fiche (point 8 de LecteurEcran.cs) ouvre en LECTURE SEULE un objet verrouille, et
/// son ecrivain REFUSE de l'ecrire, au message de RIG v2.
///
/// SOURCE (ancien RIG) : Outils\DLL\RigMetier\Demande\ISqlLockable.cs:14-19 -- l'objet est verrouille par une AUTRE demande
/// liee par DOSSIER_DEMANDE (code sequenceur de l'objet, valeur = son identifiant) qui est en cours ou en attente,
/// interrompue ou en reclamation : Demande.GetOldLock, Demande.cs:1290-1316
///   « where dssdm_valeur_sequenceur = &lt;id&gt; and dssdm_code_sequenceur = &lt;sequenceur&gt;
///     and (dmnd_en_cours = 1 or dmnd_etat_demande in ('W','I','N')) ».
/// La V3 n'a pas de demande en cours a exclure (aucune n'est ouverte par la V3) : toute demande qui repond verrouille.
/// Plusieurs repondent : la V2 prend la premiere que rend le serveur (« new Demande(sql) », sans ORDER BY) ; la V3 prend la
/// plus ancienne (DMND_ID_DMND), pour un message stable.
///
/// LE MESSAGE : celui que l'agent lit dans RIG v2 a la selection, OPE_SELECT_ENTITE_JUDICIAIRE.cs:122
/// « &lt;Type&gt; bloquée &lt;msg&gt; », msg = Demande.cs:1315 « (¤&lt;n° demande&gt;¤), demande [en ]"&lt;etat&gt;" : &lt;code processus&gt; par
/// &lt;utilisateur&gt; », etat = « En cours » si DMND_EN_COURS, sinon le libelle ecran de ETAT_DEMANDE ; « en » devant une
/// reclamation (N) ou un refus (O) (Demande.cs:1309-1311). Les ¤ marquent dans RIG v2 un numero cliquable : la V3 les retire.
/// </summary>
public static class VerrouDemandeV2
{
    /// <summary>Le code sequenceur d'une instance judiciaire (Instance.cs:5364, « 2J »).</summary>
    public const string SequenceurInstance = "2J";

    private const string Requete = """
        SELECT TOP (1) d.DMND_NUM_DEMANDE, d.DMND_EN_COURS, d.DMND_ETAT_DEMANDE, d.DMND_CODE_PROSS, d.DMND_NOM_UTILISATEUR,
               e.R_ETATDEMAND_LIBELLE_ECRAN
        FROM v2.DEMANDE AS d
        JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = d.DMND_ID_DMND
        LEFT JOIN v2.ETAT_DEMANDE AS e ON e.R_ETATDEMAND_VALEUR = d.DMND_ETAT_DEMANDE
        WHERE dd.DSSDM_VALEUR_SEQUENCEUR = @valeur AND dd.DSSDM_CODE_SEQUENCEUR = @sequenceur
          AND (d.DMND_EN_COURS = 1 OR d.DMND_ETAT_DEMANDE IN ('W', 'I', 'N'))
        ORDER BY d.DMND_ID_DMND
        """;

    /// <summary>
    /// Le motif du verrou de l'objet <paramref name="valeur"/> (son identifiant) de sequenceur <paramref name="sequenceur"/>,
    /// au message de RIG v2 (« Instance bloquée (26000000123), demande "En cours" : MJUD par DUPONT ») ; null s'il est libre.
    /// <paramref name="transaction"/> : celle du geste qui va l'ecrire (le verrou se relit DANS la transaction).
    /// </summary>
    public static async Task<string?> MotifAsync(SqlConnection baseGreffe, SqlTransaction? transaction, string type, string sequenceur,
                                                 string valeur, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(transaction is null ? Requete : Requete.Replace("FROM v2.DEMANDE AS d", "FROM v2.DEMANDE AS d WITH (UPDLOCK, HOLDLOCK)"),
                                             baseGreffe, transaction);
        cmd.Parameters.AddWithValue("@valeur", valeur);
        cmd.Parameters.AddWithValue("@sequenceur", sequenceur);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var etatCode = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE");
        var enCours = r["DMND_EN_COURS"] is true;   // NOT NULL dans RIG_DEV ; un NULL vaut « pas en cours », comme le WHERE
        var etat = enCours ? "En cours" : FormeLigne.Texte(r, "R_ETATDEMAND_LIBELLE_ECRAN");
        var en = !enCours && etatCode is "N" or "O" ? "en " : "";
        return $"{type} bloquée ({FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}), demande {en}\"{etat}\" : " +
               $"{FormeLigne.Texte(r, "DMND_CODE_PROSS")} par {FormeLigne.Texte(r, "DMND_NOM_UTILISATEUR")}";
    }
}
