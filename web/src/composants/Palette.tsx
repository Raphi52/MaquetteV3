// LA PALETTE Ctrl K : chercher depuis N'IMPORTE QUEL ecran, sans le quitter.
//
// /draft « Page Recherche » du 19/11/2026 (conv-2018) : la palette blanche
// « Empilee » (tour 3), avec deux familles d'elements retenues :
//   3 · MON TRAVAIL -- le plus urgent d'abord, filet d'etat, qui s'en occupe,
//       delai legal, activite du dossier, filtres « A moi », « Hors delai »,
//       « Cette semaine », « Me l'attribuer » (Ctrl M) et « Attribuer a… » ;
//   6 · PIECES -- le passage trouve dans le TEXTE des pieces de l'arbre,
//       les annonces BODACC, les fichiers hors du greffe.
// Socle commun : la saisie, la liste a gauche et l'apercu a droite, → pour
// les gestes du resultat (← ou Echap pour revenir), le pied des raccourcis,
// et que faire quand rien n'est trouve (un SIREN inconnu ouvre un dossier).
//
// La RECHERCHE est en C# (Metier/RecherchePalette.cs) et la vue arrive par le
// plan de travail (Pont/PlanTravailPalette.cs) ; ce composant ne tient que ce
// qui est affichage : la ligne choisie, le mode « gestes », la frappe en cours.
// Seules les annonces de l'ecran BODACC, declarees dans son .tsx, se filtrent ici.

import { useEffect, useMemo, useRef, useState } from 'react'
import type { KeyboardEvent as ToucheReact, ReactNode } from 'react'
import { AnnoncesBodacc } from '../ecrans/EcranBodacc'
import type { AnnonceBodacc } from '../ecrans/EcranBodacc'
import type {
  ElementPaletteVue, EcranPaletteVue, GestePlan, HorsGreffePaletteVue, PaletteVue, PiecePaletteVue, TravailPaletteVue,
} from '../plan/types'

// ---------------------------------------------------------------- texte

/** Sans accents ni casse, MEME LONGUEUR : une position trouvee vaut dans le texte d'origine. */
function plier(texte: string): string {
  let r = ''
  for (const c of texte) {
    const p = (c.normalize('NFD')[0] ?? c).toUpperCase()
    r += p.length === 1 ? p : c
  }
  return r
}

/** Les mots tapes (deux caracteres au moins), plies. */
function motsDe(saisie: string): string[] {
  return saisie.split(/\s+/).map(plier).filter((m) => m.length >= 2)
}

/** Le texte, avec les mots tapes surlignes (accents et casse ignores). */
export function Surligne({ texte, saisie }: { texte: string, saisie: string }) {
  const mots = motsDe(saisie)
  if (mots.length === 0) return <>{texte}</>
  const plie = plier(texte)
  const marque = new Array<boolean>(texte.length).fill(false)
  for (const m of mots) {
    for (let i = plie.indexOf(m); i >= 0; i = plie.indexOf(m, i + m.length)) marque.fill(true, i, i + m.length)
  }
  const morceaux: ReactNode[] = []
  let debut = 0
  for (let i = 1; i <= texte.length; i++) {
    if (i === texte.length || marque[i] !== marque[debut]) {
      const bout = texte.slice(debut, i)
      morceaux.push(marque[debut] ? <mark key={debut}>{bout}</mark> : bout)
      debut = i
    }
  }
  return <>{morceaux}</>
}

/** Une annonce BODACC repond-elle a la saisie ? Chaque mot doit s'y retrouver. */
function annonceRepond(a: AnnonceBodacc, saisie: string): boolean {
  const mots = motsDe(saisie)
  if (mots.length === 0 || plier(saisie).replace(/\s/g, '').length < 3) return false
  const tout = plier([a.numAnnonce, a.typeAnnonce, a.texte, a.dossier, a.numPc].join(' '))
  return mots.every((m) => tout.includes(m))
}

function copier(texte: string): void {
  void navigator.clipboard?.writeText(texte).catch(() => undefined)
}

// ---------------------------------------------------------------- pictos

