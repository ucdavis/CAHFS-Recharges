/*
  Equine (gpare) — SQL Agent job: daily AE feed load

  Prerequisite: dbo.C_Load_AE_Feed_Tables_Weekly_V2 deployed (004_...Daily.sql)

  Schedule: Daily 1:15 AM Pacific (staggered after CAHFS 1:00 AM)
  Step:     EXEC dbo.C_Load_AE_Feed_Tables_Weekly_V2;  -- current month-slice only

  Run this script while connected to the gpare SQL instance.
  Review job owner / notify operators before enabling in Prod.
*/

USE [msdb];
GO

DECLARE @jobId        uniqueidentifier;
DECLARE @scheduleId   int;
DECLARE @jobName      sysname = N'CAEI_Equine_AE_Feed_Daily';
DECLARE @scheduleName sysname = N'CAEI_Equine_AE_Feed_Daily_0115_PT';

IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = @jobName)
BEGIN
    EXEC msdb.dbo.sp_delete_job @job_name = @jobName, @delete_unused_schedule = 1;
END;

EXEC msdb.dbo.sp_add_job
    @job_name                = @jobName,
    @enabled                 = 1,
    @description             = N'Daily Equine Aggie Enterprise feed load: EXEC gpare.dbo.C_Load_AE_Feed_Tables_Weekly_V2 (month-slice find-or-create; VHFS501). Run before Hangfire DailyCoaValidation (~2 AM PT).',
    @category_name           = N'[Uncategorized (Local)]',
    @owner_login_name        = N'sa',   -- CHANGE to dedicated Agent proxy / ops login in Prod
    @notify_level_eventlog   = 2,
    @notify_level_email      = 0,
    @job_id                  = @jobId OUTPUT;

EXEC msdb.dbo.sp_add_jobstep
    @job_id               = @jobId,
    @step_name            = N'Exec_C_Load_AE_Feed_Tables_Weekly_V2',
    @step_id              = 1,
    @cmdexec_success_code = 0,
    @on_success_action    = 1,
    @on_fail_action       = 2,
    @retry_attempts       = 1,
    @retry_interval       = 5,
    @subsystem            = N'TSQL',
    @database_name        = N'gpare',
    @command              = N'SET NOCOUNT ON;
SET XACT_ABORT ON;
EXEC dbo.C_Load_AE_Feed_Tables_Weekly_V2;';

EXEC msdb.dbo.sp_update_job
    @job_id        = @jobId,
    @start_step_id = 1;

EXEC msdb.dbo.sp_add_jobschedule
    @job_id               = @jobId,
    @name                 = @scheduleName,
    @enabled              = 1,
    @freq_type            = 4,
    @freq_interval        = 1,
    @freq_subday_type     = 1,
    @freq_subday_interval = 0,
    @active_start_date    = 20260501,
    @active_end_date      = 99991231,
    @active_start_time    = 11500,     -- 01:15:00
    @active_end_time      = 235959,
    @schedule_id          = @scheduleId OUTPUT;

EXEC msdb.dbo.sp_add_jobserver
    @job_id      = @jobId,
    @server_name = N'(LOCAL)';
GO

/*
  Verify:
    SELECT job_id, name, enabled FROM msdb.dbo.sysjobs WHERE name = N'CAEI_Equine_AE_Feed_Daily';
    EXEC msdb.dbo.sp_start_job @job_name = N'CAEI_Equine_AE_Feed_Daily';
*/
