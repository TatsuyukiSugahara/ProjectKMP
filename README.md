# ProjectKMP

オープンキャンパス展示用の、オンラインマルチプレイ対戦ゲーム。
犬になって、みんなでボスに立ち向かう。

## ゲームの特徴

- みんなの攻撃とフィールド破壊で、共有の「みんなのパワー」がたまる
- 満タンになったら攻撃ボタンで参加し、全員の力を合わせた「わんぱくバースト」を発動する
- 元気玉やビームで木と草を巻き込み、連鎖破壊を起こせる
- リザルトでは順位ではなく、必殺技への参加や攻撃など一人ひとりの活躍をほめる

## 開発環境

- Unity 6000.3.14f1 / Universal Render Pipeline
- Photon PUN2(通信) / UniTask(非同期) / R3(イベント) / TextMesh Pro(文字)

## フォルダ構成

「種類 → 機能」の順で分ける。種類フォルダの直下には置かず、必ず機能名のフォルダを1階層はさむ。

| 置くもの | 場所 |
|---|---|
| C#スクリプト | `Assets/Contents/Scripts/<機能名>/` |
| プレハブ | `Assets/Contents/Prefabs/<機能名>/` |
| ネットワーク生成プレハブ | `Assets/Resources/NetworkPrefabs/` |
| モデル・テクスチャ・マテリアル | `Assets/Contents/Art/<機能名>/` |
| UIスプライト | `Assets/Contents/UI/Sprites/` |
| 効果音・BGM | `Assets/Contents/Audio/SE/` `Assets/Contents/Audio/BGM/` |
| 本番シーン | `Assets/Build/Scenes/`(Title / Lobby / InGame / Result) |
| 作業用シーン | `Assets/_Sandbox/<担当者名>/` |

**フォルダ名と名前空間は一致させる。** `Scripts/Core/` に置くなら `namespace ProjectKMP.Core`。
ずれていても動くが、型を探すときにフォルダを頼りにできなくなる。

## 設計(層の分けかた)

入力・演出・状態を層として切り出し、依存の向きを一方通行に揃えている。
層の境目は asmdef(アセンブリ定義)で区切ってあるので、**逆向きの参照はコンパイルの時点で通らない**。
口約束ではなく、仕組みとして守られる。

```
Player / UI / Battle / Attack / ...   ← ゲーム本体(Assembly-CSharp)
              ↓
      ProjectKMP.Presentation         ← 演出
              ↓
      ProjectKMP.Core                 ← 入力・状態・共通の道具
              ↓
      Unity.InputSystem / R3
```

| 層 | asmdef | 置くもの | 参照してよい先 |
|---|---|---|---|
| Core | `ProjectKMP.Core` | 入力・状態・共通の道具 | InputSystem / R3 のみ |
| Presentation | `ProjectKMP.Presentation` | 画面演出・音演出 | Core / UniTask |
| ゲーム本体 | (なし) | 犬・ボス・UI・通信など | どちらも可 |

### 入力は Core に集める

操作の読み取りは `Core/GameInput.cs` にまとめてある。
InputSystem の `InputAction` を1箇所で持ち、使う側は結果だけを見る。
キーボード・パッド・画面タッチのどれで操作されたかを、各所が知らなくてよくなる。

### 状態は R3 で受け渡す(Model)

`Core/PlayerStatus.cs` が操作している人の状態を `ReactiveProperty` で持つ。
外へは `ReadOnlyReactiveProperty` として公開するので、**書き換えられるのは持ち主だけ**。
見る側は購読するだけでよく、`Update()` で毎フレーム見に行かなくてよい。
置き場は `Core/PlayerStatusHub.cs`。

| 持っているもの | 中身 |
|---|---|
| 体力 | `CurrentHp` `MaxHp` `IsDead` |
| 技の待ち時間 | `AttackCooldown01` `BeamCooldown01` `DiveCooldown01` `EnergyBallCooldown01` |
| 押しどき | `IsInJustWindow` `IsAimingBeam` |
| 復活 | `RespawnRemainingSec` `RespawnDelaySec` |
| 相手 | `LockTarget` `FriendBeamCallTarget` |

`LockTarget` などは**位置を写し取らず `Transform` のまま持たせる**。
写し取ると誰かが毎フレーム更新しなければならないが、`Transform` のままなら
画面側が必要になった時に自分で位置を測ればよい。

### 演出は Presentation へ出す

`HitStop` `ScreenFlash` `ImpactFrame` `SkillCutin` `FriendBeamCutin` `BgmPlayer` `UiSoundPlayer` などは
`Presentation/` にまとめた。もともと `UI/` にあった物も移してある。
演出は「ゲームの都合を知らないまま呼ばれるだけ」の立場にしたいため。

### Player と UI の循環をほどいた(MVP)

