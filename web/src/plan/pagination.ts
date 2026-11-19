// SAISIE GUIDEE - le defilement de l'ACCORDEON des etapes (proposition 6 « En-
// tete en bas » du /draft du 19/11/2026, conv-2022 ; avant, des pages cote a
// cote calees par scroll-snap). Les etapes sont l'une sous l'autre, dans UNE
// colonne qui defile a la verticale. Le navigateur est le seul a savoir
// laquelle est en haut de la vue : c'est lui qui le dit au C# (geste
// « etapeVisible »), pour que le pied dise « etape N / 7 ».

/** Marge sous le haut de la zone : une ligne d'etape arrivee a 8 px du bord compte deja comme « en haut ». */
const MARGE_HAUT = 8

function etapesDe(zone: HTMLElement): HTMLElement[] {
  return [...zone.querySelectorAll<HTMLElement>('[data-etape]')]
}

/**
 * Amene l'etape `index` en vue : sa ligne en haut de la zone. Une etape DEJA
 * entierement visible ne bouge pas -- deplier une etape courte au milieu de
 * l'ecran ne doit pas faire sauter la page.
 */
export function allerEtape(zone: HTMLElement | null, index: number, doux = true): void {
  if (!zone) return
  const etape = etapesDe(zone)[index]
  if (!etape) return
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

/**
 * Suit l'etape en haut de la zone et la remonte a chaque changement. Rend la
 * fonction qui arrete le suivi.
 */
export function suivreEtapeVisible(zone: HTMLElement, etapeVisible: (index: number) => void): () => void {
  let dernier = -1
  let enAttente = false
  const mesurer = () => {
    enAttente = false
    const index = etapeEnHaut(zone)
    if (index === dernier) return
    dernier = index
    etapeVisible(index)
  }
  const auDefilement = () => {
    if (enAttente) return
    enAttente = true
    requestAnimationFrame(mesurer)
  }
  zone.addEventListener('scroll', auDefilement, { passive: true })
  mesurer()
  return () => zone.removeEventListener('scroll', auDefilement)
}
