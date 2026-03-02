-- Create database if not exists
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'observabilityDB')
BEGIN
    CREATE DATABASE observabilityDB;
END
GO

USE observabilityDB;
GO

-- Create login for application user 
IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = 'khaled')
BEGIN
    CREATE LOGIN khaled WITH PASSWORD = 'StrongPassword123!!';
END
GO

-- Create user for the login and grant permissions
IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = 'khaled')
BEGIN
    CREATE USER khaled FOR LOGIN khaled;
    ALTER ROLE db_owner ADD MEMBER khaled;
END
GO

PRINT 'Database initialization completed successfully';
GO