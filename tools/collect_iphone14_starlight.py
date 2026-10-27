"""Collect original product photos from a public model/color-specific catalog."""
import csv
import hashlib
import io
import json
import re
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import requests
from PIL import Image, ImageOps, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'training/iphone14_white_photos_20261020'
PAGE = 'https://www.nicosuma.com/iphone/iphone-14/starlight'

def main():
    DEST.mkdir(parents=True, exist_ok=True)
    for name in ('images', 'labels', 'models', 'review'):
        (DEST / name).mkdir(exist_ok=True)
    response = requests.get(PAGE, timeout=30)
    response.raise_for_status()
    match = re.search(r'<script[^>]*id="__NEXT_DATA__"[^>]*>(.*?)</script>', response.text, re.S)
    if not match:
        raise RuntimeError('Public catalog data is not available')
    collection = json.loads(match.group(1))['props']['pageProps']['collection']
    candidates = [p for p in collection['products']
                  if p['metafield'].get('model') == 'iPhone 14'
                  and p['metafield'].get('color') == 'スターライト'
                  and p['metafield'].get('grade') == 'A']
    seen_products = set()
    products = []
    for product in candidates:
        if product['id'] not in seen_products and product.get('images'):
            seen_products.add(product['id'])
            products.append(product)
    print(f'CONFIRMED_CANDIDATES={len(products)}', flush=True)

    existing = set()
    old = ROOT / 'imports/phone-models-20261020/iphone_14_white/dataset/images'
    for path in old.rglob('*'):
        if path.is_file():
            existing.add(hashlib.sha256(path.read_bytes()).hexdigest())

    def fetch(product):
        # Image 1 is the catalog's actual back view. No invented image URLs.
        url = product['images'][0]['url']
        try:
            r = requests.get(url, timeout=25)
            r.raise_for_status()
            data = r.content
            image = Image.open(io.BytesIO(data))
            image.load()
            if image.width < 400 or image.height < 400:
                raise ValueError('Image too small')
            return product, url, data, image.size, hashlib.sha256(data).hexdigest(), ''
        except Exception as error:
            return product, url, None, None, None, str(error)

    records, failures = [], []
    seen = set(existing)
    # Bounded batches avoid downloading the whole store catalog.
    with ThreadPoolExecutor(max_workers=4) as pool:
        for start in range(0, min(len(products), 220), 20):
            if len(records) >= 150:
                break
            for product, url, data, size, digest, error in pool.map(fetch, products[start:start + 20]):
                if error:
                    failures.append({'url': url, 'error': error})
                    continue
                if digest in seen or len(records) >= 150:
                    continue
                seen.add(digest)
                name = f'iphone14_white_{len(records) + 1:03d}.jpg'
                (DEST / 'images' / name).write_bytes(data)
                records.append({'file': 'images/' + name, 'model': 'iPhone 14', 'color': 'Starlight',
                                'seller_grade': product['metafield']['grade'],
                                'source_title': product['title'],
                                'source_page': 'https://www.nicosuma.com/products/' + product['handle'],
                                'catalog_page': PAGE, 'image_url': url, 'sha256': digest,
                                'width': size[0], 'height': size[1],
                                'annotation_status': 'unlabeled', 'source_product_id': product['id']})
            print(f'DOWNLOADED={len(records)}', flush=True)
    (DEST / 'sources.json').write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding='utf-8')
    (DEST / 'download_failures.json').write_text(json.dumps(failures, ensure_ascii=False, indent=2), encoding='utf-8')
    with (DEST / 'sources.csv').open('w', encoding='utf-8-sig', newline='') as stream:
        writer = csv.DictWriter(stream, fieldnames=list(records[0]))
        writer.writeheader()
        writer.writerows(records)
    for start in range(0, len(records), 30):
        sheet = Image.new('RGB', (1080, 1100), 'white')
        draw = ImageDraw.Draw(sheet)
        for cell, entry in enumerate(records[start:start + 30]):
            with Image.open(DEST / entry['file']) as image:
                thumb = ImageOps.contain(image.convert('RGB'), (174, 190))
            x, y = (cell % 6) * 180, (cell // 6) * 220
            sheet.paste(thumb, (x + (180 - thumb.width) // 2, y))
            draw.text((x + 7, y + 195), Path(entry['file']).stem, fill='black')
        sheet.save(DEST / 'review' / f'contact_{start // 30 + 1:02d}.jpg', quality=92)
    print(f'COMPLETE={len(records)}; UNIQUE_PRODUCTS={len(set(r["source_product_id"] for r in records))}', flush=True)

if __name__ == '__main__':
    main()
