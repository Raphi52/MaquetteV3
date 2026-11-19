// LA GRILLE DE L'ACCUEIL : chaque tuile reste ou on l'a posee.
//
// Une tuile se DEPLACE en la saisissant par son en-tete et se REDIMENSIONNE
// par la poignee de son coin bas-droit. Pendant le geste, la tuile suit le
// pointeur case par case (calcul dans pose.ts) ; au lacher, la pose part au C#
// (geste « poser » / « dimensionner ») qui la retient dans accueil.json. Jusqu'au
// retour de la vue, la tuile garde la pose lachee : elle ne revient jamais en
// arriere un instant.
//
// Porte en React (lot 1 du plan « finir le passage a React ») depuis la grille
// de Home.razor et activerPose / montrerTuile de wwwroot/rig-accueil.js.

import { useEffect, useRef, useState, type CSSProperties, type PointerEvent as EvenementPointeur } from 'react'
import { deplacer, dimensionner, memePose, pasColonne, type PoseCase } from './pose'
import { Actualites, Activite, Alertes, AlertesBis, Metier, Nouveautes, Performance, Reglages, Taches } from './Tuiles'
import { arreter, type GesteAccueil, type SignalVue, type TuileVue } from './types'

/** Le z-index d'une tuile saisie : au-dessus de ses voisines pendant le geste. */
const Z_SAISIE = 60

interface Prise {
  mode: 'deplacer' | 'taille'
  id: string
  depart: PoseCase
  courante: PoseCase
  x: number
  y: number
  pas: number
}

function lirePose(tuile: Element): PoseCase {
  const entier = (nom: string) => parseInt(tuile.getAttribute(nom) ?? '', 10) || 1
  return { colonne: entier('data-col'), ligne: entier('data-ligne'), largeur: entier('data-larg'), hauteur: entier('data-haut') }
}

function Corps({ t, geste }: { t: TuileVue, geste: GesteAccueil }) {
  if (t.reglages) return <Reglages vue={t.reglages} geste={geste} />
  if (t.performance) return <Performance mesures={t.performance} />
  if (t.alertes) return <Alertes vue={t.alertes} geste={geste} />
  if (t.alertesBis) return <AlertesBis vue={t.alertesBis} geste={geste} />
  if (t.taches) return <Taches lignes={t.taches} geste={geste} />
  if (t.nouveautes) return <Nouveautes nouveautes={t.nouveautes} geste={geste} />
  if (t.actualites) return <Actualites vue={t.actualites} geste={geste} />
  if (t.activite) return <Activite vue={t.activite} geste={geste} />
  // Les huit widgets metier partagent UN corps, parametre par leur categorie.
  if (t.metier) return <Metier id={t.id} vue={t.metier} geste={geste} />
  return null
}

export interface ProprietesGrille {
  tuiles: TuileVue[]
  repliee: boolean
  signal: SignalVue | null
  geste: GesteAccueil
}

