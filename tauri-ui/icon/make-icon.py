"""
从仓库根目录的 logo.png 生成圆角方形（squircle）图标源。

规格照抄 RinaDown 的实际产物（windows/runner/resources/app_icon.ico 实测）：
  - 图形占画布 89%，四周各留 5.5% 透明边
  - 圆角半径 = 图形边长的 22.3%
  - 画布 1024x1024，便于 tauri icon 生成全套（含 512@2x / .icns / .ico）

用法：
    python icon/make-icon.py            # 只生成 icon-source.png
    npm run tauri icon icon/icon-source.png   # 再铺开成 icons/*
"""

from pathlib import Path

from PIL import Image, ImageDraw

# RinaDown 实测值，改这两个常数就能换风格
SHAPE_RATIO = 0.89
RADIUS_RATIO = 0.223

# 取景：原图是一整幅场景，直接缩到 16px 只剩一团粉。RinaDown 的图标也是
# 人物特写，所以这里同样收窄到「头部 + 肩」——0.78 是边长占原图短边的比例，
# (0.47, 0.36) 是取景中心。想回到整幅取景就把 CROP_FRAC 改成 1.0。
CROP_FRAC = 0.78
CROP_CENTER = (0.47, 0.36)

CANVAS = 1024
SS = 4  # 圆角超采样倍数，直接按目标尺寸画圆角会有锯齿

ROOT = Path(__file__).resolve().parents[2]  # -> 仓库根
ART = ROOT / "logo.png"
OUT = Path(__file__).resolve().parent / "icon-source.png"

# 应用内小尺寸也跟着换，免得标题栏还是直角
IN_APP = [
    (ROOT / "tauri-ui" / "src" / "assets" / "logo.png", 128),
    (ROOT / "tauri-ui" / "src" / "assets" / "logo-small.png", 32),
]


def square_crop(im: Image.Image) -> Image.Image:
    """按 CROP_FRAC / CROP_CENTER 裁出正方形取景（越界会被夹回图内）。"""
    w, h = im.size
    side = min(w, h)
    size = round(side * CROP_FRAC)
    cx, cy = round(w * CROP_CENTER[0]), round(h * CROP_CENTER[1])
    left = max(0, min(w - size, cx - size // 2))
    top = max(0, min(h - size, cy - size // 2))
    return im.crop((left, top, left + size, top + size))


def rounded(im: Image.Image, size: int) -> Image.Image:
    """按 RinaDown 的比例把图做成圆角方块（含透明留白），返回 size x size。"""
    shape = round(size * SHAPE_RATIO)
    pad = (size - shape) // 2
    radius = round(shape * RADIUS_RATIO)

    mask = Image.new("L", (shape * SS, shape * SS), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        (0, 0, shape * SS - 1, shape * SS - 1), radius=radius * SS, fill=255
    )
    mask = mask.resize((shape, shape), Image.LANCZOS)

    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(im.resize((shape, shape), Image.LANCZOS), (pad, pad), mask)
    return out


def main() -> None:
    art = square_crop(Image.open(ART).convert("RGBA"))

    icon = rounded(art, CANVAS)
    icon.save(OUT)
    print(f"写入 {OUT.relative_to(ROOT)} ({icon.size[0]}x{icon.size[1]})")

    for path, size in IN_APP:
        rounded(art, size).save(path)
        print(f"写入 {path.relative_to(ROOT)} ({size}x{size})")


if __name__ == "__main__":
    main()
