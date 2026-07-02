# Gua 企画書

## Game UI Automation / Runtime UI Automation Protocol for Games

## 1. 概要

**Gua** は、ゲーム実行中の UI を外部から観測・操作・検証するための、エンジン非依存の Runtime UI Automation Protocol である。

Web における Playwright が DOM を通じて UI を操作・検証するように、Gua はゲームランタイム内の UI を **Semantic UI Tree** として外部に公開し、テストランナー、Inspector、MCP サーバー、AI エージェントなどから操作できるようにする。

Gua はゲームエンジンでも、Editor MCP でも、画像認識 QA Bot でもない。
主目的は、**ゲーム実行中の UI を意味情報付きで取得し、外部ツールから自動テスト・操作・検証できるようにすること**である。

## 2. コンセプト

### 2.1 タグライン

> Runtime UI automation for games.

または、

> A semantic UI automation protocol for testing and interacting with in-game UI.

### 2.2 日本語説明

Gua は、ゲーム実行中の UI を外部から観測・操作・検証するための Runtime UI Automation Protocol である。
既存のゲーム UI を捨てることなく、ボタン、テキスト、スライダー、フォーカス状態、表示状態、矩形情報などを意味ツリーとして公開し、Playwright 風のテストや AI エージェントによる操作を可能にする。

## 3. 背景と課題

Web UI では Playwright や Testing Library により、UI 要素を意味的に取得して操作・検証できる。

例:

```ts
await page.getByText("Start").click()
await expect(page.getByText("Loading")).toBeVisible()
```

一方で、ゲーム UI は一般的に以下の問題を持つ。

* UI が Canvas / Texture / RenderTarget に描画され、DOM のような意味ツリーがない
* ボタンやラベルを名前・役割で取得できない
* UI テストがスクリーンショット比較や座標クリックに寄りがち
* キーボード・ゲームパッド・マウス入力が絡み、E2E テストが難しい
* CI 上で UI の回帰テストを組みにくい
* AI エージェントがゲーム UI の状態を理解しにくい
* Unity / UE / Godot / Cocos / MonoGame / 自作エンジンごとに事情が異なる

Gua はこの問題に対して、描画そのものではなく **UI の意味情報と操作プロトコル** を提供する。

## 4. 目指すもの

Gua の中核は以下である。

```text
Game Runtime
  ↓
Semantic UI Tree
  ↓
Automation Bridge
  ↓
Test Runner / Inspector / MCP / AI Agent
```

ゲーム側は、現在の UI 状態を外部に公開する。

例:

```json
{
  "screen": "title",
  "nodes": [
    {
      "id": "start",
      "role": "button",
      "label": "Start Game",
      "visible": true,
      "enabled": true,
      "focused": false,
      "bounds": { "x": 640, "y": 420, "w": 280, "h": 64 },
      "actions": ["click", "focus"]
    }
  ]
}
```

外部のテストクライアントは、これを Playwright 風に操作する。

```ts
await game.getByRole("button", { name: "Start Game" }).click()
await expect(game.getById("loading")).toBeVisible()
```

## 5. Gua が「やること」

Gua は以下を提供する。

* ゲーム UI の Semantic UI Tree 仕様
* UI ノード登録 API
* UI 状態取得 API
* 外部からの操作コマンド
* C++ / C# から使えるテスト支援 API
* MCP サーバー
* Inspector
* C / C++ Runtime SDK
* ImGui adapter
* .NET Binding
* 将来的なアニメーションライブラリ

## 6. Gua が「やらないこと」

初期段階では、以下は明確に非目標とする。

* ゲームエンジンそのものを作る
* Unity / UE / Godot の Editor 操作 MCP を作る
* 画像認識だけで UI 操作する QA Bot を作る
* 既存ゲーム UI を強制的に置き換える
* 初手から完全な XML UI フレームワークを作る
* 初手から GSAP 相当の高機能アニメーションエンジンを作る
* 初手から全エンジン対応を目指す

Gua の初期価値は、**既存 UI を捨てずにテスト可能にすること**である。