const TRACES: Record<string, ReactNode> = {
  dem: <><path d="M4 2h6l3 3v9H4z" /><path d="M6 8h5M6 11h5" /></>,
  aff: <path d="M2 5h4l1.5 1.5H14V13H2z" />,
  cou: <><path d="M2 4h12v9H2z" /><path d="m2 4 6 5 6-5" /></>,
  doc: <><path d="M4 2h6l3 3v9H4z" /><path d="M10 2v3h3" /></>,
  piece: <><path d="M3 2h6l3 3v4" /><path d="M3 2v12h5" /><circle cx="11.5" cy="11.5" r="2.3" /><path d="m13.2 13.2 1.6 1.6" /></>,
  annonce: <><path d="M3 6h3l5-3v10L6 10H3z" /><path d="M6 10v3" /></>,
  hors: <><circle cx="8" cy="8" r="6" /><path d="M2 8h12M8 2c2 2 2 10 0 12M8 2c-2 2-2 10 0 12" /></>,
  siren: <><path d="M3 3h10v10H3z" /><path d="M8 5.5v5M5.5 8h5" /></>,
  loupe: <><circle cx="7" cy="7" r="4.5" /><path d="m10.5 10.5 3.5 3.5" /></>,
  ecran: <><path d="M2 3h12v8H2z" /><path d="M6 14h4M8 11v3" /></>,
  horloge: <><circle cx="8" cy="8" r="6" /><path d="M8 4.5V8l2.5 1.5" /></>,
}

function Picto({ nom }: { nom: string }) {
  return (
    <svg className="rig-palette__picto" viewBox="0 0 16 16" aria-hidden="true">{TRACES[nom] ?? TRACES.doc}</svg>
  )
}

function Avatar({ initiales, moi }: { initiales: string, moi?: boolean }) {
  return <span className={'rig-palette__avatar' + (moi ? ' rig-palette__avatar--moi' : '')}>{initiales}</span>
}

function Touche({ children }: { children: ReactNode }) {
  return <span className="rig-palette__touche">{children}</span>
}

// ---------------------------------------------------------------- ce que la liste montre

type Element =
  | { sorte: 'travail', t: TravailPaletteVue }
  | { sorte: 'ecran', e: EcranPaletteVue }
  | { sorte: 'element', el: ElementPaletteVue }
  | { sorte: 'piece', p: PiecePaletteVue }
  | { sorte: 'annonce', a: AnnonceBodacc }
  | { sorte: 'hors', h: HorsGreffePaletteVue }
  | { sorte: 'siren', siren: string }

interface Groupe { titre: string, compte?: number, elements: Element[] }

interface Geste { groupe: string, libelle: string, detail?: string, touche?: string, faire: () => void }

const NATURES: Record<string, string> = { dem: 'la demande', aff: 'l affaire', cou: 'le dossier du courrier', doc: 'le dossier de l acte' }

export interface ProprietesPalette {
  vue: PaletteVue
  plan: GestePlan
  /** Dessinee DANS la barre de recherche de l'accueil (composants/accueil/Recherche.tsx) :
   *  sa saisie prend la place de celle de la barre, le reste pend dessous. */
  ancree?: boolean
}

/** Le texte d'attente de la barre de l'accueil, ouverte ou fermee : la meme barre. */
export const ATTENTE_BARRE = 'Rechercher un SIREN, une societe, un dirigeant, un n° de demande…'

