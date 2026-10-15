# Editor Eye StandAlone (EES)

**Eyes and hands in the Unity Editor for your AI coding agent — with safety rails.**

EES is a Unity Editor extension that lets an AI coding agent inspect, edit, run, and verify a Unity project on Windows. The agent works by reading and writing plain files inside your project. There is no server process, network port, or external executable.

**Get it free on the Unity Asset Store:**
https://assetstore.unity.com/packages/tools/ai-ml-integration/supercharge-your-development-workflow-editor-eye-standalone-381732

This repository is the project's public home: information, bug reports, and a way to support development. It does not contain the package itself.

## Why EES

- **Built to keep token use low.** The agent reads only the help it needs. Each request returns the result and its diagnostics in one compact response. Large outputs are saved to a file instead of flooding the conversation. In our comparison with Unity's official CLI on four practical tasks, both completed all four, and EES used **31.3% fewer total tokens** (922,126 vs 1,342,245; time was about 10% longer).*
- **A transparent bridge.** EES runs inside the Unity Editor. There is no server, port, or helper process to install or keep running. Every request and response is a plain JSON file you can open and read.
- **Animation editing.** Apply Humanoid poses and save them as AnimationClips, register clips on an AnimatorController, and record BlendShape values as keyframes.
- **Screen capture and input simulation.** Capture the Game View, Scene View, or any Editor window, and drive Play Mode with mouse and keyboard input, including drags.
- **Automated debugging with scenarios.** The agent arms a Play Mode scenario: when the game logs a signal, EES sends input, waits, captures screenshots and values, and checks assertions, all in one result. The agent reads the result, fixes the code, and runs it again.
- **Smarter C# changes.** New or edited scripts are compiled against your project before anything in Assets changes. Errors come back with their diagnostics and Assets stay untouched. If Unity's own compile still fails, the change is rolled back. Requests survive compilation and Domain Reload.
- **Works while Unity is in the background** for most operations.

\* Measured in September 2026 with EES 11.1.7 and Unity CLI 1.0.0-beta.6 on Unity 6000.3.7f1, using the same AI model (gpt-5.6-sol, medium) for both, one run per task. Results vary by task and model.

## How it works

```
<Unity Project>/EditorEye/inbox/0001.json    ← the agent writes a request
<Unity Project>/EditorEye/outbox/0001.json   ← the agent reads the response
```

EES is **not an MCP server**. Any AI agent that can read and write files in your project folder can use it, for example Claude Code or Codex CLI. After import, EES writes `EditorEye/AGENTS.md`, which tells the agent how to use it.

Optional: [ees-call](ees-call) is a small MIT-licensed command-line client that sends one request and prints the response in a single command. EES works without it.

## Requirements

- Windows 10/11
- Unity 2022.3 or later (verified on 2022.3.22f1 and 6.3; Built-in, URP, HDRP)
- An AI coding agent with file access to your Unity project. AI usage fees, if any, are separate; EES contains no AI model.

## License

EES is distributed only through the Unity Asset Store, under the [Unity Asset Store End User License Agreement](https://unity.com/legal/as-terms). This repository contains no EES package code. The optional `ees-call` client is MIT-licensed. See [LICENSE.md](LICENSE.md).

## Bug reports and questions

- **Bugs:** open an [Issue](../../issues) using the bug report template. Replies are on a best-effort basis.
- **Questions and discussion:** [Editor Eye Discord community](https://discord.gg/3wBYKyHg8)

## Support the project

EES is free. If it saves you time, you can support its development with the **Sponsor** button at the top of this page. Thank you!

---

## 日本語

**AIコーディングエージェントのための、Unityエディタの目と手、そして安全装置。**

Editor Eye StandAlone（EES）は、AIコーディングエージェントがWindows上のUnityプロジェクトを調べ、変更し、動かして確かめられるようにする、Unityエディタの拡張です。エージェントは、プロジェクト内のファイルを読み書きして操作します。サーバーの起動、ネットワークのポート、外部の実行ファイルは要りません。

**Unity Asset Storeで無料で入手できます：**
https://assetstore.unity.com/packages/tools/ai-ml-integration/supercharge-your-development-workflow-editor-eye-standalone-381732

このリポジトリは、案内・不具合報告・開発の支援のための場所です。パッケージ本体は含みません。

### EESを使う利点

- **少ないトークンで済むように作っています。** エージェントは必要な説明だけを読みます。依頼ごとに、結果と診断を1つの簡潔な応答で返します。大きな出力は、会話に流さずファイルに保存します。Unity公式CLIと実作業4課題で比べたところ、どちらも4課題すべてを完了し、EESの合計トークンは **31.3%少なく** なりました（922,126対1,342,245。所要時間はEESが約10%長い）。※
- **透明な橋渡し役。** EESはUnityエディタの中で動きます。導入や常駐が必要なサーバー・ポート・補助プロセスはありません。依頼と応答はすべて普通のJSONファイルで、開いて中身を読めます。
- **アニメーションの編集。** Humanoidのポーズを当ててAnimationClipとして保存し、AnimatorControllerに登録できます。BlendShapeの値をキーフレームとして記録することもできます。
- **画面のキャプチャと入力のシミュレーション。** Game View・Scene View・任意のエディタの窓を撮影し、マウスとキーボードの入力（ドラッグを含む）でPlay Modeを操作できます。
- **シナリオによるAIの自動デバッグ。** エージェントがPlay Modeのシナリオを仕掛けると、ゲームが合図のログを出したときに、EESが入力・待機・スクリーンショットと値の記録・判定を行い、1つの結果にまとめます。エージェントは結果を読み、コードを直して、もう一度動かします。
- **C#の変更をよりスマートに。** 新しいスクリプトや変更したスクリプトは、Assetsを変える前にプロジェクトに対してコンパイルして確かめます。エラーは診断つきで返り、Assetsは変わりません。Unity本体でのコンパイルに失敗した場合は、変更を元に戻します。コンパイルやDomain Reloadの間も、依頼は失われません。
- **Unityが裏にあっても、** ほとんどの操作が動きます。

※ 2026年9月に、EES 11.1.7とUnity CLI 1.0.0-beta.6をUnity 6000.3.7f1で、両方とも同じAIのモデル（gpt-5.6-sol、medium）を使い、各課題1回ずつ測った結果です。課題やモデルによって結果は変わります。

### 動作環境

- Windows 10/11
- Unity 2022.3以降（2022.3.22f1と6.3で確認。Built-in・URP・HDRP）
- Unityプロジェクトのファイルを読み書きできるAIコーディングエージェント（Claude Code、Codex CLIなど）。AIの利用料金は別です。EESはAIのモデルを含みません。

### ライセンス

EESはUnity Asset Storeだけで、[Unity Asset Store End User License Agreement](https://unity.com/legal/as-terms) のもとで配布しています。このリポジトリにはEESのパッケージのコードを含みません。任意のクライアント `ees-call` はMITライセンスです。[LICENSE.md](LICENSE.md) を参照してください。

任意：[ees-call](ees-call) は、依頼の送信と応答の表示をコマンド1つで行う、MITライセンスの小さなコマンドラインのクライアントです。EESは、これが無くても動きます。

### 不具合報告と質問

- **不具合：** 不具合報告のテンプレートで [Issue](../../issues) を作ってください。返答は不定期です。
- **質問・相談：** [Editor Eye Discord コミュニティ](https://discord.gg/3wBYKyHg8)

### 開発の支援

EESは無料です。役に立ったと感じたら、このページ上部の **Sponsor** ボタンから開発を支援していただけるとうれしいです。
