import { cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ClasseurArbre, frise, refusFichier, type LigneArbreVue, type ProprietesClasseurArbre, type TriClasseur } from './ClasseurArbre'

afterEach(cleanup)

// Une ligne de l'arbre ; `enfants` non vide = dossier.
function l(id: string, texte: string, o: Partial<LigneArbreVue> = {}): LigneArbreVue {
  const enfants = o.enfants ?? []
  return {
    id, niveau: 0, texte, icone: o.icone ?? (enfants.length || o.compte !== undefined ? 'dossier' : 'pdf'),
    actif: false, chemin: false, depliable: enfants.length > 0, ouvert: enfants.length > 0, renomme: false,
    compte: null, enfants, vide: false, groupe: 0, niveauGroupe: 0, date: '', pages: 0, derniere: '', replie: false, ...o,
  }
}

// Le dossier 004176, comme le pont le rend : Dossier RCS (Kbis, fiche, Depots d'actes
// ouverte avec deux depots) et Judiciaire (Instances ouverte, Ordonnances fermee).
function affaire(o: { choisi?: string, filtre?: boolean } = {}) {
  const choisi = o.choisi ?? 'cert'
  const g2 = { groupe: 2, niveauGroupe: 2 }
  const g6 = { groupe: 6, niveauGroupe: 2 }
  const p = (id: string, texte: string, date: string, pages: number, g: object, niveau: number) =>
    l(id, texte, { ...g, niveau, date, pages, actif: id === choisi })
  const actes = l('da', "Depots d'actes", { ...g2, niveau: 2, compte: 3, derniere: '15/10/2024', chemin: choisi === 'cert', enfants: [
    l('info', 'Informations des depots', { ...g2, niveau: 3, icone: 'note' }),
    l('d1', 'Depot n 2024/0141', { ...g2, niveau: 3, compte: 1, enfants: [p('bail', 'Bail commercial', '12/03/2024', 9, g2, 4)] }),
    l('d2', 'Depot n A2024/038654', { ...g2, niveau: 3, compte: 2, chemin: choisi === 'cert', enfants: [
      p('pv', 'Extrait de proces-verbal du conseil de surveillance du 03/10/2024', '15/10/2024', 3, g2, 4),
      l('gen', 'Documents generes lors de la formalite', { ...g2, niveau: 4, compte: 1, chemin: choisi === 'cert',
        enfants: [p('cert', "Certificat de depot d'actes", '15/10/2024', 1, g2, 5)] }),
    ] }),
  ] })
  const rcs = l('rcs', 'Dossier RCS', { niveau: 1, compte: 21, chemin: choisi !== 'rapport', enfants: [
    p('kbis', 'Kbis a jour', '19/11/2026', 1, {}, 2),
    l('fiche', 'Informations du dossier', { niveau: 2, icone: 'note' }),
    actes,
    l('pj', 'Pieces jointes', { groupe: 1, niveauGroupe: 2, niveau: 2, compte: 4, derniere: '15/09/2026', ouvert: false }),
  ] })
  const instances = l('in', 'Instances', { ...g6, niveau: 2, compte: 7, derniere: '11/07/2023', chemin: choisi === 'rapport', enfants: [
    l('i1', 'Instance n 2022J00114 - AMIENS BILLARDS / SCI DU PARC', { ...g6, niveau: 3, compte: 3, enfants: [
      p('assig', 'Assignation en paiement', '14/02/2022', 8, g6, 4),
      l('mes', 'Mesures d instruction', { ...g6, niveau: 4, compte: 1, enfants: [p('rapport', 'Rapport d expertise', '03/03/2023', 42, g6, 5)] }),
      l('dec', 'Decisions judiciaires', { ...g6, niveau: 4, compte: 1, enfants: [p('jug', '2022J00114-JUG-GONON-11/07/2023 17:50:58', '11/07/2023', 2, g6, 5)] }),
    ] }),
  ] })
  const jud = l('jud', 'Judiciaire', { niveau: 1, compte: 23, chemin: choisi === 'rapport', enfants: [
    instances,
    l('ord', 'Ordonnances', { groupe: 1, niveauGroupe: 2, niveau: 2, compte: 6, derniere: '02/05/2024', ouvert: false }),
  ] })
  const arbre = l('racine', '004176 Modification - SCI DU PARC', { compte: 44, enfants: [rcs, jud] })
  const chemin = choisi === 'rapport'
    ? ['004176', 'Judiciaire', 'Instances', 'Instance n 2022J00114 - AMIENS BILLARDS / SCI DU PARC', 'Mesures d instruction', 'Rapport d expertise']
    : ['004176', 'Dossier RCS', "Depots d'actes", 'Depot n A2024/038654', 'Documents generes lors de la formalite', "Certificat de depot d'actes"]
  return { arbre, chemin, filtre: o.filtre ? 'certif' : '' }
}

