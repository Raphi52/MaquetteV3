// Le serveur de la V3 de RIG. Tout le montage est dans ServeurRig.cs : le banc
// tests/ServeurCheck demarre exactement le meme serveur, en memoire.
//
//   dotnet run --project Serveur/RigV3.Serveur.csproj --urls http://127.0.0.1:5080
//   (base : variable ConnectionStrings__RigV3 ; voir docs/reconstruction/architecture.md)
//
// SOC-010 : sur les sites (Amitel, Antilles, Reunion, Polynesie), il tourne en service
// Windows (paquet du site : Outils/Livraison/paquet-serveur.ps1). UseWindowsService
// repond au gestionnaire de services (demarrage, arret) et pose le dossier de l'exe comme
// racine du contenu ; lance hors service (dotnet run, bancs), il ne change rien.

RigV3.Serveur.ServeurRig.Construire(args, b => b.Host.UseWindowsService(o => o.ServiceName = "RigV3Serveur")).Run();
