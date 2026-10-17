import struct
import unittest
from types import SimpleNamespace

from content_importer.files import ImportFailure
from content_importer.grass import generate_grass, fallback_tangent


class GrassTests(unittest.TestCase):
    def mesh(self):
        return SimpleNamespace(m_Vertices=[(2.0, 3.0, 4.0), (-2.0, 1.0, 6.0)],
                               m_Normals=None, m_Tangents=None, m_Colors=None)

    def test_source_selection_three_cards_and_bounds(self):
        schema = {key: None for key in ("m_Name", "m_VertexData", "m_IndexBuffer", "m_SubMeshes", "m_LocalAABB")}
        schema["m_Name"] = "test"
        result = generate_grass(None, schema, [1, 0], 0.5, decoded=self.mesh())
        self.assertEqual(result["m_VertexData"]["m_VertexCount"], 24)
        values = list(struct.iter_unpack("<18f", result["m_VertexData"]["m_DataSize"]))
        self.assertEqual(values[0][:3], (-2.0, 1.0, 6.0))
        self.assertEqual(values[12][:3], (2.0, 3.0, 4.0))
        self.assertEqual(values[0][3:14], (0., 1., 0., 1., 0., 0., 1., 1., 1., 1., 1.))
        self.assertEqual([v[14:] for v in values[:4]], [(1., 0., 0., 0.), (1., 1., 0., 0.), (0., 0., 0., 0.), (0., 1., 0., 0.)])
        self.assertEqual(values[8][16:], (2., 0.))
        indices = struct.unpack("<36H", result["m_IndexBuffer"])
        self.assertEqual(indices[:6], (0, 1, 2, 2, 1, 3))
        self.assertEqual(indices[-6:], (20, 21, 22, 22, 21, 23))
        self.assertEqual(result["m_LocalAABB"], {"m_Center": {"x": 0., "y": 2., "z": 5.}, "m_Extent": {"x": 2.5, "y": 1.5, "z": 1.5}})

    def test_invalid_source_indices_and_parameters(self):
        for indices in ([], [-1], [2], [True], [0, 0], [0, 1, 0]):
            with self.subTest(indices=indices), self.assertRaises(ImportFailure):
                generate_grass(None, {"m_Name": "x"}, indices, 0, decoded=self.mesh())
        for padding in (-1, float("inf"), float("nan"), True):
            with self.subTest(padding=padding), self.assertRaises(ImportFailure):
                generate_grass(None, {"m_Name": "x"}, [0], padding, decoded=self.mesh())

    def test_unknown_target_payload_never_falls_back(self):
        with self.assertRaises(ImportFailure):
            generate_grass(None, {"m_Name": "x", "unexpectedTargetBytes": b"secret"}, [0], 0, decoded=self.mesh())
        self.assertEqual(fallback_tangent((0., 0., 0.)), (0., 0., 0., 1.))


if __name__ == "__main__":
    unittest.main()
