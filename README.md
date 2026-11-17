# Editor Eye StandAlone (EES)

**Eyes and hands in the Unity Editor for your AI coding agent — with safety rails.**

EES is a Unity Editor extension that lets an AI coding agent inspect, edit, run, and verify a Unity project on Windows. The agent works by reading and writing plain files inside your project. There is no server process, network port, or external executable.

**Get it free on the Unity Asset Store:**
https://assetstore.unity.com/packages/tools/ai-ml-integration/supercharge-your-development-workflow-editor-eye-standalone-381732

This repository is the project's public home: information, bug reports, and a way to support development. It does not contain the package itself.

## Why EES

- **Built to keep token use low.** The agent reads only the help it needs. Each request returns the result and its diagnostics in one compact response. Large outputs are saved to a file instead of flooding the conversation. In our comparison with Unity's official CLI on seven practical tasks, repeated five times each, EES used **about 47% fewer total tokens** (16.2M vs 30.8M over 35 runs each; ratio 0.53, 95% CI 0.44–0.62) and about a third less estimated cost. Elapsed time was about the same. The saving depends on the task, and for one task (BlendShape keyframes) there was no difference. EES completed all 35 runs and the CLI completed 34. See the [per-task results](#benchmark-unity-official-cli-vs-ees) below.*
- **A transparent bridge.** EES runs inside the Unity Editor. There is no server, port, or helper process to install or keep running. Every request and response is a plain JSON file you can open and read.
- **Animation editing.** Apply Humanoid poses and save them as AnimationClips, register clips on an AnimatorController, and record BlendShape values as keyframes.
- **Screen capture and input simulation.** Capture the Game View, Scene View, or any Editor window, and drive Play Mode with mouse and keyboard input, including drags.
- **Automated debugging with scenarios.** The agent arms a Play Mode scenario: when the game logs a signal, EES sends input, waits, captures screenshots and values, and checks assertions, all in one result. The agent reads the result, fixes the code, and runs it again.
- **Smarter C# changes.** New or edited scripts are compiled against your project before anything in Assets changes. Errors come back with their diagnostics and Assets stay untouched. If Unity's own compile still fails, the change is rolled back. Requests survive compilation and Domain Reload.
- **Works while Unity is in the background** for most operations.

\* Measured on 2026-10-22 with EES 12.3.6 via the optional ees-call client and Unity CLI 1.0.0-beta.14 on Unity 6000.3.7f1, using the same AI agent and model (Codex CLI, gpt-5.6-sol, medium) for both. 5 runs per task and method (35 pairs, 70 runs). Only this one agent and model, one machine, and one project were tested. Results vary by task and model.

## Benchmark: Unity official CLI vs EES

The same AI agent did the same seven Unity tasks with each method, five times per task. EES completed all 35 runs; the CLI completed 34 (one answer check failed).

| # | Task | Mean tokens per run: CLI | Mean tokens per run: EES | EES / CLI tokens (95% CI) | EES / CLI time (95% CI) |
|---|---|---:|---:|---:|---:|
| 1 | List the GameObjects that match a condition (read only) | 329,372 | 186,862 | 0.59 (0.33–1.05) | 1.09 (0.77–1.54) |
| 2 | Run an EditMode test and report the result | 275,571 | 190,107 | 0.70 (0.54–0.93) | 0.76 (0.61–0.94) |
| 3 | Add a C# component, set a value of 42, save, and verify after reload | 1,311,201 | 658,491 | 0.56 (0.29–1.09) | 1.15 (0.85–1.54) |
| 4 | Drag the mouse in Play Mode and report the recorded distance | 822,991 | 591,730 | 0.71 (0.44–1.14) | 1.18 (0.80–1.74) |
| 5 | Reproduce a counter bug in Play Mode, fix the script, and confirm the fix | 2,117,216 | 871,423 | **0.38** (0.16–0.89) | 0.61 (0.46–0.81) |
| 6 | Set a BlendShape value and save it as a keyframe in a new AnimationClip | 364,020 | 356,844 | 1.05 (0.57–1.96) | 1.41 (0.96–2.06) |
| 7 | Select an object and save the Inspector window as a PNG | 945,109 | 390,545 | 0.40 (0.22–0.72) | 0.86 (0.42–1.78) |
| | **All tasks (total over 35 runs each)** | **30,827,401** | **16,230,009** | **0.53** (0.44–0.62) | 0.93 (0.84–1.03) |

Each per-task ratio is the geometric mean of 5 pairs. If an interval includes 1.0, a difference cannot be established for that task. Conditions, cost, limitations, and per-pair ratios: [full benchmark](benchmarks/2026-10-unity-cli-vs-ees-5runs/README.md).

## How it works

```
<Unity Project>/EditorEye/inbox/0001.json    ← the agent writes a request
<Unity Project>/EditorEye/outbox/0001.json   ← the agent reads the response
```

