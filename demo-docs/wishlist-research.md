# eShop ウィッシュリスト機能 — 調査・設計レポート

> 本資料はウィッシュリストAPIの実装前に作成した調査レポートです。その後の実装内容ではなく、当時のコード調査と設計提案を記録しています。

## 要約

このリポジトリには、ウィッシュリスト機能は見当たらない。一方、商品カタログ、ログインユーザーに紐づく Redis の買い物カゴ、注文作成、非同期の在庫確認はすでにある。したがって、ウィッシュリストを買い物カゴに流用するのではなく、ユーザー所有の永続データとして分離し、商品詳細／専用一覧からカートへ移す導線を実装するのがよい。

**推奨する初期仕様**

- ログインユーザーの `sub` に紐づけ、商品IDと追加日時を保存する。1ユーザーにつき同一商品は1件とし、追加操作は冪等にする。
- 在庫切れ商品も保存できる。ウィッシュリストへの追加ではカートや在庫を変更しない。
- 「カートに追加」時に最新カタログ情報を確認する。在庫切れ・商品削除・追加失敗の場合はウィッシュリストを保持し、理由を表示する。
- カート追加に成功したときのみウィッシュリストから削除する（本レポートでは「カートへ移動」として扱う）。別の「カートへ追加して保存を続ける」動作を後から設ける場合は別操作にする。
- ウィッシュリスト登録は在庫予約ではない。カート投入後も注文時点の在庫確認・注文結果を正とする。

初期実装の構成は **Wishlist API + Wishlist 専用の PostgreSQL データベース**を推奨する。既存のサービス境界と永続化方式を維持でき、カタログ、買い物カゴ、注文の各責務に保存リストを混在させずに済む。小規模なデモに限り WebApp 内の独立した永続化モジュールも選択肢だが、Basket API の Redis 領域をウィッシュリストの正本にする設計は避ける。

## 調査範囲と前提

