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

## Photon App ID の扱い

このリポジトリは public のため、**Photon の App ID はコミットしない**。
`PhotonServerSettings` に直接書かず、ローカルの設定から読み込む / ビルド時に注入する運用にしている。
誤ってコミットしてしまった場合は、Photon 側で App ID を再発行すること(履歴からは消えない)。

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
