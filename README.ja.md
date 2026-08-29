# Gua

[English](README.md) | 日本語

[![License](https://img.shields.io/github/license/link1345/gua)](https://github.com/link1345/gua/blob/main/LICENSE)
[![Discord](https://img.shields.io/discord/1329272750099136552)](https://discord.gg/Zy65k8AxH2)

> この日本語版は補助ドキュメントです。内容に差異がある場合は、
> [英語版README](README.md)を正しい最新情報として扱ってください。

> **ゲーム向けのPlaywrightスタイルUI自動化。Godot 4.7とUnity 6に対応し、
> MCP経由でAIコーディングエージェントから利用できます。**

**Gua**は、ゲーム向けのランタイムUI自動化プロトコルです。実行中の
ゲームUIをSemantic UI Treeとして公開し、テスト、Inspector、AIエージェントから
利用できる意味情報付きのインターフェースを提供します。壊れやすい画像認識や
画面座標に頼らず、ID、role、text、状態からControlを検索し、操作、待機、ログ確認、
スクリーンショット取得、結果検証を行えます。

Guaは**Godot 4.7 GDScriptアドオン**と**Unity 6**向けのランタイム統合を
提供します。Unityパッケージは、Windows x64 Mono環境のUI Toolkit、uGUI、
TextMeshProランタイムUIを自動収集します。MCPサーバーの`gui-mcp`を使うと、
同じランタイムUIをMCP対応のAIコーディングエージェントへ公開し、AI支援ゲーム開発と
AIプレイテストに利用できます。

**実装 → 起動 → 観測 → テスト → 修正 → 再テスト。**

WebのDOMをSemantic UI Treeへ置き換え、ロケーター、操作、待機、アサーション、
スクリーンショット、Visual比較を実行中ゲームへ適用する、ゲームUI向けの
Playwrightスタイル自動化レイヤーだと考えると分かりやすいでしょう。

## Guaでできること

### Godot UIテスト

- 標準Godot `Control`をSemantic UI Treeとして公開
- 座標ではなくID、role、text、value、状態からControlを検索
- click、focus、値入力、選択、scroll、key入力を実行
- 通常の.NETテストからUI状態の変化を待機・検証
- スクリーンショット、Visual baseline比較、失敗診断artifactを取得
- [`link1345/gua-tester`](https://github.com/link1345/gua-tester)でGodotのE2E UIテストをCI実行

### Unity 6 UIテスト

- UI Toolkit、uGUI、TextMeshProのランタイムUIを自動的に公開
- Editor Play ModeまたはWindows x64 Mono Playerでシーンを操作
- Godotと同じSemantic APIでControlを検索・操作
- ゲーム側アダプターと外部NUnitテストホストを分離
- Unityテスト失敗時にログ、スクリーンショット、診断情報を保存
- [`link1345/gua-tester`](https://github.com/link1345/gua-tester)でWindows x64 Mono PlayerをCIビルド・テスト

### AIコーディングとAIプレイテスト

`gui-mcp`は、MCP対応AIコーディングエージェントをInspectorと同じブリッジへ
接続します。AIはUI Treeの観測、SemanticなControl操作、状態変化の待機、ログ確認、
スクリーンショット取得、RecordingのReplay、Visual比較、小さなテストシーケンスの
実行を、自ら開発を支援しているゲームに対して行えます。

GuaはAIコーディングエージェントを補完します。AIがゲームを編集し、Guaが
実行中ゲームの観測・操作・検証を可能にします。ゲームエンジンやAIを置き換える
ものではなく、Semanticな対象指定は画像認識に依存しません。

### ブラウザネイティブWebMCP（実験的）

Godot Web ExportとUnity WebGLのページは、実行中の同じSemantic UI Treeを実験的な
ブラウザ`document.modelContext` APIから公開できます。
`gua-webmcp`パッケージは、エンジンが
所有する同一ページ内ブリッジに対して`get_ui_tree`、Semantic action、wait、read-onlyの
World Object Tree観測ツール、capabilityで制御されたSemantic Game Action / Raw Input、
任意のscreenshotツールを登録します。world型とselector
定義は`gua-world-tools`として公開します。`gui-mcp`もWebSocket接続も不要で、WebMCP非対応
ブラウザでもゲーム本体はそのまま動作します。各タブがゲームとツール登録を所有し、
独自のブラウザセッションルーターは持ちません。
ゲーム入力は現在のページが所有し、request-correlatedなhost completionを待ちます。
timeout、キャンセル、ツール登録解除、engine終了時には全入力を解放します。Raw toolは
hostが入力pumpとcleanup経路を明示的に初期化するまで公開しません。
Player/Public Agent向けゲーム入力は別権限として既定拒否です。Debug Inspector用の
入力経路を有効化してもWebMCPには公開されません。Godotでは`allow_player_agents`引数、
Unityでは`AllowPlayerAgentSemanticInput` / `AllowPlayerAgentRawInput`を明示指定します。
組み込み方法は[`gua-webmcp`パッケージガイド](packages/webmcp/README.md)を参照してください。
Godot WebアドオンにはDebug用とRelease用のGDExtensionが個別に含まれます。
Godot WebのExport presetで`Extension Support`を有効にすると、Godotが
`web.wasm32.single.debug`または`web.wasm32.single.release`を選択します。

## Godot 4.7対応

推奨Godot統合はGDExtensionを利用するGDScriptアドオンです。標準`Control`を
自動収集し、GDScriptプロジェクトでも.NET有効Godotプロジェクトでも利用できます。
別のC#ランタイムサンプルは実験的で、アダプターの全機能は提供しません。

Godot開発では、Semantic UI自動化、外部E2Eテスト、スクリーンショット・Visual
regressionテスト、CI、Inspectorによる調査、MCPによるAIプレイテストに利用できます。
設定手順は[Godot 4.7 GDScriptアドオン](#godot-47-gdscriptアドオン)を参照してください。

## Unity 6対応

Unityパッケージはランタイムアダプターを自動起動し、UI Toolkit、uGUI、TextMeshProの
Controlを手動登録なしで収集します。`Gua.Testing.Unity`を使うと、通常の.NETテストから
Editor Play Modeまたはビルド済みMono Playerを起動できます。現在の対応範囲は
Unity 6000.5以降、Windows x64、Monoです。IL2CPP、Windows以外、Unity IMGUI、
EditorWindowの自動化には未対応です。導入・検証手順は
[Unity 6 Windows x64](#unity-6-windows-x64)を参照してください。

## GitHub Actions

[`link1345/gua-tester`](https://github.com/link1345/gua-tester)は、Godotと
Unityの両方に対応する公開CI部品です。Godotでは、Godot本体と配布済みアドオンを
準備して外部.NETテストを実行します。

```yaml
- uses: link1345/gua-tester/godot@v2.1
  with:
    project-path: game
    test-project: tests/GuaTester.Tests.csproj
    godot-version: "4.7"
    godot-status: stable
```

Unityでは、LinuxでWindows x64 Mono Playerをビルドし、Windows runnerへ渡して
`Gua.Testing.Unity`のNUnitテストを実行します。

```yaml
jobs:
  unity:
    if: github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository
    uses: link1345/gua-tester/.github/workflows/unity.yml@v2.1
    with:
      project-path: game
      scene-path: Assets/Scenes/Title.unity
      test-project: tests/GuaTester.Unity.Tests.csproj
      artifact-key: game
      unity-version: auto
      gua-tag: gua-v0.15.0
    secrets:
      UNITY_EMAIL: ${{ secrets.UNITY_EMAIL }}
      UNITY_PASSWORD: ${{ secrets.UNITY_PASSWORD }}
      UNITY_LICENSE: ${{ secrets.UNITY_LICENSE }}
      UNITY_SERIAL: ${{ secrets.UNITY_SERIAL }}
```

`gua-tag`で選ぶUPM packageと`Gua.Testing.Unity`のNuGetバージョンは揃えてください。
fork PRにはUnity credentialsが渡らないため、未信頼forkではUnity jobをskipします。
reusable workflowはPlayer起動前にWindows test runnerの画面解像度を1920x1080へ固定します。

## NuGetパッケージ

- **Gua.Core:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Core)](https://www.nuget.org/packages/Gua.Core) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Core)<br>
  .NETからGuaのC ABIランタイムを利用するためのP/Invokeバインディングです。
  Windows x64用ネイティブランタイムも含まれます。
- **Gua.Testing:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Testing)](https://www.nuget.org/packages/Gua.Testing) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Testing)<br>
  通常の.NETテストに、Gua用のロケーター、待機、アサーション、アダプターの
  テストループを追加します。
- **Gua.Testing.Godot:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Testing.Godot)](https://www.nuget.org/packages/Gua.Testing.Godot) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Testing.Godot)<br>
  Godotプロセスを起動し、Guaブリッジ経由で実行中のシーンを操作・検証する
  テストヘルパーです。
- **Gua.Testing.Unity:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Testing.Unity)](https://www.nuget.org/packages/Gua.Testing.Unity) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Testing.Unity)<br>
  Unityプロセスを起動し、Guaブリッジ経由で実行中のシーンを操作・検証する
  テストヘルパーです。
- **Gua.Runtime:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Runtime)](https://www.nuget.org/packages/Gua.Runtime) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Runtime)<br>
  エンジンアダプター開発者向けの共有マネージドラッパーです。P/Invokeを重複実装せず、Semantic frameの公開、actionの処理、スクリーンショット要求の完了、Inspectorブリッジのホストに利用できます。通常のゲームテストでは各エンジン向けパッケージを使用します。
- **Gua.Testing.Visual:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Testing.Visual)](https://www.nuget.org/packages/Gua.Testing.Visual) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Testing.Visual)<br>
  Semantic assertionでは検出できないclipping、Controlの位置ずれ、asset間違い、予期しないoverlayなどの描画regressionをPNG baseline比較で検出します。失敗時はexpected、actual、diff、機械可読な比較結果を保存します。
- **Gua.Testing.Recording:** [![NuGet Version](https://img.shields.io/nuget/v/Gua.Testing.Recording)](https://www.nuget.org/packages/Gua.Testing.Recording) ![NuGet Downloads](https://img.shields.io/nuget/dt/Gua.Testing.Recording)<br>
  再現可能なユーザーフローをSemantic操作として記録し、各stepをホスト側の完了と相関確認しながら再生します。壊れやすい座標や秘密値の平文を保存せず、regression flow、bug再現、scenario共有に利用できます。

パッケージの選び方は[.NETパッケージガイド](https://gua.orizika.com/docs/dotnet-packages/)、
具体的な実装・運用は[Gua.Runtime実装ガイド](https://gua.orizika.com/docs/gua-runtime/)、
[Visualテスト実践](https://gua.orizika.com/visual-testing/)、
[Recording実践](https://gua.orizika.com/recording/)を参照してください。

## MCPとInspector

### World Object Tree

Guaは、明示的にopt-inしたゲーム世界のobjectをSemantic UI Treeとは別に公開できる。各objectはstable ID、意味的なkind、2Dまたは3D座標、hostが決めるplayer可視性、tag、flatなprimitive stateを持つ。Godotでは`gua_world_object` groupと`gua_world_*` metadata、Unityでは`GuaWorldObject` componentを使う。全scene nodeやGameObjectを自動公開することはない。

native bridgeの既定はdebug viewである。host processに`GUA_OBSERVATION_PROFILE=player`を設定すると、UIとWorldの双方へancestor visibility、`private`除外、field ruleを適用してから公開する。client入力からdebugへ昇格することはできない。World v1は観測専用であり、MCPは`get_world_object_tree`、`find_world_objects`、`wait_for_world_object`を提供し、Inspectorは独立したWorld Object Tree panelへ表示する。

`GuaAgentPolicy`は公開fieldのomit、redact、同型replace、数値quantizeとUI action allowlistを定義する。Debug snapshotは完全なtreeを維持し、Playerではsnapshot、query、wait、diagnostics、action認可に同じ投影を使う。debug logは公開せず、screenshotも既定で拒否する。hostが必要性を判断した場合に限り、bridge開始前に明示的に許可できる。

Godot metadataは`gua_world_id`（必須）、`gua_world_kind`、`gua_world_label`、`gua_world_visible_to_player`、`gua_world_active`、`gua_world_agent_exposure`、`gua_world_tags`、`gua_world_state`を使う。state値は文字列、有限数値、真偽値、nullだけを許可し、秘密情報を含めてはならない。整数値はv1 C ABIの`double`表現を正確に往復できる必要があり、JavaScript selector clientはsafe integer範囲外の整数を拒否する。

### 決定論的な仮想時間

GuaClockを時刻源にしたゲームロジックは、停止したり、実時間を待たずに
進めたりできます。既存のengine timerを自動的に置き換える機能ではありません。
まずゲーム本体の対象ロジックを、Godotの`Timer`、Unityの`Time.deltaTime`や
Coroutineなどではなく、GuaClockのScheduleまたはTickを使う実装へ変更します。
その後、テストから共有ClockをInstallして操作します。

```csharp
// ゲーム側の組み込み。production codeで一度行う。
var clock = runtime.Clock;
clock.Install();
clock.Schedule(TimeSpan.FromSeconds(2), ShowMessage);

// テスト側から同じClockを操作する。
clock.Pause();
clock.RunFor(TimeSpan.FromSeconds(2));
```

ここで`Install`が行うのは共有仮想Clockの有効化であり、任意のgame objectへ
Clockを自動注入することではありません。`Pause`の対象は、あらかじめGuaClockの
SchedulerまたはTickへ接続したゲームロジックだけです。engine標準のTimer、
物理、Animation、Audio、OS時刻、ネットワークは停止しません。
bridge、MCP、Inspectorでも`get_clock`、`clock_install`、`clock_pause`、
`clock_run_for`、`clock_resume`を利用できます。

### Semantic Game ActionとRaw Input

ホストはUI Treeと独立したGame Action Mapを明示登録し、button、axis、vector、
textを`press_game_input_action`、`set_game_input_action`、
`release_game_input_action`で操作できます。明示opt-inのRaw toolはcommand schemaに
列挙されたengine共通のW3C physical key code、pointer移動/button/wheel、
Standard Gamepad、text inputを扱います。
保持入力は接続ごとに分離され、leaseは既定5秒・最大60秒です。満了、切断、
reset、replay失敗、session disposeではneutral状態へ戻します。InspectorにはAction
Map、保持lease、Raw操作、緊急`Release all`を表示します。
ローカルC++/.NET sessionは返されたrequest IDをpollしてhost完了を確認します。
enqueue受付だけではadapterが入力を注入したことを意味しません。

Unity 6000.5では`com.unity.inputsystem@1.20.0`のvirtual deviceへ注入し、
Godotではmain threadから`Input.parse_input_event`へ`InputEvent`を渡します。
adapterはinput pumpとcleanup経路が初期化済みのcapabilityだけを公開します。
既存のSemantic UI用`press_key`は変更せず、Raw Keyboard gestureには
`press_physical_key`を使います。

- **gua-webmcp:** [![NPM Version](https://img.shields.io/npm/v/gua-webmcp)](https://www.npmjs.com/package/gua-webmcp) ![NPM Downloads](https://img.shields.io/npm/dw/gua-webmcp)<br>
  ページのWebMCP APIを通じて、GuaのSemantic UI、World Object Tree、
  ゲーム入力ツールを登録するブラウザネイティブadapterです。
- **gui-mcp:** [![NPM Version](https://img.shields.io/npm/v/gui-mcp)](https://www.npmjs.com/package/gui-mcp) ![NPM Downloads](https://img.shields.io/npm/dw/gui-mcp)<br>
  Inspectorと同じWebSocketブリッジを通じて、Guaのランタイム操作を
  AIエージェントへ公開する薄いMCPサーバーです。
- **Gua Inspector:** [![Gua Release](https://img.shields.io/github/actions/workflow/status/link1345/gua/gua-release.yml?branch=main&label=Gua%20Release)](https://github.com/link1345/gua/actions/workflows/gua-release.yml)<br>
  Semantic UI Tree、ノード状態、スクリーンショット、ログを確認し、ランタイムへ
  コマンドを送信できるブラウザー・WindowsデスクトップUIです。

概念上は、Web UIをテストするようにゲームUIを操作できます。

```ts
await game.getByRole("button", { name: "Start Game" }).click()
await expect(game.getById("loading")).toBeVisible()
```

現在の実装では、安定境界となるC ABIの上にC++とC#のAPIを提供し、InspectorとMCPをプロトコルの利用者として接続しています。Godot 4.7とUnity 6向けのアダプターとサンプルも含まれます。

```cpp
gua::testing::get_by_role(ctx, "button", "Start Game").click();
gua::testing::wait_for_text(ctx, "Loading...").to_be_visible();
```

```csharp
GuaAssertions.GetByRole(ui, "button", "Start Game").Click();
GuaAssertions.WaitForText(ui, "Loading...").ToBeVisible();
```

`Click()`はゲームの状態を直接変更せず、クリック要求をキューへ追加します。
ImGuiやGodotなどのゲーム側アダプターが後続フレームで要求を消費し、通常のUI入力として処理した結果をイベントとして返します。

Guaは、ゲームランタイムと自動化ツールをつなぐUIレイヤーです。ゲームエンジン、
エディターツール、テストランナー、AIコーディングエージェントと役割分担して動作します。

## 対象範囲

初期実装は、小さく安定した中核に集中しています。

- プロトコル仕様とJSON Schema
- C ABIランタイムコア
- 薄いC++ラッパー
- ImGuiアダプター
- C++・C#テストヘルパー
- C ABIに対する.NET P/Invokeバインディング
- UI Tree、ノード詳細、スクリーンショット、ログ、操作用Inspector
- ランタイムブリッジをAIエージェントへ公開するMCPサーバー
- 共有ネイティブランタイム上で基本的なUI Tree収集とボタンクリックを示す、実験的なGodot 4.7 C#サンプル
- GDExtension経由で標準Control向けの全機能を提供する、推奨Godot 4.7 GDScriptアドオン
- UI Toolkit、uGUI、TextMeshPro向けUnity 6ランタイムパッケージと、Editor Play Mode・Windows x64 Mono Player用の外部テストホスト

エンジン固有機能はGuaの中心ではなく、プロトコル上に構築するアダプターとして扱います。現在はGodotとUnityに対応しており、追加エンジンも同じ境界上へ実装できます。

## ネイティブツールチェーン

Windowsのネイティブ開発ではMSVCを主要ツールチェーンとして使用します。

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug
```

移植可能な境界はC ABIです。将来macOS・iOSへ対応する場合はApple Clang、AndroidではAndroid NDK Clangを使用します。

## .NETテスト

通常は公開済みの`Gua.Testing`パッケージを参照します。このパッケージは対応するバージョンの`Gua.Core`へ依存しています。

```xml
<PackageReference Include="Gua.Testing" Version="0.5.0-preview.3" />
```

`Gua.Core`には`runtimes/win-x64/native/gua.dll`としてWindows x64用ネイティブランタイムが含まれます。通常の復元・ビルドによって、アプリまたはテストの出力先へコピーされます。

ローカルでパッケージを作る場合は、先にネイティブランタイムをビルドします。

```powershell
cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release --target gua
dotnet pack bindings/dotnet/src/Gua.Core/Gua.Core.csproj --configuration Release
dotnet pack bindings/dotnet/src/Gua.Testing/Gua.Testing.csproj --configuration Release
dotnet pack bindings/dotnet/src/Gua.Runtime/Gua.Runtime.csproj --configuration Release
dotnet pack bindings/dotnet/src/Gua.Testing.Unity/Gua.Testing.Unity.csproj --configuration Release
dotnet pack bindings/dotnet/src/Gua.Testing.Godot/Gua.Testing.Godot.csproj --configuration Release
dotnet pack bindings/dotnet/src/Gua.Testing.Visual/Gua.Testing.Visual.csproj --configuration Release
dotnet pack bindings/dotnet/src/Gua.Testing.Recording/Gua.Testing.Recording.csproj --configuration Release
```

NUnitサンプルは次のコマンドで実行できます。

```powershell
dotnet test examples/dotnet-nunit/GuaDotNetNUnitSample.csproj
```

### Unity 6 Windows x64

`Gua.Core`、`Gua.Testing`、`Gua.Testing.Visual`、`Gua.Testing.Recording`は
`net10.0`と`netstandard2.1`の両方を対象にします。
既定の**.NET Standard 2.1** API Compatibility Levelを使うUnity 6では、
native C ABIを変えずにmanaged assemblyを読み込めます。対応対象は
Windows Editor x64とWindows Standalone x64 Mono Playerです。managed assemblyとNuGet依存assemblyを
`Assets/Plugins/Gua/Managed`へ、`gua.dll`を`Assets/Plugins/x86_64`へ配置し、
UnityのPlugin Import SettingsでWindows EditorとWindows Standalone x86_64を
有効にします。

Unity Package Manager向けのビルド済み`.tgz`は各GitHub Releaseへ添付されます。
Unity Package Managerの**Add package from tarball**から導入できます。パッケージは
自動起動し、UI Toolkit、uGUI、TextMeshProのランタイムUIを収集します。
`Gua.Testing.Unity`はEditor Play ModeとMono Windows Playerの外部テストホストを
提供します。検証済みfixtureは[`examples/unity-smoke`](examples/unity-smoke/README.md)を
参照してください。IL2CPP、Windows以外、IMGUI、EditorWindow UIには未対応です。

## Inspector

InspectorはGuaプロトコルのスナップショットを表示するReactアプリケーションです。MCPには依存せず、`GuaInspectorClient`抽象化を通じてWebSocketブリッジやネイティブランタイムへ接続します。

ブラウザー版を起動します。

```powershell
bun run --filter @gua/inspector dev
```

別のターミナルでサンプルWebSocketブリッジを起動します。

```powershell
bun run bridge:ws
```

Inspectorの接続先は次のとおりです。

```text
ws://127.0.0.1:8765
```

InspectorのAutomationパネルでは、画面から実行したSemantic操作の記録、
`recording.schema.json`の読み込み・ダウンロード、全Semantic操作のReplayを
行えます。秘密値はメモリ上のJSON mapからだけ解決します。Visual comparisonでは、
現在のスクリーンショットまたは選択した画像をbaselineにしてブラウザー内で比較し、
Actual・Expected・Diff画像とmanifestをダウンロードできます。ブラウザー版Inspectorが
任意のローカルパスへ暗黙に書き込むことはありません。
座標fallbackを含むRecordingもschema v1として読み込めますが、Inspectorは実行せず、
Replayは既定でSemantic targetだけを使用します。

静的InspectorのビルドとTauriデスクトップシェルの開発起動には、次のコマンドを使用します。TauriにはRustツールチェーンも必要です。

```powershell
bun run --filter @gua/inspector build
bun run --filter @gua/inspector tauri:dev
```

## MCP

MCPサーバーは、Inspectorと同じブリッジを利用する薄いプロトコルクライアントです。ランタイムブリッジを起動してから、stdioサーバーを実行します。

```powershell
bun run bridge:ws
bun run mcp
```

npmで公開されている`gui-mcp`は、MCPクライアントから次のように起動できます。

```powershell
bunx gui-mcp@latest mcp
```

既定では`ws://127.0.0.1:8765`へ接続します。別のランタイムアダプターへ接続する
場合は、`GUA_BRIDGE_URL`を指定します。

```powershell
$env:GUA_BRIDGE_URL = "ws://127.0.0.1:8765"
bunx gui-mcp@latest mcp
```

提供するMCPツールは次のとおりです。

```text
get_ui_tree
get_world_object_tree
find_world_objects
wait_for_world_object
click_node
focus_node
set_value
set_checked
select
scroll
press_key
wait_for_node
get_screenshot
get_logs
start_recording
stop_recording
save_recording
replay_recording
compare_screenshot
get_visual_artifacts
run_test
```

Recording、baseline、Visual失敗artifactは既定で`.gua`へ保存します。
`GUA_ARTIFACT_DIR`で保存rootを変更できますが、MCPツールへ渡した名前からroot外へは
書き出せません。接続先bridgeが対応している場合、Semantic action toolは
request IDに対応するhost完了eventまで待機します。

World Object Treeのtoolはread-onlyであり、公開profileはhost側で固定されます。
player向けMCPではhost processへ`GUA_OBSERVATION_PROFILE=player`を設定してください。
tool引数からdebugへ昇格することはできず、world action toolも提供しません。

## Godot 4.7 C#サンプル

> **実験的機能 — 基本機能のみ。** このサンプルは、共有ランタイムブリッジ、
> 基本的なSemantic UI Tree収集、スクリーンショット、ボタンクリックを示すものです。
> GDScriptアダプターと機能的に等価ではありません。新しいGodot統合では、
> .NET対応プロジェクトを含め、後述のGDScriptアドオンを使用してください。
> Godotでは、C#のゲームスクリプトとGDScriptアドオンを併用できます。

`examples/dotnet-godot`には、`Godot.NET.Sdk/4.7.0`と`net10.0`を使用する
最小サンプルがあります。

```powershell
dotnet build examples/dotnet-godot/GuaGodotSample.csproj -v:minimal
```

サンプルは`GuaGodotRuntime`をルート`Control`へ取り付け、標準コントロールをSemantic UI Treeへ自動収集します。また、ゲームプロセス内でInspectorブリッジを起動し、外部のクリック要求をGodotの通常のボタンシグナルとして処理します。

Godotシーンの外部テストには`Gua.Testing.Godot`を使用できます。

```csharp
using var host = GodotSceneTestHost.Load("game/scenes/title_screen.tscn");

GuaAssertions.GetByRole(host.Context, "button", "開始").ToBeVisible();
host.Click("CenterPanel/Content/ButtonBox/StartButton",
    nextScene: "game/scenes/village_list.tscn");
GuaAssertions.GetByRole(host.Context, "button", "Create").ToBeVisible();
```

## Godot 4.7 GDScriptアドオン

これはGDScriptプロジェクトと.NET対応プロジェクトの両方に推奨するGodot統合です。
標準Control向けアダプターは、現在Guaが文書化しているGodot機能一式を実装しています。

`native/gua-godot`は、共有`native/gua-runtime`上に構築された薄いGDExtension
アダプターです。GDScript側でランタイムコアを再実装しません。

Windowsデバッグ版GDExtensionをビルドします。

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug --target gua-godot
```

ゲームスクリプトでは、自動収集アダプターを明示的にプリロードします。

```gdscript
const GuaAutoAdapterScript := preload("res://addons/gua/gua_auto_adapter.gd")

var ui := GuaAutoAdapterScript.new()
```

アダプターはルート`Control`から標準コントロールを収集し、ボタンシグナルを監視して、Inspectorからのクリック要求を通常のGodot入力経路へ送ります。

## リリース自動化

手動で作成した`gua-vX.Y.Z`タグをpushすると、リリースワークフローが実行されます。
Inspector・Godotプラグイン・ImGuiプラグインはまとめてビルドされ、対応する
GitHub Releaseへ添付されます。MCPと.NETパッケージも同じバージョンでnpm・
NuGetへ公開されます。`main`への変更だけではパッケージは公開されません。

## リポジトリ構成

```text
protocol/             プロトコル仕様とJSON Schema
native/gua-core/      C ABIランタイムコアとC++参照実装
native/gua-runtime/   Godot C#・GDScript用共有ネイティブランタイムブリッジ
native/gua-imgui/     ImGuiアダプター
native/gua-testing/   C ABI上のC++テストヘルパー
native/gua-godot/     GDScript用Godot GDExtensionアダプター
bindings/dotnet/      .NET P/InvokeバインディングとC#テストヘルパー
packages/mcp/         公開MCPサーバーパッケージ
packages/inspector/   ブラウザー・TauriデスクトップInspector
examples/             Godotサンプルを含む最小デモとサンプル
docs/                 ネイティブツールチェーン資料
```

## ライセンス

MIT
