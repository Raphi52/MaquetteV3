using Microsoft.Data.SqlClient;
using RigV3.Base;

// L'outil des migrations, en ligne de commande (mode d'emploi :
// docs/reconstruction/architecture.md, « Ajouter une table »).
//
//   dotnet run --project Base/RigV3.Migrations.csproj -- --base "<chaine>" [--dossier <dir>] [--liste] [--creer]
//
//   --base     chaine de connexion A LA BASE V3 (sinon variable RIGV3_BASE)
//   --dossier  dossier des scripts (defaut : migrations/ a cote de l'outil)
//   --liste    montre l'ordre, ce qui est joue et ce qui reste, sans rien jouer
//   --creer    cree la base si elle manque -- serveur LOCAL seulement (developpement)
//
// Code de sortie : 0 = a jour, 1 = une migration refusee ou en echec, 2 = mauvais usage.

string? chaine = Environment.GetEnvironmentVariable("RIGV3_BASE");
var dossier = Path.Combine(AppContext.BaseDirectory, "migrations");
bool liste = false, creer = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--base" when i + 1 < args.Length: chaine = args[++i]; break;
        case "--dossier" when i + 1 < args.Length: dossier = Path.GetFullPath(args[++i]); break;
        case "--liste": liste = true; break;
        case "--creer": creer = true; break;
        default:
            Console.Error.WriteLine($"Argument inconnu : {args[i]}");
            return 2;
    }
}
if (string.IsNullOrWhiteSpace(chaine))
{
    Console.Error.WriteLine("Base non donnee : --base \"<chaine de connexion>\" ou variable RIGV3_BASE.");
    return 2;
}

try
{
    var builder = new SqlConnectionStringBuilder(chaine);
    if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
    {
        Console.Error.WriteLine("La chaine ne nomme pas de base (Database=...) : l'outil ne migre jamais master.");
        return 2;
    }
    Console.WriteLine($"Base : {builder.InitialCatalog} sur {builder.DataSource}");
    Console.WriteLine($"Scripts : {dossier}");

    if (creer)
    {
        BaseJetable.ExigerServeurLocal(chaine);
        var master = new SqlConnectionStringBuilder(chaine) { InitialCatalog = "master" }.ConnectionString;
        await using var cn = new SqlConnection(master);
        await cn.OpenAsync();
        // EXEC(...) n'accepte que des litteraux et des variables : la commande est
        // donc composee dans @sql (QUOTENAME dans EXEC(...) : « Incorrect syntax »).
        await using var cmd = new SqlCommand(
            $"DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@nom) + N' COLLATE {BaseJetable.Collation}';\n" +
            "IF DB_ID(@nom) IS NULL EXEC(@sql);", cn);
        cmd.Parameters.AddWithValue("@nom", builder.InitialCatalog);
        await cmd.ExecuteNonQueryAsync();
    }

    if (liste)
    {
        var migrations = Migrateur.Lire(dossier);
        IReadOnlyList<string> jouees;
        try { jouees = await Migrateur.JoueesAsync(chaine); }
        catch (SqlException e) when (e.Number == 208) { jouees = []; } // journal absent : base jamais migree
        foreach (var m in migrations)
            Console.WriteLine($"  {(jouees.Contains(m.Id) ? "jouee  " : "a jouer")}  {m.Id}");
        return 0;
    }

    var rapport = await Migrateur.AppliquerAsync(chaine, dossier, Console.WriteLine);
    foreach (var absente in rapport.Absentes)
        Console.WriteLine($"  attention : {absente} est au journal mais son fichier n'est plus dans le dossier");
    Console.WriteLine($"A jour : {rapport.Jouees.Count} jouee(s) maintenant, {rapport.DejaJouees.Count} deja jouee(s).");
    return 0;
}
catch (Exception e) when (e is ErreurMigration or SqlException or InvalidOperationException or ArgumentException)
{
    Console.Error.WriteLine($"ECHEC : {e.Message}");
    return 1;
}
