CREATE TABLE dbo.DealerGroups (
    Id int IDENTITY PRIMARY KEY,
    Name nvarchar(80) NOT NULL UNIQUE,
    DiscountPercent decimal(5,2) NOT NULL
        CONSTRAINT CK_DealerGroups_Discount CHECK (DiscountPercent BETWEEN 0 AND 100),
    RowVersion rowversion NOT NULL
);

SET IDENTITY_INSERT dbo.DealerGroups ON;
INSERT INTO dbo.DealerGroups(Id, Name, DiscountPercent)
VALUES(1, N'Varsayılan', 0);
SET IDENTITY_INSERT dbo.DealerGroups OFF;

ALTER TABLE dbo.Users
ADD DealerGroupId int NULL;

ALTER TABLE dbo.Users
ADD CONSTRAINT FK_Users_DealerGroups
FOREIGN KEY(DealerGroupId) REFERENCES dbo.DealerGroups(Id);

UPDATE dbo.Users
SET DealerGroupId = 1
WHERE Role = 'Dealer' AND DealerGroupId IS NULL;

ALTER TABLE dbo.OrderItems
ADD ListUnitPrice decimal(18,2) NOT NULL
        CONSTRAINT DF_OrderItems_ListUnitPrice DEFAULT 0,
    DiscountPercent decimal(5,2) NOT NULL
        CONSTRAINT DF_OrderItems_DiscountPercent DEFAULT 0;

UPDATE dbo.OrderItems
SET ListUnitPrice = UnitPrice;
