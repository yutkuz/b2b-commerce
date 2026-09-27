IF OBJECT_ID('dbo.Users') IS NULL
BEGIN
CREATE TABLE dbo.Users (
 Id int IDENTITY PRIMARY KEY, FirstName nvarchar(80) NOT NULL, LastName nvarchar(80) NOT NULL,
 Email nvarchar(200) NOT NULL UNIQUE, Phone nvarchar(25) NOT NULL, Company nvarchar(180) NOT NULL DEFAULT '',
 PasswordHash nvarchar(500) NOT NULL, Role varchar(12) NOT NULL DEFAULT 'Dealer' CHECK(Role IN ('Admin','Dealer')),
 IsActive bit NOT NULL DEFAULT 1, AuthVersion int NOT NULL DEFAULT 1, CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
CREATE TABLE dbo.Categories (Id int IDENTITY PRIMARY KEY, Name nvarchar(80) NOT NULL UNIQUE);
CREATE TABLE dbo.Products (
 Id int IDENTITY PRIMARY KEY, Code nvarchar(60) NOT NULL UNIQUE, Name nvarchar(180) NOT NULL,
 Description nvarchar(3000) NOT NULL, Brand nvarchar(80) NOT NULL, ManufacturerCode nvarchar(80) NOT NULL,
 SpecialCode1 nvarchar(80) NOT NULL DEFAULT '', SpecialCode2 nvarchar(80) NOT NULL DEFAULT '', ImageUrl nvarchar(500) NOT NULL,
 Stock int NOT NULL CHECK(Stock>=0), CriticalStock int NOT NULL DEFAULT 5 CHECK(CriticalStock>=0),
 Price decimal(18,2) NOT NULL CHECK(Price>0), CategoryId int NOT NULL REFERENCES dbo.Categories(Id),
 CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
CREATE INDEX IX_Products_Category_Brand ON dbo.Products(CategoryId,Brand) INCLUDE(Price,Stock);
CREATE TABLE dbo.Carts (Id int IDENTITY PRIMARY KEY, UserId int NOT NULL UNIQUE REFERENCES dbo.Users(Id));
CREATE TABLE dbo.CartItems (CartId int NOT NULL REFERENCES dbo.Carts(Id), ProductId int NOT NULL REFERENCES dbo.Products(Id),
 Quantity int NOT NULL CHECK(Quantity BETWEEN 1 AND 1000000), PRIMARY KEY(CartId, ProductId));
CREATE TABLE dbo.Orders (
 Id int IDENTITY PRIMARY KEY, Number varchar(40) NOT NULL UNIQUE, UserId int NOT NULL REFERENCES dbo.Users(Id),
 CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(), Status nvarchar(20) NOT NULL DEFAULT N'Bekliyor'
 CHECK(Status IN (N'Bekliyor',N'Onaylandı',N'Reddedildi')),
 Total decimal(18,2) NOT NULL CHECK(Total>=0), RequestId uniqueidentifier NOT NULL UNIQUE, Note nvarchar(1000) NOT NULL DEFAULT '');
CREATE INDEX IX_Orders_User_Date ON dbo.Orders(UserId,CreatedAt DESC);
CREATE TABLE dbo.OrderItems (
 Id int IDENTITY PRIMARY KEY, OrderId int NOT NULL REFERENCES dbo.Orders(Id), ProductId int NOT NULL REFERENCES dbo.Products(Id),
 ProductCode nvarchar(60) NOT NULL, ProductName nvarchar(180) NOT NULL, Quantity int NOT NULL CHECK(Quantity>0),
 UnitPrice decimal(18,2) NOT NULL, Total decimal(18,2) NOT NULL);
CREATE INDEX IX_OrderItems_Order ON dbo.OrderItems(OrderId);
CREATE TABLE dbo.GridColumns (
 Id int IDENTITY PRIMARY KEY, Field varchar(50) NOT NULL UNIQUE, Label nvarchar(60) NOT NULL,
 RenderType varchar(20) NOT NULL, Position int NOT NULL, Width int NOT NULL CHECK(Width BETWEEN 60 AND 600),
 Align varchar(10) NOT NULL, Desktop bit NOT NULL, Tablet bit NOT NULL, Mobile bit NOT NULL);
CREATE TABLE dbo.Banners (
 Id int IDENTITY PRIMARY KEY, Title nvarchar(100) NOT NULL, Subtitle nvarchar(300) NOT NULL,
 ButtonText nvarchar(40) NOT NULL, SearchTerm nvarchar(100) NOT NULL DEFAULT '', IsActive bit NOT NULL, Position int NOT NULL);
CREATE TABLE dbo.SchemaVersions (Version int PRIMARY KEY, AppliedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
INSERT INTO dbo.SchemaVersions(Version) VALUES(1);
END;
