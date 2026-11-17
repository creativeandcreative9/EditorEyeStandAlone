# Benchmark: Unity official CLI vs EES 12.3.6 (7 tasks × 5 runs, October 2026)

The same AI agent did the same seven Unity tasks with Unity's official CLI and with EES, five times per task and method. We measured whether each run was correct, how many tokens it used, the estimated cost, and the elapsed time.

This measurement replaces the earlier one-run-per-task comparison (2026-10-16, mixed EES versions), which has been removed. Do not combine the numbers of the two.

[日本語は下にあります](#日本語)

## Results

All ratios are EES / CLI. A value below 1 means EES used fewer tokens, less cost, or less time.

| Measure | CLI | EES | EES / CLI (95% CI) |
|---|---:|---:|---:|
| Total tokens, 35 runs each | 30,827,401 | 16,230,009 | **0.53** (0.44–0.62) |
| Geometric mean of the 7 per-task token ratios | | | **0.59** (0.50–0.70) |
| Estimated cost, 35 runs each (published rates) | $22.24 | $14.85 | **0.67** (0.59–0.75) |
| Elapsed time, 35 runs each | 5,483 s | 5,086 s | 0.93 (0.84–1.03) |
| Correct runs | 34 / 35 | 35 / 35 | |

- **Tokens:** EES used about 47% fewer total tokens. The saving varies by task, from about 2.6× fewer (task 5) to no difference (task 6).
- **Time:** the totals are close, and the interval includes 1.0. Across the 7 tasks, the geometric mean of the time ratio is 0.97 (0.86–1.09), so no overall time difference is established.
- **Correctness:** both methods passed every run except one CLI run on task 2 (the answer check failed). That pair is included in the totals above; with it excluded, the geometric mean of the token ratios over the remaining 34 pairs is 0.59.

### Per task

Each ratio is the geometric mean of 5 pairs, with a 95% confidence interval (t-distribution, 4 degrees of freedom). If an interval includes 1.0, a difference cannot be established for that task.

| # | Task | Mean tokens per run: CLI | Mean tokens per run: EES | Token ratio (95% CI) | Time ratio (95% CI) | Correct: CLI | Correct: EES |
|---|---|---:|---:|---:|---:|---:|---:|
| 1 | List the GameObjects that match a condition (read only) | 329,372 | 186,862 | 0.59 (0.33–1.05) | 1.09 (0.77–1.54) | 5/5 | 5/5 |
| 2 | Run an EditMode test and report the result | 275,571 | 190,107 | 0.70 (0.54–0.93) | 0.76 (0.61–0.94) | 4/5 | 5/5 |
| 3 | Add a C# component, set a value of 42, save, and verify after reload | 1,311,201 | 658,491 | 0.56 (0.29–1.09) | 1.15 (0.85–1.54) | 5/5 | 5/5 |
| 4 | Drag the mouse in Play Mode and report the recorded distance | 822,991 | 591,730 | 0.71 (0.44–1.14) | 1.18 (0.80–1.74) | 5/5 | 5/5 |
| 5 | Reproduce a counter bug in Play Mode, fix the script, and confirm the fix | 2,117,216 | 871,423 | 0.38 (0.16–0.89) | 0.61 (0.46–0.81) | 5/5 | 5/5 |
| 6 | Set a BlendShape value and save it as a keyframe in a new AnimationClip | 364,020 | 356,844 | 1.05 (0.57–1.96) | 1.41 (0.96–2.06) | 5/5 | 5/5 |
| 7 | Select an object and save the Inspector window as a PNG | 945,109 | 390,545 | 0.40 (0.22–0.72) | 0.86 (0.42–1.78) | 5/5 | 5/5 |

- **Clear token savings (interval below 1.0):** tasks 2, 5, and 7.
- **Likely savings, but not established (interval includes 1.0):** tasks 1, 3, and 4.
- **No difference:** task 6 (BlendShape). EES also took longer on average, although that interval includes 1.0.
- **Time:** EES was faster on tasks 2 and 5 (intervals below 1.0). No difference is established on the other five tasks.
- **Where the saving comes from:** most of the total difference is cached input, which grows with the number of conversation turns. Output tokens were higher with EES (218,082 vs 182,406 including reasoning; 147,671 vs 104,961 excluding it).

Per-pair ratios: [pair-ratios.csv](pair-ratios.csv).

## Conditions

| Item | Value |
|---|---|
| Date | 2026-10-22 |
| Unity | 6000.3.7f1, URP, one Editor shared by both methods (runs were sequential, never in parallel) |
| Method A | Unity CLI 1.0.0-beta.14, `com.unity.pipeline` 0.9.0-exp.1 |
| Method B | EES 12.3.6 (the same version as on the Asset Store), called through the optional [ees-call](../../ees-call) 1.1.1 client |
| AI agent | Codex CLI 0.163.0, model gpt-5.6-sol, reasoning effort medium |
| Sessions | A new, independent session for every run, started from an empty folder outside the project. Each prompt pointed only to its own method's entry point; the route used was audited after each run. |
| Limits | 900 seconds or 60 assistant messages per run |
| Repetitions and order | 5 runs per task per method (35 pairs, 70 runs). The order was randomized with a fixed seed. The two runs of a pair ran consecutively, and for each task one method ran first in 3 pairs and the other in 2 pairs. |
| Initial state | Before each run: a blank scene, task files identical to the baseline, and both methods targeting the same Editor. The initial state was restored after each run. |
| Tokens | Totals from Codex's own usage report (input including cached input, plus output including reasoning) |
| Cost | Estimated from the published gpt-5.6-sol rates (uncached input $4.00, cached input $0.40, output $20.00 per million tokens; reasoning counted as output), checked 2026-10-22. It includes limited-time pricing and excludes the surcharge for prompts over 272K tokens, because per-request input sizes could not be determined. |
| Correctness | Checked by a separate, pre-written checker against the Unity project's real state, not by the agent's own report |

## Limitations

- **One agent and model.** Only Codex (gpt-5.6-sol, medium reasoning effort) was tested. Other AI tools, such as Claude Code, and other settings were not measured.
- **Seven tasks, one machine, one project.** The tasks cover reading, testing, adding C#, input, debugging, animation, and capture. They may not represent Unity work in general.
- **Five pairs per task.** The per-task intervals are wide. The main conclusion concerns the overall ratio.
- **Input settings were the same for both methods.** The Input System was set so that input reaches the Game View even when Unity is in the background. This benchmark therefore does not measure EES's ability to deliver input to a background Editor.
- **Time is wall-clock time.** It includes the AI's reasoning and communication, and does not directly measure Unity's processing speed.

---

## 日本語

# ベンチマーク：Unity公式CLIとEES 12.3.6の比較（7課題×5回、2026年10月）

同じAIエージェントに、Unityの同じ7つの作業を、Unity公式CLIとEESで、課題ごと・方式ごとに5回ずつ行わせました。正しくできたか、使ったトークンの数、推定費用、かかった時間を測りました。

この測定は、前回の「各課題1回」の比較（2026-10-16、EESの版が混在）に置き換わるもので、前回の資料は削除しました。2つの数値を混ぜて読まないでください。

### 結果

比はすべて EES/CLI です。1より小さいほど、EESのトークン・費用・時間が少ないことを表します。

| 指標 | CLI | EES | EES/CLI（95%信頼区間） |
|---|---:|---:|---:|
| 合計トークン（各35回） | 30,827,401 | 16,230,009 | **0.53**（0.44〜0.62） |
| 課題ごとのトークン比7つの幾何平均 | | | **0.59**（0.50〜0.70） |
| 推定費用（各35回、公開の料金） | $22.24 | $14.85 | **0.67**（0.59〜0.75） |
| 経過時間（各35回） | 5,483秒 | 5,086秒 | 0.93（0.84〜1.03） |
| 正しく完了した回数 | 34 / 35 | 35 / 35 | |

- **トークン：** EESの合計トークンは約47%少なくなりました。減り方は課題によって異なり、約2.6分の1（課題5）から、差なし（課題6）までありました。
- **時間：** 合計は近く、区間は1.0を含みます。7課題をまたいだ時間比の幾何平均は0.97（0.86〜1.09）で、全体として時間の差は示せません。
- **正しさ：** どちらの方式も、課題2のCLIの1回（回答の確認に失敗）を除いて、すべて合格しました。この組も上の合計に含めています。この組を除いた残り34組でも、トークン比の幾何平均は0.59です。

#### 課題ごと

比は、5組の幾何平均と95%信頼区間（t分布、自由度4）です。区間が1.0を含む課題では、差があるとは言えません。

| # | 課題 | 1回の平均トークン CLI | 1回の平均トークン EES | トークン比（95%区間） | 時間比（95%区間） | 正解 CLI | 正解 EES |
|---|---|---:|---:|---:|---:|---:|---:|
| 1 | 条件に合うGameObjectを列挙する（読むだけ） | 329,372 | 186,862 | 0.59（0.33〜1.05） | 1.09（0.77〜1.54） | 5/5 | 5/5 |
| 2 | EditModeのテストを実行し、結果を報告する | 275,571 | 190,107 | 0.70（0.54〜0.93） | 0.76（0.61〜0.94） | 4/5 | 5/5 |
| 3 | C#のコンポーネントを追加し、値42を設定して保存し、再読込で確かめる | 1,311,201 | 658,491 | 0.56（0.29〜1.09） | 1.15（0.85〜1.54） | 5/5 | 5/5 |
| 4 | Play Modeでマウスをドラッグし、記録された距離を報告する | 822,991 | 591,730 | 0.71（0.44〜1.14） | 1.18（0.80〜1.74） | 5/5 | 5/5 |
| 5 | Play Modeでカウンターの不具合を再現し、スクリプトを直し、直ったことを確かめる | 2,117,216 | 871,423 | 0.38（0.16〜0.89） | 0.61（0.46〜0.81） | 5/5 | 5/5 |
| 6 | BlendShapeの値を、新しいAnimationClipのキーとして保存する | 364,020 | 356,844 | 1.05（0.57〜1.96） | 1.41（0.96〜2.06） | 5/5 | 5/5 |
| 7 | オブジェクトを選び、Inspectorの窓をPNGで保存する | 945,109 | 390,545 | 0.40（0.22〜0.72） | 0.86（0.42〜1.78） | 5/5 | 5/5 |

- **トークンの減少がはっきりしている課題（区間が1.0より下）：** 課題2・5・7。
- **減る見込みだが、差を示せない課題（区間が1.0を含む）：** 課題1・3・4。
- **差がない課題：** 課題6（BlendShape）。EESのほうが時間も長くかかりましたが、その区間は1.0を含みます。
- **時間：** EESが速かったのは課題2と5です（区間が1.0より下）。ほかの5課題では、差を示せません。
- **差の出どころ：** 合計の差の大部分は、キャッシュされた入力から来ています。キャッシュされた入力は、会話のやり取りの回数が増えるほど増えます。出力トークンはEESのほうが多くなりました（推論を含めて218,082対182,406、含めずに147,671対104,961）。

組ごとの比：[pair-ratios.csv](pair-ratios.csv)

### 条件

| 項目 | 値 |
|---|---|
| 実施日 | 2026-10-22 |
| Unity | 6000.3.7f1、URP。両方式で同じEditorを使いました（同時ではなく、1回ずつ順に実行） |
| 方式A | Unity CLI 1.0.0-beta.14、`com.unity.pipeline` 0.9.0-exp.1 |
| 方式B | EES 12.3.6（Asset Storeと同じ版）。任意のクライアント [ees-call](../../ees-call) 1.1.1 を通して呼び出しました |
| AIエージェント | Codex CLI 0.163.0、モデル gpt-5.6-sol、reasoning medium |
| セッション | 1回ごとに新しい独立したセッション。プロジェクトの外の空のフォルダから始めました。指示文では、その方式の入口だけを案内し、使った経路は毎回あとで監査しました |
| 上限 | 1回あたり900秒、またはアシスタントのメッセージ60件 |
| 回数と順序 | 課題ごと・方式ごとに5回（35組、70回）。順序は固定のseedで無作為にしました。組の2回は連続して実行し、課題ごとに、先に動かす方式が3組と2組になるようにしました |
| 開始状態 | 毎回、空のシーン・課題用ファイルが基準と同一・両方式が同じEditorを対象にしていることを確認し、終了後に元へ戻しました |
| トークン | Codex自身の使用量の報告の合計（キャッシュされた入力を含む入力と、推論を含む出力） |
| 費用 | gpt-5.6-solの公開の料金（キャッシュされない入力 100万トークンあたり$4.00、キャッシュされた入力 $0.40、出力 $20.00。推論は出力として計算）で推定。2026-10-22に確認。期間限定の価格を含み、272Kトークンを超えるプロンプトの割増は、依頼ごとの入力の大きさを記録から求められないため含めていません |
| 正しさ | エージェントの自己申告ではなく、事前に作った別の判定器で、Unityプロジェクトの実際の状態を確かめました |

### 限界

- **AIエージェントとモデルは1つだけです。** Codex（gpt-5.6-sol、reasoning medium）だけを測りました。Claude Codeなどほかのツールや、ほかの設定は測っていません。
- **7課題、1台、1プロジェクトです。** 課題は、読取・テスト・C#の追加・入力・デバッグ・アニメーション・撮影にわたりますが、Unityの作業全般を代表するとは限りません。
- **課題ごとに5組です。** 課題ごとの区間は広く、主な結論は全体の比についてです。
- **入力の設定は両方式で同じです。** Unityが裏にあってもGame Viewに入力が届くよう、Input Systemを設定しました。このため「Unityが裏にあってもEESなら入力を届けられる」という強みは、この比較では測っていません。
- **時間は実際に経過した時間です。** AIの思考と通信を含み、Unity自身の処理速度を直接測ったものではありません。
