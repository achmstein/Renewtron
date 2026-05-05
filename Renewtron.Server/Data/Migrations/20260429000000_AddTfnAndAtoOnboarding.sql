-- Idempotent script for migration 20260429000000_AddTfnAndAtoOnboarding.
-- Safe to run multiple times.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[Leads]') AND [name] = 'Tfn')
    ALTER TABLE [Leads] ADD [Tfn] nvarchar(20) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[RenewalRequests]') AND [name] = 'Tfn')
    ALTER TABLE [RenewalRequests] ADD [Tfn] nvarchar(20) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[RenewalRequests]') AND [name] = 'AtoOnboardingJobId')
    ALTER TABLE [RenewalRequests] ADD [AtoOnboardingJobId] nvarchar(50) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[RenewalRequests]') AND [name] = 'AtoOnboardingStatus')
    ALTER TABLE [RenewalRequests] ADD [AtoOnboardingStatus] nvarchar(20) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[RenewalRequests]') AND [name] = 'AtoOnboardingResultJson')
    ALTER TABLE [RenewalRequests] ADD [AtoOnboardingResultJson] nvarchar(max) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[RenewalRequests]') AND [name] = 'AtoOnboardingCompletedAt')
    ALTER TABLE [RenewalRequests] ADD [AtoOnboardingCompletedAt] datetime2 NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20260429000000_AddTfnAndAtoOnboarding')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES ('20260429000000_AddTfnAndAtoOnboarding', '10.0.0-rc.2.25502.107');
GO