以前は `Player` と `UI` が互いを直接参照していた(Player→UI が5型、UI→Player が4型)。
調べてみると **本当に要る依存は5行だけ** で、残りは使っていない `using` だった。
その5行も、技の側が表示の用意をしていたことが原因だったので、画面側へ移した(`UI/InGame/InGameHudBootstrap.cs`)。

いまは Model を挟んだ三者の関係になっている。

| 役 | 担当 | 相手を知っているか |
|---|---|---|
| Model | `Core/PlayerStatus` | 誰も知らない |
| Presenter | `UI/Presenters/` `UI/InGame/PlayerHpPresenter.cs` | Model と View を知る |
| View | `UI/` のボタンや HUD | 何も知らない。渡された値で見た目を作るだけ |

```
Player/PlayerHealth.cs              Core/PlayerStatus          UI/Presenters/…
  PlayerStatusHub.Local              ReadOnlyReactiveProperty    status.X.Subscribe(...)
    .SetHp(hp, maxHp)        ──→        CurrentHp        ──→       → View へ値を渡す
```

書く側は誰が見ているか知らない。見る側は誰が書いたか知らない。
間に立つ `PlayerStatus` だけが両方から見える。HUD を増やしても `Player` 側は変更不要になる。

### Presenter の一覧

| Presenter | 何をつなぐか |
|---|---|
| `SkillButtonPresenter` | 3つの技の待ち時間 → ボタン |
| `AttackButtonPresenter` | 待ち時間・押しどき・押下状態 → 攻撃ボタン |
| `DangerVignettePresenter` | 体力から危険度を求める → 赤い縁 |
| `LockOnPresenter` | 注目先 → 印とターゲットボタン |
| `FriendBeamSignalPresenter` | 呼びかけ先 → 合図 |
| `PlayerHpPresenter` | 体力・死亡・復活 → HPバー |

判断(危険度の計算など)は Presenter が持ち、View には結果だけを渡す。
View が状態を知らないので、見た目を差し替えても計算側に触らなくてよい。

### 残っている循環

層として切り出した `Core` と `Presentation` は、`ProjectKMP` 配下のどこも参照していない(参照は0)。
一方、ゲーム本体の機能フォルダ同士には循環が残っている。

| 組 |
|---|
| `Attack` ↔ `Player` |
| `Battle` ↔ `Player` |
| `Battle` ↔ `UI` |
| `Battle` ↔ `Monster` |
| `Dog` ↔ `Player` |
| `Gorilla` ↔ `Player` |

これらは担当が分かれている領域なので未着手。`Player` と `UI` と同じやりかた
(状態を Core の Model へ出し、互いを直接呼ばない)で順に解いていく方針。
解けた組から `Player` `UI` などにも asmdef を切り、向きを仕組みとして固定する。

現状の詳細と解決方針は `Assets/Contents/Scripts/依存関係のメモ.md` に置いてある。

## Photon App ID の扱い

このリポジトリは public のため、**Photon の App ID はコミットしない**。
`PhotonServerSettings` に直接書かず、ローカルの設定から読み込む / ビルド時に注入する運用にしている。
誤ってコミットしてしまった場合は、Photon 側で App ID を再発行すること(履歴からは消えない)。

## 負荷対策(オブジェクトプール)

砂埃のように何度も出る物は、毎回作って捨てると片付けの処理が積み上がり、時々画面が引っかかる。
`Assets/Contents/Scripts/Core/GameObjectPool.cs` で使い回す仕組みを用意した。
借りる側は `Rent()` と `Return()` を呼ぶだけで、足りなければ勝手に増える。

### 使っている場所

| 場所 | 使い回すもの | 先に用意する数 |
|---|---|---|
| `Player/RunDust.cs`(`DustPuff`) | 走ったときの砂埃 | 16 |
| `Battle/Onomatopoeia.cs` | 「ガブッ！」などの擬音 | 8 |
| `Battle/ShockwaveRing.cs` | 着地・爆発の衝撃波の輪 | 4 |
| `Attack/DamagePopup.cs` | ダメージの数字 | 8 |
| `Core/OneShotSound.cs` | 場所を指定して鳴らす音 | 8 |

どれも「連鎖でまとめて出る」物。多人数と連鎖が重なると一度に何十個も湧くので、
そのたびに作って捨てると、その瞬間だけ処理が跳ね上がる。

`DamagePopup` だけは置き場を1つではなく **元になるプレハブごとに分けて持つ**(`Dictionary`)。
技によって数字の見た目が違うため。借りた物でなければ今までどおり `Destroy` する作りにしてあるので、
プレハブから直接置いた分と混ざっても壊れない。

`OneShotSound` は Unity 標準の `PlayClipAtPoint` の置きかえでもある。
標準のものは距離による減衰がきつく戦闘距離で聞こえないうえ、毎回 GameObject を作って捨てる。

### 直したこと(砂埃を例に)

