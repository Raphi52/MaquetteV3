// Ecran de la procedure PROC_ACCUEIL_PARAM (RIG v2 : parametres d'accueil).
// Les reglages sont ceux du formulaire v2 : chkAnimations, chkPleinEcran,
// chkColorier1LigneSur2, chkAutofocusOnClickIconEtpOpe, chkDateSaisieLibreCpp,
// chkInterdireMoletteComboCpp, txtIntervalSlideshowAlertes, txtMaxWidth,
// txtMaxHeight, plus « Colonne triee par defaut » / sens Ascendant-Descendant
// et la liste des imprimantes a cocher.
//
// Porte depuis Components/Ecrans/EcranParametresAccueil.razor (lot 4) : meme
// balisage, memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses
// reglages et ses imprimantes de demonstration l'ont suivi ici.

import { useState } from 'react'

interface Imprimante {
  nom: string
  recommandee: boolean
  cochee: boolean
}

const ImprimantesDeDepart: Imprimante[] = [
  { nom: 'Greffe - Accueil', recommandee: true, cochee: true },
  { nom: 'Greffe - Etage 1', recommandee: false, cochee: true },
  { nom: 'Greffe - Archives', recommandee: false, cochee: false },
  { nom: 'PDF (fichier)', recommandee: false, cochee: true },
]

export function EcranParametresAccueil() {
  const [imprimantes, setImprimantes] = useState<Imprimante[]>(ImprimantesDeDepart)
  const [animations, setAnimations] = useState(true)
  const [pleinEcran, setPleinEcran] = useState(false)
  const [autofocusBouton, setAutofocusBouton] = useState(true)
  const [uneLigneSurDeux, setUneLigneSurDeux] = useState(true)
  const [dateSaisieLibre, setDateSaisieLibre] = useState(false)
  const [moletteInterdite, setMoletteInterdite] = useState(true)
  // Le @bind d'un int : seul le texte saisi s'affiche, aucune autre partie de
  // l'ecran ne lisait ces valeurs.
  const [intervalAlertes, setIntervalAlertes] = useState('8')
  const [largeurMax, setLargeurMax] = useState('1600')
  const [hauteurMax, setHauteurMax] = useState('900')
  const [colonneTri, setColonneTri] = useState('Date de creation')
  const [sensTri, setSensTri] = useState('Descendant')

  function basculerImprimante(n: number) {
    setImprimantes((avant) => avant.map((imp, i) => (i === n ? { ...imp, cochee: !imp.cochee } : imp)))
  }

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>General</div>

        <button className={`rig-bascule${animations ? ' rig-bascule--actif' : ''}`}
                onClick={() => setAnimations(!animations)}>
          <span className="rig-bascule__temoin"></span><span>Animations</span>
        </button>
        <button className={`rig-bascule${pleinEcran ? ' rig-bascule--actif' : ''}`}
                onClick={() => setPleinEcran(!pleinEcran)}>
          <span className="rig-bascule__temoin"></span><span>Ouvrir en plein ecran</span>
        </button>
        <button className={`rig-bascule${autofocusBouton ? ' rig-bascule--actif' : ''}`}
                onClick={() => setAutofocusBouton(!autofocusBouton)}>
          <span className="rig-bascule__temoin"></span><span>Focus automatique au clic sur un bouton</span>
        </button>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Alertes</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Interval de defilement (secondes)</span>
            <input className="rig-saisie-champ__saisie" type="number" min="1" max="120"
                   value={intervalAlertes} onChange={(e) => setIntervalAlertes(e.target.value)} />
          </label>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Ajuster la taille des controles</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Largeur max.</span>
              <input className="rig-saisie-champ__saisie" type="number" min="100" max="4000"
                     value={largeurMax} onChange={(e) => setLargeurMax(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Hauteur max.</span>
              <input className="rig-saisie-champ__saisie" type="number" min="100" max="4000"
                     value={hauteurMax} onChange={(e) => setHauteurMax(e.target.value)} />
            </label>
          </div>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Grilles et tableaux</div>
          <span className="rig-resultats-proc__compte">{imprimantes.filter((i) => i.cochee).length} imprimante(s) cochee(s)</span>
        </div>

        <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
          <button className={`rig-bascule${uneLigneSurDeux ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setUneLigneSurDeux(!uneLigneSurDeux)}>
            <span className="rig-bascule__temoin"></span><span>Colorier une ligne sur deux</span>
          </button>
          <button className={`rig-bascule${dateSaisieLibre ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setDateSaisieLibre(!dateSaisieLibre)}>
            <span className="rig-bascule__temoin"></span><span>Date en saisie libre</span>
          </button>
          <button className={`rig-bascule${moletteInterdite ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setMoletteInterdite(!moletteInterdite)}>
            <span className="rig-bascule__temoin"></span><span>Interdire la molette dans les listes deroulantes</span>
          </button>

          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Colonne de tri des alertes</span>
              <select className="rig-saisie-champ__saisie" value={colonneTri} onChange={(e) => setColonneTri(e.target.value)}>
                <option>Date de creation</option>
                <option>N Demande</option>
                <option>Client</option>
                <option>Etat</option>
              </select>
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Sens du tri</span>
              <select className="rig-saisie-champ__saisie" value={sensTri} onChange={(e) => setSensTri(e.target.value)}>
                <option>Ascendant</option>
                <option>Descendant</option>
              </select>
            </label>
          </div>
        </div>

        <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
          <div className="rig-kicker">Imprimantes autorisees</div>
          {imprimantes.map((imp, n) => (
            <button key={imp.nom} className={`rig-bascule${imp.cochee ? ' rig-bascule--actif' : ''}`}
                    onClick={() => basculerImprimante(n)}>
              <span className="rig-bascule__temoin"></span>
              <span>{imp.nom} {imp.recommandee ? '(recommande)' : ''}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
          <div className="rig-kicker">Apercu</div>
          <div className="rig-table-cadre">
            <table className="rig-table">
              <thead>
                <tr><th>{colonneTri} ({sensTri})</th><th>Client</th><th>Etat</th></tr>
              </thead>
              <tbody>
                {[0, 1, 2, 3].map((n) => (
                  <tr key={n} style={uneLigneSurDeux && n % 2 === 1 ? { background: 'rgba(255,255,255,.04)' } : undefined}>
                    <td className="rig-table__mono">Ligne {n + 1}</td>
                    <td>Client {n + 1}</td>
                    <td><span className="rig-etat rig-etat--ok">Validee</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </section>
    </div>
  )
}
