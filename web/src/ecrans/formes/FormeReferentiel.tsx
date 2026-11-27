// FORME « REFERENTIEL » : une table de reglage (modes de paiement, mandataires, cabinets, articles tarifaires,
// destinataires, imprimantes...). Une GRILLE editable aux colonnes de la fiche RIG v2 : rechercher, ajouter une ligne,
// modifier en ligne (les cellules deviennent des champs), supprimer. Ajouter, Enregistrer et Supprimer vont a l'ecrivain
// de la procedure (types Ajouter, Modifier, Supprimer ; les cellules par libelle de colonne) ; ecrit, la table est relue
// (plan « RIG v3 100 % fonctionnel », lot 2). Sans ecrivain, ils le disent et ne changent rien : la ligne en cours de
// modification reste ouverte, marquee non enregistree.
// LES AUTRES GESTES DE LIGNE (lot 11) : un menu de ligne RIG v2 qui offre plus que modifier et supprimer (« Dupliquer » un
// document océrisé, PROC_NUM_PROC.Dupliquer) les nomme dans descripteur.gestes ; chacun s'ajoute au menu de la ligne et va a
// l'ecrivain (type Agir, son nom, la ligne). fix-ok: mesure par le controle final du lot 11 (27/11/2026) -- le menu n'avait
// que Modifier et Supprimer, si bien qu'un geste V2 de ligne n'atteignait jamais son ecrivain.
// LA BASE (lot 2) : quand le lecteur sait chercher, la recherche part dans la base (useRecherche). Une ligne de la base
// ouverte en modification lit SA fiche (useFiche) : chaque colonne prend la valeur du champ de meme libelle, seules
// celles que l'ecrivain enregistre se saisissent, la fiche entiere se lit dessous, et une ligne que RIG v2 verrouille ne
// s'enregistre pas (son message). Modifier n'envoie que les colonnes CHANGEES (une absente garde la valeur de la base),
// comme Enregistrer d'une Fiche. fix-ok: mesure par le controle final du lot 2 (26/11/2026) -- la grille filtrait ses
// lignes deja lues (texteLigne, jamais chercher) et ne lisait aucune fiche : une ligne de la base s'ouvrait [nom, '', ...].
// LA LIGNE NEUVE (lot 14) : « Ajouter » dans un onglet, ou « Dupliquer » une ligne (descripteur.duplication : RIG v2
// SQLBase_Handler.Copie, PROC_GTABREF), ouvre une ligne de champs ; dessous, les sections que le descripteur fait saisir a
// l'ajout dans ce groupe (sectionsAjout : les champs obligatoires d'une zone, que la grille ne porte pas). Une copie part de
// la ligne copiee : ses cellules sauf la premiere (la cle, que la V2 ne copie pas), et les champs de ses sections lus dans sa
// fiche ; Enregistrer l'envoie en Ajouter, ids = [la ligne copiee], et l'ecrivain recopie le reste. Controle final du lot 14
// (27/11/2026) : « Ajouter une zone » et « Dupliquer » de « Tables de reference » n'avaient aucun chemin, la ligne neuve
// n'envoyant que ses colonnes.

import { Fragment, useEffect, useState } from 'react'
import { EnteteListe, ListeModerne, MenuLigne, montant, PiedListe, VuesListe } from '../../composants/ListeModerne'
import type { ChampEcranVue, DescripteurEcranVue, LigneEcranVue } from '../../plan/types'
import {
  Annonce, ChampSaisie, GroupesVides, NoteRecherche, Provenance, Sections, champLu, cleChamp, enExemple, Exemple, nombreDe,
  parcoursDe, refus, synthese, useAnnonce, useFiche, useGeste, useLignes, useRecherche, type FicheVue, type LigneTenue, type Saisies,
  type VueLignesEcran,
} from './commun'

const Tous = '\u0000tous'

/**
 * La ligne en cours de modification (son rang, ou « nouvelle »), ses valeurs saisies, et celles qu'elle avait a l'ouverture
 * (lues dans la fiche, sinon ses cellules) : Modifier n'envoie que ce qui en differe. Une ligne neuve porte aussi son groupe
 * (l'onglet, ou celui de la ligne copiee), la ligne qu'elle copie, et les champs saisis de ses sections.
 */
