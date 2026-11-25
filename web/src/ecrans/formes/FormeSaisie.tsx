// FORME « SAISIE » : creer un objet (une instance, une procedure collective, un depot, une creance). Un enchainement
// d'etapes y est legitime -- la fiche RIG v2 cree en plusieurs ecrans --, mais chaque etape porte de VRAIS champs
// (texte, date, liste, case), pas des puces, et on la parcourt librement par la frise. Creer controle les etapes
// obligatoires de la fiche. Sous le formulaire, les saisies du greffe (les lignes) en liste moderne.

import { useState } from 'react'
import { EnteteListe, ListeModerne, PiedListe } from '../../composants/ListeModerne'
import type { DescripteurEcranVue } from '../../plan/types'
import {
  Annonce, DEMONSTRATION, Provenance, Sections, TableLignes, manquantes, partiesDe, refus, saisissable, sectionFaite,
  useAnnonce, useLignes, type Saisies,
} from './commun'

export function FormeSaisie({ d }: { d: DescripteurEcranVue }) {
  const { vue, lignes } = useLignes(d)
  const etapes = partiesDe(d)
  const [courante, poserCourante] = useState(0)
  const [saisies, poserSaisies] = useState<Saisies>({})
  const [annonce, annoncer] = useAnnonce()
  const etape = etapes[courante]

  const creer = () => {
    const manque = manquantes(etapes, saisies)
    if (manque.length > 0) {
      const premiere = etapes.findIndex((e) => e.titre === manque[0])
      if (premiere >= 0) poserCourante(premiere)
      annoncer(`Contrôle de la fiche RIG v2 : « ${manque.join(' », « ')} » ${manque.length > 1 ? 'sont obligatoires' : 'est obligatoire'}. ${DEMONSTRATION}.`)
      return
    }
    annoncer(refus('Créer', ': rien n’est créé dans la base, la saisie reste à l’écran'))
  }

  return (
    <div className="rig-proc__corps rig-eg" data-forme="Saisie">
      <ListeModerne etiquette={d.titre}>
        <EnteteListe titre={d.titre} synthese={<>Nouvelle saisie · étape <b>{courante + 1}</b> sur {etapes.length}</>} />
        <div className="rig-saisie rig-eg-saisie">
          <ol className="rig-frise" aria-label="Étapes de la saisie">
            {etapes.map((e, k) => {
              const faite = saisissable(e) && sectionFaite(e, saisies)
              return (
                <li key={e.titre} className={`rig-frise__pas${faite ? ' rig-frise__pas--complet' : ''}${k === courante ? ' rig-frise__pas--courant' : ''}`}>
                  <button type="button" className="rig-frise__bouton" aria-current={k === courante ? 'step' : undefined}
                          onClick={() => poserCourante(k)}>
                    <span className="rig-frise__pastille">{faite ? '✓' : k + 1}</span>
                    <span className="rig-frise__libelle">{e.titre}</span>
                    {e.obligatoire && saisissable(e) && <span className="rig-saisie-champ__requis">requis</span>}
                  </button>
                </li>
              )
            })}
          </ol>
          <div className="rig-eg-etape">
            {etape && <Sections sections={[etape]} saisies={saisies} changer={(cle, v) => poserSaisies((avant) => ({ ...avant, [cle]: v }))} />}
            <Annonce texte={annonce} />
            <PiedListe>
              <button type="button" className="rig-lm-outil" disabled={courante === 0} onClick={() => poserCourante(courante - 1)}>Précédent</button>
              <button type="button" className="rig-lm-outil" disabled={courante >= etapes.length - 1} onClick={() => poserCourante(courante + 1)}>Suivant</button>
              <span className="rig-eg-espace" />
              <button type="button" className="rig-lm-outil rig-lm-outil--fort" onClick={creer} title={`Dans RIG v2 : « ${d.valider} »`}>Créer</button>
            </PiedListe>
          </div>
        </div>
      </ListeModerne>

      <ListeModerne etiquette={`Saisies du greffe — ${d.titre}`}>
        <EnteteListe titre="Saisies du greffe" synthese={<><b>{lignes.length}</b> {d.sujet}</>} />
        {vue && <Provenance vue={vue} />}
        <TableLignes d={d} vue={vue} lignes={lignes} total={lignes.length} />
      </ListeModerne>
    </div>
  )
}