function rendre(donnees = affaire(), tri: TriClasseur = 'date') {
  const gestes: [string, unknown][] = []
  const p: ProprietesClasseurArbre = {
    ...donnees,
    tri,
    trier: (t) => gestes.push(['trier', t]),
    activer: (id) => gestes.push(['activer', id]),
    rubrique: (id) => gestes.push(['rubrique', id]),
    replier: (id) => gestes.push(['replier', id]),
    renommer: (r) => gestes.push(['renommer', r]),
    menuContextuel: vi.fn((s) => gestes.push(['menu', s.id])),
    joindre: (f) => gestes.push(['joindre', f]),
  }
  const r = render(<ClasseurArbre {...p} />)
  return { ...r, gestes, p }
}

// Les icones (traces SVG en texte) ajoutent des blancs : le texte lu est nettoye.
const textes = (c: Element, sel: string) => [...c.querySelectorAll(sel)].map((e) => e.textContent!.trim())

describe('ClasseurArbre : la bascule et le rappel', () => {
  it('une bascule par domaine, avec son nombre de pieces ; le classeur de la piece choisie est montre', () => {
    const { container } = rendre()
    expect(textes(container, '.rig-classeur__domaine')).toEqual(['Dossier RCS 21', 'Judiciaire 23'])
    expect(container.querySelector('.rig-classeur__domaine--on')!.textContent).toBe('Dossier RCS 21')
    expect(textes(container, '.rig-classeur__carte .rig-classeur__tete .rig-arbre__texte')).toEqual(["Depots d'actes", 'Pieces jointes'])
    // La piece ouverte est de ce cote : pas de rappel.
    expect(container.querySelector('.rig-classeur__rappel')).toBeNull()
  })

  it('l\'autre classeur, et le rappel or qui ramene a la piece ouverte', () => {
    const { container } = rendre()
    fireEvent.click(container.querySelectorAll('.rig-classeur__domaine')[1]!)
    expect(container.querySelector('.rig-classeur__domaine--on')!.textContent).toBe('Judiciaire 23')
    expect(textes(container, '.rig-classeur__tete .rig-arbre__texte')).toEqual(['Instances', 'Ordonnances'])
    // Judiciaire n'a aucune piece hors rubrique : pas de carte « hors rubrique ».
    expect(container.querySelector('.rig-classeur__hors')).toBeNull()
    const rappel = container.querySelector('.rig-classeur__rappel')!
    expect(rappel.textContent).toBe("Certificat de depot d'actes est ouvertDossier RCS ›")
    fireEvent.click(rappel)
    expect(container.querySelector('.rig-classeur__domaine--on')!.textContent).toBe('Dossier RCS 21')
    expect(container.querySelector('.rig-classeur__rappel')).toBeNull()
  })

  it('choisir une autre piece (historique, palette) montre SON classeur, meme apres une bascule', () => {
    const { container, rerender, p } = rendre()
    fireEvent.click(container.querySelectorAll('.rig-classeur__domaine')[1]!)
    rerender(<ClasseurArbre {...p} {...affaire({ choisi: 'cert' })} chemin={['004176', 'Dossier RCS', 'Kbis a jour']} />)
    expect(container.querySelector('.rig-classeur__domaine--on')!.textContent).toBe('Dossier RCS 21')
  })

  it('pendant une recherche, la bascule compte les pieces trouvees', () => {
    const { container } = rendre(affaire({ filtre: true }))
    // Dossier RCS rend 4 pieces dans ce jeu (Kbis, Bail, PV, Certificat ; la fiche ne compte pas), Judiciaire 3.
    expect(textes(container, '.rig-classeur__domaine')).toEqual(['Dossier RCS 4', 'Judiciaire 3'])
  })
})

