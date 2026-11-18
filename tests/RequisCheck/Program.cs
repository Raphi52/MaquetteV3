using RigV3Desktop;

// Le parcours guide « requis d'abord » ne vaut que si CHAQUE champ declare
// obligatoire existe vraiment dans la fiche : un requis fantome bloquerait
// un parcours que personne ne pourrait terminer.
var echecs = 0;
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK    : " : "ECHEC : ") + quoi);
    if (!ok) echecs++;
}

var champs = InformationsDossier.Sections
    .SelectMany(s => s.Champs.Select(c => (s.Table, c.Colonne)))
    .ToHashSet();

foreach (var (table, colonne) in ChampsRequisDossier.Lus)
    Exiger(champs.Contains((table, colonne)),
        $"le requis {table}.{colonne} existe dans la fiche");

Exiger(ChampsRequisDossier.Nombre > 0 && ChampsRequisDossier.Nombre < 40,
    $"les requis restent une liste COURTE ({ChampsRequisDossier.Nombre} sur {InformationsDossier.NombreChamps})");

foreach (var sys in ChampsRequisDossier.Systeme)
    Exiger(!ChampsRequisDossier.EstRequis(sys.Table, sys.Colonne),
        $"{sys.Table}.{sys.Colonne} est ecrit par le systeme, pas reclame au greffier");

Exiger(ChampsRequisDossier.EstRequis("INSCRIT_PM", "FORME_JURIDIQUE_UTI"),
    "la forme juridique reste obligatoire");
Exiger(!ChampsRequisDossier.BlocConditionnel("DOSSIER_RCS"),
    "les requis du dossier RCS valent toujours");
Exiger(ChampsRequisDossier.BlocConditionnel("DIRIGEANT"),
    "les requis d'un dirigeant ne valent que si le bloc est entame");

Console.WriteLine($"REQUIS : {echecs} echec(s).");
return echecs == 0 ? 0 : 1;
