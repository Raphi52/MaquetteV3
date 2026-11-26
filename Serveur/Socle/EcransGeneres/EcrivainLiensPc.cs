using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurLiensPc;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Liens entre PC » (PROC_XLIEN) : VALIDER rattache les instances cochees a une PC ou a une prevention, ou les en detache.
/// Mode d'emploi : EcrivainEcran.cs ; la liste et la fiche : LecteurLiensPc.cs. Plan « RIG v3 100 % fonctionnel », lot 4,
/// 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_XLIENPC / PROC_XLIENPR (proc_xlien.md, Detail OPE). Le lien est UNE colonne de l'instance :
/// INSTN_ID_PRCLL (PC) ou INSTN_ID_PREVE (prevention). Rattacher = le numero metier saisi designe exactement UNE entite
/// (« ProcedureCollective=* », OPE_XLIENPC_INSTANCE.Designer.cs:117 ; « Prevention=* », OPE_XLIENPR_INSTANCE.Designer.cs:157 ;
/// propriete ProcColl, OPE_XLIENPC_INSTANCE.cs:21-28), qui remplace le lien (_ChangementPC, :122-130). Detacher = la colonne a
/// NULL, seulement si elle est posee (SupprimerLienPc, :140-149). A la validation, SEULE l'instance est sauvee (Instance.Sauve,
/// OPE_XLIENPC_INSTANCE.cs:62-67) : la PC et la prevention ne sont jamais ecrites. Une instance a la fois dans RIG v2 : ici,
/// chaque instance cochee recoit le meme geste, une par une, avec ses controles, en une transaction (un refus n'ecrit rien).
///
/// CONTROLES DE RIG v2, dans l'ordre, avant toute ecriture :
///   1. l'instance est dans la recherche de son processus (types de numero de role, LecteurLiensPc) ;
///   2. elle n'est pas tenue par une autre demande (instance.Lock, PROC_XLIENPC.cs:51 ; message de VerrouDemandeV2) ;
///   3. elle ne porte pas le lien de l'AUTRE type (PROC_XLIENPC.cs:46-48, PROC_XLIENPR.cs:41-43), au message de RIG v2 ;
///   4. le numero designe une seule PC (ou prevention). RIG v2 ne rattache rien sinon, sans message (ProcColl null) : la V3 le
///      dit, sans rien ecrire.
/// Refus propres a la V3, faute d'objet : rien de saisi (ni numero ni case), le numero ET la case, un champ de l'autre type,
/// detacher une instance sans lien, rattacher a l'entite deja liee.
///
/// CE QUI S'ECRIT : v2.INSTANCE.INSTN_ID_PRCLL (ou INSTN_ID_PREVE), puis les effets de Instance.Sauve (SauveInstanceV2.cs).
/// Trace : famille « affaire », « a modifie », le numero lie avant et apres.
/// NON REPRIS (nomme) : la demande XLIENPC / XLIENPR de RIG v2 (DMND_NUM_INDICATIF, DMND_LES_NUMEROS, PROC_XLIENPC.cs:53-55),
/// une trace sans facture (MetierFacture = false) que le journal des gestes remplace ; la fermeture apres validation quand un
/// autre processus passe IdInstance (RETAUD, PROC_XLIENPC.cs:29-35, :79-82).
/// </summary>
internal sealed class EcrivainLiensPc : IEcrivainEcran
{
    public string Procedure => "PROC_XLIEN";
    public string Ecran => "Liens entre PC";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Confirmer];

    // Le numero metier d'une PC ou d'une prevention, sans ses blancs (comme le controle numero metier de RIG v2).
    private const string EntiteParNumero = """
        SELECT PRCLL_ID_PRCLL AS Id, PRCLL_NUM_PC AS Numero FROM v2.PROCEDURE_COLLECTIVE WHERE REPLACE(PRCLL_NUM_PC, ' ', '') = @numero
        """;

    private const string PreventionParNumero = """
        SELECT PREVE_ID_PREVE AS Id, PREVE_NUM_PRV AS Numero FROM v2.PREVENTION WHERE REPLACE(PREVE_NUM_PRV, ' ', '') = @numero
        """;

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids.Length == 0) return IssueGeste.Refuser("Aucune instance cochée : rien n'est rattaché.");
        var faites = new List<(InstanceLiee I, string Avant, string Apres)>();
        foreach (var id in geste.Demande.Ids.Distinct(StringComparer.Ordinal))
        {
            // 1. L'instance, dans la recherche de son processus.
            if (await LireInstanceAsync(geste.Base, geste.Transaction, id, ct) is not { } i)
                return IssueGeste.Refuser($"Instance introuvable dans la base du greffe ({id}).");
            // 2. Le verrou de RIG v2.
            if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance,
                                                 i.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ct) is { } verrou)
                return IssueGeste.Refuser(verrou);
            // 3. L'exclusivite PC / prevention.
            if (i.LieeAutreType) return IssueGeste.Refuser(i.MessageAutreType);

            // Ce qui est saisi : le numero du champ du groupe, ou la case de detachement.
            var champ = i.VersPc ? ChampPc : ChampPrevention;
            var autreChamp = i.VersPc ? ChampPrevention : ChampPc;
            if (!string.IsNullOrWhiteSpace(geste.Valeur(SectionLien, autreChamp)))
                return IssueGeste.Refuser($"« {SectionLien} › {autreChamp} » ne s'applique pas à {i.EnMots} : elle est dans « {(i.VersPc ? VersPc : VersPrevention)} ».");
            var numero = geste.Valeur(SectionLien, champ)?.Replace(" ", "", StringComparison.Ordinal) ?? "";
            var detacher = geste.Valeur(SectionDetachement, ChampDetacher) == "oui";
            var genre = i.VersPc ? "procédure collective" : "prévention";
            if (numero.Length == 0 && !detacher)
                return IssueGeste.Refuser($"Rien à enregistrer : saisir le numéro de la {genre} (« {SectionLien} › {champ} ») ou cocher « {ChampDetacher} ».");
            if (numero.Length > 0 && detacher)
                return IssueGeste.Refuser($"Saisir un numéro de {genre} OU cocher « {ChampDetacher} », pas les deux.");

            var avant = i.VersPc ? i.NumPc : i.NumPrv;
            int? nouvelle = null;
            var apres = "";
            if (detacher)
            {
                // SupprimerLienPc / SupprimerLienPr ne font rien sans lien (OPE_XLIENPC_INSTANCE.cs:141).
                if (!i.Lie) return IssueGeste.Refuser($"{Majuscule(i.EnMots)} n'est rattachée à aucune {genre} : rien à détacher.");
            }
            else
            {
                // 4. Le numero metier : exactement une entite.
                var trouvees = new List<(int Id, string Numero)>();
                await using (var r = await geste.LireAsync(i.VersPc ? EntiteParNumero : PreventionParNumero, ct, new SqlParameter("@numero", numero)))
                    while (await r.ReadAsync(ct)) trouvees.Add(((int)r["Id"], FormeLigne.Texte(r, "Numero")));
                if (trouvees.Count == 0) return IssueGeste.Refuser($"Aucune {genre} ne porte le numéro {numero} : rien n'est rattaché.");
                if (trouvees.Count > 1) return IssueGeste.Refuser($"Plusieurs {genre}s portent le numéro {numero} : rien n'est rattaché.");
                if (trouvees[0].Id == (i.VersPc ? i.IdPc : i.IdPrv))
                    return IssueGeste.Refuser($"{Majuscule(i.EnMots)} est déjà rattachée à la {genre} {trouvees[0].Numero} : rien ne change.");
                nouvelle = trouvees[0].Id;
                apres = trouvees[0].Numero;
            }

            var colonne = i.VersPc ? "INSTN_ID_PRCLL" : "INSTN_ID_PREVE";
            var touchees = await geste.EcrireAsync($"UPDATE v2.INSTANCE SET {colonne} = @lien, {SauveInstanceV2.Effets} WHERE INSTN_ID_INSTN = @id", ct,
                [new SqlParameter("@lien", (object?)nouvelle ?? DBNull.Value), new SqlParameter("@id", i.Id), .. SauveInstanceV2.Parametres(geste.Maintenant)]);
            if (touchees != 1) throw new InvalidOperationException($"Instance {i.Id} : {touchees} ligne(s) mise(s) a jour au lieu d'une.");
            faites.Add((i, avant, apres));
        }

        var message = string.Join(" ", faites.Select(f => f.Apres.Length == 0
            ? $"{Majuscule(f.I.EnMots)} est détachée de la {(f.I.VersPc ? "PC" : "prévention")} {f.Avant}."
            : $"{Majuscule(f.I.EnMots)} est rattachée à la {(f.I.VersPc ? "PC" : "prévention")} {f.Apres}{(f.Avant.Length > 0 ? $" (au lieu de {f.Avant})" : "")}."));
        return IssueGeste.Ecrit(message, [.. faites.Select(f => $"{(f.I.VersPc ? "PC" : "PR")}-{f.I.Id}")],
            [.. faites.Select(f => new TraceGeste("affaire", f.I.EnMots, f.I.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), "a modifie",
                new Dictionary<string, string> { [f.I.VersPc ? ChampPc : ChampPrevention] = f.Avant },
                new Dictionary<string, string> { [f.I.VersPc ? ChampPc : ChampPrevention] = f.Apres }))]);
    }

    private static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
