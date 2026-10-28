"""Cut a big floor texture (e.g. 128x128) into a strip of 32x32 tile variants for an st14MultiTile.

Usage: python split_multitile.py big.png out_strip.png

Pieces go row by row from the top left, the order ST14MultiTileSystem expects.
Then give the tile `variants: <pieces>` and add an st14MultiTile prototype with the width and height in tiles.
"""
import sys

from PIL import Image

TILE = 32


def main():
    src, dst = sys.argv[1], sys.argv[2]
    im = Image.open(src).convert("RGBA")
    if im.width % TILE or im.height % TILE:
        sys.exit(f"{src} is {im.width}x{im.height}, both sides must be a multiple of {TILE}")

    cols, rows = im.width // TILE, im.height // TILE
    strip = Image.new("RGBA", (cols * rows * TILE, TILE))
    for row in range(rows):
        for col in range(cols):
            piece = im.crop((col * TILE, row * TILE, (col + 1) * TILE, (row + 1) * TILE))
            strip.paste(piece, ((row * cols + col) * TILE, 0))
    strip.save(dst)
    print(f"{cols * rows} variants, width {cols}, height {rows}")


if __name__ == "__main__":
    main()
