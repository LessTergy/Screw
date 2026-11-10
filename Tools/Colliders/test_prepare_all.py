import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import prepare_all


class PrepareAllTests(unittest.TestCase):
    def test_manifest_deduplicates_requests_and_skips_completed_results(self):
        with tempfile.TemporaryDirectory(dir=prepare_all.CACHE.parent) as directory, patch.object(prepare_all, "CACHE", Path(directory)):
            pending = Path(directory) / "pending.input.json"
            pending.write_text(json.dumps({"worker": prepare_all.WORKER}), encoding="utf-8")
            completed = Path(directory) / "completed.input.json"
            completed.with_name("completed.json").write_text("{}", encoding="utf-8")
            self.assertEqual(prepare_all.pending_requests([str(pending), str(completed), str(pending)]),
                             [str(pending.resolve())])

    def test_stale_requests_and_paths_outside_the_cache_are_rejected(self):
        with tempfile.TemporaryDirectory(dir=prepare_all.CACHE.parent) as directory, patch.object(prepare_all, "CACHE", Path(directory)):
            stale = Path(directory) / "stale.input.json"
            stale.write_text(json.dumps({"worker": "older"}), encoding="utf-8")
            with self.assertRaises(ValueError):
                prepare_all.pending_requests([str(stale)])
            with self.assertRaises(ValueError):
                prepare_all.pending_requests([str(Path(directory).parent / "outside.input.json")])


if __name__ == "__main__":
    unittest.main()
