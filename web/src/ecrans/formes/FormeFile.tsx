// FORME « FILE » : une file de travail (relances, injonctions, rejets, retours, files d'edition et d'impression,
// surveillances). Les tuiles chiffrees FILTRENT par etat, chaque ligne a sa case, les gestes de la fiche RIG v2 (Relancer,
// Imprimer, Transmettre, Signer...) s'appliquent EN LOT a la selection. Pas d'etapes : une file ne se « franchit » pas.
// Un ecran reuni (descripteur.membres) met plusieurs files dans une liste : chaque geste ne vise que les lignes des
// procedures qui le portent. Socle : composants/ListeModerne.tsx.
// LA RECHERCHE (lot 8 du plan « RIG v3 100 % fonctionnel », 26/11/2026) : celle des autres formes (useRecherche, commun.tsx) --
// quand un lecteur de la file sait chercher, le texte part dans la base, et ses lignes cherchees ne sont pas refiltrees ici.
// Mesure sur le bureau cache : une relance faite sortait des 200 lignes lues (les dossiers encore a relancer passent devant),
// et le champ de recherche, qui ne filtrait que ces 200 lignes, ne la retrouvait pas pour la relire.
// LES SAISIES DU GESTE (lot 8, 26/11/2026) : les Parametres des procedures de la file, quand elles en ont -- le cabinet et la
// decision d'une injonction (etape obligatoire « Cabinet et decision » de PROC_ISURV) -- se saisissent a cote de la liste, comme
// l'audience d'une Audience, et partent avec chaque geste en lot (par leur libelle). Une file sans parametre n'en montre aucun.

import { useState } from 'react'
import { CartesChiffrees, EnteteListe, ListeModerne, PiedListe, type CarteChiffree } from '../../composants/ListeModerne'
import type { DescripteurEcranVue } from '../../plan/types'
import {
  Annonce, ChampSaisie, NoteRecherche, Provenance, TableLignes, enExemple, figee, gestesDe, parcoursDe, refus, synthese, teinteEtat,
  useAnnonce, useGesteEnLot, useLignes, useRecherche, type LigneTenue, type Saisies,
} from './commun'

/** Une file n'a pas de panneau de criteres : seul le champ de recherche cherche (objet stable, la recherche en depend). */
const SANS_CRITERE: Saisies = {}

