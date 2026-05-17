-- Run this script once to initialize all module schemas
-- Server: Valeria, Database: ERPEnterprise, Windows Authentication

USE ERPEnterprise;
GO

-- Module schemas
CREATE SCHEMA finance;
GO
CREATE SCHEMA hr;
GO
CREATE SCHEMA inventory;
GO
CREATE SCHEMA purchasing;
GO
CREATE SCHEMA sales;
GO
CREATE SCHEMA administration;
GO
CREATE SCHEMA audit;
GO
CREATE SCHEMA hangfire;
GO

PRINT 'All schemas created successfully.';
