// SAISIE GUIDEE - le defilement de l'ACCORDEON des etapes (proposition 6 « En-
// tete en bas » du /draft du 19/11/2026, conv-2022 ; avant, des pages cote a
// cote calees par scroll-snap). Les etapes sont l'une sous l'autre, dans UNE
// colonne qui defile a la verticale. Le navigateur est le seul a savoir
// laquelle est en haut de la vue : c'est lui qui le dit au C# (geste
// « etapeVisible »), pour que le pied dise « etape N / 7 ».

/** Marge sous le haut de la zone : une ligne d'etape arrivee a 8 px du bord compte deja comme « en haut ». */
const MARGE_HAUT = 8

/**
 * L'etape DEMANDEE par le dernier geste (piece ouverte, lien vers un defaut,
 * ligne depliee), par zone. Elle reste l'etape courante tant que l'utilisateur
 * ne fait pas defiler lui-meme : en colonne large, la derniere etape ne peut pas
 * monter en haut de la zone (le defilement bute en bas), et le pied disait
 * « etape 3 / 7 » juste apres un clic qui ouvrait l'etape 7 (vu en capture,
 * 19/11/2026).
 */
const cibles = new WeakMap<HTMLElement, number>()

/** Les touches qui font defiler la zone quand le focus est sur une de ses lignes. */
const TOUCHES_DEFILEMENT = new Set(['PageUp', 'PageDown', 'Home', 'End', 'ArrowUp', 'ArrowDown'])

function etapesDe(zone: HTMLElement): HTMLElement[] {
  return [...zone.querySelectorAll<HTMLElement>('[data-etape]')]
}

/**
 * Amene l'etape `index` en vue : sa ligne en haut de la zone. Une etape DEJA
 * entierement visible ne bouge pas -- deplier une etape courte au milieu de
 * l'ecran ne doit pas faire sauter la page. Elle devient l'etape suivie (voir
 * `cibles`), meme quand la zone ne peut pas la monter jusqu'en haut.
 */
export function allerEtape(zone: HTMLElement | null, index: number, doux = true): void {
  if (!zone) return
  const etape = etapesDe(zone)[index]
  if (!etape) return
  cibles.set(zone, index)
  const haut = etape.offsetTop
  const bas = haut + etape.offsetHeight
  const vue = zone.scrollTop
  if (haut >= vue && bas <= vue + zone.clientHeight && zone.clientHeight > 0) return
  zone.scrollTo({ top: haut, behavior: doux ? 'smooth' : 'auto' })
}

/** L'etape dont la ligne est passee en haut de la zone (la derniere au-dessus du bord). */
export function etapeEnHaut(zone: HTMLElement): number {
  const bord = zone.scrollTop + MARGE_HAUT
  let index = 0
  for (const etape of etapesDe(zone)) {
    if (etape.offsetTop > bord) break
    index = Number(etape.dataset.etape ?? index)
  }
  return index
}

/** L'etape courante : celle que le dernier geste a demandee, sinon celle du haut. */
export function etapeSuivie(zone: HTMLElement): number {
  return cibles.get(zone) ?? etapeEnHaut(zone)
}

/**
 * Suit l'etape courante (`etapeSuivie`) et la remonte a chaque changement. Un
 * defilement fait par l'utilisateur (molette, doigt, barre de defilement,
 * touches) rend la main a l'etape du haut ; le defilement doux joue par
 * `allerEtape` ne la rend pas. Rend la fonction qui arrete le suivi.
 */
export function suivreEtapeVisible(zone: HTMLElement, etapeVisible: (index: number) => void): () => void {
  let dernier = -1
  let enAttente = false
  const mesurer = () => {
    enAttente = false
    const index = etapeSuivie(zone)
    if (index === dernier) return
    dernier = index
    etapeVisible(index)
  }
  const auDefilement = () => {
    if (enAttente) return
    enAttente = true
    requestAnimationFrame(mesurer)
  }
  const lacher = () => { cibles.delete(zone) }
  // Un appui sur la zone elle-meme, pas sur une ligne ni un champ : c'est sa barre de defilement.
  const auPointeur = (e: Event) => { if (e.target === zone) lacher() }
  const auClavier = (e: KeyboardEvent) => {
    const cible = e.target as HTMLElement | null
    const saisie = cible?.closest('input, select, textarea') != null
    if (TOUCHES_DEFILEMENT.has(e.key) && !saisie) lacher()
  }
  zone.addEventListener('scroll', auDefilement, { passive: true })
  zone.addEventListener('wheel', lacher, { passive: true })
  zone.addEventListener('touchmove', lacher, { passive: true })
  zone.addEventListener('pointerdown', auPointeur)
  zone.addEventListener('keydown', auClavier)
  mesurer()
  return () => {
    zone.removeEventListener('scroll', auDefilement)
    zone.removeEventListener('wheel', lacher)
    zone.removeEventListener('touchmove', lacher)
    zone.removeEventListener('pointerdown', auPointeur)
    zone.removeEventListener('keydown', auClavier)
  }
}
