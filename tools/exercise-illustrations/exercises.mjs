import {
  FLOOR, LEN, add, along, dir, ik, figure, groundShadow, svg, colors as C,
  barbellEnd, rod, pad, rect, floorLine, cable, handle, tower, pauseMark
} from './figure.mjs';

// ---- shared helpers --------------------------------------------------------------------------------------------
const ANKLE_Y = 418;
const P = (x, y) => [x, y];
const dbEnd = (c, r = 15) => `<circle cx="${c[0]}" cy="${c[1]}" r="${r}" fill="${C.plate}" stroke="${C.ink}" stroke-width="2"/><circle cx="${c[0]}" cy="${c[1]}" r="5" fill="${C.hub}" stroke="${C.ink}" stroke-width="1"/>`;
const dot = (c, r = 8, fill = C.steel) => `<circle cx="${c[0]}" cy="${c[1]}" r="${r}" fill="${fill}" stroke="${C.ink}" stroke-width="2.5"/>`;
const plate = (c, r = 38) => barbellEnd(c, r);

/** Straight-legged standing pose whose near ankle is at `ankle`. */
const standing = (ankle = [250, ANKLE_Y], extra = {}) => ({
  hip: [ankle[0] + 6, ankle[1] - 189], torso: 90,
  legs: { near: { ankle, foot: 0 } }, ...extra
});
const shoulderOf = (hip, torso, shift = [0, 0]) => add(along(hip, torso, LEN.torso), shift);
/** Where a bar rests on the upper back, and where the hands hold it. */
const backBar = (shoulder, torso) => add(add(shoulder, along([0, 0], torso + 90, 13)), along([0, 0], torso, 5));
const backGrip = (bar, torso) => add(add(bar, along([0, 0], torso - 90, 20)), along([0, 0], torso + 180, 8));
const kneeOf = (hip, ankle, side = -1) => ik(hip, ankle, LEN.thigh, LEN.shin, side);

/** Builds a frame from layers. back = scenery, pose = figure, front = things drawn over the body. */
const frame = ({ back = [], pose, front = [], view = null, ground = true }) => () =>
  svg([...(ground ? [groundShadow()] : []), ...back, figure(pose), ...front], view);

const upper = (v) => v; // readability marker

// ---- equipment --------------------------------------------------------------------------------------------------
const benchFlat = (x1, x2, y = 332) =>
  pad([x1, y], [x2, y], 24) + rect(x1 + 14, y + 10, 14, FLOOR - y - 10, '#8d96a3', 2) + rect(x2 - 28, y + 10, 14, FLOOR - y - 10, '#8d96a3', 2);
const post = (x, top = 110, w = 14) => rect(x, top, w, FLOOR - top, '#9aa4b1', 2);
const block = (x1, x2, top = 412) => rect(x1, top, x2 - x1, FLOOR - top, '#9aa4b2', 4);

// ---- upper body, standing ---------------------------------------------------------------------------------------
const shrug = (lift) => {
  const p = standing();
  const sh = shoulderOf(p.hip, 90, [0, -lift]);
  const wrist = [sh[0] + 4, sh[1] + 112];
  p.shoulderShift = [0, -lift];
  p.arms = { near: { wrist } };
  return frame({ pose: p, front: [dbEnd(wrist)] });
};

const frontRaise = (up, viaCable) => {
  const p = standing();
  const sh = shoulderOf(p.hip, 90);
  const wrist = up ? [sh[0] + 114, sh[1] + 8] : [sh[0] + 24, sh[1] + 110];
  p.arms = { near: { wrist, side: 1 } };
  if (!viaCable) return frame({ pose: p, front: [dbEnd(wrist)] });
  return frame({ pose: p, back: [tower(16, 130, 300, 64), rect(16, 400, 64, 30, '#7e8794'), cable([58, 408], wrist)], front: [handle(wrist)] });
};

const bandPullApart = (apart) => {
  const p = standing();
  const sh = shoulderOf(p.hip, 90);
  const near = apart ? [sh[0] + 84, sh[1] + 6] : [sh[0] + 112, sh[1] + 6];
  const far = apart ? [sh[0] + 52, sh[1] + 2] : [sh[0] + 118, sh[1] + 2];
  p.arms = { near: { wrist: near, side: 1 }, far: { wrist: far, side: 1 } };
  const band = `<line x1="${near[0]}" y1="${near[1]}" x2="${far[0]}" y2="${far[1]}" stroke="#e8572a" stroke-width="7" stroke-linecap="round"/>`;
  return frame({ pose: p, front: [band] });
};

