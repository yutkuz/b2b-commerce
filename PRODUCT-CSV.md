# Product CSV format

Admin product bulk import/export uses one UTF-8 CSV contract.

Header order:

`code,name,description,brand,manufacturerCode,specialCode1,specialCode2,imageUrl,stock,criticalStock,price,category,rowVersion,stockReason`

Fields containing a comma, line break, or double quote use RFC 4180 quoting. Embedded double quotes are doubled. `category` is the category name. Existing products require the Base64 `rowVersion` returned by export; new products leave it empty. `stockReason` is required whenever stock changes. The limit is 1 MiB and 1000 data rows.

`GET /api/admin/products/export` exports the format. Text cells beginning with `=`, `+`, `-`, or `@` are prefixed with a single quote to mitigate spreadsheet formula injection.

`POST /api/admin/products/import/preview` accepts JSON `{"csv":"..."}`, writes nothing, and returns row errors plus a `previewToken`. `POST /api/admin/products/import/apply` accepts the same CSV plus `previewToken` and a unique `importId`. The importId is the retry/idempotency key. If the CSV or an affected existing product changes after preview, apply returns 409. The batch is transactional. Repeating the same importId does not create a second stock movement.
