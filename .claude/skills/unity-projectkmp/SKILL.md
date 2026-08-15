---
name: unity-projectkmp
description: Unityプロジェクト「ProjectKMP」(Photon PUN2オンラインマルチ・オープンキャンパス教材)のコンテンツ作成ルール&C#コーディング規約。Unity MCP(Unity_RunCommand・Unity_AssetGeneration等)でスクリプト作成・アセット生成・プレハブ作成・シーン操作・フォルダ作成など、Unityプロジェクトに何かを追加・変更するときは、「ルール」と明示されていなくても必ず参照する。ファイルの作成場所・命名・Photon運用・設計(MVP/プール)の規則を含む。
---

# ProjectKMP コンテンツ作成 & コーディングルール

ProjectKMP(Unity / Photon PUN2 / UniTask / R3)での作業規約。Unity MCP経由の操作はすべてこのルールに従う。
**絶対ルール(編集禁止フォルダ・Photon AppId・依存の向きなど)はリポジトリ直下の `CLAUDE.md` にある。必ず併読する。**

---

## 0. 最重要ルール: 作成場所

**何かを作成する前に、必ず §1 の作成場所マップで置き場所を確定させる。**

- マップで場所が一意に決まらない場合(該当する種類がない、機能名が判断できない、新しい機能フォルダを切るべきか迷う場合など)は、**勝手に場所を決めて作成せず、ユーザーに確認する**。「〜を作成します。場所は `Assets/Contents/...` でよいですか?」と候補を添えて質問する。
- 既存フォルダ構成と矛盾する指示を受けた場合も、作成前に一度確認する。
- 実験・試作・動作確認用の一時的なものは `Assets/_Sandbox/<担当者名>/` に作る(存在しなければユーザーに担当者名を確認して作成)。

## 1. 作成場所マップ

構成は「種類→機能」。種類フォルダ直下への直置きは禁止で、必ず機能名フォルダを1階層挟む。

| 作成するもの | 作成場所 | 例 |
|---|---|---|
| C#スクリプト(機能) | `Assets/Contents/Scripts/<機能名>/` | `Scripts/Monster/MonsterAI.cs` |
| C#スクリプト(共通基盤) | `Assets/Contents/Scripts/Core/` | GameInput, PlayerStatus, GameObjectPool |
| 演出・音の部品 | `Assets/Contents/Scripts/Presentation/` | HitStop, ScreenFlash, BgmPlayer |
| UI の Presenter | `Assets/Contents/Scripts/UI/Presenters/` | `SkillButtonPresenter.cs` |
| EditModeテスト | `Assets/Tests/EditMode/` | `BossSegmentsTests.cs` |
| プレハブ | `Assets/Contents/Prefabs/<機能名>/` | `Prefabs/Monster/PF_Monster_Slime.prefab` |
| **ネットワーク生成プレハブ** | `Assets/Resources/NetworkPrefabs/` | `PhotonNetwork.Instantiate` 対象は**ここ必須** |
| モデル・テクスチャ・マテリアル | `Assets/Contents/Art/<機能名>/` | `Art/Monster/TEX_Monster_Slime_Albedo.png` |
| AnimationClip / AnimatorController | `Assets/Contents/Animations/<機能名>/` | `Animations/Monster/AC_Slime_Attack.anim` |
| UIスプライト | `Assets/Contents/UI/Sprites/` | |
| SE / BGM | `Assets/Contents/Audio/SE/` `Audio/BGM/` | |
| ScriptableObject | `Assets/Contents/Scripts/<機能名>/`(定義) + データは機能のフォルダ | |
| シーン(本番) | `Assets/Build/Scenes/` | **新規作成・編集はユーザー確認必須(§5)** |
| シーン(作業用) | `Assets/_Sandbox/<担当者名>/` | |
| プロジェクト設定類 | `Assets/Settings/` | |

- 機能名フォルダは種類をまたいで**同じ名前**を使う(`Scripts/Monster` と `Prefabs/Monsters` の揺れ禁止)
- **フォルダ名と名前空間は一致させる**(`Scripts/Core/` なら `namespace ProjectKMP.Core`)
- 機能名フォルダは中身ができるタイミングで作る。空フォルダの量産禁止
- 現在の機能名: `Attack` `Battle` `Core` `Dog` `Field` `GameFlow` `Gorilla` `Monster` `Network` `Player` `Presentation` `Sync` `Turtle` `UI` `Utility`。**新しい機能名を切る場合はユーザーに確認**

