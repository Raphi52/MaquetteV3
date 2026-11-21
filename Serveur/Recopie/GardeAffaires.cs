using System.Text;

namespace RigV3.Serveur.Recopie;

// ============================================================
// LES AFFAIRES DES DEUX LOGICIELS DANS LA BASE V3 D'UN GREFFE (REC-010, REC-011, REC-007 ; lot 11, 2026-11-21).
//
// Une affaire se traite dans UN logiciel (Q5, Q14 ; SOC-014) : creee avant le passage, dans l'ancien RIG ; apres, dans la
// V3. La base V3 tient pourtant les deux, dans v2.AFFAIRE et v2.DEMANDE :
//   - les ANCIENNES (origine V2) : transferees (REC-001), puis tenues a jour par la remontee (REC-003) -- une demande validee
//     dans la V2 se relit validee dans la V3 au passage suivant (REC-010 CA02). Leur ligne a une correspondance V2 ;
//   - celles de la V3 (origine V3) : sans correspondance, jamais mises en file vers la V2 (RegleRecopie.RemonteeSeule) ;
//     elles n'y vont qu'au retour d'un greffe beta (REC-007, RetourBeta.cs), qui leur donne alors leur correspondance.
// L'origine d'une ligne n'est donc pas une colonne : c'est rec.Correspondance (REC-010 T01).
//
// Le declencheur de garde (pose a la mise en service de la recopie, a la place du declencheur de file) tient les regles
// qu'aucun code de domaine ne doit pouvoir oublier, comme la file du lot 9 :
//   1. une ecriture dans une ancienne affaire (l'affaire, ou l'une de ses demandes, ou une demande ajoutee) est refusee :
//      « cette affaire se traite dans l'ancien RIG », rien n'est ecrit (REC-010 CA03) ;
//   2. une creation d'affaire ou de demande pour un greffe revenu a l'ancien RIG est refusee, et le dit (REC-007 CA03) ;
//   3. une demande creee dans la V3 sur un dossier ou une ancienne demande encore en cours bloque les autres demandes est
//      bloquee par elle (DMND_INHIBEE, DMND_ID_DMND_INHIBITRICE vers sa ligne de la V3) : l'agent voit laquelle et dans quel
//      logiciel (REC-011 CA01) ;
//   4. sous la marque de la remontee : une ancienne demande validee ou abandonnee dans la V2 leve les blocages qu'elle posait
//      sur des demandes de la V3, au passage qui la remonte (5 s, REC-011 CA02).
// Les ecritures de la remontee (marque rigv3.remontee) ne sont jamais refusees : c'est la V2 qui les a faites.
// Mode d'emploi : docs/reconstruction/recopie.md, section « Les affaires des deux logiciels ».
// ============================================================

/// <summary>Les regles de recopie des affaires et des demandes : de la V2 vers la V3 seulement (REC-010 T02).</summary>
internal sealed class ReglesAffaires : IReglesRecopie
{
    public IEnumerable<RegleRecopie> Regles =>
    [
        RegleRecopie.TelleQuelle("AFFAIRE", "REC-010", "affaire") with
        {
            RemonteeSeule = true,
            ColonneNom = "AFFRE_LIBELLE",
            ColonneDateV2 = "AFFRE_DATE_CREATION",
            // Ni cle etrangere ni lien dans la V2 : des numeros d'UTILISATEUR, que le retour traduit (compte de la V3 -> V2).
            NumerosV2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AFFRE_ID_UTLST_CREATION"] = "UTILISATEUR", ["AFFRE_ID_UTLST_EN_COURS"] = "UTILISATEUR",
            },
        },
        RegleRecopie.TelleQuelle("DEMANDE", "REC-010", "demande") with
        {
            RemonteeSeule = true,
            ColonneNom = "DMND_NUM_DEMANDE",
            ColonneDateV2 = "DMND_DATE_MODIFICATION",
            NumerosV2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DMND_ID_CTGR"] = "CATEGORIE", ["DMND_ID_UTLST"] = "UTILISATEUR",
            },
        },
    ];
}

/// <summary>Le declencheur de garde des affaires et des demandes dans la base V3 (voir l'en-tete du fichier).</summary>
public static class GardeAffaires
{
    /// <summary>Une ecriture dans une ancienne affaire (REC-010 CA03).</summary>
    public const int ErreurAncienneAffaire = 51030;
    /// <summary>Une creation pour un greffe revenu a l'ancien RIG (REC-007 CA03).</summary>
    public const int ErreurGreffeRevenu = 51031;

