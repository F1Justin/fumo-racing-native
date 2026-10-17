"""A container header can be neutralized without preserving content values."""


def neutral(value):
    if isinstance(value, dict):
        return {key: neutral(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return []
    if isinstance(value, (bytes, bytearray)):
        return b""
    if isinstance(value, str):
        return ""
    if isinstance(value, bool):
        return False
    if isinstance(value, float):
        return 0.0
    if isinstance(value, int):
        return 0
    if value is None:
        return None
    raise TypeError(type(value).__name__)


def neutralize_object(reader):
    # UnityPy eagerly parses AssetBundle during container construction; retain
    # only its empty schema. Other objects can have zero-length bodies.
    if reader.type.name == "AssetBundle":
        reader.save_typetree(neutral(reader.read_typetree()))
    else:
        reader.set_raw_data(b"")