// ---- seated / supported pressing --------------------------------------------------------------------------------
const seatedPress = ({ top, kind }) => {
  const hip = [236, 322], torso = 97;
  const sh = shoulderOf(hip, torso);
  const wrist = top ? [sh[0] + 14, sh[1] - 111] : kind === 'arnold' ? [sh[0] + 46, sh[1] - 40] : [sh[0] + 42, sh[1] - 34];
  const pose = { hip, torso, legs: { near: { ankle: [305, ANKLE_Y], foot: 0 } }, arms: { near: { wrist, side: top ? 1 : -1 } } };
  const back = [
    pad([232, 332], [292, 332], 24), rect(250, 342, 14, FLOOR - 342, '#8d96a3', 2),
    pad([226, 318], [206, 168], 22), post(196, 160, 12)
  ];
  const front = kind === 'arnold' ? [dbEnd(wrist)] : [];
  if (kind === 'barbell') { back.push(post(344, 90)); front.push(plate(wrist, 30)); }
  return frame({ pose, back, front });
};

// ---- calf work --------------------------------------------------------------------------------------------------
const BLOCK_TOP = 412;
const calfAnkle = (up) => (up ? [268, BLOCK_TOP - 11 - 24] : [250, BLOCK_TOP - 11]);

const calfSingleLeg = (up) => {
  const ankle = calfAnkle(up);
  const p = standing(ankle);
  const sh = shoulderOf(p.hip, 90);
  const wrist = [sh[0] + 8, sh[1] + 114];
  p.legs.near.foot = up ? -62 : 0;
  p.legs.far = { ankle: [up ? 212 : 206, ankle[1] - 56], foot: -55, side: -1 };
  p.arms = { near: { wrist, side: 1 }, far: { wrist: [350, sh[1] + 36], side: 1 } };
  return frame({ pose: p, back: [rect(352, 70, 22, FLOOR - 70, '#aeb7c3', 3), block(190, 330)], front: [dbEnd(wrist)] });
};

const calfBarbell = (up, smith) => {
  const ankle = calfAnkle(up);
  const p = standing(ankle);
  const sh = shoulderOf(p.hip, 90);
  const bar = backBar(sh, 90);
  const grip = backGrip(bar, 90);
  p.arms = { near: { wrist: grip, side: -1 } };
  const back = [block(190, 330)];
  const front = [];
  if (smith) {
    back.push(post(352, 60, 12), post(370, 60, 12), rect(346, bar[1] - 14, 36, 28, '#6a7482', 4), rod(bar, [346, bar[1]], 6));
    front.push(dot(bar));
  } else front.push(plate(bar, 36));
  return frame({ pose: p, back, front });
};

const calfSeatedDb = (up) => {
  const ankle = up ? [326, BLOCK_TOP - 5 - 24] : [316, ANKLE_Y - 6];
  const hip = [222, 322], torso = 74;
  const sh = shoulderOf(hip, torso);
  const knee = kneeOf(hip, ankle, -1);
  const wrist = [knee[0] - 4, knee[1] - 22];
  const pose = { hip, torso, legs: { near: { ankle, foot: up ? -62 : 0, side: -1 } }, arms: { near: { wrist, side: -1 } } };
  return frame({ pose, back: [benchFlat(150, 262, 336)], front: [dbEnd([wrist[0] + 2, wrist[1] - 6], 14)] });
};

const legPressCalf = (up) => {
  const hip = [158, 346], torso = 138;
  const legDir = 42;
  const reach = up ? 187 : 170;
  const ankle = along(hip, legDir, reach);
  const pose = {
    hip, torso, headTilt: 2,
    legs: { near: { ankle, foot: up ? 100 : 132, side: -1 } },
    arms: { near: { wrist: [hip[0] - 4, hip[1] + 24], side: 1 } }
  };
  const toe = along(ankle, up ? 100 : 132, 28);
  const platformDist = Math.max(0, (toe[0] - ankle[0]) * Math.cos(legDir * Math.PI / 180) - (toe[1] - ankle[1]) * Math.sin(legDir * Math.PI / 180)) + 10;
  const pc = along(ankle, legDir, platformDist);
  const a = along(pc, 132, 70), b = along(pc, 312, 70);
  const back = [
    rod(along(hip, legDir + 180, 40), along(hip, legDir, 330), 12, '#7d8794'),
    rod([112, 396], [250, 396], 10), rod([112, 396], [112, 430], 10),
    pad(along(hip, torso, 4), along(hip, torso, 104), 20), pad([hip[0] - 12, hip[1] + 18], [hip[0] + 26, hip[1] + 18], 16)
  ];
  const front = [`<polyline points="${a} ${b}" fill="none" stroke="${C.ink}" stroke-width="16" stroke-linecap="round"/><polyline points="${a} ${b}" fill="none" stroke="#6a7482" stroke-width="11" stroke-linecap="round"/>`];
  return frame({ pose, back, front });
};

