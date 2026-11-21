// Ecran « Ancienne touche, nouvelle touche » : « a maquetter » dans le carnet (SOC-027 T02), menu Configurer ›
// Mon poste. Sans origine RIG v2 : la fiche n'existait pas dans l'ancien RIG ; elle est construite a partir de ses
// procedures (inventaire outille des touches de son code, Pont/InventaireTouchesV2.cs) et du menu de la maquette.
//
// Pour chaque raccourci de la V2, l'ecran ou il agissait, ce qu'il faisait, et dans la V3 : la nouvelle touche, ou le
// geste qui le remplace, ou pourquoi il est supprime (CA02). Elle se cherche (un mot : une touche, un ecran, un geste)
// et s'imprime. Chaque ligne ouvre l'ecran de la V3 ou essayer la touche (CA04, joue par tests/SoclePreferencesCheck).
// Tout vient de Pont/EcranCorrespondances.cs.
//
// AideTouchesEcran (plus bas) est l'aide « ? » d'un ecran de la V3 : ses touches, l'ancienne touche a cote (CA03).
//
// fix-ok: capture du 2026-11-21 (bureau cache, 1920 px) -- une section seule tombait dans la colonne ETROITE de
// .rig-proc__corps (prevue pour le bloc des criteres) : tableau ecrase a 320 px. La recherche et le filtre vont donc dans
// l'aside .rig-criteres, la fiche dans .rig-resultats-proc, comme l'ecran des compteurs.

import { useEffect, useRef, useState } from 'react'
import type { KeyboardEvent, MouseEvent } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, estBlanc } from '../criteres'
import { ecouterEchapDabord, ecouterRaccourcis } from '../plan/clavier'

export interface LigneCorrespondance {
  numero: number
  ecranV2: string
  ancienneTouche: string
  geste: string
  nouvelleTouche: string | null
  statut: 'identique' | 'nouvelle-touche' | 'geste' | 'supprimee' | 'a-venir' | 'nouveau'
  explication: string
  ecranV3: string | null
  nomEcranV3: string | null
  ouvrable: boolean
  construit: boolean
  source: string | null
}

export interface RetourBeta {
  greffe: string
  ligne: number
  remarque: string
  integre: boolean
}

export interface VueEcranCorrespondances {
  lignes: LigneCorrespondance[]
  etatFiche: 'avant-beta' | 'retours-en-cours' | 'livrable'
  retoursBeta: RetourBeta[]
}

/** Le libelle de chaque statut, et sa teinte (classes rig-etat de la charte). */
export const STATUTS: Record<LigneCorrespondance['statut'], { libelle: string; teinte: string }> = {
  identique: { libelle: 'Même touche', teinte: 'ok' },
  'nouvelle-touche': { libelle: 'Nouvelle touche', teinte: 'action' },
  geste: { libelle: 'Par un geste', teinte: 'attente' },
  supprimee: { libelle: 'Supprimée', teinte: 'alerte' },
  'a-venir': { libelle: 'Écran à venir', teinte: '' },
  nouveau: { libelle: 'Nouveau dans la V3', teinte: 'action' },
}

const ETATS_FICHE: Record<VueEcranCorrespondances['etatFiche'], string> = {
  'avant-beta': 'Avant les retours des greffes bêta',
  'retours-en-cours': 'Retours des greffes bêta en cours',
  livrable: 'Retours des greffes bêta intégrés',
}

