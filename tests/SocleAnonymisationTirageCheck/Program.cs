using System.Diagnostics;
using Microsoft.Data.SqlClient;
using RigV3.Outils.Anonymisation;

// Banc du TIRAGE de la mesure des colonnes gardees (SOC-021 CA06, lot 6 du plan de reconstruction, phase 2,
// 2026-11-21). Il est a part de tests/SocleAnonymisationCheck, que la relecture a fige tel qu'elle l'a relu ;
// « .\tests.ps1 SocleAnonymisation » lance les deux.
//
// Le defaut qu'il garde (objection de la relecture, 2026-11-21) : ControleCopie.MesurerGardeesAsync tirait des PAGES
// au hasard d'une grande table, puis gardait les valeurs distinctes de ces pages. Pour une colonne qui a peu de valeurs,
// ces pages ne rendent que les plus frequentes : 4 a 9 des 82 valeurs de DEMANDE_EDITION.DMNED_CODE_ENTITE_DESTINATAIRE
// (4 300 504 lignes sur RIG_DEV), ou un seul mot a prenom faisait 12 a 50 % -- le banc de la copie rougissait au hasard.
//
//  * L'ENTREE QUI LE FAIT ROUGIR si la correction est fausse : DMNED_CODE_ENTITE_DESTINATAIRE jugee sur moins de
//    toutes ses valeurs distinctes (et, avec elle, chaque colonne d'une grande table jugee sur moins de 200 valeurs
//    sans les avoir toutes). Le banc compte ces valeurs A PART (COUNT DISTINCT par la porte de lecture : un nombre).
//  * SON JUMEAU ACCEPTE (meme borne de 200, autre cote) : une colonne qui a 200 valeurs distinctes ou plus reste jugee
//    sur 200 valeurs TIREES (MesureGardee.Toutes = faux) -- la table n'est relue en entier que pour les colonnes maigres.
//
// Il lit RIG_DEV (SQL-DEV\DEV) en LECTURE SEULE, par la porte de l'outil (PorteSource : SELECT seul, accord « RIG_DEV ») ;
// il n'ecrit nulle part. Autre serveur : RIGV3_RIG_DEV. Doc : docs/reconstruction/socle-anonymisation.md.

const int Echantillon = 200; // celui de « controler » et du banc de la copie
var chaineRigDev = Environment.GetEnvironmentVariable("RIGV3_RIG_DEV") is { Length: > 0 } c0 ? c0 :
    @"Server=SQL-DEV\DEV;Database=RIG_DEV;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=20";
var baseRigDev = new SqlConnectionStringBuilder(chaineRigDev).InitialCatalog;
const string Temoin = "DEMANDE_EDITION.DMNED_CODE_ENTITE_DESTINATAIRE";

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

PorteSource rigDev;
try
{
    rigDev = await PorteSource.OuvrirAsync(chaineRigDev, baseRigDev, CancellationToken.None);
    Exiger(true, $"RIG_DEV lu en lecture seule par la porte de l'outil (accord « {baseRigDev} ») : {rigDev.Serveur} / {rigDev.Base}");
}
catch (Exception e)
{
    Exiger(false, $"RIG_DEV lisible ({new SqlConnectionStringBuilder(chaineRigDev).DataSource}) : {e.Message} -- il faut l'acces en lecture a SQL-DEV\\DEV (ou RIGV3_RIG_DEV)");
    return 1;
}