// ---- upper back / rows ------------------------------------------------------------------------------------------
const yRaise = (up) => {
  const hip = [196, 300], torso = 48;
  const sh = shoulderOf(hip, torso);
  const wrist = up ? along(sh, 40, 118) : [sh[0] + 14, sh[1] + 116];
  const pose = {
    hip, torso, legs: { near: { ankle: [92, 420], foot: -10, side: 1 } },
    arms: { near: { wrist, side: up ? 1 : 1 } }, headTilt: 8
  };
  const n = dir(torso - 90);
  const a = add(add(hip, [n[0] * 36, n[1] * 36]), along([0, 0], torso, -40));
  const b = add(add(sh, [n[0] * 36, n[1] * 36]), along([0, 0], torso, 36));
  const back = [pad(a, b, 24), rod(a, [a[0] - 30, FLOOR], 10), rod(b, [b[0] + 18, FLOOR], 10)];
  return frame({ pose, back, front: [dbEnd(wrist, 12)] });
};

const wideRow = (pulled) => {
  const hip = [150, 388], torso = pulled ? 95 : 66;
  const sh = shoulderOf(hip, torso);
  const wrist = pulled ? [sh[0] + 54, sh[1] + 52] : [sh[0] + 108, sh[1] + 40];
  const ankle = [336, 392];
  const pose = { hip, torso, legs: { near: { ankle, foot: 80, side: 1 } }, arms: { near: { wrist, side: -1 } } };
  const back = [
    tower(448, 180, 250, 64), rect(448, 396, 64, 34, '#7e8794'),
    pad([100, 402], [196, 402], 20), rect(344, 330, 14, 100, '#8d96a3', 3), cable([480, 392], wrist)
  ];
  return frame({ pose, back, front: [handle(wrist), rod(add(wrist, [0, -26]), add(wrist, [0, 26]), 8)] });
};

const tBarRow = (pulled) => {
  const hip = [196, 304], torso = 48;
  const sh = shoulderOf(hip, torso);
  const wrist = pulled ? [sh[0] + 24, sh[1] + 52] : [sh[0] + 16, sh[1] + 116];
  const pose = { hip, torso, legs: { near: { ankle: [96, 420], foot: -10, side: 1 } }, arms: { near: { wrist, side: pulled ? -1 : 1 } }, headTilt: 8 };
  const n = dir(torso - 90);
  const a = add(add(hip, [n[0] * 36, n[1] * 36]), along([0, 0], torso, -40));
  const b = add(add(sh, [n[0] * 36, n[1] * 36]), along([0, 0], torso, 36));
  const pivot = [470, 420];
  const barEnd = add(wrist, [26, 14]);
  const back = [pad(a, b, 24), rod(a, [a[0] - 30, FLOOR], 10), rod(b, [b[0] + 18, FLOOR], 10), rod(pivot, barEnd, 10), plate(add(barEnd, [-4, 10]), 34)];
  return frame({ pose, back, front: [handle(wrist)] });
};

const pendlayRow = (pulled) => {
  const hip = [206, 302], torso = 10;
  const sh = shoulderOf(hip, torso);
  const barC = pulled ? [sh[0] + 4, sh[1] + 54] : [304, FLOOR - 38];
  const pose = { hip, torso, legs: { near: { ankle: [262, ANKLE_Y], foot: 0, side: -1 } }, arms: { near: { wrist: barC, side: pulled ? -1 : 1 } }, headTilt: -6 };
  return frame({ pose, back: [plate(barC, 38)], front: [dot(barC, 7)] });
};

const rackPull = (lockout) => {
  const pose = lockout
    ? { ...standing(), arms: { near: { wrist: [270, 242], side: 1 } } }
    : { hip: [196, 286], torso: 36, legs: { near: { ankle: [250, ANKLE_Y], foot: 0, side: -1 } }, arms: { near: { wrist: [272, 342], side: 1 } }, headTilt: -8 };
  const barC = lockout ? [270, 242] : [272, 342];
  return frame({
    pose, back: [post(350, 160, 14), rect(322, 350, 34, 10, '#6a7482', 2), plate(barC, 38)], front: [dot(barC, 7)]
  });
};

