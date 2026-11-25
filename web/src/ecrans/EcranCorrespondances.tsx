// Ecran « Ancienne touche, nouvelle touche » : « a maquetter » dans le carnet (SOC-027 T02), menu Configurer ›
// Prendre en main la V3. Sans origine RIG v2 : la fiche n'existait pas dans l'ancien RIG ; elle est construite a partir de ses
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
//
// LISTE MODERNE (24/11/2026, plan des listes modernes, lot 5) : la fiche prend les briques de composants/ListeModerne.tsx --
// tete (synthese, « Aller à un raccourci… » Ctrl K qui filtre la fiche affichee, Entree ouvre l'ecran de la V3 du raccourci
// reste seul), vues = le critere « Dans la V3 » existant avec le compte de chaque statut (elles remplacent la phrase qui
// donnait ces comptes), statut en point colore, pied avec l'aide « un clic sur l'ecran de la V3 l'ouvre ». La fiche garde
// son retour a la ligne (RETOUR_A_LA_LIGNE) : elle se lit, elle ne se parcourt pas. L'aide « ? » (AideTouchesEcran) n'est
// pas une liste de resultats : elle garde ses pastilles. fix-ok: ajout du socle des listes en plusieurs pas (lot 5), pas un correctif.

import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { KeyboardEvent, MouseEvent } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, estBlanc } from '../criteres'
import { ecouterEchapDabord, ecouterRaccourcis } from '../plan/clavier'
import { EnteteListe, ListeModerne, PiedListe, PointEtat, VuesListe, type Teinte } from '../composants/ListeModerne'

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

/** Le libelle de chaque statut. */
export const STATUTS: Record<LigneCorrespondance['statut'], { libelle: string }> = {
  identique: { libelle: 'Même touche' },
  'nouvelle-touche': { libelle: 'Nouvelle touche' },
  geste: { libelle: 'Par un geste' },
  supprimee: { libelle: 'Supprimée' },
  'a-venir': { libelle: 'Écran à venir' },
  nouveau: { libelle: 'Nouveau dans la V3' },
}

/** La teinte du point d'etat de chaque statut, dans la fiche et dans l'aide « ? » (palette des listes, rig-listes.css). */
export const TEINTES_STATUT: Record<LigneCorrespondance['statut'], Teinte> = {
  identique: 'vert', 'nouvelle-touche': 'bleu', geste: 'orange', supprimee: 'rouge', 'a-venir': 'gris', nouveau: 'violet',
}

const ETATS_FICHE: Record<VueEcranCorrespondances['etatFiche'], string> = {
  'avant-beta': 'Avant les retours des greffes bêta',
  'retours-en-cours': 'Retours des greffes bêta en cours',
  livrable: 'Retours des greffes bêta intégrés',
}

const Explication = { fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)', maxWidth: 'none' } as const
/**
 * La fiche se LIT : chaque texte entier, revenu a la ligne (retour du chef du plan, 21/11/2026 : « Ancienne t… »,
 * « Nouveau dans la \ » et les commentaires etaient coupes). Les grilles de la charte tiennent sur une ligne
 * (.rig-table tbody td, regle du 17/09/2026) parce qu'on y cherche une ligne parmi des centaines ; ici, on lit chaque
 * ligne. La regle est donc propre a cette table (meme poids que celle de la charte, plus loin dans la page : elle
 * l'emporte), points d'etat et boutons compris. Sous 900 px, la table garde sa largeur et le cadre defile : une colonne
 * ne tombe pas a quelques lettres. A l'impression, le bloc de recherche disparait.
 */
export const RETOUR_A_LA_LIGNE = [
  '.rig-table.rig-fiche-touches { min-width: 900px; }',
  '.rig-table.rig-fiche-touches thead th, .rig-table.rig-fiche-touches tbody td {'
    + ' white-space: normal; overflow: visible; text-overflow: clip; max-width: none; overflow-wrap: break-word; vertical-align: top; }',
  // Un point d'etat revenu a la ligne (« Nouveau dans la / V3 ») garde son point face a la PREMIERE ligne : c'est la
  // regle du socle (.rig-lm-etat, rig-listes.css), et non plus un contournement de cet ecran (lot 8 des listes modernes).
  '.rig-table.rig-fiche-touches .rig-lm-etat, .rig-table.rig-fiche-touches .rig-bouton { white-space: normal; text-align: left; }',
  '@media print { .rig-criteres { display: none; } }',
].join('\n')

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
  return <PointEtat teinte={TEINTES_STATUT[s]}>{STATUTS[s].libelle}</PointEtat>
}

export interface ProprietesEcranCorrespondances {
  /** Ouvre un ecran de la V3 par son code (geste « ouvrirCode » de la page). */
  ouvrir: (code: string) => void
}