describe('ClasseurArbre : les cartes', () => {
  it('hors rubrique : une carte au nom du domaine ; la fiche ne compte pas comme piece', () => {
    const { container, gestes } = rendre()
    const hors = container.querySelector('.rig-classeur__hors')!
    expect(hors.querySelector('.rig-classeur__hors-titre')!.textContent).toBe('Dossier RCS 1 piece hors rubrique')
    expect(textes(hors, '.rig-arbre__ligne .rig-arbre__texte')).toEqual(['Kbis a jour', 'Informations du dossier'])
    expect(textes(hors, '.rig-classeur__date')).toEqual(['19/11/2026'])
    fireEvent.click(hors.querySelectorAll('.rig-arbre__ligne')[1]!)
    expect(gestes).toEqual([['activer', 'fiche']])
  })

  it('une carte fermee : filet de sa teinte, nombre de pieces et date de la plus recente ; un clic l\'ouvre', () => {
    const { container, gestes } = rendre()
    const pj = [...container.querySelectorAll('.rig-classeur__carte')].find((c) => c.getAttribute('aria-label') === 'Pieces jointes')!
    expect(pj.className).toBe('rig-classeur__carte')
    expect((pj as HTMLElement).style.getPropertyValue('--g')).toBe('var(--rig-groupe-1)')
    expect(pj.querySelector('.rig-classeur__meta')!.textContent).toBe('4 pieces · 15/09/2026')
    expect(pj.querySelector('.rig-classeur__corps')).toBeNull()
    fireEvent.click(pj.querySelector('.rig-classeur__tete')!)
    expect(gestes).toEqual([['rubrique', 'pj']])
  })

  it('les trois points ouvrent le menu sans ouvrir la carte', () => {
    const { container, gestes } = rendre()
    fireEvent.click(container.querySelector('.rig-classeur__carte .rig-classeur__tete .rig-arbre__actions')!)
    expect(gestes).toEqual([['menu', 'da']])
  })
})