// ---- heavy compound / advanced ---------------------------------------------------------------------------------
const pushPress = (up) => {
  if (!up) {
    const hip = [236, 268], ankle = [250, ANKLE_Y];
    const sh = shoulderOf(hip, 88);
    const bar = [sh[0] + 20, sh[1] - 12];
    const pose = { hip, torso: 88, legs: { near: { ankle, foot: 0, side: -1 } }, arms: { near: { wrist: add(bar, [4, 6]), side: -1 } } };
    return frame({ pose, back: [], front: [plate(bar, 30)], view: { scale: 0.84, from: [256, 430], to: [256, 430] } });
  }
  const p = standing();
  const sh = shoulderOf(p.hip, 90);
  const wrist = [sh[0] + 8, sh[1] - 112];
  p.arms = { near: { wrist, side: 1 } };
  return frame({ pose: p, front: [plate(wrist, 30)], view: { scale: 0.84, from: [256, 430], to: [256, 430] } });
};

const zPress = (up) => {
  const hip = [170, 410], torso = 90;
  const sh = shoulderOf(hip, torso);
  const wrist = up ? [sh[0] + 6, sh[1] - 114] : [sh[0] + 34, sh[1] - 8];
  const pose = { hip, torso, legs: { near: { ankle: [356, 414], foot: 72, side: -1 } }, arms: { near: { wrist, side: up ? 1 : -1 } } };
  return frame({ pose, front: [plate(wrist, 32)], view: { scale: 0.9, from: [256, 430], to: [256, 430] } });
};

const pikePushUp = (down) => {
  const hand = [274, 416];
  const sh = down ? [282, 354] : [276, 306];
  const torso = down ? -60 : -58;
  const hip = add(sh, along([0, 0], torso, -LEN.torso));
  const pose = { hip, torso, legs: { near: { ankle: [166, ANKLE_Y], foot: 0, side: -1 } }, arms: { near: { wrist: hand, side: down ? -1 : -1 } } };
  return frame({ pose, view: { scale: 0.92, from: [256, 430], to: [256, 430] } });
};

const handstandPushUp = (down) => {
  const hand = [262, 416];
  const sh = down ? [262, 376] : [262, 308];
  const hip = [sh[0], sh[1] - LEN.torso];
  const ankle = [sh[0] + 46, hip[1] - 185];
  const pose = { hip, torso: -90, faceFlip: true, headDir: -90, legs: { near: { ankle, foot: 0, side: 1 } }, arms: { near: { wrist: hand, side: 1 } } };
  return frame({ pose, back: [rect(346, 10, 18, FLOOR - 10, '#aeb7c3', 3)], view: { scale: 0.84, from: [256, 430], to: [256, 430] } });
};

const weightedHang = ({ barY = 52, pulled, hipDrop }) => {
  const wrist = [262, barY];
  const sh = pulled ? [250, barY + 46] : [258, barY + 116];
  const hip = [sh[0] - 4, sh[1] + 102];
  const ankle = [hip[0] - 40, hip[1] + 128];
  const pose = { hip, torso: pulled ? 95 : 91, legs: { near: { ankle, foot: -70, side: 1 } }, arms: { near: { wrist, side: pulled ? 1 : 1 } } };
  const weight = [ankle[0] + 18, ankle[1] + 14];
  return { pose, weight, hip };
};

const weightedPullUp = (pulled) => {
  const h = weightedHang({ pulled });
  const bar = rod([196, 52], [380, 52], 10) + rect(372, 52, 14, 330, '#9aa4b1', 2);
  return frame({ pose: h.pose, back: [bar], front: [dbEnd(h.weight, 14)], ground: false, view: { scale: 0.8, from: [256, 52], to: [256, 56] } });
};

const hangingLegRaise = (raised) => {
  const wrist = [262, 52];
  const sh = [258, 168];
  const hip = [sh[0] - 3, sh[1] + 102];
  const ankle = raised ? [hip[0] + 184, hip[1] - 22] : [hip[0] + 6, hip[1] + 186];
  const pose = { hip, torso: raised ? 98 : 91, legs: { near: { ankle, foot: raised ? 70 : -78, side: -1 } }, arms: { near: { wrist, side: 1 } } };
  const bar = rod([196, 52], [380, 52], 10) + rect(372, 52, 14, 330, '#9aa4b1', 2);
  return frame({ pose, back: [bar], ground: false, view: { scale: 0.8, from: [256, 52], to: [256, 56] } });
};