export function Palette({ vue, plan, ancree = false }: ProprietesPalette) {
  // La frappe vit ICI : le champ suit le clavier sans attendre la vue du C#,
  // qui ne fait que repondre a ce qui est tape.
  const [saisie, poserSaisie] = useState(vue.texte)
  const [choisi, choisir] = useState(0)
  const [enGestes, passerEnGestes] = useState(false)
  const [filtreGestes, poserFiltreGestes] = useState('')
  const [gesteChoisi, choisirGeste] = useState(0)
  const [copie, poserCopie] = useState<string | null>(null)
  const champ = useRef<HTMLInputElement>(null)
  const liste = useRef<HTMLDivElement>(null)
  const barre = useRef<HTMLDivElement>(null)

  useEffect(() => { champ.current?.focus() }, [enGestes])
  // Ctrl K peut derouler la palette sous une barre que la page a fait defiler
  // hors de vue, ou tout en bas : on ramene la barre en haut pour que sa liste
  // se voie.
  useEffect(() => {
    if (!ancree) return
    const r = barre.current?.getBoundingClientRect()
    if (r && (r.top < 0 || r.top > window.innerHeight * 0.4)) barre.current?.scrollIntoView?.({ block: 'start' })
  }, [ancree])
  // Accrochee a la barre, elle n'a pas de voile : un clic hors de la barre et
  // de sa liste la ferme, comme une liste deroulante.
  useEffect(() => {
    if (!ancree) return
    const surClic = (e: MouseEvent) => {
      if (barre.current && !barre.current.contains(e.target as Node)) plan('paletteFermer')
    }
    document.addEventListener('mousedown', surClic)
    return () => document.removeEventListener('mousedown', surClic)
  }, [ancree, plan])
  useEffect(() => {
    if (copie === null) return
    const minuteur = window.setTimeout(() => poserCopie(null), 1400)
    return () => window.clearTimeout(minuteur)
  }, [copie])

  const texte = vue.texte.trim()
  const annonces = useMemo(() => AnnoncesBodacc.filter((a) => annonceRepond(a, texte)), [texte])

  const groupes: Groupe[] = []
  if (texte.length === 0) {
    groupes.push({ titre: 'A traiter maintenant', compte: vue.travailTrouve, elements: vue.travail.map((t) => ({ sorte: 'travail', t })) })
  } else {
    if (vue.travail.length > 0) {
      groupes.push({ titre: 'Le plus urgent d abord', compte: vue.travailTrouve, elements: vue.travail.map((t) => ({ sorte: 'travail', t })) })
    }
    if (vue.ecrans.length > 0) groupes.push({ titre: 'Ecrans', compte: vue.ecrans.length, elements: vue.ecrans.map((e) => ({ sorte: 'ecran', e })) })
    if (vue.elements.length > 0) {
      groupes.push({ titre: 'Dans l arbre du dossier', compte: vue.elements.length, elements: vue.elements.map((el) => ({ sorte: 'element', el })) })
    }
    const pieces = vue.pieces.filter((p) => !p.annonce)
    if (pieces.length > 0) groupes.push({ titre: 'Dans le texte des pieces', compte: pieces.length, elements: pieces.map((p) => ({ sorte: 'piece', p })) })
    const avis = vue.pieces.filter((p) => p.annonce)
    if (avis.length + annonces.length > 0) {
      groupes.push({
        titre: 'Annonces BODACC', compte: avis.length + annonces.length,
        elements: [...avis.map((p): Element => ({ sorte: 'piece', p })), ...annonces.map((a): Element => ({ sorte: 'annonce', a }))],
      })
    }
    if (vue.sirenInconnu) groupes.push({ titre: 'Que faire de ce SIREN', elements: [{ sorte: 'siren', siren: vue.sirenInconnu }] })
    if (vue.horsGreffe.length > 0) groupes.push({ titre: 'Hors du greffe', compte: vue.horsGreffe.length, elements: vue.horsGreffe.map((h) => ({ sorte: 'hors', h })) })
  }
  const elements = groupes.flatMap((g) => g.elements)
  const rien = texte.length > 0 && vue.travail.length === 0 && vue.pieces.length === 0 && annonces.length === 0
    && vue.ecrans.length === 0 && vue.elements.length === 0
  const index = Math.min(choisi, Math.max(elements.length - 1, 0))
  const courant: Element | undefined = elements[index]

  // La ligne choisie reste a l'ecran quand les fleches la deplacent.
  useEffect(() => {
    liste.current?.querySelector('[data-choisi="oui"]')?.scrollIntoView?.({ block: 'nearest' })
  }, [index, enGestes, gesteChoisi])

  const aller = (nature: string, cle: string) => plan('paletteAller', { nature, cle })
  const copierAvecAvis = (valeur: string, quoi: string) => { copier(valeur); poserCopie(quoi) }

  function gestesDe(e: Element): Geste[] {
    switch (e.sorte) {
      case 'travail': {
        const t = e.t
        const g: Geste[] = []
        if (t.cle) g.push({ groupe: 'Ouvrir', libelle: 'Ouvrir ' + NATURES[t.nature], detail: t.cle, touche: 'Entree', faire: () => aller(t.nature, t.cle) })
        if (t.ecran && t.ecranLibelle) g.push({ groupe: 'Ouvrir', libelle: 'Ouvrir l ecran ' + t.ecranLibelle, ...(t.cle ? {} : { touche: 'Entree' }), faire: () => aller('ecran', t.ecran) })
        if (t.nature === 'dem' && t.enCours) {
          if (t.attribuable) {
            g.push({ groupe: 'Mon travail', libelle: 'Me l attribuer', detail: t.qui ? 'aujourd hui ' + t.qui.signature : 'personne ne s en occupe', touche: 'Ctrl M', faire: () => plan('paletteAttribuer', { numero: t.numero, signature: null }) })
          }
          for (const a of vue.agents) {
            if (a.signature === vue.moi.signature || a.signature === t.qui?.signature) continue
            g.push({ groupe: 'Mon travail', libelle: 'Attribuer a ' + a.nom, detail: a.signature, faire: () => plan('paletteAttribuer', { numero: t.numero, signature: a.signature }) })
          }
        }
        g.push({ groupe: 'Copier', libelle: 'Copier le numero', detail: t.numero, touche: 'Ctrl C', faire: () => copierAvecAvis(t.numero, 'numero copie') })
        if (t.siren) g.push({ groupe: 'Copier', libelle: 'Copier le SIREN', detail: t.siren, faire: () => copierAvecAvis(t.siren, 'SIREN copie') })
        return g
      }
      case 'ecran':
        return [{ groupe: 'Ouvrir', libelle: 'Ouvrir l ecran ' + e.e.libelle, touche: 'Entree', faire: () => aller('ecran', e.e.code) }]
      case 'element': {
        const el = e.el
        return [
          { groupe: 'Ouvrir', libelle: el.piece ? 'Ouvrir la piece' : 'Ouvrir dans l arbre', detail: el.lieu, touche: 'Entree', faire: () => plan('palettePiece', el.chemin) },
          { groupe: 'Copier', libelle: 'Copier le libelle', detail: el.libelle, touche: 'Ctrl C', faire: () => copierAvecAvis(el.libelle, 'libelle copie') },
        ]
      }
      case 'piece': {
        const p = e.p
        const passage = (p.avant + p.trouve + p.apres).replace(/^…|…$/g, '')
        return [
          { groupe: 'Cette piece', libelle: 'Ouvrir la piece', detail: p.libelle, touche: 'Entree', faire: () => plan('palettePiece', p.chemin) },
          { groupe: 'Cette piece', libelle: 'Ouvrir le dossier ' + vue.numeroDesPieces, faire: () => aller('aff', vue.numeroDesPieces) },
          { groupe: 'Copier', libelle: 'Copier le passage', touche: 'Ctrl C', faire: () => copierAvecAvis(passage, 'passage copie') },
        ]
      }
      case 'annonce': {
        const a = e.a
        return [
          { groupe: 'Ouvrir', libelle: 'Ouvrir l ecran BODACC', detail: a.numAnnonce, touche: 'Entree', faire: () => aller('ecran', 'BODACC') },
          { groupe: 'Copier', libelle: 'Copier le n° d annonce', detail: a.numAnnonce, touche: 'Ctrl C', faire: () => copierAvecAvis(a.numAnnonce, 'n° copie') },
        ]
      }
      case 'hors': {
        const siren = e.h.sujet.split(' · ').pop() ?? e.h.sujet
        return [{ groupe: 'Copier', libelle: 'Copier le SIREN', detail: siren, touche: 'Entree', faire: () => copierAvecAvis(siren, 'SIREN copie') }]
      }
      case 'siren':
        return [
          { groupe: 'Faire', libelle: 'Ouvrir un dossier pour ce SIREN', detail: 'le SIREN est deja rempli', touche: 'Entree', faire: () => aller('siren', e.siren) },
          { groupe: 'Copier', libelle: 'Copier le SIREN', detail: e.siren, touche: 'Ctrl C', faire: () => copierAvecAvis(e.siren, 'SIREN copie') },
        ]
    }
  }

  const gestes = courant ? gestesDe(courant) : []
  const gestesFiltres = gestes.filter((g) => motsDe(filtreGestes).every((m) => plier(g.libelle + ' ' + (g.detail ?? '')).includes(m)))
  const indexGeste = Math.min(gesteChoisi, Math.max(gestesFiltres.length - 1, 0))

  function changerSaisie(t: string) {
    if (enGestes) { poserFiltreGestes(t); choisirGeste(0); return }
    poserSaisie(t)
    choisir(0)
    plan('paletteTexte', t)
  }
  function entrerEnGestes() {
    if (!courant || gestes.length === 0) return
    passerEnGestes(true); poserFiltreGestes(''); choisirGeste(0)
  }
  function revenir() { passerEnGestes(false); poserFiltreGestes('') }
  function filtreSuivant(sens: number) {
    const i = vue.filtres.findIndex((f) => f.code === vue.filtre)
    const n = vue.filtres.length
    const suivant = vue.filtres[(i + sens + n) % n]
    if (suivant) plan('paletteFiltre', suivant.code)
    choisir(0)
  }

  function surTouche(e: ToucheReact<HTMLDivElement>) {
    const ctrl = e.ctrlKey || e.metaKey
    const fin = champ.current ? champ.current.selectionStart === champ.current.value.length : true
    if (e.key === 'Escape') {
      // Echap remonte d'un cran : des gestes aux resultats, puis ferme. La
      // touche s'arrete ici : le plan de travail fermerait l'ecran dessous.
      e.preventDefault(); e.stopPropagation()
      if (enGestes) revenir(); else plan('paletteFermer')
    } else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      const pas = e.key === 'ArrowDown' ? 1 : -1
      if (enGestes) choisirGeste(Math.max(0, Math.min(gestesFiltres.length - 1, indexGeste + pas)))
      else choisir(Math.max(0, Math.min(elements.length - 1, index + pas)))
    } else if (e.key === 'ArrowRight' && !enGestes && fin) {
      e.preventDefault(); entrerEnGestes()
    } else if (e.key === 'ArrowLeft' && enGestes && filtreGestes.length === 0) {
      e.preventDefault(); revenir()
    } else if (e.key === 'Enter') {
      e.preventDefault()
      const g = enGestes ? gestesFiltres[indexGeste] : gestes[0]
      g?.faire()
      if (enGestes && g && g.groupe === 'Mon travail') revenir()
    } else if (e.key === 'Tab' && !enGestes) {
      e.preventDefault(); filtreSuivant(e.shiftKey ? -1 : 1)
    } else if (ctrl && e.key.toLowerCase() === 'k') {
      // Deja ouverte : Ctrl K reselectionne la saisie, pour taper autre chose.
      e.preventDefault(); e.stopPropagation()
      champ.current?.focus(); champ.current?.select()
    } else if (ctrl && e.key.toLowerCase() === 'm' && courant?.sorte === 'travail' && courant.t.attribuable) {
      e.preventDefault(); plan('paletteAttribuer', { numero: courant.t.numero, signature: null })
    } else if (ctrl && e.key.toLowerCase() === 'c' && champ.current && champ.current.selectionStart === champ.current.selectionEnd) {
      // Ctrl C sans texte selectionne dans la saisie : copie le resultat choisi.
      const g = gestes.find((x) => x.touche === 'Ctrl C')
      if (g) { e.preventDefault(); g.faire() }
    }
  }

  const choisiTravail = courant?.sorte === 'travail' ? courant.t : null

  // La saisie : dans sa propre ligne (palette flottante) ou dans la barre de
  // l'accueil (palette accrochee), avec la meme classe que le champ de la barre.
  const fil = enGestes && courant && <span className="rig-palette__fil">{libelleCourt(courant)}<i>›</i></span>
  const saisieChamp = (
    <input ref={champ} className={ancree ? 'rig-recherche__saisie' : 'rig-palette__champ'} value={enGestes ? filtreGestes : saisie}
           onChange={(e) => changerSaisie(e.target.value)} spellCheck={false} autoComplete="off"
           aria-label={enGestes ? 'Filtrer les gestes' : 'Rechercher'}
           placeholder={enGestes ? 'Filtrer les gestes…' : ancree ? ATTENTE_BARRE : 'Rechercher un SIREN, une societe, un n° de demande, un mot dans les pieces…'} />
  )
  const avisCopie = copie && <span className="rig-palette__copie">{copie}</span>

  if (ancree) {
    return (
      <div ref={barre} className="rig-recherche rig-recherche--fixe rig-recherche--palette" onKeyDown={surTouche}>
        <span style={{ font: '13px var(--rig-font-mono)', color: 'var(--rig-focus)' }}>{'⌕'}</span>
        {' '}
        {fil}
        {saisieChamp}
        {avisCopie}
        {' '}
        <Touche>Echap</Touche>
        <div className="rig-palette rig-palette--ancree" role="dialog" aria-label="Rechercher partout">
          {panneau()}
        </div>
      </div>
    )
  }

  return (
    <div className="rig-palette__voile" onMouseDown={() => plan('paletteFermer')}>
      <div className="rig-palette" role="dialog" aria-label="Rechercher partout" onKeyDown={surTouche}
           onMouseDown={(e) => e.stopPropagation()}>
        {/* ---- La saisie ---- */}
        <div className="rig-palette__saisie">
          <Picto nom="loupe" />
          {fil}
          {saisieChamp}
          {avisCopie}
          <Touche>Echap</Touche>
        </div>
        {panneau()}
      </div>
    </div>
  )

  /** Ce qui est sous la saisie : les filtres, la liste et l'apercu, le pied. */
  function panneau() {
    return <>
        {/* ---- Les filtres de « Mon travail » ---- */}
        <div className="rig-palette__filtres">
          {vue.filtres.map((f) => (
            <button key={f.code} type="button" tabIndex={-1}
                    className={'rig-palette__filtre' + (f.code === vue.filtre ? ' rig-palette__filtre--actif' : '')
                               + (f.nombre === 0 ? ' rig-palette__filtre--vide' : '')}
                    onClick={() => { plan('paletteFiltre', f.code); choisir(0); champ.current?.focus() }}>
              {f.libelle}<b>{f.nombre}</b>
            </button>
          ))}
          <span className="rig-palette__moi"><Avatar initiales={vue.moi.initiales} moi />{vue.moi.nom}</span>
        </div>

        <div className="rig-palette__corps">
          {/* ---- La liste : resultats, ou gestes du resultat choisi ---- */}
          <div className="rig-palette__liste" ref={liste} role="listbox" aria-label={enGestes ? 'Gestes du resultat' : 'Resultats'}>
            {!enGestes && rien && (
              <div className="rig-palette__rien"><Picto nom="loupe" /><span>Aucun resultat pour <b>« {texte} »</b></span></div>
            )}
            {!enGestes && groupes.map((g) => (
              <div key={g.titre} className="rig-palette__groupe">
                <div className="rig-palette__titre">{g.titre}{g.compte !== undefined && <b>{g.compte}</b>}</div>
                {g.elements.map((el) => {
                  const i = elements.indexOf(el)
                  return (
                    <Ligne key={i} element={el} saisie={texte} choisie={i === index}
                           survoler={() => choisir(i)} ouvrir={() => gestesDe(el)[0]?.faire()}
                           gestes={() => { choisir(i); passerEnGestes(true); poserFiltreGestes(''); choisirGeste(0) }} />
                  )
                })}
              </div>
            ))}
            {!enGestes && texte.length === 0 && elements.length === 0 && (
              <div className="rig-palette__rien"><span>Rien a traiter avec ce filtre.</span></div>
            )}
            {enGestes && gestesFiltres.map((g, i) => (
              <div key={g.libelle + i}>
                {gestesFiltres[i - 1]?.groupe !== g.groupe && <div className="rig-palette__titre">{g.groupe}</div>}
                <button type="button" tabIndex={-1} data-choisi={i === indexGeste ? 'oui' : undefined}
                        className={'rig-palette__ligne' + (i === indexGeste ? ' rig-palette__ligne--choisie' : '')}
                        onMouseEnter={() => choisirGeste(i)}
                        onClick={() => { g.faire(); if (g.groupe === 'Mon travail') revenir() }}>
                  <span className="rig-palette__texte"><span className="rig-palette__t1">{g.libelle}</span>
                    {g.detail && <span className="rig-palette__t2">{g.detail}</span>}</span>
                  {g.touche && <Touche>{g.touche}</Touche>}
                </button>
              </div>
            ))}
            {enGestes && gestesFiltres.length === 0 && <div className="rig-palette__rien"><span>Aucun geste ne correspond.</span></div>}
          </div>

          {/* ---- L'apercu ---- */}
          <div className="rig-palette__apercu">
            {texte.length === 0
              ? <ApercuBilan vue={vue} />
              : courant
                ? <Apercu element={courant} vue={vue} saisie={texte} />
                : <ApercuRien />}
          </div>
        </div>

        {/* ---- Le pied des raccourcis ---- */}
        <div className="rig-palette__pied">
          {enGestes
            ? <><Touche>↑ ↓</Touche>choisir <Touche>Entree</Touche>lancer <Touche>←</Touche>ou <Touche>Echap</Touche>revenir aux resultats</>
            : <><Touche>↑ ↓</Touche>choisir <Touche>Entree</Touche>ouvrir <Touche>→</Touche>gestes <Touche>Tab</Touche>filtre suivant
                {choisiTravail?.attribuable && <><Touche>Ctrl M</Touche>me l attribuer</>} <Touche>Echap</Touche>fermer</>}
        </div>
    </>
  }
}

