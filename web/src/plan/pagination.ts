// SAISIE GUIDEE - les etapes a l'horizontale, une page par etape (ex-
// suivrePagination / allerEtape de wwwroot/rig-accueil.js, portes au lot 2 du
// plan « finir le passage a React », 19/11/2026). Chacune se cale comme une
// page (scroll-snap, cote CSS). Le navigateur est le seul a savoir laquelle est
// reellement visible : c'est lui qui le dit au C# (geste « etapeVisible »),
// pour que la frise suive.

/** Fait defiler la zone jusqu'a l'etape `index` (la page se cale d'elle-meme). */
export function allerEtape(zone: HTMLElement | null, index: number, doux = true): void {
  if (!zone) return
  zone.scrollTo({ left: index * zone.clientWidth, behavior: doux ? 'smooth' : 'auto' })
}

/**
 * Suit l'etape visible de la zone et tourne les pages a la molette. Rend la
 * fonction qui arrete le suivi (arreterPagination).
 */
export function suivrePagination(zone: HTMLElement, etapeVisible: (index: number) => void): () => void {
  // Les etapes sont COTE A COTE : chaque page fait la largeur de la zone et se
  // cale d'elle-meme. L'etape visible se deduit donc de la position
  // horizontale, arrondie a la page la plus proche.
  let dernier = -1
  let enAttente = false
  const indexVisible = () => Math.round(zone.scrollLeft / Math.max(1, zone.clientWidth))

  const mesurer = () => {
    enAttente = false
    const index = indexVisible()
    if (index === dernier) return
    dernier = index
    etapeVisible(index)
  }
  const auDefilement = () => {
    if (enAttente) return
    enAttente = true
    requestAnimationFrame(mesurer)
  }

  // La molette verticale : elle fait d'abord defiler l'etape elle-meme
  // (formulaire plus haut que la vue), et seulement arrivee au bout elle TOURNE
  // la page vers l'etape suivante ou precedente. Un geste = une page : un
  // verrou court absorbe l'inertie des pave tactiles.
  let verrouJusqu = 0
  const aLaMolette = (ev: WheelEvent) => {
    // Ctrl+molette = ZOOM de la fenetre, prioritaire : on ne touche a rien,
    // sinon le preventDefault plus bas annule le zoom natif (defaut du 15/09).
    if (ev.ctrlKey) return
    if (Math.abs(ev.deltaX) > Math.abs(ev.deltaY)) return // geste deja horizontal
    const page = ev.target instanceof Element ? ev.target.closest<HTMLElement>('[data-etape]') : null
    const sens = ev.deltaY > 0 ? 1 : -1
    if (page && page.scrollHeight > page.clientHeight + 1) {
      const auBout = sens > 0
        ? page.scrollTop + page.clientHeight >= page.scrollHeight - 1
        : page.scrollTop <= 0
      if (!auBout) return
    }
    ev.preventDefault()
    const maintenant = performance.now()
    if (maintenant < verrouJusqu) return
    verrouJusqu = maintenant + 450
    const total = zone.querySelectorAll('[data-etape]').length
    const cible = Math.min(total - 1, Math.max(0, indexVisible() + sens))
    allerEtape(zone, cible, true)
  }

  zone.addEventListener('scroll', auDefilement, { passive: true })
  zone.addEventListener('wheel', aLaMolette, { passive: false })
  mesurer()
  return () => {
    zone.removeEventListener('scroll', auDefilement)
    zone.removeEventListener('wheel', aLaMolette)
  }
}