const weightedDip = (down) => {
  const wrist = [262, 252];
  const sh = down ? [256, 208] : [258, 134];
  const torso = down ? 82 : 91;
  const hip = add(sh, along([0, 0], torso, -LEN.torso));
  const ankle = [hip[0] - 40, hip[1] + 118];
  const pose = { hip, torso, legs: { near: { ankle, foot: -70, side: 1 } }, arms: { near: { wrist, side: down ? 1 : 1 } } };
  const bars = rod([196, 252], [340, 252], 10) + rect(200, 252, 12, 178, '#9aa4b1', 2) + rect(328, 252, 12, 178, '#9aa4b1', 2);
  return frame({ pose, back: [bars], front: [dbEnd(add(ankle, [16, 20]), 14)] });
};

const lyingBench = ({ barC, pause }) => {
  const hip = [214, 312], sh = add(hip, along([0, 0], 180, LEN.torso));
  const wrist = barC;
  const pose = { hip, torso: 180, headDir: 172, legs: { near: { ankle: [292, ANKLE_Y], foot: 0, side: -1 } }, arms: { near: { wrist, side: 1 } } };
  const back = [benchFlat(62, 300, 336), post(36, 190, 14), plate(barC, 40)];
  return frame({ pose, back, front: [dot(barC, 7), ...(pause ? [pauseMark()] : [])] });
};
const pausedBench = (lockout) => lyingBench({ barC: lockout ? [102, 176] : [108, 268], pause: !lockout });

const squatLegs = (hip, ankle) => ({ near: { ankle, foot: 0, side: -1 } });

const pauseSquat = (bottom) => {
  if (bottom) {
    const hip = [204, 324], torso = 56;
    const sh = shoulderOf(hip, torso);
    const bar = backBar(sh, torso);
    const pose = { hip, torso, legs: squatLegs(hip, [250, ANKLE_Y]), arms: { near: { wrist: backGrip(bar, torso), side: -1 } } };
    return frame({ pose, front: [plate(bar, 36), pauseMark()] });
  }
  const p = standing();
  const sh = shoulderOf(p.hip, 90);
  const bar = backBar(sh, 90);
  p.arms = { near: { wrist: backGrip(bar, 90), side: -1 } };
  return frame({ pose: p, front: [plate(bar, 36)], view: { scale: 0.92, from: [256, 430], to: [256, 430] } });
};

const zercher = (deep) => {
  const hip = deep ? [204, 326] : [256, 232];
  const torso = deep ? 76 : 90;
  const ankle = deep ? [250, ANKLE_Y] : [250, ANKLE_Y];
  const sh = shoulderOf(hip, torso);
  const wrist = [sh[0] + 36, sh[1] + 2];
  const elbow = ik(sh, wrist, LEN.uarm, LEN.farm, 1);
  const bar = [elbow[0] + 2, elbow[1] - 4];
  const pose = { hip, torso, legs: squatLegs(hip, ankle), arms: { near: { wrist, side: 1 } } };
  return frame({ pose, front: [plate(bar, 30)] });
};

const deficitDeadlift = (lockout) => {
  const back = [rect(150, 408, 170, 22, '#9aa4b2', 4)];
  if (lockout) {
    const p = standing([250, 408 - 11]);
    p.arms = { near: { wrist: [p.hip[0] + 14, p.hip[1] + 18], side: 1 } };
    const b = [p.hip[0] + 14, p.hip[1] + 18];
    return frame({ pose: p, back: [...back, plate(b, 38)], front: [dot(b, 7)] });
  }
  const ankle = [248, 408 - 11], barC = [296, FLOOR - 38];
  const hip = [208, 322], torso = 30;
  const pose = { hip, torso, legs: { near: { ankle, foot: 0, side: -1 } }, arms: { near: { wrist: barC, side: 1 } }, headTilt: -10 };
  return frame({ pose, back: [...back, plate(barC, 38)], front: [dot(barC, 7)] });
};

const goodMorning = (hinged) => {
  const hip = hinged ? [168, 266] : [256, 226], torso = hinged ? 14 : 90;
  const sh = shoulderOf(hip, torso);
  const bar = backBar(sh, torso);
  const pose = { hip, torso, legs: { near: { ankle: [250, ANKLE_Y], foot: 0, side: -1 } }, arms: { near: { wrist: backGrip(bar, torso), side: -1 } }, headTilt: hinged ? -10 : 0 };
  return frame({ pose, front: [plate(bar, 34)], view: { scale: 0.94, from: [256, 430], to: [256, 430] } });
};

