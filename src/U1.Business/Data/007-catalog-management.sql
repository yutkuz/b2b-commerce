ALTER TABLE dbo.Categories
ADD RowVersion rowversion NOT NULL;

ALTER TABLE dbo.Products
ADD IsArchived bit NOT NULL
        CONSTRAINT DF_Products_IsArchived DEFAULT 0,
    ArchivedAt datetime2 NULL,
    ArchivedByUserId int NULL,
    ArchiveReason nvarchar(300) NOT NULL
        CONSTRAINT DF_Products_ArchiveReason DEFAULT N'';

ALTER TABLE dbo.Products
ADD CONSTRAINT FK_Products_ArchivedByUserId
    FOREIGN KEY (ArchivedByUserId) REFERENCES dbo.Users(Id);

CREATE INDEX IX_Products_Archive_Category_Brand
ON dbo.Products(IsArchived, CategoryId, Brand)
INCLUDE(Price, Stock);
