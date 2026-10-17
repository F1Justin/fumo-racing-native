"""PyInstaller entry point. Keep standard streams for launcher JSON progress."""
from content_importer.__main__ import main

if __name__ == "__main__":
    raise SystemExit(main())