## 7. 差別化方針

既存の Unity MCP、Godot MCP、UE 系 AI 開発支援、Cocos Creator MCP などは、多くの場合 Editor 操作や開発支援が主眼である。

Gua は Editor ではなく **Runtime** を見る。

比較:

```text
Editor MCP:
  - シーンを作る
  - アセットを操作する
  - コンポーネントを追加する
  - Editor 内作業を AI に任せる

Gua:
  - 実行中の UI を読む
  - ボタンを押す
  - フォーカス状態を見る
  - メニュー遷移を検証する
  - UI テストを実行する
  - AI に UI 状態を理解させる
```

つまり、Gua は既存 MCP と競合するより、横に置ける道具である。

## 8. 技術方針

### 8.1 本当のコアは C++ ではなく Protocol

Gua の中心は特定言語実装ではなく、言語非依存の Protocol とする。

```text
Core Protocol:
  - JSON Schema
  - UI Tree Schema
  - Command Schema
  - Event Schema
  - Transport Spec
```

C++ は最初の強い参照実装であり、最終的な本体ではない。

### 8.2 Runtime Core は C ABI を公開する

C++ ABI を外部公開すると、コンパイラ・標準ライブラリ・ビルド設定差分により破綻しやすい。
そのため、外部に公開する安定境界は C ABI とする。

例:

```c
typedef struct gua_context_t gua_context_t;

gua_context_t* gua_create_context(void);
void gua_destroy_context(gua_context_t* ctx);

void gua_begin_frame(gua_context_t* ctx);
void gua_end_frame(gua_context_t* ctx);

void gua_register_button(
    gua_context_t* ctx,
    const char* id,
    const char* label,
    float x,
    float y,
    float w,
    float h,
    int visible,
    int enabled
);
```

C++ API はこの C ABI の上に薄く乗せる。

例:

```cpp
gua::begin_frame();

if (gua::button("start", "Start Game", {100, 200, 240, 64})) {
    start_game();
}

gua::end_frame();
```

### 8.3 .NET は P/Invoke Binding とする

C# Runtime Adapter を独立実装するのではなく、C ABI に対する P/Invoke Binding として提供する。

これにより、C++ 実装と C# 実装の二重管理を避ける。

構成:

```text
gameui-core
  C ABI
  C++ wrapper
  ImGui adapter

gameui-dotnet
  P/Invoke binding
  C# ergonomic wrapper
  MonoGame / Stride / Unity examples
```

C# 側の例:

```csharp
internal static partial class Native
{
    [LibraryImport("gua")]
    internal static partial nint gua_create_context();

    [LibraryImport("gua")]
    internal static partial void gua_begin_frame(nint ctx);

    [LibraryImport("gua", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gua_register_button(
        nint ctx,
        string id,
        string label,
        float x,
        float y,
        float w,
        float h,
        int visible,
        int enabled
    );
}
```

ユーザー向け wrapper:

```csharp
ui.BeginFrame();

ui.Button(
    id: "start",
    label: "Start Game",
    bounds: new Rect(100, 200, 240, 64),
    visible: true,
    enabled: true
);

ui.EndFrame();
```

### 8.4 P/Invoke の注意点

毎フレーム・毎ノードごとに大量の P/Invoke 呼び出しを行うと、負荷が問題になる可能性がある。

初期実装では単純 API でよいが、将来的にはバッチ送信を検討する。

例:

```c
void gua_submit_nodes(
    gua_context_t* ctx,
    const gua_node_t* nodes,
    int node_count
);
```

C# 側では `Span<T>` や `unsafe` による一括送信を検討する。

また、C++ 側へ C# delegate callback を渡す設計は初期段階では避ける。
delegate 寿命、GC、保持管理が面倒になるためである。

代わりに、イベントキュー方式を採用する。

```text
C# → UI ノード登録
外部テスト → click("start")
C++ core → event queue に click event を積む
C# → PollEvents() で取得して処理
```

例:

```c
int gua_poll_event(gua_context_t* ctx, gua_event_t* out_event);
```

