// ============================================================
// RIG V3 - pose des tuiles de l'accueil.
// Le glisser-deposer est fait ici parce que seul le navigateur
// connait la geometrie reelle de la grille ; la position finale
// (colonne, ligne) est renvoyee a Blazor qui, lui, la garde.
// ============================================================

const COLONNES = 12;
const ECART = 12;      // doit valoir le gap de .rig-grille
const HAUTEUR_LIGNE = 116; // doit valoir grid-auto-rows de .rig-grille
const LARGEUR_MIN = 3;     // en colonnes
const HAUTEUR_MIN = 1;     // en lignes

let detacher = null;

export function activerPose(zone, dotnet) {
  arreterPose();
  if (!zone) return;

  let saisie = null;
  let taille = null;

  const pasColonne = () => {
    const rect = zone.getBoundingClientRect();
    return (rect.width - ECART * (COLONNES - 1)) / COLONNES + ECART;
  };

  // Redimensionnement : poignee du coin bas-droit de la tuile.
  const surAppuiTaille = (ev) => {
    if (ev.button !== 0) return;
    const prise = ev.target.closest(".rig-widget__taille");
    if (!prise) return;
    const tuile = prise.closest("[data-tuile]");
    if (!tuile) return;

    taille = {
      tuile,
      id: tuile.getAttribute("data-tuile"),
      col: parseInt(tuile.getAttribute("data-col"), 10) || 1,
      largDepart: parseInt(tuile.getAttribute("data-larg"), 10) || 1,
      hautDepart: parseInt(tuile.getAttribute("data-haut"), 10) || 1,
      larg: parseInt(tuile.getAttribute("data-larg"), 10) || 1,
      haut: parseInt(tuile.getAttribute("data-haut"), 10) || 1,
      x: ev.clientX,
      y: ev.clientY,
      pas: pasColonne()
    };
    tuile.classList.add("rig-widget--saisi");
    zone.classList.add("rig-grille--pose");
    prise.setPointerCapture?.(ev.pointerId);
    ev.preventDefault();
    ev.stopPropagation();
  };

  const surAppui = (ev) => {
    if (ev.button !== 0) return;
    if (ev.target.closest(".rig-widget__taille")) return;
    const tete = ev.target.closest(".rig-widget__tete");
    // Les commandes de l'entete (reglages) doivent recevoir leur clic :
    // demarrer un deplacement ici appellerait preventDefault et le clic
    // n'arriverait jamais jusqu'a Blazor.
    if (!tete || ev.target.closest(".rig-widget__reglages")) return;
    const tuile = tete.closest("[data-tuile]");
    if (!tuile) return;

    const pas = pasColonne();

    saisie = {
      tuile,
      id: tuile.getAttribute("data-tuile"),
      larg: parseInt(tuile.getAttribute("data-larg"), 10) || 1,
      colDepart: parseInt(tuile.getAttribute("data-col"), 10) || 1,
      ligneDepart: parseInt(tuile.getAttribute("data-ligne"), 10) || 1,
      x: ev.clientX,
      y: ev.clientY,
      pas,
      col: parseInt(tuile.getAttribute("data-col"), 10) || 1,
      ligne: parseInt(tuile.getAttribute("data-ligne"), 10) || 1
    };
    tuile.classList.add("rig-widget--saisi");
    zone.classList.add("rig-grille--pose");
    tete.setPointerCapture?.(ev.pointerId);
    ev.preventDefault();
  };

  const surDeplacement = (ev) => {
    if (taille) {
      const dCol = Math.round((ev.clientX - taille.x) / taille.pas);
      const dLigne = Math.round((ev.clientY - taille.y) / (HAUTEUR_LIGNE + ECART));
      const maxLarg = COLONNES - taille.col + 1;
      const larg = Math.min(Math.max(taille.largDepart + dCol, LARGEUR_MIN), Math.max(maxLarg, LARGEUR_MIN));
      const haut = Math.max(taille.hautDepart + dLigne, HAUTEUR_MIN);
      if (larg === taille.larg && haut === taille.haut) return;
      taille.larg = larg;
      taille.haut = haut;
      taille.tuile.style.gridColumnEnd = "span " + larg;
      taille.tuile.style.gridRowEnd = "span " + haut;
      return;
    }
    if (!saisie) return;
    const dCol = Math.round((ev.clientX - saisie.x) / saisie.pas);
    const dLigne = Math.round((ev.clientY - saisie.y) / (HAUTEUR_LIGNE + ECART));
    const maxCol = COLONNES - saisie.larg + 1;
    const col = Math.min(Math.max(saisie.colDepart + dCol, 1), Math.max(maxCol, 1));
    const ligne = Math.max(saisie.ligneDepart + dLigne, 1);
    if (col === saisie.col && ligne === saisie.ligne) return;
    saisie.col = col;
    saisie.ligne = ligne;
    saisie.tuile.style.gridColumnStart = String(col);
    saisie.tuile.style.gridRowStart = String(ligne);
  };

  const surRelache = () => {
    if (taille) {
      const { tuile, id, larg, haut } = taille;
      tuile.classList.remove("rig-widget--saisi");
      zone.classList.remove("rig-grille--pose");
      taille = null;
      dotnet.invokeMethodAsync("DimensionnerTuile", id, larg, haut);
      return;
    }
    if (!saisie) return;
    const { tuile, id, col, ligne } = saisie;
    tuile.classList.remove("rig-widget--saisi");
    zone.classList.remove("rig-grille--pose");
    saisie = null;
    dotnet.invokeMethodAsync("PoserTuile", id, col, ligne);
  };

  zone.addEventListener("pointerdown", surAppuiTaille);
  zone.addEventListener("pointerdown", surAppui);
  window.addEventListener("pointermove", surDeplacement);
  window.addEventListener("pointerup", surRelache);
  window.addEventListener("pointercancel", surRelache);

  detacher = () => {
    zone.removeEventListener("pointerdown", surAppuiTaille);
    zone.removeEventListener("pointerdown", surAppui);
    window.removeEventListener("pointermove", surDeplacement);
    window.removeEventListener("pointerup", surRelache);
    window.removeEventListener("pointercancel", surRelache);
  };
}

export function arreterPose() {
  if (detacher) { detacher(); detacher = null; }
}
