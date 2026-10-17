from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys
import traceback

from .files import ImportFailure
from .install import install


def main():
    parser = argparse.ArgumentParser(description="Import your original Touhou Fumo Racing 0.2.1 into the native edition.")
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, required=True)
    parser.add_argument("--json-progress", action="store_true")
    args = parser.parse_args()
    def emit(value):
        print(json.dumps(value, ensure_ascii=False), flush=True)
    def progress(phase, completed, total, message):
        if args.json_progress:
            emit({"type": "progress", "phase": phase, "completed": completed, "total": total, "message": message})
        else:
            print(message, file=sys.stderr, flush=True)
    try:
        result = install(args.source, args.output, args.recipe, progress)
        emit(result)
        return 0
    except ImportFailure as exc:
        print("IMPORT_FAILED " + str(exc), file=sys.stderr, flush=True)
        emit({"type": "error", "message": exc.public_message})
        return 2
    except Exception:
        traceback.print_exc(file=sys.stderr)
        emit({"type": "error", "message": "导入未完成。请重试；若仍失败，请保留日志并反馈。"})
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