C#:

```csharp
while (ui.TryPollEvent(out var e))
{
    if (e.Type == GuaEventType.Click && e.Id == "start")
    {
        StartGame();
    }
}
```

### 8.5 Native toolchain 方針

Windows の C++ 開発は MSVC を主ターゲットとする。
ただし、Gua の安定境界は C ABI であり、共有 native core は標準 C++20 の範囲に保つ。

想定 toolchain:

* Windows: MSVC
* macOS / iOS: Apple Clang
* Android: Android NDK Clang
* Linux: 後回し。対応する場合は Clang または GCC

MSVC 専用拡張、Windows API、platform 固有処理を protocol-level code に混ぜない。
必要になった場合は platform-specific な native directory に隔離する。

## 9. 初期ターゲット

### 9.1 最初に避けるもの

初手から以下に深く入らない。

* Unity 専用 plugin
* Unreal Engine 専用 plugin
* Godot 専用 plugin
* 大規模 XML UI フレームワーク
* 完全なアニメーションエンジン

理由:

* Unity / Godot / UE 周辺は既に AI / MCP 系の動きがある
* 最初から特定エンジンに寄ると、Gua のプロトコル思想がエンジン作法に引きずられる
* 大規模エンジン対応は実装・検証・サポートコストが重い

### 9.2 最初に狙うもの

初期実装では、以下を優先する。

1. Protocol Specification
2. C ABI Runtime Core
3. C++ ergonomic wrapper
4. ImGui wrapper adapter
5. C++ testing helpers
6. .NET P/Invoke Binding
7. C# testing helpers
8. MCP Server
9. Inspector
10. MonoGame sample

### 9.3 ImGui adapter

ImGui は即時モード UI であり、Gua の参照実装として相性がよい。

初期は既存 ImGui API を内部から観測するのではなく、Gua 用ラッパーを提供する。

例:

```cpp
if (GuaImGui::Button("start", "Start Game")) {
    start_game();
}
```

既存 `ImGui::Button()` を自動的に観測する方式は理想だが、ImGui 内部に強く依存するため初期段階では避ける。

## 10. プロジェクト構成

初期段階では monorepo を採用する。

理由:

* Protocol 変更が C++ / C# / MCP / Inspector にまたがる
* 複数 repository に分けると、変更の同期が面倒
* OSS 初期は clone しやすさ、PR しやすさが重要
* git submodule 地獄を避けるため

推奨構成:

```text
gua/
  README.md
  LICENSE
  docs/

  protocol/
    schema/
      ui-tree.schema.json
      commands.schema.json
      events.schema.json
    specs/
      protocol.md

  native/
    gua-core/
      include/
        gua/gua.h
      src/
      CMakeLists.txt

    gua-imgui/
      include/
      src/
      CMakeLists.txt

    gua-testing/
      include/
      CMakeLists.txt

  bindings/
    dotnet/
      src/
        Gua.Core/
        Gua.Testing/
      samples/
        MonoGameSample/

  packages/
    mcp/
      package.json
      src/

    inspector/
      package.json
      src/

  examples/
    cpp-imgui/
    dotnet-monogame/
    minimal/
```

### 10.1 git submodule 方針

Gua 本体の各プロジェクトは submodule にしない。

submodule を使う場合は、外部依存のみとする。

例:

```text
third_party/
  imgui/
```

ただし、外部依存も初期は CMake FetchContent などで済ませられるなら、それでもよい。

## 11. API イメージ

### 11.1 UI ノード

基本ノード:

```json
{
  "id": "start",
  "role": "button",
  "label": "Start Game",
  "visible": true,
  "enabled": true,
  "bounds": {
    "x": 100,
    "y": 200,
    "w": 240,
    "h": 64
  },
  "state": {
    "focused": false,
    "hovered": false,
    "pressed": false
  },
  "actions": ["click", "focus"]
}
```

### 11.2 Role

初期 role 候補:

* button
* text
* image
* checkbox
* radio
* slider
* textbox
* list
* listitem
* panel
* screen
* dialog
* menu
* menuitem

