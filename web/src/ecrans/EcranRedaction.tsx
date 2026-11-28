// Ecran de la procedure PROC_REDA — redaction d'une decision judiciaire.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_REDA) :
//   - ETP_REDA/OPE_DECISION_JUDICIAIRE.Designer.cs : tout le formulaire.
//     Listes deroulantes : Type de decision (ultCBTypeDecision), Sens
//     general (ultCBSensGeneral), Ressort (ultCBRessort).
//     Dates : Date (ultDJDate), Date prevue (ultDJDatePrevue), « Mise en
//     delibere le » et « Prononce le ».
//     Avant dire droit (ultCBAvantDireDroit), Contradictoire
//     (ultCBContradictoire) et Execution provisoire (ultCBExecutionProvisoire)
//     sont aussi des LISTES (ULT_COMBO_*, Designer.cs:43-46) : leur libelle.
//     Cases a cocher : Debats publics, Decision publique ; « Mesure
//     d'instruction » est l'operation que RIG v2 montre quand le type de
//     decision porte une mesure liee.
//     Boutons de la barre : « Rechercher Delibere » (ultBtnDelibere),
//     « Rechercher Prononce » (ultBtnPrononce), « Gestion Document GED »
//     (OperationGestionDoc).
//     Les deux messages d'absence de resultat sont repris tels quels :
//     « Aucun appel d'affaire delibere » / « Aucun appel d'affaire
//     prononce ».
//   - Le compteur « Decision judiciaire liee a N documents » vient du meme
//     fichier : c'est la GED rattachee, pas un decompte d'affaires.
//
// Porte depuis Components/Ecrans/EcranRedaction.razor (lot 5) : meme balisage,
// memes classes CSS. Depuis le lot 10 du plan « RIG v3 100 % fonctionnel »
// (27/11/2026), les decisions viennent du pont (Pont/EcranRedactionBase.cs) :
// la base du greffe (Serveur/Socle/EcransMain/LectureRedaction.cs, regles de
// RIG v2 citees), ou, sans serveur, la demonstration, marquee « exemple ».
// Son geste (lot 8 du plan « parcours complet », 28/11/2026) : « Modifier la decision » puis Valider, comme le bouton Valider
// de OPE_DECISION_JUDICIAIRE -- ses listes (lues dans la base), ses dates et ses deux cases --, ecrit par le serveur
// (Serveur/Socle/EcransMain/EcritureRedaction.cs) ; la decision se relit ensuite.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { ProvenanceEcran, type ModeLecture } from '../provenance'
import { Annonce, type CompteRendu, type ResultatGesteVue } from './formes/commun'

export interface Decision {
  id: number
  numero: string
  affaire: string
  /** Sous l'onglet « Delibere » tant que la decision n'a pas d'appel d'affaire prononce. */
  delibere: boolean
  typeDecision: string
  sensGeneral: string
  ressort: string
  date: string
  datePrevue: string
  miseEnDelibere: string
  prononce: string
  avantDireDroit: string
  contradictoire: string
  executionProvisoire: string
  debatsPublics: boolean
  decisionPublique: boolean
  mesureInstruction: boolean
  documents: number
  mesures: string[]
}

export interface VueRedaction {
  decisions: Decision[]
  /** D'ou viennent les decisions (Pont/LectureEcranMain.cs, lot 10) : la base du greffe, ou la demonstration (« exemple »). */
  mode?: ModeLecture
  message?: string | null
  /** Branche : combien la base en compte. La lecture s'arrete au plafond (LectureRedaction.Plafond) : au-dela, l'ecran le dit. */
  total?: number
}

const ou = (valeur: string): string => (valeur === '' ? '—' : valeur)

/** Une liste de la fiche (Pont/EcranRedaction.cs, ListeDecision) : le titre de son champ dans RIG v2 et ses valeurs. */
interface ListeDecision { champ: string; obligatoire: boolean; valeurs: { valeur: string; libelle: string }[] }

/** Le libelle que la vue montre pour chaque champ-liste de la fiche (ValidationDecision, Contrats/EcransMain/LectureRedaction.cs). */
const LibelleDe: Record<string, (d: Decision) => string> = {
  'Sens général': (d) => d.sensGeneral, 'Ressort': (d) => d.ressort, 'Avant dire droit': (d) => d.avantDireDroit,
  'Contradictoire': (d) => d.contradictoire, 'Exécution provisoire': (d) => d.executionProvisoire,
}

/** jj/mm/aaaa -> aaaa-mm-jj (vide si vide). */
const versIso = (jour: string): string => (/^\d{2}\/\d{2}\/\d{4}$/.test(jour) ? `${jour.slice(6)}-${jour.slice(3, 5)}-${jour.slice(0, 2)}` : '')

const caracteres = (d: Decision): Array<[string, boolean]> => [
  ['Debats publics', d.debatsPublics],
  ['Decision publique', d.decisionPublique],
  ['Mesure d\'instruction', d.mesureInstruction],
]