const bulgarian = (low) => {
  const hip = low ? [230, 322] : [232, 238], torso = low ? 80 : 88;
  const sh = shoulderOf(hip, torso);
  const bar = backBar(sh, torso);
  const front = [150, 336];
  const pose = {
    hip, torso, legs: { near: { ankle: [304, ANKLE_Y], foot: 0, side: -1 }, far: { ankle: [128, 322], foot: 190, side: -1 } },
    arms: { near: { wrist: backGrip(bar, torso), side: -1 } }, farOffset: [0, 0]
  };
  return frame({ pose, back: [benchFlat(70, 168, 342)], front: [plate(bar, 34)], view: { scale: 0.94, from: [256, 430], to: [256, 430] } });
};

const pistol = (deep) => {
  const ankle = [262, ANKLE_Y];
  const hip = deep ? [194, 374] : [268, 229], torso = deep ? 62 : 90;
  const sh = shoulderOf(hip, torso);
  const wrist = deep ? [sh[0] + 112, sh[1] - 4] : [sh[0] + 108, sh[1] + 6];
  const farAnkle = [hip[0] + 188, deep ? hip[1] + 4 : hip[1] + 10];
  const pose = {
    hip, torso, legs: { near: { ankle, foot: 0, side: -1 }, far: { ankle: farAnkle, foot: 75, side: -1 } },
    arms: { near: { wrist, side: 1 }, far: { wrist: add(wrist, [6, -2]), side: 1 } }
  };
  return frame({ pose });
};

const dragonFlag = (up) => {
  const sh = [120, 316];
  const hip = up ? [sh[0], sh[1] - LEN.torso] : [sh[0] + LEN.torso, 312];
  const torso = up ? -90 : 180;
  const ankle = up ? [hip[0] + 4, hip[1] - 186] : [hip[0] + 188, 314];
  const pose = {
    hip, torso, headDir: 176, legs: { near: { ankle, foot: up ? 175 : 85, side: -1 } },
    arms: { near: { wrist: [50, 322], side: up ? -1 : -1 } }
  };
  return frame({ pose, back: [pad([30, 342], [162, 342], 24), rect(36, 352, 14, 78, '#8d96a3', 2), rect(140, 352, 14, 78, '#8d96a3', 2)], view: { scale: 0.86, from: [140, 430], to: [200, 430] } });
};

const abWheel = (extended) => {
  const ankle = extended ? [150, ANKLE_Y] : [250, ANKLE_Y];
  const hip = extended ? [318, 340] : [238, 236];
  const torso = extended ? -4 : -32;
  const sh = shoulderOf(hip, torso);
  const wheel = extended ? [452, 412] : [352, 412];
  const pose = {
    hip, torso, headDir: torso + 8, legs: { near: { ankle, foot: extended ? -30 : 0, side: -1 } },
    arms: { near: { wrist: wheel, side: extended ? 1 : 1 } }
  };
  const wheelSvg = `<circle cx="${wheel[0]}" cy="${wheel[1]}" r="18" fill="#2c2d35" stroke="${C.ink}" stroke-width="2.5"/><circle cx="${wheel[0]}" cy="${wheel[1]}" r="6" fill="${C.hub}" stroke="${C.ink}" stroke-width="1.5"/>`;
  return frame({ pose, back: [wheelSvg] });
};

// ---- beginner / minimal equipment ----------------------------------------------------------------------------
const bodyweightSquat = (deep) => {
  if (!deep) {
    const p = standing();
    const sh = shoulderOf(p.hip, 90);
    p.arms = { near: { wrist: [sh[0] + 90, sh[1] + 20], side: 1 } };
    return frame({ pose: p });
  }
  const hip = [204, 326], torso = 58;
  const sh = shoulderOf(hip, torso);
  const pose = { hip, torso, legs: squatLegs(hip, [250, ANKLE_Y]), arms: { near: { wrist: [sh[0] + 100, sh[1] - 6], side: 1 } } };
  return frame({ pose });
};

const bodyweightLunge = (low) => {
  if (!low) {
    const p = standing();
    const sh = shoulderOf(p.hip, 90);
    p.arms = { near: { wrist: [p.hip[0] + 24, p.hip[1] + 4], side: -1 } };
    return frame({ pose: p });
  }
  const hip = [226, 304], torso = 88;
  const pose = {
    hip, torso, legs: { near: { ankle: [292, ANKLE_Y], foot: 0, side: -1 }, far: { ankle: [118, 404], foot: -62, side: -1 } },
    arms: { near: { wrist: [hip[0] + 24, hip[1] + 4], side: -1 } }, farOffset: [0, 0]
  };
  return frame({ pose });
};

