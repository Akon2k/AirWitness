SELECT 'RadioStations' as TableName, count(*) as RecordCount FROM "RadioStations"
UNION ALL
SELECT 'MonitoringSchedules', count(*) FROM "MonitoringSchedules"
UNION ALL
SELECT 'MatchRecords', count(*) FROM "MatchRecords"
UNION ALL
SELECT 'MasterAudios', count(*) FROM "MasterAudios";

SELECT "Id", "Name", "StreamUrl", "DefaultMasterPath" FROM "RadioStations" ORDER BY "Id";

SELECT "Id", "RadioStationId", "StartTime", "EndTime", "IsActive" FROM "MonitoringSchedules" ORDER BY "RadioStationId", "StartTime";