await using (rigDev)
{
    // Les regles de la copie, telles que « controler » les applique a RIG_DEV.
    var chrono = Stopwatch.StartNew();
    var schema = await SchemaSource.LireAsync(rigDev, CancellationToken.None);
    var catalogue = new CatalogueRegles();
    catalogue.AlignerSurLesCles(schema);
    var volumes = (await rigDev.LignesAsync("SELECT p.object_id, SUM(p.rows) FROM sys.partitions p WHERE p.index_id IN (0, 1) GROUP BY p.object_id", CancellationToken.None))
        .ToDictionary(l => Convert.ToInt32(l[0]), l => Convert.ToInt64(l[1]));

    // Les seules tables tirees PAR PAGES : celles de plus de GrandeTable lignes (les autres sont lues en entier).
    var grandes = schema.Tables.Where(t => volumes.GetValueOrDefault(t.Id) > ControleCopie.GrandeTable).ToList();
    var mesure = await ControleCopie.MesurerGardeesAsync(rigDev, schema, catalogue, grandes, Echantillon, CancellationToken.None);
    var dureeMesure = chrono.Elapsed;

    // Chaque colonne jugee sur moins de 200 valeurs : combien de valeurs distinctes a-t-elle vraiment ?
    chrono.Restart();
    var exactes = new List<string>();
    var maigres = new List<string>();
    var tirees = new List<MesureGardee>();
    string? temoin = null;
    foreach (var t in grandes)
        foreach (var g in mesure.Colonnes.Where(g => g.Colonne.StartsWith(t.Libelle + ".", StringComparison.Ordinal)))
        {
            if (g.Valeurs >= Echantillon) { tirees.Add(g); continue; }
            var col = "[" + g.Colonne[(t.Libelle.Length + 1)..] + "]";
            var distinctes = Convert.ToInt64(await rigDev.ValeurAsync(
                $"SELECT COUNT(DISTINCT CONVERT(nvarchar(4000), {col})) FROM {t.Complet} WITH (NOLOCK) WHERE {col} LIKE N'%[A-Za-z]%'", CancellationToken.None));
            var ligne = $"{g.Colonne[4..]} {g.Valeurs} sur {distinctes} ({volumes[t.Id]} lignes, {g.Part:P0} de prenoms)";
            (g.Valeurs == distinctes && g.Toutes ? exactes : maigres).Add(ligne);
            if (g.Colonne[4..] == Temoin) temoin = ligne;
        }

    // L'entree temoin, nommee : sans la relecture en entier, elle sort sur quelques-unes de ses 82 valeurs.
    var temoinMesure = mesure.Colonnes.FirstOrDefault(g => g.Colonne[4..] == Temoin);
    Exiger(temoinMesure is { Toutes: true } && temoin is not null && exactes.Contains(temoin),
        $"SOC-021 CA06 (tirage) : sur {baseRigDev}, {Temoin} (grande table, peu de valeurs) est jugee sur TOUTES ses valeurs distinctes : " +
        (temoin ?? (temoinMesure is null ? "colonne absente de la mesure (regle changee ? le banc perd son temoin)" : $"{temoinMesure.Valeurs} valeurs, comptage non fait")));
    Exiger(maigres.Count == 0 && exactes.Count > 0,
        $"SOC-021 CA06 (tirage) : sur {baseRigDev} ({grandes.Count} tables de plus de {ControleCopie.GrandeTable} lignes, mesure {dureeMesure.TotalSeconds:0} s, " +
        $"comptage {chrono.Elapsed.TotalSeconds:0} s), chaque colonne gardee jugee sur moins de {Echantillon} valeurs l'est sur toutes les siennes : {exactes.Count} colonnes" +
        (maigres.Count == 0 ? "" : $" -- JUGEES SUR UN TIRAGE MAIGRE : {string.Join(", ", maigres)}"));
    // Jumeau accepte, meme borne de l'autre cote : 200 valeurs ou plus -> tirage, la table n'est pas relue.
    Exiger(tirees.Count > 0 && tirees.All(g => g.Valeurs == Echantillon && !g.Toutes),
        $"SOC-021 CA06 (tirage, jumeau) : {tirees.Count} colonnes de grandes tables qui ont au moins {Echantillon} valeurs restent jugees sur {Echantillon} valeurs tirees " +
        $"(Toutes = faux), par ex. {string.Join(", ", tirees.Take(3).Select(g => $"{g.Colonne[4..]} {g.Part:P0}"))}" +
        (tirees.All(g => g.Valeurs == Echantillon && !g.Toutes) ? "" : $" -- ECART : {string.Join(", ", tirees.Where(g => g.Valeurs != Echantillon || g.Toutes).Select(g => $"{g.Colonne[4..]} {g.Valeurs}"))}"));
    // Ce qui sort de la source pendant la mesure : des empreintes salees et des nombres, jamais une valeur (CA06).
    var genresAdmis = new HashSet<string> { "empreinte", "nombre" };
    Exiger(mesure.GenresLus.IsSubsetOf(genresAdmis),
        $"SOC-021 CA06 : pendant la mesure, ne sortent de la source que : {string.Join(", ", mesure.GenresLus)} (jamais une valeur)");
}

Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "SocleAnonymisationTirageCheck : VERT" : $"SocleAnonymisationTirageCheck : {echecs.Count} ECHEC(S)");
return echecs.Count == 0 ? 0 : 1;
