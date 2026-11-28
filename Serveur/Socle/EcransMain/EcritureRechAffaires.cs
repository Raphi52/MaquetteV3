using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Recherche d'affaires » (PROC_RECHAFF, ecran fait main web/src/ecrans/EcranRechAffaires.tsx) : le geste RATTACHER UNE DEMANDE A
/// L'AFFAIRE CHOISIE, la seule ecriture de RIG v2 dans ce processus, ecrite dans la base du greffe. Plan « RIG v3 complet, mesure par
/// le banc du parcours complet », lot 8, 28/11/2026. Route : ModuleRechAffaires.cs ; la lecture : LectureAffaires.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RIG\DLL\Processus\PROC_RECHAFF\PROC_RECHAFF.cs, version commitee 4a58b08a6) :
///   - la demande vient du parametre du processus (NumDemandeInput, :20-26) ; sans elle, Valider rend l'affaire choisie sans rien
///     ecrire (:62) -- ce que fait le bouton « Ouvrir » de l'ecran de la V3 ;
///   - refus : la demande est deja dans l'affaire choisie (:40-43, message repris) ; l'affaire choisie est verrouillee par un
///     utilisateur (AFFRE_ID_UTLST_EN_COURS, :44-47 : « ... verrouillée par » son nom, prenom et abrege, Utilisateur.NomPrenomAbrege,
///     RigMetier\Habilitation\Utilisateur.cs:104 ; ici lus sur le compte de l'agent qui porte cet identifiant V2, soc.Compte.IdV2) ;
///   - ecritures : si l'affaire d'origine n'a que cette demande, ses VENTILATION passent a l'affaire choisie (:48-54) ; les
///     ARTICLE_UTILISE de la facture de la demande (Facture.GetFactureByNumDemande, Demande.cs:2599-2606 ; ARTUT_NUMERO_FACTURE,
///     Facture.cs:987-993) aussi (:55-58) ; puis la demande (DMND_ID_AFFRE, :59-60).
/// ECARTS NOMMES : une demande sans facture fait lever RIG v2 (« foreach » sur une facture nulle, :55) -- la V3 la rattache sans
/// article a deplacer ; un numero de demande inconnu ne rattache rien dans RIG v2 (GetDemande nul, :39) -- la V3 le refuse avec son
/// motif, pour ne pas dire « fait » sans ecriture ; « acturellement » (:45) est ecrit « actuellement ».
/// NON REPRISE, nommee : la trace de consultation (HistoriqueConsultationAffaire.TracerConsultation) de la copie de travail de
/// D:\RigApplication n'est pas commitee dans RIG v2.
/// TRACE : famille « affaire », « a modifie ».
/// </summary>
internal static class EcritureRechAffaires
{
    /// <summary>Le nom de l'ecran au journal des gestes.</summary>
    public const string Ecran = "Recherche d'affaires";

    /// <summary>Les gestes ecrits : tout autre type est refuse par la route (GesteFaitMain, rien d'ecrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Enregistrer];

    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids is not [var texte] || !int.TryParse(texte, NumberStyles.None, CultureInfo.InvariantCulture, out var cible))
            return IssueGeste.Refuser("Choisissez une affaire de la liste.");
        var numero = Saisi(geste, RattachementDemande.Section, RattachementDemande.Demande);
        if (numero.Length == 0) return IssueGeste.Refuser(Obligatoire(RattachementDemande.Demande));

        int? verrou;
        await using (var r = await geste.LireAsync("SELECT AFFRE_ID_UTLST_EN_COURS FROM v2.AFFAIRE WHERE AFFRE_ID_AFFRE = @a", ct, new SqlParameter("@a", cible)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser("Cette affaire n'est plus dans la base du greffe : relisez la liste.");
            verrou = r.IsDBNull(0) ? null : Convert.ToInt32(r[0], CultureInfo.InvariantCulture);
        }
        int idDemande;
        int? origine;
        await using (var r = await geste.LireAsync("SELECT TOP (1) DMND_ID_DMND, DMND_ID_AFFRE FROM v2.DEMANDE WHERE DMND_NUM_DEMANDE = @n ORDER BY DMND_ID_DMND",
                         ct, new SqlParameter("@n", numero)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Aucune demande ne porte le numéro « {numero} » dans la base du greffe : rien n'est rattaché.");
            idDemande = r.GetInt32(0);
            origine = r.IsDBNull(1) ? null : Convert.ToInt32(r[1], CultureInfo.InvariantCulture);
        }
        if (origine == cible) return IssueGeste.Refuser($"La demande {numero} est déjà dans l'affaire sélectionnée");
        if (verrou is { } idV2)
        {
            var nom = $"l'utilisateur n° {idV2}";
            await using (var r = await geste.LireAsync("SELECT TOP (1) Nom, Prenom, Abrege FROM soc.Compte WHERE IdV2 = @u ORDER BY Id", ct, new SqlParameter("@u", idV2)))
                if (await r.ReadAsync(ct))
                    nom = $"{FormeLigne.Texte(r, "Nom").Trim()} {FormeLigne.Texte(r, "Prenom").Trim()} ({FormeLigne.Texte(r, "Abrege").Trim()})";
            return IssueGeste.Refuser($"L'affaire sélectionnée est actuellement verrouillée par {nom}");
        }

        var ventilations = 0;
        if (origine is { } source)
        {
            int demandesSource;
            await using (var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.DEMANDE WHERE DMND_ID_AFFRE = @s", ct, new SqlParameter("@s", source)))
            {
                await r.ReadAsync(ct);
                demandesSource = r.GetInt32(0);
            }
            if (demandesSource == 1)
                ventilations = await geste.EcrireAsync("UPDATE v2.VENTILATION SET VENTI_ID_AFFRE = @c WHERE VENTI_ID_AFFRE = @s", ct,
                    new SqlParameter("@c", cible), new SqlParameter("@s", source));
        }
        var articles = await geste.EcrireAsync("""
            UPDATE v2.ARTICLE_UTILISE SET ARTUT_ID_AFFRE = @c
            WHERE ARTUT_NUMERO_FACTURE IN (SELECT FACTU_NUMERO_FACTURE FROM v2.FACTURE WHERE FACTU_NUM_DEMANDE = @n)
            """, ct, new SqlParameter("@c", cible), new SqlParameter("@n", numero));
        await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_ID_AFFRE = @c WHERE DMND_ID_DMND = @d", ct, new SqlParameter("@c", cible), new SqlParameter("@d", idDemande));

        return IssueGeste.Ecrit(
            $"Demande {numero} rattachée à l'affaire {cible} ({ventilations} ventilation{(ventilations > 1 ? "s" : "")} et {articles} article{(articles > 1 ? "s" : "")} déplacé{(ventilations + articles > 1 ? "s" : "")}).",
            [cible.ToString(CultureInfo.InvariantCulture)],
            new TraceGeste("affaire", $"la demande {numero}", idDemande.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { Affaire = origine }, new { Affaire = cible, Ventilations = ventilations, Articles = articles }));
    }
}
