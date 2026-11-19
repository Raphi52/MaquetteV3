// LE GLISSER D'UN ONGLET : ce que seul le navigateur sait faire (ex-« BARRE
// D ONGLETS » de wwwroot/rig-accueil.js, porte au lot 2 du plan « finir le
// passage a React », 19/11/2026). Les regles (reordonner, arracher) restent en
// C# (SessionPlanTravail.GlisseOnglet, EtatOnglets, ArrachageOnglet) : c'est la
// qu'elles sont verifiables ; la page ne fait que mesurer et suivre le pointeur.

/** Ce que la page mesure pour le C# au debut du geste (SessionPlanTravail.MesureBarre). */
export interface MesureBarre {
  /** Largeur et hauteur de la zone visible, puis le bas de la barre d'onglets. */
  page: number[]
  /** La place [gauche, droite] de chaque onglet, dans l'ordre de la barre. */
  cases: number[][]
}

/**
 * Mesure, au debut du geste : la taille de la zone visible (pour savoir quand
 * le pointeur en sort) et la place de chaque onglet (pour savoir ou l'inserer).
 */
export function mesurerOnglets(doc: Document = document): MesureBarre {
  const cases: number[][] = []
  doc.querySelectorAll('[data-rig-onglet]').forEach((el) => {
    const r = el.getBoundingClientRect()
    cases.push([r.left, r.right])
  })
  return {
    // 3e valeur : le bas de la barre d'onglets -- au-dela, l'onglet s'arrache.
    page: [doc.documentElement.clientWidth, doc.documentElement.clientHeight,
           doc.getElementById('rig-vonglets')?.getBoundingClientRect().bottom ?? 1e9],
    cases,
  }
}

interface Suivi {
  code: string
  prise: number
  departX: number
  departY: number
  actif: boolean
}

let suivi: Suivi | null = null

function ongletSuivi(doc: Document): HTMLElement | null {
  return suivi ? doc.querySelector<HTMLElement>(`[data-rig-onglet="${CSS.escape(suivi.code)}"]`) : null
}

/**
 * Le geste de l'onglet commence (pointerdown) : le pointeur est CAPTURE par
 * l'onglet. Sans capture, le navigateur cesse d'envoyer les mouvements des
 * qu'on quitte l'element, et l'arrachage hors de la fenetre serait impossible.
 * La croix n'est PAS un debut de glisser : capturer le pointeur sur l'onglet
 * redirigerait son clic vers l'onglet, et la croix ne fermerait rien.
 */
export function debuterSuivi(onglet: HTMLElement, e: { pointerId: number, clientX: number, clientY: number, target: EventTarget | null }): void {
  if (e.target instanceof Element && e.target.closest('.rig-vonglet__fermer')) return
  try { onglet.setPointerCapture(e.pointerId) } catch { /* pointeur deja relache */ }
  const r = onglet.getBoundingClientRect()
  suivi = { code: onglet.dataset.rigOnglet ?? '', prise: e.clientX - r.left, departX: e.clientX, departY: e.clientY, actif: false }
}

/**
 * RETOUR VISUEL, comme un navigateur : l'onglet SUIT le pointeur. Fait ici, a
 * chaque mouvement : un aller-retour au C# par pixel donnerait un onglet
 * saccade. Le calcul repart de la place NATURELLE de l'onglet a chaque
 * mouvement, donc il reste juste meme quand le C# le deplace dans la barre.
 */
export function suivrePointeur(e: { clientX: number, clientY: number }, doc: Document = document): void {
  if (!suivi) return
  if (!suivi.actif && Math.abs(e.clientX - suivi.departX) + Math.abs(e.clientY - suivi.departY) < 6) return
  suivi.actif = true
  const onglet = ongletSuivi(doc)
  if (!onglet) return
  onglet.style.transform = ''
  const naturel = onglet.getBoundingClientRect().left
  onglet.style.transform = `translateX(${e.clientX - suivi.prise - naturel}px)`
  onglet.classList.add('rig-vonglet--suit')
}

/** Fin du geste (lacher, ou onglet parti dans sa fenetre) : l'onglet reprend sa place. */
export function finirSuivi(doc: Document = document): void {
  const onglet = ongletSuivi(doc)
  if (onglet) { onglet.style.transform = ''; onglet.classList.remove('rig-vonglet--suit') }
  suivi = null
}

/**
 * Ecoute posee sur le DOCUMENT, en capture, comme dans rig-accueil.js : le
 * mouvement doit etre suivi meme quand React re-rend l'onglet sous le pointeur.
 * Rend la fonction qui retire l'ecoute.
 */
export function ecouterGlisse(doc: Document = document): () => void {
  const surBas = (e: PointerEvent) => {
    const onglet = e.target instanceof Element ? e.target.closest<HTMLElement>('[data-rig-onglet]') : null
    if (onglet) debuterSuivi(onglet, e)
  }
  const surMouvement = (e: PointerEvent) => suivrePointeur(e, doc)
  const surFin = () => finirSuivi(doc)
  doc.addEventListener('pointerdown', surBas, true)
  doc.addEventListener('pointermove', surMouvement, true)
  doc.addEventListener('pointerup', surFin, true)
  doc.addEventListener('pointercancel', surFin, true)
  return () => {
    doc.removeEventListener('pointerdown', surBas, true)
    doc.removeEventListener('pointermove', surMouvement, true)
    doc.removeEventListener('pointerup', surFin, true)
    doc.removeEventListener('pointercancel', surFin, true)
  }
}
