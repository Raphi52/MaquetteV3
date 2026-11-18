// Les gestes communs des ecrans de RECHERCHE portes depuis Blazor (lot 5) :
// dates affichees, recherche sans casse, listes de choix triees, nombres saisis.
// Chacun reproduit un appel C# precis des .razor d'origine, nomme ci-dessous,
// pour que l'ecran React filtre et affiche EXACTEMENT ce que Blazor affichait.

/** `d.ToString("dd/MM/yyyy")` d'une date tenue au format d'un <input type="date"> (aaaa-mm-jj). */
export function dateCourte(iso: string): string {
  const [annee, mois, jour] = iso.split('-')
  return `${jour}/${mois}/${annee}`
}

/**
 * `texte.Contains(recherche, StringComparison.OrdinalIgnoreCase)` : la casse
 * est ignoree, l'ordre des lettres et les accents ne le sont pas.
 */
export function contientSansCasse(texte: string, recherche: string): boolean {
  return texte.toUpperCase().includes(recherche.toUpperCase())
}

/** `string.IsNullOrWhiteSpace(texte)` : vide ou fait seulement de blancs. */
export function estBlanc(texte: string): boolean {
  return texte.trim().length === 0
}

// `Order()` sur des chaines compare sous la culture du poste (fr-FR), comme
// Intl.Collator : « Mme LEROY » vient apres « M. DUVAL », et un accent ne
// renvoie pas le mot en fin de liste comme le ferait un tri par code.
const comparaisonFr = new Intl.Collator('fr-FR').compare

/** `valeurs.Distinct().Order()` : chaque valeur une fois, dans l'ordre fr-FR. */
export function distinctsTries(valeurs: Iterable<string>): string[] {
  return [...new Set(valeurs)].sort(comparaisonFr)
}

/**
 * La valeur que le `@bind` d'un `int` retenait d'une saisie : le nombre entier
 * ecrit, ou null si le texte n'en est pas un. Une saisie illisible ne remet
 * pas la valeur a zero : l'appelant garde alors la derniere valeur lisible.
 */
export function entierLisible(texte: string): number | null {
  const t = texte.trim()
  return /^[-+]?\d+$/.test(t) ? Number.parseInt(t, 10) : null
}