const invertedRow = (pulled) => {
  const bar = [448, 282];
  const angle = pulled ? 14 : 6;
  const sh = pulled ? [436, 342] : [438, 398];
  const hip = along(sh, angle + 180, LEN.torso);
  const ankle = [hip[0] - 96, ANKLE_Y - 2];
  const pose = {
    hip, torso: angle, faceFlip: true, headTilt: 4, legs: { near: { ankle, foot: 0, side: 1 } },
    arms: { near: { wrist: [bar[0], bar[1]], side: pulled ? -1 : 1 } }
  };
  const back = [rod([410, 282], [498, 282], 10), rect(488, 282, 14, FLOOR - 282, '#9aa4b1', 2), rect(410, 282, 14, FLOOR - 282, '#9aa4b1', 2)];
  return frame({ pose, back });
};

const negativePullUp = (top) => {
  const wrist = [262, 52];
  const sh = top ? [250, 100] : [258, 168];
  const hip = [sh[0] - 4, sh[1] + 102];
  const pose = { hip, torso: top ? 95 : 91, legs: { near: { ankle: [hip[0] - 36, hip[1] + 126], foot: -70, side: 1 } }, arms: { near: { wrist, side: 1 } } };
  const bar = rod([196, 52], [380, 52], 10) + rect(372, 52, 14, 330, '#9aa4b1', 2);
  return frame({ pose, back: [bar], ground: false, view: { scale: 0.8, from: [256, 52], to: [256, 56] } });
};

const bandRow = (pulled) => {
  const hip = [150, 388], torso = pulled ? 95 : 70;
  const sh = shoulderOf(hip, torso);
  const wrist = pulled ? [sh[0] + 48, sh[1] + 52] : [sh[0] + 108, sh[1] + 36];
  const ankle = [336, 392];
  const pose = { hip, torso, legs: { near: { ankle, foot: 80, side: 1 } }, arms: { near: { wrist, side: -1 } } };
  const band = `<line x1="${ankle[0] + 8}" y1="${ankle[1] - 8}" x2="${wrist[0]}" y2="${wrist[1]}" stroke="#e8572a" stroke-width="6" stroke-linecap="round"/>`;
  return frame({ pose, back: [pad([100, 402], [196, 402], 20)], front: [band, handle(wrist)] });
};

const bandPulldown = (down) => {
  const p = standing();
  const sh = shoulderOf(p.hip, 90);
  const wrist = down ? [sh[0] + 30, sh[1] + 52] : [sh[0] + 16, sh[1] - 112];
  p.arms = { near: { wrist, side: down ? -1 : 1 } };
  const anchor = [338, 24];
  const band = `<line x1="${anchor[0]}" y1="${anchor[1]}" x2="${wrist[0]}" y2="${wrist[1]}" stroke="#e8572a" stroke-width="6" stroke-linecap="round"/>`;
  return frame({ pose: p, back: [rect(330, 10, 16, 40, '#9aa4b1', 3)], front: [band, handle(wrist)], view: { scale: 0.86, from: [256, 430], to: [256, 430] } });
};

const bandPress = (up) => {
  const p = standing();
  const sh = shoulderOf(p.hip, 90);
  const wrist = up ? [sh[0] + 8, sh[1] - 112] : [sh[0] + 26, sh[1] + 2];
  p.arms = { near: { wrist, side: up ? 1 : -1 } };
  const band = `<line x1="252" y1="414" x2="${wrist[0]}" y2="${wrist[1]}" stroke="#e8572a" stroke-width="6" stroke-linecap="round"/>`;
  return frame({ pose: p, front: [band, handle(wrist)], view: { scale: 0.86, from: [256, 430], to: [256, 430] } });
};

const singleLegBridge = (up) => {
  const hip = up ? [222, 358] : [222, 398], torso = up ? 205 : 180;
  const far = up ? along(hip, 18, 188) : along(hip, 34, 188);
  const pose = {
    hip, torso, headDir: 182, legs: { near: { ankle: [292, ANKLE_Y], foot: 0, side: -1 }, far: { ankle: far, foot: 70, side: -1 } },
    arms: { near: { wrist: [hip[0] - 70, 414], side: 1 } }, farOffset: [0, 0]
  };
  return frame({ pose });
};

const elevatedPike = (down) => {
  const hand = [322, 354];
  const sh = down ? [324, 300] : [318, 248];
  const torso = down ? -20 : -6;
  const hip = add(sh, along([0, 0], torso, -LEN.torso));
  const pose = { hip, torso, legs: { near: { ankle: [160, ANKLE_Y], foot: 0, side: -1 } }, arms: { near: { wrist: hand, side: -1 } } };
  return frame({ pose, back: [rect(286, 366, 80, FLOOR - 366, '#9aa4b2', 4)], view: { scale: 0.94, from: [256, 430], to: [256, 430] } });
};