function libelleCourt(e: Element): string {
  switch (e.sorte) {
    case 'travail': return `${e.t.numero} · ${e.t.titre.split(' - ')[0]}`
    case 'ecran': return e.e.libelle
    case 'element': return e.el.libelle
    case 'piece': return e.p.libelle
    case 'annonce': return `${e.a.numAnnonce} · ${e.a.typeAnnonce}`
    case 'hors': return e.h.source
    case 'siren': return e.siren
  }
}

// ---------------------------------------------------------------- une ligne

interface ProprietesLigne {
  element: Element
  saisie: string
  choisie: boolean
  survoler: () => void
  ouvrir: () => void
  gestes: () => void
}

function Ligne({ element: e, saisie, choisie, survoler, ouvrir, gestes }: ProprietesLigne) {
  let corps: ReactNode
  switch (e.sorte) {
    case 'travail':
      corps = <>
        <span className={'rig-palette__filet rig-filet-v--' + e.t.filet}></span>
        <Picto nom={e.t.nature} />
        <span className="rig-palette__texte">
          <span className="rig-palette__t1"><span className="rig-palette__num"><Surligne texte={e.t.numero} saisie={saisie} /></span>
            <Surligne texte={e.t.titre} saisie={saisie} /></span>
          <span className="rig-palette__t2">{e.t.detail}</span>
        </span>
        <span className={'rig-palette__etat' + (e.t.ton ? ' rig-palette__etat--' + e.t.ton : '')}>{e.t.etat}</span>
        {e.t.qui ? <span title={e.t.qui.signature}><Avatar initiales={e.t.qui.initiales} moi={e.t.aMoi} /></span>
                 : <span className="rig-palette__avatar rig-palette__avatar--vide"></span>}
      </>
      break
    case 'ecran':
      corps = <>
        <Picto nom="ecran" />
        <span className="rig-palette__texte">
          <span className="rig-palette__t1"><Surligne texte={e.e.libelle} saisie={saisie} /></span>
          <span className="rig-palette__t2">Ouvrir l ecran</span>
        </span>
      </>
      break
    case 'element':
      corps = <>
        <Picto nom={e.el.piece ? 'doc' : 'aff'} />
        <span className="rig-palette__texte">
          <span className="rig-palette__t1"><Surligne texte={e.el.libelle} saisie={saisie} /></span>
          <span className="rig-palette__t2">{e.el.lieu}</span>
        </span>
      </>
      break
    case 'piece':
      corps = <>
        <Picto nom={e.p.annonce ? 'annonce' : 'piece'} />
        <span className="rig-palette__texte">
          <span className="rig-palette__t1">{e.p.libelle}</span>
          <span className="rig-palette__extrait">« {e.p.avant}<mark>{e.p.trouve}</mark>{e.p.apres} »</span>
        </span>
        <span className="rig-palette__meta">{e.p.passages > 1 ? `${e.p.passages} passages` : `${e.p.pages} p.`}</span>
      </>
      break
    case 'annonce':
      corps = <>
        <Picto nom="annonce" />
        <span className="rig-palette__texte">
          <span className="rig-palette__t1"><span className="rig-palette__num">{e.a.numAnnonce}</span><Surligne texte={e.a.texte} saisie={saisie} /></span>
          <span className="rig-palette__t2">{e.a.typeAnnonce} · dossier {e.a.dossier}</span>
        </span>
        <span className={'rig-palette__etat' + (e.a.envoi ? ' rig-palette__etat--ok' : '')}>{e.a.envoi ? 'Envoyee' : 'A envoyer'}</span>
      </>
      break
    case 'hors':
      corps = <>
        <Picto nom="hors" />
        <span className="rig-palette__texte">
          <span className="rig-palette__t1">{e.h.source} · {e.h.sujet}</span>
          <span className="rig-palette__t2">{e.h.objet}</span>
        </span>
        <span className="rig-palette__etat">a brancher</span>
      </>
      break
    case 'siren':
      corps = <>
        <Picto nom="siren" />
        <span className="rig-palette__texte">
          <span className="rig-palette__t1">Ouvrir un dossier pour ce SIREN</span>
          <span className="rig-palette__t2">le SIREN {e.siren} est deja rempli</span>
        </span>
        <span className="rig-palette__touche">Entree</span>
      </>
      break
  }
  return (
    <div className={'rig-palette__ligne' + (choisie ? ' rig-palette__ligne--choisie' : '')}
         data-choisi={choisie ? 'oui' : undefined} role="option" aria-selected={choisie}
         onMouseEnter={survoler} onClick={ouvrir}>
      {corps}
      <button type="button" tabIndex={-1} className="rig-palette__vers" title="Gestes du resultat"
              onClick={(ev) => { ev.stopPropagation(); gestes() }}>›</button>
    </div>
  )
}

