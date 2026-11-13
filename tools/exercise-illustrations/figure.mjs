// Side-view figure renderer. The figure always faces right. Angles are in degrees, 0 = right, 90 = up.
// Limbs are solved with two-bone inverse kinematics so poses are described by where hands and feet are.

export const FLOOR = 430;
export const SIZE = 512;
export const LEN = { torso: 104, neck: 20, uarm: 62, farm: 58, thigh: 98, shin: 94 };
const C = {
  bg: '#d9ecfa', ground: '#bcd9ee', ink: '#1d2b3b', skin: '#f2bb8c', skinFar: '#d99a6e', shirt: '#2f9fe0', shirtFar: '#2682b6',
  shorts: '#203a6c', shortsFar: '#172c52', shoe: '#f6f6f6', shoeFar: '#d7d7dc', hair: '#5b3a22',
  steel: '#8e98a6', steelDark: '#6a7482', pad: '#4b505c', plate: '#2c2d35', hub: '#aab2be'
};
export const colors = C;

export const dir = (deg) => [Math.cos((deg * Math.PI) / 180), -Math.sin((deg * Math.PI) / 180)];
export const add = (a, b) => [a[0] + b[0], a[1] + b[1]];
export const mul = (v, k) => [v[0] * k, v[1] * k];
export const along = (p, deg, len) => add(p, mul(dir(deg), len));
const f = (n) => Math.round(n * 10) / 10;

/** Two-bone IK. side flips the bend direction. Returns the middle joint (knee/elbow). */
export function ik(a, target, l1, l2, side, warn = '') {
  let dx = target[0] - a[0], dy = target[1] - a[1];
  let d = Math.hypot(dx, dy);
  const max = l1 + l2 - 0.5;
  if (d > max + 1.5 && warn) console.warn(`  [reach] ${warn}: ${f(d)} > ${f(max)}`);
  const dd = Math.min(Math.max(d, Math.abs(l1 - l2) + 1), max);
  const ux = dx / (d || 1), uy = dy / (d || 1);
  const x = (dd * dd + l1 * l1 - l2 * l2) / (2 * dd);
  const h = Math.sqrt(Math.max(l1 * l1 - x * x, 0));
  return [a[0] + ux * x + -uy * h * side, a[1] + uy * x + ux * h * side];
}

const line = (pts, w, color) => `<polyline points="${pts.map((p) => `${f(p[0])},${f(p[1])}`).join(' ')}" fill="none" stroke="${color}" stroke-width="${w}" stroke-linecap="round" stroke-linejoin="round"/>`;
const limb = (pts, w, color) => line(pts, w + 4, C.ink) + line(pts, w, color);
const circle = (p, r, fill, stroke = C.ink, sw = 2) => `<circle cx="${f(p[0])}" cy="${f(p[1])}" r="${r}" fill="${fill}" stroke="${stroke}" stroke-width="${sw}"/>`;

function leg(hip, spec, far) {
  const ankle = spec.ankle;
  const knee = ik(hip, ankle, LEN.thigh, LEN.shin, spec.side ?? -1, 'leg');
  const skin = far ? C.skinFar : C.skin, shorts = far ? C.shortsFar : C.shorts;
  const footDir = spec.foot ?? 0;
  const heel = along(ankle, footDir + 180, 9), toe = along(ankle, footDir, 30);
  const shortsEnd = [hip[0] + (knee[0] - hip[0]) * 0.62, hip[1] + (knee[1] - hip[1]) * 0.62];
  return limb([ankle, knee], 27, skin) + limb([hip, knee], 33, skin) + limb([hip, shortsEnd], 37, shorts) + limb([heel, toe], 17, far ? C.shoeFar : C.shoe);
}

function arm(shoulder, spec, far) {
  const wrist = spec.wrist;
  const elbow = ik(shoulder, wrist, LEN.uarm, LEN.farm, spec.side ?? 1, 'arm');
  const skin = far ? C.skinFar : C.skin;
  return limb([shoulder, elbow, wrist], 21, skin) + circle(wrist, 11, skin, C.ink, 2);
}

/**
 * pose: { hip, torso, shoulderShift?, legs: {near, far?}, arms: {near, far?}, headTilt? }
 * back: svg drawn before the figure, mid: svg drawn between the far and near limbs, front: svg drawn last.
 */
