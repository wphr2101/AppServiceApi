# Logistics Help: local implementation

This is a read-only, non-modal help dialog within the Logistics workspace.
It does not look up users, orders, stock, passwords or administrator identities.
It cannot register, sign in, save customer details, change a cart or place orders.
Links only navigate to existing application pages; users perform actions themselves.

## Honest modes

- Guide: AI is disabled, unconfigured, busy or unavailable; approved guide text is
  selected using local keyword matching. This is not represented as an AI answer.
- AI topic match: Azure OpenAI selected an allowlisted guide topic. All instructions
  and links still come from server-owned content, not generated instructions.
- Policy: administrator questions always return exactly:
  `This must get approval from Mr. Richard Wu`

The public POST /api/help requires CSRF and accepts only a 2-500 character question.
Six requests per IP per minute, no queue; AI concurrency is two per API process,
with an eight-second timeout and 32 KB maximum response body. These are per-instance
limits, not a distributed spend cap. Configure Azure quotas/budgets and a gateway
limit before enabling paid AI publicly, especially with multiple app instances.

No transcript is saved. No questions or provider response bodies are logged by
this feature. The browser warns against personal data; common emails, phone numbers
and credential patterns are rejected before AI. This is not comprehensive PII
detection. Never paste real passwords, access tokens, addresses or customer data.

## Azure OpenAI (off by default)

Set server-side configuration using .NET User Secrets for local development or
Azure App Service settings / Key Vault for deployment. Do not put keys in Angular,
Git, chat messages, screenshots or shell history.

- LogisticsAi__Enabled = true
- LogisticsAi__Endpoint = https://YOUR-RESOURCE.openai.azure.com/
- LogisticsAi__Deployment = YOUR-DEPLOYMENT-NAME
- LogisticsAi__ApiKey = the secret value (enter privately in your secret manager)

The adapter uses POST /openai/v1/chat/completions with JSON-object output and
max_completion_tokens=128. Use a low-latency chat deployment supporting those
parameters. Unsupported deployment/configuration/provider failures fall back to
Guide mode, without exposing provider errors or secrets to the browser.

Only the question and a topic-classification instruction are sent to Azure OpenAI.
Cookies, customer profiles, orders, chat history and application configuration are
not part of that request. Provider-returned prose and URLs are never displayed.
Outbound redirects are disabled and endpoints must be Azure HTTPS resource origins.

## Verification

Frontend: npm test -- --watch=false; npm run build -- --configuration production
API: dotnet test Tests/CustomerOperations.Api.Tests.csproj --configuration Release

Automated provider tests use an in-memory HTTP handler, not a paid AI endpoint.
A live AI request still requires an authorized resource, secret configuration and
an explicit smoke check. Do not call the fallback a completed live AI integration.

## Release considerations

No migration or database change is required. No live deployment is part of this
change. The current Azure pipeline pins the API commit: after reviewing and
committing the backend, update that pin deliberately before assembling a release.
Do not deploy a frontend whose /api/help endpoint is absent from the backend.
The v1 provider API is documented at:
https://learn.microsoft.com/azure/ai-foundry/openai/how-to/chatgpt
