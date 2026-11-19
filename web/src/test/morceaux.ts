// Les MORCEAUX DE TEXTE d'un element, dans l'ordre : chaque noeud de texte enfant
// tel quel, chaque element enfant sous la forme « <balise> ».
//
// Pourquoi : Razor cree un noeud de texte par @expression et un par texte ecrit
// entre elles (« @n dossier(s) » -> « 3 », « dossier(s) »), et Chromium mesure
// chaque noeud a part. Un ecran React qui assemble ces morceaux en une seule
// chaine se dessine decale d'une fraction de pixel (web/README.md, « Ajouts du
// lot 3 »). Les tests d'ecran verifient donc le decoupage, pas seulement le texte.

export function morceaux(el: Element | null): string[] {
  if (!el) throw new Error('morceaux : element absent')
  return [...el.childNodes].map((n) =>
    n.nodeType === Node.TEXT_NODE ? (n as Text).data : `<${(n as Element).tagName.toLowerCase()}>`)
}