type Edition = {
  cle: number | 'nouvelle'; valeurs: string[]; ouvertes: string[]
  groupe?: string; source?: LigneTenue; saisies?: Saisies
}

/** Aucun critere : la grille ne cherche que son texte. */
const SansCritere: Saisies = {}

/** Le champ de la fiche lue qui porte une colonne : le premier de ce libelle, toutes sections confondues. */
export const champDeColonne = (fiche: FicheVue | null | undefined, libelle: string) =>
  fiche?.sections.flatMap((s) => s.champs).find((c) => c.libelle === libelle)

/** Les cellules d'une ligne dans l'ordre des colonnes ; sans valeurs, la premiere colonne porte le nom. */
export const cellules = (d: DescripteurEcranVue, l: LigneTenue): string[] =>
  d.colonnes.map((_, k) => l.valeurs?.[k] ?? (k === 0 ? l.nom : ''))

/**
 * Une ligne SANS cellules : celles que lisent les lecteurs de la base (Serveur/Socle/EcransGeneres/Lecteur*.cs), qui
 * ne rendent que nom, precisions, groupe et etat (Contrats/EcransGeneres/LignesEcran.cs, LigneLue ; le pont envoie
 * des valeurs vides), et les exemples qui n'en ont pas (Mandataires). Ce qui la distingue (abrege, SIREN, CNBF...)
 * est dans ses precisions : etalees sous les colonnes, jamais remplacees par des « — » (controle final du lot 1,
 * 25/11/2026 : « Mandataires » sur la base montrait le nom puis des « — » partout).
 */
export const sansValeurs = (l: LigneEcranVue) => !l.valeurs?.length

/** Les cases d'une ligne sans cellules : le nom dans la premiere colonne, les precisions sur toutes les autres. */
export function CasesSansValeurs({ d, vue, l }: { d: DescripteurEcranVue; vue: VueLignesEcran | null; l: LigneTenue }) {
  const precisions = l.precisions === '' ? '—' : l.precisions
  return (
    <>
      <td title={`${l.nom} — ${l.precisions}`}>
        <b>{l.nom}</b>
        {d.colonnes.length === 1 && <> <span className="rig-eg-precisions">{precisions}</span></>}
        {enExemple(vue, l) && <> <Exemple /></>}
      </td>
      {d.colonnes.length > 1 && <td colSpan={d.colonnes.length - 1} className="rig-eg-precisions">{precisions}</td>}
    </>
  )
}

/** Une cellule lue : une case en Oui / Non, un montant a la francaise, le reste tel quel (vide : « — »). */
export function lisible(c: ChampEcranVue, valeur: string): string {
  if (c.type === 'Case') return valeur === 'oui' ? 'Oui' : valeur === '' ? '—' : 'Non'
  if (c.type === 'Montant') { const n = nombreDe(valeur); return Number.isNaN(n) ? (valeur || '—') : montant(n) }
  if (c.type === 'Date' && /^\d{4}-\d{2}-\d{2}$/.test(valeur)) return valeur.split('-').reverse().join('/')
  return valeur === '' ? '—' : valeur
}

