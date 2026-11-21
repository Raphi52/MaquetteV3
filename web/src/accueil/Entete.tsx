// L'ENTETE DE L'ACCUEIL : « Proue » (/draft conv-1983, 18/11/2026) -- la date
// du jour, le salut, puis UNE phrase qui dit la journee avec les chiffres des
// tuiles ; a droite, « Commandes rondes » : le reglage de l'accueil (le theme,
// le panneau des tuiles et des dispositions) puis le rond de profil.
//
// Porte en React (lot 1 du plan « finir le passage a React ») depuis le
// <div class="rig-entete"> de Home.razor. Les morceaux de texte suivent ceux
// que Razor creait (un noeud par @expression) : voir web/README.md, lot 3.

import { Liste } from './Liste'
import { RondProfil } from './RondProfil'
import { arreter, type EnteteVue, type GesteAccueil, type PhraseVue } from './types'

function Phrase({ phrase }: { phrase: PhraseVue }) {
  const n = phrase.horsDelai
  return (
    <div className="rig-proue__phrase">
      {phrase.horsDelaiConnu && (
        <>
          {n > 0
            ? (
              <>
                <b className="rig-proue__alerte">{n}{' '}{n > 1 ? 'dossiers' : 'dossier'}</b>
                {n > 1 ? ' depassent le delai legal' : ' depasse le delai legal'}
              </>
            )
            : <span>Aucun dossier hors delai legal</span>}
          <span className="rig-proue__point">{'·'}</span>
        </>
      )}
      <b>{phrase.arrivees}{' '}{phrase.arrivees > 1 ? 'arrivees' : 'arrivee'}</b>
      {' a traiter\n'}
      {phrase.traites !== null && (
        <>
          <span className="rig-proue__point">{'·'}</span>
          {' '}
          <b>{phrase.traites}</b>
          {' '}
          <span>traites</span>
        </>
      )}
    </div>
  )
}

export function Entete({ entete, geste }: { entete: EnteteVue, geste: GesteAccueil }) {
  const panneau = entete.panneau
  return (
    <div className="rig-entete">
      <div className="rig-proue">
        {/* Branchee au serveur, le greffe ouvert a l'ouverture de RIG (lot 10), a la suite de la date. */}
        <div className="rig-proue__date">
          {entete.date}
          {entete.greffe && <span className="rig-proue__greffe">{' · '}{entete.greffe}</span>}
        </div>
        {' '}
        <div className="rig-titre">{'\n                    Bonjour '}<b>{entete.utilisateur}</b></div>
        {' '}
        <Phrase phrase={entete.phrase} />
      </div>
      {' '}
      {/* « Commandes rondes » (18/11/2026) : le reglage puis le profil, en ronds. */}
      <div className="rig-entete__outils">
        <button className={'rig-entete__action ' + (entete.panneauOuvert ? 'rig-entete__action--ouvert' : '')}
                title="Reglages de l'accueil" aria-label="Reglages de l'accueil"
                onClick={() => geste('panneauTuiles')} onMouseDown={arreter}>
          <svg viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="2.4" fill="none" stroke="currentColor" strokeWidth="1.4"></circle><path d="M8 1.8v2M8 12.2v2M1.8 8h2M12.2 8h2M3.6 3.6l1.4 1.4M11 11l1.4 1.4M3.6 12.4 5 11M11 5l1.4-1.4" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round"></path></svg>
        </button>
        {entete.panneauOuvert && panneau && (
          <>
            {/* Voile transparent plein ecran : un clic AILLEURS ferme le panneau. */}
            <div className="rig-voile-clic" onClick={() => geste('fermerPanneauTuiles')} onMouseDown={arreter}></div>
            {' '}
            <div className="rig-tuiles-panneau" onMouseDown={arreter}>
              {/* Le theme etait dans un panneau de la barre du haut ; elle ne porte
                  plus que les onglets depuis le 19/11/2026 (« comme Brave »). */}
              <label className="rig-saisie-champ rig-tuiles-panneau__theme">
                <span className="rig-saisie-champ__libelle">Theme</span>
                <Liste className="rig-saisie-champ__saisie" id="rig-choix-theme"
                       valeur={panneau.theme} changer={(code) => geste('theme', code)}>
                  {panneau.themes.map((t) => <option key={t.code} value={t.code}>{t.libelle}</option>)}
                </Liste>
              </label>
              <div className="rig-tuiles-panneau__tete">
                <span className="rig-kicker">Tuiles affichees sur l'accueil</span>
                {' '}
                <button className="rig-tuiles-panneau__reset"
                        title="Reinitialiser la disposition : replace et redimensionne les tuiles selon la disposition d'origine"
                        aria-label="Reinitialiser la disposition"
                        onClick={() => geste('reinitialiser')}>{'↻'}</button>
              </div>
              {' '}
              <span className="rig-kicker">Disposition</span>
              {' '}
              <div className="rig-dispositions">
                {panneau.dispositions.map((d) => (
                  <button key={d.id} className={'rig-disposition ' + (d.actif ? 'rig-disposition--actif' : '')}
                          title={d.aide} onClick={() => geste('disposition', d.id)}>{d.titre}</button>
                ))}
              </div>
              {panneau.tuiles.map((t) => (
                <button key={t.id} className={'rig-bascule ' + (t.actif ? 'rig-bascule--actif' : '')}
                        onClick={() => geste('tuile', t.id)}>
                  <span className="rig-bascule__temoin"></span>
                  {' '}
                  <span>{t.libelle}</span>
                </button>
              ))}
            </div>
          </>
        )}
        {entete.profil && <RondProfil profil={entete.profil} geste={geste} />}
      </div>
    </div>
  )
}
