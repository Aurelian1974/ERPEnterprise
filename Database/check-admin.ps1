$server='TS1828\ERP'
$db='ValyanERP'
Write-Host "Querying $db on $server..."
Invoke-Sqlcmd -ServerInstance $server -Database $db -Query "SELECT Id, UserName, NormalizedUserName FROM dbo.Users WHERE NormalizedUserName = 'ADMIN@VALYANERP.RO';"
Write-Host "Querying Persoane for admin email..."
Invoke-Sqlcmd -ServerInstance $server -Database $db -Query "SELECT Id, Nume, Prenume, Email FROM dbo.Persoane WHERE Email = 'admin@valyanerp.ro';"