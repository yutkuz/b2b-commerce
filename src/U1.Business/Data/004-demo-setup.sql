CREATE TABLE dbo.DemoSetup (
 Component varchar(16) NOT NULL PRIMARY KEY,
 Status varchar(16) NOT NULL CHECK(Status IN ('seeded','existing')),
 CompletedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);
