"""Imports the 'Taslağı indir' JSON and all referenced /media files into SQLite.

Examples:
  py tools/import_state.py src/FiveKor.Web/App_Data/seed.json --init
  py tools/import_state.py 5-kor-taslak.json --source https://bes-kor-hali-saha.yunusiinan.chatgpt.site
  py tools/import_state.py 5-kor-taslak.json --media-dir C:\\FiveKorMedia
"""
import argparse
import json
import pathlib
import re
import shutil
import sqlite3
import tempfile
import urllib.request
from datetime import datetime, timezone

ROOT = pathlib.Path(__file__).resolve().parents[1] / "src" / "FiveKor.Web" / "App_Data"
MEDIA = re.compile(r"^/media/([a-f0-9-]{36})$")


def all_media(value):
    if isinstance(value, dict):
        for item in value.values():
            yield from all_media(item)
    elif isinstance(value, list):
        for item in value:
            yield from all_media(item)
    elif isinstance(value, str):
        match = MEDIA.fullmatch(value)
        if match:
            yield match.group(1)


def mime(data):
    if data.startswith(b"\x89PNG"):
        return "image/png"
    if data.startswith(b"\xff\xd8\xff"):
        return "image/jpeg"
    if data.startswith(b"RIFF") and data[8:12] == b"WEBP":
        return "image/webp"
    if data[4:8] == b"ftyp":
        return "video/mp4"
    if data.startswith(b"\x1a\x45\xdf\xa3"):
        return "video/webm"
    raise ValueError("Desteklenmeyen görsel/video biçimi.")


def main():
    parser = argparse.ArgumentParser(description="5 Kor veri ve medya aktarımı")
    parser.add_argument("json_file", type=pathlib.Path)
    parser.add_argument("--init", action="store_true", help="Başlangıç SQLite dosyasını oluşturur")
    parser.add_argument("--replace", action="store_true", help="Var olan düzenlemeleri bilerek değiştirir")
    parser.add_argument("--source", help="Eski sitenin HTTPS adresi; giriş koruması dosya indirmeyi engelleyebilir")
    parser.add_argument("--media-dir", type=pathlib.Path, help="UUID adlarıyla indirilmiş medya klasörü")
    args = parser.parse_args()
    state = json.loads(args.json_file.read_text(encoding="utf-8-sig"))
    if not isinstance(state, dict) or not all(x in state for x in ("players", "settings", "stars")):
        raise SystemExit("Beklenen site taslağı JSON dosyası değil.")
    ids = sorted(set(all_media(state)))
    media_path = ROOT / "media"
    media_path.mkdir(parents=True, exist_ok=True)
    db_path = ROOT / "fivekor.db"
    with tempfile.TemporaryDirectory() as tmp:
        staged = pathlib.Path(tmp)
        details = []
        for id_ in ids:
            local = args.media_dir / id_ if args.media_dir else media_path / id_
            try:
                if local.exists():
                    data = local.read_bytes()
                elif args.source and args.source.startswith("https://"):
                    with urllib.request.urlopen(args.source.rstrip("/") + "/media/" + id_, timeout=30) as response:
                        data = response.read(50 * 1024 * 1024 + 1)
                else:
                    raise FileNotFoundError(id_)
                content_type = mime(data)
                if len(data) > (8 if content_type.startswith("image/") else 50) * 1024 * 1024:
                    raise ValueError("Dosya boyutu sınırını aşıyor.")
                (staged / id_).write_bytes(data)
                details.append((id_, content_type, len(data), "Aktarılan dosya", datetime.now(timezone.utc).isoformat()))
            except Exception as exc:
                raise SystemExit(f"Medya {id_} aktarılamadı: {exc}. Eski sitede /media/{id_} adresinden indirip --media-dir ver.") from exc
        if db_path.exists() and not args.init:
            with sqlite3.connect(db_path) as existing:
                row = existing.execute("SELECT revision FROM site_state WHERE id='main'").fetchone()
                if row and row[0] > 0 and not args.replace:
                    raise SystemExit("Veritabanında değişiklik var. Önce yedek al, sonra --replace kullan.")
            shutil.copy2(db_path, db_path.with_suffix(".db.bak"))
        with sqlite3.connect(db_path) as db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS site_state (Id TEXT PRIMARY KEY, Payload TEXT NOT NULL, Revision INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS media_files (Id TEXT PRIMARY KEY, ContentType TEXT NOT NULL, Size INTEGER NOT NULL,
                    Name TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS admin_accounts (Id INTEGER PRIMARY KEY AUTOINCREMENT, Email TEXT NOT NULL UNIQUE,
                    PasswordHash TEXT NOT NULL);
            """)
            db.execute("INSERT INTO site_state (Id, Payload, Revision) VALUES ('main', ?, 0) "
                       "ON CONFLICT(Id) DO UPDATE SET Payload=excluded.Payload, Revision=0", (json.dumps(state, ensure_ascii=False),))
            for entry in details:
                db.execute("INSERT OR REPLACE INTO media_files (Id,ContentType,Size,Name,CreatedAtUtc) VALUES (?,?,?,?,?)", entry)
            db.commit()
        for id_ in ids:
            shutil.copy2(staged / id_, media_path / id_)
    print(f"Hazır: {len(state['players'])} oyuncu, {len(ids)} medya, {db_path}")


if __name__ == "__main__":
    main()