export function FormeReferentiel({ d }: { d: DescripteurEcranVue }) {
  const lues = useLignes(d)
  const { vue, lignes, relire } = lues
  const [texte, poserTexte] = useState('')
  const [groupe, poserGroupe] = useState(Tous)
  const [edition, poserEdition] = useState<Edition | null>(null)
  const [annonce, annoncer] = useAnnonce()

  const { visibles: trouvees, enBase, filtre } = useRecherche(lues, texte, SansCritere)
  const visibles = trouvees.filter((l) => groupe === Tous || l.groupe === groupe)
  const nbColonnes = d.colonnes.length + 1
  const enModification = edition && edition.cle !== 'nouvelle' ? lignes.find((l) => l.i === edition.cle) : undefined
  // La fiche lue : celle de la ligne modifiee, ou de la ligne copiee (ses champs pre-remplissent la copie).
  const { fiche: vueFiche } = useFiche(d, enModification ?? edition?.source, vue)
  const fiche = vueFiche?.fiche ?? null
  // Les sections saisies sous une ligne neuve : celles que le descripteur fait saisir a l'ajout dans son groupe.
  const sectionsNeuves = (e: Edition) =>
    (e.groupe === undefined ? [] : d.sectionsAjout?.[e.groupe] ?? []).flatMap((t) => d.sections.filter((s) => s.titre === t))
  // La fiche arrive apres l'ouverture : ses valeurs remplacent les cellules que l'agent n'a pas encore changees ; sous une
  // copie, elles remplissent les champs de ses sections que l'agent n'a pas encore saisis.
  useEffect(() => {
    if (!fiche) return
    poserEdition((e) => {
      if (!e) return e
      if (e.cle === 'nouvelle') {
        if (!e.source) return e
        const lus = sectionsNeuves(e).flatMap((s) => s.champs.map((c) => [cleChamp(s.titre, c.libelle), champLu(fiche, s.titre, c.libelle)?.valeur ?? '']))
        const saisis = Object.entries(e.saisies ?? {}).filter(([, v]) => v !== '')
        return { ...e, saisies: Object.fromEntries([...lus, ...saisis]) }
      }
      const lus = d.colonnes.map((c, k) => champDeColonne(fiche, c.libelle)?.valeur ?? e.ouvertes[k] ?? '')
      return { ...e, valeurs: e.valeurs.map((v, k) => (v === e.ouvertes[k] ? lus[k]! : v)), ouvertes: lus }
    })
  }, [fiche])
  // Avec la fiche lue, une colonne se saisit si l'ecrivain l'enregistre et que RIG v2 ne verrouille pas la ligne.
  const modifiable = (k: number) => !fiche || (!fiche.lectureSeule && champDeColonne(fiche, d.colonnes[k]!.libelle)?.modifiable === true)
  const modifier = (l: LigneTenue) => { poserEdition({ cle: l.i, valeurs: cellules(d, l), ouvertes: cellules(d, l) }); annoncer(null) }
  const changer = (k: number, v: string) => poserEdition((e) => e && { ...e, valeurs: e.valeurs.map((x, j) => (j === k ? v : x)) })
  const jouer = useGeste(d, vue, annoncer)
  const parColonne = (valeurs: string[], garder: (k: number) => boolean = () => true) =>
    Object.fromEntries(d.colonnes.flatMap((c, k) => (garder(k) ? [[c.libelle, valeurs[k] ?? '']] : [])))
  const enregistrer = () => {
    if (!edition) return
    const ligne = edition.cle === 'nouvelle' ? undefined : lignes.find((l) => l.i === edition.cle)
    if (!ligne) {
      // Les champs de ses sections, saisis ou vides (une copie efface ainsi un champ de la ligne copiee).
      const saisies = sectionsNeuves(edition).flatMap((s) => s.champs.map((c) => cleChamp(s.titre, c.libelle)))
        .map((k) => [k, edition.saisies?.[k] ?? ''] as const)
      void jouer({ type: 'Ajouter', groupe: edition.groupe ?? (groupe !== Tous ? groupe : d.groupes[0] ?? d.titre),
                   ids: edition.source?.id ? [edition.source.id] : [], valeurs: { ...parColonne(edition.valeurs), ...Object.fromEntries(saisies) } },
        refus('Enregistrer', ': la ligne n’est pas ajoutée à la table'), () => { poserEdition(null); relire() })
      return
    }
    const changees = parColonne(edition.valeurs, (k) => modifiable(k) && edition.valeurs[k] !== edition.ouvertes[k])
    if (Object.keys(changees).length === 0) {
      annoncer(fiche
        ? `Aucune colonne n’est modifiée : « ${ligne.nom} » garde les valeurs de la base.`
        : refus('Enregistrer', ': aucune colonne n’est modifiée, la ligne garde les valeurs de la table'))
      return
    }
    void jouer({ type: 'Modifier', groupe: ligne.groupe, ids: [ligne.id], valeurs: changees },
      refus('Enregistrer', ': la ligne n’est pas modifiée dans la table'), () => { poserEdition(null); relire() })
  }
  const supprimer = (l: LigneTenue) => void jouer({ type: 'Supprimer', groupe: l.groupe, ids: [l.id], valeurs: {} },
    refus('Supprimer', `: « ${l.nom} » reste dans la table`), relire)
  const agir = (geste: string, l: LigneTenue) => void jouer({ type: 'Agir', nom: geste, groupe: l.groupe, ids: [l.id], valeurs: {} },
    refus(geste, `: « ${l.nom} » n’est pas modifié dans la table`), relire)
  const annuler = () => { poserEdition(null); annoncer(null) }
  const ajouter = () => {
    const vides = d.colonnes.map(() => '')
    poserEdition({ cle: 'nouvelle', valeurs: vides, ouvertes: vides, groupe: groupe !== Tous ? groupe : d.groupes[0] ?? d.titre, saisies: {} })
    annoncer(null)
  }
  // « Dupliquer » (RIG v2 SQLBase_Handler.Copie) : une ligne neuve de son groupe, ses cellules sauf la cle.
  const dupliquer = (l: LigneTenue) => {
    const copiees = cellules(d, l).map((v, k) => (k === 0 ? '' : v))
    poserEdition({ cle: 'nouvelle', valeurs: copiees, ouvertes: copiees, groupe: l.groupe, source: l, saisies: {} })
    annoncer(null)
  }

  // fix-ok: modifier() ouvre une ligne sans cellules (sansValeurs) en cellules(d, l) = [nom, '', ...] ; rien ne se devine
  // de ses precisions, qui restent donc affichees en lecture sous les champs, avec la raison (sur « Mandataires », Abrege
  // et SIREN s'ouvraient vides et les precisions ne se voyaient plus nulle part ; test « garde ses precisions visibles »).
  const ligneEnEdition = (cle: number | 'nouvelle', l?: LigneTenue) => edition && edition.cle === cle && (
    <Fragment key={`edition-${cle}`}>
      <tr className="rig-eg-ligne--edition">
        {d.colonnes.map((c, k) => (
          <td key={c.libelle}>
            {cle === 'nouvelle' || modifiable(k)
              ? <ChampSaisie champ={c} valeur={edition.valeurs[k] ?? ''} changer={(v) => changer(k, v)} compact />
              : <span className="rig-eg-lu__valeur" aria-label={c.libelle}>{lisible(c, edition.valeurs[k] ?? '')}</span>}
          </td>
        ))}
        <td className="rig-eg-edition-actions">
          <button type="button" className="rig-lm-outil rig-lm-outil--fort" disabled={cle !== 'nouvelle' && fiche?.lectureSeule === true}
                  onClick={enregistrer}>Enregistrer</button>
          <button type="button" className="rig-lm-outil" onClick={annuler}>Annuler</button>
        </td>
      </tr>
      {l && fiche && (
        <tr className="rig-eg-ligne--edition rig-eg-edition-lu">
          <td colSpan={nbColonnes}>
            {fiche.lectureSeule && fiche.motif
              ? <p className="rig-eg-note" role="note">Ligne en lecture seule : {fiche.motif}.</p>
              : <p className="rig-eg-note" role="note">Fiche lue dans la base du greffe{vue?.greffe ? ` ${vue.greffe}` : ''} : seules les colonnes qui s’enregistrent se saisissent.</p>}
            <Sections sections={parcoursDe(d, l.groupe).sections} saisies={{}} changer={() => {}} lecture fiche={fiche} />
          </td>
        </tr>
      )}
      {cle === 'nouvelle' && (edition.source || sectionsNeuves(edition).length > 0) && (
        <tr className="rig-eg-ligne--edition rig-eg-edition-ajout">
          <td colSpan={nbColonnes}>
            {edition.source && (
              <p className="rig-eg-note" role="note">Copie de « {edition.source.nom} » : ce qui n’est pas saisi ici est repris de la ligne copiée.</p>
            )}
            <Sections sections={sectionsNeuves(edition)} saisies={edition.saisies ?? {}}
                      changer={(k, v) => poserEdition((e) => e && { ...e, saisies: { ...e.saisies, [k]: v } })} />
          </td>
        </tr>
      )}
      {l && !fiche && sansValeurs(l) && (
        <tr className="rig-eg-ligne--edition rig-eg-edition-lu">
          <td colSpan={nbColonnes}>
            Précisions de la ligne : <span className="rig-eg-precisions">{l.precisions === '' ? '—' : l.precisions}</span>
            {' '}— les colonnes de cette ligne ne sont pas encore lues une à une : seul son nom est repris dans les champs.
          </td>
        </tr>
      )}
    </Fragment>
  )

  return (
    <div className="rig-proc__corps rig-eg" data-forme="Referentiel">
      <ListeModerne etiquette={d.titre}>
        <EnteteListe titre={d.titre} synthese={synthese(d, vue, lignes)}
                     filtre={{ valeur: texte, changer: poserTexte, indication: d.recherche }} />
        {vue && <Provenance vue={vue} />}
        <NoteRecherche enBase={enBase} />
        <VuesListe vues={[{ cle: Tous, libelle: 'Toutes', compte: lignes.length },
                          ...d.groupes.map((g) => ({ cle: g, libelle: g, compte: lignes.filter((l) => l.groupe === g).length }))]}
                   active={groupe} choisir={poserGroupe}>
          {/* Sans « Ajouter » quand la fiche RIG v2 n'ajoute pas de ligne (descripteur.ajout faux : Dates de travail). */}
          {d.ajout !== false && (
            <button type="button" className="rig-lm-outil rig-lm-outil--fort" disabled={edition?.cle === 'nouvelle'}
                    onClick={ajouter}>Ajouter</button>
          )}
        </VuesListe>
        <Annonce texte={annonce} />
        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table rig-eg-table rig-eg-grille">
            <thead>
              <tr>
                {d.colonnes.map((c) => <th key={c.libelle} title={c.libelle} className={c.type === 'Montant' ? 'rig-lm-droite' : undefined}>{c.libelle}</th>)}
                <th aria-label="Actions" style={{ width: edition ? '190px' : '40px' }} />
              </tr>
            </thead>
            <tbody>
              {ligneEnEdition('nouvelle')}
              {visibles.map((l) => ligneEnEdition(l.i, l) || (
                <tr key={l.i} onDoubleClick={() => modifier(l)}>
                  {sansValeurs(l) ? <CasesSansValeurs d={d} vue={vue} l={l} /> : cellules(d, l).map((v, k) => {
                    const c = d.colonnes[k]!
                    return (
                      <td key={c.libelle} title={k === 0 ? `${l.nom} — ${l.precisions}` : v}
                          className={c.type === 'Montant' ? 'rig-lm-droite' : undefined}>
                        {k === 0 ? <b>{lisible(c, v)}</b> : lisible(c, v)}
                        {k === 0 && enExemple(vue, l) && <> <Exemple /></>}
                      </td>
                    )
                  })}
                  <td className="rig-lm-cellule-menu">
                    <MenuLigne libelle={`Actions — ${l.nom}`} actions={[
                      { libelle: 'Modifier', agir: () => modifier(l) },
                      // Sans « Supprimer » quand la fiche RIG v2 ne supprime pas (descripteur.suppression faux : Cabinets).
                      ...(d.suppression !== false ? [{ libelle: 'Supprimer', agir: () => supprimer(l) }] : []),
                      // « Dupliquer » une ligne quand la fiche RIG v2 le fait (descripteur.duplication : Tables de reference).
                      ...(d.duplication ? [{ libelle: 'Dupliquer', agir: () => dupliquer(l) }] : []),
                      ...parcoursDe(d, l.groupe).gestes.map((g) => ({ libelle: g, agir: () => agir(g, l) })),
                    ]} />
                  </td>
                </tr>
              ))}
              {visibles.length === 0 && edition?.cle !== 'nouvelle' && (
                <tr><td colSpan={nbColonnes} className="rig-table__vide">
                  {vue === null ? 'Lecture des lignes…' : lignes.length === 0 && !filtre ? 'Aucune ligne dans la table du greffe.' : 'Rien ne correspond à la recherche.'}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>
        <GroupesVides vue={vue} />
        <PiedListe aide="Double-cliquez une ligne pour la modifier.">
          <span><b>{visibles.length}</b> ligne{visibles.length > 1 ? 's' : ''}</span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