describe('ClasseurArbre : la carte ouverte, par date et par dossier', () => {
  it('« Par date » d\'office : la frise par annee, la plus recente en tete, la fiche a la fin ; la periode reste dans l\'ordre du calendrier', () => {
    const { container } = rendre()
    const carte = container.querySelector('.rig-classeur__carte--ouverte')!
    expect(carte.querySelector('.rig-classeur__periode')!.textContent).toBe('du 12/03/2024 au 15/10/2024')
    expect(carte.querySelector('.rig-classeur__tri--on')!.textContent).toBe('Par date')
    const frise = carte.querySelector('.rig-classeur__frise')!
    expect([...frise.children].map((e) => e.className.includes('rig-classeur__an') ? e.textContent : e.querySelector('.rig-classeur__jour')!.textContent))
      .toEqual(['2024', '15/10', '15/10', '12/03', 'sans date', ''])
    const ev = [...frise.querySelectorAll('.rig-classeur__ev')]
    // A date egale (le 15/10), l'ordre de l'arbre : le PV avant le Certificat.
    expect(ev.map((e) => e.querySelector('em')!.textContent)).toEqual([
      'Depot n A2024/038654 · 3 p.', 'Documents generes lors de la formalite · 1 p.', 'Depot n 2024/0141 · 9 p.', "Depots d'actes"])
    // La piece choisie y est marquee, comme dans l'arbre.
    expect(ev[1]!.className).toContain('rig-arbre__ligne--actif')
  })

  it('les DECISIONS (rangees sous « Decisions judiciaires ») passent en or ; les annees vont de la plus recente a la plus ancienne', () => {
    const { container } = rendre(affaire({ choisi: 'rapport' }))
    const ev = [...container.querySelectorAll('.rig-classeur__carte--ouverte .rig-classeur__ev')]
    expect(ev.map((e) => e.querySelector('.rig-arbre__texte')!.textContent))
      .toEqual(['2022J00114-JUG-GONON-11/07/2023 17:50:58', 'Rapport d expertise', 'Assignation en paiement'])
    expect(ev.map((e) => e.className.includes('rig-classeur__ev--decision'))).toEqual([true, false, false])
    expect(textes(container, '.rig-classeur__carte--ouverte .rig-classeur__an')).toEqual(['2023', '2022'])
    expect(container.querySelector('.rig-classeur__carte--ouverte .rig-classeur__periode')!.textContent).toBe('du 14/02/2022 au 11/07/2023')
  })

  it('le classement vient du pont : un clic sur l\'autre le lui demande, l\'ecran ne bascule pas seul', () => {
    const { container, gestes } = rendre()
    const bouton = (t: string) => [...container.querySelectorAll('.rig-classeur__tri button')].find((b) => b.textContent === t)!
    fireEvent.click(bouton('Par date'))
    expect(gestes).toEqual([])
    fireEvent.click(bouton('Par dossier'))
    expect(gestes).toEqual([['trier', 'dossier']])
    expect(container.querySelector('.rig-classeur__tri--on')!.textContent).toBe('Par date')
  })

  it('« Par dossier » retenu par le pont : la carte s\'ouvre directement ainsi', () => {
    const { container } = rendre(affaire(), 'dossier')
    expect(container.querySelector('.rig-classeur__tri--on')!.textContent).toBe('Par dossier')
    expect(container.querySelector('.rig-classeur__frise')).toBeNull()
  })

  it('« Par dossier » : chaque sous-dossier en intertitre, avec ce qu\'il range un cran plus loin', () => {
    const { container, gestes } = rendre(affaire(), 'dossier')
    const corps = container.querySelector('.rig-classeur__carte--ouverte .rig-classeur__corps')!
    expect([...corps.querySelectorAll('.rig-classeur__it')].map((e) =>
      ['.rig-arbre__chevron', '.rig-arbre__texte', '.rig-classeur__it-n'].map((s) => e.querySelector(s)!.textContent).join(' ')))
      .toEqual(['▸ Depot n 2024/0141 1', '▸ Depot n A2024/038654 2', '▸ Documents generes lors de la formalite 1'])
    expect([...corps.querySelectorAll('.rig-classeur__it')].map((e) => (e as HTMLElement).style.getPropertyValue('--p'))).toEqual(['0', '0', '1'])
    expect([...corps.querySelectorAll('.rig-classeur__it')].map((e) => e.className.includes('--chemin'))).toEqual([false, true, true])
    const cert = [...corps.querySelectorAll('.rig-arbre__ligne')].find((e) => e.textContent!.trim().startsWith('Certificat'))!
    expect((cert as HTMLElement).style.getPropertyValue('--p')).toBe('2')
    expect(cert.querySelector('.rig-classeur__pages')!.textContent).toBe('1 p.')
    fireEvent.click(cert)
    expect(gestes).toEqual([['activer', 'cert']])
  })

  it('un element en cours de renommage montre sa saisie, Echap l\'abandonne', () => {
    const d = affaire()
    d.arbre.enfants[0]!.enfants[0] = { ...d.arbre.enfants[0]!.enfants[0]!, renomme: true }
    const { container, gestes } = rendre(d)
    const saisie = container.querySelector('.rig-classeur__hors .rig-arbre__saisie') as HTMLInputElement
    expect(saisie.value).toBe('Kbis a jour')
    fireEvent.keyDown(saisie, { key: 'Escape' })
    expect(gestes).toEqual([['renommer', { id: 'kbis', texte: '' }]])
  })
})

describe('ClasseurArbre : replier un sous-dossier en « Par dossier »', () => {
  const corps = (c: Element) => c.querySelector('.rig-classeur__carte--ouverte .rig-classeur__corps')!
  const intertitre = (c: Element, debut: string) =>
    [...corps(c).querySelectorAll('.rig-classeur__it')].find((e) => e.querySelector('.rig-arbre__texte')!.textContent!.startsWith(debut))!
  const pieces = (c: Element) => textes(corps(c), '.rig-arbre__ligne .rig-arbre__texte')
  // L'affaire telle que le pont la rend avec ces sous-dossiers replies.
  const avecReplis = (ids: string[]) => {
    const d = affaire()
    const marquer = (l: LigneArbreVue): LigneArbreVue => ({ ...l, replie: ids.includes(l.id), enfants: l.enfants.map(marquer) })
    return { ...d, arbre: marquer(d.arbre) }
  }

  it('un clic sur l\'intertitre demande au pont de le replier ; l\'ecran suit la vue, il ne replie pas seul', () => {
    const { container, gestes } = rendre(affaire(), 'dossier')
    expect(intertitre(container, 'Depot n A2024').getAttribute('aria-expanded')).toBe('true')
    fireEvent.click(intertitre(container, 'Depot n A2024'))
    expect(gestes).toEqual([['replier', 'd2']])
    expect(intertitre(container, 'Depot n A2024').getAttribute('aria-expanded')).toBe('true')
  })

  it('un sous-dossier replie par le pont : son intertitre reste (chevron, nombre), ce qu\'il range se cache, sous-dossiers compris', () => {
    const { container, gestes } = rendre(avecReplis(['d2']), 'dossier')
    const d2 = intertitre(container, 'Depot n A2024')
    expect(d2.getAttribute('aria-expanded')).toBe('false')
    expect(d2.querySelector('.rig-arbre__chevron--ouvert')).toBeNull()
    expect(d2.querySelector('.rig-classeur__it-n')!.textContent).toBe('2')
    expect(pieces(container)).toEqual(['Informations des depots', 'Bail commercial'])
    expect(textes(corps(container), '.rig-classeur__it .rig-arbre__texte')).toEqual(['Depot n 2024/0141', 'Depot n A2024/038654'])
    // Un second clic demande de le rouvrir : le meme geste, le pont bascule.
    fireEvent.click(d2)
    expect(gestes).toEqual([['replier', 'd2']])
  })

  it('les trois points d\'un intertitre ouvrent le menu sans le replier', () => {
    const { container, gestes } = rendre(affaire(), 'dossier')
    fireEvent.click(intertitre(container, 'Depot n 2024').querySelector('.rig-arbre__actions')!)
    expect(gestes).toEqual([['menu', 'd1']])
  })

  it('« Par date » ignore les replis : la frise garde toutes les pieces', () => {
    const { container } = rendre(avecReplis(['d1', 'd2']), 'date')
    expect(container.querySelectorAll('.rig-classeur__ev')).toHaveLength(4)
  })
})

