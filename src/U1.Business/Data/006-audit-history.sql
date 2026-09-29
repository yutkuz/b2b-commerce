CREATE TABLE dbo.AdminEvents (
 Id bigint IDENTITY PRIMARY KEY,
 ActorUserId int NOT NULL REFERENCES dbo.Users(Id),
 EventType varchar(40) NOT NULL,
 EntityType varchar(30) NOT NULL,
 EntityId int NULL,
 Summary nvarchar(500) NOT NULL,
 CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
CREATE INDEX IX_AdminEvents_CreatedAt ON dbo.AdminEvents(CreatedAt DESC, Id DESC);
CREATE INDEX IX_AdminEvents_Actor_CreatedAt ON dbo.AdminEvents(ActorUserId, CreatedAt DESC);
CREATE INDEX IX_AdminEvents_Entity ON dbo.AdminEvents(EntityType, EntityId, CreatedAt DESC);

CREATE TABLE dbo.StockMovements (
 Id bigint IDENTITY PRIMARY KEY,
 ProductId int NOT NULL REFERENCES dbo.Products(Id),
 OrderId int NULL REFERENCES dbo.Orders(Id),
 ActorUserId int NULL REFERENCES dbo.Users(Id),
 AdminEventId bigint NULL REFERENCES dbo.AdminEvents(Id),
 MovementType varchar(40) NOT NULL,
 QuantityDelta int NOT NULL,
 PreviousStock int NOT NULL CHECK(PreviousStock >= 0),
 NewStock int NOT NULL CHECK(NewStock >= 0),
 Reason nvarchar(300) NOT NULL,
 CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 CONSTRAINT CK_StockMovements_Balance CHECK(NewStock = PreviousStock + QuantityDelta));
CREATE INDEX IX_StockMovements_Product_CreatedAt ON dbo.StockMovements(ProductId, CreatedAt DESC, Id DESC);
CREATE INDEX IX_StockMovements_Actor_CreatedAt ON dbo.StockMovements(ActorUserId, CreatedAt DESC);
CREATE UNIQUE INDEX UX_StockMovements_Order_Product_Type
 ON dbo.StockMovements(OrderId, ProductId, MovementType)
 WHERE OrderId IS NOT NULL;

INSERT dbo.StockMovements(
 ProductId, OrderId, ActorUserId, AdminEventId, MovementType,
 QuantityDelta, PreviousStock, NewStock, Reason)
SELECT Id, NULL, NULL, NULL, 'InitialBalance', 0, Stock, Stock,
 N'Geçiş anındaki başlangıç bakiyesi; önceki hareketler bilinmiyor.'
FROM dbo.Products;