### 11.3 Command

初期 command 候補:

* get_ui_tree
* get_node
* click_node
* focus_node
* press_key
* text_input
* move_gamepad
* wait_for_node
* get_screenshot
* get_logs
* poll_events

### 11.4 C++ / C# Testing API

例:

```cpp
gua::testing::expect_node(ctx, "start").to_be_visible();
gua::testing::expect_node(ctx, "start").click();
gua::testing::expect_node(ctx, "loading").to_be_visible();
```

```csharp
GuaAssertions.ExpectNode(ui, "start").ToBeVisible();
GuaAssertions.ExpectNode(ui, "start").Click();
GuaAssertions.ExpectNode(ui, "loading").ToBeVisible();
```

## 12. MCP Server

Gua は将来的に MCP Server を提供する。

AI エージェントは MCP を通じて以下を行える。

* 現在の UI tree を読む
* ボタンを押す
* キーボード入力を送る
* ゲームパッド入力を送る
* スクリーンショットを見る
* ログを見る
* UI テストを生成する
* 失敗原因を要約する

MCP tool 例:

```text
get_ui_tree
click_node
press_key
wait_for_node
get_screenshot
get_logs
run_test
```

重要なのは、AI を主役にしすぎないこと。
Gua の主価値は Runtime UI Automation であり、AI は利用者の一種として扱う。

## 13. Inspector

Inspector は、実行中のゲームから取得した UI tree を表示する Web UI / Desktop UI である。

初期機能:

* UI tree 表示
* 選択ノードの詳細表示
* bounds 表示
* visible / enabled / focused 状態表示
* node id / role / label 確認
* click / focus 送信
* screenshot 表示
* logs 表示

将来的な機能:

* UI tree diff
* visual regression report
* event timeline
* focus navigation debug
* animation debug

## 14. アニメーション構想

Gua の将来的な収益化候補として、GSAP 的なコードベースの UI アニメーションライブラリを検討する。

初期段階では非目標だが、Gua の UI 基盤が普及した後に以下を提供する。

例:

```ts
ui.timeline()
  .to("#panel", { x: 0, opacity: 1, duration: 0.25, ease: "outCubic" })
  .from("#start", { y: 24, opacity: 0, duration: 0.2 }, "-=0.1")
  .to("#shine", { rotation: 360, duration: 1.0, repeat: -1 })
```

想定機能:

* timeline
* easing
* sequence
* delay
* repeat
* yoyo
* interrupt / cancel
* state transition
* focus animation
* popup animation
* toast animation
* number count-up
* gauge animation
* reward animation
* card flip

収益化候補:

* 高度な timeline animation
* animation presets
* visual animation editor
* profiler
* commercial license

ただし順番としては、まず Runtime UI Automation を成立させる。
その後に「この UI 基盤はテストだけでなく、UI 実装にも便利である」という流れで animation を提供する。

## 15. 収益化方針

### 15.1 初手

初手は OSS + GitHub Sponsors とする。

ただし、GitHub Sponsors は生活費を稼ぐ主砲ではなく、信用の受け皿として置く。

初期 tier 例:

```text
500円/月    Supporter
1,500円/月  Runtime supporter
5,000円/月  Early adopter
15,000円/月 Studio sponsor
```

見返りは重くしない。

候補:

* README に名前掲載
* 開発ログ先行公開
* ロードマップ投票
* Sponsor 向け Discussion
* 月1の開発報告

避けるべきもの:

* 無制限サポート
* 個別実装対応
* 確約付きの機能開発
* 重い Discord 運営

### 15.2 将来的な収益化

候補:

1. 商用ライセンス
2. Pro animation / timeline library
3. Visual animation editor
4. CI visual report
5. Studio support
6. Unity / Godot / UE / MonoGame adapters
7. Pro Inspector
8. Team dashboard

特に有望なのは以下。

* CI visual regression report
* Pro Inspector
* GSAP 的 animation timeline
* 商用利用向けライセンス

## 16. ライセンス方針

初期段階では OSS として公開する。

候補:

