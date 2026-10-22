# Benchmark: Unity official CLI vs EES (October 2026)

The same AI agent did the same seven Unity tasks with Unity's official CLI and with EES. We measured whether each run was correct, how many tokens it used, and how long it took. Task 2 uses a later EES 12.3.0 rerun; the other six EES tasks use 12.2.9, and all CLI results are unchanged.

**Results:** both methods completed all 7 tasks. EES used **56.4% fewer total tokens** (2,979,091 vs 6,835,375) and fewer tokens on all 7 tasks. Total time was 950 s vs 1,008 s. On task 2, running a test, EES used fewer tokens but still took longer (73 s vs 44 s).

[日本語は下にあります](#日本語)

## Conditions

| Item | Value |
|---|---|
| Date | 2026-10-16 |
| Unity | 6000.3.7f1, URP, one Editor shared by both methods (runs were sequential, never in parallel) |
| Method A | Unity CLI 1.0.0-beta.13 with `com.unity.pipeline` 0.8.0-exp.1 |
| Method B | EES 12.2.9 for tasks 1 and 3–7; EES 12.3.0 rerun for task 2. Called through the optional [ees-call](../../ees-call) 1.1.1 client |
| AI agent | Codex CLI 0.162.1, model gpt-5.6-sol, reasoning effort medium |
| Sessions | A new, independent session for every run, started from an empty folder outside the project. Each prompt pointed only to its own method's entry point. |
| Limits | 900 seconds or 60 assistant messages per run |
| Tokens | Totals from Codex's own usage report (input including cached input, plus output) |
| Correctness | Checked by a separate, pre-written checker against the Unity project's real state, not by the agent's own report |

## Results

| Task | What the agent had to do | Total tokens: CLI | Total tokens: EES | EES / CLI | Seconds: CLI | Seconds: EES |
|---|---|---:|---:|---:|---:|---:|
| 1 | List the GameObjects that match a condition (read only) | 372,552 | 146,515 | 0.39 | 82 | 86 |
| 2 | Run an EditMode test and report the result | 279,656 | 167,099 | **0.60** | 44 | 73 |
| 3 | Add a C# component, set a value of 42, save, and verify after reload | 1,691,706 | 574,071 | 0.34 | 243 | 222 |
| 4 | Drag the mouse in Play Mode and report the recorded distance | 783,418 | 550,900 | 0.70 | 114 | 137 |
| 5 | Reproduce a counter bug in Play Mode, fix the script, and confirm the fix | 2,460,334 | 930,225 | 0.38 | 322 | 193 |
| 6 | Set a BlendShape value and save it as a keyframe in a new AnimationClip | 718,427 | 298,056 | 0.41 | 107 | 110 |
| 7 | Select an object and save the Inspector window as a PNG | 529,282 | 312,225 | 0.59 | 96 | 128 |
| **Total** | | **6,835,375** | **2,979,091** | **0.44** | **1,008** | **950** |

Every run passed: 7/7 for each method. No run timed out, used the other method's route, or started from a wrong initial state.

- **Tokens:** EES used fewer total tokens on all 7 tasks. Across the tasks, the geometric mean of the EES/CLI ratio is 0.47; the ratio of total tokens is 0.44.
- **Shell commands run by the agent:** 228 with the CLI, 83 with EES. EES needed fewer on all 7 tasks.
- **Time:** 950 s with EES vs 1,008 s with the CLI. EES was faster on tasks 3 and 5 (322 s → 193 s on the bug-fix task). Task 2 still took longer with EES (73 s vs 44 s).
- **EditMode test rerun:** task 2 originally used 419,399 tokens and 132.4 s with EES 12.2.9. With `run_tests` in 12.3.0, it used 167,099 tokens and 73.3 s. Only the EES side was rerun; the CLI result is reused.
- **Token breakdown:**
  - Uncached input: EES used 38.9% less (201,440 vs 329,494), and less on all 7 tasks.
  - Output: EES used 35.6% more (43,955 vs 32,409), and more on 6 of 7 tasks.
  - Most of the total difference comes from cached input, which grows with the number of conversation turns.

Per-run numbers: [results.csv](results.csv).

## Limitations

- **One run per task and method.** Variation between runs is unknown. Do not read small differences, such as tasks 4 and 7, as a definite win.
- **Combined EES versions.** The table replaces only task 2 with the 12.3.0 rerun. The other six tasks were not rerun with 12.3.0, so these totals do not represent a full benchmark of that version.
- **Input settings were the same for both methods.** The Input System was set so that input reaches the Game View even when Unity is in the background. This benchmark therefore does not measure EES's ability to deliver input to a background Editor.
- **Task coverage.** The tasks cover reading, tests, C# changes, Play Mode input, debugging, animation, and window capture. Every task was made solvable with the features of both methods, and all tasks were fixed before any run.
- **One model, one machine.** Results vary by task, model, and setup.

An earlier comparison (September 2026: EES 11.1.7, Unity CLI 1.0.0-beta.6, 4 tasks, one run each) found 31.3% fewer total tokens for EES.

---

## 日本語

# ベンチマーク：Unity公式CLIとEESの比較（2026年10月）

同じAIエージェントに、Unityの同じ7つの作業をUnity公式CLIとEESで行わせました。正しくできたか、使ったトークンの数、かかった時間を測りました。課題2はEES 12.3.0での再測定値、ほかの6課題は12.2.9の値です。CLIはすべて元の測定値を使っています。

**結果の要点：** どちらの方式も、7課題すべてを正しく完了しました。EESの合計トークンは **56.4%少なく**（2,979,091対6,835,375）、7課題すべてでEESが少なくなりました。合計時間は950秒対1,008秒でした。課題2（テストの実行）もEESのトークンが少なくなりましたが、時間は長くかかりました（73秒対44秒）。

### 条件

| 項目 | 値 |
|---|---|
| 実施日 | 2026-10-16 |
| Unity | 6000.3.7f1、URP。両方式で同じEditorを使いました（同時ではなく、1回ずつ順に実行） |
| 方式A | Unity CLI 1.0.0-beta.13、`com.unity.pipeline` 0.8.0-exp.1 |
| 方式B | 課題1・3〜7はEES 12.2.9、課題2は12.3.0での再測定値。任意のクライアント [ees-call](../../ees-call) 1.1.1 を通して呼び出しました |
| AIエージェント | Codex CLI 0.162.1、モデル gpt-5.6-sol、reasoning medium |
| セッション | 1回ごとに新しい独立したセッション。プロジェクトの外の空のフォルダから始めました。指示文では、その方式の入口だけを案内しました |
| 上限 | 1回あたり900秒、またはアシスタントのメッセージ60件 |
| トークン | Codex自身の使用量の報告の合計（キャッシュされた入力を含む入力と、出力） |
| 正しさ | エージェントの自己申告ではなく、事前に作った別の判定器で、Unityプロジェクトの実際の状態を確かめました |

### 結果

| 課題 | エージェントの作業 | 合計トークン CLI | 合計トークン EES | EES/CLI | 秒 CLI | 秒 EES |
|---|---|---:|---:|---:|---:|---:|
| 1 | 条件に合うGameObjectを列挙する（読むだけ） | 372,552 | 146,515 | 0.39 | 82 | 86 |
| 2 | EditModeのテストを実行し、結果を報告する | 279,656 | 167,099 | **0.60** | 44 | 73 |
| 3 | C#のコンポーネントを追加し、値42を設定して保存し、再読込で確かめる | 1,691,706 | 574,071 | 0.34 | 243 | 222 |
| 4 | Play Modeでマウスをドラッグし、記録された距離を報告する | 783,418 | 550,900 | 0.70 | 114 | 137 |
| 5 | Play Modeでカウンターの不具合を再現し、スクリプトを直し、直ったことを確かめる | 2,460,334 | 930,225 | 0.38 | 322 | 193 |
| 6 | BlendShapeの値を、新しいAnimationClipのキーとして保存する | 718,427 | 298,056 | 0.41 | 107 | 110 |
| 7 | オブジェクトを選び、Inspectorの窓をPNGで保存する | 529,282 | 312,225 | 0.59 | 96 | 128 |
| **合計** | | **6,835,375** | **2,979,091** | **0.44** | **1,008** | **950** |

全試行が合格しました（どちらの方式も7/7）。時間切れ、もう一方の方式の経路の使用、初期状態の不一致は、どれも0件でした。

- **トークン：** 7課題すべてで、EESの合計トークンが少なくなりました。課題ごとのEES/CLIの比の幾何平均は0.47、合計トークンの比は0.44です。
- **エージェントが実行したコマンドの数：** CLIは228回、EESは83回でした。7課題すべてでEESが少なくなりました。
- **時間：** EESは合計950秒、CLIは1,008秒でした。EESが速かったのは課題3と課題5です（不具合を直す課題では322秒→193秒）。課題2は引き続きEESのほうが時間がかかりました（73秒対44秒）。
- **EditMode課題の再測定：** 課題2は、EES 12.2.9では419,399トークン・132.4秒でした。12.3.0で `run_tests` を使うと、167,099トークン・73.3秒になりました。測り直したのはEES側だけで、CLIは元の結果を使っています。
- **トークンの内訳：**
  - キャッシュされない入力：EESが38.9%少なくなりました（201,440対329,494）。7課題すべてでEESが少なくなりました。
  - 出力：EESが35.6%多くなりました（43,955対32,409）。7課題のうち6課題でEESが多くなりました。
  - 合計の差の大部分は、キャッシュされた入力の差から来ています。キャッシュされた入力は、会話のやり取りの回数が増えるほど増えます。

1回ごとの数字：[results.csv](results.csv)

### 限界

- **各課題・各方式1回だけです。** ばらつきは分かりません。課題4や課題7のような小さな差を、確かな勝ちと読まないでください。
- **EESの版が混在しています。** 課題2だけを12.3.0での再測定値に置き換えました。ほかの6課題は12.3.0で測り直していないため、この合計は12.3.0全体の性能検査結果ではありません。
- **入力の設定は両方式で同じです。** Unityが裏にあってもGame Viewに入力が届くよう、Input Systemを設定しました。このため「Unityが裏にあってもEESなら入力を届けられる」という強みは、この比較では測っていません。
- **課題の範囲。** 課題は、読取・テスト・C#の変更・Play中の入力・デバッグ・アニメーション・窓の撮影にわたります。どの課題も両方の方式の機能で解けるようにし、計測の前に固定しました。
- **モデル1つ、機械1台での結果です。** 課題、モデル、環境によって結果は変わります。

前回の比較（2026年9月、EES 11.1.7、Unity CLI 1.0.0-beta.6、4課題、各1回）では、EESの合計トークンは31.3%少なくなりました。
