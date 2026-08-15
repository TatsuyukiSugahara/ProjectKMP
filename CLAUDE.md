# CLAUDE.md

ProjectKMP — オープンキャンパス展示用のオンラインマルチ対戦ゲーム。
Unity 6000.3 / Photon PUN2 / UniTask / R3 / URP。

**public リポジトリ**。誰でも見られる前提で扱うこと。

Unity への作成・変更の手順(作成場所マップ・命名・MCP操作)はスキル `unity-projectkmp` に従う。

## 絶対ルール

- `Assets/Photon/` `Assets/TextMesh Pro/` `Assets/Packages/` などサードパーティ製は**読み取り専用**
- `Assets/Build/Scenes/` の4シーン(Title/Lobby/Battle/Result)の変更は、実行前にユーザー確認必須
- `PhotonNetwork.Instantiate` するプレハブは `Assets/Resources/NetworkPrefabs/` のみ。Resources を他の用途に使わない
- ゲーム状態の変更(HP・生成・破壊)は MasterClient のみ。`SyncObject<T>` で全員に同期する
- 作成場所が一意に決まらないときは、勝手に決めず質問する

## Photon AppId

- **AppId はコミットしない**。`PhotonServerSettings.asset` の `AppIdRealtime` は空が正しい状態
- 入力は Unity メニュー `ProjectKMP > Photon > AppId を いれる`(EditorPrefs = 端末内のみに保存)
- 再生時とビルド時に `PhotonAppIdInjector` が自動で流し込み、終わったら消す
- CI へは `-kmpPhotonAppId <id>` 引数で渡す(GitHub Secret `PHOTON_APP_ID`)。環境変数 `PROJECTKMP_PHOTON_APPID` も可
- **PUN Wizard は使わない**。アセットへ直書きされ、Injector が消すので故障に見える
- 誤ってコミットしたら Photon ダッシュボードで AppId をローテーションする一択(履歴からは消えない)

## 設計(依存の向き)

```
Core          入力の読み取り口・プレイヤーの状態・計算・使い回しの置き場
                 → ProjectKMP の誰にも依存しない(InputSystem / R3 のみ)

Presentation  演出と音(ヒットストップ・画面のひと光り・BGM・カットイン)
                 → Core のみ

Player        犬の操作・技・カメラ
UI            画面の表示
                 → 互いに依存しない
```

`Core` と `Presentation` には asmdef を付けてあり、参照できる先が設定で固定される。

### 守ること

- **フォルダ名と名前空間は一致させる**。`Scripts/Core/` なら `namespace ProjectKMP.Core`
- **Player ↔ UI の直接参照を復活させない**。状態は `Core/PlayerStatus` 経由で受け渡す
- 状態を増やすときは `PlayerStatus` に `ReactiveProperty` を足し、外へは `ReadOnlyReactiveProperty` で公開。書くのは持ち主だけ
- UI の判断(計算・分岐)は `UI/Presenters/` の Presenter に置く。View は渡された値で見た目を作るだけ
- 何度も出る物(砂埃・擬音・数字・音など)は `Core/GameObjectPool` で使い回す。作って `Destroy` の繰り返しは書かない
- 位置が要る相手は値を写し取らず `Transform` のまま Model に持たせ、見る側が必要な時に測る

### Player と UI の循環を解いた方法(前例)

1. **入力を集約** — キーの直読みが20ファイルに散っていた。割り当てを `Assets/Resources/ProjectKMP.inputactions` へ集め、`Core.GameInput` を唯一の読み取り口にした。画面のボタンは `TouchControls` が毎フレーム `GameInput.PushTouch` で押し込む
2. **演出を別層へ** — `ImpactFrame` `BgmPlayer` など「呼ばれるだけ」の処理を `Presentation` へ移し、Player からも UI からも同じように使える形にした
3. **状態を Model 経由に** — `Core.PlayerStatus` に体力・技の待ち時間・注目相手を持たせ、Player が書き UI(Presenter) が読む。UI はプレイヤーを探し回らなくてよくなり、ネットワークで遅れて生まれる相手を待つ必要もなくなった

### 未解決の循環(Battle / Monster / Gorilla)

```
Attack ↔ Player / Battle ↔ Player / Battle ↔ UI
Battle ↔ Monster / Dog ↔ Player / Gorilla ↔ Player
```

ボスの実装は別担当が進めているため**未着手**。勝手に構造を変えるとぶつかるので、着手前にユーザーへ確認する。

解くとしたらの方針(Player と UI の手がそのまま当てはまる):

- **ボスの状態を Model へ**: 体力・残り本数・いまの行動を `Core` に置き、UI と Battle はそこから読む → `Monster → Battle` が消える
- **戦いの進行を Model へ**: 『戦闘中か』『クリアしたか』を `Core` に置く。`BattlePlayGate` が近い役割を持っているので、そこを育てる
- **ボスから Player を直接見ない**: 『狙う相手』を渡す形にすれば `Gorilla → Player` が消える

先に規模を測ること。各フォルダで他フォルダの名前空間を using している箇所を数える。
Player と UI のときは、12件の using のうち7件が未使用で、本当の依存は5行だけだった。
見た目より小さいことが多い。

### 分割のときに気をつけること

- asmdef を付けると使う道具を明示する必要がある(`Core` で InputSystem / R3 の書き忘れによる型エラーの前例あり)
- シーンやプレハブの参照は名前空間を変えても切れない(GUID 管理)。手書きの `using` だけ直す
- 使っていない `using` が依存に見える。消してコンパイルが通れば要らなかったと分かる

## コミット時の注意

- SDF フォントアセット(`MPLUSRounded1c-Black SDF.asset` など)の差分は Dynamic アトラスが実行時に足した字形。ビルド時に消える設定なので**コミットせず破棄する**
- `ProjectSettings.asset` に `KMP_SINGLE_ONLY`(ひとり用ビルドの合言葉)の付け外しが出たら、意図した変更かユーザーに確認する
- コミット・push はユーザーの指示があったときだけ行う

## テストと CI

- テストは `Assets/Tests/EditMode/`。asmdef は `ProjectKMP.Core` 参照・Editor 限定・`UNITY_INCLUDE_TESTS` 制約
- **テスト対象にしたいロジックは Core に置く**(テストのasmdefから見えるのは Core だけ)
- `build.yml` = 手動実行。あそびかた(single/multi)と機種を選べる。multi は Secret `PHOTON_APP_ID` が必要
- `release.yml` = 手動実行(バージョン空欄で自動繰り上げ) or `v*` タグ push。**ひとり用固定**なので AppId 不要
- どちらも test ジョブ(EditMode)が通らないとビルドへ進まない

## ドキュメント運用

- 素材を追加したら README のクレジットと、タイトル画面のクレジット表示(`UI/CreditsLabel.cs`)の**両方**に追記
- README に書く実装の説明は、コードと照合してから書く(存在しない型・数値を載せない)