README は本リポジトリを Aspire を使うサービスベースの .NET eコマース・リファレンスアプリとして説明している（[`README.md`](../README.md#L1-L3)）。調査では主に `WebApp`、`Basket.API`、`Catalog.API`、`Ordering.API`、AppHost、関連テストを確認した。リポジトリ内に `Wishlist` / `WishList` / `wishlist` の一致はなく、GitHub のインデックス検索でも `dotnet/eShop` の `Wishlist` に一致するコード結果は得られなかった。

**前提・未確定事項**

- 依頼はWebサイトの機能であるため、初期対象は `src/WebApp` とする。`HybridApp` や MAUI クライアントにも同じ機能が必要かは、依頼からは判断できない。
- 別端末からの再訪にも対応する想定で、ログイン済みユーザー向けのサーバー永続化を前提にする。ゲスト用の一時リストやログイン時のマージは今回の初期範囲に含めない。
- カタログ商品は現在 `int Id` で特定されている（[`CatalogItem.cs`](../src/Catalog.API/Model/CatalogItem.cs#L8-L25)）。色・サイズ等のSKU／バリエーション単位の保存はコード上確認できず、必要なら商品IDでなくSKU IDを保存する設計に拡張する。
- 数量、共有リスト、複数リスト、値下げ／再入荷通知は必須要件に含まれないため初期仕様に含めない。通知を追加する場合は明示的な購読・配信停止と通知イベントが別途必要。

## 現状の仕組みと実装上の含意

### 認証とユーザー識別

WebApp は Cookie + OpenID Connect を使い、`orders` と `basket` のスコープを要求する（[`WebApp/Extensions/Extensions.cs`](../src/WebApp/Extensions/Extensions.cs#L66-L87)）。購入者IDの取得には `sub` クレームを用いる（同ファイル `GetBuyerIdAsync`）。Basket gRPC サービスも認証ユーザーのIDからバスケットを特定し、更新・削除は未認証を拒否する（[`BasketService.cs`](../src/Basket.API/Grpc/BasketService.cs#L13-L72)）。

そのためウィッシュリストもクライアント送信のユーザーIDを信用せず、サーバーが認証済み principal から `sub` を取り出して所有者を決めるべきである。API追加時は認証ミドルウェア、認可ポリシー、必要な Identity scope、WebApp の認証付き API クライアント登録を一貫して追加する。ユーザーAのIDをリクエストに指定してユーザーBのリストを読み書きできないことをテストする。

### 買い物カゴは購入用の状態

Basket API は `/basket/{userId}` 形式のキーで Redis にJSONを保存する（[`RedisBasketRepository.cs`](../src/Basket.API/Repositories/RedisBasketRepository.cs#L10-L47)）。Basket API は `BasketItem` の商品ID・数量を保持し、WebApp は商品詳細をカタログから取得して画面表示・注文用の価格を組み立てる（[`BasketState.cs`](../src/WebApp/Services/BasketState.cs#L117-L147)）。

これは「購入候補を長期間保存する」意味のウィッシュリストとはライフサイクルが違う。`BasketState.DeleteBasketAsync` はチェックアウト後にバスケットを削除するため（同 `#L78-L110`）、ここにウィッシュリストを格納すると購入完了時に消える可能性がある。Basket API に保存リストを追加する場合も、Redis のBasket key／モデルとは別の永続データとして分離しなければならない。

### カタログと在庫

Catalog API の商品モデルは `AvailableStock` を持ち、0在庫では `RemoveStock` が売り切れ例外を出す（[`CatalogItem.cs`](../src/Catalog.API/Model/CatalogItem.cs#L27-L78)）。カタログAPIには商品IDの一括取得経路がある（[`CatalogApi.cs`](../src/Catalog.API/Apis/CatalogApi.cs#L207-L236)）。

ただしWebApp共有DTOはID・名前・説明・価格等を持つ一方、在庫状態を含まない（[`WebAppComponents/Catalog/CatalogItem.cs`](../src/WebAppComponents/Catalog/CatalogItem.cs#L1-L7)）。商品ページにも現在カート追加ボタンはあるが、在庫状態を表示する分岐は見当たらない（[`ItemPage.razor`](../src/WebApp/Components/Pages/Item/ItemPage.razor#L39-L55)）。従って、保存時に在庫チェックを追加してはいけない一方、カート移動前の最新状態確認とユーザー向けの unavailable 状態表示が必要になる。画面に在庫数を公開する必要はなく、利用可否だけを返すAPIの方が望ましい。

**重要:** 受注後の在庫チェックはイベント経由で行われ、注文数量と `AvailableStock` を比較して `OrderStockRejected` / `OrderStockConfirmed` を発行する（[`OrderStatusChangedToAwaitingValidationIntegrationEventHandler.cs`](../src/Catalog.API/IntegrationEvents/EventHandling/OrderStatusChangedToAwaitingValidationIntegrationEventHandler.cs#L14-L32)）。在庫確認と引当の間に時間差があるため、一覧上の「在庫あり」やカート投入を在庫確保・購入確約として扱えない。リストからカートへ移した後でも注文結果が拒否され得ることをUIで伝え、在庫切れ時に保存アイテムを失わないことが重要である。

### 商品価格とカート投入

WebApp は買い物カゴを読むときカタログから商品詳細を再取得し、その時点の `Price` を表示用 `BasketItem` に割り当てる（[`BasketState.cs`](../src/WebApp/Services/BasketState.cs#L117-L147)）。よってWishlistEntryには原則商品IDだけを保存し、価格は表示時・カート移動時にカタログから取得する。保存時価格を表示する場合でも比較用の参考情報に限定し、注文金額の根拠にしない。

既存 `BasketState.AddAsync` は同じ商品がカートにあれば数量を1増やす（[`BasketState.cs`](../src/WebApp/Services/BasketState.cs#L33-L56)）。カート移動ボタンの二重送信や再試行による意図しない数量増加を避けるため、既にカート内に商品がある場合の仕様（「既存数量を維持して移動済みとする」か「1個追加する」か）を決め、処理中UI／冪等性を用意する。アイテムの追加成功前にリスト行を削除してはならない。

## 推奨する最小実装

### データモデル

単一リストの最小モデル例：

| 列 | 用途 |
|---|---|
| `UserId` | OIDC `sub`。認証 principal からサーバーで設定 |
| `ProductId` | カタログ商品ID。将来SKUが必要ならSKU IDに変更 |
| `AddedAtUtc` | 保存日時・並び替え用 |

`(UserId, ProductId)` に一意制約を置き、追加は upsert／既存なら成功扱いとする。商品名・価格・画像URLを保持する必要があるならスナップショットと明記し、表示のたびに最新カタログ情報を取得する。商品が削除された場合にもリスト自体は読み出せるよう、カタログ参照失敗を1商品単位の「利用不可」として表示し、ユーザーが削除できるようにする。

### API・サービス

- `GET /api/wishlist` — 認証ユーザーの保存商品一覧。
- `PUT /api/wishlist/items/{productId}` — 冪等な追加（既存商品は重複行を作らない）。追加時点の在庫は検査しない。
- `DELETE /api/wishlist/items/{productId}` — 認証ユーザー自身の項目のみ削除。
- 必要に応じてカタログの商品ID一括取得で商品情報をまとめ、一覧のN+1 API呼び出しを避ける。
- `Wishlist.API` と `wishlistdb` を AppHost に追加する場合、DB migration、health check、service discovery、YARPを経由する設計ならルートも合わせて登録する。AppHost は現在 Basket に Redis、Catalog / Ordering にそれぞれ PostgreSQL DB を割り当てている（[`AppHost.cs`](../src/eShop.AppHost/AppHost.cs#L9-L44)）。

### UI・ユーザーフロー

1. 商品詳細と一覧に「ウィッシュリストに保存」操作を加える。保存ボタンは在庫切れでも無効化しない。
2. 認証済みユーザー向けの `/wishlist` ページを設け、商品情報、価格、現在の利用可否、削除ボタンを表示する。
3. 販売可能なアイテムで「カートへ移動」を押したら、サーバーで最新カタログ状態を確認して既存Basket APIへ追加する。成功後にWishlistから削除する。失敗時・在庫切れ時は行を維持し、状態を示す。
4. 在庫は後から変化するため、カート移動成功を購入成功と表示しない。通常のカート・チェックアウト・注文結果フローへ遷移する。
5. 未ログイン時はログインを促す。ゲストも保存できるようにする場合は、Cookie上の一時リストの保持期限とログイン時マージの重複ルールを別途定義する。

## テストと受け入れ条件

### API／永続化

- 在庫0の商品でも認証ユーザーは追加できる。
- 同一ユーザーからの同一商品追加を繰り返しても重複レコードができない。
- 再ログイン後、または別セッションから一覧が復元する。
- AユーザーがBユーザーのデータを取得・変更・削除できない。未認証の追加・読み取り・削除は認可方針に従い拒否する。
- 商品が見つからない項目も一覧取得全体を失敗させず、利用不可として削除可能にする。

### Web／カート・注文

- 在庫切れの商品詳細から保存でき、注文・カートに勝手に追加されない。
- 在庫切れ商品を「カートへ移動」した場合、明確なメッセージを出し、リストに残る。
- 再入荷後はカートへ移せる。カートへの更新が失敗した場合もリストを保持する。
- 既にカートにある商品、ボタン連打、通信リトライで数量が意図せず増えない。
- 保存後に価格が変化した場合は最新価格を示し、注文では既存のカタログ連携を使う。
- カート投入後に注文側の在庫確認で拒否された場合、注文ステータスを正しく示す。少なくともウィッシュリストから消えた商品を再び保存・追加できる。

既存の Playwright E2E は `e2e/CartTest.spec.ts` と `e2e/CheckoutTest.spec.ts` にカート・購入フローがある。カタログAPIの機能テストは `tests/Catalog.FunctionalTests/CatalogApiTests.cs` にあるため、APIテストと同じレイヤーにWishlist機能テストを追加し、最後にE2Eを増やすとよい。

## 実装順序の案

1. **仕様と境界を固定:** ログイン必須、1ユーザー1件／商品、保存→移動後に削除、既存カート商品との扱いを決定。
2. **永続化とAPI:** Wishlistモデル、DB、所有者認可、冪等API、APIテスト。
3. **Web画面:** 商品詳細・一覧の保存操作と `/wishlist` 表示・削除。
4. **在庫確認とカート移動:** カタログ最新状態を確認し、カート投入成功後にのみWishlist項目を削除。
5. **E2E・競合UX:** 再訪、売り切れ、再入荷、通信失敗、注文時の在庫拒否を検証。

## 参考資料・制約

本レポートの設計判断は、上記ローカルソースコードに基づく。GitHubコード検索は一致結果なし、Web取得はこの実行環境のDNS／リモートMCP接続エラーで失敗し、外部ページを実際に確認できなかった。そのため以下は追加確認用の公式資料であり、今回の調査では内容を検証済みの根拠として扱っていない。

- [Microsoft .NET eShop reference application](https://github.com/dotnet/eShop) — 元プロジェクトのリポジトリ
- [ASP.NET Core claims-based authorization](https://learn.microsoft.com/aspnet/core/security/authorization/claims)
- [PostgreSQL constraints](https://www.postgresql.org/docs/current/ddl-constraints.html) — `(UserId, ProductId)` 一意制約の実装確認用
- [OWASP Authorization Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html) — ユーザー所有データの認可設計確認用