export function EcranRedaction() {
  const [surDelibere, setSurDelibere] = useState(true)
  // null tant que le pont n'a pas repondu : ni decision ni « aucun appel », qui serait faux pendant ce court instant.
  const [vue, setVue] = useState<VueRedaction | null>(null)
  const [selection, setSelection] = useState<number | null>(null)
  // « Valider » de OPE_DECISION_JUDICIAIRE (lot 8 du plan « parcours complet ») : les listes de la base, la saisie, le compte rendu.
  const [relecture, setRelecture] = useState(0)
  const [listes, setListes] = useState<ListeDecision[]>([])
  const [saisie, setSaisie] = useState<Record<string, string> | null>(null)
  const [annonce, setAnnonce] = useState<CompteRendu>(null)
  const [envoi, setEnvoi] = useState(false)

  useEffect(() => {
    let perime = false
    void appeler<VueRedaction>('EcranRedaction.Decisions').then((v) => {
      if (perime) return
      setVue(v)
      // Comme avant le branchement : l'ecran s'ouvre sur la premiere decision deliberee ; relue, la decision choisie reste.
      setSelection((avant) => (avant !== null && v.decisions.some((x) => x.id === avant)) ? avant
        : v.decisions.find((x) => x.delibere)?.id ?? v.decisions[0]?.id ?? null)
    })
    return () => { perime = true }
  }, [relecture])

  useEffect(() => {
    let perime = false
    void appeler<{ listes: ListeDecision[] }>('EcranRedaction.Listes')
      .then((v) => { if (!perime) setListes(v?.listes ?? []) })
      .catch(() => { /* pas de pont : pas de geste */ })
    return () => { perime = true }
  }, [])

  function modifier(dec: Decision) {
    setAnnonce(null)
    const champs: Record<string, string> = {}
    for (const l of listes) champs[l.champ] = l.valeurs.find((v) => v.libelle === (LibelleDe[l.champ]?.(dec) ?? ''))?.valeur ?? ''
    champs['Date'] = versIso(dec.date)
    champs['Date prévue'] = versIso(dec.datePrevue)
    champs['Débats publics'] = dec.debatsPublics ? '1' : '0'
    champs['Décision publique'] = dec.decisionPublique ? '1' : '0'
    setSaisie(champs)
  }

  async function valider(dec: Decision) {
    if (!saisie) return
    setEnvoi(true)
    try {
      const r = await appeler<ResultatGesteVue>('EcranRedaction.Valider', dec.id, saisie)
      setAnnonce(r.fait || r.sansEcrivain ? r.message : { refus: r.message })
      if (r.fait) { setSaisie(null); setRelecture((n) => n + 1) }
    } catch {
      setAnnonce({ refus: "Le pont n'a pas répondu : rien n'est enregistré." })
    } finally {
      setEnvoi(false)
    }
  }

  const decisions = vue?.decisions ?? []
  const appels = decisions.filter((a) => a.delibere === surDelibere)
  const d = decisions.find((x) => x.id === selection) ?? null

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Appels d'affaires</div>
        {vue && <ProvenanceEcran mode={vue.mode} message={vue.message} quoi="Décisions" />}
        {vue && (vue.total ?? 0) > decisions.length && (
          // fix-ok: liste coupee sans le dire -- la route de dev rend total 689 pour 500 lignes (LectureRedaction.Plafond) et le
          // pont perdait ce total (Pont/EcranRedactionBase.cs) ; l'ecran dit maintenant « n affichees sur total ».
          <p className="rig-eg-note" role="note">
            {decisions.length} décisions affichées sur {vue.total} : la liste s'arrête au plafond de lecture.
          </p>
        )}

        <div className="rig-onglets">
          <button className={`rig-onglet${surDelibere ? ' rig-onglet--actif' : ''}`}
                  onClick={() => setSurDelibere(true)}>Delibere</button>
          <button className={`rig-onglet${!surDelibere ? ' rig-onglet--actif' : ''}`}
                  onClick={() => setSurDelibere(false)}>Prononce</button>
        </div>

        <div className="rig-criteres__bloc">
          {appels.map((ap) => (
            <button key={ap.id} className={`rig-bascule${selection === ap.id ? ' rig-bascule--actif' : ''}`}
                    onClick={() => setSelection(ap.id)}>
              <span className="rig-bascule__temoin"></span>
              <span>{ap.numero} — {ap.affaire}</span>
            </button>
          ))}
          {vue && appels.length === 0 && (
            // Messages repris mot pour mot de la v2.
            <div className="rig-table__vide" style={{ padding: 'var(--rig-esp-3)' }}>
              {surDelibere ? 'Aucun appel d\'affaire delibere' : 'Aucun appel d\'affaire prononce'}
            </div>
          )}
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Decision judiciaire</div>
          <span className="rig-resultats-proc__compte">
            {d ? `Decision judiciaire liee a ${d.documents} documents` : 'aucune decision selectionnee'}
          </span>
        </div>

        {d ? (
          <>
            <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Type de decision</span>
                  <input className="rig-saisie-champ__saisie" value={d.typeDecision} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Sens general</span>
                  <input className="rig-saisie-champ__saisie" value={d.sensGeneral} readOnly />
                </label>
              </div>

              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Ressort</span>
                  <input className="rig-saisie-champ__saisie" value={d.ressort} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Execution provisoire</span>
                  <input className="rig-saisie-champ__saisie" value={ou(d.executionProvisoire)} readOnly />
                </label>
              </div>

              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Avant dire droit</span>
                  <input className="rig-saisie-champ__saisie" value={ou(d.avantDireDroit)} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Contradictoire</span>
                  <input className="rig-saisie-champ__saisie" value={ou(d.contradictoire)} readOnly />
                </label>
              </div>

              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Date</span>
                  <input className="rig-saisie-champ__saisie" value={d.date} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Date prevue</span>
                  <input className="rig-saisie-champ__saisie" value={d.datePrevue} readOnly />
                </label>
              </div>

              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Mise en delibere le</span>
                  <input className="rig-saisie-champ__saisie" value={ou(d.miseEnDelibere)} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Prononce le</span>
                  <input className="rig-saisie-champ__saisie" value={ou(d.prononce)} readOnly />
                </label>
              </div>
            </div>

            <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
              <div className="rig-kicker">Caracteres de la decision</div>
              {/* Le bloc parent est une colonne flexible : sans ce conteneur,
                  chaque pastille serait etiree sur toute la largeur. */}
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--rig-esp-2)' }}>
                {caracteres(d).map(([libelle, oui]) => (
                  <span key={libelle} className={`rig-etat rig-etat--${oui ? 'ok' : 'attente'}`}>{libelle}</span>
                ))}
              </div>
            </div>

            {vue?.mode === 'serveur' && listes.length > 0 && (
              <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
                {saisie === null ? (
                  <div className="rig-criteres__actions">
                    <button className="rig-bouton" onClick={() => modifier(d)}>Modifier la décision</button>
                  </div>
                ) : (
                  <>
                    <div className="rig-kicker">Décision judiciaire — Valider</div>
                    {listes.map((l) => (
                      <label key={l.champ} className="rig-saisie-champ">
                        <span className="rig-saisie-champ__libelle">{l.champ}{l.obligatoire ? ' *' : ''}</span>
                        <select className="rig-saisie-champ__saisie" value={saisie[l.champ] ?? ''}
                                onChange={(e) => setSaisie({ ...saisie, [l.champ]: e.target.value })}>
                          <option value="">—</option>
                          {l.valeurs.map((v) => <option key={v.valeur} value={v.valeur}>{v.libelle}</option>)}
                        </select>
                      </label>
                    ))}
                    <div className="rig-criteres__paire">
                      <label className="rig-saisie-champ">
                        <span className="rig-saisie-champ__libelle">Date</span>
                        <input className="rig-saisie-champ__saisie" type="date" value={saisie['Date'] ?? ''}
                               onChange={(e) => setSaisie({ ...saisie, Date: e.target.value })} />
                      </label>
                      <label className="rig-saisie-champ">
                        <span className="rig-saisie-champ__libelle">Date prévue</span>
                        <input className="rig-saisie-champ__saisie" type="date" value={saisie['Date prévue'] ?? ''}
                               onChange={(e) => setSaisie({ ...saisie, 'Date prévue': e.target.value })} />
                      </label>
                    </div>
                    {(['Débats publics', 'Décision publique'] as const).map((c) => (
                      <label key={c} className="rig-saisie-champ">
                        <span className="rig-saisie-champ__libelle">{c}</span>
                        <input type="checkbox" checked={saisie[c] === '1'} onChange={(e) => setSaisie({ ...saisie, [c]: e.target.checked ? '1' : '0' })} />
                      </label>
                    ))}
                    <div className="rig-criteres__actions">
                      <button className="rig-bouton rig-bouton--fort" disabled={envoi} onClick={() => void valider(d)}>Valider</button>{' '}
                      <button className="rig-bouton rig-bouton--plat" onClick={() => setSaisie(null)}>Annuler</button>
                    </div>
                  </>
                )}
                <Annonce texte={annonce} />
              </div>
            )}

            <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
              <div className="rig-kicker">Mesures d'instruction liees</div>
              {d.mesures.length === 0
                ? <div className="rig-table__vide" style={{ padding: 'var(--rig-esp-3)' }}>Aucune mesure liee a cette decision.</div>
                : d.mesures.map((m) => <div key={m} className="rig-table__mono">{m}</div>)}
            </div>
          </>
        ) : (
          <div className="rig-table__vide" style={{ padding: 'var(--rig-esp-4)' }}>
            {vue === null ? 'Lecture des decisions…' : 'Choisissez un appel d\'affaire dans la liste de gauche.'}
          </div>
        )}
      </section>
    </div>
  )
}