### 編集禁止フォルダ

`Assets/Photon/` `Assets/TextMesh Pro/` `Assets/Packages/` などサードパーティ製は**読み取りのみ・編集禁止**(詳細は CLAUDE.md)。

## 2. アセット命名

形式: `接頭辞_機能名_名前(_詳細)`。日本語ファイル名・スペース禁止。

| 種類 | 接頭辞 | 例 |
|---|---|---|
| Prefab | `PF_` | `PF_Monster_Slime` |
| Material | `MAT_` | `MAT_Monster_Slime` |
| Texture | `TEX_` | `TEX_Monster_Slime_Albedo` / `_Normal` / `_Emission` |
| AnimationClip | `AC_` | `AC_Slime_Attack` |
| AnimatorController | `ACtrl_` | `ACtrl_Slime` |
| ScriptableObject | `SO_` | `SO_MonsterParams_Slime` |
| SE / BGM | `SE_` / `BGM_` | `SE_Slime_Hit`, `BGM_Battle` |
| シーン | 接頭辞なし | `Title` `Lobby` `Battle` `Result` |

## 3. C#コーディング規約

※ ユーザーのC++規約(cpp-gamedev)とは別物。Unity C#では以下に従う。

- クラス/メソッド/プロパティ: `PascalCase`、インターフェースは `I` 接頭辞(`ISyncableData`)
- privateフィールド: `_camelCase`(**前置**アンダースコア。C++の後置 `_` と混同しない)
- 定数: `UPPER_SNAKE_CASE`(`MAX_PLAYERS`)
- asyncメソッド: `Async` サフィックス + `CancellationToken` を受け取る。`async void` 禁止(`UniTask` / `UniTaskVoid` を使う)
- publicクラス・publicメソッドに日本語 `<summary>` コメント必須。実装内コメントは「なぜ」を書く
- ログ: `Debug.Log($"[機能名] メッセージ")` 形式
- namespace: 新規ファイルは `namespace ProjectKMP.<機能名>`(フォルダ名と一致させる)
- セクション区切り: `// ---- 公開API -------------` スタイル(NetworkManager準拠)
- 非同期は UniTask、イベント購読は R3(`ReactiveProperty` / `Subscribe`)。コルーチンより UniTask を優先
- `Update()` でのポーリングより、R3 の購読ベースを優先

### テンプレート

```csharp
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectKMP.Monster
{
    /// <summary>
    /// スライムのAI制御。MasterClient でのみ動作する。
    /// </summary>
    public class SlimeAI : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------
        private const float CHASE_RANGE = 5.0f;

        // ---- 内部状態 ------------------------------------
        private SyncObject<MonsterSyncData> _sync;

        // ---- 公開API -------------------------------------

        /// <summary>追跡を開始する</summary>
        public void StartChase() { }
    }
}
```

## 4. 設計パターン(作るものに応じて従う)

依存の向きの全体像と禁止事項は CLAUDE.md。ここでは「作るときの手順」を示す。

### 状態を増やすとき(Model)

1. `Core/PlayerStatus.cs` に `ReactiveProperty` を足し、外へは `ReadOnlyReactiveProperty` で公開する
2. 書き込み用の `SetXxx()` を用意し、**書くのは状態の持ち主(Player側)だけ**
3. 位置が要る相手は値を写し取らず `Transform` のまま持たせる(写すと毎フレーム更新係が要る)

### UI を増やすとき(MVP)

- **View**(`UI/` のボタン・HUD): 渡された値で見た目を作るだけ。状態を知らない
- **Presenter**(`UI/Presenters/`): `PlayerStatusHub.Local` を購読し、判断(計算・分岐)をして View へ結果を渡す
- 表示の起動・配線は `UI/InGame/InGameHudBootstrap.cs` に集める
- **UI から Player を、Player から UI を直接参照しない**。必要になったら状態を Model へ足す

### 何度も出る物を作るとき(オブジェクトプール)

作って `Destroy` の繰り返しは書かず、`Core/GameObjectPool` で使い回す。

```csharp
_pool = new GameObjectPool(CreateOne, 16);  // 最初の1回。prewarm で出はじめの引っかかりを防ぐ
GameObject go = _pool.Rent();               // 借りる
_pool.Return(go);                           // 使い終わったら返す
```

