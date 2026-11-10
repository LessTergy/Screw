"""Prepare a Unity-exported request manifest with isolated CoACD workers."""

import json
import os
import sys
from concurrent.futures import ProcessPoolExecutor, as_completed
from pathlib import Path

os.environ.setdefault("OMP_NUM_THREADS", "2")
os.environ.setdefault("OPENBLAS_NUM_THREADS", "1")
CACHE = Path(__file__).resolve().parents[2] / ".tools/coacd/cache"
WORKER = Path(__file__).with_name("coacd_boxes.py").read_bytes().decode("utf-8-sig")


def pending_requests(paths):
    pending = []
    for name in dict.fromkeys(paths):
        path = Path(name).resolve()
        if path.parent != CACHE.resolve() or not path.name.endswith(".input.json"):
            raise ValueError("Expected a request in the project's CoACD cache.")
        output = path.with_name(path.name.replace(".input.json", ".json"))
        if output.exists():
            continue
        request = json.loads(path.read_text(encoding="utf-8-sig"))
        if request.get("worker") != WORKER:
            raise ValueError("Request uses an outdated worker: " + path.name)
        pending.append(str(path))
    return pending


def prepare(path):
    from coacd_boxes import decompose

    path = Path(path)
    result = decompose(json.loads(path.read_text(encoding="utf-8-sig")))
    output = path.with_name(path.name.replace(".input.json", ".json"))
    temporary = output.with_suffix("." + str(os.getpid()) + ".tmp")
    temporary.write_text(json.dumps(result, allow_nan=False), encoding="utf-8")
    temporary.replace(output)
    return path.name


if __name__ == "__main__":
    paths = pending_requests(json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig")))
    with ProcessPoolExecutor(max_workers=min(4, os.cpu_count() or 1)) as executor:
        futures = [executor.submit(prepare, path) for path in paths]
        for count, future in enumerate(as_completed(futures), 1):
            print("Prepared " + str(count) + "/" + str(len(paths)) + ": " + future.result(), flush=True)
