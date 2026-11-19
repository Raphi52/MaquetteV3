// LA PAGE « RECHERCHE » : la palette Ctrl K etalee dans un onglet, en LISEUSE.
//
// /draft « Page Recherche », tour 4, proposition 4 « Lecteur » (conv-2018, 19/11/2026) :
// la liste des resultats a gauche -- le texte des pieces d'abord, puis le travail le plus
// urgent, les ecrans, l'arbre, les annonces, hors du greffe -- et la PIECE en grand a
// droite, chaque passage trouve surligne, le passage courant en or, avec passage suivant
// et precedent (N et Maj N, ou F3 et Maj F3 depuis la saisie).
// Pas d'equivalent dans RIG v2 : PROC_RECHAFF (« Recherche d'affaires ») n'y cherchait que
// des affaires, jamais dans le texte des pieces.
//
// La recherche est celle de la palette (Metier/RecherchePalette.cs) : le plan de travail la
// calcule sur le texte de CETTE barre (Pont/PlanTravailPalette.cs, VuePageRecherche) et la
// passe dans la vue de l'ecran. Ce composant ne tient que l'affichage : la piece lue, le
// passage courant, la frappe en cours. Les ouvertures passent par les gestes de la palette.

import { useEffect, useMemo, useRef, useState } from 'react'
import type { KeyboardEvent as ToucheReact, ReactNode } from 'react'
import { annonceRepond, Avatar, Picto, Surligne, Touche } from '../composants/Palette'
import { AnnoncesBodacc } from './EcranBodacc'
import type { GestePlan, PaletteVue, PiecePaletteVue } from '../plan/types'

export interface ProprietesEcranRecherche {
  /** Null tant que la vue n'est pas arrivee. */
  vue: PaletteVue | null
  plan: GestePlan
}

/** Les morceaux du texte d'une piece : hors passage, ou le passage n (0 = le premier). */
function morceaux(texte: string, positions: number[], longueur: number): Array<{ t: string, n: number | null }> {
  const r: Array<{ t: string, n: number | null }> = []
  let i = 0
  positions.forEach((p, n) => {
    if (p > i) r.push({ t: texte.slice(i, p), n: null })
    r.push({ t: texte.slice(p, p + longueur), n })
    i = p + longueur
  })
  if (i < texte.length) r.push({ t: texte.slice(i), n: null })
  return r
}