| 作る物 | 参考にする実例 |
|---|---|
| コードで形から組む物 | `Player/RunDust.cs`(`DustPuff`) |
| 線を並べて描く物 | `Battle/ShockwaveRing.cs` |
| 文字を出す物 | `Battle/Onomatopoeia.cs` |
| プレハブから出す物 | `Attack/DamagePopup.cs` |
| 音を鳴らす物 | `Core/OneShotSound.cs` |

- `GameObject.CreatePrimitive` を繰り返し生成に使わない(コライダーが付いてきて、消す手間と無駄が出る)。試作で1つ2つ置くのは可
- マテリアルは共有し、個体差は `MaterialPropertyBlock` で上書きする
- **使い回しは前回の状態が残る**。大きさ・色などは貸し出し時に初期化する(`DamagePopup` の `_originalScale` が前例)

### テストを書くとき

- 場所は `Assets/Tests/EditMode/`。asmdef は `ProjectKMP.Core` 参照・`includePlatforms: ["Editor"]`・`defineConstraints: ["UNITY_INCLUDE_TESTS"]`
- **テストしたいロジックは Core に置く**(計算・整形・記録など、Unity のシーンに依存しない形に切り出す)
- 実行はユーザーに Test Runner(Window > General > Test Runner)での Run All を依頼する。CI でも editmode テストが走る

## 5. Photon(PUN2)運用

- `PhotonNetwork.Instantiate` するプレハブは `Assets/Resources/NetworkPrefabs/` に置く(PUN2の仕様。それ以外の場所では実行時に生成失敗する)
- Resources の用途は上記のみ。他のアセットを Resources に置かない
- ゲーム状態の変更(HP・生成・破壊)は **MasterClient のみ**。`SyncObject<T>.SetValue()` で全員に同期する
- ゲスト側からの直接変更・独自RPCの追加は原則禁止。RPCが必要な設計を求められたら、まず SyncObject で実現できないかを検討し、ユーザーに相談する
- 同期データクラスは `ISyncableData` を実装(`Serialize`/`Deserialize` で Hashtable 変換)
- **AppId の扱いは CLAUDE.md**(コミット禁止・専用メニューから入力・PUN Wizard 使用禁止)

## 6. シーン運用(Unity MCP操作時の特則)

- `Build/Scenes/` の4シーン(Title/Lobby/Battle/Result)は「1シーン=1担当者」制。**Unity MCP からこれらのシーンを変更する場合は、実行前に必ずユーザーへ確認する**(現在開いているシーンが Build/Scenes 配下かどうかを先に確認)
- シーンへの機能追加はプレハブ経由が原則。オブジェクトを直接シーンに置くのではなく、プレハブ化して組み込む
- 動作確認用のオブジェクト配置は `_Sandbox` のシーンで行う

## 7. Unity MCP 操作手順

### スクリプト作成(Unity_RunCommand)

1. §1のマップで作成先を確定(不明なら質問)
2. `Unity_RunCommand` 内で `File.WriteAllText` → `AssetDatabase.Refresh()` で作成。ディレクトリが無ければ `Directory.CreateDirectory` で作る
3. Refresh後にコンパイルが走るため、続けて `Unity_GetConsoleLogs`(logTypes: error)で**エラーゼロを確認**してから完了報告する
4. `.meta` はUnityが自動生成するので触らない。既存ファイルの上書きは、内容を読んで差分を説明してから行う

### アセット生成(Unity_AssetGeneration_GenerateAsset)

- `savePath` / `targetAssetPath` は必ず§1のマップに従って指定する。デフォルトの生成先に任せない
- ファイル名は§2の命名規則に沿って指定する
- 生成後、置き場所と名前がルール通りかを確認し、ズレていれば `Unity_RunCommand`(`AssetDatabase.MoveAsset`)で移動・リネームする

### 禁止事項

- 編集禁止フォルダ(§1)配下への書き込み
- `Build/Scenes/` の無断変更(§6)
- 作成場所が不明なままの作成(§0)
- `Resources` 直下へのネットワークプレハブ以外の配置(§5)
- Player ↔ UI の直接参照の追加(§4)
- `PhotonServerSettings.asset` への AppId 書き込み(CLAUDE.md)