export function figure(pose) {
  const hip = pose.hip;
  let shoulder = along(hip, pose.torso, LEN.torso);
  if (pose.shoulderShift) shoulder = add(shoulder, pose.shoulderShift);
  const headDir = pose.headDir ?? pose.torso + (pose.headTilt ?? 0);
  const neckTop = along(shoulder, headDir, LEN.neck);
  const head = along(shoulder, headDir, LEN.neck + 14);
  const farOffset = pose.farOffset ?? [9, -3];
  const farLeg = pose.legs.far ?? { ...pose.legs.near, ankle: add(pose.legs.near.ankle, farOffset) };
  const farArm = pose.arms.far ?? { ...pose.arms.near, wrist: add(pose.arms.near.wrist, farOffset) };

  const face = dir(headDir + (pose.faceFlip ? 90 : -90));
  const out = [];
  out.push(leg(hip, farLeg, true));
  if (!pose.hideFarArm) out.push(arm(shoulder, farArm, true));
  out.push(limb([hip, shoulder], 60, C.shirt));
  out.push(line([hip, shoulder], 56, C.shirt));
  out.push(limb([hip, along(hip, pose.torso, 26)], 60, C.shorts));
  out.push(leg(hip, pose.legs.near, false));
  out.push(line([shoulder, neckTop], 16, C.skin));
  out.push(circle(head, 27, C.hair));
  out.push(circle([head[0] + face[0] * 5, head[1] + face[1] * 5], 23, C.skin, 'none', 0));
  out.push(circle(shoulder, 15, C.shirt, C.ink, 2));
  out.push(arm(shoulder, pose.arms.near, false));
  return out.join('');
}

export const groundShadow = (cx = 256, w = 150) => `<ellipse cx="${cx}" cy="${FLOOR + 6}" rx="${w}" ry="16" fill="${C.ground}"/>`;

// ---- equipment -------------------------------------------------------------------------------------------------
export const barbellEnd = (c, r = 44) => circle(c, r, C.plate, C.ink, 2.5) + circle(c, r * 0.62, '#3b3d48', 'none', 0) + circle(c, 7, C.hub, C.ink, 1.5);
export const barbellSide = (c, r = 44) => `<ellipse cx="${f(c[0] + 7)}" cy="${f(c[1])}" rx="${r * 0.9}" ry="${r}" fill="#23242b" stroke="${C.ink}" stroke-width="2"/>` + barbellEnd(c, r);
export const dumbbell = (c, r = 15) => `<rect x="${f(c[0] - 30)}" y="${f(c[1] - 3)}" width="60" height="6" fill="${C.steel}"/>` + circle(c, r, C.plate, C.ink, 2) + circle(c, 5, C.hub, C.ink, 1);
export const bar = (a, b, w = 9) => line([a, b], w + 3, C.ink) + line([a, b], w, C.steel);
export const rod = (a, b, w = 9, color = C.steel) => line([a, b], w + 4, C.ink) + line([a, b], w, color);
export const pad = (a, b, w = 22) => limb([a, b], w, C.pad);
export const rect = (x, y, w, h, fill = C.steel, r = 3) => `<rect x="${f(x)}" y="${f(y)}" width="${f(w)}" height="${f(h)}" rx="${r}" fill="${fill}" stroke="${C.ink}" stroke-width="2.5"/>`;
export const floorLine = (x1, x2, y = FLOOR) => `<line x1="${x1}" y1="${y}" x2="${x2}" y2="${y}" stroke="${C.ink}" stroke-width="3" stroke-linecap="round"/>`;
export const cable = (a, b) => line([a, b], 3, '#3a3f49');
export const handle = (c) => circle(c, 6, C.steel, C.ink, 2);

/** Weight-stack tower of a cable machine. x,y = top-left, h = height. */
export const tower = (x, y, h, w = 70) =>
  rect(x, y, w, h, '#a7b0bd', 4) +
  Array.from({ length: Math.floor((h - 30) / 14) }, (_, i) => `<rect x="${x + 10}" y="${y + 18 + i * 14}" width="${w - 20}" height="9" rx="2" fill="${i < 3 ? '#4a505c' : '#6b7380'}"/>`).join('');

export const pauseMark = (x = 440, y = 70) =>
  circle([x, y], 26, '#ffffff', C.ink, 3) + `<rect x="${x - 10}" y="${y - 12}" width="7" height="24" rx="2" fill="${C.ink}"/><rect x="${x + 3}" y="${y - 12}" width="7" height="24" rx="2" fill="${C.ink}"/>`;

/** view: { scale, from: [x, y], to: [x, y] } maps the source point `from` to `to` after scaling around it. */
export function svg(parts, view = null) {
  const body = view
    ? `<g transform="translate(${view.to[0]} ${view.to[1]}) scale(${view.scale}) translate(${-view.from[0]} ${-view.from[1]})">${parts.join('')}</g>`
    : parts.join('');
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${SIZE}" height="${SIZE}" viewBox="0 0 ${SIZE} ${SIZE}"><rect width="${SIZE}" height="${SIZE}" fill="${C.bg}"/>${body}</svg>`;
}
