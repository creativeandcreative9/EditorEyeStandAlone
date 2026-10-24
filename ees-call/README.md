# ees-call

An optional command-line client for [Editor Eye StandAlone (EES)](../README.md). It sends one EES request and prints the first matching response, so an AI agent can call EES with one short command instead of writing the inbox/outbox file steps every time.

**EES does not need ees-call.** Any AI agent with file access can use EES directly through `EditorEye/inbox` and `EditorEye/outbox`, as described in the `EditorEye/AGENTS.md` that EES generates. ees-call is a convenience.

- Windows x64, self-contained single file (no .NET install needed to run it).
- License: MIT (this folder only). See [LICENSE](LICENSE). EES itself is distributed through the Unity Asset Store under the Unity Asset Store EULA.

## Install

1. Download `ees-call.exe` from this repository's Releases and check its SHA-256 against the value in the release notes.
2. Put it in `<Your Unity Project>/EditorEye/ees-call.exe`. ees-call finds the project from its own location, not from the current directory.

The executable is not code-signed, so Windows SmartScreen may warn on first run.

## Usage

```
ees-call --tool <name> [--params-file <UTF-8 JSON path or ->] [--code-file <path>] [--text] [--output-file <path>] [--wait-seconds <seconds>] [--fail-on-tool-error]
ees-call --wait-completion <path> --id <request-id> [--output-file <path>] [--wait-seconds <seconds>] [--fail-on-tool-error]
ees-call --status --id <request-id>
ees-call --help
```

```powershell
$ees = '<Your Unity Project>/EditorEye/ees-call.exe'
& $ees --tool help
& $ees --tool get_logs --params-file ./params.json --output-file ./response.json
& $ees --tool execute_dynamic_script --code-file ./scripts/InspectScene.cs --params-file ./script-options.json --text
```

- `--params-file` takes a UTF-8 JSON file, or `-` for standard input. Default: `{}`.
- `--code-file` sets `Params.CodePath` to an existing file's absolute path (with `/` separators). Use it only with `execute_dynamic_script` or `execute_dynamic_compilation`; other `--params-file` fields are retained, but that file must omit `Code` and `CodePath`.
- `--text` prints a successful `result.result` string directly, or JSON for a non-string result. For failures it prints the error, available exception/source lines and user frames, and up to 10 compilation errors. Async acceptance stays full JSON. Output is UTF-8 without a BOM.
- `--output-file` saves the exact full UTF-8 response to a new file. With `--text`, stdout contains the text summary while the file keeps the full response. Use it from PowerShell to avoid text-encoding problems with non-ASCII responses.
- `--wait-seconds` is the response deadline. Default: 120.
- `--status --id <request-id>` reports `completed`, `responded`, `queued`, or `unknown`, plus available `status.json` state and heartbeat, without sending anything. It checks outbox `archive/` too and returns one JSON line.
- An accepted (asynchronous) response is returned immediately and is not final success. Use `--wait-completion` with the reported completion file and request id for a separate, read-only wait; if it has moved, the same filename is read from outbox `archive/`.
- An async response with `completionFile` also prints the next `--wait-completion` command to stderr. It does not wait automatically.

| Exit code | Meaning |
|---|---|
| 0 | Response received (includes tool failures unless `--fail-on-tool-error`) |
| 2 | Invalid input; nothing was sent |
| 3 | Deadline expired; outcome unknown. Do not resend |
| 4 | Invalid response or I/O state; read the diagnostics |
| 5 | Outer `success=false` with `--fail-on-tool-error` |

## Build from source

Requires the .NET SDK 10.0.

```powershell
./build.ps1    # self-contained single-file exe in ./publish, hashes in manifest.json
dotnet run --project ./ContractTests -- ./publish/ees-call.exe
python ./test_cli.py ./publish/ees-call.exe ./contract.json
python ./test_output_file.py ./publish/ees-call.exe ./output-contract.json
```

The Python tests run against a temporary fake project and need no Unity. Python is only used for testing.

---

## 日本語

[Editor Eye StandAlone（EES）](../README.md) を呼び出すための、任意のコマンドラインのクライアントです。EESへの依頼を1つ送り、対応する最初の応答を表示します。AIエージェントは、inbox/outboxのファイルの手順を毎回書かずに、短いコマンド1つでEESを呼べます。

**EESを使うのに、ees-callは必要ありません。** ファイルを読み書きできるAIエージェントなら、EESが作る `EditorEye/AGENTS.md` の説明どおり、`EditorEye/inbox` と `EditorEye/outbox` で直接使えます。ees-callは、それを楽にするための道具です。

- Windows x64、ランタイム同梱の1ファイル（実行に .NET の導入は不要）。
- ライセンス：MIT（このフォルダだけ）。[LICENSE](LICENSE) を参照してください。EES本体は、Unity Asset Storeで、Unity Asset Store EULAのもとで配布しています。

### 導入

1. このリポジトリのReleasesから `ees-call.exe` をダウンロードし、リリースの説明にあるSHA-256と一致するかを確かめてください。
2. `<Unityプロジェクト>/EditorEye/ees-call.exe` に置いてください。ees-callは、作業フォルダではなく、自分の置き場所から対象のプロジェクトを決めます。

実行ファイルにはコード署名がないため、初回の実行時にWindowsのSmartScreenが警告を出すことがあります。

使い方、終了コード、ソースからのビルドは、上の英語の節を参照してください。`--code-file` は `execute_dynamic_script` または `execute_dynamic_compilation` 用、`--text` は文字列結果や失敗診断の UTF-8 表示、`--status --id` は要求とUnityの状態を読むだけの確認です。非同期応答はそのまま返し、完了待ちの次のコマンドを stderr に表示します。
