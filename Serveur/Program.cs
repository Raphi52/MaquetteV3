// Le serveur de la V3 de RIG. Tout le montage est dans ServeurRig.cs : le banc
// tests/ServeurCheck demarre exactement le meme serveur, en memoire.
//
//   dotnet run --project Serveur/RigV3.Serveur.csproj --urls http://127.0.0.1:5080
//   (base : variable ConnectionStrings__RigV3 ; voir docs/reconstruction/architecture.md)

RigV3.Serveur.ServeurRig.Construire(args).Run();