const Message = { margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const
const Grille = { tableLayout: 'fixed' } as const
const Coupe = { overflow: 'hidden', textOverflow: 'ellipsis' } as const
const Explication = { fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)', maxWidth: 'none' } as const
/** Sous un champ du bloc de gauche : la note dense de l'ecran des compteurs. */
const Aide = { margin: '0 0 var(--rig-esp-2)', fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)' } as const

/** Une ligne repond-elle a la recherche ? Tous ses textes comptent : l'ecran, les deux touches, le geste, l'explication. */
export function repond(l: LigneCorrespondance, recherche: string): boolean {
  if (estBlanc(recherche)) return true
  const t = recherche.trim()
  return [l.ecranV2, l.ancienneTouche, l.geste, l.nouvelleTouche ?? '', l.nomEcranV3 ?? '', l.explication, STATUTS[l.statut].libelle]
    .some((x) => contientSansCasse(x, t))
}

/** Une touche, en touche du clavier ; un tiret quand il n'y en a pas. */
function Touche({ t }: { t: string | null }) {
  return t && t !== '—' ? <kbd className="rig-vged__agir-touche">{t}</kbd> : <span aria-label="aucune">—</span>
}

function Statut({ s }: { s: LigneCorrespondance['statut'] }) {
  const { libelle, teinte } = STATUTS[s]
  return <span className={teinte ? `rig-etat rig-etat--${teinte}` : 'rig-etat'}>{libelle}</span>
}

export interface ProprietesEcranCorrespondances {
  /** Ouvre un ecran de la V3 par son code (geste « ouvrirCode » de la page). */
  ouvrir: (code: string) => void
}

export function EcranCorrespondances({ ouvrir }: ProprietesEcranCorrespondances) {
  const [vue, setVue] = useState<VueEcranCorrespondances | null>(null)
  const [recherche, setRecherche] = useState('')
  const [statut, setStatut] = useState('')

  useEffect(() => {
    let vivant = true
    void appeler<VueEcranCorrespondances>('EcranCorrespondances.Ouvrir').then((v) => { if (vivant) setVue(v) })
    return () => { vivant = false }
  }, [])

  if (!vue) return <div className="rig-proc__corps"><p className="rig-vide">Chargement de la fiche…</p></div>

  const trouvees = vue.lignes.filter((l) => (statut === '' || l.statut === statut) && repond(l, recherche))
  const compte = (s: LigneCorrespondance['statut']) => vue.lignes.filter((l) => l.statut === s).length

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Chercher dans la fiche</div>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Chercher une touche, un écran, un geste</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>
        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Dans la V3</span>
          <select className="rig-saisie-champ__saisie" value={statut} onChange={(e) => setStatut(e.target.value)}>
            <option value="">Tout</option>
            {Object.entries(STATUTS).map(([s, { libelle }]) => <option key={s} value={s}>{libelle} ({compte(s as LigneCorrespondance['statut'])})</option>)}
          </select>
        </label>
        <p style={Aide}>
          <span className={`rig-etat rig-etat--${vue.etatFiche === 'livrable' ? 'ok' : 'attente'}`}>{ETATS_FICHE[vue.etatFiche]}</span>
        </p>
        <p style={Aide}>Sur un écran ouvert, « ? » (hors d'un champ) montre ses touches, avec l'ancienne à côté.</p>
        <div className="rig-criteres__actions">
          <button type="button" className="rig-bouton" onClick={() => window.print()}>Imprimer la fiche</button>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Ancienne touche, nouvelle touche</div>
          <span className="rig-resultats-proc__compte">
            {trouvees.length === vue.lignes.length ? `${vue.lignes.length} raccourci(s)` : `${trouvees.length} sur ${vue.lignes.length}`}
          </span>
        </div>

        <p style={Message}>
          Pour chaque touche de l'ancien RIG, ce qui la remplace dans la V3 : {compte('identique')} identique(s),
          {' '}{compte('nouvelle-touche')} nouvelle(s) touche(s), {compte('geste')} remplacée(s) par un geste,
          {' '}{compte('supprimee')} supprimée(s), {compte('a-venir')} sur un écran à venir. Un clic sur l'écran de la V3 l'ouvre pour essayer.
        </p>

        <div className="rig-table-cadre">
          {/* Une ligne par ligne de grille (regle de la charte, 17/09/2026) : le texte entier est dans l'infobulle et a l'impression. */}
          <style>{'@media print { .rig-fiche-touches td { white-space: normal; max-width: none; } .rig-criteres { display: none; } }'}</style>
          <table className="rig-table rig-fiche-touches" style={Grille}>
            <colgroup>
              <col style={{ width: '14%' }} /><col style={{ width: '8%' }} /><col style={{ width: '15%' }} />
              <col style={{ width: '9%' }} /><col style={{ width: '9%' }} /><col style={{ width: '13%' }} /><col />
            </colgroup>
            <thead>
              <tr>
                {['Écran de l\'ancien RIG', 'Ancienne touche', 'Ce qu\'elle faisait', 'Dans la V3', 'Nouvelle touche', 'Écran de la V3', 'Comment']
                  .map((t) => <th key={t} title={t} style={Coupe}>{t}</th>)}
              </tr>
            </thead>
            <tbody>
              {trouvees.map((l) => (
                <tr key={l.numero}>
                  <td title={l.ecranV2} style={Coupe}>{l.ecranV2}</td>
                  <td><Touche t={l.ancienneTouche} /></td>
                  <td title={l.geste} style={Coupe}>{l.geste}</td>
                  <td><Statut s={l.statut} /></td>
                  <td><Touche t={l.nouvelleTouche} /></td>
                  <td style={Coupe}>
                    {l.nomEcranV3 && l.ouvrable && l.ecranV3
                      ? <button type="button" className="rig-bouton rig-bouton--plat" title={`Ouvrir « ${l.nomEcranV3} » pour essayer la touche`}
                                onClick={() => ouvrir(l.ecranV3!)}>{l.nomEcranV3}</button>
                      : <span>{l.nomEcranV3 ?? '—'}</span>}
                  </td>
                  <td title={l.explication} style={Explication}>{l.explication}</td>
                </tr>
              ))}
              {trouvees.length === 0 && (
                <tr><td colSpan={7} className="rig-table__vide">Aucun raccourci ne répond à cette recherche.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}

export interface ProprietesAideTouchesEcran {
  /** Le code de l'ecran ouvert. */
  code: string
  /** L'ecran est visible (son onglet est montre) : « ? » n'est ecoute que la. */
  actif: boolean
}

/**
 * L'aide « ? » d'un ecran de la V3 (SOC-027 CA03) : hors d'un champ, « ? » montre les touches de cet ecran avec
 * l'ancienne touche de la V2 a cote ; Echap ou « ? » la ferment. Les touches viennent de la fiche (EcranCorrespondances.DeLEcran).
 */
export function AideTouchesEcran({ code, actif }: ProprietesAideTouchesEcran) {
  const [lignes, setLignes] = useState<LigneCorrespondance[] | null>(null)
  useEffect(() => { if (!actif) setLignes(null) }, [actif])
  useEffect(() => {
    if (!actif || lignes) return
    return ecouterRaccourcis((touche) => {
      if (touche !== '?') return false
      void appeler<LigneCorrespondance[]>('EcranCorrespondances.DeLEcran', code).then(setLignes)
      return true
    })
  }, [actif, lignes, code])
  return lignes ? <FenetreAide lignes={lignes} fermer={() => setLignes(null)} /> : null
}

function FenetreAide({ lignes, fermer }: { lignes: LigneCorrespondance[]; fermer: () => void }) {
  const boite = useRef<HTMLDivElement>(null)
  const fermerCourant = useRef(fermer)
  fermerCourant.current = fermer
  useEffect(() => {
    const avant = document.activeElement instanceof HTMLElement ? document.activeElement : null
    boite.current?.focus()
    const arreter = ecouterEchapDabord(() => fermerCourant.current())
    return () => { arreter(); avant?.focus() }
  }, [])
  const touche = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key !== '?') return
    e.preventDefault()
    fermer()
  }
  const retenir = (e: MouseEvent) => e.stopPropagation()
  return (
    <div className="rig-voile rig-voile--haut" onMouseDown={fermer}>
      <div ref={boite} className="rig-impression rig-vged__aide" role="dialog" aria-modal="true"
           aria-label="Touches de cet écran" tabIndex={-1} onKeyDown={touche} onMouseDown={retenir}>
        <div className="rig-vged__aide-tete">
          <div className="rig-kicker">Touches de cet écran · avec l'ancienne touche</div>
          <button type="button" className="rig-bouton rig-bouton--icone rig-bouton--plat" title="Fermer (Echap)"
                  aria-label="Fermer l'aide" onClick={fermer}>
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12"></path><path d="M18 6L6 18"></path></svg>
          </button>
        </div>
        <table className="rig-table">
          <thead><tr><th>Dans la V3</th><th>Ancienne touche</th><th>Ce que fait la touche</th></tr></thead>
          <tbody>
            {lignes.map((l) => (
              <tr key={l.numero}>
                <td>{l.nouvelleTouche ? <Touche t={l.nouvelleTouche} /> : <Statut s={l.statut} />}</td>
                <td><Touche t={l.ancienneTouche} /></td>
                <td>{l.geste}<div style={Explication}>{l.explication}</div></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
