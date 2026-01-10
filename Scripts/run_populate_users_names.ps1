$cs = 'Server=TS1828\ERP;Database=ValyanERP;Integrated Security=True;TrustServerCertificate=True'
$sql = Get-Content 'd:\Projects\ERPEnterprise\Database\Scripts\005_PopulateUsersNames.sql' -Raw
$cn = New-Object System.Data.SqlClient.SqlConnection $cs
$cn.Open()
$cmd = $cn.CreateCommand()
$cmd.CommandText = $sql
$cmd.CommandTimeout = 120
$affected = $cmd.ExecuteNonQuery()
Write-Host "Rows affected: $affected"
$cmd.CommandText = 'SELECT TOP 20 Email, FirstName, LastName, PersoanaId FROM dbo.Users ORDER BY CreatedAt DESC'
$reader = $cmd.ExecuteReader()
$table = New-Object System.Data.DataTable
$table.Load($reader)
$table | Format-Table -AutoSize
$cn.Close()
