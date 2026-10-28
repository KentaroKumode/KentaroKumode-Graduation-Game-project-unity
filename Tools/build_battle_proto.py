"""docs/design/battle-prototype.src.html を、 ピクセルフォント (mihiPixelmoji) と画像 (背景・武器) を埋め込んだ
battle-prototype.html にする。 画像を data URI で埋めるのは、 file:// で開いても getImageData (dot の配列化) が通るようにするため。"""
import base64, io, json, os
root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
design = os.path.join(root, "docs", "design")
src = io.open(os.path.join(design, "battle-prototype.src.html"), encoding="utf-8").read()
ttf = open(os.path.join(root, "Assets", "TextMesh Pro", "Fonts", "mihiPixelmoji_v1.1.ttf"), "rb").read()
face = '<style>@font-face { font-family:"MihiPixelmoji"; src:url(data:font/ttf;base64,%s) format("truetype"); }</style>' % base64.b64encode(ttf).decode()
def uri(path): return "data:image/png;base64," + base64.b64encode(open(path, "rb").read()).decode()
imgs = {"bg": uri(os.path.join(design, "battle-assets", "backdrop.png"))}
wdir = os.path.join(design, "battle-assets", "weapons")
for f in sorted(os.listdir(wdir)):
    if f.endswith(".png"): imgs[f[:-4]] = uri(os.path.join(wdir, f))
out = src.replace("<!-- __FONT__ -->", face).replace("/*__IMGS__*/{}", json.dumps(imgs))
io.open(os.path.join(design, "battle-prototype.html"), "w", encoding="utf-8").write(out)
print("ok", len(out))
