# Product CSV format

Admin product bulk import/export uses one UTF-8 CSV contract.

Header order:

`code,name,description,brand,manufacturerCode,specialCode1,specialCode2,imageUrl,stock,criticalStock,price,category,rowVersion,stockReason`

Fields containing a comma, line break, or double quote use RFC 4180 quoting. Embedded double quotes are doubled. `category` is the category name. Existing products require the Base64 `rowVersion` returned by export; new products leave it empty. `stockReason` is required whenever stock changes. The limit is 1 MiB and 1000 data rows.

`GET /api/admin/products/export` exports the format. Text cells beginning with `=`, `+`, `-`, or `@` are prefixed with a single quote to mitigate spreadsheet formula injection.

In the admin **Ürün yönetimi** screen, choose **CSV dışa aktar** to download the current template and row versions. Choose **CSV içe aktar**, select a file, and inspect each product's old and new values before selecting **Önizlenen değişiklikleri uygula**. Rows with errors cannot be applied. Keep the original file until the operation is confirmed.

`POST /api/admin/products/import/preview` accepts JSON `{"csv":"..."}`, writes nothing, and returns per-row errors, old/new values, and a `previewToken`. `POST /api/admin/products/import/apply` accepts the same CSV plus `previewToken` and a unique `importId`. The importId is the retry/idempotency key: retry an uncertain response with the same ID, CSV, and token. Reusing the ID with different CSV content returns `409 IMPORT_ID_REUSED`. If the CSV, affected product, or referenced uploaded image changes after preview, apply returns `409 IMPORT_PREVIEW_STALE`. The batch is transactional; a rejected batch writes no products, stock movements, or import audit event. Concurrent imports lock affected product codes in a stable order. A transient SQL collision returns a retryable `503 IMPORT_RETRY`; take a fresh preview before retrying with a new ID.