export function Grille({ tuiles, repliee, signal, geste }: ProprietesGrille) {
  const zone = useRef<HTMLDivElement>(null)
  const prise = useRef<Prise | null>(null)
  const gesteCourant = useRef(geste)
  const [enCours, setEnCours] = useState<Prise | null>(null)
  const [lachee, setLachee] = useState<{ id: string, pose: PoseCase } | null>(null)

  useEffect(() => { gesteCourant.current = geste }, [geste])

  // Le geste continue hors de la tuile, et meme hors de la grille : le suivi
  // du pointeur et le lacher sont ecoutes sur la fenetre.
  useEffect(() => {
    const surDeplacement = (ev: PointerEvent) => {
      const p = prise.current
      if (!p) return
      const dx = ev.clientX - p.x
      const dy = ev.clientY - p.y
      const pose = p.mode === 'taille' ? dimensionner(p.depart, dx, dy, p.pas) : deplacer(p.depart, dx, dy, p.pas)
      if (memePose(pose, p.courante)) return
      prise.current = { ...p, courante: pose }
      setEnCours(prise.current)
    }
    const surRelache = () => {
      const p = prise.current
      if (!p) return
      prise.current = null
      setEnCours(null)
      setLachee({ id: p.id, pose: p.courante })
      const c = p.courante
      if (p.mode === 'taille') gesteCourant.current('dimensionner', { id: p.id, largeur: c.largeur, hauteur: c.hauteur })
      else gesteCourant.current('poser', { id: p.id, colonne: c.colonne, ligne: c.ligne })
    }
    window.addEventListener('pointermove', surDeplacement)
    window.addEventListener('pointerup', surRelache)
    window.addEventListener('pointercancel', surRelache)
    return () => {
      window.removeEventListener('pointermove', surDeplacement)
      window.removeEventListener('pointerup', surRelache)
      window.removeEventListener('pointercancel', surRelache)
    }
  }, [])

  // La pose lachee tient jusqu'a ce que la vue du C# la porte (memes bornes
  // des deux cotes : elle la porte toujours, cf. pose.ts).
  useEffect(() => {
    if (!lachee) return
    const t = tuiles.find((x) => x.id === lachee.id)
    if (!t || memePose(t, lachee.pose)) setLachee(null)
  }, [tuiles, lachee])

  const surAppui = (ev: EvenementPointeur<HTMLDivElement>) => {
    if (ev.button !== 0 || !zone.current) return
    const cible = ev.target as Element
    const demarrer = (mode: Prise['mode'], tuile: Element) => {
      const depart = lirePose(tuile)
      prise.current = {
        mode, id: tuile.getAttribute('data-tuile') ?? '', depart, courante: depart,
        x: ev.clientX, y: ev.clientY, pas: pasColonne(zone.current!.getBoundingClientRect().width),
      }
      setEnCours(prise.current)
    }

    // Redimensionnement : poignee du coin bas-droit de la tuile.
    const poignee = cible.closest('.rig-widget__taille')
    if (poignee) {
      const tuile = poignee.closest('[data-tuile]')
      if (!tuile) return
      demarrer('taille', tuile)
      poignee.setPointerCapture?.(ev.pointerId)
      ev.preventDefault()
      ev.stopPropagation()
      return
    }

    // TOUT bouton de l'entete doit recevoir son clic : demarrer un deplacement
    // ici appellerait preventDefault et le clic n'arriverait jamais. La garde
    // porte sur « button » et non sur une classe precise, sinon chaque nouveau
    // bouton d'entete redeviendrait muet en silence.
    const tete = cible.closest('.rig-widget__tete')
    if (!tete || cible.closest('button')) return
    const tuile = tete.closest('[data-tuile]')
    if (!tuile) return
    demarrer('deplacer', tuile)
    tete.setPointerCapture?.(ev.pointerId)
    ev.preventDefault()
  }

  // « Proue » : un resume ou un lien « Tout voir » a rendu la grille ; la
  // tuile visee defile dans la vue et s'entoure d'or un instant, pour que
  // l'oeil la retrouve parmi les autres.
  const dernierSignal = useRef(signal?.numero ?? 0)
  const [signalee, setSignalee] = useState<string | null>(null)
  const signalId = signal?.id
  const signalNumero = signal?.numero
  useEffect(() => {
    if (signalId === undefined || signalNumero === undefined || signalNumero === dernierSignal.current) return
    dernierSignal.current = signalNumero
    const tuile = [...(zone.current?.querySelectorAll('[data-tuile]') ?? [])]
      .find((el) => el.getAttribute('data-tuile') === signalId)
    if (!tuile) return
    tuile.scrollIntoView?.({ block: 'nearest', behavior: 'smooth' })
    setSignalee(signalId)
    const minuterie = setTimeout(() => setSignalee(null), 1400)
    return () => clearTimeout(minuterie)
  }, [signalId, signalNumero])

  return (
    <div ref={zone} className={'rig-grille ' + (repliee ? 'rig-grille--repliee' : '') + (enCours ? ' rig-grille--pose' : '')}
         onPointerDown={surAppui}>
      {tuiles.map((t) => {
        const saisie = enCours !== null && enCours.id === t.id
        const p: PoseCase = saisie ? enCours.courante : lachee?.id === t.id ? lachee.pose : t
        // Le z-index inline de la tuile (son rang d'empilement) cree un
        // CONTEXTE : un widget metier deploye prend un rang qui bat toutes
        // les autres (Rang = 96, calcule par le C#).
        const style: CSSProperties = {
          gridColumn: p.colonne + ' / span ' + p.largeur,
          gridRow: p.ligne + ' / span ' + p.hauteur,
          zIndex: saisie ? Z_SAISIE : t.rang,
        }
        return (
          <section key={t.id}
                   className={'rig-widget ' + (t.maitresse ? 'rig-widget--maitresse' : '') + ' ' + (t.devant ? 'rig-widget--devant' : '')
                              + (saisie ? ' rig-widget--saisi' : '') + (signalee === t.id ? ' rig-widget--signale' : '')}
                   data-tuile={t.id}
                   data-col={p.colonne} data-ligne={p.ligne}
                   data-larg={p.largeur} data-haut={p.hauteur}
                   style={style}>
            {/* Plus de filet colore en tete de tuile (choix du 2026-09-17) : la
                couleur ne vit plus que dans les barres verticales des alertes. */}
            <header className="rig-widget__tete">
              <span className="rig-widget__poignee">{'⋮⋮'}</span>
              {/* Un widget metier porte DEJA son titre complet dans son propre
                  en-tete : le redire ici l'ecrivait DEUX FOIS. */}
              {t.avecTitre && <span className="rig-widget__titre">{t.titre}</span>}
              <button className={'rig-widget__reglages ' + (t.reglagesOuverts ? 'rig-widget__reglages--ouvert' : '')}
                      title="Reglages de la tuile"
                      onClick={() => geste('reglagesTuile', t.id)}
                      onMouseDown={arreter}>{'⚙'}</button>
            </header>
            {' '}
            <div className="rig-widget__corps">
              <Corps t={t} geste={geste} />
            </div>
            {' '}
            <span className="rig-widget__taille" title="Redimensionner"></span>
          </section>
        )
      })}
    </div>
  )
}