* MIT
* Apache-2.0
* MPL-2.0
* Dual license

検討方針:

* 普及優先なら MIT / Apache-2.0
* 商用ライセンス展開を考えるなら、Dual license も検討
* コアは permissive、Pro 機能は proprietary という構成もあり

初期は利用障壁を下げることを優先する。

## 17. ロードマップ

### v0.1: Protocol + C++ Core + ImGui Demo

目的: Gua の最小価値を示す。

実装:

* UI tree schema
* command schema
* C ABI core
* C++ wrapper
* ImGui wrapper
* minimal example
* UI tree dump
* click event queue

成果物:

* `native/gua-core`
* `native/gua-imgui`
* `examples/cpp-imgui`
* `protocol/schema`

### v0.2: C++ / C# Testing Helpers

目的: C++ と C# のゲームコードから直接使えるテスト体験を示す。

実装:

* getById
* getByRole
* getByText
* click
* waitFor
* C++ assertions
* C# assertions

成果物:

* `native/gua-testing`
* `bindings/dotnet/src/Gua.Testing`

### v0.3: Inspector + Logs + Screenshot

目的: 視覚的なデバッグ体験を作る。

実装:

* Web UI Inspector
* UI tree viewer
* node detail viewer
* screenshot viewer
* log viewer

成果物:

* `packages/inspector`

### v0.4: MCP Server

目的: AI エージェントから実行中 UI を操作できるようにする。

実装:

* get_ui_tree
* click_node
* press_key
* wait_for_node
* get_screenshot
* get_logs
* run_test

成果物:

* `packages/mcp`

### v0.5: .NET Binding + Godot Sample

目的: C# 系ゲーム環境へ展開する。

実装:

* P/Invoke binding
* ergonomic C# wrapper
* event polling
* shared native runtime bridge for Godot C# and GDScript
* Godot 4.7 C# sample

成果物:

* `bindings/dotnet`
* `native/gua-runtime`
* `examples/dotnet-godot`
* `examples/dotnet-monogame` remains a future placeholder

### v0.5.1: Godot GDScript Runtime Addon

目的: Godot 4.7 の GDScript から Gua runtime automation protocol を利用できるようにする。

実装:

* Godot 4.7 GDExtension adapter
* shared native runtime bridge reuse
* `GuaContext : RefCounted`
* GDScript 向け `begin_frame`, `register_node`, `end_frame`
* `get_ui_tree_json`, `enqueue_click`, `poll_event`
* `addons/gua` packaging
* GDScript sample

成果物:

* `native/gua-godot`
* `examples/godot-gdscript`

### v0.6: Animation Primitives

目的: 将来的な Pro animation library の土台を作る。

実装:

* basic tween
* easing
* timeline prototype
* animation state exposure to Inspector

### v1.0 候補

* Protocol 安定化
* C ABI 安定化
* Test runner 安定化
* Inspector 実用化
* CI integration
* documentation
* examples
* sponsor page
* contribution guide

## 18. 初期デモ案

最初のデモは、タイトル画面だけでよい。

内容:

1. C++ / ImGui で簡単なゲーム風 UI を表示
2. Gua が UI tree を外部公開
3. C++ または C# のテスト helper が Start ボタンを取得
4. Start ボタンを click
5. Loading 表示を検証
6. Inspector で UI tree と screenshot を表示
7. MCP 経由で AI に UI 状態を説明させる

テスト例:

```cpp
gua::testing::expect_node(ctx, "start").to_be_visible();
gua::testing::expect_node(ctx, "start").click();
gua::testing::expect_node(ctx, "loading").to_be_visible();
```

README に載せるデモ文:

```text
Gua lets you test in-game UI like web UI.

Query buttons, labels, focus states, and screen transitions from your running game.
No image recognition. No fragile coordinate clicks.
Expose a semantic UI tree and automate it from C++, C#, CI, or AI agents.
```

## 19. リスク

### 19.1 汎用化しすぎるリスク

初手から Unity / UE / Godot / Cocos / MonoGame / Stride / 自作エンジン全対応を目指すと破綻する。

