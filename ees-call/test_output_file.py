"""Isolated output-file/PowerShell integration tests. Never contacts Unity."""
import base64
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import threading
import time


def main():
    binary, evidence = (Path(p).resolve() for p in sys.argv[1:3])
    evidence.parent.mkdir(parents=True, exist_ok=True)
    root = Path(tempfile.mkdtemp(prefix="ees_output_日本語_", dir=evidence.parent)).resolve()
    root.relative_to(evidence.parent.resolve())
    eye = root / "EditorEye"
    inbox, outbox, logs = eye / "inbox", eye / "outbox", root / "結果 logs"
    for directory in (inbox, outbox, logs, eye / "ees-call-traces"):
        directory.mkdir(parents=True, exist_ok=True)
    exe = eye / "ees-call.exe"
    shutil.copy2(binary, exe)
    tests = []
    sequence = 0

    def record(name, action):
        try:
            action()
            tests.append({"name": name, "passed": True})
        except Exception as exc:
            tests.append({"name": name, "passed": False, "error": repr(exc)})

    def payload(identifier, success=True, **extra):
        body = dict(id=identifier, success=success, result={"result": json.dumps({"姿勢名": "三角座り", "注記": "足を伸ばす😀・日本語"}, ensure_ascii=False)})
        body.update(extra)
        return (json.dumps(body, ensure_ascii=False, indent=2) + "\n").encode("utf-8")

    def invoke(args):
        return subprocess.run([str(exe), *args], cwd=logs, capture_output=True, timeout=10)

    def snapshot():
        return {p.name: p.read_bytes() for p in inbox.glob("*.json")}

    def check(proc, code, output=None, raw=None, stdout_expected=b"", stderr_expected=b""):
        assert proc.returncode == code, (proc.returncode, code, proc.stderr)
        assert proc.stdout == stdout_expected, (proc.stdout, stdout_expected)
        if code in (2, 3, 4):
            assert isinstance(json.loads(proc.stderr), dict), proc.stderr
        else:
            assert proc.stderr == stderr_expected, (proc.stderr, stderr_expected)
        if output is not None:
            if raw is None:
                assert not output.exists(), output
            else:
                assert output.read_bytes() == raw
                assert not raw.startswith(b"\xef\xbb\xbf")
                with output.open(encoding="utf-8") as stream:
                    json.load(stream)
        assert not list(logs.glob(".ees-call-output-*.tmp"))

    def tool(factory=payload, code=0, strict=False, race=False, respond=True, text=False):
        nonlocal sequence
        sequence += 1
        output = logs / f"応答 {sequence} 😀.json"
        before = snapshot()
        captured = []
        errors = []
        stop = threading.Event()

        def responder():
            try:
                deadline = time.monotonic() + 5
                while not stop.is_set() and time.monotonic() < deadline:
                    added = set(inbox.glob("*.json")) - {inbox / n for n in before}
                    if added:
                        assert len(added) == 1
                        try:
                            request_bytes = next(iter(added)).read_bytes()
                        except OSError:
                            # Windows can briefly deny access to a newly published
                            # request. Retry only this fixture read, never the CLI call.
                            time.sleep(.005)
                            continue
                        request = json.loads(request_bytes)
                        assert request["Tool"] == "fixture"
                        assert request["Params"]["名前"] == "日本語の入力"
                        raw = factory(request["Id"])
                        response = outbox / (request["Id"] + ".json")
                        if race:
                            output.write_bytes(b"concurrent-owner")
                        if respond:
                            pending_response = response.with_suffix(".tmp")
                            pending_response.write_bytes(raw)
                            pending_response.rename(response)
                        captured.append((request, response, raw))
                        return
                    time.sleep(.005)
                raise AssertionError("No request observed")
            except Exception as exc:
                errors.append(repr(exc))

        params = root / "入力.json"
        params.write_text(json.dumps({"名前": "日本語の入力"}, ensure_ascii=False), encoding="utf-8")
        worker = threading.Thread(target=responder)
        worker.start()
        args = ["--tool", "fixture", "--params-file", str(params), "--output-file", output.name, "--wait-seconds", ".20" if not respond else "2"]
        if strict:
            args.append("--fail-on-tool-error")
        if text:
            args.append("--text")
        try:
            proc = invoke(args)
        finally:
            stop.set()
            worker.join(6)
        assert not errors, errors
        assert len(captured) == 1
        assert len(snapshot()) == len(before) + 1, "tool was resent"
        request, response, raw = captured[0]
        if respond:
            assert response.read_bytes() == raw, "source outbox changed"
        if race:
            check(proc, code)
            assert output.read_bytes() == b"concurrent-owner"
            diagnostic = json.loads(proc.stderr)
            assert Path(diagnostic["responsePath"]) == response
            assert str(output) in diagnostic["reason"]
            assert "do not resend" in diagnostic["reason"]
        else:
            valid_response = respond and code in (0, 5)
            if code == 4 and respond:
                try:
                    valid_response = json.loads(raw)["id"] == request["Id"]
                except (ValueError, KeyError, UnicodeError):
                    pass
            stdout_expected = b""
            stderr_expected = b""
            if text and respond and code in (0, 5):
                envelope = json.loads(raw)
                if envelope.get("success") is True:
                    stdout_expected = envelope["result"]["result"].encode("utf-8")
            if respond and code in (0, 5):
                envelope = json.loads(raw)
                result = envelope.get("result") or {}
                completion_file = result.get("completionFile")
                if result.get("async") is True and isinstance(completion_file, str) and completion_file:
                    completion_path = Path(completion_file)
                    if not completion_path.is_absolute():
                        completion_path = root / completion_path
                    stderr_expected = f'next: ees-call --wait-completion "{completion_path.resolve()}" --id {request["Id"]}\n'.encode("utf-8")
            check(proc, code, output, raw if valid_response else None, stdout_expected, stderr_expected)
            if code == 0 and not (json.loads(raw).get("result") or {}).get("async"):
                assert json.loads(json.loads(output.read_text(encoding="utf-8"))["result"]["result"])["姿勢名"] == "三角座り"

    def completion(factory=payload, code=0, strict=False, respond=True, reread=False):
        nonlocal sequence
        sequence += 1
        identifier = f"completion-{sequence}"
        response = outbox / (identifier + ".done.json")
        output = logs / f"完了 {sequence}.json"
        raw = factory(identifier)
        if respond:
            response.write_bytes(raw)
        before = snapshot()
        args = ["--wait-completion", str(response.relative_to(root)), "--id", identifier,
                "--output-file=" + str(output), "--wait-seconds", ".15"]
        if strict:
            args.append("--fail-on-tool-error")
        proc = invoke(args)
        retained = respond and code in (0, 5)
        if respond and code == 4:
            try:
                retained = json.loads(raw)["id"] == identifier
            except (UnicodeError, ValueError, KeyError):
                pass
        check(proc, code, output, raw if retained else None)
        if reread:
            response.write_bytes(raw)
            check(invoke(args), 0, output, raw)
        assert snapshot() == before, "completion wait sent a request"
        if respond or reread:
            assert response.read_bytes() == raw

    def rejected(output, arguments=None):
        before = snapshot()
        proc = invoke(arguments or ["--tool", "fixture", "--output-file", str(output), "--wait-seconds", ".1"])
        check(proc, 2)
        assert before == snapshot(), "invalid destination published a request"

    try:
        record("tool Unicode nested JSON; relative Japanese/emoji output; exact bytes", lambda: tool())
        record("--text writes UTF-8 text to stdout and the full response to --output-file", lambda: tool(text=True))
        record("tool failure legacy preserves file", lambda: tool(lambda i: payload(i, False)))
        record("tool failure strict exit 5 preserves file", lambda: tool(lambda i: payload(i, False), 5, True))
        record("tool unclassified response preserves file and stderr", lambda: tool(lambda i: json.dumps(dict(id=i)).encode(), 4, True))
        record("async acceptance saved without waiting for done", lambda: tool(lambda i: payload(i, result={"async": True, "completionFile": f"EditorEye/outbox/{i}.done.json"})))
        record("tool timeout leaves no completed file and no resend", lambda: tool(code=3, respond=False))
        record("tool malformed response leaves no completed file", lambda: tool(lambda i: b"{broken", 4))
        record("destination race preserves other owner's data and original response", lambda: tool(code=4, race=True))
        record("completion Unicode exact bytes; absolute output", lambda: completion())
        record("completion failure strict saves exact response", lambda: completion(lambda i: payload(i, False), 5, True))
        record("completion acceptance is saved but rejected as terminal", lambda: completion(lambda i: payload(i, result={"async": True}), 4))
        record("completion malformed JSON is not saved", lambda: completion(lambda i: b"{broken", 4))
        record("completion invalid UTF8 is not saved", lambda: completion(lambda i: b'{"id":"' + i.encode() + b'","bad":"\xff"}', 4))
        record("completion wrong ID is not saved", lambda: completion(lambda i: payload("other-id"), 4))
        record("completion timeout and late reread; no requests", lambda: completion(code=3, respond=False, reread=True))
        existing = logs / "existing.json"
        existing.write_bytes(b"original-owner")
        record("existing file rejected before send", lambda: (rejected(existing), existing.read_bytes() == b"original-owner" or (_ for _ in ()).throw(AssertionError("overwritten"))))
        record("existing directory rejected before send", lambda: rejected(logs))
        record("missing parent rejected before send", lambda: rejected(logs / "missing" / "output.json"))
        record("invalid file name rejected before send", lambda: rejected(logs / "invalid:name.json"))
        for directory in (inbox, outbox, eye / "ees-call-traces"):
            (directory / "nested").mkdir()
            record("protected destination " + directory.name, lambda d=directory: rejected(d / "blocked.json"))
            record("protected descendant " + directory.name, lambda d=directory: rejected(d / "nested" / "blocked.json"))
        for args in (["--tool", "fixture", "--output-file"], ["--tool", "fixture", "--output-file="],
                     ["--tool", "fixture", "--output-file", "a.json", "--output-file", "b.json"]):
            record("output option validation " + repr(args), lambda a=args: rejected(None, a))

        if os.name == "nt":
            def nondirectory_parent():
                parent = logs / "parent-is-a-file"
                parent.write_bytes(b"original-parent")
                rejected(parent / "blocked.json")
                assert parent.read_bytes() == b"original-parent"
            record("non-directory parent cannot be written; rejected before send", nondirectory_parent)

            def protected_junction():
                # The repository volume need not support NTFS reparse points.
                # Use the OS temporary volume for this separate private project.
                junction_root = Path(tempfile.mkdtemp(prefix="ees-output-junction-")).resolve()
                junction_eye = junction_root / "EditorEye"
                junction_inbox = junction_eye / "inbox"
                junction_outbox = junction_eye / "outbox"
                link = junction_root / "outbox-alias"
                try:
                    junction_inbox.mkdir(parents=True)
                    junction_outbox.mkdir()
                    junction_exe = junction_eye / "ees-call.exe"
                    shutil.copy2(binary, junction_exe)
                    quote = lambda p: "'" + str(p).replace("'", "''") + "'"
                    script = "New-Item -ItemType Junction -Path " + quote(link) + " -Target " + quote(junction_outbox) + " -ErrorAction Stop | Out-Null"
                    encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
                    proc = subprocess.run(["C:/Program Files/PowerShell/7/pwsh.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded], capture_output=True, timeout=15)
                    assert proc.returncode == 0, proc.stderr
                    proc = subprocess.run([str(junction_exe), "--tool", "fixture", "--output-file", str(link / "blocked.json"), "--wait-seconds", ".1"], capture_output=True, timeout=10)
                    check(proc, 2)
                    assert not list(junction_inbox.iterdir()) and not list(junction_outbox.iterdir())
                finally:
                    if link.exists():
                        assert link.resolve() == junction_outbox.resolve()
                        os.rmdir(link)
                    assert junction_root.parent == Path(tempfile.gettempdir()).resolve() and junction_root.name.startswith("ees-output-junction-")
                    shutil.rmtree(junction_root)
            record("junction into outbox rejected before send", protected_junction)

            for label, shell in (("Windows PowerShell 5.1", "C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"),
                                 ("PowerShell 7", "C:/Program Files/PowerShell/7/pwsh.exe")):
                def shell_case(shell=shell, label=label):
                    identifier = "shell-unicode-" + str(sequence)
                    response = outbox / (identifier + ".done.json")
                    raw = payload(identifier)
                    response.write_bytes(raw)
                    output = logs / (label + "_日本語😀.json")
                    quote = lambda p: "'" + str(p).replace("'", "''") + "'"
                    args = [str(exe), "--wait-completion", str(response), "--id", identifier, "--output-file", str(output)]
                    code = "[Console]::OutputEncoding=[Text.Encoding]::GetEncoding(932); & " + " ".join(quote(a) for a in args) + "; exit $LASTEXITCODE"
                    encoded = base64.b64encode(code.encode("utf-16-le")).decode("ascii")
                    before = snapshot()
                    proc = subprocess.run([shell, "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded], cwd=logs, capture_output=True, timeout=15)
                    check(proc, 0, output, raw)
                    assert json.loads(json.loads(output.read_text(encoding="utf-8"))["result"]["result"])["注記"] == "足を伸ばす😀・日本語"
                    assert snapshot() == before
                record(label + " CP932 directly saves valid UTF8 without BOM", shell_case)
    finally:
        report = dict(binary=str(binary), sha256=hashlib.sha256(binary.read_bytes()).hexdigest(),
                      unity_contacted=False, passed=sum(t["passed"] for t in tests), total=len(tests), tests=tests)
        evidence.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        # Delete only this run's resolved private fixture, never a computed external path.
        assert root.parent == evidence.parent.resolve() and root.name.startswith("ees_output_")
        shutil.rmtree(root)
    print(json.dumps(report, ensure_ascii=True, indent=2))
    return 0 if all(t["passed"] for t in tests) else 1


if __name__ == "__main__":
    sys.exit(main())
