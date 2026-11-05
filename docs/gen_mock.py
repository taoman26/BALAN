"""BALAN デスクトップ画面モック (Phase 1)。Avalonia へ移しやすいよう、矩形・テキスト・スライダーのみで構成する。

    python3 docs/gen_mock.py            # docs/mock_main.png (状態: Medium)
    python3 docs/gen_mock.py --states   # docs/mock_states.png (Stopped / Slow / Medium / Fast / Disconnected)
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).parent
F = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FB = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
FM = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"
def font(p, s): return ImageFont.truetype(p, int(s * 2))

S = 2  # supersampling
W, H = 900, 690

# 配色 (Styles/BalanTheme.axaml と共通)
BG, TITLE, PANEL, FIELD, BORDER = "#1b1c21", "#141519", "#24262d", "#17181d", "#3a3d4a"
TXT, DIM, ACC = "#e6e8ef", "#8b90a3", "#4f8cff"
GREEN, YELLOW, RED, ORANGE = "#3ecf6e", "#f2c94c", "#eb5757", "#f2994a"
BRAKE, GO = "#e5594f", "#3ecf6e"

LEVELS = ["Stopped", "Slow", "Medium", "Fast"]
LEVEL_COLOR = {"Stopped": DIM, "Slow": "#56b6c2", "Medium": YELLOW, "Fast": ORANGE}


def lerp_hex(a, b, t):
    a = [int(a[i:i+2], 16) for i in (1, 3, 5)]; b = [int(b[i:i+2], 16) for i in (1, 3, 5)]
    return "#%02x%02x%02x" % tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def render(connected=True, weights=None, dead=2.0, full=5.0, brake_kg=5.0, maxspeed=15, brake=0.6, name="mock_main.png"):
    img = Image.new("RGB", (W * S, H * S), BG)
    d = ImageDraw.Draw(img)

    def rr(x0, y0, x1, y1, fill, outline=None, r=6):
        d.rounded_rectangle([x0*S, y0*S, x1*S, y1*S], r*S, fill=fill, outline=outline, width=S)
    def rect(x0, y0, x1, y1, fill):
        d.rectangle([x0*S, y0*S, x1*S, y1*S], fill=fill)
    def t(x, y, s, f, fill=TXT, a="la"):
        d.text((x*S, y*S), s, font=f, fill=fill, anchor=a)
    def dot(cx, cy, r, fill, outline=None):
        d.ellipse([(cx-r)*S, (cy-r)*S, (cx+r)*S, (cy+r)*S], fill=fill, outline=outline, width=S)
    def panel(x0, y0, x1, y1, title):
        rr(x0, y0, x1, y1, PANEL, BORDER)
        t(x0+16, y0+12, title, font(FB, 12), ACC)

    # ---- ウィンドウ枠 / タイトルバー ----
    rect(0, 0, W, 36, TITLE)
    t(14, 18, "◍", font(FB, 15), ACC, "lm")
    t(36, 18, "BALAN", font(FB, 12), TXT, "lm")
    t(W-124, 18, "—", font(F, 14), DIM, "mm")       # 最小化(トレイへ)
    t(W-84, 18, "▢", font(F, 13), DIM, "mm")
    t(W-44, 18, "✕", font(F, 13), DIM, "mm")
    d.line([0, 36*S, W*S, 36*S], fill=BORDER, width=S)

    # ---- ヘッダー ----
    t(24, 50, "BALAN", font(FB, 22), TXT)
    t(26, 80, "Balance Board Assisted Learning And Navigation", font(F, 10), DIM)
    cx = GREEN if connected else RED
    label = "Connected" if connected else "Disconnected"
    # 最小化(トレイへ) / 接続状態 / 電池
    rr(W-24-136, 52, W-24, 84, "#353847", BORDER, 6)
    t(W-24-68, 68, "— Minimize to tray", font(FB, 10), TXT, "mm")
    px = W-24-136-12-150
    rr(px, 52, px+150, 84, FIELD, BORDER, 16)
    dot(px+20, 68, 6, cx)
    t(px+36, 68, label, font(FB, 12), cx, "lm")
    if connected:
        t(px-16, 68, "Battery 82%", font(F, 10), DIM, "rm")

    # ---- Load (4 センサー) ----
    px0, py0, px1, py1 = 24, 100, 440, 372
    panel(px0, py0, px1, py1, "LOAD   (kg per sensor)")
    weights = weights or {"TL": 0, "TR": 0, "BL": 0, "BR": 0}
    total = sum(weights.values())
    bx, by, bw, bh, gap = 40, py0+38, 128, 50, 8
    pos = {"TL": (bx, by), "TR": (bx+bw+gap, by), "BL": (bx, by+bh+gap), "BR": (bx+bw+gap, by+bh+gap)}
    maxw = 6.25
    for k, (x, y) in pos.items():
        w = weights[k]
        rr(x, y, x+bw, y+bh, FIELD, BORDER, 6)
        fillh = int((bh-4) * min(w/maxw, 1))
        if fillh > 0:
            c = lerp_hex("#2d4f8f", ACC, min(w/maxw, 1))
            rr(x+2, y+bh-2-fillh, x+bw-2, y+bh-2, c, None, 4)
        t(x+10, y+8, k, font(FB, 11), TXT if connected else DIM)
        t(x+bw-10, y+bh-10, f"{w:4.1f} kg" if connected else "-- kg", font(FM, 13), TXT, "rd")
    # 右側: 合計
    t(px1-20, py0+56, "Total", font(F, 9), DIM, "ra")
    t(px1-20, py0+72, f"{total:.1f}" if connected else "--", font(FB, 22), TXT, "ra")
    t(px1-20, py0+104, "kg", font(F, 9), DIM, "ra")

    # ---- ペダル: 何 kg で効くか ----
    up, down, brk = weights["TR"], weights["BR"], max(weights["TL"], weights["BL"])
    t(44, py1-86, "PEDALS", font(FB, 10), DIM)
    meter_max = max(full, brake_kg) * 1.25
    def meter(y, label, val, thr, col, color_label):
        t(44, y, label, font(FB, 9), color_label, "lm")
        mx0, mx1 = 140, 360
        rr(mx0, y-7, mx1, y+7, FIELD, BORDER, 7)
        if connected and val > 0.02:
            wv = max((mx1-mx0)*min(val/meter_max, 1), 6)
            rr(mx0+1, y-6, mx0+wv, y+6, col if val >= thr else "#5a5e70", None, 6)
        x = mx0 + (mx1-mx0)*thr/meter_max
        d.line([x*S, (y-10)*S, x*S, (y+10)*S], fill=TXT, width=2*S)
        t(416, y, f"{val:.1f} kg" if connected else "-- kg", font(FM, 11), TXT, "rm")
    meter(py1-62, "TR  ▲ UP", up, dead, GO, GO)
    meter(py1-40, "BR  ▼ DOWN", down, dead, GO, GO)
    meter(py1-18, "TL/BL  BRAKE", brk, brake_kg, BRAKE, BRAKE)

    # ---- Cruise (巡航速度) ----
    cx0, cy0, cx1, cy1 = 456, 100, 876, 372
    panel(cx0, cy0, cx1, cy1, "CRUISE")
    force = max(up, down)
    braking = connected and brk >= brake_kg
    if not connected or braking or force <= dead:
        level, speed = "Stopped", 0.0
    else:
        u = min((force-dead)/(full-dead), 1.0)
        speed = u*u*maxspeed
        eff = dead + u*(full-dead)
        level = "Slow" if eff < dead+1 else "Medium" if eff < dead+2 else "Fast"
    col = LEVEL_COLOR[level]
    t(cx0+24, cy0+40, level, font(FB, 34), col)
    direction = "up" if up > down else "down"
    t(cx0+24, cy0+88, f"Scrolling {direction}" if speed > 0 else ("Braking" if braking else "Idle  -  nothing happens"), font(F, 10), DIM)
    t(cx1-24, cy0+44, f"{speed:4.1f}", font(FB, 28), TXT, "ra")
    t(cx1-24, cy0+82, "notches / s", font(F, 9), DIM, "ra")
    # 4 段階のステップ
    sx0, sx1, sy = cx0+24, cx1-24, cy0+150
    n = len(LEVELS); sw = (sx1-sx0-(n-1)*8)/n
    for i, lv in enumerate(LEVELS):
        x = sx0 + i*(sw+8)
        active = lv == level
        rr(x, sy, x+sw, sy+34, lerp_hex(PANEL, LEVEL_COLOR[lv], 0.30) if active else FIELD,
           LEVEL_COLOR[lv] if active else BORDER, 6)
        t(x+sw/2, sy+17, lv, font(FB if active else F, 11), LEVEL_COLOR[lv] if active else DIM, "mm")
    # 速度バー
    by2 = cy0+214
    rr(sx0, by2, sx1, by2+12, FIELD, BORDER, 6)
    frac = speed/maxspeed if maxspeed else 0
    if frac > 0:
        rr(sx0+1, by2+1, sx0+1+(sx1-sx0-2)*frac, by2+11, col, None, 5)
    t(sx0, by2+20, "0", font(F, 8), DIM)
    t(sx1, by2+20, f"{maxspeed} max", font(F, 8), DIM, "ra")

    # ---- Settings ----
    sx0_, sy0_, sx1_, sy1_ = 24, 388, 876, 638
    panel(sx0_, sy0_, sx1_, sy1_, "SENSITIVITY")
    def slider(y, name, val, vmin, vmax, text, hint):
        t(sx0_+24, y, name, font(F, 11), TXT, "lm")
        tx0, tx1 = sx0_+190, sx0_+560
        rr(tx0, y-3, tx1, y+3, FIELD, BORDER, 3)
        f = (val-vmin)/(vmax-vmin)
        rr(tx0, y-3, tx0+(tx1-tx0)*f, y+3, ACC, None, 3)
        dot(tx0+(tx1-tx0)*f, y, 8, TXT, ACC)
        rr(tx1+20, y-13, tx1+90, y+13, FIELD, BORDER, 4)
        t(tx1+55, y, text, font(FM, 11), TXT, "mm")
        t(tx1+104, y, hint, font(F, 9), DIM, "lm")
    slider(sy0_+54, "Dead Zone", dead, 0.5, 4.0, f"{dead:.1f} kg", "no reaction below this load")
    slider(sy0_+92, "Max Speed", maxspeed, 2, 40, f"{maxspeed}", "notches / s at full load")
    slider(sy0_+130, "Brake Threshold", brake_kg, 1.0, 8.0, f"{brake_kg:.1f} kg", "TL / BL load that brakes")
    slider(sy0_+168, "Brake Strength", brake, 0.0, 1.0, f"{brake:.2f}", "how fast the brake stops")
    d.line([(sx0_+16)*S, (sy0_+194)*S, (sx1_-16)*S, (sy0_+194)*S], fill=BORDER, width=S)
    # Calibrate
    rr(sx0_+24, sy0_+202, sx0_+150, sy0_+234, ACC, None, 6)
    t(sx0_+87, sy0_+218, "Calibrate", font(FB, 12), "white", "mm")
    t(sx0_+166, sy0_+218, "Rest your feet on the board (no pressing), then press.", font(F, 9), DIM, "lm")
    # Scroll output スイッチ
    rr(sx1_-196, sy0_+206, sx1_-156, sy0_+228, GREEN if connected else BORDER, None, 11)
    dot(sx1_-167 if connected else sx1_-185, sy0_+217, 8, "white")
    t(sx1_-146, sy0_+217, "Scroll output", font(F, 11), TXT, "lm")

    # ---- フッター ----
    rect(0, H-34, W, H, TITLE)
    d.line([0, (H-34)*S, W*S, (H-34)*S], fill=BORDER, width=S)
    dot(20, H-17, 4, GREEN if connected else RED)
    t(32, H-17, "Running in system tray  ·  closing this window keeps BALAN active" if connected
      else "Step on the board and press the red SYNC button  ·  waiting for Balance Board", font(F, 9), DIM, "lm")
    t(W-20, H-17, "Settings saved", font(F, 9), DIM, "rm")
    # 最後に 900x650 へ縮小
    img = img.resize((W, H), Image.LANCZOS)
    img.save(OUT / name)
    return img


MAIN = dict(weights={"TL": 0.3, "TR": 0.3, "BL": 0.3, "BR": 3.5})

if __name__ == "__main__":
    if "--states" in sys.argv:
        cases = [
            ("Stopped", dict(weights={"TL": 0.3, "TR": 0.4, "BL": 0.3, "BR": 1.2})),
            ("Slow", dict(weights={"TL": 0.3, "TR": 0.3, "BL": 0.3, "BR": 2.5})),
            ("Medium", MAIN),
            ("Fast", dict(weights={"TL": 0.3, "TR": 0.3, "BL": 0.3, "BR": 4.8})),
            ("Brake", dict(weights={"TL": 0.3, "TR": 0.3, "BL": 5.6, "BR": 4.5})),
            ("Disconnected", dict(connected=False)),
        ]
        sheet = Image.new("RGB", (W*2 + 30, H*3 + 40), "#0c0c0f")
        for i, (n, kw) in enumerate(cases):
            im = render(name=f"_tmp_{n}.png", **kw)
            sheet.paste(im, (10 + (i % 2)*(W+10), 10 + (i // 2)*(H+10)))
            (OUT / f"_tmp_{n}.png").unlink()
        sheet.save(OUT / "mock_states.png")
    else:
        render(**MAIN)
    print("ok")
