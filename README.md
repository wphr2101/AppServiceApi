# NationsApi

ASP.NET Core Web API for reading nation data from a SQL Server `nations` table.

## Run locally

1. Copy `appsettings.example.json` to `appsettings.json`.
2. Replace the `DefaultConnection` value with your SQL Server connection string.
3. Run:

```powershell
dotnet restore
dotnet run
```

The nations endpoint is:

```text
/api/nations
```

## Database table

The API expects this SQL Server table:

```sql
CREATE TABLE dbo.nations (
    [Name] varchar(50),
    Capital varchar(50),
    FlagImage varchar(150),
    MapImage varchar(300),
    Pupulation int,
    GDP real,
    HDI real
);
```