export const exercises = [
  { id: 'dumbbell-shrug', frames: [shrug(0), shrug(18)] },
  { id: 'band-pull-apart', frames: [bandPullApart(false), bandPullApart(true)] },
  { id: 'incline-bench-y-raise', frames: [yRaise(false), yRaise(true)] },
  { id: 'wide-grip-seated-cable-row', frames: [wideRow(false), wideRow(true)] },
  { id: 'chest-supported-t-bar-row', frames: [tBarRow(false), tBarRow(true)] },
  { id: 'pendlay-row', frames: [pendlayRow(false), pendlayRow(true)] },
  { id: 'rack-pull', frames: [rackPull(false), rackPull(true)] },
  { id: 'leg-press-calf-raise', frames: [legPressCalf(false), legPressCalf(true)] },
  { id: 'dumbbell-single-leg-calf-raise', frames: [calfSingleLeg(false), calfSingleLeg(true)] },
  { id: 'seated-dumbbell-calf-raise', frames: [calfSeatedDb(false), calfSeatedDb(true)] },
  { id: 'smith-machine-calf-raise', frames: [calfBarbell(false, true), calfBarbell(true, true)] },
  { id: 'barbell-standing-calf-raise', frames: [calfBarbell(false, false), calfBarbell(true, false)] },
  { id: 'dumbbell-front-raise', frames: [frontRaise(false), frontRaise(true)] },
  { id: 'cable-front-raise', frames: [frontRaise(false, true), frontRaise(true, true)] },
  { id: 'arnold-press', frames: [seatedPress({ top: false, kind: 'arnold' }), seatedPress({ top: true, kind: 'arnold' })] },
  { id: 'seated-barbell-overhead-press', frames: [seatedPress({ top: false, kind: 'barbell' }), seatedPress({ top: true, kind: 'barbell' })] },
  { id: 'pike-push-up', frames: [pikePushUp(false), pikePushUp(true)] },
  { id: 'push-press', frames: [pushPress(false), pushPress(true)] },
  { id: 'z-press', frames: [zPress(false), zPress(true)] },
  { id: 'handstand-push-up', frames: [handstandPushUp(false), handstandPushUp(true)] },
  { id: 'weighted-pull-up', frames: [weightedPullUp(false), weightedPullUp(true)] },
  { id: 'weighted-dip', frames: [weightedDip(false), weightedDip(true)] },
  { id: 'paused-bench-press', frames: [pausedBench(false), pausedBench(true)] },
  { id: 'pause-squat', frames: [pauseSquat(true), pauseSquat(false)] },
  { id: 'zercher-squat', frames: [zercher(false), zercher(true)] },
  { id: 'deficit-deadlift', frames: [deficitDeadlift(false), deficitDeadlift(true)] },
  { id: 'good-morning', frames: [goodMorning(false), goodMorning(true)] },
  { id: 'barbell-bulgarian-split-squat', frames: [bulgarian(false), bulgarian(true)] },
  { id: 'pistol-squat', frames: [pistol(false), pistol(true)] },
  { id: 'hanging-leg-raise', frames: [hangingLegRaise(false), hangingLegRaise(true)] },
  { id: 'dragon-flag', frames: [dragonFlag(false), dragonFlag(true)] },
  { id: 'standing-ab-wheel-rollout', frames: [abWheel(false), abWheel(true)] },
  { id: 'bodyweight-squat', frames: [bodyweightSquat(false), bodyweightSquat(true)] },
  { id: 'bodyweight-reverse-lunge', frames: [bodyweightLunge(false), bodyweightLunge(true)] },
  { id: 'bent-knee-inverted-row', frames: [invertedRow(false), invertedRow(true)] },
  { id: 'negative-pull-up', frames: [negativePullUp(true), negativePullUp(false)] },
  { id: 'band-row', frames: [bandRow(false), bandRow(true)] },
  { id: 'band-lat-pulldown', frames: [bandPulldown(false), bandPulldown(true)] },
  { id: 'band-shoulder-press', frames: [bandPress(false), bandPress(true)] },
  { id: 'elevated-pike-push-up', frames: [elevatedPike(false), elevatedPike(true)] },
  { id: 'single-leg-glute-bridge', frames: [singleLegBridge(false), singleLegBridge(true)] }
];