// Les gestes en lot vont a l'ecrivain de la procedure (type Agir, son nom), un envoi par procedure : ecrits, la file est
// relue et les cases decochees (plan « RIG v3 100 % fonctionnel », lot 2) ; sinon « Demonstration ».
export function FormeFile({ d }: { d: DescripteurEcranVue }) {
  const lues = useLignes(d)
  const { vue, lignes, relire } = lues
  const [texte, poserTexte] = useState('')
  const [etatChoisi, poserEtatChoisi] = useState<string | null>(null)
  const [cochees, poserCochees] = useState<ReadonlySet<number>>(() => new Set())
  const [annonce, annoncer] = useAnnonce()
  const [saisies, poserSaisies] = useState<Saisies>({})

  // Les tuiles comptent le travail REEL du greffe : un groupe encore en exemple (mode mixte) n'y entre pas.
  const comptees = lignes.filter((l) => !enExemple(vue, l))
  const { visibles: trouvees, enBase, filtre } = useRecherche(lues, texte, SANS_CRITERE)
  const visibles = trouvees.filter((l) => etatChoisi === null || l.etat === etatChoisi)
  const cartes: CarteChiffree[] = d.etats.map((e) => {
    const nombre = comptees.filter((l) => l.etat === e.code).length
    return {
      cle: e.code, titre: e.libelle, valeur: nombre, complement: `sur ${comptees.length}`,
      anneau: { part: nombre, total: comptees.length, teinte: teinteEtat(e) },
      actif: etatChoisi === e.code, choisir: () => poserEtatChoisi(etatChoisi === e.code ? null : e.code),
      indication: etatChoisi === e.code ? 'Retirer ce filtre' : `Ne montrer que « ${e.libelle} »`,
    }
  })

  // Les gestes en lot : ceux de chaque procedure de l'ecran, une fois chacun, dans l'ordre des procedures.
  const procedures = d.membres.length > 0 ? d.membres : [d]
  // Les saisies du geste : les parametres des procedures, une fois chacun (deux injonctions partagent le meme cabinet).
  const champs = procedures.flatMap((p) => p.parametres).filter((c, k, tous) => tous.findIndex((x) => x.libelle === c.libelle) === k)
  const gestes = [...new Set(procedures.flatMap(gestesDe))]
  const choisies = lignes.filter((l) => cochees.has(l.i))
  const portee = (geste: string) => choisies.filter((l) => gestesDe(parcoursDe(d, l.groupe)).includes(geste))
  // Pourquoi une action est grisee : rien de coche, ou des lignes cochees d'une procedure qui ne porte pas ce geste (ecran
  // reuni : « Imprimer » ne vaut que pour les procedures qui le nomment).
  const pourquoiGrise = (geste: string) => choisies.length === 0
    ? 'Cochez au moins une ligne'
    : `« ${geste} » ne s’applique pas aux lignes cochées : il ne vaut que pour ${procedures.filter((p) => gestesDe(p).includes(geste)).map((p) => `« ${p.titre} »`).join(', ')}`
  const enLot = useGesteEnLot(d, vue, annoncer)
  const jouer = (geste: string, visees: LigneTenue[]) => void enLot('Agir', geste, visees, saisies,
    (n) => refus(geste, `sur ${n} ligne${n > 1 ? 's' : ''} : aucune n’est modifiée dans la base`),
    () => { poserCochees(new Set()); relire() })
  const basculer = (i: number) => poserCochees((avant) => {
    const apres = new Set(avant)
    if (!apres.delete(i)) apres.add(i)
    return apres
  })

  return (
    <div className="rig-proc__corps rig-eg" data-forme="File">
      {champs.length > 0 && (
        <aside className="rig-criteres" aria-label={`Saisies de l’action — ${d.titre}`}>
          <div className="rig-kicker">Saisies de l’action</div>
          {champs.map((c) => (
            <ChampSaisie key={c.libelle} champ={c} valeur={saisies[c.libelle] ?? ''}
                         changer={(v) => poserSaisies((avant) => ({ ...avant, [c.libelle]: v }))} />
          ))}
          <div className="rig-criteres__actions">
            <button type="button" className="rig-bouton rig-bouton--plat" onClick={() => poserSaisies({})}>Effacer</button>
          </div>
        </aside>
      )}
      <ListeModerne etiquette={d.titre}>
        <EnteteListe titre={d.titre} synthese={synthese(d, vue, lignes)}
                     filtre={{ valeur: texte, changer: poserTexte, indication: d.recherche }} />
        {vue && <Provenance vue={vue} />}
        <NoteRecherche enBase={enBase} />
        <CartesChiffrees cartes={cartes} />

        {/* Les gestes en lot : sur les lignes cochees (une ligne figee ne se coche pas). */}
        <div className="rig-lm-outils rig-eg-lot" role="toolbar" aria-label="Actions sur la sélection">
          <span className="rig-eg-lot__compte"><b>{choisies.length}</b> sélectionnée{choisies.length > 1 ? 's' : ''}</span>
          {gestes.map((g, k) => {
            const visees = portee(g)
            return (
              <button key={g} type="button" className={`rig-lm-outil${k === 0 ? ' rig-lm-outil--fort' : ''}`}
                      disabled={visees.length === 0} onClick={() => jouer(g, visees)}
                      title={visees.length === 0 ? pourquoiGrise(g) : undefined}>{g}</button>
            )
          })}
          {choisies.length > 0 && (
            <button type="button" className="rig-lm-outil" onClick={() => poserCochees(new Set())}>Décocher</button>
          )}
        </div>
        <Annonce texte={annonce} />

        <TableLignes d={d} vue={vue} lignes={visibles} total={lignes.length} filtre={etatChoisi !== null || filtre}
                     cases={{ cochees, basculer, toutes: (cocher) => poserCochees(cocher ? new Set(visibles.filter((l) => !figee(l.etat)).map((l) => l.i)) : new Set()) }}
                     detail={(l) => {
                       const p = parcoursDe(d, l.groupe)
                       return (
                         <div className="rig-eg-regles">
                           {p.sections.map((s) => (
                             <div key={s.titre} className="rig-lm-champ">
                               <span className="rig-lm-champ__libelle">{s.titre}</span>
                               <div className="rig-lm-champ__valeur">{s.consigne}</div>
                             </div>
                           ))}
                         </div>
                       )
                     }}
                     actions={(l) => gestesDe(parcoursDe(d, l.groupe)).map((g) => ({ libelle: g, desactive: figee(l.etat), agir: () => jouer(g, [l]) }))} />
        <PiedListe aide="Cochez des lignes, puis choisissez l’action.">
          <span><b>{visibles.length}</b> affichée{visibles.length > 1 ? 's' : ''}</span>
          <span>Cochées <b>{choisies.length}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
