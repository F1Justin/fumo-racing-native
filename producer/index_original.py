#!/usr/bin/env python3
"""Private build-time source index; stores hashes and locators, never raw content."""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import sqlite3
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "importer"))
from content_importer.files import sha256
from content_importer.source import OriginalStore
from semantic import SourceIndex


class HashFields:
    def __init__(self, connection):
        self.connection = connection

    def setdefault(self, key, value):
        number, field, size = value
        self.connection.execute("INSERT OR IGNORE INTO fields VALUES (?, ?, ?, ?)",
                                (key, number, json.dumps(field, separators=(",", ":")), size))

    def get(self, key):
        row = self.connection.execute("SELECT locator, field, size FROM fields WHERE hash = ?", (key,)).fetchone()
        return (row[0], json.loads(row[1]), row[2]) if row else None


def input_identity(root):
    files = [root / "data.unity3d", *sorted(root.glob("StreamingAssets/aa/StandaloneWindows64/*.bundle"))]
    rows = [{"file": str(p.relative_to(root)), "sha256": sha256(p), "bytes": p.stat().st_size} for p in files]
    digest = hashlib.sha256(json.dumps(rows, sort_keys=True).encode()).hexdigest()
    return rows, digest


def make_index(root, database):
    rows, digest = input_identity(root)
    if database.exists():
        raise FileExistsError("Refusing to overwrite an existing index")
    database.parent.mkdir(parents=True, exist_ok=True)
    connection = sqlite3.connect(database)
    connection.execute("CREATE TABLE fields(hash BLOB PRIMARY KEY, locator INTEGER, field TEXT, size INTEGER) WITHOUT ROWID")
    connection.execute("CREATE TABLE metadata(key TEXT PRIMARY KEY, value TEXT)")
    connection.execute("PRAGMA journal_mode = OFF")
    connection.execute("PRAGMA synchronous = OFF")
    index = SourceIndex()
    index.fields = HashFields(connection)
    store = OriginalStore(root, [r["file"] for r in rows])
    started = time.monotonic()
    count = 0
    for reader in store.objects():
        index.add(reader, store.reader_locations[id(reader)])
        count += 1
        if count % 2000 == 0:
            print(json.dumps({"objects": count, "elapsed": round(time.monotonic() - started), "errors": len(index.errors)}), flush=True)
            connection.commit()
    metadata = {"schema": 1, "complete": True, "identity": digest, "originalFiles": rows,
                "locators": index.locators, "types": [[*key, values] for key, values in index.by_type_name.items()],
                "statistics": dict(index.statistics), "errors": index.errors}
    for key, value in metadata.items():
        connection.execute("INSERT INTO metadata VALUES (?, ?)", (key, json.dumps(value, ensure_ascii=True)))
    connection.commit()
    connection.close()
    print(json.dumps({"complete": True, "objects": count, "seconds": round(time.monotonic() - started),
                      "errors": len(index.errors), "database": str(database)}), flush=True)


def load_index(database):
    connection = sqlite3.connect("file:" + str(database.resolve()) + "?mode=ro", uri=True)
    values = {key: json.loads(value) for key, value in connection.execute("SELECT key, value FROM metadata")}
    if values.get("schema") != 1 or values.get("complete") is not True:
        raise ValueError("Source index is incomplete")
    index = SourceIndex()
    index.fields = HashFields(connection)
    index.locators = values["locators"]
    index.by_type_name = {(typ, name): ids for typ, name, ids in values["types"]}
    index.statistics = Counter(values["statistics"])
    index.errors = values["errors"]
    return index, values


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--database", type=Path, required=True)
    args = parser.parse_args()
    make_index(args.source.resolve(), args.database.resolve())
