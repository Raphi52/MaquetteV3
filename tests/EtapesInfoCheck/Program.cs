using RigV3Desktop;

// Garde-fou du regroupement en ETAPES NOMMEES de « Informations du dossier ».
//
// Defaut a l'origine (17/09/2026) : la fiche paginait 31 sous-sections
// numerotees 1..31 -- il fallait deviner que « etape 17 » portait les
// dirigeants. Les tables sont desormais regroupees en etapes nommees
// (Metier/EtapesInfoDossier.cs). Une table ajoutee plus tard et rattachee a
// aucune etape retomberait en silence dans la derniere : ce banc l'interdit.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var fiche = File.ReadAllText(Path.Combine(racine, "Components/FicheInformationsDossier.razor"));
var css = File.ReadAllText(Path.Combine(racine, "wwwroot/rig-accueil.css"));

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

var orphelines = EtapesInfoDossier.TablesOrphelines();
Exiger(orphelines.Length == 0,
    "chaque table du formulaire a une etape (orphelines : " + string.Join(", ", orphelines) + ")");

var fantomes = EtapesInfoDossier.TablesFantomes();
Exiger(fantomes.Length == 0,
    "aucune etape ne cite une table absente du formulaire (" + string.Join(", ", fantomes) + ")");

Exiger(EtapesInfoDossier.Etapes.Length >= 4 && EtapesInfoDossier.Etapes.Length <= 8,
    "entre 4 et 8 etapes (regroupement lisible) : " + EtapesInfoDossier.Etapes.Length);

Exiger(EtapesInfoDossier.Etapes.Length < InformationsDossier.Sections.Length,
    "moins d'etapes que de blocs (" + EtapesInfoDossier.Etapes.Length
    + " < " + InformationsDossier.Sections.Length + ")");

Exiger(EtapesInfoDossier.Rang("DIRIGEANT") == EtapesInfoDossier.Rang("DIRIGEANT_PP"),
    "les dirigeants personnes physiques sont dans la meme etape que les dirigeants");

Exiger(fiche.Contains("rig-frise--h") && fiche.Contains("EtapesInfoDossier"),
    "la fiche pilote ses etapes avec la MEME frise a jauges que l'ecran de saisie");

Exiger(!fiche.Contains("rig-infos__page") && !fiche.Contains("rig-infos__etape"),
    "l'ancienne pagination 1..31 et les onglets intermediaires ont disparu");

Exiger(fiche.Contains("rig-infos__vue-choix") && fiche.Contains("GardeRemplissage"),
    "la fiche ouvre sur les champs RENSEIGNES et sait montrer ceux a saisir");

Exiger(fiche.Contains("ClavierGuide") && fiche.Contains("ChampsGuide")
       && css.Contains(".rig-guidee__saisie"),
    "la saisie guidee existe : un champ a la fois, pilote au clavier");

Exiger(fiche.Contains("\"Escape\"") && fiche.Contains("\"Enter\"") && fiche.Contains("ShiftKey"),
    "Entree avance, Maj+Entree revient, Echap rend la vue complete");

Exiger(fiche.Contains("FocusAsync"),
    "le champ guide reprend le focus apres chaque Entree");

Exiger(css.Contains("scroll-snap-type: y") && css.Contains("scroll-snap-align: start"),
    "le defilement colle d'un bloc de formulaire au suivant");

Console.WriteLine();
if (echecs.Count > 0)
{
    Console.WriteLine("ROUGE : " + echecs.Count + " controle(s) en echec.");
    return 1;
}
Console.WriteLine("VERT : etapes nommees et defilement colle.");
return 0;