    /// <summary>Le debut du message de refus d'une ecriture dans une ancienne affaire, tel que l'agent le lit (REC-010 CA03).</summary>
    public const string MessageAncienne = "Cette affaire se traite dans l'ancien RIG";

    /// <summary>Les etats d'une demande de la V2 qui ne bloquent plus (validee, supprimee, terminee, refusee ; Demande.cs de
    /// RigMetier, eEtatDemande). Une demande dans un autre etat (qualifiee, en attente, arrivee, en reclamation, reservee,
    /// interrompue) est encore en cours.</summary>
    public static readonly IReadOnlyList<string> EtatsFinis = ["V", "X", "T", "O"];

    /// <summary>Les etats qui levent un blocage pose sur une demande de la V3 (REC-011 CA02 : « validee ou abandonnee »).</summary>
    public static readonly IReadOnlyList<string> EtatsQuiLevent = ["V", "X"];

    public static string NomDeclencheur(TableRecopiee t) => $"TR_{t.V3.NomSeul}_GardeAffaires";

    /// <summary>Les lots de la garde d'une table : ses index (les recherches du declencheur et de l'historique ne parcourent
    /// pas une table de plusieurs millions de lignes), puis le declencheur.</summary>
    public static IReadOnlyList<string> Lots(TableRecopiee t, string baseV2) => t.NomV2.ToUpperInvariant() switch
    {
        "AFFAIRE" => [Index(t, "AFFRE_ID_CLIEN"), DeclencheurAffaire(t, baseV2)],
        "DEMANDE" => [Index(t, "DMND_ID_AFFRE"), Index(t, "DMND_ID_DSRCS"), Index(t, "DMND_ID_DMND_INHIBITRICE"), Index(t, "DMND_ID_DMND_MERE"),
                      Index(t, "DMND_ID_ARRV"), DeclencheurDemande(t, baseV2)],
        _ => throw new InvalidOperationException($"{t.NomV2} : la garde des affaires ne vaut que pour AFFAIRE et DEMANDE"),
    };

    private static string Index(TableRecopiee t, string colonne)
    {
        var nom = $"IX_{t.V3.NomSeul}_{colonne}";
        return $"IF COL_LENGTH({Sql.Texte(t.NomV3)}, {Sql.Texte(colonne)}) IS NOT NULL AND INDEXPROPERTY(OBJECT_ID({Sql.Texte(t.NomV3)}), {Sql.Texte(nom)}, 'IndexID') IS NULL " +
               $"CREATE INDEX {Sql.Q(nom)} ON {t.NomV3Sql} ({Sql.Q(colonne)});";
    }

    /// <summary>« la ligne de cle x a une correspondance V2 » pour la table V2 <paramref name="tableV2"/> (copie v2.X).</summary>
    private static string Ancienne(string tableV2, string expressionCle, string baseV2) =>
        $"EXISTS (SELECT 1 FROM rec.Correspondance AS c WHERE c.TableV3 = N'v2.{tableV2}' AND c.IdV3 = CONVERT(nvarchar(200), {expressionCle}) " +
        $"AND c.BaseV2 = {Sql.Texte(baseV2)} AND c.TableV2 = N'{tableV2}')";

    private static string Entete(TableRecopiee t, StringBuilder sb)
    {
        sb.AppendLine($"CREATE OR ALTER TRIGGER {Sql.Q(t.V3.Schema)}.{Sql.Q(NomDeclencheur(t))} ON {t.NomV3Sql}");
        sb.AppendLine("AFTER INSERT, UPDATE, DELETE");
        sb.AppendLine("AS");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    -- Pose par Serveur\\Recopie\\GardeAffaires.cs a la mise en service de la recopie (REC-010, REC-011, REC-007).");
        sb.AppendLine("    SET NOCOUNT ON;");
        sb.AppendLine("    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;");
        return $"ISNULL(CONVERT(int, SESSION_CONTEXT(N'{PontRecopie.CleRemontee}')), 0) = 1";
    }