// ---------------------------------------------------------------- l'apercu

function ApercuBilan({ vue }: { vue: PaletteVue }) {
  const b = vue.bilan
  return <>
    <div className="rig-palette__titre">Mon travail · {b.jour}</div>
    <div className="rig-palette__chiffres">
      <span><b className={b.horsDelai > 0 ? 'rig-palette__al' : ''}>{b.horsDelai}</b>hors delai</span>
      <span><b>{b.arriveesDuJour}</b>arrivees du jour</span>
      <span><b>{b.mAttendent}</b>m attendent</span>
    </div>
    <div className="rig-palette__qui">
      <Avatar initiales={vue.moi.initiales} moi />
      <span>{b.mAttendent === 0
        ? <>Aucune demande en cours ne t attend.</>
        : <><b>{b.mAttendent}</b> {b.mAttendent > 1 ? 'demandes t attendent' : 'demande t attend'}<br />
            <span className="rig-palette__pale">la plus ancienne depuis {b.plusAncienne} {b.plusAncienne > 1 ? 'jours' : 'jour'}</span></>}</span>
    </div>
    <div className="rig-palette__titre rig-palette__sep">Chercher dans</div>
    <div className="rig-palette__sources">
      <span><Picto nom="dem" />Les demandes, affaires, le courrier et les actes</span>
      <span><Picto nom="piece" />Le texte des pieces · {vue.dossierDesPieces}</span>
      <span><Picto nom="annonce" />Les annonces BODACC</span>
      <span><Picto nom="hors" />Hors du greffe : FIBEN, interdits de gerer, beneficiaires</span>
    </div>
  </>
}

