"""アプリ/トレイアイコン生成。python3 assets/gen_icon.py"""
from pathlib import Path
from PIL import Image, ImageDraw

OUT = Path(__file__).parent
S = 8
N = 64


def base(dot=None):
    img = Image.new("RGBA", (N * S, N * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([4*S, 4*S, 60*S, 60*S], 12*S, fill="#24262d", outline="#4f8cff", width=3*S)
    # ボードを上から見た図: 左=ブレーキ(赤)、右=アクセル(緑)
    d.rounded_rectangle([12*S, 16*S, 31*S, 48*S], 5*S, fill="#e5594f")
    d.rounded_rectangle([33*S, 16*S, 52*S, 48*S], 5*S, fill="#3ecf6e")
    if dot:
        d.ellipse([38*S, 38*S, 62*S, 62*S], fill=dot, outline="#14151a", width=2*S)
    return img.resize((N, N), Image.LANCZOS)


base().save(OUT / "icon.png")
for name, color in [("disconnected", "#eb5757"), ("connecting", "#f2c94c"), ("connected", "#3ecf6e")]:
    base(color).save(OUT / f"icon_tray_{name}.png")
print("ok")
