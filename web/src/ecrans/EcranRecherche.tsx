// LA PAGE « RECHERCHE » : la palette Ctrl K etalee dans un onglet, en LISEUSE.
//
// /draft « Page Recherche », tour 4, proposition 4 « Lecteur » (conv-2018, 19/11/2026) :
// la liste des resultats a gauche -- le texte des pieces d'abord, puis le travail le plus
// urgent, les ecrans, l'arbre, les annonces, hors du greffe -- et la PIECE en grand a
// droite, chaque passage trouve surligne, le passage courant en or, avec passage suivant
// et precedent (N et Maj N, ou F3 et Maj F3 depuis la saisie). Un vrai document (PDF) se
// lit PAGE par page : la liseuse montre la page du passage courant (« page 3 sur 13 »), en
// texte surligne ou, au choix, le PDF lui-meme ouvert a cette page. « Joindre a ce depot »
// pose une copie de la piece dans un depot de l'affaire (Pont/PlanTravailPalette.cs).
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
import type { DepotPaletteVue, GestePlan, PaletteVue, PiecePaletteVue } from '../plan/types'

export interface ProprietesEcranRecherche {
  /** Null tant que la vue n'est pas arrivee. */
  vue: PaletteVue | null
  plan: GestePlan
}

/**
 * Les morceaux de la PAGE n (a partir de 1) d'une piece : hors passage, ou le passage
 * numero k de la piece (0 = le premier). Les pages sont separees par \f dans le texte.
 */
export function morceauxDeLaPage(piece: PiecePaletteVue, n: number): Array<{ t: string, k: number | null }> {
  const pages = piece.texte.split('\f')
  let debut = 0
  for (let i = 0; i < n - 1 && i < pages.length; i++) debut += (pages[i] ?? '').length + 1
  const page = pages[n - 1] ?? ''
  const r: Array<{ t: string, k: number | null }> = []
  let i = 0
  piece.positions.forEach((p, k) => {
    const lg = piece.longueurs[k] ?? 0
    if (p < debut || p >= debut + page.length) return
    const local = p - debut
    if (local > i) r.push({ t: page.slice(i, local), k: null })
    r.push({ t: page.slice(local, local + lg), k })
    i = local + lg
  })
  if (i < page.length) r.push({ t: page.slice(i), k: null })
  return r
}

/** Ce qui distingue une piece d'une autre dans la liste : son chemin dans l'arbre. */
const cleDe = (p: PiecePaletteVue) => p.chemin.join('/')