function ApercuRien() {
  return <>
    <div className="rig-palette__titre">Rien trouve</div>
    <p className="rig-palette__para">Verifie l orthographe, ou tape un numero : SIREN, n° de demande, de depot, d affaire.</p>
    <p className="rig-palette__para">Le texte des pieces se cherche a partir de trois lettres.</p>
  </>
}

function Apercu({ element: e, vue, saisie }: { element: Element, vue: PaletteVue, saisie: string }) {
  switch (e.sorte) {
    case 'travail': {
      const t = e.t
      return <>
        <div className="rig-palette__titre">Apercu · {({ dem: 'demande', aff: 'affaire', cou: 'courrier', doc: 'acte numerise' } as Record<string, string>)[t.nature]}</div>
        <div className="rig-palette__nom"><span className="rig-palette__num">{t.numero}</span><Surligne texte={t.titre} saisie={saisie} /></div>
        <div className="rig-palette__para">{t.detail}</div>
        <div className="rig-palette__qui">
          {t.qui
            ? <><Avatar initiales={t.qui.initiales} moi={t.aMoi} /><span><b>{t.aMoi ? 'Toi' : t.qui.nom}</b>{t.qui.nom !== t.qui.signature && !t.aMoi ? ` (${t.qui.signature})` : ''}<br /><span className="rig-palette__pale">s en occupe</span></span></>
            : <><span className="rig-palette__avatar rig-palette__avatar--vide"></span><span className="rig-palette__pale">Personne ne s en occupe</span></>}
        </div>
        {t.delai && (
          <div className={'rig-palette__delai' + (t.ton === 'al' ? ' rig-palette__al' : '')}><Picto nom="horloge" /><span>{t.delai}</span></div>
        )}
        {t.cle && <>
          <div className="rig-palette__titre rig-palette__sep">Activite</div>
          {t.activite.length === 0
            ? <div className="rig-palette__pale">Aucune activite visible sur ce dossier.</div>
            : t.activite.map((a, i) => (
                <div key={i} className="rig-palette__activite">
                  <span className="rig-palette__point"></span>
                  <span><b>{a.agent}</b> {a.verbe} {a.objet}</span>
                  <span className="rig-palette__quand">{a.depuis}</span>
                </div>
              ))}
        </>}
      </>
    }
    case 'ecran':
      return <>
        <div className="rig-palette__titre">Apercu · ecran</div>
        <div className="rig-palette__nom">{e.e.libelle}</div>
        <div className="rig-palette__para">Entree ouvre cet ecran dans un onglet.</div>
      </>
    case 'element':
      return <>
        <div className="rig-palette__titre">Apercu · {e.el.piece ? 'piece' : 'dossier de l arbre'}</div>
        <div className="rig-palette__nom"><Surligne texte={e.el.libelle} saisie={saisie} /></div>
        <div className="rig-palette__para">{e.el.lieu ? `${e.el.lieu} · ` : ''}{vue.dossierDesPieces}</div>
        <div className="rig-palette__para">Entree ouvre l affaire, l arbre deplie jusqu ici.</div>
      </>
    case 'piece': {
      const p = e.p
      return <>
        <div className="rig-palette__titre">Apercu · {p.annonce ? 'annonce' : 'piece'}</div>
        <div className="rig-palette__nom">{p.libelle}</div>
        <div className="rig-palette__para">{p.lieu} · {vue.dossierDesPieces}<br />{p.pages} {p.pages > 1 ? 'pages' : 'page'} · {p.date} · {p.origine}</div>
        <div className="rig-palette__page" aria-label="Passage trouve dans la piece">
          <i></i><i className="rig-palette__l2"></i>
          <span>{p.avant}<mark>{p.trouve}</mark>{p.apres}</span>
          <i></i><i className="rig-palette__l2"></i><i className="rig-palette__l3"></i>
        </div>
        <div className="rig-palette__para rig-palette__centre">{p.passages > 1 ? `${p.passages} passages trouves` : '1 passage trouve'}</div>
      </>
    }
    case 'annonce': {
      const a = e.a
      return <>
        <div className="rig-palette__titre">Apercu · annonce BODACC</div>
        <div className="rig-palette__nom"><span className="rig-palette__num">{a.numAnnonce}</span>{a.typeAnnonce}</div>
        <div className="rig-palette__para">{a.texte}<br />Dossier {a.dossier}{a.numPc ? ` · ${a.numPc}` : ''}</div>
        <div className="rig-palette__para">{a.envoi ? `Envoyee le ${a.envoi.split('-').reverse().join('/')}` : `Envoi prevu le ${a.prevueEnvoi.split('-').reverse().join('/')}`}</div>
      </>
    }
    case 'hors':
      return <>
        <div className="rig-palette__titre">Hors du greffe</div>
        <div className="rig-palette__nom">{e.h.source}</div>
        <div className="rig-palette__para">{e.h.objet}<br />Pour <b>{e.h.sujet}</b></div>
        <div className="rig-palette__para rig-palette__pale">Pas encore branche : RIG V3 n interroge aucun fichier exterieur pour l instant.</div>
      </>
    case 'siren':
      return <>
        <div className="rig-palette__titre">Apercu · Ouvrir un dossier</div>
        <div className="rig-palette__form"><span>SIREN</span><b>{e.siren}</b><span>Denomination</span><i></i><span>Forme</span><i></i></div>
        <div className="rig-palette__para">Entree ouvre l ecran avec le SIREN deja saisi.</div>
      </>
  }
}