    private static void Refus(StringBuilder sb, string cle, string baseV2, string anciennes)
    {
        // 2. REC-007 CA03 : le greffe est revenu a l'ancien RIG (ou y revient) : plus aucune creation dans la V3.
        sb.AppendLine($"    IF EXISTS (SELECT 1 FROM inserted AS i WHERE NOT EXISTS (SELECT 1 FROM deleted AS d WHERE d.{cle} = i.{cle}))");
        sb.AppendLine($"       AND EXISTS (SELECT 1 FROM rec.EtatRetour WHERE BaseV2 = {Sql.Texte(baseV2)})");
        sb.AppendLine("    BEGIN");
        sb.AppendLine("        DECLARE @revenu nvarchar(2048);");
        sb.AppendLine("        SELECT @revenu = N'Création refusée : le greffe ' + CASE Etat WHEN 'en retour' THEN N'revient à l''ancien RIG depuis le '");
        sb.AppendLine("                         ELSE N'est revenu à l''ancien RIG le ' END + CONVERT(nvarchar(10), Depuis, 103) + N' à ' + CONVERT(nvarchar(5), Depuis, 108)");
        sb.AppendLine("                         + N' : les affaires et les demandes se créent dans l''ancien RIG (REC-007).'");
        sb.AppendLine($"        FROM rec.EtatRetour WHERE BaseV2 = {Sql.Texte(baseV2)};");
        sb.AppendLine($"        THROW {ErreurGreffeRevenu}, @revenu, 1;");
        sb.AppendLine("    END;");
        // 1. REC-010 CA03 : une ancienne affaire se traite dans l'ancien RIG.
        sb.AppendLine($"    IF EXISTS ({anciennes})");
        sb.AppendLine($"        THROW {ErreurAncienneAffaire}, N'{MessageAncienne.Replace("'", "''")} : la V3 la montre en lecture seule ; ouvrez-la dans l''ancien RIG pour la modifier ou y ajouter une demande (REC-010).', 1;");
    }

    internal static string DeclencheurAffaire(TableRecopiee t, string baseV2)
    {
        var sb = new StringBuilder();
        var remontee = Entete(t, sb);
        sb.AppendLine($"    IF {remontee} RETURN;   -- la remontee ecrit ce que la V2 a fait (REC-003)");
        Refus(sb, "[AFFRE_ID_AFFRE]", baseV2,
              $"SELECT 1 FROM (SELECT AFFRE_ID_AFFRE FROM inserted UNION SELECT AFFRE_ID_AFFRE FROM deleted) AS x WHERE {Ancienne("AFFAIRE", "x.AFFRE_ID_AFFRE", baseV2)}");
        sb.AppendLine("END;");
        return sb.ToString();
    }

    internal static string DeclencheurDemande(TableRecopiee t, string baseV2)
    {
        var finis = string.Join(", ", EtatsFinis.Select(e => $"'{e}'"));
        var levent = string.Join(", ", EtatsQuiLevent.Select(e => $"'{e}'"));
        var sb = new StringBuilder();
        var remontee = Entete(t, sb);
        sb.AppendLine($"    IF {remontee}");
        sb.AppendLine("    BEGIN");
        // 4. REC-011 CA02 : la demande de l'ancien RIG validee ou abandonnee leve les blocages qu'elle pose sur la V3.
        sb.AppendLine("        DECLARE @levees TABLE (Id int PRIMARY KEY, Bloquante int, Etat varchar(1));");
        sb.AppendLine("        UPDATE b SET DMND_INHIBEE = 0, DMND_ID_DMND_INHIBITRICE = NULL, DMND_TEXTE_INHIBITEUR = NULL");
        sb.AppendLine("        OUTPUT inserted.DMND_ID_DMND, i.DMND_ID_DMND, i.DMND_ETAT_DEMANDE INTO @levees");
        sb.AppendLine("        FROM v2.DEMANDE AS b JOIN inserted AS i ON b.DMND_ID_DMND_INHIBITRICE = i.DMND_ID_DMND");
        sb.AppendLine($"        WHERE i.DMND_ETAT_DEMANDE IN ({levent}) AND b.DMND_INHIBEE = 1 AND NOT {Ancienne("DEMANDE", "b.DMND_ID_DMND", baseV2)};");
        sb.AppendLine("        UPDATE l SET LeveLe = SYSDATETIME(), Motif = CASE v.Etat WHEN 'V' THEN N'la demande de l''ancien RIG est validée' ELSE N'la demande de l''ancien RIG est abandonnée' END");
        sb.AppendLine("        FROM rec.LienDemande AS l JOIN @levees AS v ON v.Id = l.IdDemande AND v.Bloquante = l.IdVisee");
        sb.AppendLine("        WHERE l.Porteuse = 'V3' AND l.Lien = 'inhibitrice' AND l.LeveLe IS NULL;");
        sb.AppendLine("        RETURN;");
        sb.AppendLine("    END;");
        Refus(sb, "[DMND_ID_DMND]", baseV2,
              "SELECT 1 FROM (SELECT DMND_ID_DMND, DMND_ID_AFFRE FROM inserted UNION ALL SELECT DMND_ID_DMND, DMND_ID_AFFRE FROM deleted) AS x " +
              $"WHERE {Ancienne("DEMANDE", "x.DMND_ID_DMND", baseV2)} OR (x.DMND_ID_AFFRE IS NOT NULL AND {Ancienne("AFFAIRE", "x.DMND_ID_AFFRE", baseV2)})");
        // 3. REC-011 CA01 : la regle de blocage de la V2, d'un logiciel a l'autre. Une ancienne demande du meme dossier,
        // encore en cours, qui bloque deja les autres demandes de ce dossier, bloque aussi la demande creee dans la V3.
        sb.AppendLine("    DECLARE @bloquees TABLE (Id int PRIMARY KEY, Bloquante int NOT NULL, Numero varchar(11) NULL);");
        sb.AppendLine("    INSERT INTO @bloquees (Id, Bloquante, Numero)");
        sb.AppendLine("    SELECT i.DMND_ID_DMND, b.DMND_ID_DMND, b.DMND_NUM_DEMANDE");
        sb.AppendLine("    FROM inserted AS i");
        sb.AppendLine("    CROSS APPLY (SELECT TOP (1) o.DMND_ID_DMND, o.DMND_NUM_DEMANDE FROM v2.DEMANDE AS o");
        sb.AppendLine($"                 WHERE o.DMND_ID_DSRCS = i.DMND_ID_DSRCS AND o.DMND_ID_DMND <> i.DMND_ID_DMND AND o.DMND_ETAT_DEMANDE NOT IN ({finis})");
        sb.AppendLine($"                   AND {Ancienne("DEMANDE", "o.DMND_ID_DMND", baseV2)}");
        sb.AppendLine("                   AND EXISTS (SELECT 1 FROM v2.DEMANDE AS a WHERE a.DMND_ID_DMND_INHIBITRICE = o.DMND_ID_DMND AND a.DMND_INHIBEE = 1");
        sb.AppendLine("                                                             AND a.DMND_ID_DSRCS = o.DMND_ID_DSRCS)");
        sb.AppendLine("                 ORDER BY o.DMND_ID_DMND) AS b");
        sb.AppendLine("    WHERE i.DMND_ID_DSRCS IS NOT NULL AND i.DMND_INHIBEE = 0 AND NOT EXISTS (SELECT 1 FROM deleted AS d WHERE d.DMND_ID_DMND = i.DMND_ID_DMND);");
        sb.AppendLine("    IF @@ROWCOUNT = 0 RETURN;");
        sb.AppendLine("    UPDATE x SET DMND_INHIBEE = 1, DMND_ID_DMND_INHIBITRICE = q.Bloquante,");
        sb.AppendLine("                 DMND_TEXTE_INHIBITEUR = LEFT('Bloquée par la demande ' + ISNULL(q.Numero, CONVERT(varchar(11), q.Bloquante)) + ' de l''ancien RIG', 255)");
        sb.AppendLine("    FROM v2.DEMANDE AS x JOIN @bloquees AS q ON q.Id = x.DMND_ID_DMND;");
        sb.AppendLine("    INSERT INTO rec.LienDemande (Porteuse, IdDemande, Lien, IdVisee, PosePar, Motif)");
        sb.AppendLine("    SELECT 'V3', q.Id, 'inhibitrice', q.Bloquante, N'regle de blocage de la V2', N'demande ' + ISNULL(q.Numero, N'?') + N' de l''ancien RIG en cours sur le même dossier'");
        sb.AppendLine("    FROM @bloquees AS q;");
        sb.AppendLine("END;");
        return sb.ToString();
    }
}
