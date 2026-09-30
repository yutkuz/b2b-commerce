DECLARE @statusConstraint sysname;
SELECT @statusConstraint = cc.name
FROM sys.check_constraints AS cc
WHERE cc.parent_object_id = OBJECT_ID(N'dbo.Orders')
  AND cc.definition LIKE N'%Status%';

IF @statusConstraint IS NOT NULL
BEGIN
    DECLARE @dropStatusConstraint nvarchar(max) =
        N'ALTER TABLE dbo.Orders DROP CONSTRAINT ' + QUOTENAME(@statusConstraint);
    EXEC sys.sp_executesql @dropStatusConstraint;
END;

ALTER TABLE dbo.Orders
ADD CONSTRAINT CK_Orders_Status CHECK
    (Status IN (N'Bekliyor', N'Onaylandı', N'Hazırlanıyor', N'Sevk edildi', N'Teslim edildi', N'Reddedildi', N'İptal edildi'));

ALTER TABLE dbo.Orders
ADD AdminNote nvarchar(1000) NOT NULL CONSTRAINT DF_Orders_AdminNote DEFAULT N'',
    RejectionReason nvarchar(300) NOT NULL CONSTRAINT DF_Orders_RejectionReason DEFAULT N'',
    RowVersion rowversion NOT NULL;

CREATE TABLE dbo.OrderStatusHistory (
    Id bigint IDENTITY PRIMARY KEY,
    OrderId int NOT NULL REFERENCES dbo.Orders(Id),
    FromStatus nvarchar(20) NULL,
    ToStatus nvarchar(20) NOT NULL,
    ActorUserId int NULL REFERENCES dbo.Users(Id),
    Reason nvarchar(300) NOT NULL CONSTRAINT DF_OrderStatusHistory_Reason DEFAULT N'',
    ChangedAt datetime2 NOT NULL CONSTRAINT DF_OrderStatusHistory_ChangedAt DEFAULT SYSUTCDATETIME()
);

CREATE INDEX IX_OrderStatusHistory_Order_ChangedAt
ON dbo.OrderStatusHistory(OrderId, ChangedAt, Id);

INSERT INTO dbo.OrderStatusHistory(OrderId, FromStatus, ToStatus, ActorUserId, Reason, ChangedAt)
SELECT Id, NULL, Status, NULL, N'Eski siparişten taşınan başlangıç durumu.', CreatedAt
FROM dbo.Orders;
