import pathlib
import tempfile
import unittest

from stage_runtime import strip_release_symbols


class StageRuntimeTests(unittest.TestCase):
    def test_strips_debug_symbols_without_removing_runtime_libraries(self):
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            (output / "libSkiaSharp.pdb").write_bytes(b"debug")
            (output / "libSkiaSharp.dll").write_bytes(b"runtime")
            strip_release_symbols(output)
            self.assertFalse((output / "libSkiaSharp.pdb").exists())
            self.assertTrue((output / "libSkiaSharp.dll").exists())


if __name__ == "__main__":
    unittest.main()