export function EcranRecherche({ vue, plan }: ProprietesEcranRecherche) {
  const [saisie, poserSaisie] = useState(vue?.texte ?? '')
  const [lue, lire] = useState(0)
  const [passage, poserPassage] = useState(0)
  const [toutLeTravail, voirToutLeTravail] = useState(false)
  const feuille = useRef<HTMLDivElement>(null)

  const texte = vue?.texte.trim() ?? ''
  const pieces = vue?.pieces ?? []
  const piece: PiecePaletteVue | undefined = pieces[Math.min(lue, Math.max(pieces.length - 1, 0))]
  // Les annonces de l'ecran BODACC, filtrees comme dans la palette.
  const annonces = useMemo(() => AnnoncesBodacc.filter((a) => annonceRepond(a, texte)), [texte])

  // Une autre recherche, ou une autre piece : on repart du premier passage.
  useEffect(() => { lire(0); poserPassage(0) }, [texte])
  useEffect(() => { poserPassage(0) }, [lue])
  // Le passage courant reste a l'ecran dans la feuille.
  useEffect(() => {
    feuille.current?.querySelector('[data-courant="oui"]')?.scrollIntoView?.({ block: 'center' })
  }, [passage, piece])

  if (!vue) return <div className="rig-rp"></div>

  const nb = piece?.positions.length ?? 0
  const allerPassage = (sens: number) => { if (nb > 0) poserPassage((passage + sens + nb) % nb) }
  const ouvrirPiece = (p: PiecePaletteVue | undefined) => { if (p) plan('palettePiece', p.chemin) }
  const aller = (nature: string, cle: string) => plan('paletteAller', { nature, cle })

  function surTouche(e: ToucheReact<HTMLDivElement>) {
    const dansSaisie = (e.target as HTMLElement).tagName === 'INPUT'
    if (e.key === 'F3' || (!dansSaisie && e.key.toLowerCase() === 'n' && !e.ctrlKey && !e.altKey)) {
      e.preventDefault(); allerPassage(e.shiftKey ? -1 : 1)
    } else if (!dansSaisie && e.key === 'Enter') {
      e.preventDefault(); ouvrirPiece(piece)
    }
  }

  const horsDelai = vue.filtres.find((f) => f.code === 'horsDelai')?.nombre ?? 0
  const total = vue.travailTrouve + vue.ecrans.length + vue.elements.length + pieces.length + annonces.length + vue.horsGreffe.length
  const resume = [
    `${vue.travailTrouve} a traiter`, horsDelai > 0 ? `${horsDelai} hors delai` : '',
    pieces.length > 0 ? `${pieces.length} ${pieces.length > 1 ? 'pieces' : 'piece'}` : '',
    annonces.length > 0 ? `${annonces.length} ${annonces.length > 1 ? 'annonces' : 'annonce'}` : '',
    vue.horsGreffe.length > 0 ? `${vue.horsGreffe.length} hors du greffe` : '',
  ].filter((t) => t.length > 0).join(' · ')
  const travail = toutLeTravail ? vue.travail : vue.travail.slice(0, 3)

  const titre = (t: string, n?: number, droite?: ReactNode) => (
    <div className="rig-palette__titre">{t}{n !== undefined && <b>{n}</b>}{droite}</div>
  )

  return (
    <div className="rig-rp" onKeyDown={surTouche}>
      {/* ---- La barre : la meme que celle de l'accueil, en grand ---- */}
      <div className="rig-rp__tete">
        <div className="rig-recherche rig-recherche--fixe rig-rp__barre">
          <span style={{ font: '13px var(--rig-font-mono)', color: 'var(--rig-focus)' }}>{'⌕'}</span>
          {' '}
          <input className="rig-recherche__saisie" value={saisie} aria-label="Rechercher" spellCheck={false} autoComplete="off"
                 placeholder="Rechercher un SIREN, une societe, un n° de demande, un mot dans les pieces…"
                 onChange={(e) => { poserSaisie(e.target.value); plan('rechercheTexte', e.target.value) }} />
          {' '}
          <Touche>F3</Touche>
        </div>
        <div className="rig-rp__som">
          {texte.length === 0
            ? <>Tape un nom, un numero ou un mot : la recherche fouille aussi le <b>texte des pieces</b>.</>
            : total === 0
              ? <><b>Aucun resultat</b> pour « {texte} »</>
              : <><b>{total} resultats</b> pour « {texte} » · {resume}</>}
        </div>
      </div>

      <div className="rig-rp__corps">
        {/* ---- A gauche : les resultats, le texte des pieces d'abord ---- */}
        <div className="rig-rp__liste" role="listbox" aria-label="Resultats">
          {pieces.length > 0 && <>
            {titre('Dans le texte des pieces', pieces.length)}
            {pieces.map((p, i) => (
              <div key={p.chemin.join('/')} role="option" aria-selected={p === piece}
                   className={'rig-palette__ligne' + (p === piece ? ' rig-palette__ligne--choisie' : '')}
                   onClick={() => lire(i)} onDoubleClick={() => ouvrirPiece(p)}>
                <Picto nom={p.annonce ? 'annonce' : 'piece'} />
                <span className="rig-palette__texte">
                  <span className="rig-palette__t1">{p.libelle}</span>
                  <span className="rig-palette__extrait">« {p.avant}<mark>{p.trouve}</mark>{p.apres} »</span>
                </span>
                <span className="rig-palette__meta">{p.passages > 1 ? `${p.passages} passages` : '1 passage'}</span>
              </div>
            ))}
          </>}

          {vue.travail.length > 0 && <>
            {titre(texte.length === 0 ? 'A traiter maintenant' : 'Le plus urgent d abord', vue.travailTrouve,
              vue.travail.length > 3 && (
                <button type="button" className="rig-rp__voir" onClick={() => voirToutLeTravail(!toutLeTravail)}>
                  {toutLeTravail ? 'n en voir que 3' : `voir les ${vue.travail.length}`}
                </button>))}
            {travail.map((t, i) => (
              <div key={t.numero + i} className="rig-palette__ligne"
                   onClick={() => aller(t.cle ? t.nature : 'ecran', t.cle || t.ecran)}>
                <span className={'rig-palette__filet rig-filet-v--' + t.filet}></span>
                <Picto nom={t.nature} />
                <span className="rig-palette__texte">
                  <span className="rig-palette__t1"><span className="rig-palette__num">{t.numero}</span><Surligne texte={t.titre} saisie={texte} /></span>
                  <span className="rig-palette__t2">{t.detail}</span>
                </span>
                <span className={'rig-palette__etat' + (t.ton ? ' rig-palette__etat--' + t.ton : '')}>{t.etat}</span>
                {t.qui ? <span title={t.qui.signature}><Avatar initiales={t.qui.initiales} moi={t.aMoi} /></span>
                       : <span className="rig-palette__avatar rig-palette__avatar--vide"></span>}
              </div>
            ))}
          </>}

          {vue.ecrans.length > 0 && <>
            {titre('Ecrans', vue.ecrans.length)}
            {vue.ecrans.map((e) => (
              <div key={e.code} className="rig-palette__ligne" onClick={() => aller('ecran', e.code)}>
                <Picto nom="ecran" />
                <span className="rig-palette__texte"><span className="rig-palette__t1"><Surligne texte={e.libelle} saisie={texte} /></span>
                  <span className="rig-palette__t2">Ouvrir l ecran</span></span>
              </div>
            ))}
          </>}

          {vue.elements.length > 0 && <>
            {titre('Dans l arbre du dossier', vue.elements.length)}
            {vue.elements.map((el) => (
              <div key={el.chemin.join('/')} className="rig-palette__ligne" onClick={() => plan('palettePiece', el.chemin)}>
                <Picto nom={el.piece ? 'doc' : 'aff'} />
                <span className="rig-palette__texte"><span className="rig-palette__t1"><Surligne texte={el.libelle} saisie={texte} /></span>
                  <span className="rig-palette__t2">{el.lieu}</span></span>
              </div>
            ))}
          </>}

          {annonces.length > 0 && <>
            {titre('Annonces BODACC', annonces.length)}
            {annonces.map((a) => (
              <div key={a.numAnnonce} className="rig-palette__ligne" onClick={() => aller('ecran', 'BODACC')}>
                <Picto nom="annonce" />
                <span className="rig-palette__texte"><span className="rig-palette__t1"><span className="rig-palette__num">{a.numAnnonce}</span><Surligne texte={a.texte} saisie={texte} /></span>
                  <span className="rig-palette__t2">{a.typeAnnonce} · dossier {a.dossier}</span></span>
              </div>
            ))}
          </>}

          {vue.sirenInconnu && <>
            {titre('Que faire de ce SIREN')}
            <div className="rig-palette__ligne" onClick={() => aller('siren', vue.sirenInconnu!)}>
              <Picto nom="siren" />
              <span className="rig-palette__texte"><span className="rig-palette__t1">Ouvrir un dossier pour ce SIREN</span>
                <span className="rig-palette__t2">le SIREN {vue.sirenInconnu} est deja rempli</span></span>
            </div>
          </>}

          {vue.horsGreffe.length > 0 && <>
            {titre('Hors du greffe', vue.horsGreffe.length)}
            {vue.horsGreffe.map((h) => (
              <div key={h.code} className="rig-palette__ligne rig-rp__inerte">
                <Picto nom="hors" />
                <span className="rig-palette__texte"><span className="rig-palette__t1">{h.source} · {h.sujet}</span>
                  <span className="rig-palette__t2">{h.objet}</span></span>
                <span className="rig-palette__etat">a brancher</span>
              </div>
            ))}
          </>}
        </div>

        {/* ---- A droite : la liseuse ---- */}
        <div className="rig-rp__lecteur">
          {piece ? <>
            <div className="rig-rp__barre-lecteur">
              <b>{piece.libelle}</b>
              <span className="rig-rp__pale">{piece.lieu} · {piece.pages} {piece.pages > 1 ? 'pages' : 'page'} · {piece.date}</span>
              <span className="rig-rp__nav">
                passage <b>{passage + 1}</b> sur {nb}
                <button type="button" className="rig-rp__fleche" title="Passage precedent (Maj N)" onClick={() => allerPassage(-1)}>‹</button>
                <button type="button" className="rig-rp__fleche" title="Passage suivant (N)" onClick={() => allerPassage(1)}>›</button>
                <Touche>N</Touche><Touche>Maj N</Touche>
              </span>
              <button type="button" className="rig-rp__ouvrir" onClick={() => ouvrirPiece(piece)}>Ouvrir la piece <Touche>Entree</Touche></button>
            </div>
            <div className="rig-rp__feuille" ref={feuille} aria-label={'Texte de la piece ' + piece.libelle}>
              {morceaux(piece.texte, piece.positions, piece.longueur).map((m, i) =>
                m.n === null
                  ? <span key={i}>{m.t}</span>
                  : <mark key={i} data-courant={m.n === passage ? 'oui' : undefined}
                          className={'rig-rp__m' + (m.n === passage ? ' rig-rp__m--courant' : '')}>{m.t}</mark>)}
            </div>
          </> : (
            <div className="rig-rp__vide">
              <Picto nom="piece" />
              <span>{texte.length < 3
                ? <>Le texte des pieces se cherche a partir de trois lettres.</>
                : <>Aucune piece ne contient « {texte} ».</>}</span>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
