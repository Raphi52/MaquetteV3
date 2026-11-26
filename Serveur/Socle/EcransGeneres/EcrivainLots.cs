using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurLots;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Lots » (PROC_GESTLOT) : ajouter, modifier (renommer compris) et supprimer un lot d'impression, comme OPE_AJOUT de RIG v2.
/// Mode d'emploi : EcrivainEcran.cs ; la table : LecteurLots.cs. Plan « RIG v3 100 % fonctionnel », lot 9, 26/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_GESTLOT\Fenetres\OPE_AJOUT.cs (proc_gestlot.md, Detail OPE) et
/// Source\Outils\DLL\RigMetier\Edition\Lot.cs.
/// CONTROLES (DoVerifier, :165-175), avant toute ecriture, au message de RIG v2 :
///   1. le nom est renseigne : « Le nom du lot doit être renseigné » ;
///   2. un nom NOUVEAU (creation, ou renommage vers un autre nom) n'est pas deja pris : « Un lot existant porte ce nom. » ;
///   3. la case « Lot direct » n'est jamais saisie (ult_chkDirect.MetierReadOnly = true, :148) : la V3 refuse de l'ecrire ;
///   4. l'heure d'impression est une heure (l'ULT heure de RIG v2 ne laisse rien saisir d'autre) ;
///   5. modifier ou supprimer vise un lot de la table.
/// CE QUI S'ECRIT (_valider, :196-267, une transaction) :
///   - creation : la ligne v2.LOT aux valeurs de Lot.CreateLot (Lot.cs:123-137 : non direct, sans parametre d'edition, sans tri
///     automatique, invisible des editions differees), ACTIVE d'office (_link, :143), l'heure saisie sur la date du jour
///     (DateTime.Parse de l'heure, :208) -- 00:00 sans heure, comme l'heure vide de l'ULT ;
///   - modification : les colonnes changees ; un RENOMMAGE cree le lot au nouveau nom, y reporte les groupes d'impression de
///     l'ancien (tous etats, :231-239) et supprime l'ancien lot (:240-243) ;
///   - un lot direct enregistre retire « direct » a tout autre lot (un seul lot direct, :246-253) ;
///   - suppression par un agent du greffe : le lot est DESACTIVE (LOT_ACTIF = 0, :316-320), pas supprime.
/// Traces : famille « reglage », « a cree » / « a modifie » / « a desactive », le lot avant et apres.
///
/// NON REPRIS (nomme) : la question « Voulez-vous associer l'imprimante du lot au groupe d'impression actif ? » (:219-227) -- la
/// V3 ne la pose pas, le groupe en cours garde son imprimante ; la suppression DEFINITIVE d'un agent Amitel (le lot et ses
/// groupes, apres deux confirmations, :305-315) -- refusee, rien n'est supprime ; la Demande « seule » GESTLOT validee de
/// TraceAction (:269-283), une trace remplacee par le journal des gestes ; « Dupliquer » (:285-294), sans geste dans la forme
/// Referentiel ; les champs que le descripteur n'a pas (LecteurLots.cs).
/// </summary>
internal sealed class EcrivainLots : IEcrivainEcran
{
    public string Procedure => "PROC_GESTLOT";
    public string Ecran => "Lots";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Modifier, TypeGeste.Supprimer];

    /// <summary>Les messages de DoVerifier (OPE_AJOUT.cs:167-172), au mot pres.</summary>
    internal const string MessageNomVide = "Le nom du lot doit être renseigné", MessageNomPris = "Un lot existant porte ce nom.";

    internal const string MessageDirect = "La case « Lot direct » ne se saisit pas : RIG v2 la tient en lecture seule (un seul lot direct, posé par la file directe).";
    internal const string MessageAmitel = "La suppression définitive d'un lot et de ses groupes d'impression (agent Amitel, deux confirmations dans RIG v2) n'est pas reprise : rien n'est supprimé.";

    private const string LotLu = """
        SELECT LOT_NOM, LOT_NOM_IMPRIMANTE, LOT_IMPRESSION_AUTO, LOT_HEURE_IMPRESSION_AUTO, LOT_ACTIF, LOT_DIRECT
        FROM v2.LOT WHERE LOT_NOM = @nom
        """;

    private sealed record Lot(string Nom, string Imprimante, bool Auto, DateTime? Heure, bool Actif, bool Direct)
    {
        public Dictionary<string, string> Valeurs() => new()
        {
            [ColNom] = Nom, [ColImprimante] = Imprimante, [ColImpressionAuto] = LecteurLots.Case(Auto), [ColHeure] = LecteurLots.Heure(Heure),
            [ColActif] = LecteurLots.Case(Actif), [ColDirect] = LecteurLots.Case(Direct),
        };
    }

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        string? Saisi(string colonne) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v : null;
        switch (geste.Demande.Type)
        {
            case TypeGeste.Ajouter:
            {
                var nom = (Saisi(ColNom) ?? "").Trim();
                if (nom.Length == 0) return IssueGeste.Refuser(MessageNomVide);
                if (await LireAsync(geste, nom, ct) is not null) return IssueGeste.Refuser(MessageNomPris);
                if (Coche(Saisi(ColDirect))) return IssueGeste.Refuser(MessageDirect);
                if (!HeureLue(Saisi(ColHeure), geste.Maintenant, out var heure, out var refusHeure)) return IssueGeste.Refuser(refusHeure);
                var lot = new Lot(nom, (Saisi(ColImprimante) ?? "").Trim(), Coche(Saisi(ColImpressionAuto)), heure, true, false);
                await geste.EcrireAsync("""
                    INSERT INTO v2.LOT (LOT_NOM, LOT_ACTIF, LOT_IMPRESSION_AUTO, LOT_HEURE_IMPRESSION_AUTO, LOT_NOM_IMPRIMANTE, LOT_DIRECT,
                                        LOT_CODE_PARAMETRE_EDITION, LOT_TRI_AUTO, LOT_VISIBLE_EDTDIFF, LOT_IS_IMPRESSION_SUPPLEMENTAIRE_ACTIVE)
                    VALUES (@nom, 1, @auto, @heure, @imprimante, 0, '', 0, 0, 0)
                    """, ct, new SqlParameter("@nom", lot.Nom), new SqlParameter("@auto", lot.Auto),
                    new SqlParameter("@heure", (object?)lot.Heure ?? DBNull.Value), new SqlParameter("@imprimante", lot.Imprimante));
                return IssueGeste.Ecrit($"Lot « {lot.Nom} » créé.", [lot.Nom],
                    new TraceGeste("reglage", $"le lot d'impression {lot.Nom}", lot.Nom, "a cree", null, lot.Valeurs()));
            }
            case TypeGeste.Modifier:
            {
                var id = geste.Demande.Ids.FirstOrDefault() ?? "";
                if (await LireAsync(geste, id, ct) is not { } avant) return IssueGeste.Refuser($"Lot introuvable dans la base du greffe ({id}).");
                var nom = Saisi(ColNom) is { } n ? n.Trim() : avant.Nom;
                if (nom.Length == 0) return IssueGeste.Refuser(MessageNomVide);
                var renomme = nom != avant.Nom;
                if (renomme && await LireAsync(geste, nom, ct) is not null) return IssueGeste.Refuser(MessageNomPris);
                if (Saisi(ColDirect) is { } direct && Coche(direct) != avant.Direct) return IssueGeste.Refuser(MessageDirect);
                var heure = avant.Heure;
                if (Saisi(ColHeure) is { } h && h.Trim() != LecteurLots.Heure(avant.Heure))
                {
                    if (!HeureLue(h, geste.Maintenant, out heure, out var refusHeure)) return IssueGeste.Refuser(refusHeure);
                }
                var apres = avant with
                {
                    Nom = nom, Imprimante = Saisi(ColImprimante) is { } i ? i.Trim() : avant.Imprimante,
                    Auto = Saisi(ColImpressionAuto) is { } a ? Coche(a) : avant.Auto, Heure = heure,
                    Actif = Saisi(ColActif) is { } ac ? Coche(ac) : avant.Actif,
                };
                var valeurs = new[]
                {
                    new SqlParameter("@nom", apres.Nom), new SqlParameter("@imprimante", apres.Imprimante), new SqlParameter("@auto", apres.Auto),
                    new SqlParameter("@heure", (object?)apres.Heure ?? DBNull.Value), new SqlParameter("@actif", apres.Actif),
                    new SqlParameter("@ancien", avant.Nom),
                };
                if (renomme)
                {
                    // Le lot au nouveau nom, ses groupes, puis l'ancien lot supprime (OPE_AJOUT.cs:229-244).
                    await geste.EcrireAsync("""
                        INSERT INTO v2.LOT (LOT_NOM, LOT_NUM_ORDRE, LOT_ACTIF, LOT_IMPRESSION_AUTO, LOT_HEURE_IMPRESSION_AUTO, LOT_NOM_IMPRIMANTE,
                                            LOT_DIRECT, LOT_PERIODE_AFFICHAGE, LOT_CODE_PARAMETRE_EDITION, LOT_TAILLE_POSTSCRIPT, LOT_TRI_AUTO,
                                            LOT_VISIBLE_EDTDIFF, LOT_IS_IMPRESSION_SUPPLEMENTAIRE_ACTIVE, LOT_IMPRIMANTE_SUPPLEMENTAIRE)
                        SELECT @nom, LOT_NUM_ORDRE, @actif, @auto, @heure, @imprimante, LOT_DIRECT, LOT_PERIODE_AFFICHAGE, LOT_CODE_PARAMETRE_EDITION,
                               LOT_TAILLE_POSTSCRIPT, LOT_TRI_AUTO, LOT_VISIBLE_EDTDIFF, LOT_IS_IMPRESSION_SUPPLEMENTAIRE_ACTIVE, LOT_IMPRIMANTE_SUPPLEMENTAIRE
                        FROM v2.LOT WHERE LOT_NOM = @ancien
                        """, ct, Copie(valeurs));
                    await geste.EcrireAsync("UPDATE v2.GROUPE_IMPRESSION SET GPIMP_NOM_LOT = @nom WHERE GPIMP_NOM_LOT = @ancien", ct, Copie(valeurs));
                    await geste.EcrireAsync("DELETE FROM v2.LOT WHERE LOT_NOM = @ancien", ct, Copie(valeurs));
                }
                else
                {
                    await geste.EcrireAsync("""
                        UPDATE v2.LOT SET LOT_NOM_IMPRIMANTE = @imprimante, LOT_IMPRESSION_AUTO = @auto, LOT_HEURE_IMPRESSION_AUTO = @heure,
                                          LOT_ACTIF = @actif
                        WHERE LOT_NOM = @ancien
                        """, ct, Copie(valeurs));
                }
                // Un seul lot direct (OPE_AJOUT.cs:246-253).
                var autresDirects = apres.Direct
                    ? await geste.EcrireAsync("UPDATE v2.LOT SET LOT_DIRECT = 0 WHERE LOT_DIRECT = 1 AND LOT_NOM <> @nom", ct, new SqlParameter("@nom", apres.Nom))
                    : 0;
                var message = renomme ? $"Lot « {avant.Nom} » renommé « {apres.Nom} » : ses groupes d'impression le suivent." : $"Lot « {apres.Nom} » modifié.";
                if (autresDirects > 0) message += $" {(autresDirects > 1 ? $"{autresDirects} autres lots ne sont" : "L'autre lot direct n'est")} plus direct{(autresDirects > 1 ? "s" : "")}.";
                return IssueGeste.Ecrit(message, [apres.Nom],
                    new TraceGeste("reglage", $"le lot d'impression {apres.Nom}", apres.Nom, "a modifie", avant.Valeurs(), apres.Valeurs()));
            }
            case TypeGeste.Supprimer:
            {
                var id = geste.Demande.Ids.FirstOrDefault() ?? "";
                if (await LireAsync(geste, id, ct) is not { } avant) return IssueGeste.Refuser($"Lot introuvable dans la base du greffe ({id}).");
                if (geste.Agent.EstAmitel) return IssueGeste.Refuser(MessageAmitel);
                await geste.EcrireAsync("UPDATE v2.LOT SET LOT_ACTIF = 0 WHERE LOT_NOM = @nom", ct, new SqlParameter("@nom", avant.Nom));
                return IssueGeste.Ecrit($"Lot « {avant.Nom} » désactivé : RIG v2 ne supprime pas un lot pour un agent du greffe.", [avant.Nom],
                    new TraceGeste("reglage", $"le lot d'impression {avant.Nom}", avant.Nom, "a desactive", avant.Valeurs(),
                        (avant with { Actif = false }).Valeurs()));
            }
            default:
                return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Type}.");
        }
    }

    private static SqlParameter[] Copie(SqlParameter[] p) => [.. p.Select(x => new SqlParameter(x.ParameterName, x.Value))];

    private static bool Coche(string? valeur) => valeur?.Trim().ToLowerInvariant() is "oui" or "true" or "1";

    /// <summary>L'heure saisie (« 07:45 ») sur la date du jour, comme DateTime.Parse de l'ULT heure (OPE_AJOUT.cs:208) ; vide = 00:00.</summary>
    private static bool HeureLue(string? saisie, DateTime maintenant, out DateTime? heure, out string refus)
    {
        refus = "";
        var t = saisie?.Trim() ?? "";
        if (t.Length == 0) { heure = maintenant.Date; return true; }
        if (TimeSpan.TryParseExact(t, ["hh\\:mm", "h\\:mm", "hh\\:mm\\:ss"], CultureInfo.InvariantCulture, out var h) && h < TimeSpan.FromDays(1))
        {
            heure = maintenant.Date + h;
            return true;
        }
        heure = null;
        refus = $"L'heure d'impression « {t} » n'est pas une heure (HH:MM).";
        return false;
    }

    private static async Task<Lot?> LireAsync(GesteEnCours geste, string nom, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(LotLu, ct, new SqlParameter("@nom", nom));
        if (!await r.ReadAsync(ct)) return null;
        return new Lot(FormeLigne.Texte(r, "LOT_NOM"), FormeLigne.Texte(r, "LOT_NOM_IMPRIMANTE"), r["LOT_IMPRESSION_AUTO"] is true,
                       FormeLigne.DateDe(r, "LOT_HEURE_IMPRESSION_AUTO"), r["LOT_ACTIF"] is true, r["LOT_DIRECT"] is true);
    }
}