以下は砂埃(`DustPuff`)の場合。他の4つも考えかたは同じ。

| | 前 | 今 |
|---|---|---|
| 作りかた | `GameObject.CreatePrimitive` | 板1枚のメッシュを自前で組む |
| コライダー | 毎回ついてくるので毎回消していた | そもそも作らない |
| 材質 | 1つずつ持つ | 1つを共有し、濃さだけ `MaterialPropertyBlock` で上書き |
| 使い終わり | `Destroy` | プールへ返す |

材質を共有にしたことで、描画をまとめる効果も出ている(見た目の負荷も下がる)。

### CreatePrimitive をやめた理由

前はこう書いていた。

```csharp
var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
go.name = "RunDust";

// 当たり判定は要らない。付いたままだと自分の足を蹴ってしまう
Collider collider = go.GetComponent<Collider>();
if (collider != null) Destroy(collider);
```

`CreatePrimitive` は板を1枚作る手軽な関数だが、メッシュ・マテリアル・コライダーをまとめて付けてくる。
自分でメッシュを組む手間を省く近道として使ったものの、砂埃のように何度も出る物には向いていなかった。

コライダーは要らないだけではない。付いたままだと自分の足を蹴ってしまう。
つまり **作ると壊れる物を、毎回作ってから消していた**。作ってすぐ消すのは一番無駄な流れになる。

いまは頂点4つの板を自分で組んで1枚だけ共有しているので、コライダーは最初から存在しない。

> **消しているコードがあったら、そもそも作らない方法を探す。**
> 速くする工夫より、要らない処理を見つけるほうが効くことが多い。

### CreatePrimitive の使い分け

| 向いている | 向いていない |
|---|---|
| 試作や動作確認で1つ2つ置く | 実行中に繰り返し作る |
| 当たり判定も欲しい | いらない部品まで付いてくると困る |

`Assets/Contents/Scripts/Field/FieldBuilder.cs` の地面生成は前者にあたる。
エディタ上で1回だけ作り、当たり判定も要るので `CreatePrimitive` のままでよい。ここは直さないこと。

### 効果

Profiler での比較(ひとりで計測)。

| | 前 | 今 |
|---|---|---|
| GC Allocated In Frame(件数) | 74 | 1 |
| GC Allocated In Frame(量) | 3.1 KB | 34 B |
| Materials | 210 | 197 |

エディタ上の計測なので、Managed Heap や Total Memory はエディタと Profiler 自身の使用分が大半を占める。
そのため比較として意味があるのは **GC Allocated In Frame** の欄だけ。

### 20人での試算

砂埃は 0.09 秒ごとに1つなので、1人あたり毎秒およそ11個。20人なら毎秒220個になる。

- 前: 毎秒220個の作成と破棄
- 今: 最初の数十個だけ作り、あとは作成ゼロ

### 使いかた

同じように何度も出る物(当たった跡、はじけた粒など)を足すときは、
新しく `Destroy` を書かず、このプールを使うこと。

```csharp
// 最初の1回だけ用意する。prewarm しておくと出はじめで引っかからない
_pool = new GameObjectPool(CreateOne, 16);

GameObject go = _pool.Rent();   // 借りる
_pool.Return(go);               // 使い終わったら返す
```

参考にする実例は、作る物によって選ぶ。

| 作る物 | 参考にするもの |
|---|---|
| コードで形から組む物 | `Player/RunDust.cs`(`DustPuff`) |
| 線を並べて描く物 | `Battle/ShockwaveRing.cs` |
| 文字を出す物 | `Battle/Onomatopoeia.cs` |
| プレハブから出す物 | `Attack/DamagePopup.cs` |
| 音を鳴らす物 | `Core/OneShotSound.cs` |

使い回すときは **前回の状態が残る** ことに注意する。
`DamagePopup` が元の大きさを最初に控えているのは、前回の拡大が残ったまま次に貸し出されるのを防ぐため。

## クレジット

### 制作・開発

- **KBCGames**(ゲーム内表記: Produced & Developed by KBCGames)

### 絵文字

- **Noto Emoji** © Google LLC — [SIL Open Font License 1.1](https://scripts.sil.org/OFL)
  - 該当ファイル: `Assets/Contents/UI/Sprites/TEX_UI_Handshake.png`(🤝 U+1F91D を書き出したもの)
  - 出典: https://github.com/googlefonts/noto-emoji

### 通信

- **Photon Unity Networking 2**(Exit Games) — Photon の利用条件に従う

### 効果音

- `Assets/Contents/Audio/SE/` の音はすべて自作(C#で波形を生成)。外部素材は使っていない。

なお、素材を追加するときは **ここと、ゲーム内タイトル画面のクレジット表示の両方**に追記すること。
表示側は `Assets/Contents/Scripts/UI/CreditsLabel.cs` を付けたオブジェクトで管理している。