export function EcranRecherche({ vue, plan }: ProprietesEcranRecherche) {
  const [saisie, poserSaisie] = useState(vue?.texte ?? '')
  // La piece lue, retenue par son CHEMIN (pas son rang) : « Joindre a ce depot » ajoute sa
  // copie a la liste, qui se reordonne ; la liseuse doit rester sur la meme piece.
  const [lue, lire] = useState<string | null>(null)
  const [passage, poserPassage] = useState(0)
  const [toutLeTravail, voirToutLeTravail] = useState(false)
  const [enPdf, voirEnPdf] = useState(false)
  const [depotVoulu, vouloirDepot] = useState<string | null>(null)
  const feuille = useRef<HTMLDivElement>(null)

  const texte = vue?.texte.trim() ?? ''
  const pieces = vue?.pieces ?? []
  const piece: PiecePaletteVue | undefined = pieces.find((p) => cleDe(p) === lue) ?? pieces[0]
  const cleLue = piece ? cleDe(piece) : ''
  // Les annonces de l'ecran BODACC, filtrees comme dans la palette.
  const annonces = useMemo(() => AnnoncesBodacc.filter((a) => annonceRepond(a, texte)), [texte])

  // Une autre recherche, ou une autre piece : on repart du premier passage.
  useEffect(() => { lire(null); poserPassage(0) }, [texte])
  useEffect(() => { poserPassage(0); voirEnPdf(false) }, [cleLue])
  // Le passage courant reste a l'ecran dans la feuille.
  useEffect(() => {
    feuille.current?.querySelector('[data-courant="oui"]')?.scrollIntoView?.({ block: 'center' })
  }, [passage, piece])

  if (!vue) return <div className="rig-rp"></div>

  const nb = piece?.positions.length ?? 0
  const page = piece?.pagesDesPassages[passage] ?? 1
  // « Ce depot » : celui choisi dans la liste, sinon celui selectionne dans l'affaire, sinon le premier.
  const depot: DepotPaletteVue | undefined = vue.depots.find((d) => d.chemin.join('/') === depotVoulu)
    ?? vue.depots.find((d) => d.choisi) ?? vue.depots[0]
  const dejaDedans = !!piece && !!depot && depot.pieces.includes(piece.libelle)
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
            {pieces.map((p) => (
              <div key={cleDe(p)} role="option" aria-selected={p === piece}
                   className={'rig-palette__ligne' + (p === piece ? ' rig-palette__ligne--choisie' : '')}
                   onClick={() => lire(cleDe(p))} onDoubleClick={() => ouvrirPiece(p)}>
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
              <span className="rig-rp__pale">{piece.lieu} · {piece.date}</span>
              {(piece.nbPages > 1 || piece.fichier) && <span className="rig-rp__page">page <b>{page}</b> sur {piece.nbPages}</span>}
              {piece.fichier && (
                <span className="rig-rp__vues" role="group" aria-label="Voir la piece">
                  <button type="button" className={'rig-rp__vue' + (enPdf ? '' : ' rig-rp__vue--on')} onClick={() => voirEnPdf(false)}>Texte</button>
                  <button type="button" className={'rig-rp__vue' + (enPdf ? ' rig-rp__vue--on' : '')} onClick={() => voirEnPdf(true)}>PDF</button>
                </span>
              )}
              <span className="rig-rp__nav">
                passage <b>{passage + 1}</b> sur {nb}
                <button type="button" className="rig-rp__fleche" title="Passage precedent (Maj N)" onClick={() => allerPassage(-1)}>‹</button>
                <button type="button" className="rig-rp__fleche" title="Passage suivant (N)" onClick={() => allerPassage(1)}>›</button>
                <Touche>N</Touche><Touche>Maj N</Touche>
              </span>
              <button type="button" className="rig-rp__ouvrir" onClick={() => ouvrirPiece(piece)}>Ouvrir la piece <Touche>Entree</Touche></button>
            </div>
            {enPdf && piece.fichier
              ? <iframe key={piece.fichier + '#' + page} className="rig-rp__pdf" title={piece.libelle + ', page ' + page}
                        src={piece.fichier + '#page=' + page + '&view=FitH'}></iframe>
              : (
                <div className="rig-rp__feuille" ref={feuille} aria-label={'Texte de la piece ' + piece.libelle + ', page ' + page}>
                  {morceauxDeLaPage(piece, page).map((m, i) =>
                    m.k === null
                      ? <span key={i}>{m.t}</span>
                      : <mark key={i} data-courant={m.k === passage ? 'oui' : undefined}
                              className={'rig-rp__m' + (m.k === passage ? ' rig-rp__m--courant' : '')}>{m.t}</mark>)}
                </div>
              )}
            {depot && (
              <div className="rig-rp__joindre">
                <span className="rig-rp__pale">Joindre a</span>
                <select className="rig-rp__depots" aria-label="Depot ou joindre la piece" value={depot.chemin.join('/')}
                        onChange={(e) => vouloirDepot(e.target.value)}>
                  {vue.depots.map((d) => (
                    <option key={d.chemin.join('/')} value={d.chemin.join('/')}>{d.libelle} · {d.lieu}</option>
                  ))}
                </select>
                <button type="button" className="rig-rp__bouton" disabled={dejaDedans}
                        onClick={() => plan('rechercheJoindre', { piece: piece.chemin, depot: depot.chemin })}>
                  {dejaDedans ? 'Deja dans ce depot' : 'Joindre a ce depot'}
                </button>
                {vue.avis && <span className="rig-rp__avis" role="status">{vue.avis}</span>}
              </div>
            )}
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
