"""Isolated ees-call executable contract tests; no Unity or third-party dependencies.

Usage: python test_cli.py path/to/ees-call.exe evidence.json
"""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import threading
import time


def main():
    binary, evidence = map(lambda x: Path(x).resolve(), sys.argv[1:3])
    results = []
    with tempfile.TemporaryDirectory(prefix="ees契約試験_") as temporary:
        root = Path(temporary) / "日本語 project"
        eye = root / "EditorEye"
        inbox, outbox = eye / "inbox", eye / "outbox"
        inbox.mkdir(parents=True)
        outbox.mkdir()
        exe = eye / "ees-call.exe"
        # Also support framework-dependent publish directories.
        for item in binary.parent.iterdir():
            if item.is_file() and item.suffix in (".exe", ".dll", ".json", ".pdb"):
                shutil.copy2(item, eye / item.name)
        shutil.copy2(binary, exe)

        def invoke(args):
            started = time.monotonic()
            proc = subprocess.run([str(exe), *args], cwd=temporary,
                                  capture_output=True, timeout=5)
            return proc, time.monotonic() - started

        def record(name, action):
            started = time.monotonic()
            try:
                action()
                results.append(dict(name=name, passed=True,
                                    seconds=round(time.monotonic()-started, 3)))
            except Exception as exc:
                results.append(dict(name=name, passed=False, error=repr(exc)))

        def check(proc, code, raw=None):
            assert proc.returncode == code, (proc.returncode, code, proc.stderr.decode("utf-8", "replace"))
            if raw is not None:
                assert proc.stdout == raw, (proc.stdout, raw)
            if code in (2, 3, 4):
                if raw is None:
                    assert proc.stdout == b"", proc.stdout
                assert isinstance(json.loads(proc.stderr), dict)

        def response(identifier, success=True, **extra):
            return json.dumps(dict(id=identifier, success=success,
                                   message="日本語・保持", **extra), ensure_ascii=False,
                              indent=2).encode("utf-8") + b"\r\n"

        def sync(success, strict=False, accepted=False, invalid=None):
            before = set(inbox.glob("*.json"))
            written = []
            errors = []
            stop = threading.Event()
            def responder():
                try:
                    deadline = time.monotonic() + 3
                    while not stop.is_set() and time.monotonic() < deadline:
                        new = set(inbox.glob("*.json")) - before
                        if new:
                            request = json.loads(next(iter(new)).read_bytes())
                            identifier = request["Id"]
                            raw = response(identifier, success,
                                           result={"accepted": True, "async": True,
                                                   "completionFile": f"EditorEye/outbox/{identifier}.done.json"}) if accepted else response(identifier, success)
                            if invalid == "missing":
                                raw = json.dumps({"id": identifier}).encode()
                            elif invalid == "wrong-id":
                                raw = response("wrong")
                            written.append(raw)
                            (outbox / (identifier + ".json")).write_bytes(raw)
                            return
                        time.sleep(.01)
                except Exception as exc:
                    errors.append(repr(exc))
            worker = threading.Thread(target=responder)
            worker.start()
            try:
                proc, elapsed = invoke(["--tool", "fixture", "--wait-seconds", "1.5"] +
                                       (["--fail-on-tool-error"] if strict else []))
            finally:
                stop.set()
                worker.join(4)
            assert not errors, errors
            assert len(set(inbox.glob("*.json")) - before) == 1
            expected = 4 if invalid else (5 if strict and success is False else 0)
            check(proc, expected, None if invalid == "wrong-id" else written[0])
            if accepted:
                assert elapsed < 1.3, elapsed

        def call_with_response(args, make_response):
            before = set(inbox.glob("*.json"))
            observed = []
            errors = []
            stop = threading.Event()
            def responder():
                try:
                    deadline = time.monotonic() + 3
                    while not stop.is_set() and time.monotonic() < deadline:
                        added = set(inbox.glob("*.json")) - before
                        if added:
                            request_path = next(iter(added))
                            request = json.loads(request_path.read_bytes())
                            raw = make_response(request)
                            (outbox / (request["Id"] + ".json")).write_bytes(raw)
                            observed.append((request, raw))
                            request_path.unlink()
                            return
                        time.sleep(.01)
                except Exception as exc:
                    errors.append(repr(exc))
            worker = threading.Thread(target=responder)
            worker.start()
            try:
                proc, elapsed = invoke(args)
            finally:
                stop.set()
                worker.join(4)
            assert not errors, errors
            assert len(observed) == 1, observed
            assert set(inbox.glob("*.json")) == before
            return proc, observed[0][0], observed[0][1], elapsed

        for success in (True, False):
            for strict in (False, True):
                record(f"sync success={success} strict={strict}",
                       lambda s=success, t=strict: sync(s, t))
        record("accepted first response immediate; one publication", lambda: sync(True, True, True))
        record("strict missing success rejected", lambda: sync(True, True, invalid="missing"))
        record("legacy wrong id rejected", lambda: sync(True, invalid="wrong-id"))

        def help_case():
            before = set(inbox.glob("*.json"))
            proc, _ = invoke(["--help"])
            output = proc.stdout.decode("utf-8")
            assert proc.returncode == 0 and not proc.stderr
            for required in ("ees-call 1.2.0", "--code-file <path>", "--text", "--status --id <request-id>",
                             "--wait-completion <path>", "archive"):
                assert required in output, (required, output)
            assert set(inbox.glob("*.json")) == before
        record("--help describes version 1.2.0 and new options without transport access", help_case)

        def code_file_case():
            code = root / "スクリプト_日本語.cs"
            params = root / "options.json"
            code.write_bytes(b"\xff\xfe")
            params.write_text(json.dumps({"Mode": "snippet", "NoFallback": True}), encoding="utf-8")
            proc, request, raw, _ = call_with_response(
                ["--tool", "execute_dynamic_script", "--code-file", str(code.relative_to(Path(temporary))), "--params-file", str(params), "--wait-seconds", "1"],
                lambda request: response(request["Id"], result={"value": "ok"}))
            check(proc, 0, raw)
            assert request["Tool"] == "execute_dynamic_script"
            assert request["Params"]["CodePath"] == str(code.resolve()).replace("\\", "/")
            assert request["Params"]["Mode"] == "snippet" and request["Params"]["NoFallback"] is True
            before = set(inbox.glob("*.json"))
            for args in (["--tool", "fixture", "--code-file", str(code)],
                         ["--tool", "execute_dynamic_script", "--code-file", str(root / "missing.cs")]):
                rejected, _ = invoke(args)
                check(rejected, 2)
                assert set(inbox.glob("*.json")) == before
            collision_params = root / "code-field.json"
            collision_params.write_text('{"Code":"already supplied"}', encoding="utf-8")
            rejected, _ = invoke(["--tool", "execute_dynamic_script", "--code-file", str(code), "--params-file", str(collision_params)])
            check(rejected, 2)
            assert set(inbox.glob("*.json")) == before
            collision_path_params = root / "code-path-field.json"
            collision_path_params.write_text('{"CodePath":"already-supplied.cs"}', encoding="utf-8")
            rejected, _ = invoke(["--tool", "execute_dynamic_script", "--code-file", str(code), "--params-file", str(collision_path_params)])
            check(rejected, 2)
            assert set(inbox.glob("*.json")) == before
        record("--code-file merges Japanese absolute CodePath and rejects ambiguity before send", code_file_case)

        def text_case():
            proc, request, raw, _ = call_with_response(
                ["--tool", "text", "--text", "--wait-seconds", "1"],
                lambda request: json.dumps({"id": request["Id"], "success": True,
                    "result": {"result": "姿勢: 三角座り😀"}}, ensure_ascii=False).encode("utf-8"))
            assert proc.returncode == 0 and proc.stdout == "姿勢: 三角座り😀".encode("utf-8") and not proc.stderr

            proc, request, raw, _ = call_with_response(
                ["--tool", "text-object", "--text", "--wait-seconds", "1"],
                lambda request: json.dumps({"id": request["Id"], "success": True,
                    "result": {"result": {"value": 7}}}, ensure_ascii=False).encode("utf-8"))
            assert proc.returncode == 0 and proc.stdout == b'{"value": 7}' and not proc.stderr

            failure = {"success": False, "error": "script failed", "result": {
                "exception": {"type": "NullReferenceException", "sourceLine": 12,
                              "sourceSnippet": "target.value = null;"},
                "userFrames": [{"sourceLine": 12}, {"sourceLine": 4}],
                "compilationErrors": [{"line": 3, "column": 5, "errorCode": "CS0103", "message": "name missing"}]}}
            proc, request, raw, _ = call_with_response(
                ["--tool", "text-failure", "--text", "--wait-seconds", "1"],
                lambda request: json.dumps(dict(id=request["Id"], **failure), ensure_ascii=False).encode("utf-8"))
            summary = proc.stdout.decode("utf-8")
            assert proc.returncode == 0 and summary.startswith("error: script failed\n")
            for part in ("exception.type: NullReferenceException", "exception.sourceLine: 12",
                         "exception.sourceSnippet: target.value = null;", "userFrames: 12,4", "L3:5 CS0103 name missing"):
                assert part in summary, summary

            accepted, accepted_request, accepted_raw, _ = call_with_response(
                ["--tool", "text-async", "--text", "--wait-seconds", "1"],
                lambda request: json.dumps({"id": request["Id"], "success": True,
                    "result": {"async": True, "completionFile": f"EditorEye/outbox/{request['Id']}.done.json"}}, ensure_ascii=False).encode("utf-8"))
            completion = root / "EditorEye" / "outbox" / (accepted_request["Id"] + ".done.json")
            expected_next = f'next: ees-call --wait-completion "{completion.resolve()}" --id {accepted_request["Id"]}\n'.encode("utf-8")
            assert accepted.returncode == 0 and accepted.stdout == accepted_raw and accepted.stderr == expected_next

            ordinary, _, _, _ = call_with_response(
                ["--tool", "text-sync", "--wait-seconds", "1"],
                lambda request: response(request["Id"], result={"value": 1}))
            assert ordinary.returncode == 0 and not ordinary.stderr
        record("--text UTF-8 values/failure details and async next command", text_case)

        def status_case():
            status_path = eye / "status.json"
            status_path.write_text(json.dumps({"state": "compiling", "pendingRequests": 2,
                "heartbeatUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}), encoding="utf-8")
            archive = outbox / "archive"
            archive.mkdir()
            archived = "status-response"
            (archive / (archived + ".json")).write_text(json.dumps({"id": archived, "success": True,
                "result": {"status": "accepted", "async": True, "completionFile": "EditorEye/outbox/x.done.json"}}), encoding="utf-8")
            done = "status-completed"
            (archive / (done + ".done.json")).write_text(json.dumps({"id": done, "success": False,
                "result": {"outcome": "not_applied"}}), encoding="utf-8")
            queued = "status-queued"
            queued_path = inbox / (queued + ".json")
            queued_path.write_text(json.dumps({"Id": queued, "Tool": "help", "Params": {}}), encoding="utf-8")
            before = {path.name: path.read_bytes() for path in inbox.iterdir()}
            for identifier, expected_state in ((archived, "responded"), (done, "completed"),
                                                (queued, "queued"), ("status-unknown", "unknown")):
                proc, _ = invoke(["--status", "--id", identifier])
                assert proc.returncode == 0 and not proc.stderr, (proc.returncode, proc.stderr)
                payload = json.loads(proc.stdout)
                assert payload["id"] == identifier and payload["state"] == expected_state
                assert payload["unity"]["state"] == "compiling" and payload["unity"]["pendingRequests"] == 2
            assert before == {path.name: path.read_bytes() for path in inbox.iterdir()}
            status_path.unlink()
            missing, _ = invoke(["--status", "--id", "status-unknown"])
            assert missing.returncode == 0 and json.loads(missing.stdout)["unity"]["statusJson"] == "missing"
            invalid, _ = invoke(["--status", "--id", "bad/name"])
            check(invalid, 2)
            assert before == {path.name: path.read_bytes() for path in inbox.iterdir()}

            completion_id = "archive-wait"
            completion_raw = json.dumps({"id": completion_id, "success": True, "result": {"outcome": "activated"}}).encode()
            (archive / (completion_id + ".done.json")).write_bytes(completion_raw)
            proc, _ = invoke(["--wait-completion", str((outbox / (completion_id + ".done.json")).relative_to(root)),
                              "--id", completion_id, "--wait-seconds", "1"])
            assert proc.returncode == 0 and proc.stdout == completion_raw
            assert before == {path.name: path.read_bytes() for path in inbox.iterdir()}
        record("--status four states/archive/missing status and archive completion wait are read-only", status_case)

        counter = 0
        def wait_case(raw_factory=None, code=0, strict=False, delay=0, absolute=False):
            nonlocal counter
            counter += 1
            identifier = f"fixture-{counter}"
            path = outbox / (identifier + ".done.json")
            raw = raw_factory(identifier) if raw_factory else response(identifier)
            before = {p.name: p.read_bytes() for p in inbox.iterdir()}
            worker = None
            if delay:
                def writer():
                    time.sleep(delay)
                    path.write_bytes(raw)
                worker = threading.Thread(target=writer)
                worker.start()
            elif raw is not None:
                path.write_bytes(raw)
            args = ["--wait-completion", str(path if absolute else path.relative_to(root)),
                    "--id", identifier, "--wait-seconds", ".6"]
            if strict:
                args.append("--fail-on-tool-error")
            try:
                proc, _ = invoke(args)
                # Valid JSON is retained for semantic classification errors too.
                retained = code in (0, 5)
                if code == 4 and raw is not None:
                    try:
                        retained = json.loads(raw).get("id") == identifier
                    except (ValueError, UnicodeError):
                        pass
                check(proc, code, raw if retained else None)
                if code == 3:
                    assert json.loads(proc.stderr)["submissionState"] == "completion-unconfirmed"
            finally:
                if worker:
                    worker.join(3)
            assert before == {p.name: p.read_bytes() for p in inbox.iterdir()}
            if code == 3:
                path.write_bytes(response(identifier))
                again, _ = invoke(args)
                check(again, 0, response(identifier))

        record("existing done relative and raw UTF8 fidelity", lambda: wait_case())
        record("delayed done absolute", lambda: wait_case(delay=.2, absolute=True))
        record("final failure legacy", lambda: wait_case(lambda i: response(i, False)))
        record("final failure strict retains bytes", lambda: wait_case(lambda i: response(i, False), 5, True))
        invalids = {
            "wrong id": lambda i: response("wrong"),
            "malformed": lambda i: b"{broken",
            "invalid UTF8": lambda i: b'{"id":"' + i.encode() + b'","success":true,"x":"\xff"}',
            "missing success": lambda i: json.dumps({"id": i}).encode(),
            "string success": lambda i: response(i, "false"),
            "null success": lambda i: response(i, None),
            "accepted done": lambda i: response(i, result={"accepted": True, "async": True}),
            "outer async done": lambda i: response(i, **{"async": True}),
        }
        for name, factory in invalids.items():
            record(name, lambda f=factory: wait_case(f, 4))
        record("timeout then late done reread", lambda: wait_case(lambda i: None, 3))

        invalid_args = [[], ["--unknown"], ["--fail-on-tool-error=true", "--tool", "x"],
                        ["--tool", "x", "--fail-on-tool-error", "--fail-on-tool-error"],
                        ["--wait-completion", "EditorEye/outbox/x.done.json"],
                        ["--id", "x", "--tool", "x"]]
        base = ["--wait-completion", "EditorEye/outbox/x.done.json", "--id", "x"]
        invalid_args += [base + ["--tool", "x"], base + ["--params-file", "-"],
                         ["--wait-completion", "../outside.json", "--id", "x"],
                         ["--wait-completion", "EditorEye/inbox/x.json", "--id", "x"]]
        invalid_args += [base + ["--wait-seconds", n] for n in ("NaN", "Infinity", "-1", "0", "1e999")]
        for args in invalid_args:
            def invalid_case(args=args):
                before = {p.name: p.read_bytes() for p in inbox.iterdir()}
                proc, _ = invoke(args)
                check(proc, 2)
                assert before == {p.name: p.read_bytes() for p in inbox.iterdir()}
            record("invalid arguments " + repr(args), invalid_case)
    report = dict(binary=str(binary), sha256=hashlib.sha256(binary.read_bytes()).hexdigest(),
                  passed=sum(r["passed"] for r in results), total=len(results), tests=results)
    evidence.parent.mkdir(parents=True, exist_ok=True)
    evidence.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k != "tests"}, ensure_ascii=False))
    return 0 if report["passed"] == report["total"] else 1


if __name__ == "__main__":
    sys.exit(main())
