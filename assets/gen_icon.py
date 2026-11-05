"""BALAN アイコン生成: python3 assets/gen_icon.py  →  icon.png / icon.ico / icon_tray_*.png

意匠: Balance Board を真上から見た図。左半分 = ブレーキ(赤の停止ボタン)、右半分 = アクセル(緑の下向きシェブロン =
下へ巡航スクロール)。「右足で進む、左足で止まる」を 1 枚で表す。色は画面の状態色(Styles/BalanTheme.axaml)と同じ。
"""
from pathlib import Path
from PIL import Image, ImageDraw

OUT = Path(__file__).parent
S = 4096          # 作画サイズ(縮小して滑らかにする)
FINAL = 1024
BG, BG2 = (27, 28, 33), (44, 46, 58)
BOARD, BOARD_EDGE = (226, 232, 240), (150, 158, 176)
RED, GREEN = (229, 89, 79), (62, 207, 110)   # #E5594F / #3ECF6E
PAD = (36, 38, 47)


def main():
    u = S / 100.0
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=int(S * 0.22), fill=255)

    # 背景: 角丸タイル + 上から下への緩やかなグラデーション
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        gd.line([(0, y), (S, y)], fill=tuple(int(BG2[i] + (BG[i] - BG2[i]) * t) for i in range(3)) + (255,))
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)

    # ボード(真上から。やや横長の角丸矩形)+ 影
    bx0, by0, bx1, by1 = 10 * u, 22 * u, 90 * u, 78 * u
    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle([bx0, by0 + 2.2 * u, bx1, by1 + 2.2 * u], radius=11 * u, fill=(0, 0, 0, 90))
    img.alpha_composite(shadow)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([bx0, by0, bx1, by1], radius=11 * u, fill=BOARD, outline=BOARD_EDGE, width=int(0.9 * u))

    # 左右のパッド(暗い面)
    pad_y0, pad_y1 = 28 * u, 72 * u
    d.rounded_rectangle([16 * u, pad_y0, 48 * u, pad_y1], radius=6 * u, fill=PAD)
    d.rounded_rectangle([52 * u, pad_y0, 84 * u, pad_y1], radius=6 * u, fill=PAD)

    # 左: ブレーキ = 赤い停止(角丸の四角)
    d.rounded_rectangle([24 * u, 40 * u, 40 * u, 60 * u], radius=3 * u, fill=RED)

    # 右: アクセル = 緑の下向きシェブロン(2 段)
    def chevron(cy):
        cx = 68 * u
        w, h, t = 11 * u, 7 * u, 4.6 * u
        pts = [(cx - w, cy - h), (cx, cy + h - t * 0.2), (cx + w, cy - h),
               (cx + w, cy - h + t), (cx, cy + h + t * 0.8), (cx - w, cy - h + t)]
        d.polygon(pts, fill=GREEN)

    chevron(41 * u)
    chevron(54 * u)

    final = img.resize((FINAL, FINAL), Image.LANCZOS)
    final.save(OUT / "icon.png")
    final.save(OUT / "icon.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("wrote", OUT / "icon.png", OUT / "icon.ico")

    # トレイ用: 接続状態を右下の丸で示す(色は画面の状態色と同じ)。小さく表示されるので丸は大きめにする。
    states = {
        "disconnected": (235, 87, 87),   # #EB5757
        "connecting": (242, 201, 76),    # #F2C94C
        "connected": (62, 207, 110),     # #3ECF6E
    }
    base = final.resize((128, 128), Image.LANCZOS)
    for name, color in states.items():
        tray = base.copy().resize((512, 512), Image.LANCZOS)
        td = ImageDraw.Draw(tray)
        r, cx, cy = 100, 512 - 112, 512 - 112
        td.ellipse([cx - r - 20, cy - r - 20, cx + r + 20, cy + r + 20], fill=BG)   # 縁取り(背景色)
        td.ellipse([cx - r, cy - r, cx + r, cy + r], fill=color + (255,))
        tray = tray.resize((128, 128), Image.LANCZOS)
        tray.save(OUT / f"icon_tray_{name}.png")
        print("wrote", OUT / f"icon_tray_{name}.png")


main()
