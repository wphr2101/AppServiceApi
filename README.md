# CustomerOperations.Api

ASP.NET Core 8 API for the Customer Operations portfolio and Logistics demo.
The Angular source is in https://github.com/wphr2101/myAngular.

## Features

- Nations and visitor comments APIs.
- Product catalog, customer registration and cookie sign-in.
- Customer-owned orders, paginated history and saved purchase prices.
- Admin inventory, restock audit, simulated payment and shipping.
- Transactional stock checks and deduction, checkout/restock retry protection,
  CSRF, role checks, and database role revalidation for admin operations.

Public registration always creates a Customer. An existing registered account
must be explicitly promoted in the database to become an Admin. Passwords are
hashed; API keys are not user authentication.

## Local configuration

Copy appsettings.example.json to appsettings.json and configure DefaultConnection
using local settings or .NET user secrets. No shared browser API key is used. Real settings are
ignored by Git and excluded from publishing.

    dotnet run --project CustomerOperations.Api.csproj --launch-profile http

The local HTTP profile uses ports 5001 and 7001. Swagger is Development-only.
The frontend development server uses port 4200. Production serves Angular and
the API from the same HTTPS origin.

## Tests and database

    dotnet test Tests/CustomerOperations.Api.Tests.csproj -c Release
    dotnet publish CustomerOperations.Api.csproj -c Release -o ./publish

Database scripts are versioned under Database. Run them against the intended
development/demo database in order; application deployment does not execute them.
Scripts 001-005 have already been applied to the current nations-db demo database.
Do not rerun table creation on an existing database or seed over existing data.

New orders deduct stock in the same transaction as order creation. Existing
orders from before inventory migration deduct stock once at simulated shipment.
Initial stock is zero until an Admin records a restock. Payment and shipping are
demo status changes; no payment processor or delivery provider is invoked.
Cancellation/refund and durable recovery for an unconfirmed restock after a
browser reload are not implemented in this increment.

## Release

The Azure Pipeline in myAngular tests both repositories, builds Angular, publishes
this API, combines the output and publishes a versioned deploy artifact. Its API
commit is pinned in that pipeline. Generated wwwroot files are not tracked here.

The deployment package contains a non-secret client-config.js with a same-origin
baseUrl only. No shared API key is sent to the browser or required by the API.
Public routes are explicitly anonymous; all other controllers require a session.
Customer ownership and Admin roles are checked on the server, with HttpOnly
Secure cookies and CSRF on writes. Visitor comments expose name and comment only;
contact details are not returned. Login and comment submissions are rate limited.
Never put SQL connection strings, publish profiles or passwords in client files.

Keep Azure App Service connection strings and settings in Azure. Publish output
does not include appsettings files. IIS web.config must reference
CustomerOperations.Api.dll after this project's rename.