EES is **not an MCP server**. Any AI agent that can read and write files in your project folder can use it, for example Claude Code or Codex CLI. After import, EES writes `EditorEye/AGENTS.md`, which tells the agent how to use it.

### ees-call (optional)

[ees-call](ees-call) is a small command-line client. It gives the agent a single entry point for both input and output: one command writes the request to the inbox, waits for the matching response in the outbox, and prints it. EES works without it.

- **One command per request.** The agent does not have to write the file, wait, and read the response as separate steps.
- **Safe with non-English text.** `--output-file` saves the exact UTF-8 response to a file, avoiding text-encoding problems in PowerShell.
- **Clear exit codes.** For example, a timeout means "outcome unknown, do not resend", so the agent does not repeat an operation by mistake.
- **Easy to install.** One self-contained Windows x64 file, MIT license, downloadable from [Releases](../../releases). No .NET install needed.

The benchmark above used EES through ees-call.

## Requirements

- Windows 10/11
- Unity 2022.3 or later (verified on 2022.3.22f1 and 6.3; Built-in, URP, HDRP)
- An AI coding agent with file access to your Unity project. AI usage fees, if any, are separate; EES contains no AI model.

## License

EES is distributed only through the Unity Asset Store, under the [Unity Asset Store End User License Agreement](https://unity.com/legal/as-terms). This repository contains no EES package code. The optional `ees-call` client is MIT-licensed. See [LICENSE.md](LICENSE.md).

## Bug reports and questions

- **Bugs:** open an [Issue](../../issues) using the bug report template. Replies are on a best-effort basis.
- **Questions and discussion:** [Editor Eye Discord community](https://discord.gg/3wBYKyHg8)

## Request for EES Development Support

EES is free. In the [benchmark](#benchmark-unity-official-cli-vs-ees) above, it used about 47% fewer total tokens on the same tasks. If EES saves you on AI costs, consider putting a small part of that toward its development.

Use the **Sponsor** button at the top of this page. Even the price of a coffee helps. Thank you!

---

## 日本語

**AIコーディングエージェントのための、Unityエディタの目と手、そして安全装置。**

Editor Eye StandAlone（EES）は、AIコーディングエージェントがWindows上のUnityプロジェクトを調べ、変更し、動かして確かめられるようにする、Unityエディタの拡張です。エージェントは、プロジェクト内のファイルを読み書きして操作します。サーバーの起動、ネットワークのポート、外部の実行ファイルは要りません。

**Unity Asset Storeで無料で入手できます：**
https://assetstore.unity.com/packages/tools/ai-ml-integration/supercharge-your-development-workflow-editor-eye-standalone-381732

このリポジトリは、案内・不具合報告・開発の支援のための場所です。パッケージ本体は含みません。

### EESを使う利点

- **少ないトークンで済むように作っています。** エージェントは必要な説明だけを読みます。依頼ごとに、結果と診断を1つの簡潔な応答で返します。大きな出力は、会話に流さずファイルに保存します。Unity公式CLIと実作業7課題を5回ずつ比べたところ、EESの合計トークンは **約47%少なく** なり（各35回の合計で1,623万対3,083万。比0.53、95%信頼区間0.44〜0.62）、推定費用は約3分の1少なくなりました。かかった時間は同程度でした。減り方は課題によって異なり、1つの課題（BlendShapeのキー保存）では差がありませんでした。EESは35回すべて、CLIは34回を完了しました。課題ごとの数値は[下の表](#ベンチマークunity公式cliとeesの比較)にあります。※
- **透明な橋渡し役。** EESはUnityエディタの中で動きます。導入や常駐が必要なサーバー・ポート・補助プロセスはありません。依頼と応答はすべて普通のJSONファイルで、開いて中身を読めます。
- **アニメーションの編集。** Humanoidのポーズを当ててAnimationClipとして保存し、AnimatorControllerに登録できます。BlendShapeの値をキーフレームとして記録することもできます。
- **画面のキャプチャと入力のシミュレーション。** Game View・Scene View・任意のエディタの窓を撮影し、マウスとキーボードの入力（ドラッグを含む）でPlay Modeを操作できます。
- **シナリオによるAIの自動デバッグ。** エージェントがPlay Modeのシナリオを仕掛けると、ゲームが合図のログを出したときに、EESが入力・待機・スクリーンショットと値の記録・判定を行い、1つの結果にまとめます。エージェントは結果を読み、コードを直して、もう一度動かします。
- **C#の変更をよりスマートに。** 新しいスクリプトや変更したスクリプトは、Assetsを変える前にプロジェクトに対してコンパイルして確かめます。エラーは診断つきで返り、Assetsは変わりません。Unity本体でのコンパイルに失敗した場合は、変更を元に戻します。コンパイルやDomain Reloadの間も、依頼は失われません。
- **Unityが裏にあっても、** ほとんどの操作が動きます。

※ 2026年10月22日に、EES 12.3.6（任意のクライアントees-call経由）とUnity CLI 1.0.0-beta.14をUnity 6000.3.7f1で、両方とも同じAIエージェントとモデル（Codex CLI、gpt-5.6-sol、medium）を使って測りました。課題ごと・方式ごとに5回（35組、70回）です。測ったのは、このAIエージェントとモデル、1台の機械、1つのプロジェクトだけです。課題やモデルによって結果は変わります。

### ベンチマーク：Unity公式CLIとEESの比較

同じAIエージェントが、同じ7つの課題を、それぞれの方式で5回ずつ行いました。EESは35回すべて、CLIは34回を完了しました（1回は回答の確認に失敗）。

| # | 課題 | 1回の平均トークン CLI | 1回の平均トークン EES | トークンの比 EES/CLI（95%区間） | 時間の比 EES/CLI（95%区間） |
|---|---|---:|---:|---:|---:|
| 1 | 条件に合うGameObjectを列挙する（読むだけ） | 329,372 | 186,862 | 0.59（0.33〜1.05） | 1.09（0.77〜1.54） |
| 2 | EditModeのテストを実行し、結果を報告する | 275,571 | 190,107 | 0.70（0.54〜0.93） | 0.76（0.61〜0.94） |
| 3 | C#のコンポーネントを追加し、値42を設定して保存し、再読込で確かめる | 1,311,201 | 658,491 | 0.56（0.29〜1.09） | 1.15（0.85〜1.54） |
| 4 | Play Modeでマウスをドラッグし、記録された距離を報告する | 822,991 | 591,730 | 0.71（0.44〜1.14） | 1.18（0.80〜1.74） |
| 5 | Play Modeでカウンターの不具合を再現し、スクリプトを直し、直ったことを確かめる | 2,117,216 | 871,423 | **0.38**（0.16〜0.89） | 0.61（0.46〜0.81） |
| 6 | BlendShapeの値を、新しいAnimationClipのキーとして保存する | 364,020 | 356,844 | 1.05（0.57〜1.96） | 1.41（0.96〜2.06） |
| 7 | オブジェクトを選び、Inspectorの窓をPNGで保存する | 945,109 | 390,545 | 0.40（0.22〜0.72） | 0.86（0.42〜1.78） |
| | **全課題（各35回の合計）** | **30,827,401** | **16,230,009** | **0.53**（0.44〜0.62） | 0.93（0.84〜1.03） |

課題ごとの比は、5組の幾何平均です。区間が1.0を含む課題では、差があるとは言えません。条件、費用、限界、組ごとの比は、[ベンチマークの詳細](benchmarks/2026-10-unity-cli-vs-ees-5runs/README.md#日本語)にあります。

### ees-call（任意）

[ees-call](ees-call) は、小さなコマンドラインのクライアントです。依頼（input）と応答（output）の単一窓口になります。コマンド1つで、依頼をinboxに書き、outboxに届いた対応する応答を待って、表示します。EESは、これが無くても動きます。

- **依頼1つにコマンド1つ。** エージェントは、ファイルを書く・待つ・応答を読む、を別々の手順で行わずに済みます。
- **日本語の応答も安全。** `--output-file` で、応答をUTF-8のまま正確にファイルへ保存します。PowerShellでの文字化けを避けられます。
- **分かりやすい終了コード。** たとえば時間切れは「結果は不明。送り直さない」を意味します。エージェントが同じ操作を誤って繰り返すのを防ぎます。
- **導入が簡単。** Windows x64用の1ファイル（.NETの導入は不要）、MITライセンスです。[Releases](../../releases) からダウンロードできます。

上のベンチマークでは、ees-callを通してEESを使いました。

### 動作環境

- Windows 10/11
- Unity 2022.3以降（2022.3.22f1と6.3で確認。Built-in・URP・HDRP）
- Unityプロジェクトのファイルを読み書きできるAIコーディングエージェント（Claude Code、Codex CLIなど）。AIの利用料金は別です。EESはAIのモデルを含みません。

### ライセンス

EESはUnity Asset Storeだけで、[Unity Asset Store End User License Agreement](https://unity.com/legal/as-terms) のもとで配布しています。このリポジトリにはEESのパッケージのコードを含みません。任意のクライアント `ees-call` はMITライセンスです。[LICENSE.md](LICENSE.md) を参照してください。

### 不具合報告と質問

- **不具合：** 不具合報告のテンプレートで [Issue](../../issues) を作ってください。返答は不定期です。
- **質問・相談：** [Editor Eye Discord コミュニティ](https://discord.gg/3wBYKyHg8)

### EES開発支援のお願い

EESは無料です。上の[ベンチマーク](#ベンチマークunity公式cliとeesの比較)では、同じ作業で使ったトークンが約47%少なくなりました。EESで浮いたAIの利用料の一部を、開発に回していただけると助かります。

このページ上部の **Sponsor** ボタンから、コーヒー1杯分から支援できます。ありがとうございます！