describe('ClasseurArbre : joindre un fichier (glisse depuis l\'explorateur ou choisi)', () => {
  const pdf = (nom = 'Attestation URSSAF.pdf', texte = '%PDF-1.4 deux pages') => new File([texte], nom, { type: 'application/pdf' })
  // Ce que le navigateur passe a un glisser de FICHIERS depuis l'explorateur.
  const glisse = (fichiers: File[]) => ({ dataTransfer: { types: ['Files'], files: fichiers, dropEffect: 'none' } })
  const carte = (c: Element, nom: string) =>
    [...c.querySelectorAll('.rig-classeur__carte')].find((e) => e.getAttribute('aria-label') === nom)!

  it('un PDF glisse sur la carte FERMEE « Pieces jointes » : elle s\'eclaire, puis le pont recoit son nom et son contenu en base64', async () => {
    const { container, gestes } = rendre()
    const pj = carte(container, 'Pieces jointes')
    // Sans preventDefault au survol, le navigateur refuse le depot (et ouvrirait le fichier).
    expect(fireEvent.dragOver(pj, glisse([pdf()]))).toBe(false)
    expect(pj.className).toContain('rig-classeur__depot')
    fireEvent.drop(pj, glisse([pdf()]))
    expect(pj.className).not.toContain('rig-classeur__depot')
    await waitFor(() => expect(gestes).toEqual([
      ['joindre', { id: 'pj', nom: 'Attestation URSSAF.pdf', contenu: btoa('%PDF-1.4 deux pages') }]]))
    // Deposer n'ouvre pas la carte : c'est le pont qui ouvre la piece jointe.
    expect(gestes.some(([g]) => g === 'rubrique')).toBe(false)
  })

  it('un glisser qui ne porte pas de fichier (un texte, un onglet) n\'est pas accepte', () => {
    const { container } = rendre()
    const pj = carte(container, 'Pieces jointes')
    expect(fireEvent.dragOver(pj, { dataTransfer: { types: ['text/plain'], files: [] } })).toBe(true)
    expect(pj.className).not.toContain('rig-classeur__depot')
  })

  it('sur l\'intertitre d\'un sous-dossier, le fichier va dans CE sous-dossier, pas dans la rubrique', async () => {
    const { container, gestes } = rendre(affaire(), 'dossier')
    const d2 = [...container.querySelectorAll('.rig-classeur__it')].find((e) => e.textContent!.includes('A2024/038654'))!
    fireEvent.dragOver(d2, glisse([pdf()]))
    expect(d2.className).toContain('rig-classeur__depot')
    expect(carte(container, "Depots d'actes").className).not.toContain('rig-classeur__depot')
    // Des OCTETS, pas une chaine : un File fait d'une chaine l'encode en UTF-8 (0x89 -> C2 89).
    const png = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0])], 'scan.png', { type: 'image/png' })
    fireEvent.drop(d2, glisse([png]))
    await waitFor(() => expect(gestes.map(([g, f]) => [g, (f as { id: string }).id])).toEqual([['joindre', 'd2']]))
  })

  it('sur la carte « hors rubrique », le fichier va sous le domaine', async () => {
    const { container, gestes } = rendre()
    fireEvent.drop(container.querySelector('.rig-classeur__hors')!, glisse([pdf()]))
    await waitFor(() => expect(gestes.map(([g, f]) => [g, (f as { id: string }).id])).toEqual([['joindre', 'rcs']]))
  })

  it('un type non accepte est refuse EN CLAIR, sans rien envoyer ; le PDF depose avec lui passe', async () => {
    const { container, gestes } = rendre()
    const notes = new File(['bonjour'], 'notes.docx')
    fireEvent.drop(carte(container, 'Pieces jointes'), glisse([notes, pdf()]))
    // fix-ok: waitFor ne reessaie que sur une exception -- un querySelector nul rendait null aussitot (mesure : TypeError ligne 308).
    const refus = await waitFor(() => {
      const r = container.querySelector('[role="alert"]')
      expect(r).not.toBeNull()
      return r!
    })
    expect(refus.textContent).toContain("« notes.docx » n'est pas joint : seuls les PDF et les images JPG ou PNG sont acceptes.")
    expect(gestes.map(([g, f]) => [g, (f as { nom: string }).nom])).toEqual([['joindre', 'Attestation URSSAF.pdf']])
    fireEvent.click(refus.querySelector('button')!)
    expect(container.querySelector('[role="alert"]')).toBeNull()
  })

  it('un faux PDF (son contenu n\'en est pas un) est refuse aussi', async () => {
    const { container, gestes } = rendre()
    fireEvent.drop(carte(container, 'Pieces jointes'), glisse([pdf('faux.pdf', '<html></html>')]))
    await waitFor(() => expect(container.querySelector('[role="alert"]')!.textContent)
      .toBe("« faux.pdf » n'est pas un vrai PDF : il n'est pas joint.×"))
    expect(gestes).toEqual([])
  })

  it('« Joindre un fichier » dans la carte ouverte : le selecteur s\'ouvre, le fichier choisi va dans cette rubrique', async () => {
    const { container, gestes } = rendre()
    const selecteur = container.querySelector('input[type="file"]') as HTMLInputElement
    expect(selecteur.accept).toBe('.pdf,.jpg,.jpeg,.png')
    const ouvrir = vi.spyOn(selecteur, 'click')
    const bouton = container.querySelector('.rig-classeur__carte--ouverte .rig-classeur__joindre')!
    expect(bouton.textContent).toBe('Joindre un fichier')
    fireEvent.click(bouton)
    expect(ouvrir).toHaveBeenCalledTimes(1)
    fireEvent.change(selecteur, { target: { files: [pdf()] } })
    await waitFor(() => expect(gestes.map(([g, f]) => [g, (f as { id: string }).id])).toEqual([['joindre', 'da']]))
    // Une carte fermee n'a pas le bouton : elle s'ouvre d'abord (ou recoit un fichier glisse).
    expect(carte(container, 'Pieces jointes').querySelector('.rig-classeur__joindre')).toBeNull()
  })

  it('refusFichier : chaque refus a son jumeau accepte', () => {
    expect(refusFichier('notes.docx')).toContain('seuls les PDF')
    expect(refusFichier('notes.pdf')).toBeNull()
    expect(refusFichier('photo.JPEG', btoa('\xff\xd8\xff\xe0'))).toBeNull()
    expect(refusFichier('photo.jpeg', btoa('%PDF'))).toBe("« photo.jpeg » n'est pas un vrai JPG : il n'est pas joint.")
    expect(refusFichier('vide.png', '')).toBe("« vide.png » est vide : il n'est pas joint.")
    expect(refusFichier('scan.png', btoa('\x89PNG\r\n\x1a\n'))).toBeNull()
  })
})

describe('frise : les pieces d\'une rubrique pour « Par date »', () => {
  it('toutes, sous-dossiers compris, triees par date ; les non datees a part', () => {
    const rubrique = affaire().arbre.enfants[0]!.enfants[2]!
    const { datees, sansDate } = frise(rubrique)
    expect(datees.map((x) => x.ligne.id)).toEqual(['pv', 'cert', 'bail'])
    expect(sansDate.map((x) => [x.ligne.id, x.dossier])).toEqual([['info', "Depots d'actes"]])
  })
})