export function EcranCorrespondances({ ouvrir }: ProprietesEcranCorrespondances) {
  const [vue, setVue] = useState<VueEcranCorrespondances | null>(null)
  const [recherche, setRecherche] = useState('')
  const [statut, setStatut] = useState('')
  // « Aller à un raccourci… » (Ctrl K) : filtre la fiche AFFICHEE, sans toucher aux criteres.
  const [filtreRapide, setFiltreRapide] = useState('')

  useEffect(() => {
    let vivant = true
    void appeler<VueEcranCorrespondances>('EcranCorrespondances.Ouvrir').then((v) => { if (vivant) setVue(v) })
    return () => { vivant = false }
  }, [])

  if (!vue) return <div className="rig-proc__corps"><p className="rig-vide">Chargement de la fiche…</p></div>

  /** Les lignes que montre un statut (« » = tous), la recherche saisie comprise : la fiche, et le compte de chaque vue. */
  const avecStatut = (s: string) => vue.lignes.filter((l) => (s === '' || l.statut === s) && repond(l, recherche))
  const trouvees = avecStatut(statut)
  const affichees = trouvees.filter((l) => repond(l, filtreRapide))
  const compte = (s: LigneCorrespondance['statut']) => vue.lignes.filter((l) => l.statut === s).length
  const parmiAffichees = (s: LigneCorrespondance['statut']) => affichees.filter((l) => l.statut === s).length
  const nouvelles = parmiAffichees('nouvelle-touche')
  const supprimees = parmiAffichees('supprimee')

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

      <ListeModerne etiquette="Ancienne touche, nouvelle touche">
        <EnteteListe titre="Ancienne touche, nouvelle touche"
                     detail="Pour chaque touche de l'ancien RIG, ce qui la remplace dans la V3"
                     synthese={<>{affichees.length === vue.lignes.length
                                    ? `${vue.lignes.length} raccourci${vue.lignes.length > 1 ? 's' : ''}`
                                    : `${affichees.length} sur ${vue.lignes.length}`}
                       {' · '}<b>{nouvelles}</b>{nouvelles > 1 ? ' nouvelles touches' : ' nouvelle touche'}
                       {' · '}<b>{supprimees}</b>{supprimees > 1 ? ' supprimées' : ' supprimée'}</>}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller à un raccourci…',
                               valider: () => {
                                 const seule = affichees.length === 1 ? affichees[0]! : null
                                 if (seule?.ouvrable && seule.ecranV3) ouvrir(seule.ecranV3)
                               } }} />

        <VuesListe vues={[{ cle: '', libelle: 'Tout', compte: avecStatut('').length },
                          ...Object.entries(STATUTS).map(([s, { libelle }]) => ({ cle: s, libelle, compte: avecStatut(s).length }))]}
                   active={statut} choisir={setStatut} />

        <div className="rig-table-cadre">
          {/* Le texte entier de chaque ligne, revenu a la ligne : RETOUR_A_LA_LIGNE. */}
          <style>{RETOUR_A_LA_LIGNE}</style>
          <table className="rig-table rig-lm-table rig-fiche-touches">
            <colgroup>
              <col style={{ width: '14%' }} /><col style={{ width: '10%' }} /><col style={{ width: '15%' }} />
              <col style={{ width: '11%' }} /><col style={{ width: '10%' }} /><col style={{ width: '13%' }} /><col />
            </colgroup>
            <thead>
              <tr>
                {['Écran de l\'ancien RIG', 'Ancienne touche', 'Ce qu\'elle faisait', 'Dans la V3', 'Nouvelle touche', 'Écran de la V3', 'Comment']
                  .map((t) => <th key={t}>{t}</th>)}
              </tr>
            </thead>
            <tbody>
              {affichees.map((l) => (
                <tr key={l.numero}>
                  <td>{l.ecranV2}</td>
                  <td><Touche t={l.ancienneTouche} /></td>
                  <td>{l.geste}</td>
                  <td><PointEtat teinte={TEINTES_STATUT[l.statut]}>{STATUTS[l.statut].libelle}</PointEtat></td>
                  <td><Touche t={l.nouvelleTouche} /></td>
                  <td>
                    {l.nomEcranV3 && l.ouvrable && l.ecranV3
                      ? <button type="button" className="rig-bouton rig-bouton--plat" title={`Ouvrir « ${l.nomEcranV3} » pour essayer la touche`}
                                onClick={() => ouvrir(l.ecranV3!)}>{l.nomEcranV3}</button>
                      : <span>{l.nomEcranV3 ?? '—'}</span>}
                  </td>
                  <td style={Explication}>{l.explication}</td>
                </tr>
              ))}
              {trouvees.length === 0 && (
                <tr><td colSpan={7} className="rig-table__vide">Aucun raccourci ne répond à cette recherche.</td></tr>
              )}
              {trouvees.length > 0 && affichees.length === 0 && (
                <tr><td colSpan={7} className="rig-table__vide">
                  {`Aucun des ${trouvees.length} raccourcis ne contient « ${filtreRapide.trim()} ».`}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe aide="Un clic sur l'écran de la V3 l'ouvre pour essayer.">
          <span><b>{affichees.length}</b>{affichees.length > 1 ? ' raccourcis affichés' : ' raccourci affiché'}</span>
        </PiedListe>
      </ListeModerne>
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
  // useLayoutEffect, pas useEffect : la fenetre s'ouvre a la reponse du pont (hors frappe), et un useEffect ne
  // passe qu'apres l'affichage -- Echap presse a cet instant n'etait pas entendu (test « des qu'elle s'affiche »).
  useLayoutEffect(() => {
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