対策:

* まず Protocol と C++ / ImGui 参照実装に集中する
* エンジン対応は後から薄い adapter として追加する

### 19.2 UI ライブラリ本体に寄りすぎるリスク

描画・レイアウト・フォント・IME・アニメーションまで抱えると、巨大 UI フレームワーク化して死ぬ。

対策:

* 初期は「意味ツリー」と「操作」に集中する
* 描画は既存 UI に任せる
* XML UI は後回し

### 19.3 AI ブーム依存リスク

AI Agent / MCP を前面に出しすぎると、流行の変化に弱くなる。

対策:

* 主価値は Runtime UI Automation とする
* AI は利用者の一つとして扱う

### 19.4 P/Invoke 性能リスク

C# から毎フレーム大量に P/Invoke すると負荷が出る可能性がある。

対策:

* 初期は単純 API
* 将来的に batch submit
* イベントは callback ではなく polling

### 19.5 OSS 収益化リスク

GitHub Sponsors だけで十分な収入を得るのは難しい。

対策:

* Sponsors は信用の受け皿とする
* 将来的に Pro Inspector / CI report / animation library / commercial license を検討する

## 20. 初期実装タスク案

### Phase 1: Repository setup

* monorepo 作成
* README 初稿
* LICENSE 選定
* docs/planning/gua-project-plan.md 作成
* protocol ディレクトリ作成
* native/gua-core 作成
* native/gua-testing 作成
* bindings/dotnet/src/Gua.Testing 作成

### Phase 2: Protocol draft

* UI node schema 作成
* command schema 作成
* event schema 作成
* role 一覧定義
* bounds / state / actions 定義
* transport 仕様メモ作成

### Phase 3: C ABI core

* `gua_context_t`
* `gua_create_context`
* `gua_destroy_context`
* `gua_begin_frame`
* `gua_end_frame`
* `gua_register_node`
* `gua_get_ui_tree_json`
* `gua_poll_event`

### Phase 4: C++ wrapper

* `gua::Context`
* `gua::begin_frame`
* `gua::button`
* `gua::text`
* `gua::panel`
* event polling wrapper

### Phase 5: ImGui sample

* minimal ImGui window
* `GuaImGui::Button`
* UI tree export
* click event handling
* demo screen transition

### Phase 6: C++ / C# testing helpers

* getById
* getByText
* getByRole
* click
* waitFor
* C++ assertion helper
* C# assertion helper

### Phase 7: Documentation

* What is Gua?
* What Gua is not
* Quick start
* C++ example
* ImGui example
* Protocol overview
* Roadmap
* Sponsor page

## 21. README 冒頭案

````md
# Gua

**Gua** is a runtime UI automation protocol for games.

It exposes a semantic UI tree from your running game, so test runners and AI agents can inspect, query, click, and verify in-game UI without relying on fragile image recognition or coordinate-based input.

```cpp
gua::testing::expect_node(ctx, "start").click();
gua::testing::expect_node(ctx, "loading").to_be_visible();
````

Gua is not a game engine.
Gua is not an editor MCP.
Gua is not an image-recognition QA bot.

Gua is a small bridge between your game runtime and automation tools.

```

## 22. まとめ

Gua は、ゲーム UI を「描画された絵」ではなく「意味を持った操作可能なツリー」として外部に公開するための基盤である。

初期価値は以下に集約される。

> ゲーム実行中の UI を、Web UI のように自動テストできる。

長期的には、以下へ拡張する。

- Inspector
- MCP
- AI test generation
- CI visual report
- .NET / Unity / Godot / MonoGame adapters
- GSAP 的 animation library
- Pro tooling
- commercial license

初期実装では、Protocol、C ABI core、C++ wrapper、ImGui adapter、C++ / C# testing helpers に集中する。  
C# 対応は C ABI への P/Invoke Binding として提供し、テスト支援も C ABI の上に薄く乗せる。

最初から巨大な UI フレームワークを作るのではなく、まずは Runtime UI Automation の小さく強い核を作る。
```
