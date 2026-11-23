// L'ECRAN GENERIQUE : la mise en page « Proue » de Commandes web (pilule, tuiles chiffrees,
// liste en accordeon, detail en escalier, Valider n/m), nourrie par le DESCRIPTEUR d'une
// procedure du menu (Pont/EcransGeneres/, un fichier par verbe). Il ne connait aucune
// procedure : une de plus = un descripteur de plus, sans une ligne de React.
//
// La maquette vit dans l'ecran : filtrer, replier un groupe, ouvrir une ligne, franchir une
// marche, Valider. Rien ne passe par le pont (une maquette ne rend rien a Metier/) : l'etat
// se garde ici. Les etats qui portent les gestes sont ceux du descripteur (AFaire, EnCours,
// Validee) ; les autres (EnAttente, Rejetee...) sont figes.

import { useContext, useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { contientSansCasse } from '../criteres'
import { EcranVisible } from '../plan/visibilite'
import type { DescripteurEcranVue, MarcheEcranVue } from '../plan/types'
import { DureeBascule } from './EcranCommandesWeb'

export interface ProprietesEcranGenere {
  descripteur: DescripteurEcranVue
}

/** Les trois etats qui portent les gestes : le descripteur les nomme ainsi (EcransGeneresCheck le verifie). */
const AFAIRE = 'AFaire'
const ENCOURS = 'EnCours'
const VALIDEE = 'Validee'

/** Une ligne telle que l'ecran la tient : son etat et ses marches franchies (leurs rangs). */
interface LigneTenue {
  nom: string
  precisions: string
  groupe: string
  etat: string
  faites: number[]
}

const trait = { fill: 'none', stroke: 'currentColor', strokeWidth: 1.6, strokeLinecap: 'round', strokeLinejoin: 'round' } as const

/** Le dessin de chaque tuile : les icones de l'accueil, comme dans Commandes web. */
const ICONES: Record<string, ReactNode> = {
  'a-traiter': <svg viewBox="0 0 16 16"><path d="M2.5 9.5h3l1 2h3l1-2h3M3.5 4h9l1 5.5V13h-11V9.5z" {...trait} /></svg>,
  'en-attente': <svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="5.6" {...trait} /><path d="M8 4.8V8l2.2 1.6" {...trait} /></svg>,
  validee: <svg viewBox="0 0 16 16"><path d="m3.5 8.5 3 3 6-6.5" {...trait} strokeWidth={1.8} /></svg>,
  rejetee: <svg viewBox="0 0 16 16"><path d="M4.5 4.5l7 7M11.5 4.5l-7 7" {...trait} strokeWidth={1.8} /></svg>,
  'saisie-manuelle': <svg viewBox="0 0 16 16"><path d="M10.5 2.8l2.7 2.7-7.4 7.4H3.1v-2.7z" {...trait} /></svg>,
}

/** La teinte de chaque tuile (rig-chiffre--N), selon son icone : la meme table que Commandes web. */
const TEINTE: Record<string, number> = { 'a-traiter': 1, 'en-attente': 4, validee: 2, rejetee: 3, 'saisie-manuelle': 4 }

/** Une ligne se modifie tant qu'elle est a faire ou en cours ; les autres etats sont figes. */
const modifiable = (etat: string) => etat === AFAIRE || etat === ENCOURS

/** Les marches obligatoires que la ligne n'a pas encore franchies : ce qui bloque Valider. */
function manquantes(d: DescripteurEcranVue, faites: number[]): MarcheEcranVue[] {
  return d.marches.filter((m, k) => m.obligatoire && !faites.includes(k))
}

/** La part d'un etat dans la liste, en pour cent : elle remplit l'anneau de sa tuile. */
const part = (nombre: number, total: number) => (total === 0 ? 0 : Math.round((nombre * 100) / total))

export function EcranGenere({ descripteur: d }: ProprietesEcranGenere) {
  const [lignes, poserLignes] = useState<LigneTenue[]>(() => d.lignes.map((l) => ({ ...l })))
  const [texte, poserTexte] = useState('')
  const [etatChoisi, poserEtatChoisi] = useState<string | null>(null)
  const [plies, poserPlies] = useState<ReadonlySet<string>>(() => new Set())
  const [ouverte, poserOuverte] = useState<number | null>(null)
  // Vrai le temps de la bascule liste <-> colonne : le fondu, dure DureeBascule.
  const [bascule, poserBascule] = useState(false)
  const pilule = useRef<HTMLDivElement>(null)
  const visible = useContext(EcranVisible)

  useEffect(() => {
    if (!bascule) return
    const fin = window.setTimeout(() => poserBascule(false), DureeBascule)
    return () => window.clearTimeout(fin)
  }, [bascule])

  // Ctrl K pose le curseur dans la pilule, sur l'ecran affiche (comme Commandes web).
  useEffect(() => {
    if (!visible) return
    const surTouche = (e: KeyboardEvent) => {
      if (!(e.ctrlKey || e.metaKey) || e.key.toLowerCase() !== 'k') return
      const champ = pilule.current?.querySelector('input')
      if (!champ) return
      e.preventDefault()
      champ.focus()
      champ.select()
    }
    window.addEventListener('keydown', surTouche, true)
    return () => window.removeEventListener('keydown', surTouche, true)
  }, [visible])

  const recherche = texte.trim()
  const visibles = lignes
    .map((l, i) => ({ ...l, i }))
    .filter((l) => (etatChoisi === null || l.etat === etatChoisi)
      && (recherche === '' || [l.nom, l.precisions, l.groupe].some((c) => contientSansCasse(c, recherche))))
  const groupes = d.groupes
    .map((titre) => ({ titre, lignes: visibles.filter((l) => l.groupe === titre) }))
    .filter((g) => g.lignes.length > 0)
  const tuiles = d.etats.map((e) => ({ ...e, nombre: lignes.filter((l) => l.etat === e.code).length }))
  const libelleEtat = (code: string) => d.etats.find((e) => e.code === code)?.libelle ?? code
  const iconeEtat = (code: string) => d.etats.find((e) => e.code === code)?.icone ?? 'a-traiter'

  const ouvrir = (i: number) => { poserOuverte(i); poserBascule(true) }
  const fermer = () => { poserOuverte(null); poserBascule(true) }
  const basculerPlie = (titre: string) => poserPlies((avant) => {
    const apres = new Set(avant)
    if (!apres.delete(titre)) apres.add(titre)
    return apres
  })
  // Franchir une marche la pose ou la retire ; la ligne passe « en cours » des la premiere, et
  // redevient « a faire » quand la derniere est retiree.
  const basculerMarche = (i: number, k: number) => poserLignes((avant) => avant.map((l, j) => {
    if (j !== i || !modifiable(l.etat)) return l
    const faites = l.faites.includes(k) ? l.faites.filter((x) => x !== k) : [...l.faites, k]
    return { ...l, faites, etat: faites.length > 0 ? ENCOURS : AFAIRE }
  }))
  const valider = (i: number) => poserLignes((avant) => avant.map((l, j) =>
    j === i && modifiable(l.etat) && manquantes(d, l.faites).length === 0 ? { ...l, etat: VALIDEE } : l))

  // fix-ok: lignes[ouverte] vaut T | undefined (noUncheckedIndexedAccess) : mesure par le typecheck (TS2322, TS18048 sur sel).
  const sel = ouverte === null ? null : (lignes[ouverte] ?? null)
  return (
    <div className="rig-tcw">
      {/* La recherche en pilule de l'accueil : chaque frappe filtre tuiles et liste. */}
      <div className="rig-tcw__cherche">
        <div className="rig-recherche rig-recherche--fixe rig-tcw__pilule" ref={pilule}>
          <span className="rig-tcw__loupe" aria-hidden="true">
            <svg viewBox="0 0 24 24"><circle cx="11" cy="11" r="6" /><path d="M20 20l-4.5-4.5" /></svg>
          </span>
          <input className="rig-recherche__saisie" aria-label={d.recherche} placeholder={d.recherche}
                 value={texte} onChange={(e) => poserTexte(e.currentTarget.value)} />
          <span className="rig-touche">Ctrl K</span>
        </div>
      </div>
      <p className="rig-tcw__discret">Critères : {d.criteres.join(' · ')}</p>

      {/* Une tuile chiffree par etat : elle FILTRE la liste ; la tuile choisie porte l'anneau d'or. */}
      <div className="rig-tcw__tuiles" aria-label={d.sujet}>
        {tuiles.map((t) => (
          <button key={t.code} type="button" aria-pressed={etatChoisi === t.code}
                  className={`rig-chiffre rig-chiffre--${TEINTE[t.icone] ?? 4} rig-tcw__tuile ${etatChoisi === t.code ? 'rig-chiffre--deplie' : ''}`}
                  onClick={() => poserEtatChoisi(etatChoisi === t.code ? null : t.code)}>
            <span className="rig-chiffre__anneau" aria-hidden="true">
              <span className="rig-chiffre__cercle rig-chiffre__cercle--jauge"
                    style={{ '--rig-anneau-cible': part(t.nombre, lignes.length) } as CSSProperties}></span>
              <span className="rig-chiffre__rond">{ICONES[t.icone]}</span>
            </span>
            <span className="rig-chiffre__texte">
              <span className="rig-chiffre__libelle">{t.libelle}</span>
              <b className="rig-chiffre__valeur">{t.nombre}</b>
            </span>
          </button>
        ))}
      </div>

      {/* La liste et le detail se partagent la scene : une ligne ouverte se pose a cote de la liste. */}
      <div className={['rig-tcw__scene', sel && 'rig-tcw__scene--ouverte', bascule && 'rig-tcw__scene--bascule'].filter(Boolean).join(' ')}
           style={{ '--tcw-duree': `${DureeBascule}ms` } as CSSProperties}>
        <section className="rig-tcw__liste" aria-label={d.sujet}>
          <div className="rig-tcw__liste-entete">
            <span className="rig-tcw__compteur" title={`${visibles.length} ${d.sujet}`}>
              <b className="rig-tcw__compteur-nombre">{visibles.length}</b>
              <span className="rig-tcw__compteur-detail">{d.sujet}</span>
            </span>
          </div>

          {visibles.length === 0 ? (
            <div className="rig-tcw__vide">Rien ne correspond à la recherche.</div>
          ) : sel === null ? (
            <div className="rig-tcw__invite">Sélectionnez une ligne pour la traiter.</div>
          ) : null}

          <div className="rig-tcw__lignes">
            {groupes.map((g) => {
              const replie = plies.has(g.titre)
              return (
                <div key={g.titre} className={`rig-tcw__groupe rig-tcw__rang-${d.rang} ${replie ? 'rig-tcw__groupe--plie' : ''}`}>
                  <button className="rig-tcw__groupe-tete" aria-expanded={!replie} onClick={() => basculerPlie(g.titre)}>
                    <svg className="rig-tcw__chevron" viewBox="0 0 12 12" aria-hidden="true"><path d="M3 4.5l3 3 3-3" /></svg>
                    <span className="rig-tcw__groupe-repere"></span>
                    <span className="rig-tcw__groupe-titre">{g.titre}</span>
                    <span className="rig-tcw__groupe-nombre">{g.lignes.length}</span>
                    {replie && <span className="rig-tcw__groupe-resume">{g.lignes.map((l) => l.nom).join(' · ')}</span>}
                  </button>
                  <div className="rig-tcw__groupe-corps" inert={replie}>
                    <div className="rig-tcw__groupe-lignes">
                      {g.lignes.map((l) => (
                        <button key={l.i} className={`rig-tcw__cmd rig-tcw__rang-${d.rang} ${ouverte === l.i ? 'rig-tcw__cmd--active' : ''}`}
                                style={{ '--tcw-ordre': l.i } as CSSProperties}
                                aria-current={ouverte === l.i ? 'true' : undefined} onClick={() => ouvrir(l.i)}>
                          <span className="rig-tcw__cmd-qui">
                            <span className="rig-tcw__cmd-nom">{l.nom}</span>
                            <span className="rig-tcw__cmd-precisions">{l.precisions}</span>
                          </span>
                          <span className="rig-tcw__cmd-type">{libelleEtat(l.etat)}</span>
                          <span className={`rig-tcw__avancement ${manquantes(d, l.faites).length === 0 ? 'rig-tcw__avancement--prete' : ''}`}
                                title="Étapes franchies">{l.faites.length}/{d.marches.length}</span>
                        </button>
                      ))}
                    </div>
                  </div>
                </div>
              )
            })}
          </div>
        </section>

        {sel !== null && ouverte !== null && (
          // key : chaque ligne ouverte est un panneau NEUF, qui s'ouvre en fondu.
          <Panneau key={ouverte} d={d} ligne={sel} libelle={libelleEtat(sel.etat)} icone={iconeEtat(sel.etat)}
                   franchir={(k) => basculerMarche(ouverte, k)} valider={() => valider(ouverte)} fermer={fermer} />
        )}
      </div>
    </div>
  )
}

/** Le detail d'une ligne : son en-tete, l'escalier de ses marches, et le pied qui valide. */
function Panneau({ d, ligne, libelle, icone, franchir, valider, fermer }: {
  d: DescripteurEcranVue; ligne: LigneTenue; libelle: string; icone: string
  franchir: (k: number) => void; valider: () => void; fermer: () => void
}) {
  const figee = !modifiable(ligne.etat)
  const manque = manquantes(d, ligne.faites)
  return (
    <section className="rig-tcw__panneau" aria-label={ligne.nom}>
      <div className="rig-tcw__panneau-haut">
        <div className="rig-tcw__panneau-tete">
          <span className={`rig-tcw__type rig-tcw__rang-${d.rang}`}>{d.titre}</span>
          <span className="rig-tcw__etat"><span className={`rig-tcw__icone rig-tcw__icone--${icone}`}></span>{libelle}</span>
          <span className="rig-tcw__espace"></span>
          <button className="rig-tcw__fermer rig-tcw__fermer--rond" title="Fermer" aria-label="Fermer" onClick={fermer}>×</button>
        </div>
        <div className="rig-tcw__titre">{ligne.nom}</div>
        <div className="rig-tcw__reference">{ligne.precisions}</div>
      </div>

      <div className="rig-tcw__detail">
        <div className="rig-tcw__escalier">
          {d.marches.map((m, k) => (
            <Marche key={k} m={m} faite={ligne.faites.includes(k)} figee={figee} franchir={() => franchir(k)} />
          ))}
        </div>
      </div>

      <footer className="rig-tcw__pied">
        <div className="rig-tcw__pied-ligne">
          <span className={`rig-tcw__voyant rig-tcw__voyant--fin ${manque.length === 0 ? 'rig-tcw__voyant--pret' : ''}`} aria-hidden="true"></span>
          <div className="rig-tcw__decisions">
            <button className="rig-bouton rig-bouton--fort rig-tcw__valider" disabled={figee || manque.length > 0}
                    title={manque.length > 0 ? `Reste à faire : ${manque.map((m) => m.titre).join(' · ')}` : undefined}
                    onClick={valider}>
              {d.valider}<b className="rig-tcw__valider-compte">{ligne.faites.length}/{d.marches.length}</b>
            </button>
          </div>
        </div>
        {figee ? (
          <div className="rig-tcw__blocage">
            {ligne.etat === VALIDEE ? 'Ligne validée : elle ne se modifie plus.' : `Ligne figée (${libelle}) : aucun geste ici.`}
          </div>
        ) : manque.length > 0 ? (
          <div className="rig-tcw__blocage">Reste à faire : {manque.map((m) => m.titre).join(' · ')}</div>
        ) : null}
      </footer>
    </section>
  )
}

/** Une marche de l'escalier : son voyant (coche quand elle est franchie), la regle de la fiche et les champs. */
function Marche({ m, faite, figee, franchir }: { m: MarcheEcranVue; faite: boolean; figee: boolean; franchir: () => void }) {
  return (
    <div className={`rig-tcw__marche ${faite ? 'rig-tcw__marche--faite' : 'rig-tcw__marche--a-faire'}`}>
      <span className="rig-tcw__voyant" role="img" aria-label={`${m.titre} : ${faite ? 'fait' : 'à faire'}`} title={m.titre}>
        {faite && <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7.5" /></svg>}
      </span>
      <div className="rig-tcw__marche-corps">
        {/* fix-ok: mesure sur la capture du bureau cache : dans la colonne flex de marche-corps, le lien s'etirait et son texte se centrait, « facultative » tombait seul sous le titre. Le titre et le lien sont sur une ligne, comme « Modifier » dans Commandes web. */}
        <div className="rig-tcw__marche-ligne">
          <span className="rig-tcw__section">{m.titre}</span>
          {!m.obligatoire && <span className="rig-tcw__marche-resume">facultative</span>}
          {/* Ligne figee : aucun lien (un lien disabled gardait l'allure active ; le bandeau « Ligne validee » dit pourquoi). */}
          {!figee && (
            <button className="rig-tcw__lien" style={{ marginLeft: 'auto' }} onClick={franchir}>
              {faite ? 'Reprendre' : 'Terminer l’étape'}
            </button>
          )}
        </div>
        <p className="rig-tcw__discret">{m.consigne}</p>
        {m.champs.length > 0 && (
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--rig-esp-1)' }}>
            {m.champs.map((c) => <span key={c} className="rig-tcw__pastille">{c}</span>)}
          </div>
        )}
      </div>
    </div>
  )
}
