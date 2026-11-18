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

// Le theme est pose sur <html> et pas seulement sur le composant : sans cela
// le fond de la page hote reste sombre et cerne le mode clair.
export function appliquerTheme(nom) {
  document.documentElement.setAttribute("data-rig-theme", nom);
}

export function activerPose(zone, dotnet) {
  arreterPose();
  if (!zone) return;

  let saisie = null;
  let taille = null;

  const pasColonne = () => {
    const rect = zone.getBoundingClientRect();
    return (rect.width - ECART * (COLONNES - 1)) / COLONNES + ECART;
  };

  // Le z-index de chaque tuile est pose EN STYLE INLINE par Blazor
  // (`z-index:@p.Ordre`) : un style inline bat toujours une classe CSS, donc
  // une regle `.rig-widget--saisi { z-index }` reste sans effet. La tuile
  // saisie doit donc recevoir son z-index INLINE, et le rendre a Blazor au
  // relachement (c'est lui qui remet l'ordre definitif de la pose).
  const Z_SAISIE = "60";
  const monterAuDessus = (tuile) => {
    tuile.dataset.zAvant = tuile.style.zIndex || "";
    tuile.style.zIndex = Z_SAISIE;
  };
  const rendreZ = (tuile) => {
    tuile.style.zIndex = tuile.dataset.zAvant || "";
    delete tuile.dataset.zAvant;
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
    monterAuDessus(tuile);
    zone.classList.add("rig-grille--pose");
    prise.setPointerCapture?.(ev.pointerId);
    ev.preventDefault();
    ev.stopPropagation();
  };

  const surAppui = (ev) => {
    if (ev.button !== 0) return;
    if (ev.target.closest(".rig-widget__taille")) return;
    const tete = ev.target.closest(".rig-widget__tete");
    // TOUT bouton de l'entete doit recevoir son clic : demarrer un
    // deplacement ici appellerait preventDefault et le clic n'arriverait
    // jamais jusqu'a Blazor. La garde porte sur "button" et non sur une
    // classe precise, sinon chaque nouveau bouton d'entete (reglages,
    // « Voir toutes les affaires »...) redevient muet en silence.
    if (!tete || ev.target.closest("button")) return;
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
    monterAuDessus(tuile);
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
      rendreZ(tuile);
      zone.classList.remove("rig-grille--pose");
      taille = null;
      dotnet.invokeMethodAsync("DimensionnerTuile", id, larg, haut);
      return;
    }
    if (!saisie) return;
    const { tuile, id, col, ligne } = saisie;
    tuile.classList.remove("rig-widget--saisi");
    rendreZ(tuile);
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

// ============================================================
// SAISIE GUIDEE - les etapes a l'horizontale, une page par etape.
// Chacune se cale comme une page (scroll-snap, cote CSS). Le
// navigateur est le seul a savoir laquelle est reellement visible :
// c'est lui qui le dit a Blazor, pour que la frise suive.
// ============================================================

let detacherPagination = null;

export function suivrePagination(zone, dotnet) {
  arreterPagination();
  if (!zone) return;

  // Les etapes sont COTE A COTE : chaque page fait la largeur de la zone et
  // se cale d'elle-meme (scroll-snap). L'etape visible se deduit donc de la
  // position horizontale, arrondie a la page la plus proche.
  let dernier = -1;
  let enAttente = false;
  const indexVisible = () =>
    Math.round(zone.scrollLeft / Math.max(1, zone.clientWidth));

  const mesurer = () => {
    enAttente = false;
    const index = indexVisible();
    if (index === dernier) return;
    dernier = index;
    dotnet.invokeMethodAsync("EtapeVisible", index);
  };
  const auDefilement = () => {
    if (enAttente) return;
    enAttente = true;
    requestAnimationFrame(mesurer);
  };

  // La molette verticale : elle fait d'abord defiler l'etape elle-meme
  // (formulaire plus haut que la vue), et seulement arrivee au bout elle
  // TOURNE la page vers l'etape suivante ou precedente. Un geste = une page :
  // un verrou court absorbe l'inertie des pave tactiles.
  let verrouJusqu = 0;
  const aLaMolette = (ev) => {
    // Ctrl+molette = ZOOM de la fenetre, prioritaire : on ne touche a rien,
    // sinon le preventDefault plus bas annule le zoom natif (defaut du 15/09).
    if (ev.ctrlKey) return;
    if (Math.abs(ev.deltaX) > Math.abs(ev.deltaY)) return; // geste deja horizontal
    const page = ev.target.closest && ev.target.closest("[data-etape]");
    const sens = ev.deltaY > 0 ? 1 : -1;
    if (page && page.scrollHeight > page.clientHeight + 1) {
      const auBout = sens > 0
        ? page.scrollTop + page.clientHeight >= page.scrollHeight - 1
        : page.scrollTop <= 0;
      if (!auBout) return;
    }
    ev.preventDefault();
    const maintenant = performance.now();
    if (maintenant < verrouJusqu) return;
    verrouJusqu = maintenant + 450;
    const total = zone.querySelectorAll("[data-etape]").length;
    const cible = Math.min(total - 1, Math.max(0, indexVisible() + sens));
    allerEtape(zone, cible, true);
  };

  zone.addEventListener("scroll", auDefilement, { passive: true });
  zone.addEventListener("wheel", aLaMolette, { passive: false });
  detacherPagination = () => {
    zone.removeEventListener("scroll", auDefilement);
    zone.removeEventListener("wheel", aLaMolette);
  };
  mesurer();
}

export function arreterPagination() {
  if (detacherPagination) { detacherPagination(); detacherPagination = null; }
}

export function allerEtape(zone, index, doux = true) {
  if (!zone) return;
  zone.scrollTo({ left: index * zone.clientWidth, behavior: doux ? "smooth" : "auto" });
}


// ============ CLAVIER ============
// RIG v2 se conduit au clavier. Premier pas ici : Echap ferme ce qui est
// ouvert. L'ecoute est posee sur le DOCUMENT (et non sur un element focusable)
// parce que le focus peut etre n'importe ou -- dans une grille, un champ, ou
// nulle part apres un clic sur le fond.
let detacherClavier = null;

export function activerClavier(dotnet) {
  arreterClavier();
  const surTouche = (e) => {
    if (e.key !== "Escape") return;
    // Un champ en cours d'edition garde Echap pour lui (annuler la saisie)
    // seulement s'il est dans une liste deroulante ouverte ; sinon, fermer
    // l'ecran est le geste attendu.
    dotnet.invokeMethodAsync("ToucheEchap");
  };
  document.addEventListener("keydown", surTouche);
  detacherClavier = () => document.removeEventListener("keydown", surTouche);
}

export function arreterClavier() {
  if (detacherClavier) { detacherClavier(); detacherClavier = null; }
}


// ============ BARRE D ONGLETS ============
// Le glisser d un onglet doit continuer a etre suivi QUAND LE POINTEUR SORT
// de la fenetre : c est justement le geste qui sort l onglet sur un autre
// ecran. Sans capture du pointeur, le navigateur cesse d envoyer les
// mouvements des qu on quitte l element, et l arrachage serait impossible.
document.addEventListener("pointerdown", (e) => {
  const onglet = e.target instanceof Element ? e.target.closest("[data-rig-onglet]") : null;
  if (!onglet) return;
  // La croix n est PAS un debut de glisser : capturer le pointeur sur
  // l onglet redirigerait le clic vers l onglet, et la croix ne fermerait rien.
  if (e.target.closest(".rig-vonglet__fermer")) return;
  try { onglet.setPointerCapture(e.pointerId); } catch { /* pointeur deja relache */ }
  const r = onglet.getBoundingClientRect();
  glisseVisuel = { code: onglet.dataset.rigOnglet, prise: e.clientX - r.left, departX: e.clientX, departY: e.clientY, actif: false };
}, true);

// RETOUR VISUEL du glisser, comme un navigateur : l onglet SUIT le pointeur.
// Fait ici, en JS, a chaque mouvement : un aller-retour Blazor par pixel
// donnerait un onglet saccade. Le calcul repart de la place NATURELLE de
// l onglet a chaque mouvement, donc il reste juste meme quand Blazor le
// deplace dans la barre (reordonnancement).
let glisseVisuel = null;
function ongletGlisse() {
  return glisseVisuel
    ? document.querySelector(`[data-rig-onglet="${CSS.escape(glisseVisuel.code)}"]`)
    : null;
}
document.addEventListener("pointermove", (e) => {
  if (!glisseVisuel) return;
  if (!glisseVisuel.actif
      && Math.abs(e.clientX - glisseVisuel.departX) + Math.abs(e.clientY - glisseVisuel.departY) < 6) return;
  glisseVisuel.actif = true;
  const onglet = ongletGlisse();
  if (!onglet) return;
  onglet.style.transform = "";
  const naturel = onglet.getBoundingClientRect().left;
  onglet.style.transform = `translateX(${e.clientX - glisseVisuel.prise - naturel}px)`;
  onglet.classList.add("rig-vonglet--suit");
}, true);
function finVisuel() {
  const onglet = ongletGlisse();
  if (onglet) { onglet.style.transform = ""; onglet.classList.remove("rig-vonglet--suit"); }
  glisseVisuel = null;
}
document.addEventListener("pointerup", finVisuel, true);
document.addEventListener("pointercancel", finVisuel, true);
/** Appele par le C# quand l onglet vient de partir dans sa fenetre. */
export function finirGlisseOnglet() { finVisuel(); }

// Mesure, au debut du geste : la taille de la zone visible (pour savoir quand
// le pointeur en sort) et la place de chaque onglet (pour savoir ou l inserer).
// Le calcul, lui, reste en C# : c est la qu il est verifiable.
export function mesurerOnglets() {
  const cases = [];
  document.querySelectorAll("[data-rig-onglet]").forEach((el) => {
    const r = el.getBoundingClientRect();
    cases.push([r.left, r.right]);
  });
  return {
    // 3e valeur : le bas de la barre d onglets -- au-dela, l onglet s arrache.
    page: [document.documentElement.clientWidth, document.documentElement.clientHeight,
           document.getElementById("rig-vonglets")?.getBoundingClientRect().bottom ?? 1e9],
    cases: cases,
  };
}
