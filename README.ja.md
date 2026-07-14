# Gua

[English](README.md) | 日本語

[![License](https://img.shields.io/github/license/link1345/gua)](https://github.com/link1345/gua/blob/main/LICENSE)
[![Discord](https://img.shields.io/discord/1329272750099136552)](https://discord.gg/Zy65k8AxH2)

> この日本語版は補助ドキュメントです。内容に差異がある場合は、
> [英語版README](README.md)を正しい最新情報として扱ってください。

**Gua**は、ゲーム向けのランタイムUI自動化プロトコルです。

実行中のゲームUIを意味情報付きのツリーとして公開し、壊れやすい画像認識や座標指定に頼らず、テストランナーやAIエージェントからUIを調査、検索、操作、検証できるようにします。

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

## MCPとInspector

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

現在の実装では、安定境界となるC ABIの上にC++とC#のAPIを提供し、InspectorとMCPをプロトコルの利用者として接続しています。Godot 4.7向けのアダプターとサンプルも含まれます。

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

* Guaはゲームエンジンではありません。
* Guaはエディター操作用MCPではありません。
* Guaは画像認識QAボットではありません。

Guaは、ゲームランタイムと自動化ツールをつなぐ小さな橋です。

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

Unity、Unreal Engine、Godot、MonoGameなどのエンジン固有機能は、Guaの中心ではなく、プロトコル上に構築するアダプターとして扱います。

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
```

NUnitサンプルは次のコマンドで実行できます。

```powershell
dotnet test examples/dotnet-nunit/GuaDotNetNUnitSample.csproj
```

### Unity 6 Windows Editor

`Gua.Core`と`Gua.Testing`は`net10.0`と`netstandard2.1`の両方を対象にします。
既定の**.NET Standard 2.1** API Compatibility Levelを使うUnity 6では、
native C ABIを変えずにmanaged assemblyを読み込めます。最初の検証対象は
Windows Editor x64です。managed assemblyとNuGet依存assemblyを
`Assets/Plugins/Gua/Managed`へ、`gua.dll`を`Assets/Plugins/x86_64`へ配置し、
UnityのPlugin Import SettingsでWindows EditorとWindows Standalone x86_64を
有効にします。

最小`MonoBehaviour`、具体的なビルド・配置手順、Unityなしでも同じ
`netstandard2.1` assemblyとnative呼び出しを検証できるsmoke hostは
[`examples/unity-smoke`](examples/unity-smoke/README.md)を参照してください。
IL2CPP/AOTとWindows以外のnative targetは個別検証が必要です。

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
click_node
press_key
wait_for_node
get_screenshot
get_logs
run_test
```

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

関連するプロトコル利用コンポーネントが`main`で変更されると、GitHub Actionsが
成果物を公開します。Inspector・Godotプラグイン・ImGuiプラグインはまとめて
ビルドされ、同じバージョンの`gua-v*` GitHub Releaseへ添付されます。MCPと
.NETパッケージは、従来どおり独立したnpm・NuGet公開ワークフローを使用します。

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
